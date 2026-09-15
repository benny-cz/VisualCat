using Avalonia.Input;

namespace VisualCat.App.Platform;

/// <summary>
/// The modifier this platform uses for application shortcuts.
/// </summary>
/// <remarks>
/// <para>
/// The product's shortcuts were written against <see cref="KeyModifiers.Control"/> everywhere,
/// which is right on Windows and Linux and wrong on macOS, where the primary modifier is Command.
/// A Mac user pressing ⌘F got nothing and concluded that search did not exist — while ⌃F is
/// already spoken for by the platform, where it moves the caret forward one character inside a
/// text field (finding F-03).
/// </para>
/// <para>
/// So macOS answers to Command only, and every other platform to Control only. Accepting both on
/// the Mac would take ⌃F away from the text fields the system gives it to, which is a worse
/// trade than asking a Mac user to press the key their platform has always used.
/// </para>
/// </remarks>
internal static class PlatformShortcuts
{
    /// <summary>Command on macOS, Control everywhere else.</summary>
    public static KeyModifiers Primary { get; } =
        OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    /// <summary>Whether this key press carries the platform's shortcut modifier.</summary>
    public static bool HasPrimary(KeyModifiers modifiers) => modifiers.HasFlag(Primary);

    /// <summary>How to write the modifier in a tooltip or a document — <c>⌘</c> or <c>Ctrl+</c>.</summary>
    public static string PrimaryLabel { get; } = OperatingSystem.IsMacOS() ? "⌘" : "Ctrl+";
}
