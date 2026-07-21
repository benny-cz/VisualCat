# Platform and source support

| Surface | Support |
|---|---|
| Windows desktop | Primary release and profiling target |
| Linux desktop | Shared Avalonia/Skia application; CI build/test |
| macOS desktop | Shared Avalonia/Skia application; CI build/test |
| Android companion | Android 12+ target; reduced single-session UI, app-private capture, and explicit portable-session share sheet |
| Captured files | Finite import with source-change detection |
| Growing files | Explicit follow mode; truncation/rotation stops visibly |
| Host ADB | Refreshing device discovery, explicit buffers/format/pre-roll, bounded reconnect with `-T` resume, and PID/name sampling |

A normal Android application is usually restricted to its own logs. Full-device capture is reported only when `READ_LOGS` is actually granted, commonly through:

```text
adb shell pm grant com.visualcat.app android.permission.READ_LOGS
```

ADB device states (`device`, `unauthorized`, `offline`, unknown) are surfaced rather than retried as parser failures. Signing, notarization, and store credentials are release-operator secrets and are not committed to the repository.
