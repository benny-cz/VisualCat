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

Reader-started Live capture also declares Android's `FOREGROUND_SERVICE` and
`FOREGROUND_SERVICE_DATA_SYNC` permissions. Its required foreground-service
notification is private and contains only capture state, never a log message,
device identifier, pairing code, search or file name. Android 13+ notification
visibility is requested once through `POST_NOTIFICATIONS`; denial does not
change what VisualCat stores or transmit anything, and Android still exposes
the running service through Active apps. Android 15+ can end background
data-sync work after six hours; VisualCat then requests its ordinary graceful
Stop so already-received session data is kept.

Debug or explicitly opted-in non-Play builds may additionally declare
`READ_LOGS` so developers can test the existing direct on-device source after an
external ADB grant. VisualCat's production Wireless ADB path never grants that
permission to itself.

## Update checks on Google Play

A build installed from Google Play asks the Play Store whether a newer VisualCat
is available. That check is an inter-process call into the Play Store app that
is already on the device, through Google's in-app update client. VisualCat opens
no socket for it, sends no identifier, and reaches no VisualCat-operated
endpoint; the Play Store does whatever network work is required, under the
account relationship the user already has with it, to service the app they
installed from it. Nothing about the user, the device, or any session is sent
anywhere by VisualCat, and no session, log, file name, or search ever forms part
of the exchange. What comes back is a version code and whether an update may be
started.

The check runs at most once a day for a production build, and more often only on
the alpha and beta testing channels, where testers are there to run the newest
build. What the app remembers between launches is three values in its own local
settings file: the version code the reader last declined, when they may next be
asked, and when the store was last asked. These stay on the device.

**A build that Google Play did not install is never checked automatically.** The
APK attached to a GitHub release, and any developer build, cannot be updated in
place by Play, and VisualCat does not contact GitHub or any other server to look
for one either — that would be exactly the direct network egress this product
does not do. Such a build answers the explicit **Check for updates…** command by
saying so and offering to open the GitHub releases page in the system browser,
which is an ordinary link the user chooses to follow.

Downloading and installing an update is performed by the Play Store, in its own
interface, after the user agrees to it. VisualCat never installs anything itself
and does not hold `REQUEST_INSTALL_PACKAGES`.

## Diagnostics

Redacted application diagnostics are local, bounded rolling JSON lines. Property
names associated with paths, device serials, searches, queries, raw data, and
messages are redacted. Diagnostic bundles omit raw logs and redact paths,
hashes, file names, tags, templates, process names, searches, and device serials;
the UI still requires an explicit sensitive-metadata acknowledgement before
creating one.
