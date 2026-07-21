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
        if (_viewModel.Snapshot is { } snapshot)
        {
            _timeline.SetTimeZoneContext(snapshot.Descriptor.TimestampPolicy.TimeZoneId);
        }

        _timeline.SetResult(_viewModel.HeatMap, _viewModel.Snapshot?.TimedRange);
        _timeline.SetSearchResult(_viewModel.SearchResult);
        _minimap.SetResult(_viewModel.Overview, _viewModel.Viewport, _viewModel.Snapshot?.TimedRange);
        _zoomReadout.Text = _viewModel.Viewport is { } viewport
            ? $"{FormatSpan(viewport.DurationUs)} · {FormatResolution(viewport.DurationUs / Math.Max(1d, _viewModel.HeatMap?.Viewport.DevicePixelWidth ?? 1))}"
            : string.Empty;
        UpdateSessionInfo();
    }

    private void UpdateCaptureActions()
    {
        var sourceKind = _viewModel.Snapshot?.Descriptor.SourceKind;
        var live = sourceKind is SourceKind.Adb or SourceKind.Android or SourceKind.GrowingFile ||
                   _viewModel.Status.StartsWith("Capturing", StringComparison.Ordinal) ||
                   _viewModel.Status.StartsWith("Stopping", StringComparison.Ordinal);
        _follow.IsVisible = live;
        _stopCapture.IsVisible =
            _viewModel.Status.StartsWith("Capturing", StringComparison.Ordinal) ||
            _viewModel.Status.StartsWith("Stopping", StringComparison.Ordinal);
        _stopCapture.IsEnabled = !_viewModel.Status.StartsWith("Stopping", StringComparison.Ordinal);
        _newData.IsVisible = live && _viewModel.HasNewData;
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
