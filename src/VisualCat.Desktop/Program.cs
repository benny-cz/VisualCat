using Avalonia;
using VisualCat.App;

namespace VisualCat.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<VisualCat.App.App>()
            .UsePlatformDetect()
            .UseSkia()
            .LogToTrace();
}
