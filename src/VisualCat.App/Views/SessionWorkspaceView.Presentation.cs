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
    private void RefreshPresentation()
    {
        _status.Text = _viewModel.Status;
        _searchStatus.Text = _viewModel.SearchStatus;
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
        _stopCapture.IsVisible = live;
        _stopCapture.IsEnabled = !stopping;
        _stopCapture.Content = stopping ? "Stopping…" : "Stop capture";
        _newData.IsVisible = live && _viewModel.HasNewData;

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
                return ("Waiting to read this log…", "Another log is being read first.");
            case SessionActivity.Importing:
                return ("Reading the log…", "The severity × time signal fills in as entries become readable.");
            case SessionActivity.Connecting:
                return ("Connecting to the device…", "Checking the device and logcat format.");
            case SessionActivity.Starting:
                return ("Starting live capture…", "Waiting for the first log entry.");
            case SessionActivity.Capturing:
                return ("Live capture is running", "Waiting for the first visible log entry.");
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
            case SessionActivity.Ready or SessionActivity.Stopped:
                return (
                    "Nothing to plot in this view",
                    "Fit the timeline, widen the severity filter, or clear the query.");
            default:
                return (
                    "Open a logcat file or start a live capture.",
                    "The severity × time signal will appear here.");
        }
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
        _summary.Text = _mobile ? mobileSummary : full;
        AutomationProperties.SetName(_summary, full);
        ToolTip.SetTip(_summary, full);
    }

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
