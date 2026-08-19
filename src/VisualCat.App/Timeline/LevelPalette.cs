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
    private static readonly ImmutablePen[] CaretPens = BuildCaretPens();

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

    /// <summary>Cached 1-px pen for a full-height locator line. Held below the baseline
    /// alpha so that spanning every lane locates a point without competing with the data
    /// the lanes are drawing.</summary>
    public static ImmutablePen CaretPen(LogLevel level) => CaretPens[IndexOf(level)];

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

    private static ImmutablePen[] BuildCaretPens()
    {
        var pens = new ImmutablePen[Colors.Length];
        for (var i = 0; i < pens.Length; i++)
        {
            pens[i] = new ImmutablePen(
                new ImmutableSolidColorBrush(Color.FromArgb(150, Colors[i].R, Colors[i].G, Colors[i].B)),
                1);
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

    /// <summary>Top of the command bar's gradient.</summary>
    /// <remarks>
    /// The shell band used to be one fixed near-black gradient in both variants, described as
    /// the application's identity band. On a phone set to light that is a #0C1422 slab wedged
    /// between a white system status bar and a white page, present from a cold start, and no
    /// amount of brand intent makes it read as anything but a rendering fault. The band keeps
    /// its shape and its wordmark in both variants; only the ground under them follows the
    /// theme (audit 2, A1a).
    /// </remarks>
    public static Color ShellTop(bool dark) => dark ? Color.Parse("#111C2D") : Color.Parse("#FFFFFF");

    /// <summary>Bottom of the command bar's gradient.</summary>
    public static Color ShellBottom(bool dark) => dark ? Color.Parse("#0B1220") : Color.Parse("#EDF2F9");

    /// <summary>The hairline that separates the command bar from the workspace.</summary>
    public static Color ShellEdge(bool dark) => dark ? Color.Parse("#243753") : Color.Parse("#C6D3E4");

    /// <summary>The wordmark and any primary label drawn on the shell band.</summary>
    public static Color ShellText(bool dark) => dark ? Color.Parse("#EAF2FF") : Color.Parse("#172033");

    /// <summary>The strapline and the transient message drawn on the shell band.</summary>
    /// <remarks>
    /// 6.7:1 on the light band and 4.9:1 on the dark one, so the 8 pt strapline clears AA for
    /// normal text in both variants rather than only in the one it was picked against.
    /// </remarks>
    public static Color ShellTextMuted(bool dark) => dark ? Color.Parse("#93A8C6") : Color.Parse("#4A5C74");

    /// <summary>What the platform's own status and navigation bars are painted with.</summary>
    /// <remarks>
    /// Avalonia's Android insets manager derives the bar's icon appearance from the luminance
    /// of this color, so a light value here is also what turns the status-bar glyphs dark.
    /// </remarks>
    public static Color SystemBar(bool dark) => dark ? Color.Parse("#11151C") : Color.Parse("#FFFFFF");

    /// <summary>The ground of a primary (accented) shell action.</summary>
    public static Color PrimaryActionFill(bool dark) => dark ? Color.Parse("#174F78") : Color.Parse("#D7EBFB");

    /// <summary>The outline of a primary (accented) shell action.</summary>
    public static Color PrimaryActionEdge(bool dark) => dark ? Color.Parse("#3CAFEF") : Color.Parse("#2E93D8");

    /// <summary>The label of a primary (accented) shell action.</summary>
    public static Color PrimaryActionText(bool dark) => dark ? Color.Parse("#EDF6FF") : Color.Parse("#10314C");

    /// <summary>The ground of a secondary shell action.</summary>
    public static Color SecondaryActionFill(bool dark) => dark ? Color.Parse("#172235") : Color.Parse("#E4ECF6");

    /// <summary>The outline of a secondary shell action.</summary>
    public static Color SecondaryActionEdge(bool dark) => dark ? Color.Parse("#2A3B55") : Color.Parse("#BFCFE2");

    /// <summary>An alpha variant of <paramref name="color"/>, for tints and scrims.</summary>
    public static Color Tint(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
}
