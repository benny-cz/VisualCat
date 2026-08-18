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
    private void RebuildChips()
    {
        while (_chips.Children.Count > 1)
        {
            _chips.Children.RemoveAt(_chips.Children.Count - 1);
        }

        var filter = _viewModel.Filter;
        UpdateLevelChecks();
        if (filter.IncludedLevels.Count > 0)
        {
            AddChip(
                $"levels: {string.Join(',', filter.IncludedLevels.Order().Select(static level => level.ToLetter()))}",
                () => _viewModel.ClearLevelFilterAsync());
        }

        // Several values in one dimension are OR'd — a second included tag widens the
        // result rather than narrowing it. One chip per dimension and direction says so
        // in the chip itself instead of leaving the user to infer it (§14.11).
        AddDimensionChip("tag", FacetDimension.Tag, filter.IncludedTags.Order(StringComparer.Ordinal), exclude: false);
        AddDimensionChip("tag", FacetDimension.Tag, filter.ExcludedTags.Order(StringComparer.Ordinal), exclude: true);
        AddDimensionChip(
            "process",
            FacetDimension.Process,
            filter.IncludedProcesses.Order(StringComparer.Ordinal),
            exclude: false);
        AddDimensionChip(
            "process",
            FacetDimension.Process,
            filter.ExcludedProcesses.Order(StringComparer.Ordinal),
            exclude: true);
        AddDimensionChip("pid", FacetDimension.Pid, filter.IncludedPids.Order().Select(Number), exclude: false);
        AddDimensionChip("pid", FacetDimension.Pid, filter.ExcludedPids.Order().Select(Number), exclude: true);
        AddDimensionChip("tid", FacetDimension.Tid, filter.IncludedTids.Order().Select(Number), exclude: false);
        AddDimensionChip("tid", FacetDimension.Tid, filter.ExcludedTids.Order().Select(Number), exclude: true);
        AddDimensionChip(
            "buffer",
            FacetDimension.Buffer,
            filter.IncludedBuffers.Order(StringComparer.Ordinal),
            exclude: false);
        AddDimensionChip(
            "buffer",
            FacetDimension.Buffer,
            filter.ExcludedBuffers.Order(StringComparer.Ordinal),
            exclude: true);
        AddDimensionChip(
            "template",
            FacetDimension.Template,
            filter.IncludedTemplates.Order().Select(static id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            exclude: false);
        AddDimensionChip(
            "template",
            FacetDimension.Template,
            filter.ExcludedTemplates.Order().Select(static id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            exclude: true);

        if (filter.Search is { } search)
        {
            AddChip(
                $"{(search.IsRegex ? "regex" : "text")} = {search.Query}",
                async () =>
                {
                    _search.Text = string.Empty;
                    await ApplySearchAsync();
                });
        }

        if (filter.TimeRange is { } range)
        {
            AddChip(
                $"time = {FormatInstant(range.StartInclusive)} — {FormatInstant(range.EndExclusive)}",
                () => _viewModel.SetTimeRangeFilterAsync(null));
        }

        // An empty chip strip is dead vertical space; it reappears with the first
        // active filter chip or range selection.
        UpdateMobileFilterCount(filter);
        UpdateChipBarVisibility();
    }

    private void UpdateMobileFilterCount(FilterSpec filter)
    {
        if (!_mobile)
        {
            return;
        }

        var activeGroups = 0;
        activeGroups += filter.Search is null ? 0 : 1;
        activeGroups += filter.TimeRange is null ? 0 : 1;
        activeGroups += filter.IncludedLevels.Count == 0 ? 0 : 1;
        activeGroups += filter.IncludedTags.Count == 0 ? 0 : 1;
        activeGroups += filter.ExcludedTags.Count == 0 ? 0 : 1;
        activeGroups += filter.IncludedPids.Count == 0 ? 0 : 1;
        activeGroups += filter.ExcludedPids.Count == 0 ? 0 : 1;
        activeGroups += filter.IncludedProcesses.Count == 0 ? 0 : 1;
        activeGroups += filter.ExcludedProcesses.Count == 0 ? 0 : 1;
        activeGroups += filter.IncludedTids.Count == 0 ? 0 : 1;
        activeGroups += filter.ExcludedTids.Count == 0 ? 0 : 1;
        activeGroups += filter.IncludedTemplates.Count == 0 ? 0 : 1;
        activeGroups += filter.ExcludedTemplates.Count == 0 ? 0 : 1;
        activeGroups += filter.IncludedBuffers.Count == 0 ? 0 : 1;
        activeGroups += filter.ExcludedBuffers.Count == 0 ? 0 : 1;
        activeGroups += filter.IncludedOutcomes.Count == 0 ? 0 : 1;

        if (_mobileFilterButton is { } button)
        {
            button.Content = activeGroups == 0 ? "Filters" : $"Filters · {activeGroups}";
        }

        if (_mobileFilterCount is { } count)
        {
            count.Text = activeGroups == 0
                ? "No active filters"
                : $"{activeGroups} active filter {(activeGroups == 1 ? "group" : "groups")}";
        }
    }

    /// <summary>
    /// Keeps the chip strip's height reserved for any session that can be filtered.
    /// </summary>
    /// <remarks>
    /// The strip used to appear with the first chip, pushing the analysis tabs and everything
    /// below them down by about a touch row: applying a filter moved every control the reader
    /// was about to tap next (finding 26). Reserving the row costs one line and buys a
    /// workspace that does not rearrange itself, and the line is not blank — it states that
    /// nothing is filtered, which is the question the strip answers.
    /// </remarks>
    private void UpdateChipBarVisibility()
    {
        var hasChips = _chips.Children.Count > 1 || _rangeActions.IsVisible;
        var canFilter = _viewModel.Snapshot is not null;
        _chipBar.IsVisible = !(_mobile && _mobileFiltersOpen) && (hasChips || canFilter);
        if (_chipEmptyLabel is { } empty)
        {
            empty.IsVisible = !hasChips;
        }

        if (_clearFilters is { } clear)
        {
            clear.IsVisible = hasChips;
        }
    }

    /// <summary>
    /// Rebuilds the facet panel from the latest statistics. Two rules keep the panel
    /// usable while the filter changes underneath it: values the filter already acts on
    /// are pinned to the top of their group with their state visible, and the scroll
    /// offset is restored, so the row under the pointer does not move between two
    /// clicks (§14.11).
    /// </summary>
    private void RebuildFacets(StatisticsResult? statistics)
    {
        var scroll = _facetScroll?.Offset;
        _facets.Children.Clear();
        if (statistics is null)
        {
            return;
        }

        AddFacetGroup(
            "Tags",
            FacetDimension.Tag,
            statistics.Tags.Select(facet => (FacetKey.OfText(facet.Value), facet.Value, facet.Count)));
        // A process with no resolvable name is reported as "PID 9431", which made the
        // Processes group an exact restatement of the PIDs group below it — the same values
        // with the same counts, listed twice (finding 28). Named processes are what this
        // group is for; the unnamed ones are already one group down.
        AddFacetGroup(
            "Processes",
            FacetDimension.Process,
            (statistics.Processes ?? [])
                .Where(static facet => !IsPidPlaceholder(facet.Value))
                .Select(facet => (FacetKey.OfText(facet.Value), facet.Value, facet.Count)));
        AddFacetGroup(
            "PIDs",
            FacetDimension.Pid,
            statistics.Pids.Select(facet => (FacetKey.OfNumber(facet.Value), Number(facet.Value), facet.Count)));
        AddFacetGroup(
            "Threads",
            FacetDimension.Tid,
            statistics.Tids.Select(facet => (FacetKey.OfNumber(facet.Value), Number(facet.Value), facet.Count)));
        AddFacetGroup(
            "Buffers",
            FacetDimension.Buffer,
            statistics.Buffers.Select(facet => (FacetKey.OfText(facet.Value), facet.Value, facet.Count)));

        if (scroll is { } offset)
        {
            // The panel is rebuilt inside the same layout pass that produced the click, so
            // the offset is reapplied once the new rows have been measured.
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (_facetScroll is { } viewer)
                    {
                        viewer.Offset = new Vector(offset.X, Math.Min(offset.Y, Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height)));
                    }
                },
                DispatcherPriority.Loaded);
        }
    }

    private static string Number(int value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether a process facet value is the query engine's stand-in for a process whose name
    /// could not be resolved (<c>PID 9431</c>) rather than an actual process name.
    /// </summary>
    private static bool IsPidPlaceholder(string value)
    {
        const string prefix = "PID ";
        return value.StartsWith(prefix, StringComparison.Ordinal) &&
               value.Length > prefix.Length &&
               int.TryParse(
                   value.AsSpan(prefix.Length),
                   System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out _);
    }

    private void AddFacetGroup(
        string heading,
        FacetDimension dimension,
        IEnumerable<(FacetKey Key, string Text, long Count)> values)
    {
        var rows = values
            .Select(value => (value.Key, value.Text, value.Count, State: _viewModel.StateOf(dimension, value.Key)))
            .OrderBy(static row => row.State == FacetState.Neutral ? 1 : 0)
            .ToArray();
        if (rows.Length == 0)
        {
            // A heading with nothing under it is a promise of a group that does not exist.
            return;
        }

        var active = rows.Count(static row => row.State != FacetState.Neutral);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 9, 0, 2) };
        header.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeight.Bold });
        if (active > 0)
        {
            var clear = new Button
            {
                Content = "Clear",
                FontSize = 10,
                Padding = new Thickness(6, 0),
                Background = Brushes.Transparent,
            };
            ToolTip.SetTip(clear, $"Remove every {heading.ToLowerInvariant()} filter");
            clear.Click += (_, _) => _ = _viewModel.ClearFacetDimensionAsync(dimension);
            Grid.SetColumn(clear, 1);
            header.Children.Add(clear);
        }

        _facets.Children.Add(header);
        foreach (var row in rows)
        {
            _facets.Children.Add(FacetRow(dimension, row.Key, row.Text, row.Count, row.State));
        }
    }

    private Grid FacetRow(FacetDimension dimension, FacetKey key, string text, long count, FacetState state)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            Margin = new Thickness(0, 1),
        };
        row.Children.Add(new TextBlock
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = state == FacetState.Excluded ? 0.55 : 1,
            TextDecorations = state == FacetState.Excluded ? TextDecorations.Strikethrough : null,
            FontWeight = state == FacetState.Included ? FontWeight.SemiBold : FontWeight.Normal,
        });
        var countText = new TextBlock
        {
            Text = count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            FontFamily = MonoFont,
            TextAlignment = TextAlignment.Right,
            MinWidth = 52,
            Margin = new Thickness(6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.8,
        };
        Grid.SetColumn(countText, 1);
        row.Children.Add(countText);
        var include = FacetButton("+", state == FacetState.Included, IncludeActive, dimension, key, exclude: false);
        Grid.SetColumn(include, 2);
        row.Children.Add(include);
        var exclude = FacetButton("−", state == FacetState.Excluded, ExcludeActive, dimension, key, exclude: true);
        Grid.SetColumn(exclude, 3);
        row.Children.Add(exclude);
        return row;
    }

    private Button FacetButton(
        string glyph,
        bool active,
        IBrush activeBrush,
        FacetDimension dimension,
        FacetKey key,
        bool exclude)
    {
        var button = new Button
        {
            Content = glyph,
            // Facet controls were a 16-pixel target; the pointer, not the eye, is the
            // constraint here (§14.14 touch/pointer targets).
            Padding = new Thickness(10, 3),
            MinWidth = _mobile ? 48 : 32,
            MinHeight = _mobile ? 48 : 26,
            Margin = new Thickness(2, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        if (active)
        {
            // Assigned only in the active state: a null brush is a local value that
            // overrides the theme rather than falling back to it, which renders the
            // control invisible.
            button.Background = activeBrush;
            button.Foreground = Brushes.White;
        }
        ToolTip.SetTip(
            button,
            active
                ? $"Currently {(exclude ? "excluded" : "included")} · click to remove this filter"
                : exclude
                    ? "Exclude: hide these entries"
                    : "Include: keep only entries matching a value from this group");
        AutomationProperties.SetName(button, exclude ? "Exclude facet value" : "Include facet value");
        button.Click += (_, _) => _ = _viewModel.ToggleFacetAsync(dimension, key, exclude);
        return button;
    }

    private void AddDimensionChip(string label, FacetDimension dimension, IEnumerable<string> values, bool exclude)
    {
        var listed = values.ToArray();
        if (listed.Length == 0)
        {
            return;
        }

        var operators = exclude ? "≠" : "=";
        var text = listed.Length == 1
            ? $"{label} {operators} {Shorten(listed[0])}"
            : $"{label} {operators} {string.Join(" or ", listed.Take(3).Select(Shorten))}" +
              (listed.Length > 3 ? $" +{listed.Length - 3}" : string.Empty);
        AddChip(text, () => _viewModel.ClearFacetDimensionAsync(dimension, exclude));
    }

    private static string Shorten(string value) => value.Length <= 28 ? value : value[..27] + "…";

    private void AddChip(string text, Func<Task>? remove = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        if (remove is not null)
        {
            var close = new Button
            {
                Content = "×",
                Padding = new Thickness(4, 0),
                MinWidth = 0,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(close, "Remove this filter");
            AutomationProperties.SetName(close, $"Remove filter {text}");
            close.Click += (_, _) => _ = remove();
            content.Children.Add(close);
        }

        _chips.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#304DA3FF")),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(3, 1),
            Padding = new Thickness(7, 2),
            Child = content,
        });
    }

    private void UpdateLevelChecks()
    {
        _updatingLevelChecks = true;
        try
        {
            foreach (var (level, toggle) in _levelChecks)
            {
                toggle.IsChecked = _viewModel.Filter.IncludedLevels.Count == 0 ||
                                   _viewModel.Filter.IncludedLevels.Contains(level);
            }
        }
        finally
        {
            _updatingLevelChecks = false;
        }
    }

    /// <summary>
    /// Paints one severity toggle for its current state. Included reads as the level's own
    /// color on a tinted plate; excluded drops to a flat muted outline so a hidden severity
    /// is obvious at a glance rather than requiring the reader to notice an unticked box.
    /// Shape and letter carry the state as well as color, so the distinction survives both
    /// a monochrome display and the high-contrast setting (§14.14).
    /// </summary>
    private void ApplyLevelToggleColors(ToggleButton toggle, LogLevel level)
    {
        var dark = ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light;
        var color = LevelPalette.ColorOf(level);
        if (toggle.IsChecked == true)
        {
            toggle.Background = new SolidColorBrush(Color.FromArgb(dark ? (byte)56 : (byte)40, color.R, color.G, color.B));
            toggle.BorderBrush = new SolidColorBrush(Color.FromArgb(220, color.R, color.G, color.B));
            toggle.Foreground = new SolidColorBrush(dark ? color : Darken(color));
            toggle.BorderThickness = new Thickness(1);
            toggle.Opacity = 1;
        }
        else
        {
            toggle.Background = Brushes.Transparent;
            toggle.BorderBrush = new SolidColorBrush(WorkspacePalette.BorderLine(dark));
            toggle.Foreground = new SolidColorBrush(WorkspacePalette.TextMuted(dark));
            toggle.BorderThickness = new Thickness(1);
            toggle.Opacity = 0.55;
        }
    }

    /// <summary>Keeps a level color legible as text on a light surface.</summary>
    private static Color Darken(Color color) =>
        Color.FromRgb((byte)(color.R * 0.62), (byte)(color.G * 0.62), (byte)(color.B * 0.62));

    /// <summary>
    /// Minimal toggle template that honours the control's own brushes. Fluent's default
    /// paints the checked state from a theme resource onto an inner content presenter,
    /// which sits above anything assigned to the control itself — so every severity
    /// toggle rendered in the same accent blue no matter which color was set on it. This
    /// template binds the visual straight to Background/BorderBrush/Foreground, leaving
    /// <see cref="ApplyLevelToggleColors"/> as the single authority on how a state looks.
    /// </summary>
    private static ControlTheme BuildLevelToggleTheme()
    {
        var template = new FuncControlTemplate<ToggleButton>((control, scope) =>
        {
            var presenter = new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            presenter[!ContentPresenter.ContentProperty] = control[!ContentControl.ContentProperty];
            presenter[!ContentPresenter.ForegroundProperty] = control[!TemplatedControl.ForegroundProperty];
            presenter[!ContentPresenter.FontWeightProperty] = control[!TemplatedControl.FontWeightProperty];
            presenter[!ContentPresenter.FontSizeProperty] = control[!TemplatedControl.FontSizeProperty];

            var border = new Border { Child = presenter };
            border[!Border.BackgroundProperty] = control[!TemplatedControl.BackgroundProperty];
            border[!Border.BorderBrushProperty] = control[!TemplatedControl.BorderBrushProperty];
            border[!Border.BorderThicknessProperty] = control[!TemplatedControl.BorderThicknessProperty];
            border[!Border.CornerRadiusProperty] = control[!TemplatedControl.CornerRadiusProperty];

            // Only the named part joins the scope; registering the unnamed border throws.
            presenter.RegisterInNameScope(scope);
            return border;
        });

        var theme = new ControlTheme(typeof(ToggleButton));
        theme.Setters.Add(new Setter(TemplatedControl.TemplateProperty, template));

        // Pointer feedback the stripped template would otherwise lose.
        var hover = new Style(static selector => selector.Nesting().Class(":pointerover"));
        hover.Setters.Add(new Setter(OpacityProperty, 0.82));
        theme.Add(hover);
        return theme;
    }


}
