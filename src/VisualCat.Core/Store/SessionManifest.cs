using VisualCat.Domain.Entries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Templates;

namespace VisualCat.Core.Store;

public sealed record SourceIdentity(
    string Kind,
    string? Path,
    long Length,
    DateTimeOffset? LastWriteUtc,
    string Sha256,
    bool Embedded);

public sealed record SegmentManifest(
    int Id,
    string RelativePath,
    int EntryCount,
    long MinimumTimestampUs,
    long MaximumTimestampUs,
    long MinimumSequence,
    long MaximumSequence,
    // Digests of the segment's files, populated only in sessions written before they
    // moved to a per-segment sidecar. Read through SegmentChecksums.Load, which prefers
    // this when present and falls back to the sidecar otherwise.
    IReadOnlyDictionary<string, string>? Checksums = null);

public sealed record SessionManifest(
    string FormatVersion,
    SessionDescriptor Descriptor,
    SourceIdentity Source,
    IngestSettings IngestSettings,
    string ParserVersion,
    string TemplateAlgorithmVersion,
    long SnapshotGeneration,
    IReadOnlyList<SegmentManifest> Segments,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Buffers,
    IReadOnlyList<TemplateDefinition> Templates,
    bool Finalized,
    DateTimeOffset UpdatedUtc,
    IReadOnlyList<ProcessNameRange>? ProcessNames = null);

public sealed record VerificationIssue(string Code, string Message, bool IsError);

public sealed record VerificationReport(
    string SessionPath,
    bool IsValid,
    IReadOnlyList<VerificationIssue> Issues,
    long EntriesChecked,
    long SourceRecordsChecked);
