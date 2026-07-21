using Avalonia.Platform.Storage;

namespace VisualCat.App.Platform;

/// <summary>
/// Bridges provider-backed files (for example Android SAF <c>content://</c> documents)
/// to the path-based application services without assuming that a picker result is a
/// directly accessible filesystem path.
/// </summary>
internal static class StorageFileBridge
{
    private const int CopyBufferBytes = 1024 * 1024;

    public static async Task<MaterializedStorageFile> MaterializeForReadAsync(
        IStorageFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.TryGetLocalPath() is { Length: > 0 } localPath && File.Exists(localPath))
        {
            return new MaterializedStorageFile(Path.GetFullPath(localPath), IsTemporary: false);
        }

        await using var input = await file.OpenReadAsync().ConfigureAwait(false);
        return await CopyToTemporaryAsync(input, file.Name, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteAsync(
        IStorageFile file,
        Func<string, CancellationToken, Task> producer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(producer);
        if (file.TryGetLocalPath() is { Length: > 0 } localPath &&
            !localPath.StartsWith("content:", StringComparison.OrdinalIgnoreCase))
        {
            await producer(Path.GetFullPath(localPath), cancellationToken).ConfigureAwait(false);
            return;
        }

        var temporary = CreateTemporaryPath(file.Name, "Outgoing");
        try
        {
            await producer(temporary, cancellationToken).ConfigureAwait(false);
            await using var output = await file.OpenWriteAsync().ConfigureAwait(false);
            await CopyFileToStreamAsync(temporary, output, cancellationToken).ConfigureAwait(false);
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
            await input.CopyToAsync(output, CopyBufferBytes, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            return new MaterializedStorageFile(destination, IsTemporary: true);
        }
        catch
        {
            TryDeleteFile(destination);
            throw;
        }
    }

    internal static async Task CopyFileToStreamAsync(
        string sourcePath,
        Stream output,
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
        await input.CopyToAsync(output, CopyBufferBytes, cancellationToken).ConfigureAwait(false);
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
