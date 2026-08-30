# VisualCat — Windows live test report (host ADB slice, physical Android device)

Live execution of [`WINDOWS-LIVE-TEST-PLAN.md`](WINDOWS-LIVE-TEST-PLAN.md) against a
real Windows host and a physical Android phone over USB ADB. This report is
**context-agnostic**: every device, path, hash, and setting it depends on is
recorded here, so a reader who has never seen the run can reproduce or continue it
without a single fact that exists only in a previous session.

The run is documented **continuously**. §0 is the restore point and is rewritten
after every scenario, so an interrupted run resumes from the last line there.
Findings are appended the moment they are observed.

---

## 0. Restore point — resume here

| Field | Value |
|---|---|
| Run ID | `20260830-windows-adb-samsung` |
| Status | **REMEDIATION COMPLETE** — the original run is complete and every §3 finding is implemented/verified; see §7 |
| Last completed | final remediation gate: build clean, **666/666** tests, connected Samsung/process proof, X-21 and clean diff check |
| Next step | review and commit the intentionally uncommitted working tree when ready |
| Findings | 13 found: 3 Major, 7 Minor, 3 Polish (§3); **13 remediated** (§7.1) |

**To resume.** Start at §7.0; it is the current implementation restore point and
contains the gates, device evidence, exact working-tree state, and remaining
handoff action. Confirm the phone with `adb shell getprop ro.serialno` — never
with `adb devices -l`, which can list a transport that has gone away (§6). No step
depends on a live process, a shell variable, or a fact recorded only in a previous
run.

---

## 1. Run header

### 1.1 What was executed, and why this subset

The plan is a catalogue, not a page-order demand (§12 "Execution schedules"). This
run executed the **ADB smoke** slice plus every other row that needs the physical
phone, because the phone is the surface under test:

| Plan row | Reported in | Verdict |
|---|---|---|
| B-09 Host ADB discovery and three-minute capture | §2.1 | PASS |
| B-10 Stop capture is answered, sticky, and complete | §2.2 | PASS |
| A-16 ADB device-state and topology matrix | §2.3 | PASS |
| A-17 ADB buffers and format negotiation | §2.4 | PASS |
| A-18 ADB pre-roll, duration, and byte limits | §2.5 | PASS |
| A-15 ADB locator precedence | §2.6 | PASS |
| A-19 ADB reconnect and numeric resume cursor | §2.7 | PASS |
| A-20 Device clock and Windows time zone differ | §2.8 | PASS |
| I-05 / I-09 desktop↔CLI parity and simultaneous capture | §2.9 | PASS |
| I-06 Desktop and CLI reconnect semantics | §2.10 | PASS |
| P-19 ADB authority and shared-server boundary | §2.11 | PASS |
| X-08 Ring-buffer pressure and declared loss | §2.12 | PARTIAL |
| X-09 / X-10 Transport gauntlet and rapid cycling | §2.13 | X-09 PARTIAL · X-10 PASS |
| R-01, R-09, R-21, R-26–R-31, R-35, R-48 | §2.14 | 9 PASS · R-27 FAIL · R-29 CLI FAIL |

### 1.1.1 Findings at a glance

| ID | Severity | One line |
|---|---|---|
| [F-01](#f-01--major--live-adb-capture-dialog-three-numeric-fields-have-no-accessible-name-and-their-spin-buttons-announce-avaloniacontrolspathicon) | Major | The capture dialog's three numeric fields have no accessible name; spin buttons announce `Avalonia.Controls.PathIcon` |
| [F-11](#f-11--major--vcat-capture-adb-pins-the-timestamp-policy-to-the-hosts-time-zone-instead-of-the-zone-the-device-agreed-to) | Major | `vcat capture-adb` uses the host time zone, not the negotiated one — R-29 still open on the CLI |
| [F-12](#f-12--major--capture-status-tracks-record-arrival-not-the-transport--a-silent-stream-says-connecting-forever-and-a-vanished-device-still-says-capturing) | Major | Status follows record arrival, not the transport: a silent stream says "Connecting…" forever, a rebooted device still says "Capturing · 49/s" |
| [F-02](#f-02--minor--a-finished-capture-does-not-record-which-buffers-pre-roll-or-logcat-format-it-was-asked-for) | Minor | A finished capture does not record which buffers, pre-roll, or format it asked for |
| [F-04](#f-04--minor--a-device-dropping-off-the-bus-silently-re-points-the-capture-dialog-at-a-different-phone) | Minor | A device dropping off the bus silently re-points the dialog at another phone |
| [F-05](#f-05--minor--closing-the-capture-dialog-leaves-a-hung-adb-devices-child-running) | Minor | Closing the dialog leaves a hung `adb devices` child running |
| [F-06](#f-06--minor--pre-roll-0--the-shipped-default--captures-the-entire-ring-buffer-not-from-now) | Minor | Pre-roll 0 — the shipped default — captures the whole ring: 320,832 entries for a 20 s capture |
| [F-07](#f-07--minor--a-capture-stopped-by-its-own-byte-cap-reports-that-the-log-source-ended-it) | Minor | A capture stopped by its own byte cap says "the log source ended" it |
| [F-08](#f-08--minor--the-byte-cap-cuts-the-last-record-in-half-and-books-it-as-a-parse-defect) | Minor | The byte cap cuts the last record in half and books it as a parse defect |
| [F-13](#f-13--minor--about-15-mb-of-private-bytes-per-capture-cycle-is-not-returned-when-every-session-is-closed) | Minor | ≈15 MB per capture cycle is not returned when every session is closed |
| [F-03](#f-03--polish--1-devices-detected) | Polish | `1 device(s) detected.` |
| [F-09](#f-09--polish--an-unusable-configured-adb-path-is-ignored-without-a-word) | Polish | An unusable configured ADB path is ignored without a word |
| [F-10](#f-10--polish--the-live-status-line-alternates-between-two-different-counts) | Polish | The live status line alternates between "lines received" and "entries" |

### 1.2 Candidate under test

| Field | Value |
|---|---|
| Desktop asset | `artifacts/packages/VisualCat-Desktop-win-x64-v2.0.10.zip` |
| Desktop ZIP SHA-256 | `41F0A5AEC4C4E826FE5C919948C7CF492D9749E93552DF3BC1E39E8CE3890428` (75,992,839 B, 242 entries) |
| CLI asset | `artifacts/packages/VisualCat-CLI-win-x64-v2.0.10.zip` |
| CLI ZIP SHA-256 | `16F178AC09E9E925137EF6C9A45153E4E5111586C86FD4789CE24C64537E2A76` (37,257,147 B, 208 entries) |
| `<VCAT>` | `artifacts/live-test/20260830-windows-adb-samsung/candidate/desktop/VisualCat.exe` |
| `VisualCat.exe` SHA-256 | `059382DED7500E8E19010537D75B29EDF6538F0048E1A7C8406DB244DED45773` |
| `<VCAT-CLI>` | `artifacts/live-test/20260830-windows-adb-samsung/candidate/cli/vcat.exe` |
| `vcat.exe` SHA-256 | `1CD310C77813F829B6F68C3D465830D88DC3B331AF9636E0A4517CF776F90591` |
| File / informational version | `2.0.10.0` / `2.0.10+29c3bd8c5d677c3f27d63e805c7e64029c561c16` |
| Repository commit | `29c3bd8`, five commits past tag `v2.0.10`, `[Unreleased]` non-empty |
| Archive safety (plan §2.4) | 0 absolute, `..`, drive-qualified, or case-colliding entries in either ZIP; flat root; `VisualCat.exe`, `LICENSE`, `THIRD-PARTY-NOTICES.md`, `README.txt` all present |
| Authenticode | unsigned — expected; the product ships as a portable unsigned ZIP |
| Mark-of-the-Web | none (`:$DATA` only); the ZIPs were built locally, not downloaded |

**Deviation from plan §2.4, recorded and not waived.** The plan makes the
*uploaded* release ZIP the release authority. No uploaded ZIP exists for commit
`29c3bd8`, so the candidate was produced by the repository's own documented
packaging path — `pwsh tools/package.ps1 -Runtime win-x64 -Archive` — which emits
the layout the release workflow ships and self-verifies with
`tools/verify-package-contents.ps1`. Every result below is therefore a
**diagnostic result on a release-shaped artifact**, not a release decision. The
rows whose entire point is provenance (B-01 SmartScreen/MotW, I-12 published
archive rehearsal, P-13) were not attempted and are not marked Pass.

### 1.3 Windows host

| Field | Value |
|---|---|
| OS | Windows 11 Pro Insider Preview 10.0.26220, x64, physical (not VM, not RDP) |
| Machine | Gigabyte B550M AORUS PRO-P · AMD Ryzen 9 5900X (12C/24T) · 64 GiB RAM |
| GPU | NVIDIA GeForce GT 1030, driver 32.0.15.6094 |
| Displays | 2 × 3840×2160 at 100% scale; primary `\\.\DISPLAY2` at (0,0), second at (−3840,0) |
| Culture / UI culture / time zone | `cs-CZ` / `en-US` / Central Europe Standard Time (UTC+2 on the run date) |
| Defender | enabled; real-time and behaviour monitoring on; signatures 2026-08-30 |
| Power | Balanced, AC |
| Free space | E: 121.5 GiB (candidate, corpora, evidence); C: 312.3 GiB (product data) |
| PowerShell / .NET | 7 (`pwsh`) / SDK 10.0.400 |
| Token | **elevated (administrator)** — see the deviation below |
| Product data | `%LOCALAPPDATA%\VisualCat`: `settings.json`, `Sessions` (179 pre-existing), `Diagnostics` |
| Starting data profile | **P0/P2 preserved** — the host's real VisualCat directory was left in place |

**Deviation from plan §2.1 and §2.6, recorded.** The run used an elevated account
and the host's existing product data, not a standard user with a clean profile.
`%LOCALAPPDATA%\VisualCat` holds 179 real sessions belonging to the machine's
owner; deleting or moving it is destructive under plan §2.6 and an ADB slice does
not justify it. Consequences: rows that depend on a no-data first run (the P1
cold-start privacy baseline) are outside this run, and no result here is evidence
for the standard-user boundary (P-08).

### 1.4 Android device under test

| Field | Value |
|---|---|
| Model / serial | Samsung **SM-G990B** (Galaxy S21 FE 5G) / `RFCRC0A9GND` |
| Android / API | 16 / 36 |
| Build fingerprint | `samsung/r9qxeea/r9q:16/BP2A.250605.031.A3/G990BXXSKIZF1:user/release-keys` |
| Device time zone / clock | `Europe/Prague`; device UTC agreed with host UTC to the second at run start |
| Transport | USB, state `device`, authorized, transport_id 31 |
| Display | 1080×2340 physical, density override 360 (2.25 px/dp) → 480×1040 dp portrait |
| Ring buffers | main 5 MiB · system 2 MiB · crash 512 KiB · kernel 4 MiB; `events` and `radio` readable and busy; **crash stayed empty for the whole run** |
| Installed companion | `com.barebit.visualcat` present but **not debuggable** (release build) — `run-as` refuses it, so `am crash` cannot be used to fill the crash buffer |
| `<adb>` | `E:\Android\Sdk\platform-tools\adb.exe` — ADB 1.0.41, platform-tools 35.0.1-11580240 |
| `ANDROID_SDK_ROOT` / `ANDROID_HOME` | both unset; ADB is reachable through `PATH` only |

### 1.5 Traffic oracle (plan §3.4)

A device-side producer emitted one record per second into the **main** buffer:

```text
log -p {v|d|i|w|e|f} -t VCatMark "RUN=VCATRUN20260830A SEQ=<n> T=<device epoch>"
```

Every record therefore carries a run id, a monotonic sequence, a severity cycling
through all six levels, and the device's own clock; host UTC start and end were
recorded beside it. **The device's `log` binary (toybox 0.8.12) has no `-b`
option**, so a shell-emitted marker can only reach `main`. The plan anticipates
exactly this ("record any test traffic that the OS refuses to place in a requested
buffer"), so `system`, `events` and `radio` attribution was proved against the
device's own real traffic instead, and `crash` could only be proved negatively:
selected, no records, no error, no divider.

### 1.6 Evidence

`E:\VisualCat\artifacts\live-test\20260830-windows-adb-samsung\` — `candidate\`
(extracted ZIPs), `corpus\`, `evidence\<scenario>\` (screenshots, automation-tree
dumps, producer ledgers), `sessions\`. Screenshots are native 3856×2128 PNGs of
the whole maximized window.

**Automation method.** The desktop was driven through **UI Automation**, selecting
by accessible name and control type as plan §4.4 requires, not by recorded
coordinates. Every control quoted below is one a screen reader also reaches. One
measurement caveat this creates is recorded in §6.

---

## 2. Scenario results


### 2.1 B-09 · Host ADB discovery and three-minute capture — **PASS (with F-01, F-02)**

*Start / end UTC* 2026-08-30T14:33:18Z → 14:37:40Z · attempt-01 · PID 72064 ·
evidence `evidence\B-09\`

**What was done.** From the empty state, activated the command-bar button
`●  ADB live`; sampled the dialog three times two seconds apart; left the default
buffer selection (`main`, `system`, `crash` on; `events`, `radio` off); set
pre-roll to 30 s through the spinner's RangeValue pattern; left both stop limits
at 0; activated `Start capture` while the marker producer had already emitted 27
records; watched the live view; stopped once (§2.2).

**Discovery.** The dialog opened **13 ms** after activation (plan §4.2 budget
≤250 ms) and was populated on first paint — no empty-then-fill flash:

```text
Live ADB capture
  Select an authorized Android device and logcat buffers.
  [combo] RFCRC0A9GND · SM_G990B · Device
  [x] main  [x] system  [x] crash  [ ] events  [ ] radio
  Pre-roll seconds [0]      Stop after minutes (0 = unlimited) [0]  or MiB [0]
  1 device(s) detected. Unauthorized devices must be approved on the device.
  [Refresh devices] [Cancel] [Start capture]
```

Serial, model and state are all shown in one line, which is what B-09 asks for.
Three samples at 14:33:18.497 / 14:33:20.568 / 14:33:22.621 were identical and
correct, and the list never flickered or lost the selection.

**Start-up sequence, from the automation tree (times from the click):**

| +ms | Observed |
|---|---|
| 254 | workspace appears, session tab named `ADB RFCRC0A9GND 16h33m39`, status `Importing…` |
| 666 | `Connecting · ADB device RFCRC0A9GND` |
| 1,217 | `Capturing · 1 entry · ADB device RFCRC0A9GND`, first row rendered |
| 1,662 | `Capturing · 711 lines received · 0/s · ADB device RFCRC0A9GND` |

The discovering → connecting → capturing progression B-09 requires is visible and
truthful, and the first complete record reached the screen 1.2 s after the click —
inside the 2 s budget and without waiting for a second chunk.

**The spawned child process is exactly right** (plan §4.1.6, P-19):

```text
"E:\Android\Sdk\platform-tools\adb.exe" -s RFCRC0A9GND logcat
    -b main,system,crash -D -v threadtime,year,UTC,usec
    -T "2026-08-30 14:33:09.857228"
```

Serial-qualified (never a bare global `adb logcat`), the exact buffers selected,
`-D` for buffer dividers, the richest format ladder the device supports, and a
`-T` cursor that is the click instant minus exactly the 30 s pre-roll.

**Pre-roll was exact.** Click at 14:33:39.857 Z; `-T` cursor 14:33:09.857 Z; first
captured entry **16:33:09.998655** local (= 14:33:09.998 Z), i.e. the first record
that exists at or after the boundary. The producer's `SEQ=38` is the first marker
in the session, and `SEQ=38` was emitted at ≈16:33:10 — one record of framing
allowance, which is what plan A-18 permits.

**Live behaviour over 4 min 31 s** (samples every 15 s):

```text
14:34:04  Capturing ·   1,159 lines received · 15/s   593 in view · 1,027 in session
14:34:49  Capturing ·   2,104 lines received · 19/s   592 in view · 1,929 in session
14:35:35  Capturing ·   3,025 lines received · 13/s   532 in view · 2,776 in session
14:36:06  Capturing ·   3,648 lines received · 16/s   556 in view · 3,324 in session
```

Counts advance monotonically, the session end instant tracks the live edge, and
the four count populations named by R-21 (in view / match the filter / in session /
range) are all on screen rather than hidden in a tooltip.

**Resource cost.** Measured over a clean 20 s window with no automation traffic:
**1.00 s CPU over 20.0 s = 5.0% of one core**, working set stable at 311–362 MiB,
`adb.exe` child 0.11 s CPU and 10 MiB — for a maximized window on a 3840×2160
display redrawing a live timeline. See §6 for why a naive reading of this number
is much worse.

**Integrity, after stop (`vcat verify`, exit 0):**

```json
{ "isValid": true, "issues": [], "entriesChecked": 5058, "sourceRecordsChecked": 5411 }
```

and the counters reconcile line by line with the captured bytes:

| Counter | Value | Independent check on `raw.log` |
|---|---|---|
| `sourceLines` | 5,411 | — |
| `parsedEntries` | 5,058 | — |
| `metaRecords` | 353 | `grep -c '^--------- '` = **353** |
| `unknownLines` / `rejectedCandidates` / `continuations` / `untimedEntries` | 0 / 0 / 0 / 0 | no unaccounted line |
| `outOfOrderEntries` | 3 | expected when two buffers interleave |
| `chattyDeclaredDrops` | 0 | no `chatty` line in `raw.log` |

5,058 + 353 = 5,411 exactly. The meta records break down as 1 × `beginning of
main`, 1 × `beginning of system`, 176 × `switch to main`, 175 × `switch to system`
— that is `-D` working, and it is also the positive evidence for R-31 below.

**Marker oracle — lossless.** The session contains **251 markers, `SEQ=38` through
`SEQ=288`, with zero gaps and no duplicates** (verified twice: `grep -o 'SEQ=[0-9]*'`
over `raw.log` with an awk gap scan, and `vcat search` which reports 251). The
producer emitted 251 records in that window. Nothing was dropped, reordered out of
recoverable order, or invented.

**Process names are plausible** (B-09): the manifest carries 867 pid→name pairs
with first/last-seen instants, sampled by the product running
`adb shell ps -A -o PID,NAME` — visible in the capture itself, since the device
logs `adbd service requested 'shell,v2,raw:ps -A -o PID,NAME'`.

**Why this is still a Pass with two findings.** Every *Fail if* condition in B-09
is negative: the selected device is the one captured, no second capture started,
the first record did not wait for another chunk, and format and time zone are
right. The two findings are an accessibility defect in the dialog (**F-01**) and a
metadata gap in what the finished session remembers (**F-02**).

---

### 2.2 B-10 · Stop capture is answered, sticky, and complete — **PASS**

*UTC* 14:37:39.870Z (click) → 14:37:40.97Z (manifest final) · evidence
`evidence\B-09\B-10-attempt-01-after-stop.png`

`Stop capture` was pressed **once** and never again. Measured by polling the
button's own automation element every 15 ms:

| +ms | Button | Meaning |
|---|---|---|
| 13 | `Stopping…`, disabled | acknowledgement in **13 ms** against a ≤250 ms budget |
| ~1,100 | control gone | replaced by the finalized-session command set |

The status never returned to `Capturing`, the button never sprang back, and the
final state is:

```text
Stopped · 5,058 entries kept
597 in view · 5,058 match the filter · 5,058 in session ·
08-30 16:33:09.998655 — 08-30 16:37:40.835734
```

- **Live-only controls disappear.** After stop, `Stop capture` and `Follow: on` are
  no longer in the automation tree at all, while `Save`, `Save portable` and
  `Export` became enabled. That is R-28 ("Follow belongs only to the active
  source") and R-35 ("session commands disable honestly") holding.
- **No orphan child.** The `adb … logcat` child (PID 85036) was gone from
  `Win32_Process` at the first post-stop sample; only the shared `adb fork-server`
  and my own unrelated producer shell remained. R-01 and the P-19 clause "stopping
  capture kills only its logcat child" both hold.
- **Committed counts did not change on reopen** and `vcat verify` passed on the
  finalized directory (§2.1).
- **Finalization was fast**: manifest `updatedUtc` 14:37:39.970Z, 100 ms after the
  click, for a 5,058-entry / 653,726-byte session.

One measurement note, kept because the plan forbids overwriting a first
observation: a *cached* AutomationElement handle for the button reported the name
reverting to `Stop capture` with `enabled=True` at +218 ms, which would be an R-01
violation if the UI really did that. It does not — the control is absent from a
freshly walked tree afterwards, and the re-test with a fresh lookup on every poll
is §2.13. The cached-handle reading is an artifact of Avalonia recycling the
automation peer behind a handle the test still held.

---


### 2.3 A-16 · ADB device-state and topology matrix — **PASS (with F-04, F-05)**

*UTC* 14:46–14:52Z · evidence `evidence\A-16\`

A single physical phone cannot safely present half of this matrix: revoking USB
debugging authorization to reach the `unauthorized` state locks the host out, and
the only way back is a tap on the phone that an unauthorized transport cannot
deliver. So the states were presented through a **stub `adb.exe`** driven by a
control file (source in this run's `rig\`), which answers `version` and
`devices -l` in exactly the byte format `AdbDeviceParser` consumes. Every state
below was set while the dialog was open, then left for at least two refresh
intervals.

The refresh interval was measured directly from the stub's own call log:
`14:46:20.611` and `14:46:22.583` — **1.97 s**, matching the documented 2 s.

| # | Presented state | Selected device shown | `Start capture` | Verdict |
|---|---|---|---|---|
| 01 | no devices | *(empty)* | **disabled** | ✓ `No devices detected. Connect a device and enable USB debugging, then refresh.` |
| 02 | one authorized | `RFCRC0A9GND · SM_G990B · Device` | enabled | ✓ |
| 03 | unauthorized | `RFCRC0A9GND · Android device · Unauthorized` | **disabled** | ✓ state surfaced; model falls back to `Android device` because `adb` reports none for an unauthorized transport |
| 04 | offline | `RFCRC0A9GND · SM_G990B · Offline` | **disabled** | ✓ |
| 05 | unknown (`recovery`) | `RFCRC0A9GND · SM_G990B · Unknown` | **disabled** | ✓ an unmodelled state degrades to Unknown rather than to Device |
| 06 | emulator + physical | `RFCRC0A9GND · SM_G990B · Device` | enabled | ✓ selection held on the previously chosen serial |
| 07 | two physical | `RFCRC0A9GND · SM_G990B · Device` | enabled | ✓ selection held by exact serial |
| 08 | selected serial disappears | `R5CT21XPTEST · SM_S908B · Device` | enabled | **F-04** — silently re-pointed at the other phone |
| 09 | original serial returns | `R5CT21XPTEST · SM_S908B · Device` | enabled | consistent with 08; the original is not restored |
| 10 | serial changes entirely | `RFCRC0A9GNX · SM_G990B · Device` | enabled | same shape as 08 |

Rows 01–07 are exactly what A-16 demands: the two-second refresh preserves the
selection by exact serial while the serial is present, disables Start for every
non-capturable state, and surfaces the real state rather than a guess. Rows 08–10
are **F-04**.

**Hung discovery.** With the stub made to sleep 60 s inside `devices -l`:

- the dialog showed `Discovering devices…` — truthful, not a frozen list;
- exactly **one** discovery child existed at any moment, sampled every 4 s for
  20 s — the refresh loop waits for the previous call instead of piling up;
- `Cancel` closed the dialog in **32 ms**, and the dialog's own system close
  (`WindowPattern.Close`) in **211 ms** — both inside the ≤250 ms budget;
- no late callback reopened or repopulated the closed dialog;
- reopening after the stub recovered showed the device correctly — discovery still
  works after a hang;
- **but the hung `adb devices -l` child survived the close** (**F-05**).

---

### 2.4 A-17 · ADB buffers and format negotiation — **PASS**

*UTC* 14:54–14:59Z · real device, real traffic · evidence `evidence\A-17\`

| Selection | Child process arguments | Buffers attributed in the session |
|---|---|---|
| `main,system,crash` (default) | `-b main,system,crash -D -v threadtime,year,UTC,usec` | `main`, `system` (crash silent) |
| `events,radio` | `-b events,radio -D …` | `events`, `radio` |
| all five | `-b main,system,crash,events,radio -D …` | `main`, `system`, `events`, `radio` (crash silent) |
| **none** | *not started* | dialog stays open: **`Select at least one buffer.`** |

**Buffer attribution is exact per record (R-31).** For the all-five capture the
`-D` dividers in `raw.log` were walked independently to rebuild the expected
buffer for every byte offset, then compared against the buffer the product stored
for each entry (`vcat query`, matched by `raw.offset`, not by row order):

```text
records cross-checked : 883
offset not a record   : 0
buffer mismatches     : 0
expected distribution : {'main': 655, 'radio': 188, 'system': 34, 'events': 6}
```

**Zero mismatches across four interleaved buffers.** The divider arithmetic
reconciles too: 4 `beginning of` + 5 + 62 + 46 + 26 `switch to` = 143 = the
manifest's `metaRecords`, and 883 + 143 = 1,026 = `sourceLines`.

**Format negotiation.** The ladder is `threadtime,year,UTC,usec` →
`threadtime,year,UTC` → `threadtime,year,usec` → `threadtime,year` →
`threadtime`, probed functionally because `logcat -v help` is rejected as an
invalid format on current devices. This Samsung accepted the richest rung on
every one of the run's captures, so the degraded rungs were **not exercised
live** — recorded as an untested cell, not as a pass. The consequence of landing
on the richest rung is visible and correct: timestamps carry the year and
microseconds (`2026-08-30 15:07:17.998983 +0000`), `timestampPolicy.timeZoneId`
is `UTC`, and `timestampProvenance` is `ExplicitUtc`, so R-29 (a degraded UTC
modifier must not shift every timestamp) holds on this rung by construction.

`crash` was requested in two captures and produced no records and no divider,
because the device's crash ring was empty for the whole run and the installed
companion is a non-debuggable release build, so `am crash` cannot fill it. That
is a **negative** result only: crash selection neither failed nor produced a
spurious attribution, and the positive case is untested here.

**Not reachable on this device:** an unsupported buffer or format, which A-17
expects to "fail with device/buffer detail". This phone supports all five buffers
and the richest format, so no failure could be provoked without a second device.

---

### 2.5 A-18 · ADB pre-roll, duration, and byte limits — **PASS (with F-06, F-07, F-08)**

*UTC* 14:33–15:09Z · evidence `evidence\A-18\`

| Case | Configuration | Result |
|---|---|---|
| Pre-roll non-zero | 30 s | `-T "2026-08-30 14:33:09.857228"` = click − exactly 30 s; first entry `14:33:09.998` — **exact within one record** |
| Pre-roll non-zero | 5 s | `-T` = click − exactly 5.000 s in all four captures that used it |
| **Pre-roll zero** | 0 s (**the shipped default**) | **no `-T` at all** → the entire ring buffer: **F-06** |
| Duration only | 1 min | auto-stopped at **+61.3 s**; `Stopped · this capture ran its full duration · 1,031 entries kept` |
| Byte only | 1 MiB | auto-stopped at **exactly 1,048,576 bytes**, but the reason is misreported (**F-07**) and the last record is cut in half (**F-08**) |
| Both | 1 min + 1 MiB | duration won at +61.6 s and was **named**: `…ran its full duration…` |

**Automatic stop uses the same finalization path as manual Stop**, which A-18
requires: in all three limit cases the `Stop capture` control disappeared, the
`adb` child count owned by the app returned to **0**, the manifest was finalized,
and the session verified. No overflow or wrap appeared at large accepted values
(the spinners clamp at 3600 s pre-roll, 10080 minutes, 1048576 MiB).

---

### 2.6 A-15 · ADB locator precedence — **PASS**

*UTC* 14:52–15:00Z · six labelled stub `adb.exe` builds, one per rung; each logs
its own label when invoked, so the winner is unambiguous.

| Case | Environment | Stub actually invoked | Expected |
|---|---|---|---|
| F1 | settings `adbPath` valid + `ANDROID_SDK_ROOT` + PATH | **explicit** | explicit ✓ |
| A | `ANDROID_SDK_ROOT` + `ANDROID_HOME` + PATH | **sdkroot** | `ANDROID_SDK_ROOT` ✓ |
| B | `ANDROID_HOME` + PATH | **home** | `ANDROID_HOME` ✓ |
| D | `%LOCALAPPDATA%\Android\Sdk` + PATH | **localappdata** | LocalAppData SDK ✓ |
| C | PATH only | **path** | PATH ✓ |
| E | nothing anywhere | *(none)* | actionable absence ✓ |
| F2 | settings `adbPath` **invalid**, rogue `adb.exe` in the working directory, PATH present | **path** | PATH, never the working directory ✓ |

Case E produced, verbatim, in the notice lane rather than an inert dialog:

```text
ADB was not found. Install Android platform-tools or set ANDROID_SDK_ROOT.
```

That satisfies R-48. Case F2 is the security-relevant rung and it is clean: the
rogue `adb.exe` sitting in the process working directory was **never invoked**
(its call log stayed empty), because the locator only ever composes absolute
candidates from `PATH` entries and this host's `PATH` contains no relative entry.
An invalid explicit path silently falls through to auto-detection, which is
**F-09**.

---


### 2.7 A-19 · ADB reconnect and numeric resume cursor — **PASS**

*UTC* 15:12–15:16Z · real device, real interruption · evidence `evidence\A-19\`

**Attempt 01 — `adb reconnect device`: not an interruption.** The device logged
`adbd service requested 'reconnect'`, but the running logcat child kept its PID
and the stream never broke. Recorded because it is a useful negative: this command
does not exercise the reconnect path.

**Attempt 02 — `adb kill-server` under marker traffic.** This does break the
stream, and the product handled it cleanly:

| Observation | Value |
|---|---|
| capture start / interruption | 15:14:45.7Z / 15:15:11.1Z |
| logcat child before → after | PID 51792 → **81960**, respawned within **0.5 s** |
| `defects.reconnectGaps` | **1** — the gap is counted, distinctly from every other defect |
| markers captured | `SEQ 5 … 33`, 29 records |
| **sequence gaps** | **none** |
| **duplicates** | **none** |
| `unknownLines` / `rejectedCandidates` | 0 / 0 |
| session | finalized, `degraded: false`, verifies |

The resume cursor is a numeric `-T` timestamp taken from the last genuine complete
record, and the bounded overlap it creates was **de-duplicated exactly**: markers
are emitted once per second and span the cut, so a duplicate would have been
visible at one-second resolution. Sixteen duplicate raw lines exist in the file,
but all carry a timestamp 18 s *before* the cut — a device-side block the phone
logged twice — so they are source content, not reconnect artefacts.

The policy in code matches what was observed: at most **5 attempts**, backoff
`250 ms · 2^(n−1)` capped at 10 s, `reconnectGaps` incremented per attempt.

**Not reachable on this device:** revoke/re-authorize (revoking USB debugging
authorization requires a tap on the phone to undo, and an unauthorized transport
cannot deliver one — the host would be locked out) and device-clock rollback
(`date -s` returns `Operation not permitted` for the shell user, and this is a
non-rooted retail device). Both are recorded as untested cells.

---

### 2.8 A-20 · Device clock and Windows clock/time zone differ — **PASS**

*UTC* 15:17–15:19Z · host set to **Tokyo Standard Time** (UTC+9), device left on
**Europe/Prague** (UTC+2) · evidence `evidence\A-20\`

| Assertion | Observation |
|---|---|
| pre-roll cursor unaffected by host zone | `-T "2026-08-30 15:17:57.294505"` = click − 5.000 s, in UTC |
| session information names the render policy | **`Times shown in: Tokyo Standard Time · captured as UTC`** |
| session information names the source | `Source: Adb · ADB RFCRC0A9GND 00h18m02`, `Format: ThreadTime` |
| desktop instant | `08-31 00:17:57.406846` local = `2026-08-30T15:17:57.406846Z` |
| CLI instant for the same record | `originalTimestamp: 2026-08-30 15:17:57.406846 +0000` |
| **desktop and CLI agree** | **exactly, to the microsecond** |
| manifest policy | `timeZoneId: UTC`, `timestampProvenance: ExplicitUtc` |

The desktop renders in the *host's* zone and says so in one sentence, which is the
honest arrangement: the instant is the device's, the label is the reader's, and
the pane states both. Host time zone was restored to Central Europe Standard Time
immediately (§5).

One behaviour worth knowing, observed while restoring: **a running instance keeps
the zone it started with.** After the host zone was set back to CET, the already
running app continued to name sessions in Tokyo local time (`ADB … 00h47m59` for a
17:47 CET capture); a restart picked up the new zone. Plan row X-27 (clock/zone
changes during live work) belongs to the Soak schedule and was not otherwise
executed, so this is recorded as an observation rather than a scenario result.

---

### 2.9 I-05 / I-09 · Desktop↔CLI ADB parity, and simultaneous capture — **PASS (with F-11)**

*UTC* 15:20–15:22Z · both surfaces capturing the **same device at the same time**

Running both at once is the stronger form of I-05: because the windows overlap,
the same physical log record can be found in both sessions by marker identity and
compared field by field, which sequential captures cannot do.

| | desktop | CLI |
|---|---|---|
| own logcat child | 72944 (parent 71080) | 83620 (parent 60472) |
| markers captured | `SEQ 4 … 74` (71) | `SEQ 1 … 68` (68) |
| sequence gaps | none | none |
| entries | 1,687 | 252,911 |
| exit / final state | stopped by user, finalized | exit code 0, path printed on stdout, stderr empty |

**Overlapping markers: 65 (`SEQ 4 … 68`). Every stored field matches on every one
of them** — `pid`, `tid`, `level`, `tag`, `buffer`, `message`, `originalTimestamp`,
`timestampProvenance`, `timestampConfidence`, `format`, `parserVersion`.

**I-09 assertions all hold:** each surface spawned and owned its own logcat child
against the same serial; neither stole nor killed the other's transport; the CLI
finishing did not stop the desktop capture (verified explicitly after the CLI
exited); and neither assumed exclusive global ADB ownership.

The entry-count difference is fully explained and is not waved away: the CLI has
**no pre-roll option**, so its capture began with the whole `main` ring
(252,911 entries, 6 h of history) while the desktop used a 5 s pre-roll. That is
the missing-option case the plan asks to be named rather than papered over, and it
is the same underlying behaviour as **F-06**.

**Manifest source properties diverge in one field**, and it matters: the desktop
recorded `timestampPolicy.timeZoneId = UTC`, the CLI `Central Europe Standard
Time`, for the same device at the same moment — **F-11**.

---

### 2.10 I-06 · Desktop and CLI reconnect semantics — **PASS**

*UTC* 15:26–15:28Z · the same `adb kill-server` schedule applied to a CLI capture

| | desktop (§2.7) | CLI |
|---|---|---|
| logcat child respawned | 51792 → 81960 | 38564 → 34260 |
| `defects.reconnectGaps` | 1 | **1** |
| markers | `SEQ 5…33`, no gaps, no duplicates | `SEQ 1…36`, **no gaps, no duplicates** |
| `unknownLines` / `rejectedCandidates` | 0 / 0 | 0 / 0 |
| final state | finalized, not degraded | finalized, not degraded, exit code 0 |

Data semantics are identical, which is what I-06 requires. Two presentational
differences, both acceptable under "differences in user-facing text are
acceptable": the CLI prints nothing about the reconnect (the desktop shows it in
the live view), and the CLI kept both `--------- beginning of main` dividers
(`metaRecords: 2`) where the desktop kept one (`metaRecords: 1`) — each is
internally consistent with its own byte stream.

---

### 2.11 P-19 · ADB authority and shared-server boundary — **PASS**

The device's own log is the oracle here: `adbd` records every service request it
receives, so the complete list of what VisualCat asked this phone to do can be
read back from the phone rather than inferred from the host.

Over the whole run, VisualCat issued exactly three kinds of request:

```text
 40 ×  shell,v2,raw:ps -A -o PID,NAME
 12 ×  shell,v2: exec logcat '-d' '-b' '<selected buffers>' '-v' 'threadtime,year,UTC,usec' '-t' '1'
  n ×  shell,v2: exec logcat '-b' '<selected buffers>' '-D' '-v' 'threadtime,year,UTC,usec' ['-T' '<cursor>']
```

- **Never** `logcat -c` (clear), `logcat -G` (resize), `setprop`, `settings put`,
  `svc`, `pm`, `am`, `su`, or any file transfer. The buffer state the run found is
  the buffer state the product left.
- **Every** invocation is `-s <serial>`-qualified; not one bare global `adb logcat`
  appeared, so a second device could never be captured by accident.
- **No device was authorized silently**: the `unauthorized` state disables Start
  (§2.3) and nothing in the request log attempts to change it.
- **No shared server is killed on normal start.** VisualCat never ran
  `kill-server`; the two that appear in this run were issued by the test harness
  and are in the ledger (§5).
- **Stopping kills only its own logcat child** (§2.2, §2.5, §2.13: 40 capture
  cycles, zero surviving children). The one process-lifetime defect found is on
  the *discovery* path, not the capture path — **F-05**.
- **No undeclared secret is persisted.** The session stores the serial (declared,
  and necessary to identify the source) and the log bytes themselves. The only
  identifier-like content in the captured bytes came pre-redacted from the device
  (`redacted-pii:imsi[chars:15]`).

---

### 2.12 X-08 · Ring-buffer pressure and declared loss — **PARTIAL**

*UTC* 15:29–15:32Z · `main` ring shrunk from **5 MiB to 64 KiB** for the test and
restored afterwards (§5)

Eight concurrent device-side writers pushed **3,200 records of ~900 bytes
(≈2.9 MB) through a 64 KiB ring** — 45× oversubscribed — while a live capture ran.

```text
writer 1: 400/400 captured, range 1-400, 0 gap-runs
writer 2: 400/400 …   (all eight identical)
flood records captured: 3200 of 3200 emitted
chatty lines in the capture: 0
defects: {'outOfOrderEntries': 57}
```

**Nothing was lost**, and the loss counters correctly stayed at zero because there
was no loss to declare. The product never claimed losslessness in the abstract:
`chattyDeclaredDrops`, `reconnectGaps` and `sourceChanges` are separate manifest
counters, so a declared drop has somewhere distinct to go.

**Why this is Partial, not Pass:** the `chatty` declaration path could not be
exercised. This Android 16 device emitted no `chatty` line under any pressure the
test could apply — modern `logd` no longer writes them at this rate — so the
assertion "declared drops are counted distinctly from source and reconnect gaps"
is proved only for the two gap kinds that did occur. A device that still emits
`chatty` is needed to close the row.

---

### 2.13 X-09 / X-10 · Transport gauntlet and rapid cycling — **X-09 PARTIAL (F-12), X-10 PASS**

**X-09 mutations attempted** (each its own pass):

| Mutation | Result |
|---|---|
| kill/restart the ADB server | §2.7 / §2.10 — bounded reconnect, gap counted, no loss |
| `adb reconnect device` | no interruption occurs; recorded as a negative |
| **restart the device** (`adb reboot`) | **F-12** |
| introduce another device / serial change | §2.3 via the stub |
| unplug/replug USB, switch USB mode, revoke authorization, Wi-Fi transport | **not attempted** — they need physical access to the phone or create a state the host cannot undo remotely |

**X-10, 40 capture cycles** (32 + 8 after a full tab close), alternating immediate
stop, one-second, five-second and permanently-silent captures, each with a fresh
automation lookup on every poll:

| Assertion | Result |
|---|---|
| every capture stopped | **40 / 40** |
| stop acknowledgement | 88–464 ms, median ≈200 ms |
| **stop sprang back** | **0 of 40** — settles the §2.2 measurement caveat: R-01 holds |
| adb child left behind | **0 of 40** |
| unique session directory per capture | **40 / 40**, no collisions (R-09) |
| short/empty captures finalize | 8 of 8 verified `isValid: true`, including 4 with **zero entries**, all `finalized: true`, `state: Ready` (R-26) |
| threads | 113 → 129 with tabs open, **back to 113** after closing them |
| handles | 1,496 → 3,276 with 38 tabs open, **down to 1,028** after closing them — fully released |
| private bytes | 476 MB → 830 MB → **861 MB after closing every tab** → 1,036 MB after 8 more cycles — **F-13** |

---


### 2.14 Tier R — ADB regression guards

| Guard | Verdict | Evidence |
|---|---|---|
| **R-01** Stop is answered and sticky | **PASS** | 40 stop cycles with a fresh automation lookup on every poll: 40/40 stopped, **0 sprang back**, acknowledgement 88–464 ms, status never returned to `Capturing` (§2.2, §2.13) |
| **R-09** Capture names distinguish runs | **PASS** | 40 captures produced 40 unique session directories and tab names of the form `ADB RFCRC0A9GND 17h05m13` (§2.13) |
| **R-21** Counts name their population | **PASS**, with **F-10** | `597 in view · 5,058 match the filter · 5,058 in session · <range>` is on screen, not in a tooltip; the *live* status line alternates between two populations (F-10) |
| **R-26** Short captures finalize | **PASS** | 8 of 8 sampled cycle sessions `isValid: true`, including 4 with **zero entries**, all `finalized: true`, `state: Ready` (§2.13) |
| **R-27** Quiet status stops claiming arrivals | **FAIL** | A silent stream stays `Connecting to the device…` indefinitely; a vanished device keeps `Capturing · 720 lines received · 49/s` for 150 s — **F-12** |
| **R-28** Follow belongs only to the active source | **PASS** | After stop, `Stop capture` and `Follow: on` leave the automation tree entirely while Save/Save portable/Export become enabled (§2.2) |
| **R-29** ADB time zone follows the negotiated format | **PASS (desktop) / FAIL (CLI)** | Desktop records `UTC` from the negotiated ladder; `vcat capture-adb` records the host zone for the same device at the same moment — **F-11** |
| **R-30** Wrong/unknown serial cannot hang | **PASS at start**, gap on reconnect | Starting against a serial that had just disappeared failed in ~4 s with `Device 'RFCRC0A9GND' was not found (no devices are connected). Connect the device and enable USB debugging.` and spawned **zero** logcat children. The same preflight is not repeated before a re-spawn inside the reconnect loop — **F-12** |
| **R-31** Buffer attribution is per record | **PASS** | 883 records across four interleaved buffers cross-checked by byte offset against an independent `-D` divider walk: **0 mismatches** (§2.4) |
| **R-35** Session commands disable honestly | **PASS** | `Save`, `Save portable`, `Export` are disabled in the empty state and enabled once a session exists (§2.1, §2.2) |
| **R-48** ADB missing message is actionable | **PASS** | `ADB was not found. Install Android platform-tools or set ANDROID_SDK_ROOT.` in the notice lane; no inert dialog opens (§2.6) |

---


## 3. Findings

Severity follows plan §13.3: **Blocker** prevents a verified launch, loses or
corrupts data, or escapes a trust boundary; **Major** breaks a primary workflow,
gives silent wrong results, or makes the product unusable with a required
accessibility mode; **Minor** has a bounded workaround; **Polish** is perceptible
without impeding completion.

---

### F-01 · Major · Live ADB capture dialog: three numeric fields have no accessible name, and their spin buttons announce `Avalonia.Controls.PathIcon`

*First observed* §2.1 (B-09) · *layer* view model / Avalonia automation peers

The dialog's three numeric controls — pre-roll seconds, stop-after minutes, and
stop-after MiB — expose **no accessible name and no `LabeledBy` relationship**. The
visible `TextBlock` beside each one is a sibling in the visual tree with nothing
tying it to the control. Their inner increment/decrement buttons expose the
framework type name as their accessible name.

Verbatim from the UI Automation tree (`evidence\B-09\`):

```text
[Text]    name="Pre-roll seconds"
[Spinner] name="" id="" range=0/[0..3600]
  [Button] name="Avalonia.Controls.PathIcon" id="PART_IncreaseButton"
  [Button] name="Avalonia.Controls.PathIcon" id="PART_DecreaseButton"
  [Edit]   name="" id="PART_TextBox" value="0"
[Text]    name="Stop after minutes (0 = unlimited)"
[Spinner] name="" range=0/[0..10080]
[Text]    name="or MiB"
[Spinner] name="" range=0/[0..1048576]
```

Probed directly: `LabeledBy` is null and `HelpText` is empty for all three
spinners, all five buffer checkboxes, and the device combo box.

**Failure scenario.** A Narrator user tabbing through the capture dialog hears
"spin button, 0 … spin button, 0 … spin button, 0" and cannot tell the pre-roll
from the two stop limits. Setting a byte cap instead of a minute cap changes what
the capture does. The dialog's own text is on screen, but a screen reader reaches
it only as unrelated static text, so the association a sighted user makes by
proximity is unavailable. Plan §4.4 requires state and purpose to be
distinguishable "visually **and** through UI Automation"; R-34's principle (a
screen reader hears meaning, not dumps) is contradicted twice over by an
accessible name of `Avalonia.Controls.PathIcon`.

**Fix.** Two independent changes, both small:

1. Set `AutomationProperties.Name` on each `NumericUpDown` (`"Pre-roll seconds"`,
   `"Stop after minutes"`, `"Stop after megabytes"`) or, better,
   `AutomationProperties.LabeledBy={Binding ElementName=...}` pointing at the
   existing label so the on-screen text stays the single source of truth. The
   combo box needs the same treatment ("Android device").
2. Give the spin buttons real names in the control template
   (`"Increase"`/`"Decrease"`) — `Avalonia.Controls.PathIcon` is a leaked type
   name, and every `NumericUpDown` in the product inherits it, so fixing it in one
   shared style fixes the settings sheets too.

Worth a guard test: assert that no accessible name in the shipped UI matches
`^Avalonia\.`, which is cheap and catches the whole class.

---

### F-02 · Minor · A finished capture does not record which buffers, pre-roll, or logcat format it was asked for

*First observed* §2.1 (B-09) · *layer* store / session manifest

B-09 requires that "metadata records serial, buffers, negotiated format/time zone
and source kind". Three of those four are present in `manifest.json`; **the
requested buffer set is not, and neither is the pre-roll or the negotiated format
ladder**:

```jsonc
"descriptor": {
  "sourceKind": 2,                                  // Adb            ✓
  "sourceDescription": "ADB device RFCRC0A9GND",    // serial         ✓
  "detectedFormat": 1,                              // ThreadTime     ~
  "timestampPolicy": { "timeZoneId": "UTC", … }     // time zone      ✓
}
"buffers": ["", "main", "system"]                   // *observed*, not requested
```

`buffers` is the per-record attribution dictionary — the values that actually
appeared. `crash` was selected for this capture and is absent from it because the
crash ring stayed empty. The command line that ran was
`-b main,system,crash -D -v threadtime,year,UTC,usec -T <cursor>`, and none of
`main,system,crash`, `30` (pre-roll), or `threadtime,year,UTC,usec` survives
anywhere in the session.

**Failure scenario.** Someone reopens a capture next week, sees no `crash`
records, and cannot tell whether the crash buffer was quiet or was never
requested. The same ambiguity defeats the loss accounting the plan asks for in
X-08: `chattyDeclaredDrops` and `reconnectGaps` are recorded honestly, but without
the requested buffer set a zero in a buffer is unattributable. It also blocks an
exact desktop↔CLI parity claim (I-05), since neither side can state its own
configuration from the artifact alone.

**Fix.** Add a `captureSettings` object beside `ingestSettings` in the descriptor —
`requestedBuffers`, `preRollSeconds`, `durationLimit`, `byteLimit`,
`negotiatedLogcatFormat`, `adbVersion`, and the device `model`/`fingerprint` that
`adb devices -l` already returns. All are known at start and none is sensitive
beyond the serial already stored. Surface them in `vcat info` and in the desktop's
session-information pane, where the reader is actually asking the question.

---

### F-03 · Polish · `1 device(s) detected.`

*First observed* §2.1 (B-09) · *layer* view model

The capture dialog's status line reads, verbatim,
`1 device(s) detected. Unauthorized devices must be approved on the device.` The
`(s)` is a placeholder plural in a product whose other counts are written properly
("5,058 entries kept", "1 entry"). Use the count-aware string the rest of the
product already uses: `1 device detected.` / `2 devices detected.`

---


### F-04 · Minor · A device dropping off the bus silently re-points the capture dialog at a different phone

*First observed* §2.3 (A-16 rows 08–10) · *layer* view model

With two devices listed and `RFCRC0A9GND` selected, removing that serial from the
`adb devices` output moved the selection to the *other* phone within one refresh,
left `Start capture` enabled, and said nothing:

```text
before  selected='RFCRC0A9GND · SM_G990B · Device'   2 device(s) detected.
after   selected='R5CT21XPTEST · SM_S908B · Device'  1 device(s) detected.
```

When the original serial came back, the selection stayed on the substitute.

**Failure scenario.** A tester with two phones on one host picks the one under
test, sets buffers and a pre-roll, and gets interrupted. The USB hub drops that
phone for two seconds — routine on a hub, and exactly what a flaky cable does.
They come back, press `Start capture`, and record the wrong device. Nothing in
the dialog says the selection changed; the only difference is a serial they were
not looking at. A-16 asks that the refresh "never silently switches a capture to
another sole device", and while a *running* capture is safe (it is bound to
`-s <serial>` and A-19 confirms it), the pre-start selection is not.

**Fix.** Treat a disappearing selected serial as a state change worth a sentence.
Either keep the serial listed as `RFCRC0A9GND · not connected` with `Start`
disabled until it returns or the user picks another device — the option that
matches the rest of the dialog's honesty about states — or auto-select and say
`RFCRC0A9GND disconnected — selection moved to R5CT21XPTEST` in the status line
that is already there. Restoring the original selection when its serial returns
would also match what "preserves selection by exact serial" implies.

---

### F-05 · Minor · Closing the capture dialog leaves a hung `adb devices` child running

*First observed* §2.3 (A-16) · *layer* infrastructure / process lifetime

With device discovery wedged (stub `adb.exe` sleeping inside `devices -l`), the
dialog closed promptly — 32 ms via `Cancel`, 211 ms via the window's system close
— but the discovery child outlived it:

```text
ProcessId       : 61832
ParentProcessId : 80248          <- VisualCat
CommandLine     : "…\rig\path\adb.exe" devices -l
still alive after dialog closed +8s, +33s … until its own 60 s sleep ended
```

Plan §4.2 is explicit that "a dialog that outlives its own child is a finding",
and A-16 requires the close to "terminate the discovery child".

**Mitigating facts, established in the same test:** exactly one discovery child
exists at a time — the 2 s refresh waits for the previous call rather than
spawning a new one every interval — so this leaks one process per wedged dialog,
not a process per tick. Discovery also recovers cleanly once ADB is healthy
again. And the *capture* child is handled correctly: it is killed on Stop
(§2.2) and on every automatic stop (§2.5).

**Failure scenario.** A wedged ADB server or a device in a bad USB state makes
`adb devices` block — the ordinary reason someone opens this dialog and gives up.
Each open-then-close leaves an `adb.exe` behind holding a transport, and the user
sees no sign of it. Repeat while fighting a bad cable and the leftovers
accumulate, keeping the very ADB server the user is trying to restart alive.

**Fix.** Give discovery the same treatment the capture child already gets: run it
under a `CancellationToken` tied to the dialog's lifetime, and on cancel kill the
process rather than only abandoning the await. A discovery call that has not
returned within a few seconds could additionally surface as
`ADB is not responding — the device list may be stale`, which is more useful than
an indefinite `Discovering devices…`.

---

### F-06 · Minor · Pre-roll 0 — the shipped default — captures the entire ring buffer, not "from now"

*First observed* §2.5 (A-18) · *layer* infrastructure / view model

The capture dialog offers `Pre-roll seconds` defaulting to `0`, and
`settings.json` ships `"defaultCapturePreRollSeconds": 0`. A non-zero value
becomes a `-T <click − preroll>` cursor, which is exact. **Zero omits `-T`
entirely**, and `adb logcat` with no cursor dumps everything the ring still holds
before it starts following:

```text
childCmd: adb -s RFCRC0A9GND logcat -b main,system,crash -D -v threadtime,year,UTC,usec
          (no -T)
```

Measured on the real device with the shipped defaults, holding the capture for
**20 seconds**:

| | |
|---|---|
| entries | **320,832** |
| session bytes | **44.4 MiB** |
| session span | `10:46:04.460` → `16:57:08.047` = **6 h 11 m 03 s** |
| of which *before* the user pressed Start | **6 h 10 m 41 s** |

The source is `AdbLogSource`: `if (preRollValue > TimeSpan.Zero) _initialSince = …`,
so zero leaves `_initialSince` null and no `-T` argument is added.

**Failure scenario.** Someone opens Live ADB, leaves the defaults alone — the
overwhelmingly common path — and presses Start to watch their app misbehave.
They get a session dominated by six hours of unrelated history: a 44 MiB session
directory instead of ~50 KiB, minutes of ingest before the live edge is reached,
a minimap where the twenty seconds they care about is a hairline, and template
and facet counts computed over a day's background chatter. The dialog itself
teaches the opposite reading: the two stop limits are annotated
`(0 = unlimited)`, so a bare `0` next to `Pre-roll seconds` reads as *none*.

**In fairness**, the product handles the flood well — the timeline stays fitted to
the live window rather than zooming out to six hours (R-15), and every count is
correct. The defect is scope and expectation, not correctness.

**Fix.** Decide what `0` means and say it in the label. The least surprising
choice is to make `0` mean *no history*: pass `-T <now>` for zero, and give the
whole-buffer behaviour its own explicit control — a checkbox
`Include everything already in the buffer`, or a distinguished value labelled
`all`. If the current meaning is kept instead, annotate the field
`Pre-roll seconds (0 = everything already in the buffer)` and warn before
starting a capture that is about to ingest a full ring.

---

### F-07 · Minor · A capture stopped by its own byte cap reports that "the log source ended" it

*First observed* §2.5 (A-18) · *layer* view model

The duration limit names itself when it fires. The byte limit does not — it
produces the generic source-ended message, which points the user at the device:

```text
duration limit:  Stopped · this capture ran its full duration · 1,031 entries kept
byte limit:      Stopped · the log source ended this capture · 1,655 entries kept
notice lane:     The live capture stopped on its own — the log source ended it.
                 1,655 entries were kept; start Live again to carry on.
```

The byte cap really was the cause: the session is exactly 1,048,576 bytes, the
1 MiB the user asked for.

**Failure scenario.** A user sets a 1 MiB cap to keep a session small, the capture
stops, and the product tells them their *log source* ended it. The honest reading
is "the phone disconnected or logcat died", so they go check the cable, the
device, and USB debugging — all healthy. The suggested remedy, "start Live again
to carry on", walks straight into the same cap. A-18 requires that "whichever
limit wins is named", and here the losing message is not merely unnamed but
misattributed.

**Fix.** Give the byte cap the same terminal reason the duration cap already has —
`this capture reached its 1 MiB limit` — and reserve `the log source ended this
capture` for an actual source end. Since both limits can be configured together,
name the one that fired; §2.5 shows the machinery for that already exists on the
duration path.

---

### F-08 · Minor · The byte cap cuts the last record in half and books it as a parse defect

*First observed* §2.5 (A-18) · *layer* infrastructure

The cap is applied to the byte stream, not to record boundaries. `raw.log` for the
1 MiB capture ends mid-line:

```text
…plugstarttime: 35791394\r\n2026-08-30 15:07:17.998983 +0000  1454  1469
                                                                        ^ EOF at byte 1,048,576
```

and the manifest books the fragment as a defect in an otherwise clean capture:

```jsonc
"defects": { "rejectedCandidates": 1, "outOfOrderEntries": 5 }
```

Every other capture in this run ended with `rejectedCandidates: 0`.

**Failure scenario.** A user who caps a capture gets a session whose defect
counters say the *log* was malformed, when the truth is that their own limit cut a
line. Anyone triaging a device by the defect counts — which is what they are for —
now has a false positive, and the byte-faithful raw source ends in a partial
record that no parser can read back.

**Fix.** Plan A-18 already allows the slack needed: the cap is exact "within one
complete-record framing allowance". Stop at the last complete record at or below
the cap (or admit the record that crosses it and then stop), and never persist a
partial trailing line. Do not count a limit-induced truncation as a rejected
candidate.

---

### F-09 · Polish · An unusable configured ADB path is ignored without a word

*First observed* §2.6 (A-15 case F2) · *layer* view model

Setting `adbPath` to a path that does not exist makes the locator fall through to
auto-detection — correct precedence, and it correctly refuses the rogue `adb.exe`
in the working directory. But the user is never told that the ADB they configured
is not the ADB being used; the capture simply runs against a different binary.

**Fix.** When an explicit `adbPath` is set and unusable, say so once in the notice
lane — `The configured ADB path … was not found; using E:\Android\Sdk\platform-tools\adb.exe`
— and mark the field in Settings. Someone who typed that path was usually pinning a
specific platform-tools version, and silently substituting another one defeats the
reason they typed it. The absence message in case E is the model to follow: it
names the thing to fix.

---

### F-10 · Polish · The live status line alternates between two different counts

*First observed* §2.5 (A-18) · *layer* view model

While capturing, the status text alternates between two populations from one
sample to the next:

```text
+47,7s  Capturing · 889 lines received · 13/s · ADB device RFCRC0A9GND
+48,5s  Capturing · 899 entries · ADB device RFCRC0A9GND
+49,3s  Capturing · 910 lines received · 10/s · ADB device RFCRC0A9GND
```

"Lines received" and "entries" are different populations — a multi-buffer capture
has hundreds of `-D` divider lines that are lines but not entries — and the rate
suffix disappears with them. A reader watching the number cannot tell whether it
jumped because traffic arrived or because the label changed underneath it.

**Fix.** Pick one population for the live line and keep it for the whole capture;
if both are worth showing, show both at once (`1,024 lines · 987 entries · 13/s`)
rather than alternating. The plan's R-21 principle — counts name their population —
applies to the live status line as much as to the count row under the timeline.

---


### F-11 · Major · `vcat capture-adb` pins the timestamp policy to the **host's** time zone instead of the zone the device agreed to

*First observed* §2.9 (I-05) · *layer* CLI · **the R-29 defect, still open on the CLI surface**

Desktop and CLI captured the same device at the same moment. Their manifests
disagree about what zone the log was written in:

```jsonc
desktop  "timestampPolicy": { "timeZoneId": "UTC" }
CLI      "timestampPolicy": { "timeZoneId": "Central Europe Standard Time" }
```

The desktop is right because it asks. `AdbLogSource.PrepareAsync` negotiates the
logcat format and derives the zone from what the device actually agreed to:

```csharp
var format = await NegotiateFormatAsync(cancellationToken);
var zone = format.Contains("UTC", StringComparison.Ordinal)
    ? "UTC"
    : await ReadDeviceTimeZoneAsync(cancellationToken);
… Properties[SourceMetadata.LogTimeZoneProperty] = zone;
```

`SourceMetadata.ResolveLogTimeZoneId()` exists to read that back, and it has
exactly **one** caller in the whole repository —
`src/VisualCat.App/Presentation/WorkspaceViewModel.cs:451`. The CLI's
`CaptureAdbAsync` never calls `PrepareAsync`, so the property is never even
populated, and builds its policy from the host instead:

```csharp
// src/VisualCat.Cli/Program.cs
private static TimestampPolicy Policy(Arguments options, DateTimeOffset reference) =>
    new(options.GetNullableInt("--year"),
        options.Get("--timezone") ?? TimeZoneInfo.Local.Id,   // <- the host, always
        reference);
```

**Why this device did not expose it, and why that is luck.** This Samsung accepts
the richest rung, `threadtime,year,UTC,usec`, so every record carries an explicit
`+0000`, the parser resolves it as `ExplicitUtc`, and the policy zone is never
consulted. That is why all 65 shared markers matched to the microsecond. The
defect is latent, not absent: it fires on any device whose ladder degrades below
the `UTC` rung, where logcat writes **device-local** time with no offset.

**Demonstrated consequence.** The same finite corpus — a real `logcat -v
threadtime` dump from this phone, the lowest rung of the ladder — indexed twice,
changing nothing but the host zone:

```text
first line:  08-30 17:23:34.815 10526 27012 I NearbyMediums: …

host = Central Europe Standard Time  ->  2026-08-30T15:23:34.815Z
host = Tokyo Standard Time           ->  2026-08-30T08:23:34.815Z
                                          ^ same bytes, 7 hours apart
```

Every timestamp in the session, the heat map, every query bound, and every
exported CSV row moves by the host↔device offset, silently, with `isValid: true`
and no defect counter raised. This is precisely the failure R-29 was written to
guard against; the guard is satisfied on the desktop and not on the CLI.

**Failure scenario.** A CI job or a support engineer captures from a phone in
another region — or simply a laptop that travelled — with `vcat capture-adb`. If
that phone's logcat does not support the `UTC` modifier, the session's instants
are wrong by hours. Nothing says so: the manifest states a zone confidently, and
`vcat verify` passes because the store is internally consistent.

**Fix.** Three lines in `CaptureAdbAsync`:

1. `await source.PrepareAsync(cancellationToken)` before `SessionCoordinator.ImportAsync`;
2. build the policy from `source.Metadata.ResolveLogTimeZoneId()`, keeping an
   explicit `--timezone` as an override;
3. record the negotiated format in the manifest so a reader can see which rung was
   used (this is **F-02** and would have made the divergence visible immediately).

A regression test can be written headlessly: a fake `IAdbClient` whose probe
rejects every candidate containing `UTC` must produce a session whose policy zone
is the device's, on both surfaces.

---

### F-12 · Major · Capture status tracks record arrival, not the transport — a silent stream says "Connecting…" forever, and a vanished device still says "Capturing"

*First observed* §2.13 (the X-09 reboot) and the R-27 silent-stream probe · *layer* view model

Two observations with one root cause.

**(a) A connected but silent stream never leaves "Connecting".** A capture of the
`crash` buffer alone is a healthy live stream that will never carry a record on
this device. Two seconds in, the child is running:

```text
adb -s RFCRC0A9GND logcat -b crash -D -v threadtime,year,UTC,usec
```

At **t+25 s** and again at **t+50 s** the workspace still reads, verbatim:

```text
Connecting to the device…
Checking the device and logcat format.
                                        (status bar) Connecting · ADB device RFCRC0A9GND
```

The device check and the format negotiation both finished before the child was
spawned. The state is not "connecting"; it is "connected, and the buffer you chose
is empty".

**(b) A device that goes away keeps the last known rate on screen.** With a capture
running, the phone was rebooted (`adb reboot` at 15:33:22.751Z):

| | |
|---|---|
| last record actually ingested | **15:33:26.175Z**, 3.4 s after the reboot |
| `defects.reconnectGaps` | 1 — the first break *was* noticed and the child respawned within 0.8 s |
| app-owned adb child | respawned once, then **alive and unchanged for the next 150 s** |
| UI state throughout | `Capturing`, Stop button present, last text `Capturing · 720 lines received · 49/s` |
| session when finally stopped by hand | finalized, valid, 719 entries, `degraded: false` |

The mechanism is the one the product's own source documents. From
`EnsureDeviceIsCapturableAsync`:

> "`adb -s <unknown> logcat` does not fail, it blocks indefinitely waiting for the
> device to appear, so a capture against a wrong or disconnected serial would
> otherwise hang until the session's own stop fired and then report an empty
> capture as a success."

That preflight is called **once, before the retry loop**. Inside the loop the
re-spawn happens without it, so when the device is genuinely gone, the new child
blocks instead of failing, the reconnect looks successful, the five-attempt budget
is never consumed, and the explicit-failure path X-09 asks for is unreachable.

**Failure scenario.** Someone starts a capture, the phone reboots or the cable is
knocked out, and they walk away. Ten minutes later the window still says
*Capturing · 49/s*. They stop it and get a session that is valid, undegraded, and
missing ten minutes — with one `reconnectGaps: 1` as the only clue, and no
indication of how long the gap was. The plan's R-27 asks for the opposite: the
rate should fall to zero and a heartbeat should name the silence.

**Fix.** Drive the status from the transport, not from arrivals:

1. Re-run the device-presence check before each re-spawn inside the reconnect loop.
   A device that is gone then consumes the bounded attempts and ends with the
   explicit failure the design already intends.
2. Enter `Capturing` when the child is spawned and the format is settled, not when
   the first record lands, and say what is being waited for:
   `Connected · no records yet · crash buffer is empty`.
3. Add the R-27 heartbeat: after a silence threshold, `No records for 2 min` on a
   healthy stream, and `Device RFCRC0A9GND has not responded for 2 min` once the
   presence check fails — and let the last-second rate decay to `0/s` instead of
   freezing at its last value.
4. Record the measured gap duration in the manifest beside `reconnectGaps`, so a
   reopened session can say how much time is missing (this pairs with **F-02**).

---

### F-13 · Minor · About 15 MB of private bytes per capture cycle is not returned when every session is closed

*First observed* §2.13 (X-10) · *layer* not established — needs the plan's X-21 pass

Forty capture cycles, with a full close of every session tab in the middle:

| Point | Handles | Threads | Private bytes |
|---|---|---|---|
| baseline (6 tabs open) | 1,496 | 113 | 476 MB |
| after 32 cycles (38 tabs open) | 3,276 | 129 | 830 MB |
| **after closing all 38 tabs, +60 s** | **1,028** | **113** | **861 MB** |
| after 8 further cycles (8 tabs open) | 1,566 | 126 | 1,036 MB |

**What is established.** Handles and threads are fully released — 1,028 handles
after the close is *below* the 1,496 baseline, and threads return exactly to 113,
so no mapping, file, or thread is being retained per capture. Every `adb` child
is reaped. Stop latency does not degrade with cycle count (88–363 ms in the last
eight cycles, matching the first eight).

**What is not established.** Private bytes rose ≈560 MB across 40 cycles and did
not come back when every session was closed, i.e. roughly **15 MB per capture that
outlives the session that caused it**. Plan §4.2 is deliberate about this: a
rising private-bytes line, uncorroborated by handles, threads, mappings, or
latency, is *not* a leak oracle, and .NET has no obligation to return collected
heap to the OS. So this is reported as a **signal to chase, not a proven leak**.

**Next step, cheap and decisive.** Run the plan's X-21 repetition pass with a
defined warm-up and rolling-window medians, and take a `dotnet-gcdump` at cycle 5
and cycle 50 (`artifacts/debug-tools/dotnet-dump` is already in the repository).
If the delta is Gen2-retained managed objects rooted in per-capture types, it is a
leak; if it is decommitted-but-reserved heap, it is not. Forty cycles is enough to
raise the question and not enough to answer it.

---


## 4. Insights, suggestions, and what was not reached

### 4.1 What this run says about the product

The ADB layer is in good shape where it is hardest. Buffer attribution is exact
per record across four interleaved buffers; a mid-capture transport kill loses
nothing and duplicates nothing; a 45× oversubscribed ring drops nothing; the
locator's precedence is correct including the security-relevant rung; the desktop
and the CLI agree field-for-field on 65 records captured simultaneously from one
phone; and every one of 40 stop cycles was answered, sticky, and orphan-free. The
counters reconcile arithmetically with the captured bytes in every session
examined — `parsedEntries + metaRecords = sourceLines`, every time.

The defects cluster in one place, and it is worth naming because it makes them one
problem rather than ten: **the product knows less about a capture's configuration
and state than it knows about the capture's contents.** Contents are handled with
real rigour. Configuration (which buffers, what pre-roll, which format rung — F-02)
is collected and then dropped. State (connected? silent? gone? — F-12) is inferred
from whether records are arriving rather than read from the transport. Two of the
three Major findings and four of the Minor ones are that same gap seen from
different angles.

### 4.2 Suggestions, in the order that buys the most

1. **Persist a `captureSettings` block** (F-02). It is a small serialisation change
   that closes F-02, makes F-11 visible the moment it happens, gives F-07 the
   vocabulary to name the limit that fired, and lets a reopened session answer
   "was `crash` even selected?". Everything needed is already in
   `SourceMetadata.Properties`; it just never reaches the manifest.
2. **Make the capture state machine transport-driven** (F-12). Re-check device
   presence before each re-spawn, enter `Capturing` when the stream is established
   rather than when the first record lands, add the silence heartbeat R-27 already
   asks for, and record the gap duration. This turns a class of silent failure into
   an ordinary visible one.
3. **Give `vcat capture-adb` the desktop's zone negotiation** (F-11) — three lines,
   and it closes a Major correctness gap on a shipped surface.
4. **Decide what pre-roll `0` means and say it** (F-06). The default path currently
   ingests an entire ring buffer; on this device that was 320,832 entries and
   44 MiB for a 20-second capture.
5. **Name the controls for screen readers** (F-01). Three `AutomationProperties`
   assignments and one shared template fix; `Avalonia.Controls.PathIcon` as an
   accessible name is inherited by every `NumericUpDown` in the product, so the
   settings sheets get fixed for free.

### 4.3 Smaller consistency notes, offered as observations rather than defects

- **Two invalid configurations, two different affordances.** `Start capture` is
  *disabled* when the selected device is not capturable, but *enabled* with zero
  buffers selected, where it fails validation on activation with
  `Select at least one buffer.` Both are defensible; being consistent would be
  better, and disabling Start is the one that matches the rest of the dialog.
- **The CLI's `--help` usage line omits options that `CLI.md` documents** for
  `capture-adb` (`--buffers`, `--adb`, and the index options). The plan
  anticipates this (I-01) and the documented options do work — `--buffers` was
  used throughout §2.9 and §2.10 — but a user reading only `--help` cannot
  discover them.
- **`vcat query --limit` is capped at 10,000** with a clear message
  (`Page size is capped at 10,000. (Parameter 'pageSize')`). Honest, and worth a
  line in `CLI.md`, which documents `--limit` without the ceiling.
- **A running instance keeps the time zone it started with.** Changing the host
  zone while the app is open leaves session names and rendering on the old zone
  until restart (§2.8). Plan row X-27 covers this properly and belongs to a Soak
  run.
- **The device redacts its own PII before VisualCat sees it** — captured records
  contain `redacted-pii:imsi[chars:15]`. Worth knowing when reasoning about P-03:
  on this platform some redaction is upstream of the product, so a session that
  looks clean is not evidence that the product would redact anything itself.
- **A session directory is a verbatim copy of a device's log.** The captures in
  this run include Wi-Fi SSIDs, package names, account-shaped identifiers and
  network parameters from the phone's own logging. That is the product working as
  designed, and it is the reason plan §4.1 treats session directories as sensitive
  evidence. Worth stating once in the product's own documentation.

### 4.4 Cells this run did not reach

Named explicitly so nothing here is mistaken for coverage:

| Not reached | Why |
|---|---|
| A degraded rung of the format ladder | This device accepts `threadtime,year,UTC,usec`; no supported way to force a lower rung. This is what makes **F-11** latent rather than demonstrated end to end on a device |
| `crash` buffer with content | The ring stayed empty all run and the installed companion is a non-debuggable release build, so `am crash` cannot fill it |
| `chatty` declared drops | Android 16 emitted none under 45× ring oversubscription (§2.12) |
| Unauthorized / revoked-and-re-authorized device | Undoing it needs a tap on the phone that an unauthorized transport cannot deliver; the state was reached through the stub instead (§2.3) |
| Unplug/replug USB, USB-mode switch, Wi-Fi transport | Need physical access to the phone |
| Device-clock rollback | `date -s` is `Operation not permitted` on this retail, non-rooted device |
| Unsupported buffer or format error | This phone supports all five buffers and the richest format |
| Provenance rows (B-01 SmartScreen/MotW, I-12, P-13) | The candidate was built locally; there is no uploaded asset for this commit (§1.2) |
| Standard-user boundary (P-08), clean-profile first run (P1) | The run used an elevated token and the host's real product data (§1.3) |
| X-05 four-hour endurance, X-21 repetition leak pass | Soak-schedule rows; **F-13** is the reason X-21 should now be run |

---

## 5. Mutation ledger and restoration

| UTC | Scenario | Target | Original | New | Restored |
|---|---|---|---|---|---|
| 14:52 | A-15 | `%LOCALAPPDATA%\Android\Sdk` | did not exist | stub SDK layout | **yes** — directory removed, absence verified |
| 15:00 | A-15 | `settings.json` → `adbPath` | `null` | stub path, then an invalid path | **yes** — set back to `null`; pre-change copy kept at `evidence\settings.json.original` (SHA-256 `D901F1FF…8269361`) |
| 15:17 | A-20 | Windows time zone | `Central Europe Standard Time` | `Tokyo Standard Time` | **yes** |
| 15:23 | F-11 demo | Windows time zone | `Central Europe Standard Time` | `Tokyo Standard Time` | **yes** — verified `(Get-TimeZone).Id` after each restore |
| 15:15, 15:27 | A-19, I-06 | ADB server | running | `adb kill-server` ×2 | **yes** — server auto-restarted; no other ADB consumer existed on this host |
| 15:28 | X-08 | device `main` ring buffer | 5 MiB | 64 KiB | **yes** — `logcat -b main -G 5M`, re-read as `5 MiB` |
| 15:33 | X-09 | device power state | running | `adb reboot` | **yes** — device returned, `sys.boot_completed=1`, same serial |
| throughout | oracle | device `main`/`system` ring content | device traffic | added `VCatMark`, `VCatBurst`, `VCatFlood` records | self-restoring — ring content rotates; a reboot occurred mid-run |
| throughout | all | `%LOCALAPPDATA%\VisualCat\Sessions` | 179 sessions | **+57 test sessions (169 MB)** | **not removed** — see below |

**The one open item.** This run left 57 capture sessions (169 MB) in the product's
own session cache, every one named `…-ADB RFCRC0A9GND …`. They are the run's
evidence, they are the user's data directory, and deleting another person's
sessions unasked is exactly the kind of thing plan §2.6 warns about — so they were
left in place and are recorded here instead. To remove them:

```powershell
Get-ChildItem "$env:LOCALAPPDATA\VisualCat\Sessions" -Directory |
  Where-Object { $_.Name -like '2026083*-ADB RFCRC0A9GND *' } |
  Remove-Item -Recurse -Force -WhatIf   # drop -WhatIf to delete
```

Everything else is closed. Final device state confirmed: serial `RFCRC0A9GND`,
`device`, boot completed, `main` ring 5 MiB, time zone `Europe/Prague`.

---

## 6. Traps that cost time, and how each was settled

Kept because the plan's Appendix B exists for exactly this, and because three of
them nearly became findings.

1. **A cached `AutomationElement` outlives the control it points at.** Polling a
   held handle for the Stop button reported the label reverting to `Stop capture`
   218 ms after the press — an apparent R-01 violation. Re-tested with a fresh
   lookup on every poll across 40 cycles: never reproduced. **Look controls up by
   name on every sample; never poll a cached automation handle across a state
   change.**
2. **UI Automation queries are served by the application process, so the test
   inflates the number it is measuring.** An early sample showed 55 s of CPU during
   a 2.5-minute capture — until the same measurement without a tree walk in flight
   gave **5.0% of one core**. The full-descendant `FindAll` over a maximized 4K
   window is not free for the app. **Measure resource use in a window with no
   automation traffic.**
3. **`adb reconnect device` does not interrupt a running capture.** It logs a
   reconnect request on the device and the existing logcat stream survives with the
   same PID. To actually break the stream, use `adb kill-server`; to make the
   device genuinely absent, reboot it.
4. **`adb devices -l` is not proof of which device answers.** Established in
   earlier runs on this host and re-confirmed here: identify the phone with
   `getprop ro.serialno` / `ro.product.model` before drawing any conclusion.
5. **A byte cap needs traffic to reach it.** A 1 MiB cap did not fire in 240 s on
   this device's ordinary ~2 KB/s. That was the test being too short, not the cap
   failing; a device-side burst of ~900-byte records reached it in 18 s.
6. **PowerShell member lookup is case-insensitive**, so a P/Invoke class holding
   both a `ShowWindow` method and a `SHOWWINDOW` constant resolves the constant and
   fails with *"does not contain a method named 'ShowWindow'"*. Rename the
   constant.
7. **`vcat query --limit` above 10,000 is rejected**, so a whole-session cross-check
   must page rather than ask for everything at once.
8. **Device `log` (toybox) has no `-b`.** A shell-emitted marker can only reach
   `main`; `system`, `events`, `radio` attribution must be proved against the
   device's own traffic (§1.5).

---

## 7. Remediation — implementation log

Every finding in §3 is being closed in the product. This section is the **restore
point for the remediation**, written the same way §0 is written for the run: it is
rewritten after every finding, so an interrupted implementation resumes from the
last line here without depending on a fact that exists only in a previous session.

### 7.0 Restore point — resume here

| Field | Value |
|---|---|
| Work ID | `20260830-windows-adb-remediation` |
| Branch | `main` (base commit `29c3bd8`) |
| Status | **COMPLETE — implementation, regression gates, X-21, and connected-device/process proof all pass** |
| Last completed | final review gate: build 0 warnings/errors; solution tests **666/666 passed** (Domain 47, Core 107, Application 69, App 443); `git diff --check` clean |
| Next step | review and commit the intentionally uncommitted working tree when ready |
| Build gate | `dotnet build VisualCat.Desktop.slnx -c Debug` and `dotnet test VisualCat.Desktop.slnx` must both pass before a finding is marked Done |

**To resume.** Read §7.1 for what is done and what is not, then continue at
*Next step*. Every change is in the working tree of `E:\VisualCat`; nothing lives
in a scratch directory or a shell variable.

### 7.1 Work order and status

Ordered by §4.2 (what buys the most), then by severity. `Done` means implemented,
covered by a test that fails without the change, and building clean.

| # | Finding | Severity | Status | Where |
|---|---|---|---|---|
| 1 | F-02 persist `captureSettings` | Minor | **DONE** | manifest round-trip test; `vcat info` and Session information expose the request; connected-device manifest verified |
| 2 | F-12 transport-driven capture state | Major | **DONE** | source watchdog/status/gap duration plus workspace stream-established state and headless integration tests |
| 3 | F-11 CLI zone negotiation | Major | **DONE** | CLI prepares the source and uses the negotiated device zone unless `--timezone` explicitly overrides it; device manifest records UTC in source and policy |
| 4 | F-06 pre-roll `0` means "from now" | Minor | **DONE** | `-T now`, explicit whole-buffer checkbox and CLI switch, unambiguous labels/help; four-second phone capture imported only the live interval |
| 5 | F-01 accessible names | Major | **DONE** | device combo, three fields, and all six spinner buttons have tested accessible names |
| 6 | F-07 the byte cap names itself | Minor | **DONE** | source reason reaches CLI stderr, desktop status, and desktop notice; live CLI reports the exact 4 KiB limit |
| 7 | F-08 the byte cap stops on a record boundary | Minor | **DONE** | source admits the crossing record whole; device artifact ends LF with zero rejected candidates and verifies cleanly |
| 8 | F-04 a vanished device is not silently replaced | Minor | **DONE** | disconnected placeholder preserves exact serial; Start stays disabled until reconnect or deliberate choice |
| 9 | F-05 discovery child dies with its dialog | Minor | **DONE** | dialog-lifetime token cancels discovery; real 60-second blocked child is gone when cancellation returns |
| 10 | F-10 one population on the live line | Polish | **DONE** | snapshot refresh reuses source-line status instead of replacing it with parsed-entry status |
| 11 | F-03 `1 device detected.` | Polish | **DONE** | count-aware device wording; headless regression test |
| 12 | F-09 an unusable configured ADB path says so | Polish | **DONE** | one-time fallback notice and inline accessible warning in Appearance & timeline |
| 13 | §4.3 consistency notes (Start with no buffers, CLI `--help`, `--limit` ceiling, session sensitivity) | — | **DONE** | Start is disabled with an inline zero-buffer explanation; per-command CLI help is generated from the accepted-option table; the 10,000 query cap and session sensitivity are documented |
| 14 | F-13 the X-21 repetition pass | Minor | **DONE** | 50-cycle shell test found and fixed an unbounded recent-session rescan queue; every closed tab/view is collectable and handles/threads settle |

### 7.2 What changed, per finding

**2026-08-30 recovery checkpoint.** `git status` at takeover showed modified
`SessionCoordinator.cs`, `ILogSource.cs`, `Program.cs`, `Sessions.cs`,
`AdbLogSource.cs`, and `AdbTests.cs`, plus this report and a new
`WindowsLiveTestRemediationTests.cs`. The recovered implementation attempts the
manifest capture-settings block, reconnect-gap duration, transport status events,
the CLI time-zone fix, zero-pre-roll cursor, record-aligned byte caps, truthful cap
completion, and complete CLI command help. This checkpoint deliberately marks all
of them *Partial*: the application has not yet been built, the new source events
are not yet proven to reach the workspace, and the requested desktop information
surfaces are not present in the files changed so far.

**2026-08-30 source/desktop checkpoint.** The recovered source work now reaches
the actual reader surfaces. `captureSettings` survives import, manifest write and
reopen; Session information renders requested buffers (including an empty selected
buffer), history mode, limits, negotiated format/zone, ADB build and device
identity. `StreamEstablished` drives a distinct connected/no-records state, source
transport trouble remains visible, measured reconnect duration is rendered, and a
source-owned completion reason replaces the generic “log source ended” text in
both status and notice. The dialog now keeps a disappeared serial as `Not
connected`, disables Start for stale discovery and zero buffers, cancels its
discovery with a five-second bounded timeout, makes whole-buffer history explicit,
pluralizes device counts, and exposes meaningful automation names. Focused gates:
`dotnet build VisualCat.Desktop.slnx -c Debug` passed with 0 warnings; Application
ADB/remediation tests **22/22** passed; App headless remediation tests **6/6**
passed. These rows remain “full gate pending” until the complete solution test run
and live process/device checks finish.

**2026-08-30 F-13 / X-21 checkpoint.** A new full-shell repetition test creates,
renders, closes and releases 50 real capture workspaces. The first run made the
original signal actionable: closing the last tab launched
`RefreshRecentSessionsAsync` fire-and-forget, so a rapid cycle run accumulated one
ever-larger directory scan per close. At cycle 50 the in-flight queue had grown to
4,375 handles, 71 threads and a 352.6 MB managed heap. `MainView` now coalesces
requests into one active scan plus at most one latest-state pass, passes a lifetime
token into the scan, and drains it on disposal. The same test now passes in 4m27s:

| X-21 measure | Cycle 5 | Cycle 50 | Result |
|---|---:|---:|---|
| handles | 383 | 378 | stable / lower |
| threads | 20 | 16 | stable / lower |
| managed heap after compacting GC | 60.4 MB | 84.2 MB | bounded; cycle 20–50 stays 72.9–87.7 MB |
| private bytes | 87.1 MB | 166.8 MB | CLR reservation still rises, but not corroborated by roots or OS resources |
| closed capture tabs alive | — | **0 / 50** | pass |
| closed workspace views alive | — | **0 / 50** | pass |
| cycle latency | — | median 128 ms, p95 641 ms, max 906 ms | no late-cycle degradation |

Machine-readable evidence is
`artifacts/live-test/20260830-windows-adb-samsung/evidence/remediation/F-13-X21-50-cycles.json`.
This resolves the product leak question: there was a real unbounded work backlog,
now removed; the remaining private-byte delta is uncorroborated CLR reservation,
not retained per-capture tabs or views.

**2026-08-30 final full-gate checkpoint.** After making recent-refresh cancellation
idempotent (the shell deliberately permits `DisposeAsync` twice), and after the
final review made a recovered silent stream settle its gap at transport recovery,
`dotnet build VisualCat.Desktop.slnx -c Debug` passed with **0 warnings and 0
errors**. The exact report gate `dotnet test VisualCat.Desktop.slnx -c Debug
--no-build` then passed **666/666** tests: Domain 47, Core 107, Application 69 and
App 443. The App project took 5m57s because the full X-21 lifecycle pass is part
of the ordinary suite. The earlier divider-band timing flake did not recur.
`git diff --check` is clean. Live ADB's fixed wire format is also enforced: an
attempt to request `--format brief` now exits 2 with the correction to omit the
option or use `threadtime`, instead of accepting and silently ignoring it.

**2026-08-30 connected-device/process checkpoint.** The physical Samsung
`RFCRC0A9GND` answered both `get-state` and `getprop ro.serialno`. Three Debug CLI
captures exercised the remediated source rather than a fake:

| Evidence | Observation |
|---|---|
| `evidence/remediation/F-06-zero-preroll.vcat` | request 17:19:19.946Z; first captured instant 17:19:20.513Z; four-second capture contains 332 source records / 40,775 B rather than the ring; `preRollSeconds: 0`, `includesBufferHistory: false`; verify 317 entries / 332 records, no issues |
| `evidence/remediation/F-02-F-11-preroll-zone.vcat` | requested pre-roll 2 s persisted; `threadtime,year,UTC,usec`, capture zone UTC and timestamp-policy zone UTC agree; ADB 1.0.41, model `SM_G990B` and fingerprint persisted; verify 416 entries / 429 records, no issues |
| `evidence/remediation/F-07-F-08-byte-cap.vcat` | stderr says `this capture reached its 4 KiB limit`; raw is 4,066 B / 35 records, final byte LF, 0 rejected candidates; verify 32 entries / 35 records, no issues |

F-05 also has real process proof, not only a cancellable fake client. The
reproducible net10 probe in `evidence/remediation/F-05-process-probe/` starts the
run's stub `adb.exe` with `#hang 60`, cancels `ProcessAdbClient.ListDevicesAsync`
after 750 ms, then resolves survivors by exact executable path. Result: cancellation
observed, returned in **811 ms**, **0 surviving stub children**. Its isolated stub
and control are in `evidence/remediation/F-05-process-stub/`; no machine ADB or
settings were mutated.
