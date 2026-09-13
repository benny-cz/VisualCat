# VisualCat — macOS live test plan

Manual and semi-automated verification of the shipped macOS desktop product on
real Macs. These are **live tests**: the exact release tarball, a real extraction
with real mode bits and real extended attributes, a real Gatekeeper decision on
an unsigned and un-notarized binary, a real Aqua session under a real window
server, real APFS semantics, real TCC consent, a real GPU — and, where the
scenario needs it, a physical Android device over USB or Wi-Fi ADB. They
complement, and never replace, `dotnet test`.

**This plan is context-agnostic.** It assumes no previous test run, remembered
ADB serial, trusted download, extracted candidate, clean home directory, granted
privacy consent, or known data path. The artifact, host, chip, user account,
login session, display topology, source files, Android device, starting state,
and oracles are established and recorded at run time. A tester can begin at §1
without facts that exist only in a previous report, chat, or shell history.

The plan is product-specific but **machine-state-independent**. Expected product
behaviour comes from this repository; macOS version, chip and translation layer,
Gatekeeper and TCC state, volume format and case sensitivity, fonts, locale,
clock, and previous VisualCat data never do. When implementation, documentation,
and observation disagree, record the discrepancy. Do not silently rewrite the
expected result to match the machine.

> **No macOS row in this plan has ever been executed.**
> [`RELEASE-CHECKLIST.md`](RELEASE-CHECKLIST.md) records *macOS hardware
> validation* as intentionally deferred for every release from `2.0.0` to
> `2.0.13`. This plan is the instrument that closes that deferral. Two
> consequences follow, and they apply to every measurement below. First, there is
> **no accepted macOS baseline**: the first controlled run establishes one, and
> until it does, a ">20% regression" signal has nothing to regress against —
> report the absolute number and say that it is a first observation. Second,
> anything this plan states about how the product behaves on macOS is derived
> from the repository rather than from an observation, so a contradiction found
> here is a finding to file rather than a mistake to correct in silence.

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
| 8 | [Tier U — desktop UX, UI, input, and accessibility](#8-tier-u--desktop-ux-ui-input-and-accessibility) |
| 9 | [Tier I — CLI, cross-platform, and artifact integration](#9-tier-i--cli-cross-platform-and-artifact-integration) |
| 10 | [Tier P — privacy, security, and negative scenarios](#10-tier-p--privacy-security-and-negative-scenarios) |
| 11 | [Tier R — regression pack for released and current fixes](#11-tier-r--regression-pack-for-released-and-current-fixes) |
| 12 | [Execution schedules](#12-execution-schedules) |
| 13 | [Recording results and exit criteria](#13-recording-results-and-exit-criteria) |
| A | [Appendix A — macOS cookbook](#appendix-a--macos-cookbook) |
| B | [Appendix B — macOS traps that impersonate product bugs](#appendix-b--macos-traps-that-impersonate-product-bugs) |
| C | [Appendix C — coverage map](#appendix-c--coverage-map) |

---

## Start here — how to execute this plan

This document is a catalogue, not a demand to run every check in page order. Use
this workflow so a tester can make progress without losing release rigor:

1. **Choose the gate before touching the candidate.** Use §12 for a named
   schedule and §12.1 for change-based additions. Artifact smoke, Standard, and
   Full macOS are cumulative gates. A release needs Full macOS plus Soak and any
   conditional Upgrade or architecture-expansion rows. The ADB, Accessibility,
   Security/storage, Display, and Parity schedules are reusable focused slices:
   their evidence can satisfy the same Full rows when candidate, configuration,
   and oracle requirements are identical; do not execute them twice merely
   because they appear under two schedule names.
2. **Create the run record.** Fill §13.1, record this plan's repository commit,
   declare candidate capabilities per §2.11, create distinct evidence, session,
   and corpus roots, and make the mutation ledger ready before changing the host.
3. **Prove the inputs.** Resolve every §2.3 token, hash the exact assets, select
   an M-state, a Q-state, an S-state and a D-profile, and prepare immutable
   independent oracles from §3.
4. **Pass the trust boundary first.** Run B-01/B-02/B-03 and I-01/I-12/I-14
   before an expensive, destructive, or unattended scenario. Stop on an artifact
   identity, archive-safety, mode-bit, code-signature, quarantine, or provenance
   contradiction. On Apple silicon this is not a formality: a Mach-O the kernel
   refuses to execute produces `Killed: 9` and no other diagnosis, and every
   later row would be Blocked by it.
5. **Run Basic as the product gate.** Every applicable B scenario must pass
   before relying on that workflow in A/X/U/I/P. If a prerequisite fails, mark
   dependent rows **Blocked by `<finding-id>`**; do not manufacture dozens of
   duplicate failures from one broken setup or primary path.
6. **Run selected specialist tiers.** One result row is still required per
   scenario. Shared setup, corpus, screenshots, or traces may be referenced by
   hash instead of copied, but no scenario inherits Pass implicitly.
7. **Close the loop.** Preserve the first observation, file findings, perform a
   fresh-state rerun when justified, apply §13.4, and complete §13.5 even after
   an abort, a log-out, or a kernel panic.

B/A/X/U/I/P scenario IDs are headings so they are available to document-outline
and screen-reader heading navigation; R guards stay in one compact table. Search
the exact ID to jump directly to a check. B cards spell out Risk/Pre/Steps/
Expect/Fail. In the compact A/X/U/I/P cards, the opening imperative is the
setup or action and every following assertion is a required pass condition. The
global §4 oracles also apply even when a card does not repeat them.

Never overwrite a failed attempt with a passing retry. Keep `attempt-01`, record
the intervention, then use `attempt-02` under a new evidence directory. A retry
can verify a fix or classify an environmental cause; it does not erase the first
result. Stop any run that crosses its recorded free-space, thermal, trace-size,
privacy, time, or restoration threshold.

---

## 1. Scope and surfaces under test

The macOS release contains two executable surfaces, published for two
architectures. A complete macOS release run exercises both surfaces on both
architectures and, where applicable, exchanges data with the Windows desktop, the
Linux desktop, and the Android companion. A macOS desktop result is not
automatically a CLI, Windows, Linux, or other-architecture result.

| Surface | What it is | Tiers |
|---|---|---|
| **macOS desktop** (`VisualCat`) | The primary Avalonia/Skia GUI, shipped as a bare terminal-launched executable rather than a Finder `.app` bundle: file import with preview, growing-file follow, host ADB capture, analysis, session management, saves, exports, settings, and diagnostics | B, A, X, U, P, R |
| **macOS CLI** (`vcat`) | The scriptable indexing, query, verify, export, generation, and ADB-capture surface shipped in a separate tarball, and the natural macOS automation surface | I, P, parity assertions |
| **Windows / Linux desktop and CLI** | The primary release target and the cross-platform parity partners for sessions, exports, and portable archives | I only in this plan; their own live behaviour belongs to [`WINDOWS-LIVE-TEST-PLAN.md`](WINDOWS-LIVE-TEST-PLAN.md) and [`LINUX-LIVE-TEST-PLAN.md`](LINUX-LIVE-TEST-PLAN.md) |
| **Android companion** (`com.barebit.visualcat`) | A producer and consumer of portable sessions and a second capture surface | I only in this plan; see [`ANDROID-LIVE-TEST-PLAN.md`](ANDROID-LIVE-TEST-PLAN.md) |

Two architectures ship, and
[`.github/release-targets.json`](../.github/release-targets.json) does not treat
them alike:

| Artifact | Runner | Archive | CI verification |
|---|---|---|---|
| `VisualCat-Desktop-osx-arm64-v<version>.tar.gz` | `macos-15` | `tar.gz` | Layout, notices, version, mode bits |
| `VisualCat-CLI-osx-arm64-v<version>.tar.gz` | `macos-15` | `tar.gz` | The above **plus** `vcat --version` and `vcat help` actually run |
| `VisualCat-Desktop-osx-x64-v<version>.tar.gz` | `macos-15` | `tar.gz` | Layout, notices, version, mode bits |
| `VisualCat-CLI-osx-x64-v<version>.tar.gz` | `macos-15` | `tar.gz` | Layout only — the target declares `executable: false`, so **CI never runs this binary** |

That last row is why the x64 artifact is a first-class part of this plan rather
than a footnote. No automated gate has ever executed an `osx-x64` build; its
first execution anywhere is either on an Intel Mac or under Rosetta 2, and it
happens for the first time during this run.

### 1.1 Functional inventory to be covered

**Distribution and launch** — exact release asset and checksum provenance,
`tar.gz` archive safety and layout, preserved or restorable mode bits, the
`com.apple.quarantine` extended attribute and how it propagates through each
download and extraction route, Gatekeeper and XProtect assessment of an unsigned
and un-notarized binary, the ad-hoc code signature an Apple-silicon Mach-O needs
in order to execute at all, Rosetta 2 translation of the `osx-x64` artifact,
self-contained startup without a system .NET runtime, launch from Terminal, from
`open`, from Finder, and from a user-created wrapper, startup arguments
(`--log`, `--session`, and a bare path), working directories, Unicode-normalized
paths, window identity and the absence of bundle identity, and clean removal of
the extracted directory.

**CLI automation** — deterministic five-format test-log generation, indexing,
inspection, query/search/statistics/templates, verification and export; stable
JSON/NDJSON, stdout/stderr and exit-code contracts; pipes, `SIGINT`, `SIGTERM`,
`SIGHUP` and `SIGPIPE`, redirection, locale independence, Unicode and
normalization-sensitive paths, cancellation, and desktop/Windows/Linux/Android/
session parity.

**Capture and import** — finite log import; the import review's format, year,
time-zone, template, and portable-raw choices, reached through *Open log* and
through *Open log with options…*; five supported logcat formats; host ADB
discovery through a Homebrew `adb`, an Android Studio SDK `adb`, an explicit
path, or `PATH`, with USB-accessory approval and Wi-Fi/mDNS states, buffers,
pre-roll, duration and byte caps, format negotiation, process-name sampling,
bounded reconnect and resume; growing-file follow with visible truncation,
rotation, and removal handling under `newsyslog` and hand-rolled rotation.

**Analysis** — six-severity density timeline, minimap, zoom/pan/fit, time-range
selection, text and bounded-regex search, exact match navigation over every match
in the session, marker presence columns, severity filters, facets including
complete-value discovery through *Find…*, deterministic Drain templates,
statistics, saved views, keyset paging, load-all cancellation, exact entry
inspection, clipboard actions, and byte-faithful raw source context.

**Session lifetime** — progressive snapshots, partial and recoverable sessions,
finalize and reopen, external-source identity checks and degraded index-only
mode, temporary-session cache and retention, recent sessions and the *Recent
captures* deletion flow, multiple tabs, standard and portable saves, `.vcat.zip`
import and export, the reviewed CSV export with its stored row order, encoding
and line-ending defaults, the shell's one cancellable file operation, diagnostic
bundle, cancellation, signal handling, crash recovery, upgrades, and concurrent
process access through the shared per-user session lease directory.

**macOS presentation and integration** — the data root under the user's
`Library`, window state under the macOS window server, minimum and large window
sizes, the green zoom button and native full screen, Retina backing scale and
"scaled" resolutions, mixed-scale multi-display, display hot-plug, negative
virtual coordinates, Spaces, Mission Control, Stage Manager and Split View, the
menu bar and the notch safe area, Dock and ⌘-Tab identity for a process with no
bundle, pointer, trackpad, Force Touch and gesture input, focus and modal
ownership, the NSPasteboard and Universal Clipboard, Dark Mode and automatic
appearance switching, system accent and highlight colours, *Increase contrast*,
*Reduce motion*, *Reduce transparency*, *Differentiate without colour*, colour
filters and Zoom, fonts and font fallback, input methods, keyboard layouts and
the Option/Command modifier map, Full Keyboard Access, VoiceOver and the
accessibility tree, App Nap and occlusion throttling, display sleep, system sleep
and wake, screen lock, fast user switching, log-out, and remote sessions over
Screen Sharing and SSH.

### 1.2 Out of scope here

- Unit, integration, benchmark, and headless UI suites except as pre-flight.
- Android companion behaviour except a portable and parity exchange.
- Windows and Linux platform chrome and packaging.
- Finder `.app` bundles, `Info.plist` identity, Launch Services registration,
  document-type and URL-scheme associations, the Dock, Quick Look, Spotlight
  metadata importers, Services, Sparkle or any other automatic updater, `.pkg`
  and `.dmg` installers, Homebrew casks, the Mac App Store, App Sandbox
  entitlements, and Developer ID signing or notarization.
  [`SUPPORT.md`](SUPPORT.md) states that the macOS archives contain
  terminal-launched executables, not Finder `.app` bundles, and that they are
  unsigned and not notarized. Absence of bundle-created integration is **not** a
  defect; the presence of any one of them becomes §2.11 capability
  reconciliation.
- Any architecture other than `osx-arm64` and `osx-x64`. Running the x64 build
  under Rosetta 2 is an explicitly tested compatibility path, not a separate
  supported architecture.
- Desktop drag-and-drop, user notifications, a menu-bar status item, and Dock
  badge or progress are not current product claims. Their absence is not a defect
  unless the candidate or its documentation adds them; any one that appears
  becomes part of §2.11 capability reconciliation and must then be tested for its
  safety and user-facing contract.
- Forensic erasure guarantees. Cache cleanup and `rm -rf` of an extracted
  directory are ordinary file deletion, not secure erase — and on an APFS volume
  with snapshots or Time Machine local snapshots enabled, the bytes can outlive
  the deletion entirely.
- General log formats other than Android logcat.

### 1.3 Applicability and test semantics

- **Pass** means every stated expectation was observed on the identified
  artifact, host profile, login session, and source, with the required evidence.
- **Fail** means an expectation was contradicted, including a documented control
  being absent, an operation silently doing something else, or a required
  integrity check disagreeing.
- **Blocked** means no product assertion could be reached because of a named
  external condition. Retain setup evidence and identify the owner of the block.
- **N/A** is allowed only when the capability is explicitly unsupported by
  [`SUPPORT.md`](SUPPORT.md), the candidate, or absent hardware or software. A
  Mac with no Touch Bar or no Force Touch trackpad can make that pass N/A; a Mac
  without Rosetta 2 installed makes the x64 execution rows Blocked rather than
  N/A, because Rosetta is installable; a surprising missing command is never N/A.

Words such as *responsive*, *stable*, *correct*, *accessible*, and *graceful*
are not pass criteria alone. Each scenario using one also cites a budget, an
integrity oracle, or an observable transition from §4.

### 1.4 macOS coverage matrix

macOS is more uniform than Linux and less uniform than it looks. The four
dimensions that change product behaviour most are **chip and translation**
(Apple silicon native, Apple silicon under Rosetta 2, Intel), **macOS major
version** (Gatekeeper, TCC, and window-server behaviour change between them),
**volume format and case sensitivity**, and **display class** (Retina scale,
ProMotion refresh, notch, external displays).

| Gate | Minimum live coverage | Important dimensions |
|---|---|---|
| Change smoke | One Apple-silicon Mac on the newest supported macOS | `osx-arm64` candidate, ordinary non-administrator account, built-in Retina display, default case-insensitive APFS, Terminal launch |
| Release candidate | Apple silicon **and** x64 coverage, and two macOS major versions where available | `osx-arm64` natively; `osx-x64` on an Intel Mac if one exists, otherwise under Rosetta 2 with that stated; oldest supported macOS available and newest; clean home; no system .NET; ordinary Gatekeeper and TCC policy active |
| Display gate | Built-in Retina display plus at least one external display of a different scale | 1× and 2× backing scale, a "scaled" non-integer resolution, ProMotion and fixed refresh, a notched built-in display, negative virtual coordinates, full screen and Split View |
| UI/accessibility gate | One host with VoiceOver, *Increase contrast*, and *Reduce motion* available | 100–200% effective scale, small and large displays, light/dark/auto appearance, keyboard-only, Zoom magnifier, an input method; Force Touch and Touch Bar where hardware exists |
| ADB gate | One physical supported Android device and one Mac with current platform-tools | USB transport including the Apple-silicon USB-accessory approval, `unauthorized` and `offline` states, Wi-Fi or transport interruption where available, at least main/system/crash buffers |
| Storage gate | Internal APFS (case-insensitive) plus one materially different supported path | A case-**sensitive** APFS volume; an exFAT or FAT volume with no POSIX modes; an SMB or NFS share; an iCloud Drive path with *Optimize Mac Storage* active; an encrypted external volume; a restrictive `umask` |
| Performance and soak | Dedicated physical Mac | AC power, `caffeinate` or *Prevent automatic sleeping*, fixed display topology, Spotlight indexing policy recorded, sufficient storage, no competing benchmark workload, and thermal state sampled throughout |

A virtual machine is a legitimate host for several tiers and is often the only
safe one for destructive rows, but record it as a VM: on Apple silicon a macOS
guest has a paravirtual display with no ProMotion and often no GPU acceleration,
USB pass-through changes ADB behaviour substantially, Rosetta may be provided by
the host rather than the guest, and the clock can jump on snapshot restore. A
finding seen only under virtualization must be reproduced on metal before it is
filed against rendering, timing, or USB.

If the matrix cannot be completed, execute what is available and name every
untested architecture, macOS version, display class, volume format, input device,
or ADB cell in the release decision. Untested cells do not become green because
another MacBook passed.

---

## 2. Environment, artifact, and pre-flight

### 2.1 Requirements

- A Mac inside the support policy current at execution time. The repository
  publishes `osx-arm64` and `osx-x64`; do not infer support for any other
  architecture, and do not treat a successful Rosetta 2 run as evidence about a
  native Intel Mac without saying which one you had.
- An ordinary non-administrator account for the primary run. An administrator
  account is additionally useful for `fs_usage`, disk images, and installing
  Rosetta, but elevation must not be required for ordinary analysis.
- A real logged-in graphical (Aqua) session owned by the account under test. The
  desktop head needs the window server; an SSH shell into a Mac whose console is
  logged out, or logged in as another user, is not one, and the failure looks
  nothing like the Linux "no `DISPLAY`" case.
- At least **20 GB free** on the home volume for the full run, and **80 GB** for
  XL, corruption, low-space, and soak scenarios. Use a dedicated APFS volume, a
  sparse disk image, a throwaway account, or a VM for destructive cases. Note
  that APFS reports "purgeable" space that is not immediately available, so
  record the figure `df -h` gives **and** the figure Finder gives, and say which
  one an abort threshold is measured against.
- The command-line tools a macOS host actually has: `shasum`, `tar` (bsdtar),
  `stat`, `xattr`, `codesign`, `spctl`, `sw_vers`, `system_profiler`, `lsof`,
  `sample`, `spindump`, `vmmap`, `footprint`, `log`, `plutil`, `mdutil`,
  `hdiutil`, `diskutil`, `caffeinate`, `screencapture`. `sha256sum`, `stat -c`,
  GNU `sed -i`, `readlink -f`, `head -c -1`, and `timeout` are **not** present
  unless GNU coreutils has been installed; §3 and Appendix A use the BSD
  spellings throughout and Appendix B records the traps.
- The exact macOS desktop and CLI release tarballs for the architecture under
  test, the matching `SHA256SUMS`, release notes, and the GitHub build-provenance
  attestation, verified with `gh` where that tooling is available.
- For ADB tiers: current Android SDK Platform Tools, a data-capable USB cable or
  working Wi-Fi ADB, and a supported physical Android device whose use is
  authorized by its owner.
- A performance and trace path appropriate to the host: Xcode Instruments where
  it is installed, otherwise `sample`, `spindump`, `footprint`, `powermetrics`,
  and an external high-frame-rate camera. `dotnet-counters` and `dotnet-trace`
  attach to the self-contained process through its diagnostics socket but are not
  mandatory.
- A capture tool whose impact is understood. `screencapture` and an external
  camera are safe defaults; QuickTime or a screen recorder changes GPU and CPU
  load, requires the *Screen Recording* privacy grant, and must be labelled
  whenever it is used for performance evidence.

Do not run low-space, forced power loss, permission-denial,
`DYLD_INSERT_LIBRARIES`, corrupt-archive, TCC-reset, or mass-cache-deletion tests
against a personal home directory or irreplaceable logs. `tccutil reset` in
particular is account-wide and silently revokes consent that other applications
depend on.

### 2.2 Pre-flight — identify the machine, session, display, and policy

Run this at the beginning of every run and after a VM restore, a macOS update, a
user switch, a login-session change, or a display-topology change. Save the
output rather than relying on a screenshot of System Settings.

```shell
# --- identity, chip, translation ----------------------------------------
sw_vers                                   # ProductName, ProductVersion, BuildVersion
uname -a; uname -m                        # arm64 or x86_64
sysctl -n machdep.cpu.brand_string hw.model hw.memsize hw.ncpu 2>/dev/null
sysctl -n hw.perflevel0.logicalcpu hw.perflevel1.logicalcpu 2>/dev/null || true
sysctl -n sysctl.proc_translated 2>/dev/null || echo 'not translated'
/usr/bin/pgrep -q oahd && echo 'Rosetta 2 present' || echo 'Rosetta 2 absent'
system_profiler SPHardwareDataType SPDisplaysDataType 2>/dev/null | sed -n '1,80p'
csrutil status                            # System Integrity Protection
spctl --status                            # Gatekeeper assessment policy

# --- account, limits, shell ---------------------------------------------
id; umask; echo "$HOME"; echo "$SHELL"; dscl . -read "/Users/$USER" NFSHomeDirectory
ulimit -a                                 # macOS default open-file limit is small
launchctl limit maxfiles
sysctl kern.maxfiles kern.maxfilesperproc

# --- login session and window server ------------------------------------
stat -f '%Su' /dev/console                # console owner; must be the test account
launchctl managername 2>/dev/null || true # Aqua, Background, or StandardIO
echo "TERM_PROGRAM=$TERM_PROGRAM  SSH_CONNECTION=${SSH_CONNECTION:-none}"

# --- appearance, motion, contrast, accent --------------------------------
defaults read -g AppleInterfaceStyle 2>/dev/null || echo 'Light'
defaults read -g AppleInterfaceStyleSwitchesAutomatically 2>/dev/null || echo 'false'
defaults read -g AppleAccentColor 2>/dev/null || echo 'multicolour'
defaults read -g AppleHighlightColor 2>/dev/null || true
defaults read com.apple.universalaccess reduceMotion 2>/dev/null || echo '0'
defaults read com.apple.universalaccess reduceTransparency 2>/dev/null || echo '0'
defaults read com.apple.universalaccess increaseContrast 2>/dev/null || echo '0'
defaults read com.apple.universalaccess differentiateWithoutColor 2>/dev/null || echo '0'
defaults read -g AppleKeyboardUIMode 2>/dev/null || echo 'full keyboard access off'
defaults read -g com.apple.keyboard.fnState 2>/dev/null || echo 'F-keys are media keys'
defaults read -g com.apple.swipescrolldirection 2>/dev/null || true
defaults read -g AppleShowScrollBars 2>/dev/null || true

# --- locale, time, fonts --------------------------------------------------
defaults read -g AppleLocale; defaults read -g AppleLanguages
locale; date -u '+%Y-%m-%dT%H:%M:%SZ'
systemsetup -gettimezone 2>/dev/null || readlink /etc/localtime
ls /usr/share/zoneinfo >/dev/null && echo 'tzdata present'
system_profiler SPFontsDataType 2>/dev/null | grep -c 'Family Name' || true

# --- storage, volumes, case sensitivity, snapshots ------------------------
df -h / "$HOME" "$TMPDIR"
diskutil info -plist / | plutil -p - | grep -iE 'FilesystemName|CaseSensitive|APFS'
mount
tmutil listlocalsnapshots / 2>/dev/null | head
mdutil -s /                               # Spotlight indexing state per volume

# --- background load, power, thermals -------------------------------------
pmset -g; pmset -g assertions | head -20
ps -Ao pid,%cpu,%mem,comm -r | head -15   # what else is competing right now

# --- runtime that must NOT be required ------------------------------------
command -v dotnet && dotnet --info || echo 'no system dotnet (expected)'
```

Also record manually or with an approved inventory tool:

- each display's resolution, the *looks like* scaled resolution, backing scale
  factor, refresh rate and whether it is ProMotion or adaptive, colour profile,
  HDR state, orientation, primary flag, and whether the built-in display has a
  camera housing (notch);
- whether *Displays have separate Spaces* is on, whether Stage Manager is on,
  how many Spaces exist, and whether the menu bar and Dock auto-hide;
- the Gatekeeper policy, and the XProtect and background-update versions
  (`system_profiler SPInstallHistoryDataType | grep -i -A2 xprotect`);
- the privacy consents already granted to the terminal the tester will launch
  from — *Full Disk Access*, *Files and Folders*, *Accessibility*, *Screen
  Recording*, *Developer Tools* — because on macOS those grants belong to the
  **launching** application, not to VisualCat (§2.6);
- input devices, keyboard layouts, whether F-keys act as standard function keys,
  the active input source, and whether a Touch Bar or Force Touch trackpad is
  present;
- background load that competes for I/O or CPU: Spotlight (`mds`, `mdworker`),
  Time Machine, iCloud (`bird`, `cloudd`), Photos analysis, backup agents,
  antivirus, and any MDM agent;
- power source, battery percentage, Low Power Mode, and whether the Mac is a
  laptop that will throttle on battery or with the lid closed.

Do not disable Gatekeeper, SIP, Spotlight, or a corporate security agent just to
make the default path pass. Run with the ordinary policy first, then use an
explicitly recorded controlled comparison if diagnosis needs it.

### 2.3 Resolve and record every placeholder

| Token | Resolution rule |
|---|---|
| `<run-id>` | Unique path-safe identifier, normally UTC date and time plus candidate version, architecture, and host label |
| `<arch>` | `osx-arm64` or `osx-x64`, the architecture of the artifact under test — never the architecture of the Mac, which may differ under Rosetta 2 |
| `<candidate-tar>` | Absolute path to the immutable desktop release archive whose hash is recorded, published as `VisualCat-Desktop-<arch>-v<version>.tar.gz` |
| `<cli-tar>` | Matching immutable CLI archive, published as `VisualCat-CLI-<arch>-v<version>.tar.gz`; its version must equal the desktop candidate's |
| `<candidate-root>` | A fresh absolute extraction directory created for this run; never `$HOME`, the repository, `~/Downloads`, or a synced iCloud folder |
| `<VCAT>` | Exact `<candidate-root>/VisualCat` path |
| `<VCAT-CLI>` | Exact extracted matching candidate `vcat`; a surface under test and a cross-check, never the sole correctness oracle |
| `<evidence-root>` | Dedicated absolute directory outside the product's session and cache directories, and outside any synced folder |
| `<data-home>` | The product's data root **as it actually resolves on this host**, discovered per §2.4 and reconciled against [`PRIVACY.md`](PRIVACY.md), which states `~/Library/Application Support/VisualCat`. Prove it; do not assume it |
| `<session-root>` | Product session root discovered from the UI or settings; default currently `<data-home>/Sessions` |
| `<settings-path>` | Current product settings file; default currently `<data-home>/settings.json` |
| `<diagnostics-root>` | Default currently `<data-home>/Diagnostics` |
| `<lease-root>` | Cross-process session lease directory, currently `<data-home>/SessionAccess-v1` |
| `<tmp>` | The account's private temporary directory as `echo "$TMPDIR"` reports it, normally under `/var/folders/…/T/`; record the resolved `/private/var/…` form beside it |
| `<adb>` | Exact `adb` binary selected by the scenario; record its absolute path, origin (Homebrew, Android Studio SDK, manual download), version, hash, and architecture (`file` or `lipo -archs`) |
| `<serial>` | Exact authorized Android transport selected from `adb devices -l`, re-proved after every disconnect |
| `<corpus-root>` | Dedicated generated test-data directory with recorded hashes and an oracle manifest |

Expand tokens before running a command. A command containing an unresolved
`<...>` token is a setup error, not evidence. Quote every path (`"$VCAT"`), use
`--` before filename arguments, and never build a shell command by concatenating
a file name, device serial, or log content into a string. macOS file names may
legally contain spaces, a `:` as POSIX APIs see it, and any Unicode; a harness
that breaks on one of those is a harness defect, not a product finding.

Record which shell you used. macOS defaults to `zsh`, and `zsh` differs from
`bash` in ways that reach this plan: an unquoted glob that matches nothing is an
error rather than being passed through, `$(...)` does not word-split by default,
and a leading `=` is filename expansion. Every command in this document is
written to work in both, but a transcript that does not say which shell produced
it cannot be re-run reliably.

### 2.4 Candidate acquisition, provenance, extraction, and identity

The exact uploaded tarball is the release authority. A source build can diagnose
a finding but cannot make the uploaded bytes pass. A tarball produced on a
Windows host is **not** a substitute: it loses every executable bit, which
reproduces as a fake permission defect (Appendix B). The repository says this
itself — [`RELEASE-CHECKLIST.md`](RELEASE-CHECKLIST.md) states that cross-built
Unix tarballs made on Windows are layout checks only, and that **the Linux and
macOS workflow runners are authoritative for permissions**. That checklist also
requires at least one Unix artifact to be tested on a clean machine; this plan is
how that requirement is met for macOS.

| Artifact | Purpose | Release authority? |
|---|---|---|
| Debug or source run (`dotnet run`) | Inspect exceptions and iterate quickly | No |
| Local Release publish from `tools/package.ps1` on a Mac | Rehearse layout and early smoke | No, unless byte-identical to the uploaded candidate and provenance says so |
| Local publish archived on Windows | Diagnosis only | **No** — Unix mode bits are authoritative only when the archive is created on Unix |
| Exact `VisualCat-Desktop-<arch>-v<version>.tar.gz` | macOS desktop release decision for that architecture | Yes |
| Exact `VisualCat-CLI-<arch>-v<version>.tar.gz` | Shipped CLI decision and desktop cross-check, paired with independent or previously trusted oracles | Yes for CLI and integration rows |

Before extraction:

```shell
stat -f '%N %z %Sm %Sp %Su:%Sg' -- '<candidate-tar>'
shasum -a 256 -- '<candidate-tar>'
grep -F -- "$(basename '<candidate-tar>')" SHA256SUMS
shasum -a 256 -c SHA256SUMS 2>/dev/null | grep -F "$(basename '<candidate-tar>')"
xattr -l -- '<candidate-tar>'                  # quarantine, where-from, anything else
file -- '<candidate-tar>'
gh attestation verify '<candidate-tar>' --repo benny-cz/VisualCat   # where gh exists
```

Compare SHA-256 against the matching `SHA256SUMS` line and, when present, verify
the GitHub build-provenance attestation. Record the release URL, tag, commit,
asset size, archive hash, checksum-file hash, attestation result, download
method, and **every extended attribute the download tool attached** —
`com.apple.quarantine` and `com.apple.metadata:kMDItemWhereFroms` are both
evidence about how the artifact arrived, and the first one decides which Q-state
(§2.6) this attempt is in.

List archive members before execution and reject absolute paths, `..`
components, symlinks or hard links pointing outside the archive, device or FIFO
entries, setuid or setgid bits, world-writable modes, duplicate or
normalization-colliding names, AppleDouble `._` members, and wrapper-directory
surprises:

```shell
tar -tvzf '<candidate-tar>' | tee '<evidence-root>/<run-id>/archive-listing.txt'
tar -tzf  '<candidate-tar>' | grep -E '^/|(^|/)\.\./' && echo 'UNSAFE MEMBER'
tar -tvzf '<candidate-tar>' | awk '$1 ~ /^[lhcbp]/ || $1 ~ /[st]/ {print "REVIEW: " $0}'
tar -tzf  '<candidate-tar>' | grep -E '(^|/)\._|(^|/)\.DS_Store' \
  && echo 'REVIEW: resource fork or Finder metadata in the archive'
```

Extract into a fresh directory, then record the full inventory, mode bits,
extended attributes, and hash set:

```shell
mkdir -p '<candidate-root>' && tar -xzf '<candidate-tar>' -C '<candidate-root>'
find '<candidate-root>' -exec stat -f '%Sp %z %N' {} + | sort -k3 \
  > '<evidence-root>/<run-id>/extract-inventory.txt'
find '<candidate-root>' -type f -exec shasum -a 256 {} + | sort -k2 \
  > '<evidence-root>/<run-id>/extract-hashes.txt'
xattr -lr '<candidate-root>' | tee '<evidence-root>/<run-id>/extract-xattrs.txt'
ls -l@ '<candidate-root>/VisualCat'
```

The desktop archive root must contain `VisualCat`, `LICENSE`,
`THIRD-PARTY-NOTICES.md`, and `README.txt`; the CLI archive must contain `vcat`
and the same three files. The README and the visible application identity must
name the candidate version. No separate .NET installation may be required.

`VisualCat` and `vcat` must extract with the executable bit set when the archive
was produced on macOS by the release workflow. If they do not, determine first
whether the archive or the extraction tool dropped it — Archive Utility,
`bsdtar`, `unar`, The Unarchiver, and a restrictive `umask` all behave
differently — and record which, before filing anything. The README's `chmod +x`
line is a documented remedy for a lossy extractor, not an admission that the
shipped bit is expected to be missing.

**Then check the thing no other platform requires.** An Apple-silicon Mac will
not execute an `arm64` Mach-O that carries no code signature at all: the kernel
kills it, and the shell prints `Killed: 9` with no other diagnosis. The .NET SDK
ad-hoc signs the apphost when publishing on macOS, so the shipped binary is
expected to carry an ad-hoc signature — one that proves nothing about who built
it and everything about whether it can run. Prove it is there and intact:

```shell
codesign -dv --verbose=4 '<candidate-root>/VisualCat' 2>&1 | tee signature.txt
codesign --verify --strict --verbose=2 '<candidate-root>/VisualCat'
codesign -dv --verbose=2 '<candidate-root>/vcat' 2>&1
# Every bundled native library, not only the apphost:
find '<candidate-root>' -name '*.dylib' -exec codesign -dv {} \; 2>&1 | grep -c 'adhoc'
find '<candidate-root>' -name '*.dylib' -exec codesign --verify --strict {} \; 2>&1 | head
spctl --assess --type execute --verbose=4 '<candidate-root>/VisualCat' 2>&1
lipo -archs '<candidate-root>/VisualCat' 2>/dev/null || file '<candidate-root>/VisualCat'
```

Record the signature identifier, the `CDHash`, whether it is `adhoc`, the team
identifier (expected `not set`), the runtime version, the architectures present,
and exactly what `spctl` says. `spctl --assess` is **expected to reject** an
unsigned, un-notarized artifact — that rejection is the documented state, not a
finding. A missing or invalid signature on Apple silicon, an unexpected team
identifier, a Developer ID nobody claims to have used, or a `--verify` that fails
on a bundled `.dylib` are all findings.

Then resolve the data root rather than assuming it, and reconcile it with what
[`PRIVACY.md`](PRIVACY.md) publishes:

```shell
# Before the first launch, with D1 in force:
ls -la "$HOME/Library/Application Support" | grep -i visualcat || echo 'not there yet'
ls -la "$HOME/.local/share" 2>/dev/null | grep -i visualcat || echo 'not there either'
# Launch once, then find whatever it actually created, wherever that is:
find "$HOME" -maxdepth 5 -name 'VisualCat' -type d -newermt '-10 minutes' 2>/dev/null
```

`PRIVACY.md` states the macOS data root is
`~/Library/Application Support/VisualCat`. The product resolves it from
`Environment.SpecialFolder.LocalApplicationData`, which is a runtime decision
this plan deliberately does not restate. Record the path that actually appears,
record whether an **absolute** `XDG_DATA_HOME` moves it, and treat any difference
from the published sentence as a documentation-or-behaviour finding for P-02 —
not as a value to quietly correct in your notes.

After launch, record:

```shell
shasum -a 256 -- '<VCAT>' '<candidate-root>/vcat' 2>/dev/null
pid=$(pgrep -x VisualCat); echo "$pid"
ps -o pid,ppid,user,stat,etime,time,rss,vsz,comm -p "$pid"
ps -o pid,args -p "$pid" | cat
lsof -p "$pid" -Fn | head -40
```

Whether *this process* is translated is the question that matters for an
`osx-x64` run on Apple silicon. `sysctl sysctl.proc_translated` answers for the
calling process, so read it from a child of the same shell, or use Activity
Monitor's **Kind** column (`Apple` or `Intel`), and record the answer in the run
header. When more than one process has that name, bind every later sample to the
recorded PID **and** its executable path, not to the name alone.

### 2.5 Execution states — test dimensions, not setup shortcuts

| State | How to produce | Expected product behaviour |
|---|---|---|
| **M0 — ordinary verified portable run** | Download the exact tarball, verify checksum and provenance, extract into a user-writable directory on the home volume, clear quarantine as [`RELEASE-NOTES.md`](RELEASE-NOTES.md) documents, launch from Terminal | Starts as a non-administrator user; no installer, elevation, or system .NET required; identity and notices match the candidate |
| **M1 — clean home, no VisualCat data** | A new local test account, or a validated backup-and-move of exactly the resolved `<data-home>` | First launch has no stale settings, sessions, leases, or diagnostics, and creates nothing outside the declared locations |
| **M2 — preserved home, upgrade** | The previous supported release's genuine settings, sessions, saved views, and an interrupted session in place; start the candidate from a different extraction directory | Data is migrated or read compatibly; the candidate does not rewrite old data merely by listing it; rollback risk is documented |
| **M3 — restrictive destination** | Run from a readable directory while the session or export target is denied (`chmod 500`), on a read-only mounted image, on a full volume, or inside a directory the account cannot traverse | Launch still works if its own directory is readable and executable; each denied write fails visibly and safely; no authorization prompt and no silent fallback to another directory |
| **M4 — alternate path and volume topology** | Candidate and corpora under spaces, Unicode in both NFC and NFD spellings, a very deep path, a case-**sensitive** APFS volume, an exFAT or FAT volume with no POSIX modes, an SMB or NFS share, an encrypted external volume, and an iCloud Drive path, as separate passes | Supported local paths work; unsupported or unstable storage fails honestly without corrupting the source or the cache; a case-insensitive volume does not silently merge two sessions and a case-sensitive one does not silently split one |
| **M5 — translated execution** | The `osx-x64` artifact on an Apple-silicon Mac with Rosetta 2 installed | Runs, or refuses with a message naming Rosetta. Behaviour, counts, exports, and hashes are identical to the native run; only timings may differ, and they are reported as their own baseline |
| **M6 — interrupted session** | Hide, minimize, occlude, switch Space, enter and leave full screen, lock the screen, let the display sleep, put the system to sleep and wake it, switch users quickly, disconnect a Screen Sharing session, and log out during capture or import, as separate passes | Acquisition and visible-refresh semantics match §6 and §7; committed data remains recoverable; a log-out's termination is handled, not merely survived |
| **M7 — background contention** | Spotlight indexing a volume under test, Time Machine running, an iCloud sync in progress, or a controlled test process briefly holding a new manifest or destination | Bounded retries tolerate transient contention; cancellation stays prompt; a persistent condition produces a precise failure rather than a hang |
| **M8 — concurrent processes** | Two candidate instances under one account with distinct sources, then one shared-session conflict probe, then a `vcat` process against the same session, then one native and one translated instance together | No settings, session, or lease corruption, no cross-instance tab confusion, no unsafe deletion; unsupported simultaneous writes are refused through the lease directory, and the native and translated builds agree about lease identity |
| **M9 — managed or hardened host** | A Mac under MDM with a configuration profile, with FileVault on, with an endpoint-security agent installed, with a restrictive `umask`, and with the default (small) `ulimit -n` unchanged | Either the product runs normally, or it fails with a message that names the actual restriction; it never proposes weakening host policy as the first remedy |

These states do not authorize weakening machine security. When a state requires a
policy change, use a dedicated machine, record the original value, and restore
it.

### 2.6 Trust and consent states — Gatekeeper, quarantine, and TCC

This family has no counterpart on Windows or Linux and is the single largest
source of macOS-only behaviour. macOS decides three separate questions about a
downloaded, unsigned, un-notarized executable: may the kernel execute this code
at all, may the user launch this quarantined file, and may this process read the
user's data. Record which Q-state produced every observation; a launch result
without its Q-state is not comparable to another host's.

| State | How to produce | What it tests |
|---|---|---|
| **Q0 — no quarantine** | Download with `curl`, extract with `tar -xzf` from Terminal | The reference launch path. `xattr -l` on the extracted executable shows no `com.apple.quarantine`. This is the state most CI-adjacent testers produce by accident, and it silently skips the whole Gatekeeper story — which is why Q1 exists |
| **Q1 — quarantined by the browser, extracted by Archive Utility** | Download in Safari, double-click the `.tar.gz` in Finder | The route a real user takes. Archive Utility propagates `com.apple.quarantine` to the extracted files; launching then involves Gatekeeper, and the first launch of a large self-contained tree also involves an XProtect scan that costs measurable time |
| **Q2 — quarantined archive, command-line extraction** | Download in Safari, extract with `tar -xzf` in Terminal | The mixed case. Whether the quarantine flag reaches the extracted files depends on the extractor, so record `xattr -l` on the archive **and** on the executable **and** on one bundled `.dylib`, and do not generalize from one of the three |
| **Q3 — the documented remedy, exactly as written** | From Q1 or Q2, run precisely what [`RELEASE-NOTES.md`](RELEASE-NOTES.md) tells the user to run: `xattr -dr com.apple.quarantine VisualCat`, then `./VisualCat` | Whether the published instruction is sufficient. It names only the executable, and the extracted tree contains many bundled native libraries beside it. Record whether any quarantined `.dylib` remains, and whether the app launches anyway |
| **Q4 — remedy applied to the whole tree** | `xattr -dr com.apple.quarantine <candidate-root>` | The comparison case for Q3. If Q4 launches and Q3 does not, the documented remedy is incomplete, and that is a documentation finding with a one-line fix |
| **Q5 — user-approved through System Settings** | From Q1 without clearing quarantine, attempt the launch, then approve through **System Settings → Privacy & Security → Open Anyway** where macOS offers it | Whether the platform's own approval route works for a bare executable at all. On current macOS that affordance is oriented at bundled applications; if it is not offered for this artifact, that is a fact about the shipped format worth recording against [`SUPPORT.md`](SUPPORT.md) |
| **Q6 — privacy consent (TCC)** | Open a log from `~/Desktop`, `~/Documents`, `~/Downloads`, an external volume, and a network share, with no consent previously granted | Which prompt appears, **what application name it names**, and whether the product copes with a denial. See the note below, which is the most important macOS-only consequence of shipping an unbundled executable |
| **Q7 — consent denied and revoked** | Deny each prompt; then revoke an existing grant in System Settings while the app is running, and again while a capture is running | The product reports a denial as a denial rather than as a missing file; a revocation mid-run does not corrupt a session or hang a read |

> **TCC attributes consent to the launching application, not to VisualCat.**
> macOS decides privacy prompts by *responsible process*, which for a bare
> executable started from a shell is the terminal application — Terminal.app,
> iTerm2, or whatever started it. Three consequences follow, and every one of
> them is a real user-visible behaviour that must be recorded rather than
> assumed: the prompt names the terminal and not VisualCat; a grant already given
> to that terminal means **no prompt appears at all** and VisualCat silently
> inherits access the user never granted it by name; and revoking the grant
> revokes it for everything that terminal ever runs. Establish the terminal's
> existing *Full Disk Access*, *Files and Folders*, *Desktop*, *Documents*,
> *Downloads*, and *Removable Volumes* grants in pre-flight, state them in the
> run header, and run at least one Q6 pass from a terminal that has none — a
> freshly installed one, or an account that has never granted any — because a
> tester's own daily terminal almost certainly has Full Disk Access and would
> make the entire consent story invisible.

### 2.7 Display and window-server states

| State | How to produce | What it tests |
|---|---|---|
| **S0 — built-in Retina display, default scaling** | One display, the default *looks like* resolution, backing scale 2× | The reference environment for every visual baseline |
| **S1 — non-integer "scaled" resolution** | Choose a *looks like* resolution other than the default, so macOS renders above native and downsamples | Text metrics, hairline borders, and any layout that assumes an integer scale |
| **S2 — external display of a different scale** | A 1× external display beside a 2× built-in, and the window moved across and straddled over the boundary | Per-display backing scale, dialog placement, hit testing, and whether a window re-rasterizes correctly when it crosses |
| **S3 — ProMotion and fixed refresh** | A 120 Hz adaptive-refresh Mac, one fixed at 60 Hz, and a display pinned to a fixed refresh rate in System Settings | Frame-pacing budgets: an adaptive display legitimately drops to a low refresh when content is static, so a naïve frames-per-second number is meaningless without the active mode beside it |
| **S4 — full screen, Split View, Stage Manager, Spaces** | The green zoom button, native full screen, Split View with another app, Stage Manager on, and the window moved between Spaces and left on another Space during a capture | Window-state semantics with no Windows or Linux equivalent, and whether "maximized" as the product persists it means zoomed or full screen here |
| **S5 — notch and menu-bar safe area** | A MacBook with a camera housing, with the menu bar shown and auto-hidden, in full screen and out of it | Whether any product chrome is drawn under the camera housing or under the menu bar |
| **S6 — display sleep, lock, and hot-plug** | Let the display sleep, lock the screen, unplug and reattach an external display, change the primary display, rotate one, and disconnect a Screen Sharing session, each during active work | Recovery of the window and of rendering; whether a capture continues; whether a dialog ends up on a display that no longer exists |
| **S7 — no graphical session** | SSH into the Mac while the console is logged out or logged in as a different user; and run the desktop binary from a context where `launchctl managername` reports `Background` | The failure must name what is actually wrong. The product's startup-failure explanation is written for X11 and names `DISPLAY`, `WAYLAND_DISPLAY`, and Debian package names; whether any of that appears on a Mac is the assertion |

Record for every S-state: the display list with resolution, scaled resolution,
backing scale and refresh; whether *Displays have separate Spaces* is on; Stage
Manager state; the renderer Avalonia actually selected; and whether the screen
locks or sleeps during long runs.

### 2.8 Starting data profiles

| Profile | Contents | Use |
|---|---|---|
| **D0 — preserved** | The account's existing `<data-home>`, untouched | Read-only discovery only; never for destructive tiers |
| **D1 — clean** | No `<data-home>` and a fresh candidate extraction | Cold-start and privacy baseline |
| **D2 — seeded** | Known sessions: complete, interrupted, portable, external-source, and a corrupted copy; known settings and saved views | Most A, U, and I scenarios |
| **D3 — upgrade** | The previous supported release's genuine on-disk data and settings | A-29 and the release gate |
| **D4 — pressure** | Dedicated volume, sparse image, or account prepared for low space, high session count, contention, permission, and crash matrices | X and P only |

Moving or deleting `<data-home>` removes settings, cached sessions, leases, and
diagnostics. That is destructive. Resolve the exact path, prove it is the
expected child of the resolved parent directory, stop every `VisualCat` and
`vcat` process, take a recoverable backup when required, and record the action.
Never build a recursive removal from a variable that might be unset or empty:
resolve it, print it, confirm it, then act. And remember that on a volume with
Time Machine local snapshots the removal frees no space immediately, so a
low-space scenario that "cleaned up" may not have.

### 2.9 Mutation ledger and guaranteed restoration

Before any mutation, append a ledger row:

```text
Timestamp UTC | Scenario | Host/user | Setting/path/device | Original value/state |
New value/state | Exact restoration | Owner | Restored evidence
```

Ledger at minimum: the VisualCat data root; extracted candidates; generated
corpora; extended attributes removed, including quarantine; environment
variables including `PATH`, `HOME`, `TMPDIR`, `XDG_DATA_HOME`, `DYLD_*`,
`DOTNET_*`, `AVALONIA_*`, `ANDROID_SDK_ROOT`, `ANDROID_HOME`, `TZ`, `LANG` and
`LC_*`; ADB server and device state and USB authorization; file modes, ACLs,
`chflags` flags, and ownership; `umask`; `launchctl limit maxfiles` and `ulimit`
values; mounted disk images, created APFS volumes, and network mounts; Spotlight
indexing state per volume; Time Machine state and local snapshots; system clock
and time zone; locale, language, and input sources; display resolution, scaled
resolution, arrangement, primary display, refresh rate, and rotation; appearance,
accent and highlight colour, contrast, motion, transparency, and colour filters;
Dock and menu-bar auto-hide; Stage Manager; keyboard `fnState` and
`AppleKeyboardUIMode`; accessibility services and every TCC grant added or reset;
energy and sleep settings and any `caffeinate` assertion; Gatekeeper policy;
firewall rules; and test accounts.

Every destructive scenario owns its cleanup even when it fails. Do not start a
new destructive scenario while an earlier ledger row has no plausible
restoration.

### 2.10 Destructive-scenario register

| Scenario family | Risk | Required containment |
|---|---|---|
| Clean home or upgrade reset | Deletes settings, sessions, leases, diagnostics | Validated path under the resolved data root and a recoverable backup |
| Low disk, huge corpus | Home or startup volume exhaustion; an unbootable Mac if the system volume group fills | Dedicated APFS volume or sparse disk image with a fixed size; hard abort threshold; never the startup volume's own free space |
| Permission, ACL, `chflags`, and mount tests | Access loss, an unexpected target, a home directory left unreadable | Dedicated subtree; capture `ls -le@`, `stat -f`, and `chflags` state first; never `chmod -R` a home root |
| Corruption and archive bombs | CPU or disk exhaustion, unsafe extraction | Generated copies only; size and time limits; dedicated volume |
| Signal, kill, and power interruption | Partial sessions, orphan `adb`, unsaved work | Synthetic data; dedicated host or VM; a scenario-specific recovery oracle |
| Cache deletion | Irrecoverable removal of temporary sessions | Seeded disposable cache only; verify protected and open sessions first |
| Clock, locale, display, energy, and appearance changes | Affects the whole login session | One change at a time; record the exact original; restore immediately |
| TCC grant or reset | `tccutil reset` is account-wide and revokes consent other applications depend on | Dedicated account; enumerate affected services first; never reset `All` |
| Gatekeeper or SIP changes | Weakens the machine's security posture, and SIP requires a reboot into recovery | **Do not disable either.** If a scenario appears to need it, the scenario is wrong; record the platform behaviour instead |
| ADB server, device, or USB-approval mutation | Disrupts IDEs, other users, and other devices | Dedicated device and host; serial-qualified commands |
| `DYLD_INSERT_LIBRARIES` and loader-integrity probes | Malware-like or policy-sensitive execution | Isolated VM; inert, hash-recorded probe library; owner approval; note that SIP and the platform's own protections block much of this by design, and that a blocked probe is itself a result |
| Memory pressure | `memorystatus` can terminate unrelated user processes, including the window server's clients | Dedicated VM or Mac; use `memory_pressure` with a bounded duration; never on a machine holding unsaved work |

### 2.11 Run-time capability and claim manifest

Before selecting N/A rows, create a short capability manifest beside the run
header. A control being absent is an observation, not proof that the capability
was never promised. Reconcile the exact candidate, its bundled `README.txt`,
release notes, [`SUPPORT.md`](SUPPORT.md), [`PRIVACY.md`](PRIVACY.md), and the
release announcement.

| Claim family | Record at run time | Applicability consequence |
|---|---|---|
| macOS platform | Claimed macOS versions, the two published architectures, Rosetta expectations, VM and remote-session limits | Selects §1.4 cells; an advertised but unavailable host cell is a coverage gap, not Pass |
| Distribution | Tarball only, bare executable rather than `.app`, unsigned, un-notarized, self-contained runtime, and the explicit absence of installers, Launch Services registration, file associations, and automatic updates | Drives B-01/B-02/B-16, A-28, I-12, P-13, P-18; any unexpected integration is tested, not ignored |
| Trust and consent | The exact quarantine remedy the release notes publish, what `spctl` is expected to say, and which privacy prompts are expected and under whose name | Determines the Q-states in §2.6; an undocumented prompt, or a missing one, is a finding against the published instructions |
| Storage location | [`PRIVACY.md`](PRIVACY.md) states the macOS data root is `~/Library/Application Support/VisualCat`, that a session directory is `700` and a portable `raw.log` is `600` wherever it is written, and that nothing else is left outside that root | Determines P-02, P-18, P-22; a resolved root that differs from the published one is a documentation-or-behaviour finding, not a tester's assumption to adjust |
| Sources | Finite import, growing follow, host ADB, supported formats, buffers, reconnect | A missing advertised source is Fail; an unavailable physical device may Block only the hardware-dependent attempt |
| Data exchange | Standard and portable sessions, `.vcat.zip`, CSV with its line-ending contract, and matching CLI, Windows, Linux, and Android exchange | Identifies mandatory I rows and their exact verification oracles |
| Keyboard contract | [`KEYBOARD.md`](KEYBOARD.md) lists Ctrl-based shortcuts and `F3`, with no macOS-specific mapping stated | Determines U-06 and its regression guards; whichever modifier the candidate actually uses, the document and the build must agree |
| Optional hardware and software | Force Touch, Touch Bar, multiple displays, ProMotion, a notched display, GPU class, VoiceOver, an input method, external and network volumes, Rosetta 2 | N/A requires an explicit unsupported claim or a recorded absence; do not generalize one probe result to all Macs |
| Diagnostics and network | Structured diagnostics setting, diagnostic bundle, telemetry, update and network statements, and the runtime diagnostics socket | Determines P-01 to P-03, P-15 and P-18, and whether any connection is expected after an explicit action |

For each row record one state — `claimed`, `explicitly unsupported`, `not
documented`, or `present but unclaimed` — plus the evidence location and the
scenario effect. A documented feature missing from the candidate is Fail. A
visible unclaimed feature must be tested for its safety and user-facing contract,
or the release is Blocked until the plan and documentation account for it.
Product source can explain a mismatch but cannot overrule the shipped user
contract during a release run.

---

## 3. Test data preparation

Every command in this section is written for the tools a stock Mac actually
has. macOS ships BSD userland, not GNU coreutils, and the difference is not
cosmetic: `sha256sum`, `stat -c`, `readlink -f`, `timeout`, and GNU `sed -i`
do not exist, `head -c -1` does not accept a negative count, and `date` has no
`%N`. A corpus recipe copied from the Linux plan will fail or, worse, silently
produce a different file. Appendix B records the individual traps; the recipes
below already avoid them.

### 3.1 Deterministic corpora and independent oracles

Generate data with the matching candidate `vcat`, but do not let the
implementation under test be its own only oracle. For each corpus, also retain
the generation seed and options, the SHA-256, the byte length, a line count from
a binary-safe scanner, and the expected outcome, facet, and template summary
produced by a previously trusted CLI or an independent fixture script. Compare
the candidate CLI separately in Tier I; I-14 proves the generator itself before
its output is allowed to serve as setup for a candidate result.

```shell
C='<corpus-root>'; V='<VCAT-CLI>'
mkdir -p "$C"
"$V" generate-test-log --output "$C/small.txt"  --lines 1000    --seed 42 --format threadtime
"$V" generate-test-log --output "$C/medium.txt" --lines 100000  --seed 42 --format threadtime
"$V" generate-test-log --output "$C/large.txt"  --lines 1000000 --seed 42 --format threadtime
"$V" generate-test-log --output "$C/xl.txt"     --lines 5000000 --seed 42 --format threadtime
for fmt in threadtime time brief long epoch; do
  "$V" generate-test-log --output "$C/fmt-$fmt.txt" --lines 5000 --seed 42 --format "$fmt"
done
( cd "$C" && shasum -a 256 -- * | tee SHA256SUMS.corpus )
( cd "$C" && for f in *.txt; do printf '%s bytes=%s lines=%s lf=%s cr=%s\n' \
    "$f" "$(wc -c <"$f")" "$(wc -l <"$f")" \
    "$(tr -dc '\n' <"$f" | wc -c)" "$(tr -dc '\r' <"$f" | wc -c)"; done )
```

Count carriage returns with `tr -dc '\r' < file | wc -c`. Do **not** test for
CRLF with a `grep` pattern built from a shell escape: in `bash`, `$"..."` is
locale-translation syntax, so `grep $"\r"` matches any line containing the letter
*r* and makes every file look like CRLF; in `zsh` the same expression means
something different again. That exact mistake has already manufactured a false
cross-platform line-ending finding against this product, and the line-ending
contract is one of the things a macOS run exists to check (I-07, I-15).

Also prepare the repository's own fixtures, which are versioned, sanitized, and
have known shapes, so they cost nothing and cannot drift with a generator change:

| Fixture | What it is |
|---|---|
| `samples/logcat_supersmall.txt` | The smallest bundled sample; a fast detection and first-paint check |
| `samples/logcat_small.txt` | The bundled deterministic sample referenced by the other plans |
| `samples/logcat_large.txt` | A bundled larger sample for a quick interactive check without generating a corpus |
| `test-data/golden-formats.txt` | The golden parser fixture; the authority for format detection and per-format parsing |

Read `samples/README.md` for what each file demonstrates, and hash all four into
the corpus manifest so a run states which revision of them it used. They
supplement the generated corpora; they do not replace the exact-count oracles
below.

Required ordinary corpora:

| Corpus | Purpose | Produced by |
|---|---|---|
| `small.txt` | Cold import, exact raw offsets, keyboard, export | generator block above |
| `medium.txt` | Multi-tab, facets, saved views, routine timing | generator block above |
| `large.txt` | One-million-entry performance, paging, and the snapshot-refresh A/B | generator block above |
| `xl.txt` | Limits, cache, soak, cancellation; not routine smoke | generator block above |
| `fmt-*.txt` | Detection and override across all five supported formats | generator block above |
| `mixed-formats.txt` | Honest unknown and outcome accounting | composition block below |
| `outcomes.txt` | One line of every parse outcome, for the B-20 gutter and off-timeline oracle | composition block below |
| `crashy.txt` | Known `AndroidRuntime`, a stack trace, untimed and continuation content | composition block below |
| `quiet-live-seed.txt` | One complete record followed by controlled pauses, for growing-file latency | composition block below |

The generator cannot produce the last four: they need content it deliberately
never emits. Build them by composing its output, so they remain reproducible from
a recipe rather than from an unrecorded hand edit. The script below writes LF
only; the newline form is varied deliberately in §3.2.

```shell
C='<corpus-root>'; cd "$C"

# mixed-formats.txt — concatenate whole single-format files byte for byte and
# record each part's byte range. Those ranges are the oracle; never re-derive
# them from the product afterwards.
: > mixed-formats.txt
for fmt in threadtime brief long epoch; do
  printf '%s starts at %s for %s bytes\n' "$fmt" \
    "$(wc -c < mixed-formats.txt | tr -d ' ')" "$(wc -c < "fmt-$fmt.txt" | tr -d ' ')"
  cat "fmt-$fmt.txt" >> mixed-formats.txt
done | tee mixed-formats.ranges.txt

# outcomes.txt — one line of every disposition the parser can reach, small
# enough to reconcile by eye. The long-format header is what makes the two
# following lines continuations rather than unknown lines.
printf '%s\n' \
  '--------- beginning of main' \
  '05-15 14:13:37.496  1073  1151 I VCatOracle: ordinary threadtime record' \
  '[ 05-15 14:13:37.500  1073: 1151 E/VCatLong ]' \
  'java.lang.IllegalStateException: VCAT-CRASH-<run-id>' \
  "$(printf '\tat com.example.app.Main.run(Main.java:42)')" \
  '' \
  'D/VCatBrief( 1073): brief record carries no timestamp' \
  '05-15 99:99:99.999  1073  1151 E VCatBad: impossible clock' \
  'this line is not a logcat header at all' > outcomes.txt

# crashy.txt — splice a crash block into generated traffic at a recorded offset.
head -n 500 small.txt > crashy.head
printf 'crash block starts at byte %s\n' "$(wc -c < crashy.head | tr -d ' ')" \
  | tee crashy.offset.txt
{ cat crashy.head
  printf '%s\n' \
    '--------- beginning of crash' \
    '05-15 14:13:40.000  1073  1073 E AndroidRuntime: FATAL EXCEPTION: main' \
    '05-15 14:13:40.000  1073  1073 E AndroidRuntime: Process: com.example.app, PID: 1073' \
    '[ 05-15 14:13:40.001  1073: 1073 E/AndroidRuntime ]' \
    'java.lang.IllegalStateException: VCAT-CRASH-<run-id>' \
    "$(printf '\tat com.example.app.Main.run(Main.java:42)')" \
    "$(printf '\tat java.lang.Thread.run(Thread.java:1012)')" \
    'D/VCatBrief( 1073): untimed brief record inside the crash block' \
    '05-15 99:99:99.999  1073  1073 E VCatBad: impossible clock' \
    'the crash reporter wrote this sentence with no header'
  tail -n +501 small.txt
} > crashy.txt
rm -f crashy.head

# quiet-live-seed.txt — the one complete record the §3.3 producer appends to.
printf '%s\n' \
  '--------- beginning of main' \
  '05-15 14:13:37.496  1073  1151 I VCatSeed: seed record' > quiet-live-seed.txt

shasum -a 256 -- *.txt | tee -a SHA256SUMS.corpus
chmod a-w -- *.txt      # originals are read-only; mutate disposable copies only
xattr -c -- *.txt       # strip any quarantine or where-from a download attached
```

That last line matters more here than anywhere else. If the corpus itself is
quarantined — because it was generated on one Mac and AirDropped to another, or
copied out of a downloaded archive — then a later import failure may be
Gatekeeper's decision about the *corpus*, not the product's behaviour. Strip the
attributes deliberately, record that you did, and keep one **deliberately
quarantined** copy for the P-tier row that tests the opposite case.

`outcomes.txt` is deliberately mostly defects, so automatic detection scores it
too low to select a format and refuses it. Import it with an explicit
`threadtime` override, in the preview or through the CLI's `--format`. That
refusal is the confidence threshold working, not a finding. The other three
detect cleanly and should be imported both ways — once on detection and once
overridden — so the override path is exercised on content whose oracle is known.
Give every year-less corpus an explicit year so its instants are deterministic
across hosts.

Imported as `threadtime`, `outcomes.txt` must account for its 9 source lines as
exactly 2 timed entries, 1 untimed entry, 1 meta record, 2 continuations, 1
unknown line, 1 rejected candidate, and 1 ignored blank. Every other composed
corpus must likewise account for each of its source lines exactly once.

The test manifest must name exact expected parsed, timed, untimed, continuation,
unknown, and rejected counts; first and last instant; severity totals; top
facets; template identities; and selected byte ranges. Never copy a count from
the GUI into the oracle after the test starts.

### 3.2 Adversarial corpora

Create deterministic, versioned, checksummed files. Keep originals read-only and
mutate disposable copies only. Several of these exercise things only macOS does,
and those are the rows neither a Windows nor a Linux run can cover.

| File | Required property / oracle |
|---|---|
| `empty.txt` | Zero bytes; one clear empty-source outcome |
| `notalog.bin` | Binary input; refused or fully accounted, never invented entries |
| `crlf.txt`, `lf.txt`, `mixed-eol.txt` | Exact byte offsets across LF, CRLF, and mixed endings |
| `cr-only.txt` | Refused, naming carriage-return framing as the cause — never imported as one record. This is the historical Mac OS 9 line ending, so a real Mac user is more likely than anyone else to have such a file |
| `bom.txt` | UTF-8 BOM handled without corrupting the first record |
| `nonutf8.bin` | Invalid sequences retained and accounted, no silent replacement claim |
| `truncated.txt` | A final incomplete line and an incomplete long-format record |
| `nofinalnewline.txt` | The last complete record is published at EOF |
| `longline.txt` | At least one 2 MiB line; bounded UI, wrap, and copy behaviour |
| `continuations.txt` | Stack frames and continuations linked and visible in source |
| `outoforder.txt` | Timestamp disorder retained; source order remains available |
| `dst.txt` | Ambiguous and invalid local times around a real DST transition in the recorded test zone |
| `pathological-regex.txt` | Long near-matches for `(a+)+$` timeout testing |
| `controls.txt` | ANSI CSI and OSC sequences, NUL, bidi overrides, zero-width characters, huge tokens |
| `unicode-name-😀-é-中文.txt` | Display-name handling with astral-plane and CJK characters |
| **NFC and NFD spellings of one visual name** | On APFS these are **one file**, not two: the volume is normalization-insensitive and normalization-preserving. Creating both must leave one file whose name is spelled as first written. This is the exact opposite of Linux, where they are two files, and it is the single most likely place for a cross-platform session-identity defect |
| `name with spaces, 'quotes' and $dollar.txt` | Shell-hostile but legal name; must never be word-split or expanded by the product or its own child processes |
| `name-with-newline.txt` (literal newline in the name) | Legal at the POSIX layer on macOS; pickers, notices, export names, and diagnostics must not corrupt or truncate it |
| `name:with:colons.txt` | Legal to POSIX APIs; Finder displays a `/` for each colon. Whatever the product shows, it must reopen and export the same file |
| `deep/.../log.txt` | Each component under 255 bytes but the total path beyond 1024 where the volume permits |
| `Session.vcat` / `session.vcat` pair | Two names differing only in case. On the default case-**insensitive** APFS volume they are one path; on a case-**sensitive** APFS volume they are two. Both must be exercised — see the note below |
| A file carrying extended attributes and a Finder tag | Must survive import unchanged; the product has no business reading or writing them |
| A file with a resource fork / `._` AppleDouble sibling | Produced by copying to a FAT or SMB volume; the `._` sibling must not be mistaken for a log |
| `.DS_Store` planted in a session directory | Finder creates one in any folder it displays. A session verifier must not be confused by it, and must say which it is if it refuses |
| `archive-traversal.vcat.zip` | `..`, absolute, drive-letter, symlink, and hard-link entries; duplicate and case-colliding paths; entries with POSIX mode bits including setuid |
| `archive-bomb.vcat.zip` | Declared and compressed size disproportion bounded before exhaustion |
| `archive-fifo.vcat.zip` | A FIFO or device entry; must be refused, not created |
| `archive-appledouble.vcat.zip` | An archive made by macOS `zip` without `-X`, carrying `__MACOSX/._*` members; must be refused or fully accounted, never unpacked as session content |
| session-corrupt copies | Manifest, schema, checksum, column, bitmap, and raw-source faults, one at a time |

> **The two case rows are a product assertion, not a filesystem curiosity.** The
> store's own path-containment check and the shared session-path comparer do not
> obviously agree about macOS: one repository comment says session paths are
> compared case-insensitively on Windows and macOS, another says the shared
> comparer is Windows-insensitive and ordinal everywhere else. A live run
> settles it. On the **default case-insensitive** volume, prove that two
> spellings of one session are one session everywhere the product counts,
> leases, protects, and deletes them. On a **case-sensitive** APFS volume, prove
> that two genuinely different sessions are never conflated, and that a manifest
> path differing from its session root only in case is refused rather than
> accepted as contained. Whichever way the candidate behaves, record it; a
> disagreement between the two mechanisms is a finding even when neither one
> visibly misbehaves on the volume you happened to use.

Build them from one recipe, not by hand, so that a corpus disagreeing with its
manifest is recognizable as a harness fault:

```shell
C='<corpus-root>'; cd "$C"

# --- newline forms, encoding, truncation ---------------------------------
cp small.txt lf.txt
sed -e 's/$/\r/' lf.txt > crlf.txt          # BSD sed: no -i without an argument
tr '\n' '\r' < lf.txt > cr-only.txt
{ head -n 200 crlf.txt; tail -n +201 lf.txt; } > mixed-eol.txt
printf '\xEF\xBB\xBF' > bom.txt && cat lf.txt >> bom.txt
head -c "$(( $(wc -c < medium.txt) / 2 + 37 ))" medium.txt > truncated.txt
printf 'truncated at %s bytes\n' "$(wc -c < truncated.txt | tr -d ' ')" > truncated.oracle.txt
# BSD head has no negative count: compute the length instead of subtracting in head.
head -c "$(( $(stat -f %z lf.txt) - 1 ))" lf.txt > nofinalnewline.txt
head -c 1000 lf.txt > nonutf8.bin && printf '\x80\xC3\x28\xFE\xFF' >> nonutf8.bin \
  && cat lf.txt >> nonutf8.bin
head -c $((10 * 1024 * 1024)) /dev/urandom > notalog.bin
: > empty.txt

# --- shape and content extremes ------------------------------------------
{ printf '05-15 14:13:37.496  1073  1151 I VCatLong: '
  head -c $((2 * 1024 * 1024)) /dev/zero | tr '\0' 'x'
  printf '\n'; cat lf.txt; } > longline.txt
{ head -n 50 lf.txt
  printf 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaab\n'
  head -n 50 lf.txt; } > pathological-regex.txt
printf '05-15 14:13:37.496 1073 1151 W VCatCtl: \033[31mred\033[0m \033]0;title\007 \xE2\x80\xAEoverride\xE2\x80\xAC %s\n' \
  "$(printf 'zero\xE2\x80\x8Bwidth')" > controls.txt
printf '05-15 14:13:37.497 1073 1151 W VCatCtl: NUL follows: ' >> controls.txt
printf '\x00 and the line continues\n' >> controls.txt
"$V" generate-test-log --output continuations.txt --lines 2000 --seed 42 --format long

# --- name topology: the macOS-only rows ----------------------------------
cp lf.txt 'unicode-name-😀-é-中文.txt'
cp lf.txt "name with spaces, 'quotes' and \$dollar.txt"
cp lf.txt "$(printf 'name-with\nnewline.txt')"
cp lf.txt 'name:with:colons.txt'
# NFC (single code point) and NFD (e + combining acute) spellings of "café".
cp lf.txt "$(printf 'cafe\xCC\x81-nfd.txt')"
cp lf.txt "$(printf 'caf\xC3\xA9-nfc.txt')"
# The assertion: on APFS these two DIFFERENT names are two files, but the SAME
# name in two normalizations is one. Prove it, on this volume, now:
cp lf.txt "$(printf 'norm-test-caf\xC3\xA9.txt')"
cp medium.txt "$(printf 'norm-test-cafe\xCC\x81.txt')"
ls -b | grep -c 'norm-test'          # 1 on APFS/HFS+, 2 on a case- and
                                     # normalization-sensitive volume
shasum -a 256 norm-test-*            # which content won tells you which name won
deep=$(perl -e 'print join("/", map { sprintf("d%03d", $_) } 1..60)')
mkdir -p "deep/$deep" && cp lf.txt "deep/$deep/log.txt"
printf 'deep path length: %s\n' "$(printf '%s' "$PWD/deep/$deep/log.txt" | wc -c | tr -d ' ')"

# --- a deliberately quarantined corpus file ------------------------------
cp lf.txt quarantined.txt
xattr -w com.apple.quarantine \
  "0081;$(printf '%x' "$(date +%s)");VCatTest;$(uuidgen)" quarantined.txt
xattr -l quarantined.txt
```

Keep a second copy of every hostile-name file on an ordinary-name path so a
failure can be attributed to the name rather than to the content. When the volume
under test cannot hold a name — FAT rejects `:` and `?`, and a case-insensitive
volume cannot hold the case pair — record that as a host property and run the row
on a volume that can. Creating a case-sensitive APFS volume for exactly that
purpose costs one command (Appendix A) and does not repartition the disk.

Damage session copies one fault at a time, against a session that has already
verified clean, and record the exact byte or field changed:

```shell
cp -Rp '<session-root>/<clean-session>' ./session-fault-manifest
#   manifest   truncate manifest.json mid-object
#   schema     raise manifest.json formatVersion to an unsupported major
#   checksum   edit one recorded digest in segments/000001/checksums.json
#   column     flip one byte in a segment column such as level.bin or pid.bin
#   bitmap     flip one byte under segments/000001/bitmaps
#   raw        flip one byte inside raw.log, leaving its length unchanged
#   mode       chmod 000 one segment file, and separately chmod 000 the directory
#   symlink    replace one segment file with a symlink to /etc/hosts
#   locked     chflags uchg one segment file (macOS-only: a "locked" file that
#              refuses deletion and modification even to its owner)
#   dsstore    drop a .DS_Store into the session directory, as Finder would
#   xattr      attach a Finder tag and a quarantine flag to raw.log
```

The last four have no Windows or Linux equivalent and are mandatory here. A
session whose segment has been replaced by a link out of the session tree must
fail verification and must never be followed outside the session root; a
`uchg`-locked file is the macOS shape of "this cannot be deleted" and the
deletion flow must classify it rather than leaving a half-removed directory; and
a stray `.DS_Store` or an extended attribute is ordinary macOS debris that a
verifier must tolerate or name precisely. Inspect the candidate's own session
layout first — segment and column file names belong to the format version under
test, not to this plan. See [`SESSION-FORMAT.md`](SESSION-FORMAT.md).

### 3.3 Growing-file producer

A growing-file test needs a producer whose ledger is the oracle, not a guess
about what the product should have seen. Append with a recorded UTC timestamp per
flush, and keep the producer's own log beside the session evidence. `date` on
macOS has no `%N`, so take sub-second time from `perl`, which is always present:

```shell
SRC='<corpus-root>/growing.txt'; LEDGER='<evidence-root>/<run-id>/producer.ledger'
cp '<corpus-root>/quiet-live-seed.txt' "$SRC"
i=0
while [ "$i" -lt 3000 ]; do
  i=$((i + 1))
  line="05-15 14:20:00.000  1073  1151 I VCatGrow: RUN=<run-id> seq=$i"
  printf '%s\n' "$line" >> "$SRC"
  printf '%s append seq=%s bytes=%s\n' \
    "$(perl -MTime::HiRes=time -MPOSIX -e 'my $t=time; printf "%s.%03dZ", strftime("%Y-%m-%dT%H:%M:%S", gmtime($t)), ($t-int($t))*1000')" \
    "$i" "$(wc -c < "$SRC" | tr -d ' ')" >> "$LEDGER"
  sleep 0.2
done
```

Vary the producer deliberately across passes: a partial line flushed without its
newline and completed seconds later; a long idle window followed by a burst; a
writer that exits between appends; and, for A-21, the ways a real macOS log
actually changes shape — `: > file` truncation in place, `mv` plus a fresh file,
and `newsyslog -nvv -f <conf>` followed by a real `newsyslog` run against a
dedicated configuration file on a dedicated host. macOS rotates system logs with
`newsyslog` rather than `logrotate`, and its default behaviour is rename-then-
recreate, which is the case the length comparison cannot see. On macOS, as on
Linux, the product can keep reading an unlinked file through its open descriptor;
whether it should is a product contract, so record what it does and compare it
with what the notice claims.

Two macOS-specific producer variants are worth their own passes. Append from a
process running under a **different** architecture than the reader — a native
`arm64` writer and a translated `x86_64` VisualCat, or the reverse — to prove
that translation changes nothing about file visibility or flush timing. And run
one pass with the followed file on an **iCloud Drive** path with *Optimize Mac
Storage* active, because the file can become dataless and be re-materialized
underneath the reader, which is a mutation neither truncation nor rotation.

### 3.4 ADB traffic and loss oracle

Traffic must be attributable to the run and must have an independent count. Emit
run-specific begin and end markers through the device's own `log` binary, and
keep the requested count as an input only — Android rate limiting and
ring-buffer overwrite make delivery a separate question the product has to report
honestly.

```shell
A='<adb> -s <serial>'
$A shell 'log -p i -t VCATTEST "RUN=<run-id> BEGIN steady"'
$A shell 'for i in $(seq 1 3000); do log -p i -t VCATTEST "RUN=<run-id> steady $i"; sleep 0.2; done'
$A shell 'log -p i -t VCATTEST "RUN=<run-id> END steady"'

# independent count of what the device buffer still holds for this run
$A logcat -d -v threadtime -s VCATTEST | grep -c "RUN=<run-id> steady"
$A logcat -d -v threadtime | grep -c 'chatty'      # declared drops
```

Probe that the device shell provides `log`, `seq`, and fractional `sleep` before
relying on them. Real system traffic — opening the camera, installing a large
app, toggling airplane mode — produces distinctive multi-buffer bursts that a
full-device capture should show, and is often a better stress source than a
synthetic loop. Stop every generator explicitly during cleanup.

Record the `adb` binary's own architecture (`lipo -archs "$(command -v adb)"`).
A `x86_64`-only `adb` on an Apple-silicon Mac runs under Rosetta, which is fine
but worth knowing when a transport misbehaves, and a universal binary run by a
translated VisualCat may execute in either architecture depending on how it was
spawned. On an Apple-silicon laptop the **first** USB connection of a new device
also raises an *Allow accessory to connect* prompt; until that is answered the
device is invisible to `adb`, which looks exactly like a cable fault. Answer it
before the measured interval and say in the run header that you did.

---

## 4. Evidence, budgets, and instrumentation

### 4.1 Evidence for every scenario

1. **Run boundary** — `<run-id>`, scenario ID, host and user identity, local and
   UTC start and end, candidate archive and executable hashes, PIDs, the
   architecture and whether the process was translated, the M/Q/S states, and the
   exact command or source.
2. **Before and after screenshots** at assertion moments. Capture all displays
   when off-screen placement or scaling is relevant, and retain native pixel
   dimensions — which on a Retina display are twice the logical size, so record
   both. `screencapture` needs the *Screen Recording* privacy grant, granted to
   the **terminal** that runs it; a capture taken without it silently comes back
   as desktop wallpaper with no windows, which looks like the app having
   vanished.
3. **Video or trace** for ordering, animation, resizing, input latency, hangs, or
   focus. State the recorder, resolution, fps, dropped frames, and likely
   overhead.
4. **Visible product text verbatim** — title and version, status, notices,
   counts, source identity, progress and final state, errors, selected scope, and
   file name.
5. **Product artifact** — the session directory or a portable copy, plus CLI
   `verify`, the manifest, the export, and a raw-source hash as applicable. Never
   mutate the only copy while collecting evidence. Copy with `cp -Rp` or `ditto`,
   which preserve extended attributes; a plain `cp` does not, and on macOS that
   silently changes what you are holding.
6. **Process and resource samples** — PID and executable path, CPU, `footprint`
   (the phys-footprint figure macOS itself uses under memory pressure), RSS,
   compressed memory, open descriptors, mapped regions, threads, child `adb`
   processes, I/O, free space, power state, display topology, and thermal state
   at scenario-defined points.
7. **Host failure evidence** — time-bounded `log show` output for the process and
   for the system, `.ips` crash and hang reports from
   `~/Library/Logs/DiagnosticReports/`, the process exit status or terminating
   signal, `memorystatus` entries in the unified log for a jetsam kill, and
   `sample`/`spindump`/`fs_usage` evidence for hang, lock, or I/O cases.
8. **Product structured diagnostics** when sequencing matters. *Appearance &
   timeline* carries *Write redacted structured diagnostics*; inspect and record
   its current value, enable it when the scenario requires it, collect
   `<diagnostics-root>/visualcat-*.jsonl` alongside the session, hash it, and
   restore the prior value. Those records are the best available oracle for
   ingest ordering: `ingest.detected`, `store.snapshot` with its
   `snapshotGeneration` and `timedEntries`, and `ingest.ready` turn "the numbers
   look wrong" into "the view stopped at snapshot generation 2 of 5".
9. **External oracle** — the corpus manifest, the producer ledger, ADB markers,
   trusted CLI output, file hashes and byte ranges, the accessibility tree, or a
   trace query.

Store evidence under `<evidence-root>/<run-id>/<scenario-id>/` with an index and
a SHA-256 list. Logs, sessions, screenshots, clipboard captures, crash reports,
and traces can contain source payloads, user names, paths, serials, tokens, and
account data. Restrict access, use synthetic input, review before sharing, and
delete according to the run retention policy. Keep the evidence root **out of**
iCloud Drive, Desktop, and Documents: with *Desktop & Documents Folders* sync
enabled — which is on by default for many accounts — an evidence directory is
uploaded as you create it, and a crash report containing log payload leaves the
machine without anyone deciding that it should.

Use stable evidence names such as
`<scenario>-attempt-<nn>-<utc>-<assertion>-<state>.<ext>`. The scenario index
must record assertion, UTC, producing tool and version, original path, SHA-256,
sensitivity class, redaction status, and any derived or redacted copy. Hash the
native original before annotation, cropping, transcoding, masking, or redaction;
never replace it with an edited copy. Every stated pass condition needs an
evidence pointer or an explicit reason that the assertion is N/A or Blocked.

### 4.2 Performance and responsiveness budgets

These are provisional absolute gates plus regression signals. Use at least five
cold or ten warm repetitions for short timings; report median and p95 and record
every discarded run. Keep macOS version, chip, translation state, candidate hash,
power source and Low Power Mode, Spotlight indexing policy, display topology and
scale, refresh rate, renderer, S-state, and corpus constant. On Apple silicon
also record whether the window was frontmost: the scheduler moves a background,
hidden, or App-Napped process onto the efficiency cores, which changes throughput
by a large factor for reasons that have nothing to do with this product. A
measurement taken while the window was behind another one is not comparable to
one taken in front of it, and on a fanless Mac neither is comparable to one taken
after the machine has warmed up.

**There is no accepted macOS baseline yet** (see the note at the top of this
document), so the >20% regression signal used by the other plans has nothing to
compare against on a first run. Establish the baseline, state that it is a first
observation, and apply the regression signal from the second controlled run
onward. Never restate a Windows or Linux figure as if it had been reproduced
here: the controlled harness gates in [`PERFORMANCE.md`](PERFORMANCE.md) belong
to a Windows x64 reference machine, and its scheduled Linux data point belongs to
a shared GitHub-hosted runner. Name which of the four measurement paths —
reference machine, hosted runner, live Linux host, live Mac — produced every
number, and keep native and translated macOS results as two separate baselines.

| Signal | Starting budget | Measurement |
|---|---|---|
| Cold launch to usable empty state | median ≤3 s; no run >5 s, after a separate first-run measurement | Wall clock from exec to a drawn empty state on video; process start alone is not first usable frame |
| First-ever launch of a quarantined candidate | Measured and reported separately, never discarded | Includes Gatekeeper's assessment and an XProtect scan of a large self-contained tree, plus cold page-cache reads and font-cache work. This is the real first-user experience and on a Mac it is the slowest single thing in this table |
| Warm launch | median ≤1.5 s; p95 ≤2.5 s | Close normally, relaunch from the same extraction and home |
| Input → visible acknowledgement | p95 ≤100 ms; none >250 ms for local commands | High-fps external camera, or an Instruments trace correlating input with presentation |
| Open or close an owned dialog or the More menu | ≤250 ms to settled state | Video |
| Close a dialog whose background work is still running | Acknowledged ≤250 ms; closed or truthfully `Cancelling…` ≤2 s; the child process is gone before the next assertion | Video plus `ps`/`lsof` observation; a dialog that outlives its own child is a finding |
| Resize, move between displays, or change scale | No freeze >500 ms; no stale-scale frame persisting >1 s | Video plus display-change timestamps |
| Import preview for an ordinary small or medium file | ≤1 s after the picker returns | Stopwatch or video; exclude any TCC prompt time and say so |
| First heat map after import starts | ≤3 s | Product progress plus video |
| Sustained one-million-line import | Establish the macOS baseline; never below 30,000 full-pipeline lines/s natively on the reference class, and report the translated figure separately without holding it to the native gate | Wall clock and manifest counts; exclude preview time explicitly |
| Search over 1 M entries | first result ≤1.5 s | Stopwatch from the Enter or search action |
| Full-view 2,000-column heat-map query | Structured-diagnostics p95 ≤20 ms; the UI never freezes >500 ms | Structured diagnostics plus the matching benchmark result; do not substitute one measurement path for the other |
| Timeline pan and zoom | No freeze >250 ms; ≤15% missed frames **against the display's active refresh at the time**; see §4.3.1 before reading any percentage | §4.3.1 procedure; a zero-frame or fixed-frame measurement is Blocked |
| Entry page (`Load 500 more`) | ≤400 ms | Video and the status or count |
| Reopen a finalized session of ≤1 M entries | median ≤5 s; no run >10 s | From the command to a correctly drawn plot and final count, 3 runs |
| ADB discovery | initial list ≤5 s; a 2 s refresh reflected within one interval | Dialog video and ADB trace |
| ADB or growing source, first complete line | visible within 2 s of a producer flush once capture is running | Producer UTC ledger and product video |
| Stop → sticky acknowledgement | ≤250 ms | Video |
| Stop → saved | No fixed absolute cap; the elapsed indicator must advance and the stage must remain truthful | Total by entries and bytes, compared with baseline |
| Live capture UI refresh | Bounded by the *Live UI refresh limit (Hz)* setting in *Appearance & timeline*; no busy loop at any permitted value | CPU sampling at the lowest and highest allowed value, plus the §4.3.1 frame measurement |
| Hidden, minimized, or occluded live view | No continuous redraw or query cadence; acquisition continues | CPU sampling plus manifest progress before, during, and after. Record whether App Nap engaged (`pmset -g assertions`, and the process's state in Activity Monitor) — a drop in CPU may be the platform throttling the app rather than the product relaxing its own cadence, and those are different results |
| Idle growing-file follow | Footprint and the managed heap settle; post-warm-up growth ≤32 MiB/hour and no sustained gen2 cadence attributable to polling | 15-minute samples plus GC counters |
| Memory at 1 M entries | No jetsam kill; peak `footprint` within the established macOS baseline, with mapped session segments accounted separately | Fixed progress samples from `footprint` and `vmmap -summary`. On macOS, memory compression means RSS understates pressure and overstates residency; `footprint`'s phys-footprint is the figure the platform itself uses |
| Open descriptors and mapped regions | Both plateau; neither approaches `ulimit -n` | `lsof -p` count and `vmmap -summary` at fixed points. macOS has historically set a far smaller default open-file limit than a Linux desktop, and it changes between releases, so a memory-mapped session store can reach it sooner here; record this host's actual `ulimit -n` and `launchctl limit maxfiles` beside every count rather than assuming a value |
| Soak resources | No sustained post-warm-up positive slope in footprint, descriptors, threads, mapped regions, or latency | Rolling-window medians at least every 15 minutes |
| Crash, hang, jetsam, spin | Zero attributable events in the scenario window | `~/Library/Logs/DiagnosticReports/`, `log show`, the exit status or signal, and Activity Monitor's *Not Responding* state |

For leak claims, define warm-up, cadence, workload, and comparison windows before
starting. Memory-mapped segments, font and shader caches, and loaded rows can
grow legitimately. Fail growth that does not plateau, exceeds the accepted
envelope, and is corroborated by increasing retained mappings, descriptors, or
threads, worsening latency, or exhaustion risk. A single rising line in Activity
Monitor is not a leak oracle, and on macOS the *Memory* column in particular
mixes compressed and mapped pages in a way that makes it the worst available one.

### 4.3 macOS instrumentation

Bind every sample to the recorded PID. A name-only sample mixes two instances,
and `pkill -f` on a pattern that appears in your own command line kills the
sampling shell instead of the target — use `pkill -x VisualCat`, or the PID.

```shell
pid=$(pgrep -x VisualCat)
ps -o pid,ppid,user,stat,etime,time,rss,vsz,comm -p "$pid"
footprint "$pid" 2>/dev/null | head -20        # phys_footprint: the figure that matters
vmmap -summary "$pid" | head -30               # mapped regions, dirty, swapped
lsof -p "$pid" | wc -l                         # descriptors; compare with ulimit -n
lsof -p "$pid" | awk '{print $5}' | sort | uniq -c | sort -rn | head
top -l 2 -pid "$pid" -stats pid,cpu,mem,threads,state | tail -5
sample "$pid" 10 -f '<evidence-root>/<run-id>/sample.txt'
heap "$pid" 2>/dev/null | head -20             # allocation profile, when needed
```

Time-bound host failure evidence to the recorded scenario interval. The unified
log is the counterpart of `journalctl`, and it needs an explicit predicate or it
returns the whole machine:

```shell
log show --style compact --start '<scenario-start-local>' --end '<scenario-end-local>' \
  --predicate 'process == "VisualCat" OR senderImagePath CONTAINS "VisualCat"'
log show --style compact --last 30m --predicate \
  'eventMessage CONTAINS "VisualCat" OR eventMessage CONTAINS "memorystatus"'
ls -lt ~/Library/Logs/DiagnosticReports/ | head -20
grep -l VisualCat ~/Library/Logs/DiagnosticReports/*.ips 2>/dev/null | head
spindump "$pid" 10 -file '<evidence-root>/<run-id>/spindump.txt'   # for a hang
sudo fs_usage -w -f filesys "$pid"             # I/O attribution; needs privileges
leaks "$pid" 2>/dev/null | head -20            # leaked allocations, when needed
```

Thermal and power state are required evidence for every soak and performance row
(§1.4, §4.1, §13.1), and a fanless Mac will throttle long before a desktop does.
Sample them on a fixed cadence for the whole interval rather than once at the
end:

```shell
pmset -g therm                                 # CPU speed and scheduler limits
pmset -g ps; pmset -g assertions               # power source and what holds it awake
sudo powermetrics --samplers smc,cpu_power -n 1 -i 1000   # thermal pressure, package power
log show --last 10m --predicate 'eventMessage CONTAINS[c] "thermal"' --style compact
```

A run whose throughput falls while `pmset -g therm` shows a reduced CPU limit has
measured the enclosure, not the product. Record the thermal series beside the
throughput series so the two can be read together, and say whether the Mac was on
AC, on battery, or in Low Power Mode.

A `.ips` crash report is a JSON header followed by a JSON body; `plutil -p` will
not read it, but the first line is parseable JSON and the rest is readable as
text. Treat every crash report as sensitive: a crash report of a log viewer
contains log content by construction, and macOS may be configured to offer it to
Apple. That offer is host policy, not product telemetry, and P-15 says so.

For CPU and allocation questions use Instruments when Xcode is installed (Time
Profiler, Allocations, Leaks, File Activity, Metal System Trace); otherwise
`sample`, `heap`, `leaks`, and `footprint` answer most of them without it.
`fs_usage`, `dtruss`, and the other DTrace-family tools require privileges and
are restricted by System Integrity Protection; a tool that returns nothing under
SIP is an instrumentation Block, not a clean result.

For managed-runtime detail, `dotnet-counters` and `dotnet-trace` attach to the
self-contained process through its diagnostics socket in `$TMPDIR`; record the
socket path and the tool version, because a mismatched diagnostics tool fails in
a way that looks like the app refusing to respond. On macOS that socket lives in
the account's private `/var/folders/…/T/` directory rather than in `/tmp`, which
matters for P-18.

For accessibility, use VoiceOver plus Accessibility Inspector from Xcode.
Automation-tree presence is necessary, not sufficient: activate each primary flow
with assistive technology and confirm spoken names, state, order, live updates,
and modal boundaries.

### 4.3.1 Frame pacing on macOS — measure the pipeline that exists

macOS has no counterpart to ETW's present stream or Android's FrameTimeline, and
an adaptive-refresh display makes the naïve measurement actively misleading.
Pick one of the following, in this order of preference, and record which was
used; a budget measured by a different method is not comparable across hosts.

1. **An external high-frame-rate camera** pointed at the physical screen, with a
   timestamped input action visible in frame. This is the only method that works
   identically on ProMotion and fixed-refresh displays, native and translated,
   hardware and paravirtual, and it is the documented fallback the other two are
   checked against. It measures the thing that matters — input to visible change
   — rather than a frame count.
2. **Instruments**, where Xcode is installed: the *Core Animation*, *Displays*,
   and *Metal System Trace* instruments, attached to the recorded PID. Record the
   Instruments version and confirm the numbers move when the workload does.
3. **`sample` on the render thread**, used to show where time goes rather than to
   compute a jank percentage. Useful for attribution, never sufficient as the
   gate by itself.

Four rules keep this honest, and the second one is macOS-specific enough that a
Windows or Linux tester will get it wrong:

- **A zero-frame or constant-frame measurement is Blocked, never Pass.** Assert
  that the frame count is greater than zero and that it responds to load before
  reading any percentage, and record the count beside it.
- **On ProMotion, a low frame rate is not jank.** An adaptive display drops to a
  low refresh when the content is static, entirely correctly, and a static
  VisualCat window will therefore report a small number of frames per second
  while being perfectly responsive. Record the display's **active** refresh
  during the measured interval, not its maximum, and compute missed frames
  against that. Where the comparison matters, pin the display to a fixed refresh
  rate in System Settings and say that you did.
- **Record the renderer and the architecture.** Note which rendering path
  Avalonia selected on this host, and whether the process was translated.
  Hardware acceleration, a software fallback, a VM's paravirtual adapter, and
  Rosetta all produce different absolute numbers for identical code; each is its
  own baseline, not a regression against another.
- **Record whether the window was frontmost and unoccluded.** macOS throttles
  timers and lowers the priority of an occluded or App-Napped process by design.
  A measurement taken while another window covered VisualCat is measuring the
  platform's policy, which is what X-07 tests deliberately and what every other
  row must avoid accidentally.

Before each measured pass, open the same finalized session, fit the same
viewport, warm the same panes, let Spotlight and any background import settle,
disable display sleep with `caffeinate`, and record display, scale, refresh,
renderer, window state, and PID. Run at least three 30-second passes per
configuration, report every pass, the median, and the worst p95; do not keep only
the smoothest one. Screenshots and video corroborate a visible defect but do not
replace a frame or latency measurement.

### 4.4 Human interaction, visual, accessibility, and automation oracle

Run the human path before inspecting or automating it. For every primary action,
judge the complete interaction loop:

1. **Discoverability** — the action and its scope can be found from visible text,
   conventional placement, or a documented shortcut; hover is not the only clue.
   On macOS this includes the question of whether a Mac user would look for the
   action in a menu bar that this product does not populate.
2. **Affordance and state** — enabled, disabled, selected, destructive, default,
   and progress states are distinguishable visually and through the accessibility
   tree.
3. **Acknowledgement** — input receives visible feedback within §4.2, and longer
   work keeps a truthful stage and a cancel or stop affordance without stealing
   focus.
4. **Outcome** — completion or failure names the affected source, session, scope,
   row count, and destination needed to verify what happened.
5. **Recovery and reversibility** — cancellation preserves prior work; a failure
   keeps viable retry, change-destination, or inspect actions; destructive
   choices state the exact object and require proportionate confirmation.
6. **Consistency** — equivalent pointer, keyboard, trackpad, automation,
   command-bar, More-menu, and shortcut routes have the same meaning, and live
   layout changes never move a repeated action into another command.

Use a WCAG-aligned release floor even though VisualCat is a native desktop app:
ordinary text at least 4.5:1; large text (at least 18 pt regular or 14 pt bold,
or the rendered equivalent) at least 3:1; and active control boundaries, focus
and selection indicators, icons, and graphical cues required to understand or
operate the product at least 3:1 against adjacent colours. Where a meaningful
graphic uses lower-contrast gradation, demonstrate an equivalent textual or
non-colour route to the same information. Do not round a value up to pass, and do
not rely on colour alone. Record sampled foreground and background values, tool
and version, state, appearance, and display profile. Measure from a native-pixel
screenshot on a display whose colour profile you have recorded — a wide-gamut
Mac display and an sRGB external one will give different sampled values for the
same drawn colour, and the profile is part of the measurement. See the W3C
guidance for
[text contrast](https://www.w3.org/WAI/WCAG22/Techniques/general/G18.html) and
[non-text contrast](https://www.w3.org/WAI/WCAG22/understanding/non-text-contrast.html).

Visual comparisons use native-pixel captures from an approved reference with the
same candidate, corpus, window bounds, backing scale, text scale, appearance,
contrast setting, accent and highlight colour, culture, font set, S-state, and
interaction state. **Backing scale is part of the baseline on macOS**: a capture
from a 2× Retina display is twice the pixel size of the same window on a 1×
external display and the two are not comparable, and a "scaled" resolution
produces a third result again. Record the display, its scaled resolution, and its
backing scale with every capture. Never resample an image to make it align. Mask
only named nondeterministic regions such as a clock, PID, device serial, or live
rate; keep the unmasked original and the mask definition. A baseline change is a
separate reviewed artifact, not an automatic consequence of the candidate being
different. Cover normal, hover, focus, pressed, disabled, selected, loading,
empty, failure, long-content, and modal states. A pixel diff alone cannot Pass
usability, semantics, focus, animation, or screen-reader behaviour.

Automation should select by stable semantic properties — the accessible name,
role, and owned hierarchy exposed to the platform's accessibility API — and
assert name, role, and state as well as activation. Three macOS-specific facts
shape how that is done, and all three must be recorded in the run header:

- **Driving the UI requires a privacy grant.** AppleScript through
  `System Events`, `cliclick`, and any other synthetic-input tool need the
  *Accessibility* grant, and that grant belongs to the **terminal** that runs
  them, not to the tool. Without it, commands fail with an authorization error or
  silently do nothing, which looks exactly like a product that ignores input.
- **An unbundled process is awkward to address.** `System Events` addresses
  applications by bundle identifier or by name from Launch Services, and this
  product has neither a bundle nor a registration. Establish how the automation
  reaches the window at all — by process name, by window title, or by
  accessibility element — and record the method; if it cannot be reached, that
  is a fact about the shipped format, and the row falls back to manual
  interaction rather than to N/A.
- **Coordinate playback is not a release oracle.** Backing scale, text size,
  responsive command folding, virtualization, scroll position, and notice
  insertion all move targets, and a screenshot pixel on a Retina display is not a
  click coordinate. Geometry scenarios may use coordinates only after recording
  the logical-to-physical transform and confirming that the pointer hit the
  intended semantic control.

Keep manual and VoiceOver checks for the Skia timeline and any custom control
whose accessibility surface cannot express the visual relationship. Where the
toolkit's macOS accessibility support cannot express a relationship at all,
record that as a known limitation with its user-facing effect, not as N/A.

---

## 5. Tier B — basic scenarios

Purpose: prove the exact macOS candidate works in the ordinary path a new reader
takes. All applicable B scenarios pass before A or X work begins; a primary-path
failure can invalidate later observations.

**Block format:** *Risk* — what could go wrong. *Pre* — starting state. *Steps* —
what to do. *Expect* — observable pass criteria. *Fail if* — disqualifying
observations.

---

### B-01 · Verify, extract, and cold-launch the exact tarball

*Risk* The uploaded product is not the built product, cannot be extracted safely,
loses its executable bit, needs an undeclared runtime, or is misrepresented by
the platform's own security machinery.
*Pre* M0, D1, S0, and the Q-state recorded. Candidate never executed on this
account.
*Steps* Verify checksum, the `SHA256SUMS` line, and the provenance attestation
per §2.4 · list archive members and check them against the safety rules · extract
into a fresh directory · record the inventory, mode bits, extended attributes,
and hashes · read `README.txt` and assess whether a first-time reader learns how
to verify and how to launch · launch from Terminal with no arguments and watch
stdout and stderr · leave the empty state untouched for 30 s.
*Expect* Checksum and attestation match. No member is absolute, traversing,
setuid, world-writable, a device, a FIFO, or a link outside the archive, and none
is an AppleDouble `._` file or a `.DS_Store`. The root holds `VisualCat`,
`LICENSE`, `THIRD-PARTY-NOTICES.md`, and `README.txt`. `VisualCat` extracts
executable. Launch reaches a usable empty state inside the cold-launch budget,
writes nothing alarming to stderr, and requires no administrator authorization,
no package installation, and no system .NET. A window appears, is frontmost, and
accepts keyboard input without the tester having to click it — a bare executable
does not always get activated automatically, and a window that opens behind the
terminal and cannot be reached is a finding, not a quirk. The displayed version
equals the candidate version in the archive name and in `README.txt`.
*Fail if* Any archive-safety rule is violated; the checksum, attestation, or
version disagrees; the binary is not executable straight from a `tar -xzf`
extraction of a workflow-produced archive; a system .NET or administrator
authorization is required without the README saying so; the identity or version
misrepresents the artifact; launch produces an unhandled exception trace instead
of a window; or the window never becomes reachable.

### B-02 · Code signature, architecture, and translated execution

*Risk* macOS is the only supported platform that can refuse to execute a correct
binary outright, and the only one where a second published architecture has never
been executed by any automated gate.
*Pre* Both `osx-arm64` and `osx-x64` archives available. Record the Mac's own
architecture and whether Rosetta 2 is installed.
*Steps* Run the §2.4 signature block against both archives' `VisualCat` and
`vcat` and against the bundled `.dylib` files · record `lipo -archs` or `file`
for each · run `spctl --assess --type execute` and record its exact words · on
Apple silicon, launch the native `osx-arm64` build and then the `osx-x64` build,
recording for each whether it started and whether the process was translated ·
on an Intel Mac, launch `osx-x64` natively · where Rosetta is absent, attempt the
x64 launch anyway and record what macOS offers.
*Expect* Every shipped Mach-O carries a valid signature — expected to be ad-hoc
— and `codesign --verify --strict` passes on the apphost and on every bundled
library. The architectures present match the artifact name: an `osx-arm64`
archive contains `arm64` code and an `osx-x64` archive contains `x86_64` code.
`spctl` rejects both as unsigned by a known developer, which is the documented
state for this release and not a finding. The native build launches. The x64
build launches on an Intel Mac, and on Apple silicon it launches under Rosetta 2
and its process reports as translated; where Rosetta is not installed, macOS
offers to install it or the launch fails with a message naming Rosetta rather
than with an unexplained error. The two architectures produce identical counts,
manifests, exports, and hashes for the same corpus; only timings differ.
*Fail if* A shipped binary has no signature, or a signature that fails
verification — on Apple silicon that shows as `Killed: 9` with no other
diagnosis, and every dependent row is then Blocked. Also fail if an archive
contains the wrong architecture, if a team identifier appears that nobody claims
to have used, if the x64 build cannot run under Rosetta with no explanation
offered, or if the two architectures disagree about any analytical result.

### B-03 · Runtime dependency and self-containment probe

*Risk* A self-contained publish still depends on the host for system frameworks
and for the dynamic loader's search behaviour; this is the most likely
first-launch failure on a Mac that is not the build host.
*Pre* B-01 extraction present. Record the library inventory *before* installing
anything, and confirm no Homebrew, no Xcode, and no .NET are required.
*Steps* Run `otool -L` against `VisualCat`, `vcat`, and the bundled native
libraries and record every dependency that is not inside the extraction
directory or a macOS system path · record the minimum macOS version each Mach-O
declares (`otool -l ... | grep -A4 LC_BUILD_VERSION`) and compare it with this
host's version · confirm the product starts on an account with an empty `PATH`
beyond the system default · confirm it starts with Homebrew absent from `PATH`
entirely.
*Expect* Nothing outside the extraction directory and the macOS system
frameworks is required. The declared minimum macOS version is at or below the
oldest version [`SUPPORT.md`](SUPPORT.md) claims, and a host older than that
declared minimum fails in the loader with a version message rather than
crashing silently. No Homebrew library, no Xcode component, and no system .NET
is needed for either surface.
*Fail if* An undocumented host dependency is required; a missing framework
produces an unhandled managed exception dump with no identifiable cause; the
declared minimum macOS version is newer than the claimed support floor; or the
product works only when a package manager's library directory is on the search
path.

### B-04 · Desktop empty state and command inventory

*Risk* The first screen must offer the desktop commands and no phantom ones.
*Pre* B-01 session running, D1.
*Steps* Read the empty state without touching anything · enumerate every visible
command and every command in *More* · hover and keyboard-focus each one · check
the menu bar.
*Expect* The identity line shows `VisualCat <version> · local-first · no
telemetry`, with a version that matches the installed build and a `-dev` suffix
on a non-release build. The desktop hero actions are present — open a log, open a
session, ADB live, follow a growing file — together with recent sessions when any
exist. Session-dependent commands, including export, save, share, and
lines-not-on-the-timeline, are disabled and say why. No Android-only control
appears. Whatever the menu bar contains, it is consistent with §2.11: this
product does not claim a populated application menu, so a minimal or
toolkit-default menu bar is expected — but it must not offer a command that does
nothing, and the application's name in it must not be a file-system path or a
placeholder.
*Fail if* A documented desktop command is missing, a session command is enabled
with no session, a command is present but inert, the version does not track the
artifact, or a menu-bar item exists that performs no action.

### B-05 · Import a small file through the macOS file chooser

*Risk* The file chooser is where macOS inserts its privacy machinery, and where a
process with no bundle identity behaves differently from every application the
user has seen before.
*Pre* `small.txt` in an ordinary directory. Q6 recorded: know in advance which
privacy grants the launching terminal already holds.
*Steps* *Open log* · record whether a privacy prompt appears and, if so, **what
application name it uses** · navigate and select `small.txt` · observe the import
review and the ingest · repeat once with the file on `~/Desktop`, once on
`~/Documents`, once on `~/Downloads`, and once on an external volume · repeat
once with a file whose name contains a space and an accented character in NFD ·
repeat once by pressing ⇧⌘G and typing a path rather than clicking.
*Expect* The chooser appears within budget and is usable by pointer and keyboard.
The selected file is opened whatever route was used. Ingest completes; parsed,
timed, untimed, unknown, and rejected counts match the corpus oracle; the heat
map draws; the tab is named after the file. A confidently detected file is not
interrupted by the review. Where macOS asks for consent, the product continues
after approval and reports a denial as a denial, naming the folder, rather than
as a missing or unreadable file. A name in NFD is displayed, tabbed, and recorded
exactly as the volume stores it.
*Fail if* The chooser does not appear or returns a path the product cannot open;
the product reports a different file than the one chosen; counts disagree with
the oracle; a name with non-ASCII characters is corrupted in the tab, the notice,
or the session manifest; or a denied folder is reported as a missing file, which
sends the reader looking for something that is sitting there and readable by
every other application.

### B-06 · Heat map to exact source bytes

*Risk* The core value proposition, end to end, with byte fidelity.
*Pre* B-05 session open.
*Steps* Read the six severity rows · click a dense cell · read the entry list ·
select a row · open the entry inspector · read the raw source context · verify
those bytes independently against the corpus with a byte-exact reader.
*Expect* Cell → entries → selected entry → raw source are consistent: the same
instants, the same message, and raw bytes that match the file at the stated
offset. Use `dd` or `tail -c +N | head -c M` — not a line-oriented tool that can
transform encoding or line endings. The source gutter starts at line 1 and the
selected line stays visible with its context.
*Fail if* The raw view shows a different record than the selected row, offsets do
not resolve, or the gutter numbering disagrees with an independent count.

### B-07 · Severity filters and clear semantics

*Steps* Toggle each of the six severities individually and in combination ·
observe counts, chips, and the plot · *Clear all*.
*Expect* Counts change consistently with the plot; every active filter is named
in the chip bar; clearing returns to the unfiltered view exactly and leaves no
residue.
*Fail if* A severity's count and its plotted density disagree, or a cleared
filter leaves a chip or a hidden constraint behind.

### B-08 · Text and regex search

*Risk* Search identity and counter honesty, plus the one place where the
documented keyboard contract meets a Mac keyboard and may lose.
*Pre* A corpus whose match count exceeds 20,000, so an old marker cap would be
visible in the total; the oracle names the k-th match for at least one k above
it. Record whether this Mac has *Use F1, F2, etc. keys as standard function keys*
enabled.
*Steps* Search a literal known to occur · read the counter before stepping · step
forward and backward with the on-screen buttons · then with the documented
keyboard routes, and record for each one what the key actually did: `F3` and
`Shift+F3`, `N` and `Shift+N`, `Ctrl+G`, `Alt+Home`, `Alt+End`, and — because a
Mac user will try them first — `⌘G`, `⌘F`, and `⌘+↑`/`⌘+↓` · reach both ends and
step past them · press *Fit* and step once more · pan by hand and read the
counter · enable regex and run a valid pattern, an invalid one, and `(a+)+$`
against `pathological-regex.txt` · correct the invalid pattern · then type a
pattern containing straight quotes, a double hyphen and three dots, and read
back exactly what landed in the field.
*Expect* Before the first step the counter reads `– / N` where **N is every match
in the session**. Each step selects an exact record, reveals its row even when
that row is past the loaded page, and preserves the chosen zoom. Stepping wraps
at both ends. From *Fit* a step still selects a record and opens a readable
window around it. A manual pan returns the counter to `– / N` without changing
the search. *Go to match* states its order, refuses an out-of-range number rather
than clamping it, and is inert with a reason when there are no matches. An
invalid pattern produces one product sentence — never a resource key or a
framework dump — leaves the previous result standing, and stops being reported
once the pattern is valid. The pathological pattern is cut off by the bounded
timeout, says so, and leaves the UI responsive.
Every keyboard route either works or is explained. `F3` on a Mac is Mission
Control unless the function-key preference is changed, so record whether the
product receives it at all, whether `fn+F3` is needed, and whether the `N`
alternative covers the same ground; whichever is true, [`KEYBOARD.md`](KEYBOARD.md)
and the build must agree.
What the field receives is part of this row too. macOS applies system-wide text
substitution in ordinary text controls: smart quotes replace straight ones, a
double hyphen becomes an en dash, and three dots become an ellipsis. Any of
those silently changes a regular expression or a literal before the product ever
sees it, and the reader watches their own correct pattern fail for a reason
nothing on screen explains. Record whether this build's search field is subject
to substitution, and if it is, whether the product opts out of it. The same
question applies to every free-text field reached in A-06 and A-14 — a time-zone
identifier or a path is just as easy to corrupt as a pattern.
*Fail if* The total stops at a marker cap, a step changes the zoom or skips a
match, wrapping fails, *Fit* leaves the view unchanged, the UI freezes on any
pattern, a corrected pattern keeps reading its old rejection back, a documented
shortcut is unreachable on a stock Mac keyboard with nothing in the product or
the documentation acknowledging it, or the field silently rewrites what was
typed.

### B-09 · Timeline pointer, wheel, trackpad, and keyboard basics

*Steps* Drag to pan · wheel-zoom with a mouse · pinch and two-finger scroll on the
trackpad, with natural scrolling on and then off · double-click · drag a time
range · use the minimap · press `Left`/`Right`, `Plus`/`Minus`, `0`, `Home`/`End`,
`F`, `J`/`K` with the timeline focused — and on a laptop keyboard with no Home
and End keys, `fn+Left` and `fn+Right`.
*Expect* Panning stays inside the session bounds with no phantom time beyond
either end. Wheel and trackpad zoom around the pointer, and the natural-scroll
setting is respected as the system configures it rather than being inverted a
second time. Momentum scrolling does not overshoot the session bounds or keep
scrolling after the gesture ends in a way the reader cannot stop. Double-click
zooms **only** — it does not also re-scope the entry list or the chip bar. A
dragged range is distinct from the viewport. `0` fits the whole session in one
key. The minimap and viewport always agree. Axis labels stay inside the plot and
never draw under the minimap; a viewport too narrow for two ticks labels its own
ends. Where a keyboard route needs `fn` on this hardware, that is recorded rather
than reported as a missing shortcut.
*Fail if* Panning escapes the session, a gesture carries a second meaning, the
natural-scroll setting is applied twice or ignored, momentum scrolling leaves the
viewport somewhere the reader did not ask for, *Fit* requires opening a drawer,
or the axis escapes its band.

### B-10 · Analysis panes, paging, and the selected-entry workflow

*Steps* Cycle the workspace panes · page the entry list with `Load 500 more` to
several pages · select entries by pointer and by keyboard · copy a message and a
raw line · paste each into TextEdit and verify with `pbpaste | shasum -a 256` ·
open the facets and templates panes · use `Alt+1`..`Alt+4` and record what those
chords produce on this keyboard layout.
*Expect* Each pane composes within budget. Paging is within budget and states how
many rows are shown, earlier, and later. Copy places exactly the selected text on
the general pasteboard, with no added or lost whitespace and no re-encoding —
`pbpaste` is the oracle, not a visual comparison in another app. Contextual
action slots are stable: the appearance of a paging control never moves *Copy
raw* so that two clicks in the same place hit different commands. Pane focus goes
where the documented chords promise, or the row records precisely what the
Option-based chords do on a Mac keyboard instead, since Option is a
character-composing modifier here and `⌥1` produces a character in most layouts.
*Fail if* A pane exceeds budget, a count is unaccounted, a copy is truncated or
re-encoded, an action slot shifts under a repeated click, or a documented focus
chord silently types a character into a field instead.

### B-11 · Host ADB discovery and a three-minute capture

*Risk* The primary live desktop source, on a platform where USB access needs no
rules but does need the user's approval, and where the product's own
permission-remedy text was written for a different operating system.
*Pre* A physical authorized device. Record the `adb` binary's absolute path,
origin, version, hash, and architecture; record whether the Mac has already
approved this device as a USB accessory.
*Steps* Open the ADB capture dialog · read the device list and let it refresh ·
select the device, buffers, and options · capture for 3 minutes while §3.4
traffic runs · read the status line throughout · then disconnect the cable and
read the list again.
*Expect* Discovery lists the device inside budget with its serial, model, and
state, and the refresh interval is visible in the list's behaviour. Capture
starts, names the transport and the buffers, and the first complete line appears
within budget of a producer flush. The status line shows a rate measured over the
last second and a heartbeat when the source goes quiet. Marker-bounded counts
reconcile with §3.4 within declared drops. A device the Mac has not yet approved,
or one in `unauthorized` or `offline` state, is surfaced as that state.
*Fail if* A present, authorized device is not listed; a device in `unauthorized`
or `offline` state is shown as ready; capture starts against a serial that is not
the selected one; counts disagree with the marker oracle without the product
declaring a gap; or any remedy text offered on this platform names a Linux
mechanism — `udev` rules, a `plugdev` group, a rules reload — none of which
exists on macOS.

### B-12 · Stop capture is answered, sticky, and complete

*Steps* Capture at least 5 minutes · press *Stop* once · do not press again ·
watch to the end · verify the session.
*Expect* Acknowledgement within budget. The control never springs back to *Stop*
and the status never returns to *Capturing*. The status leads with a visibly
advancing elapsed clock and names the stage — draining, compacting, writing the
index, reopening. When the manifest is written it says the capture is saved, the
live controls disappear, and `vcat verify` passes. No `adb` child process
outlives the capture (`pgrep -a adb`, and check its parent).
*Fail if* The status reverts, the state never resolves, a short capture fails to
finalize, or an orphan `adb` remains.

### B-13 · Follow a growing file

*Steps* Start the §3.3 producer · *Follow growing file* against it · watch the
first complete line, a partial line completed later, an idle window, and a burst ·
stop following · verify the final sequence against the producer ledger.
*Expect* The first complete record appears within budget of its flush. A partial
line is not published until it is complete, and is published once it is. After an
idle window the next line arrives promptly, and the tail the writer left behind
when it paused is published rather than held in memory until something else
arrives. *Follow* and *new data* affordances belong to the active source and
disappear when the source closes; re-engaging Follow opens a window on the live
edge rather than keeping a whole-session span. The final record sequence matches
the ledger exactly.
*Fail if* A line is lost, duplicated, or reordered against the ledger; a partial
line is published as a record; a pause leaves records stranded in memory; the
quiet status keeps claiming arrivals; or Follow survives the source.

### B-14 · Save standard and portable sessions

*Risk* macOS is where a mode promise is easiest to break, because the platform
attaches metadata to files and because several volumes a Mac mounts cannot carry
POSIX modes at all.
*Steps* Save a session normally and as a portable archive · record the
destination, file modes, ownership, extended attributes and flags, and the sizes ·
verify both with `vcat verify` · reopen both · repeat one save to an exFAT
volume and one to an SMB share.
*Expect* Each save names its destination and its completion in the notice lane.
On a POSIX volume the written files are owned by the running user, the session
directory is `700` and a portable session's embedded `raw.log` is `600`,
regardless of the account's `umask`, exactly as [`PRIVACY.md`](PRIVACY.md)
promises — a promise that exists because a portable `raw.log` is the log content
verbatim. Nothing is world-writable, nothing is setuid, and no `com.apple.quarantine`
attribute is attached to anything the product writes. Both sessions verify clean
and reopen with the same entry count and time range. The portable archive carries
its raw source; the standard save records the external source identity rather
than copying it, per [`SESSION-FORMAT.md`](SESSION-FORMAT.md). On a volume that
cannot express POSIX modes, the product either refuses with a reason or writes
and **says** that the mode promise cannot be kept there; what it must not do is
write the file and keep claiming the promise.
*Fail if* A save is silent, lands somewhere other than the stated destination,
creates world-readable session content on a POSIX volume, fails to verify, omits
data the product said it embedded, or silently writes content-bearing files to a
mode-less volume while the product's documentation still says they are
owner-only.

### B-15 · Open and round-trip a portable archive

*Steps* Open a `.vcat.zip` produced by this candidate · re-export it · compare
the manifest, counts, instants, and raw hash against the original · repeat with
an archive produced by the Windows candidate, by the Linux candidate, and by
Android, where available · repeat once with an archive created by macOS `zip`
without `-X`, so it carries `__MACOSX/._*` members.
*Expect* Counts, instants, severity totals, template identities, and raw-source
hashes survive the round trip. Entry names inside the archive use forward slashes
and are interpreted the same way on every platform. An archive carrying
AppleDouble members is either refused with a reason or accounts for those members
explicitly; they are never unpacked as session content and never mistaken for a
segment.
*Fail if* Any oracle field changes, a path separator is reinterpreted, an archive
produced on one platform cannot be opened on another, or resource-fork debris
enters a session.

### B-16 · CSV export scopes, order, encoding, and line endings

*Risk* The line-ending contract is explicitly cross-platform, and macOS is the
third platform it has to hold on.
*Steps* Export with each scope the review offers · read the promised row count
beside each one before choosing · change row order, encoding, and line endings in
the review · verify each file against the CLI oracle with a byte-exact comparison
(`cmp`, never a text diff) · count carriage returns with
`tr -dc '\r' < file | wc -c` · reopen *Appearance & timeline* afterwards.
*Expect* The review names each scope with its exact timed row count, the
displayed time zone, and the filters it applies. The completion notice states the
same number, the scope, and the file name. The file contains that many data rows
plus one header. Line endings are exactly what the review selected, the default is
a single line feed as [`CLI.md`](CLI.md) states, and the same file written on
macOS, Linux, and Windows from the same session with the same options is
byte-identical. The two stored options in the review are the two stored in
settings, and a successful export leaves settings showing what it used.
*Fail if* A promised count disagrees with the file, an explicit range silently
gains a broader fallback, the extension is doubled, the newline form is not the
selected one, the macOS default differs from the other platforms', or settings
and the review disagree after an export.

### B-17 · Startup paths, working directory, and argument dispatch

*Risk* On macOS a program can be started four materially different ways, and
three of them give it a different environment than the shell the tester is
looking at.
*Steps* Launch with `--log <file>`, with `--session <dir>`, with a bare path, with
a relative path from three different working directories, with a path containing
spaces and quotes, with a path containing a colon, with a name in NFD, with a path
that does not exist, with a directory where a file is expected, with a named pipe
and with `/dev/stdin`, and with an unknown flag · launch once through a symlink to
the binary and once through a relative path · launch once with `open ./VisualCat`
· launch once by double-clicking the executable in Finder · launch once from a
tester-written wrapper — an `.app`-shaped folder or a shell script the Finder can
run — so the process inherits the login session's environment rather than a
shell's · record `ps -o args` and the process environment for each.
*Expect* Each valid form opens exactly the intended source, regardless of the
working directory. A non-seekable source — a FIFO, `/dev/stdin` — is either
supported or refused with a reason; it must not half-import, because the store
addresses the source by byte offset. A successful open never stays on *Opening*
and never reports a cancellation as a startup error. An invalid form produces one
clear message and a usable empty state, not a crash or a hollow workspace. An
unknown flag is either ignored as documented or reported; it must not be treated
as a file name. Invoking through a symlink or a relative path still resolves the
bundled runtime beside the real binary. Every launch route reaches a usable
window; where a route cannot pass arguments at all — Finder double-click has no
argument vector — the product opens its ordinary empty state rather than
misbehaving. Record every difference in `PATH`, `LANG`, `LC_*`, and `TMPDIR`
between the terminal-started and launcher-started processes, because those
differences are what A-15 then has to survive.
*Fail if* A working directory changes which file is opened, a hostile-but-legal
name is mangled, a missing or non-seekable file crashes the app or produces a
silently partial import, launching through a symlink fails to find the runtime,
or the app works from Terminal and not from Finder.

### B-18 · Recent sessions, close, and reopen

*Steps* Create several sessions · close tabs during and after ingest · reopen from
*Recent sessions* · quit the application and relaunch.
*Expect* Sessions are listed with unambiguous source and start identity, so two
captures of the same source are distinguishable. Reopen is within budget and shows
the same counts and range. A reopened finished capture shows the whole capture,
not a stale live window or a zero-count empty view. Closing a tab during ingest
never crashes the workspace.
*Fail if* Entries are indistinguishable, a reopened session shows an empty list
under a ready status, or closing a tab throws.

### B-19 · Window state: zoom, full screen, minimize, close, and persisted size

*Risk* "Maximized" is not a macOS concept, and the product persists it.
*Pre* Record the display and its scale. Repeat on S0 and at least once on S2 and
S4.
*Steps* Resize to the minimum and beyond · press the green zoom button · enter
and leave native full screen · minimize to the Dock and restore · hide the
application and unhide it · move the window to another Space and back · close the
window with its red button, with ⌘W, and with ⌘Q · relaunch and read the restored
size.
*Expect* The window respects its 900×600 minimum. Zoom, full screen, minimize,
hide, and restore all work; a minimized or hidden window stops the expensive
redraw cadence while acquisition continues. Closing by any route shuts down
cleanly, with no late write into a disposed sink and no orphan child process, and
the process actually exits — a Mac application that keeps running with no windows
is conventional, but this product has no menu bar to bring a window back from, so
whichever it does must be deliberate and must not leave an unreachable process
holding a session lease. Width, height, and the persisted maximized state are
restored on relaunch; window **position** is not a declared persisted field, so
require reachability rather than exact coordinates. Record precisely what the
persisted "maximized" state does here — zoomed, full screen, or neither — because
the contract was written on a platform where the word means one thing.
*Fail if* The app exits non-zero or leaves an orphan on a graceful close, a
minimized or hidden window keeps redrawing, the last window closing leaves an
unreachable process holding a lease, a restored window is unreachable or smaller
than the minimum, or full screen leaves the workspace unusable.

### B-20 · Keyboard-only primary journey

*Risk* The documented keyboard contract was written for Ctrl and function keys,
and macOS is the platform where both of those assumptions meet a different
convention.
*Pre* Record whether *Keyboard navigation* (Full Keyboard Access) is on, and
whether F-keys act as standard function keys. Run the journey once with each
setting in its default state and once with both enabled.
*Steps* With the pointer unused, complete: open a log, filter by severity, search
and step to an exact match, select an entry, read its source, change a setting,
export, and close — using only the keyboard and the routes in
[`KEYBOARD.md`](KEYBOARD.md). At each documented shortcut, try the **Command**
spelling first, as a Mac user would, and then the documented **Control**
spelling, and record which one the product answers.
*Expect* Every step is reachable. Focus is always visible and never trapped; Tab
order follows the visual order; Escape precedence behaves as documented and is
inert when there is nothing to dismiss. No step requires a pointer.
Whichever modifier the product answers, the result is coherent and documented:
either ⌘ works as a Mac user expects, or ⌃ works and
[`KEYBOARD.md`](KEYBOARD.md) says so for macOS. Record the collisions explicitly,
because macOS text fields bind several Control chords to text editing — ⌃A to
start of line, ⌃E to end of line, ⌃F forward one character, ⌃K kill to end — and
those are exactly the letters this product's shortcuts use. A shortcut that edits
text instead of running its command, inside a field, is the finding.
*Fail if* Any step is pointer-only, focus disappears or is trapped, a documented
shortcut does nothing and says nothing, a shortcut collides with a system or
text-editing binding with no acknowledgement anywhere, or the product answers
neither the Command nor the Control spelling of a documented command.

### B-21 · Off-timeline and unparsed evidence stays discoverable

*Steps* Import `outcomes.txt` with an explicit `threadtime` override and
`crashy.txt` with detection · read every count the product offers · open *More →
Lines not on the timeline…* · read the source gutter codes and their legend ·
repeat the whole card five times on the same file.
*Expect* Timed, untimed, continuation, unknown, and rejected populations are all
explicitly accounted, and the totals equal the §3.1 oracle. The notice that names
lines which are not logcat records states the **finished** count, gives the same
number on every one of the five runs, waits until the source has stopped
arriving, and names a menu item that exists. The command opens the exact
source-ordered lines and does not claim that more of the file remains to be
scanned once it has listed every line the session counted. Every non-ordinary
gutter code is explained on screen and accessibly, never by tooltip alone — which
matters more here than elsewhere, because a trackpad user who never hovers will
otherwise never see it.
*Fail if* A count is invented, drifts between runs of the same file, or never
corrects itself; a notice names a command that does not exist; a population is
counted but unreachable; or a gutter code has no visible legend.

---

## 6. Tier A — advanced scenarios

Purpose: exercise deliberate second-day workflows, platform variation, recovery,
and combinations that do not belong in every smoke run.

---

### A-01 · Templates and statistics on real ADB traffic

Capture at least 10 min while exercising several Android subsystems. Templates
must be stable across reopen, ranked consistently, filterable, includable,
excludable, and copyable. Statistics totals and first and last instants must
equal the active query oracle. A process name changing for one PID must not
retain stale facet tallies. Run the same capture once on the native build and
once on the translated one and confirm the template identities are identical:
Drain's output is a product contract, not an architecture artifact.

### A-02 · High-cardinality facets and composition

Use thousands of tags, PIDs, TIDs, and process names. Scroll the ranked summary,
then open **Find…** on every browsable group and reach a value too rare to rank;
page with *Previous 100* and *Next 100*, search the list by plain text and by
number, and press include and exclude **in the middle of their targets**, not on
their glyphs. Combine one facet from each dimension with severity, regex, and a
time range. AND applies across dimensions; a group ignores its own filters, which
the pane discloses as `COUNTS · OTHER FILTERS`; counts name their population; an
active value stays individually removable even when it is rare, excluded, or now
matches nothing; no unrelated filter disappears. An active **template** reads as
its canonical message shape rather than a numeric id, in the chips and in a
Templates group that exists only while one is set. On a growing capture the
browser holds one snapshot, says which one, and offers *Refresh counts* rather
than moving the page under a trackpad's momentum scroll.

### A-03 · Saved views round trip

Save a named severity + facet + regex + range view; clear, apply, close and
reopen the session, and apply again; save Unicode names in both NFC and NFD
spellings, a very long name, and a duplicate; delete one. Every dimension and
Follow state the schema allows returns exactly; an invalid or unsupported
`view.json` is ignored without blocking the session; delete affects only the
named view. A view file created on Windows or Linux applies identically here. Two
view names that differ only in Unicode normalization are the case this platform
uniquely creates: whichever the product does — treat them as one view or as two
— it must be consistent between the list, the applied view, and the file on disk.

### A-04 · Range, viewport, filter, and export remain distinct

Create a selected time range inside a zoomed viewport over an active filter.
Exercise Zoom range, Filter range, Export range, Clear selection, and Escape. Each
changes only its promised dimension, and exported half-open boundaries match the
independent microsecond oracle; after I-02 validates it on the same corpus, the
candidate CLI result agrees too. *Export range* opens the review with that span as
a fixed summary — no scope question to answer again, and no broader fallback
beside it, including when the explicit range is empty.

### A-05 · Many independently stateful tabs

Open at least eight sessions: file, ADB, growing, standard, portable, recovered,
degraded, and failed. Give each a different filter, viewport, and selection.
Switch, reorder where supported, close the first, the middle, and the selected
one while progress occurs, and use the scrolled tab strip. State never leaks; the
selected tab and command availability track the visible session; a clipped tab
can still be reached and closed; every close is prompt and none throws. Watch the
open-descriptor count while eight memory-mapped sessions are open at once
(`lsof -p` against `ulimit -n`): macOS sets a much lower default limit than the
other two platforms, and eight large sessions is where that first becomes visible
rather than theoretical.

### A-06 · Import-preview override matrix

For each `fmt-*`, import once with detection and once with an intentional format
override; vary the assumed year, IANA time-zone identifiers (the native form on
macOS), template mining off, and Embed raw source on. Reach the review both ways:
through *Open log*, and through **Open log with options…** in *More*, which opens
it regardless of detection confidence — the two are different commands and the
plain one must import a confidently detected file directly. Editing an option
re-evaluates the sample already read — the file is not reopened or copied again —
and *Import* stays disabled until the preview catches up, so a rapid edit cannot
accept an old span. Validation rejects a blank or invalid zone and years outside
1970–9999 without closing. Enter a Windows zone identifier
(`Central Europe Standard Time`) as well and record whether the runtime accepts
it on this host; whichever it does, the review must validate rather than
accept-then-fail. An explicit override never displays a fabricated detection
confidence. The manifest records exactly the settings that produced the accepted
preview.

### A-07 · Automatic detection and mixed content

Import every format and `mixed-formats.txt`. Candidate scores and order,
warnings, outcome counts, and the selected default match the oracle, including
each part's recorded byte range. Low-confidence input remains an explicit user
decision in the preview; `outcomes.txt` is refused by detection and that refusal
names choosing a format as the way forward. No bytes disappear merely because a
line does not fit the primary format.

### A-08 · Adversarial corpus sweep

Open every §3.2 finite file, including the hostile-name, colon, newline, and
normalization rows. No crash or hang; invalid bytes, continuations, untimed,
unknown, and rejected records remain counted and reachable; 2 MiB lines are
bounded, inspectable, wrappable, and copyable; source offsets remain exact across
LF, CRLF, BOM, and no-final-newline, and a lone-CR source is refused by name
rather than read as a single record; control, NUL, and bidi content cannot alter
surrounding UI. A one-line source and an empty file each produce one coherent
result. A file carrying extended attributes and a Finder tag imports unchanged,
and the product neither reads nor writes them. A `._` AppleDouble sibling is not
mistaken for a log.

Then open the files a Mac user will genuinely try, because the product is a log
viewer on a machine full of logs that are not logcat: `/var/log/system.log`, a
`log show` dump, `/var/log/install.log`, a `.ips` crash report from
`~/Library/Logs/DiagnosticReports/`, and `~/Library/Logs/` application logs.
These are out of scope as *formats* — see §1.2 — so the requirement is the
refusal, not the parse: detection scores them too low to choose a format and says
that choosing one is the way forward, an explicit override accounts for every
line as unknown rather than fabricating records, and nothing claims a timeline it
did not derive. Several of these paths are also protected by the platform's own
privacy machinery or are root-owned, so the same step exercises the
permission-denied path on files a Mac user reaches for first — and the product
must distinguish *you may not read this* from *this is not there*, which on macOS
is the difference between a TCC denial and a typo.

### A-09 · Source mutation during finite import

Import a large file, then — on separate copies — replace it with `mv`, truncate
it in place, append to it, `unlink` it, `chmod 000` it, and `chflags uchg` it
while materialization and ingest run. The product either uses one consistent
identity snapshot or fails and reports the source change. It never builds a
hybrid, silently accepts a changed hash, corrupts the replacement, or waits
forever. **macOS-specific:** an unlinked file remains readable through the open
descriptor, so the product may legitimately complete an import of a file that no
longer has a name — but whatever it does, the session's recorded source identity
and the notice must agree with it, and a later reopen must report the source as
missing rather than silently degrading. Repeat once with the source on an iCloud
Drive path and evict it mid-import (`brctl evict`, or *Remove Download* in
Finder): a dataless file that the system is re-materializing underneath the
reader is a mutation neither truncation nor replacement, and the product must not
present a stalled fetch as a healthy import.

### A-10 · External source changed or missing on reopen

Save a non-portable session, close it, then modify, move, `chmod 000`, `chflags
uchg`, and delete the external log as separate passes; and once, revoke the
folder's privacy consent instead of touching the file at all. Reopen each time.
The identity check detects the change; degraded index-only mode is entered
explicitly and labelled; raw context says why it is unavailable and offers a
route; nothing claims bytes it cannot read. A permission-denied source, a
consent-denied source, and a missing one are three different sentences, and the
consent case is the one only macOS produces.

### A-11 · Recovered interrupted session

Interrupt an ingest with `SIGKILL`, relaunch, and recover. The partial session is
listed as interrupted, reports what reached disk, verifies as a partial, and
reopens read-only or recovered exactly as [`SESSION-FORMAT.md`](SESSION-FORMAT.md)
describes. Its completion text says *Interrupted* and accounts for the recovered
entry count rather than presenting itself as complete. Repeat once with the kill
delivered by the platform rather than by you — a jetsam termination under
`memory_pressure` — and confirm the recovery path is identical and that the next
launch says what happened rather than starting as though nothing had.

### A-12 · Session cache and retention policy

With a seeded cache, inspect *Session cache*, its computed size, and its
retention policy; run cleanup with an open session, a protected session, and a
session held by a second process. Restore and protection precede cleanup; the
preview is recomputed rather than reused; open and protected sessions survive;
the reported reclaimed size matches what the filesystem shows. Measure the
reclaim with `du -sk` before and after **and** with `df`, because on APFS they
can disagree: a clone shares blocks, a Time Machine local snapshot pins deleted
ones, and "purgeable" space is neither free nor used. A reported figure that
matches `du` and not `df` is correct and must not be filed as a discrepancy;
state which oracle the row used.

### A-13 · Appearance, timeline, and diagnostics settings

Exercise every setting in *Appearance & timeline*, including text scale, theme,
high contrast, live refresh limit, default export order, encoding, line endings,
and structured diagnostics. Labels are human language, never implementation
identifiers, and no phone-only control appears on this desktop. A changed setting
takes effect without a restart and reaches every open workspace, remeasuring
together rather than replacing the session or ending a capture. The settings
writer preserves the newest value: a coalesced workspace write cannot overwrite a
newer preference. Enabling diagnostics creates
`<diagnostics-root>/visualcat-*.jsonl` — the exact pattern the diagnostic bundle
collects — and nothing outside it, and in particular nothing in
`~/Library/Preferences`, `~/Library/Caches`, or `~/Library/Saved Application
State`, which is where a Mac application would ordinarily put such things.

### A-14 · Custom session directory, symlink, and alias refusal

Point the session root at another directory, a directory on a second APFS volume,
a directory on a mode-less volume, a symlink to a directory, a **Finder alias**
to a directory, a firmlink, and a path that does not exist. Each is accepted or
refused with a reason. The Finder alias is the macOS-only row and the interesting
one: an alias is an ordinary file carrying a bookmark, not a symlink, so a
product that only refuses reparse points will accept it as a plain file and
produce a failure with no useful sentence in it — record exactly what happens.
Prove the same boundary on the CLI: [`CLI.md`](CLI.md) states that
`index --force` refuses a filesystem root and any tree containing links or
reparse points, so plant a symlink inside an existing `.vcat` directory and
confirm the refusal rather than a recursive replace. A symlinked lease or session
root is refused rather than followed, and that refusal is a clear message rather
than an opaque I/O error. Sessions written to the new root are found, and the old
root is not silently abandoned with data in it.

Include the row that macOS itself broke once. The product's temporary-storage
root validation must accept a root reached through a symbolic link above it —
which on macOS is the ordinary case, because the standard temporary directory is
reached through `/var`, a symlink to `/private/var`. A root under `$TMPDIR` must
work; only the root itself must be a real directory, and nothing inside it may be
followed.

### A-15 · ADB locator precedence on macOS

Macs commonly have more than one `adb`, and none of them is where the product's
default probe looks. Prove the precedence the implementation actually applies: an
explicit configured path first, then `ANDROID_SDK_ROOT`, then `ANDROID_HOME`,
then `platform-tools` under the default SDK directory beneath the resolved local
application-data root, then each `PATH` entry in order. Record which binary was
actually spawned for every configuration, and run the same matrix through
`vcat adb-devices --adb <path>` so the desktop and the CLI are shown to resolve
identically.

Three things to reconcile rather than assume, all of them macOS-shaped.
[`CLI.md`](CLI.md) documents the CLI locator as `--adb`, `ANDROID_SDK_ROOT`, or
`PATH` — it names neither `ANDROID_HOME` nor the default SDK directory, so
establish which is authoritative and record the mismatch as a documentation or
behaviour finding per §2.11. The conventional Mac SDK location is
`~/Library/Android/sdk`, and the conventional Homebrew location is
`/opt/homebrew/bin/adb` on Apple silicon or `/usr/local/bin/adb` on Intel; the
product's default probe is a `Android/Sdk` directory beneath its own resolved
data root, which is neither of those. Confirm what actually happens on a Mac with
exactly the Android Studio layout and nothing else, because that is the ordinary
Mac Android developer's machine. And note the third: the probe's `Sdk` differs
from Android Studio's `sdk` **only in case**, so a default case-insensitive
volume would forgive a path a case-sensitive one would not — run the probe row on
both volumes and say which you used.

Run the whole matrix twice: once from a terminal, and once from the Finder or
wrapper launch used in B-17. A launched process inherits the login session's
environment, not a shell's, so an `adb` that a terminal finds because Homebrew's
directory is on `PATH` from `.zprofile` can be completely invisible to a
Finder-started process. Discovery that works in one and fails in the other is the
finding, and "it works in my terminal" is not evidence about a user's launcher.

With no `adb` anywhere, the message names platform-tools and SDK configuration
and offers a route; it is never an inert dialog, and it never names a Linux
mechanism. A configured path that exists but is not executable, a path that is
quarantined, a path to a binary of the wrong architecture with Rosetta absent, a
dangling symlink, and a directory where a binary is expected each fail with their
own specific reason.

### A-16 · ADB device-state and topology matrix

Cover `device`, `unauthorized`, `offline`, a wrong or absent serial, two devices
attached at once, a device that disappears mid-discovery, and — the macOS-only
state — a device that has not yet been approved as a USB accessory on an
Apple-silicon laptop, which is simply absent from `adb devices` until the user
answers a system prompt. Each state is surfaced with its own text and is never
retried as a parser failure. Pre-flight rejects a missing serial before spawning
`logcat`, so an unknown serial can never produce an indefinite empty wait that
looks like a successful capture.

Then the transport macOS gates separately. ADB over Wi-Fi finds and reaches a
device through mDNS and a local socket, and current macOS asks the user's
permission before an application may talk to devices on the local network. That
consent is attributed the same way every other one is (§2.6) — to the launching
terminal, not to VisualCat — so a tester whose terminal was granted it long ago
will never see the prompt, and a first-time user will. Run one pass from a
terminal that has not been granted it: record whether the prompt appears, what it
names, whether discovery works after approval, and what the product says when it
is denied. A denial that presents as *no devices found* sends the reader to check
their cable for a permission their Mac is withholding, which is the same failure
shape as A-10's consent-denied source and is just as wrong here.

The remedy text is the assertion this platform adds. The product learned, on
Linux, to name that platform's permission mechanism when a device is present but
unopenable. macOS has no `udev`, no rules file, and no `plugdev` group, so
whatever this build says on a Mac must be true here: either a macOS-appropriate
sentence, or a generic one, but never an instruction to edit a rules file that
does not exist on this operating system.

### A-17 · ADB buffers and format negotiation

Capture with each buffer individually and in combination, and with a device that
rejects `threadtime,year,UTC,usec`. Per-record buffer attribution is exact across
`-D` boundaries. Negotiation degrades in bounded steps, records what it settled
on in the manifest, and never lets a lost UTC modifier shift every timestamp by
the host-to-device offset.

### A-18 · ADB pre-roll, buffer history, duration, and byte limits

Exercise pre-roll, a duration cap, and a byte cap, each alone and together.
Pre-roll content is identified as pre-roll; a cap ends the capture, finalizes it,
and says which cap ended it; the manifest records the configured values.

Then exercise the option [`CLI.md`](CLI.md) deliberately keeps separate from
pre-roll: `--include-buffer-history`, which may add hundreds of thousands of
older records on a busy device. Prove it is genuinely distinct from a large
pre-roll — the two must not be conflated in the dialog, in the manifest, or in
the session's own account of where its first record came from — and that with it
off a capture starts at the live edge rather than spending minutes ingesting the
ring buffer while the present goes unshown. On a phone that has been running for
days that is the difference between a capture that starts now and one that
starts last week.

### A-19 · ADB reconnect and numeric resume cursor

Interrupt the transport mid-capture: unplug, `adb disconnect`, toggle Wi-Fi, put
the Mac to sleep and wake it with the device attached, and — on a laptop — close
and open the lid. Reconnect is bounded, uses the original serial only, resumes
from a numeric cursor rather than re-reading the whole buffer, counts reconnect
gaps distinctly from source gaps, and either continues or fails explicitly while
preserving committed data. Sleep is the macOS-weighted case: the USB bus is
powered down and re-enumerated on wake, so the transport genuinely disappears and
returns, and the product must not describe that as a parser failure.

### A-20 · Device clock and host clock or time zone differ

Set the Mac to a zone at least ±2 h from the device's, and separately set `TZ`
for the process only. The live capture is read in the device's own clock; the
newest entry sits at about *now*; Follow tracks the live edge; the session pane
names both zones. Changing `TZ` for the process alone must not silently
reinterpret stored instants on reopen.

### A-21 · Growing source truncation, rotation, removal, and writer crash

The declared policy is **stop**: the source advertises a stop rotation policy,
and a changed source must be detected and recorded in the session's defect
counters. Prove that contract, and then prove the cases that are hard on any Unix
and specific in their shape here.

Run these as separate passes: `: > file` truncation in place; truncation to a
shorter non-zero length; `mv` plus a new shorter file; `mv` plus a replacement
that is immediately **longer** than what has already been read; `rm` with no
replacement while the writer keeps appending to the unlinked inode; a real
`newsyslog` rotation against a dedicated configuration file on a dedicated host;
and a writer killed between appends. Each stops **visibly**, the notice
distinguishes *removed* from *truncated or rotated*, the session records the
source change, and everything committed before the change verifies.

Two macOS-flavoured additions. Rotate onto a **different volume** — which
`newsyslog` can be configured to do and a user can do by hand — so the inode
number and the device both change, and confirm identity comparison notices.
And run one pass where the followed file is on iCloud Drive and the system
evicts it mid-follow: the path still exists, the bytes do not, and a follow that
reports neither a stop nor an error is the finding. Record the exact timing used
in each pass, because with a short poll interval a real rotation can win the
race.

### A-22 · Growing source sharing and first-batch timing

Follow a file another process is writing, and follow one opened `O_APPEND` by two
writers. The reader must never block the writer, never make it see an error, and
never take an advisory or mandatory lock against it — verify with `lsof` that the
product holds only a read descriptor, and confirm the writer's own ledger shows
no stall. The first complete line arrives within budget of a flush. A quiet
source's last-second rate falls to zero and the heartbeat names the silence, and
the tail the writer left behind when it paused is published rather than held
until something else arrives. Repeat once with a native reader and a translated
writer, and once the other way around.

### A-23 · Concurrent capture, import, and query

Run an ADB capture, a large import, and an interactive query at once. Each
progresses; none starves; the status and notice lane attribute work correctly;
and nothing is relabelled as another operation. Sample open descriptors
throughout: three concurrent memory-mapped workloads against a low default
`ulimit -n` is the combination most likely to reach the limit first.

### A-24 · Selection and source context across live refresh

With a live capture and an entry selected, let several refreshes pass. Selection
and the timeline caret are restored by entry id every time. Source context
survives reattachment, retries when interrupted, and can read a sidecar the
capture is still writing. An inspected entry that the active filter excludes is
still admitted — entry and *Copy raw* agree, and the UI offers a way back to it.
Every read ends in bytes, an explicit interruption, or a failure offering retry;
never a permanent *Reading*. Include one pass where the window is hidden (⌘H) for
a minute and then unhidden, and one where the Mac sleeps and wakes, because both
suspend the redraw path in ways a Windows or Linux run does not exercise.

### A-25 · File-chooser cancellation and refusal paths

Cancel the chooser at every stage; dismiss it with Escape and with ⌘. ; select a
directory, a **bundle** — an `.app`, an `.rtfd`, a `.photoslibrary`, which macOS
presents as a single item but which is a directory underneath — a device node, a
FIFO, a broken symlink, a Finder alias, a file you cannot read, a dataless iCloud
file, and a file on a volume that is ejected between selection and read. Each is
refused with a reason and leaves a usable state. A cancelled open is never
reported as an error; it releases the shell immediately, so the operation card
goes, the work lease is released, and the next file command is available at once
rather than the shell sitting on *Cancelling…*. The bundle row is the one only
macOS has: whatever the product does with a directory the platform draws as a
file, its message must describe what the user actually chose.

### A-26 · Names, normalization, and case collisions

This is the macOS row with no counterpart anywhere else, and it is worth running
carefully rather than quickly.

Open and save using the hostile-name corpus: spaces, quotes, `$`, a literal
newline, a colon, NFC and NFD spellings of the same visual name, and a pair of
names differing only in case. Display, tab name, notice, export file name,
manifest, recent-sessions list, and diagnostics all carry the name the volume
actually stores, byte for byte, and reopening selects the file the user chose.

Then the two structural cases. On the **default case-insensitive, normalization-
insensitive APFS volume**, two spellings that the filesystem treats as one path
must be one session everywhere the product counts, leases, protects, and deletes
them — one tab, one lease, one cache entry, one row in *Recent*, and a deletion
that removes it once. On a **case-sensitive APFS volume**, two genuinely distinct
sessions whose paths differ only in case must never be conflated: not in the
lease directory, not in the size index, not in the protection checks, and not in
the deletion flow. Run both, on volumes you created for the purpose, and record
which mechanism decided each result. A product whose path-containment check and
whose session-path comparer disagree about case will pass one of these two rows
and fail the other, and the failure may be silent — so check the lease directory
and the recent list, not only the screen.

No name is ever passed to a child process in a way that re-splits or expands it.

### A-27 · Diagnostic bundle review

Create the bundle through the UI. The confirmation states exactly what will be
collected; the archive contains only that; it is written where the product said;
and it is readable with ordinary tools. Its entry names are safe, its modes are
not world-readable by accident, it carries no extended attributes or AppleDouble
members, and its contents satisfy P-03. Confirm it collects only
`<diagnostics-root>/visualcat-*.jsonl` and the metadata it declares, and in
particular that it does not sweep in a macOS `.ips` crash report, which would
carry log payload the confirmation never mentioned.

### A-28 · Update route and release-origin communication

With no network activity of its own, *Check for updates…* must say plainly that a
build installed from a GitHub release or built locally is not updated
automatically, and offer to open the releases page only on request. Verify that
the browser actually launches through the system's default handler, that it
launches the *user's* default browser rather than Safari unconditionally, and
that when no handler is available the product shows the URL instead of failing
silently. Confirm with P-01 that nothing was fetched before the user asked. The
command must exist on this desktop: it was gated off on every desktop head at
one point while [`SUPPORT.md`](SUPPORT.md) described it to desktop readers, and
macOS has never been checked.

### A-29 · Upgrade from the previous supported release

With D3, start the candidate from a separate extraction. Settings, sessions,
saved views, cache, and an interrupted session are read compatibly or migrated
with a recorded path; the candidate does not rewrite old data merely by listing
it; the previous release still runs afterwards, or the rollback risk is
documented explicitly.

macOS adds an upgrade path the other platforms do not have, and it is the one a
real user takes: Migration Assistant and a Time Machine restore both copy the
user's `Library` wholesale to a new Mac, which can be a different chip. Simulate
it by restoring a data root captured on one machine — ideally one architecture —
onto another, and open it. Sessions, settings, saved views, and leases must be
read compatibly, absolute paths recorded inside a session must be recognised as
stale rather than followed to whatever now sits at them, and a lease left behind
by a process on a machine that no longer exists must not lock a session out
permanently. This is also where a data root that moved between releases would do
its worst damage: a restore that lands data in a directory the candidate no
longer reads presents as a first run with an empty history beside a full folder. Where the previous release resolved its data root to a
different directory than this one does — which is exactly the risk if the
resolved root and [`PRIVACY.md`](PRIVACY.md) ever disagreed — the upgrade must
either migrate or say plainly that the old data is elsewhere; silently starting
empty beside a full directory is the failure mode to watch for.

### A-30 · Window state and display-topology restoration

Save a window size, then change resolution, scaled resolution, arrangement, and
primary display; unplug and reattach an external display; rotate one; and
relaunch in each configuration. The window is always reachable and correctly
scaled; a saved size is honoured without restoring an invalid position; dialogs
open on the active display. Include the case a Mac makes easy: close the window
while it is on an external display, detach the display, and relaunch — the window
must come back on a display that exists. Also confirm that a window left in
native full screen, or on a Space that no longer exists, comes back reachable.

### A-31 · Lock, sleep, display sleep, user switching, and log-out during work

As separate passes during a capture and during an import: lock the screen, let
the display sleep, let the system sleep and wake it, close and open a laptop lid,
switch users with fast user switching, disconnect a Screen Sharing session, and
log out. Acquisition continues where the platform allows it; resume is
immediately current; a log-out's termination drains and finalizes or fails
explicitly, never leaving a session that verifies as neither complete nor
recoverable. Record what the platform did as well as what the product did: system
sleep suspends the process entirely and the wall clock jumps, so a capture that
shows a gap across sleep is reporting the truth and must say so rather than
pretending continuity. Use `caffeinate` deliberately where a row needs the Mac
awake, ledger the energy settings you changed, and restore them.

### A-32 · Multi-instance ordinary use

Run two candidate instances under one account on distinct sources. Settings
coalesce without losing the newest value, sessions stay separate, recent lists
converge sensibly, and no instance's tab state appears in the other. Run one pass
with a native instance and a translated instance together, and confirm they agree
about the data root, the lease directory, and session identity — two
architectures of the same product resolving different paths for the same account
would be a silent split-brain.

### A-33 · Multi-instance shared-session conflict

Point two instances, and then a `vcat` process, at the same session. The lease
directory serializes them: a read lease is shared, a write is exclusive, an
attempt to delete a session another process holds is refused, and nothing is
corrupted. Record the lease files created and confirm they are removed when the
last user exits — including after a `SIGKILL`, where a stale lease must not
permanently lock a session out. Repeat once with the two instances addressing the
session through two spellings that differ only in case, on each of the two volume
types, and confirm the lease agrees with the filesystem about whether that is one
session or two.

### A-34 · Settings corruption, incompatibility, and write recovery

Supply a truncated, malformed, schema-newer, and unreadable (`chmod 000`)
`settings.json`, a `chflags uchg` locked one, and a read-only settings directory.
The product starts with defaults, says what it did, and does not destroy the
unreadable original without saying so. A failed write is reported and retried,
not silently dropped, and a locked file produces a message naming the lock rather
than an unexplained I/O error.

### A-35 · The one file operation acknowledges, progresses, and stops

Start each long file operation — materialize, standard save, portable save, CSV
export, diagnostics bundle, cache cleanup — and watch the shell's single file
operation. Nothing copies, writes, or archives behind an empty screen; the card
appears immediately naming the work, shows a stage and — only if the work lasts —
a count; progress advances; *Cancel* is a request the shell then waits on, says
`Cancelling…`, cannot be pressed twice, and stays until the writer actually
stops, then reports exactly one terminal result. While one runs, every other file
command is disabled and **names the operation holding it**; reading, searching,
inspecting another session, and stopping an unrelated live capture never are, and
a command held by a running file operation names that reason in its accessibility
tree rather than being silently disabled. A cancelled or failed operation removes
its own staged file and leaves any previously complete destination untouched.
When preparation hands its file to the import that opens it, the card goes and
the session's own progress takes over — one action, reported once — and the
workspace returns to exactly the height it had before the card appeared. The card
must also not claim to be copying while the reader is deciding: an import review
left open must not sit behind an animating *Copying file…* with no descriptor
open and nothing on disk.

### A-36 · Data root, Library, and HOME edge cases

Run with `XDG_DATA_HOME` set to an absolute path, set to a relative path, set to
a path that does not exist, set to a path that is not writable, and unset. Also
run with `HOME` pointing elsewhere and with `HOME` unset. Record, for each, the
directory the product actually used.

Then reconcile the result with the published contract.
[`PRIVACY.md`](PRIVACY.md) states that the macOS data root is
`~/Library/Application Support/VisualCat`, and that it holds `Sessions/`,
`Diagnostics/`, `SessionAccess-v1/`, and `settings.json` and nothing else. The
product resolves that root through the runtime's local-application-data folder
and applies an `XDG_DATA_HOME` override on every non-Windows platform. Whether
those two statements describe the same directory on macOS is precisely what this
row exists to find out, and there are three possible outcomes, each with its own
verdict:

- the resolved root is `~/Library/Application Support/VisualCat` and an absolute
  `XDG_DATA_HOME` moves it — consistent, and the row passes with both facts
  recorded;
- the resolved root is somewhere else, for example under `~/.local/share` — the
  product contradicts its own published privacy statement about where a user's
  log data lives, which is a documentation-or-behaviour finding and not a
  tester's note;
- the resolved root is the documented one but an **absolute** `XDG_DATA_HOME` is
  ignored with nothing said — the product's own stated principle is that ignoring
  a value is correct and ignoring it *silently* is not, and it already warns
  about a relative one, so silence about an absolute one is a finding of the same
  shape.

Whichever holds, the product creates only its four declared children beneath its
own directory, reports a specific failure when the root cannot be created, never
falls back to the working directory, to `$TMPDIR`, or to another user's tree, and
writes nothing elsewhere in `~/Library` — no `Preferences` plist, no `Caches`
entry, no `Saved Application State`, no `Containers` directory.

### A-37 · Locale, globalization mode, and the time-zone database

Run under `C`, `en_US.UTF-8`, a locale with comma decimal separators
(`cs_CZ.UTF-8`), and an RTL locale; run once with split `LC_NUMERIC`; run once
with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`; and run once with a `TZ` naming a
zone the system cannot resolve. Also change the system region and first day of
week in System Settings and confirm the product does not adopt them. The
interface stays internally consistent: ISO dates and interface-culture numbers
throughout, never the host's conventions mixed into one English surface.
Invariant globalization either works with a clear consequence or fails with a
clear message; it must not silently produce different instants for the same file.
An unresolvable zone is reported, not quietly answered with UTC — which would
move every instant in a file that carries no offset of its own.

### A-38 · App Nap, occlusion, and the energy contract

macOS actively throttles applications it believes are idle, and this product runs
long acquisitions with its window hidden. Run comparable intervals with the
window frontmost, occluded by another window, on a different Space, hidden with
⌘H, minimized to the Dock, and with the display asleep. For each, record the
acquisition rate and count, the product's own redraw and query cadence, the
process's CPU, and whether the system placed it in App Nap.

Acquisition rates and counts stay equivalent within source variance in every
state. A window the system has occluded or napped stops the expensive redraw
cadence, and restoring it is immediately current rather than showing a stale
frame or a burst of catch-up. The distinction the row exists to draw is
attribution: a fall in CPU when the window is hidden may be the product relaxing
its own cadence, or it may be the platform throttling timers underneath it, and
those have different consequences when the window comes back. Record which.

Check the power-assertion boundary in both directions with `pmset -g assertions`.
This product is not expected to take a sleep or idle assertion, so a capture is
allowed to be interrupted by the system's own sleep — which is what M6 and A-31
test. The finding would be an assertion that is taken and then outlives the
capture, leaving the Mac unable to sleep afterwards, or a product that silently
relies on one the system does not grant.

---

## 7. Tier X — complex, stress, and soak scenarios

Run X tiers only on a dedicated Mac, account, and volume, with the mutation
ledger and abort thresholds prepared. Keep workloads isolated when measuring
them. Several rows here can make a Mac unusable if they are pointed at the
startup volume or at a real home directory, and one of them can leave the machine
unable to sleep.

Before any unattended row, take a `caffeinate` assertion deliberately, record it
in the ledger, and release it afterwards — a soak that the display sleeps through
produces black screenshots and a locked session that cannot be driven, and a
`caffeinate` left running is a mutation the machine keeps.

---

### X-01 · One-million-line import and interactive analysis

Import `large.txt`; record preview, first plot, throughput, peak and settled
resources, and finalization. During ingest, repeatedly pan, zoom, search, filter,
switch panes, and inspect source. The final oracle must be exact; an untouched
viewport follows to the whole session; the first user navigation hands viewport
control to the reader for good; budgets pass. Run this five times per build as
the **A/B check for the snapshot-refresh path**: read the summary line and the
zoom readout from screenshots each time, because this race needs a real
compositor and a real overlapping import and has never reproduced headlessly.
Run the five passes natively and five more translated; a race whose timing
changes under translation is exactly the kind this check exists to catch.

### X-02 · Five-million-line and configured-limit behaviour

Import `xl.txt` with and without templates and portable raw, on a volume with
measured headroom. Record disk amplification, segment count, mapped regions, open
descriptors, time, peak `footprint`, and final compact, verify, and reopen. The
product completes within available resources or refuses before unsafe exhaustion,
with committed partial state recoverable. Watch the descriptor count against
`ulimit -n` specifically: a five-million-entry session is the most segment files
this product will ever open at once, and macOS's default limit is the lowest of
the three supported platforms.

### X-03 · Twenty-million-entry live growth

Use a controlled high-rate source until ≥20 M entries where hardware permits.
Measure snapshot cadence, statistics and facet time, UI refresh count, resource
slopes, and finalization. Per-refresh query cost must not grow linearly with
total published history; a published segment's cached contribution stays stable.

### X-04 · Interaction and input storm during ingest

While X-01 or X-03 runs, continuously resize, change panes, pan and zoom, search
and cancel, toggle filters, page, open and close dialogs, copy, switch tabs, and
switch Spaces for 20 min. Drive part of it with a scripted input tool so the rate
is reproducible, having first granted that tool's terminal the *Accessibility*
consent it needs. No UI-thread exception, lost focus, stale selection,
command-slot shift, freeze >1 s, or source data loss.

### X-05 · Four-hour ADB capture endurance

Capture controlled mixed-rate traffic for ≥4 h with fixed buffers. Sample every
15 min and interact hourly. The marker and loss oracle, gap counters, session
verification, absence of sustained resource growth, sticky stop,
query-during-finalize, and reopen must all pass. Fix the window state, display
sleep, and system sleep policy for the whole run and state it: hold the Mac awake
with `caffeinate -dimsu`, record the assertion, and restore energy settings
afterwards. A laptop on battery will throttle; run on AC and say so.

### X-06 · Overnight growing-file soak

Follow a growing file for 8–12 h with long idle windows and bursts. Sample CPU,
`footprint`, managed heap, GC counts by generation, descriptors, threads, and
file I/O. Idle cost plateaus. The read buffer is allocated once for the life of
the read, not once per poll: the regression this guards against allocated a
large-object buffer on every poll — several MiB per second while the followed
file was idle and the loop was delivering nothing at all — so watch gen2
collections and large-object-heap size specifically, not just resident memory,
which macOS's compressor can flatter. The first post-idle line is picked up on
the next poll and becomes visible within the §4.2 first-complete-line budget, and
the final record sequence is exact against the producer ledger.

### X-07 · Hidden, minimized, occluded, and napped capture efficiency

Run comparable 60-min intervals with the window visible and frontmost, hidden
with ⌘H, minimized to the Dock, fully occluded by another window, on a different
Space, in native full screen behind another full-screen app, and with the display
asleep. Acquisition rates and counts stay equivalent within source variance; a
window the system has unmapped or napped stops the expensive redraw and query
cadence; restore is immediately current. Record how each state was produced and
whether App Nap engaged, because "occluded" under this window server is a
specific state the system decides and not simply "covered", and because a drop in
CPU may be the platform's doing rather than the product's. Confirm the machine
can still sleep when the run ends: an assertion this product should never have
taken, still held after the capture, is the finding.

### X-08 · ADB ring-buffer pressure and declared loss

On a dedicated device, record the original buffer state, create a marker storm
and optionally a controlled small buffer, then capture with pre-roll and
reconnect. The product must not claim losslessness: declared drops, source gaps,
and reconnect gaps are counted distinctly; buffer attribution and the surviving
sequence are correct. Restore every per-buffer size exactly.

### X-09 · ADB server, transport, and USB gauntlet

During capture, as separate passes: kill and restart only the dedicated ADB
server; unplug and replug USB; switch the device's USB mode; revoke and
re-authorize; toggle Wi-Fi transport; sleep and wake the Mac with the device
attached; close and open a laptop lid; change USB-C ports or move through a hub
or dock; restart the device; and attach a second device. Bounded reconnect uses
the original serial only; no indefinite unknown-serial wait, device substitution,
or orphan process; partial data verifies after a terminal failure. Record the
system's own USB events in the unified log for the window
(`log show --predicate 'subsystem == "com.apple.iokit.usb"'` or the equivalent
predicate this macOS version uses) so a transport loss is never filed against
ingest. Dock and hub changes are the macOS-weighted case: a single-cable dock
re-enumerates every device on it when the display wakes.

### X-10 · Rapid start/stop and limit cycling

Repeat ≥100 ADB captures and ≥100 growing follows, alternating immediate stop,
one-line, one-second, short-duration, and byte-cap endings. Every session gets a
unique path; short captures finalize; no double-start or invisible source
appears; `adb` children, descriptors, and threads return to the baseline
envelope; Stop remains idempotent. Check for orphaned `adb` processes by parent,
not by name, since another tool on the Mac may legitimately own one.

### X-11 · Concurrent source saturation

Discover the product's actual concurrent-operation limit by starting long
imports, follows, and captures until one visibly queues. Keep active sources
producing, cancel only the queued work, then release slots in varied order.
Queued work is named *preparing*, cancellation affects only it, fairness is
reasonable, and every active source finalizes correctly.

### X-12 · Signal and process-kill phase matrix

Terminate the exact PID at preview, materialization, ingest before and after the
first snapshot, compaction, manifest replace, portable extraction publication,
standard save, portable save, CSV export, diagnostics bundle, and ADB
finalization. Run each phase twice: once with `SIGTERM` and once with `SIGKILL`.
Also send `SIGHUP` to a process started from a terminal that then closes, and
`SIGINT` from the controlling terminal, and once use **Force Quit** from the
platform's own interface. Relaunch and classify the exact residue: `SIGTERM` must
get an orderly drain or a truthful partial; `SIGKILL` may leave a recoverable
partial but never an unsafe published destination, a corrupted prior session, an
orphan `adb`, or a stale lease that locks a session out permanently. Confirm the
documented 20-second grace for an in-flight generation is honoured on `SIGTERM`
and `SIGHUP` here as [`CLI.md`](CLI.md) states for the CLI.

### X-13 · Reopen while finalizing and the view-query race

Reopen and query a session while it finalizes, and run a search as a capture
finishes. A superseded view query may not relabel a finished capture as failed,
and a completed import must redraw when what is on screen was computed from an
older snapshot generation than the tab holds. Use the structured diagnostics
`snapshotGeneration` values as the oracle.

### X-14 · Filesystem and indexing contention matrix

With Spotlight actively indexing the volume under test, Time Machine running a
backup, and an iCloud sync in flight, run saves, exports, and finalization.
Separately, have a controlled test process hold a descriptor on a new manifest or
destination for a bounded interval, and separately make the destination directory
briefly unwritable. Bounded retries tolerate transient conditions; cancellation
stays prompt; a persistent condition produces a precise failure. **Do not**
expect Windows sharing-violation semantics: on macOS an open descriptor does not
block a rename or an unlink, so the failure modes here are different and the
oracle is the published result's integrity, not an access error.

Two macOS-specific contention sources deserve their own passes. Spotlight will
index a newly written session directory within seconds, so measure whether
excluding it (`mdutil -i off` on a dedicated volume, ledgered and restored)
changes throughput — if it does materially, that is a fact worth recording in the
release notes rather than a defect. And Time Machine's local snapshots pin the
blocks of deleted sessions, so a cleanup that reports reclaimed bytes while `df`
does not move is correct behaviour that must nonetheless be understood before it
is filed.

### X-15 · Low disk, read-only volume, and quota at every publication boundary

On a dedicated sparse disk image or APFS volume, drive free space toward zero
during materialization, ingest, compaction, manifest replace, save, export, and
diagnostics; separately remount read-only mid-write; separately eject the volume
mid-write. Each boundary fails visibly with a specific reason, leaves no
half-published destination, keeps committed data recoverable, and never fills the
startup volume. Record `ENOSPC`, `EROFS`, and the ejected-volume error separately
— they are different errors with different remedies. Set the abort threshold
against the *available* figure rather than the purgeable one, and confirm after
each pass that the startup volume's free space is unchanged.

### X-16 · Memory pressure, jetsam, and the compressor

Drive the Mac into genuine memory pressure with `memory_pressure -l critical -S`
for a bounded interval while a large import runs, and separately run with swap
constrained where the host allows it. The product either completes, or slows
without corrupting data, or is terminated by the system — and if the system
terminates it, the session must be recoverable and the next launch must say what
happened. Record the jetsam event from the unified log so an external kill is
never filed as a crash, and record `footprint` rather than resident size, because
macOS's memory compressor makes RSS a poor proxy for the pressure the system is
actually measuring. Cancellation and close remain reachable while pressure is
applied; after release, resource and latency recover inside the baseline
envelope.

### X-17 · Bulk-load completion, cancellation, close, and shutdown

Load all rows of a huge filtered result; cancel midway; close the tab during the
load; quit the application during the load. The action names the platform ceiling
and the remaining rows, stops at that ceiling and says so, streams progress, and
cancels promptly. Tab and application close complete within 5 s without waiting
for all rows and without throwing.

### X-18 · Deep zoom and precision boundaries

Zoom to microsecond spans, to a single instant, and to an empty region; pan to
both ends. Pixel and data precision are clamped so one instant is never printed
as two different labels; panning is bounded to the session with no phantom time;
a nearly empty plot does not over-claim precision. Repeat at 1× and 2× backing
scale and at a non-integer scaled resolution — the precision clamp is computed in
pixels, and a Retina display has four times as many of them per logical point as
the reference machine the clamp was tuned on.

### X-19 · Paging to the end of huge filtered results

Page to the true end of a multi-million-row filtered result. Counts, keyset
paging, and the end-of-range statement stay exact; contextual action slots never
shift; no page is skipped or repeated. Drive part of the paging with trackpad
momentum scrolling, which delivers events far faster than a wheel and is the most
likely input to outrun a virtualized list.

### X-20 · High session count and cache churn

Create several hundred sessions, then exercise recent lists, the cache view, and
retention. Listing stays responsive, the computed size matches the filesystem,
retention removes exactly what it names, and the lease directory does not
accumulate stale files. Include sessions whose names differ only in case and only
in Unicode normalization, on both volume types, and confirm the count the list
shows equals the number of directories that exist.

### X-21 · Repetition leak pass

Repeat open → analyze → close 200 times over mixed sources, and repeat tab
open/close and dialog open/close 500 times. `footprint`, descriptors, mapped
regions, threads, and GC cadence return to the baseline envelope; no queued
redraw reads a disposed snapshot; the process survives every close. Allow a fixed
idle interval before the final sample, and compare rolling-window medians across
the second half of the run rather than endpoint to endpoint.

### X-22 · Multi-instance collision soak

Run two or three instances for several hours against overlapping sessions and
settings, with `vcat` operations interleaved, and with at least one instance
translated. No settings regression, session corruption, lease leak,
cross-instance state, or architecture-dependent path resolution appears; a late
diagnostic write at shutdown cannot reach a disposed sink or extend the process
lifetime.

### X-23 · Display, GPU, and session-transition gauntlet

During active work: change resolution and scaled resolution, switch the primary
display, unplug and reattach an external display, rotate an output, connect and
disconnect a dock, enter and leave native full screen, enter and leave Split
View, toggle Stage Manager, move between Spaces, lock and unlock, sleep and wake,
and connect and disconnect a Screen Sharing session. The window stays reachable
and correctly scaled; rendering recovers; no stale-scale frame persists; a
display that disappears never strands a dialog; and a capture survives each
transition or reports its interruption honestly. Screen Sharing is the
macOS-weighted case: connecting can change the effective display configuration
underneath the running app, and disconnecting changes it back.

### X-24 · Path, name, and alternate-volume soak

Run ordinary workflows for several hours with sources and destinations on a very
deep path, a name in NFD, a case-sensitive APFS volume, an exFAT volume, an SMB
share, an NFS share, an encrypted external volume, and an iCloud Drive path —
each as its own pass. Eject the external volume mid-write once, and interrupt the
network share once. Supported local paths behave exactly as on the internal
volume; unsupported storage fails honestly; a disconnected share or an ejected
volume produces a specific error and leaves the source intact; an iCloud path
never leaves a session pointing at bytes the system has evicted without saying
so.

### X-25 · Large export and diagnostics denial-of-service

Export a multi-million-row CSV and build a diagnostics bundle under low disk,
with cancellation, and with the destination removed or its volume ejected
mid-write. Each is bounded, states its progress, cancels promptly, and never
leaves a partial file presented as complete.

### X-26 · Session corruption and verifier matrix

Open and verify every §3.2 damaged session copy, one fault per copy, including
the macOS-only mode, symlink, `uchg`, `.DS_Store`, and extended-attribute faults.
Each fault is detected and named; no fault is silently repaired; a symlinked
segment is never followed outside the session root; an unreadable segment
produces a permission-specific message; a locked file is reported as locked
rather than as an I/O error; stray Finder metadata is tolerated or named
precisely; the verifier's resource use stays bounded.

### X-27 · Clock, zone, and time-database changes during live work

During a capture and an import: step the system clock forward and backward,
change the time zone, cross a DST boundary with a zone that has one, and let the
Mac sleep and wake so the wall clock jumps by the sleep duration. Monotonic
duration and progress must not break; file names and retention follow wall clock
and are accounted for; stored instants are not retroactively reinterpreted. The
sleep-resume jump is the macOS-weighted case and must be handled the same way as
a deliberate clock step.

### X-28 · Restart, log-out, and crash-recovery handoff

Interrupt with a hard power cut, with a restart, and with a log-out during
ingest, save, and capture. After restart, classify the residue and recover.
Nothing unsafe is published; committed data verifies; a partial session is
offered as recoverable; leases left behind by the killed process do not block
reopening. Record whether the system asked to reopen windows on next login, and
confirm the product neither depends on that nor is confused by it: with no bundle
and no saved-state support, the expected answer is that nothing is restored, and
the product must not have written into `~/Library/Saved Application State` to try.

### X-29 · Descriptor and mapping exhaustion

This row matters more on macOS than anywhere else, because the platform has
historically set a far smaller default open-file limit than a Linux desktop, and
a memory-mapped session store is exactly the design that finds it first. The
default moves between macOS versions, so do not carry a number in from anywhere:
record `ulimit -n`, `launchctl limit maxfiles`, and `kern.maxfilesperproc` as
this host actually reports them, and state them beside every count. Then open many large sessions and many tabs
under the **default** limit — do not raise it first — and record the descriptor
and mapping counts at each step. Repeat once with the limit lowered further on a
dedicated host.

The product either stays within the limit or fails with a message naming the
resource; it never corrupts a session, leaks descriptors on the failure path, or
crashes with an unhandled `EMFILE`. A product that works only after the tester
has raised the limit is a product that does not work on a stock Mac, and that is
the finding — not the tester's `ulimit` line.

### X-30 · Translated-execution soak

Run the `osx-x64` artifact under Rosetta 2 for a full working session: a
one-million-line import, a two-hour capture, a hundred open/close cycles, and an
export. Compare every analytical result with the native run — counts, manifests,
template identities, export bytes, session hashes — and confirm they are
identical. Compare the resource and latency profile separately and report it as
its own baseline. Confirm that native and translated instances of the same
version agree about the data root, the lease directory, session identity, and
settings, and that one can open a session the other wrote.

*Fail if* any analytical result differs between architectures, a session written
by one cannot be opened by the other, the two disagree about where the data root
is, or the translated build leaks or grows in a way the native one does not.

---

## 8. Tier U — desktop UX, UI, input, and accessibility

Run U with ordinary human interaction first, then with automation and
accessibility tools. A tree dump cannot prove that a workflow is understandable
or usable. Record the S-state and the backing scale for every row; a U result
without them is not comparable across hosts.

---

### U-01 · Window-size and responsive-command matrix

Exercise the declared minimum (900×600), 1024×768, 1280×800, 1440×900,
1680×1050, 1920×1080, 2560×1440, a 4K or 5K display, zoomed, native full screen,
and Split View halves. The command bar keeps the primary open and capture actions
inline; flexible actions fold into *More* without clipping; status, notice, and
tab strips stay reachable; plot, minimap, entries, and source stay inside their
bands; the minimap keeps a usable row in the plot column even at the shortest
supported viewport; nothing overflows horizontally. A clipped session tab can
still be brought into view and closed.

### U-02 · Backing-scale and scaled-resolution matrix

Repeat key screens at 1× and 2× backing scale and at each *looks like* scaled
resolution the display offers, recording the logical and physical bounds each
time. Text, icons, borders, hit targets, and timeline pixels scale consistently;
there is no clipped control, no subpixel gap, no hairline that disappears at one
scale and doubles at another, and no mismatch between pointer and visual. Note
where macOS is rendering above native and downsampling — a "scaled" resolution is
not the same as a different logical size, and softness from downsampling is a
platform property to record, not a layout defect.

### U-03 · Mixed-scale display crossing

With a 2× built-in display and a 1× external one, move the window fully and
straddled across the boundary; open the file chooser, import review, ADB dialog,
settings, and a confirmation before and after the move; zoom on each. Owned
dialogs appear on the active display at the correct scale; hit testing and
screenshot coordinates stay aligned; the window never jumps off-screen or becomes
unreachable; and the window re-rasterizes rather than showing a scaled-up frame
from the other display's backing store.

### U-04 · Multi-display coordinates, arrangement, and hot-plug

Place the secondary display left of or above the primary so virtual coordinates
go negative; close the window there; unplug the display; relaunch; reattach;
change the primary. The window and its dialogs remain reachable; a saved size is
honoured without restoring an invalid position; the ⌘-Tab switcher and the Dock
entry identify the candidate. Repeat with *Displays have separate Spaces* both on
and off, because that setting changes where a full-screen window and a menu bar
live.

### U-05 · Window-management matrix

Repeat U-01's key sizes, a modal dialog, a minimize and restore, a hide and
unhide, and a close under each of: ordinary windowed use, native full screen,
Split View, Stage Manager on, and Mission Control. Window controls, minimum-size
handling, modal ownership, always-on-top behaviour, keyboard close route, and
switcher identity all work or degrade visibly. Record what the product's window
identity looks like to the system: with no bundle there is no application icon to
show, so the Dock entry and the ⌘-Tab switcher will use whatever the process
provides — a generic icon is the expected consequence of the shipped format and
not a defect, but a window with no identifiable name at all is a finding.

### U-06 · Keyboard contract, modifiers, and focus order

Execute every row in [`KEYBOARD.md`](KEYBOARD.md), including the timeline
`J`/`K`/`F` keys, match wrapping, `Alt+1`..`Alt+4`, the `Ctrl` shortcuts,
`Ctrl+G`, `Alt+Home`, `Alt+End`, and Escape precedence. For each, record what the
**Command** spelling does, what the **Control** spelling does, and what the key
does inside a text field as against the workspace.

Four macOS realities have to be reconciled here rather than discovered by a user:

- **Command is the platform's command modifier.** A Mac user reaches for ⌘O, ⌘F,
  ⌘E and ⌘G first. If the product answers only Control, every one of those does
  nothing or does something else, and the documentation says nothing about it.
- **Control chords collide with text editing.** macOS text fields bind ⌃A, ⌃E,
  ⌃F, ⌃K and others to caret movement and deletion. A product shortcut on those
  letters, pressed inside a field, must not silently edit the text.
- **Option is a character-composing modifier.** ⌥1 and its neighbours produce
  characters in most layouts, so the documented `Alt+1`..`Alt+4` pane-focus chords
  may type rather than focus. Record what happens, including whether a character
  reaches a focused field.
- **Function keys are media keys by default.** `F3` is Mission Control unless the
  keyboard preference is changed, so record whether the product receives it, and
  whether the documented `N`/`Shift+N` alternative covers the same ground.

Focus order follows the documented desktop order — search, then severity, then
timeline, then the analysis panes — and no control is reachable only by pointer.
Run the whole pass once with *Keyboard navigation* off and once on, and once
under a non-US layout to confirm accelerators are bound to physical keys sensibly
rather than to characters the layout does not produce.

Cover the *Recent captures* keyboard contract in full, because it is the one
place the keyboard can destroy data: arrows, Home and End move through the list
without changing a check; Space toggles the focused row's check only when that
capture can be deleted; `Ctrl+A` checks every deletable capture and
`Ctrl+Shift+A` clears every check; Delete confirms deletion of the checked
captures; Enter or double-click opens the **highlighted** capture; and Escape
clears checks on the first press and closes the dialog on the second. On a Mac
keyboard, ⌫ is Delete and there may be no forward-delete key at all, and ⌘A is
the platform's select-all — record which keys actually reach each action.
Highlight and checks stay independent, the confirmation's initial focus and
default action is **Cancel** so Return there never deletes, and after a deletion
focus lands on the nearest surviving capture — or on the remaining action when
the last one goes. A focused button, checkbox, or text selection keeps its own
keys, and nothing in the list is claimed while a confirmation or the results view
owns the keyboard.

### U-07 · VoiceOver end-to-end pass

With VoiceOver running, complete the B-20 journey by listening. Entry rows
announce level, tag, time, and message — never a session identifier, a raw span,
or a private storage path. Insights, both stored-session lists, the notice lane,
and dialogs are all announced. The main window announces a focused control rather
than reading its entire contents as one utterance when it opens. Live updates are
announced without flooding. Record the VoiceOver and macOS versions, and the
rotor and interaction behaviour for the timeline and the entry list specifically.
Where the toolkit's macOS accessibility surface cannot express a relationship,
record that limitation explicitly with its user-facing effect rather than marking
the row N/A.

### U-08 · Accessibility tree and modal boundary

Inspect the tree with Accessibility Inspector. Every interactive control has a
role, an accessible name, and state; disabled commands say why; a sheet or dialog
is modal to assistive technology and not only to the pointer — the tree must not
allow walking past a scrim into the workspace behind it. Confirm the application
introduces itself by product name rather than by a toolkit default, and that the
search field and both splitters are named for what they do rather than for what
they are.

### U-09 · Increase contrast, product high contrast, and the system accent

Enable *Increase contrast*, and separately the product's own high-contrast mode,
then both. Selection, focus, the tab underline, list surfaces, severity colours,
and the timeline remain distinguishable and meet the §4.4 contrast floor. Then
set the system **accent colour** and **highlight colour** to something loud — a
red or an orange — and restart the product: selection, focus, and list surfaces
must come from the product palette, not from the system accent, because a red
system accent that reaches the selection highlight puts an error-looking tint
under every selected row. Also exercise *Reduce transparency* and
*Differentiate without colour*: severity must never be communicated by hue alone,
and a translucent surface that becomes opaque must not lose a border the layout
depended on.

### U-10 · Light, dark, and automatic appearance

Switch the system appearance with the app running, start cold in each, and enable
*Auto* so the appearance changes on its own during a long session. Record whether
the product follows the system preference on this platform at all, and whichever
it does, its own theme setting must work, must repaint every surface including
the minimap, source view, tab strip, and dialogs, and must need no restart. An
automatic appearance change that arrives while a capture is running must not end
the capture or blank the session.

### U-11 · Text scale and the absence of a system text size

macOS has no system-wide text-scaling factor of the kind Windows and GNOME
provide, so the product's own text scale carries the whole load here, and display
scaling is the only other lever. Vary the product's text scale across its range
and the display's scaled resolution independently and together. Both reach the
chrome and every open workspace, which remeasure together without replacing the
session or ending a capture. No text is clipped and no row floor is violated.
Record explicitly that the system offers no equivalent global setting, so the
row's coverage comes from the product's own control rather than from the platform
— a Mac user who finds the text too small has only this one place to go, which
raises the bar for how far the product's own scale must usefully reach.

### U-12 · Zoom, cursor, and colour aids

With the system Zoom magnifier on — in both full-screen and picture-in-picture
modes — with a large pointer, with pointer shake-to-locate, and with a display
colour filter or inversion applied, complete a short journey. The product remains
usable, the caret and focus stay in the magnified viewport, the magnifier follows
keyboard focus, no tooltip or dialog opens outside the magnified region, and no
information is conveyed by colour alone.

### U-13 · Reduce motion

Enable *Reduce motion*. Transitions shorten or disappear; nothing depends on an
animation to become reachable; no control ends up permanently mid-transition; and
a state change that was previously communicated by movement is still
communicated. Confirm the product honours the system setting rather than only its
own.

### U-14 · Pointer, trackpad, and Force Touch interaction

Exercise click, double-click, drag, right-click and two-finger secondary click,
middle-click where a mouse has one, wheel, horizontal wheel, two-finger scroll,
pinch, rotate, three- and four-finger swipes, and kinetic scrolling, with natural
scrolling on and off. On a Force Touch trackpad, add force click and haptic
feedback. Hit targets match their visuals; a drag that leaves the window ends
sensibly; momentum scrolling does not overshoot a bounded view or continue past
a gesture the user stopped; a swipe gesture the system claims — a three-finger
Space switch, a two-finger back swipe — is not also interpreted by the product;
and force click does not trigger an unintended command.

### U-15 · Touch Bar and external input, where hardware exists

On a Touch Bar Mac, record what the Touch Bar shows for this application: with no
bundle and no declared Touch Bar items, the expected answer is the system default
set, and that is not a defect. Confirm nothing the product owns appears there in
a broken state. With an external keyboard, a numeric keypad, and a third-party
mouse attached, confirm the keyboard contract and pointer behaviour are
unchanged. N/A requires recorded absent hardware.

### U-16 · Input methods, layouts, dead keys, and the character palette

With at least a CJK input method, a layout with dead keys, and the ABC Extended
layout's press-and-hold accent picker, type into the search field, a saved-view
name, and a file-name field: a composition, a dead-key accent, a press-and-hold
accent, an emoji from the Character Viewer, and a paste of non-ASCII text.
Candidates appear where the caret is, committed text lands once and intact, and a
composition in progress is not submitted by a shortcut or by Return. A name typed
in NFD and one typed in NFC must behave consistently with what A-26 established
about this volume. Record the input source and version; a limitation of the
toolkit's macOS input-method support is recorded explicitly, not silently passed.

### U-17 · Pasteboard behaviour

Copy a message, a raw line, a count, and a path; paste into another application
and back; verify each with `pbpaste` rather than by eye. The general pasteboard
carries exactly the copied text with no added or lost whitespace and no
re-encoding. Then the macOS-specific cases. Copy and then **quit VisualCat**
before pasting: on this platform the pasteboard is owned by the system rather
than by the process, so the content must still be there — the opposite of X11,
and a row that fails here would be a genuine regression rather than a platform
property. Record whether Universal Clipboard is enabled on this Mac and,
if it is, that a copy may reach the user's other Apple devices; that is a
platform behaviour and belongs in P-14 as a privacy note rather than as a defect.
Confirm the product writes plain text and does not claim a rich type it cannot
honour, and that it never reads the pasteboard it was not asked to read.

### U-18 · Locale, number and date culture, and RTL content

Run under a comma-decimal locale, a split `LC_NUMERIC`, and an RTL system
language, with RTL and bidi log content loaded. Dates and numbers are internally
consistent — ISO dates and interface-culture numbers throughout, never the host's
conventions mixed into one English surface. Bidi content cannot reverse the
direction of surrounding UI or move a control. Changing the system region and
first-day-of-week must not change the product's own rendering.

### U-19 · Dialog ownership, switcher, Escape, and window close

Open each dialog and sheet. Each is modal to its owner, appears over it on the
correct display, is dismissible by Escape and by the platform's own close
affordance, returns a result, and never appears in the ⌘-Tab switcher as a
separate application. Escape closes exactly one layer. The live-capture dialog in
particular must answer Escape like every other dialog, because it is shown by a
different route and was once the single place a keyboard-only reader had no way
out of. An open device list takes Escape first, to close itself.

### U-20 · Notice lane and status messaging

Trigger every durable action — copy, mute, save, export, cleanup, delete, failure
— and read the notice lane. Every meaningful action reports where the reader is
looking; a notice never moves a repeated action, so a second click in the same
place is the same command; a quiet status stops claiming arrivals; messages are
product sentences, not exception text; and no message names a mechanism from
another operating system.

### U-21 · Empty, loading, quiet, partial, degraded, and failed states

Reach each state deliberately and read it: empty workspace, loading, a quiet live
source, a partial recovered session, a degraded index-only session, a
consent-denied source, and a failed import. Each explains itself in one place
with a reason, a remedy, and viable actions; a failed import is never a hollow
workspace with inert panes; and no remedy offered on this platform describes a
mechanism macOS does not have.

### U-22 · First-run comprehension with a fresh participant

With a participant who has never used the product, and no guidance beyond what
the release page and `README.txt` provide, observe the whole first-user path:
download, verify the checksum, get past whatever macOS says about an unsigned
binary, launch, open a log, find an error burst, read one record, and export it.
Do not coach any step, and especially not the security step — how a real Mac user
copes with an unsigned, un-notarized executable and a `xattr` instruction is one
of the most valuable things this run can learn, and it cannot be learned by
someone who already knows the answer. Record where they hesitate, what they
misread, what they could not find, whether they understood what they were being
asked to trust, and whether any of them pasted a command they did not understand.
Use three independent participants for the release gate, and record each
participant's macOS and command-line familiarity separately from the product
observations.

### U-23 · Visual regression sweep

Capture the §4.4 state matrix on S0 at a fixed window size, backing scale,
appearance, accent, contrast setting, text scale, culture, and font set, and
compare with the reviewed baseline. Record the display, its scaled resolution,
its backing scale, and its colour profile with every capture; a capture from a
different scale or a different profile is legitimately a different image and is
not a regression.

### U-24 · Zoomed and long-content layout

With the largest text scale and the longest content — 2 MiB lines, very long
tags, long file names, many chips — confirm that an unselected entry row
ellipsizes near the actual available width, a selected row wraps within its
budget, and no chrome overlaps or clips. Repeat once at the minimum window size
on a 1× display, which is the tightest combination this platform offers.

### U-25 · Dynamic accessibility announcements

With VoiceOver running, start a capture, let progress advance, complete it,
trigger a failure, change filters, and step through search matches. Important
state changes are announced once at a useful priority; per-line and per-tick
progress chatter does not flood speech; arriving at a match says its position
once, when the reader stepped there, and a capture that keeps republishing the
same selection does not repeat it; focus is not stolen; focus returns to the
invoking command when an operation card disappears; and dismissing a notice stops
stale re-announcement.

### U-26 · Application identity in the Dock, switcher, and Activity Monitor

Confirm how the product identifies itself everywhere the system shows it: the
Dock while running, the ⌘-Tab switcher, Mission Control, the window's own title,
Activity Monitor, and Force Quit. Record that no `.app` bundle, no bundle
identifier, and no application icon are shipped — so a generic icon, a process
name rather than a display name, and the absence of Dock pinning or a Launch
Services registration are the expected consequences of the shipped format and
match [`SUPPORT.md`](SUPPORT.md). What would be a finding: a window with no
identifiable title at all, a name in the switcher that is a file-system path or a
temporary directory, two entries for one process, or an application that cannot
be quit or force-quit through the platform's ordinary routes.

### U-27 · Fonts and fallback

Record which families the product resolves for its monospace and proportional
roles on this Mac, then load content with CJK, emoji, combining characters, and
an RTL script. The entry list and timeline labels remain legible and monospaced
where the design requires alignment; a missing family falls back visibly rather
than breaking a column the product depends on; missing glyphs are a visible
fallback rather than a crash or a blank row; and emoji render in colour without
disturbing row height. macOS ships a fixed, rich font set, so unlike Linux the
risk here is not absence but substitution — record the resolved families so a
later visual baseline is comparable.

### U-28 · The menu bar, services, and the conventions this product does not have

A Mac user expects an application menu with About, Preferences, Hide, and Quit,
a File menu with Open, and an Edit menu with Copy. This product ships no bundle
and claims no menu bar. Record what actually appears in the menu bar while it is
frontmost, and check each item: anything present must work, and anything absent
must be reachable another way. Specifically confirm that the reader can reach
settings, copy, open, and quit without the menu bar, that ⌘Q quits and ⌘W closes
in whatever way the product defines, and that no menu item is a placeholder. The
absence of Preferences under the application menu is expected and matches
[`SUPPORT.md`](SUPPORT.md); a Preferences item that does nothing is not.

---

## 9. Tier I — CLI, cross-platform, and artifact integration

macOS is where two things meet that meet nowhere else: a second published
architecture of the same product, and a third platform for a byte-parity contract
that [`CLI.md`](CLI.md) states explicitly — "the same session and options export
byte for byte the same on Linux, macOS and Windows". Every parity row names its
independent oracle, and every one of them runs on both architectures.

---

### I-01 · Matching macOS CLI artifact identity

Verify `<cli-tar>` exactly as §2.4 verifies the desktop archive: checksum,
`SHA256SUMS` line, provenance, member safety, inventory, mode bits, extended
attributes, code signature, and hashes, for **both** `osx-arm64` and `osx-x64`.
`vcat` must extract executable, report a version equal to the desktop
candidate's, run with no system .NET, and resolve every library. `vcat --version`,
`vcat help`, and `vcat` with no arguments each behave as [`CLI.md`](CLI.md)
documents, write to the documented stream, and exit with the documented status.

Give the x64 CLI archive its own attention. Its release target declares that CI
does not execute it, so this row is the **first execution that binary has ever
had** — run it natively on an Intel Mac if one is available and under Rosetta 2
otherwise, and say which. Then walk [`CLI.md`](CLI.md) command by command and run
every option it lists and every example it prints against this binary. An option
the reference documents but the shipped `vcat` answers with `does not take` is a
Fail against the published contract, and a documented example that cannot run is
the same finding in a more visible place. The archived usage line is not a
substitute: it states one usage per command and stays silent about the options
listed under it.

### I-02 · File import desktop/CLI parity

Index each corpus with `vcat index` and import the same file in the desktop with
the same explicit options. Entry counts, outcome counts, first and last instants,
severity totals, facet tallies, template identities, and raw byte ranges are
identical. Where they differ, the corpus manifest — not either implementation —
decides which is wrong. Where the desktop preview offers an import choice the
shipped CLI has no option for, record the pair as unreachable parity and name the
missing option rather than substituting a different setting on one side.

### I-03 · Desktop save verified by CLI

Save standard and portable sessions from the desktop, then run `vcat verify`,
`vcat verify --require-raw`, `vcat info`, and `vcat stats` against them. Every
check passes and every reported figure matches what the desktop displayed. Prove
the exit-code contract in the same pass: `0` on success, `3` when verification
finds corruption, and `4` when `--require-raw` could not check the raw evidence
at all — the case a standard session whose external source has been deleted must
produce, rather than a clean exit that a script cannot distinguish from a checked
one.

### I-04 · CLI session opened and saved by desktop

Index with `vcat`, open the result in the desktop, modify nothing, save, and
verify again. The session round-trips without a link or an alias being followed
out of the session tree, and without the desktop rewriting data it only read.

### I-05 · Marker-bounded desktop/CLI ADB parity

First compare discovery: `vcat adb-devices` must print a JSON array whose items
carry `serial`, `state`, the optional `model`, `product`, and `transportId`, and
the parsed ADB `properties`, and it must agree with the desktop's device list —
including for a device in `unauthorized` or `offline` state, and including a
device the Mac has not yet approved as a USB accessory.

Then capture the same marker-bounded interval with `vcat capture-adb` and with
the desktop, against the same device. Within the marker-bounded window and the
declared drops, entry sequences agree. Two live captures are never directly equal
by total alone — use the markers.

### I-06 · Desktop and CLI reconnect semantics

Interrupt the transport during both a CLI and a desktop capture, including once
by sleeping and waking the Mac. Both bound their reconnect attempts, both resume
from a numeric cursor, both count gaps the same way, and both report the same
terminal outcome for the same interruption.

### I-07 · Export equivalence

Export the same scope from the desktop and with
`vcat export <session> <output> --type csv` using the same range, order, filters,
and line-ending choice. The files are byte-identical, including newline form,
encoding, quoting, and column order. Compare with `cmp`, and count carriage
returns with `tr -dc '\r' | wc -c` — never with a text diff that can normalize
line endings, and never with a shell-escape grep pattern (§3.1). Prove the
default is a single line feed and that `--newline crlf` produces the other one,
on this platform as on the other two.

### I-08 · Portable round trip through Android, Windows, and Linux

Exchange a `.vcat.zip` in both directions with the Android companion, the Windows
candidate, and the Linux candidate. Counts, instants, severity totals, template
identities, and raw hashes survive every hop. Archive entry names use forward
slashes throughout and are interpreted identically everywhere. Confirm that an
archive written on macOS carries no AppleDouble members, no `.DS_Store`, and no
extended attributes that the other platforms would unpack as content, and that an
archive written elsewhere opens here without acquiring any.

### I-09 · Simultaneous desktop, CLI, and Android capture

Capture from the same device with the macOS desktop, `vcat`, and the Android
companion at once, where the transport allows it. Each produces a valid session;
none corrupts another; ADB server contention is reported rather than silently
dropping a reader.

### I-10 · Startup argument dispatch parity

Compare the desktop's `--log`, `--session`, and bare-path handling with the CLI's
equivalent argument handling, including a path with spaces, a path with a
newline, a path with a colon, an NFC and an NFD spelling of one name, a missing
file, and a directory in a file position. Both surfaces resolve and reject the
same inputs the same way, and both quote correctly when they report a path back.
Confirm the CLI honours the POSIX `--` separator so a file whose name begins with
`-` has a safe spelling, and that an argument beyond the ones a command reads is
refused by name rather than ignored.

### I-11 · Cross-culture and time-zone reproducibility

Index and export the same corpus under `C`, `en_US.UTF-8`, `cs_CZ.UTF-8`, an RTL
locale, and `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, and under at least three
`TZ` values including one with DST. Machine-readable output is identical across
locales; human-readable output changes only where the contract says it may;
stored instants never change with the presenting culture. Include one run where
`TZ` names a zone the system cannot resolve and confirm it is reported rather
than answered with UTC.

### I-12 · Published archive rehearsal

Reproduce the release layout locally with
`pwsh ./tools/package.ps1 -Runtime osx-arm64,osx-x64 -Archive` **on a Mac**,
compare the inventory, mode bits, and layout with the published archives, and run
`tools/verify-package-contents.ps1` against both. Record every difference. A
locally built archive with the same version but different bytes is diagnostic
evidence only. An archive produced on Windows may differ in mode bits by design;
state that rather than filing it. Note that this row needs PowerShell 7 installed
on the Mac, which the rest of this plan does not — if it is not available, the
row is Blocked on tooling rather than N/A, and the published archives are still
verified by §2.4 and I-01.

### I-13 · Shell, pipes, exit codes, signals, and cancellation

Run every `vcat` command from **both** `zsh` and `bash`, with stdout to a pipe,
to a file, and to `/dev/null`; with stderr redirected separately; under `set -e`;
with `LC_ALL=C`; and with a closed downstream (`vcat query … | head -1`). Exit
codes match [`CLI.md`](CLI.md): `0`, `2` for invalid input, `3` for corruption or
a bounded search timeout, `4` for `verify --require-raw` that could not check the
evidence, `130` for cancellation, `1` otherwise. NDJSON output from `query` is
one entry per line and is consumable a line at a time; every other command prints
one indented document. A closed pipe terminates quietly with the documented
status rather than printing a stack trace. Mutation commands print absolute
destination paths.

Two documented contracts need their own assertions. Progress is written **only**
when standard error is connected to a terminal, so run each long command once
interactively and once with stderr redirected to a file and to `/dev/null`, and
prove that the redirected structured output is clean — no progress, no spinner,
no carriage-return repainting — while the interactive run does show progress.
And cancellation must leave completed session generations recoverable: interrupt
an `index` and a `capture-adb` at several points with Ctrl+C, with `SIGTERM`, and
with `SIGHUP`, and verify each surviving session with `vcat verify`. The
documented behaviour is that all three take the same path and that the process
waits up to 20 seconds for the in-flight generation to publish.

Beware the harness trap when scripting this: a signal sent to a background job of
a non-interactive shell may reach nothing, because such a shell sets `SIGINT` to
be ignored for its asynchronous children and the disposition survives `exec`. The
command then runs to completion and exits `0`, which looks exactly like a clean
cancellation. Use an interactive shell with job control, or reset the disposition
before `exec`, before concluding anything about signal handling.

`VISUALCAT_DEBUG=1` changes exception detail only after that explicit opt-in,
never mixes it into structured standard output, and every artifact it produces is
treated as sensitive because the extra detail can carry paths and payload.

Run one pass from the context a Mac actually automates in rather than from an
interactive shell: a `launchd` job, or a scheduled task, where there is no
terminal, no `TERM`, a minimal environment, and standard error is a file. Progress
must be absent, structured output must be clean, exit codes must be unchanged, and
the data root must resolve to the same directory it does interactively — a job
running as the same user but with a different environment resolving a different
data root would silently split that account's sessions in two.

No command requires a terminal, and none emits escape sequences into a non-TTY
stream unless documented.

### I-14 · Deterministic test-log generator and format matrix

Generate each of the five formats with a fixed seed, twice, on `osx-arm64`, on
`osx-x64`, and — where evidence exists — on Linux and Windows. Output is
byte-identical across runs, across architectures, and across platforms for the
same seed and options; the requested format is honoured exactly and never
silently falls back; every generated file is detected as its own format at full
confidence. In particular, `--format long` produces genuine framed long-format
records rather than a threadtime fallback. Success prints only the absolute
output path and exits `0`; rejected input exits `2`; a destination failure exits
`1`; and no failed or help invocation creates or truncates the requested output.
This row gates the use of generated corpora as setup for every other result.

### I-15 · Cross-platform byte parity with the Windows and Linux candidates

Using the same corpus, produce on all three platforms: an index, a portable
archive, a CSV export with identical options, and a `stats` and `templates` JSON
document. Compare each pair with `cmp`. Differences are permitted only where the
contract names them — and a newline form, a path separator inside a manifest, a
culture-formatted number, or a case-folded identifier is **not** such a place.
Verify the newline claim by counting bytes.

macOS adds one comparison the other two cannot make between themselves: a session
directory created here may carry extended attributes or a `.DS_Store` that Finder
added after the fact. Compare the session's declared contents rather than a naïve
directory diff, and record any platform metadata that appears so it can be
excluded deliberately rather than discovered as a false difference.

### I-16 · Architecture parity between the two macOS builds

The macOS-only parity row, and the one no automated gate covers. Using one
corpus, one device, and one set of options, produce on `osx-arm64` and on
`osx-x64`: an index, a portable archive, a CSV export, a `stats` and a
`templates` document, a marker-bounded ADB capture, and a desktop-saved session.
Compare every pair with `cmp` and every manifest field by value.

Analytical identity is exact: counts, outcome accounting, instants, severity
totals, facet tallies, template identities, raw hashes, and export bytes are
identical between the two architectures. A session written by one opens in the
other, verifies there, and reports the same figures. Both resolve the same data
root, the same lease directory, and the same session identity for the same
account, so one can protect a session the other is using.

*Fail if* any analytical value differs between the architectures, an export
differs by a byte, a session written by one cannot be opened or verified by the
other, the two disagree about the data root or about session identity, or the
translated build's floating-point or culture handling produces a different
rendered figure for the same stored instant.

---

## 10. Tier P — privacy, security, and negative scenarios

Use only synthetic secrets (`VCAT_SECRET_<run-id>`) and dedicated accounts and
volumes. Security tests must not weaken or attack systems outside scope, and on
this platform they must not weaken the platform's own protections: do not disable
Gatekeeper, do not disable System Integrity Protection, and do not reset privacy
consent for anything but a dedicated account.

---

### P-01 · No unsolicited network traffic

With a clean profile, capture per-process network activity while cold-launching,
importing, querying, saving, exporting, building diagnostics, and sitting idle
for 15 min. Use `nettop -p <pid>`, `lsof -i -a -p <pid>`, and a unified-log
predicate, and repeat with ADB disabled and enabled so local ADB socket and mDNS
traffic is distinguished from anything else. Expect no VisualCat-originated
telemetry, update check, or content upload. An explicit releases-page action may
launch the user's default browser; the application itself must not fetch anything
silently.

Record whether the macOS application firewall prompted for incoming connections,
and whether a local-network consent prompt appeared. An unsigned binary that asks
to accept incoming connections is a question the user will notice and should not
have to answer for a log viewer; if either prompt appears, establish what asked
for it — the product's own listener would be a finding, whereas ADB's local
daemon and its mDNS discovery are expected and belong to ADB. Distinguish the
three kinds of local traffic explicitly in the record: a loopback connection to
an ADB server, mDNS on the local network for a wireless device, and anything
leaving the machine. Only the third is a privacy question, and only the third
should be impossible.

### P-02 · Data locality and declared storage

Trace file writes for ordinary workflows with `sudo fs_usage -w -f filesys` bound
to the recorded PID, or with an equivalent. Writes stay inside the chosen
destinations, the resolved data root's four declared children, and bounded
temporary materialization roots under `$TMPDIR`. No source payload reaches the
home directory outside the data root, the extraction directory, another user's
tree, a network location, or anywhere else in `~/Library` — no `Preferences`
plist, no `Caches` entry, no `Saved Application State`, no `Containers`, no
`Group Containers`, and no `Logs` entry the product wrote itself.

This row also carries the reconciliation A-36 set up. [`PRIVACY.md`](PRIVACY.md)
publishes a specific macOS data root. Record the one the product actually uses,
and treat a difference as a finding against the published privacy statement — the
document tells a user auditing their machine where to look, and a user who looks
in the wrong place concludes that a log viewer left nothing behind when it did.

### P-03 · Diagnostic redaction

Place a synthetic secret in a log message, a source path, a search string, a
saved-view name, an ADB serial-like token, and the pasteboard. Generate the
bundle and search every entry, including compressed and binary members, with
`unzip -p` piped to `grep` and with `strings`. None appears; no hash or path
enables easy payload recovery beyond the declared sanitized metadata; the
confirmation exactly matches the contents. A redaction failure is a Blocker.

### P-04 · Portable archive traversal, link, and expansion safety

Open the §3.2 hostile archives: entries with `..`, absolute paths,
drive-qualified names, symlinks and hard links pointing outside the archive,
duplicate and case-colliding paths — and, because this platform decides case at
the volume level, **normalization**-colliding paths too — excessive count, depth,
and name length, POSIX mode bits including setuid and world-writable, FIFO and
device entries, encrypted entries, AppleDouble `__MACOSX/._*` members, a corrupt
central directory, and an expansion bomb. Each is refused before unsafe
publication; nothing appears outside the exact temporary root; no file is created
with a setuid or world-writable mode; disk and time limits hold; temporary data
is removed; the source archive is unchanged. Verify with `find -newer` that
nothing outside the root was created or modified.

Add the macOS question: a `.vcat.zip` downloaded from a browser carries
`com.apple.quarantine`, and so may its extracted contents. Confirm that what the
product unpacks into its own data root is not left quarantined, and that a
quarantined archive is not silently unreadable in a way that presents as
corruption.

### P-05 · Untrusted log rendering in the GUI

Import `controls.txt` and content with NUL, ANSI CSI and OSC sequences, bidi
overrides, zero-width characters, huge tokens, HTML and Markdown, CSV-formula-like
strings, and synthetic URLs. They render and copy as data. No control sequence
changes surrounding layout, opens a URL or launches a process, changes the
direction of the UI, or creates active content. CSV export quotes fields per
RFC 4180 and rewrites nothing, which — as [`CLI.md`](CLI.md) and
[ADR 0021](adr/0021-csv-export-fidelity.md) state explicitly — is **not** a
defence against formula interpretation: confirm the product and its documentation
say so and tell the reader to import an untrusted log's CSV as text. Test that
claim without opening the file in an unsafe spreadsheet configuration.

### P-06 · Untrusted content in a terminal

`vcat` prints log content to a terminal, and a terminal interprets escape
sequences. Run `vcat query`, `search`, `info`, `stats`, and an export to standard
output against `controls.txt` in Terminal.app and in at least one third-party
terminal, and capture the raw bytes with `script` or by redirecting to a file.
Expect that content reaching a terminal is either escaped, or documented as
passed through verbatim; either way, it must not be able to set the window title,
move the cursor arbitrarily, start a line that impersonates a shell prompt,
trigger a terminal response sequence that is injected back as input, or alter the
terminal's state after the command exits. A sequence that survives into the
terminal and changes its mode is a Blocker; one that is printed literally is
fine. Record both terminals' behaviour separately: they do not implement the same
sequence set.

### P-07 · Session verifier and parser resource bounds

Run the verifier and the parser against the corruption matrix, the 2 MiB-line
corpus, the pathological regex corpus, and the expansion bomb, under a bounded
time and a bounded address space where the shell allows it. Each terminates
within its declared bounds with a specific result; none spins, allocates without
limit, or recurses until it faults.

### P-08 · Symlink, hard link, alias, firmlink, and case boundary

Point sources, session roots, export destinations, and the lease directory at
symlinks, hard links, Finder aliases, a firmlink, a bind-style mount, and a
dangling symlink. Also place a symlink *inside* a session directory where a
segment belongs. The product resolves or refuses consistently with its documented
contract, never writes through a link to a target outside the intended root,
never follows a symlinked lease or session root, and never deletes a link target
when it means to delete a link. Verify with `stat -f '%i %N'` that the inode
written is the inode intended.

Two macOS-specific cases belong here rather than anywhere else. The temporary
directory is reached through a symlink at `/var`, so a root **above** the
validated boundary is legitimately a link and must be accepted — the product must
not refuse every capture because the platform's own layout puts a link in the
path. And the case boundary is decided by the volume rather than by the platform:
on the default case-insensitive volume two case-differing session paths are one
path and must be one session; on a case-sensitive volume they are two and must
never be conflated. Run both and record which mechanism decided each result.

### P-09 · Permission, consent, and user boundary

As an ordinary user, attempt operations against another user's home, a
root-owned directory, a `chmod 000` source, a `chmod 500` destination, a
directory without execute permission, a file with an ACL that denies the running
user, and a `chflags uchg` file. Then the macOS-only half: a directory the
account can read but the *launching application* has not been granted access to
— Desktop, Documents, Downloads, a removable volume, a network volume — with the
grant denied, and again with it revoked mid-operation.

Each fails visibly with a specific reason, never with an authorization prompt for
administrator rights, never by silently choosing another destination, and never
by partially writing. No elevation is required for any ordinary analysis path. A
consent denial reads as a consent denial and names what to do about it; the
failure this row exists to prevent is the one where a file that is present,
readable, and intact is reported as missing because the platform declined on the
user's behalf.

### P-10 · Temporary-file and atomic-publication boundary

Observe materialization, manifest replacement, save, export, and diagnostics with
`fs_usage`. Temporary files live under the declared root, are created with modes
that do not expose content, are renamed atomically into place within the same
volume, and are removed. Then attack the boundary on a dedicated host: set
`TMPDIR` to a world-writable directory, pre-create the expected temporary name as
a symlink to a file you own elsewhere, and set `TMPDIR` to a path on a different
volume so a rename cannot be atomic. The product must not follow a pre-existing
symlink, must not publish a half-written destination, and must report a
cross-volume publication honestly rather than leaving a partial file.

Record the platform's own contribution: the default `$TMPDIR` on macOS is a
per-user directory under `/var/folders` that the system creates with restrictive
permissions, so the shared-`/tmp` exposure that the Linux plan tests for does not
arise by default. That makes the deliberately hostile `TMPDIR` the important half
of this row here, because it is the only way the product's own behaviour — rather
than the platform's — is what is being measured.

### P-11 · Process creation and command-line safety

Inspect every child process the product spawns (`ps -ef`, `ps -o args`).
`adb` is invoked with an argument array, not through a shell; no file name,
serial, pattern, or log content can inject an argument or a shell metacharacter;
no secret appears in a command line visible to other users; the child inherits no
more environment than it needs; and every child is reaped rather than left as a
zombie. Confirm no child is launched through `open`, `osascript`, or a shell
wrapper, and that opening the releases page uses the system's documented URL
handling rather than a constructed command line.

### P-12 · Dynamic loader and executable-directory integrity

On an isolated host: run the candidate with a hostile `DYLD_LIBRARY_PATH` and
with `DYLD_INSERT_LIBRARIES` naming an inert, hash-recorded probe library; place
a same-named library beside the executable and in the current working directory;
run from a world-writable directory; and run from a volume mounted `noexec`.
Record what the loader does.

Expect the platform to do most of the defending, and record that it did. macOS
ignores `DYLD_*` variables for processes that carry certain protections, and
System Integrity Protection strips them across many exec boundaries; a probe that
is ignored is a **result**, not a failed test, and it should be recorded as the
platform's protection working. What must still be true of the product: it
resolves its bundled libraries from its own directory rather than from the
working directory, it is not made to load a library from a world-writable path it
did not intend, and it fails clearly on a `noexec` volume rather than partially
starting. Document the trust assumption a portable extraction directory makes,
since loading libraries relative to the executable is how a self-contained
publish works, and note what the ad-hoc signature does and does not guarantee
here — it proves the code has not changed since it was signed, and nothing about
who signed it.

### P-13 · Archive provenance and unsigned-artifact honesty

The macOS release is unsigned and un-notarized, and this row is where that is
held to account. Confirm that `README.txt`, the release notes, and
[`SUPPORT.md`](SUPPORT.md) together tell a user how to establish trust —
checksum against `SHA256SUMS`, then the provenance attestation — **before** they
are told to remove a quarantine attribute, and that nothing in the product or its
documentation claims a signature, a notarization, a publisher identity, or a
Gatekeeper guarantee it does not have. A tampered copy must fail the checksum and
the attestation; verify that by altering one byte of a throwaway copy.

Then judge the published remedy as a piece of security advice, because that is
what it is. `xattr -dr com.apple.quarantine VisualCat` asks a user to disable a
platform protection on a binary they have just downloaded. The instruction is
defensible only if the verification steps come first, are unmissable, and are
phrased so that a reader who skips them notices that they skipped something. If
the remedy is incomplete as well — Q3 against Q4 — then the user is being asked
to run an unexplained command that does not even work, which is worse than either
problem alone.

### P-14 · Pasteboard and sensitive display

Copy content containing a synthetic secret; confirm it goes only to the general
pasteboard, that it is not also written to a file, a log, or the diagnostics
bundle, and that the product never reads a pasteboard it was not asked to read.
No secret is displayed in a title, a notice, or a tooltip the user did not ask to
reveal.

Record two platform behaviours as context rather than as defects. Pasteboard
contents persist after the process exits, so a copied log line remains available
to every other application until something replaces it. And if Universal
Clipboard is enabled, a copy may be transferred to the user's other Apple
devices — a log line leaving the machine because the user pressed Copy is the
platform working as designed, but it belongs in the privacy record of a product
whose entire premise is local-first handling, and it is worth knowing whether
[`PRIVACY.md`](PRIVACY.md) should say so.

### P-15 · Crash reports, the unified log, and error redaction

Force a crash path on a dedicated host and inspect what the system captured:
`.ips` reports under `~/Library/Logs/DiagnosticReports/` and
`/Library/Logs/DiagnosticReports/`, and unified-log entries. A crash report of a
log viewer contains log content by construction, so the requirement is that the
**product** does not write payload into a system log or a report it controls, and
that any message it does emit is a product sentence without payload.

Record whether this Mac is configured to share analytics with Apple or with
developers, because a crash report being offered for submission is host policy
rather than product telemetry — and it will otherwise look exactly like the
application phoning home. Record it, do not change it on someone else's machine,
and delete collected reports per the retention policy.

### P-16 · Cache cleanup cannot escape or delete active data

With a seeded cache, run cleanup while a session is open, while one is protected,
while one is held by a second process, with a symlink planted inside the cache
root pointing outside it, with a `chflags uchg` file inside a session, and with a
`.DS_Store` present. Cleanup removes exactly what it names, never follows the
symlink out of the root, never deletes an open or protected session, classifies
a locked file rather than leaving a half-removed directory, and reports a
reclaimed size that matches the filesystem — measured with `du` and cross-checked
against `df`, understanding that local snapshots can pin the blocks (§A-12). The
wording admits that deletion is not a secure erase, which on a volume with
snapshots is more literally true here than anywhere else.

### P-16.1 · Deleting captures from Recent captures

Seed a disposable cache root with at least five captures: one recording, one open
in a tab, one held by a second VisualCat process, one whose directory contains a
`uchg`-locked file, and one idle.

*Keyboard only.* Open **Recent**, move through the list with the arrow keys,
toggle a check with Space, check everything and clear every check with the
documented chords, confirm, decline, and close — recording which physical keys
reach each action on a Mac keyboard, where ⌫ is Delete and ⌘A is the platform's
select-all. The highlight and the checks stay independent — **Open** acts on the
highlight and **Delete** on the checks — and the confirmation's initial focus and
default action is **Cancel**, so Return there never deletes.

*Protection.* The recording capture cannot be checked by any route and says why.
The capture held by the second process is refused at execution, nothing on disk
changes, and the same delete succeeds once that process exits — counted once,
with no outstanding failure left in the summary. The `uchg`-locked capture is
classified and reported rather than leaving a half-removed directory.

*Tab lifetime and Stop.* The open capture's tab closes before that capture is
removed, and only when its turn arrives. **Stop** during a multi-capture deletion
leaves later captures *and their tabs* untouched and reports them as not
attempted.

*Window lifetime.* Close the dialog while nothing is running, and again while a
deletion is in progress: the first is an ordinary dismissal, the second is
refused with the in-progress explanation rather than leaving a pending task
behind. Closing the main window during a deletion settles the operation once and
preserves the committed removals.

*Storage truth.* After every case, list the cache root: only the confirmed
captures are gone, no staging directory or ownership record survives a settled
sweep, and nothing outside the root is touched. Leftovers that cannot be
reclaimed stay visible as pending cleanup with a retry, and a staging directory
with no valid ownership record is reported as unresolved instead of being swept.

*A small dialog.* Resize the dialog toward its minimum height and back. Help is
what gets dropped, not the list; the reconciling actions join the decision row
rather than keeping a band of their own; nothing overlaps, leaves the window, or
stops responding at any size in between.

### P-17 · Multi-user isolation

With two accounts on one Mac, confirm each user's sessions, settings,
diagnostics, and leases are readable and writable only by that user — check with
`stat -f '%Sp %Su'` across the tree — that one user cannot see another's list, and
that a session on a shared path is not silently readable by the other account.
Repeat once with fast user switching, so both accounts are logged in at the same
time and both may have a VisualCat process running — the lease directory is
per-user, so two users working on a shared volume is the case where an assumption
about exclusivity would show.

### P-18 · Removal and residue accounting

Delete the extracted directory and account for exactly what remains: the data
root, any temporary files, and any system-level state. Then delete the data root
and account for what that removes. Document that removing the program directory
is not removing user data, and that neither is a secure erase. Confirm the
product created no `launchd` agent, no login item, no `.app` bundle, no Launch
Services registration, no file association, no `~/Library/Preferences` entry, no
`~/Library/Saved Application State` entry, no `~/Library/Containers` or
`~/Library/Group Containers` directory, no keychain item, and no shell-profile
modification. The keychain is worth checking explicitly rather than assuming:
the Android companion does keep an encrypted ADB identity in platform-protected
storage, so a reader who knows that will reasonably ask whether the desktop does
anything equivalent here. Search for one by name and record that there is none.

Then the macOS-specific residue this plan exists to find. The .NET runtime
creates one diagnostics IPC socket per process. The product sweeps stale ones and
unlinks its own **on Linux only** — the sweep is explicitly gated to that
platform — while [`PRIVACY.md`](PRIVACY.md) describes the socket as a Linux
phenomenon and then states that there is *nothing else* left outside the data
root. On macOS the runtime creates the same socket in the account's private
`$TMPDIR`. Run the product twenty times, kill some of those runs, and count what
remains:

```shell
ls -la "$TMPDIR" | grep -i 'dotnet-diagnostic' | wc -l
```

If sockets accumulate, the residue is real and undocumented for this platform,
and the finding is the gap between the two — either the sweep should cover macOS,
or `PRIVACY.md`'s account of what is left behind should. Either way it is a
one-line-scope question that only a live macOS run can answer, and the answer
belongs in the release record. Record the count, the directory, whether the
sockets are zero-byte, and whether anything else the runtime or the platform
created for this process is left behind with them.

### P-19 · ADB authority and shared-server boundary

Confirm that the product uses an existing ADB server rather than commandeering
it, that it does not kill a server it did not start, that it never reads or
copies the user's ADB private key, and that its device commands are
serial-qualified. Record how this Mac grants USB device access — which is the
system's own accessory approval rather than a rules file — and confirm the
product does not attempt to modify it, does not ask for administrator rights to
do so, and does not offer an instruction belonging to another operating system.

### P-20 · Export, path, and error disclosure

Trigger failures with long, hostile, colon-bearing, and NFD paths. Messages name
what the user needs without dumping an internal stack, a framework resource key,
an absolute path the user did not supply, or another user's path. A path reported
back is quoted so it can be copied and used, and a path the user typed in one
normalization is reported back in a form that resolves to the same file.

### P-21 · Gatekeeper, quarantine, and consent honesty

Walk the Q-states in §2.6 end to end and judge the whole trust story as a user
experiences it. Record, for each state, exactly what macOS said, exactly what the
product said, and exactly what the documentation told the user to do.

The assertions: no message from the product claims a signature, notarization, or
publisher identity it does not have; a Gatekeeper refusal is not presented by the
product as a product error, nor a product error as a Gatekeeper refusal; the
documented remedy works as written or the documentation is wrong; a privacy
denial is reported as a denial naming the folder rather than as a missing file;
and nothing in the product's own text encourages the user to disable a platform
protection more broadly than the one attribute the release notes name. Disabling
Gatekeeper system-wide is never suggested, by the product or by this plan.

### P-22 · File modes, flags, and ownership of created data

Inspect the modes, flags, extended attributes, and ownership of everything the
product creates — the data root, session directories, segment files,
`settings.json`, diagnostics, leases, temporary files, exports, and portable
archives — under `umask 022` and under `umask 077`, with `ls -le@` and
`stat -f '%Sp %Su:%Sg %N'`. Nothing is world-writable, nothing is setuid or
setgid, nothing is owned by another user, no inherited ACL widens access beyond
what the mode implies, and a session directory is `700` with a portable
`raw.log` at `600` regardless of `umask`, exactly as [`PRIVACY.md`](PRIVACY.md)
promises. Nothing the product writes carries a quarantine attribute or an
unexplained extended attribute of its own.

Then repeat on a volume that cannot express POSIX modes at all — exFAT, FAT, or
an SMB share mounted without POSIX semantics. The promise cannot be kept there,
and the product must either refuse or say so; writing content-bearing files to
such a volume while the documentation still describes them as owner-only is the
finding.

### P-23 · Spotlight, Time Machine, and iCloud exposure of session content

This row exists because macOS copies and indexes the user's `Library` by default,
and the product's data root lives inside it. None of what follows is a defect on
its own; all of it is the platform behaving as designed. The question is whether
the product's privacy account is complete on a platform that does this.

Establish four facts and record them:

- **Spotlight.** Determine whether the session root is indexed
  (`mdutil -s`, and `mdfind` for a synthetic secret placed in a log message).
  If it is, log content a user imported is searchable from the system's own
  search interface and appears in its results, which is not something
  [`PRIVACY.md`](PRIVACY.md) currently tells them.
- **Time Machine.** Determine whether the data root is included in a default
  backup and whether it is excluded by any system default. If it is included, a
  portable session's embedded `raw.log` is copied verbatim to the backup
  destination, which may be a network volume.
- **Local snapshots.** Confirm that deleting a session does not remove its blocks
  while a local snapshot references them, and that the product's wording about
  deletion not being a secure erase covers this.
- **iCloud.** Confirm the data root is not inside a synced location by default,
  and confirm what happens if a user *points the session directory* at one —
  Desktop, Documents, or iCloud Drive — which the product allows. A session
  written there is uploaded, and the product should either say so or have nothing
  to say because it refused.

*Expect* The product's behaviour is defensible on each point and its documented
privacy account either covers these platform behaviours or is identified as
needing to. *Fail if* the product actively makes any of them worse — by placing
its data somewhere more exposed than the documented root, by encouraging a synced
destination, or by claiming a locality guarantee that the platform's own defaults
contradict.

---

## 11. Tier R — regression pack for released and current fixes

These guards derive from [`CHANGELOG.md`](../CHANGELOG.md) and from current
source comments. Re-derive the table whenever a release adds a macOS-visible fix.

The **Origin** column says something different here than in the other plans,
because macOS has never had a live run. A version number names the changelog
section that records the fix, and the guard is a re-check on a platform it has
not been re-checked on. `Unreleased` means it must pass before the next tag.
**`New on macOS`** means the row has no changelog entry at all: it is a
platform-specific contract that this plan asserts for the first time, and its
first execution is the first evidence that exists either way. A `New on macOS`
row that fails is a finding, not a regression — file it as one.

Rows **R-01 to R-18** are the macOS-specific ones. They are the rows a macOS run
must never skip, because no other platform's run can reach them and no automated
gate covers any of them.

| ID | Guard | Procedure | Pass condition | Origin |
|---|---|---|---|---|
| **R-01** | An Apple-silicon build executes at all | B-02 | Every shipped Mach-O carries a valid signature, ad-hoc is expected, and `codesign --verify --strict` passes on the apphost and on every bundled library; no launch ends in `Killed: 9` | New on macOS |
| **R-02** | The x64 artifact runs | B-02, I-01, I-16, X-30 | `osx-x64` launches natively on an Intel Mac and under Rosetta 2 on Apple silicon, or refuses with a message naming Rosetta; CI has never executed it, so this is its first evidence | New on macOS |
| **R-03** | The two architectures agree exactly | I-16, X-30 | Counts, manifests, template identities, export bytes and session hashes are identical; a session written by one opens and verifies in the other; both resolve the same data root and lease directory | New on macOS |
| **R-04** | The documented quarantine remedy works as written | Q3 against Q4 in B-01 | `xattr -dr com.apple.quarantine VisualCat` alone is sufficient to launch, or the documentation names what else is needed | New on macOS |
| **R-05** | Trust instructions verify before they disable | P-13, U-22 | Checksum and provenance come first and unmissably; nothing claims a signature, notarization or publisher identity the release does not have; no advice disables Gatekeeper more broadly than the one attribute | New on macOS |
| **R-06** | The data root is where the privacy statement says | A-36, P-02 | The resolved root matches [`PRIVACY.md`](PRIVACY.md), or the difference is filed; only the four declared children are created; nothing else in `~/Library` is written | New on macOS |
| **R-07** | An ignored `XDG_DATA_HOME` is not ignored silently | A-36 | Whatever the platform does with an absolute value, the product's behaviour and its own stated principle agree — it already warns about a relative one | New on macOS |
| **R-08** | Runtime residue is accounted for on this platform too | P-18 | Diagnostic sockets in `$TMPDIR` do not accumulate across twenty runs, or the residue is documented for macOS; the sweep is currently gated to Linux while the privacy account says nothing else is left behind | New on macOS |
| **R-09** | Temporary storage reached through a link still works | A-14, P-08 | A capture root under `$TMPDIR` is accepted although `/var` is a symlink; only the root itself must be a real directory and nothing inside it is followed. This is the defect macOS itself found once: every capture refused with *Temporary storage is unavailable* | 2.0.13 |
| **R-10** | Case and normalization identity is coherent | A-26, P-08, X-20, A-33 | On a case-insensitive volume two spellings are one session in the tabs, leases, cache, recent list and deletion; on a case-sensitive volume two sessions are never conflated; the containment check and the path comparer agree | New on macOS |
| **R-11** | The keyboard contract is honest on a Mac keyboard | B-20, U-06 | Whichever modifier the product answers, the build and [`KEYBOARD.md`](KEYBOARD.md) agree; a Control chord does not silently edit text inside a field; `F3` is reachable or its alternative is documented; Option chords do not type characters instead of focusing panes | New on macOS |
| **R-12** | A failure message names this platform's problem | B-03, S7 | A startup or dependency failure on a Mac does not name `DISPLAY`, `WAYLAND_DISPLAY`, X11, or Debian and Fedora package lists; it names what is actually wrong here | New on macOS |
| **R-13** | An ADB remedy names this platform's mechanism | B-11, A-15, A-16, P-19 | No message on macOS instructs the reader to edit `udev` rules, join `plugdev`, or reload a rules file; an unopenable or unapproved device is explained in terms macOS has | New on macOS |
| **R-14** | Hidden and napped capture keeps acquiring | A-38, X-07 | Acquisition continues and counts match across visible, hidden, minimized, occluded, other-Space and display-asleep intervals; redraw cadence relaxes; App Nap involvement is recorded rather than mistaken for the product's own doing | New on macOS |
| **R-15** | The product works at the stock descriptor limit | X-29, A-05, A-23, X-02 | Many large sessions open without raising `ulimit -n`; at the limit the product names the resource rather than crashing with `EMFILE`; working only after the tester raises the limit is the failure | New on macOS |
| **R-16** | A copy survives quitting the application | U-17 | The pasteboard still holds the copied text after the process exits, which is this platform's contract and the opposite of X11's | New on macOS |
| **R-17** | The product owns its accent on this platform | U-09, U-10 | A loud system accent and highlight colour do not reach selection, focus, tab underline or list surfaces; severity encoding survives *Increase contrast*, *Reduce transparency* and *Differentiate without colour* | New on macOS |
| **R-18** | The mode promise is kept or withdrawn honestly | B-14, P-22 | Session directories are `700` and portable `raw.log` is `600` on every POSIX volume regardless of `umask`; on a mode-less volume the product refuses or says the promise cannot be kept there, rather than writing and still claiming it | New on macOS |
| **R-19** | A large import finishes showing the whole log | X-01, five `--log` launches per build per architecture | The plot, severity totals, axis, entry list, templates and every derived counter show the **full** session, not a prefix, with no need to press *Fit*; the span beside the zoom controls agrees with the span the plot drew | Unreleased |
| **R-20** | A rejected view query runs again | X-13 | A query whose answers were rejected because the session grew under it is retried rather than dropped, and a completed import redraws when what is on screen came from an older snapshot generation than the tab holds | Unreleased |
| **R-21** | A long import keeps up with itself | X-01, A-35 | A progress refresh arriving while another runs is coalesced into one further pass, not dropped | Unreleased |
| **R-22** | The not-logcat notice states the finished count | B-21 with `outcomes.txt` and `crashy.txt`, five times | The same file reports the same number every run, the number equals the chip and the summary line, and the notice waits until the source has stopped arriving | Unreleased |
| **R-23** | Notices name commands that exist | B-21 | The notice and the menu item come from one name; *Lines not on the timeline…* is what both say | Unreleased |
| **R-24** | Lines not on the timeline stops over-claiming | B-21 | It does not report that more of the file remains to be scanned once it has listed every line the session counted | Unreleased |
| **R-25** | `vcat query` emits NDJSON | I-13 | One entry per line, consumable a line at a time; every other command still prints one indented document | Unreleased |
| **R-26** | Stop is answered and sticky | B-12, X-05 | Button and status never return to *Capturing*; the ending resolves and verifies | Unreleased |
| **R-27** | Scanner and contention are tolerated | X-14 | A bounded transient condition succeeds; cancellation and a persistent failure are truthful; Spotlight and Time Machine are the contenders here | 2.0.9 |
| **R-28** | Idle growing follow does not churn | X-06 | One reusable read buffer; no sustained multi-MiB-per-second idle allocation or gen2 cadence | 2.0.9 |
| **R-29** | Closing during a bulk load is prompt | X-17 | Tab and application close within 5 s without waiting for all rows and without throwing | 2.0.9 |
| **R-30** | Live statistics and facets do not rescan history | X-03 | Per-refresh cost plateaus with cached published segments | 2.0.9 |
| **R-31** | The diagnostic logger is safe at shutdown | B-19, X-22 | A late failure cannot write into a disposed sink or extend the process lifetime | 2.0.9 |
| **R-32** | The displayed version tracks the artifact | B-01, B-04 | UI, archive name, `README.txt` and release agree; a non-release build says `-dev` | 2.0.4 |
| **R-33** | Capture and session names distinguish runs | B-18 | Tabs, *Recent*, the cache view and file names carry unambiguous source and start identity | 2.0.4 |
| **R-34** | Settings labels are human language | A-13 | No implementation identifiers are exposed, and no phone-only control appears on this desktop | 2.0.4 |
| **R-35** | Actions report in the notice lane | B-14, B-16, A-35 | Every durable or meaningful action reports where the reader is looking | 2.0.4 |
| **R-36** | The empty state is useful | B-04, B-18 | Correct desktop actions and recent sessions; no inert session command | 2.0.4 |
| **R-37** | Fit is directly reachable and exact | B-09 | One action or key fits; no drawer dependency and no geometry jump | 2.0.4 |
| **R-38** | A failed import is not a hollow workspace | A-08, A-25 | One reason and remedy with viable actions; no inert panes | 2.0.4 |
| **R-39** | An import ends fitted until the user navigates | X-01 | An untouched viewport follows the whole import; the first navigation takes ownership for good | 2.0.4 |
| **R-40** | Closing a tab cannot crash the plot | A-05, X-21 | No queued redraw reads a disposed snapshot; the process survives | 2.0.4 |
| **R-41** | Source context always resolves | B-06, A-24 | Bytes, an explicit interruption, or a retryable failure — never a permanent *Reading* | 2.0.4 |
| **R-42** | Double-click zoom has one meaning | B-09 | Zoom only; no cell filter and no list rescope | 2.0.4 |
| **R-43** | The axis remains a scale inside the plot | U-01, X-18 | Labels never overlap the minimap; a narrow view labels its endpoints; the clamp holds at 1× and 2× backing scale | 2.0.4 |
| **R-44** | Contextual actions keep their slots | B-10, X-19 | Paging and load controls never move *Copy raw* or *Entry* between clicks | 2.0.4 |
| **R-45** | Counts name their population | A-02 | Session, filter, viewport and off-timeline scopes are visible on screen, not tooltip-only — which matters more where a trackpad user may never hover | 2.0.4 |
| **R-46** | Culture is internally consistent | U-18, I-11 | Dates and numbers never mix host conventions into one English surface, and the system region does not change the product's rendering | 2.0.4 |
| **R-47** | A nearly empty plot does not over-claim | X-18 | Pixel and data precision are clamped; one instant is never printed as two labels | 2.0.4 |
| **R-48** | Live refresh preserves the selected entry | A-24 | Entry and caret restored by id; source remains reachable across hide, sleep and reattachment | 2.0.3 |
| **R-49** | The full message is reachable | B-06 | The selected row and the inspector show the whole message; a clipped cell offers it too | 2.0.3 |
| **R-50** | Short captures finalize | X-10 | Immediate, one-line and one-second captures all produce valid final manifests | 2.0.4 |
| **R-51** | A quiet status stops claiming arrivals | A-22 | The last-second rate falls to zero, a heartbeat names the silence, and the tail the writer left behind is published rather than held | 2.0.4 |
| **R-52** | Follow belongs only to an active source | B-13 | Follow and new-data affordances disappear when the source closes; re-engaging opens a live-edge span | 2.0.4 |
| **R-53** | ADB time zone follows the negotiated format | A-17, A-20 | A degraded UTC modifier cannot shift every timestamp by the host-to-device offset | 2.0.4 |
| **R-54** | A wrong or unknown serial cannot hang | A-16, X-09 | Pre-flight rejects a missing serial before spawning `logcat` | 2.0.5 |
| **R-55** | ADB buffer attribution is per record | A-17 | Buffer boundaries yield an exact per-record facet | Unreleased |
| **R-56** | Startup restore and cancel are not failures | B-17 | A successful open never stays *Opening*, and a cancellation is not shown as a startup error | 2.0.5 |
| **R-57** | Source line and continuation offsets are exact | A-08 | The gutter starts at 1; the selected line and its following context stay visible | 2.0.5 |
| **R-58** | A screen reader hears entries, not dumps | U-07 | Level, tag, time and message only; no identifier, raw span or private path | 2.0.4 |
| **R-59** | A modal is modal to accessibility, not only to the pointer | U-08 | Assistive technology cannot walk past a scrim into the workspace behind it | Unreleased |
| **R-60** | The product introduces itself by name | U-08 | It appears on the accessibility surface as VisualCat rather than as a toolkit default, and the search field and both splitters are named for what they do | Unreleased |
| **R-61** | A window with no focused control is not read out whole | U-07 | Opening the main window announces a focused command rather than the notice, the strapline, every count and both lists as one utterance | Unreleased |
| **R-62** | Session commands disable honestly | B-04, U-08 | Save, export and line commands are unavailable without an applicable session and say why | 2.0.4 |
| **R-63** | A theme change repaints the whole product | U-10 | No stale command, tab, list, minimap, source or dialog variant; no restart needed; an automatic appearance change mid-capture does not end it | 2.0.4 |
| **R-64** | A notice cannot move a repeated action | U-20 | A second click in the same place is the same action | 2.0.4 |
| **R-65** | An entry row uses the available width | U-24 | An unselected row ellipsizes near the actual width; a selected row wraps within budget | 2.0.4 |
| **R-66** | A filtered-out inspected entry is admitted | A-24 | Entry and *Copy raw* agree, and the UI offers a way back to it | Unreleased |
| **R-67** | Settings writes preserve the newest value | A-13, A-32 | A coalesced workspace write cannot overwrite a newer preference | 2.0.9 |
| **R-68** | Manifest replacement survives readers | X-13, X-14 | A concurrent reader or indexer cannot cause a publication failure or discard an ingest | 2.0.4 / 2.0.9 |
| **R-69** | Cache cleanup protects open sessions | A-12, P-16 | Restore and protection precede cleanup; the preview is recomputed | 2.0.5 |
| **R-70** | A regex error is a product sentence | B-08 | A trimmed Release build never exposes a resource key or a framework dump, and a corrected pattern stops reading its old rejection back | 2.0.5 |
| **R-71** | A missing ADB message is actionable | A-15 | It names platform-tools and SDK configuration; never an inert dialog | 2.0.0 |
| **R-72** | Off-timeline evidence is discoverable | B-21 | Timed, untimed and unparsed populations are explicit; the chip and command open the exact source-ordered lines | Unreleased |
| **R-73** | Source gutter codes have a visible legend | B-21, U-07 | Every non-ordinary code is explained on screen and accessibly, never tooltip-only | Unreleased |
| **R-74** | Text scale reaches the active session | A-13, U-11 | Chrome and every open workspace remeasure together without replacing the session or ending a capture | Unreleased |
| **R-75** | A clipped session tab remains closable | A-05, U-01 | The first action brings the tab into view; its close action then works and is never silently disabled | Unreleased |
| **R-76** | Search reaches first, last and numbered matches | B-08 | Direct controls land on the exact oracle identity without thousands of steps or zoom drift | Unreleased |
| **R-77** | The bulk row load is explicit, bounded and cancellable | X-17, X-19 | It names the ceiling and the remaining rows, stops at that ceiling and says so, streams progress, and cancels promptly | Unreleased |
| **R-78** | Export can ignore the active filter honestly | B-16, I-07 | An everything-in-session scope appears when it is distinct and contains the exact unfiltered row set | Unreleased |
| **R-79** | The manual update route matches the install origin | A-28, P-01 | The command is present on this desktop, never checks silently, says that a GitHub or self-built install is not updated automatically, and opens the official page in the user's own default browser only on request | 2.0.9 / Unreleased |
| **R-80** | The generator honours the requested format | I-14 | All five formats are deterministic and detected exactly, identical across both architectures; `long` never falls back to `threadtime` | Unreleased |
| **R-81** | Search navigation reaches every match exactly | B-08 | The counter's total is every match in the session, each step selects one record, and *Fit* still moves | Unreleased |
| **R-82** | Every applied filter value is individually removable | A-02 | A rare, excluded or now-empty value keeps its own undo, and **Find…** reaches values ranking cannot | Unreleased |
| **R-83** | The export writes the scope it offered | B-16, A-04 | Intersection, cell and explicit range each export themselves from one frozen snapshot, and the promised count is the count written | Unreleased |
| **R-84** | File work is visible and stoppable | A-35 | One named operation with progress and a *Cancel* that waits for the writer's actual result; the card does not claim to be copying while the reader is deciding | Unreleased |
| **R-85** | Lease storage refuses a symlinked root | A-14, P-08 | A symlinked lease or session root is refused with a clear message rather than followed — while a link *above* the boundary, which macOS itself puts there, is accepted | 2.0.5 / 2.0.13 |
| **R-86** | Text output is byte-identical on every platform | I-07, I-15 | Every text export and every JSON result uses a single line feed by default here as elsewhere, with `crlf` available on request | Unreleased |
| **R-87** | Escape closes every dialog, including the capture dialog | U-19 | The live-capture dialog answers Escape like the rest; an open device list takes it first | Unreleased |
| **R-88** | A cancelled chooser releases the shell | A-25 | Cancel dismisses the chooser and frees every file command at once, rather than sitting on *Cancelling…*; a chooser that never appears is reported | Unreleased |
| **R-89** | *Open log* and *Open log with options…* are different commands | A-06 | The plain command imports a confidently detected file directly; the other always opens the review | Unreleased |
| **R-90** | A session you may not read is not reported as missing | A-10, P-09 | A permission denial, a consent denial and an absent file are three different sentences | Unreleased |
| **R-91** | A carriage-return-framed log is refused by name | A-08 | Such a file is not imported as one enormous record with everything else buried inside its message | Unreleased |
| **R-92** | An unattributable lease marker is reclaimed | A-33, X-20 | A marker that never recorded its session, older than the safety window and held by no process, is swept | Unreleased |
| **R-93** | A portable archive extracts only what a session is made of | P-04 | A declared bomb, a deep path and an over-long name are refused rather than written into the product's own data directory | Unreleased |
| **R-94** | An unresolvable time zone is reported, not guessed | A-37, I-11 | A zone the system cannot resolve is named rather than quietly answered with UTC | Unreleased |
| **R-95** | The export review states its range in the displayed zone | B-16 | It does not publish UTC beside a plot labelled with another zone | Unreleased |

---

## 12. Execution schedules

| Schedule | Role | When | Contents | Planning time |
|---|---|---|---|---|
| **Artifact smoke** | Cumulative entry gate | Every macOS candidate tarball, per architecture | B-01–B-04, B-05–B-10, B-16, B-17, B-19, B-21; I-01, I-12, I-14 | 3–4 attended h per architecture |
| **Trust smoke** | Focused reusable slice | Every packaging, signing, or release-notes change | B-01, B-02, Q0–Q5 in §2.6, P-13, P-21, R-01–R-05 | 2–3 attended h |
| **ADB smoke** | Focused reusable slice | Every capture or ADB change | B-11, B-12; A-15–A-20; R-13, R-26, R-50, R-53–R-55, R-71 | 3–5 attended h |
| **Standard** | Cumulative sharing gate | Before a candidate is shared | All B; applicable R except the endurance rows; U-01, U-02, U-06, U-10, U-17, U-20, U-21, U-26; X-01 | 1.5–2.5 person-days |
| **Upgrade** | Conditional supplement | Whenever settings, session, schema, storage, runtime, or packaging compatibility changes | A-03, A-10–A-14, A-29, A-30, A-32–A-34, A-36; I-02–I-04 on migrated data | 1 person-day plus setup |
| **Full macOS** | Cumulative release core | Before a release tag | Every applicable B/A/U/I/P/R; X-01, X-02, X-04, X-10–X-19, X-24–X-27, X-29; the exact assets are authoritative, and both architectures are covered | 6–9 person-days plus unattended runs |
| **Soak** | Mandatory release supplement | Before a release tag, on a dedicated Mac | X-03, X-05–X-09, X-20–X-23, X-28, X-30; no overlapping measurement workloads | 30–50 elapsed h, 8–12 attended h and review |
| **Display** | Focused reusable slice | Any rendering, layout, window, or input change | S0–S7 across U-01–U-05, U-10, U-14, U-19, U-26; X-23; A-30, A-38 | 1–2 person-days |
| **Accessibility** | Focused reusable slice | Before release and after presentation changes | U-01–U-28, including three independent fresh-participant sessions for U-22 | 1–2 person-days plus participant sessions |
| **Security/storage** | Focused reusable slice | Before release and after archive, path, cache, permission, or trust changes | M3, M4, M7, M8, M9; Q0–Q7; X-12, X-14–X-16, X-24–X-26, X-29; all P | 2–4 person-days on an isolated Mac |
| **Parity** | Focused reusable slice | Parser, store, export, session, CLI, or ADB changes | All I plus A-06–A-10, A-17–A-20, A-37, X-26 | 1–2 person-days |
| **Architecture expansion** | Conditional supplement | A new macOS major version, a new chip generation, or a change to the published runtime identifiers | B + U + X-01 + X-05 + X-30 on the new host, the relevant P rows, and the artifact and trust gates | 12–24 elapsed h plus review |

Times are planning ranges, not pass criteria. Candidate re-downloads, thermal
recovery, large-data generation, macOS updates, device reauthorization, evidence
review, and defect retries extend them. A first macOS run will exceed every one
of these ranges, because there is no baseline to compare against and because
every `New on macOS` row in §11 is being observed for the first time; budget for
that explicitly rather than reporting it as a slip.

Parallel execution is valid only on independent Macs, accounts, and devices with
distinct run identifiers and session and evidence roots. Two performance
workloads on one Mac are not parallel evidence, and two VMs on one host are not
two independent performance hosts. **Two architectures are not two hosts
either**, but they are two candidates: an `osx-arm64` result never satisfies an
`osx-x64` row and the schedule manifest must show them separately.

Treat each schedule as a checklist with dependencies, not a bag of identifiers.
Record `not started | running | pass | fail | blocked | N/A` for every selected
row and name the prerequisite finding for every Blocked result. Artifact smoke is
the entry gate. Full macOS plus Soak is the ordinary release set; add Upgrade and
Architecture expansion when their triggers apply. Focused slices are planning and
rerun views over rows already present in Full macOS. One scenario result may
satisfy several schedules only when it uses the same exact candidate, the same
architecture, and meets the strictest host, Q-state, S-state, hardware, oracle,
and evidence requirement of all of them; otherwise create a distinct attempt. The
schedule manifest must show this many-to-one mapping so reuse cannot turn an
unexecuted matrix cell green.

### 12.1 Change-based minimum selection

| Changed area | Minimum live rerun |
|---|---|
| Packaging, runtime, version, notices, signing | B-01, B-02, B-03, B-04, B-17, A-28, I-01, I-12, I-13, P-13, P-18, P-21, R-01–R-05, R-32, R-79 |
| Anything architecture-dependent, or a runtime-identifier change | B-02, I-01, I-14, I-16, X-30, R-01–R-03 |
| Parser, time, detection | B-05, B-06, B-08, B-21, A-06–A-10, A-20, A-37, X-26, X-27, I-02, I-11, R-72, R-73, R-91, R-94 |
| Store, manifest, checksums, mapping | B-12–B-15, B-18, A-10, A-11, A-29, A-33, X-12–X-17, X-26, X-29, I-03, I-04, R-10, R-68, R-85 |
| ADB | B-11, B-12, A-15–A-20, X-05, X-08–X-10, I-05, I-06, P-19, R-13, R-50–R-55, R-71 |
| Growing file, framing | B-13, A-21, A-22, X-06, X-10, X-24, R-28, R-50–R-52 |
| Query, filter, search, templates, paging | B-07–B-10, A-01–A-05, X-03, X-04, X-17–X-19, I-07, R-76, R-77, R-81, R-82 |
| Timeline, rendering, layout, theme, tabs | B-06, B-09, B-10, B-19, A-05, A-13, X-04, X-18, X-23, the Display schedule, R-37, R-42–R-45, R-63–R-65, R-74, R-75 |
| Settings, cache, retention, data-root resolution | A-12–A-14, A-29, A-32, A-34, A-36, X-20, X-22, P-02, P-08, P-09, P-16, P-22, P-23, R-06, R-07, R-67, R-69, R-74 |
| Save, export, archive, diagnostics | B-14–B-16, A-25, A-27, A-35, X-12, X-14, X-15, X-25, X-26, I-03, I-04, I-07, I-08, P-03, P-04, P-10, R-18, R-78, R-83, R-84, R-86 |
| CLI command, parser, console, generator | I-01–I-07, I-10–I-16, P-05, P-06, P-07, P-11, P-15, R-25, R-70, R-80, R-86 |
| Accessibility, focus, keyboard | B-20, B-21, U-06–U-08, U-11–U-19, U-24–U-28, R-11, R-58–R-62, R-72–R-76 |
| Window, display, energy, or lifecycle handling | B-19, A-30, A-31, A-38, X-07, X-23, X-28, U-01–U-05, U-19, U-26, R-14, R-63 |
| Anything touching the snapshot-refresh path | **R-19–R-21 by the X-01 A/B procedure on both architectures**, plus X-13 and B-21; the unit suite does not cover this |
| Anything touching paths, links, or containment | A-14, A-26, P-08, P-16, X-20, X-24, R-09, R-10, R-85 |

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
First macOS run for this product? (yes means no baseline exists — say so on every measurement)

Architecture under test:            osx-arm64 | osx-x64
Desktop asset path / URL / size / SHA-256:
SHA256SUMS path / SHA-256 / matching line:
Provenance attestation result:
Archive member-safety review result:
Extracted root / inventory hash / mode bits of VisualCat and vcat:
Code signature: identifier / adhoc? / CDHash / team identifier / verify result:
Bundled .dylib signature verification result:
spctl --assess verdict, verbatim:
Architectures present (lipo -archs):
Quarantine state of archive / executable / a bundled library (xattr -l):
Q-state exercised (Q0–Q7) and the remedy applied, verbatim:
Desktop version as displayed / as in README.txt / as in archive name:
CLI asset / version / hash / architecture:
Release tag / commit / channel:

macOS ProductVersion / BuildVersion:
Mac model / chip / cores / memory:
Process translated? (Rosetta 2 present / in use):
Physical / VM (hypervisor) / remote:
System Integrity Protection / Gatekeeper policy / MDM profile / endpoint agent:
Display(s): resolution, scaled resolution, backing scale, refresh, ProMotion,
            colour profile, orientation, primary, notch:
Displays have separate Spaces / Stage Manager / Dock and menu-bar auto-hide:
S-state(s) exercised:
Appearance / auto-switching / accent colour / highlight colour:
Increase contrast / Reduce motion / Reduce transparency / Differentiate without colour:
Keyboard navigation (Full Keyboard Access) / fnState / layout / input source:
VoiceOver and Accessibility Inspector versions:
Locale / language / region / time zone / tzdata state:
Volumes: format, case sensitivity, encryption, and mount options for home,
         session root, corpus, evidence:
Time Machine state / local snapshots / Spotlight indexing per volume:
iCloud Drive state and whether Desktop and Documents are synced:
umask / ulimit -n / launchctl limit maxfiles / kern.maxfilesperproc:
TMPDIR (as reported) and its resolved /private path:
Privacy grants already held by the launching terminal
  (Full Disk Access, Files and Folders, Desktop, Documents, Downloads,
   Removable Volumes, Accessibility, Screen Recording, Developer Tools):
Terminal application and version / shell (zsh or bash):
Background load: Spotlight, Time Machine, iCloud, Photos, backup, antivirus:
Power source / Low Power Mode / battery / thermal state / caffeinate assertion:

ADB path / origin / version / hash / architecture / USB accessory approval:
Android serial / model / API / fingerprint / clock / time zone:
VisualCat data root (resolved) and how it was resolved:
  reconciled against PRIVACY.md? (match | differs — file a finding)
Settings / sessions / diagnostics / leases paths:
Starting data profile D0–D4 / execution state M0–M9:
Corpus manifest path / hash:
Evidence root (and confirmation it is outside iCloud, Desktop and Documents):
Trace, recorder, and camera configuration:
Baseline run ID used for performance comparison (or: none, first observation):
Abort thresholds: free space (available, not purgeable) / thermal / duration /
                  trace size / privacy:
Coverage-matrix cells intentionally absent:
Open mutation-ledger rows at start (must be none or explicitly inherited):
```

### 13.2 Result row — one per scenario

```text
Scenario ID / exact title:
Status: PASS | FAIL | BLOCKED | N/A
Architecture / translated?:
Schedule(s) and matrix cell(s) satisfied by this attempt:
Attempt / prerequisite scenario and finding IDs:
Candidate executable hash / PID(s) / executable path:
Start/end UTC:
Starting M-state / Q-state / S-state / D-profile / preconditions:
Exact source / commands / input / actions:
Expected oracle / budget:
Observed result and verbatim product text:
Verbatim macOS text, where the platform spoke (Gatekeeper, a consent prompt,
  a loader error, a jetsam entry):
Measurements: repetitions, median, p95, min/max, samples, method, and whether
  this is a first observation with no baseline:
Integrity result: hashes / counts / verify / raw ranges / gaps / modes / xattrs:
UX loop: discoverability / state / acknowledgement / outcome / recovery / consistency:
Accessibility and visual baseline result, with display, backing scale, colour
  profile and resolved font families:
Host observations: unified log / .ips reports / spindump / fs_usage / footprint:
Evidence paths and SHA-256:
Mutation-ledger row(s) / restoration proof:
Finding IDs / rerun dependency:
```

Do not write "works", "looks good", "responsive", or "no crash" without the
corresponding evidence. A pass row must be independently auditable.

### 13.3 Defect report — one per finding

```text
Finding ID / severity / title:
First observed run / scenario:
Candidate archive and executable SHA-256 / version / architecture / PID:
macOS version and build, Mac model and chip, translated or native:
Q-state, S-state, M-state, D-profile:
Display, backing scale, refresh, colour profile, appearance, accent:
Volume format, case sensitivity, mount options, umask, ulimit -n:
Gatekeeper, SIP, MDM, endpoint-agent and TCC state, including which grants the
  launching terminal held:
ADB, device and source state where applicable:
Preconditions and exact reproduction, including the exact command line and shell:
Expected: citation to this plan or a repository contract
Actual: verbatim text, measurement and integrity effect
Reproduction rate / attempts:
Does it reproduce on the other architecture? On another macOS version? Outside a VM?
Does it reproduce with a different Q-state — that is, is it Gatekeeper or the product?
Time-bounded unified-log, .ips, exit-signal and jetsam evidence:
Session, corpus and input hashes, and a safe attachment location:
Screenshots / video / traces / diagnostic bundle:
First suspected layer: package | signature or trust | macOS framework |
  window server | privacy consent | source | ingest | store | query |
  view model | Avalonia/Skia | ADB | Rosetta
Appendix-B trap checks completed:
Security and privacy handling, and redactions applied:
Workaround / affected users / release impact:
```

Severity: **Blocker** prevents verified launch, loses or corrupts data, executes
or escapes an untrusted boundary, leaks protected content, or prevents the
release gate. **Major** breaks a primary workflow, gives silent wrong results,
crashes or hangs, or makes the product unusable with a required accessibility
mode. **Minor** has a bounded workaround or affects a secondary path. **Polish**
is perceptible without impeding completion or correctness. Severity measures
impact, not fix effort or reproducibility.

Two macOS-specific severity notes. A launch that the platform refuses — an
invalid signature on Apple silicon, a quarantine remedy that does not work — is a
**Blocker** even though the product's own code is blameless, because the artifact
as published cannot be run by the person it was published for. And a message that
names another operating system's mechanism is at least **Minor** even when
everything works, because it sends a reader to edit a file that does not exist.

### 13.4 Release exit criteria

1. The schedule-accounting manifest covers every applicable row in Full macOS and
   Soak, plus triggered Upgrade and Architecture-expansion rows, **for each
   published architecture**, with no unaccounted scenario or matrix cell. Every B
   and applicable R row passes on the **exact uploaded or upload-ready immutable
   macOS tarballs**; every other required row is Pass or has an explicit release
   exception linked to its finding or gap, owner, affected population, risk,
   mitigation, and expiry. Local source builds do not substitute, and an archive
   created on a non-Unix host never satisfies a mode-bit or launch row. I-01 and
   I-12–I-16 pass on the exact matching CLI tarballs.
2. Required §1.4 matrix cells are exercised, or each gap has an owner, affected
   population, risk, mitigation, expiry, and explicit release approval. An
   accepted gap remains untested, not passed. At minimum, `osx-arm64` is covered
   natively and `osx-x64` is executed at least once — natively on an Intel Mac or
   under Rosetta 2, with which one stated. Shipping an architecture that no
   automated gate and no live run has ever executed is not a coverage gap; it is
   an untested product.
3. No open Blocker or Major finding. Every open Minor or Polish finding has an
   owner, affected configuration, workaround or rationale, explicit release
   acceptance, and target release or expiry. Security and privacy boundary
   failures in P-03, P-04, P-06, P-07, P-08, P-09, P-10, P-11, P-12, P-16, P-18
   or P-22 are Blockers by default.
4. B-05/B-06/B-12/B-14/B-15/B-16 and I-02–I-08, I-15 and I-16 parity and
   integrity oracles are exact; every released session, save, export, and
   portable path verifies; the cross-platform comparison against the Windows and
   Linux candidates is byte-exact everywhere the contract requires it; and the
   two macOS architectures agree exactly.
5. Zero attributable application crash, unhandled managed exception, native
   fault, hang, or unexplained exit inside scenario windows. An external
   termination — a jetsam kill, a log-out — is classified from host evidence and
   not counted as a product crash, but its recovery behaviour must still pass.
6. The trust gate passes: signatures verify, the published checksum and
   provenance instructions are correct and come before the quarantine remedy, the
   remedy works exactly as written, and nothing in the product or its
   documentation claims a signature, notarization, or publisher identity it does
   not have. [`RELEASE-CHECKLIST.md`](RELEASE-CHECKLIST.md) asks that the
   Windows and macOS signing and notarization state match the release decision
   and the public documents, with tested OS-warning instructions for any
   intentionally unsigned artifact; this run is the evidence for the macOS half
   of that line, and "tested" now has a meaning it did not have before.
7. The ADB release gate proves physical-device discovery, capture, stop, format
   and time policy, buffer attribution, reconnect and failure, and child cleanup,
   and it proves that no remedy text offered on macOS names a mechanism this
   platform does not have. Results bind to the exact serial and platform-tools
   hash.
8. Soak completes without sustained resource or latency growth outside baseline,
   data loss beyond explicitly observed source or platform drops, orphan work,
   descriptor growth trending toward the platform's default limit, or a power
   assertion the product left behind.
9. The Accessibility schedule completes with the primary journey possible by
   keyboard and by VoiceOver, correct modal boundaries, no private content
   spoken, and usable contrast, text-scale, and display-scale configurations. The
   keyboard contract and [`KEYBOARD.md`](KEYBOARD.md) agree about macOS. Any
   limitation of the toolkit's macOS accessibility surface is recorded as a known
   limitation with its effect, not as N/A.
10. Every absolute budget miss is fixed or explicitly accepted with owner,
    rationale, affected configuration, and expiry. On a first macOS run, the
    measured values are recorded as the baseline and labelled as first
    observations; from the second run onward the >20% like-for-like regression
    signal applies, with native and translated results compared only against
    their own baselines. A frame measurement with zero or constant frames is
    Blocked, never Pass, and the measurement method and the display's active
    refresh are both named.
11. A-29 passes from the previous supported release whenever settings, session
    format, cache, default path, versioning, runtime, or packaging changes — and
    whenever the resolved data root changes, which on this platform is a
    migration question and not only a documentation one.
12. Candidate hashes, versions, notices, checksums, provenance, and the
    documented launch instructions all agree; no undeclared dependency,
    administrator authorization, installer, auto-update, `launchd` agent, login
    item, `.app` bundle, file association, or network traffic appears.
13. The run header, results, defect links, evidence hash index, sensitive-data
    retention decision, and completed mutation ledger are archived with the
    release. The corresponding manual gates in
    [`RELEASE-CHECKLIST.md`](RELEASE-CHECKLIST.md) reference the run ID — at
    minimum its signing-and-notarization line and its requirement that one Unix
    artifact be tested on a clean machine. Where previous releases recorded
    *macOS hardware validation* as intentionally deferred, this run's identifier
    replaces that deferral, or the deferral is restated deliberately with its own
    owner and expiry.
14. The §2.11 capability manifest has no unresolved candidate, documentation, or
    claim contradiction, and every unclaimed visible capability has a tested
    contract.
15. Final cleanup in §13.5 passes. A green run that leaves a test account, a
    mounted disk image, a created APFS volume, an altered privacy grant, a
    changed Gatekeeper policy, a disabled Spotlight index, a held `caffeinate`
    assertion, a modified energy setting, an orphan `adb`, generated secrets,
    huge corpora, or a low-space condition behind is incomplete.

### 13.5 Mandatory cleanup and Mac hand-back

Run after success, failure, abort, crash, or power recovery. Restore ledger
values — not guessed defaults.

1. Stop and close every `VisualCat` and `vcat` process, import, capture and
   follow producer, ADB traffic generator, tracer, recorder, and pressure tool.
   Confirm the exact PIDs ended and that no VisualCat-owned `adb` child remains
   (`pgrep -a adb`, checked by parent). Do not kill a shared ADB server unless
   the ledger says this run created and owns it. Use `pkill -x`, or the PID —
   never a `pkill -f` pattern that also matches your own command line.
2. Release every power assertion the run took (`pmset -g assertions` must show
   none belonging to this work) and confirm the Mac can sleep and that the
   display sleeps again.
3. Hash and archive the required evidence, then delete only the exact recorded
   temporary, corpus, extraction, session, and export paths according to
   retention. Resolve each path and confirm it is inside the intended test root
   before any recursive removal. Report what is recoverable and what is not —
   and note that on a volume with local snapshots, deleted evidence may persist
   in them.
4. Unmount and delete only the disk images, APFS volumes, network mounts, and
   filler files the run created, after resolving their exact targets. Verify that
   free space and volume health recover on the startup volume as well as on the
   test volume.
5. Restore file modes, ACLs, `chflags` flags, and ownership; `umask`; `ulimit`
   and `launchctl limit maxfiles`; Spotlight indexing per volume; and Time
   Machine state. Remove test-created local snapshots only when the ledger
   recorded creating them.
6. Restore the clock and time zone; locale, language, region, and input sources;
   display resolution, scaled resolution, arrangement, primary display, refresh
   and rotation; appearance and automatic switching; accent and highlight colour;
   contrast, motion, transparency and colour filters; Zoom and other
   accessibility services; Dock and menu-bar auto-hide; Stage Manager; keyboard
   `fnState` and Full Keyboard Access; and energy and sleep settings.
7. Restore trust state exactly. Re-grant or revoke only the privacy consents the
   ledger recorded changing, and never reset consent broadly on a machine
   somebody uses. Confirm Gatekeeper policy is as found, and confirm System
   Integrity Protection was never touched. Remove any quarantine attribute you
   added to a corpus file, and leave the ones you did not add alone.
8. Restore Android buffer, debugging, authorization, and device state, and remove
   test traffic and artifacts as the device's owner requires. Re-run the serial,
   fingerprint, buffer, and free-space checks. Leave the Mac's USB-accessory
   approval as the owner wants it.
9. Remove test accounts only when the exact account, its home directory, creation
   evidence, and owner-approved deletion are recorded. Never alter a real
   organizational policy or MDM profile to tidy up a test.
10. Decide explicitly whether the data root and the extracted candidates remain.
    Deleting the extracted directory is not user-data removal; deleting the data
    root destroys sessions, settings, leases, and diagnostics. Do exactly the
    recorded hand-back, and account for the runtime diagnostic sockets in
    `$TMPDIR` that P-18 counted.
11. Restart if a restoration mechanism requires it, then re-run the §2.2
    identity, policy, and free-space checks and prove that the graphical session,
    ordinary applications, USB, audio, network, display sleep, and power are
    healthy.
12. Attach the completed ledger and the cleanup evidence to the run record; no
    row remains without *Restored* or an explicit owner-accepted residual state.

---

## Appendix A — macOS cookbook

These examples are templates. Resolve every placeholder and quote every path.
Commands that change host policy, permissions, volumes, the clock, display
settings, privacy consent, ADB buffers, or data require a ledger row and scenario
authorization. Everything here uses the BSD userland a stock Mac has; nothing
assumes GNU coreutils.

```shell
# --- artifact identity ---------------------------------------------------
shasum -a 256 -- '<candidate-tar>' '<cli-tar>'
grep -F -- "$(basename '<candidate-tar>')" SHA256SUMS
gh attestation verify '<candidate-tar>' --repo benny-cz/VisualCat
xattr -l -- '<candidate-tar>'                     # quarantine and where-from
tar -tvzf '<candidate-tar>'                       # members, modes, owners, sizes
tar -xzf '<candidate-tar>' -C '<candidate-root>'
find '<candidate-root>' -exec stat -f '%Sp %z %N' {} + | sort -k3
xattr -lr '<candidate-root>'
file -- '<VCAT>'; lipo -archs '<VCAT>'; otool -L '<VCAT>' | head -20

# --- code signature and Gatekeeper ---------------------------------------
codesign -dv --verbose=4 '<VCAT>' 2>&1
codesign --verify --strict --verbose=2 '<VCAT>'
find '<candidate-root>' -name '*.dylib' -exec codesign --verify --strict {} \; 2>&1 | head
spctl --assess --type execute --verbose=4 '<VCAT>' 2>&1   # rejection is expected
spctl --status                                             # policy; never disable it
xattr -d com.apple.quarantine '<VCAT>'                     # the documented remedy
xattr -dr com.apple.quarantine '<candidate-root>'          # the whole-tree comparison

# --- translation and architecture ----------------------------------------
uname -m; sysctl -n sysctl.proc_translated 2>/dev/null
/usr/bin/pgrep -q oahd && echo 'Rosetta 2 present'
arch -x86_64 '<candidate-root>/vcat' --version              # force translated
arch -arm64  '<candidate-root>/vcat' --version              # force native

# --- resolved product data paths -----------------------------------------
# Do not assume; find what the product actually created.
find "$HOME/Library/Application Support" "$HOME/.local/share" -maxdepth 1 \
  -name 'VisualCat' -type d 2>/dev/null
DR='<data-home>'
ls -le@ "$DR"; du -sh "$DR"/*
find "$DR" -exec stat -f '%Sp %Su:%Sg %N' {} + | sort -k3
xattr -lr "$DR" | head

# --- launch with argument boundaries -------------------------------------
'<VCAT>' &
'<VCAT>' --log '<corpus-root>/small.txt' &
'<VCAT>' --session '<session-root>/<session>' &
'<VCAT>' -- "$(printf 'name-with\nnewline.txt')" &
'<VCAT>' > '<evidence-root>/<run-id>/stdout.txt' 2> '<evidence-root>/<run-id>/stderr.txt' &
open '<VCAT>'                                   # Launch Services route, no arguments

# --- observing the window and the process --------------------------------
pid=$(pgrep -x VisualCat)
ps -o pid,ppid,user,stat,etime,time,rss,vsz,comm -p "$pid"
footprint "$pid" | head -20                     # phys_footprint: the figure that matters
vmmap -summary "$pid" | head -30
lsof -p "$pid" | wc -l; ulimit -n; launchctl limit maxfiles
top -l 2 -pid "$pid" -stats pid,cpu,mem,threads,state | tail -5
sample "$pid" 10 -f '<evidence-root>/<run-id>/sample.txt'
spindump "$pid" 10 -file '<evidence-root>/<run-id>/spindump.txt'   # for a hang

# --- screenshots ----------------------------------------------------------
# Needs the Screen Recording grant for the terminal running it; without it the
# capture silently comes back as wallpaper with no windows.
screencapture -x '<evidence-root>/<run-id>/<scenario>-<assertion>.png'
screencapture -x -R 0,0,1440,900 '<evidence-root>/.../region.png'
screencapture -x -w '<...>.png'                 # click the window to capture it
# A scripted single-window capture needs the window id, which macOS ships no
# first-party command for. Use the interactive -w form, or a tool you have
# recorded, and never a full-screen capture where the assertion is about one
# window's bounds.
sips -g pixelWidth -g pixelHeight '<...>.png'   # native pixels, not logical points

# --- time-bounded host failure evidence ----------------------------------
log show --style compact --start '<start-local>' --end '<end-local>' \
  --predicate 'process == "VisualCat" OR senderImagePath CONTAINS "VisualCat"'
log show --style compact --last 30m --predicate 'eventMessage CONTAINS "memorystatus"'
ls -lt ~/Library/Logs/DiagnosticReports/ | head -20
grep -l VisualCat ~/Library/Logs/DiagnosticReports/*.ips 2>/dev/null
sudo fs_usage -w -f filesys "$pid"              # needs privileges; SIP limits it

# --- file identity, permissions, links, flags -----------------------------
stat -f '%i %Sp %Su:%Sg %z %N' -- '<path>'
ls -le@ '<path>'                                # ACLs and extended attributes
xattr -l -- '<path>'; chflags -- '<path>' 2>/dev/null; ls -lO '<path>'
diskutil info '<path>' | grep -iE 'Case|File System|Mount Point'
cmp -- '<a>' '<b>' && echo identical            # byte comparison, not diff
tr -dc '\r' < '<file>' | wc -c                  # CR count; never a shell-escape grep
ditto '<src>' '<dst>'                           # preserves xattrs; plain cp does not

# --- a case-sensitive APFS volume, without repartitioning -----------------
# Read the exact personality name off this macOS rather than hard-coding it,
# and read the container disk off this Mac rather than assuming disk1.
diskutil listFilesystems | grep -i -B1 -A1 'case-sensitive'
diskutil list | grep -i 'APFS Container'
diskutil apfs addVolume <container-disk> '<case-sensitive-personality>' VCatCaseSensitive
diskutil info /Volumes/VCatCaseSensitive | grep -i case   # prove it before using it
# ... run the case rows against /Volumes/VCatCaseSensitive ...
diskutil apfs deleteVolume VCatCaseSensitive

# --- an isolated volume for low-space work --------------------------------
hdiutil create -size 4g -fs APFS -volname VCatTest '<evidence-root>/vcat-test.dmg'
hdiutil attach '<evidence-root>/vcat-test.dmg'
mkfile 3800m /Volumes/VCatTest/filler           # approach ENOSPC deliberately
hdiutil detach /Volumes/VCatTest
rm -f '<evidence-root>/vcat-test.dmg'

# --- memory pressure on a dedicated Mac only ------------------------------
memory_pressure -h            # read this host's flags; they differ by version
sudo memory_pressure -l critical -s <seconds>   # bounded, never open-ended
log show --last 5m --predicate 'eventMessage CONTAINS "memorystatus"' --style compact
# Stop the pressure explicitly before the next row; a machine left under
# simulated pressure will terminate unrelated processes, including the tester's.

# --- energy, sleep, and keeping a soak awake ------------------------------
pmset -g; pmset -g assertions
caffeinate -dimsu -w "$pid" &                   # released when the PID exits
tmutil listlocalsnapshots /                     # blocks a deletion cannot free
mdutil -s /                                     # Spotlight state per volume

# --- residue the product must not have created ----------------------------
# -d would prompt for the keychain password once per item; the name search does not.
security find-generic-password -s VisualCat 2>&1 | head -3
security dump-keychain 2>/dev/null | grep -i visualcat || echo 'no keychain item'
ls ~/Library/LaunchAgents /Library/LaunchAgents 2>/dev/null | grep -i visualcat || echo 'no agent'
ls ~/Library/Preferences | grep -i visualcat || echo 'no preferences plist'
ls ~/Library/'Saved Application State' 2>/dev/null | grep -i visualcat || echo 'no saved state'
ls ~/Library/Containers ~/Library/'Group Containers' 2>/dev/null | grep -i visualcat || echo 'no container'
ls "$TMPDIR" | grep -c 'dotnet-diagnostic'    # P-18 counts these

# --- ADB identity: discovery may be unqualified; capture is not ----------
'<adb>' version; shasum -a 256 -- '<adb>'; lipo -archs '<adb>' 2>/dev/null
'<adb>' devices -l
'<adb>' -s '<serial>' shell getprop ro.serialno
'<adb>' -s '<serial>' shell date -u
'<adb>' -s '<serial>' logcat -d -v threadtime -s VCATTEST | tail -5
system_profiler SPUSBDataType | grep -iA6 'android\|adb' || echo 'no ADB interface seen'

# --- candidate CLI cross-checks; never the only oracle -------------------
S='<corpus-root>/parity-<run-id>.vcat'
'<VCAT-CLI>' index '<corpus-root>/small.txt' --output "$S"
'<VCAT-CLI>' verify "$S"; '<VCAT-CLI>' verify "$S" --require-raw
'<VCAT-CLI>' info "$S"; '<VCAT-CLI>' stats "$S"
'<VCAT-CLI>' query "$S" --levels E --limit 50 | head
'<VCAT-CLI>' export "$S" "$S.csv" --type csv
'<VCAT-CLI>' export "$S" "$S.zip" --type portable-zip
```

### A.1 Tracing and privilege discipline

`fs_usage`, `dtruss`, and the rest of the DTrace family need privileges and are
restricted by System Integrity Protection; a tool that returns nothing under SIP
has produced an instrumentation Block, not a clean result, and the answer is
never to disable SIP. `sample` and `spindump` need no special privileges for a
process you own and answer most "where is the time going" questions. Never
collect a §4.2 timing budget under `fs_usage` or under Instruments' heavier
instruments; use them to answer a question, then re-measure clean. Record the
collection overhead with every trace, keep the raw artifact, and scope every
filter to the recorded PID — a global trace is huge, intrusive, and likely to
collect content unrelated to VisualCat.

Anything that drives the interface — AppleScript through `System Events`,
`cliclick`, a recorded macro — needs the *Accessibility* privacy grant for the
**terminal** running it. Grant it deliberately to a terminal used for testing,
record it in the ledger, and revoke it at hand-back. A tool that silently does
nothing is almost always this, not the product.

### A.2 Safe clean-profile preparation

A clean profile is either a new account or a reversible move of one directory.
Prefer the new account: it cannot damage the tester's own data, it proves the
first-run path including directory creation, and — uniquely useful here — it has
granted no privacy consent to anything, which is the only way to see the Q6
prompts a real first-time user sees.

```shell
# preferred: a dedicated account on a disposable Mac, created through
# System Settings so the account is complete and its home is properly created.

# alternative: a reversible move, with the path proved first
root='<data-home>'
ls -ld -- "$root"                           # confirm it is the intended directory
pgrep -x VisualCat || pgrep -x vcat         # must be empty
mv -v -- "$root" "$root.backup-<run-id>"    # reversible; never rm at this stage
# restore: mv -v -- "$root.backup-<run-id>" "$root"
```

Never run a recursive removal against a path built from a variable that might be
unset. Resolve it, print it, confirm it, then act. And remember that a *move*
inside one volume is instant and reversible while a *delete* on a volume with
local snapshots is neither fully reversible nor fully effective.

---

## Appendix B — macOS traps that impersonate product bugs

Check every finding against these before filing, and record the check in the
defect report.

**Userland and shell**

1. **macOS ships BSD userland, not GNU coreutils.** `sha256sum`, `stat -c`,
   `readlink -f`, `timeout`, and `grep -P` do not exist; `head -c -1` is
   rejected; `sed -i` requires an argument; `date` has no `%N`. A recipe copied
   from the Linux plan fails, or silently produces a different file, and the
   corpus then disagrees with its own manifest.
2. **The default shell is `zsh`, and it is not `bash`.** An unquoted glob that
   matches nothing is an error rather than being passed through, `$(...)` does
   not word-split, and a leading `=` is filename expansion. Say which shell
   produced a transcript.
3. **`pkill -f "VisualCat"` can kill its own shell**, because the pattern matches
   the command line containing it. Use `pkill -x VisualCat`, or the PID. The same
   trap has already killed a product under test along with an unrelated tool
   whose name appeared in a test file's path.
4. **`kill -INT` on a background job of a non-interactive shell reaches
   nothing.** Such a shell sets `SIGINT` to be ignored for its asynchronous
   children and the disposition survives `exec`, so the process runs to
   completion and exits `0` — which looks exactly like a clean cancellation.
   `SIGTERM` and `SIGHUP` are delivered normally, which is what makes the set
   look asymmetric when it is not.
5. **A shell-escape grep is not a carriage return.** `grep $"\r"` in `bash` is
   locale-translation syntax and matches any line containing the letter *r*,
   making every file look like CRLF. This has already faked a cross-platform
   line-ending finding against this product. Count bytes with
   `tr -dc '\r' < f | wc -c`.

**Trust, signing, and quarantine**

6. **Apple silicon refuses to execute an unsigned `arm64` binary.** The kernel
   kills it and the shell prints `Killed: 9` with no managed stack and no other
   diagnosis. Check `codesign --verify` before filing anything about a launch
   failure — an ad-hoc signature is expected, and its absence or invalidity is
   the whole explanation.
7. **`spctl --assess` rejecting the artifact is the documented state.** The
   release is unsigned and un-notarized by design. The rejection is not a
   finding; a *missing* rejection, or a claimed publisher identity, would be.
8. **Quarantine propagation depends entirely on the download and extraction
   route.** A browser download extracted by Archive Utility propagates
   `com.apple.quarantine` to the extracted files; `curl` plus `tar -xzf` usually
   does not. A tester who uses the command line for both silently skips the whole
   Gatekeeper story and will report that there is nothing to see.
9. **`xattr -dr` on a single file is not recursive over the tree.** The `-r` has
   nothing to recurse into. The published remedy names only the executable, and
   the extraction contains many bundled libraries; whether that is sufficient is
   Q3 against Q4, not an assumption.
10. **A first launch of a quarantined tree includes a malware scan.** Gatekeeper
    and XProtect assess a large self-contained directory on first launch, which
    can dominate the cold-launch measurement. Keep it, report it separately, and
    do not discard it as an outlier: it is the real first-user experience.
11. **`sysctl sysctl.proc_translated` answers for the calling process.** Reading
    it in a shell tells you about the shell. Read it from a child of the process
    under test, or use Activity Monitor's **Kind** column.
12. **Rosetta translates ahead of time and caches the result.** The first
    translated launch of a binary is materially slower than every later one. Do
    not compare a first translated launch with a warm native one and call the
    difference a regression.

**Filesystem and paths**

13. **APFS is case-insensitive by default and case-sensitive on request.** Case
    behaviour is a property of the volume, not of the platform. Two paths
    differing only in case are one file on the volume most Macs have and two on
    a volume that exists for exactly this test. Say which you used.
14. **APFS is normalization-insensitive and normalization-preserving.** A name
    written in NFC and the same name written in NFD are **one file**, stored as
    first written. This is the opposite of Linux, where they are two files, and
    it is where a cross-platform session-identity defect will surface first.
15. **`/var` is a symlink to `/private/var`, and `$TMPDIR` lives under it.** A
    validator that refuses any reparse point among a path's ancestors refuses
    every temporary directory on every Mac. That is not hypothetical: it once
    made every capture report *Temporary storage is unavailable* here.
16. **Finder shows `/` where POSIX has `:` and vice versa.** A name that looks
    like it contains a slash in Finder contains a colon to every API. Neither
    rendering is wrong; a report that does not say which layer it came from is.
17. **A Finder alias is not a symlink.** It is an ordinary file carrying a
    bookmark, so a check that refuses links accepts it and then fails on content.
18. **A bundle is a directory that Finder draws as a file.** An `.app`, an
    `.rtfd`, or a photo library selected in a chooser is a directory. The refusal
    must describe what the user thinks they chose.
19. **`.DS_Store`, `._` AppleDouble files, extended attributes, and Finder tags
    appear without anyone asking.** Viewing a folder in Finder creates a
    `.DS_Store` inside it; copying to a FAT or SMB volume creates `._` siblings.
    Neither is the product's doing, and a directory comparison that does not
    exclude them produces false differences.
20. **`cp` does not preserve extended attributes; `ditto` does.** Copying
    evidence with plain `cp` silently changes what you are holding.
21. **APFS clones make a copy cost nothing.** A duplicated corpus file consumes
    no additional space until it is written to, so a free-space oracle based on
    file sizes will be wrong.
22. **Time Machine local snapshots pin deleted blocks.** A cleanup that reports
    reclaimed bytes while `df` does not move is correct. So is a "deleted"
    session whose bytes are still on the disk — which is why deletion is
    explicitly not a secure erase here.
23. **APFS "purgeable" space is neither free nor used.** `df` and Finder give
    different answers. State which one an abort threshold is measured against.
24. **iCloud Drive files can be dataless.** With *Optimize Mac Storage* active a
    file's bytes may not be local, and opening it triggers a download that can
    fail or stall. *Desktop & Documents Folders* sync makes this true of paths a
    tester would never think of as cloud storage.
25. **Spotlight indexes new files within seconds.** It holds them briefly, costs
    I/O during a measurement, and makes log content searchable from the system's
    own search interface.

**Privacy, consent, and capture**

26. **TCC attributes consent to the launching application.** For a bare
    executable started from a shell, the responsible process is the terminal. The
    prompt names the terminal, a grant the terminal already holds means no prompt
    appears at all, and revoking it affects everything that terminal runs. A
    tester's daily terminal almost certainly has Full Disk Access, which makes
    the entire consent story invisible.
27. **`screencapture` without the Screen Recording grant returns the wallpaper.**
    No error, no windows — which looks exactly like the application having
    vanished.
28. **A screenshot of a locked or sleeping display is black.** So is one taken
    after the display slept during an unattended run. Hold the Mac awake with
    `caffeinate` for long rows and restore the energy settings afterwards.
29. **`tccutil reset` is account-wide.** It revokes consent that other
    applications depend on, and there is no undo.

**Keyboard, input, and window**

30. **`F3` is Mission Control.** Function keys are media and system keys unless
    the keyboard preference is changed, so a documented `F3` shortcut may never
    reach the application. `fn+F3` does.
31. **Option is a character-composing modifier.** `⌥1` and its neighbours produce
    characters in most layouts, so an Option-based chord may type instead of
    acting, and may deliver a character into a focused field.
32. **Control chords are text-editing bindings inside fields.** `⌃A`, `⌃E`,
    `⌃F`, and `⌃K` move and delete text in a macOS text field. A product shortcut
    on those letters will collide there and nowhere else.
33. **Mac laptop keyboards have no Home, End, or forward Delete.** They are
    `fn`-combinations, and `⌫` is Delete. A row that reports a shortcut as
    missing should first say which physical key was pressed.
34. **Secure Input can silently stop every keystroke.** When any application
    enables secure input — a focused password field, some password managers, a
    terminal in a particular state — macOS withholds key events from other
    processes, and an application that was accepting typing a moment ago simply
    stops. It looks exactly like a hung UI or a broken shortcut. Check with
    `ioreg -l -w 0 | grep SecureInput` before filing anything about keyboard
    input that stopped working, and record whether it was enabled and by what.
35. **macOS rewrites text as it is typed.** System-wide substitution turns
    straight quotes into smart quotes, `--` into an en dash, and `...` into an
    ellipsis in ordinary text controls. A regular expression, a time-zone
    identifier, or a path typed into such a field can reach the product as
    something the reader did not type, and the product's own error message will
    look wrong. Check what is actually in the field, and check System Settings,
    before filing a parsing defect.
36. **Background work runs on the efficiency cores.** On Apple silicon the
    scheduler moves a hidden, occluded, or App-Napped process onto the E-cores,
    and a soak measured with the window behind another one can be several times
    slower than the same work in front. That is the platform allocating power,
    not the product degrading. Record window state with every throughput number,
    and keep the two conditions as separate baselines.
37. **A fanless Mac throttles, and it does not announce it.** A MacBook Air on a
    long import slows because the enclosure is hot. `pmset -g therm` shows the
    reduced CPU limit; without that series beside the throughput series, the
    slowdown reads as a regression.
38. **The pasteboard survives the process exiting.** This is the opposite of X11,
    where the selection dies with its owner. A copy that is still pastable after
    quitting is correct here.
39. **Universal Clipboard can move a copy to another device.** If Handoff is on,
    a copied log line may reach the user's iPhone. That is the platform, not the
    product, but it belongs in the privacy record.
40. **"Maximized" is not a macOS concept.** There is a zoom button and there is
    native full screen, and they are different. A persisted maximized state has
    to mean one of them here.
41. **ProMotion refresh is adaptive.** A static window legitimately presents at a
    low rate. A frames-per-second number without the display's *active* refresh
    beside it says nothing about jank.
42. **A "scaled" resolution renders above native and downsamples.** Softness at a
    non-default *looks like* setting is the platform, not a layout defect.
43. **A Retina screenshot pixel is not a click coordinate.** At 2× backing scale
    a 900×600 window is 1800×1200 native pixels. Record the transform before
    measuring clipping or hit boxes.
44. **App Nap and occlusion throttling are the system's decision.** A drop in CPU
    when the window is hidden may be the product relaxing its cadence or the
    platform throttling its timers, and those have different consequences on
    restore. Record which.
45. **System sleep suspends the process and jumps the wall clock.** A capture
    that shows a gap across sleep is reporting the truth.

**Process, memory, and devices**

46. **macOS has no Linux-style OOM killer; it has jetsam and a memory
    compressor.** A termination under pressure arrives as `SIGKILL` with no
    managed stack and appears in the unified log as a `memorystatus` event.
    Resident size understates and overstates pressure by turns; `footprint`'s
    phys-footprint is the figure the platform itself uses.
47. **The default open-file limit is low.** Far lower than a Linux desktop's, and
    a memory-mapped session store finds it first. A product that works only after
    the tester raises `ulimit -n` does not work on a stock Mac.
48. **SIP strips `DYLD_*` across many exec boundaries.** A loader probe that is
    ignored is the platform's protection working, and should be recorded as a
    result rather than retried until it succeeds.
49. **`.ips` crash reports are not property lists.** `plutil` will not read one;
    the first line is JSON and the remainder is readable as text. Every one of
    them contains log content by construction, and whether the Mac offers to send
    it to Apple is host policy rather than product telemetry.
50. **Sleep, lid close, port changes, and docks re-enumerate USB.** The transport
    genuinely disappears and returns, which looks exactly like a parser failure.
    Correlate the system's own USB events before blaming ingest.
51. **macOS has no `udev`.** There are no rules to fix, no group to join, and no
    daemon to restart. A permission remedy that names any of those was written
    for another operating system. What macOS does have is a first-connection
    accessory approval on Apple silicon, and until it is answered the device is
    simply absent.
52. **A Finder double-click passes no arguments, and a launcher's environment is
    not a shell's.** A process started by the Finder inherits the login session's
    environment, so `PATH`, `LANG`, and `TMPDIR` set in `.zprofile` are simply
    absent — and an `adb` found through a shell profile can be invisible to it.
    "Works in my terminal" is not evidence about a user's launcher.
53. **Homebrew's prefix differs by architecture.** `/opt/homebrew/bin` on Apple
    silicon, `/usr/local/bin` on Intel. A `PATH` assumption that holds on one Mac
    fails on the other.

**Product and cross-platform**

54. **A release archive and a local publish can share a version but not bytes.**
    Only the recorded exact asset hash signs off a release. A successful source
    rerun is diagnostic evidence.
55. **Two live captures are not comparable by total.** Start-up, pre-roll, and
    scheduling windows differ. Exact parity needs the same finite bytes or a
    unique marker-bounded interval.
56. **Logcat declares its own drops.** Ring-buffer overwrite and source gaps are
    Android and ADB observations. VisualCat must account for them; it cannot
    recover bytes the source never delivered.
57. **Format modifiers degrade by device capability.** A device rejecting the
    richest timestamp modifiers can legitimately fall back. The product bug would
    be a wrong manifest or time policy, or unbounded negotiation — not the
    fallback itself.
58. **Automatic detection may refuse a corpus made mostly of defects.** Detection
    scores the whole file, not its size. Import such a file with an explicit
    format. The refusal is the confidence threshold working; the findings are the
    opposite cases — a file of defects accepted confidently, or a refusal whose
    message does not say that choosing a format is the way forward.
59. **The import-and-refresh race is not reproducible headlessly.** It needs a
    real compositor and a real import to overlap. The reliable check is an A/B of
    the shipped binary: open a one-million-line log with `--log` five times per
    build and read the summary line and the zoom readout off screenshots.
60. **The diagnostics bundle is the best available ingest oracle.** The
    structured records turn "the numbers look wrong" into "the view stopped at
    snapshot generation 2 of 5". Enable it before a sequencing investigation
    rather than guessing afterwards.
61. **A VM's clock jumps on snapshot restore, and its display is paravirtual.**
    Re-run §2.2 after every restore, and never take a frame-pacing baseline from
    a guest.

---

## Appendix C — coverage map

Every functional area and its primary scenarios. A change reruns at least its row
plus the change-based selection in §12.1.

| Area | Scenarios |
|---|---|
| Artifact, provenance, extraction, mode bits | B-01, I-01, I-12, P-04, P-13, P-18, R-32 |
| Code signature, architecture, Rosetta | B-02, I-01, I-14, I-16, X-30, R-01–R-03 |
| Gatekeeper, quarantine, and trust honesty | Q0–Q5, B-01, U-22, P-13, P-21, R-04, R-05 |
| Privacy consent and TCC attribution | Q6, Q7, B-05, A-10, P-02, P-09, P-21, R-90 |
| Host frameworks, loader, self-containment | B-03, S7, A-37, U-27, P-12, R-12 |
| Launch, arguments, working directory, launcher environment | B-01, B-04, B-17, A-15, A-37, I-10, P-11, P-12, R-56 |
| Data root, Library, and declared storage | A-36, P-02, P-18, P-23, R-06, R-07, R-08 |
| Window lifecycle and state | B-19, A-30–A-32, A-38, X-07, X-12, X-23, X-28, U-01–U-05, U-19, U-26, R-14 |
| Display, backing scale, multi-display, Spaces | S0–S6, A-30, X-23, U-01–U-05, U-09–U-14, U-23, U-24 |
| Energy, App Nap, sleep, and occlusion | A-31, A-38, X-05, X-07, X-27, M6, R-14 |
| Finite import, preview, formats | B-05, B-21, A-06–A-09, A-25, A-26, X-01, X-02, X-24, I-02, R-38, R-39, R-70, R-72, R-73, R-89, R-91 |
| Off-timeline and unparsed evidence | B-21, A-08, U-07, U-21, U-24, I-02, I-07, R-22–R-24, R-45, R-57, R-72, R-73 |
| File chooser and cancellation | B-05, A-25, U-19, P-05, R-88 |
| Host ADB discovery and capture | B-11, B-12, A-15–A-20, X-05, X-08–X-10, I-05, I-06, I-09, P-19, R-13, R-26, R-50, R-53–R-55, R-71 |
| Growing-file follow | B-13, A-21–A-24, X-06, X-10, X-11, X-24, X-27, X-28, R-28, R-50–R-52 |
| Ingest, snapshots, finalization, recovery | B-12–B-14, B-18, A-09–A-11, X-01, X-03, X-10–X-17, X-28, R-19–R-21, R-26–R-31, R-50, R-68 |
| Heat map, minimap, axis | B-06, B-09, A-04, X-01, X-04, X-18, X-23, U-01–U-03, U-09–U-15, U-23, R-37, R-40, R-42, R-43, R-47, R-63 |
| Search, regex, markers | B-08, A-03, A-05, A-08, X-04, X-19, U-06, U-16, I-02, I-07, R-70, R-76, R-81 |
| Filters, facets, statistics, templates | B-07, B-10, A-01–A-04, X-03, X-04, X-19, I-07, R-30, R-44, R-45, R-82 |
| Paging and bulk load | B-10, B-21, A-05, X-04, X-17, X-19, U-07, U-24, R-29, R-44, R-77 |
| Entry inspector, source context, pasteboard | B-06, B-10, B-21, A-08–A-10, A-24, X-04, X-18, X-19, U-07, U-14–U-17, P-05, P-14, R-16, R-41, R-48, R-49, R-57, R-65, R-66 |
| Sessions, recent, cache, retention, leases | B-14, B-18, A-03, A-05, A-10–A-14, A-29, A-33, X-12, X-13, X-20, X-22, X-26, X-28, U-06, P-16, P-16.1, R-33, R-36, R-67, R-69, R-85, R-92 |
| Save, portable archive, round trip | B-14, B-15, A-08, A-10, A-25, A-29, A-33, X-12–X-15, X-24–X-26, I-03, I-04, I-08, P-04, P-08, P-10, R-93 |
| CSV export and line endings | B-16, A-04, A-25, A-27, X-12, X-15, X-25, I-07, I-15, P-05, P-10, P-20, R-78, R-83, R-86, R-95 |
| Diagnostics and bundle | A-13, A-27, X-12, X-22, X-25, P-02, P-03, P-10, P-15, P-20, R-31, R-35 |
| Notice lane and status messaging | B-12, B-13, B-21, A-22, A-24, U-20, U-21, U-25, P-20, R-23, R-35, R-51, R-64 |
| The one visible, cancellable file operation | A-25, A-35, B-16, U-20, U-25, X-11, X-25, R-84, R-88 |
| Settings and upgrade | A-03, A-12–A-15, A-28–A-30, A-32, A-34, A-36, X-22, X-27, I-11, R-32, R-34, R-63, R-67, R-69, R-74, R-79 |
| Keyboard, modifiers, focus, modality | B-20, B-21, U-06–U-08, U-16, U-19, U-25, R-11, R-37, R-58–R-62, R-73, R-75, R-76, R-87 |
| VoiceOver and accessibility | B-21, U-07–U-13, U-16–U-25, P-14, P-20, R-11, R-58–R-62, R-73 |
| Appearance, accent, contrast, text scale, locale, fonts | A-13, A-37, X-23, X-27, U-09–U-13, U-18, U-23, U-24, U-27, I-11, R-17, R-46, R-63, R-74 |
| Time, zones, clocks, and the zone database | A-20, A-37, X-27, I-11, B-16, R-53, R-94, R-95 |
| Input methods, layouts, pasteboard | U-14, U-16, U-17, P-14, R-16 |
| Application identity, Dock, menu bar | B-04, U-05, U-19, U-26, U-28, P-18 |
| Performance, scale, endurance | §4.2, §4.3.1, X-01–X-11, X-17–X-23, X-25, X-29, X-30, R-27–R-30 |
| Filesystem, permissions, volumes, case and normalization | M3, M4, M7, M9, A-09, A-14, A-25, A-26, A-36, X-12–X-16, X-24, X-25, X-29, P-02, P-08–P-10, P-16, P-22, P-23, R-09, R-10, R-18, R-27, R-68, R-85 |
| Signals, process lifecycle, memory pressure | B-19, A-11, A-31, X-12, X-16, X-28, I-13, P-11, P-15, R-15 |
| Descriptor and resource limits | X-02, X-29, A-05, A-23, R-15 |
| Multi-instance and concurrency | M8, A-05, A-23, A-32, A-33, X-11, X-13, X-22, P-10, P-16, P-19, R-31, R-67, R-68 |
| Privacy, network, redaction, platform exposure | A-27, A-28, P-01–P-03, P-11, P-14, P-15, P-17–P-20, P-23, R-79 |
| Untrusted content: GUI, terminal, archive, session | A-08, A-26, X-24, X-26, P-04–P-08, P-12, P-20 |
| CLI contract, pipes, signals, generator | I-01, I-06, I-07, I-10, I-11, I-13–I-16, B-14–B-16, A-06, A-17, A-20, X-26, R-25, R-70, R-78, R-80 |
| Cross-platform and cross-architecture parity | I-02–I-08, I-11, I-14–I-16, B-15, A-26, A-37, X-30, R-03, R-86 |

---

## Related documents

- [`WINDOWS-LIVE-TEST-PLAN.md`](WINDOWS-LIVE-TEST-PLAN.md) — the primary desktop
  plan, a cross-platform parity partner, and the source of the tier discipline
  used here.
- [`LINUX-LIVE-TEST-PLAN.md`](LINUX-LIVE-TEST-PLAN.md) — the other Unix desktop
  plan, the closest sibling to this one, and the partner for byte-parity and
  POSIX-behaviour comparisons.
- [`ANDROID-LIVE-TEST-PLAN.md`](ANDROID-LIVE-TEST-PLAN.md) — companion-device
  live test plan and the origin of this tier structure.
- [`ARCHITECTURE.md`](../ARCHITECTURE.md) — layer ownership and invariants.
- [`SUPPORT.md`](SUPPORT.md) — current platform, source, and distribution limits,
  including the statement that the macOS archives are bare terminal-launched
  executables rather than `.app` bundles, and that they are unsigned and not
  notarized.
- [`CLI.md`](CLI.md) — exact CLI commands, options, output, exit codes, and the
  cross-platform byte-identical text contract.
- [`KEYBOARD.md`](KEYBOARD.md) — the keyboard and accessibility contract this
  plan reconciles against a Mac keyboard.
- [`PERFORMANCE.md`](PERFORMANCE.md) — the reproducible controlled baseline and
  the reference-machine class its numbers belong to, which is not this one.
- [`SESSION-FORMAT.md`](SESSION-FORMAT.md) — manifest, segments, raw ownership,
  recovery, and the portable archive contract.
- [`PRIVACY.md`](PRIVACY.md) and [`SECURITY.md`](SECURITY.md) — data-flow and
  untrusted-input boundaries, including the published macOS data root that A-36
  and P-02 reconcile.
- [`RELEASE-NOTES.md`](RELEASE-NOTES.md) — checksum, provenance, and the macOS
  quarantine and launch instructions that B-01 and P-13 test as written.
- [`RELEASE-CHECKLIST.md`](RELEASE-CHECKLIST.md) — the release gates this run
  satisfies, including the macOS hardware validation every release so far has
  recorded as deferred.
- [`CHANGELOG.md`](../CHANGELOG.md) — source for Tier R and version-specific
  user-visible behaviour.
- [`adr/0008-time-policy.md`](adr/0008-time-policy.md),
  [`adr/0009-continuations.md`](adr/0009-continuations.md),
  [`adr/0015-packaging.md`](adr/0015-packaging.md),
  [`adr/0021-csv-export-fidelity.md`](adr/0021-csv-export-fidelity.md),
  [`adr/0022-capture-deletion.md`](adr/0022-capture-deletion.md) — the decisions
  several assertions in §3, §5, and §10 rest on.
