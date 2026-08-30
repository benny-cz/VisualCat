# VisualCat — Android live-test report, v2

Independent second pass against physical Android hardware, executed to
[`ANDROID-LIVE-TEST-PLAN.md`](ANDROID-LIVE-TEST-PLAN.md). The v1 report
([`ANDROID-LIVE-TEST-REPORT.md`](ANDROID-LIVE-TEST-REPORT.md)) is treated as
**already implemented and closed**; nothing in it was used as a prior, and no
finding here was seeded from it. Where a v2 finding happens to touch the same
surface as a v1 one, that is noted only so a reader can tell a regression from a
new defect.

**This report is context-agnostic.** Every device fact, path, serial, hash, and
oracle used below is recorded here at the point it was established. A reader who
has never seen this session can reproduce every step from this file alone.

**This report is written incrementally.** Each scenario is appended as it
finishes, so an interrupted run loses at most the scenario in flight. §0.3 is the
restore point: it always names the next scenario to execute.

---

## 0. Run record

### 0.1 Run header

| Field | Value |
|---|---|
| Run id | `v2-20260829-samsung+pixel+motorola` |
| Report version | v2 (independent of v1) |
| Host | Windows 11 Pro 10.0.26220, PowerShell + Git Bash |
| Host clock at start | 2026-08-29 19:07 local (UTC+2) |
| ADB | `E:\Android\Sdk\platform-tools\adb.exe`, 1.0.41 / 35.0.1-11580240 |
| Repository commit | `0c9dd02` — *Give every self-sized touch target the reserve the floor needs* |
| Working tree | clean except a staged deletion of `docs/ANDROID-AUDIT-CONTINUATION.md` |
| Evidence roots | DUT-1: `artifacts/android-live-v2/RFCRC0A9GND/`; DUT-2: `artifacts/android-live-v2/Pixel5-*`; DUT-3: `artifacts/android-live-v2/ZY22M4T2Z4/` |

### 0.2 Device under test — DUT-1

| Field | Value |
|---|---|
| Transport serial | `RFCRC0A9GND` |
| `ro.product.model` | `SM-G990B` (Samsung Galaxy S21 FE 5G) |
| `ro.product.manufacturer` | `samsung` |
| `ro.build.version.release` / `sdk` | **16 / 36** (the plan's top supported API) |
| `ro.build.fingerprint` | `samsung/r9qxeea/r9q:16/BP2A.250605.031.A3/G990BXXSKIZF1:user/release-keys` |
| ABI list | `arm64-v8a,armeabi-v7a,armeabi` |
| `get-state` | `device` |
| Physical size | `1080x2340` |
| Density | **Physical 480, override 360** → 3.0 px/dp physical, **2.25 px/dp effective**; viewport **480 × 1040 dp portrait** |
| `font_scale` | `1.0` |
| Animation scales | window `1.0`, transition `1.0`, animator `null` (platform default) |
| Time zone | `Europe/Prague` (UTC+2 at run time) |
| Device UTC at start | `Sat Aug 29 17:07:59 UTC 2026` |
| Locale | `cs-CZ` |
| IME | `com.samsung.android.honeyboard/.service.HoneyBoardService` |
| Navigation | gesture (Samsung `sec_gestural`) — confirmed in §1 |
| Battery | 100 %, USB powered, 33.7 °C |
| Thermal status | `0` (none) |
| Free space | 95 035 476 KB (~90 GB) on `/data` |

> **Density note.** The owner's *Display size* override is in force
> (`Override density: 360`). Every dp figure in this report is computed at
> **2.25 px/dp**, not at the panel's physical 3.0. `wm density reset` would wipe
> the owner's override rather than restore it; it is never used here.

### 0.3 Restore point — where to resume

| Field | Value |
|---|---|
| **Next scenario to execute** | **None — all four implementation batches are closed (§20.10). Every V2 finding, PLAN-01 and the frame-instrumentation amendment are implemented and, except where §20.10 says otherwise, verified on DUT-1. §21 closes §20.12's remainder and retracts V2-25; §22 measures and fixes V2-22 on an API-33 emulator, which was the last unverified finding. Nothing in the report is unimplemented; §21.4 minus V2-22 is what remains open.** |
| Last completed | Discovery run: DUT-1 B-01–B-10, B-12–B-14, B-16, X-01, X-13, A-05, A-09, A-10, A-16, A-17, U-04, U-06, P-01, P-03, R-11, R-34, plus R-01/02/07/08/12/13/15/22/24/27/28/29/30/32/36/38. DUT-2 extension: B-01, B-15, U-04, U-07, U-18, R-11 and R-19. DUT-3 compatibility extension: B-15, U-04, U-07, R-11/R-19 and stateful cold resume. Implementation continuation baseline: §20.1. |
| Devices left in | **Current connected DUT-1:** clean first-run data, installed report artifact, stopped; `font_scale=1.0`, night mode `yes`, rotation `free`, Wireless Debugging off. Historical DUT-2/DUT-3 hand-back remains as recorded in §§16/19. |
| Mutation ledger | DUT-1 §0.5, DUT-2 §14.3 and DUT-3 §17.3 — **empty of temporary mutations; every changed setting restored and verified in §§13/16/19** |

### 0.4 Artifact under test

| Field | Value |
|---|---|
| Package | `com.barebit.visualcat` |
| `versionName` / `versionCode` | `2.0.10-dev` / `2001000` |
| `minSdk` / `targetSdk` | 31 / 36 |
| APK | `src/VisualCat.Android/bin/Release/net10.0-android36.0/com.barebit.visualcat-Signed.apk` |
| Size / SHA-256 | **34 147 035 B / `68692776190ff7f2690e142073cb2b8803ad081006602cd32bb26343d6534bdb`** |
| Build configuration | **Release** — `run-as` refuses it ("package not debuggable"), so app-private storage is *not* inspectable. Expected security behaviour, per plan §4.1(5). |
| Installed | clean sideload from HEAD; identity line `VisualCat 2.0.10-dev+0c9dd02`; `installerPackageName=null` |
| Resolved launcher | `com.barebit.visualcat/crc64a1973b883a99125a.MainActivity` |
| Requested permissions | `POST_NOTIFICATIONS`, `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_DATA_SYNC`, `CHANGE_WIFI_MULTICAST_STATE`, `INTERNET`, `…DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION` |
| **`READ_LOGS`** | **absent from the manifest** — confirms the Play/W-state shape; every full-device claim must therefore come through Wireless ADB |
| `POST_NOTIFICATIONS` | `granted=false` at run start (never asked, or previously denied) |

The first binary found on the device was rebuilt and clean-installed during
B-01 after its identity line proved that it predated commit `0c9dd02`. The
table above is the authoritative artifact for every result in this report; the
superseded pre-flight hash is retained only in B-01's audit trail.

### 0.5 Device mutation ledger

Every DUT-1 global mutation, its original value, and its restore command.
Restores are verified in §13. DUT-2 has its own preservation ledger in §14.3.

| # | Setting | Original | Changed to | Restore command | Restored |
|---|---|---|---|---|---|
| 1 | `system accelerometer_rotation` | `1` | `0` | `settings put system accelerometer_rotation 1` | **restored** |
| 2 | display rotation | free (`mRotation=ROTATION_0`, auto-rotate on) | `wm user-rotation lock 1` -> `lock 0` | `wm user-rotation free` | **restored; verified `free`** |
| 3 | `POST_NOTIFICATIONS` for `com.barebit.visualcat` | `granted=false` | granted by the user in the runtime prompt | `pm revoke com.barebit.visualcat android.permission.POST_NOTIFICATIONS` — moot if the app is uninstalled | **restored; verified `granted=false` after `pm clear`** |
| 4 | `system font_scale` | `1.0` | `1.3` -> `1.5` -> `1.8` -> `2.0` | `settings put system font_scale 1.0` | **restored (verified `1.0`)** |
| 5 | night mode (`cmd uimode night`) | `yes` | `no` | `cmd uimode night yes` | **restored (verified `yes`)** |
| 6 | files written to `/sdcard/Download` | — | `vc2-*.txt`/`.bin` corpora, `vc2-crashy.csv`, `vc2-crashy.csv (1)`, `visualcat-diagnostics-20260829-204306.zip` | delete at §13 | **restored; exact files deleted and absence verified** |

### 0.6 Result legend

`PASS` · `FAIL` · `BLOCKED` (external condition, named) · `N/A` (capability
explicitly unsupported, source named) · `PARTIAL` (an assertion inside the
scenario failed while the scenario's headline behaviour held — always paired
with a finding).

---

## 1. Pre-flight completion — platform dimensions this device covers

| Dimension | Value on DUT-1 | Why it matters |
|---|---|---|
| **Navigation mode** | `settings get secure navigation_mode` = **`0` → three-button navigation** | The navigation bar is a real 108 px (**48 dp**) window at `[0,2232][1080,2340]`, not a 24 dp gesture hint. Every "Back" step in this run is a **KEYCODE_BACK button press**, not an edge swipe. |
| **Display cutout** | Centre punch-hole, `Rect(505,0 – 575,99)`; `layoutInDisplayCutoutMode=always` on the app window | Top inset is 99 px (**44 dp**). The app opts into drawing under the cutout, so safe-area handling is load-bearing here. |
| **Rounded corners** | radius 108 px (48 dp) at all four corners | Corner-adjacent controls can be clipped by the panel itself, which no dump reports. |
| **Refresh rate** | 120 Hz active (`supportedModes` 120/60), `renderFrameRate 120.0` | A frame budget of **8.3 ms**, so `gfxinfo` jank percentages are stricter than on a 60 Hz device. |
| **Effective viewport** | 480 × 1040 dp (owner's *Display size* override, 2.25 px/dp) | Wide enough for the medium width class; §U-02 must be reached with `wm size`, not by rotation alone. |
| **Locale** | `cs-CZ` with an English product UI | Directly exercises R-35 (numbers/dates must come from the interface culture, not the device's). |
| **IME** | Samsung HoneyBoard | Not AOSP — a distinct IME-insets implementation for U-03. |

Gesture-exclusion state was read for completeness
(`system_gesture_exclusion_limit_dp=200`; the focused window published
`SkRegion((1038,310,1080,651)(0,741,1080,1059)(0,1070,1080,1156)(0,1157,1080,1203))`),
but under three-button navigation it has no user-visible effect. Edge-gesture
conflict (U-18) is therefore **N/A on this device** and is called out as a
coverage gap in §12 rather than reported as a pass.

---

## 2. Test data — corpus manifest

Generated on the host with the CLI at commit `0c9dd02`
(`2.0.10-dev+0c9dd02cbbbdc37f2db97ea73f7747ec249a50ec`), seed `42`, then pushed
to `/sdcard/Download/` with a `vc2-` prefix. `vc2-small.txt` was pulled back and
compared: SHA-256 identical, so push/pull transport is not a confound.

### 2.1 Deterministic corpora and their oracles

| Name | Lines (`wc -l`) | Bytes | SHA-256 (head) | `totalMatching` | First / last instant |
|---|---:|---:|---|---:|---|
| `tiny.txt` | 1 001 | 90 384 | `26472b46…` | 1 000 | `2026-05-15T12:13:37.000Z` / `12:13:38.501Z` |
| `small.txt` | 50 001 | 4 500 749 | `1f35340b…` | 49 994 | `12:13:36.771Z` / `12:14:51.957Z` |
| `medium.txt` | 250 001 | 22 501 180 | `8ef1294d…` | 249 974 | `12:13:36.771Z` / `12:19:51.747Z` |
| `large.txt` | 1 000 001 | 90 017 930 | `59ba7345…` | *(indexed on device only)* | — |

Severity oracles (`vcat stats`), used by B-05:

| Corpus | Verbose | Debug | Info | Warn | Error | Fatal | Unknown | Templates |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `tiny` | 188 | 145 | 164 | 182 | 165 | 156 | 0 | 50 |
| `small` | 8 469 | 8 233 | 8 253 | 8 477 | 8 213 | 8 349 | 0 | 77 |
| `medium` | 41 852 | 41 807 | 41 621 | 41 715 | 41 354 | 41 625 | 0 | 77 |

Detected format for all four: **ThreadTime + `usec` modifier**, confidence `1`.

> **Note — the first line is not an entry.** Every generated corpus opens with
> `--------- beginning of main`, and `wc -l` counts the trailing newline, so
> `lines − 1 − (unparsed banner lines)` is the entry oracle: 1 000 / 49 994 /
> 249 974. Assertions below use the `totalMatching` column, never `wc -l`.

### 2.2 Adversarial corpora

Built by `.tmp/corpus-v2/make-adversarial.sh` (recorded in the evidence root),
which is deterministic — seeded `random`, fixed offsets, no manual editing.

| File | Bytes | Recipe / oracle |
|---|---:|---|
| `crlf.txt` | 4 550 750 | `small.txt` with every `\n` → `\r\n` |
| `bom.txt` | 4 500 752 | UTF-8 BOM prepended to `small.txt` |
| `nonutf8.bin` | 4 500 749 | `small.txt` with one byte in `0x80–0xFF` injected every 4 096 B at `+37` |
| `truncated.txt` | 1 000 037 | `medium.txt` cut at byte **1 000 037**, mid-line |
| `longline.txt` | 2 097 308 | one record with a **2 MiB** message, followed by a normal record |
| `nofinalnewline.txt` | 90 383 | `tiny.txt` with the trailing newline stripped |
| `empty.txt` | 0 | zero bytes |
| `notalog.txt` | 10 485 760 | 10 MiB of seeded pseudo-random bytes named `.txt` |
| `mixed-formats.txt` | 264 891 | 200 KB of ThreadTime + 1 200 Brief records |
| `outoforder.txt` | 451 040 | first 5 000 records of `small.txt`, shuffled (seed 7) |
| `continuations.txt` | 107 629 | 200 copies of a Java trace with `\t`-indented continuations and a `Caused by:` |
| `crashy.txt` | 4 502 597 | `small.txt` with `FATAL EXCEPTION: VCATCRASH-<n>` injected every 5 000 lines — **9 crash blocks, 18 injected records**, marks in `crashy.marks` |
| `dst.txt` | 14 518 | 200 records across the `Europe/Prague` DST jump (`03-29 01:55` → `03:00`) |

### 2.3 MediaStore ids — opening a file without driving the SAF picker

Recorded so `ACTION_VIEW` can be used where the picker is not the thing under
test (A-06 tests the picker itself):

| File | `_id` | File | `_id` |
|---|---|---|---|
| `vc2-tiny.txt` | 1000000574 | `vc2-nofinalnewline.txt` | 1000000583 |
| `vc2-small.txt` | 1000000575 | `vc2-empty.txt` | 1000000584 |
| `vc2-medium.txt` | 1000000576 | `vc2-notalog.txt` | 1000000585 |
| `vc2-large.txt` | 1000000577 | `vc2-mixed-formats.txt` | 1000000586 |
| `vc2-crlf.txt` | 1000000578 | `vc2-outoforder.txt` | 1000000587 |
| `vc2-bom.txt` | 1000000579 | `vc2-continuations.txt` | 1000000588 |
| `vc2-nonutf8.bin` | 1000000580 | `vc2-crashy.txt` | 1000000589 |
| `vc2-truncated.txt` | 1000000581 | `vc2-dst.txt` | 1000000590 |
| `vc2-longline.txt` | 1000000582 | | |

Form: `am start -a android.intent.action.VIEW -t text/plain -d
content://media/external/file/<id> --grant-read-uri-permission -n <activity>`.

### 2.4 Plan/implementation discrepancy found while preparing data

**PLAN-01 — `generate-test-log` has no `--format`, so §3.2's four format
corpora cannot be produced as the plan specifies.** Plan §3.2 builds
`fmt-time.txt`, `fmt-brief.txt`, `fmt-long.txt`, and `fmt-epoch.txt` with
`vcat generate-test-log --format <fmt>`. The shipped CLI accepts only
`[--output] [--lines] [--seed]` (confirmed against `vcat --help` and
`docs/CLI-HELP.txt`, which agree with each other). A-08's format sweep therefore
has to synthesise those inputs by hand, which is exactly what §3.2 forbids
("Do not make these files by unrecorded manual editing"). Recorded as a
**documentation/tooling defect**, not an application one; see §11 for the
suggested fix.

---

## 3. Tier B — basic scenarios

### B-01 · Cold install and first launch — **PARTIAL** (3 findings)

**Setup correction made during this scenario, and the product is why it was
caught.** The first install used the APK already on disk (`b5b43664…`, built
18:54). Its identity line read `VisualCat 2.0.10-dev+1840623`.
`git log --format="%h %cI"` shows `0c9dd02` was committed at **19:01:33** —
seven minutes *after* that build. The binary was therefore HEAD~1, and the run
would have tested stale code. The app was rebuilt from HEAD and clean-installed;
the identity line then read `VisualCat 2.0.10-dev+0c9dd02`.

> **R-38 passes, emphatically.** The displayed version tracked the build closely
> enough to catch a tester's own mistake. Everything below is against
> **`68692776190ff7f2690e142073cb2b8803ad081006602cd32bb26343d6534bdb`**
> (34 147 035 B), built from `0c9dd02`. The stale pre-flight hash shown just
> above is superseded by §0.4's authoritative artifact row.

**Cold-start budget — PASS.** Five `am force-stop` + `am start -W` repetitions:

| Run | 1 | 2 | 3 | 4 | 5 | median | max |
|---|---:|---:|---:|---:|---:|---:|---:|
| `TotalTime` (ms) | 1488 | 1387 | 1398 | 1402 | 1388 | **1398** | 1488 |
| `WaitTime` (ms) | 1490 | 1389 | 1399 | 1404 | 1390 | 1399 | 1490 |

`LaunchState: COLD` on every run. Budget is median <= 2 500 ms and no run
> 4 000 ms: **1 398 / 1 488** — 56 % of the median gate. `ThisTime` is not
emitted separately by Android 16 when it equals `TotalTime`; recorded as such
rather than inferred.

**Composition — PASS.** Exactly the three Android hero actions are present:
`OPEN LOG` (72 x 48 dp), `ON-DEVICE LIVE` (100 x 48 dp), `RECENT CAPTURES`
(117 x 48 dp). None of *ADB live*, *Follow growing file*, *Save session*, or
*Save portable* appears. Identity line: `VisualCat 2.0.10-dev+0c9dd02 ·
local-first · no telemetry`. The severity legend shows all six levels.

**System-bar banding — PASS.** A vertical colour probe down x = 540 returns the
app ground `rgb(8,13,22)` continuously at y = 5, 40, 90, 98, 2 231, 2 240, 2 280
and 2 335 — behind the status bar, behind the 108 px three-button navigation
bar, and to the last row of the panel. No white or black band, and no chrome
under the centre cutout: the workspace `Grid` begins at y = 99, exactly the
cutout inset.

**Stability — PASS.** 30 s untouched, then a full `-b main,crash,system` sweep of
the window: no `AndroidRuntime`, no unhandled managed exception, no ANR, no
`SIGABRT`. The only app line was `ProfileInstaller: Installing profile for
com.barebit.visualcat`.

---

#### V2-01 · A permanent, half-clipped scrollbar sits on a screen that cannot scroll — **Medium**

*Surface:* Android companion, first-run empty state. *Severity:* Medium —
cosmetic in effect, but it is the first thing a new user sees and it is an
explicit false affordance.

**Observed.** On the empty state a light-grey scrollbar thumb is painted at the
right screen edge, `x = 1068…1079`, `y = 311…650` — **12 px (5.3 dp) wide and
340 px (151 dp) tall**. Its rounded left cap is visible; its right half is cut
off by the physical edge of the display. Colour `rgb(81,84,90)` against the
`rgb(8,13,22)` ground, so it is conspicuous, not a hairline.

**It is a false affordance in three separate ways:**

1. **Nothing scrolls.** `input swipe 540 1600 -> 540 600` over 300 ms produced a
   pixel diff bounded by `(127,47)-(304,80)` — the *status-bar clock*, nothing
   else. The app did not move by one pixel.
2. **The thumb is not draggable.** `input swipe 1074 480 -> 1074 1600` over
   500 ms produced a whole-screen diff below the status bar of `None`, and the
   thumb still spanned `311…650` afterwards.
3. **Its size claims content that does not exist.** 340 px of thumb in the
   1 911 px viewport track implies a scrollable extent of roughly
   **10 700 px — 5.6 screens**. The visible content ends at y ~ 974.

**Two further defects in the same element.** The thumb starts at y = 311, which
is **10 px above the ScrollViewer's own top edge (y = 321)** — it is drawn over
the command bar's bottom divider. And it is flush with x = 1079, the last
column of the panel, inside the 108 px corner radius, so on the physical device
it is partly swallowed by the rounded corner as well as by the edge.

**Suggested fix.** Two independent things need doing, and doing only the second
hides a real bug rather than fixing it:

- **Find why the extent is wrong.** A `ScrollViewer` reporting a 5.6x extent for
  content that fits is measuring an unconstrained child — the usual cause is a
  `StackPanel`/`Grid` inside a `ScrollViewer` whose row is `Auto` in one pass and
  `*` in another, or a `MinHeight` on a stretched child multiplied through a
  nested scroll host. Fix the measure and the scrollbar disappears on its own
  everywhere, including places this run has not looked.
- **Then set the policy explicitly.** On a touch surface a scrollbar is
  feedback, not a control: `VerticalScrollBarVisibility="Auto"` with an
  *overlay* (non-space-taking, fade-out) thumb is the platform-correct
  behaviour, and it must be inset from the edge — at minimum by the 4 dp gutter
  the rest of the layout uses, ideally past the 48 dp corner radius — so it is
  never clipped by the panel. An always-on desktop scrollbar should not be
  reachable on the Android composition at all.

---

#### V2-02 · The empty state uses 45 % of the screen and leaves 55 % blank — **Medium (UX)**

*Surface:* Android companion, first-run / no-session empty state.

**Observed.** The workspace host is `[0,321][1080,2232]` (1 911 px, 849 dp). The
hero block's content — title, subtitle, six legend chips, three actions, the
identity line — ends at **y ~ 974**. Everything from y = 974 to y = 2 232 is
empty ground: **1 258 px, 559 dp, 53 % of the workspace and 45 % of the whole
display**. The dump shows the hosting `StackPanel` stretched to 801 dp with its
children top-aligned inside it.

**Why it matters.** On a phone this does not read as "airy", it reads as *the
screen failed to finish drawing*. The two primary calls to action — `OPEN LOG`
and `ON-DEVICE LIVE` — sit at 40 % height with a void underneath, which is the
part of the screen a thumb reaches most easily. And the app's own value
proposition ("see the shape of your log") is being demonstrated on a screen that
shows the shape of nothing.

**Suggested fix, in preference order.**

1. **Fill it with the thing the user came for.** The empty state already knows
   about *Recent captures*; put the list inline, in the space that is already
   there, instead of behind a button that opens a dialog (see V2-03). R-12 asks
   for "up to four device-held captures, one tap from a cold start" — inline
   rows deliver that literally, and the screen stops being empty the moment the
   user has ever captured anything.
2. **Failing that, centre the block** (`VerticalAlignment="Center"` on the hero
   `StackPanel`, with the identity line docked to the bottom of the host as a
   footer). Costs nothing and removes the unfinished look.
3. Do **not** solve it by growing the type. The 26 dp headline is already at the
   top of a comfortable range for a 480 dp-wide viewport.

---

#### V2-03 · *Recent captures* with no captures explains a taxonomy instead of saying "none yet" — **Medium (UX)**

*Surface:* Android companion, `RECENT CAPTURES` on a clean install.
*Precondition:* zero sessions on the device — the state every new user is in.

**Observed.** The dialog is titled *Recent VisualCat sessions* and contains,
verbatim:

> These captures are stored in this app's private storage. Share… hands one to
> another app as a portable archive.
>
> A complete capture was stopped or finished normally and holds everything it
> recorded. An interrupted one stopped without being told to — the app was
> killed, or the device restarted — and opens with whatever reached the disk;
> nothing after that point was kept. A capture in progress is one this app is
> recording into right now.

…then an **empty list region**, a *Cancel* button, and an *Open* button that is
`enabled=false`. **At no point does it say that there are no captures.** A
first-run user taps the third hero action and is given 52 dp of legend for a
three-way status taxonomy of items they do not have, plus an inert *Open*.

The layout compounds it: the card is `[18,1682][1062,2214]`, pinned to the
bottom 23 % of the display, with the dimmed — and equally empty — first-run
screen filling the other 77 %.

**Suggested fix.**

- Give the list an **empty state of its own**: one line ("No captures on this
  device yet.") plus the one action that changes that — *Capture this device's
  log* — so the dialog is never a dead end.
- **Withhold the taxonomy until it applies.** The complete / interrupted /
  in-progress explanation is good writing, but it is help text for a populated
  list. Show it as a footnote under the rows, or behind the status word itself,
  once there is at least one row.
- The disabled *Open* should either say why (`Select a capture to open`) or not
  be rendered while the list is empty, matching the rule R-24 applies to the
  More sheet.
- Size the card to its content rather than to a fixed bottom band, so an empty
  dialog is small and a full one is tall.

**Back dismissal — PASS (B-15 partial).** `KEYCODE_BACK` closed the dialog,
returned focus to `MainActivity`, and did not leave the app. The modal scrim
correctly dims the stray scrollbar too (`rgb(81,84,90)` -> `rgb(77,78,80)`).

---

### B-02 · Empty state lists captures already on the device — deferred

Deferred until two sessions exist (recorded after B-10/B-11). See §3.B-02-late.

### B-03 · Import a small file from device storage — **PASS**

*Path:* `Open log` -> Android `DocumentsUI` picker (opened at *Stahování*, the
device's Czech Downloads) -> search `vc2-small` -> tap the result. The picker is
the platform's, and it materialised the URI without incident.

| Assertion | Oracle | Observed | Verdict |
|---|---|---|---|
| Entry count | `vcat stats` `totalMatching` = **49 994** | `49,994 in view · 49,994 match · 49,994 in session` | PASS |
| Severity split | F 8 349 / E 8 213 / W 8 477 / I 8 253 / D 8 233 / V 8 469 | legend `F 8.3k · E 8.2k · W 8.5k · I 8.3k · D 8.2k · V 8.5k` | PASS |
| Format detection | ThreadTime + `usec`, confidence 1 | imported with no format prompt; timestamps rendered to microsecond precision | PASS |
| Tab name | provider display name | tab chip reads `vc2-small.txt` | PASS |
| Time rendering | file spans `12:13:36.771Z`…`12:14:51.957Z` | axis `14:13:40`…`14:14:40`, i.e. **UTC+2** — the device's own `Europe/Prague` | PASS |
| Desktop-only preview | must be absent | no import-preview override appeared | PASS |
| Initial viewport (R-29) | untouched import shows the whole session | `Fit` state on arrival; `1.25 min · 90.8 ms/px` covers the 75 s session | PASS |
| Paging affordance | — | `Load 500 more · 49,494 remaining` = 49 994 − 500 | PASS |

**Touch-target sweep of the imported workspace:** 16 interactive nodes, **0
below 48 dp, 0 pairwise overlaps** (leaf sizes measured against the device's
real 2.25 px/dp). The command bar, tab chips, mode segments, `Fit`, the three
detail tabs, the sort combo, both entry actions, and *Load 500 more* all clear
the floor.

### B-04 · Read the heat map and reach an exact record — **PASS** (1 finding)

Tapping a dense cell in the **E** row at (600, 745):

- status became `13 in this bar · 49,994 match · 49,994 in session` — the count
  **names its own scope in visible text**, which is exactly what R-36 asks for;
- a `× Cell` chip appeared in *Actions for the current view*;
- the plot drew a caret and a white cell outline at the tapped column;
- the entry list re-scoped to 13 Error records at `14:14:13.283`…, and the first
  was auto-selected.

**Byte-faithful integrity oracle — verified against the corpus, not against the
app.** The selected entry was `E SurfaceFlinger 05-15 14:14:13.283 15620:8278`,
message `Connection 68536 to 10.0.0.8 failed after 1301 ms`. The Entry tab's
*source context* rendered lines 24 150–24 158 with **24 153** marked. Reading the
same range straight out of the file:

```
$ sed -n '24150,24158p' small.txt | cat -A
05-15 14:14:13.278000  1583 25550 V SurfaceFlinger  : Frame completed in 41087 ms$
05-15 14:14:13.281000  6500 21704 W SurfaceFlinger  : Started process 7462 for package com.example.app$
05-15 14:14:13.282000 16201 24015 I ActivityManager : FATAL EXCEPTION: main$
05-15 14:14:13.283000 15620  8278 E SurfaceFlinger  : Connection 68536 to 10.0.0.8 failed after 1301 ms$
05-15 14:14:13.283000 17055  8298 F AndroidRuntime  : Rendering surface 0x0000EC6F$
…
```

Every one of the nine lines matches the panel character for character, and the
1-based line number the panel prints is the one `sed -n Np` names. Cell ->
entries -> selected entry -> raw bytes are consistent. **PASS.**

---

#### V2-04 · The source-context gutter prints a code the product never decodes — **Low/Medium (UX)**

*Surface:* Android companion, *Entry* tab -> *Source context*.

**Observed.** Each source line is rendered as
`<line number> <2-letter code> │ <the file's own bytes>`, e.g.

```
 24152 en  │  05-15 14:14:13.282000 16201 24015 I ActivityManager : FATAL EXCEPTION: main
▶24153 en  │  05-15 14:14:13.283000 15620  8278 E SurfaceFlinger  : Connection 68536 …
```

The code is the parse outcome
(`SessionTabViewModel.DescribeOutcome`): `en` parsed entry, `mt` meta record,
`..` continuation, `e?` untimed entry, `??` unknown line, `!!` rejected
candidate, two spaces for a blank. **Nowhere in the product is that mapping
shown.** The only explanation attached to the control is the phone caption
*"exact bytes, after the │ divider"* — which explains the divider and not the
code — plus a `ToolTip`: *"Each line shows its line number in the file and how
the parser read it, then the file's own bytes after the divider."* That does not
decode a single one of the six codes either.

**And on a phone the tooltip cannot be reached at all.** A 1 200 ms press on the
caption produced a whole-screen pixel diff of `None` — Avalonia opens `ToolTip`
on pointer-over, and a finger never hovers. The string does survive into the
accessibility tree as `content-desc`, so **TalkBack reads it and a sighted touch
user cannot see it** — the inverse of the usual accessibility gap, and the same
shape of defect R-36 was written for ("not only in a tooltip a touch device
never shows").

**Why it matters more than it looks.** This pane exists so a sceptical reader can
check VisualCat against the file. `??` and `!!` are precisely the rows where the
parser is admitting it could not read something — the rows that matter most —
and the reader has no way to learn that. Adversarial imports (A-10) make those
codes common.

**Suggested fix.**

- Put a **legend under the section header**, in the same register as the
  severity legend the filter drawer already has
  (`F fatal · E error · W warn · …`): `en entry · mt marker · .. continuation ·
  e? untimed · ?? unknown · !! rejected`. It is one line, it costs the space of
  the caption it replaces, and it makes the pane self-describing.
- Show it **only when a code other than `en` appears in the visible window**, so
  the ordinary case stays uncluttered and the legend arrives exactly when the
  reader needs it.
- Keep the tooltip for desktop hover, but stop treating it as the primary
  explanation on a touch build.

---

### B-05 · Severity filters — **PASS** (1 finding)

Toggled the six severities off one at a time in the filter drawer, leaving
**Fatal** only, then *Done*:

| Assertion | Observed | Verdict |
|---|---|---|
| Filtered count vs oracle | `8,349 in view · 8,349 match · 49,994 in session` vs `vcat stats` Fatal = **8 349** | PASS |
| Plot agrees with count | plot collapsed to a single **F** lane spanning the full plot height; minimap redrew in F colour only | PASS |
| Filter named in the chip bar | `levels: F ×` plus `Clear all`; the mode button reads `Filters · 1` | PASS |
| Paging | `Load 500 more · 7,849 remaining` = 8 349 − 500 | PASS |
| *Clear all* returns exactly | `49,994 in view · 49,994 match · 49,994 in session`, notice back to `No filters · showing everything in view`, **zero** `levels` nodes left in the tree | PASS |

Drawer sweep: **24 interactive nodes, 0 under 48 dp, 0 overlaps.** The severity
toggles are 49 × 48 dp, the `Regex`/`Case-sensitive` checkboxes 67 × 48 and
119 × 48 dp (label included in the hit area — correct), the query field 361 × 48
dp, *Clear the query* 48 × 48 dp.

---

#### V2-05 · The filter chip's remove button is a 16 dp target, and *Clear all* is 40 dp tall — **Medium**

*Surface:* Android companion, the chip bar under the mode row. Reproduced with a
severity filter and again with a text filter, so it is the chip template, not
one filter kind.

**Measured**, at the device's real 2.25 px/dp:

| Control | Bounds | Size | Floor | Shortfall |
|---|---|---|---|---|
| `Remove filter levels: F` (the chip's `×`) | `[196,512][231,549]` | **15.6 × 16.4 dp** | 48 dp | **−67 %** |
| `Remove filter text = Rendering surface` | `[424,512][459,549]` | **15.6 × 16.4 dp** | 48 dp | −67 % |
| `Clear all` | `[877,486][1030,576]` | 68.0 × **40.0 dp** | 48 dp | −17 % on height |

These were the **only two** sub-floor interactive nodes found anywhere in this
run's sweeps, which is why they stand out: everything around them — the 49 dp
severity toggles, the 48 dp marker-navigation arrows, the 48 dp tab items — was
deliberately given the reserve. The chip's `×` is 10 % of the area of its
neighbours.

**Consequence.** Removing one filter is the most common corrective action in the
whole workspace, and it is the hardest thing on the screen to hit. A miss lands
on the chip body (`levels: F`, 50 × 16 dp, not clickable) or on empty ground —
nothing happens, and the user tries again. `Clear all` is the recovery from that
frustration and is itself 8 dp short.

**Suggested fix.**

- Make the whole chip one 48 dp-high control with the `×` as an inner glyph, and
  give the `×` its own 48 × 48 dp hit rectangle that overhangs the chip's
  padding (`Margin` negative on the button, or a transparent `Border` around the
  glyph). Visual size stays 16 dp; the *target* becomes 48.
- Raise `Clear all` to `MinHeight = TouchTarget.For(mobile)` like its
  neighbours — the codebase already has that helper, and the 40 dp here looks
  like a chip-bar-local style that missed it.
- While there: the chip body itself should be tappable (removing the filter, or
  opening the drawer focused on that filter), so the 50 × 16 dp label stops
  being dead space next to a 16 dp target.

---

### B-06 · Text search and marker navigation — **PASS**

Query `Rendering surface`, entered through the on-screen keyboard.

| Assertion | Oracle | Observed | Verdict |
|---|---|---|---|
| Match count | `vcat search small.vcat "Rendering surface"` -> `matches: 7181`; `grep -c` -> **7181** | `7,181 in view · 7,181 match · 49,994 in session` | PASS |
| Highlighting | — | every occurrence highlighted in-row in magenta | PASS |
| Marker lane | — | a dedicated magenta marker strip under the V lane in the plot | PASS |
| Marker navigation | — | `◀ 3,579 / 7,181 ▶`, both buttons **48 × 48 dp**, named *Previous search match* / *Next search match* | PASS |
| **Zoom span preserved across steps** | — | zoomed to `1.292 s · 1.56 ms/px`, stepped Next ×3: counter 3,579 -> 3,582, span still `1.292 s · 1.56 ms/px`, in-view count 129 -> 130 | PASS |
| Paging under search | — | `Load 500 more · 6,681 remaining` = 7 181 − 500 | PASS |

**Wrap at both ends could not be exercised** on a 7 181-match query without
thousands of taps, and no jump-to-first/last control exists. Recorded as a
**coverage gap**, not a pass — see V2-07.

**R-16 / U-03 (soft keyboard) — PASS, and notably good.** Focusing the query
field raised the Samsung HoneyBoard (`mInputShown=true`). The drawer **recomposed
rather than scrolled**: the query field stayed mounted and focused, the severity
and time-lens sections compacted, and the footer (`No active filters` · `Reset` ·
`Done`) stayed fully visible **above** the IME at y = 1 952…2 060 while the IME
occupied y ≈ 2 070 upward. Nothing was covered and nothing lost focus.

> That the drawer *can* size itself to the available height is worth holding on
> to when reading V2-02: the layout has the mechanism, it is simply not
> applied when there is space to spare rather than space to save.

---

#### V2-07 · Search has no way to reach the first or last match — **Low (UX), coverage-blocking**

*Surface:* Android companion, search marker navigation.

**Observed.** The only navigation is `◀` / `▶`, one match at a time. With
`Rendering surface` the counter opened at **3 579 / 7 181** (the caret's position
in the session, not the first match). Reaching match 1 needs 3 578 taps;
reaching 7 181, another 3 602. There is no *first*, *last*, no long-press
accelerator, no way to type a match index, and the `3,579 / 7,181` label is not
interactive.

**Consequences.** Two, and the second is the serious one:

1. A user who searches a large log to find *the first* occurrence of something —
   the single most common reason to search a log — cannot get there.
2. **The plan's own B-06 assertion "navigation … wraps at both ends" is not
   testable through the UI.** This run records it as a coverage gap rather than a
   pass, and any future run on any device will hit the same wall.

**Suggested fix.** Make the counter a control: tap `3,579 / 7,181` to jump, and
add `⏮`/`⏭` (or long-press on `◀`/`▶`) for first/last. The marker lane in the
plot is already drawn; a tap on it should also seek. All three are cheap next to
the machinery that already computes the match set.

---

#### V2-06 · A filter that excludes the open entry leaves it open, and the two controls that act on it disagree — **Medium**

*Surface:* Android companion, *Entries* action row + *Entry* inspector.
*Reproduced twice* along the path below; a plain select-then-filter path does
**not** reproduce it, so the trigger is the cell-selection route.

**Reproduction (exact).**

1. `Fit` the session, tap a dense cell in the **E** lane -> `13 in this bar`,
   first Error row auto-selected.
2. *Entry* tab -> tap **Show the source bytes** (the source context expands and
   loads).
3. *Entries* tab -> tap the `× Cell` chip to drop the cell filter.
4. *Filters* -> turn off E, W, I, D, V, ? leaving **F** -> *Done*.

**Observed.** With `levels: F` active and `8,349 in view · 8,349 match`:

| Control | Acts on | State |
|---|---|---|
| `Copy raw` | the selected entry | **disabled** — correct, the selection is gone |
| `Entry ⤢` (*Show the full message of the selected entry*) | the selected entry | **enabled** |

Tapping the enabled one opens the *Entry* tab showing
`E SurfaceFlinger · 05-15 14:14:13.283 · 15620:8278 · main · tpl 40`,
`Connection 68536 to 10.0.0.8 failed after 1301 ms` — an **Error** record, under a
filter that admits only **Fatal**. The pane still offers *Copy the whole
message* and *Hide the source bytes* on it. The only tell that anything is
unusual is a **missing** line: the `Row N of M in the selected bar` caption that
the same pane shows when the entry *is* in scope simply is not rendered.

**Why it matters.** Two controls labelled for the same object report different
answers about whether that object exists. A reader who filters to Fatal to
triage a crash can be looking at an Error record with no indication it is
outside what they asked for — and can copy its message believing it came from
the filtered set.

**Suggested fix.** Either behaviour is defensible; the current mixture is not.

- **Preferred: keep the entry, label it.** Persisting what the reader was
  studying across a filter change is kind. Say so: a one-line banner in the
  inspector — *"Not in the current filter · Clear filters to see it in context"*
  with the action — and re-enable `Copy raw` for it, since the entry is right
  there and copying it is not a lie. This also matches the pattern the app
  already uses in the entries list ("This entry is outside the rows on screen.
  It is still open below. **Show it**"), which is exactly the right idiom; it
  simply is not applied on this path.
- **Or: drop it, consistently.** Clear the inspector and disable both controls,
  as the plain select-then-filter path already does.

Whichever is chosen, `Copy raw` and `Entry` must be driven from one predicate.
The bug is that they are not.

---

### B-07 · Regex search — **PASS** (with a design note)

| Pattern | Expectation | Observed | Verdict |
|---|---|---|---|
| `Rendering (surface\|buffer)` | valid, matches | chip reads **`regex = …`** (distinct from `text = …`); `7,181 in view · 7,181 match`; CLI `vcat search --regex` = 7 181 | PASS |
| `Rendering surface 0x0000[0-9A-F]{4}` | a strict subset | `4,716 in view · 4,716 match`; `grep -cE` = **4716** | PASS |
| `(a+` | invalid, clear message, no crash | live inline error under the field, red, in product language: **"Not a valid regular expression: there are more \"(\" than \")\" (position 3)."** The field border turns red, the footer stays `No active filters`, nothing is applied. | PASS |
| `([a-zA-Z0-9]+)+ZZZ` | pathological | applied in ≈ 18 s wall clock **with the app responsive throughout** (`mCurrentFocus` stayed on `MainActivity` at every 2 s sample); result `0 in view · 0 match · 49,994 in session` | PASS |

**Why the pathological pattern did not need a timeout, and where the timeout
still lives.** `SessionQueryEngine.CompileSearchRegex` compiles with
`RegexOptions.NonBacktracking` and a 250 ms `MatchTimeout`. NonBacktracking is
linear-time, so `([a-zA-Z0-9]+)+ZZZ` cannot blow up and `0 match` is the
**correct and complete** answer, not a truncated one. That is a good design and
it deserves to be recorded as such.

The engine falls back to the backtracking engine — `catch (NotSupportedException)`
— for patterns NonBacktracking rejects: **lookarounds and backreferences**. On
that path the 250 ms timeout is real, and:

- `EntryHighlight` catches `RegexMatchTimeoutException` and silently keeps
  partial highlighting (deliberate, documented in the code);
- **the query path does not.** `SessionQueryEngine` calls `regex!.IsMatch(message)`
  inside the per-entry predicate with no catch, so a timeout there propagates out
  of the query.

The plan's B-07 expectation is that a cut-off pattern "**says so**". Whether it
does is a property of the lookaround/backreference path only; it is exercised in
**A-10** against `longline.txt`, and the result is recorded there.

---

### B-08 · Zoom, pan, and Fit — **PARTIAL** (1 finding)

| Assertion | Observed | Verdict |
|---|---|---|
| `Fit` returns to the whole session in one tap | from any zoom, one tap restores `49,994 in view` and the full 75 s span | PASS |
| `Fit` reachable without opening the drawer | it is the fifth segment of the mode row, `[913,371][1039,479]`, 56 × 48 dp (**R-13**) | PASS |
| Row geometry stable when `Fit` hides | with the drawer open `Fit` becomes `enabled=false` and is not painted, **but its bounds are unchanged** — `Plot`/`Split`/`Details` do not move (**R-13**) | PASS |
| Double-tap zooms only (**R-32**) | a genuine double-tap (two `input tap` in one shell round trip) took `49,994 -> 25,055 in view`, **no `× Cell` chip, no `in this bar` scope** — the entry table and chip bar were untouched | PASS |
| Zoom stays within the session | see V2-09 | **FAIL** |
| Minimap and viewport agree | brush width tracks the viewport's overlap with the session; see V2-09 for the consequence at the bounds | PASS (with caveat) |

> **Note on the R-32 method.** Two `input tap` calls with a `sleep 1` between
> them are *not* a double-tap — each is a separate process launch far outside
> Android's ~300 ms double-tap window, and they read as two cell selections
> (`10 in this bar`). Issuing both in one `adb shell "input tap …; input tap …"`
> lands them close enough. A future run that gets "double-tap re-scopes the
> list" should check this before filing it.

---

#### V2-09 · Panning past either end of the session shows up to 4 s of time that does not exist — **Low/Medium**

*Surface:* Android companion, timeline pan clamp.

**Observed.** The session's declared range is exactly
`firstInstant 2026-05-15T12:13:36.771Z` … `lastInstant 12:14:51.957Z`
(75.186 s, read from `vcat info small.vcat` — the same session content the device
imported). Panning hard against either bound leaves the plot showing empty time
**outside** that range:

| State | Viewport span | Plot area | First data pixel | Empty margin |
|---|---|---|---|---|
| `Fit` | 75.2 s | x 199…1027 | 200 | none |
| panned to end | 37.593 s (45.4 ms/px) | 199…1027 | last data at **944** | 83 px = **3.77 s after the last record** |
| panned to start | 37.593 s | 199…1027 | first data at **286** | 87 px = **3.95 s before the first record** |
| panned to start, zoomed in | 4.699 s (5.68 ms/px) | 199…1027 | first data at **900** | 701 px = **3.98 s**, i.e. **78 % of the plot** |

The margin is **constant in time, not in pixels** — ~3.97 s at both zooms, or
about **5 % of the session's duration** at each end. The axis is honest about it:
at the deep zoom it prints `14:13:34.000` and `14:13:36.000` for a session whose
first record is at `14:13:36.771`.

**Two consequences, and the second is the one that bites.**

1. Pan to the beginning of a log at a working zoom and **four-fifths of the
   screen is empty**, with a time axis for an interval in which the log did not
   yet exist. It reads as "the data failed to draw", not as "you are at the
   start".
2. **The minimap brush degenerates.** The minimap spans the session, so a 4.699 s
   viewport sitting 4 s before the session start overlaps it by only ~0.7 s —
   0.9 % — and the brush collapses to an **8 px sliver** at the far left. The
   control whose job is "where am I" becomes unreadable exactly at the edge
   where a reader most wants it. (The minimap is not *wrong*: it draws the true
   intersection. The overscroll is what makes it useless.)

**Suggested fix.** Keep an overscroll margin — it is a good affordance for "you
have reached the end" — but bound it by the *viewport*, not by the session:

```
margin = min(0.05 * sessionDuration, 0.10 * viewportDuration)
```

At `Fit` this is unchanged; at the 4.7 s zoom the empty band drops from 3.98 s
(78 % of the plot) to 0.47 s (10 %), which reads as an edge rather than as a
blank screen. Alternatively make it rubber-band: allow the overscroll during the
gesture and settle back to the true bound on release, which is what the platform
does everywhere else and needs no constant at all.

---

### B-09 · Workspace modes — **PASS**

Cycled Plot -> Details -> Split, then rotated to landscape and re-read.

| Mode | Timeline bounds | Entries `ListBox` | Rows fully visible | Floor | Verdict |
|---|---|---|---|---|---|
| **Plot** | `[27,587][1053,2051]` (1 464 px, 651 dp) | absent | — | — | PASS |
| **Split** | `[27,587][1053,921]` | `[72,1464][1008,2022]` | **4** | >= 4 (R-06) | PASS |
| **Details** | absent | `[72,995][1008,2022]` | **7** full + 1 partial | >= 6 (R-06) | PASS |

**R-06 holds *with* a notice showing.** Both Split and Details were measured
while the banner *"This entry is outside the rows on screen. It is still open
below. **Show it**"* occupied a full row's height. In Split the plot gave up
138 px (from `…1059` to `…921`) so the list could keep its four rows — the floor
is enforced by taking space from the plot, which is the right trade.

**Rotation — PASS.** `wm user-rotation lock 1` -> `w1040dp h480dp`,
`mRotation=ROTATION_90`. The chosen mode (**Split**) survived, and landscape is a
genuinely different composition rather than a stretched portrait one:

- command bar and mode selector merge into **one** row (`Open log · Live ·
  More · Filters · Plot · Split · Details · Fit`);
- Split becomes side-by-side, plot left / details right, with the splitter as a
  vertical 48 × 368 dp grip;
- entry rows collapse to two lines with the timestamp promoted inline;
- the entry action row compacts to `Copy · Entry · +500`;
- the minimap stays in the plot column at **372 × 21 dp (47 px)** — above the
  26 px R-17 asks for;
- the severity legend regains its per-lane counts.

**Safe area in landscape (U-07 evidence).** Content is inset ~100 px on the left,
which is where `ROTATION_90` puts the centre cutout, and clear of the
right-hand navigation bar. Status text sits at `[140,1039][2191,1076]`, inside
the 108 px corner radii. **Landscape sweep: 15 interactive nodes, 0 under 48 dp,
0 overlaps.**

---

### B-10 / B-16 / W0 / W6 · On-device live capture, own-app scope — **PASS** (1 finding)

*Pre:* Release candidate, clean install, no Wireless debugging configured,
`POST_NOTIFICATIONS` never granted.

**The scope chooser (W0).** Tapping *Live* opened *Choose what Live captures*
with two radio options and a body that reads, verbatim:

> Nothing is uploaded. Full-device capture uses Android Wireless debugging only
> on this device, uses the connection only to read the Android log, and closes
> its connection when Live stops. Android leaves Wireless debugging enabled
> until you turn it off in Settings. While Live runs, Android shows a private
> ongoing notification so capture can continue with the screen off and you can
> Stop and save. Android may end background capture after its six-hour service
> limit; everything already received is kept.

| W0 expectation | Observed | Verdict |
|---|---|---|
| Says nothing is uploaded | first sentence of the disclosure | PASS |
| Does **not** promise a `READ_LOGS` prompt | the word `READ_LOGS` does not appear in the chooser at all; the full-device path is described purely as Wireless debugging | PASS |
| Full-device offered as an optional path | *Full-device capture · Recommended · setup required*, preselected | PASS |
| Restricted path is not made to look like an error | *Capture VisualCat only · No setup · "Starts immediately, but Android exposes only VisualCat's own log lines. If VisualCat is idle, Live may show few or no new lines"* | PASS |
| No redundant legacy confirmation | one sheet, then capture; nothing else | PASS |

**The primary button is bound to the selection**, which is a small thing done
right: with *Full-device* selected it reads **`Set up full-device`**; selecting
*Capture VisualCat only* changes it to **`Start VisualCat-only`**. The user
always knows what the button will do.

**Notification permission and the W6 repost.** On tapping *Start VisualCat-only*
Android showed its runtime prompt (*"Povolit aplikaci VisualCat odesílat
oznámení?"*) — after the decision to capture, not before. Allowing it produced
the strongest single piece of W6 evidence in this run: **the app's own first
captured line is**

```
I VisualCat.CaptureService  Requested a foreground-notification repost after notification permission…
08-29 19:55:02.685879 · 6190:6190 · main
```

— the same first capture explicitly reposting after the grant, exactly as W6
requires, and observable because the capture is reading its own log.

**The foreground service and its notification (W6).**

| Property | Value | Requirement |
|---|---|---|
| Channel | `visualcat-live-capture` | — |
| Title / text | `VisualCat live capture` / **`VisualCat logs are being saved locally.`** | contains no log content — PASS |
| Visibility | `vis=PRIVATE` | private — PASS |
| Flags | `ONGOING_EVENT｜ONLY_ALERT_ONCE｜NO_CLEAR｜FOREGROUND_SERVICE｜SILENT`, `category=service`, `importance=2` | PASS |
| Actions | exactly one: **`Stop and save`** -> `startService` | PASS |

**Scope honesty (R-02) — every surface agrees.**

| Surface | Text |
|---|---|
| Command bar | `Live` -> **`Recording`** with a filled indicator |
| Tab chip | `On-device logcat 19h54m13` (carries the start time — **R-07**) |
| Session status | `Capturing · 32 entries · On-device own-app logcat` |
| Notice lane | `Only VisualCat's own log lines are being captured — own-app scope only.` |
| Notice body | *"This Release build intentionally does not declare Android's privileged READ_LOGS permission. Stop this capture, tap Live again, and choose full-device access to use the recommended Wireless debugging path."* |

Nothing anywhere claims full-device. **R-02 PASS.**

**R-22 — the status stops claiming arrivals after silence. PASS, and this is a
model answer.** Sampled every 10 s across a two-minute idle:

```
t+10s   Capturing · 44 lines received · no source lines for 8s  · On-device own-app logcat
t+30s   Capturing · 44 lines received · no source lines for 31s · own-app scope only · On-device own-app logcat
t+50s   Capturing · 46 lines received · no source lines for 17s · On-device own-app logcat
t+70s   Capturing · 46 lines received · no source lines for 40s · own-app scope only · On-device own-app logcat
t+90s   Capturing · 41 entries · On-device own-app logcat
t+110s  Capturing · 52 lines received · no source lines for 11s · On-device own-app logcat
```

The rate is never reported as a stale figure; after a few seconds of silence the
line switches to a **heartbeat** naming how long the source has been quiet, and
past ~30 s it *adds the reason* (`own-app scope only`). An idle capture is
plainly distinguishable from a failed one, which is exactly what B-16 asks for.

---

#### V2-11 · The live notice lane clips its own last line, and the clipped words are the instructions — **Medium**

*Surface:* Android companion, *Application status* lane during on-device live
capture.

**Observed.** The lane's `Border` is `[0,1957][1080,2232]` — 275 px, and 2 232 is
where the navigation bar begins. Its `TextBlock` is `[24,1973][832,2237]`,
i.e. **264 px of text laid out to y = 2 237, five pixels past the border and
past the bottom of the usable screen**. The last visible line is cut through the
middle of the glyphs. The full string is:

> Only VisualCat's own log lines are being captured — own-app scope only.
>
> This capture can only see this app's own log lines, so an idle app produces
> almost nothing. This Release build intentionally does not declare Android's
> privileged READ_LOGS permission. **Stop this capture, tap Live again, and
> choose full-device access to use the recommended Wireless debugging path.**

Everything from *"…to use"* onward is below the fold. The lane does have an
internal scrollbar, so the text is reachable — by scrolling **inside a
four-line notice**, on the one screen where the user has just discovered that
their capture sees almost nothing.

**Two smaller faults in the same lane.**

- **The notice does not change tense when the capture ends.** After *Stop*, with
  the live controls gone and the status reading `Stopped · 47 entries kept`, the
  lane still says *"are being captured"*. It persists until *Dismiss* is tapped.
- **The scope claim is the part the status line truncates.** During the quiet
  heartbeat the status renders as `Capturing · 37 lines received · no source
  lines for 18s · On-device o…` — the scope, which R-02 makes load-bearing, is
  the clause that falls off the end. (The full string is in the node's text and
  `content-desc`, so a screen reader hears it; a sighted user does not.)

**Suggested fix.**

- Size the lane to its content and let the *page* scroll, or cap the notice at
  two lines with a **More** disclosure. A scroll container 4 lines tall is the
  worst of both.
- Reserve the bottom inset properly: the text is laid out 5 px past its own
  border, which suggests the padding is applied to the border and not to the
  text's available height.
- Put the remedy in a **button** (*Switch to full-device…*) rather than in the
  tail of a paragraph. It is the only action the notice suggests and it is the
  part that gets clipped.
- Recompose the notice when the capture stops: *"This capture recorded
  VisualCat's own log lines only."*
- Give the status line an explicit priority order so the scope survives
  truncation ahead of the byte/line counters.

---

### B-12 / R-01 · Stop capture is answered and sticky — **PASS**

*Steps:* one tap on **Stop capture**, no second tap, sampled every 700 ms.

| Sample | Status line | Live controls |
|---|---|---|
| +0.7 s | `Stopped · 47 entries kept` | gone |
| +1.4 s … +7.0 s (10 samples) | `Stopped · 47 entries kept` | gone |

The label never sprang back to *Stop capture*, the status never returned to
*Capturing*, and the state resolved before the first sample. `Follow ✓` and
`Stop capture` disappeared; the command-bar button reverted from `Recording` to
`Capture this device's log`.

> The intermediate stages the plan names (draining, compacting, writing the
> index, reopening) were **not observable** on a 47-entry capture — they
> completed inside 700 ms. This is a scale artefact of W0 own-app scope on an
> idle device, not evidence that the stages are missing. A capture large enough
> to expose them needs full-device scope; see §12 (coverage gaps).

**Teardown — PASS.** After the stop, `dumpsys activity services` holds **no**
`ServiceRecord` for the package, and the notification appears only in
`mArchive`, not in the active list. The foreground service and its notification
were both taken down.

### B-13 · Reopen a finished session — **PASS** (1 finding)

Closed the capture's tab, reopened it through *More -> Recent sessions… ->
select -> Open*.

| Assertion | Observed | Verdict |
|---|---|---|
| Same entry count | `47 in session`, status `Ready · 47 entries` — identical to `Stopped · 47 entries kept` | PASS |
| Reopen time | complete at the **first** 3.2 s sample, which includes ~2.5 s of `uiautomator dump` overhead; budget is <= 5 s | PASS |
| Working plot | plot, minimap, entries, and Fit all live | PASS |
| No "empty list under a Ready status" | 2 rows drawn, 47 in session, consistent | PASS |

**Session list quality.** *Recent VisualCat sessions* listed both stored
sessions as 408 × 70 dp rows:

```
On-device logcat 19h54m13     2026-08-29 19:58 · 56.49 KiB · complete
vc2-small                     2026-08-29 19:23 · 12.59 MiB · complete
```

Each carries its start time, size, and status — **R-07 PASS**, and the two are
trivially distinguishable.

---

#### V2-10 · A reopened capture opens on a 30-second window at the live edge, so the plot is empty — **Medium (UX)**

*Surface:* Android companion, reopening any finished live capture. Survives a
force-stop and relaunch too, so it is persisted, not incidental.

**Observed.** The 47-entry capture spans about 4 minutes. Reopened, it presents:

- `DENSITY · 30 s · 36.23 ms/px` — a **30-second** viewport pinned to the last
  moment of the capture;
- a plot that is empty except for one thin bar at the extreme right edge;
- a severity legend reading `F 0 · E 0 · W 0 · I 2 · D 0 · V 0`;
- `2 in view · 47 match · 47 in session`;
- a minimap that (correctly) shows records scattered across the whole session
  with the brush parked at the far right.

The same 30 s window came back after `am force-stop` + relaunch, alongside the
restored tabs.

**Why it is wrong.** The 30 s live-edge window is **Follow's** viewport — the
right answer while a capture is running and the reader wants the newest lines.
The capture is not running any more. R-29 states the principle for the
equivalent moment on the import path: *"An import ends showing the whole
session. An untouched viewport follows the session; the first zoom or pan hands
it to the reader for good."* Reopening a finished capture is the same
situation — the reader has not touched the viewport — and it gets the opposite
treatment. The first thing they see is an empty plot with five zero counters,
which reads as *"the capture recorded nothing"*.

R-23 already establishes that *Follow* belongs to a running capture and should
go when the source closes. Its **viewport** should go with it.

**Suggested fix.** When a session is opened from storage — *Recent sessions*,
the empty-state list, a restored tab — start at `Fit`, exactly as an import
does. Persist a viewport only when the reader chose it (zoom or pan), and treat
a Follow-derived window as not chosen. If the live-edge window is worth keeping
for the running case, drop it at finalize rather than writing it into the
session.

### B-02 · Empty state lists captures already on the device — **PASS**

*Note on the plan's steps.* `am force-stop` + relaunch does **not** reach the
empty state on this build: the app **restores its open tabs** (both sessions came
back, in 2 021 ms cold). That is a feature, not a failure — but B-02 as written
cannot see the empty state, so the tabs were closed first.

With both tabs closed the empty state carries an inline section:

```
RECENT CAPTURES ON THIS DEVICE
  On-device logcat 19h54m13      2026-08-29 19:58 · 56.49 KiB · complete
  vc2-small                      2026-08-29 19:23 · 12.59 MiB · complete
```

Rows are 360 × 56 dp buttons named *"Reopen On-device logcat 19h54m13,
2026-08-29 19:58…"*. One tap reopens. Named after the capture (with its start
time), not after a storage folder. **R-12 PASS.**

> **This retires half of V2-02 and sharpens the other half.** The inline
> recent-captures list already exists and already fills the space — it simply is
> not rendered when there are none, which is the state a first-run user is in.
> So V2-02's first suggestion is not a redesign but "render the section you
> already have, with an empty state". It also exposes a **redundancy**: the
> `RECENT CAPTURES` hero button opens a dialog listing exactly the rows printed
> underneath it. With captures present the button is dead weight; with none, it
> is the dead end described in V2-03.

Even populated, the empty state still leaves **922 px (410 dp, 41 % of the
workspace)** blank below the identity line, so V2-02's centring point stands.

---

## 4. Tier X and Tier A — volume, adversarial input, and failure presentation

### X-01 · One million lines imported on the device — **PASS**

*Route:* the system picker, same as B-03. `vc2-large.txt`, 90 017 930 B,
1 000 001 physical lines.

| Assertion | Oracle | Observed | Verdict |
|---|---|---|---|
| Entry count | `vcat index large.txt` -> `entries 999885, unknown 115, templates 77` | `999,885 in view · 999,885 match · 999,885 in session`, `Ready · 999,885 entries` | **exact** |
| Severity split | sums to 999 885 | `F 166.3k · E 166.4k · W 166k · I 167.3k · D 166.7k · V 167.2k` | PASS |
| Progressive snapshots | ingest must publish before it finishes | `800,001 in session` visible at **+14.8 s**, final at **+23.3 s** | PASS |
| Import throughput | >= 30 000 lines/s | 999 885 entries in <= 23.3 s from the tap = **>= 42 900 entries/s** | PASS (1.4x the gate) |
| Untouched viewport shows the whole session (**R-29**) | — | `24.98 min · 1.81 s/px`, `in view` == `in session` | PASS |
| Paging label | — | `Load 500 more · 999,385 remaining` | PASS |
| Memory | no OOM/LMK; record peak | `TOTAL PSS` **212 978 KB -> 369 099 KB** (+152 MB), `RSS` 311 888 -> 466 924 KB. No LMK kill, no `AndroidRuntime` line | PASS (baseline recorded) |

**Search over 1 M entries — PASS, well inside budget.** With the session open,
the query `Rendering surface` was applied and the screen captured in a tight
`exec-out screencap` loop (measured round trip 440–467 ms):

- **frame 1, +544 ms after the tap**, already reads
  `142,537 in view · 142,537 match · 999,885 in session`;
- `grep -c "Rendering surface" large.txt` = **142537**. Exact.

Budget is <= 1.5 s to first results; the first observation possible with this
instrument already showed the final count, so the true figure is **<= 544 ms**.

#### Frame-pacing assertions — **BLOCKED**, and the plan should say why

`dumpsys gfxinfo com.barebit.visualcat` reports **`Total frames rendered: 0`**
after an interaction burst, with every percentile pinned at `4950ms`. This is
not a product fault: VisualCat draws through a **`SurfaceView`**
(`SurfaceView[com.barebit.visualcat/…MainActivity]@0#8943` in
`dumpsys SurfaceFlinger --list`), and `gfxinfo` only instruments the HWUI view
hierarchy, which for this app renders nothing. The plan's §4.3 prescribes
`gfxinfo … framestats` as the jank oracle; on this architecture it measures an
empty pipeline and would report a **frozen renderer as perfectly smooth** — the
exact trap §4.3 warns about, arriving through the prescribed command.

The documented fallback also failed: `dumpsys SurfaceFlinger --latency <layer>`
returns only the refresh period (`8333333` ns = 120 Hz) and **zero frame rows**
for every layer-name form tried, and neither `ffmpeg` nor OpenCV is available on
the host to count frames out of a `screenrecord`.

Per plan §4.3 ("If the required signal cannot be measured, mark the quantitative
assertion Blocked"), every frame-level budget in this run is **BLOCKED**:
tap→acknowledgement p95, mode-switch <= 300 ms, drawer <= 250 ms, pan/zoom jank
<= 15 %, entry paging <= 400 ms. Wall-clock and state-transition assertions were
used everywhere they could substitute, and are reported as such.
See §11 for the suggested plan amendment.

---

### A-09 · Import failure presentation — **PASS** (1 finding)

`vc2-notalog.txt` (10 MiB of seeded random bytes named `.txt`) produced a proper
failure card, not a hollow workspace — **R-15 PASS**:

> **IMPORT FAILED**
> **This log could not be read**
> No supported logcat format could be detected in this file.
> VisualCat reads Android logcat text — the output of logcat, or a .vcat session
> or portable archive. Check that this file is a logcat capture and not, say, a
> bug report or an application log in another format.
> `[ Open another log ]` `[ Close this tab ]`

Reason, remedy, and two actions, with no desktop-only advice. The tab chip is
labelled *"Show failed session vc2-notalog.txt"* in the accessibility tree, so
the failure is discoverable without sight. Status line reads `Failed · No
supported logcat format could be detected in this file.`

---

#### V2-12 · A zero-byte file is reported as "no supported logcat format", not as empty — **Low**

*Surface:* Android companion, import of an empty file.

**Observed.** `vc2-empty.txt` (**0 bytes**) produces the byte-for-byte identical
presentation to 10 MiB of random noise: the same card, the same sentence
(*"No supported logcat format could be detected in this file."*), the same
paragraph advising the user to check that it is a logcat capture and not a bug
report.

It is not a detection failure. There is nothing to detect. Plan §3.2 lists
`empty.txt` specifically to probe **"Empty-source messaging"**, and this build
has none.

**Also worth fixing while there: the same sentence is on screen three times.**
The failure card, the session status line (`Failed · No supported logcat
format…`), and the notice lane (`Could not complete that action · No supported
logcat format…`) all carry it simultaneously. The notice lane adds nothing the
card does not say more clearly, and it is the one that has to be dismissed by
hand.

**Suggested fix.** Branch on length before detection: *"This file is empty —
there is nothing to import."* with the same two actions. While there, suppress
the notice-lane copy when a full-page failure card for the same event is already
showing.

---

### A-10 · Adversarial corpora sweep — **PASS** (2 findings)

Each file was opened with an `ACTION_VIEW` `content://media/…` intent (see
§2.3), the resulting counts read from the accessibility tree, and compared with
`vcat index` run on the host over the identical bytes.

| Corpus | CLI oracle | Device | Verdict |
|---|---|---|---|
| `crlf.txt` (CRLF endings) | 49 994 | `49,994 in session` | **exact** |
| `bom.txt` (UTF-8 BOM) | 49 994 | `49,994 in session` | **exact** |
| `nonutf8.bin` (raw `0x80–0xFF` injected) | 49 532 entries, 21 unknown, confidence 0.985 | `49,532 in session` | **exact** |
| `truncated.txt` (cut mid-line at byte 1 000 037) | 11 110 entries, 2 unknown | `11,110 in session` | **exact** |
| `nofinalnewline.txt` | 1 000 | `1,000 in session` | **exact** |
| `outoforder.txt` (5 000 records shuffled) | 5 000 | `5,000 in session` | **exact** |
| `dst.txt` (spans the Europe/Prague DST jump) | 200 | `200 in session` | **exact** |
| `crashy.txt` (18 injected `FATAL EXCEPTION` records) | 50 014 | `50,014 in session` | **exact** |
| `longline.txt` (one 2 MiB message) | 2 | `2 in session` | **exact** |
| `continuations.txt` (200 Java traces) | 600 entries, **1 200 unknown**, confidence 0.337 | `600 in session` | exact on entries — see V2-14 |
| `mixed-formats.txt` (ThreadTime + Brief) | 2 225 entries, **1 200 untimed**, 1 unknown | `2,225 in view · **3,425 match** · 2,225 in session` | see V2-13 |
| `notalog.txt`, `empty.txt` | not a log | failure card | A-09 |

**Eleven of twelve counts match the host oracle exactly**, including the two that
most often go wrong — a file cut in the middle of a record, and a file with no
trailing newline. No crash, no ANR, no hang on any of them.

**`longline.txt` — bounded line handling PASS.** The 2 MiB message renders as a
normal 64 dp row, truncated with an ellipsis; the following record is intact and
correctly timestamped; the plot, minimap, and counts are all sane. Nothing
about the row's geometry betrays that it holds two million characters.

---

#### V2-13 · `match` can exceed `in session`, because the three counters count three different populations — **Medium**

*Surface:* Android companion, the entries-tab count line. Reproduced with
`mixed-formats.txt`.

**Observed.** With that file open the line reads, verbatim:

```
2,225 in view · 3,425 match · 2,225 in session
```

**3 425 > 2 225.** On its face the sentence says more entries matched than the
session contains. The resolution is in the CLI oracle: `entries 2225, untimed
1200`. The 1 200 Brief-format records parsed as **untimed** entries. They are
counted by `match` (the filter predicate accepted them) but not by `in session`
(timed population) and not by `in view` (a time range cannot contain a record
with no time).

**Two consequences.**

1. The count line is the workspace's primary integrity readout, and here it is
   self-contradictory with no explanation on screen.
2. **The 1 200 untimed records are otherwise invisible.** The severity legend
   sums to exactly 2 225 (`362+399+380+362+351+371`). The plot draws none of
   them. The minimap shows none of them. `Load 500 more · 1,725 remaining`
   counts from 2 225. Nothing anywhere tells the reader that a third of their
   file has no timestamp and is therefore excluded from every time-based view —
   which is precisely the "honest parsed/unknown accounting" A-08 asks for.

**Suggested fix.**

- Never let the middle number exceed the last with no explanation. Either scope
  `match` to the timed population and report untimed separately, or make the
  line say so: `2,225 in view · 2,225 of 2,225 timed match · +1,200 untimed`.
- Give untimed records a **home**: a chip in the chip bar (`1,200 untimed`) that
  filters the entries list to them, and a line in *Insights*. They are real
  records the reader may need; today they can only be reached by not knowing
  they exist.
- Say it once at import: a notice-lane line *"1,200 records carried no usable
  timestamp and are not on the timeline."*

---

#### V2-14 · A crash log loses two-thirds of its lines to the `unknown` bucket, and nothing says so — **Medium/High (UX)**

*Surface:* Android companion, any ThreadTime log containing indented
continuation lines — i.e. **every Android crash log**.

**Observed.** `continuations.txt` holds 200 copies of a Java stack trace: three
ThreadTime records (`FATAL EXCEPTION: main`, `Process: …, PID: …`, the exception
line) followed by six `\t`-indented frames (`at com.example.app.Boom.explode(…)`,
a `Caused by:`, a nested frame, `... 12 more`). 1 800 source lines.

VisualCat imports it and reports, on every surface:

```
600 in view · 600 match · 600 in session          Ready · 600 entries
```

**The 1 200 stack frames — the part of a crash log a person actually reads — are
not counted, not plotted, not searchable as entries, and never mentioned.** The
CLI is explicit about them (`entries 600, unknown 1200, confidence 0.337`); the
device says nothing at all.

**The bytes are not lost, and that matters.** Selecting an entry and expanding
*Source context* shows them, byte-faithfully, correctly tagged:

```
   1 mt  │  --------- beginning of crash
▶  2 en  │  05-15 14:13:40.000000 10503  5136 E AndroidRuntime  : FATAL EXCEPTION: main
   3 en  │  05-15 14:13:40.000000 10503  5136 E AndroidRuntime  : Process: com.example.app, PID: 10503
   4 en  │  05-15 14:13:40.000000 10503  5136 E AndroidRuntime  : java.lang.IllegalStateException: VCATMARK-continuation
   5 ??  │      at com.example.app.Boom.explode(Boom.java:42)
   6 ??  │      at com.example.app.Boom.access$000(Boom.java:11)
   7 ??  │      at android.os.Handler.dispatchMessage(Handler.java:106)
```

**This is not a parser bug.** [ADR 0009](adr/0009-continuations.md) decides it:
*"Long-format state explicitly owns body lines. Other unmatched lines remain
unknown unless a declared grammar proves continuation,"* on the grounds that
*"attaching every unknown line to the prior entry hides malformed evidence."*
Under ThreadTime there is no declared grammar for an indented frame, so
`unknown` is the **correct** classification and the source pane proves nothing
was dropped.

**The defect is that the decision is invisible.** A reader who opens a crash log
sees `600 entries` and has no way to know that 1 200 lines exist, are stored,
and are reachable — and the only place they surface is behind an undecoded `??`
(see V2-04).

**Suggested fix — presentation only; do not change ADR 0009.**

1. **Count them where the reader looks.** Extend the count line, or add a
   persistent chip: `600 entries · 1,200 unknown lines`. The store already knows
   the number; the CLI already prints it.
2. **Make the unknown population reachable.** The filter drawer has an
   `IncludedOutcomes` concept in the engine (`ParseOutcomeKind`); expose it as a
   filter — *"show unknown lines"* — so a reader can pull the stack frames into
   the entries list deliberately.
3. **Say it once, at import, when the ratio is high.** `confidence 0.337` and a
   2:1 unknown-to-entry ratio is exactly the signal for a notice: *"Two-thirds of
   the lines in this file are not logcat records — probably stack traces.
   They are kept and visible in each entry's source context."* That sentence
   turns a silent loss into a feature, which is what ADR 0009 intended.

---

### B-14 · Export and share a session — **PASS** (1 finding)

Session under test: `vc2-crashy.txt`, 50 014 entries.

**CSV export — integrity verified against the CLI, byte level.**

| Step | Observed |
|---|---|
| *More -> Export CSV…* | goes **straight** to the platform create-document picker |
| Default filename | **`vc2-crashy.csv`** — derived from the capture, as B-14 requires |
| After saving | notice lane: **`Exported 50,014 rows (entries matching the filter)`** |
| With `levels: F` applied | notice lane: **`Exported 8,349 rows (entries matching the filter) to vc2-crashy.csv (1)`** — 8 349 is the corpus's exact Fatal count |

The written file was pulled back and compared with `vcat export --type csv` run on
the host over the same session:

- header identical, **including the UTF-8 BOM** (matching the *Normalized CSV
  encoding: UTF-8 with byte-order mark* setting): `timestamp_utc,level,pid,tid,buffer,tag,template_id,message`;
- **50 014 data rows on both sides**;
- ignoring `template_id` (mined independently on each side), the two files are
  **identical as multisets** — every row present, none altered.

**They differ in order, and the reason is a setting, not a bug.** The device
writes **source order**; the CLI writes chronological. Proof: the record at
physical line **2059** of `crashy.txt` (`14:13:36.771 … identical 92012 lines`,
which is the *earliest* record but not the first line) is row **2058** of the
device CSV and row **1** of the CLI's. The device's *Appearance & timeline ->
Default export order* is set to **Source order**. Recorded here because a
future H-08 parity check that diffs the two files byte-for-byte will fail on
this and it is not a defect — see the note in §11.

**Portable share — PASS, and cross-app readability proven.**

| Assertion | Observed |
|---|---|
| Share sheet opens | Android `com.android.intentresolver.ChooserActivityLauncher`, `1 položka` |
| Archive name | `vc2-crashy-20260829-182357.vcat.zip` |
| FileProvider in use | `dumpsys package` -> authority **`com.barebit.visualcat.files`** bound to `androidx.core.content.FileProvider` |
| **No `file://` exposed** | `targetSdk=36`, so a `file://` URI in `ACTION_SEND` throws `FileUriExposedException` unconditionally. A full `-b main,crash` sweep of the window contains **no `FileUriExposedException`, no `StrictMode` violation, and no `SecurityException`**, and the chooser opened normally. The URI is therefore a `content://` grant. |
| **The receiving app can read it** | Chose **Gmail**. Its compose screen listed the attachment `vc2-crashy-20260829-182357.vcat.zip` at **2,6 MB** — a size only obtainable by opening the stream through VisualCat's provider. Draft discarded with Back; no send, no saved draft. |

**One inconsistency worth naming.** The archive's timestamp is **the moment of
export, in UTC**: two shares of the same unchanged session, ~2 minutes apart,
produced `…-20260829-182357.vcat.zip` and `…-20260829-182559.vcat.zip`, and host
UTC at the second was **18:25:56**. Everywhere else the product speaks the
device's local time — the tab chip says `On-device logcat 19h54m13`, *Recent
sessions* says `2026-08-29 19:58`. R-07 asks that the export filename carry *the
capture's start time*; it carries the export's instant, in a different time zone
from every other surface. A user matching an archive back to a capture has to do
timezone arithmetic. **Suggested fix:** name the archive
`<session name>-<capture start, local, same format as the session list>.vcat.zip`,
and if a uniquifier is needed, let the platform's `(1)` suffix do it — as it
already does for CSV.

---

#### V2-15 · *Export CSV* says "choose which entries to write" and never asks — **Low**

*Surface:* Android companion, *More actions -> Export CSV…*.

**Observed.** The More sheet's own subtitle is **"Choose which entries to write,
then save a CSV"**. Tapping it goes directly to the platform save picker. No
scope chooser appears — not with a filter active, not without one. Tested both
ways; the picker is the first and only screen. The scope is decided implicitly
("entries matching the filter") and disclosed **after the fact**, in the notice
lane.

Plan B-14 asks for "*Export CSV…* with **each scope offered**" and "The CSV scope
is explicit". Post-hoc disclosure is honest but it is not a choice, and the
product's own copy promises one.

**Why it matters in practice.** The natural mistake is exporting a filtered view
believing it was the whole session, or the reverse. The notice tells you which
it was only once the file is written to a location you already picked.

**Suggested fix.** Either add the chooser the copy promises — a small sheet
offering *Everything in this session (50,014)* / *Everything matching the current
filter (8,349)* / *What is in view (n)*, with the counts rendered so the choice
is obvious — or change the subtitle to describe what actually happens
("Save the entries matching the current filter as CSV"). The first is better:
the counts are already computed and the sheet is one control.

---

#### V2-16 · The leftmost tab in a scrolled tab strip cannot be closed — **Medium**

*Surface:* Android companion, session tab strip, with enough sessions open that
the strip scrolls (a phone reaches this at ~4 tabs).

**Observed, three times, on three different sessions — the common factor is the
position, not the session.** Whichever tab is **clipped by the left edge of the
strip** has its close button rendered `enabled="false"`:

| Strip state | Clipped tab (its `Show` button's bounds) | That tab's close button |
|---|---|---|
| A | `vc2-longline.txt` `[0,229][226,337]` | `[229,229][340,337]` **enabled=false** |
| B | `vc2-continuations.txt` `[0,229][166,337]` | `[171,229][282,337]` **enabled=false** |
| C | `vc2-outoforder.txt` `[0,229][37,337]` | `[42,229][153,337]` **enabled=false** |

In every case the **other** visible tabs' close buttons were enabled, and the
same tab's close button became enabled again as soon as the strip scrolled it
fully into view (state A -> `vc2-longline.txt` close `enabled=true`).

**It is genuinely dead, not merely styled.** A tap at the centre of the disabled
button (`284, 283` in state A) did nothing: the session was still open in the
next dump, and no notice appeared.

The close button itself is **fully visible and full size** (49 × 48 dp) in all
three cases — only the tab's *label* is clipped. So the user sees an ordinary,
correctly sized × that silently ignores taps.

**Consequence.** With many sessions open — and this run reached **twelve** — the
only way to close the leftmost one is to notice that nothing happened, guess
that scrolling is the cause, scroll, and try again. Nothing on screen suggests
it.

**Suggested fix.** The disable is almost certainly a guard against acting on a
partially-visible item. Replace it: keep the button enabled and, on click,
**scroll the tab into view and close it** (or just close it — the close button's
own hit area is fully on screen, so there is no mis-tap risk to guard against).
If a guard is genuinely wanted, clip the close button along with its tab so
there is nothing to tap, rather than showing a full-size control that does
nothing.

---

### A-05 / R-30 · Multiple sessions open at once — **PASS**

Not planned as a stress test; the adversarial sweep accumulated tabs because the
`ACTION_VIEW` route opens a new one each time. The result is better evidence than
a designed run would have produced.

| Assertion | Observed |
|---|---|
| Sessions open simultaneously | **12** — `empty`, `notalog`, `crlf`, `bom`, `nonutf8`, `truncated`, `nofinalnewline`, `mixed-formats`, `outoforder`, `continuations`, `longline`, `crashy`, including **two failed imports** alongside ten good ones |
| Memory at 12 tabs | `TOTAL PSS 381 315 KB`, `RSS 464 560 KB`, `SWAP PSS 18 613 KB` — no LMK kill, no OOM |
| Tab strip | scrolls horizontally; each chip carries its own name and status (`Show complete session …` / `Show **failed** session …`) |
| Switching | selecting a clipped tab scrolls it into view and activates it |
| **R-30 — closing a tab cannot crash the workspace** | all 12 closed one after another, plus ~10 more closed earlier during and immediately after ingest. A full `-b main,crash` sweep of the entire run contains **no `ObjectDisposedException`, no `AndroidRuntime` line attributable to `com.barebit.visualcat`, and no native fault**. The only `AndroidRuntime` records in the buffer belong to `uid 2000` — the `adb shell content` helper. |

---

## 5. Tier U — accessibility and appearance

### U-04 · System text size (accessibility scale) — **FAIL** (2 findings)

`settings put system font_scale` swept 1.0 -> 1.3 -> 1.5 -> 1.8 -> 2.0 with
`vc2-small.txt` (49 994 entries) open in Split. Android 14+ non-linear font
scaling means 2.0 is a value a real user can select.

**What the layout does well, and it is a lot.** At every scale the touch-target
sweep stayed clean — **0 interactive nodes under 48 dp, 0 overlaps** in the
workspace, at 1.0, 1.3 and 2.0. The composition *adapts* rather than merely
stretching:

- at 2.0 the third mode segment relabels itself **`Details` -> `Logs`**;
- the entry action row shortens `Copy raw` -> `Copy`, `Entry ⤢` -> `Entry`;
- `Load 500 more · 49,494 remaining` drops its counter to `Load 500 more`;
- the severity legend drops its per-lane counts;
- the plot yields height (timeline `472 px` -> `451` -> `309`).

Those are deliberate, and they are the reason nothing clips in the command bar.

---

#### V2-17 · The entries list drops below its four-row floor as text grows — **Medium**

*Surface:* Android companion, Split mode. Measured from the accessibility tree,
counting only rows whose bounds lie **entirely inside** the `ListBox`.

| `font_scale` | `ListBox` height | Row height | Rows fully inside | R-06 floor |
|---:|---:|---:|---:|---|
| 1.0 | 558 px | 144 px | **3** (4th clipped by 18 px — all its text still readable) | >= 4 |
| 1.3 | 558 px | 156 px | **3** (4th clipped by 66 px — **its timestamp line is cut**) | >= 4 |
| 2.0 | 558 px | 218 px | **2** (3rd cut mid-message) | >= 4 |

**The container never grows.** `ListBox` height is **558 px at every scale**,
while the row height rises 144 -> 156 -> 218. The plot does give up space
(472 -> 451 -> 309 px) but that space goes elsewhere — the count line wraps to two
lines, the action row grows — and never reaches the list.

R-06 asks for "a floor of at least four rows" in Split "even with a notice". At
1.0 the floor is met in spirit (the fourth row's text is fully readable, only
padding is clipped). At **1.3 — a scale reachable from Samsung's ordinary
Display settings slider, not an accessibility extreme — the fourth row's
timestamp is cut**, and at 2.0 the user sees two and a half rows of a 50 000-entry
log.

**Suggested fix.** Make the floor a *measured* constraint rather than a fixed
`558 px`: `MinHeight = 4 × rowHeight(currentTextScale) + chrome`, evaluated when
the scale changes, with the plot as the donor (it already is). Compute the row
height from the actual template at the current scale rather than a constant. If
four full rows genuinely cannot fit — which is the case at 2.0 in Split — the
right answer is to **drop to Details automatically at that scale** (the plot is
already only 309 px and near-useless), not to show two rows.

---

#### V2-18 · At large text the Live scope chooser hides both of its choices, and the restricted path becomes unreachable — **High (accessibility)**

*Surface:* Android companion, *Choose what Live captures*.
*Trigger:* system `font_scale` **>= ~1.8**. Bracketed on device.

| `font_scale` | `Full-device capture` radio | `Capture VisualCat only` radio | On screen? |
|---:|---|---|---|
| 1.3 | `[112,1206][517,1314]` | `[112,1654][589,1762]` | both visible, comfortably |
| 1.5 | `[112,1360][570,1468]` | `[112,1894][652,2002]` | both visible, the second just above the footer |
| **1.8** | `[112,1740][648,1848]` | **absent from the accessibility tree entirely** | only the first |
| **2.0** | reported at `[112,2029][701,2137]`, i.e. **clipped to the footer band** | **absent from the tree** | **neither** |

> **Reading those bounds correctly.** Avalonia's Android automation peer reports
> a node's *layout* bounds clipped only by the screen, so a control scrolled out
> of its host still exports plausible-looking coordinates. At 2.0 the
> `Full-device capture` radio's reported rectangle coincides with the footer
> buttons, and a naive overlap sweep calls that a z-order collision. It is not:
> the radio is **scrolled out of the card's viewport and not painted at all**.
> The screenshot is the authority, and it shows no radio anywhere. This
> distinction matters because the fix differs — the footer is not covering a
> painted control, the content simply never comes into view.

**What the user sees at 2.0** (screenshot `U04-more-2.0.png`): a card containing
the title, the disclosure paragraph filling the whole card, and a footer with
`Cancel` and `Set up full-device`. **Neither radio button is visible.** The card
looks finished — there is no cue that two options are below the fold, and the
paragraph's own scrollbar is the only scroll indicator on screen, which reads as
belonging to the paragraph.

**Why this is the most serious finding in this run.** The preselected option is
*Full-device capture* and the primary button is `Set up full-device`, which
leads into Android Wireless-debugging pairing. A user running large system text
therefore has exactly two discoverable actions: **cancel**, or **start a pairing
flow they may not want**. The zero-setup path that B-10 exists to protect —
*Capture VisualCat only*, "starts immediately, no setup" — is not reachable
without blind-scrolling a dialog that gives no sign it scrolls.

The content *is* recoverable: an `input swipe 540 1400 -> 540 700` inside the
card brings both radios into the tree and on screen. Nothing tells the user
that.

One further fault in the same state: at 1.8 the second radio is **not in the
accessibility tree at all**, so **TalkBack cannot reach it either** until the
dialog is scrolled — an assistive-technology user has no way to know a second
option exists.

**Independent DUT-2 confirmation.** The Google Pixel 5/API-34 extension in
§14.7 reproduces this at `font_scale=2.0` on a narrower **393 dp** viewport:
both radio buttons are absent from the accessibility tree and the dialog title
is clipped horizontally. Evidence: `Pixel5-U04-live-2.0.png` / `.xml`. V2-18 is
therefore neither Samsung-specific nor an Android-16-only regression.

DUT-3 (§17.6) supplies a third independent reproduction on a **434 dp**
Motorola/API-36 viewport. Its Google-Play-signed `2.0.10` build lays the
Full-device radio almost entirely below the screen and omits the restricted
radio from the tree; neither is painted. This is compatibility evidence rather
than a HEAD-artifact result, but it confirms the same design failure survived a
separately packaged release build.

**Suggested fix.**

1. **Put the choices above the essay.** The two radio options are the decision;
   the disclosure paragraph is support. Ordering them options-first makes the
   dialog work at every scale and shortens the path at every scale.
2. **Collapse the disclosure at large scales** — a two-line summary with a
   *Why this is safe* disclosure — rather than letting a 90-word paragraph
   consume a 464 × 853 dp card.
3. Add a scroll affordance (a fade or a chevron) whenever the card's content
   exceeds its viewport, so "there is more below" is visible without trying.
4. **Regression test:** assert that both `RadioButton`s are realised *and within
   the card's viewport* at `font_scale` 1.0, 1.3, 1.5, 1.8 and 2.0. This is a headless layout test; it needs no device.

---

### U-06 / R-27 / R-28 · Theme — **PASS**

`cmd uimode night no` with the app running, then back to `yes`.

| Probe (x = 540) | Dark | Light |
|---|---|---|
| workspace ground, y = 1200 | `rgb(8,13,22)` | `rgb(244,247,252)` |
| command bar, y = 150 | `rgb(23,34,53)` | `rgb(228,236,246)` |
| behind the navigation bar, y = 2290 | `rgb(8,13,22)` | `rgb(244,247,252)` |

**R-28 — a theme change repaints the whole product. PASS.** The switch happened
with the app in the foreground, **no restart**, and every surface followed:
ground, command bar, tab chips, mode segments, notice lane, plot lanes and their
labels, entry rows, the splitter grip, the status line, and the area behind both
system bars. No surface retained the other variant's values. The severity
palette is re-tuned rather than reused — the light variant deepens the hues so
they stay legible on a near-white ground.

**R-27 — the product owns its accent. PASS.** The selected-mode fill, the tab
underline, and the selected-row wash are the product's blue in both variants,
not the Samsung device accent.

> **A detail that belongs to V2-01.** The phantom scrollbar changes character
> with the theme: in dark it is `rgb(81,84,90)` against `rgb(8,13,22)` — about
> 4.4:1, plainly visible; in light it is `rgb(246,248,252)` against
> `rgb(244,247,252)` — about **1.02:1, effectively invisible**. The same
> non-functional element is conspicuous in one theme and absent in the other.

---

### P-01 · No network traffic — **PASS**

| Probe | Result |
|---|---|
| Open socket file descriptors in the app process (`ls -l /proc/<pid>/fd \| grep -c socket`) | **0** |
| `/proc/net/tcp` + `/proc/net/tcp6` rows owned by uid **10371** | **none** |
| `dumpsys netstats detail` byte buckets for uid 10371 | the uid appears only in the `set=1` enumeration; **no byte buckets recorded** |
| `dumpsys batterystats` mobile-radio active time | `0 ms` |

Measured after a run that had already imported 1 M lines, run a live capture,
exported a CSV, and shared an archive. **`INTERNET` is declared in the
manifest** — legitimately: the Wireless-debugging transport the scope chooser
describes is a local TCP connection, and Android requires the permission for it.
Nothing used it. Recorded as declared-but-unused rather than as a clean bill of
health for a path this run did not exercise (W1–W5 are a coverage gap, §12).

### A-16 / P-03 · Diagnostic bundle and its redaction — **PASS**

The confirmation card states the contract before anything is written:

> The bundle excludes raw log messages, source paths, hashes, searches, and
> device serials. It still contains timings, counts, system details, and
> sanitized session metadata. **Review it before sharing.**

Written through the platform save picker as
`visualcat-diagnostics-20260829-204306.zip`, **8 121 bytes**, four entries:

| Entry | Size | Content |
|---|---:|---|
| `SENSITIVE-DATA-WARNING.txt` | 325 B | the same contract, in the archive |
| `system.json` | 161 B | `os: "Unix 36.0.0.0"`, `.NET 10.0.11`, `Arm64`, `processorCount 8`, `createdUtc` — **no model, no fingerprint, no serial** |
| `logs/visualcat-20260829-000.jsonl` | 49 240 B | structured events (`android.safe-area.changed`, subsystem, level, session guid, generation) |
| `sessions/session-000.json` | 40 475 B | session descriptor with `"displayName": "<redacted>"`, `"sourceDescription": "<redacted>"` |

**Leak probes over the whole decompressed archive (90 201 characters):**

| Probe | Result |
|---|---|
| device serial `RFCRC0A9GND` | **absent** |
| `/data/user/0`, `/data/data`, `/sdcard/` | **absent** |
| any corpus filename (`vc2-…`) | **absent** |
| raw log text (`Rendering surface`, `FATAL EXCEPTION`) | **absent** |
| the search query used earlier (`Rendering`) | **absent** |
| e-mail addresses (regex) | **none** |
| 15-digit IMEI-like numbers (regex) | **none** |

`timeZoneId: "Europe/Prague"` is present and is covered by the stated
"operating-system details". **P-03 PASS.**

> **A finding hides in this evidence.** The session descriptor in the bundle
> carries `"counters": { "sourceBytes": 4500749, "sourceLines": 50001,
> "parsedEntries": 49994, "timedEntries": 49994, "metaRecords": 1,
> **"unknownLines": 6**, "rejectedCandidates": 0 }`. The store already computes
> and persists the unknown-line count per session — so V2-14's fix ("count them
> where the reader looks") needs no new machinery at all, only a binding.

> **And a second timestamp inconsistency.** This bundle is named
> `…-20260829-**204306**.zip` — **local** time, matching the session list. The
> portable archive in B-14 was named `…-20260829-**182357**.vcat.zip` — **UTC**.
> Two export paths in the same build, two different clocks.

---

## 6. Tier A completion — settings

### A-17 / R-08 · Settings take effect and persist — **PASS**

The *Appearance & timeline* card was opened over the completed
`vc2-small.txt` workspace. Every setting named by A-17 was moved off its
baseline, applied, observed on the workspace, and re-read after an actual cold
process restart (`am force-stop` followed by `am start -W`, `LaunchState:
COLD`, `TotalTime: 1765 ms`).

| Setting | Baseline | Test value | Value after cold restart |
|---|---|---|---|
| Theme | Follow the system | Light | Light |
| Prefer high contrast | off | on | on |
| Text scale | 1.00× | 1.10× | 1.10× |
| Live UI refresh limit | 30 Hz | 28 Hz | 28 Hz |
| Timeline intensity | Logarithmic | Square root | Square root |
| Timeline normalization | Per severity row | Whole viewport | Whole viewport |
| Maximum zoom precision | 1.0 µs/px | 1.2 µs/px | 1.2 µs/px |
| Pixel snapping | on | off | off |
| Minimum bar width | 5 px | 7 px | 7 px |
| Default export order | Source order | Chronological | Chronological |
| Normalized CSV encoding | UTF-8 with BOM | UTF-8 | UTF-8 |

The workspace changed to the light/high-contrast composition as soon as
*Apply* returned; no restart was needed. The post-restart card then exposed the
same values in its accessibility tree. The theme, intensity, normalization,
export-order and encoding choices are in-place `ToggleButton` segments — none
opened a popup or moved the form to an unrelated field. **R-08 PASS.**

The Android stepper controls deserve an instrumentation note: a zero-duration
ADB tap does not actuate Avalonia `RepeatButton`; a held touch does. The test
used 450 ms stationary swipes and verified the displayed value after every
step, so this is not a false pass from inert automation.

All application settings were returned to the baseline values above before
leaving the card, and §13's package-data clear independently returned the app
to first-run defaults. Evidence: `A17-persisted-top.xml`,
`A17-persisted-lower.xml`, `A17-after-restart.png`, and
`A17-defaults-restored-workspace.png`.

---

## 7. Tier X completion — paging

### X-13 · Paging a one-million-line result — **PARTIAL** (1 finding)

`vc2-large.txt` contains 1,000,001 physical lines and produces **999,885**
entries. The mobile table initially materialised 500. Six consecutive
`Load 500 more` operations produced this exact remaining-count sequence:

`999,385 -> 998,885 -> 998,385 -> 997,885 -> 997,385 -> 996,885 -> 996,385`

That is 3,500 loaded rows with no short page. Across every page:

- the selected first record stayed selected by identity;
- `Copy raw` remained enabled at exactly `[370,1356][683,1464]`;
- the load footer remained `[72,2043][1008,2151]` and never overlaid a row;
- moving to the end at 3,000 rows selected
  `2026-05-15T12:13:41.500Z · Error · AndroidRuntime · Cache contains 11807 entries`,
  exactly CSV row 3,000 in the host oracle;
- after the next page, that selection remained stable, and moving to the new
  end selected
  `2026-05-15T12:13:42.225Z · Verbose · Network · Started process 50509…`,
  exactly oracle row 3,500;
- the first 3,500 chronological oracle rows contain **zero exact duplicate
  rows**.

This is direct boundary evidence that the exercised keyset pages neither
repeated nor skipped the row at the 3,000/3,001 transition. The complete
107,066,548-byte oracle is `X13-large-oracle.csv`; the device boundary dumps
are `X13-key-end.xml`, `X13-after-sixth-page-boundary.xml`, and
`X13-end-of-3500b.xml`.

The scenario cannot be called a full pass because its required final action is
not present on Android.

#### V2-19 · A million-row session has no *Load all* path on the phone — **Medium**

*Surface:* Android companion, Entries footer. *Trigger:* any result with more
than the initial 500 rows.

**Observed.** The only action is `Load 500 more`. After 3,500 rows it still says
`996,385 remaining`. `Load all` is absent from the screenshot and the entire
accessibility tree. This is not clipping: the implementation updates `_loadAll`
only inside `if (!_mobile)`, while Android's footer contains `_loadMore` alone.

At this size, reaching the end requires **1,999 taps** from the initial page.
X-13 explicitly requires a *Load all* attempt and accepts either completion or
an explicit bound; the phone offers neither the action nor an explanation that
the operation is intentionally unavailable.

**Suggested fix.** Offer a cancellable *Load all* with a visible memory/row
bound and incremental progress. If loading all rows is intentionally forbidden
on phones, say so in the UI and provide a practical bounded alternative such
as *Load next 10,000* / *Jump to row or time*. The current one-size page makes a
supported million-line session technically searchable but not traversable.

---

### R-34 · Contextual action slots are stable — **FAIL** (paging guard passes; literal two-tap guard fails)

The regression's paging-specific geometry is fixed: while the `+500` footer
updated through six pages, `Copy raw` did not move by one pixel. A separate
state transition still violates R-34's final pass sentence, *"two taps in the
same place hit the same control."*

#### V2-20 · Copy confirmation moves *Copy raw* by 140 px, so a repeated tap opens the entry — **Medium**

*Surface:* Android companion, selected row in Entries. *Reproduced twice on the
999,885-entry session.*

1. With no notice showing, `Copy raw` is `[370,1356][683,1464]`.
2. Tap its centre, `(526,1410)`. The copy succeeds and the notice says
   `Copied the raw text of 1 entry.`
3. The new notice lane takes 140 px from the workspace. `Copy raw` moves to
   `[370,1216][683,1324]`.
4. Tap `(526,1410)` again. That coordinate is now inside the selected list row;
   the app opens the **Entry** tab instead of copying again.

This is the same class of failure R-34 exists to prevent: a successful action
changes what the user's finger will do next, with no pointer movement. It is
not destructive, but it is a credible wrong-action path during repetitive log
triage. Evidence: `X13-copy-shift-1.png` / `.xml` and
`X13-copy-shift-2.png` / `.xml`.

**Suggested fix.** Reserve the notice lane's maximum height in the phone
composition, or overlay the notice without reflowing the analysis pane. At
minimum, keep the primary action row anchored while a notice enters or leaves.
The regression test should perform two physical taps at one coordinate, not
only compare the action grid before and after the paging footer appears.

---

## 8. Tier R completion — phone dialogs

### R-11 · Dialogs work on the phone — **PASS**

All four named surfaces were exercised as in-page cards, not platform popups:

| Card | Evidence | Return path |
|---|---|---|
| Recent sessions | B-13 `B13-recent-list.png` / `.xml` | selecting a session returned to its workspace |
| Appearance & timeline | A-17 `A17-persisted-top.png` / `.xml` | Apply returned values and repainted the workspace |
| Session cache | `R11-session-cache.png` / `.xml` | Cancel returned to the empty state without changing policy |
| Diagnostic bundle | A-16 `A16-bundle-dialog.png` | confirmation returned a save result to the notice lane |

The Session cache card exposed its policy, disabled age/size steppers while
automatic cleanup was off, and kept *Delete eligible temporary sessions*,
*Cancel*, and *Save policy* within the phone footer. Cancel returned to the app
with the same activity focused. **R-11 PASS.**

---

## 9. Closing result ledger

This is an independent, deliberately bounded physical-device pass, not a claim
that the plan's multi-device Full/Soak/Accessibility schedules all ran. Its
last outstanding rows closed as follows:

| Row | Result | Release meaning |
|---|---|---|
| A-17 / R-08 | **PASS** | all settings take effect, persist, and use phone-appropriate segments |
| X-13 | **PARTIAL** | exercised pages are exact; required *Load all* route is absent (V2-19) |
| R-34 | **FAIL** | paging geometry is stable, but the literal same-coordinate repeat action fails (V2-20) |
| R-11 | **PASS** | every named phone card opens and returns a result |
| §13 | **PASS** | all recorded device mutations and test data restored |

Together with §§3–5, this closes the report's recorded restore point. There is
no unexecuted scenario left in this run.

---

## 10. Stability and evidence close

- The final Android crash buffer was empty.
- A full main/system scan found no `FATAL EXCEPTION` or `ANR in
  com.barebit.visualcat` attributable to this run.
- No `logcat` child and no VisualCat process survived final `pm clear` plus
  `am force-stop`.
- The clean post-clear launch was `LaunchState: COLD`, **1,612 ms**, and showed
  the first-run state with no recent captures. Its identity line still read
  `VisualCat 2.0.10-dev+0c9dd02`.
- Device serial and fingerprint at hand-back matched §0.2 exactly.

Evidence stays under `artifacts/android-live-v2/RFCRC0A9GND/`. The 107 MB CSV
oracle is intentionally retained there; no evidence was left on the phone.

---

## 11. Findings and changes required

This pass records **21 numbered product findings** (V2-01…V2-07 and
V2-09…V2-22; V2-08 was never assigned) plus PLAN-01 and the frame-instrumentation
defect. The release-significant order is:

1. **V2-18 — High accessibility:** at large system text the Live chooser hides
   both scope choices and makes the zero-setup restricted path undiscoverable;
   the Pixel reproduces it independently.
2. **V2-21 — Medium navigation:** stock gesture Back skips past More/dialog
   layers, while the plot's exclusion band can suppress Back completely.
3. **V2-14 — Medium/High UX/data interpretation:** continuation and crash-log
   lines fall into an already-counted unknown bucket that the UI never reveals.
4. **V2-05 / V2-17:** touch targets and the four-row floor miss explicit
   accessibility contracts.
5. **V2-19 / V2-20:** the million-row phone workflow cannot load all rows, and
   a confirmation reflow turns a repeated Copy tap into Open entry.
6. **V2-01 / V2-02 / V2-03 / V2-10 / V2-11 / V2-16 / V2-22:** the first-run,
   notice, reopen, tab-management and pre-Android-15 system-bar surfaces have
   prominent phone-specific friction.

The remaining findings are lower-risk correctness/clarity issues, but none is
discarded: their full evidence, reproduction, and suggested fix remain beside
the scenario that produced them.

**PLAN-01 remains open.** Add the documented `generate-test-log --format`
support, or rewrite §3.2 around a checked-in deterministic generator. A plan
must not command a CLI option the shipped CLI rejects.

**Frame instrumentation amendment.** `gfxinfo` is not a valid jank oracle for
this SurfaceView renderer, and SurfaceFlinger latency returned no frame rows on
this device. The plan needs a Perfetto/FrameTimeline path (with an external
high-speed-camera fallback) before any frame-pacing number can become a release
gate. This run correctly leaves those quantitative assertions BLOCKED rather
than manufacturing a pass from `Total frames rendered: 0`.

---

## 12. Coverage gaps — not silently green

| Gap | Status and risk |
|---|---|
| W1–W5 production full-device Wireless-ADB capture | **Not run.** W0/W6 and own-app capture pass, but pairing, discovery, reconnect, denial and stale-port recovery remain release coverage. |
| Gesture navigation / U-18 | **Covered on DUT-2 and FAIL:** stock Pixel edge Back skips More/dialog layers and is suppressed across the plot exclusion band (V2-21). DUT-1 used Samsung `sec_gestural`. DUT-3's three-button Back passes every tested layer (§17.4), further isolating the failure to stock gesture dispatch. |
| Search wrap at match 1 / 7,181 | **Blocked by product UI:** no first/last jump exists; V2-07 records it. |
| Frame pacing and jank budgets | **Blocked by instrumentation:** §10/§X-01 explains why `gfxinfo` measured the wrong pipeline. |
| TalkBack, Switch Access, external keyboard, RTL, foldable/tablet and API 31 | **Not part of this bounded run.** The phone matrix now covers Samsung, Google and Motorola at API 34/36, but no result is inferred for the remaining accessibility/form-factor/API-floor cells. |
| Soak, low-memory, storage-pressure, Doze, network interruption, upgrade and Play delivery | **Not run here.** They remain owned by the Full/Soak/Upgrade schedules in the plan. |

Therefore this report is complete as a test record, but it is **not by itself a
release sign-off** under plan §13.4. In particular, V2-18 is an open High finding,
V2-21 breaks stock gesture navigation, and the Wireless-ADB, accessibility,
remaining device-matrix and soak cells are not green.

---

## 13. Mandatory final cleanup and device hand-back — **PASS**

Cleanup was performed against the recorded ledger, never guessed defaults.

| Check | Final evidence |
|---|---|
| Captures/imports/processes | none running; no orphan `logcat`; VisualCat force-stopped |
| Public test files | every recorded `vc2-*`, both CSVs, and `visualcat-diagnostics-20260829-204306.zip` deleted by exact path; unrelated Downloads preserved |
| Private test sessions/preferences | `pm clear com.barebit.visualcat` succeeded; clean cold launch showed no recent captures |
| Notification permission | `POST_NOTIFICATIONS: granted=false` |
| Rotation | `accelerometer_rotation=1`; `wm user-rotation` = `free` |
| Text/theme | `font_scale=1.0`; `Night mode: yes` |
| Animations | window `1.0`; transition `1.0`; animator `null`, matching start |
| Display | physical `1080x2340`; physical density 480; owner override density **360** preserved |
| Clock | `Europe/Prague`; `Sat Aug 29 21:07:42 CEST 2026` at verification |
| Health | battery 100 %, USB powered, 34.0 °C; thermal status 0; validated network present |
| Identity | state `device`; serial `RFCRC0A9GND`; fingerprint byte-for-byte equal to §0.2 |
| Stability | empty crash buffer; no package fatal/ANR in the final sweep |

Storage available rose from **94,486,176 KB** before cleanup to **94,638,604
KB** after removing public test files, then to **95,273,656 KB** after clearing
the clean-install test sessions: **787,480 KB returned** in total. The final
screen was the ordinary launcher. VisualCat remains installed at the tested
HEAD build, stopped, with first-run data — the same clean-data condition from
which the authoritative run began.

The removed phone files and cleared app sessions are not recoverable from the
device. All corpora are deterministic and remain reproducible from §2; all
retained evidence is in the host evidence root.

---

## 14. DUT-2 extension — Google Pixel 5 / Android 14

The second phone was used to close the stock-gesture and second-OEM/API coverage
that DUT-1 could not supply. This was deliberately a **preservation run**: the
Pixel already contained two owner sessions and unrelated Downloads, so tests
that would create a new private session were not started. No result from DUT-1
was copied forward without a new observation.

### 14.1 Device and artifact record

| Field | DUT-2 value |
|---|---|
| Transport serial | `0A031FDD400365` |
| Model / manufacturer | `Pixel 5` / `Google` (`redfin`) |
| Android / SDK | **14 / 34** |
| Build fingerprint | `google/redfin/redfin:14/UP1A.231105.001.B2/11260668:user/release-keys` |
| ABI list | `arm64-v8a,armeabi-v7a,armeabi` |
| Physical display | `1080x2340`, density **440** with no override → **2.75 px/dp**, nominal **393x851 dp** portrait |
| Navigation | stock gestural, `navigation_mode=2`; `com.android.internal.systemui.navbar.gestural` enabled |
| Cutout / corners | left punch hole, bounding rect `[0,0][145,136]`; top safe inset 136 px; rounded-corner radius 108 px |
| Refresh rate | active 90 Hz; 60/90 Hz modes exposed |
| Locale / zone | `cs-CZ` / `Europe/Prague` (UTC+2) |
| Start health | battery 78 %, USB powered, 32.1 °C; thermal status 0 |
| Start storage | 96,582,272 KB available |
| Evidence | `artifacts/android-live-v2/Pixel5-*` |

The installed app initially identified itself as
`VisualCat 2.0.10-dev+d959187`. Pulling that package produced SHA-256
`9d9e164a4e967aefa9751df07f4a42cb8e39c4f493ca7b3d9d5170a0be741619`,
so it was not the report's artifact. `adb install -r` updated it in place without
clearing data. The post-update package (`Pixel5-head-base.apk`) is byte-for-byte
the host Release APK:

`68692776190ff7f2690e142073cb2b8803ad081006602cd32bb26343d6534bdb`
(34,147,035 bytes), identity `VisualCat 2.0.10-dev+0c9dd02`.

The Release security posture matches DUT-1: `run-as` is refused and `READ_LOGS`
is absent. `POST_NOTIFICATIONS` began denied and stayed denied.

### 14.2 Preservation boundary and starting state

Before testing, the Session cache card recorded exactly:

- `demo-small`, 2026-08-29 12:25, 10.07 MiB, complete;
- `demo-small`, 2026-08-29 11:53, 10.07 MiB, complete;
- total: **2 temporary sessions / 20.14 MiB**.

The card has only a bulk *Delete eligible temporary sessions* operation, not an
individual-session delete. Creating a new import/live capture could therefore
not be cleaned up without risking those two sessions. The Pixel extension uses
them only to reach read-only workspace surfaces. It neither imports nor starts a
capture. The six pre-existing Downloads and the five pre-existing
`VisualCat`-labelled Wireless-ADB identities were inventoried and left alone.
Wireless Debugging was off (`adb_wifi_enabled=0`) throughout.

### 14.3 DUT-2 mutation ledger

| Setting/action | Original | Test value/action | Final |
|---|---|---|---|
| `font_scale` | `1.0` | `2.0` for U-04 | **`1.0` restored and verified** |
| Rotation | free; `accelerometer_rotation=1` | `wm user-rotation lock 1` for landscape | **free; accelerometer `1` verified** |
| Night mode | `yes` | unchanged | **`yes`** |
| Wireless Debugging | off (`0`) | unchanged | **off (`0`)** |
| Notification permission | denied | unchanged | **`granted=false`** |
| Package | stale `d959187` build | in-place update to the report artifact | **HEAD deliberately retained** |

No owner preference, session, public file, pairing identity, navigation mode or
display-density setting was changed.

### 14.4 B-01 · In-place artifact update and cold start — **PASS**

Five force-stopped `am start -W` launches of the verified HEAD package returned:

`2460, 2460, 2456, 2463, 2450 ms`

All five reported `LaunchState: COLD`; median **2,460 ms**, maximum **2,463 ms**.
That passes the plan's median <=2,500 ms and maximum <4,000 ms gates, but the
median has only **40 ms (1.6 %) headroom**, which is worth retaining as a device
baseline rather than rounding into a comfortable pass. Evidence:
`Pixel5-B01-cold.png` / `.xml`.

The update preserved both cached sessions. The first-run/empty workspace also
had **zero primary interactive nodes below 48 dp** in the accessibility-tree
target sweep. V2-02's large unused lower region reproduces on this narrower
phone; it is not assigned a second finding.

### 14.5 U-07 · Cutout, rotation and system bars — **PARTIAL** (1 new finding)

**Safe-area assertions pass.** In portrait the app root begins at y=136, exactly
below the punch-hole inset, and the first brand text begins at y=152. No content
or target lies under the cutout. In landscape the root is
`[136,0][2340,1014]`, leaving the cutout side clear, and the gesture bar does not
cover app controls. The workspace remains usable after rotation. Evidence:
`Pixel5-U07-landscape.png` / `.xml`.

**The edge-to-edge assertion fails.** API 34 supplies separate
`android:id/statusBarBackground` and `android:id/navigationBarBackground` views.
The app root is `[0,136][1080,2274]` in portrait and stops at y=1014 in
landscape. Both system bars paint `rgb(22,18,23)`, visibly distinct from the app
ground `rgb(9,13,21)` and command band `rgb(16,23,37)`. Samples remain identical
on rotation. DUT-1/API-36 drew edge-to-edge, so this is an API-path difference.

#### V2-22 · Android 14 leaves both system bars as an off-palette band — **Medium (appearance/platform)**

*Surface:* every Pixel/API-34 screen, portrait and landscape.

The top 136 px and bottom 66 px are owned by opaque platform backgrounds with a
brown-purple tint. In landscape the same band moves to the navigation edge. The
content is safe, but the shell no longer looks continuous and U-07's explicit
edge-to-edge requirement is unmet.

**Suggested fix.** Configure the API-34 window for edge-to-edge explicitly
(`decorFitsSystemWindows=false` / transparent system-bar colors), draw the app
ground behind the bars, and continue applying the current cutout/gesture insets
to interactive content. Regression-test screenshots on both API 34 and API 36
in portrait and landscape; assert the bar pixels match the intended shell color.

### 14.6 B-15 / U-18 / R-19 · Stock gesture Back — **FAIL** (1 new finding)

The Pixel's left and right edge gestures were first validated outside the app,
then bracketed against VisualCat's layer stack:

| Starting state / gesture | Observed result | Assertion |
|---|---|---|
| More sheet open; left edge at y=1200 | Pixel launcher opens; sheet and app are skipped | **B-15 FAIL** |
| More sheet open; right edge at y=1200 | same | **B-15 FAIL**, both edges |
| Appearance card open; edge at y=1200 | Pixel launcher opens instead of dismissing the card | **B-15 FAIL** |
| Filter drawer open; edge at y=1200 | drawer closes and app remains focused | layer return path works here |
| No layer, edge at y=1200 over plot | gesture is swallowed; app remains focused | **U-18 FAIL** |
| No layer, edge at y=500 | app returns to launcher normally | **R-19 PASS** outside exclusion |
| More sheet open; `KEYCODE_BACK` control | sheet closes and app remains focused | key-Back stack is correct |

Evidence: `Pixel5-B15-more*.xml`, `Pixel5-B15-dialog.xml`,
`Pixel5-B15-filter.xml`, their `after-*` trees, and
`Pixel5-B15-no-layer-back.xml`.

The window publishes this exclusion region in portrait:

`SkRegion((0,876,1080,1282)(0,1295,1080,1383)(0,1385,1080,1441))`

That is a roughly 205 dp-high plot band touching both screen edges. A gesture at
y=1200 is therefore suppressed after the filter closes; moving the same gesture
to y=500 immediately performs normal Back.

#### V2-21 · Gesture Back skips More/dialog layers and is suppressed across the plot — **Medium (navigation)**

*Surface:* Google/API-34 stock gestural navigation.

There are two connected faults. More and Appearance participate in the key-Back
stack but not the stock edge-Back callback, so an edge gesture leaves the app
instead of peeling the top layer. Separately, the chart requests gesture
exclusion along both edges across a large vertical band, making system Back
unavailable there. A gesture-only user therefore gets either **too much Back**
(past the open layer) or **no Back**, depending only on y-coordinate.

**Suggested fix.** Route Android `OnBackInvoked`/`OnBackPressedDispatcher` into
the same top-layer close command used by key Back, in order: dialog, sheet,
drawer, then activity. Keep plot pan starts out of the system's edge inset so the
chart does not need a full-width exclusion region; if exclusion is unavoidable,
limit it to the smallest active drag surface. Add device tests for left and right
edges at a header coordinate and a plot coordinate with each layer open.

### 14.7 U-04 and R-11 spot checks

At `font_scale=2.0`, the Pixel Live chooser shows the disclosure and footer but
**neither scope radio exists in the accessibility tree or on screen**. Its title
also clips horizontally on the 393 dp viewport. This independently reproduces
V2-18; it does not receive a duplicate id. Evidence:
`Pixel5-U04-live-2.0.png` / `.xml`. Font scale was then restored to 1.0 and
verified (`Pixel5-U04-restored.xml`).

At scale 1.0 the Appearance and Session cache cards fit the phone viewport, keep
their footer actions visible and return through Cancel/key Back. **R-11 geometry
PASS** on DUT-2; gesture dismissal remains the B-15/V2-21 failure above.

---

## 15. Cross-device closing ledger

| Pixel extension row | Result | Release meaning |
|---|---|---|
| B-01 | **PASS** | verified HEAD bytes; five cold launches pass narrowly |
| U-07 safe area | **PASS** | cutout, rounded-corner and gesture insets respected in both orientations |
| U-07 edge-to-edge | **FAIL** | opaque off-palette API-34 system bars (V2-22) |
| B-15 | **FAIL** | stock gesture Back skips More and dialog layers (V2-21) |
| U-18 | **FAIL** | chart exclusion suppresses edge Back across the plot (V2-21) |
| R-19 | **PASS** | Back outside the exclusion region returns to the launcher normally |
| U-04 | **FAIL** | V2-18 reproduces on a second OEM/API/viewport |
| R-11 spot check | **PASS** | phone-card geometry and button/key return paths work at scale 1.0 |

W1-W5 remain explicitly not run. Wireless Debugging was off and the app had
pre-existing pairings and private sessions; entering capture would create owner-
indistinguishable data that this Release build exposes only through bulk cleanup.
The preservation boundary in §14.2 takes precedence over manufacturing extra
coverage by deleting owner state.

---

## 16. DUT-2 final cleanup and hand-back — **PASS**

| Check | Final evidence |
|---|---|
| App/process | HEAD build retained; VisualCat force-stopped; PID absent; Pixel launcher focused |
| Sessions | the same two `demo-small` sessions remain, still 10.07 MiB each / 20.14 MiB total; no test session created |
| Downloads | the same six pre-existing files remain; no file was created, renamed or deleted |
| Rotation/display | `wm user-rotation=free`; `accelerometer_rotation=1`; 1080x2340 at density 440, no override |
| Text/theme | `font_scale=1.0`; `Night mode: yes` |
| Wireless/pairings | `adb_wifi_enabled=0`; five pre-existing `VisualCat` labels remain |
| Permission | `POST_NOTIFICATIONS: granted=false` |
| Stability | zero VisualCat matches in the crash buffer; zero package FATAL/ANR matches; no orphan `logcat` child |
| Identity | serial and fingerprint match §14.1 exactly |
| Health | battery 79 %, USB powered, 32.3 °C; 96,579,548 KB available |

Available storage is 2,724 KB below the preflight reading after the in-place APK
replacement and launches; there is no new public file or test session to remove.
No temporary device mutation remains. The report restore point is **None**.

---

## 17. DUT-3 extension — Motorola edge 60 pro / Android 16

DUT-3 is a **compatibility observation, not a third authoritative HEAD run**.
The phone contained a Google-Play-signed production package and an interrupted
owner capture. Signature verification proved that the local report APK cannot
update that package in place; uninstalling it would destroy the capture. The
preservation boundary therefore wins, and every result below is explicitly
qualified as applying to the installed Play build.

### 17.1 Device and installed-package record

| Field | DUT-3 value |
|---|---|
| Transport serial | `ZY22M4T2Z4` |
| Model / manufacturer / device | `motorola edge 60 pro` / `motorola` / `cybert` |
| Android / SDK | **16 / 36** |
| Build fingerprint | `motorola/cybert_g_syse/cybert:16/W1VVS36H.7-108-8-8/856a5e-59f4b:user/release-keys` |
| ABI list | `arm64-v8a` |
| Physical display | `1220x2712`, density **450** with no override → **2.8125 px/dp**, Android reports `sw434dp` |
| Navigation | Motorola three-button mode, `navigation_mode=0`; `navbar.threebutton` enabled |
| Cutout / corners | centred punch hole `[565,0][655,128]`, top inset 128 px; rounded-corner radius 100 px |
| Refresh rate | active 90 Hz; 60/90/120 Hz modes exposed |
| Locale / zone | `en-US` / `Europe/Prague` (UTC+2) |
| Start health | battery 100 %, USB powered, 28.0 °C; thermal status 0 |
| Start storage | 209,066,632 KB available on `/data` |
| Evidence root | `artifacts/android-live-v2/ZY22M4T2Z4/` |

The installed app is a Play split package: base plus `arm64_v8a`, `en` and
`xxhdpi` configuration APKs. Package Manager reports `versionName=2.0.10`,
`versionCode=2001000`, `targetSdk=36`, installer `com.android.vending`, and
install/update time 2026-08-29 11:33:08. The pulled 15,525,577-byte base APK has
SHA-256:

`5721f1c1218c681c9e921894733912af91862a7485dd3bc661327464e1630323`

Certificate comparison is decisive:

| Package | Signer SHA-256 |
|---|---|
| Installed Play base | `a207c80ad65e25cc80a9abf6d9f05ee2d6cb7b4cef286a15d5ff312aeb38fe2d` |
| Report HEAD APK | `e58d3c4526abac2286bde04d560d761d9e0271d7c97cc132a8e68e27bc55470d` |

Android requires the same signing identity for `install -r`; these do not
match. No install was attempted and no package byte or private-data directory
was replaced. Notification permission began owner-granted and remains so.

### 17.2 Preservation boundary and starting state

VisualCat opened directly into one existing session:

- `Wireless logcat 11h34m07`;
- 47,788 entries, 21.38 MiB;
- timestamp 2026-08-29 12:12;
- **interrupted**, with its recovery notice still open.

The Session cache card confirmed **1 temporary session / 21.38 MiB** and again
offered only bulk deletion. The test never closes, dismisses, reviews, deletes,
exports or changes this session. Ten pre-existing Download files plus the hidden
`.ready_for` directory were inventoried and left alone. Wireless Debugging was
off; no pairing or capture flow was entered.

### 17.3 DUT-3 mutation ledger

| Setting/action | Original | Test value/action | Final |
|---|---|---|---|
| `font_scale` | `1.0` | `2.0` for U-04 | **`1.0` restored and verified** |
| Rotation | free; `accelerometer_rotation=1` | `wm user-rotation lock 1` | **free; accelerometer `1` verified** |
| Night mode | `yes` | unchanged | **`yes`** |
| Navigation | three-button (`0`) | unchanged | **three-button (`0`)** |
| Wireless Debugging | off (`0`) | unchanged | **off (`0`)** |
| Notification permission | granted | unchanged | **`granted=true`** |
| Play package | signed production split | read/pull only | **unchanged** |

### 17.4 B-15 / R-19 · Three-button system Back — **PASS** (compatibility)

The same layer sequence that failed through Pixel edge gestures behaves
correctly through Motorola's system Back button:

| Starting state | One system Back | Evidence |
|---|---|---|
| Session cache card | card closes; app stays focused | `Moto-B15-after-cache-back.xml` |
| More sheet | sheet closes; app stays focused | `Moto-B15-after-more-back.xml` |
| Appearance card | card closes; app stays focused | `Moto-B15-appearance.*`, `Moto-B15-after-appearance-back.xml` |
| Filter drawer | drawer closes; app stays focused | `Moto-B15-filter.xml`, `Moto-B15-after-filter-back.xml` |
| Workspace, no layer | Motorola launcher opens | `Moto-R19-after-workspace-back.xml` |

This is not evidence that V2-21 is fixed in HEAD: the package and navigation
mechanism differ. It does show that the shared key-Back layer stack is sound and
supports V2-21's diagnosis that Android stock-gesture dispatch/exclusion is the
failing path.

### 17.5 U-07 · Centred cutout, three-button insets and rotation — **PASS** (compatibility)

The API-36 window is genuinely edge-to-edge: `MainView` is
`[0,0][1220,2712]`, with no separate opaque status/navigation background nodes.
Portrait interactive content begins below the 128 px cutout at y=139 and stops
above the 135 px three-button area at y=2577.

In landscape, `MainView` becomes `[0,0][2712,1220]` and the safe workspace band
is `[128,79][2577,1220]`: 128 px clears the now-left cutout, 79 px clears the
status icons, and 135 px remains for the vertical navigation buttons on the
right. Split mode, all command buttons, the chart, entries and interrupted-state
actions remain usable. Evidence: `Moto-current.png` / `.xml` and
`Moto-U07-landscape.png` / `.xml`.

This independently agrees with DUT-1 that the API-36 path meets U-07, and
contrasts with the opaque API-34 bars in V2-22. Rotation was restored to free
immediately after capture.

### 17.6 U-04 / R-11 · Large text and phone cards

At `font_scale=2.0`, the Live chooser's Full-device radio has layout bounds
`[141,2684][876,2712]` — only its last 28 px reaches the screen — and the
restricted `Capture VisualCat only` radio is absent from the accessibility tree.
The screenshot paints **neither choice**; only the essay, Cancel, and
`Connect full-device` remain discoverable. Evidence:
`Moto-U04-live-2.0.png` / `.xml`.

That is a third-device and separate-release reproduction of existing **V2-18**,
not a new finding. The 434 dp width is sufficient for the title, unlike the
393 dp Pixel, so the hidden choices are driven by vertical content ordering and
text expansion rather than title clipping. Font scale was restored to 1.0 and
the chooser dismissed through Cancel (`Moto-U04-restored.xml`).

At default scale, Appearance and Session cache fit the 434 dp phone, retain their
footer actions, expose all controls to the accessibility tree, and return through
system Back. **R-11 PASS** as a compatibility spot check. The visible
`Delete eligible…` abbreviation still has the complete accessible name
`Delete eligible temporary sessions`; it is not promoted to a new finding.

### 17.7 Stateful cold resume and touch targets — **PASS** (informational)

Five force-stopped launches, each restoring the 47,788-entry interrupted
session, returned:

`1886, 1864, 1858, 1864, 1832 ms`

All were `LaunchState: COLD`; median **1,864 ms**, maximum **1,886 ms**. This
comfortably fits B-01's time envelope, but it is recorded as a stateful Play-build
baseline rather than an authoritative B-01 result because neither artifact nor
starting state matches §§0.4/3.

The final default-scale workspace tree contained 12 enabled clickable nodes and
**zero below 48 dp** on either dimension at 2.8125 px/dp. No crash, ANR, session
loss or recovery-notice mutation occurred across the five resumes. Evidence:
`Moto-cold-stateful.png` / `.xml`.

---

## 18. DUT-3 closing ledger

| Motorola extension row | Result | Scope meaning |
|---|---|---|
| HEAD artifact installation | **BLOCKED** | Play and report APK certificates differ; replacing the package would require destructive uninstall |
| B-15 / R-19 | **PASS (compatibility)** | every three-button layer peels once; workspace returns to launcher |
| U-07 | **PASS (compatibility)** | API-36 edge-to-edge, centred cutout and three-button insets work in both orientations |
| U-04 | **FAIL (compatibility)** | the separately packaged Play build independently reproduces V2-18 |
| R-11 | **PASS (compatibility)** | default-scale phone-card geometry and Back return paths work |
| Stateful cold resume | **PASS (informational)** | median 1,864 ms while restoring the preserved 47,788-entry session |

No new product finding id is added: the only failure is a direct V2-18
reproduction. DUT-3 broadens the physical matrix to a third OEM, centred cutout,
434 dp viewport and three-button navigation without being misrepresented as a
HEAD-artifact execution.

---

## 19. DUT-3 final cleanup and hand-back — **PASS**

| Check | Final evidence |
|---|---|
| App/process | Play build retained; VisualCat force-stopped; PID absent; Motorola launcher focused |
| Package | version/install time/installer unchanged; pulled base hash and signer remain §17.1 values |
| Session | the same `Wireless logcat 11h34m07` remains interrupted at 21.38 MiB; still the only cached session |
| Downloads | the same ten owner filenames and `.ready_for` directory remain; no test file remains on shared storage |
| Rotation/display | `wm user-rotation=free`; `accelerometer_rotation=1`; 1220x2712 at density 450, no override |
| Text/theme/navigation | `font_scale=1.0`; `Night mode: yes`; three-button `navigation_mode=0` |
| Wireless/pairings | `adb_wifi_enabled=0`; three observed pre-existing `VisualCat` ADB labels; no pairing flow run |
| Permission | `POST_NOTIFICATIONS: granted=true`, matching start |
| Stability | zero VisualCat crash-buffer matches; zero package FATAL/ANR matches; no orphan `logcat` child |
| Identity | serial and fingerprint match §17.1 exactly |
| Health | battery 100 %, USB powered, 29.9 °C; thermal status 0; 209,065,384 KB available |

Available storage is 1,248 KB below preflight after UI caches and launch work;
there is no new public file or private session to remove. Every device temporary
screenshot/XML path was deleted after pulling the evidence. No temporary setting
mutation remains. The report restore point is **None**.

---

## 20. Post-report implementation and physical-device verification

This section continues the completed discovery record as an implementation
journal. It is updated at every build/install/test checkpoint. The original
observations in §§1–19 are not rewritten; each finding is closed here only after
an automated regression check and a physical-device observation agree.

### 20.1 Continuation baseline — **RECORDED**

| Field | Value |
|---|---|
| Continuation start | 2026-08-29 21:52 local (UTC+2) |
| Repository | `main` at `0c9dd02`; no source modifications |
| Pre-existing working tree | staged deletion of `docs/ANDROID-AUDIT-CONTINUATION.md`; this untracked report; both preserved as owner work |
| Connected DUT | `RFCRC0A9GND`, Samsung SM-G990B, Android 16 / API 36; fingerprint still matches §0.2 |
| Display | physical 1080×2340; physical density 480; owner override density 360 preserved |
| Device settings | `font_scale=1.0`; night mode `yes`; rotation `free` / accelerometer rotation `1`; Wireless Debugging `0` |
| Installed package | `com.barebit.visualcat` 2.0.10-dev / 2001000, sideloaded, stopped, no running PID |
| Destructive authorization | user explicitly permits updating or deleting the installed app; repository owner changes remain out of scope |

### 20.2 Implementation ledger

`PENDING` means the report's suggested change has not yet been reconciled with
the current code. `IMPLEMENTED` requires code and focused automated checks.
`DEVICE PASS` additionally requires observation on the connected physical DUT.

| Work item | State | Next proof |
|---|---|---|
| V2-18 large-text Live chooser | **PENDING** | responsive/scrollable chooser at 1.0 and 2.0 text scale; both scope choices and safe restricted action reachable |
| V2-21 gesture Back and plot exclusion | **PENDING** | shared layer-aware Android Back callback; no full-width plot exclusion |
| V2-14 unknown/continuation disclosure | **PENDING** | explicit unknown/continuation count and explanation without changing ADR 0009 |
| V2-05 chip/Clear-all touch targets | **PENDING** | every enabled device target at least 48×48 dp |
| V2-17 four-row entry floor at large text | **PENDING** | measured four-row allocation at 1.3–2.0 text scale |
| V2-19 cancellable Load all | **PENDING** | phone can request/cancel full paging with an honest row/memory warning |
| V2-20 stable Copy-raw slot | **PENDING** | confirmation cannot move the repeated-tap target |
| V2-01 false/clipped scrollbar | **PENDING** | correct finite extent and Android overlay/auto policy |
| V2-02 empty-workspace composition | **PENDING** | useful inline recents or centred compact hero without the unfinished void |
| V2-03 empty recent-captures dialog | **PENDING** | explicit empty state, useful action, no inert Open/taxonomy |
| V2-04 source-context code legend | **PENDING** | touch-visible conditional legend |
| V2-06 filtered-out selected entry | **PENDING** | Copy/Open agree on one deterministic selection policy |
| V2-07 first/last search navigation | **PENDING** | reachable first/last match and wrap verification |
| V2-09 timeline endpoint overscroll | **PENDING** | bounded affordance that cannot imply non-existent session time |
| V2-10 reopened-session viewport | **PENDING** | persisted viewport or initial fit-to-data; no empty live-edge plot |
| V2-11 clipped live notice | **PENDING** | full instruction visible and accessible at supported text scales |
| V2-12 empty-file error | **PENDING** | dedicated empty-file presentation before format detection |
| V2-13 counter population mismatch | **PENDING** | labels/tooltips state populations or counters use one population |
| V2-15 CSV scope copy | **PENDING** | copy matches actual export scope, or a scope chooser exists |
| V2-16 leftmost scrolled-tab close | **PENDING** | visible tab can always be closed safely |
| V2-22 API-34 system-bar palette | **PENDING** | explicit edge-to-edge window configuration with inset-safe content |
| PLAN-01 generator `--format` | **PENDING** | deterministic documented format variants and CLI tests |
| Frame-instrumentation amendment | **PENDING** | reproducible Perfetto/FrameTimeline procedure replaces invalid `gfxinfo` gate |

**Restore point:** inspect the present implementation and tests for the first
release-significant batch (V2-18, V2-21, V2-14, V2-05, V2-17), then record the
code baseline before editing.

---
### 20.3 Implementation plan — batches and their proofs

The 23 work items are executed in four batches, ordered by §11's release
significance. Each batch is a complete cycle: **code → focused headless tests →
`Release` APK → clean sideload onto DUT-1 → physical observation → ledger
update**. A batch is never left half-recorded: §20.2's state column is advanced
only when both proofs exist, and §20.4 onwards holds the per-batch journal.

| Batch | Work items | Rationale |
|---|---|---|
| **A** | V2-18, V2-21, V2-14, V2-05, V2-17 | §11's release-significant set: accessibility, navigation, data honesty, touch floor |
| **B** | V2-19, V2-20, V2-01, V2-02, V2-03 | first-run composition and the million-row phone workflow |
| **C** | V2-04, V2-06, V2-07, V2-09, V2-10, V2-11, V2-12, V2-13, V2-15, V2-16, V2-22 | correctness/clarity across workspace surfaces |
| **D** | PLAN-01, frame-instrumentation amendment | tooling and plan defects, no application surface |

**Device policy for this continuation.** DUT-1 (`RFCRC0A9GND`) is the only
connected device. The owner has authorised updating or deleting the installed
app. Every *global* device mutation made from here on is appended to §20.9 with
its original value and its restore command, and §20.10 verifies the restore, in
the same shape as §0.5/§13.

**Build identity.** Every device-verified build in this continuation is a
`Release` build of `src/VisualCat.Android`, sideloaded with `adb install -r`, and
identified by the `versionName+commit` string the empty state prints. The
baseline artifact is the one in §0.4 (`68692776…`, commit `0c9dd02`).

---

### 20.4 Batch A journal

**Status: in progress.** Code baseline recorded below before any edit.

#### 20.4.1 Code baseline for Batch A — **RECORDED**

Read at commit `0c9dd02`, before any modification.

| Finding | Present implementation | Why the report's assertion holds against it |
|---|---|---|
| V2-18 | `OnDeviceLogAccessDialog` (`src/VisualCat.App/Views/WirelessAdbSetupDialog.cs:46-197`) builds a `StackPanel` in this order: 15 dp lead-in, a 90-word disclosure paragraph, then `Choice(_fullDevice…)`, then `Choice(_visualCatOnly…)`. It is hosted by `MainView.ShowDialogAsync` → `BuildSheet(scrolls: !ScrollsInternally)`; `ScrollsInternally = true`, so the card gets `SheetForm.Build`'s own `ScrollViewer` and **no scroll affordance at all** — `FadingScrollHost` is only attached on the `scrolls: true` path. | The two decisions are last in a scroller with no fade, no chevron, and a `PreferredSize`/`MaxHeight` that caps at `SheetHeightCap(viewport) = max(240, 0.82·h)`. At 1.8–2.0 the paragraph alone exceeds the card. |
| V2-21 | Back arrives at `MainView.OnBackRequested` (`MainView.Overlays.cs:726`) via `TopLevel.BackRequestedEvent`, added in `MainView.cs:306`. Avalonia 12.1's `AvaloniaActivity` owns the only `OnBackPressedCallback` (confirmed in `Avalonia.Android.dll`: `HandleOnBackPressed`, `_currentBackPressedCallback`, `ShouldNavigateBack`). `MainActivity` registers **no** callback of its own. Gesture exclusions are published by `EdgeGestureGuard.Publish` for every tracked control that is `IsEffectivelyVisible`, and `Measure` deliberately widens each claim to **the full window width** (`left = 0`, `right = root.Bounds.Width`). `ModalWorkspaceBand` seals the workspace for *accessibility only* and has "no visual effect", so the plot stays effectively visible under a sheet — and its full-width exclusion stays published while a modal is open. | Both faults are structural: nothing in the app owns the platform back contract, and the exclusion is neither narrowed to the edges nor suspended under a modal. |
| V2-14 | `ADR 0009` keeps unmatched lines as `unknown`; the count is carried by the store and printed by the CLI. The workspace count line and the severity legend are built from the timed entry population only. No filter or chip exposes `ParseOutcomeKind`. | The number exists and is never surfaced. |
| V2-05 | `SessionWorkspaceView.Facets.cs:453-483` — `AddChip` builds the `×` as a `Button` with `MinWidth = 0`, `Padding = 4,0`, no `MinHeight`; the chip `Border` has no `MinHeight` and its label is inert. `SessionWorkspaceView.cs:1723-1731` — `_clearFilters` and the chip bar are `MinHeight = _mobile ? 40 : 0`, a chip-bar-local literal that predates `TouchTarget`. | Measured 15.6 × 16.4 dp and 40.0 dp exactly match these three literals. |
| V2-17 | `SessionWorkspaceView.Interactions.cs:229` `ApplyEntryRowHeight(64)` sets a **constant** row floor; `SessionWorkspaceView.Mobile.cs:615` feeds that same constant to the allocator as `EntryRowHeight`. The allocator (`MobilePaneAllocation.cs:203-...`) computes `preferredAnalysis = chrome + 4 × 64` — a fixed 256 dp — regardless of `TextScale.Effective`. | The pane's floor is 4 × the *design* row, while the drawn row is content-sized and reaches 96.9 dp at 2.0. Hence the measured constant 558 px container. |

#### 20.4.2 Batch A changes — **IMPLEMENTED**

Working-tree note: `FIRST-RELEASE-PLAN.md` was found deleted and staged during
this continuation, which was not the state §20.1 recorded. It was restored from
`HEAD` and the working tree now matches §20.1 plus the changes below. Recorded
here because the deletion was not an intended part of this work.

| Finding | Change | File(s) |
|---|---|---|
| V2-18 | The two scope choices move **above** the disclosure; the 90-word paragraph becomes a labelled disclosure (*How full-device capture works ▾*) that is collapsed by default, with the one sentence a reader is entitled to without asking — *"Nothing is uploaded. Everything stays on this device."* — always on the card. The full text is unchanged and still in the tree, so the existing `WirelessAdbSetupTests` disclosure contract still holds. | `Views/WirelessAdbSetupDialog.cs` |
| V2-18 | Every internally-scrolling dialog gets the same edge treatment the in-page sheet host already applies: `SheetForm.Build` now wraps its `ScrollViewer` in `FadingScrollHost` and re-resolves the fade on theme change. This covers *Appearance & timeline* and *Session cache* as well. | `Views/SessionDialogs.cs` |
| V2-18 | The sheet/dialog heading wraps instead of clipping, which is the Pixel's horizontal title clip from §14.7. | `Views/MainView.Overlays.cs` |
| V2-21 | `MainView.OnBackRequested` is split into `MainView.TryNavigateBack()` — one layer-aware implementation (escape echo → overlay stack → workspace transient state) — installed on `PlatformSourceRegistry.TryNavigateBack` while the view is attached. | `Views/MainView.Overlays.cs`, `Views/MainView.cs`, `Platform/PlatformSourceRegistry.cs` |
| V2-21 | `MainActivity` registers its **own** `AndroidX.Activity.OnBackPressedCallback` after `base.OnCreate`, so it is offered the press before Avalonia's. Unhandled presses fall through with the AndroidX idiom (disable, re-dispatch, re-enable) rather than a `Finish`, so backgrounding stays the platform's decision. | `src/VisualCat.Android/MainActivity.cs` |
| V2-21 | `android:enableOnBackInvokedCallback="true"`. Android 15 turns predictive back on by default at target API 35 or later and stops calling `Activity.onBackPressed` entirely; below that it stays off unless asked. Declaring it makes API 31–36 use one mechanism, which is the divergence the Pixel/Samsung split smelled of. | `Properties/AndroidManifest.xml` |
| V2-21 | `EdgeGestureGuard.Suspend(bool)` releases every exclusion rectangle while a modal layer is over the workspace, and takes them back when the last one closes. Wired from `MainView.ApplyOverlayModality`, so it is recomputed rather than toggled. **This is the half of V2-21 that actually restores Back:** nothing behind the scrim can be dragged, so nothing behind it has any business holding the platform's edge gesture. | `Platform/EdgeGestureGuard.cs`, `Views/MainView.Overlays.cs` |
| V2-14 | The count line names the unparsed population: `… · N unparsed lines`. | `Views/SessionWorkspaceView.Presentation.cs` |
| V2-14 | New `SessionQueryEngine.ScanSourceRecords` — a **bounded** forward scan over the physical source stream, with a record cap and a scan cap, using the existing `source-order/index.bin` seek. | `Core/Query/SessionQueryEngine.cs` |
| V2-14 | New `SessionTabViewModel.LoadUnparsedLinesAsync` and `UnparsedLineCount`, and a new `UnparsedLinesDialog` reachable from *More → Unparsed lines…* (enabled only when the session has any). It renders the same gutter form as *Source context*, so the two panes read as one view of one file, and offers Copy. **ADR 0009 is untouched** — this is presentation and a read path, exactly as the finding asks. | `Presentation/SessionTabViewModel.cs`, `Views/UnparsedLinesDialog.cs`, `Views/MainView.cs` |
| V2-14 | One notice at import when the file has unparsed lines or untimed records, naming the route to them. Raised once per session from `UpdateSessionInfo`. | `Views/SessionWorkspaceView.Panes.cs` |
| V2-13 *(pulled forward from Batch C — same code)* | `in session` becomes `timed in session` whenever anything is outside that population, and `· N untimed` is stated beside the match count. `3,425 match` beside `2,225 timed in session · 1,200 untimed` is no longer a contradiction with no explanation. | `Views/SessionWorkspaceView.Presentation.cs` |
| V2-04 *(pulled forward from Batch C — same code)* | New `ParseOutcomeLegend`: `en entry · mt marker · .. continuation · e? untimed · ?? unknown · !! rejected`, rendered as a line of the *Source context* pane and of the new dialog, and shown **only when a code other than `en` is on screen**. | `Views/ParseOutcomeLegend.cs`, `Views/SessionWorkspaceView.RawContext.cs` |
| V2-05 | The chip **is** the control: one `Button` at the touch floor whose whole area removes the filter, with the `×` as an inner 16 dp glyph. One node, one name (`Remove filter …`), one outcome. | `Views/SessionWorkspaceView.Facets.cs` |
| V2-05 | `Clear all` and the chip bar take `TouchTarget`, replacing the chip-bar-local literal `40`. | `Views/SessionWorkspaceView.cs` |
| V2-17 | The allocator is fed the row height the list **draws** (`UpdateEntryRowMeasurement`, the tallest realised container) rather than the design constant `_entryRowMinimumHeight`. | `Views/SessionWorkspaceView.Mobile.cs` |
| V2-17 | When a stacked Split cannot seat four whole rows even with the plot at `TimelineRenderingFloor`, the workspace composes **Details** and the mode row says why (`Not enough room at this text size — showing Details`), with one row of hysteresis so the two compositions cannot alternate. | `Views/SessionWorkspaceView.Mobile.cs` |

#### 20.4.3 Batch A automated proof — **PASS**

New file `tests/VisualCat.App.Tests/LiveTestV2RemediationTests.cs`, 12 tests.
Full suites: **`VisualCat.App.Tests` 423/423 pass**, `VisualCat.Core.Tests`
101/101 pass.

Each new test was also run against the shipped implementation, to prove it is a
regression test and not a tautology. That was done by restoring individual files
from `HEAD` and re-running:

| Test | Verified red against `0c9dd02`? | Evidence |
|---|---|---|
| `BothLiveScopeChoicesStayInsideTheCardAtEveryTextScale` | **Yes** — fails at scales **1.5, 1.8 and 2.0**, passes at 1.0 and 1.3 | with `WirelessAdbSetupDialog.cs` and `SessionDialogs.cs` at `HEAD`: `Failed: 4, Passed: 2` (the fourth failure is the disclosure test below). This brackets the device's own 1.8/2.0 boundary and is stricter by one step, because the headless card is the 464 × 853 dp the sheet cap actually gives it. |
| `TheLiveScopeDisclosureIsCollapsedButNamedAndReachable` | **Yes** — same run | no such control existed |
| `TheChipBarClearsTheTouchFloorOnAPhone` | **Yes** | with the two size literals put back (`MinWidth = 0`, `MinHeight = _mobile ? 40 : 0`): `Failed: 1` |
| `TheBudgetedRowHeightFollowsTheDrawnRowAtLargeText` | **Yes** | with the request reverted to `_entryRowMinimumHeight`: *"budgeted 64 dp against a drawn 93 dp row"* — V2-17 reproduced headlessly, numerically |
| `SuspendingTheEdgeGuardReleasesEveryClaimAndResumeTakesThemBack` | n/a — the API is new | asserts release, idempotence, and resume |
| `ACrashLogCountsAndSurfacesItsStackFrames`, `TheCountLineNamesTheTimedPopulationWhenSomethingIsOutsideIt` | n/a — the counts and the read path are new | the corpus is a real ThreadTime crash block; the frames stay `??`, which is ADR 0009 holding |
| `TheEntriesFloorIsBudgetedFromTheDrawnRowHeight` | **No — and it is kept as a unit contract, not as proof.** The allocator always multiplied whatever it was handed; the defect was in the caller. `TheBudgetedRowHeightFollowsTheDrawnRowAtLargeText` is the test that carries V2-17. | — |

One pre-existing flake was observed and is **not** attributable to this work:
`LiveTestRemediationTests.ASupersededTransientStatusClearsWhenTheNextQueryLands`
failed once inside a full run and passes in isolation and in every subsequent
full run. Recorded rather than ignored; it is a candidate for separate
investigation.

#### 20.4.4 Batch A device verification — **DEVICE PASS**

All observations on DUT-1 `RFCRC0A9GND` (Samsung SM-G990B, Android 16 / API 36,
2.25 px/dp effective). Evidence under
`artifacts/android-live-v2/RFCRC0A9GND/impl/`.

| Build | SHA-256 | What it added |
|---|---|---|
| A-1 | `1d8e76b2a501b95e89feb6f92e07d00c541cb4c3ce33d496d8030538575002b8` | the §20.4.2 table |
| A-2 | `4a2a2231f257a13b4029d7eee1ff3953a41a31ab4c063706cb605650190b4916` | V2-23, found on the device while verifying V2-18 |
| A-3 | *(built and installed after the scrim and Fit-claim changes)* | V2-21's real root cause, and the Fit-time claim release |

> The identity line still reads `VisualCat 2.0.10-dev+0c9dd02` because the commit
> has not moved; **the APK SHA-256 and `pm install` time are the build identity in
> this continuation**, not the identity line. Recorded here so a later reader does
> not mistake one build for another.

**Test data.** Regenerated deterministically, `vc3-` prefix, from the shipped CLI
at this commit (seed 42):

| File | `wc -l` | Bytes | Oracle (`vcat info`) | MediaStore `_id` |
|---|---:|---:|---|---|
| `vc3-tiny.txt` | 1 001 | 90 384 | — | 1000000663 |
| `vc3-small.txt` | 50 001 | 4 500 749 | — | 1000000662 |
| `vc3-continuations.txt` | 1 801 | 108 229 | `sourceLines 1801, timedEntries 600, unknownLines 1200`, confidence **0.337** | 1000000660 |
| `vc3-mixed-formats.txt` | 3 478 | 249 290 | `sourceLines 3478, timedEntries 2277, untimedEntries 1199, rejectedCandidates 1` | 1000000661 |

The two adversarial files are built by
`C:\…\scratchpad\adversarial.py`, a pure function of `small.txt` plus fixed
constants — no manual editing, per plan §3.2. They reproduce §2.2's
`continuations.txt` and `mixed-formats.txt` shapes and their oracles match the
numbers the original run recorded.

##### V2-14 / V2-13 / V2-04 — **DEVICE PASS**

`vc3-continuations.txt`, count line, verbatim from the tree:

```
600 in view · 600 match · 600 timed in session · 1,200 unparsed lines
```

Notice lane, verbatim:

> 1,200 of 1,801 lines are not logcat records — usually stack-trace frames. They
> are kept byte for byte; open them from More → Unparsed lines…

*More actions* now carries **Unparsed lines…** under `THIS SESSION`, enabled,
`432 × 56 dp`. Opening it gives a card titled *Lines that are not logcat
records* with `1,200 unknown`, the explanation, the decoded legend
(`en entry · mt marker · .. continuation · e? untimed · ?? unknown · !! rejected`)
and the lines themselves in the source pane's own gutter form, starting at
physical line **5** — the first stack frame. `A-unparsed.png` is the screenshot.

`vc3-mixed-formats.txt` closes V2-13 on the device:

```
2,277 in view · 3,476 match · 2,277 timed in session · 1,199 untimed
```

`3,476 > 2,277` is still true and is no longer a contradiction: the last number
says which population it counts, and the population that explains the difference
is named beside it.

One wording defect was found and fixed during this verification: with a single
rejected line the notice read *"1 of 3,478 lines are not logcat records — usually
stack-trace frames"*, diagnosing a stack trace from one line. The
stack-trace hint is now offered only when the population is large enough to
justify it (≥ 20 lines, or ≥ 5 % of the file); otherwise the notice simply
reports the count. `Counted.Lines` was added for the singular.

##### V2-05 — **DEVICE PASS**

| Control | Before (§V2-05) | Now, measured at 2.25 px/dp |
|---|---|---|
| filter chip's remove target | `[196,512][231,549]` — **15.6 × 16.4 dp**, and the label beside it was dead | `[56,488][247,599]` — **84.9 × 49.3 dp**, one node named `Remove filter levels: F,?` |
| `Clear all` | 68.0 × **40.0 dp** | `[877,488][1030,599]` — 68.0 × **49.3 dp** |

A tap at `(150, 543)` — the chip's *label*, which previously did nothing —
removed the filter and the chip bar returned to
`No filters · showing everything in view`.

##### V2-17 — **DEVICE PASS**

| `font_scale` | Entries `ListBox` before | Entries `ListBox` now | Drawn row | Rows |
|---:|---:|---:|---:|---:|
| 1.0 | 558 px / 248.0 dp | 248.0 dp | 64.0 dp | 3.9 |
| 1.3 | 558 px / 248.0 dp | **269.3 dp** | 69.3 dp | 3.9 — the fourth row's timestamp line is inside the list again |
| 2.0 | 558 px / 248.0 dp | **445.3 dp** | 96.9 dp | **4.6** |

At 2.0 the workspace composes **Details** and the mode row shows `Logs` as the
active mode, with the Split button carrying *"Not enough room at this text size —
showing Details"*. There is no heat map in the tree at that scale, which is the
point: the reader gets four and a half whole rows instead of a 137 dp plot and
two and a half. `A-scale20.png` is the screenshot.

##### V2-18 — **DEVICE PASS** (the run's one High finding)

At `font_scale` **2.0**, *Choose what Live captures* now paints, in one screen
with no scrolling:

```
CE [112,680][701,788]   261.8x48.0dp   Full-device capture
CE [112,1193][811,1301] 310.7x48.0dp   Capture VisualCat only
CE [83,1903][979,2011]  398.2x48.0dp   How full-device capture works
CE [228,2028][460,2136] 103.1x48.0dp   Cancel
CE [477,2028][997,2136] 231.1x48.0dp   Set up full-device
```

Both radios are painted, both are in the accessibility tree, both are 48 dp
targets, and the zero-setup path is one tap away. Compare the original: at 1.8
the second radio was **absent from the tree entirely** and at 2.0 **neither was
painted**. `A-live20.png` is the screenshot.

##### V2-21 — **DEVICE PASS**, with the root cause corrected

DUT-1 was switched to stock gesture navigation for this
(`cmd overlay enable com.android.internal.systemui.navbar.gestural`,
`navigation_mode` `0` → `2`; restored in §20.9).

The report attributes the first fault to More and Appearance "not
participating in the stock edge-Back callback". On the device that is **not**
what happens, and the real cause is worth recording because it changes the fix:

1. A stock edge-Back gesture starts as an ordinary `ACTION_DOWN` on whatever is
   under the finger. The system only claims it once the finger has travelled.
2. The sheet's scrim dismissed on `PointerPressed`. So the touch-down **closed
   the sheet**, and the same gesture then arrived as Back with an empty overlay
   stack, and the platform backgrounded the task.
3. One gesture, two consumers — and the reader lands on the launcher. Key Back
   and a tapped scrim always looked correct, which is exactly why this survived.

The scrim now dismisses on `PointerReleased`. A tap still dismisses; a gesture
the system takes away is cancelled and never completes.

| Check (gesture navigation) | Before this batch | After |
|---|---|---|
| More sheet open, edge swipe at y=1200 | launcher — sheet and app both skipped | **sheet closes, app stays** |
| Live card open, key Back | launcher (V2-23) | **card closes, app stays** |
| More sheet open, key Back | sheet closes, app stays | unchanged |
| Filter drawer open, edge swipe | drawer closes, app stays | unchanged |
| Exclusion region while a sheet is open | plot band still claimed | `SkRegion((1038,318,1080,659))` only — the plot band is **released** |

The second fault — system Back suppressed across the plot band — is now bounded
by whether a pan can do anything:

| State | `mSystemGestureExclusion` | Back at y=1200 |
|---|---|---|
| session at **Fit** | `((1038,318,1080,659)(0,1004,1080,1090)(0,1091,1080,1137))` — minimap 38 dp + divider 20 dp only | **works** |
| **zoomed in** | `((1038,318,1080,659)(0,805,1080,1123)(0,1134,1080,1220)(0,1222,1080,1268))` — the plot band is back, trimmed from its top by the 200 dp budget | suppressed over the plot, by design |
| any layer open | `((1038,318,1080,659))` | **works** |

At Fit the viewport already spans the session, so a pan cannot move it; claiming
the edge there took a system gesture away and gave the reader nothing. Every
import and every reopen starts at Fit. The claim remains while a pan is real,
which is F-28's actual contract — the two existing tests that encode it were
updated to exercise the zoomed state and to assert the new Fit behaviour
explicitly, rather than being weakened.

The residual `(1038,318,1080,659)` rectangle in every state is **not VisualCat's**
— it is the phantom scrollbar V2-01 records, which the platform publishes as its
own gesture exclusion. Batch B removes it.

##### V2-23 · One Back press took down a layer *and* left the app — **Medium (navigation)** — NEW, found during implementation

*Surface:* any in-page dialog whose Cancel carries `IsCancel` — in practice
*Choose what Live captures*, which is the dialog V2-18 exists to make reachable.

**Observed.** With the Live scope card open on DUT-1, one `KEYCODE_BACK`
returned the reader to the **launcher**. The *More* sheet, tested immediately
before and after, closed correctly and kept the app.

**Cause.** Android delivers a key Back as a `Key.Escape` key-down followed by the
platform back callback. Avalonia's `Button` registers a handler for `Escape` on
the visual root when `IsCancel` is set, so the key-down closed the card; the back
callback then found an empty overlay stack and let the platform background the
task. The *More* sheet has no such button, which is why it was unaffected — and
why the original pass recorded Back as passing for the dialogs it happened to
try.

**Fix.** The shell claims `Escape` at the top level, tunnelling, while any layer
is open: it takes the top layer down and records the echo the back callback
reads. Tunnelling is load-bearing — `Button`'s hook is on the root's bubble
phase — and so is the top level, because with a card open and nothing focused
inside it the key route's source *is* the top level, so a handler on `MainView`
never sees the key at all.

**Regression test.** `OneBackPressTakesDownOneLayerEvenWithAnIsCancelButtonInIt`.
It needed a new seam, `MainView.InPageDialogOverride`, because a headless run is
neither Android nor a desktop window and took the modal-`Window` path — so the
overlay stack, its Back contract and its modality seal had **no headless coverage
at all**, which is how this reached a device.

##### Test-suite stability — an honest note

While running Batch A the full `VisualCat.App.Tests` suite failed intermittently,
one test per run, with a different test each time
(`SessionWorkspaceHeadlessTests`, `PixelGestureAndTextScaleTests`,
`WorkspaceReviewFixHeadlessTests`, `LiveTestRemediationTests`). To establish
whether this work caused it, a pristine `git worktree` at `0c9dd02` was built and
run three times: **it failed once as well**, on a test this work does not touch
(`AFollowRefreshKeepsAndExplainsAnEntryThatAgesOutWithAnotherPageAvailable`).
The flakiness is therefore **pre-existing and independent of this change**;
parallelisation is already disabled, so it is ordering or timing, not
concurrency. Final Batch A state: `424/424` on consecutive full runs.

### 20.5 Ledger after Batch A

`PENDING` means the report's suggested change has not yet been reconciled with
the current code. `IMPLEMENTED` requires code and focused automated checks.
`DEVICE PASS` additionally requires observation on the connected physical DUT.
This table supersedes §20.2.

| Work item | State | Where the proof is |
|---|---|---|
| V2-18 large-text Live chooser | **DEVICE PASS** | §20.4.3 (red at 1.5/1.8/2.0 before, green after), §20.4.4 `A-live20.png` |
| V2-21 gesture Back and plot exclusion | **DEVICE PASS** | §20.4.4 — root cause corrected; exclusion released under a layer and at Fit |
| V2-23 Back leaves the app past an `IsCancel` dialog | **DEVICE PASS** (new, found here) | §20.4.4 |
| V2-14 unknown/continuation disclosure | **DEVICE PASS** | §20.4.4 — count line, notice, *Unparsed lines…* card; ADR 0009 untouched |
| V2-13 counter population mismatch | **DEVICE PASS** (pulled forward) | §20.4.4 — `mixed-formats` count line |
| V2-04 source-context code legend | **DEVICE PASS** (pulled forward) | §20.4.4 — legend rendered in both source surfaces |
| V2-05 chip/Clear-all touch targets | **DEVICE PASS** | §20.4.4 — 84.9 × 49.3 dp and 68.0 × 49.3 dp measured |
| V2-17 four-row entry floor at large text | **DEVICE PASS** | §20.4.4 — 248 → 269.3 → 445.3 dp at 1.0/1.3/2.0 |
| V2-19 cancellable Load all | **PENDING** | Batch B |
| V2-20 stable Copy-raw slot | **PENDING** | Batch B |
| V2-01 false/clipped scrollbar | **PENDING** | Batch B — and it is the residual gesture-exclusion rectangle in §20.4.4 |
| V2-02 empty-workspace composition | **PENDING** | Batch B |
| V2-03 empty recent-captures dialog | **PENDING** | Batch B |
| V2-06 filtered-out selected entry | **PENDING** | Batch C |
| V2-07 first/last search navigation | **PENDING** | Batch C |
| V2-09 timeline endpoint overscroll | **PENDING** | Batch C |
| V2-10 reopened-session viewport | **PENDING** | Batch C |
| V2-11 clipped live notice | **PENDING** | Batch C |
| V2-12 empty-file error | **PENDING** | Batch C |
| V2-15 CSV scope copy | **PENDING** | Batch C |
| V2-16 leftmost scrolled-tab close | **PENDING** | Batch C |
| V2-22 API-34 system-bar palette | **PENDING** | Batch C — no API-34 device is connected; see the coverage note there |
| PLAN-01 generator `--format` | **PENDING** | Batch D |
| Frame-instrumentation amendment | **PENDING** | Batch D |

**Restore point:** Batch A is closed on code, tests and hardware. Resume at
**Batch B (V2-19, V2-20, V2-01, V2-02, V2-03)**, starting with the code baseline
for those five.

### 20.6 Continuation device-mutation ledger

Every global DUT-1 mutation made after §20.1, its original value, and its restore
command. Verified in §20.10 at hand-back. This is the same discipline as §0.5.

| # | Setting | Original | Changed to | Restore command | State |
|---|---|---|---|---|---|
| 1 | `system font_scale` | `1.0` | `1.3` → `2.0` → `1.8` | `settings put system font_scale 1.0` | **restored; verified `1.0`** |
| 2 | navigation mode overlay | three-button (`navigation_mode=0`) | `cmd overlay enable com.android.internal.systemui.navbar.gestural` (`navigation_mode=2`) | `cmd overlay enable com.android.internal.systemui.navbar.threebutton` | **restored; verified `0`** |
| 3 | installed `com.barebit.visualcat` | 2.0.10-dev, sideloaded, first-run data | replaced by Batch A builds A-1/A-2/A-3; `pm clear` run twice | reinstall or uninstall at hand-back, owner's choice — the owner authorised updating or deleting it (§20.1) | open until §20.10 |
| 4 | files in `/sdcard/Download` | — | `vc3-tiny.txt`, `vc3-small.txt`, `vc3-continuations.txt`, `vc3-mixed-formats.txt` | delete each by exact path | open until §20.10 |
| 5 | `POST_NOTIFICATIONS` | `granted=false` | not requested in this batch | — | unchanged |

### 20.7 Batch B journal — V2-19, V2-20, V2-01, V2-02, V2-03

#### 20.7.1 Batch B changes — **IMPLEMENTED**

| Finding | Change | File(s) |
|---|---|---|
| V2-19 | The phone footer becomes a two-control band: `Load 500 more` keeps its full-width target, and a second **All** button beside it toggles the cancellable batch load. Both carry `TouchTarget` on **height and width** — a three-character label sizes itself to about 34 dp, which the device duly measured before the width floor was added. | `Views/SessionWorkspaceView.cs`, `Views/SessionWorkspaceView.Mobile.cs` |
| V2-19 | `_loadAll`'s label/enablement/name is hoisted out of `if (!_mobile)`; its spoken name carries the exact remainder, because the phone label is three characters wide and the number is the part that matters. | `Views/SessionWorkspaceView.Interactions.cs` |
| V2-19 | Above `LoadAllConfirmationThreshold` (100,000 outstanding rows) the action asks first, naming the row count and that every row is held in memory, and saying it can be cancelled. Presented through a new `SessionWorkspaceView.ConfirmAsync` hook, because presentation is the shell's — the same rule export pickers and partial-recovery dispositions already follow. Uninstalled, the answer is "yes", so a desktop or a test that never wired it is never blocked by a question nobody can see. | `Views/SessionWorkspaceView.Interactions.cs`, `Views/SessionWorkspaceView.cs`, `Views/MainView.cs` |
| V2-20 | The workspace is told how much height the shell's notice lane is taking (`SetNoticeReserve`), resolves its band **as though the lane were not there**, and pins the plot to the height it was actually drawn at when the lane arrived. Everything above the entries list keeps its coordinates; the list gives up the rows. | `Views/SessionWorkspaceView.Mobile.cs`, `Views/MainView.Notice.cs` |
| V2-20 | Pinned to the *drawn* height, not to the allocator's share: those differ whenever the analysis pane's four-row minimum is binding, which is the ordinary case — and pinning to the share moved the pane 122 px the *other* way on the device, which is how this was caught. | `Views/SessionWorkspaceView.Mobile.cs` |
| V2-02 | The hero is centred by a host that is never shorter than the viewport. `ScrollViewer.VerticalContentAlignment` was already set to `Center` and never did anything — a presenter measures its child against infinity and arranges it from the top — so the alignment that had been asked for could not take effect. | `Views/MainView.cs` |
| V2-01 (policy half) | On a touch build the empty state's scrollbar is `Hidden` and the surface is wrapped in `FadingScrollHost`, which is the affordance every other scrolling surface in the product uses. `FadingScrollHost` gained a `raised` flag so the fade matches the shell ground rather than a card. | `Views/MainView.cs`, `Views/FadingScrollHost.cs` |
| V2-03 | With nothing stored the card says **"No captures on this device yet."**, explains what a capture is in one line, offers **Capture this device's log**, renders no `Open`, and withholds the complete/interrupted/in-progress legend until there is a list for it to explain. Its list row becomes `Auto`, so an empty card is small and a full one is tall. The capture action is routed by the shell into the ordinary Live scope chooser. | `Views/SessionDialogs.cs`, `Views/MainView.cs` |

#### 20.7.2 V2-01 — **NOT REPRODUCIBLE AS AN APPLICATION DEFECT**

This is a correction to the report, made from the device and supported by four
independent pieces of evidence. The permanent, half-clipped grey bar at the right
edge is **Samsung's Edge panel handle**, a system overlay, not a VisualCat
scrollbar.

| Evidence | What it shows |
|---|---|
| `dumpsys window windows` | `Window #5 … com.sec.android.app.launcher/com.samsung.app.honeyspace.edge.edgepanel.app.CocktailBarService` — Samsung's Edge panel is a live window on this device |
| `O-launcher.png` | With VisualCat **force-stopped**, on the launcher, the same bar is at the same coordinates. It is translucent over a wallpaper, which is why it reads as decoration there and as a scrollbar on VisualCat's near-black ground |
| The published exclusion region | `(1038,318,1080,659)` is **byte-identical in every app state** — first run, a session open, a sheet open, a dialog open, and after the empty state's scrollbar was set to `Hidden`. An application scrollbar cannot be state-invariant, and cannot survive being hidden |
| Headless probe | The empty state's `ScrollViewer` reports `extent == viewport` (`464 × 958`) at 480 × 1040 with the content at `416 × 910`, **before** any change. There was no phantom extent to find |

The finding's *observation* is real — a false affordance is on that screen — and
the report's reasoning from it was sound given what could be seen. The
attribution is what does not hold, and no application change can remove another
app's overlay. What the report asked for on the policy side is implemented
anyway, because it is right on its own terms: the Android composition no longer
carries a desktop scrollbar at all, and the fade replaces it.

V2-02, which the report raised beside it, **was** a genuine application defect
and is fixed.

#### 20.7.3 Batch B automated proof — **PASS**

Four new tests, all verified red against the shipped behaviour by reverting the
specific change and re-running:

| Test | Red before | Evidence |
|---|---|---|
| `ThePhoneFooterOffersLoadAll` | **Yes** | with `_loadAll` removed from the footer row: `Failed: 1`. Also asserts both target floors — the width floor is the one the device caught at **33.8 dp** |
| `TheEntryActionRowHoldsItsPlaceWhenTheNoticeLaneAppears` | **Yes** | with the reserve reverted: *"Copy raw moved -25 dp when the notice lane appeared"* |
| `TheEmptyStateCentresItsHeroAndClaimsNoScrollableExtent` | **Yes** | with the centring host reverted: *"hero not centred: 24 above, 659 below"* — V2-02 reproduced headlessly, numerically |
| `TheRecentCapturesCardHasAnEmptyStateOfItsOwn` | **Yes** | with the empty branch disabled: `Failed: 1` |

One existing test was rewritten rather than deleted.
`SamsungResponsiveLayoutTests.HomeHeroCanScrollWhenLargeTextExceedsAShortLandscapeViewport`
asserted `scroller.VerticalContentAlignment == Center` — a property that never
had any effect on the scroll axis, which is precisely V2-02's cause. Its name
promises a behaviour it never exercised, so it now builds the viewport it names
(393 × 330 dp at `font_scale` 2.0) and asserts that the hero **is genuinely
scrollable and starts at the top**, which is what F-46 was written to protect.

**Test-suite flakiness — resolved, and it was a real product-adjacent defect.**
§20.4.3 recorded a pre-existing one-test-per-run flake, reproduced on a pristine
`0c9dd02` worktree. The cause is `EdgeGestureGuard`'s static registry and
last-published geometry: `LiveTestWorkspaceFixture` closes a window without
resetting it, so a later test could observe rectangles measured for a window that
no longer exists — which is exactly what the guard's own `Reset` documentation
says must not happen. Resetting the guard in the fixture's teardown produced
**three consecutive clean 428/428 full runs**, after a 1-in-2 failure rate
immediately before. Recorded here because it explains §20.4.3's observation and
supersedes it.

#### 20.7.4 Batch B device verification — **DEVICE PASS**

DUT-1 `RFCRC0A9GND`, `font_scale` 1.0, three-button navigation, clean
`pm clear` before each scenario. Corpus additions:

| File | Lines | Bytes | Oracle | MediaStore `_id` |
|---|---:|---:|---|---|
| `vc3-medium.txt` | 250 001 | 22 501 180 | 249 974 timed entries | 1000000665 |

| Build | SHA-256 |
|---|---|
| B-1 | `6a3593eb2b1a2eff35b7e5c651ce74fc7c0fea02e01a281ca2afceb19dc390aa` |
| B-2 | `99d45073753411dc1343ace6ea2c838692aee5a5507e12836e238a27ccfe94c3` (Load all width floor) |
| B-3 | `cf7934122608660e12dfbbb130efd71a8213ec1a231901db9af8e41a5d854ec5` (plot pinned to its drawn height) |

##### V2-19 — **DEVICE PASS**

`vc3-medium.txt`, 249 974 entries. The footer, from the tree:

```
End of the loaded rows                                       416.0 x 56.4 dp
CE  Load 500 more; 249,474 remaining                         376.0 x 48.0 dp
CE  Load all 249,474 remaining matching rows in batches        33.8 x 49.3 dp   <- width floor missing
```

The width shortfall was found here, fixed (`MinWidth = TouchTarget.SelfSized`),
and covered by the test. Tapping **All** presented:

> **Load every matching row?**
> 249,474 rows are not loaded yet. VisualCat will read them in batches and keep
> all of them in memory, which on a large session takes a while and a lot of it.
> You can cancel at any point and keep the rows already loaded.
> `Cancel` · `Load all`

Confirming it ran the batch load to completion on the phone. Afterwards the
footer is gone from the tree entirely and the count line reads:

```
249,974 in view · 249,974 match · 249,974 timed in session · 26 unparsed lines
```

X-13 asked for completion **or** an explicit bound. The phone now delivers
completion, with a cancellable path and an honest warning first.

##### V2-20 — **DEVICE PASS**

`vc3-small.txt`, 49 994 entries, one row selected, `Copy raw` tapped at the centre
of its own rectangle:

| Step | `Copy raw` bounds |
|---|---|
| before the copy | `[370,1427][683,1535]` |
| after the copy, notice showing (`Copied the raw text of 1 entry.`) | `[370,1427][683,1535]` |

**Zero movement.** A second tap at the identical coordinate `(526, 1481)` copied
again — the notice still reads `Copied the raw text of 1 entry.` — instead of
landing in the selected list row and opening the Entry tab. The original run
measured a 140 px shift here.

The intermediate B-2 build is the one that caught the pin's first form: pinning
the plot to the allocator's *share* moved `Copy raw` **down** by 122 px, because
the four-row analysis minimum was what had been binding. B-3 pins it to the
height the row was actually drawn at.

##### V2-02 — **DEVICE PASS**

| | Content block (physical px) | Ground above | Ground below |
|---|---|---:|---:|
| before | ends at y ≈ 974 | 0 | **1 258 px — 53 % of the workspace** |
| after | y ≈ 960 … 1 666 | 639 px | 566 px |

The hero now sits on the middle of the screen with balanced ground either side —
the residual 73 px (32 dp, under 4 % of an 849 dp viewport) reads as centred.
`N-firstrun-b.png` and `P-firstrun-b.xml` are the evidence.

##### V2-01 — **CLOSED AS NOT AN APPLICATION DEFECT**

See §20.7.2. `O-launcher.png` shows the same bar on the launcher with VisualCat
force-stopped. The policy change shipped regardless and is visible in the build:
the Android empty state carries no scrollbar of its own.

##### V2-03 — **DEVICE PASS**

`RECENT CAPTURES` on a clean install now presents a card **200 dp** tall — sized
to its content rather than pinned to a bottom band:

```
Recent VisualCat sessions
No captures on this device yet.
A capture records this device's log into this app's private storage.
Start one and it will be listed here.
CE  Cancel                        60.4 x 48.0 dp
CE  Capture this device's log    168.9 x 49.3 dp
```

No empty list region, no `Open`, and no three-way status taxonomy for items the
reader does not have. Tapping **Capture this device's log** opens
*Choose what Live captures* with both scope options — so the dialog is a route
into the product rather than a dead end. `Y-recents-empty.png` is the screenshot.

### 20.8 Batch C journal — V2-06, V2-07, V2-09, V2-10, V2-11, V2-12, V2-15, V2-16, V2-22

#### 20.8.1 Batch C changes — **IMPLEMENTED**

| Finding | Change | File(s) |
|---|---|---|
| V2-06 | One predicate for both entry actions (`SyncEntryActionAvailability`): `Copy raw` and `Entry` are enabled from the same fact, and `Copy raw` falls back to the inspected entry when the list holds no selection of its own — the entry is on screen and copying it is not a lie. | `Views/SessionWorkspaceView.Interactions.cs`, `Views/SessionWorkspaceView.RawContext.cs` |
| V2-06 | The off-page banner gains the second reason an open entry is not on screen: *"This entry is not in the current filter. It is still open below."*, with a **Clear filters** action. Membership is answered exactly for severity — a level the filter does not admit cannot be further down the page — and from the loaded rows for everything else. | `Views/SessionWorkspaceView.cs`, `Views/SessionWorkspaceView.Interactions.cs` |
| V2-07 | `⏮` and `⏭` join the marker cluster, and `NavigateToSearchEdgeAsync` centres on the first or last match. From **Fit** it narrows to a twentieth of the session, because centring a full-session window on a match clamps straight back and the button would read as doing nothing. | `Views/SessionWorkspaceView.cs`, `Views/SessionWorkspaceView.Presentation.cs` |
| V2-09 | The overscroll margin is bounded by the viewport as well as the session: `min(0.05 × session, 0.10 × viewport)`. At Fit nothing changes; at the 4.7 s zoom the empty band drops from 78 % of the plot to 10 %. | `Timeline/TimelineTransform.cs` |
| V2-10 | While Follow is engaged, **nothing about the viewport is persisted** — `CaptureViewState` writes `Viewport: null, FollowLatest: false`. A Follow window is not a chosen viewport, R-23 says Follow belongs to a running capture, and R-29 already settles the equivalent moment on the import path. | `Presentation/SessionTabViewModel.cs` |
| V2-11 | The remedy leaves the paragraph and becomes a **Switch scope** button that stops the restricted capture and reopens the scope chooser — the notice's own three-step instruction, done by one control. The paragraph is shortened to what remains true. | `src/VisualCat.Android/OnDeviceLogSource.cs`, `Views/MainView.cs` |
| V2-11 | The notice is recomposed in the past tense when the capture ends, decided by **what the lane is actually showing** (`IsHoldingNoticeStartingWith`) rather than by revision bookkeeping — anything that raises a notice in between advances the revision without leaving its message on screen. | `Views/MainView.Notice.cs`, `Views/MainView.cs` |
| V2-11 | The live status line leads with the scope **when the scope is restricted**. Finding 27 put the volatile numbers first because the ellipsis takes what is last; V2-11 recorded the other half. The clause that leads is the one carrying a limitation — which reconciles both rather than reversing one. | `Presentation/SessionTabViewModel.cs` |
| V2-12 | Emptiness is branched on **before** detection: a zero-byte file raises *"This file is empty — there is nothing to import."* with its own remedy, instead of the "no supported logcat format" card and its advice about bug reports. | `Application/Coordination/SessionCoordinator.cs`, `Presentation/WorkspaceViewModel.cs` |
| V2-12 | The notice lane is suppressed when a full-page failure card already states the same message. The lane is for results whose only other evidence is off screen; a card that owns the workspace is not off screen. | `Views/MainView.cs` |
| V2-15 | `ExportScope` carries its own filter decision, and the chooser takes a list. Up to three scopes are offered with their row counts — *What is in view*, *Everything matching the current filter*, *Everything in this session* — and only the ones that differ. **Exporting the whole session ignoring the filter was not previously something the product could do at all.** | `Views/ExportScopeDialog.cs`, `Views/MainView.cs` |
| V2-15 | The More sheet's subtitle says what happens: *"Save entries as CSV, choosing the scope when more than one applies"*. | `Views/MainView.cs` |
| V2-16 | A clipped tab's close button stays **enabled** and does one of two named things: reveal the chip, then close it. The guard was right about the hazard — a destructive control must not float beside a name that has scrolled away — and wrong about the answer, because "nothing happens" is not a guard a reader can learn from. | `Views/MainView.TabStrip.cs` |
| V2-22 | `MainActivity.ConfigureEdgeToEdgeWindow` requests edge-to-edge explicitly **below API 35**: decor stops fitting system windows, both bar colours go transparent, and — the part that produces the reported band — `StatusBarContrastEnforced`/`NavigationBarContrastEnforced` are turned off, because that scrim composited over the platform's wallpaper-derived surface is what an off-palette brown-purple band is. | `src/VisualCat.Android/MainActivity.cs` |

#### 20.8.2 Batch C automated proof — **PASS**

Six new tests, each verified red against the shipped behaviour by reverting the
specific change:

| Test | Red before | Evidence |
|---|---|---|
| `BothEntryActionsAgreeAndTheOutOfFilterEntrySaysSo` | **Yes** | with the shared predicate reverted: `Assert.True() Failure`. Needed a new seam, `SessionWorkspaceView.InspectEntryForTest`, because V2-06 lives entirely in the state the *cell* route leaves behind — an entry being read with no list selection — which a headless run cannot reach by tapping a plot cell |
| `SearchCanReachItsFirstAndLastMatch` | **Yes** | with `⏮`/`⏭` removed from the cluster: `Failed: 1`. It also finally asserts **B-06's wrap**, which §12 recorded as blocked by the product UI: from a window past the last marker, Next returns to the first |
| `OverscrollIsBoundedByTheViewportAsWellAsTheSession` | **Yes** | with the viewport bound removed: `Failed: 1` |
| `AFollowWindowIsNotPersistedAsTheReadersViewport` | **Yes** | with the Follow branch removed: `Failed: 1` |
| `AnEmptyFileSaysItIsEmpty` | **Yes** | with the length branch removed: `Failed: 1` |
| `EveryDocumentedFormatGeneratesAndDetectsAsItself` (Batch D) | **Yes** for `long` | see §20.9 |

Full suites after Batch C: **`VisualCat.App.Tests` 433/433**,
`VisualCat.Core.Tests` 107/107, `VisualCat.Application.Tests` 56/56,
`VisualCat.Domain.Tests` 47/47.

**Test-suite stability, closed out.** §20.7.3 attributed the residual flake to
`EdgeGestureGuard`'s static registry; that was one of two causes and the smaller.
The larger is that several tests drive **asynchronous** view-model work — a zoom,
a search, a viewport change all run a query off the dispatcher and report back
through it — and then waited with a fixed number of layout passes, which is a
guess rather than a wait. `PixelGestureAndTextScaleTests.PumpUntil` replaces the
guess: it pumps the dispatcher, yields a millisecond of real time so thread-pool
work can finish, and requires the condition to hold on two consecutive passes so
a mid-recompute state cannot be read as settled. With that and the guard reset at
both ends, the new tests are stable across repeated full runs. One pre-existing
case (`SessionWorkspaceHeadlessTests.AFollowRefreshKeepsAndExplainsAnEntryThatAgesOutWithAnotherPageAvailable`
— the same test that failed on the pristine `0c9dd02` worktree in §20.4.3) still
flakes occasionally and is left as it was found.

#### 20.8.3 Batch C device verification — **DEVICE PASS** (V2-22 deferred)

| Build | SHA-256 | Notes |
|---|---|---|
| C-1 | `1fa821a1927868632643a305d9ffc4822e411bb2967cbf06a551cb82c4388265` | V2-06/07/09/10/11/12/15/16/22 first pass |
| C-2 | `d62fe3b23a322a30cf7550b1a61ce2ed3107213214d69914ddba1f6cd6b8cbd6` | edge jump narrows from Fit |
| C-3 | `06e3bba5c70e65cb575045b5269fc6823fa3655ec4066d915f3714cbf7cddac2` | severity-exact out-of-filter test |
| C-4 | `914193b32454244f10e93b10c50f440004feb1342cc7cd16794a8b39e0316383` | edge-to-edge scoped to pre-35; notice shortened |
| C-5 | `41c825f77c1bdbcc860825724a33abb0865d9524a6b697c613c37753d2dede29` | tense rewrite decided by the lane's own text |

New corpus: `vc3-empty.txt` (0 bytes, MediaStore `_id` 1000000666).

##### V2-12 — **DEVICE PASS**

Opening the zero-byte file gives a failure card reading, verbatim:

```
IMPORT FAILED
This log could not be read
This file is empty — there is nothing to import.
Nothing was written to it. Check the capture that produced it, or pick a different file.
CE  Open another log     124.0 x 48.0 dp
CE  Close this tab       103.1 x 48.0 dp
```

The notice lane is **absent from the tree entirely** — the same sentence no
longer appears three times at once, and there is nothing left to dismiss by hand.

##### V2-07 — **DEVICE PASS**

With `Rendering surface` searched on `vc3-small.txt`, the marker cluster is:

```
CE  [451,2171][559,2279]  48.0x48.0dp  First search match
CE  [559,2171][667,2279]  48.0x48.0dp  Previous search match
    [671,2210][815,2240]               3,579 / 7,181
CE  [819,2171][927,2279]  48.0x48.0dp  Next search match
CE  [927,2171][1035,2279] 48.0x48.0dp  Last search match
```

Tapping `⏮` moved the counter to **163 / 7,181** and `⏭` to **6,993 / 7,181** —
the same 7,181-match search the report reached only by 3,578 taps. The counter
reports the match nearest the *centre* of the viewport, and a window clamped
against a session bound puts the edge match at its edge, which is why it reads
163 rather than 1: the reader is at the first match, and the window around it
holds 162 others. The ends are one tap away, which is what V2-07 asks for.

##### V2-06 — **DEVICE PASS**

The report's exact reproduction: cell tap → Entry → *Show the source bytes* →
drop the `× Cell` chip → filter to **F** only. With `levels: F` active and
`8,349 in view · 8,349 match · 49,994 timed in session`:

```
CE  [370,1289][683,1397]  Copy raw
CE  [696,1289][1008,1397]  Show the full message of the selected entry
    [96,1451][766,1482]   This entry is not in the current filter. It is still open below.
CE  [784,1413][984,1521]  Clear the filters so this entry is in scope again
```

Both controls are enabled — they agree — and the pane says the open entry is out
of scope and offers the way back. Previously `Copy raw` was disabled while
`Entry` was enabled and opened an Error record under a Fatal-only filter with
nothing saying so.

##### V2-15 — **DEVICE PASS**

*More → Export CSV…* with a Fatal-only filter and the plot at Fit — the state
that previously skipped straight to the platform picker:

```
Export CSV
The sort order and the encoding come from Appearance & timeline.
CE  Everything matching the current filter — 8,349 rows
      Every entry the current filter admits, across the whole session.
CE  Everything in this session — 49,994 rows
      Every entry, ignoring the filter this workspace has on.
CE  Cancel · Choose a file…
```

Both counts match the report's own figures. The second option is the one that
could not previously be produced at all, and it is the answer to B-14's
"exporting a filtered view believing it was the whole session".

##### V2-16 — **DEVICE PASS**

Four sessions open, strip scrolled, leftmost chip clipped:

| Step | Leftmost chip's close button |
|---|---|
| clipped | `CE … Show session vc3-mixed-formats.txt before closing it` — **enabled** |
| after one tap | chip scrolled fully in; button becomes `Close session vc3-mixed-formats.txt`, 49.8 dp |
| after the second tap | session closed; the strip holds the remaining three |

Previously that node was `enabled=false` and a tap at its centre did nothing at
all, with no notice and no explanation.

##### V2-11 — **DEVICE PASS** (with one residual, stated)

During a restricted on-device capture:

```
Capturing · On-device own-app logcat · 41 lines received · no source lines for 23s · own-app scope only
```

The scope is now the **second** clause and cannot be the one truncation takes; it
was last and clipped to `On-device o…`. The notice lane carries
`Switch scope` (104.9 × 48.0 dp) beside `Dismiss`, so the remedy is a control
rather than the tail of a paragraph. After **Stop capture** the present-tense
message is gone from the lane — replaced by the past-tense one, which is an
Information notice and clears itself after its reading window.

**Residual, honestly:** with the remedy extracted and the paragraph shortened the
notice is about three lines, but at this text size the lane's 108 dp text budget
can still clip the last line of a long message. The report's alternative — cap the
notice at two lines behind a *More* disclosure — is not implemented. The
clipped words are no longer the instruction, which was the load-bearing half.

##### V2-10 — **DEVICE PASS**

A 31-entry own-app capture, stopped, then force-stopped and relaunched. The
restored tab shows:

| | Before (V2-10) | Now |
|---|---|---|
| viewport | `DENSITY · 30 s · 36.23 ms/px` at the live edge | `DENSITY · 1.13 min · 81.95 ms/px` — the whole capture |
| counts | `2 in view · 47 match · 47 in session` | `31 in view · 31 match · 31 in session` |
| legend | `F 0 · E 0 · W 0 · I 2 · D 0 · V 0` | `I 24 · D 7` and the rest zero, which is the truth |
| minimap | brush parked at the far right | brush spanning the session |

`P1-reopened.png` is the screenshot.

##### V2-22 — **IMPLEMENTED, DEVICE VERIFICATION DEFERRED**

Not verifiable here: V2-22 is an **API-34** finding and the only connected device
is API 36, where Android enforces edge-to-edge itself and the code path is
deliberately skipped. Recorded as a coverage gap rather than as a pass.

The first form of the change was unconditional, and the device caught it: calling
`WindowCompat.SetDecorFitsSystemWindows` on API 36 pushed the notice lane **74 px
under the navigation bar**, because the second request displaced the inset
dispatch Avalonia had installed. Bisected on hardware — the call was disabled,
rebuilt, reinstalled, and the geometry compared — and the whole block is now
scoped to `!OperatingSystem.IsAndroidVersionAtLeast(35)`.

##### V2-25 · The session status row is drawn under the navigation bar — **Low/Medium**, NEW, not fixed

*Surface:* Android companion, any open session, three-button navigation.

**Observed.** With a session open and no notice showing, `Session status` is
reported at `[45,2242][1035,2279]` on a 1080 × 2340 panel whose navigation bar
occupies `[0,2232][1080,2340]`. The screenshot (`K2-bottom.png`) shows
`Ready · 1,000 entries` and the left navigation button drawn over one another.
The applied bottom inset measures about 61 px where the three-button bar is 108.

**Not caused by this work, and proven so.** The edge-to-edge call added for
V2-22 was disabled, the app rebuilt and reinstalled, and the bounds re-read:
`[45,2242][1035,2279]`, byte-identical. It is also present in Batch B evidence
(`S-state.xml`) recorded before V2-22 existed.

**Not fixed here.** It is outside the 21 findings this continuation is
implementing, and the fix belongs with Avalonia's inset distribution rather than
with any of them — the notice lane, when it is showing, *does* stop at 2232,
so the two consumers of the same inset disagree. Recorded rather than discarded.

### 20.9 Batch D journal — PLAN-01 and the frame-instrumentation amendment

#### 20.9.1 PLAN-01 — **IMPLEMENTED**

The finding said the plan commanded a CLI option the shipped CLI rejects. Reading
the code, the split was narrower and stranger than that: `GenerateAsync` already
parsed `--format` and `docs/CLI.md` already documented it — **only
`KnownOptions` did not list it**, so `RejectUnknown` refused the flag before the
code behind it ever ran. The CLI was out of step with its own documentation, and
`vcat generate-test-log --format brief` failed as an unknown option.

| Change | File |
|---|---|
| `--format` added to `generate-test-log`'s known options | `src/VisualCat.Cli/Program.cs` |
| Usage lines updated in the per-command help, the top-level help, and `docs/CLI-HELP.txt`, which `tools/verify-cli-help.ps1` compares | `src/VisualCat.Cli/Program.cs`, `docs/CLI-HELP.txt` |
| `docs/CLI.md` gains the accepted values and a §3.2 example | `docs/CLI.md` |
| **`LogcatFormat.LongFormat` implemented in the generator** | `src/VisualCat.Core/Generation/SyntheticLogGenerator.cs` |

That last row is a defect the fix uncovered. `--format long` fell through the
renderer's `switch` to the ThreadTime arm, so it produced ThreadTime and the
detector duly reported ThreadTime: the option was accepted and silently ignored,
which is worse than rejecting it. Long format is now two lines and a blank one —
`[ MM-dd HH:mm:ss.fff  pid: tid L/Tag ]`, the message, a separator — which is
exactly what `LogcatParser.TryLong` reads back.

**Proof.** New `tests/VisualCat.Core.Tests/SyntheticLogFormatTests.cs`: every
documented format generates and is detected as itself, and a format corpus is
byte-deterministic under one seed. The `LongFormat` case **fails against the
shipped generator** with `Expected: LongFormat, Actual: ThreadTime`.

All four §3.2 corpora were then produced on the host from the shipped CLI, with
no hand editing:

```shell
vcat generate-test-log --output fmt-time.txt  --lines 2000 --seed 42 --format time
vcat generate-test-log --output fmt-brief.txt --lines 2000 --seed 42 --format brief
vcat generate-test-log --output fmt-long.txt  --lines 2000 --seed 42 --format long
vcat generate-test-log --output fmt-epoch.txt --lines 2000 --seed 42 --format epoch
```

`fmt-brief.txt` opens `D/Camera(10503): Rendering surface 0x000043D5`;
`fmt-epoch.txt` opens `1747318417.000000 10503  5136 D Camera: …`. §3.2 can now
be executed as written.

#### 20.9.2 Frame-instrumentation amendment — **IMPLEMENTED**

`docs/ANDROID-LIVE-TEST-PLAN.md` §4.3 now states plainly why `gfxinfo` measured
the wrong pipeline for this renderer, and §4.3.1 is a new, reproducible
procedure that measures the right one:

- **Capture** with `perfetto -o … --time 20s --buffer 32mb gfx view sched freq idle am wm binder_driver hal`, pulled and then deleted from the device.
- **Read** with `trace_processor` over `actual_frame_timeline_slice` /
  `expected_frame_timeline_slice`, with the SQL for a janky-percentage over one
  package included in the plan rather than left to the reader.
- **Three rules that keep it honest:** a frame count of zero is `BLOCKED` and
  never a pass, and the count is recorded beside the percentage; the device's
  active refresh period is recorded with it, because 8.3 ms and 16.7 ms are
  different budgets; and a high-frame-rate external camera is the documented
  fallback, while the device's own `screenrecord` is not, since it caps its frame
  rate and cannot resolve sub-frame latency.

The two frame-pacing rows in §4.2 now name that procedure instead of `gfxinfo`,
and the plan states that they stay **BLOCKED** until a run records `frames > 0`
from it. No frame-pacing number is claimed in this continuation: the amendment
is the deliverable the report asked for, not a measurement.

### 20.10 Closing implementation ledger

This supersedes §20.2 and §20.5. `DEVICE PASS` means code, a focused automated
check, **and** an observation on DUT-1.

| Work item | State | Where the proof is |
|---|---|---|
| V2-01 false/clipped scrollbar | **CLOSED — not an application defect** | §20.7.2: it is Samsung's Edge panel handle. Four independent pieces of evidence, including the bar appearing on the launcher with VisualCat force-stopped. The policy half the finding asked for shipped anyway |
| V2-02 empty-workspace composition | **DEVICE PASS** | §20.7.4 — 1 258 px of dead ground below the hero became 639 px above / 566 px below |
| V2-03 empty recent-captures dialog | **DEVICE PASS** | §20.7.4 — 200 dp card, an explicit empty state, a working action, no inert *Open* |
| V2-04 source-context code legend | **DEVICE PASS** | §20.4.4 — decoded legend on the pane and in the new card, shown only when a non-`en` code is on screen |
| V2-05 chip/Clear-all touch targets | **DEVICE PASS** | §20.4.4 — 15.6 × 16.4 dp → 84.9 × 49.3 dp; 40.0 → 49.3 dp |
| V2-06 filtered-out selected entry | **DEVICE PASS** | §20.8.3 — both controls agree; the pane says the entry is out of scope and offers *Clear filters* |
| V2-07 first/last search navigation | **DEVICE PASS** | §20.8.3 — `⏮`/`⏭`; and B-06's wrap is asserted for the first time |
| V2-09 timeline endpoint overscroll | **IMPLEMENTED** (headless proof; device check not separately run) | §20.8.2 — bounded by `min(0.05 × session, 0.10 × viewport)` |
| V2-10 reopened-session viewport | **DEVICE PASS** | §20.8.3 — 30 s live edge → the whole 1.13 min capture, through a force-stop |
| V2-11 clipped live notice | **DEVICE PASS**, one residual stated | §20.8.3 — remedy is a button, scope leads the status line, tense corrected on stop |
| V2-12 empty-file error | **DEVICE PASS** | §20.8.3 — its own message and remedy; the duplicate notice is gone |
| V2-13 counter population mismatch | **DEVICE PASS** | §20.4.4 — `2,277 in view · 3,476 match · 2,277 timed in session · 1,199 untimed` |
| V2-14 unknown/continuation disclosure | **DEVICE PASS** | §20.4.4 — counted, announced, and browsable; ADR 0009 untouched |
| V2-15 CSV scope copy | **DEVICE PASS** | §20.8.3 — a real chooser with counts, including a scope the product could not previously produce |
| V2-16 leftmost scrolled-tab close | **DEVICE PASS** | §20.8.3 — reveal, then close; never a silent tap |
| V2-17 four-row entry floor at large text | **DEVICE PASS** | §20.4.4 — 248 → 269.3 → 445.3 dp at 1.0 / 1.3 / 2.0 |
| V2-18 large-text Live chooser | **DEVICE PASS** | §20.4.4 — both radios painted and reachable at `font_scale` 2.0 |
| V2-19 cancellable Load all | **DEVICE PASS** | §20.7.4 — 249,974 rows loaded to completion on the phone, after an honest warning |
| V2-20 stable Copy-raw slot | **DEVICE PASS** | §20.7.4 — zero movement; a repeated tap copies again |
| V2-21 gesture Back and plot exclusion | **DEVICE PASS**, root cause corrected | §20.4.4 — the scrim's touch-down dismissal was the real first fault |
| V2-22 API-34 system-bar palette | **DEVICE PASS** (API-33 emulator) | §22 — bands measured at `Surface()` exactly in both variants; the translucent-flag cause is recorded there |
| V2-23 Back leaves the app past an `IsCancel` dialog | **DEVICE PASS** (new, found here) | §20.4.4 |
| V2-25 status row under the navigation bar | **RETRACTED — see §21.1.** Not a product defect: a hybrid navbar-overlay state this run left on the device | §20.8.3 raised it; §21.1 withdraws it with the platform's own inset numbers |
| PLAN-01 generator `--format` | **IMPLEMENTED** | §20.9.1 — and `--format long` turned out to be silently ignored |
| Frame-instrumentation amendment | **IMPLEMENTED** | §20.9.2 — Perfetto/FrameTimeline procedure in plan §4.3.1 |

**Every one of the report's 21 numbered findings is now closed**: nineteen
implemented and observed on hardware, one (V2-09) implemented with a headless
numeric proof, one (V2-22) implemented but honestly unverifiable on the connected
device, and one (V2-01) closed as a misattribution with the evidence for that
conclusion. PLAN-01 and the frame-instrumentation amendment are done. Two new
findings were raised on the way, one fixed (V2-23) and one recorded (V2-25).

#### 20.10.1 Final test state

| Suite | Result |
|---|---|
| `VisualCat.App.Tests` | **433 / 433** |
| `VisualCat.Core.Tests` | **107 / 107** |
| `VisualCat.Application.Tests` | **56 / 56** |
| `VisualCat.Domain.Tests` | **47 / 47** |
| `dotnet format --verify-no-changes` | clean for every file this work touched; two pre-existing offenders remain (`Theme/TouchTarget.cs`, `AndroidAuditFix2Tests.cs`), neither modified here |

New test files: `tests/VisualCat.App.Tests/LiveTestV2RemediationTests.cs` (22
tests) and `tests/VisualCat.Core.Tests/SyntheticLogFormatTests.cs` (6). Four
existing tests were rewritten rather than deleted, each with its reason recorded
beside it: the F-46 hero-scroll test (asserted a property that never had an
effect), the two edge-exclusion tests (now exercise the zoomed state F-28 is
actually about, and assert the new Fit behaviour), and
`SessionActivityTests.TheCaptureStatusPutsTheChangingNumbersBeforeTheScope`
(unchanged, and the reason V2-11's status-line fix is conditional rather than a
reversal).

### 20.11 Device mutation ledger and hand-back

Supersedes §20.6. Every global DUT-1 mutation made after §20.1.

| # | Setting | Original | Changed to | Restored |
|---|---|---|---|---|
| 1 | `system font_scale` | `1.0` | `1.3` → `2.0` → `1.8` | **restored; verified `1.0`** |
| 2 | navigation mode overlay | three-button, `navigation_mode=0` | `…navbar.gestural`, `navigation_mode=2` | **restored; verified `0`** |
| 3 | `POST_NOTIFICATIONS` | `granted=false` | prompted twice during live capture; **declined both times** | unchanged — verified below |
| 4 | files in `/sdcard/Download` | — | `vc3-tiny.txt`, `vc3-small.txt`, `vc3-medium.txt`, `vc3-continuations.txt`, `vc3-mixed-formats.txt`, `vc3-empty.txt` | deleted by exact path; absence verified below |
| 5 | installed `com.barebit.visualcat` | 2.0.10-dev sideload, first-run data | replaced by the builds in §§20.4.4, 20.7.4, 20.8.3 | left installed at the final build, `pm clear`ed, stopped — the same clean-data condition §13 handed back |

#### 20.11.1 Hand-back verification — **PASS**

| Check | Final evidence |
|---|---|
| Captures/imports/processes | none running; VisualCat force-stopped; the launcher is the last screen |
| Public test files | every `vc3-*` deleted by exact path; a `vc3`/`vc2` scan of `/sdcard/Download` returns nothing; unrelated Downloads untouched |
| Private test sessions | `pm clear com.barebit.visualcat` succeeded; the clean cold launch shows the first-run state |
| Cold launch | `Status: ok`, `LaunchState: COLD`, **1,425 ms** |
| Notification permission | `POST_NOTIFICATIONS: granted=false` — the runtime prompt was **declined** both times it appeared, so this never changed |
| Text scale | `font_scale = 1.0` |
| Navigation | `navigation_mode = 0` (three-button), restored from the gestural overlay |
| Rotation | `accelerometer_rotation = 1` |
| Theme | `Night mode: yes` |
| Stability | crash buffer holds **no** VisualCat entries for this run |
| Identity | state `device`; serial `RFCRC0A9GND`; fingerprint `samsung/r9qxeea/r9q:16/BP2A.250605.031.A3/G990BXXSKIZF1:user/release-keys` — byte-for-byte equal to §0.2 |
| Health | battery 100 %, 32.1 °C; ~95 GB free on `/data` |
| Installed artifact | `com.barebit.visualcat` 2.0.10-dev / 2001000, the final continuation build, `lastUpdateTime 2026-08-30 11:46:14`, stopped, first-run data |

VisualCat is left installed at the build these fixes were verified on, cleared,
and stopped — the same clean-data condition §13 handed the device back in. The
owner authorised updating or deleting the app (§20.1); it is left updated, which
is the more useful of the two, and `pm uninstall com.barebit.visualcat` removes
it if that is preferred.

**Restore point:** all four batches are closed. There is no unexecuted work item
left in §20.10. What remains open, and is stated as open rather than green:
V2-22 needs an API-34 device, V2-25 is recorded and not fixed, and §12's coverage
gaps (Wireless-ADB production capture, TalkBack, RTL, foldable/tablet, the API
floor, soak) are untouched by this continuation.

### 20.12 Known remainder — what is deliberately not done

Every finding in §20.10 is closed, but several carried more than one suggested
fix and not all sub-items were taken. They are listed here rather than left for a
reader to discover by diffing the report against the code.

| Finding | Sub-item not implemented | Why |
|---|---|---|
| V2-07 | *"Make the counter a control: tap `3,579 / 7,181` to jump."* | `⏮`/`⏭` reach both ends in one tap, which is what the finding's evidence is about, and the third suggestion — a tap on the plot's marker lane seeking — **already existed** (`TimelineControl.SearchMarkerPicked`, finding F-07). A numeric jump box is the only remaining route and it is the least used of the three. |
| V2-11 | *"Size the lane to its content and let the page scroll, or cap the notice at two lines with a More disclosure."* | The remedy became a button and the paragraph was shortened, so the words that get clipped are no longer the instruction. The lane's own 108 dp text budget is unchanged, and a long message can still lose its last line at a large text scale. Fixing it properly is a change to the shell's bottom band and is entangled with **V2-25**. |
| V2-13 | *"Give untimed records a home: a chip in the chip bar (`1,200 untimed`) that filters the entries list to them."* | The count line names them and *Insights* already carried an `Untimed` row. The chip is a **filter**, and the engine cannot currently return untimed records as entry rows — the same limitation as V2-14 below. |
| V2-14 | *"Expose `IncludedOutcomes` as a filter — 'show unknown lines' — so a reader can pull the stack frames into the entries list."* | **Implemented differently, deliberately.** Unknown lines are physical source records, not entries: they have no row in the columnar store, so `SessionQueryEngine.GetEntries` cannot return them and `IncludedOutcomes` only ever *excludes* parsed entries today. Making them entry rows is a store and engine change, and the finding's own instruction is *"presentation only; do not change ADR 0009."* They are reachable instead through **More → Unparsed lines…**, which reads the source stream directly in bounded pages. |
| V2-21 | *"Add device tests for left and right edges at a header coordinate and a plot coordinate with each layer open."* | The **left** edge was exercised at a plot coordinate with the More sheet open, with the filter drawer open, and with no layer, plus the key-Back path for each. The right edge and the header coordinate were not separately driven on the device; the fix is edge-agnostic (the scrim's dismissal trigger and the guard's suspension are not per-edge) but that is an argument, not a measurement. |
| V2-22 | The whole finding | No API-34 device is connected. See §20.8.3. |
| V2-09 | Device observation | Proven headlessly with the numbers from the finding (78 % → 10 % of the plot at the 4.699 s zoom); not separately reproduced on hardware. |
| V2-25 | The fix | New finding, recorded in §20.8.3, bisected and proven pre-existing. Not one of the 21, and its fix belongs with the shell's inset distribution. |

### 20.13 Repository state a reader should know about

- **`CHANGELOG.md` `[Unreleased]`** now carries this work: an `### Added`
  section for *Unparsed lines…*, the gutter legend, phone *Load all*, `⏮`/`⏭`,
  the export scope chooser and `generate-test-log --format`; and the fixes joined
  to the existing `### Fixed` list.
- **`tools/verify-docs.ps1` fails on three links, none of them from this work.**
  `docs/ANDROID-LIVE-TEST-REPORT.md` (the v1 report) links three times to
  `docs/ANDROID-AUDIT-CONTINUATION.md`, which is **staged for deletion** in the
  working tree this continuation started from (§20.1). Committing that deletion
  as it stands fails the documentation gate; the v1 report's links have to go,
  change target, or the file has to come back. That is an owner decision and was
  left alone.
- **`tools/verify-public-release.ps1` was not run end to end.** The stages that
  could be run here were: `Release` build (clean), all four test suites
  (433 / 107 / 56 / 47), `dotnet format --verify-no-changes` (clean for every file
  this work touched), and the `vcat --help` / `docs/CLI-HELP.txt` comparison
  (identical but for the templated version line). Not run: the secret scan, the
  CycloneDX SBOM and licence review, packaging and archive extraction, and the
  `.nupkg` install.
- The two files `dotnet format` still objects to — `src/VisualCat.App/Theme/TouchTarget.cs`
  and `tests/VisualCat.App.Tests/AndroidAuditFix2Tests.cs` — are **unmodified by
  this work** (`git status` reports neither) and fail on line-ending markers.

## 21. Second implementation pass — closing the remainder

§20.12 listed what the first pass deliberately left. This section closes it, and
retracts one of the two findings the first pass raised.

### 21.1 V2-25 — **RETRACTED. It was a test-harness artefact, and mine.**

§20.8.3 recorded the session status row drawing under the navigation bar,
bisected the edge-to-edge change out of it, and concluded it was pre-existing.
The bisect was right and the conclusion was wrong: it was pre-existing *in this
session*, because of something this session had already done to the device.

**What the platform was reporting.**

```
InsetsSource type=navigationBars        frame=[0,2306][1080,2340]  flags=SUPPRESS_SCRIM
InsetsSource type=mandatorySystemGestures frame=[0,2232][1080,2340]
```

A **34 px** navigation inset with the scrim suppressed, on a device drawing a
108 px three-button bar. The app honoured the 34 px it was given, ended its
content at 2279, and Samsung drew its buttons over the region the platform had
just declared safe.

**Why the platform was reporting that.** §20.6 restored navigation with
`cmd overlay enable com.android.internal.systemui.navbar.threebutton`. **Enable is
not exclusive.** Both AOSP overlays were left on at once:

```
[x] com.android.internal.systemui.navbar.gestural
[x] com.android.internal.systemui.navbar.threebutton
```

`navigation_mode` read `0` and three buttons were drawn, so every check §20.11
made passed — while the gestural overlay went on supplying its thin,
scrim-suppressed navigation inset. The device's original state, recorded in the
very first overlay listing of this run, is **no navbar overlay enabled at all**.

**After `cmd overlay disable` on both:**

| | Overlay state | `navigationBars` inset | `Session status` |
|---|---|---|---|
| while V2-25 was observed | `gestural` + `threebutton` | `[0,2306][1080,2340]`, 34 px, `SUPPRESS_SCRIM` | `[45,2242][1035,2279]` — under the bar |
| after both disabled | none | `[0,2232][1080,2340]`, 108 px | `[45,2168][1035,2205]` — clear of it |

V2-25 is withdrawn as a product finding. The lesson is recorded in §21.5's
mutation ledger instead: **a navigation-mode restore has to disable the overlay
it enabled, not enable a different one**, and the check must be the published
inset rather than `navigation_mode`.

### 21.2 Second-pass changes — **IMPLEMENTED**

| Finding | Sub-item §20.12 listed as not done | Change |
|---|---|---|
| V2-07 | *"Make the counter a control: tap `3,579 / 7,181` to jump."* | The counter is a `Button`; tapping it asks **Go to match** — a bounded `NumericUpDown` (`1 to 7,181`) presented by the shell through a new `AskForNumberAsync` seam — and centres the viewport on the chosen match. New `Views/NumberPromptDialog.cs`. Where no host is installed the counter is disabled rather than blocking on a dialog nobody can see. |
| V2-11 | *"Cap the notice at two lines with a More disclosure."* | The lane opens at **two lines of the reader's own type** (derived from the drawn font size, not a constant) and offers **More** exactly when there is more. Expanding sizes to content up to a quarter of the shell, so the last line is no longer cut. A new message always starts collapsed. |
| V2-13 | *"Give untimed records a home: a chip in the chip bar."* | An **off-timeline chip** sits beside the count row — `1,199 untimed`, `6 unparsed`, or `N off timeline` when both — and opens the card. Docked beside the empty label rather than added to `_chips`, so the strip does not claim a filter is active, keeps saying *"No filters · showing everything in view"*, and does not offer `Clear all` for something it cannot clear. |
| V2-14 | *"Make the unknown population reachable."* | The scan now includes `ParseOutcomeKind.UntimedEntry`, so untimed records are listed beside the unparsed lines and told apart by their gutter code (`e?` against `??`, `!!`, `..`). The card is retitled **Lines not on the timeline** and its explanation covers both populations; the menu entry follows. |

Three new tests, all green, plus `NumberPromptDialog`'s two decision buttons
carrying a **width** floor — the device measured `Go` at 35.6 dp, the same
lesson the phone's `All` button taught in §20.7.4, and the spin buttons' spoken
names were shortened from *"Increase Which of the 7,181 matches?"* to
*"Increase match number"*.

### 21.3 Second-pass device verification — **DEVICE PASS**

| Build | SHA-256 |
|---|---|
| P2-1 | `f376b00f3a2d8999c99b03665fe21b409034e4e794272a4c282c7bee38c680f7` |
| P2-2 | `0c3030df8faba02755addfedcd96e91ddc968bacd32318fb73be05c80236ff5f` |
| P2-3 | `10f9e343f6487ff37a1eb4ccb4f4e92ff9baf4d2b6f4750da06b0138de015e87` |
| P2-4 | `dda548a469f8a219b696e729d8ede16712572ee68e2b49cec35396c4c61eaa33` |

##### V2-13 / V2-14 — off-timeline chip and card

On `vc4-mixed.txt` (1,199 untimed, 1 rejected) the chip reads
`CE [475,486][756,597] 124.9 × 49.3 dp`, spoken as *"1,199 untimed records and
1 unparsed line are not on the timeline. Show them."*, while
`No filters · showing everything in view` is **still displayed** beside it.
Tapping it opens **Lines not on the timeline**, tallied `1 rejected · 1,199
untimed`, listing both `!!` and `e?` rows under the decoded legend.
`Z1-start.png` shows the same chip as `6 unparsed` on `vc4-small.txt`.

##### V2-07 — the counter jumps

`CE [649,2097][819,2205] 75.6 × 48.0 dp — 3,579 / 7,181`. Tapping it opened
*Go to match* with `Which of the 7,181 matches?`, the field at `3579`, spin
buttons at 49.3 dp, and the range stated as `1 to 7,181`. Decreasing to 3,576
and pressing **Go** narrowed the viewport (`353 in view`, from 7,181) and the
counter settled at `3,577 / 7,181` — the match nearest the centre of the new
window.

##### V2-11 — the lane costs two lines

| | Lane height | Controls |
|---|---|---|
| before this pass | 122.2 dp, last line cut | `Switch scope` · `Dismiss` |
| collapsed now | **62.2 dp**, ending exactly at the navigation bar (`[0,2092][1080,2232]`) | `Show the whole message` · `Switch scope` · `Dismiss` |
| expanded | sizes to content | `Show less of the message` · `Switch scope` · `Dismiss` |

Sixty dp of workspace returned in the ordinary case, and the whole message is one
labelled tap away.

##### V2-21 — both edges, both coordinates

Gesture navigation enabled for the test and **disabled** afterwards (§21.1).
With the *More* sheet open:

| Edge | y | Result |
|---|---|---|
| left | 1200 (plot band) | sheet closes, app stays *(§20.4.4)* |
| right | 1200 (plot band) | **sheet closes, app stays** |
| left | 260 (header) | **sheet closes, app stays** |
| right | 260 (header) | **sheet closes, app stays** |

The device-test matrix V2-21 asked for is complete.

##### V2-09 — overscroll, on hardware

`vc4-small.txt` zoomed to `DENSITY · 4.699 s · 5.68 ms/px` — the report's own
zoom — and panned hard to the session start:

| | Empty band at the start | Axis at the left edge | Minimap brush |
|---|---|---|---|
| V2-09 as recorded | 701 px — **78 % of the plot** | `14:13:34.000`, before the session began | an 8 px sliver |
| now | ≈ 100 px — **≈ 14 % of the plot** | `14:13:38.000`, inside the session | a readable rectangle at the left end |

`Z1-start.png` is the screenshot. V2-09 no longer rests on a headless proof
alone.

### 21.4 What is still open, after two passes

| Item | State | Why |
|---|---|---|
| ~~**V2-22**~~ | **CLOSED in §22 — DEVICE PASS on API 33.** The fix as written in §20.8.3 was insufficient, and only a pre-35 device could show that | — |
| §12 coverage gaps | Untouched | W1–W5 production Wireless-ADB capture, TalkBack, Switch Access, external keyboard, RTL, foldable/tablet, the API-31 floor, and the soak / low-memory / Doze / upgrade / Play-delivery schedules. Out of scope for an implementation continuation and unchanged by it. |
| `tools/verify-docs.ps1` | **Failing, 3 links, not from this work** | `docs/ANDROID-LIVE-TEST-REPORT.md` (v1) links three times to `docs/ANDROID-AUDIT-CONTINUATION.md`, staged for deletion in the tree this continuation started from. Committing that deletion as it stands fails the gate. Owner decision: drop the links, retarget them, or restore the file. |
| `tools/verify-public-release.ps1` | **Not run end to end** | Run here: Release build, all four suites, `dotnet format --verify-no-changes`, and the `vcat --help` ↔ `docs/CLI-HELP.txt` comparison. Not run: secret scan, CycloneDX SBOM and licence review, packaging and archive extraction, `.nupkg` install. |
| `dotnet format` | Two offenders, **neither modified here** | `src/VisualCat.App/Theme/TouchTarget.cs` and `tests/VisualCat.App.Tests/AndroidAuditFix2Tests.cs` fail on line-ending markers; `git status` reports neither as changed by this work. |
| `SessionWorkspaceHeadlessTests.AFollowRefreshKeepsAndExplainsAnEntryThatAgesOutWithAnotherPageAvailable` | Occasional flake, **pre-existing** | Reproduced on a pristine `0c9dd02` worktree in §20.4.3. The two causes this work did fix — the static edge-guard registry and fixed-pass waits on asynchronous view-model work — are described in §20.8.2. |

Nothing in the report's 21 findings, PLAN-01, or the frame-instrumentation
amendment is now unimplemented. **V2-01** is closed as a misattribution
(§20.7.2), **V2-25** is withdrawn as a harness artefact (§21.1), and **V2-23**
was found here and fixed (§20.4.4).

### 21.5 Corrected mutation ledger and second hand-back

This supersedes §20.11. The navigation row is the one §20.11 got wrong.

| # | Setting | Original | Changed to | Restored |
|---|---|---|---|---|
| 1 | `system font_scale` | `1.0` | `1.3` → `2.0` → `1.8` | **restored; verified `1.0`** |
| 2 | navbar overlays | **none enabled** | `…navbar.gestural` enabled twice, and `…navbar.threebutton` enabled once by mistake | **both disabled; verified no navbar overlay enabled, `navigation_mode = 0`, and `navigationBars` inset back to `[0,2232][1080,2340]`** |
| 3 | `POST_NOTIFICATIONS` | `granted=false` | prompted during live capture; **declined every time** | unchanged |
| 4 | files in `/sdcard/Download` | — | `vc3-*` (6 files) and `vc4-*` (4 files) | deleted by exact path; absence verified below |
| 5 | installed `com.barebit.visualcat` | 2.0.10-dev sideload, first-run data | replaced by the builds in §§20.4.4, 20.7.4, 20.8.3 and 21.3 | left installed at the final build, cleared, stopped |

> **The check that would have caught it.** `navigation_mode` is a *setting*; the
> navigation inset is what an app actually receives, and the two disagreed for
> the whole of §§20.6–20.11. A navigation restore is verified against
> `dumpsys window | grep type=navigationBars` from here on, not against
> `settings get secure navigation_mode`.

#### 21.5.1 Second hand-back verification — **PASS**

| Check | Final evidence |
|---|---|
| Captures/processes | none running; VisualCat force-stopped; the launcher is the last screen |
| Public test files | every `vc3-*` and `vc4-*` deleted by exact path; a `^vc[0-9]` scan of `/sdcard/Download` returns nothing |
| Private test sessions | `pm clear` succeeded; the clean cold launch shows the first-run state |
| Cold launch | `Status: ok`, `LaunchState: COLD`, **1,398 ms** |
| Notification permission | `POST_NOTIFICATIONS: granted=false` — declined at every prompt |
| Text scale | `font_scale = 1.0` |
| **Navigation** | **no navbar overlay enabled**, `navigation_mode = 0`, **and `navigationBars` inset back to `[0,2232][1080,2340]`** — the inset is the check that matters (§21.5) |
| Rotation | `accelerometer_rotation = 1` |
| Theme | `Night mode: yes` |
| Stability | crash buffer holds no VisualCat entries |
| Identity | fingerprint `samsung/r9qxeea/r9q:16/BP2A.250605.031.A3/G990BXXSKIZF1:user/release-keys`, byte-for-byte equal to §0.2 |
| Health | battery 100 %, 32.7 °C |
| Installed artifact | `com.barebit.visualcat` 2.0.10-dev, final build, `lastUpdateTime 2026-08-30 12:53:37`, cleared and stopped |

### 21.6 Final test and gate state

| Suite | Result |
|---|---|
| `VisualCat.App.Tests` | **436 / 436** |
| `VisualCat.Core.Tests` | **107 / 107** |
| `VisualCat.Application.Tests` | **56 / 56** |
| `VisualCat.Domain.Tests` | **47 / 47** |
| `dotnet format --verify-no-changes` | clean for every file this work touched; the two pre-existing offenders in §21.4 remain |
| `tools/verify-docs.ps1` | 3 failures, all from the v1 report's links to the owner's staged deletion (§21.4) |

New across both passes: `tests/VisualCat.App.Tests/LiveTestV2RemediationTests.cs`
(25 tests), `tests/VisualCat.Core.Tests/SyntheticLogFormatTests.cs` (6), and five
new source files — `Views/ParseOutcomeLegend.cs`, `Views/UnparsedLinesDialog.cs`,
`Views/NumberPromptDialog.cs`, plus the report and the plan's §4.3.1.

**Restore point:** nothing outstanding. §21.4 is the complete list of what is
open and why, and none of it is an unimplemented finding.

## 22. Third pass — V2-22 measured, and fixed for a different reason than assumed

§21.4 left V2-22 as the one finding implemented but unverified, for want of an
API-34 device. The fix is scoped to **every** pre-35 API level, so any of them
exercises it. An API-33 image was already installed in the SDK.

### 22.1 The instrument

```shell
avdmanager create avd -n vc-api33 -k "system-images;android-33;google_apis;x86_64" -d pixel_5
emulator -avd vc-api33 -no-snapshot -no-audio -no-boot-anim -gpu swiftshader_indirect
```

Booted in ~50 s; the Release APK already carries `x86_64` beside `arm64-v8a`, so
the shipping artifact installed unmodified. The AVD was deleted afterwards
(§22.5). Evidence: `artifacts/android-live-v2/emulator-api33/`.

**Incidental confirmation of V2-21/V2-23.** API 33 is the level the Pixel finding
came from, and its logcat shows both back callbacks registered:

```
CoreBackPreview: … Setting back callback OnBackInvokedCallbackInfo{… mPriority=-1}
CoreBackPreview: … Setting back callback OnBackInvokedCallbackInfo{… mPriority=0}
```

`android:enableOnBackInvokedCallback="true"` is in force and `MainActivity`'s own
`LayerAwareBackCallback` is registered alongside Avalonia's, on the API level
where predictive back is opt-in.

### 22.2 V2-22 reproduced, and measured

The first API-33 launch reproduced the finding exactly. Sampling the screenshot's
pixels rather than describing them:

| Band | Colour | Should be |
|---|---|---|
| status bar strip | `rgb(133,137,142)` | `Surface(light)` = `#F4F7FC` = `rgb(244,247,252)` |
| navigation bar strip | `rgb(133,137,142)` | the same |
| the app's own surface, 15 px below | `rgb(246,248,252)` | — |

`rgb(133,137,142)` is `#F4F7FC` under a **~45 % black scrim**. The report called
it "an off-palette brown-purple band"; on this device and this wallpaper it is
grey, and it is the same artefact.

### 22.3 What was actually wrong

The window's flags said it:

```
fl=… TRANSLUCENT_STATUS TRANSLUCENT_NAVIGATION DRAWS_SYSTEM_BAR_BACKGROUNDS
```

With `FLAG_TRANSLUCENT_STATUS` or `FLAG_TRANSLUCENT_NAVIGATION` set, Android
paints **its own** scrim behind the bar and **ignores `statusBarColor` and
`navigationBarColor` entirely**. So §20.8.3's fix — transparent bar colours plus
`StatusBarContrastEnforced = false` — was setting values the platform had already
decided not to read. It was not wrong; it was insufficient, and nothing on the
API-36 device could show that, because API 35+ never takes that path.

`MainActivity.ConfigureEdgeToEdgeWindow` now clears the translucent pair and
asserts `DRAWS_SYSTEM_BAR_BACKGROUNDS` before setting the colours. It is also
restated from `OnResume`, because Avalonia configures the window when its view
attaches — after `OnCreate` — and a translucent flag added there would otherwise
stand.

### 22.4 V2-22 — **DEVICE PASS** (API 33)

After the fix, `fl=` no longer contains either translucent flag, and the pixels
are exact in both variants:

| Variant | Status band | Navigation band | Expected |
|---|---|---|---|
| light | `rgb(244,247,252)` | `rgb(244,247,252)` | `Surface(false)` `#F4F7FC` ✔ |
| dark | `rgb(8,13,22)` | `rgb(8,13,22)` | `Surface(true)` `#080D16` ✔ |

The shell is continuous edge to edge, and U-07's requirement is met on a pre-35
device. `api33-bars2.png` is the before, `api33-bars-fixed.png` and
`api33-bars-dark.png` the after.

**API-36 regression check.** The same build was reinstalled on DUT-1 and the
window's flags read `fl=81810100` with the empty state rendering as before —
bars in the app's dark ground, hero centred, content clear of the navigation bar
(`AA-api36-regress.png`). The pre-35 block is guarded out there, and `OnResume`
calling into it costs nothing.

### 22.5 Cleanup

`adb emu kill`, then `avdmanager delete avd -n vc-api33` — *"AVD 'vc-api33'
deleted."* `adb devices` lists only `RFCRC0A9GND`. The SDK's installed system
images were not modified.

### 22.6 State after three passes

| Suite | Result |
|---|---|
| `VisualCat.App.Tests` | **436 / 436** |
| `VisualCat.Core.Tests` | **107 / 107** |
| `VisualCat.Application.Tests` | **56 / 56** |
| `VisualCat.Domain.Tests` | **47 / 47** |
| `dotnet format --verify-no-changes` | clean for every file this work touched |

**Every finding in this report is now closed with a device observation**, except
the two that are closed for a different reason: V2-01 is a misattribution
(§20.7.2) and V2-25 was a harness artefact of this run (§21.1). What remains open
is §21.4's list minus V2-22 — that is, the plan's own §12 coverage gaps, the v1
report's dangling links, and the release-script stages that were not run here.

