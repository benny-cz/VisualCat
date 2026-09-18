# Release checklist

## One command first

```shell
pwsh ./tools/verify-public-release.ps1 -AllRuntimes -ScanHistory
```

This answers "is this commit mechanically ready to package?" It composes the
existing checks — formatting, Release build, tests, CLI help, documentation and
version consistency, vulnerable packages, packaging with the notice files users
receive, CycloneDX SBOM generation and license review, workflow shell-body
parsing and action pinning, text-output newline parity, and a secret scan — and
exits non-zero naming the first failing stage.
It never tags, pushes, or publishes anything.

Everything below is what a machine cannot decide.

## Enforced automatically

These are gates, not reminders. CI enforces them on every pull request, and the
release workflow's `preflight` job enforces them again on the exact commit being
packaged. Tagged commits must also be reachable from `main`, so a release cannot
be published from an unmerged commit or one that would fail a pull request:

- formatting, Release build with warnings as errors, and the full test suite on
  Windows, Linux, and macOS;
- `vcat` help matching `docs/CLI-HELP.txt`, checked by `tools/verify-cli-help.ps1`;
- relative Markdown links, required repository files, and README/changelog/
  `Directory.Build.props` version agreement, checked by `tools/verify-docs.ps1`;
- a secret scan over the working tree and all reachable Git history,
  `tools/scan-secrets.ps1`;
- every `run:` body in every workflow parsed by the shell that will actually be
  given it, and every `uses:` pinned to a commit SHA, `tools/verify-workflows.ps1`
  — a step that does not parse runs none of itself, and the release workflow's
  steps are unreachable from a pull request, so their first execution used to be
  the tag;
- the Linux desktop and CLI archives packaged and verified on every pull request,
  rather than first at the tag;
- the desktop binary launched with no display, which must exit 69 with one
  sentence rather than abort;
- every text export recorded on Windows, Linux and macOS and compared byte for
  byte, `tools/verify-output-parity.ps1`;
- a tag matching `vMAJOR.MINOR.PATCH` with an optional prerelease suffix, and a
  changelog section for the version being released;
- `LICENSE`, `THIRD-PARTY-NOTICES.md`, and a `README.txt` staged into every
  desktop and CLI publish directory before archiving;
- extraction of each finished archive, verifying its layout, notice files, and
  reported version, and running the archived CLI where the runner can execute
  it;
- installation of the packed `.nupkg` from a local feed, then running it;
- a CycloneDX SBOM whose licenses are reviewed, failing on licenses
  incompatible with an MIT-licensed self-contained distribution; and
- GitHub build provenance attestations over every published artifact.

## Manual gates

- [ ] `global.json` resolves to the recorded stable SDK.
- [ ] Sanitized golden corpus and sample scripts reconcile reviewed counts.
- [ ] Corruption checks reject altered manifest, column, bitmap, and raw data.
- [ ] Reference ingest, reopen, heat-map, search, cancellation, and memory measurements are recorded.
- [ ] The exact Windows candidate satisfies every applicable release schedule and
      exit criterion in the [Windows live test plan](WINDOWS-LIVE-TEST-PLAN.md);
      the release record names its run ID, coverage gaps, findings, evidence hash
      index, and cleanup result.
- [ ] Four-hour host-ADB soak and Android Wireless-ADB soak/reconnect/disconnect matrices complete without an orphan process, leaked stream, persistent debugging connection, or sustained post-warm-up memory growth. Exercise enough log traffic to prove the bounded Wireless-ADB receive pump recycles/reconnects rather than allowing the third-party queue to grow without limit.
- [ ] File, portable, growing-file, partial, degraded, and incompatible sessions are manually exercised.
- [ ] Keyboard, contrast, text scaling, focus, and screen-reader labels are reviewed.
- [ ] Windows and macOS signing/notarization state matches the release decision and public docs; any intentionally unsigned artifact has explicit risk approval and tested OS-warning instructions; Linux packages are validated.
- [ ] Android own-app and Wireless-debugging full-device modes are tested on physical hardware, including first pairing, saved reconnect, Stop/disconnect, background/resume, rotation, and revoked/stale pairing recovery.
- [ ] Cancel is exercised during Wireless-ADB discovery/connection and during the low-level pairing handshake. Discovery/connection must unwind promptly; pairing may remain visibly `Cancelling…` until LibADB's local socket handshake returns, but Live must not start afterward and no authenticated ADB connection may remain.
- [ ] Android Live warning/setup UX matches the Release transport: the scope chooser contains no normal-Play `READ_LOGS` promise/jargon, choosing a scope does not trigger a redundant second disclosure before capture, saved pairing hides the new-code form until explicit recovery, and Back/scrim dismissal during pairing follows the same visible `Cancelling…` lifecycle as the Cancel button.
- [ ] The exact Linux candidate satisfies the applicable rows of the
      [Linux live test plan](LINUX-LIVE-TEST-PLAN.md); the release record names its
      run ID, coverage gaps, findings, evidence hash index, and cleanup result.
- [ ] Every screenshot filed as Linux release evidence is checked for unpainted
      regions before it is filed, and any capture with a non-zero unpainted
      fraction is retaken rather than kept. An XWayland client can present a
      window with a band that was never drawn, so a screenshot is not by itself
      proof of what the product rendered
      (`docs/LINUX-LIVE-TEST-REPORT.md` F-10):

      ```shell
      W=$(xdotool search --class VisualCat | tail -1)
      import -window "$W" -silent /tmp/w.png
      convert /tmp/w.png -colorspace gray -threshold 2% -negate -format '%[fx:mean*100]' info:
      ```

- [ ] Privacy, support matrix, known limits, migration policy, and third-party notices are current.
- [ ] Components the SBOM reports without license metadata have been resolved by
      hand and explained in `docs/THIRD-PARTY-NOTICES.md`.
- [ ] The exact Play AAB manifest has `INTERNET`, `CHANGE_WIFI_MULTICAST_STATE`, `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_DATA_SYNC`, and `POST_NOTIFICATIONS`; declares the unexported Live capture service as `dataSync`; does **not** contain `android.permission.READ_LOGS`; and has no unexpected sensitive permission.
- [ ] The exact Play AAB is inspected for Android-only Maven/JNI dependencies and licenses, including `libadb-android-bc`, Bouncy Castle, and every bundled/transitive pairing component.
- [ ] Wireless ADB pairing code is absent from logs, diagnostics, persisted files, backups, and crash artifacts; the saved ADB identity is encrypted in `NoBackupFilesDir` and removal/clear-data behavior is verified.
- [ ] Play Console Data Safety, App access, permissions, privacy policy, and store description match the audited AAB rather than an older direct-`READ_LOGS` build.
- [ ] If targetSdk is ever raised to 37+, Android 17 local-network permission behavior is re-designed and physically tested before release.

## Version codes, tracks, and in-app updates

Google Play orders uploads by `versionCode` alone and **refuses a code it has
already accepted, on any track**. The project derives it from the release version
and an explicit build counter:

```
versionCode = major * 1000000 + minor * 10000 + patch * 100 + VisualCatBuildNumber
```

so 2.0.14 is `2001400`, and `-VisualCatBuildNumber 3` on 2.1.0 gives `2010003`.
Each field owns two digits and the build fails rather than wrap. The prerelease
suffix is **not** an input, so two builds of the same version — `v2.1.0-beta.1`
and `v2.1.0-beta.2` — need the counter bumped or Play rejects the second.

**Promote, do not rebuild.** Move the same artifact and the same version code from
closed to open to production. That is what makes the pre-launch report and the
testers' feedback apply to the build that reaches users, and it keeps the codes
dense. Rebuilding for the stable release is legal but requires bumping
`VisualCatBuildNumber` and wastes the pre-launch report; record the reason if you
do it.

| Play track | Tag | Channel the app reports | Suggested `inAppUpdatePriority` |
|---|---|---|---|
| Internal testing | untagged `workflow_dispatch` | `Development` — never prompts | — |
| Closed testing | `v2.1.0-alpha.N` | `Alpha` | 3 |
| Open testing | `v2.1.0-beta.N` | `Beta` | 2 |
| Production, routine | `v2.1.0` | `Stable` | 1 |
| Production, data-loss or security fix | `v2.1.0` | `Stable` | 5 |

`inAppUpdatePriority` is set **per release**, through the Google Play Developer
API only (`edits.tracks.releases[].inAppUpdatePriority`); the Play Console UI does
not expose it, and **it cannot be changed once the release is rolled out**. The
app reads it to decide whether an update may escalate past a dismissal, or take
the screen. Default low and escalate deliberately.

- [ ] Version code is unique and higher than every code previously uploaded on any track.
- [ ] `inAppUpdatePriority` is set for this release before rolling out — it cannot be corrected afterwards.
- [ ] The release is promoted rather than rebuilt, or `VisualCatBuildNumber` was bumped and the reason recorded.
- [ ] In-app update is verified end to end against the previous build through **internal app sharing**, which is the only way to exercise the real Play client. Upload build N, install it from the internal-app-sharing link, upload build N+1, then launch build N. Both builds must carry an alpha or beta version — a `Development` build does not prompt, so the test would silently prove nothing:
      `pwsh ./tools/package-android.ps1 -Format aab -Version 2.1.0-alpha.1` then the same with `-Version 2.1.0-alpha.2 -VisualCatBuildNumber 2`.
- [ ] A staged production rollout is at 100% before anyone concludes an update "did not appear": a release held at 20% is offered only to the fraction Play has admitted, and the app cannot tell that apart from being up to date — nor should it try.
- [ ] The Play Core dependency still declares no permission and no exported component. The packaging script asserts the permissions; check components by hand when the binding version moves.

## Release records

> **Historical transport note:** the records through v2.0.6 below predate the
> unreleased Wireless ADB production transport. Their full-device results validate
> the old externally granted `READ_LOGS` path only. They remain immutable release
> evidence, but they do **not** sign off the current Wireless debugging path. A new
> candidate must satisfy the manual Wireless ADB gates above and record a new
> physical-device run before Play publication.

### v2.0.14 — 2026-09-18 — **candidate NOT signed off**

Smoke schedule (§12) on the exact Play candidate. This is the first release
record since v2.0.8; **2.0.9 through 2.0.13 have none** and are not signed off
retrospectively by this one.

- **Artifact.** `com.barebit.visualcat` 2.0.14, versionCode `2001400`, minSdk 31,
  targetSdk 36, ABIs `arm64-v8a` + `x86_64`. The installed APK is byte-identical
  to `artifacts/android/VisualCat-Android-v2.0.14.apk`
  (`8e7e7baff2f495af1ea61334b2bc5bd0a0724cc1b539340eddefeab28c82bb73`), signed by
  the pinned upload certificate `a715b030…6530e184` (RSA 4096, v3 scheme). The
  manifest carries exactly the five audited permissions plus the generated
  `DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION`, and **no `READ_LOGS`**. The empty
  state prints `VisualCat 2.0.14+6cfd2d9`, which is the tagged commit.
- **Device.** Samsung SM-G990B (Galaxy S21 FE), Android 16 / API 36, serial
  `RFCRC0A9GND`, three-button navigation, override density 360 (2.25 px/dp →
  480 × 1040 dp portrait), `font_scale` 1.0, night mode on.
- **Passed.** B-01 (cold launch 1562 ms, correct hero actions, no desktop
  commands, no unpainted bars), B-03 (quick import, provider-named tab), B-10
  (own-app Live: honest `own-app scope` notice, first record reads
  `scope resolved: fullDevice=False, declined=False`, notification permission
  requested once and **declined without blocking capture**), B-12 (`Stop capture`
  pressed once → `Stopped · 122 entries kept`, live-only controls removed, label
  never sprang back), B-13 (forced process stop + cold relaunch → `Ready · 122
  entries`, same counts and range). The Live scope chooser carries no
  normal-Play `READ_LOGS` promise or jargon.
- **H-06 parity — PARTIAL.** Entry and severity counts agree exactly between the
  phone and the 2.0.14 CLI on a 3,999-record `long` corpus: 3,999 entries and
  F 638 / E 659 / W 677 / I 666 / D 675 / V 684 on both. The **unparsed-line
  count does not agree** — see F-01 below.

- **B-11 Wireless ADB — PASS, both states.** The largest untested cell, and the
  first real pairing evidence since the 2.0.7 cycle. **W1:** Android's
  pairing-code panel is cancelled when Settings loses focus, so the first attempt
  failed — and failed *well*, naming that exact cause and clearing the code field
  so a stale code is not retried. Split screen (Recents → the card's app icon →
  *Open in split screen view*) keeps the panel alive; pairing then succeeded and
  Live ran at `Wireless debugging full-device` scope, **25,951 entries** over
  ~90 s at 43 lines/s, dense across all six severities where own-app capture had
  only I 117 / D 5. **W2:** Live offered *Recommended · already paired* with the
  new-code form hidden and the button changed to **Connect full-device**;
  reconnect used the saved encrypted identity with no new code and captured
  **4,292 entries** at 49 lines/s. Stop closed the transport both times, with an
  explicit notice — *"VisualCat closed its Wireless debugging connection and
  discarded the decrypted…"* — and an **Open settings** action. Afterwards the ADB
  TLS port held **zero ESTABLISHED sockets**, one in `TIME_WAIT`, which is the
  kernel holding the 4-tuple after a clean close. Android listed the pairing as
  `VisualCat`; it was forgotten at hand-back and the owner's two existing
  pairings were left untouched.
- **The pairing code never reaches a VisualCat log.** A grep of the full logcat
  for the live code returned one hit, and it was `adbd` logging *this harness's
  own* `input text <code>` shell command — an artifact of typing the code over
  ADB that a person using the keyboard cannot produce. VisualCat logged nothing.
  The code is masked as six dots in the field, and both fields stay above the
  keyboard.

**Findings. Neither is fixed; both are open against this candidate.**

- **F-01 · `long` format counts every record's own message body as an unreadable
  line. Major. Partly fixed after this run; see the changelog's `[Unreleased]`.** `LogcatParser.cs:115` classifies every non-header, non-blank
  line in `long` format as `Continuation`, and the chip bar and *Lines not on the
  timeline* count those outcomes. A file with two records, three body lines and
  **zero** unattached lines reports `3 unparsed lines`; the 3,999-record corpus
  reports `4,000 unparsed lines` where the true number of lines belonging to no
  record is **one**. The dialog then tells the reader these lines "are not logcat
  records at all" and are "deliberately not attached to the entry above", while
  the CLI shows them attached as the entry's own message
  (`'line-one\nLINE-TWO-CONTINUATION'`). `threadtime` is unaffected: 500 records
  report `500 in session` with no unparsed clause and no chip.
  The one genuinely orphaned line — a line after a record separator that belongs
  to no record — is discarded with `unknown`, `untimed` and rejected-candidate
  all reading 0, and is not reachable from search. The shipped generator emits
  such a line in every file it writes, so every synthetic corpus loses its last
  line silently.
  `SourceAccountingTests` cannot catch this: it asserts
  `SourceLines == attributed` with `Continuations` in the sum, so a continuation
  that had no record to attach to still balances the books — the same blind spot
  the 2.0.14 changelog identifies for the defect it was written to close.
  **This contradicts the 2.0.14 Play release note**, which claims "lines that are
  not logcat records are counted honestly and listed in full."
- **F-02 · Native crash on the render thread. Major.** `2026-09-18 16:30:00`,
  SIGABRT, `FORTIFY: pthread_mutex_lock called on a destroyed mutex`, backtrace
  `eglCreateWindowSurfaceImpl → Surface::hook_query → lock_shared → abort` — an
  EGL window surface created from an already-destroyed Android Surface. The
  dump's fingerprint is this device and `Process uptime: 158741s` puts process
  start at `2026-09-16 20:24:19`, matching the candidate's install time exactly,
  so this is the candidate's own process. `src/VisualCat.Android` contains no
  surface or EGL code, so the race is in Avalonia.Android 12.1.1. **Not
  reproduced on demand**: 10 background/trim/screen-off cycles and 12 rotation
  cycles left the process alive. Evidence: `crash-buffer-full.txt`,
  `tombstone_20`, dropbox `data_app_native_crash@1789741802038`.

**Not run, and therefore not signed off:** W3–W5 (denial, stale-port and
reconnect recovery), A-19 upgrade, A-22 Play-delivered install, the four-hour
soak, the accessibility schedule, and B-04's raw-source-byte assertion. The
`x86_64` split was not exercised. A-19 was declined deliberately: Play refuses a
downgrade, so it would have required uninstalling the candidate and erasing a
pre-existing capture.

**Hand-back.** `font_scale` 1.0, `accelerometer_rotation` 1, `adb_wifi_enabled`
0, `navigation_mode` 0, night mode on, override density 360 preserved,
`POST_NOTIFICATIONS` still `granted=false`, no `vc18-*` file or MediaStore row,
and an empty crash buffer across the whole Wireless run. The leftover
`com.barebit.visualcat.deletetest` 2.0.12-dev package from 2026-09-05 — a §13.5
miss from an earlier pass — was removed. Left in place: the captures this run
created and the pre-existing `foxtrot · 2026-09-05` capture.

### v2.0.8 — 2026-08-24

- The Google Play upload-key-signed candidate APK was clean-installed on a
  Google Pixel 5 (`redfin`) running Android 14/API 34. It reported application
  ID `com.barebit.visualcat`, version `2.0.8`, version code `20008`, target SDK
  36, and no debuggable flag; its cold launch produced no VisualCat fatal
  exception or ANR.
- Filters, Plot, Split, Details, Follow and Stop capture exported complete,
  aligned 132 px (48 dp) high touch targets in portrait. The earlier current-
  source Pixel evidence records the 49 dp severity-chip reserve, masked numeric
  pairing code, keyboard occlusion, large-text landscape and OEM fallback
  checks added for this release.
- A clean first VisualCat-only Live run requested notification permission once,
  then held exactly one `dataSync` foreground service, one app-owned `logcat`
  reader and one private ongoing notification with a **Stop and save** action.
  Backgrounding and locking the screen for 12 seconds retained the same process
  and all three resources; returning to VisualCat resumed the same capture.
- In-app Stop kept 65 entries, then removed the reader, foreground service and
  notification. A forced process stop and cold launch reopened the same complete
  65-entry session. Automatic rotation and font scale 1.0 were restored after
  testing.
- The AAB and APK passed the release packager's application ID, version, API,
  ABI, 16 KB alignment, permission, foreground-service, signature and pinned
  Google Play upload-certificate checks. The Release manifest contains the five
  audited Wireless/background-capture permissions, the unexported `dataSync`
  service and no `READ_LOGS` declaration.

### v2.0.7 — 2026-08-23

- The production-upload-key-signed APK was clean-installed on a Samsung
  SM-G990B running Android 16/API 36 and reported application ID
  `com.barebit.visualcat`, version `2.0.7`, and version code `20007`. It was
  non-debuggable, cold-started in 1.3 seconds, remained alive, and emitted no
  fatal exception or ANR.
- The first-use Live chooser clearly distinguished recommended Wireless-ADB
  full-device capture from immediate VisualCat-only capture. The latter started
  without setup, received 12 own-app entries, reported its restricted scope and
  quiet behavior accurately, and retained all 12 entries after Stop.
- On the physical phone, Filters, Plot, Split, Details, Follow, and Stop capture
  were complete, vertically centred touch targets. The full-device chooser and
  its pinned actions were fully framed and reachable. Earlier API-34 transport
  evidence in the Android live-test report covers real first pairing, encrypted
  saved reconnect, interruption recovery, external-log ingestion, and disconnect.
- The AAB and APK passed the release packager's application ID, API level, ABI,
  16 KB alignment, version, signature-scheme, permission, and pinned Google Play
  upload-certificate checks. The Release manifest contains the required local
  Wireless ADB permissions and no `READ_LOGS` declaration.
- After testing, the app and its capture data were removed; the same signed APK
  was installed cleanly and opened for hand-back. No log permission was granted,
  and the device remained at its original font scale and rotation settings.

### v2.0.6 — 2026-08-22

- The release-signed APK was installed on a Google Pixel 5 running Android
  14/API 34 and reported application ID `com.barebit.visualcat`, version
  `2.0.6`, and version code `20006`.
- Without `READ_LOGS`, the clean-install explanation accurately said that the
  capture would contain only VisualCat's own records. The capture stayed in
  own-app scope through its quiet heartbeat and finalized cleanly with four
  entries.
- With the adb grant and Android's one-time consent, the same APK resolved to
  full-device scope, initially reported 117 lines/s, and finalized cleanly at
  1,303 entries. Rotation kept the same process and running capture alive.
- In portrait and landscape, Filters, Plot, Split, Details, Fit, Follow and Stop
  capture remained vertically centred, fully labelled and at least 48 dp tall.
  Stop removed the live-only controls, both completed sessions returned after a
  forced process restart, and no crash or ANR was logged.
- The AAB and APK passed the release packager's application ID, API level, ABI,
  16 KB alignment, version, signature-scheme, and pinned Google Play upload
  certificate checks. The temporary `READ_LOGS` grant was revoked after testing.
- The accessibility review, macOS hardware validation, and Windows/macOS code
  signing remain intentionally deferred. The four-hour ADB soak was completed
  during the 2.0.6 development cycle and is recorded in the Android live-test
  report.

### v2.0.5 — 2026-08-21

- The release-signed APK was clean-installed on a Samsung SM-G990B running
  Android 16/API 36 and reported application ID `com.barebit.visualcat`, version
  `2.0.5`, and version code `20005`.
- Without `READ_LOGS`, live capture stayed honestly in own-app scope beyond the
  eight-second decision window, reported a quiet heartbeat after 19 seconds,
  and kept the restricted-scope guidance visible instead of falsely switching
  to full-device.
- With the adb grant and Android's one-time consent, the same build resolved to
  full-device, reported 852 lines at 112/s after seven seconds, and finalized
  cleanly at 1,970 entries. No crash or ANR was logged in either mode.
- The AAB and APK passed the release packager's application ID, API level, ABI,
  16 KB alignment, version, signature-scheme, and pinned Google Play upload
  certificate checks. The temporary `READ_LOGS` grant was revoked after testing.
- The four-hour ADB soak, accessibility review, macOS hardware validation, and
  Windows/macOS code signing remain intentionally deferred.

### v2.0.4 — 2026-08-20

- The release-signed APK was clean-installed on a Motorola edge 60 pro running
  Android 16/API 36 and reported application ID `com.barebit.visualcat`, version
  `2.0.4`, and version code `20004`.
- The More actions sheet exposed its full command set to Android accessibility,
  the system Back gesture closed it, and the first live capture showed the
  privacy and one-time-permission explanation before capture began.
- Full-device capture reached 1,038 entries. The status reported the resolved
  scope, current rate, and quiet heartbeat honestly; capture survived an app
  background/resume cycle, and re-engaging Follow returned to a 30-second live
  edge.
- Light-theme repainting, vertically centred touch controls, Details mode's
  expanded entry list, and the absence of Fit when its plot was hidden were
  visually reviewed on the device. Stop capture finalized cleanly, removed the
  live-only controls, and the named completed capture remained on the home
  screen after a forced process restart. No crash or ANR was logged.
- The AAB and APK passed the release packager's application ID, API level, ABI,
  16 KB alignment, version, signature-scheme, and pinned Google Play upload
  certificate checks. Android left no persistent `READ_LOGS` grant behind.
- The four-hour ADB soak, accessibility review, macOS hardware validation, and
  Windows/macOS code signing remain intentionally deferred.

### v2.0.3 — 2026-08-16

- The release-signed APK was clean-installed on a Motorola edge 60 pro running
  Android 16/API 36 and reported application ID `com.barebit.visualcat`, version
  `2.0.3`, and version code `20003`. Live capture reached 56 entries with a
  visible capture status, then continued updating normally.
- With Follow disabled, entry 59 stayed selected across live snapshots and an
  app background/resume cycle. Its row expanded to show the complete wrapped
  message, and the Entry inspector exposed the full message, copy action, and
  exact source context with the selected raw line clearly highlighted.
- Stop capture finalized cleanly at 163 entries/snapshot 12. No crash or ANR was
  logged during clean install, launch, capture, inspection, resume, or shutdown.
- The AAB and APK passed the release packager's application ID, API level, ABI,
  16 KB alignment, version, signature-scheme, and pinned Google Play upload
  certificate checks.
- The four-hour ADB soak, accessibility review, macOS hardware validation, and
  Windows/macOS code signing remain intentionally deferred.

### v2.0.2 — 2026-08-14

- The release-signed APK was clean-installed on a Motorola edge 60 pro running
  Android 16/API 36 and reported application ID `com.barebit.visualcat`, version
  `2.0.2`, and version code `20002`. Live capture reached its first inspected
  state with 56 entries and a visible capture status instead of an idle prompt.
- Two background/resume cycles preserved the active capture and selected Source
  context. Raw source remained readable while its sidecar grew, Stop capture
  finalized cleanly at 825 entries/snapshot 39, and no crash or ANR was logged.
- The centered workspace controls, filter-driven severity lanes, row severity
  ribbons/tints, and selected-entry/source orientation were visually reviewed on
  the physical device.
- The AAB and APK passed the release packager's application ID, API level, ABI,
  16 KB alignment, version, signature-scheme, and pinned Google Play upload
  certificate checks.
- The four-hour ADB soak, accessibility review, macOS hardware validation, and
  Windows/macOS code signing remain intentionally deferred.

### v2.0.1 — 2026-08-13

- The Android companion was exercised on a Motorola edge 60 pro running
  Android 16/API 36 after the live-capture status and batching changes. The
  application launched, entered capture promptly, displayed incoming own-app
  records, and stopped cleanly without a crash or ANR.
- The four-hour ADB soak, accessibility review, macOS hardware validation, and
  Windows/macOS code signing remain intentionally deferred.

### v2.0.0 — 2026-07-22

- Published Linux x64 CLI and desktop archives passed their checksums and were
  exercised on Ubuntu 24.04 under WSL2/WSLg. CLI indexing, verification,
  statistics, queries, search, and export passed; the desktop completed a
  20-second launch smoke with no missing native dependency or fatal log.
- The published release-signed APK was tested on a Motorola edge 60 pro running
  Android 16/API 36. The installed APK was byte-identical to the release asset,
  cold-launched successfully, and passed both platform-restricted own-app
  capture and an explicitly granted full-device `READ_LOGS` capture. No crash
  or ANR was detected, and `READ_LOGS` was revoked after validation.
- The four-hour ADB soak, accessibility review, macOS hardware validation, and
  Windows/macOS code signing remain intentionally deferred. See the
  [first-release plan](../FIRST-RELEASE-PLAN.md) for exact versions, hashes, and
  observed throughput.

## Release rehearsal

Run the release workflow with `workflow_dispatch` before creating the first
tag. Dispatch runs derive `<VersionPrefix>-preview.<run>` from the checkout and
never publish a GitHub release or push to NuGet, so the packaging path can be
exercised repeatedly and safely.

Download and inspect the resulting artifacts: Windows, Linux, Intel macOS, and
Apple-silicon macOS desktop archives; the matching CLI archives; the `.nupkg`;
the SBOM; `SHA256SUMS`; and the APK if Android release signing is enabled.
Test at least the primary Windows artifact and one Unix artifact on a clean
machine. Confirm that checksums, file permissions, archive names, embedded
versions, license files, and the documented launch steps agree.

Reproduce the same file layout locally with:

```shell
pwsh ./tools/package.ps1 -Runtime win-x64,linux-x64,osx-x64,osx-arm64 -Archive
```

When this runs on Windows, cross-built Unix tarballs are layout checks only:
Windows cannot faithfully create or validate Unix executable mode bits. The
Linux and macOS workflow runners are authoritative for permissions. Run the
command from a normal PowerShell session; on Windows, `tools/package.ps1`
selects the system `tar.exe` explicitly and uses a relative archive filename so
Git for Windows' GNU `tar` cannot misread a drive-qualified path as `host:path`.

Keep a short release record noting any item intentionally deferred, especially
code signing, notarization, physical Android testing, and soak tests.

Tag only after a rehearsal you were satisfied with. A tag starts publication and
should represent artifacts that are ready to keep available; do not create one to
make a badge green.

For the stable release, promote the current changelog entries to the dated
`## [MAJOR.MINOR.PATCH]` section and merge that commit before tagging it.
`tools/verify-docs.ps1` permits exactly the declared `VersionPrefix` to be staged
as the pending release while its tag does not yet exist; older released sections
and tag links remain checked. After CI is green on that commit, create the
annotated tag on the same commit and push it. This avoids an intentionally red
branch build and ensures the tag points at a commit already tested on `main`.

## First public release only

These cannot be represented in the working tree and must be done once, in the
GitHub repository settings.

### Before changing visibility

- [ ] Repository description, topics (`android`, `adb`, `logcat`,
      `log-analysis`, `avalonia`, `dotnet`, `desktop`, `local-first`), and
      `docs/assets/social-preview.jpg` as the social preview are set.
- [ ] Issues are enabled; the wiki stays disabled while repository Markdown is
      the documentation source of truth; Discussions only if the maintainer
      wants another channel to monitor.
- [ ] The default branch and owner match the hard-coded URLs in package
      metadata, badges, security links, and `CODEOWNERS`.
- [ ] Stale automation branches and pull requests are resolved or deleted, so
      the initial activity feed shows current work rather than release
      preparation residue.
- [ ] A maintained scanner (Gitleaks or TruffleHog) has been run over all
      reachable refs, in addition to `tools/scan-secrets.ps1`, and every
      finding is classified rather than blindly suppressed. If a real
      credential ever entered Git, revoke or rotate it first — rewriting
      history is not sufficient.

### After changing visibility

- [ ] Private vulnerability reporting is enabled, so the links in
      `docs/SECURITY.md` and the issue chooser work.
- [ ] Secret scanning and push protection are enabled.
- [ ] Dependabot alerts and security updates are enabled, in addition to the
      existing scheduled version updates in `.github/dependabot.yml`.
- [ ] GitHub Actions defaults to read-only permissions, and first-time external
      contributor workflows require approval.
- [ ] A `main` ruleset blocks force-push and deletion and requires the stable CI
      checks. For a single-maintainer project, keep an explicit administrator
      bypass so a ruleset mistake cannot deadlock the repository, and never
      require a check that only runs conditionally — such a check leaves pull
      requests permanently pending.
- [ ] Every README badge and link works from a signed-out browser, checked once
      immediately and again after badge caches refresh.

### Restoring hidden badges

The README deliberately shows only badges that report real data. Restore each
one only after it has a successful public run to report, and never accept
`unknown`, `repo not found`, or a stale red state as a launch artifact.

- **CodeQL** — trigger `codeql.yml` manually, confirm the analysis and the
  result upload both succeed on `main`, then add:

  ```markdown
  [![CodeQL](https://github.com/benny-cz/VisualCat/actions/workflows/codeql.yml/badge.svg?branch=main)](https://github.com/benny-cz/VisualCat/actions/workflows/codeql.yml)
  ```

  Pin it to `main` but not to `push`, so a failed scheduled scan stays visible.

- **Codecov** — authorize the public repository, confirm OIDC is accepted, and
  confirm a processed report exists for the current `main` commit. During
  diagnosis set `verbose: true`, `disable_search: true`, and
  `fail_ci_if_error: true` on the upload step. Then add:

  ```markdown
  [![non-UI coverage](https://codecov.io/gh/benny-cz/VisualCat/branch/main/graph/badge.svg)](https://codecov.io/gh/benny-cz/VisualCat)
  ```

  Do not commit a private Codecov badge token to make a badge render. Decide the
  steady-state failure policy separately: the most transparent setup is a
  non-required coverage-upload job whose own failures are visible and whose
  Codecov action uses `fail_ci_if_error: true`, keeping a provider outage from
  blocking builds without silently claiming a successful upload. The local HTML
  coverage artifact remains the provider-independent result.

- **Release** — restore only after a signed-out visitor can see a real release:

  ```markdown
  [![Release](https://img.shields.io/github/v/release/benny-cz/VisualCat?display_name=tag&sort=semver)](https://github.com/benny-cz/VisualCat/releases)
  ```

`tools/verify-docs.ps1` fails if the release badge reappears while the
repository has no tags, or if the dynamic Shields license badge replaces the
static one.

## Maintainer identity

Reviewed 2026-07-22 and accepted deliberately: the maintainer's real name and
personal address appear in `LICENSE`, package metadata, `CODE_OF_CONDUCT.md`,
and human commit metadata. VisualCat is a single-maintainer project and a
conduct report should reach a person rather than an unmonitored alias. Security
reports go through GitHub private vulnerability reporting instead.

Revisit this only with a plan: changing it after publication requires rewriting
every human commit's author and committer metadata with a purpose-built tool,
updating or deleting every remote branch, and coordinating the force-push while
there are no public forks or contributor clones. A `.mailmap` changes display in
some tools but does not remove addresses from commit objects.

## Google Play

The Android companion is published to Google Play as `com.barebit.visualcat`.
The Play AAB uses Wireless debugging for full-device Live capture and must not
declare `READ_LOGS`. Debug and controlled non-Play builds can opt into the old
direct permission path, so never infer the Play manifest from a Debug APK.
`docs/PLAY-LISTING.md` is the source of truth for every field of the store
listing and every app-content answer; Play Console is where it is pasted, not
where it is decided.

Build and verify the upload artifact locally:

```shell
pwsh ./tools/package-android.ps1 -Format both `
  -Keystore <path>/visualcat-upload.keystore -KeyAlias visualcat-upload -StorePassword <secret>
```

This produces `artifacts/android/VisualCat-Android-v<version>.aab` for Play and
the matching `.apk` for direct installation, then proves what Play checks after
upload: application ID, versionCode, versionName, `minSdkVersion`,
`targetSdkVersion`, 64-bit code, a real release signature, and 16 KB page
alignment of every shipped ELF. It also rejects any signing key except the
registered VisualCat upload certificate
(`SHA1 37:5C:8D:64:4F:BF:BD:07:DE:4C:1A:71:95:10:6C:94:4B:C6:B8:14`). The
release workflow runs the same script and
uploads the bundle as the separate `google-play-bundle` artifact, deliberately
not as a public release asset — the bundle is not installable and Play re-signs
it, so offering it for download would only confuse.

Before the first submission:

- [ ] The upload keystore and its passwords are backed up off the build machine.
      With Play App Signing, Google holds the app signing key and can reset a
      lost upload key, but the reset still blocks every update until it
      completes.
- [ ] Play App Signing is enabled with a Google-generated app signing key.
- [ ] `docs/PLAY-LISTING.md` matches what is actually in Play Console, including
      the privacy policy URL and the data-safety answers.
- [ ] The store assets in `artifacts/play/` were regenerated from a capture of
      the exact build being submitted.
- [ ] The pre-launch report on the internal or closed track shows no crash, ANR,
      or accessibility blocker before promoting to production.

A personal Play developer account registered after 13 November 2023 must run a
closed test with at least twelve testers opted in continuously for fourteen days
before it can apply for production access. Check which case applies before
planning a launch date; on an affected account, the first upload starts a
two-week clock rather than a release.

Renaming the application ID is a one-way door: Play permanently binds a listing
to its package name, and a rename means a new listing with no reviews, no
install base, and no upgrade path from the old app.

## Signing and publication secrets

Android releases are installable APKs signed by a persistent release key. Set
the `ANDROID_RELEASE_ENABLED` repository variable to `true` and configure these
GitHub Actions repository secrets before tagging:

- `ANDROID_KEYSTORE_BASE64` — base64-encoded keystore bytes;
- `ANDROID_KEYSTORE_PASSWORD`;
- `ANDROID_KEY_ALIAS`;
- `ANDROID_KEY_PASSWORD`.

The workflow fails closed if any signing value is absent and never uploads an
unsigned, debug-signed, or wrong-certificate Android package. Back up the
keystore and passwords outside GitHub; losing the key prevents users from
upgrading an installed APK. Without `ANDROID_RELEASE_ENABLED`, the workflow
deliberately ships desktop, CLI, NuGet, SBOM, and checksum assets without an APK.

Set `NUGET_API_KEY` to publish the `VisualCat.Cli` global-tool package to
nuget.org. Without it, the `.nupkg` is still built, checksummed, and attached to
the GitHub release for inspection and manual publication.

Desktop signing, macOS notarization, store submission, physical-device
validation, and multi-hour soak gates require release infrastructure or hardware
and must be recorded explicitly when deferred. The published CycloneDX SBOM
covers the desktop solution's resolved packages; it does not enumerate the
embedded .NET runtime or Android-only dependency graph.
