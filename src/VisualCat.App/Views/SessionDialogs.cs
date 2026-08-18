using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using VisualCat.Infrastructure.Configuration;

namespace VisualCat.App.Views;

/// <summary>
/// A settings value and the words a reader should see for it.
/// </summary>
/// <remarks>
/// Two combo boxes were populated with the stored identifiers themselves — <c>PerRow</c>,
/// <c>GlobalViewport</c>, <c>SourceSequence</c> — so a settings sheet read as source code
/// (finding 15b). The stored value has to stay exactly what it was, because it is written to
/// settings.json and validated there, so the label travels beside it instead of replacing it.
/// </remarks>
internal sealed record SettingChoice(string Value, string Label)
{
    public override string ToString() => Label;

    internal static SettingChoice[] Of(params (string Value, string Label)[] choices) =>
        [.. choices.Select(static choice => new SettingChoice(choice.Value, choice.Label))];

    internal static SettingChoice Resolve(SettingChoice[] choices, string? value) =>
        Array.Find(choices, choice => string.Equals(choice.Value, value, StringComparison.Ordinal))
        ?? choices[0];
}

public sealed class RecentSessionsDialog : DialogBody<string>
{
    private readonly ListBox _sessions = new();

    public RecentSessionsDialog(IReadOnlyList<TemporarySessionInfo> sessions)
        : base("Recent VisualCat sessions")
    {
        PreferredSize = new Size(760, 480);
        MinimumSize = new Size(560, 320);
        ScrollsInternally = true;
        var mobile = OperatingSystem.IsAndroid();
        _sessions.ItemsSource = sessions;
        _sessions.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<TemporarySessionInfo>((session, _) =>
            session is null
                ? new TextBlock()
                : new StackPanel
                {
                    Margin = new Thickness(5, mobile ? 8 : 5),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = SessionCacheName.Describe(session.Path),
                            FontWeight = FontWeight.Bold,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        },
                        new TextBlock
                        {
                            Text = $"{session.UpdatedUtc.ToLocalTime():g} · {FormatBytes(session.SizeBytes)} · " +
                                   (session.Finalized ? "ready" : "partial"),
                            Opacity = 0.75,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        },
                    },
                });
        _sessions.DoubleTapped += (_, _) => OpenSelected();

        // On a touch device a tap on a row opens that session, which is what the same list of
        // captures already does on the welcome screen; having one gesture mean "select" here
        // and "open" there was the whole of finding 16's confusion. The Cancel/Open row below
        // stays for the keyboard and for a reader who wants to look before committing.
        if (mobile)
        {
            _sessions.Tapped += OnSessionTapped;
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var cancel = new Button { Content = "Cancel", MinHeight = mobile ? 48 : 0 };
        cancel.Click += (_, _) => Complete(null);
        buttons.Children.Add(cancel);
        var open = new Button { Content = "Open", IsDefault = true, MinHeight = mobile ? 48 : 0 };
        open.Click += (_, _) => OpenSelected();
        buttons.Children.Add(open);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(12),
        };
        root.Children.Add(new TextBlock
        {
            // The desktop sentence pointed at Save session…, a command the Android build does
            // not register at all, so it described an action the reader could not take
            // (finding 13).
            Text = mobile
                ? "These captures are stored in this app's private storage. Share… hands one to "
                + "another app as a portable archive."
                : "Temporary sessions are stored locally. Saving a session promotes it to a "
                + "location you choose.",
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
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

    private void OnSessionTapped(object? sender, TappedEventArgs eventArgs)
    {
        if (eventArgs.Source is Visual source &&
            source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: TemporarySessionInfo session })
        {
            Complete(session.Path);
        }
    }

    private void OpenSelected()
    {
        if (_sessions.SelectedItem is TemporarySessionInfo session)
        {
            Complete(session.Path);
        }
    }
}

public sealed class AppearanceDialog : DialogBody<ApplicationSettings>
{
    private static readonly SettingChoice[] ThemeChoices = SettingChoice.Of(
        ("System", "Follow the system"),
        ("Light", "Light"),
        ("Dark", "Dark"));

    private static readonly SettingChoice[] IntensityChoices = SettingChoice.Of(
        ("Logarithmic", "Logarithmic"),
        ("SquareRoot", "Square root"),
        ("Linear", "Linear"));

    private static readonly SettingChoice[] NormalizationChoices = SettingChoice.Of(
        ("PerRow", "Per severity row"),
        ("GlobalViewport", "Whole viewport"));

    private static readonly SettingChoice[] ExportOrderChoices = SettingChoice.Of(
        ("SourceSequence", "Source order"),
        ("Chronological", "Chronological"));

    private static readonly SettingChoice[] ExportEncodingChoices = SettingChoice.Of(
        ("utf-8-bom", "UTF-8 with byte-order mark"),
        ("utf-8", "UTF-8"));

    private static readonly bool Mobile = OperatingSystem.IsAndroid();

    private readonly ComboBox _theme = Choices(ThemeChoices);
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
    private readonly ComboBox _intensity = Choices(IntensityChoices);
    private readonly ComboBox _normalization = Choices(NormalizationChoices);
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
    private readonly ComboBox _exportOrder = Choices(ExportOrderChoices);
    private readonly ComboBox _exportEncoding = Choices(ExportEncodingChoices);
    private readonly CheckBox _pixelSnap = new() { Content = "Snap timeline cells to device pixels" };
    private readonly CheckBox _diagnostics = new() { Content = "Write redacted structured diagnostics" };
    private readonly ApplicationSettings _settings;

    public AppearanceDialog(ApplicationSettings settings)
        : base("Appearance & timeline")
    {
        _settings = settings;
        PreferredSize = new Size(520, 820);
        MinimumSize = new Size(420, 620);
        ScrollsInternally = true;
        _theme.SelectedItem = SettingChoice.Resolve(ThemeChoices, settings.Theme);
        _highContrast.IsChecked = settings.HighContrast;
        _textScale.Value = (decimal)settings.TextScale;
        _adbPath.Text = settings.AdbPath;
        _sessionDirectory.Text = settings.SessionDirectory;
        _captureBuffers.Text = string.Join(',', settings.DefaultCaptureBuffers ?? ["main", "system", "crash"]);
        _preRollSeconds.Value = settings.DefaultCapturePreRollSeconds;
        _uiRefresh.Value = settings.UiRefreshLimit;
        _intensity.SelectedItem = SettingChoice.Resolve(IntensityChoices, settings.IntensityScale);
        _normalization.SelectedItem = SettingChoice.Resolve(NormalizationChoices, settings.TimelineNormalization);
        _minimumUsPerPixel.Value = (decimal)settings.TimelineMinimumUsPerPixel;
        _minimumBarWidth.Value = (decimal)settings.TimelineMinimumBarWidth;
        _exportOrder.SelectedItem = SettingChoice.Resolve(ExportOrderChoices, settings.ExportOrder);
        _exportEncoding.SelectedItem = SettingChoice.Resolve(ExportEncodingChoices, settings.ExportEncoding);
        _pixelSnap.IsChecked = settings.TimelinePixelSnap;
        _diagnostics.IsChecked = settings.DiagnosticsEnabled;

        var form = new StackPanel { Spacing = 8 };
        form.Children.Add(new TextBlock { Text = "Theme" });
        form.Children.Add(_theme);
        form.Children.Add(_highContrast);
        form.Children.Add(new TextBlock { Text = "Text scale" });
        form.Children.Add(_textScale);

        // The Android companion reads this device's log directly: there is no adb binary to
        // point at, no adb capture to pre-roll, and no filesystem path outside the sandbox a
        // reader could name — three settings that could be changed and could never apply
        // (finding 13).
        if (!Mobile)
        {
            form.Children.Add(new TextBlock { Text = "ADB executable" });
            form.Children.Add(_adbPath);
            form.Children.Add(new TextBlock { Text = "Default capture buffers (comma separated)" });
            form.Children.Add(_captureBuffers);
            form.Children.Add(new TextBlock { Text = "Default ADB pre-roll (seconds)" });
            form.Children.Add(_preRollSeconds);
            form.Children.Add(new TextBlock { Text = "Temporary session directory" });
            form.Children.Add(_sessionDirectory);
        }

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
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
        });
        form.Children.Add(_pixelSnap);
        form.Children.Add(new TextBlock { Text = "Minimum bar width" });
        form.Children.Add(_minimumBarWidth);
        form.Children.Add(new TextBlock
        {
            Text = "Isolated bursts are widened to this many pixels so they stay visible and clickable. " +
                   "Dense regions keep their exact one-pixel-per-column shape.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
        });
        form.Children.Add(new TextBlock { Text = "Default export order" });
        form.Children.Add(_exportOrder);
        form.Children.Add(new TextBlock { Text = "Normalized CSV encoding" });
        form.Children.Add(_exportEncoding);
        form.Children.Add(_diagnostics);
        form.Children.Add(new TextBlock
        {
            Text = Mobile
                ? "Changes apply to the whole application and are stored only on this device."
                : "Changes apply to the whole application and are stored only on this computer.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        });

        foreach (var control in form.Children.OfType<Control>())
        {
            StretchForTouch(control);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var cancel = new Button { Content = "Cancel", MinHeight = Mobile ? 48 : 0 };
        cancel.Click += (_, _) => Complete(null);
        buttons.Children.Add(cancel);
        var save = new Button { Content = "Apply", IsDefault = true, MinHeight = Mobile ? 48 : 0 };
        save.Click += (_, _) => Complete(_settings with
        {
            Theme = Value(_theme, ThemeChoices),
            HighContrast = _highContrast.IsChecked == true,
            TextScale = (double)(_textScale.Value ?? 1m),
            AdbPath = NullIfWhiteSpace(_adbPath.Text),
            SessionDirectory = NullIfWhiteSpace(_sessionDirectory.Text),
            DefaultCaptureBuffers = (_captureBuffers.Text ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            DefaultCapturePreRollSeconds = (int)(_preRollSeconds.Value ?? 0),
            UiRefreshLimit = (int)(_uiRefresh.Value ?? 30),
            IntensityScale = Value(_intensity, IntensityChoices),
            TimelineNormalization = Value(_normalization, NormalizationChoices),
            TimelineMinimumUsPerPixel = (double)(_minimumUsPerPixel.Value ?? 1m),
            TimelinePixelSnap = _pixelSnap.IsChecked == true,
            TimelineMinimumBarWidth = (double)(_minimumBarWidth.Value ?? 3m),
            ExportOrder = Value(_exportOrder, ExportOrderChoices),
            ExportEncoding = Value(_exportEncoding, ExportEncodingChoices),
            DiagnosticsEnabled = _diagnostics.IsChecked == true,
        });
        buttons.Children.Add(save);

        // The form scrolls; the decision does not. Apply and Cancel used to scroll away with
        // the twenty controls above them (finding 16 / 21.4).
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(16) };
        root.Children.Add(new ScrollViewer
        {
            Content = form,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        Content = root;
    }

    private static ComboBox Choices(SettingChoice[] choices) => new()
    {
        ItemsSource = choices,
        Width = 180,
        MinHeight = Mobile ? 48 : 0,
    };

    private static string Value(ComboBox box, SettingChoice[] fallback) =>
        (box.SelectedItem as SettingChoice ?? fallback[0]).Value;

    /// <summary>
    /// A 180 px control in a 380 px sheet leaves its own spinner buttons against the edge.
    /// On a phone every field takes the width it is given (finding 12.2).
    /// </summary>
    private static void StretchForTouch(Control control)
    {
        if (!Mobile)
        {
            return;
        }

        switch (control)
        {
            case ComboBox or NumericUpDown or TextBox:
                control.Width = double.NaN;
                control.HorizontalAlignment = HorizontalAlignment.Stretch;
                control.MinHeight = Math.Max(48, control.MinHeight);
                break;
            case CheckBox:
                control.MinHeight = Math.Max(48, control.MinHeight);
                break;
            default:
                break;
        }
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class SessionCacheDialog : DialogBody<ApplicationSettings>
{
    private static readonly bool Mobile = OperatingSystem.IsAndroid();
    private readonly string _cacheRoot;
    private readonly ApplicationSettings _settings;
    private readonly CheckBox _enabled = new() { Content = "Enable automatic temporary-session cleanup" };
    private readonly NumericUpDown _days = new() { Minimum = 1, Maximum = 3650, Increment = 1, Width = 140 };
    private readonly NumericUpDown _maximumGiB = new() { Minimum = 0, Maximum = 1024, Increment = 1, Width = 140 };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListBox _sessions = new();

    public SessionCacheDialog(string cacheRoot, ApplicationSettings settings)
        : base("Temporary session cache")
    {
        _cacheRoot = Path.GetFullPath(cacheRoot);
        _settings = settings;
        PreferredSize = new Size(780, 590);
        MinimumSize = new Size(620, 460);
        ScrollsInternally = true;
        _enabled.IsChecked = settings.TemporaryCleanupEnabled;
        _days.Value = settings.TemporaryRetentionDays;
        _maximumGiB.Value = settings.TemporaryRetentionMaximumBytes is { } bytes
            ? Math.Max(1, Math.Round((decimal)bytes / (1024 * 1024 * 1024)))
            : 0;

        // Name on one line, date and size on the next. One line held all three, so the row
        // that most needed reading — the largest session — was the one whose size was cut
        // mid-number with nothing saying anything was missing (finding 12.3).
        _sessions.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<TemporarySessionInfo>((session, _) =>
            session is null
                ? new TextBlock()
                : new StackPanel
                {
                    Margin = new Thickness(4, Mobile ? 7 : 4),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = SessionCacheName.Describe(session.Path),
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        },
                        new TextBlock
                        {
                            Text = $"{session.UpdatedUtc.ToLocalTime():g} · " +
                                   RecentSessionsDialog.FormatBytes(session.SizeBytes),
                            Opacity = 0.75,
                            FontSize = 11.5,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        },
                    },
                });

        var policy = BuildPolicyForm();

        var buttons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 8,
            LineSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var clean = new Button { Content = "Delete eligible sessions…", MinHeight = Mobile ? 48 : 0 };
        clean.Click += async (_, _) => await CleanAsync();
        buttons.Children.Add(clean);
        var cancel = new Button { Content = "Cancel", MinHeight = Mobile ? 48 : 0 };
        cancel.Click += (_, _) => Complete(null);
        buttons.Children.Add(cancel);
        var save = new Button { Content = "Save policy", IsDefault = true, MinHeight = Mobile ? 48 : 0 };
        save.Click += (_, _) => Complete(CurrentSettings());
        buttons.Children.Add(save);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            Margin = new Thickness(14),
        };
        root.Children.Add(new TextBlock
        {
            Text = $"Cache location: {_cacheRoot}",
            TextWrapping = TextWrapping.Wrap,
            FontSize = Mobile ? 11.5 : 12,
            Opacity = 0.8,
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
    }

    /// <summary>
    /// Label above field on a phone, label beside field on the desktop.
    /// </summary>
    /// <remarks>
    /// "Maximum total size (GiB, 0 = unlimited)" and its numeric field shared one row of a
    /// 380 px sheet: the label's closing parenthesis ran underneath the field's value and the
    /// row rendered as <c>…unlimite(0)</c>, with the spinner's chevron cut off by the sheet's
    /// own edge (finding 12.1 and 12.2).
    /// </remarks>
    private Grid BuildPolicyForm()
    {
        var policy = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(Mobile ? "*" : "Auto,*"),
            RowDefinitions = new RowDefinitions(Mobile ? "Auto,Auto,Auto,Auto,Auto" : "Auto,Auto,Auto"),
            ColumnSpacing = 10,
            RowSpacing = Mobile ? 4 : 2,
        };
        Grid.SetColumnSpan(_enabled, Mobile ? 1 : 2);
        if (Mobile)
        {
            _enabled.MinHeight = 48;
        }

        policy.Children.Add(_enabled);
        AddPolicyRow(policy, "Maximum age (days)", _days, firstRow: 1);
        AddPolicyRow(policy, "Maximum total size (GiB, 0 = unlimited)", _maximumGiB, firstRow: Mobile ? 3 : 2);
        return policy;
    }

    private static void AddPolicyRow(Grid policy, string text, NumericUpDown field, int firstRow)
    {
        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = Mobile ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        Grid.SetRow(label, firstRow);
        policy.Children.Add(label);
        Grid.SetRow(field, Mobile ? firstRow + 1 : firstRow);
        Grid.SetColumn(field, Mobile ? 0 : 1);
        if (Mobile)
        {
            field.Width = double.NaN;
            field.HorizontalAlignment = HorizontalAlignment.Stretch;
            field.MinHeight = 48;
        }

        AutomationProperties.SetName(field, text);
        policy.Children.Add(field);
    }

    /// <summary>The cache is scanned once the dialog is on screen, not while it is built.</summary>
    protected override void OnPresented() => _ = RefreshAsync();

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

        var confirmed = await ShowNestedAsync(new ConfirmationDialog(
            "Delete eligible temporary sessions?",
            "Sessions older than the configured age, and oldest sessions above the size cap, will be permanently removed."));
        if (confirmed != true)
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

public sealed class ConfirmationDialog : DialogBody<bool>
{
    public ConfirmationDialog(string title, string message, string confirmText = "Delete")
        : base(title)
    {
        PreferredSize = new Size(480, 230);
        MinimumSize = new Size(360, 200);
        var mobile = OperatingSystem.IsAndroid();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinHeight = mobile ? 48 : 0 };
        cancel.Click += (_, _) => Complete(false);
        buttons.Children.Add(cancel);
        var confirm = new Button { Content = confirmText, IsDefault = true, MinHeight = mobile ? 48 : 0 };
        confirm.Click += (_, _) => Complete(true);
        buttons.Children.Add(confirm);
        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 18,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                buttons,
            },
        };
    }
}
