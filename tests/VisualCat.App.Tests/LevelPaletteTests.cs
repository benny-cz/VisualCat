using VisualCat.App.Timeline;
using VisualCat.Domain.Entries;

namespace VisualCat.App.Tests;

public sealed class LevelPaletteTests
{
    [Fact]
    public void EveryStorageLevelHasADistinctDisplayColor()
    {
        // R5: one canonical severity enum and one display mapping. Every level the
        // store can hold must resolve to a color, and no two levels may collide.
        var seen = new HashSet<uint>();
        foreach (var level in LogLevels.StorageOrder)
        {
            var color = LevelPalette.ColorOf(level);
            Assert.True(seen.Add(color.ToUInt32()), $"{level} shares a color with another level.");
        }
    }

    [Fact]
    public void FillBrushesAreCachedPerLevelAndAlpha()
    {
        // The render loop draws thousands of cells per frame; Fill must hand back the
        // same immutable instance for a repeated (level, alpha) pair (R11, §19.3).
        foreach (var level in LogLevels.StorageOrder)
        {
            Assert.Same(LevelPalette.Fill(level, 128), LevelPalette.Fill(level, 128));
            Assert.NotSame(LevelPalette.Fill(level, 128), LevelPalette.Fill(level, 129));
            Assert.Equal((byte)128, LevelPalette.Fill(level, 128).Color.A);
        }
    }

    [Fact]
    public void PensAndSolidBrushesAreCachedAndMatchTheLevelColor()
    {
        foreach (var level in LogLevels.StorageOrder)
        {
            Assert.Same(LevelPalette.BrushOf(level), LevelPalette.BrushOf(level));
            Assert.Same(LevelPalette.BaselinePen(level), LevelPalette.BaselinePen(level));
            Assert.Same(LevelPalette.AccentPen(level), LevelPalette.AccentPen(level));
            Assert.Equal(LevelPalette.ColorOf(level), LevelPalette.BrushOf(level).Color);
        }
    }

    [Fact]
    public void LabelsMatchTheCanonicalLetterMapping()
    {
        Assert.Equal("F", LevelPalette.Label(LogLevel.Fatal));
        Assert.Equal("E", LevelPalette.Label(LogLevel.Error));
        Assert.Equal("W", LevelPalette.Label(LogLevel.Warn));
        Assert.Equal("I", LevelPalette.Label(LogLevel.Info));
        Assert.Equal("D", LevelPalette.Label(LogLevel.Debug));
        Assert.Equal("V", LevelPalette.Label(LogLevel.Verbose));
        Assert.Equal("?", LevelPalette.Label(LogLevel.Unknown));
    }
}
