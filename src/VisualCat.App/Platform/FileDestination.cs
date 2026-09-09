using Avalonia.Platform.Storage;
using VisualCat.Application.UseCases;

namespace VisualCat.App.Platform;

/// <summary>
/// One chosen place a file operation writes to, named without naming the platform.
/// </summary>
/// <remarks>
/// <para>
/// The destination a reader picks is an <see cref="IStorageFile"/> on both shells, and
/// Avalonia's storage interfaces are explicitly not implementable outside the framework. An
/// operation that takes one therefore cannot be driven past its picker by a test at all — and
/// the export's five exits, its provider failure and its cancellation-during-delivery are
/// exactly the states no native picker will produce on demand.
/// </para>
/// <para>
/// So the operation depends on this instead: a name, whether publication is a local rename or
/// a provider stream, and the one write it performs. The shell hands it
/// <see cref="Of(IStorageFile)"/> around the picker's real answer; a test hands it something
/// that fails at a chosen byte. Nothing about the write path changes — this only stops the
/// platform's own type from being the only way to express a destination.
/// </para>
/// </remarks>
internal abstract class FileDestination : IDisposable
{
    /// <summary>The destination's file name, as the completion notice reports it.</summary>
    internal abstract string Name { get; }

    /// <summary>
    /// Whether writing publishes straight to a local path, in which case the producer's own
    /// atomic staging is the commit and no provider stream is involved.
    /// </summary>
    internal abstract bool UsesDirectLocalPath { get; }

    /// <summary>Runs the producer and delivers its output, reporting publication facts.</summary>
    internal abstract Task WriteAsync(
        Func<string, CancellationToken, Task> producer,
        IProgress<FileWorkProgress>? progress,
        Action<StorageWritePublication>? publication,
        CancellationToken cancellationToken);

    public virtual void Dispose()
    {
    }

    /// <summary>The destination a picker actually returned.</summary>
    internal static FileDestination Of(IStorageFile file) => new StorageFileDestination(file);

    private sealed class StorageFileDestination(IStorageFile file) : FileDestination
    {
        internal override string Name => file.Name;

        internal override bool UsesDirectLocalPath => StorageFileBridge.UsesDirectLocalPath(file);

        internal override Task WriteAsync(
            Func<string, CancellationToken, Task> producer,
            IProgress<FileWorkProgress>? progress,
            Action<StorageWritePublication>? publication,
            CancellationToken cancellationToken) =>
            StorageFileBridge.WriteAsync(file, producer, progress, publication, cancellationToken);

        public override void Dispose()
        {
            base.Dispose();
            file.Dispose();
        }
    }
}
