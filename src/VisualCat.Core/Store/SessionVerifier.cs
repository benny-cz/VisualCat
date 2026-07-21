using System.Security.Cryptography;

namespace VisualCat.Core.Store;

public static class SessionVerifier
{
    public static async Task<VerificationReport> VerifyAsync(
        string sessionPath,
        bool verifyRawHash = true,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<VerificationIssue>();
        long entries = 0;
        long sourceRecords = 0;
        SessionSnapshot? snapshot = null;
        try
        {
            snapshot = await SessionStore.OpenAsync(sessionPath, cancellationToken).ConfigureAwait(false);
            var sequences = new HashSet<long>();
            foreach (var segment in snapshot.Segments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                entries += segment.Count;
                long previousTimestamp = long.MinValue;
                long previousSequence = long.MinValue;
                for (var i = 0; i < segment.Count; i++)
                {
                    var timestamp = segment.TimestampAt(i);
                    var sequence = segment.SequenceAt(i);
                    if (timestamp < previousTimestamp || (timestamp == previousTimestamp && sequence < previousSequence))
                    {
                        issues.Add(new VerificationIssue("segment.sort", $"Segment {segment.Manifest.Id} is not stably sorted at index {i}.", true));
                        break;
                    }

                    if (!sequences.Add(sequence))
                    {
                        issues.Add(new VerificationIssue("sequence.duplicate", $"Duplicate source sequence {sequence}.", true));
                    }

                    previousTimestamp = timestamp;
                    previousSequence = sequence;
                }

                foreach (var checksum in segment.Manifest.Checksums)
                {
                    var file = Path.GetFullPath(Path.Combine(segment.DirectoryPath, checksum.Key.Replace('/', Path.DirectorySeparatorChar)));
                    var segmentRoot = Path.GetFullPath(segment.DirectoryPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!file.StartsWith(segmentRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new VerificationIssue("segment.path", $"Checksum path escapes its segment: {checksum.Key}.", true));
                    }
                    else if (!File.Exists(file))
                    {
                        issues.Add(new VerificationIssue("segment.file.missing", $"Missing {checksum.Key}.", true));
                    }
                    else if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))
                    {
                        issues.Add(new VerificationIssue("segment.file.link", $"Checksum path is a symbolic link or reparse point: {checksum.Key}.", true));
                    }
                    else if (!string.Equals(SegmentWriter.HashFile(file), checksum.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new VerificationIssue("segment.checksum", $"Checksum mismatch for {checksum.Key}.", true));
                    }
                }

                foreach (var required in SegmentFileContract.RequiredRelativePaths())
                {
                    if (!segment.Manifest.Checksums.ContainsKey(required))
                    {
                        issues.Add(new VerificationIssue(
                            "segment.checksum.missing",
                            $"Segment {segment.Manifest.Id} has no checksum for {required}.",
                            true));
                    }
                }

                var bitmapTotal = segment.SeverityBitmaps.Values.Sum(static bitmap => bitmap.Cardinality);
                if (bitmapTotal != segment.Count)
                {
                    issues.Add(new VerificationIssue("bitmap.cardinality", $"Severity bitmap total {bitmapTotal} differs from segment count {segment.Count}.", true));
                }
            }

            var recordsPath = Path.Combine(snapshot.RootPath, "source-order", "records.bin");
            if (File.Exists(recordsPath))
            {
                using var stream = new FileStream(recordsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new BinaryReader(stream);

                // The raw-context seek trusts this sidecar, so every offset it claims is
                // checked against where the record actually begins.
                var indexPath = Path.Combine(snapshot.RootPath, "source-order", "index.bin");
                using var indexStream = File.Exists(indexPath)
                    ? new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read)
                    : null;
                var indexReader = indexStream is null ? null : new BinaryReader(indexStream);
                var indexUsable = indexReader is not null;
                long expectedSequence = 0;
                long expectedOffset = 0;
                while (stream.Position < stream.Length)
                {
                    if (indexUsable)
                    {
                        if (indexStream!.Position + sizeof(long) > indexStream.Length)
                        {
                            issues.Add(new VerificationIssue(
                                "source.index",
                                $"Source-order index ends before record {expectedSequence}.",
                                true));
                            indexUsable = false;
                        }
                        else if (indexReader!.ReadInt64() != stream.Position)
                        {
                            issues.Add(new VerificationIssue(
                                "source.index",
                                $"Source-order index offset for record {expectedSequence} does not locate that record.",
                                true));
                        }
                    }

                    var record = SourceRecordCodec.Read(reader);
                    if (record.Sequence != expectedSequence)
                    {
                        issues.Add(new VerificationIssue("source.sequence", $"Expected source sequence {expectedSequence}, found {record.Sequence}.", true));
                        expectedSequence = record.Sequence;
                    }

                    if (record.Raw.Offset != expectedOffset)
                    {
                        issues.Add(new VerificationIssue("source.coverage", $"Expected raw offset {expectedOffset}, found {record.Raw.Offset}.", true));
                        expectedOffset = record.Raw.Offset;
                    }

                    if (!Enum.IsDefined(record.Outcome))
                    {
                        issues.Add(new VerificationIssue("source.outcome", $"Source record {record.Sequence} has invalid outcome {(byte)record.Outcome}.", true));
                    }

                    if (record.EntryId is { } entryId &&
                        record.Outcome != VisualCat.Domain.Entries.ParseOutcomeKind.UntimedEntry &&
                        !sequences.Contains(entryId))
                    {
                        issues.Add(new VerificationIssue("source.entry", $"Source record {record.Sequence} references missing entry {entryId}.", true));
                    }

                    expectedSequence++;
                    expectedOffset += record.Raw.Length;
                    sourceRecords++;
                }

                indexReader?.Dispose();

                if (expectedOffset != snapshot.Manifest.Source.Length)
                {
                    issues.Add(new VerificationIssue(
                        "source.coverage.length",
                        $"Declared outcomes cover {expectedOffset} bytes; source has {snapshot.Manifest.Source.Length}.",
                        true));
                }
            }
            else
            {
                issues.Add(new VerificationIssue("source.records.missing", "Source-order records are missing.", true));
            }

            if (verifyRawHash && snapshot.RawPath is { } rawPath && File.Exists(rawPath))
            {
                await using var source = new FileStream(rawPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false));
                if (!string.Equals(actual, snapshot.Manifest.Source.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new VerificationIssue("source.hash", "Raw source hash differs from the indexed source.", true));
                }
            }
            else if (verifyRawHash)
            {
                issues.Add(new VerificationIssue("source.unavailable", "Raw source is unavailable; the index is degraded.", false));
            }

            if (entries != snapshot.Descriptor.Counters.TimedEntries)
            {
                issues.Add(new VerificationIssue(
                    "summary.timed",
                    $"Manifest reports {snapshot.Descriptor.Counters.TimedEntries} timed entries; segments contain {entries}.",
                    true));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            issues.Add(new VerificationIssue("session.open", exception.Message, true));
        }
        finally
        {
            snapshot?.Dispose();
        }

        return new VerificationReport(
            Path.GetFullPath(sessionPath),
            issues.All(static issue => !issue.IsError),
            issues,
            entries,
            sourceRecords);
    }
}
