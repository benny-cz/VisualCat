using VisualCat.Domain;
using VisualCat.Domain.Sessions;

namespace VisualCat.App.Presentation;

/// <summary>
/// Whether a session's acquisition finished — one fact, read from the manifest.
/// </summary>
/// <remarks>
/// A capture killed mid-flight used to be described three different ways at once: Recent
/// sessions called it <c>interrupted</c>, the automatically restored workspace called it
/// <c>Ready · 1,173 entries</c> with no partial notice at all, and Session info called it
/// <c>State: Importing</c> although no reader, import or capture existed (finding F-19). A
/// reader entering through the restored workspace — the common route — could reasonably read
/// <c>Ready</c> as "this capture is complete" and never learn that its tail is missing.
///
/// So completion is derived in exactly one place, from the durable fact the store already
/// records (<see cref="SessionDescriptor.Finalized"/>) plus whether this process is the one
/// currently writing, and every surface phrases it from here.
/// </remarks>
public enum SessionCompletion
{
    /// <summary>Acquisition finished and the manifest was finalized.</summary>
    Complete,

    /// <summary>This process is capturing or importing into the session right now.</summary>
    InProgress,

    /// <summary>
    /// Acquisition ended before the session was finalized. Everything that reached disk is
    /// readable and exact; the tail after the last commit is gone.
    /// </summary>
    RecoverablePartial,
}

/// <summary>The words each surface uses for a session's completion and lifecycle state.</summary>
public static class SessionCompletionText
{
    /// <summary>The completion of a stored session, given whether this process is writing it.</summary>
    public static SessionCompletion Of(bool finalized, bool workInFlight) =>
        workInFlight ? SessionCompletion.InProgress
        : finalized ? SessionCompletion.Complete
        : SessionCompletion.RecoverablePartial;

    /// <summary>One word for a list row: <c>complete</c>, <c>interrupted</c>, <c>capture in progress</c>.</summary>
    public static string Outcome(SessionCompletion completion) => completion switch
    {
        SessionCompletion.InProgress => "capture in progress",
        SessionCompletion.Complete => "complete",
        _ => "interrupted",
    };

    /// <summary>
    /// The lifecycle row in Session info, in product language rather than as a raw enum.
    /// </summary>
    /// <remarks>
    /// The pane rendered <c>$"{descriptor.State}"</c>, so a running on-device capture read
    /// <c>State: Importing</c> — the domain word for reading a finite file — while the status
    /// line beside it correctly said <c>Capturing</c> (finding F-14). Two of these are also
    /// internal-only (<c>Empty</c>, <c>SelectingSource</c>) and one is a word no reader of a
    /// log analyser should have to interpret (<c>Streaming</c>).
    /// </remarks>
    public static string State(SessionState state, SessionCompletion completion)
    {
        if (completion == SessionCompletion.RecoverablePartial)
        {
            return "Interrupted · what reached disk was recovered";
        }

        return state switch
        {
            SessionState.Empty or SessionState.SelectingSource => "Preparing",
            SessionState.Importing => "Reading",
            SessionState.Connecting => "Connecting",
            SessionState.Streaming => "Capturing",
            SessionState.Paused => "Paused",
            SessionState.Stopping => "Finishing",
            SessionState.Stopped => "Stopped",
            SessionState.Ready => "Ready",
            SessionState.Cancelling => "Cancelling",
            SessionState.Cancelled => "Cancelled",
            SessionState.Failed => "Failed",
            _ => "Ready",
        };
    }

    /// <summary>The status line of a session opened from disk.</summary>
    public static string OpenedStatus(SessionCompletion completion, long entries) => completion switch
    {
        SessionCompletion.RecoverablePartial =>
            $"Interrupted · {Counted.Entries(entries)} recovered · the capture ended before it was finished",
        _ => $"Ready · {Counted.Entries(entries)}",
    };
}
