namespace VisualCat.DemoLog;

/// <summary>
/// The message corpus for the demo log. Everything here is invented: the device, the app
/// (<c>com.northlight.transit</c>), the operator, the hosts, and the users. The shapes follow
/// what AOSP components actually print so the result reads like a real capture, but no line
/// is copied from, or derived from, a real device.
/// </summary>
internal static class Corpus
{
    public static readonly string[] Packages =
    [
        "com.northlight.transit",
        "com.northlight.transit.wear",
        "com.android.systemui",
        "com.android.settings",
        "com.android.launcher3",
        "com.android.providers.media.module",
        "com.android.bluetooth",
        "com.android.phone",
        "com.northlight.conductor",
        "com.android.inputmethod.latin",
    ];

    public static readonly string[] Activities =
    [
        "com.northlight.transit/.ui.JourneyActivity",
        "com.northlight.transit/.ui.TicketWalletActivity",
        "com.northlight.transit/.ui.DepartureBoardActivity",
        "com.northlight.transit/.ui.RoutePlannerActivity",
        "com.northlight.transit/.ui.CheckoutActivity",
        "com.northlight.transit/.ui.SettingsActivity",
        "com.northlight.transit/.onboarding.WelcomeActivity",
        "com.android.settings/.SubSettings",
        "com.android.launcher3/.uioverrides.QuickstepLauncher",
    ];

    public static readonly string[] Components =
    [
        "com.northlight.transit/.sync.TimetableSyncService",
        "com.northlight.transit/.sync.FareUpdateWorker",
        "com.northlight.transit/.location.VehicleTrackerService",
        "com.northlight.transit/.push.NotificationRelayService",
        "com.northlight.transit/.wallet.TicketValidationService",
        "com.android.providers.media.module/.MediaService",
        "com.android.bluetooth/.btservice.AdapterService",
    ];

    public static readonly string[] Files =
    [
        "/data/user/0/com.northlight.transit/databases/timetable.db",
        "/data/user/0/com.northlight.transit/databases/wallet.db",
        "/data/user/0/com.northlight.transit/cache/tiles/z14",
        "/data/user/0/com.northlight.transit/files/fares-2026-08.pb",
        "/data/user/0/com.northlight.transit/shared_prefs/journey_state.xml",
        "/data/app/~~9pQ1kZ==/com.northlight.transit-Hf2s7A==/base.apk",
        "/storage/emulated/0/Android/data/com.northlight.transit/cache",
    ];

    public static readonly string[] Wakelocks =
    [
        "*job*/com.northlight.transit/.sync.FareUpdateWorker",
        "*alarm*:com.northlight.transit.DEPARTURE_REFRESH",
        "NlpWakeLock",
        "TimetableSync",
        "VehicleTracker",
        "*launch*",
        "ActivityManager-Launch",
        "NetworkStats",
    ];

    public static readonly string[] Exceptions =
    [
        "java.lang.IllegalStateException",
        "java.lang.NullPointerException",
        "java.util.ConcurrentModificationException",
        "java.io.IOException",
        "android.database.sqlite.SQLiteDatabaseLockedException",
        "java.lang.IndexOutOfBoundsException",
        "retrofit2.HttpException",
    ];

    public static readonly string[] Endpoints =
    [
        "https://api.northlight-transit.example/v3/departures",
        "https://api.northlight-transit.example/v3/journeys/plan",
        "https://api.northlight-transit.example/v3/wallet/tickets",
        "https://api.northlight-transit.example/v2/fares/table",
        "https://tiles.northlight-transit.example/v1/vector",
        "https://push.northlight-transit.example/v1/register",
        "https://api.northlight-transit.example/v3/vehicles/live",
    ];

    public static readonly string[] Ssids = ["\"NL-Depot-5G\"", "\"NL-Guest\"", "\"Platform-WiFi\"", "\"HomeNet-2G\""];

    public static readonly string[] GcCauses =
    [
        "Background concurrent copying GC",
        "Explicit concurrent copying GC",
        "Alloc concurrent copying GC",
        "NativeAlloc concurrent copying GC",
        "CollectorTransition concurrent copying GC",
    ];

    public static readonly string[] Stations =
    [
        "Northgate", "Harbour Yard", "Kelvin Bridge", "Old Mill", "Saltmarket",
        "Riverside Interchange", "Fernhill", "Beacon Park", "Cross Quay", "Ashvale",
    ];

    public static readonly string[] Queues = ["timetable", "wallet", "telemetry", "tiles", "vehicles", "fares"];

    public static readonly string[] Services =
    [
        "am.ActivityManagerService", "wm.WindowManagerService", "pm.PackageManagerService",
        "power.PowerManagerService", "job.JobSchedulerService", "net.NetworkPolicyManagerService",
        "notification.NotificationManagerService", "usage.UsageStatsService", "audio.AudioService",
        "location.LocationManagerService", "display.DisplayManagerService",
    ];

    public static readonly string[] Databases =
    [
        "/data/user/0/com.northlight.transit/databases/timetable.db",
        "/data/user/0/com.northlight.transit/databases/wallet.db",
        "/data/user/0/com.northlight.transit/databases/vehicles.db",
    ];

    /// <summary>
    /// Tags whose emitting thread is effectively fixed on a real device. Anything absent here
    /// is free to appear on any of its process's threads, which is also what really happens.
    /// </summary>
    public static string? PreferredThread(string tag) => tag switch
    {
        "HWUI" or "OpenGLRenderer" or "SurfaceControl" => "RenderThread",
        "Choreographer" or "ViewRootImpl" or "RecyclerView" or "StrictMode" => "main",
        "TransitApp" or "DepartureBoard" or "TicketWallet" or "RoutePlanner" or "FareEngine" => "main",
        "OkHttp" or "Retrofit" or "PushRelay" => "OkHttp Dispatcher",
        "Glide" or "MapTiles" => "glide-source-thread-1",
        "art" => "HeapTaskDaemon",
        "Room" or "SQLiteConnection" => "RoomTransaction",
        "WorkManager" => "WM.task-1",
        "InputDispatcher" => "InputDispatcher",
        "InputReader" => "InputReader",
        "PackageManager" => "PackageManager",
        "AlarmManager" => "AlarmManager",
        "JobScheduler" => "JobScheduler",
        "BatteryStatsService" => "batterystats-sync",
        "NetworkStatsService" => "NetworkStats",
        "WindowManager" or "ActivityTaskManager" => "android.ui",
        _ => null,
    };

    public static readonly ProcessInfo SystemServer = new("system_server", 1567,
    [
        ("main", 1567), ("android.ui", 1601), ("android.display", 1603), ("android.anim", 1605),
        ("Binder:1567_4", 1638), ("Binder:1567_9", 1702), ("Binder:1567_12", 1744),
        ("batterystats-sync", 1811), ("PackageManager", 1592), ("InputReader", 1618),
        ("InputDispatcher", 1619), ("NetworkStats", 1926), ("JobScheduler", 2001), ("AlarmManager", 1877),
    ]);

    public static readonly ProcessInfo SurfaceFlinger = new("surfaceflinger", 812,
    [
        ("surfaceflinger", 812), ("app", 838), ("appEventThread", 841), ("sfEventThread", 843),
        ("TimerDispatch", 848), ("binder:812_2", 855),
    ]);

    public static readonly ProcessInfo Zygote = new("zygote64", 743, [("main", 743), ("Signal Catcher", 748), ("HeapTaskDaemon", 752)]);

    public static readonly ProcessInfo Netd = new("netd", 1043,
    [
        ("netd", 1043), ("Binder:1043_1", 1067), ("Binder:1043_3", 1071), ("dnsproxyd", 1082),
    ]);

    public static readonly ProcessInfo Logd = new("logd", 726, [("logd.reader", 733), ("logd.writer", 735), ("logd.auditd", 737)]);

    public static readonly ProcessInfo SystemUi = new("com.android.systemui", 2214,
    [
        ("main", 2214), ("RenderThread", 2298), ("Binder:2214_3", 2266), ("StatusBar", 2311), ("hwuiTask1", 2340),
    ]);

    public static readonly ProcessInfo Transit = new("com.northlight.transit", 9431,
    [
        ("main", 9431), ("RenderThread", 9508), ("HeapTaskDaemon", 9463), ("FinalizerDaemon", 9465),
        ("Jit thread pool", 9451), ("OkHttp Dispatcher", 9577), ("OkHttp ConnectionPool", 9581),
        ("queued-work-looper", 9522), ("pool-4-thread-2", 9614), ("WM.task-1", 9640),
        ("glide-source-thread-1", 9659), ("RoomTransaction", 9688), ("Binder:9431_2", 9444),
    ]);

    /// <summary>The same app after the crash storm restarts it under a new PID.</summary>
    public static readonly ProcessInfo TransitRestarted = new("com.northlight.transit", 9812,
    [
        ("main", 9812), ("RenderThread", 9871), ("HeapTaskDaemon", 9840), ("TileDecoder", 9899),
        ("OkHttp Dispatcher", 9922), ("pool-4-thread-2", 9614), ("Signal Catcher", 9820),
    ]);

    public static readonly ProcessInfo TransitSync = new("com.northlight.transit:sync", 9702,
    [
        ("main", 9702), ("WM.task-3", 9744), ("pool-2-thread-1", 9758), ("HeapTaskDaemon", 9711),
    ]);

    public static readonly ProcessInfo Media = new("media.swcodec", 1289, [("media.swcodec", 1289), ("CodecLooper", 1322), ("ALooper", 1331)]);

    public static readonly ProcessInfo Audio = new("audioserver", 1119, [("audioserver", 1119), ("AudioOut_D", 1146), ("FastMixer", 1151)]);

    public static readonly ProcessInfo CameraServer = new("cameraserver", 1130, [("cameraserver", 1130), ("Camera3-Dev", 1177), ("CamHal-Notify", 1184)]);

    public static readonly ProcessInfo Bluetooth = new("com.android.bluetooth", 3355, [("main", 3355), ("BT Service", 3388), ("bt_stack", 3402)]);

    public static readonly ProcessInfo Vold = new("vold", 715, [("vold", 715), ("Binder:715_2", 728)]);

    public static readonly ProcessInfo Init = new("init", 1, [("init", 1)]);

    public static Channel[] Channels =>
    [
        new("am", SystemServer,
        [
            new("ActivityManager", 'I', "Start proc 9431:com.northlight.transit/u0a231 for activity {#a}", 14),
            new("ActivityManager", 'I', "Killing #P:#p/#u (adj 985): empty #n+1", 6),
            new("ActivityManager", 'D', "freezing #d #p", 8),
            new("ActivityManager", 'I', "Process #p (pid #P) has died: cch+#n CEM ", 5),
            new("ActivityManager", 'W', "Slow operation: #M ms so far, now at attachApplicationLocked: after mServices.attachApplicationLocked", 3),
            new("ActivityManager", 'I', "Force stopping #p appid=#A user=0: from pid #P", 2),
            new("ActivityTaskManager", 'I', "START u0 {act=android.intent.action.MAIN cat=[android.intent.category.LAUNCHER] flg=0x10200000 cmp=#a} from uid #D", 12),
            new("ActivityTaskManager", 'I', "Displayed #a: +#mms", 10),
            new("ActivityTaskManager", 'V', "Config changes=#H {1.0 ?mcc?mnc [en_GB] ldltr sw411dp w411dp h915dp 420dpi nrml long port}", 6),
            new("ActivityTaskManager", 'W', "Force finishing activity #a", 2),
            new("ActivityTaskManager", 'D', "TaskDisplayArea reparenting task=Task{#h #d visible=true type=standard mode=fullscreen}", 5),
            new("ActivityManager", 'D', "Skipping #p; not a background service", 4),
            new("ActivityManager", 'I', "Waited #m ms for ProcessRecord{#h #P:#p/#u}", 3),
            new("ProcessStats", 'D', "Committing #q stats for #D processes in #m ms", 3),
        ]),

        new("wm", SystemServer,
        [
            new("WindowManager", 'V', "Relayout Window{#h u0 #a}: viewVisibility=0 req=1080x2400", 12),
            new("WindowManager", 'D', "finishDrawingWindow: Window{#h u0 #a} mDrawState=DRAW_PENDING", 9),
            new("WindowManager", 'I', "Screen frozen for +#mms due to Window{#h u0 #a}", 3),
            new("WindowManager", 'W', "Failed to deliver inset state change to w=Window{#h u0 #a}", 2),
            new("WindowManager", 'D', "setRotation: rotation=0 alwaysSendConfiguration=false forceRelayout=false", 5),
            new("InputReader", 'D', "Input event: device=#y type=0x0000 code=0x0000 value=0x00000000 when=#D", 10),
            new("InputDispatcher", 'D', "Focus entered window: Window{#h u0 #a}", 8),
            new("InputDispatcher", 'V', "Delivering touch to Window{#h u0 #a} x=#d y=#D", 12),
            new("InputManager-JNI", 'W', "Input channel object '#a (client)' was disposed without first being removed", 1),
            new("ViewRootImpl", 'D', "hardware acceleration = true, mRemoved = false", 4),
        ]),

        new("pm", SystemServer,
        [
            new("PackageManager", 'D', "Ignoring attempt to set enabled state of disabled component #c", 4),
            new("PackageManager", 'I', "Update package #p code path from /data/app/~~9pQ1kZ== to /data/app/~~pT4mVc==", 2),
            new("PackageManager", 'V', "Queried #D packages for android.intent.action.VIEW in #m ms", 6),
            new("AppOps", 'D', "noteOperation: code=#y uid=#A package=#p result=MODE_ALLOWED", 10),
            new("AppOps", 'W', "Noting op not started: #y for uid #A package #p", 2),
            new("UsageStatsService", 'D', "Reporting event #y for #p at #D", 8),
            new("UsageStatsService", 'V', "Flushing #n pending usage events to disk", 4),
            new("PermissionManager", 'D', "Checking android.permission.ACCESS_FINE_LOCATION for #p: granted", 5),
            new("SettingsProvider", 'V', "Notifying for 0: content://settings/global/#q", 4),
        ]),

        new("power", SystemServer,
        [
            new("PowerManagerService", 'D', "acquireWakeLockInternal: lock=#h, flags=0x1, tag=\"#w\", ws=WorkSource{#A}", 12),
            new("PowerManagerService", 'D', "releaseWakeLockInternal: lock=#h [#w], flags=0x0", 12),
            new("PowerManagerService", 'I', "Going to sleep due to screen timeout (uid 1000)", 2),
            new("PowerManagerService", 'I', "Waking up from sleep (uid 1000 reason=android.policy:POWER)", 2),
            new("BatteryStatsService", 'D', "Recording battery level #x%, status=discharging, plug=none", 8),
            new("BatteryStatsService", 'V', "noteStartWakeLocked: uid=#A pid=#P name=#w", 10),
            new("DeviceIdleController", 'I', "Moved from STATE_ACTIVE to STATE_INACTIVE", 3),
            new("DeviceIdleController", 'D', "Setting AppIdle #p to true", 4),
            new("AlarmManager", 'D', "Adding alarm Alarm{#h type 2 when #D #p}", 10),
            new("AlarmManager", 'V', "Triggering alarm #w after #M ms window", 8),
            new("JobScheduler", 'D', "Enqueueing job #d for #p (network=any, charging=false)", 10),
            new("JobScheduler", 'I', "Running job JobStatus{#h #u/#c u0 #d}", 8),
            new("JobScheduler", 'V', "Job finished #d, reschedule=false, elapsed=#Mms", 8),
            new("ThermalService", 'D', "Thermal status changed to #j (NONE)", 3),
        ]),

        new("netsvc", SystemServer,
        [
            new("ConnectivityService", 'D', "NetworkAgentInfo [WIFI () - #d] validation passed", 10),
            new("ConnectivityService", 'D', "rematching NetworkAgentInfo [WIFI () - #d]", 8),
            new("ConnectivityService", 'V', "Adding iface wlan0 to network #d", 6),
            new("WifiService", 'D', "RSSI poll: rssi=-#y dBm linkSpeed=#dMbps frequency=5180 ssid=#v", 12),
            new("WifiClientModeImpl", 'D', "L2ConnectedState: CMD_RSSI_POLL screenOn=true", 8),
            new("NetworkPolicy", 'D', "updateRulesForDataUsageRestrictionsUL: uid=#A restricted=false", 6),
            new("NetworkStatsService", 'V', "Recorded #b bytes for uid #A on wlan0", 8),
            new("TelephonyManager", 'D', "getNetworkTypeForSubscriber: subId=1 type=LTE", 4),
            new("ConnectivityService", 'W', "Network #d validation failed: probe returned #d", 1),
        ]),

        new("netd", Netd,
        [
            new("netd", 'D', "trafficSetInterfaceQuota(wlan0, #b)", 8),
            new("netd", 'I', "firewallSetUidRule(#j, #A, 1) <#zms>", 6),
            new("DnsResolver", 'D', "res_nsend: query for api.northlight-transit.example took #mms via #i", 12),
            new("DnsResolver", 'V', "Cache hit for api.northlight-transit.example (#j records, ttl #M)", 10),
            new("Netd", 'D', "bandwidthSetGlobalAlert(#b)", 4),
            new("ClatdController", 'V', "clatd not needed on wlan0", 2),
        ]),

        new("sf", SurfaceFlinger,
        [
            new("SurfaceFlinger", 'D', "Latched buffer #d for layer #a#0", 12),
            new("SurfaceFlinger", 'V', "Setting active config #j for display #j (120.00 Hz)", 6),
            new("SurfaceFlinger", 'D', "Display #j: composited #n layers in #M us", 12),
            new("SurfaceFlinger", 'W', "Dropping frame for layer #a: buffer queue is full", 2),
            new("EventThread", 'V', "app: VSYNC period 8333333 ns, requested #j", 10),
            new("HWComposer", 'D', "presentAndGetReleaseFences: display #j present fence #d", 8),
            new("BufferQueueProducer", 'V', "[#a#0](this:#h) queueBuffer: slot #j", 10),
            new("BufferQueueConsumer", 'V', "[#a#0](this:#h) acquireBuffer: slot #j", 8),
            new("Layer", 'D', "Setting transform hint to #j for #a", 4),
        ]),

        new("gfx", Transit,
        [
            new("Choreographer", 'I', "Skipped #n frames!  The application may be doing too much work on its main thread.", 6),
            new("OpenGLRenderer", 'D', "endAllActiveAnimators on #h (RippleDrawable) with handle #h", 8),
            new("OpenGLRenderer", 'V', "Flushing caches (mode #j)", 6),
            new("HWUI", 'D', "RenderThread: draw took #zms, #n draw ops", 12),
            new("ViewRootImpl", 'D', "Relayout returned: old=(0,0,1080,2400) new=(0,0,1080,2400)", 6),
            new("ViewRootImpl", 'V', "performTraversals: mFirst=false mWindowAttributesChanged=false", 10),
            new("RecyclerView", 'D', "Rebound #n view holders for JourneyAdapter in #m ms", 8),
            new("Glide", 'V', "Loaded resource in #m ms from DATA_DISK_CACHE for #r", 8),
            new("Choreographer", 'W', "Frame time is #m ms in the future! Check that graphics HAL is generating vsync timestamps", 1),
            new("SurfaceControl", 'D', "Transaction applied for #a in #M us", 5),
        ]),

        new("art", Transit,
        [
            new("art", 'I', "#g freed #D(#nMB) AllocSpace objects, #n(#kKB) LOS objects, #x% free, #nMB/#nMB, paused #zms total #zms", 16),
            new("art", 'I', "Compiler allocated #kKB to compile void com.northlight.transit.ui.JourneyActivity.onCreate(android.os.Bundle)", 6),
            new("art", 'D', "Deoptimizing void com.northlight.transit.ui.DepartureAdapter.onBindViewHolder due to JIT inline cache", 4),
            new("art", 'V', "Starting a blocking GC #g", 8),
            new("art", 'W', "Long monitor contention with owner #t (#P) at void com.northlight.transit.data.TimetableStore.commit() waiters=#n for #zms", 3),
            new("art", 'I', "Background young concurrent copying GC freed #D(#nMB) AllocSpace objects, #x% free, #nMB/#nMB, paused #zms", 10),
            new("art", 'D', "JIT compiled void com.northlight.transit.map.TileDecoder.decode(byte[]) in #zms (osr=false)", 6),
            new("System.out", 'I', "Timetable revision #D applied for region #s", 4),
        ]),

        new("app", Transit,
        [
            new("TransitApp", 'D', "Journey planner warm start completed in #m ms", 8),
            new("TransitApp", 'I', "Session #D resumed for anonymous rider profile", 6),
            new("DepartureBoard", 'D', "Rendered #n departures for stop #s (feed age #ms)", 14),
            new("DepartureBoard", 'V', "Countdown tick: next service to #s in #y min", 12),
            new("RoutePlanner", 'D', "Computed #n itineraries #s → #s in #m ms (transfers<=#j)", 10),
            new("TicketWallet", 'I', "Ticket #D activated, valid for #y minutes", 6),
            new("TicketWallet", 'D', "Barcode refresh scheduled in #y s", 8),
            new("TicketWallet", 'W', "Wallet clock drift #ms exceeds tolerance, forcing NTP resync", 2),
            new("VehicleTracker", 'V', "Vehicle #D at #L,#O heading #d° speed #ykm/h", 12),
            new("FareEngine", 'D', "Fare table #D loaded, #D zone pairs, #kKB resident", 6),
            new("FareEngine", 'I', "Capping applied: daily total #z GBP for rider hash #h", 4),
            new("Room", 'D', "Query on #B returned #D rows in #m ms", 12),
            new("Room", 'V', "Transaction committed: #n statements, wal #kKB", 8),
            new("SQLiteConnection", 'W', "Slow query on #f took #M ms: SELECT * FROM departures WHERE stop_id = ?", 2),
            new("WorkManager", 'D', "Worker result SUCCESS for #c", 8),
            new("WorkManager", 'I', "Enqueued unique work #q-refresh, existing policy KEEP", 6),
            new("Preferences", 'V', "Committing #n keys to #f", 6),
            new("MapTiles", 'D', "Tile 14/#D/#D decoded in #m ms (#kKB)", 10),
            new("MapTiles", 'V', "Evicted #n tiles, cache now #kKB of #kKB", 8),
        ]),

        new("http", Transit,
        [
            new("OkHttp", 'D', "--> GET #r", 14),
            new("OkHttp", 'D', "<-- 200 OK #r (#mms, #kKB body)", 14),
            new("OkHttp", 'V', "Connection pool: #j idle, #n total, evicting after #M ms", 8),
            new("OkHttp", 'D', "Reusing connection to #i:443 (h2, TLSv1.3)", 10),
            new("Retrofit", 'V', "Deserialized #D departures in #m ms", 8),
            new("Retrofit", 'D', "Cache-Control: max-age=#d, ETag \"#h\"", 6),
            new("OkHttp", 'W', "<-- 429 Too Many Requests #r (#mms) retry-after #d", 2),
            new("Glide", 'D', "Started prefetch of #n tiles for viewport #s", 6),
            new("PushRelay", 'D', "Heartbeat ack #D, rtt #m ms", 8),
        ]),

        new("sysui", SystemUi,
        [
            new("StatusBar", 'D', "Notification posted: #p / #D", 10),
            new("NotificationService", 'V', "Ranking update: #n notifications, #n visible", 8),
            new("KeyguardViewMediator", 'D', "handleKeyguardDoneDrawing", 4),
            new("SystemUI", 'D', "QS tile #q updated to state ACTIVE", 8),
            new("SystemUI", 'V', "Shade expansion #z, tracking=false", 10),
            new("VolumeDialog", 'D', "showH r=#y reason=volume_changed", 3),
            new("NavigationBar", 'V', "Gesture nav hint updated, mode=#j", 6),
            new("StatusBar", 'W', "Ignoring notification with no valid small icon: #p", 1),
        ]),

        new("media", Media,
        [
            new("MediaCodec", 'D', "[c2.android.avc.decoder] configure: 1920x1080 @#jfps", 8),
            new("CCodec", 'D', "setting configuration: #n parameters", 8),
            new("Codec2Client", 'V', "queue: work #D queued, #n in flight", 12),
            new("MediaCodecList", 'D', "Enumerated #n codecs in #m ms", 3),
            new("ACodec", 'V', "onOutputBufferDrained: buffer #d ts #D", 10),
            new("MediaCodec", 'W', "Output format changed mid-stream, reconfiguring #n buffers", 2),
        ]),

        new("audio", Audio,
        [
            new("AudioFlinger", 'D', "createTrack: session #y, sampleRate 48000, frameCount #k", 8),
            new("AudioFlinger", 'V', "mixer: underrun count #j on fast track #y", 8),
            new("AudioPolicyManager", 'D', "getOutputForAttr: stream #j flags #H device #D", 8),
            new("AudioTrack", 'V', "Buffer size #kKB, latency #m ms", 10),
            new("AudioFlinger", 'W', "write blocked for #m ms, #n mixer overruns", 2),
        ]),

        new("camera", CameraServer,
        [
            new("CameraService", 'I', "connectDevice: Camera #j connected by #p", 4),
            new("Camera3-Device", 'D', "processCaptureResult: frame #D, #j results pending", 12),
            new("Camera2ClientBase", 'V', "disconnect: Closing camera #j for #p", 3),
            new("CameraDeviceClient", 'D', "submitRequestList: #j requests, streaming=true", 8),
            new("Camera3-OutputStream", 'V', "Buffer #y returned to queue, #n free", 10),
            new("CameraService", 'W', "Camera #j torch unavailable while streaming", 1),
        ]),

        new("bt", Bluetooth,
        [
            new("BluetoothAdapter", 'D', "isLeEnabled(): ON", 8),
            new("bt_stack", 'V', "btm_ble_scanner: scan window #y interval #d", 10),
            new("BluetoothLeScanner", 'D', "onScanResult: #j devices in batch, rssi -#y", 10),
            new("A2dpService", 'D', "Connection state changed for device XX:XX:XX:XX:#H: CONNECTED", 4),
            new("bt_stack", 'W', "hci: command timeout for opcode #H, retrying", 1),
        ]),

        new("sensors", SystemServer,
        [
            new("SensorService", 'D', "Enabling sensor #y for #p at #D us", 8),
            new("SensorManager", 'V', "Batch report: #n samples for accelerometer", 12),
            new("GnssLocationProvider", 'D', "Reporting location: lat #L lon #O acc #ym sats #n", 10),
            new("LocationManagerService", 'D', "Request from #p: interval #Ms priority HIGH_ACCURACY", 8),
            new("GnssLocationProvider", 'W', "GNSS fix lost, falling back to network provider", 2),
            new("LocationManagerService", 'V', "Delivering location to #n listeners", 8),
        ]),

        new("storage", Vold,
        [
            new("vold", 'D', "Trimmed #b bytes on /data in #M ms", 3),
            new("StorageManagerService", 'V', "Volume public:179,65 state changed to MOUNTED", 2),
            new("vold", 'I', "Detected #b free bytes on /data (#x% free)", 6),
            new("installd", 'D', "Cleaning cache for #p, freed #b bytes", 4),
            new("F2FS", 'V', "checkpoint: #D blocks flushed in #m ms", 6),
        ]),

        // chatty suppression is attributed to the process whose lines were dropped, not to
        // logd, so it rides the app's PID/TID rather than the logd channel's.
        new("chatty", Transit,
        [
            new("chatty", 'I', "uid=10231(com.northlight.transit) #t identical #n lines", 20),
            new("chatty", 'I', "uid=1000(system) #t identical #n lines", 8),
        ]),

        new("logd", Logd,
        [
            new("logd", 'D', "buffer main: #D entries, #kKB used of 4096KB", 6),
            new("logd", 'W', "Prune: dropped #D lines from buffer main (uid #D)", 2),
            new("logd", 'V', "Rotating buffer system at #kKB watermark", 3),
        ]),

        new("sync", TransitSync,
        [
            new("TimetableSync", 'I', "Sync started for region #s, cursor #h", 10),
            new("TimetableSync", 'D', "Fetched #D route patterns, #D trips, #D stop times", 12),
            new("TimetableSync", 'V', "Delta applied: +#D -#D rows in #M ms", 10),
            new("TimetableSync", 'I', "Sync completed in #M ms, next window in #y min", 8),
            new("FareUpdateWorker", 'D', "Fare bundle #D verified (sha256 #h…)", 8),
            new("FareUpdateWorker", 'W', "Bundle signature stale by #j days, requesting refresh", 2),
            new("SyncScheduler", 'V', "Backoff reset for queue #q", 6),
        ]),

        // Failure-flavoured channels. Phases raise these instead of switching to a different
        // vocabulary, so a burst looks like the same device having a bad time.
        new("netfail", Transit,
        [
            new("OkHttp", 'W', "<-- HTTP FAILED: java.net.SocketTimeoutException: timeout after #Mms #r", 14),
            new("OkHttp", 'E', "Request to #r failed after #n retries: java.net.SocketTimeoutException", 10),
            new("DnsResolver", 'W', "res_nsend: connection refused for api.northlight-transit.example via #i", 8),
            new("TransitApp", 'E', "Departure refresh failed for stop #s: java.io.IOException: unexpected end of stream", 10),
            new("Retrofit", 'W', "Falling back to cached response for #r (age #Ms)", 8),
            new("ConnectivityService", 'W', "Network #d lost validation: no internet access detected", 6),
            new("WifiClientModeImpl", 'W', "CMD_IP_REACHABILITY_LOST, disconnecting from #v", 4),
            new("TicketWallet", 'E', "Validation server unreachable, ticket #D queued for offline verification", 6),
            new("PushRelay", 'W', "Heartbeat missed #j times, reconnecting in #y s", 8),
            new("VehicleTracker", 'W', "Live vehicle feed stale by #Ms, hiding map overlay", 6),
            new("OkHttp", 'E', "SSL handshake aborted: ssl=#h: I/O error during system call, Connection reset by peer", 5),
        ]),

        new("gcpressure", Transit,
        [
            new("art", 'W', "Suspending all threads took: #m.#dms", 10),
            new("art", 'I', "#g freed #DKB AllocSpace bytes, #n(#kKB) LOS objects, #x% free, #kKB/#kKB, paused #Mus total #Mms", 16),
            new("art", 'W', "Throwing OutOfMemoryError \"Failed to allocate a #D byte allocation with #kKB free bytes and #kKB until OOM\"", 3),
            new("art", 'I', "WaitForGcToComplete blocked #g on HeapTaskDaemon for #Mms", 10),
            new("lowmemorykiller", 'I', "Kill '#p' (#P), uid #D, adj #D to free #DKB rss", 4),
            new("Glide", 'W', "Bitmap pool trimmed to #kKB after memory pressure signal #j", 8),
            new("MapTiles", 'W', "Tile cache evicted #D entries under pressure, hit rate now #x%", 8),
            new("ActivityManager", 'I', "Low on memory: #D processes, #DKB free, adj threshold #D", 6),
            new("TransitApp", 'W', "onTrimMemory(level=#d), releasing #n cached itineraries", 8),
        ]),

        new("boot", Init,
        [
            new("init", 'I', "Starting service 'vendor.thermal-hal-2-0'...", 6),
            new("init", 'I', "processing action (init.svc.zygote=running) from (/system/etc/init/hw/init.rc:#D)", 8),
            new("SELinux", 'I', "avc:  granted  { find } for pid=#P uid=#A name=#q scontext=u:r:untrusted_app:s0", 8),
            new("init", 'W', "Service 'vendor.sensors' exited with status #j, restarting", 2),
            new("Zygote", 'I', "Preloaded #D classes in #M ms", 4),
            new("Zygote", 'D', "begin preload, cache size #kKB", 3),
            new("vold", 'I', "Mounting /data with checkpoint=#j", 2),
            new("PackageManager", 'I', "Finished scanning #D packages in #M ms", 3),
            new("SystemServer", 'I', "StartBootPhase #d took #M ms", 6),
            new("SystemServiceManager", 'D', "Starting com.android.server.#S", 8),
        ]),
    ];
}
