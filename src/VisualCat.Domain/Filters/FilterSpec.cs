using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Time;

namespace VisualCat.Domain.Filters;

public sealed record TextSearchSpec(string Query, bool IsRegex = false, bool CaseSensitive = false, TimeSpan? RegexTimeout = null);

public sealed record FilterSpec
{
    public TimeRange? TimeRange { get; init; }
    public ImmutableHashSet<LogLevel> IncludedLevels { get; init; } = ImmutableHashSet<LogLevel>.Empty;
    public ImmutableHashSet<string> IncludedTags { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public ImmutableHashSet<string> ExcludedTags { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public ImmutableHashSet<int> IncludedPids { get; init; } = ImmutableHashSet<int>.Empty;
    public ImmutableHashSet<int> ExcludedPids { get; init; } = ImmutableHashSet<int>.Empty;
    public ImmutableHashSet<string> IncludedProcesses { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public ImmutableHashSet<string> ExcludedProcesses { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public ImmutableHashSet<int> IncludedTids { get; init; } = ImmutableHashSet<int>.Empty;
    public ImmutableHashSet<int> ExcludedTids { get; init; } = ImmutableHashSet<int>.Empty;
    public ImmutableHashSet<uint> IncludedTemplates { get; init; } = ImmutableHashSet<uint>.Empty;
    public ImmutableHashSet<uint> ExcludedTemplates { get; init; } = ImmutableHashSet<uint>.Empty;
    public ImmutableHashSet<string> IncludedBuffers { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public ImmutableHashSet<string> ExcludedBuffers { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public ImmutableHashSet<ParseOutcomeKind> IncludedOutcomes { get; init; } = ImmutableHashSet<ParseOutcomeKind>.Empty;
    public TextSearchSpec? Search { get; init; }

    public static FilterSpec All { get; } = new();

    public string Fingerprint()
    {
        var builder = new StringBuilder(256);
        if (TimeRange is { } range)
        {
            builder.Append("t=").Append(range.StartInclusive.Value).Append(':').Append(range.EndExclusive.Value);
        }

        Append(builder, "l", IncludedLevels.Select(static x => ((byte)x).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Append(builder, "it", IncludedTags);
        Append(builder, "xt", ExcludedTags);
        Append(builder, "p", IncludedPids.Select(static x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Append(builder, "xpid", ExcludedPids.Select(static x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Append(builder, "pr", IncludedProcesses);
        Append(builder, "xpr", ExcludedProcesses);
        Append(builder, "d", IncludedTids.Select(static x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Append(builder, "xtid", ExcludedTids.Select(static x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Append(builder, "ip", IncludedTemplates.Select(static x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Append(builder, "xp", ExcludedTemplates.Select(static x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Append(builder, "b", IncludedBuffers);
        Append(builder, "xb", ExcludedBuffers);
        Append(builder, "o", IncludedOutcomes.Select(static x => ((byte)x).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (Search is { } search)
        {
            builder.Append("|s=").Append(search.IsRegex ? 'r' : 's').Append(search.CaseSensitive ? 'c' : 'i').Append(':').Append(search.Query);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void Append(StringBuilder builder, string name, IEnumerable<string> values)
    {
        builder.Append('|').Append(name).Append('=');
        foreach (var value in values.Order(StringComparer.Ordinal))
        {
            builder.Append(value.Length).Append(':').Append(value).Append(',');
        }
    }
}
