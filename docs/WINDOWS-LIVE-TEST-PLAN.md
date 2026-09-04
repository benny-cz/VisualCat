# VisualCat — Windows live test plan

Manual and semi-automated verification of the shipped Windows desktop product on
real Windows installations. These are **live tests**: the exact release ZIP,
Windows Explorer and SmartScreen, real NTFS permissions and file locks, real
monitors and input devices, a real GPU compositor, and—where the scenario needs
it—a physical Android device over USB or Wi-Fi ADB. They complement, and never
replace, `dotnet test`.

**This plan is context-agnostic.** It assumes no previous test run, remembered
ADB serial, trusted download, extracted candidate, clean profile, or known data
path. The artifact, host, user token, display topology, source files, Android
device, starting state, and oracles are established and recorded at run time. A
tester can begin at §1 without facts that exist only in a previous report.

The plan is product-specific but **machine-state-independent**. Expected product
behaviour comes from this repository; paths, clocks, policies, antivirus state,
monitor scaling, and previous VisualCat data never do. When implementation,
documentation, and observation disagree, record the discrepancy. Do not silently
rewrite the expected result to match the machine.

---

## Contents

| § | Section |
|---|---|
| Start | [How to execute this plan](#start-here--how-to-execute-this-plan) |
| 1 | [Scope and surfaces under test](#1-scope-and-surfaces-under-test) |
| 2 | [Environment, artifact, and pre-flight](#2-environment-artifact-and-pre-flight) |
| 3 | [Test data preparation](#3-test-data-preparation) |
| 4 | [Evidence, budgets, and instrumentation](#4-evidence-budgets-and-instrumentation) |
| 5 | [Tier B — basic scenarios](#5-tier-b--basic-scenarios) |
| 6 | [Tier A — advanced scenarios](#6-tier-a--advanced-scenarios) |
| 7 | [Tier X — complex, stress, and soak scenarios](#7-tier-x--complex-stress-and-soak-scenarios) |
| 8 | [Tier U — Windows UX, UI, input, and accessibility](#8-tier-u--windows-ux-ui-input-and-accessibility) |
| 9 | [Tier I — CLI, Android, and artifact integration](#9-tier-i--cli-android-and-artifact-integration) |
| 10 | [Tier P — privacy, security, and negative scenarios](#10-tier-p--privacy-security-and-negative-scenarios) |
| 11 | [Tier R — regression pack for released and current fixes](#11-tier-r--regression-pack-for-released-and-current-fixes) |
| 12 | [Execution schedules](#12-execution-schedules) |
| 13 | [Recording results and exit criteria](#13-recording-results-and-exit-criteria) |
| A | [Appendix A — Windows and PowerShell cookbook](#appendix-a--windows-and-powershell-cookbook) |
| B | [Appendix B — Windows traps that impersonate product bugs](#appendix-b--windows-traps-that-impersonate-product-bugs) |
| C | [Appendix C — coverage map](#appendix-c--coverage-map) |

---

## Start here — how to execute this plan

This document is a catalogue, not a demand to run every check in page order. Use
this workflow so a tester can make progress without losing release rigor:

1. **Choose the gate before touching the candidate.** Use §12 for a named
   schedule and §12.1 for change-based additions. Artifact smoke, Standard, and
   Full Windows are cumulative gates. A release needs Full Windows plus Soak and
   any conditional Upgrade/Windows-expansion rows. The ADB, Accessibility,
   Security/storage, and Parity schedules are reusable focused slices: their
   evidence can satisfy the same Full rows when candidate, configuration, and
   oracle requirements are identical; do not execute them twice merely because
   they appear under two schedule names.
2. **Create the run record.** Fill §13.1, record this plan's repository commit,
   declare candidate capabilities per §2.9, create distinct evidence/session/
   corpus roots, and make the mutation ledger ready before changing the host.
3. **Prove the inputs.** Resolve every §2.3 token, hash the exact assets, select a
   W-state and P-profile, and prepare immutable independent oracles from §3.
4. **Pass the trust boundary first.** Run B-01/B-02 and I-01/I-12/I-14 before an
   expensive, destructive, or unattended scenario. Stop on an artifact identity,
   archive-safety, executable-path, or provenance contradiction.
5. **Run Basic as the product gate.** Every applicable B scenario must pass
   before relying on that workflow in A/X/U/I/P. If a prerequisite fails, mark
   dependent rows **Blocked by `<finding-id>`**; do not manufacture dozens of
   duplicate failures from one broken setup or primary path.
6. **Run selected specialist tiers.** One result row is still required per
   scenario. Shared setup, corpus, screenshots, or traces may be referenced by
   hash instead of copied, but no scenario inherits Pass implicitly.
7. **Close the loop.** Preserve the first observation, file findings, perform a
   fresh-state rerun when justified, apply §13.4, and complete §13.5 even after an
   abort or crash.

B/A/X/U/I/P scenario IDs are headings so they are available to document-outline
and screen-reader heading navigation; R guards stay in one compact table. Search
the exact ID to jump directly to a check. B cards spell out Risk/Pre/Steps/Expect/
Fail. In the compact A/X/U/I/P cards, the opening imperative is the setup/action
and every following assertion is a required pass condition. The global §4
oracles also apply even when a card does not repeat them.

Never overwrite a failed attempt with a passing retry. Keep `attempt-01`, record
the intervention, then use `attempt-02` under a new evidence directory. A retry
can verify a fix or classify an environmental cause; it does not erase the first
result. Stop any run that crosses its recorded free-space, thermal, trace-size,
privacy, time, or restoration threshold.

---

## 1. Scope and surfaces under test

The Windows release contains two executable surfaces. A complete Windows release
run exercises both and, where applicable, exchanges data with the Android
companion. A desktop result is not automatically a CLI or cross-platform result.

| Surface | What it is | Tiers |
|---|---|---|
| **Windows desktop** (`VisualCat.exe`) | The primary Avalonia/Skia GUI: file import with preview, growing-file follow, host ADB capture, analysis, session management, saves, exports, settings, and diagnostics | B, A, X, U, P, R |
| **Windows CLI** (`vcat.exe`) | The scriptable indexing, query, verify, export, generation, and ADB-capture surface shipped in a separate ZIP | I, P, parity assertions |
| **Android companion** (`com.barebit.visualcat`) | A producer and consumer of portable sessions and a second capture surface for cross-platform parity | I only in this plan; its own live behaviour belongs to [`ANDROID-LIVE-TEST-PLAN.md`](ANDROID-LIVE-TEST-PLAN.md) |

### 1.1 Functional inventory to be covered

**Distribution and launch** — exact release asset and checksum provenance,
archive safety and layout, self-contained startup without a system .NET runtime,
Mark-of-the-Web/SmartScreen presentation, standard-user execution, launch from
Explorer and PowerShell, startup arguments (`--log`, `--session`, and a bare
path), working directories, Unicode paths, window icon/title/version, and clean
removal of the portable program directory.

**CLI automation** — deterministic five-format test-log generation, indexing,
inspection, query/search/statistics/templates, verification and export; stable
JSON/NDJSON, stdout/stderr and exit-code contracts; streaming, piping, Unicode
console paths, cancellation, and desktop/Android/session parity.

**Capture and import** — finite log import; import-preview format, year, time-zone,
template, and portable-raw choices; five supported logcat formats; host ADB
discovery, buffers, pre-roll, duration and byte caps, format negotiation,
process-name sampling, bounded reconnect and resume; growing-file follow with
visible truncation/rotation/removal handling.

**Analysis** — six-severity density timeline, minimap, zoom/pan/fit, time-range
selection, text and bounded-regex search, marker navigation, severity filters,
facets, deterministic Drain templates, statistics, saved views, keyset paging,
load-all cancellation, exact entry inspection, clipboard actions, and
byte-faithful raw source context.

**Session lifetime** — progressive snapshots, partial/recoverable sessions,
finalize and reopen, external-source identity checks and degraded index-only
mode, temporary-session cache and retention, recent sessions, multiple tabs,
standard and portable saves, `.vcat.zip` import/export, CSV export scopes,
diagnostic bundle, cancellation, crash recovery, upgrades, and concurrent
process access.

**Windows presentation** — normal/maximized/minimized window state, minimum and
large window sizes, per-monitor DPI, display hot-plug, multi-monitor coordinates,
taskbar and Alt+Tab, mouse/touchpad/wheel/keyboard/touch/pen where available,
focus and modal ownership, Windows text/display scaling, product text scale,
system/light/dark themes, contrast themes, Narrator, Magnifier, color filters,
IME, keyboard layouts, reduced animation, remote desktop, lock/unlock, sleep,
hibernate, and user switching where supported.

### 1.2 Out of scope here

- Unit, integration, benchmark, and headless UI suites except as pre-flight.
- Android companion behaviour except a portable/parity exchange.
- Linux and macOS platform chrome and packaging.
- File associations, Start-menu shortcuts, MSI/MSIX install/uninstall, automatic
  desktop updating, code signing, or Windows Store deployment unless the
  candidate begins shipping one of those capabilities. The current Windows
  product is a portable unsigned ZIP; absence of installer-created integration
  is not a defect.
- Explorer drag-and-drop, shell context-menu verbs, jump lists, taskbar progress,
  toast notifications, and a tray icon are not current product claims. Their
  absence is not a defect unless the candidate or its documentation adds them;
  any one that appears becomes part of §2.9 capability reconciliation.
- General log formats other than Android logcat.
- Forensic erasure guarantees. Cache cleanup and deleting an extracted folder
  are ordinary file deletion, not secure wipe.

### 1.3 Applicability and test semantics

- **Pass** means every stated expectation was observed on the identified
  artifact, host profile, and source, with the required evidence.
- **Fail** means an expectation was contradicted, including a documented control
  being absent, an operation silently doing something else, or a required
  integrity check disagreeing.
- **Blocked** means no product assertion could be reached because of a named
  external condition. Retain setup evidence and identify the owner of the block.
- **N/A** is allowed only when the capability is explicitly unsupported by
  [`SUPPORT.md`](SUPPORT.md), the candidate, or absent hardware. A missing
  optional touchscreen can make the touch pass N/A; a surprising missing command
  cannot.

Words such as *responsive*, *stable*, *correct*, *accessible*, and *graceful*
are not pass criteria alone. Each scenario using one also cites a budget, an
integrity oracle, or an observable transition from §4.

### 1.4 Windows coverage matrix

Windows is a family of execution environments. Select hosts by dimensions, not
only by the newest machine available.

| Gate | Minimum live coverage | Important dimensions |
|---|---|---|
| Change smoke | One supported Windows 11 x64 host | Candidate ZIP, standard user, 100% or 125% scale, mouse and keyboard |
| Release candidate | Windows 11 plus Windows 10 where the release still claims it | Clean local profile; standard user; Defender active; 100% and non-integer scale; single monitor and mixed-DPI dual monitor; exact unsigned candidate |
| UI/accessibility gate | One host with Narrator and Windows contrast themes; two materially different displays | 100/125/150/200% scaling, small/large resolution, light/dark/contrast, keyboard-only, Magnifier, IME; touch/pen where claimed and available |
| ADB gate | One physical supported Android device and one Windows host with current platform-tools | USB transport, unauthorized/offline state, Wi-Fi or transport interruption where available, at least main/system/crash buffers |
| Storage gate | NTFS local SSD plus one materially different supported path | Long/Unicode/spaced path, non-system volume, removable or network/OneDrive path as an explicit compatibility probe, restrictive ACL, Defender/indexer interaction |
| Performance/soak | Dedicated physical Windows host | AC power, fixed power mode and display topology, controlled Defender policy, sufficient storage, no competing benchmark workload |

If the matrix cannot be completed, execute what is available and name every
untested Windows version, scale, GPU, input, storage, or ADB cell in the release
decision. Untested cells do not become green because another Windows 11 laptop
passed.

---

## 2. Environment, artifact, and pre-flight

### 2.1 Requirements

- An x64 Windows host inside the support policy current at execution time. The
  repository currently publishes only `win-x64`; do not infer ARM64 or x86
  support from Windows emulation.
- A standard, non-administrator Windows account for the primary run. An
  administrator account is additionally useful for system-policy and ETW
  diagnostics, but elevation must not be required for ordinary analysis.
- At least 20 GB free on the session volume for the full run and 80 GB for XL,
  corruption, low-space, and soak scenarios. Use a disposable VM/profile or a
  dedicated test directory for destructive cases.
- PowerShell 7 for repository packaging commands; Windows PowerShell 5.1 may be
  included as a compatibility shell for launch and path quoting.
- The exact Windows desktop and CLI release ZIPs, matching `SHA256SUMS`, release
  notes, and provenance attestations when the release workflow supplies them.
- For ADB tiers: current Android SDK Platform Tools, a data-capable USB cable or
  working Wi-Fi ADB, and a supported physical Android device whose use is
  authorized by its owner.
- Windows Performance Recorder/Analyzer or an equivalent ETW collection path
  for frame, CPU, disk, and hang investigation; Sysinternals Process Explorer/
  Procmon are useful but not mandatory when equivalent evidence is available.
- A capture tool whose impact is understood. Snipping Tool screenshots and an
  external camera are safe defaults; Game Bar recording can change GPU/CPU load
  and must be labelled when used for performance evidence.

Do not run low-space, forced power loss, ACL denial, Controlled Folder Access,
DLL-search, corrupt-archive, or mass-cache-deletion tests against a personal
profile or irreplaceable logs.

### 2.2 Pre-flight — identify the machine, user, display, and policy

Run this at the beginning of every run and after a VM restore, Windows update,
GPU-driver change, user switch, or display-topology change. Save output rather
than relying on a screenshot of Settings.

```powershell
$PSVersionTable
Get-ComputerInfo | Select-Object WindowsProductName,WindowsVersion,OsBuildNumber,OsArchitecture,CsSystemType,BiosFirmwareType
Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber,OSArchitecture,LastBootUpTime,Locale
Get-CimInstance Win32_ComputerSystem | Select-Object Manufacturer,Model,TotalPhysicalMemory,Domain,PartOfDomain
Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors,Architecture
Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,DriverDate,AdapterRAM,VideoModeDescription
Get-CimInstance Win32_DesktopMonitor | Select-Object Name,MonitorType,ScreenWidth,ScreenHeight,Status
Get-Culture
Get-UICulture
Get-TimeZone
Get-Date -AsUTC -Format o
Get-Volume | Select-Object DriveLetter,FileSystem,DriveType,HealthStatus,Size,SizeRemaining
Get-MpComputerStatus | Select-Object AntivirusEnabled,RealTimeProtectionEnabled,BehaviorMonitorEnabled,AntivirusSignatureLastUpdated
powercfg /getactivescheme
powercfg /requests
whoami /all
dotnet --info
```

Also record manually or through an approved inventory tool:

- each display's resolution, refresh rate, orientation, HDR state, primary flag,
  and Windows scale percentage;
- GPU preference for `VisualCat.exe`, hardware-accelerated GPU scheduling, remote
  desktop/VM state, and whether Windows animations are enabled;
- Windows text-size setting, color-filter state, active contrast theme, input
  language/IME, pointer size, touch/pen presence, and Narrator version;
- Defender exclusions, Controlled Folder Access, third-party antivirus, backup/
  sync agents, Windows Search indexing, corporate application-control policy,
  and whether the test volume is local NTFS, ReFS, network, cloud-synced, or
  removable;
- power source, power mode, battery percentage, thermal condition, and any CPU/
  GPU throttling visible to the host.

Do not disable security software just to make the default path pass. Run with the
ordinary policy first, then use an explicitly recorded controlled comparison if
diagnosis needs it.

### 2.3 Resolve and record every placeholder

| Token | Resolution rule |
|---|---|
| `<run-id>` | Unique path-safe identifier, normally UTC date/time plus candidate version and host label |
| `<candidate-zip>` | Absolute path to the immutable desktop `win-x64` release ZIP whose hash is recorded, published as `VisualCat-Desktop-win-x64-v<version>.zip` |
| `<cli-zip>` | Matching immutable Windows CLI ZIP, published as `VisualCat-CLI-win-x64-v<version>.zip`; its version must equal the desktop candidate's |
| `<candidate-root>` | A fresh absolute extraction directory created for this run; never the repository or Downloads root |
| `<VCAT>` | Exact `<candidate-root>\VisualCat.exe` path |
| `<VCAT-CLI>` | Exact extracted matching candidate `vcat.exe`; it is a surface under test and cross-check, never the sole correctness oracle |
| `<evidence-root>` | Dedicated absolute directory outside product session/cache directories |
| `<profile-root>` | The current profile returned by `[Environment]::GetFolderPath('UserProfile')`; do not assume `C:\Users\name` |
| `<local-app-data>` | Current profile's `[Environment]::GetFolderPath('LocalApplicationData')` |
| `<session-root>` | Product cache root discovered from the UI/settings; default currently `<local-app-data>\VisualCat\Sessions` |
| `<settings-path>` | Current product settings file; default currently `<local-app-data>\VisualCat\settings.json` |
| `<diagnostics-root>` | Default currently `<local-app-data>\VisualCat\Diagnostics` |
| `<adb>` | Exact `adb.exe` selected by the scenario; record version and hash |
| `<serial>` | Exact authorized Android transport selected from `adb devices -l`, re-proved after disconnects |
| `<corpus-root>` | Dedicated generated test-data directory with recorded hashes and oracle manifest |

Expand tokens before running a command. A command containing an unresolved
`<...>` token is a setup error, not evidence. Use `-LiteralPath`, argument arrays,
or properly quoted direct invocation; never compose a PowerShell command string
from a file name, device serial, or log content and pass it to `Invoke-Expression`.

### 2.4 Candidate acquisition, provenance, extraction, and identity

The exact uploaded ZIP is the release authority. A source build can diagnose a
finding but cannot make the uploaded bytes pass.

| Artifact | Purpose | Release authority? |
|---|---|---|
| Debug/source run (`dotnet run`) | Inspect exceptions and iterate quickly | No |
| Local Release publish from `tools/package.ps1` | Rehearse layout and early smoke | No, unless byte-identical to the uploaded candidate and provenance says so |
| Exact `VisualCat-Desktop-win-x64-v<version>.zip` | Windows desktop release decision | Yes |
| Exact `VisualCat-CLI-win-x64-v<version>.zip` | Shipped CLI decision and desktop cross-check, paired with independent or previously trusted oracles | Yes for CLI/integration rows |

Before extraction:

```powershell
Get-Item -LiteralPath '<candidate-zip>' | Select-Object FullName,Length,CreationTimeUtc,LastWriteTimeUtc,Attributes
Get-FileHash -Algorithm SHA256 -LiteralPath '<candidate-zip>'
Get-Item -LiteralPath '<candidate-zip>' -Stream * -ErrorAction SilentlyContinue
Get-AuthenticodeSignature -LiteralPath '<candidate-zip>' | Format-List *
```

Compare SHA-256 to the matching `SHA256SUMS` line and, when present, verify the
GitHub build-provenance attestation. Record the release URL, tag, commit, asset
size, ZIP hash, checksum-file hash, attestation result, download method, and
whether a `Zone.Identifier` stream exists. A checksum served beside the asset
detects corruption; provenance is the stronger origin oracle.

List ZIP entries before execution and reject absolute, drive-qualified, `..`,
alternate-data-stream, duplicate/case-colliding, or wrapper-directory surprises.
Extract into a fresh directory and record the full inventory/hash set. The
archive root must contain `VisualCat.exe`, `LICENSE`, `THIRD-PARTY-NOTICES.md`,
and `README.txt`; its README and visible application identity must name the
candidate version. No separate .NET installation may be required.

Do **not** click *Run anyway* before capturing the expected unsigned-candidate
SmartScreen path. Mark-of-the-Web propagation depends on browser and extractor,
so record the stream on both ZIP and executable after extraction. A warning is
expected for the currently unsigned artifact; an unexplained signature, a
publisher claim, a changed warning, or instructions that omit checksum review
is a finding.

After launch, record:

```powershell
Get-AuthenticodeSignature -LiteralPath '<VCAT>' | Format-List Status,StatusMessage,SignerCertificate,TimeStamperCertificate,Path
Get-FileHash -Algorithm SHA256 -LiteralPath '<VCAT>'
(Get-Item -LiteralPath '<VCAT>').VersionInfo | Format-List *
Get-Process -Name VisualCat | Select-Object Id,Path,StartTime,CPU,WorkingSet64,PrivateMemorySize64,HandleCount,Threads
```

When more than one process has that name, bind every later sample to the recorded
PID **and executable path**, not name alone.

### 2.5 Execution states — test dimensions, not setup shortcuts

| State | How to produce | Expected product behaviour |
|---|---|---|
| **W0 — ordinary verified portable run** | Download exact ZIP, verify it, extract through Explorer to a user-writable local NTFS directory, accept expected unsigned warning | Starts as standard user; no installer, elevation, or system .NET required; identity and notices match candidate |
| **W1 — clean profile / no VisualCat data** | New local test user or validated backup-and-move of the exact VisualCat data directory | First launch has no stale settings, sessions, or diagnostics and does not create data outside declared locations |
| **W2 — preserved profile / upgrade** | Previous supported release has settings, sessions, saved views, and an interrupted session; start candidate from a different extracted directory | Data is migrated or read compatibly; candidate does not rewrite old data merely by listing it; rollback risk is documented |
| **W3 — standard user with restrictive destination** | Run app from a readable directory while session/export target is denied, read-only, Controlled-Folder-Access protected, or low-space | Launch still works if its own directory is readable; each denied write fails visibly and safely; no elevation prompt or silent fallback |
| **W4 — alternate path topology** | Candidate/corpora under spaces, Unicode, long paths, another local volume, removable media, UNC, and cloud-synced storage as separate passes | Supported local paths work; unsupported/unstable storage fails honestly without corrupting the source or cache |
| **W5 — mixed-DPI/multi-monitor** | Window crosses monitors with different scale/refresh/orientation; primary monitor changes | Layout, hit testing, focus, dialogs, and rendering track the current monitor; no off-screen modal or stale scaling |
| **W6 — interrupted desktop session** | Minimize, lock, disconnect RDP, sleep/hibernate, display off, and user switch during capture/import as separate passes | Acquisition and visible refresh semantics match §6/§7; USB/ADB loss is surfaced; committed data remains recoverable |
| **W7 — security scanner/indexer contention** | Defender and Windows Search active; controlled Procmon or test handle briefly opens new manifest/destination | Bounded retries tolerate transient locks; cancellation stays prompt; a persistent lock produces a precise failure |
| **W8 — concurrent process** | Two candidate instances under one profile, each with distinct sources, then one shared-session conflict probe | No settings/session corruption, cross-instance tab confusion, or unsafe deletion; unsupported simultaneous writes are refused |

These states do not authorize weakening machine security. When a state requires a
policy change, use a dedicated machine, record the original value, and restore it.

### 2.6 Starting data profiles

| Profile | Contents | Use |
|---|---|---|
| **P0 — preserved** | The user's existing VisualCat directory, untouched | Read-only discovery only; never use for destructive tiers |
| **P1 — clean** | No `%LOCALAPPDATA%\VisualCat` and a fresh candidate extraction | Cold-start and privacy baseline |
| **P2 — seeded** | Known sessions: complete, interrupted, portable, external-source, corrupted copy; known settings and saved views | Most A/U/I scenarios |
| **P3 — upgrade** | Previous supported release's genuine on-disk data and settings | A-29 and release gate |
| **P4 — pressure** | Dedicated volume/profile prepared for low space, high session count, scanner/ACL, and crash matrices | X/P only |

Moving or deleting `%LOCALAPPDATA%\VisualCat` removes settings, cached sessions,
and diagnostics. That is destructive. Resolve the exact path, prove it is the
expected child of the current profile's LocalApplicationData, stop all VisualCat
processes, take a recoverable backup when required, and record the action.

### 2.7 Mutation ledger and guaranteed restoration

Before any mutation, append a ledger row:

```text
Timestamp UTC | Scenario | Machine/user | Setting/path/device | Original value/state |
New value/state | Exact restoration | Owner | Restored evidence
```

Ledger at minimum: VisualCat data directory, extracted candidates, generated
corpora, environment variables, PATH, Android SDK roots, ADB server/device state,
USB authorization, Defender exclusions/CFA, Search indexing, ACLs, power plan,
sleep/display timers, time zone/clock, locale/language/IME, display scale/
resolution/orientation/refresh/HDR, primary monitor, contrast/color filters,
text size, animation settings, GPU preference, page file, network/proxy/VPN,
test users, subst drives, mapped shares, removable media, and crash-dump policy.

Every destructive scenario owns cleanup even when it fails. Do not start a new
destructive scenario while an earlier ledger row has no plausible restoration.

### 2.8 Destructive-scenario register

| Scenario family | Risk | Required containment |
|---|---|---|
| Clean profile / upgrade reset | Deletes settings, sessions, diagnostics | Validated profile-local path and recoverable backup |
| Low disk / huge corpus | Host or user volume exhaustion | Dedicated VHD/VHDX, VM disk, or isolated test volume; abort threshold |
| ACL/CFA/reparse tests | Access loss or unexpected target | Dedicated subtree; record ACL with `Get-Acl`; never change a broad profile root |
| Corruption/archive bombs | CPU/disk exhaustion or unsafe extraction | Generated copies only; size/time limits; dedicated volume |
| Process kill/power interruption | Partial sessions, orphan ADB, unsaved work | Synthetic data, dedicated host/VM; scenario-specific recovery oracle |
| Cache deletion | Irrecoverable removal of temporary sessions | Seeded disposable cache only; verify protected/open sessions first |
| Clock/locale/display/power changes | Affects whole login session | One change at a time; record exact original; restore immediately |
| ADB buffer/server/device mutation | Disrupts IDEs and other users/devices | Dedicated device/host; serial-qualified commands; record shared server impact |
| Security/DLL-search probes | Malware-like or policy-sensitive execution | Isolated VM, inert signed/hash-recorded probe material, owner approval |

### 2.9 Run-time capability and claim manifest

Before selecting N/A rows, create a short capability manifest beside the run
header. A control being absent is observation, not proof that the capability was
never promised. Reconcile the exact candidate, its bundled README, release notes,
[`SUPPORT.md`](SUPPORT.md), and the release announcement.

| Claim family | Record at run time | Applicability consequence |
|---|---|---|
| Windows platform | Claimed Windows editions/builds, x64-only status, local/VM/RDP limits | Select §1.4 cells; an advertised but unavailable host cell is a coverage gap, not Pass |
| Distribution | Portable ZIP, unsigned/signing state, self-contained runtime, installer/file-association/update claims | Drives B-01/B-15, A-28, I-12, P-13/P-18; unexpected integration is tested, not ignored |
| Sources | Finite import, growing follow, host ADB, supported formats/buffers/reconnect | Missing advertised source is Fail; unavailable physical device may Block only the hardware-dependent attempt |
| Data exchange | Standard/portable sessions, `.vcat.zip`, CSV, matching CLI and Android exchange | Identifies mandatory I rows and exact verification oracles |
| Optional Windows hardware | Touch, pen, precision touchpad, mixed-DPI displays, HDR, removable/network/cloud path | N/A requires an explicit unsupported claim or recorded absent hardware; do not generalize probe results to all Windows hosts |
| Diagnostics and network | Structured diagnostics setting, diagnostic bundle, telemetry/update/network statements | Determines P-01–P-03/P-15 and whether any connection is expected after an explicit action |

For each row record one state—`claimed`, `explicitly unsupported`, `not
documented`, or `present but unclaimed`—plus the evidence location and scenario
effect. A documented feature missing from the candidate is Fail. A visible
unclaimed feature must be tested for its safety and user-facing contract or the
release is Blocked until the plan and documentation account for it. Product
source can explain a mismatch but cannot overrule the shipped user contract
during a release run.

---

## 3. Test data preparation

### 3.1 Deterministic corpora and independent oracles

Generate data with the matching `vcat.exe`, but do not let the implementation
under test be its own only oracle. For each corpus, also retain the generation
seed/options, SHA-256, byte length, line count from a binary-safe scanner, and
expected outcome/facet/template summary produced by a previously trusted CLI or
an independent fixture script. Compare the candidate CLI separately in Tier I;
I-14 proves the generator itself before its output is allowed to serve as setup
for a candidate result.

```powershell
& '<VCAT-CLI>' generate-test-log --output '<corpus-root>\small.txt' --lines 1000 --seed 42 --format threadtime
& '<VCAT-CLI>' generate-test-log --output '<corpus-root>\medium.txt' --lines 100000 --seed 42 --format threadtime
& '<VCAT-CLI>' generate-test-log --output '<corpus-root>\large.txt' --lines 1000000 --seed 42 --format threadtime
& '<VCAT-CLI>' generate-test-log --output '<corpus-root>\xl.txt' --lines 5000000 --seed 42 --format threadtime
foreach ($format in 'threadtime','time','brief','long','epoch') {
    & '<VCAT-CLI>' generate-test-log --output "<corpus-root>\fmt-$format.txt" --lines 5000 --seed 42 --format $format
}
Get-ChildItem -LiteralPath '<corpus-root>' -File | Get-FileHash -Algorithm SHA256
```

Required ordinary corpora:

| Corpus | Purpose | Produced by |
|---|---|---|
| `small.txt` | Cold import, exact raw offsets, keyboard, export | generator block above |
| `medium.txt` | Multi-tab, facets, saved views, routine timing | generator block above |
| `large.txt` | One-million-entry performance and paging | generator block above |
| `xl.txt` | Limits, cache, soak, cancellation; not routine smoke | generator block above |
| `fmt-*.txt` | Detection/override across all supported formats | generator block above |
| `mixed-formats.txt` | Honest unknown/outcome accounting | composition block below |
| `outcomes.txt` | One line of every parse outcome, for the B-19 gutter and off-timeline oracle | composition block below |
| `crashy.txt` | Known `AndroidRuntime`, stack trace, untimed/continuation content | composition block below |
| `quiet-live-seed.txt` | One complete record followed by controlled pauses for growing-file latency | composition block below |

The generator cannot produce the last four: they need content it deliberately
never emits. Build them by composing its output, so they stay reproducible from a
recipe rather than from an unrecorded hand edit.

```powershell
$corpus = '<corpus-root>'
$utf8 = New-Object Text.UTF8Encoding $false
# generate-test-log writes LF. Every composition below keeps LF unless the
# newline form is itself under test (§3.2).
function Save([string]$name, [string]$text) {
    [IO.File]::WriteAllText((Join-Path $corpus $name), $text, $utf8)
}

# mixed-formats.txt — concatenate whole single-format files, byte for byte, and
# record each part's byte range. Those ranges are the oracle; do not re-derive
# them from the product afterwards.
$stream = [IO.File]::Create((Join-Path $corpus 'mixed-formats.txt'))
try {
    foreach ($format in 'threadtime','brief','long','epoch') {
        $part = [IO.File]::ReadAllBytes((Join-Path $corpus "fmt-$format.txt"))
        "$format starts at $($stream.Position) for $($part.Length) bytes"
        $stream.Write($part, 0, $part.Length)
    }
} finally { $stream.Dispose() }

# outcomes.txt — one line of every disposition the parser can reach, in a file
# small enough to reconcile by eye. The long-format header is what makes the two
# following lines continuations rather than unknown lines.
Save 'outcomes.txt' ((@(
    '--------- beginning of main'
    '05-15 14:13:37.496  1073  1151 I VCatOracle: ordinary threadtime record'
    '[ 05-15 14:13:37.500  1073: 1151 E/VCatLong ]'
    'java.lang.IllegalStateException: VCAT-CRASH-<run-id>'
    "`tat com.example.app.Main.run(Main.java:42)"
    ''
    'D/VCatBrief( 1073): brief record carries no timestamp'
    '05-15 99:99:99.999  1073  1151 E VCatBad: impossible clock'
    'this line is not a logcat header at all'
) -join "`n") + "`n")

# crashy.txt — splice a crash block into generated traffic at a recorded offset.
$crash = @(
    '--------- beginning of crash'
    '05-15 14:13:40.000  1073  1073 E AndroidRuntime: FATAL EXCEPTION: main'
    '05-15 14:13:40.000  1073  1073 E AndroidRuntime: Process: com.example.app, PID: 1073'
    '[ 05-15 14:13:40.001  1073: 1073 E/AndroidRuntime ]'
    'java.lang.IllegalStateException: VCAT-CRASH-<run-id>'
    "`tat com.example.app.Main.run(Main.java:42)"
    "`tat java.lang.Thread.run(Thread.java:1012)"
    'D/VCatBrief( 1073): untimed brief record inside the crash block'
    '05-15 99:99:99.999  1073  1073 E VCatBad: impossible clock'
    'the crash reporter wrote this sentence with no header'
) -join "`n"
$rows = $utf8.GetString([IO.File]::ReadAllBytes((Join-Path $corpus 'small.txt'))).TrimEnd("`n") -split "`n"
$head = ($rows[0..499] -join "`n") + "`n"
"crash block starts at byte $($utf8.GetByteCount($head))"
Save 'crashy.txt' ($head + $crash + "`n" + (($rows[500..($rows.Count - 1)] -join "`n") + "`n"))

# quiet-live-seed.txt — the one complete record the §3.3 producer appends to.
Save 'quiet-live-seed.txt' "--------- beginning of main`n05-15 14:13:37.496  1073  1151 I VCatSeed: seed record`n"

Get-ChildItem -LiteralPath $corpus -File | Get-FileHash -Algorithm SHA256
```

`outcomes.txt` is deliberately mostly defects, so automatic detection scores it
too low to select a format and refuses it. Import it with an explicit
`threadtime` override; that refusal is the confidence threshold working, not a
finding. The other three detect cleanly and should be imported both ways, once
on detection and once overridden, so the override path is exercised on content
whose oracle is known. Give every year-less corpus an explicit year so its
instants are deterministic across hosts.

Imported as `threadtime`, `outcomes.txt` must account for its 9 source lines as
exactly 2 timed entries, 1 untimed entry, 1 meta record, 2 continuations, 1
unknown line, 1 rejected candidate and 1 ignored blank. Every other composed
corpus must likewise account for each of its source lines exactly once.

The test manifest must name exact expected parsed, timed, untimed, continuation,
unknown and rejected counts; first/last instant; severity totals; top facets;
template identities; and selected byte ranges. Never copy a count from the GUI
into the oracle after the test starts.

### 3.2 Adversarial corpora

Create deterministic, versioned, checksummed files. Keep originals read-only and
mutate disposable copies only.

| File | Required property / oracle |
|---|---|
| `empty.txt` | Zero bytes; one clear empty-source outcome |
| `notalog.bin` | Binary/non-log input; refused or fully accounted, never invented entries |
| `crlf.txt`, `lf.txt`, `mixed-eol.txt` | Exact byte offsets across newline forms |
| `bom.txt` | UTF-8 BOM handled without corrupting first record |
| `nonutf8.bin` | Invalid sequences retained/accounted, no silent replacement claim |
| `truncated.txt` | Final incomplete line and incomplete long-format record |
| `nofinalnewline.txt` | Last complete record published at EOF |
| `longline.txt` | At least one 2 MiB line; bounded UI and copy behaviour |
| `continuations.txt` | Stack frames/continuations linked and visible in source |
| `outoforder.txt` | Timestamp disorder retained; source order remains available |
| `dst.txt` | Ambiguous/invalid local times around a real DST transition |
| `pathological-regex.txt` | Long near-matches for `(a+)+$` timeout testing |
| `controls.txt` | ANSI/control/NUL, bidi overrides, zero-width characters, huge tokens |
| `unicode-name-😀-é-中文.txt` | Unicode normalization and display-name handling |
| long-path copy | Total absolute path >260 characters where host policy/API permits |
| `archive-traversal.vcat.zip` | `..`, absolute, drive, ADS-like, symlink/reparse metadata, duplicates/case collisions |
| `archive-bomb.vcat.zip` | Declared/compressed size disproportion bounded before exhaustion |
| session-corrupt copies | Manifest/schema/checksum/column/bitmap/raw-source faults, one at a time |

Build them from one recipe, not by hand. Every file below is composed from
generator output or written byte by byte, so a corpus that disagrees with its
manifest can be rebuilt and compared rather than argued about. Run it after §3.1
in the same `<corpus-root>`.

```powershell
$corpus = '<corpus-root>'
$utf8 = New-Object Text.UTF8Encoding $false
function Save([string]$name, [string]$text) {
    [IO.File]::WriteAllText((Join-Path $corpus $name), $text, $utf8)
}
function Load([string]$name) { [IO.File]::ReadAllBytes((Join-Path $corpus $name)) }

$base = $utf8.GetString((Load 'small.txt'))
$rows = $base.TrimEnd("`n") -split "`n"

# --- newline forms, encoding, and truncation ---------------------------
Save 'lf.txt'   $base
Save 'crlf.txt' ($base -replace "`n", "`r`n")
$index = 0
Save 'mixed-eol.txt' (($rows | ForEach-Object { $index++; if ($index % 2) { "$_`r`n" } else { "$_`n" } }) -join '')
[IO.File]::WriteAllBytes((Join-Path $corpus 'empty.txt'), [byte[]]@())
[IO.File]::WriteAllBytes((Join-Path $corpus 'bom.txt'), ([byte[]](0xEF,0xBB,0xBF) + (Load 'lf.txt')))
$invalid = Load 'lf.txt'
foreach ($at in 1000, 2000, 3000) { $invalid[$at] = 0xFF; $invalid[$at + 1] = 0xFE }
[IO.File]::WriteAllBytes((Join-Path $corpus 'nonutf8.bin'), $invalid)
$whole = Load 'lf.txt'
# Record both cut offsets: they are the oracle for the partial final record.
[IO.File]::WriteAllBytes((Join-Path $corpus 'truncated.txt'), $whole[0..49999])
[IO.File]::WriteAllBytes((Join-Path $corpus 'nofinalnewline.txt'), $whole[0..($whole.Length - 2)])
$random = [byte[]]::new(10MB)
[Random]::new(42).NextBytes($random)
[IO.File]::WriteAllBytes((Join-Path $corpus 'notalog.bin'), $random)

# --- shape and content extremes ----------------------------------------
Save 'longline.txt' ("05-15 14:13:37.496  1073  1151 I VCatLong: " + ('A' * 2MB) + "`n")
Save 'outoforder.txt' ((($rows[0..199]) + ($rows[600..799] | Sort-Object -Descending) + ($rows[200..599])) -join "`n")
Save 'pathological-regex.txt' (((1..200) | ForEach-Object {
    "05-15 14:13:37.496  1073  1151 I VCatRegex: " + ('a' * 5000) + 'b'
}) -join "`n")
# Long-format bodies are the only way to reach a continuation outcome.
& '<VCAT-CLI>' generate-test-log --output (Join-Path $corpus 'continuations.txt') --lines 2000 --seed 42 --format long

# Replace the dates with a transition the recorded test zone really has.
Save 'dst.txt' ((@(
    '--------- beginning of main'
    '03-30 01:59:59.999  1073  1151 I VCatDst: before the spring gap'
    '03-30 02:30:00.000  1073  1151 W VCatDst: inside the spring gap'
    '03-30 03:00:00.000  1073  1151 I VCatDst: after the spring gap'
    '10-26 02:30:00.000  1073  1151 W VCatDst: first pass of the autumn hour'
    '10-26 02:30:00.000  1073  1151 W VCatDst: second pass of the autumn hour'
) -join "`n") + "`n")

$controls = [Collections.Generic.List[byte]]::new()
$controls.AddRange($utf8.GetBytes("--------- beginning of main`n"))
$controls.AddRange($utf8.GetBytes("05-15 14:13:37.496  1073  1151 I VCatCtl: ansi $([char]27)[31mred$([char]27)[0m`n"))
$controls.AddRange($utf8.GetBytes('05-15 14:13:37.497  1073  1151 I VCatCtl: nul'))
$controls.AddRange([byte[]](0x00, 0x07, 0x08))
$controls.AddRange($utf8.GetBytes("`n05-15 14:13:37.498  1073  1151 I VCatCtl: bidi $([char]0x202E)desrever$([char]0x202C) zwsp $([char]0x200B) end`n"))
$controls.AddRange($utf8.GetBytes("05-15 14:13:37.499  1073  1151 I VCatCtl: token " + ('T' * 100000) + "`n"))
[IO.File]::WriteAllBytes((Join-Path $corpus 'controls.txt'), $controls.ToArray())

# --- name and path topology --------------------------------------------
$emoji = [char]::ConvertFromUtf32(0x1F600)
[IO.File]::Copy((Join-Path $corpus 'lf.txt'),
    (Join-Path $corpus "unicode-name-$emoji-e$([char]0x0301)-`u{4E2D}`u{6587}.txt"), $true)
$deep = $corpus
foreach ($n in 1..12) { $deep = Join-Path $deep ("segment-$($n.ToString('00'))-" + ('padding-' * 4)) }
New-Item -ItemType Directory -Path $deep -Force | Out-Null
[IO.File]::Copy((Join-Path $corpus 'lf.txt'), (Join-Path $deep 'deep.txt'), $true)
"long path length: $((Join-Path $deep 'deep.txt').Length)"

# --- malformed portable archives ----------------------------------------
# ZipArchive writes the entry name it is given, so the hostile names reach the
# central directory intact. Explorer would normalize or refuse several of them.
Add-Type -AssemblyName System.IO.Compression
function New-RawZip([string]$path, [string[]]$names, [string[]]$bodies) {
    if (Test-Path -LiteralPath $path) { [IO.File]::Delete($path) }
    $file = [IO.File]::Create($path)
    try {
        $zip = [IO.Compression.ZipArchive]::new($file, [IO.Compression.ZipArchiveMode]::Create)
        try {
            for ($i = 0; $i -lt $names.Count; $i++) {
                $writer = [IO.StreamWriter]::new($zip.CreateEntry($names[$i]).Open())
                try { $writer.Write($bodies[$i]) } finally { $writer.Dispose() }
            }
        } finally { $zip.Dispose() }
    } finally { $file.Dispose() }
}
$manifest = '{"formatVersion":"2.0"}'
New-RawZip (Join-Path $corpus 'archive-traversal.vcat.zip') @(
    '..' + [char]47 + 'escaped-manifest.json'
    [string][char]47 + 'absolute-entry.json'
    'C:' + [char]92 + 'drive-qualified.json'
    'manifest.json'
    'MANIFEST.JSON'
    'raw.log:hidden'
) @('{}', '{}', '{}', $manifest, $manifest, 'ads-like name')

$filler = '0' * 1MB
$names = @(); $bodies = @()
1..64 | ForEach-Object { $names += "pad-$_.bin"; $bodies += $filler }
New-RawZip (Join-Path $corpus 'archive-bomb.vcat.zip') ($names + 'manifest.json') ($bodies + $manifest)

# --- session faults, one per copy ---------------------------------------
# Damage a copy of a session that has already verified clean, one fault at a
# time, and record the exact byte or field changed.
& '<VCAT-CLI>' index (Join-Path $corpus 'small.txt') --output (Join-Path $corpus 'good.vcat') --portable
& '<VCAT-CLI>' verify (Join-Path $corpus 'good.vcat')
foreach ($fault in 'manifest', 'schema', 'checksum', 'column', 'bitmap', 'raw') {
    $copy = Join-Path $corpus "corrupt-$fault.vcat"
    if (Test-Path -LiteralPath $copy) { Remove-Item -LiteralPath $copy -Recurse -Force }
    Copy-Item -LiteralPath (Join-Path $corpus 'good.vcat') -Destination $copy -Recurse
}
# Then damage exactly one thing per copy and record the byte or field changed:
#   manifest  truncate manifest.json mid-object
#   schema    raise manifest.json formatVersion to an unsupported major
#   checksum  edit one recorded digest in segments\000001\checksums.json
#   column    flip one byte in a segment column such as level.bin or pid.bin
#   bitmap    flip one byte under segments\000001\bitmaps
#   raw       flip one byte inside raw.log, leaving its length unchanged
# Inspect the candidate's own session layout first: segment and column file
# names belong to the format version under test, not to this plan.
Get-ChildItem -LiteralPath $corpus -File | Get-FileHash -Algorithm SHA256
```

Explorer may normalize or refuse malicious archive names before VisualCat sees
them; pass the original file to the product and keep its hash. Where a scenario
needs a symlink or reparse entry inside an archive, or a deliberately invalid
central directory, extend `New-RawZip` and list the intended metadata in the
oracle rather than editing bytes by hand.

The repository's own `samples/` and `test-data/golden-formats.txt` fixtures are
useful known-shape cross-checks when the tester has a checkout, but they are not
required: a Windows run must be able to build every corpus it needs from the
shipped CLI and stock Windows APIs alone.

### 3.3 Growing-file producer

Use a producer that writes complete, uniquely numbered records, flushes after a
known cadence, and records its own UTC write times. It must support:

- one line then a long idle period;
- 5 lines/s steady traffic;
- burst traffic;
- a partial line completed later;
- in-place truncation;
- rename-and-create rotation;
- deletion;
- writer process crash;
- an exclusive-share interval;
- clock rollback/out-of-order record values without changing host time.

Record producer PID, output path, line range, byte count, flush times, exit code,
and final SHA-256. Stop it during cleanup. A generic `Get-Content -Wait` is not an
oracle for byte offsets or sharing flags.

### 3.4 ADB traffic and loss oracle

For host ADB tests, identify the device again and emit unique markers from a
controlled test app or `adb shell log` where the device supports it. Use a run
ID, monotonically increasing sequence, severity/tag/buffer expectations, and
separate host timestamps. Populate the selected buffers before capture for
pre-roll tests.

Never compare a live unbounded desktop and CLI capture by total count alone:
their time windows differ. Use marker-bounded intervals or capture the same
finite `adb logcat -d` bytes into a corpus for exact parity. Record platform
declared drops (`chatty`), reconnect gaps, buffer-clearing, and any test traffic
that the OS refuses to place in a requested buffer.

---

## 4. Evidence, budgets, and instrumentation

### 4.1 Evidence for every scenario

1. **Run boundary** — `<run-id>`, scenario ID, host/user identity, local and UTC
   start/end, candidate ZIP/executable hash, PID(s), and exact command/source.
2. **Before/after screenshots** at assertion moments. Capture all monitors when
   off-screen placement or DPI is relevant; retain native pixel dimensions.
3. **Video or event trace** for ordering, animation, resizing, input latency,
   hangs, or focus. State the recorder, resolution, fps, dropped frames, and
   likely overhead.
4. **Visible product text verbatim** — title/version, status, notices, counts,
   source identity, progress/final state, errors, selected scope, and file name.
5. **Product artifact** — session directory or portable copy plus CLI `verify`,
   manifest, export, and raw-source hash as applicable. Never mutate the only
   copy while collecting evidence.
6. **Process/resource samples** — PID/path, CPU, private/working set, handles,
   threads, child `adb.exe`, I/O, free space, power state, display topology, and
   GPU/thermal context at scenario-defined points.
7. **Windows failure evidence** — time-bounded Application event log, WER/
   Reliability entry, process exit code when obtainable, dump only under the
   approved synthetic-data policy, and Procmon/ETW evidence for lock or I/O cases.
8. **Product structured diagnostics** when sequencing matters. Record whether
   diagnostics were enabled, create the redacted diagnostic bundle through the
   UI, review it, hash it, and restore the original setting.
9. **External oracle** — corpus manifest, producer ledger, ADB markers, trusted
   CLI output, file hashes/byte ranges, accessibility tree, or trace query.

Store evidence under `<evidence-root>\<run-id>\<scenario-id>\` with an index and
SHA-256 list. Logs, sessions, screenshots, clipboard captures, dumps, ETW traces,
and Procmon files can contain source payloads, user names, paths, serials, tokens,
and account data. Restrict access, use synthetic input, review before sharing,
and delete according to the run retention policy.

Use stable evidence names such as
`<scenario>-attempt-<nn>-<utc>-<assertion>-<state>.<ext>`. The scenario index must
record assertion, UTC, producing tool/version, original path, SHA-256, sensitivity
class, redaction status, and any derived/redacted copy. Hash the native original
before annotation, cropping, transcoding, masking, or redaction; never replace it
with an edited copy. Every stated pass condition needs an evidence pointer or an
explicit reason that the assertion is N/A/Blocked.

### 4.2 Performance and responsiveness budgets

These are provisional absolute gates plus regression signals. Use at least five
cold or ten warm repetitions for short timings; report median and p95 and record
every discarded run. Keep Windows build, candidate hash, power mode, AC state,
Defender/indexing policy, display topology/refresh/scale, GPU driver, and corpus
constant. Flag a >20% median or p95 regression against a like-for-like accepted
baseline even when the absolute gate passes.

The controlled harness gates in [`PERFORMANCE.md`](PERFORMANCE.md) remain
authoritative wherever its corpus, reference-machine class, and measurement path
apply. The live UI budgets below add first-paint, input, redraw, and exact-asset
coverage; they never replace a stricter published engine gate. If a run cannot
reproduce the reference class, report both the absolute live result and the
like-for-like baseline delta rather than silently lowering the repository gate.

| Signal | Starting budget | Measurement |
|---|---|---|
| Cold launch to usable empty state | median ≤3 s; no run >5 s after the separate first-scan run | ETW/WPA or high-frame-rate video; process start alone is not first usable frame |
| Warm launch | median ≤1.5 s; p95 ≤2.5 s | Close normally, relaunch same extraction/profile |
| Input → visible acknowledgement | p95 ≤100 ms; none >250 ms for local commands | High-fps camera/video or ETW input/present correlation |
| Open/close owned dialog or More menu | ≤250 ms to settled state | Video/ETW |
| Close a dialog whose background work is still running | Acknowledged ≤250 ms; closed or truthfully `Cancelling…` ≤2 s; child process gone before the next assertion | Video plus process observation; a dialog that outlives its own child is a finding |
| Resize/move between DPI monitors | No freeze >500 ms; no stale-scale frame persisting >1 s | Video plus display-change timestamps |
| Import preview for ordinary small/medium file | ≤1 s after picker returns | Stopwatch/video |
| First heat map after import starts | ≤3 s | Product progress plus video |
| Sustained one-million-line import | Published harness gate ≥120,000 full-pipeline lines/s when its reference conditions apply; exact desktop asset also must stay within 20% of its accepted live baseline and never below 30,000 lines/s | Wall clock and manifest counts; report harness and live UI paths separately; exclude preview time explicitly |
| Search over 1 M entries | first result ≤1.5 s | Stopwatch from Enter/Ctrl+F action |
| Full-view 2,000-column heat-map query | Published controlled-harness average ≤10 ms; exact desktop diagnostic p95 ≤20 ms; UI never freezes >500 ms | Structured diagnostics plus the matching benchmark result and ETW; do not substitute one measurement path for the other |
| Timeline pan/zoom | No freeze >250 ms; p95 present no worse than 20% over baseline; ≤15% missed/janky presents | ETW DWM/present trace; a zero-frame trace is Blocked |
| Entry page (`Load 500 more`) | ≤400 ms | Video and status/count |
| Reopen finalized ≤1 M session | median ≤5 s; no run >10 s | Command to correctly drawn plot/final count, 3 runs |
| ADB discovery | initial list ≤5 s; 2 s refresh reflected within one interval | Dialog video and ADB trace |
| ADB/growing source first complete line | visible within 2 s of producer flush after capture is running | Producer UTC ledger and product video |
| Stop → sticky acknowledgement | ≤250 ms | Video |
| Stop → saved | No fixed absolute cap; elapsed indicator must advance and stage must remain truthful | Total by entries/bytes, compare baseline |
| Minimized live-view CPU | No continuous redraw/query cadence; acquisition continues | ETW/CPU and manifest progress before/during/after minimize |
| Idle growing-file follow | Private bytes and LOH settle; post-warm-up growth ≤32 MiB/hour and no sustained gen2 cadence attributable to polling | 15 min samples plus GC/ETW counters |
| Memory at 1 M entries | No OOM; peak/private bytes ≤25% over comparable accepted baseline | Fixed progress samples |
| Soak resources | No sustained post-warm-up positive slope in private bytes, handles, threads, mapped files, or latency | Rolling-window medians at least every 15 min |
| Crash/hang/WER | Zero attributable events in scenario window | Event logs, WER/Reliability, process observation |

First launch of a freshly downloaded unsigned self-contained build may include
SmartScreen, Defender scanning, runtime image mapping, and shader/font-cache
warm-up. Measure it, but report it separately from repeat cold launches. Never
discard it as an outlier: it is a real first-user experience.

For leak claims, define warm-up, cadence, workload, and comparison windows before
starting. Memory-mapped segments, font/shader caches, and loaded rows can grow
legitimately. Fail growth that does not plateau, exceeds the accepted envelope,
and is corroborated by increasing retained mappings/handles/threads, worsening
latency, or exhaustion risk. A single rising Task Manager line is not a leak
oracle.

### 4.3 Windows instrumentation

Use PID-bound sampling. `VisualCat` name-only counters become ambiguous when two
instances exist.

```powershell
$process = Get-Process -Id <pid>
$process | Select-Object Id,Path,StartTime,CPU,WorkingSet64,PrivateMemorySize64,VirtualMemorySize64,HandleCount,@{n='Threads';e={$_.Threads.Count}}
Get-CimInstance Win32_Process -Filter 'ProcessId=<pid>' | Select-Object ProcessId,ParentProcessId,ExecutablePath,CommandLine,CreationDate
Get-Process adb -ErrorAction SilentlyContinue | Select-Object Id,Path,StartTime,CPU,WorkingSet64,HandleCount
Get-Counter '\Process(*)\ID Process','\Process(*)\Working Set - Private','\Process(*)\Private Bytes','\Process(*)\Handle Count','\Process(*)\Thread Count','\Process(*)\IO Data Bytes/sec'
Get-Volume | Select-Object DriveLetter,SizeRemaining,HealthStatus
powercfg /requests
```

Time-bound Windows failure evidence to the recorded scenario interval:

```powershell
$start = [datetime]'<scenario-start-utc>'
Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=$start} |
  Where-Object ProviderName -in '.NET Runtime','Application Error','Windows Error Reporting' |
  Select-Object TimeCreated,ProviderName,Id,LevelDisplayName,Message
```

Use WPR/WPA with a saved profile appropriate to the question. At minimum, CPU,
disk/file I/O, process/thread lifetime, DWM/present/frame, input, and .NET GC
providers should be collected for interaction or leak diagnosis. Start before
the action, stop immediately after, retain the `.etl`, and record lost-event
counts. Do not infer smoothness from an ETW capture containing zero VisualCat
presents.

Procmon filters should be narrow: PID/path plus operations such as `CreateFile`,
`ReadFile`, `WriteFile`, `SetRenameInformationFile`, and `CloseFile`. A global
unfiltered capture is intrusive, huge, and likely to collect secrets unrelated
to VisualCat.

For accessibility, use Narrator plus Accessibility Insights for Windows or the
Windows SDK Inspect tool. Automation-tree presence is necessary, not sufficient:
activate each primary flow with assistive technology and confirm spoken names,
state, order, live updates, and modal boundaries.

### 4.3.1 Frame pacing and input-to-present procedure

Use one repeatable interaction script for pan, zoom, range selection, tab switch,
dialog open/close, and resize. Before each measured pass, open the same finalized
session, fit the same viewport, warm the same panes, wait for background import/
Defender work to settle, and record monitor refresh, scale, HDR, GPU, window state,
and exact PID. Run at least three 30-second passes per configuration.

Collect DWM/present, input, CPU, GPU, disk, process/thread, and lost-event data in
one time-correlated WPR trace. In WPA, isolate the candidate PID and the main
window's visible present stream; do not mix owned dialogs, another VisualCat
process, RDP reconnect, monitor hot-plug, minimized/occluded intervals, or screen-
recorder presents unless that condition is the scenario. Export the query/table
used to calculate:

- eligible presents and capture duration;
- p50/p95/p99 inter-present interval and the worst stall;
- missed-refresh equivalents for the recorded refresh rate;
- input-to-first-correct-present latency for timestamped actions;
- CPU/GPU utilization and lost-event count over the same interval.

Reject the measurement as Blocked when the wrong PID/window is selected, there
are no eligible presents, events were lost enough to change the result, the
recorder altered the frame cap, or setup differs from the comparison baseline.
Screenshots and video corroborate visible defects but do not replace present
events. Report every pass, the median, and the worst p95; do not keep only the
smoothest trace.

### 4.4 Human interaction, visual, accessibility, and automation oracle

Run the human path before inspecting or automating it. For every primary action,
judge the complete interaction loop:

1. **Discoverability** — the action and its scope can be found from visible text,
   conventional placement, or a documented shortcut; hover is not the only clue.
2. **Affordance and state** — enabled, disabled, selected, destructive, default,
   and progress states are distinguishable visually and through UI Automation.
3. **Acknowledgement** — input receives visible feedback within §4.2, and longer
   work keeps a truthful stage/cancel or stop affordance without stealing focus.
4. **Outcome** — completion/failure names the affected source, session, scope,
   row count, and destination needed to verify what happened.
5. **Recovery and reversibility** — cancellation preserves prior work; a failure
   keeps viable retry/change-destination/inspect actions; destructive choices
   state the exact object and require proportionate confirmation.
6. **Consistency** — equivalent pointer, keyboard, touch, automation, command-
   bar, More-menu, and shortcut routes have the same meaning, and live layout
   changes never move a repeated action into another command.

Use a WCAG-aligned release floor even though VisualCat is a native desktop app:
ordinary text at least 4.5:1; large text (at least 18 pt regular or 14 pt bold, or
the rendered equivalent) at least 3:1; and active control boundaries, focus/
selection indicators, icons, and graphical cues required to understand or
operate the product at least 3:1 against adjacent colors. Where a meaningful
graphic uses lower-contrast gradation, demonstrate an equivalent textual/non-
color route to the same information. Do not round a value up to pass, and do not
rely on color alone. Record sampled foreground/background values, tool/version,
state, theme, and display pipeline. See the W3C guidance for
[text contrast](https://www.w3.org/WAI/WCAG22/Techniques/general/G18.html) and
[non-text contrast](https://www.w3.org/WAI/WCAG22/understanding/non-text-contrast.html).

Visual comparisons use native-pixel captures from an approved reference with the
same candidate/corpus/window bounds/display scale/text scale/theme/contrast/
culture/font and interaction state. Never resample an image to make it align.
Mask only named nondeterministic regions such as a clock, PID, device serial, or
live rate; keep the unmasked original and mask definition. A baseline change is
a separate reviewed artifact, not an automatic consequence of the candidate
being different. Cover normal, hover, focus, pressed, disabled, selected,
loading, empty, failure, long-content, and modal states. A pixel diff alone cannot
Pass usability, semantics, focus, animation, or screen-reader behaviour.

Automation should select by stable semantic properties—AutomationId when the
product supplies one, otherwise accessible Name + ControlType + owned hierarchy—
and assert name/role/state as well as activation. Fixed screen coordinates,
recorded mouse macros, OCR-only selectors, timing-only sleeps, and tree indices
are not release evidence across DPI or virtualization. Geometry scenarios may
use coordinates only after recording logical/physical transforms and confirming
the pointer hit the intended semantic control. Keep manual/Narrator checks for
the Skia timeline and any custom control whose automation surface cannot express
the visual relationship.

---

## 5. Tier B — basic scenarios

Purpose: prove the exact Windows candidate works in the ordinary path a new
reader takes. All applicable B scenarios pass before A/X work begins; a primary
path failure can invalidate later observations.

**Block format:** *Risk* — what could go wrong. *Pre* — starting state. *Steps* —
what to do. *Expect* — observable pass criteria. *Fail if* — disqualifying
observations.

---

### B-01 · Verify, extract, and cold-launch the exact ZIP

*Risk* The uploaded product is not the built product, cannot be extracted, needs
an undeclared runtime/elevation, or is misrepresented by Windows security UI.
*Pre* W0, candidate not previously executed on this profile.
*Steps* Verify checksum/provenance and ZIP entries per §2.4 · open the unopened
archive in Explorer and assess whether its README/release instructions clearly
say to extract before launch · extract with Explorer · inspect Mark-of-the-Web on
ZIP and executable · launch from Explorer as a standard user · handle the
expected unsigned SmartScreen route only after verification · wait 30 s.
*Expect* Required notices and `VisualCat.exe` are at archive root; no installer,
elevation, or system .NET prompt appears; one main window opens inside budget
with the VisualCat icon and product title; the in-window identity shows the exact
candidate version and agrees with file metadata. Executable path/hash match the
inventory; no crash/WER event. The unsigned warning and README instructions
agree with [`RELEASE-NOTES.md`](RELEASE-NOTES.md), make extraction discoverable,
and never encourage running a dependency-filled app from Explorer's compressed-
folder view.
*Fail if* archive identity differs, an unexpected publisher/signature appears,
the app requires admin or a separately installed runtime, more than one main
process/window appears, or the first window is blank/frozen.

### B-02 · Desktop empty state and command inventory

*Risk* Platform composition exposes Android controls or omits desktop value.
*Pre* P1, B-01 running, no session.
*Steps* Read the hero and command bar · resize from 1440×900 to minimum 900×600
and back · open More.
*Expect* Identity says `VisualCat <version> · local-first · no telemetry` and
matches the binary. Empty state offers `OPEN LOG`, `ADB LIVE`, and `REOPEN
SESSION`; desktop command surfaces provide Open session, Recent sessions, Follow
growing file, Open portable archive, Save session, Save portable, Export CSV,
Appearance & timeline, Session cache, Diagnostic bundle, and Lines not on the
timeline only when applicable. Android-only `ON-DEVICE LIVE`, share sheet,
Wireless-debugging setup, and phone Plot/Split/Details mode row do not appear.
Session-dependent actions are disabled with an accessible reason.
*Fail if* platform-only commands leak across, a required desktop command is
absent, controls disappear rather than fold into More, or disabled commands
silently look actionable.

### B-03 · Import a small file through Windows picker and preview

*Risk* Native picker, import preview, detection, time policy, or source identity
is broken.
*Pre* `small.txt` and oracle available.
*Steps* Open log · select `small.txt` · inspect detected candidates/outcomes,
resolved span, format, year, time zone, template and portable-raw controls ·
accept defaults.
*Expect* Preview appears inside budget; detected settings match oracle; import
progress is visible; final parsed/unknown/untimed totals, time range, and source
name match; the manifest persists the chosen format/time policy/source identity;
plot and minimap draw.
*Fail if* desktop skips its preview, picker cancellation is reported as failure,
counts differ, source name becomes a temporary cache name, or the preview and
manifest disagree.

### B-04 · Heat map to exact source bytes

*Risk* The core value chain is disconnected.
*Pre* B-03 open; oracle contains selected raw ranges.
*Steps* Read six severity lanes · select a dense cell · select an entry · open
Selected entry/source context · expand, wrap, copy, and compare source bytes at
the recorded offset using a binary reader.
*Expect* Cell, result list, entry inspector, timeline caret, full message, source
gutter, highlighted raw record, and file bytes identify the same record. The
desktop clipped-message tooltip contains the full message.
*Fail if* any surface points to another entry, raw offsets fail, a source read
stays pending, or copy changes bytes/text beyond the documented normalization.

### B-05 · Severity filters and clear semantics

*Steps* Toggle each of `V/D/I/W/E/F/?` individually and in combinations · inspect
plot, list, counts and chips · Clear all.
*Expect* Every visible count names its scope; plot and entries agree; active
dimensions appear as removable chips; Clear all restores the exact unfiltered
view without changing zoom or selected session.
*Fail if* density and count differ, removing one filter clears another, a chip's
label/state is ambiguous, or a hidden filter remains.

### B-06 · Text and regex search

*Steps* Ctrl+F and search a known literal · use F3/N, Shift+F3/Shift+N, first,
last, and position-jump controls · toggle case sensitivity/regex as offered · run
a valid, invalid, and pathological regex.
*Expect* Search field receives/selects focus; matching count and markers agree
with oracle; navigation wraps and preserves zoom span; invalid syntax is
explained; pathological matching hits its configured timeout without freezing;
Escape clears search according to [`KEYBOARD.md`](KEYBOARD.md).
*Fail if* typing triggers an unmodified shortcut, navigation skips/duplicates,
zoom changes, an invalid expression crashes, or UI input stalls beyond budget.

### B-07 · Timeline mouse, touchpad, wheel, and keyboard basics

*Steps* Pan by drag, wheel/precision touchpad, Left/Right · zoom by wheel/gesture,
`+`/`-`, double-click and buttons · use minimap, Home/End, and `0`/Fit · select
and clear a time range.
*Expect* All routes share bounds and scale semantics; panning never exposes time
outside the session; minimap and viewport agree; double-click zoom does not also
scope entries; Fit returns to the complete session; keyboard focus is visible.
*Fail if* input routes disagree, the axis loses two meaningful endpoints, a
gesture changes an unrelated filter, or focus disappears.

### B-08 · Analysis panes, paging, and selected-entry workflow

*Steps* Navigate Templates, Facets, Statistics, entries and source context · load
the next 500 rows · double-click/Enter a selected row · use Copy message and Copy
raw.
*Expect* Pane focus and selection stay synchronized; one page adds the expected
number; stable action slots do not move under the pointer; full entry is
reachable by mouse and keyboard; clipboard contains exactly the labelled scope.
*Fail if* paging repeats/skips identities, opening an inspector loses selection,
or two taps in the same fixed action slot execute different commands.

### B-09 · Host ADB discovery and three-minute capture

*Risk* The primary live desktop source cannot locate ADB/device or record it.
*Pre* Physical device authorized; exact `<adb>` and `<serial>` recorded; buffers
main/system/crash; deterministic traffic prepared.
*Steps* ADB live · observe two refresh intervals · choose exact device and
buffers · set a small pre-roll · start · emit marker-bounded traffic for 3 min ·
inspect live updates · stop once.
*Expect* Dialog shows serial/model/state and refreshes within budget; Start is
enabled only for `device`; status moves through discovering/connecting/
capturing; pre-roll markers and live records appear; metadata records serial,
buffers, negotiated format/time zone and source kind; process names/facets are
plausible; no second capture is started.
*Fail if* a different device is selected, unauthorized/offline is capturable,
the first complete record waits for another chunk, format/time zone are wrong,
or a bare/global ADB ambiguity reaches the source.

### B-10 · Stop capture is answered, sticky, and complete

*Risk* Stop appears ignored or final state lies.
*Steps* On B-09 press Stop capture once · do not repeat · watch button/status to
completion · prove child ADB process ends · verify/reopen session.
*Expect* Acknowledgement ≤250 ms and never returns to Capturing/Stop; elapsed
time advances while named stages drain/finalize; once saved, live-only Follow/
new-data/Stop controls disappear; manifest finalizes; counts and CLI verification
pass; no orphan ADB child owned by this process remains.
*Fail if* stop springs back, finalization never resolves, query cancellation
turns the capture into failure, child process leaks after graceful stop, or
committed counts change on reopen.

### B-11 · Follow a growing file

*Risk* Desktop-only source is missing, mislabelled, or loses its first/last line.
*Pre* Growing-file producer at zero or one complete line.
*Steps* Follow growing file · choose source · append a complete line, pause,
append partial then complete it, run steady traffic, stop through product.
*Expect* Source is labelled as growing-file follow, not import/ADB; first complete
line appears within budget without needing a second; partial line publishes only
when complete; Follow tracks the live edge; stop finalizes a portable live-style
session with raw bytes and producer sequence intact.
*Fail if* opening requires import preview, idle polling causes visible busy work,
partial bytes become a record, records duplicate/disappear, or source remains
live after stop.

### B-12 · Save standard and portable sessions

*Steps* From B-03 choose Save session to a fresh folder, then Save portable to
another folder · close the tab · reopen each with Open session · verify with CLI.
*Expect* Product creates uniquely named `.vcat` directories under chosen parent;
standard save preserves external source identity and view sidecar; portable save
contains `raw.log`; both verify and reopen with identical analytical identity,
counts, templates, filters, viewport and selected entry; destination publication
is atomic and no temporary directory remains.
*Fail if* source is overwritten, destination is partially visible, raw data is
missing from portable save, or save/reopen changes results.

### B-13 · Open and round-trip a portable ZIP

*Steps* Create a verified portable `.vcat.zip` with CLI · Open portable archive
in desktop · inspect · save portable directory · close/reopen.
*Expect* Extraction is bounded and verified before publication; archive and
saved session share counts, raw hash, template IDs and selected records; archive
does not depend on its original location after open.
*Fail if* unverified content becomes a session, raw data changes, temporary
extraction remains after failure, or moving the source ZIP breaks an open tab.

### B-14 · CSV export scopes, order, and encoding

*Steps* With a filter, zoom viewport and selected range that differ, Ctrl+E ·
exercise every offered scope including everything ignoring filter · export in
source and chronological order, UTF-8 and UTF-8 BOM via settings · cancel once.
*Expect* Chooser names only distinct scopes and row counts; saved file has one
`.csv` extension, correct suggested name, exact rows/order/range/encoding and
CSV escaping; completion notice names scope/count/file; cancellation creates
nothing and no failure notice.
*Fail if* menu promise and actual chooser differ, filters leak into ignore-filter
scope, BOM/order setting is ignored, `.csv.csv` appears, or existing destination
is damaged on cancellation/failure.

### B-15 · Startup paths and working-directory independence

*Steps* From Explorer/PowerShell and three different working directories launch
`<VCAT>` with `--log <file>`, `--session <dir>`, and a bare log/session/archive
path; include spaces/Unicode; pass duplicate spelling/case of one path; pass one
missing path.
*Expect* Each valid path opens once in the correct flow; log gets import preview,
session opens directly, archive verifies; duplicate paths do not create duplicate
tabs in one launch; relative paths resolve from caller working directory; missing
path produces a visible actionable notice without preventing other valid paths.
Unknown switches are not interpreted as files.
*Fail if* app depends on its own current directory for runtime assets, quoting
breaks, startup cancellation becomes a permanent failure, or a missing path
aborts all valid inputs.

### B-16 · Recent sessions, close, and reopen

*Steps* Create at least two complete and one interrupted seeded session · close
tabs · use Recent sessions · close/relaunch application normally · inspect empty
state recent list.
*Expect* Lists use distinguishable source/capture names and local time, size, and
truthful complete/interrupted state; no private materialization GUID/path is
spoken as the name; opening preserves counts/view; close does not delete; desktop
does not claim to restore open tabs unless that behaviour is documented.
*Fail if* finished sessions show a stale live-edge window, states use conflicting
vocabulary, recent entry is missing, tab close crashes, or close destroys data.

### B-17 · Window minimize, restore, close, and persisted size

*Steps* Resize to a non-default valid size · minimize 60 s during a quiet capture
or follow · prove source progresses while redraw work relaxes · restore ·
maximize · close the main window while the source is still active · relaunch and
inspect the resulting cached session · repeat a normal close with no active work.
*Expect* Minimize does not stop acquisition; restore catches visible view up
immediately; no blank/stale window; normal size and maximized state persist per
implemented contract; minimum stays 900×600; closing disposes capture/session
work without hang and does not leave child ADB. After an active close, committed
data is either finalized successfully or reopens as explicitly interrupted and
recoverable; it is never silently discarded or falsely labelled complete. Any
confirmation or absence of confirmation is consistent with the recoverability
and loss risk shown to the reader.
*Fail if* acquisition pauses silently, minimized UI keeps full redraw cadence,
window restores off-screen/zero-sized, relaunch ignores persisted state, or
shutdown hangs/crashes.

### B-18 · Keyboard-only primary journey

*Steps* Disconnect pointer where practical · use Tab/Shift+Tab, arrows, Enter,
Space, Escape, Ctrl+O, Ctrl+Shift+O, Ctrl+E, Ctrl+F, marker shortcuts, Alt+1..4,
and timeline keys to import, filter, inspect, export and close dialogs.
*Expect* Focus order is logical and visible; commands in [`KEYBOARD.md`](KEYBOARD.md)
work; typing fields suppress unmodified shortcuts; Escape dismisses one layer or
scope at a time in documented order; modal dialogs trap focus and return it to
the invoking control.
*Fail if* any primary action requires a pointer, focus moves behind a modal,
Escape closes the application while a layer is open, or focus is lost after a
view refresh.

### B-19 · Off-timeline and unparsed evidence stays discoverable

*Risk* Stack frames, untimed records, rejected candidates, or unknown lines are
counted by the parser but effectively disappear from the reader's investigation.
*Pre* `crashy.txt` and `outcomes.txt` from §3.1, each imported with an explicit
format and year. `outcomes.txt` carries one line of every gutter code — `en`,
`mt`, `..`, `e?`, `??`, `!!` — and its nine source lines must reconcile to 2
timed entries, 1 untimed entry, 1 meta record, 2 continuations, 1 unknown line,
1 rejected candidate and 1 ignored blank. A continuation is only reachable after
a long-format header, so a corpus without one cannot produce `..` and its absence
there is not a finding.
*Steps* Import both corpora · reconcile source/timed/untimed/continuation/unknown/
rejected totals · inspect the count row and off-timeline chip · open Lines not on
the timeline from the chip and More · page to the end · read the on-screen gutter
legend · select/copy text with keyboard and pointer · inspect neighbouring source
context and close/reopen the session.
*Expect* Every source line belongs to exactly one oracle outcome; plot/entry
counts say that they cover timed entries rather than implying every source line.
The chip distinguishes untimed records from unparsed lines when only one exists
and truthfully combines them when both exist. The command appears only when
applicable; both routes open one bounded, pageable card containing every relevant
line once in source order. `en entry · mt marker · .. continuation · e? untimed ·
?? unknown · !! rejected` is visible whenever non-ordinary gutter codes are
shown, without requiring hover, and is exposed accessibly. Copy contains exactly
the loaded text; raw bytes and one-based source line numbers remain exact after
reopen. Entry CSV does not imply that off-timeline non-record lines were exported.
*Fail if* any line is omitted/double-counted/fabricated into a timed entry, totals
or wording hide the population difference, the only legend is a tooltip, paging
skips/repeats, the card requires a pointer, or source/copy order differs from the
oracle.

---

## 6. Tier A — advanced scenarios

Purpose: exercise deliberate second-day workflows, platform variation, recovery,
and combinations that do not belong in every smoke run.

---

### A-01 · Templates and statistics on real ADB traffic

Capture at least 10 min while exercising several Android subsystems. Templates
must be stable across reopen, ranked consistently, filterable/includable/
excludable, and copyable. Statistics totals and first/last instants must equal
the active query oracle. A process name changing for one PID must not retain
stale facet tallies.

### A-02 · High-cardinality facets and composition

Use thousands of tags/PIDs/TIDs/processes. Scroll and search facets; combine one
facet from each dimension, severity, regex and time range. AND applies across
dimensions, intended within-dimension semantics are visible, counts name their
population, and no unrelated filter disappears.

### A-03 · Saved views round trip

Save a named severity+facet+regex+range view; clear, apply, close/reopen the
session and apply again; save Unicode/long/duplicate names; delete one. Every
dimension and Follow state allowed by the schema returns exactly; invalid or
unsupported `view.json` is ignored without blocking the session; delete affects
only the named view.

### A-04 · Range, viewport, filter, and export remain distinct

Create a selected time range inside a zoomed viewport over an active filter.
Exercise Zoom range, Filter range, Export range, Clear selection and Escape.
Each changes only its promised dimension, and exported half-open boundaries
match the independent microsecond oracle; after I-02 validates it on the same
corpus, the candidate CLI result agrees too.

### A-05 · Many independently stateful tabs

Open at least eight sessions: file, ADB, growing, standard, portable, recovered,
degraded, and failed. Give each a different filter/viewport/selection. Switch,
reorder if supported, close first/middle/selected while progress occurs, and
use the scrolled tab strip. State never leaks; selected tab and command
availability track the visible session; every close is prompt.

### A-06 · Import-preview override matrix

For each `fmt-*`, import once with detection and once with an intentional format
override; vary assumed year, valid Windows and IANA time-zone IDs accepted by
the runtime, template mining off, and Embed raw source on. Preview validation
rejects blank/invalid zone and years outside 1970–9999 without closing. Manifest
records exactly the accepted choice; misleading overrides account for unknown
input rather than fabricating records.

### A-07 · Automatic detection and mixed content

Import every format and `mixed-formats.txt`. Candidate scores/order, warnings,
outcome counts and selected default match oracle. Low-confidence input remains
an explicit user decision in preview. No bytes disappear merely because a line
does not fit the primary format.

### A-08 · Adversarial corpus sweep

Open every §3.2 finite file. No crash/hang; invalid bytes, continuations,
untimed/unknown/rejected records remain counted and reachable; 2 MiB lines are
bounded, inspectable, wrappable and copyable; source offsets remain exact across
CRLF/BOM/no-final-newline; control/bidi content cannot alter surrounding UI.

### A-09 · Source mutation during finite import

Import a large file, then replace, truncate, append, delete, and ACL-deny separate
copies while materialization/ingest runs. The product either uses one consistent
identity snapshot or fails/reports source change. It never builds a hybrid,
silently accepts a changed hash, corrupts the replacement, or waits forever.

### A-10 · External source changed or missing on reopen

Save a non-portable session, close it, then modify/move/delete the external log.
Reopen. Analytical index remains readable in explicit degraded mode; source
context says why unavailable; verification distinguishes index validity from raw
coverage; Retry succeeds if the exact source returns. It must not silently read a
different same-named file.

### A-11 · Recovered interrupted session

Use a phase-killed capture to produce a non-finalized session. Recent/cache state
says interrupted, not active or complete. Opening offers Keep, Export recovered
data, and Delete only when inside the managed cache. Each action has accurate
count/irreversibility; export verifies; deletion asks again and targets only the
exact session.

### A-12 · Session cache and retention policy

Seed complete/interrupted/open/actively capturing sessions across age/size
thresholds. Default performs no automatic deletion. Enable age/size rules,
preview cleanup, cancel, then confirm. Preview and execution name exact count,
bytes and representative sessions; open/capturing sessions are protected;
eligibility is recomputed before deletion; errors are enumerated; policy persists.

### A-13 · Appearance, timeline, and diagnostics settings

Exercise System/Light/Dark, High contrast, Text scale 0.75–2, UI refresh 1–60,
linear/square-root/log intensity, per-row/global normalization, minimum
microseconds/pixel, pixel snapping, minimum bar width, export order/encoding,
diagnostics, session directory, ADB defaults, and cache policy. Apply/cancel,
restart, and inspect validated `settings.json`. Effects are immediate where
claimed, labels are human-readable, invalid edited values clamp/default safely,
and existing open sessions survive view rebuilds. The Windows dialog exposes no
phone-only plot/details split or other setting that cannot affect this desktop
installation; platform-local settings do not mutate irrelevant mobile state.

### A-14 · Custom session directory and reparse refusal

Set a fresh absolute local directory and create a session; restart and verify new
cache root. Then attempt nonexistent/denied/relative/root/reparse/junction paths.
Valid directory is created and used; a reparse point is refused explicitly;
failure leaves prior root/settings usable and never writes through an unexpected
target.

### A-15 · ADB locator precedence

Test a valid configured path if UI exposes it, `ANDROID_SDK_ROOT`, `ANDROID_HOME`,
the default LocalAppData Android SDK, PATH, and no ADB in that order with distinct
hash-recorded harmless versions. Product selects documented precedence and
reports actionable absence. An invalid explicit value must not make an unrelated
binary named `adb.exe` from the working directory win.

### A-16 · ADB device-state and topology matrix

Exercise no devices, one authorized, unauthorized, offline, unknown, emulator
plus physical, two physical, disappear/reappear, and serial change. Two-second
refresh preserves selection by exact serial when still present, disables Start
when not capturable, surfaces actual state, and never silently switches a capture
to another sole device. In a separate pass, make `adb devices` slow or hung,
start Refresh, then close the dialog with its close action and Alt+F4. It closes
within the §4.2 dialog-cancellation budget (or truthfully shows a bounded
Cancelling state), terminates the discovery child, and no late callback reopens or repopulates
the closed UI. Reopen the dialog and prove discovery still works.

### A-17 · ADB buffers and format negotiation

Run main/system/crash, each optional events/radio where supported, all selected,
and zero selected. Verify `-D` buffer attribution with known per-buffer markers,
richest supported format ladder, UTC/local policy when modifiers degrade, year/
microsecond precision, and manifest properties. Unsupported buffer/format fails
with device/buffer detail; zero buffers stays in dialog with validation.

### A-18 · ADB pre-roll, duration, and byte limits

Populate buffers with known markers; run pre-roll 0 and non-zero, duration-only,
byte-only, and both. Start boundary and cap are exact within one complete-record
framing allowance; whichever limit wins is named; no overflow/wrap occurs at
large accepted values; automatic stop follows the same finalization path as
manual Stop.

### A-19 · ADB reconnect and numeric resume cursor

During marker traffic disconnect/reconnect USB, revoke/re-authorize once, restart
the device-side transport, and create out-of-order/device-clock-rollback entries
as separate passes. Product records reconnect gaps, makes at most five bounded
attempts with backoff, resumes from last genuine complete timestamp with bounded
overlap, deduplicates according to session identity rules, and preserves all
committed data or fails explicitly.

### A-20 · Device clock and Windows clock/time zone differ

Set Android device at a non-UTC zone and Windows at another zone without changing
real time. Capture with richest format and with the first supported degraded
format. Newest entry renders around device event time, Follow sits at live edge,
session information names source/render policy, and CLI/desktop instants match.

### A-21 · Growing source truncation, rotation, removal, and writer crash

For separate copies, truncate below current offset, rename/create replacement,
delete, crash writer, and hold an exclusive lock. Policy is visibly “stop”; each
source change increments the defect account and ends/recoverably fails without
splicing replacement bytes, busy-waiting, or relabelling the session complete.

### A-22 · Growing source sharing and first-batch timing

Use a writer with `FileShare.ReadWrite|Delete` compatible behaviour, one complete
line then two minutes idle, partial line, then burst. Follow opens while writer
runs; first line publishes within budget; quiet heartbeat/rate is truthful;
polling stays idle; complete line after idle appears promptly; source bytes match.

### A-23 · Concurrent capture/import/query

Run one ADB capture and one growing-file follow while importing large data;
search/filter/export a completed third session. Operations queue only at actual
resource limits and name preparing/running states accurately. UI remains within
budgets; stopping one source never cancels another; final counts verify.

### A-24 · Selection and source context across live refresh

Select an older record while Follow is on, turn Follow off, inspect/copy source,
filter it out and restore it, minimize/restore, reconnect ADB, then stop. Entry
identity/caret/source survive refresh by ID; out-of-scope state is admitted and
offers a way back; every source read resolves to bytes/interruption/retryable
failure, never permanent loading.

### A-25 · File-picker cancellation and refusal paths

Cancel every open/save/folder picker; select wrong type, nonexistent after
selection, read-only destination, existing destination, very long name, root,
UNC, removable drive ejection, and cloud placeholder not hydrated. Cancellation
is silent success with no artifact; refusal is actionable; existing data stays
intact; no modal is orphaned.

### A-26 · Names, Unicode, normalization, and reserved paths

Exercise spaces, emoji, composed/decomposed accents, CJK, RTL content, leading/
trailing dots/spaces, case-only differences, Windows reserved names, and near-
maximum path lengths in sources/session parents/exports. UI names are safe and
distinguishable; generated names avoid reserved/illegal characters; canonical
paths prevent duplicate tabs without conflating distinct files; errors do not
expose internal temporary GUIDs as user names.

### A-27 · Diagnostic bundle review

Enable diagnostics, exercise import/capture/failure/settings, create bundle,
cancel once, and inspect every entry before sharing. It contains declared timing,
counts, system and sanitized session metadata but no raw messages, source paths,
hashes, searches, ADB serials, user name, clipboard text, or undeclared data.
Bundle creation is atomic, `.zip` extension is singular, and source sessions are
unchanged.

### A-28 · Windows update/release-origin communication

The changelog declares an on-demand update command on desktop while current
desktop distribution is a manually downloaded archive. Inspect command presence
against the candidate's documentation. If present, it must make no automatic
network request, say that the build cannot self-update, and open the official
release page only after an explicit act; browser failure is reported. If absent
while still documented, record Fail rather than silently marking N/A.

### A-29 · Upgrade from previous supported release

On P3, inventory/hashes before launch; open sessions/views, create complete and
interrupted captures, set non-default appearance/cache/window/ADB/export values,
then run exact candidate from a new directory. Everything readable remains
correct or migrates atomically; old candidate data is not destructively rewritten
without version policy; window settings validate; sessions/portable exports
verify; interrupted state remains truthful; rollback compatibility is recorded.

### A-30 · Window state and display-topology restoration

Close normally from normal/maximized/minimized states at several sizes, unplug
the prior monitor, change primary monitor/coordinates, then launch. Width/height
persist only from valid Normal bounds; maximized state persists; app clamps to
minimum and appears on a reachable display; dialogs center on current owner and
never restore off-screen.

### A-31 · Lock, sleep, hibernate, and user switch during work

Run separate ADB, growing-file, and import passes. Lock/unlock, display off,
sleep/resume, hibernate/resume, fast user switch, and RDP disconnect/reconnect.
Record which source can physically continue. Committed data survives; Windows/
USB loss is not called parser failure; reconnect/stop is bounded; UI catches up;
no duplicate process/window or stale modal appears.

### A-32 · Multi-instance ordinary use

Launch two exact candidates from same/different extraction roots under one
profile. Import distinct files, change settings alternately, create sessions,
close in both orders. Each window owns its tabs/process; settings remains valid
JSON with a coherent last-writer result; session directories do not collide;
diagnostic logger and cleanup do not access disposed/shared state unsafely.

### A-33 · Multi-instance shared-session conflict

Open the same finalized session read-only in two instances, then try save/export/
view updates and cache deletion around it. Read-only query parity holds; view
sidecar writes remain valid/atomic; cleanup protects open sessions it knows and
never follows reparse targets. If cross-process protection is not implemented,
the plan requires safe refusal or documented limitation—not silent corruption.

### A-34 · Settings corruption, incompatibility, and write recovery

On disposable P2 copies, launch separately with absent, zero-byte, truncated,
invalid-JSON, wrong-root-type, unknown-version, extreme-value, read-denied, and
persistently locked `settings.json`; also leave a stale `settings.json.tmp-*`
sibling. Inventory the file before launch and do not hand-edit the only copy.
VisualCat always reaches a usable safe-default UI or gives one actionable startup
explanation; it never crashes, trusts out-of-range/mobile-only values, deletes
sessions, follows a reparse target, requests elevation, or treats a stale temp as
authoritative. An incompatible/corrupt file is not silently rewritten merely by
being read. After an explicit settings change, the new file is validated and
atomically published, with no stale temp; denied/locked persistence reports that
the choice was not saved while keeping the current session usable. Restore the
original file and prove its settings still load.

---

## 7. Tier X — complex, stress, and soak scenarios

Run X tiers only on a dedicated host/profile/volume with the mutation ledger and
abort thresholds prepared. Keep workloads isolated when measuring them.

---

### X-01 · One-million-line import and interactive analysis

Import `large.txt`; record preview, first plot, throughput, peak/settled resources
and finalization. During ingest repeatedly pan/zoom/search/filter/switch panes and
inspect source. Final oracle must be exact; untouched viewport follows to whole
session; first user navigation hands viewport control to reader; budgets pass.

### X-02 · Five-million-line and configured-limit behaviour

Import `xl.txt` with/without templates and portable raw on a volume with measured
headroom. Record disk amplification, segments, mapped files, time, peak memory,
final compact/verify/reopen. Product completes within available resources or
refuses before unsafe exhaustion with committed partial state recoverable.

### X-03 · Twenty-million-entry live growth

Use a controlled high-rate source until ≥20 M entries when hardware permits.
Measure snapshot cadence, statistics/facet time, UI refresh count, resource
slopes and finalization. Per-refresh query cost must not grow linearly with total
published history; a published segment's cached contribution remains stable.

### X-04 · Interaction and input storm during ingest

While X-01/X-03 runs, continuously resize, change panes, pan/zoom, search/cancel,
toggle filters, page, open/close dialogs, copy and switch tabs for 20 min. No UI
thread exception, lost focus, stale selection, command-slot shift, freeze >1 s,
or source data loss; ETW lost events are recorded.

### X-05 · Four-hour ADB capture endurance

Capture controlled mixed-rate traffic ≥4 h with fixed buffers. Sample every 15
min and interact hourly. Stop once. Marker/loss oracle, gap counters, session
verification, no sustained resource leak, sticky stop, query-during-finalize and
reopen must pass. Keep screen/minimize policy fixed and state it.

### X-06 · Overnight growing-file soak

Follow 8–12 h with long idle windows and bursts. Sample CPU/private bytes/GC/
handles/threads/file I/O. The one-MiB polling buffer is constructed once per
source, idle cost plateaus, no 4 MiB/s allocation pattern returns, first
post-idle line arrives promptly, and final record sequence is exact.

### X-07 · Minimized and obscured capture efficiency

Run comparable 60-min visible, minimized, and fully obscured ADB/follow intervals.
Acquisition rates/counts remain equivalent within source variance; minimized
view stops expensive redraw/query cadence; restore is immediately current; no
Windows “Not responding” or power request persists after stop.

### X-08 · ADB ring-buffer pressure and declared loss

On a dedicated device, record original buffer state, create marker storm and
optional controlled small buffer, then capture with pre-roll/reconnect. Product
must not claim losslessness: `chatty` declared drops, source gaps and reconnect
gaps are counted distinctly; buffer attribution and surviving sequence are
correct; restore device state exactly.

### X-09 · ADB server and transport gauntlet

During capture: kill/restart only the dedicated ADB server, unplug/replug USB,
switch USB mode, revoke/re-authorize, toggle Wi-Fi transport, restart device,
and introduce another device. Each mutation is its own pass. Bounded reconnect
uses original serial only; no indefinite unknown-serial wait, device substitution,
or orphan process; partial data verifies after terminal failure.

### X-10 · Rapid start/stop and limit cycling

Repeat ≥100 ADB captures and ≥100 growing follows, alternating immediate stop,
1-line, 1-second, short duration, and byte cap. Every session gets a unique path,
short captures finalize, no double-start/invisible source appears, ADB children/
handles/threads return to baseline envelope, and Stop remains idempotent.

### X-11 · Concurrent source saturation

Discover the product's actual concurrent-operation limit by starting long
imports/follows/capture until one visibly queues. Keep active sources producing;
cancel only queued work, then release slots in varied order. Queued work is named
preparing, cancellation affects only it, fairness is reasonable, and every
active source finalizes correctly.

### X-12 · Process-kill phase matrix

Terminate the exact PID at preview, materialization, ingest before/after first
snapshot, compaction, manifest replace, portable extraction publication, standard
save, portable save, CSV export, diagnostics bundle, and ADB finalization.
Relaunch and classify exact residue: no unsafe published destination; committed
sessions are recovered/interrupted; temp files are bounded/cleanable; existing
destinations remain intact.

### X-13 · Reopen while finalizing and view-query race

As a large capture stops, rapidly search/filter/page, open its cache/recent card
from another instance where safe, and try reopening as soon as manifest appears.
Readers see an internally consistent snapshot or a bounded retry; no mixed
generation, disposed mapping, false failed capture, or manifest lock crash.

### X-14 · Windows scanner/indexer lock matrix

With Defender/Search normal, then with a controlled test handle, briefly hold
the manifest, destination directory, raw file, and temp rename target during
finalize/save/archive extraction. Transient locks use bounded retry and succeed;
persistent locks fail with file/operation context; cancellation interrupts wait;
no completed ingest is discarded after its data is safe.

### X-15 · Low disk at every publication boundary

On an isolated volume, reduce headroom before raw copy, segment write, snapshot,
compaction, manifest replace, save, archive extract, CSV, and diagnostics. Define
abort floor. Product detects failure, preserves prior valid generation/source,
does not fill system volume via fallback, reports required operation, and can
resume/retry where supported after space returns.

### X-16 · Memory pressure and paging-file variation

In a VM/dedicated host, apply controlled memory pressure with normal and reduced
page file. Import/query/load-all, minimize/restore, stop/finalize. Product slows
without corrupting data; cancellation and close remain reachable; no OOM/WER;
after pressure release resource/latency recovers inside baseline envelope.

### X-17 · Bulk-load completion, cancellation, close, and shutdown

On ≥1 M matches choose the bounded bulk load (`Load up to 100,000` on desktop);
cancel at early/middle/late points; retry to completion; close tab and app while
walking. There is no confirmation to accept: the fill is bounded by the platform
ceiling and takes about 1.5 s on the reference machine, so the control simply
becomes Cancel for the duration.
Cancel keeps rows already loaded and returns promptly; close/shutdown cancels
session lifetime before disposal lock and completes within 5 s; no unclosable
window, ObjectDisposedException or post-close callback.

### X-18 · Deep zoom and precision boundaries

Zoom from hours to minimum microseconds/pixel on empty, 1-entry, 2-entry, dense,
out-of-order and extreme-timestamp sessions at multiple window widths/DPI.
Transforms remain finite/monotonic; axis labels stay within plot, endpoints make
a scale, precision never exceeds pixels/data, selection/half-open ranges are
exact, and pan bounds contain no phantom time.

### X-19 · Paging to the end of huge filtered results

Page forward/back where offered, jump markers first/last/index, run the bounded
bulk load, filter mid-page, and inspect boundary identities. No gaps/duplicates, stable source/
chronological ordering, correct remain count, fixed actions, bounded memory on
ordinary paging, and exact final total.

### X-20 · High session count and cache churn

Create hundreds of mixed complete/interrupted/corrupt/temp sessions with Unicode
names and varied ages/sizes. Cold start, Recent, Session cache, cleanup preview,
open/close, export, and automatic pass. Lists are bounded/responsive; corrupt
entries are isolated; open/capturing protected; size/age math exact; no quadratic
startup or accidental deletion.

### X-21 · Repetition leak pass

Repeat 200 cycles of open medium session → search/filter/inspect → close; 100
dialog/settings cycles; 100 portable open/close cycles. After warm-up, private
bytes, handles, threads, mapped files, temp directories and latency plateau.
Suspected growth is repeated with ETW/handle/mapping evidence before filing.

### X-22 · Multi-instance collision soak

For 2 h, two instances import/save/export/change settings while a third read-only
instance reopens finalized sessions. Include simultaneous close and diagnostic
events. Settings/view/manifest files always parse; no temp-file accumulation,
cross-process deletion, logger disposal fault, path collision or wrong-window
notice occurs.

### X-23 · Display/GPU/RDP transition gauntlet

With a large session and active capture, move across mixed-DPI/refresh/HDR
monitors, rotate, change primary, unplug/replug, toggle HDR, lock/unlock, connect/
disconnect RDP, and update resolution. No renderer crash/device-loss hang,
off-screen owner dialog, incorrect hit testing, stale scale, white/black surface,
or capture loss; each transition settles within budget.

### X-24 · Long path, high Unicode, and storage-provider soak

Run import/save/archive/export/reopen repeatedly from long local NTFS, another
volume, removable, UNC and cloud-synced paths. Classify support per source. Local
supported paths preserve hashes/atomicity; disappearance/placeholder/conflict is
reported; no path truncation, case collision, hidden fallback or UI freeze.

### X-25 · Large export and diagnostics denial-of-service

Export all rows of XL to CSV/portable; create diagnostics with huge metadata,
many sessions, long names and full temp destination; cancel at stages. Size/time
and free-space bounds hold; progress/cancellation remain usable; temporary files
are removed; source sessions are never evicted/corrupted; completion row count
and output hash/oracle pass.

### X-26 · Session corruption and verifier matrix

Open separate copies with each manifest/schema/checksum/column/bitmap/string/
raw/view fault. Major incompatibility and analytical corruption are refused;
missing/changed external raw opens only in explicit degraded mode where safe;
malformed view sidecar is ignored; no crash, arbitrary allocation, directory
escape, or rewrite of evidence.

### X-27 · Clock/zone changes during live work

During ADB and growing capture, change Windows time zone, DST boundary simulation
in a VM, and wall clock forward/back separately; leave source timestamps fixed.
Stored instants/source policy remain deterministic; rendering updates or remains
explicitly based on session policy; rates/durations use monotonic time; cache
retention and file names do not delete/misorder sessions due to clock jumps.

### X-28 · Host reboot and crash-recovery handoff

On a disposable VM with synthetic data, separately perform normal sign-out and
normal reboot during active work, then hard VM power-off during active ADB/follow
after a committed snapshot. Record whether Windows allowed the app to finish or
terminated it; no modal may strand shutdown indefinitely. On return, candidate
launches; a gracefully completed session verifies, otherwise the partial session
is recoverable and labelled interrupted; no session is falsely complete; ADB
child is absent; cache/diagnostic files are bounded; restore original automatic-
start, sign-in, and power policy.

---

## 8. Tier U — Windows UX, UI, input, and accessibility

Run U with ordinary human interaction first, then automation/accessibility tools.
A tree dump cannot prove that a workflow is understandable or usable.

---

### U-01 · Window-size and responsive-command matrix

Exercise 900×600 minimum, 1024×768, 1280×720, 1366×768, 1440×900, 1920×1080,
2560×1440, 4K, maximized, and snapped half/third layouts at 100% scale. Command
bar keeps primary Open log/ADB live inline; flexible actions fold into More
without clipping; status/notice/tab strips remain reachable; plot, minimap,
entries and source stay inside their bands; no horizontal window overflow.

### U-02 · Per-monitor DPI matrix

Repeat key screens at 100, 125, 150, 175, 200 and 300% where hardware/Windows
allows. Record logical and physical bounds. Text, icons, borders, hit targets and
timeline pixels scale consistently; no fractional-pixel blur that impairs text,
subpixel gaps, clipped spinner/buttons, or mismatch between pointer and visual.

### U-03 · Mixed-DPI monitor crossing

Use two monitors with materially different scale and resolution. Move window
fully and straddled across boundary; open picker/import/ADB/settings/confirmation
before and after move; maximize on each. Owner dialogs/pickers appear on active
monitor at correct scale; hit testing and screenshot coordinates remain aligned;
window never jumps or becomes unreachable.

### U-04 · Multi-monitor coordinates and hot-plug

Place secondary left/above primary (negative virtual coordinates), close there,
unplug it, relaunch, reattach and change primary. Window/dialogs remain reachable;
saved size/maximized state is honored without restoring an invalid position;
taskbar thumbnail and Alt+Tab identify the candidate.

### U-05 · Keyboard contract, accelerators, and focus order

Execute every row in [`KEYBOARD.md`](KEYBOARD.md), including timeline J/K/F,
marker wrap, Alt+1..4, Ctrl shortcuts and Escape precedence. Traverse every
dialog/control with Tab/Shift+Tab/arrows/Space/Enter. Focus order follows command
bar → search/severity → timeline → analysis; disabled items are skipped or
explained; no shortcut fires while ordinary text is typed.

### U-06 · Narrator end-to-end pass

With Narrator and scan mode/forms interaction as appropriate, cold-launch,
import, use preview, search, filters, timeline, entries, inspector/source,
templates/facets/statistics, More, settings/cache/recent/diagnostics, export and
ADB dialog. Every command has concise name/role/state/help; rows announce level,
tag, time, message—not record dumps, GUIDs, raw spans or private paths; counts and
failures announce once; focus follows actions; primary journey is completable.

### U-07 · Automation tree and modal boundary

Inspect realized and virtualized controls before/after scroll. Names update when
containers recycle; search/timeline expose help; numeric spinner children say
increase/decrease and field name; disabled commands say why. With an owned modal
open, automation focus cannot walk or invoke the workspace behind it; closing
returns focus to invoker.

### U-08 · Windows contrast themes and product high contrast

Run Windows Aquatic/Desert/Dusk/Night Sky (or current built-ins) with System
theme, then VisualCat High contrast on/off in light/dark. Text/control/focus/
selection/plot severity/caret remain distinguishable without color alone;
system forced colors do not make content transparent; product high-contrast
selection is visible. Measure normal/large text and required focus, selection,
control, icon, caret, minimap, and severity-graphic states against the §4.4
4.5:1/3:1 floors; screenshots alone are insufficient.

### U-09 · Light, dark, and System theme live changes

Change Windows app mode while VisualCat follows System, then force Light/Dark and
switch in app with sessions/dialogs/notices open. Whole product repaints in one
transition: command/brand/tab/menu/dialog/picker-host/workspace/list/metadata/
minimap/source/notice. Product selection/focus palette remains its own and no
surface retains other-theme colors or needs restart.

### U-10 · Windows text size and VisualCat text scale

Test Windows text-size 100/125/150/200% where it affects Avalonia, Windows display
scale separately, and product text scale 0.75/1/1.25/1.5/2. Change product scale
during import/capture. All session content—not only chrome—updates; controls and
dialogs reflow/scroll; entry floor and source readability hold where space
exists; active source/session/selection/filter remain; no 10 s blank rebuild.

### U-11 · Magnifier, color filters, pointer, and caret aids

Use Magnifier docked/lens/fullscreen at 200–500%, grayscale/inverted/color-
blindness filters, larger pointer, pointer trails if supported, and text cursor
indicator. Focus/caret/selection/timeline caret remain findable; Magnifier tracks
keyboard focus; no tooltip/modal opens off magnified viewport; color is not the
only severity signal.

### U-12 · Reduced motion and Windows animation setting

Disable “Show animations in Windows” and exercise menus/dialogs/tabs/notices/
timeline navigation; then enable. Product remains usable and does not require an
animation to communicate completion/failure. Any animation not following system
policy is recorded; motion never blocks input or causes flashing above accepted
accessibility limits.

### U-13 · Mouse and precision-touchpad interaction

Test left/right/middle click, double-click speed extremes, wheel lines/page mode,
horizontal wheel, precision touchpad two-finger pan/pinch, drag threshold, and
high-DPI pointer. Context/tooltip behaviour is correct, selection is not doubled,
timeline gestures do not scroll the whole window unexpectedly, and action hit
regions match visuals.

### U-14 · Touch and pen (where hardware is available)

At 150–250% scale use tap, double-tap, press/drag, pinch, pen barrel/hover, and
touch keyboard. Ordinary desktop controls are operable without accidental tiny
targets; mouse hover is not the sole route to information; touch does not create
phone-only UI; pen/touch hit testing follows per-monitor DPI. Record N/A if the
release does not claim touch and no hardware exists.

### U-15 · IME, keyboard layouts, dead keys, and clipboard

Use at least English, Czech/German dead keys, and CJK IME in search, view name,
paths, year/time-zone and any name fields. Composition text is not prematurely
searched/saved; Enter commits in correct layer; shortcuts use intended physical/
logical key behaviour; Ctrl+C copies exact selected scope; clipboard errors are
reported without losing selection.

### U-16 · Locale, number/date culture, and RTL content

Run UI culture/format combinations with comma decimal, non-English short date,
12/24-hour clocks, and an RTL Windows display language/profile where practical.
English interface uses its declared display culture consistently; ISO/session
data remains deterministic; counts/grouping/date/time do not mix cultures;
RTL log content stays inside message/source and cannot reverse surrounding UI.

### U-17 · Dialog ownership, Alt+Tab, Escape, and system close

For import preview, ADB, Recent, Appearance, Cache, Diagnostic confirmation,
export scope, number prompt, recovered session and delete confirmation: Alt+Tab
away/back, minimize owner, press Escape, Enter default, Alt+F4 dialog, and close
owner. Exactly one layer closes; no orphan taskbar window or enabled owner behind
modal; cancellation is not shown as failure; system close disposes safely.

### U-18 · Notice lane and status messaging

Trigger info/completion/failure/progress, long scanner/ADB/path errors and rapid
replacement. On desktop text is readable, expands where promised, stays within
window, does not cover actions or shift a control under a repeated click, uses
appropriate persistence/dismissal, and Narrator hears the same sentence once.

### U-19 · Empty, loading, quiet, partial, degraded, and failed states

Capture screenshots/tree/speech for: no sessions, empty file, import preview,
preparing queue, ingest, quiet ADB/follow, first snapshot, no filter match,
off-timeline records, interrupted recovery, missing raw source, corrupt session,
ADB absent/unauthorized/offline, low disk and finalizing. Each state names what is
happening, offers only viable actions, and never builds an inert full workspace
over a terminal failure.

### U-20 · First-run comprehension with fresh participant

With consent and synthetic data, recruit representative Windows log-analysis
participants who have not used VisualCat. Give each the unopened candidate ZIP,
checksum/release notes, small log and goal: “verify the download, find the crash,
inspect its exact source, export errors.” Do not coach extraction, labels, routes,
or controls. Record each participant's Windows/CLI familiarity separately from
product observations, plus milestone time, wrong turns, backtracks, mis-clicks,
moderator interventions, help/document use, security-prompt understanding,
whether local-first/no-telemetry is understood, and whether ADB vs file routes
are distinguishable. The participant must complete every
milestone with zero unsafe security action, destructive error, unexplained dead
end, or moderator rescue; otherwise Fail. Recoverable confusion is still a
finding with the observed cost. Use at least three independent first-time
participants for a release UX gate, report individual results and median time,
and do not record face, voice, name, or screen content without explicit consent.

### U-21 · Visual regression sweep

At fixed host/display/corpus capture canonical empty, preview, ingest, full plot,
zoom/range, filters, templates, facets, statistics, entry/source, More, every
dialog, notice types, failure/recovery, ADB, cache and diagnostics screens in
light/dark/high contrast at 100/150/200%. Compare geometry/color/type/icon/clipping
to accepted references under §4.4. Include hover/focus/pressed/disabled/selected
and long/error states; retain native originals, masks, diff images, comparison
metadata, and explicit approval for any baseline replacement. Allow only
documented font-rasterization variance, not blanket pixel tolerances that can
hide clipping, stale theme, or one-pixel hit-target drift.

### U-22 · Zoomed and long-content layout

Use maximum app text scale, long localized/user/source names, 2 MiB message,
large counts and path errors at minimum window. Wrapping/ellipsis drops whole
facts rather than half glyphs; tooltips/accessibility expose clipped desktop
content; scrollbars reach last control; pinned action rows do not cover form;
status and tab close remain reachable.

### U-23 · Dynamic accessibility announcements

With Narrator, start/stop capture, receive first data, go quiet, reconnect, finish
import, fail source, export, clear filters and change match position. Important
state changes are announced once at useful priority; per-line/progress chatter
does not flood speech; focus is not stolen; dismissing a notice stops stale
re-announcement.

### U-24 · Taskbar, system menu, and window lifecycle

Verify icon/title in taskbar, Alt+Tab and Task Manager; Snap Layouts, Win+Arrow,
minimize/maximize/restore, system menu, Show desktop, virtual desktop move and
Alt+F4. One main window remains identifiable; capture semantics match minimize;
system close waits/disposes within budget; no tray/background process is implied
or left because none is claimed.

---

## 9. Tier I — CLI, Android, and artifact integration

Tier I makes exactness claims. Use hashes, manifests and normalized machine-
readable output rather than visual estimates.

---

### I-01 · Matching Windows CLI artifact identity

Verify CLI ZIP checksum/provenance/layout/notices; extract fresh; run `--version`
and `help` from multiple working directories without system .NET. Version matches
desktop/release; stdout/stderr/exit codes follow [`CLI.md`](CLI.md); archived help
matches checked reference; no unexpected network/elevation/runtime dependency.
Then walk [`CLI.md`](CLI.md) command by command and run every option it lists and
every example it prints against this binary. An option the reference documents
but the shipped `vcat.exe` answers with `does not take` is a Fail against the
published contract, and a documented example that cannot run is the same finding
in a more visible place. The archived usage line is not a substitute: it states
one usage per command and stays silent about the options the reference lists
under it, so a reference that has drifted past the parser passes that check.

### I-02 · File import desktop/CLI parity

Index every ordinary/adversarial finite corpus with candidate CLI using the same
format/year/zone/templates/portable settings accepted in desktop. Compare full
manifest, outcome/severity counts, first/last, facets, templates, query identities
and selected raw spans. All exact fields match; UI-only view state is excluded.
Where the desktop preview offers an import choice the shipped CLI has no option
for, record the pair as unreachable parity and name the missing option rather
than substituting a different setting on one side; an import setting only the
GUI can express is a coverage gap in this row and a claim for §2.9.

### I-03 · Desktop save verified by CLI

For standard and portable desktop saves, run `vcat verify` (with/without
`--skip-raw` where meaningful), info/stats/query/search/templates/export. Standard
source coverage reflects external identity; portable verifies independently;
view sidecar does not change analytical identity.

### I-04 · CLI session opened and saved by desktop

Create standard, portable directory and portable ZIP with CLI; open in desktop,
apply/save view, save standard/portable, then verify all with CLI. Counts,
templates, raw hashes and session identity rules persist; desktop never rewrites
the CLI source session merely by opening it.

### I-05 · Marker-bounded desktop/CLI ADB parity

Use sequential captures against the same device/configuration and deterministic
marker intervals, or feed identical finite captured bytes. Compare marker set,
buffer, PID/TID/tag/level/message/time provenance, drops/gaps, process-name
ranges and manifest source properties. Exact finite input must be exact; live
interval differences are explained by marker boundaries, never waved away.

### I-06 · Desktop and CLI reconnect semantics

Apply the same serial disappearance/reconnect/clock rollback schedule. Both use
bounded attempts, numeric time-safe cursor, genuine last timestamp and declared
gap accounting; neither waits indefinitely for an unknown serial or switches
device. Differences in user-facing text are acceptable; data semantics are not.

### I-07 · Export equivalence

For the same session/filter/range/order, compare desktop CSV to CLI CSV after
normalizing only deliberate BOM/newline policy; compare row identities/fields
exactly. Also generate CLI raw, templates and stats reports and cross-check UI
counts. Desktop does not claim report/raw exporters it does not expose.

### I-08 · Portable round trip through Android

Desktop portable ZIP → Android open → Android portable share/export → Windows
desktop open → CLI verify. Every hop preserves counts, template identities, raw
hash/source offsets, time policy and representative queries. Android-specific
view layout may differ; analytical identity may not.

### I-09 · Simultaneous desktop and Android/CLI capture

Where device capacity permits, run desktop ADB, CLI ADB and Android companion
capture with uniquely marked windows. Stopping one does not stop or steal another
transport; each declares its own gaps/scope/buffers; no desktop process assumes
exclusive global ADB ownership. Exact parity uses overlapping markers, not totals.

### I-10 · Startup argument dispatch parity

For each file/session/archive path, compare GUI launch classification with CLI
`info`/verify classification. Case-insensitive extensions and bare arguments are
handled consistently; unsupported switch/path is rejected visibly; command-line
path is passed as one argument and never shell-evaluated.

### I-11 · Cross-culture/time-zone reproducibility

Index/open same corpora under two Windows cultures/time zones and on Android/CLI
where available, using explicit policy. Analytical instants, templates, counts,
CSV invariant fields and hashes are deterministic; only documented rendered
local-time/user-format surfaces vary.

### I-12 · Published archive rehearsal

Run `tools/verify-package-contents.ps1` against the exact desktop and CLI assets,
then repeat human B-01/I-01 from extracted bytes. Automated layout presence does
not launch desktop; the live run closes that gap. Record archive/extracted sizes,
entry count, notice/version agreement and any bloat vs previous release.

### I-13 · Windows console, pipelines, exit codes, and cancellation

Run the extracted `vcat.exe` from PowerShell 7, Windows PowerShell 5.1, and
`cmd.exe` under ordinary and UTF-8 console configurations, from paths containing
spaces/Unicode. Exercise success, invalid input, corrupt verification, missing
file/general failure, pathological regex, long NDJSON query, redirected stdout/
stderr, a pipeline consumer that exits early, and Ctrl+C during index and ADB
capture. Exit codes are exactly `0/2/3/1/130` per [`CLI.md`](CLI.md); diagnostics
and optional TTY progress stay on stderr, structured JSON/NDJSON or the promised
absolute path stays alone on stdout, and redirected output has no prompt, ANSI,
progress, partial JSON object, or locale-dependent encoding corruption. Query
streams incrementally with bounded memory; a broken pipe and Ctrl+C terminate
children promptly and retain only documented recoverable session generations.
`VISUALCAT_DEBUG=1` changes exception detail only after explicit opt-in, never
mixes it into structured stdout, and all resulting evidence is treated as
sensitive.

### I-14 · Deterministic test-log generator and format matrix

Using the exact candidate CLI, generate two independently named files for each
`threadtime`, `time`, `brief`, `long`, and `epoch` value with the same non-default
line count and seed; repeat one pair through positional output syntax and another
supported OS CLI when available. Generate a third file with a changed seed, then
exercise `--help`, an invalid format, zero/negative counts, and an unwritable
destination in a clean directory. Same-options pairs are byte-identical and,
where cross-platform evidence exists, cross-OS identical; the changed seed differs.
An independent grammar/count oracle and `vcat info` identify the requested format,
exact record count, and valid timestamps, and index/query/verify cover every
source byte. In particular, `--format long` produces genuine framed long-format
records, never threadtime fallback. Success prints only the absolute output path
and exits `0`; rejected input exits `2`, a destination failure exits `1`, and no
failed/help invocation creates or truncates the default or requested output.

---

## 10. Tier P — privacy, security, and negative scenarios

Use only synthetic secrets (`VCAT_SECRET_<run-id>`) and dedicated accounts/
volumes. Security tests must not weaken or attack systems outside scope.

---

### P-01 · No unsolicited network traffic

With a clean profile, capture per-process DNS/TCP/UDP using Windows Firewall
logging, Resource Monitor/ETW, or an approved packet tool while cold-launching,
importing, querying, saving, diagnostics and sitting idle 15 min. Repeat with ADB
disabled and enabled, distinguishing ADB local/device traffic. Expect no
VisualCat-originated telemetry/update/content upload. An explicit releases-page
action may launch the default browser; the app itself must not fetch it silently.

### P-02 · Data locality and declared storage

Trace file I/O for ordinary workflows. Writes stay in selected destinations,
`%LOCALAPPDATA%\VisualCat\{Sessions,Diagnostics,settings.json}`, and bounded temp
materialization roots. No source payload is written to registry, executable
directory, Documents/Desktop, another profile, roaming AppData, or network
location without explicit selection.

### P-03 · Diagnostic redaction

Place synthetic secret in message, source path, search, saved-view name, ADB
serial-like token and clipboard. Generate bundle and search raw ZIP entries plus
compressed/binary strings. None appear; no hash/path enables easy payload
recovery beyond declared sanitized metadata; confirmation exactly matches
contents. A redaction failure is Blocker.

### P-04 · Portable archive traversal and expansion safety

Open §3.2 archives containing parent/absolute/drive/UNC/ADS names, symlink/
reparse metadata, duplicate and case-colliding paths, excessive count/depth/name,
encrypted entries, corrupt central directory and expansion bomb. Each is refused
before unsafe publication; nothing appears outside exact temp root; disk/time
limits hold; temp data is removed; source archive is unchanged.

### P-05 · Untrusted log rendering

Import control/bidi/NUL/ANSI/HTML/Markdown/CSV-formula-like strings, huge tokens
and synthetic URLs. They render/copy as data; no control changes surrounding
layout, launches URI/process, executes terminal escape, changes direction of UI,
or creates active content. CSV safely quotes fields; document spreadsheet-formula
policy and test it without opening in an unsafe spreadsheet configuration.

### P-06 · Session verifier and parser resource bounds

Open malicious lengths/counts/offsets/checksums, huge declared arrays/string
tables, recursive/invalid JSON, unsupported major schema and overlapping raw
spans. Product refuses before arbitrary allocation/out-of-bounds mapping; no
native access violation, endless CPU, path escape, or partial rewrite; error
does not echo unbounded attacker text.

### P-07 · NTFS reparse, junction, symlink, hard-link, and case boundary

Inside a dedicated subtree, place links at session root, cache root, save temp,
archive destination and child entries; use case-only/8.3 aliases where enabled.
Configuration refuses reparse cache root; `--force`/cleanup/save/archive never
traverse outside validated root or overwrite a linked victim; hard-link policy is
explicit and safe. Verify intended victim hashes before/after.

### P-08 · ACL and standard-user boundary

Under a non-admin user, test read-only source, execute-only/readable program
folder, denied cache/destination, inherited deny, ownership by another test user,
and UAC virtualization-sensitive locations. Product never requests elevation,
weakens ACLs, takes ownership, or writes via virtualization silently; it explains
the denied operation and preserves existing data.

### P-09 · Controlled Folder Access and antivirus

On an authorized disposable host, enable normal CFA policy and target a protected
folder; quarantine/scan a harmless known test artifact only through approved
security tooling. VisualCat reports OS denial, does not advise disabling security
globally, does not fall back with payload elsewhere, and succeeds after user
chooses an allowed destination. Scanner transient locks follow W7/X-14.

### P-10 · Temporary-file and atomic-publication boundary

Observe settings, manifest, session save, archive extract, CSV and diagnostics
with Procmon. Temp names are unpredictable enough for local race resistance,
created under intended parent, closed/flushed before replace, and cleaned on
success/failure. A published destination is old-valid or new-valid, never
attacker-controlled mix; cancellation cannot replace an existing file partially.

### P-11 · Process creation and command-line safety

Inspect VisualCat and child ADB executable path/command line for files with shell
metacharacters and serial-like punctuation. `UseShellExecute=false`/argument
boundaries prevent interpretation; no log content/search/secret is placed on a
child command line; ADB path resolves to recorded executable; child has no
unexpected shell (`cmd.exe`/PowerShell) intermediary.

### P-12 · DLL search and executable-directory integrity

In an isolated VM only, compare candidate inventory before/after and use approved
inert canary DLL names to audit load attempts with Procmon. VisualCat must load
shipped/system libraries from intended paths and not execute a user-writable
current-directory impostor. Do not create a functional malicious DLL. Unexpected
load path is a security finding; archive files must remain hash-identical.

### P-13 · Mark-of-the-Web and SmartScreen honesty

Compare browser download + Explorer extract, PowerShell extract, copy via USB,
and explicitly unblocked controlled copy. Record Zone.Identifier propagation and
actual warning. Product docs never instruct bypass before checksum/provenance;
unsigned artifact never claims signed publisher; warning absence caused by
reputation/MOTW loss is not called a product pass or fail by itself.

### P-14 · Clipboard and sensitive display

Copy message/raw/source selection; inspect clipboard history/cloud-sync policy,
paste exact result, then copy unrelated text and close. Copy is explicit and
scope-exact; VisualCat does not monitor or re-read clipboard; no hidden bulk copy.
If Recents thumbnails/screenshots are not protected, record it as documented
desktop policy rather than assuming secrecy. Never use real secrets.

### P-15 · Crash dumps, Event Log, and error redaction

With synthetic payload and approved local dump policy, induce a controlled
handled import failure and isolated crash build if needed. User-visible error,
product diagnostics and Windows Event Log do not disclose more payload/path than
their contract. OS crash dumps may contain process memory/log data: evidence
handling explicitly treats them as sensitive and they are never auto-uploaded by
VisualCat.

### P-16 · Cache cleanup cannot escape or delete active data

Mix protected open/capturing sessions, external sessions, reparse entries,
read-only/locked files, name collisions and policy thresholds. Preview and
recomputed execution target exact eligible cache children only; failures leave
remaining valid; no source, saved session, evidence root or another user's path
is touched; wording admits deletion is not forensic erase.

### P-17 · Multi-user/profile isolation

Use two local Windows test users. Each runs same extracted candidate and creates
distinct settings/cache/diagnostics; attempt to open other's data under normal
ACLs; delete candidate program directory from shared/read-only location. Local
AppData is per user; one recent list/settings does not expose another; access
denial is respected; shared portable exports are readable only through explicit
ACL/share choice.

### P-18 · Portable removal and residue accounting

Because current product has no installer, “remove” means stop all instances,
delete exact extracted program directory, and separately decide whether to keep
or delete `%LOCALAPPDATA%\VisualCat`. Program deletion leaves no service,
scheduled task, startup entry, file association, firewall rule or background
process created by VisualCat. User data remains until explicitly removed and is
accurately documented; no claim of uninstall erasure is made.

### P-19 · ADB authority and shared-server boundary

Verify VisualCat uses the selected existing ADB authority and exact serial,
does not silently authorize a device, enable debugging, clear buffers, kill a
shared server on normal start, or persist an undeclared secret. Unauthorized/
offline/revoked states are visible; stopping capture kills only its logcat child,
not unrelated IDE/CLI/device activity.

### P-20 · Export/path disclosure and error amplification

Use very long/untrusted file and device names and denied paths. UI/diagnostics/
bundle bound text and sanitize private portions according to contract; exported
files contain only requested log scope and declared metadata; an error cannot
inject another command, hyperlink, newline-spoofed success, or huge diagnostic
payload.

---

## 11. Tier R — regression pack for released and current fixes

These guards derive from [`CHANGELOG.md`](../CHANGELOG.md) and current source
comments. Re-derive the table whenever a release adds a Windows-visible fix.
“First fixed” names the changelog section that records the behaviour; `Unreleased`
means it must pass before the next tag.

| ID | Guard | Procedure | Pass condition | First fixed |
|---|---|---|---|---|
| **R-01** | Stop is answered and sticky | B-10 and X-05 | Button/status never return to Capturing; ending resolves and verifies | Unreleased |
| **R-02** | Final query cannot fail capture | X-13, search while finalizing | Finished capture is not relabelled failed by a superseded query | Unreleased |
| **R-03** | Scanner/indexer lock is tolerated | X-14 | Bounded transient lock succeeds; cancellation/persistent failure is truthful | 2.0.9 |
| **R-04** | Idle growing follow does not churn LOH | X-06 | One reusable read buffer; no ~4 MiB/s idle allocation/gen2 cadence | 2.0.9 |
| **R-05** | Closing during a bulk load is prompt | X-17 | Tab/app closes within 5 s without waiting for all rows or throwing | 2.0.9 |
| **R-06** | Live statistics/facets do not rescan history | X-03 | Per-refresh cost plateaus with cached published segments | 2.0.9 |
| **R-07** | Diagnostic logger is safe at shutdown | B-17, X-22 | Late failure cannot write into disposed sink or root process lifetime | 2.0.9 |
| **R-08** | Displayed version tracks artifact | B-01/B-02 | UI/file/README/release agree; non-release says `-dev` | 2.0.4 |
| **R-09** | Capture/session names distinguish runs | B-16 | Tabs, Recent, Cache and filenames contain unambiguous source/start identity | 2.0.4 |
| **R-10** | Settings labels are human language | A-13 | No `PerRow`, `GlobalViewport`, `SourceSequence` implementation labels exposed | 2.0.4 |
| **R-11** | Actions report in notice lane | Copy/mute/save/export | Every durable/meaningful action reports where reader is looking | 2.0.4 |
| **R-12** | Empty state is useful | B-02/B-16 | Correct desktop actions and recent sessions; no inert session command | 2.0.4 |
| **R-13** | Fit is directly reachable and exact | B-07 | One action/key fits; no filter drawer dependency or geometry jump | 2.0.4 |
| **R-14** | Failed import is not hollow workspace | A-08/A-25 | One reason/remedy with viable actions; no inert panes | 2.0.4 |
| **R-15** | Import ends fitted until user navigates | X-01 | Untouched viewport follows whole import; first navigation takes ownership | 2.0.4 |
| **R-16** | Closing a tab cannot crash plot | A-05/X-21 | No queued redraw reads disposed snapshot; process survives | 2.0.4 |
| **R-17** | Source context always resolves | B-04/A-24 | Bytes, interruption or retryable failure—never permanent Reading | 2.0.4 |
| **R-18** | Double-click zoom has one meaning | B-07 | Zoom only; no cell filter/list rescope | 2.0.4 |
| **R-19** | Axis remains a scale inside plot | U-01/X-18 | Labels do not overlap minimap; narrow views label endpoints | 2.0.4 |
| **R-20** | Context actions keep slots | B-08/X-19 | Paging/load controls never move Copy raw/Entry between taps | 2.0.4 |
| **R-21** | Counts name population | A-02 | Session/filter/viewport/off-timeline scopes visible, not tooltip-only | 2.0.4 |
| **R-22** | Culture is internally consistent | U-16/I-11 | Dates/numbers do not mix device conventions into one English surface | 2.0.4 |
| **R-23** | Nearly empty plot does not overclaim | X-18 | Pixel/data precision clamp; one instant not printed as two false labels | 2.0.4 |
| **R-24** | Live refresh preserves selected entry | A-24 | Entry and caret restored by ID; source remains reachable | 2.0.3 |
| **R-25** | Desktop full message is reachable | B-04 | Selected row/inspector show whole message; clipped cell tooltip does too | 2.0.3 |
| **R-26** | Short captures finalize | X-10 | Immediate/one-line/one-second captures all produce valid final manifests | 2.0.4 |
| **R-27** | Quiet status stops claiming arrivals | A-22 | Last-second rate falls to zero and heartbeat names silence | 2.0.4 |
| **R-28** | Follow belongs only to active source | B-10/B-11 | Follow/new-data disappear when source closes; re-engage opens live-edge span | 2.0.4 |
| **R-29** | ADB time-zone follows negotiated format | A-17/A-20 | UTC modifier degradation cannot shift every timestamp by host/device offset | 2.0.4 |
| **R-30** | ADB wrong/unknown serial cannot hang | A-16/X-09 | Preflight rejects missing serial before spawning logcat | 2.0.5 |
| **R-31** | ADB buffer attribution is per record | A-17 | `-D` boundaries yield exact main/system/crash/event/radio facet | Unreleased |
| **R-32** | Startup restore/cancel is not failure | B-15 | Successful open never remains Opening or shows cancellation as startup error | 2.0.5 |
| **R-33** | Source line and continuation offsets are exact | A-08 | Gutter starts at 1; selected line and following context remain visible | 2.0.5 |
| **R-34** | Screen reader hears entries, not dumps | U-06 | Level/tag/time/message only; no GUID/raw span/path | 2.0.4 |
| **R-35** | Session commands disable honestly | B-02/U-07 | Save/export/lines commands unavailable without applicable session and say why | 2.0.4 |
| **R-36** | Theme repaints whole desktop | U-09 | No stale command/tab/list/minimap/source/dialog variant | 2.0.4 |
| **R-37** | Product owns selection accent | U-08/U-09 | Selection/focus comes from product palette, remains visible in contrast | 2.0.4 |
| **R-38** | Notice cannot move repeated action | U-18 | A second click at same action remains same action | 2.0.4 |
| **R-39** | Entry row uses available width | U-22 | Unselected row ellipsizes near actual width; selected row wraps budget | 2.0.4 |
| **R-40** | Filtered-out inspected entry is admitted | A-24 | Entry/Copy raw agree and UI offers return to it | Unreleased |
| **R-41** | Reopened finished capture shows whole capture | B-16 | No stale 30-second Follow window or zero-count false empty view | Unreleased |
| **R-42** | Panning is bounded to session | B-07/X-18 | No phantom seconds beyond either end | Unreleased |
| **R-43** | One-line/empty source has one coherent result | A-08/U-19 | EOF line appears; empty file produces one clear explanation | Unreleased |
| **R-44** | Settings writer preserves newest value | A-13/A-32 | Coalesced workspace writes cannot overwrite a newer preference | 2.0.9 |
| **R-45** | Atomic manifest replacement survives readers | X-13/X-14 | Reader/scanner cannot cause UnauthorizedAccessException or discard ingest | 2.0.4/2.0.9 |
| **R-46** | Cache cleanup protects open sessions | A-12 | Restore/protection precedes cleanup; preview is recomputed | 2.0.5 |
| **R-47** | Regex error is a product sentence | B-06 | Trimmed Release never exposes resource key/framework dump | 2.0.5 |
| **R-48** | ADB missing message is actionable | A-15 | Names platform-tools/SDK configuration; no inert dialog | 2.0.0 |
| **R-49** | Off-timeline evidence is discoverable | B-19 | Timed/untimed/unparsed populations are explicit; chip and command open exact source-ordered lines | Unreleased |
| **R-50** | Source gutter codes have a visible legend | B-19/U-06 | Every non-ordinary code is explained on screen and accessibly, never tooltip-only | Unreleased |
| **R-51** | Text scale reaches the active session | A-13/U-10 | Chrome and every open workspace remeasure together without replacing the session or capture | Unreleased |
| **R-52** | A clipped session tab remains closable | A-05/U-01 | First action brings the tab into view; its close action then works and never stays silently disabled | Unreleased |
| **R-53** | Search can reach first, last, and numbered match | B-06 | Direct controls land on the exact oracle identity without thousands of steps or zoom drift | Unreleased |
| **R-54** | The bulk row load is explicit, bounded, and cancellable | X-17/X-19 | Action names the platform ceiling and the remaining rows, stops at that ceiling and says so, streams progress, and cancels promptly | Unreleased |
| **R-55** | Export can ignore the active filter honestly | B-14/I-07 | Everything-in-session scope appears when distinct and contains the exact unfiltered row set | Unreleased |
| **R-56** | Manual update route matches install origin | A-28/P-01 | Desktop command is present when documented, never checks silently, and opens the official page only on request | 2.0.9 |
| **R-57** | Test-log generator honors requested format | I-14 | All five formats are deterministic and detected exactly; `long` never falls back to threadtime | Unreleased |

---

## 12. Execution schedules

| Schedule | Role | When | Contents | Planning time |
|---|---|---|---|---|
| **Artifact smoke** | Cumulative entry gate | Every Windows candidate ZIP | B-01–B-08, B-12, B-14/B-15, B-17–B-19; I-01/I-12/I-14 | 3–4 attended h |
| **ADB smoke** | Focused reusable slice | Every capture/ADB change | B-09–B-11; A-16–A-20; R-01, R-26–R-31 | 3–5 attended h |
| **Standard** | Cumulative sharing gate | Before candidate is shared | All B; applicable R except endurance rows; U-01, U-02, U-05, U-09, U-18, U-19; X-01 | 1.5–2.5 person-days |
| **Upgrade** | Conditional supplement | Whenever settings/session/schema/storage/runtime/package compatibility changes | A-03, A-10–A-14, A-29, A-30, A-32–A-34; I-02–I-04 on migrated data | 1 person-day plus setup |
| **Full Windows** | Cumulative release core | Before release tag | Every applicable B/A/U/I/P/R; X-01, X-02, X-04, X-10–X-19, X-24–X-27; exact asset authoritative | 5–8 person-days plus unattended runs |
| **Soak** | Mandatory release supplement | Before release tag on dedicated host | X-03, X-05–X-09, X-20–X-23, X-28; no overlapping measurement workloads | 30–50 elapsed h, 8–12 attended h/review |
| **Accessibility** | Focused reusable slice | Before release and after presentation changes | U-01–U-24, including three independent fresh-participant sessions for U-20 | 1–2 person-days plus participant sessions |
| **Security/storage** | Focused reusable slice | Before release and after archive/path/cache changes | W3/W4/W7/W8; X-12, X-14–X-16, X-24–X-26; all P | 2–4 person-days on isolated host |
| **Parity** | Focused reusable slice | Parser/store/export/session/ADB changes | All I plus A-06–A-10, A-17–A-20, X-26 | 1–2 person-days |
| **Windows expansion** | Conditional supplement | New Windows build/GPU/architecture/storage class | B + U + X-01 + X-05, relevant P and artifact gate | 12–24 elapsed h plus review |

Times are planning ranges, not pass criteria. Candidate rescans, thermal recovery,
large-data generation, Windows updates, device reauthorization, evidence review,
and defect retries extend them. Parallel execution is valid only on independent
hosts/profiles/devices with distinct run IDs and session/evidence roots. Two
“performance” workloads on one host are not parallel evidence.

Treat each schedule as a checklist with dependencies, not a bag of IDs. Record
`not started | running | pass | fail | blocked | N/A` for every selected row and
name the prerequisite finding for every Blocked result. Artifact smoke is the
entry gate. Full Windows plus Soak is the ordinary release set; add Upgrade and
Windows expansion when their triggers apply. Focused slices are planning and
rerun views over rows already present in Full Windows. One scenario result may
satisfy several schedules only when it uses the same exact candidate and meets
the strictest host/profile/hardware/oracle/evidence requirement of all of them;
otherwise create a distinct attempt. The schedule manifest must show this
many-to-one mapping so reuse cannot turn an unexecuted matrix cell green.

### 12.1 Change-based minimum selection

| Changed area | Minimum live rerun |
|---|---|
| Packaging/runtime/version/notices | B-01, B-02, B-15, A-28, I-01/I-12/I-13, P-13, P-18, R-08/R-56 |
| Parser/time/detection | B-03/B-04/B-06/B-19, A-06–A-10, A-20, X-26/X-27, I-02/I-11, R-49/R-50 |
| Store/manifest/checksums/mapping | B-12/B-13/B-16, A-10/A-11/A-29/A-33, X-12–X-17/X-26, I-03/I-04 |
| ADB | B-09/B-10, A-15–A-20, X-05/X-08–X-10, I-05/I-06, P-19, R-26–R-31 |
| Growing file/framing | B-11, A-21/A-22, X-06/X-10/X-24, R-04/R-26/R-27/R-43 |
| Query/filter/search/templates/paging | B-05–B-08, A-01–A-05, X-03/X-04/X-17–X-19, I-07, R-53/R-54 |
| Timeline/rendering/layout/theme/tabs | B-04/B-07/B-08/B-17, A-05/A-13, X-04/X-18/X-23, all relevant U, R-13/R-18–R-20/R-36–R-42/R-51/R-52 |
| Settings/cache/retention | A-12–A-14/A-29/A-32/A-34, X-20/X-22, P-02/P-07/P-08/P-16, R-44/R-46/R-51 |
| Save/export/archive/diagnostics | B-12–B-14, A-25/A-27, X-12/X-14/X-15/X-25/X-26, I-03/I-04/I-07/I-08, P-03/P-04/P-10, R-55 |
| CLI command/parser/console/generator | I-01–I-07/I-10–I-14, P-05/P-06/P-11/P-15, R-47/R-55/R-57 |
| Accessibility/focus/keyboard | B-18/B-19, U-05–U-08/U-10–U-18/U-22–U-24, R-34/R-35/R-49–R-53 |

---

## 13. Recording results and exit criteria

### 13.1 Run header — complete before the first scenario

```text
Run ID:
Tester / evidence owner:
Start/end UTC and local:
Selected schedule(s) / change trigger / approved omissions:
Scenario-to-schedule accounting manifest path / SHA-256:
Plan path / repository commit / local plan modifications:
Capability-and-claim manifest path / SHA-256:
Attempt number / prior run or finding dependency:

Desktop asset path / URL / size / SHA-256:
SHA256SUMS path / SHA-256 / matching line:
Provenance attestation result:
ZIP and EXE Zone.Identifier state:
Authenticode status / signer:
Archive inventory hash:
Extracted root / inventory hash:
Desktop file/product/informational version:
CLI asset/version/hash:
Release tag / commit / channel:

Windows edition/version/build/architecture:
Physical / VM / RDP:
Machine make/model / CPU / RAM:
GPU / driver / HAGS / power mode / AC-battery:
Storage volumes/filesystems/free space/indexing/sync:
Defender/AV/CFA/app-control state:
User/profile/token/elevation:
Culture/UI culture/time zone/clock:
Display topology: resolution, scale, refresh, orientation, HDR, primary:
Windows text size/theme/contrast/color filter/animations:
Input: mouse/touchpad/keyboard layouts/IME/touch/pen:
Accessibility: Narrator/Magnifier/tool versions:

PowerShell/.NET versions:
ADB path/version/hash:
Android serial/model/API/fingerprint/clock/time zone:
VisualCat settings/session/diagnostics paths:
Starting data profile P0–P4:
Corpus manifest path/hash:
Evidence root and access/retention policy:
WPR/Procmon/recording configuration:
Baseline run ID used for performance comparison:
Abort thresholds: free space / thermal / duration / trace size / privacy:
Coverage-matrix cells intentionally absent:
Open mutation-ledger rows at start (must be none or explicitly inherited):
```

### 13.2 Result row — one per scenario

```text
Scenario ID / exact title:
Status: PASS | FAIL | BLOCKED | N/A
Schedule(s) and matrix cell(s) satisfied by this attempt:
Attempt / prerequisite scenario and finding IDs:
Candidate EXE hash / PID(s):
Start/end UTC:
Starting W-state / P-profile / preconditions:
Exact source/commands/input/actions:
Expected oracle/budget:
Observed result and verbatim product text:
Measurements: repetitions, median, p95, min/max, samples:
Integrity result: hashes/counts/verify/raw ranges/gaps:
UX loop: discoverability/state/acknowledgement/outcome/recovery/consistency:
Accessibility/visual baseline and diff result where applicable:
Windows/ADB/WER/ETW observations:
Evidence paths and SHA-256:
Mutation-ledger row(s) / restoration proof:
Finding IDs / rerun dependency:
```

Do not write “works”, “looks good”, “responsive”, or “no crash” without the
corresponding evidence. A pass row must be independently auditable.

### 13.3 Defect report — one per finding

```text
Finding ID / severity / title:
First observed run/scenario:
Candidate ZIP and EXE SHA-256 / version / PID:
Windows host/user/display/policy/storage state:
ADB/device/source state where applicable:
Preconditions and exact reproduction:
Expected: citation to this plan or repository contract
Actual: verbatim text, measurement and integrity effect
Reproduction rate / attempts:
Time-bounded crash/WER/event evidence:
Session/corpus/input hashes and safe attachment location:
Screenshots/video/ETW/Procmon/diagnostic bundle:
First suspected layer: package | Windows host | source | ingest | store | query | view model | Avalonia/renderer | ADB
Appendix-B trap checks completed:
Security/privacy handling and redactions:
Workaround / affected users / release impact:
```

Severity: **Blocker** prevents verified launch, loses/corrupts data, executes or
escapes an untrusted boundary, leaks protected content, or prevents the release
gate. **Major** breaks a primary workflow, gives silent wrong results, crashes/
hangs, or makes the product unusable with a required accessibility mode.
**Minor** has a bounded workaround or affects a secondary path. **Polish** is
perceptible without impeding completion/correctness. Severity measures impact,
not fix effort or reproducibility.

### 13.4 Release exit criteria

1. The schedule-accounting manifest covers every applicable row in Full Windows
   and Soak, plus triggered Upgrade/Windows-expansion rows, with no unaccounted
   scenario or matrix cell. Every B and applicable R row passes on the **exact
   uploaded or upload-ready immutable Windows desktop ZIP**; every other required
   row is Pass or has an explicit release exception linked to its finding/gap,
   owner, affected population, risk, mitigation, and expiry. Local source builds
   do not substitute. I-01/I-12–I-14 pass on the exact matching CLI ZIP.
2. Required §1.4 Windows matrix cells are exercised, or each gap has owner,
   affected population, risk, mitigation, expiry and explicit release approval.
   An accepted gap remains untested, not passed.
3. No open Blocker or Major finding. Every open Minor/Polish finding has an owner,
   affected configuration, workaround or rationale, explicit release acceptance,
   and target release/expiry. Security/privacy boundary failures in P-03, P-04,
   P-06, P-07, P-10, P-11, P-12 or P-16 are Blockers by default.
4. B-03/B-04/B-12/B-13/B-14 and I-02–I-08 parity/integrity oracles are exact;
   every released session/save/export/portable path verifies.
5. Zero attributable application crash, unhandled .NET exception, native access
   violation, Windows “Not responding” hang, WER failure, or unexplained process
   exit inside scenario windows.
6. ADB release gate proves physical-device discovery, capture, stop, format/time
   policy, buffer attribution, reconnect/failure and child cleanup. Results bind
   to exact serial and platform-tools hash.
7. Soak completes without sustained resource/latency growth outside baseline,
   data loss beyond explicitly observed source/platform drops, or orphan work.
8. Accessibility schedule completes with primary journey possible by keyboard
   and Narrator, correct modal boundary, no private debug dump spoken, and usable
   contrast/text/DPI configurations.
9. Every absolute budget miss and >20% like-for-like median/p95 regression is
   fixed or explicitly accepted with owner, rationale, affected configuration
   and expiry. Missing/zero-frame instrumentation is Blocked, never Pass.
10. A-29 passes from the previous supported release whenever settings, session
    format, cache, default path, versioning, runtime, or packaging changes.
11. Candidate hash/version/notices/checksum/provenance/unsigned SmartScreen
    documentation all agree; no undeclared runtime, elevation, installer,
    auto-update, file association, service, task or network traffic appears.
12. Run header, results, defect links, evidence hash index, sensitive-data
    retention decision, and completed mutation ledger are archived with release.
    Corresponding manual gates in [`RELEASE-CHECKLIST.md`](RELEASE-CHECKLIST.md)
    reference the run ID.
13. The §2.9 capability manifest has no unresolved candidate/documentation/claim
    contradiction, and every unclaimed visible capability has a tested contract.
14. Final cleanup in §13.5 passes. A green run that leaves a test user, VHD,
    Defender exclusion, altered ACL, power/display/time policy, orphan ADB,
    generated secrets, huge corpora or low-space condition behind is incomplete.

### 13.5 Mandatory cleanup and Windows hand-back

Run after success, failure, abort, crash, or power recovery. Restore ledger
values—not guessed defaults.

1. Stop/close VisualCat instances, import/capture/follow producers, `vcat`, ADB
   traffic, WPR/Procmon/recorders and pressure tools. Confirm exact PIDs ended and
   no VisualCat-owned ADB child remains. Do not kill a shared ADB server unless
   the ledger says this run created and owns it.
2. Hash/archive required evidence, then delete only exact recorded temp/corpus/
   extraction/session/export paths according to retention. Verify each resolved
   path belongs to the intended test root before recursive removal. Report what
   is recoverable and what is not.
3. Detach/delete only the dedicated VHD/VHDX, subst drive, mapping, test share or
   filler file created by the run after resolving its exact target. Verify free
   space and volume health recover.
4. Restore ACL/owner, Defender exclusions/CFA, Search indexing, app-control/
   firewall/crash-dump policy, environment variables/PATH/SDK roots, proxy/VPN/
   network and removable/cloud state.
5. Restore time/time zone, culture/language/IME, power plan/mode/timers, page
   file, animation/text/accessibility/color/contrast settings, display scaling/
   resolution/orientation/refresh/HDR/primary topology and GPU preference.
6. Restore Android buffer/debugging/authorization/network/device state and remove
   test traffic/artifacts as its owner requires. Re-run serial/fingerprint and
   buffer/free-space checks.
7. Remove test local users only when exact account SID, creation evidence and
   owner-approved deletion are recorded. Never alter a real corporate/profile
   policy to “clean up” a test.
8. Decide explicitly whether `%LOCALAPPDATA%\VisualCat` and extracted candidate
   remain. Deleting program files is not user-data removal; deleting LocalAppData
   destroys sessions/settings/diagnostics. Do exactly the recorded hand-back.
9. Reboot if required by a restoration mechanism, then re-run §2.2 identity/
   policy/free-space checks and prove ordinary apps, Windows security, display,
   power and network are healthy.
10. Attach completed ledger and cleanup evidence to run record; no row remains
    without Restored or an explicit owner-accepted residual state.

---

## Appendix A — Windows and PowerShell cookbook

These examples are templates. Resolve placeholders and use `-LiteralPath`.
Commands that change machine policy, ACL, clock, display, power, ADB buffers, or
data require a ledger row and scenario authorization.

```powershell
# --- artifact identity --------------------------------------------------
Get-Item -LiteralPath '<candidate-zip>' | Format-List FullName,Length,CreationTimeUtc,LastWriteTimeUtc,Attributes
Get-FileHash -Algorithm SHA256 -LiteralPath '<candidate-zip>'
Get-Item -LiteralPath '<candidate-zip>' -Stream * -ErrorAction SilentlyContinue
Get-AuthenticodeSignature -LiteralPath '<VCAT>' | Format-List *
(Get-Item -LiteralPath '<VCAT>').VersionInfo | Format-List *

# List ZIP paths without extraction.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead('<candidate-zip>')
try { $zip.Entries | Select-Object FullName,Length,CompressedLength } finally { $zip.Dispose() }

# --- resolved product data paths ---------------------------------------
$vcatLocal = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'VisualCat'
$vcatSettings = Join-Path $vcatLocal 'settings.json'
$vcatSessions = Join-Path $vcatLocal 'Sessions'
$vcatDiagnostics = Join-Path $vcatLocal 'Diagnostics'
Get-Item -LiteralPath $vcatLocal -Force -ErrorAction SilentlyContinue
Get-ChildItem -LiteralPath $vcatLocal -Force -ErrorAction SilentlyContinue

# --- launch with argument boundaries -----------------------------------
& '<VCAT>' --log '<corpus-root>\small.txt'
& '<VCAT>' --session '<absolute-session.vcat>'
& '<VCAT>' '<absolute-portable.vcat.zip>'

# For a deliberately visible GUI test, capture the process object.
$started = Start-Process -FilePath '<VCAT>' -ArgumentList @('--log','<absolute-log>') -PassThru
$started.Id

# --- process/resource snapshot -----------------------------------------
$pidUnderTest = <pid>
Get-Process -Id $pidUnderTest | Select-Object Id,Path,StartTime,CPU,WorkingSet64,PrivateMemorySize64,VirtualMemorySize64,HandleCount,@{n='Threads';e={$_.Threads.Count}}
Get-CimInstance Win32_Process -Filter "ProcessId=$pidUnderTest" | Select-Object ProcessId,ParentProcessId,ExecutablePath,CommandLine,CreationDate
Get-Process adb -ErrorAction SilentlyContinue | Select-Object Id,Path,StartTime,CPU,WorkingSet64,HandleCount

# Sample one PID without confusing Process(foo#1) instances. Resolve the
# counter instance by its ID Process sample before using its other values.
Get-Counter '\Process(*)\ID Process','\Process(*)\Working Set - Private','\Process(*)\Private Bytes','\Process(*)\Handle Count','\Process(*)\Thread Count'

# --- time-bounded Windows errors ---------------------------------------
$scenarioStart = [datetime]'<scenario-start-utc>'
Get-WinEvent -FilterHashtable @{LogName='Application';StartTime=$scenarioStart} |
  Where-Object ProviderName -in '.NET Runtime','Application Error','Windows Error Reporting' |
  Select-Object TimeCreated,ProviderName,Id,LevelDisplayName,Message

# --- file identity, ACL, reparse, streams -------------------------------
Get-FileHash -Algorithm SHA256 -LiteralPath '<file>'
Get-Acl -LiteralPath '<path>' | Format-List *
Get-Item -LiteralPath '<path>' -Force | Select-Object FullName,Attributes,LinkType,Target
Get-Item -LiteralPath '<file>' -Stream * -ErrorAction SilentlyContinue
Get-ChildItem -LiteralPath '<root>' -Force -Recurse -File | Get-FileHash -Algorithm SHA256

# --- storage and power --------------------------------------------------
Get-Volume | Select-Object DriveLetter,FileSystem,DriveType,HealthStatus,Size,SizeRemaining
powercfg /getactivescheme
powercfg /requests
Get-MpComputerStatus | Select-Object AntivirusEnabled,RealTimeProtectionEnabled,AntivirusSignatureLastUpdated

# --- ADB identity: discovery may be unqualified; mutation/capture is not --
& '<adb>' version
& '<adb>' devices -l
& '<adb>' -s '<serial>' get-state
& '<adb>' -s '<serial>' shell getprop ro.serialno
& '<adb>' -s '<serial>' shell getprop ro.product.model
& '<adb>' -s '<serial>' shell getprop ro.build.fingerprint
& '<adb>' -s '<serial>' shell getprop ro.build.version.sdk
& '<adb>' -s '<serial>' shell date -u
& '<adb>' -s '<serial>' shell getprop persist.sys.timezone
& '<adb>' -s '<serial>' logcat -g

# Product-independent finite parity input. Treat output as sensitive.
& '<adb>' -s '<serial>' logcat -b main,system,crash -D -v threadtime,year,UTC,usec -d > '<evidence-root>\finite-logcat.txt'

# --- Candidate CLI integrity cross-checks; never use as the only oracle -
& '<VCAT-CLI>' --version
& '<VCAT-CLI>' info '<session-or-log>'
& '<VCAT-CLI>' verify '<session.vcat>'
& '<VCAT-CLI>' stats '<session.vcat>'
& '<VCAT-CLI>' query '<session.vcat>' --order source --limit 100
& '<VCAT-CLI>' search '<session.vcat>' 'AndroidRuntime'
& '<VCAT-CLI>' templates '<session.vcat>' --top 50
& '<VCAT-CLI>' export '<session.vcat>' '<output.csv>' --type csv --order source
```

PowerShell redirection is text-oriented. It is suitable for logcat text but not
for arbitrary binary streams. Use `adb pull`, `Copy-Item`, a product save/export,
or a verified binary-safe API for archives/screenshots/raw tar data. Hash every
binary artifact after transport.

### A.1 ETW/WPR and Procmon discipline

- Create a short named WPR profile or use UI presets that include CPU, disk/file
  I/O, process/thread lifetime, DWM/present/input and .NET GC. Record profile
  hash/options and Windows build.
- Begin immediately before the measured action and stop immediately after. A
  multi-hour circular trace needs an explicit maximum size and rollover policy.
- In WPA, bind to exact `VisualCat.exe` PID/path and record event/lost-event
  counts. Export the table/query used for every numeric result.
- Procmon filters: `PID is <pid>` plus exact session/destination roots; include
  only needed operations. Backing PML may contain file content fragments and
  paths; protect it as source data.
- Dumps require synthetic inputs, explicit collection purpose, access control and
  deletion date. VisualCat never needs a dump merely to claim “no crash.”

### A.2 Safe clean-profile preparation

Prefer a dedicated Windows test user or VM snapshot. If a profile-local data
directory must be moved for P1:

1. Close all VisualCat instances and verify exact PIDs are gone.
2. Resolve `<local-app-data>` through .NET and resolve
   `<local-app-data>\VisualCat` to an absolute path.
3. Prove the target's parent is the current user's LocalApplicationData and the
   leaf is exactly `VisualCat`; inspect reparse attributes.
4. Record inventory/hashes/ACL/size and choose a unique backup path outside the
   target.
5. Move through one PowerShell process with `Move-Item -LiteralPath`; do not
   enumerate in one shell and pass strings to another.
6. After testing, stop product and restore exactly or retain the new state per
   the run's hand-back decision.

The test plan intentionally does not provide a copy-paste recursive delete for
this directory. Its contents are user sessions and settings.

---

## Appendix B — Windows traps that impersonate product bugs

Check every finding against these before filing; record the check in the defect.

1. **Mark-of-the-Web is transport/extractor-dependent.** Chrome/Edge may attach
   `Zone.Identifier`; Explorer may propagate it to extracted files; PowerShell,
   USB, Git, network shares, and some archivers may not. SmartScreen not appearing
   does not prove signing or reputation.
2. **SmartScreen reputation is external state.** A byte-identical unsigned build
   can warn on one host and not another. The product gate is honest artifact
   identity/documentation and successful verified launch, not a specific blue
   dialog on every machine.
3. **First launch includes security scanning and cache warm-up.** Defender may
   scan hundreds of self-contained runtime files; fonts/shaders/native images may
   warm. Keep first-user timing, but compare repeat cold timings separately.
4. **Portable removal is not uninstall.** Deleting the extracted directory does
   not remove `%LOCALAPPDATA%\VisualCat`; deleting LocalAppData destroys user
   sessions/settings. Treat those as separate actions.
5. **The desktop is `WinExe`.** A normal Explorer launch has no console; stderr
   from an early fatal error may be invisible. Use Event Log/WER and an explicit
   diagnostic console launch during investigation, not absence of a console as
   evidence of no error.
6. **Multiple `VisualCat` processes break name-only sampling.** Task Manager,
   `Get-Process -Name`, and `\Process(VisualCat*)` can mix candidate/source builds.
   Bind to PID and executable path; Windows counter instance suffixes can change.
7. **A killed GUI bypasses disposal.** Orphaned temp state or an ADB child after
   Task Manager End task may be expected crash residue; graceful-close leakage is
   a different finding. X-12/X-28 classify both.
8. **ADB server is shared global state.** Android Studio, VS, CLI and other users
   may own it. Routine VisualCat pre-flight must not kill the server, and a server
   restart can disrupt unrelated work.
9. **Bare ADB becomes unsafe after topology changes.** One device disappearing
   can make an unqualified command hit another sole device. Discovery may list
   globally; capture/mutation uses exact `-s <serial>` and re-proves identity.
10. **Unknown-serial `adb logcat` can wait indefinitely.** A timeout-ending empty
    capture may look successful. Product must enumerate/reject the serial before
    spawn; tester must not use a timeout as the oracle.
11. **USB sleep/selective suspend looks like parser failure.** Lock/sleep/RDP/
    dock power changes can drop the physical transport. Correlate Windows USB/
    power and `adb devices -l` state before blaming ingest.
12. **Search indexers, antivirus and sync tools take transient handles.** A
    millisecond `ACCESS DENIED`/sharing violation during atomic rename can be
    external contention. The product still owes bounded retry and a truthful
    persistent-lock failure; do not disable the scanner before reproducing W7.
13. **Memory-mapped session files retain handles.** An open tab can legitimately
    prevent replacement/deletion on Windows. Test product close/protection and
    identify the owning PID/handle; do not call every sharing violation corruption.
14. **Explorer preview/thumbnail panes and editors can hold files.** Close them or
    retain handle evidence when testing save/replace. A test-created persistent
    lock is valid only when its exact handle interval is recorded.
15. **Cloud/OneDrive files may be placeholders or conflict copies.** Hydration,
    sync delay and rename semantics differ from local NTFS. Classify the storage
    path and do not generalize its failure to local import.
16. **UNC/removable/ReFS semantics differ.** Atomic rename, case, links, ACL,
    timestamp precision and disconnect behaviour may differ. The plan treats them
    as compatibility probes unless SUPPORT claims them.
17. **Windows path display and identity are not the same.** Paths are generally
    case-insensitive, may have 8.3 aliases, Unicode normalization differences,
    `\\?\` forms and legacy 260-character policy. Record exact API-visible path
    and file identity before filing duplicate/missing-source defects.
18. **Explorer and PowerShell wildcard expansion differ.** Use `-LiteralPath` and
    argument arrays. A filename containing `[]`, `*`, `?`, quotes, semicolon or
    backtick that a test shell expands is a harness defect, not VisualCat's.
19. **PowerShell 5.1 binary redirection can transform bytes.** Never collect ZIP,
    screenshot, tar or raw binary via text redirection. Use binary-safe transfer
    and verify hash.
20. **DPI uses logical and physical coordinates.** A 900×600 Avalonia window at
    150% is not a 900×600-pixel screenshot. Record RenderScaling/Windows scale and
    native capture size before measuring clipping or hit boxes.
21. **RDP changes display/GPU conditions.** Connecting can replace monitors,
    scale, refresh, color depth and renderer device. Record it as W6/X-23, not as
    an ordinary local-monitor continuation.
22. **Screen recording changes the workload.** Game Bar/Desktop recording can
    consume GPU encoder, cap fps, add present latency and exclude some owned
    dialogs. Record actual clip properties; use ETW or external camera when it
    cannot resolve the budget.
23. **A zero-present ETW trace is not a smooth trace.** Wrong provider/profile/
    PID can yield zero frames. Mark quantitative frame row Blocked and fix
    instrumentation.
24. **Task Manager memory is not one metric.** Working set, private bytes, commit,
    mapped files and GPU memory differ. Mapped immutable segments/cache warm-up
    can grow legitimately. Use fixed metrics and corroboration.
25. **Windows time-zone IDs and IANA IDs differ.** Modern .NET can translate many,
    not every host/configuration must. Preview must validate; the test must record
    which ID was accepted rather than assuming `Europe/Prague` or
    `Central Europe Standard Time` everywhere.
26. **Wall-clock changes distort filenames and retention, not monotonic duration.**
    Correlate UTC/local/Stopwatch/file timestamps; a harness stopwatch based on
    `Get-Date` can itself jump.
27. **Controlled Folder Access is an OS policy decision.** Denial is expected;
    silent data loss, global-disable advice or undeclared fallback is the product
    bug.
28. **Elevation changes profile/storage/security context.** “Run as
    administrator” can use another token, mappings and policy. It is not a valid
    workaround for a standard-user failure and can make the failure disappear
    for the wrong reason.
29. **File associations/shortcuts are not shipped.** Double-clicking `.log` or
    `.vcat.zip` in Explorer need not offer VisualCat until distribution adds a
    registration mechanism. Command-line startup paths are the current contract.
30. **Window position is not currently a declared persisted field.** Width,
    height and maximized state are stored; tests should require reachability after
    topology change, not invent exact coordinate restoration.
31. **Desktop open-tab restoration differs from Android.** Recent/cache sessions
    persist, but current source only restores the open workspace automatically on
    Android. Do not require desktop tab restoration without a new contract.
32. **Closing an imported external source is not deleting it.** Temporary cache,
    standard save and portable save have different raw-data ownership. Use
    manifest/source identity and hashes before judging residue or loss.
33. **Live totals from two captures are not directly equal.** Startup/pre-roll/
    scheduling windows differ. Exact parity needs the same finite bytes or unique
    marker-bounded intervals.
34. **Logcat may declare its own drops.** `chatty`, ring-buffer overwrite and
    source gaps are Android/ADB observations; VisualCat must account for them but
    cannot recover bytes the source never delivered.
35. **Format modifiers degrade by device capability.** A device rejecting
    `threadtime,year,UTC,usec` can legitimately fall back. The product bug is a
    wrong manifest/time policy or unbounded negotiation, not fallback itself.
36. **A release ZIP and a local publish can share version but not bytes.** Only
    the recorded exact asset hash signs off release. A successful source rerun is
    diagnostic evidence.
37. **Explorer's compressed-folder view is not an extraction directory.** An EXE
    opened inside a ZIP may be staged to a temporary path without its sibling
    runtime files, fail with a misleading missing-file message, or disappear
    after Explorer closes. The release UX must tell a first-time reader to extract
    the whole archive; measurements and artifact identity use the recorded fresh
    extraction root.
38. **Automatic detection is allowed to refuse a corpus made mostly of defects.**
    Detection scores the whole file, not its size: §3.1's two-line
    `quiet-live-seed.txt` is detected at full confidence, while `outcomes.txt` —
    nine lines, seven of them deliberate defects — is refused outright. Import
    that one with an explicit format, in the preview or with the CLI's
    `--format`. The refusal is the confidence threshold working. The findings are
    the opposite cases: a file of defects accepted at high confidence, or a
    refusal whose message does not tell the reader that choosing a format is the
    way forward.
39. **Coordinate playback is not DPI-safe UI automation.** Per-monitor scaling,
    text size, responsive command folding, virtualization, scroll position, and
    notice insertion all move targets. A macro clicking the intended pixel once
    does not prove the intended semantic control was invoked; bind to accessible
    properties and confirm the resulting state.

---

## Appendix C — coverage map

Every functional area and its primary scenarios. A change reruns at least its
row plus the change-based selection in §12.1.

| Area | Scenarios |
|---|---|
| Artifact, provenance, extraction, launch | B-01, B-02, B-15, A-27–A-30, I-01, I-12/I-13, P-13, P-18, R-08/R-32/R-56 |
| Windows profile/data paths | W1–W4, A-12–A-14/A-29/A-34, P-02, P-08, P-17, P-18 |
| Window lifecycle/state | B-17, A-30–A-33, X-07, X-12, X-23, X-28, U-01–U-04, U-17, U-24 |
| Finite import/preview/formats | B-03/B-19, A-06–A-09, A-25/A-26, X-01/X-02/X-24, I-02, R-14/R-15/R-43/R-47/R-49/R-50 |
| Off-timeline/unparsed evidence | B-19, A-08, U-06/U-19/U-22, I-02/I-07, R-21/R-33/R-43/R-49/R-50 |
| Host ADB discovery/capture | B-09/B-10, A-15–A-20, X-05/X-08–X-10, I-05/I-06/I-09, P-19, R-01/R-26–R-31/R-48 |
| Growing-file follow | B-11, A-21–A-24, X-06/X-10/X-11/X-24/X-27/X-28, R-04/R-26–R-28/R-43 |
| Ingest/finalization/recovery | B-10–B-13, A-09–A-11, X-03/X-10–X-17/X-28, R-01–R-07/R-26/R-45 |
| Heat map/minimap/axis | B-04/B-07, A-04, X-01/X-04/X-18/X-23, U-01–U-03/U-08–U-14/U-21, R-13/R-16/R-18/R-19/R-23/R-36/R-37/R-42 |
| Search/regex/markers | B-06, A-03/A-05/A-08, X-04/X-19, U-05/U-15, I-02/I-07, R-47/R-53 |
| Filters/facets/statistics/templates | B-05/B-08, A-01–A-04, X-03/X-04/X-19, I-02/I-07, R-06/R-20–R-22 |
| Paging/bulk load | B-08/B-19, A-05, X-04/X-17/X-19, U-06/U-22, R-05/R-20/R-54 |
| Entry inspector/source context/clipboard | B-04/B-08/B-19, A-08–A-10/A-24, X-04/X-18/X-19, U-06/U-13–U-16, P-05/P-14, R-17/R-24/R-25/R-33/R-34/R-39/R-40/R-49/R-50 |
| Sessions/recent/cache/retention | B-12/B-13/B-16, A-03/A-05/A-10–A-14/A-29/A-33, X-12/X-13/X-20/X-22/X-26/X-28, P-16, R-09/R-12/R-41/R-44/R-46 |
| Save/portable/archive | B-12/B-13, A-08/A-10/A-25/A-29/A-33, X-12–X-15/X-24–X-26, I-03/I-04/I-08, P-04/P-07/P-10 |
| CSV/export | B-14, A-04/A-25/A-27, X-12/X-15/X-25, I-07, P-05/P-10/P-20, R-55 |
| Diagnostics | A-13/A-27/A-32, X-12/X-22/X-25, P-02/P-03/P-10/P-15/P-20, R-07/R-11 |
| Notice lane and status messaging | B-10/B-11/B-19, A-22/A-24, U-18/U-19/U-23, P-20, R-11/R-27/R-38 |
| Settings/upgrade | A-03/A-12–A-15/A-28–A-30/A-32/A-34, X-22/X-27, I-11, R-08/R-10/R-36/R-37/R-44/R-46/R-51/R-56 |
| DPI/displays/GPU/RDP | W5/W6, A-30/A-31, X-23, U-01–U-04/U-10–U-14/U-21/U-22/U-24 |
| Keyboard/focus/modality | B-18/B-19, U-05–U-07/U-15/U-17/U-23/U-24, R-13/R-34/R-35/R-50/R-52/R-53 |
| Narrator/accessibility | B-19, U-05–U-12/U-15–U-23, P-14/P-20, R-34/R-35/R-37/R-49/R-50 |
| Theme/contrast/text/locale | A-13, X-23/X-27, U-08–U-12/U-16/U-21/U-22, I-11, R-22/R-36/R-37/R-51 |
| Performance/scale/endurance | §4.2, X-01–X-11/X-17–X-23/X-25, R-03–R-06 |
| Windows locks/ACL/storage | W3/W4/W7, A-09/A-14/A-25/A-26, X-12–X-16/X-24/X-25, P-07–P-10/P-16, R-03/R-45 |
| Multi-instance/concurrency | W8, A-05/A-23/A-32/A-33, X-11/X-13/X-22, P-10/P-16/P-19, R-07/R-44/R-45 |
| Privacy/network/redaction | A-27/A-28, P-01–P-03/P-11/P-14/P-15/P-17–P-20 |
| Untrusted content/archive/session | A-08/A-26, X-24/X-26, P-04–P-07/P-12/P-20 |
| CLI/cross-platform parity, generator, and console contract | I-01–I-14, B-12–B-14, A-06/A-17/A-20, X-26, R-47/R-55/R-57 |

---

## Related documents

- [`ANDROID-LIVE-TEST-PLAN.md`](ANDROID-LIVE-TEST-PLAN.md) — companion-device
  live test plan and cross-surface source of the tier discipline used here.
- [`ARCHITECTURE.md`](../ARCHITECTURE.md) — layer ownership and invariants.
- [`SUPPORT.md`](SUPPORT.md) — current platform/source/distribution limits.
- [`CLI.md`](CLI.md) — exact CLI commands, options, output and exit codes.
- [`KEYBOARD.md`](KEYBOARD.md) — keyboard and accessibility contract.
- [`PERFORMANCE.md`](PERFORMANCE.md) — reproducible Windows baseline.
- [`SESSION-FORMAT.md`](SESSION-FORMAT.md) — manifest, segments, raw ownership,
  recovery and portable archive contract.
- [`PRIVACY.md`](PRIVACY.md) and [`SECURITY.md`](SECURITY.md) — data-flow and
  untrusted-input boundaries.
- [`RELEASE-NOTES.md`](RELEASE-NOTES.md) — checksum, provenance, SmartScreen and
  portable launch instructions.
- [`RELEASE-CHECKLIST.md`](RELEASE-CHECKLIST.md) — release gates this run satisfies.
- [`CHANGELOG.md`](../CHANGELOG.md) — source for Tier R and version-specific
  user-visible behaviour.
