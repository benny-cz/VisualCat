using System.Globalization;

namespace VisualCat.Infrastructure.Diagnostics;

/// <summary>
/// Removes the files the .NET runtime leaves in the temporary directory on this process's behalf.
/// </summary>
/// <remarks>
/// <para>
/// The runtime's diagnostics IPC server creates one Unix socket per process,
/// <c>/tmp/dotnet-diagnostic-&lt;pid&gt;-&lt;key&gt;-socket</c>, and never unlinks it. After a
/// session of ordinary use there were fifteen of them, fourteen belonging to processes that had
/// long exited — the only things VisualCat leaves outside its declared data root, and the only
/// ones a user auditing what it left behind would find undocumented (finding F-17).
/// </para>
/// <para>
/// Both halves are needed. Unlinking this process's own socket on the way out handles the
/// ordinary case; sweeping sockets whose process is provably gone handles the case that made
/// them accumulate, which is a process that did not get to run its shutdown at all. A socket is
/// only removed once <c>/proc</c> says its process no longer exists and it has been untouched
/// for long enough that a just-started process cannot be mistaken for a dead one.
/// </para>
/// </remarks>
public static class RuntimeResidue
{
    private const string Prefix = "dotnet-diagnostic-";
    private const string Suffix = "-socket";

    /// <summary>How long a socket must have been untouched before it is a candidate.</summary>
    /// <remarks>
    /// A process that has only just started has already created its socket, and a PID that is
    /// not in <c>/proc</c> for an instant during process setup would otherwise look dead. Ten
    /// minutes is far beyond any such window and far below the lifetime of the accumulation.
    /// </remarks>
    private static readonly TimeSpan MinimumAge = TimeSpan.FromMinutes(10);

    /// <summary>How many entries one sweep looks at.</summary>
    private const int MaximumExamined = 2_000;

    /// <summary>
    /// Removes diagnostic sockets left by processes that have exited, including this process's
    /// own from an earlier run.
    /// </summary>
    /// <remarks>
    /// Every failure is swallowed. Tidying the temporary directory must never be a reason a
    /// session does not open.
    /// </remarks>
    public static void SweepExitedDiagnosticSockets()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            var temporary = Path.GetTempPath();
            var now = DateTime.UtcNow;
            var examined = 0;
            foreach (var path in Directory.EnumerateFiles(temporary, Prefix + "*" + Suffix))
            {
                if (++examined > MaximumExamined)
                {
                    return;
                }

                try
                {
                    if (ProcessOf(Path.GetFileName(path)) is not { } pid ||
                        now - File.GetLastWriteTimeUtc(path) < MinimumAge ||
                        Directory.Exists($"/proc/{pid.ToString(CultureInfo.InvariantCulture)}"))
                    {
                        continue;
                    }

                    File.Delete(path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Another user's, or gone already. Neither is this process's residue.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Unlinks this process's own diagnostic socket, for a shutdown that gets to run.</summary>
    public static void RemoveOwnDiagnosticSocket()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            var mine = Prefix + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "-";
            foreach (var path in Directory.EnumerateFiles(Path.GetTempPath(), mine + "*" + Suffix))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The process id a socket name carries, or null when the name is not one of ours.</summary>
    private static int? ProcessOf(string fileName)
    {
        if (!fileName.StartsWith(Prefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(Suffix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = fileName[Prefix.Length..^Suffix.Length];
        var separator = rest.IndexOf('-', StringComparison.Ordinal);
        var digits = separator < 0 ? rest : rest[..separator];
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid > 0
            ? pid
            : null;
    }
}
