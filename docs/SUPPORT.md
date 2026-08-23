# Platform and source support

| Surface | Support |
|---|---|
| Windows desktop | Primary release and profiling target |
| Linux desktop | Shared Avalonia/Skia application; CI build/test; x64 tarball only |
| macOS desktop | Shared Avalonia/Skia application; CI build/test; bare executable rather than a `.app` bundle |
| Android companion | Android 12+ (API 31) to API 36, `arm64-v8a` and `x86_64`; reduced single-session UI, local Wireless-debugging full-device capture, app-private fallback, and explicit portable-session share sheet |
| Captured files | Finite import with source-change detection |
| Growing files | Explicit follow mode; truncation/rotation stops visibly |
| Host ADB | Refreshing device discovery, explicit buffers/format/pre-roll, bounded reconnect with `-T` resume, and PID/name sampling |

## Android live capture

On a normal Release/Play install, tap **Live → Full-device capture**. VisualCat
uses Android's own Wireless debugging feature instead of trying to request the
privileged `READ_LOGS` permission.

### First full-device capture

1. Tap **Open Developer options** in VisualCat.
2. Enable **Wireless debugging**.
3. Open **Pair device with pairing code** and keep that Android panel visible.
4. Enter the pairing port (the digits after the colon) and six-digit code in
   VisualCat, then tap **Pair & connect**.
5. Keep Wireless debugging enabled while Live capture is running. VisualCat
   closes its connection when capture stops; Android leaves Wireless debugging
   enabled until you turn it off in Settings.

Android normally remembers the pairing. On later captures, enable Wireless
debugging and use **Connect saved pairing**; another code is usually unnecessary.
If Android has forgotten or revoked the pairing, use **Pair again with a new code**.
If Android still lists a stale VisualCat entry under **Wireless debugging → Paired devices**,
remove that entry before pairing again.

The pairing code is not saved. VisualCat considers a pairing reusable only after
Android accepts it, then records a non-secret completion marker beside the encrypted
ADB identity. The identity is encrypted in app-private no-backup storage with a key
protected by Android Keystore.

### Why is there no READ_LOGS permission prompt?

`READ_LOGS` is not an ordinary runtime permission that a Play application can
request. Release builds therefore do not declare it by default. Wireless
debugging is the normal full-device path. Choosing **VisualCat only** requires no
Wireless debugging but captures only VisualCat's own log lines, so an idle app
can produce very little output.

Debug or explicitly opted-in non-Play builds may declare `READ_LOGS`. Developers
can then grant it from a separate trusted ADB host:

```text
adb shell pm grant com.barebit.visualcat android.permission.READ_LOGS
```

That is a developer shortcut, not the Google Play installation flow. On Android
13+ the direct READ_LOGS path can also trigger Android's separate per-use
log-access consent. Uninstalling/reinstalling removes an external grant.

### Wireless debugging troubleshooting

- **Saved pairing cannot connect:** verify Wireless debugging is currently on;
  if it still fails, remove the stale Android paired-device entry and pair again.
- **Pairing code rejected:** generate a fresh code and keep Android's pairing
  panel open while entering both the port and code. Some devices require split
  screen/pop-up view because switching apps closes the panel.
- **Capture loses the transport:** VisualCat records a reconnect gap and tries to
  reconnect with the saved identity. If Wireless debugging was turned off, stop
  Live, turn it back on, and reconnect.
- **Want to revoke access:** stop Live, remove VisualCat from Android's Wireless
  debugging paired devices, and clear VisualCat app data or uninstall it to
  remove the local encrypted identity.
- **Android 17 / target API 37:** current releases target API 36. Before VisualCat
  targets API 37, the new Android local-network permission flow must be added and
  tested; this is a release gate, not a current user action.

ADB device states (`device`, `unauthorized`, `offline`, unknown) are surfaced
rather than retried as parser failures. Signing, notarization, and store
credentials are release-operator secrets and are not committed to the repository.

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
