using System.Buffers;
using System.Text.Json;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Templates;

namespace VisualCat.Core.Store;

/// <summary>
/// Append-only revisions of mined template definitions. A manifest commits only a byte
/// prefix, so a reader never observes a partial append or data written for a later live
/// snapshot.
/// </summary>
internal static class TemplateTable
{
    public const string FileName = "templates.jsonl";

    /// <summary>The compacted file a finalized session names instead: one record per id.</summary>
    public const string FinalFileName = "templates-final.jsonl";
    private const int MaximumDefinitionBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<TemplateDefinition> Load(
        string root,
        string fileName,
        long committedLength,
        long expectedCount)
    {
        // The name comes from an untrusted manifest and is joined onto the session root.
        // Only the two names this writer produces are accepted, so it can never escape.
        if (!string.Equals(fileName, FileName, StringComparison.Ordinal) &&
            !string.Equals(fileName, FinalFileName, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Session manifest names an unsupported template file: {fileName}");
        }

        if (committedLength < 0 ||
            expectedCount < 0 ||
            expectedCount > TemplateSettings.AbsoluteMaximumClusters)
        {
            throw new InvalidDataException("Session template table declares unreasonable dimensions.");
        }

        if (committedLength == 0)
        {
            return expectedCount == 0
                ? []
                : throw new InvalidDataException("Session template table is missing committed definitions.");
        }

        var path = Path.Combine(root, fileName);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Session template table was not found.", path);
        }

        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException("Session template table may not be a symbolic link or reparse point.");
        }

        if (info.Length < committedLength)
        {
            throw new InvalidDataException("Session template table ends before its committed boundary.");
        }

        using var file = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.SequentialScan);
        using var bounded = new BoundedReadStream(file, committedLength);
        var definitions = new Dictionary<uint, TemplateDefinition>(
            checked((int)Math.Min(expectedCount, 65_536)));
        var readBuffer = new byte[64 * 1024];
        var line = new ArrayBufferWriter<byte>(1024);
        while (bounded.Read(readBuffer) is var read && read > 0)
        {
            var remaining = readBuffer.AsSpan(0, read);
            while (!remaining.IsEmpty)
            {
                var newline = remaining.IndexOf((byte)'\n');
                var take = newline < 0 ? remaining.Length : newline;
                AppendBounded(line, remaining[..take]);
                remaining = remaining[(take + (newline < 0 ? 0 : 1))..];
                if (newline >= 0)
                {
                    ReadDefinition(line.WrittenSpan, definitions, expectedCount);
                    line.Clear();
                }
            }
        }

        if (line.WrittenCount > 0)
        {
            ReadDefinition(line.WrittenSpan, definitions, expectedCount);
        }

        if (definitions.Count != expectedCount)
        {
            throw new InvalidDataException(
                $"Session template table contains {definitions.Count:N0} definitions; the manifest declares {expectedCount:N0}.");
        }

        var ordered = definitions.Values.OrderBy(static definition => definition.TemplateId).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            if (ordered[index].TemplateId != (uint)index + 1)
            {
                throw new InvalidDataException("Session template table has a missing or non-contiguous template id.");
            }
        }

        return ordered;
    }

    private static void AppendBounded(ArrayBufferWriter<byte> line, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaximumDefinitionBytes - line.WrittenCount)
        {
            throw new InvalidDataException("Session template table contains an oversized definition.");
        }

        line.Write(bytes);
    }

    private static void ReadDefinition(
        ReadOnlySpan<byte> json,
        Dictionary<uint, TemplateDefinition> definitions,
        long expectedCount)
    {
        if (!json.IsEmpty && json[^1] == (byte)'\r')
        {
            json = json[..^1];
        }

        TemplateDefinition definition;
        try
        {
            definition = JsonSerializer.Deserialize<TemplateDefinition>(json, Options)
                ?? throw new InvalidDataException("Session template table contains an empty definition.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Session template table contains invalid JSON or UTF-8.", exception);
        }

        Validate(definition, expectedCount);
        definitions[definition.TemplateId] = definition;
    }

    private static void Validate(TemplateDefinition definition, long expectedCount)
    {
        if (definition.TemplateId == 0 ||
            definition.TemplateId > expectedCount ||
            definition.CanonicalText is null ||
            string.IsNullOrWhiteSpace(definition.Algorithm) ||
            string.IsNullOrWhiteSpace(definition.Version) ||
            definition.Tokens is null ||
            definition.Tokens.Any(static token => token is null) ||
            definition.MatchCount < 0 ||
            definition.RepresentativeEntryIds is null ||
            definition.ContentHash is null)
        {
            throw new InvalidDataException("Session template table contains an invalid definition.");
        }
    }

    private sealed class BoundedReadStream(Stream inner, long length) : Stream
    {
        private readonly long _length = length;
        private long _remaining = length;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _length - _remaining; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var allowed = (int)Math.Min(count, _remaining);
            if (allowed == 0) return 0;
            var read = inner.Read(buffer, offset, allowed);
            _remaining -= read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var allowed = (int)Math.Min(buffer.Length, _remaining);
            if (allowed == 0) return 0;
            var read = inner.Read(buffer[..allowed]);
            _remaining -= read;
            return read;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
