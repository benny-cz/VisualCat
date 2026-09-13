using System.Security.Cryptography;
using System.Text;
using VisualCat.Domain;

namespace VisualCat.Core.Store;

/// <summary>Identity and comparison rules shared by the store, shell and temporary-storage deletion.</summary>
/// <remarks>
/// One helper, used by deletion, by the checks in the interface, by the usage sets and by the
/// size index, so a path cannot mean one thing in one of them and something else in another.
/// Windows-insensitive, otherwise ordinal, which is the policy the rest of Infrastructure
/// already follows for the default filesystems this product supports.
/// </remarks>
public static class SessionPath
{
    public static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Absolute, with a trailing separator trimmed but a volume root preserved.</summary>
    public static string Canonical(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}

/// <summary>
/// Shared-use and exclusive-delete leases over one stored session, held by cooperating processes.
/// </summary>
/// <remarks>
/// <para>
/// Three lease files live outside the payload in stable per-user application storage. Every
/// new user passes through a shared <c>.intent</c> gate, readers share <c>.read</c>, and one
/// writer holds <c>.write</c>. Deletion takes intent exclusively, refuses existing writers and
/// foreign readers, and keeps all new users out while its own idle tabs close. The final
/// exclusive-use check also requires every local reader to have released its reference.
/// </para>
/// <para>
/// The operating system releases a handle when its process dies, so a killed process cannot
/// leave a capture permanently protected. The lease files themselves are never unlinked, and
/// their existence is not a lock: only an open handle is.
/// </para>
/// <para>
/// This protects cooperating processes on local, controlled storage. It says nothing about an
/// older build, an unrelated tool, hostile same-user mutation, or a network share whose locking
/// semantics differ; those are documented limits, not guarantees.
/// </para>
/// </remarks>
public static class SessionAccess
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, Entry> Entries = new(SessionPath.Comparer);

    /// <summary>Local work began or finished. Raised outside the lease lock; observers must
    /// marshal to their own dispatcher and coalesce updates. Idle reads do not raise it.</summary>
    public static event Action<string>? WorkChanged;

    /// <summary>Shared use: opening, reading, listing. Any number, in any process.</summary>
    public static IDisposable Read(string path) => Acquire(path, writer: false);

    /// <summary>A bounded operation using a capture, such as export or verification. Unlike
    /// an idle snapshot, it must finish before deletion is allowed to close any tabs.</summary>
    public static IDisposable ReadForWork(string path) => Acquire(path, writer: false, work: true);

    public static bool IsWorking(string path)
    {
        lock (Gate)
        {
            return Entries.TryGetValue(SessionPath.Canonical(path), out var entry) &&
                (entry.Writers > 0 || entry.WorkReaders > 0);
        }
    }

    /// <summary>Exclusive use: recording, importing, saving into, writing metadata.</summary>
    public static IDisposable Write(string path) => Acquire(path, writer: true);

    /// <summary>Whether this process is writing to the session, for protection decisions.</summary>
    public static bool IsWriting(string path)
    {
        lock (Gate)
        {
            return Entries.TryGetValue(SessionPath.Canonical(path), out var entry) && entry.Writers > 0;
        }
    }

    /// <summary>
    /// Takes deletion intent: refuses writers and foreign readers, blocks all new users, and
    /// allows existing readers in this process to release their references before deletion.
    /// </summary>
    public static DeletionReservation ReserveDeletion(string path)
    {
        path = SessionPath.Canonical(path);
        lock (Gate)
        {
            var entry = GetOrCreate(path);
            try
            {
                if (entry.Deleting || entry.Writers > 0 || entry.WorkReaders > 0)
                {
                    throw new SessionInUseException();
                }

                entry.TakeIntentLease();
                entry.TakeWriteLease();
                // The intent gate now blocks new users in every process. Temporarily release
                // this process's shared handle to detect foreign readers before closing tabs.
                // Keep exclusive OS ownership while our existing local readers drain.
                entry.TakeExclusiveLease();
                entry.Deleting = true;
                return new DeletionReservation(path);
            }
            catch
            {
                ReleaseUnused(path, entry);
                throw;
            }
        }
    }

    private static Usage Acquire(string path, bool writer, bool work = false)
    {
        path = SessionPath.Canonical(path);
        bool changed;
        lock (Gate)
        {
            var entry = GetOrCreate(path);
            try
            {
                if (entry.Deleting)
                {
                    throw new SessionInUseException();
                }

                // Every new reader participates, including when this process already owns a
                // shared handle. Otherwise a foreign deletion could not block new opens.
                using var intent = entry.EnterUse();
                entry.TakeReadLease();
                changed = (writer || work) && entry.Writers == 0 && entry.WorkReaders == 0;
                if (writer)
                {
                    entry.TakeWriteLease();
                    entry.Writers++;
                }

                entry.Users++;
                if (work) entry.WorkReaders++;
            }
            catch
            {
                ReleaseUnused(path, entry);
                throw;
            }
        }

        if (changed) NotifyWorkChanged(path);
        return new Usage(path, writer, work);
    }

    private static void NotifyWorkChanged(string path)
    {
        foreach (var observer in WorkChanged?.GetInvocationList() ?? [])
        {
            try { ((Action<string>)observer)(path); }
            // A display observer cannot interrupt storage work or leak its acquired lease.
            catch (Exception) { }
        }
    }

    private static Entry GetOrCreate(string path)
    {
        if (Entries.TryGetValue(path, out var existing))
        {
            return existing;
        }

        // TEMP can differ between a GUI app, CLI and test host. Ownership must use a stable
        // per-user location shared by all of them, independent of launch environment. The root
        // is created if it is not there — a fresh account has no ~/.local/share, and failing on
        // the lease directory reported a missing data root as unavailable lease storage, which
        // is a symptom three layers from its cause (F-19).
        var leases = ProductDataRoot.Combine("SessionAccess-v1");
        SessionFileModes.CreateOwnerOnlyDirectory(leases);
        if (File.GetAttributes(leases).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException(
                $"The session lease directory '{leases}' is a link. VisualCat will not follow it; " +
                "remove or replace it with a real directory.");
        }

        // Off the caller's thread: a first launch must not wait on a directory sweep, and the
        // sweep needs nothing from the lease table it would have to be serialized against.
        if (Interlocked.Exchange(ref s_swept, 1) == 0)
        {
            _ = Task.Run(() => SweepMarkersOfVanishedSessions(leases));
        }

        // Case-folded on the platform whose paths are, so two spellings of one path cannot
        // take two different leases over the same session.
        var key = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
        var stem = Path.Combine(leases, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))));
        var entry = new Entry(stem + ".read", stem + ".write", stem + IntentSuffix);

        // Which session this lease belongs to, so a sweep can tell a marker whose session is
        // gone from one whose session is merely idle. The file name is a hash and cannot say
        // (F-24). It lives beside the sessions themselves, inside the owner-only data root.
        TryRecordSession(stem + IntentSuffix, path);
        Entries.Add(path, entry);
        return entry;
    }

    /// <summary>How many marker files one sweep looks at, so a huge directory cannot stall a launch.</summary>
    private const int MaximumSweptMarkers = 5_000;

    /// <summary>The suffix of the marker that records which session a lease belongs to.</summary>
    private const string IntentSuffix = ".intent";

    /// <summary>How old an intent with no recorded session must be before it counts as residue.</summary>
    private static readonly TimeSpan UnattributableMarkerGrace = TimeSpan.FromHours(1);

    private static int s_swept;

    /// <summary>
    /// Removes the markers of sessions that no longer exist, once per process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing ever deleted them. Three zero-byte files per session accumulated for the life of
    /// the account — 127 of them in a few hours of testing — and a user auditing what VisualCat
    /// leaves behind found a growing directory with no explanation (finding F-24).
    /// </para>
    /// <para>
    /// The condition is deliberately narrow. A marker's existence is not the lock — only an open
    /// handle is — so deleting an unheld marker is harmless in principle, but unlinking a name
    /// another process is in the middle of opening refuses that process a lease it should have
    /// had, and "this capture is in use" is a much worse thing to be wrong about than a stray
    /// zero-byte file. So only markers naming a session directory that is gone are considered,
    /// and only while every one of the three can be held exclusively — which is never true of a
    /// lease anybody holds.
    /// </para>
    /// </remarks>
    internal static void SweepMarkersOfVanishedSessions(string leaseRoot)
    {
        try
        {
            var examined = 0;
            foreach (var intent in Directory.EnumerateFiles(leaseRoot, "*" + IntentSuffix))
            {
                if (++examined > MaximumSweptMarkers)
                {
                    return;
                }

                try
                {
                    var session = File.ReadAllText(intent).Trim();
                    if (session.Length == 0)
                    {
                        // An intent whose path never got written can never be attributed, so the
                        // vanished-session test below can never fire for it and it would outlive
                        // every sweep — the slow remainder of F-24, one marker pair per few
                        // hundred leases. Age is what makes removing it safe: a process between
                        // creating this file and writing to it is milliseconds old, so anything
                        // older than the grace is residue, and DeleteIfUnheld still refuses to
                        // touch a marker anybody holds.
                        if (DateTime.UtcNow - File.GetLastWriteTimeUtc(intent) < UnattributableMarkerGrace)
                        {
                            continue;
                        }
                    }
                    else if (Directory.Exists(session) || File.Exists(session))
                    {
                        continue;
                    }

                    var stem = intent[..^IntentSuffix.Length];
                    DeleteIfUnheld(stem + ".read");
                    DeleteIfUnheld(stem + ".write");
                    DeleteIfUnheld(intent);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Held, unreadable, or gone already. None of those is residue to remove.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Removes one marker, but only while holding it exclusively, so no process can be between
    /// opening that name and locking it.
    /// </summary>
    private static void DeleteIfUnheld(string marker)
    {
        if (!File.Exists(marker))
        {
            return;
        }

        try
        {
            // FileShare.Delete lets the unlink happen while the exclusive handle is still held,
            // which leaves no window in which another process could take the lease on a name
            // this one is about to remove.
            using var held = new FileStream(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete);
            File.Delete(marker);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Notes which session a marker belongs to, if it does not say so already.</summary>
    private static void TryRecordSession(string intentPath, string sessionPath)
    {
        try
        {
            if (File.Exists(intentPath) && new FileInfo(intentPath).Length > 0)
            {
                return;
            }

            // Shared with everything: this is a note beside the lock, never the lock itself, and
            // a writer that loses a race simply writes the same text.
            using var stream = new FileStream(
                intentPath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            // No byte-order mark: this is a path for a sweep to compare, not a document.
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n" };
            writer.Write(sessionPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A marker that cannot say which session it belongs to is simply never swept.
        }
    }

    private static void ReleaseUnused(string path, Entry entry)
    {
        if (entry.Deleting)
        {
            // Nothing is released while intent is held: the exclusive read lease is what the
            // rename commits under.
        }
        else if (entry.Users == 0)
        {
            entry.ReleaseReadLease();
        }
        else
        {
            // Deletion ended without removing the capture — a refused close, a changed
            // identity, Stop — and this process still has readers. The exclusive lease it took
            // must not outlive it: kept, it would go on refusing every other cooperating
            // process a capture that is merely open here.
            entry.DowngradeToShared();
        }

        if (entry.Writers == 0 && !entry.Deleting)
        {
            entry.ReleaseWriteLease();
            entry.ReleaseIntentLease();
        }

        if (entry.Users == 0 && entry.Writers == 0 && !entry.Deleting)
        {
            Entries.Remove(path);
        }
    }

    private sealed class Entry(string readPath, string writePath, string intentPath)
    {
        private FileStream? _shared;
        private FileStream? _exclusive;
        private FileStream? _writing;
        private FileStream? _intent;

        internal int Users { get; set; }

        internal int Writers { get; set; }

        internal int WorkReaders { get; set; }

        internal bool Deleting { get; set; }

        internal FileStream EnterUse() => Open(intentPath, FileAccess.Read, FileShare.Read);
        internal void TakeIntentLease() => _intent = Open(intentPath, FileAccess.ReadWrite, FileShare.None);
        internal void ReleaseIntentLease() { _intent?.Dispose(); _intent = null; }

        internal void TakeReadLease()
        {
            if (_shared is not null || _exclusive is not null)
            {
                return;
            }

            _shared = Open(readPath, FileAccess.Read, FileShare.Read);
        }

        internal void TakeWriteLease()
        {
            _writing ??= Open(writePath, FileAccess.ReadWrite, FileShare.None);
        }

        /// <summary>
        /// Takes the OS read lease exclusively while intent prevents new users. Local readers
        /// may still exist, and must drain before RequireExclusive permits a rename.
        /// </summary>
        internal void TakeExclusiveLease()
        {
            if (_exclusive is not null)
            {
                return;
            }

            var hadReaders = _shared is not null;
            _shared?.Dispose();
            _shared = null;
            try
            {
                _exclusive = Open(readPath, FileAccess.ReadWrite, FileShare.None);
            }
            catch
            {
                // Our snapshots still exist; restore their shared protection before releasing
                // intent. New opens cannot race this restoration while the gate is held.
                if (hadReaders) _shared = Open(readPath, FileAccess.Read, FileShare.Read);
                throw;
            }
        }

        /// <summary>
        /// Hands sole use back without dropping this process's readers. Losing the race to a
        /// cooperating deleter leaves no lease rather than a wrong one; the next user acquires
        /// it again, and a deleter that wins is removing the capture in any case.
        /// </summary>
        internal void DowngradeToShared()
        {
            if (_exclusive is null)
            {
                return;
            }

            _exclusive.Dispose();
            _exclusive = null;
            try
            {
                _shared = Open(readPath, FileAccess.Read, FileShare.Read);
            }
            catch (IOException)
            {
                _shared = null;
            }
        }

        internal void ReleaseReadLease()
        {
            _shared?.Dispose();
            _shared = null;
            _exclusive?.Dispose();
            _exclusive = null;
        }

        internal void ReleaseWriteLease()
        {
            _writing?.Dispose();
            _writing = null;
        }

        private static FileStream Open(string path, FileAccess access, FileShare share)
        {
            if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException("Session lease storage is unavailable.");
            }

            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, access, share);
            }
            catch (IOException exception)
            {
                throw new SessionInUseException(exception);
            }
        }
    }

    private sealed class Usage(string path, bool writer, bool work) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            bool changed;
            lock (Gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                var entry = Entries[path];
                entry.Users--;
                if (work) entry.WorkReaders--;
                if (writer)
                {
                    entry.Writers--;
                }

                changed = (writer || work) && entry.Writers == 0 && entry.WorkReaders == 0;
                ReleaseUnused(path, entry);
            }
            if (changed) NotifyWorkChanged(path);
        }
    }

    /// <summary>Deletion intent, and the upgrade to sole use that a removal commits under.</summary>
    public sealed class DeletionReservation : IDisposable
    {
        private bool _disposed;

        internal DeletionReservation(string path) => Path = path;

        public string Path { get; }

        /// <summary>
        /// Asserts that nothing is using the session, here or in any cooperating process, and
        /// keeps it that way until the reservation is released.
        /// </summary>
        public void RequireExclusive()
        {
            lock (Gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var entry = Entries[Path];
                if (entry.Users != 0)
                {
                    throw new SessionInUseException();
                }

                entry.TakeExclusiveLease();
            }
        }

        public void Dispose()
        {
            lock (Gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                var entry = Entries[Path];
                entry.Deleting = false;
                ReleaseUnused(Path, entry);
            }
        }
    }
}

/// <summary>Something else holds the session: a writer, a reader, or another process.</summary>
public sealed class SessionInUseException : IOException
{
    public SessionInUseException()
        : base("This capture is in use.")
    {
    }

    public SessionInUseException(Exception innerException)
        : base("This capture is in use.", innerException)
    {
    }
}
