namespace VisualCat.App.Presentation;

/// <summary>
/// What a session tab is currently doing, as a state rather than as a sentence.
/// </summary>
/// <remarks>
/// The workspace used to answer this by matching prefixes of the status line
/// (<c>Status.StartsWith("Capturing")</c> and five more), which made every reading of the
/// state depend on the exact wording of a user-facing string: rephrasing "Waiting for
/// capture capacity…" silently broke the "preparing" empty state, and the wording could not
/// be improved without hunting for the views that parse it. The status line is prose for
/// the reader; this is the state the views switch on.
/// </remarks>
public enum SessionActivity
{
    /// <summary>Opened from disk, or not started yet: nothing is in flight.</summary>
    Idle,

    /// <summary>Waiting for a free reader before an import or capture can begin.</summary>
    Queued,

    /// <summary>Reading a file into the session.</summary>
    Importing,

    /// <summary>Checking the device and settling the logcat format.</summary>
    Connecting,

    /// <summary>The capture is open and waiting for its first line.</summary>
    Starting,

    /// <summary>A live capture is running.</summary>
    Capturing,

    /// <summary>A graceful stop is draining what has already been committed.</summary>
    Stopping,

    /// <summary>Finished: everything the source offered is in the session.</summary>
    Ready,

    /// <summary>Ended early at the user's request; what was committed is retained.</summary>
    Stopped,

    /// <summary>Ended in a failure the reader has to be told about.</summary>
    Failed,
}
