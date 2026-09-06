using System.Security.Cryptography;
using System.Text;

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

    /// <summary>Shared use: opening, reading, listing. Any number, in any process.</summary>
    public static IDisposable Read(string path) => Acquire(path, writer: false);

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
                if (entry.Deleting || entry.Writers > 0)
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

    private static Usage Acquire(string path, bool writer)
    {
        path = SessionPath.Canonical(path);
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
                if (writer)
                {
                    entry.TakeWriteLease();
                    entry.Writers++;
                }

                entry.Users++;
                return new Usage(path, writer);
            }
            catch
            {
                ReleaseUnused(path, entry);
                throw;
            }
        }
    }

    private static Entry GetOrCreate(string path)
    {
        if (Entries.TryGetValue(path, out var existing))
        {
            return existing;
        }

        // TEMP can differ between a GUI app, CLI and test host. Ownership must use a stable
        // per-user location shared by all of them, independent of launch environment.
        var storage = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(storage)) throw new IOException("Session lease storage is unavailable.");
        var leases = Path.Combine(storage, "VisualCat", "SessionAccess-v1");
        Directory.CreateDirectory(leases);
        if (File.GetAttributes(leases).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException("Session lease storage is unavailable.");
        }

        // Case-folded on the platform whose paths are, so two spellings of one path cannot
        // take two different leases over the same session.
        var key = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
        var stem = Path.Combine(leases, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))));
        var entry = new Entry(stem + ".read", stem + ".write", stem + ".intent");
        Entries.Add(path, entry);
        return entry;
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

    private sealed class Usage(string path, bool writer) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            lock (Gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                var entry = Entries[path];
                entry.Users--;
                if (writer)
                {
                    entry.Writers--;
                }

                ReleaseUnused(path, entry);
            }
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
