using System.Text;
using System.Text.RegularExpressions;
using VisualCat.Domain.Filters;

namespace VisualCat.Core.Query;

/// <summary>
/// Why a regular expression the reader typed cannot be used, said in product language.
/// </summary>
/// <param name="Error">The parser's own classification, kept for diagnostics and tests.</param>
/// <param name="Offset">Where in the pattern the parser stopped, or -1 if it did not say.</param>
/// <param name="Description">One plain clause, with no trailing punctuation.</param>
public readonly record struct SearchPatternProblem(RegexParseError Error, int Offset, string Description)
{
    /// <summary>The whole sentence, position included where the parser gave one.</summary>
    public string Sentence => Offset >= 0
        ? $"Not a valid regular expression: {Description} (position {Offset})."
        : $"Not a valid regular expression: {Description}.";
}

/// <summary>
/// Turns a rejected pattern into something a reader can act on.
/// </summary>
/// <remarks>
/// The Android Release build sets <c>System.Resources.UseSystemResourceKeys=true</c> — the .NET
/// Android SDK's Release default, which trims framework resource strings — so
/// <c>RegexParseException.Message</c> degrades to its own resource key plus the arguments:
/// the status line read <c>Failed · MakeException, (unclosed, 9, InsufficientClosingParentheses</c>
/// on a device and the ordinary sentence everywhere a test runs, which is exactly why it
/// survived (finding F-04). The remedy is not to re-enable the framework strings: a BCL
/// sentence is still not product language, and it is not localised with the rest of the product.
/// The structured members of the exception — the error code and the offset — do not degrade, so
/// the sentence is composed here from those.
/// </remarks>
public static class SearchPattern
{
    /// <summary>
    /// Compiles the pattern the reader typed, or explains why it cannot be compiled.
    /// </summary>
    public static bool TryCompile(TextSearchSpec search, out Regex? regex, out SearchPatternProblem problem)
    {
        ArgumentNullException.ThrowIfNull(search);
        try
        {
            regex = SessionQueryEngine.CompileSearchRegex(search);
            problem = default;
            return true;
        }
        catch (RegexParseException parse)
        {
            regex = null;
            problem = new SearchPatternProblem(parse.Error, parse.Offset, Explain(parse.Error));
            return false;
        }
        catch (ArgumentException)
        {
            // A pattern rejected without a parse classification — an empty pattern with
            // options that require one, for instance. Still the reader's input, still not a
            // crash, and still not a framework sentence.
            regex = null;
            problem = new SearchPatternProblem(RegexParseError.Unknown, -1, Explain(RegexParseError.Unknown));
            return false;
        }
    }

    /// <summary>
    /// One plain clause per parser error.
    /// </summary>
    /// <remarks>
    /// The common errors are named, because a reader fixing <c>(unclosed</c> wants to be told
    /// about brackets rather than about a parser. Everything else falls back to the enum name
    /// spaced into words — still not a sentence a writer would choose, but readable, and it
    /// cannot regress into an unreadable key when the framework adds a member.
    /// </remarks>
    public static string Explain(RegexParseError error) => error switch
    {
        RegexParseError.InsufficientClosingParentheses => "there are more \"(\" than \")\"",
        RegexParseError.InsufficientOpeningParentheses => "there are more \")\" than \"(\"",
        RegexParseError.UnterminatedBracket => "a \"[\" character class was never closed with \"]\"",
        RegexParseError.UnterminatedComment => "a \"(?#\" comment was never closed",
        RegexParseError.UnescapedEndingBackslash => """the pattern ends with a single "\", which has nothing to escape""",
        RegexParseError.UnrecognizedEscape => """that "\" escape is not one the engine knows""",
        RegexParseError.QuantifierAfterNothing => "a repeat such as \"*\", \"+\" or \"?\" has nothing before it to repeat",
        RegexParseError.NestedQuantifiersNotParenthesized => "two repeats follow each other; put the inner one in a group",
        RegexParseError.ReversedQuantifierRange => "the repeat count counts down, as in \"{3,1}\"",
        RegexParseError.ReversedCharacterRange => "a character range runs backwards, as in \"[z-a]\"",
        RegexParseError.QuantifierOrCaptureGroupOutOfRange => "a repeat count or group number is too large",
        RegexParseError.InvalidGroupingConstruct => "a \"(?\" group is not one the engine knows",
        RegexParseError.CaptureGroupNameInvalid => "that capture group name is not allowed",
        RegexParseError.CaptureGroupOfZero => "a capture group cannot be numbered 0",
        RegexParseError.UndefinedNamedReference => """a "\k<name>" reference names a group the pattern does not define""",
        RegexParseError.UndefinedNumberedReference => "a backreference points at a group the pattern does not define",
        RegexParseError.MalformedNamedReference => "a named reference is malformed",
        RegexParseError.InsufficientOrInvalidHexDigits => """a "\x" or "\u" escape is missing its hexadecimal digits""",
        RegexParseError.MissingControlCharacter => """a "\c" escape is missing its control character""",
        RegexParseError.UnrecognizedControlCharacter => """that "\c" control character is not one the engine knows""",
        RegexParseError.UnrecognizedUnicodeProperty => """that "\p" Unicode property is not one the engine knows""",
        RegexParseError.InvalidUnicodePropertyEscape => """a "\p" Unicode property escape is not valid""",
        RegexParseError.MalformedUnicodePropertyEscape => """a "\p" Unicode property escape is malformed""",
        RegexParseError.ShorthandClassInCharacterRange => """a shorthand class such as "\d" cannot be an endpoint of a range""",
        RegexParseError.AlternationHasTooManyConditions => "a conditional alternation has more than two branches",
        RegexParseError.AlternationHasMalformedCondition => "a conditional alternation's condition is malformed",
        RegexParseError.AlternationHasMalformedReference => "a conditional alternation's reference is malformed",
        RegexParseError.AlternationHasUndefinedReference => "a conditional alternation names a group the pattern does not define",
        RegexParseError.AlternationHasNamedCapture => "a conditional alternation cannot open with a named capture",
        RegexParseError.AlternationHasComment => "a conditional alternation cannot open with a comment",
        RegexParseError.Unknown => "the engine could not read it",
        _ => Humanize(error.ToString()),
    };

    private static string Humanize(string name)
    {
        var text = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
            {
                text.Append(' ');
                text.Append(char.ToLowerInvariant(name[i]));
            }
            else
            {
                text.Append(i == 0 ? char.ToLowerInvariant(name[i]) : name[i]);
            }
        }

        return text.ToString();
    }
}
