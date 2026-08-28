# VisualCat — Android live-test report

Live execution of [`ANDROID-LIVE-TEST-PLAN.md`](ANDROID-LIVE-TEST-PLAN.md)
against a physical Android device.

> **Transport scope:** sections 1–11 predate the Play-oriented Wireless ADB
> transport and exercised the older direct path with externally granted `READ_LOGS`
> where noted. Their evidence remains authoritative for those builds. Section 12
> is the first physical-device pass for Wireless debugging pairing, encrypted
> identity storage, saved reconnect, transport resume, Stop cleanup, and the
> Release manifest's removal of `READ_LOGS`.

**Status: COMPLETE through F-48 / §23 — all implementation and physical-device
steps are closed; remaining limits are declared, not promoted to passes.**
Results are written continuously, including across interrupted test processes.
Completed passes and declared gaps remain authoritative.

Sections 1–20 record the original run, its remediation and the subsequent
independent device continuations. §5.1 remains the single status table for every
finding, and the newest pass is always the last section.

This report is context-agnostic. Device identity, artifact provenance, oracles,
and observations are all recorded here; nothing depends on a previous session.

---

## 1. Run header

| Field | Value |
|---|---|
| Run id | `20260821-edge60pro` |
| Date/time (UTC) | 2026-08-21 17:32 → 20:20 (resumed after interruption at 18:39) |
| Host UTC vs device UTC | identical to the second at pre-flight (`Fri Aug 21 17:32:42 UTC 2026`) |
| Repository commit | `b56fb5a` — *Make Stop capture answer the press and keep answering it* |
| Working tree | no product-source changes; untracked test plan and this report only after temporary handoff deletion |
| Artifact | `src/VisualCat.Android/bin/Release/net10.0-android36.0/com.barebit.visualcat-Signed.apk` |
| Artifact SHA-256 | `78f7023baf66110d7f71c09fa1a62dd29944aabc832b51b6cf792f8d5ba5c6c9` |
| Artifact bytes | 30 270 907 |
| Build/artifact class | **Locally built Release** (§2.3 middle row) — *not* the production-signed candidate |
| Signing certificate | `CN=Android Debug, O=Android, C=US`, SHA-256 `e58d3c4526abac2286bde04d560d761d9e0271d7c97cc132a8e68e27bc55470d` |
| Device | motorola edge 60 pro (`motorola`) |
| Serial | `ZY22M4T2Z4` |
| Android release / API | 16 / 36 |
| ABIs | `arm64-v8a` |
| Build fingerprint | `motorola/cybert_g_syse/cybert:16/W1VVS36H.7-108-8-6/cf0b3-79ad00:user/release-keys` |
| Screen / density | 1220 × 2712 px, density 450 (2.8125 px/dp), cutout `Rect(565,0–655,128)` |
| Refresh rate | 60/90/120 Hz supported; active mode 90 Hz primary, render 60 Hz |
| Navigation mode | `0` — **three-button navigation** |
| Locale / time zone / 12-24 h | device zone `Europe/Prague` (**UTC+2** in August), `time_12_24 = null` (12 h shown) |
| Font scale / theme / animations | `font_scale 1.0`; `uimode night = yes` (**dark**); window/transition scale 1.0, animator scale `null` |
| Battery / thermal | 93 %, USB powered, `Thermal Status: 0` (none), CPU 67.5 °C at pre-flight |
| Free space `/data` | 209 306 676 KB ≈ **199 GiB** free (10 % used) |
| App versionName / versionCode | `2.0.5` / `20005`, `minSdk 31`, `targetSdk 36` |
| Install mode | **clean** (`uninstall` then `install`) — CLEAN profile |
| `pkgFlags` | `[ HAS_CODE ALLOW_CLEAR_USER_DATA ]` — **not** `DEBUGGABLE`, so `run-as` is expected to fail |
| Resolved activity | `com.barebit.visualcat/crc64a1973b883a99125a.MainActivity` |
| ADB | `1.0.41` / `35.0.1-11580240` |
| Host OS / SDK | Windows 11 Pro 26220, .NET SDK per `global.json` (`10.0.101`, rollForward `latestFeature`) |
| Schedule executed | Standard, extended into A/H/P/X and U where a single device permits |
| Evidence root | `artifacts/live-test/20260821-edge60pro/evidence/` |
| Interrupted-session handoff | `docs/output.md` was recovered as a temporary transcript, reconciled into this report, and deleted per the device owner's instruction |

### 1.1 Declared coverage gaps

These are **gaps, not passes** (§1.4, §13.4/2).

1. **One device only.** One OEM, one API level (36), one form factor, one ABI
   (`arm64-v8a`). Nothing is claimed for API 31–35, for `x86_64`, for tablets or
   foldables, or for another OEM's power management.
2. **Not the production-signed candidate.** The artifact under test is a locally
   built Release signed with the Android debug key. Functional and UX findings
   transfer; signing, Play delivery (A-22), and certificate provenance do not.
3. **Three-button navigation only for most passes.** Gesture-navigation insets
   (U-07) are only partly covered.
4. **Dark theme is the device default.** Light-mode passes are performed by
   switching, not by a cold device default.
5. **No true system-locale/RTL or split-screen transition.** App-locale
   configuration changes (`ar`, `cs`) were exercised while live, but Android
   denied the protected system-locale broadcast; A-13's split-screen branch was
   also not run.
6. **No hands-on assistive-technology session.** Accessibility trees and touch
   geometry were audited, but TalkBack gestures, Switch Access, hardware
   keyboard, and repeated screen-reader announcements were not exercised.
7. **No endurance, severe-pressure, or destructive-storage pass.** X-05/X-06/
   X-07, genuine competing-app memory pressure, cache/database corruption,
   ENOSPC, reboot/doze, battery-saver, and force-stop-during-finalization remain
   unexecuted. Synthetic trim signals alone are not a pressure result.
8. **No upgrade, Play, or host-parity matrix.** Play installation/signing,
   upgrade/migration, desktop and CLI parity, and second-device/OEM comparison
   are outside this run.
9. **ACTION_VIEW coverage is bounded.** Exact-URI redelivery through Android's
   Downloads provider passed in a warm activity. Cold delivery, a second
   provider, revoked/stale grants, and malicious metadata remain unexecuted.

### 1.2 Corpus manifest

Generated on the host with
`vcat generate-test-log --seed 42`, CLI version `2.0.5+b56fb5a380fee6a2b807986b0f5637904a3384a7`.
Pushed to `/sdcard/Download/`. `large.txt` and `small.txt` were pulled back and
compared by SHA-256 — **transport verified byte-identical**.

| File | Lines | Bytes | SHA-256 (host = device) |
|---|---:|---:|---|
| `tiny.txt` | 1 000 | 90 384 | — |
| `small.txt` | 50 000 | 4 500 749 | `1f35340b8882324c76352d87457d924a7a34325e573e69ea76ead9a7638b869c` |
| `medium.txt` | 250 000 | 22 501 180 | — |
| `large.txt` | 1 000 000 | 90 017 930 | `59ba73453fa5c9c6a642733ff5f6361d3666df18bbb7f5a5dd0114c79f280223` |

**`small.txt` oracle** (host `vcat index` + `vcat stats`, the authority for every
exact-count assertion below):

```text
total lines      50 000
parsed           49 994      unknown 6      untimed 0
templates        77
firstInstant     2026-05-15T12:13:36.771+00:00   (= 14:13:36.771 at UTC+2)
lastInstant      2026-05-15T12:14:51.957+00:00   (= 14:14:51.957 at UTC+2)
levels           V 8469  D 8233  I 8253  W 8477  E 8213  F 8349  Unknown 0
```

Adversarial corpora built by a recorded, reproducible Python recipe from
`small.txt`/`medium.txt` (seeded, so byte-reproducible): `crlf.txt`, `bom.txt`,
`nonutf8.bin`, `truncated.txt` (cut mid-line at a recorded offset),
`longline.txt` (one 2 MB message), `continuations.txt` (Java stack traces),
`nofinalnewline.txt`, `empty.txt` (0 bytes), `notalog.bin` (10 MB random),
`crashy.txt` (`FATAL EXCEPTION` blocks with the unique needle
`VCATCRASHMARKER` at three known line offsets), `outoforder.txt`.

### 1.3 Mutation ledger

| Setting | Original | Changed to | Restored |
|---|---|---|---|
| `android.permission.READ_LOGS` | not granted (P0, package dump) | granted with `pm grant` for P1 at ≈18:20 UTC | **Yes — revoked at ≈18:55 UTC; package line absent and the old PID ended** |
| Display rotation | portrait, auto-rotate enabled | landscape for B-09/U-01/U-03, then portrait | **Yes** |
| System font scale | `1.0` | `1.3`, `0.85`, `1.15`, `1.0`, plus a combined `1.3 × 1.10` in-app multiplier pass | **Yes — system `1.0`, in-app `1.00×`** |
| App appearance | Follow system, high contrast off | Light; Light + high contrast; Dark + high contrast; Follow system | **Yes — Follow system, high contrast off** |
| Shared export | absent for this run | `/sdcard/Download/On-device logcat 20-48-26.csv` (1,359 rows + header; SHA-256 `fbccf2b5…`) | **Yes — exact path deleted** |
| Shared export | absent for this run | `/sdcard/Download/On-device logcat 20-48-26-all.csv` (1,765 rows + header; SHA-256 `393a8e86…`) | **Yes — exact path deleted** |
| One-entry crash corpus | absent | `/sdcard/Download/vcat-one-line.txt` (162 bytes: one `threadtime` record plus logcat divider) | **Yes — exact path deleted** |
| Diagnostic bundle export | absent | `/sdcard/Download/visualcat-diagnostics-20260821-213942.zip` (48,297 bytes; SHA-256 `a2e5e54e098c51b465c4b48af547f28594f87265a5c1588b9f2a49a66841c6d8`) | **Yes — host evidence retained; exact device path deleted** |
| Temporary-session cleanup policy | disabled; 30 days; unlimited size | enabled temporarily; age reduced to 1 day; delete-eligible exercised (0 eligible) | **Yes — disabled; 30 days; unlimited, reopened and verified** |
| Test corpora and on-device captures | pre-existing Downloads preserved | 15 generated corpora, two CSV exports, one diagnostic ZIP, one one-line crash corpus, and 153 run-created top-level XML/PNG captures | **Yes — all verified run-created paths deleted; pre-existing Downloads left intact** |
| App-private test data | clean-install profile, then 46 test sessions / 289.41 MiB plus settings and diagnostics accumulated during this run | exact package `com.barebit.visualcat` uninstalled after A-15/A-16, then the same recorded APK reinstalled for remaining tests | **Yes — final `adb uninstall` succeeded; package and process absent** |
| System/app locale | system `en-US`; app override `[]` | system setting briefly `ar` (protected locale-change broadcast was denied); app override `ar`, then `cs` | **Yes — system `en-US`; app override cleared before uninstall** |

---

## 2. Progress ledger

Resume at the first row without a result.

| ID | Result | One-line outcome |
|---|---|---|
| Pre-flight | **Pass** | Device identified, supported (API 36, `arm64-v8a`), 199 GiB free |
| B-01 | **Pass** (1 finding) | Cold start median 1 587 ms; empty state correct; no desktop commands |
| B-02 | **Pass** (1 finding) | After close-all + force-stop + cold launch, three retained sessions appear with distinct names, date/time, size, and completion state; one tap reopens (F-17 affects accessibility metadata) |
| B-15 | **Pass** | Back closed exactly one layer for More sheet, Appearance dialog, and filter drawer; with all layers closed it fell through to the Motorola launcher while the process stayed warm |
| B-03 | **Pass** | 49 994 entries — exact oracle match; heat map drawn; tab named `small.txt` |
| B-04 | **Pass** (1 note) | Cell → entries → entry → raw bytes all agree; gutter index is 0-based (F-08) |
| B-05 | **Pass** | Fatal-only = 8 349, exact oracle match; `Clear all` restores 49 994 exactly |
| B-06 | **Fail** | Counts exact (7 181), highlights correct; **marker navigation unreachable by touch** (F-07) |
| B-07 | **Fail** | Pathological pattern survives (NonBacktracking); invalid pattern leaks a resource key and sticks (F-04, F-05) |
| U-13 (partial) | **Fail** | Zero-result filter renders a blank ~700 px pane (F-06) |
| B-08 | **Pass** | Double-tap zooms only (R-32 ✅); pan stays bounded; Fit returns exactly 49 994 |
| B-09 portrait | **Pass** | Modes cycle; Split 4 rows, Details 7 rows (R-06 ✅) |
| B-09 / U-01 landscape | **Fail** | Split 3 rows, Details 4 rows, last row clipped by the status line (F-09) |
| U-03 portrait | **Pass** | Footer rises above the IME; action key applies and closes (R-16 ✅) |
| U-03 landscape | **Fail** | IME covers the whole drawer including *Reset*/*Done* (F-10, F-11) |
| U-04 / U-05 / R-05 | **Pass** | One live PID survived system scales 1.3 → 0.85 → 1.15 → 1.0; the 1.3 × 1.10 in-app multiplier was visibly larger; labels, one-row mode selector, Follow, and Stop stayed present; exact original settings restored |
| U-06 / U-19 / R-27 / R-28 | **Pass** | Light, light-high-contrast, dark-high-contrast, and system-dark repainted immediately during one capture without PID change or cross-variant system bars. High contrast added a non-colour selection outline; VisualCat stayed blue/cyan while the Android picker used the device's loud red accent |
| R-17 | **Pass** | Minimap survives the landscape short viewport (59 px in the plot column) |
| R-33 | **Pass** | Axis labels stay inside the plot in both orientations and at deep zoom |
| R-02 | **Pass** | Status, source description, notice and *Session info* all agree on scope in P0 and P1 |
| R-04 | **Pass** | PIDs parsed correctly under `-v threadtime,year,UTC,usec`; zone offset never read as a PID |
| R-07 | **Pass** (1 note) | Three captures distinguishable by start time everywhere; name reads as a date (F-16) |
| R-14 | **Pass** | Pre-prompt shown once and remembered |
| R-36 | **Pass** | Facet counts name their scope on screen: `COUNTS · WHOLE SESSION · CURRENT FILTER` |
| §4.2 live CPU | **Fail / soak still required** | Matched idle samples were 0%; one low-volume P0 capture sampled 17.8–25.0%, while a later quiet run sampled 3.4% and 11.1%. The refresh cost is bursty rather than a proven constant floor; X-05/X-06 remain unexecuted (F-15) |
| P4 durability subpass | **Pass** | `pm grant` killed the app; both tabs and the finalized 1 984-entry session restored intact |
| B-10 / P2 | **Fail** | P0/P2 are honestly own-app and the exact grant remedy exists in *Session info*, but the pre-prompt falsely promises a sheet that cannot appear in P0 and the notice omits the verbatim remedy (F-13) |
| B-11 | **Pass** (2 findings outside its scope oracle) | P1 sheet appeared; allow produced full-device scope within seconds; 11,646 entries over 4 minutes; foreign PIDs parsed; stop/finalize succeeded |
| P2 | **Fail** | Declining keeps the capture own-app and explicitly says access was not allowed, but the notice omits the required “Tap Live again and allow” remedy and points to a differently named pane (F-13) |
| P3 / R-03 | **Pass** | Sheet remained focused after 31 s; late allow resolved directly to full-device and delivered foreign-process records; the decision clock did not latch at subprocess spawn |
| P4 / P-06 / X-08 (process-death branch) | **Fail** | Revoke killed PID 12206; 1,173 committed entries survived and Recents says `interrupted`, but automatic restore says `Ready` while Session info says `Importing`, with no partial warning (F-19) |
| B-14 (partial) | **Pass so far / receiver read Blocked** | Both offered CSV scopes exported exactly (1,359 and 1,765 data rows); sensible default name; system chooser shows one `.vcat.zip`; installed provider authority is `com.barebit.visualcat.files`. No controlled receiver was available to prove read-grant consumption without transmitting logs to a personal/cloud target |
| B-12 | **Blocked (latency) / functional Pass** | 6 min 31 s capture: first captured post-tap frame showed `Stopping · 0s · compacting`; finalized by the next frame with 1,984 entries and controls gone; 493 ms screencap cadence cannot prove the ≤250 ms acknowledgement gate |
| B-13 | **Fail** (functional path passes) | Both empty-state and *Recent sessions* routes reopen exact counts in <2 s, and process death lost no session; one intermediate frame says `Ready · 11,646 entries` over a blank list (F-18) |
| B-16 direct / quiet | **Fail** (quiet-source subpass passes) | Capture starts and first data appear, but running live *Session info* reports `State: Importing` (F-14). In a separate P0 run the heartbeat advanced from `no source lines for 3s` to `1m 59s` with the count fixed at 2 and the UI responsive; queued-start remains |
| A-09 / R-15 | **Pass** | Attempting to reopen VisualCat's own exported CSV produced a plain-language *Import failed* card with the detected-format reason, relevant format guidance, and working *Open another log* / *Close this tab* actions; no raw exception or hollow workspace |
| X-01 / R-37 | **Fail** (integrity path passes) | First 90.02 MB import completed at the exact 999,885-entry oracle while mode switching and a live search remained responsive; measured PSS rose from 283 MiB to 1,069 MiB during ingest and settled to 474 MiB. On a repeat, double-tapping the plot when its progressive snapshot held one entry crashed the whole app (F-20) |
| A-23 / R-20 | **Pass** | At UTC+2, live rows render around the device-local clock, Follow remains at the live edge, and *Session info* names `Europe/Prague · captured as UTC` |
| R-25 (stored-session names) | **Fail** | Empty-state recent-capture buttons expose the full app-private session path as `content-desc` (F-17); entry-row/TalkBack subpasses remain |
| X-25 (partial) | **Fail** | Live-tail start and broad buffer acquisition were observed, but per-entry buffer attribution is silently wrong for most merged-buffer records (F-12) |
| X-24 | **Fail — 30/30 completed** | All 30 selected sessions finalized without a visible failure or lingering Stop control: 0.5 s ×4, 1 s ×5, 2 s ×5, 3 s ×5, 5 s ×4, 10 s ×4, 15 s ×3. Four one-entry results say `1 entries kept` (F-21). The cache and process table then exposed an older, hidden capture still running; a controlled double-Live repro created two app-owned `logcat` children and stopping the selected session left the first alive (F-22) |
| R-22 | **Pass** | In a deliberately quiet P0 capture, the status kept advancing the explicit `no source lines` duration through 1m 59s without inventing rows or implying that the capture had stopped |
| A-14 / R-23 | **Pass** | Follow remained operable through live refresh and settings changes; re-engaging it reset the plot to a 30-second live-edge window, and Stop removed Follow from both the screen and accessibility tree |
| A-15 | **Fail** (safety path passes) | Disabled cleanup rejected deletion and left all 46 sessions / 289.41 MiB intact; enabled cleanup with a 1-day threshold reported `Deleted 0 eligible sessions`; the original disabled/30-day/unlimited policy was restored and verified. The irreversible confirmation does not say how many sessions or bytes it will remove (F-23); exact size reconciliation is Blocked on this non-debuggable artifact |
| A-16 / R-11 | **Pass** | Confirmation accurately names excluded and retained data. The saved 48,297-byte ZIP has 15 safe relative entries, no traversal names, and no serial, source/storage paths, corpus names, raw markers, searches, or 64-hex hashes; session display/source names are redacted |
| R-24 | **Pass** | After an authorized clean reinstall, the empty state has no share/export link; in More, Share and Export CSV are disabled and their accessible descriptions append `needs an open session` |
| R-10 (tree) / R-26 | **Pass** (TalkBack interaction remains) | With More open, all nine sheet buttons are in the accessibility tree; disabled commands explain why. No empty-state, toolbar, or workspace control exists in the modal tree behind the scrim |
| R-08 | **Pass** | Default export order and normalized CSV encoding render as in-place segments on the phone. Choosing Chronological kept the form at the same scroll position and switched the checked segment; Cancel preserved the stored default |
| R-29 | **Pass** | A fresh untouched `small.txt` import ended fitted to the full 1.25-minute oracle span with 49,994/49,994 entries in view; no first-interaction handoff was needed |
| R-34 / U-18 | **Fail** (load-more geometry itself passes) | Loading the next 500 kept Copy raw at exactly `[723,1479][935,1614]`, but its completion notice immediately moved it to `[723,1319][935,1454]`. In 2/2 rapid double-tap attempts, tap two at the original coordinate opened the Entry inspector instead of repeating Copy (F-24) |
| A-18 / R-09 (Copy raw subpass) | **Pass / clipboard readback Blocked** | Copy raw produced the scoped notice `Copied the raw text of 1 entry.`; Android denies shell clipboard readback, and no external/networked app was used to disclose even synthetic clipboard contents |
| R-31 | **Pass** | Both the finished finite import and a still-writing live sidecar resolved to source bytes; neither stayed on a loading line or needed Retry |
| A-24 / R-39 | **Fail** (resume workaround passes) | In Follow mode, a selected entry and its caret are cleared as soon as the moving 30-second window ages that entry out; the Entry pane falls to `No entry selected` (F-25). With Follow deliberately off, the same selected identity and live source bytes survived a 62 s background interval and rotation |
| R-40 | **Pass** | With Follow off to isolate reattachment from F-25, live source context highlighted exact source line 66 while capture appended; after 62 s background/resume and activity rotation, entry `21:51:12.630139` and the same source context remained readable |
| U-11 / R-35 | **Partial Pass / system-locale route Blocked** | Applying app configurations `ar` and `cs` kept PID 21332 and the capture alive, LTR usable, and product numbers fixed (`11.08 min`, `734.9 ms/px`, ISO/English entry text). Android denied the protected system locale-change broadcast, so a true whole-device RTL transition was not asserted; both system/app locale settings were restored exactly |
| A-13 (configuration-change subset) | **Partial Pass** | Rotation, system text scale, theme and app-locale configuration all preserved the live PID, Follow/Stop controls, selected identity (with Follow off), and capture tense. System-locale and split-screen branches remain uncovered |
| U-08 (geometry) | **Fail** | Main toolbar/mode/analysis controls meet 48 dp, but every session tab and its close target is only 123–124 px = 43.7–44.1 dp at the recorded 450 dpi density (F-26). TalkBack gesture-repeat subpass remains uncovered |
| R-30 | **Pass** | PID 21332 survived closing a completed `small.txt` tab and an active `large.txt` ingest at its 900,001-entry progressive snapshot; the prior tab remained usable and no current-window `ObjectDisposedException`/crash appeared |
| A-06 (Downloads `content://` subset) | **Pass** (1 finding; cold/second-provider branches remain) | Files/Downloads delivered `tiny.txt` to the already-running activity and 1,000/1,000 entries imported. Redelivering exact URI `content://com.android.providers.downloads.documents/document/msf%3A1000000323` to `onNewIntent` left the two-title tree byte-for-byte unchanged—no duplicate. The provider name is mangled in the tab/accessibility label (F-27) |
| X-09 (trim-signal subset) | **Pass / genuine-pressure branch remains** | A ready 999,885-entry session and PID 21332 survived `RUNNING_MODERATE`, `RUNNING_LOW`, and `RUNNING_CRITICAL` in sequence with exact counts and usable rows. PSS was 432,408 KiB before and 437,570 KiB after; the app did not shed measurable memory in this foreground sample. Heavy-app pressure was not induced |
| A-05 (three-tab layout subset) | **Fail** | Three sessions remain independently selectable, but the selected rightmost tab is not fully auto-scrolled into view: its close node is clipped from 124 px to 43 px (15.3 dp), compounding F-26. File/live/portable state-isolation parity remains incomplete |
| Final cleanup / hand-back | **Pass** | Revoked `READ_LOGS`, force-stopped and uninstalled `com.barebit.visualcat`, removed 153 verified run-created top-level captures and 19 exact Download targets, preserved pre-existing Downloads, and re-verified locale/font/rotation/theme, package/process absence, device state, storage, and power |

---

## 3. Findings

Severity per §13.3: **Blocker** / **Major** / **Minor** / **Polish**.
Every finding has been checked against Appendix B.

### F-01 · A Release build of unreleased code calls itself `2.0.5`, exactly like the shipped release

- **Severity** Major · **Scenario** B-01 / R-38 · **Reproducibility** 1 of 1, deterministic
- **First suspicion** build/versioning (`Directory.Build.props:19`)

`Directory.Build.props` applies the `-dev` suffix only outside Release:

```xml
<VersionSuffix Condition="'$(Configuration)' != 'Release' And '$(VersionSuffix)' == ''">dev</VersionSuffix>
```

`VersionPrefix` is `2.0.5` and the `2.0.5` tag has already shipped, so **every
Release build made from any commit after that tag reports `versionName=2.0.5`**
and paints `VisualCat 2.0.5 · local-first · no telemetry` on the empty state.
The build under test contains the entire `[Unreleased]` changelog section —
including the Stop-capture rewrite that R-01 and R-18 exist to guard — and is
indistinguishable, by any user-visible string, from the release that does not
contain it.

R-38 is worded as *"the identity line's version matches the installed
`versionName`, and a non-release build says so in the version itself."* The
first clause passes. The second is only satisfied by `Configuration != Release`,
which is precisely the configuration nobody tests a release candidate in.

The CLI in the same tree does not have this problem — `vcat --help` prints
`2.0.5+b56fb5a380fee6a2b807986b0f5637904a3384a7`, commit and all.

**Why it matters.** A bug report with a screenshot is the main channel by which
these defects arrive. "VisualCat 2.0.5" on a screenshot currently cannot answer
"which build?", and a tester validating a release candidate cannot prove from the
device that they installed the candidate rather than the previous release.

**Suggested fix.** Make the suffix depend on *release-ness*, not on
`Configuration`. Drive it from an explicit release signal that CI sets and a
developer build does not — e.g. `-p:ReleaseChannel=stable`, or the presence of a
tag matching `VersionPrefix`:

```xml
<!-- a Release build that is not a tagged release is still not the release -->
<VersionSuffix Condition="'$(VersionSuffix)' == '' And '$(ContinuousIntegrationBuild)' != 'true'">dev</VersionSuffix>
```

and put the source-revision short hash into the Android identity line the way
the CLI already does, so a screenshot names its build:
`VisualCat 2.0.5+b56fb5a · local-first · no telemetry`. `versionCode` should also
advance for a candidate, otherwise `install -r` upgrade tests (A-19) cannot tell
the two builds apart either.

### F-02 · `vcat generate-test-log --help` silently writes a 90 MB file

- **Severity** Minor · **Scenario** §3.1 test-data preparation · **Reproducibility** 1 of 1
- **First suspicion** CLI argument parsing

```shell
$ vcat generate-test-log --help
C:\...\scratchpad\corpus\synthetic-logcat.txt      # 90,017,930 bytes, ~14 s
```

`--help` is not recognised as a request for help and not rejected as an unknown
option; it falls through to the command's defaults (1 000 000 lines, seed 42,
`synthetic-logcat.txt` in the working directory). The top-level `vcat --help`
works correctly, so the inconsistency is per-command.

**Why it matters.** The first thing a tester following §3.1 does with an
unfamiliar command is ask it for help; the plan's own §3 says the corpus must be
reproducible from a recorded recipe, and a command that does work when asked to
explain itself invites unrecorded files into the corpus directory. Any unknown
option is silently ignored, so a typo like `--lines1000` produces a million-line
file instead of an error.

**Suggested fix.** In the CLI argument loop, treat `-h`/`--help`/`-?` on any
subcommand as "print that subcommand's usage and exit 0", and treat an
unrecognised `--option` as an error with exit code 2 rather than ignoring it.
Both behaviours should be shared by every subcommand, not added per command.

### F-03 · Empty-state hero links are 18.8 dp tall — well under the 48 dp touch-target floor

- **Severity** Minor · **Scenario** B-01 / U-08 · **Reproducibility** deterministic, measured from the accessibility tree
- **First suspicion** view (empty-state hero link styling)

Measured from `uiautomator dump` bounds, converted with the device's recorded
density (450 dpi ⇒ 2.8125 px/dp):

| Control | Bounds (px) | Size (px) | Size (dp) | 48 dp? |
|---|---|---|---|---|
| `Open log` (command band) | `[28,249][315,384]` | 287 × 135 | 102 × **48** | ✅ exactly |
| `Live` (command band) | `[335,249][537,384]` | 202 × 135 | 72 × **48** | ✅ exactly |
| `More actions` (command band) | `[556,249][755,384]` | 199 × 135 | 71 × **48** | ✅ exactly |
| `OPEN LOG` (hero) | `[207,1579][370,1632]` | 163 × 53 | 58 × **18.8** | ❌ |
| `ON-DEVICE LIVE` (hero) | `[424,1579][667,1632]` | 243 × 53 | 86 × **18.8** | ❌ |
| `RECENT CAPTURES` (hero) | `[721,1579][1011,1632]` | 290 × 53 | 103 × **18.8** | ❌ |

The three hero links are the empty state's own call to action — on a first run
they are the *only* thing inviting a tap besides the command band — and their
hit rect is the text rect. They are also mutually adjacent with ~19 dp of gap
occupied by a `·` separator, so a slightly low tap on `ON-DEVICE LIVE` lands on
nothing and a slightly wide one is ambiguous.

**Why it matters.** 48 dp is the platform's stated minimum and the plan's U-08
gate. At 18.8 dp these are approximately a third of it, in the one screen a
first-time user meets.

**Suggested fix.** Give the hero links a transparent padded hit area rather than
enlarging the visible text: wrap each in a container with `MinHeight="48"` and
~8 dp vertical padding, keeping the underline/º baseline where it is. The
command band already demonstrates the correct 48 dp geometry, so the token
exists; the hero row is simply not using it.

### F-04 · Release builds show framework exception messages as resource keys — `MakeException, (unclosed, 9, InsufficientClosingParentheses`

- **Severity** Major · **Scenario** B-07, and latent in A-09, U-13, X-02, X-10, P-12
- **Reproducibility** 3 of 3 · **First suspicion** build configuration, then view

**Steps** Import `small.txt` → *Filters* → tick *Regex* → type `(unclosed` → apply.

**Actual**, verbatim from the status line:

```text
Failed · MakeException, (unclosed, 9, InsufficientClosingPar…
```

(clipped by the one-line status bar; the untruncated string in the accessibility
dump is `Failed · MakeException, (unclosed, 9, InsufficientClosingParentheses`.)

**Expected** per B-07: *"an invalid pattern produces a clear message and no crash."*

**Root cause — and it is bigger than regex.** The Release Android build ships
with `System.Resources.UseSystemResourceKeys = true`, confirmed by decoding the
generated `VisualCat.Android.runtimeconfig.json.bin`:

```text
System.Resources.UseSystemResourceKeys  →  true
```

That is the .NET Android SDK's Release default (framework resource strings are
trimmed to save size). With it set, `SR.Format` returns
`"{ResourceKey}, {arg0}, {arg1}, …"` instead of the sentence. So `MakeException`
is the *resource key*, `(unclosed` is the pattern, `9` is the parse offset and
`InsufficientClosingParentheses` is a `RegexParseError` enum member.

**This is not a regex-specific defect.** Every framework exception message the
app puts in front of a user degrades the same way in Release, and only in
Release — Debug builds and `dotnet test` show the real sentences, which is
exactly why it survived. `WorkspaceViewModel.FriendlyMessage`
(`src/VisualCat.App/Presentation/WorkspaceViewModel.cs:680`) ends in

```csharp
_ => Shorten(cause.Message),
```

and `IOException` / `UnauthorizedAccessException` interpolate `cause.Message`
directly, so the plan's own gates that forbid a raw exception (X-10 *"the failure
message is a raw exception"*, A-09, U-13) are all one unlucky code path away.

**Additional defects visible in the same screenshot**

1. The chip bar shows `regex = (unclosed` as an **active filter**, and the
   entries list simultaneously shows all **49,994** rows unfiltered. The product
   claims a filter it did not apply. A pattern that cannot compile should be
   refused at the point of entry, not accepted as a filter that silently matches
   everything.
2. The accessibility description of that same status line still reads
   `Ready · 49,994 entries` — see F-05.

**Suggested fixes**, in order of value:

1. **Validate the pattern at the input, not at the query.** `Regex` construction
   already happens in `SessionQueryEngine.CompileSearchRegex`; add a
   `TryCompileSearchRegex(TextSearchSpec, out Regex, out RegexParseError, out int offset)`
   and have the filter drawer call it on apply. On failure keep the drawer open,
   mark the query field invalid, and say it in product language:

   > Not a valid regular expression: there are more "(" than ")" (position 9).

   Map `RegexParseError` to sentences — the enum has ~20 members and each maps to
   one plain clause. This is self-contained and removes the dependency on
   framework message text entirely.
2. **Never render `exception.Message` to a user in Release.** Give
   `FriendlyMessage`'s `_ =>` arm a generic product sentence plus a stable error
   code, and route the raw text to the diagnostic bundle instead.
3. If framework sentences are genuinely wanted, set
   `<UseSystemResourceKeys>false</UseSystemResourceKeys>` in
   `src/VisualCat.Android/VisualCat.Android.csproj` — but treat that as a safety
   net, not the fix: a BCL sentence is still not product language.
4. Add a Release-configuration regression test asserting that no user-facing
   failure string matches `^[A-Za-z_][A-Za-z0-9_]*, ` — the signature of a
   resource-key leak.

### F-05 · Five routes write the status line behind `ApplyStatusText`, so the message goes sticky and the screen reader is told the opposite

- **Severity** Major · **Scenario** B-07, U-17, R-25 · **Reproducibility** 4 of 4
- **First suspicion** view (`SessionWorkspaceView`)

After the invalid-regex failure above, the status line
`Failed · MakeException, (unclosed, 9, InsufficientClosingParentheses` **never
cleared**. Measured, in one session tab, in this order:

| Action after the failure | Entries shown | Status line |
|---|---:|---|
| Apply regex `(a+)+$` (succeeds) | 423 | `Failed · MakeException, (unclosed, …` |
| Apply literal `Rendering surface` | 7 181 | `Failed · MakeException, (unclosed, …` |
| *Clear all* — no filters at all | 49 994 | `Failed · MakeException, (unclosed, …` |
| Switch workspace mode | 49 994 | `Failed · MakeException, (unclosed, …` |

Throughout, `uiautomator dump` reports the node's accessible name as
`Ready · 49,994 entries`. **The sighted user sees a permanent failure; a TalkBack
user hears a permanent "Ready".** They are never both right.

**Root cause.** `SessionWorkspaceView.Presentation.cs:148` documents the
invariant:

```csharp
/// … Every route that changes the status now comes through here, and an
/// explicitly set HelpText is a property change the platform follows …
private void ApplyStatusText()
{
    var status = _viewModel.Status ?? string.Empty;
    _status.Text = status;
    AutomationProperties.SetName(_status, status);
    AutomationProperties.SetHelpText(_status, status);
    …
}
```

Five routes do not come through there. They assign `_status.Text` directly:

| Location | Written text |
|---|---|
| `SessionWorkspaceView.Interactions.cs:1104` | `"Cancelled"` |
| `SessionWorkspaceView.Interactions.cs:1108` | `$"Failed · {exception.GetBaseException().Message}"` |
| `SessionWorkspaceView.Interactions.cs:1135` | `$"Failed · {exception.GetBaseException().Message}"` |
| `SessionWorkspaceView.RawContext.cs:786` | `$"Copied {entry.Message.Length:N0} characters"` |
| `SessionWorkspaceView.RawContext.cs:1423` | `$"Source unavailable · {exception.Message}"` |

Each bypass causes both symptoms at once:

- **Divergence** — only `Text` is written, so `AutomationProperties.Name/HelpText`
  keep the last value `ApplyStatusText` set. This is precisely the defect the
  remark above records as already fixed once ("audit 2, B2": *a finished session
  went on being announced as "Starting capture"*), reintroduced through a
  different door.
- **Stickiness** — `ApplyStatusText` only rewrites the `TextBlock` when
  `_viewModel.Status` changes. The view model's `Status` never changed (it stayed
  `Ready · 49,994 entries`), so no later refresh overwrote the directly poked
  text. It survives for the life of the tab.

**Suggested fix.** Make the invariant enforceable rather than documented:

1. Add `SessionTabViewModel.ReportTransientStatus(string text)` that sets the
   view-model `Status`, so every message flows through `ApplyStatusText` and both
   the visible text and the accessible name move together.
2. Replace all five direct `_status.Text = …` assignments with it, and make
   `_status` reachable only through `ApplyStatusText` (wrap it in a small
   `StatusLine` type whose only mutator takes the view model's value). A
   convention that five call sites already broke will be broken again.
3. Transient statuses need an **expiry**: a failure from a superseded query
   should clear as soon as the next query succeeds. Tie the transient text to the
   query generation that produced it — `SessionQueryEngine` already stamps
   `queryGeneration` — and drop it when a newer generation lands.
4. Add a headless view test: apply an invalid regex, then a valid one, and assert
   the status line and `AutomationProperties.GetName` are equal and no longer
   mention the failure.

### F-06 · A filter that matches nothing renders a ~700 px blank pane

- **Severity** Minor · **Scenario** U-13 · **Reproducibility** deterministic
- **First suspicion** view (entries pane empty state)

With a filter that matches no rows the plot behaves well — each severity row is
labelled `0`, the grid is drawn empty, the counts read
`0 in view · 0 match · 49,994 in session` and the status bar says
`0 search matches`. The **entries list**, the largest region on screen, becomes
an empty rectangle roughly 700 px tall with no text, no explanation and no
action. Its accessibility node is an empty `ListBox` named `Filtered log
entries`, so a screen-reader user is told nothing at all.

U-13 requires that every empty/partial/failed state *"explains itself in product
language and offers the next action"* and fails explicitly if any *"renders as a
blank pane"*.

**Suggested fix.** Render an empty-result card in the list area with the reason
and the two actions that exist: *"No entry matches these filters. 49,994 entries
are in the session."* with *Clear all filters* and *Widen the time range*
buttons — the failure-card component from A-09 already provides the shape. Also
give the empty `ListBox` an accessible name that states the count, so the
announcement matches the screen.

### F-07 · Search-marker navigation is keyboard-only, so it is unreachable on a phone

- **Severity** Major · **Scenario** B-06, U-10 · **Reproducibility** deterministic
- **First suspicion** view (workspace command surface)

[`KEYBOARD.md`](KEYBOARD.md) defines marker navigation as `F3`/`N` and
`Shift+F3`/`Shift+N`, and the function itself is correct — injecting
`KEYCODE_F3` at a 2.35 s zoom span moved the viewport to the next match and
**preserved the span exactly** (`DENSITY · 2.35 s · 2.6 ms/px` before and
after). But on the Android companion there is no touch route to it:

- no *next match* / *previous match* control anywhere in the command band, the
  chip bar, the filter drawer, or the *More* sheet;
- the magenta marker lane drawn under the plot is **not tappable** — a tap at
  `(1100, 1055)`, inside the lane, changed **0 pixels**;
- the accessibility tree contains no marker-navigation node, so TalkBack and
  Switch Access cannot reach it either.

With `Rendering surface` applied, the product tells the user there are
`7,181 search matches` and then offers a phone user no way to step through them.
The only route to a specific match is scrolling the entries list, which is a
different affordance with a different ordering.

**Why it matters.** B-06 is a *basic* scenario — the plan puts marker navigation
in the same row as search itself, and §1.3 counts *"a required control or
capability being absent"* as a Fail. It also blocks U-10's "every action is
operable without touch **and** without a keyboard" pairing from being satisfiable
in both directions.

**Suggested fix.** Put the marker stepper where the match count already is. The
status bar already renders `7,181 search matches`; make that a small cluster:

```text
◀   3 / 7,181   ▶        (48 dp targets, disabled with a reason when no search is active)
```

Wire both buttons to the same commands `F3`/`Shift+F3` already invoke, give them
`AutomationProperties.Name` of *"Previous search match"* / *"Next search match"*,
and announce the new position politely so U-17 is satisfied. Making the marker
lane itself tappable — jump to the nearest match at the tapped time — is a
natural second affordance and costs one hit-test.
### F-08 · The source-context gutter is 0-based, so it disagrees with every line-numbering tool

- **Severity** Minor · **Scenario** B-04 · **Reproducibility** deterministic
- **First suspicion** view (source-context rendering)

The *Entry → Source context* pane marks the selected record `19328`:

```text
 19327 en │ 05-15 14:14:06.015000  1415  5843 V chatty          : Cache contains 10340 entries
▶19328 en │ 05-15 14:14:06.015000  1876  4674 E ActivityManager : Cache contains 28427 entries
 19329 en │ 05-15 14:14:06.015000 18026  2303 E chatty          : FATAL EXCEPTION: main
```

The bytes are **correct** — a binary range read of `small.txt` confirms the
selected entry, its timestamp `14:14:06.015`, its pid pair `1876:4674` and its
message all match. But the record is physical line **19329** of the file, not
19328; the numbering is 0-based, verified at four separate offsets (displayed
19323/19328/19333 ↔ physical 19324/19329/19334, a constant +1).

The pane's own accessible description calls the column a *"source sequence
number"*, so it is internally self-consistent, and unknown lines do consume
sequence numbers, so the mapping is exactly `physical = displayed + 1`.

**Why it matters.** The entire purpose of this pane is to let someone cross-check
the app against the file. Every tool they will reach for to do that is 1-based:
`sed -n 19328p`, `grep -n`, an editor's *Go to line*, `awk NR==`. Each lands one
record early — on a neighbouring line that, in a log, looks plausible.

**Suggested fix.** Display `sequence + 1` in the gutter and label the column
*line* — the sequence stays 0-based internally where the store needs it. If the
0-based value must stay visible, put both on the header row
(`line (seq+1)`), but a single 1-based number matching the file is the honest
presentation.

### F-09 · Landscape: the entries list drops below its row floor and the last row is clipped by the status line

- **Severity** Major · **Scenario** B-09, U-01, R-06 · **Reproducibility** deterministic
- **First suspicion** view (workspace layout in the short viewport)

R-06 requires *"≥ 4 rows in Split, ≥ 6 in Details"*. Rotated to landscape
(2712 × 1220, ≈ 434 dp of height — inside U-02's compact-height band), measured
from `uiautomator dump` on the same `small.txt` session:

| Mode | Orientation | Rows visible | Floor | Result |
|---|---|---:|---:|---|
| Split | portrait | 4 | 4 | ✅ |
| Details | portrait | 7 | 6 | ✅ |
| Split | **landscape** | **3** | 4 | ❌ |
| Details | **landscape** | **4** | 6 | ❌ |

In landscape *Details* the fourth row occupies `[213,1045][2475,1180]` while the
status `DockPanel` starts at `y = 1167` — a **13 px overlap**, visible in the
screenshot as the last row (`W ActivityManager · FATAL EXCEPTION: main`) running
into the status line with its lower border cut. U-01 fails on *"anything is
clipped or overlapped in any combination"* independently of the row count.

There is also space to reclaim: in landscape the command band and the
`Filters · Plot · Split · Details · Fit` row occupy only the leftmost ~35 % of
the width, leaving roughly 1 700 px of empty header. The height that costs is
exactly the height the list is short of.

**Suggested fix.**

1. Make the row floor a real constraint rather than a consequence: give the
   entries `ListBox` a `MinHeight` of `floor × rowHeight` for the current mode
   and let the *plot* yield height in the short viewport, which is what the split
   is for. Then clamp so the list can never start above the status bar.
2. Reserve the status line's height in the layout (`DockPanel.Dock="Bottom"` set
   before the list is measured) so no row can ever be measured into it.
3. In landscape, collapse the command band and the mode selector onto one row —
   they already fit side by side in a third of the width — and give the ~135 px
   saved to the list. That alone buys one row in Split.

### F-10 · Landscape: the soft keyboard covers the whole filter drawer, including *Reset* and *Done*

- **Severity** Major · **Scenario** U-03, R-16 · **Reproducibility** 2 of 2
- **First suspicion** view (drawer IME inset handling)

U-03 requires *"The drawer's Reset and Done stay above the keyboard."*
Measured footer position with and without the IME, same drawer, same session:

| Orientation | IME hidden | IME shown | IME top edge | Verdict |
|---|---|---|---|---|
| Portrait | `Reset [768,2314][948,2449]` | `Reset [768,1499][948,1634]` | ≈ 1700 | ✅ footer moved above the keyboard |
| **Landscape** | `Reset [2125,1054][2305,1189]` | `Reset [2125,1054][2305,1189]` | ≈ 522 | ❌ **footer did not move at all** |

In portrait the drawer reflows correctly — the footer rises by 815 px and stays
clear, so R-16 passes there. In landscape the drawer does not react to the IME
at all: the keyboard's top edge lands at y ≈ 522 and *Reset*, *Done*, the
severity toggles, the time lens and **all but the top 23 px of the query field
itself** are behind it. The user types blind and cannot reach the button that
applies the filter without dismissing the keyboard first.

The keyboard's own action key still works, so the drawer is not unusable — but
the affordance the plan names is fully occluded, and there is no visual cue that
*Done* exists.

**Suggested fix.** The portrait path already does the right thing, so this is a
missing inset subscription in the landscape composition rather than new
behaviour: apply the same bottom `SafeAreaPadding`/IME inset to the drawer's
container in both size classes. In the short viewport additionally make the
drawer body scrollable with the footer pinned, so *Reset*/*Done* stay visible
even when the remaining height is smaller than the drawer's natural size.

### F-11 · Landscape: the query placeholder is truncated without an ellipsis, and its accessible bounds overflow the field

- **Severity** Polish · **Scenario** U-03, U-01 · **Reproducibility** deterministic

In landscape the query field is `[204,499][648,634]` (444 px wide) while its
placeholder `Search message text or regex…` reports accessible bounds
`[235,519][781,617]` — 546 px, overflowing the field by 133 px and nominally
overlapping the *Clear the query* button at `[670,499][755,634]`.

Visually the text is clipped by the field, so nothing is painted over the
button — the rendering is correct and only the reported node bounds are wrong.
But the visible result is `Search message text` with the *"or regex…"* silently
gone and no ellipsis to show that anything was cut, which matters because
*"or regex"* is the only place the interface says the field accepts a pattern.

**Suggested fix.** Give the placeholder `TextTrimming="CharacterEllipsis"` so the
cut is visible, and shorten the landscape placeholder to something that fits —
`Search or regex…` is 17 characters and says the same thing. Setting the
placeholder's measured width to the field's width also corrects the accessible
bounds, which currently mislead automated layout checks into reporting an
overlap that is not there.

### F-12 · A full-device capture attributes about 80% of its records to the wrong buffer

- **Severity** Major (silent wrong results) · **Scenario** X-25, A-02, H-06
- **Reproducibility** 2 of 2 captures plus a direct `logcat` reproduction
- **First suspicion** source/parser/ingest (`OnDeviceLogSource.cs:150`, `LogcatParser.cs:37`)

**Observed.** A four-minute P1 capture finished with 11,646 entries. Its
*Insights → Facets → Buffers* group reported:

| Buffer claimed by VisualCat | Entries | Share |
|---|---:|---:|
| `radio` | 9,376 | 80.5% |
| `events` | 2,225 | 19.1% |
| `system` | 39 | 0.3% |
| `main` | **6** | **0.05%** |

The same session contained 960 `HfLooper` entries and the generated
`VCATTEST` traffic. A direct per-buffer probe proved both exist only in `main`:

```text
buffer=main    VCATBUFTEST=1  HfLooper=47
buffer=system  VCATBUFTEST=0  HfLooper=0
buffer=crash   VCATBUFTEST=0  HfLooper=0
buffer=events  VCATBUFTEST=0  HfLooper=0
buffer=radio   VCATBUFTEST=0  HfLooper=0
buffer=kernel  VCATBUFTEST=0  HfLooper=0
```

The product nevertheless rendered those `main` records with buffer `radio`.
Evidence: `X-25-facets.xml`, `X-25-facets-buffers.xml`, and the P1 entry rows in
`A-23-follow-edge.png`.

**Root cause.** The on-device source runs one merged stream:

```text
logcat -b all -T 1 -v threadtime,year,UTC,usec
```

The stream emits a `--------- beginning of <buffer>` divider when it begins
reading a buffer; ordinary `threadtime` records carry no buffer field. The
parser turns a divider into buffer metadata and ingest latches it for all
following records. In an eight-second sample, the only dividers were lines 1
(`main`) and 6 (`events`); 284 of 290 lines followed the last divider, so 98%
would inherit the same false buffer.

**Impact.** Per-entry buffer labels and the Buffers facet are wrong, so include/
exclude filters silently select the wrong entries. The product also cannot use
its stored buffer labels as X-25 or H-06 evidence.

**Suggested fix.** Start one probed reader per supported/readable buffer, stamp
its records at the source, and merge by timestamp. Until that exists, store the
buffer as unknown for merged records, hide/disable the Buffers facet with an
explanation, and never present guessed attribution as fact. Add a regression
fixture with two dividers followed by interleaved records. **Trap check:** this
is not an ADB targeting or permission artefact; identity was re-proved, P1 was
held, and the per-buffer ground-truth probes ran on the same serial and window.

### F-13 · The P0 pre-capture explanation promises a consent sheet that cannot appear

- **Severity** Major · **Scenario** B-10, §2.4, R-14
- **Reproducibility** 2 of 2 · **First suspicion** view (`MainView.cs:1433–1444`)

The first-run dialog says, verbatim:

> Android will now ask you to allow access to device logs. It asks every time,
> because the permission it grants is one-time.

Every later capture similarly says *“Android will ask you to allow log access.
It asks on every capture.”* The copy is unconditional.

| State | `READ_LOGS` | Sheet observed | Copy accuracy |
|---|---|---|---|
| P0 clean install | not granted | none; no `LogAccessDialogActivity` focused | **false** |
| P1 after `pm grant` | granted | `com.android.systemui.logcat.LogAccessDialogActivity` | true |

The product contradicts itself: P0 *Session info → Log scope* correctly says
Android cannot prompt for wider access because `READ_LOGS` is not a runtime
permission and provides the exact `adb shell pm grant …` command. The initial
dialog tells the same user to wait for a prompt that cannot arrive. The P0
notice also says only *“See the session details for how to widen it”* even though
the pane is labelled *Session info* and B-10 requires the command in the notice.
Evidence: `B-10-live-preprompt.png/.xml`, `B-10-session-info.png`,
`B-11-t6.png`. P2 exposes the same navigation/copy defect after an explicit
decline: *“Only VisualCat's own log lines are being captured — log access was
not allowed. See the session details for how to widen it.”* The capture and
decline diagnosis are correct, but §2.4 requires the immediate remedy *“Tap Live
again and choose the option that allows access.”* Evidence:
`P2-consent-sheet.png`, `P2-running-18s.png`.

**Suggested fix.** Compose the explanation from the actual grant state. With
the grant, explain the one-time sheet. Without it, explicitly say the capture
will contain only VisualCat's own lines, that no sheet will appear, show the
exact grant command, and provide a *Copy command* action. Use *Session info*
consistently across the notice and navigation. **Trap check:** §2.4 and Appendix
B explicitly distinguish the development grant from the consent sheet; package
state and focused activity were captured for both states.

### F-14 · Session info reports `State: Importing` while a live capture is running

- **Severity** Minor; it is a literal B-16 failure · **Scenario** B-16
- **Reproducibility** 2 of 2 · **First suspicion** ingest state stamping

During both P0 and P1 live captures, the status line correctly said `Capturing`
while *Insights → Session info* said:

```text
Source          Android · On-device logcat 20-09-12
Format          ThreadTime
Times shown in  Europe/Prague · captured as UTC
State           Importing
```

After stop the row became `Ready`. B-16 explicitly fails when a live capture is
described as an import. The domain already has distinct `Importing`,
`Connecting`, `Streaming`, `Paused`, and `Stopping` states, but the pane renders
the raw enum with `$"{descriptor.State}"`.

**Suggested fix.** Stamp a live source `Streaming`, transition it through
`Stopping`, and map internal states to stable product phrases such as
*Capturing*, *Finishing*, *Ready*, and *Recoverable partial*. Do not expose raw
enum names. Evidence: `B-10-session-info.png` and the running P1 status in
`B-11-12s.png`. **Trap check:** the capture was visibly active and receiving
records; this is not stale private-storage inspection or an ADB state.

### F-15 · An untouched live capture shows bursty CPU use and can mostly record its own render noise in P0

- **Severity** Major risk for X-05/X-06 · **Scenario** §4.2 live refresh, X-05, X-06
- **Reproducibility** 13 CPU samples across matched active/idle states
- **First suspicion** live view/surface refresh loop

Like-for-like samples used the same process and open session, screen on, USB
powered, with `Thermal Status: 0`:

| State | `%CPU` samples | `Surface::disconnect` in 10 s |
|---|---|---:|
| No capture, untouched | 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 | 0 |
| P0 live, about 5 lines/s, untouched | 17.8, 17.8, 25.0, 17.8, 21.4 | about 50 |
| P0 live, source quiet for two minutes | 3.4 at the one-minute checkpoint; 11.1 in a one-second sample immediately before Stop | not sampled |

During capture, `uiautomator dump` repeatedly failed with `ERROR: could not get
idle state`; it succeeded after stop. Logcat reported the app's SurfaceView at
about 4 fps and `Surface::disconnect` about five times per second. In 30 seconds
the live Templates pane counted 149 `Surface::disconnect` records. The 6 min
31 s P0 capture held 1,984 entries and was dominated by render-surface and GC
messages provoked by the app itself—the capture observes its own exhaust.

A second, intentionally quiet P0 run adds an important qualification: high CPU
was not a stable floor. With the entry count fixed at 2, one spot sample was
3.4% and a later one-second sample was 11.1%; a coarse five-minute
`dumpsys cpuinfo` window rounded the young process to zero and is not comparable.
The app still updated its elapsed-no-source heartbeat once per second, so this
run supports a bursty-refresh concern but not the stronger claim that every
untouched capture continuously consumes about 20% CPU.

This is a release risk rather than a completed soak verdict: X-05/X-06 have not
yet run. Sustained CPU at this rate can turn into battery drain, heat, and
process death over four to twelve hours.

**Suggested fix.** Trace why the surface is disconnected/reconfigured on live
ticks; invalidate existing visual nodes instead of rebuilding the surface.
Refresh only when snapshot generation or viewport changes, coalesce arrivals to
the configured refresh ceiling, suppress rendering while backgrounded/screen
off, and render once on resume. Add a screen-off ≤10-lines/s CPU/battery soak
budget against the same idle baseline. **Trap check:** thermal throttling was
absent, samples were taken on one identified PID, and the idle control dropped
to zero immediately after stop.

### F-16 · Capture names such as `20-09-12` look like dates, not start times

- **Severity** Polish · **Scenario** B-02, R-07
- **Reproducibility** deterministic · **First suspicion** presentation/naming

Captures started at 20:09:12 and 20:21:06 are named `On-device logcat 20-09-12`
and `On-device logcat 20-21-06`. R-07's uniqueness intent is met, but the first
looks like 12 September 2020. Middle truncation compounds it:
`On-device log…t 20-09-12` hides the word that identifies the source.

**Suggested fix.** Display `20:09:12` (escaping only the filesystem form) or an
unambiguous `20h09m12`; include the date for non-today captures. Preserve source
and time in compact chips with deliberate end/middle segments rather than blind
middle truncation. Evidence: `B-02-empty-after-kill.png/.xml` and
`X-25-facets.xml`. **Trap check:** both names came from distinct, finalized
sessions and survived process death.

### F-17 · Empty-state capture cards expose an app-private path to accessibility services

- **Severity** Minor · **Scenario** B-02, U-09, R-25
- **Reproducibility** 3 of 3 retained cards · **First suspicion** view/accessibility naming

After close-all, force-stop, and cold launch, the visible recent-capture cards
are clear and useful. Their accessibility nodes are not. For example:

```text
text="Reopen On-device logcat 20-21-06, 2026-08-21 20:25 · 5.38 MiB · complete"
content-desc="/data/user/0/com.barebit.visualcat/files/VisualCat/Sessions/
20260821-182106-On-device logcat 20-21-06-4213f0105e144c4eadfa8f7e2ee44a43.vcat"
```

All three cold-start cards expose a private `/data/user/0/...` path and session
GUID. The in-page *Recent VisualCat sessions* dialog does not, demonstrating
that the path is not needed for selection. R-25 explicitly forbids screen-reader
record dumps, session GUIDs, and private-storage paths.

**Suggested fix.** Keep the storage path only in the command parameter/model.
Set the card's accessible name to the same human summary shown on screen and its
help text to an action phrase such as *“Double tap to reopen this complete
capture.”* Add an Android accessibility-tree regression asserting that no node
under the empty-state recent list contains `/data/`, `files/VisualCat`, or a
session GUID. Evidence: `B-02-cold-recents.xml`. **Trap check:** this is the
installed app's own accessibility metadata, not a `run-as` diagnostic or an ADB
path printed by the test harness.

### F-18 · Reopen briefly says `Ready` while the entries pane is blank

- **Severity** Minor · **Scenario** B-13, U-13
- **Reproducibility** 1 of 1 instrumented reopens · **First suspicion** view-model/view publication order

Reopening the 11,646-entry finished capture from the cold empty state completed
well inside the five-second budget, but the captured sequence was contradictory:

| Screenshot start after tap | Visible state |
|---:|---|
| 0.30 s / 0.68 s | cold empty state still visible |
| 1.09 s | session chrome mounted; entries pane blank; status `Ready · 11,646 entries` |
| 1.47 s | entry rows visible with `1,132 in view · 11,646 match · 11,646 in session` |

The blank/Ready frame persisted for one 380–500 ms screenshot interval. B-13's
literal fail condition is a reopened session showing an empty list under Ready,
and U-13 requires loading states to explain themselves rather than render as a
blank pane.

**Suggested fix.** Publish a `Reopening…`/`Loading entries…` activity state
before mounting the workspace, then transition atomically to `Ready` only after
the first page and plot snapshot are bound. If the list intentionally streams
later, render a progress/placeholder card and keep the count status in the
loading tense. Add a frame-sequence regression that forbids `Ready` while both
the first-page collection and explicit empty-result state are absent. Evidence:
`B-13-reopen-02.png` through `B-13-reopen-04.png`. **Trap check:** the same
session populated without retry by the next frame and retained the exact count,
so this is neither corruption nor provider latency.

### F-19 · A process-killed capture is `interrupted` in Recents, `Ready` in the workspace, and `Importing` in Session info

- **Severity** Major · **Scenario** P4, P-06, X-08, U-13
- **Reproducibility** 2 of 2 interrupted acquisitions (one controlled permission kill, one app crash)
- **First suspicion** recovery state mapping across store, view model, and view

With a P1 full-device capture visibly running, `pm revoke … READ_LOGS` returned
success and killed PID 12206 within 500 ms. After a cold relaunch:

- the committed 1,173 entries reopened correctly;
- *Recent VisualCat sessions* correctly called the session `interrupted` and
  explained that an interrupted capture contains only what reached disk;
- the automatically restored workspace said `Ready · 1,173 entries`, with no
  partial/interrupted notice or degraded treatment;
- *Session info* said `State: Importing`, despite there being no source process,
  import, or capture running.

The data recovery path works, but the three user-facing sources disagree about
whether acquisition completed. A user entering through automatic restoration
can reasonably treat `Ready` as a complete capture and never visit Recents to
discover that its tail is missing. This violates X-08/P-06's requirement that
interrupted work be clearly identified as partial.

**Suggested fix.** Make completion/recovery a single persisted domain fact—not
three independently translated states. On open, map an unfinished manifest to
one explicit `RecoverablePartial` state and surface it everywhere: status line,
notice lane, Session info, Recents, tab semantics, and portable export metadata.
The workspace should say, for example, *“Interrupted · 1,173 entries recovered ·
capture ended when the app stopped”* and offer *Keep*, *Export recovered data*,
and *Delete*. Never map an unfinished stored descriptor to `Ready` or retain the
live ingest enum `Importing` after no reader exists. Add a process-kill test that
asserts the same completion label on all surfaces after two relaunches.

The F-20 crash supplied an independent recovery check. The second `large.txt`
import had published one row immediately before the crash and reopened at its
last 100,001-entry snapshot. Its plot and rows were usable, but the workspace
again called it `Ready` with no interrupted/partial warning. This strengthens
the state-mapping finding while confirming progressive snapshot durability
across an exception exit.

Evidence: `P4-before-revoke.png`, `P4-after-relaunch.png/.xml`,
`P4-session-info.png/.xml`, `P4-recents.png/.xml`, and
`F-20-after-relaunch.png/.xml`. **Trap check:** the serial and
old PID were recorded; the process demonstrably ended; the permission was
actually revoked; and this was not `am kill` failing against a foreground app.

### F-20 · Double-tapping the plot during the first progressive entry crashes the app

- **Severity** Blocker · **Scenario** X-01, R-37, U-18
- **Reproducibility** 2 of 2 double-taps (one progressive import, one finished one-entry file)
- **First suspicion** timeline view's zoom bounds

During a repeat one-million-line import, the workspace first showed
`Queued · waiting for a free reading slot`, then published one parsed entry and
reported `Reading · 1 entries ready`. A normal double-tap in the plot at that
point immediately returned the device to the launcher. Android independently
classified PID 13802's exit as `reason=4 (APP CRASH(EXCEPTION))`, importance
100, RSS 642 MiB. The crash buffer is unambiguous:

```text
FATAL EXCEPTION: main
System.ArgumentOutOfRangeException: maximumSpanUs … 2 … minimum … 905
at VisualCat.App.Timeline.TimelineTransform.Zoom
at VisualCat.App.Timeline.TimelineControl.OnPointerPressed
```

The progressive one-entry session spans only about 2 µs, while the phone's
physical-pixel resolution produced a 905 µs minimum useful viewport. The wheel
route already guards this exact relationship with
`maximum = Math.Max(minimum, MaximumSpan(session))`; the double-tap, pinch,
keyboard `+`/`-`, and `ZoomAtCenter` routes pass `MaximumSpan(session)` without
that guard. `TimelineTransform.Zoom` correctly rejects the inverted contract,
but the resulting exception escapes the UI thread and terminates the app.

**Suggested fix.** Centralize construction of effective zoom bounds in one
method and make every gesture/keyboard caller use it. For a session shorter than
one physically meaningful span, keep the Fit-expanded viewport and treat
zoom-in as a bounded no-op (or zoom within that expanded viewport); do not clamp
back to a two-microsecond range and never throw from an input handler. Add UI
tests for one-entry and same-timestamp progressive snapshots across double-tap,
pinch, wheel, keyboard, and programmatic zoom, plus a live device regression
that double-taps before entry two arrives and asserts the PID survives. The
invariant belongs at the shared call boundary so a fifth zoom route cannot omit
it again.

Evidence: `X-01-rerun2-t0.7.png`,
`X-01-rerun2-entry-during-ingest.png`, `X-01-rerun2-plot-during-ingest.png`,
`X-01-rerun2-exit-info.txt`, `X-01-rerun2-crash-buffer.txt`, and
`X-01-rerun2-focused-logcat.txt`. **Trap check:** the screenshot after the
gesture showed the launcher, `dumpsys activity exit-info` recorded an app crash
rather than LMK/user navigation, and the crash buffer names the exact touch
handler and rejected values.

The defect then reproduced without the million-line workload. A 162-byte file
containing one `threadtime` record imported successfully and rendered
`DENSITY · 1 µs · 1.1 ns/px`, with the same instant printed at both ends of the
axis. One double-tap again returned to the launcher; PID 15217 vanished and a
second exit-info row recorded `APP CRASH(EXCEPTION)` at 462 MiB RSS. This rules
out memory pressure, concurrent ingestion, and an unfinished snapshot as
necessary conditions. It also shows that the intended R-37/Fit clamp is not
being applied to the initial viewport of a finished one-entry import.

Additional evidence: `F-20-one-line-source.txt`,
`F-20-repro-one-entry-before.png/.xml`,
`F-20-repro-one-entry-after.png`, and
`F-20-repro-one-entry-exit-info.txt`.

### F-21 · One-entry stop summaries say `1 entries kept`

- **Severity** Polish · **Scenario** X-24
- **Reproducibility** 4 of 4 one-entry results in 30 short captures
- **First suspicion** view-model status-string construction

Cycles 18, 22, 25, and 30 each finalized normally with exactly one parsed
entry, but the visible and accessibility status was `Stopped · 1 entries kept`.
The neighbouring 2- and 3-entry cases are grammatically correct, so this is a
plural-selection defect rather than a corrupt count. It is exposed repeatedly
by ordinary quiet P0 captures and makes the final confirmation look unfinished.

**Suggested fix.** Centralize count phrasing (`no entries`, `1 entry`,
`N entries`) or use an ICU/plural-aware resource rather than interpolating the
noun independently at each status site. Apply it to stopped, duration-ended,
unprompted-ending, export, and recovery notices, and unit-test 0, 1, 2, and a
thousands-formatted value. Evidence: `X-24-18-3s.xml`,
`X-24-22-5s.xml`, `X-24-25-10s.xml`, and `X-24-30-15s.xml`.

### F-22 · Starting Live again leaves multiple captures running; stopping the visible one leaves the hidden one alive

- **Severity** Major · **Scenario** X-24, B-16
- **Reproducibility** 1 incidental sweep occurrence plus 1 of 1 controlled reproductions
- **First suspicion** global command availability and capture ownership

After the 30-cycle sweep, every selected-session XML tree said `Stopped` and
contained no *Stop capture* control. Nevertheless, *Session cache* described
`On-device logcat 21-22-33` as `capture in progress`, and the process table
confirmed an app-owned child still running:

```text
15517  ... com.barebit.visualcat
17250 15517 logcat -b all -T 1 -v threadtime,year,UTC,usec
```

Selecting the older tab exposed live *Follow* and *Stop capture* controls and
current timestamps. Closing that exact active tab finally removed PID 17250.

A controlled reproduction removed harness ambiguity. With one live capture
already active, the global *Live* button remained enabled. Tapping it again
immediately created a second session and a second child process:

```text
18139 15517 logcat -b all -T 1 -v threadtime,year,UTC,usec
18249 15517 logcat -b all -T 1 -v threadtime,year,UTC,usec
```

Stopping the newly selected session removed only PID 18249 and displayed a
normal `Stopped · 4 entries kept` result; PID 18139 continued capturing in the
background with no global running indicator. Only closing the older active tab
ended it. This can record indefinitely, consume battery/storage, and collect
log data after the user reasonably believes capture has stopped.

The implementation permits this state directly: the Android toolbar registers
`Primary("●  Live", StartOnDeviceAsync)` without availability logic, while
`WorkspaceViewModel` owns operations per tab and uses a two-slot semaphore.
Each session's Stop button correctly targets only its own operation, but the
shell neither prevents nor summarizes another active on-device operation.

**Suggested fix.** Treat on-device capture as a single shell-owned activity.
While one exists, make the global *Live* action focus that session or morph into
an explicit *Stop live capture* action; do not silently create another. If
parallel captures are a deliberate advanced feature, require an explicit
confirmation, show a persistent global `2 captures running` indicator, provide
*Stop all*, and mark every active tab unmistakably in the strip. On background,
close, and process teardown, cancel and await every owned source. Add an
instrumented regression that taps Live twice, asserts at most one child
`logcat`, stops from a different selected tab, and verifies zero children plus
zero cache rows marked in progress.

Evidence: `X-24-session-cache-after30.png/.xml`,
`X-24-orphan-active-selected.png/.xml`, `X-24-orphan-closed.png/.xml`,
`F-22-first-prompt.png/.xml`, `F-22-second-live.png/.xml`,
`F-22-after-stop-selected.png/.xml`, and
`F-22-after-close-hidden-active.png/.xml`.

### F-23 · Cache deletion asks for confirmation without saying how much it will delete

- **Severity** Minor · **Scenario** A-15
- **Reproducibility** 1 of 1
- **First suspicion** session-cache confirmation presentation

The populated cache clearly reported `46 temporary sessions · 289.41 MiB`, and
cleanup correctly refused to act while the default policy was disabled. After
cleanup was enabled, however, the irreversible confirmation said only:

> Sessions older than the configured age, and oldest sessions above the size
> cap, will be permanently removed.

It did not state the current policy values, number of eligible sessions, total
bytes, or even whether the result would be zero. The next screen reported
`Deleted 0 eligible sessions.` The safety mechanisms work, but A-15 explicitly
requires the delete action to name exactly what it will remove before removal;
the current wording asks the user to perform that calculation mentally from a
long cache list.

**Suggested fix.** Evaluate the policy before presenting confirmation and show
`Delete N sessions (size)?`, the effective age/size thresholds, and a concise
preview of the affected names with an expandable complete list. Re-scan and
revalidate immediately before deleting, explicitly excluding capturing and open
sessions; if eligibility changed, update the confirmation instead of applying
the stale preview. For zero eligible sessions, skip the destructive prompt and
say so directly. Add tests for disabled, zero-eligible, age-only, size-only,
open, and actively capturing cases.

Evidence: `A-15-initial.png/.xml`, `A-15-delete-while-disabled.png/.xml`,
`A-15-enabled-age1.png/.xml`, `A-15-delete-confirmation.png/.xml`,
`A-15-after-clean.png/.xml`, and `A-15-verify-restored.xml`.

### F-24 · A copy notice moves the action row, so a rapid second tap opens an unrelated pane

- **Severity** Minor · **Scenario** R-34, U-12, U-18
- **Reproducibility** 2 of 2 rapid double-tap attempts
- **First suspicion** notice-lane layout / pointer routing

With one row selected in the Split workspace, *Copy raw* occupied
`[723,1479][935,1614]`. Loading the next 500 entries did exactly what R-34
guards: the button remained at the identical bounds while the footer advanced
from 49,494 to 48,994 rows left.

Tapping *Copy raw*, however, inserted the completion lane
`Copied the raw text of 1 entry.` and synchronously took about 160 px from the
workspace. The same button moved to `[723,1319][935,1454]`. A second tap 350 ms
after the first at the original center (`829,1546`) therefore landed on the
reflowed entry list; in both controlled attempts the app switched to the *Entry*
inspector. One deliberate single tap stayed on *Entries*, proved the copy, and
showed the notice, so navigation is caused by the repeated coordinate after
reflow rather than by the Copy command itself.

This is not destructive, but it violates the explicit invariant that two taps
in one place invoke one control and U-18's requirement that repeated taps not
activate an unrelated action. The same layout jump can misroute repetition on
other actions as notices arrive.

**Suggested fix.** Do not make the transient notice lane participate in the
height allocation of the active workspace. Overlay it in the already protected
bottom inset, reserve a stable lane before interaction, or otherwise keep the
toolbar and analysis-tab coordinates fixed while it appears. As defence in
depth, do not treat a second tap as a list double-tap when pointer-down began on
a different control/layout generation. Add an instrumented test that records
the action bounds, taps Copy twice within the platform double-tap interval, and
asserts two Copy invocations, the Entries tab still selected, and unchanged
bounds before/after notice publication.

Evidence: `R-34-selected-before-load.png/.xml`,
`R-34-after-load.png/.xml`, `R-34-single-copy.png/.xml`,
`R-34-repro2-before.xml`, and `R-34-repro2-after.png/.xml`.

### F-25 · Follow mode clears the entry being read when its 30-second window advances

- **Severity** Major · **Scenario** A-24, R-39
- **Reproducibility** 3 of 3 selections near the oldest edge of the live window;
  two controlled before/after pairs were five seconds apart
- **First suspicion** selection lifetime is coupled to the current loaded page

During an active P0 capture with *Follow* enabled, selecting a row correctly
enabled *Copy raw* and *Entry* and placed the timeline caret. When subsequent
refreshes advanced the 30-second viewport far enough that the entry fell out of
the refreshed collection, the app cleared the selection, disabled both actions,
removed the caret, and changed an already-open inspector to
`No entry selected`. In the clearest pair, entry
`08-21 21:52:40.990953` was visibly selected; five seconds later no row was
selected and the first visible rows were already around `21:52:48–49`.

This contradicts A-24 and R-39's explicit contract that a live refresh restores
the selected row by entry id. It makes an investigator lose the message and
source bytes simply by reading for a few seconds. Turning *Follow* off is a
working but undisclosed workaround: with it off, entry
`21:51:12.630139` and exact source line 66 survived a 62-second background
interval, resume, continued sidecar writes, and rotation.

The implementation explains the device result. `RestoreEntrySelection()`
searches only `_viewModel.Entries`; when the id is absent and `CanLoadMore` is
false, it explicitly nulls `_selectedEntryId`, the inspector, and the timeline
caret (`SessionWorkspaceView.Interactions.cs:1250–1275`). In a moving Follow
window, “not in this viewport” is not user deselection and must not be treated
as one.

**Suggested fix.** Keep the inspected `NormalizedEntry`/entry id and caret as
workspace state independent of the current virtualized page. If the selected
row ages out, preserve the inspector and render a clear off-screen direction
marker; optionally pin that entry or automatically pause Follow after an
explicit selection, with a visible *Resume live edge* action. Clear selection
only for a user deselection, a filter that explicitly excludes the record, or
session closure. Add a live-refresh regression that selects the oldest visible
row, advances the viewport beyond it, and asserts that inspector identity,
source bytes, and caret remain stable through background/resume and rotation.

Evidence: `A-24-live-selected.png`, `A-24-live-entry-immediate.png`,
`R-39-repro2-selected.png`, `R-39-repro2-after5.png`,
`R-39-latest-selected.png`, `R-39-latest-after5.png`,
`R-40-live-follow-off-source.png`, `A-24-resume-after-60s.png`, and
`A-24-rotate-landscape-selected-source.png`.

### F-26 · Session tabs and their close buttons are only 44 dp tall

- **Severity** Minor · **Scenario** U-08
- **Reproducibility** every open session chip and close target inspected
- **First suspicion** explicit mobile `MinHeight = 44`

The stable post-capture accessibility tree gives exact physical bounds. At the
recorded effective density of 450 dpi (`2.8125 px/dp`):

- *Show session small.txt* is `[31,291][228,414]`: 123 px / 2.8125 =
  **43.7 dp** high.
- *Close session small.txt* is `[228,291][352,414]`: **44.1 × 43.7 dp**.
- The live-session switch and close targets have the same 123 px height; the
  close target is again 124 px wide.

The main toolbar buttons, workspace modes, Fit, and the analysis tabs all
measured 135 px = 48 dp, so this is not a density-estimation error. Session
switching and closing are primary U-08 controls and explicitly fail its 48 dp
minimum. The adjacent switch/close targets also share an edge, increasing the
risk of closing a session while trying to select it one-handed.

With three tabs open, the selected rightmost `large.txt` chip was only partly
scrolled into the viewport. Its close node reported `[1177,291][1220,414]`:
only **43 px = 15.3 dp wide** remained tappable on screen instead of the normal
124 px. The active label was visible, but its primary close affordance required
the user to discover and horizontally scroll the strip first.

The layout source sets both mobile chip controls to 44 logical pixels
(`MainView.TabStrip.cs:142,163`). The same below-plan constant is used by the
notice dismiss action and source-section header, so a broader touch-target audit
should accompany the fix.

**Suggested fix.** Use one Android touch-target token of at least 48 dp for
every actionable chip, close affordance, notice action, and collapsible header;
keep the visible glyph compact if needed but expand its hit area. Add a rendered
Android geometry test at more than one density that walks every actionable
accessibility node and asserts both dimensions are at least 48 dp unless the
control is explicitly documented as an inline exception. Give the destructive
close region a small visual/semantic separation from the tab switch target.

Evidence: `U-08-primary-targets.xml`, `A-24-stopped.png`, and
`A-05-three-tabs.xml`.

### F-27 · ACTION_VIEW tabs expose the materialized cache filename instead of the provider display name

- **Severity** Minor · **Scenario** A-06, A-21, R-25
- **Reproducibility** 2 of 2 Downloads URI forms for the same `tiny.txt`
- **First suspicion** URI last-segment is used as a cache and display name

Opening the visible Downloads document `tiny.txt` through Android's real
*Open with → VisualCat* path succeeds and imports exactly 1,000 entries, but the
tab does not say `tiny.txt`. The first provider form became:

```text
20260821-200744-8fde316a7e2447a281e7e825dfc2d0f5-raw:_storage_emulated_0_Download_tiny.txt
```

The 472 px tab can only render `20260821-2007…d_tiny.txt`; its accessibility
name reads the entire timestamp, UUID, provider document id, and absolute
shared-storage path. A later equivalent provider identity for the same physical
file became another opaque name ending `-msf:1000000323.txt`. This is safe from
filesystem traversal, but it is not a user-facing provider display name and is
especially noisy for a screen reader.

Exact-URI duplicate suppression itself passes: after the app already held the
URI grant, redelivering the precise `msf:1000000323` ACTION_VIEW intent to the
running top instance reported `intent has been delivered to currently running
top-most instance`, and the before/after accessibility trees contained the same
two session titles with no third tab. Selecting the same physical file through
two equivalent provider URI forms did create two sessions, which is outside the
strict exact-URI oracle but shows why an optional content-identity check would
be more robust.

`MainActivity.MaterializeIncomingAsync()` uses `uri.LastPathSegment`, sanitizes
that provider id, prepends UTC time plus a GUID, and returns only the cache path
(`MainActivity.cs:156–201`). `FileLogSource` then publishes `FileInfo.Name` as
the source display name (`FileLogSource.cs:17–39`). The original OpenableColumns
`DISPLAY_NAME` is never retained separately from the internal cache filename.

**Suggested fix.** Query the provider's `OpenableColumns.DisplayName`, validate
and length-cap it, and carry it as immutable presentation metadata while keeping
the GUID cache filename private. Fall back to a neutral `shared-log.txt`, not a
raw document id/path. Give TalkBack the concise display name and expose provider
provenance only in Session info. Preserve exact-URI idempotence, and consider a
bounded size/hash identity after materialization to recognize equivalent URIs
without incorrectly merging two mutable documents. Add instrumentation for
`raw:`, `msf:`, Unicode, absent/malicious display names, and exact redelivery.

Evidence: `A-06-open-with-tiny.png`, `A-06-content-first-success.png/.xml`,
`A-06-before-exact-shell-redeliver.xml`,
`A-06-exact-shell-redeliver-result.png/.xml`, and the activity intent dump for
`msf%3A1000000323`.

---

## 4. Evidence, measurement limitations, and hand-back

- Android `uiautomator dump` could not reach an idle state while this Avalonia
  live workspace was updating. Screenshots, window focus, process state, and
  post-stop XML dumps were used instead; a failed dump is not treated as proof
  of an inaccessible node.
- `dumpsys gfxinfo` reported zero rendered frames while visible Avalonia content
  changed. HWUI frame totals are therefore not a valid animation/jank oracle for
  this renderer on this device; future latency work needs Perfetto or an external
  high-frame-rate camera.
- Device `screencap` took about 493 ms. The first post-Stop capture was initiated
  81 ms after the tap and already showed `Stopping`; the next showed final state
  by 500 ms. This proves prompt functional acknowledgement but cannot establish
  the plan's strict ≤250 ms gate, so B-12's latency assertion remains Blocked.
- Evidence paths are relative to
  `artifacts/live-test/20260821-edge60pro/evidence/`. XML trees and screenshots
  are retained; raw full-device log streams can contain sensitive data and were
  not promoted into the durable report without review.
- R-22/B-16 quiet-source evidence is `B-16-P0-quiet-t4.png`,
  `B-16-P0-quiet-t60.png`, `B-16-P0-quiet-t125.png`, and
  `B-16-P0-quiet-stopped.png/.xml`. The count remained exactly 2 while the
  status advanced from `no source lines for 3s` to `1m 59s`; the capture then
  stopped normally.

### 4.1 Final device hand-back

At 2026-08-21 20:19 UTC (`22:19:51 CEST` on the device), explicit cleanup and
read-back produced the following terminal state:

- `adb uninstall com.barebit.visualcat` returned `Success`; package lookup and
  process-table lookup were both empty afterward. `READ_LOGS` had been revoked
  before force-stop/uninstall.
- 153 top-level XML/PNG captures were selected only after their absolute paths,
  extensions, and 2026-08-21 creation window were audited. The 19 exact
  run-created Download targets were deleted separately. A final listing showed
  only the pre-existing `.ready_for` directory and seven pre-existing user files
  in Downloads.
- Device settings read back as `font_scale=1.0`, system locale `en-US`,
  auto-rotate enabled with `user_rotation=0`, and night mode enabled—the recorded
  pre-test state.
- ADB state was `device`; battery was 94% and USB-powered; `/data` had 200 GiB
  free. No product source file was edited by this test run.

Durable evidence remains on the host under
`artifacts/live-test/20260821-edge60pro/evidence/`. The temporary interrupted-
session handoff `docs/output.md` was deleted only after its observations were
reconciled into this report.

---

## 5. Remediation ledger

**Purpose.** §3 records what was found. This section records what was *done about
it*, so that an independent session can resume without re-deriving anything. It is
the single source of truth for remediation status.

**How to resume.** Read §5.1 (status table). Pick the first row whose **Status** is
not `Done` or `Won't fix`. Read that finding's entry in §5.2 for the decisions
already taken. Then continue.

**Status vocabulary**

| Status | Meaning |
|---|---|
| `Not started` | No code written. |
| `In progress` | Code partially written; §5.2 says exactly where it stopped. |
| `Code done` | Implemented and unit/headless-tested on the host; not yet exercised on the device. |
| `Done` | Implemented, host-tested, **and** verified on the physical device; §5.2 records the device evidence. |
| `Won't fix` | Deliberately not done; §5.2 records why. |
| `Deferred` | Real, but out of this remediation's scope; §5.2 records why and what it needs. |

### 5.1 Status table

| Finding | Severity | Area | Status | Device-verified |
|---|---|---|---|---|
| F-20 Zoom crash on one-entry session | Blocker | Timeline | Done | Yes |
| F-01 Release build calls itself `2.0.5` | Major | Build | Done | Yes |
| F-04 Resource-key exception text | Major | Build + view-model | **Done** | **Yes — trimmed Release on Samsung** |
| F-05 Status-line bypasses `ApplyStatusText` | Major | View | Done | Yes |
| F-07 Marker navigation is keyboard-only | Major | View | Done | Yes |
| F-09 Landscape row floor + status overlap | Major | Layout | **Done** | **Yes — Samsung 360 dp floor/clip/status invariant** |
| F-10 Landscape drawer ignores the IME | Major | Layout | Done | Yes (Samsung) |
| F-12 Wrong buffer attribution | Major | Source/parse | Done | Yes |
| F-13 P0 promises a sheet that cannot appear | Major | Copy | Done | Yes (P0+P1) |
| F-15 Bursty live-refresh CPU | Major | Render loop | **Done** | **Yes — Samsung, Release, screen-off soak, both legs (§8.5)** |
| F-19 Interrupted capture reads `Ready` | Major | State mapping | **Done** | **Yes — Samsung process kill, durable state, and all three recovery routes** |
| F-22 Two live captures, one Stop | Major | Shell | Done | Yes |
| F-25 Follow clears the selected entry | Major | View | **Done** | **Yes — Samsung identity, off-page explanation, and Show-it restoration** |
| F-02 `generate-test-log --help` writes 90 MB | Minor | CLI | Done | n/a (CLI) |
| F-03 Hero links 18.8 dp | Minor | Layout | Done | Yes |
| F-06 Zero-match filter blank pane | Minor | View | Done | Yes |
| F-08 0-based source gutter | Minor | View | Done | Yes |
| F-14 `State: Importing` while live | Minor | Presentation | Done | Yes |
| F-17 Private path in `content-desc` | Minor | Accessibility | Done | Yes (Samsung) |
| F-18 `Ready` over a blank list on reopen | Minor | View | Done | Yes |
| F-23 Cache deletion does not say how much | Minor | Dialog | **Done** | **Yes — Samsung zero/non-zero previews, cancel, exact delete, and open-path protection** |
| F-24 Notice reflow misroutes a second tap | Minor | Layout | Done | Yes (Samsung) |
| F-26 44 dp tabs and close targets | Minor | Layout | Done | Yes |
| F-27 Cache filename shown as tab title | Minor | Android intent | Done | Yes |
| F-11 Landscape placeholder truncation | Polish | Layout | Done | Yes (Samsung) |
| F-16 Capture names look like dates | Polish | Naming | Done | Yes |
| F-21 `1 entries kept` | Polish | Copy | Done | Yes |

Found by the third-device run (§7); same vocabulary, same rules.

| Finding | Severity | Area | Status | Device-verified |
|---|---|---|---|---|
| D-04.0 Startup paints a cancellation as a failure, and the tab sticks on `Opening` (F-04 + F-18 recurrence) | Major | Presentation + view | **Done** | **Yes — Pixel 5, three cold launches** |
| F-28 Gesture-navigation Back steals the plot's drag and leaves the app | Major | Android platform | **Done** | **Yes — Pixel 5, both edges, with Back still working elsewhere** |
| F-30 A landscape keyboard slices the query field it was raised for | Major | Layout | **Done** | **Yes — Pixel 5, 341 dp landscape and 777 dp portrait** |
| F-29 Drawer `Clear the query` is 30.5 dp wide | Minor | Layout | **Done** | **Yes — 0 of 24 clickable nodes under 48 dp** |
| F-16 (second half) Tab strip cuts the word out of every generated capture name | Polish | Layout | **Done** | **Yes — Pixel 5** |

Found by the fourth pass (§8); same vocabulary, same rules.

| Finding | Severity | Area | Status | Device-verified |
|---|---|---|---|---|
| F-31 Every number field’s spin buttons are 34 × 46 dp | Minor | Layout | **Done** | **Yes — Samsung Release build, both settings sheets** |
| F-32 A tall notice makes a portrait workspace compact, and the merged row clips `Stop capture` to 15 dp | Major | Layout | **Done** | **Yes — Samsung Release, the exact state reproduced from a cold launch** |
| F-33 The notice drops the sentence it exists to deliver | Major | Layout + copy | **Done** | **Yes — Samsung Release, the remedy now reads in full** |

Found by the fifth pass (§9); same vocabulary, same rules.

| Finding | Severity | Area | Status | Device-verified |
|---|---|---|---|---|
| F-34 The shared command row draws five controls on top of each other; `Filters` has no reachable touch point | **Blocker** | Layout | **Done** | **Yes — Motorola Release, 434 × 498 dp, 0 overlapping pairs and the drawer opens** |
| F-35 `Load 500 more` is laid out past the right edge of the pane | Major | Layout | **Done** | **Yes — Motorola Release, 34.1 dp → 49.4 dp; also failed headlessly at 801 dp** |
| F-36 A short, narrow filter drawer draws two captions and none of its controls | Major | Layout | **Done** | **Yes — Motorola Release, 7 of 7 severity chips, body 28.8 dp → 67.9 dp** |
| F-37 The merged capture row is decided on a width the row does not have; `Follow` is 23.5 dp | Major | Layout | **Done** | **Yes — Motorola Release, 780 dp landscape with a capture running, 23.5 dp → 339.2 dp** |

Found by the sixth pass (§10); same vocabulary, same rules.

| Finding | Severity | Area | Status | Device-verified |
|---|---|---|---|---|
| F-38 Every compact-height pane opts nine controls out of the 48 dp floor | Minor | Layout | **Done** | **Yes — Motorola Release, landscape; 42.3 dp → 48.0 dp on all five measurable controls** |
| F-39 Each system text-size change, and each session close, leaves another copy of the command strip in the shell row | Major | Shell | **Done** | **Yes — Motorola Release, four text-size changes and two tab closes; 1 strip and 0 overlapping pairs throughout** |

Found by the seventh pass (§11); same vocabulary, same rules.

| Finding | Severity | Area | Status | Device-verified |
|---|---|---|---|---|
| F-40 A sheet is built from the state it opened in, and no state change reaches it | Minor | Shell | **Done** | **Yes — Pixel 5 Release, theme flip and rotation; light sheet on a light shell, 9 of 9 commands** |
| F-41 The analysis tab strip is arranged once and never again, so it slices its own labels at any enlarged text size | Minor | Layout | **Done** | **Yes — Pixel 5 Release, 92.0 dp → 117.1 dp, contiguous, `Entries`/`Insights`/`Entry` whole at 1.3×** |
| F-42 `Load 500 more· 49,656 remaining` — the separator lost its space | Polish | Copy | **Done** | **Yes — Pixel 5 Release** |

Found by the Motorola independent continuation (§19); same vocabulary, same
rules.

| Finding | Severity | Area | Status | Device-verified |
|---|---|---|---|---|
| F-43 Pairing help implies split screen preserves Android's code on every OEM | Minor | Copy + setup UX | **Done** | **Yes — Motorola API 36 Release; §§19.6–19.8** |
| F-44 Pairing fields and footer stay behind the Pixel numeric IME | Major | Sheet + input-pane layout | **Done** | **Device-verified — Pixel 5 API 34 Release; §20.7** |
| F-45 Pairing code opens QWERTY and remains plain accessibility text on Pixel | Major | Input UX + privacy | **Done** | **Device-verified — Pixel 5 API 34 Release; §20.8** |
| F-46 Large-text landscape home clips provenance below the system bar | Minor | Responsive home layout | **Done** | **Device-verified — Pixel 5 API 34 Release; §20.9** |
| F-47 Tall landscape IME collapses the setup sheet to its title | Major | Extreme-height sheet + IME | **Done** | **Device-verified — Pixel 5 API 34 Release; §20.10** |
| F-48 One 48 dp severity toggle rounds down to 47.6 dp on Pixel | Polish | Filter touch geometry | **Done** | **Device-verified — Pixel 5 API 34 Release; §20.13** |

### 5.2 Per-finding remediation record

Each entry records the decision taken, the files touched, the host test that
guards it, and the device evidence. Entries appear in the order they were worked.

#### F-20 · Zoom crash on a one-entry session

**Decision.** Two independent guards, because the report showed two independent
ways into the degenerate state (a progressive one-entry snapshot, and a finished
one-entry file).

1. **The crash itself** is fixed at the zoom boundary. `TimelineControl` now
   builds effective zoom bounds in exactly one place —
   `ZoomBounds(geometry, session)` returns
   `(minimum, Math.Max(minimum, MaximumSpan(session)))` — and every zoom route
   goes through `ZoomViewport(...)`, which also widens a below-minimum *current*
   viewport before constructing the transform. The four routes that previously
   passed `MaximumSpan(session)` raw (double-tap, pinch, keyboard `+`/`-`,
   `ZoomAtCenter`) now cannot: the raw call no longer appears in the file. The
   invariant is at the shared call boundary, so a sixth route cannot omit it.
2. **The degenerate state itself** is no longer reachable on open.
   `SessionTabViewModel.FitViewport(sessionRange)` widens a fitted viewport to
   `MinimumViewportUs` (2 s) anchored at the newest instant — the same clamp
   Follow has used since audit 2 (C6). Widening never hides a record, and the
   assertion that the fitted window still contains the whole session is a test.

**Files** `src/VisualCat.App/Timeline/TimelineControl.cs`,
`src/VisualCat.App/Presentation/SessionTabViewModel.cs`.

**Host tests** `LiveTestRemediationTests.EveryZoomRouteSurvivesAOneEntrySession`
(double-tap, wheel both directions, keyboard `+`/`-`, `ZoomAtCenter`, `FitSession`
against a one-line file), `.AOneEntryImportOpensAtADrawableSpan`,
`.AnOrdinaryImportStillOpensExactlyFitted` (the floor must not widen a session
that is already wider). New shared fixture `LiveTestWorkspaceFixture`.

**Device evidence — verified.** One-entry file opened at `2 s · 2.21 ms/px` with distinct instants at both ends of the axis; **eight double-taps** left PID 31051 alive with no `exit-info` crash row, and the plot clamped at `905 µs · 1 µs/px` — the exact minimum span §3 computed for this device. See §5.3.

#### F-01 · A Release build of unreleased code calls itself `2.0.5`

**Decision.** Release-ness is an explicit signal, not a configuration, and the
identity line names the build rather than only the release.

1. `Directory.Build.props` — the `-dev` suffix is now suppressed only by
   `-p:ReleaseChannel=stable`, which the release pipeline passes and no developer
   build, CI check, or candidate build does. Every other build, Release included,
   is `2.0.5-dev+<commit>`. The old condition keyed on `$(Configuration)`, which
   made the one configuration nobody can test a candidate in the only one that
   admitted to not being a release.
2. `ProductInfo.BuildVersion` — release version plus the **7-character** source
   revision. `MainView`'s identity line uses it, so the empty state now reads
   `VisualCat 2.0.5-dev+b56fb5a · local-first · no telemetry` and a screenshot
   answers "which build?". `DisplayVersion` is unchanged for anywhere that wants
   the bare release number; `InformationalVersion` keeps the full 40 for
   diagnostics.

**Deliberately not done.** The Android `versionCode` formula is unchanged.
Google Play orders uploads by `versionCode` alone and refuses a duplicate, so
perturbing the formula for candidate builds risks the publishing path for a
problem `versionName` already solves: `dumpsys package | grep versionName` now
distinguishes a candidate from the release, and `-p:ApplicationVersion=N` remains
the documented override when a candidate genuinely needs its own code.

**Files** `Directory.Build.props`, `src/VisualCat.Domain/ProductInfo.cs`,
`src/VisualCat.App/Views/MainView.cs`.

**Host test** `LiveTestRemediationTests.TheIdentityLineNamesTheBuild`.

**Device evidence — verified.** Empty state reads `VisualCat 2.0.5-dev+b56fb5a · local-first · no telemetry`; `dumpsys package` reports `versionName=2.0.5-dev`. See §5.3.

#### F-04 · Release builds show framework exception messages as resource keys

**Decision.** All four remedies in the report, in its own order of value.

1. **Validation at the input.** New `VisualCat.Core.Query.SearchPattern` —
   `TryCompile(spec, out regex, out SearchPatternProblem)` plus `Explain(...)`,
   which maps every `RegexParseError` member to one plain clause and humanises
   any member the framework adds later, so it can never degrade into a key.
   `SessionTabViewModel.ApplySearchAsync` now returns `SearchPatternProblem?` and
   **does not replace the filter** when the pattern is rejected: the previous
   result, the chip bar, and the status line all stay as they were, because none
   of them has stopped being true. This also removes the report's "additional
   defect 1" — the chip bar claiming `regex = (unclosed` while showing 49,994
   unfiltered rows.
2. **A message beside the field.** `SessionWorkspaceView._searchProblem` is an
   assertive live region under the query field, in error ink, reading
   *"Not a valid regular expression: there are more "(" than ")" (position 9)."*
   The query field is marked and carries the same sentence as help text. On the
   phone the drawer stays open on a refused pattern (the IME action key no longer
   closes it), so the reader is left looking at what they have to fix.
3. **No framework text in front of a reader.** `WorkspaceViewModel.FriendlyMessage`
   routes its `_ =>` arm and its two interpolating arms through `Presentable` /
   `Detail`, which replace a resource-key-shaped message with a product sentence
   and a stable code (`VisualCat could not finish that (InvalidOperation). …`) and
   send the raw text to the diagnostic bundle via the new
   `WorkspaceViewModel.RecordFailure`.
4. **Safety net.** `<UseSystemResourceKeys>false</UseSystemResourceKeys>` in
   `VisualCat.Android.csproj`, so a stack trace in a bundle stays legible. It is a
   net, not the fix: a BCL sentence is still not product language.

**Files** `src/VisualCat.Core/Query/SearchPattern.cs` (new),
`src/VisualCat.App/Presentation/SessionTabViewModel.cs`,
`src/VisualCat.App/Presentation/WorkspaceViewModel.cs`,
`src/VisualCat.App/Views/SessionWorkspaceView.cs`,
`SessionWorkspaceView.Interactions.cs`,
`src/VisualCat.Android/VisualCat.Android.csproj`.

**Host tests** `AnUncompilablePatternIsRefusedAndChangesNothing`,
`TheSameTextAppliesAsALiteral`, `APatternProblemReadsAsProductLanguage`,
`NoPatternExplanationLooksLikeAResourceKey` (walks every `RegexParseError`
member), `ATrimmedFrameworkMessageIsReplacedByAProductSentence` — the last two
being the report's requested "no user-facing failure string matches
`^[A-Za-z_][A-Za-z0-9_]*, `" regression.

**Device evidence — verified (Debug build).** `(unclosed` under *Regex* produces “Not a valid regular expression: there are more "(" than ")" (position 9).” beside the field, the chip bar still reads `No active filters`, and the list still shows 49,994. No resource key anywhere. See §5.3.

**Closed since this entry was written.** The `UseSystemResourceKeys` half is Release-only by construction, so it wanted one Release build on the device. §6/C-06 supplied it (trimmed Release, SHA-256 `43CF819C…`, `(unclosed` under *Regex* producing the product sentence with zero `MakeException` / `InsufficientClosingParentheses` / `ResourceKey` matches in the tree), and §7/D-04.0 closed the wider half by routing the five call sites that had never used `FriendlyMessage` through it and adding the source guard that keeps them there. Nothing is owed.

#### F-05 · Five routes wrote the status line behind `ApplyStatusText`

**Decision.** Make the invariant unbreakable rather than documented, and give
transient messages an expiry.

1. **`StatusLine` (new type).** The status `TextBlock` is private to it and its
   only mutator, `Refresh()`, takes no text — it reads
   `SessionTabViewModel.Status`. `Text` is readable and not writable, and the
   layout members the workspace genuinely needs (`ArrangedWidth`, `Layout`,
   `SetExpanded`, `LayoutUpdated`) are re-exposed one by one. There is no longer a
   text property for a sixth route to poke.
2. **`ReportTransientStatus` / `ClearTransientStatus`.** Both the visible text and
   `AutomationProperties.Name`/`HelpText` now move together for every message,
   because they all travel the same route. All five bypasses
   (`Interactions.cs:1104/1108/1135`, `RawContext.cs:786/1423`) call it.
   `SessionTabViewModel.Status` is now `private set`.
3. **Expiry.** A transient message records the `_queryGeneration` that produced
   it; when a strictly newer query lands in `RefreshAsync`, it is dropped and the
   session's own description returns. The report's exact scenario — invalid regex,
   then a valid one, then *Clear all* — can no longer leave a stale failure.
4. Failure text from those routes now goes through `FriendlyActionFailure`, which
   composes with `FriendlyMessage` (see F-04) instead of interpolating
   `exception.Message`.

**Files** `src/VisualCat.App/Views/StatusLine.cs` (new),
`SessionWorkspaceView.cs`, `SessionWorkspaceView.Presentation.cs`,
`SessionWorkspaceView.Interactions.cs`, `SessionWorkspaceView.RawContext.cs`,
`src/VisualCat.App/Presentation/SessionTabViewModel.cs`.

**Migration note for other sessions.** Three existing tests set `tab.Status`
directly; they now call `tab.ReportActivity(activity, text)`. That is the
supported route, along with `ReportTransientStatus`.

**Host tests** `ATransientStatusReachesTheScreenReaderToo`,
`ASupersededTransientStatusClearsWhenTheNextQueryLands`.

**Device evidence — verified.** After the refused pattern, `Rendering.surface` gives the exact §1.2 oracle (7,181) and the status line reads `Ready · 49,994 entries` — read from the accessibility tree, so text and accessible name are one string. See §5.3.

#### F-14 · Session info reported `State: Importing` during a live capture

**Decision.** Fix the state that is stamped, and stop rendering the enum.

1. **Root cause.** `SessionCoordinator` chose its first state with
   `Kind == SourceKind.Adb ? Connecting : Importing`, so an Android on-device
   capture (`SourceKind.Android`) took the finite-file branch and never reached
   `Streaming`. It now branches on `Metadata.IsFinite`, which is what the question
   actually is; the existing `Connecting → Streaming → Stopping → Stopped`
   transitions then apply to every live source on every platform.
2. **Presentation.** New `SessionCompletionText.State(...)` maps every
   `SessionState` to a product phrase — `Reading`, `Connecting`, `Capturing`,
   `Paused`, `Finishing`, `Stopped`, `Ready`, `Cancelling`, `Cancelled`,
   `Failed`, `Preparing` — and Session info renders that instead of
   `$"{descriptor.State}"`.

**Files** `src/VisualCat.Application/Coordination/SessionCoordinator.cs`,
`src/VisualCat.App/Presentation/SessionCompletion.cs` (new),
`src/VisualCat.App/Views/SessionWorkspaceView.Panes.cs`.

**Host test** `NoLifecycleStateIsShownAsARawEnumName`.

**Device evidence — verified.** *Session info* reads `State: Capturing` during a live capture and `State: Ready` after stop. See §5.3.

#### F-19 · A process-killed capture read `Ready` in the workspace

**Decision.** Completion is one derived fact, from the one durable thing the
store already records, and every surface phrases it from there.

1. `SessionCompletion` — `Complete` / `InProgress` / `RecoverablePartial` —
   derived by `SessionCompletionText.Of(descriptor.Finalized, workInFlight)`.
2. `SessionActivity.RecoverablePartial` is a new activity, so every view that
   switches on activity treats an interrupted session differently from a complete
   one rather than having to re-derive it.
3. `SessionTabViewModel.ReportOpened(descriptor)` replaces the two
   `ReportActivity(Ready, …)` call sites on the load path. An unfinalized manifest
   now opens as
   *"Interrupted · 1,173 entries recovered · the capture ended before it was
   finished"*.
4. Session info's `State` row and *Recent sessions*' one-word outcome both call
   the same mapper, so the three surfaces the report found disagreeing cannot
   disagree again.
5. The workspace raises one notice per view on first observing the state,
   because a reader arriving through automatic session restoration is looking at
   rows rather than at the status line.

**Continuation follow-up implemented.** The partial banner now has a Review
action with explicit Keep, Export recovered data, and Delete dispositions.
Delete is available only for a validated direct cached session and requires a
second confirmation; Export is scoped to the exact recovered tab; Keep does not
rewrite the durable interrupted fact. See §6 C-04.7/C-04.8.

**Files** `src/VisualCat.App/Presentation/SessionCompletion.cs` (new),
`SessionActivity.cs`, `SessionTabViewModel.cs`,
`src/VisualCat.App/Views/SessionWorkspaceView.Interactions.cs`,
`SessionWorkspaceView.Panes.cs`, `src/VisualCat.App/Views/SessionDialogs.cs`.

**Host test** `AnUnfinishedSessionSaysSoEverywhere`.

**Device evidence — verified on Samsung.** A controlled `READ_LOGS` revoke killed
the live process; two cold launches recovered the exact 947 committed entries and
said Interrupted consistently on the tab, workspace, Session info, notice, and
Recents. Keep, Export-to-picker/cancel, and Delete-confirm/cancel were exercised;
the exact cache directory remained. See §6 C-04.7/C-04.8.

#### F-21 · One-entry stop summaries said `1 entries kept`

**Decision.** One counted-noun helper, applied at every site that interpolates a
count, rather than a fix at the four that were caught.

`VisualCat.Domain.Counted` — `Entries(n)`, `EntriesOrNone(n)`, `Sessions(n)`,
and a general `Of(n, singular, plural)`. Thousands separators still follow
`DisplayCulture`. Applied to the stop, duration-ended, source-ended and
unprompted-ending summaries, to `Ready`/`Capturing`/`Reading` status lines, to the
facet and template accessible names, to the *Copy raw* notice (whose hand-written
singular branch is now redundant and gone), and to the timeline-bar hint.

**Files** `src/VisualCat.Domain/Counted.cs` (new),
`WorkspaceViewModel.cs`, `SessionTabViewModel.cs`,
`SessionWorkspaceView.Facets.cs`, `.Interactions.cs`, `.Panes.cs`,
`.RawContext.cs`.

**Host test** `CountedNounsAgreeWithTheirNumber` (0, 1, 2, thousands).

**Device evidence — verified.** `Ready · 1 entry` on the one-line file; `1 entry`, `1 entry matching`, `1 entry · 14.3 % of current matches` in Templates; `Stopped · 14 entries kept` and `Stopped · 6,391 entries kept`. See §5.3.

### 5.3 Device verification log

Everything in this section was measured on the physical device against the
remediated build. It is the evidence behind every `Done` in §5.1.

**Run header**

| Field | Value |
|---|---|
| Date (UTC) | 2026-08-21 21:33 → 2026-08-22 00:35 |
| Device | motorola edge 60 pro, serial `ZY22M4T2Z4`, Android 16 / API 36, `arm64-v8a` |
| Screen / density | 1220 × 2712 px, 450 dpi (**2.8125 px/dp**) — identical to §1, so every dp figure is directly comparable |
| Build under test | **Debug**, `versionName=2.0.5-dev`, `versionCode=20005`, working tree on top of `b56fb5a` |
| Install route | `dotnet build src\VisualCat.Android\VisualCat.Android.csproj -c Debug -t:Install -p:EmbedAssembliesIntoApk=true` (see the live-test setup notes; a plain `adb install` breaks Fast Deployment) |
| Corpus | `vcat-small.txt` — regenerated with `vcat generate-test-log --lines 50000 --seed 42`; device SHA-256 `1f35340b8882324c76352d87457d924a7a34325e573e69ea76ead9a7638b869c` is **byte-identical to §1.2's `small.txt`**, so §1.2's oracle (49,994 parsed, 7,181 `Rendering surface` matches) applies unchanged |
| Also pushed | `vcat-one-line.txt` (73 bytes, one `threadtime` record), `vcat-third-session.txt` (first 3,000 lines of the corpus) |
| Evidence root | `artifacts/live-test/20260821-remediation/evidence/` |
| Host helpers added | `tools/scripts/measure_targets.py` (48 dp audit of a `uiautomator` dump), `tools/scripts/dump_text.py` (read a dump's labels without a screenshot), `tools/scripts/cpu_sample.sh` (CPU% from `/proc` deltas over a window), `tools/scripts/ledger.py` (this ledger) |

**Declared limits of this pass.** One device, one API level, **Debug** rather than
Release, and no upgrade, Play, endurance, TalkBack-gesture, or destructive-storage
work. It re-tests the findings; it does not re-run the plan.

#### Verified

| Finding | What was measured |
|---|---|
| **F-20** | One-entry session opened at `DENSITY · 2 s · 2.21 ms/px` with **different** instants at each end of the axis (was `1 µs · 1.1 ns/px`, same instant twice). **Eight double-taps** in the plot: PID 31051 before, PID 31051 after, no `exit-info` crash row. The plot clamps at `905 µs · 1 µs/px` — exactly the minimum useful span §3's F-20 computed for this device — and stops. Zoom-in is now a bounded no-op. |
| **F-01** | Empty state reads `VisualCat 2.0.5-dev+b56fb5a · local-first · no telemetry`; `dumpsys package` reports `versionName=2.0.5-dev`. A screenshot now names its build. |
| **F-03** | `measure_targets.py` on the cold empty state: `OPEN LOG` 203 × 135 px = **72.2 × 48.0 dp**, `ON-DEVICE LIVE` 100.3 × 48.0 dp, `RECENT CAPTURES` 117.7 × 48.0 dp. **0 of 6 clickable nodes under 48 dp** (was 18.8 dp for all three). |
| **F-04** | `small.txt` → *Filters* → *Regex* → `(unclosed` renders, under the field in error ink: **“Not a valid regular expression: there are more "(" than ")" (position 9).”** The field is outlined in the error colour, the chip bar still says `No active filters`, and the entries list still shows 49,994 — the product no longer claims a filter it did not apply. The IME action key leaves the drawer open on the field. No resource key anywhere. |
| **F-05** | After the refused pattern, applying the valid `Rendering.surface` gives `7,181 in view · 7,181 match · 49,994 in session` (**exact §1.2 oracle**) and the status line reads `Ready · 49,994 entries` — read from the **accessibility tree**, so the visible text and the accessible name are the same string. No sticky `Failed`. |
| **F-06** | A filter matching nothing renders a card in the list's own region: **“No entry matches these filters”** / “The session holds 49,994 entries. Clearing the filters brings all of them back.” / **[Clear all filters]**. The region's accessible name is now that whole sentence, not a bare `Filtered log entries` on an empty `ListBox`. |
| **F-07** | The status bar carries `◀ 3,579 / 7,181 ▶`. Both buttons measure **135 × 135 px = 48.0 × 48.0 dp**, and the accessibility tree contains `Search match navigation`, `Previous search match`, `3,579 / 7,181`, `Next search match` — so the function is reachable by touch **and** by a screen reader. |
| **F-08** | *Entry → Source context* on the first `small.txt` entry prints ` 2058`, `▶2059`, ` 2060`. `sed -n '2058,2060p' small.txt` returns exactly those three lines in that order. The +1 disagreement with every line-numbering tool is gone. |
| **F-12** | Full-device capture, 6,391 entries. **Buffers facet: `main` 5,803 · `events` 493 · `system` 85 · `radio` 10.** Ground truth on the same device and window: `HfLooper` appears **436 times in `main` and 0 times in `system`, `crash`, `events`, `radio`, `kernel`** — and this session holds 614 `HfLooper` entries. Compare §3's F-12: `radio` 9,376 (80.5%) against `main` **6** (0.05%). The capture runs `logcat -b all -D -T 1 -v threadtime,year,UTC,usec`, confirmed in the process table. |
| **F-13** | **P0** (`READ_LOGS` revoked): the pre-capture dialog says “This capture will contain only VisualCat's own log lines… **Android will not ask you for anything: the permission a full-device capture needs is not one an app can request.** Granting it takes one command over adb…” followed by `adb shell pm grant com.barebit.visualcat android.permission.READ_LOGS`. The running notice carries the whole remedy verbatim plus a **Copy command** action. **P1** (granted): `mCurrentFocus=com.android.systemui.logcat.LogAccessDialogActivity` — the sheet the copy promises does appear, exactly when the copy promises it. |
| **F-14** | *Insights → Session info* during a live capture: **`State` → `Capturing`** (was `Importing`). After stop: `State` → `Ready`. No raw enum name is rendered. |
| **F-16** | Captures are named `On-device logcat 00h13m13` and `On-device logcat 00h25m52`. `Source` in Session info reads `Android · On-device logcat 00h13m13`. Nothing reads as a date. |
| **F-18** | `small.txt` reopened: the status line settles on `Ready · 49,994 entries` **with the rows bound**. The first attempt on device exposed a real bug in this fix (below); it was fixed and re-verified. |
| **F-21** | One-entry file: `Ready · 1 entry`. Templates pane: `1 entry`, `1 entry matching`, `1 entry · 14.3 % of current matches`. Stops: `Stopped · 14 entries kept`, `Stopped · 6,391 entries kept`. |
| **F-22** | Capture running, one child `logcat` (PID 5661). Tapping **Live** again: **still exactly one child `logcat`**, still one tab. The command-band button reads **`◉ Recording`**, with the accessible name *“Go to the running capture, On-device logcat 00h13m13”* and the description *“…is capturing. Tap to go to it; stop it there.”* |
| **F-26 / A-05** | With **three** sessions open and the rightmost selected: `Close session vcat-third-session.txt` measures **135 × 135 px = 48.0 × 48.0 dp** and `measure_targets.py` reports **0 of 15 clickable nodes under 48 dp**. Before the last fix in this pass the same node measured 54 px = **19.2 dp**. |
| **F-27** | `tiny`-style files opened through a real `ACTION_VIEW` content URI produce tabs named **`vcat-one-line.txt`**, **`vcat-small.txt`**, **`vcat-third-session.txt`** — the provider display name. No timestamp, GUID, document id, or `/storage/emulated/0/...` path anywhere in the tab or its accessible name. Re-delivering the same exact URI created **no** second tab (exact-URI idempotence intact). A second device pass exposed that a *reopened* session still showed the cache filename; that is fixed too (below). |

#### Partly verified

| Finding | Measured | Still owed |
|---|---|---|
| **F-15** | Comparable `/proc` deltas on one PID, screen on, USB powered. **Idle, no capture: 1.00 % over 15 s. Untouched P0 capture: 6.93 % and 7.00 % over 15 s.** `Surface::disconnect` in 10 s: **0** during a P0 capture and **0** during a full-device capture — §3 measured **≈50 per 10 s** during a quieter P0 capture. A full-device capture at 163 lines/s measured 49.85 % over 20 s, which is ingest work rather than refresh work. | Nothing — **§8.5 ran the soak** and F-15 is `Done`. This row stays as the screen-**on** half of the picture. |
| **F-02** | Verified on the **host**, which is where the CLI runs: `vcat generate-test-log --help` prints its usage, exits 0, and **writes no file**; `vcat generate-test-log --lines1000` prints `error: 'generate-test-log' does not take '--lines1000'.` and exits 2. | Nothing — there is no device surface for this. |

#### Closed by the Samsung continuation run

Section 6 now closes `F-09`, the landscape half of `F-10`, `F-11`, `F-17`,
`F-19`, `F-23`, `F-24`, and `F-25` on the Samsung. It also closes F-04's
Release-only evidence gap and the short-portrait query/guarded-Done defects the
continuation itself exposed. F-15's hours-long endurance verdict remains the
only implemented-finding validation gap.

#### Two defects this device pass found in the remediation itself

Recorded because they are the reason `Code done` is not the same as `Done`.

1. **F-18's first fix left the status stuck on `Opening`.** `LoadSnapshotAsync`
   has an early-return path for an unchanged snapshot; it published the loading
   tense and then returned before the announce that would have cleared it, so
   `small.txt` sat on `Opening · 49,994 entries` with every row bound. The path
   now reports the opened state directly when it is about to return without
   refreshing, and the loading tense only when a refresh really is coming.
2. **F-26's strip scroll never converged.** Three fixes were needed, and the
   third came from measurement rather than reasoning. A one-shot
   `BringIntoView(Rect)` was replaced by explicit offset arithmetic (no effect),
   then by a retry that stands until the chip is whole (no effect), and a
   `Console.WriteLine` trace read off `logcat` finally gave the numbers:
   `extent=656.7 viewport=433.8 offset=222.9` — **already at the maximum offset**
   — while the selected chip's own bounds ran to `686.5`. The scroll host's extent
   under-reports this content by ~30 logical px, so no scroll request could ever
   reach. `_tabChips` now carries a trailing margin of one touch target plus a
   gutter, which gives the extent the room the last chip's close button needs.
   The selected chip's close measured 48.0 dp immediately afterwards.

### 5.4 Deliberate deferrals

Recorded here rather than left implicit, so a later session does not re-derive
them or assume they were missed.

1. **F-01 — the Android `versionCode` formula is unchanged.** Google Play orders
   uploads by `versionCode` alone and refuses a duplicate, so perturbing the
   formula for candidate builds risks the publishing path for a problem
   `versionName` already solves: `dumpsys package | grep versionName` now reads
   `2.0.5-dev` on a non-release build, and `-p:ApplicationVersion=N` remains the
   documented override when a candidate genuinely needs its own code.
2. **F-24 — the notice lane still participates in the workspace's height.** Every
   reflow-free placement costs something worse: overlaying at the bottom covers
   the session status line (the defect a previous audit fixed by docking the lane
   in the first place), overlaying at the top covers the plot header, and a
   permanently reserved lane spends a band of a phone screen on emptiness in the
   state that holds almost all of the time. The gesture learns to distrust a
   coordinate that has just moved instead.
3. **F-27 — no content-hash identity for two provider URI forms of one file.** The
   report offers it as optional and flags the risk itself: two mutable documents
   that momentarily hash alike would be wrongly merged. The strict exact-URI
   oracle passes, and that is the contract A-06 actually states.
4. **F-15 — no soak.** ~~X-05/X-06 are not run here. §5.3 records a rate-of-work
   improvement, not an endurance verdict, and §3 is right that a screen-off,
   hours-long budget is what would close it.~~ **Discharged by §8.5**, which ran
   both legs on a Release build: idle 0.04 % / 0.11 %, capture **1.29 % mean over
   six 600-second windows** with the screen off. The hours-long half is honest
   about what it is — one hour of flat, non-drifting samples, not twelve.
5. **F-40 — a dialog body still does not re-resolve its own typography.** The
   sheet around it does: scrim, panel, border, heading, height cap and fade all
   answer a theme, size or text-size change, and the command sheet — a menu that
   holds nothing the reader has half-finished — is given a whole new body. A
   `DialogBody` is not, because rebuilding one would discard exactly the edit the
   reader opened it to make: a half-changed text scale, a chosen export order, a
   typed filename. Its own controls are theme-resourced and follow the variant on
   their own, so what is left stale is a handful of `TextScale.Of` sizes inside
   five dialogs until the sheet is next opened. Making that live needs the font
   sizes to be bound rather than resolved at build time, which is a change to how
   every view in the product is built — including the seam F-39 lives in — and
   not one to make from inside a remediation.

---

## 6. Continuation run — Samsung SM-G990B

This section is the live handoff for the follow-up run requested on the newly
connected physical device. It is deliberately separate from §5.3: results from
the Motorola are not silently transferred to a different OEM, viewport, or app
installation. Update this ledger after every material action and resume at the
first row that is not **Done**.

### 6.1 Step ledger

| Step | Status | Outcome / next action |
|---|---|---|
| C-01 Audit report, repository, and device | **Done** | The initial audit found 26 implemented findings, six pending Samsung checks, F-10/F-15 partial, and F-09 not started. Those are historical inputs; C-04/C-05 and the status table record their final disposition. The already-dirty remediation tree was intentionally preserved. |
| C-02 Record new-device baseline | **Done** | Samsung `SM-G990B` (`RFCRC0A9GND`), Android 16/API 36, `arm64-v8a`, fingerprint `samsung/r9qxeea/r9q:16/BP2A.250605.031.A3/G990BXXSKIZF1:user/release-keys`; 1080×2340 px at 480 dpi (3 px/dp); portrait with auto-rotate; font scale 1.0; dark mode; three-button navigation; locale `cs-CZ`; 100% battery/USB powered; about 90.6 GiB free on `/data`. |
| C-03 Update installed app before testing | **Done** | Built Debug with embedded assemblies (0 warnings/errors), then installed the exact signed APK with `adb install -r -t`; incremental install succeeded without clearing app data. SHA-256 `76E67512F87B9F31E5B30DE2C8D7E519CCE43F15FA606D995C72812062A4901A`, 75,576,004 bytes. Package `lastUpdateTime` advanced to 2026-08-22 00:40:22 while `firstInstallTime` stayed 2026-08-21 13:06:36 and `READ_LOGS` remained granted. Cold launch succeeded in 1,625 ms as PID 32611; the retained 440,614-entry session reached `Ready` with rows present. Evidence: `C-03-after-package-update.xml`, `C-03-ready.xml/.png`. |
| C-04 Verify pending fixes on Samsung | **Done** | F-10/F-11, F-17, F-19, F-23, F-24, and F-25 all pass their physical-device oracles. F-19 includes the controlled permission-kill and all recovery choices; F-23 includes a disposable non-zero exact-delete fixture; F-25 includes the busy `CanLoadMore` off-page branch. Evidence is preserved under the run directory. |
| C-05 Implement F-09 robustly | **Done** | Samsung's 360 dp compact-height floor now shows three complete 48 dp rows in both Split and Details, with hard clipping and a 9 px separation from status. The selected workspace command strip shares the landscape shell row; Load-more/count reuse existing header/tab space; focused query editing yields non-editor chrome. The original 4/6 oracle remains for a ≥434 dp device because six 48 dp rows cannot physically fit in this 360 dp viewport. Targeted Android-layout tests pass 29/29. |
| C-06 Regression and hand-back | **Done** | Final focused slice 35/35 and full solution 346/346; `git diff --check` clean. Final trimmed Release installed and live-tested. Auto-rotate, portrait rotation, granted `READ_LOGS`, disabled cleanup, hidden IME, zero capture children, and zero disposable fixtures verified at hand-back. F-15's hours-long soak remains explicitly deferred. |

### 6.3 Continuous execution log

#### C-04.1 — compact-height baseline before further changes

**Status: Done — three failures reproduced on Samsung.** The updated APK was
rotated to landscape by disabling auto-rotate and setting `user_rotation=1`.
This is a run mutation; restore `accelerometer_rotation=1` and
`user_rotation=0` at hand-back.

| Finding | Samsung measurement before the next fix | Result |
|---|---|---|
| F-09 Split | The entries `ListBox` is `[1031,738][2107,862]` (124 px / 41.3 dp high). Its first item is `[1031,738][2089,882]`; subsequent items continue through y=1080 while the status text occupies `[153,1024][2142,1074]`. Only the first row is complete and list content paints outside the list into the status/screen edge. | **Fail** |
| F-09 Details | The entries `ListBox` is `[189,687][2106,1015]`. Two 144 px / 48 dp rows are complete; the third is `[189,975][2088,1080]` and is clipped at the screen edge beneath the status line at y=1024–1074. | **Fail** |
| F-10 | With the landscape IME shown (`mInputShown=true`), the keyboard begins at approximately y=486. The drawer remains `[153,429][2142,1074]`; Reset and Done remain at y=903–1047. The command/mode chrome leaves only about 57 physical px above the keyboard, so the existing 104 dp guard cannot engage. | **Fail** |
| F-11 | `Search or regex…` is the right compact copy, but its node is `[213,561][532,666]` while its `TextBox` is only `[180,540][387,684]`: the accessible placeholder still overflows the field by 145 px. | **Fail** |

Evidence: `C-04-landscape-initial.xml/.png`,
`C-04-F09-before-details.xml/.png`,
`C-04-F10-F11-landscape-drawer.xml`, and
`C-04-F10-landscape-ime.xml/.png` under
`artifacts/live-test/20260822-samsung-remediation/evidence/`.

**Implementation consequence.** F-09 is not merely missing a row preference:
child rows are painting outside the list's arranged bounds. The next change must
make clipping/status separation an invariant first, then reclaim compact-height
chrome without shrinking actionable rows below 48 dp. F-10 cannot rely only on
an IME inset after the drawer starts below two 48+ dp command rows; focused
landscape editing needs a compact composition that temporarily yields
non-editing chrome. F-11 needs an actual width constraint on the placeholder,
not only shorter copy.

#### C-04.2 — pre-change automated regression baseline

**Status: Done — clean baseline.**
`dotnet test VisualCat.slnx -c Debug --no-restore` passed with 0 failures:
Domain 11, Core 88, Application 47, App 190 (**336 total**). This proves the
existing suite did not cover the three physical-device failures above; the next
implementation must add headless invariants where possible and retain the XML
geometry checks as the platform oracle.

#### C-05.1 — first compact-layout implementation

**Status: Code done; device verification pending.**

1. **One command row (F-09).** In compact height, `MainView` moves the selected
   workspace's actual Filters/Plot/Split/Details/Fit strip beside
   Open/Live/More. It is reparented—not copied—so enabled state, selection,
   labels, and accessibility metadata cannot diverge. The strip returns to row
   zero of its workspace on portrait, tab change, or teardown.
2. **Hard paint boundary (F-09).** Both the entries `ListBox` and its containing
   grid now clip to their arranged bounds. A virtualized row can no longer paint
   into the status band when less than one row remains. The compact Load-more
   action moves into the existing header at 430 dp rather than consuming a
   separate list footer band.
3. **Focused editor composition (F-10).** Focusing the compact-height search
   field hides the combined shell row until the editor/drawer closes, giving the
   pinned footer the whole area from the safe top inset to the IME. IME geometry
   is polled from `IInputPane.State/OccludedRect` as well as observed through its
   event, covering keyboards whose open event pre-dates the view subscription.
   The usable floor is now the 48 dp footer plus a 16 dp visible body slice
   (64 dp), not a 104/190 dp whole-card guess.
4. **One truthful search node (F-11).** The framework placeholder is disabled on
   phone composition. A separate non-interactive `TextBlock` is stretched and
   clipped inside the field's exact grid cell with character ellipsis; it is
   removed from the automation tree. The `TextBox` itself is named `Search
   message text or regular expression`, so accessibility gets complete meaning
   from bounds that belong to the actual field.

Files: `MainView.cs`, `SessionWorkspaceView.cs`,
`SessionWorkspaceView.Mobile.cs`, `SessionWorkspaceView.Interactions.cs`, and
`AndroidAuditFix2Tests.cs`.

Validation so far: App test project builds with 0 warnings/errors; all 29
`AndroidAuditFix2Tests` pass, including three new structural regressions for the
list clip boundary, symmetric one-owner command reparenting, and the accessible
search-hint design. This is not a device pass yet.

#### C-05.2 — first Samsung measurement after C-05.1

**Status: Partial; one more layout iteration required.** Android build succeeded
with 0 warnings/errors in 1m22s. The in-place install succeeded; artifact
SHA-256 is
`53BD6332DBAA7EB03BF61C9D3CE9BB040DED7D4A57584B56DBBC148D80A0A7DD`.
The Samsung reset `user_rotation` to 0 during package update even though
auto-rotate remained disabled, so landscape was re-locked with
`wm user-rotation lock 1`; this supersedes the earlier hand-back instruction—use
`wm user-rotation free` (then verify `accelerometer_rotation=1`,
`user_rotation=0`) at the end.

- The combined row works: Open/Live/More and
  Filters/Plot/Split/Details/Fit share y=111–255, all 144 px / 48 dp high. The
  separate workspace-command band is gone.
- **Details improved from two to three complete rows.** List
  `[189,543][2106,1015]` is 472 px / 157.3 dp; three exact 144 px rows finish at
  y=975. The fourth is clipped by the list at y=1015. Status begins at y=1024,
  leaving a 9 px gap; no pixels cross the semantic boundary.
- **Split is still below the compact floor.** List
  `[1031,594][2107,862]` is 268 px / 89.3 dp: one complete row and one partial.
  Its 42 dp Load-more footer remains at y=889–1015 because the split analysis
  column missed the first 430 dp threshold. The count and action controls also
  use separate bands. The invariant fix works (content clips at y=862 rather
  than painting into status), but the result is not usable enough.

Evidence: `C-05-first-landscape-locked.xml/.png` and
`C-05-first-split.xml/.png`.

**Next exact change.** In compact height, use the already documented short
`+500` presentation while retaining the full remaining count in its accessible
name/help text, move it into any ≥300 dp analysis header, and allow the existing
ellipsised count to share that row at the same threshold. This should return the
footer plus count-row height to the Split list without shrinking the 48 dp rows.

#### C-05.3 — second and third compact iterations

**Second iteration measured; third iteration code done and pending install.**

The second artifact (`D82FB6226158DDC2A4F4B2706AA328971F594DEBD0243A1162E113EEE164F27A`)
built with 0 warnings/errors, installed in place, and cold-launched in 1,641 ms.
In Split, Load-more now renders visually as `+500` in the existing 42 dp action
row while its Android node and help text retain the complete
`Load 500 more; 2,216 remaining`. The footer band disappeared and the list grew
from `[1031,594][2107,862]` (268 px / 1 complete row) to
`[1031,594][2106,1015]` (421 px / **2 complete rows** plus 133 px of the third).
The list still stops 9 px before status. Evidence:
`C-05-second-landscape.xml/.png`.

The remaining 11 px / 3.7 dp needed for the third full row comes from the count
line above the action row. The third iteration moves the same `_summary`
`TextBlock`—not a duplicate—into unused space at the end of the compact
Entries/Insights/Entry tab strip. Its visual compact form is
`N view · N session`; its automation name and tooltip remain the complete
scope/time sentence. In portrait it moves back to the entry header. This returns
the count row to the list while preserving both information and stable action
bounds. Targeted Android layout tests remain **29/29 passing**. Device install
and geometry measurement are next.

#### C-05.4 — F-09 Samsung result

**Status: Pass for the compact-height adaptive floor; original 434 dp oracle
still requires a taller-device recheck.** The third artifact SHA-256 is
`6F72ED6EF9A10FAE8B319C7083EABF6FB37CE70ADACD081F6D0F29717E92108E`;
build and in-place install succeeded with 0 warnings/errors, and cold launch was
1,620 ms.

On this device the usable landscape height is only 360 dp, 74 dp shorter than
the Motorola viewport that defined R-06's 4/6 target. Six 48 dp Details rows
alone require 288 dp, leaving 72 dp for Android's 30 dp status bar, the app
command row, tabs, actions, and status; that literal target is physically
impossible without violating the 48 dp touch/row floor or hiding navigation.
The existing code's documented `CompactEntryRowFloor=3` is therefore the honest
Samsung oracle; the 4/6 oracle remains applicable at the report's 434 dp
viewport.

- **Split:** list `[1031,543][2107,1015]` = 472 px / 157.3 dp;
  three complete 144 px / 48 dp rows end at y=975.
- **Details:** list `[189,543][2106,1015]`, the same three complete rows.
- In both modes, the next virtualized row is clipped at the list's y=1015
  boundary. Status begins at y=1024, leaving 9 px; no pixels or action bounds
  overlap it.
- The compact count occupies unused tab-strip width and remains readable as
  `2,716 view · 440,614`; its node retains the full scope/time sentence.
  `+500` remains 149 × 126 px (49.7 × 42 dp) in the existing action row, with
  the complete accessible name `Load 500 more; 2,216 remaining`.

Evidence: `C-05-third-split.xml/.png` and
`C-05-third-details.xml/.png`. F-09's former clipping/overlap is fixed; the only
declared gap is re-measuring the 4/6 row counts at ≥434 dp height.

#### C-05.5 — F-10/F-11 Samsung result and accessibility hardening

**F-10 status: Pass.** Opening Filters in 360 dp landscape and focusing Search
sets `mInputShown=true`; the keyboard begins at approximately y=486. The
focused-editor composition removes the combined command row and moves the
search field to `[180,207][387,351]`. Reset is `[1713,309][1905,453]` and Done
is `[1923,309][2115,453]`, so both remain fully visible and actionable with a
33 px / 11 dp gap above the IME. Typing `InputReader` and tapping the physical
center of Done closed the drawer, changed `mInputShown` to false, restored the
combined command row, and applied the query. Evidence:
`C-05-F10-ime.xml/.png` and `C-05-F10-done.xml/.png`.

**F-11 status: Pass for geometry; one accessibility-string rebuild pending.**
With the drawer open and IME hidden, the TextBox is
`[180,396][387,540]`; the separate visual placeholder is
`[213,443][364,492]`, entirely inside the field with a 23 px trailing margin.
The framework placeholder is empty, so there is no second expanding
placeholder owned by the TextBox. Android nevertheless includes the visual
`Raw` node in its UI dump and does not expose Avalonia's TextBox automation
name there. To make TalkBack meaning independent of that platform mapping, the
mobile TextBox help text now begins with the same complete label—`Search message
text or regular expression`—before its interaction guidance. A regression test
asserts that complete help text; rebuild/install and the final XML check are the
next action. Evidence for the fixed bounds: `C-05-F10-ime.xml/.png`.

The accessibility hardening test passed in the complete targeted fixture
(**29/29**, 0 failures). The temporary `InputReader` filter was then cleared on
device; the session returned from `0 view` to `2,716 view` while retaining all
440,614 entries. Evidence: `C-05-filter-clear-retry.xml`. This restores the
large retained session before the remaining interaction checks.

#### C-05.6 — final build/install and F-11 accessible-name oracle

**Status: Done.** The accessibility-hardened artifact built in 1m21s with 0
warnings/errors and installed in place without clearing data. Signed APK:
75,580,100 bytes, SHA-256
`BD4B621424487DDFB4B98E30F37827A97DBA48C767A6575E254881981A016EF0`.
`lastUpdateTime` advanced to 2026-08-22 01:09:24, `firstInstallTime` remained
2026-08-21 13:06:36, and `READ_LOGS` remained granted.

The final Android node for the empty search TextBox is `[81,732][841,876]` and
now exposes the full content description: `Search message text or regular
expression. Filters the entries as you type. The keyboard's action key applies
it and closes this panel.` Its visual placeholder is
`[114,778][817,829]`, fully inside the field. This closes F-11 on the Samsung
with both geometric and spoken-label evidence. Evidence:
`C-05-accessibility-final-open.xml/.png`.

#### C-04.3 — F-24 rapid Copy repetition

**Status: Pass (2/2).** In portrait Split, a row was selected and Copy raw was
enabled at `[547,1363][774,1507]`. Two ADB taps at its center were issued first
250 ms and then, after dismissing the notice, 350 ms apart. In both attempts the
completion notice read `Copied the raw text of 1 entry.`, the Entries tab
remained selected, Entry remained unselected, the app PID stayed 4729, and Copy
raw remained at the identical bounds after notice publication. The second tap
never opened the unrelated inspector. The run did not read clipboard contents.
Evidence: `C-04-F24-selected-before.xml`,
`C-04-F24-double-copy-after.xml/.png`, and
`C-04-F24-double-copy-after-2.xml`.

#### C-04.4 — F-23 cache-deletion safety, first Samsung pass

**Status: Partial pass; one robustness gap found and implementation next.** The
cache contained 2 sessions / 190.26 MiB: the open 190.24 MiB complete session
and a 16.63 KiB interrupted session. With the stored cleanup policy disabled,
Delete performed no mutation and said `Enable cleanup first. VisualCat never
deletes temporary sessions under the default policy.` Enabling the form locally
at its unchanged 30-day/unlimited values and pressing Delete skipped the
destructive prompt because the preview was empty, saying `Nothing is eligible
under this policy: older than 30 days, with no size cap. No session was
deleted.` Cancel then discarded the local edit, preserving the disabled stored
policy. Evidence: `C-04-F23-cache-initial.xml/.png`,
`C-04-F23-disabled-delete.xml`, `C-04-F23-enabled-preview.xml`,
`C-04-F23-zero-eligible.xml/.png`, and `C-04-F23-cancelled.xml`.

Reviewing the exact safety route exposed a remaining mismatch with F-23's
suggestion: the dialog protects only actively capturing paths, not every open
session, and automatic startup cleanup currently runs before workspace restore
so it cannot protect restored tabs. The shared cleanup engine accepts arbitrary
protected paths already. The next change will pass every open tab to the cache
dialog, run startup cleanup after restoration, pass those restored paths to it,
and add separate age-only, size-only, zero-eligible, and protected-session
service regressions. No session has been deleted in this run.

**Robustness implementation complete; device update pending.** `MainView` now
restores tabs and opens launch-intent sessions before automatic retention runs,
then passes every open session path as protected. The cache dialog separately
receives capturing paths for truthful `capturing` labels and all open paths for
deletion protection; preview and revalidated cleanup both use the latter. The
new service regression independently covers zero eligibility, age-only
selection, size-only selection, two protected paths (representing an open
complete tab and an active capture), and actual cleanup preserving both. Both
targeted cleanup tests passed (2/2), and the affected Android layout fixture
remains 29/29. Files: `MainView.cs`, `SessionDialogs.cs`, and
`SessionPersistenceTests.cs`. Rebuild/install and a final open-tab cache check
remain before F-23 is marked Done.

#### C-04.5 — F-17 and existing interrupted-session precheck

**F-17 status: Pass.** The only open tab was closed, the app was force-stopped,
and a 1,367 ms cold launch presented two recent cards. Their complete Android
nodes are human-only: `Reopen On-device logcat 13-08-17, 2026-08-21 16:21 ·
190.24 MiB · complete` and `Reopen On-device logcat 13-07-49, 2026-08-21 13:07
· 16.63 KiB · interrupted`; each content description is `Double tap to reopen
this capture.` A search of the entire dump found zero `/data/`,
`files/VisualCat`, or 32-hex GUID matches. Evidence:
`C-04-F17-empty-warm.xml` and `C-04-F17-empty-cold.xml/.png`.

**F-19 precheck: state mapping passes, responsive Session info needs one fix.**
Opening the retained 13-entry interrupted session produced the recovery notice
and the exact status `Interrupted · 13 entries recovered · the capture ended
before it was finished`; rows were present. The Session info selector works,
but on the 360 dp portrait composition its two-column rows reserve a fixed 148
dp label column inside an approximately 148 dp pane. Labels such as `State` are
visible while all ordinary values are arranged off the right edge, so the State
value cannot be read or verified there. The session ID is visible only because
it already uses a separate stacked layout. The next implementation will stack
every Session info label/value on phones, retaining the two-column desktop
layout, before the controlled process-kill test. Evidence:
`C-04-F19-existing-partial-open.xml/.png` and
`C-04-F19-existing-partial-session-info.xml/.png`.

#### C-04.6 — F-25 live selection lifetime

**Status: Core identity/source pass; busy-window marker fix built and device
update pending.** A real P1 capture obtained Samsung's one-time log-access grant,
ran one app-owned `logcat -b all -D -T 1 -v threadtime,year,UTC,usec`, and reached
full-device flow. With Follow enabled, the oldest visible row was selected and
opened: `sensors-hal`, timestamp `08-22 01:20:09.605831`, message beginning
`handle_sns_std_sensor_event:90…`. While the inspector remained open, the live
window advanced past 01:21:40 and then 01:22:20—well over the 30-second window.
The inspector still showed the identical tag, timestamp, complete message, and
the collapsed `SOURCE CONTEXT · exact bytes, after the │ divider` route. It
never fell to `No entry selected`; Entry remained enabled. This closes the data
loss/lifetime part of F-25 on Samsung. The capture was stopped normally at
10,164 entries and its `logcat` child exited. Evidence:
`C-04-F25-selected-immediate.png`, `C-04-F25-entry-immediate.png`,
`C-04-F25-entry-after35.png`, `C-04-F25-entry-source-after90.png`, and
`C-04-F25-stopped.xml/.png`.

The high-volume run exposed a narrower presentation branch: after the inspected
row aged out, the new window still had another 500-row page, so `CanLoadMore`
was true. `RestoreEntrySelection` returned early under the old finite-list
assumption; it preserved the inspector but did not show the promised `This
entry has scrolled out… / Show it` marker. The logic now recognizes an absent
identity during active Follow as off-page before considering `CanLoadMore`.
A new 650-row, >500-row-page headless regression advances a live viewport past
the selected id and asserts the explanation is visible and Entry stays enabled.
It passes with the two F-19 state tests (3/3).

**Final Samsung status: Pass.** On the final APK, a second real P1 capture
selected `WindowManager` at `08-22 01:46:16.863324` and kept its full message
open while Follow advanced the 30-second window. Returning to Entries displayed
`This entry has scrolled out of the live window. It is still open below.` and a
48 dp `Show it` action even though `Load 425 more · 425 remaining` proved
`CanLoadMore=true`. Activating Show it turned Follow off, restored the exact
purple `WindowManager` row and plot range, and kept Entry enabled. The capture
then stopped normally at 4,603 entries; its `logcat` child had already exited on
the first process poll, the tab became Complete, and status said `Stopped ·
4,603 entries kept`. Evidence: `C-04-F25-final-live.xml/.png`,
`C-04-F25-final-entry-selected.png`, `C-04-F25-final-marker-after.png`,
`C-04-F25-final-marker-entries.png`, `C-04-F25-final-show-it.png`, and
`C-04-F25-final-stopped.xml/.png`.

#### C-04.7 — controlled F-19 process-death recovery

**State status: Pass after two relaunches.** The combined F-23/F-25/F-19 layout
artifact built with 0 warnings/errors and installed in place: 75,251,669 bytes,
SHA-256
`C12D0FF550D04CEBD188DAFDA834DF7F953E0BFB9BE3E1F17540CA59287AC41E`;
`lastUpdateTime=2026-08-22 01:28:05`, original first-install time retained, and
`READ_LOGS` initially still granted. Cold launch was 2,251 ms.

A new full-device capture `On-device logcat 01h29m04` visibly reached 1,036
source lines with one app-owned `logcat`. Revoking `READ_LOGS` killed app PID
6536; Android exit info records `reason=8 (PERMISSION CHANGE)` at 01:29:32, and
the child process also exited. No Stop/finalize action ran. On the first cold
relaunch (2,145 ms), then again after force-stop and a second cold relaunch
(2,122 ms), the same 947 committed entries and the same four claims appeared:

- status: `Interrupted · 947 entries recovered · the capture ended before it
  was finished`;
- assertive recovery notice: everything that reached disk is exact and the
  post-save tail is absent;
- stacked Session info value: `Interrupted · what reached disk was recovered`;
- Recents: `On-device logcat 01h29m04 · 892.26 KiB · interrupted`.

The previously retained 13-entry partial produces the same mapping. The phone
Session info rows now stack label over value; `Source`, `Format`,
`Europe/Prague · captured as UTC`, and `State` all fit inside the narrow Split
pane. Evidence: `C-04-F19-before-revoke.xml/.png`,
`C-04-F19-after-revoke-relaunch1.xml/.png`,
`C-04-F19-after-revoke-relaunch2.xml/.png`,
`C-04-F19-killed-session-info.xml/.png`, and
`C-04-F19-killed-recents.xml/.png`.

**Recovery actions implementation: code done; final device check pending.** The
old deliberate deferral is now superseded. An interrupted banner carries a
Review action opening three explicit dispositions:

1. **Keep** acknowledges the notice while retaining the durable `interrupted`
   fact; it does not falsely finalize the manifest.
2. **Export recovered data** uses the existing scoped CSV picker for this exact
   tab, even if another tab becomes selected.
3. **Delete…** is enabled only for a direct `.vcat` child of VisualCat's cache,
   names the session and recovered entry count in a second irreversible
   confirmation, closes all handles, then deletes that one validated child.

Open-session tab semantics now say `interrupted`, `in progress`, `failed`, or
`complete` in their accessible name/help rather than making every tab sound
complete. `DeleteExactSession` reuses the cleanup engine's direct-child and
reparse-point validation; a new test proves an outside sibling cannot be
deleted. The recovered-action dialog test proves all three choices exist,
Delete promises confirmation, and Delete is disabled for an external session.
Focused App tests pass 32/32 and retention/deletion tests pass 3/3. Files:
`MainView.cs`, `MainView.TabStrip.cs`, `SessionWorkspaceView.cs`,
`SessionWorkspaceView.Interactions.cs`, `SessionDialogs.cs`,
`TemporarySessionRetentionService.cs`, and tests.

The permission mutation is already restored: granting `READ_LOGS` killed the
second-relaunch PID 7197 with the same Android `PERMISSION CHANGE` reason, and
`dumpsys package` now confirms `android.permission.READ_LOGS: granted=true`,
matching the new-device baseline. No app or `logcat` process remains at this
checkpoint; the final build can be installed without interrupting work.

#### C-04.8 — recovery-action device check and tab-semantic correction

**Actions pass; one event-order correction code done.** The final-action build
completed in 1m20s with 0 warnings/errors and installed in place: 75,584,196
bytes, SHA-256
`2DD6DAD7E34F98309F8E4D4067D3FC89A048E140A41D90FFCA1264BAE1FA5F31`;
`lastUpdateTime=2026-08-22 01:37:58`, original first-install time and granted
`READ_LOGS` retained; cold launch 2,326 ms.

The recovery notice now shows a 48 dp **Review** action. On the killed
947-entry capture it opened a pinned decision sheet whose Android tree contains
all three enabled choices: `Delete this recovered capture` (help explicitly
promises a second confirmation), `Keep this recovered capture`, and `Export
recovered data`. The body explains the exact 947-entry recovery and each
choice's consequence. Choosing Keep closed the sheet and confirmed `Kept
On-device logcat 01h29m04 as an interrupted capture; 947 entries remain
available`; the durable status below remained Interrupted. No deletion or
export was performed. Evidence: `C-04-F19-recovery-actions.xml/.png` and
`C-04-F19-recovery-keep.xml`.

The first cold frame exposed one last semantic timing bug: the tab initially
announced `Show in progress session…` even though the workspace had already
transitioned to RecoverablePartial. Chip semantics were refreshed on snapshots,
but `ReportOpened` changes Activity after that snapshot callback. `MainView` now
also observes `SessionTabViewModel.Activity`/`Title` and refreshes the chip,
unsubscribing on close. A direct regression asserts the interrupted tab name and
help. The affected App test slice now passes **33/33** (31 Android/audit layout,
semantics, and recovery-dialog checks plus the F-25 off-page-selection and F-19
unfinished-session checks). One rebuild/device dump remains to close this
subcheck.

The semantic-refresh patch then rebuilt in 1m18s with 0 warnings/errors and was
installed in place without clearing data. This exact signed Debug APK is
75,259,861 bytes, SHA-256
`7CF8B884649AC72BA334888308396C22EB8B9CADB6AF3B1966760AFE477591F0`;
package `lastUpdateTime=2026-08-22 01:44:40`, original first-install time and
`READ_LOGS: granted=true` retained. No capture child existed when the package was
updated. A 2,263 ms cold launch then exposed the corrected final semantics:
`Show interrupted session On-device logcat 01h29m04`, with help `This capture
ended before it was finalized; open it to inspect the recovered data.` The same
tree contains the recovery notice/Review action and status `Interrupted · 947
entries recovered · the capture ended before it was finished`. The sibling
complete tab independently says `Show complete session…`, proving the state is
per-tab rather than global. Evidence: `C-04-F19-tab-semantic-final.xml/.png`.
The event-order correction is **Pass** on Samsung.

The two non-mutating action branches were then exercised against that same
947-entry capture. **Export recovered data** opened Android DocumentsUI with the
exact proposed filename `On-device logcat 01h29m04.csv`; Back cancelled it, so
no private log export was written. **Delete this recovered capture** opened a
second confirmation titled `Delete recovered capture?`, naming the session and
stating that its 947 entries would be permanently removed and cannot be undone.
Cancel returned without deletion; the exact cache directory remains present.
Together with the earlier Keep check and automated exact-delete fixture, all
three recovery dispositions are now covered without destroying user data.
Evidence: `C-04-F19-actions-final.xml`,
`C-04-F19-export-picker.xml/.png`,
`C-04-F19-after-export-cancel.xml`, and
`C-04-F19-delete-confirm.xml/.png`. F-19 is **Done** on Samsung.

#### C-04.9 — F-23 non-zero destructive-preview fixture

**Status: Pass — disposable clone deleted, all four real sessions preserved.** Because every real
Samsung session is less than one day old and the UI's safe minimum age is one
day, no user session can exercise the non-zero branch without changing the
device clock or policy semantics. The Debug package's `run-as` access was used
instead to clone the smallest 13-entry cache session into the explicit test-only
direct child
`20000101-000000-F23-disposable-preview.vcat`. Only that clone's manifest
`updatedUtc` was changed to `2020-01-01T00:00:00+00:00`; the source session and
all user sessions are untouched. The clone was 272 KiB allocated on disk and
16.62 KiB by the app's file-size accounting; it was the only session that a
30-day/unlimited preview could select.

With cleanup enabled only in the uncommitted dialog form, the destructive
preview said exactly `Delete 1 session?` and `1 session · 16.62 KiB will be
permanently removed (older than 30 days, with no size cap). ·
F23-disposable-preview — 16.62 KiB`. Cancel left the clone present. Repeating
the preview and confirming produced `Deleted 1 session.`; `run-as test -d`
then returned exit 1 for the clone, while the four original cache directories
remained. Cancelling the cache dialog discarded its local checkbox change;
`settings.json` still contains `"temporaryCleanupEnabled": false`. There is no
run-created fixture left on the device. Evidence:
`C-04-F23-confirmation-first.xml/.png` and
`C-04-F23-after-disposable-delete.xml/.png`.

#### C-06.1 — final regression / Release-build hand-back checkpoint

**Status: Done.** Every newly implemented Samsung branch is device verified.
This checkpoint began by building and
installing one Release APK in place to close F-04's declared Release-only evidence
gap. Re-run an invalid regular expression against a retained session and require
the product-owned explanation rather than a framework resource key; then decide
whether to leave the representative Release build installed or restore the
provenance-recorded Debug artifact needed for any final diagnostics. F-15's
hours-long screen-off soak remains a separately declared endurance requirement,
not something a short interactive pass can honestly claim.

The trimmed/native Release package built in 1m20s with 0 warnings/errors and
installed in place successfully. Exact signed APK: 31,131,818 bytes, SHA-256
`9DB6E9724150A2CAB5FA7A3FBF685456028A9C8E5178CC2DEB8D8B450127741C`.
The package remains `versionCode=20005`, `versionName=2.0.5-dev`, and
`lastUpdateTime=2026-08-22 01:52:30`; first-install time, app data, and granted
`READ_LOGS` were preserved. The invalid-regex interaction is next.

#### C-06.2 — compact-height query field collapsed in a short portrait workspace

**Status: Done — fixed, regression-tested, and Release-verified.** Opening Filters while the
interrupted-session notice reduces the portrait workspace below the 520 dp
compact-height breakpoint placed QUERY/TIME in one half-column and SEVERITY in
the other. The same routine also moved Regex and Case-sensitive beside the
field. In the resulting roughly 180 dp query column, those options plus the
48 dp clear action consumed the entire row: the Android tree had Clear, Regex,
and Case-sensitive but no TextBox node, and the screenshot showed no editable
field. This is independent of Release trimming; the Release test merely exposed
the size combination. Evidence: `C-06-F04-release-filter-open.xml/.png`.

The robust composition will give QUERY a full-width first row in compact-height
mode, put TIME and SEVERITY side-by-side below it, and add a short-portrait
headless minimum-width regression. That preserves the no-wasted-column goal in
landscape while making the primary editor reachable under notices, split-screen,
and other height reductions.

**Implementation status: Code done; Release rebuild/device recheck next.** The
new `360 × 480` headless oracle first failed with a 64 dp editor, reproducing the
same structural problem without Android. QUERY now spans both compact columns;
TIME and SEVERITY share the second row; Regex/Case move beside the field only at
600 dp or wider and otherwise stay immediately below it. The affected slice now
passes **34/34**, including the new ≥96 dp editor invariant and all earlier
F-09/F-10/F-11, F-19, and F-25 checks. Files:
`SessionWorkspaceView.Mobile.cs` and `AndroidAuditFix2Tests.cs`.

The corrected Release rebuild completed in 46s with 0 warnings/errors and was
installed in place. Exact current signed APK: 30,807,483 bytes, SHA-256
`C1AB20037B4CB8957ED21CDDFFFC7546429685051D9B9C209AE57F15BE98A7C7`;
`lastUpdateTime=2026-08-22 01:58:06`, original first-install time and granted
`READ_LOGS` retained. This supersedes the pre-fix Release hash above; device
geometry and invalid-regex validation remain next.

**Device geometry: Pass.** With the same recovery notice still present, the
Release Android tree now exposes the TextBox at `[81,732][823,876]` (742 px /
247.3 dp wide). Regex and Case-sensitive occupy their own following row; Reset
and Done remain visible. Evidence: `C-06-F04-release-filter-fixed.xml/.png`.

**F-04 Release text: Pass; Done-behaviour polish found and code fixed.** With
Regex enabled, typing `(unclosed` produced exactly `Not a valid regular
expression: there are more "(" than ")" (position 9).` both under the field and
as the field's accessible description. The same tree says `No active filters`
and contains zero `MakeException`, `InsufficientClosingParentheses`, or
`ResourceKey` matches. The invalid query therefore remained refused in the
trimmed Release runtime. Evidence: `C-06-F04-release-invalid-typed.xml/.png`.

The visible Done action nevertheless closed the drawer even though the IME
action correctly kept an invalid field open. Done now uses the same guarded
commit and closes only after a valid/empty query; otherwise it leaves the error
in place for repair. A direct interaction regression is included, and the
affected slice passes **35/35**. One last Release rebuild/device Done check is
next; no filter or persisted setting was changed by the refused query.

The final guarded-Done Release rebuild completed in 47s with 0 warnings/errors
and installed in place. Exact current signed APK: 31,135,914 bytes, SHA-256
`43CF819C456E30E0BC64A9E23E576103FA6509B3DCD1A702F1AD6ACABCB10D84`;
`versionCode=20005`, `versionName=2.0.5-dev`,
`lastUpdateTime=2026-08-22 02:01:35`, original first-install time and granted
`READ_LOGS` retained. This is the hand-back candidate; only the final invalid
Done interaction, full tests, and state restoration remain.

**Final Release interaction: Pass.** The actual 48 dp Done button was activated
with `(unclosed` still present. The drawer stayed open on the red-outlined field,
the same product explanation remained visible/accessibly attached, and `No
active filters` remained true. Evidence:
`C-06-F04-release-final-done-actual.xml/.png` (the earlier
`C-06-F04-release-final-done.xml/.png` is the pre-action geometry frame). F-04's
Release-only evidence gap and C-06.2 are closed. The final Release APK remains
installed; full host regression and device-state hand-back are next.

**Full host regression: Pass.** `dotnet test VisualCat.slnx -c Debug
--no-restore` completed with 0 failures: Domain 11, Core 88, Application 49,
App 198 (**346 total**). This includes the final 35-test focused slice and all
unrelated solution tests. Device restoration and repository hygiene checks are
the only remaining C-06 actions.

**Repository/device hand-back: Pass.** `git diff --check` exits 0. The repository
was already broadly dirty on entry; no unrelated change was discarded or
reverted. On the Samsung, `wm user-rotation free` restored
`accelerometer_rotation=1` with `user_rotation=0`; the IME reports
`mInputShown=false`; package permission state is `READ_LOGS: granted=true`; one
VisualCat app process and zero `logcat -b all` capture children remain. The
Session cache form shows automatic cleanup unchecked, five legitimate sessions /
198.16 MiB (the fifth is this run's completed 4,603-entry F-25 capture), and no
`F23-disposable-preview` match. The cache dialog was cancelled without writing
policy. A final 1,945 ms cold launch left the final Release candidate open in
portrait. Evidence: `C-06-handback-more.xml`, `C-06-handback-cache.xml/.png`,
and `C-06-handback-final.xml/.png`.

### 6.2 Baseline notes

- Host baseline: 2026-08-21 22:37:49 UTC (2026-08-22 00:37:49
  Europe/Prague). No `AGENTS.md` exists in the repository.
- The package already held `android.permission.READ_LOGS`. This is pre-existing
  device state, not a mutation by this run. C-04 must explicitly exercise both
  P1 and P0 where relevant and return the permission to **granted** at hand-back.
- The app was open and focused at baseline. The update must be an in-place
  install so existing sessions needed by the pending recovery/retention checks
  are not erased. A clean-install claim will not be made.
- Baseline report SHA-256 before this section was added:
  `4DFF5FE1B7665D5F07F7815A766F6B0EC4C6B0EA1ED2AC7C2A68FFC3DA4B729E`.

---

## 7. Third-device run — Google Pixel 5 (API 34, gesture navigation)

This section is the live handoff for the third physical device. Like §6 it is
deliberately separate: nothing from the Motorola (API 36, three-button, 450 dpi)
or the Samsung (API 36, three-button, 480 dpi) is transferred to it. Update the
ledger in §7.3 after every material action and resume at the first row that is
not **Done**.

### 7.1 Why this device closes declared gaps

§1.1 declared nine coverage gaps. This device closes parts of three of them by
existing, before a single test is run:

| §1.1 gap | What this device adds |
|---|---|
| 1 — one OEM, one API level (36) | Google/`redfin`, **API 34** (Android 14). The two earlier devices were both API 36. `minSdk` is 31, so 34 is inside the supported range and was previously untested. |
| 3 — three-button navigation only | `navigation_mode=2` — **gesture navigation**. U-07's gesture insets were declared "only partly covered". |
| 4 / locale | The device locale list starts `cs_CZ`, so the app runs under a **non-English system locale** by default rather than by an app override. |

It also lands on a third density (**440 dpi, 2.75 px/dp**) and a third dp
viewport, which is the axis every touch-target and compact-layout finding is
measured on.

### 7.2 Run header and baseline

| Field | Value |
|---|---|
| Date/time (UTC) | 2026-08-22 00:24 → 02:15 |
| Host UTC vs device UTC | identical to the second at pre-flight (`Sat Aug 22 00:24:1x UTC 2026`) |
| Device | Google **Pixel 5** (`redfin`) |
| Serial | `0A031FDD400365` |
| Android release / API | **14 / 34** |
| ABIs | `arm64-v8a, armeabi-v7a, armeabi` |
| Build fingerprint | `google/redfin/redfin:14/UP1A.231105.001.B2/11260668:user/release-keys` |
| Screen / density | 1080 × 2340 px, 440 dpi (**2.75 px/dp**) |
| Configuration at baseline | `sw393dp w393dp h777dp … port night finger -keyb/v/h -nav/h` |
| App bounds (portrait) | `Rect(0, 136 – 1080, 2274)` → 136 px status inset, **66 px gesture inset** |
| Navigation mode | `2` — **gesture navigation** |
| Locale list | `[cs_CZ, zh_MO_#Hant, ru_MD, en_US, az_AZ_#Latn]` — Czech first |
| Theme | `night` (dark) |
| Font scale | `1.0` |
| Rotation | `accelerometer_rotation=1`, `user_rotation=0` (portrait, auto-rotate on) |
| Thermal status | `0` |
| Battery / power | 100 %, USB powered |
| Free space | 91 GiB available |
| Package at baseline | `com.barebit.visualcat` **`versionName=2.0.4-dev`, `versionCode=20004`**, installed 2026-08-21 12:11:14, `READ_LOGS: granted=true` |
| Evidence root | `artifacts/live-test/20260822-pixel5/evidence/` |

**Pre-existing device state, not a mutation by this run:** `READ_LOGS` is
already granted, auto-rotate is on, and the installed package is one release
behind (`2.0.4-dev`). The hand-back target is the same state with the package
updated.

**Host regression before any change:** `dotnet test VisualCat.slnx -c Debug`
→ **346/346 passed, 0 failed** (Domain 11, Core 88, App 198, Application 49).
This is the same total §6 handed back, so the tree is unmodified since C-06.

### 7.3 Step ledger

| Step | Status | Outcome / next action |
|---|---|---|
| D-01 Audit the report and the tree against the code | **Done** | Every §5.1 row except F-15 is `Done`; F-15 is `Code done`. Code audit confirms F-20 (`ZoomBounds`/`ZoomViewport`, no raw `MaximumSpan`), F-01 (`ReleaseChannel=stable` gate + `ProductInfo.BuildVersion`), and F-15's implemented half (Android refresh ceiling 6/s, in-flight coalescing, `LiveViewerPresence` suspend/resume wired to `OnPause`/`OnResume`). Host suite 346/346. |
| D-02 Record the third-device baseline | **Done** | §7.2. |
| D-03 Install the current build on the Pixel 5 | **Done** | In-place update `2.0.4-dev` → `2.0.5-dev`; `firstInstallTime` preserved, `READ_LOGS` re-granted, cold launch 2 332 ms. §7.4/D-03. |
| D-04 Re-measure the density-dependent findings at 2.75 px/dp | **Done** | 0 of 12 clickable nodes under 48 dp in the workspace; nothing intrudes into a system inset. The first cold launch also exposed D-04.0. |
| D-05 Gesture-navigation insets (§1.1 gap 3 / U-07) | **Done** | Found and fixed **F-28**: the back gesture took the plot's drag and left the app. |
| D-06 Landscape compact height on a third viewport (F-09/F-10/F-11) | **Done** | F-09's invariant holds at **341 dp**, 19 dp under the Samsung floor. Found and fixed **F-29** and **F-30**. |
| D-07 API 34 behaviour (F-12, F-13, F-22) | **Done** | Buffer attribution ground-truth verified, the consent sheet appears, one capture child under a double press, `Stopped · 2,777 entries kept`, clean teardown. Found and fixed **F-16's second half**. F-19's process-death branch was not re-run on this device. |
| D-08 F-15 — the declared soak | **Partly done** | Screen-off idle control measured (**0.13 % / 0.00 %** over two 300 s windows, same PID). The capture leg is blocked on the device credential after an unplanned reboot; scripts are checked in and resumable. §7.4/D-08. |
| D-09 Fix whatever this run finds, with host tests | **Done** | Five defects fixed, each with a host test that was proved to fail first where the defect was reproducible headlessly: D-04.0, F-28, F-29, F-30, F-16 (second half). |
| D-10 Regression, hand-back, commit and push to `main` | **Done** | Full solution **354/354**, `git diff --check` clean, device restored to its baseline (§7.5), committed and pushed to `main`. |

### 7.4 Continuous execution log

Entries are appended as they happen, so an interrupted session resumes here.

#### D-01/D-02 — audit and baseline

**Status: Done.** Recorded in §7.1–§7.3 above. No product source was changed.

#### D-03 — in-place update to the current build

**Status: Done.** `dotnet build src\VisualCat.Android\VisualCat.Android.csproj -c
Debug -t:Install -p:EmbedAssembliesIntoApk=true -p:AdbTarget="-s 0A031FDD400365"`
built in 1m30s with **0 warnings / 0 errors** and installed in place. The project's
`_GrantDebugLogPermission` target re-granted `READ_LOGS`.

| After the update | Value |
|---|---|
| `versionCode` / `versionName` | `20005` / `2.0.5-dev` (was `20004` / `2.0.4-dev`) |
| `firstInstallTime` | `2026-08-21 12:11:14` — **preserved**, so this was an update, not a clean install |
| `lastUpdateTime` | `2026-08-22 02:28:29` |
| `READ_LOGS` | `granted=true` |
| Launcher activity | `com.barebit.visualcat/crc64a1973b883a99125a.MainActivity` |
| Cold launch | `Displayed … +2s332ms`, PID 8785 |

The device carried one retained session from the 2.0.4 build,
`On-device logcat 12-12-02`, 58,781 entries, which restored automatically.

#### D-04.0 — two defects on the very first cold launch

**Status: Found, root-caused, fix designed.** Evidence:
`D03-cold-empty.png/.xml`, `D03-cold-empty-t2.xml`.

The first frame this device ever showed of the current build carried both:

1. A red failure notice reading **`Startup settings: The operation was canceled.`**
   with a *Dismiss* action.
2. A status line reading **`Opening · 58,781 entries`** — with every row bound,
   the plot drawn, and the final count already correct. It was still `Opening`
   five seconds later, and stayed there.

Neither is cosmetic and they are **one defect**, reproduced on both cold launches.

**Root cause.** `SessionTabViewModel.RefreshAsync` supersedes an older refresh by
cancelling its linked token:

```csharp
var previous = Interlocked.Exchange(ref _queryCancellation, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
previous.Cancel();
…
var token = _queryCancellation.Token;
await _loadLock.WaitAsync(token).ConfigureAwait(false);   // ← outside the try
try { … }
catch (OperationCanceledException) when (token.IsCancellationRequested) { }
finally { _loadLock.Release(); }
```

The body's catch makes supersession silent — but the `WaitAsync` that *queues for
the lock* sits **outside** that `try`. A refresh that is superseded while still
waiting for the lock therefore throws `OperationCanceledException` out of
`RefreshAsync` entirely. From there it propagates through `LoadSnapshotAsync` →
`OpenSessionAsync` → `RestoreWorkspaceAsync` → the one blanket
`catch (Exception)` in `MainView.LoadSettingsAsync`, which renders
`$"Startup settings: {exception.GetBaseException().Message}"`.

That single escape produces both symptoms:

* the notice is that exception's framework text, and
* `LoadSnapshotAsync` never reaches the line after the refresh —

```csharp
await RefreshAsync(cancellationToken).ConfigureAwait(false);
if (announceOpened is { } opened) { … ReportOpened(opened) … }   // ← never runs
```

  — so the tab keeps the `Opening` tense `BeginOpening` set, for the life of the
  tab. This is **finding F-18 recurring on the startup-restore route**: §5.3
  already records one instance of it ("F-18's first fix left the status stuck on
  `Opening`") on the *reopen* route.

**Why the first two devices did not show it.** It is a race — a second refresh has
to be requested while the first is still queued on `_loadLock`. This device is the
slowest of the three and restored a 58,781-entry session at launch; on the
Motorola and the Samsung the first refresh won.

**The F-04 half is wider than this one route.** A code audit for the same class of
defect — framework exception text rendered to a reader, which a trimmed Release
build turns into `MakeException, arg0, arg1` — finds **five** call sites that were
never routed through the `FriendlyMessage` helper F-04 introduced:

| Site | What it renders |
|---|---|
| `MainView.cs:2099` | `Startup settings: {GetBaseException().Message}` — the one observed here |
| `MainView.cs:2165` | the appearance/settings apply failure, bare `{GetBaseException().Message}` |
| `MainView.cs:2419` | `ReportFailure` — the funnel **every** `RunAsync`-wrapped action fails through. Its own doc comment says a bare framework message "does not say which action failed", and then interpolates one |
| `AdbCaptureDialog.cs:137` | device-list failure status |
| `ImportPreviewDialog.cs:173` | validation text, which can carry `TimeZoneNotFoundException` |

§5.3 verified F-04 only on the invalid-regex route. The remaining five are the
same defect on other routes.

**Fix designed (implemented next).**

| # | Change | Why |
|---|---|---|
| A | `RefreshAsync` guards the pre-lock `WaitAsync` so its **own** supersession returns quietly; a genuine caller cancellation still propagates | Root cause. The invariant becomes "a superseded refresh never throws, at any point in its life", which the body already assumed |
| B | `LoadSnapshotAsync` resolves the loading tense in a `finally` | Defence in depth for F-18: no route may leave a tab reading `Opening` forever, whatever the refresh does |
| C | Startup catches `OperationCanceledException` separately and uses `FriendlyMessage` plus an honest label for real failures | A cancellation is not a failure, and "Startup settings" was not what failed |
| D–F | `ReportFailure`, the appearance catch, `AdbCaptureDialog`, `ImportPreviewDialog` all route through `FriendlyMessage` | Closes F-04 on the four routes it was never applied to |

**Implemented.** Files: `src/VisualCat.App/Presentation/SessionTabViewModel.cs`
(A, B), `src/VisualCat.App/Views/MainView.cs` (C, D, E),
`src/VisualCat.App/Views/AdbCaptureDialog.cs`,
`src/VisualCat.App/Views/ImportPreviewDialog.cs` (F).

**Host tests — and they were proved to fail first.** Four new tests in
`LiveTestRemediationTests`:

* `ARefreshSupersededWhileQueuedDoesNotThrow` — three overlapping refreshes, so
  the second is cancelled while queued for the lock.
* `ACallerCancelledRefreshStillReportsCancellation` — silence is only for this
  refresh's own supersession; a caller who cancels still gets its
  `OperationCanceledException`.
* `OpeningASessionAlwaysEndsTheOpeningTense` — a reload racing two refreshes must
  not leave the tab in `SessionActivity.Opening`.
* `NoViewComposesUserTextFromAFrameworkException` — a source guard over
  `src/VisualCat.App/Views`. A helper only helps where it is called, and F-04
  came back on five routes precisely because nothing checked that it was. This
  is the test that would have caught it.

With fix A reverted, the two behavioural tests fail with
`System.OperationCanceledException : The operation was canceled.` — the same
sentence the device painted. With the fix, the slice passes **18/18**.

**Device verification: Pass.** Rebuilt (0 warnings/0 errors) and installed in
place. **Three consecutive cold launches**, PIDs 9016 / 9106 / 9190:

| | Before | After |
|---|---|---|
| Status line | `Opening · 58,781 entries`, permanently | **`Ready · 58,781 entries`** on all three |
| Failure notice | `Startup settings: The operation was canceled.` | **no notice nodes in the tree at all** (`Dismiss` count 0) |
| Tab accessible name | `Show **in progress** session …` | `Show **complete** session On-device logcat 12-12-02` |

The tab's own accessible name was wrong too, for the same reason — the stuck
tense made a finished session read as one still being written. Evidence:
`D04-cold-1/2/3.xml/.png`.

#### D-05 — F-28 · On gesture navigation, dragging the plot near either edge leaves the app

**Status: Found, reproduced 2 of 2 with a passing control, fix designed.**

- **Severity** Major · **Scenario** D-05 / §1.1 gap 3 (U-07) · **Device** Pixel 5, `navigation_mode=2`
- **New finding.** Neither earlier device could see it: both ran three-button navigation.

The plot's own accessible description is *"Drag to pan; pinch to zoom; double-tap
to zoom in; Fit shows the whole session."* Drag-to-pan is its primary gesture.

Measured on this device:

| Thing | Bounds (px) | Distance to screen edge |
|---|---|---|
| Heat map plot | `[33,733][1047,1044]` | **12.0 dp left, 12.0 dp right** |
| Minimap / viewport brush | `[245,1058][1011,1146]` | 89.1 dp left, **25.1 dp right** |
| System back-gesture strips | `[0,0][82,2340]` and `[998,0][1080,2340]` | **29.8 dp wide, each side** |
| `mSystemGestureExclusion` | `SkRegion()` | **empty — the app declares nothing** |

So the plot's outer 49 px on each side, and the right 13 px of the minimap brush,
sit underneath the system's back-gesture strips.

**Reproduction.** All three with the workspace open and no overlay:

| Gesture | Result |
|---|---|
| `input swipe 50 890 → 700 890` (starts 17 px inside the plot's left edge) | **Back fired. `mCurrentFocus` became `nexuslauncher`** — the reader is out of the app, on the home screen |
| `input swipe 1030 890 → 500 890` (starts 17 px inside the plot's right edge) | **Back fired. `mCurrentFocus` became `nexuslauncher`** |
| `input swipe 400 890 → 800 890` (control, same row, away from both strips) | **Pans correctly**: `234 in view` → `78 in view`, focus stays on VisualCat |

The process survives — this is not a crash — but the outcome of touching the plot
where it looks touchable is *leaving the workspace*, which is the worst available
answer for a pan. The brush's right edge is the same problem on the one control
whose description explicitly invites an edge drag ("drag either edge to resize").

**Why the plot cannot simply be inset.** Clearing both strips costs 59.6 dp of a
393 dp screen — 15 % of the plot's width, on the axis the plot exists to show.
Android's answer for exactly this case is `View.setSystemGestureExclusionRects`,
which asks the system to let the app have the touch instead. It is budgeted:
Android honours at most 200 dp of exclusion height per edge. The plot is 113 dp
tall and the minimap 32 dp, so 145 dp — inside the budget with room to spare, and
worth spending only on the surfaces that actually consume a horizontal drag.

**Fix designed.** The shared workspace names the rectangles that need the raw
gesture; the Android host is what knows how to ask for them.

**Implemented.** New `src/VisualCat.App/Platform/EdgeGestureGuard.cs`; new
`PlatformSourceRegistry.SetGestureExclusions` seam;
`SessionWorkspaceView` tracks `_timeline` and `_minimap`;
`VisualCat.Android/MainActivity.ApplyGestureExclusions` sets
`DecorView.SystemGestureExclusionRects`. The guard recomputes on `LayoutUpdated`
(the plot *moves* when the notice lane or the drawer appears, and a stale
rectangle is worse than none), coalesces to one publish per dispatcher turn, and
does nothing at all where the seam is not installed — the desktop pays one null
check.

**Host test.** `ThePlotAndTheMinimapClaimTheirOwnEdgeGestures` — in a 393 × 777
phone viewport, exactly two rectangles are published, each covering its control
in device pixels, and `Reset` gives them back. Slice **19/19**.

**Device verification: Pass — and the fix is surgical.**

`mSystemGestureExclusion` after the fix is
`SkRegion((33,733,1047,1044)(245,1058,1011,1146))` — **byte-identical to the
plot's and the minimap's accessibility bounds**, so no coordinate offset between
Avalonia's top-level and the decor view.

Behaviour, measured with a raw-framebuffer diff over the plot rectangle
(`tools/scripts/` companion `rawdiff.py`; an idle control measured **0.00 %**, so
0 % means "the plot did not move" rather than "not measured"):

| Gesture | Before | After |
|---|---|---|
| Drag from the **left** strip on the plot (x = 70) | Back fired → `nexuslauncher` | **plot pans, 7.95 % of the rectangle changed**, focus stays on VisualCat |
| Drag from the **right** strip on the plot (x = 1005) | Back fired → `nexuslauncher` | **plot pans, 7.91 %**, focus stays |
| Drag on the **minimap brush** inside the right strip (x = 1005) | Back fired | **brush resizes**: `104 in view` → `19,449 in view` |
| Drag from the left strip **outside** every claimed rectangle (x = 50, y = 1800) | Back fired | **Back still fires** → `nexuslauncher` |

The last row is the one that makes this a fix rather than a trade: Back keeps
working everywhere the app is not consuming a horizontal drag.

**A measurement note worth keeping.** The first after-fix attempts used x = 50 and
x = 1030 and showed 0 % — which looks like a failed fix and is not. The
`TimelineControl` node spans `[33 … 1047]` but its *drawable* plot runs about
`[60 … 1015]`; the rest is the severity legend's left margin and a right padding,
neither of which pans. A drag has to start inside both the gesture strip and the
drawable plot to test anything, which x = 70 and x = 1005 do. The exclusion is
still declared over the whole control, deliberately: the harm being removed is
"a touch anywhere on the plot leaves the app", not only "a pan is lost".

#### D-06 — landscape at a third compact height (341 dp)

**Status: Done.** This device's landscape configuration is `sw393dp w801dp h341dp` —
**19 dp shorter than the Samsung's 360 dp**, which is the floor §6's C-05 was built
for. The F-09 oracle holds at the new height, in both modes:

| | Entries list | Whole 48 dp rows inside it | Gap to the status line |
|---|---|---|---|
| Split | `[1108,494][2258,953]`, 166.9 dp | **3** (the 4th is clipped by the list, not by the status line) | **9 px** |
| Details | `[218,494][2258,952]`, 166.5 dp | **3** | **10 px** |

That is C-05's invariant — three complete rows, hard clipping, and a positive
separation from status — reproduced on a viewport it was not tuned for.

Two new findings came out of the same pass.

#### F-29 · The drawer's Clear action is 30.5 dp wide

- **Severity** Minor · **Scenario** D-06 · **Device-independent**

`measure_targets.py` on the open filter drawer: **`Clear the query` 84 × 132 px =
30.5 × 48.0 dp** — the one clickable control in the product still under the 48 dp
floor, in both orientations, with and without the keyboard. It is a one-glyph
button (`✕`) that had been left to measure to its glyph.

F-03 and F-26 both closed with "0 of N clickable nodes under 48 dp", and both were
true: those sweeps were run on the empty state, the workspace, and the tab strip.
**Nobody had run one with the filter drawer open.** A floor is only as good as the
panes it was measured on.

**Fixed.** `searchAction.MinWidth = 48` on mobile.
**Host test** `TheDrawerClearActionMeetsTheTouchFloor`.
**Device: Pass** — the drawer now reports **24 clickable nodes, 0 under 48 dp**
(`Clear the query` 132 × 132 px = 48.0 × 48.0 dp).

#### F-30 · A landscape keyboard slices the query field it was raised for

- **Severity** Major · **Scenario** D-06 / U-03 landscape · **Device** Pixel 5, 341 dp landscape

The Pixel 5's landscape keyboard takes **69 % of the screen**, leaving the drawer
**93 dp**. The drawer is a scroller with a pinned decision footer — the right shape
until the scroller is shorter than its own first row:

| | Before |
|---|---|
| Query field | `[210,184][1574,316]`, clipped by the scroller's 89 px viewport to **32 dp of its 48**, cut across the middle |
| Regex / Case-sensitive / Clear | sliced the same way |
| Reset / Done | whole, 48 dp |
| `QUERY` caption | scrolled above the card's top edge |

Scrolling cannot answer it: the row is taller than the viewport it would scroll in.

**Fixed — and the first fix was wrong in an instructive way.** The obvious change
is to move the query section into a pinned band *while the keyboard is up*. That
reparents a focused `TextBox`, which unmounts it, which drops focus, which makes
Avalonia withdraw the IME it had just asked for. The device said so exactly:

```text
D InputMethodManager: showSoftInput() view=…AvaloniaView… reason=SHOW_SOFT_INPUT
I ImeTracker: …onRequestHide at ORIGIN_CLIENT_HIDE_SOFT_INPUT reason HIDE_SOFT_INPUT_BY_INSETS_API
```

— the app asking for the keyboard and hiding it again in the same breath, leaving a
field that **could not be typed into at all, in either orientation**. That is the
same trap `ObserveInputPane` already records from finding 1, one layer down; the
first device pass caught it at the size-class level and this one caught it at the
element level.

The fix is therefore **structural, not conditional**: the query section is the
drawer's own first band in *every* state, above the scroller, so nothing is ever
reparented. `_mobileFilterBody` keeps only TIME and SEVERITY. When the drawer is
squeezed below 132 dp, `ApplyTightDrawerChrome` spends what chrome is left in a
declared order — the `QUERY` caption first (its words are already the field's
accessible name and its hint), then the padding around both rows.

| At 93 dp | Before | After |
|---|---|---|
| Query field | 32 dp, sliced | **48 dp, whole** |
| Clear / Regex / Case-sensitive | sliced | **48 dp, whole** |
| Reset / Done | 48 dp | 48 dp, **37 dp visible** |
| Typing into the field | ✔ | ✔ (`mInputShown=true`, text lands) |

**Residual, stated rather than hidden.** 93 dp cannot hold two 48 dp rows — that is
arithmetic, not a defect to fix. What this change decides is *which* row is whole,
and the field a reader is typing into is the right answer. Reset and Done keep
37 dp of visible, touchable height (up from 26 dp before the chrome trim).

**Portrait is unaffected and still passes F-10**: 269 dp drawer, caption shown,
Clear/Regex/Case-sensitive and Reset/Done all whole at 48 dp, TIME LENS reachable
by scrolling. `mInputShown=true`, typed text lands.

**Files** `SessionWorkspaceView.cs`, `SessionWorkspaceView.Mobile.cs`.
**Host tests** `TheQueryRowIsNeverInsideTheDrawerScroller` (393 × 341 — asserts no
`ScrollViewer` contains the Regex box, and the field is ≥ 48 dp tall and ≥ 96 dp
wide), `TheDrawerClearActionMeetsTheTouchFloor`. Slice **21/21**; App project
**203/203**.

#### D-07 — capture behaviour on API 34

**Status: Done.** One full-device capture, start to finalize, on an API level
neither earlier device covered.

| Finding | Oracle on this device |
|---|---|
| **F-13 (P1)** | With `READ_LOGS` granted, Android's own sheet appears on the capture press — `Povolit aplikaci VisualCat přístup ke všem protokolům zařízení?` / `Povolit jednorázový přístup` / `Nepovolovat`, in the device's Czech locale. The sheet the copy promises does appear on API 34, exactly when it promises. |
| **F-22** | Capture running with **one** child, `logcat -b all -D -T 1 -v threadtime,year,UTC,usec` (PID 16795). Pressing the capture control again: **still exactly one child, still one tab**, and the band reads *"Go to the running capture, On-device logcat 03h45m47"* / *"…is capturing. Tap to go to it; stop it there."* |
| **F-16 (first half)** | The capture is named **`On-device logcat 03h45m47`**. Nothing reads as a date. |
| **F-12** | 2,777 entries. Ground truth taken on the same device in the same window: `StatusBarIconController` appears **10 times in `main` and 0 times in `system`, `events`, `radio`, `crash`, `kernel`**; `HalDevMgr` **25 in `main`, 0 everywhere else**. The app labels every one of those rows **`main`** — and labels the event-log rows `events`. The retained 2.0.4 session on the same device labels the same tags **`radio`**, which is what F-12 was. The fix holds on API 34. |
| **F-21** | `Stopped · 2,777 entries kept`. |
| Teardown | **Zero** `logcat` children after the stop. |

#### F-16 (second half) · The tab strip cut the word out of every capture name

- **Severity** Polish · **Scenario** D-07 · **Device-independent**

F-16 named two things. The first — hyphenated triples reading as dates — was
fixed. The second was quoted in the same finding and was still true:

> Middle truncation compounds it: `On-device log…t 20-09-12` hides the word that
> identifies the source.

This device shows `On-device log…t 03h45m47`. The cause is arithmetic: the phone
tab budget was **24** characters and `On-device logcat HHhMMmSS` — the name this
product gives every capture it makes — is **25**. So the product's own default
name was middle-truncated on every phone, every time, and the one character it
was over cost two characters plus an ellipsis, taken out of the middle, which is
exactly where the word `logcat` is.

**Fixed.** `TabTitle.MobileBudget = 26` (that name's length plus one), named and
documented rather than left as a literal at two call sites; `DesktopBudget = 34`
likewise. A genuinely long imported filename still shortens from the middle,
which is what finding 28 asked for.
**Host test** `APhoneTabShowsAGeneratedCaptureNameWhole`.

**Host regression after all of §7's changes:** `dotnet test VisualCat.slnx -c Debug`
→ **354/354 passed, 0 failed** (Domain 11, Core 88, App 206, Application 49) —
eight new tests over §6's 346, no existing test changed.

#### D-08 — F-15: the screen-off soak

**Status: Idle control done; the capture leg is blocked on the device credential.**

F-15 is the report's one remaining implemented-finding validation gap (§5.4/4):
§5.3 measured a rate of work, not endurance, and §3 asks for "a screen-off
≤10 lines/s CPU/battery soak budget against the same idle baseline". This run set
out to close it.

**Idle control — done, and it is a better baseline than §5.3's.** App open on a
retained session, **screen off**, no capture, USB powered, `Thermal Status: 0`,
sampled from `/proc/<pid>/stat` (utime + stime, USER_HZ 100) on one PID:

| Window | Ticks | CPU |
|---|---:|---:|
| idle-1, 300 s | 38 | **0.13 %** |
| idle-2, 301 s | 0 | **0.00 %** |

Same PID (16719) before and after. §5.3's screen-*on* idle control was 1.00 %; with
the screen off this build's idle floor is **effectively zero**, which is what
`SuspendLiveViews` on `OnPause` is supposed to buy and had never been measured.

**The capture leg did not run.** The sequence that would have followed — start an
own-app (declined-consent, ≈5 lines/s) capture, screen off, six 10-minute windows,
then compare against the control above — needs the device interactive. Between the
two legs the notification shade wedged (`mCurrentFocus=NotificationShade`, `pidof`
empty, `screencap` returning 0 bytes), and a reboot to clear it left the device at
`deviceLocked=1, strongAuthRequired=0x1` — the after-boot credential prompt, which
no ADB command can answer and which nothing should try to.

**This is a blocked measurement, not a failed one, and F-15's status is unchanged
from §5.1: `Code done`.** The scripts are checked in and idempotent — resuming is
one command once the phone is unlocked:

```sh
sh tools/scripts/f15-soak.sh            # phase 1, idle control
sh tools/scripts/f15-soak-capture.sh    # phase 2, capture leg
```

Evidence so far: `evidence/F15-soak.log`.

### 7.5 Hand-back

Everything below was verified over ADB after the reboot, with the device locked —
none of it needs the screen.

| | Baseline (§7.2) | At hand-back |
|---|---|---|
| `accelerometer_rotation` / `user_rotation` | `1` / `0` | **`1` / `0`** — auto-rotate restored (this run locked it to landscape for D-06) |
| `READ_LOGS` | `granted=true` | **`granted=true`** |
| Package | `20004` / `2.0.4-dev` | `20005` / `2.0.5-dev`, **`firstInstallTime=2026-08-21 12:11:14` preserved** — every install this run was in place |
| Capture children | 0 | **0** |
| App process | running | none (post-reboot; the app is not auto-started) |
| IME | hidden | `mInputShown=false` |
| Files added to shared storage | — | **none** — this run pushed no corpus to the device and created no export |
| App data | 1 retained session (`On-device logcat 12-12-02`) | that session plus the two captures this run made (`03h45m47`, 2 777 entries) |

Two deliberate deviations from the baseline, both recorded rather than reverted:

1. **The package is one release newer.** That is the point of D-03; the device was
   a release behind.
2. **The device was rebooted.** Not planned: `am force-stop com.android.systemui`,
   used to clear a stuck notification shade, left SurfaceFlinger unable to produce
   a screenshot, and a reboot was the clean recovery. It cost the soak's second
   leg (above) and nothing else.

### 7.6 What this device changed about the product

Five defects, four of them invisible to the two earlier devices:

| Finding | Why only this device saw it |
|---|---|
| D-04.0 (`OperationCanceledException` escaping `RefreshAsync`) | A race that needs a second refresh to be queued behind the first. This is the slowest of the three devices and it restored a 58 781-entry session at launch. |
| **F-28** (edge drag leaves the app) | The first device with **gesture navigation**. §1.1 gap 3. |
| **F-30** (landscape keyboard slices the query field) | 341 dp landscape — 19 dp shorter than the Samsung, and a keyboard taking 69 % of the screen. |
| **F-29** (`Clear the query` 30.5 dp) | Device-independent, and missed by three passes because every 48 dp sweep was run on a pane that does not contain that button. |
| **F-16 second half** (tab strip cuts `logcat` out of every capture name) | Device-independent, quoted in the original finding, and left unfixed when the naming half was fixed. |

The two device-independent ones are the useful lesson: a third device found them
not because it is different but because it was a fresh pair of eyes over panes the
earlier sweeps had not opened.

---

## 8. Fourth pass — audit of the remediation, and the F-15 soak (Samsung SM-G990B)

This section is the live handoff for the fourth pass. Its brief was different from
§6's and §7's: not "run the plan on a new device" but **"check whether every
finding in this report has actually been addressed, and fix what has not."**
It therefore starts with a code audit of all 32 findings and only then goes to a
device, for the one item the audit could not close on the host.

Update the ledger in §8.3 after every material action and resume at the first row
that is not **Done**.

### 8.1 Which device, and why this one

§7 ended with the Pixel 5 locked behind an after-boot credential prompt, which is
where F-15's soak stopped. At the start of this pass the Pixel was not connected
at all; the only device answering ADB was the Samsung **SM-G990B**
(`RFCRC0A9GND`) — §6's device. The choice was put to the operator, who chose the
connected Samsung.

That is a better artifact than it first looks, for the one measurement this pass
owes:

| | §5.3 (Motorola) | §7/D-08 (Pixel 5) | This pass (Samsung) |
|---|---|---|---|
| Build class | Debug | Debug | **Release, non-debuggable** (`run-as` refuses it) |
| F-15 idle control | screen **on**, 1.00 % | screen **off**, 0.13 % / 0.00 % | screen off, Release |
| F-15 capture leg | not run | **blocked** on the credential prompt | the deliverable of this pass |

F-15 asks for "a screen-off ≤10-lines/s CPU/battery soak budget against the same
idle baseline". A Debug build is not the endurance answer that ships; this device
is already carrying a Release build, so the soak is measured on the artifact class
a user would actually run.

### 8.2 Run header and baseline

| Field | Value |
|---|---|
| Date/time (UTC) | 2026-08-22 09:25 → (device local is UTC+2, so device clocks read 11:25) |
| Repository commit at start | `c74fee3` — *Answer the Android live-test report, and let a third phone answer back* |
| Working tree at start | clean |
| Device | Samsung **SM-G990B** (`r9q`) |
| Serial | `RFCRC0A9GND` |
| Android release / API | 16 / 36 |
| ABIs | `arm64-v8a, armeabi-v7a, armeabi` |
| Build fingerprint | `samsung/r9qxeea/r9q:16/BP2A.250605.031.A3/G990BXXSKIZF1:user/release-keys` |
| Screen / density | 1080 × 2340 px, 480 dpi (**3.0 px/dp**) |
| Configuration at baseline | `mcc230-mnc3-cs-rCZ-ldltr-sw360dp-w360dp-h780dp-normal-long-port-night-xxhdpi-finger-navhidden-nonav` |
| Navigation mode | `0` — three-button |
| Locale | `cs-CZ` (system), LTR |
| Theme / font scale | `night` (dark) / `1.0` |
| Rotation | `accelerometer_rotation=1`, `user_rotation=0` |
| Thermal status | `0` |
| Battery / power | 100 %, USB powered |
| Free space | 91 GiB |
| Package at baseline | `com.barebit.visualcat` `versionName=2.0.5-dev`, `versionCode=20005`, `firstInstallTime=2026-08-21 13:06:36`, `lastUpdateTime=2026-08-22 02:01:35` |
| Build class at baseline | **Release** — `pkgFlags=[ HAS_CODE ALLOW_CLEAR_USER_DATA ]`, no `DEBUGGABLE`; `run-as` answers *"package not debuggable"* |
| `READ_LOGS` | **granted** (pre-existing, not a mutation by this run) |
| App state at baseline | not running; device on the launcher |
| Evidence root | `artifacts/live-test/20260822-samsung-audit/evidence/` |

**Host regression before any change:** `dotnet test VisualCat.slnx -c Debug` →
**354/354 passed, 0 failed** (Domain 11, Core 88, App 206, Application 49) — the
same total §7 handed back, so the tree is unmodified since D-10.

### 8.3 Step ledger

| Step | Status | Outcome / next action |
|---|---|---|
| E-01 Audit every §5.1 finding against the code | **Done** | All 32 findings have an artefact in the tree. F-15 is the only one whose status is evidence rather than code. §8.4/E-01. |
| E-02 Record the device baseline | **Done** | §8.2. Release (non-debuggable) build, `READ_LOGS` granted, 100 % battery, thermal 0. |
| E-03 Put the current commit's Release build on the device | **Done** | In-place update, `firstInstallTime` preserved, cold launch 1 554 ms. F-01 verified on a **Release** build for the first time. §8.4/E-03. |
| E-04 F-15 — the screen-off soak, both legs | **Done** | Idle 0.04 % / 0.11 %; capture **1.29 % mean over six 600 s windows** (range 1.21–1.39 %), one hour, same PID, one child, thermal 0 throughout. F-15 moves to `Done`. §8.5. |
| E-05 Fresh-eyes sweep over panes no earlier pass measured | **Done** | Ten panes opened; eight clean, two failed. Found **F-31**. §8.4/E-05. |
| E-06 Fix whatever this pass finds, with host tests | **Done** | **F-31**, **F-32** and **F-33** fixed, each with a host test proved to fail first — two of them reproducing the device's exact numbers (34 dp wide; `Stop capture ends at 659 dp in a 360 dp workspace`). Also closed a non-device gap: `c74fee3` carried the whole remediation and never updated `CHANGELOG.md`. §8.4/E-05, §8.4/E-06, §8.5. |
| E-07 Regression, hand-back, commit and push | **Done** | Full solution **359/359** (Domain 11, Core 88, App 211, Application 49 — five new tests over §7’s 354); `git diff --check` clean; `verify-docs.ps1` consistent; device restored to its baseline (§8.6); committed and pushed to `main`. |

### 8.4 Continuous execution log

Entries are appended as they happen, so an interrupted session resumes here.

#### E-01/E-02 — audit of all 32 findings, and the baseline

**Status: Done.** The audit was run against the code at `c74fee3`, not against
§5.1's own claims, because a status table can only be trusted as far as the tree
agrees with it. Each row was checked by locating the named artefact.

| Finding | Claimed in §5.1 | Found in the tree at `c74fee3` |
|---|---|---|
| F-20 | Done | `TimelineControl.ZoomBounds`/`ZoomViewport` at `:1164`/`:1170`; all six zoom routes (`:124`, `:288`, `:850`, `:950`, `:995`, `:996`) go through `ZoomViewport` — **no raw `MaximumSpan` call site survives** |
| F-01 | Done | `Directory.Build.props:27` — `VersionSuffix` = `dev` unless `ReleaseChannel == stable`; `ProductInfo.BuildVersion` |
| F-04 | Done | `src/VisualCat.Core/Query/SearchPattern.cs` present; `UseSystemResourceKeys=false` at `VisualCat.Android.csproj:21`; `WorkspaceViewModel.Presentable`/`Detail`/`IsPresentable`/`ErrorCode` |
| F-05 | Done | `src/VisualCat.App/Views/StatusLine.cs` present; no writable text property on it |
| F-07 | Done | `SessionWorkspaceView.cs:896`/`:912` — `Previous search match`, `Search match navigation` |
| F-09/F-10/F-11 | Done | compact-height path in `SessionWorkspaceView.Mobile.cs`; `ApplyTightDrawerChrome` |
| F-12 | Done | `OnDeviceLogSource.cs:145–156` — `-D` divider tracking, `buffers=all` |
| F-13 | Done | `MainView.cs:1628` verbatim remedy; `OnDeviceLogSource.cs:61` the exact `pm grant` line |
| F-14 | Done | `SessionCompletionText.State(...)`; `SessionCoordinator` branches on `Metadata.IsFinite` |
| F-15 | **Code done** | `WorkspaceViewModel.cs:41`/`:181` Android refresh ceiling 6/s; `SuspendLiveViews`/`ResumeLiveViews` at `:146`/`:153`, wired to `OnPause`/`OnResume` in `MainView.cs:117`/`:124`; `LiveViewerPresence` threaded to `SessionStoreWriter`. **The code is there; the endurance evidence is not.** |
| F-19 | Done | `SessionCompletion.cs`; `SessionActivity.RecoverablePartial`; `ReportOpened` |
| F-22 | Done | `MainView.cs:1097` — `Go to the running capture, {title}` |
| F-25 | Done | `_entryOffPageBanner` + `Show it` at `SessionWorkspaceView.cs:936`/`:950` |
| F-02 | Done | `Program.cs:62` comment and the early-help path |
| F-03 | Done | hero links at `TouchTarget.Minimum` |
| F-06 | Done | `SessionWorkspaceView.Presentation.cs:479` — `No entry matches these filters` |
| F-08 | Done | `SessionWorkspaceView.RawContext.cs:392` — the gutter is documented and drawn 1-based |
| F-16 | Done | `SourceMetadata.NameCaptureStartedNow`; `TabTitle.MobileBudget = 26` |
| F-17 | Done | `MainView.cs:807` — `Double tap to reopen this capture.` |
| F-18 | Done | `LoadSnapshotAsync` resolves the tense in a `finally` |
| F-21 | Done | `src/VisualCat.Domain/Counted.cs` |
| F-23 | Done | `SessionDialogs.cs:970` — the zero-eligible sentence; protected open paths |
| F-24 | Done | `SessionWorkspaceView.Interactions.cs:925–971` — `ObserveEntriesPosition` / `EntriesJustMoved`, 400 ms |
| F-26 | Done | `TouchTarget.For(mobile, …)` in `MainView.TabStrip.cs:215/238/239` |
| F-27 | Done | `MainActivity.QueryDisplayName` via `OpenableColumns.DisplayName`, `?? "shared-log.txt"` |
| D-04.0 | Done | `RefreshAsync` guards the pre-lock `WaitAsync`; `NoViewComposesUserTextFromAFrameworkException` guard test |
| F-28 | Done | `src/VisualCat.App/Platform/EdgeGestureGuard.cs`; `MainActivity.cs:369` sets `SystemGestureExclusionRects` |
| F-29 | Done | `SessionWorkspaceView.cs:1063` — `MinWidth = _mobile ? 48 : 0` |
| F-30 | Done | the query band is built outside `_mobileFilterBody`; `ApplyTightDrawerChrome` |
| F-16 (2nd half) | Done | `TabTitle.cs:18` — `MobileBudget = 26`, named |

**Audit verdict.** §5.1 is honest. Every finding it calls `Done` has an artefact
in the tree, and the one it calls `Code done` — **F-15** — is exactly that: the
implementation is complete and the *evidence* is missing. F-15 is therefore the
whole of this pass's outstanding work, and §8.5 is what it produced.

Two things the audit noticed that are not defects but are worth stating, because
neither is written down anywhere a later session would find it:

1. **The framework-exception source guard walks only `src/VisualCat.App/Views`.**
   A sweep of `Presentation/`, `src/VisualCat.Android/` and `src/VisualCat.Desktop/`
   for the same pattern finds nothing user-facing: the `Presentation` hits are
   inside `FriendlyMessage` itself (which is the helper), the `IsResourceExhaustion`
   / `IsDiskFull` hits are classification rather than display, and the four Android
   hits are `Android.Util.Log` calls that go to logcat, not to a reader. The guard's
   scope is correct; it was simply never justified.
2. **Compact-height landscape deliberately runs some chrome at 42 dp** —
   `SessionWorkspaceView.Mobile.cs:845/910/915/920` (analysis tabs, `_order`,
   `_loadMore`, `_fitMatches`, `_clearScope`, `Copy`, the inspector button). Entry
   rows keep 48 dp, which is what F-09's oracle measures. F-26's own suggested fix
   permits "an inline exception" that is *explicitly documented*; it is documented
   in the code and was not in this report. §8.6 records it.

**Baseline** is §8.2. Host suite **354/354** before any change.

#### E-03 — the current commit's Release build on the device

**Status: Done.** `dotnet build src\VisualCat.Android\VisualCat.Android.csproj -c Release
-p:EmbedAssembliesIntoApk=true` → **0 warnings, 0 errors**, 66 s. Installed in
place with `adb install -r -t` (the memory note's Fast-Deployment trap does not
apply to a Release build with embedded assemblies).

| | Value |
|---|---|
| Artifact | `src/VisualCat.Android/bin/Release/net10.0-android36.0/com.barebit.visualcat-Signed.apk` |
| SHA-256 | `6baba4226ca1e0ebc70fcfad75dbedb8813de10bfa4b07907b8e97456371112e` |
| Bytes | 30 815 675 |
| Install | incremental, `Success` in 1 447 ms |
| `firstInstallTime` | `2026-08-21 13:06:36` — **preserved**, so app data survived |
| `lastUpdateTime` | advanced to `2026-08-22 11:32:15` |
| `READ_LOGS` | still `granted=true` |
| Cold launch | `LaunchState: COLD`, **1 554 ms** |

**F-01 verified on a Release build, which §5.3 could not do.** The empty state
reads **`VisualCat 2.0.5-dev+c74fee3 · local-first · no telemetry`** — the running
commit, named by a Release artifact, which is exactly the confusion F-01 was
about. §5.3's evidence was a Debug build; this closes that half.

**F-03 re-measured at a third density** (480 dpi, 3.0 px/dp): **11 clickable nodes
on the cold empty state, 0 under 48 dp.** Evidence: `E03-cold.xml`.

#### E-05 — fresh-eyes sweep over panes no earlier pass measured

§7.6's lesson was that two of the third device's five findings were
device-independent and had simply never been looked at: *"a floor is only as good
as the panes it was measured on."* This step took that literally and opened every
pane the three earlier passes did not.

| Pane | Clickable nodes | Under 48 dp |
|---|---:|---:|
| Cold empty state | 11 | **0** |
| *More actions* sheet | 9 | **0** |
| *Recent sessions* | 2 buttons + 6 rows at 70.3 dp | **0** |
| **Appearance & timeline** | 18 (top) / 14 (scrolled) | **8 spinner buttons** |
| **Session cache** | 8 | **4 spinner buttons** |

Two of the five panes fail, in the same way, for the same reason.

##### F-31 · Every number field's spinner buttons are 34 × 46 dp

- **Severity** Minor · **Scenario** E-05 · **Device-independent** · **Found by** the fourth pass
- **Reproducibility** deterministic; every `NumericUpDown` in the product, both dialogs, both scroll positions

`measure_targets.py` at 480 dpi on the *Appearance & timeline* sheet and the
*Session cache* sheet:

| Control | Bounds (px) | dp |
|---|---|---|
| `Increase` / `Decrease text scale` | `102 × 138` | **34.0 × 46.0** |
| `Increase` / `Decrease live UI refresh limit in hertz` | `102 × 138` | **34.0 × 46.0** |
| `Increase` / `Decrease maximum zoom precision` | `102 × 138` | **34.0 × 46.0** |
| `Increase` / `Decrease minimum bar width` | `102 × 138` | **34.0 × 46.0** |
| `Increase` / `Decrease Maximum age (days)` | `102 × 138` | **34.0 × 46.0** |
| `Increase` / `Decrease Maximum total size (GiB, 0 = unlimited)` | `102 × 138` | **34.0 × 46.0** |

Twelve controls, all of them under the floor on **both** axes, and 34 dp wide is
narrower than F-29's 30.5 dp button was tall.

**Why every earlier sweep missed it.** `StretchForTouch` (`SessionDialogs.cs:674`)
does the right thing to the `NumericUpDown` *container* — `MinHeight = 48`, full
width — and the container measures exactly 48 dp (`[87,925][753,1069]` = 144 px).
The spinners are **template parts inside it**, inset by the control's border, so
they measure to 46 dp tall and to their own glyph's 34 dp wide. A sweep that reads
the container passes; only a sweep that reads the accessibility tree's leaf nodes
— which is what `measure_targets.py` does — sees them, and nobody had pointed it
at these two sheets.

This is F-29 exactly one level down: F-29 was a one-glyph button left to measure
to its glyph, and so is this, twelve times over.

**Not a defect, recorded so the next reader does not chase it.** In the same dump,
`Snap timeline cells to device pixels` first measured `733 × 82 px` = 244.3 ×
**27.3 dp**. That is a scroll artifact, not a target: the node sat at
`[87,2258][820,2340]`, below the dialog card's own bottom edge at `y=2196` and
clipped by the 2340 px screen. Scrolling the sheet down showed the same control at
`733 × 144 px` = **48.0 dp**. Every checkbox in both sheets is 48 dp.

**Fixed — at the seam, not at the twelve sites.** `SheetForm.NameSpinButtons` was
already the one place every numeric field goes through: it waits for the first
layout pass that realises the buttons (they are three templates deep, so
`TemplateApplied` is too early) and names them. That pass is also the only moment
their size can be set, so it is now `SheetForm.PrepareSpinButtons` and does both.
An eighth numeric field cannot omit it, because there is no other route.

```csharp
// Zero on the desktop, where a pointer is not a thumb.
var floor = TouchTarget.Here();
…
button.MinWidth = Math.Max(floor, button.MinWidth);
button.MinHeight = Math.Max(floor, button.MinHeight);
```

The width comes out of the field's text column, which is stretched to the sheet on
a phone and has about 222 dp for values like `1.00×` and `30`; the container grows
from 48 dp to 50 dp to hold 48 dp buttons inside its own border. The desktop is
untouched — `TouchTarget.Here()` is `0` there, and `Math.Max` leaves the theme's
own metrics alone.

**Files** `src/VisualCat.App/Views/SessionDialogs.cs`,
`src/VisualCat.App/Theme/TouchTarget.cs`.

**Host test — proved to fail first.**
`LiveTestRemediationTests.ANumberFieldsSpinButtonsMeetTheTouchFloor`. With the two
assignments removed it fails with

```text
Increase live UI refresh limit in hertz is 34 dp wide
```

— **the same 34 dp the device measured**, which is the point: this was headlessly
reproducible all along and no headless test had ever asked. That needed one new
seam, `TouchTarget.TouchOverride`, for the same reason
`SessionWorkspaceView.PhoneCompositionOverride` exists: the floor is zero on a
desktop, so a headless run measures every control against nothing and proves
nothing. Null means "ask the platform", which is what every shipping build does.

**Other panes re-verified on this device, at 480 dpi.**

| Pane | Result |
|---|---|
| Workspace, session open | **13 clickable nodes, 0 under 48 dp** |
| *Insights → Templates* | **13 nodes, 0 under 48 dp**; `Filter to` / `Mute` / `Copy selected template` all ≥ 48 dp |
| **Filter drawer (F-29's pane)** | **24 clickable nodes, 0 under 48 dp**; `Clear the query` **144 × 144 px = 48.0 × 48.0 dp** — §7's Pixel result reproduced exactly on a second device and a Release build |
| Analysis tabs (portrait) | `Entries` / `Insights` / `Entry` each **92.0 × 48.0 dp** |
| **F-16 (both halves)** | the tab reads **`On-device logcat 01h46m04`** — whole, no ellipsis, the word `logcat` present |
| **F-05 / F-18** | status line settles on **`Ready · 4,603 entries`** with rows bound; no `Opening` |

**Device verification — Pass, on the Release build.** Rebuilt (0 warnings/0
errors), installed in place, cold-launched. SHA-256
`ec5a62cbc02e6129b9f6f2cedfd1ae517b9657bb4a7faffa49e6de629bea46a8`,
31 144 106 bytes.

| Sheet | Before | After |
|---|---|---|
| Appearance & timeline (top) | 18 nodes, **7 under 48 dp** | 18 nodes, **0 under 48 dp** |
| Appearance & timeline (scrolled) | 14 nodes, **4 under 48 dp** | 14 nodes, **0 under 48 dp** |
| Session cache | 8 nodes, **4 under 48 dp** | 8 nodes, **0 under 48 dp** |

Every spin button now measures **144 × 144 px = 48.0 × 48.0 dp**. The field around
it grew from 48 dp to **50.0 dp** as designed (48 plus its own 1 px border on each
side) and its text column is **210.0 dp** — `30`, `0` and `1.00×` are as legible as
they were.

**And the new area is a hit area, not just a reported bound.** The `Increase text
scale` button used to occupy `x ∈ [750, 852]`; it now starts at `x = 666`. Two
taps at **x = 672** — 6 px inside the *new* left edge, and 78 px outside the old
button entirely — moved the value `1.00× → 1.05×`. *Cancel* then put it back to
`1.00×`, so this run changed no stored setting. Evidence:
`E06-appearance-fixed.xml`, `E06-appearance-fixed-scrolled.xml`,
`E06-cache-fixed.xml`, `E06-spin-works.xml`, `E06-cancel-restored.xml`.

**One thing that is not a defect, checked rather than assumed.** In the cache
sheet the two number fields did not respond to the enlarged buttons at all. They
report `enabled=false`, because *Enable automatic temporary-session cleanup* is
unchecked — the safe stored default §6/C-04.4 verified. A disabled control not
answering a tap is the correct behaviour, and the enabled field above proves the
mechanism.

**Two more panes swept while the fix built, both clean.**

| Pane | Clickable nodes | Under 48 dp |
|---|---:|---:|
| *Diagnostic bundle* confirmation | 2 | **0** |
| *Export CSV* | — | hands straight to Android's own Storage Access Framework picker; the one sub-floor node in that dump (`On-device logcat 01h46m04.csv`, 32.0 dp) is the **system picker's** filename field, not a VisualCat control |

The export was backed out with Back rather than completed;
`ls /sdcard/Download` confirms **no file was written**, so shared storage is
unchanged by this run.

#### E-06 — the remediation was never written down where a release would read it

**Status: Done.** Not a device finding, and not in §5.1, but squarely inside this pass’s brief. `CONTRIBUTING.md` item 3 requires `CHANGELOG.md` to be updated *"when you change behavior or the public surface"*, and `docs/RELEASE-CHECKLIST.md` gates a release on a changelog section for the version being released.

Commit `c74fee3` — the one carrying the whole remediation of §5, §6 and §7, 56 files and 10 390 insertions — **did not touch `CHANGELOG.md`.** `[Unreleased]` held exactly one entry, from the commit before it. So a release cut from this tree would have shipped a changelog that mentioned none of it: not the zoom crash, not the touch targets, not the gesture-navigation fix, not the buffer attribution, none of about thirty user-visible changes.

The fixes were real and tested; the record of them was missing, which is its own kind of not-addressed. `[Unreleased] → Fixed` now carries fifteen entries covering the whole remediation and this pass’s F-31, in the file’s established voice — what went wrong, then what changed — without finding IDs, which mean nothing to a reader of a changelog. `tools/verify-docs.ps1` passes: *"Checked 92 relative links across 43 Markdown files, required files, and version metadata. All consistent."*

#### E-04.1 — stopping the capture found two more defects, in the state the soak left behind

**Status: Found, root-caused, reproduced, causality proved.** The soak ended with
a notice on screen — the one F-13 added for a capture whose consent was declined
— and in that state the capture could not be stopped.

This was not a measurement. Two synthetic taps at the centre of *Stop capture*
did nothing, twice, which is exactly what a thumb would have experienced.
Measuring the tree explained why.

##### F-32 · A tall notice makes a portrait workspace compact, and the merged row clips *Stop capture* to 15 dp

- **Severity** Major · **Scenario** E-04 · **Device-independent** · **Found by** the fourth pass
- **Reproducibility** deterministic; 2 of 2 failed taps, and dismissing the notice restores it every time

Same session, same orientation, 360 dp portrait, minutes apart:

| Control | Capture just started (no notice) | After the notice appeared | Notice dismissed |
|---|---|---|---|
| **`Stop capture`** | `292 × 144 px` = **97.3 × 48.0 dp** | `45 × 144 px` = **15.0 × 48.0 dp** | `292 × 144 px` = **97.3 dp** |
| `Copy raw` | 75.7 × 48.0 dp | `Copy`, **49.7 × 42.0 dp** | 75.7 × 47.7 dp |
| `Show the full message of the selected entry` | 64.0 × 48.0 dp | **12.3 × 42.0 dp** | 64.0 × 47.7 dp |
| Clickable nodes under 48 dp | 0 | **4** | 1 (the scrolled tab chip, F-26's known behaviour) |

The screenshot shows what the numbers mean: the command strip reads
`Filters | Plot | Split | Details | Fit |` and then a bare sliver against the
right edge of the screen. That sliver is *Stop capture* — the one control that
ends a running recording.

**Root cause, and it is written in the code's own comment.**
`SessionWorkspaceView.Mobile.cs` merges the capture controls into the shell row
whenever compact height is selected:

```csharp
// A short viewport has width to spare and no height at all, so the capture
// controls move up beside the mode selector instead of taking a band of their
// own; a portrait phone keeps them on their own full-width row.
filterShell.ColumnDefinitions = new ColumnDefinitions(enabled ? "Auto,*" : "*");
```

*"A short viewport has width to spare"* is true of the viewport the compact
layout was built for — §6's Samsung landscape at 780 dp and §7's Pixel landscape
at 801 dp — and false here. The chain is:

1. the notice lane participates in the workspace's height (§5.4's deferral 2, deliberately);
2. this notice is tall, so the workspace drops under the compact-height threshold;
3. compact height merges `quickActions` **and** `captureActions` into one row;
4. that row needs roughly 500 dp and has **360**, so its last control is clipped off the screen.

"A portrait phone keeps them on their own full-width row" is the intent, and it
is keyed on the wrong thing: height, when the constraint is width.

**The same bug was already found and fixed one control over.** Twenty lines
below, the query row carries this gate and this comment:

```csharp
// Regex and Case-sensitive move beside the field only when the whole drawer is
// actually wide. A short 360 dp portrait workspace can select compact-height
// composition too; there the options stay directly below the editor so its
// width never collapses.
var beside = enabled && availableWidth >= 600;
```

That is §6's C-06.2. The lesson was drawn for the query row and not carried to
the row beside it.

##### F-33 · The notice drops the sentence it exists to deliver

- **Severity** Major · **Scenario** E-04 · **Device-independent** · **Found by** the fourth pass

The notice's accessible name carries the whole message. What is *drawn* is
`MaxLines = 6` with no trimming, and on a 360 dp phone the message needs about
eleven lines. The screenshot ends mid-clause:

> Only VisualCat's own log lines are being captured — log access was not allowed.
>
> Android asks for permission to read the device log on every capture, and this
> one was not allowed, so the capture can only see

— and the next words, which never appear, are **"Tap Live again and choose the
option that allows access."**

That sentence is F-13's fix. §3 recorded the defect as *"the notice omits the
required 'Tap Live again and allow' remedy"*; the copy was written, verified in
the accessibility tree, and is then cut off before a sighted reader reaches it —
with no ellipsis to say anything is missing and no way to scroll to it. A screen
reader hears the remedy; the eye does not. The fix was correct and the layout
defeats it.

##### Both fixed, at the place each one is actually decided

**F-32 — merge the capture row on width, not on height.** The decision now reads
the constraint that binds it, using the gate and the threshold C-06.2 already
established twenty lines below for the query options:

```csharp
var mergeCaptureRow = enabled && availableWidth >= 600;
```

The merged row needs roughly 500 dp: `Filters` 76 + three mode buttons 168 +
`Fit` 56 + `Follow` (which stretches, but wants ~90) + `Stop capture` 97, plus
spacing. 600 dp clears that with room and sits far below the 780 dp and 801 dp
landscape viewports §6 and §7 built the compact layout for, so **landscape keeps
exactly the composition it had.** Only a short *and narrow* workspace — the case
that was never designed for — gives the capture controls their own full-width row
back.

**F-33 — stop buying height by throwing the message away.** `MaxLines = 6` is
gone; the text sits in a `ScrollViewer` bounded to `NoticeTextMaximumHeight`
(108 logical px, about the six lines the cap used to draw). The lane therefore
takes **the same height as before** — so this does not undo F-32 — and the part
that does not fit is a scroll away instead of absent. The bound is applied on
both platforms rather than behind an `IsAndroid()` check: the lane is hidden on
the desktop anyway, so one behaviour is simpler than two and this one can be
asserted without a device.

**Files** `src/VisualCat.App/Views/SessionWorkspaceView.Mobile.cs` (F-32),
`src/VisualCat.App/Views/MainView.Notice.cs` (F-33).

**Host tests — both proved to fail first.**

| Test | Without the fix |
|---|---|
| `StopCaptureStaysOnScreenInAShortNarrowWorkspace` | **`Stop capture ends at 659 dp in a 360 dp workspace`** — 299 dp past the right edge, which is the device's clipping reproduced headlessly |
| `AWideShortWorkspaceStillMergesTheCaptureRow` | passes before *and* after — it exists to prove the fix costs landscape nothing, and it would fail if the merge had simply been removed |
| `ALongNoticeKeepsItsWholeMessageInABoundedLane` | `Assert.Equal() Failure: Values differ` on the message text |

**A test-shape note worth keeping.** The first attempt at F-33's test forced the
Android-only notice lane visible in a desktop composition, through a new
`MainView.NoticeLaneOverride` seam. That **hangs the headless layout** — the run
was killed at ten minutes against a normal suite time of about ninety seconds.
The seam was removed rather than worked around, and the test now asserts the
shape of the fix (no line cap, a bounded scroller) while how the lane *looks* at
360 dp stays device-verified. It is recorded here because the next person to
reach for that seam should know what it costs.

##### A third half the first device check exposed

Installing the first fix and reproducing the exact state showed `Stop capture`
back at **97.3 dp** — and **two controls still clipped**: `Copy` at 49.7 dp and
`Show the full message of the selected entry` at **12.3 dp**. Switching to
*Details* on the same screen, with the same notice, measured the same button at
**64.0 dp**. So the remainder was specific to **Split**.

`splitTimeline` had the identical defect one level up:

```csharp
var splitTimeline = enabled && timelineVisible && analysisVisible;
```

Two columns of a 360 dp portrait workspace leave the analysis pane about
**131 dp** (`(360 × 0.58) - 78`), and its actions are clipped with it. The same
gate closes it, and below the threshold the plot and the pane stack the way an
ordinary portrait workspace already does. The compact row structure and its
shorter chrome still apply, because those save *height*, and height is what is
actually short.

**Host test** `AShortNarrowWorkspaceStacksThePlotAndThePane` — one column at
360 × 340, **two** at 801 × 341, so the landscape composition §6 and §7 built is
asserted unchanged. Without the fix: `Assert.Single() Failure: The collection
contained 2 items`.

##### Device verification — Pass, on the Release build, in the exact state

Rebuilt (0 warnings/0 errors), installed in place, and the failing state
reproduced from scratch: cold launch, start a capture, decline Android's consent
sheet, so the same notice is on screen.

| | Before | After |
|---|---|---|
| `Stop capture` | **15.0 × 48.0 dp** | **97.3 × 48.0 dp** |
| `Follow` | not reachable in the merged row | **214.7 × 48.0 dp**, own full-width row |
| `Show the full message` (Split) | **12.3 × 42.0 dp** | **64.0 × 42.0 dp** |
| `Copy` (Split) | 49.7 × 42.0 dp | 49.7 × 42.0 dp |
| Clickable nodes under 48 dp | **4** | **1** — the horizontally scrolled tab chip, which is F-26's known and documented behaviour |
| The notice's last sentence | never drawn | **drawn**, after a scroll, with a visible scrollbar |

The screenshots are the clearest evidence: `E04-stop-clipped.png` shows the
command strip ending in a bare sliver against the right edge;
`E06-f32-fixed.png` shows `Follow ✓` and `Stop capture` on a row of their own,
whole; and `E06-f33-scrolled.png` shows the notice scrolled to
*"…Tap Live again and choose the option that allows access."* — the sentence
F-13 wrote and the layout had been eating.

The two remaining 42 dp heights are the compact-height chrome exception §6
introduced deliberately and §8.6 records; they are a height trade in a short
viewport, and this pass did not disturb it.

### 8.5 F-15 — the screen-off soak, both legs

This is the measurement §5.4/4 deferred, §3 asked for in these words —

> Add a screen-off ≤10-lines/s CPU/battery soak budget against the same idle
> baseline

— and §7/D-08 got half of before the Pixel locked itself behind a credential
prompt. It is the last thing standing between F-15 and `Done`.

**Conditions, identical across both legs.** Same device, same app process, same
Release build, screen **off**, USB powered, `Thermal Status: 0`, battery 100 %,
sampled from `utime + stime` in `/proc/<pid>/stat` (USER_HZ 100) on **one** PID
whose identity is checked before and after every window. The app is open on a
retained session throughout. Nothing touches the UI between the first sample and
the last, because being untouched is the condition under test.

#### Leg 1 — idle control (no capture)

| Window | Ticks | CPU |
|---|---:|---:|
| idle-1, 300 s | 13 | **0.04 %** |
| idle-2, 300 s | 33 | **0.11 %** |

PID 5252 before and after; zero `logcat` children throughout; thermal 0; battery
100 %. This is a **Release** build's idle floor with the screen off, and it is
lower than §7's Debug measurement on the Pixel (0.13 % / 0.00 %) and two orders
of magnitude below §5.3's screen-**on** control (1.00 %).

#### Leg 2 — an untouched own-app capture

**Scope.** `READ_LOGS` is granted on this device, so Android's own sheet appeared
on the capture press — *"Povolit aplikaci VisualCat přístup ke všem protokolům
zařízení?"* with *Povolit jednorázový přístup* / *Nepovolovat*. This run pressed
**Nepovolovat**, which is what makes the capture own-app and puts it under
F-15's ≤10 lines/s budget. Three findings answered for free on a Release build
while doing it:

| | Observed |
|---|---|
| **F-13 (P1)** | the sheet the copy promises **does** appear, on the press, with the permission held |
| **F-22** | exactly **one** `logcat` child, PID 6961, `logcat -b all -D -T 1 -v threadtime,year,UTC,usec`; the command band reads *"Go to the running capture, On-device logcat 12h01m03"* / *"…is capturing. Tap to go to it; stop it there."* |
| **F-14 / F-16 / F-21 / R-22** | `Capturing · 2 lines received · no source lines for 6s · On-device logcat` — the live tense is *Capturing* and not *Importing*, the name is an unambiguous start time, the counted noun agrees, and the quiet interval is stated rather than implied |

Capture started 2026-08-22 12:01:03 device-local. Six consecutive 600-second
windows, screen off, untouched:

| Window | Seconds | Ticks | CPU |
|---|---:|---:|---:|
| capture-1 | 601 | 783 | **1.30 %** |
| capture-2 | 601 | 727 | **1.21 %** |
| capture-3 | 600 | 747 | **1.24 %** |
| capture-4 | 600 | 814 | **1.36 %** |
| capture-5 | 600 | 834 | **1.39 %** |
| capture-6 | 600 | 749 | **1.25 %** |

**One hour, mean 1.29 %, range 1.21 - 1.39 %.** PID 5252 before the first window
and after the last; the capture child (PID 6961) alive at every checkpoint and
`01:03:03` old at the end; `Thermal Status: 0` at every checkpoint; battery 100 %
throughout.

#### What this settles

| F-15 asked | Answer |
|---|---|
| Is the refresh cost **bursty**? | **No, not any more.** §3's screen-on samples were 17.8, 17.8, 25.0, 17.8, 21.4 % and the finding's own word for them was "bursty"; a later quiet run gave 3.4 % and 11.1 % in the same state, which is what made it a *risk* rather than a *number*. Six hourly-scale windows here span **0.18 percentage points**. The distribution is flat, which is the thing that could not be claimed before. |
| Is it **within budget against the same idle baseline**? | **Yes.** Idle floor on the same device, same build, same conditions: 0.04 % / 0.11 %. An untouched own-app capture costs about **1.2 percentage points over idle**, with the screen off. |
| Does it **survive** four to twelve hours? | **One hour, measured, with nothing degrading**: no process death, no child death, no thermal rise, no upward drift across windows (the highest window is the fifth, the sixth is back down). §3 feared "battery drain, heat, and process death over four to twelve hours". Heat and process death are answered for an hour and show no trend; twelve hours is still an extrapolation from a flat line rather than a measurement. |
| **Battery?** | **Not measurable in this run, and it would be dishonest to claim it.** The device was USB-powered at 100 % for every window, which is what keeps the CPU comparison clean, and it is also what makes the battery figure meaningless. What is measured is the thing battery drain is a function of: CPU time. |

#### Against the two earlier passes

| | Build | Screen | Idle control | Untouched capture |
|---|---|---|---|---|
| §5.3, Motorola | Debug | **on** | 1.00 % | 6.93 % / 7.00 % (15 s windows) |
| §7/D-08, Pixel 5 | Debug | off | 0.13 % / 0.00 % | **blocked** |
| **§8.5, Samsung** | **Release** | **off** | **0.04 % / 0.11 %** | **1.29 % over 6 x 600 s** |

§5.3's screen-on capture numbers and this leg's screen-off numbers are not the
same measurement and are not presented as one: the difference between them is
exactly what `SuspendLiveViews` buys, and it is about **5.5 percentage points**.

**F-15's status changes from `Code done` to `Done`,** and §5.4's fourth deferral
- "F-15 - no soak" - is discharged. The scripts are checked in and parameterised
by `ANDROID_SERIAL`, so the same measurement can be repeated on any device:

```sh
ANDROID_SERIAL=<serial> sh tools/scripts/f15-soak-samsung.sh idle
ANDROID_SERIAL=<serial> sh tools/scripts/f15-soak-samsung.sh capture
```

Evidence: `evidence/F15-soak.log`, `E04-precapture.xml`, `E04-capturing.xml`.

### 8.6 Hand-back

| | Baseline (§8.2) | At hand-back |
|---|---|---|
| `accelerometer_rotation` / `user_rotation` | `1` / `0` | **`1` / `0`** — this run never rotated the device |
| System font scale | `1.0` | **`1.0`** |
| Configuration | `sw360dp w360dp h780dp port night` | **identical** |
| `READ_LOGS` | `granted=true` | **`granted=true`** — never revoked or granted by this run |
| Package | `20005` / `2.0.5-dev`, Release | `20005` / `2.0.5-dev`, Release, **`firstInstallTime=2026-08-21 13:06:36` preserved** — every install was in place |
| Capture children | 0 | **0** |
| IME | hidden | **`mInputShown=false`** |
| Battery / thermal | 100 %, `Thermal Status: 0` | **100 %, `Thermal Status: 0`** |
| Files added to shared storage | — | **none** — the two `uiautomator` dumps this run wrote to `/sdcard` (`d.xml`, `e03.xml`) were deleted; the export was backed out before writing and `/sdcard/Download` still holds its original 4 entries |
| Stored settings | text scale `1.00×`, cleanup disabled | **unchanged** — every settings sheet this run opened was left with *Cancel*; the one value it changed to prove a hit area (`1.00× → 1.05×`) was cancelled back |

**Deliberate deviations, recorded rather than reverted.**

1. **The package is newer than the baseline artifact**, three times over: this
   pass built and installed the current commit, then the F-31 fix, then the
   F-32/F-33 fixes. All in place; `firstInstallTime` never moved.
2. **App-private data holds three more sessions than the baseline** — the
   soak's own capture (`12h01m03`, 218 entries, complete), the capture that was
   live when the app was force-stopped to install the fixed build
   (`13h47m50`, interrupted), and the verification capture (`13h54m06`,
   45 entries, complete). They are the evidence for §8.5 and for F-32's
   before/after, so they are kept rather than deleted.
3. **Two files from §6's run remain on `/sdcard`** (`C-04-F19-tab-semantic-final.png`
   and `.xml`, both timestamped 2026-08-22 01:45). They are not this run's, and
   deleting another run's evidence is not this run's call. Noted so the next
   pass does not attribute them here.

**One thing verified for free at hand-back.** The restore brought both sessions
back, and the interrupted one carried F-19's notice verbatim — *"This capture was
interrupted. Everything below reached disk and is exact; anything the source
produced after the last save is not in the session."* — with its **Review**
action, and the tab read *"Show interrupted session On-device logcat 13h47m50"*.
That is §6/C-04.7's work holding on a Release build, on a process death this pass
caused by accident rather than by design.

### 8.7 What this pass changed about the report

Four things, and only one of them was on the brief.

1. **F-15 is `Done`.** The soak ran, both legs, on a Release build. §5.4's
   fourth deferral is discharged and §5.1 has no `Code done` row left.
2. **Three new findings**, all device-independent, all in panes or states no
   earlier pass had looked at: **F-31** (twelve spin buttons at 34 × 46 dp),
   **F-32** (a tall notice clips `Stop capture` to 15 dp in a narrow workspace)
   and **F-33** (the notice drops the remedy sentence it exists to deliver).
3. **A record that was missing.** The commit carrying the entire remediation
   never updated `CHANGELOG.md`, which `CONTRIBUTING.md` requires and the release
   checklist gates on (§8.4/E-06).
4. **Two stale statements corrected**: F-04's §5.2 entry still said a Release
   check was "Still owed" after §6 had supplied it, and §5.3's F-15 row still
   said a soak was owed.

**The pattern, for whoever runs the fifth pass.** §7.6 concluded that its two
device-independent findings were found "not because it is different but because
it was a fresh pair of eyes over panes the earlier sweeps had not opened." All
three of this pass's findings are the same shape, and they sharpen the lesson:

- **F-31** was invisible to every sweep because the sweeps measured **containers**
  and the defect was in **template parts** inside them. A control that passes at
  its own bounds can still fail at the bounds of the thing a thumb lands on.
- **F-32** and **F-33** were invisible because no sweep had been run **while a
  notice was on screen**. Every measurement in §5, §6 and §7 was taken in a
  quiet state. The states a product is *in trouble* in are exactly the states
  its controls matter most in, and they had never been measured.
- F-32 in particular is the third instance of one root cause: a compact layout
  chosen by **height** that assumes **width**. C-06.2 found it on the query
  options, this pass found it on the capture row and again on the split
  composition. Anything else in that method still keyed on `enabled` alone is
  worth a look.

**Declared limits of this pass.** One device, API 36, Release but **debug-signed**
(`CN=Android Debug`), so §1.1's gap 2 is narrowed and not closed. The soak is one
hour, not the four-to-twelve §3 speculates about, and it is USB-powered so it
measures CPU rather than battery. No TalkBack, no upgrade/Play, no
destructive-storage and no locale work was attempted.

---

## 9. Fifth pass — the lead §8.7 left open, on the Motorola

This section is the live handoff for the fifth pass. Its brief was §8's brief
again — **"check whether every finding in this report has actually been
addressed, and fix what has not"** — with one difference: §8.7 ended by naming
an unfinished thread rather than a finding, and this pass starts by pulling it.

> **§8.7:** F-32 in particular is the third instance of one root cause: a compact
> layout chosen by **height** that assumes **width**. C-06.2 found it on the query
> options, this pass found it on the capture row and again on the split
> composition. *Anything else in that method still keyed on `enabled` alone is
> worth a look.*

Update the ledger in §9.3 after every material action and resume at the first row
that is not **Done**. Every entry in §9.4 is written when it happens, not at the
end, so an interrupted session resumes from the last one.

### 9.1 Which device, and why

The Motorola **edge 60 pro** (`ZY22M4T2Z4`) — §1's and §5.3's device, and the one
the operator asked for. It is the right device for this brief for a reason the
brief did not anticipate:

| | §6/§8 Samsung | §7 Pixel 5 | This pass (Motorola) |
|---|---|---|---|
| Portrait width | 360 dp | 393 dp | **433.8 dp** |
| Density | 3.0 px/dp | 2.75 px/dp | **2.8125 px/dp** |

The compact-height defects of §8 (F-32) were all found at **360 dp**. A third
portrait width tells the difference between a fix that is correct and a fix that
happens to be correct at 360.

### 9.3 Step ledger

| Step | Status | Outcome / next action |
|---|---|---|
| G-01 Audit every finding against the tree at `7cbf352` | **Done** | All 35 findings have an artefact in the tree; no regression against §8.4/E-01. §9.4/G-01. |
| G-02 Record the device baseline | **Done** | §9.4/G-01. 434 dp portrait, 450 dpi, three-button, en-US, thermal 0. |
| G-03 Put the current commit's Release build on the device | **Done** | In-place, `firstInstallTime` preserved, `DEBUGGABLE` gone, cold launch 1 896 ms, 15 nodes / 0 under 48 dp. §9.4/G-01. |
| G-04 Chase §8.7's lead: the gates still keyed on `enabled` alone | **Done** | Found **F-34** (Blocker) and **F-35**; four gates now share one named, text-scale-aware breakpoint. §9.4/G-04. |
| G-05 Fresh-eyes sweep over states no earlier pass measured | **Done** | The narrow compact-height workspace, its filter drawer, and the shared row with a capture running — three states no pass had measured. Found **F-36** and **F-37**. §9.4/G-04, G-06, G-08. |
| G-06 Fix what this pass finds, with host tests proved to fail first | **Done** | **F-34**, **F-35**, **F-36** and **F-37** fixed; six host tests, three proved red first (`544 dp`, `887 dp`, `331 dp`). All four device-verified on Release builds. |
| G-08 Put the one half this pass had only reasoned about on a device | **Done** | The shared row with a capture running at 780 dp: `Follow` measured **23.5 dp**. Found and fixed **F-37**, a defect in §8's own F-32 remediation. §9.4/G-08. |
| G-07 Regression, hand-back, commit and push | **Done** | Full solution **365/365** (Domain 11, Core 88, App 217, Application 49 — six new over §8's 359); `git diff --check` clean; `verify-docs.ps1` consistent; `CHANGELOG.md` updated; device restored (§9.6); committed and pushed to `main`. |

### 9.4 Continuous execution log

Entries are appended as they happen.

#### G-01/G-02/G-03 — audit, baseline, and the Release build on the device

**Status: Done.** The audit was run against the tree at `7cbf352`, not against
§5.1's and §8's claims. All **35** findings (F-01…F-33, D-04.0, F-16 second half)
have a named artefact in the tree; the two that first read as missing were the
audit's own error, recorded here so the next reader does not repeat them:
`TabTitle.cs` is under `src/VisualCat.App/Views/`, not `Presentation/`, and the
one surviving `MaxLines` token in `MainView.Notice.cs` is inside the comment
*"Not MaxLines"* that explains F-33's fix. **No regression against §8.4/E-01.**

**Run header**

| Field | Value |
|---|---|
| Date/time (UTC) | 2026-08-22 12:07 → (device clock identical to the host to the second: `Sat Aug 22 12:07:59 UTC 2026`) |
| Repository commit at start | `7cbf352` — *Run the soak the report kept deferring, and answer what it exposed* |
| Working tree at start | clean |
| Device | motorola **edge 60 pro** (`cybert`) |
| Serial | `ZY22M4T2Z4` |
| Android release / API | 16 / 36 |
| ABIs | `arm64-v8a` |
| Build fingerprint | `motorola/cybert_g_syse/cybert:16/W1VVS36H.7-108-8-6/cf0b3-79ad00:user/release-keys` |
| Screen / density | 1220 × 2712 px, 450 dpi (**2.8125 px/dp**) |
| Configuration at baseline | `mcc230-mnc1-en-rUS-ldltr-sw434dp-w434dp-h964dp-normal-long-notround-widecg-highdr-port-night-450dpi-finger-navhidden-nonav` |
| Navigation mode | `0` — three-button |
| Locale | `en-US`, LTR |
| Theme / font scale | `night` (dark) / `1.0` |
| Rotation | `accelerometer_rotation=1`, `user_rotation=0` |
| Thermal / battery | `Thermal Status: 0`, USB powered |
| Package at baseline | `2.0.5-dev` / `20005`, `firstInstallTime=2026-08-21 23:33:16`, **`pkgFlags=[ DEBUGGABLE … ]`** — a Debug build |
| `READ_LOGS` | **granted** (pre-existing) |
| Evidence root | `artifacts/live-test/20260822-motorola-pass5/evidence/` |

**G-03 — the current commit's Release build, in place.**
`dotnet build … -c Release -p:EmbedAssembliesIntoApk=true` → **0 warnings, 0
errors**, 48 s. SHA-256 `9da7cb132f080a518bc7e33d809588c60c843f38e9a0c6b3e84d1b96a677e683`,
30 815 675 bytes. `adb install -r -t` → `Success` in 1 545 ms; `firstInstallTime`
**preserved**, `READ_LOGS` still granted, and `pkgFlags` lost `DEBUGGABLE` — so
this device now carries a **Release** build, which §5.3 never had. Cold launch
`LaunchState: COLD`, **1 896 ms**.

**Baseline sweep, at a third density (2.8125 px/dp).** The restored workspace —
two retained captures, one open — measures **15 clickable nodes, 0 under 48 dp**,
including `Load 500 more; 812 remaining` and both tab close buttons at exactly
48.0 dp. Evidence `G03-cold.xml/.png`.

#### G-04/G-05 — §8.7's lead, pulled: the shared command row collapses on a narrow short viewport

**Status: Found, root-caused, reproduced, causality proved.**

**How the state was reached.** Compact height is selected below **520 dp**
(`MobileWorkspaceLayout.CompactHeightBreakpoint`). This device is **964 dp** tall
in portrait, so §8's route into it — a tall notice — is no longer available here:
F-33 bounded the notice lane to 108 px, which is the fix working. The state is
still reachable in the two ways the code's own comments name, split-screen and a
short window, so it was reached deterministically with
`adb shell wm size 1220x1400` → `w434dp-h498dp`, **portrait, 434 dp wide, under
the 520 dp compact-height breakpoint**. Recorded in §9.5 as a mutation; reverted
at hand-back.

This is the first time any pass has measured a **narrow** compact-height
workspace on a device. §6 and §7 built the compact layout at **780 dp** and
**801 dp** landscape; §8 found F-32 at 360 dp but through the notice, in the
workspace only.

##### F-34 · In a short, narrow workspace the shared command row draws five controls on top of each other, and *Filters* cannot be tapped at all

- **Severity** Blocker · **Scenario** G-04 · **Device-independent** · **Found by** the fifth pass
- **Reproducibility** deterministic; every cold launch in a 434 × 498 dp viewport

`UpdateCompactCommandComposition` reparents the selected workspace's command
strip beside `Open log / Live / More`, into a two-column row — and decides to do
it on height alone:

```csharp
var combine = _mobileCompactHeight && active is not null;
…
_commandContent.ColumnDefinitions = new ColumnDefinitions("Auto,*");
```

Its own summary says what it assumes: *"**Uses landscape width** instead of
spending a second 48 dp band on workspace commands."* The toolbar takes `Auto` —
about 260 dp for `Open log` + `Live` + `More` — and the strip gets what is left.
At 780 dp there is 512 dp left and the strip needs about 330. At **434 dp there
are 166**, and the strip does not clip, it **overlaps**:

| Control | Bounds (px) | dp | Overlaps |
|---|---|---:|---|
| `Open search and timeline filters` | `[788,147][1003,282]` | 76.4 × 48.0 | **covered by `Plot` and `Split`** |
| `Show plot workspace` | `[782,147][939,282]` | 55.8 × 48.0 | over `Filters` |
| `Show split workspace` | `[940,147][1098,282]` | 56.2 × 48.0 | over `Filters` |
| `Show details workspace` | `[1098,147][1220,282]` | **43.4** × 48.0 | clipped at the screen edge, and covered by `Fit` |
| `Show the whole session in the plot` | `[1037,147][1195,282]` | 56.2 × 48.0 | over `Details` |

The screenshot is unambiguous: the row reads `+ Open log` · `● Live` · `More ▾`
and then **`Plot`, `s`, `Spl`, `Fit`, `ils`** — `Filters` reduced to the letter
`s`, `Split` to `Spl`, and `Details` to the `ils` hanging off the right edge with
`Fit` painted across it.

**Proved, not inferred.** A synthetic tap at `x = 995, y = 214` — inside
`Filters`' reported bounds `[788…1003]` and outside `Plot`'s — **did not open the
drawer**. The dump after it is byte-identical to the dump before: the button still
reads `Open search and timeline filters`. The tap went to `Split`, which is drawn
over `Filters` there and was already the selected mode. Later children paint over
earlier ones, so the first control in the strip is the one buried:
**the filter drawer has no reachable touch point in this state**, and `Details`
has a 25 px (8.9 dp) slice at the very edge of the screen that `Fit` does not
cover.

**This is the fifth instance of §8.7's root cause** — a compact layout chosen by
**height** that assumes **width** — and the first that is a Blocker rather than a
clipping: C-06.2 (query options), F-32 (capture row), F-32's second half (split
composition), and now the shared command row itself, which is the one that
carries every other one of them.

##### F-35 · `Load 500 more` is laid out past the right edge of the screen in the same viewport

- **Severity** Major · **Scenario** G-04 · **Device-independent** · **Found by** the fifth pass

Same dump, the analysis pane's own header:

| Control | Bounds (px) | dp | Same control, 964 dp portrait |
|---|---|---:|---|
| `Load 500 more; 812 remaining` | `[1124,607][1220,726]` | **34.1** × 42.3 | **369.8 × 48.0 dp** |

`1220` is the screen's right edge, so this is the F-32 signature exactly: not a
small button, a **whole button pushed off the display**. The gate is
`MoveLoadMore(intoHeader: enabled && analysisWidth >= 300)`. The header it moves
into already carries the sort control, `Copy` and `Show the full message`; with
`Load more`'s own label it needs roughly 600 dp and the pane has 356.
**300 dp was measured for the header without `Load more` in it.**

##### Both fixed, at the place each one is actually decided

**F-34 — the shared row is a width decision, so it now asks about width.** The four
gates that had each invented their own answer to the same question now share one
named number, `MobileWorkspaceLayout.SharedRowBreakpoint`, reached through
`SharesARow(width)`:

```csharp
var combine = _mobileCompactHeight
              && active is not null
              && MobileWorkspaceLayout.SharesARow(_mobileViewportWidth);
```

600 dp is what the widest of them measures — the application toolbar is about
274 dp (`Open log` 102 + `Live` 72 + `More` 70, plus spacing and the command bar's
own padding) and the workspace strip about 320 dp (`Filters` 76 + three mode
buttons 168 + `Fit` 56, plus spacing) — **602 dp together**, which is why 434 dp
collapses and 780/801/964 dp do not. Below the threshold the strip goes back to
the workspace and takes a band of its own: the composition every tall portrait
phone already uses. That band is the honest price of a viewport too narrow to
share, and it is the same trade F-32 made for the capture row.

**And the number now moves with the reader's text size.** `SharesARow` scales the
breakpoint by `TextScale.Effective`, because every control it measures is scaled
by it: at 1.3× those same five controls need about 780 dp — a landscape phone
exactly — and a fixed 600 would have gone on sharing the row until they
overlapped again. This is what turns three literal `>= 600` comparisons into one
invariant; a sixth site cannot invent a different answer, because there is one
function to call.

**F-35 — the header is composed for where the count line actually is.** The old
gate was `enabled && analysisWidth >= 330`, and `enabled` is *exactly* when
`MoveSummaryIntoTabStrip` takes the count line out of the header. So the
"count line beside the actions" composition **only ever ran with no count line in
it**, and its cap — `analysisWidth × 0.78`, width held back for that absent
control — was pure loss. It is gone, along with the ratio and the 330 dp
threshold; a row with one occupant is not shared with anything:

```csharp
var summaryInHeader = !_summaryInTabStrip;
entryHeader.ColumnDefinitions = new ColumnDefinitions("*");
entryActions.HorizontalAlignment = HorizontalAlignment.Stretch;
entryActions.MaxWidth = double.PositiveInfinity;
```

The row keeps the sort control at its left and its actions at its right through
its own star column, so stretching it costs the pane nothing. `MoveLoadMore`'s
threshold is now `LoadMoreHeaderWidth (300) × TextScale.Effective` — the same
number at 1.0×, so §6's and §7's landscape behaviour is unchanged, and a reader
at 1.3× gets the footer back instead of a clipped button.

**Files** `src/VisualCat.App/Views/MobileWorkspaceLayout.cs`,
`src/VisualCat.App/Views/MainView.cs` (F-34),
`src/VisualCat.App/Views/SessionWorkspaceView.Mobile.cs` (F-35).

**Host tests — proved to fail first.**

| Test | Without the fix |
|---|---|
| `LoadMoreStaysInsideThePaneInAShortNarrowWorkspace` | **`Load more ends at 544 dp in a 434 dp workspace`** — 110 dp past the right edge, the device's clipping reproduced headlessly |
| `AWideShortWorkspaceKeepsLoadMoreInItsHeader` | **`Load more ends at 887 dp in a 801 dp workspace`** — see below |
| `ANarrowShortViewportDoesNotShareOneRowBetweenTwoCommandGroups` | asserts the decision, not the composition — see the note |

**The second row is the more important one.** That test was written to prove the
fix costs landscape nothing, on the Pixel's own 801 dp viewport from §7 — and it
**failed before the fix, by 86 dp**. So F-35 was never a narrow-viewport defect:
`Load 500 more` has been laid out past the right edge of the pane in *every*
compact-height viewport, including the two the compact layout was designed for.
Three device passes missed it because a session with nothing left to load has no
load-more control on screen, and none of them opened one that had. §7.6's lesson
— *"a floor is only as good as the panes it was measured on"* — extends to
**states**: this control only exists while a session is partly loaded.

**A test-shape note.** F-34's composition is `MainView`'s and Android-only, and
§8.4 records what forcing an Android-only lane into a headless desktop layout
costs: a run killed at ten minutes against a normal ninety seconds. So F-34's
test asserts the **decision** — a pure function over the constraint that binds it,
including its text-scale behaviour and that a non-finite width is not assumed to
be roomy — and how the row *looks* at 434 dp stays device-verified below. Unlike
the F-35 tests it cannot be shown red first, because the code it replaces never
asked the question at all; that is stated rather than implied.

**One thing the tests found that is not a defect.** The logical-tree walk reaches
the entry actions grid by two paths, so a single `Load more` button is yielded
twice. It is one instance, not two controls; the locator de-duplicates rather
than working around it, and this is recorded so the next reader does not chase a
phantom duplicate.

#### G-06 — device verification of F-34/F-35, and what opening the drawer then found

**Status: Done.** Rebuilt (0 warnings, 0 errors), installed in place, and the same
viewport reproduced from a cold launch. SHA-256
`b8a19f4087e5634f43f84166c38c70f6e4c18cae566a74536198129fb2260d44`, 31 144 106
bytes, `adb install -r -t` → `Success` in 1 752 ms, cold launch 1 894 ms.

| | Before | After |
|---|---|---|
| `Open search and timeline filters` | 76.4 dp, **covered by `Plot` and `Split`; a tap on it went to `Split`** | 76.1 dp, **opens the drawer** |
| `Show plot workspace` | 55.8 dp, over `Filters` | **84.6 dp**, own slot |
| `Show split workspace` | 56.2 dp, over `Filters` | **84.6 dp**, own slot |
| `Show details workspace` | **43.4 dp**, clipped at the screen edge and covered by `Fit` | **84.3 dp**, whole |
| `Show the whole session in the plot` | 56.2 dp, over `Details` | **56.5 dp**, own slot |
| `Load 500 more; 812 remaining` | **34.1 dp**, ending on the 1220 px screen edge | **49.4 dp** — its full natural width |
| Overlapping control pairs | **4** | **0**, checked pairwise across all four rows |
| Controls laid out past the right edge | **1** | **0** |
| Clickable nodes under 48 dp | 4 | **3 — all of them 42.3 dp *tall*, widths all ≥ 48** |

The three remaining sub-floor nodes are `Copy`, `Show the full message` and
`Load more` at **42.3 dp high**: the compact-height chrome exception §6 introduced
deliberately and §8.6 records. This pass did not disturb it. **Every width is now
at or above the floor.**

**The screenshot is the evidence.** Before, the row read `+ Open log · ● Live ·
More ▾` then `Plot`, `s`, `Spl`, `Fit`, `ils`. After, the strip has a row of its
own and reads `Filters | Plot | Split | Details` whole, with `Fit` beside them.

##### F-36 · The filter drawer, finally reachable, turns out to draw two captions and none of its controls

- **Severity** Major · **Scenario** G-06 · **Device-independent** · **Found by** the fifth pass
- **Reproducibility** deterministic

Fixing F-34 made the drawer openable in this viewport for the first time, and the
first look at it found the sixth instance of §8.7's root cause plus a second
defect underneath it. Measured on the Release build at 434 × 498 dp:

| | Bounds (px) | dp |
|---|---|---|
| Drawer card | `[51,617][1169,1259]` | 397.5 × **228.3** |
| QUERY band (caption, field, options) | `[76,642][1144,990]` | 379.7 × **123.7** |
| **The scrolling body** | `[54,1012][1166,1093]` | 395.4 × **28.8** |
| …its content | `[54,1029][1131,1400]` | 382.9 × **131.9** |
| Severity chips (2 rows of 3) | `[616,1112][1110,1400]` | 175.6 × 102.4 |
| `Unknown level` | — | **not in the accessibility tree at all** |
| Pinned footer (`Reset`/`Done`) | `[76,1099][1144,1234]` | 379.7 × 48.0 |

Two things, one on top of the other:

1. **The body is 28.8 dp** — one line — because the card is 228.3 dp and its fixed
   chrome (caption, field, options row, chip bar, pinned footer) is about 210 of
   it. What the reader sees is the words `TIME LENS` and `SEVERITY` and then the
   footer: **no severity toggles, no zoom buttons, no readout.** The footer is
   painted across the body's overflow, and the body's content runs to y = 1400 —
   the bottom of the screen — well past the card's own bottom edge at 1259.
2. **The body is still in two columns**, at 175.6 dp each, which wraps seven
   48 dp severity chips into three rows of three. The third row starts at
   y = 1404 — **below the screen** — so `Unknown level` is not merely off-card, it
   never reaches the accessibility tree. A screen reader cannot find it either.

The drawer is the primary way to filter a log. In this viewport it showed none of
its controls.

##### Both fixed

**The two-column body was the sixth site keyed on height alone**, and the last one
left in that method. It now asks the same question as the other five —
`enabled && MobileWorkspaceLayout.SharesARow(availableWidth)` — so a landscape
drawer keeps the two columns §6 and §7 measured it in, and a narrow one gets a
single 380 dp column that holds **all seven chips on one 48 dp row**.

**And the drawer's fixed chrome now yields to its body.** `ApplyTightDrawerChrome`
already knew how to do this and how to order it — caption first, then the padding
around both rows — but it was only ever asked when the *keyboard* took the room
(F-10). A short viewport takes it just as effectively, so the trigger now also
reads the card:

```csharp
var card = panel.Bounds.Height;
var shortCard = card > 0 && card < TightDrawerCardHeight;   // 260 dp
```

The card fills its band whatever its own chrome does, so reading the card rather
than the body cannot oscillate between the two states. It buys back about 42 dp —
a whole row of severity toggles, which is the first thing the drawer exists to
show.

**Files** `src/VisualCat.App/Views/SessionWorkspaceView.Mobile.cs`.

**Host tests — proved to fail first.**

| Test | Without the fix |
|---|---|
| `AShortNarrowDrawerKeepsItsSeverityRowOnScreen` | **`Info level ends at 331 dp in a 286 dp workspace`** — 45 dp below the bottom, the device's clipping reproduced headlessly; it also asserts all seven levels are present and that the body is at least one 48 dp row |
| `AWideShortDrawerKeepsItsTwoColumns` | passes before *and* after — it exists to prove the fix costs the landscape drawer nothing |

**A test-shape note worth keeping.** The first version of the failing test used a
434 × **498** dp window and **passed**, because a standalone workspace in a
headless window gets the whole 498 dp while on the device the command bar and the
session tab strip take the top and the workspace gets the **286 dp** that is left.
Composing the drawer against the window rather than against the band it actually
receives is what hid this from three passes of headless tests. The test now uses
286 and says why.

##### F-36 device verification — Pass, on the Release build, in the exact state

Rebuilt (0 warnings, 0 errors), installed in place. SHA-256
`7848a7f199496388828d77147430c5f0c4397838408c9a3b2dbdaa6802de8549`,
30 815 675 bytes; `Success` in 1 552 ms; cold launch 1 843 ms.

| | Before | After |
|---|---|---|
| Severity toggles in the accessibility tree | **6 of 7** — `Unknown level` was laid out below the screen | **7 of 7**, each **48.0 × 48.0 dp** |
| Severity chip rows | 3 rows of 3 in a 175.6 dp column | **1 row of 7** in a 380 dp column |
| The scrolling body | **28.8 dp** | **67.9 dp** |
| What a cold open draws | `TIME LENS` and `SEVERITY`, then the footer | **`SEVERITY` and all seven colour-coded chips** — `F E W I D V ?` — with a scrollbar showing there is more |
| `Zoom out` / `Zoom in` | not drawn at all | **48.0 × 48.0 dp** after one scroll, inside the card |
| Drawer clickable nodes under 48 dp | — | **0**, once the body is scrolled to them |

The two zoom buttons read `48.0 × 32.4 dp` *before* scrolling. That is the scroll
artefact §8's own device notes describe — a node past the edge of the viewport
reports clipped bounds — not a target defect: one swipe inside the body puts them
at `[76,1014][211,1149]` = **48.0 × 48.0 dp**, inside the card. The check that
distinguishes the two is comparing the node against the card's bounds, which is
what was done here.

**Landscape — the composition §6 and §7 built is untouched.** Rotated to
964 × 434 dp on the same build:

| | Before the fixes (`G04-landscape`) | After |
|---|---|---|
| `Filters` / `Plot` / `Split` / `Details` / `Fit` | 76.4 / 55.8 / 56.2 / 56.2 / 56.2 dp, one shared row with `Open log · Live · More` | **identical, same shared row** |
| Drawer body | two columns | **two columns** — `TIME LENS` x 206–1322, `SEVERITY` x 1369–2484, 396 dp each |
| Drawer clickable nodes under 48 dp | 0 | **0 of 26** |

**And the tall portrait baseline is byte-for-byte what it was.** After restoring
the device: workspace **15 clickable nodes, 0 under 48 dp**, `Load 500 more; 812
remaining` back in its footer at **369.8 × 48.0 dp**; drawer **26 nodes, 0 under
48 dp** with `SEVERITY` and `TIME LENS` stacked in one column at the same x range.
Identical to `G03-cold` and `G04-drawer-tall`, taken before any change.

##### One thing deliberately not done

The severity row a cold open draws is **vertically cut at about 72 %** by the
scroll viewport — every chip is coloured, lettered, identifiable and tappable, and
the scrollbar says there is more, but the row is not whole. Buying the last ~14 dp
means moving the `Regex` / `Case-sensitive` row into the scroller, and that row's
placement is C-06.2's and F-30's work, device-verified twice on two other phones.
Trading a verified composition for 14 dp of a row that is already legible and
reachable is the wrong side of that bargain. Recorded here rather than left
implicit, with what it would cost, so a later pass does not re-derive it.

#### G-08 — the one thing this pass had only reasoned about, put on a device

**Status: Done.** F-34's state was reached without a capture running, which left
the *other* half of the shared row untested: what happens when `Follow` and
`Stop capture` join it. That was written down as a limit of this pass — and then
measured rather than left, because the arithmetic already looked wrong: the
toolbar takes about 282 dp of the shared row, and the merged strip needs the
workspace's own `Filters + three modes + Fit` (≈ 320 dp) **plus** `Follow` and
`Stop capture` (≈ 320 dp).

The device was put at **780 × 434 dp** — the Samsung landscape viewport §6, §7 and
§8 built and verified this layout for — with `adb shell wm size 2194x1220`, and a
capture was started and its consent sheet declined.

##### F-37 · The merged capture row is decided on the workspace's width, and the row does not get the workspace's width

- **Severity** Major · **Scenario** G-08 · **Device-independent** · **Found by** the fifth pass
- **Reproducibility** deterministic, at any width where the workspace clears the threshold and the strip's own column does not

| Control | 780 dp, capture running | After the fix |
|---|---:|---:|
| **`Follow ✓`** | **23.5 × 48.0 dp** | **339.2 × 48.0 dp** |
| `Stop capture` | 97.1 dp | 97.4 dp |
| `Show plot workspace` | 55.8 dp | **101.3 dp** |
| `Show split workspace` | 56.2 dp | **101.3 dp** |
| `Show details workspace` | 56.5 dp | **100.6 dp** |
| Overlapping control pairs | 0 | 0 |
| Sub-floor controls | **`Follow` 23.5 dp wide** | **none by width** |

`mergeCaptureRow` asked `SharesARow(availableWidth)`, and `availableWidth` is the
**workspace's** width. When the strip is hosted in the shell row it does not get
that: the application toolbar takes its own `Auto` column first, so at 780 dp the
strip's column is about **498 dp** and the merged row needs about **640**. 780
passes the test; 498 is what the row actually has. `Stop capture` survived because
it is not the control that gives — **`Follow` is**, at less than half the touch
floor.

**This is a defect in §8's own F-32 remediation**, not in this pass's work: the
`≥ 600 dp` merge gate was §8's fix, and §8 verified F-32 in **portrait at 360 dp**
and reasoned about landscape rather than measuring it with a capture running. The
pattern §8.7 named — a layout decision taken against a width it does not have — had
one more form left, and it is the subtlest: not *height instead of width*, but
**the wrong width**.

**Fixed by asking the row about itself.**

```csharp
var shellWidth = _compactCommandsExternallyHosted && filterShell.Bounds.Width > 0
    ? filterShell.Bounds.Width
    : availableWidth;
var mergeCaptureRow = enabled && MobileWorkspaceLayout.SharesARow(shellWidth);
```

The strip's column is a star column, so its width is the viewport minus the
toolbar whatever the strip puts in it — the decision cannot oscillate. Where the
column is wide enough the merge stands; where it is not, the capture controls take
a row of their own inside the strip, which is what a portrait phone already does.
On this device the whole shell row then grows from 48 dp to about 99, with the
toolbar centred beside a two-row strip — one band, spent to make the control that
ends a recording and the control that follows it both reachable.

**Host test** `TheSharedRowBudgetsTheStripByWhatIsLeftAfterTheToolbar` pins the
arithmetic and the rule applied to each number: 780 dp shares a row, `780 − 282`
does not, and `964 − 282` does. Like F-34's, it asserts the decision rather than
the Android-only composition, and the composition is device-verified above.

**Two things verified for free while doing it, on a Release build.**

| | Observed |
|---|---|
| **F-13 (P1)** | the consent sheet the copy promises **does** appear on the press — `mCurrentFocus=…LogAccessDialogActivity`, with *Allow one-time access* / *Don't allow* |
| **F-21 / Stop capture** | both stops answered the press and read **`Stopped · 11 entries kept`** and **`Stopped · 9 entries kept`** — the counted noun agrees — with **zero** `logcat` children left afterwards each time |

The one control still under the floor at hand-back in that state is
`Close On-device logcat 00h13m13` at **42.3 dp wide**, with four sessions open: the
horizontally scrolled tab chip, F-26's known and documented behaviour, unchanged
by this pass.

#### G-07 — regression, and the record a release would read

**Status: Done.** `dotnet test VisualCat.slnx -c Debug` → **364/364 passed, 0
failed** (Domain 11, Core 88, App **216**, Application 49) — five more App tests
than §8's 359, which are exactly the five this pass added. `git diff --check`
clean. `tools/verify-docs.ps1`: *"Checked 92 relative links across 43 Markdown
files, required files, and version metadata. All consistent."*

`CHANGELOG.md` carries all three findings under `[Unreleased] → Fixed`, in the
file's established voice — what went wrong, then what changed, no finding IDs —
because §8/E-06 established that `CONTRIBUTING.md` requires it and
`docs/RELEASE-CHECKLIST.md` gates a release on it.

### 9.5 Mutation ledger

| Setting | Original | Changed to | Restored |
|---|---|---|---|
| `wm size` | `1220x2712` (physical) | `1220x1400` — the short viewport F-34/F-35/F-36 live in, and the only way to reach compact height on a 964 dp device now that F-33 has bounded the notice lane; then `2194x1220` for G-08, which is the **Samsung's own 780 × 434 dp landscape** | **Yes — `wm size reset`; `Physical size: 1220x2712`, config back to `w434dp-h964dp`** |
| `wm user-rotation` | free, `user_rotation=0` | locked `1` (landscape) for the regression check | **Yes — `lock 0` then `free`; `accelerometer_rotation=1`, `user_rotation=0`** |
| Installed package | `2.0.5-dev`, **Debug** (`pkgFlags=[ DEBUGGABLE … ]`) | Release build of `7cbf352`, then of the F-34/F-35 fix, then of the F-36 fix — all `adb install -r -t`, in place | **Not reverted, deliberately** — see §9.6/1 |
| App-private data | two retained captures | **two more**, from G-08's two own-app captures (`14h44m19`, 11 entries; `16h24m26`, 9 entries) — both started, stopped cleanly, and kept as F-37's evidence | **Not reverted, deliberately** — see §9.6/3; `firstInstallTime` preserved throughout |
| Android log-access consent | — | the consent sheet was answered **Don't allow** twice, which is what keeps a capture own-app and small | **n/a — a per-capture choice, not a stored setting; `READ_LOGS` was never granted or revoked by this run** |
| Shared storage | — | two `uiautomator` dumps at `/sdcard/vc5.xml`, deleted after each pull | **Yes — nothing left on `/sdcard`** |
| Stored settings | text scale `1.00×`, cleanup disabled | **untouched** — no settings sheet was opened by this pass | **n/a** |

### 9.6 Hand-back

| | Baseline (§9.4/G-01) | At hand-back |
|---|---|---|
| Configuration | `sw434dp w434dp h964dp port night` | **identical** |
| `wm size` / `wm density` | `1220x2712` / `450` | **`1220x2712` / `450`** |
| `accelerometer_rotation` / `user_rotation` | `1` / `0` | **`1` / `0`** |
| System font scale | `1.0` | **`1.0`** |
| `READ_LOGS` | `granted=true` | **`granted=true`** — never revoked or granted by this run |
| `firstInstallTime` | `2026-08-21 23:33:16` | **preserved** — every install was in place |
| Capture children | 0 | **0** |
| Battery / thermal | USB powered, `Thermal Status: 0` | **100 %, `Thermal Status: 0`** |
| Files added to shared storage | — | **none** |
| Workspace at hand-back | 15 clickable nodes, 0 under 48 dp | **14 clickable nodes, 0 under 48 dp** (fourteen, not fifteen: the session selected at hand-back is one of G-08's fully loaded captures, which has no `Load more` to show) |
| Capture consent | not asked | **`READ_LOGS` still `granted=true`** — the two declines were per-capture answers, not grants |

**Deliberate deviations, recorded rather than reverted.**

1. **The device now carries a Release build where it carried a Debug one.** The
   baseline package was `DEBUGGABLE`; three in-place installs later it is
   `pkgFlags=[ HAS_CODE ALLOW_CLEAR_USER_DATA ]`. Reverting would mean putting a
   *staler* and less representative artifact back, so it is left on the build this
   pass verified. §5.3's Motorola evidence was Debug-only; this closes that half
   for this device, as §8/E-03 did for the Samsung.
2. **The two Samsung-run files §8.6 noted on `/sdcard` are not on this device**,
   and this run added nothing to shared storage.
3. **App-private data holds two more sessions than the baseline** — G-08's two
   own-app captures, `On-device logcat 14h44m19` (11 entries) and `16h24m26`
   (9 entries). Both were stopped from the UI, both left **zero** `logcat`
   children, and they are the evidence for F-37's before and after, so they are
   kept rather than deleted.

### 9.7 What this pass changed about the report

1. **§8.7's lead was real, and it was not one more site.** It named "anything else
   in that method still keyed on `enabled` alone". There were **three**, and the
   worst of them was not in that method at all — it was in `MainView`, in the
   decision that hosts the whole strip. F-34 is a **Blocker**: a control with no
   reachable touch point, not a small one.
2. **The root cause had one more form, and §8's own fix carries it.** F-37 is not
   *height instead of width* — it is **the wrong width**: `mergeCaptureRow` asks
   about the workspace's 780 dp while the row it governs has 498. `Follow`
   measured **23.5 dp** in the exact landscape viewport §6, §7 and §8 built this
   layout for. The lesson generalises past this pass: a layout decision must be
   taken against the space the thing being laid out is actually given, and that is
   not always the space its owner has.
3. **The four scattered `>= 600` literals are now one named, tested invariant.**
   `MobileWorkspaceLayout.SharesARow(width)` is the only way to ask the question,
   it is scaled by the reader's text size, and a sixth site cannot invent a
   different answer. That is the actual remedy for a root cause that had recurred
   six times across four passes.
4. **F-35 was never a narrow-viewport defect.** Its "this costs landscape nothing"
   test failed first at **801 dp** — §7's own Pixel landscape — by 86 dp. The
   defect had been in every compact-height viewport all along; three device passes
   missed it because a fully loaded session has no `Load more` on screen.
5. **A third axis for the sweeps.** §7.6 concluded findings hide in *panes* nobody
   opened; §8.7 sharpened that to *states* nobody measured. Both of this pass's
   headless misses were about neither: they were about **the size the thing under
   test is actually given**. F-36's first test passed at 434 × 498 and failed at
   434 × **286**, because on the device the command bar and tab strip take the top
   and the workspace gets the band that is left. A headless test that composes a
   view against the *window* rather than against its *band* will keep proving the
   wrong thing.

**Declared limits of this pass.** One device, API 36, Release but **debug-signed**,
so §1.1's gap 2 is unchanged. The short viewport was reached with `wm size`, which
is the same geometry a split-screen or a small window produces but is not itself a
split-screen transition — §1.1's gap 5 is narrowed, not closed. No TalkBack, no
upgrade/Play, no endurance, no destructive-storage and no locale work was
attempted. The capture-row half of the shared row was the one thing this pass had
only reasoned about; G-08 put it on the device rather than leaving it, and it was
wrong — see F-37. Two own-app captures were started and stopped to do that, and
the sessions they produced are kept (§9.6/3).

---

## 10. Sixth pass — a full re-audit of every finding, on the Motorola

This section is the live handoff for the sixth pass. The brief is the standing
one — **"check whether every issue in this report has actually been addressed,
and implement or fix what has not"** — with the device the operator named again:
the Motorola **edge 60 pro** (`ZY22M4T2Z4`).

It differs from §9 in where it starts. §9 began from a named lead (§8.7). This
pass begins with **no lead**: §9.7 closed its own thread by turning four
scattered `>= 600` literals into one tested invariant, and left three *lessons*
rather than a next site. So this pass re-derives the audit from scratch against
the tree at `81bb56b`, and then goes looking in the places §7.6, §8.7 and §9.7
each said findings hide: panes nobody opened, states nobody measured, and **the
size the thing under test is actually given**.

Update the ledger in §10.3 after every material action and resume at the first
row that is not **Done**. Every entry in §10.4 is written when it happens, not at
the end, so an interrupted session resumes from the last one.

### 10.1 Run header

| Field | Value |
|---|---|
| Date/time (UTC) | 2026-08-22 14:57 → (device clock identical to the host to the second: `Sat Aug 22 14:57:51 UTC 2026`) |
| Repository commit at start | `81bb56b` — *Pull the lead the fourth pass left open, and answer what it found* |
| Working tree at start | clean |
| Device | motorola **edge 60 pro** (`cybert`) |
| Serial | `ZY22M4T2Z4` |
| Android release / API | 16 / 36 |
| ABIs | `arm64-v8a` |
| Screen / density | 1220 × 2712 px, 450 dpi (**2.8125 px/dp**) |
| Configuration at baseline | `mcc230-mnc1-en-rUS-ldltr-sw434dp-w434dp-h964dp-normal-long-notround-widecg-highdr-port-night-450dpi-finger-navhidden-nonav-2712x1220-v36` |
| Navigation mode | `0` — three-button |
| Locale | `en-US`, LTR; app override `[]` |
| Theme / font scale | `night` (dark) / `1.0` |
| Rotation | `accelerometer_rotation=1`, `user_rotation=0` |
| Thermal / battery | `Thermal Status: 0`, USB powered, 100 % |
| Package at baseline | `2.0.5-dev` / `20005`, `firstInstallTime=2026-08-21 23:33:16`, `lastUpdateTime=2026-08-22 16:24:15`, `pkgFlags=[ HAS_CODE ALLOW_CLEAR_USER_DATA ]` — the **Release** build §9.6/1 deliberately left |
| `READ_LOGS` | **granted** (pre-existing, from §1.3; §9 neither granted nor revoked it) |
| App process at baseline | PID 11761 alive, 0 `logcat` children |
| Evidence root | `artifacts/live-test/20260822-motorola-pass6/evidence/` |

### 10.2 Which device, and why

The Motorola **edge 60 pro** (`ZY22M4T2Z4`) — the device the operator named, §1's
device, and §9's. It is also the right device for a brief with no lead, because it
is the one this report knows best: §1 ran the whole standard schedule on it, §5.3
verified twenty-seven findings on it, and §9 left it carrying a Release build. Two
findings landed on it in the last pass, so a state it has not been measured in is
a state four passes have failed to reach rather than a state one device happens
not to have.

Its portrait width, **433.8 dp**, remains the third of the three this report has
(360 dp Samsung, 393 dp Pixel), and this pass added two more viewports it had
never been measured at: **601 × 400 dp**, the `SharesARow` threshold itself, and
its own **964 × 434 dp** natural landscape — which, it turns out, is where F-38
had been living in plain sight since §6.

### 10.3 Step ledger

| Step | Status | Outcome / next action |
|---|---|---|
| H-01 Record the device baseline | **Done** | §10.4/H-01. 434 dp portrait, 450 dpi, three-button, en-US, thermal 0, Release build present. |
| H-02 Re-audit all 39 findings against the tree at `81bb56b` | **Done** | All 39 have a named artefact in the tree; the two that read as missing in §9's audit are still where §9 said. §10.4/H-02. |
| H-03 Host regression baseline | **Done** | `dotnet test VisualCat.slnx -c Debug` → **365/365**, 0 failed (Domain 11, Core 88, App 217, Application 49). Matches §9/G-07. |
| H-04 Fresh-eyes sweep for what no pass has measured | **Done** | Found **F-38** and **F-39**. Panes swept clean: Insights, Details, Session cache, Recent sessions, the filter drawer at a third width, and the Export picker (the one sub-floor control there is Android's own). §10.4/H-04. |
| H-05 Fix what this pass finds, host tests red first | **Done** | **F-38** and **F-39** fixed; four host tests, all four proved red first. Full solution **369/369**. |
| H-06 Device verification of both fixes on a Release build | **Done** | Both fixed on the device: F-38 42.3 dp → 48.0 dp on five controls, F-39 one strip through four text-size changes and two tab closes. §10.5. |
| H-07 Regression, hand-back, commit and push | **Done** | Full solution **369/369**; `git diff --check` clean; `verify-docs.ps1` consistent; `CHANGELOG.md` updated; device restored (§10.7); committed and pushed to `main`. |

### 10.4 Continuous execution log

Entries are appended as they happen.

#### H-01 — device baseline

**Status: Done.** Recorded in §10.1 above, evidence
`artifacts/live-test/20260822-motorola-pass6/evidence/H01-baseline.txt`. The
device is exactly as §9.6 handed it back: Release build of the F-37 fix, two
extra own-app captures in app-private data, nothing on shared storage,
`READ_LOGS` granted, thermal 0, battery 100 %.

#### H-02/H-03 — the audit, and the tool the audit needed

**Status: Done.** The audit was run against the tree at `81bb56b`. All **39**
findings (F-01…F-37, D-04.0, F-16 second half) have a named artefact in the tree.
No regression against §9's own audit; the two entries §9 recorded as its own
error (`TabTitle.cs` under `Views/`, and the `MaxLines` token that lives inside
the comment explaining F-33) are still exactly where §9 left them.

Host regression before touching anything: `dotnet test VisualCat.slnx -c Debug`
→ **365/365 passed, 0 failed** (Domain 11, Core 88, App 217, Application 49) —
identical to §9/G-07.

**The audit needed a better instrument, and building it found the first defect.**
`tools/scripts/measure_targets.py` answers exactly one question — *is a clickable
node under 48 dp* — and four passes have now found defects it cannot see: F-34
was an **overlap** (every control measured 48 dp; two of them were painted on top
of a third), and F-35 was a **clip** (the control measured 49.4 dp and 34.1 of
them were past the right edge of the screen). So this pass wrote
`tools/scripts/audit_layout.py`, which answers all three — sub-floor, overlapping
pairs, clipped-past-the-edge — from the same `uiautomator dump`.

Running it over **§9's own 29 evidence dumps** was the fastest audit in this
report. It reproduces every defect §9 found, in the dumps §9 took before its
fixes, and it also reports a sub-floor control in **eleven** dumps that §9 took
*after* them — including its two hand-back dumps' predecessors and both G-08
Release-build captures. That is F-38.

#### H-04 — the sweep, and what it found first

##### F-38 · Every compact-height pane opts nine of its controls out of the 48 dp floor, with a literal `42`

- **Severity** Minor · **Scenario** H-04 · **Device-independent** · **Found by** the sixth pass
- **Reproducibility** deterministic, in every compact-height viewport measured: natural landscape 964 × 434 dp, the 780 × 434 dp viewport §6–§9 built this layout for, and 601 × 400 dp
- **First suspicion** `SessionWorkspaceView.Mobile.cs` — confirmed

`ConfigureWideMobileComposition` ends with three blocks that give the compact
composition its own, lower floor:

```csharp
item.MinHeight = enabled ? 42 : 48;                 // the three analysis tabs
…
foreach (var control in new Control[] { _order, _loadMore, _fitMatches, _clearScope })
{
    control.MinHeight = enabled ? 42 : 48;
}
if (_copyRaw is { } compactCopy)     { compactCopy.MinHeight = enabled ? 42 : 48; }
if (_openInspector is { } compactIns) { compactIns.MinHeight = enabled ? 42 : 48; }
```

Measured on the device, in the ordinary landscape workspace (`w964dp-h434dp`,
450 dpi):

| Control | Bounds (px) | dp | 48 dp? |
|---|---|---:|---|
| `Copy` | `[2139,557][2278,676]` | 49.4 × **42.3** | ❌ |
| `Show the full message of the selected entry` | `[2295,557][2474,676]` | 63.6 × **42.3** | ❌ |
| `Entries` (TabItem) | — | 78.2 × **42.3** | ❌ |
| `Insights` (TabItem) | — | 78.2 × **42.3** | ❌ |
| `Entry` (TabItem) | — | 78.2 × **42.3** | ❌ |
| every control in the row above the toolbar | — | 48.0 | ✅ |

`_order`, `_loadMore`, `_fitMatches` and `_clearScope` are the same 42 dp when
they are on screen: §9's own `G04-landscape.xml`, `G06-*.xml` and `G08-*.xml`
dumps record `Load 500 more; 812 remaining` at **49.4 × 42.3 dp** on a Release
build, and `G08-780-fixed.xml` — the dump §9 took to *prove* F-37 fixed — records
`Show the full message of the selected entry` at **63.3 × 42.3 dp** in the same
frame.

**Why four passes missed it.** Two independent blind spots, and it needed both.

1. `measure_targets.py` was run on the dumps that were about a specific finding,
   and each time the finding's own control was the one being read. The tool prints
   every node, so the 42.3 dp rows were on screen — in §9's own terminal — under a
   headline that said the thing being measured had passed.
2. The three analysis tabs are `clickable="false"` in Android's accessibility
   tree (Avalonia's `TabItem` exposes selection, not click), so **no** sub-floor
   tool has ever counted them. They are the primary navigation of the analysis
   pane and a finger is what taps them.

**Why it matters.** `TouchTarget`'s own remarks name "toolbar buttons, workspace
modes, Fit and the analysis tabs" as the controls that "measured 135 px = 48 dp
… and were right". In the compact composition they are not: the compact
composition is *landscape*, which is the orientation a reader turns the phone to
in order to read a log, and `Copy`, `Entry ⤢` and the three analysis tabs are the
pane's whole command set. 42 dp is 6 dp under the platform floor and under this
report's own U-08 gate — the same gate F-03 (18.8 dp), F-26 (43.7 dp), F-29
(30.5 dp) and F-31 (34 dp) were each raised for.

**What it costs to fix, honestly.** 6 dp per band, and at most two of these bands
are on screen at once (the tab strip, plus whichever pane's action row is
showing). In the 434 dp-tall landscape workspace that is 12 dp of 434 — under 3 %,
about a quarter of one entry row — bought for five controls that a finger can
actually hit. The alternative the code chose is 6 dp of log.

##### F-39 · Every system text-size change leaves another copy of the workspace command strip in the shell's shared row

- **Severity** Major · **Scenario** H-04 · **Device-independent** · **Found by** the sixth pass
- **Reproducibility** deterministic — one change is enough, in any compact-height viewport with a session open; the copies accumulate
- **First suspicion** `MainView.RebuildWorkspaceViews` — confirmed, and the invariant belongs one level down

**How it was found.** Not by looking for it. The sweep changed the system text
size to measure the layout at 1.3×, and `audit_layout.py` reported **5
overlapping pairs** where there had been none. After a second change it reported
**15** — which is C(3,2) × 5: the same five controls, three times each.

**Measured**, on the Release build, `w964dp-h434dp`, a session open:

| System text size | `Filters` nodes | `Plot` / `Split` / `Details` / `Fit` nodes | Overlapping pairs |
|---|---:|---:|---:|
| cold at 1.0 | 1 | 1 each | 0 |
| after → 1.3 | **2** | **2 each** | **5** |
| after → 1.0 | **3** | **3 each** | **15** |

Every copy reports **byte-identical bounds** — `[1057,97][1271,232]` for
`Filters` — and every copy says `enabled="true"`.

**Root cause.** In a compact-height viewport the workspace's command strip is
*reparented* into `MainView`'s own command row: `HostCompactCommands(host)` takes
it out of the workspace and adds it to `_compactWorkspaceCommands`, so that
`Open log · Live · More` and `Filters · Plot · Split · Details · Fit` share one
48 dp band (§6, and F-34's threshold). That move is what makes the leak possible.

A workspace view is **replaced** whenever the reader changes the device's text
size, because every font size in it is resolved while it is being built —
`RebuildWorkspaceViews` builds a new `SessionWorkspaceView` and drops the old
one. The old one had already given its strip to the shell, so dropping it drops
nothing: the strip is a child of `MainView`'s grid, not of the view. Nothing
asked for it back, and `UpdateCompactCommandComposition` then adds the new
workspace's strip to the same cell.

`DetachViewModel` — whose own comment says a replaced view "would answer every
change that session makes for as long as the tab is open" — took the *event* half
off and left the *visual* half on.

**What it costs the reader.**

1. **The accessibility tree doubles, then triples.** TalkBack walks `Filters`,
   `Plot`, `Split`, `Details`, `Fit` once per copy. The stale copies belong to a
   view that has stopped answering its session, and screen-reader activation goes
   to the focused node rather than through hit-testing, so they are reachable.
2. **It is unbounded.** Five changes leave six strips and 75 overlapping pairs.
3. **It is a retention leak.** Each stale strip holds a detached workspace
   subtree alive for as long as the session is open.
4. **The reader who pays is the one the feature exists for.** `TextScale` exists
   so that "somebody who has already told the operating system they need larger
   text" gets it without finding a second switch. That is the exact gesture that
   triggers this.

Touch still works: the new strip is added last and so paints on top, and a
synthetic tap at `Filters`' centre after one change did open the drawer (one copy
then read `Close filters` while the other still read `Open search and timeline
filters` — the clearest possible proof that both are live).

**Why five passes missed it.** Every pass that changed the system text size did it
to measure something *at* the new size, and both a stale strip and a live one
measure 48 dp. `measure_targets.py` prints duplicate rows without comment; the
defect is only visible if you count them, or if you ask — as `audit_layout.py`
does — whether two touch targets overlap. §5's U-04/U-05 row records that the app
*survived* system scales 1.3 → 0.85 → 1.15 → 1.0. Four scale changes: on the
build under test that state ends with **five** copies of the strip.

**Fixed at the seam that creates the situation, and at the moment that ends it.**

1. `HostCompactCommands(host)` clears the row before it adopts. The row holds
   exactly one strip — the selected workspace's — and the only thing that can put
   one there is this method. A strip already in it is, by construction, one no
   live workspace is hosting: `UpdateCompactCommandComposition` gives the host to
   the selected workspace alone and `null` to every other tab, and a workspace
   that finds its strip gone re-inserts it into its own row zero on the next pass.
2. `DetachViewModel` gives the strip back, beside the subscriptions it already
   dropped, so "replaced" means one thing rather than two halves.

**Host tests** `LiveTestRemediationTests.TheSharedRowNeverHoldsTwoWorkspacesCommandStrips`
(host one workspace's strip, then another's, without detaching in between — the
row holds one, and it is the second) and
`.ADetachedWorkspaceGivesBackTheRowItWasHostedIn` (the row empties, and the strip
goes home rather than nowhere). Both were **proved red first**, with the failure
message the device produced: *"Assert.Single() Failure: The collection contained
2 items"*.

**A second route into the same defect, found while looking for something else.**
Closing session tabs duplicates the strip too, and it needs no text-size change at
all: with four sessions open in landscape, closing three left **5 overlapping
pairs** — one extra strip — and the one remaining session's own strip on top of
it. `RemoveTab` removed the tab and its item but never told the view it was
finished, so a closed session's workspace stayed subscribed to the session it no
longer draws *and* kept the strip it had handed to the shell.

That half is fixed in the same place the first half is: `RemoveTab` now detaches
the view it is dropping, which is the release `RebuildWorkspaceViews` already
performs, for the same reason. The row-clears-before-it-adopts invariant covers
it either way — this is the belt to that brace, and it also stops a closed
session's view from answering a disposed view model.

**Honest about the test.** The two host tests pin the seam
(`HostCompactCommands` / `DetachViewModel`). The `RemoveTab` half has **no**
headless test: `MainView`'s compact composition is gated on
`OperatingSystem.IsAndroid()` with no override, and it has no seam for opening a
session, so a desktop run cannot reach the state. Adding one purely to observe a
one-line release would be a worse trade than saying this plainly and verifying it
on the device, which §10.5 does.

### 10.5 Device verification, on a Release build

Both fixes were put on the device as a **Release** build and measured there. The
device carried the Release build of `81bb56b` at the start of this pass (§9.6/1),
so this is Release-against-Release.

| Field | Value |
|---|---|
| Artifact | `src/VisualCat.Android/bin/Release/net10.0-android36.0/com.barebit.visualcat-Signed.apk` |
| SHA-256 | `d17794aed5ad925ad4778259d5c62d34d423758ff85093d67b9f78520be403ca` |
| Bytes | 30 815 675 |
| Build | 0 warnings, 0 errors, 55 s |
| Install | `adb install -r -t` → `Success` in 1 599 ms, **in place** — `firstInstallTime=2026-08-21 23:33:16` preserved, `pkgFlags=[ HAS_CODE ALLOW_CLEAR_USER_DATA ]` (no `DEBUGGABLE`) |
| Cold launch | `LaunchState: COLD`, **1 760 ms** |

#### F-38 — verified

Landscape workspace, `w964dp-h434dp`, 450 dpi, session open. Evidence
`V2-f38-landscape.xml/.png`.

| Control | Before (§10.4) | After |
|---|---:|---:|
| `Copy` | 49.4 × **42.3** dp | 49.4 × **48.0** dp |
| `Show the full message of the selected entry` | 63.6 × **42.3** dp | 63.6 × **48.0** dp |
| `Entries` (TabItem) | 78.2 × **42.3** dp | 78.2 × **48.0** dp |
| `Insights` (TabItem) | 78.2 × **42.3** dp | 78.2 × **48.0** dp |
| `Entry` (TabItem) | 78.2 × **42.3** dp | 78.2 × **48.0** dp |
| Whole workspace | 2 under 48 dp | **0 under 48 dp, 0 overlapping, 0 clipped** |

#### F-39 — verified, both routes

**The text-size route**, run as §5's U-04/U-05 sequence — four changes, the exact
gesture that produced five stacked strips on the build under test:

| Step | `Filters` nodes | Overlapping pairs |
|---|---:|---:|
| baseline, system scale 1.0 | 1 | 0 |
| → 1.3 | **1** | **0** |
| → 1.0 | **1** | **0** |
| → 1.15 | **1** | **0** |
| → 1.0 | **1** | **0** |

Evidence `V3-f39-a…e.xml`. Before the fix the same sequence measured 2, 3, 4, 5
copies and 5, 15, 30, 50 overlapping pairs.

**The tab-close route.** Three sessions opened from *Recent sessions*
(`00h13m13`, `16h24m26`, `14h44m19`; 16 nodes, 0 overlapping), then closed one at
a time from the right:

| After closing | `Filters` nodes | Overlapping pairs |
|---|---:|---:|
| `14h44m19` | **1** | **0** |
| `16h24m26` | **1** | **0** |

Evidence `V5-close1/2.xml`. Before the fix, closing three tabs left **5**
overlapping pairs — one whole extra strip — with no text-size change involved.

**Still a live strip, not a surviving corpse.** After all of the above:
`Filters` opened the drawer (`Close filters`, 22 nodes, 0 under 48 dp, 0
overlapping), `Plot` and `Details` both switched the workspace, and each state
measured clean. Evidence `V6-drawer/plot/details.xml`.

#### Regression check — the viewports §9 fixed

F-38 spends 6 dp per band, and the band it spends it in is the one §9's F-32,
F-35 and F-36 were about. Both of §9's short viewports were re-measured on this
build:

| Viewport | Result |
|---|---|
| `w434dp-h498dp` workspace (F-34/F-35's state) | 10 clickable nodes, **0 under 48 dp, 0 overlapping, 0 clipped** |
| `w434dp-h498dp` filter drawer (F-36's state) | 22 clickable nodes, **0 under 48 dp, 0 overlapping, 0 clipped** |
| `w964dp-h434dp` landscape, Plot / Split / Details | 8 / 10 / 10 nodes, **0 / 0 / 0** in every column |

Evidence `V7-narrow-compact.xml`, `V7-narrow-drawer.xml`, `V6-*.xml`.

### 10.6 Two things measured and deliberately not turned into findings

Recorded so the next pass does not spend its budget re-deriving them.

**1. A node scrolled out of a scroller still reports on-screen coordinates.**
This is Avalonia's automation peer reporting a control's layout bounds without
intersecting them with its ancestors' clips, and Android then clamps them to the
screen. It is not a VisualCat layout defect, and it is what three earlier
"sub-floor" readings actually were:

| Reading | Where | What it was |
|---|---|---|
| `Close On-device logcat 00h13m13` **42.3 dp** | §9.4/G-08, four sessions open | the leftmost tab chip, horizontally scrolled half out of the strip |
| `Close … 00h13m13` **15.3 dp** | §5.1/A-05 | the same thing, before §6's trailing-margin fix |
| `Case-sensitive` × `Info level` overlap | §9's `G06-f36-fresh.xml` | drawer rows scrolled above the viewport |

Measured directly this pass: in the landscape *More* sheet, scrolling down moves
`Share…` out of view above the fold, and its reported bounds `[518,278][2153,436]`
then sit **on top of the sheet's own header**, overlapping `Close this sheet` by
49.1 × 40.2 dp. The screenshot of the same frame shows the header drawn correctly
and `Share…` nowhere — the pixels are right and the tree is wrong. A synthetic tap
at a scrolled-out chip's reported centre does nothing, which is the same fact from
the other side.

It has one real cost — TalkBack's touch exploration and node bounds — and no
app-level lever short of custom automation peers for every scroller. §1.1's gap 6
(no hands-on assistive-technology session) is where it belongs, and this entry is
so the *next* sweep reads a duplicate or an overlap in a scrolled pane correctly
instead of filing it.

**One consequence for the tooling.** `audit_layout.py` reports overlaps and
sub-floor nodes from any dump, so a dump taken while a pane is scrolled will show
both. Read it on an unscrolled pane, or read the screenshot beside it.

**2. Panes swept clean.** Measured this pass, on the current build, and free of
sub-floor, overlapping and clipped controls: the **Insights** pane and its
template actions (19 nodes), **Details** mode (19), **Session cache** in landscape
(8 — F-31's spin buttons measure 48.0 dp there), **Recent sessions** in landscape
(the list scrolls and Cancel/Open stay pinned — finding 16's fix holds in a short
viewport), the **filter drawer** at a fourth width (601 dp, 26 nodes), and the
shared command row at the **601 dp boundary** of `SharesARow` (0 overlapping —
the arithmetic in `MobileWorkspaceLayout`'s remarks is conservative, and measured
here the toolbar takes 258.7 dp and the strip 307.2 dp of the 601 available).

The one sub-floor control found in a sheet is **Android's own**: the filename
field in the system Save-file picker, 692.6 × 44.8 dp.

### 10.7 Mutation ledger and hand-back

| Setting | Original (§10.1) | Changed to | Restored |
|---|---|---|---|
| System font scale | `1.0` | `1.3`, `1.0`, `1.15`, `1.0` — F-39's own repro, which is §5's U-04/U-05 sequence | **Yes — `1.0`** |
| Display rotation | `accelerometer_rotation=1`, `user_rotation=0` | locked landscape (`user_rotation=1`) for every compact-height measurement, and portrait for the paired ones | **Yes — `1` / `0`, config back to `w434dp-h964dp port`** |
| `wm size` | `1220x2712` (physical) | `1690x1125` — 601 × 400 dp, the `SharesARow` boundary; `1220x1400` — 434 × 498 dp, §9's F-34/F-35/F-36 viewport, for the regression check | **Yes — `wm size reset`; `Physical size: 1220x2712`** |
| Installed package | Release build of `81bb56b` | Release build of this pass's fixes, `adb install -r -t`, in place | **Not reverted, deliberately** — see below |
| Open session tabs | four (`00h13m13`, `00h25m52`, `14h44m19`, `16h24m26`), `16h24m26` selected, Split | closed to one and reopened, as F-39's tab-close repro | **Yes — four open again, the tab strip byte-identical to §10.1's dump (`[0,291][332,426]` and `[497,291][997,426]`), Split mode restored** |
| Workspace display mode | Split | Plot and Details, to sweep those panes | **Yes — Split** |
| Shared storage | — | `uiautomator` dumps and screenshots at `/sdcard/vc6.xml` / `.png`, deleted after every pull | **Yes — `ls /sdcard/vc6.*` → no such file** |
| `READ_LOGS` | granted | untouched — this pass started no capture | **n/a** |
| Stored settings | text scale `1.00×`, cleanup disabled | untouched — *Session cache* was opened and cancelled, *Appearance & timeline* opened and dismissed; neither was saved | **n/a** |

**Hand-back**

| | Baseline (§10.1) | At hand-back |
|---|---|---|
| Configuration | `sw434dp w434dp h964dp port night` | **identical** |
| `wm size` / `wm density` | `1220x2712` / `450` | **`1220x2712` / `450`** |
| `accelerometer_rotation` / `user_rotation` | `1` / `0` | **`1` / `0`** |
| System font scale | `1.0` | **`1.0`** |
| System / app locale | `en-US` / `[]` | **`en-US` / `[]`** |
| `READ_LOGS` | `granted=true` | **`granted=true`** — neither granted nor revoked by this run |
| `firstInstallTime` | `2026-08-21 23:33:16` | **preserved** — the install was in place |
| `pkgFlags` | `[ HAS_CODE ALLOW_CLEAR_USER_DATA ]` | **identical** — still a Release build |
| Capture children | 0 | **0** — no capture was started |
| Files added to shared storage | — | **none** |
| Battery / thermal | USB powered, `Thermal Status: 0` | **100 %, `Thermal Status: 0`** |
| Workspace at hand-back | 14 clickable nodes, 0 under 48 dp | **14 clickable nodes, 0 under 48 dp, 0 overlapping, 0 clipped** |

**Deliberate deviation, recorded rather than reverted.** The device carries the
Release build of this pass's fixes rather than the Release build of `81bb56b`.
Reverting would put a build with F-38 and F-39 in it back on the phone, which is
strictly worse; §9.6/1 made the same call for the same reason.

### 10.8 What this pass changed about the report

1. **The audit's instrument was the bottleneck, not the audit.** Four passes ran
   a tool that answers one of the three questions a touch layout can fail. Writing
   `audit_layout.py` — sub-floor, overlapping, clipped — and running it over §9's
   own 29 evidence dumps found F-38 in **eleven dumps that had already been taken,
   read, and filed as passes**. The cheapest place to look for the next finding was
   the evidence already on disk.
2. **F-39 is the first finding in this report that is not about a size.** Every
   layout defect from F-32 to F-37 was a decision taken against the wrong number.
   F-39 is about **ownership**: a control was moved out of the view that made it,
   and then that view was replaced without anyone asking for the control back. The
   move itself was §6's fix for a real problem, and it has been correct in every
   viewport ever measured — it just had no symmetric half for "the owner is gone".
   The question that finds this class is not *how big is it* but **who is holding
   it, and what happens when they leave**.
3. **A defect can hide inside the act of measuring.** F-39 is triggered by changing
   the system text size, which is exactly what a pass does to measure a layout at
   1.3×. Five passes performed the trigger and none saw the effect, because both
   the stale strip and the live one measure 48 dp and neither an eye nor a
   sub-floor tool counts duplicates.
4. **Three earlier sub-floor readings were never layout defects.** §10.6 documents
   what they were — Avalonia reporting a scrolled-out node's unclipped bounds — so
   the next sweep spends its budget somewhere else.
5. **The panes are running out.** This pass swept Insights, Details, Session cache,
   Recent sessions, the drawer at a fourth width, and the shared row at its
   threshold, and found **nothing** in any of them. The two findings it did make
   came from a *transition* (a text-size change, a tab close), not from a state.
   §7.6 said findings hide in panes nobody opened; §8.7 said states nobody
   measured; §9.7 said the size the thing is actually given. The next one to try is
   **what a state change leaves behind**.

**Declared limits of this pass.** One device, API 36, Release but debug-signed, so
§1.1's gap 2 is unchanged. No capture was started, so nothing here re-tests the
live path — F-39's capture-row half is covered by the same seam but was not
exercised with a recording running. No TalkBack session, which is where §10.6's
scrolled-bounds observation would actually be judged. No upgrade, Play, endurance,
destructive-storage or locale work. The `RemoveTab` half of F-39's fix has no
headless test and says so in §10.4.

---

## 11. Seventh pass — what a state change leaves behind, on the Pixel 5

§10.8/5 closed the sixth pass with a lead rather than a finding: *"The next one
to try is **what a state change leaves behind**."* This pass takes that lead, on
the one device in the fleet that no compact-layout finding has ever been measured
on — the Pixel 5 — and re-audits every finding the last three passes fixed at a
density and API level they were never measured at.

Entries are appended as they happen, so an interrupted session resumes here.

### 11.1 Run header

| Field | Value |
|---|---|
| Run id | `20260822-pixel5-pass7` |
| Date/time (UTC) | 2026-08-22 15:57 → 16:55 |
| Repository commit at start | `d1eb45a` — *Audit the report a sixth time, and answer what the tooling could not see* |
| Device | Google **Pixel 5** (`redfin`) |
| Serial | `0A031FDD400365` |
| Android release / API | **14 / 34** |
| ABIs | `arm64-v8a, armeabi-v7a, armeabi` |
| Build fingerprint | `google/redfin/redfin:14/UP1A.231105.001.B2/11260668:user/release-keys` |
| Screen / density | 1080 × 2340 px, **440 dpi (2.75 px/dp)** |
| Navigation mode | `2` — **gesture navigation** |
| System locale | `cs-CZ` — a **non-English system locale by default** |
| Theme / font scale | `night` (dark) / `1.0` |
| Rotation | `accelerometer_rotation=1`, `user_rotation=0` |
| Battery / thermal | 100 %, USB powered, `Thermal Status: 0` |
| Artifact | `src/VisualCat.Android/bin/Release/net10.0-android36.0/com.barebit.visualcat-Signed.apk` |
| Artifact SHA-256 (as found) | `b1b7293dc420dbc3cd017b091ce7f284c486e4b8f2f274461021729315fec414`, 31 144 106 bytes |
| Artifact SHA-256 (this pass's fixes) | `92f33713b78a717eed7cea5c0fe463b66a2a1e4112e5f39aaecf879a0b015195` |
| Build/artifact class | **Locally built Release**, debug-signed — §1.1 gap 2 unchanged |
| Install mode | **clean** (`firstInstallTime` = `lastUpdateTime` = `2026-08-22 18:01:17`) |
| Package at install | `versionName=2.0.5-dev`, `versionCode=20005`, `minSdk 31`, `targetSdk 36` |
| `READ_LOGS` at install | `granted=false` |
| Host regression before any change | `dotnet test VisualCat.slnx -c Debug` → **369/369 passed, 0 failed** (Domain 11, Core 88, App 221, Application 49) |
| Evidence root | `artifacts/live-test/20260822-pixel5-pass7/evidence/` |

### 11.2 Why this device, and why this lead

| What the last three passes did | What this pass adds |
|---|---|
| F-31…F-39 were found and verified on the Motorola (450 dpi, API 36) and the Samsung (480 dpi, API 36) | **440 dpi, API 34**, a third dp viewport, and gesture navigation — the axis every compact-layout decision is taken on |
| §10 swept *states*: panes, widths, viewports | §10.8/5's lead: **transitions** — rotate, change the text size, switch theme, close a tab, stop a capture *while something else is open* |
| §7 (this device) ran before F-31…F-39 existed | Every fix since is unmeasured here |
| §7/D-07 left F-19's process-death branch unrun on this device | It is in this pass's ledger |

### 11.3 Step ledger

Resume at the first row that is not **Done**.

| Step | Status | Outcome / next action |
|---|---|---|
| K-01 Audit the tree against §5.1 | **Done** | 40 of 40 findings have a named artefact in the tree; host suite 369/369 |
| K-02 Baseline and clean install of a Release build | **Done** | §11.1 |
| K-03 Re-measure F-31…F-39 at 440 dpi / API 34 | **Done** | 0 sub-floor, 0 overlapping, 0 clipped in all five states swept |
| K-04 The lead: what a state change leaves behind | **Done** | Found **F-40**, **F-41** and **F-42** |
| K-05 F-37/F-39's capture halves, with a recording running | **Done** | One strip, one `Follow`, one `Stop capture` after three text-size changes |
| K-06 F-19's process-death branch on this device | **Done** | Four surfaces agree on `Interrupted · 19 entries recovered`; §7/D-07's gap closed |
| K-07 Fix what this pass finds, with host tests | **Done** | Three fixes, five tests, each proved to fail first; 374/374 |
| K-08 Device verification on a Release build | **Done** | All three fixed and measured on the Pixel; §11.4/K-08 |
| K-09 Hand-back, commit and push | **Done** | Device restored to a package-absent baseline (§11.5) |

### 11.4 Continuous execution log

#### K-01/K-02 — audit and baseline

**Status: Done.** The tree at `d1eb45a` carries a named artefact for all 40
findings (F-01…F-39 plus D-04.0); `dotnet test VisualCat.slnx -c Debug` →
**369/369**. The Release build (`-p:EmbedAssembliesIntoApk=true`) produced
**0 warnings, 0 errors** in 58.7 s and installed clean in 2 118 ms.
Evidence `A01-baseline.txt`, `A02-artifact.txt`, `A03-package.txt`.

#### K-03 — F-31…F-39 re-measured at 440 dpi, API 34, gesture navigation

**Status: Done.** Every finding the last three passes fixed was measured on a
device it had never been measured on. `audit_layout.py` over each dump:

| State | Viewport | Clickable nodes | Sub-floor | Overlapping | Clipped |
|---|---|---:|---:|---:|---:|
| Cold empty state | 393 × 851 dp portrait | 6 | **0** | **0** | **0** |
| Workspace, Split, session open | 393 × 851 dp portrait | 13 | **0** | **0** | **0** |
| Workspace, Split, landscape | 851 × 393 dp | 11 | **0** | **0** | **0** |
| Workspace at system text 1.3× | 393 × 851 dp portrait | 13 | **0** | **0** | **0** |
| Filter drawer, after a text-size change | 393 × 851 dp portrait | 24 | **0** | **0** | **0** |

F-38's compact-height controls measure **48.4 dp** here (`Copy`, `Show the full
message…`, `Load 500 more`), F-34's shared command row has no overlapping pair at
851 dp, and F-35's `Load 500 more` is inside the pane. Evidence
`K03-cold-empty.*`, `K03-opened.*`, `K03-landscape.*`, `K04-workspace-fs13.*`,
`K04-drawer-after-textsize.*`.

Two sub-floor readings in this pass belong to **Android's own** surfaces, not to
VisualCat, and are recorded here so the next pass does not file them: the Files
app's preview toolbar (`Další možnosti`, 40.4 dp) and — as §10.6 already recorded
— the system Save-file picker's filename field.

#### K-04 — the lead: what a state change leaves behind

**Status: Done — three findings.** §10.8/5 asked what a *transition* leaves
behind rather than what a *state* looks like. Three transitions were run against
an open sheet, and all three left it behind. A fourth reading, taken while
setting up the third, is a separate defect in the same pane.

##### F-40 · A sheet is built from the state it opened in, and no state change reaches it

- **Severity** Minor · **Scenario** K-04 · **Device-independent** · **Found by** the seventh pass
- **Reproducibility** deterministic, 3 of 3 transitions, both sheet kinds
- **First suspicion** `MainView.Overlays.cs` — confirmed

`BuildSheet` resolves everything from the moment it runs: the scrim's alpha and
the panel's surface, border and heading colour from `dark`, the heading's size
from `TextScale.Of(15)`, and the panel's height cap from
`Math.Max(240, Bounds.Height * 0.82)`. `PushOverlay` then adds it to
`_overlayHost` and nothing ever writes to it again. `ApplyThemeSurfaces` repaints
the shell, the tab strip, the notice lane, the empty state and every open
workspace — the overlay host is not in its list — and no size handler reaches it
at all.

Measured on the device, all three with *More actions* or *Appearance & timeline*
open:

| Transition | What the workspace did | What the sheet did |
|---|---|---|
| System theme dark → light | repainted fully | **stayed dark**, on a light workspace, with its `Close` button repainted light because that one is theme-resourced — half of one sheet in each variant |
| Rotate landscape → portrait | recomposed | **kept the landscape height cap**: 317 dp of an available 698, less than half the screen it had, listing 3 of 9 commands where all 9 fit |
| System text size 1.0 → 1.3 | rebuilt at 1.3× | **kept every 1.0× font**, so the sheet was the only surface on screen that ignored the reader's text size |

Evidence `K04-sheet-portrait.png` (before), `K04-sheet-rotated-landscape.png`,
`K04-sheet-rotated-portrait.png`, `K04-sheet-theme-flip.png`,
`K04-sheet-fontscale.png`.

Rotation the other way is safe — a cap larger than the window cannot force a
sheet past it — so the defect is one-directional and the theme half is the one a
reader sees first.

##### F-41 · The analysis tab strip slices its own labels at any enlarged text size

- **Severity** Minor · **Scenario** K-04 · **Device-only (measure differs headlessly)** · **Found by** the seventh pass
- **Reproducibility** deterministic at every system text size ≥ 1.15 measured
- **First suspicion** `SessionWorkspaceView.cs` `MobileDetailTab` — confirmed

At system text 1.3× the analysis pane's three tabs read **`Entrie`**,
**`Insigh`** and **`Entr`** — hard-clipped mid-glyph, with no ellipsis and no
other cue that a word has been cut. `Insights` clipped to `Insigh` reads as a
different word.

The tabs do not grow with their text. Measured on the device, the three tab
nodes are **byte-identical at font scale 0.85, 1.0, 1.15 and 1.3** —
`[55,1107][308,1239]`, `[308,…]`, `[561,…]`, 253 px = **92.0 dp each**, which is
exactly `MobileDetailTab`'s `MinWidth = 92`; in landscape they are 215 px =
**78.2 dp**, exactly the compact `MinWidth = 78`. The same view measured
headlessly *does* size to content (118 / 132 / 92 dp at 1.0, 148 / 166 / 111 at
1.3), so the string header's desired width reaches the strip on the desktop and
does not on Android.

Two consequences, and the fix has to answer both: a label that is cut must say
so, and the strip must use the width it actually has rather than a constant
chosen at 1.0×.

##### F-42 · `Load 500 more· 49,656 remaining` — the separator lost its space

- **Severity** Polish · **Scenario** K-04 · **Device-independent** · **Found by** the seventh pass

`SessionWorkspaceView.Interactions.cs` builds one label for the screen reader and
one for the screen, and the screen's is `fullLabel.Replace(';', '·')` — a
character-for-character swap, so `more; 49,656` becomes **`more· 49,656`**. Every
other separator in the product is ` · ` with a space on both sides: `No filters ·
showing everything in view`, `Ready · 50,156 entries`, `DENSITY · 2.03 h · 9.49
s/px`. Visible in `K04-workspace-fs13.png` and every portrait screenshot in this
pass.

#### K-05 — F-37 and F-39's capture halves, with a recording running

**Status: Done — no defect.** §10's declared limit was that no capture ran, so
F-39's capture-row half was covered by the same seam but never exercised. It was
here, on gesture navigation at 440 dpi.

A P0 own-app capture was started (`On-device logcat 18h21m35` — F-16's naming
holds), rotated to landscape, and then the **system text size was changed three
times** (1.3 → 1.0 → 1.15) with the capture running and the command strip hosted
in the shell's shared row:

| After | Clickable nodes | Strips in the row | `Follow` | `Stop capture` | Overlapping |
|---|---:|---:|---:|---:|---:|
| capture started, portrait | 18 | — | 247.6 dp | 97.5 dp | 0 |
| rotated to landscape | 18 | 1 | 360.0 dp | 97.5 dp | 0 |
| three text-size changes | 18 | **1** | 317.8 dp | 109.5 dp | **0** |

One of each control throughout, and 0 sub-floor / 0 clipped in every dump.
Evidence `K05-live-start.*`, `K05-live-running.*`, `K05-live-landscape.*`,
`K05-live-textsize-x3.*`.

The P0 pre-capture explanation was read in full and is F-13's fixed text: own-app
scope stated plainly, no promise of a consent sheet, and the exact `pm grant`
command with the warning that it must be repeated after a reinstall.

#### K-06 — F-19's process-death branch, on this device

**Status: Done — no defect.** §7/D-07 left this unrun on the Pixel. A capture
holding 19 entries was killed with `am force-stop` mid-recording (PID gone, no
`logcat` child left behind), then the activity was launched cold.

All four surfaces agree, and none of them says `Ready` or `Importing`:

| Surface | What it says |
|---|---|
| Tab chip | `Show interrupted session On-device logcat 18h21m35` · *"This capture ended before it was finalized; open it to inspect the recovered data."* |
| Notice lane | *"This capture was interrupted. Everything below reached disk and is exact; anything the source produced after the last save is not in the session."* + **Review** |
| Status line | `Interrupted · 19 entries recovered · the capture ended before it was finished` |
| Review sheet | `Recovered capture` · 19 entries · **Keep** / **Export recovered data** / **Delete**, each explained |

19 recovered = the 19 that were in the session at the kill. Evidence
`K06-before-kill.*`, `K06-capture-tab.*`, `K06-after-restore.*`, `K06-review.*`.

#### K-07 — the fixes, and the host tests that guard them

**Status: Code done.** Each test was run against the tree *before* the fix and
**failed**, then against the tree after and passed — 5 of 5.

| Finding | Files | Decision |
|---|---|---|
| F-40 | `MainView.Overlays.cs`, `MainView.cs` | `BuildSheet` now reports a `SheetSurface` — scrim, panel, heading, close, fade, body host — and `RefreshOverlays()` re-resolves all of it from the state the application is in *now*. It is called from the three transitions that walked past the overlay host: `ApplyThemeSurfaces`, `SizeChanged`, and the text-scale branch of `ApplyDisplayConfigurationChange`. A sheet whose body holds nothing half-finished (the command list, derived entirely from `_secondaryCommands`) is given a **new body**; a dialog body is left alone, because rebuilding one would discard the edit the reader opened it to make, and its own controls are theme-resourced and follow the variant on their own. |
| F-41 | `SessionWorkspaceView.cs`, `.Mobile.cs` | Two halves, because the defect has two. The header is a `TextBlock` with `CharacterEllipsis` rather than a bare string, so a caption that cannot fit ends in an ellipsis instead of being cut through a glyph — and `AutomationProperties.SetName` keeps the whole word for a screen reader. And the tabs are given `Width = (pane − slack) / 3` rather than a constant chosen at 1.0×, decided where the pane's width is known instead of left to a measure that answers differently on each platform. `MoveSummaryIntoTabStrip` now takes the room *left beside* the tabs rather than re-deriving it from a hard-coded 78 dp tab. |
| F-42 | `SessionWorkspaceView.Interactions.cs` | `Replace("; ", " · ")` rather than `Replace(';', '·')`, so the mark keeps its own spacing. |

**Host regression:** `dotnet test VisualCat.slnx -c Debug` → **374/374 passed, 0
failed** (Domain 11, Core 88, App 226, Application 49) — the 369 this pass started
from plus its own 5.

One regression was caught and fixed by the suite rather than by the device:
three tabs sized to exactly the pane wrapped to a second row at 360 dp and took a
band of the entries list with them (`TheEntriesListKeepsAFloorOnAShortViewport`).
The strip pays for its own padding, so the share is taken from the pane *less a
slack*, and one row stays one row at every width measured.

#### K-08 — device verification, on a Release build

**Status: Done.** All three fixes were built into a Release APK
(`0 warnings, 0 errors`), installed with `adb install -r -t`, and measured on the
Pixel 5.

**F-41.** The analysis tabs, from the same dump position at two text sizes:

| | Before (`K04-tabs-*`) | After (`V2-tabs-*`) |
|---|---|---|
| Tab bounds at 1.0× | `[55,1107][308,1239]` and the two beside it — 253 px, **92.0 dp** | `[55,1107][377,1239]`, `[377,…]`, `[699,…][1021,…]` — 322 px, **117.1 dp** |
| Tab bounds at 1.3× | **byte-identical to 1.0×** | 117.1 dp, **contiguous and non-overlapping** |
| Labels at 1.3× | `Entrie` · `Insigh` · `Entr` | **`Entries` · `Insights` · `Entry`** — whole, no ellipsis needed |
| Strip width used | 276 dp of 393, with 79 dp standing empty | 351 dp of 393 |

The 25 dp per tab the strip already had was enough to hold the whole word at
1.3×; the ellipsis is what answers the scales beyond it.

**A defect the fix created, and the check that caught it.** The first version set
`Width` on each tab. On the device the tabs then reported 117.1 dp *at a 92 dp
stride* — `[21,343]`, `[273,595]`, `[526,848]` — each drawing 25 dp over its
neighbour and the first starting 16 dp off the pane's left edge, which is F-34's
defect wearing F-41's clothes. It reproduced headlessly, and the probe that
followed is what found the real root cause:

> The strip's `WrapPanel` is arranged **once**. Every later change to a tab — its
> font size, its minimum width, its padding — re-measures the tab and leaves the
> row's slots exactly where the first pass put them. `ApplyMobileLayout` runs on
> `SizeChanged`, which is *after* that first arrange, so **nothing it has ever
> written to these tabs has reached their layout**.

That is the whole of F-41, and it explains both halves of the evidence at once:
on the device the first pass ran before the headers were templated, so all three
were arranged at their bare `MinWidth` and no text size ever moved them; in a
headless run the first pass ran after templating, so the same defect showed as
tabs frozen at their 1.0× content. The fix is `RearrangeTabStrip`, which
invalidates the panel's *arrange* — reaching it through the `ItemsPresenter`,
because invalidating the `TabControl` re-measures the strip and leaves the slots
where they were — and it is guarded so that it runs only when the share actually
changes. `Bounds.X` non-overlap is now an assertion, not an observation.

**F-40**, with *More actions* open throughout:

| Transition | Before | After |
|---|---|---|
| System theme dark → light | sheet stayed dark on a light shell, with a light `Close` button on it | **the whole sheet is light** — panel, scrim, headings, labels and descriptions |
| Rotate landscape → portrait | 317 dp of an available 698; 3 of 9 commands | **1 885 px ≈ 685 dp; all 9 commands under all 3 headings** |

Evidence `V4-sheet-theme-flip.png`, `V4-sheet-landscape.*`,
`V4-sheet-back-portrait.png`.

**F-42.** `Load 500 more · 49,656 remaining` on screen, and the screen reader's
sentence is unchanged (`content-desc="Load 500 more; 49,656 remaining"`).
Evidence `V3-loadmore.png`.

### 11.5 Mutation ledger and hand-back

| Setting | Baseline (§11.1) | Changed to | Restored |
|---|---|---|---|
| Installed package | **absent** | clean install, then two in-place Release updates | **Yes — `adb uninstall` succeeded; package and process absent** |
| System font scale | `1.0` | `1.3`, `0.85`, `1.15`, `1.0` — F-41's repro and F-39's | **Yes — `1.0`** |
| Display rotation | `accelerometer_rotation=1`, `user_rotation=0` | locked landscape and portrait for the rotation transitions | **Yes — `1` / `0`** |
| System theme | `night = yes` | `no`, for F-40's theme half | **Yes — `night = yes`** |
| `READ_LOGS` | not granted | **untouched** — every capture in this pass was P0 own-app | **n/a** |
| Shared storage | six pre-existing files in `/sdcard/Download/` | **nothing added** — the pass used the device's own `demo-small.txt` | **Yes — all six present, none added, none removed** |
| App-private data | none | two sessions (one file import, one interrupted capture) | **Yes — removed with the package** |
| Device dumps | — | `/sdcard/vc7.*` and `/sdcard/vct.xml`, deleted after every pull | **Yes** |

**Hand-back**

| | Baseline (§11.1) | At hand-back |
|---|---|---|
| Package | absent | **absent**, no process |
| `wm size` / `wm density` | `1080x2340` / `440` | **identical** |
| `accelerometer_rotation` / `user_rotation` | `1` / `0` | **`1` / `0`** |
| System font scale | `1.0` | **`1.0`** |
| Theme | `night = yes` | **`night = yes`** |
| `/sdcard/Download/` | 6 files | **the same 6 files** |
| Battery / thermal | 100 %, `Thermal Status: 0` | **100 %, `Thermal Status: 0`** |

Evidence `HB-handback.txt`. Unlike §9.6 and §10.7, nothing is deliberately left
behind: this device had no VisualCat package at baseline, so restoring it means
removing the one this pass installed.

### 11.6 Declared limits of this pass

1. **One device, API 34, Release but debug-signed.** §1.1's gap 2 is unchanged.
2. **P0 only.** `READ_LOGS` was never granted, so nothing here re-tests
   full-device scope, F-12's buffer attribution, or the P1 consent sheet.
3. **No TalkBack session.** §1.1's gap 6 stands, and §10.6's scrolled-bounds
   observation is still where it would be judged.
4. **F-40's text-size half is verified headlessly, not on the device.** The theme
   and rotation halves were both measured on the Pixel; the text-size half has a
   host test that was proved to fail first, and one `RefreshOverlays` call serves
   all three.
5. **A dialog body still does not re-resolve its own typography.** Deliberate,
   and recorded in §5.4/5 rather than left implicit.
6. **No upgrade, Play, endurance, destructive-storage or locale work.** The
   device's `cs-CZ` system locale was observed — the Android file picker is
   Czech, VisualCat's own strings are English as it ships — but no locale
   assertion was made.

### 11.7 What this pass changed about the report

1. **§10.8/5's lead was right, and it was worth three findings.** Asking what a
   *transition* leaves behind rather than what a *state* looks like found F-40 in
   the first three transitions tried, and F-41 turned out to be the same shape:
   not a wrong number, but **a number written after the only pass that would have
   read it**.
2. **The two are one sentence about different owners.** A sheet is built from the
   state it opened in and no state change reaches it; a tab strip is arranged
   from the state it was built in and no later write reaches it. §10.8/2 said the
   question that finds F-39's class is *who is holding it, and what happens when
   they leave*. This pass's is **when was this decided, and what has changed
   since**.
3. **The instrument caught a defect the fix introduced.** The first F-41 fix
   produced overlapping touch targets — F-34's exact defect — and the overlap
   check plus a headless bounds probe found it before the device did. §10.8/1's
   lesson about the audit's instrument applies to the remediation's: a layout fix
   needs the overlap check run against it, not only the size check it was written
   for.
4. **A stale arrange is invisible to every check this report has.** Both sweeps
   read the *rendered* tree; a control whose measure is right and whose row is
   stale passes the size check, passes the overlap check for as long as it stays
   under-sized, and only fails once something makes it grow. The question that
   finds this class is **does the parent know?**
5. **The panes really have run out; the transitions have not.** §10 swept six
   panes and found nothing in any of them. This pass opened one sheet and rotated
   the phone.

---

## 12. Play-style Wireless ADB transport — Pixel 5, API 34

### 12.1 Run header and scope

| Field | Value |
|---|---|
| Date/time (UTC) | 2026-08-23 18:27–18:56 |
| Repository commit / tree | `479beab8`; working-tree implementation under review |
| Device | Google Pixel 5 (`redfin`), Android 14 / API 34, `arm64-v8a` |
| Serial | `0A031FDD400365` |
| Test artifact | Debug-signed, Play-like build with `VisualCatEnableReadLogsPermission=false`, embedded assemblies, 77,709,717 bytes, SHA-256 `c6ab8336c6c3ffea9fab938a56f4596bd055d093877e7d94d4117239735e8dde` |
| Package state | clean uninstall/install before first-use tests; data retained for restart tests |
| Permission oracle | packaged APK declared only `INTERNET`, `CHANGE_WIFI_MULTICAST_STATE`, and AndroidX's package-scoped `DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION`; no log, phone-state, or storage permission |
| Test constraint | Android 14 closes the pairing-code socket when its Settings dialog loses the foreground. A temporary DEBUG-only activity invoked the same `WirelessAdbService.PairAndConnectAsync` while Settings remained visible in split screen. It was deleted immediately after pairing and is absent from the final source/build. All saved reconnect, capture, interruption, recovery, Stop, and restart checks used the normal product UI. |

This is a focused transport/UX pass, not a replacement for the broader OEM,
accessibility, endurance, signing, or Play-delivery matrix in the release plan.

### 12.2 Results

| Scenario | Result and evidence |
|---|---|
| Clean first use | **Pass.** Live showed **Recommended · setup required**, the full-device/own-app scope distinction, no `READ_LOGS` promise, and explicit text that Android leaves Wireless debugging enabled after VisualCat closes its connection. |
| Invalid first pairing | **Pass.** Pairing to loopback port 1 failed with actionable UI; the six-digit field was cleared; an encrypted identity file existed but no success marker did. Reopening Live still showed setup required rather than “already paired.” |
| Pairing secret handling | **Pass for app-owned surfaces.** VisualCat's logs contained no six-digit code and explicitly logged that it was neither stored nor logged. The test harness necessarily supplied the ephemeral code through an ADB command, so host/adbd command-history confidentiality is not claimed by this test mechanism. |
| Real pairing and persisted state | **Pass.** Android accepted the app's RSA identity, mDNS found the separate TLS service, authenticated connection succeeded, and `no_backup` contained the 2,019-byte AES-GCM identity plus the 12-byte non-secret `wireless-adb-paired-v1` marker. |
| Returning-user UX | **Pass.** Live changed to **Recommended · already paired** and **Connect full-device**. Selecting it automatically attempted the saved connection, eliminating the previous second manual connect press. |
| Full-device scope | **Pass.** The normal UI reached `Wireless debugging full-device logcat`; externally generated `VC_EXTERNAL` error/warn records were present in the finalized `raw.log`. |
| Transport interruption UX | **Pass.** Turning Wireless debugging off changed the status to `Wireless debugging interrupted · reconnecting 1/5`, retained the received-line count, stated that Stop remained available, and kept the session open. |
| Resume | **Pass.** Re-enabling Wireless debugging during the retry window recovered on attempt 5 and returned the status to normal capturing. The bounded receive queue also recycled once under load, exercising the same reconnect path. |
| Reconnect record integrity | **Pass.** The finalized manifest recorded 15,668 source lines = 14,040 parsed entries + 1,628 meta records, with 0 unknown lines, 0 rejected candidates, 0 continuations, 0 long-line overflows, and 2 reconnect gaps. Timestamp replay deliberately duplicated some complete records and produced out-of-order entries; no partial old/new transport line was observed or parsed. |
| Stop and key lifecycle | **Pass.** Stop kept 14,040 entries, closed the stream and connection, disposed the manager, and logged that decrypted key material was discarded. UI explained that Android's Wireless debugging toggle remained enabled and offered **Open settings**, which opened Developer options successfully. |
| Process-death saved reconnect | **Pass.** After force-stopping and relaunching VisualCat, Live still recognized the completed pairing, automatically reconnected without a new code, captured 511 entries, and stopped cleanly. |

### 12.3 Finalized-session oracle

The first Wireless capture finalized normally (`degraded=false`) with SHA-256
`2AFCAC5C71DAAC6E0ECDBFEA4A9CB3D871A4913FFE0670EDAA7D5947B8526532`
over its embedded 2,449,564-byte raw source. Its defect record contains
`reconnectGaps=2` and no parser-corruption indicators. The raw source contains
both externally injected proof records twice because timestamp-based reconnect
replays the boundary intentionally; this is complete-record overlap, not byte
concatenation.

### 12.4 Remaining limits

1. The transport was exercised on one API-34 Google device and one Wi-Fi network.
2. The locally built test APK was debug-signed, not the Play upload-key artifact.
3. First pairing needed the explicitly documented temporary harness because the
   Android 14 Settings pairing socket closes on app switch; the product setup UI
   itself was inspected and its validation/failure path was exercised live.
4. No TalkBack, API 31–33, OEM skin, network-roaming, or multi-hour Wireless ADB
   soak is claimed by this focused pass.

### 12.5 Final release-package smoke and hand-back

After removing the temporary test installation and its app-private pairing data,
the final harness-free Release APK was installed cleanly on the same Pixel. The
installed package reports version `2.0.6` / code `20006`, is not debuggable,
launches to the expected first-use home screen, remains alive after startup, and
produced no fatal or unhandled exception in its process log. Its decoded manifest
contains no test activity and requests only `INTERNET`,
`CHANGE_WIFI_MULTICAST_STATE`, and AndroidX's package-scoped dynamic-receiver
permission.

The packaging verifier accepted both the 35,216,767-byte APK (SHA-256
`70D1F44531741237E4220631B98AA549337786F2DAB2D0D282CA28CA2C26C800`) and the
35,086,056-byte AAB (SHA-256
`6B63D950D214ACDB8C61826837386144A1185C8EB8136913CFDBDB08E71152A9`) through
their structural, manifest, and signing checks, then correctly rejected the
locally supplied debug certificate because it is not the configured Google Play
upload certificate. This is an intentional release gate, not a package defect.

At hand-back, the Release app is installed and open, its data is clean (no saved
test identity or pairing marker), and Android's Wireless debugging toggle is off.

---

## 13. Samsung API-36 responsive-layout recheck

### 13.1 Run header and scope

| Field | Value |
|---|---|
| Date | 2026-08-23 |
| Repository commit / tree | `479beab8`; working-tree implementation under review |
| Device | Samsung Galaxy S21 FE / SM-G990B, Android 16 / API 36, `arm64-v8a` |
| Display | 1080 × 2340, density 480 (360 × 780 dp application viewport in portrait) |
| Package | Optimized, non-debuggable Release 2.0.6 APK, locally debug-signed |
| Install state | Existing 2.0.5-dev removed; every decisive pass began from an uninstall/install or the freshly rebuilt in-place Release update |
| Capture scope | On-device own-app logcat. The already device-verified Wireless ADB transport in §12 was not re-paired on this OEM device. |

The trigger for this pass was a visible Samsung landscape defect: the Time
picker and Copy and Entry actions were only partly usable and appeared to run
into the list/status controls below them. The pass therefore measured the
rendered Android bounds as well as inspecting screenshots and exercising the
actions.

### 13.2 Findings and fixes

| Finding | Remediation and Samsung proof |
|---|---|
| Home severity/actions wrapped differently at Samsung's 480 dpi | Mobile severity is now a deliberate 3 × 2 grid and home actions a stable 2 + 1 grid. No orphan chip, bullet, or action remains. Evidence `02-responsive-home.png`. |
| A long own-app notice consumed 86 dp in a 360 dp-high landscape workspace | Compact-height notices now keep their complete, scrollable text in a 48 dp lane with no vertical host padding. The entry list grew from about 8 dp to 45 dp and the status line moved below it. Evidence `06-own-live-landscape.png` before and `08-fixed-live-landscape.png` after. |
| Entry-inspector descendants could paint beyond the inspector card | The inspector's outer grid, scroll viewport, pinned header, raw surface, and card now own clipping boundaries. Scrolling keeps Copy message and source context inside the card. Evidence `10-entry-inspector-landscape.png`, `11-fixed-entry-inspector-landscape.png`, and `12-entry-inspector-scrolled.png`. |
| The reported Time / Copy / Entry action strip could exceed its pane | The sort picker owns a measured slot; Copy and Entry divide all remaining width, keep 48 dp touch height, and use compact visible labels when width divided by effective text scale requires them. Their full tooltip and accessibility descriptions are unchanged. In the final landscape tree Time was `[1031,435][1367,579]`, Copy `[1385,435][1728,579]`, and Entry `[1746,435][2088,579]`: two equal 48 dp action slots, wholly inside the pane. Evidence `34-final-equal-actions-landscape.png`. |
| At 130% Android text, `Details`, `Copy raw`, and `Entry ⤢` could be clipped | The tight visible labels become `Logs`, `Copy`, and `Entry`, while the full semantic names remain in the accessibility tree. The 130% scope dialog remained scrollable above a pinned footer and both choices were reachable. Evidence `22-clean-home-font-scale-130.png`, `24-scope-dialog-font-scale-130-scrolled.png`, and `26-live-font-scale-130.png`. |
| A short *and narrow* workspace classified as compact-height put the plot and analysis pane in the same cell | Compact-height now uses side-by-side panes only when the scaled shared-row width actually fits; otherwise the plot, minimap, and analysis occupy rows 2, 3, and 5. This removed the large-text/long-notice overlap. Evidence `26-live-font-scale-130.png` before and `27-after-stacked-fix-launch.png` after. |

### 13.3 Functional and interaction results

| Scenario | Result |
|---|---|
| Clean install / launch | **Pass.** Release 2.0.6 launched on API 36, stayed alive, and emitted no fatal or unhandled exception. |
| Scope chooser | **Pass.** Both choices, explanations, state change, and pinned action footer remained reachable at 100% and 130% system text. |
| Own-app live capture | **Pass.** Entries arrived, counters and timeline updated, Follow could be disabled, and Stop retained the captured session. |
| Rotation during capture | **Pass.** The same process and capture survived portrait/landscape recomposition. |
| Reported controls | **Pass.** Time, Copy, and Entry were fully visible, separate, and inside the analysis pane in short landscape. |
| Row Copy | **Pass.** Selecting a row enabled Copy; tapping it produced Samsung's clipboard toast and VisualCat's `Copied the raw text of 1 entry.` notice. Evidence `17-entries-landscape.png`, `18-header-copy-confirmation.png`, and the final build's `35-final-copy-confirmation.png`. |
| Inspector / Copy message | **Pass.** Entry opened the inspector, its content scrolled within the card, and Copy message produced Samsung's clipboard toast plus `Copied 66 characters of this entry.` Evidence `14-entry-copy-position.png`, `16-entry-copy-immediate.png`, and the final build's `36-final-entry-inspector.png`. |
| Large text | **Pass after remediation.** At system font scale 1.3, home, scope selection, plot/list stacking, and action labels remained readable and reachable. |

Five Samsung-specific headless contracts now cover the 3 × 2 legend, stable
home actions, compact notice lane, inspector clipping/scroll ownership, and
large-text action bounds. The existing narrow-short regression now also asserts
that the plot and analysis occupy distinct rows; checking only the one-column
root had previously missed their same-cell overlap.

### 13.4 Limits and hand-back

This OEM pass did not repeat real Wireless ADB pairing or full-device capture;
those transport and process-death paths are recorded in §12. It did exercise the
Samsung/API-36 first-use UI and own-app fallback that a user can select without
pairing. The APK is structurally verified and correctly fails the final
production-upload gate when compared with the configured Play certificate: the
local test certificate is intentionally not the production upload certificate.
The final APK is 33,658,932 bytes with SHA-256
`5C4648B404E1312CF797E7BA88BFF26F208C4FCF0C53DE5DC830568F33A66E14`.

At hand-back the device is restored to font scale 1.0, automatic rotation with
portrait user rotation, and Wireless debugging off. The final Release build is
installed cleanly and open on the home screen; capture/session test data and the
previous app installation have been removed.

---

## 14. Motorola API-36 final OEM and overlay recheck

### 14.1 Run header and scope

| Field | Value |
|---|---|
| Date | 2026-08-23 |
| Repository commit / tree | `479beab8`; working-tree implementation under review |
| Device | Motorola Edge 60 Pro (`motorola_edge_60_pro` / `cybert`), Android 16 / API 36, `arm64-v8a` |
| Serial | `ZY22M4T2Z4` |
| Display | 1220 × 2712, density 450 (approximately 434 × 964 dp in portrait) |
| Package | Optimized, non-debuggable Release 2.0.6 APK, locally debug-signed, 35,216,767 bytes, SHA-256 `99BCD3E830DC03EC2916C62CF71F50B7C24A5B25FDE28DFBBA4376CDF73DFE2B` |
| Install state | Existing 2.0.5-dev removed before testing; corrected Release package then removed and installed cleanly for hand-back |
| Capture scope | On-device own-app logcat plus Wireless-debugging setup/validation UI. Real Wireless ADB pairing was not repeated; §12 owns that transport evidence. |

This pass rechecked the Samsung-responsive changes on a second API-36 OEM and
then followed up two user-observed edge defects: the warning appeared cut off,
and dialog sheets appeared to have the same incomplete lower boundary.

### 14.2 Findings, intent and remediation

| Finding | Resolution and Motorola proof |
|---|---|
| The compact warning deliberately shows its complete long message through an internal scroller, but its border omitted the lower edge | The compact 48 dp lane remains intentional so it does not cover the log list; the missing edge was not. The host now draws all four border sides. At 130% text in landscape its rendered bounds were `[128,1079][2577,1220]`, the full red lower outline was visible, and the message remained scrollable beside a fully visible Dismiss target. Evidence `17-large-text-live-landscape.png`. |
| Phone dialog sheets used an open-bottom border and square lower corners flush with the viewport | Every in-page sheet now has a complete one-pixel frame, four 16 dp rounded corners, and an 8 dp side/bottom inset. The scope sheet changed from visually open at the lower screen edge to `[480,197][2225,1198]` in landscape and `[22,882][1198,2555]` in portrait. Evidence `12-corrected-popup-landscape.png` and `13-corrected-popup-portrait.png`. |
| Enlarged text is the highest-risk version of both defects | At Android font scale 1.3, the portrait scope sheet remained completely framed at `[22,367][1198,2554]`; both choices and the pinned Cancel/Start action row were reachable. Evidence `15-large-text-popup-portrait.png`. |

The notice and sheet fixes have headless contracts for all four border edges;
the sheet contract additionally requires rounded lower corners and positive
left, right and bottom insets.

### 14.3 Responsive and functional results

| Scenario | Result |
|---|---|
| Clean install / launch | **Pass.** The prior 2.0.5-dev app was uninstalled. Release 2.0.6 reported version code 20006, target SDK 36, no `DEBUGGABLE` flag, remained alive, and emitted no fatal or unhandled exception. |
| Home at 100% and 130% text | **Pass.** Header actions, 3 × 2 severity legend, 2 + 1 hero actions, version and session card remained separate and readable in portrait and landscape. Evidence `11-corrected-clean-landscape.png` and `14-large-text-home-portrait.png`. |
| Scope chooser and rotation | **Pass.** The open sheet recomposed between landscape and portrait without losing state; its body scrolled independently above a pinned, reachable footer. |
| Reported Time / Copy / Entry strip | **Pass.** At 130% text in the compact landscape pane, Time was `[1204,545][1519,680]`, Copy `[1536,545][1998,680]`, and Entry `[2014,545][2475,680]`. All were 48 dp high, separate, and inside the pane; selecting a row enabled both actions. Evidence `17-large-text-live-landscape.png`. |
| Own-app capture | **Pass.** Entries arrived and timeline/counters updated; Follow toggled; selecting, row Copy, Entry, inspector Copy message, and Stop all worked, with Motorola clipboard feedback and VisualCat confirmation. The stopped session retained 11 entries. |
| Entry inspector | **Pass.** The selected message, Copy message, source-context disclosure and internal scrollbar stayed inside the inspector card in short landscape. Evidence `08-entry-inspector.png` and `09-inspector-copy.png`. |
| Wireless setup UI | **Pass.** The nested setup sheet, pairing-port/code fields, privacy explanation and footer were fully visible. Empty submission focused the numeric port field; after keyboard dismissal the inline `1 to 65535` validation and both footer buttons were reachable. Evidence `21-wireless-setup-portrait.png` through `23-wireless-validation-visible.png`. |
| Warning behavior | **Pass.** Portrait shows the longer scroll viewport; short landscape uses the compact lane. Both retain the complete message and a full-size Dismiss target without covering the action strip or list. |

### 14.4 Limits, package gate and hand-back

Real pairing/full-device capture was not repeated on the Motorola because the
same product transport, pairing identity lifecycle, reconnect and process-death
paths are exercised in §12. This pass does not claim TalkBack, locale, multi-hour
soak, network roaming or production-key signing on this OEM device.

The packaging verifier accepted the APK's structure, manifest and signing, then
correctly rejected the local debug certificate SHA-256
`848F98961FA6784F651331B2312C97D47A5D0861B492243BFE4062F58B8B92A9`
against the configured Google Play upload certificate. This expected release
gate is not a functional package failure.

At hand-back, font scale is 1.0, automatic rotation is restored with portrait
user rotation, Wireless debugging remains off, temporary device-side XML files
are removed, and the final Release APK is installed cleanly and opened on the
home screen. The test capture/session data and prior installation are removed.

---

## 15. Pixel 5 post-commit UI and functional recheck

### 15.1 Run header and scope

| Field | Value |
|---|---|
| Date | 2026-08-23 |
| Repository commit / tree | `97099da`; working-tree remediation under review |
| Device | Google Pixel 5 (`redfin`), Android 14 / API 34, `arm64-v8a` |
| Serial | `0A031FDD400365` |
| Display | 1080 × 2340, density 440 (approximately 393 × 851 dp in portrait) |
| Final test package | Optimized, non-debuggable Release 2.0.6 APK, locally debug-signed, 33,658,932 bytes, SHA-256 `8295D414D11F34DF9C48041A0C89691415AB58320C425ABD67E715DCED69BBAB` |
| Install state | Existing Release 2.0.6 and its saved pairing identity were removed before the pass; the corrected package was installed again from a clean state for hand-back |
| Scope | Regression of commit `97099da` at 100% and 130% Android text: home, scope/setup sheets, warning frame, Time/Copy/Entry, own-app capture, inspector, clipboard, rotation, invalid pairing UI and process logs |

This returns to the API-34 Pixel that owns the real Wireless ADB transport
evidence in §12. Because the required clean uninstall removed that encrypted
pairing identity and Wireless debugging was off, the pass did not manufacture a
second pairing harness. It rechecked the product UI and own-app transport while
retaining §12 as the pairing/reconnect oracle.

### 15.2 Regressions from the preceding commit

| Finding | Remediation and Pixel proof |
|---|---|
| At 130% Android text, the home headline's accessibility node still contained `SEE THE SHAPE OF YOUR LOG`, but the visible single line ended after `YOUR L` | The headline now wraps and remains center-aligned. Its rendered height changed from 93 px (`[66,921][1014,1014]`) to 185 px (`[77,874][1003,1059]`), showing the complete final word on a second centered line. Evidence `22-large-text-home-observed.png` and `23-fixed-large-text-home.png`. |
| Empty Wireless-ADB submission focused the port, but the only validation line was after the entire form and appeared behind the pinned footer at 130% text | The first remediation kept port and code validation adjacent to their editor, cleared it as the reader edited, and used assertive accessibility announcements. The final Pixel port error was fully visible at `[102,1504][945,1597]` rather than intersecting the footer beginning at y=2023. §16 subsequently refines the adjacent ordering and IME scrolling for Samsung's taller keyboard. Evidence `17-large-text-wireless-validation.png` and `24-final-validation.png`. |

Two headless contracts preserve these results: the home headline must wrap and
center, while each field validation must remain grouped directly with its
editor, appear on invalid submission, and clear on edit.

### 15.3 Regression and interaction results

| Scenario | Result |
|---|---|
| Clean launch | **Pass.** The old package was uninstalled, the current Release installed cleanly, the process remained alive, and no fatal exception or ANR was logged. |
| Complete popup frame | **Pass.** The scope sheet was fully bordered, rounded and inset above Pixel gesture navigation in portrait (`[22,570][1058,2252]`) and landscape (`[386,106][2091,992]`). Its body scrolled independently while Cancel and the primary action stayed pinned. |
| Large-text sheets | **Pass after remediation.** At font scale 1.3, scope choices, setup guidance, port/code editors, adjacent validation and footer actions remained reachable. |
| Own-app capture | **Pass.** Live received entries, timeline and counters updated, Follow toggled, and Stop retained the complete session. |
| Time / Copy / Entry | **Pass.** In compact landscape Time was `[1108,534][1416,666]`, Copy `[1432,534][1828,666]`, and Entry `[1845,534][2242,666]`: separate, full 48 dp targets inside the analysis pane. Selecting a row enabled both actions. |
| Clipboard and inspector | **Pass.** Row Copy invoked Pixel clipboard feedback; Entry opened the clipped/scroll-owned inspector; scrolling revealed Copy message and source context; Copy message produced `Copied 78 characters of this entry.` |
| Compact warning | **Pass.** The complete lower border remained visible immediately above gesture navigation, long text scrolled internally, and Dismiss stayed a full touch target without covering the action strip. |
| Rotation | **Pass.** The open scope sheet and active capture recomposed between portrait and landscape without process loss or state loss. |

### 15.4 Package gate, limits and hand-back

The packaging verifier accepted the APK structure, manifest and signature, then
correctly stopped at the configured production-upload-certificate comparison:
the local debug certificate is not the Google Play upload key. Real new pairing,
saved reconnect, interruption recovery and full-device external-log ingestion
remain covered by §12 rather than being claimed again here. TalkBack, multi-hour
soak and network roaming were not repeated in this focused post-commit pass.

At hand-back the test installation/data and device-side XML dumps are removed,
font scale is restored to 1.0, automatic rotation and portrait user rotation are
restored, Wireless debugging is off, and the final corrected Release package is
installed cleanly and open on the home screen.

---

## 16. Samsung API-36 quick UX/UI follow-up

### 16.1 Run header and scope

| Field | Value |
|---|---|
| Date | 2026-08-23 |
| Repository baseline | `ed02669`; working-tree remediation under review |
| Device | Samsung Galaxy S21 FE / SM-G990B (`r9q`), Android 16 / API 36, `arm64-v8a` |
| Serial | `RFCRC0A9GND` |
| Display | 1080 × 2340 at density 480 (360 × 780 dp portrait); Samsung three-button navigation and Samsung keyboard |
| Final test package | Optimized, non-debuggable Release 2.0.6 APK, locally debug-signed, SHA-256 `0E8E12150234A2193517668D9A883A680FE619CC8164DF6D676AAEA3A63594B2` |
| Install state | The previously installed Release and all of its app data were removed before testing; each corrected package was installed with clean app data |
| Scope | 100% and 130% Android text; home hero; portrait and landscape sheets; both invalid pairing fields with the Samsung IME; own-app capture; Time/Copy/Entry; inspector; compact warning; rotation and crash logs |

### 16.2 Finding and final remediation

The Pixel correction in §15 exposed one remaining OEM-keyboard edge case. At
130% text, Samsung's numeric keyboard began around y=1310. When port validation
followed its editor, the editor was fully visible at `[111,1144][933,1288]` but
the explanation began at `[111,1306][933,1407]` and was covered by the IME.
Simply moving the explanation first reversed the problem: the explanation was
visible, but only the top of the editor remained above the keyboard.

The final implementation treats each validation/editor pair as one field group,
places the assertive explanation immediately before its editor, and adds a
temporary non-interactive bottom scroll reserve only after Android validation
opens the IME. Once the IME animation settles, the internal form receives a
bounded nudge derived from the measured group height and remaining scroll range;
there are no device-resolution coordinates in product code. The timer and target
are released when the dialog is disposed, and valid submission removes the
temporary reserve.

Final Samsung measurements at 130% text:

| Field | Validation bounds | Editor bounds | Result |
|---|---:|---:|---|
| Pairing port | `[111,1007][933,1108]` | `[111,1144][933,1288]` | **Pass:** both are complete above the numeric IME |
| Pairing code | `[111,878][933,979]` | `[111,1016][933,1160]` | **Pass:** both are complete above the larger alphanumeric IME |

The headless contract now also requires the explanation and editor to be the two
members of the same field group in that order. Evidence is preserved as
`06-large-validation.png` (original failure), `11-nudged-validation.png` and
`13-reserved-code.png` under the ignored Samsung recheck artifact directory.

### 16.3 Focused UX/UI and functional results

| Scenario | Result |
|---|---|
| Clean launch / enlarged home | **Pass.** The old package was uninstalled first. The complete hero headline rendered on one line at 100% and two centered lines at 130%, with all severity and action controls intact. |
| Popup frame and pinned actions | **Pass.** Scope and pairing sheets remained rounded and inset in portrait and landscape; body content scrolled independently while all footer buttons remained full targets. |
| Own-app Live | **Pass.** Live started from the explicit VisualCat-only choice, updated density/timeline/counters, and retained 22 entries after Stop. |
| Time / Copy / Entry | **Pass.** In Samsung landscape Time was `[1031,435][1367,579]`, Copy `[1385,435][1728,579]`, and Entry `[1746,435][2088,579]`: separate complete 144 px / 48 dp targets. A retained row enabled both actions. |
| Clipboard and inspector | **Pass.** Copy produced `Copied the raw text of 1 entry.` and Samsung clipboard feedback. Entry opened the scroll-owned inspector with a complete Copy message action. |
| Compact warning | **Pass.** The lower border remained visible at y=1079, the body owned its overflow, and Dismiss stayed a complete 144 px / 48 dp target at `[1947,933][2163,1077]`. |
| Stability | **Pass.** The process remained alive and logcat contained no VisualCat fatal exception or ANR. |

### 16.4 Package limit and hand-back

The packaging verifier accepted APK structure, manifest, target SDK and local
signature, then correctly rejected the debug certificate because it is not the
configured Google Play upload key. This quick follow-up did not create a new
Wireless-ADB pairing; real pairing/reconnect remains covered by §12.

At hand-back the test installation and data are removed, font scale is restored
to 1.0, automatic rotation with portrait user rotation is restored, Wireless
debugging remains off, device-side test dumps are removed, and the final
corrected Release APK is installed cleanly and opened on the home screen.

---

## 17. Version 2.0.7 production-signed Samsung release smoke

### 17.1 Run header and package gate

| Field | Value |
|---|---|
| Date | 2026-08-23 |
| Repository baseline / candidate tree | `5085b87`; release-metadata, Linux-formatting and deterministic double-tap-test changes under review |
| Device | Samsung Galaxy S21 FE / SM-G990B (`r9q`), Android 16 / API 36, `arm64-v8a` |
| Serial | `RFCRC0A9GND` |
| Display | 1080 × 2340 at density 480 (360 × 780 dp portrait), font scale 1.0 |
| Signed APK | 35,224,959 bytes, SHA-256 `CF69FC8FBFE41955A6C0941241B736E3D7A5B6A8C82242034614A0252BD360DB` |
| Signed AAB | 35,090,117 bytes, SHA-256 `48624DA3C861A2BDB6D00D24D85DE67663AA17BED12FE227E674B8C086B49137` |
| Upload certificate | SHA-1 `37:5C:8D:64:4F:BF:BD:07:DE:4C:1A:71:95:10:6C:94:4B:C6:B8:14` |

The release packager accepted both artifacts as VisualCat 2.0.7. The APK reports
version code 20007, API 31–36, `arm64-v8a` and `x86_64`, APK Signature Scheme v3,
and 188 native libraries aligned for 16 KB pages. The Play/Release manifest
contains `INTERNET`, `CHANGE_WIFI_MULTICAST_STATE`, and AndroidX's package-scoped
dynamic-receiver permission; it contains no `READ_LOGS` declaration. The AAB's
certificate matches the pinned Google Play upload key through both SHA-1 and
SHA-256.

### 17.2 Physical-device results

| Scenario | Result |
|---|---|
| Clean install / launch | **Pass.** The previous installation and data were removed. The production-signed, non-debuggable APK reported 2.0.7 / 20007, cold-started in 1.3 seconds, stayed alive, and logged no fatal exception or ANR. |
| Live scope chooser | **Pass.** The fully framed sheet explained recommended full-device Wireless ADB and immediate VisualCat-only capture, local-only processing, connection shutdown, setup state and restricted-scope behavior without promising a normal `READ_LOGS` grant. |
| Own-app capture | **Pass.** VisualCat-only capture started immediately, showed a truthful own-app notice and quiet-state guidance, received 12 entries, and retained all 12 after Stop. |
| Capture controls | **Pass.** Filters, Plot, Split, Details, Fit, Follow and Stop capture rendered as complete, vertically centred phone touch targets. Entries, timeline, minimap, status and the scroll-owned scope notice remained readable. |
| Stability and cleanup | **Pass.** Stop finalized normally and left the process healthy. The tested app/data were then uninstalled, the same signed APK was installed cleanly, and a second cold launch logged no fatal exception or ANR. |

### 17.3 Scope and hand-back

This final release smoke did not repeat a real Wireless ADB pairing because the
clean uninstall deliberately removed the reusable encrypted identity and no
pairing-code panel was available during the unattended pass. Section 12 remains
the physical transport oracle for real pairing, saved reconnect, interruption
recovery, external records and disconnect; sections 13–16 cover the current
API-36 OEM layouts and pairing validation. This pass adds the exact production
upload-key package, manifest and own-app fallback evidence rather than claiming
another transport pairing.

At hand-back, the Samsung retains its original font scale and rotation settings.
The production-signed 2.0.7 candidate is installed cleanly and open on the home
screen, with no saved pairing identity, capture/session data or granted log
permission.

---

## 18. Production-signed Samsung Wireless ADB endurance continuation

### 18.1 Durable continuation checkpoint

This section is intentionally updated after every material checkpoint so an
interrupted or independent session can resume without reconstructing device
state. Times are Europe/Prague unless explicitly marked UTC.

| Field | Value |
|---|---|
| Continuation start | 2026-08-24 11:46 CEST |
| Repository | `main` at tagged release commit `0c2f332` (`v2.0.7`); clean working tree before this report update |
| Device | Samsung Galaxy S21 FE / SM-G990B, Android 16 / API 36, serial `RFCRC0A9GND` |
| Installed package | `com.barebit.visualcat`, version 2.0.7 / 20007; production-signed release installed cleanly at 2026-08-24 00:17:29 |
| Rotation / power baseline | Automatic rotation enabled, portrait user rotation, device awake |
| Active test inherited from device | Real Wireless ADB full-device capture `Wireless logcat 11h04m26`, started at approximately 11:04:26 |

The active session was already present when this continuation began and is being
preserved rather than restarted. At 11:44:23 its visible UI reported 765 rows in
view, 7,354,220 matching rows, 7,354,613 retained session rows, 8,493,055 source
lines received and a current input rate of 141 lines/s. The production app
process was healthy (`PID 12193`). Its active shell transport used
`logcat -b all -D -T "2026-08-24 08:59:51.347040" -v
threadtime,year,UTC,usec`; at 11:47 the current shell/logcat pair was PIDs
6115/6117 and had been alive for about ten minutes. That younger transport
process is evidence of a connection/process replacement during the still-open
app session, not evidence that the app capture itself began ten minutes ago.

The session title is a start-time label (`11h04m26`), not an elapsed duration.
Consequently this checkpoint claims approximately 42 minutes of app-session
runtime, not eleven hours. The large received count also includes the initial
device-log backlog and must not be divided by wall time as a steady-state rate.

### 18.2 Step ledger (live)

| ID | Step | Status | Evidence / resume instruction |
|---|---|---|---|
| H-01 | Preserve and identify the inherited live session | **Done** | Package, process tree, UI counters, repository state and start-time semantics are recorded in §18.1; graceful stop and the later authorized cleanup are recorded in §§18.9–18.13. |
| H-02 | Validate the corrected Wireless ADB endurance path on the physical Samsung | **Done** | §§18.11–18.13 contain the bounded capture, forced and natural reconnects, five-minute screen-off continuity, foreground notification and clean first-grant proof. |
| H-03 | Gracefully stop and verify final session integrity | **Done** | §18.12 records notification Stop-and-save, the 79,505-entry seal, child/service/notification teardown, tab switching and cold-process persistence with no fatal/ANR. |
| H-04 | Restore and document hand-back state | **Done** | §18.13 records the final full-screen corrected build, granted notification permission, saved fresh pairing, stopped 9,477-entry verification session, no service/child/notification, 95% battery and thermal status 0. Wireless debugging remains on as explicitly disclosed. |
| H-02a | Implement supported Android background execution | **Done — automated and physical pass** | §§18.5–18.13 record the implementation, five-minute screen-off counter advance, notification stop and genuinely first-grant notification repost. |
| H-02b | Prevent merged-buffer cursor regression | **Done — automated and physical pass** | §§18.7 and 18.12 record the monotonic cursor, controlled EOF and later natural recycle; both reconnects used increasing high-water values without replay amplification. |
| H-02c | Make reconnect resume timezone-independent | **Done — automated and physical pass** | §§18.11–18.12 record the controlled epoch/local/UTC comparison and physical replacement commands using six-fraction Unix epoch `-T` arguments. |
| H-02d | Repost the notification after the first runtime grant | **Done — clean-install physical pass** | §18.13 records the exact pre-grant absence, permission callback, service refresh command, immediate shade rendering and notification stop action. |

An attempted fresh UIAutomator dump at 11:47 could not obtain Android's idle
state while the high-volume capture was updating. The already-preserved 11:44
XML is therefore the baseline oracle; this is an automation-observation limit,
not currently classified as an app defect.

### 18.3 Package and resource baseline

The installed `base.apk` was pulled without changing the running application.
It is 35,224,959 bytes with SHA-256
`2D88FBA6EE5185FCCC9BB5A999EBCFB5D5EF105A41EDA309038F517084131903`, an exact
byte match for
`artifacts/release-verification/v2.0.7-0c2f332/public-assets/VisualCat-Android-v2.0.7.apk`.
Android build-tools 36 `apksigner` reports one signer, APK Signature Scheme v3,
certificate SHA-1
`37:5C:8D:64:4F:BF:BD:07:DE:4C:1A:71:95:10:6C:94:4B:C6:B8:14` and certificate
SHA-256
`A7:15:B0:30:95:89:AA:83:DD:21:54:8D:19:59:AF:4B:B9:7B:8D:F0:6D:97:FD:AE:32:71:5F:BD:65:30:E1:84`.
This closes the ambiguity between a production-signed repository artifact and
the package actually exercising the Wireless ADB transport.

At 11:51, with USB power connected, battery was 96%, Android thermal status was
0 and battery temperature was 35.8 °C. The app used approximately 714 MiB PSS
(718 MiB RSS, 65 MiB swap PSS). The detailed breakdown included 212 MiB of
other memory maps and 295 MiB of private-dirty unknown memory; Java heap was
16 MiB PSS and native heap was 23 MiB PSS. This is a high-volume stress case
with about 7.35 million retained rows, so memory is now a monitored endurance
metric rather than being labelled a leak from one sample. Android's exit-info
history contains only the clean self-exit at the 00:17 installation transition,
and current app-PID logs contain no managed fatal exception or ANR. Samsung
surface teardown emitted repeated `BufferQueue has been abandoned` errors; the
process and visible capture remained healthy, so they are recorded for trend
comparison rather than classified as a crash.

### 18.4 Screen-off/background checkpoint and new finding

At 11:50:41 the screen was turned off with Android's sleep key event for a
planned five-minute background leg. The app and Wireless shell/logcat child
started as PIDs 12193/6117 and Android reported `mWakefulness=Dozing`. The screen
was restored at 11:55:53; no Stop, process kill, package mutation or Wireless
debugging toggle was used.

| Minute | App CPU (one-core %) | Transport CPU | App / child PID | Thermal / battery | Result |
|---:|---:|---:|---|---|---|
| 1 | 31.45% | 0.33% | 12193 / 6117 | status 0 / 97% | Initial screen-off/backlog transition; both alive |
| 2 | 0.00% | 0.03% | 12193 / 6117 | status 0 / 97% | Stable doze |
| 3 | 0.00% | unavailable | 12193 / absent | status 0 / 98% | Wireless shell child disappeared |
| 4 | 0.00% | unavailable | 12193 / absent | status 0 / 98% | No transport recovery while dozing |
| 5 | 0.00% | unavailable | 12193 / absent | status 0 / 98% | No transport recovery while dozing |

The app process remained alive, and no fatal exception or ANR was emitted. The
earlier 11:37 logs prove that the bounded 1,024 KiB receive queue can deliberately
recycle the transport and rapidly reauthenticate with the saved identity; the
current child had survived from 11:37:15 until the doze leg. During deeper doze,
however, the child disappeared and there was no Wireless ADB reconnect log before
the app became suspended. This is a reproducible background-lifecycle gap, not a
completed endurance pass.

After wake, Samsung presented its secure swipe/PIN bouncer. The original
unlocked foreground state cannot be restored without user authentication, so
credentials must not be guessed or automated. VisualCat remains alive behind
the lock screen with its saved identity and session data intact; the stopped or
reconnect-pending UI state is not yet observable. Continue with code-level
diagnosis while preserving the device, then obtain an authorized unlock before
installing or repeating the test.

### 18.5 Background-capture remediation checkpoint

**Root cause.** Product comments and UI treated a Live capture as work that could
continue with the screen off, but the Android adapter had no foreground service
or ongoing notification. The capture task, Wireless socket and session writer
all remained owned only by the ordinary app process. Samsung could therefore
suspend the process after lock before the read loop observed the dead shell
stream and ran its otherwise-working reconnect path.

**Implementation now under verification:**

- `PlatformSourceRegistry` exposes one platform background-execution lease for
  Live capture and a typed stop reason. `WorkspaceViewModel.CaptureAsync` acquires
  it synchronously before import begins, releases it on every completion/failure
  path, and routes notification/timeout requests through the existing graceful
  Stop pipeline so received records are drained, sealed and reopened.
- Android supplies `CaptureForegroundService`, an unexported `dataSync`
  foreground service with a low-importance, private, ongoing **VisualCat live
  capture** notification. It contains no log data or device identity and offers
  **Stop and save**. The action changes the notice to **Stopping VisualCat
  capture** while the normal session finalization runs.
- The service is `NotSticky`: Android must not recreate a stale capture notice
  after process loss. API-35+ `OnTimeout` requests graceful stop, removes
  foreground state immediately as Android requires, and gives the final session
  the truthful status **Android's six-hour background limit ended this capture**.
- The manifest adds only the reviewed foreground-service/data-sync permissions
  and optional `POST_NOTIFICATIONS`. The notification permission is requested
  once, only after the reader explicitly starts Live. Denial does not block
  capture or cause repeated prompts; Android still exposes the foreground
  service through Active apps, although the drawer action is unavailable.
- No partial wake lock is used. The foreground service is the platform-supported
  user-visible mechanism for background network access; avoiding an hours-long
  wake lock prevents a disproportionate battery cost and Android-vitals risk.
- The Live scope chooser now discloses the private ongoing notification, screen-
  off behavior, Stop-and-save path, Android's six-hour background limit and the
  guarantee that already-received data is kept. The release permission allowlist
  and AAB service-type gate are updated with the implementation.

Focused Release tests for `CaptureStopTests` and `WirelessAdbSetupTests` pass
19/19. The new regression starts a synthetic Live source, invokes the platform
time-limit callback, verifies the session stops and keeps entries through the
normal pipeline, verifies the six-hour explanation, and proves the background
lease is disposed. The embedded-assembly Android Debug build then succeeded with
0 warnings and 0 errors. Its 79,279,840-byte signed APK has SHA-256
`58C784A43B03D9B638282E337A0F4CBDFCD5014B2367B2AA57F2AFE5EDF26402`; `aapt2`
confirms target API 36, the expected debug-only `READ_LOGS`, all three reviewed
background-capture permissions, and an unexported
`com.barebit.visualcat.CaptureForegroundService` whose foreground-service type
is `dataSync` (`0x1`). Release compilation, the full test suite and physical-
device retest are the next gates; this checkpoint does not yet claim the fix
works on Samsung.

### 18.6 Automated and trimmed-Release gates

The final full Release solution suite passes **406/406**: Domain 11, Core 95,
Application 52 and App 248. `git diff --check` is clean, and `dotnet format
--verify-no-changes` passes for every changed C# file. A whole-solution formatter
run still reports whitespace in Android binding files generated under `obj`; no
generated file was edited or treated as product source.

The final trimmed Release Android build succeeds with 0 warnings and 0 errors.
Its locally signed APK, including both remediations, is 35,380,824 bytes with
SHA-256 `B22ABAB43FCDCD9E713601FCDBC4A468810E272A68EA84BE2A399A99719D62CB`.
`aapt2` confirms version 2.0.7-dev / 20007, minimum API 31, target API 36,
`INTERNET`, `CHANGE_WIFI_MULTICAST_STATE`, `FOREGROUND_SERVICE`,
`FOREGROUND_SERVICE_DATA_SYNC`, `POST_NOTIFICATIONS`, and AndroidX's package-
scoped receiver permission. It contains no `READ_LOGS`, phone-state or storage
permission. The unexported capture service and `dataSync` type survive trimming.

The release packager's explicit allowlist now requires those five product
permissions and rejects a bundle without the unexported named `dataSync`
service. This shell has no `ANDROID_KEYSTORE_*` signing environment, so it cannot
produce the production-upload-key APK needed for an in-place update over the
currently installed production package. The locally signed Release/Debug APKs
must not be installed over it: Android will reject the signature mismatch, and
uninstalling first would destroy the saved pairing identity and inherited
session. Obtain the authorized production signing environment or explicitly
choose a clean uninstall/re-pair test only after the secure device is unlocked.

### 18.7 Wireless resume-cursor replay finding and remediation

The inherited app diagnostics expose a second endurance defect. During the
11:37 backpressure storm the bounded receive queue correctly recycled and
reauthenticated the transport, but successive resume timestamps were not
monotonic: `08:12:22.346901` → `08:08:08.413491` → `08:17:16.890079` →
`08:04:23.217766` → `08:43:32.346446` → `08:11:09.412062` →
`08:59:51.347040`. The implementation used the timestamp on the last line it
saw. A merged `-b all` stream is not guaranteed to be perfectly ordered across
buffers, so moving the cursor backward can replay a large ring-buffer suffix,
fill the queue again, reconnect again, and amplify both row count and memory.
That loop is consistent with the otherwise disproportionate 8.49 million source
lines, 7.35 million retained rows and 714 MiB PSS in the short inherited run.

A separate read-only host sample of the current device collected 60,355 output
lines / 51,487 structurally valid threadtime records. It contained 204 adjacent
timestamp regressions, with a maximum backward step of 5.505 ms. That proves a
last-line cursor is invalid even without the hours-scale stress trace; it also
gives an independent bound for the normal merged-buffer jitter observed on this
Samsung.

The new `LogcatResumeCursor` accepts only a structurally valid year/usec
threadtime record (date/time, optional zone, nonzero PID/TID, priority and tag),
retains the greatest genuine UTC timestamp, and supplies `logcat -T` an inclusive
one-second overlap before that high-water mark. Small cross-buffer reordering can
no longer move the cursor backward, while the bounded overlap deliberately
prefers a small number of duplicate boundary records to unknown loss. A real
device wall-clock rollback is detected independently: if the former high-water
mark is more than five seconds in the device clock's future, the cursor starts a
new clock epoch and emits a diagnostic instead of skipping post-adjustment data.

The Android source now converts at most the documented 96-character ASCII
record prefix on the stack, avoiding a per-line full-message allocation in this
hot path. Focused Release tests for the record reader and resume cursor pass
36/36, covering millisecond reordering, one-second overlap, timestamp-looking
non-record text, invalid dividers/PIDs, and a multi-hour wall-clock rollback.
This is not yet a physical pass; the next build and device run must prove that
resume timestamps stay monotonic and memory settles after a forced queue recycle.

Post-remediation gates are green: the trimmed Release Android build completes
with 0 warnings/errors, the full Release solution passes 406/406, changed-file
format verification passes, and `git diff --check` is clean.

### 18.8 Authorized unlock and pre-update session state

The user unlocked the Samsung normally at approximately 12:19; no credential was
shared or automated. VisualCat returned to the foreground as the same PID 12193.
The Wireless shell/logcat child remained absent, while the product still exposed
**Stop capture** and reported:

`Capturing · 8,511,960 lines received · no source lines for 30m 27s · Wireless debugging full-device logcat`

The visible session contained 7,370,577 retained/matching entries and 928 rows in
the current view. This proves the original build neither lost the already-sealed
segments nor recovered the suspended transport after unlock; it also confirms
the stale **Capturing** state is a real background-lifecycle failure rather than
an automation inability to inspect the locked UI. Battery was 98% on USB,
temperature 32.5 °C and thermal status 0. The next authorized mutation is the
visible in-app **Stop capture** action so the inherited session finalizes before
any package update.

### 18.9 Graceful stop of the inherited session

At approximately 12:21, with the device unlocked and VisualCat visible, the
in-app **Stop capture** button was activated at its current accessibility bounds
`[820,486][1039,594]`. This was the product's normal non-destructive stop path;
the process was not killed and app data was not cleared. Within four seconds the
UI changed to **Stopping…** / **Stopping · 4s · compacting the session**. The
session still exposed all 7,370,577 retained entries, and the Wireless shell /
`logcat` child was absent while the VisualCat process remained alive as PID
12193. Finalization is being monitored before any install or signing decision.

At the 50-second checkpoint the UI still truthfully reported **compacting the
session** with the same 7,370,577 entries. VisualCat remained PID 12193 and
Android's exit history showed no new crash or kill. Compaction temporarily
raised total PSS/RSS to approximately 1.22 GiB with 94 MiB swap PSS, up from the
already-high pre-stop footprint. This is recorded as a stress-path memory cost;
interrupting the normal finalizer would risk the very session this step is
intended to preserve.

The next checkpoint completed successfully. The tab changed from in-progress to
complete and reported **Stopped · 7,370,820 entries kept**; the additional 243
entries were already buffered records drained by the graceful stop. VisualCat
remained alive as PID 12193, no Wireless shell / `logcat` child remained, and
memory settled back to 728,023 KiB PSS / 697,080 KiB RSS with 82,553 KiB swap
PSS. This closes the inherited capture without deleting app data, its saved
Wireless ADB identity, or the resulting complete session.

### 18.10 Update gate requiring owner choice

The repository's supported release path was rechecked after finalization. It
requires `ANDROID_KEYSTORE_PATH`, `ANDROID_KEY_ALIAS` and
`ANDROID_KEYSTORE_PASSWORD` (or equivalent explicit parameters), verifies the
known Google Play upload-certificate SHA-256, and fails closed when signing data
is absent. None of those environment variables is present in this shell. The
installed production APK and the fixed local Release APK therefore have
different signing identities, which Android will not accept as an in-place
update regardless of version code.

Further mutation is deliberately paused at this boundary. The preferred path
is to make the authorized production signing environment available so the fixed
APK can update in place and preserve the just-completed session and saved
pairing. The fallback is an explicitly authorized uninstall followed by install
and Wireless ADB re-pair; that irreversibly clears the app's 7,370,820-entry
session and pairing identity. No uninstall, data clear, sideload attempt,
credential search, commit, push or external release job has been performed.

### 18.11 Authorized clean-install fallback

The owner explicitly authorized deleting VisualCat from the physical device if
needed. This authorizes uninstalling only `com.barebit.visualcat`, with the
understood consequence that Android will irreversibly remove the completed
7,370,820-entry session, app preferences and saved Wireless ADB pairing
identity. It does not authorize deleting unrelated packages or changing device-
wide data. The clean-install path will use the already verified locally signed
Release APK, then establish a new pairing and repeat the physical tests.

The destructive step completed exactly within that scope: `adb uninstall
com.barebit.visualcat` returned `Success`, followed by a successful incremental
install of the 35,380,824-byte fixed APK (SHA-256
`B22ABAB43FCDCD9E713601FCDBC4A468810E272A68EA84BE2A399A99719D62CB`). The
fresh package reports version 2.0.7-dev / 20007, minimum API 31, target API 36,
`firstInstallTime=2026-08-24 12:42:24`, and no installer package. This is a
locally signed engineering candidate, not a claim about Play delivery. The old
session and pairing are no longer recoverable from the device.

Fresh launch resolved to the generated MAUI launcher activity and completed cold
in 1.47 seconds. The first-run portrait UI is visually intact at 1080×2340 / 480
dpi: the app bar and three primary actions fit without clipping, the empty-state
message and severity legend remain centered and legible, system bars do not
overlap content, and the build provenance is visible at the bottom. The
accessibility tree exposes the primary controls and contains no stale recent
session. Android reports both foreground-service permissions granted and the
optional notification permission not yet granted, which is the intended state
before the reader explicitly starts Live.

Opening Live presents the revised scope chooser without triggering a permission
prompt prematurely. At this device size the tall disclosure dialog fits between
the system bars, both radio choices and their consequences are readable, and
**Cancel** / **Set up full-device** remain visible without scrolling. Full-
device capture is selected by default and labelled recommended; the copy clearly
states local-only use, Wireless-debugging persistence, the ongoing notification,
Stop-and-save behavior, the Android six-hour limit and retained partial data.

Selecting **Set up full-device** opens the fresh pairing form and still does not
request notification access before capture exists. Its accessibility tree
exposes the port and six-digit code fields, and the copy states that the code is
not saved or logged while the reusable identity is protected by Android
Keystore. One physical UX issue was found: **Open Developer options** reaches
Samsung's general Developer options page at its top, leaving Wireless debugging
far down a long OEM settings list. The label is technically accurate but the
extra search/scroll burden is avoidable; implementation is paused before pairing
to route this control directly to Android's Wireless debugging settings action
with a compatible fallback.

The route is now implemented as a three-level Android fallback: first
`android.settings.WIRELESS_DEBUGGING_SETTINGS`, then Developer options when the
OEM does not expose or allow the dedicated activity, then general Settings as a
last resort. The control and accessibility help now say **Open Wireless
debugging**, while post-stop **Open settings** uses the same route. Support copy
and test-plan W1 were updated. A new headless UX regression proves the dedicated
action is exposed and invoked; the focused setup/background-stop suite passes
20/20. A rebuilt APK and physical recheck are still required before this is
marked passed.

The first Android compile of this incremental UX change failed before packaging
with two `CS0103` errors because the fallback filter used an unqualified
`SecurityException`. The intended platform exception is
`Java.Lang.SecurityException`; both filters now name it explicitly. No failed-
build artifact was installed, and the Android gate is being rerun from that
correction.

The corrected trimmed Release Android build then succeeded in 46.71 seconds
with 0 warnings and 0 errors. Its newly signed APK is 33,827,085 bytes with
SHA-256 `8555C275E171C8465A4EECD1183EBDB9197547ECA1A38C9CEBDFBA539635B192`.
An in-place `adb install -r` over the fresh engineering install returned
`Success`; `firstInstallTime` remained 12:42:24 and `lastUpdateTime` advanced to
12:47:36, confirming data-preserving update semantics under the same local
signing identity. The dedicated-settings route now needs a physical pass.

That first physical recheck showed Samsung registers no activity for the
unpublished `android.settings.WIRELESS_DEBUGGING_SETTINGS` action, so the new
fallback still reached the unpositioned Developer options page. The candidate
was not accepted as a pass. A read-only package query confirmed **No activities
found**, and Android's public `Settings` API exposes Developer options but no
Wireless-debugging activity action. AOSP Settings instead supports the stable
preference key `toggle_adb_wireless` and the best-effort highlight argument
`:settings:fragment_args_key`. Launching the public Developer-options action with
that hint was tested independently on this Samsung: the page opened scrolled to
the **Bezdrátové ladění** (Wireless debugging) row. The implementation now uses
that official activity plus the non-destructive AOSP hint; OEMs that ignore the
hint still get the existing usable Developer-options page, and devices without
that activity fall back to general Settings. A final rebuild/retest is pending.

The revised focused route passes on the physical Samsung. The focused tests again
pass 20/20, the trimmed Android build succeeds with 0 warnings/errors, and the
35,380,824-byte APK (SHA-256
`1DA92C469D4B5FAB2A3516B8DAE4E062DA31C1DC3B151156A804DBECDC41F30A`) updated
in place successfully. Starting from VisualCat's fresh home, automation followed
Live → Set up full-device → Open Wireless debugging. Samsung opened Developer
options with the full Wireless debugging row visible at the bottom of the first
viewport—no search or manual scroll. Its localized switch was exposed as a
clickable, unchecked accessibility node. This closes the shortcut finding on
this device while preserving an honest fallback for other OEMs.

Wireless debugging was enabled through its unchecked localized accessibility
switch. Android still retained three obsolete **VisualCat** public keys from
earlier app identities even though the corresponding private keys were removed
by uninstall; Settings owns that list independently. Each stale VisualCat entry
was opened by its exact row bounds and forgotten, with the list refreshed after
each removal. The final count was zero VisualCat entries while the unrelated
`marek@BENNY-WORKSTATI…` workstation pairing remained exactly one.

Samsung split screen was then established with Wireless debugging above and
VisualCat's setup sheet below, as the product instructions recommend. The layout
itself remains usable at half height: the form scrolls, its two fields can be
positioned together, and the fixed Cancel / Pair & connect footer remains
reachable. A platform/tooling limitation was observed: at this reduced Avalonia
window height Android UI Automator exposes only VisualCat's native root, not the
managed control descendants. This prevents accessibility-coordinate automation
of the fields in split screen even though they render and accept focus; it does
not reproduce in full-screen setup and is recorded for later accessibility
coverage rather than misreported as a product pairing failure.

A fresh Android pairing panel was kept open in the upper pane. Its ephemeral
port/code were read only into process memory, never printed, logged or saved to
the repository; the transient device hierarchy file was removed immediately
after each read and expired attempts were discarded. The values were entered in
the visible lower-pane fields and **Pair & connect** was activated. Pairing and
connection succeeded: Android immediately presented VisualCat's first explicit
`POST_NOTIFICATIONS` runtime request, which can only occur after the setup sheet
returns success and reader-started Live begins acquiring its foreground-service
lease. The permission decision and service/capture verification are next.

The localized **Allow** action was selected. Android now reports
`POST_NOTIFICATIONS: granted=true` with `USER_SET`, and no second prompt
occurred. `dumpsys activity services` shows the unexported
`CaptureForegroundService` created from top/visible state, `isForeground=true`,
foreground ID 4108, service type `0x1` (`dataSync`), `startRequested=true`, and a
private, silent, ongoing notification with exactly one action. The app process
is PID 10366 and a real `logcat -b all -D -T ... -v
threadtime,year,UTC,usec` child is running. This is the first physical proof that
the fixed build starts its Android foreground lease for the real Wireless ADB
source; notification content/action, full-screen UI, counters, memory and
background survival remain separate gates.

The first real capture is **not a pass**. Within minutes the UI reached
3,663,939 source lines / roughly 2.54 million visible matches at 49,620 lines/s,
then 6,363,681 source lines / roughly 4.24 million visible matches. PSS had
already risen to 732,197 KiB. Diagnostics show the bounded 1 MiB queue recycling
every 1–5 seconds and every authenticated reconnect reopening from the identical
`2026-08-24 11:01:57.410479 UTC` cursor. The monotonic high-water remediation is
working as written, but cannot advance because each replay fills the queue
before the stream reaches a newer record.

Device clocks explain the loop: at the checkpoint Samsung reported local
`2026-08-24T13:03:57+0200` and UTC `2026-08-24T11:03:57+0000`. VisualCat emits
records with the `UTC` logcat presentation modifier and passed the bare UTC
wall-clock text `11:01:57` back to `logcat -T`. Android's `-T` parser interprets
that zone-less wall-clock form in local time, placing the restart roughly two
hours behind the intended instant. `logcat --help` also exposes a numeric epoch
seconds form (`sssss.mmm...`), which is timezone-independent. The next cursor
revision must use that form and retain human-readable UTC only for diagnostics.

The permitted notification did not appear in Samsung's shade after permission
was granted: the foreground service had already posted while permission was
undecided, and NotificationManager recorded no posted VisualCat notification.
The service retained its internal foreground notification object, but Android
did not automatically repost it after the grant. Consequently the intended
notification Stop action could not be exercised in this run. Stop was activated
from the visible in-app button instead; the first split-pane tap only focused
VisualCat, and the second stopped the Wireless child. Normal finalization and
service removal are being monitored before rebuilding. Permission must be
requested before the first foreground notification, or the service notification
must be explicitly reposted after a grant.

At the 58-second stop checkpoint the UI truthfully reported **saving the last of
the capture**, the Wireless child remained absent, and the retained view had
grown to 5,457,065 replay-amplified entries. Finalization again temporarily
raised memory to 1,220,687 KiB PSS / 1,333,264 KiB RSS with 31,730 KiB swap PSS.
The foreground service correctly remains present during this graceful save; it
must disappear only after the session seal completes.

Finalization completed without a crash: **Stopped · 5,757,926 entries kept**.
The foreground service and every Wireless shell/`logcat` child disappeared only
after sealing, the process remained alive, Android exit history contained only
the two expected package-update exits, and memory settled to 528,455 KiB PSS /
547,800 KiB RSS with 35,070 KiB swap PSS. The post-stop notice correctly states
that VisualCat closed its connection/discarded decrypted key material while
Android leaves Wireless debugging enabled. This validates graceful failure
containment, but the session contents are intentionally classified as replay-
amplified evidence rather than a successful capture.

A controlled read-only `logcat -d` comparison on the stopped device confirms
the timezone diagnosis from one instant. `-T 1787569728.000000` (epoch) returned
42 lines; the equivalent bare UTC text `-T '2026-08-24 11:08:48.000000'`
returned 271,572 lines; the equivalent Prague local text `-T '2026-08-24
13:08:48.000000'` returned 110 lines. The small epoch/local difference is normal
traffic produced while commands ran. The five-order-of-magnitude UTC-text
increase proves Android parses zone-less `-T` wall time locally and that epoch
seconds are the appropriate invariant resume argument.

The second cursor revision now separates command data from diagnostics. The
inclusive one-second overlap is serialized as Unix epoch seconds with exactly
six fractional digits for `logcat -T`; a separate UTC wall-clock string is used
only in logs. The Wireless service validates the epoch argument as digits, one
decimal point and six fractional digits before placing it in the still-fixed
shell destination. Tests assert both representations across reordering,
overlap, invalid records and a real wall-clock rollback; the focused Core suite
passes 7/7. Focused setup/background-stop tests remain green at 20/20.

The first-grant notification path now sends an internal refresh command only
when Android's permission callback reports `Granted` and an active capture lease
still exists. The non-exported service handles that command by rebuilding the
same private ongoing notification from its in-memory summary. Denial remains
non-blocking and is not re-prompted; stale grants cannot create a notification
without an active lease. Android compilation and a physical in-place update are
the next gates.

The corrected Android build passes trimming/AOT in 63.57 seconds with 0 warnings
and 0 errors. Its APK is 33,827,085 bytes with SHA-256
`EFE3F762F399C4702DBF85743E9A8040780A81FC81B464030F631F8BCFCE1CF1`.
The in-place physical update returned `Success`; first-install time remained
12:42:24, last-update time advanced to 13:13:39, the saved pairing/session data
were preserved, and notification permission remains granted. A new capture can
therefore test the epoch fix and immediately visible notification without
another pairing or permission prompt.

### 18.12 Corrected-build launch state

Samsung retained the Settings/VisualCat split container after its combined
recents card was dismissed. The app was not capturing, so the exact VisualCat
package was force-stopped to finish only its activities/process; no app data,
pairing identity, Android Wireless-debugging authorization or retained session
was cleared. Relaunching the manifest-resolved activity with fullscreen
windowing created fresh task 2670 in `mode=fullscreen` and cold-started PID
16002. The stale empty split stages remain invisible system bookkeeping and do
not own the app task.

The full-screen accessibility tree is restored and exposes the toolbar, saved
session tab and actionable controls. The replay-amplified evidence session is
still present and truthfully reports **Ready · 5,757,926 entries**; it has not
been deleted or reused as a success result. The saved pairing and granted
notification permission remain available for the next clean Live capture.

The corrected chooser then passed its physical UX checkpoint: it opened
full-screen, selected **Full-device capture** by default, labelled it
**Recommended · already paired**, retained the local-only/screen-off/six-hour
disclosure, and exposed a large **Connect full-device** action. Selecting it
created a distinct in-progress session titled `Wireless logcat 13h18m37`; the
5,757,926-entry evidence session remained a separate complete tab.

The first bounded acceptance window is a pass. At roughly eight seconds the new
session contained 806 entries / 941 received lines at 94/s and spanned only
13:18:38–13:18:46. After about two minutes it showed 9,296 matching entries /
10,781 received lines at 52/s with current 13:20 records. This is ordinary
device traffic, not the prior millions-of-lines replay: there is one app
process (PID 16002), one capture `logcat` child (PID 16144), no queue-recycle
storm and no second capture child. PSS was 512,454 KiB / RSS 625,572 KiB / swap
PSS 199 KiB; most of that process baseline is the still-open 5.76-million-entry
evidence session, so the bounded new-session growth is the meaningful result.

Android NotificationManager now has exactly one VisualCat record, ID 4108, on
the `visualcat-live-capture` channel. It is private, silent, ongoing/no-clear,
uses the foreground-service flag and has exactly one **Stop and save** action.
Samsung's notification shade visibly renders the VisualCat icon, title
**VisualCat live capture** and concise text **Full-device logs are being saved
locally.** at the top of the notification list. This closes the previously
missing-notification regression for the already-granted path; the action's
expanded rendering and behavior remain to be exercised after reconnect and
screen-off validation.

Expanding that card also passes: Samsung renders the app name/time separately,
keeps the title and local-only scope readable, and presents **Stop and save** as
a full-width 966-by-108-pixel accessible button (`clickable=true`, content
description `Stop and save`). The action has not yet been pressed, because the
same session must first prove reconnect and screen-off continuity.

The epoch reconnect gate is a pass. With the app in the foreground, the exact
capture command PID 16144 was terminated once to simulate transport EOF. The
reader classified one `TransportClosed` gap, retained a high-water cursor of
`2026-08-24 11:22:57.915898 UTC`, waited 250 ms, reused the saved identity,
discovered/authenticated successfully on attempt 1 and opened the replacement
stream from epoch `1787570577.915898`. The new device command was visibly
`logcat -b all -D -T 1787570577.915898 -v threadtime,year,UTC,usec`; it appeared
within the first one-second poll as PID 16596 and remained the only capture
child more than 25 seconds later.

At 13:24 the UI showed current 13:23–13:24 records, 22,011 entries in the clean
session, 25,302 received lines and a settled 17/s rate. PSS was 533,642 KiB /
RSS 646,132 KiB / swap PSS 192 KiB. The foreground service remained type
`dataSync`, NotificationManager retained exactly one VisualCat notification,
and diagnostics contained exactly the expected one-gap/one-reconnect sequence—
no old wall-clock `-T`, no two-hour replay, no bounded-queue recycle and no
rapid reconnect loop. This physically validates both the monotonic high-water
and timezone-independent serialization fixes.

The repeat screen-off leg began at local `2026-08-24T13:24:55+0200`
(`11:24:55Z`). USB power remained connected, battery was 96%, battery
temperature 33.2 °C and thermal status 0. The pre-sleep process pair was app PID
16002 / resumed `logcat` PID 16596; Android reported `mWakefulness=Dozing` two
seconds after the sleep key. The UI baseline immediately before this leg was
25,302 received lines.

At minute 1 (`13:26:10`) Android was still dozing. App PID 16002 and the same
epoch-resumed child PID 16596 were alive; a point-in-time `top` sample showed
11.5% / 3.8% of one core. The service remained foreground/start-requested and
the sole ID-4108 notification remained posted. PSS was 505,794 KiB / RSS
611,560 KiB / swap PSS 192 KiB, battery remained 96% at 32.9 °C and thermal
status remained 0. This is a pass; no reconnect or suspension was observed.

At minute 2 (`13:27:15`) the device remained dozing with the identical
16002/16596 process pair, foreground service and one notification. Instant CPU
was 3.8% / 0.0%; PSS declined again to 489,486 KiB / RSS 591,316 KiB / swap PSS
192 KiB. Battery was 96% at 32.8 °C and thermal status 0. This is a pass and,
unlike the original screen-off run, the transport is still present beyond its
previous disappearance window.

At minute 3 (`13:28:03`) the device remained dozing and the same process pair,
foreground service and one notification were intact. Instant CPU was 3.8% /
0.0%; PSS/RSS settled to 477,050/571,284 KiB with 183 KiB swap PSS. Battery was
96% at 32.8 °C and thermal status 0. This is a pass.

At minute 4 (`13:29:06`) the device remained dozing with PIDs 16002/16596,
foreground service and sole notification unchanged. Instant CPU was 3.8% /
0.0%; PSS/RSS were 472,649/566,728 KiB with 183 KiB swap PSS. Battery was 96%
at 32.8 °C and thermal status 0. This is a pass.

At minute 5 (`13:30:08`) Android was still dozing with the same PIDs 16002 /
16596, foreground service and sole notification. Instant CPU was 7.6% / 0.0%;
PSS/RSS had settled further to 463,670/557,692 KiB with 183 KiB swap PSS.
Battery remained 96% at 32.8 °C and thermal status 0. The device was woken at
13:30:23; it became `Awake`, retained the app as the focused underlying
activity and retained the identical process pair. Samsung is now at its secure
lock screen, so post-wake counter and notification-action checks require an
owner unlock. The complete five-minute background leg is a service/transport
survival pass.

While the phone awaited owner unlock, the final solution regression gate was
rerun against the repository's actual `VisualCat.slnx`: **407/407 passed** in
Release with no restore (Domain 11, Core 95, App 249, Application 52). An
initial invocation using the obsolete/nonexistent filename `VisualCat.sln`
failed before build or test discovery and is not a product/test failure; the
correct `.slnx` run completed with exit code 0.

`git diff --check` is clean. Whole-solution `dotnet format` again reports only
the known whitespace emitted into
`VisualCat.Android.AdbBinding/obj/.../__NamespaceMapping__.cs`; that generated,
ignored output was not edited. Five source-scoped formatter gates covering
every changed C# file in Android, App, Core and both affected test projects all
pass `--verify-no-changes --no-restore` with exit code 0.

At the 15-minute service-health checkpoint the corrected session still had app
PID 16002 and the same post-test reconnect child PID 16596. The dataSync service
remained foreground/start-requested and Android's bounded 15-minute log window
contained no VisualCat fatal exception, ANR or service timeout. Exit history
contains only three package-update exits plus the explicitly documented
13:16:59 remove-task/force-stop used to escape Samsung's stale split task; there
is no crash or low-memory exit.

The owner unlocked the device at approximately 13:34 without exposing or
automating credentials. VisualCat returned as the focused full-screen activity
with the identical app/capture PIDs, foreground service and notification. The
UI showed current 13:34 records, 74,281 entries in the clean session and 83,325
received lines at 18/s. Relative to the 25,302-line pre-sleep baseline, at least
58,023 additional lines were received while the screen-off/locked interval and
post-wake wait elapsed. This closes the background capture continuity gate; it
is not merely process survival.

The expanded notification's exact **Stop and save** button was pressed at
`13:36:02+0200`. Android immediately replaced the action-bearing card with an
action-free ongoing state titled **Stopping VisualCat capture** and text
**Saving received logs and finalizing the session…**. Diagnostics confirm the
notification pending intent—not the in-app button—requested stop. VisualCat
closed Wireless ADB/discarded decrypted key material, removed the child and
then stopped the foreground service/ongoing notification at 13:36:03. The shade
visibly contained no VisualCat card afterward.

The app remained alive and returned to its normal **Live** toolbar state. The
session sealed as **Stopped · 79,505 entries kept**, remained selected/readable,
and displayed the correct post-stop explanation that VisualCat closed its
connection while Android leaves Wireless debugging enabled, with **Open
settings** and **Dismiss** actions. PSS settled to 350,051 KiB / RSS 374,160 KiB
with 67,348 KiB swap PSS; the child and service were absent and exit history
still had no crash/ANR/low-memory event.

The full corrected run contained exactly two authenticated reconnects: the
intentional forced-EOF gate at 13:22 and one natural bounded-queue recycle at
13:35 after roughly 12 minutes. Both succeeded on attempt 1 and used increasing
epoch cursors (`1787570577.915898`, then `1787571312.551789`). There was no
reconnect storm, no partial-record loss reported, no replay-amplified count and
no fatal exception or ANR in the 30-minute bounded diagnostics window.

Tab re-openability also passes without mutating either session: selecting the
older tab restored **Ready · 5,757,926 entries**, and selecting the corrected
tab again restored **Stopped · 79,505 entries kept**. The corrected tab is the
selected hand-back view. A cold-process persistence check is next now that no
capture/service/notification is active.

Cold-process persistence passes. With no active capture, the exact package was
force-stopped once at 13:39:39 and relaunched from its manifest activity as a
new fullscreen process (PID 19995). Both complete session tabs returned from
disk, the corrected tab remained selected, its histogram/rows rendered and the
count restored exactly as **Ready · 79,505 entries**. The status appropriately
normalizes from the transient post-stop wording to `Ready` after reload. No
`logcat` child, capture service or active VisualCat notification was recreated.
The cold-loaded process used 544,147 KiB PSS / 639,776 KiB RSS / 343 KiB swap
PSS while both the 5.76-million-entry stress artifact and corrected 79,505-entry
session were open. Exit history labels the preceding termination
`USER REQUESTED / FORCE STOP`, not a crash.

### 18.13 Clean first-grant notification gate

The permission-refresh fix cannot be honestly exercised by merely revoking the
permission in place. The test revoked only `POST_NOTIFICATIONS` and cleared the
OS decision flags without touching app data. The saved-pairing capture started,
the foreground service ran and NotificationManager correctly had no visible
record, but VisualCat did not nag again because the app-private
`capture-notification-requested` one-shot flag records that this installation
already answered once. This is intended denial/revocation behavior, not a
callback failure.

That 13:42 probe was stopped from the in-app action; its child and service were
both absent at the first one-second poll. The owner explicitly authorized app
deletion if useful, and all preceding session evidence is now captured in this
report/screenshots. The next step is therefore an authorized uninstall/reinstall
of the same corrected APK, fresh pairing and genuinely first permission grant.
This will erase the device copies of the 5,757,926-entry stress session, the
79,505-entry corrected session, the short permission probe and the saved app
identity; Android's independent Wireless-debugging authorization list will be
inspected and cleaned where Samsung accepts its exact Forget action.

The same 33,827,085-byte corrected APK was clean-installed successfully at
13:43:21 (version 2.0.7-dev / 20007); `POST_NOTIFICATIONS` began ungranted and
the app-private sessions/identity were absent as expected. The fresh setup again
proved the Wireless-debugging shortcut lands with Samsung's localized row
visible. Samsung continued to list one old VisualCat public key even after its
exact **Forget** action was invoked twice and the page was fully exited/reopened;
that authorization is OS-owned and did not block a fresh cryptographic pairing.

Split screen was re-established with VisualCat's responsive setup sheet above
Wireless debugging. The two fields and fixed footer were simultaneously
visible after one scroll. Android's pairing-code panel was parsed only in
process memory: the report records only that a six-digit code and five-digit
port had valid shapes. The transient `/sdcard/pair.xml` was deleted before the
values were submitted, and neither value was printed, logged or saved in the
repository. Fresh pairing succeeded and Android displayed the genuinely first
notification-permission prompt.

Immediately before the grant, the capture was real (`logcat -T 1`), the dataSync
foreground service was active with start ID 1, permission was false and
NotificationManager had no VisualCat record—the exact previously failing
state. Selecting the localized **Allow** changed permission to granted and the
same service to start ID 2. Logs then recorded **Requested a foreground-
notification repost after notification permission was granted** followed by
**Reposting the active capture notification after notification permission was
granted** five milliseconds later.

NotificationManager immediately contained exactly the intended ID-4108 private,
silent, ongoing foreground record with title **VisualCat live capture**, local-
only full-device text and one **Stop and save** action. Samsung's shade visibly
rendered it at the top of the list at 13:51. Expanding that fresh-grant card
exposed a clickable 966-by-108-pixel Stop action; selecting it removed the
transport and service by the first one-second poll, left permission granted and
left no active VisualCat notification record. The first-grant repost regression
is therefore physically closed, not merely unit-tested.

Final hand-back verification passed after another cold force-stop/fullscreen
relaunch. VisualCat is installed from the corrected APK and opens to one saved
verification session, **Wireless logcat 13h50m19**, restored as **Ready · 9,477
entries** with its rows and histogram visible. The source chooser independently
reports **Recommended · already paired** and offers **Connect full-device**, so
the newly generated app identity survived the cold restart; the chooser was
then cancelled without starting another capture.

The device is being returned with VisualCat foregrounded, notification
permission granted, no active capture child, no running capture service and no
active VisualCat notification. Battery was 95%, temperature 32.7 °C and Android
thermal status 0 at the final poll. Wireless debugging intentionally remains
enabled, matching the product's post-stop disclosure. Samsung may still show
the older OS-owned VisualCat authorization that its **Forget** action refused
to remove alongside the fresh working authorization; this does not affect the
app-owned saved pairing or the completed tests. All transient pairing XML files
were removed from `/sdcard` and no pairing code or port was retained.

The installed package was pulled back to
`artifacts/live-test/continuation-20260824/final-installed-base.apk`: it is
33,827,085 bytes and has SHA-256
`EFE3F762F399C4702DBF85743E9A8040780A81FC81B464030F631F8BCFCE1CF1`, an exact
match for the locally built signed APK. The final visual hand-back capture is
`artifacts/live-test/continuation-20260824/final-handback.png`. This leaves the
device and repository in a deterministic state from which an independent
session can continue without repeating any completed gate.

A final redacted-only evidence audit found zero exact six-digit, five-digit or
IP-and-port credential-like attributes in the intentionally retained
`recents-split-pair.xml` UI-hierarchy capture. Together with the device-side
absence checks above, this confirms that the fresh pairing secret was not
retained in either the evidence folder or the two transient `/sdcard` paths.

Final repository documentation gate: the host's signature policy rejected the
first direct invocation before the checker ran. Re-running the same repository
script with a process-scoped `-ExecutionPolicy Bypass` (without changing system
policy) passed: 95 relative links across 43 Markdown files, all required files
and version metadata are consistent. The subsequent `git diff --check` also
exits 0.

---

## 19. Motorola independent continuation — current Android lifecycle build

### 19.1 Durable continuation checkpoint

This section is updated after every material checkpoint. It is the authoritative
resume point for the currently connected device; an independent session should
read the step ledger below before repeating or changing device state. Times are
Europe/Prague unless explicitly marked UTC.

| Field | Value |
|---|---|
| Continuation start | 2026-08-24, afternoon CEST |
| Repository | `main` at `16ed98e` (`Harden Android live capture lifecycle`); clean working tree before this report update |
| Device | Motorola Edge 60 Pro (`cybert`), Android 16 / API 36, serial `ZY22M4T2Z4`, `arm64-v8a` |
| Display | 1220 × 2712 at density 450 (approximately 434 × 964 dp), system font scale 1.0 |
| Power / thermal baseline | USB powered, battery 100%, 27.7 °C, `Thermal Status: 0` |
| Inherited package | `com.barebit.visualcat` 2.0.6 / 20006, installed 2026-08-23 22:26:41; app foregrounded and healthy |
| Authorization | The owner explicitly authorized updating and, if useful, deleting the installed app. A clean install is therefore permitted after preserving the baseline. |

The complete §5 remediation ledger is already `Done`, including physical-device
proof. Sections 12–18 also close the subsequent Wireless ADB, responsive-layout,
foreground-service, reconnect-cursor, epoch-serialization and first-notification-
grant work. This pass does not reopen completed findings without contrary
evidence. Its purpose is to update the stale Motorola installation to the current
tree, re-audit the highest-risk mobile interactions with fresh eyes, and close
the Motorola real-Wireless-ADB coverage limit declared in §14 if the device
permits a secure fresh pairing.

### 19.2 Step ledger (live)

| ID | Step | Status | Evidence / resume instruction |
|---|---|---|---|
| M-01 | Audit the report, repository and connected-device baseline | **Done** | §19.1 records the clean tree, exact commit, old installed version, device/display/power state and the already-completed remediation scope. Preserve the pre-update evidence before uninstalling. |
| M-02 | Preserve the inherited 2.0.6 visual/package oracle | **Done** | `m01-before-update.{png,xml}`, `m01-before-package.txt`, `m01-before-process.txt` and `m01-before-logcat.txt` are under `artifacts/live-test/motorola-continuation-20260824/`; details are in §19.3. |
| M-03 | Build the current optimized Release and run pre-install gates | **Done** | §19.4: optimized build 0 warnings/errors; 407/407 Release tests; APK identity, permissions, ABI and v3 signature verified; immutable copy/hash preserved. |
| M-04 | Clean-install the current package on the Motorola | **Done** | §19.5: exact old package removed, absence confirmed, M-03 APK installed, version/flags/permissions verified and 1.698 s cold launch passed with no fatal/ANR. |
| M-05 | Re-audit responsive UX/UI and accessibility | **Done** | §19.6 records passing home/sheet/IME/touch checks at 1.0×/1.3× and portrait/landscape. §19.7 adds active-capture portrait/landscape plus Plot/Split/Details workspace proof. Entry inspector/copy are already device-closed in earlier sections and were not reopened by contrary evidence. |
| M-06 | Exercise capture lifecycle and Motorola-only Wireless ADB gap | **Done** | §§19.6–19.8 prove reusable pairing, real Release full-device capture, permission/service/notification state, single ownership, forced reconnect, 125-second screen-off continuity, notification **Stop and save**, graceful teardown and cold persistence. |
| M-07 | Implement and regress every defect found in M-05/M-06 | **Done** | F-43 was the only current-tree product finding. The implementation and 409/409 suite pass, and §19.8 physically verifies the final copy on Motorola API 36. |
| M-08 | Final regression and deterministic hand-back | **Done** | §§19.7–19.8 record repository gates, exact installed artifact/hash, sensitive-evidence cleanup, restored settings, two persisted sessions and zero active reader/service/notification. |

### 19.3 Inherited-package oracle

M-02 completed before any destructive device action. The accessibility tree and
full-device screenshot show a retained 303,145-entry Wireless session on 2.0.6.
Its finalized data is readable; the current status is `Reading · 303,145 entries
ready`, while a separate, fully visible application notice truthfully says that
Wireless debugging disconnected repeatedly and that the already-written capture
was kept. Toolbar, tab, workspace switches, equal-width Time/Copy/Entry row,
loaded entries, `Load 10 more`, session status and the notice's 48 dp-or-larger
Dismiss target are separate and inside the 434 dp-wide portrait viewport.

The process was foregrounded and alive, and the bounded log contained no current
fatal exception or ANR. This is a useful visual/state oracle, not a failure of the
current tree: the installed package is 2.0.6 whereas all §18 lifecycle changes
are in the repository's later 2.0.7-dev source. The evidence is now durable, so
clean removal of only `com.barebit.visualcat` is safe once M-03 produces the
replacement APK.

### 19.4 Current-build gate

`dotnet build src/VisualCat.Android/VisualCat.Android.csproj -c Release
--no-restore` completed in 55.18 seconds with 0 warnings and 0 errors, including
the trim/AOT pass. `dotnet test VisualCat.slnx -c Release --no-restore` then
passed **407/407** tests: Domain 11, Core 95, App 249 and Application 52.

Android build-tools 36 independently report package `com.barebit.visualcat`,
version `2.0.7-dev` / 20007, target SDK 36, and `arm64-v8a` plus `x86_64` native
code. The manifest requests exactly `INTERNET`, `CHANGE_WIFI_MULTICAST_STATE`,
`FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_DATA_SYNC`, `POST_NOTIFICATIONS`, and
AndroidX's package-scoped non-exported receiver permission; it does not request
`READ_LOGS`, phone state, or storage. `apksigner` verifies one APK Signature
Scheme v3 signer. It is the expected local Android debug certificate, not a
claim about the production upload key.

The exact install candidate is preserved as
`artifacts/live-test/motorola-continuation-20260824/m03-current-release-signed.apk`:
35,384,920 bytes, SHA-256
`A601FA737E23FD6309B557B21B772FCB238C98BF208B627A3CEF74061E176F4C`.

### 19.5 Authorized clean replacement

Immediately before deletion, `pm path` resolved the exact inherited target as
`com.barebit.visualcat` and its only app process was PID 11047; no active capture
service was reported. `adb uninstall com.barebit.visualcat` returned `Success`,
and a second `pm path` proved the package absent. This erased only that app's
private 2.0.6 data, including the preserved-on-host 303,145-entry session.

Installing the exact M-03 APK then returned `Success`. The device reports
2.0.7-dev / 20007, target SDK 36, `HAS_CODE` and `ALLOW_CLEAR_USER_DATA`, with no
debuggable flag. First/last install time is 2026-08-24 17:00:32. The manifest-
resolved activity cold-launched in 1.698 seconds as PID 17538. Android grants the
normal and foreground-service install permissions while `POST_NOTIFICATIONS`
is correctly false before the first runtime prompt. The clean bounded launch log
contains no VisualCat fatal exception, unhandled exception or ANR.

### 19.6 Responsive sweep and F-43 first-pairing finding

The clean home at 1.0× text is visually balanced and the accessibility tree has
exactly six clickable nodes. Every one is at least 48.0 dp tall; the three hero
actions measure 71.8–117.7 dp wide by 48.0 dp high. The 3 × 2 severity legend,
2 + 1 actions, complete headline and build provenance are separate and readable.

The scope and setup sheets pass in portrait and in the 341 dp-high landscape
viewport at 1.3× Android text. The sheet keeps its four-sided rounded frame and
pinned 48 dp footer; both scope choices are reachable after an internal scroll.
The pairing form exposes both 48 dp numeric fields and its privacy note. With the
599 px landscape IME visible, the focused port field remains wholly above the
keyboard. Submitting port `0` reveals the complete inline `1 to 65535` guidance
beside that field and leaves the footer reachable. Evidence is under
`artifacts/live-test/motorola-continuation-20260824/m05-*`.

The product's **Open Wireless debugging** route also passes on Motorola: it opens
Developer options with the exact Wireless debugging row in the first viewport.
The switch and pairing page are reachable, and the setup sheet remains usable in
a 434 × 477 dp split pane. A dry run proved the port/code fields, numeric IME,
Next/TAB focus order and fixed **Pair & connect** action all work in that pane.

#### F-43 · Pairing help implies split screen preserves Android's code on every OEM

**Status: Done — reproduced twice, failing contract now passes, and the final
Release copy is physically verified on Motorola API 36.**

On this Motorola API-36 build, Android's pairing-code panel and VisualCat can be
shown simultaneously in split screen, but focusing/submitting from the other
pane causes Android to reject the short-lived pairing socket. Two fresh,
shape-validated in-memory attempts ended in Android's own **Pairing
unsuccessful** dialog. VisualCat created no saved identity, foreground service,
capture or notification-permission decision. The fields and LibADB call were
reached; this is not field validation, clipping, network mismatch or a stale app
install. Section 12 records the same foreground-panel restriction on Pixel/API
34 and required a temporary test harness, while Samsung/API 36 accepts the split
flow. The behavior is therefore an OEM/platform constraint, not something an
ordinary app can force open.

The product defect is narrower but real: step 3 currently says to use split
screen when Android closes the panel, without warning that some Android builds
also invalidate the code when the other pane takes focus. The robust UX fix is
to keep split screen as the first recovery, explicitly say a new code is required
after **Pairing unsuccessful**, and offer the immediate VisualCat-only capture
fallback when the OEM repeats the cancellation. This does not pretend the app
can override Android's pairing service. The low-level transport will be tested
with a temporary DEBUG-only bridge while Settings retains focus; that bridge is
test infrastructure and must be absent from the final source, manifest and APK.

The contract `SetupExplainsWhenAnOemCancelsPairingEvenInSplitScreen` was added
first and failed against the previous copy because **Pairing unsuccessful** was
absent. The setup now keeps split screen as the first recovery but says to pair
before expiry, generate one fresh code after Android's rejection, and fall back
to VisualCat-only capture if the OEM repeats it. The Android service's two
pairing-failure results carry the same guidance, so the reader sees it after a
real failed attempt as well as before one. `docs/SUPPORT.md`, W1 in the Android
live-test plan and `[Unreleased]` in the changelog agree. The focused Wireless
setup suite passes **16/16**, including a second contract that keeps the fallback
visible after a simulated low-level rejection.

The temporary harness was developed as a deliberately disposable test fixture,
and its failed iterations are recorded so a later session does not misdiagnose
them as product regressions. The first DEBUG compile caught and corrected a
nullable `ApplicationContext` guard. The first install used .NET Android fast
deployment, whose thin APK cannot run after a standalone `adb install`; it
failed before managed product code loaded. Rebuilding a self-contained DEBUG
APK removed that packaging-only problem. A `Theme.NoDisplay` activity then hit
Android's requirement to finish before `onResume` and, by taking activity focus,
also allowed Motorola to close the loopback pairing socket. That design was
discarded.

The corrected fixture is a DEBUG-only exported broadcast receiver using
`GoAsync()`. It invokes the unchanged production `WirelessAdbService` without
moving foreground focus away from Settings, validates only the expected port/
code shapes, and logs neither value. Its self-contained build completed with 0
warnings/errors. A deliberately malformed request was rejected cleanly while
Settings remained the focused app. One fresh real request then completed with
`connected=True` and `pairingSucceeded=True`; the receiver disposed its
temporary connection, Android closed the code panel, and Settings listed the
reusable VisualCat authorization. No pairing secret or pairing endpoint was
written to repository evidence. This closes the Motorola low-level pairing gap
without weakening F-43's truthful product guidance. The receiver and its helper
scripts are temporary and must be deleted before the next Release build.

### 19.7 Final Release transport/lifecycle checkpoint

All temporary DEBUG source and helper scripts were deleted before the shipping
build. A repository search finds no harness class, action or log tag. The
optimized Release build completed in 46.00 seconds with 0 warnings/errors. Its
decoded manifest contains neither a harness component nor `READ_LOGS`, and the
installed package has no `DEBUGGABLE` flag. The exact immutable candidate is
`artifacts/live-test/motorola-continuation-20260824/m07-final-release-signed.apk`:
33,827,085 bytes, SHA-256
`639C4191FB08D90DDC5AFA66DD8A982188167BE1034424D5DBA638407302F652`.
Installing it with `-r` preserved the app-owned pairing identity and the
original 17:00:32 install time while updating the package at 17:37:57.
The installed `base.apk` was then pulled back as
`artifacts/live-test/motorola-continuation-20260824/m08-installed-base.apk`;
its byte count and SHA-256 exactly match the immutable candidate, proving the
device is running the artifact described here.

The final Release source chooser immediately reported **Recommended · already
paired**. **Connect full-device** reused that identity and started a real
Wireless ADB `logcat -b all` session. Before the permission decision, Android
reported exactly one `dataSync` foreground service with ID 4108/start ID 1,
permission false and no posted VisualCat record. The genuine first-use prompt
was fully visible; selecting **Allow** granted `POST_NOTIFICATIONS`, reposted the
same foreground service at start ID 2, and produced the intended private,
silent, ongoing, non-clearable notification with one action. Exactly one shell
`logcat` child and one in-progress session existed. The workspace showed 2,599
entries at the first post-grant evidence capture and continued climbing.

Active-capture composition passes portrait and 1220 × 2712 landscape rotation.
Landscape keeps Open/Recording/More, Filters/Plot/Split/Details/Fit,
Follow/Stop, histogram/minimap, rows and the complete status in one usable
viewport. Back in portrait, Plot, Details and Split each rendered as distinct,
usable workspaces; the session stayed owned by the same tab and **Recording**
returned to it rather than creating another capture.

The reconnect test killed the sole child PID only. One second later there was
exactly one replacement child, the foreground service remained ID 4108/start
ID 2, and the visible session advanced from 9,845 to 11,463+ received lines.
There was no duplicate reader or stale-session reset. With the screen then off
and Android reporting `Dozing`, the app process, sole reader and same foreground
service remained present at 55, 100 and 125 seconds. Battery stayed at 100% and
temperature fell from 31.9 °C to 31.2 °C.

At wake, the owner's secure fingerprint lock engaged. VisualCat's private
notification action was correctly not exposed on that lock screen, and no
credential was guessed or bypassed. To avoid leaving work running unattended,
the exact package was force-stopped. Immediately before that action Android
reported one app notification and one reader; three seconds afterward it
reported zero app processes, zero readers, zero VisualCat notification records
and no capture service. This safely exercised abrupt lifecycle teardown. The
owner later unlocked normally and §19.8 completed every UI check that was paused
at this boundary.

Final repository gates are otherwise complete. The Release suite passes
**409/409** tests: Domain 11, Core 95, App 251 and Application 52. Scoped
`dotnet format --verify-no-changes` passes for all three changed C# files;
`tools/verify-docs.ps1` passes 95 relative links across 43 Markdown files; and
`git diff --check` exits 0. A whole-solution format probe reported only the
pre-existing Android binding generator's `obj/Debug/.../__NamespaceMapping__.cs`
whitespace, so generated output was not edited and the scoped source gate was
used as the meaningful result.

The privacy audit also removed eleven exact, reproducible M-06 artifacts that
showed the device's local Wireless-debugging connection endpoint and three
lock-screen artifacts containing owner-visible personal content. Safe product
evidence remains. A post-delete scan finds zero text artifacts with an
IP-and-port endpoint and zero standalone six-digit values in pairing context.
Font scale is restored to 1.0, rotation is restored to portrait/rotation 0, and
Wireless debugging remains enabled for future saved-pairing capture.

### 19.8 Owner-unlocked completion — F-43, notification stop and persistence

After the owner unlocked normally, cold launch recovered the force-stopped
session exactly as designed: **Interrupted · 19,450 entries recovered**, with a
truthful notice that everything which reached disk is exact and later source
output may be absent. Its tab, histogram, rows, workspace controls and Review/
Dismiss actions remained usable. This closes the abrupt-stop persistence half
of the lifecycle test.

Wireless debugging was then temporarily switched off so the saved-pairing
dialog could remain visible instead of immediately reconnecting. Automatic
saved reconnect failed truthfully and left **Connect saved pairing**, **Pair
again with a new code**, and Cancel reachable. Selecting the new-code form on
the final Release displayed the complete five-step F-43 guidance in one
scrollable, framed sheet with the pinned footer intact. It explicitly names
Android's **Pairing unsuccessful** result, says to generate one fresh code,
explains that some devices cancel codes even in split screen, and directs the
reader to VisualCat-only capture after the repeated OEM failure. Pairing port,
pairing code, Cancel and **Pair & connect** remained reachable. The physical
evidence is `m08-f43-copy.{png,xml}`. F-43 is therefore device-verified, not only
code- and contract-verified.

After Wireless debugging was restored, a second saved-pairing connection
started one fresh full-device capture with exactly one app process, one reader,
one private foreground notification and foreground service ID 4108/start ID 1.
The unlocked Motorola shade rendered VisualCat under Silent with truthful
full-device/local copy. Expanding it exposed a **Stop and save** accessibility
node measuring 318 × 135 px. Selecting it removed the service and notification
by the first one-second poll and the sole reader by the second. VisualCat then
showed a separate complete session as **Stopped · 4,720 entries kept**.

A final exact-package force-stop/cold launch reopened that session as **Ready ·
4,720 entries**, with its histogram and rows intact; the earlier 19,450-entry
interrupted session remained independently labeled rather than being merged or
silently rewritten. The final visual oracle is
`m08-final-persistence.{png,xml}`. Android reports zero reader processes, zero
capture services and zero VisualCat notification records. The app is
foregrounded on the complete session; `POST_NOTIFICATIONS` remains granted;
Wireless debugging is enabled for the reusable pairing; font scale is 1.0;
automatic rotation is enabled with portrait user rotation 0; battery is 100%,
temperature 32.9 °C and thermal behavior remained normal.

The two temporary unlocked notification-shade screenshots and their XML dumps
were deleted after the action geometry/result were recorded because they also
contained unrelated personal notifications. No pairing code, pairing endpoint
or personal notification content is retained in the final continuation
evidence. This completes M-01 through M-08 with no remaining device or code
action.

---

## 20. Pixel 5 independent continuation — current tree and fresh install

This section is the durable handoff for the continuation requested on the newly
connected physical Android device. It begins after §19 completed on Motorola;
its steps deliberately preserve the inherited Pixel state before the owner-
authorized clean update. Every row in §20.2 is now closed; §20.12 is the
deterministic hand-back checkpoint for a later independent session.

### 20.1 Durable continuation checkpoint

| Field | Value |
|---|---|
| Continuation start | 2026-08-24 17:00 UTC / 19:00 CEST |
| Repository | `main` at `16ed98e` (`Harden Android live capture lifecycle`) |
| Inherited working tree | Seven intentional modified files from §19: F-43 product copy, two contracts and matching support/plan/changelog/report records; no unrelated source edit identified |
| Device | Google Pixel 5 (`redfin`), Android 14 / API 34, serial `0A031FDD400365`, `arm64-v8a` primary ABI |
| Build fingerprint | `google/redfin/redfin:14/UP1A.231105.001.B2/11260668:user/release-keys` |
| Display | 1080 × 2340 at density 440 (approximately 393 × 851 dp); gesture navigation; dark mode; font scale 1.0; rotation enabled and currently portrait |
| Locale / time zone | `cs-CZ`; `Europe/Prague` |
| Power / thermal / storage | USB powered, battery 100%, thermal status 0, battery 28.3 °C, approximately 92.3 GiB free on `/data` |
| Inherited package | `com.barebit.visualcat` 2.0.6 / 20006, target SDK 36, installed 2026-08-23 22:53:10; non-debuggable |
| Inherited runtime | Process PID 17603 retained while the screen was dozing; no VisualCat capture service, app-owned `logcat` reader or posted VisualCat notification found |
| Authorization | The owner explicitly authorized updating and, if useful, deleting the installed app; exact-package clean replacement is permitted after build and evidence gates pass |

All 43 findings in §5.1 are already marked **Done** and device-verified. This
pass does not mechanically reimplement their original suggestions. It verifies
the uncommitted F-43 handoff, updates the new device to the exact current-tree
artifact, and re-audits high-risk UX/UI, accessibility and capture lifecycle
paths. Contrary physical evidence becomes a new numbered finding, a failing
contract where practical, a robust implementation, and a same-device retest.

### 20.2 Step ledger (live)

| ID | Step | Status | Evidence / resume instruction |
|---|---|---|---|
| N-01 | Audit report, working tree and connected-device baseline | **Done** | §20.1 records the exact commit/diff scope, device, display, locale, power, inherited package and inactive capture state. |
| N-02 | Preserve and privacy-scrub the inherited 2.0.6 oracle | **Done, visual credential-blocked** | The black ambient frame and package/runtime facts are preserved. The personal lock-screen XML was deleted and absence confirmed. `wm dismiss-keyguard` correctly left the secure keyguard in place; no credential was guessed or bypassed, so there is no inherited foreground product visual. |
| N-03 | Verify the inherited F-43 change; build and run pre-install gates | **Done** | §20.4: F-43 setup 16/16 and full Release 409/409; clean optimized build 0 warnings/errors; manifest, permissions, components, ABIs and v3 signature verified; exact install candidate/hash preserved. |
| N-04 | Clean-install the exact current-tree package | **Done** | §20.5: exact 2.0.6 target rechecked and removed, absence proved, current-tree candidate installed, installed bytes/hash identical, Android identity/permissions correct, and cold launch completed without fatal/ANR. The owner subsequently unlocked normally for the visual pass. |
| N-05 | Fresh-eyes responsive UX/UI and accessibility sweep | **Done** | §§20.6–20.11: portrait, landscape, 1.3× text, sheet/IME, Plot, Split, Details, Filters and search-keyboard states are physically exercised. All 15 clickable workspace nodes measured at least 48 dp in the final sweep. |
| N-06 | Capture, notification, reconnect and persistence lifecycle | **Done, with declared transport/action gaps** | §20.11 proves one-reader ownership, background continuity, UI Stop/save, exact-package cold persistence and forced recovery. A clean uninstall intentionally removed saved Wireless ADB identity, and Pixel's shade did not expose the action node to automation; those two unclaimed branches are explicit in §20.12. |
| N-07 | Implement and regress every current-tree defect found | **Done — F-44…F-48** | §§20.7–20.10 close F-44–F-47; §20.13 closes the final fractional-density target-rounding defect with a failing-first contract and exact Pixel remeasurement. |
| N-08 | Final regression, privacy cleanup and deterministic hand-back | **Done** | §20.13 supersedes the complete N-13 checkpoint in §20.12 with 413/413 tests, a clean N-18 build, installed-hash proof, final privacy checks and the restored idle device state. |

### 20.3 Baseline evidence and privacy decision

The inherited package resolves to
`com.barebit.visualcat/crc64a1973b883a99125a.MainActivity`. Android reports
`HAS_CODE` and `ALLOW_CLEAR_USER_DATA` without `DEBUGGABLE`; it requests no
`READ_LOGS`. The bounded runtime inspection found the app process but no active
capture resources. The display was `Dozing`, so the first screenshot is a black
ambient frame rather than a product visual.

`artifacts/live-test/pixel5-continuation-20260824/p20-baseline.png` is retained
only as the screen-state oracle (15,495 bytes; SHA-256
`01C30EC12F73D7C99AA134DE9AF955E26CAAFABD7BD1B67DEF06E8C0AC391533`).
The accompanying UI hierarchy contained unrelated personal notification labels
despite the black frame. It was therefore deleted as soon as this limited fact
was recorded and must not be recreated on the lock screen or notification shade.

Waking the device and calling Android's normal `wm dismiss-keyguard` route left
the secure keyguard in place, as it should. The inherited foreground visual is
therefore a declared credential-blocked gap rather than an implied pass. The
package/runtime oracle is sufficient for the authorized replacement, and no
owner credential was guessed, entered or bypassed.

### 20.4 F-43 and clean current-build gate

The focused `WirelessAdbSetupTests` suite passes **16/16**, including both F-43
contracts. `dotnet test VisualCat.slnx -c Release --no-restore` passes
**409/409**: Domain 11, Core 95, App 251 and Application 52.

The first incremental Release output had the same 35,384,920-byte size as §19's
pre-harness artifact. Although no temporary harness source remained, artifact
provenance should not depend on old `obj`/`bin` state, so it was rejected before
installation. `dotnet clean` followed by a new optimized Release build completed
in 1m19.78s with 0 warnings and 0 errors. A source search and the decoded clean
manifest both find no pairing harness action, class, receiver or service.

Build-tools 36 report `com.barebit.visualcat`, 2.0.7-dev / 20007, min SDK 31,
target SDK 36, and `arm64-v8a` plus `x86_64`. The manifest requests exactly
`INTERNET`, `CHANGE_WIFI_MULTICAST_STATE`, `FOREGROUND_SERVICE`,
`FOREGROUND_SERVICE_DATA_SYNC`, `POST_NOTIFICATIONS`, and AndroidX's
package-scoped non-exported receiver permission. It requests neither `READ_LOGS`
nor storage. The application capture service and provider are non-exported; the
launcher/ACTION_VIEW activity is the intended exported product surface.

`apksigner` verifies one APK Signature Scheme v3 signer: the expected local
Android debug certificate (`e58d3c45…`), not the production upload key. The exact
install candidate is
`artifacts/live-test/pixel5-continuation-20260824/n03-clean-release-signed.apk`:
35,384,920 bytes, SHA-256
`46E44003267363D25E4BEA4C50FCB50FA542E10B32089719D5CB383C394C0DFD`.

### 20.5 Authorized clean replacement

Immediately before deletion, `pm path` again resolved only the intended
`com.barebit.visualcat` package and PID 17603. `adb uninstall
com.barebit.visualcat` returned `Success`; subsequent `pm path` and `pidof`
returned nothing. This erased only VisualCat 2.0.6's private state, as explicitly
authorized, including any old sessions or saved Wireless ADB identity.

Installing the immutable N-03 APK returned `Success`. Android now reports
2.0.7-dev / 20007, min SDK 31, target SDK 36, `HAS_CODE` and
`ALLOW_CLEAR_USER_DATA`, without `DEBUGGABLE`. First/last install time is
2026-08-24 19:12:55 local. `POST_NOTIFICATIONS` is correctly false on clean
first use and `READ_LOGS` is absent. The manifest-resolved activity is unchanged.

The installed `base.apk` was pulled back as
`artifacts/live-test/pixel5-continuation-20260824/n04-installed-base.apk`.
Its 35,384,920-byte length and SHA-256
`46E44003267363D25E4BEA4C50FCB50FA542E10B32089719D5CB383C394C0DFD`
exactly match N-03, proving the Pixel is running the gated artifact.

An exact-package force-stop followed by `am start -W` completed in 3.025s as
PID 16828. The activity and process are healthy and the process-bounded launch
log contains no fatal exception, unhandled exception or ANR. The screen returned
to Dozing behind the secure keyguard, so launch timing is functional evidence,
not a claim about first-frame visual latency. The owner was asked to unlock
normally before N-05; no credential is required by or exposed to this test.

### 20.6 Responsive/accessibility sweep — live checkpoint

After the owner unlocked normally, the clean portrait home rendered correctly
at 393 × 851 dp, dark theme and 1.0× text. The full VisualCat branding, three
toolbar commands, hero explanation, 3 × 2 severity legend, three hero actions
and build provenance are distinct, unclipped and balanced. The UI hierarchy has
exactly six clickable nodes; every one is 48.0 dp high and 70.5–118.2 dp wide.
The labels distinguish Open, Live, More and all three hero routes without an
app-private path. Safe evidence is `n05-home.{png,xml}`.

The device's inherited screen timeout is only 30 seconds. It expired during the
home-node measurement, so the next scripted tap reached the ambient lock screen
instead of VisualCat. That attempt is not product evidence. Its black screenshot
and UI hierarchy were both deleted immediately because the hierarchy contained
unrelated personal notification labels. The original
`stay_on_while_plugged_in` value is 0; it is temporarily 2 (USB) for the live
pass and must be restored to 0 in N-08. The secure keyguard again remains for the
owner to unlock normally.

After the second normal unlock, the clean first-use source chooser passes. Its
complete local-only/background-service explanation, recommended full-device
choice, honest VisualCat-only fallback, Cancel and **Set up full-device** all fit
inside a four-sided framed sheet; the two scope targets and both footer buttons
are at least 48 dp high. `n05-source.{png,xml}` is the safe oracle.

The setup sheet also proves F-43's final copy on Pixel/API 34. The complete five
steps name Android's **Pairing unsuccessful** result, require one fresh code and
offer VisualCat-only capture after a repeated OEM cancellation. The Wireless
debugging route, both pairing fields, privacy statement and pinned actions are
present in the accessibility tree. Without the keyboard, the body scrolls under
a stable four-sided frame and the footer remains separate. Evidence is
`n05-setup.{png,xml}`.

### 20.7 F-44 — the pairing form ignores the Pixel numeric IME

**Status: Done — contract and exact Release behavior verified on Pixel/Gboard.**

Tapping the visible pairing-port field focuses it and opens Gboard's numeric
layout (`mInputShown=true`). The screenshot shows the IME beginning at physical
y=1194. Android's accessibility geometry still places the focused port editor at
y=1839–1971, the code editor at y=2072–2204 and the pinned Cancel/Pair row at
y=2023–2155. All three are completely behind the keyboard; no part of the value
being typed or the visible exit/submit route remains on screen. The failure is
`n05-setup-ime.{png,xml}`. No pairing attempt or secret was used; the only value
entered was invalid port `0`.

This contradicts §19's Motorola/API-36 pass and is not a false screenshot caused
by the earlier lock timeout: VisualCat remained the focused window, its process
remained PID 16828, Gboard was explicitly reported open, and both the screenshot
and hierarchy agree on the non-intersection. It is a major clean-setup UX defect.

The root cause is shared sheet geometry. `AdjustResize` is requested, but this
Pixel reports Gboard as an `InputPane.OccludedRect` without reducing Avalonia's
sheet host. `SessionWorkspaceView` already consumes that rectangle for its
filter drawer; `MainView`'s overlay sheets do not. `WirelessAdbSetupDialog` adds a
small scroll reserve only after validation fails, not when an editor first gains
focus, and it cannot move the outer panel or pinned footer above the IME.

The robust fix is therefore not a Pixel-sized spacer. Every in-page sheet must
observe the top-level input pane, translate its occluded top into the overlay
host, move its bottom edge above the IME, cap itself to the genuinely available
height, and restore its ordinary margin/cap when the IME closes. After that
reflow, the active editor must be scrolled wholly into the internal viewport.
This also protects future sheet forms instead of special-casing these two fields.

The contract `SheetInputPaneLayoutKeepsThePanelWhollyAboveTheIme` was added
first. It failed to compile against the reproduced source because the shell had
no input-pane placement model. The implementation adds that model to the shared
`MainView` overlay host: every sheet observes the top-level `InputPane`, converts
its occluded top edge into host coordinates, assigns exactly the matching bottom
inset and visible maximum height, and restores the ordinary 82% cap when the IME
closes. The focused control is then revealed against the newly arranged internal
scroll viewport. Wireless setup also schedules the same exact reveal when focus
moves between port and code while the IME is already open; its earlier fixed
one-row nudge is gone. No keyboard height, device density or OEM constant is
hard-coded.

The focused setup suite now passes **17/17**, including F-43 and F-44. The
changelog records the user-visible behavior. A new optimized Release must be
built, installed and re-measured against the exact Pixel/Gboard failure state
before F-44 can be marked Done.

The exact rebuilt candidate is
`artifacts/live-test/pixel5-continuation-20260824/n07-f44-release-signed.apk`:
35,389,016 bytes, SHA-256
`4BF1993C9149B9CA0D5AB6F1646830023E3C241B0E6ED94D874007A7A31F15BC`.
The optimized clean build completed in 1m22.32s with 0 warnings/errors after the
full Release suite passed **410/410** (Domain 11, Core 95, App 252, Application
52). Build-tools and `apksigner` reconfirm the same identity, SDKs, ABIs and one
v3 signer. `adb install -r` succeeded; the installed base was pulled back and
its byte count/hash exactly match the immutable candidate.

F-44 now passes its original Pixel/Gboard oracle. With the port keyboard open,
the editor is wholly visible at y=933–1065, the pinned actions end at y=1245,
and Android reports the IME beginning at y=1364. Moving focus to code while the
IME remains open also re-scrolls: code y=934–1066, actions y=1113–1245, and the
taller IME begins at y=1267. Port validation is likewise complete and adjacent
at y=689–760, its editor y=794–926 and actions y=1015–1147, all above the IME at
y=1364. Safe evidence is `n07-f44-{port,code,validation}.{png,xml}`. F-44's
functional and geometry requirements are therefore device-closed; its table row
will be marked Done with F-45's final setup build so the installed artifact
remains one coherent hand-back candidate.

### 20.8 F-45 — the six-digit code is neither numeric nor masked on Pixel

**Status: Done — numeric input, visible masking and accessibility redaction verified together.**

Changing focus from port to code proves the F-44 reflow, but Gboard changes from
its numeric layout to a full Czech QWERTY keyboard even though the value is
exactly six digits. More importantly, after a non-secret test digit `1` is
entered, the screenshot renders `1` and the Android hierarchy exposes
`text="1"` with `password="false"`. The app set Avalonia's abstract
`TextInputContentType.Pin` and `IsSensitive`, but on this Pixel/API-34 bridge
that pair neither requests a digit-only keyboard nor masks the `TextBox`/its
accessibility value. No real pairing code was entered or retained.

The robust contract is explicit at the control as well as the platform hint:
use the known-working digit-only content type, keep suggestions off and sensitive
semantics on, and set a non-empty `PasswordChar` with reveal disabled. Validation
still independently rejects anything outside six ASCII digits; masking is not a
substitute for shape validation. The device retest must prove numeric Gboard,
masked visible text and no plaintext code in the UI hierarchy.

The implementation now requests `Digits`, uses a bullet `PasswordChar`, keeps
password reveal disabled, and retains sensitive/no-suggestions hints. The first
physical retest produced numeric Gboard and six visible bullets, but Android's
UI hierarchy still disclosed the synthetic test value because Avalonia 12's
stock `TextBoxAutomationPeer` returns `Text` even for a password-painted editor.
That XML was deleted immediately; only the visually masked screenshot remains.

A new regression therefore exercises the actual `IValueProvider`, not just
control properties. It failed with plaintext `123456` before the bridge fix.
The initial attempt to override `TextBoxAutomationPeer.Value` correctly failed
at compile time because Avalonia declares the member final. The supported fix is
a small sensitive `TextBox` peer that retains the stock peer and edit-control
behavior while explicitly reimplementing `IValueProvider.Value` as one bullet
per character. The focused setup suite passes **17/17**, including the numeric,
paint masking, reveal-disabled and automation-redaction contracts. A fresh
Release build and Pixel hierarchy retest are required before closure.

The first peer-enabled device build passed the keypad and hierarchy checks:
Android exported `●●●●●●` and a literal search found zero occurrences of the
synthetic input. Direct screenshot review nevertheless rejected that build: the
derived control had no applied text presenter, so its focused editor looked
blank rather than showing masking feedback. Avalonia themes custom controls by
their derived type unless they opt into a base style key. `SensitiveTextBox`
now returns `typeof(TextBox)` from `StyleKeyOverride`, and the setup regression
locks that invariant alongside redaction. The suite remains **17/17**. The
rejected screenshot is diagnostic evidence only; a second fresh build must
prove both visible bullets and redacted hierarchy together.

The second build, N-09, passes all three physical requirements simultaneously.
Gboard stays on its numeric layout, the focused field paints six bullets, and
Android exports `SensitiveTextBox text="●●●●●●"`; a literal scan finds zero
occurrences of the synthetic `123456`. The field is wholly visible at
y=934–1066, the actions end at y=1245 and the IME starts at y=1364. Safe
evidence is `n09-f45-pass.{png,xml}`. The final N-13 candidate repeats the same
masked/numeric/redacted result in F-47's more demanding state, so F-45 is closed
on the installed hand-back artifact as well as its first passing build.

### 20.9 F-46 — 1.3× landscape home clips its final content

**Status: Done — same-state 1.3× landscape behavior verified on Pixel.**

At the plan's 1.3× font-scale boundary, the portrait home remains balanced and
complete: all six controls are present, each is at least 48 dp high, the wrapped
headline and description are readable, and build provenance is above the
gesture inset. Safe evidence is `n10-font130-home.{png,xml}`.

Rotating the same running process to landscape exposes a short-viewport defect.
The final **RECENT CAPTURES** action ends at physical y=1013, exactly one pixel
above the usable viewport edge at y=1014, while the provenance is arranged at
y=1054–1080 behind the 66-pixel navigation bar. The accessible node exists but
the text is visibly clipped. Evidence is
`n10-font130-landscape-home.{png,xml}`.

The root cause is the hero's oversized `StackPanel` being vertically centered
directly in a shorter host. Avalonia clips equal overflow rather than offering
movement. The new contract
`HomeHeroCanScrollWhenLargeTextExceedsAShortLandscapeViewport` first failed
because no ancestor scroller existed. The hero is now hosted by a vertical
auto-scroller with horizontal movement disabled and centered content alignment:
ordinary screens retain the existing composition, while short/large-text
screens can reach every action and the provenance. All eight Samsung/responsive
contracts pass. A fresh Release build and same-state Pixel retest are required
before closure.

The N-11 Release retest preserves the centered first frame and makes the entire
oversized hero reachable. After one ordinary upward swipe, **RECENT CAPTURES**
occupies y=733–865 and the provenance occupies y=906–948, both above the usable
viewport edge at y=1014. Safe before/after evidence is
`n11-f46-landscape-{top,scrolled}.{png,xml}`. The responsive suite remains 8/8;
F-46 is device-closed.

### 20.10 F-47 — the 1.3× landscape IME collapses setup to a title

**Status: Done — the final Release preserves the editor and restores the full sheet.**

The N-11 candidate was kept in the Pixel's 1.3× font/landscape state and the
full-device form was scrolled to its pairing fields. Gboard begins at physical
y=344, leaving only 208 px (about 76 dp) above it. The original F-44 rule tried
to fit the complete sheet into that strip even though its title and 48 dp
decision row alone cannot fit. The result is a card containing only **Connect
full-device capture**; the editor, privacy explanation and actions are all
absent. `n11-font130-landscape-code-ime.{png,xml}` records the failure; the
hierarchy contains no synthetic code because the hidden editor did not receive
the attempted input.

This is an impossibility boundary, not a new device constant. If the unoccluded
height is at least the sheet's existing 240 dp useful-height floor, the F-44
behavior remains exact: the full panel and footer stay above the IME. Below that
floor, the panel keeps its ordinary responsive cap, anchors to the top, and
allows the keyboard to cover the deferred footer. Both the shared focus reveal
and Wireless-setup's delayed port-to-code reveal cap their usable scroll
viewport at the real IME top. This prioritizes the focused 48 dp editor in the
only usable strip; hiding the keyboard restores the pinned actions.

`SheetInputPaneLayoutPreservesAnEditorViewportWhenTheImeIsExtremelyTall` was
added first and failed because the placement model had no extreme-height mode.
The setup suite now passes **18/18**, including ordinary no-overlap geometry,
top-aligned fallback, exact unoccluded scroller height, numeric/masked code and
automation redaction. A fresh optimized Release must reproduce the exact
landscape/1.3×/IME state before F-47 is closed.

The first extreme-height candidate, N-12, moved the editor into view but left
its lower 26 px (about 9.5 dp) under the IME. It was rejected. The final change
temporarily hides only the visual heading in this impossible-height mode while
retaining the panel's accessible name, reclaiming enough room for the complete
48 dp editor. No dimension or keyboard model is special-cased.

The N-13 Release pass reproduces the exact 1.3× landscape state. The masked
`SensitiveTextBox` occupies y=203–335 and Gboard begins at y=344: a 9 px clear
gap, six visible bullets, numeric keys and zero plaintext in the hierarchy. The
card remains named **Connect full-device capture** for accessibility. Pressing
Back closes the IME and restores the visual heading at y=259–322 plus Cancel and
**Pair & connect** at y=763–895; the retained value remains bullet-redacted.
Safe evidence is `n13-f47-code-ime.{png,xml}` and
`n13-f47-ime-dismissed.{png,xml}`. F-47 is device-closed.

### 20.11 Capture, recovery and workspace continuation

**Status: Done, with the two unclaimed branches stated below.** The clean install
had no saved Wireless ADB identity, so the lifecycle pass used the source
chooser's honest **VisualCat only** fallback. Android first presented its Czech
notification-permission dialog; the owner-visible **Allow** route was selected
and `POST_NOTIFICATIONS` became granted. No endpoint or real pairing code was
entered.

Starting capture produced exactly one app PID (23269), one app-owned `logcat`
child (23534), one `CaptureForegroundService` with `isForeground=true`, and one
private ongoing notification (ID 4108) with exactly one **Stop and save** service
action. `n14-capture-running.{png,xml}` shows 15 entries already arriving. Home
then kept the app backgrounded for 20 seconds: the same app PID, reader, service
and notification remained singular. Returning to VisualCat was a hot 49 ms
resume and showed 40 entries (`n14-capture-resumed.xml`), proving continuity
rather than a hidden restart.

The Pixel notification shelf exposed only VisualCat's app icon to UI automation;
tapping it opened Android's general notification settings, not the notification
action. The decoded notification still proves the action's label, intent type,
privacy and singular ownership, but this pass does **not** claim a physical tap
of **Stop and save**. The temporary shade/settings evidence was deleted because
it could include unrelated personal notification state. §19.8 already verifies
the same final action end to end on the unlocked Motorola. On this Pixel the app
UI's **Stop** route completed normally with **Stopped · 5,311 entries kept** and
zero reader, service or notification; safe evidence is
`n14-capture-complete.{png,xml}`.

An exact-package force-stop removed PID 23269. Cold launch created PID 25358 in
2.506 seconds and reopened the same session as **Ready · 5,311 entries**, with
rows and plot intact and no live resources. Evidence is
`n14-cold-persistence.{png,xml}`. A second VisualCat-only capture then produced
one reader and four durable entries. Force-stopping the package while it was
active removed the process, reader, service and notification. A 2.486-second
cold launch as PID 25759 recovered a separate session as **Interrupted · 4
entries recovered** and explained that only records already on disk are
recoverable. `n15-recovery-dialog.{png,xml}` is the recovery-notice state;
`n15-recovery-review.{png,xml}` is the actual Review dialog with Delete, Keep and
Export choices. **Keep** retained the four entries with truthful interrupted
semantics (`n15-recovery-kept.{png,xml}`).

The final fresh-eyes workspace sweep exercised Plot, Details and Split
(`n16-plot`, `n16-details` and `n16-final-workspace`), then opened Filters and
focused Search (`n16-filters` and `n16-filters-ime`, each with PNG/XML). At 440
dpi all 15 clickable workspace controls are at least 48 dp. With Gboard open,
Search occupies y=700–832, Reset/Done y=1196–1328 and the IME begins at y=1364,
leaving the complete footer reachable. Reset returns **No filters · showing
everything in view**. The final workspace is Split on the complete session and
reports **970 in view · 5,311 match · 5,311 in session** and **Ready · 5,311
entries**. Plot, filters, rows, progressive loading and status remain visually
distinct without clipping.

Fresh real Wireless ADB pairing/reconnect is the other unclaimed branch. The
owner-authorized clean uninstall deliberately erased the old encrypted identity,
and switching from Android's pairing-code surface back to the app invalidates
the Pixel's one-time code as already documented in §12/F-43. No credential was
invented and no pairing failure is promoted to a pass. Saved reconnect and the
real notification-action path remain covered by the earlier Motorola run.

### 20.12 Final gates, privacy cleanup and hand-back

**Status: Complete N-13 checkpoint; superseded by the F-48/N-18 final in
§20.13.** At 2026-08-24 19:14 UTC / 21:14 CEST, the optimized clean Release
build completed in 1m27.06s with 0 warnings and 0 errors. Its immutable candidate
is
`artifacts/live-test/pixel5-continuation-20260824/n13-final-release-signed.apk`:
35,389,016 bytes, SHA-256
`7F0459B569B54EAE589E5537E9C6B89E1DF25C33D796CD28E73B30B5210F53DC`.
The installed `base.apk` pulled back as `n13-installed-base.apk` has the exact
same length and hash.

Build-tools 36 reconfirm `com.barebit.visualcat`, 2.0.7-dev / 20007, min SDK 31,
target SDK 36 and `arm64-v8a` plus `x86_64`. The APK requests only Internet,
Wi-Fi multicast-state change, foreground/data-sync service, notifications and
AndroidX's package-scoped receiver permission; it requests neither `READ_LOGS`
nor storage. `apksigner` verifies one v3 signer, the expected local Android debug
certificate (`e58d3c45…`), not the production upload key.

N-13 repository gates against that source all pass:

- `dotnet test VisualCat.slnx -c Release --no-restore`: **412/412** — Domain 11,
  Core 95, App 254 and Application 52;
- focused Wireless setup: **18/18**, including F-43–F-45 and F-47; responsive
  layout: **8/8**, including F-46;
- `dotnet format ... --verify-no-changes --no-restore` over every touched source
  and test file: pass;
- `tools/verify-docs.ps1` under a process-scoped execution-policy bypass: 95
  relative links across 43 Markdown files plus required files/version metadata,
  all consistent;
- `git diff --check`: pass.

The evidence tree contains zero XML files with literal synthetic code `123456`.
The one pre-fix hierarchy that exposed it, plus temporary lock-screen and
notification-shade/settings captures that could contain personal labels, were
deleted. Retained rejection screenshots contain no real credential. No endpoint,
real pairing code or other pairing secret was used or preserved.

The Pixel is handed back with font scale 1.0, automatic rotation enabled,
portrait user rotation 0, screen timeout 30,000 ms and
`stay_on_while_plugged_in=0`, matching its recorded original settings. Battery
is 100%, temperature 29.3 °C, thermal status 0 and about 92.3 GiB remains free on
`/data`. `POST_NOTIFICATIONS` remains granted as the explicit first-use choice.
VisualCat is foregrounded on the complete 5,311-entry session in unfiltered
Split mode; the 4-entry interrupted session remains a separate tab and the
recovery notice is dismissed. PID 25759 is healthy. There are **zero** app-owned
`logcat` readers, capture services and VisualCat notifications. Safe final
evidence is `n17-handback.{png,xml}`.

The only limits of this continuation are the two precise branches above: no
fresh real Wireless ADB pairing/reconnect after the authorized identity-erasing
uninstall, and no physical Pixel tap of the notification action because its OEM
shade did not expose that node to automation. Neither limits F-44–F-47 or the
physically completed fallback capture, background, UI-stop, persistence,
forced-recovery and workspace results.

### 20.13 F-48 — Android rounds one nominal 48 dp severity target below the floor

**Status: Done — failing-first contract, clean build and exact Pixel retest pass.** The final
evidence audit measured every clickable node rather than assuming a logical
`Width = 48` must export as 48 physical dp. In both
`n16-filters.{png,xml}` and `n16-filters-ime.{png,xml}`, six severity toggles
occupy 132 px at 440 dpi, but **Debug** occupies x=646–777: 131 px / 2.75 =
47.6 dp. Height is exactly 132 px / 2.75 = 48 dp. The neighboring 4 dp gaps and
fractional layout origin make Android round this control's two accessibility
edges inward independently.

This is a polish defect with a direct accessibility oracle. The implementation
must retain the compact wrap and visual rhythm while reserving enough logical
width that no platform-edge rounding can cross the 48 dp floor. A host contract
will require the mobile severity targets to reserve one additional logical dp;
then a fresh Release update must show all seven target bounds at or above 48 dp
in the same Pixel drawer, both without and with Gboard.

`PhoneSeverityFilterTargetsReserveForPlatformEdgeRounding` was added first and
failed 7/7 target assertions: every chip reserved exactly 48 logical dp. The
mobile chip width now uses `TouchTarget.Minimum + 1`; desktop remains 28 dp.
That one-dp reserve does not change the phone's six-plus-one two-row wrap, label
copy, height, color or spacing. The focused contract passes, and the complete
Samsung/responsive suite passes **9/9**.

The exact N-18 Pixel retest passes before and during Gboard. All seven exported
bounds are 135 × 132 px = **49.1 × 48.0 dp**; Debug is now x=658–793 instead of
the failing x=646–777. Search and the pinned Reset/Done footer retain their
complete IME geometry, and the severity body remains scrollable. Safe evidence
is `n18-f48-filters.{png,xml}` and
`n18-f48-filters-ime.{png,xml}`. Two intermediate scroll captures were deleted
because the body drag dismissed Gboard and their filenames would therefore have
misrepresented the state.

After `dotnet clean`, the final Android Release build completed in 1m46.23s with
0 warnings and 0 errors, including trim/AOT. The immutable hand-back candidate
is `artifacts/live-test/pixel5-continuation-20260824/n18-final-release-signed.apk`:
35,389,016 bytes, SHA-256
`D78031210D05FB4599975F248B76012C485E6C95CBF9E0CADA316E05A4DC0BE5`.
Build-tools 36 reconfirm 2.0.7-dev / 20007, target SDK 36 and `arm64-v8a` plus
`x86_64`; `apksigner` reconfirms the expected single local v3 debug signer.
`adb install -r` succeeded without deleting the two sessions. The installed
`base.apk`, pulled back as `n18-installed-base.apk`, has the exact same length
and SHA-256. Cold launch completed in 3.012 seconds and reopened the persisted
5,311-entry session.

The final source passes **413/413** Release tests: Domain 11, Core 95, App 255
and Application 52. The F-48 contract passes individually; responsive layout is
9/9; focused Wireless setup remains 18/18. Scoped `dotnet format
--verify-no-changes --no-restore`, documentation verification and
`git diff --check` pass. The safe-evidence privacy scan still finds zero XML
files containing literal synthetic code `123456`.

At 2026-08-24 19:43 UTC / 21:43 CEST, the Pixel is again foregrounded on the
complete 5,311-entry session in unfiltered Split mode, with the recovery notice
dismissed and the separate four-entry interrupted tab retained. Font scale is
1.0, automatic rotation is enabled with portrait user rotation 0, the inherited
30-second timeout is untouched and `stay_on_while_plugged_in=0`. There are zero
app-owned `logcat` readers, capture services and VisualCat notifications. Safe
final evidence is `n18-handback.{png,xml}`. The two precise limits in §20.12 — no
fresh real Wireless ADB pairing after the authorized identity-erasing uninstall,
and no Pixel notification-action tap — remain unchanged.

## 21. Version 2.0.8 production-signed Pixel release smoke

**Status: Pass.** On 2026-08-24, the Google Play upload-key-signed 2.0.8
candidate was clean-installed on the connected Google Pixel 5 (`redfin`),
Android 14/API 34, serial `0A031FDD400365`. The previous development install and
its data were removed with the user's explicit authorization before install.

The exact local artifacts were:

| Artifact | Bytes | SHA-256 |
|---|---:|---|
| `artifacts/android/VisualCat-Android-v2.0.8.apk` | 35,393,112 | `10a004c50bb9921f4d8d252d002146024274fc241e70d86562a85dfb460758c2` |
| `artifacts/android/VisualCat-Android-v2.0.8.aab` | 35,263,976 | `2be753177cc7c9cc9e3f1af90e8b23c21fa3b9bf24d36ec051df22f7b7e0b674` |

Both packages passed `tools/package-android.ps1 -Format both -Version 2.0.8`.
The verifier confirmed application ID `com.barebit.visualcat`, version code
20008, min/target API 31/36, `arm64-v8a` and `x86_64`, all 190 native libraries
16 KB aligned, and the five audited Release permissions. The compiled AAB
contains the unexported capture service with foreground-service type
`dataSync`; neither package declares `READ_LOGS`. The APK uses Signature Scheme
v3 and both artifacts use the pinned Play upload certificate, SHA-1
`37:5C:8D:64:4F:BF:BD:07:DE:4C:1A:71:95:10:6C:94:4B:C6:B8:14`.

The clean install reported 2.0.8 / 20008, target SDK 36, no debuggable package
flag, and cold-started without a VisualCat fatal exception or ANR. The home
screen exposed the stable 2.0.8 provenance. In the Live chooser, full-device
and VisualCat-only scopes remained explicit; VisualCat-only was selected so the
clean-install lifecycle could be exercised without creating or retaining a new
Wireless ADB identity.

The first capture raised Android's one-time notification prompt. After the
owner selected Allow, the running workspace exposed Filters, Plot, Split,
Details, Follow and Stop capture as complete 132 px / 48 dp high targets. It
reported its limited own-app scope and a truthful quiet interval. Runtime
inspection found one `CaptureForegroundService` with
`isForeground=true`, foreground ID 4108 and type `00000001`, one app-owned
`logcat` child, and one private ongoing VisualCat notification with exactly one
**Stop and save** service action.

The app was sent Home and the screen was locked for 12 seconds. The same app
PID, single reader, single foreground service and single notification remained;
after the owner unlocked the phone, VisualCat returned to the same capture and
its received-line counter had advanced. In-app Stop then retained 65 entries
and removed the reader, service and notification. After a forced process stop,
a cold launch reopened the same complete 65-entry session as Ready, proving
that graceful stop persisted the capture.

The temporary UI hierarchies contain no pairing code or endpoint and remain in
the ignored local `artifacts/android` verification area only. Automatic
rotation was restored to enabled and font scale remained 1.0. VisualCat is
installed with the signed 2.0.8 candidate and foregrounded on the persisted
session; there are zero app-owned `logcat` readers, capture services and active
VisualCat notifications.

---

## 22. Play in-app update — Samsung physical-device pass

**Device:** Samsung SM-G990B (`RFCRC0A9GND`), Android 16, 1080x2340, portrait,
gesture navigation. Confirmed by `getprop ro.product.model` / `ro.serialno`
rather than by the `adb devices -l` listing, per the standing warning about
stale transport entries.

**Why this pass needs a stand-in.** Google Play never offers an update to a build
it did not install, so no `adb install` deploy — and no CI job — can reach the
real client. The behaviour that decides what the reader sees therefore lives in
`AppUpdatePolicy`, covered by unit tests off-device, and this pass drives Play
Core's own `FakeAppUpdateManager` through the identical adapter and UI. The
build is `-p:VisualCatFakeAppUpdate=true -p:Version=2.1.0-beta.1`: the fake
manager is opted into explicitly, and the channel is real rather than forced,
because a build that lied about its channel would not be exercising the rules it
is meant to demonstrate. End-to-end validation against the live Play client
still requires internal app sharing and remains a release-checklist item.

| Case | Result |
|---|---|
| Cold start, beta channel, update available | Offer rendered in the notice lane: *"VisualCat 2.1.1 is available on Google Play. You are on the beta channel."* with **Update** and **Dismiss**. Both 108 px / 48 dp tall. |
| Version name decoded from the version code | Fake offered version code `2010100`; the app named it **2.1.1** without Play supplying any name. |
| Tap **Update**, flexible flow | *"Downloading VisualCat 2.1.1 · 23.3 MB of 31.0 MB."* — no action button, and the version survived the transition into install-state reporting. |
| Download completes | *"VisualCat 2.1.1 is downloaded. Installing restarts the app."* with **Install**. |
| **Live capture running while an update is downloaded** | Install withheld. Lane read *"VisualCat 2.1.1 is downloaded. Stop the capture to install it — installing restarts the app."* with **no action button**, over a capture receiving 17 lines/s. |
| Capture stopped | *"Stopped · 2,975 entries kept"*, and **Install** returned immediately in the same lane. |
| **Dismiss** | `settings.json` recorded `updateDismissedVersionCode 2010100`, `updateSnoozedUntilUtc` +24 h (the beta snooze) and `updateLastCheckedUtc`. |
| Relaunch inside the snooze | No banner. The workspace restored its session with an empty lane. |
| **More ▾ → Check for updates…** | Present beside its three siblings, described *"Ask Google Play whether a newer VisualCat is out"*. Bypassed the standing snooze and re-offered, as a question the reader typed must. |
| Side-loaded build, real Play client | No update log line, no crash, no banner, and **no TCP socket owned by the app's uid** at any point. The automatic check never ran. |
| Side-loaded build, manual check | Description changed to *"Open the GitHub releases page — this build cannot update itself"*; the lane said *"This build was installed from a file, so Google Play cannot update it. Releases are published on GitHub."* with **Open releases**, which opened Chrome. |

**Two defects found and fixed during the pass**, both exposed rather than caused
by the feature:

- *The command bar painted the status message off the edge of the screen.* The
  message sat in the brand row's trailing `Auto` column, which sizes to the
  content, so the `CharacterEllipsis` it asked for could never engage. Any long
  notice ran through the wordmark and out of the window; the update offer made
  that the first thing a cold start showed. It now occupies the flexible column
  with right-aligned text, so the trimming has a width to trim against. The same
  message is no longer echoed there on Android at all, where the always-visible
  lane below already carries it in full — it was on screen twice.
- *The offered version lost its name as soon as the download began.* An
  `InstallState` carries a status and a byte count but no version code, and the
  adapter was reading the name back out of the shared cache, which a direct check
  never wrote to. Mid-download the lane degraded to *"Downloading a newer
  VisualCat"*. The adapter now keeps what its own last answer decoded, and the
  registry gained a cache-only write so a view rebuilt by an activity recreation
  can still render an offer without asking Play again.

**Release packaging.** `tools/package-android.ps1 -Format both` passed against the
Play upload keystore: version code **2000900** under the widened scheme, the
permission list **unchanged** (no permission from the Play Core chain), all 196
native libraries 16 KB aligned, APK Signature Scheme v3, and the pinned upload
certificate. Signed-AAB size went from 35,278,156 to 35,532,567 bytes — **+248
KiB, +0.72%** — measured by building the same commit with and without the package
reference.

### 22.1 Second pass — re-audit of the merged implementation

The implementation was re-read after it was merged, and the audit found thirteen defects, one of
them capable of losing a reader's settings. Two needed hardware to confirm the fix, and both were
re-run on the same device:

| Case | Result |
|---|---|
| Dismiss a **downloaded** update, then background and resume | Lane stayed empty. Before the fix the install prompt returned on every resume, because a pending download is re-reported each time and that state ignored the snooze — so the only answer that would end it was the restart the reader had just declined. `settings.json` showed `updateDismissedVersionCode 2010100` and a +24 h beta snooze. |
| **More ▾ → Check for updates…** inside that snooze | Offered the install again. A question the reader typed is never silenced by an earlier Dismiss. |

The defect that mattered most needed no device: update settings writes were not gated on the
settings file having been loaded, and a resume arriving during startup would have persisted a
default `ApplicationSettings` over the reader's real one — theme, timeline preferences and open
workspace included. It is now gated the same way the existing workspace writes are.

Packaging was re-run after the fixes: version code 2000900, permission list unchanged, 196 native
libraries 16 KB aligned, pinned upload certificate.

**Not covered here, by construction:** the live Play client's own consent UI, a
real staged rollout, `UpdateAvailability.DeveloperTriggeredUpdateInProgress`
after a genuine interrupted Immediate flow, and a device with no Play Store. The
first three need internal app sharing; the last needs non-GMS hardware. All four
are release-checklist items rather than claims made here.

### 22.2 Release audit and final 2.0.9 candidate

The last two implementation commits and the untracked verification plan were
audited against the merged code rather than taken as evidence by themselves.
The audit and final release gate found and corrected six release-relevant gaps: a Store fallback could
still expose an install during Live when Play disallowed flexible download; AAB
signing split whitespace-containing passwords into extra `jarsigner` arguments;
an explicitly requested prerelease did not override the checked-in
`VersionPrefix` used by the Android version code; and a Release command could
opt into the fake update manager. Long Release verification also reproduced two
short Windows scanner locks at atomic publication boundaries: one while sealing
a session manifest and one while publishing an extracted portable session. The
policy now withholds every install route during capture, signing uses temporary
password files, package metadata is derived and verified explicitly, Release
fails closed on the fake seam, and completed session writes use a bounded,
cancellation-aware backoff instead of losing the import to a transient lock.

The same Samsung (`RFCRC0A9GND`, API 36) was clean-installed twice:

| Case | Result |
|---|---|
| Fake beta offer | `2.1.0-beta.1` / code `2010001` offered fake `2.1.1`; the offer was complete, readable and had 48 dp **Update** and **Dismiss** actions. |
| Flexible flow | Download completed and became an **Install** offer. The action button is disabled while its asynchronous step runs, and unit coverage proves a second tap cannot start a second flow. |
| Downloaded update plus active Live | A full-device capture reached 85 lines/s. The lane said to stop the capture and carried **no install action**. **Filters**, **Plot**, **Split**, **Details**, and **Follow** were 48 dp tall with vertically centred content. |
| Stop | The session sealed with 2,703 entries; the foreground service and active notification disappeared, and **Install** returned immediately without a resume. |
| Exact signed Release APK | Clean install reported `versionName=2.0.9`, `versionCode=2000900`, target API 36 and no `DEBUGGABLE` flag. A side-loaded cold start made no Play query and raised no update banner. |
| Manual check on the side-loaded Release | **More → Check for updates…** explained that the file-installed build cannot self-update and offered **Open releases** rather than contacting Play. |
| Release VisualCat-only Live | The foreground data-sync service was visible 865 ms after the final Start tap. Scope guidance immediately explained why an idle own-app capture can be quiet; Stop removed the service and notification. |

`tools/package-android.ps1 -Format both -Version 2.0.9` passed with the real Play
upload key after the password-file correction. Both artifacts declare code
`2000900`; carry API 31–36, arm64-v8a and x86_64; have all 194 native libraries
16 KB aligned; contain the explicit Release permission allowlist with no
`READ_LOGS`; and use upload certificate SHA-256
`a715b0309589aa83dd21548d1959af4bb97b8df06d97fdae32715fbd6530e184`.

| Artifact | Bytes | SHA-256 |
|---|---:|---|
| `VisualCat-Android-v2.0.9.aab` | 35,537,210 | `5b59a74bbf260ce70ad8f7352ab3a8a63884a2f14f5384ec16b61968bb2ff5b1` |
| `VisualCat-Android-v2.0.9.apk` | 35,672,392 | `b9f0c6560838ccc929df3f6faf7dd4f239729251c760c3d124a402772c1c2415` |

The remaining real-Play exclusions above still apply; the fake manager validates
VisualCat's adapter, policy and UI, not Google's production consent surface.

---

## 23. Phone timeline/details splitter — Samsung physical-device pass

This pass implements and verifies
[`MOBILE-TIMELINE-SPLITTER-IMPLEMENTATION-PLAN.md`](../MOBILE-TIMELINE-SPLITTER-IMPLEMENTATION-PLAN.md)
against the Samsung configuration that motivated it. The app was clean-installed
once, then replaced in place with each final Release build so settings migration,
activity recreation and durable split restoration were exercised rather than
simulated.

| Field | Value |
|---|---|
| Date | 2026-08-28 |
| Device | Samsung SM-G990B, serial `RFCRC0A9GND` |
| Android | 16 / API 36, `arm64-v8a` |
| Display | 1080 × 2340 px; native 480 dpi and restored 360 dpi override |
| App | `2.0.9-dev`, code `2000900`, target API 36; locally built Release |
| APK SHA-256 | `b13c20eef8e96c032d044c4308249d53e28ad2a956d2643df57c91758cdf7bdb` |
| Evidence | `artifacts/live-test/mobile-timeline-splitter-20260828/` |

### 23.1 Geometry and allocation

At the device's ordinary 360 dpi override (480 × 1040 dp logical), automatic
Split allocated the timeline itself 472 px / **209.8 dp**, the minimap 86 px /
38.2 dp, and the analysis pane 947 px / 420.9 dp with approximately four entry
rows. The divider's automation node was 216 × 108 px: exactly **96 × 48 dp**,
while its grid lane remained 12 dp. A downward one-finger drag enlarged the
timeline to 788 px / **350.2 dp** and reduced analysis to 631 px / 280.4 dp.

Both stops were exercised. The upper stop left the timeline at exactly 297 px /
**132 dp**; the lower stop enlarged it to 978 px / 434.7 dp while retaining the
analysis chrome plus one usable row. The status band stayed fixed at
`[45,2242][1035,2279]`, and neither pane painted into it. Selecting a warning
cell in the enlarged heat map produced the visible selection marker and
`1 in this bar`, proving that the new target did not consume plot taps.

At native 480 dpi (360 × 780 dp logical), the same node measured 288 × 144 px,
again exactly **96 × 48 dp**. The shorter viewport correctly clamped automatic
timeline height to 132 dp and a drag to the opposite stop reached 158 dp while
preserving one entry row. This is a viewport constraint, not a density-dependent
target or an overpaint.

### 23.2 Persistence and composition transitions

One completed drag produced one durable write. Force-stop/cold launch restored
the exact manual geometry (`[27,587][1053,1565]` timeline and
`[432,1631][648,1739]` divider target). The same share was ignored while the
divider was inapplicable and restored on return across all of these transitions:

- Details → Split and Plot → Split;
- Filters open → closed;
- portrait → wide landscape → portrait;
- Home/background → foreground and process force-stop → cold launch;
- native density → override density after cold recreation; and
- system font scale 1.0 → 1.3 → 1.0.

The divider was absent in Details, Filters and wide side-by-side landscape. At
1.3 system font scale it remained 216 × 108 px / 96 × 48 dp and the stored share
was clamped only for the current chrome. Switching to Insights before reset also
found a device-only stale-presenter case during this pass; the final build now
uses selected-tab state as the authoritative chrome-measurement guard. Resetting
from Insights returned to the same automatic allocation instead of pinning the
plot to 132 dp.

The visible **Appearance & timeline → Reset plot and details split → Apply**
route was exercised on device. A subsequent cold launch reproduced the automatic
geometry, proving that reset persisted as `null`; double tap and **Home** are
covered by physical and headless input respectively.

### 23.3 Timeline, minimap and Android Back gestures

UI Automator injected a genuine two-pointer pinch inside the resized timeline;
the viewport changed from 2.754 s to approximately 198 ms and the minimap brush
narrowed accordingly. A separate minimap drag outside the centred grip moved
the viewport, and Entries/Insights/Entry remained tappable outside the target.
The reverse synthetic pinch was accepted by the injector but rendered as a pan,
so it is not promoted to a separate pinch-in hardware pass; ordinary pinch zoom
and the absence of splitter interference are directly evidenced.

Gesture navigation was temporarily enabled and then restored to the owner's
original three-button mode. The final app publishes full-width exclusion bands,
keeps the minimap whole, and trims the timeline from the top to a total of exactly
200 dp. At the enlarged override-density position Android reported the accepted
lower timeline band plus minimap as
`SkRegion((0,1203,1080,1566)(0,1576,1080,1663))` (the unrelated Samsung edge
handle is a separate region). A right-edge swipe within that protected band
panned from the early two-second window to the late two-second window and kept
VisualCat focused. Background/foreground and cold relaunch both republished the
same region; before the resume fix, Android had cleared it while unchanged
Avalonia bounds caused publication to be skipped.

The top of a plot taller than the remaining platform budget deliberately keeps
Android Back, exactly as the budget policy requires. Back therefore remains
available outside the minimap and protected lower plot band rather than being
disabled for the full workspace.

### 23.4 Automated and build gates

- Focused allocator, state, settings, interaction, accessibility and gesture
  tests: **34 passed**.
- Complete `VisualCat.App.Tests` rerun: **379 passed, 0 failed**.
- Domain: **47 passed**; Core: **101 passed**; Application: **56 passed**.
- Serialized full solution gate: **583 passed, 0 failed**.
- Two unrelated timing assertions observed only under long concurrent runs were
  rerun individually and passed; the complete 379-test UI suite then passed.
- Final Android Release build: **0 warnings, 0 errors**.

The device was left with its original 360 dpi override, font scale 1.0,
three-button navigation, automatic screen rotation, and portrait orientation.

## 24. Splitter drag tracking — Samsung physical-device follow-up

The owner reported that the divider shipped in §23 was "very hard or impossible
to drag it down or up" in ordinary use, which §23's own synthetic gestures had
not reproduced. This pass finds why, fixes it, and re-verifies the feature.

| Field | Value |
|---|---|
| Date | 2026-08-28 |
| Device | Samsung SM-G990B, serial `RFCRC0A9GND` |
| Android | 16 / API 36, `arm64-v8a` |
| Display | 1080 × 2340 px, 360 dpi override (480 × 1040 dp logical) |
| App | `2.0.9-dev`, code `2000900`; locally built Debug, installed in place |
| Navigation | Three-button, as the device was found |

### 24.1 The defect

`Thumb.DragDelta` reports the pointer's offset from the press **in the thumb's
own coordinate space**, and it is cumulative rather than incremental. A probe
against a stationary `MobilePaneSplitter` returned `5, 10, 15` for three 5 dp
moves, not `5, 5, 5`.

The divider summed those vectors. That is only correct while a layout pass lands
between every two pointer events, because the control travels with the boundary
it moves and re-bases the vector each time. Two events inside one frame are
therefore counted twice, and a drag held against a hard stop cannot re-base at
all, so every further event re-adds the whole rejected excursion. §23 never saw
it: `input swipe` and UI Automator deliver gestures slowly enough that a layout
pass always intervenes.

Measured on the device, before the fix, at identical 60 px distances:

| Gesture | Requested | Divider moved |
|---|---:|---:|
| 900 ms drag | +60 px | +60 px |
| 40 ms flick | +60 px | +48 px |
| 30 ms flick | +60 px | **0 px** |
| 25 ms flick | +60 px | **0 px** |

A finger is fast. The feature worked for the injector and not for its owner.

### 24.2 The fix

The divider now measures the pointer in its parent's coordinate space, which
stands still while the panes resize, and reports absolute travel since the
press. Each position is derived from the press baseline instead of summed, so a
coalesced, dropped or duplicated event cannot leave the boundary offset from the
finger. The release position is applied before the drag closes, so a flick whose
moves were coalesced away still lands where the finger finished. A drag is also
no longer answered by recomposing the whole phone layout — only rows 2-5 are
re-resolved — which is what a large live session needs to keep up.

Re-measured on the device:

| Gesture | Requested | Divider moved |
|---|---:|---:|
| 30 ms flick | +60 px | **+60 px** |
| Far-left grab at `x=80`, 400 ms | −70 px | −69 px |
| Far-right grab at `x=1000`, 400 ms | +70 px | +69 px |
| Held 500 px past the stop, returned 100 px | −100 px | −101 px |

The last row is the one the owner could not perform at all: overshooting the
stop used to bank travel the boundary never made and could never give back.

### 24.3 The target is now the whole boundary, not a pill in the middle

A 96 dp target centred on a 480 dp screen asks the reader to find one fifth of
the width. The divider is now full width with a shaped hit area: the visible
20 dp gap between the minimap frame and the tab strip is grabbable across the
whole line, and the marked 96 dp grip additionally reaches the full 48 dp.

This makes the neighbours *better* off than in §23, because the full-width band
lies entirely in the gap. Measured node edges: minimap ends at 1277 px, the
band spans 1278–1323 px, the tab strip begins at 1324 px.

- The minimap keeps every pixel of its own area outside the marked grip. A brush
  drag at `x=300` panned the timeline from 18:34:30 to 18:40:20.
- Entries, Insights and Entry remained tappable, including a tap 6 px inside the
  strip's top edge at `y=1330`.
- The divider's automation node measured 1026 × 108 px — **456 × 48 dp** — so
  the 48 dp accessible height is unchanged.

The grip is also drawn on a full-width hairline now, and its accent state is
limited to keyboard focus: a touch drag used to leave it lit permanently, next
to the tab strip's accent underline, where it read as a selection.

### 24.4 Re-verified feature behaviour

- Maximum plot: timeline 693 px / **308 dp**, past the 214 dp preferred height,
  with the analysis pane keeping its chrome and one entry row.
- One completed drag wrote one value: `"mobileTimelineShare": 0.41604197901049483`.
- Cold relaunch restored the same normalised share.
- Details → Split returned to the identical position, share untouched.
- Rotation to landscape removed the divider from the tree entirely and preserved
  the share; rotating back restored the exact bounds.
- Double tap restored automatic sizing and persisted `"mobileTimelineShare": null`.
- At the enlarged plot, `dumpsys window` reported
  `mSystemGestureExclusion=SkRegion((1038,310,1080,651)(0,916,1080,1280)(0,1291,1080,1377))`.
  The minimap band is whole at 86 px and the timeline is trimmed from the top to
  364 px: **exactly 450 px = 200.0 dp**, against the device's own
  `system_gesture_exclusion_limit_dp=200`. The first rectangle is the unrelated
  Samsung edge handle.

### 24.5 Automated gates

- `MobilePaneSplitTests`: **32 passed**, including two regressions that fail on
  the previous implementation — a drag held against a stop must come straight
  back off it, and the boundary must follow the finger across many moves.
- `VisualCat.App.Tests`: **387 passed, 0 failed**.
- Domain **47**, Core **101**, Application **56**, all passing.
- Desktop and Android builds: 0 warnings, 0 errors.

The device was left with its 360 dpi override, font scale 1.0, three-button
navigation and free rotation, as found.

## 25. Landscape column divider — Samsung physical-device pass

§23 and §24 built the divider for the stacked portrait boundary. The landscape
composition puts the plot and the details in columns instead, and the owner
asked for the same control there. The implementation plan had scoped that out as
"a reasonable follow-up but a different interaction and persistence axis"; this
pass implements it and verifies it on the same device.

| Field | Value |
|---|---|
| Date | 2026-08-28 |
| Device | Samsung SM-G990B, serial `RFCRC0A9GND` |
| Android | 16 / API 36, `arm64-v8a` |
| Display | 1080 × 2340 px, 360 dpi override (1040 × 480 dp logical in landscape) |
| App | `2.0.9-dev`, code `2000900`; locally built Debug, installed in place |

### 25.1 What changed

The root grid's landscape column model went from `21*,29*` to `21*,Auto,29*`.
The middle column is the divider's lane; the analysis pane moved to column 2 and
every band that spans the workspace — the command shell, the filter drawer, the
chip bar, the status line — spans three columns instead of two. Nothing else
about the composition moved.

The divider is the same control on a second axis, so it inherits §24's fix: it
reports absolute travel since the press rather than summing per-event deltas.
Its hit area is the same cross, rotated — the whole boundary line is grabbable
at 20 dp across, and the marked 96 dp grip reaches the full 48 dp.

The two axes keep **separate** stored shares. A height share cannot drive a
width split: portrait is bounded by a readable lane band and entry rows,
landscape by the plot's 88 dp label gutter and the message column beside it.

### 25.2 Measured

The divider's automation node was `[948,210][1056,925]` — **48 dp** wide,
spanning the pane band. Drags tracked the finger exactly:

| Gesture | Requested | Divider moved |
|---|---:|---:|
| 500 ms drag right | +200 px | +200 px |
| 500 ms drag left | −120 px | −120 px |
| 500 ms drag right | +300 px | +300 px |
| 35 ms flick left | −90 px | −90 px |
| 400 ms grab at the top of the line, `y=250` | +150 px | +150 px |
| 400 ms grab at the bottom of the line, `y=880` | −100 px | −100 px |

Both stops hold and release cleanly. At the far right the plot column reached a
1355 px / **602 dp** timeline; at the far left it stopped at 473 px / **210 dp**,
which still draws six labelled severity lanes, two axis labels and the minimap,
and is well clear of the 120 dp width at which `TimelineControl.Geometry()`
refuses to draw.

A swipe starting inside the divider's rectangle but outside the cross — 40 px off
centre, near the top — moved the divider **0 px** in both directions, so the plot
and the details keep their own area everywhere except the marked grip.

### 25.3 The header defect the divider exposed

At the narrow stop the plot header read `DENSITY · 15.72 min · 3.42 s` with the
resolution cut mid-glyph at the plot's right edge. The two header tiers assumed a
plot at least as wide as the narrowest viewport that composes side by side, and
the divider makes a narrower one reachable on purpose. This is the same silent
truncation as F-11, so the header now drops a whole fact rather than part of one:
it renders `DENSITY · 15.72 min` at that width, verified on the device.

### 25.4 Persistence and orientation

- One completed landscape drag wrote one value:
  `"mobileTimelineWidthShare": 0.2412280701754386`, beside an untouched
  `"mobileTimelineShare": 0.4399748688811188`.
- A cold relaunch in landscape restored the divider to the same column.
- Rotating landscape → portrait → landscape returned both dividers to their exact
  positions, each share applied only on its own axis.
- A double tap on the landscape grip reset **only** the width share to `null`;
  the portrait share was untouched.
- **Appearance & timeline** offers its reset whenever either boundary is
  overridden, and clears both.

### 25.5 Automated gates

Re-verified on a second device in section 26.

- `MobilePaneSplitTests`: **36 passed**.
- `VisualCat.App.Tests`: **391 passed, 0 failed**.
- Domain **47**, Core **101**, Application **56**, all passing.
- Desktop and Android builds: 0 warnings, 0 errors.

## 26. Both dividers on a second device — Motorola recheck

§24 and §25 were verified on the Samsung the feature was designed against. This
pass re-runs both axes on a device with a different display, a different density
and a different aspect ratio, to separate the behaviour from that one geometry.

| Field | Value |
|---|---|
| Date | 2026-08-28 |
| Device | Motorola edge 60 pro, serial `ZY22M4T2Z4` |
| Android | 16, `arm64-v8a`, three-button navigation |
| Display | 1220 × 2712 px at 450 dpi — 434 × 964 dp portrait, 964 × 434 dp landscape |
| App | `2.0.9-dev`; upgraded in place over the `2.0.7-dev` build the device carried |

`adb devices -l` listed only this device, and `getprop ro.product.model` /
`ro.serialno` confirmed it before anything was concluded from a measurement.

### 26.1 Portrait

The automatic allocation resolved the timeline to **131.9 dp** — the readable
plot minimum, reached from the opposite direction than on the Samsung, because
this viewport's entries floor wants more than the band can give. The divider's
node spanned the full workspace width at exactly **48 dp**, and its full-width
band again landed in the gap: the minimap ends at 1230 px, the band spans
1232–1289, the tab strip begins at 1289.

| Gesture | Requested | Divider moved |
|---|---:|---:|
| 500 ms drag | +120 px | +121 px |
| 500 ms drag | −70 px | −70 px |
| 500 ms drag | +200 px | +200 px |
| 30 ms flick | −60 px | −60 px |
| 400 ms grab at the far left of the line, `x=60` | −90 px | −90 px |
| 400 ms grab at the far right of the line, `x=1160` | +130 px | +129 px |
| Held 800 px past the stop, returned 150 px | −150 px | −151 px |

### 26.2 Landscape

The column divider measured **48 dp** wide across the pane band. Its grip zone
sits below the tab strip, so the tabs keep their whole area.

| Gesture | Requested | Divider moved |
|---|---:|---:|
| 500 ms drag right | +180 px | +180 px |
| 500 ms drag left | −110 px | −111 px |
| 500 ms drag right | +250 px | +251 px |
| 30 ms flick left | −140 px | −140 px |
| 400 ms grab at the top of the line, `y=460` | +160 px | +159 px |
| 400 ms grab at the bottom of the line, `y=1000` | −120 px | −119 px |

Stops: the plot column reached **526 dp** at one end and **211 dp** at the other,
where the heat map still drew six labelled lanes, two axis labels and the
minimap. At that narrow stop the header read `DENSITY · 1.18 min` — the
resolution dropped whole, confirming §25.3 on a second density.

A swipe starting inside the divider's rectangle but off the cross moved it
**0 px**, and tapping the tab strip beside it switched tabs.

### 26.3 State and platform

- Rotation landscape → portrait → landscape returned both dividers to their exact
  positions (`1614,427` both times), each share applied only on its own axis.
- A cold relaunch in landscape restored the same column.
- Settings held both values apart: `"mobileTimelineShare": 0.4442970822281167`
  and `"mobileTimelineWidthShare": 0.6405367983810842`.
- With the plot at its widest, `dumpsys window` reported
  `mSystemGestureExclusion=SkRegion((0,442,2577,945)(0,953,2577,1012))` — 503 px
  of trimmed timeline plus 59 px of whole minimap, **199.8 dp** against this
  device's own `system_gesture_exclusion_limit_dp=200`.

The device was left on free rotation, as found.
