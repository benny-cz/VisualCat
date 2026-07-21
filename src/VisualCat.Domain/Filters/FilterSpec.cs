using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Time;

namespace VisualCat.Domain.Filters;

/// <summary>Defines a bounded literal or regular-expression message search.</summary>
public sealed record TextSearchSpec(string Query, bool IsRegex = false, bool CaseSensitive = false, TimeSpan? RegexTimeout = null);

/// <summary>
/// Defines immutable include and exclude constraints shared by queries, views, and exports.
/// Empty sets mean that the dimension is unconstrained.
/// </summary>
public sealed record FilterSpec
{
    /// <summary>Gets the optional session-wide time constraint.</summary>
    public TimeRange? TimeRange { get; init; }
    /// <summary>Gets severities that are included.</summary>
    public ImmutableHashSet<LogLevel> IncludedLevels { get; init; } = ImmutableHashSet<LogLevel>.Empty;
    /// <summary>Gets tags that are included.</summary>
    public ImmutableHashSet<string> IncludedTags { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    /// <summary>Gets tags that are excluded.</summary>
    public ImmutableHashSet<string> ExcludedTags { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    /// <summary>Gets process IDs that are included.</summary>
    public ImmutableHashSet<int> IncludedPids { get; init; } = ImmutableHashSet<int>.Empty;
    /// <summary>Gets process IDs that are excluded.</summary>
    public ImmutableHashSet<int> ExcludedPids { get; init; } = ImmutableHashSet<int>.Empty;
    /// <summary>Gets resolved process names that are included.</summary>
    public ImmutableHashSet<string> IncludedProcesses { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    /// <summary>Gets resolved process names that are excluded.</summary>
    public ImmutableHashSet<string> ExcludedProcesses { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    /// <summary>Gets thread IDs that are included.</summary>
    public ImmutableHashSet<int> IncludedTids { get; init; } = ImmutableHashSet<int>.Empty;
    /// <summary>Gets thread IDs that are excluded.</summary>
    public ImmutableHashSet<int> ExcludedTids { get; init; } = ImmutableHashSet<int>.Empty;
    /// <summary>Gets template IDs that are included.</summary>
    public ImmutableHashSet<uint> IncludedTemplates { get; init; } = ImmutableHashSet<uint>.Empty;
    /// <summary>Gets template IDs that are excluded.</summary>
    public ImmutableHashSet<uint> ExcludedTemplates { get; init; } = ImmutableHashSet<uint>.Empty;
    /// <summary>Gets log buffers that are included.</summary>
    public ImmutableHashSet<string> IncludedBuffers { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    /// <summary>Gets log buffers that are excluded.</summary>
    public ImmutableHashSet<string> ExcludedBuffers { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    /// <summary>Gets physical-line outcomes that are included.</summary>
    public ImmutableHashSet<ParseOutcomeKind> IncludedOutcomes { get; init; } = ImmutableHashSet<ParseOutcomeKind>.Empty;
    /// <summary>Gets the optional message search constraint.</summary>
    public TextSearchSpec? Search { get; init; }

    /// <summary>Gets an unconstrained filter.</summary>
    public static FilterSpec All { get; } = new();

    /// <summary>Returns a deterministic SHA-256 identity for query-cache keys.</summary>
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
