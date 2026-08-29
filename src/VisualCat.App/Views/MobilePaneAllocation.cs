namespace VisualCat.App.Views;

/// <summary>The mutually exclusive ways rows 2-5 are used by the phone workspace.</summary>
internal enum MobilePaneComposition
{
    Unavailable,
    Filters,
    Plot,
    SplitStacked,
    SplitWide,
    Details,
}

internal enum MobilePaneTrackUnit
{
    Pixel,
    Star,
}

/// <summary>A framework-independent grid track returned by <see cref="MobilePaneAllocator"/>.</summary>
internal readonly record struct MobilePaneTrack(double Value, MobilePaneTrackUnit Unit)
{
    internal static MobilePaneTrack Pixels(double value) =>
        new(double.IsFinite(value) ? Math.Max(0, value) : 0, MobilePaneTrackUnit.Pixel);

    internal static MobilePaneTrack Stars(double value = 1) =>
        new(double.IsFinite(value) && value > 0 ? value : 1, MobilePaneTrackUnit.Star);
}

/// <summary>All inputs that can affect the allocation of mobile workspace rows 2-5.</summary>
internal readonly record struct MobilePaneAllocationRequest(
    MobilePaneComposition Composition,
    double AvailableBandHeight,
    double TimelineWeight,
    double AnalysisWeight,
    double MinimapHeight,
    double SplitterLaneHeight,
    double AnalysisChromeHeight,
    double EntryRowHeight,
    int PreferredEntryRows,
    int ManualEntryRows,
    double? TimelineShare);

/// <summary>The single resolved description of mobile workspace rows 2-5.</summary>
internal readonly record struct MobilePaneAllocation(
    MobilePaneTrack Timeline,
    MobilePaneTrack Minimap,
    MobilePaneTrack Splitter,
    MobilePaneTrack Analysis,
    double AnalysisMinimumHeight,
    double AnalysisMaximumHeight,
    bool SplitterVisible,
    bool SplitterEnabled,
    double MinimumTimelineShare,
    double MaximumTimelineShare,
    double ResolvedTimelineShare,
    bool StoredShareWasClamped)
{
    internal static MobilePaneAllocation Fixed(
        MobilePaneTrack timeline,
        MobilePaneTrack minimap,
        MobilePaneTrack splitter,
        MobilePaneTrack analysis,
        double analysisMinimum = 0,
        double analysisMaximum = double.PositiveInfinity) =>
        new(
            timeline,
            minimap,
            splitter,
            analysis,
            analysisMinimum,
            analysisMaximum,
            SplitterVisible: false,
            SplitterEnabled: false,
            MinimumTimelineShare: 0,
            MaximumTimelineShare: 1,
            ResolvedTimelineShare: 0,
            StoredShareWasClamped: false);
}

/// <summary>All inputs that can affect the side-by-side allocation of the two mobile columns.</summary>
internal readonly record struct MobilePaneWidthRequest(
    bool SideBySide,
    double AvailableWidth,
    double PlotWeight,
    double AnalysisWeight,
    double SplitterLaneWidth,
    double PlotMinimumWidth,
    double AnalysisMinimumWidth,
    double? PlotShare);

/// <summary>The single resolved description of the side-by-side columns.</summary>
internal readonly record struct MobilePaneWidthAllocation(
    MobilePaneTrack Plot,
    MobilePaneTrack Splitter,
    MobilePaneTrack Analysis,
    bool SplitterVisible,
    bool SplitterEnabled,
    double MinimumPlotShare,
    double MaximumPlotShare,
    double ResolvedPlotShare,
    bool StoredShareWasClamped)
{
    internal static MobilePaneWidthAllocation Automatic(double plotWeight, double analysisWeight) =>
        new(
            MobilePaneTrack.Stars(plotWeight),
            MobilePaneTrack.Pixels(0),
            MobilePaneTrack.Stars(analysisWeight),
            SplitterVisible: false,
            SplitterEnabled: false,
            MinimumPlotShare: 0,
            MaximumPlotShare: 1,
            ResolvedPlotShare: plotWeight / (plotWeight + analysisWeight),
            StoredShareWasClamped: false);
}

/// <summary>
/// Resolves every mobile pane-size decision without depending on Avalonia's visual tree.
/// </summary>
internal static class MobilePaneAllocator
{
    /// <summary>The plot remains readable at this height; the minimap is additional.</summary>
    internal const double MinimumReadableTimelineHeight = 132;

    /// <summary>
    /// The narrowest plot column the divider will resolve. The control reserves 76 dp of
    /// severity gutter and 12 dp of right margin before it draws anything, so this leaves
    /// about 132 dp of drawable span and stays well clear of the width cliff below.
    /// </summary>
    internal const double MinimumReadableTimelineWidth = 220;

    /// <summary>
    /// Below this width <c>TimelineControl.Geometry()</c> returns null and the control draws
    /// its empty-state sentence instead of a heat map. The readability minimum is the drag
    /// limit; this is the invariant no allocation may cross.
    /// </summary>
    internal const double TimelineRenderingWidthFloor = 120;

    /// <summary>
    /// The narrowest details column the divider will resolve. F-32 recorded what a squeezed
    /// analysis column does: at 131 dp its own controls clipped to a few dp. This is the
    /// share the pane already keeps at the narrowest viewport that composes side by side.
    /// </summary>
    internal const double MinimumUsableAnalysisWidth = 300;

    /// <summary>
    /// Below this the timeline's geometry has no drawable lane band and renders an empty-state
    /// sentence instead of the heat map. This is a fallback invariant, not the drag limit.
    /// </summary>
    internal const double TimelineRenderingFloor = 70;

    /// <summary>
    /// Travel below this is not offered at all. A divider that answers a drag with a couple of
    /// pixels reads as broken, and Plot and Details already give either pane the whole band.
    /// </summary>
    internal const double MinimumUsefulTravel = 12;

    /// <summary>
    /// Whether a short viewport can give both panes their own minimum width, and so compose
    /// them side by side rather than stacking them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to be asked as <c>MobileWorkspaceLayout.SharesARow</c>, which answers a
    /// different question — whether two <em>command groups</em> fit on one row — and needs
    /// 600 dp where two panes need 532. The two part company as soon as the reader raises
    /// their text size, because both scale: on a Pixel 5 in landscape, 850.9 × 392.7 dp, a
    /// 1.55× scale put the command-row threshold at 930 dp, so the panes stacked into a
    /// 143 dp band. Stacked, that band cannot seat both: the plot keeps its readable floor
    /// and the analysis pane resolves to <em>nothing</em>, so **Split** drew a plot and no
    /// details at all while its own button stayed selected (A-06). Widening the same
    /// viewport to 945 dp brought the details straight back, which is the wrong lever
    /// entirely — the viewport was already wide, it was the question that was wrong.
    /// </para>
    /// <para>
    /// Deliberately <em>not</em> scaled by the reader's text size, which is where the first
    /// attempt at this fix went wrong on the same device. The command row scales because not
    /// fitting there is a hard failure — five controls drawn on top of each other, with one
    /// of them unreachable (F-34). Two columns that are narrower than their comfort width are
    /// not a hard failure: the allocator keeps them above
    /// <see cref="TimelineRenderingWidthFloor"/>, and the workspace this viewport actually
    /// has is 777 dp wide and 143 dp tall once stacked. Scaling turned a comfort target into
    /// a cliff, and what fell off it was the whole analysis pane. A narrow column beats no
    /// column.
    /// </para>
    /// <para>
    /// This is also why the rule is width alone: a composition chosen from a band height that
    /// is itself decided by the composition would have two answers and could alternate
    /// between them on consecutive layout passes.
    /// </para>
    /// </remarks>
    internal static bool FitsSideBySide(double width, double laneWidth) =>
        double.IsFinite(width) &&
        double.IsFinite(laneWidth) &&
        width >= MinimumReadableTimelineWidth + MinimumUsableAnalysisWidth + Math.Max(0, laneWidth);

    internal static MobilePaneAllocation Resolve(MobilePaneAllocationRequest request)
    {
        var band = FiniteNonNegative(request.AvailableBandHeight);
        var minimap = FiniteNonNegative(request.MinimapHeight);
        var timelineWeight = FinitePositive(request.TimelineWeight, 2);
        var analysisWeight = FinitePositive(request.AnalysisWeight, 3);
        var chrome = FiniteNonNegative(request.AnalysisChromeHeight);
        var rowHeight = FinitePositive(request.EntryRowHeight, 64);
        var preferredRows = Math.Max(0, request.PreferredEntryRows);
        var manualRows = Math.Max(1, request.ManualEntryRows);
        var preferredAnalysis = chrome + (preferredRows * rowHeight);

        switch (request.Composition)
        {
            case MobilePaneComposition.Filters:
                return MobilePaneAllocation.Fixed(
                    MobilePaneTrack.Stars(),
                    MobilePaneTrack.Pixels(0),
                    MobilePaneTrack.Pixels(0),
                    MobilePaneTrack.Pixels(0));

            case MobilePaneComposition.Plot:
                return MobilePaneAllocation.Fixed(
                    MobilePaneTrack.Stars(),
                    MobilePaneTrack.Pixels(minimap),
                    MobilePaneTrack.Pixels(0),
                    MobilePaneTrack.Pixels(0));

            case MobilePaneComposition.Details:
                return MobilePaneAllocation.Fixed(
                    MobilePaneTrack.Pixels(0),
                    MobilePaneTrack.Pixels(0),
                    MobilePaneTrack.Pixels(0),
                    MobilePaneTrack.Stars(),
                    analysisMinimum: Math.Min(preferredAnalysis, band),
                    analysisMaximum: band > 0 ? band : double.PositiveInfinity);

            case MobilePaneComposition.SplitWide:
                // ConfigureWideMobileComposition moves the minimap frame into row 5. The
                // analysis pane is a column spanning rows 2-5, so it needs no row of its own.
                return MobilePaneAllocation.Fixed(
                    MobilePaneTrack.Stars(),
                    MobilePaneTrack.Pixels(0),
                    MobilePaneTrack.Pixels(0),
                    MobilePaneTrack.Pixels(minimap));

            case MobilePaneComposition.SplitStacked:
                return ResolveStacked(
                    request,
                    band,
                    minimap,
                    timelineWeight,
                    analysisWeight,
                    chrome,
                    rowHeight,
                    preferredAnalysis,
                    manualRows);

            default:
                return MobilePaneAllocation.Fixed(
                    MobilePaneTrack.Pixels(0),
                    MobilePaneTrack.Pixels(0),
                    MobilePaneTrack.Pixels(0),
                    MobilePaneTrack.Pixels(0));
        }
    }

    private static MobilePaneAllocation ResolveStacked(
        MobilePaneAllocationRequest request,
        double band,
        double minimap,
        double timelineWeight,
        double analysisWeight,
        double chrome,
        double rowHeight,
        double preferredAnalysis,
        int manualRows)
    {
        var lane = FiniteNonNegative(request.SplitterLaneHeight);
        var analysisHardMinimum = chrome + (manualRows * rowHeight);
        var plotHardMinimum = MinimumReadableTimelineHeight + minimap;
        var resizableWithLane = Math.Max(0, band - lane);
        var minimumPlotShare = resizableWithLane > 0 ? plotHardMinimum / resizableWithLane : 0;
        var maximumPlotShare = resizableWithLane > 0
            ? (resizableWithLane - analysisHardMinimum) / resizableWithLane
            : 0;
        var usefulRange = resizableWithLane >= plotHardMinimum + analysisHardMinimum &&
                          maximumPlotShare >= minimumPlotShare &&
                          (maximumPlotShare - minimumPlotShare) * resizableWithLane >= MinimumUsefulTravel;

        if (!usefulRange)
        {
            // There is no honest draggable range. Keep the established weighted fallback,
            // prefer as many entry rows as fit, and protect at least the timeline rendering
            // cliff when the viewport has enough room to do so.
            var fallbackPlotFloor = Math.Min(
                MinimumReadableTimelineHeight,
                Math.Max(0, band - minimap));
            if (band >= minimap + TimelineRenderingFloor)
            {
                fallbackPlotFloor = Math.Max(fallbackPlotFloor, TimelineRenderingFloor);
            }

            var analysisCeiling = Math.Max(0, band - minimap - fallbackPlotFloor);
            var analysisMinimum = Math.Min(preferredAnalysis, analysisCeiling);
            var weightedShare = WeightedPlotShare(band, minimap, timelineWeight, analysisWeight);
            return new MobilePaneAllocation(
                MobilePaneTrack.Stars(timelineWeight),
                MobilePaneTrack.Pixels(minimap),
                MobilePaneTrack.Pixels(0),
                MobilePaneTrack.Stars(analysisWeight),
                analysisMinimum,
                analysisCeiling,
                SplitterVisible: false,
                SplitterEnabled: false,
                MinimumTimelineShare: 0,
                MaximumTimelineShare: 1,
                ResolvedTimelineShare: weightedShare,
                StoredShareWasClamped: false);
        }

        minimumPlotShare = Math.Clamp(minimumPlotShare, 0, 1);
        maximumPlotShare = Math.Clamp(maximumPlotShare, minimumPlotShare, 1);
        var analysisMaximum = Math.Max(0, resizableWithLane - plotHardMinimum);

        if (ValidStoredShare(request.TimelineShare) is { } requestedShare)
        {
            var resolvedShare = Math.Clamp(requestedShare, minimumPlotShare, maximumPlotShare);
            var analysisHeight = resizableWithLane * (1 - resolvedShare);
            return new MobilePaneAllocation(
                MobilePaneTrack.Stars(),
                MobilePaneTrack.Pixels(minimap),
                MobilePaneTrack.Pixels(lane),
                MobilePaneTrack.Pixels(analysisHeight),
                AnalysisMinimumHeight: 0,
                AnalysisMaximumHeight: analysisMaximum,
                SplitterVisible: true,
                SplitterEnabled: true,
                MinimumTimelineShare: minimumPlotShare,
                MaximumTimelineShare: maximumPlotShare,
                ResolvedTimelineShare: resolvedShare,
                StoredShareWasClamped: Math.Abs(resolvedShare - requestedShare) > 0.0001);
        }

        var automaticMinimum = Math.Clamp(preferredAnalysis, analysisHardMinimum, analysisMaximum);
        return new MobilePaneAllocation(
            MobilePaneTrack.Stars(timelineWeight),
            MobilePaneTrack.Pixels(minimap),
            MobilePaneTrack.Pixels(lane),
            MobilePaneTrack.Stars(analysisWeight),
            automaticMinimum,
            analysisMaximum,
            SplitterVisible: true,
            SplitterEnabled: true,
            MinimumTimelineShare: minimumPlotShare,
            MaximumTimelineShare: maximumPlotShare,
            ResolvedTimelineShare: Math.Clamp(
                WeightedPlotShare(resizableWithLane, minimap, timelineWeight, analysisWeight),
                minimumPlotShare,
                maximumPlotShare),
            StoredShareWasClamped: false);
    }

    /// <summary>
    /// Resolves the side-by-side boundary, where the reader is moving a column edge rather
    /// than a row edge.
    /// </summary>
    /// <remarks>
    /// The two axes share the divider, the state type and the storage rule, and nothing else.
    /// A height share cannot drive a width split: the plot's vertical minimum is about a
    /// readable lane band and the analysis pane's is about entry rows, while horizontally the
    /// plot needs its label gutter plus a drawable span and the pane needs a message column.
    /// The stored values are therefore separate, and each is only ever applied on its own
    /// axis.
    /// </remarks>
    internal static MobilePaneWidthAllocation ResolveWidth(MobilePaneWidthRequest request)
    {
        var band = FiniteNonNegative(request.AvailableWidth);
        var plotWeight = FinitePositive(request.PlotWeight, 21);
        var analysisWeight = FinitePositive(request.AnalysisWeight, 29);
        var lane = FiniteNonNegative(request.SplitterLaneWidth);
        var plotMinimum = FiniteNonNegative(request.PlotMinimumWidth);
        var analysisMinimum = FiniteNonNegative(request.AnalysisMinimumWidth);

        if (!request.SideBySide)
        {
            return MobilePaneWidthAllocation.Automatic(plotWeight, analysisWeight);
        }

        var resizable = Math.Max(0, band - lane);
        var minimumShare = resizable > 0 ? plotMinimum / resizable : 0;
        var maximumShare = resizable > 0 ? (resizable - analysisMinimum) / resizable : 0;
        var usefulRange = resizable >= plotMinimum + analysisMinimum &&
                          maximumShare >= minimumShare &&
                          (maximumShare - minimumShare) * resizable >= MinimumUsefulTravel;
        if (!usefulRange)
        {
            return MobilePaneWidthAllocation.Automatic(plotWeight, analysisWeight);
        }

        minimumShare = Math.Clamp(minimumShare, 0, 1);
        maximumShare = Math.Clamp(maximumShare, minimumShare, 1);
        var weightedShare = Math.Clamp(
            plotWeight / (plotWeight + analysisWeight),
            minimumShare,
            maximumShare);

        if (ValidStoredShare(request.PlotShare) is not { } requested)
        {
            return new MobilePaneWidthAllocation(
                MobilePaneTrack.Stars(plotWeight),
                MobilePaneTrack.Pixels(lane),
                MobilePaneTrack.Stars(analysisWeight),
                SplitterVisible: true,
                SplitterEnabled: true,
                minimumShare,
                maximumShare,
                weightedShare,
                StoredShareWasClamped: false);
        }

        var resolved = Math.Clamp(requested, minimumShare, maximumShare);
        return new MobilePaneWidthAllocation(
            MobilePaneTrack.Pixels(resizable * resolved),
            MobilePaneTrack.Pixels(lane),
            MobilePaneTrack.Stars(),
            SplitterVisible: true,
            SplitterEnabled: true,
            minimumShare,
            maximumShare,
            resolved,
            StoredShareWasClamped: Math.Abs(resolved - requested) > 0.0001);
    }

    internal static double? ValidStoredShare(double? share) =>
        share is { } value && double.IsFinite(value) && value is >= 0.05 and <= 0.95
            ? value
            : null;

    private static double WeightedPlotShare(
        double resizableHeight,
        double minimap,
        double timelineWeight,
        double analysisWeight)
    {
        if (resizableHeight <= 0)
        {
            return 0;
        }

        var starBand = Math.Max(0, resizableHeight - minimap);
        var timeline = starBand * timelineWeight / (timelineWeight + analysisWeight);
        return Math.Clamp((minimap + timeline) / resizableHeight, 0, 1);
    }

    private static double FiniteNonNegative(double value) =>
        double.IsFinite(value) ? Math.Max(0, value) : 0;

    private static double FinitePositive(double value, double fallback) =>
        double.IsFinite(value) && value > 0 ? value : fallback;
}

/// <summary>
/// Holds the reader's normalized aggregate plot-pane share independently from any temporary
/// viewport clamp. The share includes the minimap and excludes the divider lane.
/// </summary>
internal sealed class MobilePaneSplitState
{
    internal double? TimelineShare { get; private set; }

    internal bool HasUserOverride => TimelineShare.HasValue;

    internal bool Restore(double? persisted)
    {
        var validated = MobilePaneAllocator.ValidStoredShare(persisted);
        var changed = !Nullable.Equals(TimelineShare, validated);
        TimelineShare = validated;
        return changed;
    }

    internal bool Set(double share)
    {
        var validated = MobilePaneAllocator.ValidStoredShare(share);
        if (validated is null || TimelineShare is { } current && Math.Abs(current - validated.Value) < 0.0001)
        {
            return false;
        }

        TimelineShare = validated;
        return true;
    }

    internal bool UpdateFromArrangedHeights(double plotPaneHeight, double analysisPaneHeight)
    {
        if (!double.IsFinite(plotPaneHeight) ||
            !double.IsFinite(analysisPaneHeight) ||
            plotPaneHeight < 0 ||
            analysisPaneHeight < 0 ||
            plotPaneHeight + analysisPaneHeight <= 0)
        {
            return false;
        }

        return Set(plotPaneHeight / (plotPaneHeight + analysisPaneHeight));
    }

    internal bool Reset()
    {
        if (TimelineShare is null)
        {
            return false;
        }

        TimelineShare = null;
        return true;
    }
}
