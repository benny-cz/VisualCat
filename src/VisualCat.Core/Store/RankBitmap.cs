using System.Numerics;

namespace VisualCat.Core.Store;

public sealed class RankBitmap
{
    private const int WordsPerSuperblock = 8;
    private readonly ulong[] _words;
    private readonly int[] _prefix;

    public RankBitmap(int length, ulong[] words)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (words.Length != WordCountFor(length))
        {
            throw new ArgumentException("Bitmap word count does not match its declared length.", nameof(words));
        }

        Length = length;
        _words = words;
        if (length % 64 is { } remainder and > 0 && words.Length > 0)
        {
            _words[^1] &= (1UL << remainder) - 1;
        }

        _prefix = BuildPrefix(words);
    }

    public int Length { get; }
    public int Cardinality => Rank(Length);
    public ReadOnlySpan<ulong> Words => _words;

    public static RankBitmap Empty(int length) => new(length, new ulong[WordCountFor(length)]);

    public static RankBitmap Full(int length)
    {
        var words = new ulong[WordCountFor(length)];
        Array.Fill(words, ulong.MaxValue);
        return new RankBitmap(length, words);
    }

    public static RankBitmap FromPredicate(int length, Func<int, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var words = new ulong[WordCountFor(length)];
        for (var i = 0; i < length; i++)
        {
            if (predicate(i))
            {
                words[i >> 6] |= 1UL << (i & 63);
            }
        }

        return new RankBitmap(length, words);
    }

    public bool this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Length);

            return (_words[index >> 6] & (1UL << (index & 63))) != 0;
        }
    }

    public int Rank(int endExclusive)
    {
        if ((uint)endExclusive > (uint)Length)
        {
            throw new ArgumentOutOfRangeException(nameof(endExclusive));
        }

        var fullWords = endExclusive >> 6;
        var superblock = fullWords / WordsPerSuperblock;
        var count = _prefix[superblock];
        var firstWord = superblock * WordsPerSuperblock;
        for (var word = firstWord; word < fullWords; word++)
        {
            count += BitOperations.PopCount(_words[word]);
        }

        var remainder = endExclusive & 63;
        if (remainder > 0 && fullWords < _words.Length)
        {
            count += BitOperations.PopCount(_words[fullWords] & ((1UL << remainder) - 1));
        }

        return count;
    }

    public int CountInRange(int startInclusive, int endExclusive)
    {
        if ((uint)startInclusive > (uint)endExclusive || endExclusive > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(startInclusive));
        }

        return Rank(endExclusive) - Rank(startInclusive);
    }

    public RankBitmap And(RankBitmap other) => Combine(other, static (left, right) => left & right);
    public RankBitmap Or(RankBitmap other) => Combine(other, static (left, right) => left | right);
    public RankBitmap AndNot(RankBitmap other) => Combine(other, static (left, right) => left & ~right);

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan);
        using var writer = new BinaryWriter(stream);
        writer.Write("VCBM"u8);
        writer.Write((ushort)1);
        writer.Write(Length);
        writer.Write(_words.Length);
        foreach (var word in _words)
        {
            writer.Write(word);
        }
    }

    public static RankBitmap Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        using var reader = new BinaryReader(stream);
        if (!reader.ReadBytes(4).AsSpan().SequenceEqual("VCBM"u8))
        {
            throw new InvalidDataException($"Invalid rank bitmap header: {path}");
        }

        if (reader.ReadUInt16() != 1)
        {
            throw new InvalidDataException($"Unsupported rank bitmap version: {path}");
        }

        var length = reader.ReadInt32();
        var wordCount = reader.ReadInt32();
        if (length < 0 || wordCount != WordCountFor(length) || stream.Length != 14L + (wordCount * sizeof(ulong)))
        {
            throw new InvalidDataException($"Invalid rank bitmap dimensions: {path}");
        }

        var words = new ulong[wordCount];
        for (var i = 0; i < words.Length; i++)
        {
            words[i] = reader.ReadUInt64();
        }

        return new RankBitmap(length, words);
    }

    private RankBitmap Combine(RankBitmap other, Func<ulong, ulong, ulong> operation)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Length != other.Length)
        {
            throw new ArgumentException("Bitmap lengths differ.", nameof(other));
        }

        var result = new ulong[_words.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = operation(_words[i], other._words[i]);
        }

        return new RankBitmap(Length, result);
    }

    private static int[] BuildPrefix(ulong[] words)
    {
        var blocks = (words.Length + WordsPerSuperblock - 1) / WordsPerSuperblock;
        var prefix = new int[blocks + 1];
        var count = 0;
        for (var block = 0; block < blocks; block++)
        {
            prefix[block] = count;
            var end = Math.Min(words.Length, (block + 1) * WordsPerSuperblock);
            for (var word = block * WordsPerSuperblock; word < end; word++)
            {
                count += BitOperations.PopCount(words[word]);
            }
        }

        prefix[^1] = count;
        return prefix;
    }

    private static int WordCountFor(int length) => checked((length + 63) / 64);
}
