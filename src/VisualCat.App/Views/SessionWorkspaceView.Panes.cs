using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using VisualCat.App.Presentation;
using VisualCat.App.Timeline;
using VisualCat.Domain;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;

namespace VisualCat.App.Views;

public sealed partial class SessionWorkspaceView : UserControl
{
    /// <summary>Characters of a template a screen reader is given for one row.</summary>
    private const int TemplateSpokenTextLength = 240;

    /// <summary>What a screen reader should hear for one row of the Insights list.</summary>
    private string TemplateAutomationName(TemplateSummary template)
    {
        var text = template.CanonicalText.Length > TemplateSpokenTextLength
            ? template.CanonicalText[..TemplateSpokenTextLength] + "…"
            : template.CanonicalText;
        return $"{Counted.Entries(template.Count)}: {text.ReplaceLineEndings(" ")}, " +
               $"from {FormatInstant(template.First)} to {FormatInstant(template.Last)}";
    }

    private Control BuildTemplatePane()
    {
        // Without this the list inherits Fluent's touch-sized rows and only three or four
        // templates fit the pane; the same compact container the entry table uses shows a
        // useful ranking instead.
        _templates.Styles.Add(CompactItemStyle(_mobile ? 64 : 22));

        // The entries list got a spoken name per row and this one did not, so Insights read
        // out the TemplateSummary record's generated ToString() — template id, canonical
        // text, count, two round-trip timestamps and
        // "System.Collections.Generic.List`1[System.Int64]" (audit 2, B1). Set on the
        // container so it survives virtualisation, in the order the questions arrive: how
        // often, what it says, and over what span.
        _templates.ContainerPrepared += (_, eventArgs) =>
        {
            if (eventArgs.Container.DataContext is TemplateSummary prepared)
            {
                AutomationProperties.SetName(eventArgs.Container, TemplateAutomationName(prepared));
            }
        };
        _templates.ContainerClearing += (_, eventArgs) =>
            AutomationProperties.SetName(eventArgs.Container, string.Empty);
        AutomationProperties.SetName(_templates, "Repeated message templates");
        _templates.ItemTemplate = new FuncDataTemplate<TemplateSummary>((template, _) =>
        {
            if (template is null)
            {
                return new Grid();
            }

            // The metric gutter is fixed rather than Auto: Fluent's desktop ProgressBar has
            // a generous theme MinWidth which otherwise steals a surprising share of the
            // inspector from the canonical message. Exact counts remain in the tooltip and
            // accessibility name while the visible metric stays compact (§14.11).
            var metricTrackWidth = _mobile ? 40d : 44d;
            var metricWidth = _mobile ? 34d : 38d;
            var metricGap = 6d;
            var row = new Grid
            {
                Margin = new Thickness(2, 1),
                ColumnDefinitions = new ColumnDefinitions($"{metricTrackWidth},*"),
                RowDefinitions = new RowDefinitions("Auto,Auto"),
            };
            var exactCount = template.Count.ToString("N0", DisplayCulture.Current);
            var count = new TextBlock
            {
                Text = FormatTemplateCount(template.Count),
                FontFamily = MonoFont,
                FontWeight = FontWeight.SemiBold,
                FontSize = TextScale.Of(11),
                Width = metricWidth,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 0, metricGap, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            var matching = $"{Counted.Entries(template.Count)} matching";
            ToolTip.SetTip(count, matching);
            AutomationProperties.SetName(count, matching);
            row.Children.Add(count);
            var totalMatching = Math.Max(template.Count, _viewModel.Statistics?.TotalMatching ?? template.Count);

            // Scaled against the biggest template in the list, not against the whole matching
            // set. The busiest template in a real session is a couple of percent of it, so
            // every bar filled about one pixel of an 86 px track and six rows counting 864
            // down to 570 drew six identical rules (finding 18). This list exists to rank
            // templates against each other; the absolute share stays in the tooltip.
            var leader = Math.Max(1, LargestTemplateCount());
            var prevalence = new ProgressBar
            {
                Minimum = 0,
                Maximum = leader,
                Value = Math.Min(template.Count, leader),
                Width = metricWidth,
                MinWidth = 0,
                Height = 3,
                Margin = new Thickness(0, 4, metricGap, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Foreground = new SolidColorBrush(WorkspacePalette.Accent(
                    ActualThemeVariant != ThemeVariant.Light)),
            };
            ToolTip.SetTip(
                prevalence,
                $"{Counted.Entries(template.Count)} · {template.Count / (double)Math.Max(1, totalMatching):P1} of current matches · " +
                $"bar is relative to the largest template listed");
            AutomationProperties.SetName(
                prevalence,
                $"{template.Count / (double)Math.Max(1, totalMatching):P1} of current matches");
            Grid.SetRow(prevalence, 1);
            row.Children.Add(prevalence);
            var canonical = new TextBlock
            {
                Text = template.CanonicalText,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = _mobile ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MaxLines = _mobile ? 2 : 1,
            };
            Grid.SetColumn(canonical, 1);
            row.Children.Add(canonical);
            var span = new TextBlock
            {
                Text = $"{FormatInstant(template.First)} — {FormatInstant(template.Last)}",
                FontFamily = MonoFont,
                FontSize = TextScale.Of(10),
                Opacity = 0.6,
            };
            Grid.SetColumn(span, 1);
            Grid.SetRow(span, 1);
            row.Children.Add(span);
            return row;
        });

        // A WrapPanel so the three actions fold onto a second line rather than clipping in
        // the narrow insights column.
        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 6,
            LineSpacing = 6,
            Margin = new Thickness(4),
        };
        actions.Children.Add(CountScopeLabel(
            "COUNTS · THIS VIEW",
            "Template counts follow the current timeline viewport and active filter."));
        var include = _templateInclude = new Button
        {
            Content = _mobile ? "Filter" : "Filter to template",
            IsEnabled = false,
        };
        ToolTip.SetTip(include, "Show only entries matching the selected template");
        AutomationProperties.SetName(include, "Filter to selected template");
        include.Click += (_, _) =>
        {
            if (_templates.SelectedItem is TemplateSummary template)
            {
                _ = RunUiActionAsync(async () =>
                {
                    await _viewModel.IncludeTemplateAsync(template.TemplateId);

                    // The chip that records this lives on a bar the phone may not be showing —
                    // in Details mode it is off screen entirely — so the action says what it did
                    // where the reader is looking (audit 2, C4).
                    Notify("Filtered to this template. Clear it from the filter chips.");
                });
            }
        };
        actions.Children.Add(include);
        var exclude = _templateExclude = new Button
        {
            Content = _mobile ? "Mute" : "Mute template",
            IsEnabled = false,
        };
        ToolTip.SetTip(exclude, "Hide entries matching the selected template");
        AutomationProperties.SetName(exclude, "Mute selected template");
        exclude.Click += (_, _) =>
        {
            if (_templates.SelectedItem is TemplateSummary template)
            {
                _ = RunUiActionAsync(async () =>
                {
                    await _viewModel.ExcludeTemplateAsync(template.TemplateId);
                    Notify("Muted this template. Clear it from the filter chips.");
                });
            }
        };
        actions.Children.Add(exclude);
        var copy = _templateCopy = new Button
        {
            Content = _mobile ? "Copy" : "Copy template",
            IsEnabled = false,
        };
        ToolTip.SetTip(copy, "Copy the selected template text");
        AutomationProperties.SetName(copy, "Copy selected template");
        copy.Click += async (_, _) =>
        {
            if (_templates.SelectedItem is TemplateSummary template &&
                TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(template.CanonicalText);
                Notify("Copied the template text.");
            }
        };
        actions.Children.Add(copy);
        EnsureMobileTouch(include, exclude, copy);
        _templates.SelectionChanged += (_, _) =>
        {
            var hasSelection = _templates.SelectedItem is TemplateSummary;
            include.IsEnabled = hasSelection;
            exclude.IsEnabled = hasSelection;
            copy.IsEnabled = hasSelection;
        };

        var templateGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        templateGrid.Children.Add(actions);
        Grid.SetRow(_templates, 1);
        templateGrid.Children.Add(_templates);

        var viewsPane = BuildViewsPane();
        Control viewsDestination = viewsPane;
        if (_mobile)
        {
            viewsDestination = new ScrollViewer
            {
                Content = viewsPane,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            AutomationProperties.SetName(viewsDestination, "Saved views");
        }

        var panes = new Control[]
        {
            templateGrid,
            BuildFacetPane(),
            viewsDestination,
            BuildSessionPane(),
        };
        if (_mobile)
        {
            var destination = new ComboBox
            {
                ItemsSource = new[] { "Templates", "Facets", "Saved views", "Session info" },
                SelectedIndex = 0,
                MinHeight = 48,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetName(destination, "Insights destination");
            var host = new ContentControl { Content = panes[0] };
            destination.SelectionChanged += (_, _) =>
            {
                if (destination.SelectedIndex is >= 0 and < 4)
                {
                    host.Content = panes[destination.SelectedIndex];
                }
            };
            var mobilePane = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                Margin = new Thickness(6, 2, 6, 4),
            };
            mobilePane.Children.Add(destination);
            Grid.SetRow(host, 1);
            mobilePane.Children.Add(host);
            return mobilePane;
        }

        return new TabControl
        {
            Items =
            {
                new TabItem { Header = "Templates", Content = panes[0] },
                new TabItem { Header = "Facets", Content = panes[1] },
                new TabItem { Header = "Views", Content = panes[2] },
                new TabItem { Header = "Session", Content = panes[3] },
            },
        };
    }

    /// <summary>
    /// The count the prevalence bars are scaled against: the busiest template currently
    /// listed. The list is capped at a few dozen rows and only re-realized when the query
    /// changes, so scanning it per row is cheaper than caching a value that can go stale
    /// against the collection it describes.
    /// </summary>
    private long LargestTemplateCount()
    {
        var largest = 0L;
        foreach (var template in _viewModel.Templates)
        {
            if (template.Count > largest)
            {
                largest = template.Count;
            }
        }

        return largest;
    }

    internal static string FormatTemplateCount(long count)
    {
        if (count < 1_000)
        {
            return count.ToString("N0", DisplayCulture.Current);
        }

        var scaled = (double)count;
        var unitIndex = 0;
        string[] units = ["", "k", "M", "B", "T"];
        while (scaled >= 999.5 && unitIndex < units.Length - 1)
        {
            scaled /= 1_000;
            unitIndex++;
        }

        var format = scaled switch
        {
            >= 100 => "0",
            >= 10 => "0.#",
            _ => "0.##",
        };
        return scaled.ToString(format, DisplayCulture.Current) + units[unitIndex];
    }

    /// <summary>
    /// Saved views, split into a clearly labelled "apply an existing view" half — now with a
    /// Delete so the list is manageable — and a "save the current view" half, instead of a
    /// bare stack of controls whose two jobs were indistinguishable (§14.11).
    /// </summary>
    private StackPanel BuildViewsPane()
    {
        var views = new StackPanel { Spacing = 8, Margin = new Thickness(8) };
        _savedViews.ItemsSource = _viewModel.SavedViews;
        _savedViews.HorizontalAlignment = HorizontalAlignment.Stretch;
        views.Children.Add(SectionLabel("Saved views"));
        views.Children.Add(_savedViews);
        var savedButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var apply = new Button { Content = "Apply" };
        apply.Click += async (_, _) =>
        {
            if (_savedViews.SelectedItem is string name)
            {
                await RunUiActionAsync(() => _viewModel.ApplySavedViewAsync(name));
            }
        };
        savedButtons.Children.Add(apply);
        var delete = new Button { Content = "Delete" };
        delete.Click += async (_, _) =>
        {
            if (_savedViews.SelectedItem is string name)
            {
                await RunUiActionAsync(() => _viewModel.DeleteSavedViewAsync(name));
            }
        };
        savedButtons.Children.Add(delete);
        views.Children.Add(savedButtons);

        views.Children.Add(SectionLabel("Save current view as"));
        _viewName.Width = double.NaN;
        _viewName.HorizontalAlignment = HorizontalAlignment.Stretch;
        views.Children.Add(_viewName);
        var save = new Button { Content = "Save", HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += async (_, _) => await RunUiActionAsync(
            () => _viewModel.SaveCurrentViewAsync(_viewName.Text ?? string.Empty));
        views.Children.Add(save);
        EnsureMobileTouch(_savedViews, apply, delete, _viewName, save);
        return views;
    }

    private Grid BuildSessionPane()
    {
        var pane = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var copyDetails = new Button
        {
            Content = "Copy details",
            Margin = new Thickness(10, 8, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        EnsureMobileTouch(copyDetails);
        copyDetails.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(_sessionInfoText);
                Notify("Copied the session details.");
            }
        };
        pane.Children.Add(copyDetails);
        var scroll = new ScrollViewer { Content = _sessionInfo };
        Grid.SetRow(scroll, 1);
        pane.Children.Add(scroll);
        return pane;
    }

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Opacity = 0.85,
        Margin = new Thickness(0, 2, 0, 0),
    };

    private void EnsureMobileTouch(params Control[] controls)
    {
        if (!_mobile)
        {
            return;
        }

        foreach (var control in controls)
        {
            control.MinHeight = Math.Max(48, control.MinHeight);
        }
    }

    /// <summary>
    /// The facet panel states its own scope and semantics. Its counts are whole-session
    /// while the table below the timeline lists the current view, and several values in
    /// one group combine with OR — both are invisible in a bare "+ / −" row and both
    /// change what a click appears to do (§14.11).
    /// </summary>
    private Grid BuildFacetPane()
    {
        var pane = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var header = new StackPanel
        {
            Margin = new Thickness(7, 6, 7, 2),
            Spacing = 2,
            Children =
            {
                // The qualifier has to be visible. It was in a tooltip, which a touch device
                // never shows, so a phone only ever read the misleading half: with a search
                // active, "WHOLE SESSION" sat above counts that were already filtered
                // (finding 11).
                CountScopeLabel(
                    "COUNTS · WHOLE SESSION · CURRENT FILTER",
                    "Facet counts cover the whole session, not just the visible time range, " +
                    "and they count only entries that match the current filter."),
                // Kept to one line on a phone: in a landscape workspace the pane's own header
                // was consuming the height the rows needed, and Details mode showed a heading
                // and not one data row (finding 3d).
                new TextBlock
                {
                    Text = _mobile
                        ? "+ include (OR) · − exclude · tap again to undo"
                        : "+ includes (OR within a group); − excludes; tap an active action again to remove it.",
                    TextWrapping = _mobile ? TextWrapping.NoWrap : TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontSize = TextScale.Of(10),
                    Opacity = 0.72,
                },
            },
        };
        pane.Children.Add(header);
        _facetScroll = new ScrollViewer { Content = _facets };
        AutomationProperties.SetName(_facetScroll, "Facets");
        Grid.SetRow(_facetScroll, 1);
        pane.Children.Add(_facetScroll);
        return pane;
    }

    private static TextBlock CountScopeLabel(string text, string helpText)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = TextScale.Of(10),
            FontWeight = FontWeight.Bold,
            Opacity = 0.82,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0),
        };
        ToolTip.SetTip(label, helpText);
        AutomationProperties.SetName(label, $"{text}. {helpText}");
        return label;
    }


    /// <summary>
    /// Builds the session pane as a grouped label/value list rather than one monospace
    /// blob: sections give the eye anchors, the muted-label / bright-value split reads as a
    /// table, and a non-zero defect is drawn in the warn color so a health problem stands
    /// out instead of hiding in an identical wall of digits (§14.1, §14.11).
    /// </summary>
    private void UpdateSessionInfo()
    {
        _sessionInfo.Children.Clear();
        var snapshot = _viewModel.Snapshot;
        if (snapshot is null)
        {
            _sessionInfoText = string.Empty;
            _sessionInfo.Children.Add(new TextBlock
            {
                Text = "Session metadata becomes available after the first committed snapshot.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
            });
            return;
        }

        var dark = ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light;
        var labelBrush = new SolidColorBrush(WorkspacePalette.TextMuted(dark));
        var valueBrush = new SolidColorBrush(WorkspacePalette.TextPrimary(dark));
        var headBrush = new SolidColorBrush(WorkspacePalette.Accent(dark));
        var warnBrush = new SolidColorBrush(LevelPalette.InkOf(LogLevel.Warn, dark));

        var descriptor = snapshot.Descriptor;
        var counters = descriptor.Counters;
        var defects = descriptor.Defects;
        var manifest = snapshot.Manifest;
        var text = new System.Text.StringBuilder();

        void Section(string title)
        {
            text.Append('\n').Append(title).Append('\n');
            _sessionInfo.Children.Add(new TextBlock
            {
                Text = title.ToUpperInvariant(),
                FontWeight = FontWeight.Bold,
                FontSize = TextScale.Of(10),
                Foreground = headBrush,
                Margin = new Thickness(0, _sessionInfo.Children.Count == 0 ? 0 : 11, 0, 3),
            });
        }

        void Row(string label, string value, bool warn = false)
        {
            text.Append(label).Append(": ").Append(value).Append('\n');
            var labelText = new TextBlock
            {
                Text = label,
                Foreground = labelBrush,
                FontSize = TextScale.Of(_mobile ? 10.5 : 11.5),
            };
            var valueText = new TextBlock
            {
                Text = value,
                Foreground = warn ? warnBrush : valueBrush,
                FontWeight = warn ? FontWeight.SemiBold : FontWeight.Normal,
                FontSize = TextScale.Of(11.5),
                TextWrapping = TextWrapping.Wrap,
            };

            if (_mobile)
            {
                // Split mode gives this pane roughly half of a 360 dp phone. The desktop
                // 148 dp label column consumed that entire width and arranged every value
                // outside the viewport, including the recovery State a reader came here to
                // inspect (F-19). A label-over-value pair uses the narrow column honestly;
                // the containing ScrollViewer absorbs the extra height.
                _sessionInfo.Children.Add(new StackPanel
                {
                    Margin = new Thickness(0, 2, 0, 4),
                    Spacing = 1,
                    Children = { labelText, valueText },
                });
                return;
            }

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("148,*"),
                Margin = new Thickness(0, 1),
            };
            row.Children.Add(labelText);
            Grid.SetColumn(valueText, 1);
            row.Children.Add(valueText);
            _sessionInfo.Children.Add(row);
        }

        // A row whose value is one long unbreakable token, on a line of its own. A
        // 36-character session id in the 190 px value column of a phone wrapped to three
        // lines, the last of which held a single character (audit 3, E4). An identifier is
        // read by comparing it, not by reading it, so it gets the full width and a
        // fixed-pitch face — which is also what makes two of them comparable at a glance.
        void IdentifierRow(string label, string value)
        {
            text.Append(label).Append(": ").Append(value).Append('\n');
            var stack = new StackPanel { Margin = new Thickness(0, 1) };
            stack.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = labelBrush,
                FontSize = TextScale.Of(11.5),
            });
            var valueText = new SelectableTextBlock
            {
                Text = value,
                Foreground = valueBrush,
                FontFamily = MonoFont,
                FontSize = TextScale.Of(11),
                TextWrapping = TextWrapping.Wrap,
            };
            AutomationProperties.SetName(valueText, $"{label}: {value}");
            stack.Children.Add(valueText);
            _sessionInfo.Children.Add(stack);
        }

        static string N(long value) => value.ToString("N0", DisplayCulture.Current);

        static string Duration(double seconds) => seconds switch
        {
            < 60 => $"{seconds:N0} s",
            < 3600 when seconds % 60 == 0 => $"{seconds / 60:N0} min",
            < 3600 => $"{(int)(seconds / 60)} min {seconds % 60:N0} s",
            _ when seconds % 3600 == 0 => $"{seconds / 3600:N0} h",
            _ => TimeSpan.FromSeconds(seconds).ToString("g", DisplayCulture.Current),
        };

        Section("Session");

        // First row in the pane when it applies, because the status bar's marker sends
        // the reader here to find out what is wrong, and it must be the thing they see.
        if (_viewModel.CaptureHealthWarning is { Length: > 0 } health)
        {
            Row("Needs attention", health, true);
        }

        Row("Source", $"{descriptor.SourceKind} · {descriptor.DisplayName}");

        // Stated as a fact about the session rather than as a warning: this pane is where a
        // reader comes to ask what they are looking at, and the scope is part of the answer
        // whether or not it is currently a problem.
        if (_viewModel.CaptureScopeRemedy is { Length: > 0 } scopeRemedy)
        {
            Row("Log scope", scopeRemedy);
        }
        Row("Format", manifest.IngestSettings.FormatOverride?.ToString() ?? "auto-detected");

        // Which clock the workspace is reading in, and — when they differ — which one the
        // source was parsed in. A device capture is parsed in UTC and read locally, and a
        // reader comparing a row against their own watch needs to be told that.
        var displayZone = DisplayZoneId();
        var parseZone = descriptor.TimestampPolicy.TimeZoneId;
        Row(
            "Times shown in",
            string.Equals(displayZone, parseZone, StringComparison.Ordinal)
                ? displayZone
                : $"{displayZone} · captured as {parseZone}");
        // Never the raw enum: "Importing" is the domain's word for reading a finite file and
        // it was shown beside a live capture (finding F-14), and an unfinalized manifest has to
        // say so here as well as in Recents (finding F-19).
        var completion = SessionCompletionText.Of(descriptor.Finalized, _viewModel.IsSessionWorkInFlight);
        Row(
            "State",
            SessionCompletionText.State(descriptor.State, completion) +
                (descriptor.Degraded ? " · degraded/index-only" : string.Empty),
            descriptor.Degraded || completion == SessionCompletion.RecoverablePartial);
        IdentifierRow("Session id", descriptor.SessionId.ToString());

        // This is the request, not an inference from what happened to arrive. In
        // particular an empty `crash` buffer remains visible here as selected, which is the
        // question a reader reopening a capture is trying to answer (Windows F-02).
        if (descriptor.CaptureSettings is { } capture)
        {
            Section("Capture request");
            Row(
                "Requested buffers",
                capture.RequestedBuffers is { Count: > 0 } requested
                    ? string.Join(", ", requested)
                    : "not recorded");
            Row(
                "History",
                capture.IncludesBufferHistory
                    ? "Everything already in the selected buffers"
                    : capture.PreRollSeconds is { } preRoll
                        ? preRoll == 0
                            ? "From capture start (no earlier records)"
                            : $"{Duration(preRoll)} before capture start"
                        : "not recorded");

            var stopLimits = new List<string>(2);
            if (capture.DurationLimitSeconds is { } durationLimit)
            {
                stopLimits.Add(Duration(durationLimit));
            }
            if (capture.ByteLimit is { } byteLimit)
            {
                stopLimits.Add(RecentSessionsDialog.FormatBytes(byteLimit));
            }
            Row("Stop limits", stopLimits.Count == 0 ? "Until stopped" : string.Join(" or ", stopLimits));
            Row("Logcat format", capture.NegotiatedFormat ?? "not reported");
            Row("Log timestamp zone", capture.LogTimeZoneId ?? "not reported");
            Row("Device model", capture.DeviceModel ?? "not reported");
            Row("ADB version", capture.AdbVersion ?? "not reported");
            if (capture.DeviceFingerprint is { Length: > 0 } fingerprint)
            {
                IdentifierRow("Device fingerprint", fingerprint);
            }
        }

        Section("Entries");
        Row("Timed", N(counters.TimedEntries));
        // Untimed lines (buffer markers and the like) and inferred/continued timestamps are
        // expected in ordinary logs, so they stay neutral; highlighting them would cry wolf
        // on every healthy session and bury the counts that do signal a problem.
        Row("Untimed", N(counters.UntimedEntries));
        Row("Parsed", N(counters.ParsedEntries));
        Row("Rejected", N(counters.RejectedCandidates), counters.RejectedCandidates > 0);
        Row("Unknown", N(counters.UnknownLines), counters.UnknownLines > 0);
        Row("Process-name ranges", N(snapshot.ProcessNames.Count));

        Section("Defects");
        Row("Continuations", N(defects.Continuations));
        Row("Inferred time", N(defects.TimestampInferences));
        Row("Low confidence", N(defects.LowConfidenceTimestamps), defects.LowConfidenceTimestamps > 0);
        Row("Out-of-order", N(defects.OutOfOrderEntries), defects.OutOfOrderEntries > 0);
        Row("Late segment", N(defects.LateSegmentEntries), defects.LateSegmentEntries > 0);
        Row("Source changes", N(defects.SourceChanges));

        Section("Live loss evidence");
        Row("Chatty drops", N(defects.ChattyDeclaredDrops), defects.ChattyDeclaredDrops > 0);
        Row("Reconnect gaps", N(defects.ReconnectGaps), defects.ReconnectGaps > 0);
        Row(
            "Time missing across gaps",
            defects.ReconnectGapMilliseconds < 1000
                ? $"{N(defects.ReconnectGapMilliseconds)} ms"
                : Duration(defects.ReconnectGapMilliseconds / 1000d),
            defects.ReconnectGapMilliseconds > 0);
        Row("Duplicates", N(defects.ReconnectDuplicates), defects.ReconnectDuplicates > 0);

        Section("Safety");
        Row("Encoding fallback", N(defects.EncodingFallbacks), defects.EncodingFallbacks > 0);
        Row("Long-line overflow", N(defects.LongLineOverflows), defects.LongLineOverflows > 0);
        Row("Template limit", N(defects.TemplateOverflowEntries), defects.TemplateOverflowEntries > 0);
        Row("Retention deleted", N(defects.RetentionDeleted), defects.RetentionDeleted > 0);

        // Segment count is the one number that used to grow with how long a capture ran
        // rather than with how much it captured, and exhausting it took the capture down
        // with it. Compaction keeps it small; showing it is how a reader can tell that it
        // still is.
        Section("Storage");
        Row("Segments", N(snapshot.Segments.Count));
        Row("Open mappings", N(snapshot.MappedColumnCount));
        Row("Entries per segment", snapshot.Segments.Count == 0
            ? "—"
            : N(counters.TimedEntries / Math.Max(1, snapshot.Segments.Count)));

        Section("Build");
        Row("Snapshot", $"{snapshot.Generation} · store {manifest.FormatVersion}");
        Row("Parser", $"{manifest.ParserVersion}");
        Row("Templates", $"{manifest.TemplateAlgorithmVersion}");
        Row("Updated", manifest.UpdatedUtc.ToString("u", System.Globalization.CultureInfo.InvariantCulture));

        Section("Location");
        Row("Path", snapshot.RootPath);

        _sessionInfoText = text.ToString().TrimStart('\n');
        AnnounceSourceAccountingOnce(counters);

        // The chip's number comes from the descriptor, so this is the first moment it can
        // be stated — and the last that is guaranteed to run when a session's counters settle.
        UpdateOffTimelineChip();
    }

    private bool _sourceAccountingAnnounced;

    /// <summary>
    /// Says once, when it applies, that part of this file is not on the timeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two populations are invisible on every time-based surface and were never mentioned on
    /// any of them. Indented stack frames are unknown lines under ADR 0009 — correctly — and a
    /// crash log loses two thirds of itself to that bucket while every counter says
    /// <c>600 entries</c> (V2-14). Records that parsed but carry no usable timestamp are
    /// counted by the filter and not by the session, which is how <c>3,425 match</c> came to
    /// sit beside <c>2,225 in session</c> with nothing explaining the difference (V2-13).
    /// </para>
    /// <para>
    /// Once, on the notice lane, at the moment the session's counters first settle — the same
    /// place and the same tense the product uses for everything else it has to tell the reader
    /// about the file they just opened. The threshold is deliberately low for unknown lines:
    /// one stack trace in a log is exactly the case the reader needs to know is kept.
    /// </para>
    /// </remarks>
    private void AnnounceSourceAccountingOnce(SessionCounters counters)
    {
        if (_sourceAccountingAnnounced || counters.SourceLines <= 0)
        {
            return;
        }

        var unparsed = counters.UnknownLines + counters.RejectedCandidates;
        var untimed = counters.UntimedEntries;
        if (unparsed == 0 && untimed == 0)
        {
            return;
        }

        _sourceAccountingAnnounced = true;
        var parts = new List<string>(2);
        if (unparsed > 0)
        {
            // The stack-trace hint is a diagnosis, and a diagnosis of one line is a guess. It
            // is offered only when the population is large enough for the shape to be the
            // likely explanation; a handful of odd lines is just reported.
            var many = unparsed >= 20 || unparsed * 20 >= counters.SourceLines;
            parts.Add(
                (many
                    ? $"{unparsed:N0} of {counters.SourceLines:N0} lines are not logcat records — usually " +
                      "stack-trace frames. "
                    : $"{Counted.Lines(unparsed)} could not be read as a logcat record. ") +
                "They are kept byte for byte; open them from More → Unparsed lines…");
        }

        if (untimed > 0)
        {
            parts.Add(
                $"{untimed:N0} records carried no usable timestamp, so they are not on the timeline.");
        }

        Notify(string.Join("  ", parts));
    }

    /// <summary>One value in a log row. The foreground takes a brush rather than a color so
    /// the severity letter can use <see cref="LevelPalette"/>'s cached instances instead of
    /// allocating a brush per row (§19.3).</summary>
    private static TextBlock Cell(string text, int column, IBrush? foreground = null)
    {
        var cell = new TextBlock
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 2),
        };
        if (foreground is not null)
        {
            cell.Foreground = foreground;
        }

        Grid.SetColumn(cell, column);
        return cell;
    }

}
