using VisualCat.Domain.Time;

namespace VisualCat.Domain.Templates;

/// <summary>Associates an entry with a mined template and extracted parameters.</summary>
public sealed record TemplateAssignment(uint TemplateId, string CanonicalText, IReadOnlyList<string> Parameters);

/// <summary>Describes one persisted, versioned message template and its evidence.</summary>
public sealed record TemplateDefinition(
    uint TemplateId,
    string CanonicalText,
    string Algorithm,
    string Version,
    IReadOnlyList<string> Tokens,
    InstantUs? FirstOccurrence,
    InstantUs? LastOccurrence,
    long MatchCount,
    IReadOnlyList<long> RepresentativeEntryIds,
    string ContentHash);
