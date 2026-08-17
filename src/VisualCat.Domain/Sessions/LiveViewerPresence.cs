namespace VisualCat.Domain.Sessions;

/// <summary>
/// Whether a human is currently able to see a live capture.
/// </summary>
/// <remarks>
/// <para>
/// A live capture publishes a snapshot every few seconds so the timeline feels
/// continuous. That cadence is only worth paying for while someone is looking at it: with
/// the screen off or the window hidden, each publication still sealed a segment, rewrote
/// the manifest, and woke every view query to redraw a display nobody could see. Over a
/// night that is hours of avoidable CPU, and on a phone it is avoidable flash wear as
/// well.
/// </para>
/// <para>
/// The capture never pauses and no data is lost either way — only the rate at which
/// already-captured entries are made visible changes, and re-entering the foreground
/// publishes immediately so the user never waits for the relaxed interval to elapse
/// (§10.6).
/// </para>
/// </remarks>
public sealed class LiveViewerPresence
{
    private int _watching = 1;

    /// <summary>Gets or sets whether the capture is currently on screen.</summary>
    public bool IsWatching
    {
        get => Volatile.Read(ref _watching) != 0;
        set => Volatile.Write(ref _watching, value ? 1 : 0);
    }
}
