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
        _zoomReadout.Text = _viewModel.Viewport is { } viewport
            ? $"{FormatSpan(viewport.DurationUs)} · {FormatResolution(viewport.DurationUs / Math.Max(1d, _viewModel.HeatMap?.Viewport.DevicePixelWidth ?? 1))}"
            : string.Empty;
        UpdateSessionInfo();
    }

    private void UpdateTimelineLevels()
    {
        var sessionHasUnknown = _viewModel.Snapshot?.Segments.Any(
            static segment => segment.SeverityBitmaps[LogLevel.Unknown].Cardinality > 0) == true;
        _timeline.SetDisplayLevels(_viewModel.Filter.IncludedLevels, sessionHasUnknown);
    }

    private void UpdateCaptureActions()
    {
        var sourceKind = _viewModel.Snapshot?.Descriptor.SourceKind;
        var starting = _viewModel.Status.StartsWith("Waiting for capture", StringComparison.Ordinal) ||
                       _viewModel.Status.StartsWith("Connecting", StringComparison.Ordinal) ||
                       _viewModel.Status.StartsWith("Starting capture", StringComparison.Ordinal);
        var capturing = _viewModel.Status.StartsWith("Capturing", StringComparison.Ordinal);
        var stopping = _viewModel.Status.StartsWith("Stopping", StringComparison.Ordinal);
        var live = sourceKind is SourceKind.Adb or SourceKind.Android or SourceKind.GrowingFile ||
                   starting || capturing || stopping;
        _follow.IsVisible = live;
        _stopCapture.IsVisible = _viewModel.IsLiveCaptureActive || starting || capturing || stopping;
        _stopCapture.IsEnabled = !stopping;
        _stopCapture.Content = stopping ? "Stopping…" : "Stop capture";
        _newData.IsVisible = live && _viewModel.HasNewData;

        if (_viewModel.Status.StartsWith("Waiting for capture", StringComparison.Ordinal))
        {
            _timeline.SetEmptyState("Preparing live capture…", "Waiting for an available capture slot.");
        }
        else if (_viewModel.Status.StartsWith("Connecting", StringComparison.Ordinal))
        {
            _timeline.SetEmptyState("Connecting to the device…", "Checking the device and logcat format.");
        }
        else if (_viewModel.Status.StartsWith("Starting capture", StringComparison.Ordinal))
        {
            _timeline.SetEmptyState("Starting live capture…", "Waiting for the first log entry.");
        }
        else if (capturing)
        {
            _timeline.SetEmptyState("Live capture is running", "Waiting for the first visible log entry.");
        }
        else if (live && _viewModel.Snapshot?.Descriptor.Counters.ParsedEntries == 0)
        {
            // An own-app capture of an idle app is empty for a reason the platform imposes,
            // not for one the user can fix by trying again.
            _timeline.SetEmptyState(
                "No log entries were captured",
                _viewModel.CaptureScopeRemedy ?? "Start Live again and generate app activity.");
        }
        else
        {
            _timeline.SetEmptyState(
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

        var inView = _viewModel.MatchesInView ?? 0;
        var scoped = _viewModel.DetailRange is not null;
        var full = $"{inView:N0} {(scoped ? "in selected cell" : "in view")}  ·  " +
                   $"{stats.TotalMatching:N0} in session  ·  " +
                   $"{FormatInstant(stats.FirstInstant)} — {FormatInstant(stats.LastInstant)}";
        var mobileSummary = scoped && _viewModel.DetailRange is { } range
            ? $"{inView:N0} bar · {FormatInstant(range.StartInclusive)} · {stats.TotalMatching:N0} session"
            : $"{inView:N0} view · {stats.TotalMatching:N0} session";
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
