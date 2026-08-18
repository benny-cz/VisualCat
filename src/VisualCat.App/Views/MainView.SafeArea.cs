using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Media;

namespace VisualCat.App.Views;

/// <summary>
/// Android safe-area and system-surface integration for the main application shell.
/// </summary>
public sealed partial class MainView
{
    private IInsetsManager? _insetsManager;

    /// <summary>
    /// Uses Avalonia's Android inset integration rather than treating the status bar, display
    /// cutout, navigation bar, or IME as application layout. The top level paints edge to edge,
    /// while Avalonia keeps this main view's content inside the current safe area.
    /// </summary>
    /// <remarks>
    /// Before this was enabled, the Android window stopped at the cutout-safe edge. AppCompat's
    /// decor surface then showed through as a white band in dark mode, and accessibility nodes
    /// were reported in a coordinate space offset from the controls they represented. Avalonia
    /// 12 exposes both edge-to-edge preference and automatic safe-area padding through the
    /// top-level inset manager; using those APIs keeps rendering, hit testing, automation, and
    /// keyboard resizing in the same coordinate system.
    /// </remarks>
    private void ObserveSafeArea()
    {
        if (!OperatingSystem.IsAndroid())
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        // AttachedToVisualTree can run again after an Android surface recreation. Always
        // detach from the previous manager first so one MainView never accumulates duplicate
        // SafeAreaChanged subscriptions.
        StopObservingSafeArea();

        TopLevel.SetAutoSafeAreaPadding(this, true);
        _insetsManager = topLevel.InsetsManager;
        if (_insetsManager is { } manager)
        {
            manager.DisplayEdgeToEdgePreference = true;
            manager.SafeAreaChanged += OnSafeAreaChanged;
        }

        ApplySystemBarSurface();
        _ = WriteMainViewDiagnosticAsync(
            "android.safe-area.attached",
            "information",
            new Dictionary<string, string>
            {
                ["edgeToEdgeRequested"] = (_insetsManager?.DisplayEdgeToEdgePreference == true).ToString(),
                ["autoSafeAreaPadding"] = TopLevel.GetAutoSafeAreaPadding(this).ToString(),
            });
    }

    private void StopObservingSafeArea()
    {
        if (_insetsManager is { } manager)
        {
            manager.SafeAreaChanged -= OnSafeAreaChanged;
            _insetsManager = null;
        }
    }

    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs eventArgs)
    {
        _ = sender;
        _rootPanel.InvalidateMeasure();
        _rootPanel.InvalidateArrange();
        _overlayHost.InvalidateMeasure();
        _overlayHost.InvalidateArrange();

        _ = WriteMainViewDiagnosticAsync(
            "android.safe-area.changed",
            "information",
            new Dictionary<string, string>
            {
                ["left"] = eventArgs.SafeAreaPadding.Left.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                ["top"] = eventArgs.SafeAreaPadding.Top.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                ["right"] = eventArgs.SafeAreaPadding.Right.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                ["bottom"] = eventArgs.SafeAreaPadding.Bottom.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            });
    }

    /// <summary>Paints system-owned bars with the same dark shell surface as the command bar.</summary>
    private void ApplySystemBarSurface()
    {
        if (!OperatingSystem.IsAndroid())
        {
            return;
        }

        TopLevel.SetSystemBarColor(this, new SolidColorBrush(Color.Parse("#11151C")));
    }
}
