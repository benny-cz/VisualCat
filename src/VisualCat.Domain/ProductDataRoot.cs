namespace VisualCat.Domain;

/// <summary>
/// Where this product keeps its own data, resolved once and created on first use.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> verifies the directory on
/// Unix and returns an <em>empty string</em> when it does not exist, which is the ordinary state
/// of a fresh account with no <c>~/.local/share</c> and of any profile that sets
/// <c>XDG_DATA_HOME</c> without creating it. Every caller then failed three layers down, and
/// the closest thing to an explanation the product produced was
/// <c>Session lease storage is unavailable.</c> on the command line and
/// <c>Part of the session is missing. It may have been moved or deleted while it was open.</c>
/// in the shell — a sentence that sends the reader looking for a deleted capture when the fix
/// is <c>mkdir -p</c> (finding F-19).
/// </para>
/// <para>
/// Resolving with <see cref="Environment.SpecialFolderOption.DoNotVerify"/> and creating the
/// tree is what every XDG-aware application does, and the specification asks for exactly that.
/// A failure to create it names the path and the underlying cause, the way the unwritable case
/// already did.
/// </para>
/// </remarks>
/// <summary>
/// VisualCat has nowhere to keep its data. Distinct from an ordinary <see cref="IOException"/>
/// so both surfaces can say so plainly instead of describing it as a session that failed to
/// open, which is what sent readers looking for a deleted capture (finding F-19).
/// </summary>
public sealed class ProductDataRootException(string message, Exception? innerException = null)
    : IOException(message, innerException);

public static class ProductDataRoot
{
    private const string ProductFolder = "VisualCat";

    private static readonly Lock Gate = new();
    private static string? s_resolved;
    private static string? s_ignoredDataHome;

    /// <summary>
    /// The product's data root, created if it does not exist.
    /// </summary>
    /// <exception cref="ProductDataRootException">
    /// The directory does not exist and could not be created. The message names the path and the
    /// cause.
    /// </exception>
    public static string Path
    {
        get
        {
            lock (Gate)
            {
                return s_resolved ??= Resolve();
            }
        }
    }

    /// <summary>
    /// A subdirectory of the data root, created along with it.
    /// </summary>
    /// <param name="name">A directory directly under the data root, such as <c>Sessions</c>.</param>
    public static string Combine(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>
    /// A file path under the data root, created along with it.
    /// </summary>
    public static string Combine(string name, string leaf) => System.IO.Path.Combine(Path, name, leaf);

    /// <summary>
    /// Set when <c>XDG_DATA_HOME</c> was set to something this product could not honour, so a
    /// caller can say so rather than letting the data land silently somewhere else.
    /// </summary>
    /// <remarks>
    /// The XDG base-directory specification says a relative value is invalid and must be
    /// ignored, and .NET duly ignores it — which means a user who asked for one directory gets
    /// another with no word said. Ignoring it is correct; ignoring it silently is not.
    /// </remarks>
    public static string? IgnoredDataHome
    {
        get
        {
            lock (Gate)
            {
                _ = s_resolved ??= Resolve();
                return s_ignoredDataHome;
            }
        }
    }

    /// <summary>Forgets the resolved root, for tests that move the environment underneath it.</summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            s_resolved = null;
            s_ignoredDataHome = null;
        }
    }

    private static string Resolve()
    {
        // DoNotVerify returns the path whether or not it exists, which is the whole point:
        // the caller wants somewhere to put its data, not an answer about what is already there.
        var storage = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        if (!OperatingSystem.IsWindows())
        {
            var requested = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrEmpty(requested) && !System.IO.Path.IsPathRooted(requested))
            {
                s_ignoredDataHome = requested;
            }
        }

        if (string.IsNullOrEmpty(storage))
        {
            throw new ProductDataRootException(
                "VisualCat could not work out where to keep its data: neither HOME nor " +
                "XDG_DATA_HOME names a usable directory. Set HOME, or set XDG_DATA_HOME to an " +
                "absolute path.");
        }

        var root = System.IO.Path.Combine(storage, ProductFolder);
        try
        {
            Directory.CreateDirectory(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new ProductDataRootException(
                $"VisualCat could not create its data directory '{root}'. cause: {exception.Message}",
                exception);
        }

        return root;
    }
}
