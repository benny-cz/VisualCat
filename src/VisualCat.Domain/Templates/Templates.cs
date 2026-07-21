using VisualCat.Domain.Time;

namespace VisualCat.Domain.Templates;

public sealed record TemplateAssignment(uint TemplateId, string CanonicalText, IReadOnlyList<string> Parameters);

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
