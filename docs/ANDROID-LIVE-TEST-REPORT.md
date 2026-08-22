# VisualCat — Android live-test report

Live execution of [`ANDROID-LIVE-TEST-PLAN.md`](ANDROID-LIVE-TEST-PLAN.md)
against a physical Android device.

**Status: COMPLETED AS EXECUTED.** Results were written continuously, including
across an interrupted test process, and the final device hand-back is recorded
below. Declared gaps remain gaps rather than implied passes.

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
