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
    private static readonly Color[] LightInkColors = BuildLightInkColors();
    private static readonly ImmutableSolidColorBrush?[][] FillCache = BuildFillCache();
    private static readonly ImmutableSolidColorBrush[] SolidBrushes = BuildSolidBrushes();
    private static readonly ImmutableSolidColorBrush[] LightInkBrushes = BuildLightInkBrushes();
    private static readonly ImmutablePen[] BaselinePens = BuildBaselinePens();
    private static readonly ImmutablePen[] AccentPens = BuildAccentPens();
    private static readonly ImmutablePen[] CaretPens = BuildCaretPens();

    public static Color ColorOf(LogLevel level) => Colors[IndexOf(level)];

    /// <summary>Fully opaque cached brush for the level.</summary>
    public static ImmutableSolidColorBrush BrushOf(LogLevel level) => SolidBrushes[IndexOf(level)];

    /// <summary>
    /// The severity color as <em>ink</em>: the level letter, the tag, a heading — anything a
    /// reader reads rather than looks at.
    /// </summary>
    /// <remarks>
    /// The saturated palette was chosen against a midnight ground and every value clears AA on
    /// it. On the light surfaces it clears nothing: Warn measured 1.33:1, Debug 1.44:1 and the
    /// best of the seven, Fatal, 3.12:1 — and this is the ink of the first line of every entry
    /// row, the level letter and the tag, which is precisely what a log is scanned with
    /// (audit 3, B1). The light variant keeps each hue and darkens it until it clears 4.5:1 on
    /// all three light surfaces, so a row still reads Warn as amber and Fatal as magenta while
    /// being legible.
    ///
    /// Fills are a separate question and keep the saturated values: <see cref="BrushOf"/>,
    /// <see cref="Fill"/> and the pens below paint the plot, the minimap, the row ribbon and
    /// the legend chips, which are areas of color rather than text, and which the light theme
    /// already renders well.
    /// </remarks>
    public static Color InkOf(LogLevel level, bool dark) =>
        dark ? Colors[IndexOf(level)] : LightInkColors[IndexOf(level)];

    /// <summary>Cached opaque brush for <see cref="InkOf"/>.</summary>
    public static ImmutableSolidColorBrush InkBrushOf(LogLevel level, bool dark) =>
        dark ? SolidBrushes[IndexOf(level)] : LightInkBrushes[IndexOf(level)];

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

    /// <summary>
    /// The same seven hues, darkened for use as text on the light theme's surfaces.
    /// </summary>
    /// <remarks>
    /// Measured against the three grounds severity text lands on — <c>#F4F7FC</c>,
    /// <c>#E9EFF7</c> and <c>#E1E9F4</c> — the worst case of each of these is between 4.76:1
    /// and 5.81:1, so all seven clear AA for normal text on every one of them. Error and Fatal
    /// are further apart here (ΔE 34) than in the dark palette (ΔE 19), which is the one place
    /// the light variant is easier to tell apart than the original.
    /// </remarks>
    private static Color[] BuildLightInkColors() =>
    [
        Color.Parse("#6D28D9"), // Verbose — violet
        Color.Parse("#0B6E5B"), // Debug — deep mint
        Color.Parse("#0A66B8"), // Info — signal blue
        Color.Parse("#8A5A00"), // Warn — amber
        Color.Parse("#C42026"), // Error — coral red
        Color.Parse("#B00050"), // Fatal — magenta-red
        Color.Parse("#4A5C74"), // Unknown — steel
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

    private static ImmutableSolidColorBrush[] BuildLightInkBrushes()
    {
        var brushes = new ImmutableSolidColorBrush[LightInkColors.Length];
        for (var i = 0; i < brushes.Length; i++)
        {
            brushes[i] = new ImmutableSolidColorBrush(LightInkColors[i]);
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

    /// <summary>
    /// The search channel: what a match is marked with, wherever a match is marked.
    /// </summary>
    /// <remarks>
    /// One colour for the plot's search ticks and for the highlighted run inside a message, so
    /// a hit in the plot and a hit in the text read as one channel rather than as two
    /// unrelated marks. It is deliberately not on the severity ramp — a match is orthogonal to
    /// how bad a line is — and it is measurably far from all seven (ΔE 51 at the nearest,
    /// Verbose) and from the accent, so it cannot be misread as either.
    ///
    /// It lived as a literal in two files, which is the whole of what made it look like an
    /// accident rather than a decision (audit 3, D5). Same value in both variants: it is a
    /// filled mark carrying its own foreground, so it does not depend on what it sits on.
    /// </remarks>
    public static Color SearchMatch => Color.Parse("#FF3FE0");

    /// <summary>The ink on a search mark — 6.7:1 on it, in either theme.</summary>
    public static Color SearchMatchText => Color.Parse("#150411");

    /// <summary>An alpha variant of <paramref name="color"/>, for tints and scrims.</summary>
    public static Color Tint(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
}
