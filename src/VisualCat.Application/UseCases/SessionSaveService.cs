using System.Text.Json;
using VisualCat.Core.Store;

namespace VisualCat.Application.UseCases;

public static class SessionSaveService
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static Task SaveAsync(
        SessionSnapshot snapshot,
        string destination,
        bool portable,
        CancellationToken cancellationToken = default) =>
        SaveAsync(snapshot, destination, portable, progress: null, cancellationToken);

    public static async Task SaveAsync(
        SessionSnapshot snapshot,
        string destination,
        bool portable,
        IProgress<FileWorkProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var sourceRoot = Path.GetFullPath(snapshot.RootPath);
        var destinationRoot = Path.GetFullPath(destination);
        using var sourceUsage = SessionAccess.ReadForWork(sourceRoot);
        using var destinationUsage = SessionAccess.Write(destinationRoot);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (Directory.Exists(destinationRoot) || File.Exists(destinationRoot))
        {
            throw new IOException($"Destination already exists: {destinationRoot}");
        }

        var sourcePrefix = sourceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                           Path.DirectorySeparatorChar;
        if (destinationRoot.Equals(sourceRoot, comparison) || destinationRoot.StartsWith(sourcePrefix, comparison))
        {
            throw new IOException("The destination cannot be inside the source session.");
        }

        if (EnclosingSession(destinationRoot) is { } enclosing)
        {
            throw new IOException(
                $"That location is inside the session '{Path.GetFileName(enclosing)}'. Choose a directory outside it. " +
                "A session is a directory, so a save panel will navigate into one, and a session nested inside " +
                "another is carried along by the outer session's cache retention without its manifest describing it.");
        }

        var parent = Path.GetDirectoryName(destinationRoot) ?? ".";
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, $".{Path.GetFileName(destinationRoot)}.tmp-{Guid.NewGuid():N}");
        SessionFileModes.CreateOwnerOnlyDirectory(temporary);
        try
        {
            await CopyDirectoryAsync(sourceRoot, temporary, progress, cancellationToken).ConfigureAwait(false);
            var manifest = snapshot.Manifest with { UpdatedUtc = DateTimeOffset.UtcNow };
            if (portable)
            {
                if (snapshot.RawPath is null)
                {
                    throw new InvalidOperationException("A portable session cannot be created because raw source data is unavailable.");
                }

                var rawDestination = Path.Combine(temporary, "raw.log");
                if (!File.Exists(rawDestination))
                {
                    // Embed exactly the bytes this session indexed, verified on the handle the
                    // copy reads from (ADR 0020). An external capture that has been appended to
                    // since the import still holds this session's evidence in its recorded
                    // prefix; copying whatever the file happens to hold now would embed bytes
                    // the session never saw and then fail its own verification below.
                    await using var raw = await VerifiedRawSource.OpenAsync(snapshot, cancellationToken)
                        .ConfigureAwait(false);
                    await CopyPrefixAsync(raw, rawDestination, progress, cancellationToken).ConfigureAwait(false);
                }

                // Embedded raw evidence is verbatim log content that travels with the session
                // wherever it is saved, so it is owner-only whatever the account's umask says
                // (finding F-27).
                SessionFileModes.MakeFileOwnerOnly(rawDestination);

                manifest = manifest with
                {
                    Source = manifest.Source with { Path = null, Embedded = true },
                };
            }

            await WriteManifestAsync(temporary, manifest, cancellationToken).ConfigureAwait(false);
            var verifyRaw = portable || snapshot.RawPath is { } sourcePath && File.Exists(sourcePath);
            progress?.Report(new FileWorkProgress(FileWorkStage.Verifying));
            var report = await SessionVerifier.VerifyAsync(temporary, verifyRaw, cancellationToken).ConfigureAwait(false);
            if (!report.IsValid)
            {
                throw new InvalidDataException(
                    $"Saved session verification failed: {string.Join("; ", report.Issues.Select(static issue => issue.Message))}");
            }

            progress?.Report(new FileWorkProgress(FileWorkStage.Publishing));
            await FileSystemPublish.MoveDirectoryAsync(temporary, destinationRoot, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, true);
            }

            throw;
        }
    }

    /// <summary>
    /// The nearest ancestor of <paramref name="destination"/> that is itself a session, or null.
    /// </summary>
    /// <remarks>
    /// A session is a directory, so every platform's save panel navigates into one as readily as
    /// into any folder — and during the macOS run a Save portable landed at
    /// <c>cli-capture.vcat/ADB RFCRC0A9GND …-portable-….vcat</c>. Both sessions still verified,
    /// so nothing was corrupted; what the outer session gained was seven megabytes its own
    /// manifest does not describe, and a later cache-retention sweep of the outer session would
    /// take the inner one with it (finding F-13). The guard is cheap, needs no platform work,
    /// and is equally right on Windows and Linux.
    /// </remarks>
    private static string? EnclosingSession(string destination)
    {
        // Bounded rather than walking to the filesystem root: a session is never nested more
        // than a handful of levels below one, and an unbounded walk on a deep path costs a
        // stat per level on every save.
        const int MaximumAncestors = 24;
        var directory = Path.GetDirectoryName(destination);
        for (var depth = 0; depth < MaximumAncestors && !string.IsNullOrEmpty(directory); depth++)
        {
            try
            {
                if (File.Exists(Path.Combine(directory, "manifest.json")) &&
                    Directory.Exists(Path.Combine(directory, "segments")))
                {
                    return directory;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // An ancestor this account cannot inspect is not evidence of a session, and is
                // certainly not a reason to refuse a save.
                return null;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    private static async Task CopyDirectoryAsync(
        string source,
        string destination,
        IProgress<FileWorkProgress>? progress,
        CancellationToken cancellationToken)
    {
        var sourcePrefix = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                           Path.DirectorySeparatorChar;
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLink(directory);
            var relative = Path.GetFullPath(directory)[sourcePrefix.Length..];
            SessionFileModes.CreateOwnerOnlyDirectory(Path.Combine(destination, relative));
        }

        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var relative = Path.GetFullPath(path)[sourcePrefix.Length..];
                return relative != ".capture-identity" &&
                       !relative.StartsWith(".capture-identity.", StringComparison.Ordinal);
            })
            .ToArray();
        var total = files.Sum(static path => new FileInfo(path).Length);
        long copied = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLink(file);
            var relative = Path.GetFullPath(file)[sourcePrefix.Length..];
            // Storage identity belongs to the original directory. Saved/imported copies must
            // receive a fresh identity even on filesystems without a stable birth time.
            await CopyFileAsync(
                file,
                Path.Combine(destination, relative),
                bytes =>
                {
                    progress?.Report(new FileWorkProgress(FileWorkStage.Copying, copied + bytes, total, "bytes"));
                },
                cancellationToken).ConfigureAwait(false);
            copied += new FileInfo(file).Length;
        }
    }

    private static void RejectLink(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException($"Session save refuses symbolic links and reparse points: {path}");
        }
    }

    private static async Task CopyPrefixAsync(
        VerifiedRawSource raw,
        string destination,
        IProgress<FileWorkProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? ".");
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        progress?.Report(new FileWorkProgress(FileWorkStage.Copying));
        await raw.CopyPrefixToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyFileAsync(
        string source,
        string destination,
        Action<long> report,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? ".");
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[1024 * 1024];
        long copied = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            copied += read;
            report(copied);
        }
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteManifestAsync(
        string root,
        SessionManifest manifest,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, "manifest.json");
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(
                         temporary,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         64 * 1024,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, manifest, Options, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        await FileSystemPublish.MoveFileAsync(temporary, path, overwrite: true, cancellationToken)
            .ConfigureAwait(false);
    }
}
