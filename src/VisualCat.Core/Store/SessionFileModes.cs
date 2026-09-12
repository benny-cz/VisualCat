namespace VisualCat.Core.Store;

/// <summary>
/// Applies owner-only POSIX modes to what a session is made of.
/// </summary>
/// <remarks>
/// <para>
/// A session is, by construction, somebody else's log. Honouring the account's <c>umask</c> is
/// correct POSIX behaviour and it is what the product did — so with Ubuntu's default
/// <c>umask 002</c>, a session saved to <c>/tmp</c> or a shared project directory was mode 775
/// with its <c>manifest.json</c>, its segments and a portable session's embedded <c>raw.log</c>
/// all 664. Any other local account could read the full log content (finding F-27). Inside
/// <c>$HOME</c> that cannot happen, but the protection there belongs to the desktop's <c>700</c>
/// on <c>~/.local</c>, not to VisualCat, and it disappears the moment a session is saved
/// anywhere else.
/// </para>
/// <para>
/// The directory mode is what carries the guarantee: without <c>x</c> on the session directory
/// no other user can reach anything inside it, whatever the files' own modes are. The explicit
/// <c>600</c> on raw evidence is defence in depth for the case where such a file is later moved
/// somewhere less careful.
/// </para>
/// <para>
/// Every call is a no-op on Windows, where the inherited ACL is the equivalent mechanism, and
/// every failure is swallowed: a filesystem that cannot express POSIX modes — FAT, NTFS through
/// a driver, some network mounts — must not stop a session being written.
/// </para>
/// </remarks>
public static class SessionFileModes
{
    private const UnixFileMode OwnerOnlyDirectory =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>Creates a directory that only its owner can enter.</summary>
    public static void CreateOwnerOnlyDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        // The mode overload applies it at creation, so there is no instant at which the
        // directory exists with a wider mode than it is meant to have.
        Directory.CreateDirectory(path, OwnerOnlyDirectory);
    }

    /// <summary>Narrows an existing directory to its owner.</summary>
    public static void MakeDirectoryOwnerOnly(string path) => Apply(path, OwnerOnlyDirectory);

    /// <summary>Narrows an existing file to its owner.</summary>
    public static void MakeFileOwnerOnly(string path) => Apply(path, OwnerOnlyFile);

    private static void Apply(string path, UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, mode);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // A filesystem that cannot express the mode is not a reason to refuse the write.
        }
    }
}
