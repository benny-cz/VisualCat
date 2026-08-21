using VisualCat.Domain.Entries;

namespace VisualCat.Core.Parsing;

/// <summary>
/// Reads the writing process's id straight off a raw <c>threadtime</c> record, for callers that
/// must know who wrote a line before there is anything to parse it into.
/// </summary>
/// <remarks>
/// <para>
/// The Android companion decides from the first records of a live capture whether it is seeing
/// the whole device or only this app's own log lines, and it has to decide that about the same
/// bytes <see cref="LogcatParser"/> will read later. Two readers of one format is how they
/// drift, so the format knowledge lives here and both of them use it.
/// </para>
/// <para>
/// They did drift. The companion took the pid to be the third whitespace-separated token, which
/// is where <c>-v threadtime</c> puts it and not where <c>-v threadtime,UTC</c> does: the zone
/// modifier inserts the offset as a token of its own, so the third token is <c>+0000</c>.
/// <c>int.TryParse</c> accepts a leading sign, so that read back as pid 0; no process on a
/// device has pid 0; and so every record on the phone — this app's own included — looked as
/// though somebody else had written it. An own-app-only capture announced itself as full-device
/// 0.4 seconds in, latched the verdict where nothing could revise it, and then sat delivering
/// nothing for as long as the reader left it running (fourth pass over the phone build).
/// </para>
/// <para>
/// So this reads a record by its shape rather than by counting to three, and declines to answer
/// unless the whole prefix is there and every part of it is what it should be. Declining is the
/// safe failure: a line this cannot read says nothing about what the capture can see, whereas a
/// line it misreads is a capture that lies about it.
/// </para>
/// </remarks>
public static class LogcatRecordOrigin
{
    /// <summary>
    /// How much of a record a caller has to keep to be sure <see cref="TryReadProcessId"/> can
    /// answer.
    /// </summary>
    /// <remarks>
    /// Everything read here is bounded: a date is at most ten characters, a time sixteen, a
    /// zone six, and a Linux pid or tid at most seven digits, which is fifty-one with the
    /// separators. The rest is slack, so that a buffered prefix always runs past the priority
    /// letter and into the tag — a record cut off exactly at a number would otherwise offer a
    /// truncated pid that reads perfectly well as a smaller one.
    /// </remarks>
    public const int MaximumPrefixLength = 96;

    /// <summary>
    /// Reads the pid from a <c>threadtime</c> record, with or without the <c>year</c>,
    /// <c>UTC</c>/<c>zone</c> and <c>usec</c> modifiers.
    /// </summary>
    /// <param name="record">
    /// A single record, without its line ending. A prefix of at least
    /// <see cref="MaximumPrefixLength"/> characters is enough; the message is never read.
    /// </param>
    /// <param name="processId">The writing process's id, or zero when the record cannot be read.</param>
    /// <returns>
    /// <see langword="true"/> only when the record carries a date, a time, an optional zone
    /// offset, a pid, a tid, a priority letter and the beginning of a tag, in that order.
    /// Anything else — a <c>--------- beginning of main</c> divider, a wrapped message, a
    /// format this does not know — is not an answer and says so.
    /// </returns>
    public static bool TryReadProcessId(ReadOnlySpan<char> record, out int processId)
    {
        processId = 0;
        var position = 0;

        if (!TryToken(record, ref position, out var date) || !IsDate(date) ||
            !TryToken(record, ref position, out var time) || !IsTime(time) ||
            !TryToken(record, ref position, out var token))
        {
            return false;
        }

        // `-v UTC` and `-v zone` write the offset as its own token between the time and the
        // pid: "2026-07-19 21:08:42.081222 +0000  1567  1626 I HfLooper: lux = 0.0".
        if (IsZoneOffset(token) && !TryToken(record, ref position, out token))
        {
            return false;
        }

        if (!TryIdentifier(token, out var pid) ||
            !TryToken(record, ref position, out var thread) || !TryIdentifier(thread, out _) ||
            !TryToken(record, ref position, out var priority) || !IsPriority(priority) ||
            !TryToken(record, ref position, out _))
        {
            return false;
        }

        processId = pid;
        return true;
    }

    /// <summary>A threadtime date: <c>MM-DD</c>, or <c>YYYY-MM-DD</c> under <c>-v year</c>.</summary>
    private static bool IsDate(ReadOnlySpan<char> token)
    {
        if (token.Length is not (5 or 10))
        {
            return false;
        }

        var separators = 0;
        foreach (var value in token)
        {
            if (value == '-')
            {
                separators++;
            }
            else if (!char.IsAsciiDigit(value))
            {
                return false;
            }
        }

        return separators == (token.Length == 5 ? 1 : 2);
    }

    /// <summary>A threadtime time: <c>HH:MM:SS.mmm</c>, or six fractional digits under <c>-v usec</c>.</summary>
    private static bool IsTime(ReadOnlySpan<char> token)
    {
        if (token.Length < 8)
        {
            return false;
        }

        var colons = 0;
        var points = 0;
        foreach (var value in token)
        {
            if (value == ':')
            {
                colons++;
            }
            else if (value == '.')
            {
                points++;
            }
            else if (!char.IsAsciiDigit(value))
            {
                return false;
            }
        }

        return colons == 2 && points <= 1;
    }

    /// <summary>A zone token such as <c>+0000</c>, <c>-0700</c> or <c>+02:00</c>.</summary>
    private static bool IsZoneOffset(ReadOnlySpan<char> token)
    {
        if (token.Length is not (5 or 6) || token[0] is not ('+' or '-'))
        {
            return false;
        }

        for (var index = 1; index < token.Length; index++)
        {
            if (token.Length == 6 && index == 3)
            {
                if (token[index] != ':')
                {
                    return false;
                }

                continue;
            }

            if (!char.IsAsciiDigit(token[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A pid or tid as logcat writes it.</summary>
    /// <remarks>
    /// Digits and nothing else, so no signed token can pass as a number here whatever
    /// <see cref="int.TryParse(ReadOnlySpan{char}, out int)"/> would have made of it. Never
    /// zero, because pid 0 is the kernel's scheduler idle task and does not write to the log:
    /// a record that appears to come from it has been misread, not received.
    /// </remarks>
    private static bool TryIdentifier(ReadOnlySpan<char> token, out int value)
    {
        value = 0;
        if (token.IsEmpty || token.Length > 7)
        {
            return false;
        }

        var parsed = 0;
        foreach (var digit in token)
        {
            if (!char.IsAsciiDigit(digit))
            {
                return false;
            }

            parsed = (parsed * 10) + (digit - '0');
        }

        value = parsed;
        return parsed > 0;
    }

    /// <summary>The single-letter priority column, read with the same table the parser uses.</summary>
    private static bool IsPriority(ReadOnlySpan<char> token) =>
        token.Length == 1 && LogLevels.Parse(token[0]) != LogLevel.Unknown;

    private static bool TryToken(ReadOnlySpan<char> line, ref int position, out ReadOnlySpan<char> token)
    {
        while (position < line.Length && line[position] == ' ')
        {
            position++;
        }

        if (position >= line.Length)
        {
            token = default;
            return false;
        }

        var start = position;
        while (position < line.Length && line[position] != ' ')
        {
            position++;
        }

        token = line[start..position];
        return true;
    }
}
