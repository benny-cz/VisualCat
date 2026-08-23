# Privacy

Logcat can contain credentials, tokens, personal data, device identifiers,
notification text, network names, file paths, and proprietary information.
VisualCat therefore treats both imported logs and live device logs as sensitive
content.

## What VisualCat does with logs

- VisualCat processes sessions locally and ships with no telemetry, advertising,
  account system, cloud sync, upload service, or remote analysis backend.
- External file sessions retain a path and content identity. Portable and live
  sessions retain raw bytes in `raw.log` so source context remains byte-faithful.
- Exports and portable sessions can contain every secret present in the source;
  review them before sharing.
- Android sharing happens only after an explicit user action through the system
  share sheet.
- Temporary sessions use platform-local application storage. Automatic cleanup
  is disabled by default and is visible under **Session cache**. Deleting a
  session removes VisualCat's files but does not promise forensic erasure from
  flash storage.

## Android live capture

A normal Google Play/Release build does **not** declare or attempt to obtain the
privileged `android.permission.READ_LOGS` permission. When the user explicitly
chooses full-device Live capture, VisualCat can instead use Android's own
**Wireless debugging** feature:

1. the user enables Developer options and Wireless debugging in Android Settings;
2. for the first connection, the user enters the pairing port and six-digit code
   shown by Android;
3. VisualCat creates an authenticated ADB connection to Android's debugging daemon
   and opens only its fixed full-device `logcat` stream;
4. the connection is closed when Live capture stops.

The six-digit pairing code is used only for that pairing attempt. It is not
persisted and is intentionally omitted from application logs. Android normally
remembers the paired public ADB identity. VisualCat stores the corresponding RSA
private identity in its no-backup app-private directory encrypted with
AES-256-GCM; the wrapping AES key is non-exportable and held by Android Keystore.
Clearing VisualCat's app data or uninstalling the app removes VisualCat's local
identity; Android's Wireless debugging **Paired devices** screen can be used to
remove the Android-side pairing as well.

Wireless debugging requires local socket and mDNS access. The Release manifest
therefore declares `INTERNET` and `CHANGE_WIFI_MULTICAST_STATE`. VisualCat uses
those permissions for the user-initiated device-local Wireless ADB transport;
it does not use them to contact a VisualCat server, upload logs, or send
telemetry. Current builds target Android 16 / API 36. A future move to target
Android 17 / API 37 must add the platform's local-network permission flow (or a
system-mediated alternative) before release.

Debug or explicitly opted-in non-Play builds may additionally declare
`READ_LOGS` so developers can test the existing direct on-device source after an
external ADB grant. VisualCat's production Wireless ADB path never grants that
permission to itself.

## Diagnostics

Redacted application diagnostics are local, bounded rolling JSON lines. Property
names associated with paths, device serials, searches, queries, raw data, and
messages are redacted. Diagnostic bundles omit raw logs and redact paths,
hashes, file names, tags, templates, process names, searches, and device serials;
the UI still requires an explicit sensitive-metadata acknowledgement before
creating one.
