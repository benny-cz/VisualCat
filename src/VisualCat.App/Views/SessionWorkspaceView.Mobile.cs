using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
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
using Avalonia.VisualTree;
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
    private IInputPane? _inputPane;
    private bool _inputPaneOpen;

    /// <summary>The viewport the workspace was composed for before the keyboard took a share of it.</summary>
    private Size _settledSize;

    private static TextBlock MobileSectionLabel(string text) =>
        new()
        {
            Text = text,
            FontSize = TextScale.Of(10),
            FontWeight = FontWeight.Bold,
            Opacity = 0.62,
            Margin = new Thickness(1, 4, 0, 0),
        };

    /// <summary>
    /// Watches the soft keyboard, because the viewport it takes must not be read as a
    /// different device.
    /// </summary>
    /// <remarks>
    /// The activity resizes rather than pans when the IME opens, which is what keeps the
    /// drawer's own footer reachable — but it also drops a 777 dp portrait viewport to about
    /// 480 dp, below <see cref="MobileWorkspaceLayout.CompactHeightBreakpoint"/>. The size
    /// class flipped, and the recomposition that follows a genuine rotation dismissed the
    /// drawer and unmounted the very <see cref="TextBox"/> that had just been focused: the
    /// query field could not be typed into at all, by touch, in either orientation
    /// (finding 1). Opening a keyboard is not a change of device and not a change of user
    /// intent, so the size class is held at the last settled viewport and only the space the
    /// drawer is given shrinks.
    /// </remarks>
    private void ObserveInputPane()
    {
        if (!_mobile || _inputPane is not null)
        {
            return;
        }

        _inputPane = TopLevel.GetTopLevel(this)?.InputPane;
        if (_inputPane is { } pane)
        {
            pane.StateChanged += OnInputPaneStateChanged;
            _inputPaneOpen = pane.State == InputPaneState.Open;
        }
    }

    private void StopObservingInputPane()
    {
        if (_inputPane is { } pane)
        {
            pane.StateChanged -= OnInputPaneStateChanged;
            _inputPane = null;
        }

        _inputPaneOpen = false;
    }

    private void OnInputPaneStateChanged(object? sender, InputPaneStateEventArgs eventArgs)
    {
        _inputPaneOpen = eventArgs.NewState == InputPaneState.Open;
        ApplyMobileLayout(Bounds.Size);
    }

    private void SetMobileDisplayMode(MobileWorkspaceDisplayMode mode)
    {
        _mobileWorkspaceState.Select(mode);
        if (_mobileFiltersOpen)
        {
            _mobileFiltersOpen = false;
        }

        ApplyMobileLayout(Bounds.Size);
        DisplayModeChanged?.Invoke(_mobileWorkspaceState.Persisted);
    }

    private void SetMobileFiltersOpen(bool open)
    {
        _mobileFiltersOpen = open;
        ApplyMobileLayout(Bounds.Size);
        if (!open && _mobileFilterButton is { } filterButton)
        {
            // Closing the drawer must also put the keyboard away: it was raised for a field
            // that is no longer on screen, and leaving it up covers the results the query was
            // typed to find. Moving focus to the control that closed the drawer is what makes
            // the platform withdraw the IME.
            filterButton.Focus();
        }
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

        // The keyboard's share of the screen changes how much room the drawer has and
        // nothing else. Classifying against the settled viewport is what keeps a focused
        // field mounted while the IME animates in (finding 1).
        var settled = _inputPaneOpen && _settledSize.Height > size.Height ? _settledSize : size;
        if (!_inputPaneOpen)
        {
            _settledSize = size;
        }

        var layout = MobileWorkspaceLayout.ForSize(settled.Width, settled.Height);
        var wideComposition = layout.UsesWideMobileComposition;
        _mobileWorkspaceState.ApplyLayout(layout);

        if (_mobileLayoutMode != layout.Mode)
        {
            var first = _mobileLayoutMode is null;
            _mobileLayoutMode = layout.Mode;

            // A filter workspace is deliberately transient; the visualization mode is not.
            // This keeps Plot/Split/Details stable across rotation while preventing a tall
            // portrait drawer from consuming a newly short landscape viewport — but never
            // while the reader is typing into it.
            if (!first && !FilterDrawerHoldsFocus())
            {
                _mobileFiltersOpen = false;
            }

            if (!_rawWrapPreferenceSet && _rawWrapToggle is not null)
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

        // The drawer, the plot and the analysis pane all live in rows 2..5. Exactly one
        // composition of that band is in force at a time.
        if (filtersOpen)
        {
            _root.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
            _root.RowDefinitions[3].Height = new GridLength(0);
            _root.RowDefinitions[5].Height = new GridLength(0);
        }
        else if (wideComposition)
        {
            _root.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
            _root.RowDefinitions[3].Height = new GridLength(0);
            _root.RowDefinitions[5].Height = timelineVisible && hasOverview && layout.MinimapHeight > 0
                ? new GridLength(layout.MinimapHeight, GridUnitType.Pixel)
                : new GridLength(0);
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

        var minimapVisible = timelineVisible && hasOverview && layout.MinimapHeight > 0;
        if (_minimapFrame is { } minimapFrame)
        {
            minimapFrame.IsVisible = minimapVisible;
        }

        ConfigureWideMobileComposition(
            wideComposition,
            timelineVisible,
            analysisVisible,
            minimapVisible,
            settled.Width);
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

        if (_mobileFilterPanel is { } filterPanel)
        {
            filterPanel.IsVisible = filtersOpen;
            filterPanel.Margin = wideComposition
                ? new Thickness(6, 2, 6, 2)
                : new Thickness(6, 2, 6, 4);
        }

        if (_mobileFilterScroll is { } filterScroll)
        {
            // The drawer fills the band it was given, so the footer stays pinned to the
            // bottom of the card and the query section scrolls under the keyboard rather
            // than being pushed off screen.
            filterScroll.MaxHeight = double.PositiveInfinity;
        }

        if (_statusBar is { } statusRow)
        {
            // A landscape viewport with the keyboard up has about 140 dp left. The status
            // line is a report on a workspace that is currently hidden behind the drawer, so
            // its row goes to the drawer for as long as the drawer is open.
            statusRow.IsVisible = !(filtersOpen && (wideComposition || _inputPaneOpen));
        }

        if (_mobileFilterButton is { } filterButton)
        {
            ApplyMobileChoiceAppearance(filterButton, filtersOpen);
            AutomationProperties.SetName(filterButton, filtersOpen ? "Close filters" : "Open search and timeline filters");
        }

        // Fit acts on the plot, so it goes where the plot goes. In Details it was still
        // present, still enabled, still costing a share of the one row it shares with the
        // mode selector, and still moving a surface nobody could see (audit 2, C7).
        if (_mobileFit is { } fit)
        {
            fit.IsVisible = timelineVisible;
        }

        EnforceEntriesFloor();
    }

    /// <summary>
    /// How many entry rows the analysis pane must be able to show before the plot above it
    /// is entitled to any height at all.
    /// </summary>
    /// <remarks>
    /// The entries list is what the product is for and it was the smallest thing on screen:
    /// 173 px of a 2340 px display in Split, less than one 192 px row, and 60 px with a
    /// notice showing. The row weights alone cannot prevent that, because the chrome above
    /// the list — tab headers, the count line, the sort row — is fixed and comes out of the
    /// same band. So the floor is stated in rows, the pane's own chrome is measured rather
    /// than assumed, and the plot gives way (audit 2, A2).
    /// </remarks>
    private const int SplitEntryRowFloor = 4;

    /// <summary>The floor in Details, where the plot is hidden and the list is the pane.</summary>
    private const int DetailsEntryRowFloor = 6;

    /// <summary>The floor in a short viewport, which has about a third of the height.</summary>
    private const int CompactEntryRowFloor = 3;

    private double _entriesFloorApplied;

    /// <summary>
    /// Reserves the entries list its floor out of the workspace band, taking the difference
    /// from the plot.
    /// </summary>
    /// <remarks>
    /// The pane's chrome is read from the arranged tree rather than restated here: the tab
    /// strip, the count line and the action rows all size themselves, and a constant copied
    /// from them would be wrong the first time one of them changed. One measured pass is
    /// enough because the chrome does not depend on the height it is given, so the second
    /// pass computes the same number and the row stops moving.
    /// </remarks>
    private void EnforceEntriesFloor()
    {
        if (!_mobile || _root.RowDefinitions.Count < 7 || _analysisGrid is not { } analysis)
        {
            return;
        }

        if (_mobileFiltersOpen ||
            _mobileWorkspaceState.DisplayMode == MobileWorkspaceDisplayMode.Plot ||
            !analysis.IsVisible ||

            // In the wide composition the analysis pane is a column beside the plot rather
            // than a band under it: it already has the whole height, and forcing a minimum
            // on a row it merely spans would push the status line off the bottom.
            _mobileLayoutMode == MobileWorkspaceMode.CompactHeight)
        {
            SetEntriesFloor(0);
            return;
        }

        var chrome = analysis.Bounds.Height - _entries.Bounds.Height;
        if (!double.IsFinite(chrome) || chrome <= 0 || analysis.Bounds.Height <= 0)
        {
            // Nothing has been arranged yet, so there is nothing to measure. The layout pass
            // that arranges it calls back here.
            return;
        }

        var rows = _mobileLayoutMode == MobileWorkspaceMode.CompactHeight
            ? CompactEntryRowFloor
            : _mobileWorkspaceState.DisplayMode == MobileWorkspaceDisplayMode.Details
                ? DetailsEntryRowFloor
                : SplitEntryRowFloor;
        var wanted = chrome + (rows * _entryRowMinimumHeight);

        // The plot keeps a band it can still be read in; below that the reader is better
        // served by switching to Details than by a two-row heat map.
        var band = Bounds.Height - _root.RowDefinitions[0].ActualHeight
                   - _root.RowDefinitions[1].ActualHeight
                   - _root.RowDefinitions[6].ActualHeight;
        var ceiling = band > 0 ? Math.Max(0, band - MinimumReadablePlotHeight) : wanted;
        SetEntriesFloor(Math.Min(wanted, ceiling));
    }

    /// <summary>Below this the plot states a shape it cannot draw, so it yields entirely.</summary>
    private const double MinimumReadablePlotHeight = 132;

    private void SetEntriesFloor(double floor)
    {
        if (Math.Abs(_entriesFloorApplied - floor) < 0.5)
        {
            return;
        }

        _entriesFloorApplied = floor;
        _root.RowDefinitions[5].MinHeight = floor;
    }

    /// <summary>Whether the reader is currently working inside the filter drawer.</summary>
    private bool FilterDrawerHoldsFocus() =>
        _mobileFiltersOpen &&
        (_inputPaneOpen ||
         _search.IsFocused ||
         _mobileFilterPanel is { } panel &&
         TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual focused &&
         focused.GetSelfAndVisualAncestors().Contains(panel));

    private void ConfigureWideMobileComposition(
        bool enabled,
        bool timelineVisible,
        bool analysisVisible,
        bool minimapVisible,
        double availableWidth)
    {
        var splitTimeline = enabled && timelineVisible && analysisVisible;
        _root.ColumnDefinitions = new ColumnDefinitions(splitTimeline ? "21*,29*" : "*");

        if (_mobileFilterShell is { } topStrip)
        {
            Grid.SetColumn(topStrip, 0);
            Grid.SetColumnSpan(topStrip, splitTimeline ? 2 : 1);
        }

        if (_mobileFilterPanel is { } drawer)
        {
            Grid.SetColumn(drawer, 0);
            Grid.SetColumnSpan(drawer, splitTimeline ? 2 : 1);
        }

        Grid.SetColumn(_chipBar, 0);
        Grid.SetColumnSpan(_chipBar, splitTimeline ? 2 : 1);
        if (_statusBar is { } workspaceStatus)
        {
            Grid.SetColumn(workspaceStatus, 0);
            Grid.SetColumnSpan(workspaceStatus, splitTimeline ? 2 : 1);
        }

        // In the wide composition the plot and the analysis pane are columns, so the plot
        // gives the minimap the last band of its own column and the analysis keeps all four
        // rows beside it.
        var wideMinimap = enabled && timelineVisible && minimapVisible;
        Grid.SetRow(_timeline, 2);
        Grid.SetRowSpan(_timeline, enabled ? wideMinimap ? 3 : 4 : 1);
        Grid.SetColumn(_timeline, 0);
        Grid.SetColumnSpan(_timeline, 1);

        if (_minimapFrame is { } wideMinimapFrame)
        {
            Grid.SetRow(wideMinimapFrame, enabled ? 5 : 3);
            Grid.SetColumn(wideMinimapFrame, 0);
            wideMinimapFrame.Margin = enabled
                ? new Thickness(6, 0, 3, 3)
                : new Thickness(76, 4, 12, 4);
        }

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
            // A short viewport has width to spare and no height at all, so the capture
            // controls move up beside the mode selector instead of taking a band of their
            // own; a portrait phone keeps them on their own full-width row.
            filterShell.RowDefinitions = new RowDefinitions("Auto,Auto,Auto");
            filterShell.ColumnDefinitions = new ColumnDefinitions(enabled ? "Auto,*" : "*");
            Grid.SetRow(quickActions, 0);
            Grid.SetColumn(quickActions, 0);
            quickActions.Margin = enabled ? new Thickness(6, 3, 3, 3) : new Thickness(6, 3);
            quickActions.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (_mobileCaptureActions is { } captureActions)
            {
                Grid.SetRow(captureActions, enabled ? 0 : 1);
                Grid.SetColumn(captureActions, enabled ? 1 : 0);
                captureActions.Margin = enabled
                    ? new Thickness(0, 3, 6, 3)
                    : new Thickness(6, 0, 6, 3);
            }
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
                item.FontSize = TextScale.Of(enabled ? 12.5 : 14);
                item.Padding = enabled ? new Thickness(7, 0) : new Thickness(10, 0);

                // A short viewport pays for every dp of chrome twice: once here and once in
                // the header row below. 42 dp still exceeds the 40 dp Material floor for a
                // tab and buys back a whole entry row (finding 2).
                item.MinHeight = enabled ? 42 : 48;
            }
        }

        if (_entryHeader is { } entryHeader && _entryActions is { } entryActions)
        {
            var analysisWidth = splitTimeline ? availableWidth * 0.58 - 78 : availableWidth - (enabled ? 78 : 0);

            // 400 was still above what a split landscape phone actually offers: a 1080 px
            // portrait device turned sideways leaves this pane about 374 px, so the summary
            // went on taking a second full touch row and the entries list was left 106 px of
            // a 415 px pane (audit 2, A2/D9). At 330 the count line keeps an ellipsised line
            // of its own beside the actions, which is what it is for.
            var sideBySide = enabled && analysisWidth >= 330;
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
                ? Math.Clamp(analysisWidth * 0.62, 240, 430)
                : double.PositiveInfinity;
            _summary.TextWrapping = sideBySide ? TextWrapping.NoWrap : TextWrapping.Wrap;
            _summary.TextTrimming = sideBySide ? TextTrimming.CharacterEllipsis : TextTrimming.None;
            _summary.FontSize = TextScale.Of(enabled ? 11 : 12);
            _summary.Margin = sideBySide ? new Thickness(0, 0, 8, 0) : new Thickness(0, 0, 0, 4);
        }

        // A row of entries is the point of the pane, so it is the last thing that gives way.
        // A short viewport still has to clear the 48 dp touch floor, which it does; the
        // 64 dp portrait row exists for comfort, not for reach.
        ApplyEntryRowHeight(enabled ? 48 : 64);
        foreach (var control in new Control[] { _order, _loadMore, _fitMatches, _clearScope })
        {
            control.MinHeight = enabled ? 42 : 48;
        }

        if (_copyRaw is { } compactCopy)
        {
            compactCopy.MinHeight = enabled ? 42 : 48;
        }

        if (_openInspector is { } compactInspector)
        {
            compactInspector.MinHeight = enabled ? 42 : 48;
        }

        // Spacing is the panels' own (column and item spacing), so the labels change with
        // the available width and nothing else moves.
        _order.Width = enabled ? 112 : 126;
        if (_copyRaw is { } copyRaw)
        {
            copyRaw.Content = enabled ? "Copy" : "Copy raw";
        }

        if (_openInspector is { } openInspector)
        {
            // Always the word. "⤢" on its own is not identifiable on a touch device — there
            // is no pointer to hover for the tooltip — and the label was collapsing to the
            // bare glyph while about 200 px of the row sat unused beside it (audit 2, D10).
            openInspector.Content = "Entry ⤢";
        }

        _timeline.Margin = splitTimeline ? new Thickness(6, 2, 3, 2) : new Thickness(0);
        _analysisGrid!.Margin = enabled
            ? splitTimeline ? new Thickness(3, 2, 6, 2) : new Thickness(6, 2)
            : new Thickness(8, 4);
        _chipBar.Margin = enabled ? new Thickness(6, 0, 6, 2) : new Thickness(10, 0, 10, 5);
        _chipBar.MinHeight = enabled ? 0 : 40;
        if (_statusBar is { } statusBar)
        {
            statusBar.Margin = enabled ? new Thickness(6, 1, 6, 2) : new Thickness(8, 4, 8, 12);
        }
    }
}
