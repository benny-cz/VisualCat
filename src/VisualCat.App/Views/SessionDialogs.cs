using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using VisualCat.Infrastructure.Configuration;

namespace VisualCat.App.Views;

public sealed class RecentSessionsDialog : Window
{
    private readonly ListBox _sessions = new();

    public RecentSessionsDialog(IReadOnlyList<TemporarySessionInfo> sessions)
    {
        Title = "Recent VisualCat sessions";
        Width = 760;
        Height = 480;
        MinWidth = 560;
        MinHeight = 320;
        _sessions.ItemsSource = sessions;
        _sessions.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<TemporarySessionInfo>((session, _) =>
            session is null
                ? new TextBlock()
                : new StackPanel
                {
                    Margin = new Thickness(5),
                    Children =
                    {
                        new TextBlock { Text = Path.GetFileName(session.Path), FontWeight = Avalonia.Media.FontWeight.Bold },
                        new TextBlock
                        {
                            Text = $"{session.UpdatedUtc:g} · {FormatBytes(session.SizeBytes)} · {(session.Finalized ? "ready" : "partial")}",
                            Opacity = 0.75,
                        },
                    },
                });
        _sessions.DoubleTapped += (_, _) => OpenSelected();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close(null);
        buttons.Children.Add(cancel);
        var open = new Button { Content = "Open", IsDefault = true };
        open.Click += (_, _) => OpenSelected();
        buttons.Children.Add(open);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(12),
        };
        root.Children.Add(new TextBlock
        {
            Text = "Temporary sessions are stored locally. Saving a session promotes it to a location you choose.",
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        Grid.SetRow(_sessions, 1);
        root.Children.Add(_sessions);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
    }

    internal static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private void OpenSelected()
    {
        if (_sessions.SelectedItem is TemporarySessionInfo session)
        {
            Close(session.Path);
        }
    }
}

public sealed class AppearanceDialog : Window
{
    private readonly ComboBox _theme = new() { ItemsSource = new[] { "System", "Light", "Dark" }, Width = 180 };
    private readonly CheckBox _highContrast = new() { Content = "Prefer high-contrast presentation" };
    private readonly NumericUpDown _textScale = new()
    {
        Minimum = 0.75m,
        Maximum = 2m,
        Increment = 0.05m,
        FormatString = "0.00×",
        Width = 180,
    };
    private readonly TextBox _adbPath = new() { PlaceholderText = "Auto-detect when empty" };
    private readonly TextBox _sessionDirectory = new() { PlaceholderText = "Platform-local default when empty" };
    private readonly TextBox _captureBuffers = new() { PlaceholderText = "main,system,crash" };
    private readonly NumericUpDown _preRollSeconds = new() { Minimum = 0, Maximum = 3600, Increment = 5, Width = 180 };
    private readonly NumericUpDown _uiRefresh = new() { Minimum = 1, Maximum = 60, Increment = 1, Width = 180 };
    private readonly ComboBox _intensity = new() { ItemsSource = new[] { "Logarithmic", "SquareRoot", "Linear" }, Width = 180 };
    private readonly ComboBox _normalization = new()
    {
        ItemsSource = new[] { "PerRow", "GlobalViewport" },
        Width = 180,
    };
    private readonly NumericUpDown _minimumUsPerPixel = new()
    {
        Minimum = 0.1m,
        Maximum = 500m,
        Increment = 0.1m,
        FormatString = "0.0 µs/px",
        Width = 180,
    };
    private readonly NumericUpDown _minimumBarWidth = new()
    {
        Minimum = 1m,
        Maximum = 12m,
        Increment = 1m,
        FormatString = "0 px",
        Width = 180,
    };
    private readonly ComboBox _exportOrder = new() { ItemsSource = new[] { "SourceSequence", "Chronological" }, Width = 180 };
    private readonly ComboBox _exportEncoding = new() { ItemsSource = new[] { "utf-8-bom", "utf-8" }, Width = 180 };
    private readonly CheckBox _pixelSnap = new() { Content = "Snap timeline cells to device pixels" };
    private readonly CheckBox _diagnostics = new() { Content = "Write redacted structured diagnostics" };
    private readonly ApplicationSettings _settings;

    public AppearanceDialog(ApplicationSettings settings)
    {
        _settings = settings;
        Title = "Appearance";
        Width = 520;
        Height = 820;
        MinHeight = 620;
        _theme.SelectedItem = settings.Theme;
        _highContrast.IsChecked = settings.HighContrast;
        _textScale.Value = (decimal)settings.TextScale;
        _adbPath.Text = settings.AdbPath;
        _sessionDirectory.Text = settings.SessionDirectory;
        _captureBuffers.Text = string.Join(',', settings.DefaultCaptureBuffers ?? ["main", "system", "crash"]);
        _preRollSeconds.Value = settings.DefaultCapturePreRollSeconds;
        _uiRefresh.Value = settings.UiRefreshLimit;
        _intensity.SelectedItem = settings.IntensityScale;
        _normalization.SelectedItem = settings.TimelineNormalization;
        _minimumUsPerPixel.Value = (decimal)settings.TimelineMinimumUsPerPixel;
        _minimumBarWidth.Value = (decimal)settings.TimelineMinimumBarWidth;
        _exportOrder.SelectedItem = settings.ExportOrder;
        _exportEncoding.SelectedItem = settings.ExportEncoding;
        _pixelSnap.IsChecked = settings.TimelinePixelSnap;
        _diagnostics.IsChecked = settings.DiagnosticsEnabled;

        var form = new StackPanel { Spacing = 8 };
        form.Children.Add(new TextBlock { Text = "Theme" });
        form.Children.Add(_theme);
        form.Children.Add(_highContrast);
        form.Children.Add(new TextBlock { Text = "Text scale" });
        form.Children.Add(_textScale);
        form.Children.Add(new TextBlock { Text = "ADB executable" });
        form.Children.Add(_adbPath);
        form.Children.Add(new TextBlock { Text = "Default capture buffers (comma separated)" });
        form.Children.Add(_captureBuffers);
        form.Children.Add(new TextBlock { Text = "Default ADB pre-roll (seconds)" });
        form.Children.Add(_preRollSeconds);
        form.Children.Add(new TextBlock { Text = "Temporary session directory" });
        form.Children.Add(_sessionDirectory);
        form.Children.Add(new TextBlock { Text = "Live UI refresh limit (Hz)" });
        form.Children.Add(_uiRefresh);
        form.Children.Add(new TextBlock { Text = "Timeline intensity scale" });
        form.Children.Add(_intensity);
        form.Children.Add(new TextBlock { Text = "Timeline normalization" });
        form.Children.Add(_normalization);
        form.Children.Add(new TextBlock { Text = "Maximum zoom precision" });
        form.Children.Add(_minimumUsPerPixel);
        form.Children.Add(new TextBlock
        {
            Text = "1 µs/px exposes source-level timing; increase this value on lower-powered devices.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Opacity = 0.72,
        });
        form.Children.Add(_pixelSnap);
        form.Children.Add(new TextBlock { Text = "Minimum bar width" });
        form.Children.Add(_minimumBarWidth);
        form.Children.Add(new TextBlock
        {
            Text = "Isolated bursts are widened to this many pixels so they stay visible and clickable. " +
                   "Dense regions keep their exact one-pixel-per-column shape.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Opacity = 0.72,
        });
        form.Children.Add(new TextBlock { Text = "Default export order" });
        form.Children.Add(_exportOrder);
        form.Children.Add(new TextBlock { Text = "Normalized CSV encoding" });
        form.Children.Add(_exportEncoding);
        form.Children.Add(_diagnostics);
        form.Children.Add(new TextBlock
        {
            Text = "Changes apply to the whole application and are stored only on this computer.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Opacity = 0.75,
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close(null);
        buttons.Children.Add(cancel);
        var save = new Button { Content = "Apply", IsDefault = true };
        save.Click += (_, _) => Close(_settings with
        {
            Theme = _theme.SelectedItem as string ?? "System",
            HighContrast = _highContrast.IsChecked == true,
            TextScale = (double)(_textScale.Value ?? 1m),
            AdbPath = NullIfWhiteSpace(_adbPath.Text),
            SessionDirectory = NullIfWhiteSpace(_sessionDirectory.Text),
            DefaultCaptureBuffers = (_captureBuffers.Text ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            DefaultCapturePreRollSeconds = (int)(_preRollSeconds.Value ?? 0),
            UiRefreshLimit = (int)(_uiRefresh.Value ?? 30),
            IntensityScale = _intensity.SelectedItem as string ?? "Logarithmic",
            TimelineNormalization = _normalization.SelectedItem as string ?? "PerRow",
            TimelineMinimumUsPerPixel = (double)(_minimumUsPerPixel.Value ?? 1m),
            TimelinePixelSnap = _pixelSnap.IsChecked == true,
            TimelineMinimumBarWidth = (double)(_minimumBarWidth.Value ?? 3m),
            ExportOrder = _exportOrder.SelectedItem as string ?? "SourceSequence",
            ExportEncoding = _exportEncoding.SelectedItem as string ?? "utf-8-bom",
            DiagnosticsEnabled = _diagnostics.IsChecked == true,
        });
        buttons.Children.Add(save);

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children = { form, buttons },
            },
        };
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class SessionCacheDialog : Window
{
    private readonly string _cacheRoot;
    private readonly ApplicationSettings _settings;
    private readonly CheckBox _enabled = new() { Content = "Enable automatic temporary-session cleanup" };
    private readonly NumericUpDown _days = new() { Minimum = 1, Maximum = 3650, Increment = 1, Width = 140 };
    private readonly NumericUpDown _maximumGiB = new() { Minimum = 0, Maximum = 1024, Increment = 1, Width = 140 };
    private readonly TextBlock _summary = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly ListBox _sessions = new();

    public SessionCacheDialog(string cacheRoot, ApplicationSettings settings)
    {
        _cacheRoot = Path.GetFullPath(cacheRoot);
        _settings = settings;
        Title = "Temporary session cache";
        Width = 780;
        Height = 590;
        MinWidth = 620;
        MinHeight = 460;
        _enabled.IsChecked = settings.TemporaryCleanupEnabled;
        _days.Value = settings.TemporaryRetentionDays;
        _maximumGiB.Value = settings.TemporaryRetentionMaximumBytes is { } bytes
            ? Math.Max(1, Math.Round((decimal)bytes / (1024 * 1024 * 1024)))
            : 0;
        _sessions.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<TemporarySessionInfo>((session, _) =>
            session is null
                ? new TextBlock()
                : new TextBlock
                {
                    Text = $"{Path.GetFileName(session.Path)} · {session.UpdatedUtc:g} · {RecentSessionsDialog.FormatBytes(session.SizeBytes)}",
                    Margin = new Thickness(4),
                });

        var policy = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
        Grid.SetColumnSpan(_enabled, 2);
        policy.Children.Add(_enabled);
        var daysLabel = new TextBlock { Text = "Maximum age (days)", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(daysLabel, 1);
        policy.Children.Add(daysLabel);
        Grid.SetRow(_days, 1);
        Grid.SetColumn(_days, 1);
        policy.Children.Add(_days);
        var sizeLabel = new TextBlock { Text = "Maximum total size (GiB, 0 = unlimited)", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(sizeLabel, 2);
        policy.Children.Add(sizeLabel);
        Grid.SetRow(_maximumGiB, 2);
        Grid.SetColumn(_maximumGiB, 1);
        policy.Children.Add(_maximumGiB);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var clean = new Button { Content = "Delete eligible sessions…" };
        clean.Click += async (_, _) => await CleanAsync();
        buttons.Children.Add(clean);
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close(null);
        buttons.Children.Add(cancel);
        var save = new Button { Content = "Save policy", IsDefault = true };
        save.Click += (_, _) => Close(CurrentSettings());
        buttons.Children.Add(save);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            Margin = new Thickness(14),
        };
        root.Children.Add(new TextBlock
        {
            Text = $"Cache location: {_cacheRoot}",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        Grid.SetRow(policy, 1);
        policy.Margin = new Thickness(0, 10);
        root.Children.Add(policy);
        Grid.SetRow(_summary, 2);
        root.Children.Add(_summary);
        Grid.SetRow(_sessions, 3);
        root.Children.Add(_sessions);
        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);
        Content = root;
        Opened += async (_, _) => await RefreshAsync();
    }

    private ApplicationSettings CurrentSettings()
    {
        var maximumGiB = _maximumGiB.Value ?? 0;
        return _settings with
        {
            TemporaryCleanupEnabled = _enabled.IsChecked == true,
            TemporaryRetentionDays = (int)(_days.Value ?? 30),
            TemporaryRetentionMaximumBytes = maximumGiB <= 0
                ? null
                : checked((long)maximumGiB * 1024 * 1024 * 1024),
        };
    }

    private async Task RefreshAsync()
    {
        var sessions = await TemporarySessionRetentionService.ScanAsync(_cacheRoot);
        _sessions.ItemsSource = sessions;
        _summary.Text = $"{sessions.Count:N0} temporary sessions · {RecentSessionsDialog.FormatBytes(sessions.Sum(static session => session.SizeBytes))}. " +
                        "Cleanup deletes whole session folders; it does not claim forensic erasure.";
    }

    private async Task CleanAsync()
    {
        if (_enabled.IsChecked != true)
        {
            _summary.Text = "Enable cleanup first. VisualCat never deletes temporary sessions under the default policy.";
            return;
        }

        var confirmation = new ConfirmationDialog(
            "Delete eligible temporary sessions?",
            "Sessions older than the configured age, and oldest sessions above the size cap, will be permanently removed.");
        if (!await confirmation.ShowDialog<bool>(this))
        {
            return;
        }

        var settings = CurrentSettings();
        var result = await TemporarySessionRetentionService.CleanupAsync(
            _cacheRoot,
            enabled: true,
            TimeSpan.FromDays(settings.TemporaryRetentionDays),
            settings.TemporaryRetentionMaximumBytes,
            DateTimeOffset.UtcNow);
        await RefreshAsync();
        _summary.Text = result.Errors.Count == 0
            ? $"Deleted {result.DeletedPaths.Count:N0} eligible sessions."
            : $"Deleted {result.DeletedPaths.Count:N0}; {result.Errors.Count:N0} could not be removed.";
    }
}

public sealed class ConfirmationDialog : Window
{
    public ConfirmationDialog(string title, string message, string confirmText = "Delete")
    {
        Title = title;
        Width = 480;
        Height = 210;
        CanResize = false;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        buttons.Children.Add(cancel);
        var confirm = new Button { Content = confirmText, IsDefault = true };
        confirm.Click += (_, _) => Close(true);
        buttons.Children.Add(confirm);
        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 18,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                buttons,
            },
        };
    }
}
