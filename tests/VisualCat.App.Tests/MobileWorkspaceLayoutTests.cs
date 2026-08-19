using VisualCat.App.Views;

namespace VisualCat.App.Tests;

public sealed class MobileWorkspaceLayoutTests
{
    [Theory]
    [InlineData(434, 820, (int)MobileWorkspaceMode.TallPortrait)]
    [InlineData(360, 800, (int)MobileWorkspaceMode.CompactPortrait)]
    [InlineData(800, 360, (int)MobileWorkspaceMode.CompactHeight)]
    [InlineData(412, 480, (int)MobileWorkspaceMode.CompactHeight)]
    public void SizeClassesUseBothWidthAndHeight(double width, double height, int expected)
    {
        Assert.Equal((MobileWorkspaceMode)expected, MobileWorkspaceLayout.ForSize(width, height).Mode);
    }

    [Fact]
    public void CompactHeightUsesWideCompositionForTimelineAndAnalysis()
    {
        var layout = MobileWorkspaceLayout.ForSize(900, 400);

        Assert.Equal(MobileWorkspaceDisplayMode.Split, layout.DefaultDisplayMode);

        // The minimap survives a short viewport now, in a slimmer band: it is the aid that
        // says where the viewport sits in the whole session, and a short viewport is exactly
        // where the plot is most zoomed (audit 2, D9). It costs the plot column, not the
        // analysis column beside it.
        Assert.InRange(layout.MinimapHeight, 20, 32);
        Assert.True(layout.AnalysisWeight > layout.TimelineWeight);
        Assert.InRange(layout.TimelineWeight / (layout.TimelineWeight + layout.AnalysisWeight), 0.4, 0.44);
        Assert.True(layout.FilterMaximumHeight >= 200);
        Assert.True(layout.UsesWideMobileComposition);
    }

    [Fact]
    public void PortraitGivesAnalysisMoreSpaceThanTimeline()
    {
        var layout = MobileWorkspaceLayout.ForSize(434, 900);

        Assert.Equal(MobileWorkspaceDisplayMode.Split, layout.DefaultDisplayMode);
        Assert.True(layout.AnalysisWeight > layout.TimelineWeight);
        Assert.True(layout.MinimapHeight > 0);
        Assert.False(layout.UsesWideMobileComposition);
    }

    [Fact]
    public void CompactPortraitStartsSplitAndKeepsBothNavigationContexts()
    {
        var layout = MobileWorkspaceLayout.ForSize(360, 800);

        Assert.Equal(MobileWorkspaceMode.CompactPortrait, layout.Mode);
        Assert.Equal(MobileWorkspaceDisplayMode.Split, layout.DefaultDisplayMode);
        Assert.True(layout.AnalysisWeight > layout.TimelineWeight);
        Assert.True(layout.MinimapHeight > 0);
    }

    [Fact]
    public void ExplicitDisplayModeSurvivesOrientationChanges()
    {
        var state = new MobileWorkspaceState();
        state.ApplyLayout(MobileWorkspaceLayout.ForSize(434, 900));
        state.Select(MobileWorkspaceDisplayMode.Details);

        state.ApplyLayout(MobileWorkspaceLayout.ForSize(900, 434));

        Assert.Equal(MobileWorkspaceDisplayMode.Details, state.DisplayMode);
    }

    [Fact]
    public void FirstMeasuredLayoutInitializesDisplayModeOnlyOnce()
    {
        var state = new MobileWorkspaceState();

        state.ApplyLayout(MobileWorkspaceLayout.ForSize(434, 900));
        state.ApplyLayout(MobileWorkspaceLayout.ForSize(900, 434));

        Assert.Equal(MobileWorkspaceDisplayMode.Split, state.DisplayMode);
    }

    [Theory]
    [InlineData(999, "999")]
    [InlineData(1_000, "1k")]
    [InlineData(12_345, "12.3k")]
    [InlineData(123_456, "123k")]
    [InlineData(999_999, "1M")]
    [InlineData(2_500_000, "2.5M")]
    public void TemplateCountsUseCompactReadableMetrics(long count, string expected)
    {
        // No longer translated into the machine's own separator: the product formats every
        // number it prints in one display culture now, because the interface those numbers
        // appear inside is written in one language (audit 2, E1).
        Assert.Equal(expected, SessionWorkspaceView.FormatTemplateCount(count));
    }

    [Theory]
    [InlineData(double.NaN, double.NaN)]
    [InlineData(0, 0)]
    [InlineData(double.PositiveInfinity, -1)]
    public void InvalidMeasurementsFallBackToSafePortrait(double width, double height)
    {
        Assert.Equal(MobileWorkspaceMode.TallPortrait, MobileWorkspaceLayout.ForSize(width, height).Mode);
    }
}
