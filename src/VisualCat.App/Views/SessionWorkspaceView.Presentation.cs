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
    private bool _statusExpanded;

    /// <summary>What the status affordance was last decided against.</summary>
    private (string? Text, double Width, bool Expanded) _statusShape = (null, double.NaN, false);

    /// <summary>
    /// Switches the status row between one clipped line and the whole sentence.
    /// </summary>
    private void SetStatusExpanded(bool expanded)
    {
        _statusExpanded = expanded;
        _status.SetExpanded(expanded);
        _searchStatus.TextWrapping = expanded ? TextWrapping.Wrap : TextWrapping.NoWrap;
        _searchStatus.TextTrimming = expanded ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        if (_statusBar is { } statusBar)
        {
            statusBar.ClipToBounds = !expanded;
            if (_statusChevron is { } chevron)
            {
                chevron.Text = expanded ? "⌃" : "⌄";
            }
        }

        UpdateStatusAffordance();
    }

    /// <summary>
    /// Shows the disclosure only while the status line has something behind it, and says so.
    /// </summary>
    /// <remarks>
    /// Whether the line is clipped is a fact about the arranged layout, not about the string:
    /// the same sentence fits in landscape and does not in portrait, and the drawer opening
    /// changes the answer again. So it is read off the text layout after each pass, and the
    /// row is only tappable, and only carries the promise, while the answer is yes
    /// (audit 3, C2). Every write is guarded, because this runs from
    /// <see cref="Layoutable.LayoutUpdated"/> and an unguarded one would invalidate the layout
    /// it was just told about.
    /// </remarks>
    private void UpdateStatusAffordance()
    {
        if (!_mobile || _statusChevron is not { } chevron || _statusBar is not { } statusBar)
        {
            return;
        }

        // This runs from every layout pass, and the answer can only change when the sentence
        // or the width it is being laid out in changes. Both are cheap to compare and the
        // text layout behind StatusOverflows is not cheap to walk.
        var shape = (_status.Text, _status.ArrangedWidth, _statusExpanded);
        if (shape == _statusShape)
        {
            return;
        }

        _statusShape = shape;
        var revealable = StatusOverflows();
        if (chevron.IsVisible != revealable)
        {
            chevron.IsVisible = revealable;
        }

        // A touch target is 48 dp while it is a touch target, and a label the rest of the time.
        // The row measured 17.8 dp, which is not a target — but making it 48 unconditionally
        // would spend a band of a phone screen on a line of text that has nothing behind it,
        // and on a 560 px viewport that band comes straight out of the entries list.
        var wanted = revealable ? 48d : 0d;
        if (Math.Abs(statusBar.MinHeight - wanted) > 0.5)
        {
            statusBar.MinHeight = wanted;
        }

        var help = revealable
            ? _statusExpanded ? "Tap to shorten the status line." : "Tap to show the whole status line."
            : null;
        if (!string.Equals(AutomationProperties.GetHelpText(statusBar), help, StringComparison.Ordinal))
        {
            AutomationProperties.SetHelpText(statusBar, help);
        }
    }

    /// <summary>Whether the status line is showing less than it holds.</summary>
    /// <remarks>
    /// Collapsed, the question is whether trimming actually took anything. Expanded, trimming
    /// is off and wrapping is on, so the same question is whether it took more than one line —
    /// which is what it would have been trimmed to.
    /// </remarks>
    private bool StatusOverflows()
    {
        if (_status.Layout is not { } layout || layout.TextLines.Count == 0)
        {
            return false;
        }

        if (_statusExpanded)
        {
            return layout.TextLines.Count > 1;
        }

        foreach (var line in layout.TextLines)
        {
            if (line.HasCollapsed)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Writes the status line and everything a screen reader is told about it.
    /// </summary>
    /// <remarks>
    /// The status changes several times a second and only its <em>text</em> was being
    /// rewritten: the accessible name and description were set once, by the full presentation
    /// refresh, and Android's node then kept the first description it read forever. A
    /// finished session went on being announced as "Starting capture", and a session of
    /// 59 640 entries as "Importing…" (audit 2, B2). That was fixed by routing every write
    /// through this method — and five routes then went around it anyway (finding F-05), which
    /// is why the line itself is now a <see cref="StatusLine"/> whose only mutator reads the
    /// view model. There is no longer a text property for a sixth route to poke.
    /// </remarks>
    private void ApplyStatusText() => _status.Refresh();

    private void RefreshPresentation()
    {
        ApplyStatusText();
        _searchStatus.Text = _viewModel.SearchStatus;
        UpdateMarkerNavigation();

        UpdateFollowButton();
        _search.Text = _viewModel.SearchText;
        _order.SelectedIndex = _viewModel.EntryOrder == EntryOrder.SourceSequence ? 1 : 0;
        UpdateTimelines();
        UpdateStatistics();
        UpdateEntryLoadControls();
        UpdateCaptureActions();
    }

    private void UpdateFollowButton()
    {
        var following = _viewModel.FollowLatest;
        _follow.Content = _mobile
            ? following ? "Follow ✓" : "Follow"
            : following ? "Follow: on" : "Follow: off";
        if (_mobile)
        {
            ApplyMobileChoiceAppearance(_follow, following);
        }
    }

    private void UpdateTimelines()
    {
        UpdateTimelineLevels();
        if (_viewModel.Snapshot is not null)
        {
            // The axis reads in the same zone as the rows; a plot an offset away from the
            // table would be worse than either choice on its own.
            _timeline.SetTimeZoneContext(DisplayZoneId());
        }

        _timeline.SetResult(_viewModel.HeatMap, _viewModel.Snapshot?.TimedRange);
        _timeline.SetSearchResult(_viewModel.SearchResult);
        _minimap.SetResult(_viewModel.Overview, _viewModel.Viewport, _viewModel.Snapshot?.TimedRange);
        UpdateMinimapVisibility();
        _zoomReadout.Text = _viewModel.Viewport is { } viewport
            ? $"{FormatSpan(viewport.DurationUs)} · {FormatResolution(viewport.DurationUs / Math.Max(1d, _viewModel.HeatMap?.Viewport.DevicePixelWidth ?? 1))}"
            : string.Empty;
        UpdateSessionInfo();
    }

    /// <summary>
    /// Keeps the minimap out of the layout until there is a session to overview. An empty
    /// frame is a bordered rectangle floating mid-screen promising a control that cannot do
    /// anything yet (finding 28), and on a phone it costs a row that the plot can use.
    /// </summary>
    private void UpdateMinimapVisibility()
    {
        if (_minimapFrame is not { } frame)
        {
            return;
        }

        var hasOverview = _viewModel.Overview is not null && _viewModel.Snapshot?.TimedRange is not null;
        if (_mobile)
        {
            // The mobile layout owns this row's height; it consults the same condition.
            ApplyMobileLayout(Bounds.Size);
            return;
        }

        frame.IsVisible = hasOverview;
        _root.RowDefinitions[3].Height = hasOverview
            ? new GridLength(62, GridUnitType.Pixel)
            : new GridLength(0, GridUnitType.Pixel);
    }

    private void UpdateTimelineLevels() =>
        _timeline.SetDisplayLevels(_viewModel.Filter.IncludedLevels, _viewModel.HasUnknownLevelEntries);

    /// <summary>
    /// Keeps the capture controls and the empty plot describing the state the session is
    /// actually in.
    /// </summary>
    /// <remarks>
    /// This used to read the state by matching prefixes of the status line, which made every
    /// decision here depend on the exact wording of a sentence written for the reader. It
    /// switches on <see cref="SessionTabViewModel.Activity"/> instead — which is also what
    /// lets Follow and the new-data jump disappear when the capture ends, instead of going on
    /// offering to follow a source that has closed and to jump to data that is not coming
    /// (finding 27).
    /// </remarks>
    private void UpdateCaptureActions()
    {
        var activity = _viewModel.Activity;
        var live = _viewModel.IsLiveSourceAttached;
        var stopping = activity == SessionActivity.Stopping;
        _follow.IsVisible = live;

        // Both controls stay in place while the capture finishes and neither does anything:
        // there is no longer a live edge to follow, and the stop is already under way. Kept
        // visible rather than removed so the row does not reflow under a thumb that is still
        // on it, and disabled so that a second press is answered by the button itself.
        _follow.IsEnabled = !stopping;
        _stopCapture.IsVisible = live;
        _stopCapture.IsEnabled = !stopping;
        _stopCapture.Content = stopping ? "Stopping…" : "Stop capture";
        _newData.IsVisible = live && _viewModel.HasNewData;

        // The band exists only while there is a capture to control, so a session being read
        // back does not pay a touch row for three hidden buttons (audit 2, A2).
        if (_mobileCaptureActions is { } captureActions)
        {
            captureActions.IsVisible = live;
        }

        var (title, detail) = DescribeEmptyPlot(activity);
        _timeline.SetEmptyState(title, detail);
        UpdateFailureState();
    }

    /// <summary>
    /// What the plot says while it has nothing to draw. Every branch names the state the
    /// session is in: "Open a logcat file or start a live capture" was shown during an
    /// import too, telling the reader to do the thing they had just done (finding 28).
    /// </summary>
    private (string Title, string Detail) DescribeEmptyPlot(SessionActivity activity)
    {
        switch (activity)
        {
            case SessionActivity.Queued when _viewModel.IsLiveCaptureActive:
                return ("Preparing live capture…", "Waiting for an available capture slot.");
            case SessionActivity.Queued:
                return (
                    "Waiting to read this log…",
                    _viewModel.QueuedBehind is { Length: > 0 } ahead
                        ? $"{ahead} is being read first — its own tab shows how far it has got."
                        : "Another log is being read first.");
            case SessionActivity.Importing:
                return ("Reading the log…", "The severity × time signal fills in as entries become readable.");
            case SessionActivity.Connecting:
                return ("Connecting to the device…", "Checking the device and logcat format.");
            case SessionActivity.Starting:
                return ("Starting live capture…", "Waiting for the first log entry.");
            case SessionActivity.Capturing:
                return ("Live capture is running", "Waiting for the first visible log entry.");
            case SessionActivity.Stopping:
                // Reached only by a capture stopped before it committed anything visible.
                // The plot has no data to keep showing, so it says what the status bar says
                // rather than falling through to "Open a logcat file or start a live
                // capture" — an instruction to do the thing that is finishing right now.
                return ("Finishing this capture…", "Saving what was captured and closing the session.");
            case SessionActivity.Failed:
                return (
                    "This log could not be read",
                    _viewModel.FailureReason ?? "The import ended in a failure.");
            case SessionActivity.Ready or SessionActivity.Stopped
                when _viewModel.Snapshot?.Descriptor.Counters.ParsedEntries == 0:
                // An own-app capture of an idle app is empty for a reason the platform
                // imposes, not for one the user can fix by trying again.
                return (
                    "No log entries were captured",
                    _viewModel.CaptureScopeRemedy ?? "Start Live again and generate app activity.");
            // "Loaded" and "queried" are not the same moment. Opening a million-entry session
            // showed the empty-view card with three remedies for two seconds while the status
            // bar already read "Ready", so the app said the session was loaded and empty when
            // it was loaded and still being read (finding 19).
            case SessionActivity.Ready or SessionActivity.Stopped when _viewModel.Statistics is null:
                return ("Preparing this view…", "Reading the first entries of the session.");
            case SessionActivity.Ready or SessionActivity.Stopped:
                return ("Nothing to plot in this view", DescribeEmptyPlotRemedy());
            default:
                return (
                    "Open a logcat file or start a live capture.",
                    "The severity × time signal will appear here.");
        }
    }

    /// <summary>
    /// Only the remedies that apply. "Fit the timeline, widen the severity filter, or clear
    /// the query" was printed under a chip bar reading "No filters · showing everything in
    /// view" and over an already-fitted timeline: three instructions, none of which the
    /// reader could carry out (finding 19).
    /// </summary>
    private string DescribeEmptyPlotRemedy()
    {
        var remedies = new List<string>(3);
        var session = _viewModel.Snapshot?.TimedRange;
        if (session is { } whole && _viewModel.Viewport is { } viewport &&
            (viewport.StartInclusive > whole.StartInclusive || viewport.EndExclusive < whole.EndExclusive))
        {
            remedies.Add("fit the timeline");
        }

        var filter = _viewModel.Filter;
        if (filter.IncludedLevels.Count > 0)
        {
            remedies.Add("widen the severity filter");
        }

        if (filter.Search is not null)
        {
            remedies.Add("clear the query");
        }

        if (remedies.Count == 0 && filter.Fingerprint() != FilterSpec.All.Fingerprint())
        {
            remedies.Add("clear the active filters");
        }

        if (remedies.Count == 0)
        {
            return "This time range holds no entries.";
        }

        remedies[0] = char.ToUpperInvariant(remedies[0][0]) + remedies[0][1..];
        return remedies.Count == 1
            ? $"{remedies[0]}."
            : $"{string.Join(", ", remedies[..^1])} or {remedies[^1]}.";
    }

    private static string FormatSpan(long microseconds) =>
        microseconds switch
        {
            < 1_000 => $"{microseconds:N0} µs",
            < 1_000_000 => $"{microseconds / 1_000d:0.###} ms",
            < 60_000_000 => $"{microseconds / 1_000_000d:0.###} s",
            < 3_600_000_000 => $"{microseconds / 60_000_000d:0.##} min",
            _ => $"{microseconds / 3_600_000_000d:0.##} h",
        };

    private static string FormatResolution(double microsecondsPerPixel) =>
        microsecondsPerPixel switch
        {
            < 0.01 => $"{microsecondsPerPixel * 1_000:0.##} ns/px",
            < 1 => $"{microsecondsPerPixel:0.###} µs/px",
            < 1_000 => $"{microsecondsPerPixel:0.#} µs/px",
            < 1_000_000 => $"{microsecondsPerPixel / 1_000:0.##} ms/px",
            _ => $"{microsecondsPerPixel / 1_000_000:0.##} s/px",
        };

    private void UpdateStatistics()
    {
        var stats = _viewModel.Statistics;
        var inView = _viewModel.MatchesInView;
        var scoped = _viewModel.DetailRange is not null;
        UpdateSummaryText();
        _clearScope.IsVisible = scoped;
        _fitMatches.IsVisible = stats is { TotalMatching: > 0 } &&
                                inView is 0 or null &&
                                stats.FirstInstant is not null &&
                                stats.LastInstant is not null;
        UpdateEmptyResults();
        UpdateEntryActionRows();

        // The summary above tracks the viewport, but chips and facets depend only on the
        // filter and the statistics behind them. Panning must not rebuild a hundred facet
        // rows per frame — and must not move the row under the pointer.
        var filter = _viewModel.Filter;
        if (ReferenceEquals(_renderedChipFilter, filter) && ReferenceEquals(_renderedFacets, stats))
        {
            return;
        }

        _renderedChipFilter = filter;
        _renderedFacets = stats;
        RebuildChips();
        RebuildFacets(stats);
    }

    /// <summary>
    /// Explains an empty entries list, and offers the action that would refill it.
    /// </summary>
    /// <remarks>
    /// U-13 requires every empty state to explain itself in product language and offer the
    /// next action, and fails explicitly if one "renders as a blank pane" — which is what the
    /// largest region of the workspace did whenever a filter matched nothing (finding F-06).
    /// The list is left in the tree and named with the count, so the screen reader hears the
    /// same thing the screen says.
    /// </remarks>
    private void UpdateEmptyResults()
    {
        if (_emptyResultsCard is not { } card ||
            _emptyResultsTitle is not { } title ||
            _emptyResultsDetail is not { } detail ||
            _emptyResultsWiden is not { } widen ||
            _emptyResultsClear is not { } clear)
        {
            return;
        }

        var stats = _viewModel.Statistics;
        var filtered = _viewModel.Filter.Fingerprint() != FilterSpec.All.Fingerprint();
        var empty = _viewModel.Entries.Count == 0 &&
                    _viewModel.Snapshot is not null &&
                    !_viewModel.IsSessionWorkInFlight;

        card.IsVisible = empty;
        _entries.IsVisible = !empty;
        if (!empty)
        {
            AutomationProperties.SetName(_entries, "Filtered log entries");
            return;
        }

        var session = _viewModel.Snapshot?.Descriptor.Counters.TimedEntries ?? 0;
        var matching = stats?.TotalMatching ?? 0;
        var elsewhere = matching > 0;
        var dark = ActualThemeVariant != ThemeVariant.Light;
        card.Background = new SolidColorBrush(WorkspacePalette.SurfaceRaised(dark));
        card.BorderBrush = new SolidColorBrush(WorkspacePalette.BorderLine(dark));
        title.Foreground = new SolidColorBrush(WorkspacePalette.TextPrimary(dark));
        detail.Foreground = new SolidColorBrush(WorkspacePalette.TextPrimary(dark));

        if (!filtered && session == 0)
        {
            title.Text = "This session has no entries yet";
            detail.Text = "Nothing has been parsed into it. A capture that has just started shows its first rows here.";
            widen.IsVisible = false;
            clear.IsVisible = false;
        }
        else if (elsewhere)
        {
            title.Text = "No match in the time range on screen";
            detail.Text =
                $"{Counted.Entries(matching)} in this session match these filters, outside the range the plot is showing. " +
                $"The session holds {Counted.Entries(session)}.";
            widen.IsVisible = true;
            clear.IsVisible = true;
        }
        else
        {
            title.Text = "No entry matches these filters";
            detail.Text = $"The session holds {Counted.Entries(session)}. Clearing the filters brings all of them back.";
            widen.IsVisible = false;
            clear.IsVisible = filtered;
        }

        // The empty list keeps its place in the accessibility tree so the announcement and
        // the screen agree; naming it with the reason is what it was missing.
        AutomationProperties.SetName(_entries, $"Filtered log entries: {title.Text}. {detail.Text}");
        AutomationProperties.SetName(card, $"{title.Text}. {detail.Text}");
    }

    /// <summary>
    /// Shows where in the matches the view is, and offers the two steps either way.
    /// </summary>
    /// <remarks>
    /// The position is derived from the viewport rather than stored, so it cannot drift out of
    /// agreement with what is drawn: after a step the viewport is centred on that marker, so
    /// the number is exact; after a pan it names the match nearest the middle of the view, or
    /// says there is none in view at all. Disabled with a reason when no search is active,
    /// which is what U-10 asks of a control that is sometimes not applicable.
    /// </remarks>
    private void UpdateMarkerNavigation()
    {
        if (_markerNav is not { } nav ||
            _markerPosition is not { } position ||
            _markerPrevious is not { } previous ||
            _markerNext is not { } next)
        {
            return;
        }

        var markers = _viewModel.SearchResult?.Markers;
        var count = markers?.Count ?? 0;
        nav.IsVisible = count > 0;

        // The stepper already ends in "/ 7,181", so a "7,181 search matches" label beside it
        // is the same number twice on a row that is one clipped line wide — and it was pushing
        // the session's own status out to "Ready · 49,…". While a search is still running the
        // percentage is the useful half and the total is not settled, so the label comes back.
        _searchStatus.IsVisible = !nav.IsVisible || _viewModel.SearchInProgress;
        previous.IsEnabled = count > 0;
        next.IsEnabled = count > 0;
        if (count == 0)
        {
            const string reason = "No search is active, so there are no matches to step through.";
            AutomationProperties.SetHelpText(previous, reason);
            AutomationProperties.SetHelpText(next, reason);
            return;
        }

        AutomationProperties.SetHelpText(previous, null);
        AutomationProperties.SetHelpText(next, null);
        position.Foreground = new SolidColorBrush(
            WorkspacePalette.TextPrimary(ActualThemeVariant != ThemeVariant.Light));

        var index = MarkerIndexInView(markers!);
        position.Text = index is { } visible
            ? $"{visible + 1:N0} / {count:N0}"
            : $"– / {count:N0}";
        var spoken = index is { } inView
            ? $"Match {inView + 1:N0} of {count:N0}"
            : $"{Counted.Of(count, "match", "matches")}, none in the range on screen";
        AutomationProperties.SetName(position, spoken);
        ToolTip.SetTip(position, spoken);

        // Announced politely, so stepping through matches reports where it arrived without
        // interrupting whatever the reader is reading (U-17).
        AutomationProperties.SetLiveSetting(position, AutomationLiveSetting.Polite);
    }

    /// <summary>The match nearest the middle of the viewport, when one is inside it.</summary>
    private int? MarkerIndexInView(IReadOnlyList<InstantUs> markers)
    {
        if (_viewModel.Viewport is not { } viewport)
        {
            return null;
        }

        var centre = viewport.StartInclusive.Value + viewport.DurationUs / 2;
        int? best = null;
        var bestDistance = long.MaxValue;
        for (var i = 0; i < markers.Count; i++)
        {
            var value = markers[i].Value;
            if (value < viewport.StartInclusive.Value || value >= viewport.EndExclusive.Value)
            {
                continue;
            }

            var distance = Math.Abs(value - centre);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private void UpdateSummaryText()
    {
        var stats = _viewModel.Statistics;
        if (stats is null)
        {
            _summary.Text = string.Empty;
            AutomationProperties.SetName(_summary, string.Empty);
            ToolTip.SetTip(_summary, null);
            return;
        }

        // Three different numbers used to be labelled with two words: "5 view · 5 session"
        // above a status bar reading "Ready · 10 entries", where "session" meant "matching,
        // across the session" and "entries" meant the session's own size. Each number now
        // carries what it counts, and "bar" — which nobody reads as "entries in the selected
        // timeline bar" — says so (finding 23).
        var inView = _viewModel.MatchesInView ?? 0;
        var scoped = _viewModel.DetailRange is not null;
        var scope = scoped ? "in this bar" : "in view";
        var sessionTotal = _viewModel.Snapshot?.Descriptor.Counters.TimedEntries;
        var sessionPart = sessionTotal is { } total ? $"{total:N0} in session" : null;
        var full = string.Join(
            "  ·  ",
            new[]
            {
                $"{inView:N0} {scope}",
                $"{stats.TotalMatching:N0} match the filter",
                sessionPart,
                $"{FormatInstant(stats.FirstInstant)} — {FormatInstant(stats.LastInstant)}",
            }.Where(static part => part is { Length: > 0 }));
        var mobileSummary = string.Join(
            " · ",
            new[] { $"{inView:N0} {scope}", $"{stats.TotalMatching:N0} match", sessionPart }
                .Where(static part => part is { Length: > 0 }));
        _summary.Text = _mobile
            ? _summaryInTabStrip
                ? NarrowestSummaryThatFits(inView, sessionTotal, _summaryRoom, MeasureSummaryWidth)
                : mobileSummary
            : full;
        AutomationProperties.SetName(_summary, full);
        ToolTip.SetTip(_summary, full);
    }

    /// <summary>
    /// The most the count line can say in the room the tab strip leaves it, dropping whole
    /// facts rather than letting the last one be cut off mid-digit.
    /// </summary>
    /// <remarks>
    /// The compact form is the one that shares a row with the three analysis tabs, and it
    /// used to be a single string leaning on <see cref="TextTrimming.CharacterEllipsis"/>.
    /// In a 393 dp landscape workspace the row leaves it 80 dp and it rendered
    /// <c>50,156 view · 5…</c> — and a half-drawn number is worse than a half-drawn word,
    /// because <c>5…</c> is itself a plausible count (A-04). §25.3 settled the rule when the
    /// landscape divider exposed the same shape in the plot header; this is
    /// <c>TimelineControl.NarrowestHeaderThatFits</c> applied to the other line that has to
    /// live in whatever width is left. The trimming stays as the last resort, for a room too
    /// small even for the bare number.
    /// </remarks>
    internal static string NarrowestSummaryThatFits(
        long inView,
        long? sessionTotal,
        double room,
        Func<string, double> measure) =>
        NarrowestThatFits(
            sessionTotal is { } total
                ? [$"{inView:N0} view · {total:N0}", $"{inView:N0} view", $"{inView:N0}"]
                : [$"{inView:N0} view", $"{inView:N0}"],
            room,
            measure);

    /// <summary>
    /// The first candidate that draws inside <paramref name="room"/>, or the shortest one when
    /// none of them does.
    /// </summary>
    /// <remarks>
    /// The product's answer to a label that will not fit is to say less, not to say half of
    /// something — established when the landscape divider let a reader make the plot narrower
    /// than any viewport had been (§25.3) and applied since to every line that has to live in
    /// whatever width is left. Candidates are longest first; the caller writes them, because
    /// what is worth giving up is a question about the sentence rather than about the width.
    /// </remarks>
    internal static string NarrowestThatFits(
        IReadOnlyList<string> candidates,
        double room,
        Func<string, double> measure)
    {
        foreach (var candidate in candidates)
        {
            if (measure(candidate) <= room)
            {
                return candidate;
            }
        }

        return candidates[^1];
    }

    /// <summary>How wide the count line would draw, in the face and size it will draw in.</summary>
    private double MeasureSummaryWidth(string text) =>
        new FormattedText(
            text,
            DisplayCulture.Current,
            FlowDirection.LeftToRight,
            new Typeface(_summary.FontFamily, _summary.FontStyle, _summary.FontWeight),
            _summary.FontSize,
            Brushes.White).WidthIncludingTrailingWhitespace;

    /// <summary>Moves the viewport onto the span the current filter actually matches.</summary>
    private void FitToMatches()
    {
        if (_viewModel.Statistics is not { FirstInstant: { } first, LastInstant: { } last })
        {
            return;
        }

        var padding = Math.Max(1_000, (last.Value - first.Value) / 40);
        _ = _viewModel.SetViewportAsync(new TimeRange(
            new InstantUs(first.Value - padding),
            new InstantUs(last.Value + padding)));
    }


}
