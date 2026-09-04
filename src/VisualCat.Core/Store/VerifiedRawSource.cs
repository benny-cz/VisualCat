using System.Security.Cryptography;
namespace VisualCat.Core.Store;

/// <summary>
/// An open raw-evidence handle whose recorded prefix has been verified against the session
/// manifest. All reads are confined to that prefix, so bytes appended after indexing can
/// never become part of the session by accident.
/// </summary>
public sealed class VerifiedRawSource : IAsyncDisposable
{
    private const int BufferSize = 1024 * 1024;
    private readonly FileStream _stream;
    private readonly string _readFailureMessage;

    private VerifiedRawSource(FileStream stream, long recordedLength, string readFailureMessage)
    {
        _stream = stream;
        RecordedLength = recordedLength;
        _readFailureMessage = readFailureMessage;
    }

    /// <summary>The immutable byte prefix described by the open session snapshot.</summary>
    public long RecordedLength { get; }

    /// <summary>
    /// Opens and verifies the exact file handle that subsequent reads use. External files may
    /// have grown, but their complete recorded prefix must still hash to the manifest digest.
    /// A finalized embedded source must also have exactly the recorded length.
    /// </summary>
    public static async Task<VerifiedRawSource> OpenAsync(
        SessionSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var identity = snapshot.Manifest.Source;
        var embedded = identity.Embedded;
        var unavailable = embedded
            ? "This session's embedded raw evidence is missing or damaged. The index remains usable; verify or restore the session."
            : "The original log file is unavailable. The indexed session remains usable, but its raw evidence cannot be read.";
        var changed = embedded
            ? "This session's embedded raw evidence is damaged and no longer matches its index. The index remains usable; verify or restore the session."
            : "The original log file changed and no longer matches this session's index. The index remains usable; re-import the current file or restore the unchanged original.";

        if (snapshot.RawPath is not { } path)
        {
            throw new RawEvidenceException(unavailable);
        }

        FileStream stream;
        try
        {
            stream = OpenPreferringWriteExclusion(path);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            throw new RawEvidenceException(unavailable, exception);
        }

        try
        {
            if (stream.Length < identity.Length ||
                embedded && snapshot.Descriptor.Finalized && stream.Length != identity.Length)
            {
                throw new RawEvidenceException(changed);
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[BufferSize];
            var remaining = identity.Length;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await stream.ReadAsync(
                    buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new RawEvidenceException(changed);
                }

                hash.AppendData(buffer, 0, read);
                remaining -= read;
            }

            var actual = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.Equals(actual, identity.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new RawEvidenceException(changed);
            }

            stream.Position = 0;
            return new VerifiedRawSource(stream, identity.Length, changed);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Reads one indexed span after proving it stays inside the verified prefix.</summary>
    public async ValueTask ReadExactlyAsync(
        long offset,
        Memory<byte> destination,
        CancellationToken cancellationToken = default)
    {
        ValidateSpan(offset, destination.Length);
        try
        {
            _stream.Position = offset;
            await _stream.ReadExactlyAsync(destination, cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException exception)
        {
            throw new RawEvidenceException(_readFailureMessage, exception);
        }
    }

    /// <summary>
    /// Copies the whole verified prefix — this session's complete raw evidence, and nothing
    /// a later writer appended to the same file.
    /// </summary>
    public async Task CopyPrefixToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        _stream.Position = 0;
        var buffer = new byte[BufferSize];
        var remaining = RecordedLength;
        while (remaining > 0)
        {
            var read = await _stream.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new RawEvidenceException(_readFailureMessage);
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
        }
    }

    /// <summary>Copies one indexed span without ever crossing the verified prefix.</summary>
    public async Task CopyToAsync(
        Stream destination,
        long offset,
        int length,
        byte[] buffer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length == 0)
        {
            throw new ArgumentException("The copy buffer cannot be empty.", nameof(buffer));
        }

        ValidateSpan(offset, length);
        _stream.Position = offset;
        var remaining = length;
        while (remaining > 0)
        {
            var read = await _stream.ReadAsync(
                buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new RawEvidenceException(_readFailureMessage);
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
        }
    }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();

    /// <summary>
    /// Opens the evidence, excluding other writers where the platform will allow it and
    /// sharing where it will not.
    /// </summary>
    /// <remarks>
    /// The exclusion is the stronger lease: while it holds, no cooperative process can alter
    /// the file between this handle's verification and its reads. It is not always available,
    /// and the case where it is not is an ordinary one — the capture that produced the file is
    /// still running (`adb logcat &gt; capture.txt`), or VisualCat itself is still appending to
    /// a progressive `raw.log`. Refusing raw evidence for a file that is merely still growing
    /// would be a worse answer than the one this lease exists to prevent, and a false one: the
    /// recorded prefix is right there and verifies. So the exclusive open is attempted first
    /// and a sharing violation falls back to a shared one, where the residual exposure is a
    /// cooperative process rewriting the recorded prefix inside a single operation. An
    /// appending writer cannot: it never moves a recorded byte, and every operation
    /// re-verifies the whole prefix on the handle it reads from.
    /// </remarks>
    private static FileStream OpenPreferringWriteExclusion(string path)
    {
        try
        {
            return Open(path, FileShare.Read);
        }
        catch (IOException) when (File.Exists(path))
        {
            return Open(path, FileShare.ReadWrite | FileShare.Delete);
        }
    }

    private static FileStream Open(string path, FileShare share) =>
        new(
            path,
            FileMode.Open,
            FileAccess.Read,
            share,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

    private void ValidateSpan(long offset, int length)
    {
        if (offset < 0 || length < 0 || offset > RecordedLength - length)
        {
            throw new RawEvidenceException(
                "The session index points outside its verified raw evidence. The index may be damaged; run Verify for details.");
        }
    }
}

/// <summary>A raw-evidence request that could not meet the session's exact-byte contract.</summary>
public sealed class RawEvidenceException : Exception
{
    public RawEvidenceException(string message)
        : base(message)
    {
    }

    public RawEvidenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
