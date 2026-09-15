using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace VisualCat.App.Platform;

/// <summary>
/// The handful of application-level actions a macOS menu bar has to be able to perform.
/// </summary>
/// <remarks>
/// Declaring the application menu is what replaces the stock one's <c>About Avalonia</c> and its
/// ⌥⌘Q binding for <em>Hide Others</em> — one modifier away from ⌘Q on a command people reach for
/// in a hurry (finding F-03). Declaring it also means supplying what its items do, which for
/// hide, hide-others and show-all means talking to <c>NSApplication</c> directly, because Avalonia
/// exposes no managed equivalent.
/// </remarks>
internal static class MacApplicationCommands
{
    /// <summary>Hides this application, as ⌘H does.</summary>
    public static void Hide() => MacObjectiveC.SendToSharedApplication("hide:");

    /// <summary>Hides every other application, as ⌥⌘H does.</summary>
    public static void HideOthers() => MacObjectiveC.SendToSharedApplication("hideOtherApplications:");

    /// <summary>Unhides every application.</summary>
    public static void ShowAll() => MacObjectiveC.SendToSharedApplication("unhideAllApplications:");

    /// <summary>
    /// Ends the application the product's own way, so windows close through the lifetime and the
    /// workspace and window state are persisted, rather than being torn down by AppKit.
    /// </summary>
    public static void Quit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    /// <summary>Opens a documentation link in the user's browser.</summary>
    public static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return;
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is { } window)
        {
            _ = TopLevel.GetTopLevel(window)?.Launcher.LaunchUriAsync(uri);
        }
    }
}
