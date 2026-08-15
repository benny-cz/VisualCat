using System.Globalization;
using System.Text;
using VisualCat.DemoLog;

// Usage: VisualCat.DemoLog <output> [lines] [seed]
//
// Writes a synthetic but realistic-looking Android logcat capture in `threadtime` format.
// The device, the app, the hosts, and the people are all invented; nothing here is derived
// from a real capture. The output is deterministic for a given line count and seed.
var output = Path.GetFullPath(args.Length > 0 ? args[0] : "demo-logcat.txt");
var totalLines = args.Length > 1 ? long.Parse(args[1], CultureInfo.InvariantCulture) : 1_000_000L;
var seed = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 20_260_812;

// A weekday morning on the demo device: boot, commute, a bad network patch, a memory
// squeeze, a crash storm with an ANR and a native abort, then a calm afternoon.
var start = new DateTime(2026, 8, 12, 6, 41, 7, 214, DateTimeKind.Unspecified);

Phase[] phases =
[
    new("boot", 95, 0.055,
        [("boot", 40), ("am", 14), ("pm", 12), ("sf", 8), ("power", 6), ("netsvc", 4), ("storage", 4), ("sysui", 6), ("wm", 6)],
        [(0.93, "boot-complete")]),

    new("settle", 240, 0.045,
        [("am", 10), ("pm", 10), ("power", 12), ("netsvc", 10), ("sf", 8), ("sysui", 8), ("wm", 6), ("sensors", 6), ("storage", 4), ("sync", 6), ("logd", 3), ("bt", 4), ("chatty", 3)]),

    // Idle is not quiet, it is intermittent: alarms and jobs wake the device, it logs for a
    // minute, and then nothing at all until the next maintenance window.
    new("idle-morning", 1500, 0.050,
        [("power", 20), ("netsvc", 12), ("sensors", 10), ("sync", 8), ("am", 6), ("pm", 6), ("logd", 4), ("bt", 6), ("netd", 6), ("chatty", 4), ("sysui", 6), ("storage", 3)],
        Bursts: 7, Duty: 0.40),

    new("commute-usage", 780, 0.140,
        [("app", 22), ("gfx", 16), ("http", 14), ("art", 8), ("wm", 8), ("sf", 8), ("am", 6), ("sysui", 5), ("sensors", 6), ("netsvc", 4), ("chatty", 3)],
        [(0.42, "strictmode"), (0.88, "strictmode")]),

    new("network-degradation", 520, 0.120,
        [("netfail", 34), ("http", 12), ("app", 12), ("netsvc", 10), ("netd", 8), ("gfx", 6), ("sync", 6), ("am", 4), ("chatty", 4), ("sysui", 4)],
        [(0.18, "http-storm"), (0.44, "http-storm"), (0.71, "http-storm"), (0.92, "http-storm")]),

    // Deep doze: three short maintenance windows separated by four-and-a-half minutes of
    // complete silence, which is the widest empty stretch in the capture.
    new("screen-off-doze", 900, 0.004,
        [("power", 30), ("sensors", 8), ("netsvc", 8), ("sync", 10), ("logd", 6), ("am", 4), ("bt", 4), ("storage", 4)],
        Bursts: 3, Duty: 0.09),

    new("memory-pressure", 540, 0.130,
        [("gcpressure", 30), ("art", 14), ("gfx", 12), ("app", 12), ("am", 8), ("http", 6), ("sf", 6), ("wm", 4), ("chatty", 4), ("logd", 4)],
        [(0.62, "oom-kill"), (0.88, "oom-kill")]),

    new("anr-and-crashes", 420, 0.135,
        [("gcpressure", 16), ("netfail", 12), ("app", 12), ("gfx", 12), ("am", 14), ("wm", 10), ("art", 10), ("http", 6), ("sysui", 4), ("chatty", 4)],
        [(0.12, "anr"), (0.34, "crash-cursor"), (0.46, "restart"), (0.63, "crash-npe"), (0.79, "tombstone"), (0.93, "watchdog")]),

    new("restart-recovery", 360, 0.080,
        [("am", 14), ("app", 18), ("gfx", 14), ("art", 10), ("http", 10), ("wm", 8), ("sf", 8), ("pm", 6), ("sysui", 6), ("sync", 6)],
        [(0.08, "restart")]),

    new("media-capture", 480, 0.105,
        [("camera", 22), ("media", 18), ("audio", 14), ("gfx", 12), ("sf", 10), ("app", 8), ("am", 4), ("wm", 4), ("storage", 4), ("sysui", 4)]),

    new("steady-afternoon", 900, 0.095,
        [("app", 20), ("http", 14), ("gfx", 12), ("sf", 10), ("am", 8), ("wm", 8), ("netsvc", 6), ("sensors", 6), ("sync", 6), ("art", 6), ("chatty", 4), ("sysui", 4)],
        Bursts: 4, Duty: 0.72),

    new("wind-down", 600, 0.041,
        [("power", 16), ("am", 10), ("sync", 12), ("netsvc", 10), ("sensors", 8), ("logd", 6), ("storage", 6), ("pm", 6), ("bt", 6), ("sysui", 6), ("chatty", 4), ("app", 8)],
        Bursts: 3, Duty: 0.50),
];

var channels = Corpus.Channels.ToDictionary(static channel => channel.Name, StringComparer.Ordinal);
var random = new Rng(seed);
var builder = new StringBuilder(512);
var shareTotal = phases.Sum(static phase => phase.Share);
var written = 0L;
var currentBuffer = "main";

var directory = Path.GetDirectoryName(output);
if (!string.IsNullOrEmpty(directory))
{
    Directory.CreateDirectory(directory);
}

await using var stream = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None, 4 << 20);
await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4 << 20) { NewLine = "\n" };

void Marker(string buffer)
{
    if (!string.Equals(currentBuffer, buffer, StringComparison.Ordinal))
    {
        currentBuffer = buffer;
        writer.WriteLine("--------- beginning of " + buffer);
    }
}

void Emit(DateTime instant, ProcessInfo process, int tid, char level, string tag, string message)
{
    builder.Clear();
    builder.Append(instant.ToString("MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
    builder.Append(' ').Append(process.Pid.ToString(CultureInfo.InvariantCulture).PadLeft(5));
    builder.Append(' ').Append(tid.ToString(CultureInfo.InvariantCulture).PadLeft(5));
    builder.Append(' ').Append(level).Append(' ').Append(tag).Append(": ").Append(message);
    writer.WriteLine(builder);
    written++;
}

// A real `logcat -b all` dump opens on the oldest buffer and switches once it has drained
// it, then only switches again when a crash buffer has something to say.
writer.WriteLine("--------- beginning of system");
currentBuffer = "system";

var cursor = start;
foreach (var phase in phases)
{
    var budget = Math.Max(1L, (long)Math.Round(totalLines * (phase.Share / shareTotal)));
    var phaseStart = cursor;
    var stepUs = phase.Seconds * 1_000_000d / budget;
    var mixTotal = phase.Mix.Sum(static entry => entry.Weight);
    var pending = new Queue<(double At, string SetPiece)>(
        (phase.SetPieces ?? []).OrderBy(static piece => piece.At));

    // A phase with a constant line rate draws a perfectly flat band in a heat map, which no
    // real device has ever produced. Two slow harmonics warp line index against wall clock so
    // density breathes within the phase while its start and end stay exactly where they were.
    var slowCycles = 1.6 + (random.NextDouble() * 2.4);
    var fastCycles = 4.5 + (random.NextDouble() * 4.0);
    var phaseOffset = random.NextDouble() * Math.Tau;
    double Wave(double fraction) => Math.Clamp(
        fraction
        + (0.30 * Math.Sin(Math.Tau * slowCycles * fraction) / (Math.Tau * slowCycles))
        + (0.14 * Math.Sin((Math.Tau * fastCycles * fraction) + phaseOffset) / (Math.Tau * fastCycles)),
        0,
        1);

    // Then pack the whole phase into Bursts activity windows separated by real silence: no
    // line carries a timestamp in the gaps. Each window's width and position are jittered
    // inside its slot, because evenly spaced identical bursts read as a metronome rather than
    // as a device waking up when it happens to have work.
    var windows = new (double Start, double End)[Math.Max(1, phase.Bursts)];
    var lineEdges = new double[windows.Length + 1];
    var weights = new double[windows.Length];
    var weightTotal = 0d;
    for (var burst = 0; burst < windows.Length; burst++)
    {
        var slotStart = burst / (double)windows.Length;
        var slotWidth = 1d / windows.Length;
        var width = Math.Min(
            slotWidth * 0.94,
            slotWidth * phase.Duty * (0.55 + (random.NextDouble() * 0.9)));
        var windowStart = slotStart + (random.NextDouble() * Math.Max(0, slotWidth - width));
        windows[burst] = (windowStart, windowStart + width);

        // Bursts also differ in how much they log, so one maintenance window can be busy and
        // the next almost idle.
        weights[burst] = 0.45 + (random.NextDouble() * 1.35);
        weightTotal += weights[burst];
    }

    for (var burst = 0; burst < windows.Length; burst++)
    {
        lineEdges[burst + 1] = lineEdges[burst] + (weights[burst] / weightTotal);
    }

    double Warp(double fraction)
    {
        if (phase.Bursts <= 1)
        {
            return Wave(fraction);
        }

        var index = windows.Length - 1;
        for (var burst = 0; burst < windows.Length; burst++)
        {
            if (fraction < lineEdges[burst + 1])
            {
                index = burst;
                break;
            }
        }

        var span = Math.Max(1e-9, lineEdges[index + 1] - lineEdges[index]);
        var window = windows[index];
        return window.Start + (Wave((fraction - lineEdges[index]) / span) * (window.End - window.Start));
    }


    for (var index = 0L; index < budget; index++)
    {
        var progress = index / (double)budget;
        cursor = phaseStart.AddTicks((long)(phase.Seconds * TimeSpan.TicksPerSecond * Warp(progress)));

        if (string.Equals(phase.Name, "boot", StringComparison.Ordinal) && progress >= 0.03)
        {
            Marker("main");
        }

        if (pending.Count > 0 && progress >= pending.Peek().At)
        {
            var piece = pending.Dequeue().SetPiece;
            var marker = SetPieces.BufferMarkerBefore(piece);
            if (marker is not null)
            {
                Marker(marker);
            }

            var incident = cursor;
            foreach (var line in SetPieces.Build(piece, random))
            {
                incident = incident.AddMilliseconds(line.DeltaMs);
                Emit(incident, line.Process, line.Tid, line.Level, line.Tag, line.Message);
            }

            if (marker is not null)
            {
                Marker("main");
            }

            continue;
        }

        // Jittered spacing rather than a fixed step: real logs arrive in ragged clusters,
        // and an even comb is instantly recognisable as generated in a timeline heat map.
        var jitter = 0.15 + (random.NextDouble() * 1.7);
        var instant = cursor.AddTicks((long)(stepUs * jitter * TimeSpan.TicksPerMicrosecond));

        // VisualCat is built to keep late arrivals in place instead of clamping them, so the
        // demo log has to actually contain some.
        if (random.NextDouble() < 0.0008)
        {
            instant = instant.AddMilliseconds(-random.Next(40, 2600));
        }

        var roll = random.NextDouble() * mixTotal;
        var channel = channels[phase.Mix[^1].Channel];
        foreach (var (name, weight) in phase.Mix)
        {
            roll -= weight;
            if (roll <= 0)
            {
                channel = channels[name];
                break;
            }
        }

        var template = channel.Pick(random);
        var tid = channel.Process.ThreadFor(template.Tag, random);
        builder.Clear();
        Slots.Expand(builder, template.Text, random, channel.Process);
        Emit(instant, channel.Process, tid, template.Level, template.Tag, builder.ToString());
    }

    cursor = phaseStart.AddSeconds(phase.Seconds);
}

await writer.FlushAsync().ConfigureAwait(false);
Console.WriteLine(output);
Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"{written} lines, {new FileInfo(output).Length / (1024d * 1024d):F1} MiB, {start:MM-dd HH:mm:ss} → {cursor:MM-dd HH:mm:ss}"));
return 0;
