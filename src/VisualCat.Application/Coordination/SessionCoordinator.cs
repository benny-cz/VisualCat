using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading.Channels;
using VisualCat.Application.Ports;
using VisualCat.Core.Mining;
using VisualCat.Core.Parsing;
using VisualCat.Core.Store;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;

namespace VisualCat.Application.Coordination;

public sealed record ImportResult(SessionSnapshot Snapshot, FormatDetectionResult Detection, TimeSpan Elapsed);

public sealed class SessionCoordinator
{
    private static long _coordinatorCounter;

    public static async Task<ImportResult> ImportAsync(
        ILogSource source,
        string sessionDirectory,
        IngestSettings settings,
        IProgress<ProgressSnapshot>? progress = null,
        IDiagnosticSink? diagnostics = null,
        LiveViewerPresence? presence = null,
        CancellationToken gracefulStopToken = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        ArgumentNullException.ThrowIfNull(settings);
        ValidateSettings(settings);
        var coordinatorGeneration = Interlocked.Increment(ref _coordinatorCounter);
        var sessionId = Guid.NewGuid();
        var state = new SessionStateMachine();
        var createdUtc = DateTimeOffset.UtcNow;
        state.TransitionTo(SessionState.SelectingSource);
        // Liveness, not which live kind it is. An Android on-device capture is
        // SourceKind.Android, so it took the finite branch and every running capture stamped
        // its published manifests SessionState.Importing — Session info said "State: Importing"
        // beside a status line correctly saying "Capturing" (finding F-14). A source that is
        // not finite is a stream, whichever platform it comes from.
        state.TransitionTo(source.Metadata.IsFinite ? SessionState.Importing : SessionState.Connecting);
        var stopwatch = Stopwatch.StartNew();

        var samples = await source.ProbeAsync(200, cancellationToken).ConfigureAwait(false);
        ImportSourceException.ThrowIfUnsupportedEncoding(samples);

        var detection = settings.FormatOverride is { } format
            ? new FormatDetectionResult(format, [], 1, [new FormatCandidate(format, samples.Count, samples.Count * 6, 1)], samples.Count)
            : FormatDetector.Detect(samples);
        if (detection.PrimaryFormat == LogcatFormat.Unknown)
        {
            // Emptiness is not a detection failure — there is nothing to detect. A zero-byte
            // file produced the byte-for-byte identical card to 10 MiB of random noise, down
            // to the paragraph advising the reader to check that it is a logcat capture and
            // not a bug report, and the live-test plan lists an empty source specifically to
            // probe this message (V2-12). Branching before the verdict is what lets the two
            // cases say different, true things.
            if (samples.Count == 0)
            {
                throw new ImportSourceException(
                    ImportFailureReason.EmptySource,
                    "This file is empty — there is nothing to import.");
            }

            // States the fact only. What to do about it differs by platform — the desktop
            // import preview offers a format override and the Android companion has no such
            // control — so the remedy is added by whoever is talking to the user rather than
            // baked into a message that was advising phone users to use a desktop dialog.
            throw new ImportSourceException(
                ImportFailureReason.UndetectableFormat,
                "No supported logcat format could be detected in this file.");
        }

        await WriteDiagnosticAsync(
            "information",
            "ingest.detected",
            new Dictionary<string, string>
            {
                ["sourceKind"] = source.Metadata.Kind.ToString(),
                ["format"] = detection.PrimaryFormat.ToString(),
                ["confidence"] = detection.Confidence.ToString("R", CultureInfo.InvariantCulture),
                ["parseWorkers"] = settings.EffectiveParseWorkers.ToString(CultureInfo.InvariantCulture),
                ["batchBytes"] = settings.BatchBytes.ToString(CultureInfo.InvariantCulture),
            }).ConfigureAwait(false);

        var root = Path.GetFullPath(sessionDirectory);
        Directory.CreateDirectory(root);
        var identity = await InitialIdentityAsync(source.Metadata, settings.PortableRaw, cancellationToken).ConfigureAwait(false);
        await using var store = new SessionStoreWriter(root, settings, identity, presence);
        var resolver = new TimestampResolver(settings.TimestampPolicy);

        // Shard count changes only how the work is scheduled, never the templates it
        // produces (§9.4), so it is free to follow the machine rather than the session.
        var miner = new ShardedTemplateMiner(
            settings.TemplateSettings,
            Math.Clamp(Environment.ProcessorCount - 1, 1, 16));
        var processNames = new ProcessNameTracker();
        using var processSamplingStop = new CancellationTokenSource();
        var processSamplingTask = source is IProcessNameSource processNameSource
            ? SampleProcessNamesAsync(processNameSource, processNames, processSamplingStop.Token)
            : Task.CompletedTask;
        var rawChannel = Channel.CreateBounded<LineBatch>(new BoundedChannelOptions(settings.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true,
        });
        var parsedChannel = Channel.CreateBounded<ParsedBatch>(new BoundedChannelOptions(settings.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

        var context = new SourceReadContext(sessionId, coordinatorGeneration, root);
        long bytesRead = 0;
        long linesRead = 0;
        var counters = new MutableCounters();
        var first = (InstantUs?)null;
        var last = (InstantUs?)null;
        long nextBatch = 0;
        string activeBuffer = string.Empty;
        PendingLong? pendingLong = null;
        var pendingBatches = new SortedDictionary<long, ParsedBatch>();
        var lastProgress = TimeSpan.Zero;
        var hasReportedSnapshot = false;
        string? publishFailure = null;
        if (state.State == SessionState.Connecting)
        {
            state.TransitionTo(SessionState.Streaming);
        }

        // If the commit loop dies, parse workers would otherwise block forever writing
        // into a full bounded channel and keep the source open. Every pipeline stage
        // observes this token so the finally block can always reclaim them (§10.7).
        using var pipelineAbort = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pipelineToken = pipelineAbort.Token;
        var readerTask = ReadAsync();
        var parseTasks = Enumerable.Range(0, settings.EffectiveParseWorkers).Select(_ => ParseAsync()).ToArray();
        var completionTask = CompleteParsedAsync();
        var drained = false;

        try
        {
            await foreach (var parsed in parsedChannel.Reader.ReadAllAsync(pipelineToken).ConfigureAwait(false))
            {
                pendingBatches.Add(parsed.BatchId, parsed);
                while (pendingBatches.Remove(nextBatch, out var ordered))
                {
                    // §5.5 template-partition stage. The batch is already in source
                    // order, so mining it here — before the commit walk — moves masking
                    // and clustering onto the shard pool while the identities themselves
                    // are still handed out in source order below (§9.4). Long-format
                    // records are excluded: their message is not complete until their
                    // body lines have been read, so they are mined one at a time when
                    // committed.
                    var templateIds = MineBatch(ordered);
                    var outcomeIndex = -1;
                    foreach (var outcome in ordered.Outcomes)
                    {
                        outcomeIndex++;
                        cancellationToken.ThrowIfCancellationRequested();

                        // The parse worker already stamped the source sequence, so the
                        // outcome arrives with its final identity. Only a long-format
                        // body reclassification still needs a copy, below.
                        var current = outcome;
                        if (pendingLong is not null &&
                            current.Kind is ParseOutcomeKind.Continuation or ParseOutcomeKind.UnknownLine)
                        {
                            current = current with { Kind = ParseOutcomeKind.Continuation, Reason = "long-format body" };
                        }

                        counters.ObserveOutcome(current);
                        store.AddOutcome(current, current.Fields is null ? null : current.Source.Sequence);
                        if (current.Kind == ParseOutcomeKind.MetaRecord)
                        {
                            if (pendingLong is not null)
                            {
                                await CommitLongAsync().ConfigureAwait(false);
                            }

                            if (current.Reason?.StartsWith("buffer:", StringComparison.Ordinal) == true)
                            {
                                activeBuffer = current.Reason["buffer:".Length..];
                            }

                            continue;
                        }

                        if (current.Fields is { Format: LogcatFormat.LongFormat } longHeader)
                        {
                            if (pendingLong is not null)
                            {
                                await CommitLongAsync().ConfigureAwait(false);
                            }

                            pendingLong = new PendingLong(current, longHeader);
                            continue;
                        }

                        if (pendingLong is not null &&
                            current.Kind == ParseOutcomeKind.Continuation)
                        {
                            pendingLong.Body.Add(DecodeBody(current.Source.Bytes.Span));
                            pendingLong.RawEnd = current.Source.Raw.Offset + current.Source.Raw.Length;
                            continue;
                        }

                        if (pendingLong is not null)
                        {
                            await CommitLongAsync().ConfigureAwait(false);
                        }

                        if (current.Fields is { } fields)
                        {
                            // Synchronous unless this entry closed a segment, so the
                            // common line costs no await and no state machine.
                            var prepared = templateIds is null ? null : (PreparedEntry?)templateIds[outcomeIndex];
                            if (CommitEntry(fields, current.Source, prepared) is not null)
                            {
                                await PublishFlushedSegmentAsync().ConfigureAwait(false);
                            }
                        }
                    }

                    // The first live batch is the user's confirmation that capture
                    // actually works. A time-based store flush is normally evaluated by
                    // the next entry, but a quiet source may have no next entry, leaving
                    // valid data invisible indefinitely. Publish that first completed
                    // batch now; later batches keep the adaptive 1–4 second segment
                    // cadence and avoid accumulating tiny segments (§10.6, §15.2).
                    if (source.Metadata.Kind is SourceKind.Adb or SourceKind.Android or SourceKind.GrowingFile &&
                        store.Generation == 0 &&
                        counters.TimedEntries > 0 &&
                        store.FlushSegment() is not null)
                    {
                        await PublishFlushedSegmentAsync().ConfigureAwait(false);
                    }

                    nextBatch++;
                }

                PublishProgress(IngestStage.Committing, false);
            }

            await readerTask.ConfigureAwait(false);
            await Task.WhenAll(parseTasks).ConfigureAwait(false);
            await completionTask.ConfigureAwait(false);
            drained = true;
            if (pendingLong is not null)
            {
                await CommitLongAsync().ConfigureAwait(false);
            }

            PublishProgress(IngestStage.Compacting, true);
            if (settings.PortableRaw && source.Metadata.SourcePath is { } sourcePath)
            {
                await SessionStoreWriter.EmbedRawAsync(sourcePath, root, cancellationToken).ConfigureAwait(false);
            }

            if (identity.Embedded)
            {
                var rawPath = Path.Combine(root, "raw.log");
                if (File.Exists(rawPath))
                {
                    store.UpdateSourceIdentity(await IdentityOfRawAsync(rawPath, true, cancellationToken).ConfigureAwait(false));
                }
            }

            await StopProcessSamplingAsync().ConfigureAwait(false);
            var descriptor = CreateDescriptor(SessionState.Ready, true);
            await store.FinalizeAsync(
                descriptor,
                // Finalization writes authoritative counts, time bounds, and examples.
                // Progressive snapshots publish only new or generalized shapes so the
                // sidecar remains linear even when every hot template matches forever.
                miner.GetDefinitions(),
                processNames.Snapshot(),
                cancellationToken).ConfigureAwait(false);
            miner.MarkDefinitionsPublished();
            if (state.State == SessionState.Streaming)
            {
                state.TransitionTo(SessionState.Stopping);
                state.TransitionTo(SessionState.Stopped);
            }

            state.TransitionTo(SessionState.Ready);
            PublishProgress(IngestStage.Ready, true, SessionState.Ready);
            await WriteDiagnosticAsync(
                "information",
                "ingest.ready",
                new Dictionary<string, string>
                {
                    ["elapsedMilliseconds"] = stopwatch.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                    ["sourceBytes"] = bytesRead.ToString(CultureInfo.InvariantCulture),
                    ["sourceLines"] = linesRead.ToString(CultureInfo.InvariantCulture),
                    ["timedEntries"] = counters.TimedEntries.ToString(CultureInfo.InvariantCulture),
                    ["snapshotGeneration"] = store.Generation.ToString(CultureInfo.InvariantCulture),
                }).ConfigureAwait(false);
            var snapshot = await SessionStore.OpenAsync(root, cancellationToken).ConfigureAwait(false);
            return new ImportResult(snapshot, detection, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            await DrainPipelineAsync().ConfigureAwait(false);
            if (state.State is SessionState.Importing or SessionState.Connecting or SessionState.Streaming or SessionState.Paused)
            {
                state.TransitionTo(SessionState.Cancelling);
                state.TransitionTo(SessionState.Cancelled);
            }

            PublishProgress(IngestStage.Cancelled, true, SessionState.Cancelled);
            await PublishPartialSafelyAsync(SessionState.Cancelled).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await DrainPipelineAsync().ConfigureAwait(false);
            if (state.State is not SessionState.Failed and not SessionState.Ready)
            {
                state.TransitionTo(SessionState.Failed);
            }

            PublishProgress(IngestStage.Failed, true, SessionState.Failed, exception.Message);
            await PublishPartialSafelyAsync(SessionState.Failed).ConfigureAwait(false);
            await WriteDiagnosticAsync(
                "error",
                "ingest.failed",
                new Dictionary<string, string>
                {
                    ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name,
                    ["bytesRead"] = bytesRead.ToString(CultureInfo.InvariantCulture),
                    ["linesRead"] = linesRead.ToString(CultureInfo.InvariantCulture),
                }).ConfigureAwait(false);
            throw new SessionPipelineException(IngestStage.Committing, bytesRead, linesRead, exception);
        }
        finally
        {
            await DrainPipelineAsync().ConfigureAwait(false);
            await StopProcessSamplingAsync().ConfigureAwait(false);
        }

        // Reclaims every task this coordinator owns. Faults are already represented by
        // the exception being propagated, so they are observed and dropped here rather
        // than replacing the causal failure.
        async Task DrainPipelineAsync()
        {
            if (drained)
            {
                return;
            }

            drained = true;
            await pipelineAbort.CancelAsync().ConfigureAwait(false);
            foreach (var task in parseTasks.Append(readerTask).Append(completionTask))
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Observed so the task does not become an unobserved exception.
                }
            }
        }

        async Task ReadAsync()
        {
            Exception? failure = null;
            FileStream? rawCapture = null;
            using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(pipelineToken, gracefulStopToken);
            try
            {
                if (source.Metadata.SourcePath is null)
                {
                    rawCapture = new FileStream(
                        Path.Combine(root, "raw.log"),
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.Read,
                        // Published snapshots are read while capture continues. Disable
                        // FileStream's private buffer so a committed raw span is visible to
                        // that reader as soon as its batch enters the parsing pipeline.
                        1,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                }

                await foreach (var batch in LineBatching.ReadBatchesAsync(
                                   source,
                                   context,
                                   settings.BatchBytes,
                                   settings.MaximumLineBytes,
                                   TimeSpan.FromMilliseconds(settings.BatchLatencyMilliseconds),
                                   readCancellation.Token).ConfigureAwait(false))
                {
                    if (rawCapture is not null)
                    {
                        await rawCapture.WriteAsync(batch.Bytes, pipelineToken).ConfigureAwait(false);
                    }

                    // Only the commit coordinator publishes progress (§5.5). The reader
                    // records its own totals atomically so the committer can read a
                    // consistent value without sharing the mutable counter object.
                    Interlocked.Add(ref bytesRead, batch.Bytes.Length);
                    Interlocked.Add(ref linesRead, batch.Lines.Count);
                    await rawChannel.Writer.WriteAsync(batch, pipelineToken).ConfigureAwait(false);
                }

                if (rawCapture is not null)
                {
                    await rawCapture.FlushAsync(pipelineToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (
                gracefulStopToken.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                // A stop request ends acquisition but is not an aborted session.
                // Complete the channels normally so already-read batches drain,
                // compact, and publish a reopenable Ready snapshot.
                if (rawCapture is not null)
                {
                    await rawCapture.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
                throw;
            }
            finally
            {
                if (rawCapture is not null)
                {
                    await rawCapture.DisposeAsync().ConfigureAwait(false);
                }

                rawChannel.Writer.TryComplete(failure);
            }
        }

        async Task ParseAsync()
        {
            await foreach (var batch in rawChannel.Reader.ReadAllAsync(pipelineToken).ConfigureAwait(false))
            {
                var outcomes = new ParseOutcome[batch.Lines.Count];
                for (var index = 0; index < batch.Lines.Count; index++)
                {
                    var slice = batch.Lines[index];

                    // The batch carries the sequence of its first line and lines within a
                    // batch are contiguous, so the final identity is known here. Leaving
                    // it zero and stamping it on the commit thread meant rebuilding both
                    // the outcome and its source line for every line in the session.
                    var sourceLine = new SourceLine(
                        sessionId,
                        batch.FirstSequence + index,
                        new RawSpan(slice.RawOffset, slice.Length),
                        batch.Bytes.AsMemory(slice.Offset, slice.Length));
                    outcomes[index] = slice.ExceededLimit
                        ? ParseOutcome.Rejected(sourceLine, $"line exceeds {settings.MaximumLineBytes} byte safety limit")
                        : LogcatParser.Parse(sourceLine, detection.PrimaryFormat);
                }

                await parsedChannel.Writer.WriteAsync(new ParsedBatch(batch.BatchId, outcomes), pipelineToken).ConfigureAwait(false);
            }
        }

        async Task CompleteParsedAsync()
        {
            Exception? failure = null;
            try
            {
                await Task.WhenAll(parseTasks).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                parsedChannel.Writer.TryComplete(failure);
            }
        }

        // Split deliberately: the per-line work is synchronous and the rare segment
        // publication is not. Awaiting one `async Task` per line allocated a state
        // machine box and a Task for every line in the session even though the await
        // completed synchronously in all but one call in a hundred thousand (§19.3).
        async Task CommitAsync(ParsedFields fields, SourceLine sourceLine)
        {
            var flushed = CommitEntry(fields, sourceLine, null);
            if (flushed is not null)
            {
                await PublishFlushedSegmentAsync().ConfigureAwait(false);
            }
        }

        // Resolves and mines a whole ordered batch ahead of the commit walk, or returns
        // null when the batch must be handled entry by entry (mining disabled, or a long
        // format whose messages are assembled at commit time).
        //
        // Timestamps are resolved here rather than during the walk because a template
        // records the first and last instant it was seen at, so the miner needs the
        // resolved instant, not the raw token. Resolution is stateful — year inference,
        // rollover, and out-of-order detection all depend on the entries before it — so
        // it runs over the batch in the same source order the commit walk will use,
        // which makes the two indistinguishable in their effect on resolver state.
        PreparedEntry[]? MineBatch(ParsedBatch batch)
        {
            if (!settings.TemplateSettings.Enabled || detection.PrimaryFormat == LogcatFormat.LongFormat)
            {
                return null;
            }

            var count = batch.Outcomes.Count;
            var prepared = new PreparedEntry[count];
            var mined = new MinedEntry[count];
            for (var index = 0; index < count; index++)
            {
                var outcome = batch.Outcomes[index];
                if (outcome.Fields is not { } fields || fields.Format == LogcatFormat.LongFormat)
                {
                    // Meta, blank, unknown, and rejected lines are not entries. A long-format
                    // header inside another primary format is not one either: the commit walk
                    // assembles it with its body and mines that completed entry later. Mining
                    // the header here would publish a phantom, unreferenced empty template.
                    continue;
                }

                var resolved = resolver.Resolve(fields.Timestamp, outcome.Source.ArrivalInstant);
                prepared[index] = new PreparedEntry(resolved, 0);
                mined[index] = new MinedEntry(fields.Tag, fields.Message, resolved.Instant, outcome.Source.Sequence);
            }

            var ids = new uint[count];
            miner.AssignBatch(mined, ids);
            for (var index = 0; index < count; index++)
            {
                prepared[index] = prepared[index] with { TemplateId = ids[index] };
            }

            return prepared;
        }

        SegmentManifest? CommitEntry(ParsedFields fields, SourceLine sourceLine, PreparedEntry? prepared)
        {
            counters.NextEntrySequence++;

            // Normally the batch pre-pass already resolved and mined this entry; the
            // fallback path (long format, or mining disabled) does both here instead.
            var resolved = prepared?.Resolved ?? resolver.Resolve(fields.Timestamp, sourceLine.ArrivalInstant);
            var attributes = fields.Attributes | resolved.Attributes;

            // Mined before the entry is built, so the template lands in the constructor
            // instead of forcing a second copy of every entry in the session.
            var templateId = prepared?.TemplateId
                             ?? miner.AssignOne(new MinedEntry(fields.Tag, fields.Message, resolved.Instant, sourceLine.Sequence));
            var normalized = new NormalizedEntry(
                sessionId,
                sourceLine.Sequence,
                sourceLine.Sequence,
                sourceLine.Raw,
                resolved.Instant,
                fields.Timestamp?.OriginalText ?? string.Empty,
                resolved.Provenance,
                resolved.Confidence,
                fields.Pid,
                fields.Tid,
                fields.Level,
                fields.Tag,
                fields.Buffer ?? activeBuffer,
                fields.Message,
                fields.Format,
                "2",
                templateId,
                attributes);
            var flushed = store.AddEntry(normalized);
            counters.ObserveEntry(normalized, fields.ChattyDeclaredDrops);
            if (resolved.Instant is { } instant)
            {
                first = first is null || instant < first ? instant : first;
                last = last is null || instant > last ? instant : last;
            }

            return flushed;
        }

        async Task PublishFlushedSegmentAsync()
        {
            var partial = CreateDescriptor(state.State, false);
            try
            {
                await store.PublishSnapshotAsync(
                    partial,
                    miner.GetChangedDefinitions(),
                    processNames.Snapshot(),
                    cancellationToken).ConfigureAwait(false);
                miner.MarkDefinitionsPublished();
                publishFailure = null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Publication makes already-captured data visible; it is not what makes
                // it durable. The segments are on disk and the next flush republishes a
                // manifest covering them, so a momentarily unwritable manifest costs the
                // viewer a few seconds of freshness rather than costing the user the
                // capture (§10.8).
                publishFailure = exception.Message;
                await WriteDiagnosticAsync(
                    "warning",
                    "store.snapshot.deferred",
                    new Dictionary<string, string>
                    {
                        ["snapshotGeneration"] = store.Generation.ToString(CultureInfo.InvariantCulture),
                        ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name,
                    }).ConfigureAwait(false);
                return;
            }

            await WriteDiagnosticAsync(
                "information",
                "store.snapshot",
                new Dictionary<string, string>
                {
                    ["snapshotGeneration"] = store.Generation.ToString(CultureInfo.InvariantCulture),
                    ["timedEntries"] = counters.TimedEntries.ToString(CultureInfo.InvariantCulture),
                    ["segments"] = store.SegmentCount.ToString(CultureInfo.InvariantCulture),
                    ["compactedSegments"] = store.CompactedSegments.ToString(CultureInfo.InvariantCulture),
                }).ConfigureAwait(false);
        }

        async Task CommitLongAsync()
        {
            var current = pendingLong!;
            var body = string.Join('\n', current.Body);
            var fields = current.Fields with { Message = body };
            await CommitAsync(
                fields,
                current.Outcome.Source with
                {
                    Raw = new RawSpan(
                        current.Outcome.Source.Raw.Offset,
                        checked((int)(current.RawEnd - current.Outcome.Source.Raw.Offset))),
                }).ConfigureAwait(false);
            pendingLong = null;
        }

        SessionDescriptor CreateDescriptor(SessionState sessionState, bool finalized)
        {
            var observedBytes = Interlocked.Read(ref bytesRead);
            var observedLines = Interlocked.Read(ref linesRead);
            return new SessionDescriptor(
                sessionId,
                source.Metadata.DisplayName,
                source.Metadata.Kind,
                source.Metadata.Description,
                createdUtc,
                sessionState,
                store.Generation,
                detection.PrimaryFormat,
                detection.Confidence,
                settings.TimestampPolicy,
                settings.TemplateSettings,
                counters.ToCounters(observedBytes, observedLines, miner.TemplateCount),
                counters.ToDefects((source as ISourceDefectSource)?.GetDefects(), miner.OverflowAssignments),
                first,
                last,
                finalized,
                false,
                CaptureSettings: source.Metadata.Capture);
        }

        void PublishProgress(
            IngestStage stage,
            bool force,
            SessionState? terminal = null,
            string? error = null)
        {
            // A quiet live source can publish its first snapshot before the ordinary
            // 100 ms progress cadence, then produce no more batches. Suppressing that
            // sole notification leaves the UI empty indefinitely even though the store
            // already contains viewable data. Always report the first non-zero snapshot;
            // later generations retain the throttle used by busy captures.
            var firstPublishedSnapshot = !hasReportedSnapshot && store.Generation > 0;
            if (!force && !firstPublishedSnapshot && stopwatch.Elapsed - lastProgress < TimeSpan.FromMilliseconds(100))
            {
                return;
            }

            lastProgress = stopwatch.Elapsed;
            hasReportedSnapshot |= store.Generation > 0;
            var observedBytes = Interlocked.Read(ref bytesRead);
            var observedLines = Interlocked.Read(ref linesRead);
            var throughput = stopwatch.Elapsed.TotalSeconds <= 0 ? 0 : counters.ParsedEntries / stopwatch.Elapsed.TotalSeconds;
            TimeSpan? remaining = null;
            if (source.Metadata.Length is { } total && observedBytes > 0 && throughput > 0)
            {
                var fraction = observedBytes / (double)total;
                if (fraction > 0.01)
                {
                    remaining = TimeSpan.FromSeconds(stopwatch.Elapsed.TotalSeconds * ((1 - fraction) / fraction));
                }
            }

            progress?.Report(new ProgressSnapshot(
                sessionId,
                coordinatorGeneration,
                stage,
                observedBytes,
                counters.BytesCommitted,
                observedLines,
                counters.Outcomes,
                source.Metadata.Length,
                counters.ToCounters(observedBytes, observedLines, miner.TemplateCount),
                throughput,
                stopwatch.Elapsed,
                remaining,
                source.Metadata.Length is null,
                terminal is null,
                store.Generation,
                terminal,
                error,
                store.SegmentCount,
                DescribeRecoveredTrouble()));
        }

        // Conditions the capture worked through rather than failed on. Reported so the
        // user learns that something is wrong while it is still recoverable, instead of
        // finding out when it stops being recoverable (§10.8, §15.2).
        string? DescribeRecoveredTrouble()
        {
            if (store.DeferredFlushFailure is { } flushFailure)
            {
                return $"Holding {store.PendingEntryCount:N0} captured entries in memory — storage is not accepting writes: {flushFailure}";
            }

            if (publishFailure is { } manifestFailure)
            {
                return $"The view is a few seconds behind the capture — the session manifest could not be updated: {manifestFailure}";
            }

            return null;
        }

        async Task PublishPartialSafelyAsync(SessionState sessionState)
        {
            try
            {
                store.FlushSegment();
                await store.PublishSnapshotAsync(
                    CreateDescriptor(sessionState, false),
                    miner.GetChangedDefinitions(),
                    processNames.Snapshot(),
                    CancellationToken.None).ConfigureAwait(false);
                miner.MarkDefinitionsPublished();
            }
            catch
            {
                // The original pipeline exception remains the authoritative failure.
            }
        }

        async Task StopProcessSamplingAsync()
        {
            if (!processSamplingStop.IsCancellationRequested)
            {
                processSamplingStop.Cancel();
            }

            try
            {
                await processSamplingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (processSamplingStop.IsCancellationRequested)
            {
            }
        }

        async Task WriteDiagnosticAsync(
            string level,
            string name,
            IReadOnlyDictionary<string, string> properties)
        {
            if (diagnostics is null)
            {
                return;
            }

            try
            {
                await diagnostics.WriteAsync(
                    new DiagnosticEvent(
                        DateTimeOffset.UtcNow,
                        level,
                        "session-coordinator",
                        name,
                        sessionId,
                        coordinatorGeneration,
                        properties),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Diagnostics never change ingest correctness or liveness.
            }
        }
    }

    private static void ValidateSettings(IngestSettings settings)
    {
        if (!string.Equals(settings.EncodingName, "utf-8", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("VisualCat currently accepts UTF-8 log input only.", nameof(settings));
        }

        if (settings.BatchBytes is < 1 or > 256 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Batch size must be between 1 byte and 256 MiB.");
        }

        if (settings.BatchLatencyMilliseconds is < 1 or > 60_000)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Batch latency must be between 1 ms and 60 seconds.");
        }

        if (settings.ChannelCapacity is < 1 or > 1_024)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Channel capacity must be between 1 and 1,024.");
        }

        if (settings.ParseWorkers is < 0 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Parse worker count must be automatic or between 1 and 256.");
        }

        if (settings.TemplateSettings.MaximumClusters is < 1 or > TemplateSettings.AbsoluteMaximumClusters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                $"The session-wide template cluster limit must be between 1 and {TemplateSettings.AbsoluteMaximumClusters:N0}.");
        }

        if (settings.SegmentEntries is < 1 or > 5_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Segment size must be between 1 and 5,000,000 entries.");
        }

        if (settings.ReorderHorizonUs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Reorder horizon cannot be negative.");
        }

        if (settings.MaximumLineBytes is < 1 or > 256 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Maximum line size must be between 1 byte and 256 MiB.");
        }
    }

    private static async Task SampleProcessNamesAsync(
        IProcessNameSource source,
        ProcessNameTracker tracker,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                tracker.Observe(await source.GetProcessNamesAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Process-name evidence is advisory. Capture correctness must not
                // depend on the device supporting a particular `ps` dialect.
            }

            if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                break;
            }
        }
    }

    private static async Task<SourceIdentity> InitialIdentityAsync(
        SourceMetadata metadata,
        bool portable,
        CancellationToken cancellationToken)
    {
        if (metadata.SourcePath is { } path)
        {
            var identity = await SessionStoreWriter.CreateFileIdentityAsync(path, portable, cancellationToken).ConfigureAwait(false);
            return identity;
        }

        return new SourceIdentity(metadata.Kind.ToString().ToLowerInvariant(), null, metadata.Length ?? 0, null, string.Empty, true);
    }

    private static async Task<SourceIdentity> IdentityOfRawAsync(string rawPath, bool embedded, CancellationToken cancellationToken)
    {
        var info = new FileInfo(rawPath);
        await using var stream = new FileStream(rawPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return new SourceIdentity("capture", null, info.Length, info.LastWriteTimeUtc, Convert.ToHexString(hash), embedded);
    }

    private static string DecodeBody(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.IsEmpty && bytes[^1] == (byte)'\n')
        {
            bytes = bytes[..^1];
        }

        if (!bytes.IsEmpty && bytes[^1] == (byte)'\r')
        {
            bytes = bytes[..^1];
        }

        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private sealed class PendingLong(ParseOutcome outcome, ParsedFields fields)
    {
        public ParseOutcome Outcome { get; } = outcome;
        public ParsedFields Fields { get; } = fields;
        public List<string> Body { get; } = [];
        public long RawEnd { get; set; } = outcome.Source.Raw.Offset + outcome.Source.Raw.Length;
    }

    private sealed class ProcessNameTracker
    {
        private readonly object _gate = new();
        private readonly List<ProcessNameRange> _ranges = [];

        public void Observe(IReadOnlyList<ProcessNameRange> observations)
        {
            lock (_gate)
            {
                foreach (var observation in observations)
                {
                    if (observation.Pid < 0 ||
                        string.IsNullOrWhiteSpace(observation.Name) ||
                        observation.Name.Length > 4096 ||
                        observation.LastSeen < observation.FirstSeen)
                    {
                        continue;
                    }

                    var latestPidIndex = _ranges.FindLastIndex(range => range.Pid == observation.Pid);
                    if (latestPidIndex >= 0 &&
                        string.Equals(_ranges[latestPidIndex].Name, observation.Name, StringComparison.Ordinal))
                    {
                        var existing = _ranges[latestPidIndex];
                        _ranges[latestPidIndex] = existing with
                        {
                            FirstSeen = observation.FirstSeen < existing.FirstSeen ? observation.FirstSeen : existing.FirstSeen,
                            LastSeen = observation.LastSeen > existing.LastSeen ? observation.LastSeen : existing.LastSeen,
                        };
                        continue;
                    }

                    if (latestPidIndex >= 0 && _ranges[latestPidIndex].LastSeen >= observation.FirstSeen)
                    {
                        var prior = _ranges[latestPidIndex];
                        _ranges[latestPidIndex] = prior with
                        {
                            LastSeen = new InstantUs(Math.Max(prior.FirstSeen.Value, observation.FirstSeen.Value - 1)),
                        };
                    }

                    _ranges.Add(observation);
                }
            }
        }

        public ProcessNameRange[] Snapshot()
        {
            lock (_gate)
            {
                return _ranges
                    .OrderBy(static range => range.Pid)
                    .ThenBy(static range => range.FirstSeen)
                    .ThenBy(static range => range.Name, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }
}

public sealed class SessionPipelineException : Exception
{
    public SessionPipelineException(
        IngestStage stage,
        long bytesSafelyRead,
        long linesSafelyRead,
        Exception innerException)
        : base($"VisualCat ingest failed in {stage} after {bytesSafelyRead} bytes and {linesSafelyRead} lines.", innerException)
    {
        Stage = stage;
        BytesSafelyRead = bytesSafelyRead;
        LinesSafelyRead = linesSafelyRead;
    }

    public IngestStage Stage { get; }
    public long BytesSafelyRead { get; }
    public long LinesSafelyRead { get; }
    public bool PartialSessionMayBeUsable => BytesSafelyRead > 0;
}

internal sealed class MutableCounters
{
    public long Outcomes { get; private set; }
    public long ParsedEntries { get; private set; }
    public long TimedEntries { get; private set; }
    public long Meta { get; private set; }
    public long Unknown { get; private set; }
    public long Rejected { get; private set; }
    public long Continuations { get; private set; }
    public long Untimed { get; private set; }
    public long Blanks { get; private set; }
    public long BytesCommitted { get; private set; }
    public long Inferred { get; private set; }
    public long LowConfidence { get; private set; }
    public long OutOfOrder { get; private set; }
    public long EncodingFallbacks { get; private set; }
    public long LongLines { get; private set; }
    public long ChattyDrops { get; private set; }
    public long NextEntrySequence { get; set; }

    public void ObserveOutcome(ParseOutcome outcome)
    {
        Outcomes++;
        BytesCommitted += outcome.Source.Raw.Length;
        switch (outcome.Kind)
        {
            case ParseOutcomeKind.MetaRecord: Meta++; break;
            case ParseOutcomeKind.UnknownLine: Unknown++; break;
            case ParseOutcomeKind.RejectedCandidate:
                Rejected++;
                if (outcome.Reason?.Contains("safety limit", StringComparison.Ordinal) == true)
                {
                    LongLines++;
                }

                break;
            case ParseOutcomeKind.Continuation: Continuations++; break;
            case ParseOutcomeKind.UntimedEntry: Untimed++; break;
            case ParseOutcomeKind.IgnoredBlank: Blanks++; break;
        }
    }

    public void ObserveEntry(NormalizedEntry entry, long chattyDrops)
    {
        ParsedEntries++;
        if (entry.Timestamp is null)
        {
            // The explicit UntimedEntry parse outcome already owns this count.
        }
        else
        {
            TimedEntries++;
        }

        if (entry.Flags.HasFlag(EntryAttributes.InferredTimestamp)) Inferred++;
        if (entry.Flags.HasFlag(EntryAttributes.LowTimestampConfidence)) LowConfidence++;
        if (entry.Flags.HasFlag(EntryAttributes.OutOfOrder)) OutOfOrder++;
        if (entry.Flags.HasFlag(EntryAttributes.EncodingFallback)) EncodingFallbacks++;
        if (entry.Flags.HasFlag(EntryAttributes.LongLineOverflow)) LongLines++;
        ChattyDrops += chattyDrops;
    }

    public SessionCounters ToCounters(long bytes, long lines, long templates) =>
        new(bytes, lines, ParsedEntries, TimedEntries, Meta, Unknown, Rejected, Continuations, Untimed, Blanks, templates);

    public DefectCounters ToDefects(DefectCounters? sourceDefects = null, long templateOverflowEntries = 0)
    {
        var local = new DefectCounters(
            Unknown,
            Rejected,
            Continuations,
            Untimed,
            Inferred,
            LowConfidence,
            OutOfOrder,
            0,
            EncodingFallbacks,
            LongLines,
            ChattyDrops,
            TemplateOverflowEntries: templateOverflowEntries);
        if (sourceDefects is null)
        {
            return local;
        }

        return new DefectCounters(
            local.UnknownLines + sourceDefects.UnknownLines,
            local.RejectedCandidates + sourceDefects.RejectedCandidates,
            local.Continuations + sourceDefects.Continuations,
            local.UntimedEntries + sourceDefects.UntimedEntries,
            local.TimestampInferences + sourceDefects.TimestampInferences,
            local.LowConfidenceTimestamps + sourceDefects.LowConfidenceTimestamps,
            local.OutOfOrderEntries + sourceDefects.OutOfOrderEntries,
            local.LateSegmentEntries + sourceDefects.LateSegmentEntries,
            local.EncodingFallbacks + sourceDefects.EncodingFallbacks,
            local.LongLineOverflows + sourceDefects.LongLineOverflows,
            local.ChattyDeclaredDrops + sourceDefects.ChattyDeclaredDrops,
            local.ReconnectGaps + sourceDefects.ReconnectGaps,
            local.ReconnectDuplicates + sourceDefects.ReconnectDuplicates,
            local.SourceChanges + sourceDefects.SourceChanges,
            local.RetentionDeleted + sourceDefects.RetentionDeleted,
            local.ReconnectGapMilliseconds + sourceDefects.ReconnectGapMilliseconds,
            local.TemplateOverflowEntries + sourceDefects.TemplateOverflowEntries);
    }
}


/// <summary>
/// What the batch pre-pass computed for one outcome: its resolved instant and the
/// template the shard pool assigned it. Kept as a value type so a batch is one array
/// rather than one allocation per line (§19.3).
/// </summary>
internal readonly record struct PreparedEntry(ResolvedTimestamp Resolved, uint TemplateId);
