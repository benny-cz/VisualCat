using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using VisualCat.App.Views;

namespace VisualCat.App;

public sealed class App : Avalonia.Application
{
    public static bool HighContrastEnabled { get; set; }

    public override void Initialize()
    {
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
            await view.PersistWindowStateAsync();
            await view.DisposeAsync();
        };
    }
}
