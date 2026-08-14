using VisualCat.Domain.Entries;

namespace VisualCat.App.Timeline;

/// <summary>
/// Resolves the visible severity lanes from the session and the immutable query filter.
/// Keeping this policy outside the renderer makes row geometry deterministic and directly
/// testable without allocating or inspecting anything in the render path.
/// </summary>
internal static class TimelineLevelLayout
{
    public static LogLevel[] Resolve(IReadOnlySet<LogLevel> includedLevels, bool sessionHasUnknown)
    {
        ArgumentNullException.ThrowIfNull(includedLevels);

        var unconstrained = includedLevels.Count == 0;
        var levels = LogLevels.DisplayOrder
            .ToArray()
            .Where(level =>
                level == LogLevel.Unknown
                    ? sessionHasUnknown && (unconstrained || includedLevels.Contains(level))
                    : unconstrained || includedLevels.Contains(level))
            .ToArray();

        // An explicit Unknown-only filter is a valid empty-result investigation. Preserve
        // that one lane even when the session currently contains no Unknown entries so the
        // plot still explains what is being queried and TimelineTransform always has a row.
        return levels.Length == 0 && includedLevels.Contains(LogLevel.Unknown)
            ? [LogLevel.Unknown]
            : levels;
    }
}
