using Avalonia.Media;
using Avalonia.Media.Immutable;
using VisualCat.Domain.Entries;

namespace VisualCat.App.Timeline;

/// <summary>
/// The one severity-to-display-color mapping (R5). Every surface that colors by level —
/// timeline rows, minimap columns, entry table, legend chips — reads it from here, so a
/// palette change cannot leave two views disagreeing about what Error looks like.
///
/// It also owns the immutable brush and pen caches for the render path: a heat map frame
/// touches thousands of cells, and building a brush per cell per frame is exactly the
/// per-frame allocation R11 and §19.3 prohibit.
/// </summary>
public static class LevelPalette
{
    private static readonly Color[] Colors = BuildColors();
    private static readonly ImmutableSolidColorBrush?[][] FillCache = BuildFillCache();
    private static readonly ImmutableSolidColorBrush[] SolidBrushes = BuildSolidBrushes();
    private static readonly ImmutablePen[] BaselinePens = BuildBaselinePens();
    private static readonly ImmutablePen[] AccentPens = BuildAccentPens();

    public static Color ColorOf(LogLevel level) => Colors[IndexOf(level)];

    /// <summary>Fully opaque cached brush for the level.</summary>
    public static ImmutableSolidColorBrush BrushOf(LogLevel level) => SolidBrushes[IndexOf(level)];

    /// <summary>Cached translucent fill; one brush per (level, alpha) pair ever exists.</summary>
    public static ImmutableSolidColorBrush Fill(LogLevel level, byte alpha)
    {
        var row = FillCache[IndexOf(level)];
        return row[alpha] ??= new ImmutableSolidColorBrush(Color.FromArgb(alpha, ColorOf(level).R, ColorOf(level).G, ColorOf(level).B));
    }

    /// <summary>Cached 1-px pen used for the bright baseline under each drawn cell.</summary>
    public static ImmutablePen BaselinePen(LogLevel level) => BaselinePens[IndexOf(level)];

    /// <summary>Cached 3-px pen used for the row label accent bar.</summary>
    public static ImmutablePen AccentPen(LogLevel level) => AccentPens[IndexOf(level)];

    public static string Label(LogLevel level) =>
        level == LogLevel.Unknown ? "?" : level.ToLetter().ToString();

    private static int IndexOf(LogLevel level) => level switch
    {
        LogLevel.Verbose => 0,
        LogLevel.Debug => 1,
        LogLevel.Info => 2,
        LogLevel.Warn => 3,
        LogLevel.Error => 4,
        LogLevel.Fatal => 5,
        _ => 6,
    };

    private static Color[] BuildColors() =>
    [
        Color.Parse("#A78BFA"), // Verbose — soft violet
        Color.Parse("#34E1B6"), // Debug — mint
        Color.Parse("#43B4FF"), // Info — signal blue
        Color.Parse("#FFC857"), // Warn — amber
        Color.Parse("#FF5A5F"), // Error — coral red
        Color.Parse("#FF2D68"), // Fatal — hot magenta-red
        Color.Parse("#B8C4D6"), // Unknown — desaturated steel
    ];

    private static ImmutableSolidColorBrush?[][] BuildFillCache()
    {
        var cache = new ImmutableSolidColorBrush?[Colors.Length][];
        for (var i = 0; i < cache.Length; i++)
        {
            cache[i] = new ImmutableSolidColorBrush?[256];
        }

        return cache;
    }

    private static ImmutableSolidColorBrush[] BuildSolidBrushes()
    {
        var brushes = new ImmutableSolidColorBrush[Colors.Length];
        for (var i = 0; i < brushes.Length; i++)
        {
            brushes[i] = new ImmutableSolidColorBrush(Colors[i]);
        }

        return brushes;
    }

    private static ImmutablePen[] BuildBaselinePens()
    {
        var pens = new ImmutablePen[Colors.Length];
        for (var i = 0; i < pens.Length; i++)
        {
            pens[i] = new ImmutablePen(
                new ImmutableSolidColorBrush(Color.FromArgb(230, Colors[i].R, Colors[i].G, Colors[i].B)),
                1);
        }

        return pens;
    }

    private static ImmutablePen[] BuildAccentPens()
    {
        var pens = new ImmutablePen[Colors.Length];
        for (var i = 0; i < pens.Length; i++)
        {
            pens[i] = new ImmutablePen(new ImmutableSolidColorBrush(Colors[i]), 3);
        }

        return pens;
    }
}

/// <summary>
/// Theme-aware chrome colors for the code-built views. The plan requires dark and light
/// themes from the beginning (§14.1); hardcoding the dark surface into each view made the
/// Light setting produce light Fluent controls floating on midnight panels.
/// </summary>
public static class WorkspacePalette
{
    public static Color Surface(bool dark) => dark ? Color.Parse("#080D16") : Color.Parse("#F4F7FC");
    public static Color SurfaceRaised(bool dark) => dark ? Color.Parse("#0D1625") : Color.Parse("#E9EFF7");
    public static Color SurfaceHeader(bool dark) => dark ? Color.Parse("#111C2D") : Color.Parse("#E1E9F4");
    public static Color BorderLine(bool dark) => dark ? Color.Parse("#243753") : Color.Parse("#C6D3E4");
    public static Color TextPrimary(bool dark) => dark ? Color.Parse("#EAF2FF") : Color.Parse("#172033");
    public static Color TextMuted(bool dark) => dark ? Color.Parse("#8FA5C4") : Color.Parse("#54647A");
    public static Color Accent(bool dark) => Color.Parse(dark ? "#43B4FF" : "#0B78D0");
    public static Color ChipFill(bool dark) => dark ? Color.Parse("#304DA3FF") : Color.Parse("#334DA3FF");
}
