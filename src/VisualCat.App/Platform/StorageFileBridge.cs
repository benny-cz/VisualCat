using Avalonia.Platform.Storage;

namespace VisualCat.App.Platform;

internal enum StorageWritePublication : byte
{
    LocalCommitted,
    ProviderDeliveryStarted,
    ProviderFinalizing,
    ProviderDeliveryCompleted,
}

/// <summary>
/// Bridges provider-backed files (for example Android SAF <c>content://</c> documents)
/// to the path-based application services without assuming that a picker result is a
/// directly accessible filesystem path.
/// </summary>
internal static class StorageFileBridge
{
    private const int CopyBufferBytes = 1024 * 1024;

    /// <summary>Whether writing this picker result publishes directly to a local path.</summary>
    internal static bool UsesDirectLocalPath(IStorageFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return file.TryGetLocalPath() is { Length: > 0 } localPath &&
               !localPath.StartsWith("content:", StringComparison.OrdinalIgnoreCase);
    }

    public static Task<MaterializedStorageFile> MaterializeForReadAsync(
        IStorageFile file,
        CancellationToken cancellationToken = default) =>
        MaterializeForReadAsync(file, progress: null, cancellationToken);

    /// <summary>Materializes a picker result, reporting copied bytes for a provider file.</summary>
    public static async Task<MaterializedStorageFile> MaterializeForReadAsync(
        IStorageFile file,
        IProgress<VisualCat.Application.UseCases.FileWorkProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.TryGetLocalPath() is { Length: > 0 } localPath && File.Exists(localPath))
        {
            return new MaterializedStorageFile(Path.GetFullPath(localPath), IsTemporary: false);
        }

        // The provider's stated size when it states one: a document that will not say how
        // large it is is reported as indeterminate rather than as a guess.
        long? declared = null;
        try
        {
            declared = (await file.GetBasicPropertiesAsync().ConfigureAwait(false)).Size is { } size and > 0
                ? (long)size
                : null;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or UnauthorizedAccessException)
        {
        }

        await using var input = await file.OpenReadAsync().ConfigureAwait(false);
        return await CopyToTemporaryAsync(
            input,
            file.Name,
            root: null,
            progress,
            declared,
            cancellationToken).ConfigureAwait(false);
    }

    public static Task WriteAsync(
        IStorageFile file,
        Func<string, CancellationToken, Task> producer,
        CancellationToken cancellationToken = default) =>
        WriteAsync(file, producer, progress: null, publication: null, cancellationToken);

    public static async Task WriteAsync(
        IStorageFile file,
        Func<string, CancellationToken, Task> producer,
        IProgress<VisualCat.Application.UseCases.FileWorkProgress>? progress,
        Action<StorageWritePublication>? publication,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(producer);
        if (UsesDirectLocalPath(file) && file.TryGetLocalPath() is { Length: > 0 } localPath)
        {
            await producer(Path.GetFullPath(localPath), cancellationToken).ConfigureAwait(false);
            publication?.Invoke(StorageWritePublication.LocalCommitted);
            return;
        }

        await WriteToProviderAsync(
            file.Name,
            producer,
            file.OpenWriteAsync,
            progress,
            publication,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Produces into an owned local stage, then delivers it to a provider stream whose
    /// cancellation behavior is controlled by the platform rather than by this application.
    /// </summary>
    internal static async Task WriteToProviderAsync(
        string? proposedName,
        Func<string, CancellationToken, Task> producer,
        Func<Task<Stream>> openWriteAsync,
        IProgress<VisualCat.Application.UseCases.FileWorkProgress>? progress,
        Action<StorageWritePublication>? publication,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(openWriteAsync);
        var temporary = CreateTemporaryPath(proposedName, "Outgoing");
        try
        {
            await producer(temporary, cancellationToken).ConfigureAwait(false);
            publication?.Invoke(StorageWritePublication.ProviderDeliveryStarted);
            await using (var output = await openWriteAsync().ConfigureAwait(false))
            {
                var total = new FileInfo(temporary).Length;
                await CopyFileToStreamAsync(temporary, output, progress, total, cancellationToken)
                    .ConfigureAwait(false);
                publication?.Invoke(StorageWritePublication.ProviderFinalizing);
            }

            publication?.Invoke(StorageWritePublication.ProviderDeliveryCompleted);
        }
        finally
        {
            TryDeleteFile(temporary);
        }
    }

    internal static async Task<MaterializedStorageFile> CopyToTemporaryAsync(
        Stream input,
        string? proposedName,
        string? root = null,
        IProgress<VisualCat.Application.UseCases.FileWorkProgress>? progress = null,
        long? knownLength = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var directory = root is null
            ? StorageDirectory("Incoming")
            : Path.GetFullPath(root);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"{Guid.NewGuid():N}-{SafeFileName(proposedName)}");
        try
        {
            await using var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (progress is null)
            {
                await input.CopyToAsync(output, CopyBufferBytes, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var buffer = new byte[CopyBufferBytes];
                long copied = 0;
                progress.Report(new VisualCat.Application.UseCases.FileWorkProgress(
                    VisualCat.Application.UseCases.FileWorkStage.Copying,
                    0,
                    knownLength,
                    "bytes"));
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
                    progress.Report(new VisualCat.Application.UseCases.FileWorkProgress(
                        VisualCat.Application.UseCases.FileWorkStage.Copying,
                        copied,
                        knownLength,
                        "bytes"));
                }
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            return new MaterializedStorageFile(destination, IsTemporary: true);
        }
        catch
        {
            TryDeleteFile(destination);
            throw;
        }
    }

    internal static Task CopyFileToStreamAsync(
        string sourcePath,
        Stream output,
        CancellationToken cancellationToken = default) =>
        CopyFileToStreamAsync(sourcePath, output, progress: null, knownLength: null, cancellationToken);

    internal static async Task CopyFileToStreamAsync(
        string sourcePath,
        Stream output,
        IProgress<VisualCat.Application.UseCases.FileWorkProgress>? progress,
        long? knownLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(output);
        if (output.CanSeek)
        {
            output.Position = 0;
            output.SetLength(0);
        }

        await using var input = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var total = knownLength ?? input.Length;
        var buffer = new byte[CopyBufferBytes];
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
            progress?.Report(new VisualCat.Application.UseCases.FileWorkProgress(
                VisualCat.Application.UseCases.FileWorkStage.SavingToProvider,
                copied,
                total,
                "bytes"));
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string CreateTemporaryPath(string? proposedName, string area)
    {
        var directory = StorageDirectory(area);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}-{SafeFileName(proposedName)}");
    }

    private static string StorageDirectory(string area) =>
        Path.Combine(Path.GetTempPath(), "VisualCat", "Storage", area);

    private static string SafeFileName(string? proposedName)
    {
        var name = Path.GetFileName(proposedName);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "document.bin";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(name.Select(character =>
            invalid.Contains(character) || character is '/' or '\\' ? '_' : character));
        return string.IsNullOrWhiteSpace(safe) ? "document.bin" : safe;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal readonly record struct MaterializedStorageFile(string Path, bool IsTemporary)
{
    public void DeleteIfTemporary()
    {
        if (!IsTemporary)
        {
            return;
        }

        try
        {
            File.Delete(Path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
