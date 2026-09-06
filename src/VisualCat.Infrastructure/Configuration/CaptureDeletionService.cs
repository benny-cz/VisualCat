using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using VisualCat.Core.Store;

namespace VisualCat.Infrastructure.Configuration;

public enum CaptureDeleteOutcome
{
    Deleted, DeletedPendingReclaim, AlreadyMissing, Protected, Refused, Changed,
    Locked, Denied, IoFailure, Cancelled, NotAttempted, Unknown,
}

public sealed record CaptureDeleteTarget(string Path, string Identity, long? SizeEstimate);

public sealed record CaptureDeleteResult(CaptureDeleteTarget Target, CaptureDeleteOutcome Outcome, Exception? Error = null)
{
    public bool Removed => Outcome is CaptureDeleteOutcome.Deleted or CaptureDeleteOutcome.DeletedPendingReclaim or CaptureDeleteOutcome.AlreadyMissing;
    public bool Committed => Outcome is CaptureDeleteOutcome.Deleted or CaptureDeleteOutcome.DeletedPendingReclaim;
}

public sealed record CaptureInventory(
    IReadOnlyList<TemporarySessionInfo> Sessions, bool Available, int InspectionIssues,
    int PendingCleanup, int UnresolvedCleanup)
{
    public Exception? Error { get; init; }
    /// <summary>Identities whose owned payload remains. Null means this inventory cannot
    /// reconcile individual outcomes (for example an unavailable root).</summary>
    public IReadOnlySet<string>? PendingIdentities { get; init; }
}

/// <summary>
/// Explicit deletion of local, controlled temporary storage. A flushed ownership record
/// precedes the same-parent rename. Rename is the commit; reclaim failure never undoes it.
/// Reparse points are rejected before descending, including all existing root ancestors.
/// Cooperating processes use SessionAccess; unrelated hostile filesystem mutation is outside
/// this protocol. No free-space or power-loss durability guarantee is inferred from rename.
/// </summary>
public static class CaptureDeletionService
{
    private const string IdentityFile = ".capture-identity";
    private const string StageSuffix = ".vcat-deleting";
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 8 };
    private static readonly ConcurrentDictionary<string, Task> Workers = new(SessionPath.Comparer);
    private static readonly ConcurrentDictionary<string, long> LastPass = new(SessionPath.Comparer);
    private static readonly ConcurrentDictionary<string, bool> BirthTimeStable = new(SessionPath.Comparer);
    private static readonly ConcurrentDictionary<string, Stack<string>> ReclaimCursors = new(SessionPath.Comparer);
    private static readonly ConcurrentDictionary<string, int> CleanupOffsets = new(SessionPath.Comparer);
    private static readonly Lock WorkerGate = new();
    private const int StagingAttempts = 8;
    private const int ReclaimEntryBudget = 128;
    private const int ReclaimMillisecondBudget = 100;
    private static readonly TimeSpan CleanupDrainBudget = TimeSpan.FromSeconds(20);
    // Deterministic barriers/faults for commit and recovery tests; never configured by production.
    internal static Action<string>? TestPhase { get; set; }

    /// <summary>
    /// The synchronous compatibility route. Its exception contract predates typed outcomes and
    /// is what <see cref="TemporarySessionRetentionService.DeleteExactSession"/> still promises:
    /// an invalid target is a plain <see cref="IOException"/>, an absent one is a
    /// <see cref="DirectoryNotFoundException"/>, and a committed removal is never reported as a
    /// pre-commit failure.
    /// </summary>
    internal static void DeleteExact(string root, string path)
    {
        try
        {
            ValidateTarget(root, path);
            using var reservation = SessionAccess.ReserveDeletion(path);
            reservation.RequireExclusive();
            if (!DirectoryPresent(path))
            {
                throw new DirectoryNotFoundException("The capture is no longer in temporary storage.");
            }

            var target = new CaptureDeleteTarget(SessionPath.Canonical(path), Identify(path, create: true), null);
            var result = DeleteCore(root, target, reservation, null, CancellationToken.None);
            if (!result.Committed)
            {
                throw result.Error ?? new IOException("The capture could not be deleted.");
            }
        }
        catch (Exception error) when (error is CaptureRefusedException or CaptureChangedException)
        {
            // The typed subclasses are the async route's vocabulary. Callers of this one have
            // always caught IOException itself, so the boundary translates rather than leaks.
            throw new IOException(error.Message, error);
        }
    }

    public static Task<CaptureInventory> InventoryAsync(string cacheRoot, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            var root = SessionPath.Canonical(cacheRoot);
            try
            {
                ValidateRoot(root);
                if (!DirectoryPresent(root))
                {
                    if (Path.GetPathRoot(root) is { } volume && !DirectoryPresent(volume))
                        throw new DirectoryNotFoundException("Temporary storage is unavailable.");
                    return new CaptureInventory([], true, 0, 0, 0);
                }

                var listed = await TemporarySessionRetentionService.ScanAsync(root, cancellationToken).ConfigureAwait(false);
                var sessions = new List<TemporarySessionInfo>(listed.Count);
                var issues = Directory.EnumerateDirectories(root).Count(IsCapture) - listed.Count;
                foreach (var session in listed)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        // A shared lease, so listing never fights an open tab or another
                        // reader; only a capture being deleted right now refuses one.
                        using var usage = SessionAccess.Read(session.Path);
                        sessions.Add(session with { Identity = Identify(session.Path, create: true) });
                    }
                    catch (Exception error) when (Expected(error))
                    {
                        // Listed, but with no identity to freeze: the row says it cannot be
                        // deleted right now. That is not the same as a capture the scan could
                        // not read at all, which is what the inspection count means.
                        sessions.Add(session);
                    }
                }

                var (pending, unresolved, pendingIdentities) = CleanupInventory(root);
                _ = ScheduleCleanup(root);
                return new CaptureInventory(sessions, true, Math.Max(0, issues), pending, unresolved)
                { PendingIdentities = pendingIdentities };
            }
            catch (Exception error) when (Expected(error))
            {
                return new CaptureInventory([], false, 0, 0, 0) { Error = error };
            }
        }, cancellationToken);

    public static Task<CaptureDeleteTarget> PrepareAsync(string root, TemporarySessionInfo session, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateTarget(root, session.Path);
            using var usage = SessionAccess.Read(session.Path);
            var identity = Identify(session.Path, create: true);
            if (session.Identity is not null && session.Identity != identity)
            {
                throw new CaptureChangedException();
            }

            return new CaptureDeleteTarget(SessionPath.Canonical(session.Path), identity, session.SizeBytes >= 0 ? session.SizeBytes : null);
        }, cancellationToken);

    /// <summary>Validate before the shell closes any tabs. Execution repeats this under reservation.</summary>
    public static Task ValidateAsync(string root, CaptureDeleteTarget target, CancellationToken cancellationToken) =>
        Task.Run(() => ValidatePrepared(root, target, cancellationToken), cancellationToken);

    /// <summary>
    /// Removes one prepared capture under a reservation this caller already holds. Raises
    /// <paramref name="committed"/> once removal is committed and reclaim begins, so a caller
    /// can say what it is doing without Infrastructure knowing any product copy; it is raised
    /// on the pool.
    /// </summary>
    public static Task<CaptureDeleteResult> DeleteAsync(
        string root, CaptureDeleteTarget target, SessionAccess.DeletionReservation reservation,
        Action? committed = null, CancellationToken cancellationToken = default) =>
        // Scheduling must not suppress a result for a pre-cancelled request.
        Task.Run(() => DeleteCore(root, target, reservation, committed, cancellationToken));

    public static async Task<IReadOnlyList<CaptureDeleteResult>> DeleteAsync(
        string root, IReadOnlyList<CaptureDeleteTarget> targets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var results = new List<CaptureDeleteResult>();
        var distinct = new HashSet<string>(SessionPath.Comparer);
        foreach (var target in targets)
        {
            var key = target.Path;
            try { key = SessionPath.Canonical(key); }
            catch (Exception error) when (Expected(error)) { }
            if (!distinct.Add(key))
            {
                continue;
            }

            results.Add(await Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ValidateTarget(root, target.Path);
                    using var reservation = SessionAccess.ReserveDeletion(target.Path);
                    return DeleteCore(root, target, reservation, null, cancellationToken);
                }
                catch (Exception error)
                {
                    return new CaptureDeleteResult(target, Classify(error), error);
                }
            }).ConfigureAwait(false));
        }

        return results;
    }

    internal static CaptureDeleteResult DeleteCore(
        string root, CaptureDeleteTarget target, SessionAccess.DeletionReservation reservation,
        Action? committed, CancellationToken cancellationToken)
    {
        var renamed = false;
        string? recordPath = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateTarget(root, target.Path);
            if (!SessionPath.Comparer.Equals(SessionPath.Canonical(target.Path), reservation.Path))
            {
                throw new CaptureRefusedException();
            }

            reservation.RequireExclusive();
            if (!DirectoryPresent(target.Path))
            {
                return new CaptureDeleteResult(target, CaptureDeleteOutcome.AlreadyMissing);
            }

            ValidatePrepared(root, target, cancellationToken);
            var stage = PublishOwnership(root, target, out recordPath);
            var destination = Path.Combine(root, stage);
            TestPhase?.Invoke("published");
            ValidatePrepared(root, target, cancellationToken);
            reservation.RequireExclusive();
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(target.Path, destination);
            renamed = true;
            TemporarySessionRetentionService.ForgetSize(target.Path);
            TestPhase?.Invoke("committed");
            committed?.Invoke();
            var complete = ReclaimSlice(destination, cancellationToken);
            if (complete)
            {
                File.Delete(recordPath);
            }
            else
            {
                _ = ScheduleCleanup(root, force: true);
            }

            return new CaptureDeleteResult(target, complete ? CaptureDeleteOutcome.Deleted : CaptureDeleteOutcome.DeletedPendingReclaim);
        }
        catch (Exception error)
        {
            if (renamed)
            {
                _ = ScheduleCleanup(root, force: true);
                return new CaptureDeleteResult(target, CaptureDeleteOutcome.DeletedPendingReclaim, error);
            }

            if (recordPath is not null)
            {
                // A published record whose rename never committed is safe for recovery to
                // retire, and retiring it never authorises touching the original capture.
                // Cleanup of metadata must not obscure the actual file outcome, so it is
                // scheduled rather than awaited here.
                _ = ScheduleCleanup(root, force: true);
            }

            return new CaptureDeleteResult(target, Classify(error), error);
        }
    }

    public static CaptureDeleteOutcome Classify(Exception error) => error switch
    {
        OperationCanceledException => CaptureDeleteOutcome.Cancelled,
        SessionInUseException => CaptureDeleteOutcome.Protected,
        CaptureChangedException => CaptureDeleteOutcome.Changed,
        CaptureRefusedException or ArgumentException or NotSupportedException => CaptureDeleteOutcome.Refused,
        UnauthorizedAccessException => CaptureDeleteOutcome.Denied,
        IOException io when OperatingSystem.IsWindows() && (io.HResult & 0xffff) is 32 or 33 => CaptureDeleteOutcome.Locked,
        IOException => CaptureDeleteOutcome.IoFailure,
        _ => CaptureDeleteOutcome.Unknown,
    };

    /// <summary>
    /// Reserves a staging name and durably publishes its ownership record before any rename.
    /// </summary>
    /// <remarks>
    /// A collision takes a fresh name and never removes what occupies the old one: an
    /// unexpected occupant may be another operation's committed payload. Publication is
    /// write-then-rename with an explicit flush, and <see cref="FileMode.CreateNew"/> so it
    /// can never overwrite another operation's record. Atomic publication and recovery from
    /// process termination are not, on every filesystem, a guarantee against sudden power loss.
    /// </remarks>
    private static string PublishOwnership(string root, CaptureDeleteTarget target, out string recordPath)
    {
        for (var attempt = 1; ; attempt++)
        {
            var stage = "." + Guid.NewGuid().ToString("N") + StageSuffix;
            var destination = Path.Combine(root, stage);
            var candidate = destination + ".json";
            var temporary = candidate + ".tmp";
            if (Directory.Exists(destination) || File.Exists(candidate))
            {
                if (attempt >= StagingAttempts)
                {
                    throw new IOException("Temporary storage could not reserve a staging name.");
                }

                continue;
            }

            try
            {
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        JsonSerializer.Serialize(
                            stream,
                            new Ownership(1, stage, Path.GetFileName(target.Path), target.Identity, target.SizeEstimate),
                            JsonOptions);
                        stream.Flush(flushToDisk: true);
                    }

                    File.Move(temporary, candidate);
                }
                finally
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
            }
            catch (IOException) when (attempt < StagingAttempts && (File.Exists(candidate) || Directory.Exists(destination)))
            {
                continue;
            }

            recordPath = candidate;
            return stage;
        }
    }

    private static void ValidatePrepared(string root, CaptureDeleteTarget target, CancellationToken cancellationToken)
    {
        ValidateTarget(root, target.Path);
        if (!DirectoryPresent(target.Path))
        {
            return;
        }

        string identity;
        try { identity = Identify(target.Path, create: false); }
        catch (FileNotFoundException) { throw new CaptureChangedException(); }
        if (identity != target.Identity)
        {
            throw new CaptureChangedException();
        }

        ValidateTree(target.Path, cancellationToken);
    }

    internal static void ValidateTarget(string root, string path)
    {
        if (!Path.IsPathFullyQualified(root) || !Path.IsPathFullyQualified(path))
        {
            throw new CaptureRefusedException();
        }

        root = SessionPath.Canonical(root);
        path = SessionPath.Canonical(path);
        if (!SessionPath.Comparer.Equals(Path.GetDirectoryName(path), root) || !IsCapture(path))
        {
            throw new CaptureRefusedException();
        }

        ValidateRoot(root);
        if (!DirectoryPresent(root))
        {
            throw new DirectoryNotFoundException("Temporary storage is unavailable.");
        }
    }

    private static bool IsCapture(string path) => path.EndsWith(".vcat", SessionPath.Comparison);

    /// <summary>
    /// The storage root is the boundary: it must be a real directory rather than a link, and
    /// nothing above it is inspected.
    /// </summary>
    /// <remarks>
    /// This used to walk every ancestor to the volume and refuse any reparse point among them,
    /// which made the feature unusable wherever a platform's own layout puts one there. macOS
    /// reaches its standard temporary directory through <c>/var</c>, a symlink, so every root
    /// under it reported <em>Temporary storage is unavailable</em> and every capture refused;
    /// a Windows junction or a redirected profile does the same. Above the root is the reader's
    /// filesystem, not this product's: a link there is followed once by the operating system,
    /// consistently, for every call this operation makes. What the guarantee actually rests on
    /// is unchanged — the target must be a direct <c>.vcat</c> child of this root, and
    /// <see cref="ValidateTree"/> refuses a link anywhere inside it. Swapping an ancestor
    /// between validation and rename remains the documented hostile-mutation boundary, which
    /// walking it never closed either.
    /// </remarks>
    private static void ValidateRoot(string path)
    {
        if (OperatingSystem.IsWindows() && path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new CaptureRefusedException();
        }

        if (OperatingSystem.IsWindows() && Path.GetPathRoot(path) is { } drive &&
            new DriveInfo(drive).DriveType == DriveType.Network)
            throw new CaptureRefusedException();

        try
        {
            RequireDirectory(path);
        }
        catch (DirectoryNotFoundException) { }
        catch (FileNotFoundException) { }
    }

    private static FileAttributes Attributes(string path)
    {
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new CaptureRefusedException();
        }

        return attributes;
    }

    private static void RequireDirectory(string path)
    {
        if (!Attributes(path).HasFlag(FileAttributes.Directory))
        {
            throw new CaptureRefusedException();
        }
    }

    private static bool DirectoryPresent(string path)
    {
        try { RequireDirectory(path); return true; }
        catch (DirectoryNotFoundException) { return false; }
        catch (FileNotFoundException) { return false; }
    }

    private static void ValidateTree(string path, CancellationToken cancellationToken)
    {
        var stack = new Stack<string>();
        stack.Push(path);
        while (stack.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireDirectory(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Attributes(entry).HasFlag(FileAttributes.Directory))
                {
                    stack.Push(entry);
                }
            }
        }
    }

    private static string Identify(string path, bool create)
    {
        RequireDirectory(path);
        var identityPath = Path.Combine(path, IdentityFile);
        if (create && !File.Exists(identityPath))
        {
            var temporaryIdentity = identityPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporaryIdentity, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = System.Text.Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N"));
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporaryIdentity, identityPath);
            }
            catch (IOException) when (File.Exists(identityPath)) { }
            finally { if (File.Exists(temporaryIdentity)) File.Delete(temporaryIdentity); }
        }

        if (Attributes(identityPath).HasFlag(FileAttributes.Directory) || new FileInfo(identityPath).Length != 32)
        {
            throw new CaptureRefusedException();
        }

        var identity = File.ReadAllText(identityPath);
        if (!Guid.TryParseExact(identity, "N", out _))
        {
            throw new CaptureRefusedException();
        }

        // The random marker distinguishes ordinary directory replacement, including legacy
        // captures without a manifest ID. Birth time additionally detects a copy that kept the
        // marker — but only where the storage actually keeps one (see BirthTimeSurvivesWrites).
        var root = Path.GetDirectoryName(path);
        if (root is null || !BirthTimeSurvivesWrites(root))
        {
            return identity;
        }

        return identity + ":" + Directory.GetCreationTimeUtc(path).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Whether this storage reports a directory creation time that survives writes into it.
    /// </summary>
    /// <remarks>
    /// Identity must separate "the same capture" from "a different directory at the same path".
    /// The marker file covers replacement; birth time additionally covers a copy that kept the
    /// marker. On storage with no birth time, .NET derives creation time from the change time,
    /// so writing into a directory moves it — and an identity that moves on its own turns every
    /// ordinary refresh into "this capture changed". The property is therefore measured once per
    /// root rather than assumed from the operating system, and where it does not hold the marker
    /// alone is the identity and the copy case is a documented limit rather than a faked check.
    /// </remarks>
    private static bool BirthTimeSurvivesWrites(string root) => BirthTimeStable.GetOrAdd(
        SessionPath.Canonical(root),
        static key =>
        {
            var probe = Path.Combine(key, "." + Guid.NewGuid().ToString("N") + ".vcat-probe");
            try
            {
                Directory.CreateDirectory(probe);
                var before = Directory.GetCreationTimeUtc(probe);
                File.WriteAllBytes(Path.Combine(probe, "entry"), [0]);
                Directory.CreateDirectory(Path.Combine(probe, "child"));
                return before > DateTime.UnixEpoch && Directory.GetCreationTimeUtc(probe) == before;
            }
            catch (Exception error) when (Expected(error))
            {
                return false;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(probe))
                    {
                        Directory.Delete(probe, recursive: true);
                    }
                }
                catch (Exception error) when (Expected(error))
                {
                }
            }
        });

    /// <summary>
    /// One bounded slice of the staged tree's removal. Returns true only when the tree is gone.
    /// </summary>
    /// <remarks>
    /// The budget is entries and elapsed time, not top-level folders: a capture is one folder
    /// and can hold tens of thousands of segment files, so "one folder per slice" bounds nothing.
    /// A slice materialises a bounded batch of names and closes the enumerator before deleting
    /// any of them, because an open enumeration handle over a directory being emptied is
    /// precisely what must not be held across a yield. One OS call may still exceed the budget;
    /// the budget is checked between calls.
    /// </remarks>
    private static bool ReclaimSlice(string path, CancellationToken cancellationToken)
    {
        // Keep descent between slices. Restarting at the root on every yield would spend
        // the entire budget repeatedly descending a tree deeper than 128 entries.
        if (ReclaimCursors.Count >= 256 && !ReclaimCursors.ContainsKey(path) && ReclaimCursors.Keys.FirstOrDefault() is { } oldest)
            ReclaimCursors.TryRemove(oldest, out _);
        var cursor = ReclaimCursors.GetOrAdd(path, static root => new Stack<string>([root]));
        try
        {
            if (ReclaimCore(path, cursor, cancellationToken))
            {
                ReclaimCursors.TryRemove(path, out _);
                return true;
            }
            return false;
        }
        catch
        {
            ReclaimCursors.TryRemove(path, out _);
            throw;
        }
    }

    private static bool ReclaimCore(string path, Stack<string> stack, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var budget = ReclaimEntryBudget;
        var marker = Path.Combine(path, IdentityFile);
        // A resumed cursor must not descend through a link introduced between passes.
        if (stack.TryPeek(out var current))
        {
            for (var ancestor = current; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
            {
                RequireDirectory(ancestor);
                if (SessionPath.Comparer.Equals(ancestor, path)) break;
            }
        }
        while (stack.TryPeek(out var directory))
        {
            if (cancellationToken.IsCancellationRequested || budget <= 0 || clock.ElapsedMilliseconds >= ReclaimMillisecondBudget)
            {
                return false;
            }

            RequireDirectory(directory);
            var batch = new List<string>(Math.Min(budget, ReclaimEntryBudget));
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                // The identity marker is removed last, so a partially reclaimed tree can still
                // prove it is the tree this operation owns.
                if (SessionPath.Comparer.Equals(entry, marker))
                {
                    continue;
                }

                batch.Add(entry);
                if (batch.Count >= budget)
                {
                    break;
                }
            }

            if (batch.Count == 0)
            {
                if (SessionPath.Comparer.Equals(directory, path) && File.Exists(marker))
                {
                    _ = Attributes(marker);
                    File.Delete(marker);
                    budget--;
                }

                Directory.Delete(directory);
                budget--;
                stack.Pop();
                continue;
            }

            foreach (var entry in batch)
            {
                if (cancellationToken.IsCancellationRequested || clock.ElapsedMilliseconds >= ReclaimMillisecondBudget)
                {
                    return false;
                }

                budget--;
                if (Attributes(entry).HasFlag(FileAttributes.Directory))
                {
                    // Descend now; the rest of this directory's batch is re-read when its
                    // child is gone, which keeps one live enumerator's worth of state at a time.
                    stack.Push(entry);
                    break;
                }

                File.Delete(entry);
            }
        }

        return true;
    }

    public static async Task RetryCleanupAsync(string root, CancellationToken cancellationToken = default)
    {
        root = SessionPath.Canonical(root);
        if (Workers.TryGetValue(root, out var previous)) await previous.WaitAsync(cancellationToken).ConfigureAwait(false);
        await ScheduleCleanup(root, force: true).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Cold-start and foreground scans schedule recovery without waiting for it.</summary>
    internal static void RequestCleanup(string root) => _ = ScheduleCleanup(root);

    private static Task ScheduleCleanup(string root, bool force = false)
    {
        root = SessionPath.Canonical(root);
        var now = Environment.TickCount64;
        lock (WorkerGate)
        {
            if (Workers.TryGetValue(root, out var running) && !running.IsCompleted) return running;
            if (!force && LastPass.TryGetValue(root, out var last) && now - last < 30_000) return Task.CompletedTask;
            var task = Start();
            Workers[root] = task;
            return task;
        }
        // Slices until the owned trees are gone, yielding between them so a cold start's scan
        // is never behind the sweep. A tree too large for one drain keeps its ownership record
        // and resumes at the next inventory, foreground event or explicit retry; it is never
        // abandoned, and its pending status stays visible in the meantime.
        Task Start() => Task.Run(async () =>
        {
            var clock = Stopwatch.StartNew();
            var backoff = 0;
            while (true)
            {
                LastPass[root] = Environment.TickCount64;
                if (!CleanupPass(root, CancellationToken.None) || clock.Elapsed >= CleanupDrainBudget)
                {
                    return;
                }

                await Task.Delay(backoff, CancellationToken.None).ConfigureAwait(false);
                backoff = Math.Min(50, backoff + 5);
            }
        });
    }

    // Returns true only when an owned tree made progress and may benefit from another pass.
    private static bool CleanupPass(string root, CancellationToken cancellationToken)
    {
        var progress = false;
        try
        {
            ValidateRoot(root);
            RequireDirectory(root);
            using var rootReservation = SessionAccess.ReserveDeletion(Path.Combine(root, ".cleanup-worker"));
            rootReservation.RequireExclusive();
            var candidates = Directory.EnumerateFiles(root, "*" + StageSuffix + ".json").Order(StringComparer.Ordinal).ToArray();
            var offset = candidates.Length == 0 ? 0 : CleanupOffsets.GetOrAdd(root, 0) % candidates.Length;
            var records = candidates.Skip(offset).Concat(candidates.Take(offset)).Take(128).ToArray();
            CleanupOffsets[root] = offset + records.Length;
            progress = candidates.Length > records.Length;
            foreach (var recordPath in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var record = ReadOwnership(recordPath);
                    var source = Path.Combine(root, record.Source);

                    // Intent, and no more. Taking it proves no cooperating process is between
                    // publishing this record and renaming under it, which is the only thing
                    // recovery has to establish: it reclaims a detached payload and retires
                    // obsolete metadata, and it never renames the source. RequireExclusive is
                    // the gate a rename passes through, and it additionally refuses while any
                    // local reader holds the source — which every listing of the root does,
                    // one session at a time. Asked for here it made recovery lose a race it
                    // did not need to enter: on the desktop a record whose rename had failed
                    // survived pass after pass, because a concurrent inventory was reading the
                    // very capture it named.
                    using var reservation = SessionAccess.ReserveDeletion(source);
                    var stage = Path.Combine(root, record.Stage);
                    if (DirectoryPresent(stage))
                    {
                        ValidateOwnedStage(stage, record);
                        if (ReclaimSlice(stage, cancellationToken))
                        {
                            File.Delete(recordPath);
                        }
                        else
                        {
                            progress = true;
                        }

                    }
                    else
                    {
                        // Either publication preceded an uncommitted rename or reclaim finished.
                        // Neither case authorises touching the original capture.
                        File.Delete(recordPath);
                    }
                }
                catch (Exception error) when (Expected(error))
                {
                    // Fairness is kept in the cursor, never by writing to an untrusted record.
                }
            }
        }
        catch (Exception error) when (Expected(error)) { }
        return progress;
    }

    private static (int Pending, int Unresolved, IReadOnlySet<string> Identities) CleanupInventory(string root)
    {
        var pending = 0;
        var unresolved = 0;
        var identities = new HashSet<string>(StringComparer.Ordinal);

        // Materialised, so the enumeration handle is closed before any of the per-stage work.
        // Files and linked occupants must be reported too: a valid journal next to a file
        // at the payload path is a refused cleanup, never "storage cleanup finished".
        var stages = Directory.EnumerateFileSystemEntries(root, "*" + StageSuffix).ToHashSet(SessionPath.Comparer);
        foreach (var stage in stages)
        {
            try
            {
                var record = ReadOwnership(stage + ".json");
                ValidateOwnedStage(stage, record);
                pending++;
                identities.Add(record.Identity);
            }
            catch (Exception error) when (Expected(error)) { unresolved++; }
        }

        // Corrupt/partial records also remain discoverable when there is no payload folder.
        foreach (var metadata in Directory.EnumerateFileSystemEntries(root, "*" + StageSuffix + ".*"))
        {
            if (stages.Contains(metadata)) continue; // Windows wildcard matching may include the bare directory.
            if (!metadata.EndsWith(".json", StringComparison.Ordinal)) { unresolved++; continue; }
            if (stages.Contains(metadata[..^5])) continue;
            try { _ = ReadOwnership(metadata); }
            catch (Exception error) when (Expected(error)) { unresolved++; }
        }

        return (pending, unresolved, identities);
    }

    private static void ValidateOwnedStage(string stage, Ownership record)
    {
        RequireDirectory(stage);
        if (Path.GetFileName(stage) != record.Stage)
        {
            throw new CaptureRefusedException();
        }

        // Renaming preserves birth time on supported local storage. After marker removal,
        // only an entirely empty directory is allowed (the final two OS calls can be split).
        if (Directory.EnumerateFileSystemEntries(stage).Any() &&
            Identify(stage, create: false).Split(':')[0] != record.Identity.Split(':')[0])
        {
            throw new CaptureRefusedException();
        }
    }

    private static Ownership ReadOwnership(string path)
    {
        if (Attributes(path).HasFlag(FileAttributes.Directory) || new FileInfo(path).Length > 4096)
        {
            throw new CaptureRefusedException();
        }

        using var stream = File.OpenRead(path);
        var record = JsonSerializer.Deserialize<Ownership>(stream, JsonOptions) ?? throw new CaptureRefusedException();
        if (record.Version != 1 || record.Stage is null || record.Stage.Length != 1 + 32 + StageSuffix.Length ||
            record.Stage[0] != '.' || !record.Stage.EndsWith(StageSuffix, StringComparison.Ordinal) ||
            !record.Stage.AsSpan(1, 32).ToArray().All(c => char.IsAsciiHexDigit(c) && !char.IsAsciiLetterUpper(c)) ||
            Path.GetFileName(path) != record.Stage + ".json" ||
            string.IsNullOrEmpty(record.Source) || record.Source.Length > 255 ||
            record.Source.IndexOfAny(['/', '\\', ':']) >= 0 || !IsCapture(record.Source) ||
            string.IsNullOrEmpty(record.Identity) || record.Identity.Length > 64 || record.SizeEstimate is < 0)
        {
            throw new CaptureRefusedException();
        }

        var identity = record.Identity.Split(':');
        if (identity.Length > 2 || !Guid.TryParseExact(identity[0], "N", out _) ||
            (identity.Length == 2 && (!long.TryParse(identity[1], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var ticks) || ticks <= 0 || ticks > DateTime.MaxValue.Ticks)))
            throw new CaptureRefusedException();

        return record;
    }

    private static bool Expected(Exception error) => error is IOException or UnauthorizedAccessException or
        ArgumentException or NotSupportedException or JsonException or InvalidOperationException;

    private sealed record Ownership(int Version, string Stage, string Source, string Identity, long? SizeEstimate);
}

/// <summary>The capture at a confirmed path is no longer the capture that was confirmed.</summary>
public sealed class CaptureChangedException()
    : IOException("This capture changed after it was selected.");

/// <summary>Root, target, link or identity validation refuses to remove this capture.</summary>
public sealed class CaptureRefusedException()
    : IOException("This capture cannot be safely removed from temporary storage.");
