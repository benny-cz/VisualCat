namespace VisualCat.Domain;

/// <summary>How a text artifact ends its lines.</summary>
/// <remarks>
/// <para>
/// Every text output used the host's native newline, so the same session exported on Linux and
/// on Windows differed by 5,001 carriage returns and nothing else — identical after stripping
/// them (finding F-29). A team with mixed machines could not diff two exports of one session,
/// check one into version control, or compare checksums in CI, and nothing in the product said
/// that was what differed.
/// </para>
/// <para>
/// The default is <see cref="Lf"/> everywhere, chosen rather than inherited: machine-readable
/// output that depends on which machine produced it is not machine-readable. <see cref="Crlf"/>
/// stays available for tools that need it.
/// </para>
/// </remarks>
public enum NewlineStyle
{
    /// <summary>A single line feed. The deterministic default on every platform.</summary>
    Lf,

    /// <summary>A carriage return and a line feed.</summary>
    Crlf,
}

/// <summary>Turns a <see cref="NewlineStyle"/> into the characters a writer emits.</summary>
public static class Newline
{
    /// <summary>The literal line terminator for a style.</summary>
    public static string Of(NewlineStyle style) => style == NewlineStyle.Crlf ? "\r\n" : "\n";
}
