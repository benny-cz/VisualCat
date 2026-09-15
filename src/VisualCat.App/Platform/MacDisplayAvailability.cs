using System.Runtime.InteropServices;

namespace VisualCat.App.Platform;

/// <summary>
/// Whether this Mac currently has a display the window server can draw on.
/// </summary>
/// <remarks>
/// <para>
/// Avalonia's macOS backend starts a CoreVideo display link during <c>AppBuilder.Setup()</c>, and
/// <c>CVDisplayLinkStart</c> has nothing to attach to while every display is asleep. The failure
/// arrives as an <see cref="InvalidOperationException"/> carrying the raw number <c>-6661</c>,
/// nothing catches it, and the process aborts — printing the same stack trace twice, leaving a
/// crash report in <c>~/Library/Logs/DiagnosticReports/</c> per attempt, and showing an
/// interactive user the system's "quit unexpectedly" dialog (finding F-23).
/// </para>
/// <para>
/// The state is neither exotic nor rare: the display sleeps on idle after ten minutes by default,
/// the screen locks after that, and every scheduled capture, every SSH or Remote Desktop session
/// onto an unattended Mac, and every laptop woken with the lid closed lands in it. Asking
/// CoreGraphics first costs one call and turns an abort into either a short wait or one sentence.
/// </para>
/// <para>
/// Every failure here answers <see langword="true"/>. This class exists to produce a better
/// message, never to withhold a launch: if CoreGraphics cannot be reached or answers something
/// unexpected, the ordinary start-up path runs exactly as it did before.
/// </para>
/// </remarks>
public static class MacDisplayAvailability
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/Versions/A/CoreGraphics";

    /// <summary>
    /// How long a launch waits for a display before giving up.
    /// </summary>
    /// <remarks>
    /// Short enough that an unattended job is not held up, long enough to cover the case a user
    /// actually hits: clicking the icon as the screen goes dark, or launching over SSH right
    /// after sending a wake. Nothing wakes the display on the product's behalf — taking over a
    /// Mac's screen because a log viewer was started is not this program's decision to make.
    /// </remarks>
    private static readonly TimeSpan WaitForDisplay = TimeSpan.FromSeconds(6);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Waits briefly for a usable display and reports whether one appeared.
    /// </summary>
    /// <param name="sleep">Injected so the wait can be tested without spending it.</param>
    public static bool WaitForUsableDisplay(Action<TimeSpan>? sleep = null)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return true;
        }

        sleep ??= Thread.Sleep;
        var deadline = DateTime.UtcNow + WaitForDisplay;
        while (true)
        {
            if (HasUsableDisplay())
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            sleep(PollInterval);
        }
    }

    /// <summary>Whether at least one active display is attached and awake, right now.</summary>
    public static bool HasUsableDisplay()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return true;
        }

        try
        {
            var displays = new uint[16];
            if (CGGetActiveDisplayList((uint)displays.Length, displays, out var count) != 0)
            {
                return true;
            }

            if (count == 0)
            {
                return false;
            }

            // Every attached display asleep is the state that breaks the display link. One awake
            // display is enough, which is also the right answer for a clamshell Mac driving an
            // external screen.
            for (var index = 0; index < Math.Min(count, (uint)displays.Length); index++)
            {
                if (CGDisplayIsAsleep(displays[index]) == 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return true;
        }
    }

    /// <summary>
    /// Whether a start-up failure is the display link finding no display, whatever wrapped it.
    /// </summary>
    /// <remarks>
    /// Matched on the message rather than on a type, for the same reason the X11 branch is: the
    /// same condition surfaces bare from <c>AppBuilder.Setup()</c> and wrapped in a
    /// <see cref="TypeInitializationException"/> when the backend is touched from a static
    /// constructor. <c>-6661</c> is <c>kCVReturnDisplayLinkCallbacksNotSet</c>'s neighbourhood in
    /// CoreVideo's error range and is what the failure actually prints, so it is matched
    /// alongside the sentence, not instead of it.
    /// </remarks>
    public static bool IsNoDisplayStartupFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("RenderTimer", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("-6661", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What to print instead of a stack trace and a raw CoreVideo number.</summary>
    /// <remarks>
    /// Naming <c>vcat</c> is the important half. The product has a complete answer for the
    /// unattended case — the command line needs no display at all, and indexes, captures and
    /// exports identically — and a user staring at a crash has no way to know that.
    /// </remarks>
    public static string Explain() =>
        string.Join(
            Environment.NewLine,
            "VisualCat could not start because this Mac has no display it can draw on:",
            "  the screen is asleep, locked, or no display is attached.",
            "  Wake the display and try again.",
            "  For an unattended or scheduled capture, use the vcat command line instead — it",
            "  needs no display, and it is in the VisualCat-CLI archive beside this one:",
            "      vcat capture-adb --serial <device> --output capture.vcat",
            "      vcat index <logfile> --output session.vcat");

    // DllImport rather than LibraryImport: the source generator it uses needs unsafe code, and
    // two calls taking blittable arguments are not worth turning that on for the whole assembly.
    [DllImport(CoreGraphics)]
    private static extern int CGGetActiveDisplayList(uint maxDisplays, uint[] activeDisplays, out uint displayCount);

    [DllImport(CoreGraphics)]
    private static extern int CGDisplayIsAsleep(uint display);
}
