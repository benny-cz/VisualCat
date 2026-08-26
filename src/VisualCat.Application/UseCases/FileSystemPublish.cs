namespace VisualCat.Application.UseCases;

/// <summary>
/// Publishes completed files and directories while tolerating the short sharing violations
/// caused by Windows search indexers and real-time antivirus scanners.
/// </summary>
internal static class FileSystemPublish
{
    private const int Attempts = 17;

    public static Task MoveFileAsync(
        string source,
        string destination,
        bool overwrite,
        CancellationToken cancellationToken) =>
        RetryAsync(() => File.Move(source, destination, overwrite), cancellationToken);

    public static Task MoveDirectoryAsync(
        string source,
        string destination,
        CancellationToken cancellationToken) =>
        RetryAsync(() => Directory.Move(source, destination), cancellationToken);

    private static async Task RetryAsync(Action publish, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                publish();
                return;
            }
            catch (Exception exception) when (
                attempt < Attempts && exception is UnauthorizedAccessException or IOException)
            {
                // 25, 50, 100, 200, then 400 ms: about five seconds in total. The common
                // collision remains almost invisible, while a completed save is not lost
                // merely because an external scanner kept its first handle a little longer.
                var delayMilliseconds = 25 * (1 << Math.Min(attempt - 1, 4));
                await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
