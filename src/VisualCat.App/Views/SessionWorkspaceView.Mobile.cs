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
        _inputPaneRect = _inputPaneOpen ? eventArgs.EndRect : default;
        if (!_inputPaneOpen)
        {
            SetCompactEditorActive(false);
        }

        ApplyMobileLayout(Bounds.Size);
    }

    /// <summary>
    /// Keeps the drawer's decision row above the keyboard rather than behind it.
    /// </summary>
    /// <remarks>
    /// The activity is declared <c>AdjustResize</c>, but the platform reports the keyboard as
    /// an occluding rectangle rather than by resizing the window, so the drawer went on being
    /// laid out against the whole screen: Reset and Done sat 723 px below the top of the
    /// keyboard, and the drawer <em>grew</em> when the IME opened, because the status row
    /// yields its band to the drawer at that moment — pushing the footer further under, not
    /// less far (audit 3, C5). The field's action key applied the filter and closed the panel,
    /// so the reader was never trapped; the visible route out was simply hidden at the moment
    /// they were most likely to want it.
    ///
    /// The occluded rectangle is in top-level coordinates, so the panel's own top is
    /// translated into them and the difference is what the panel may occupy. Nothing is
    /// applied while the panel has not been arranged yet, or where the answer would be too
    /// small to be a drawer — a keyboard covering nearly everything is better answered by the
    /// panel scrolling than by a 40 px card.
    /// </remarks>
    private void ApplyInputPaneRoom()
    {
        if (_mobileFilterPanel is not { } panel)
        {
            return;
        }

        // Some Android keyboards publish their final state before Avalonia attaches this
        // workspace's StateChanged handler. Polling the current properties makes the geometry
        // correct even when no transition callback belongs to this view (Samsung/One UI,
        // F-10); the event is still used for animation-time updates.
        if (_inputPane is { } inputPane)
        {
            _inputPaneOpen = inputPane.State == InputPaneState.Open;
            if (_inputPaneOpen && inputPane.OccludedRect.Height > 0)
            {
                _inputPaneRect = inputPane.OccludedRect;
            }
        }

        var room = double.PositiveInfinity;
        if (_inputPaneOpen &&
            _mobileFiltersOpen &&
            _inputPaneRect.Height > 0 &&
            TopLevel.GetTopLevel(this) is { } topLevel &&
            panel.Bounds.Height > 0 &&
            panel.TranslatePoint(default, topLevel) is { } origin)
        {
            var available = _inputPaneRect.Y - origin.Y - panel.Margin.Bottom;
            if (available >= MinimumDrawerHeightOverKeyboard)
            {
                room = available;
            }
        }

        // Portrait reflowed correctly and landscape did not move at all: the keyboard's top
        // edge lands about 186 dp down a 434 dp viewport, the drawer starts at the top of the
        // workspace band, and the room left came out just under the old 190 dp floor — so the
        // guard declined to constrain the card and Reset, Done, the severity toggles and all
        // but the top few pixels of the query field stayed behind the keyboard, with no visual
        // cue that Done existed (finding F-10). The drawer is already built to survive this:
        // its body is a scroller and its footer is pinned under it, so what a short band costs
        // is scrolling, not reachability. The floor is therefore the footer plus one row of
        // body — below which there is genuinely nothing to show — rather than a whole card.

        // Guarded: this runs from a layout pass, and an unguarded write would invalidate the
        // layout it was just told about.
        if (Math.Abs(panel.MaxHeight - room) > 0.5)
        {
            panel.MaxHeight = room;
        }

        // A stretched control smaller than its slot is centred, which would open a gap above
        // the drawer and put the footer back where it started. Constrained, it hangs from the
        // top of the band it was given.
        var wanted = double.IsInfinity(room) ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        if (panel.VerticalAlignment != wanted)
        {
            panel.VerticalAlignment = wanted;
        }

        ApplyTightDrawerChrome(!double.IsInfinity(room) && room < TightDrawerHeight);
    }

    /// <summary>
    /// Spends the drawer's remaining chrome on its two working rows when the keyboard has
    /// left it less room than they need.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A 48 dp field and a 48 dp decision row are 96 dp of content, and a landscape keyboard
    /// on a 393 dp screen leaves the drawer 93 (finding F-30). No arrangement fits both whole,
    /// so the order of what goes is decided here rather than by whichever one the layout
    /// happens to reach last: the caption first — its words are already on the field, as the
    /// field's accessible name and as the hint inside it — and then the padding around both
    /// rows, which buys back 22 px of the 30 the two rows are short.
    /// </para>
    /// <para>
    /// What is never traded is the field. A reader typing into a drawer needs to see what
    /// they are typing more than they need eight pixels of air around it, and the decision
    /// row stays legible and touchable at the height that is left.
    /// </para>
    /// </remarks>
    private void ApplyTightDrawerChrome(bool tight)
    {
        if (_mobileQueryCaption is { } caption && caption.IsVisible == tight)
        {
            caption.IsVisible = !tight;
        }

        if (_mobileQuerySection is { } section)
        {
            var margin = tight ? new Thickness(8, 2) : new Thickness(8);
            if (section.Margin != margin)
            {
                section.Margin = margin;
            }
        }

        if (_mobileFilterFooter is { } footer)
        {
            var margin = tight ? new Thickness(8, 2, 8, 2) : new Thickness(8, 2, 8, 8);
            if (footer.Margin != margin)
            {
                footer.Margin = margin;
            }
        }
    }

    /// <summary>
    /// The drawer height below which the query section drops its caption to keep its field.
    /// </summary>
    private const double TightDrawerHeight = 132;

    /// <summary>
    /// Below this there is no drawer to show: the pinned footer plus one row of scrollable body.
    /// </summary>
    /// <remarks>
    /// It was 190 — a whole card — and a landscape keyboard leaves about 184, so the one
    /// viewport that most needed the constraint was the one that never got it (finding F-10).
    /// A band that fits the footer and a row of the body is usable: the body scrolls, Reset and
    /// Done stay on screen, and the reader can see what they are typing into.
    /// </remarks>
    private const double MinimumDrawerHeightOverKeyboard = 64;

    /// <summary>
    /// Gives the focused query editor the compact-height chrome that the IME would otherwise
    /// cover, and restores it as one atomic composition when editing ends.
    /// </summary>
    private void SetCompactEditorActive(bool active)
    {
        active &= _mobile &&
                  _mobileFiltersOpen &&
                  _mobileLayoutMode == MobileWorkspaceMode.CompactHeight;
        if (_compactEditorActive == active)
        {
            return;
        }

        _compactEditorActive = active;
        CompactEditorChanged?.Invoke(active);
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
        if (!open)
        {
            SetCompactEditorActive(false);
        }

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
        if (!wideComposition && _compactEditorActive)
        {
            SetCompactEditorActive(false);
        }

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

        // The full placeholder is 29 characters and the landscape query field is 444 px:
        // it rendered as "Search message text" with "or regex…" silently gone and no ellipsis
        // to show that anything had been cut — which matters because that is the only place
        // the interface says the field takes a pattern (finding F-11). Seventeen characters
        // say the same thing and fit.
        if (_mobileSearchPlaceholder is { } placeholder)
        {
            placeholder.Text = wideComposition
                ? "Search or regex…"
                : "Search message text or regex…";
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
        //
        // It keeps its slot, though. Taking the control out of the layout let the three mode
        // segments spread into the space it left: Details' right edge travelled 158 px on the
        // single tap that hid Fit, so a second tap at the place Details had just been hit
        // Split and undid the switch (audit 3, C4). Opening the drawer did the same. Held
        // rather than removed, the row's geometry is the same in every state, and the slot
        // sizes itself from the button, so it stays right at any text scale.
        if (_mobileFit is { } fit)
        {
            ControlSlot.Hold(fit, timelineVisible);
        }

        EnforceEntriesFloor();
        ApplyInputPaneRoom();
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
            // than a band under it: it already spans every row there is, so a minimum on one
            // of them adds to the grid's height instead of taking from a neighbour, and the
            // status line goes off the bottom.
            //
            // Which is why the short viewport's answer is not here. Measured in landscape
            // with the pane holding the whole band: analysis tab strip 42 dp, count-and-sort
            // row 42 dp, Load-more footer 42 dp, and 75 dp — 1.3 rows — of actual log
            // (audit 3, D2). There is no row above to take from; what the list is short of is
            // the chrome in its own column, so that is where it is taken from. See
            // ConfigureWideMobileComposition, which gives the footer's band back to the list.
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

    /// <summary>Where the load-more control currently lives, so it is only ever moved once.</summary>
    private bool? _loadMoreInHeader;

    /// <summary>
    /// Puts the load-more control in the header row, or back in its own footer under the list.
    /// </summary>
    /// <remarks>
    /// Both placements are right for the viewport they belong to. Under the list is where the
    /// gesture ends and where a reader who has run out of rows is already looking, and that is
    /// worth a band on a tall screen. On a short one the band is the thing the list is short
    /// of, and the row above has width going spare — so the control moves rather than the list
    /// giving up a third of what it has (audit 3, D2).
    /// </remarks>
    private void MoveLoadMore(bool intoHeader)
    {
        if (_loadMoreInHeader == intoHeader ||
            _entryFooter is not { } footer ||
            _entryPrimaryActions is not { } header)
        {
            return;
        }

        _loadMoreInHeader = intoHeader;
        if (intoHeader)
        {
            footer.Child = null;
            _loadMore.Margin = new Thickness(6, 0, 0, 0);

            // In the footer it stretched across the pane; here it sizes to its own label, so
            // the count line beside it keeps the rest of the row.
            _loadMore.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(_loadMore, 4);
            header.ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto");
            header.Children.Add(_loadMore);
        }
        else
        {
            header.Children.Remove(_loadMore);
            header.ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto");
            _loadMore.Margin = new Thickness(0);
            _loadMore.HorizontalAlignment = HorizontalAlignment.Stretch;
            footer.Child = _loadMore;
        }

        UpdateEntryLoadControls();
    }

    /// <summary>
    /// Uses the otherwise empty end of the three-tab strip for the compact count, returning
    /// its former row to the log. The same TextBlock moves, so its complete automation name
    /// and tooltip remain the one source of truth.
    /// </summary>
    private void MoveSummaryIntoTabStrip(bool intoTabs, double availableAnalysisWidth)
    {
        if (_mobileSummaryHost is not { } host || _entryHeader is not { } header ||
            _summaryInTabStrip == intoTabs)
        {
            if (intoTabs && _mobileSummaryHost is { } existing)
            {
                existing.MaxWidth = Math.Max(72, availableAnalysisWidth - (3 * 78) - 12);
            }

            return;
        }

        if (_summary.Parent is Panel current)
        {
            current.Children.Remove(_summary);
        }

        _summaryInTabStrip = intoTabs;
        if (intoTabs)
        {
            host.MaxWidth = Math.Max(72, availableAnalysisWidth - (3 * 78) - 12);
            host.Children.Add(_summary);
            host.IsVisible = true;
            _summary.Margin = new Thickness(6, 0, 0, 0);
            _summary.TextWrapping = TextWrapping.NoWrap;
            _summary.TextTrimming = TextTrimming.CharacterEllipsis;
            _summary.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            host.Children.Remove(_summary);
            host.IsVisible = false;
            Grid.SetRow(_summary, 0);
            Grid.SetColumn(_summary, 0);
            header.Children.Insert(0, _summary);
            _summary.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
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
            _mobileSeveritySection is { } severity &&
            _mobileTimeSection is { } time)
        {
            // QUERY is not in this grid: it is the drawer's own first band, above the
            // scroller, in every state (see the panel grid). Compact height is also possible
            // in a narrow portrait workspace — below a recovery notice, or in split-screen —
            // so the editor could never own only half the width anyway: Regex,
            // Case-sensitive and Clear reduced it to 64 dp headlessly and to no Android node
            // at all on the 360 dp Samsung (C-06.2). What is left here is TIME and the taller
            // SEVERITY group, which share the two columns a short viewport has to spare and
            // stack on a portrait one.
            filterBody.ColumnDefinitions = new ColumnDefinitions(enabled ? "*,*" : "*");
            filterBody.RowDefinitions = new RowDefinitions(enabled ? "Auto" : "Auto,Auto");
            Grid.SetColumn(time, 0);
            Grid.SetRow(time, enabled ? 0 : 1);
            Grid.SetRowSpan(time, 1);
            Grid.SetColumn(severity, enabled ? 1 : 0);
            Grid.SetRow(severity, 0);

            // Not spanned. A row-spanning element's height is shared out across the rows it
            // covers, so the severity group — the tallest of the three — pushed the first row
            // to nearly twice its own content and drove TIME LENS off the bottom of the
            // scroller again. One group per cell keeps each row the height of what is in it:
            // measured, the whole body then fits the landscape viewport with room to spare
            // instead of overflowing it by 139 px (audit 3, D2).
            Grid.SetRowSpan(severity, 1);
            severity.VerticalAlignment = VerticalAlignment.Top;

            // Regex and Case-sensitive move beside the field only when the whole drawer is
            // actually wide. A short 360 dp portrait workspace can select compact-height
            // composition too; there the options stay directly below the editor so its
            // width never collapses. The body scrolls while the decision footer stays pinned.
            if (_mobileQueryRow is { } queryRow &&
                _mobileQuerySection is StackPanel querySection &&
                _mobileQueryOptions is { } queryOptions)
            {
                var beside = enabled && availableWidth >= 600;
                if (beside && queryOptions.Parent != queryRow)
                {
                    querySection.Children.Remove(queryOptions);
                    Grid.SetColumn(queryOptions, 2);
                    queryRow.Children.Add(queryOptions);
                }
                else if (!beside && queryOptions.Parent != querySection)
                {
                    queryRow.Children.Remove(queryOptions);
                    querySection.Children.Add(queryOptions);
                }
            }
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

        var analysisWidth = splitTimeline ? (availableWidth * 0.58) - 78 : availableWidth - (enabled ? 78 : 0);
        MoveSummaryIntoTabStrip(enabled, analysisWidth);
        if (_entryHeader is { } entryHeader && _entryActions is { } entryActions)
        {

            // 400 was still above what a split landscape phone actually offers: a 1080 px
            // portrait device turned sideways leaves this pane about 374 px, so the summary
            // went on taking a second full touch row and the entries list was left 106 px of
            // a 415 px pane (audit 2, A2/D9). At 330 the count line keeps an ellipsised line
            // of its own beside the actions, which is what it is for.
            var sideBySide = enabled && analysisWidth >= 330;
            entryHeader.RowDefinitions = new RowDefinitions(sideBySide ? "Auto,Auto" : "Auto,Auto,Auto");
            entryHeader.ColumnDefinitions = new ColumnDefinitions(sideBySide ? "*,Auto" : "*");
            Grid.SetRow(_summary, 0);
            Grid.SetColumn(_summary, 0);
            Grid.SetRow(entryActions, sideBySide ? 0 : 1);
            Grid.SetColumn(entryActions, sideBySide ? 1 : 0);
            if (_entryOffPageBanner is { } offPageBanner)
            {
                // Last row whichever way the header reflowed, and the full width of it.
                Grid.SetRow(offPageBanner, sideBySide ? 1 : 2);
                Grid.SetColumn(offPageBanner, 0);
                Grid.SetColumnSpan(offPageBanner, sideBySide ? 2 : 1);
            }
            entryActions.HorizontalAlignment = sideBySide
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Stretch;
            // The cap keeps the actions from eating the count line beside them. It has to know
            // how many controls are in the row: with the load-more control moved up here, a
            // cap sized for three left the fourth to be laid out past the right edge of the
            // pane, and in Split it landed off the screen entirely (audit 3, D2).
            entryActions.MaxWidth = sideBySide
                ? Math.Clamp(analysisWidth * (enabled ? 0.78 : 0.62), 240, 560)
                : double.PositiveInfinity;
            _summary.TextWrapping = sideBySide ? TextWrapping.NoWrap : TextWrapping.Wrap;
            _summary.TextTrimming = sideBySide ? TextTrimming.CharacterEllipsis : TextTrimming.None;
            _summary.FontSize = TextScale.Of(enabled ? 11 : 12);
            _summary.Margin = sideBySide ? new Thickness(0, 0, 8, 0) : new Thickness(0, 0, 0, 4);
        }

        // The load-more control stops taking a band of its own where there is no band to
        // spare. Measured in landscape, the pane spent three 42 dp bands on chrome — the tab
        // strip, the count-and-sort row and this — over 75 dp of actual log, which is 1.3 rows
        // (audit 3, D2). A short viewport is short and wide by definition, and the row above
        // has 810 dp to lay four controls out in, so this joins it and the band goes back to
        // the list. It is a footer again the moment the viewport is tall enough to hold one,
        // which is where the argument for putting it under the list still applies.
        // Only where the pane is wide enough to hold it beside the count line and the three
        // controls already there: in Split the analysis column is 458 dp, and a fourth control
        // laid out past its right edge is worse than the band it was saving.
        MoveLoadMore(intoHeader: enabled && analysisWidth >= 300);

        // Two lines to a row rather than three, in a viewport that has no height to give one.
        SetCompactEntryRows(enabled);

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
