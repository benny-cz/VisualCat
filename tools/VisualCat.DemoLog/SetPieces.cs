using System.Globalization;

namespace VisualCat.DemoLog;

/// <summary>One line of a scripted incident, already resolved to a concrete process and thread.</summary>
internal sealed record SetPieceLine(int DeltaMs, ProcessInfo Process, int Tid, char Level, string Tag, string Message);

/// <summary>
/// Scripted multi-line incidents. Random lines give a log its texture; these give it a story —
/// an ANR, a Java crash with a real-shaped stack trace, a native tombstone. They are what a
/// reader zooms into, so they are written out line by line instead of sampled from templates.
/// </summary>
internal static class SetPieces
{
    private static readonly ProcessInfo Sys = Corpus.SystemServer;
    private static readonly ProcessInfo App = Corpus.Transit;
    private static readonly ProcessInfo Restarted = Corpus.TransitRestarted;

    public static IReadOnlyList<SetPieceLine> Build(string name, Rng random) => name switch
    {
        "boot-complete" => BootComplete(),
        "strictmode" => StrictMode(random),
        "http-storm" => HttpStorm(random),
        "oom-kill" => OomKill(random),
        "anr" => Anr(random),
        "crash-cursor" => CrashCursor(),
        "crash-npe" => CrashNullPointer(),
        "tombstone" => Tombstone(),
        "watchdog" => Watchdog(random),
        "restart" => Restart(),
        _ => [],
    };

    /// <summary>Buffer markers a real `logcat -b all` dump interleaves around a crash.</summary>
    public static string? BufferMarkerBefore(string name) => name is "crash-cursor" or "crash-npe" or "tombstone" ? "crash" : null;

    private static SetPieceLine Sysline(int delta, char level, string tag, string message, int tid = 1601) =>
        new(delta, Sys, tid, level, tag, message);

    private static SetPieceLine Appline(int delta, char level, string tag, string message, int tid = 9431) =>
        new(delta, App, tid, level, tag, message);

    private static SetPieceLine[] BootComplete() =>
    [
        Sysline(0, 'I', "SystemServer", "Enabled StrictMode for system server main thread."),
        Sysline(14, 'I', "ActivityManager", "System now ready", 1567),
        Sysline(31, 'I', "SystemServiceManager", "Starting phase 550"),
        Sysline(96, 'I', "ActivityManager", "Force stopping com.northlight.transit appid=10231 user=0: finished booting", 1567),
        Sysline(120, 'I', "BootReceiver", "Copying /data/misc/boottrace to dropbox (tag SYSTEM_BOOT)"),
        Sysline(48, 'I', "ActivityManager", "Boot completed in 24318 ms", 1567),
        Sysline(37, 'I', "PowerManagerService", "Boot animation finished, releasing *boot* wake lock"),
        Sysline(60, 'D', "SystemUI", "Boot completed, registering keyguard listeners"),
    ];

    private static SetPieceLine[] StrictMode(Rng random)
    {
        var duration = random.Next(420, 2400);
        return
        [
            Appline(0, 'D', "StrictMode", "StrictMode policy violation; ~duration=" + duration.ToString(CultureInfo.InvariantCulture) + " ms: android.os.strictmode.DiskReadViolation"),
            Appline(1, 'D', "StrictMode", "\tat android.os.StrictMode$AndroidBlockGuardPolicy.onReadFromDisk(StrictMode.java:1608)"),
            Appline(0, 'D', "StrictMode", "\tat java.io.UnixFileSystem.checkAccess(UnixFileSystem.java:251)"),
            Appline(0, 'D', "StrictMode", "\tat com.northlight.transit.data.PrefsStore.readJourneyState(PrefsStore.kt:64)"),
            Appline(0, 'D', "StrictMode", "\tat com.northlight.transit.ui.JourneyActivity.onResume(JourneyActivity.kt:118)"),
            Appline(1, 'W', "StrictMode", "Disk read on main thread reported to dropbox, throttling further reports for 60s"),
        ];
    }

    private static SetPieceLine[] HttpStorm(Rng random)
    {
        var host = random.Pick(Corpus.Endpoints);
        var timeout = random.Next(9800, 30000);
        return
        [
            Appline(0, 'W', "OkHttp", "<-- HTTP FAILED: java.net.SocketTimeoutException: failed to connect to api.northlight-transit.example/203.0.113." + random.Next(2, 250).ToString(CultureInfo.InvariantCulture) + " (port 443) after " + timeout.ToString(CultureInfo.InvariantCulture) + "ms", 9577),
            Appline(3, 'W', "OkHttp", "Retrying " + host + " (attempt 2 of 4) after 800ms backoff", 9577),
            Appline(812, 'W', "OkHttp", "Retrying " + host + " (attempt 3 of 4) after 1600ms backoff", 9577),
            Appline(1604, 'E', "OkHttp", "Giving up on " + host + " after 4 attempts", 9577),
            Appline(2, 'E', "TransitApp", "Departure refresh failed: java.io.IOException: unexpected end of stream on api.northlight-transit.example", 9431),
            Appline(1, 'E', "TransitApp", "\tat okhttp3.internal.http1.Http1ExchangeCodec.readResponseHeaders(Http1ExchangeCodec.kt:203)"),
            Appline(0, 'E', "TransitApp", "\tat com.northlight.transit.net.DepartureApi.fetch(DepartureApi.kt:41)"),
            Appline(4, 'W', "TransitApp", "Serving cached departures for Northgate (age 187s), banner shown"),
            Sysline(9, 'D', "ConnectivityService", "reportNetworkConnectivity(102, false) by uid 10231", 1702),
        ];
    }

    private static SetPieceLine[] OomKill(Rng random)
    {
        var freed = random.Next(140_000, 380_000);
        return
        [
            Appline(0, 'W', "art", "Throwing OutOfMemoryError \"Failed to allocate a 4194320 byte allocation with 2097152 free bytes and 1993KB until OOM, target footprint 268435456, growth limit 268435456\"", 9659),
            Appline(2, 'E', "MapTiles", "Tile decode aborted: java.lang.OutOfMemoryError while decoding 14/8193/5451", 9659),
            Appline(1, 'E', "MapTiles", "\tat android.graphics.BitmapFactory.nativeDecodeByteArray(Native Method)"),
            Appline(0, 'E', "MapTiles", "\tat com.northlight.transit.map.TileDecoder.decode(TileDecoder.kt:88)"),
            Appline(6, 'W', "TransitApp", "onTrimMemory(TRIM_MEMORY_RUNNING_CRITICAL), dropping tile cache and 12 itineraries"),
            Sysline(21, 'I', "ActivityManager", "Killing 4471:com.android.providers.media.module/u0a92 (adj 906): lowmem " + freed.ToString(CultureInfo.InvariantCulture) + "KB", 1567),
            Sysline(4, 'I', "lowmemorykiller", "Kill 'com.northlight.transit.wear' (5218), uid 10232, adj 906 to free " + freed.ToString(CultureInfo.InvariantCulture) + "KB rss"),
            Sysline(30, 'W', "ActivityManager", "Process ProcessRecord{a83f1c 9431:com.northlight.transit/u0a231} failed to trim memory within 5000ms", 1567),
        ];
    }

    private static SetPieceLine[] Anr(Rng random)
    {
        var elapsed = random.Next(5200, 11000);
        return
        [
            Sysline(0, 'W', "InputDispatcher", "Application is not responding: Window{7c31a04 u0 com.northlight.transit/.ui.JourneyActivity}. It has been " + elapsed.ToString(CultureInfo.InvariantCulture) + "ms since event, 5001ms since wait started.", 1619),
            Sysline(2, 'I', "WindowManager", "Input event dispatching timed out sending to com.northlight.transit/.ui.JourneyActivity", 1619),
            Sysline(140, 'E', "ActivityManager", "ANR in com.northlight.transit (com.northlight.transit/.ui.JourneyActivity)", 1619),
            Sysline(0, 'E', "ActivityManager", "PID: 9431", 1619),
            Sysline(0, 'E', "ActivityManager", "Reason: Input dispatching timed out (Waiting to send non-key event because the touched window has not finished processing certain input events that were delivered to it over 500.0ms ago.)", 1619),
            Sysline(0, 'E', "ActivityManager", "Parent: com.northlight.transit/.ui.JourneyActivity", 1619),
            Sysline(0, 'E', "ActivityManager", "Frozen: false", 1619),
            Sysline(0, 'E', "ActivityManager", "Load: 14.2 / 9.71 / 6.03", 1619),
            Sysline(0, 'E', "ActivityManager", "----- Output from /proc/pressure/memory -----", 1619),
            Sysline(0, 'E', "ActivityManager", "some avg10=41.22 avg60=28.90 avg300=11.44 total=88213004", 1619),
            Sysline(0, 'E', "ActivityManager", "CPU usage from 0ms to " + elapsed.ToString(CultureInfo.InvariantCulture) + "ms later:", 1619),
            Sysline(0, 'E', "ActivityManager", "  87% 9431/com.northlight.transit: 66% user + 21% kernel / faults: 21883 minor 41 major", 1619),
            Sysline(0, 'E', "ActivityManager", "  19% 1567/system_server: 11% user + 7.8% kernel / faults: 3402 minor", 1619),
            Sysline(0, 'E', "ActivityManager", "  6.1% 812/surfaceflinger: 3.9% user + 2.2% kernel / faults: 118 minor", 1619),
            Sysline(0, 'E', "ActivityManager", "  0.4% 726/logd: 0.1% user + 0.3% kernel", 1619),
            Sysline(18, 'I', "ActivityManager", "Dumping to /data/anr/anr_2026-08-12-09-47-31-118", 1601),
            Appline(64, 'I', "Process", "Sending signal. PID: 9431 SIG: 3", 9431),
            Appline(311, 'I', "art", "Thread[6,tid=9448,WaitingInMainSignalCatcherLoop,Thread*=0xb400007a1c0e2800,peer=0x12c40248,\"Signal Catcher\"]: reacting to signal 3", 9448),
            Appline(402, 'I', "art", "Wrote stack traces to tombstoned", 9448),
            Sysline(96, 'W', "ActivityManager", "Killing 9431:com.northlight.transit/u0a231 (adj 0): user request after error: Application isn't responding", 1601),
        ];
    }

    private static SetPieceLine[] CrashCursor() =>
    [
        Appline(0, 'E', "AndroidRuntime", "FATAL EXCEPTION: main"),
        Appline(0, 'E', "AndroidRuntime", "Process: com.northlight.transit, PID: 9431"),
        Appline(0, 'E', "AndroidRuntime", "java.lang.IllegalStateException: attempt to re-open an already-closed object: SQLiteCursor for departures"),
        Appline(0, 'E', "AndroidRuntime", "\tat android.database.sqlite.SQLiteClosable.acquireReference(SQLiteClosable.java:55)"),
        Appline(0, 'E', "AndroidRuntime", "\tat android.database.sqlite.SQLiteCursor.getCount(SQLiteCursor.java:130)"),
        Appline(0, 'E', "AndroidRuntime", "\tat com.northlight.transit.data.DepartureCursorAdapter.getItemCount(DepartureCursorAdapter.kt:57)"),
        Appline(0, 'E', "AndroidRuntime", "\tat androidx.recyclerview.widget.RecyclerView$Adapter.notifyDataSetChanged(RecyclerView.java:7412)"),
        Appline(0, 'E', "AndroidRuntime", "\tat com.northlight.transit.ui.DepartureBoardActivity$observeFeed$1.onChanged(DepartureBoardActivity.kt:184)"),
        Appline(0, 'E', "AndroidRuntime", "\tat androidx.lifecycle.LiveData.considerNotify(LiveData.java:133)"),
        Appline(0, 'E', "AndroidRuntime", "\tat androidx.lifecycle.LiveData.dispatchingValue(LiveData.java:151)"),
        Appline(0, 'E', "AndroidRuntime", "\tat android.os.Handler.handleCallback(Handler.java:958)"),
        Appline(0, 'E', "AndroidRuntime", "\tat android.os.Looper.loopOnce(Looper.java:205)"),
        Appline(0, 'E', "AndroidRuntime", "\tat android.app.ActivityThread.main(ActivityThread.java:8177)"),
        Appline(0, 'E', "AndroidRuntime", "\tat com.android.internal.os.RuntimeInit$MethodAndArgsCaller.run(RuntimeInit.java:552)"),
        Appline(0, 'E', "AndroidRuntime", "\tat com.android.internal.os.ZygoteInit.main(ZygoteInit.java:971)"),
        Sysline(24, 'W', "ActivityTaskManager", "Force finishing activity com.northlight.transit/.ui.DepartureBoardActivity", 1601),
        Sysline(38, 'I', "DropBoxManagerService", "add tag=data_app_crash isTagEnabled=true flags=0x2", 1638),
        Sysline(112, 'I', "ActivityManager", "Process com.northlight.transit (pid 9431) has died: fg  TOP ", 1567),
        Sysline(9, 'I', "WindowManager", "WIN DEATH: Window{7c31a04 u0 com.northlight.transit/.ui.DepartureBoardActivity}", 1601),
    ];

    private static SetPieceLine[] CrashNullPointer() =>
    [
        new(0, Restarted, 9614, 'E', "AndroidRuntime", "FATAL EXCEPTION: pool-4-thread-2"),
        new(0, Restarted, 9614, 'E', "AndroidRuntime", "Process: com.northlight.transit, PID: 9812"),
        new(0, Restarted, 9614, 'E', "AndroidRuntime", "java.lang.NullPointerException: Attempt to invoke virtual method 'java.lang.String com.northlight.transit.model.Stop.getCode()' on a null object reference"),
        new(0, Restarted, 9614, 'E', "AndroidRuntime", "\tat com.northlight.transit.route.ItineraryBuilder.leg(ItineraryBuilder.kt:96)"),
        new(0, Restarted, 9614, 'E', "AndroidRuntime", "\tat com.northlight.transit.route.ItineraryBuilder.build(ItineraryBuilder.kt:41)"),
        new(0, Restarted, 9614, 'E', "AndroidRuntime", "\tat com.northlight.transit.route.RoutePlanner.plan(RoutePlanner.kt:73)"),
        new(0, Restarted, 9614, 'E', "AndroidRuntime", "\tat com.northlight.transit.route.RoutePlanner$$ExternalSyntheticLambda3.run(Unknown Source:6)"),
        new(0, Restarted, 9614, 'E', "AndroidRuntime", "\tat java.util.concurrent.ThreadPoolExecutor.runWorker(ThreadPoolExecutor.java:1145)"),
        new(0, Restarted, 9614, 'E', "AndroidRuntime", "\tat java.util.concurrent.ThreadPoolExecutor$Worker.run(ThreadPoolExecutor.java:644)"),
        new(0, Restarted, 9614, 'E', "AndroidRuntime", "\tat java.lang.Thread.run(Thread.java:1012)"),
        new(0, Restarted, 9614, 'E', "AndroidRuntime", "Caused by: java.util.NoSuchElementException: Stop 'NL:HRB:2' missing from timetable revision 41822"),
        new(0, Restarted, 9614, 'E', "AndroidRuntime", "\tat com.northlight.transit.data.TimetableStore.stopOrThrow(TimetableStore.kt:212)"),
        new(0, Restarted, 9614, 'E', "AndroidRuntime", "\t... 8 more"),
        Sysline(31, 'I', "DropBoxManagerService", "add tag=data_app_crash isTagEnabled=true flags=0x2", 1638),
        Sysline(88, 'I', "ActivityManager", "Process com.northlight.transit (pid 9812) has died: cch CEM ", 1567),
    ];

    private static SetPieceLine[] Tombstone()
    {
        const string Lib = "/data/app/~~9pQ1kZ==/com.northlight.transit-Hf2s7A==/lib/arm64/libtiledecode.so";
        return
        [
            new(0, Restarted, 9899, 'F', "libc", "Fatal signal 11 (SIGSEGV), code 1 (SEGV_MAPERR), fault addr 0x18 in tid 9899 (TileDecoder), pid 9812 (light.transit)"),
            new(28, Corpus.Init, 9905, 'I', "crash_dump64", "obtaining output fd from tombstoned, type: kDebuggerdTombstoneProto"),
            new(6, Corpus.Init, 9905, 'I', "tombstoned", "received crash request for pid 9899"),
            new(11, Corpus.Init, 9905, 'F', "DEBUG", "*** *** *** *** *** *** *** *** *** *** *** *** *** *** *** ***"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "Build fingerprint: 'Northlight/nl_edge/nl_edge:16/VQ1A.260701.004/26.07.14:user/release-keys'"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "Revision: '0'"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "ABI: 'arm64'"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "Timestamp: 2026-08-12 10:14:02.331884+0100"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "Process uptime: 214s"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "Cmdline: com.northlight.transit"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "pid: 9812, tid: 9899, name: TileDecoder  >>> com.northlight.transit <<<"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "uid: 10231"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "signal 11 (SIGSEGV), code 1 (SEGV_MAPERR), fault addr 0x0000000000000018"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "    x0  0000007b3c2d1000  x1  0000000000000018  x2  0000000000001000  x3  0000000000000000"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "    x4  0000007b3c2d2000  x5  0000000000000010  x6  0000007ae41a8c40  x7  7f7f7f7f7f7f7f7f"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "    x28 0000007ae41a9000  x29 0000007ae41a8b90"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "    lr  0000007b41c04d18  sp  0000007ae41a8b60  pc  0000007b41c04d40  pst 0000000060001000"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "backtrace:"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "      #00 pc 000000000004d140  /apex/com.android.runtime/lib64/bionic/libc.so (memcpy+192) (BuildId: 7f1c9a2e4d)"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "      #01 pc 0000000000031a18  " + Lib + " (nl_tile_blit+284) (BuildId: 3ac0198bb2)"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "      #02 pc 0000000000030c64  " + Lib + " (nl_tile_decode+1120) (BuildId: 3ac0198bb2)"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "      #03 pc 0000000000012e80  " + Lib + " (Java_com_northlight_transit_map_TileDecoder_nativeDecode+96) (BuildId: 3ac0198bb2)"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "      #04 pc 0000000000387a44  /apex/com.android.art/lib64/libart.so (art_quick_generic_jni_trampoline+148)"),
            new(0, Corpus.Init, 9905, 'F', "DEBUG", "Note: multiple threads found, only the crashing thread is shown"),
            Sysline(64, 'I', "BootReceiver", "Copying /data/tombstones/tombstone_07 to DropBox (SYSTEM_TOMBSTONE)", 1638),
            Sysline(41, 'I', "ActivityManager", "Process com.northlight.transit (pid 9812) has died: fg  TOP ", 1567),
            Sysline(18, 'W', "ActivityTaskManager", "Activity top resumed state loss timeout for ActivityRecord{5f2e910 u0 com.northlight.transit/.ui.RoutePlannerActivity}", 1601),
        ];
    }

    private static SetPieceLine[] Watchdog(Rng random)
    {
        var blocked = random.Next(21_000, 62_000);
        return
        [
            Sysline(0, 'W', "Watchdog", "*** WATCHDOG KILLING SYSTEM PROCESS: Blocked in handler on ActivityManager (ActivityManager)", 1744),
            Sysline(0, 'W', "Watchdog", "ActivityManager blocked for " + blocked.ToString(CultureInfo.InvariantCulture) + "ms", 1744),
            Sysline(2, 'W', "Watchdog", "*** GOODBYE!", 1744),
            Sysline(6, 'I', "Watchdog", "Dumping stack traces to /data/anr/anr_2026-08-12-10-18-44-006", 1744),
            Sysline(180, 'I', "ActivityManager", "Recovering from watchdog: restarting ActivityManager handler thread", 1567),
        ];
    }

    private static SetPieceLine[] Restart() =>
    [
        Sysline(0, 'I', "ActivityManager", "Start proc 9812:com.northlight.transit/u0a231 for activity {com.northlight.transit/.ui.JourneyActivity}", 1567),
        new(46, Corpus.Zygote, 743, 'I', "Zygote", "Forked child process 9812"),
        new(58, Restarted, 9812, 'I', "TransitApp", "Cold start after crash, restoring journey state revision 41822"),
        new(24, Restarted, 9812, 'W', "TransitApp", "Previous session ended abnormally (data_app_crash), sending anonymous crash summary"),
        new(140, Restarted, 9812, 'I', "art", "Late-enabling -Xcheck:jni"),
        Sysline(211, 'I', "ActivityTaskManager", "Displayed com.northlight.transit/.ui.JourneyActivity: +812ms", 1601),
        new(30, Restarted, 9812, 'D', "TransitApp", "Recovery complete, 0 pending writes replayed from journal"),
    ];
}
