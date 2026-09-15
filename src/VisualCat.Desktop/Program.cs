using Avalonia;
using VisualCat.App;
using VisualCat.App.Platform;
using VisualCat.Domain;
using VisualCat.Infrastructure.Diagnostics;

namespace VisualCat.Desktop;

internal static class Program
{
    /// <summary>
    /// <c>EX_UNAVAILABLE</c>. A configuration problem, not a crash: the process exits rather
    /// than aborting, so a host with <c>apport</c> or <c>systemd-coredump</c> enabled does not
    /// store a core dump of a log viewer for what is an ordinary mistake.
    /// </summary>
    private const int GraphicalSessionUnavailable = 69;

    [STAThread]
    public static void Main(string[] args)
    {
        // macOS aborts inside Avalonia's display-link start-up when every display is asleep,
        // with an unhandled exception, a doubled stack trace and a system crash report per
        // attempt (finding F-23). Ask CoreGraphics before the backend does, wait a few seconds
        // for a screen that is on its way back, and answer with a sentence rather than a trace.
        if (OperatingSystem.IsMacOS() && !MacDisplayAvailability.WaitForUsableDisplay())
        {
            Console.Error.WriteLine(MacDisplayAvailability.Explain());
            Environment.Exit(GraphicalSessionUnavailable);
        }

        // Every desktop build knows where it came from: a portable archive is by definition not
        // store-installed. Without this the update command was gated off on every desktop, so
        // "Check for updates…" did not exist anywhere outside Android while SUPPORT.md
        // described it to desktop readers (finding F-03).
        PlatformSourceRegistry.GetInstallOrigin ??= static () => AppInstallOrigin.PortableArchive;

        // The runtime's diagnostics IPC socket is the only thing this product leaves outside
        // its declared data root, and nothing ever removed it: fifteen accumulated in one
        // session of ordinary use, fourteen of them from processes long gone (finding F-17).
        RuntimeResidue.SweepExitedDiagnosticSockets();
        AppDomain.CurrentDomain.ProcessExit += static (_, _) => RuntimeResidue.RemoveOwnDiagnosticSocket();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            RuntimeResidue.RemoveOwnDiagnosticSocket();
        }
        catch (Exception exception) when (IsGraphicalStartupFailure(exception))
        {
            // No DISPLAY, a DISPLAY nothing is listening on, or a missing libX11 all ended in a
            // bare .NET stack trace printed twice — once by this handler, once by the runtime's
            // default one — followed by SIGABRT, with nothing naming the missing package
            // (finding F-02). It is the first thing a user on a headless box or an SSH session
            // without -X sees, so it says what is wrong and what to do instead.
            Console.Error.WriteLine(Explain(exception));
            Environment.Exit(GraphicalSessionUnavailable);
        }
        catch (Exception exception) when (MacDisplayAvailability.IsNoDisplayStartupFailure(exception))
        {
            // The race the pre-flight check above cannot close: the display slept between the
            // check and the backend's own initialization. Same condition, same sentence.
            Console.Error.WriteLine(MacDisplayAvailability.Explain());
            Environment.Exit(GraphicalSessionUnavailable);
        }
        catch (ProductDataRootException exception)
        {
            // Already a product sentence naming the directory and the cause (F-19).
            Console.Error.WriteLine($"error: {exception.Message}");
            Environment.Exit(GraphicalSessionUnavailable);
        }

        // There is deliberately no catch-all below. One used to print the exception and rethrow,
        // so every unhandled start-up failure reached the reader twice — 38 lines of identical
        // trace in exactly the situation where they are hunting for one useful one (finding F-23,
        // and LINUX-LIVE-TEST-REPORT F-02). The runtime's own handler already reports an
        // unhandled exception in full; adding nothing to it is what stops the doubling.
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<VisualCat.App.App>()
            .UsePlatformDetect()
            .UseSkia()
            .LogToTrace();

    /// <summary>
    /// Whether the failure is the platform refusing to give the process a graphical session,
    /// rather than a fault in the product.
    /// </summary>
    /// <remarks>
    /// Matched on the shape of the failure rather than on an exception type, because the three
    /// routes into it produce three different types: a plain <see cref="Exception"/> for
    /// <c>XOpenDisplay failed</c>, a <see cref="DllNotFoundException"/> for an absent
    /// <c>libX11.so.6</c>, and a <see cref="TypeInitializationException"/> wrapping either when
    /// the backend is initialized from a static constructor.
    /// </remarks>
    private static bool IsGraphicalStartupFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DllNotFoundException)
            {
                return true;
            }

            if (current.Message.Contains("XOpenDisplay", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("wl_display", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("Could not initialize GLX", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string Explain(Exception exception)
    {
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        var wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        var missingLibrary = Chain(exception).OfType<DllNotFoundException>().FirstOrDefault();
        var lines = new List<string>
        {
            "VisualCat needs a graphical X11 or XWayland session, and could not open one.",
            $"  DISPLAY={Quote(display)}   WAYLAND_DISPLAY={Quote(wayland)}",
        };

        if (missingLibrary is not null)
        {
            lines.Add($"  A library it needs is missing: {missingLibrary.Message}");
        }

        if (OperatingSystem.IsLinux())
        {
            lines.Add("  On Debian or Ubuntu the packages are: libx11-6 libice6 libsm6 libfontconfig1");
            lines.Add("  On Fedora or RHEL: libX11 libICE libSM fontconfig");
        }

        lines.Add("  Over SSH, connect with -X or -Y so a display is forwarded.");
        lines.Add("  With no display at all, use the vcat command line instead — it needs none,");
        lines.Add("  and it is in the VisualCat-CLI archive beside this one.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string Quote(string? value) => value is null ? "(not set)" : $"'{value}'";

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
