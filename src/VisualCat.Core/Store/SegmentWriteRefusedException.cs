namespace VisualCat.Core.Store;

/// <summary>
/// Storage refused to accept a log segment for long enough that the capture could not go
/// on holding what it had captured.
/// </summary>
/// <remarks>
/// Distinguished from the underlying <see cref="IOException"/> because the two say
/// different things to a user. The framework's message names an operation and a path; this
/// one names how long the capture persisted, how much it could not save, and — the part
/// that matters most to someone who has been capturing for hours — that everything written
/// before the trouble started is still there.
/// </remarks>
public sealed class SegmentWriteRefusedException : IOException
{
    public SegmentWriteRefusedException(string message, int attempts, int unsavedEntries, Exception innerException)
        : base(message, innerException)
    {
        Attempts = attempts;
        UnsavedEntries = unsavedEntries;
    }

    public SegmentWriteRefusedException()
    {
    }

    public SegmentWriteRefusedException(string message)
        : base(message)
    {
    }

    public SegmentWriteRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Gets how many consecutive attempts to save a segment were refused.</summary>
    public int Attempts { get; }

    /// <summary>Gets how many captured entries had not been saved when the capture stopped.</summary>
    public int UnsavedEntries { get; }
}
