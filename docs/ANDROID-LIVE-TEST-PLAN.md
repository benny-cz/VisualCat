# VisualCat — live test plan for a physical Android device

Manual and semi-automated verification of the shipped product against a real
phone or tablet over USB/Wi-Fi ADB. These are **live tests**: a real device, a
real `logd`, real storage, real interruptions. They complement — and never
replace — `dotnet test`, which covers parsing, storage, query, and headless
view-model behaviour.

**This plan is context-agnostic.** It assumes no prior session, remembered
device serial, pre-built artifact, shell family, or knowledge of previous test
runs. The artifact, target device, supported capabilities, starting state, and
oracles are established and recorded at run time. A tester can begin at §1
without relying on facts that exist only in a previous chat or test report.

The plan is product-specific but **state-independent**. Product names and
expected behaviours come from this repository; device identity, paths, timing,
permissions, and previous data never do. If the implementation and this plan
disagree, record the discrepancy rather than silently adapting the expected
result.

---

## Contents

| § | Section |
|---|---|
| 1 | [Scope and surfaces under test](#1-scope-and-surfaces-under-test) |
| 2 | [Environment, build, and pre-flight](#2-environment-build-and-pre-flight) |
| 3 | [Test data preparation](#3-test-data-preparation) |
| 4 | [Evidence, budgets, and instrumentation](#4-evidence-budgets-and-instrumentation) |
| 5 | [Tier B — basic scenarios](#5-tier-b--basic-scenarios) |
| 6 | [Tier A — advanced scenarios](#6-tier-a--advanced-scenarios) |
| 7 | [Tier X — complex, stress, and soak scenarios](#7-tier-x--complex-stress-and-soak-scenarios) |
| 8 | [Tier U — UX/UI and accessibility passes](#8-tier-u--uxui-and-accessibility-passes) |
| 9 | [Tier H — host-side scenarios against the same device](#9-tier-h--host-side-scenarios-against-the-same-device) |
| 10 | [Tier P — privacy, security, and negative scenarios](#10-tier-p--privacy-security-and-negative-scenarios) |
| 11 | [Tier R — regression pack for previously shipped defects](#11-tier-r--regression-pack-for-previously-shipped-defects) |
| 12 | [Execution schedules](#12-execution-schedules) |
| 13 | [Recording results and exit criteria](#13-recording-results-and-exit-criteria) |
| A | [Appendix A — ADB cookbook](#appendix-a--adb-cookbook) |
| B | [Appendix B — device and ADB traps that impersonate product bugs](#appendix-b--device-and-adb-traps-that-impersonate-product-bugs) |
| C | [Appendix C — coverage map](#appendix-c--coverage-map) |

---

## 1. Scope and surfaces under test

Three products meet the same physical device. A complete release run exercises
all three, because they share one engine and differ in composition. A scenario
is not automatically applicable to every surface.

| Surface | What it is | Tiers |
|---|---|---|
| **Android companion** (`com.barebit.visualcat`) | The app running *on* the device: on-device logcat capture, finite file import, portable-session import/share, CSV export, and the phone workspace | B, A, X, U, P, R |
| **Desktop app** (`VisualCat.Desktop`) | Host machine capturing *from* the device over ADB | H, and round-trip parity |
| **CLI** (`vcat`) | Scriptable capture/index/verify/export against the same device | H, and every parity assertion |

### 1.1 Functional inventory to be covered

**Capture and import** — on-device live capture (own-app and full-device scope),
host ADB capture with buffer/pre-roll/duration options, Android finite-file
import with automatic format detection, portable `.vcat.zip` import, and
incoming `content://` and `file://` `ACTION_VIEW` intents. Import-preview
override and growing-file follow are desktop capabilities; they are not claimed
for the Android companion.

**Analysis** — severity × time heat map, minimap, zoom/pan/fit, time-range
selection, text and regex search with marker navigation, severity filters,
facets, mined Drain templates, statistics, keyset-paged entry details,
byte-faithful source context.

**Session lifetime** — progressive snapshots during ingest, partial and
recoverable sessions, finalize and reopen, session cache and retention, saved
views, recent sessions, tab strip, Android CSV export and portable share,
desktop/CLI rich exports, the share sheet, and the diagnostic bundle.

**Presentation** — Plot/Split/Details workspace modes, filter drawer, More
actions sheet, notice lane, theme, text scale, high contrast, safe-area and
cutout handling, IME handling, the Back gesture, orientation and size-class
changes.

### 1.2 Out of scope here

Unit and headless UI tests; CI packaging; Play Console submission mechanics;
iOS; desktop-only chrome, except where a round trip depends on it.

Desktop import-preview override and growing-file follow remain covered by their
desktop tests. They are mentioned here only to prevent an Android run from
mistaking their deliberate absence for a missing control.

### 1.3 Applicability and test semantics

- **Pass** means every stated expectation was observed on the identified
  artifact and device, with the required evidence.
- **Fail** means an expectation was contradicted, including a required control
  or capability being absent.
- **Blocked** means the scenario could not reach an assertion because of an
  external condition; name that condition and retain setup evidence.
- **N/A** is allowed only when the capability is explicitly unsupported by
  [`SUPPORT.md`](SUPPORT.md), the installed artifact, or the device hardware.
  Record which source makes it inapplicable. Do not turn a surprising absence
  into N/A.

Words such as *responsive*, *stable*, *correct*, and *graceful* are not pass
criteria by themselves. A scenario using one must also cite a numeric budget,
an integrity oracle, or an observable state transition from §4.

### 1.4 Physical-device coverage matrix

One device is enough for a development smoke run, not for claiming the entire
supported Android range. Select devices by coverage dimensions rather than by
model popularity.

| Gate | Minimum physical coverage | Important dimensions |
|---|---|---|
| Change smoke | One supported phone | Candidate build, one navigation mode, one permission state |
| Release candidate | Two devices where available | Lowest supported API available and target/latest API; different OEMs; restricted and granted log scope |
| UI/accessibility release gate | Phone plus a materially different form factor where supported | Small/large viewport, cutout/no cutout, gesture/three-button navigation, 60 Hz/high-refresh, light/dark, maximum font scale |
| New platform or OEM support | A physical representative of that platform/OEM | B + U + X-01 + X-05, plus vendor power-management behaviour |

If only one physical device exists, execute the run, state the matrix gaps in
the release record, and do not imply that untested API/OEM/form-factor cells
passed.

---

## 2. Environment, build, and pre-flight

### 2.1 Requirements

- One physical Android device, **API 31 (Android 12) – API 36**, `arm64-v8a` or
  `x86_64`, as currently declared by [`SUPPORT.md`](SUPPORT.md). Re-read that
  document at execution time; this copied range is not authority for a future
  release.
- USB cable or working Wi-Fi ADB, with USB debugging authorised.
- Host with the .NET SDK family in [`global.json`](../global.json), the
  `android` workload for building the companion, and `adb` on `PATH` or at a
  known path.
- At least **8 GB free** on the device's `/data` for the XL scenarios, and 4 GB
  free on the host.
- A dedicated or backed-up device for scenarios that clear app data, change
  global settings, shrink log buffers, induce low storage, or exercise Doze.
  Never run destructive X-tier setup on a person's primary phone.

> A second device of a different vendor, screen size, or API level roughly
> doubles the value of tiers U and X. Where two are available, run tier U on
> both.

### 2.2 Pre-flight — establish which device you are actually talking to

Run this **at the start of every session, and again after any device
disappearance**. Starting the server is safe; killing the shared ADB server is
not a routine pre-flight step because it disrupts other devices, IDEs, and test
runs on the host.

```shell
adb start-server
adb version
adb devices -l
adb -s <serial> get-state
adb -s <serial> shell getprop ro.serialno
adb -s <serial> shell getprop ro.product.manufacturer
adb -s <serial> shell getprop ro.product.model
adb -s <serial> shell getprop ro.product.cpu.abilist
adb -s <serial> shell getprop ro.build.version.release
adb -s <serial> shell getprop ro.build.version.sdk
adb -s <serial> shell getprop ro.build.fingerprint
adb -s <serial> shell date -u
adb -s <serial> shell wm size
adb -s <serial> shell wm density
adb -s <serial> shell settings get system font_scale
adb -s <serial> shell settings get system time_12_24
adb -s <serial> shell settings get global window_animation_scale
adb -s <serial> shell settings get global transition_animation_scale
adb -s <serial> shell settings get global animator_duration_scale
adb -s <serial> shell getprop persist.sys.timezone
adb -s <serial> shell settings get secure default_input_method
adb -s <serial> shell dumpsys display
adb -s <serial> shell dumpsys battery
adb -s <serial> shell dumpsys thermalservice
adb -s <serial> shell df /data /sdcard
```

Record the values in the run header. Verify that `get-state` is `device`, that
the returned serial/model is the physical device in hand, and that its API/ABI
is supported. Every later assertion about a crash, ANR, capture, or performance
sample belongs to *that* fingerprint and run time window.

Angle-bracket tokens are placeholders, not literal command text. Resolve them
once in the run header and reuse the recorded values:

| Token | Resolution rule |
|---|---|
| `<serial>` | Exact authorised transport serial selected from `adb devices -l`; re-prove it after every disappearance |
| `<ADB>` | `adb -s <serial>`, using the recorded ADB executable |
| `<PKG>` | Installed application id; currently expected to be `com.barebit.visualcat`, but verify it from the candidate/installed package |
| `<candidate.apk>` | Absolute path to the immutable candidate whose hash is in the run header |
| `<resolved-activity>` | Exact output of `cmd package resolve-activity --brief <PKG>` for this install |
| `<run-id>` | Unique, path-safe identifier used in markers and evidence names |
| `<session-root>` | Debug-only app-private session root discovered and validated in Appendix A |
| `<sha256-tool>` | Recorded host command that computes SHA-256 without modifying the input |
| Other scenario-local tokens | Values such as `<buffer>`, `<fmt>`, `<pid>`, `<id>`, `<hexfragment>`, and run-name fields; resolve them from that scenario's recorded setup |

Expand every token before execution. Never issue a device-mutating command
through a bare `adb` invocation, even when only one device currently appears
attached. A command containing an unresolved `<...>` token is a setup failure,
not evidence. Quote resolved paths and values for the host/device shell in use;
never paste an untrusted value into a shell command by string concatenation.

### 2.3 Build and install matrix

Debug and release builds have different purposes. Debug is useful for internal
inspection; the exact signed candidate is authoritative for a release decision.

| Build/artifact | Acquisition | Consequences |
|---|---|---|
| **Debug diagnostic build** | `dotnet build src/VisualCat.Android/VisualCat.Android.csproj -c Debug -t:Install -p:EmbedAssembliesIntoApk=true -p:AdbTarget="-s <serial>"` | `run-as` works. The deploy target attempts to grant `READ_LOGS`; verify the target and resulting state rather than assuming either. Never use Debug alone to sign off a release. |
| **Locally built Release** | `dotnet build src/VisualCat.Android/VisualCat.Android.csproj -c Release` | Useful for an early release smoke, but not necessarily signed with the production key. |
| **Signed candidate APK** | Obtain the immutable artifact produced by `tools/package-android.ps1` or the release workflow | The release authority. Record its source, byte size, SHA-256, signing-certificate digest, version, and install output before testing. `run-as` is expected to fail. |

For the signed candidate, save artifact provenance before installation and prove
that the installed package is the intended one:

```shell
# Use the host's SHA-256 tool; record the result and the absolute APK path.
<sha256-tool> <candidate.apk>
<ADB> install <candidate.apk>                 # clean-install scenario
<ADB> install -r <candidate.apk>              # upgrade/preserve-data scenario only
<ADB> shell pm path <PKG>
<ADB> shell dumpsys package <PKG>
```

Do not treat `install -r` as a clean install: it preserves app data and granted
development permissions. Do not add `-d` to force a downgrade. For the clean
path, explicitly uninstall first and record that app-private sessions/settings
will be irrecoverably removed. For the upgrade path, use A-19 and preserve them.

**Do not `adb install -r` an APK over a previous `-t:Install` debug deploy.**
Fast Deployment leaves assemblies in `files/.__override__/<abi>`; a plain APK
install does not populate that directory, and the runtime then aborts at launch
(`No assemblies found in …/.__override__/arm64-v8a`, SIGABRT, no managed stack).
It looks exactly like a startup crash and is a deploy artefact. Either deploy
with `-t:Install`, or uninstall first.

Resolve the launcher rather than hard-coding it — the activity name carries a
build-generated `crc64…` prefix that changes with the namespace:

```shell
<ADB> shell cmd package resolve-activity --brief <PKG>
<ADB> shell am start -n <resolved-activity>
<ADB> shell dumpsys package <PKG>
```

Record the resolved activity, `versionName`, `versionCode`, install/update times,
debuggable state, requested/granted permissions, and installer. Validate the
candidate's signature, manifest, API levels, ABIs, and 16 KB native alignment
with `tools/package-android.ps1 -SkipBuild` when the signing credentials are
available; otherwise attach the release workflow's verification output.

### 2.4 Permission states — a test dimension, not a setup step

`READ_LOGS` is `signature|privileged|development`. Android never prompts to grant
that permission and the app cannot request it. Separately, Android 13 and later
normally put a **one-time log-access confirmation sheet** in front of a capture that
holds the grant. Its presence and exact presentation can vary with API level,
OEM build, policy, and current platform state. Record whether it appeared on
every capture; never infer consent from the grant or infer the grant from the sheet.
The two mechanisms are independent, and every reachable combination is a
distinct product state.

Start P1–P3 while the activity is visibly foreground. The
[AOSP device-log policy](https://source.android.com/docs/core/tests/debug/understanding-logging#device-logs)
applies a stricter rule when an app requests all-device logs from the background;
a lifecycle case that triggers a new background request is a separate platform
policy branch, not evidence that the user declined a foreground sheet. In all
cases, a successful `pm grant` and package dump are only preconditions: a record
from another UID is the positive oracle for full-device reach.

| State | How to produce | Expected product behaviour |
|---|---|---|
| **P0 — no grant** | Fresh install, or `<ADB> shell pm revoke <PKG> android.permission.READ_LOGS` | Own-app-only from the first byte. The capture title includes its start time; source description/status says "On-device own-app logcat". The notice states the `adb` grant command. |
| **P1 — granted, consent allowed** | `pm grant`; if the platform sheet appears, tap *Allow one-time access* promptly; otherwise record that no sheet appeared | Resolves to full-device as soon as a foreign PID arrives. The time-stamped capture's source description/status says "On-device full-device logcat". |
| **P2 — granted, consent declined** | On a device/API that presents the sheet, hold the grant and tap *Don't allow* | Restricted, with the *declined* remedy — "Tap Live again and choose the option that allows access". The capture continues; it does not fail. |
| **P3 — granted, consent answered slowly** | On a device/API that presents the sheet, hold the grant and wait ≥ 30 s before allowing | Must still resolve to **full-device**. The scope clock starts at the first byte, not at process spawn. |
| **P4 — grant revoked mid-run** | Revoke it through the selected transport while a capture runs | Behaviour is recorded, not assumed. Note exactly what the status line, session name, and manifest say. |

```shell
<ADB> shell pm grant <PKG> android.permission.READ_LOGS
<ADB> shell dumpsys package <PKG>
```

Verify the command exit status **and** the package dump. The grant may survive
an in-place reinstall; it does not survive uninstall. Revoke it at final cleanup
unless the device owner explicitly wants it retained.

P2 and P3 are N/A on a specific device only when evidence proves that its
platform does not present the sheet. That does not satisfy release coverage for
platforms that do present it: the release matrix must include at least one such
physical device or carry an explicit coverage gap. In every state, the product's
pre-capture explanation must match what the current platform actually does; copy
that promises a sheet which never appears is a UX failure, not a platform pass.

### 2.5 State profiles and destructive clean state

Use three named starting profiles rather than clearing between every scenario:

| Profile | State | Use |
|---|---|---|
| **CLEAN** | Candidate clean-installed; no app data; permission state set explicitly | First-run, permission, and release-install scenarios |
| **WARM** | Process or activity previously used; sessions/settings retained | Resume, reopen, and ordinary regression scenarios |
| **CONTINUED** | Deliberately carries state from the named preceding scenario | Multi-stage, endurance, upgrade, and recovery scenarios |

The following procedure is destructive. Confirm the serial again, archive any
needed sessions, and use it only when a scenario requires **CLEAN**:

```shell
<ADB> get-state
<ADB> shell getprop ro.serialno
<ADB> shell am force-stop <PKG>
<ADB> shell pm clear <PKG>                    # wipes sessions, settings, and grants
<ADB> shell pm grant <PKG> android.permission.READ_LOGS   # only for P1–P4
```

Do not clear global log buffers as routine setup; they belong to the whole
device. Instead, bracket evidence by UTC timestamps and run markers (§4.1). A
scenario specifically testing ring-buffer clearing may clear them on a dedicated
device and must say so.

### 2.6 Device mutation ledger and guaranteed restoration

Before changing any global setting, record its exact original value, the command
that changes it, and the inverse/restore command in a mutation ledger. Restore
in a `finally`-style cleanup even after a failure. This applies at minimum to:

- log-buffer sizes and clears;
- font scale, display size/density, rotation, locale, layout direction, time,
  time zone, navigation mode, theme, animation scales, and refresh rate;
- battery simulation, Doze, battery saver, and *Don't keep activities*;
- proxy/VPN configuration, airplane mode, and network state;
- filler files and other storage-pressure data;
- test users/profiles, per-profile installs, and per-profile grants;
- temporary `READ_LOGS` grants and evidence copied to shared storage.

If the starting value cannot be read or the restore command cannot be proved,
do not mutate that setting on a shared device. §13.5 is a mandatory final
cleanup check, not an optional courtesy.

### 2.7 Destructive-scenario register

Read this before booking a device, not after. Everything below changes state
outside the app, and some of it cannot be undone without a reboot or a reflash.

| Scenario | What it changes beyond the app | Isolation / approval required |
|---|---|---|
| §2.5 CLEAN profile, B-01, P-08 | Uninstalls or clears the app: sessions, settings, and grants are gone | Back up needed data and obtain explicit device-owner approval |
| §3.3 traffic generators | Floods the device's shared log buffers | Recommended |
| X-04 | Global log-buffer sizes; clears buffers other tools are reading | **Yes** |
| X-07 | Battery simulation, Doze, battery saver | **Yes** |
| X-08 | Enables *Don't keep activities* and deliberately kills the process | Controlled device; ledger and prove the option is restored |
| X-10 | Fills `/data` toward a safety reserve | **Yes** |
| X-11, A-23 | Device clock and time zone | Recommended |
| X-16 | Deliberately drives the device into thermal throttling | **Yes** |
| X-20 | Reboots the device; uses Android *Force stop* | Recommended |
| X-25 and optional §3.3 traffic | May toggle airplane/network state and install or exercise other apps | Ledger every mutation; controlled device recommended |
| H-02 | Revokes USB-debugging authorisation and interrupts the host transport | Controlled device; prove the owner can re-authorise it |
| P-04 | Writes crafted archives, including inflation cases | **Yes** |
| P-13 | Creates a secondary user or managed profile | **Yes** |
| A-19, A-22 | Changes the installed build, signing/delivery state, and possibly persisted data | Controlled or backed-up device; explicit owner approval |
| U-04, U-05, U-06, U-07, U-09, U-10, U-11, U-16, U-17, U-19 | Global font scale, theme, navigation, locale, accessibility services, animation scales, and device accent | No, but ledger every value and restore assistive technology to the owner's state |
| X-09 | Sends memory-trim signals to the running process | No |

"Dedicated device" means a phone whose data nobody needs. A scenario marked
**Yes** must not run on somebody's primary handset even with a ledger, because
its failure mode is a device left unusable rather than a setting left wrong.
"Recommended" may be waived only when the owner accepts the recorded impact and
the exact recovery path has been rehearsed.

---

## 3. Test data preparation

### 3.1 Deterministic corpora

Generate on the host with the CLI, which produces byte-identical content for the
same seed on every platform, then push to the device. Sizes below are the ones
the scenarios refer to by name.

```shell
dotnet build src/VisualCat.Cli/VisualCat.Cli.csproj -c Release
dotnet run --project src/VisualCat.Cli -c Release --no-build -- generate-test-log --output tiny.txt   --lines 1000     --seed 42
dotnet run --project src/VisualCat.Cli -c Release --no-build -- generate-test-log --output small.txt  --lines 50000    --seed 42
dotnet run --project src/VisualCat.Cli -c Release --no-build -- generate-test-log --output medium.txt --lines 250000   --seed 42
dotnet run --project src/VisualCat.Cli -c Release --no-build -- generate-test-log --output large.txt  --lines 1000000  --seed 42
dotnet run --project src/VisualCat.Cli -c Release --no-build -- generate-test-log --output xl.txt     --lines 5000000  --seed 42

<ADB> push tiny.txt small.txt medium.txt large.txt xl.txt /sdcard/Download/
```

The `dotnet run -- ...` form is intentionally independent of host executable
suffix and publish layout. Before pushing, create a corpus manifest containing:

- repository commit and CLI version;
- generator arguments and seed;
- byte length, SHA-256, physical line count, final-newline state, and encoding;
- expected parsed/unknown counts, severity counts, first/last timestamp, and
  template identities from CLI `stats`/`verify` where applicable.

Pull at least one pushed file back and compare SHA-256 to catch transport or
storage corruption. Every scenario asserting an exact count or byte range must
name the manifest field used as its oracle.

| Name | Lines | Approx. size | Used by |
|---|---:|---:|---|
| `tiny` | 1 000 | ~90 KB | B-tier smoke, intent import |
| `small` | 50 000 | ~4.5 MB | B-tier, UX passes |
| `medium` | 250 000 | ~23 MB | A-tier, interaction-under-load |
| `large` | 1 000 000 | ~86 MB | X-tier, the headline volume case |
| `xl` | 5 000 000 | ~430 MB | X-tier storage/limits case |

Also prepare the repository's own fixtures, which have known shapes:

- `samples/logcat_small.txt` — the bundled deterministic sample.
- `test-data/` — sanitized golden parser fixtures, one per format.

### 3.2 Adversarial corpora — deterministic, versioned, and checksummed

| File | How to build | What it probes |
|---|---|---|
| `fmt-time.txt`, `fmt-brief.txt`, `fmt-long.txt`, `fmt-epoch.txt` | `vcat generate-test-log --format <fmt>` for each supported format | Android automatic format detection; desktop override is a separate test |
| `mixed-formats.txt` | Concatenate two different formats | Detection confidence, unknown-line accounting |
| `crlf.txt` | Convert `small.txt` line endings to CRLF | Byte-faithful offsets |
| `bom.txt` | Prepend a UTF-8 BOM to `small.txt` | Encoding handling |
| `nonutf8.bin` | Inject raw `0x80–0xFF` bytes into `small.txt` | Decoder robustness, no data invention |
| `truncated.txt` | Cut `medium.txt` at a recorded byte offset in the middle of a line | Partial final record |
| `longline.txt` | One record with a 2 MB message | Bounded line handling, wrap, copy |
| `continuations.txt` | Java stack traces with indented continuation lines | [ADR 0009](adr/0009-continuations.md) continuation attribution |
| `outoforder.txt` | Shuffle timestamps backwards across a few thousand records | Late-segment handling; timestamps are never clamped |
| `dst.txt` | Records spanning a DST transition in a non-UTC zone | Timestamp policy, [ADR 0008](adr/0008-time-policy.md) |
| `nofinalnewline.txt` | Strip the trailing newline | Last-record attribution |
| `empty.txt` | Zero bytes | Empty-source messaging |
| `notalog.bin` | 10 MB of random bytes named `.txt` | Import failure presentation |
| `crashy.txt` | Inject `FATAL EXCEPTION` / `AndroidRuntime` blocks at known offsets | Search, templates, severity, crash discovery |

Do not make these files by unrecorded manual editing. Generate them with a
version-controlled script or exact recorded byte operations, then add the same
oracle fields as §3.1. Include at least one occurrence and one non-occurrence of
every marker used by a search assertion. Push whichever a scenario names; keep
the rest on the host for tier H. If the corpus cannot be reproduced from its
recipe and checksum, its exact-count assertions are Blocked.

### 3.3 Producing live traffic on the device

Some scenarios need the device to *emit* log volume on demand. Run each command
in its own terminal through the selected transport. Emit run-specific begin/end
markers; line numbers alone collide across runs. Android/OEM rate limiting means
the requested count is an input, not proof of delivery—the product must account
for observed loss rather than be judged against the loop count.

```shell
# Steady, labelled traffic — 5 lines/s for 10 minutes, at a known tag.
<ADB> shell 'log -p i -t VCATTEST "RUN=<run-id> BEGIN steady"; for i in $(seq 1 3000); do log -p i -t VCATTEST "RUN=<run-id> steady $i"; sleep 0.2; done; log -p i -t VCATTEST "RUN=<run-id> END steady"'

# Burst storm — as fast as the shell can go, to force ring-buffer pressure.
<ADB> shell 'log -p e -t VCATSTORM "RUN=<run-id> BEGIN burst"; for i in $(seq 1 200000); do log -p e -t VCATSTORM "RUN=<run-id> burst $i"; done; log -p e -t VCATSTORM "RUN=<run-id> END burst"'
```

First probe that the device shell supplies `log`, `seq`, and fractional `sleep`.
If it does not, use a small, versioned traffic-generator APK with a recorded
version/seed/rate instead of improvising another source during the run. Stop all
generators explicitly during cleanup.

Real system traffic is often better than synthetic: opening the camera, playing
video, toggling airplane mode, and running a large app install all produce
distinctive multi-buffer bursts that a full-device capture should show and an
own-app capture should not.

---

## 4. Evidence, budgets, and instrumentation

### 4.1 Evidence to capture for every scenario

1. **Run boundary** — record host UTC, device UTC, elapsed time, package PID(s),
   and a unique `VCATTEST RUN=<run-id> START/END` marker. This makes later crash,
   ANR, and log evidence attributable instead of relying on global history.
2. **Screenshot** at each assertion moment. Use a device file and `pull` so the
   command is binary-safe in Windows PowerShell as well as POSIX shells:
   `<ADB> shell screencap -p /sdcard/vcat-<run-id>-<id>.png`, then `<ADB> pull`
   it and remove the device copy.
3. **Screen recording** for motion, ordering, or timing. Record the actual frame
   rate; `screenrecord` may cap duration and frame rate, so chain named clips for
   longer cases and do not infer sub-frame latency it cannot show.
4. **A time-bounded full logcat stream** — not only `--pid`. PID-filtered logcat
   exits when the process dies and therefore loses the relaunch/crash evidence a
   lifecycle test needs. Save `-b all -v threadtime` for the scenario window and
   filter to `<PKG>`, `VisualCat`, `AndroidRuntime`, activity/process manager,
   and run markers during analysis. Record PID changes.
5. **The session or portable export** — use `run-as` only for Debug. For Release,
   use the product's portable share/export path; inability to inspect private
   storage is expected security behaviour, not missing evidence.
6. **Visible state text**, verbatim, at start/mid/end when present: status line,
   notice, session name, count, scope, progress, and terminal message.
7. **Resource samples named by the scenario** with elapsed time, battery,
   thermal status, free space, PSS/RSS, CPU, and frame statistics.

8. **The product's own structured diagnostics**, for scenarios that inspect
   internal sequencing — scope resolution, safe-area attachment, finalize stages.
   *Appearance & timeline* carries *Write redacted structured diagnostics*.
   Inspect and record its current value; enable it if the scenario requires it,
   collect the output alongside the session, and restore the prior value if it
   was changed. Do not assume a default: settings can survive upgrades and can
   differ between artifacts.

Store evidence under `<run-id>/<scenario-id>/` with a short index containing
capture times and SHA-256 values. Logs can contain credentials, account names,
message content, and device identifiers: restrict access and retention, redact a
copy for defects, and never attach an unreviewed full-device log publicly.

### 4.2 Performance and responsiveness budgets

These are **provisional absolute gates plus regression signals**. On the first
controlled run, establish a per-device/build baseline, but do not redefine a
missed absolute gate until it passes. For launch and short interactions use at
least 5 cold or 10 warm repetitions, report median and p95, and discard no run
without recording the reason. Cool the device to its baseline thermal state;
keep charge state, refresh rate, animations, brightness, and background workload
fixed. Compare like with like and flag a >20% median or p95 regression even when
the absolute gate still passes.

| Signal | Starting budget | Measurement |
|---|---|---|
| Cold start to reported launch | median ≤ 2.5 s; no run > 4 s | Force-stop before each `am start -W`; record `ThisTime`, `TotalTime`, and `WaitTime`. These are launch metrics, not proof of first frame. |
| Warm resume | median ≤ 600 ms; p95 ≤ 1 s | Home/recents then `am start -W` without killing the process |
| Tap → visible acknowledgement | p95 ≤ 100 ms where the evidence frame rate can resolve it; no response > 250 ms | High-frame-rate camera/Perfetto preferred; screen recording only when its measured fps is adequate |
| Plot ↔ Split ↔ Details switch | ≤ 300 ms to settled layout | screenrecord |
| Filter drawer open/close | ≤ 250 ms | screenrecord |
| On-device import throughput | ≥ 30 000 lines/s sustained | Status line rate, cross-checked with wall clock |
| First heat map visible after import starts | ≤ 3 s | screenrecord |
| Search over 1 M entries, first results | ≤ 1.5 s | Stopwatch from Apply |
| Timeline pan/zoom during ingest | No visible freeze > 1 s; ≤ 15% janky frames; p95 no worse than 20% over baseline | `dumpsys gfxinfo <PKG> framestats`, interpreted against the device refresh period |
| Entry paging (`+500`) | ≤ 400 ms | screenrecord |
| Live capture UI refresh | Bounded by the *Live UI refresh limit (Hz)* setting; no busy-loop | gfxinfo + `top` |
| Stop capture → button answers | ≤ 250 ms to acknowledgement, and the acknowledgement is sticky | screenrecord |
| Stop → "capture saved" | No absolute gate; the elapsed clock must advance visibly throughout and the stage must be named | Record total elapsed against entry count for each capture size; compare like with like across builds |
| Live capture ingest rate, full-device | No absolute gate; flag a >20% drop against the recorded baseline for the same device and the same traffic | Status-line rate, cross-checked against the marker-bounded interval |
| Session reopen after finalize | ≤ 5 s for a session of ≤ 1 M entries; no run > 10 s | From the tap to a drawn plot showing the final count; 3 repetitions, report median |
| Memory during 1 M-line import | No OOM/LMK; peak and settled PSS ≤ 25% over the comparable baseline unless explained | Sample `dumpsys meminfo <PKG>` at fixed progress points |
| Memory during soak | No sustained positive slope after workload reaches steady state | Sample at least every 15 min; graph PSS and retained session size against elapsed time/entries |
| ANRs | Zero attributable to `<PKG>` in the scenario window | Time-bounded logcat and bugreport/dropbox evidence where accessible |
| Crashes / native faults | Zero attributable to `<PKG>` in the scenario window | Full/crash log buffers plus bugreport; consumer builds need not allow `/data/anr` or `/data/tombstones` listing |

For leak and soak claims, declare the warm-up interval, sample cadence, workload,
and comparison windows before starting. PSS can legitimately step upward while
caches warm and downward only after garbage collection or idle; monotonic samples
alone are not a leak oracle. Compare rolling-window medians after warm-up,
normalize disk/session growth to the work performed, look for a plateau, and
repeat a suspicious loop. Fail sustained post-warm-up growth outside the
baseline envelope when it does not plateau and is corroborated by retained
threads/handles/files, worsening latency, or resource exhaustion risk.

### 4.3 Instrumentation commands

```shell
<ADB> shell dumpsys gfxinfo <PKG> reset          # before an interaction burst
<ADB> shell dumpsys gfxinfo <PKG>                # after: janky frames, percentiles
<ADB> shell dumpsys gfxinfo <PKG> framestats     # per-frame detail
<ADB> shell dumpsys meminfo <PKG>                # PSS breakdown
<ADB> shell dumpsys thermalservice               # throttling during soak
<ADB> shell dumpsys batterystats --charged <PKG> # battery attribution
<ADB> shell df /data /sdcard                     # storage headroom
<ADB> shell "top -b -n 1 -o PID,%CPU,RES,CMDLINE | grep -i visualcat"
<ADB> shell dumpsys activity processes <PKG>     # process/importance state
<ADB> logcat -b crash -d -v threadtime           # correlate only within run window
```

Reset `gfxinfo` immediately before the interaction being measured; otherwise the
numbers include start-up frames and mean nothing. `gfxinfo` counts only frames
the process rendered; pair it with the recording and process-state evidence so a
dead or frozen renderer does not look artificially smooth. Directory listings
under `/data/anr` and `/data/tombstones` are optional diagnostics on a rooted or
debuggable environment, never the sole release gate on a consumer device.
Probe OEM-dependent command options before the measured interval. If an option is
missing, retain its exit status/stdout/stderr and use a recorded equivalent that
measures the same signal. If the required signal cannot be measured, mark the
quantitative assertion Blocked; neither command failure nor missing evidence is
a product Pass.

---

## 5. Tier B — basic scenarios

Purpose: prove the product works at all on this device, in the plain path a
first-time user takes. Every B scenario should pass before any A or X scenario
is attempted; a B failure invalidates the rest of the run.

**Format of each block:** *Risk* — what would go wrong. *Pre* — starting state.
*Steps* — what to do. *Expect* — what must be true. *Fail if* — the disqualifying
observations.

---

**B-01 · Cold install and first launch**
*Risk* Packaging, runtime init, first-run empty state.
*Pre* App uninstalled. Device pre-flight done (§2.2).
*Steps* Install per §2.3 · launch via the resolved activity · observe the first
screen without touching anything for 30 s.
*Expect* Launch inside the cold-start budget. The empty state shows the identity
line — `VisualCat <version> · local-first · no telemetry`, whose version must
match the `versionName` recorded in the run header — the severity legend, and
exactly the Android hero actions: *OPEN LOG*, *ON-DEVICE LIVE*, and *RECENT
CAPTURES*. *ADB live*, *Follow growing file*, *Save session*, and *Save portable*
are desktop commands and must not appear. No crash, no ANR, no white or black
band at the status bar or navigation bar.
*Fail if* SIGABRT at launch (check §2.3 for the Fast Deployment trap before
filing), any unhandled exception in the app's logcat, chrome drawn under a
cutout, a displayed version that does not track the installed build, or a
missing *ON-DEVICE LIVE* link — that means the on-device source failed to
register, and is a Fail rather than a platform limit.

**B-02 · Empty state lists captures already on the device**
*Risk* The cold-start screen must be useful after a process restart.
*Pre* At least two sessions previously created on the device.
*Steps* Force-stop the app · relaunch · read the empty state.
*Expect* *Recent captures on this device* lists up to four sessions, named after
the capture (including its start time), not after a storage folder. One tap
reopens one.
*Fail if* Sessions exist on disk but the list is empty, or entries are named
identically with nothing to choose between them.

**B-03 · Import a small file from device storage**
*Risk* SAF picker/provider compatibility, automatic format detection, and
byte-faithful ingest.
*Pre* `small.txt` in `/sdcard/Download`.
*Steps* *Open log* · pick `small.txt` through the system picker · observe ingest.
*Expect* Android imports immediately with the automatically detected format and
timestamp policy; the stored manifest records both. Ingest completes; parsed and
unknown counts match the corpus oracle; the heat map draws; the tab is named
after the provider display name. Android does **not** show the desktop-only
import-preview override.
*Fail if* Counts or manifest settings disagree with the oracle, the picker URI
cannot be materialized, or a desktop-only preview is required to proceed.

**B-04 · Read the heat map and reach an exact record**
*Risk* The core value proposition, end to end.
*Pre* B-03 session open.
*Steps* Observe the six severity rows · tap a dense cell · read the entry list ·
select a row · open the *Entry* tab · read the source bytes behind it.
*Expect* Cell → entries → selected entry → raw source are consistent: the same
timestamps, the same message, and raw bytes that match the file at the stated
offset (verify with a binary range reader against the corpus manifest; do not
use a line-oriented tool that can transform encoding or line endings).
*Fail if* The raw view shows a different record than the selected row, or offsets
do not resolve.

**B-05 · Severity filters**
*Risk* Filter composition and density semantics.
*Steps* Toggle each of the six severities individually and in combination ·
observe counts, chips, and the plot.
*Expect* Counts change consistently with the plot; active filters are named in
the chip bar; *Clear all* returns to the unfiltered view exactly.
*Fail if* A severity's count and its plotted density disagree, or a cleared
filter leaves residue in the chip bar.

**B-06 · Text search and marker navigation**
*Risk* Search, markers, and viewport preservation.
*Steps* Search a literal known to occur (for example `AndroidRuntime` in
`crashy.txt`) · step forward and backward through markers · reach the ends.
*Expect* Matches are highlighted; navigation moves the viewport to each match,
wraps at both ends, and preserves the current zoom span.
*Fail if* Navigation changes zoom, skips matches, or fails to wrap.

**B-07 · Regex search**
*Steps* Enable *Regex* · run a valid pattern · then an invalid one · then a
pathological one (`(a+)+$` against long lines).
*Expect* Valid patterns match; an invalid pattern produces a clear message and no
crash; a pathological pattern is cut off by the bounded regex timeout and says
so, leaving the UI responsive.
*Fail if* The UI freezes on any pattern.

**B-08 · Zoom, pan, and Fit**
*Steps* Pinch-zoom into a burst · drag to pan · use the minimap · tap *Fit*.
*Expect* Zoom is smooth and stays within the session's time range; the minimap
shows where the viewport sits; *Fit* returns to the whole session in one tap and
is reachable without opening the filter drawer.
*Fail if* Panning escapes the session bounds, the minimap and viewport disagree,
or *Fit* is only reachable inside a drawer.

**B-09 · Workspace modes**
*Steps* Cycle Plot → Split → Details → Plot, then rotate the device and cycle
again.
*Expect* Each mode composes within budget. In Split the entries list keeps its
floor of at least four rows; in Details at least six. The chosen mode survives
rotation.
*Fail if* The entries list collapses below its floor, or rotation silently
changes the mode.

**B-10 · On-device live capture, own-app (state P0)**
*Risk* The restricted path must be honest.
*Pre* No `READ_LOGS` grant (§2.4).
*Steps* Tap *Live* · read the product explanation · record whether this OS/OEM
shows its log-access sheet in P0 and answer it if shown · let it run 60 s · stop.
*Expect* The pre-prompt explains what is captured and where it goes. The session
is named for the capture start time and reports **own-app** scope. The notice
gives the `adb shell pm grant …` remedy verbatim. Line count is small (order of
tens to hundreds) and that is presented as expected, not as an error.
*Fail if* The product claims full-device scope, or the remedy text is absent.

**B-11 · On-device live capture, full-device (state P1)**
*Pre* `READ_LOGS` granted; if the platform sheet appears, consent allowed
promptly.
*Steps* Start capture · generate traffic (§3.3) · run 3 minutes · stop.
*Expect* Scope resolves to **full-device** within seconds of the first foreign
record; the status line, source description, notice, session details, and stored
manifest agree. The capture title remains a time-stamped discriminator rather
than a scope claim. Throughput is orders of magnitude above B-10. Stop finalizes
and the session reopens.
*Fail if* Any scope-bearing surface disagrees with the others.

**B-12 · Stop capture is answered and sticky**
*Risk* The single most user-visible past failure.
*Steps* Run a capture for at least 5 minutes · press *Stop capture* once · do not
press again · watch the status line to the end.
*Expect* Acknowledgement within budget; the label never springs back to *Stop
capture*; the status line leads with a visibly moving elapsed clock and names the
stage (draining, compacting, writing the index, reopening). Once the manifest is
written it says the capture is saved. When it ends, the live controls disappear.
*Fail if* The status returns to "Capturing" after the press, or the state never
resolves.

**B-13 · Reopen a finished session**
*Steps* Close the tab · reopen the session from *Recent sessions* and from the
empty state.
*Expect* It reopens with the same entry count, the same time range, and a working
plot. Reopen time is recorded.
*Fail if* Counts differ from the finished capture, or a reopened session shows an
empty list under a "Ready" status.

**B-14 · Export and share a session**
*Steps* *Export CSV…* with each scope offered · verify the CSV against the CLI
oracle · then *Share…* the portable archive to another installed app.
*Expect* The CSV scope is explicit; the file lands with a sensible name derived
from the capture; the portable share sheet opens with a read-granted
`content://` URI from the app's FileProvider; the receiving app can read it.
Android is not expected to expose the desktop/CLI raw and report exporters.
*Fail if* A `file://` URI is exposed, or the share fails with a permission error.

**B-15 · Back gesture and sheet dismissal**
*Steps* Open the More sheet, a dialog, and the filter drawer in turn · dismiss
each with the system Back gesture · then, with every layer closed, press Back
once more.
*Expect* Back closes exactly one layer at a time, innermost first, and does not
exit the app while a layer is open. With nothing open it **falls through to the
platform** and leaves the app: the workspace must not claim a Back press it has
nothing to answer.
*Fail if* Back exits the app from an open sheet, a layer cannot be dismissed
without touching a specific pixel, or the app cannot be left with the gesture at
all once every layer is closed.

**B-16 · Live capture start states and the first batch**
*Risk* The seconds between tapping *Live* and the first visible entries — where
the product looks broken if it says nothing.
*Pre* Run the direct start once in P0 (own-app, deliberately low volume) and once
in P1. For a third pass, start known long imports one at a time until a probe
import visibly queues; cancel only that queued probe while leaving every active
reader running, then tap *Live*, so the live operation must queue without prior
knowledge of the build's concurrency limit.
*Steps* For each direct pass, tap *Live* and watch the status line and empty plot
until the first entries are drawn · in P0, then leave it idle with almost no
traffic for two minutes · for the queued pass, watch it until the preceding
operation releases the slot and capture begins.
*Expect* A direct on-device start progresses from a named *starting* state to a
running/waiting-for-data state. The queued pass says it is *preparing* while it
waits, then starts normally. *Connecting* is a host-ADB state tested in H-03/H-04
and is not required for an on-device source. The empty timeline explains what the
capture is doing instead of inviting the user to start it again. The first batch
becomes visible as soon as it exists: a low-volume source publishes its completed
line after the configured latency without waiting for a second chunk. The
operation is never relabelled as an import, and *Stop capture* stays available
for as long as acquisition is active.
*Fail if* The first entries stay buffered until a second chunk arrives, a live
capture is described as an import, *Stop capture* disappears while acquisition
continues, or an idle capture is indistinguishable from a failed one.

---

## 6. Tier A — advanced scenarios

Purpose: the paths a competent user reaches on their second day — multi-session
work, lifecycle events, imports that are not well-behaved, and the analysis
features beyond search.

---

**A-01 · Mined templates on real device traffic**
*Steps* Full-device capture for 10 minutes with the device doing real work
(camera, video, app install) · open *Insights → Templates*.
*Expect* Templates are ranked and stable; the same capture reopened produces the
same template identities; filtering by a template narrows both plot and entries;
muting a template reports what it did in the notice lane.
*Fail if* Template identity changes between reopens of the same session.

**A-02 · Facets at high cardinality**
*Steps* On the same capture, open *Insights → Facets* · scroll tags and PIDs ·
apply a facet · combine with a severity filter and a search.
*Expect* Facet lists are scrollable and responsive with thousands of distinct
values; combined filters compose (AND across dimensions) and every active
dimension appears in the chip bar.
*Fail if* Facet application drops an unrelated filter, or the list stalls.

**A-03 · Saved views round trip**
*Steps* Build a non-trivial filter (severity + facet + regex + time range) · save
it as a named view · clear everything · apply the saved view · reopen the app and
apply it again.
*Expect* Every dimension is restored exactly, including the time range. Deleting
a view removes it and does not disturb the current filter.
*Fail if* Any dimension is silently dropped on restore.

**A-04 · Time-range selection, filter, zoom, and export by range**
*Steps* Drag a range on the plot · use *Zoom range*, *Filter range*, and
*Export range* in turn.
*Expect* The three are distinct: zooming changes the viewport only, filtering
changes the result set, exporting writes exactly the selected span. Escape clears
a selected scope before it clears filters.
*Fail if* Zoom and filter are conflated, or an export contains records outside
the range.

**A-05 · Multiple sessions open at once**
*Steps* Open three sessions (a file import, a live capture, a portable archive) ·
switch between tabs repeatedly · close the middle one.
*Expect* Each tab keeps its own filters, viewport, and selection. Switching is
immediate. Closing one does not disturb the others. On the phone the tab strip
stays legible with three chips.
*Fail if* State leaks between tabs, or the strip becomes unreadable.

**A-06 · Open through an `ACTION_VIEW` intent (`content://`)**
*Steps* From at least two DocumentsProvider sources (for example Downloads and a
third-party provider), use *Open with* to view a `.txt` log in VisualCat while
the app is (a) not running and (b) running with a session open. Redeliver the
same URI to the same running activity.
*Expect* Both lifecycle cases open the log. The incoming stream is materialized
into the app cache with a safe display name. The same URI delivered twice to the
same activity does not create duplicate tabs. This is `ACTION_VIEW`; inbound
`ACTION_SEND` is not an implemented promise.
*Fail if* A repeat delivery creates duplicate tabs, a running app ignores the
new intent, or provider-specific URI syntax becomes a path traversal/name bug.

**A-07 · Import a portable `.vcat.zip` produced elsewhere**
*Pre* An archive exported from the desktop app or CLI, pushed to the device.
*Expect* It opens with identical counts and template identities to the source.
Verify with the parity procedure in H-06.
*Fail if* Counts or templates differ across the transport.

**A-08 · Automatic detection across formats**
*Steps* Import `mixed-formats.txt` and each `fmt-*.txt` through Android's picker.
*Expect* Each single-format file is detected according to the corpus oracle and
the choice/timestamp policy is persisted in the manifest. Mixed or uncertain
content produces honest parsed/unknown accounting; unknown lines remain
reachable and no desktop-only override dialog is promised on Android.
*Fail if* The manifest disagrees with the detected settings, input disappears,
or the Android UI tells the user to operate a dialog it does not expose.

**A-09 · Import failure presentation**
*Steps* Import `notalog.bin`, then `empty.txt`.
*Expect* A failure card in the workspace stating the reason, the step the
platform can offer, and the two actions worth taking. No half-built workspace of
inert panes over an empty store. No advice that only makes sense on a desktop.
*Fail if* The app builds a full workspace over nothing, or offers a desktop-only
remedy on a phone.

**A-10 · Adversarial corpora sweep**
*Steps* Import `crlf.txt`, `bom.txt`, `nonutf8.bin`, `truncated.txt`,
`longline.txt`, `continuations.txt`, `nofinalnewline.txt`, `outoforder.txt`,
`dst.txt` in turn.
*Expect* For each: no crash; unknown/undecodable content is accounted for rather
than invented or dropped; continuations attach to their parent record;
out-of-order timestamps are preserved rather than clamped; a 2 MB line renders,
wraps on request, and can be copied.
*Fail if* Any source byte becomes unattributable, or any file crashes the app.

**A-11 · Provider mutation during finite import**
*Pre* A provider-backed copy of `large.txt` whose source can be replaced or
revoked while Android is opening it.
*Steps* Begin opening it · during materialization revoke the URI or replace/
truncate the provider source · repeat with an intentionally slow provider if
available.
*Expect* VisualCat imports one consistent materialized snapshot or fails with a
clear, recoverable message. It never creates a hybrid of old/new bytes, hangs
indefinitely, or reports a finished session whose source hash cannot verify.
*Fail if* A torn source is accepted as complete or cancellation leaves an inert
workspace. Growing-file follow itself is desktop-only and is not tested here.

**A-12 · Live capture survives the screen going off**
*Steps* Start a full-device capture · turn the screen off for 10 minutes ·
turn it back on.
*Expect* The capture is still running, the entry count grew across the dark
period, and the UI redraws correctly on resume without a stale frame.
*Fail if* The capture ended, or the resumed UI shows counts from before the
screen went off.

**A-13 · Live capture survives a configuration change**
*Steps* During a running capture, in turn: rotate · change the system text size ·
toggle dark mode · change the system locale · enter split-screen.
*Expect* The capture keeps running through every one. The controls (*Follow*,
*Stop capture*) stay present, the status line keeps its tense, and the session
does not reopen from disk.
*Fail if* Any configuration change ends the capture or blanks the session while
it reloads.

**A-14 · Follow mode during live capture**
*Steps* Toggle *Follow* on and off during a capture · zoom away from the live
edge with Follow on · use the *New data* affordance.
*Expect* Follow keeps the viewport at the live edge; manually panning away either
disengages Follow or is explicitly signalled; *New data* returns to the edge.
*Fail if* Follow fights the user's pan silently.

**A-15 · Session cache and retention**
*Pre* Inventory sessions and record the current cleanup policy so it can be
restored. Explicitly disable automatic cleanup for the first phase.
*Steps* Open *Session cache* · read what the device is storing · exercise the
disabled policy · enable automatic cleanup · delete eligible sessions · restore
the prior policy.
*Expect* Sizes and counts are internally consistent. On the signed candidate,
cross-check product-visible totals against portable artifacts and the before/
after storage delta in Android's app-storage settings; on the separate Debug
diagnostic pass, verify exact app-private sizes with `run-as du`. Nothing is
deleted while cleanup is disabled. The delete action names exactly what it will
remove before removing it, and storage converges after deletion.
*Fail if* A session is deleted without the policy being enabled, or reported
sizes/counts contradict the available independent oracle.

**A-16 · Diagnostic bundle**
*Steps* Create a diagnostic bundle · review it before sharing · pull it to the
host and open it.
*Expect* The confirmation states what goes in. The bundle contains no log
content beyond what was declared and no personal paths beyond what redaction
allows. It is a valid zip with no traversal entries.
*Fail if* Raw log content or unredacted paths appear in a bundle described as
redacted.

**A-17 · Settings take effect**
*Steps* In *Appearance & timeline*, change in turn: theme, text scale, high
contrast, timeline intensity scale, timeline normalization, maximum zoom
precision, minimum bar width, pixel snapping, default export order, CSV encoding,
live UI refresh limit.
*Expect* Each change is visible on the workspace without restarting the app, and
survives a process restart. Two- and three-option settings render as segments,
not dropdowns, on the phone.
*Fail if* A setting needs a restart, is lost on restart, or opens a popup that
lands over an unrelated field.

**A-18 · Copy actions**
*Steps* Use *Copy message*, *Copy raw*, *Copy details*, and the Insights *Copy*.
*Expect* Each puts the right content on the clipboard (verify by pasting into
another app) and each reports what it did in the notice lane.
*Fail if* A copy action is silent, or copies the wrong scope.

**A-19 · Upgrade from the previous supported release**
*Pre* Install the previous stable signed APK; create one file session, one
finished live capture, saved views, non-default appearance/export settings, and
a partially recoverable session. Record counts and hashes.
*Steps* Install the candidate with `<ADB> install -r <candidate.apk>` without
clearing data · launch normally · open and exercise every retained item.
*Expect* Android accepts the signature/version upgrade. Sessions, settings,
saved views, recents, and portable exports remain correct or are migrated with
an explicit one-time message. Migration is idempotent across a second launch.
No temporary `READ_LOGS` grant is mistaken for a user-facing app permission.
*Fail if* install requires data loss, retained data silently disappears, counts
change, or a failed migration prevents reaching cleanup/export.
*Note* An application-ID change is not an upgrade path. A build published under a
previous application ID is a different app to Android: it is not upgraded in
place, and its `READ_LOGS` grant does not carry over. Choose the newest previous
release sharing the candidate's application ID, or record N/A with that reason.

**A-20 · Cancellation and refusal paths**
*Steps* Cancel each system picker, export destination picker, share chooser,
product confirmation, Android log-access sheet, long search/load-all operation,
and capture stop/finalize path at every offered cancellation point.
*Expect* Each returns to the prior usable state, performs no unintended action,
and leaves no duplicate tab, empty export, stuck scrim, orphan process, or
misleading completion notice. Cancellation is distinct from failure.
*Fail if* Any cancellation performs part of the action anyway, or leaves a
duplicate tab, an empty export file, a stuck scrim, an orphan process, or a
completion notice for work that did not happen.

**A-21 · Names, providers, and Unicode boundaries**
*Steps* Import/export/share files with empty/very long provider display names,
multiple dots, no extension, reserved punctuation, emoji, combining marks, RTL
text, and names differing only by case or normalization. Repeat through two
providers.
*Expect* UI labels remain readable; generated cache/export names are safe and
unique; extensions are neither lost nor doubled; no name can escape the intended
directory; reopening selects the correct session.
*Fail if* A generated name escapes its directory, collides silently, loses or
doubles an extension, or a label renders as mojibake or as unbounded text that
pushes its row's controls off screen.

**A-22 · Distribution-channel install and update**
*Pre* When a Play internal/closed track exists, enrol the physical device and
record the previous track version and expected Play app-signing certificate.
*Steps* Install/update through Play rather than ADB · record delivered base/split
APKs, version, installer, and signing certificate · repeat B smoke and A-19 on
the Play-delivered build.
*Expect* Delivery selects compatible 64-bit splits, launches, preserves data on
track update, and behaves like the release candidate. Do not expect byte or
certificate identity with the directly distributed APK when Play App Signing
uses a separate app-signing key; compare each channel with its own declared
provenance. If no track exists, record N/A with the release-channel reason.
*Fail if* Play delivers an incompatible or non-launching split set, a track
update loses data, or the delivered signing certificate does not match the
provenance that channel declares.

**A-23 · A live capture is rendered in the device's own clock**
*Risk* A device at a non-zero UTC offset is the case that broke this; on a UTC
device the defect is invisible.
*Pre* Device time zone set to a non-zero offset of at least ±2 h. Record the
original in the mutation ledger.
*Steps* Start a full-device capture with *Follow* engaged · generate traffic ·
compare the newest rendered entry's time with the device clock
(`<ADB> shell date`) · read the zone rows in the session pane · then import a
file and compare a rendered row with the raw line behind it.
*Expect* Live rows render in the device's own clock, so the newest entry sits at
about *now* and *Follow* visibly tracks the live edge. The session pane names
both zones: storage in UTC, because that is the format `logcat` is asked for, and
rendering in the device's zone. An imported file keeps its own policy zone, so a
rendered row still agrees with the raw bytes behind it.
*Fail if* The newest live entry appears a whole UTC offset in the past, *Follow*
looks stopped on a device that is producing records, or an imported row disagrees
with its raw line.

**A-24 · Selection and source context survive live refresh and resume**
*Risk* Progressive snapshots replace the entry collection, and resume reattaches
the activity.
*Steps* During a live capture, select an entry and open *Entry* · let several
refreshes arrive · background the app for a minute and return · rotate · read the
source bytes again · repeat while the capture is still writing its sidecar.
*Expect* The selection and the timeline caret hold across every refresh — the row
is restored by entry id, not by position. Source context survives resume and
activity reattachment, retries an interrupted read, and can read a live sidecar
while the capture is still writing it. An interrupted read says so and offers
*Retry*.
*Fail if* A refresh clears the selection or the caret, the pane stays on "Reading
the source bytes…" with no timeout and no way to ask again, or a resumed app
cannot read bytes it read a minute earlier.

**A-25 · A restricted verdict is corrected when the device finally speaks**
*Risk* The absence of a foreign record is evidence, never proof, so the verdict
must stay revisable for the life of the capture.
*Pre* P1 — grant held and, when shown, consent allowed. Make the device as quiet
as normal user-visible controls permit: no traffic generator and no deliberate
app activity. Do not change permissions, suppress buffers, or filter the product's
input merely to force this branch.
*Steps* Start a capture and do nothing for at least 15 s, so the scope-decision
window can expire having seen only own-app records · read the status line, the
notice, and the session pane's *Log scope* row · then provoke foreign traffic
(§3.3, or open the camera) · re-read all three · stop, reopen the session, and
inspect the stored manifest.
*Expect* If the decision window expires without a foreign record, the early
own-app/restricted verdict is explicitly provisional. It may offer permission
and retry remedies, but it must not state that consent was declined unless the
platform supplied a reliable decline signal; quiet traffic alone cannot prove
that cause. When a foreign record arrives the verdict is **corrected to
full-device**, and the correction reaches the status line, source description,
the *Log scope* row, **and the stored manifest** — not only the screen. A
full-device verdict is never revised back.
If unavoidable system traffic resolves full-device before the window expires,
retain that observation but mark this branch Blocked and retry on a quieter
supported device; immediate correct resolution is not a product failure and is
not proof that the correction branch works.
*Fail if* Silence is presented as proof that consent was declined, the restricted
verdict latches, the correction appears on screen but not in the reopened
session's manifest, or the two verdicts oscillate.

---

## 7. Tier X — complex, stress, and soak scenarios

Purpose: the conditions that break real software — volume, duration, resource
pressure, and hostile timing. Each X scenario names its own instrumentation.

---

**X-01 · One million lines imported on the device**
*Risk* The headline claim.
*Pre* `large.txt` (1 M lines, ~86 MB) in Downloads. `gfxinfo` reset.
*Steps* Import it · **do not wait** — during ingest, pan the plot, zoom into a
burst, switch Plot/Split/Details, open the filter drawer, run a search, select an
entry, and open the *Entry* tab.
*Expect* The workspace stays interactive throughout. Progressive snapshots grow
the plot as data arrives. No interaction blocks for longer than the frame budget.
Final count matches the generator exactly. Throughput recorded.
*Fail if* Any interaction is unavailable during ingest, or the UI stops
responding for more than a second.
*Instrument* `gfxinfo` before/after, `meminfo` at 25/50/75/100 %, wall clock.

**X-02 · Five million lines — limits and storage**
*Pre* `xl.txt` (~430 MB) pushed; at least 8 GB free on `/data`.
*Steps* Import · monitor `df /data` and `meminfo` throughout · complete or fail
gracefully.
*Expect* Either it completes with correct counts, or it fails with an explicit,
recoverable, honest message. Memory stays bounded — the bounded channels must
prevent a fast source from growing memory without limit.
*Fail if* The process is killed by the low-memory killer, or storage fills with
no warning and no cleanup path.

**X-03 · Interaction under a message storm**
*Steps* Start a full-device capture · run the burst generator from §3.3 ·
while it floods, drive the UI continuously for 5 minutes: search, filter, zoom,
switch modes, page entries.
*Expect* Every interaction remains possible. The refresh rate is bounded by the
*Live UI refresh limit* setting rather than by the arrival rate. Counts keep up
or the product says it is behind — it does not quietly drop records.
*Fail if* The app becomes unusable during the storm, or record loss is silent.

**X-04 · Ring-buffer overflow and loss evidence**
*Pre* Dedicated device. Save `<ADB> logcat -g` in the mutation ledger.
*Steps* Shrink only the intended buffers where the device supports it · start a
capture · flood with the burst generator · clear the intended buffers
mid-capture · finish and then restore the exact recorded sizes (or reboot and
prove every size returned to the recorded baseline).
*Expect* Loss and discontinuity are **retained as evidence** in the session and
surfaced to the user, not smoothed over. The capture continues.
*Fail if* A gap appears in the data with nothing in the product saying so.

**X-05 · Four-hour capture (endurance)**
*Steps* Full-device capture, screen off, device on charge, left for 4 hours with
the device in normal use for part of it · then stop from the UI · then reopen.
*Expect* The capture runs to the end. Stop behaves exactly as B-12 describes at
this scale — this is the size at which the stop defect appeared. The finished
session reopens with a complete, verifiable manifest.
*Instrument* `dumpsys thermalservice` and `batterystats` at the end; record
throttling and battery attribution. Take `meminfo` hourly.
*Fail if* Memory grows without bound, the process is killed, or stop does not
resolve.

**X-06 · Overnight capture (soak)**
*Steps* As X-05 but 8–12 hours, unattended, off charge for the first half.
*Expect* Behaviour across naturally entered Doze windows matches the product's
declared background-capture contract. Any platform-caused suspension is marked
as a gap rather than silently presented as continuous. No attributable ANR,
crash, or unbounded growth. The session is finalizable in the morning.
*Instrument* Before and after: `meminfo`, `df /data`, time-bounded full/crash
logcat, and a bugreport/dropbox snapshot where available.
*Fail if* The app is dead in the morning, or the session cannot be finalized.

**X-07 · Doze and battery saver explicitly**
```shell
<ADB> shell dumpsys battery unplug             # Doze needs the device to look unplugged
<ADB> shell dumpsys deviceidle force-idle      # enter Doze
<ADB> shell dumpsys deviceidle unforce         # leave it
<ADB> shell dumpsys battery reset              # restore real charging state
<ADB> shell settings put global low_power 1    # battery saver on
<ADB> shell settings put global low_power 0    # restore; then verify dumpsys battery
```
*Expect* Behaviour under Doze and battery saver is defined and stated: either the
capture continues, or the product says it was interrupted. Nothing is silently
lost. Record whether each forcing command was accepted; OEMs may refuse it.
Always `unforce`, reset battery simulation, restore the original low-power
value, wake/unlock the device, and verify those states in cleanup.
*Fail if* Records are missing with nothing in the session or the UI marking the
interval, or the product claims continuous coverage across a platform-imposed
suspension.

**X-08 · Process death and restoration**
*Steps* Enable *Developer options → Don't keep activities* · exercise B-09,
A-13, and a live capture · then disable it and kill the process outright
by backgrounding the app, issuing `<ADB> shell am kill <PKG>` mid-capture,
verifying the old PID ended, and relaunching.
*Expect* Activity recreation preserves the workspace mode and open session where
the design says it should. A killed process leaves a **recoverable partial
session** that reopens with everything committed before the kill.
*Fail if* A partial session cannot be reopened, or reopening loses committed
data.

**X-09 · Low-memory pressure**
```shell
<ADB> shell am send-trim-memory <PKG> RUNNING_CRITICAL
```
*Steps* Send trim signals at each level during a 1 M-entry session, then open
several other heavy apps to create genuine pressure.
*Expect* The app sheds what it can and stays correct. Returning to it either
restores the workspace or reopens the session — either way, with correct counts.
*Fail if* Trim causes a crash or data loss.

**X-10 · Storage exhaustion during capture**
*Pre* Dedicated, backed-up device; battery >50%; at least 2 GB free before setup.
Record `df`, resolve one explicit filler path under `/data/local/tmp/`, and set a
hard safety reserve of the greater of 1 GB or 10% of `/data`. Never compute a
fill count from stale output and never fill `/data` to 100%.
*Steps* Create bounded filler files in increments, rechecking free space after
each · stop before the reserve · run a live capture toward the controlled limit
· remove only the exact recorded filler paths in `finally` · verify space is
recovered and reboot if device services remain unhealthy.
*Expect* The product detects the condition, says so, and leaves a recoverable
session. No corrupt manifest.
*Fail if* The session is left unverifiable, or the failure message is a raw
exception. Abort immediately if free space crosses the reserve or system apps
become unstable.

**X-11 · Clock and time-zone changes mid-capture**
*Steps* During a capture, change the device time zone; then move the clock
forward an hour; then back.
*Expect* On-device capture requests UTC from logcat and says so in the manifest.
Records are not clamped or reordered to hide the change; the timeline shows what
actually happened.
*Fail if* Timestamps are silently rewritten.

**X-12 · Deep zoom precision**
*Steps* On a 1 M-entry session, zoom to the maximum precision setting (down to
1 µs/px) · pan at that scale · Fit back out.
*Expect* Rendering stays correct and responsive; isolated bursts remain visible
at the configured minimum bar width; Fit returns exactly to the full session.
*Fail if* Bars vanish, the axis mislabels, or panning at depth stutters beyond
budget.

**X-13 · Paging to the end of a huge result set**
*Steps* With 1 M entries and no filter, page with `+500` repeatedly, then
*Load all*.
*Expect* Keyset paging never repeats or skips a record — verify by exporting and
checking for duplicates. *Load all* either completes or is explicitly bounded.
*Fail if* Paging duplicates records or scrambles after new data arrives.

**X-14 · Live capture with concurrent import**
*Steps* Start a live capture · while it runs, import `medium.txt` into a second
tab · work in both.
*Expect* Both progress. Neither starves the other into unresponsiveness. Tab
state stays separate.
*Fail if* One session's progress corrupts or stalls the other.

**X-15 · Rapid start/stop cycling**
*Steps* Start and stop a live capture 20 times in quick succession, varying the
run length from under a second to 30 s.
*Expect* Every cycle produces a well-formed session or an explicit refusal. No
orphan on-device `logcat` processes attributable to the app remain (compare
selected-device process snapshots). File-descriptor counts remain stable when
observable through `run-as` on Debug; on Release, use repeated PSS/process-state
samples and do not fail merely because SELinux denies `/proc/<pid>/fd`.
*Fail if* Handles or child processes accumulate.

**X-16 · Thermal throttling under sustained load**
*Steps* Run X-01 and X-03 back to back, off charge, until the device throttles.
*Expect* The product degrades gracefully — slower, not broken. No dropped
records, no crash, no unbounded queue.
*Instrument* `dumpsys thermalservice` sampled every minute.
*Fail if* Throttling produces dropped records, a crash, an unbounded queue, or a
hang rather than a slower run.

**X-17 · Interruption gauntlet**
*Steps* During a running capture with a 1 M-entry session open, in sequence:
incoming phone call · alarm · notification shade pull-down · power menu · recent
apps switch · another app in split-screen · screenshot · screen record · lock and
unlock.
*Expect* The capture survives all of them; the workspace is intact after each;
no visual corruption on return.
*Fail if* Any interruption ends the capture or leaves a broken frame.

Mark hardware-dependent steps N/A only with a reason (for example, no cellular
subscription for an incoming call); execute the rest of the gauntlet.

**X-18 · Reopen-while-finalizing race**
*Steps* Stop a large capture and, while it is still writing the index, attempt to
open the same session from *Recent sessions* and from the empty state.
*Expect* The product either waits, refuses clearly, or serves the finalized
result — never a torn read and never a duplicate tab over the same store.
*Fail if* Two tabs open over one session with different counts.

**X-19 · Phase-boundary kill matrix**
*Steps* In separate runs, kill the process during source materialization,
ingest, progressive snapshot publication, compaction, manifest/index write,
reopen, CSV export, portable archive creation, and share preparation. Use a
large deterministic corpus so each phase is observable. Relaunch after each.
*Expect* The previous committed state is recoverable; temporary files are either
resumed or safely discarded; no finished manifest points at missing/torn data;
no partial export is presented as complete. Recovery is idempotent.
*Fail if* Any phase leaves a finished manifest pointing at missing or torn data,
a partial export presented as complete, or a recovery that is not idempotent on
a second relaunch.

**X-20 · Reboot and forced-stop semantics**
*Steps* Reboot once with only finished sessions, once during an active capture,
and once after stop while finalization is running. Separately use Android's
*Force stop* and relaunch manually.
*Expect* Finished sessions and settings survive. Interrupted work is recoverable
to its last committed boundary or is clearly identified as partial. The app does
not claim background continuation across a reboot/force-stop. No automatic
launch violates Android force-stop semantics.
*Fail if* A finished session or setting is lost, interrupted work is presented as
complete rather than partial, or the app relaunches itself after Force stop.

**X-21 · High session count and cache churn**
*Steps* Create/import at least 100 small sessions plus several large ones; open,
close, search, export, and delete sessions in varied order; restart repeatedly;
exercise retention at the exact age/size boundaries.
*Expect* Recents/cache remain responsive and correctly ordered; duplicate names
remain distinguishable; cleanup removes only eligible, unpinned, non-capturing
sessions; disk usage converges after deletion; open tabs never point at deleted
storage.
*Fail if* Recents or the cache become misordered or unresponsive, cleanup removes
an ineligible, pinned, or capturing session, disk usage does not converge after
deletion, or an open tab points at deleted storage.

**X-22 · Repetition leak pass**
*Steps* For 100 iterations, open a deterministic session, run search/filter,
open/close every sheet, export a small scope, close the tab, and return to the
empty state. Every 10 iterations sample PSS, iteration time, open-session count,
product-reported cache/session size, device free space, and process thread/file-
descriptor counts where the OS exposes them. On the separate Debug diagnostic
pass, add `run-as` app-private `du` and file-descriptor evidence; do not pretend a
Release SELinux denial is a zero. Treat iterations 1–20 as warm-up; compare
rolling-window medians for 21–40, 41–60, 61–80, and 81–100, and allow a fixed
idle/collection interval before the final sample.
*Expect* Metrics settle into a bounded band after warm-up; legitimate caches
plateau. Open-session count returns to zero, temporary share/export files return
to the documented retained set, thread count returns to its steady band, and
response time does not progressively worsen.
*Fail if* Post-warm-up windows show a repeatable material positive trend with no
plateau and either final steady-window PSS/disk is >20% above the first steady
window without workload-retention justification, observable threads/handles/
files accumulate, or response times degrade progressively. A merely monotonic
noisy PSS sample is a trigger to repeat and diagnose, not a leak verdict by
itself. If neither the signed candidate nor the diagnostic pass can expose a
required resource signal, mark that assertion Blocked rather than inventing a
zero or inferring cleanup from access denial.

**X-23 · On-device capture is independent of the ADB transport**
*Steps* Start a full-device capture while connected · emit a start marker ·
disconnect USB/Wi-Fi ADB completely for 10 minutes while using the phone · stop
and finalize from the device UI · reconnect, re-establish identity, and export
the session/evidence.
*Expect* The on-device source continues without the host transport, the UI never
claims an ADB failure, and the session covers the disconnected interval. Any
evidence unavailable during disconnection is collected after reconnect and is
not confused with absence of product activity.
*Fail if* The capture stops, stalls, or blames ADB while the host transport is
absent, or the finished session does not cover the disconnected interval.

**X-24 · Short captures finalize**
*Risk* Publishing a progressive snapshot and finalizing the session both rewrite
the manifest; overlapping writes once failed the whole ingest, reliably, on
captures short enough for the two to coincide.
*Steps* Run 30 captures whose durations sweep the overlap region — roughly 0.5 s
to 15 s, with several at about 1 s, 2 s, 3 s, 5 s, and 10 s · stop each from the
UI · reopen and verify every resulting session.
*Expect* Every capture finalizes; every session reopens and verifies. No ingest
fails with a file-access error, and no session is left claiming to be live.
*Fail if* Any duration in the sweep fails to finalize, or leaves a session that
cannot be reopened or verified.

**X-25 · Buffer coverage and live-tail start**
*Risk* A narrower buffer set and a ring-buffer replay both look like a working
capture.
*Pre* P1, on a device whose ring buffers already hold substantial history —
record `<ADB> logcat -g` and save a bounded timestamped tail with
`<ADB> logcat -b all -d -v epoch -t 200`. Probe `events` and `radio` separately
with `-d -t 1`; record unsupported, empty, or access-denied buffers rather than
assuming every device exposes them.
*Steps* Start an on-device capture · provoke traffic in buffers outside `main`:
`events` by launching and switching apps, and `radio` by toggling airplane mode
when that buffer is supported and doing so is allowed by the mutation ledger ·
run `<ADB> logcat -b all -v epoch` in a dedicated host evidence stream over the
same marker-bounded interval · compare per-buffer/tag markers and compare the
capture's first record time with the recorded start time and pre-start tail.
*Expect* A full-device on-device capture carries records from every supported
buffer that the app UID may read rather than a narrower `main`-style subset.
`events` and `radio` are assertions only when the probes and generated traffic
prove they are observable in this device state. Asking for every buffer does not
bypass the permission model, so a difference from the host-shell capture is
acceptable when a recorded platform/UID restriction explains it; an unexplained
difference is not. The capture starts at the **live edge**: its first record
belongs to the start window, not the saved pre-start history, so the app does not
spend minutes ingesting old ring-buffer contents while the present goes unshown.
*Fail if* A buffer visible to the host capture is absent from the on-device
capture over the same interval with no platform restriction that accounts for it,
or the session opens holding history from before the capture began.

---

## 8. Tier U — UX/UI and accessibility passes

Purpose: the product must be *usable*, not merely functional. Run each pass over
a session with real data (at least `medium`), and once more with a live capture
running, because the live status band changes the layout budget.

---

**U-01 · Orientation matrix**
Portrait → landscape → portrait, in each workspace mode, with and without the
filter drawer open, with and without a capture running.
*Expect* Nothing is clipped, nothing overlaps, the axis labels stay inside the
plot, the minimap survives in the short-viewport composition, and the chosen mode
is preserved. In landscape the analysis pane keeps a usable share of the width.
*Fail if* Anything is clipped or overlapped in any combination, the axis labels
leave the plot, the minimap disappears from the short-viewport composition, or
rotation changes the workspace mode.

**U-02 · Size-class boundaries**
Force narrow and short viewports (split-screen, freeform where available, a small
device if one is at hand). Cross the compact-width (~380 dp) and compact-height
(~520 dp) breakpoints in both directions. On a foldable, also fold/unfold and
move the app across displays/postures while capture runs.
*Expect* Each side of a breakpoint is a usable layout; crossing does not lose
user intent (display mode, filters, selection).
*Fail if* Either side of a breakpoint is unusable, or crossing one loses the
display mode, the filters, or the selection.

**U-03 · Soft keyboard**
Open the filter drawer, focus the query field, type, submit, dismiss — in both
orientations, and with the drawer at each size class.
*Expect* The field stays mounted and focused while the IME animates in. The
drawer's *Reset* and *Done* stay above the keyboard. The keyboard's action key
applies the filter. Closing the drawer puts the keyboard away.
*Fail if* The query field cannot be typed into by touch in either orientation.

**U-04 · System text size (accessibility scale)**
Set the device text size to each step from smallest to largest
(`Settings → Display → Font size`, or
`<ADB> shell settings put system font_scale 1.3`). Record and restore the exact
original scale.
*Expect* The app honours the platform scale as its baseline; the in-app *Text
scale* multiplies it rather than replacing it. At the largest step nothing is
clipped, no control loses its label, and the mode selector row keeps its
geometry.
*Fail if* The app is pixel-identical across two different platform scales, or the
largest step clips a label, hides a control, or reflows the mode selector row
into a second row.

**U-05 · Text size changed *during* a live capture**
*Expect* The capture keeps running, the controls stay present, and the views
rebuild at the new scale. A million-entry session must not blank out under the
word "Ready" while it reopens from disk.
*Fail if* The capture ends, the live controls disappear, or the session blanks
while it reopens from disk.

**U-06 · Theme**
System / Light / Dark, plus *Prefer high-contrast presentation*, switched while
the workspace is open and while a capture runs.
*Expect* Severity colours stay distinguishable in all four combinations; high
contrast raises selection and focus contrast rather than relying on colour alone;
no white band at the system bars in dark mode.
*Fail if* Two severities become indistinguishable in any combination, a theme
change needs a restart, or a system bar shows a band from the other variant.

**U-07 · Safe area, cutout, and gesture insets**
Test with gesture navigation and with three-button navigation; on a device with a
display cutout, in both orientations; with a hidden or auto-hiding status bar if
the device offers it.
*Expect* Content is inside the safe area, the top level paints edge to edge, and
hit testing matches what is drawn — no control that responds a few pixels away
from where it appears.
*Fail if* Content is drawn under a cutout or a system bar, or a control responds
at coordinates other than where it is drawn.

**U-08 · Touch targets and one-handed reach**
Measure the primary controls (mode selector, *Fit*, *Filters*, *Live*, *Stop
capture*, tab chips, analysis tabs) against a 48 dp minimum, and check that the
most frequent actions are reachable with one thumb in portrait.
Convert screenshot pixels using the recorded effective density; do not compare
pixels directly to dp. Check spacing between adjacent targets and repeat with
TalkBack enabled, which can change gesture behaviour.
*Fail if* Any primary control is under 48 dp in either dimension or adjacent
targets cannot be selected reliably without zooming.

**U-09 · TalkBack (screen reader) pass**
Enable TalkBack. Navigate the whole product by swipe only: empty state → open a
log → plot → filters → entries → entry → More sheet → each dialog.
*Expect* Every control has a name and, where it matters, a description. The More
sheet is ordinary content — every command in it is reachable and announced. A
command that needs an open session says so rather than being silently inert. The
timeline announces its purpose and its shortcut help.
*Fail if* Any command exists on screen but not in the accessibility tree — dump
it with `<ADB> shell uiautomator dump` to check.

**U-10 · Switch Access / keyboard navigation**
First use Android Switch Access (or the OEM's equivalent scanning control) to
scan, activate, scroll, open/close layers, and complete the primary workflow.
Then attach a USB or Bluetooth keyboard, drive the app by Tab/Shift+Tab, arrows,
Enter/Space, and Back/Escape, and exercise the documented shortcuts in
[`KEYBOARD.md`](KEYBOARD.md) that apply on Android. If no scanning service is
available under device policy, retain that proof and mark only the Switch Access
subpass N/A; the keyboard pass remains required.
*Expect* Scan and keyboard order are sensible and stable; every action is
operable without touch; the visible focus indicator remains clear everywhere;
scroll containers advance without skipping their contents; and no dialog,
sheet, drawer, plot, or entry list traps focus.
*Fail if* Either available input route skips an actionable control, activation
does the wrong thing, focus is invisible, a scrollable region cannot be
traversed, or any layer traps focus.

**U-11 · Locale and layout direction**
Switch the device to a right-to-left locale (`ar`) and to a locale with a
different decimal separator (`de` or `cs`), while the app is running.
*Expect* The app keeps a fixed display culture for its own numbers and
timestamps, does not crash, does not mirror into an unusable layout, and keeps
the capture running through the change.
*Fail if* The app crashes, mirrors into an unusable layout, adopts the device's
number or date conventions inside its own interface, or ends the capture on the
locale change.

**U-12 · Notice lane**
Trigger notices from several sources: a copy action, a template mute, a scope
resolution, a failed export, a storage warning.
*Expect* Every one lands in the notice lane, is dismissible, does not stack into
an unreadable pile, and never steals the entries list's floor.
*Fail if* A notice is lost, cannot be dismissed, stacks into an unreadable pile,
pushes the entries list below its floor, or a confirmation of something durable
disappears on a timer before it has been read.

**U-13 · Empty, loading, partial, and failed states**
Visit deliberately: a session with zero entries after filtering; a bar with no
entries after filtering; an entry that cannot be read; a revoked/short/timeout
provider stream during materialization; a partial session after X-19; and a
deleted original after a provider-backed import has completed.
*Expect* Each state explains itself in product language and offers the next
action. None is a blank pane or raw exception. A completed Android import that
materialized/embedded its source remains reopenable after the provider's
original is deleted; it must not retain an accidental dependency on Downloads.
*Fail if* Any state renders as a blank pane or a raw exception string, or a
completed import stops reopening once the provider's original is deleted.

**U-14 · First-run comprehension (fresh eyes)**
Hand the device to somebody who has not seen VisualCat, with one instruction:
"find the crash in this log" (use `crashy.txt`). Do not help. Time them and note
every hesitation.
*Expect* They reach a `FATAL EXCEPTION` record within 3 minutes using the plot,
search, or templates. Record every place they stalled — those are the findings.
*Fail if* The participant cannot reach a fatal record inside the time box, or
abandons the task.

**U-15 · Visual regression sweep**
Capture a fixed screenshot set at the end of every full run: empty state,
initial import/progress, workspace in each mode, filter drawer open, More sheet,
each dialog, a live capture running, a live capture stopped, and a failure card.
Keep them per build and diff against the previous run.

Keep device, resolution, density, font scale, locale, theme, navigation mode,
seeded data, and viewport fixed. Mask only documented dynamic regions (clock,
rate, run id), retain the masks with the baseline, and visually review diffs —
pixel difference alone is neither pass nor fail.
*Fail if* A diff is accepted without visual review, or a baseline is regenerated
to make a diff disappear.

**U-16 · Magnification, colour assistance, and reduced motion**
Run the primary workflow with system magnification, colour correction/grayscale,
high-contrast text where offered, and animation scales at 0× and a slow value.
*Expect* Severity is never communicated by colour alone; focus/selection remain
visible; magnification does not make essential controls unreachable; disabled
animations do not suppress state changes; slow animations do not expose stale
or touch-through layers. Restore every setting from the mutation ledger.
*Fail if* Severity is legible only by hue, focus or selection disappears under
magnification or colour correction, an essential control cannot be reached at
magnification, or a state change is lost when animations are off.

**U-17 · Dynamic accessibility announcements**
With TalkBack active, start/stop capture, trigger an error and a completion
notice, change filters, load another page, and finish a long import.
*Expect* Important state changes are announced once, in useful order, without a
per-message announcement storm. Focus remains on the initiating control or moves
to the resulting dialog/error intentionally; background count refreshes do not
continually interrupt reading.
*Fail if* An important state change is never announced, is announced repeatedly
enough to prevent reading, or focus moves somewhere the user did not initiate.

**U-18 · Gesture conflict and accidental repetition**
Exercise slow/fast pinch, two-finger pan, edge-back near plot gestures,
double-taps, long-presses, pointer cancellation, and rapid repeated taps on
destructive/expensive actions.
*Expect* System Back remains available at the edge; plot gestures do not trigger
unrelated controls; duplicate taps are debounced or safely idempotent; a lost
pointer cannot leave capture/selection controls pressed or a scrim touchable.
*Fail if* A plot gesture activates an unrelated control, edge Back is swallowed,
a repeated tap performs a destructive or expensive action twice, or a lost
pointer leaves a control latched or a scrim touchable.

**U-19 · Product identity is independent of device theming**
*Risk* Fluent adopts the platform accent, so the product can take the device's
Material You colour — and a screenshot taken on another device cannot catch it.
*Steps* Set the device's Material You / wallpaper accent to something loud (a red
or an orange) and apply any OEM theme pack offered · restart the app · inspect
selection highlights, focus borders, tab underlines, list backgrounds, and the
command band, in both light and dark · switch theme with the app running, and
again from a cold start in light mode.
*Expect* Selection, focus, and list surfaces come from the product palette in both
variants, not from the device accent: a red device accent must not put an
error-looking tint under every selected row. The command band follows the variant
rather than painting one fixed slab between light system bars, and the platform's
own status and navigation bars follow it too. A theme change repaints the whole
product without a restart, and a cold start in light mode is never served dark
values.
*Fail if* Any product surface takes the device accent, a theme change needs a
restart to complete, or a cold start shows one variant's values under the other's
chrome.

---

## 9. Tier H — host-side scenarios against the same device

Purpose: the desktop and CLI paths that read the *same* physical device, plus the
parity assertions that prove the engine agrees with itself across surfaces.

---

**H-01 · Device discovery**
```shell
dotnet run --project src/VisualCat.Cli -c Release --no-build -- adb-devices
```
*Expect* The connected device appears with serial, state, model, product, and
transport id. Compare against §2.2 — the two must agree. In the desktop app,
*ADB live* → *Refresh devices* shows the same set.
*Fail if* The CLI and the desktop dialog disagree about the attached set, or
either reports a device the pre-flight did not confirm.

**H-02 · Device-state surfacing**
Produce each state and check both the CLI and the desktop dialog: `device`,
`unauthorized` (revoke USB debugging authorisation on the device),
`offline` (pull the cable mid-enumeration), and absent.
*Expect* Each state is reported as itself, not retried as a parse failure or
presented as an empty capture.
*Fail if* Any state is reported as a parse failure, as an empty capture, or as a
silent retry loop.

**H-03 · Desktop ADB capture**
*Steps* Open *ADB live* · select the device · vary buffers (default
`main,system,crash`, then `all`, then a single buffer) · set a pre-roll · set a
stop-after duration · start · stop early on one run, let another reach its
duration.
*Expect* Buffers and pre-roll are honoured; an empty buffer selection is refused
with "Select at least one buffer"; a capture that reaches its stated duration
gets the same clear account of its ending as one stopped by hand.
*Fail if* A buffer or pre-roll choice is not reflected in the captured data, an
empty buffer selection is accepted, or a duration-ended capture is accounted for
differently from a hand-stopped one.

**H-04 · CLI capture**
```shell
dotnet run --project src/VisualCat.Cli -c Release --no-build -- capture-adb --serial <serial> --duration-seconds 60 --output minute.vcat
dotnet run --project src/VisualCat.Cli -c Release --no-build -- verify minute.vcat
dotnet run --project src/VisualCat.Cli -c Release --no-build -- stats minute.vcat
```
*Expect* A portable session at the printed absolute path; `verify` reports valid;
`stats` agrees with what the desktop app shows for the same session.
*Fail if* `verify` reports invalid, the printed path is not absolute, or `stats`
disagrees with the desktop view of the same session.

**H-05 · Reconnect and resume**
*Steps* Start a host ADB capture · unplug the cable for 30 s · plug it back in ·
continue for another minute · stop.
*Expect* Bounded reconnect with `-T` resume. The gap is retained as evidence. The
session remains verifiable. `adb` child processes are cleaned up on stop — check
for orphans on the host.
*Fail if* The gap is absent from the session, the session fails verification, or
an `adb` child process survives the stop on the host.

**H-06 · Cross-surface parity (the important one)**
*Steps* Capture the same span two ways where possible, and in all cases:
1. Take a session created **on the device**.
2. On Release, share/export it as a portable archive; on Debug, optionally pull
   the private session as an additional diagnostic (Appendix A).
3. `vcat verify` it, then `vcat stats`, `vcat templates`, `vcat search`.
4. Open the same session in the desktop app.
*Expect* Entry counts, per-severity counts, time range, template identities, and
search hit counts are **identical** across on-device UI, desktop UI, and CLI.
Parallelism must not change any result.
*Fail if* Any number differs by even one.

**H-07 · Round-trip through portable transport**
*Steps* Export portable from the device → import on the host → export portable
from the host → import back on the device.
*Expect* Counts and templates identical at every hop; raw bytes byte-identical;
no traversal or link entries in any archive. Inspect names, uncompressed sizes,
and external file attributes with an archive tool that exposes all three; a
filename-only listing cannot prove that an entry is not a link.
*Fail if* Any hop changes a count, a template identity, or a raw byte, or any
archive contains a link or traversal entry.

**H-08 · Export equivalence**
*Steps* Export the same filtered scope as CSV from Android and CLI, and share a
portable archive from Android. Verify the portable archive with the CLI. Then,
from that archive, exercise CLI/desktop `raw`, `templates-md`, `templates-csv`,
`stats-md`, and `stats-csv` exports.
*Expect* Android and CLI CSVs match record-for-record after accounting for the
explicit encoding/order/scope. The portable archive verifies and preserves raw
bytes. CLI/desktop reports agree with CLI `stats`. Android is not expected to
offer the rich desktop/CLI report menu.
*Fail if* The two CSVs differ beyond the declared encoding, order, and scope, or
a report contradicts CLI `stats` for the same session.

**H-09 · Concurrent host capture and on-device capture**
*Steps* Run a host ADB capture and an on-device capture of the same device at the
same time for 5 minutes, bracketing the comparison interval with unique run
markers and recording each source's buffers/format/start semantics.
*Expect* Both work. Neither starves the other of `logd`. Within the common
marker-bounded interval, differences are explainable by scope, buffers, start
time, platform filtering, or explicit loss evidence. Do not demand global
subset/superset equality from captures with different contracts.
*Fail if* Either capture stalls, or a difference inside the marker-bounded
interval cannot be explained by scope, buffers, start time, platform filtering,
or recorded loss evidence.

---

## 10. Tier P — privacy, security, and negative scenarios

---

**P-01 · No network traffic**
*Steps* Run a full session — import, capture, export, share — with the device in
airplane mode; then repeat with network on. Inspect the installed manifest for
`INTERNET` and network-security settings, resolve the package UID, and compare
UID-attributed network counters before/after. On a dedicated device, supplement
with a VPN capture or packet trace that covers IPv4/IPv6 and bypasses neither
cellular nor DNS; a normal HTTP proxy alone is not proof.
*Expect* No Internet capability or outbound traffic attributable to the app.
Sharing may cause the **receiving app** to use the network; keep that attribution
separate.
*Fail if* The candidate unexpectedly requests Internet access or its UID sends
traffic during the bounded workflow.

**P-02 · Nothing leaves the app without an explicit act**
*Expect* Log content reaches another app only through the share sheet or an
explicit export the user chose. The FileProvider exposes only the share
directory; `usesCleartextTraffic` is false; `allowBackup` is false. Confirm from
the **installed APK** with `apkanalyzer manifest print` or `aapt2 dump xmltree`
and inspect the packaged `res/xml/file_paths.xml`; `dumpsys package` alone does
not prove every manifest/resource value.
*Fail if* The installed manifest or the packaged `file_paths.xml` exposes
anything beyond the share directory, or either flag is not as declared.

**P-03 · Diagnostic bundle redaction**
Covered functionally by A-16; here, read the bundle line by line and confirm no
device identifiers, account names, or log payloads appear that the confirmation
did not declare.
*Fail if* The bundle contains a payload, an identifier, or a path that the
confirmation did not declare.

**P-04 · Archive safety**
*Steps* Craft a `.vcat.zip` containing a path-traversal entry (`../../evil`) and
a symlink entry, plus one that inflates far beyond its declared bounds. Attempt
to open each on the device.
*Expect* Each is refused with a clear message. Nothing is written outside the
session directory. Confirm with `find` under `run-as` on Debug and with before/
after app storage plus product-visible sessions on Release. Run zip-bomb cases
with free-space and time limits on a dedicated device; abort before system
stability is threatened.
*Fail if* Any crafted archive writes outside the session directory, is accepted
silently, or exhausts storage or time before being refused.

**P-05 · Untrusted content rendering**
*Steps* Import a log whose messages contain ANSI escapes, terminal control
characters, extremely long single tokens, RTL override characters, and
zero-width joiners.
*Expect* They render as text. No control character changes the layout of anything
outside its own row, and no RTL override flips surrounding UI.
*Fail if* A control character alters the layout of anything outside its own row,
or a direction override flips the surrounding interface.

**P-06 · Permission revocation mid-capture**
Covered as state P4 in §2.4 — record the behaviour precisely; it is a security
boundary as well as a UX one.
*Fail if* The product goes on claiming a scope it no longer holds, or the
revocation loses data already committed.

**P-07 · Another app's files**
*Steps* Attempt to open a `content://` URI whose permission has already been
revoked, and a `file://` path outside the app's reach.
*Expect* A clear failure, not a crash, and no attempt to read around the
platform's decision.
*Fail if* The app crashes, hangs, or reaches the content by a route the platform
had closed.

**P-08 · Uninstall leaves nothing behind**
*Steps* Uninstall · check for residue in `/sdcard`, `/data/media/0/Android`, and
any shared directory the app touched.
*Expect* App-private data is gone with the package. Anything the user
deliberately exported to shared storage remains — that is correct — and nothing
else does. Re-check that `READ_LOGS` is gone.
*Fail if* App-private data survives uninstall, or an undeclared file is left in
shared storage.

**P-09 · Exported component and intent boundary**
*Steps* Inspect the merged installed manifest for exported activities/providers,
authorities, intent filters, and permissions. Send `ACTION_VIEW` with missing,
malformed, oversized, wrong-MIME, duplicate, revoked, and unsupported-scheme
URIs; vary flags and ClipData. Launch from stopped and running states.
*Expect* Only intended entry points are exported. Malformed/unreadable input is
rejected without crash or path escape; duplicate delivery is idempotent; MIME
metadata is treated as a hint, not trusted over content; no component exposes an
unprotected write path.
*Fail if* An unintended component is exported, malformed input crashes the app or
escapes its directory, repeat delivery is not idempotent, or declared MIME
metadata is trusted over the actual content.

**P-10 · Temporary share-grant lifetime**
*Steps* Share a portable archive to a controlled receiver, read it while the
grant is active, dismiss/cancel a second chooser, restart/force-stop VisualCat,
and attempt to reuse previously captured URIs from an unrelated app.
*Expect* Only the chosen receiver gets read access needed for the share; no write
grant is issued; canceled sharing discloses nothing; stale cache files age out;
an unrelated app cannot enumerate the provider or guess another shared file.
*Fail if* A write grant is issued, a cancelled share still discloses the file, a
stale share file never ages out, or an unrelated app can enumerate or reach
another shared file.

**P-11 · Sensitive display and clipboard handling**
*Steps* Put unmistakably sensitive synthetic text in a log, view/copy it, inspect
the clipboard preview/notification behaviour on supported APIs, move the app to
Recents, and take a screenshot/screen recording.
*Expect* Behaviour matches the documented privacy contract. Copy is always an
explicit act and copies exactly the selected scope. If screenshots/Recents are
not intentionally protected, record that as a known product policy rather than
assuming secrecy. No real secrets are used in this test.
*Fail if* A copy takes more than the selected scope, or observed behaviour
contradicts the documented privacy contract without being recorded as a finding
or a known policy.

**P-12 · Diagnostic/evidence denial of service**
*Steps* Exercise huge diagnostic metadata, many sessions, long names, a full
share cache, repeated canceled exports, and low-space bundle creation.
*Expect* Size/time limits are enforced; temporary files are cleaned; diagnostic
generation cannot evict or corrupt source sessions; failure does not include raw
payloads or private paths in an error shown to another app.
*Fail if* A size or time limit is unenforced, a temporary file is left behind, a
source session is evicted or corrupted by diagnostics, or an error handed to
another app carries a payload or a private path.

**P-13 · Android user/profile isolation**
*Pre* A secondary user or managed profile is available without weakening a real
work profile's policy.
*Steps* Install/use the app separately in two users/profiles; create distinctive
sessions and grants in each; switch users; attempt to open the other profile's
URI/session; uninstall from only one profile.
*Expect* App-private sessions, settings, recent lists, FileProvider authorities,
and development grants are isolated according to Android user/profile rules.
One profile cannot enumerate or read the other's logs/exports without an
explicit platform-mediated share. If profile creation is unavailable, record
N/A and the policy/hardware reason.
*Fail if* One profile can read the other's sessions, exports, recents, or grants
without an explicit platform-mediated share.

---

## 11. Tier R — regression pack for previously shipped defects

Every one of these was a real, user-visible defect, recorded in
[`CHANGELOG.md`](../CHANGELOG.md). Many are quick to re-check; scale-dependent,
accessibility, and device-state rows are deliberately not. They are still cheaper
than shipping the defect again. Run the whole pack before every release, assigning
the long rows to the Full, Soak, and Accessibility schedules.

*First fixed in* names the release whose changelog records the fix, so a tester
knows which builds to be suspicious of and where to read the full account. Most
of these are findable only on a physical device: they depend on the device's
accent, clock, locale, log buffers, consent sheet, or screen reader. Re-derive
this table from the changelog whenever a release adds a user-visible fix — a
regression pack that stops growing stops protecting.

| ID | Guard | Procedure | Pass condition | First fixed in |
|---|---|---|---|---|
| **R-01** | Stop capture is answered and sticky | B-12 at ≥ 4 hours and ≥ 500 000 lines (X-05) | The button never springs back; the status never returns to "Capturing"; the ending is accounted for and the controls disappear | Unreleased |
| **R-02** | Scope is not claimed falsely | B-10 (P0) and B-11 (P1), inspecting the status line, source description, notice, session details, **and** the stored manifest | All five agree; the time-stamped capture title makes no false scope claim, and an own-app source is never called full-device | 2.0.5 |
| **R-03** | Slow consent still yields full-device | On a supported device/API that presents the sheet, state P3 — wait ≥ 30 s | Resolves to full-device; the scope clock starts at the first byte. No sheet means N/A for this device plus a release-matrix gap, never a Pass | 2.0.5 |
| **R-04** | PID parsing under `-v threadtime,UTC` | Any full-device capture | The zone offset is never read as a PID; the app's own records are not counted as foreign | 2.0.5 |
| **R-05** | Text-size change does not kill a capture | U-05 | The capture continues; Follow and Stop remain; no ten-second blank reload | 2.0.4 |
| **R-06** | Entries list keeps its floor | B-09 in Split and Details, with a notice showing | ≥ 4 rows in Split, ≥ 6 in Details, even with a notice | 2.0.4 |
| **R-07** | Captures are distinguishable by name | Two on-device captures open at once | Tab chips, *Recent sessions*, *Session cache*, and the export filename all carry the capture's start time | 2.0.4 |
| **R-08** | Settings popups stay with their control | A-17, tapping *Default export order* on the phone | The control answers in place as segments; the form does not scroll away | 2.0.4 |
| **R-09** | Workspace actions reach the notice lane | A-18 and a template mute | Every action reports its result where the user is looking | 2.0.4 |
| **R-10** | More actions are accessible | U-09 with TalkBack, plus `uiautomator dump` | All commands present in the accessibility tree; disabled commands say why | 2.0.4 |
| **R-11** | Dialogs work on the phone | Open *Recent sessions*, *Appearance & timeline*, *Session cache*, the diagnostic-bundle confirmation | Each presents as an in-page card and returns a result | 2.0.4 |
| **R-12** | The empty state is useful | B-02 | Up to four device-held captures, one tap from a cold start | 2.0.4 |
| **R-13** | `Fit` is one tap from the plot | B-08 | Present beside the mode selector; the row's geometry does not shift when it hides | 2.0.4 |
| **R-14** | The capture is explained before Android asks | B-10 first run after a clear | The pre-prompt appears once and is remembered | 2.0.4 |
| **R-15** | A failed import shows a reason, not a hollow workspace | A-09 | Failure card with reason, remedy, and two actions; no desktop-only advice | 2.0.4 |
| **R-16** | Filter drawer survives the keyboard | U-03 | The focused query field stays mounted; the footer stays above the IME | 2.0.4 |
| **R-17** | Minimap survives the short viewport | U-01 in landscape | A 26 px minimap remains in the plot column | 2.0.4 |
| **R-18** | A view query cannot fail a finished capture | X-05, running a search as the capture finalizes | The finished capture is not reported as failed | Unreleased |
| **R-19** | The Back gesture falls through when nothing is open | B-15, with every layer closed | Back leaves the app; the workspace never claims a press it cannot answer | 2.0.4 |
| **R-20** | A live capture is read in the device's own clock | A-23 on a device at ≥ ±2 h offset | The newest entry sits at about *now*, Follow tracks the live edge, and the session pane names both zones | 2.0.4 |
| **R-21** | A live capture finalizes, short ones included | X-24 | Every duration in the sweep finalizes; no manifest write fails the ingest | 2.0.4 |
| **R-22** | The status stops claiming arrivals after silence | B-16 in P0, left idle for two minutes | The rate is measured over the last second, and a heartbeat says how long the source has been quiet | 2.0.4 |
| **R-23** | Follow belongs to a running capture | A-14, then stop the capture | *Follow* and *↓ New data* go when the source closes; re-engaging Follow opens a window on the live edge instead of keeping a whole-session span | 2.0.4 |
| **R-24** | Session-dependent commands are disabled without a session | B-01, then the More sheet with nothing open | Share, Export CSV, and Save are disabled and say why; the empty state offers no share link that cannot work | 2.0.4 |
| **R-25** | A screen reader hears entries, not record dumps | U-09 over entry rows, Insights, and both stored-session lists | Rows announce level, tag, time, and message; no session guid, raw span, or private storage path is read aloud | 2.0.4 |
| **R-26** | A sheet is modal to accessibility, not only to touch | U-09 with the More sheet open | Assistive technology cannot walk past the scrim into the workspace behind it | 2.0.4 |
| **R-27** | The product owns its accent | U-19 with a loud device accent | Selection, focus, tab underline, and list surfaces come from the product palette, not from Material You | 2.0.4 |
| **R-28** | A theme change repaints the whole product | U-19, switching with the app running and from a cold start in light | No surface keeps the other variant's values, and no restart is needed | 2.0.4 |
| **R-29** | An import ends showing the whole session | B-03 and X-01 | An untouched viewport follows the session; the first zoom or pan hands it to the reader for good | 2.0.4 |
| **R-30** | Closing a tab cannot crash the workspace | A-05, closing tabs during and after ingest | No `ObjectDisposedException`; the application survives every close | 2.0.4 |
| **R-31** | The source pane always resolves | A-24 | Every read ends in bytes, an explicit interruption, or a failure offering *Retry* — never a permanent loading line | 2.0.4 |
| **R-32** | Double-tap zoom does not also re-scope the list | B-08, double-tapping the plot | The gesture zooms only; the entry table and the chip bar are unchanged | 2.0.4 |
| **R-33** | The time axis is a scale, and stays inside the plot | U-01 and X-12 | Axis labels are never drawn under the minimap; a viewport too narrow for two ticks labels its own ends | 2.0.4 |
| **R-34** | Contextual action slots are stable | X-13, as *Load next 500* appears | *Copy raw* does not move; two taps in the same place hit the same control | 2.0.4 |
| **R-35** | Numbers and dates use the interface's own culture | U-11 on a locale with different separators | ISO dates and interface-culture numbers throughout, not the device's conventions mixed into an English UI | 2.0.4 |
| **R-36** | Counts state their scope in visible text | A-02 with a search active | Facet and template counts name their scope on screen, not only in a tooltip a touch device never shows | 2.0.4 |
| **R-37** | A nearly empty capture does not over-claim precision | B-16 in P0 with one or two entries | *Fit* is clamped to the resolution the plot has pixels for; the axis does not print one instant as two labels | 2.0.4 |
| **R-38** | The displayed version tracks the build | B-01 | The identity line's version matches the installed `versionName`, and a non-release build says so in the version itself | 2.0.4 |
| **R-39** | A live refresh does not deselect the entry being read | A-24 | Selection and timeline caret are restored by entry id across every refresh | 2.0.3 |
| **R-40** | Android source context survives resume and reattachment | A-24, backgrounding during a capture | Reads survive reattachment, retry when interrupted, and can read a live sidecar the capture is still writing | 2.0.2 |

---

## 12. Execution schedules

| Schedule | When | Contents | Time |
|---|---|---|---|
| **Smoke** | Every device build | B-01, B-03, B-04, B-10 or B-11, B-12, B-13, B-16 direct-start pass | ~45–60 min |
| **Standard** | Before a candidate build is shared | All B; R except the four-hour R-01/R-18 procedures; U-01, U-03, U-04, U-06, U-19; X-01; X-24 | ~8–12 attended h |
| **Upgrade** | Every schema/settings/storage change and every release candidate | A-19 and, where applicable, A-22; then B-03, B-13, B-14, H-06 on migrated data | ~3–5 attended h |
| **Full** | Before a release tag | Every applicable tier except X-06; reachable P0–P4; exact signed candidate is authoritative, with a separate Debug diagnostic subset where private evidence is required | ~5–8 person-days plus unattended machine time |
| **Soak** | Before a release tag, on a dedicated device | X-05, X-06, X-07, X-16, X-22; do not overlap workloads on one device | ~20–30 elapsed h/device, including ~6–10 attended h and review |
| **Parity** | Whenever the engine, store, or parser changes | All H, plus X-25 and A-23 | ~4–6 attended h |
| **Accessibility** | Before a release tag, and after any layout change | All U, including a fresh participant for U-14 | ~6–10 attended h plus participant availability |
| **Device expansion** | When a new device or API level is supported | B + U + X-01 + X-05 on the new device | ~12–18 elapsed h, including the four-hour endurance run |

These are planning ranges, not pass criteria. Data volume, device speed, thermal
recovery, artifact reinstalls, evidence review, and defect retries can extend
them. Parallel execution is valid only on distinct physical devices with
independent run headers and evidence roots; two workloads sharing one phone are
not parallel coverage. A Full result is an aggregation of those independently
identified runs, not one ambiguous multi-day row. Standard intentionally defers
R-01 and R-18 at their required X-05 scale; they must pass in Full/Soak before
release.

Order within a run: pre-flight → B → R → U → A → H → non-destructive P → X →
destructive P. Volume, thermal, storage-pressure, archive-bomb, and soak
scenarios go last because they contaminate later measurements. Restore and
re-verify the device between scenarios that require incompatible global states;
do not chain them merely to save setup time.

---

## 13. Recording results and exit criteria

### 13.1 Run header — fill in before the first scenario

```text
Run id:            <date>-<device>-<build>
Tester:
Date/time (UTC):
Repository commit / dirty state:
Artifact origin / absolute path:
Artifact SHA-256 / bytes:
Signing certificate SHA-256:
Device manufacturer / model:
Serial:                           ro.serialno
Android release / API:            ro.build.version.release / .sdk
ABIs:
Build fingerprint:                ro.build.fingerprint
Screen / density:                 wm size / wm density
Refresh rate / navigation mode:
Locale / time zone / 12-24 h:
Font scale / theme / animation scales:
Battery / charging / thermal state:
Free space on /data and /sdcard:
Network state / proxy / VPN:
App versionName / versionCode:    dumpsys package
Build type:                       Debug (-t:Install) | Release APK | Play build
Install mode:                     clean | upgrade-preserving-data
READ_LOGS / consent state:        P0 | P1 | P2 | P3 | P4
Consent sheet:                    shown/allowed | shown/declined | not shown | N/A
Resolved activity / PID at start:
ADB absolute path and version:
Host OS and .NET SDK:
Schedule executed:                Smoke | Standard | Upgrade | Full | Soak | Parity | Accessibility | Device expansion
Calibrated budgets (§4.2):        <attach the measured table>
Corpus manifest / SHA-256:
Evidence root / retention class:
Coverage gaps and approved N/A cells:
```

### 13.2 Result row — one per scenario

```text
ID | Start profile (CLEAN/WARM/CONTINUED) | Preconditions verified
   | Result (Pass/Fail/Blocked/N/A) | Expected oracle | Actual observation
   | Repetitions / measured values | Evidence files + SHA-256
   | Mutations restored | Notes / linked defects
```

One row can contain multiple assertions, but one failed assertion fails the row.
Record partial progress under Actual; do not call a scenario Passed because its
happy-path subset worked. Blocked and N/A are not Pass and must never be counted
in a pass percentage.

### 13.3 Defect report — one per finding

```text
Title:            one line, what the user sees
Scenario:         ID
Severity:         Blocker | Major | Minor | Polish
Reproducibility:  n of m attempts
Device state:     from the run header, plus anything scenario-specific
Steps:            numbered, from a known clean state
Expected:         quoting this plan or the product's own text
Actual:           quoting the product's own text verbatim
Evidence:         screenshots, recording, app logcat, session directory, manifest
First suspicion:  which layer (source, ingest, store, query, view model, view)
Trap check:       confirm it is not one of Appendix B before filing
```

Severity meanings: **Blocker** prevents install/launch, loses or corrupts data,
creates an exploitable security/privacy boundary failure, or prevents release
testing; **Major** breaks a primary workflow, produces silent wrong results,
crashes/ANRs, or makes the app unusable for an accessibility mode; **Minor** has
a bounded workaround or affects a secondary path; **Polish** is perceptible but
does not impede completion or correctness. Severity is impact, not implementation
effort or reproducibility.

The last two lines matter. Appendix B lists failure modes that look exactly like
product bugs and are not; a finding that has not been checked against them is not
yet a finding.

### 13.4 Exit criteria for a release

1. Every **B** and every **R** scenario passes on at least one physical device,
   with each scenario's applicable permission state, on the exact signed artifact
   being released. B-10/R-02 cover P0; B-11/R-02 cover P1; P2/P3 are explicit
   permission gates rather than being applied nonsensically to every B scenario.
   P-06 covers P4. P2, P3, and R-03 must pass on at least one supported physical
   platform that actually presents the consent sheet; absence of such a device is
   a recorded release-coverage gap, not a substitute Pass.
   Tier-R rows that carry a device precondition — a non-UTC clock (R-20), a loud
   device accent (R-27, R-28), an active screen reader (R-25, R-26), a populated
   ring buffer (via X-25) — are satisfied by **creating that precondition**, not
   by recording N/A. §1.3 allows N/A only for a capability the platform or the
   artifact does not have.
2. Every required cell in the §1.4 physical-device coverage matrix is exercised,
   or the release decision explicitly names the missing API/OEM/form-factor cell,
   owner, risk, mitigation, and expiry. An accepted gap is a conditional release
   decision; it does not turn untested cells green.
3. No Blocker or Major open against any tier.
4. **H-06 parity is exact** — device, desktop, and CLI agree on every count.
5. Zero crashes, ANRs, native faults, or low-memory kills attributable to
   `<PKG>` inside any scenario window, verified from time-bounded logs and a
   bugreport/dropbox source where available.
6. The soak schedule completed without unbounded memory growth or a dead process.
7. The accessibility schedule completed with no command missing from the
   accessibility tree.
8. Measured budgets recorded and compared with a like-for-like previous release;
   every absolute miss and >20% median/p95 regression is fixed or explicitly
   accepted with owner, rationale, and expiry.
9. The run header, all result rows, and the evidence set are archived with the
   release, and the manual-gate lines in
   [`RELEASE-CHECKLIST.md`](RELEASE-CHECKLIST.md) are ticked against this run id.
10. A-19 passes from the previous supported release whenever persisted schema,
   settings, session format, application ID, or signing configuration changed.
   A-22 also passes when Play is the intended release channel.
11. Every mutation-ledger entry is restored and §13.5 passes. An otherwise green
    run that leaves global state, filler data, or a development permission behind
    is incomplete.

### 13.5 Mandatory final cleanup and device hand-back

Run this after success, failure, or abort. Compare every value with the mutation
ledger rather than restoring guessed defaults.

1. Stop captures, imports, screen recordings, traffic generators, host CLI/
   desktop captures, and any orphan child process. Confirm no work remains.
2. Delete only the exact recorded filler/evidence/temp paths; verify free space
   returned. Never use a wildcard or recursive delete against a computed root.
3. Restore log-buffer sizes; battery simulation; Doze/battery saver; font,
   display, rotation, locale/time/time-zone, theme, animations, navigation,
   refresh, network/proxy/VPN, and developer options. Reboot if that is the
   documented restoration mechanism, then re-run pre-flight.
4. Return Android user/profile state. Remove an exact test-created profile only
   when its recorded user id, creation evidence, and owner-approved deletion are
   all present; never remove or weaken a pre-existing managed/work profile.
   Remove test-only per-profile installs and grants as the run record requires.
5. Revoke the temporary development grant and prove the result:
   `<ADB> shell pm revoke <PKG> android.permission.READ_LOGS`, followed by the
   package dump. Preserve it only with explicit device-owner approval.
6. Remove test corpora and public evidence copies if the device owner does not
   want them. Preserve deliberately exported user-visible files only when the
   run record says so.
7. Confirm ordinary apps launch, storage/battery/network are healthy, the device
   clock is correct, and the selected serial/fingerprint still match the run.
8. Attach the completed mutation ledger and cleanup evidence to the run record.

---

## Appendix A — ADB cookbook

```shell
# --- identity and lifecycle ---------------------------------------------
adb devices -l                              # discovery only
<ADB> get-state
<ADB> shell getprop ro.serialno
<ADB> shell getprop ro.product.model
<ADB> shell cmd package resolve-activity --brief <PKG>
<ADB> shell am start -W -n <resolved-activity>
<ADB> shell am force-stop <PKG>
<ADB> shell am kill <PKG>                  # background first; prove PID ended
<ADB> shell pm clear <PKG>                 # DESTRUCTIVE: app data and grants

# --- permission ---------------------------------------------------------
<ADB> shell pm grant <PKG> android.permission.READ_LOGS
<ADB> shell pm revoke <PKG> android.permission.READ_LOGS
<ADB> shell dumpsys package <PKG>          # inspect requested/granted permissions

# --- time-bounded logs --------------------------------------------------
<ADB> shell pidof <PKG>
<ADB> logcat -b all -v threadtime           # save in a dedicated terminal
<ADB> logcat -b all -d -s VisualCat:V       # supplemental tag-filtered view
<ADB> logcat -b crash -d -v threadtime      # correlate with run window/package
<ADB> bugreport <run-id>-bugreport.zip       # when deeper evidence is needed

# --- device log volume --------------------------------------------------
<ADB> logcat -g                             # save exact original per-buffer sizes
<ADB> logcat -b <buffer> -G 64K             # DEDICATED DEVICE; repeat only intended buffers
<ADB> logcat -b all -c                      # GLOBAL/DESTRUCTIVE; X-04 only
<ADB> shell 'for i in $(seq 1 100000); do log -p e -t VCATSTORM "RUN=<run-id> burst $i"; done'
# Restore each recorded buffer size exactly; never restore one guessed global value.
# If the device rejects per-buffer sizing, do not improvise: record N/A/Blocked.

# --- files ---------------------------------------------------------------
<ADB> push large.txt /sdcard/Download/
<ADB> shell ls -l /sdcard/Download/
<ADB> shell df /data /sdcard

# Sessions live in app-private storage; run-as needs a debuggable build.
# Derive the session root rather than assuming it. The app stores under the
# runtime's local-application-data folder, and that mapping is an implementation
# detail that can differ by runtime version and API level.
<ADB> shell run-as <PKG> find . -type d -name Sessions
<ADB> shell run-as <PKG> find . -name manifest.json
# Select one root only after proving that its child session/manifests match this run;
# record the exact returned path as <session-root>. Ambiguous roots are not evidence.
# Session directory names contain a SPACE, so never glob inside run-as sh -c:
# the glob expands and is then word-split. Match a path fragment instead.
<ADB> shell run-as <PKG> find <session-root> -maxdepth 1 -type d
<ADB> shell run-as <PKG> find <session-root> -path '*<hexfragment>*' -name manifest.json -exec cat {} \;
<ADB> shell run-as <PKG> du -sh <session-root>

# Prefer product portable-share/export on Release. On Debug only, this emits a
# binary tar stream; redirect it only in a shell known to preserve bytes:
<ADB> shell run-as <PKG> tar --help            # capability probe; not universal
<ADB> exec-out run-as <PKG> tar cf - <session-root> > sessions.tar

# --- UI driving and evidence --------------------------------------------
<ADB> shell screencap -p /sdcard/<run-id>-shot.png
<ADB> pull /sdcard/<run-id>-shot.png
<ADB> shell rm /sdcard/<run-id>-shot.png
<ADB> shell screenrecord --time-limit 180 /sdcard/<run-id>-clip.mp4
<ADB> pull /sdcard/<run-id>-clip.mp4
<ADB> shell rm /sdcard/<run-id>-clip.mp4
<ADB> shell uiautomator dump /sdcard/<run-id>-ui.xml
<ADB> pull /sdcard/<run-id>-ui.xml
<ADB> shell rm /sdcard/<run-id>-ui.xml
<ADB> shell input tap <x> <y>
<ADB> shell input text "AndroidRuntime"
<ADB> shell input keyevent KEYCODE_BACK

# --- configuration changes ----------------------------------------------
<ADB> shell settings get system font_scale              # ledger before put
<ADB> shell settings put system font_scale 1.3
<ADB> shell cmd uimode night yes                         # ledger original first
<ADB> shell settings get system accelerometer_rotation
<ADB> shell settings put system accelerometer_rotation 0
<ADB> shell settings get global low_power
<ADB> shell settings put global low_power 1
<ADB> shell dumpsys battery unplug
<ADB> shell dumpsys deviceidle force-idle
<ADB> shell dumpsys deviceidle unforce
<ADB> shell dumpsys battery reset
<ADB> shell settings put global low_power <original-value>
<ADB> shell am send-trim-memory <PKG> RUNNING_CRITICAL
# Restore every other setting to its ledger value; if the original was null,
# use `settings delete <namespace> <key>` instead of inventing a default.

# --- instrumentation -----------------------------------------------------
<ADB> shell dumpsys gfxinfo <PKG> reset
<ADB> shell dumpsys gfxinfo <PKG>
<ADB> shell dumpsys gfxinfo <PKG> framestats
<ADB> shell dumpsys meminfo <PKG>
<ADB> shell dumpsys thermalservice
<ADB> shell dumpsys batterystats --charged <PKG>
```

The tar command is an optional Debug diagnostic: some Android builds do not
provide `tar` in the `run-as` environment. Probe first; if unavailable, use the
product's portable share and record the diagnostic limitation rather than a
product failure. Its `>` is safe in Bash, `cmd.exe`, and PowerShell 7+, but
Windows PowerShell 5.1 can transform binary redirected output. In that shell,
use the product's portable share or invoke a verified binary-safe wrapper and
hash the result before treating it as evidence.

Screenshot coordinates: on a device sitting in landscape, `screencap` returns
the rotated resolution while `wm size` reports the physical one, and
`input tap` uses the screenshot's coordinate space — so a pixel measured on a
screenshot is directly tappable, without conversion.

That coordinate fact does not make fixed-coordinate taps a semantic UI test.
Prefer touch/manual interaction and accessibility-node bounds; use coordinates
only after recording orientation, insets, density, magnification, and the exact
screenshot from which they were derived.

---

## Appendix B — device and ADB traps that impersonate product bugs

Check every finding against this list before filing it.

1. **Discovery output is not target identity.** Transports can disappear,
   reconnect under another serial, or coexist with an emulator. A bare `adb`
   command may fail or reach an unintended sole device after topology changes.
   Use `-s`, re-check `get-state`, model, serial, and fingerprint, and bracket
   evidence by run time; never trust the earlier listing alone.
2. **`adb -s <unknown-serial> logcat` blocks indefinitely** instead of failing.
   Any capture that relies on a timeout to end then looks like a successful but
   empty session.
3. **`adb logcat -v help` is rejected** as an invalid format on current devices.
   Probe format support functionally:
   `logcat -d -b <buffers> -v <candidate> -t 1` exits non-zero when unsupported.
4. **`-v UTC` / `-v zone` emit the offset as a separate token** between the time
   and the PID, not as a suffix on the timestamp. Anything counting whitespace
   tokens will read the offset as a PID.
5. **A plain `adb install -r` over a Fast Deployment debug install aborts at
   launch** with `No assemblies found in …/.__override__/<abi>` — a SIGABRT with
   no managed stack. It is a deploy artefact, not a crash. See §2.3.
6. **Session directory names contain a space**, which breaks every
   `run-as … sh -c '… glob …'` form: the glob expands, then word-splits. Use
   `find` with a `-path '*<hex>*'` pattern instead.
7. **`run-as` refuses a non-debuggable build** with "package not debuggable".
   A release or Play build cannot be inspected this way. Use the product's
   portable share for release evidence. Installing Debug creates a different
   test condition and its deploy target attempts to re-grant `READ_LOGS`.
8. **A clean-installed release build has no automatic `READ_LOGS` grant**, so its
   on-device capture is own-app-only — often a scale difference of orders of
   magnitude — until the grant is issued explicitly. An in-place install may
   preserve a previous development grant; prove package state every time.
9. **The consent sheet is separate from the grant.** Android 13+ can present it
   for each capture while the grant is held; API level, OEM, policy, and current
   state can affect whether it appears. Declining narrows the capture without
   failing it. Record observation instead of assuming either outcome.
10. **Uninstalling can pop the Play Store into split-screen**, which changes the
    size class of the next launch. Force-stop `com.android.vending` and restart
    the launcher activity before measuring layout.
11. **`pm clear` drops the `READ_LOGS` grant** along with app data. Re-grant it
    if the scenario calls for it, or the next capture is silently restricted.
12. **An orphan `adb … logcat` on the host** usually means an app instance was
    force-killed, bypassing disposal — not a teardown defect. Check the process
    list and how the app was terminated before filing.
13. **Doze and battery saver can throttle a background capture** by design.
    Distinguish platform policy from product failure before calling it a bug
    (X-07 exists to pin this down).
14. **Thermal throttling silently halves throughput** on a warm device. Take
    `dumpsys thermalservice` alongside any disappointing performance number.
15. **Protected crash directories commonly return permission denied.** A
    consumer release need not allow shell access to `/data/anr` or
    `/data/tombstones`. Use time-bounded logcat plus bugreport/dropbox evidence;
    do not record a denied directory listing as a clean result.
16. **Binary redirection is shell-dependent.** Windows PowerShell 5.1 can alter
    `exec-out` binary data. Capture screenshots through a device file plus
    `pull`, and use a binary-safe shell for tar streams; verify hashes.
17. **`screenrecord` is intrusive and not a universal 60 fps clock.** It may cap
    duration/fps and add GPU/thermal load. Record its actual properties and use
    Perfetto or an external high-speed camera for latency it cannot resolve.
18. **`am kill` is not guaranteed to kill a foreground process.** Background it,
    record the old PID, issue the command, and prove the PID ended before calling
    a restoration test valid.
19. **Log-buffer sizing/clearing is global device state.** `logcat -G 4M` is not
    a valid universal restore value. Save exact starting sizes, restore them or
    reboot and compare every size with that baseline, and never run X-04 on a
    shared primary phone.
20. **Android inbound files are `ACTION_VIEW`, not generic shares.** A file
    manager's *Share* usually emits `ACTION_SEND`, which this app does not claim
    to receive. Use *Open with* or an explicit VIEW intent for A-06.

---

## Appendix C — coverage map

Every functional area, and the scenarios that cover it. A change to any area
should re-run at least the scenarios in its row.

| Area | Scenarios |
|---|---|
| Install, launch, and artifact identity | B-01, A-19, A-22, R-38 |
| Lifecycle, resume, and configuration change | A-12, A-13, A-24, U-05, X-08, X-09, X-17, X-20 |
| On-device capture — scope and permission | B-10, B-11, A-25, P-06, R-02, R-03, R-04, R-14, §2.4 |
| On-device capture — starting, running, stopping | B-12, B-16, A-14, X-03, X-05, X-06, X-15, X-18, X-23, X-24, X-25, R-01, R-18, R-21, R-22, R-23 |
| Host ADB capture | H-01, H-02, H-03, H-04, H-05, H-09 |
| File import and formats | B-03, A-08, A-09, A-10, A-11, A-21, X-01, X-02, R-29 |
| Providers and source materialization | B-03, A-06, A-11, A-21, U-13, P-07, P-09 |
| Intents, share, and transport | B-14, A-06, A-07, A-20, A-21, H-07, P-02, P-04, P-09, P-10 |
| Heat map, minimap, zoom, and axis | B-04, B-08, X-12, U-01, R-13, R-17, R-32, R-33, R-37 |
| Search and markers | B-06, B-07, X-13 |
| Filters, facets, ranges, saved views | B-05, A-02, A-03, A-04, R-36 |
| Templates and mining | A-01, H-06 |
| Entry details, selection, and raw source | B-04, A-10, A-24, U-13, X-13, R-31, R-39, R-40 |
| Sessions, cache, retention, recents | B-02, B-13, A-05, A-15, A-19, X-18, X-19, X-20, X-21, X-22, R-07, R-12, R-30 |
| Upgrade, signing, and distribution | A-19, A-22, H-06, H-07 |
| Export and reports | B-14, A-04, A-20, A-21, H-08, X-19, P-10, P-12 |
| Diagnostics bundle | A-16, P-03, P-12 |
| Settings | A-17, U-04, U-06, R-08 |
| Workspace modes and layout | B-09, U-01, U-02, R-06, R-34 |
| Back, sheets, and dismissal | B-15, A-20, U-18, R-19, R-26 |
| Keyboard and IME | U-03, U-10, R-16 |
| Accessibility | U-08, U-09, U-10, U-16, U-17, U-18, R-10, R-11, R-24, R-25, R-26 |
| Theme, contrast, text scale, and product identity | U-04, U-05, U-06, U-16, U-19, R-05, R-27, R-28 |
| Notices and messaging | A-18, U-12, U-13, R-09, R-15, R-22 |
| Locale, culture, and layout direction | A-13, U-11, R-35 |
| Time, zones, and clocks | A-23, X-11, R-20 |
| Cancellation and idempotence | A-20, U-18, X-15, X-18, X-19 |
| Volume and responsiveness | X-01, X-02, X-03, X-13, X-14, X-21, X-22 |
| Endurance and resource pressure | X-05, X-06, X-07, X-09, X-10, X-16, X-21, X-22, P-12 |
| Data integrity and loss evidence | A-10, A-11, A-19, X-04, X-11, X-19, X-20, X-24, H-06, H-07 |
| Privacy and security | P-01, P-02, P-03, P-04, P-05, P-06, P-07, P-08, P-09, P-10, P-11, P-12, P-13 |
| Android user and profile isolation | P-13 |
| First-run comprehension and visual regression | U-07, U-14, U-15 |
---

## Related documents

- [`ARCHITECTURE.md`](../ARCHITECTURE.md) — what each layer owns, and the
  invariants these tests are ultimately protecting.
- [`SUPPORT.md`](SUPPORT.md) — supported API levels, ABIs, and source kinds.
- [`CLI.md`](CLI.md) — every command used in tiers H and §3.
- [`KEYBOARD.md`](KEYBOARD.md) — the shortcut and accessibility contract checked
  in tier U.
- [`PERFORMANCE.md`](PERFORMANCE.md) — the host baselines the device budgets are
  calibrated against.
- [`SESSION-FORMAT.md`](SESSION-FORMAT.md) — the manifest and segment contract
  that parity assertions inspect.
- [`RELEASE-CHECKLIST.md`](RELEASE-CHECKLIST.md) — the manual gates this plan
  satisfies.
- [`CHANGELOG.md`](../CHANGELOG.md) — the account behind every tier-R row, and
  the source to re-derive that tier from after each release.
