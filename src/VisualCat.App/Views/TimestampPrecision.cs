using VisualCat.Domain.Time;

namespace VisualCat.App.Views;

/// <summary>
/// How many fractional digits a rendered entry timestamp needs.
/// </summary>
/// <remarks>
/// Logcat's text formats print milliseconds; only a capture that asked for the <c>usec</c>
/// modifier carries microseconds, and nothing in a session says which happened. Rather than
/// guess from the detected format — the same format name covers both — the workspace watches
/// the data it is already showing: an instant that is not a whole millisecond is proof the
/// capture has microsecond detail, and until one appears the three trailing zeros are noise
/// on a row whose message is already being clipped.
/// </remarks>
internal static class TimestampPrecision
{
    /// <summary>Row/inspector format while the session has shown only whole milliseconds.</summary>
    internal const string MillisecondFormat = "MM-dd HH:mm:ss.fff";

    /// <summary>Row/inspector format once sub-millisecond detail has been seen.</summary>
    internal const string MicrosecondFormat = "MM-dd HH:mm:ss.ffffff";

    /// <summary>Whether this instant carries detail a millisecond rendering would drop.</summary>
    internal static bool NeedsMicroseconds(InstantUs? instant) =>
        instant is { } value && value.Value % 1_000 != 0;
}
