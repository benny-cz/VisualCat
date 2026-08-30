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

        // The keyboard is not the only thing that can leave this card too little. A short
        // viewport gives the drawer the band below the command strip and no more: at
        // 434 x 498 dp the card is 228.3 dp, its fixed chrome - caption, field, the options
        // row, the chip bar and the pinned footer - is about 210 of that, and the scrolling
        // body was left **28.8 dp**. The drawer then drew two captions and nothing else: no
        // severity chips, no zoom controls, no readout, with the footer painted across what
        // did not fit (finding F-36). The card fills its band whatever its chrome does, so
        // this reads the card and not the body, and cannot oscillate between the two states.
        var card = panel.Bounds.Height;
        var shortCard = card > 0 && card < TightDrawerCardHeight;
        ApplyTightDrawerChrome((!double.IsInfinity(room) && room < TightDrawerHeight) || shortCard);
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
    /// The card height below which the drawer's fixed chrome yields so its body keeps a row.
    /// </summary>
    /// <remarks>
    /// The chrome that does not scroll - the query caption, field and options, the chip bar
    /// and the pinned decision footer - is about 210 dp, so a card under about 260 dp leaves
    /// the scrolling body less than one 48 dp row of controls. Trading the caption and the
    /// padding around both rows buys back about 42 dp, which is a whole row of severity
    /// toggles: the first thing the drawer exists to show.
    /// </remarks>
    private const double TightDrawerCardHeight = 260;

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
        // The mode row reports the composition the reader is actually looking at. When Split
        // cannot seat its four-row floor at this text size the workspace composes Details and
        // says so on the Split button, rather than leaving Split lit above a plot and two and
        // a half rows (V2-17).
        var effective = _splitStarvedByTextScale &&
                        _mobileWorkspaceState.DisplayMode is MobileWorkspaceDisplayMode.Split
            ? MobileWorkspaceDisplayMode.Details
            : _mobileWorkspaceState.DisplayMode;
        foreach (var (mode, button) in _mobileModeButtons)
        {
            var selected = mode == effective;
            var starved = _splitStarvedByTextScale && mode is MobileWorkspaceDisplayMode.Split;
            ApplyMobileChoiceAppearance(button, selected);
            button.FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
            var help = starved
                ? "Not enough room at this text size — showing Details"
                : selected
                    ? "Current workspace mode"
                    : "Switch workspace mode";
            AutomationProperties.SetHelpText(button, help);
            ToolTip.SetTip(button, starved ? help : null);
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
        // The pane question, not the command row's (A-06): whether both panes can have their
        // own minimum width. See MobilePaneAllocator.FitsSideBySide.
        var stackedCompact = wideComposition && timelineVisible && analysisVisible &&
                             !MobilePaneAllocator.FitsSideBySide(
                                 settled.Width,
                                 MobilePaneSplitter.LaneExtent);

        var minimapVisible = timelineVisible && hasOverview && layout.MinimapHeight > 0;

        // V2-17's second half. When the band cannot seat four whole rows even with the plot
        // pushed to the height below which it stops drawing a heat map at all, Split is a
        // promise the viewport cannot keep: at font_scale 2.0 the reader was shown a 137 dp
        // plot and two and a half rows of a fifty-thousand-entry log. Details is what the
        // reader wants at that size, and the mode row says so rather than silently changing
        // under them.
        var stacked = timelineVisible && analysisVisible && (!wideComposition || stackedCompact);
        _splitStarvedByTextScale = stacked &&
                                   !StackedSplitCanSeatEntryFloor(
                                       minimapVisible ? layout.MinimapHeight : 0);
        if (_splitStarvedByTextScale)
        {
            timelineVisible = false;
            minimapVisible = false;
            stackedCompact = false;
        }

        var composition = filtersOpen
            ? MobilePaneComposition.Filters
            : timelineVisible && !analysisVisible
                ? MobilePaneComposition.Plot
                : !timelineVisible && analysisVisible
                    ? MobilePaneComposition.Details
                    : timelineVisible && analysisVisible && wideComposition && !stackedCompact
                        ? MobilePaneComposition.SplitWide
                        : timelineVisible && analysisVisible
                            ? MobilePaneComposition.SplitStacked
                            : MobilePaneComposition.Unavailable;
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

        UpdateAnalysisChromeMeasurement();
        ApplyMobilePaneAllocation(layout, composition, minimapVisible ? layout.MinimapHeight : 0);
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

    private const int ManualEntryRowFloor = 1;
    private double _analysisChromeHeight;
    private double _entryRowDrawnHeight;
    private bool _splitStarvedByTextScale;

    /// <summary>
    /// Whether a stacked Split can still give the entries pane its four-row floor.
    /// </summary>
    /// <param name="minimapHeight">The overview strip's height, or zero when it is not shown.</param>
    /// <remarks>
    /// Answered from three measured quantities and one invariant, none of which depends on
    /// the composition this decides: the band rows 2-5 have between the command rows and the
    /// status row, the analysis pane's own arranged chrome, the drawn row height, and
    /// <see cref="MobilePaneAllocator.TimelineRenderingFloor"/> — the height below which the
    /// plot stops drawing a heat map at all. Unknown measurements answer <see langword="true"/>,
    /// so a workspace that has not been arranged yet is never downgraded on a guess.
    ///
    /// The restore threshold is one whole row above the downgrade threshold. Without that
    /// margin the two compositions measure marginally different chrome and the layout can
    /// alternate between them on consecutive passes.
    /// </remarks>
    private bool StackedSplitCanSeatEntryFloor(double minimapHeight)
    {
        if (_root.RowDefinitions.Count < 7)
        {
            return true;
        }

        var band = Bounds.Height
                   - _root.RowDefinitions[0].ActualHeight
                   - _root.RowDefinitions[1].ActualHeight
                   - _root.RowDefinitions[6].ActualHeight;
        var row = AllocatedEntryRowHeight;
        if (!double.IsFinite(band) || band <= 0 ||
            !double.IsFinite(row) || row <= 0 ||
            _analysisChromeHeight <= 0)
        {
            return true;
        }

        var required = _analysisChromeHeight
                       + (SplitEntryRowFloor * row)
                       + MobilePaneAllocator.TimelineRenderingFloor
                       + Math.Max(0, minimapHeight)
                       + MobilePaneSplitter.LaneExtent;
        return band >= (_splitStarvedByTextScale ? required + row : required);
    }
    private MobilePaneAllocation _lastMobilePaneAllocation;
    private (MobileWorkspaceLayout Layout, MobilePaneComposition Composition, double MinimapHeight)?
        _lastMobileComposition;

    private bool _splitDragActive;
    private double _splitDragInitialPlotHeight;
    private double _splitDragTotalHeight;
    private double? _splitDragInitialShare;
    private bool _splitDragChanged;

    /// <summary>Refreshes the allocator when arranged analysis chrome changes.</summary>
    /// <remarks>
    /// The pane's chrome is read from the arranged tree rather than restated here: the tab
    /// strip, the count line and the action rows all size themselves, and a constant copied
    /// from them would be wrong the first time one of them changed. One measured pass is
    /// enough because the chrome does not depend on the height it is given, so the second
    /// pass computes the same number and the row stops moving.
    /// </remarks>
    private void EnforceEntriesFloor()
    {
        if (!_mobile || _root.RowDefinitions.Count < 7 || _analysisGrid is null)
        {
            return;
        }

        // The measurement has to include the row itself, not only the chrome above it. The
        // pane's floor is stated in rows, and the number it was multiplied by was the design
        // constant 64 — while the drawn row is content-sized and reaches 96.9 dp at
        // font_scale 2.0. That is why the ListBox measured exactly 558 px at 1.0, 1.3 and 2.0
        // alike while the row grew 144 -> 156 -> 218 px underneath it, and the four-row floor
        // silently became three rows, then two and a half (V2-17).
        var moved = UpdateEntryRowMeasurement();
        if (UpdateAnalysisChromeMeasurement() || moved)
        {
            // The resolver consumes the measurement and owns every resulting row write. A
            // tolerance in the measurement and in ApplyMobilePaneAllocation makes this settle
            // after the one extra arrange pass the old entries-floor implementation needed.
            ApplyMobileLayout(Bounds.Size);
        }
    }

    /// <summary>
    /// The height a realised entry row actually draws at, in this text scale and this width.
    /// </summary>
    /// <remarks>
    /// Taken from the tallest realised container rather than from the style's
    /// <c>MinHeight</c>, because the row is content-sized: a two-line message, a wrapped
    /// metadata line and the reader's own text scale all raise it above the floor, and the
    /// floor is the only number the allocator used to see. A single realised row is enough —
    /// they share one template and one width — and the tallest is the honest one, since the
    /// selected row is the one that expands.
    /// </remarks>
    private bool UpdateEntryRowMeasurement()
    {
        if (!_entries.IsEffectivelyVisible)
        {
            return false;
        }

        var tallest = 0d;
        foreach (var item in _entries.GetRealizedContainers())
        {
            if (item.Bounds.Height > tallest)
            {
                tallest = item.Bounds.Height;
            }
        }

        if (tallest <= 0 || Math.Abs(tallest - _entryRowDrawnHeight) < 0.5)
        {
            return false;
        }

        _entryRowDrawnHeight = tallest;
        return true;
    }

    /// <summary>What the allocator last resolved, for a test that has to see the budget.</summary>
    internal MobilePaneAllocation LastMobilePaneAllocation => _lastMobilePaneAllocation;

    private double _noticeReserve;

    /// <summary>
    /// How much height the shell's notice lane is taking out of this view right now.
    /// </summary>
    /// <remarks>
    /// Pushed by the shell, because the lane is the shell's and its height depends on the
    /// message. Zero when no notice is showing, which is the ordinary case and the one every
    /// existing measurement was made in.
    /// </remarks>
    internal void SetNoticeReserve(double reserve)
    {
        var settled = double.IsFinite(reserve) && reserve > 0 ? reserve : 0;
        if (Math.Abs(settled - _noticeReserve) < 0.5)
        {
            return;
        }

        // The plot is pinned to the height it was actually drawn at, captured at the moment the
        // lane arrives — not to the share the allocator would resolve. Those two differ
        // whenever the analysis pane's four-row minimum is what is binding, which is the
        // ordinary case, and pinning to the share moved the pane the other way instead.
        if (settled > 0 && _noticeReserve <= 0 && _mobile && _root.RowDefinitions.Count > 2)
        {
            _pinnedTimelineHeight = _root.RowDefinitions[2].ActualHeight;
        }
        else if (settled <= 0)
        {
            _pinnedTimelineHeight = 0;
        }

        _noticeReserve = settled;
        if (_mobile)
        {
            RefreshMobilePaneAllocation();
        }
    }

    private double _pinnedTimelineHeight;

    /// <summary>
    /// The plot height to hold while the notice lane is showing, or zero to let it float.
    /// </summary>
    /// <remarks>
    /// Held only while the analysis pane can still seat its manual floor underneath it. A
    /// notice tall enough to squeeze the pane past that is a notice worth giving plot height
    /// to, and the ordinary weighted allocation takes over again.
    /// </remarks>
    private double ResolvePinnedTimelineHeight(double band, double minimapHeight, double laneHeight)
    {
        if (_pinnedTimelineHeight <= 0)
        {
            return 0;
        }

        var remaining = band - _pinnedTimelineHeight - Math.Max(0, minimapHeight) - laneHeight;
        var floor = _analysisChromeHeight + (ManualEntryRowFloor * AllocatedEntryRowHeight);
        return remaining >= floor ? _pinnedTimelineHeight : 0;
    }

    /// <summary>
    /// The row height the last allocation was actually resolved with, for the same reason.
    /// </summary>
    /// <remarks>
    /// Recorded at the call site rather than read back from the property, because V2-17 is
    /// exactly the difference between the two: the property could answer correctly while the
    /// request carried the constant.
    /// </remarks>
    internal double BudgetedEntryRowHeight => _budgetedEntryRowHeight;

    private double _budgetedEntryRowHeight;

    /// <summary>Whether Split has been composed as Details because it cannot seat its floor.</summary>
    internal bool SplitStarvedByTextScale => _splitStarvedByTextScale;

    /// <summary>
    /// The row height the allocator budgets with: the drawn one when it is known, the design
    /// floor otherwise.
    /// </summary>
    private double AllocatedEntryRowHeight =>
        double.IsFinite(_entryRowDrawnHeight) && _entryRowDrawnHeight > 0
            ? Math.Max(_entryRowDrawnHeight, _entryRowMinimumHeight)
            : _entryRowMinimumHeight;

    private bool UpdateAnalysisChromeMeasurement()
    {
        // Android can keep the Entries presenter effectively visible, with its last arranged
        // bounds, for a pass after another tab is selected. Subtracting those stale bounds
        // from the newly expanded Insights pane turns its content into "chrome" and pins the
        // next automatic split to the plot floor. Selection is the authoritative signal.
        if (_analysisGrid is not { IsVisible: true } analysis ||
            analysis.Bounds.Height <= 0 ||
            _mobileAnalysisTabs is { SelectedIndex: not 0 } ||
            !_entries.IsEffectivelyVisible ||
            _entries.Bounds.Height <= 0)
        {
            return false;
        }

        var chrome = analysis.Bounds.Height - _entries.Bounds.Height;
        if (!double.IsFinite(chrome) || chrome <= 0 || Math.Abs(chrome - _analysisChromeHeight) < 0.5)
        {
            return false;
        }

        _analysisChromeHeight = chrome;
        return true;
    }

    /// <summary>
    /// Re-resolves rows 2-5 from the last composition, without recomposing the workspace.
    /// </summary>
    /// <remarks>
    /// A drag asks for a new boundary many times a second, and the only thing that changed
    /// between two of those questions is the requested share. Recomposing the whole phone
    /// layout for each one is what makes a fast gesture on a large session drop frames, and
    /// dropped frames are what a reader feels as a divider that will not follow the finger.
    /// </remarks>
    private void RefreshMobilePaneAllocation()
    {
        if (_lastMobileComposition is { } cached)
        {
            ApplyMobilePaneAllocation(cached.Layout, cached.Composition, cached.MinimapHeight);
        }
    }

    private void ApplyMobilePaneAllocation(
        MobileWorkspaceLayout layout,
        MobilePaneComposition composition,
        double minimapHeight)
    {
        _lastMobileComposition = (layout, composition, minimapHeight);
        _budgetedEntryRowHeight = AllocatedEntryRowHeight;
        var band = Bounds.Height - _root.RowDefinitions[0].ActualHeight
                   - _root.RowDefinitions[1].ActualHeight
                   - _root.RowDefinitions[6].ActualHeight;

        // The shell's notice lane is docked below this whole view, so raising one shortens the
        // band. Both panes are star-sized, so both used to give up their share of it — and the
        // analysis pane's own top moved with the plot, which is what carried `Copy raw` 140 px
        // up the screen and turned a repeated tap into Open entry (V2-20).
        //
        // The band is therefore resolved as though the lane were not there, and the plot is
        // then pinned to the height that resolved. Everything above the entries list keeps its
        // coordinates and the list gives up the rows instead — which is the honest trade, since
        // a list is the one thing here that is *supposed* to hold as much as it is given.
        var reserve = Math.Max(0, _noticeReserve);
        var allocation = MobilePaneAllocator.Resolve(new MobilePaneAllocationRequest(
            composition,
            AvailableBandHeight: Math.Max(0, band + reserve),
            TimelineWeight: layout.TimelineWeight,
            AnalysisWeight: layout.AnalysisWeight,
            MinimapHeight: minimapHeight,
            SplitterLaneHeight: MobilePaneSplitter.LaneExtent,
            AnalysisChromeHeight: _analysisChromeHeight,
            EntryRowHeight: _budgetedEntryRowHeight,
            PreferredEntryRows: composition == MobilePaneComposition.Details
                ? DetailsEntryRowFloor
                : SplitEntryRowFloor,
            ManualEntryRows: ManualEntryRowFloor,
            TimelineShare: _mobilePaneSplitState.TimelineShare));

        _lastMobilePaneAllocation = allocation;
        var pinned = reserve > 0.5 && composition == MobilePaneComposition.SplitStacked
            ? ResolvePinnedTimelineHeight(band, minimapHeight, allocation.Splitter.Value)
            : 0;
        ApplyTrack(
            _root.RowDefinitions[2],
            pinned > 0 ? MobilePaneTrack.Pixels(pinned) : allocation.Timeline);
        ApplyTrack(_root.RowDefinitions[3], allocation.Minimap);
        ApplyTrack(_root.RowDefinitions[4], allocation.Splitter);
        ApplyTrack(_root.RowDefinitions[5], allocation.Analysis);
        ApplyLimits(
            _root.RowDefinitions[5],
            allocation.AnalysisMinimumHeight,
            allocation.AnalysisMaximumHeight);

        if (_mobilePaneSplitter is { } splitter)
        {
            var interactive = allocation.SplitterVisible && allocation.SplitterEnabled && !_failureVisible;
            splitter.SetRange(
                allocation.MinimumTimelineShare,
                allocation.MaximumTimelineShare,
                allocation.ResolvedTimelineShare);
            splitter.SetInteractive(interactive);
        }
    }

    /// <summary>Resolves and applies the side-by-side column widths and their divider.</summary>
    private void ApplyMobileWidthAllocation(bool sideBySide, double availableWidth)
    {
        if (_root.ColumnDefinitions.Count < 3 && sideBySide)
        {
            return;
        }

        var allocation = MobilePaneAllocator.ResolveWidth(new MobilePaneWidthRequest(
            sideBySide,
            AvailableWidth: availableWidth,
            PlotWeight: WidePlotWeight,
            AnalysisWeight: WideAnalysisWeight,
            SplitterLaneWidth: MobilePaneSplitter.LaneExtent,
            PlotMinimumWidth: MobilePaneAllocator.MinimumReadableTimelineWidth,
            AnalysisMinimumWidth: MobilePaneAllocator.MinimumUsableAnalysisWidth,
            PlotShare: _mobilePaneWidthSplitState.TimelineShare));

        _lastMobileWidthAllocation = allocation;
        if (_root.ColumnDefinitions.Count >= 3)
        {
            ApplyTrack(_root.ColumnDefinitions[0], allocation.Plot);
            ApplyTrack(_root.ColumnDefinitions[1], allocation.Splitter);
            ApplyTrack(_root.ColumnDefinitions[2], allocation.Analysis);
        }

        if (_mobileWidthSplitter is { } splitter)
        {
            var interactive = sideBySide && allocation.SplitterVisible &&
                              allocation.SplitterEnabled && !_failureVisible;
            splitter.SetRange(
                allocation.MinimumPlotShare,
                allocation.MaximumPlotShare,
                allocation.ResolvedPlotShare);
            splitter.SetInteractive(interactive);
        }
    }

    /// <summary>The automatic side-by-side weights, kept where the allocator can read them.</summary>
    private const double WidePlotWeight = 21;

    private const double WideAnalysisWeight = 29;

    private MobilePaneWidthAllocation _lastMobileWidthAllocation;
    private bool _widthDragActive;
    private double _widthDragInitialPlotWidth;
    private double _widthDragTotalWidth;
    private double? _widthDragInitialShare;
    private bool _widthDragChanged;

    private void BeginMobileWidthDrag()
    {
        if (_mobileWidthSplitter is not { IsEffectivelyEnabled: true } ||
            !_lastMobileWidthAllocation.SplitterEnabled ||
            _root.ColumnDefinitions.Count < 3)
        {
            return;
        }

        var plot = _root.ColumnDefinitions[0].ActualWidth;
        var analysis = _root.ColumnDefinitions[2].ActualWidth;
        if (!double.IsFinite(plot) || !double.IsFinite(analysis) || plot + analysis <= 0)
        {
            return;
        }

        _widthDragActive = true;
        _widthDragInitialPlotWidth = plot;
        _widthDragTotalWidth = plot + analysis;
        _widthDragInitialShare = _mobilePaneWidthSplitState.TimelineShare;
        _widthDragChanged = false;
    }

    private void ContinueMobileWidthDrag(double offsetFromPress)
    {
        if (!_widthDragActive || !double.IsFinite(offsetFromPress))
        {
            return;
        }

        _widthDragChanged |= ApplyUserTimelineWidthShare(
            (_widthDragInitialPlotWidth + offsetFromPress) / _widthDragTotalWidth);
    }

    private void CompleteMobileWidthDrag()
    {
        if (!_widthDragActive)
        {
            return;
        }

        _widthDragActive = false;
        if (_widthDragChanged &&
            !NullableShareEquals(_widthDragInitialShare, _mobilePaneWidthSplitState.TimelineShare))
        {
            SplitWidthShareChanged?.Invoke(_mobilePaneWidthSplitState.TimelineShare);
        }
    }

    private void NudgeMobileWidthSplit(double horizontalDelta)
    {
        if (!_lastMobileWidthAllocation.SplitterEnabled || _root.ColumnDefinitions.Count < 3)
        {
            return;
        }

        var plot = _root.ColumnDefinitions[0].ActualWidth;
        var analysis = _root.ColumnDefinitions[2].ActualWidth;
        if (plot + analysis <= 0)
        {
            return;
        }

        NotifyWidthShareChange(() => ApplyUserTimelineWidthShare((plot + horizontalDelta) / (plot + analysis)));
    }

    private void SetMobileTimelineWidthShareFromAutomation(double share) =>
        NotifyWidthShareChange(() => ApplyUserTimelineWidthShare(share));

    private void NotifyWidthShareChange(Func<bool> change)
    {
        var before = _mobilePaneWidthSplitState.TimelineShare;
        if (change() && !NullableShareEquals(before, _mobilePaneWidthSplitState.TimelineShare))
        {
            SplitWidthShareChanged?.Invoke(_mobilePaneWidthSplitState.TimelineShare);
        }
    }

    private bool ApplyUserTimelineWidthShare(double requestedShare)
    {
        if (!_lastMobileWidthAllocation.SplitterEnabled || !double.IsFinite(requestedShare))
        {
            return false;
        }

        var changed = _mobilePaneWidthSplitState.Set(Math.Clamp(requestedShare, 0.05, 0.95));
        ApplyMobileLayout(Bounds.Size);

        // A reader dragging against a hard stop chose that stop, so the effective value is
        // committed for this gesture; a later viewport-only clamp still leaves it alone.
        if (_lastMobileWidthAllocation.SplitterEnabled)
        {
            changed |= _mobilePaneWidthSplitState.Set(_lastMobileWidthAllocation.ResolvedPlotShare);
        }

        return changed;
    }

    private static void ApplyTrack(ColumnDefinition column, MobilePaneTrack track)
    {
        var unit = track.Unit == MobilePaneTrackUnit.Star ? GridUnitType.Star : GridUnitType.Pixel;
        if (column.Width.GridUnitType == unit && Math.Abs(column.Width.Value - track.Value) < 0.25)
        {
            return;
        }

        column.Width = new GridLength(track.Value, unit);
    }

    private static void ApplyTrack(RowDefinition row, MobilePaneTrack track)
    {
        var unit = track.Unit == MobilePaneTrackUnit.Star ? GridUnitType.Star : GridUnitType.Pixel;
        if (row.Height.GridUnitType == unit && Math.Abs(row.Height.Value - track.Value) < 0.25)
        {
            return;
        }

        row.Height = new GridLength(track.Value, unit);
    }

    private static void ApplyLimits(RowDefinition row, double minimum, double maximum)
    {
        minimum = double.IsFinite(minimum) ? Math.Max(0, minimum) : 0;
        maximum = double.IsFinite(maximum) ? Math.Max(minimum, maximum) : double.PositiveInfinity;

        // RowDefinition validates each assignment against the other bound. Change whichever
        // side has to move outwards first, then the side that moves inwards.
        if (minimum > row.MaxHeight)
        {
            SetMaximum(row, maximum);
            SetMinimum(row, minimum);
        }
        else if (maximum < row.MinHeight)
        {
            SetMinimum(row, minimum);
            SetMaximum(row, maximum);
        }
        else
        {
            SetMinimum(row, minimum);
            SetMaximum(row, maximum);
        }
    }

    private static void SetMinimum(RowDefinition row, double value)
    {
        if (!NearlyEqual(row.MinHeight, value))
        {
            row.MinHeight = value;
        }
    }

    private static void SetMaximum(RowDefinition row, double value)
    {
        if (!NearlyEqual(row.MaxHeight, value))
        {
            row.MaxHeight = value;
        }
    }

    private static bool NearlyEqual(double left, double right) =>
        left.Equals(right) || double.IsFinite(left) && double.IsFinite(right) && Math.Abs(left - right) < 0.25;

    private void BeginMobileSplitDrag()
    {
        if (_mobilePaneSplitter is not { IsEffectivelyEnabled: true } ||
            !_lastMobilePaneAllocation.SplitterEnabled)
        {
            return;
        }

        var plot = _root.RowDefinitions[2].ActualHeight + _root.RowDefinitions[3].ActualHeight;
        var analysis = _root.RowDefinitions[5].ActualHeight;
        if (!double.IsFinite(plot) || !double.IsFinite(analysis) || plot + analysis <= 0)
        {
            return;
        }

        _splitDragActive = true;
        _splitDragInitialPlotHeight = plot;
        _splitDragTotalHeight = plot + analysis;
        _splitDragInitialShare = _mobilePaneSplitState.TimelineShare;
        _splitDragChanged = false;
    }

    /// <summary>
    /// Resolves the boundary from the finger's total travel since the press.
    /// </summary>
    /// <remarks>
    /// Every position is derived from the same press baseline rather than summed, so a
    /// dropped, coalesced or duplicated pointer event cannot leave the divider offset from
    /// the finger, and a drag held against a hard stop comes straight back off it.
    /// </remarks>
    private void ContinueMobileSplitDrag(double offsetFromPress)
    {
        if (!_splitDragActive || !double.IsFinite(offsetFromPress))
        {
            return;
        }

        var requested = (_splitDragInitialPlotHeight + offsetFromPress) / _splitDragTotalHeight;
        _splitDragChanged |= ApplyUserTimelineShare(requested);
    }

    private void CompleteMobileSplitDrag()
    {
        if (!_splitDragActive)
        {
            return;
        }

        _splitDragActive = false;
        if (_splitDragChanged && !NullableShareEquals(_splitDragInitialShare, _mobilePaneSplitState.TimelineShare))
        {
            SplitShareChanged?.Invoke(_mobilePaneSplitState.TimelineShare);
        }
    }

    private void NudgeMobileSplit(double verticalDelta)
    {
        var plot = _root.RowDefinitions[2].ActualHeight + _root.RowDefinitions[3].ActualHeight;
        var analysis = _root.RowDefinitions[5].ActualHeight;
        if (!_lastMobilePaneAllocation.SplitterEnabled || plot + analysis <= 0)
        {
            return;
        }

        var before = _mobilePaneSplitState.TimelineShare;
        var changed = ApplyUserTimelineShare((plot + verticalDelta) / (plot + analysis));
        if (changed && !NullableShareEquals(before, _mobilePaneSplitState.TimelineShare))
        {
            SplitShareChanged?.Invoke(_mobilePaneSplitState.TimelineShare);
        }
    }

    private void SetMobileTimelineShareFromAutomation(double share)
    {
        var before = _mobilePaneSplitState.TimelineShare;
        var changed = ApplyUserTimelineShare(share);
        if (changed && !NullableShareEquals(before, _mobilePaneSplitState.TimelineShare))
        {
            SplitShareChanged?.Invoke(_mobilePaneSplitState.TimelineShare);
        }
    }

    private bool ApplyUserTimelineShare(double requestedShare)
    {
        if (!_lastMobilePaneAllocation.SplitterEnabled || !double.IsFinite(requestedShare))
        {
            return false;
        }

        requestedShare = Math.Clamp(requestedShare, 0.05, 0.95);
        var changed = _mobilePaneSplitState.Set(requestedShare);
        if (_splitDragActive)
        {
            RefreshMobilePaneAllocation();
        }
        else
        {
            ApplyMobileLayout(Bounds.Size);
        }

        // A user dragging against a hard stop chose that stop. Commit the effective value for
        // this gesture; later viewport-only clamps still leave an already stored share alone.
        if (_lastMobilePaneAllocation.SplitterEnabled)
        {
            changed |= _mobilePaneSplitState.Set(_lastMobilePaneAllocation.ResolvedTimelineShare);
        }

        return changed;
    }

    private static bool NullableShareEquals(double? left, double? right) =>
        left is null && right is null ||
        left is { } l && right is { } r && Math.Abs(l - r) < 0.0001;

    /// <summary>Where the load-more control currently lives, so it is only ever moved once.</summary>
    private bool? _loadMoreInHeader;

    /// <summary>
    /// The analysis width the header row needs before the load-more control may join it.
    /// </summary>
    /// <remarks>
    /// Measured on the device at 2.8125 px/dp: the sort control is 112 dp, <c>Copy</c> 49.4,
    /// <c>Entry &#x2921;</c> 63.6 and load-more 49.4, with four 6 dp gaps - 298.4 dp. Scaled by the
    /// reader's text size for the same reason the shared-row breakpoint is: the labels are
    /// what the number measures. Below it the control keeps its footer, where it costs a band
    /// and reads in full, rather than being laid out past the right edge of the pane.
    /// </remarks>
    private const double LoadMoreHeaderWidth = 300;

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
            // The whole band goes, Load all with it. This is the composition that has no room
            // for a band at all — a 136 dp analysis pane — and a second control there would
            // be the thing drawn through its own middle.
            _mobileFooterRow?.Children.Remove(_loadMore);
            footer.Child = null;
            _loadMore.Margin = new Thickness(6, 0, 0, 0);

            // In the footer it stretched across the pane; here it sizes to its own label, so
            // the count line beside it keeps the rest of the row.
            _loadMore.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(_loadMore, 3);
            header.ColumnDefinitions = new ColumnDefinitions("Auto,*,*,Auto");
            header.Children.Add(_loadMore);
        }
        else
        {
            header.Children.Remove(_loadMore);
            header.ColumnDefinitions = new ColumnDefinitions("Auto,*,*");
            _loadMore.Margin = new Thickness(0);
            _loadMore.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (_mobileFooterRow is { } row)
            {
                Grid.SetColumn(_loadMore, 0);

                // The first call arrives with _loadMoreInHeader still unset, and the control is
                // already where the footer put it when it was built. Adding it twice is a
                // second visual parent, which Avalonia refuses outright.
                if (!row.Children.Contains(_loadMore))
                {
                    row.Children.Insert(0, _loadMore);
                }

                footer.Child = row;
            }
            else
            {
                footer.Child = _loadMore;
            }
        }

        UpdateEntryLoadControls();
    }

    /// <summary>The narrowest the compact count line is worth showing at.</summary>
    private const double SummaryFloor = 72;

    /// <summary>The gap the compact count line keeps between itself and the last tab.</summary>
    private const double SummaryLead = 6;

    /// <summary>
    /// The width the count line may actually draw in, so it can choose what to say rather
    /// than be cut off saying it. Unbounded whenever it is not sharing the tab strip's row.
    /// </summary>
    private double _summaryRoom = double.PositiveInfinity;

    /// <summary>What the analysis tab strip spends on itself before its tabs get anything.</summary>
    private const double TabStripSlack = 16;

    /// <summary>The share and composition the tab strip was last given.</summary>
    private (double Share, bool Compact) _tabStripApplied = (double.NaN, false);

    /// <summary>
    /// Whether the tab strip's metrics have to be written again.
    /// </summary>
    /// <remarks>
    /// This runs on every arrange, and re-arranging the strip is only free while nothing has
    /// changed, so the numbers are compared before they are written.
    /// </remarks>
    private bool ApplyTabStripMetrics(TabControl tabs, double share, bool compact)
    {
        _ = tabs;
        if (Math.Abs(_tabStripApplied.Share - share) < 0.5 && _tabStripApplied.Compact == compact)
        {
            return false;
        }

        _tabStripApplied = (share, compact);
        return true;
    }

    /// <summary>
    /// Tells the strip that the row it laid out is not the row it has.
    /// </summary>
    /// <remarks>
    /// The strip arranges its three tabs once, from the sizes they had before this class ever
    /// wrote to them, and a later change re-measured each tab without re-arranging the row:
    /// the panel's own desired width changed and its <em>arrange</em> did not, so the slots
    /// stayed the width the first pass had given them. That is the whole of F-41. On the
    /// device the first pass ran before the headers were templated, so every tab was arranged
    /// at its bare <c>MinWidth</c> — 92.0 dp in portrait, 78.2 in landscape, byte-identical
    /// from 0.85x to 1.3x — and no later text size ever moved them; headlessly the first pass
    /// ran after templating, so the same defect showed as tabs frozen at their 1.0x content
    /// instead. Writing a new width without this left each tab drawing 25 dp outside its own
    /// slot, over its neighbour, with the first starting 16 dp off the left edge.
    ///
    /// The panel is reached through the presenter because it is the panel's arrange that is
    /// stale; invalidating the <see cref="TabControl"/> re-measures the strip and leaves the
    /// slots exactly where they were.
    /// </remarks>
    private static void RearrangeTabStrip(TabControl tabs)
    {
        if (tabs.GetVisualDescendants().OfType<ItemsPresenter>().FirstOrDefault()?.Panel is not { } strip)
        {
            return;
        }

        strip.InvalidateMeasure();
        strip.InvalidateArrange();
    }

    /// <summary>
    /// Uses the otherwise empty end of the three-tab strip for the compact count, returning
    /// its former row to the log. The same TextBlock moves, so its complete automation name
    /// and tooltip remain the one source of truth.
    /// </summary>
    /// <param name="intoTabs">Whether the compact composition is in force.</param>
    /// <param name="roomBesideTheTabs">
    /// What is left of the analysis pane once the tabs have taken their share. The caller
    /// owns that arithmetic because it is the caller that decides the share (F-41); this
    /// method used to re-derive it from a hard-coded 78 dp tab and the two then disagreed
    /// whenever the tabs were given anything else.
    /// </param>
    private void MoveSummaryIntoTabStrip(bool intoTabs, double roomBesideTheTabs)
    {
        // What the line may actually draw in: the host's cap, less the lead it keeps off the
        // last tab. The text it chooses is resolved against this (A-04), so the room has to
        // be recorded on every pass — including the passes where nothing else moves, which
        // are the ones a rotation and a divider drag arrive as.
        var cap = Math.Max(SummaryFloor, roomBesideTheTabs);
        var room = intoTabs ? cap - SummaryLead : double.PositiveInfinity;
        var roomMoved = Math.Abs(_summaryRoom - room) > 0.5;
        _summaryRoom = room;

        if (_mobileSummaryHost is not { } host || _entryHeader is not { } header ||
            _summaryInTabStrip == intoTabs)
        {
            if (intoTabs && _mobileSummaryHost is { } existing)
            {
                existing.MaxWidth = cap;
            }

            if (roomMoved)
            {
                UpdateSummaryText();
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
            host.MaxWidth = cap;
            host.Children.Add(_summary);
            host.IsVisible = true;
            _summary.Margin = new Thickness(SummaryLead, 0, 0, 0);
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

        // The placement decides which of the two forms is used at all, so the text is
        // resolved after the move rather than before it.
        UpdateSummaryText();
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
        // Side by side needs width, not just a short viewport. Compact height is chosen by
        // height alone, so a 360 dp portrait workspace under a tall notice reached this too -
        // and two columns of 360 dp left the analysis pane about 131 dp, which clipped "Show
        // the full message" to 12.3 dp while the same control measured 64.0 dp in Details on
        // the same screen (finding F-32). Below the threshold the plot and the pane stack, as
        // they do in an ordinary portrait workspace, and the pane gets the whole width; the
        // compact row structure and its shorter chrome still apply, because those save height
        // and height is what is actually short.
        //
        // The threshold is the one the panes themselves state, not the command row's, and the
        // two are not the same number (A-06). See MobilePaneAllocator.FitsSideBySide.
        var splitTimeline = enabled && timelineVisible && analysisVisible &&
                            MobilePaneAllocator.FitsSideBySide(
                                availableWidth,
                                MobilePaneSplitter.LaneExtent);
        var stackedCompact = enabled && timelineVisible && analysisVisible && !splitTimeline;

        // Three columns rather than two: the middle one is the divider's lane, and it is the
        // column model's only change. Every band that spans the workspace spans it too.
        var columnCount = splitTimeline ? 3 : 1;
        if (_root.ColumnDefinitions.Count != columnCount)
        {
            _root.ColumnDefinitions = new ColumnDefinitions(splitTimeline ? "21*,Auto,29*" : "*");
        }

        if (_mobileFilterShell is { } topStrip)
        {
            Grid.SetColumn(topStrip, 0);
            Grid.SetColumnSpan(topStrip, columnCount);
        }

        if (_mobileFilterPanel is { } drawer)
        {
            Grid.SetColumn(drawer, 0);
            Grid.SetColumnSpan(drawer, columnCount);
        }

        Grid.SetColumn(_chipBar, 0);
        Grid.SetColumnSpan(_chipBar, columnCount);
        if (_statusBar is { } workspaceStatus)
        {
            Grid.SetColumn(workspaceStatus, 0);
            Grid.SetColumnSpan(workspaceStatus, columnCount);
        }

        // In the wide composition the plot and the analysis pane are columns, so the plot
        // gives the minimap the last band of its own column and the analysis keeps all four
        // rows beside it.
        var wideMinimap = enabled && timelineVisible && minimapVisible;
        Grid.SetRow(_timeline, 2);
        Grid.SetRowSpan(_timeline, enabled && !stackedCompact ? wideMinimap ? 3 : 4 : 1);
        Grid.SetColumn(_timeline, 0);
        Grid.SetColumnSpan(_timeline, 1);

        if (_minimapFrame is { } wideMinimapFrame)
        {
            Grid.SetRow(wideMinimapFrame, enabled && !stackedCompact ? 5 : 3);
            Grid.SetColumn(wideMinimapFrame, 0);
            wideMinimapFrame.Margin = enabled
                ? new Thickness(6, 0, 3, 3)
                : new Thickness(76, 4, 12, 4);
        }

        if (_analysisGrid is { } analysis)
        {
            Grid.SetRow(analysis, enabled && !stackedCompact ? 2 : 5);
            Grid.SetRowSpan(analysis, enabled && !stackedCompact ? 4 : 1);
            Grid.SetColumn(analysis, splitTimeline ? 2 : 0);
            Grid.SetColumnSpan(analysis, 1);
        }

        ApplyMobileWidthAllocation(splitTimeline, availableWidth);

        if (_mobileFilterShell is { } filterShell &&
            _mobileQuickActions is { } quickActions)
        {
            // A short viewport usually has width to spare and no height at all, so the
            // capture controls move up beside the mode selector instead of taking a band of
            // their own; otherwise they keep their own full-width row.
            //
            // "Usually" is the whole of it. Compact height is selected by height alone, and a
            // 360 dp portrait workspace reaches it too - under a tall notice, or in
            // split-screen - where the merged row needs about 500 dp and has 360. Its last
            // control is then laid out past the right edge of the screen: Stop capture
            // measured 15.0 dp and the entry actions 12.3 dp, while the same controls in the
            // same session measured 97.3 dp and 64.0 dp with the notice dismissed
            // (finding F-32). Stop capture is the one control that ends a running recording,
            // so this is gated on the thing that actually constrains it. It is the same gate,
            // and the same 600 dp, that the query options below already use for the same
            // reason (C-06.2).
            //
            // On the width this row actually gets, which is not the workspace's. When the
            // strip is hosted in the shell row the application toolbar takes its own Auto
            // column first - about 282 dp - and the strip gets the star column that is left.
            // Deciding the merge on the workspace's width instead squeezed `Follow` to
            // **23.5 dp** at 780 dp, the Samsung landscape viewport §6, §7 and §8 built this
            // layout for: 780 passed the test, the strip had 498, and the merged row needs
            // about 640 (finding F-37). `Stop capture` survived it, which is why F-32's own
            // device check - taken in portrait, and reasoning about landscape rather than
            // measuring it - did not see this.
            var shellWidth = _compactCommandsExternallyHosted && filterShell.Bounds.Width > 0
                ? filterShell.Bounds.Width
                : availableWidth;
            var mergeCaptureRow = enabled && MobileWorkspaceLayout.SharesARow(shellWidth);
            filterShell.RowDefinitions = new RowDefinitions("Auto,Auto,Auto");
            filterShell.ColumnDefinitions = new ColumnDefinitions(mergeCaptureRow ? "Auto,*" : "*");
            Grid.SetRow(quickActions, 0);
            Grid.SetColumn(quickActions, 0);
            quickActions.Margin = mergeCaptureRow ? new Thickness(6, 3, 3, 3) : new Thickness(6, 3);
            quickActions.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (_mobileCaptureActions is { } captureActions)
            {
                Grid.SetRow(captureActions, mergeCaptureRow ? 0 : 1);
                Grid.SetColumn(captureActions, mergeCaptureRow ? 1 : 0);
                captureActions.Margin = mergeCaptureRow
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
            //
            // Two columns need the width for two columns. This was the sixth site keyed on
            // height alone, and the last one left: at 434 dp each column is 175.6 dp, which
            // wraps the seven severity chips into three rows of three, and the third row -
            // the one holding `Unknown` - was laid out below the bottom of the screen and did
            // not reach the accessibility tree at all (finding F-36). One column of 380 dp
            // holds all seven on a single 48 dp row, which is also the first thing a short
            // drawer has room to show.
            var twoColumns = enabled && MobileWorkspaceLayout.SharesARow(availableWidth);
            filterBody.ColumnDefinitions = new ColumnDefinitions(twoColumns ? "*,*" : "*");
            filterBody.RowDefinitions = new RowDefinitions(twoColumns ? "Auto" : "Auto,Auto");
            Grid.SetColumn(time, 0);
            Grid.SetRow(time, twoColumns ? 0 : 1);
            Grid.SetRowSpan(time, 1);
            Grid.SetColumn(severity, twoColumns ? 1 : 0);
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
                var beside = enabled && MobileWorkspaceLayout.SharesARow(availableWidth);
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

        var analysisWidth = splitTimeline ? (availableWidth * 0.58) - 78 : availableWidth - (enabled ? 78 : 0);

        // At Android's 1.3x text setting a 360 dp phone leaves each mode segment about
        // 52 dp wide. "Details" needs more than that and Avalonia clips the last glyphs
        // instead of signalling that the label continues. The full accessible name remains
        // "Show details workspace"; the compact visible label says what that mode presents.
        if (_mobileModeButtons.TryGetValue(MobileWorkspaceDisplayMode.Details, out var detailsMode))
        {
            detailsMode.Content = availableWidth < 300 * TextScale.Effective ? "Logs" : "Details";
        }

        // What the three tabs may take of the analysis pane. In the compact composition the
        // count line shares their row, so it keeps its own floor out of the budget first.
        var tabFloor = enabled ? 78.0 : 92.0;
        var tabBudget = enabled
            ? Math.Max(3 * tabFloor, analysisWidth - SummaryFloor - 12)
            : analysisWidth;

        // `analysisWidth` is the pane's, and the strip inside it pays for its own padding
        // and the selected-tab margin, so three tabs that add up to exactly the pane wrap to
        // a second row and take a band of log with them. The slack is what keeps one row one
        // row at every width measured.
        var tabShare = Math.Max(tabFloor, Math.Floor((tabBudget - TabStripSlack) / 3));

        if (_mobileAnalysisTabs is { } tabs && ApplyTabStripMetrics(tabs, tabShare, enabled))
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
                // The width the room actually allows, not a constant chosen at 1.0x text.
                // Min and Max together rather than `Width`, so the three are pinned by the
                // same constraint the strip already honours (F-41).
                item.MinWidth = tabShare;
                item.MaxWidth = tabShare;
                item.FontSize = TextScale.Of(enabled ? 12.5 : 14);
                item.Padding = enabled ? new Thickness(7, 0) : new Thickness(10, 0);

                // The floor is the platform's, in every composition. A short viewport pays
                // for every dp of chrome twice - once here and once in the header row below -
                // so this row was given a literal 42, which cleared the 40 dp Material floor
                // for a tab and missed the 48 dp floor for the thing a finger lands on. These
                // three tabs are the analysis pane's whole navigation, they are
                // `clickable="false"` in Android's tree so no sub-floor sweep has ever counted
                // them, and they measured 78.2 x 42.3 dp on the device in ordinary landscape
                // (finding F-38). 6 dp of one band is what the reach costs.
                item.MinHeight = TouchTarget.Minimum;
            }

            RearrangeTabStrip(tabs);
        }

        MoveSummaryIntoTabStrip(enabled, analysisWidth - (3 * tabShare) - 12);
        if (_entryHeader is { } entryHeader && _entryActions is { } entryActions)
        {

            // Composed for where the count line actually is, not for the viewport that was
            // expected to move it. MoveSummaryIntoTabStrip has just run, so this reads its
            // result rather than re-deriving it - and the two never agreed. The old gate was
            // `enabled && analysisWidth >= 330`, and `enabled` is exactly when the summary
            // leaves the header, so the "count line beside the actions" composition only ever
            // ran with no count line in it, and its cap - width held back for that absent
            // control - was pure loss. In a 434 dp portrait workspace the cap left the actions
            // 258.8 dp for 298.4 dp of controls, and `Load 500 more` was laid out past the
            // right edge of the screen: 34.1 dp of its own 49.4, ending on the 1220 px edge
            // (finding F-35). Audit 3/D2 met the same failure and answered it by raising the
            // cap's ratio from 0.62 to 0.78, which fits one viewport rather than stating an
            // invariant. There is no ratio here, because a row with one occupant is not
            // shared with anything.
            var summaryInHeader = !_summaryInTabStrip;
            entryHeader.RowDefinitions = new RowDefinitions(summaryInHeader ? "Auto,Auto,Auto" : "Auto,Auto");
            entryHeader.ColumnDefinitions = new ColumnDefinitions("*");
            Grid.SetRow(_summary, 0);
            Grid.SetColumn(_summary, 0);
            Grid.SetRow(entryActions, summaryInHeader ? 1 : 0);
            Grid.SetColumn(entryActions, 0);
            if (_entryOffPageBanner is { } offPageBanner)
            {
                // Last row whichever way the header reflowed, and the full width of it.
                Grid.SetRow(offPageBanner, summaryInHeader ? 2 : 1);
                Grid.SetColumn(offPageBanner, 0);
                Grid.SetColumnSpan(offPageBanner, 1);
            }

            // The row keeps the sort control at its left and its actions at its right through
            // its own star column, so stretching it costs the pane nothing and gives the last
            // control the width it measured for.
            entryActions.HorizontalAlignment = HorizontalAlignment.Stretch;
            entryActions.MaxWidth = double.PositiveInfinity;
            _summary.TextWrapping = summaryInHeader ? TextWrapping.Wrap : TextWrapping.NoWrap;
            _summary.TextTrimming = summaryInHeader ? TextTrimming.None : TextTrimming.CharacterEllipsis;
            _summary.FontSize = TextScale.Of(enabled ? 11 : 12);
            _summary.Margin = summaryInHeader ? new Thickness(0, 0, 0, 4) : new Thickness(6, 0, 0, 0);
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
        MoveLoadMore(intoHeader: enabled && analysisWidth >= LoadMoreHeaderWidth * TextScale.Effective);

        // Two lines to a row rather than three, in a viewport that has no height to give one.
        SetCompactEntryRows(enabled);

        // A row of entries is the point of the pane, so it is the last thing that gives way.
        // A short viewport still has to clear the 48 dp touch floor, which it does; the
        // 64 dp portrait row exists for comfort, not for reach.
        ApplyEntryRowHeight(enabled ? 48 : 64);

        // Every control this pane owns, through one seam and one floor. They were three
        // separate assignments of the same literal 42, and on the device that is 42.3 dp:
        // `Copy`, `Entry`, the sort selector and `Load 500 more` all measured it in ordinary
        // landscape, in Release, in §9's own evidence (finding F-38). A control is not less of
        // a touch target for being in a short viewport, and one list of them cannot acquire a
        // seventh member with a different answer.
        foreach (var control in new Control?[] { _order, _loadMore, _fitMatches, _clearScope, _copyRaw, _openInspector })
        {
            if (control is not null)
            {
                control.MinHeight = TouchTarget.Minimum;
            }
        }

        // Spacing is the panels' own (column and item spacing), so the labels change with
        // the available width and nothing else moves. Width alone is not the constraint:
        // 360 dp held the ordinary labels at 1.0x, but not at Samsung's 1.3x system text
        // setting. Comparing the pane with the effective text scale gives both viewports the
        // same readable-content budget and preserves the full accessible names/tooltips.
        var compactActionLabels = enabled || analysisWidth < 320 * TextScale.Effective;
        _order.Width = compactActionLabels ? 112 : 126;
        var actionLabelsChanged = false;
        if (_copyRaw is { } copyRaw)
        {
            var label = compactActionLabels ? "Copy" : "Copy raw";
            actionLabelsChanged |= !Equals(copyRaw.Content, label);
            copyRaw.Content = label;
            copyRaw.Padding = compactActionLabels ? new Thickness(8, 0) : new Thickness(12, 0);
        }

        if (_openInspector is { } openInspector)
        {
            // Always the word. "⤢" on its own is not identifiable on a touch device — there
            // is no pointer to hover for the tooltip — and the label was collapsing to the
            // bare glyph while about 200 px of the row sat unused beside it (audit 2, D10).
            var label = compactActionLabels ? "Entry" : "Entry ⤢";
            actionLabelsChanged |= !Equals(openInspector.Content, label);
            openInspector.Content = label;
            openInspector.Padding = compactActionLabels ? new Thickness(8, 0) : new Thickness(12, 0);
        }

        if (actionLabelsChanged && _entryPrimaryActions is { } primaryActions)
        {
            // These labels are selected after the first arranged size is known. Re-measure
            // their star columns now; otherwise a column can retain the wider initial label's
            // demand until another resize and leave the two stable actions visibly lopsided.
            primaryActions.InvalidateMeasure();
            primaryActions.InvalidateArrange();
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
