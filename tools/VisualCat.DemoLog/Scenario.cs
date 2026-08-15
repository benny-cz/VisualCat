using System.Globalization;
using System.Text;

namespace VisualCat.DemoLog;

/// <summary>
/// A process that owns a coherent PID and a stable set of named threads, so every line the
/// generator attributes to it keeps the PID/TID relationship a reader would expect from a
/// real device. Randomising PIDs per line is the single most obvious tell of a fake log.
/// </summary>
internal sealed record ProcessInfo(string Name, int Pid, (string Name, int Tid)[] Threads)
{
    public (string Name, int Tid) MainThread => Threads[0];

    public (string Name, int Tid) PickThread(Rng random) => Threads[random.Next(Threads.Length)];

    /// <summary>
    /// Resolves the thread a tag habitually logs from — HWUI from RenderThread, GC from
    /// HeapTaskDaemon — falling back to a random thread of this process. A reader who knows
    /// Android reads the TID column, and a rendering line on a Binder thread reads as fake.
    /// </summary>
    public int ThreadFor(string tag, Rng random)
    {
        var preferred = Corpus.PreferredThread(tag);
        if (preferred is not null)
        {
            foreach (var thread in Threads)
            {
                if (string.Equals(thread.Name, preferred, StringComparison.Ordinal))
                {
                    return thread.Tid;
                }
            }
        }

        return PickThread(random).Tid;
    }
}

/// <summary>
/// A single message shape: the tag that emits it, its severity, its text, and its relative
/// frequency inside its channel.
/// </summary>
internal sealed record Template(string Tag, char Level, string Text, int Weight = 10);

/// <summary>
/// A subsystem that logs as one process under a handful of tags. Phases mix channels rather
/// than individual templates so a phase reads as "what the device was busy with".
/// </summary>
internal sealed record Channel(string Name, ProcessInfo Process, Template[] Templates)
{
    private readonly int[] _cumulative = Build(Templates);

    private static int[] Build(Template[] templates)
    {
        var cumulative = new int[templates.Length];
        var running = 0;
        for (var index = 0; index < templates.Length; index++)
        {
            running += Math.Max(1, templates[index].Weight);
            cumulative[index] = running;
        }

        return cumulative;
    }

    public Template Pick(Rng random)
    {
        var roll = random.Next(_cumulative[^1]);
        var index = Array.BinarySearch(_cumulative, roll);
        if (index < 0)
        {
            index = ~index;
        }
        else
        {
            index++;
        }

        return Templates[Math.Min(index, Templates.Length - 1)];
    }
}

/// <summary>
/// A stretch of wall-clock time with its own activity mix and line density. Share is the
/// slice of the total line budget; density follows from Share divided by Seconds. Mix names
/// channels with relative weights, and SetPieces places scripted incidents at a fraction of
/// the phase.
/// <para>
/// Bursts and Duty carve the phase into activity windows separated by genuine silence: with
/// Bursts=3 and Duty=0.1 the phase's lines land in three short windows and nothing at all is
/// logged for the 90% of each slot in between. A device asleep between maintenance windows
/// writes no log lines, and a timeline with no empty stretches anywhere is the giveaway that
/// a capture was manufactured.
/// </para>
/// </summary>
internal sealed record Phase(
    string Name,
    double Seconds,
    double Share,
    (string Channel, double Weight)[] Mix,
    (double At, string SetPiece)[]? SetPieces = null,
    int Bursts = 1,
    double Duty = 1.0);

/// <summary>
/// A deterministic xorshift generator. <see cref="Random"/> would work, but pinning the
/// algorithm here keeps a regenerated demo log byte-identical across .NET versions, which is
/// the whole point of shipping the generator instead of the 130 MB file.
/// </summary>
internal sealed class Rng(int seed)
{
    private ulong _state = (ulong)seed * 6364136223846793005UL + 1442695040888963407UL;

    private ulong NextBits()
    {
        _state ^= _state << 13;
        _state ^= _state >> 7;
        _state ^= _state << 17;
        return _state;
    }

    public int Next(int exclusiveUpperBound) =>
        exclusiveUpperBound <= 1 ? 0 : (int)(NextBits() % (ulong)exclusiveUpperBound);

    public int Next(int inclusiveLowerBound, int exclusiveUpperBound) =>
        inclusiveLowerBound + Next(exclusiveUpperBound - inclusiveLowerBound);

    public double NextDouble() => (NextBits() >> 11) * (1.0 / 9007199254740992.0);

    public T Pick<T>(T[] values) => values[Next(values.Length)];
}

/// <summary>
/// Expands the compact slot markers used in <see cref="Template.Text"/>. Templates stay
/// readable as literal strings while every emitted line still carries plausible, varying
/// numbers, paths, and identifiers.
/// </summary>
internal static class Slots
{
    public static void Expand(StringBuilder builder, string text, Rng random, ProcessInfo process)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character != '#' || index + 1 >= text.Length)
            {
                builder.Append(character);
                continue;
            }

            index++;
            switch (text[index])
            {
                case 'd': Number(builder, random.Next(1, 1000)); break;
                case 'D': Number(builder, random.Next(1000, 999_999)); break;
                case 'n': Number(builder, random.Next(2, 64)); break;
                case 'm': Number(builder, random.Next(1, 320)); break;
                case 'M': Number(builder, random.Next(400, 9800)); break;
                case 'k': Number(builder, random.Next(24, 4096)); break;
                case 'b': Number(builder, random.Next(120, 65_500) * 1024); break;
                case 'x': Number(builder, random.Next(1, 100)); break;
                case 'h': builder.Append(CultureInfo.InvariantCulture, $"0x{random.Next(0x1000_0000, 0x7fff_ffff):x8}"); break;
                case 'H': builder.Append(CultureInfo.InvariantCulture, $"0x{random.Next(0x1000, 0xffff):x4}"); break;
                case 'u': builder.Append(CultureInfo.InvariantCulture, $"u0a{random.Next(120, 260)}"); break;
                case 'P': Number(builder, random.Next(1000, 24_000)); break;
                case 'p': builder.Append(random.Pick(Corpus.Packages)); break;
                case 'a': builder.Append(random.Pick(Corpus.Activities)); break;
                case 'c': builder.Append(random.Pick(Corpus.Components)); break;
                case 'f': builder.Append(random.Pick(Corpus.Files)); break;
                case 'w': builder.Append(random.Pick(Corpus.Wakelocks)); break;
                case 'e': builder.Append(random.Pick(Corpus.Exceptions)); break;
                case 'r': builder.Append(random.Pick(Corpus.Endpoints)); break;
                case 'i': builder.Append(CultureInfo.InvariantCulture, $"10.{random.Next(0, 40)}.{random.Next(0, 255)}.{random.Next(2, 250)}"); break;
                case 'v': builder.Append(random.Pick(Corpus.Ssids)); break;
                case 't': builder.Append(process.PickThread(random).Name); break;
                case 'g': builder.Append(random.Pick(Corpus.GcCauses)); break;
                case 's': builder.Append(random.Pick(Corpus.Stations)); break;
                case 'q': builder.Append(random.Pick(Corpus.Queues)); break;
                case 'z': builder.Append(CultureInfo.InvariantCulture, $"{random.Next(1, 99):00}.{random.Next(0, 9)}"); break;
                case 'j': Number(builder, random.Next(0, 10)); break;
                case 'y': Number(builder, random.Next(1, 40)); break;
                case 'A': Number(builder, random.Next(10_100, 10_260)); break;
                case 'S': builder.Append(random.Pick(Corpus.Services)); break;
                case 'B': builder.Append(random.Pick(Corpus.Databases)); break;
                case 'L': builder.Append(CultureInfo.InvariantCulture, $"51.{random.Next(41_000, 48_999)}"); break;
                case 'O': builder.Append(CultureInfo.InvariantCulture, $"-2.{random.Next(81_000, 97_999)}"); break;
                default: builder.Append('#').Append(text[index]); break;
            }
        }
    }

    private static void Number(StringBuilder builder, int value) =>
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
}
