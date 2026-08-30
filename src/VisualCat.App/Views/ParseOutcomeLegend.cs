using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VisualCat.App.Views;

/// <summary>
/// What the two-letter codes in a source gutter mean, said on screen.
/// </summary>
/// <remarks>
/// <para>
/// The source-context pane renders every line as
/// <c>&lt;line number&gt; &lt;code&gt; │ &lt;the file's own bytes&gt;</c>, where the code is
/// <c>SessionTabViewModel.DescribeOutcome</c>'s rendering of the parse outcome. Nowhere in the
/// product was that mapping shown. The caption beside it explains the divider and not the
/// code; the tooltip explains neither, and on a phone a tooltip cannot be reached at all —
/// a 1 200 ms press produced no pixel change, because Avalonia opens a tooltip on pointer-over
/// and a finger never hovers. The string does survive into the accessibility tree, so
/// TalkBack read an explanation a sighted touch user could not get (V2-04).
/// </para>
/// <para>
/// <c>??</c> and <c>!!</c> are exactly the rows where the parser is admitting it could not
/// read something — the rows a sceptical reader opened this pane for — and adversarial or
/// crash-shaped input makes them common. So the legend is a line of the pane, in the same
/// register as the severity legend the filter drawer already has, and it appears only when a
/// code other than <c>en</c> is actually on screen.
/// </para>
/// </remarks>
internal static class ParseOutcomeLegend
{
    /// <summary>The mapping, on one line.</summary>
    internal const string Text =
        "en entry · mt marker · .. continuation · e? untimed · ?? unknown · !! rejected";

    /// <summary>
    /// Whether a rendered gutter contains any code the ordinary case does not.
    /// </summary>
    /// <remarks>
    /// Asked of the text that is actually drawn rather than of the session's counters, so the
    /// legend arrives when the reader can see something it explains and stays away when every
    /// visible line parsed cleanly.
    /// </remarks>
    internal static bool AppliesTo(string? gutter)
    {
        if (string.IsNullOrEmpty(gutter))
        {
            return false;
        }

        foreach (var code in (ReadOnlySpan<string>)[" mt │", " .. │", " e? │", " ?? │", " !! │", "  │"])
        {
            if (gutter.Contains(code, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The legend as a caption, sized and toned like the rest of a pane's metadata.</summary>
    internal static TextBlock Caption(double fontSize) => new()
    {
        Text = Text,
        FontSize = fontSize,
        Opacity = 0.75,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 4),
        IsVisible = false,
    };
}
