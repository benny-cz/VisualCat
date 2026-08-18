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
    private static TextBlock MobileSectionLabel(string text) =>
        new()
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            Opacity = 0.62,
            Margin = new Thickness(1, 4, 0, 0),
        };

    private void SetMobileDisplayMode(MobileWorkspaceDisplayMode mode)
    {
        _mobileWorkspaceState.Select(mode);
        if (_mobileFiltersOpen)
        {
            _mobileFiltersOpen = false;
            if (_mobileFilterPanel is { } panel)
            {
                panel.IsVisible = false;
            }
        }

        ApplyMobileLayout(Bounds.Size);
    }

    private void SetMobileFiltersOpen(bool open)
    {
        _mobileFiltersOpen = open;
        if (_mobileFilterPanel is { } panel)
        {
            panel.IsVisible = open;
        }

        ApplyMobileLayout(Bounds.Size);
    }

    private void ApplyMobileModeButtonStyles()
    {
        foreach (var (mode, button) in _mobileModeButtons)
        {
            var selected = mode == _mobileWorkspaceState.DisplayMode;
            ApplyMobileChoiceAppearance(button, selected);
            button.FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
            AutomationProperties.SetHelpText(button, selected ? "Current workspace mode" : "Switch workspace mode");
        }
    }

    private void ApplyMobileChoiceAppearance(TemplatedControl control, bool selected)
    {
        var dark = ActualThemeVariant != ThemeVariant.Light;
        var accent = WorkspacePalette.Accent(dark);
        control.Background = selected
            ? new SolidColorBrush(Color.FromArgb(dark ? (byte)44 : (byte)28, accent.R, accent.G, accent.B))
            : new SolidColorBrush(WorkspacePalette.SurfaceRaised(dark));
        control.BorderBrush = new SolidColorBrush(selected ? accent : WorkspacePalette.BorderLine(dark));
        control.BorderThickness = new Thickness(1);
        control.Foreground = new SolidColorBrush(
            selected ? WorkspacePalette.TextPrimary(dark) : WorkspacePalette.TextMuted(dark));
    }

    private void ApplyMobileLayout(Size size)
    {
        if (!_mobile || _root.RowDefinitions.Count < 7)
        {
            return;
        }

        // A session with nothing to show has no layout to compose; the failure card owns the
        // workspace band until the tab is closed.
        if (_failureVisible)
        {
            return;
        }

        var layout = MobileWorkspaceLayout.ForSize(size.Width, size.Height);
        var wideComposition = layout.UsesWideMobileComposition;
        _mobileWorkspaceState.ApplyLayout(layout);

        if (_mobileLayoutMode != layout.Mode)
        {
            _mobileLayoutMode = layout.Mode;
            // A filter workspace is deliberately transient; the visualization mode is not.
            // This keeps Plot/Split/Details stable across rotation while preventing a tall
            // portrait drawer from consuming a newly short landscape viewport.
            _mobileFiltersOpen = false;
            if (_mobileFilterPanel is { } panel)
            {
                panel.IsVisible = false;
            }

            if (!_rawWrapPreferenceSet && _rawWrapToggle is { } wrapToggle)
            {
                SetRawWrap(!wideComposition);
            }
        }

        var filtersOpen = _mobileFiltersOpen;

        // Nothing to overview yet means no minimap row: an empty bordered frame is not worth a
        // row of a phone screen (finding 28).
        var hasOverview = _viewModel.Overview is not null && _viewModel.Snapshot?.TimedRange is not null;
        var timelineVisible = !filtersOpen &&
                              _mobileWorkspaceState.DisplayMode is not MobileWorkspaceDisplayMode.Details;
        var analysisVisible = !filtersOpen &&
                              _mobileWorkspaceState.DisplayMode is not MobileWorkspaceDisplayMode.Plot;
        if (wideComposition)
        {
            _root.RowDefinitions[2].Height = filtersOpen
                ? new GridLength(0)
                : new GridLength(1, GridUnitType.Star);
            _root.RowDefinitions[3].Height = new GridLength(0);
            _root.RowDefinitions[5].Height = new GridLength(0);
        }
        else
        {
            _root.RowDefinitions[2].Height = timelineVisible
                ? new GridLength(
                    _mobileWorkspaceState.DisplayMode == MobileWorkspaceDisplayMode.Plot ? 1 : layout.TimelineWeight,
                    GridUnitType.Star)
                : new GridLength(0);
            _root.RowDefinitions[3].Height = timelineVisible && hasOverview && layout.MinimapHeight > 0
                ? new GridLength(layout.MinimapHeight, GridUnitType.Pixel)
                : new GridLength(0);
            _root.RowDefinitions[5].Height = analysisVisible
                ? new GridLength(
                    _mobileWorkspaceState.DisplayMode == MobileWorkspaceDisplayMode.Details ? 1 : layout.AnalysisWeight,
                    GridUnitType.Star)
                : new GridLength(0);
        }

        ConfigureWideMobileComposition(wideComposition, timelineVisible, analysisVisible, size.Width);
        UpdateSummaryText();
        _analysisGrid!.IsVisible = analysisVisible;
        UpdateChipBarVisibility();
        ApplyMobileModeButtonStyles();

        _timeline.IsVisible = timelineVisible;

        // No minimum height. A star-sized row cannot refuse one, so the control was arranged
        // taller than its cell whenever the chrome grew — and its own bottom, which is where
        // the axis labels live, was then drawn underneath the minimap that follows it in the
        // grid. The plot's bands give way instead (finding 18); the row weights above are what
        // keep it a useful size.
        _timeline.MinHeight = 0;

        if (_minimapFrame is { } minimap)
        {
            minimap.IsVisible = timelineVisible && hasOverview && layout.MinimapHeight > 0;
        }

        if (_mobileFilterScroll is { } filterScroll)
        {
            var maximumPanelHeight = Math.Max(160, size.Height - 58);
            filterScroll.MaxHeight = Math.Max(96, Math.Min(layout.FilterMaximumHeight, maximumPanelHeight - 58));
            if (_mobileFilterPanel is { } filterPanel)
            {
                filterPanel.MaxHeight = maximumPanelHeight;
            }
        }

        if (_mobileFilterButton is { } filterButton)
        {
            ApplyMobileChoiceAppearance(filterButton, filtersOpen);
            AutomationProperties.SetName(filterButton, filtersOpen ? "Close filters" : "Open search and timeline filters");
        }
    }

    private void ConfigureWideMobileComposition(
        bool enabled,
        bool timelineVisible,
        bool analysisVisible,
        double availableWidth)
    {
        var splitTimeline = enabled && timelineVisible && analysisVisible;
        _root.ColumnDefinitions = new ColumnDefinitions(splitTimeline ? "21*,29*" : "*");

        if (_mobileFilterShell is { } topStrip)
        {
            Grid.SetColumn(topStrip, 0);
            Grid.SetColumnSpan(topStrip, splitTimeline ? 2 : 1);
        }

        Grid.SetColumn(_chipBar, 0);
        Grid.SetColumnSpan(_chipBar, splitTimeline ? 2 : 1);
        if (_statusBar is { } workspaceStatus)
        {
            Grid.SetColumn(workspaceStatus, 0);
            Grid.SetColumnSpan(workspaceStatus, splitTimeline ? 2 : 1);
        }

        Grid.SetRow(_timeline, 2);
        Grid.SetRowSpan(_timeline, enabled ? 4 : 1);
        Grid.SetColumn(_timeline, 0);
        Grid.SetColumnSpan(_timeline, 1);

        if (_analysisGrid is { } analysis)
        {
            Grid.SetRow(analysis, enabled ? 2 : 5);
            Grid.SetRowSpan(analysis, enabled ? 4 : 1);
            Grid.SetColumn(analysis, splitTimeline ? 1 : 0);
            Grid.SetColumnSpan(analysis, 1);
        }

        if (_mobileFilterShell is { } filterShell &&
            _mobileQuickActions is { } quickActions)
        {
            filterShell.RowDefinitions = new RowDefinitions("Auto,Auto");
            filterShell.ColumnDefinitions = new ColumnDefinitions("*");
            Grid.SetRow(quickActions, 0);
            Grid.SetColumn(quickActions, 0);
            quickActions.Margin = new Thickness(6, 3);
            quickActions.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        if (_mobileFilterBody is { } filterBody &&
            _mobileQuerySection is { } query &&
            _mobileSeveritySection is { } severity &&
            _mobileTimeSection is { } time)
        {
            filterBody.ColumnDefinitions = new ColumnDefinitions(enabled ? "*,*" : "*");
            filterBody.RowDefinitions = new RowDefinitions(enabled ? "Auto,Auto" : "Auto,Auto,Auto");
            Grid.SetColumn(query, 0);
            Grid.SetRow(query, 0);
            Grid.SetRowSpan(query, enabled ? 2 : 1);
            Grid.SetColumn(severity, enabled ? 1 : 0);
            Grid.SetRow(severity, enabled ? 0 : 1);
            Grid.SetRowSpan(severity, 1);
            Grid.SetColumn(time, enabled ? 1 : 0);
            Grid.SetRow(time, enabled ? 1 : 2);
            Grid.SetRowSpan(time, 1);
        }

        if (_mobileAnalysisTabs is { } tabs)
        {
            // The rail stays on top in every orientation. A left rail looked like it saved
            // the height a short viewport needs, but the strip laid `Entries` and `Entry`
            // side by side with `Insights` beneath, each item pinned to a width narrower
            // than its own label, and rotating between placements reapplied the whole
            // TabControl template — which is why the selected tab had to be restored by
            // hand afterwards. Three short labels in one row cost one line and are always
            // legible (finding 3c).
            foreach (var item in tabs.Items.OfType<TabItem>())
            {
                item.MinWidth = enabled ? 78 : 92;
                item.Width = double.NaN;
                item.FontSize = enabled ? 12.5 : 14;
                item.Padding = enabled ? new Thickness(7, 0) : new Thickness(10, 0);
            }
        }

        if (_entryHeader is { } entryHeader && _entryActions is { } entryActions)
        {
            var analysisWidth = splitTimeline ? availableWidth * 0.58 - 78 : availableWidth - (enabled ? 78 : 0);
            var sideBySide = enabled && analysisWidth >= 540;
            entryHeader.RowDefinitions = new RowDefinitions(sideBySide ? "Auto" : "Auto,Auto");
            entryHeader.ColumnDefinitions = new ColumnDefinitions(sideBySide ? "*,Auto" : "*");
            Grid.SetRow(_summary, 0);
            Grid.SetColumn(_summary, 0);
            Grid.SetRow(entryActions, sideBySide ? 0 : 1);
            Grid.SetColumn(entryActions, sideBySide ? 1 : 0);
            entryActions.HorizontalAlignment = sideBySide
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Stretch;
            entryActions.MaxWidth = sideBySide
                ? Math.Clamp(analysisWidth * 0.58, 280, 430)
                : double.PositiveInfinity;
            _summary.TextWrapping = sideBySide ? TextWrapping.NoWrap : TextWrapping.Wrap;
            _summary.TextTrimming = sideBySide ? TextTrimming.CharacterEllipsis : TextTrimming.None;
            _summary.Margin = sideBySide ? new Thickness(0, 0, 8, 0) : new Thickness(0, 0, 0, 4);
        }

        // Spacing is the panels' own (column and item spacing), so the labels change with
        // the available width and nothing else moves.
        _order.Width = enabled ? 112 : 126;
        _loadMore.Content = splitTimeline ? "More" : enabled ? "Load +500" : "Load next 500";
        if (_copyRaw is { } copyRaw)
        {
            copyRaw.Content = enabled ? "Copy" : "Copy raw";
        }

        if (_openInspector is { } openInspector)
        {
            openInspector.Content = enabled ? "⤢" : "Entry ⤢";
        }

        _timeline.Margin = splitTimeline ? new Thickness(6, 2, 3, 2) : new Thickness(0);
        _analysisGrid!.Margin = enabled
            ? splitTimeline ? new Thickness(3, 2, 6, 2) : new Thickness(6, 2)
            : new Thickness(8, 4);
        _chipBar.Margin = enabled ? new Thickness(6, 0, 6, 2) : new Thickness(10, 0, 10, 5);
        if (_statusBar is { } statusBar)
        {
            statusBar.Margin = enabled ? new Thickness(6, 2, 6, 4) : new Thickness(8, 4, 8, 12);
        }
    }


}
