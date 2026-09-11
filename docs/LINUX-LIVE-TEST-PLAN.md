# VisualCat — Linux live test plan

Manual and semi-automated verification of the shipped Linux desktop product on
real Linux installations. These are **live tests**: the exact release tarball, a
real extraction with real mode bits, a real graphical session on X11 or XWayland,
a real window manager, real POSIX permissions and filesystems, a real GPU or
llvmpipe, real `udev` rules for USB — and, where the scenario needs it, a
physical Android device over USB or Wi-Fi ADB. They complement, and never
replace, `dotnet test`.

**This plan is context-agnostic.** It assumes no previous test run, remembered
ADB serial, trusted download, extracted candidate, clean home directory, live VM
snapshot, or known data path. The artifact, host, user account, display server,
desktop environment, source files, Android device, starting state, and oracles
are established and recorded at run time. A tester can begin at §1 without facts
that exist only in a previous report, chat, or shell history.

The plan is product-specific but **machine-state-independent**. Expected product
behaviour comes from this repository; distribution, kernel, glibc, compositor,
fonts, portal backend, locale, clock, mount options, confinement policy, and
previous VisualCat data never do. When implementation, documentation, and
observation disagree, record the discrepancy. Do not silently rewrite the
expected result to match the machine.

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
| A | [Appendix A — Linux cookbook](#appendix-a--linux-cookbook) |
| B | [Appendix B — Linux traps that impersonate product bugs](#appendix-b--linux-traps-that-impersonate-product-bugs) |
| C | [Appendix C — coverage map](#appendix-c--coverage-map) |

---

## Start here — how to execute this plan

This document is a catalogue, not a demand to run every check in page order. Use
this workflow so a tester can make progress without losing release rigor:

1. **Choose the gate before touching the candidate.** Use §12 for a named
   schedule and §12.1 for change-based additions. Artifact smoke, Standard, and
   Full Linux are cumulative gates. A release needs Full Linux plus Soak and any
   conditional Upgrade/distribution-expansion rows. The ADB, Accessibility,
   Security/storage, Display-server, and Parity schedules are reusable focused
   slices: their evidence can satisfy the same Full rows when candidate,
   configuration, and oracle requirements are identical; do not execute them
   twice merely because they appear under two schedule names.
2. **Create the run record.** Fill §13.1, record this plan's repository commit,
   declare candidate capabilities per §2.10, create distinct evidence/session/
   corpus roots, and make the mutation ledger ready before changing the host.
3. **Prove the inputs.** Resolve every §2.3 token, hash the exact assets, select
   an L-state, a G-state and a D-profile, and prepare immutable independent
   oracles from §3.
4. **Pass the trust boundary first.** Run B-01/B-02/B-03 and I-01/I-12/I-14
   before an expensive, destructive, or unattended scenario. Stop on an artifact
   identity, archive-safety, mode-bit, dynamic-loader, or provenance
   contradiction.
5. **Run Basic as the product gate.** Every applicable B scenario must pass
   before relying on that workflow in A/X/U/I/P. If a prerequisite fails, mark
   dependent rows **Blocked by `<finding-id>`**; do not manufacture dozens of
   duplicate failures from one broken setup or primary path.
6. **Run selected specialist tiers.** One result row is still required per
   scenario. Shared setup, corpus, screenshots, or traces may be referenced by
   hash instead of copied, but no scenario inherits Pass implicitly.
7. **Close the loop.** Preserve the first observation, file findings, perform a
   fresh-state rerun when justified, apply §13.4, and complete §13.5 even after
   an abort, a session logout, or a host crash.

B/A/X/U/I/P scenario IDs are headings so they are available to document-outline
and screen-reader heading navigation; R guards stay in one compact table. Search
the exact ID to jump directly to a check. B cards spell out Risk/Pre/Steps/
Expect/Fail. In the compact A/X/U/I/P cards, the opening imperative is the
setup/action and every following assertion is a required pass condition. The
global §4 oracles also apply even when a card does not repeat them.

Never overwrite a failed attempt with a passing retry. Keep `attempt-01`, record
the intervention, then use `attempt-02` under a new evidence directory. A retry
can verify a fix or classify an environmental cause; it does not erase the first
result. Stop any run that crosses its recorded free-space, thermal, trace-size,
privacy, time, or restoration threshold.

---

## 1. Scope and surfaces under test

The Linux release contains two executable surfaces. A complete Linux release run
exercises both and, where applicable, exchanges data with the Windows desktop and
the Android companion. A Linux desktop result is not automatically a CLI,
Windows, or macOS result.

| Surface | What it is | Tiers |
|---|---|---|
| **Linux desktop** (`VisualCat`) | The primary Avalonia/Skia GUI on the X11 backend: file import with preview, growing-file follow, host ADB capture, analysis, session management, saves, exports, settings, and diagnostics | B, A, X, U, P, R |
| **Linux CLI** (`vcat`) | The scriptable indexing, query, verify, export, generation, and ADB-capture surface shipped in a separate tarball, and the natural Linux automation surface | I, P, parity assertions |
| **Windows desktop / CLI** | The primary release target and the cross-platform parity partner for sessions, exports, and portable archives | I only in this plan; its own live behaviour belongs to [`WINDOWS-LIVE-TEST-PLAN.md`](WINDOWS-LIVE-TEST-PLAN.md) |
| **Android companion** (`com.barebit.visualcat`) | A producer and consumer of portable sessions and a second capture surface | I only in this plan; see [`ANDROID-LIVE-TEST-PLAN.md`](ANDROID-LIVE-TEST-PLAN.md) |

### 1.1 Functional inventory to be covered

**Distribution and launch** — exact release asset and checksum provenance,
`tar.gz` archive safety and layout, preserved or restorable mode bits,
self-contained startup without a system .NET runtime, shared-library resolution
against the host's `libX11`/`libICE`/`libSM`/`fontconfig`/ICU, glibc floor,
execution from a terminal and from a user-created launcher, `noexec` and
confinement behaviour, startup arguments (`--log`, `--session`, and a bare
path), working directories, byte-exact and non-UTF-8 paths, window identity
(title, `WM_CLASS`, icon), and clean removal of the extracted directory.

**CLI automation** — deterministic five-format test-log generation, indexing,
inspection, query/search/statistics/templates, verification and export; stable
JSON/NDJSON, stdout/stderr and exit-code contracts; pipes, `SIGINT` and
`SIGPIPE`, redirection, locale independence, Unicode and byte-exact paths,
cancellation, and desktop/Windows/Android/session parity.

**Capture and import** — finite log import; the import review's format, year,
time-zone, template, and portable-raw choices, reached through *Open log* and
through *Open log with options…*; five supported logcat formats; host ADB
discovery through a distribution `adb`, an SDK `adb`, or `PATH`, with `udev`
permission states, buffers, pre-roll, duration and byte caps, format negotiation,
process-name sampling, bounded reconnect and resume; growing-file follow with
visible truncation/rotation/removal handling under real `logrotate` semantics.

**Analysis** — six-severity density timeline, minimap, zoom/pan/fit, time-range
selection, text and bounded-regex search, exact match navigation over every match
in the session, marker presence columns, severity filters, facets including
complete-value discovery through *Find…*, deterministic Drain templates,
statistics, saved views, keyset paging, load-all cancellation, exact entry
inspection, clipboard actions, and byte-faithful raw source context.

**Session lifetime** — progressive snapshots, partial and recoverable sessions,
finalize and reopen, external-source identity checks and degraded index-only
mode, temporary-session cache and retention, recent sessions, multiple tabs,
standard and portable saves, `.vcat.zip` import/export, the reviewed CSV export
and its stored row-order and encoding defaults, the shell's one cancellable file
operation, diagnostic bundle, cancellation, signal handling, crash recovery,
upgrades, and concurrent process access through the shared per-user session lease
directory.

**Linux presentation and integration** — XDG base directories, window state under
several window managers, minimum and large window sizes, X11 scaling and
`AVALONIA_SCREEN_SCALE_FACTORS`, desktop text-scaling factors, display hot-plug
through `xrandr`, multi-monitor coordinates including negative origins, taskbar
and dock identity, pointer/wheel/libinput touchpad/keyboard/touch input, focus
and modal ownership, X11 CLIPBOARD and PRIMARY selections, desktop light/dark
preference, product themes and high contrast, fonts and fontconfig fallback, IME
through ibus or fcitx5, keyboard layouts and Compose, AT-SPI and Orca, reduced
motion, portal file choosers, screen blanking and lock, suspend and resume,
session logout, and remote sessions over VNC/RDP/X11 forwarding where supported.

### 1.2 Out of scope here

- Unit, integration, benchmark, and headless UI suites except as pre-flight.
- Android companion behaviour except a portable/parity exchange.
- Windows and macOS platform chrome and packaging.
- `.deb`, `.rpm`, AppImage, Flatpak, Snap, `.desktop` entries, icon-theme
  installation, MIME and file associations, `man` pages, shell completions,
  systemd units, distribution dependency metadata, automatic updating, and
  package signing. [`SUPPORT.md`](SUPPORT.md) currently states that the Linux
  release is a tarball only. Absence of packaging-created integration is **not**
  a defect; the presence of any one of them becomes §2.10 capability
  reconciliation.
- 32-bit, ARM, RISC-V, and musl targets. Only `linux-x64` against glibc is
  published; do not infer support from a successful run under `qemu-user` or a
  compatibility layer.
- Wayland-native rendering. The application is an X11 client and runs on a
  Wayland desktop through XWayland. A defect that appears only when an
  `AVALONIA_*` variable forces a backend the release does not select is a probe
  result, not a release gate.
- Desktop drag-and-drop (XDND), `libnotify` desktop notifications, a tray or
  StatusNotifier item, and taskbar progress are not current product claims. Their
  absence is not a defect unless the candidate or its documentation adds them; any
  one that appears becomes part of §2.10 capability reconciliation and must then be
  tested for its safety and user-facing contract.
- Forensic erasure guarantees. Cache cleanup and `rm -rf` of an extracted
  directory are ordinary file deletion, not secure wipe.
- General log formats other than Android logcat.

### 1.3 Applicability and test semantics

- **Pass** means every stated expectation was observed on the identified
  artifact, host profile, graphical session, and source, with the required
  evidence.
- **Fail** means an expectation was contradicted, including a documented control
  being absent, an operation silently doing something else, or a required
  integrity check disagreeing.
- **Blocked** means no product assertion could be reached because of a named
  external condition. Retain setup evidence and identify the owner of the block.
- **N/A** is allowed only when the capability is explicitly unsupported by
  [`SUPPORT.md`](SUPPORT.md), the candidate, or absent hardware or software. A
  machine with no touchscreen can make the touch pass N/A; a missing portal
  backend makes that *specific* picker pass Blocked, not the picker N/A; a
  surprising missing command is never N/A.

Words such as *responsive*, *stable*, *correct*, *accessible*, and *graceful*
are not pass criteria alone. Each scenario using one also cites a budget, an
integrity oracle, or an observable transition from §4.

### 1.4 Linux coverage matrix

Linux is not one platform. Select hosts by dimension, not by whichever laptop is
free. The four dimensions that change product behaviour most are **display
server**, **desktop environment and window manager**, **libc and distribution
vintage**, and **filesystem and confinement policy**.

| Gate | Minimum live coverage | Important dimensions |
|---|---|---|
| Change smoke | One supported x64 host on a current Ubuntu LTS | Candidate tarball, ordinary unprivileged user, GNOME on Wayland through XWayland, 100% scale, mouse and keyboard |
| Release candidate | Two distributions of materially different vintage, for example the current Ubuntu LTS and one of Debian stable, Fedora, or Arch | Oldest supported glibc available and newest; an X11 session **and** a Wayland+XWayland session; clean home; no system .NET; ordinary desktop security policy active |
| Desktop-environment gate | GNOME plus at least one of KDE Plasma, Xfce, or a tiling WM | Different window managers, decoration policies, portal backends, dark/light preference mechanisms, and clipboard managers |
| UI/accessibility gate | One host with Orca and a desktop high-contrast theme; two materially different displays | 100/125/150/200% effective scale, small and large resolution, light/dark/high contrast, keyboard-only, screen magnifier, IME; touch and pen where claimed and available |
| ADB gate | One physical supported Android device and one Linux host with current platform-tools and correct `udev` rules | USB transport, `no permissions`/`unauthorized`/`offline` states, Wi-Fi or transport interruption where available, at least main/system/crash buffers |
| Storage gate | ext4 on a local SSD plus one materially different supported path | btrfs or XFS; a case-insensitive or permission-less mount (exFAT/vfat/NTFS-3G); a network path (NFS/SMB/sshfs) as an explicit compatibility probe; encrypted home; restrictive `umask`; quota |
| Performance and soak | Dedicated physical Linux host | AC power, fixed CPU governor, fixed display topology, controlled background indexing, sufficient storage, no competing benchmark workload |

A virtual machine is a legitimate host for most tiers and is often the only safe
one for destructive rows, but record it as a VM: the GPU is usually a virtual
adapter with software rendering, USB pass-through changes ADB behaviour, and the
clock can jump on snapshot restore. A finding seen only under virtualization must
be reproduced on metal before it is filed against rendering, timing, or USB.

If the matrix cannot be completed, execute what is available and name every
untested distribution, display server, desktop environment, scale, GPU, input,
storage, or ADB cell in the release decision. Untested cells do not become green
because another Ubuntu laptop passed.

---

## 2. Environment, artifact, and pre-flight

### 2.1 Requirements

- An x86-64 Linux host inside the support policy current at execution time. The
  repository publishes only `linux-x64`; do not infer ARM64, x86, or musl support
  from a successful run under emulation.
- An ordinary unprivileged user account for the primary run. `sudo` is
  additionally useful for kernel-level diagnostics and for `udev`, quota, and
  mount setup, but elevation must not be required for ordinary analysis.
- A graphical session. The application uses Avalonia's X11 backend and therefore
  needs a running X server or XWayland, plus the platform equivalents of
  `libX11`, `libICE`, `libSM`, and `fontconfig` — on Debian or Ubuntu,
  `libx11-6 libice6 libsm6 libfontconfig1`. Record which are present *before*
  the first launch; a missing one produces a startup failure that looks exactly
  like a product crash.
- At least **20 GB free** on the home and session filesystem for the full run,
  and **80 GB** for XL, corruption, low-space, and soak scenarios. Use a
  disposable VM, a throwaway user, or a dedicated loop-mounted filesystem for
  destructive cases.
- GNU coreutils, `tar`, `sha256sum`, `file`, `ldd`, `strace`, `lsof`, and
  `xdotool` (or an equivalent X11 automation tool) for evidence collection.
  PowerShell 7 is needed only if a repository packaging script is to be run on
  Linux; it is not needed to execute this plan.
- The exact Linux desktop and CLI release tarballs, the matching `SHA256SUMS`,
  release notes, and the GitHub build-provenance attestation, verified with `gh`
  where that tooling is available.
- For ADB tiers: current Android SDK Platform Tools, working `udev` rules and
  group membership, a data-capable USB cable or working Wi-Fi ADB, and a
  supported physical Android device whose use is authorized by its owner.
- A performance and trace path appropriate to the host: `perf` where
  `kernel.perf_event_paranoid` permits, otherwise `/proc` sampling plus an
  external high-frame-rate camera. `dotnet-counters` and `dotnet-trace` work
  against the self-contained process through its diagnostics socket but are not
  mandatory.
- A capture tool whose impact is understood. A still-screenshot utility and an
  external camera are safe defaults; a compositor screen recorder changes GPU and
  CPU load and must be labelled whenever it is used for performance evidence.

Do not run low-space, forced power loss, permission-denial, `LD_PRELOAD`,
corrupt-archive, `udev`-rule, or mass-cache-deletion tests against a personal
home directory or irreplaceable logs.

### 2.2 Pre-flight — identify the machine, session, display, and policy

Run this at the beginning of every run and after a VM restore, kernel or GPU
driver update, distribution upgrade, user switch, session-type change, or
display-topology change. Save the output rather than relying on a screenshot of
a settings panel.

```shell
# --- identity and kernel ------------------------------------------------
uname -a
cat /etc/os-release
hostnamectl 2>/dev/null || true
getconf GNU_LIBC_VERSION; ldd --version | head -1
uptime; nproc; lscpu | sed -n '1,20p'
free -h; swapon --show
lspci -k | grep -A3 -iE 'vga|3d|display' || true
systemd-detect-virt || true

# --- user, groups, limits ----------------------------------------------
id; umask; echo "$HOME"; echo "$SHELL"
ulimit -a
cat /proc/sys/vm/max_map_count /proc/sys/fs/file-max
cat /proc/sys/kernel/core_pattern
cat /proc/sys/kernel/perf_event_paranoid 2>/dev/null || true

# --- graphical session --------------------------------------------------
echo "$XDG_SESSION_TYPE / $XDG_CURRENT_DESKTOP / $DESKTOP_SESSION"
loginctl show-session "$(loginctl --no-legend | awk -v u="$USER" '$3==u{print $1; exit}')" \
  -p Type -p Remote -p Active 2>/dev/null || true
echo "DISPLAY=$DISPLAY WAYLAND_DISPLAY=$WAYLAND_DISPLAY XAUTHORITY=$XAUTHORITY"
xrandr --query 2>/dev/null | sed -n '1,20p'
xdpyinfo 2>/dev/null | grep -E 'dimensions|resolution|depth of root' || true
xrdb -query 2>/dev/null | grep -i dpi || true
glxinfo -B 2>/dev/null | sed -n '1,12p' || true
env | grep -E '^(AVALONIA|DOTNET|GDK|QT|GTK|LIBGL|MESA)_' || echo 'no renderer overrides'

# --- desktop preferences that change what the product must do -----------
gsettings get org.gnome.desktop.interface color-scheme 2>/dev/null || true
gsettings get org.gnome.desktop.interface gtk-theme 2>/dev/null || true
gsettings get org.gnome.desktop.interface text-scaling-factor 2>/dev/null || true
gsettings get org.gnome.desktop.interface enable-animations 2>/dev/null || true
gsettings get org.gnome.desktop.session idle-delay 2>/dev/null || true
gsettings get org.gnome.desktop.screensaver idle-activation-enabled 2>/dev/null || true

# --- locale, time, fonts -------------------------------------------------
locale; locale -a | wc -l
timedatectl 2>/dev/null || { date -u; cat /etc/timezone 2>/dev/null; }
readlink -f /etc/localtime
fc-match monospace; fc-match sans-serif; fc-list | wc -l

# --- storage, confinement, background load -------------------------------
findmnt -o TARGET,SOURCE,FSTYPE,OPTIONS --target "$HOME"
df -h "$HOME" /tmp /var/tmp
mount | grep -E 'noexec|nosuid|nodev' || true
quota -s 2>/dev/null || true
aa-status 2>/dev/null | head -5 || true
getenforce 2>/dev/null || true
systemctl --user list-units --type=service --state=running | head -20

# --- runtime that must NOT be required ------------------------------------
command -v dotnet && dotnet --info || echo 'no system dotnet (expected)'
```

Also record manually or with an approved inventory tool:

- each display's resolution, refresh rate, orientation, primary flag, connector
  name, and the effective scale the desktop applies. X11 exposes one global
  scale; a Wayland desktop may hand XWayland an integer scale and blur the rest.
  Record the mechanism, not only the percentage;
- the compositor and window manager, its decoration policy, whether compositing
  is enabled, and whether the session is local, virtualized, nested, VNC, RDP, or
  forwarded;
- the portal implementation present (`xdg-desktop-portal` plus which backend),
  the DBus session address, and whether a clipboard manager is running;
- input devices as `libinput list-devices` reports them, keyboard layouts, and
  the active input-method framework;
- the font packages installed, the `fc-match` result for `monospace`, and whether
  the fallback families the product names are present;
- background load that competes for I/O or CPU: file indexers
  (`tracker-miner-fs`, `baloo_file`), backup agents, `updatedb`, package-manager
  timers, antivirus (`clamd`), and cloud-sync daemons;
- power source, CPU governor, thermal condition, and whether the host throttles.

Do not disable desktop security or indexing just to make the default path pass.
Run with the ordinary policy first, then use an explicitly recorded controlled
comparison if diagnosis needs it.

### 2.3 Resolve and record every placeholder

| Token | Resolution rule |
|---|---|
| `<run-id>` | Unique path-safe identifier, normally UTC date and time plus candidate version and host label |
| `<candidate-tar>` | Absolute path to the immutable desktop `linux-x64` release archive whose hash is recorded, published as `VisualCat-Desktop-linux-x64-v<version>.tar.gz` |
| `<cli-tar>` | Matching immutable Linux CLI archive, published as `VisualCat-CLI-linux-x64-v<version>.tar.gz`; its version must equal the desktop candidate's |
| `<candidate-root>` | A fresh absolute extraction directory created for this run; never `$HOME`, the repository, or the download directory |
| `<VCAT>` | Exact `<candidate-root>/VisualCat` path |
| `<VCAT-CLI>` | Exact extracted matching candidate `vcat`; a surface under test and a cross-check, never the sole correctness oracle |
| `<evidence-root>` | Dedicated absolute directory outside the product's session and cache directories |
| `<data-home>` | `${XDG_DATA_HOME:-$HOME/.local/share}` as it resolves for the account under test; prove it rather than assuming `$HOME/.local/share` |
| `<session-root>` | Product session root discovered from the UI or settings; default currently `<data-home>/VisualCat/Sessions` |
| `<settings-path>` | Current product settings file; default currently `<data-home>/VisualCat/settings.json` |
| `<diagnostics-root>` | Default currently `<data-home>/VisualCat/Diagnostics` |
| `<lease-root>` | Cross-process session lease directory, currently `<data-home>/VisualCat/SessionAccess-v1` |
| `<adb>` | Exact `adb` binary selected by the scenario; record its absolute path, package origin, version, and hash |
| `<serial>` | Exact authorized Android transport selected from `adb devices -l`, re-proved after every disconnect |
| `<corpus-root>` | Dedicated generated test-data directory with recorded hashes and an oracle manifest |
| `<display>` | The exact `DISPLAY` and `XAUTHORITY` pair the product runs under, read from a live session process rather than assumed |

Expand tokens before running a command. A command containing an unresolved
`<...>` token is a setup error, not evidence. Quote every path (`"$VCAT"`), use
`--` before filename arguments, and never build a shell command by concatenating
a file name, device serial, or log content into a string. Linux file names may
legally contain spaces, newlines, quotes, `*`, and byte sequences that are not
valid UTF-8; a harness that breaks on one of those is a harness defect, not a
product finding.

### 2.4 Candidate acquisition, provenance, extraction, and identity

The exact uploaded tarball is the release authority. A source build can diagnose a
finding but cannot make the uploaded bytes pass. A tarball produced on a Windows host
is **not** a substitute: it loses every executable bit, which reproduces as a fake
permission defect (Appendix B). The repository says this itself —
[`RELEASE-CHECKLIST.md`](RELEASE-CHECKLIST.md) states that cross-built Unix tarballs
made on Windows are layout checks only, because Windows cannot faithfully create or
validate Unix executable mode bits, and that **the Linux and macOS workflow runners are
authoritative for permissions**. That checklist also requires at least one Unix artifact
to be tested on a clean machine; this plan is how that requirement is met for Linux.

| Artifact | Purpose | Release authority? |
|---|---|---|
| Debug or source run (`dotnet run`) | Inspect exceptions and iterate quickly | No |
| Local Release publish from `tools/package.ps1` on a Linux host | Rehearse layout and early smoke | No, unless byte-identical to the uploaded candidate and provenance says so |
| Local publish archived on Windows | Diagnosis only | **No** — the script itself documents that Unix mode bits are authoritative only when the archive is created on Unix |
| Exact `VisualCat-Desktop-linux-x64-v<version>.tar.gz` | Linux desktop release decision | Yes |
| Exact `VisualCat-CLI-linux-x64-v<version>.tar.gz` | Shipped CLI decision and desktop cross-check, paired with independent or previously trusted oracles | Yes for CLI and integration rows |

Before extraction:

```shell
stat -c '%n %s %y %a %U:%G' -- '<candidate-tar>'
sha256sum -- '<candidate-tar>'
grep -F -- "$(basename '<candidate-tar>')" SHA256SUMS
sha256sum -c --ignore-missing SHA256SUMS
getfattr -d -m - -- '<candidate-tar>' 2>/dev/null || true
file -- '<candidate-tar>'
gh attestation verify '<candidate-tar>' --repo benny-cz/VisualCat   # where gh is available
```

Compare SHA-256 against the matching `SHA256SUMS` line and, when present, verify
the GitHub build-provenance attestation. Record the release URL, tag, commit,
asset size, archive hash, checksum-file hash, attestation result, download
method, and any extended attributes the download tool attached. A checksum served
beside the asset detects corruption; provenance is the stronger origin oracle.

List archive members before execution and reject absolute paths, `..`
components, symlinks or hard links pointing outside the archive, device or FIFO
entries, setuid or setgid bits, world-writable modes, duplicate or case-colliding
names, and wrapper-directory surprises:

```shell
tar -tvzf '<candidate-tar>' | tee '<evidence-root>/<run-id>/archive-listing.txt'
tar -tzf  '<candidate-tar>' | grep -E '^/|(^|/)\.\./' && echo 'UNSAFE MEMBER'
tar -tvzf '<candidate-tar>' | awk '$1 ~ /^[lh]/ || $1 ~ /[st]/ {print "REVIEW: " $0}'
```

Extract into a fresh directory with GNU `tar`, then record the full inventory,
mode bits, and hash set:

```shell
mkdir -p '<candidate-root>' && tar -xzf '<candidate-tar>' -C '<candidate-root>'
find '<candidate-root>' -printf '%M %8s %p\n' | sort -k3 > '<evidence-root>/<run-id>/extract-inventory.txt'
find '<candidate-root>' -type f -exec sha256sum {} + | sort -k2 > '<evidence-root>/<run-id>/extract-hashes.txt'
ls -l '<candidate-root>/VisualCat'
```

The desktop archive root must contain `VisualCat`, `LICENSE`,
`THIRD-PARTY-NOTICES.md`, and `README.txt`; the CLI archive must contain `vcat`
and the same three files. The README and the visible application identity must
name the candidate version. No separate .NET installation may be required.

`VisualCat` and `vcat` must extract with the executable bit set when the archive
was produced on Linux by the release workflow. If they do not, determine first
whether the archive or the extraction tool dropped it — GNOME Files, Ark,
`unar`, `bsdtar`, and a restrictive `umask` all behave differently — and record
which, before filing anything. The README's `chmod +x` line is a documented
remedy for a lossy extractor, not an admission that the shipped bit is expected
to be missing.

After launch, record:

```shell
sha256sum -- '<VCAT>' '<candidate-root>/vcat' 2>/dev/null
file -- '<VCAT>'
ldd -- '<VCAT>'; ldd '<candidate-root>'/*.so 2>/dev/null | grep -i 'not found' || echo 'all resolved'
pid=$(pgrep -x VisualCat); echo "$pid"
readlink -f /proc/"$pid"/exe
ps -o pid,ppid,user,etime,rss,vsz,nlwp,args -p "$pid"
tr '\0' '\n' < /proc/"$pid"/environ | grep -E '^(DISPLAY|XAUTHORITY|XDG_|LANG|LC_|TZ|DOTNET_|AVALONIA_)'
```

When more than one process has that name, bind every later sample to the recorded
PID **and** `/proc/<pid>/exe`, not to the name alone.

### 2.5 Execution states — test dimensions, not setup shortcuts

| State | How to produce | Expected product behaviour |
|---|---|---|
| **L0 — ordinary verified portable run** | Download the exact tarball, verify checksum and provenance, extract with GNU `tar` into a user-writable directory on the home filesystem, launch from a terminal | Starts as an unprivileged user; no package installation, elevation, or system .NET required; identity and notices match the candidate |
| **L1 — clean home, no VisualCat data** | New local test user, or a validated backup-and-move of exactly `<data-home>/VisualCat` | First launch has no stale settings, sessions, leases, or diagnostics, and creates nothing outside the declared XDG locations |
| **L2 — preserved home, upgrade** | The previous supported release's genuine settings, sessions, saved views, and an interrupted session in place; start the candidate from a different extraction directory | Data is migrated or read compatibly; the candidate does not rewrite old data merely by listing it; rollback risk is documented |
| **L3 — restrictive destination** | Run from a readable directory while the session or export target is denied (`chmod 500`), read-only remounted, quota-exhausted, or on a full filesystem | Launch still works if its own directory is readable and executable; each denied write fails visibly and safely; no `sudo` prompt and no silent fallback to another directory |
| **L4 — alternate path and filesystem topology** | Candidate and corpora under spaces, Unicode, a non-UTF-8 byte sequence, a very deep path, a second filesystem (btrfs/XFS/ZFS), a permission-less mount (exFAT/vfat/NTFS-3G), a network mount (NFS/SMB/sshfs), and an overlay or FUSE mount, as separate passes | Supported local paths work; unsupported or unstable storage fails honestly without corrupting the source or the cache; a case-insensitive mount does not silently merge two sessions |
| **L5 — display-server and scaling variation** | The G-states in §2.6 | Layout, hit testing, focus, dialogs, and rendering track the session the product actually got; no off-screen modal, stale scale, or unreachable window |
| **L6 — interrupted desktop session** | Minimize, unmap, lock, blank, switch virtual desktop, disconnect a remote session, suspend and resume, and log out during capture or import, as separate passes | Acquisition and visible-refresh semantics match §6 and §7; transport loss is surfaced; committed data remains recoverable; the `SIGTERM` at logout is handled, not merely survived |
| **L7 — background contention** | File indexer, backup agent, `updatedb`, package-manager timer, or antivirus active; a controlled test process briefly opens a new manifest or destination | Bounded retries tolerate transient contention; cancellation stays prompt; a persistent condition produces a precise failure rather than a hang |
| **L8 — concurrent processes** | Two candidate instances under one account with distinct sources, then one shared-session conflict probe, then a `vcat` process against the same session | No settings, session, or lease corruption, no cross-instance tab confusion, no unsafe deletion; unsupported simultaneous writes are refused through the lease directory |
| **L9 — confined or hardened host** | AppArmor enforcing, SELinux enforcing, `noexec` on the extraction filesystem, `fs.protected_symlinks` and `fs.protected_regular` on, hardened `umask 077`, restricted unprivileged user namespaces | Either the product runs normally, or it fails with a message that names the actual restriction; it never proposes weakening host policy as the first remedy |

These states do not authorize weakening machine security. When a state requires a
policy change, use a dedicated machine, record the original value, and restore it.

### 2.6 Graphical-session states

The display server is the single largest source of Linux-only behaviour. Record
which state produced every UI observation; a screenshot without its G-state is
not comparable to another host's.

| State | How to produce | What it tests |
|---|---|---|
| **G0 — X11 session, single display, 100%** | Log in to an "on Xorg" session; one monitor at its native resolution and scale 1 | The reference environment for every visual baseline |
| **G1 — Wayland session, XWayland client** | Log in to the default Wayland session; the product runs as an XWayland client | The default on a current Ubuntu or Fedora desktop; scaling, clipboard, screenshots, and automation all behave differently here |
| **G2 — HiDPI and fractional scaling** | 150%, 175%, and 200% desktop scale under both G0 and G1; separately set `AVALONIA_SCREEN_SCALE_FACTORS` and a desktop `text-scaling-factor` | X11 has one global scale; XWayland may give the client an integer scale and let the compositor blur the rest. Record the mechanism |
| **G3 — multi-monitor** | Two outputs with different resolution, refresh, orientation, and scale; the secondary placed left of or above the primary so coordinates go negative; change the primary during a run | Window placement, dialog ownership, hot-plug, and coordinate handling |
| **G4 — alternate window manager** | KDE Plasma (KWin), Xfce (Xfwm), and one tiling WM (i3, or Sway through XWayland) | Decoration policy, minimize and iconify, maximize, always-on-top, modal ownership, taskbar identity, and forced tiling of a window that declares a minimum size |
| **G5 — software rendering** | `LIBGL_ALWAYS_SOFTWARE=1`, a VM's virtual adapter, or a host with no GPU driver | Skia's fallback path, timeline redraw cost, and whether the product describes the situation honestly |
| **G6 — remote session** | X11 forwarding over SSH, VNC, `xrdp`, or a nested server (`Xephyr`) | Latency tolerance, clipboard across the transport, and reconnect behaviour. A remote session is a compatibility probe, never a performance baseline |
| **G7 — headless or hostile session** | No `DISPLAY`; a `DISPLAY` that refuses the connection; a missing or stale `XAUTHORITY`; a host without `libX11` installed | The failure must name what is missing and where to get it. A bare unhandled exception dumped to stderr is a finding |

Record for every G-state: session type, compositor, window manager, output list
with scale and refresh, the `AVALONIA_*` environment actually in effect, the
renderer in use, and whether the screen blanks or locks during long runs.

### 2.7 Starting data profiles

| Profile | Contents | Use |
|---|---|---|
| **D0 — preserved** | The account's existing `<data-home>/VisualCat`, untouched | Read-only discovery only; never for destructive tiers |
| **D1 — clean** | No `<data-home>/VisualCat` and a fresh candidate extraction | Cold-start and privacy baseline |
| **D2 — seeded** | Known sessions: complete, interrupted, portable, external-source, and a corrupted copy; known settings and saved views | Most A, U, and I scenarios |
| **D3 — upgrade** | The previous supported release's genuine on-disk data and settings | A-29 and the release gate |
| **D4 — pressure** | Dedicated filesystem or account prepared for low space, high session count, contention, permission, and crash matrices | X and P only |

Moving or deleting `<data-home>/VisualCat` removes settings, cached sessions,
leases, and diagnostics. That is destructive. Resolve the exact path, prove it is
the expected child of the resolved `XDG_DATA_HOME`, stop every `VisualCat` and
`vcat` process, take a recoverable backup when required, and record the action.
`rm -rf "$XDG_DATA_HOME/VisualCat"` with `XDG_DATA_HOME` unset deletes
`/VisualCat`, or nothing, or something else entirely; always resolve first and
never expand a possibly unset variable into a recursive removal.

### 2.8 Mutation ledger and guaranteed restoration

Before any mutation, append a ledger row:

```text
Timestamp UTC | Scenario | Host/user | Setting/path/device | Original value/state |
New value/state | Exact restoration | Owner | Restored evidence
```

Ledger at minimum: the VisualCat data directory; extracted candidates; generated
corpora; environment variables including `PATH`, `HOME`, `TMPDIR`, `XDG_*`,
`LD_*`, `DOTNET_*`, `AVALONIA_*`, `ANDROID_SDK_ROOT` and `ANDROID_HOME`, `TZ`,
and `LANG`/`LC_*`; ADB server and device state, USB authorization, `udev` rules
and group membership; file modes, ACLs, and ownership; `umask`; mount options,
loop and LVM devices, quotas, and bind mounts; system clock and time zone; locale
generation and IME configuration; display scale, resolution, orientation,
refresh, and primary output; desktop theme, contrast, animation, text scale,
idle/blank/lock timers, and accessibility services; CPU governor and power
settings; `sysctl` values such as `vm.max_map_count`, `kernel.core_pattern`, and
`kernel.perf_event_paranoid`; `ulimit` values; AppArmor
or SELinux mode; firewall rules; test users; and network and proxy state.

Every destructive scenario owns its cleanup even when it fails. Do not start a
new destructive scenario while an earlier ledger row has no plausible
restoration.

### 2.9 Destructive-scenario register

| Scenario family | Risk | Required containment |
|---|---|---|
| Clean home or upgrade reset | Deletes settings, sessions, leases, diagnostics | Validated path under the resolved `XDG_DATA_HOME` and a recoverable backup |
| Low disk, quota, huge corpus | Home or root filesystem exhaustion; an unbootable host if `/` fills | Dedicated loop-mounted filesystem, LVM volume, or VM disk; hard abort threshold; never `/` |
| Permission, ACL, and mount tests | Access loss, an unexpected target, a home directory left unreadable | Dedicated subtree; capture `getfacl` and `stat` first; never `chmod -R` a home root |
| Corruption and archive bombs | CPU or disk exhaustion, unsafe extraction | Generated copies only; size and time limits; dedicated filesystem |
| Signal, kill, and power interruption | Partial sessions, orphan `adb`, unsaved work | Synthetic data; dedicated host or VM; a scenario-specific recovery oracle |
| Cache deletion | Irrecoverable removal of temporary sessions | Seeded disposable cache only; verify protected and open sessions first |
| Clock, locale, display, power, and `sysctl` changes | Affects the whole session or the whole host | One change at a time; record the exact original; restore immediately |
| ADB buffer, server, device, or `udev` mutation | Disrupts IDEs, other users, and other devices; a bad rule can hide every USB device | Dedicated device and host; serial-qualified commands; keep a copy of the original rules file; reload and verify |
| `LD_PRELOAD` and loader-integrity probes | Malware-like or policy-sensitive execution | Isolated VM; inert, hash-recorded probe library; owner approval |
| `systemd-oomd` and cgroup pressure | Can kill unrelated user services or the graphical session | Dedicated VM; scope the limit to a transient unit, never to the session slice |

### 2.10 Run-time capability and claim manifest

Before selecting N/A rows, create a short capability manifest beside the run
header. A control being absent is an observation, not proof that the capability
was never promised. Reconcile the exact candidate, its bundled `README.txt`,
release notes, [`SUPPORT.md`](SUPPORT.md), and the release announcement.

| Claim family | Record at run time | Applicability consequence |
|---|---|---|
| Linux platform | Claimed distributions, x64-only status, glibc floor, X11/XWayland requirement, VM and remote-session limits | Selects §1.4 cells; an advertised but unavailable host cell is a coverage gap, not Pass |
| Distribution | Tarball only, unsigned, self-contained runtime, and the explicit absence of `.deb`/`.rpm`/AppImage/Flatpak/`.desktop`/dependency metadata | Drives B-01/B-02/B-16, A-28, I-12, P-13, P-18; any unexpected integration is tested, not ignored |
| Runtime dependencies | The exact shared libraries the candidate needs from the host, the ICU and globalization mode, and the documented package names | A missing documented dependency that produces an unexplained failure is Fail; a dependency present but undocumented is a documentation finding |
| Sources | Finite import, growing follow, host ADB, supported formats, buffers, reconnect | A missing advertised source is Fail; an unavailable physical device may Block only the hardware-dependent attempt |
| Data exchange | Standard and portable sessions, `.vcat.zip`, CSV, and matching CLI, Windows, and Android exchange | Identifies mandatory I rows and their exact verification oracles |
| Desktop integration | Window icon, `WM_CLASS`, title, clipboard, portal file chooser, browser launch for the releases page | Determines U-17, U-19, U-26, A-25, A-28; an absent integration must be reconciled against the claim, not assumed optional |
| Optional hardware and software | Touch, pen, precision touchpad, multiple displays, GPU acceleration, Orca, IME framework, portal backend, network and removable mounts | N/A requires an explicit unsupported claim or a recorded absence; do not generalize one probe result to all Linux hosts |
| Diagnostics and network | Structured diagnostics setting, diagnostic bundle, telemetry, update and network statements | Determines P-01 to P-03 and P-15, and whether any connection is expected after an explicit action |

For each row record one state — `claimed`, `explicitly unsupported`, `not
documented`, or `present but unclaimed` — plus the evidence location and the
scenario effect. A documented feature missing from the candidate is Fail. A
visible unclaimed feature must be tested for its safety and user-facing contract,
or the release is Blocked until the plan and documentation account for it.
Product source can explain a mismatch but cannot overrule the shipped user
contract during a release run.

---

## 3. Test data preparation

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
( cd "$C" && sha256sum -- * | tee SHA256SUMS.corpus )
( cd "$C" && for f in *.txt; do printf '%s bytes=%s lines=%s lf=%s cr=%s\n' \
    "$f" "$(wc -c <"$f")" "$(wc -l <"$f")" \
    "$(tr -dc '\n' <"$f" | wc -c)" "$(tr -dc '\r' <"$f" | wc -c)"; done )
```

Count carriage returns with `tr -dc '\r' < file | wc -c`. Do **not** test for
CRLF with `grep $"\r"`: in bash, `$"..."` is locale-translation syntax, so that
pattern matches any line containing the letter *r* and makes every file look like
CRLF. That exact mistake has already manufactured a false cross-platform
line-ending finding against this product.

Also prepare the repository's own fixtures, which are versioned, sanitized, and have
known shapes, so they cost nothing and cannot drift with a generator change:

| Fixture | What it is |
|---|---|
| `samples/logcat_supersmall.txt` | The smallest bundled sample; a fast detection and first-paint check |
| `samples/logcat_small.txt` | The bundled deterministic sample referenced by the other plans |
| `samples/logcat_large.txt` | A bundled larger sample for a quick interactive check without generating a corpus |
| `test-data/golden-formats.txt` | The golden parser fixture; the authority for format detection and per-format parsing |

Read `samples/README.md` for what each file is meant to demonstrate, and hash all four
into the corpus manifest so a run states which revision of them it used. They supplement
the generated corpora; they do not replace the exact-count oracles below.

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
never emits. Build them by composing its output so they remain reproducible from
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
    "$(wc -c < mixed-formats.txt)" "$(wc -c < "fmt-$fmt.txt")"
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
printf 'crash block starts at byte %s\n' "$(wc -c < crashy.head)" | tee crashy.offset.txt
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

sha256sum -- *.txt | tee -a SHA256SUMS.corpus
chmod a-w -- *.txt      # originals are read-only; mutate disposable copies only
```

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
mutate disposable copies only. Several of these exercise things only a POSIX
filesystem permits, and those are the rows a Windows run cannot cover at all.

| File | Required property / oracle |
|---|---|
| `empty.txt` | Zero bytes; one clear empty-source outcome |
| `notalog.bin` | Binary input; refused or fully accounted, never invented entries |
| `crlf.txt`, `lf.txt`, `mixed-eol.txt`, `cr-only.txt` | Exact byte offsets across every newline form, including lone CR |
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
| `unicode-name-😀-é-中文.txt` | Normalization and display-name handling; NFC and NFD spellings as separate files |
| `name with spaces, 'quotes' and $dollar.txt` | Shell-hostile but legal name; must never be word-split or expanded by the product or its own child processes |
| `name-with-newline.txt` (literal `\n` in the name) | A name no Windows filesystem accepts; pickers, notices, export names, and diagnostics must not corrupt or truncate it |
| `latin1-name.bin` (byte `0xE9` in the name) | A path that is not valid UTF-8; the product must display, reopen, and export it without invention |
| `deep/.../log.txt` | Component under 255 bytes but total path beyond 4096 where the filesystem permits |
| `Session.vcat` / `session.vcat` pair | Two names differing only in case, on a case-sensitive filesystem |
| `archive-traversal.vcat.zip` | `..`, absolute, drive-letter, symlink, and hard-link entries; duplicate and case-colliding paths; entries with POSIX mode bits including setuid |
| `archive-bomb.vcat.zip` | Declared and compressed size disproportion bounded before exhaustion |
| `archive-fifo.vcat.zip` | A FIFO or device entry; must be refused, not created |
| session-corrupt copies | Manifest, schema, checksum, column, bitmap, and raw-source faults, one at a time |

Build them from one recipe, not by hand, so that a corpus disagreeing with its
manifest is recognizable as a harness fault:

```shell
C='<corpus-root>'; cd "$C"

# --- newline forms, encoding, truncation ---------------------------------
cp small.txt lf.txt
sed 's/$/\r/' lf.txt > crlf.txt
tr '\n' '\r' < lf.txt > cr-only.txt
{ head -n 200 crlf.txt; tail -n +201 lf.txt; } > mixed-eol.txt
printf '\xEF\xBB\xBF' > bom.txt && cat lf.txt >> bom.txt
head -c "$(( $(wc -c < medium.txt) / 2 + 37 ))" medium.txt > truncated.txt
printf 'truncated at %s bytes\n' "$(wc -c < truncated.txt)" >> truncated.oracle.txt
head -c -1 lf.txt > nofinalnewline.txt
head -c 1000 lf.txt > nonutf8.bin && printf '\x80\xC3\x28\xFE\xFF' >> nonutf8.bin && cat lf.txt >> nonutf8.bin
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

# --- name and path topology (Linux-only rows) ----------------------------
cp lf.txt 'unicode-name-😀-é-中文.txt'
cp lf.txt "$(printf 'nfd-name-e\xCC\x81.txt')"
cp lf.txt "name with spaces, 'quotes' and \$dollar.txt"
cp lf.txt "$(printf 'name-with\nnewline.txt')"
cp lf.txt "$(printf 'latin1-name-\xE9.bin')"
deep=$(python3 - <<'PY'
print('/'.join('d%03d' % i for i in range(1, 60)))
PY
); mkdir -p "deep/$deep" && cp lf.txt "deep/$deep/log.txt"
printf 'deep path length: %s\n' "$(printf '%s' "$PWD/deep/$deep/log.txt" | wc -c)"
```

Keep a second copy of every hostile-name file on an ordinary-name path so a
failure can be attributed to the name rather than to the content. When the
filesystem under test cannot hold a name (vfat rejects `*`, `?`, `:`; a
case-insensitive mount cannot hold the case pair), record that as a host property
and run the row on a filesystem that can.

Damage session copies one fault at a time, against a session that has already
verified clean, and record the exact byte or field changed:

```shell
cp -a '<session-root>/<clean-session>' ./session-fault-manifest
#   manifest   truncate manifest.json mid-object
#   schema     raise manifest.json formatVersion to an unsupported major
#   checksum   edit one recorded digest in segments/000001/checksums.json
#   column     flip one byte in a segment column such as level.bin or pid.bin
#   bitmap     flip one byte under segments/000001/bitmaps
#   raw        flip one byte inside raw.log, leaving its length unchanged
#   mode       chmod 000 one segment file, and separately chmod 000 the directory
#   symlink    replace one segment file with a symlink to /etc/passwd
```

The last two rows have no Windows equivalent and are mandatory here: a session
whose segment has become unreadable, or has been replaced by a link out of the
session tree, must fail a verify and must never be followed outside the session
root. Inspect the candidate's own session layout first — segment and column file
names belong to the format version under test, not to this plan. See
[`SESSION-FORMAT.md`](SESSION-FORMAT.md).

### 3.3 Growing-file producer

A growing-file test needs a producer whose ledger is the oracle, not a guess
about what the product should have seen. Append with a recorded UTC timestamp per
flush, and keep the producer's own log beside the session evidence.

```shell
SRC='<corpus-root>/growing.txt'; LEDGER='<evidence-root>/<run-id>/producer.ledger'
cp '<corpus-root>/quiet-live-seed.txt' "$SRC"
i=0
while [ "$i" -lt 3000 ]; do
  i=$((i + 1))
  line="05-15 14:20:00.000  1073  1151 I VCatGrow: RUN=<run-id> seq=$i"
  printf '%s\n' "$line" >> "$SRC"
  printf '%s append seq=%s bytes=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%S.%NZ)" "$i" \
    "$(wc -c < "$SRC")" >> "$LEDGER"
  sleep 0.2
done
```

Vary the producer deliberately across passes: a partial line flushed without its
newline and completed seconds later; a long idle window followed by a burst; a
writer that exits between appends; and, for A-21, the three ways a real Linux log
actually changes shape — `truncate -s 0` in place, `mv` plus a fresh file (what
`logrotate` does without `copytruncate`), and `logrotate --force` against a real
configuration file on a dedicated host. On Linux the product can keep reading an
unlinked file through its open descriptor; whether it should is a product
contract, so record what it does and compare it with what the notice claims.

### 3.4 ADB traffic and loss oracle

Traffic must be attributable to the run and must have an independent count.
Emit run-specific begin and end markers through the device's own `log` binary,
and keep the requested count as an input only — Android rate limiting and
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

---

## 4. Evidence, budgets, and instrumentation

### 4.1 Evidence for every scenario

1. **Run boundary** — `<run-id>`, scenario ID, host and user identity, local and
   UTC start and end, candidate archive and executable hashes, PIDs, the G-state,
   and the exact command or source.
2. **Before and after screenshots** at assertion moments. Capture all outputs
   when off-screen placement or scaling is relevant, and retain native pixel
   dimensions. Record which tool produced them: a compositor screenshot, an X11
   `import`/`scrot` capture, and a hypervisor framebuffer grab do not see the
   same thing, and only the last one is immune to what covers the window.
3. **Video or trace** for ordering, animation, resizing, input latency, hangs, or
   focus. State the recorder, resolution, fps, dropped frames, and likely
   overhead.
4. **Visible product text verbatim** — title and version, status, notices,
   counts, source identity, progress and final state, errors, selected scope, and
   file name.
5. **Product artifact** — the session directory or a portable copy, plus CLI
   `verify`, the manifest, the export, and a raw-source hash as applicable. Never
   mutate the only copy while collecting evidence.
6. **Process and resource samples** — PID and `/proc/<pid>/exe`, CPU, RSS and
   PSS, open descriptors, mapped regions, threads, child `adb` processes, I/O,
   free space, power state, display topology, and GPU or thermal context at
   scenario-defined points.
7. **Host failure evidence** — time-bounded `journalctl` output for the user and
   system units, `coredumpctl` or `apport` records, the process exit status or
   terminating signal, `dmesg` for OOM and USB events, and `strace`/`perf`
   evidence for lock, syscall, or I/O cases.
8. **Product structured diagnostics** when sequencing matters. *Appearance &
   timeline* carries *Write redacted structured diagnostics*; inspect and record
   its current value, enable it when the scenario requires it, collect
   `<diagnostics-root>/visualcat-*.jsonl` alongside the session, hash it, and restore the
   prior value. Those records are the best available oracle for ingest ordering:
   `ingest.detected`, `store.snapshot` with its `snapshotGeneration` and
   `timedEntries`, and `ingest.ready` turn "the numbers look wrong" into "the
   view stopped at snapshot generation 2 of 5".
9. **External oracle** — the corpus manifest, the producer ledger, ADB markers,
   trusted CLI output, file hashes and byte ranges, the AT-SPI tree, or a trace
   query.

Store evidence under `<evidence-root>/<run-id>/<scenario-id>/` with an index and
a SHA-256 list. Logs, sessions, screenshots, clipboard captures, core dumps, and
traces can contain source payloads, user names, paths, serials, tokens, and
account data. Restrict access, use synthetic input, review before sharing, and
delete according to the run retention policy.

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
every discarded run. Keep distribution, kernel, candidate hash, CPU governor, AC
state, background-indexing policy, display topology and scale, renderer, G-state,
and corpus constant. Flag a >20% median or p95 regression against a like-for-like
accepted baseline even when the absolute gate passes.

The controlled harness gates in [`PERFORMANCE.md`](PERFORMANCE.md) remain authoritative
wherever their corpus, reference-machine class, and measurement path apply. Its
reference machine is a Windows x64 workstation, so on Linux report the absolute live
result **and** the delta against a Linux baseline of the same class rather than silently
restating a Windows figure as if it had been reproduced here.

There is already a second Linux data point, and it must not be confused with either: that
document also records a scheduled **GitHub-hosted Linux** artifact over a 100,000-line
smoke corpus. Hosted runners are slower and more variable than the reference machine
because their CPU and storage are shared, and the document is explicit that this is a
regression signal rather than a product gate. So a live Linux run may compare its shape
against the hosted distribution, but may neither fail a candidate against a
shared-runner floor nor pass one by pointing at a hosted artifact. Name which of the
three measurement paths — reference machine, hosted runner, live Linux host — produced
every number.

The live UI budgets below add first-paint, input, redraw, and exact-asset coverage; they
never replace a stricter published engine gate.

| Signal | Starting budget | Measurement |
|---|---|---|
| Cold launch to usable empty state | median ≤3 s; no run >5 s, after a separate first-run measurement | Wall clock from exec to a drawn empty state on video; process start alone is not first usable frame |
| First-ever launch on a fresh host | Measured and reported separately, never discarded | Includes page-cache cold reads of the self-contained runtime, fontconfig cache building, and shader or GL initialization |
| Warm launch | median ≤1.5 s; p95 ≤2.5 s | Close normally, relaunch from the same extraction and home |
| Input → visible acknowledgement | p95 ≤100 ms; none >250 ms for local commands | High-fps external camera, or X11 event timestamps correlated with present evidence |
| Open or close an owned dialog or the More menu | ≤250 ms to settled state | Video |
| Close a dialog whose background work is still running | Acknowledged ≤250 ms; closed or truthfully `Cancelling…` ≤2 s; the child process is gone before the next assertion | Video plus `/proc` observation; a dialog that outlives its own child is a finding |
| Resize, move between outputs, or change scale | No freeze >500 ms; no stale-scale frame persisting >1 s | Video plus `xrandr` change timestamps |
| Import preview for an ordinary small or medium file | ≤1 s after the picker returns | Stopwatch or video; exclude portal dialog time and say so |
| First heat map after import starts | ≤3 s | Product progress plus video |
| Sustained one-million-line import | Within 20% of the accepted Linux live baseline and never below 30,000 full-pipeline lines/s on the reference class | Wall clock and manifest counts; report harness and live UI paths separately; exclude preview time explicitly |
| Search over 1 M entries | first result ≤1.5 s | Stopwatch from the Enter or Ctrl+F action |
| Full-view 2,000-column heat-map query | Structured-diagnostics p95 ≤20 ms; the UI never freezes >500 ms | Structured diagnostics plus the matching benchmark result; do not substitute one measurement path for the other |
| Timeline pan and zoom | No freeze >250 ms; ≤15% missed frames at the recorded refresh rate; p95 no worse than 20% over baseline | §4.3.1 procedure; a zero-frame measurement is Blocked |
| Entry page (`Load 500 more`) | ≤400 ms | Video and the status or count |
| Reopen a finalized session of ≤1 M entries | median ≤5 s; no run >10 s | From the command to a correctly drawn plot and final count, 3 runs |
| ADB discovery | initial list ≤5 s; a 2 s refresh reflected within one interval | Dialog video and ADB trace |
| ADB or growing source, first complete line | visible within 2 s of a producer flush once capture is running | Producer UTC ledger and product video |
| Stop → sticky acknowledgement | ≤250 ms | Video |
| Stop → saved | No fixed absolute cap; the elapsed indicator must advance and the stage must remain truthful | Total by entries and bytes, compared with baseline |
| Live capture UI refresh | Bounded by the *Live UI refresh limit (Hz)* setting in *Appearance & timeline*; no busy loop at any permitted value | CPU sampling at the lowest and highest allowed value, plus the §4.3.1 frame measurement |
| Minimized or unmapped live view | No continuous redraw or query cadence; acquisition continues | CPU sampling plus manifest progress before, during, and after minimize |
| Idle growing-file follow | RSS and the managed heap settle; post-warm-up growth ≤32 MiB/hour and no sustained gen2 cadence attributable to polling | 15-minute samples plus GC counters |
| Memory at 1 M entries | No OOM kill; peak RSS ≤25% over the comparable accepted baseline, with mapped session segments accounted separately | Fixed progress samples from `/proc/<pid>/status` and `smaps_rollup` |
| Open descriptors and mapped regions | Both plateau; neither approaches `ulimit -n` or `vm.max_map_count` during ordinary work | Count the entries in `/proc/<pid>/fd` and the lines in `/proc/<pid>/maps` at fixed points |
| Soak resources | No sustained post-warm-up positive slope in RSS, descriptors, threads, mapped files, or latency | Rolling-window medians at least every 15 minutes |
| Crash, hang, OOM kill, core dump | Zero attributable events in the scenario window | `journalctl`, `coredumpctl`, `dmesg`, and the process exit status or signal |

For leak claims, define warm-up, cadence, workload, and comparison windows before
starting. Memory-mapped segments, font and shader caches, and loaded rows can grow
legitimately. Fail growth that does not plateau, exceeds the accepted envelope,
and is corroborated by increasing retained mappings, descriptors, or threads,
worsening latency, or exhaustion risk. A single rising line in a system monitor is
not a leak oracle, and on Linux RSS in particular counts mapped file pages the
kernel may reclaim at any time.

### 4.3 Linux instrumentation

Bind every sample to the recorded PID. A name-only sample mixes two instances,
and `pkill -f` on a pattern that appears in your own command line kills the
sampling shell instead of the target — use `pkill -x VisualCat`, or the PID.

```shell
pid='<pid>'
ps -o pid,ppid,user,stat,etime,time,rss,vsz,nlwp,args -p "$pid"
grep -E 'VmRSS|VmHWM|VmSize|VmSwap|Threads|FDSize' /proc/$pid/status
cat /proc/$pid/smaps_rollup 2>/dev/null | grep -E '^(Rss|Pss|Private|Swap)'
ls /proc/$pid/fd | wc -l ; wc -l < /proc/$pid/maps
cat /proc/$pid/io
top -b -n 1 -H -p "$pid" | head -25
pidstat -h -r -u -d -p "$pid" 1 5 2>/dev/null || true
lsof -p "$pid" | awk '{print $5}' | sort | uniq -c | sort -rn | head
ss -tanp 2>/dev/null | grep -F "pid=$pid" || echo 'no sockets'
```

Time-bound host failure evidence to the recorded scenario interval:

```shell
journalctl --since '<scenario-start-local>' --until '<scenario-end-local>' -b \
  | grep -iE 'visualcat|oom|segfault|traps|usb|xwayland|gnome-shell' || true
journalctl --user --since '<scenario-start-local>' -n 200 --no-pager
coredumpctl list --since '<scenario-start-local>' 2>/dev/null || true
dmesg -T --level=err,warn | tail -40
```

For CPU, I/O, and syscall questions use `perf` when `kernel.perf_event_paranoid`
allows it, otherwise sample `/proc`. Start before the action, stop immediately
after, and record the collection overhead:

```shell
perf record -F 299 -g -p "$pid" -- sleep 20          # needs paranoid <= 1
perf report --stdio | head -60
strace -f -p "$pid" -e trace=openat,rename,renameat2,unlink,fsync,mmap -tt -o trace.txt
```

`strace` on a running GUI process can slow it by an order of magnitude. Never
collect a performance budget under `strace`; use it to answer a question about
*which* syscalls happen, then re-measure timing without it.

For managed-runtime detail, `dotnet-counters` and `dotnet-trace` attach to the
self-contained process through its diagnostics socket in `$TMPDIR`; record the
socket path and the tool version, because a mismatched diagnostics tool can fail
in a way that looks like the app refusing to respond.

For accessibility, use Orca plus `accerciser` or another AT-SPI browser.
Automation-tree presence is necessary, not sufficient: activate each primary flow
with assistive technology and confirm spoken names, state, order, live updates,
and modal boundaries.

### 4.3.1 Frame pacing on Linux — measure the pipeline that exists

Linux has no single counterpart to ETW's present stream or Android's
FrameTimeline, and there is no universally available per-window frame counter.
Pick one of the following, in this order of preference, and record which was
used; a budget measured by a different method is not comparable across hosts.

1. **An external high-frame-rate camera** pointed at the physical screen, with a
   timestamped input action visible in frame. This is the only method that works
   identically under X11, XWayland, software rendering, and every window manager,
   and it is the documented fallback the other two are checked against.
2. **Compositor-side frame data**, where the desktop exposes it: KWin's debug
   console and `kwin_x11 --replace` logging, `mutter`'s
   `MUTTER_DEBUG_*` output, or a GL overlay such as MangoHud attached through
   `MANGOHUD=1` when the renderer uses OpenGL. Record the compositor and overlay
   versions and confirm that the numbers move when the workload does; an overlay
   that reports a fixed 60.0 is reporting the compositor, not the application.
3. **`perf` on the render thread**, used to show where time goes rather than to
   compute a jank percentage. Useful for attribution, never sufficient as the
   gate by itself.

Three rules keep this honest:

- **A zero-frame or constant-frame measurement is Blocked, never Pass.** Assert
  that the frame count is greater than zero and that it responds to load before
  reading any percentage, and record the count beside it.
- **Name the refresh period.** Record the active mode from `xrandr --query` for
  the output the window is on; 8.3 ms at 120 Hz and 16.7 ms at 60 Hz are
  different budgets, and a Wayland compositor may be presenting at a rate the
  X11 client never sees.
- **Record the renderer.** Hardware GL, llvmpipe, and a VM's virtual adapter
  produce different absolute numbers on identical code. A G5 result is a separate
  baseline, not a regression against G0.

Before each measured pass, open the same finalized session, fit the same
viewport, warm the same panes, let background import and indexing settle, and
record output, scale, renderer, window state, and PID. Run at least three
30-second passes per configuration, report every pass, the median, and the worst
p95; do not keep only the smoothest one. Screenshots and video corroborate a
visible defect but do not replace a frame measurement.

### 4.4 Human interaction, visual, accessibility, and automation oracle

Run the human path before inspecting or automating it. For every primary action,
judge the complete interaction loop:

1. **Discoverability** — the action and its scope can be found from visible text,
   conventional placement, or a documented shortcut; hover is not the only clue.
2. **Affordance and state** — enabled, disabled, selected, destructive, default,
   and progress states are distinguishable visually and through AT-SPI.
3. **Acknowledgement** — input receives visible feedback within §4.2, and longer
   work keeps a truthful stage and a cancel or stop affordance without stealing
   focus.
4. **Outcome** — completion or failure names the affected source, session, scope,
   row count, and destination needed to verify what happened.
5. **Recovery and reversibility** — cancellation preserves prior work; a failure
   keeps viable retry, change-destination, or inspect actions; destructive choices
   state the exact object and require proportionate confirmation.
6. **Consistency** — equivalent pointer, keyboard, touch, automation, command-bar,
   More-menu, and shortcut routes have the same meaning, and live layout changes
   never move a repeated action into another command.

Use a WCAG-aligned release floor even though VisualCat is a native desktop app:
ordinary text at least 4.5:1; large text (at least 18 pt regular or 14 pt bold, or
the rendered equivalent) at least 3:1; and active control boundaries, focus and
selection indicators, icons, and graphical cues required to understand or operate
the product at least 3:1 against adjacent colors. Where a meaningful graphic uses
lower-contrast gradation, demonstrate an equivalent textual or non-color route to
the same information. Do not round a value up to pass, and do not rely on color
alone. Record sampled foreground and background values, tool and version, state,
theme, and display pipeline. See the W3C guidance for
[text contrast](https://www.w3.org/WAI/WCAG22/Techniques/general/G18.html) and
[non-text contrast](https://www.w3.org/WAI/WCAG22/understanding/non-text-contrast.html).

Visual comparisons use native-pixel captures from an approved reference with the
same candidate, corpus, window bounds, scale, text scale, theme, contrast,
culture, font set, G-state, and interaction state. **Font availability is part of
the baseline on Linux**: a host with a different `fc-match monospace` result will
produce a legitimately different image, so record the resolved families rather
than treating the difference as a regression. Never resample an image to make it
align. Mask only named nondeterministic regions such as a clock, PID, device
serial, or live rate; keep the unmasked original and the mask definition. A
baseline change is a separate reviewed artifact, not an automatic consequence of
the candidate being different. Cover normal, hover, focus, pressed, disabled,
selected, loading, empty, failure, long-content, and modal states. A pixel diff
alone cannot Pass usability, semantics, focus, animation, or screen-reader
behaviour.

Automation should select by stable semantic properties — the accessible name,
role, and owned hierarchy exposed through AT-SPI — and assert name, role, and
state as well as activation. `xdotool` drives this application reliably because it
is an X11 client (`search --name`, `windowactivate`, `mousemove … click`, `type`,
`key`, `windowsize`, `windowclose`), and `windowclose` is the graceful
`WM_DELETE_WINDOW` route; `alt+F4` and compositor shortcuts are handled by the
window manager and may not reach the app at all. But coordinate playback is not a
release oracle: scale, text size, responsive command folding, virtualization,
scroll position, and notice insertion all move targets. Geometry scenarios may use
coordinates only after recording the logical-to-physical transform and confirming
that the pointer hit the intended semantic control. Keep manual and Orca checks
for the Skia timeline and any custom control whose automation surface cannot
express the visual relationship.

An automation shell reached over SSH has none of the graphical session's
environment. Read it from a live session process rather than inventing it — for
example `tr '\0' '\n' < /proc/<gui-pid>/environ` — and record `DISPLAY`,
`XAUTHORITY` (whose path includes a per-session suffix that must never be
hard-coded), `XDG_RUNTIME_DIR`, `WAYLAND_DISPLAY`, and
`DBUS_SESSION_BUS_ADDRESS`. A tool that fails because it inherited the wrong
`XAUTHORITY` has produced no product evidence at all.

---

## 5. Tier B — basic scenarios

Purpose: prove the exact Linux candidate works in the ordinary path a new reader
takes. All applicable B scenarios pass before A or X work begins; a primary-path
failure can invalidate later observations.

**Block format:** *Risk* — what could go wrong. *Pre* — starting state. *Steps* —
what to do. *Expect* — observable pass criteria. *Fail if* — disqualifying
observations.

---

### B-01 · Verify, extract, and cold-launch the exact tarball

*Risk* The uploaded product is not the built product, cannot be extracted safely,
loses its executable bit, needs an undeclared runtime, or needs elevation.
*Pre* L0, D1, G0 or G1. Candidate never executed on this account.
*Steps* Verify checksum, `SHA256SUMS` line, and provenance attestation per §2.4 ·
list archive members and check them against the safety rules · extract with GNU
`tar` into a fresh directory · record the inventory, mode bits, and hashes · read
`README.txt` and assess whether a first-time reader learns how to launch and how
to verify · launch from a terminal with no arguments and watch stdout and stderr ·
leave the empty state untouched for 30 s.
*Expect* Checksum and attestation match. No member is absolute, traversing,
setuid, world-writable, a device, or a link outside the archive. The root holds
`VisualCat`, `LICENSE`, `THIRD-PARTY-NOTICES.md`, and `README.txt`. `VisualCat`
extracts executable. Launch reaches a usable empty state inside the cold-launch
budget, writes nothing alarming to stderr, and requires no `sudo`, no package
installation, and no system .NET — `ldd` shows every needed library resolved from
the host's ordinary set or from the bundled runtime. The window has the product
title, the product icon, and a `WM_CLASS` that identifies it to the window
manager and taskbar. The displayed version equals the candidate version in the
archive name and in `README.txt`.
*Fail if* Any archive-safety rule is violated; the checksum, attestation, or
version disagrees; the binary is not executable straight from a GNU `tar`
extraction of a workflow-produced archive; a system .NET, a missing library, or
elevation is required without the README saying so; the identity or version
misrepresents the artifact; or launch produces an unhandled exception trace
instead of a window.

### B-02 · Runtime dependency and self-containment probe

*Risk* A self-contained publish still depends on the host for X11, fontconfig,
ICU, and glibc. A missing one of those is the most likely first-launch failure on
a distribution that is not the build host.
*Pre* B-01 extraction present. Record the library inventory *before* installing
anything.
*Steps* Run `ldd` against `VisualCat`, `vcat`, and the bundled native libraries
and record every `not found` · record `getconf GNU_LIBC_VERSION` and the glibc
version the binaries require (`objdump -T` or `strings` against the runtime
libraries) · confirm `fc-match monospace` and `fc-match sans-serif` resolve ·
then, on a dedicated host only, remove or mask one documented dependency at a
time (`libx11-6`, `libice6`, `libsm6`, `libfontconfig1`, the ICU package) and
launch again · restore each one.
*Expect* On a host with the documented packages, nothing is `not found` and the
app starts. With a documented dependency absent, the failure **names what is
missing** in a way a user can act on — a message, or at minimum a first line of
stderr that identifies the library — and the README or
[`SUPPORT.md`](SUPPORT.md) lists that package. With the ICU package absent, the
product either starts with globalization working from its bundled data or fails
with a message that says so; it must not start and then silently produce
invariant-culture dates while the UI claims a culture. A glibc older than the
runtime requires fails with a loader message, not a silent crash.
*Fail if* An undocumented host dependency is required; a missing library produces
an unhandled managed exception dump with no identifiable cause; globalization
degrades silently; or `fc-match` has no monospace result and the product draws
the entry list in a proportional font without saying anything.

### B-03 · Desktop empty state and command inventory

*Risk* The first screen must offer the desktop commands and no phantom ones.
*Pre* B-01 session running, D1.
*Steps* Read the empty state without touching anything · enumerate every visible
command and every command in *More* · hover and keyboard-focus each one.
*Expect* The identity line shows `VisualCat <version> · local-first · no
telemetry`, with a version that matches the installed build and a `-dev` suffix
on a non-release build. The desktop hero actions are present — open a log, open a
session, ADB live, follow a growing file — together with recent sessions when any
exist. Session-dependent commands (export, save, share, lines-not-on-the-timeline)
are disabled and say why. No Android-only control appears.
*Fail if* A documented desktop command is missing, a session command is enabled
with no session, a command is present but inert, or the version does not track the
artifact.

### B-04 · Import a small file through the portal file chooser

*Risk* Picker compatibility is a distinct Linux risk: the dialog may come from
`xdg-desktop-portal` with a GNOME, KDE, or GTK backend, or from a fallback, and
each returns paths differently.
*Pre* `small.txt` in an ordinary directory. Record which portal backend is
installed and running.
*Steps* *Open log* · note which dialog appears and where it came from (`ps` for
the portal process, or DBus) · navigate and select `small.txt` · observe the
import review and the ingest · repeat once with a file whose name contains a
space and an accented character · repeat once by typing a path rather than
clicking.
*Expect* A file chooser appears within the budget and is usable by pointer and
keyboard. The selected file is materialized from whatever URI form the portal
returns, including a document-portal path under `/run/user/<uid>/doc/`. Ingest
completes; parsed, timed, untimed, unknown, and rejected counts match the corpus
oracle; the heat map draws; the tab is named after the file. A confidently
detected file is not interrupted by the review.
*Fail if* The chooser does not appear, returns a path the product cannot open, or
the product reports a different file than the one chosen; counts disagree with
the oracle; or a name with non-ASCII characters is corrupted in the tab, the
notice, or the session manifest. Typed text that does not land in the portal's
own name field is a portal behaviour to record, not a product finding — check
where the file actually went before filing it.

### B-05 · Heat map to exact source bytes

*Risk* The core value proposition, end to end, with byte fidelity.
*Pre* B-04 session open.
*Steps* Read the six severity rows · click a dense cell · read the entry list ·
select a row · open the entry inspector · read the raw source context · verify
those bytes independently against the corpus with a binary range reader.
*Expect* Cell → entries → selected entry → raw source are consistent: the same
instants, the same message, and raw bytes that match the file at the stated
offset. Use `dd`/`tail -c +N | head -c M` or an equivalent byte-exact reader —
not a line-oriented tool that can transform encoding or line endings. The source
gutter starts at line 1 and the selected line stays visible with its context.
*Fail if* The raw view shows a different record than the selected row, offsets do
not resolve, or the gutter numbering disagrees with an independent count.

### B-06 · Severity filters and clear semantics

*Steps* Toggle each of the six severities individually and in combination ·
observe counts, chips, and the plot · *Clear all*.
*Expect* Counts change consistently with the plot; every active filter is named
in the chip bar; clearing returns to the unfiltered view exactly and leaves no
residue.
*Fail if* A severity's count and its plotted density disagree, or a cleared
filter leaves a chip or a hidden constraint behind.

### B-07 · Text and regex search

*Steps* Search a literal known to occur · read the counter before stepping · step
forward and backward with the buttons and with `F3`/`Shift+F3` · reach both ends
and step past them · use `Ctrl+G`, `Alt+Home`, and `Alt+End` · press *Fit* and
step once more · pan by hand and read the counter · enable regex and run a valid
pattern, an invalid one, and `(a+)+$` against `pathological-regex.txt` · correct
the invalid pattern.
*Expect* Before the first step the counter reads `– / N` where **N is every match
in the session**. Each step selects an exact record, reveals its row even when
that row is past the loaded page, and preserves the chosen zoom. Stepping wraps at
both ends. From *Fit* a step still selects a record and opens a readable window
around it. A manual pan returns the counter to `– / N` without changing the
search. `Ctrl+G` states its order, refuses an out-of-range number rather than
clamping it, and is inert with a reason when there are no matches. An invalid
pattern produces one product sentence — never a resource key or a framework dump —
leaves the previous result standing, and stops being reported once the pattern is
valid. The pathological pattern is cut off by the bounded timeout, says so, and
leaves the UI responsive.
*Fail if* The total stops at a marker cap, a step changes the zoom or skips a
match, wrapping fails, *Fit* leaves the view unchanged, the UI freezes on any
pattern, or a corrected pattern keeps reading its old rejection back.

### B-08 · Timeline pointer, wheel, touchpad, and keyboard basics

*Steps* Drag to pan · wheel-zoom · pinch or two-finger scroll on a libinput
touchpad · double-click · drag a time range · use the minimap · press
`Left`/`Right`, `Plus`/`Minus`, `0`, `Home`/`End`, `F`, `J`/`K` with the timeline
focused.
*Expect* Panning stays inside the session bounds with no phantom time beyond
either end. Wheel and touchpad zoom around the pointer; natural-scroll and
inverted settings are respected as the desktop configures them. Double-click
zooms **only** — it does not also re-scope the entry list or the chip bar. A
dragged range is distinct from the viewport. `0` fits the whole session in one
key. The minimap and viewport always agree. Axis labels stay inside the plot and
never draw under the minimap; a viewport too narrow for two ticks labels its own
ends.
*Fail if* Panning escapes the session, a gesture carries a second meaning, *Fit*
requires opening a drawer, or the axis escapes its band.

### B-09 · Analysis panes, paging, and the selected-entry workflow

*Steps* Cycle the workspace panes · page the entry list with `Load 500 more` to
several pages · select entries by pointer and by keyboard · copy a message and a
raw line · open the facets and templates panes · use `Alt+1`..`Alt+4`.
*Expect* Each pane composes within budget. Paging is within budget and states how
many rows are shown, earlier, and later. Copy places exactly the selected text on
the CLIPBOARD selection. Contextual action slots are stable: the appearance of a
paging control never moves *Copy raw* so that two clicks in the same place hit
different commands. Focus moves where `Alt+1`..`Alt+4` promise.
*Fail if* A pane exceeds budget, a count is unaccounted, a copy is truncated or
re-encoded, or an action slot shifts under a repeated click.

### B-10 · Host ADB discovery and a three-minute capture

*Risk* On Linux, USB device access depends on `udev` rules and group membership;
this is the most common reason a device is visible to `lsusb` and useless to
`adb`.
*Pre* A physical authorized device. Record the `adb` binary's absolute path,
origin (distribution package or SDK platform-tools), version, and hash; record
`udev` rules present and the user's groups.
*Steps* Open the ADB capture dialog · read the device list and let it refresh ·
select the device, buffers, and options · capture for 3 minutes while §3.4
traffic runs · read the status line throughout.
*Expect* Discovery lists the device inside budget with its serial, model, and
state, and the refresh interval is visible in the list's behaviour. Capture
starts, names the transport and the buffers, and the first complete line appears
within budget of a producer flush. The status line shows a rate measured over the
last second and a heartbeat when the source goes quiet. Marker-bounded counts
reconcile with §3.4 within declared drops.
*Fail if* A present, authorized device is not listed; a device in
`no permissions`, `unauthorized`, or `offline` state is shown as ready; capture
starts against a serial that is not the selected one; or counts disagree with the
marker oracle without the product declaring a gap.

### B-11 · Stop capture is answered, sticky, and complete

*Steps* Capture at least 5 minutes · press *Stop* once · do not press again ·
watch to the end · verify the session.
*Expect* Acknowledgement within budget. The control never springs back to *Stop*
and the status never returns to *Capturing*. The status leads with a visibly
advancing elapsed clock and names the stage — draining, compacting, writing the
index, reopening. When the manifest is written it says the capture is saved, the
live controls disappear, and `vcat verify` passes. No `adb` child process
outlives the capture.
*Fail if* The status reverts, the state never resolves, a short capture fails to
finalize, or an orphan `adb` remains.

### B-12 · Follow a growing file

*Steps* Start the §3.3 producer · *Follow growing file* against it · watch the
first complete line, a partial line completed later, an idle window, and a burst ·
stop following · verify the final sequence against the producer ledger.
*Expect* The first complete record appears within budget of its flush. A partial
line is not published until it is complete, and is published once it is. After an
idle window the next line arrives promptly rather than after a poll that has
backed off indefinitely. *Follow* and *new data* affordances belong to the active
source and disappear when the source closes; re-engaging Follow opens a window on
the live edge rather than keeping a whole-session span. The final record sequence
matches the ledger exactly.
*Fail if* A line is lost, duplicated, or reordered against the ledger; a partial
line is published as a record; the quiet status keeps claiming arrivals; or Follow
survives the source.

### B-13 · Save standard and portable sessions

*Steps* Save a session normally and as a portable archive · record the
destination, the file modes, and the sizes · verify both with `vcat verify` ·
reopen both.
*Expect* Each save names its destination and its completion in the notice lane.
Written files are owned by the running user with modes consistent with the
account's `umask` and no world-writable bit. Both verify clean and reopen with the
same entry count and time range. The portable archive carries its raw source; the
standard save records the external source identity rather than copying it, per
[`SESSION-FORMAT.md`](SESSION-FORMAT.md).
*Fail if* A save is silent, lands somewhere other than the stated destination,
creates world-writable or root-owned files, fails to verify, or a portable archive
omits data the product said it embedded.

### B-14 · Open and round-trip a portable archive

*Steps* Open a `.vcat.zip` produced by this candidate · re-export it · compare
the manifest, counts, instants, and raw hash against the original · repeat with an
archive produced by the Windows candidate and by Android, where available.
*Expect* Counts, instants, severity totals, template identities, and raw-source
hashes survive the round trip. Entry names inside the archive use forward slashes
and are interpreted the same way on both platforms.
*Fail if* Any oracle field changes, a path separator is reinterpreted, or an
archive produced on one platform cannot be opened on the other.

### B-15 · CSV export scopes, order, and encoding

*Steps* Export with each scope the review offers · read the promised row count
beside each one before choosing · change row order and encoding in the review ·
verify each file against the CLI oracle with a byte-exact comparison · reopen
*Appearance & timeline* afterwards.
*Expect* The review names each scope with its exact timed row count, the displayed
time zone, and the filters it applies. The completion notice states the same
number, the scope, and the file name. The file contains that many data rows plus
one header. Line endings and encoding are exactly what the review selected, and
the same file written on Windows and Linux from the same session is byte-identical
for the same options. The two options in the review are the two stored in
settings, and a successful export leaves settings showing what it used.
*Fail if* A promised count disagrees with the file, an explicit range silently
gains a broader fallback, the extension is doubled, the newline form is not the
selected one, or settings and the review disagree after an export.

### B-16 · Startup paths, working directory, and argument dispatch

*Steps* Launch with `--log <file>`, with `--session <dir>`, with a bare path, with a
relative path from three different working directories, with a path containing spaces
and quotes, with a non-UTF-8 byte in the name, with a path that does not exist, with a
directory where a file is expected, with a named pipe and with `/dev/stdin`, and with an
unknown flag · launch once through a symlink to the binary and once with the binary
invoked through a relative path · then launch it **the way a Linux user actually
will**: from a file manager, and from a hand-written `.desktop` entry the tester
creates, so the process inherits the graphical session's environment rather than a
shell's.
*Expect* Each valid form opens exactly the intended source, regardless of the working
directory. A non-seekable source — a FIFO, `/dev/stdin` — is either supported or refused
with a reason; it must not half-import, because the store addresses the source by byte
offset. A successful open never stays on *Opening* and never reports a cancellation as a
startup error. An invalid form produces one clear message and a usable empty state, not
a crash or a hollow workspace. An unknown flag is either ignored as documented or
reported; it must not be treated as a file name. Invoking through a symlink or a relative
path still resolves the bundled runtime. The launcher-started process behaves identically
to the terminal-started one — record its environment from `/proc/<pid>/environ` and note
every difference in `PATH`, `LANG`, `LC_*`, and `TMPDIR`, because those differences are
what A-15 then has to survive.
*Fail if* A working directory changes which file is opened, a hostile-but-legal name is
mangled, a missing or non-seekable file crashes the app or produces a silently partial
import, launching through a symlink fails to find the runtime beside the real binary, or
the app works from a terminal and not from a launcher.

### B-17 · Recent sessions, close, and reopen

*Steps* Create several sessions · close tabs during and after ingest · reopen from
*Recent sessions* · close the application and relaunch.
*Expect* Sessions are listed with unambiguous source and start identity, so two
captures of the same source are distinguishable. Reopen is within budget and shows
the same counts and range. A reopened finished capture shows the whole capture, not
a stale live window or a zero-count empty view. Closing a tab during ingest never
crashes the workspace.
*Fail if* Entries are indistinguishable, a reopened session shows an empty list
under a ready status, or closing a tab throws.

### B-18 · Window state: minimize, maximize, restore, close, persisted size

*Pre* Record the window manager. Repeat on G0 and G1, and at least once on G4.
*Steps* Resize to the minimum and beyond · maximize · minimize and restore ·
fullscreen if the WM offers it · move between workspaces · close with the window
manager's close affordance, with the keyboard route the desktop provides, and with
`xdotool windowclose` · relaunch and read the restored size.
*Expect* The window respects its minimum size; the WM is not able to force it
smaller than the layout supports without the layout responding. Maximize,
minimize, and restore all work, and a minimized window stops the expensive redraw
cadence while acquisition continues. Closing by any route shuts down cleanly, with
no late write into a disposed sink and no orphan child process. Width, height, and
maximized state are restored on relaunch; window **position** is not a declared
persisted field, so require reachability rather than exact coordinates.
*Fail if* The app exits non-zero or leaves an orphan on a graceful close, a
minimized window keeps redrawing, a tiling WM makes the workspace unusable with no
response, or a restored window is unreachable.

### B-19 · Keyboard-only primary journey

*Steps* With the pointer unplugged or unused, complete: open a log, filter by
severity, search and step to an exact match, select an entry, read its source,
change a setting, export, and close — using only the keyboard and the routes in
[`KEYBOARD.md`](KEYBOARD.md).
*Expect* Every step is reachable. Focus is always visible and never trapped; Tab
order follows the visual order; Escape precedence behaves as documented — mobile
filters, then focused search, then a selected timeline scope, then filters — and is
inert when there is nothing to dismiss. No step requires a pointer.
*Fail if* Any step is pointer-only, focus disappears or is trapped, or a shortcut
documented in `KEYBOARD.md` does nothing and says nothing.

### B-20 · Off-timeline and unparsed evidence stays discoverable

*Steps* Import `outcomes.txt` with an explicit `threadtime` override and
`crashy.txt` with detection · read every count the product offers · open *More →
Lines not on the timeline…* · read the source gutter codes and their legend.
*Expect* Timed, untimed, continuation, unknown, and rejected populations are all
explicitly accounted, and the totals equal the §3.1 oracle. The notice that names
lines which are not logcat records states the **finished** count, waits until the
source has stopped arriving, and names a menu item that exists. The command opens
the exact source-ordered lines and does not claim that more of the file remains to
be scanned once it has listed every line the session counted. Every non-ordinary
gutter code is explained on screen and accessibly, never by tooltip alone.
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
excludable, and copyable. Statistics totals and first and last instants must equal
the active query oracle. A process name changing for one PID must not retain stale
facet tallies.

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
Templates group that exists only while one is set. On a growing capture the browser
holds one snapshot, says which one, and offers *Refresh counts* rather than moving
the page.

### A-03 · Saved views round trip

Save a named severity + facet + regex + range view; clear, apply, close and reopen
the session, and apply again; save Unicode, very long, and duplicate names; delete
one. Every dimension and Follow state the schema allows returns exactly; an invalid
or unsupported `view.json` is ignored without blocking the session; delete affects
only the named view. A view file created on Windows applies identically here.

### A-04 · Range, viewport, filter, and export remain distinct

Create a selected time range inside a zoomed viewport over an active filter.
Exercise Zoom range, Filter range, Export range, Clear selection, and Escape. Each
changes only its promised dimension, and exported half-open boundaries match the
independent microsecond oracle; after I-02 validates it on the same corpus, the
candidate CLI result agrees too. *Export range* opens the review with that span as
a fixed summary — no scope question to answer again, and no broader fallback beside
it, including when the explicit range is empty.

### A-05 · Many independently stateful tabs

Open at least eight sessions: file, ADB, growing, standard, portable, recovered,
degraded, and failed. Give each a different filter, viewport, and selection.
Switch, reorder where supported, close the first, the middle, and the selected one
while progress occurs, and use the scrolled tab strip. State never leaks; the
selected tab and command availability track the visible session; a clipped tab can
still be reached and closed; every close is prompt and none throws.

### A-06 · Import-preview override matrix

For each `fmt-*`, import once with detection and once with an intentional format
override; vary the assumed year, IANA time-zone IDs (the native form on Linux),
template mining off, and Embed raw source on. Reach the review both ways: through
*Open log*, and through **Open log with options…** in *More*, which opens it
regardless of detection confidence. Editing an option re-evaluates the sample
already read — the file is not reopened or copied again — and *Import* stays
disabled until the preview catches up, so a rapid edit cannot accept an old span.
Validation rejects a blank or invalid zone and years outside 1970–9999 without
closing. Enter a Windows zone ID (`Central Europe Standard Time`) as well and
record whether the runtime accepts it on this host; whichever it does, the review
must validate rather than accept-then-fail. An explicit override never displays a
fabricated detection confidence. The manifest records exactly the settings that
produced the accepted preview.

### A-07 · Automatic detection and mixed content

Import every format and `mixed-formats.txt`. Candidate scores and order, warnings,
outcome counts, and the selected default match the oracle, including each part's
recorded byte range. Low-confidence input remains an explicit user decision in the
preview; `outcomes.txt` is refused by detection and that refusal names choosing a
format as the way forward. No bytes disappear merely because a line does not fit
the primary format.

### A-08 · Adversarial corpus sweep

Open every §3.2 finite file, including the hostile-name and non-UTF-8 rows. No
crash or hang; invalid bytes, continuations, untimed, unknown, and rejected records
remain counted and reachable; 2 MiB lines are bounded, inspectable, wrappable, and
copyable; source offsets remain exact across LF, CRLF, lone CR, BOM, and
no-final-newline; control, NUL, and bidi content cannot alter surrounding UI. A
one-line source and an empty file each produce one coherent result. A non-UTF-8 path is
displayed, reopened, exported, and recorded in diagnostics without invention or
replacement.

Then open the files a Linux user will genuinely try, because the product is a log viewer
on a machine full of logs that are not logcat: `/var/log/syslog` or `/var/log/messages`,
a `journalctl` dump, `dmesg` output, and `~/.xsession-errors`. These are out of scope as
*formats* — see §1.2 — so the requirement is the refusal, not the parse: detection scores
them too low to choose a format and says that choosing one is the way forward, an explicit
override accounts for every line as unknown rather than fabricating records, and nothing
claims a timeline it did not derive. Most of these files are also root-owned or
group-restricted on a default install, so the same step exercises the permission-denied
path on a file users reach for first; copy one with `sudo` into the corpus to get the
readable case as well.

### A-09 · Source mutation during finite import

Import a large file, then — on separate copies — replace it with `mv`, truncate it
in place, append to it, `unlink` it, and `chmod 000` it while materialization and
ingest run. The product either uses one consistent identity snapshot or fails and
reports the source change. It never builds a hybrid, silently accepts a changed
hash, corrupts the replacement, or waits forever. **Linux-specific:** an unlinked
file remains readable through the open descriptor, so the product may legitimately
complete an import of a file that no longer has a name — but whatever it does, the
session's recorded source identity and the notice must agree with it, and a later
reopen must report the source as missing rather than silently degrading.

### A-10 · External source changed or missing on reopen

Save a non-portable session, close it, then modify, move, `chmod 000`, and delete
the external log as separate passes. Reopen each time. The identity check detects
the change; degraded index-only mode is entered explicitly and labelled; raw
context says why it is unavailable and offers a route; nothing claims bytes it
cannot read. A permission-denied source is distinguished from a missing one.

### A-11 · Recovered interrupted session

Interrupt an ingest with `SIGKILL`, relaunch, and recover. The partial session is
listed as interrupted, reports what reached disk, verifies as a partial, and
reopens read-only or recovered exactly as [`SESSION-FORMAT.md`](SESSION-FORMAT.md)
describes. Its completion text says *Interrupted* and accounts for the recovered
entry count rather than presenting itself as complete.

### A-12 · Session cache and retention policy

With a seeded cache, inspect *Session cache*, its computed size, and its retention
policy; run cleanup with an open session, a protected session, and a session held
by a second process. Restore and protection precede cleanup; the preview is
recomputed rather than reused; open and protected sessions survive; the reported
reclaimed size matches what the filesystem shows (`du -sb` before and after).

### A-13 · Appearance, timeline, and diagnostics settings

Exercise every setting in *Appearance & timeline*, including text scale, theme,
live refresh limit, default export order and encoding, and structured diagnostics.
Labels are human language, never implementation identifiers. A changed setting
takes effect without a restart and reaches every open workspace, remeasuring
together rather than replacing the session or ending a capture. The settings writer
preserves the newest value: a coalesced workspace write cannot overwrite a newer
preference. Enabling diagnostics creates `<diagnostics-root>/visualcat-*.jsonl` — the
exact pattern the diagnostic bundle collects — and nothing outside it.

### A-14 · Custom session directory and symlink refusal

Point the session root at another directory, a directory on a second filesystem, a
directory on a permission-less mount, a symlink to a directory, and a path that
does not exist. Each is accepted or refused with a reason. Prove the same boundary
on the CLI: [`CLI.md`](CLI.md) states that `index --force` refuses a filesystem
root and any tree containing links or reparse points, so plant a symlink inside an
existing `.vcat` directory and confirm the refusal rather than a recursive replace.
A symlinked lease or session root is refused rather than followed — the product's own
lease storage rejects a reparse point, and that refusal must be a clear message, not
an opaque IO error. Sessions written to the new root are found, and the old root is not
silently abandoned with data in it.

### A-15 · ADB locator precedence

Linux hosts commonly have more than one `adb`: a distribution package in `/usr/bin`, an
SDK copy under `~/Android/Sdk/platform-tools`, and possibly a third on `PATH`. Prove the
precedence the implementation actually applies: an explicit configured path first, then
`ANDROID_SDK_ROOT`, then `ANDROID_HOME`, then `platform-tools` under the default SDK
directory beneath the resolved local application-data root, then each `PATH` entry in
order. Record which binary was actually spawned
(`readlink -f /proc/<adb-pid>/exe`) for every configuration, and run the same matrix
through `vcat adb-devices --adb <path>` so the desktop and the CLI are shown to resolve
identically.

Two things to reconcile rather than assume. [`CLI.md`](CLI.md) documents the CLI locator
as `--adb`, `ANDROID_SDK_ROOT`, or `PATH` — it names neither `ANDROID_HOME` nor the
default SDK directory, so establish which is authoritative and record the mismatch as a
documentation or behaviour finding per §2.10. And on Linux the local application-data
root resolves under `~/.local/share`, so the default SDK probe looks somewhere the
conventional Linux SDK install is **not**: an `adb` at
`~/Android/Sdk/platform-tools/adb` is reachable only through `PATH` or an explicit
variable. Confirm what actually happens on a host with exactly that layout and nothing
else, because that is the ordinary Linux developer's machine.

Run the whole matrix twice: once from a terminal, and once from the file manager or
`.desktop` launcher used in B-16. A graphical session's `PATH` comes from the systemd
user environment, not from a shell profile, so an `adb` that a terminal finds through a
line in `.bashrc` or `.profile` can be invisible to a launcher-started process. Discovery
that works in one and fails in the other is the finding, and "it works in my terminal" is
not evidence that a user's launcher will.

With no `adb` anywhere, the message names platform-tools and SDK configuration and offers
a route; it is never an inert dialog. A configured path that exists but is not
executable, a path on a `noexec` mount, a dangling symlink, and a directory where a
binary is expected each fail with their own specific reason.

### A-16 · ADB device-state and topology matrix

Cover `device`, `unauthorized`, `offline`, `no permissions`, a device that appears
only after `udev` rules are installed, a wrong or absent serial, two devices
attached at once, and a device that disappears mid-discovery. Each state is
surfaced with its own text and is never retried as a parser failure. The
`no permissions` state in particular must name the `udev`/group cause rather than
reporting a generic failure, because that is the Linux default on a fresh host.
Pre-flight rejects a missing serial before spawning `logcat`, so an unknown serial
can never produce an indefinite empty wait that looks like a successful capture.

### A-17 · ADB buffers and format negotiation

Capture with each buffer individually and in combination, and with a device that
rejects `threadtime,year,UTC,usec`. Per-record buffer attribution is exact across
`-D` boundaries. Negotiation degrades in bounded steps, records what it settled on
in the manifest, and never lets a lost UTC modifier shift every timestamp by the
host-to-device offset.

### A-18 · ADB pre-roll, duration, and byte limits

Exercise pre-roll, a duration cap, and a byte cap, each alone and together.
Pre-roll content is identified as pre-roll; a cap ends the capture, finalizes it,
and says which cap ended it; the manifest records the configured values.

### A-19 · ADB reconnect and numeric resume cursor

Interrupt the transport mid-capture (unplug, `adb disconnect`, Wi-Fi toggle, USB
autosuspend). Reconnect is bounded, uses the original serial only, resumes from a
numeric cursor rather than re-reading the whole buffer, counts reconnect gaps
distinctly from source gaps, and either continues or fails explicitly while
preserving committed data.

### A-20 · Device clock and host clock or time zone differ

Set the host to a zone at least ±2 h from the device's, and separately set `TZ`
for the process only. The live capture is read in the device's own clock; the
newest entry sits at about *now*; Follow tracks the live edge; the session pane
names both zones. Changing `TZ` for the process alone must not silently
reinterpret stored instants on reopen.

### A-21 · Growing source truncation, rotation, removal, and writer crash

The declared policy is **stop**: the source advertises `rotationPolicy = stop`, and
the follow loop detects a changed source only when a read returns nothing and then
re-resolves the *path* — a missing path is reported as removed, and a path now
shorter than the bytes already delivered is reported as truncated or rotated. Both
raise a source change the session must record in its defect counters. Prove that
contract, and then prove the Linux-specific hole in it.

Run these as separate passes: `truncate -s 0` in place; `truncate` to a shorter
non-zero length; `mv` plus a new shorter file (rotation without `copytruncate`);
`logrotate --force` against a real configuration on a dedicated host; `rm` with no
replacement; and a writer killed between appends. Each stops **visibly**, the notice
distinguishes *removed* from *truncated or rotated*, the session records the source
change, and everything committed before the change verifies.

Then the two cases the length comparison cannot see, because the open descriptor and
the path have come apart. **Rotate, then immediately make the replacement at least
as long as the bytes already read** before the next poll: the path is no longer
shorter, so no change is detected, and the loop is now reading an inode nobody writes
to any more. **Unlink the file and keep appending to the unlinked inode**: the reads
keep succeeding, so the removal is not noticed either. In both cases the product must
not present a stalled or orphaned follow as a healthy one — the quiet heartbeat from
A-22 is the minimum honest outcome, and silently describing the old inode as the file
at that path is the finding. Record the exact timing used, because with a 250 ms
default poll this is a real race a real `logrotate` can win.

### A-22 · Growing source sharing and first-batch timing

Follow a file another process is writing, and follow one opened `O_APPEND` by two
writers. The reader's share mode is a Windows concept with no enforcement here, so the
Linux assertion is behavioural: the writer is never blocked, never sees an error, and
no advisory or mandatory lock is taken against it — verify with `lsof` and `fuser` that
the product holds only a read descriptor. The first complete line arrives within budget
of a flush, which with the 250 ms default poll makes the budget a poll-interval
question and not a notification one. A quiet source's last-second rate falls to zero
and the heartbeat names the silence.

### A-23 · Concurrent capture, import, and query

Run an ADB capture, a large import, and an interactive query at once. Each
progresses; none starves; the status and notice lane attribute work correctly; and
nothing is relabelled as another operation.

### A-24 · Selection and source context across live refresh

With a live capture and an entry selected, let several refreshes pass. Selection
and the timeline caret are restored by entry id every time. Source context survives
reattachment, retries when interrupted, and can read a sidecar the capture is still
writing. An inspected entry that the active filter excludes is still admitted —
entry and *Copy raw* agree, and the UI offers a way back to it. Every read ends in
bytes, an explicit interruption, or a failure offering retry; never a permanent
*Reading*.

### A-25 · File-chooser cancellation and refusal paths

Cancel the chooser at every stage; dismiss it with Escape and with the window
manager's close; select a directory, a device node, a FIFO, a broken symlink, a
file you cannot read, and a file on a mount that disappears between selection and
read. Each is refused with a reason and leaves a usable state. Repeat once with
`xdg-desktop-portal` stopped, to confirm the fallback path is either functional or
honest about being unavailable. A cancelled open is never reported as an error.

### A-26 · Names, normalization, and case collisions

Open and save using the hostile-name corpus: spaces, quotes, `$`, a literal
newline, NFC and NFD spellings of the same visual name, a non-UTF-8 byte, and a
pair of names differing only in case. Display, tab name, notice, export file name,
manifest, recent-sessions list, and diagnostics all carry the byte-exact name. On a
case-sensitive filesystem the case pair remains two distinct sessions; on a
case-insensitive mount the product must not create two sessions that resolve to one
path. No name is ever passed to a child process in a way that re-splits or expands
it.

### A-27 · Diagnostic bundle review

Create the bundle through the UI. The confirmation states exactly what will be
collected; the archive contains only that; it is written where the product said;
and it is readable with ordinary tools. Its entry names are safe, its modes are not
world-readable by accident, and its contents satisfy P-03.

### A-28 · Update route and release-origin communication

With no network activity of its own, *Check for updates…* must say plainly that a
build installed from a GitHub release or built locally is not updated
automatically, and offer to open the releases page only on request. Verify the
browser actually launches through the desktop's default handler, and that when no
handler is available the product shows the URL instead of failing silently. Confirm
with P-01 that nothing was fetched before the user asked.

### A-29 · Upgrade from the previous supported release

With D3, start the candidate from a separate extraction. Settings, sessions, saved
views, cache, and an interrupted session are read compatibly or migrated with a
recorded path; the candidate does not rewrite old data merely by listing it; the
previous release still runs afterwards, or the rollback risk is documented
explicitly.

### A-30 · Window state and display-topology restoration

Save a window size, then change resolution, scale, orientation, and primary output
with `xrandr`; unplug and reattach an output; and relaunch in each configuration.
The window is always reachable and correctly scaled; a saved size is honored
without restoring an invalid position; dialogs open on the active output.

### A-31 · Lock, blank, suspend, and session logout during work

As separate passes during a capture and during an import: lock the screen, let the
screen blank, switch users, suspend and resume, disconnect a remote session, and
log out. Acquisition continues where the platform allows it; resume is immediately
current; a logout's `SIGTERM` drains and finalizes or fails explicitly, never
leaving a session that verifies as neither complete nor recoverable. **Note that
GNOME's default idle timer blanks the screen after 300 s**, which makes a framebuffer
screenshot come back all black; that is the session blanking, not the app dying.
Ledger and restore `idle-delay` and `idle-activation-enabled` if the scenario
changes them.

### A-32 · Multi-instance ordinary use

Run two candidate instances under one account on distinct sources. Settings
coalesce without losing the newest value, sessions stay separate, recent lists
converge sensibly, and no instance's tab state appears in the other.

### A-33 · Multi-instance shared-session conflict

Point two instances, and then a `vcat` process, at the same session. The lease
directory under `<lease-root>` serializes them: a read lease is shared, a write is
exclusive, an attempt to delete a session another process holds is refused, and
nothing is corrupted. Record the lease files created and confirm they are removed
when the last user exits — including after a `SIGKILL`, where a stale lease must
not permanently lock a session out.

### A-34 · Settings corruption, incompatibility, and write recovery

Supply a truncated, malformed, schema-newer, and unreadable (`chmod 000`)
`settings.json`, and a read-only settings directory. The product starts with
defaults, says what it did, and does not destroy the unreadable original without
saying so. A failed write is reported and retried, not silently dropped.

### A-35 · The one file operation acknowledges, progresses, and stops

Start each long file operation — materialize, standard save, portable save, CSV
export, diagnostics bundle, cache cleanup — and watch the shell's single file
operation. Nothing copies, writes, or archives behind an empty screen; progress
advances; *Cancel* is a request the shell then waits on, and the operation reports
its actual result. A command held by a running file operation names that reason in
its accessibility tree rather than being silently disabled.

### A-36 · XDG base directories and HOME edge cases

Run with `XDG_DATA_HOME` set to another directory, set to a relative path, set to a
path that does not exist, set to a path that is not writable, and unset. Also run
with `HOME` pointing elsewhere and with `HOME` unset. The product resolves its data
root per the XDG specification, creates only `Sessions`, `Diagnostics`,
`SessionAccess-v1`, and `settings.json` beneath its own directory, and reports a
specific failure when the root cannot be created. It never falls back to the
current working directory, to `/tmp`, or to another user's tree, and never writes
into `$HOME` outside the resolved data root.

### A-37 · Locale, globalization mode, and the time-zone database

Run under `C`, `en_US.UTF-8`, a locale with comma decimal separators
(`cs_CZ.UTF-8`), a locale with a non-Gregorian default where available, and an RTL
locale; run once with split `LC_NUMERIC`; run once with
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`; and run once with the system `tzdata`
package absent or `/etc/localtime` broken. The interface stays internally
consistent: ISO dates and interface-culture numbers throughout, never the host's
conventions mixed into one English surface. Invariant globalization either works
with a clear consequence or fails with a clear message; it must not silently
produce different instants for the same file. A missing zone database is reported,
not guessed.

---

## 7. Tier X — complex, stress, and soak scenarios

Run X tiers only on a dedicated host, account, and filesystem, with the mutation
ledger and abort thresholds prepared. Keep workloads isolated when measuring them.
Several rows here can render a host unusable if they are pointed at `/` or at a
real home directory.

---

### X-01 · One-million-line import and interactive analysis

Import `large.txt`; record preview, first plot, throughput, peak and settled
resources, and finalization. During ingest, repeatedly pan, zoom, search, filter,
switch panes, and inspect source. The final oracle must be exact; an untouched
viewport follows to the whole session; the first user navigation hands viewport
control to the reader for good; budgets pass. Run this five times per build as the
**A/B check for the snapshot-refresh path**: read the summary line and the zoom
readout from screenshots each time, because this race needs a real compositor and a
real overlapping import and has never reproduced headlessly.

### X-02 · Five-million-line and configured-limit behaviour

Import `xl.txt` with and without templates and portable raw, on a filesystem with
measured headroom. Record disk amplification, segment count, mapped regions, open
descriptors, time, peak memory, and final compact, verify, and reopen. The product
completes within available resources or refuses before unsafe exhaustion, with
committed partial state recoverable.

### X-03 · Twenty-million-entry live growth

Use a controlled high-rate source until ≥20 M entries where hardware permits.
Measure snapshot cadence, statistics and facet time, UI refresh count, resource
slopes, and finalization. Per-refresh query cost must not grow linearly with total
published history; a published segment's cached contribution stays stable.

### X-04 · Interaction and input storm during ingest

While X-01 or X-03 runs, continuously resize, change panes, pan and zoom, search
and cancel, toggle filters, page, open and close dialogs, copy, and switch tabs for
20 min. Drive part of it with `xdotool` so the rate is reproducible. No UI-thread
exception, lost focus, stale selection, command-slot shift, freeze >1 s, or source
data loss.

### X-05 · Four-hour ADB capture endurance

Capture controlled mixed-rate traffic for ≥4 h with fixed buffers. Sample every 15
min and interact hourly. The marker and loss oracle, gap counters, session
verification, absence of sustained resource growth, sticky stop,
query-during-finalize, and reopen must all pass. Fix the screen, blank, and
minimize policy for the whole run and state it; disable the desktop idle blank
deliberately and restore it afterwards.

### X-06 · Overnight growing-file soak

Follow a growing file for 8–12 h with long idle windows and bursts. Sample CPU, RSS,
managed heap, GC counts by generation, descriptors, threads, and file I/O. Idle cost
plateaus. The 1 MiB read buffer is allocated once for the life of the read, not once per
poll: the regression this guards against allocated a large-object buffer every 250 ms —
roughly 4 MiB/s and 15 GiB/h, with a continuous gen2 cadence while the followed file was
idle and the loop was delivering nothing at all — so watch gen2 collections and
large-object-heap size specifically, not just RSS. The first post-idle line is picked up
on the next poll and becomes visible within the §4.2 first-complete-line budget — the
poll interval is that budget's floor, not a separate promise — and the final record
sequence is exact against the producer ledger.

### X-07 · Minimized, unmapped, and obscured capture efficiency

Run comparable 60-min intervals with the window visible, minimized, fully obscured by
another window, on a different virtual desktop, and with the display asleep. Acquisition
rates and counts stay equivalent within source variance; a window the compositor has
unmapped stops the expensive redraw and query cadence; restore is immediately current.
Record how each state was produced, because an "obscured" window under a compositor may
still be receiving frame callbacks, and `xdotool windowminimize` is not the same event as
the window manager's own minimize.

Check the session-power boundary in both directions with `systemd-inhibit --list` and
`loginctl show-session`: the product is not expected to take an idle, sleep, or shutdown
inhibitor, so a capture is allowed to be interrupted by the session's own suspend — which
is what L6 and A-31 test. The finding would be an inhibitor that is taken and then
outlives the capture, or a product that silently relies on one the host does not grant.

### X-08 · ADB ring-buffer pressure and declared loss

On a dedicated device, record the original buffer state, create a marker storm and
optionally a controlled small buffer, then capture with pre-roll and reconnect. The
product must not claim losslessness: `chatty` declared drops, source gaps, and
reconnect gaps are counted distinctly; buffer attribution and the surviving
sequence are correct. Restore every per-buffer size exactly.

### X-09 · ADB server, transport, and USB gauntlet

During capture, as separate passes: kill and restart only the dedicated ADB server;
unplug and replug USB; switch the device's USB mode; revoke and re-authorize;
toggle Wi-Fi transport; let USB autosuspend engage
(`/sys/bus/usb/devices/*/power/control`); restart the device; attach a second
device; and remove the `udev` rule mid-run. Bounded reconnect uses the original
serial only; no indefinite unknown-serial wait, device substitution, or orphan
process; partial data verifies after a terminal failure. Record every `dmesg` USB
event in the window so a transport loss is not filed against ingest.

### X-10 · Rapid start/stop and limit cycling

Repeat ≥100 ADB captures and ≥100 growing follows, alternating immediate stop,
one-line, one-second, short-duration, and byte-cap endings. Every session gets a
unique path; short captures finalize; no double-start or invisible source appears;
`adb` children, descriptors, and threads return to the baseline envelope; Stop
remains idempotent.

### X-11 · Concurrent source saturation

Discover the product's actual concurrent-operation limit by starting long imports,
follows, and captures until one visibly queues. Keep active sources producing,
cancel only the queued work, then release slots in varied order. Queued work is
named *preparing*, cancellation affects only it, fairness is reasonable, and every
active source finalizes correctly.

### X-12 · Signal and process-kill phase matrix

Terminate the exact PID at preview, materialization, ingest before and after the
first snapshot, compaction, manifest replace, portable extraction publication,
standard save, portable save, CSV export, diagnostics bundle, and ADB
finalization. Run each phase twice: once with `SIGTERM` (the logout and
`systemctl` path) and once with `SIGKILL`. Also send `SIGHUP` to a process started
from a terminal that then closes, and `SIGINT` from the controlling terminal.
Relaunch and classify the exact residue: `SIGTERM` must get an orderly drain or a
truthful partial; `SIGKILL` may leave a recoverable partial but never an unsafe
published destination, a corrupted prior session, an orphan `adb`, or a stale lease
that locks a session out permanently.

### X-13 · Reopen while finalizing and the view-query race

Reopen and query a session while it finalizes, and run a search as a capture
finishes. A superseded view query may not relabel a finished capture as failed, and
a completed import must redraw when what is on screen was computed from an older
snapshot generation than the tab holds. Use the structured diagnostics
`snapshotGeneration` values as the oracle.

### X-14 · Filesystem contention matrix

With an indexer (`tracker-miner-fs` or `baloo_file`), a backup agent, `updatedb`,
and optionally `clamd` active, run saves, exports, and finalization. Separately,
have a controlled test process hold a descriptor on a new manifest or destination
for a bounded interval, and separately make the destination directory briefly
unwritable. Bounded retries tolerate transient conditions; cancellation stays
prompt; a persistent condition produces a precise failure. **Do not** expect
Windows sharing-violation semantics: on Linux an open descriptor does not block a
rename or unlink, so the failure modes here are different and the oracle is the
published result's integrity, not an access error.

### X-15 · Low disk, quota, and read-only remount at every publication boundary

On a dedicated loop-mounted filesystem, drive free space toward zero during
materialization, ingest, compaction, manifest replace, save, export, and
diagnostics; separately exhaust a user quota; separately remount read-only
mid-write. Each boundary fails visibly with a specific reason, leaves no
half-published destination, keeps committed data recoverable, and never fills the
root filesystem. Record `ENOSPC`, `EDQUOT`, and `EROFS` handling separately — they
are different errors with different remedies.

### X-16 · Memory pressure, cgroup limits, and systemd-oomd

Run a large import inside a transient scope with a hard memory limit
(`systemd-run --user --scope -p MemoryMax=...`), and separately under
`systemd-oomd` pressure, and separately with swap disabled. The product either
completes within the limit or fails with a message; if the kernel kills it, the
session must be recoverable and the next launch must say what happened. Record the
OOM kill in `dmesg` and `journalctl` so an external kill is never filed as a crash.

### X-17 · Bulk-load completion, cancellation, close, and shutdown

Load all rows of a huge filtered result; cancel midway; close the tab during the
load; quit the application during the load. The action names the platform ceiling
and the remaining rows, stops at that ceiling and says so, streams progress, and
cancels promptly. Tab and application close complete within 5 s without waiting for
all rows and without throwing.

### X-18 · Deep zoom and precision boundaries

Zoom to microsecond spans, to a single instant, and to an empty region; pan to both
ends. Pixel and data precision are clamped so one instant is never printed as two
different labels; panning is bounded to the session with no phantom time; a nearly
empty plot does not over-claim precision.

### X-19 · Paging to the end of huge filtered results

Page to the true end of a multi-million-row filtered result. Counts, keyset paging,
and the end-of-range statement stay exact; contextual action slots never shift; no
page is skipped or repeated.

### X-20 · High session count and cache churn

Create several hundred sessions, then exercise recent lists, the cache view, and
retention. Listing stays responsive, the computed size matches the filesystem,
retention removes exactly what it names, and the lease directory does not accumulate
stale files.

### X-21 · Repetition leak pass

Repeat open → analyze → close 200 times over mixed sources, and repeat tab
open/close and dialog open/close 500 times. RSS, descriptors, mapped regions,
threads, and GC cadence return to the baseline envelope; no queued redraw reads a
disposed snapshot; the process survives every close.

### X-22 · Multi-instance collision soak

Run two or three instances for several hours against overlapping sessions and
settings, with `vcat` operations interleaved. No settings regression, session
corruption, lease leak, or cross-instance state appears; a late diagnostic write at
shutdown cannot reach a disposed sink or extend the process lifetime.

### X-23 · Display, GPU, compositor, and remote transition gauntlet

During active work: change resolution and scale, rotate an output, unplug and
reattach a monitor, switch the primary output, restart the compositor where the
desktop supports it (`kwin_x11 --replace`), switch between hardware GL and
`LIBGL_ALWAYS_SOFTWARE=1` across launches, connect and disconnect a remote session,
and switch virtual desktops. The window stays reachable and correctly scaled;
rendering recovers; no stale-scale frame persists; a compositor restart does not
take the application with it, or, if the X connection is genuinely lost, the
failure is reported rather than producing a silent zombie window.

### X-24 · Path, name, and alternate-filesystem soak

Run ordinary workflows for several hours with sources and destinations on a very
deep path, a non-UTF-8 name, a case-insensitive mount, a permission-less mount, an
NFS or SMB share, an `sshfs` mount, and an overlay or FUSE filesystem — each as its
own pass. Interrupt the network mount mid-write once. Supported local paths behave
exactly as on ext4; unsupported storage fails honestly; a disconnected network mount
produces a specific error and leaves the source intact.

### X-25 · Large export and diagnostics denial-of-service

Export a multi-million-row CSV and build a diagnostics bundle under low disk, with
cancellation, and with the destination removed mid-write. Each is bounded, states
its progress, cancels promptly, and never leaves a partial file presented as
complete.

### X-26 · Session corruption and verifier matrix

Open and verify every §3.2 damaged session copy, one fault per copy, including the
Linux-only mode and symlink faults. Each fault is detected and named; no fault is
silently repaired; a symlinked segment is never followed outside the session root;
an unreadable segment produces a permission-specific message; the verifier's
resource use stays bounded.

### X-27 · Clock, zone, and tzdata changes during live work

During a capture and an import: step the system clock forward and backward, change
the time zone, cross a DST boundary with a zone that has one, and update or remove
`tzdata`. Monotonic duration and progress must not break; file names and retention
follow wall clock and are accounted for; stored instants are not retroactively
reinterpreted; a suspend-resume clock jump is handled the same way.

### X-28 · Host reboot, logout, and crash-recovery handoff

Interrupt with a hard power cut, with `reboot`, and with a session logout during
ingest, save, and capture. After restart, classify the residue and recover. Nothing
unsafe is published; committed data verifies; a partial session is offered as
recoverable; leases left behind by the killed process do not block reopening.

### X-29 · Descriptor and mapping exhaustion

Lower `ulimit -n` to a realistic desktop value and `vm.max_map_count` to its
documented floor on a dedicated host, then open many large sessions and many tabs.
The product either stays within the limits or fails with a message naming the
resource; it never corrupts a session, leaks descriptors on the failure path, or
crashes with an unhandled `EMFILE`/`ENOMEM`. Record the descriptor and mapping
counts at each step — a memory-mapped session store is exactly the design that
finds these limits first.

---

## 8. Tier U — desktop UX, UI, input, and accessibility

Run U with ordinary human interaction first, then with automation and accessibility
tools. A tree dump cannot prove that a workflow is understandable or usable. Record
the G-state for every row; a U result without it is not comparable across hosts.

---

### U-01 · Window-size and responsive-command matrix

Exercise the declared minimum (900×600), 1024×768, 1280×720, 1366×768, 1440×900,
1920×1080, 2560×1440, 4K, maximized, and half and third tiled layouts. The command
bar keeps the primary open and capture actions inline; flexible actions fold into
*More* without clipping; status, notice, and tab strips stay reachable; plot,
minimap, entries, and source stay inside their bands; the minimap keeps a usable row in
the plot column even at the shortest supported viewport; nothing overflows
horizontally. A clipped session tab can still
be brought into view and closed.

### U-02 · Scaling matrix

Repeat key screens at 100%, 125%, 150%, 175%, and 200% effective scale, produced
three different ways and recorded separately: the desktop's display scale, a
`text-scaling-factor` change, and `AVALONIA_SCREEN_SCALE_FACTORS`. Record the
logical and physical bounds each time. Text, icons, borders, hit targets, and
timeline pixels scale consistently; there is no clipped control, no subpixel gap,
and no mismatch between pointer and visual. Under XWayland, note where the
compositor is upscaling a 1× surface rather than the app rendering at scale — blur
from compositor upscaling is a platform property to record, not a layout defect.

### U-03 · Mixed-scale monitor crossing

With two outputs at materially different scale and resolution, move the window
fully and straddled across the boundary; open the file chooser, import review, ADB
dialog, settings, and a confirmation before and after the move; maximize on each.
Owned dialogs appear on the active output at the correct scale; hit testing and
screenshot coordinates stay aligned; the window never jumps off-screen or becomes
unreachable. Under X11 a single global scale means a "mixed-DPI" desktop is really
one scale across two physical densities — record that rather than reporting the
resulting size difference as a bug.

### U-04 · Multi-monitor coordinates and hot-plug

Place the secondary output left of or above the primary so virtual coordinates go
negative; close the window there; unplug the output; relaunch; reattach; change the
primary. The window and its dialogs remain reachable; a saved size is honored
without restoring an invalid position; the taskbar or dock entry identifies the
candidate.

### U-05 · Window-manager matrix

Repeat U-01's key sizes, a modal dialog, a minimize and restore, and a close under
GNOME (Mutter), KDE Plasma (KWin), Xfce (Xfwm), and one tiling WM. Decorations,
minimum-size handling, modal ownership, always-on-top behaviour, keyboard close
route, and taskbar identity all work or degrade visibly. A tiling WM that ignores
the minimum size must still leave a usable layout; `WM_CLASS` and the window icon
must identify the app in every WM's switcher.

### U-06 · Keyboard contract, accelerators, and focus order

Execute every row in [`KEYBOARD.md`](KEYBOARD.md), including the timeline `J`/`K`/`F`
keys, match wrapping, `Alt+1`..`Alt+4`, the `Ctrl` shortcuts, `Ctrl+G`, `Alt+Home`,
`Alt+End`, and Escape precedence. Each works from the workspace and from the search
field where documented, says why it is inert when it is, and never fires while
typing unless it uses Ctrl, Alt, Escape, or a function key. Focus order follows the
documented desktop order — search, then severity, then timeline, then the analysis
panes — and no control is reachable only by pointer.

Cover the *Recent captures* keyboard contract in full, because it is the one place the
keyboard can destroy data: arrows, Home and End move through the list without changing
a check; Space toggles the focused row only when that capture can be deleted; `Ctrl+A`
checks every deletable capture and `Ctrl+Shift+A` clears every check; Delete confirms
deletion of the checked captures; Enter or double-click opens the **highlighted**
capture; and Escape clears checks on the first press and closes the dialog on the
second. Highlight and checks stay independent, the confirmation's initial focus and
default action is **Cancel** so Enter there never deletes, and after a deletion focus
lands on the nearest surviving capture — or on the remaining action when the last one
goes. A focused button, checkbox, or text selection keeps its own keys, and nothing in
the list is claimed while a confirmation or the results view owns the keyboard.

Repeat the whole pass once under a non-US keyboard layout to confirm accelerators are
bound to physical keys sensibly rather than to characters the layout does not
produce.

### U-07 · Orca end-to-end pass

With Orca running, complete the B-19 journey by listening. Entry rows announce
level, tag, time, and message — never a session GUID, a raw span, or a private
storage path. Insights, both stored-session lists, the notice lane, and dialogs are
all announced. Live updates are announced without flooding. Record Orca and AT-SPI
versions; where Avalonia's AT-SPI surface cannot express a relationship, record that
limitation explicitly rather than marking the row N/A.

### U-08 · AT-SPI tree and modal boundary

Inspect the tree with `accerciser` or an equivalent. Every interactive control has a
role, an accessible name, and state; disabled commands say why; a sheet or dialog is
modal to assistive technology and not only to the pointer — the tree must not allow
walking past a scrim into the workspace behind it.

### U-09 · Desktop high contrast and product high contrast

Enable the desktop's high-contrast theme and, separately, the product's own
high-contrast mode, then both. Selection, focus, the tab underline, list surfaces,
severity colors, and the timeline remain distinguishable and meet the §4.4 contrast
floor. The product owns its accent: a loud desktop accent color must not replace
the product palette in a way that destroys the severity encoding.

### U-10 · Light, dark, and System theme live changes

Switch the desktop `color-scheme` with the app running, and start cold in each.
Record whether the product follows the desktop preference on this desktop
environment at all — an X11 client may not be told — and whichever it does, the
product's own theme setting must work, must repaint every surface including the
minimap, source view, tab strip, and dialogs, and must need no restart.

### U-11 · Desktop text scale and product text scale

Vary the desktop `text-scaling-factor` and the product's text scale independently
and together. Both reach the chrome and every open workspace, which remeasure
together without replacing the session or ending a capture. No text is clipped and
no row floor is violated.

### U-12 · Magnifier, cursor, and color aids

With the desktop magnifier on, and with a large cursor, and with a color filter or
inversion applied, complete a short journey. The product remains usable, the caret
and focus stay in the magnified viewport, and no information is conveyed by color
alone.

### U-13 · Reduced motion

Disable desktop animations (`enable-animations false`). Transitions shorten or
disappear; nothing depends on an animation to become reachable; no control ends up
permanently mid-transition.

### U-14 · Pointer and libinput touchpad interaction

Exercise click, double-click, drag, right-click, middle-click, wheel, horizontal
wheel, two-finger scroll, pinch, and kinetic scroll, with natural scrolling on and
off. Hit targets match their visuals; a drag that leaves the window ends sensibly;
middle-click does not trigger an unintended command where the desktop uses it for
PRIMARY paste.

### U-15 · Touch and pen, where hardware is available

On a touch-capable host, exercise tap, long press, drag, pinch, and two-finger pan
on the timeline and lists, and pen hover and input where supported. Gestures do not
carry a second meaning, scrolling and zooming are distinguishable, and no control
is too small for touch. N/A requires recorded absent hardware.

### U-16 · IME, keyboard layouts, dead keys, and Compose

With ibus and, separately, fcitx5, type into the search field, a saved-view name,
and a file-name field: a CJK composition, a dead-key accent, a Compose sequence, and
a clipboard paste of non-ASCII text. Candidates appear where the caret is, committed
text lands once and intact, and a composition in progress is not submitted by a
shortcut. Record the framework and version; a limitation of Avalonia's X11 IME
support is recorded explicitly, not silently passed.

### U-17 · Clipboard and X11 selections

Copy a message, a raw line, a count, and a path; paste into another application and
back. Verify the CLIPBOARD selection carries exactly the copied text with no added
or lost whitespace. Then test the Linux-specific cases: selecting text and
middle-click pasting (PRIMARY), copying and then **quitting VisualCat** before
pasting, and copying with and without a clipboard manager running. An X11 client
owns its selection, so a copy lost when the app exits is expected X11 behaviour
unless a clipboard manager is present — record which, and only file a finding if the
product fails to offer the selection while it is running.

### U-18 · Locale, number and date culture, and RTL content

Run under a comma-decimal locale, a split `LC_NUMERIC`, and an RTL locale, with RTL
and bidi log content loaded. Dates and numbers are internally consistent — ISO dates
and interface-culture numbers throughout, never the host's conventions mixed into
one English surface. Bidi content cannot reverse the direction of surrounding UI or
move a control.

### U-19 · Dialog ownership, Alt+Tab, Escape, and window close

Open each dialog and sheet. Each is modal to its owner, appears over it on the
correct output, is dismissible by Escape and by the WM close affordance, returns a
result, and never appears in the window switcher as a separate top-level
application. Alt+Tab identifies the candidate by title and icon.

### U-20 · Notice lane and status messaging

Trigger every durable action — copy, mute, save, export, cleanup, delete, failure —
and read the notice lane. Every meaningful action reports where the reader is
looking; a notice never moves a repeated action, so a second click in the same place
is the same command; a quiet status stops claiming arrivals; messages are product
sentences, not exception text.

### U-21 · Empty, loading, quiet, partial, degraded, and failed states

Reach each state deliberately and read it: empty workspace, loading, a quiet live
source, a partial recovered session, a degraded index-only session, and a failed
import. Each explains itself in one place with a reason, a remedy, and viable
actions; a failed import is never a hollow workspace with inert panes.

### U-22 · First-run comprehension with a fresh participant

With a participant who has never used the product, and no guidance beyond
`README.txt`, observe: extract, launch, open a log, find an error burst, read one
record, and export it. Record where they hesitate, what they misread, and what they
could not find. Use three independent participants for the release gate.

### U-23 · Visual regression sweep

Capture the §4.4 state matrix on G0 at a fixed size, scale, theme, text scale,
culture, and font set, and compare with the reviewed baseline. Record the resolved
font families with every capture; a host whose `fc-match monospace` differs produces
a legitimately different image.

### U-24 · Zoomed and long-content layout

With the largest text scale and the longest content — 2 MiB lines, very long tags,
long file names, many chips — confirm that an unselected entry row ellipsizes near
the actual available width, a selected row wraps within its budget, and no chrome
overlaps or clips.

### U-25 · Dynamic accessibility announcements

With Orca running, start a capture, let progress advance, complete it, and trigger a
failure. Progress, completion, and failure are announced once each, not repeatedly,
and never at a rate that makes the product unusable.

### U-26 · Taskbar, dock, window identity, and lifecycle

Confirm the window title, icon, and `WM_CLASS` (`xprop WM_CLASS`) identify the
product in the switcher, the taskbar or dock, and any window list. Repeat under each
G4 desktop. Record that no `.desktop` file is shipped — so there is no launcher
icon, no pinning, and no MIME association — and that this absence matches
[`SUPPORT.md`](SUPPORT.md). A desktop that shows a generic placeholder icon because
no `.desktop` entry exists is expected; a window with no `WM_CLASS` at all is a
finding.

### U-27 · Font availability and fallback

Record `fc-match monospace` and `fc-match sans-serif`, then, on a dedicated host,
remove the font that resolves for monospace and relaunch; separately remove all of
the fallback families the product names; separately load content with CJK, emoji,
and combining characters. The entry list and timeline labels remain legible and
monospaced where the design requires alignment; a missing family falls back
visibly rather than rendering boxes in a column whose alignment the product depends
on; missing glyphs are tofu rather than a crash or a blank row. A host with no
`fontconfig` at all is covered by B-02.

---

## 9. Tier I — CLI, cross-platform, and artifact integration

The Linux CLI is the surface most Linux users will automate, and Linux is the
platform where a cross-platform byte-parity defect is most likely to be found —
path separators, newline forms, case sensitivity, and culture all differ here.
Every parity row names its independent oracle.

---

### I-01 · Matching Linux CLI artifact identity

Verify `<cli-tar>` exactly as §2.4 verifies the desktop archive: checksum,
`SHA256SUMS` line, provenance, member safety, inventory, mode bits, and hashes.
`vcat` must extract executable, report a version equal to the desktop candidate's,
run with no system .NET, and resolve every library. `vcat --version`, `vcat help`,
and `vcat` with no arguments each behave as [`CLI.md`](CLI.md) documents, write to
the documented stream, and exit with the documented status.

### I-02 · File import desktop/CLI parity

Index each corpus with `vcat index` and import the same file in the desktop with
the same explicit options. Entry counts, outcome counts, first and last instants,
severity totals, facet tallies, template identities, and raw byte ranges are
identical. Where they differ, the corpus manifest — not either implementation —
decides which is wrong.

### I-03 · Desktop save verified by CLI

Save standard and portable sessions from the desktop, then run `vcat verify`,
`vcat info`, and `vcat stats` against them. Every check passes and every reported
figure matches what the desktop displayed.

### I-04 · CLI session opened and saved by desktop

Index with `vcat`, open the result in the desktop, modify nothing, save, and verify
again. The session round-trips without a reparse and without the desktop rewriting
data it only read.

### I-05 · Marker-bounded desktop/CLI ADB parity

First compare discovery: `vcat adb-devices` must print a JSON array whose items carry
`serial`, `state`, the optional `model`, `product`, and `transportId`, and the parsed ADB
`properties`, and it must agree with the desktop's device list — including for a device
in `no permissions`, `unauthorized`, or `offline` state.

Then capture the same marker-bounded interval with `vcat capture-adb` and with the
desktop, against the same device. Within the marker-bounded window and the declared
drops, entry sequences agree. Two live captures are never directly equal by total alone
— use the markers.

### I-06 · Desktop and CLI reconnect semantics

Interrupt the transport during both a CLI and a desktop capture. Both bound their
reconnect attempts, both resume from a numeric cursor, both count gaps the same
way, and both report the same terminal outcome for the same interruption.

### I-07 · Export equivalence

Export the same scope from the desktop and with
`vcat export <session> <output> --type csv` using the same range, order, and
filters. The files are byte-identical, including newline form, encoding, quoting,
and column order. Compare with `cmp`, not with a text diff that can normalize line
endings.

### I-08 · Portable round trip through Android and Windows

Exchange a `.vcat.zip` in both directions with the Android companion and with the
Windows candidate. Counts, instants, severity totals, template identities, and raw
hashes survive every hop. Archive entry names use forward slashes throughout and
are interpreted identically on both desktops.

### I-09 · Simultaneous desktop, CLI, and Android capture

Capture from the same device with the Linux desktop, `vcat`, and the Android
companion at once, where the transport allows it. Each produces a valid session;
none corrupts another; ADB server contention is reported rather than silently
dropping a reader.

### I-10 · Startup argument dispatch parity

Compare the desktop's `--log`, `--session`, and bare-path handling with the CLI's
equivalent argument handling, including a path with spaces, a path with a newline,
a non-UTF-8 path, a missing file, and a directory in a file position. Both surfaces
resolve and reject the same inputs the same way, and both quote correctly when they
report a path back.

### I-11 · Cross-culture and time-zone reproducibility

Index and export the same corpus under `C`, `en_US.UTF-8`, `cs_CZ.UTF-8`, an RTL
locale, and `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, and under at least three
`TZ` values including one with DST. Machine-readable output is identical across
locales; human-readable output changes only where the contract says it may; stored
instants never change with the presenting culture.

### I-12 · Published archive rehearsal

Reproduce the release layout locally with `tools/package.ps1 -Runtime linux-x64
-Archive` **on a Linux host**, compare the inventory and layout with the published
archive, and run `tools/verify-package-contents.ps1` against both. Record every
difference. A locally built archive with the same version but different bytes is
diagnostic evidence only. An archive produced on Windows may differ in mode bits by
design; state that rather than filing it.

### I-13 · Shell, pipes, exit codes, signals, and cancellation

Run every `vcat` command with stdout to a pipe, to a file, and to `/dev/null`; with
stderr redirected separately; under `set -e`; with `LC_ALL=C`; and with a closed
downstream (`vcat query … | head -1`). Exit codes match [`CLI.md`](CLI.md). NDJSON
output is one entry per line and is consumable by `jq` a line at a time. A closed
pipe terminates quietly with the documented status rather than printing a stack trace.
Mutation commands print absolute destination paths.

Two documented contracts need their own assertions. Progress is written **only** when
standard error is connected to a terminal, so run each long command once interactively
and once with stderr redirected to a file and to `/dev/null`, and prove that the
redirected structured output is clean — no progress, no spinner, no carriage-return
repainting — while the interactive run does show progress. And `Ctrl+C` requests
cancellation such that completed session generations remain recoverable: interrupt an
`index` and a `capture-adb` at several points and verify each surviving session with
`vcat verify`.

`SIGTERM` behaves as `SIGINT` does. No command requires a TTY, and none emits ANSI
escapes into a non-TTY stream unless documented.

### I-14 · Deterministic test-log generator and format matrix

Generate each of the five formats with a fixed seed, twice, on Linux and on
Windows. Output is byte-identical across runs and across platforms for the same
seed and options; the requested format is honored exactly and never silently falls
back; every generated file is detected as its own format at full confidence. This
row gates the use of generated corpora as setup for every other result.

### I-15 · Cross-platform byte parity with the Windows candidate

Using the same corpus, produce on both platforms: an index, a portable archive, a
CSV export with identical options, and a `stats` and `templates` JSON document.
Compare each pair with `cmp`. Differences are permitted only where the contract
names them — and a newline form, a path separator inside a manifest, a culture-
formatted number, or a case-folded identifier is **not** such a place. Verify the
newline claim by counting bytes, never with a locale-sensitive `grep` pattern.

---

## 10. Tier P — privacy, security, and negative scenarios

Use only synthetic secrets (`VCAT_SECRET_<run-id>`) and dedicated accounts and
filesystems. Security tests must not weaken or attack systems outside scope.

---

### P-01 · No unsolicited network traffic

With a clean profile, capture per-process network activity while cold-launching,
importing, querying, saving, exporting, building diagnostics, and sitting idle for
15 min. Use `ss -tanp` sampling, `strace -f -e trace=network`, `nethogs`, or a
transient unit with `IPAddressDeny=any`, and repeat with ADB disabled and enabled so
local ADB socket traffic is distinguished from anything else. Expect no
VisualCat-originated telemetry, update check, or content upload. An explicit
releases-page action may launch the desktop's default browser; the application
itself must not fetch anything silently.

### P-02 · Data locality and declared XDG storage

Trace file writes for ordinary workflows with `strace -f -e trace=openat,unlinkat,
renameat2` or an equivalent. Writes stay inside the chosen destinations,
`<data-home>/VisualCat/{Sessions,Diagnostics,SessionAccess-v1,settings.json}`, and
bounded temporary materialization roots under `$TMPDIR`. No source payload reaches
`$HOME` outside the data root, the extraction directory, `/tmp` outside its own
subtree, another user's tree, a network location, or `~/.config` unless explicitly
selected.

### P-03 · Diagnostic redaction

Place a synthetic secret in a log message, a source path, a search string, a
saved-view name, an ADB serial-like token, and the clipboard. Generate the bundle and
search every entry, including compressed and binary members, with `zipgrep` and with
`strings`. None appears; no hash or path enables easy payload recovery beyond the
declared sanitized metadata; the confirmation exactly matches the contents. A
redaction failure is a Blocker.

### P-04 · Portable archive traversal, link, and expansion safety

Open the §3.2 hostile archives: entries with `..`, absolute paths, drive-qualified
names, symlinks and hard links pointing outside the archive, duplicate and
case-colliding paths, excessive count, depth, and name length, POSIX mode bits
including setuid and world-writable, FIFO and device entries, encrypted entries, a
corrupt central directory, and an expansion bomb. Each is refused before unsafe
publication; nothing appears outside the exact temporary root; no file is created
with a setuid or world-writable mode; disk and time limits hold; temporary data is
removed; the source archive is unchanged. Verify with `find -newer` that nothing
outside the root was created or modified.

### P-05 · Untrusted log rendering in the GUI

Import `controls.txt` and content with NUL, ANSI CSI and OSC sequences, bidi
overrides, zero-width characters, huge tokens, HTML and Markdown, CSV-formula-like
strings, and synthetic URLs. They render and copy as data. No control sequence
changes surrounding layout, launches a URI or a process, changes the direction of
the UI, or creates active content. CSV export quotes fields per RFC 4180 and
rewrites nothing, which — as [`CLI.md`](CLI.md) and
[ADR 0021](adr/0021-csv-export-fidelity.md) state explicitly — is **not** a defence
against formula interpretation: confirm the product and its documentation say so
and tell the reader to import an untrusted log's CSV as text. Test that claim
without opening the file in an unsafe spreadsheet configuration.

### P-06 · Untrusted content in a terminal

This is a Linux-weighted risk the GUI plan cannot cover: `vcat` prints log content
to a terminal, and a terminal interprets escape sequences. Run `vcat query`,
`search`, `info`, `stats`, and `export --stdout` against `controls.txt` in a real
terminal emulator and capture the raw bytes with `script` or by redirecting to a
file. Expect that content reaching a TTY is either escaped, or documented as
passed through verbatim; either way, it must not be able to set the window title,
move the cursor arbitrarily, start a new shell prompt line that impersonates one,
trigger a terminal response sequence that gets injected back as input, or alter the
terminal's state after the command exits. A sequence that survives into the
terminal and changes its mode is a Blocker; one that is printed literally is fine.

### P-07 · Session verifier and parser resource bounds

Run the verifier and the parser against the corruption matrix, the 2 MiB-line
corpus, the pathological regex corpus, and the expansion bomb, under a bounded
`ulimit -v` and a bounded time. Each terminates within its declared bounds with a
specific result; none spins, allocates without limit, or recurses until it faults.

### P-08 · Symlink, hard link, bind mount, and case boundary

Point sources, session roots, export destinations, and the lease directory at
symlinks, hard links, bind mounts, and a dangling symlink. Also place a symlink
*inside* a session directory where a segment belongs. The product resolves or
refuses consistently with its documented contract, never writes through a link to a
target outside the intended root, never follows a symlinked lease or session root,
and never deletes a link target when it means to delete a link. On a case-sensitive
filesystem two case-differing session paths remain distinct; on a case-insensitive
mount the product does not create two records for one path. Verify with
`stat -c '%i %n'` that the inode written is the inode intended.

### P-09 · Permission and user boundary

As an ordinary user, attempt operations against another user's home, a root-owned
directory, a `chmod 000` source, a `chmod 500` destination, a directory without
execute permission, and a file with an ACL that denies the running user
(`setfacl -m u:$USER:---`). Each fails visibly with a specific reason, never with a
`sudo` prompt, never by silently choosing another destination, and never by
partially writing. No elevation is required for any ordinary analysis path.

### P-10 · Temporary-file and atomic-publication boundary

Observe materialization, manifest replacement, save, export, and diagnostics with
`strace`. Temporary files live under the declared root, are created with modes that
do not expose content to other users on a shared `/tmp`, are renamed atomically into
place within the same filesystem, and are removed. Then attack the boundary on a
dedicated host: set `TMPDIR` to a world-writable directory, pre-create the expected
temporary name as a symlink to a file you own elsewhere, and set `TMPDIR` to a path
on a different filesystem so a rename cannot be atomic. The product must not follow
a pre-existing symlink, must not publish a half-written destination, and must report
a cross-filesystem publication honestly rather than leaving a partial file.

### P-11 · Process creation and command-line safety

Inspect every child process the product spawns (`ps -ef --forest`,
`/proc/<pid>/cmdline`). `adb` is invoked with an argument array, not through a
shell; no file name, serial, pattern, or log content can inject an argument or a
shell metacharacter; no secret appears in a command line visible to other users in
`/proc`; the child inherits no more environment than it needs; and every child is
reaped rather than left as a zombie.

### P-12 · Dynamic-loader and executable-directory integrity

On an isolated host: run the candidate with a hostile `LD_LIBRARY_PATH` and with a
hostile `LD_PRELOAD` containing an inert, hash-recorded probe library; place a
same-named library beside the executable and in the current working directory; run
from a world-writable directory; and run from a `noexec` mount. Record what the
loader does (`LD_DEBUG=libs`). The product must resolve its bundled libraries from
its own directory rather than from the working directory, must not be made to load a
library from a world-writable path it did not intend, and must fail clearly on a
`noexec` mount rather than partially starting. Document the trust assumption that a
portable extraction directory makes, since `rpath`-relative resolution is how a
self-contained publish works.

### P-13 · Archive provenance and unsigned-artifact honesty

The Linux release is unsigned and has no distribution signature chain. Confirm that
`README.txt`, the release notes, and `SUPPORT.md` together tell a user how to
establish trust — checksum against `SHA256SUMS`, then the provenance attestation —
and that nothing in the product or its documentation claims a signature, a
publisher identity, or a package-manager guarantee it does not have. A tampered copy
must fail the checksum and the attestation; verify that by altering one byte of a
throwaway copy.

### P-14 · Clipboard and sensitive display

Copy content containing a synthetic secret; confirm it goes only to the selection
the user asked for; confirm it is not also written to a file, a log, or the
diagnostics bundle; confirm that a clipboard manager's history is the desktop's
behaviour and is recorded as such. No secret is displayed in a title, a notice, or a
tooltip that the user did not ask to reveal.

### P-15 · Core dumps, apport, journald, and error redaction

Force a crash path on a dedicated host and inspect what the system captured:
`coredumpctl` storage, `apport` reports under `/var/crash`, and `journalctl`
entries. A core dump of a log viewer contains log content by construction, so the
requirement is that the **product** does not write payload into a system log or a
crash report it controls, and that any message it does emit is a product sentence
without payload. Record whether the host is configured to upload crash reports —
Ubuntu's `apport` can prompt to send one — because that is a host policy, not
product telemetry, and it will otherwise look like the app phoning home. Delete
collected dumps per the retention policy.

### P-16 · Cache cleanup cannot escape or delete active data

With a seeded cache, run cleanup while a session is open, while one is protected,
while one is held by a second process, and with a symlink planted inside the cache
root pointing outside it. Cleanup removes exactly what it names, never follows the
symlink out of the root, never deletes an open or protected session, and reports a
reclaimed size that matches the filesystem.

### P-16.1 · Deleting captures from Recent captures

Delete a capture from the list while it is open, while another process holds it,
after it has been saved elsewhere, and when its directory is already gone. The
confirmation names the exact object; deletion removes only that session's tree;
the list, the cache size, and the lease directory all agree afterwards; a failure
to delete is reported rather than silently leaving the entry listed.

### P-17 · Multi-user isolation

With two accounts on one host, confirm each user's sessions, settings, diagnostics,
and leases are readable and writable only by that user (`stat -c '%a %U'` across the
tree), that one user cannot see another's session list, and that a session on a
shared path is not silently readable by the other account because of an overly
permissive mode.

### P-18 · Removal and residue accounting

Delete the extracted directory and account for exactly what remains: the XDG data
root, any temporary files, and any desktop-level state. Then delete the data root
and account for what that removes. Document that removing the program directory is
not removing user data, and that neither is a secure wipe. Confirm the product
created no systemd unit, no autostart entry, no `.desktop` file, no MIME
association, and no shell profile modification.

### P-19 · ADB authority and shared-server boundary

Confirm that the product uses an existing ADB server rather than commandeering it,
that it does not kill a server it did not start, that it never reads or copies
`~/.android/adbkey`, and that its device commands are serial-qualified. Record the
`udev` and group mechanism the host uses for USB access and confirm the product does
not attempt to modify it or ask for `sudo` to do so.

### P-20 · Export, path, and error disclosure

Trigger failures with long, hostile, and non-UTF-8 paths. Messages name what the
user needs without dumping an internal stack, a framework resource key, an absolute
path the user did not supply, or another user's path. A path reported back is quoted
so it can be copied and used.

### P-21 · Confinement honesty under AppArmor and SELinux

On a host with AppArmor enforcing and, separately, SELinux enforcing, run the
primary journey. If a policy denies an operation, the product's message names the
operation and the path rather than reporting a generic failure, and the denial
appears in the host's audit log (`ausearch -m avc`, `journalctl -k`) so it can be
attributed correctly. The product must not suggest disabling the policy as the first
remedy.

### P-22 · File modes and ownership of created data

Inspect the modes and ownership of everything the product creates — the data root,
session directories, segment files, `settings.json`, diagnostics, leases,
temporary files, exports, and portable archives — under `umask 022` and under
`umask 077`. Nothing is world-writable, nothing is setuid or setgid, nothing is
owned by another user, and a settings file or diagnostics record containing user
data is not world-readable when the account's `umask` says it should not be.

---

## 11. Tier R — regression pack for released and current fixes

These guards derive from [`CHANGELOG.md`](../CHANGELOG.md) and current source
comments. Re-derive the table whenever a release adds a Linux-visible fix. "First
fixed" names the changelog section that records the behaviour; `Unreleased` means
it must pass before the next tag.

Rows **R-01 to R-06** were found on Linux and are the ones a Linux run must never
skip: they came out of a live Linux pass, several of them are invisible to a
headless test, and one of them is reproducible only as an A/B of the shipped
binary against a real compositor.

| ID | Guard | Procedure | Pass condition | First fixed |
|---|---|---|---|---|
| **R-01** | A large import finishes showing the whole log | X-01, five `--log` launches per build over `large.txt`, reading the summary line and the zoom readout from screenshots | The plot, severity totals, axis, entry list, templates and every derived counter show the **full** session, not a prefix, with no need to press *Fit*; the span beside the zoom controls agrees with the span the plot drew | Unreleased |
| **R-02** | A rejected view query runs again | X-13 | A query whose answers were rejected because the session grew under it is retried rather than dropped, and a completed import redraws when what is on screen came from an older snapshot generation than the tab holds | Unreleased |
| **R-03** | A long import keeps up with itself | X-01, A-35 | A progress refresh arriving while another runs is coalesced into one further pass, not dropped; the plot follows the log instead of stopping at whichever generation won the last race | Unreleased |
| **R-04** | The not-logcat notice states the finished count | B-20 with `outcomes.txt` and `crashy.txt`, repeated five times | The same file reports the same number every run, the number equals the chip and the summary line, and the notice waits until the source has stopped arriving | Unreleased |
| **R-05** | Notices name commands that exist | B-20 | The notice and the menu item come from one name; *Lines not on the timeline…* is what both say | Unreleased |
| **R-06** | `vcat query` emits NDJSON | I-13 | One entry per line, consumable by `jq` a line at a time; every other command still prints one indented document | Unreleased |
| **R-07** | Lines not on the timeline stops over-claiming | B-20 | It does not report that more of the file remains to be scanned once it has listed every line the session counted | Unreleased |
| **R-08** | Stop is answered and sticky | B-11, X-05 | Button and status never return to *Capturing*; the ending resolves and verifies | Unreleased |
| **R-09** | Scanner and contention are tolerated | X-14 | A bounded transient condition succeeds; cancellation and a persistent failure are truthful | 2.0.9 |
| **R-10** | Idle growing follow does not churn | X-06 | One reusable read buffer; no sustained multi-MiB-per-second idle allocation or gen2 cadence | 2.0.9 |
| **R-11** | Closing during a bulk load is prompt | X-17 | Tab and application close within 5 s without waiting for all rows and without throwing | 2.0.9 |
| **R-12** | Live statistics and facets do not rescan history | X-03 | Per-refresh cost plateaus with cached published segments | 2.0.9 |
| **R-13** | The diagnostic logger is safe at shutdown | B-18, X-22 | A late failure cannot write into a disposed sink or extend the process lifetime | 2.0.9 |
| **R-14** | The displayed version tracks the artifact | B-01, B-03 | UI, archive name, `README.txt` and release agree; a non-release build says `-dev` | 2.0.4 |
| **R-15** | Capture and session names distinguish runs | B-17 | Tabs, *Recent*, the cache view and file names carry unambiguous source and start identity | 2.0.4 |
| **R-16** | Settings labels are human language | A-13 | No implementation identifiers such as `PerRow`, `GlobalViewport`, `SourceSequence` are exposed | 2.0.4 |
| **R-17** | Actions report in the notice lane | B-13, B-15, A-35 | Every durable or meaningful action reports where the reader is looking | 2.0.4 |
| **R-18** | The empty state is useful | B-03, B-17 | Correct desktop actions and recent sessions; no inert session command | 2.0.4 |
| **R-19** | Fit is directly reachable and exact | B-08 | One action or key fits; no drawer dependency and no geometry jump | 2.0.4 |
| **R-20** | A failed import is not a hollow workspace | A-08, A-25 | One reason and remedy with viable actions; no inert panes | 2.0.4 |
| **R-21** | An import ends fitted until the user navigates | X-01 | An untouched viewport follows the whole import; the first navigation takes ownership for good | 2.0.4 |
| **R-22** | Closing a tab cannot crash the plot | A-05, X-21 | No queued redraw reads a disposed snapshot; the process survives | 2.0.4 |
| **R-23** | Source context always resolves | B-05, A-24 | Bytes, an explicit interruption, or a retryable failure — never a permanent *Reading* | 2.0.4 |
| **R-24** | Double-click zoom has one meaning | B-08 | Zoom only; no cell filter and no list rescope | 2.0.4 |
| **R-25** | The axis remains a scale inside the plot | U-01, X-18 | Labels never overlap the minimap; a narrow view labels its endpoints | 2.0.4 |
| **R-26** | Contextual actions keep their slots | B-09, X-19 | Paging and load controls never move *Copy raw* or *Entry* between clicks | 2.0.4 |
| **R-27** | Counts name their population | A-02 | Session, filter, viewport and off-timeline scopes are visible on screen, not tooltip-only | 2.0.4 |
| **R-28** | Culture is internally consistent | U-18, I-11 | Dates and numbers never mix host conventions into one English surface | 2.0.4 |
| **R-29** | A nearly empty plot does not over-claim | X-18 | Pixel and data precision are clamped; one instant is never printed as two labels | 2.0.4 |
| **R-30** | Live refresh preserves the selected entry | A-24 | Entry and caret restored by id; source remains reachable | 2.0.3 |
| **R-31** | The full message is reachable | B-05 | The selected row and the inspector show the whole message; a clipped cell offers it too | 2.0.3 |
| **R-32** | Short captures finalize | X-10 | Immediate, one-line and one-second captures all produce valid final manifests | 2.0.4 |
| **R-33** | A quiet status stops claiming arrivals | A-22 | The last-second rate falls to zero and a heartbeat names the silence | 2.0.4 |
| **R-34** | Follow belongs only to an active source | B-12 | Follow and new-data affordances disappear when the source closes; re-engaging opens a live-edge span | 2.0.4 |
| **R-35** | ADB time zone follows the negotiated format | A-17, A-20 | A degraded UTC modifier cannot shift every timestamp by the host-to-device offset | 2.0.4 |
| **R-36** | A wrong or unknown serial cannot hang | A-16, X-09 | Pre-flight rejects a missing serial before spawning `logcat` | 2.0.5 |
| **R-37** | ADB buffer attribution is per record | A-17 | `-D` boundaries yield an exact main/system/crash/event/radio facet | Unreleased |
| **R-38** | Startup restore and cancel are not failures | B-16 | A successful open never stays *Opening*, and a cancellation is not shown as a startup error | 2.0.5 |
| **R-39** | Source line and continuation offsets are exact | A-08 | The gutter starts at 1; the selected line and its following context stay visible | 2.0.5 |
| **R-40** | A screen reader hears entries, not dumps | U-07 | Level, tag, time and message only; no GUID, raw span or private path | 2.0.4 |
| **R-41** | A sheet is modal to accessibility, not only to the pointer | U-08 | Assistive technology cannot walk past a scrim into the workspace behind it | 2.0.4 |
| **R-42** | Session commands disable honestly | B-03, U-08 | Save, export and line commands are unavailable without an applicable session and say why | 2.0.4 |
| **R-43** | A theme change repaints the whole product | U-10 | No stale command, tab, list, minimap, source or dialog variant; no restart needed | 2.0.4 |
| **R-44** | The product owns its accent | U-09, U-10 | Selection and focus come from the product palette and stay visible in high contrast | 2.0.4 |
| **R-45** | A notice cannot move a repeated action | U-20 | A second click in the same place is the same action | 2.0.4 |
| **R-46** | An entry row uses the available width | U-24 | An unselected row ellipsizes near the actual width; a selected row wraps within budget | 2.0.4 |
| **R-47** | A filtered-out inspected entry is admitted | A-24 | Entry and *Copy raw* agree, and the UI offers a way back to it | Unreleased |
| **R-48** | Settings writes preserve the newest value | A-13, A-32 | A coalesced workspace write cannot overwrite a newer preference | 2.0.9 |
| **R-49** | Manifest replacement survives readers | X-13, X-14 | A concurrent reader or indexer cannot cause a publication failure or discard an ingest | 2.0.4 / 2.0.9 |
| **R-50** | Cache cleanup protects open sessions | A-12, P-16 | Restore and protection precede cleanup; the preview is recomputed | 2.0.5 |
| **R-51** | A regex error is a product sentence | B-07 | A trimmed Release build never exposes a resource key or a framework dump | 2.0.5 |
| **R-52** | A missing ADB message is actionable | A-15 | It names platform-tools and SDK configuration; never an inert dialog | 2.0.0 |
| **R-53** | Off-timeline evidence is discoverable | B-20 | Timed, untimed and unparsed populations are explicit; the chip and command open the exact source-ordered lines | Unreleased |
| **R-54** | Source gutter codes have a visible legend | B-20, U-07 | Every non-ordinary code is explained on screen and accessibly, never tooltip-only | Unreleased |
| **R-55** | Text scale reaches the active session | A-13, U-11 | Chrome and every open workspace remeasure together without replacing the session or ending a capture | Unreleased |
| **R-56** | A clipped session tab remains closable | A-05, U-01 | The first action brings the tab into view; its close action then works and is never silently disabled | Unreleased |
| **R-57** | Search reaches first, last and numbered matches | B-07 | Direct controls land on the exact oracle identity without thousands of steps or zoom drift | Unreleased |
| **R-58** | The bulk row load is explicit, bounded and cancellable | X-17, X-19 | It names the ceiling and the remaining rows, stops at that ceiling and says so, streams progress, and cancels promptly | Unreleased |
| **R-59** | Export can ignore the active filter honestly | B-15, I-07 | An everything-in-session scope appears when it is distinct and contains the exact unfiltered row set | Unreleased |
| **R-60** | The manual update route matches the install origin | A-28, P-01 | The command is present, never checks silently, says that a GitHub or self-built install is not updated automatically, and opens the official page only on request | 2.0.9 |
| **R-61** | The generator honors the requested format | I-14 | All five formats are deterministic and detected exactly; `long` never falls back to `threadtime` | Unreleased |
| **R-62** | Search navigation reaches every match exactly | B-07 | The counter's total is every match in the session, each step selects one record, and *Fit* still moves | Unreleased |
| **R-63** | Every applied filter value is individually removable | A-02 | A rare, excluded or now-empty value keeps its own undo, and **Find…** reaches values ranking cannot | Unreleased |
| **R-64** | The export writes the scope it offered | B-15, A-04 | Intersection, cell and explicit range each export themselves from one frozen snapshot, and the promised count is the count written | Unreleased |
| **R-65** | File work is visible and stoppable | A-35 | One named operation with progress and a *Cancel* that waits for the writer's actual result | Unreleased |
| **R-66** | Lease storage refuses a symlinked root | A-14, P-08 | A symlinked lease or session root is refused with a clear message rather than followed | 2.0.5 |
| **R-67** | Session paths are compared case-sensitively on Linux | A-26, P-08 | Two paths differing only in case are two sessions, and a snapshot is never reused across them | 2.0.9 |

---

## 12. Execution schedules

| Schedule | Role | When | Contents | Planning time |
|---|---|---|---|---|
| **Artifact smoke** | Cumulative entry gate | Every Linux candidate tarball | B-01–B-09, B-15, B-16, B-18–B-20; I-01, I-12, I-14 | 3–4 attended h |
| **ADB smoke** | Focused reusable slice | Every capture or ADB change | B-10, B-11; A-15–A-20; R-08, R-32, R-35–R-37, R-52 | 3–5 attended h |
| **Standard** | Cumulative sharing gate | Before a candidate is shared | All B; applicable R except the endurance rows; U-01, U-02, U-06, U-10, U-20, U-21, U-27; X-01 | 1.5–2.5 person-days |
| **Upgrade** | Conditional supplement | Whenever settings, session, schema, storage, runtime, or packaging compatibility changes | A-03, A-10–A-14, A-29, A-30, A-32–A-34, A-36; I-02–I-04 on migrated data | 1 person-day plus setup |
| **Full Linux** | Cumulative release core | Before a release tag | Every applicable B/A/U/I/P/R; X-01, X-02, X-04, X-10–X-19, X-24–X-27, X-29; the exact asset is authoritative | 5–8 person-days plus unattended runs |
| **Soak** | Mandatory release supplement | Before a release tag, on a dedicated host | X-03, X-05–X-09, X-20–X-23, X-28; no overlapping measurement workloads | 30–50 elapsed h, 8–12 attended h and review |
| **Display-server** | Focused reusable slice | Any rendering, layout, window, or input change | G0–G7 across U-01–U-05, U-10, U-14, U-17, U-19, U-26, U-27; X-23; A-30 | 1–2 person-days |
| **Accessibility** | Focused reusable slice | Before release and after presentation changes | U-01–U-27, including three independent fresh-participant sessions for U-22 | 1–2 person-days plus participant sessions |
| **Security/storage** | Focused reusable slice | Before release and after archive, path, cache, or permission changes | L3, L4, L7, L8, L9; X-12, X-14–X-16, X-24–X-26, X-29; all P | 2–4 person-days on an isolated host |
| **Parity** | Focused reusable slice | Parser, store, export, session, CLI, or ADB changes | All I plus A-06–A-10, A-17–A-20, A-37, X-26 | 1–2 person-days |
| **Distribution expansion** | Conditional supplement | A new distribution, glibc vintage, desktop environment, display server, or GPU class | B + U + X-01 + X-05, the relevant P rows, and the artifact gate | 12–24 elapsed h plus review |

Times are planning ranges, not pass criteria. Candidate re-downloads, thermal
recovery, large-data generation, distribution updates, device reauthorization,
evidence review, and defect retries extend them. Parallel execution is valid only
on independent hosts, accounts, and devices with distinct run IDs and session and
evidence roots. Two performance workloads on one host are not parallel evidence,
and two VMs on one hypervisor are not two independent performance hosts.

Treat each schedule as a checklist with dependencies, not a bag of IDs. Record
`not started | running | pass | fail | blocked | N/A` for every selected row and
name the prerequisite finding for every Blocked result. Artifact smoke is the entry
gate. Full Linux plus Soak is the ordinary release set; add Upgrade and
Distribution expansion when their triggers apply. Focused slices are planning and
rerun views over rows already present in Full Linux. One scenario result may
satisfy several schedules only when it uses the same exact candidate and meets the
strictest host, profile, G-state, hardware, oracle, and evidence requirement of all
of them; otherwise create a distinct attempt. The schedule manifest must show this
many-to-one mapping so reuse cannot turn an unexecuted matrix cell green.

### 12.1 Change-based minimum selection

| Changed area | Minimum live rerun |
|---|---|
| Packaging, runtime, version, notices | B-01, B-02, B-03, B-16, A-28, I-01, I-12, I-13, P-13, P-18, R-14, R-60 |
| Parser, time, detection | B-04, B-05, B-07, B-20, A-06–A-10, A-20, A-37, X-26, X-27, I-02, I-11, R-53, R-54 |
| Store, manifest, checksums, mapping | B-11–B-14, B-17, A-10, A-11, A-29, A-33, X-12–X-17, X-26, X-29, I-03, I-04, R-49, R-67 |
| ADB | B-10, B-11, A-15–A-20, X-05, X-08–X-10, I-05, I-06, P-19, R-32–R-37 |
| Growing file, framing | B-12, A-21, A-22, X-06, X-10, X-24, R-10, R-32–R-34 |
| Query, filter, search, templates, paging | B-06–B-09, A-01–A-05, X-03, X-04, X-17–X-19, I-07, R-57, R-58, R-62, R-63 |
| Timeline, rendering, layout, theme, tabs | B-05, B-08, B-09, B-18, A-05, A-13, X-04, X-18, X-23, the Display-server schedule, R-19, R-24–R-26, R-43–R-46, R-55, R-56 |
| Settings, cache, retention, XDG paths | A-12–A-14, A-29, A-32, A-34, A-36, X-20, X-22, P-02, P-08, P-09, P-16, P-22, R-48, R-50, R-55 |
| Save, export, archive, diagnostics | B-13–B-15, A-25, A-27, A-35, X-12, X-14, X-15, X-25, X-26, I-03, I-04, I-07, I-08, P-03, P-04, P-10, R-59, R-64, R-65 |
| CLI command, parser, console, generator | I-01–I-07, I-10–I-15, P-05, P-06, P-07, P-11, P-15, R-06, R-51, R-59, R-61 |
| Accessibility, focus, keyboard | B-19, B-20, U-06–U-08, U-11–U-19, U-24–U-26, R-40–R-42, R-53–R-57 |
| Anything touching the snapshot-refresh path | **R-01–R-03 by the X-01 A/B procedure**, plus X-13 and B-20; the unit suite does not cover this |

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
Archive member-safety review result:
Extracted root / inventory hash / mode bits of VisualCat and vcat:
Desktop version as displayed / as in README.txt / as in archive name:
CLI asset / version / hash:
Release tag / commit / channel:

Distribution / version / kernel / architecture:
glibc version / required glibc of the runtime:
Physical / VM (hypervisor) / container / remote:
Machine make/model / CPU / RAM / storage:
GPU / driver / renderer in use / software-rendering fallback:
Display server (X11 | Wayland+XWayland) / compositor / window manager:
G-state(s) exercised:
Display topology: outputs, resolution, scale, refresh, orientation, primary:
Desktop theme / color-scheme / contrast / text-scaling-factor / animations:
Idle, blank, lock and suspend timers as found, and as set for this run:
Portal implementation and backend / DBus session address:
Fonts: fc-match monospace / sans-serif / font packages installed:
Input: pointer/touchpad/keyboard layouts/IME framework/touch/pen:
Accessibility: Orca / AT-SPI / inspection tool versions:
Locale / LC_* / time zone / tzdata version / /etc/localtime target:
Filesystem of home, session root, corpus, evidence: type and mount options:
umask / ulimit -n / vm.max_map_count / file-max:
AppArmor or SELinux mode / noexec mounts / protected_symlinks:
Background indexers, backup, antivirus, sync agents running:
Power source / CPU governor / thermal state:

ADB path / origin / version / hash / udev rules / user groups:
Android serial / model / API / fingerprint / clock / time zone:
VisualCat data root (resolved) / settings / sessions / diagnostics / leases:
Starting data profile D0–D4 / execution state L0–L9:
Corpus manifest path / hash:
Evidence root and access/retention policy:
Trace, recorder and camera configuration:
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
Candidate executable hash / PID(s) / /proc/<pid>/exe:
Start/end UTC:
Starting L-state / G-state / D-profile / preconditions:
Exact source / commands / input / actions:
Expected oracle / budget:
Observed result and verbatim product text:
Measurements: repetitions, median, p95, min/max, samples, method:
Integrity result: hashes / counts / verify / raw ranges / gaps / modes:
UX loop: discoverability / state / acknowledgement / outcome / recovery / consistency:
Accessibility and visual baseline result, with resolved font families:
Host observations: journald / coredumpctl / dmesg / strace / perf:
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
Candidate archive and executable SHA-256 / version / PID:
Distribution, kernel, glibc, display server, compositor, WM, G-state:
Renderer, display topology, scale, fonts, locale, time zone:
Filesystem and mount options, umask, confinement mode:
ADB, device and source state where applicable:
Preconditions and exact reproduction, including the exact command line:
Expected: citation to this plan or a repository contract
Actual: verbatim text, measurement and integrity effect
Reproduction rate / attempts / whether it reproduces on a second distribution:
Whether it reproduces outside a VM:
Time-bounded journald, coredumpctl, dmesg and exit-signal evidence:
Session, corpus and input hashes, and a safe attachment location:
Screenshots / video / traces / diagnostic bundle:
First suspected layer: package | host library | display server/WM | portal | source | ingest | store | query | view model | Avalonia/Skia | ADB
Appendix-B trap checks completed:
Security and privacy handling, and redactions applied:
Workaround / affected users / release impact:
```

Severity: **Blocker** prevents verified launch, loses or corrupts data, executes or
escapes an untrusted boundary, leaks protected content, or prevents the release
gate. **Major** breaks a primary workflow, gives silent wrong results, crashes or
hangs, or makes the product unusable with a required accessibility mode. **Minor**
has a bounded workaround or affects a secondary path. **Polish** is perceptible
without impeding completion or correctness. Severity measures impact, not fix effort
or reproducibility.

### 13.4 Release exit criteria

1. The schedule-accounting manifest covers every applicable row in Full Linux and
   Soak, plus triggered Upgrade and Distribution-expansion rows, with no unaccounted
   scenario or matrix cell. Every B and applicable R row passes on the **exact
   uploaded or upload-ready immutable Linux desktop tarball**; every other required
   row is Pass or has an explicit release exception linked to its finding or gap,
   owner, affected population, risk, mitigation, and expiry. Local source builds do
   not substitute, and an archive created on a non-Unix host never satisfies a
   mode-bit or launch row. I-01 and I-12–I-15 pass on the exact matching CLI
   tarball.
2. Required §1.4 matrix cells are exercised, or each gap has an owner, affected
   population, risk, mitigation, expiry, and explicit release approval. An accepted
   gap remains untested, not passed. At minimum one X11 session **and** one
   Wayland+XWayland session are covered, on at least two distributions of different
   vintage.
3. No open Blocker or Major finding. Every open Minor or Polish finding has an
   owner, affected configuration, workaround or rationale, explicit release
   acceptance, and target release or expiry. Security and privacy boundary failures
   in P-03, P-04, P-06, P-07, P-08, P-10, P-11, P-12, P-16, or P-22 are Blockers by
   default.
4. B-04/B-05/B-11/B-13/B-14/B-15 and I-02–I-08 and I-15 parity and integrity oracles
   are exact; every released session, save, export, and portable path verifies, and
   the cross-platform comparison against the Windows candidate is byte-exact
   everywhere the contract requires it.
5. Zero attributable application crash, unhandled managed exception, native fault,
   hang, OOM kill, or unexplained exit signal inside scenario windows. An external
   kill — `systemd-oomd`, a cgroup limit, a logout `SIGTERM` — is classified from
   host evidence and not counted as a product crash, but its recovery behaviour must
   still pass.
6. The ADB release gate proves physical-device discovery, capture, stop, format and
   time policy, buffer attribution, reconnect and failure, and child cleanup, and it
   proves that the `no permissions` state is reported with its actual Linux cause.
   Results bind to the exact serial and platform-tools hash.
7. Soak completes without sustained resource or latency growth outside baseline,
   data loss beyond explicitly observed source or platform drops, orphan work, or
   descriptor and mapping growth that trends toward a host limit.
8. The Accessibility schedule completes with the primary journey possible by
   keyboard and by Orca, correct modal boundaries, no private debug content spoken,
   and usable contrast, text-scale, and scaling configurations. Any AT-SPI
   limitation is recorded as a known limitation with its effect, not as N/A.
9. Every absolute budget miss and >20% like-for-like median or p95 regression is
   fixed or explicitly accepted with owner, rationale, affected configuration, and
   expiry. A frame measurement with zero or constant frames is Blocked, never Pass,
   and the measurement method is named.
10. A-29 passes from the previous supported release whenever settings, session
    format, cache, default path, versioning, runtime, or packaging changes.
11. Candidate hash, version, notices, checksum, provenance, and the documented
    dependency list all agree; no undeclared host dependency, elevation, installer,
    auto-update, `.desktop` entry, MIME association, systemd unit, autostart entry,
    or network traffic appears.
12. The run header, results, defect links, evidence hash index, sensitive-data
    retention decision, and completed mutation ledger are archived with the release.
    The corresponding manual gates in
    [`RELEASE-CHECKLIST.md`](RELEASE-CHECKLIST.md) reference the run ID — at minimum
    its "Linux packages are validated" line and its requirement that one Unix artifact
    be tested on a clean machine, both of which this run is the evidence for.
13. The §2.10 capability manifest has no unresolved candidate, documentation, or
    claim contradiction, and every unclaimed visible capability has a tested
    contract.
14. Final cleanup in §13.5 passes. A green run that leaves a test user, a loop
    device, a modified `udev` rule, altered permissions, a changed `sysctl`, a
    disabled idle timer, an orphan `adb`, generated secrets, huge corpora, or a
    low-space condition behind is incomplete.

### 13.5 Mandatory cleanup and host hand-back

Run after success, failure, abort, crash, or power recovery. Restore ledger
values — not guessed defaults.

1. Stop and close every `VisualCat` and `vcat` process, import, capture and follow
   producer, ADB traffic generator, tracer, recorder, and pressure tool. Confirm the
   exact PIDs ended and that no VisualCat-owned `adb` child remains
   (`pgrep -a adb`). Do not kill a shared ADB server unless the ledger says this run
   created and owns it. Use `pkill -x`, or the PID — never a `pkill -f` pattern that
   also matches your own command line.
2. Hash and archive the required evidence, then delete only the exact recorded
   temporary, corpus, extraction, session, and export paths according to retention.
   Resolve each path with `readlink -f` and confirm it is inside the intended test
   root before any recursive removal. Report what is recoverable and what is not.
3. Unmount and delete only the dedicated loop device, LVM volume, bind mount, test
   share, or filler file the run created, after resolving its exact target. Verify
   that free space and filesystem health recover.
4. Restore file modes, ACLs, and ownership; `umask`; mount options and quotas;
   `sysctl` values including `vm.max_map_count`, `kernel.core_pattern`, and
   `kernel.perf_event_paranoid`; `ulimit` settings; and
   AppArmor or SELinux mode. Restore firewall and proxy state.
5. Restore the clock and time zone, locale and IME configuration, display scale,
   resolution, orientation, refresh and primary output, desktop theme, contrast,
   color filters, animations, text scale, accessibility services, and — explicitly —
   the idle, blank, lock, and suspend timers that long runs disabled.
6. Restore Android buffer, debugging, authorization, network, and device state,
   restore the original `udev` rules file and reload it, and remove test traffic and
   artifacts as the device's owner requires. Re-run the serial, fingerprint, buffer,
   and free-space checks.
7. Remove test local users only when the exact account, its home directory, creation
   evidence, and owner-approved deletion are recorded. Never alter a real
   organizational policy to tidy up a test.
8. Decide explicitly whether `<data-home>/VisualCat` and the extracted candidate
   remain. Deleting the program directory is not user-data removal; deleting the data
   root destroys sessions, settings, leases, and diagnostics. Do exactly the recorded
   hand-back.
9. Reboot if a restoration mechanism requires it, then re-run the §2.2 identity,
   policy, and free-space checks and prove that the graphical session, ordinary
   applications, USB, audio, network, and power are healthy.
10. Attach the completed ledger and the cleanup evidence to the run record; no row
    remains without *Restored* or an explicit owner-accepted residual state.

---

## Appendix A — Linux cookbook

These examples are templates. Resolve every placeholder and quote every path.
Commands that change host policy, permissions, mounts, the clock, display settings,
`sysctl` values, ADB buffers, or data require a ledger row and scenario
authorization.

```shell
# --- artifact identity ---------------------------------------------------
sha256sum -- '<candidate-tar>' '<cli-tar>'
sha256sum -c --ignore-missing SHA256SUMS
gh attestation verify '<candidate-tar>' --repo benny-cz/VisualCat
tar -tvzf '<candidate-tar>'                       # members, modes, owners, sizes
tar -xzf '<candidate-tar>' -C '<candidate-root>'
find '<candidate-root>' -printf '%M %8s %p\n' | sort -k3
file -- '<VCAT>'; ldd -- '<VCAT>' | grep -i 'not found' || echo resolved

# --- resolved product data paths -----------------------------------------
printf 'data-home=%s\n' "${XDG_DATA_HOME:-$HOME/.local/share}"
ls -la "${XDG_DATA_HOME:-$HOME/.local/share}/VisualCat"
du -sh "${XDG_DATA_HOME:-$HOME/.local/share}/VisualCat"/*
find "${XDG_DATA_HOME:-$HOME/.local/share}/VisualCat" -printf '%M %U:%G %p\n' | sort -k3

# --- launch with argument boundaries -------------------------------------
'<VCAT>' &                                        # bare launch, stderr visible
'<VCAT>' --log '<corpus-root>/small.txt' &
'<VCAT>' --session '<session-root>/<session>' &
'<VCAT>' -- "$(printf 'name-with\nnewline.txt')" &
# capture stderr from a GUI launch for diagnosis
'<VCAT>' > '<evidence-root>/<run-id>/stdout.txt' 2> '<evidence-root>/<run-id>/stderr.txt' &

# --- the graphical environment an SSH shell does not have ----------------
gui=$(pgrep -u "$USER" -x gnome-shell || pgrep -u "$USER" -x nautilus)
tr '\0' '\n' < /proc/"$gui"/environ | grep -E '^(DISPLAY|XAUTHORITY|XDG_RUNTIME_DIR|WAYLAND_DISPLAY|DBUS_SESSION_BUS_ADDRESS)='
# export exactly those values before running any X tool over SSH;
# XAUTHORITY carries a per-session suffix and must never be hard-coded.

# --- driving and observing the window ------------------------------------
xdotool search --name 'VisualCat' getwindowgeometry
xdotool search --name 'VisualCat' windowactivate --sync
xdotool mousemove '<x>' '<y>' click 1
xdotool type --delay 30 'AndroidRuntime'
xdotool key ctrl+f; xdotool key F3; xdotool key 0
xdotool search --name 'VisualCat' windowsize 1280 800
xdotool search --name 'VisualCat' windowclose        # graceful WM_DELETE_WINDOW
xprop -name 'VisualCat' WM_CLASS _NET_WM_NAME _NET_WM_PID
xwininfo -name 'VisualCat' | sed -n '1,12p'

# --- screenshots ----------------------------------------------------------
import -window root '<evidence-root>/<run-id>/<scenario>-<assertion>.png'   # X11
gnome-screenshot -f '<evidence-root>/.../shot.png'                          # portal
# A hypervisor framebuffer grab is immune to what covers the window on the host;
# on VMware Workstation: vmrun -T ws -gu '<user>' -gp '<pw>' captureScreen '<vmx>' out.png

# --- process and resource snapshot ---------------------------------------
pid=$(pgrep -x VisualCat)
ps -o pid,ppid,stat,etime,time,rss,vsz,nlwp,args -p "$pid"
grep -E 'VmRSS|VmHWM|VmSize|VmSwap|Threads' /proc/$pid/status
grep -E '^(Rss|Pss|Private|Swap)' /proc/$pid/smaps_rollup
find /proc/$pid/fd -maxdepth 1 -type l | wc -l
wc -l < /proc/$pid/maps
cat /proc/$pid/io
lsof -p "$pid" | awk '{print $5}' | sort | uniq -c | sort -rn | head

# --- time-bounded host failure evidence ----------------------------------
journalctl --since '<start-local>' --until '<end-local>' -b \
  | grep -iE 'visualcat|oom|segfault|traps|usb|xwayland' || true
journalctl --user --since '<start-local>' -n 200 --no-pager
coredumpctl list --since '<start-local>' || true
dmesg -T --level=err,warn | tail -40
ausearch -m avc -ts recent 2>/dev/null || true        # SELinux denials

# --- file identity, permissions, links -----------------------------------
stat -c '%i %a %U:%G %s %n' -- '<path>'
getfacl -- '<path>'; getfattr -d -m - -- '<path>' 2>/dev/null
findmnt -o TARGET,FSTYPE,OPTIONS --target '<path>'
readlink -f -- '<path>'
cmp -- '<a>' '<b>' && echo identical                  # byte comparison, not diff
tr -dc '\r' < '<file>' | wc -c                        # CR count; never grep $"\r"

# --- storage pressure on a dedicated host only ---------------------------
truncate -s 4G /var/tmp/vcat-test.img
mkfs.ext4 -q /var/tmp/vcat-test.img
mkdir -p /var/tmp/vcat-mnt && sudo mount -o loop /var/tmp/vcat-test.img /var/tmp/vcat-mnt
sudo chown "$USER" /var/tmp/vcat-mnt
fallocate -l 3800M /var/tmp/vcat-mnt/filler          # approach ENOSPC deliberately
sudo mount -o remount,ro /var/tmp/vcat-mnt           # EROFS mid-write
sudo umount /var/tmp/vcat-mnt && rm -rf /var/tmp/vcat-mnt /var/tmp/vcat-test.img

# --- memory and OOM pressure on a dedicated host only --------------------
systemd-run --user --scope -p MemoryMax=1500M -- '<VCAT>' --log '<corpus-root>/large.txt'
dmesg -T | grep -iE 'out of memory|oom-kill|Killed process'

# --- ADB identity: discovery may be unqualified; capture is not ----------
'<adb>' version; sha256sum -- '<adb>'; readlink -f -- '<adb>'
'<adb>' devices -l
'<adb>' -s '<serial>' shell getprop ro.serialno
'<adb>' -s '<serial>' shell date -u
'<adb>' -s '<serial>' logcat -d -v threadtime -s VCATTEST | tail -5
id -nG | tr ' ' '\n' | grep -E 'plugdev|adbusers' || echo 'no USB group'
ls /etc/udev/rules.d/ | grep -i android || echo 'no android udev rule'
lsusb; ls -l /dev/bus/usb/*/* | head

# --- candidate CLI cross-checks; never the only oracle -------------------
S='<corpus-root>/parity-<run-id>.vcat'
'<VCAT-CLI>' index '<corpus-root>/small.txt' --output "$S"
'<VCAT-CLI>' verify "$S"
'<VCAT-CLI>' info "$S"
'<VCAT-CLI>' stats "$S"
'<VCAT-CLI>' query "$S" --levels E --limit 50 | jq -c '.level' | head
'<VCAT-CLI>' export "$S" "$S.csv" --type csv
'<VCAT-CLI>' export "$S" "$S.zip" --type portable-zip
```

### A.1 Tracing discipline

`strace` can slow a GUI process by an order of magnitude and `perf record` changes
scheduling. Never collect a §4.2 timing budget under either. Use them to answer
"which syscalls" or "where does the time go", then re-measure the budget clean.
Record the collection overhead and the lost-event or dropped-sample count with every
trace, keep the raw artifact, and scope every filter to the recorded PID — a global
trace is huge, intrusive, and likely to collect secrets unrelated to VisualCat.
`perf` needs `kernel.perf_event_paranoid` at 1 or lower for per-process call graphs;
record the value you found and restore it.

### A.2 Safe clean-profile preparation

A clean profile is either a new account or a reversible move of one directory.
Prefer the new account: it cannot damage the tester's own data, and it proves the
first-run path including directory creation.

```shell
# preferred: a dedicated user on a disposable host
sudo useradd -m -s /bin/bash vcattest && sudo passwd vcattest

# alternative: reversible move, with the path proved first
root="${XDG_DATA_HOME:-$HOME/.local/share}/VisualCat"
readlink -f -- "$root"                      # confirm it is the intended child
pgrep -x VisualCat || pgrep -x vcat         # must be empty
mv -v -- "$root" "$root.backup-<run-id>"    # reversible; never rm at this stage
# restore: mv -v -- "$root.backup-<run-id>" "$root"
```

Never run a recursive removal against a path built from a variable that might be
unset. Resolve it, print it, confirm it, then act.

---

## Appendix B — Linux traps that impersonate product bugs

Check every finding against these before filing, and record the check in the defect
report.

1. **A tarball packaged on Windows loses every executable bit.** `chmod +x VisualCat
   vcat` after extracting. Real releases are tarred on `ubuntu-latest` per
   `.github/release-targets.json`, so a missing bit from a locally packaged archive is
   a harness artifact. Use a real release asset before filing anything about
   permissions.
2. **Extraction tools differ.** GNOME Files, Ark, `unar`, `bsdtar`, and a restrictive
   `umask` can each drop or alter modes that GNU `tar` preserves. Establish what the
   archive contains with `tar -tvzf` before blaming the extraction, and say which
   tool produced the tree under test.
3. **An all-black framebuffer screenshot usually means the screen blanked, not that
   the app died.** GNOME's default 300 s `idle-delay` locks the session mid-run.
   Disable `org.gnome.desktop.session idle-delay` and
   `org.gnome.desktop.screensaver idle-activation-enabled` for long runs and restore
   them afterwards. `xset -dpms` is rejected under XWayland.
4. **`pkill -f "./VisualCat"` kills its own shell.** The pattern matches the
   `bash -c` command line that contains it, so the command dies silently and returns
   nothing. Use `pkill -x VisualCat`, or the PID.
5. **`$"\r"` in bash is locale-translation syntax, not a carriage return.**
   `grep -q $"\r"` matches any line containing the letter *r*, so every file looks
   like CRLF. This has already faked a CLI-versus-desktop line-ending inconsistency.
   Count bytes: `tr -dc '\r' < f | wc -c`.
6. **An SSH shell has none of the graphical session's environment.** A tool that
   fails because it inherited no `DISPLAY`, or a stale `XAUTHORITY` whose per-session
   suffix was hard-coded from a previous login, has produced no product evidence at
   all. Read the values from a live GUI process.
7. **GTK portal dialogs take clicks reliably but typed text often does not land in
   their name field.** Check where a file actually went before calling it a product
   bug. The portal is a separate process with its own focus rules.
8. **The portal backend decides what the file chooser looks like and what it
   returns.** A GNOME, KDE, or GTK backend, or none at all, changes the dialog, the
   URI form, and whether a document-portal path under `/run/user/<uid>/doc/` is
   involved. Record which backend answered.
9. **XWayland clients do not see the compositor's fractional scale.** A 125% or 150%
   Wayland desktop may hand the client a 1× or 2× surface and scale the result, so the
   app's own layout can be correct while the image is blurry. That is a platform
   property to record, not a layout defect.
10. **X11 has one global scale.** A "mixed-DPI" X11 desktop really applies one scale
    to two physical densities, so UI will legitimately be a different physical size on
    each monitor. Only a Wayland compositor can do better, and not for an XWayland
    client.
11. **The window manager owns minimize, maximize, fullscreen, and keyboard close.**
    `alt+F4` and compositor shortcuts may never reach the application; a tiling WM may
    ignore a minimum size entirely. Use `xdotool windowclose` for the graceful path
    and name the WM in the result.
12. **X11 clipboard ownership dies with the process.** Copying and then quitting
    VisualCat legitimately loses the selection unless a clipboard manager is running.
    Check for a manager before filing a clipboard loss.
13. **PRIMARY and CLIPBOARD are different selections.** Middle-click pastes PRIMARY.
    A "paste produced the wrong text" report must say which selection was used.
14. **Fonts are host state.** `fc-match monospace` resolving differently on two hosts
    produces a legitimately different rendering, different text metrics, and a
    different visual diff. Record the resolved families with every visual baseline;
    DejaVu Sans Mono is in the product's fallback list precisely because it is the
    common Linux answer.
15. **A self-contained publish still needs host libraries.** `libX11`, `libICE`,
    `libSM`, and `fontconfig` come from the distribution. A missing one produces a
    startup failure that looks exactly like a product crash. Run `ldd` before filing.
16. **ICU absence changes globalization, not just formatting.** Without ICU or with
    `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, culture-sensitive behaviour changes
    across the whole runtime. Record the globalization mode before attributing a date
    or number difference to the product.
17. **glibc is a floor, not a preference.** A distribution older than the runtime's
    minimum fails in the dynamic loader with a version message. That is an unsupported
    host, not a product regression — check `ldd --version` first.
18. **USB access needs `udev` rules and group membership.** A device visible to
    `lsusb` and useless to `adb` is the Linux default on a fresh host, and `adb`
    reports it as `no permissions`. Fix the host before filing an ADB discovery
    defect; the product's obligation is to report the state accurately.
19. **USB autosuspend drops transports.** `/sys/bus/usb/devices/*/power/control` set
    to `auto` can suspend a device mid-capture and look exactly like a parser failure.
    Correlate `dmesg -T` USB events and `adb devices -l` before blaming ingest.
20. **The ADB server is shared global state.** An IDE, another user, or a previous run
    may own it. Routine pre-flight must not kill it, and a restart can disrupt
    unrelated work. A VisualCat run that kills a server it did not start is the
    finding; a server restart by someone else is not.
21. **Unqualified `adb` becomes unsafe after topology changes.** One device
    disappearing can make a bare command hit another sole device. Discovery may list
    globally; capture and mutation use `-s <serial>` and re-prove identity.
22. **Unknown-serial `adb logcat` can wait indefinitely.** A timeout-ended empty
    capture can look successful. The product must enumerate and reject the serial
    before spawning; a tester must not use a timeout as the oracle.
23. **On Linux an open descriptor does not block rename or unlink.** Windows-shaped
    expectations do not transfer: a file can be replaced or deleted while VisualCat
    has it open and mapped, and reads continue against the old inode. The oracle is the
    integrity of what was published, not the presence of a sharing error.
24. **Truncation in place and rotation by rename are different events.**
    `truncate -s 0` keeps the inode; `logrotate` without `copytruncate` replaces it.
    The product detects a change only when a read returns nothing and the *path* is
    then missing or shorter than what it has already delivered, so a rotation whose
    replacement is already at least as long escapes detection and leaves the follow
    reading an inode nobody writes to. Say which shape you produced and with what
    timing; A-21 treats these as separate cases for exactly this reason.
25. **Following a growing file is a 250 ms poll, not a kernel notification.** There is
    no `inotify` watch, so `fs.inotify.max_user_watches` and friends cannot explain a
    missed or late append, and raising them will not fix one. Latency is bounded by the
    poll interval; a stalled follow is either the path comparison above or the product.
26. **A Linux GUI launch has a console, unlike the Windows build.** The desktop writes
    an early fatal error to stderr and rethrows, so the terminal that launched it is the
    first place to look — redirect both streams before a launch you expect to fail.
    Conversely, the absence of a `journalctl` entry proves nothing: there is no systemd
    unit and no `.desktop` entry, so nothing routes the app's output to the journal.
27. **A launcher's environment is not a shell's.** A process started from a file manager
    or a `.desktop` entry inherits the systemd user environment, so `PATH`, `LANG`,
    `LC_*`, `TMPDIR`, and anything else set in `.bashrc` or `.profile` can simply be
    absent. "Works in my terminal" is therefore not evidence about a user's launcher, and
    an `adb` found through a shell profile can be invisible to the app. Read
    `/proc/<pid>/environ` for the process you actually tested.
28. **Memory-mapped session segments inflate RSS.** The kernel counts mapped file
    pages it can reclaim at any time. Use `smaps_rollup` Pss and private-memory
    figures, and corroborate a leak claim with descriptors, mappings, and threads, not
    with one rising number in a system monitor.
29. **An OOM kill is not a crash.** `systemd-oomd`, a cgroup `MemoryMax`, or the
    kernel killer terminates with `SIGKILL` and leaves no managed stack. Check
    `dmesg` and `journalctl` before filing a crash; the product's obligation is
    recoverability.
30. **A logout sends `SIGTERM`.** Work ending when the session ends is expected;
    ending *without* a truthful partial or a recoverable session is the finding.
31. **`apport` and `coredumpctl` are host policy.** Ubuntu can prompt to upload a
    crash report, and `systemd-coredump` stores dumps that contain log payload by
    construction. Neither is product telemetry; both are privacy-relevant and must be
    recorded and cleaned up.
32. **`perf_event_paranoid` can block profiling entirely.** A profiler returning
    nothing is an instrumentation Block, not a smooth application. Assert a non-zero
    sample or frame count before reading any percentage.
33. **Software rendering is a different performance class.** llvmpipe, a VM's virtual
    adapter, and hardware GL produce different absolute numbers for identical code.
    Keep G5 results as their own baseline.
34. **A screen recorder changes the workload.** A compositor recorder consumes GPU
    encode capacity, can cap the frame rate, and may not capture override-redirect
    windows at all. Record the clip's actual properties; use an external camera when it
    cannot resolve the budget.
35. **Linux paths are bytes, not text.** A name can be legal and not valid UTF-8, and
    .NET will represent it with surrogate escapes. Round-tripping such a name is a real
    requirement; a terminal that renders it as `?` is the terminal's doing.
36. **Names may contain almost anything.** Spaces, newlines, quotes, `$`, `*`, and
    `:` are all legal. A harness that word-splits or glob-expands such a name has
    produced a harness defect, not a product finding. Quote, and use `--`.
37. **Case sensitivity is filesystem policy, not platform policy.** ext4 is
    case-sensitive; an exFAT, vfat, or `casefold`-enabled directory is not. The product
    compares session paths case-sensitively on Linux, so a case-insensitive mount is a
    deliberately different test, not a contradiction.
38. **`noexec` and confinement produce failures that look like corruption.** A
    `noexec` mount refuses to run the binary; AppArmor or SELinux can deny one path
    while allowing its sibling. Check `aa-status`, `getenforce`, `ausearch -m avc`, and
    `journalctl -k` before attributing the failure to the product.
39. **A `.desktop` file is not shipped.** There is no launcher icon, no pinning, no
    MIME association, and no "open with" entry, by design per
    [`SUPPORT.md`](SUPPORT.md). Their absence is not a defect; a generic placeholder
    icon in a dock is the expected consequence.
40. **Deleting the extracted directory is not uninstalling.** The XDG data root
    survives, and deleting it destroys sessions, settings, and diagnostics. They are
    two separate actions with two separate consequences.
41. **A release archive and a local publish can share a version but not bytes.** Only
    the recorded exact asset hash signs off a release. A successful source rerun is
    diagnostic evidence.
42. **Two live captures are not directly comparable by total.** Start-up, pre-roll,
    and scheduling windows differ. Exact parity needs the same finite bytes or a
    unique marker-bounded interval.
43. **Logcat declares its own drops.** `chatty`, ring-buffer overwrite, and source
    gaps are Android and ADB observations. VisualCat must account for them; it cannot
    recover bytes the source never delivered.
44. **Format modifiers degrade by device capability.** A device rejecting
    `threadtime,year,UTC,usec` can legitimately fall back. The product bug would be a
    wrong manifest or time policy, or unbounded negotiation — not the fallback itself.
45. **Automatic detection may refuse a corpus made mostly of defects.** Detection
    scores the whole file, not its size: a two-line seed file can be detected at full
    confidence while a nine-line file of deliberate defects is refused outright. Import
    that one with an explicit format. The refusal is the confidence threshold working;
    the findings are the opposite cases — a file of defects accepted confidently, or a
    refusal whose message does not say that choosing a format is the way forward.
46. **The import-and-refresh race is not reproducible headlessly.**
    `Avalonia.Headless` never reproduced it at 250k lines even with a window shown: it
    needs a real compositor and a real import to overlap. The reliable check is an A/B
    of the shipped binary — open a one-million-line log with `--log` five times per
    build and read the summary line and the zoom readout off screenshots. Re-run that,
    not the unit tests, when the snapshot-refresh path is touched.
47. **The diagnostics bundle is the best available ingest oracle.**
    `<data-home>/VisualCat/Diagnostics/visualcat-*.jsonl` records `ingest.detected`,
    `store.snapshot` with `snapshotGeneration` and `timedEntries`, and `ingest.ready`.
    It is what turns "the numbers look wrong" into "the view stopped at snapshot
    generation 2 of 5". Enable it before a sequencing investigation rather than
    guessing afterwards.
48. **A hypervisor framebuffer grab and a compositor screenshot see different
    things.** The framebuffer grab is immune to what covers the window on the host and
    works when the guest session is otherwise unreachable; a compositor screenshot can
    miss override-redirect surfaces. Say which produced each image.
49. **VM clocks jump on snapshot restore.** A restored VM can resume with a wall clock
    minutes or days off, which distorts file names, retention, and live-edge
    assertions. Re-run §2.2 after every restore.
50. **A PowerShell host's own formatting is not a product culture leak.** Reading a
    product JSON file through `ConvertFrom-Json | Format-List` on a comma-decimal
    host prints doubles with a comma. That is the shell, not the product — check the
    raw bytes.

---

## Appendix C — coverage map

Every functional area and its primary scenarios. A change reruns at least its row
plus the change-based selection in §12.1.

| Area | Scenarios |
|---|---|
| Artifact, provenance, extraction, mode bits | B-01, B-02, I-01, I-12, P-04, P-13, P-18, R-14 |
| Host dependencies, glibc, ICU, X11 libraries | B-02, G7, A-37, U-27, P-12, R-14 |
| Launch, arguments, working directory, launcher environment | B-01, B-03, B-16, A-15, A-37, I-10, P-11, P-12, R-38 |
| XDG paths, data locality, permissions | A-14, A-36, P-02, P-08, P-09, P-17, P-18, P-22, R-67 |
| Window lifecycle and state | B-18, A-30–A-32, X-07, X-12, X-23, X-28, U-01–U-05, U-19, U-26 |
| Display server, scaling, multi-monitor | G0–G7, A-30, X-23, U-01–U-05, U-09–U-12, U-23, U-24 |
| Window manager and desktop integration | G4, B-18, U-05, U-19, U-26, P-18 |
| Finite import, preview, formats | B-04, B-20, A-06–A-09, A-25, A-26, X-01, X-02, X-24, I-02, R-20, R-21, R-51, R-53, R-54 |
| Off-timeline and unparsed evidence | B-20, A-08, U-07, U-21, U-24, I-02, I-07, R-04, R-05, R-07, R-27, R-39, R-53, R-54 |
| Portal file chooser and cancellation | B-04, A-25, U-19, P-05 |
| Host ADB discovery and capture | B-10, B-11, A-15–A-20, X-05, X-08–X-10, I-05, I-06, I-09, P-19, R-08, R-32–R-37, R-52 |
| Growing-file follow | B-12, A-21–A-24, X-06, X-10, X-11, X-24, X-27, X-28, R-10, R-32–R-34 |
| Ingest, snapshots, finalization, recovery | B-11–B-13, B-17, A-09–A-11, X-01, X-03, X-10–X-17, X-28, R-01–R-03, R-08–R-13, R-32, R-49 |
| Heat map, minimap, axis | B-05, B-08, A-04, X-01, X-04, X-18, X-23, U-01–U-03, U-09–U-15, U-23, R-19, R-22, R-24, R-25, R-29, R-43, R-44 |
| Search, regex, markers | B-07, A-03, A-05, A-08, X-04, X-19, U-06, U-16, I-02, I-07, R-51, R-57, R-62 |
| Filters, facets, statistics, templates | B-06, B-09, A-01–A-04, X-03, X-04, X-19, I-07, R-12, R-26–R-28, R-63 |
| Paging and bulk load | B-09, B-20, A-05, X-04, X-17, X-19, U-07, U-24, R-11, R-26, R-58 |
| Entry inspector, source context, clipboard | B-05, B-09, B-20, A-08–A-10, A-24, X-04, X-18, X-19, U-07, U-14–U-18, P-05, P-14, R-23, R-30, R-31, R-39, R-46, R-47 |
| Sessions, recent, cache, retention, leases | B-13, B-17, A-03, A-05, A-10–A-14, A-29, A-33, X-12, X-13, X-20, X-22, X-26, X-28, U-06, P-16, P-16.1, R-15, R-18, R-48, R-50, R-66, R-67 |
| Save, portable archive, round trip | B-13, B-14, A-08, A-10, A-25, A-29, A-33, X-12–X-15, X-24–X-26, I-03, I-04, I-08, P-04, P-08, P-10 |
| CSV export | B-15, A-04, A-25, A-27, X-12, X-15, X-25, I-07, I-15, P-05, P-10, P-20, R-59, R-64 |
| Diagnostics and bundle | A-13, A-27, X-12, X-22, X-25, P-02, P-03, P-10, P-15, P-20, R-13, R-17 |
| Notice lane and status messaging | B-11, B-12, B-20, A-22, A-24, U-20, U-21, U-25, P-20, R-05, R-17, R-33, R-45 |
| Settings and upgrade | A-03, A-12–A-15, A-28–A-30, A-32, A-34, A-36, X-22, X-27, I-11, R-14, R-16, R-43, R-44, R-48, R-50, R-55, R-60 |
| Keyboard, focus, modality | B-19, B-20, U-06–U-08, U-16, U-19, U-25, R-19, R-40–R-42, R-54, R-56, R-57 |
| Orca and accessibility | B-20, U-06–U-12, U-16–U-25, P-14, P-20, R-40–R-42, R-44, R-53, R-54 |
| Theme, contrast, text scale, locale, fonts | A-13, A-37, X-23, X-27, U-09–U-12, U-18, U-23, U-24, U-27, I-11, R-28, R-43, R-44, R-55 |
| IME, layouts, clipboard selections | U-14, U-16, U-17, P-14 |
| Performance, scale, endurance | §4.2, §4.3.1, X-01–X-11, X-17–X-23, X-25, X-29, R-09–R-12 |
| Filesystem, permissions, mounts, storage | L3, L4, L7, L9, A-09, A-14, A-25, A-26, A-36, X-12–X-16, X-24, X-25, X-29, P-02, P-07–P-10, P-16, P-22, R-09, R-49, R-66, R-67 |
| Signals, process lifecycle, OOM | B-18, A-11, A-31, X-12, X-16, X-28, I-13, P-11, P-15 |
| Multi-instance and concurrency | L8, A-05, A-23, A-32, A-33, X-11, X-13, X-22, P-10, P-16, P-19, R-13, R-48, R-49 |
| Privacy, network, redaction | A-27, A-28, P-01–P-03, P-11, P-14, P-15, P-17–P-20, R-60 |
| Untrusted content: GUI, terminal, archive, session | A-08, A-26, X-24, X-26, P-04–P-08, P-12, P-20 |
| Confinement and hardening | L9, P-09, P-12, P-21 |
| CLI contract, pipes, signals, generator | I-01, I-06, I-07, I-10, I-11, I-13–I-15, B-13–B-15, A-06, A-17, A-20, X-26, R-06, R-51, R-59, R-61 |
| Cross-platform parity (Windows, Android) | I-02–I-08, I-11, I-14, I-15, B-14, A-26, A-37 |

---

## Related documents

- [`WINDOWS-LIVE-TEST-PLAN.md`](WINDOWS-LIVE-TEST-PLAN.md) — the primary desktop
  plan, the cross-platform parity partner, and the source of the tier discipline
  used here.
- [`ANDROID-LIVE-TEST-PLAN.md`](ANDROID-LIVE-TEST-PLAN.md) — companion-device live
  test plan and the origin of this tier structure.
- [`ARCHITECTURE.md`](../ARCHITECTURE.md) — layer ownership and invariants.
- [`SUPPORT.md`](SUPPORT.md) — current platform, source, and distribution limits,
  including the Linux X11/XWayland requirement and the tarball-only statement.
- [`CLI.md`](CLI.md) — exact CLI commands, options, output, and exit codes.
- [`KEYBOARD.md`](KEYBOARD.md) — keyboard and accessibility contract.
- [`PERFORMANCE.md`](PERFORMANCE.md) — the reproducible controlled baseline and the
  reference-machine class its numbers belong to.
- [`SESSION-FORMAT.md`](SESSION-FORMAT.md) — manifest, segments, raw ownership,
  recovery, and the portable archive contract.
- [`PRIVACY.md`](PRIVACY.md) and [`SECURITY.md`](SECURITY.md) — data-flow and
  untrusted-input boundaries.
- [`RELEASE-NOTES.md`](RELEASE-NOTES.md) — checksum, provenance, and portable launch
  instructions.
- [`RELEASE-CHECKLIST.md`](RELEASE-CHECKLIST.md) — the release gates this run
  satisfies.
- [`CHANGELOG.md`](../CHANGELOG.md) — source for Tier R and version-specific
  user-visible behaviour.
- [`adr/0008-time-policy.md`](adr/0008-time-policy.md),
  [`adr/0009-continuations.md`](adr/0009-continuations.md),
  [`adr/0015-packaging.md`](adr/0015-packaging.md) — the decisions several
  assertions in §3 and §5 rest on.
