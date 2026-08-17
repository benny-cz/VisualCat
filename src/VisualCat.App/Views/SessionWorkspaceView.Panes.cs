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
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;

namespace VisualCat.App.Views;

public sealed partial class SessionWorkspaceView : UserControl
{
    private Control BuildTemplatePane()
    {
        // Without this the list inherits Fluent's touch-sized rows and only three or four
        // templates fit the pane; the same compact container the entry table uses shows a
        // useful ranking instead.
        _templates.Styles.Add(CompactItemStyle(_mobile ? 64 : 22));
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
            var exactCount = template.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
            var count = new TextBlock
            {
                Text = FormatTemplateCount(template.Count),
                FontFamily = MonoFont,
                FontWeight = FontWeight.SemiBold,
                FontSize = 11,
                Width = metricWidth,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 0, metricGap, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            ToolTip.SetTip(count, $"{exactCount} matching entries");
            AutomationProperties.SetName(count, $"{exactCount} matching entries");
            row.Children.Add(count);
            var totalMatching = Math.Max(template.Count, _viewModel.Statistics?.TotalMatching ?? template.Count);
            var prevalence = new ProgressBar
            {
                Minimum = 0,
                Maximum = totalMatching,
                Value = template.Count,
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
                $"{exactCount} entries · {template.Count / (double)Math.Max(1, totalMatching):P1} of current matches");
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
                FontSize = 10,
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
                _ = _viewModel.IncludeTemplateAsync(template.TemplateId);
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
                _ = _viewModel.ExcludeTemplateAsync(template.TemplateId);
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

    internal static string FormatTemplateCount(long count)
    {
        if (count < 1_000)
        {
            return count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
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
        return scaled.ToString(format, System.Globalization.CultureInfo.CurrentCulture) + units[unitIndex];
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
                CountScopeLabel(
                    "COUNTS · WHOLE SESSION",
                    "Facet counts cover the whole session under the current filter."),
                new TextBlock
                {
                    Text = "+ includes (OR within a group); − excludes; tap an active action again to remove it.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 10,
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
            FontSize = 10,
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
        var warnColor = LevelPalette.ColorOf(LogLevel.Warn);
        var warnBrush = new SolidColorBrush(dark ? warnColor : Darken(warnColor));

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
                FontSize = 10,
                Foreground = headBrush,
                Margin = new Thickness(0, _sessionInfo.Children.Count == 0 ? 0 : 11, 0, 3),
            });
        }

        void Row(string label, string value, bool warn = false)
        {
            text.Append(label).Append(": ").Append(value).Append('\n');
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("148,*"),
                Margin = new Thickness(0, 1),
            };
            row.Children.Add(new TextBlock { Text = label, Foreground = labelBrush, FontSize = 11.5 });
            var valueText = new TextBlock
            {
                Text = value,
                Foreground = warn ? warnBrush : valueBrush,
                FontWeight = warn ? FontWeight.SemiBold : FontWeight.Normal,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(valueText, 1);
            row.Children.Add(valueText);
            _sessionInfo.Children.Add(row);
        }

        static string N(long value) => value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

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
        Row("State", $"{descriptor.State}{(descriptor.Degraded ? " · degraded/index-only" : string.Empty)}", descriptor.Degraded);
        Row("Session id", descriptor.SessionId.ToString());

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
        Row("Duplicates", N(defects.ReconnectDuplicates), defects.ReconnectDuplicates > 0);

        Section("Safety");
        Row("Encoding fallback", N(defects.EncodingFallbacks), defects.EncodingFallbacks > 0);
        Row("Long-line overflow", N(defects.LongLineOverflows), defects.LongLineOverflows > 0);
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
