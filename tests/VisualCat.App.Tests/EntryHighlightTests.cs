using VisualCat.App.Views;
using VisualCat.Domain.Filters;

namespace VisualCat.App.Tests;

public sealed class EntryHighlightTests
{
    [Fact]
    public void NoSearchMarksNothing()
    {
        Assert.Empty(EntryHighlight.Match("FATAL EXCEPTION: main", null));
        Assert.Empty(EntryHighlight.Match("FATAL EXCEPTION: main", new TextSearchSpec(string.Empty)));
        Assert.Empty(EntryHighlight.Match(string.Empty, new TextSearchSpec("main")));
        Assert.Empty(EntryHighlight.Match(null, new TextSearchSpec("main")));
    }

    [Fact]
    public void LiteralSearchMarksEveryOccurrence()
    {
        var spans = EntryHighlight.Match("am zygote am cached", new TextSearchSpec("am"));

        Assert.Equal([new HighlightSpan(0, 2), new HighlightSpan(10, 2)], spans);
    }

    [Fact]
    public void LiteralSearchHonoursCaseSensitivity()
    {
        const string message = "AndroidRuntime and androidruntime";

        Assert.Equal(
            [new HighlightSpan(0, 14), new HighlightSpan(19, 14)],
            EntryHighlight.Match(message, new TextSearchSpec("androidruntime")));
        Assert.Equal(
            [new HighlightSpan(19, 14)],
            EntryHighlight.Match(message, new TextSearchSpec("androidruntime", CaseSensitive: true)));
    }

    [Fact]
    public void OverlappingCandidatesAreMarkedWithoutOverlapping()
    {
        // "aaaa" contains three overlapping "aa"; the marks must stay disjoint so the
        // inline runs they drive cover the message exactly once.
        var spans = EntryHighlight.Match("aaaa", new TextSearchSpec("aa"));

        Assert.Equal([new HighlightSpan(0, 2), new HighlightSpan(2, 2)], spans);
    }

    [Fact]
    public void MarkingStopsAtTheScannedWindow()
    {
        // A match ending exactly on the window boundary is still on screen to mark; one
        // that would run past it is not, and must not be reported as a partial run.
        var atBoundary = new string('.', EntryHighlight.MaximumScannedCharacters - 6) + "needle tail";
        var pastBoundary = new string('.', EntryHighlight.MaximumScannedCharacters - 5) + "needle tail";

        Assert.Equal(
            [new HighlightSpan(EntryHighlight.MaximumScannedCharacters - 6, 6)],
            EntryHighlight.Match(atBoundary, new TextSearchSpec("needle")));
        Assert.Empty(EntryHighlight.Match(pastBoundary, new TextSearchSpec("needle")));
        Assert.Empty(EntryHighlight.Match(pastBoundary, new TextSearchSpec("needle", IsRegex: true)));
    }

    [Fact]
    public void MarkCountIsCapped()
    {
        var message = new string('x', EntryHighlight.MaximumSpans * 4);

        Assert.Equal(EntryHighlight.MaximumSpans, EntryHighlight.Match(message, new TextSearchSpec("x")).Count);
    }

    [Fact]
    public void RegexSearchMarksMatches()
    {
        var spans = EntryHighlight.Match(
            "pid=9731 tid=9744",
            new TextSearchSpec("[0-9]+", IsRegex: true));

        Assert.Equal([new HighlightSpan(4, 4), new HighlightSpan(13, 4)], spans);
    }

    [Fact]
    public void ZeroWidthRegexMatchesTerminateAndMarkNothing()
    {
        Assert.Empty(EntryHighlight.Match("abc", new TextSearchSpec("x*", IsRegex: true)));
        Assert.Empty(EntryHighlight.Match("abc", new TextSearchSpec(@"\b", IsRegex: true)));
    }

    [Fact]
    public void InvalidRegexMarksNothingInsteadOfThrowing()
    {
        Assert.Empty(EntryHighlight.Match("abc", new TextSearchSpec("([unclosed", IsRegex: true)));
    }

    [Fact]
    public void RegexSearchRespectsCaseSensitivity()
    {
        const string message = "Fatal fatal";

        Assert.Equal(2, EntryHighlight.Match(message, new TextSearchSpec("fatal", IsRegex: true)).Count);
        Assert.Equal(
            [new HighlightSpan(6, 5)],
            EntryHighlight.Match(message, new TextSearchSpec("fatal", IsRegex: true, CaseSensitive: true)));
    }
}
