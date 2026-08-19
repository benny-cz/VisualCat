using System.Globalization;
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

/// <summary>
/// The parts a settings sheet needs in order to stop half-drawing its last control.
/// </summary>
internal static class SheetForm
{
    /// <summary>
    /// What the three words a stored session can be described with mean.
    /// </summary>
    /// <remarks>
    /// This is a good sentence that nobody could read. It was attached as a tooltip and as
    /// accessibility help text, and Android has no pointer — so the tooltip could never appear,
    /// and a sighted touch user had no route to the explanation at all (audit 3, E2). It is on
    /// the sheet now, under the list it explains, which is the same treatment the status row's
    /// chevron got for the same defect.
    /// </remarks>
    internal const string SessionStateHelp =
        "A complete capture was stopped or finished normally and holds everything it recorded. " +
        "An interrupted one stopped without being told to — the app was killed, or the device " +
        "restarted — and opens with whatever reached the disk; nothing after that point was " +
        "kept. A capture in progress is one this app is recording into right now.";

    /// <summary>
    /// The second line of a stored session: when, how large, and whether it is finished.
    /// </summary>
    /// <remarks>
    /// Two lists showed the same three sessions in two vocabularies. The empty state printed
    /// "· partial" for an unfinished one and said nothing at all for a finished one; Recent
    /// sessions printed "· ready" for the very same finished one (audit 2, E2). Neither word
    /// was explained anywhere in the product (audit 2, E3), and neither is a word a reader
    /// brings with them — "ready" in particular says nothing about what it is ready for. One
    /// sentence, in one place, used by every list.
    /// </remarks>
    internal static string DescribeSessionState(TemporarySessionInfo session, bool capturingNow = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        return $"{session.UpdatedUtc.ToLocalTime():g} · " +
               $"{RecentSessionsDialog.FormatBytes(session.SizeBytes)} · " +
               DescribeSessionOutcome(session, capturingNow);
    }

    /// <summary>
    /// The last word of a stored session's line: what happened to it, in the right tense.
    /// </summary>
    /// <remarks>
    /// "Still being written" is a faithful rendering of <c>finalized == false</c> and the wrong
    /// tense for it: it says a process is writing to the file at this moment. Any capture that
    /// ends other than through Stop capture gets that state permanently, so a session from the
    /// previous day, whose files nothing had touched for 26 hours, was described as one
    /// currently being recorded — and a reader had no way to tell it from a capture that
    /// genuinely was (audit 3, E1). A past state gets a past tense, and the present tense is
    /// kept for the one case that is actually present, which the app knows because it is the
    /// one doing the recording.
    ///
    /// One word rather than two. "incomplete · interrupted" says the same thing twice and was
    /// one character too wide for the longest of these lines on a phone, so the row that most
    /// needed the word ended on "interrupt…". What the state means is on the sheet now
    /// (see <see cref="SessionStateHelp"/>), which is where it belongs.
    /// </remarks>
    internal static string DescribeSessionOutcome(TemporarySessionInfo session, bool capturingNow)
    {
        ArgumentNullException.ThrowIfNull(session);
        return capturingNow
            ? "capture in progress"
            : session.Finalized ? "complete" : "interrupted";
    }

    /// <summary>
    /// What a screen reader should hear for one stored session.
    /// </summary>
    /// <remarks>
    /// A <see cref="ListBoxItem"/> with no name of its own falls back to its content's
    /// <c>ToString()</c>, and the content here is a record whose generated one reads out the
    /// private storage path and the 32-hex materialisation guid — the two things the first
    /// audit worked to keep out of every user-visible name — followed by a raw byte count
    /// and <c>Finalized = True</c> (audit 2, B1). Sighted readers see a clean two-line row;
    /// this is the same two lines, spoken.
    /// </remarks>
    internal static string DescribeSessionRow(TemporarySessionInfo session, bool capturingNow = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        return $"{SessionCacheName.Describe(session.Path)}, " +
               DescribeSessionState(session, capturingNow).Replace(" · ", ", ", StringComparison.Ordinal);
    }

    /// <summary>
    /// The explanation of the state vocabulary, as a line of the sheet rather than as metadata.
    /// </summary>
    /// <remarks>
    /// See <see cref="SessionStateHelp"/>: on a platform with no pointer, a tooltip is a string
    /// that exists and can never be shown.
    /// </remarks>
    internal static TextBlock SessionStateLegend() => new()
    {
        Text = SessionStateHelp,
        TextWrapping = TextWrapping.Wrap,
        FontSize = TextScale.Of(11),
        Opacity = 0.75,
        Margin = new Thickness(0, 8, 0, 0),
    };

    /// <summary>
    /// Gives every realised row of <paramref name="list"/> a spoken name of its own.
    /// </summary>
    /// <remarks>
    /// <c>ContainerPrepared</c> rather than the item template, because the name belongs on
    /// the container and a recycled container is prepared again for its new item — which is
    /// what makes this survive virtualisation.
    /// </remarks>
    internal static void SpeakRows<T>(ListBox list, Func<T, string> describe)
    {
        list.ContainerPrepared += (_, eventArgs) =>
        {
            if (eventArgs.Container.DataContext is T item)
            {
                AutomationProperties.SetName(eventArgs.Container, describe(item));
            }
        };
        list.ContainerClearing += (_, eventArgs) =>
            AutomationProperties.SetName(eventArgs.Container, string.Empty);
    }

    /// <summary>
    /// Puts a scrolling body over a decision row that stays put, with an edge between them.
    /// </summary>
    /// <remarks>
    /// Both sheets pinned their Cancel/Apply row and then let the form run underneath it,
    /// so <em>Maximum zoom precision</em> and the third cached session were each cut in
    /// half against the buttons with nothing between them — which reads as a rendering
    /// fault rather than as "there is more below" (audit 2, D2). The rule is a rule of the
    /// sheet, not of each form: one hairline, one gap the last control can finish inside,
    /// and the scrollbar's own lane kept clear of the content (audit 2, A3).
    /// </remarks>
    internal static Grid Build(Control body, Control decision, Thickness margin)
    {
        var scroller = new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,

            // Bottom padding is what lets the last field finish above the divider instead
            // of ending against it; the top matches so the first field is not flush either.
            Padding = new Thickness(0, 2, 0, 10),
        };
        var divider = new Border
        {
            Height = 1,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 0, 0, 8),
        };
        divider[!TemplatedControl.BorderBrushProperty] =
            new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(
                VisualCat.App.Theme.ProductTheme.BorderLineKey);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
            Margin = margin,
        };
        root.Children.Add(scroller);
        Grid.SetRow(divider, 1);
        root.Children.Add(divider);
        Grid.SetRow(decision, 2);
        root.Children.Add(decision);
        return root;
    }

    /// <summary>
    /// A decision row that keeps its confirm action on the same line as its cancel.
    /// </summary>
    /// <remarks>
    /// Session cache laid Delete eligible sessions… / Cancel / Save policy into a wrap panel
    /// on a 380 px sheet: the destructive action was the widest and came first, and the
    /// confirm was orphaned on a second line below the cancel (audit 2, D3). A destructive
    /// action is not a peer of the decision, so it moves to the far side of the row where
    /// convention puts it, and the pair that decides the sheet stays together on the right.
    /// </remarks>
    internal static Grid Decision(Control? destructive, Control cancel, Control confirm)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
        };
        if (destructive is not null)
        {
            destructive.HorizontalAlignment = HorizontalAlignment.Left;
            row.Children.Add(destructive);
        }

        Grid.SetColumn(cancel, 1);
        row.Children.Add(cancel);
        Grid.SetColumn(confirm, 2);
        row.Children.Add(confirm);
        return row;
    }

    /// <summary>
    /// Names a numeric field's own spin buttons.
    /// </summary>
    /// <remarks>
    /// Every increment and decrement button in the product announced itself as
    /// <c>Avalonia.Controls.PathIcon</c> — eight of them across two sheets — because a
    /// templated button with an icon and no text has nothing else to fall back on
    /// (audit 2, B3). The names are attached once the template exists, which is the first
    /// moment the buttons do.
    /// </remarks>
    internal static void NameSpinButtons(NumericUpDown field, string label)
    {
        // Not on TemplateApplied. The spin buttons are three templates deep — the field's own,
        // a validation content presenter's, and the ButtonSpinner's — and a content presenter
        // realises its child during measure, so when the outer template is applied there is
        // nothing yet to name. The first layout pass that produces them is the one that gets
        // them named, and the handler retires itself.
        void OnLayoutUpdated(object? sender, EventArgs eventArgs)
        {
            if (Name(field, label))
            {
                field.LayoutUpdated -= OnLayoutUpdated;
            }
        }

        field.LayoutUpdated += OnLayoutUpdated;
    }

    private static bool Name(NumericUpDown field, string label)
    {
        var named = 0;
        foreach (var button in field.GetVisualDescendants().OfType<Button>())
        {
            var increases = button.Name is { } name && name.Contains("Increase", StringComparison.OrdinalIgnoreCase);
            AutomationProperties.SetName(button, increases ? $"Increase {label}" : $"Decrease {label}");
            named++;
        }

        return named >= 2;
    }
}

public sealed class RecentSessionsDialog : DialogBody<string>
{
    private readonly ListBox _sessions = new();
    private readonly IReadOnlySet<string> _capturing;

    /// <param name="sessions">Every stored session, newest first.</param>
    /// <param name="capturing">
    /// The paths this app is recording into at this moment, so the one state that is actually
    /// present tense can be said in it (audit 3, E1).
    /// </param>
    public RecentSessionsDialog(IReadOnlyList<TemporarySessionInfo> sessions, IReadOnlySet<string>? capturing = null)
        : base("Recent VisualCat sessions")
    {
        _capturing = capturing ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                            Text = SheetForm.DescribeSessionState(session, IsCapturing(session)),
                            Opacity = 0.75,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        },
                    },
                });
        _sessions.DoubleTapped += (_, _) => OpenSelected();
        SheetForm.SpeakRows<TemporarySessionInfo>(
            _sessions,
            session => SheetForm.DescribeSessionRow(session, IsCapturing(session)));
        AutomationProperties.SetName(_sessions, "Stored sessions");

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

        // The dialog opens with nothing selected, and Open was enabled anyway: tapping it
        // was accepted and produced no result, no message and no state change (audit 2, C3).
        // It is now exactly as available as the session it would open.
        var open = new Button
        {
            Content = "Open",
            IsDefault = true,
            MinHeight = mobile ? 48 : 0,
            IsEnabled = false,
        };
        open.Click += (_, _) => OpenSelected();
        _sessions.SelectionChanged += (_, _) => open.IsEnabled = _sessions.SelectedItem is TemporarySessionInfo;
        buttons.Children.Add(open);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
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

        // Under the list, where a reader meets the words. It had existed only as a tooltip and
        // as help text, neither of which a sighted touch user can reach (audit 3, E2).
        var legend = SheetForm.SessionStateLegend();
        Grid.SetRow(legend, 2);
        root.Children.Add(legend);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);
        Content = root;
        AutomationProperties.SetHelpText(_sessions, SheetForm.SessionStateHelp);
    }

    private bool IsCapturing(TemporarySessionInfo session) => _capturing.Contains(session.Path);

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

    private readonly ChoiceSelector? _themeChoice;
    private readonly ChoiceSelector? _intensityChoice;
    private readonly ChoiceSelector? _normalizationChoice;
    private readonly ChoiceSelector? _exportOrderChoice;
    private readonly ChoiceSelector? _exportEncodingChoice;
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
        form.Children.Add(Pick("Theme", _theme, ThemeChoices, settings.Theme, out _themeChoice));
        form.Children.Add(_highContrast);
        form.Children.Add(new TextBlock { Text = "Text scale" });
        form.Children.Add(_textScale);
        form.Children.Add(new TextBlock
        {
            // The one control the OS setting does not already cover, now that it is
            // honoured: this multiplies it rather than replacing it (audit 2, B5).
            Text = "Multiplies the device's own text size setting.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
        });

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
        form.Children.Add(Pick("Timeline intensity scale", _intensity, IntensityChoices, settings.IntensityScale, out _intensityChoice));
        form.Children.Add(new TextBlock { Text = "Timeline normalization" });
        form.Children.Add(Pick("Timeline normalization", _normalization, NormalizationChoices, settings.TimelineNormalization, out _normalizationChoice));
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
        form.Children.Add(Pick("Default export order", _exportOrder, ExportOrderChoices, settings.ExportOrder, out _exportOrderChoice));
        form.Children.Add(new TextBlock { Text = "Normalized CSV encoding" });
        form.Children.Add(Pick("Normalized CSV encoding", _exportEncoding, ExportEncodingChoices, settings.ExportEncoding, out _exportEncodingChoice));
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

        SheetForm.NameSpinButtons(_textScale, "text scale");
        SheetForm.NameSpinButtons(_preRollSeconds, "default ADB pre-roll in seconds");
        SheetForm.NameSpinButtons(_uiRefresh, "live UI refresh limit in hertz");
        SheetForm.NameSpinButtons(_minimumUsPerPixel, "maximum zoom precision");
        SheetForm.NameSpinButtons(_minimumBarWidth, "minimum bar width");

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
            Theme = _themeChoice?.Value ?? Value(_theme, ThemeChoices),
            HighContrast = _highContrast.IsChecked == true,
            TextScale = (double)(_textScale.Value ?? 1m),
            AdbPath = NullIfWhiteSpace(_adbPath.Text),
            SessionDirectory = NullIfWhiteSpace(_sessionDirectory.Text),
            DefaultCaptureBuffers = (_captureBuffers.Text ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            DefaultCapturePreRollSeconds = (int)(_preRollSeconds.Value ?? 0),
            UiRefreshLimit = (int)(_uiRefresh.Value ?? 30),
            IntensityScale = _intensityChoice?.Value ?? Value(_intensity, IntensityChoices),
            TimelineNormalization = _normalizationChoice?.Value ?? Value(_normalization, NormalizationChoices),
            TimelineMinimumUsPerPixel = (double)(_minimumUsPerPixel.Value ?? 1m),
            TimelinePixelSnap = _pixelSnap.IsChecked == true,
            TimelineMinimumBarWidth = (double)(_minimumBarWidth.Value ?? 3m),
            ExportOrder = _exportOrderChoice?.Value ?? Value(_exportOrder, ExportOrderChoices),
            ExportEncoding = _exportEncodingChoice?.Value ?? Value(_exportEncoding, ExportEncodingChoices),
            DiagnosticsEnabled = _diagnostics.IsChecked == true,
        });
        buttons.Children.Add(save);

        // The form scrolls; the decision does not. Apply and Cancel used to scroll away with
        // the twenty controls above them (finding 16 / 21.4).
        Content = SheetForm.Build(form, buttons, new Thickness(16));
    }

    /// <summary>
    /// Presents a settings choice the way the platform can actually show it.
    /// </summary>
    /// <remarks>
    /// The desktop keeps its combo box: it has a pointer, a window, and no in-page sheet for
    /// a popup to go wrong inside. A phone gets segments; see <see cref="ChoiceSelector"/>
    /// for why (audit 2, A4).
    /// </remarks>
    private static Control Pick(
        string name,
        ComboBox desktop,
        SettingChoice[] choices,
        string? value,
        out ChoiceSelector? selector)
    {
        if (!Mobile)
        {
            selector = null;
            AutomationProperties.SetName(desktop, name);
            return desktop;
        }

        selector = new ChoiceSelector(name, choices, value);
        return selector;
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
    private readonly List<Control> _policyFields = [];
    private readonly IReadOnlySet<string> _capturing;

    /// <param name="cacheRoot">Where the app keeps temporary sessions.</param>
    /// <param name="settings">The retention policy to edit.</param>
    /// <param name="capturing">
    /// The paths this app is recording into at this moment (see <see cref="RecentSessionsDialog"/>).
    /// </param>
    public SessionCacheDialog(
        string cacheRoot,
        ApplicationSettings settings,
        IReadOnlySet<string>? capturing = null)
        : base("Temporary session cache")
    {
        _capturing = capturing ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                            Text = SheetForm.DescribeSessionState(session, IsCapturing(session)),
                            Opacity = 0.75,
                            FontSize = TextScale.Of(11.5),
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        },
                    },
                });

        SheetForm.SpeakRows<TemporarySessionInfo>(
            _sessions,
            session => SheetForm.DescribeSessionRow(session, IsCapturing(session)));
        AutomationProperties.SetName(_sessions, "Cached sessions");
        AutomationProperties.SetHelpText(_sessions, SheetForm.SessionStateHelp);

        var policy = BuildPolicyForm();

        // "Delete eligible sessions…" is destructive, so it is not a peer of Cancel/Save
        // and does not sit between them. On a phone it also carries the shorter label the
        // row can actually hold.
        var clean = new Button
        {
            Content = Mobile ? "Delete eligible…" : "Delete eligible sessions…",
            MinHeight = Mobile ? 48 : 0,
        };
        AutomationProperties.SetName(clean, "Delete eligible temporary sessions");
        clean.Click += async (_, _) => await CleanAsync();
        var cancel = new Button { Content = "Cancel", MinHeight = Mobile ? 48 : 0 };
        cancel.Click += (_, _) => Complete(null);
        var save = new Button { Content = "Save policy", IsDefault = true, MinHeight = Mobile ? 48 : 0 };
        save.Click += (_, _) => Complete(CurrentSettings());
        var buttons = SheetForm.Decision(clean, cancel, save);

        // The list is the only part of this sheet that can grow, so it is the only part
        // that scrolls; the cache location, the policy and the summary stay above it.
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto") };
        body.Children.Add(new TextBlock
        {
            Text = $"Cache location: {_cacheRoot}",
            TextWrapping = TextWrapping.Wrap,
            FontSize = TextScale.Of(Mobile ? 11.5 : 12),
            Opacity = 0.8,
        });
        Grid.SetRow(policy, 1);
        policy.Margin = new Thickness(0, 10);
        body.Children.Add(policy);
        Grid.SetRow(_summary, 2);
        body.Children.Add(_summary);
        Grid.SetRow(_sessions, 3);
        _sessions.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(_sessions);

        // The vocabulary, where the vocabulary is used (audit 3, E2).
        var legend = SheetForm.SessionStateLegend();
        Grid.SetRow(legend, 4);
        body.Children.Add(legend);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
            Margin = new Thickness(14),
        };
        root.Children.Add(body);
        var divider = new Border
        {
            Height = 1,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 10, 0, 8),
        };
        divider[!TemplatedControl.BorderBrushProperty] =
            new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(
                VisualCat.App.Theme.ProductTheme.BorderLineKey);
        Grid.SetRow(divider, 1);
        root.Children.Add(divider);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;

        // A policy field that cannot take effect says so by receding, instead of sitting
        // fully interactive under an unchecked switch that governs it (audit 2, D7).
        _enabled.IsCheckedChanged += (_, _) => ApplyPolicyAvailability();
        ApplyPolicyAvailability();
    }

    private void ApplyPolicyAvailability()
    {
        var enabled = _enabled.IsChecked == true;
        foreach (var field in _policyFields)
        {
            field.IsEnabled = enabled;
        }
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
    private bool IsCapturing(TemporarySessionInfo session) => _capturing.Contains(session.Path);

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

    private void AddPolicyRow(Grid policy, string text, NumericUpDown field, int firstRow)
    {
        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = Mobile ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        _policyFields.Add(label);
        _policyFields.Add(field);
        SheetForm.NameSpinButtons(field, text);
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
