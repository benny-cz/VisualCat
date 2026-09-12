using Microsoft.Win32.SafeHandles;

namespace VisualCat.Infrastructure.Files;

/// <summary>
/// Whether the file a follow has open is still the file at the path it is following.
/// </summary>
/// <remarks>
/// <para>
/// An ordinary <c>logrotate</c> without <c>copytruncate</c> — the default on most distributions
/// — moves the live file aside and creates a replacement. On Linux the moved-away inode stays
/// open, readable, and writable, so reads keep succeeding from a file that no longer has that
/// name while everything written to the live path is invisible. Comparing lengths cannot see it:
/// a replacement is usually <em>longer</em>, not shorter (finding F-31).
/// </para>
/// <para>
/// Two independent signals answer the question, so neither has to be trusted alone:
/// </para>
/// <list type="bullet">
///   <item>
///     On Linux, <c>/proc/self/fd/&lt;fd&gt;</c> is a symlink to the current name of the open
///     inode, and it follows the inode when the name moves. Comparing it with the followed path
///     answers exactly the question being asked, needs no interop, and works on every glibc this
///     product supports — including the 2.28 floor, where a <c>stat</c> P/Invoke would not bind.
///   </item>
///   <item>
///     Everywhere else, the creation time read through the open handle against the creation time
///     of whatever is at the path now. On ext4 that is the statx birth time at nanosecond
///     resolution, so a rotation is unmistakable; on a platform with no birth time both reads
///     agree and this signal simply says nothing, leaving the follow exactly as honest as it was.
///   </item>
/// </list>
/// </remarks>
internal sealed class FollowedFileIdentity
{
    private readonly SafeFileHandle _handle;
    private readonly DateTime _openedCreationUtc;

    private FollowedFileIdentity(SafeFileHandle handle, DateTime openedCreationUtc)
    {
        _handle = handle;
        _openedCreationUtc = openedCreationUtc;
    }

    /// <summary>Records what the just-opened stream is reading.</summary>
    public static FollowedFileIdentity Of(FileStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new FollowedFileIdentity(stream.SafeFileHandle, CreationOf(stream.SafeFileHandle));
    }

    /// <summary>
    /// Whether the open file is no longer the file at <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    /// Only a positive answer is acted on. Anything this cannot establish — a platform with no
    /// procfs and no birth time, or a handle the runtime will not answer for — returns false,
    /// so this can end a follow that has genuinely lost its source but can never end one that
    /// has not.
    /// </remarks>
    public bool HasMoved(string path)
    {
        if (CurrentNameOfOpenFile() is { } openName)
        {
            // A descriptor whose last name is gone reads "<path> (deleted)", which is not the
            // followed path either, so the same comparison covers the unlinked case.
            return !string.Equals(openName, FullPathOrSelf(path), StringComparison.Ordinal);
        }

        if (_openedCreationUtc == default)
        {
            return false;
        }

        try
        {
            var current = File.GetCreationTimeUtc(path);
            return current != default && current != _openedCreationUtc;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The name the open descriptor has right now, on platforms that can say.</summary>
    private string? CurrentNameOfOpenFile()
    {
        if (!OperatingSystem.IsLinux() || _handle.IsInvalid || _handle.IsClosed)
        {
            return null;
        }

        try
        {
            var descriptor = (int)_handle.DangerousGetHandle();
            return File.ResolveLinkTarget($"/proc/self/fd/{descriptor}", returnFinalTarget: false)?.FullName;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The followed path in the same spelling the descriptor reports, so a path given relatively
    /// or through a symlinked directory is not mistaken for a rotation.
    /// </summary>
    private static string FullPathOrSelf(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return path;
        }
    }

    private static DateTime CreationOf(SafeFileHandle handle)
    {
        try
        {
            return handle.IsInvalid || handle.IsClosed ? default : File.GetCreationTimeUtc(handle);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return default;
        }
    }
}
