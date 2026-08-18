using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using VisualCat.App.Timeline;
using VisualCat.App.Views;
using VisualCat.Domain.Time;

namespace VisualCat.App.Tests;

/// <summary>
/// Regressions from the Android UX review: the pure decisions behind the visible fixes.
/// </summary>
public sealed class ReviewFixTests
{
    /// <summary>
    /// A capture's name is recognised from its start and disambiguated by its end, and
    /// end-ellipsis threw the end away — <c>northlight-transit-20260812.txt</c> became
    /// <c>northlight-transit-20…</c>, cutting the date that distinguishes two captures of the
    /// same app (finding 28).
    /// </summary>
    [Fact]
    public void ATruncatedSessionNameKeepsBothEnds()
    {
        var shortened = TabTitle.Shorten("northlight-transit-20260812.txt", 24);

        Assert.Equal(24, shortened.Length);
        Assert.StartsWith("northlight", shortened, StringComparison.Ordinal);
        Assert.EndsWith("0812.txt", shortened, StringComparison.Ordinal);
        Assert.Contains('…', shortened);
    }

    [Fact]
    public void ANameThatFitsIsNotTruncated()
    {
        Assert.Equal("demo-small.txt", TabTitle.Shorten("demo-small.txt", 24));
    }

    /// <summary>
    /// A viewport narrow enough to contain a single aligned tick cannot get two labels out of
    /// its ticks, and one label states an instant but not a scale (finding 17).
    /// </summary>
    [Fact]
    public void AOneMicrosecondViewportLabelsItsOwnEnds()
    {
        var viewport = new TimeRange(new InstantUs(1_000_000), new InstantUs(1_000_001));

        Assert.Equal(1, TimelineAxis.CountDrawableTickLabels(viewport, 1, 76, 300, 90));
        Assert.True(TimelineAxis.UseEndpointLabels(viewport, 1, 76, 300, 90));
    }

    /// <summary>
    /// Ticks are aligned instants, so where they fall — not how many the spacing asked for —
    /// decides how many land inside the viewport. A viewport shorter than the interval holds at
    /// most one, and one label is not a scale.
    /// </summary>
    [Fact]
    public void AViewportShorterThanItsTickIntervalLabelsItsOwnEnds()
    {
        var viewport = new TimeRange(new InstantUs(900_000), new InstantUs(1_800_000));

        Assert.Equal(1, TimelineAxis.CountDrawableTickLabels(viewport, 1_000_000, 76, 400, 90));
        Assert.True(TimelineAxis.UseEndpointLabels(viewport, 1_000_000, 76, 400, 90));
    }

    /// <summary>
    /// Labels that would print over each other are dropped — the gridline still marks every
    /// tick — so the drawn count is a property of the plot's width, not of the tick count.
    /// </summary>
    [Fact]
    public void OverlappingTickLabelsAreDroppedButTheAxisStillReadsAsAScale()
    {
        var viewport = new TimeRange(new InstantUs(0), new InstantUs(1_000_000));

        var drawn = TimelineAxis.CountDrawableTickLabels(viewport, 10_000, 76, 300, 90);

        Assert.InRange(drawn, 2, 4);
        Assert.False(TimelineAxis.UseEndpointLabels(viewport, 10_000, 76, 300, 90));
    }

    /// <summary>An axis whose ticks can supply two labels is left alone.</summary>
    [Fact]
    public void AnAxisWithRoomForTickLabelsKeepsThem()
    {
        var viewport = new TimeRange(new InstantUs(0), new InstantUs(1_000_000));

        Assert.True(TimelineAxis.CountDrawableTickLabels(viewport, 200_000, 76, 900, 90) >= 2);
        Assert.False(TimelineAxis.UseEndpointLabels(viewport, 200_000, 76, 900, 90));
    }

    /// <summary>
    /// A plot too narrow for two labels side by side is not given two overlapping ones.
    /// </summary>
    [Fact]
    public void ANarrowPlotIsNotGivenTwoOverlappingEndpointLabels()
    {
        var viewport = new TimeRange(new InstantUs(1_000_000), new InstantUs(1_000_001));

        Assert.False(TimelineAxis.UseEndpointLabels(viewport, 1, 76, 140, 90));
    }

    /// <summary>
    /// Logcat prints milliseconds unless a capture asked for microseconds, so a fixed six
    /// digits printed three constant zeros on most sessions — width taken from the message on
    /// the row where the message is already being clipped (finding 25).
    /// </summary>
    [Theory]
    [InlineData(1_700_000_000_000_000L, false)]
    [InlineData(1_700_000_000_563_000L, false)]
    [InlineData(1_700_000_000_563_001L, true)]
    [InlineData(1_700_000_000_000_500L, true)]
    public void MicrosecondsAreShownOnlyWhenTheCaptureCarriesThem(long microseconds, bool expected)
    {
        Assert.Equal(expected, TimestampPrecision.NeedsMicroseconds(new InstantUs(microseconds)));
    }

    [Fact]
    public void AnUntimedEntryNeedsNoMicroseconds()
    {
        Assert.False(TimestampPrecision.NeedsMicroseconds(null));
    }

    /// <summary>
    /// A cached session's folder name carries a timestamp prefix and a GUID suffix so two
    /// captures of the same file cannot collide. Both are for the filesystem; a reader looking
    /// for yesterday's capture needs the name in the middle.
    /// </summary>
    [Fact]
    public void ACachedSessionIsNamedAfterTheCaptureItHolds()
    {
        Assert.Equal(
            "northlight-transit-20260812",
            SessionCacheName.Describe(Path.Combine(
                "cache",
                "20260818-095645-northlight-transit-20260812-8fc528c284374714badacc1421957648.vcat")));
    }

    [Fact]
    public void AnUnrecognisedSessionFolderKeepsItsName()
    {
        Assert.Equal("saved-session", SessionCacheName.Describe(Path.Combine("elsewhere", "saved-session.vcat")));
    }

    /// <summary>
    /// Selection, focus and every tab indicator used to take the device's Material You accent,
    /// which on the review device was a brick red that read as an error tint under every
    /// selected row and could not be tested from a screenshot taken elsewhere (finding 7).
    /// The list surfaces used Fluent's neutral grey in a navy workspace (finding 16).
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheProductOwnsItsAccentAndItsListSurfaces(bool dark)
    {
        var application = Avalonia.Application.Current;
        Assert.NotNull(application);
        var variant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        Assert.True(application.TryFindResource("SystemAccentColor", variant, out var accent));
        Assert.Equal(WorkspacePalette.Accent(dark), Assert.IsType<Color>(accent));

        Assert.True(application.TryFindResource("SystemChromeMediumLowColor", variant, out var listSurface));
        Assert.Equal(WorkspacePalette.SurfaceRaised(dark), Assert.IsType<Color>(listSurface));

        Assert.True(application.TryFindResource("SystemRegionColor", variant, out var region));
        Assert.Equal(WorkspacePalette.Surface(dark), Assert.IsType<Color>(region));
    }
}
