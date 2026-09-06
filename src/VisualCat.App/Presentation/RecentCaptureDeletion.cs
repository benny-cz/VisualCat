using VisualCat.App.Views;
using VisualCat.Core.Store;
using VisualCat.Domain;
using VisualCat.Infrastructure.Configuration;

namespace VisualCat.App.Presentation;

/// <summary>Why the shell, rather than the filesystem, refuses to delete a capture.</summary>
internal enum CaptureProtection { None, Recording, Working, CloseFailed }

/// <summary>What the reader chose, with the label the product may say for it.</summary>
internal sealed record CaptureSelection(TemporarySessionInfo Session, string Label);

/// <summary>One frozen deletion target: identity, safe label and observed local tab state.</summary>
internal sealed record PreparedCapture(CaptureDeleteTarget Target, string Label, bool Open);

/// <summary>
/// A capture the reader had checked that preparation removed from the request before
/// confirmation, with the reason to say for it.
/// </summary>
internal sealed record CaptureExclusion(CaptureSelection Selection, CaptureProtection Protection, CaptureDeleteOutcome Outcome)
{
    internal string Reason => CaptureReason.For(Protection, Outcome);
}

internal sealed record PreparedCaptures(
    Guid Operation,
    string Root,
    IReadOnlyList<PreparedCapture> Captures,
    IReadOnlyList<CaptureExclusion> Excluded);

internal sealed record RecentCaptureSnapshot(CaptureInventory Inventory, IReadOnlySet<string> Capturing,
    IReadOnlySet<string> Busy, IReadOnlySet<string> Open, long Generation);

internal enum CaptureDeletionPhase { Closing, Deleting, Cleaning, Resolved }

/// <summary>
/// Where one operation has reached. <c>Current</c> is the 1-based index of the capture being
/// worked on; <c>Completed</c> counts targets with a settled result, which is not how many were
/// deleted; <c>Total</c> counts every distinct target, including ones that will fail or be
/// skipped, and is stable across the closing and filesystem phases.
/// </summary>
internal sealed record CaptureDeletionProgress(
    Guid Operation, CaptureDeletionPhase Phase, int Current, int Completed, int Total, string Label);

/// <summary>The product sentence for one capture's outcome. Never an exception's own words.</summary>
internal static class CaptureReason
{
    internal static string For(CaptureProtection protection, CaptureDeleteOutcome outcome) => protection switch
    {
        CaptureProtection.Recording => "This capture is being recorded. Stop the capture first.",
        CaptureProtection.Working => "VisualCat is still working on this capture. Wait for it to finish.",
        CaptureProtection.CloseFailed =>
            "VisualCat could not finish closing this capture. Try closing its tab if it is still open, then retry.",
        _ => outcome switch
        {
            CaptureDeleteOutcome.Deleted => "Deleted from temporary storage.",
            CaptureDeleteOutcome.DeletedPendingReclaim =>
                "Deleted. Storage cleanup is pending; some space is still in use. Try Retry storage cleanup.",
            CaptureDeleteOutcome.AlreadyMissing => "This capture was already missing. Its stale listing was removed.",
            CaptureDeleteOutcome.Protected or CaptureDeleteOutcome.Locked =>
                "This capture is in use. Close anything using it and try again.",
            CaptureDeleteOutcome.Refused =>
                "VisualCat cannot safely remove this capture from temporary storage. Check the storage location.",
            CaptureDeleteOutcome.Changed => "This capture changed after you selected it. Refresh and select it again.",
            CaptureDeleteOutcome.Denied => "VisualCat does not have permission to remove this capture.",
            CaptureDeleteOutcome.IoFailure =>
                "VisualCat could not remove this capture. Check that temporary storage is available and try again.",
            CaptureDeleteOutcome.Cancelled or CaptureDeleteOutcome.NotAttempted => "This capture was not deleted.",
            _ => "VisualCat could not verify what happened to this capture. Refresh before trying again.",
        },
    };
}

/// <summary>What a notice says about a whole visit to Recent captures, if anything.</summary>
internal enum CaptureNoticeKind { None, Information, Completion, Failure }

internal sealed record CaptureDeletionResult(PreparedCapture Capture, CaptureDeleteResult File,
    CaptureProtection Protection = CaptureProtection.None, bool TabsClosed = false)
{
    internal bool Failed => Protection != CaptureProtection.None || File.Outcome is
        CaptureDeleteOutcome.Protected or CaptureDeleteOutcome.Refused or CaptureDeleteOutcome.Changed or
        CaptureDeleteOutcome.Locked or CaptureDeleteOutcome.Denied or CaptureDeleteOutcome.IoFailure or
        CaptureDeleteOutcome.Unknown;

    internal bool Stopped => Protection == CaptureProtection.None &&
        File.Outcome is CaptureDeleteOutcome.Cancelled or CaptureDeleteOutcome.NotAttempted;

    internal string Reason => CaptureReason.For(Protection, File.Outcome);

    internal string Detail => Capture.Label + "\n" + Reason +
        (TabsClosed && !File.Removed ? " The tab was closed, but the capture was not deleted." : string.Empty);

    /// <summary>The in-dialog result line: every category that happened, none of them hidden.</summary>
    internal static string Summary(IEnumerable<CaptureDeletionResult> results)
    {
        var tally = Tally.Of(results);
        if (tally.Total == 0)
        {
            return string.Empty;
        }

        var sentences = new List<string>();
        if (tally.Deleted > 0)
        {
            sentences.Add($"Deleted {Counted.Captures(tally.Deleted)}.");
            if (tally.DeletedSize > 0)
            {
                sentences.Add(tally.SizeComplete
                    ? $"About {RecentSessionsDialog.FormatBytes(tally.DeletedSize)} of captures removed."
                    : $"At least about {RecentSessionsDialog.FormatBytes(tally.DeletedSize)} of captures removed.");
            }
        }
        else if (tally.Failed > 0)
        {
            sentences.Add("No captures were deleted.");
        }

        if (tally.Failed > 0)
        {
            sentences.Add($"{Counted.Captures(tally.Failed)} could not be deleted.");
        }

        if (tally.Missing > 0)
        {
            sentences.Add($"{Counted.Captures(tally.Missing)} {(tally.Missing == 1 ? "was" : "were")} already missing.");
        }

        if (tally.Stopped > 0)
        {
            sentences.Add($"Stopped. {Counted.Captures(tally.Stopped)} {(tally.Stopped == 1 ? "was" : "were")} not attempted.");
        }

        if (tally.Pending > 0)
        {
            sentences.Add($"Storage cleanup is pending for {Counted.Captures(tally.Pending)}. Some space is still in use.");
        }

        if (tally.Unknown > 0)
        {
            sentences.Add("Some results could not be verified. Try Refresh.");
        }

        return string.Join(' ', sentences);
    }

    /// <summary>
    /// The one notice a completed visit to Recent captures leaves behind, if it leaves one.
    /// </summary>
    /// <remarks>
    /// A batch that changed nothing because the reader stopped it has already said so in the
    /// dialog, and repeating it in the lane after the dialog closes reads as a failure report
    /// for a decision the reader made deliberately.
    /// </remarks>
    internal static (CaptureNoticeKind Kind, string Text) FinalNotice(IEnumerable<CaptureDeletionResult> results)
    {
        var values = results as IReadOnlyList<CaptureDeletionResult> ?? [.. results];
        var tally = Tally.Of(values);
        if (tally.Total == 0 || tally.Stopped == tally.Total)
        {
            return (CaptureNoticeKind.None, string.Empty);
        }

        if (tally.Unknown > 0)
        {
            return (CaptureNoticeKind.Failure,
                "Some deletion results could not be verified. Open Recent captures to refresh.");
        }

        if (tally.Deleted == 0 && tally.Failed == 0)
        {
            return tally.Missing > 0
                ? (CaptureNoticeKind.Information, "The selected captures were already missing.")
                : (CaptureNoticeKind.None, string.Empty);
        }

        if (tally.Deleted == 0)
        {
            return (CaptureNoticeKind.Failure,
                "No captures were deleted. " + values.First(result => result.Failed).Reason);
        }

        if (tally.Failed > 0)
        {
            return (CaptureNoticeKind.Failure,
                $"Deleted {Counted.Captures(tally.Deleted)}. {Counted.Captures(tally.Failed)} could not be deleted.");
        }

        return tally.Pending > 0
            ? (CaptureNoticeKind.Completion, $"Deleted {Counted.Captures(tally.Deleted)}. Storage cleanup is still pending.")
            : (CaptureNoticeKind.Completion, $"Deleted {Counted.Captures(tally.Deleted)} from temporary storage.");
    }

    /// <summary>
    /// The counters section 6.1 keeps distinct. Sizes estimate what a capture held; neither they
    /// nor a successful recursive delete measure storage that became free.
    /// </summary>
    internal readonly record struct Tally(
        int Total, int Deleted, int Pending, int Missing, int Failed, int Stopped, int Unknown,
        long DeletedSize, bool SizeComplete)
    {
        internal static Tally Of(IEnumerable<CaptureDeletionResult> results)
        {
            int total = 0, deleted = 0, pending = 0, missing = 0, failed = 0, stopped = 0, unknown = 0;
            long size = 0;
            var complete = true;
            foreach (var result in results)
            {
                total++;
                if (result.File.Committed)
                {
                    deleted++;
                    if (result.Capture.Target.SizeEstimate is { } estimate && estimate >= 0)
                    {
                        // Overflow-safe: corrupt metadata cannot turn a committed deletion into
                        // an exception raised while summarising it.
                        size = estimate > long.MaxValue - size ? long.MaxValue : size + estimate;
                    }
                    else
                    {
                        complete = false;
                    }
                }

                if (result.File.Outcome == CaptureDeleteOutcome.DeletedPendingReclaim)
                {
                    pending++;
                }

                if (result.File.Outcome == CaptureDeleteOutcome.AlreadyMissing)
                {
                    missing++;
                }

                if (result.File.Outcome == CaptureDeleteOutcome.Unknown)
                {
                    unknown++;
                }

                if (result.Failed)
                {
                    failed++;
                }
                else if (result.Stopped)
                {
                    stopped++;
                }
            }

            return new(total, deleted, pending, missing, failed, stopped, unknown, size, complete);
        }
    }
}

/// <summary>Everything the dialog needs from the shell, all of it bound to one frozen root.</summary>
internal sealed record RecentCaptureActions(
    Func<IReadOnlyList<CaptureSelection>, CancellationToken, Task<PreparedCaptures>> Prepare,
    Func<PreparedCaptures, IProgress<CaptureDeletionProgress>, CancellationToken, Task<IReadOnlyList<CaptureDeletionResult>>> Delete,
    Func<CancellationToken, Task<RecentCaptureSnapshot>> Refresh,
    Func<CancellationToken, Task> RetryCleanup,
    Func<IReadOnlyList<CaptureDeletionResult>>? Resolved = null);

/// <summary>The shell's ordered per-capture lifetime boundary, independent of dialog controls.</summary>
/// <remarks>
/// One capture at a time, and its tabs are closed only when its turn arrives: closing every
/// requested tab up front would mean Stop after the first capture had already closed tabs for
/// captures it then promises not to touch.
/// </remarks>
internal sealed class CaptureDeletionCoordinator(
    string root,
    Func<string, CaptureProtection> protection,
    Func<string, IReadOnlyList<Func<Task>>> closeTabs,
    Action<CaptureDeletionResult> resolved)
{
    /// <summary>
    /// Freezes the request. Captures that became protected, changed or unreachable are dropped
    /// with a reason rather than failing the whole preparation: the reader asked for a set, and
    /// the honest answer is the part of it that can still be confirmed.
    /// </summary>
    internal async Task<PreparedCaptures> PrepareAsync(
        IReadOnlyList<CaptureSelection> selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var captures = new List<PreparedCapture>();
        var excluded = new List<CaptureExclusion>();
        var distinct = new HashSet<string>(SessionPath.Comparer);
        foreach (var item in selection)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!distinct.Add(SessionPath.Canonical(item.Session.Path)))
            {
                continue;
            }

            var reason = protection(item.Session.Path);
            if (reason != CaptureProtection.None)
            {
                excluded.Add(new CaptureExclusion(item, reason, CaptureDeleteOutcome.Protected));
                continue;
            }

            try
            {
                var target = await CaptureDeletionService.PrepareAsync(root, item.Session, cancellationToken)
                    .ConfigureAwait(true);
                captures.Add(new PreparedCapture(target, item.Label, closeTabs(target.Path).Count > 0));
            }
            catch (Exception error) when (error is not OperationCanceledException && IsExpected(error))
            {
                excluded.Add(new CaptureExclusion(item, CaptureProtection.None, CaptureDeletionService.Classify(error)));
            }
        }

        return new PreparedCaptures(
            Guid.NewGuid(), SessionPath.Canonical(root), captures.AsReadOnly(), excluded.AsReadOnly());
    }

    internal async Task<IReadOnlyList<CaptureDeletionResult>> DeleteAsync(
        PreparedCaptures request, IProgress<CaptureDeletionProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        var results = new List<CaptureDeletionResult>(request.Captures.Count);
        var distinct = new HashSet<string>(SessionPath.Comparer);
        var total = request.Captures.Count(capture => distinct.Add(capture.Target.Path));
        distinct.Clear();
        foreach (var capture in request.Captures)
        {
            if (!distinct.Add(capture.Target.Path))
            {
                continue;
            }

            var index = results.Count + 1;
            if (cancellationToken.IsCancellationRequested)
            {
                // Stop reached this target before anything touched it. Its tabs are untouched,
                // which is the guarantee Stop makes, and "not attempted" is what that means.
                Publish(new CaptureDeletionResult(capture, new(capture.Target, CaptureDeleteOutcome.NotAttempted)));
                continue;
            }

            var closed = false;
            var reason = CaptureProtection.None;
            CaptureDeleteResult file;
            try
            {
                if (!SessionPath.Comparer.Equals(request.Root, SessionPath.Canonical(root)))
                {
                    throw new CaptureRefusedException();
                }

                // The whole root and this capture's identity are validated before any local tab
                // is closed: a close is a visible side effect and must not precede a refusal.
                await CaptureDeletionService.ValidateAsync(root, capture.Target, cancellationToken).ConfigureAwait(true);
                reason = protection(capture.Target.Path);
                if (reason != CaptureProtection.None)
                {
                    throw new SessionInUseException();
                }

                // Deletion intent first: it blocks a new reader or writer from arriving while
                // this operation's own idle tabs are being closed. Exclusive access is required
                // only later, once those local holders have released, so the operation never
                // waits for a lease the references it is still holding would prevent.
                using var reservation = await Task
                    .Run(() => SessionAccess.ReserveDeletion(capture.Target.Path), CancellationToken.None)
                    .ConfigureAwait(true);
                foreach (var close in closeTabs(capture.Target.Path))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    reason = protection(capture.Target.Path);
                    if (reason != CaptureProtection.None)
                    {
                        throw new SessionInUseException();
                    }

                    progress.Report(new(request.Operation, CaptureDeletionPhase.Closing, index, results.Count, total, capture.Label));
                    try
                    {
                        // Close removes the tab before awaiting its disposal, so a failure can
                        // leave no tab and no deleted capture. Both halves are reported.
                        closed = true;
                        await close().ConfigureAwait(true);
                    }
                    catch
                    {
                        reason = CaptureProtection.CloseFailed;
                        throw;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                reason = protection(capture.Target.Path);
                if (reason != CaptureProtection.None)
                {
                    throw new SessionInUseException();
                }

                progress.Report(new(request.Operation, CaptureDeletionPhase.Deleting, index, results.Count, total, capture.Label));
                file = await CaptureDeletionService.DeleteAsync(
                    root,
                    capture.Target,
                    reservation,
                    () => progress.Report(new(request.Operation, CaptureDeletionPhase.Cleaning, index, results.Count, total, capture.Label)),
                    cancellationToken).ConfigureAwait(true);
            }
            catch (Exception error)
            {
                file = new CaptureDeleteResult(capture.Target, CaptureDeletionService.Classify(error), error);
            }

            Publish(new CaptureDeletionResult(capture, file, reason, closed));
        }

        return results;

        void Publish(CaptureDeletionResult result)
        {
            // The outcome is preserved before diagnostics, refresh or the next target: a failure
            // in any of those must not lose a committed removal.
            results.Add(result);
            try
            {
                progress.Report(new(request.Operation, CaptureDeletionPhase.Resolved,
                    results.Count, results.Count, total, result.Capture.Label));
            }
            catch (Exception error) { WorkspaceViewModel.RecordFailure("capture.delete.progress", error); }
            try
            {
                resolved(result);
            }
            catch (Exception error)
            {
                WorkspaceViewModel.RecordFailure("capture.delete.reconcile", error);
            }

            if (result.Failed && result.File.Error is { } failure)
            {
                // The logger redacts the message; only the classification travels with it.
                WorkspaceViewModel.RecordFailure("capture.delete." + result.File.Outcome, failure);
            }
        }
    }

    private static bool IsExpected(Exception error) => error is IOException or UnauthorizedAccessException
        or ArgumentException or NotSupportedException or InvalidOperationException;
}
