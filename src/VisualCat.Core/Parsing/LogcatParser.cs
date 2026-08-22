using System.Buffers.Text;
using System.Globalization;
using System.Text;
using VisualCat.Domain.Entries;

namespace VisualCat.Core.Parsing;

public sealed class LogcatParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    // Enum.GetValues allocates a fresh array on every call, and this runs for every
    // line the primary format rejects (§19.3: no allocation in parse loops).
    private static readonly LogcatFormat[] FallbackFormats =
    [
        LogcatFormat.ThreadTime,
        LogcatFormat.Time,
        LogcatFormat.Brief,
        LogcatFormat.LongFormat,
        LogcatFormat.Epoch,
    ];

    public static int Probe(ReadOnlySpan<byte> bytes, LogcatFormat format)
    {
        var line = Decode(bytes, out _);
        return TryParse(line.AsSpan(), format, null, out var fields, out _) && fields is not null ? Score(fields) : 0;
    }

    /// <summary>
    /// Recognises the two lines logcat uses to say which buffer the records after it came from.
    /// </summary>
    /// <remarks>
    /// Only <c>beginning of</c> was recognised, and that one is printed once per buffer at the
    /// start of the stream. A merged <c>-b all</c> capture therefore latched whichever buffer
    /// happened to be announced last and stamped it on everything that followed: a four-minute
    /// device capture attributed 9,376 of its 11,646 records to <c>radio</c>, including
    /// <c>main</c>-only traffic that a per-buffer probe proved exists nowhere else
    /// (finding F-12). <c>logcat -D</c> additionally prints <c>--------- switch to &lt;buffer&gt;</c>
    /// every time the merged stream crosses from one buffer to another, which is the missing
    /// per-record signal; the sources ask for it, and this is the half that reads it.
    /// </remarks>
    internal static bool TryReadBufferDivider(ReadOnlySpan<byte> bytes, out ReadOnlySpan<byte> buffer)
    {
        if (bytes.StartsWith("--------- beginning of "u8))
        {
            buffer = bytes["--------- beginning of "u8.Length..];
            return true;
        }

        if (bytes.StartsWith("--------- switch to "u8))
        {
            buffer = bytes["--------- switch to "u8.Length..];
            return true;
        }

        buffer = default;
        return false;
    }

    public static ParseOutcome Parse(SourceLine source, LogcatFormat primaryFormat, string? activeBuffer = null)
    {
        var bytes = TrimLine(source.Bytes.Span);
        if (bytes.IsEmpty)
        {
            return ParseOutcome.Blank(source);
        }

        if (TryReadBufferDivider(bytes, out var divided))
        {
            return ParseOutcome.Meta(source, $"buffer:{Decode(divided, out _).Trim()}");
        }

        var text = Decode(bytes, out var encodingFallback);
        var attributes = encodingFallback ? EntryAttributes.EncodingFallback : EntryAttributes.None;
        string? rejection = null;

        if (primaryFormat != LogcatFormat.Unknown &&
            TryParse(text.AsSpan(), primaryFormat, activeBuffer, out var primary, out rejection) &&
            primary is not null)
        {
            // Copied only when there is something to merge. A clean UTF-8 line — which is
            // effectively every line — otherwise paid for a second ParsedFields whose only
            // difference from the first was an unchanged flags field.
            var fields = attributes == EntryAttributes.None
                ? primary
                : primary with { Attributes = primary.Attributes | attributes };
            return fields.Timestamp is null ? ParseOutcome.Untimed(source, fields) : ParseOutcome.Parsed(source, fields);
        }

        foreach (var fallback in FallbackFormats)
        {
            if (fallback == primaryFormat)
            {
                continue;
            }

            if (TryParse(text.AsSpan(), fallback, activeBuffer, out var fields, out _) && fields is not null)
            {
                if (attributes != EntryAttributes.None)
                {
                    fields = fields with { Attributes = fields.Attributes | attributes };
                }

                return fields.Timestamp is null ? ParseOutcome.Untimed(source, fields) : ParseOutcome.Parsed(source, fields);
            }
        }

        if (primaryFormat == LogcatFormat.LongFormat && text.Length > 0)
        {
            return new ParseOutcome(ParseOutcomeKind.Continuation, source, null, "long-format body");
        }

        return LooksLikeHeader(text.AsSpan())
            ? ParseOutcome.Rejected(source, rejection ?? "malformed logcat header")
            : ParseOutcome.Unknown(source, "no supported logcat header");
    }

    private static bool TryParse(
        ReadOnlySpan<char> line,
        LogcatFormat format,
        string? buffer,
        out ParsedFields? fields,
        out string? rejection)
    {
        fields = null;
        rejection = null;
        return format switch
        {
            LogcatFormat.ThreadTime => TryThreadTime(line, buffer, out fields, out rejection),
            LogcatFormat.Epoch => TryEpoch(line, buffer, out fields, out rejection),
            LogcatFormat.Time => TryTime(line, buffer, out fields, out rejection),
            LogcatFormat.Brief => TryBrief(line, buffer, out fields, out rejection),
            LogcatFormat.LongFormat => TryLong(line, buffer, out fields, out rejection),
            _ => false,
        };
    }

    private static bool TryThreadTime(
        ReadOnlySpan<char> line,
        string? buffer,
        out ParsedFields? fields,
        out string? rejection)
    {
        fields = null;
        rejection = null;
        var position = 0;
        if (!TryToken(line, ref position, out var date) ||
            !TryToken(line, ref position, out var time) ||
            !TryToken(line, ref position, out var pidToken))
        {
            return false;
        }

        // `-v UTC` and `-v zone` insert the offset as its own token between the time
        // and the PID: "2026-07-19 21:08:42.081222 +0000  1567  1626 I tag: message".
        TimeSpan? explicitOffset = null;
        if (TryZoneOffset(pidToken, out var parsedOffset))
        {
            explicitOffset = parsedOffset;
            if (!TryToken(line, ref position, out pidToken))
            {
                return false;
            }
        }

        if (!TryToken(line, ref position, out var tidToken) ||
            !TryToken(line, ref position, out var levelToken))
        {
            return false;
        }

        if (!TryCalendarTimestamp(date, time, explicitOffset, out var timestamp))
        {
            return false;
        }

        if (!TryPositiveInt(pidToken, out var pid) || !TryPositiveInt(tidToken, out var tid) || levelToken.Length != 1)
        {
            rejection = "invalid PID, TID, or level";
            return false;
        }

        SkipSpaces(line, ref position);
        var colon = FindTagSeparator(line, position);
        if (colon < 0)
        {
            rejection = "missing tag separator";
            return false;
        }

        var tag = line[position..colon].Trim().ToString();
        if (tag.Length == 0)
        {
            rejection = "empty tag";
            return false;
        }

        var messageStart = colon + 1;
        if (messageStart < line.Length && line[messageStart] == ' ')
        {
            messageStart++;
        }

        var message = line[messageStart..].ToString();
        var drops = ParseChattyDrops(tag, message);
        fields = new ParsedFields(
            timestamp,
            pid,
            tid,
            LogLevels.Parse(levelToken[0]),
            tag,
            message,
            LogcatFormat.ThreadTime,
            buffer,
            messageStart,
            line.Length - messageStart,
            drops,
            drops > 0 ? EntryAttributes.Chatty : EntryAttributes.None);
        return true;
    }

    private static bool TryEpoch(
        ReadOnlySpan<char> line,
        string? buffer,
        out ParsedFields? fields,
        out string? rejection)
    {
        fields = null;
        rejection = null;
        var position = 0;
        if (!TryToken(line, ref position, out var epoch) ||
            !TryToken(line, ref position, out var pidToken) ||
            !TryToken(line, ref position, out var tidToken) ||
            !TryToken(line, ref position, out var levelToken) ||
            !decimal.TryParse(epoch, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        if (seconds < -62_135_596_800m || seconds > 253_402_300_799m ||
            !TryPositiveInt(pidToken, out var pid) ||
            !TryPositiveInt(tidToken, out var tid) ||
            levelToken.Length != 1)
        {
            rejection = "invalid epoch, PID, TID, or level";
            return false;
        }

        SkipSpaces(line, ref position);
        var colon = FindTagSeparator(line, position);
        if (colon < 0)
        {
            rejection = "missing tag separator";
            return false;
        }

        var tag = line[position..colon].Trim().ToString();
        var messageStart = colon + 1;
        if (messageStart < line.Length && line[messageStart] == ' ')
        {
            messageStart++;
        }

        var epochUs = decimal.ToInt64(decimal.Truncate(seconds * 1_000_000m));
        var timestamp = new TimestampToken(epoch.ToString(), 1970, 1, 1, 0, 0, 0, 0, epochUs, true);
        var message = line[messageStart..].ToString();
        var drops = ParseChattyDrops(tag, message);
        fields = new ParsedFields(
            timestamp,
            pid,
            tid,
            LogLevels.Parse(levelToken[0]),
            tag,
            message,
            LogcatFormat.Epoch,
            buffer,
            messageStart,
            line.Length - messageStart,
            drops,
            drops > 0 ? EntryAttributes.Chatty : EntryAttributes.None);
        return true;
    }

    private static bool TryTime(
        ReadOnlySpan<char> line,
        string? buffer,
        out ParsedFields? fields,
        out string? rejection)
    {
        fields = null;
        rejection = null;
        var position = 0;
        if (!TryToken(line, ref position, out var date) || !TryToken(line, ref position, out var time))
        {
            return false;
        }

        if (!TryCalendarTimestamp(date, time, TryConsumeZoneOffset(line, ref position), out var timestamp))
        {
            return false;
        }

        SkipSpaces(line, ref position);
        return TryPriorityTagPidMessage(line, position, timestamp, LogcatFormat.Time, buffer, out fields, out rejection);
    }

    private static bool TryBrief(
        ReadOnlySpan<char> line,
        string? buffer,
        out ParsedFields? fields,
        out string? rejection) =>
        TryPriorityTagPidMessage(line, 0, null, LogcatFormat.Brief, buffer, out fields, out rejection);

    private static bool TryPriorityTagPidMessage(
        ReadOnlySpan<char> line,
        int start,
        TimestampToken? timestamp,
        LogcatFormat format,
        string? buffer,
        out ParsedFields? fields,
        out string? rejection)
    {
        fields = null;
        rejection = null;
        if (start + 2 >= line.Length || line[start + 1] != '/')
        {
            return false;
        }

        var open = line[start..].IndexOf('(');
        if (open < 0)
        {
            rejection = "missing PID opening parenthesis";
            return false;
        }

        open += start;
        var close = line[open..].IndexOf(')');
        if (close < 0)
        {
            rejection = "missing PID closing parenthesis";
            return false;
        }

        close += open;
        var colon = close + 1;
        while (colon < line.Length && char.IsWhiteSpace(line[colon]))
        {
            colon++;
        }

        if (colon >= line.Length || line[colon] != ':')
        {
            rejection = "missing message separator";
            return false;
        }

        if (!TryPositiveInt(line[(open + 1)..close].Trim(), out var pid))
        {
            rejection = "invalid PID";
            return false;
        }

        var tag = line[(start + 2)..open].Trim().ToString();
        if (tag.Length == 0)
        {
            rejection = "empty tag";
            return false;
        }

        var messageStart = colon + 1;
        if (messageStart < line.Length && line[messageStart] == ' ')
        {
            messageStart++;
        }

        var message = line[messageStart..].ToString();
        var drops = ParseChattyDrops(tag, message);
        fields = new ParsedFields(
            timestamp,
            pid,
            0,
            LogLevels.Parse(line[start]),
            tag,
            message,
            format,
            buffer,
            messageStart,
            line.Length - messageStart,
            drops,
            drops > 0 ? EntryAttributes.Chatty : EntryAttributes.None);
        return true;
    }

    private static bool TryLong(
        ReadOnlySpan<char> line,
        string? buffer,
        out ParsedFields? fields,
        out string? rejection)
    {
        fields = null;
        rejection = null;
        var trimmed = line.Trim();
        if (trimmed.Length < 5 || trimmed[0] != '[' || trimmed[^1] != ']')
        {
            return false;
        }

        trimmed = trimmed[1..^1].Trim();
        var position = 0;
        if (!TryToken(trimmed, ref position, out var date) ||
            !TryToken(trimmed, ref position, out var time))
        {
            rejection = "incomplete long-format header";
            return false;
        }

        var longOffset = TryConsumeZoneOffset(trimmed, ref position);
        if (!TryToken(trimmed, ref position, out var pidWithColon) ||
            !TryToken(trimmed, ref position, out var tidToken) ||
            !TryToken(trimmed, ref position, out var priorityTag))
        {
            rejection = "incomplete long-format header";
            return false;
        }

        if (!TryCalendarTimestamp(date, time, longOffset, out var timestamp) ||
            !pidWithColon.EndsWith(':') ||
            !TryPositiveInt(pidWithColon[..^1], out var pid) ||
            !TryPositiveInt(tidToken, out var tid))
        {
            rejection = "invalid long-format header";
            return false;
        }

        var slash = priorityTag.IndexOf('/');
        if (slash != 1 || priorityTag.Length < 3)
        {
            rejection = "invalid long-format priority/tag";
            return false;
        }

        fields = new ParsedFields(
            timestamp,
            pid,
            tid,
            LogLevels.Parse(priorityTag[0]),
            priorityTag[2..].ToString(),
            string.Empty,
            LogcatFormat.LongFormat,
            buffer,
            line.Length,
            0);
        return true;
    }

    /// <summary>
    /// Consumes the optional zone token that <c>-v UTC</c> and <c>-v zone</c> insert
    /// after the time, leaving <paramref name="position"/> untouched when absent.
    /// </summary>
    private static TimeSpan? TryConsumeZoneOffset(ReadOnlySpan<char> line, ref int position)
    {
        var probe = position;
        if (TryToken(line, ref probe, out var candidate) && TryZoneOffset(candidate, out var offset))
        {
            position = probe;
            return offset;
        }

        return null;
    }

    /// <summary>
    /// Parses a logcat zone token such as <c>+0000</c>, <c>-0700</c> or <c>+02:00</c>.
    /// </summary>
    private static bool TryZoneOffset(ReadOnlySpan<char> token, out TimeSpan offset)
    {
        offset = default;
        if (token.Length is not (5 or 6) || token[0] is not ('+' or '-'))
        {
            return false;
        }

        if (token.Length == 6 && token[3] != ':')
        {
            return false;
        }

        if (!TryTwoDigits(token.Slice(1, 2), out var hours) || !TryTwoDigits(token[^2..], out var minutes))
        {
            return false;
        }

        if (hours > 18 || minutes > 59)
        {
            return false;
        }

        offset = new TimeSpan(hours, minutes, 0);
        if (token[0] == '-')
        {
            offset = -offset;
        }

        return true;
    }

    private static bool TryCalendarTimestamp(
        ReadOnlySpan<char> date,
        ReadOnlySpan<char> time,
        TimeSpan? explicitOffset,
        out TimestampToken? token)
    {
        token = null;
        int? year;
        int month;
        int day;
        if (date.Length == 5 && date[2] == '-' &&
            TryTwoDigits(date[..2], out month) &&
            TryTwoDigits(date[3..], out day))
        {
            year = null;
        }
        else if (date.Length == 10 && date[4] == '-' && date[7] == '-' &&
                 int.TryParse(date[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var explicitYear) &&
                 TryTwoDigits(date[5..7], out month) &&
                 TryTwoDigits(date[8..], out day))
        {
            year = explicitYear;
        }
        else
        {
            return false;
        }

        if (time.Length < 8 || time[2] != ':' || time[5] != ':' ||
            !TryTwoDigits(time[..2], out var hour) ||
            !TryTwoDigits(time[3..5], out var minute) ||
            !TryTwoDigits(time[6..8], out var second))
        {
            return false;
        }

        var microsecond = 0;
        if (time.Length > 8)
        {
            if (time[8] != '.' || time.Length > 15 || time.Length == 9)
            {
                return false;
            }

            var fraction = time[9..];
            if (!int.TryParse(fraction, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                return false;
            }

            microsecond = parsed;
            for (var i = fraction.Length; i < 6; i++)
            {
                microsecond *= 10;
            }
        }

        if (month is < 1 or > 12 || day is < 1 or > 31 || hour > 23 || minute > 59 || second > 60)
        {
            return false;
        }

        // A leap second is preserved in OriginalText and folded to :59 for the instant;
        // the calendar has no representation for it and inventing one would move the
        // event by a whole second.
        token = new TimestampToken(
            explicitOffset is null
                ? $"{date.ToString()} {time.ToString()}"
                : $"{date.ToString()} {time.ToString()} {FormatOffset(explicitOffset.Value)}",
            year,
            month,
            day,
            hour,
            minute,
            Math.Min(second, 59),
            microsecond,
            null,
            explicitOffset == TimeSpan.Zero,
            explicitOffset);
        return true;
    }

    /// <summary>
    /// Finds the colon that ends the tag. Logcat writes the header as <c>"%-8s: %s"</c>
    /// and tags themselves legitimately contain colons — <c>binder:1854_2</c>,
    /// <c>AF::TrackHandle</c>, <c>WifiClientModeImpl[16509:wlan0]</c> — so the
    /// separator is the first colon followed by a space, not the first colon.
    /// </summary>
    private static int FindTagSeparator(ReadOnlySpan<char> line, int start)
    {
        var separator = line[start..].IndexOf(": ", StringComparison.Ordinal);
        if (separator >= 0)
        {
            return separator + start;
        }

        // An empty message whose trailing space was stripped leaves the colon last.
        if (line.Length > start && line[^1] == ':')
        {
            return line.Length - 1;
        }

        // Non-logcat producers may omit the space; a bare colon is the best remaining
        // evidence and matches the historical behaviour for such input.
        var bare = line[start..].IndexOf(':');
        return bare < 0 ? -1 : bare + start;
    }

    private static string FormatOffset(TimeSpan offset) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{(offset < TimeSpan.Zero ? '-' : '+')}{Math.Abs(offset.Hours):D2}{Math.Abs(offset.Minutes):D2}");

    private static int ParseChattyDrops(string tag, string message)
    {
        if (!tag.Equals("chatty", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var marker = message.IndexOf("identical ", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return 0;
        }

        marker += "identical ".Length;
        var end = marker;
        while (end < message.Length && char.IsAsciiDigit(message[end]))
        {
            end++;
        }

        return int.TryParse(message.AsSpan(marker, end - marker), NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            ? count
            : 0;
    }

    private static bool TryToken(ReadOnlySpan<char> line, ref int position, out ReadOnlySpan<char> token)
    {
        SkipSpaces(line, ref position);
        if (position >= line.Length)
        {
            token = default;
            return false;
        }

        var start = position;
        while (position < line.Length && !char.IsWhiteSpace(line[position]))
        {
            position++;
        }

        token = line[start..position];
        return true;
    }

    private static void SkipSpaces(ReadOnlySpan<char> line, ref int position)
    {
        while (position < line.Length && char.IsWhiteSpace(line[position]))
        {
            position++;
        }
    }

    private static bool TryPositiveInt(ReadOnlySpan<char> token, out int value) =>
        int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;

    private static bool TryTwoDigits(ReadOnlySpan<char> token, out int value)
    {
        value = 0;
        return token.Length == 2 &&
               char.IsAsciiDigit(token[0]) &&
               char.IsAsciiDigit(token[1]) &&
               (value = ((token[0] - '0') * 10) + token[1] - '0') >= 0;
    }

    private static bool LooksLikeHeader(ReadOnlySpan<char> line) =>
        line.Length > 8 &&
        (char.IsAsciiDigit(line[0]) || line[0] == '[') &&
        (line.IndexOf(':') >= 0 || line.IndexOf('/') >= 0);

    private static int Score(ParsedFields fields)
    {
        var score = 1;
        score += fields.Timestamp is null ? 0 : 2;
        score += fields.Pid >= 0 ? 1 : 0;
        score += fields.Tag.Length > 0 ? 1 : 0;
        score += fields.Message is not null ? 1 : 0;
        return score;
    }

    private static string Decode(ReadOnlySpan<byte> bytes, out bool fallback)
    {
        try
        {
            fallback = false;
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            fallback = true;
            return Encoding.UTF8.GetString(bytes);
        }
    }

    private static ReadOnlySpan<byte> TrimLine(ReadOnlySpan<byte> line)
    {
        if (!line.IsEmpty && line[^1] == (byte)'\n')
        {
            line = line[..^1];
        }

        if (!line.IsEmpty && line[^1] == (byte)'\r')
        {
            line = line[..^1];
        }

        return line;
    }
}
