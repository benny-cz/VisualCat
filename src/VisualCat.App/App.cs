using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Platform;
using VisualCat.App.Views;

namespace VisualCat.App;

public sealed class App : Avalonia.Application
{
    public static bool HighContrastEnabled { get; set; }

    public override void Initialize()
    {
        // What the accessibility bus and the window manager call this application. The default
        // is "Avalonia Application", which is what a screen-reader user saw in their
        // application list — the first thing the product ever said about itself, and it did not
        // say VisualCat (finding F-13).
        Name = "VisualCat";

        // Before any view exists, because a view formats its first numbers while it is being
        // built (audit 2, E1).
        DisplayCulture.Install();

        Styles.Add(Theme.ProductTheme.CreateFluentTheme());

        // The product's own brushes come first: the styles below resolve them by key, and a
        // style whose resource is not there yet resolves to nothing.
        Resources.MergedDictionaries.Add(Theme.ProductTheme.BuildResources());
        foreach (var style in Theme.ProductTheme.BuildStyles())
        {
            Styles.Add(style);
        }

        // Control themes rather than styles, because they replace a control's whole
        // appearance rather than adjusting one: the touch scroll indicator has no arrows and
        // no paging regions to adjust (audit 3, B5).
        foreach (var (key, controlTheme) in Theme.ProductTheme.BuildControlThemes())
        {
            Resources.Add(key, controlTheme);
        }

        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var startupPaths = ParseStartupPaths(desktop.Args ?? []);
            desktop.MainWindow = new MainWindow(new MainView(startupPaths));
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activity)
        {
            activity.MainViewFactory = static () => new MainView();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            single.MainView = new MainView();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IEnumerable<string> ParseStartupPaths(string[] arguments)
    {
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (argument is "--session" or "--log")
            {
                if (index + 1 < arguments.Length)
                {
                    yield return arguments[++index];
                }

                continue;
            }

            if (!argument.StartsWith('-'))
            {
                yield return argument;
            }
        }
    }
}

public sealed class MainWindow : Window
{
    public MainWindow(MainView? mainView = null)
    {
        var view = mainView ?? new MainView();
        Title = "VisualCat v2 — See the shape of your log";
        Width = 1440;
        Height = 900;
        MinWidth = 900;
        MinHeight = 600;
        using (var iconStream = AssetLoader.Open(new Uri("avares://VisualCat.App/Assets/visualcat-icon.png")))
        {
            Icon = new WindowIcon(iconStream);
        }
        Content = view;
        view.AttachHostWindow(this);
        Platform.FullRepaintOnResize.Attach(this);

        // The macOS menu bar, before the window is shown. Avalonia's macOS exporter reads a
        // window's menu as the window becomes key, so a menu set afterwards is a value nothing
        // looks at again and the stock two-item bar stays (finding F-03).
        view.InstallMacMenuBar(this);

        // A window with no focused control is one a screen reader reads out whole: Orca
        // announced the frame and then the entire workspace — the notice, the strapline, every
        // count, the template list and the entry list — as a single utterance, because there
        // was nothing inside holding focus for it to announce instead (U-07). It is the same
        // shape as a dialog opening without focus, and the same answer: put focus on the first
        // thing a reader would reach with Tab anyway.
        Opened += (_, _) => Dispatcher.UIThread.Post(
            () =>
            {
                if (FocusManager?.GetFocusedElement() is Visual focused &&
                    focused.FindAncestorOfType<Window>(includeSelf: true) == this)
                {
                    return;
                }

                this.GetVisualDescendants()
                    .OfType<InputElement>()
                    .FirstOrDefault(static element =>
                        element.Focusable && element.IsEffectivelyEnabled && element.IsEffectivelyVisible)
                    ?.Focus();
            },
            DispatcherPriority.Loaded);

        // A minimized window is the desktop's version of a screen that has turned off:
        // the capture must keep running, but re-running the heat map, overview,
        // statistics and search every few seconds produces a frame nobody can see.
        // Restoring brings every live tab straight up to date.
        PropertyChanged += (_, change) =>
        {
            if (change.Property == WindowStateProperty)
            {
                Platform.PlatformSourceRegistry.PublishWindowVisibility(WindowState != WindowState.Minimized);
            }
        };
        Closed += async (_, _) =>
        {
            // First, and synchronously. This is what the next launch reads to decide whether to
            // say "VisualCat closed unexpectedly" (F-17), and the two awaits below are enough
            // for the process to be gone before a continuation runs: ⌘Q on macOS left the
            // marker behind and every subsequent launch reported a crash that never happened.
            // The window is closing either way, so recording the exit here rather than at the
            // very end costs nothing and is the only placement that actually runs.
            MainView.MarkWorkspaceClosed();
            await view.PersistWindowStateAsync();
            await view.DisposeAsync();
        };
    }
}
