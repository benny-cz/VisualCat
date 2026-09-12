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
    IReadOnlyDictionary<string, string>? Checksums = null,
    long? SizeBytes = null);

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
    // Older sessions carry the complete table here. New writers leave it empty and
    // commit a prefix of the append-only template sidecar instead.
    IReadOnlyList<TemplateDefinition>? Templates,
    bool Finalized,
    DateTimeOffset UpdatedUtc,
    IReadOnlyList<ProcessNameRange>? ProcessNames = null,
    long? TemplateSidecarLength = null,
    long? SessionSizeBytes = null,

    // Names the committed template file. A live capture appends revisions to the default
    // one and a reader folds them by id; finalization writes a compacted file holding one
    // record per template and names that instead, so a finished session neither stores
    // nor parses a superseded revision.
    string? TemplateSidecarName = null);

/// <summary>
/// One thing verification found, with how many times it found it.
/// </summary>
/// <param name="Code">A stable code a script can branch on.</param>
/// <param name="Message">The first occurrence, phrased for a person.</param>
/// <param name="IsError">Whether it makes the session invalid.</param>
/// <param name="Occurrences">
/// How many identical findings this one stands for. A single corrupted segment file produced
/// roughly 380 byte-identical <c>source.index</c> objects — several screens of the same line for
/// one broken file, in a report a CI job prints (finding F-25). Repeats are collapsed into a
/// count and a bounded sample instead.
/// </param>
/// <param name="Sample">Up to five of the collapsed messages, so the shape stays visible.</param>
public sealed record VerificationIssue(
    string Code,
    string Message,
    bool IsError,
    long Occurrences = 1,
    IReadOnlyList<string>? Sample = null);

/// <summary>What verification found, and whether it could check everything it wanted to.</summary>
/// <remarks>
/// <para>
/// <see cref="IsValid"/> means "nothing was detected as wrong", which is not the same as "the
/// evidence was checked and matches". A standard session deliberately does not own its source,
/// so a deleted source is <c>isValid: true</c> with a <c>source.unavailable</c> note and exit 0
/// — and a script checking <c>$?</c>, which is what the exit-code contract invites, reads that
/// as a verified capture (finding F-28). <see cref="RawVerified"/> is that missing axis, stated
/// separately so neither answer has to stand for both.
/// </para>
/// </remarks>
public sealed record VerificationReport(
    string SessionPath,
    bool IsValid,
    IReadOnlyList<VerificationIssue> Issues,
    long EntriesChecked,
    long SourceRecordsChecked,
    bool RawVerified = true,
    bool Truncated = false)
{
    /// <summary>
    /// Whether the raw evidence was actually checked against its recorded hash, rather than
    /// found to be absent or skipped.
    /// </summary>
    public bool RawVerified { get; init; } = RawVerified;

    /// <summary>Whether distinct issues were dropped to keep the report bounded.</summary>
    public bool Truncated { get; init; } = Truncated;
}
