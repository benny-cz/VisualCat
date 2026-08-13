# Platform and source support

| Surface | Support |
|---|---|
| Windows desktop | Primary release and profiling target |
| Linux desktop | Shared Avalonia/Skia application; CI build/test; x64 tarball only |
| macOS desktop | Shared Avalonia/Skia application; CI build/test; bare executable rather than a `.app` bundle |
| Android companion | Android 12+ (API 31) to API 36, `arm64-v8a` and `x86_64`; reduced single-session UI, app-private capture, and explicit portable-session share sheet |
| Captured files | Finite import with source-change detection |
| Growing files | Explicit follow mode; truncation/rotation stops visibly |
| Host ADB | Refreshing device discovery, explicit buffers/format/pre-roll, bounded reconnect with `-T` resume, and PID/name sampling |

A normal Android application is usually restricted to its own logs. Full-device capture is reported only when `READ_LOGS` is actually granted, commonly through:

```text
adb shell pm grant com.barebit.visualcat android.permission.READ_LOGS
```

ADB device states (`device`, `unauthorized`, `offline`, unknown) are surfaced rather than retried as parser failures. Signing, notarization, and store credentials are release-operator secrets and are not committed to the repository.

## Desktop distribution limits

The initial Windows and macOS binaries are unsigned, and the macOS binaries are
not notarized. Windows SmartScreen and macOS Gatekeeper may therefore require a
one-time confirmation after the user verifies the release checksum and build
provenance. The macOS archives contain terminal-launched executables, not Finder
`.app` bundles, so they do not provide normal Finder, Dock, or application-menu
integration.

Linux uses Avalonia's X11 backend (through XWayland on a default Wayland
desktop). A graphical X11/XWayland session, fonts, and the platform equivalents
of `libX11`, `libICE`, `libSM`, and `fontconfig` must be installed. For example,
on Debian or Ubuntu:

```shell
sudo apt install libx11-6 libice6 libsm6 libfontconfig1
```

The Linux release is a tarball only. It does not include a `.deb`, `.rpm`,
AppImage, Flatpak, `.desktop` launcher, or distribution-managed dependency
metadata.
