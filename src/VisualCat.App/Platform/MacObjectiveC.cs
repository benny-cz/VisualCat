using System.Runtime.InteropServices;

namespace VisualCat.App.Platform;

/// <summary>
/// The smallest possible door into the Objective-C runtime: send one no-argument message to
/// <c>NSApplication.sharedApplication</c>.
/// </summary>
/// <remarks>
/// <para>
/// Three of the application menu's items — Hide, Hide Others, Show All — are <c>NSApplication</c>
/// selectors with no managed equivalent in Avalonia. Declaring the application menu is what fixes
/// the ⌥⌘Q binding on Hide Others and the framework's about box (finding F-03), and declaring it
/// means supplying the behaviour too.
/// </para>
/// <para>
/// Everything here is best-effort and silent on failure. A menu item that cannot hide the
/// application is a disappointment; an exception raised out of a menu click is a crash, and this
/// product has already had one of those on a path a user could not report (finding F-14).
/// </para>
/// </remarks>
internal static class MacObjectiveC
{
    private const string ObjectiveCRuntime = "/usr/lib/libobjc.A.dylib";

    /// <summary>
    /// Sends <paramref name="selector"/> to the shared <c>NSApplication</c>, with <c>nil</c> as
    /// the sender, which is what a menu item's target-action pair does.
    /// </summary>
    public static void SendToSharedApplication(string selector)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            var applicationClass = objc_getClass("NSApplication");
            if (applicationClass == IntPtr.Zero)
            {
                return;
            }

            var shared = objc_msgSend(applicationClass, sel_registerName("sharedApplication"));
            if (shared == IntPtr.Zero)
            {
                return;
            }

            objc_msgSend(shared, sel_registerName(selector), IntPtr.Zero);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
        }
    }

    [DllImport(ObjectiveCRuntime, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjectiveCRuntime, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr argument);
}
