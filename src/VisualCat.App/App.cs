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
        Styles.Add(Theme.ProductTheme.CreateFluentTheme());
        foreach (var style in Theme.ProductTheme.BuildStyles())
        {
            Styles.Add(style);
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
