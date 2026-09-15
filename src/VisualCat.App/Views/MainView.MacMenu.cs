using Avalonia.Controls;
using Avalonia.Input;

namespace VisualCat.App.Views;

/// <summary>
/// The macOS menu bar.
/// </summary>
/// <remarks>
/// <para>
/// On macOS the menu bar <em>is</em> the application's command surface, and this product shipped
/// without one: <c>count menu bar items</c> was 2 — the Apple menu and a stock application menu
/// Avalonia synthesises. No File, no Edit, no View, no Window, no Help. That removes ⌘W, ⌘M, ⌘,,
/// ⌘O, ⌘S, the Window menu's window list, the Help menu's searchable help, and — the one that
/// matters most — the Edit menu, which is what assistive technology enumerates to find the
/// editing commands and what a sighted user opens to discover that a command exists at all
/// (finding F-03).
/// </para>
/// <para>
/// The stock application menu also binds <em>Hide Others</em> to ⌥⌘Q rather than the
/// macOS-standard ⌥⌘H — one modifier away from ⌘Q, so a user reaching for "hide everything else"
/// could quit the application and lose an unsaved capture. Declaring the application menu here
/// replaces that binding along with <c>About Avalonia</c>, which opened the framework's about box
/// rather than the product's.
/// </para>
/// <para>
/// The File menu is generated from the same command descriptors the toolbar and the More menu are
/// built from, so a command cannot exist in one presentation and be missing from another — the
/// rule the shell already follows between the desktop toolbar and the phone command sheet. The
/// gestures are attached by label, in one table, so the whole keyboard contract of the platform
/// can be read at once and checked for collisions.
/// </para>
/// </remarks>
public sealed partial class MainView
{
    private readonly List<(NativeMenuItem Item, Func<bool> CanExecute)> _macMenuCommands = [];
    private NativeMenu? _macMenu;

    /// <summary>
    /// The accelerator each command carries on macOS, keyed by the label it is registered under.
    /// </summary>
    /// <remarks>
    /// Kept as one table rather than beside each registration, because the point is to be able to
    /// read the whole keyboard contract of the platform at once and see that nothing collides.
    /// </remarks>
    private static readonly Dictionary<string, KeyGesture> MacCommandGestures = new(StringComparer.Ordinal)
    {
        ["Open log…"] = new(Key.O, KeyModifiers.Meta),
        ["Open log with options…"] = new(Key.O, KeyModifiers.Meta | KeyModifiers.Alt),
        ["Open session…"] = new(Key.O, KeyModifiers.Meta | KeyModifiers.Shift),
        ["Recent captures…"] = new(Key.R, KeyModifiers.Meta | KeyModifiers.Shift),
        ["Save session…"] = new(Key.S, KeyModifiers.Meta),
        ["Save portable…"] = new(Key.S, KeyModifiers.Meta | KeyModifiers.Shift),
        ["Export CSV…"] = new(Key.E, KeyModifiers.Meta),
        ["Appearance & timeline…"] = new(Key.OemComma, KeyModifiers.Meta),
    };

    /// <summary>
    /// Builds and installs the menu bar. A no-op off macOS, where the toolbar is the command
    /// surface and a second one would be a duplicate rather than a convention.
    /// </summary>
    internal void InstallMacMenuBar(Window window)
    {
        if (!OperatingSystem.IsMacOS() || Avalonia.Application.Current is not { } application || _macMenu is not null)
        {
            return;
        }

        // Two different menus, exported by two different calls, and mixing them up puts File
        // inside the application menu. NativeMenu.GetMenu(Application) is the *application*
        // menu — the one bearing the product's name, which Avalonia synthesises when the app
        // declares none and then stores back on the Application, so replacing the property
        // later changes a value nothing reads again. It is mutated in place instead.
        if (NativeMenu.GetMenu(application) is { } applicationMenu)
        {
            PatchApplicationMenu(applicationMenu);
        }

        // The menu bar itself hangs off the window. It is set before the window is shown,
        // because the macOS exporter reads a window.s menu as that window becomes key: setting
        // it on an already-active window changes a value nothing looks at again.
        var menu = new NativeMenu();
        menu.Add(BuildFileMenu());
        menu.Add(BuildEditMenu());
        menu.Add(BuildViewMenu());
        menu.Add(BuildWindowMenu());
        menu.Add(BuildHelpMenu());
        _macMenu = menu;
        NativeMenu.SetMenu(window, menu);
        UpdateMacMenuAvailability();
    }

    /// <summary>
    /// Corrects the two defects in the stock application menu, in place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Avalonia synthesises this menu when the application declares none, and its items are the
    /// platform's own — Services, Hide, Show All and Quit all behave correctly and are left
    /// exactly as they are. Two do not.
    /// </para>
    /// <para>
    /// <c>About Avalonia</c> opens the framework's about box, so the product's version string —
    /// the one thing a bug report needs — was unreachable from the menu bar. And
    /// <em>Hide Others</em> is bound to ⌥⌘Q rather than the macOS-standard ⌥⌘H: one modifier away
    /// from Quit, on a command people reach for in a hurry, which can cost an unsaved capture
    /// (finding F-03). Both are replaced by header, and a <em>Settings…</em> item is inserted
    /// where macOS users look for it.
    /// </para>
    /// </remarks>
    private void PatchApplicationMenu(NativeMenu applicationMenu)
    {
        var about = new NativeMenuItem("About VisualCat");
        about.Click += async (_, _) => await RunAsync(ShowAboutAsync);
        Replace(applicationMenu, header => header.StartsWith("About", StringComparison.Ordinal), about, fallbackIndex: 0);

        var hideOthers = new NativeMenuItem("Hide Others")
        {
            Gesture = new KeyGesture(Key.H, KeyModifiers.Meta | KeyModifiers.Alt),
        };
        hideOthers.Click += (_, _) => Platform.MacApplicationCommands.HideOthers();
        Replace(applicationMenu, header => header == "Hide Others", hideOthers, fallbackIndex: null);

        var settings = new NativeMenuItem("Settings…")
        {
            Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta),
        };
        settings.Click += async (_, _) => await RunAsync(ShowAppearanceAsync);
        applicationMenu.Items.Insert(Math.Min(1, applicationMenu.Items.Count), new NativeMenuItemSeparator());
        applicationMenu.Items.Insert(Math.Min(2, applicationMenu.Items.Count), settings);
    }

    /// <summary>
    /// Swaps the first item whose header matches, or inserts at <paramref name="fallbackIndex"/>
    /// when the platform has renamed it under us.
    /// </summary>
    private static void Replace(
        NativeMenu menu,
        Func<string, bool> matches,
        NativeMenuItem replacement,
        int? fallbackIndex)
    {
        for (var index = 0; index < menu.Items.Count; index++)
        {
            if (menu.Items[index] is NativeMenuItem { Header: { } header } && matches(header))
            {
                menu.Items[index] = replacement;
                return;
            }
        }

        if (fallbackIndex is { } position)
        {
            menu.Items.Insert(Math.Min(position, menu.Items.Count), replacement);
        }
    }

    private NativeMenuItem BuildFileMenu()
    {
        var file = new NativeMenu();

        // Open log and ADB live are toolbar primaries rather than secondary commands, so they
        // are named here; everything else is taken from the registered descriptors, in the
        // order and grouping the shell already gives them.
        Add(file, "Open log…", OpenLogAsync, CanStartFileOperation, new KeyGesture(Key.O, KeyModifiers.Meta));
        if (!OperatingSystem.IsAndroid())
        {
            Add(file, "ADB live…", StartAdbAsync, null, new KeyGesture(Key.L, KeyModifiers.Meta | KeyModifiers.Shift));
        }

        var lastGroup = CommandGroup.Open;
        foreach (var command in _secondaryCommands
                     .Where(static command => !command.IsSetting)
                     .OrderBy(static command => command.Group))
        {
            if (command.Group != lastGroup)
            {
                file.Add(new NativeMenuItemSeparator());
                lastGroup = command.Group;
            }

            Add(file, command.Label, command.Action, command.CanExecute, Gesture(command.Label));
        }

        file.Add(new NativeMenuItemSeparator());
        var close = new NativeMenuItem("Close Window") { Gesture = new KeyGesture(Key.W, KeyModifiers.Meta) };
        close.Click += (_, _) => (VisualRoot as Window)?.Close();
        file.Add(close);

        return new NativeMenuItem("File") { Menu = file };
    }

    /// <summary>
    /// The Edit menu.
    /// </summary>
    /// <remarks>
    /// Avalonia already routes ⌘X/⌘C/⌘V/⌘A inside a text box through its own hotkey
    /// configuration, so these items are not what makes the keys work. They are what makes the
    /// commands <em>visible</em>: a sighted user opens Edit to find out whether Find exists, and
    /// assistive technology enumerates it to offer the editing commands at all. Each item acts
    /// on whatever currently holds focus, which is the macOS contract for this menu.
    /// </remarks>
    private NativeMenuItem BuildEditMenu()
    {
        var edit = new NativeMenu();
        AddEditing(edit, "Cut", new KeyGesture(Key.X, KeyModifiers.Meta), static box => box.Cut());
        AddEditing(edit, "Copy", new KeyGesture(Key.C, KeyModifiers.Meta), static box => box.Copy());
        AddEditing(edit, "Paste", new KeyGesture(Key.V, KeyModifiers.Meta), static box => box.Paste());
        AddEditing(edit, "Select All", new KeyGesture(Key.A, KeyModifiers.Meta), static box => box.SelectAll());
        edit.Add(new NativeMenuItemSeparator());

        var find = new NativeMenuItem("Find…") { Gesture = new KeyGesture(Key.F, KeyModifiers.Meta) };
        find.Click += (_, _) => ActiveWorkspace?.FocusSearch();
        edit.Add(find);

        var next = new NativeMenuItem("Find Next") { Gesture = new KeyGesture(Key.G, KeyModifiers.Meta) };
        next.Click += (_, _) => ActiveWorkspace?.StepSearchMatch(1);
        edit.Add(next);

        var previous = new NativeMenuItem("Find Previous")
        {
            Gesture = new KeyGesture(Key.G, KeyModifiers.Meta | KeyModifiers.Shift),
        };
        previous.Click += (_, _) => ActiveWorkspace?.StepSearchMatch(-1);
        edit.Add(previous);

        return new NativeMenuItem("Edit") { Menu = edit };
    }

    private NativeMenuItem BuildViewMenu()
    {
        var view = new NativeMenu();
        var fit = new NativeMenuItem("Fit Session") { Gesture = new KeyGesture(Key.D0, KeyModifiers.Meta) };
        fit.Click += (_, _) => ActiveWorkspace?.FitSession();
        view.Add(fit);

        var zoomIn = new NativeMenuItem("Zoom In") { Gesture = new KeyGesture(Key.OemPlus, KeyModifiers.Meta) };
        zoomIn.Click += (_, _) => ActiveWorkspace?.ZoomTimeline(zoomIn: true);
        view.Add(zoomIn);

        var zoomOut = new NativeMenuItem("Zoom Out") { Gesture = new KeyGesture(Key.OemMinus, KeyModifiers.Meta) };
        zoomOut.Click += (_, _) => ActiveWorkspace?.ZoomTimeline(zoomIn: false);
        view.Add(zoomOut);

        return new NativeMenuItem("View") { Menu = view };
    }

    private NativeMenuItem BuildWindowMenu()
    {
        var window = new NativeMenu();
        var minimise = new NativeMenuItem("Minimize") { Gesture = new KeyGesture(Key.M, KeyModifiers.Meta) };
        minimise.Click += (_, _) =>
        {
            if (VisualRoot is Window host)
            {
                host.WindowState = WindowState.Minimized;
            }
        };
        window.Add(minimise);

        var zoom = new NativeMenuItem("Zoom");
        zoom.Click += (_, _) =>
        {
            if (VisualRoot is Window host)
            {
                host.WindowState = host.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }
        };
        window.Add(zoom);

        return new NativeMenuItem("Window") { Menu = window };
    }

    private static NativeMenuItem BuildHelpMenu()
    {
        var help = new NativeMenu();
        AddLink(help, "VisualCat Help", "https://github.com/benny-cz/VisualCat/blob/main/README.md");
        AddLink(help, "Keyboard Shortcuts", "https://github.com/benny-cz/VisualCat/blob/main/docs/KEYBOARD.md");
        AddLink(help, "Release Notes", "https://github.com/benny-cz/VisualCat/blob/main/docs/RELEASE-NOTES.md");
        help.Add(new NativeMenuItemSeparator());
        AddLink(help, "Report a Bug", "https://github.com/benny-cz/VisualCat/issues");
        return new NativeMenuItem("Help") { Menu = help };
    }

    private static KeyGesture? Gesture(string label) =>
        MacCommandGestures.TryGetValue(label, out var gesture) ? gesture : null;

    private void Add(
        NativeMenu menu,
        string label,
        Func<Task> action,
        Func<bool>? canExecute,
        KeyGesture? gesture)
    {
        var item = new NativeMenuItem(label) { Gesture = gesture };
        item.Click += async (_, _) => await RunAsync(action);
        menu.Add(item);
        if (canExecute is not null)
        {
            _macMenuCommands.Add((item, canExecute));
        }
    }

    private static void AddEditing(NativeMenu menu, string label, KeyGesture gesture, Action<TextBox> act)
    {
        var item = new NativeMenuItem(label) { Gesture = gesture };
        item.Click += (_, _) =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow?.FocusManager?.GetFocusedElement() is TextBox box)
            {
                act(box);
            }
        };
        menu.Add(item);
    }

    private static void AddLink(NativeMenu menu, string label, string url)
    {
        var item = new NativeMenuItem(label);
        item.Click += (_, _) => Platform.MacApplicationCommands.OpenUrl(url);
        menu.Add(item);
    }

    /// <summary>
    /// Greys the menu items whose commands cannot run, alongside the toolbar buttons.
    /// </summary>
    /// <remarks>
    /// A menu that offers Save while nothing is open is worse than no menu: the reader learns the
    /// command exists and then that pressing it does nothing. The same predicates the toolbar
    /// uses answer here, so the two can never disagree.
    /// </remarks>
    private void UpdateMacMenuAvailability()
    {
        if (_macMenu is null)
        {
            return;
        }

        foreach (var (item, canExecute) in _macMenuCommands)
        {
            item.IsEnabled = canExecute();
        }
    }
}
