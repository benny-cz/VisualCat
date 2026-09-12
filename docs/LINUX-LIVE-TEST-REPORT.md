# VisualCat — Linux live test report (Ubuntu 22.04 LTS, GNOME on Wayland/XWayland)

Live execution of [`LINUX-LIVE-TEST-PLAN.md`](LINUX-LIVE-TEST-PLAN.md) against a real
Ubuntu desktop, driving the **shipped `linux-x64` release tarball** through a real
X11/XWayland session with a real window manager, a real `xdg-desktop-portal-gnome`
file chooser, real POSIX permissions, and a **physical Android phone** over a real
ADB transport.

This report is **context-agnostic**: every path, hash, setting, and command it depends
on is recorded here, so a reader who has never seen the run can reproduce or continue
it without a single fact that exists only in a previous session.

The run is documented **continuously**. §0 is the restore point and is rewritten after
every scenario, so an interrupted run resumes from the last line there. Findings are
appended to §3 the moment they are observed.

**Part II (§20) is the remediation.** It implements the suggested fix for every finding and
live-verifies each one against the same guest and the same phone. Read
[§20.1](#201-restore-point) for where it got to, [§20.3](#203-progress-ledger) for the
per-finding state, [§20.4](#204-live-verification-on-the-guest) for the measurements, and
[§21](#21-sixth-pass--closing-what-208-left-and-four-rows-from-15) for the sixth pass, which
closed what §20.8 left open and four standing rows from §15.

---

## 0. Restore point — resume here

| Field | Value |
|---|---|
| Run ID | `20260911-linux-ubuntu2204` |
| Status | **COMPLETE** — five passes; cleanup done and the host handed back after each |
| Last completed | §17 fifth pass: A-21 completed, including both holes the plan predicted |
| Next step | None on this host. §15 is the open list, minus A-21 which §17 closed. The highest-value remaining item is a full B-tier run on a second distribution |
| Findings | **31** — 8 Major, 13 Minor, 10 Polish (§3, §8, §11, §14, §18) |

**Read it in this order.** §1–§6 are the first pass (artifact, B tier, ADB, save/export, follow,
accessibility, rendering). §7–§8 are the second pass, which closes the Blocker-class **Tier P**
security rows, verifies the release provenance, and settles whether [F-10](#f-10) is XWayland-specific
(it is). §10–§11 are the third pass: the glibc floor, globalization and time-zone reproducibility, the
session-corruption matrix, multi-user isolation, and `umask 077`. §13–§14 are the fourth
(cross-platform parity, the upgrade gate, scaling) and §17–§18 the fifth (A-21, including both holes
the plan predicted). Findings are numbered
continuously: F-01…F-17 in §3, F-18…F-25 in §8, F-26…F-28 in §11, F-29…F-30 in §14, F-31 in §18.
**§15 is the standing list of what is still untested**, as amended by §17.

**To resume.**

```shell
# 1. find the guest
"C:\Program Files\VMware\VMware Workstation\vmrun.exe" -T ws getGuestIPAddress \
  "V:\Virtual Machines\Barebit\Ubuntu 64-bit\Ubuntu 64-bit.vmx"
# 2. shell in — the host's public key is already in the guest's authorized_keys,
#    so this needs no password. vmrun guest operations need one; ask the VM's owner.
ssh benny@<ip>
# 3. load every path token and the graphical-session environment this report uses
. ~/vcat-run/env.sh
```

`env.sh` re-reads `XAUTHORITY` from the live session each time, so it survives a guest
logout. Nothing below depends on a live process or a shell variable set in a previous
session.

Screenshots for the assertions below are on the **Windows host** at
`artifacts/live-test/20260911-linux-ubuntu2204/` (git-ignored, like the Windows run's
evidence); per-scenario evidence stays in the guest at
`~/vcat-run/evidence/20260911-linux-ubuntu2204/`. Every number and every quoted string in
this report is reproduced verbatim in the text, so the report stands on its own without
them.

To reach the phone from the guest again, the host's ADB server must first be put back on
all interfaces — §4 restored it to loopback-only at hand-back, so this is setup, not a
standing state:

```powershell
# on the Windows host
adb kill-server; Start-Process adb -ArgumentList "-a","-P","5037","nodaemon","server" -WindowStyle Hidden
New-NetFirewallRule -DisplayName VCAT-ADB-5037 -Direction Inbound -Protocol TCP `
  -LocalPort 5037 -RemoteAddress 172.24.176.0/20 -Action Allow
```
```shell
# in the guest — and in the environment of any VisualCat you launch, so its adb child inherits it
export ADB_SERVER_SOCKET=tcp:172.24.176.1:5037     # see §1.5 for what this does and does not cover
adb devices -l
```

---

## 1. Run header

### 1.1 What was executed, and why this subset

The plan is a catalogue, not a page-order demand (§12). This run executed the
**Artifact smoke** gate first (the plan's step 4 trust boundary), then the **Standard**
schedule's B rows, then the rows whose whole point is that they are Linux-only —
portal chooser, XDG paths, non-UTF-8 byte paths, `G7` hostile sessions, POSIX modes,
`--` argument safety — and the ADB slice against a physical phone.

| Plan row | Reported in | Verdict |
|---|---|---|
| B-01 Verify, extract, cold-launch the exact tarball | §2.1 | **PASS** |
| B-02 Runtime dependency and self-containment probe | §2.2 | **PASS** (positive path) |
| G7 Headless / hostile graphical session | §2.2 | **FAIL** → [F-02](#f-02) |
| B-03 Desktop empty state and command inventory | §2.3 | **PASS** with [F-03](#f-03) |
| A-28 / R-60 Update route and release-origin communication | §2.3 | **FAIL** → [F-03](#f-03) |
| B-04 Import a small file through the portal file chooser | §2.4 | **PASS** with [F-04](#f-04), [F-05](#f-05) |
| B-05 Heat map to exact source bytes | §2.5 | **PASS** |
| B-06 Severity filters and clear semantics | §2.6 | **PASS** |
| B-07 Text and regex search | §2.7 | **PASS** (R-51, R-57, R-62) |
| A-08 Adversarial corpus sweep (CLI leg) | §2.8 | **FAIL** → [F-01](#f-01), [F-06](#f-06) |
| A-26 Names, normalization, case collisions (byte-path leg) | §2.8 | **FAIL** → [F-06](#f-06) |
| I-01 Matching Linux CLI artifact identity | §2.9 | **PASS** |
| I-13 Shell, pipes, exit codes, signals | §2.9 | **PASS** with [F-07](#f-07) |
| I-14 / R-61 Generator and format matrix | §2.9 | **FAIL** → [F-01](#f-01) |
| R-06 `vcat query` emits NDJSON | §2.9 | **FAIL on 2.0.13 · PASS on HEAD** |
| B-20 Off-timeline and unparsed evidence | §2.10 | **PASS on HEAD** with [F-15](#f-15); R-04/R-05 **FAIL on 2.0.13** |
| X-01 / R-01–R-03 Large import A/B (5 launches per build) | §2.11 | **2.0.13 FAIL 3/5 · HEAD PASS 5/5** |
| B-10 Host ADB discovery and capture | §2.12 | **PASS** |
| B-11 Stop is answered, sticky, complete | §2.12 | **PASS** |
| I-05 Desktop ↔ CLI ADB discovery parity | §2.12 | **PASS** |
| R-37 Per-record buffer attribution | §2.12 | **PASS** |
| P-11 / P-19 Process creation and ADB authority | §2.12, §2.16 | **PASS** |
| B-13 Save standard session | §2.13 | **PASS** |
| B-15 CSV export scopes, order, encoding | §2.13 | **PASS** with [F-16](#f-16) |
| I-07 Desktop ↔ CLI export equivalence | §2.13 | **PASS** — byte-identical |
| R-59 Export can ignore the active filter | §2.13 | **PASS** |
| B-12 Follow a growing file | §2.14 | **FAIL** → [F-09](#f-09) |
| R-33 A quiet status stops claiming arrivals | §2.14 | **PASS** on wording, undermined by [F-09](#f-09) |
| A-13 / R-16 Appearance and timeline settings | §2.15 | **PASS** with [F-14](#f-14) |
| A-12 Session cache view | §2.15 | **PASS** (partial) |
| U-08 / R-41 AT-SPI tree and modal boundary | §2.15 | **FAIL** → [F-11](#f-11) |
| U-19 Dialog dismissal by Escape | §2.15 | **FAIL** → [F-12](#f-12) |
| U-23-class visual check (all windows, all runs) | §2.15 | **FAIL** → [F-10](#f-10) |
| B-16 Startup paths and argument dispatch | §2.16 | **PASS** (R-38, R-20) |
| P-01 No unsolicited network traffic | §2.16 | **PASS** — zero sockets |
| P-02 Data locality and declared XDG storage | §2.16 | **PASS** with an observation |
| P-22 File modes and ownership | §2.16 | **PASS** (`umask 002` leg) |
| P-18 Removal and residue accounting | §2.16 | **PASS** with [F-17](#f-17) |
| G7 Headless / hostile graphical session | §2.2 | **FAIL** → [F-02](#f-02) |

### 1.1.1 Findings at a glance

| ID | Severity | One line |
|---|---|---|
| [F-01](#f-01) | **Major** | `logcat -v long` silently loses every record whose TID is five digits — 472 of 4,000 on a real phone, 3,299 of 5,000 synthetic — booked as continuations, counted nowhere, and the file then fails auto-detection |
| [F-09](#f-09) | **Major** | *Follow file* withholds the newest records indefinitely — 13 of 120 still missing 75 s after the writer stopped — while the status says the source has gone quiet; a single further append releases them all |
| [F-10](#f-10) | **Major** | Windows and dialogs render with unpainted and duplicated regions on XWayland — 4 of 5 *Appearance & timeline* openings, and it hid the export review's own *Row order* and *Encoding* controls |
| [F-11](#f-11) | **Major** | A modal dialog is not modal to assistive technology: exposed as a sibling `frame` with no `MODAL` state, and all 434 nodes of the workspace behind it stay reachable |
| [F-02](#f-02) | Minor | No `DISPLAY`, a refusing `DISPLAY`, or a missing `libX11` ends in a bare .NET stack trace printed **twice** and `SIGABRT`, with nothing naming the missing package |
| [F-03](#f-03) | Minor | The desktop has **no update route at all** — *Check for updates…* is Android-only, so A-28/R-60 cannot pass and `SUPPORT.md` describes a command the desktop does not have |
| [F-04](#f-04) | Minor | The file-operation card says **“Copying file…”**, with an animating progress bar, for the entire time the import review is waiting on the user — indefinitely |
| [F-06](#f-06) | Minor | A legal non-UTF-8 file name (`latin1-name-\xE9.bin`) is reported as **“Log source was not found.”** |
| [F-07](#f-07) | Minor | `vcat` rejects the POSIX `--` end-of-options separator, so a file name beginning with `-` has no safe spelling |
| [F-12](#f-12) | Minor | Escape does not dismiss *Appearance & timeline*; only the window manager's close affordance does |
| [F-15](#f-15) | Minor | *Lines not on the timeline* never says how many of the counted lines it has listed, and *Load 500 more* loaded **26**, then **0** |
| [F-05](#f-05) | Polish | On the desktop *Open log* and *More → Open log with options…* are the same command: both force the review, so the second one promises nothing the first does not already do |
| [F-08](#f-08) | Polish | `Import` is styled as a disabled button while it is live and clickable |
| [F-13](#f-13) | Polish | The product registers on the accessibility bus as **“Avalonia Application”**, and some accessible names are control type names (`TextBox`, `GridSplitter`, `TextBlock`) |
| [F-14](#f-14) | Polish | Phone-only settings and phone vocabulary (“the device's own text size”, *Phone plot and details split*) appear in the desktop settings dialog |
| [F-16](#f-16) | Polish | The export review states its range in **UTC** while the workspace it was opened from is showing **Europe/Prague** |
| [F-17](#f-17) | Polish | 14 stale `/tmp/dotnet-diagnostic-*-socket` files from exited processes |

The second pass adds eight more — [F-18](#f-18) to [F-25](#f-25), three of them Major. They are
listed in [§7.1](#71-findings-added-by-this-pass) and written up in [§8](#8-findings-from-the-second-pass).

### 1.2 Candidate under test

Two artifacts were exercised. The **release tarball is the release authority**; the
HEAD build exists only to answer "is this already fixed?" for the rows the changelog
claims under `[Unreleased]`.

| Field | Value |
|---|---|
| Desktop asset | `VisualCat-Desktop-linux-x64-v2.0.13.tar.gz`, downloaded from the GitHub release for tag `v2.0.13` |
| Desktop tar.gz SHA-256 | `32536bd04667209bfc3f18ae610bd3098acefe281c9d06fc018cb7b302e0a69e` (45,674,616 B, 240 members) |
| CLI asset | `VisualCat-CLI-linux-x64-v2.0.13.tar.gz` |
| CLI tar.gz SHA-256 | `04508d7045f7245810e8b585e8cdec670774b2c3ab20427ea43af8c1eb1ccfe7` (36,048,489 B, 209 members) |
| `SHA256SUMS` SHA-256 | `109adf198b1ce57fe45640e3aa828be786453cb953da53d33f2fd736a154a057` — both lines verified `OK` |
| `<VCAT>` | `~/vcat-run/candidate/rel-2.0.13/desktop/VisualCat` |
| `VisualCat` SHA-256 | `0e348b96b9efc23cedf5849343dbd69c443571feda137b9996178579d6e3c066` (78,256 B, mode `755`) |
| `<VCAT-CLI>` | `~/vcat-run/candidate/rel-2.0.13/cli/vcat` |
| `vcat` SHA-256 | `914961b70c6c36d132bcaf4244ba23bae3eb8c8b02b8c349b2f753ae78845650` (78,256 B, mode `755`) |
| Version as displayed | `VisualCat 2.0.13+0670981 · local-first · no telemetry` |
| Version from `vcat --version` | `vcat 2.0.13+06709815674a2ea19dfdb08cd1c552836378c27b` |
| Release tag / commit | `v2.0.13` / `0670981` ("Stabilize asynchronous CI baselines"), published 2026-09-09T18:22Z |
| Comparison build ("HEAD") | `2.0.13+692ad6b`, repository commit `692ad6b`, two commits past the tag, `[Unreleased]` non-empty. Packaged with `pwsh tools/package.ps1 -Runtime linux-x64 -Archive` **on Windows**, so its tarball carries no executable bit — a harness artifact (plan Appendix B #1), corrected with `chmod +x` and never reported as a defect |
| HEAD desktop / CLI SHA-256 | `377cf365e40f29578dbbcd29759ebd94f15ee52366210538483b2609b6812884` / `2fc977f91b9f6b8db7cd3747f6c1005c8b8004720a724d4175973bb67195736a` |

**Provenance — closed in the second pass.** `gh` is not installed in the guest, so the first
pass established only the checksum chain and left **P-13** and B-01's attestation half
unclaimed. §7.2 closes them: run from the Windows host against the same downloaded release
bytes, both tarballs verify, and the attestation names workflow
`release.yml@refs/tags/v2.0.13`, `runnerEnvironment: github-hosted`, and source commit
`06709815674a2ea19dfdb08cd1c552836378c27b` — exactly the commit the binary reports.
**P-13 PASS.**

### 1.3 Linux host

| Field | Value |
|---|---|
| Distribution | Ubuntu 22.04.5 LTS (Jammy), kernel `6.8.0-138-generic`, x86-64 |
| glibc | host 2.35 (`ldd (Ubuntu GLIBC 2.35-0ubuntu3.15)`). The **floor is established at 2.28** in §10.2 — the release runs on Debian 10 |
| Physical / VM | **VMware Workstation 17 guest** (`systemd-detect-virt` → `vmware`) on a Windows 11 host |
| CPU / RAM | 8 vCPU of an AMD Ryzen 9 5900X · 7.7 GiB RAM · 2 GiB swap |
| GPU / renderer | VMware virtual adapter. **Confirmed G5 (software rendering)** — §7.18: the app logs `Renderer 'SVGA3D; build: RELEASE;  LLVM;' is blacklisted by 'SVGA3D'` and falls back from GLX on **every** launch (27 of 27). Treat every timing here as a G5-class result, never as a metal baseline |
| Display server | **Wayland session, XWayland client** (`XDG_SESSION_TYPE=wayland`, `Xwayland :0 -rootless`) — plan G-state **G1** |
| Desktop / WM | GNOME Shell 42.9 / Mutter 42.9 (`XDG_CURRENT_DESKTOP=ubuntu:GNOME`) |
| Display topology | single output `XWAYLAND0`, primary, scale 1, 96 dpi. Started 1280×800, **auto-fit resized it to 1737×1279 mid-run** when the VMware window was focused; both geometries are named per screenshot |
| Portal | `xdg-desktop-portal 1.14.4` + `xdg-desktop-portal-gnome 42.1` + `-gtk 1.14.0`; `xdg-document-portal` running, `/run/user/1000/doc` mounted `fuse.portal` |
| Theme / contrast / animation | `color-scheme 'default'`, `gtk-theme 'Adwaita'`, `text-scaling-factor 1.0`, `enable-animations true` |
| Idle / blank / lock | found `idle-delay 300 s`, `idle-activation-enabled true`; **set to `0` / `false` for this run and ledgered for restoration** (§1.6) |
| Fonts | `fc-match monospace` → **DejaVu Sans Mono Book**; `fc-match sans-serif` → **DejaVu Sans Book**; 352 fonts installed |
| Locale / time | `LANG=en_US.UTF-8` with `LC_NUMERIC=LC_TIME=cs_CZ.UTF-8` (a genuinely **split locale**, which is the A-37 / R-28 case for free); `Europe/Prague` (CEST, UTC+2); `tzdata 2026c`; NTP synchronized |
| Filesystem | `/dev/sda3` **ext4**, `rw,relatime,errors=remount-ro`, 163 GiB free |
| `umask` / limits | `0002` · `ulimit -n 1024` · `vm.max_map_count 65530` |
| Confinement | **AppArmor enabled**; SELinux absent; `kernel.yama.ptrace_scope` blocks `strace` against an already-running non-child process |
| Crash policy | `core_pattern` = `|/usr/share/apport/apport …`; `apport` produced **no** report for the aborts in §2.2 (the binary is not distribution-packaged), `/var/crash` stayed empty |
| Accessibility | Orca 42.0, `at-spi2-core 2.44.0` installed |
| System .NET | **none** — `command -v dotnet` empty, as the self-contained claim requires |

### 1.4 Product data, corpora, evidence

| Token | Resolved value |
|---|---|
| `<data-home>` | `/home/benny/.local/share` (`XDG_DATA_HOME` unset) |
| Data root | `/home/benny/.local/share/VisualCat` → `Sessions/`, `Diagnostics/`, `SessionAccess-v1/`, `settings.json` |
| `<candidate-root>` | `~/vcat-run/candidate/rel-2.0.13/{desktop,cli}` |
| `<corpus-root>` | `~/vcat-run/corpus` (manifest `SHA256SUMS.corpus`) |
| `<evidence-root>` | `~/vcat-run/evidence/20260911-linux-ubuntu2204` |
| Ledger | `~/vcat-run/ledger/mutation-ledger.tsv` |
| Starting data profile | **D1 clean** — the account's real `VisualCat` directory (4 sessions) was moved to `VisualCat.backup-20260911-linux-ubuntu2204` before the first launch and is ledgered for restoration |
| Execution state | **L0/L1** — verified portable run, clean home, ordinary unprivileged user |

### 1.5 Android device under test

| Field | Value |
|---|---|
| Model / serial | Samsung **SM-G990B** (Galaxy S21 FE 5G) / `RFCRC0A9GND` |
| Android / API | 16 / 36, product `r9qxeea`, device `r9q` |
| Device time zone / clock | `Europe/Prague`; device UTC agreed with guest UTC to the second |
| Transport | USB to the **Windows host**; the guest reaches it through the host's ADB server |
| `adb` in the guest | `/usr/bin/adb`, Debian package `28.0.2-debian` (Android Debug Bridge 1.0.41) |
| `adb` on the host | `35.0.1-11580240` |

**How the phone was made visible to the VM.** The guest is on the Hyper-V Default
Switch subnet and has no USB pass-through. Instead the host's ADB server was restarted
listening on every interface and the guest was pointed at it:

```shell
# on the Windows host (ledgered — it restarts a server other tools may have been using)
adb kill-server
adb -a -P 5037 nodaemon server          # background
New-NetFirewallRule -DisplayName VCAT-ADB-5037 -Direction Inbound -Protocol TCP `
  -LocalPort 5037 -RemoteAddress 172.24.176.0/20 -Action Allow

# in the guest
export ADB_SERVER_SOCKET=tcp:172.24.176.1:5037
adb devices -l      # RFCRC0A9GND  device  product:r9qxeea model:SM_G990B
```

This is a **recorded deviation** from plan §2.1, which asks for a locally attached
device: the guest's `adb` is a *client* of a remote server, so `udev` rules, group
membership, and the `no permissions` state (plan A-16, B-10) **cannot** be exercised
from here and are not claimed. Transport identity, buffers, format negotiation, and
capture content are unaffected and are claimed.

### 1.6 Mutation ledger (open rows)

| UTC | Scenario | Target | Original | New | Restoration | Restored |
|---|---|---|---|---|---|---|
| 2026-09-11T18:03:23Z | setup | `org.gnome.desktop.session idle-delay` | `uint32 300` | `uint32 0` | `gsettings set org.gnome.desktop.session idle-delay 300` | pending |
| 2026-09-11T18:03:23Z | setup | `org.gnome.desktop.screensaver idle-activation-enabled` | `true` | `false` | `gsettings set … idle-activation-enabled true` | pending |
| 2026-09-11T18:03:23Z | setup-D1 | `~/.local/share/VisualCat` | 4 real sessions | moved aside | `mv ~/.local/share/VisualCat.backup-20260911-linux-ubuntu2204 ~/.local/share/VisualCat` | pending |
| 2026-09-11T21:34Z | ADB | Windows host ADB server | default loopback server | `-a` all-interfaces server + firewall rule | `adb kill-server; adb start-server; Remove-NetFirewallRule -DisplayName VCAT-ADB-5037` | pending |

---

## 2. Scenario results

### 2.1 B-01 · Verify, extract, and cold-launch the exact tarball — **PASS**

**Archive provenance and safety.** `sha256sum -c --ignore-missing SHA256SUMS` returned
`OK` for both Linux tarballs against the release's own checksum file. `file` reports
`gzip compressed data, from Unix` for both — the release really was tarred on a Unix
runner, which is what makes its mode bits authoritative (plan §2.4).

Member safety, all clean:

| Check | Result |
|---|---|
| absolute paths / `..` traversal | **none** in either archive |
| symlink, hardlink, device, FIFO members | **none** |
| setuid / setgid / sticky bits | **none** |
| world-writable members | **none** |
| exact duplicate names | none |
| case-colliding names | none |
| wrapper directory | none — flat `./` root, 240 members (desktop), 209 (CLI) |

Required root members, with the modes as shipped:

```
-rwxr-xr-x runner/runner   78256 2026-09-09 20:10 ./VisualCat
-rw-r--r-- runner/runner    1072 2026-09-09 20:10 ./LICENSE
-rw-r--r-- runner/runner    7666 2026-09-09 20:10 ./THIRD-PARTY-NOTICES.md
-rw-r--r-- runner/runner    1846 2026-09-09 20:10 ./README.txt
```

**The executable bit survives a GNU `tar` extraction**: `stat -c '%a'` reads `755` for
both `VisualCat` and `vcat` straight out of the archive, with `umask 0002` in force. No
`chmod` was needed. (Contrast the HEAD build packaged on Windows, which extracts `664` —
plan Appendix B #1. That is the harness, not the product, and is not filed.)

**Self-containment.** `ldd VisualCat` resolves everything from the ordinary host set:
`libdl`, `libpthread`, `libstdc++`, `libm`, `libgcc_s`, `libc`, `ld-linux`. No
`not found` in the executable or in any of the 16 bundled `.so` files. No system .NET
is installed and none was required. The running process maps the host's
`libX11.so.6.4.0`, `libICE.so.6.3.0`, `libSM.so.6.0.1`, `libfontconfig.so.1.12.0`,
`libGL`/`libGLX_mesa`, and `libicuuc/​libicui18n/​libicudata.so.70` — exactly the
dependency set `README.txt` names.

**Cold launch.** From `nohup ./VisualCat` to a mapped X window: **1.04 s** (plan budget:
median ≤3 s, none >5 s). stdout and stderr stayed **byte-empty** for the whole run.
`readlink /proc/<pid>/exe` matched the candidate path. Window identity:

```
_NET_WM_NAME  = "VisualCat v2 — See the shape of your log"
WM_CLASS      = "VisualCat", "VisualCat"
_NET_WM_ICON  = Icon (128 x 128)        # a real icon, not a placeholder
```

At a settled idle empty state: RSS 204 MiB, 11 threads, 140 descriptors, 1,163 mapped
regions, and — for the P-01 baseline — **zero sockets** (`ss -tanp` matched nothing for
the PID). No `journalctl`, `coredumpctl`, or `dmesg` entry was attributable to it.

**Data locality.** The first launch created exactly `~/.local/share/VisualCat/` and
`~/.local/share/VisualCat/Diagnostics/`, both `775 benny:benny`, and **nothing** under
`~/.config`, `/tmp`, or anywhere else in `$HOME`.

**README comprehension.** `README.txt` gives the launch line (`./VisualCat`, and
`./VisualCat --log path/to/logcat.txt`), the checksum command, the `gh attestation`
command, the X11/XWayland and `libX11`/`libICE`/`libSM`/`fontconfig` requirement, and
the `chmod +x` remedy for a lossy extractor. A first-time reader is served.

### 2.2 B-02 · Runtime dependency probe — **PASS**, and G7 — **FAIL**

The positive path passes (§2.1). The negative paths are where Linux differs from
Windows, and all four behave the same way: an unhandled managed exception, **printed
twice**, then `SIGABRT`.

| Probe | Exit | What the user sees |
|---|---|---|
| `LD_LIBRARY_PATH` shadowing `libX11.so.6` with an unusable file | 134 (`SIGABRT`) | `System.DllNotFoundException: Unable to load shared library 'libX11.so.6'…` + loader search list + .NET stack, ×2 |
| `env -u DISPLAY -u WAYLAND_DISPLAY ./VisualCat` | 134 | `System.Exception: XOpenDisplay failed` + .NET stack, ×2 |
| `DISPLAY=:77 ./VisualCat` (nothing listening) | 134 | identical to the above |
| `XAUTHORITY=/tmp/nonexistent-xauth ./VisualCat` | ran normally | **PASS** — XWayland accepts the same-user connection; the window opened |

The `libX11` case is the *better* of the two, because the loader's own search list at
least names the library. The `XOpenDisplay failed` case names nothing a user can act
on. Neither is a product sentence, and the abort is reported by the shell as
`Aborted (core dumped)`.

Privacy consequence checked (P-15): `apport` declined to store a report for a
non-packaged binary, `coredumpctl` listed nothing, `/var/crash` stayed empty. So no
core file containing log payload was produced — but that is host policy, not product
design. → [F-02](#f-02)

**glibc floor.** The host's 2.35 is comfortably above what the binaries require
(`VisualCat` itself needs ≤ `GLIBC_2.16`; `libSkiaSharp.so` needs ≤ `GLIBC_2.27`). No
older-glibc host was available, so the *lower* bound of the support claim is untested
and is recorded as a coverage gap, not a pass.

### 2.3 B-03 · Desktop empty state and command inventory — **PASS**, with a gap

Identity line, verbatim: **`VisualCat 2.0.13+0670981 · local-first · no telemetry`** —
matches the archive name, `README.txt`, and the release tag; no `-dev` suffix on a
release build, as R-14 requires.

Command bar, left to right: `＋ Open log` · `● ADB live` · `Open session` · `Recent` ·
`Follow file` · `Open archive` · `Save` · `Save portable` · `Export` · `More ▾`.
The last three are **disabled with no session open**, and `Open archive` is disabled
too. The hero row repeats `OPEN LOG · ADB LIVE · RECENT CAPTURES`. No Android-only
control appears. R-18 and R-42 hold.

`More ▾` contains exactly five items:

```
Open log with options…
Lines not on the timeline…        (disabled — correct, no session)
Appearance & timeline…
Session cache…
Diagnostic bundle…
```

**What is missing is *Check for updates…***, and its absence is structural, not a
rendering accident — see [F-03](#f-03). It also makes *Open log with options…* a
duplicate of *Open log* on this platform — see [F-05](#f-05).

### 2.4 B-04 · Import through the portal file chooser — **PASS**, with two findings

`Open log` raised a real portal dialog owned by **`xdg-desktop-portal-gnome`**
(window name `Open Android logcat file`, an `xdg-desktop-portal-gnome` top-level, not an
in-process Avalonia dialog). It offered `Recent`, the XDG user directories, a `Text logs`
type filter, and an `Open files read-only` toggle.

Typing a path is the plan's third route, and it worked here — `Ctrl+L`, then
`xdotool type` **to the focused window** (`--window` targeting does *not* land text in
this dialog; plan Appendix B #7). The location bar accepted
`/home/benny/vcat-run/corpus/small.txt` and `Return` closed the chooser.

The import review then opened, and its reading of the file is exact:

```
small.txt
Preview of up to the first 200 lines · 200 complete lines · 17.7 KiB retained
Detected sample format: Thread time (100 % confidence)
Parsing preview with auto-detection
199 parsed · 0 unknown · 0 rejected
Sample time span: 2026-05-15 14:13:37.000000 +02:00 — 2026-05-15 14:13:37.305000 +02:00 · Europe/Prague
Year follows source reference date 2026-09-11
No preview warnings.
```

199 parsed of 200 complete lines is correct: line 1 is
`--------- beginning of main`, a meta record. After `Import`, the workspace showed
**1,000 entries**, tab `small.txt`, status `Ready · 1,000 entries`, six severity rows
(F 156 · E 165 · W 182 · I 164 · D 145 · V 188 = 1,000), and the summary line
`1,000 in view · 1,000 match the filter · 1,000 in session · 05-15 14:13:37.000 — 05-15 14:13:38.501`.
The independent oracle agrees exactly: `vcat index` on the same file reports
`entries 1000, untimed 0, unknown 0, format ThreadTime, confidence 1`.

Two observations, both filed: the shell's file-operation card claimed **“Copying
file…”** for the entire time the modal review sat waiting for a decision
([F-04](#f-04)), and the live `Import` button is drawn in the disabled style
([F-08](#f-08)).

**Plan-versus-product discrepancy, recorded rather than filed.** B-04 expects that
"a confidently detected file is not interrupted by the review". It was: a 100 %-confidence
file still opened the review. This is deliberate — `MainView.OpenLogAsync` passes
`alwaysReview: !OperatingSystem.IsAndroid()`, so **every** desktop *Open log* reviews,
while `ImportPreparationPolicy.Decide` would have chosen a quick import at ≥ 0.6
confidence. The product and the plan disagree; the product is self-consistent, so this
is a **plan correction**, not a defect. (It is the reason [F-05](#f-05) exists.)

### 2.5 B-05 · Heat map to exact source bytes — **PASS**

Clicking a dense cell in the `E` row produced a hover readout
`14:13:37.574955 → 14:13:37.576242 · Σ 1 · F:0 E:1 W:0 I:0 D:0 V:0` and
`E · 1 of 1 in cell · 100 % · top 1× Connection <*> to <*> failed after <*>`, the entry
list narrowed to `1 in this bar`, and the selected entry opened with its source
context. The source gutter numbered from **1**, showed the selected line highlighted
with a `▶` marker, and kept context around it.

Independent byte verification against the corpus:

```shell
sed -n '374,382p' corpus/small.txt          # matches the on-screen gutter, line for line
```

and, addressing by the store's own recorded range rather than by line,

```shell
OFF=36183; LEN=83
tail -c +$((OFF+1)) corpus/small.txt | head -c $LEN
# 05-15 14:13:37.609000  1790 10558 E Network         : Rendering surface 0x0000A8C4
```

which is byte-identical to what the CLI reports for that entry
(`{"entryId":399,"raw":{"offset":36183,"length":83},"level":"Error","tag":"Network"}`).
Cell → entries → selected entry → raw bytes are consistent, and R-23/R-31/R-39 hold.

### 2.6 B-06 · Severity filters and clear semantics — **PASS**

Toggling `E` off: the chip bar gained `levels: hiding E ×` and a `Clear all`, the `E`
row left the plot, and the counts moved from `1,000 in view · 1,000 match the filter`
to `835 in view · 835 match the filter · 1,000 in session` — exactly 1,000 − 165.
`Clear all` restored the unfiltered view with no residue.

R-47 is visibly satisfied on the same screen: with the inspected entry now excluded by
the filter, the workspace said **“This entry is not in the current filter. It is still
open below.”** beside a `Clear filters` button, and the entry and its source context
stayed readable.

### 2.7 B-07 · Text and regex search — **PASS** (so far)

`FATAL EXCEPTION` over `small.txt`:

| Assertion | Observed | Oracle |
|---|---|---|
| total before stepping | `– / 139` | `grep -c 'FATAL EXCEPTION' small.txt` = **139**; `vcat search` = `{"matches":139,"markersTruncated":false}` |
| per-severity match counts | F 19 · E 24 · W 25 · I 20 · D 21 · V 30 | sums to 139 |
| *Last* (`⏭`) | counter `139 / 139`, entry at `14:13:38.491`, gutter line **994**, list `1 shown · 138 earlier · 0 later` | last match in the file is source line 994 |

So the counter's total really is every match in the session, each step selects an exact
record, and reaching a match reveals its row even when the loaded page is elsewhere —
R-57 and R-62 hold.

**R-51 holds.** With `Regex` on and `(unclosed` submitted, the product printed one
sentence beside the field:

> `Not a valid regular expression: there are more "(" than ")" (position 9).`

No resource key, no framework dump, in a trimmed Release build. The `Search` button
went disabled while the pattern was invalid, and — the part that matters — **the
previous result stood**: the chip still read `text = FATAL EXCEPTION`, the counter still
read `139 / 139`, and the selected entry and its source context were untouched.

### 2.8 A-08 / A-26 · Adversarial corpus sweep — **FAIL**

The full §3.2 corpus was built from the plan's recipe and swept through
`vcat index` (the CLI leg; the GUI leg follows). Every ordinary file is accounted for
exactly, and every refusal is honest — with two exceptions that are filed.

| File | Exit | Result |
|---|---|---|
| `small.txt` / `lf.txt` | 0 | 1,000 entries, 0 untimed, 0 unknown |
| `medium.txt` | 0 | 99,992 + 8 unknown = **100,000** exactly |
| `large.txt` | 0 | 999,885 + 115 unknown = **1,000,000** exactly |
| `crlf.txt`, `mixed-eol.txt`, `nofinalnewline.txt`, `bom.txt` | 0 | 1,000 entries each — every newline form and the BOM survive |
| `truncated.txt` | 0 | 49,981 + 7 unknown; the final incomplete line is not invented |
| `longline.txt` (one 2 MiB line) | 0 | 1,001 entries, no hang |
| `nonutf8.bin` | 0 | 1,010 entries + 1 unknown; invalid bytes retained, not silently replaced |
| `controls.txt` (ANSI CSI/OSC, NUL, bidi, zero-width) | 0 | 3 entries, all parsed |
| `unicode-name-😀-é-中文.txt`, `nfd-name-é.txt`, `name with spaces, 'quotes' and $dollar.txt`, `name-with<newline>.txt` | 0 | 1,000 entries each — **every shell-hostile but UTF-8-valid name works** |
| `Session.txt` / `session.txt` | 0 | two distinct sessions on this case-sensitive ext4 — R-67 holds |
| `empty.txt` | 1 | `error: This file is empty — there is nothing to import.` |
| `notalog.bin` (10 MiB random) | 1 | `error: No supported logcat format could be detected in this file.` — nothing invented |
| `outcomes.txt` | 1 | refused by detection, which is the confidence threshold working (plan Appendix B #45) |
| `cr-only.txt` (lone CR) | 1 | refused — honest, but lone-CR offsets are therefore **not** covered |
| **`fmt-long.txt`** | 0 | **1,701 of 5,000 records**, confidence 0.16 → [F-01](#f-01) |
| **`latin1-name-\xE9.bin`** | 1 | **`error: Log source was not found.`** → [F-06](#f-06) |

`fmt-brief.txt` reports `0 entries · 5,000 untimed`, which is correct — brief format
carries no timestamp — and its detection confidence is 0.667, only just over the 0.6
threshold.

The sweep also produced [F-07](#f-07): the loop was first written the way plan §2.3
instructs, with `--` before the file name, and **every** invocation failed.

### 2.9 I-01 / I-13 / I-14 · CLI contract — **PASS** with findings

`vcat --version`, `vcat help`, and `vcat` with no arguments all print to **stdout** and
exit **0**, as `CLI.md` documents. Exit codes match the documented contract:

| Command | Exit | Documented |
|---|---|---|
| `--version`, `help`, `info <log>` | 0 | 0 on success ✓ |
| `bogus-command` | 2 | 2 for invalid command input ✓ — and the message lands on **stderr**: `error: Unknown command 'bogus-command'.` |
| `verify /nonexistent.vcat` | 3 | 3 for verification failure ✓, with a complete JSON report on stdout (`"code": "session.open"`, `"message": "Session manifest was not found."`) |

Progress discipline holds: `vcat index` with stderr redirected to a file wrote **0
bytes** and **0 carriage returns** — no spinner, no repainting, clean for a pipeline.

**Generator determinism (I-14).** All five formats are byte-identical across two runs
with the same seed:

```
threadtime  identical   brief  identical   long  identical   epoch  identical   time  identical
```

**Format detection of the generator's own output (I-14 / R-61) — FAIL.** The plan
requires "every generated file is detected as its own format at full confidence":

| Format | Detected as | Confidence |
|---|---|---|
| threadtime | ThreadTime | **1.0** |
| time | Time | **1.0** |
| epoch | Epoch | **1.0** |
| brief | Brief | 0.667 |
| **long** | LongFormat | **0.160** |

`long` is far below the 0.6 auto-detect threshold. The cause is [F-01](#f-01), not the
generator.

**R-06 — FAIL on the release, PASS on HEAD.** `vcat query <session> --limit 3`:

* release `2.0.13+0670981`: **72 lines** for 3 entries — one indented document per
  entry; `jq` on the first line fails with `Unfinished JSON term at EOF`.
* HEAD `2.0.13+692ad6b`: **3 lines**, one compact object per line, each consumable by
  `jq` on its own. `vcat info` still prints one indented document, as the fix intends.

This matches the `[Unreleased]` changelog entry exactly. The guard is real and the fix
works; it is simply not in the shipped 2.0.13.

### 2.10 B-20 · Off-timeline and unparsed evidence — **PASS on HEAD**, R-04/R-05 **FAIL on 2.0.13**

Corpus: `crashy.txt` (1,011 lines) and `crashy-large.txt` (1,000,010 lines — `large.txt` with the
crash block spliced in at line 500,001). Independent oracle, from `vcat info` on the same files:

| | `crashy.txt` | `crashy-large.txt` |
|---|---|---|
| source lines | 1,011 | 1,000,010 |
| parsed entries | 1,004 (1,003 timed + 1 untimed) | 999,888 (999,887 timed + 1 untimed) |
| meta records | 2 | 2 |
| unknown lines | 1 | **117** |
| rejected candidates | 1 | 1 |
| continuations | 3 | 2 |
| **accounted total** | **1,011 ✓** | **1,000,010 ✓** |

Every line of both files is accounted for exactly once. That part is solid on both builds.

**On the shipped 2.0.13 the notice is wrong and names a command that does not exist.** Over
`crashy-large.txt` the screen carried three different numbers for closely related things:

| Surface | Text |
|---|---|
| notice lane | `8 lines could not be read as a logcat record. They are kept byte for byte; open them from More → `**`Unparsed lines…`** |
| chip bar | `121 off timeline` |
| summary line | `117 unparsed lines` |
| *More* menu | `Lines not on the timeline…` |

The true "could not be read" count is **118** (117 unknown + 1 rejected). The notice said **8** —
computed from whatever the counters held when the first snapshot carrying one arrived, and never
corrected. **R-04 FAIL, R-05 FAIL** on 2.0.13, exactly as the `[Unreleased]` changelog describes.

**On HEAD both pass.** The notice reads:

> `118 of 1,000,010 lines are not logcat records — usually stack-trace frames. They are kept byte
> for byte; open them from More → Lines not on the timeline… 1 records carried no usable
> timestamp, so they are not on the …`

118 matches the oracle, the total line count is named, and the command name matches the menu.
(Minor wart worth fixing while the string is open: *“1 records carried no usable timestamp”* —
singular/plural.)

**The dialog itself is good.** *More → Lines not on the timeline…* opens with a header matching the
oracle field for field — `117 unknown · 2 continuation · 1 rejected · 1 untimed` — an explanation of
why two different kinds of line end up there, and, on screen rather than in a tooltip, the full
gutter legend:

```
en entry · mt marker · .. continuation · e? untimed · ?? unknown · !! rejected
```

**R-54 PASS.** Rows are source-ordered and carry the source line number, the gutter code and the
byte-exact line. What it does not do is say how much of the population it has listed — see
[F-15](#f-15).

### 2.11 X-01 / R-01–R-03 · Large-import A/B — **2.0.13 FAIL 3/5 · HEAD PASS 5/5**

This is the procedure the plan names as the only reliable check for the snapshot-refresh race
(§12.1 and Appendix B #46): open a one-million-line log with `--log`, five times per build, and read
the summary line and the zoom readout off the screen. Corpus `crashy-large.txt`; oracle 999,887
timed entries, 1 untimed, 117 unknown, span `05-15 14:13:36.771 — 05-15 14:38:35.471`.

| Build | Run | Summary line | Verdict |
|---|---|---|---|
| 2.0.13 | 1 | `999,887 in view · 999,888 match the filter · 999,887 timed in session` | pass |
| 2.0.13 | 2 | full session | pass |
| 2.0.13 | 3 | **`900,001 in view · 900,002 match the filter · 900,001 timed in session · 106 unparsed lines`** | **FAIL** |
| 2.0.13 | 4 | **`900,001 …`** | **FAIL** |
| 2.0.13 | 5 | **`900,001 …`** | **FAIL** |
| HEAD | 1–5 | `999,887 in view · 999,888 match the filter · 999,887 timed in session · 1 untimed · 117 unparsed lines · 05-15 14:13:36.771 — 05-15 14:38:35.471` on every run | pass |

On each failing run the status bar underneath read **`Ready · 999,887 entries`** over a view showing
900,001, the six severity rows summed to ~900 k instead of ~1 M, and — the symptom the changelog
names — the zoom readout beside the controls said **`24.98 min · 1.13 s/px`** while the plot's own
header said **`22.48 min · 1.02 s/px`**. Nothing on screen said the picture was partial.

Two things follow. The `[Unreleased]` fix is real, and it is confirmed by an A/B of the shipped
binary against a real compositor, which is the only way this reproduces. And **the currently
published 2.0.13 shows a prefix of a large log more often than not on this host** — that is the
strongest argument in this report for shipping the `[Unreleased]` work.

### 2.12 B-10 / B-11 / I-05 / R-37 · Host ADB, physical device — **PASS**

Discovery listed the phone within budget as `RFCRC0A9GND · SM_G990B · Device`, with
`main`/`system`/`crash` pre-checked, pre-roll `0`, and the text *“1 device detected. Unauthorized
devices must be approved on the device.”*

The child process is exactly what P-11 and P-19 ask for — one argument array, serial-qualified, no
shell, and the negotiated format visible in the command line itself:

```
/usr/bin/adb -s RFCRC0A9GND logcat -b main,system,crash -D \
             -v threadtime,year,UTC,usec -T 2026-09-11 22:02:12.481160
```

Capture ran 4 m 27 s over real Samsung traffic while the §3.4 marker generator ran on the device.
Status during capture: `Capturing · 488 lines received · 45/s · ADB device RFCRC0A9GND`. Tab title
`ADB RFCRC0A9GND 00h02m12` — unambiguous source and start identity (**R-15**).

**Stop (B-11).** One press. The status went to **`Stopped · 19,577 entries kept`** and stayed there;
`Follow: on` and `Stop capture` disappeared; the control never sprang back. **No orphan `adb`
remained** (`ps -ef | grep '[a]db .*logcat'` empty) and there were zero zombies.

**Integrity and the marker oracle.**

| Check | Result |
|---|---|
| `vcat verify` | `isValid true`, 19,577 entries checked, 20,346 source records, **0 issues** |
| accounting | 19,577 parsed + 769 meta = 20,346 source lines ✓; 0 unknown, 0 rejected, 0 continuations |
| markers on the device (`logcat -d -s VCATTEST`) | **600** `steady` markers between BEGIN and END |
| markers in the session (`vcat search`) | **600** (each search also matches one `adbd` line echoing the shell command, which contains the literal text) |
| `chatty` declared drops in the window | **0** |
| **R-37** buffer attribution | `main 18,954` + `system 623` = **19,577 ✓**, `crash` correctly absent (empty on this device) |

**I-05 discovery parity.** `vcat adb-devices` printed the documented shape and agreed with the
desktop's list:

```json
[{ "serial":"RFCRC0A9GND", "state":"Device", "model":"SM_G990B", "product":"r9qxeea",
   "transportId":"1",
   "properties":{ "product":"r9qxeea","model":"SM_G990B","device":"r9q","transport_id":"1" }}]
```

Not claimed from this host: the `no permissions` / `unauthorized` / `offline` states, `udev` rules
and group membership — the guest is an ADB *client* of the host's server (§1.5), so those states
cannot be produced here. **A-16 is a coverage gap, not a pass.**

One wording observation carried over from the Windows run: the live status counts *“lines
received”* (488) while the summary line counts *entries* (247). Both are true of different
populations, and neither says which, so a reader comparing them sees a discrepancy that is not one.

### 2.13 B-13 / B-15 / I-07 / R-59 · Save and export — **PASS**

**B-13 standard save.** The notice named the destination in full:

> `Saved: /tmp/out/adb-capture-live.vcat/ADB RFCRC0A9GND 00h02m12-20260912-000818.vcat`

A VisualCat session is a directory, so the picker asks for a *destination folder* and creates the
session inside it under a generated name; the notice states the real path, which is what B-13
requires. `vcat verify` on the saved copy: `isValid true`, 19,577 entries, 20,346 source records.

**P-22 modes** (`umask 0002`): every file `-rw-rw-r--`, every directory `drwxrwxr-x`, all
`benny:benny`; **0** world-writable, **0** setuid/setgid — across the saved session, the data root,
`settings.json` (`664`) and the diagnostics file.

**B-15 CSV export.** The review names each scope with its exact timed row count, the zone it is
using and the filters it applies:

```
Visible plot range — 3,518 timed rows        22:06:09.464 to 22:06:39.464 (end excluded)
All timed entries in session — 19,577 timed rows
  Every parsed entry on the timeline; workspace filters are ignored.      ← R-59
3,518 timed rows · UTC · Filters: time
Row order: Source order     Encoding: UTF-8 with byte-order mark
A successful export remembers these two choices as the new defaults.
```

Chosen scope *All timed entries in session*. Completion notice:
**`Exported 19,577 timed rows · all timed entries in session · gui-export.csv`** — same number, same
scope, same file name. The file:

| Property | Value |
|---|---|
| data rows | **19,577** + 1 header = 19,578 lines ✓ |
| encoding | BOM `EF BB BF` ✓ matches the selected *UTF-8 with byte-order mark* |
| line endings | **0** carriage returns, 19,578 LF — counted with `tr -dc '\r'`, never with a locale-sensitive `grep` |
| extension | `gui-export.csv` — not doubled |
| header | `timestamp_utc,level,pid,tid,buffer,tag,template_id,message` |

**I-07 — byte-identical.** The first comparison differed at line 279 because `vcat export` defaults
to chronological order while the desktop's stored default is *Source order*. With the orders matched
the contract holds exactly:

```shell
vcat export "<session>" cli-src.csv --type csv --order source
cmp gui-export.csv cli-src.csv     # no output — 2,796,771 bytes each, BYTE-IDENTICAL
```

That difference is the contract working, not a defect — but it is worth knowing that the desktop's
default export and the CLI's default export are **not** the same document, and only the CLI's
`--order` flag makes them agree.

### 2.14 B-12 · Follow a growing file — **FAIL**

Follow itself works. Against `/tmp/growing2.txt` with a recorded producer ledger: a second tab
appeared named after the file, `Follow: on` and `Stop capture` belonged to it (**R-34**), the status
read `Capturing · 120 lines received · 3/s · /tmp/growing2.txt (follow)`, and the deliberately
partial line — written without its newline and completed eight seconds later — was published
**once, complete**, as its own template `PARTIAL-LINE-SEQ-60-completed · 1`. A partial line is never
published as a record.

**R-33 passes on its own terms.** After the writer stopped, the rate disappeared and the heartbeat
named the silence: `Capturing · 303 lines received · no source lines for 1m 39s ·
/tmp/growing2.txt (follow)`.

**But that heartbeat is not true**, and that is [F-09](#f-09). Measured against the producer ledger
and an independent `vcat index` of the same file, the session was holding records back the whole
time. Second, controlled run — 120 records appended at 0.3 s, then the writer stops:

| Moment | Records in the session | Records in the file |
|---|---|---|
| writer stops | — | 120 |
| t + 5 s | **107** | 120 |
| t + 20 s | **107** | 120 |
| t + 45 s | **107** | 120 |
| t + 75 s | **107** | 120 |
| after **one** further append | **121** | 121 |

`vcat index` of the same file recovers all 120. Pressing *Stop capture* also flushes them — the
first run went from 297 to 300 of 300 the moment Stop was pressed, and finalized with
`parsedEntries 302`, state `Ready`. So nothing is lost; but the live view sits permanently behind
the live edge, and the product says the opposite.

A static file behaves correctly: following a file that already held 50 records published all 50 at
once, and a single append after that appeared within 12 s. It is the **incremental streaming path**
that keeps a tail.

### 2.15 A-12 / A-13 / U-08 / U-19 / visual — settings, accessibility, rendering

**A-13 / R-16 PASS.** Every label in *Appearance & timeline* is human language, and so is every
value: *Theme · Follow the system*, *Prefer high-contrast presentation*, *Text scale*, *ADB
executable*, *Default capture buffers (comma separated)*, *Default ADB pre-roll (seconds)*,
*Temporary session directory*, *Live UI refresh limit (Hz)*, *Timeline intensity scale ·
Logarithmic*, *Timeline normalization · Per severity row*, *Maximum zoom precision*, *Snap timeline
cells to device pixels*, *Minimum bar width*, *Default export order · Source order*, *Normalized CSV
encoding · UTF-8 with byte-order mark*, *Write redacted structured diagnostics* (on). No `PerRow`,
`GlobalViewport` or `SourceSequence` anywhere. Two wording problems are filed as [F-14](#f-14).

**A-12 partial PASS.** *Session cache* named its location correctly for Linux —
`Cache location: /home/benny/.local/share/VisualCat/Sessions` — reported `13 temporary sessions ·
1.61 GiB`, and stated plainly that *“Cleanup deletes whole session folders; it does not claim
forensic erasure.”* Cleanup itself was not executed: it is destructive and the run still needed the
sessions.

**U-08 / R-41 FAIL** — see [F-11](#f-11). With `org.gnome.desktop.interface toolkit-accessibility`
set true and the app restarted, the AT-SPI tree is otherwise good: **435 nodes**, 23 push buttons, 7
toggle buttons, 2 check boxes, 4 page tabs, 15 list items, and genuinely useful accessible names —
`Fatal level`, `Apply the query`, `Fit the complete session`, `Show complete session small.txt`,
`Close session small.txt`, `Show the full message of the selected entry`. The summary line is
exposed verbatim as a label. That is a solid foundation; the modal boundary and the application
name ([F-13](#f-13)) are what let it down.

**U-19 FAIL** — Escape does not dismiss *Appearance & timeline*; see [F-12](#f-12). The window
manager's close affordance and `xdotool windowclose` (graceful `WM_DELETE_WINDOW`) both work.

**Rendering FAIL** — see [F-10](#f-10). This affected more of the run than any other single defect
and is the reason several screenshots in this report needed a forced resize before they could be
read.

### 2.16 B-16 / P-01 / P-02 / P-11 / P-18 / P-22 — startup, privacy, residue

**B-16 PASS.** Every invalid form launched to a usable empty state with one notice and no crash:

| Invocation | Result |
|---|---|
| `--log /nonexistent/nope.txt` | notice `Startup source not found: /nonexistent/nope.txt`, empty state with *Recent captures* listed; the process kept running |
| `--log /tmp` (directory where a file is expected) | launched, no crash |
| `--frobnicate` (unknown flag) | launched, no crash, not treated as a file name |
| bare path, relative path | opened the intended source |

No run stayed on *Opening* and no cancellation was reported as a startup error (**R-38**); a failed
startup source produced a useful empty state rather than a hollow workspace (**R-20**).

**P-01 PASS — the strongest result in this report.** After a cold launch, a portal import, a
1 M-line import, five `--log` launches, a 4½-minute live ADB capture, a save, a CSV export and
~25 minutes of uptime, the process held:

```
ss -tanp | grep pid=<pid>     →  (nothing)
lsof -p <pid> -a -i           →  (nothing)
```

**Zero sockets of any kind.** The only network-shaped thing in the picture was the local ADB client
connection, and that lives in the `adb` child, not in VisualCat.

**P-02 PASS, with one observation that belongs to the desktop rather than the product.** Everything
VisualCat wrote stayed inside `~/.local/share/VisualCat/{Sessions,Diagnostics,SessionAccess-v1,
settings.json}` and the destinations the user chose. Nothing appeared in `~/.config`. But opening a
log through the portal caused **the desktop** to record the log's path in
`~/.local/share/recently-used.xbel` and `~/.local/share/gnome-shell/application_state`. That is
`xdg-desktop-portal-gnome` doing what it does for every application, not VisualCat — it still
deserves a sentence in `PRIVACY.md`, because a reader of a local-first log viewer may not expect the
*name of the log they opened* to be added to a desktop-wide recent-files list.

**P-11 PASS.** One child process during capture, with an argument array and no shell (§2.12); zero
zombies; no child outlived its capture.

**P-19 PASS.** The product used the existing ADB server, never killed it, and never touched
`~/.android/adbkey` — the guest has no `~/.android` at all. Every device command was
serial-qualified.

**P-22 PASS** for the `umask 002` leg (§2.13). The `umask 077` leg was not executed and is a
coverage gap.

**P-18 PASS with [F-17](#f-17).** Deleting an extracted candidate directory leaves the XDG data root
untouched, as documented. No `.desktop` file, no systemd unit, no autostart entry, no MIME
association and no shell-profile modification was created — consistent with `SUPPORT.md`. The one
residue is 14 stale `/tmp/dotnet-diagnostic-*-socket` files from exited processes.

### 2.17 Plan corrections observed during this run

Places where the plan and the product disagree and **the product is right**. Recorded here so the
next run does not re-file them as defects.

1. **B-04** expects that "a confidently detected file is not interrupted by the review". On the
   desktop it always is: `OpenLogAsync` passes `alwaysReview: !OperatingSystem.IsAndroid()`. The
   plan sentence describes the Android behaviour. Either the plan should say so, or the product
   should adopt the plan's behaviour — [F-05](#f-05) argues for the latter.
2. **§2.3** instructs testers to "use `--` before filename arguments". `vcat` rejects `--`
   ([F-07](#f-07)); until that is fixed, following the plan's own instruction makes every CLI row
   fail on setup rather than on the product.
3. **§3.2** lists `cr-only.txt` among the corpora whose "exact byte offsets across every newline
   form, including lone CR" must hold. Lone-CR input is refused outright
   (`No supported logcat format could be detected in this file.`). The refusal is honest, so this is
   a plan expectation to soften — but it means lone-CR offsets are untested, not passing.
4. **I-14** requires every generated format to be "detected as its own format at full confidence".
   `brief` detects at 0.667 by construction, because brief format carries no timestamp and the
   detector cannot reach 1.0 on it. Only `long` is a genuine failure ([F-01](#f-01)). The assertion
   should read "at or above the auto-detect threshold", with `long` expected at ≥ 0.9.

---

## 3. Findings

### F-01 · Major · `logcat -v long` silently loses every record whose thread id has five digits

**Severity** Major — a documented, user-selectable format produces **silently wrong
results**: records vanish, nothing counts them as lost, and the file then fails
automatic detection.

**Where** Parser. `src/VisualCat.Core/Parsing/LogcatParser.cs`, `TryLong`.

**What happens.** `TryLong` splits the bracketed header on whitespace and requires the
third token to end with `:`

```csharp
if (!TryToken(trimmed, ref position, out var pidWithColon) ||
    !TryToken(trimmed, ref position, out var tidToken)   ||
    !TryToken(trimmed, ref position, out var priorityTag))
…
if (… || !pidWithColon.EndsWith(':') || …)
```

Android prints that field as `%5d:%5d`. When the thread id needs all five columns there
is **no space** after the colon, `pid:tid` is a single token, the header is rejected —
and the rejected header line *and its message line* are then folded into the previous
entry as **continuations**.

**Minimal reproduction** (four records, hand-written exactly as `logcat -v long` prints
them):

```
[ 05-15 14:13:37.003   926: 9315 W/Camera ]      ← parses
[ 05-15 14:13:37.004   926:12019 W/Camera ]      ← LOST
[ 05-15 14:13:37.005 10503:12019 W/Camera ]      ← LOST
[ 05-15 14:13:37.006 10503: 5136 W/Camera ]      ← parses
```

```shell
vcat index long-shapes.txt --format long --output long-shapes.vcat
# "entries": 2                     ← of four records
# counters: parsedEntries 2, continuations 6, unknownLines 0, rejectedCandidates 0
```

**On a real device.** 4,000 records from the Samsung SM-G990B under test,
`adb -s RFCRC0A9GND logcat -d -v long -b main -t 4000`:

| Measure | Value |
|---|---|
| header lines in the file | 4,001 |
| headers with a 5-digit tid (no space) | **472** |
| `parsedEntries` | 3,528 |
| `unknownLines` / `rejectedCandidates` | **0 / 0** |
| `continuations` | 4,947 |
| auto-detection confidence | **0.449** — below the 0.6 threshold |

So 472 real records (11.8 % of this sample; 66 % of the synthetic corpus, whose tids are
uniform over a wider range) are **gone from the timeline, the counts, the facets, the
templates and every export**, and the two counters a reader would look at to notice —
unknown lines and rejected candidates — both say **zero**. The lost content is not even
discoverable through *Lines not on the timeline…*, because it is filed as a
*continuation*, which asserts it belongs to the entry above it. It does not.

The second half of the defect is the one a user meets first: because so many headers
fail, the detector's score collapses, so **an ordinary unmodified `logcat -v long` dump
from a real phone is refused outright** — `error: No supported logcat format could be
detected in this file.` in the CLI, and a low-confidence review in the desktop.

**Expected** (`CLI.md` lists `long` among `--format threadtime|time|brief|long|epoch`;
plan §1.1 "five supported logcat formats"; R-61) every long-format record is parsed,
and any record that is not is counted as unknown or rejected, never as a continuation.

**Suggested fix.**

1. In `TryLong`, stop tokenizing `pid:tid` on whitespace. Read the field as one unit
   and split it on the **last** `:` before the priority/tag token:
   ```csharp
   // Android prints "%5d:%5d", so a five-digit tid leaves no space after the colon.
   // Take the whole pid:tid field, then split on ':' — never on whitespace.
   if (!TryToken(trimmed, ref position, out var ids)) { … }
   var colon = ids.LastIndexOf(':');
   if (colon <= 0 || !TryPositiveInt(ids[..colon], out var pid)
                  || !TryPositiveInt(ids[(colon + 1)..], out var tid)) { … }
   ```
   and accept the split-by-space spelling as well, so both `926: 9315` and `926:12019`
   work. The same shape appears in the `[ … ]` header with an optional zone offset, so
   keep `TryConsumeZoneOffset` where it is.
2. Add the four-line fixture above to the golden parser fixture
   (`test-data/golden-formats.txt`) with its expected counts, so the width case is
   covered by `dotnet test` and cannot regress.
3. Change `SyntheticLogGenerator` to emit tids that exercise **both** widths
   deliberately (it currently does so by accident), and add an I-14 assertion that every
   generated format detects at ≥ 0.9 confidence — that assertion alone would have caught
   this.
4. Independently of the parse fix: make a header that fails validation a **rejected
   candidate**, not a continuation. A line that starts `[` and ends `]` and carries a
   date and a priority/tag is self-evidently an attempted header; silently reclassifying
   it as message text of the previous record is what turned a parse bug into a
   *silent data-loss* bug. Rejected candidates are counted, surfaced in the chip bar and
   reachable from *Lines not on the timeline…*; continuations are not.

**Appendix-B trap checks.** Not a VM artifact (pure parsing, reproduced from a byte
file). Not a locale artifact (`LC_ALL=C` reproduces). Not a generator artifact —
confirmed on unmodified real-device output. Not a line-ending artifact (LF only,
counted with `tr -dc '\r'`).

---

### F-02 · Minor · A missing display or `libX11` ends in a doubled .NET stack trace and `SIGABRT`

**Severity** Minor — bounded workaround (install the package, set `DISPLAY`), but it is
the **first** thing a user on a headless box, an SSH session without `-X`, or a
distribution without `libx11-6` will see, and plan **G7** names this outcome a finding
explicitly.

**Reproduction and verbatim output** — all three exit `134` (`SIGABRT`), and the shell
reports `Aborted (core dumped)`:

```shell
env -u DISPLAY -u WAYLAND_DISPLAY ./VisualCat
# System.Exception: XOpenDisplay failed
#    at Avalonia.X11.AvaloniaX11Platform.Initialize(X11PlatformOptions options)
#    …
#    at VisualCat.Desktop.Program.Main(String[] args) in /_/src/VisualCat.Desktop/Program.cs:line 13
# Unhandled exception. System.Exception: XOpenDisplay failed        ← the same trace again
#    …
```

`DISPLAY=:77` (nothing listening) is identical. Shadowing `libX11.so.6` gives
`System.DllNotFoundException`, which at least names the library, still twice, still
aborting.

**Why it is doubled.** `src/VisualCat.Desktop/Program.cs` catches, writes the exception
to `Console.Error`, and rethrows; the runtime's default handler then prints the same
exception again before aborting.

```csharp
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    throw;                       // runtime prints it a second time, then SIGABRT
}
```

**Suggested fix.** Keep the rethrow only for genuinely unexpected faults, and translate
the two startup failures a Linux user actually hits into one product sentence and a
non-abort exit:

```csharp
catch (Exception exception) when (IsGraphicalStartupFailure(exception))
{
    Console.Error.WriteLine(
        "VisualCat needs a graphical X11 or XWayland session.\n" +
        $"  DISPLAY={Environment.GetEnvironmentVariable("DISPLAY") ?? "(not set)"}\n" +
        "  On Debian or Ubuntu the packages are: libx11-6 libice6 libsm6 libfontconfig1\n" +
        "  Over SSH, connect with -X or -Y, or run the vcat CLI instead — it needs no display.");
    Environment.Exit(69);        // EX_UNAVAILABLE; not an abort, no core dump
}
```

Two details worth keeping: mention **`vcat`**, because the CLI is the right answer for
the headless case and the user has it in the other tarball; and exit rather than abort,
so hosts with `apport`/`systemd-coredump` enabled do not store a dump of a log viewer
(plan P-15) for what is an ordinary configuration mistake.

**Appendix-B trap checks.** `ldd` run first (#15). Host `libX11` present and resolvable.
Not a `noexec`/AppArmor denial — `aa-status` showed no matching denial and the same
binary starts normally with `DISPLAY` set.

---

### F-03 · Minor · The desktop has no update route at all, and `SUPPORT.md` describes one it does not have

**Severity** Minor by workaround (the user can visit GitHub), but it makes plan rows
**A-28** and **R-60** unpassable on this platform and leaves a documentation
contradiction in the shipped support matrix.

**Observed.** `More ▾` on the Linux desktop contains five items and **no
*Check for updates…***. There is no other update affordance anywhere in the shell: no
status-lane offer, no About box, no releases link.

**Why.** `MainView` gates the command on the host being able to say where it was
installed from:

```csharp
if (PlatformSourceRegistry.GetInstallOrigin is { } installOrigin)
{
    Setting("Check for updates…", CheckForUpdatesManuallyAsync,
        installOrigin() == AppInstallOrigin.PlayStore
            ? "Ask Google Play whether a newer VisualCat is out"
            : "Open the GitHub releases page — this build cannot update itself");
}
```

`GetInstallOrigin` is assigned in exactly one place — `src/VisualCat.Android/MainActivity.cs`.
On every desktop it stays `null`, so the branch never runs. The non-Play description
string (*"Open the GitHub releases page — this build cannot update itself"*) is written
for precisely this case and is currently unreachable.

**The documentation contradiction.** `docs/SUPPORT.md` §"Keeping VisualCat up to date"
ends its GitHub-install paragraph with:

> **Check for updates…** says so plainly and offers to open the releases page in your
> browser, where you can download the newer APK and install it the same way you
> installed this one. **Desktop releases work the same way: download the new archive.**

A reader on Linux reads that as a promise about their build, goes to `More ▾`, and the
command is not there.

**Suggested fix.** The honest, cheap version is to make the desktop answer the same
question the string already knows how to answer:

1. Assign `PlatformSourceRegistry.GetInstallOrigin` in `VisualCat.Desktop.Program`
   (and the macOS/Windows heads) to a function returning
   `AppInstallOrigin.SelfBuiltOrDirectDownload` — there is nothing to probe, a portable
   tarball is by definition not store-installed. The existing description string and
   `CheckForUpdatesManuallyAsync` then work unchanged, and P-01 stays satisfied because
   that path opens a browser only on request and fetches nothing itself.
2. Verify the browser hand-off on Linux specifically: it must go through
   `xdg-open`/the portal's `OpenURI`, and when no handler is registered the product must
   **show the URL** rather than fail silently (plan A-28).
3. If the decision is instead that desktops deliberately have no update command, then
   change `SUPPORT.md` to say so — "the desktop builds have no update check; watch the
   releases page" — and strike **A-28** and **R-60** from the Linux plan as
   *explicitly unsupported* rather than leaving them as rows that can never pass.

---

### F-04 · Minor · "Copying file…" claims to be copying for as long as the import review waits on the user

**Severity** Minor — no data effect, but the shell's one file-operation card is the
product's promise that visible work is real work, and here it is not.

**Observed.** From the moment `Open log` is pressed the status bar shows
`Opening log…` with an indeterminate progress bar and a `Cancel`. After a file is
chosen it becomes **`Copying file…`** with an animating bar — and stays there for the
entire life of the modal *Import preview*. Measured on a **90 KiB** file, it animated
for **more than four minutes**, which was simply how long the review was left open. It
cleared only when `Import` was pressed.

Proof that nothing was being copied: during that window the process had **no descriptor
open on the source** (`ls -l /proc/<pid>/fd` showed only the two redirected streams and
a `memfd`), and no temporary materialization existed under `/tmp`, `/var/tmp`, or the
data root.

**Why.** `MainView.OpenLogPickerCoreAsync` reports `FileWorkStage.Copying` *before*
materializing and does not report anything else until the import is handed off; the
modal `PrepareImportAsync` — including `ShowDialogAsync(dialog)` — runs inside that
window.

```csharp
operation.Report(new FileWorkProgress(FileWorkStage.Copying));
var materialized = await StorageFileBridge.MaterializeForReadAsync(file, operation.Progress, operation.Token);
var preparation  = await PrepareImportAsync(…);      // ← opens the modal; card still says "Copying file…"
```

**Suggested fix.** Report a stage that is true, and stop animating when nothing is
moving:

1. Add a `FileWorkStage.AwaitingReview` (label *"Waiting for import options"*, or simply
   drop the card while a modal owns the interaction) and report it immediately after
   `MaterializeForReadAsync` returns, before `PrepareImportAsync`.
2. Give that stage a **determinate-idle** presentation rather than a marquee: an
   animating progress bar is the product asserting that bytes are moving.
3. While the review is open, the card's `Cancel` and the review's own `Cancel` are two
   controls for one decision. Either hide the card's `Cancel` for the review stage, or
   make it close the review — today it is the only one of the two whose effect a reader
   cannot predict.
4. `Opening log…` while the *picker* is open has the same shape but is defensible;
   at minimum it should not animate either, since the product is idle and the user is
   browsing.

---

### F-05 · Polish · *Open log* and *Open log with options…* are the same command on the desktop

**Severity** Polish — two menu entries, one behaviour; nothing breaks.

**Observed.** `More ▾ → Open log with options…` and the `＋ Open log` button both open
the picker and then always open the import review.

```csharp
private Task OpenLogAsync() =>
    OpenLogPickerAsync(alwaysReview: !OperatingSystem.IsAndroid(), withOptions: false);
private Task OpenLogWithOptionsAsync() =>
    OpenLogPickerAsync(alwaysReview: true, withOptions: true);
```

On any desktop `!OperatingSystem.IsAndroid()` is `true`, so the two calls differ only in
the picker's **title** (`Open Android logcat file` vs `… with options`). The command was
added for the phone, where `Open log` quick-imports — the changelog says so: *"**Open log
with options…** in *More* opens the picker and then the import review on either
platform, which is how a phone can set the assumed year or time zone before importing."*
On the desktop it is a second door into the same room.

**Suggested fix.** Either hide `Open log with options…` where `alwaysReview` is already
the default —

```csharp
if (OperatingSystem.IsAndroid())
{
    Secondary("Open log with options…", OpenLogWithOptionsAsync, "Set the year or time zone before importing");
}
```

— or, better for the desktop, make the pair actually mean something: let plain
*Open log* quick-import a confidently detected file (which is what
`ImportPreparationPolicy.Decide` is written to do, and what plan **B-04** expects) and
keep *Open log with options…* as the deliberate way to force the review. That removes a
click from the common path and gives the second command a reason to exist.

---

### F-06 · Minor · A legal non-UTF-8 file name is reported as "Log source was not found."

**Severity** Minor by frequency, but it is a **wrong diagnosis**: the file is there and
readable, and the message sends the user looking for a missing file.

**Reproduction.**

```shell
cd ~/vcat-run/corpus
printf 'latin1-name-\xE9.bin' | xargs -0 ls -b        # latin1-name-\351.bin   90384 bytes
vcat info "$(printf 'latin1-name-\xE9.bin')"
# error: Log source was not found.        exit 1
vcat info "$PWD/$(printf 'latin1-name-\xE9.bin')"
# error: Log source was not found.        exit 1
```

The same byte path resolves fine for every other tool, including Python
(`os.path.exists` → `True`, size 90,384). Every *UTF-8-valid* hostile name in the same
corpus works — spaces, `'`, `$`, a literal newline, emoji, CJK, and NFC/NFD spellings
all import 1,000 entries each — so this is specifically the **not-valid-UTF-8** case.

**Expected** (plan A-08, A-26, Appendix B #35) Linux paths are bytes; .NET represents
the invalid ones with surrogate escapes, and round-tripping them is a stated
requirement. At minimum the message must distinguish *"this path is not valid UTF-8 and
VisualCat cannot address it"* from *"there is no such file"*.

**Suggested fix.**

1. Find where the CLI turns the argument into a path. If the byte sequence is being
   normalized or re-encoded anywhere (a `string` round-trip through a non-surrogate
   encoding will destroy it), pass the OS bytes straight through — on .NET, arguments
   already arrive with `\uDCxx` surrogate escapes, and `File.Exists` honours them, so the
   loss is almost certainly a deliberate normalization step, not the runtime.
2. If a decision is made not to support such names, say so in the message and in
   `SUPPORT.md`, and make it a distinct error from "not found":
   `error: This path is not valid UTF-8; VisualCat cannot open it. (bytes: 6c 61 74 … e9 …)`.
3. Same check on the desktop side, for the tab name, the notice, the session manifest,
   the export file name, and the diagnostics record — the plan requires the byte-exact
   name in all six.

---

### F-07 · Minor · `vcat` rejects the POSIX `--` end-of-options separator

**Severity** Minor — but it means a file whose name begins with `-` (legal everywhere on
Linux) has **no safe spelling**, and every script written to the usual convention fails.

**Reproduction.**

```shell
vcat index -- corpus/small.txt --output /tmp/x.vcat
# error: 'index' does not take '--'. Run 'vcat index --help' to see what it does take.
# exit 2
```

The plan's own §2.3 instructs testers to "quote every path (`"$VCAT"`), use `--` before
filename arguments" — the first sweep in §2.8 was written that way and every one of its
33 invocations failed on the separator rather than on the file.

**Suggested fix.** Treat `--` the way every POSIX tool does: consume it in the argument
parser and treat everything after it as positional.

```csharp
// A bare "--" ends option parsing; everything after it is a positional argument.
// Without this a file named "-report.txt" cannot be passed at all.
if (argument == "--") { optionsEnded = true; continue; }
if (optionsEnded) { positional.Add(argument); continue; }
```

Then add the two cases to the CLI tests: `vcat index -- ./-report.txt`, and
`vcat index ./-report.txt` (which should also work, since the path is `./`-qualified).

---

### F-08 · Polish · The live `Import` button is drawn in the disabled style

**Severity** Polish, but it cost real time in this run: the button was read as disabled
and the review was treated as blocked.

**Observed.** In the *Import preview* dialog, with the preview fully evaluated and
`Import` genuinely enabled and clickable, `Import` is rendered in a noticeably lighter
grey than the adjacent `Cancel`. Clicking it works immediately. `ImportPreviewDialog`
does set `_import.IsEnabled = true` on the success path — the state is right, the paint
is misleading.

Contributing factor: the shell is simultaneously showing `Copying file…` ([F-04](#f-04)),
which reads as "work in progress, controls not ready yet". The two together make a
ready dialog look busy.

**Suggested fix.** Give the dialog's primary action the accent treatment the rest of the
product uses for a default action — the portal's own `Open` button in the same
screenshot is a filled accent button, and the contrast difference is obvious side by
side. Concretely: style `_import` as the dialog's default button (filled accent
background, foreground meeting the 4.5:1 floor in both themes), keep `Cancel` as the
secondary, and check the **disabled** variant is then visibly different from the enabled
one — today the enabled state looks like most products' disabled state. Worth sampling
against the §4.4 contrast floor while the change is being made, in light, dark, and
high contrast.

---

### F-09 · Major · A followed file withholds its newest records indefinitely, while the status says the source has gone quiet

**Severity** Major — the live view silently shows fewer records than the source holds, and the
product asserts the opposite. Nothing is lost, but for a log-tailing tool the newest lines are the
only ones anybody is watching.

**Where** The incremental read/publish path of *Follow file*. A static file is published in full;
this is the streaming path only.

**Reproduction.** Follow a file, append to it for a while, stop the writer, and compare the session
with the file:

```shell
cp corpus/quiet-live-seed.txt /tmp/growing4.txt        # 1 meta line + 1 record
# ... Follow file → /tmp/growing4.txt ...
for i in $(seq 1 120); do
  printf '05-15 14:20:00.000  1073  1151 I VCatGrow: RUN=B12C seq=%s\n' "$i" >> /tmp/growing4.txt
  sleep 0.3
done
# writer stops here; the file now holds 120 records
```

| Moment | `vcat query <session> \| grep -c RUN=B12C` | records in the file |
|---|---|---|
| t + 5 s | **107** | 120 |
| t + 20 s | **107** | 120 |
| t + 45 s | **107** | 120 |
| t + 75 s | **107** | 120 |
| after appending **one** more line | **121** | 121 |

Thirteen records sat unpublished for more than 75 seconds and were released all at once by a single
further append. `vcat index` of the same file recovers all 120 immediately, so the bytes were on
disk the whole time. Pressing *Stop capture* also flushes them: the first run of this test went from
297 to 300 of 300 the instant Stop was pressed, and finalized with `parsedEntries 302`, state
`Ready`.

**What makes it a correctness problem rather than a latency one** is the status line. Throughout
those 75 seconds the shell read:

> `Capturing · 303 lines received · no source lines for 1m 39s · /tmp/growing2.txt (follow)`

The heartbeat that **R-33** added — so that a quiet source stops claiming arrivals — is here telling
the reader that they are up to date with a source that has stopped, at the exact moment the product
is sitting on records it has already read. The two together are worse than either alone: the user
has a positive reason to believe the tail is genuinely absent.

**Suggested fix.** Two changes, one small and one that closes the contract.

1. **Publish the pending batch on an idle tick.** The follow loop already polls every 250 ms and
   already knows when a read returned nothing. Make "read returned nothing and a pending batch
   exists" a publication trigger rather than a no-op:
   ```csharp
   // A read that delivers nothing is exactly when the tail should be committed: the writer has
   // paused, so waiting for a fuller batch trades freshness for nothing. (F-09)
   if (delivered == 0 && pending.Count > 0)
   {
       await PublishAsync(pending, cancellationToken);
       pending.Clear();
   }
   ```
2. **Make the heartbeat tell the truth.** Do not print *“no source lines for …”* while a pending
   batch is unpublished. Either publish first (per 1) or say what is actually happening —
   *“no new source lines for 12s · 13 waiting”* — so the number on screen and the number in the file
   can never disagree silently.
3. Add a regression test at the level this reproduces: a follow over a temp file, append N records,
   stop, wait past two poll intervals, and assert `session.EntryCount == N`. That assertion fails
   today and is cheap to keep.

**Appendix-B trap checks.** Not a poll-interval artifact — the lag persisted 300× the 250 ms poll
(#25). Not `inotify` limits — there is no watch (#25). Not the producer: the ledger and an
independent `vcat index` of the same bytes both show all 120. Not truncation or rotation — the
inode, path and length were unchanged throughout (#24).

---

### F-10 · Major · Windows and dialogs render with unpainted and duplicated regions on XWayland

**Severity** Major — it hid the export review's own *Row order* and *Encoding* controls, and it
reproduced on 4 of 5 openings of the settings dialog. Any screenshot-based release evidence taken on
this platform is unreliable until it is fixed.

**Not a capture artifact.** Two independent capture paths agree: a hypervisor framebuffer grab shows
the workspace behind bleeding through the dialog, and `import -window <id>` — which reads the
window's own contents — returns the same regions as **black**, meaning the window really was never
painted there.

**Reproduction rate**, measured by opening *More → Appearance & timeline*, grabbing the window and
measuring the fraction of pure-black (unpainted) pixels:

| attempt | unpainted |
|---|---|
| 1 | **15.5 %** |
| 2 | **30.8 %** |
| 3 | **30.8 %** |
| 4 | **15.5 %** |
| 5 | 0.1 % (clean) |

It is not limited to that dialog:

* **Export CSV** — the band covered *Row order* and *Encoding* entirely. The dialog read "A
  successful export remembers these two choices as the new defaults" above two controls that were
  not on screen. Only a forced resize revealed them.
* **Temporary session cache** — a band across the explanatory paragraph.
* **The main window's empty state** — after `--log /nonexistent/nope.txt`, the hero block was drawn
  **twice**, offset, with the headline half-erased: two copies of *“Turn raw Android logcat into a
  navigable severity × time signal.”* and two rows of severity chips. 5 % of the window unpainted,
  and **a forced resize did not heal it** (6.7 % after).

**Environment** (this is the default Ubuntu desktop, not an exotic configuration): Ubuntu 22.04.5,
GNOME Shell 42.9 / Mutter 42.9, Wayland session with the app as an **XWayland** client, Mesa
`libGLX_mesa.so`, VMware virtual adapter, no `AVALONIA_*`/`LIBGL_*`/`MESA_*` overrides in the
process environment, animations enabled.

**Suggested fix.**

1. First establish whether this is XWayland-specific or general X11, because that decides whether it
   is a VisualCat bug or an Avalonia/Skia one to report upstream. Log in to an **“Ubuntu on Xorg”**
   session and repeat the five-open measurement above; then repeat once with
   `LIBGL_ALWAYS_SOFTWARE=1` and once on hardware GL on metal. The measurement is three lines and
   gives a yes/no:
   ```shell
   W=$(xdotool search --class VisualCat | tail -1)
   import -window "$W" -silent /tmp/w.png
   convert /tmp/w.png -colorspace gray -threshold 2% -negate -format '%[fx:mean*100]' info:
   ```
2. The shape of the damage — a horizontal band, and a second copy of content drawn at an offset —
   points at the damage/invalidation rectangles handed to the X11 backend rather than at the drawing
   code: the content is being composed correctly and then presented with the wrong region. Check
   whether the window is being presented with a stale or partial damage region after a layout pass
   that changes the content height (both the settings and export dialogs size themselves to their
   content, and the empty state re-lays out when *Recent captures* arrives asynchronously).
3. Until it is fixed, a cheap mitigation that costs one frame: force a full invalidation when a
   top-level's content size changes, instead of an incremental one.
4. Add this to the release evidence procedure regardless of cause: every screenshot taken for a
   release gate on Linux should be checked with the one-liner above, and a capture with a non-zero
   unpainted fraction must be retaken, not filed.

**Appendix-B trap checks.** Not the screen blanking (#3) — `idle-delay` was 0 for the whole run and
the rest of the frame is drawn correctly. Not a compositor-screenshot limitation (#48) — both
capture paths agree, including the one that reads the window's own contents. Not a theme or font
issue (#14) — the same fonts render correctly in the unaffected regions of the same frame.

---

### F-11 · Major · A modal dialog is not modal to assistive technology

**Severity** Major — the plan makes this a named release exit criterion (**R-41**, exit criterion 8),
and the practical consequence is that a screen-reader user can drive controls that the pointer
cannot reach.

**Observed**, with *Appearance & timeline* open and modal to the pointer:

```
[application] 'Avalonia Application' {}
  [frame] 'VisualCat v2 — See the shape of your log' {ENABLED,SENSITIVE,SHOWING,ACTIVE,VISIBLE}
  [frame] 'Appearance & timeline'                    {ENABLED,SENSITIVE,SHOWING,ACTIVE,VISIBLE}
  nodes still reachable under the main window while the dialog is open: 434
```

Three separate problems in one tree:

1. The dialog is exposed with role **`frame`**, not `dialog`. Orca announces it as another window.
2. It carries **no `MODAL` state**, so assistive technology has no way to know it is modal.
3. Both frames are **`ACTIVE` at once**, and all **434** nodes of the workspace behind the dialog
   remain `SHOWING` and reachable. AT can walk straight past the scrim into the workspace.

The same shape appears for *Lines not on the timeline* and *Export CSV*.

**Suggested fix.** Avalonia exposes this through the window's automation peer, so the fix is at the
point where these dialogs are shown:

1. Show owned dialogs so the backend maps them as `dialog` with `STATE_MODAL` — set the window's
   `WindowStartupLocation`/owner relationship and, on the automation side, ensure the peer reports
   `AutomationControlType.Window` with the modal flag rather than a bare top-level frame.
2. While a modal dialog is open, mark the owner window's automation subtree as **not** currently
   reachable — `AutomationProperties.SetAccessibilityView(owner, AccessibilityView.Raw)` or the
   equivalent for the AT-SPI backend — so the 434 nodes behind the scrim stop being offered.
3. Add an automated guard, because this one is genuinely testable without a human: with a dialog
   open, assert that the AT-SPI tree exposes exactly one `ACTIVE` top-level and that its role is
   `dialog`. That is a dozen lines against `pyatspi` and it pins both halves.
4. While in there, fix the application name — see [F-13](#f-13); an AT user currently sees
   *“Avalonia Application”* in the application list.

**Verification note.** This was measured with
`org.gnome.desktop.interface toolkit-accessibility` set to **true** and the application restarted
afterwards, so it is not an artifact of the bridge being off. (It is off by default on Ubuntu 22.04,
and the setting was restored afterwards — see §4.)

---

### F-12 · Minor · Escape does not dismiss the settings dialog

**Severity** Minor — `Cancel` is reachable by Tab and the window manager's close affordance works,
so nothing is trapped; but plan **U-19** requires Escape, and a keyboard-only user reaches for it
first.

**Observed.** With *Appearance & timeline* focused (`xdotool getactivewindow getwindowname` →
`Appearance & timeline`), Escape was sent both to the window explicitly and to the focused window.
The dialog stayed open both times. `xdotool windowclose` (graceful `WM_DELETE_WINDOW`) closed it
immediately.

`KEYBOARD.md` documents Escape only for the workspace — *“Close mobile filters, clear focused
search, clear a selected timeline scope, or clear filters (in that order); ignored when there is
nothing to dismiss”* — and for *Recent captures*. So the dialogs were simply never given an Escape
handler; the documentation is silent rather than wrong.

**Suggested fix.** Give the shared dialog base a cancel key handler and document it, so every sheet
behaves the same way:

```csharp
// Escape is the conventional cancel on every desktop; the WM close affordance is the only
// other route today. (U-19 / F-12)
protected override void OnKeyDown(KeyEventArgs e)
{
    if (e.Key == Key.Escape && !e.Handled) { Cancel(); e.Handled = true; }
    else base.OnKeyDown(e);
}
```

Then add the row to `KEYBOARD.md`: *Escape — cancel the open dialog or sheet*. Take care that it
cancels rather than applies, and that a dialog with unsaved edits and a running preview (the import
review) treats Escape as its `Cancel` button, not as a dismissal that loses the answer silently.

---

### F-13 · Polish · The accessibility bus shows “Avalonia Application”, and some accessible names are control type names

**Severity** Polish, but it is the first thing a screen-reader user hears about this product.

**Observed.** Enumerating the AT-SPI desktop lists, beside `gnome-terminal-server`,
`xdg-desktop-portal-gnome` and the rest, an application called **`Avalonia Application`**. That is
VisualCat. Within the tree most names are good, but a few are the control's type:

| Node | Accessible name | Should be |
|---|---|---|
| search field | `TextBox` | `Search message text or regex` |
| plot/details splitter | `GridSplitter` | `Resize the plot and the entry list` |
| several static labels | `TextBlock` | their text, or no name at all |
| structural nodes | `WindowChrome`, `VisualLayerManager`, `ContentPresenter`, `MainView`, `ModalWorkspaceBand`, `UnparsedLinesDialog` | unnamed (`panel` role carries no useful name) |

The structural ones are `panel`/`unknown` roles that Orca mostly skips, so they are cosmetic; the
search field is not — it is the first control in the documented focus order, and it announces itself
as "TextBox".

**Suggested fix.**

1. Set the application's accessible name once, at startup, next to where the window title is set —
   `Application.Current.Name = "VisualCat"` / the AT-SPI backend's application name — so the bus
   shows the product.
2. Give the search field an `AutomationProperties.Name` equal to its placeholder
   (*Search message text or regex*), and the splitter one describing what it resizes. The rest of
   the tree already does this well (`Fatal level`, `Fit the complete session`,
   `Close session small.txt`), so this is filling three gaps rather than starting work.
3. For the structural nodes, prefer clearing the name over renaming it: an unnamed `panel` is
   correct and silent, whereas `VisualLayerManager` is implementation detail read aloud — which is
   what **R-40** exists to prevent.

---

### F-14 · Polish · Phone-only settings and phone vocabulary appear in the desktop settings dialog

**Severity** Polish.

**Observed** in *Appearance & timeline* on the Linux desktop:

| Text | Problem |
|---|---|
| *Text scale* → “Multiplies **the device's** own text size setting.” | On a desktop the referent is the desktop's text-scaling factor, not "the device" |
| *Snap timeline cells to **device** pixels* | same word, same reason |
| ***Phone plot and details split*** — disabled, placeholder “Automatic sizing is active” | A phone-only setting, present and inert on a desktop |
| its help text: “In Split mode, drag the grip between the plot and the tabs to resize them — downwards in portrait, sideways in landscape.” | Portrait/landscape and Split mode are phone concepts |
| *Normalized CSV encoding* | “Normalized” is unexplained; the control chooses the file's encoding. The value is also clipped to `UTF-8 with byte-orc` in its combo at the default width |

Plan **B-03** asks that no Android-only control appear, and **R-16** asks for human language rather
than implementation vocabulary; this is the same idea one level up — platform vocabulary from the
other platform.

**Suggested fix.** Hide *Phone plot and details split* where `OperatingSystem.IsAndroid()` is false,
the same way *Check for updates…* is gated today (and see [F-03](#f-03) for the other half of that
pattern). Replace "the device" with "this computer" on desktop builds, or with neutral wording that
works on both — *“Multiplies the system text size setting.”* Rename *Normalized CSV encoding* to
*CSV encoding* and widen the combo so its longest value fits.

---

### F-15 · Minor · *Lines not on the timeline* never says how much it has listed, and *Load 500 more* loads far fewer than 500

**Severity** Minor — the population is reachable, but the reader cannot tell when they have seen all
of it, and the control's label is wrong about what it does.

**Observed** over `crashy-large.txt`, whose off-timeline population the same dialog's own header
correctly states as `117 unknown · 2 continuation · 1 rejected · 1 untimed` — **121 lines**:

| Step | Footer |
|---|---|
| on open | `19 shown · more of the file remains to be scanned.` |
| after one *Load 500 more* | `45 shown · more of the file remains to be scanned.` |
| after further presses | still `45 shown · more of the file remains to be scanned.` |

So the button labelled *Load 500 more* added **26** rows, then **0**. It is scanning a further slice
of the *source file* rather than loading more *rows*, which on a 1,000,010-line file means roughly
seven presses to reach 121 rows — and the footer never once relates what is shown to the 121 the
header already knows.

Plan **R-58** is explicit that a bulk row load "names the ceiling and the remaining rows"; **R-07**
requires only that it stop over-claiming once everything is listed, which it does, so this is the
softer half of the same idea.

**Suggested fix.**

1. Make the footer name both numbers, from the count the header already has:
   `45 of 121 shown · scanned to line 397,673 of 1,000,010`.
2. Rename the button to what it does — *Scan further* — or make it do what it says by continuing to
   scan until it has *added* 500 rows or reached the end, whichever comes first. The second is the
   better user experience and is what "Load 500 more" means everywhere else in the product.
3. When `shown == total`, drop the button and say so: `121 of 121 shown · the whole file has been
   scanned.` That also gives **R-07** a positive assertion instead of an absence.

---

### F-16 · Polish · The export review states its range in UTC while the workspace is showing Europe/Prague

**Severity** Polish — the review does label its zone, so nothing is ambiguous on close reading; but
the two surfaces present the same instants in two different zones with no hint that they match.

**Observed.** With the workspace plot axis labelled `Europe/Prague` and its header reading
`00:06:09.000 … 00:06:39.000`, *Export* opened a review that read:

```
Visible plot range — 3,518 timed rows
22:06:09.464 to 22:06:39.464 (end excluded)
3,518 timed rows · UTC · Filters: time
```

Same instants, two spellings, two hours apart. The session's stored `timeZoneId` is `UTC` (the ADB
capture negotiated `threadtime,year,UTC,usec`) while the plot presents in the host zone, so both are
internally consistent — but a reader checking the export against what they can see on the plot has
to do the arithmetic to discover that they agree.

**Suggested fix.** State the range in the zone the workspace is presenting, and name it, exactly as
the plot header does — `00:06:09.464 to 00:06:39.464 · Europe/Prague`. If there is a reason to
publish UTC in the review, show both: `00:06:09.464 – 00:06:39.464 Europe/Prague (22:06:09.464 –
22:06:39.464 UTC)`. The CSV's own `timestamp_utc` column is unaffected either way — it carries a
full offset per row and is correct as it stands.

---

### F-17 · Polish · Stale `dotnet-diagnostic-*-socket` files accumulate in `/tmp`

**Severity** Polish — 0-byte unix sockets, mode `600`, no content; they simply never go away.

**Observed** after a session of repeated launches:

```shell
ls /tmp/dotnet-diagnostic-*-socket | wc -l     # 15
# of those, sockets whose PID is no longer alive: 14
```

One is created per process by the .NET diagnostics IPC server and is not unlinked when the process
exits. Plan **P-18** asks a run to account for exactly what remains after removal, and these are the
only files that remain outside the declared data root.

**Suggested fix.** Either unlink the socket on a clean shutdown, or — simpler and more robust,
because it also covers `SIGKILL` — set `DOTNET_DiagnosticPorts` off for the shipped desktop build
unless diagnostics are explicitly wanted, since the product does not use the diagnostics IPC channel
itself. Whichever is chosen, add the file to the residue list in `PRIVACY.md`/`SUPPORT.md` so a user
auditing what VisualCat leaves behind finds it documented rather than surprising.

---

## 4. Cleanup and host hand-back (plan §13.5)

Executed in full. Every ledger row is closed.

| Step | Action | Verified |
|---|---|---|
| 1 | Stop every `VisualCat`, `vcat`, producer and marker process | `pgrep -x VisualCat` = 0, `pgrep -x vcat` = 0, `pgrep -a adb` = none in the guest. `pkill -x` was used throughout, never a `pkill -f` pattern that could match its own shell (Appendix B #4) |
| 2 | Restore `idle-delay` | back to `uint32 300` |
| 3 | Restore `idle-activation-enabled` | back to `true` |
| 4 | Restore `toolkit-accessibility` | back to `false` (its pre-run value) |
| 5 | Restore the account's real VisualCat data | `~/.local/share/VisualCat` is the original directory again, **4 sessions** present, `settings.json` intact. The run's own data root was removed only after resolving it with `readlink -f` and checking the prefix |
| 6 | Delete generated corpora | `large.txt`, `crashy-large.txt`, `medium.txt`, `notalog.bin`, `truncated.txt` removed, each resolved with `readlink -f` and confirmed inside `~/vcat-run/corpus` first |
| 7 | Delete run scratch | `/tmp/out`, `/tmp/adv`, `/tmp/growing*.txt`, the `/tmp/*.vcat` indexes, and the 15 stale `dotnet-diagnostic-*-socket` files ([F-17](#f-17)) |
| 8 | Restore the host's ADB server | `adb kill-server` + `adb start-server`; it listens on **127.0.0.1:5037** again, not `0.0.0.0` |
| 9 | Remove the firewall rule | `VCAT-ADB-5037` deleted; confirmed absent |
| 10 | Hand the phone back | screen off and locked (`screenState=SCREEN_STATE_OFF`), still `device` and authorized, no traffic generator left running. Its buffers, debugging and authorization state were never modified — only `log -t VCATTEST` messages were written, which age out of the ring |
| 11 | Host health re-check | Wayland session active, GNOME Shell running, display 1737×1279, 163 GiB free, 5.7 GiB memory available |
| 12 | Failure evidence for the run window | **0** journal errors matching `visualcat|segfault|oom`, **0** core dumps, `/var/crash` empty |

**Deliberately retained**, because a continuation run needs them:

| Path | Size | Why |
|---|---|---|
| `~/vcat-run/candidate/{rel-2.0.13,head}` | 369 MB | the exact hashed artifacts under test |
| `~/vcat-run/download` | 156 MB | the release tarballs and `SHA256SUMS` |
| `~/vcat-run/corpus` | ~10 MB after step 6 | the small deterministic and adversarial corpora plus `SHA256SUMS.corpus` |
| `~/vcat-run/evidence/20260911-linux-ubuntu2204` | 124 KB | per-scenario evidence |
| `~/vcat-run/ledger/mutation-ledger.tsv` | 8 KB | the closed ledger |
| `~/vcat-run/env.sh`, `shot.sh` | — | the resume helpers §0 uses |

Deleting `~/vcat-run` removes all of it and nothing else; it touches no VisualCat user data.

---

## 5. Assessment

### 5.1 What this run says about the candidate

**The engine is in good shape.** Every integrity oracle this run could construct agreed with the
product: 1,000,000 lines accounted for exactly, 100,000 exactly, 1,011 exactly, byte offsets
resolving to the right bytes, `vcat verify` clean on every session produced, 600 of 600 ADB markers
delivered with zero declared drops, buffer attribution exact to the record, and a desktop CSV export
that is **byte-identical** to the CLI's. Privacy is not merely acceptable but exemplary: **zero
sockets** across a full working session, and every byte written inside the declared XDG locations.

The second pass strengthens this considerably. The **security boundaries hold**, including the ones
the plan treats as Blockers by default: diagnostic redaction is exact (§7.3), untrusted content
cannot reach a terminal unescaped (§7.4), a hostile archive cannot escape the session root or create
a link, device node or setuid file (§7.5), a symlinked lease root is refused (§7.7), permission
denials are specific and never prompt for `sudo` (§7.8), publication is atomic through an
unguessable temporary name (§7.9), and bundled libraries cannot be shadowed from the working
directory or `LD_LIBRARY_PATH` (§7.10).

**What is weak is the layer above it** — what the product *says* about what it has, how reliably it
draws it, and what it does when something interrupts it:

* [F-01](#f-01) loses real records from a documented format and reports the loss nowhere.
* [F-09](#f-09) shows fewer records than it holds and states the opposite.
* [F-10](#f-10) fails to draw parts of its own windows, including controls a user needs.
* [F-18](#f-18) turns a `SIGTERM` into an unverifiable session where `SIGINT` is clean.
* [F-19](#f-19) refuses to run without a pre-existing data root, and blames a deleted session.
* [F-20](#f-20) corrupts a session under contention while exiting 0.
* [F-31](#f-31) keeps reading a rotated-away inode while naming the live path.
* §2.11 reproduces the published 2.0.13 drawing a *prefix* of a large log on 3 of 5 launches.

Almost all of those are the same shape: **the product's account of itself diverges from what is
actually on disk, and the account is what the reader trusts.** That is a remarkably coherent theme
for defects found independently across two passes, and it suggests the highest-value work is not
scattered bug-fixing but a pass over every place the product states a count, a stage, a freshness
claim or a cause, asking what it says while the underlying work is in flight or has failed.

A second, narrower theme joins it in the second pass: **the error messages are excellent where
someone wrote them for the specific case, and misleading where a generic one got reused.** Compare
`error: Access to the path '…/ro/VisualCat' is denied. cause: Permission denied` with
`error: Session lease storage is unavailable.` — the same code path, two very different levels of
help. [F-19](#f-19) is that gap at its worst.

**Release view.** The `[Unreleased]` work is confirmed good and should ship: §2.10 and §2.11 are
direct A/B evidence that it fixes real, reproducible, user-visible defects in the published build.
Measured against the plan's exit criteria, this slice would **not** pass a release gate — criterion 3
(no open Major) fails on seven findings, criterion 8 (accessibility) fails on [F-11](#f-11), and
[F-21](#f-21) needs an explicit decision because the plan classes P-04 failures as Blockers by
default even though the actual boundary held.

### 5.2 Coverage this run did **not** establish

Named explicitly so none of it is mistaken for green (plan §13.4 criterion 2):

Amended after the second and third passes — rows they closed are struck through with a pointer.
**§10.10 is the current, consolidated list;** this table is kept because it records why each cell was
out of reach in the first place.

| Cell | Why | What it would take |
|---|---|---|
| **A second distribution, as a full run** | Only Ubuntu 22.04 was available | §10.2 established the glibc floor at **2.28** by running the CLI on Debian 10, 11 and 12 — but only `--version`. A full B-tier run with a desktop session on Debian or Fedora is still untested |
| ~~A real X11 session (G0)~~ | — | **Closed in §7.17.** Xorg session exercised; [F-10](#f-10) is XWayland-specific, [F-11](#f-11) and [F-12](#f-12) are not |
| **Metal, and a real GPU** | VMware guest, virtual adapter, `glxinfo` absent | Any physical Linux host. Every timing here is G5-class and **no §4.2 performance budget is claimed** |
| ~~Provenance attestation~~ | — | **Closed in §7.2.** Both tarballs verify; P-13 PASS |
| **ADB `no permissions` / `unauthorized` / `offline`, `udev`, group membership** | The guest is a client of the host's ADB server (§1.5) | A device attached directly to the Linux host. A-16 is a gap, not a pass |
| **Orca end-to-end (U-07)** | The AT-SPI tree was inspected, but nobody listened | Orca 42.0 is installed; needs a human through the B-19 journey |
| **KDE / Xfce / a tiling WM (G4)** | GNOME only | U-05, U-26 |
| **Scaling matrix (U-02), multi-monitor (U-03, U-04)** | Single 1× output | A second output and 125/150/175/200 % |
| ~~`umask 077` (P-22 second leg)~~ | — | **Closed in §10.7.** Session and lease files are `700`/`600` under `umask 077`; 0 world-writable, 0 setuid |
| **A correct ACL denial (P-09)** | Retried with a second account in §10.8; a plain `cat` control showed the kernel was not enforcing the ACL against this user either, so the row still proves nothing | An ACL that `cat` demonstrably honours |
| **Quota, read-only remount, low disk (L3, X-15)** | Not executed | A dedicated loop-mounted filesystem |
| **Soak (X-03, X-05–X-09, X-20–X-23, X-28)** | Needs a dedicated host and 30–50 h | Dedicated hardware |
| **A-29 upgrade from the previous release** | The D3 profile was not built | The 2.0.12 artifacts plus genuine on-disk data |
| **Portable archive round trip (B-14, I-08)** | A local `portable-zip` export and reopen were exercised in §7.5; the **cross-platform** hops were not | Windows and Android exchange partners |
| **A-21 growing-source truncation and rotation** | Attempted in §10; the GUI automation could not be made reliable after the display-server switches. The plan names a known detection hole here that pairs with [F-09](#f-09) | `truncate`, `mv`+new file, `logrotate --force`, `rm`, and the two cases the length comparison cannot see. **The highest-value untested row** |
| **Lone-CR offsets** | The input is refused (§2.17 item 3) | n/a — the plan expectation needs softening first |

### 5.3 Suggested order of work

1. **[F-01](#f-01)** — a one-line parsing fix, a golden fixture, and a detection-confidence
   assertion. Silent data loss on real device output, and the cheapest of the Majors.
2. **[F-19](#f-19)** — create the data root instead of failing on the lease directory, and stop
   reporting it as a deleted session. One `Directory.CreateDirectory` plus two messages, and it
   removes a case where the product simply will not start.
3. **[F-18](#f-18)** — route `SIGTERM` to the cancellation path `SIGINT` already uses. Small, and it
   is the signal a `systemd` stop or a logout actually sends.
4. **[F-20](#f-20)** — take the write lease before `--force` deletes anything, and fail non-zero if
   the published session does not verify. Silent corruption with a success exit code.
5. **[F-09](#f-09)** and **[F-31](#f-31)** together — both live in the follow loop, and the second is
   the more serious: publish the pending batch on an idle read, stop printing a quiet-source
   heartbeat while a batch is pending, and compare the source by **inode** rather than by length so
   an ordinary `logrotate` cannot leave the follow reading a file nobody writes to.
6. **[F-11](#f-11)** — the modal-boundary fix plus the `pyatspi` guard; a rare accessibility defect
   that an automated test can pin. Confirmed not platform-specific (§7.17).
7. **[F-10](#f-10)** — now known to be XWayland-only (§7.17), so the next step is an upstream report
   against Avalonia's X11 backend under XWayland, with the five-open measurement as the repro. The
   unpainted-pixel check belongs in the release-evidence process regardless, because it invalidates
   screenshots taken on the default Ubuntu desktop.
8. **[F-21](#f-21)** — extract only members the session format defines. One guard closes the bomb,
   the deep path and the long name together, and the plan classes this row as a Blocker by default.
9. Ship the `[Unreleased]` work (§2.10, §2.11) — verified good against the shipped binary.
10. The remaining Minors, then the Polish items opportunistically when their strings, dialogs or
    documentation are next touched. [F-23](#f-23) is worth doing with the next release notes, since
    it is the only place the strongest origin guarantee is offered to users.

Three items from the third pass slot in alongside those: **[F-26](#f-26)** (a missing zone database
must be reported, not guessed) belongs with the parsing work in item 1, since both are about
timestamps being silently wrong; **[F-27](#f-27)** (a session on a shared path is world-readable) is
a one-line file-mode decision worth taking deliberately rather than by default; and
**[F-28](#f-28)** (`verify` cannot distinguish *verified* from *unverifiable*) is a small contract
change to `CLI.md` and one extra field.

One documentation item is now free: §10.2 established that the release runs on **glibc 2.28**, so
`SUPPORT.md` can state a floor where it currently states none.

The fourth pass adds one more to the top half of that list: **[F-29](#f-29)** — the CLI writing the
host's native newline — is small to fix and it is the only thing standing between this candidate and
**release exit criterion 4**. Everything else criterion 4 asks for already passes byte-exactly
(§13.4).

---

## 6. Related documents

- [`LINUX-LIVE-TEST-PLAN.md`](LINUX-LIVE-TEST-PLAN.md) — the plan this run executed
- [`WINDOWS-LIVE-TEST-REPORT.md`](WINDOWS-LIVE-TEST-REPORT.md) — the Windows ADB slice, and the
  cross-platform partner for §2.13
- [`ANDROID-LIVE-TEST-REPORT.md`](ANDROID-LIVE-TEST-REPORT.md),
  [`ANDROID-LIVE-TEST-REPORT-V2.md`](ANDROID-LIVE-TEST-REPORT-V2.md) — the companion-device runs
- [`CHANGELOG.md`](../CHANGELOG.md) — the `[Unreleased]` entries §2.10 and §2.11 verify
- [`SUPPORT.md`](SUPPORT.md) — the update statement [F-03](#f-03) contradicts
- [`CLI.md`](CLI.md) — the exit-code and NDJSON contracts checked in §2.9
- [`KEYBOARD.md`](KEYBOARD.md) — the Escape contract [F-12](#f-12) extends
- [`SECURITY.md`](SECURITY.md) and [`PRIVACY.md`](PRIVACY.md) — the boundaries §7 exercised, and where
  [F-17](#f-17), [F-24](#f-24) and the portal recent-files note in §2.16 belong
- [`adr/0021-csv-export-fidelity.md`](adr/0021-csv-export-fidelity.md) — the byte-faithful CSV
  contract that makes §7.4's raw control bytes correct rather than a defect

---

## 7. Second pass — Tier P security rows, provenance, and the G0 decider

The first pass (§1–§6) left the Blocker-class **Tier P** rows and several cheap A/I rows unexecuted;
§5.2 named them. This pass closes most of them. Same candidate, same host, same run ID. The
security rows ran against an **isolated data root** (`XDG_DATA_HOME=~/vcat-run/data-p`) so that none
of them could touch the account's real VisualCat data.

### 7.1 Findings added by this pass

| ID | Severity | One line |
|---|---|---|
| [F-18](#f-18) | **Major** | `SIGTERM` does not behave as `SIGINT`: it leaves an `Importing` session that fails `vcat verify`, where `SIGINT` leaves a clean recoverable one — 3/3 each way |
| [F-19](#f-19) | **Major** | A data root that does not yet exist is never created: the CLI says `Session lease storage is unavailable.` and the desktop blames a *deleted session* |
| [F-20](#f-20) | **Major** | Two processes writing one session: the loser is refused correctly, but the **winner exits 0 and leaves a session that fails verification** |
| [F-21](#f-21) | Minor | The portable-archive extractor has no expansion bound — a 1.1 MB archive wrote a 1 GiB file into the data root |
| [F-22](#f-22) | Minor | The shell's *Cancel* on a file-chooser operation never completes: it sits on `Cancelling…`, leaves the chooser open, and disables every file command |
| [F-23](#f-23) | Polish | `gh attestation verify` — the command `README.txt` tells users to run — prints nothing and exits 0 |
| [F-24](#f-24) | Polish | Lease files are never removed: 9 zero-byte files after every process exited cleanly, 127 in a working root |
| [F-25](#f-25) | Polish | One corrupted session file produces ~380 identical issue objects in the `verify` report |

### 7.2 P-13 / B-01 provenance — now **PASS**

§1.2 recorded the attestation as unclaimed because `gh` was absent from the guest. Run from the
Windows host against the same downloaded release bytes, it verifies, and the chain is complete:

```
sourceRepositoryURI    https://github.com/benny-cz/VisualCat
buildSignerURI         .../.github/workflows/release.yml@refs/tags/v2.0.13
runnerEnvironment      github-hosted
sourceRepositoryDigest 06709815674a2ea19dfdb08cd1c552836378c27b
```

That last value is exactly the commit the binary reports (`2.0.13+0670981…`), so bytes → attestation
→ workflow → tag → commit → the version string on screen is one unbroken chain. Both the desktop and
the CLI tarball verify. **P-13 PASS**, and B-01's provenance half is now claimed.

The only wrinkle is [F-23](#f-23): the verification is silent, so a tester has to ask for
`--format json` to see that it happened at all.

### 7.3 P-03 · Diagnostic redaction — **PASS** (Blocker-class row)

A synthetic secret `VCAT_SECRET_20260912` was planted in six places before the bundle was built: the
log **message** (as `password=` and `ghp_`), the **tag**, the **directory name**, the **file name**,
the **search string**, and a real device serial `RFCRC0A9GND` in another message. The confirmation
dialog promised:

> "The bundle excludes raw log messages, source paths, hashes, searches, and device serials. It
> still contains timings, counts, system details, and sanitized session metadata. Review it before
> sharing."

Checked three independent ways — `zipgrep` per member, `strings` over the raw archive bytes, and a
full extraction followed by a recursive `grep`:

| Pattern | Occurrences |
|---|---|
| `VCAT_SECRET_20260912` | **0** |
| `RFCRC0A9GND` | **0** |
| `ghp_`, `password=`, `SecretTag` | **0** |
| `/home/benny`, `vcat-run/sec` | **0** |

The bundle holds four members totalling 7,388 bytes: `SENSITIVE-DATA-WARNING.txt` (whose text
matches the dialog), `system.json` (OS, framework, architecture, processor count, timestamp — **no
host name, no user name**), one diagnostics log and one session document. In those, every
sensitive field is literally `<redacted>`:

```json
"displayName": "<redacted>",  "sourceDescription": "<redacted>",
"Properties": { "exceptionType": "System.IO.InvalidDataException", "message": "<redacted>" }
```

The promise and the contents match exactly. This is the cleanest result in either pass.

### 7.4 P-06 · Untrusted content in a terminal — **PASS** (Blocker-class, Linux-weighted)

`controls.txt` carries ANSI CSI colour, an **OSC window-title set** (`ESC ] 0 ; VCAT-TITLE-PWN BEL`),
a screen-clear (`ESC [ 2J ESC [ H`), a **device-attributes query** (`ESC [ c`, the one that gets
injected back as keyboard input), a NUL, bidi overrides and a zero-width space.

Every command that can print to a terminal escapes them:

| Command | raw ESC | raw BEL | raw NUL |
|---|---|---|---|
| `vcat query` | **0** | **0** | **0** |
| `vcat search` | **0** | **0** | **0** |
| `vcat info` | **0** | **0** | **0** |
| `vcat stats` | **0** | **0** | **0** |

The message arrives as JSON string escapes:

```json
"message":"\u001B[31mred\u001B[0m \u001B]0;VCAT-TITLE-PWN\u0007 \u202Eoverride\u202C zero\u200Bwidth"
```

The CSV export *does* contain the raw bytes (6 ESC, 1 BEL, 1 NUL) — which is correct and documented:
ADR 0021 makes CSV byte-faithful. Crucially there is **no CLI path that streams CSV to a terminal**:
`vcat export` has no `--stdout`, and exporting to `/dev/stdout` fails cleanly because the writer
publishes through a `.tmp-<guid>` sibling (see §7.7). So untrusted log content cannot reach a
terminal unescaped by any route this candidate offers.

### 7.5 P-04 · Portable archive safety — **the boundary holds, the resource bound does not**

Six hostile archives were built from a legitimate `portable-zip` export and opened through the
desktop's *Open archive*.

**Refused, each with a specific named reason:**

| Archive | Verbatim notice |
|---|---|
| `../`, `/abs`, `C:\` entries | `Could not open the portable archive · Portable archive entry escapes the session root: ../../../../tmp/VCAT-TRAVERSAL-ESCAPED.` |
| symlink entries → `/etc/passwd`, `/etc/shadow` | `Could not open the portable archive · Portable archives cannot contain symbolic links…` |
| corrupt central directory | `Could not open the portable archive · End of Central Directory record could not be found…` |

**Nothing escaped, under any of them.** A full filesystem audit after the pass found: no escape
marker anywhere, no file created outside the session root, **no FIFO, no device node, no symlink, no
setuid/setgid file and no world-writable file** — the extractor ignores the archive's type and mode
bits entirely, which is the safe choice. `/etc/passwd` was byte-identical afterwards.

**Accepted, and this is [F-21](#f-21):** the expansion bomb. `archive-bomb.vcat.zip` is 1.1 MB on
disk and declares a 1 GiB member; it opened successfully and wrote
**`bomb.bin`, exactly 1,073,741,824 bytes**, into the session directory, which then occupied 1.1 GiB
of the product's own data root. A 200-deep path and a 240-character name were also extracted
verbatim. The root cause is the same in all three: the extractor writes **every member**, including
ones the session format does not define.

### 7.6 P-07 · Parser and verifier resource bounds — **PASS**

First, a harness correction worth recording: **`ulimit -v 2000000` (2 GB address space) kills .NET
itself**, not the parser —

```
Fatal error. Failed to create RW mapping for RX memory.
```

— and a trivial `vcat index` dies the same way. The floor is between 2 and 4 GB of *address space*;
at `ulimit -v 4000000` everything below runs. Measured there, with `ulimit -t 120`:

| Input | Result |
|---|---|
| 2 MiB single-line corpus | indexes, exit 0 |
| the same session | verifies, exit 0 |
| `(a+)+$` over `pathological-regex.txt` with `--timeout-ms 250` | **0.33 s**, exit 0, 0 matches — bounded, no spin |

Nothing recursed until it faulted, nothing allocated without limit.

### 7.7 P-08 / A-14 / R-66 · Link and root boundaries — **PASS**

| Case | Result |
|---|---|
| source is a symlink to a log | read correctly, 1,000 entries ✓ |
| source is a hard link | read correctly, 1,000 entries ✓ |
| source is a dangling symlink | `error: Could not find file '…dangling.txt'.` ✓ |
| output into a symlinked directory | written to the real directory (inode confirmed), not duplicated ✓ |
| **output is a symlink pointing at `/tmp`** | `error: The file '…evil-out.vcat' already exists.` — **did not write through the link**; the target was never created ✓ |
| **a segment file replaced by a symlink to `/etc/passwd`** | `verify` exit **3**, `isValid: false`, `source.index` errors; `/etc/passwd` unchanged at 3,026 bytes ✓ |
| **`SessionAccess-v1` is a symlink pointing outside** | `error: Session lease storage is unavailable.`, exit 1, **0 files created in the escape target** — **R-66 PASS** |
| `Sessions` is a symlink pointing outside | index succeeded; 0 files in the escape target |

The lease refusal is a product sentence but does not name the cause; "the lease directory is a
symbolic link, which VisualCat will not follow" would be actionable. Minor wording, folded into
[F-19](#f-19) since the same message is reused there for a different cause.

### 7.8 P-09 / P-22 · Permission boundary and file modes — **PASS**

As an ordinary user, every denial is specific and **no `sudo` prompt ever appeared**:

| Attempt | Message |
|---|---|
| `chmod 000` source | `error: Access to the path '…/denied-src.txt' is denied. cause: Permission denied` |
| read-only destination (`chmod 500`) | same shape; **nothing was created in the destination** |
| write into `/root` | `error: Access to the path '/root/f.vcat' is denied. cause: Permission denied` |
| read another user's home | `error: Log source was not found.` — no path disclosure beyond what was supplied (**P-20**) |
| directory without execute permission | `error: Log source was not found.` — honest, though a permission problem is reported as a missing file |

*Not claimed:* the ACL leg. `setfacl -m u:benny:--- file` on a file **owned by benny** is a no-op
under POSIX ACL precedence, so the index succeeding proves nothing. The test was wrong, not the
product; a correct ACL test needs a second account.

`umask 077` could not be measured separately because the failure in [F-19](#f-19) masked it — every
umask from 022 to 077 failed identically for the same unrelated reason. The `umask 002` leg passed in
§2.13. **P-22's second leg remains a coverage gap.**

### 7.9 P-10 · Temporary files and atomic publication — **PASS**

`strace` over a CSV export shows the whole contract in one line:

```
rename("/…/out.csv.tmp-e08f6a6afb9a4d6690e10d94a9f881a6", "/…/out.csv") = 0
```

Written to a temporary sibling **in the destination directory** (so the rename is same-filesystem and
atomic), then renamed into place. No temp file in `$TMPDIR` at all, and **no residue** beside the
destination afterwards. Pre-creating a symlink at a guessed temp name does not work and cannot: the
name carries a fresh 128-bit GUID, which is the correct defence. The planted symlink target was never
created.

### 7.10 P-12 · Dynamic loader and executable-directory integrity — **PASS**

| Probe | Result |
|---|---|
| hostile `LD_PRELOAD` with an inert, hash-recorded probe library | **honoured** — expected for any dynamically linked program; recorded, not filed |
| a same-named `libSkiaSharp.so` planted in the **current working directory** | **not** loaded; the product ran normally ✓ |
| `LD_LIBRARY_PATH` pointing at a directory holding a fake `libSkiaSharp.so` | **not** loaded; ran normally ✓ |
| run from a `noexec` loop mount | `Permission denied` from the kernel — refused outright, never partially started ✓ |

So the **bundled** libraries resolve from the executable's own directory and cannot be shadowed by
the working directory or `LD_LIBRARY_PATH`. **Host** libraries resolved by soname (`libX11.so.6`)
*can* be shadowed — that is inherent to dynamic linking and is the trust assumption a portable
extraction directory makes. It is worth one sentence in `SECURITY.md`: extract the tarball somewhere
only you can write.

### 7.11 A-36 · XDG base directories — **FAIL**, see [F-19](#f-19)

| `XDG_DATA_HOME` | Result |
|---|---|
| an existing directory | works ✓, creates only `VisualCat/{Sessions,Diagnostics,SessionAccess-v1,settings.json}` beneath it |
| **a path that does not exist** | **exit 1, `error: Session lease storage is unavailable.`** |
| **a nested missing path** | same |
| an unwritable directory | `error: Access to the path '…/ro/VisualCat' is denied. cause: Permission denied` ✓ — specific, names the path |
| a **relative** path | works, but the relative directory is **never created** — it silently falls back to the default root |
| empty string / unset | falls back to the default ✓ (correct per the XDG specification) |
| `HOME` unset, `XDG_DATA_HOME` unset | works |
| **`HOME` pointing at a directory that does not exist** | **exit 1, same opaque message** |

The unwritable case proves the code can produce a good message; the missing-directory case simply
does not create parents and does not report properly.

### 7.12 A-33 · Multi-process access through the lease directory — **FAIL**

| Case | Result |
|---|---|
| two simultaneous **reads** of one session | both exit 0, both return data ✓ — a read lease is shared |
| two simultaneous **writes**, the loser | `error: This capture is in use. cause: The process cannot access the file…` exit 1 ✓ |
| two simultaneous **writes**, the winner | **exit 0, and `vcat verify` reports `isValid: false` with `source.records.missing`** → [F-20](#f-20) |
| a stale lease after `SIGKILL` | does **not** lock the session out; a later index of the same session succeeds ✓ |
| lease cleanup after every process exits | **9 zero-byte files remain**, and they accumulate (127 in a working root) → [F-24](#f-24) |

The control matters: an *uncontended* index of the same file produces `isValid: true` with 399,956
entries, twice in a row. Only contention produces the invalid session.

### 7.13 I-13 · Signals — `SIGINT` **PASS**, `SIGTERM` **FAIL**

Interrupting `vcat index` over a 900,000-line file 2.5 s in, three times per signal:

| Signal | exit | session state | `vcat verify` |
|---|---|---|---|
| `SIGINT` ×3 | 0 | `Ready` | **`isValid: true`, 899,900 entries** |
| `SIGTERM` ×3 | 143 | `Importing` | **`isValid: false`, 1 entry** |

`CLI.md` and plan I-13 both say `SIGTERM` behaves as `SIGINT` does. It does not — see
[F-18](#f-18).

### 7.14 A-34 · Settings corruption — **partial PASS**

A truncated, a malformed, a schema-newer and a `chmod 000` `settings.json` all let the product start
with defaults, on both surfaces, and **the unreadable original was never destroyed**. What is missing
is the second half of the requirement: neither surface **says** it ignored a corrupt settings file —
the CLI is silent and the desktop's notice lane is empty. Minor, folded into the observations rather
than filed separately.

### 7.15 I-02 · Import parity — **PASS**

Every corpus accounts for its source lines exactly, and the counters are consistent across formats:

| Corpus | parsed | timed | meta | unknown | rejected | continuations |
|---|---|---|---|---|---|---|
| `small.txt` | 1,000 | 1,000 | 1 | 0 | 0 | 0 |
| `fmt-threadtime.txt` | 5,000 | 5,000 | 1 | 0 | 0 | 0 |
| `fmt-time.txt` | 5,000 | 5,000 | 1 | 0 | 0 | 0 |
| `fmt-epoch.txt` | 5,000 | 5,000 | 1 | 0 | 0 | 0 |

### 7.16 A-25 · File-chooser cancellation — **FAIL**, see [F-22](#f-22)

| Route | Result |
|---|---|
| dismiss the chooser with the **window manager's close** | clean — the toolbar re-enables, no stuck operation ✓ |
| dismiss the chooser with **Escape** | clean ✓ |
| press the **shell's own Cancel** while the chooser is open | **`Cancelling…` forever**; the chooser stays open; every file command goes disabled |

Recoverable — dismissing the chooser afterwards clears it — but the control that says it is
cancelling is the one that does not.

### 7.17 G0 · The [F-10](#f-10) decider — **it is XWayland-specific**

§5.2 named this the single most valuable next step, so it was run. The guest was switched to a real
**Xorg** session (`WaylandEnable=false` in `/etc/gdm3/custom.conf`, autologin, `systemctl restart
gdm3`; ledgered and restored afterwards — `loginctl` confirmed `Type=x11`, `Xorg` running, no
`Xwayland`). The same five-open measurement:

| Session | attempt 1 | 2 | 3 | 4 | 5 |
|---|---|---|---|---|---|
| **Wayland + XWayland** | 15.5 % | 30.8 % | 30.8 % | 15.5 % | 0.1 % |
| **Xorg (G0)** | **0.1 %** | **0.1 %** | **0.1 %** | **0.1 %** | **0.1 %** |

(unpainted fraction of the *Appearance & timeline* window; 0.1 % is the antialiasing baseline.) The
main window measured 0.15 % on Xorg, against 5 % with a duplicated hero block on XWayland.

**Five of five clean on X11, four of five broken on XWayland.** [F-10](#f-10) is therefore an
XWayland presentation defect, not a general drawing bug — which changes who fixes it, though not how
much it matters: XWayland *is* the default Ubuntu and Fedora desktop.

Two other findings were re-checked on X11 and are **not** XWayland artifacts — both reproduce
identically there:

* [F-11](#f-11) — both frames still `SHOWING,ACTIVE`, still no `MODAL`.
* [F-12](#f-12) — Escape still does not dismiss the dialog.

---


### 7.18 Two facts the journal settled, and a plan trap that is wrong

The `/var/crash` sweep at the end of the run turned up something worth chasing, and chasing it fixed
two gaps in §1.3 and contradicted one of the plan's Appendix B entries.

**The renderer is now positively identified — this whole run was software rendering.** §1.3 could
only say "probably G5" because `glxinfo` is absent. The journal says it outright, on **every one of
27 launches**:

```
[OpenGL]Unable to initialize GLX-based rendering:
  'Avalonia.OpenGL.OpenGlException: Renderer 'SVGA3D; build: RELEASE;  LLVM;' is blacklisted by 'SVGA3D'
```

Avalonia deliberately blacklists VMware's SVGA3D renderer and falls back. So every timing, frame and
visual observation in this report is a **G5 result**, now as a measured fact rather than an
assumption, and none of it is a baseline for metal.

That is also a lead for [F-10](#f-10), and it is offered as a lead rather than a conclusion: the
paint defect appeared on XWayland and not on Xorg (§7.17), and the message was present throughout the
XWayland passes. It was **not** observed in the journal window covering the Xorg pass — but that
window was narrow and the absence was not independently confirmed, so *"XWayland forces the software
path and the software path has the bug"* is a hypothesis for whoever files the upstream report, not a
finding. Confirming it is one command on each session type:

```shell
journalctl --since '-5 minutes' | grep -a 'Unable to initialize GLX-based rendering'
```

**A second line, on every launch:**

```
[X11Platform] SMLib/ICELib reported a new error: SESSION_MANAGER environment variable not defined
```

Harmless — there is no X session manager on a modern GNOME desktop — but it is emitted 27 times out
of 27 and says nothing a user can act on. Worth downgrading to a debug-level message.

**Plan Appendix B #26 is wrong on this host.** It states: *"the absence of a `journalctl` entry
proves nothing: there is no systemd unit and no `.desktop` entry, so nothing routes the app's output
to the journal."* In fact the opposite holds — with a proper graphical environment, redirected
`stdout` and `stderr` are both **byte-empty** (measured: 0 B each), and these two diagnostics appear
**only** in the journal. So `journalctl` is exactly where to look for renderer and platform problems,
and the redirected streams are where to look for startup exceptions ([F-02](#f-02)). Both halves are
needed; neither alone is complete. The trap should be rewritten to say so.

**The one `/var/crash` entry is not VisualCat's.** Signal 6 (`SIGABRT`) in
`/usr/libexec/xdg-desktop-portal-gnome`, at 00:53:36, with:

```
Gdk:ERROR:../../../gdk/gdksurface.c:948:_gdk_surface_destroy_hierarchy: assertion failed
Bail out! Gdk:ERROR:…
```

This is a GTK/GDK assertion inside the **portal**, during the §7.16 sequence of repeated
chooser open/dismiss cycles. Classified per plan exit criterion 5 as a host-component failure, **not**
an attributable product crash: no VisualCat process died, no VisualCat core was stored, and
`journalctl -p err` shows **0** entries matching `visualcat|segfault|oom` for the whole run. It is
recorded rather than filed because it is reachable from an ordinary VisualCat workflow — open a file
chooser, close it with the window manager — and because a tester who meets it should know where it
came from. The report was deleted during hand-back per the run's retention policy.

---

## 8. Findings from the second pass

### F-18 · Major · `SIGTERM` leaves an unverifiable session where `SIGINT` leaves a clean one

**Severity** Major — `SIGTERM` is how a long CLI index actually gets interrupted on Linux: a
`systemd` stop, a session logout, a container shutdown, and a bare `kill` all send it. The plan and
`CLI.md` both promise the two signals behave alike.

**Reproduction**, three times per signal, `vcat index` over a 900,000-line generated log, signalled
2.5 s in:

```shell
vcat generate-test-log --output big.txt --lines 900000 --seed 7
vcat index big.txt --output s.vcat & P=$!; sleep 2.5; kill -INT  $P; wait $P   # exit 0
vcat index big.txt --output s.vcat & P=$!; sleep 2.5; kill -TERM $P; wait $P   # exit 143
```

| Signal | exit | `.descriptor.state` | `vcat verify` |
|---|---|---|---|
| `SIGINT` | 0 | `Ready` | `isValid: true`, **899,900 entries** recoverable |
| `SIGTERM` | 143 | `Importing` | `isValid: false`, **1 entry** |

3/3 each way, no variation.

**Expected** — `CLI.md`: *"`SIGTERM` behaves as `SIGINT` does."* Plan I-13: *"interrupt an `index`
and a `capture-adb` at several points and verify each surviving session with `vcat verify`."*

**Suggested fix.** The `SIGINT` path already does the right thing — it requests cooperative
cancellation, lets the in-flight generation publish, and finalizes the manifest. `SIGTERM` is not
wired to it, so the process is torn down mid-publication. In .NET the two are handled separately:

```csharp
// SIGINT already routes here. SIGTERM must too, or a systemd stop or a logout truncates
// the session mid-publication and leaves it unverifiable (I-13 / F-18).
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;          // take over from the default terminate
    cancellation.Cancel();          // same cooperative path as Ctrl+C
});
```

Then give the shutdown a bounded drain so the cancellation can actually finish — a `SIGTERM` handler
that returns immediately still lets the runtime exit — and add the `kill -TERM` case beside the
existing `SIGINT` test so the pair stays honest.

**Appendix-B trap checks.** Not an OOM kill (#29) — exit is 143, not 137, `dmesg` is clean. Not a
disk or permission problem — the `SIGINT` control at the same point in the same file succeeds.

---

### F-19 · Major · A data root that does not exist is never created, and neither surface says so

**Severity** Major — the product is unusable, and the message points the reader at the wrong cause.
It is reachable on a fresh account (`~/.local/share` not yet created) and by anyone who sets
`XDG_DATA_HOME` in a shell profile without creating the directory.

**CLI**

```shell
XDG_DATA_HOME=/does/not/exist vcat index small.txt --output out.vcat
# error: Session lease storage is unavailable.        exit 1
```

Same for a nested missing path, and same when `HOME` points at a directory that does not exist. The
message names neither the path nor the cause, and the directory is never created. Contrast the
*unwritable* case, which gets this right:
`error: Access to the path '…/ro/VisualCat' is denied. cause: Permission denied`.

**Desktop — worse.** With `XDG_DATA_HOME` pointing at a missing directory and `--log small.txt`, the
app starts, shows an empty state, opens nothing, and puts this in the notice lane:

> **VisualCat started, but part of the startup did not finish · Part of the session is missing. It
> may have been moved or deleted while it was open.**

There is no missing session. The data root does not exist. A reader follows that sentence into
looking for a deleted capture, when the fix is `mkdir -p`. `stderr` is empty, so the usual Linux
console route (plan Appendix B #26) gives nothing either, and every subsequent *Open log* fails the
same way with no route out.

**A second, smaller defect in the same area:** a **relative** `XDG_DATA_HOME` is silently ignored —
the directory is never created and the product falls back to the default root without a word. The
XDG specification does say a relative value is invalid, so ignoring it is defensible; doing it
silently means the user's data quietly lands somewhere other than where they asked.

**Suggested fix.**

1. Create the data root, including parents, on first use. This is what every XDG-aware application
   does, and the specification expects it (`$XDG_DATA_HOME` "should be created with permissions
   0700" if it does not exist):
   ```csharp
   // A fresh account has no ~/.local/share, and a profile-set XDG_DATA_HOME often does not
   // exist yet. Create the tree rather than failing on the lease directory (A-36 / F-19).
   Directory.CreateDirectory(dataRoot);          // creates parents
   ```
2. When creation genuinely fails, report it the way the unwritable case already does — name the path
   and the cause — instead of `Session lease storage is unavailable.`, which is a symptom three
   layers down. The same string is also produced by a symlinked lease directory (§7.7), so it
   currently conflates two very different causes.
3. On the desktop, do not describe a startup failure as a missing session unless a session is
   actually missing. The notice should name the data root and the reason, and offer the one action
   that helps.
4. Decide explicitly what a relative `XDG_DATA_HOME` means and say it: either resolve it against
   `$HOME` or reject it with a message, but do not silently use a different directory.

---

### F-20 · Major · Two processes writing one session: the winner exits 0 and leaves a session that fails verification

**Severity** Major — silent corruption with a success exit code. A script that checks `$?` sees
success and ships a broken session.

**Reproduction.** One session, two `vcat index --force` processes staggered by 0.4 s:

```shell
vcat index med.txt --output race.vcat                      # 400,000 lines, verifies clean
vcat index med.txt --output race.vcat --force &            # writer A
sleep 0.4
vcat index med.txt --output race.vcat --force &            # writer B
wait
vcat verify race.vcat
```

| | Result |
|---|---|
| writer B (loser) | exit 1, `error: This capture is in use. cause: The process cannot access the file…` ✓ correct |
| writer A (winner) | **exit 0** |
| `vcat verify race.vcat` | **`isValid: false`**, `issues: ["source.records.missing"]`, 399,956 entries checked |

**Control**, proving it is contention and not the corpus: an *uncontended* index of the same file
produces `isValid: true` with 399,956 entries, twice in a row.

**Expected** — plan A-33: *"The lease directory serializes them: a read lease is shared, a write is
exclusive … and **nothing is corrupted**."*

**Suggested fix.** The exclusion is working at the *lease* level — the loser is refused — so the
damage is happening outside the lease's protection. Two likely seams, both worth checking:

1. `--force` almost certainly clears the existing session directory **before** taking the write
   lease. Move the lease acquisition ahead of any destructive step, so a refused writer cannot have
   already deleted or truncated part of what the winner is publishing:
   ```csharp
   // Take the exclusive write lease before --force touches anything on disk: today the
   // loser can delete part of the session the winner is still publishing (A-33 / F-20).
   await using var lease = await SessionLease.AcquireWriteAsync(path, cancellationToken);
   if (force) { ClearExistingSession(path); }
   ```
2. Make the winner notice. A session that publishes successfully should verify; if the writer cannot
   guarantee that under contention, it should fail loudly rather than exit 0. A cheap guard is to
   re-read the manifest after the final rename and confirm the record count matches what was
   written, returning a non-zero exit when it does not.

Add the race to the test suite directly — two writers, staggered, assert both that the loser is
refused **and** that the survivor verifies.

---

### F-21 · Minor · The portable-archive extractor has no expansion bound

**Severity** Minor by reach — the user must open a hostile archive — but the plan classes P-04
failures as Blockers by default, so this needs an explicit release decision rather than a silent
acceptance. The actual security boundary (escape, links, special files, modes) held completely;
what is missing is a resource bound.

**Observed.** `archive-bomb.vcat.zip` — 1,106,404 bytes on disk, declaring a 1 GiB member — opened
successfully (`Opened archive-bomb.vcat.zip`) and wrote:

```
1073741824  …/Sessions/20260911-225827-archive-bomb.vcat-…/bomb.bin
```

The session directory ended at **1.1 GiB** inside the product's own data root, where it also counts
against the session cache. At a higher compression ratio the same 1 MB archive fills any disk.

Two related members were also extracted verbatim: a **200-directory-deep** path and a
**240-character** file name.

**The root cause is common to all three:** the extractor writes **every member of the archive**,
including `bomb.bin`, which is not part of the session format at all.

**Suggested fix — one change fixes all three.** Extract only the members the session format defines:

```csharp
// Only members the session format defines are extracted. Anything else — a bomb payload, a
// 200-deep path, a 240-character name — is not part of a session and is refused (P-04 / F-21).
if (!SessionLayout.IsKnownMember(entry.FullName))
{
    throw new InvalidDataException(
        $"Portable archive contains an entry that is not part of a session: {entry.FullName}");
}
```

`manifest.json`, `raw.log`, `view.json`, `templates-final.jsonl`, `source-order/*`,
`segments/NNNNNN/*` is the whole set — a legitimate archive here had exactly 32 members, all
matching. Add a belt-and-braces total-uncompressed-size cap and a per-entry ratio cap as well, so a
member with a *known* name cannot be inflated either, and make both caps say which limit they hit.

---

### F-22 · Minor · The shell's *Cancel* on a chooser operation never completes and disables every file command

**Severity** Minor — recoverable — but it misses a named §4.2 budget and the control that claims to
be cancelling is the one that is not.

**Reproduction**, entirely through the UI:

1. Click **Open archive**. The portal chooser *Open portable VisualCat archive* opens and the shell
   shows `Choosing portable archive…` with a **Cancel**.
2. Click that **Cancel**.

| Observation | |
|---|---|
| status | **`Cancelling…`** at t+3 s, t+15 s, t+40 s and t+90 s, progress bar still animating |
| the chooser | **still open** |
| *Open archive*, the shell's *Cancel* | both **disabled** — the single file-operation slot is held |

Dismissing the chooser afterwards (Escape, or the window manager's close) clears it and re-enables
everything. Both of those routes on their own are clean — it is only the shell's Cancel that hangs.

**Expected** — §4.2: *"Close a dialog whose background work is still running — Acknowledged ≤250 ms;
closed or truthfully `Cancelling…` ≤2 s"*; **R-65**: *"a **Cancel** that waits for the writer's
actual result"*.

**Suggested fix.** The picker is awaited without the operation's token, so cancelling the operation
cannot dismiss it and the operation waits on a call that will not return:

```csharp
// Without the token the portal dialog keeps the operation alive after Cancel, and the shell
// sits on "Cancelling…" until the user dismisses the chooser themselves (A-25 / F-22).
var files = await storage.OpenFilePickerAsync(options).WaitAsync(operation.Token);
```

`IStorageProvider` does not accept a `CancellationToken` directly, so `WaitAsync(token)` — or a
`TaskCompletionSource` linked to the token — is the practical route; the abandoned picker then
closes on its own when the user dismisses it, and the slot is already free. Whichever way it is
wired, the invariant to test is the one that failed: **after pressing Cancel, the file commands are
usable again within the §4.2 budget.**

---

### F-23 · Polish · `gh attestation verify` prints nothing and exits 0

**Severity** Polish, and it is a **documentation** problem rather than a product one — but
`README.txt` inside the shipped tarball tells every user to run exactly this command, and silence is
indistinguishable from success.

**Observed** with `gh` 2.100.0 against the genuine release tarball:

```shell
gh attestation verify VisualCat-Desktop-linux-x64-v2.0.13.tar.gz --repo benny-cz/VisualCat
# (no output at all)      $? = 0
```

The same command with `--format json` produces 17 KB of verification result including the workflow,
the tag and the source commit (§7.2). So verification really does happen — the human-readable banner
is simply not printed when stdout is not a terminal, and in this environment it never is.

**Suggested fix.** In `README.txt` and `RELEASE-NOTES.md`, tell the reader what success looks like
and give them a form that shows it:

```
  gh attestation verify <archive> --repo benny-cz/VisualCat --format json | jq \
    '.[0].verificationResult.signature.certificate
     | {sourceRepositoryURI, buildSignerURI, sourceRepositoryDigest}'
```

and add one sentence: *"the digest it prints is the commit this build came from; it should match the
version shown in the application."* That turns a silent exit code into something a user can actually
check, and it is the only place in the documentation where the strongest origin guarantee is offered.

---

### F-24 · Polish · Lease files are never removed

**Severity** Polish — zero-byte files, no functional effect observed.

**Observed.** After every VisualCat and `vcat` process had exited cleanly, the lease directory still
held **9** files (`.intent`, `.read`, `.write` per session), all zero bytes, and they persisted. In
the working data root the count had reached **127** over a few hours of testing.

Plan A-33 asks to *"confirm they are removed when the last user exits"*. They are not — though the
related and more important half does pass: a stale lease after `SIGKILL` does **not** lock a session
out (§7.12).

**Suggested fix.** Delete a lease file when its last holder releases it, and — because `SIGKILL`
means that cannot be relied on — sweep lease files whose session directory no longer exists when the
lease root is next opened. Given the files are already zero-byte markers whose liveness is
established by locking rather than by existence, a sweep on open is enough:

```csharp
// A killed process leaves its marker behind; nothing else ever removes it. Sweep markers whose
// session is gone when the lease root is opened (A-33 / F-24).
foreach (var marker in Directory.EnumerateFiles(leaseRoot))
{
    if (!SessionExistsFor(marker) && !IsHeld(marker)) { TryDelete(marker); }
}
```

Worth listing in `PRIVACY.md`'s residue section too, beside [F-17](#f-17) — these are the two things
VisualCat leaves behind that a user auditing it will find.

---

### F-25 · Polish · One corrupted file produces ~380 identical issues in the verify report

**Severity** Polish — the verdict is correct; the report is just unusable at a glance.

**Observed.** With a single segment file replaced by a symlink (§7.7), `vcat verify` correctly
returns exit 3 and `isValid: false`, but the JSON carries roughly **380 identical objects**:

```json
{"code":"source.index","message":"…","isError":true}   ×380
```

A human reading the output, or a CI job printing it, gets several screens of the same line for one
broken file.

**Suggested fix.** Collapse repeats in the report: keep the first occurrence, add a count and a
bounded sample of locations.

```json
{ "code": "source.index", "isError": true, "occurrences": 380,
  "message": "…", "firstEntryId": 1, "sampleEntryIds": [1, 2, 3, 4, 5] }
```

Cap the number of distinct issue objects as well (say 100) with a `"truncated": true` flag, so a
thoroughly corrupted session cannot produce an unbounded report — which is also what **P-07** asks
of the verifier.

---

## 9. Second-pass cleanup and host hand-back

The second pass changed more of the host than the first, because it switched the display server.
Everything is restored and every ledger row is closed.

| Step | Action | Verified |
|---|---|---|
| 1 | Stop every product process | `pgrep -x VisualCat` = 0, `pgrep -x vcat` = 0 |
| 2 | **Restore the Wayland session** | `/etc/gdm3/custom.conf` byte-compared against the backup (`cmp` clean), `gdm3` restarted, `loginctl` reports `Type=wayland Active=yes`, `Xwayland` running, **`pgrep -c Xorg` = 0** |
| 3 | Restore `idle-delay` / `idle-activation-enabled` | `uint32 300` / `true` |
| 4 | Restore `toolkit-accessibility` | `false` (its pre-run value) |
| 5 | Restore the host's ADB server | already done in §4; untouched by this pass |
| 6 | Delete the security scratch | `~/vcat-run/sec` and `~/vcat-run/data-p` removed, including the 1.1 GiB bomb expansion from [F-21](#f-21) |
| 7 | Remove planted escape targets | `/tmp/lease-escape`, `/tmp/sess-escape`, `/tmp/VCAT-*`, `/tmp/bx` |
| 8 | Remove the `noexec` loop image | unmounted and deleted; `findmnt` clean |
| 9 | Remove stale diagnostic sockets | [F-17](#f-17) residue cleared again |
| 10 | Confirm the account's own data is intact | `~/.local/share/VisualCat/Sessions` holds its original **4** sessions |
| 11 | Free space | 163 GiB, unchanged from the start of the run |

The ledger, now fully closed:

| UTC | Scenario | Target | Restored |
|---|---|---|---|
| 2026-09-11T18:03:23Z | setup | `org.gnome.desktop.session idle-delay` | ✓ 22:33:58Z |
| 2026-09-11T18:03:23Z | setup | `org.gnome.desktop.screensaver idle-activation-enabled` | ✓ 22:33:58Z |
| 2026-09-11T18:03:23Z | setup-D1 | `~/.local/share/VisualCat` | ✓ 22:33:58Z |
| 2026-09-11T21:55:49Z | U-07/U-08 | `org.gnome.desktop.interface toolkit-accessibility` | ✓ 22:33:58Z |
| 2026-09-11T23:12:23Z | G0 | `/etc/gdm3/custom.conf WaylandEnable` | ✓ 23:18:41Z |
| 2026-09-11T21:34Z | ADB | Windows host ADB server + firewall rule | ✓ §4 |

`~/vcat-run` retains 532 MB: both candidates, the release tarballs, the small corpora, the evidence
index and the closed ledger. Deleting it removes all of it and nothing else.

### 9.1 Traps this run added to the plan's Appendix B

Four things cost real time here and are not in the plan's trap list. They are worth adding.

1. **`ulimit -v 2000000` kills .NET itself, not the code under test.** A trivial `vcat --version`
   dies with `Fatal error. Failed to create RW mapping for RX memory.` The runtime reserves a large
   *address space* regardless of actual use. Run P-07 at `ulimit -v 4000000` or higher, and always
   take a control measurement with a trivial input before attributing an OOM to the parser.
2. **AT-SPI and framebuffer coordinates disagree while a window is moving, and both go stale.**
   Re-read a control's extents **immediately** before clicking it; a coordinate read even one action
   earlier can land on the neighbouring button. Several minutes here were spent diagnosing a
   "wrong dialog" that was entirely a stale-coordinate misclick.
3. **A depth-limited accessibility walker looks exactly like an empty tree.** The real tree is
   435 nodes and the first interactive control sits below depth 8. Walk to depth 40 before
   concluding anything about accessibility support.
4. **`xdotool search --name 'a\|b'` is not alternation.** It takes a regex, but `\|` does not mean
   *or* in that engine, so a search for two possible dialog titles silently matches nothing and the
   dialog looks absent. Search for one exact title, or enumerate and filter in the shell.

A fifth is already in the plan (#49, VM clocks) and was observed: the guest clock was ~3 h behind at
the start of the run and jumped forward when NTP corrected it, mid-run.

---

## 10. Third pass — glibc floor, globalization, the corruption matrix, and multi-user

§5.2 still listed the glibc floor, the ICU-absent leg, the session-corruption matrix, locale and
time-zone reproducibility, multi-user isolation and `umask 077` as untested. This pass closes them.
Same candidate, same host. Distribution userlands were exercised with **rootless `podman`** — the
same kernel with a different libc and userland, which is a legitimate way to test the glibc floor
(it is not emulation, so plan §1.2's exclusion does not apply). Podman was installed for this pass,
ledgered, and removed afterwards.

### 10.1 Findings added by this pass

| ID | Severity | One line |
|---|---|---|
| [F-26](#f-26) | Minor | With `tzdata` absent, an explicit `TZ` is **silently ignored** and the session records UTC — the plan requires a missing zone database to be reported, not guessed |
| [F-27](#f-27) | Minor | A session on a shared path is readable by any other local user, including a portable session's embedded `raw.log` (mode 664) |
| [F-28](#f-28) | Polish | `vcat verify` returns `isValid: true` and exit 0 when it could **not** check the raw source, so an exit code cannot distinguish "verified" from "unverifiable" |

### 10.2 B-02 · The glibc floor — **PASS, and it is low**

§1.3 recorded the floor as entirely untested: only glibc 2.35 had been exercised. The release CLI
was run against four real distribution userlands:

| Userland | glibc | Result |
|---|---|---|
| Debian 12 | **2.36** | runs, `vcat 2.0.13+0670981…` |
| Ubuntu 22.04 (host) | **2.35** | runs (the whole of §1–§9) |
| Debian 11 | 2.31 | not established — the image's `libicu` install failed, so it never got past the ICU check |
| **Debian 10** | **2.28** | **runs**, `vcat 2.0.13+0670981…` |

So the shipped `linux-x64` build runs on **glibc 2.28** — Debian 10, released 2019. That is a
materially wider support statement than the repository currently makes, and it is worth putting a
number in `SUPPORT.md` now that one exists.

### 10.3 B-02 · The ICU-absent leg — **PASS**, with F-02's mechanism again

Every stock image lacks `libicu`, which gave this leg for free. The message is exactly what B-02
asks for — it names the missing package, both common package names, the escape hatch, and a docs
URL:

> `Couldn't find a valid ICU package installed on the system. Please install libicu (or icu-libs)
> using your package manager and try again. Alternatively you can set the configuration flag
> System.Globalization.Invariant to true if you want to run with no globalization support. Please
> see https://aka.ms/dotnet-missing-libicu for more information.`

`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` then runs cleanly, exit 0 — and §10.4 proves it does not
silently change instants, which is the plan's stated fail condition.

One carry-over, **and it qualifies [F-02](#f-02)'s suggested fix.** The process terminates through
`Environment.FailFast` and the shell reports `Aborted (core dumped)` — the same
abort-instead-of-clean-exit shape as F-02, from a different cause. But the stack shows it happens
*earlier* than F-02's cases:

```
Process terminated.
Couldn't find a valid ICU package installed on the system. …
   at System.Environment.FailFast(…)
   at VisualCat.Domain.ProductInfo.ChannelOf(System.String)
   at VisualCat.Domain.ProductInfo..cctor()
   at VisualCat.Domain.ProductInfo.get_InformationalVersion()
   at VisualCatCli.RunAsync(System.String[])
```

It aborts inside a **static constructor** (`ProductInfo..cctor`), reached from the very first version
lookup — before `Program.Main`'s `try`/`catch` can see anything, and in the CLI rather than the
desktop. So the handler F-02 proposes would catch the `DISPLAY` and `libX11` cases but **not** this
one. The runtime's own message here is already excellent, so the right treatment is narrower: set
`System.Globalization.UseNls`/`InvariantGlobalization` policy deliberately in the runtime config, or
probe for ICU before the first culture-sensitive call, so the product chooses its own failure rather
than inheriting a `FailFast`. Worth noting in F-02's fix as a second, separate case.

### 10.4 A-37 / I-11 · Locale, culture and time zone — **PASS, decisively**

A session indexed **once** under `TZ=UTC`, then exported and queried under seven different
presenting environments:

| Presenting environment | CSV SHA-256 (first 16) | first entry |
|---|---|---|
| `LC_ALL=C TZ=UTC` | `8336404f20f3335b` | `2026-05-15T14:13:37.0000000+00:00` |
| `TZ=Europe/Prague` | `8336404f20f3335b` | identical |
| `TZ=Pacific/Auckland` | `8336404f20f3335b` | identical |
| `TZ=America/New_York` | `8336404f20f3335b` | identical |
| `cs_CZ.UTF-8` (comma decimals) | `8336404f20f3335b` | identical |
| `ar_AE.UTF-8` (RTL) | `8336404f20f3335b` | identical |
| **`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`** | `8336404f20f3335b` | identical |

**Byte-identical across all seven.** Stored instants do not move with the presenting culture, the
presenting zone, or invariant globalization — the strongest form of the I-11 assertion, and it
disposes of the plan's explicit worry that invariant mode "must not silently produce different
instants for the same file".

Indexing under a different `TZ` does change the result, and correctly so: `small.txt` is threadtime
with no year and no zone, so the host zone *is* part of the parse, and the manifest records which
one was used (`timestampPolicy.timeZoneId`). Parse environment matters; presenting environment does
not. That is the right split.

The one failure in this area is [F-26](#f-26), below.

### 10.5 X-26 · Session corruption and verifier matrix — **PASS**

Eleven fault types, one at a time, against a session that verified clean first
(`isValid: true`, 1,000 entries):

| Fault | exit | `isValid` | issue code |
|---|---|---|---|
| manifest truncated | 3 | false | `session.open` |
| manifest `formatVersion` raised to 99.0 | 3 | false | `session.open` |
| manifest deleted | 3 | false | `session.open` |
| column byte flipped (`tag.bin`) | 3 | false | **`segment.checksum`** |
| bitmap byte flipped (`*.rbm`) | 3 | false | **`segment.checksum`** |
| segment file `chmod 000` | 3 | false | `session.open` |
| segment directory `chmod 000` | 3 | false | `session.open` |
| `source-order/index.bin` zeroed | 3 | false | `session.open` |
| whole segment directory removed | 3 | false | `session.open` |
| segment file replaced by a symlink to `/etc/passwd` | 3 | false | `source.index` (§7.7) |

Raw-source coverage, tested separately for each session kind because they store the source
differently:

| | 1-byte flip | truncated | deleted |
|---|---|---|---|
| **standard** session (external source, `source.sha256` in the manifest) | `source.hash`, invalid ✓ | `source.hash`, invalid ✓ | `source.unavailable`, **still `isValid: true`** → [F-28](#f-28) |
| **portable** session (embedded `raw.log`) | `source.embedded`, invalid ✓ | `source.embedded`, invalid ✓ | `source.embedded`, invalid ✓ |

`--skip-raw` correctly returns `isValid: true` on a standard session whose source was altered, as
`CLI.md` documents.

**A self-correction worth recording.** The first attempt at this row created a `raw.log` inside a
*standard* session and reported that corrupting it went undetected. That was wrong: a standard
session has no `raw.log` at all — its source is external — so the file was one the product never
reads. The control (`ls` on a clean session, and `sourceKind: File` in its manifest) exposed the
mistake, and the row was redone correctly above. It would have been a false Major.

### 10.6 A-10 (CLI leg) · External source changed or missing — **PASS** with [F-28](#f-28)

The standard-session rows above are exactly A-10's oracle: a modified or truncated external source
is detected by hash and the session goes invalid; a deleted one is reported as `source.unavailable`
and the index itself remains valid, which is the documented degraded, index-only behaviour. The
desktop leg — the labelled degraded mode and the "why raw context is unavailable" route — was not
executed and remains open.

### 10.7 P-22 · `umask 077` — **PASS** (both legs now complete)

[F-19](#f-19) had masked this in §7.8. With the data root pre-created to work around it:

| | session directory | session files | lease directory | lease files |
|---|---|---|---|---|
| `umask 077` | `drwx------` | `-rw-------` | `drwx------` | `-rw-------` |
| `umask 022` (control) | `drwxr-xr-x` | `-rw-r--r--` | — | — |

**0** world-writable, **0** setuid/setgid under either. The product honours the account's umask
rather than forcing a mode — which is correct, and is also what makes [F-27](#f-27) possible.

### 10.8 P-17 · Multi-user isolation — **PASS in `$HOME`**, [F-27](#f-27) on a shared path

A second local account (`vcattest`, created for this row and removed afterwards) could reach
**nothing** of the real user's data:

```
ls /home/benny/                                  → Permission denied
ls /home/benny/.local/share/VisualCat/           → Permission denied
head …/VisualCat/settings.json                   → Permission denied
ls …/VisualCat/Sessions/                         → Permission denied
```

But the protection comes from the **desktop**, not from VisualCat: `~/.local` and `~/.local/share`
are `700`, while VisualCat's own directory is `775`. Move a session onto a shared path and the
protection is gone — see [F-27](#f-27).

**P-09's ACL leg is recorded as *not established*, not as a pass or a fail.** An ACL denying the
running user was set on a file owned by the other account, and `vcat index` read it — but so did a
plain `cat`, on both `/tmp` and ext4 under `$HOME`. The control proves the kernel was not enforcing
the ACL against this user at all, so the row says nothing about the product. The earlier attempt in
§7.8 was worse (the ACL named the file's own owner, which POSIX makes a no-op). A correct test needs
an ACL that `cat` demonstrably honours.

### 10.9 [F-19](#f-19) reproduced a third time, with a wider impact

Every container run in this pass failed at first with `error: Session lease storage is unavailable.`
until `mkdir -p /root/.local/share` was added. A stock container image has no XDG data directory, so
as it stands **the CLI cannot run in a container or a CI job at all** without a preparatory `mkdir`.
That is a materially wider impact than §7.11 recorded, and it is added to [F-19](#f-19).

### 10.10 Still open after three passes

| Row | Why |
|---|---|
| **A second distribution as a full run** | The glibc floor is now established (§10.2), but only `vcat --version` was exercised there. A full B-tier run on Debian or Fedora, with a desktop session, is still untested |
| **A-21 growing-source truncation, rotation and removal** | Attempted in this pass; the GUI automation could not be made reliable after the display-server switches left the window manager in a confused state. Still the highest-value untested row, because the plan names a known detection hole there that pairs with [F-09](#f-09) |
| **A correct ACL denial (P-09)** | §10.8 — needs an ACL the kernel demonstrably enforces |
| **Metal and a real GPU; performance budgets** | Unchanged — this host is confirmed software rendering (§7.18) |
| **ADB `no permissions` / `udev` (A-16)** | Unchanged — needs a device attached directly to the Linux host |
| **Orca end-to-end (U-07)** | Unchanged — needs a human listening |
| **G4 desktop-environment matrix, U-02 scaling, U-03/U-04 multi-monitor** | Unchanged |
| **A-29 upgrade, B-14/I-08 cross-platform round trip, soak, quota/low-disk** | Unchanged |

---

## 11. Findings from the third pass

### F-26 · Minor · With `tzdata` absent, an explicit `TZ` is silently ignored

**Severity** Minor by reach — you need a system without a zone database — but containers, minimal
images and CI runners are exactly that, and the consequence is silently wrong instants.

**Reproduction**, in a Debian 12 container with `libicu` installed and the data root pre-created:

```shell
# control — tzdata present
TZ=Europe/Prague vcat index /corpus/small.txt --output /tmp/a.vcat
#   manifest: "timeZoneId": "Europe/Prague"

rm -rf /usr/share/zoneinfo /etc/localtime

TZ=Europe/Prague vcat index /corpus/small.txt --output /tmp/b.vcat
#   "entries": 1000       exit 0        no warning, no error
#   manifest: "timeZoneId": "UTC"        ← the request was dropped
```

`small.txt` is threadtime with no year and no zone, so the zone **is** the interpretation of every
timestamp in the file. The user asked for `Europe/Prague`, got UTC, and nothing said so — every
instant in that session is off by the offset, in a session that looks entirely healthy.

**Expected** — plan A-37: *"run once with the system `tzdata` package absent or `/etc/localtime`
broken … **A missing zone database is reported, not guessed.**"*

**Suggested fix.** Do not silently substitute. When a requested zone cannot be resolved, say so and
let the caller decide:

```csharp
// A missing zone database is not a reason to reinterpret every timestamp in the file.
// Report it; do not quietly fall back to UTC (A-37 / F-26).
if (!TryFindTimeZone(requestedId, out var zone))
{
    throw new InvalidOperationException(
        $"Time zone '{requestedId}' could not be resolved. The system time-zone database " +
        $"(tzdata / /usr/share/zoneinfo) appears to be missing. Install it, or pass an " +
        $"explicit UTC offset.");
}
```

If a fallback is wanted for robustness, make it explicit rather than silent: record
`"timeZoneFallback": "requested Europe/Prague, resolved UTC (no zone database)"` in the manifest and
surface it as a parse warning, so the session carries the reason a reader would need.

---

### F-27 · Minor · A session on a shared path is readable by any other local user, including its raw log

**Severity** Minor — the product honours the account's `umask`, which is correct POSIX behaviour, so
this is a defensible design that nonetheless deserves an explicit decision for a tool whose files
are, by construction, other people's logs.

**Observed.** With Ubuntu's default `umask 002`, a session written to `/tmp` and a portable export
beside it:

| Object | mode |
|---|---|
| session directory | `775` |
| `manifest.json`, segment files | `664` |
| **portable session's `raw.log`** | **`664`** |

A second local account with no relationship to the first could then:

```
read the manifest:  {  "formatVersion": "2.0",  "descripto…
read embedded raw:  --------- beginning of main05-15 14:13:37.000000 10503  513…
list the session:   diagnostics manifest.json segments source-order templates-fi…
WRITE into it:      refused
```

So another user on the same machine can read **the full log content** of a portable session. Inside
`$HOME` this cannot happen — `~/.local` and `~/.local/share` are `700` — but that protection belongs
to the desktop, not to VisualCat, and it disappears the moment a session is saved to `/tmp`, a
shared project directory, or a group-shared mount.

**Expected** — plan P-17: *"a session on a shared path is not silently readable by the other account
because of an overly permissive mode."*

**Suggested fix.** Treat the raw source the way every tool treats a private key: create it `600`
regardless of umask, and create session directories `700`, because the content is not the user's own
by default.

```csharp
// A session's raw.log is verbatim log content that is usually not the operator's own.
// Create it owner-only regardless of umask, the way a private key is created (P-17 / F-27).
var options = new UnixFileMode(UnixFileMode.UserRead | UnixFileMode.UserWrite);
File.Create(rawPath, bufferSize, FileOptions.None, options);
```

If the decision instead is to keep honouring the umask, then say so where it matters: one line in
`PRIVACY.md` noting that a session saved outside your home directory inherits your umask and may be
readable by other users on the machine, and a note in the save dialog when the destination is
outside `$HOME`.

---

### F-28 · Polish · `verify` cannot distinguish "verified" from "could not be verified"

**Severity** Polish — the information is present in the report; only the exit code and the top-level
verdict conflate two different states.

**Observed.** A standard session whose external source has been **deleted**:

```shell
vcat verify std.vcat
# {"isValid": true, "issues": [{"code": "source.unavailable", …}]}     exit 0
```

versus the same session whose source was **altered**:

```shell
# {"isValid": false, "issues": [{"code": "source.hash", …}]}           exit 3
```

Both outcomes are reasonable on their own — a standard session deliberately does not own its source,
so a missing one means "cannot check", not "corrupt". But a script that checks `$?`, which is what
`CLI.md`'s exit-code contract invites, sees **success** for a session whose raw evidence could not be
checked at all. For a command whose whole job is establishing trust, that is the one distinction
worth surfacing.

**Suggested fix.** Keep `isValid` meaning "nothing detected as wrong", and add the missing axis
explicitly:

```json
{ "isValid": true, "rawVerified": false,
  "issues": [ { "code": "source.unavailable", "isError": false, … } ] }
```

and give it its own exit status — a fourth code, or reuse `3` behind an opt-in `--require-raw` flag
for callers that need the stronger guarantee. Document whichever is chosen in `CLI.md` beside the
existing `--skip-raw`, since the two are the same axis seen from opposite ends.

---

## 12. Third-pass cleanup and host hand-back

| Step | Action | Verified |
|---|---|---|
| 1 | Stop every product process | `pgrep -x VisualCat` = 0 |
| 2 | Close stray portal choosers left by the A-21 attempt | window list clean |
| 3 | **Remove the test user** created for P-17 | `userdel -r vcattest`; `id vcattest` → *no such user*; `/home/vcattest` gone |
| 4 | **Remove `podman` and `uidmap`** installed for §10.2 | `apt-get remove --purge` + `autoremove`; `command -v podman` → absent |
| 5 | Remove the rootless container storage it left behind | resolved with `readlink -f`, prefix-checked against the test root, then removed — 470 MB reclaimed |
| 6 | Remove third-pass scratch | `i11`, `x26`, `x26b`, `u077`, `u022`, `a21root`, `/tmp/vcat-shared`, `/tmp/rot.txt`, `/tmp/acl*` |
| 7 | Close every ledger row | **0** rows remain `pending` |
| 8 | Confirm the account's own data | `~/.local/share/VisualCat/Sessions` still holds its original **4** sessions |
| 9 | Free space | 163 GiB — identical to the start of the run |

Two mutations this pass made and reversed are worth naming, because they are the kind a careless run
leaves behind: a **local user account**, and a **package installation**. Both are recorded in the
ledger with their exact reversal commands, and both were reversed.

`~/vcat-run` retains 532 MB — both candidates, the release tarballs, the small corpora, the evidence
index and the closed ledger. Deleting it removes all of it and nothing else.

### 12.1 A correction this pass made to its own work

§10.5 records it in place, but it belongs in the cleanup summary too, because it is the kind of thing
a reader should be able to find: **the first attempt at the raw-source verification row produced a
false Major.** It reported that corrupting a session's `raw.log` went undetected. A standard session
has no `raw.log` — its source is external — so the file being corrupted was one the product never
reads. The control that caught it was trivial (`ls` on a clean session, and `sourceKind: File` in the
manifest) and should have been run first.

The same discipline turned up twice more in this pass: the `ulimit -v` "out of memory" that was .NET
reserving address space (§7.6), and the ACL row where a plain `cat` behaved exactly like the product
(§10.8). In all three cases the control, not the observation, decided the result. Every finding in
this report that survived has one.

### 12.2 Final host and device state

Re-verified after every pass and every restoration:

| | |
|---|---|
| Guest session | `Type=wayland Active=yes`; `gnome-shell` running, `Xwayland` 1, **`Xorg` 0** |
| Product processes | `VisualCat` 0 · `vcat` 0 · `adb` 0 |
| Test user | `id vcattest` → *no such user*; home removed |
| Packages added for testing | `podman`, `uidmap` — removed; `imagemagick`, `adb`, `at-spi2-core`, `python3-pyatspi`, `x11-apps`, `xclip` left in place as ordinary tooling |
| Desktop settings | `idle-delay 300`, `idle-activation-enabled true`, `toolkit-accessibility false` — all original values |
| `/etc/gdm3/custom.conf` | byte-identical to the pre-run backup |
| Mutation ledger | **0** open rows |
| The account's own VisualCat data | 4 sessions, `settings.json` 1,123 B — untouched throughout |
| Free space | 163 GiB, identical to the start of the run |
| `/var/crash` | **0** entries |
| VisualCat-attributable journal errors | **0** for the whole run |
| Android device | `RFCRC0A9GND` still `device` and authorized; **screen off and locked** |

The two `/var/crash` entries seen during the run were both `xdg-desktop-portal-gnome` aborting on a
GDK assertion when its dialogs were force-closed (§7.18) — a host component, not the product — and
the seven journal errors were the .NET `FailFast` from the **intentional** ICU-absent container test
in §10.3. Excluding those, VisualCat produced **zero** attributable errors, crashes, core dumps or
OOM kills across three passes.

---

## 13. Fourth pass — cross-platform parity, the upgrade gate, scaling, and A-21

§10.10 left five reachable rows. This pass closes four of them and establishes why the fifth cannot
be executed on this host. Two of the four are **named release exit criteria**: criterion 4 (byte-exact
cross-platform comparison) and criterion 10 (A-29 upgrade).

### 13.1 Findings added by this pass

| ID | Severity | One line |
|---|---|---|
| [F-29](#f-29) | Minor | Every `vcat` text output uses the platform's native newline with no way to control it, so the same session never exports byte-identically across platforms — **release exit criterion 4** |
| [F-30](#f-30) | Minor | Closing a portal file chooser with the window manager reliably crashes `xdg-desktop-portal-gnome`; after a few it stays `failed`. A host bug, but reachable from ordinary VisualCat use, and it blocked A-21 |

### 13.2 I-14 · Cross-platform generator parity — **PASS**

The same seed and options on both platforms:

| | SHA-256 |
|---|---|
| Linux `vcat generate-test-log --lines 5000 --seed 42 --format threadtime` | `6F931A6A72B136761350F7B8CFC904134A6A69AB514CC36982A46BA94A638298` |
| Windows, identical invocation | `6F931A6A72B136761350F7B8CFC904134A6A69AB514CC36982A46BA94A638298` |

**Byte-identical.** That completes **R-61**'s cross-platform half, and it means generated corpora are
a sound basis for parity work — which matters, because it is the control that makes §13.3's failure
attributable to the export path rather than to the input.

### 13.3 I-15 · Cross-platform artifact parity — **FAIL**, see [F-29](#f-29)

The same corpus indexed and exported on both platforms with identical options
(`--type csv --order source`, `stats`, `templates`):

| Artifact | Result |
|---|---|
| `export --type csv` | **differ** — identical once `\r` is stripped; Linux 0 CR, Windows 5,001 CR |
| `templates` JSON | **differ** — identical once `\r` is stripped |
| `stats` JSON | **differ** — identical once `\r` is stripped **and** the session GUID is excluded |

So every difference is the newline form, plus one legitimately fresh GUID. Content, ordering, BOM,
quoting and column order all match exactly. The plan is explicit that this is not an allowed
difference, and release exit criterion 4 requires the comparison to be byte-exact.

### 13.4 B-14 / I-08 · Portable archive round trip, Linux ↔ Windows — **PASS**

| Check | Linux → Windows | Windows → Linux |
|---|---|---|
| archive members | 32, **0 containing a backslash** | 32, **0 containing a backslash** |
| `vcat verify` on the far side | `isValid: true`, 0 issues, **5,000 entries**, 5,001 source records | `isValid: true`, 0 issues, **5,000 entries**, 5,001 source records |
| counters through the hop | `parsedEntries 5000 · timedEntries 5000 · metaRecords 1 · unknown 0 · rejected 0 · continuations 0 · templates 64 · sourceBytes 451040` | identical |
| **embedded `raw.log` SHA-256** | `6f931a6a72b136761350f7b8cfc90413…` | `6f931a6a72b136761350f7b8cfc90413…` |
| the original source corpus SHA-256 | `6f931a6a72b136761350f7b8cfc90413…` | same |

The raw source survives the platform hop **byte for byte** — the embedded `raw.log` hashes equal to
the original corpus file on both sides. Entry names use forward slashes in both directions and are
interpreted identically. Counts, instants, severity totals and template identities all survive.

This is the strongest cross-platform result in the report, and it stands in useful contrast to
[F-29](#f-29): the *binary* exchange format is exact, and only the *text* outputs drift.

### 13.5 A-29 · Upgrade from the previous supported release — **PASS**

The genuine previous release (`v2.0.12`, `2.0.12+ece618e`) was downloaded and used to build a real
D3 profile: two complete sessions, one interrupted with `SIGINT` mid-index (599,937 entries), and the
data root it creates. 228 files were hashed before the candidate was allowed near them.

| Step | Result |
|---|---|
| 2.0.13 **reads** 2.0.12 sessions | all three read correctly — `formatVersion 2.0`, `state: Ready`, counts 1,000 / 5,000 / 599,937 |
| 2.0.13 **verifies** a 2.0.12 session | `isValid: true`, 1,000 entries |
| did reading **rewrite** anything? | **No.** The only change to the 228 hashed files is *new*, zero-byte lease markers under `SessionAccess-v1`. No existing session file was touched — the plan's "does not rewrite old data merely by listing it" holds |
| 2.0.13 **writes** into the same root | creates a `formatVersion 2.0` session alongside the old ones |
| **rollback**: 2.0.12 after 2.0.13 | still reads its own sessions, **and** reads and verifies the session 2.0.13 created (`isValid: true`, 5,000 entries) |

So the upgrade is compatible in both directions for this pair, and the rollback risk is nil rather
than merely documented. **Release exit criterion 10 satisfied** for 2.0.12 → 2.0.13.

Incidentally this re-confirms [F-24](#f-24): three `info` calls left six new lease markers behind.

### 13.6 U-02 / U-11 · Scaling — **PASS**

Two mechanisms, recorded separately as the plan requires:

| Mechanism | Window (logical) | Capture (physical) | Unpainted |
|---|---|---|---|
| baseline | 1440×900 | 1440×900 | 0.15 % |
| `AVALONIA_SCREEN_SCALE_FACTORS=XWAYLAND0=1.25` | 1737×1215 | 1737×1215 | 0.31 % |
| `…=1.5` | 1737×1215 | 1737×1215 | 0.46 % |
| `…=2` | **2880×1800** | 1737×1215 | 0.05 % |
| `text-scaling-factor 1.25` | 1737×1208 | — | 0.30 % |
| `text-scaling-factor 1.5` | 1737×1201 | — | 0.43 % |

At 2× the whole interface scales coherently — text, icons, borders, severity chips, row labels, the
timeline plot and its axis — with no clipped control and no pointer/visual mismatch. The logical
2880×1800 against a 1737×1215 physical capture is the expected XWayland behaviour (the client renders
at scale, the compositor maps it) and is recorded rather than reported, per plan Appendix B #9.

`text-scaling-factor` reaches the layout too: the window remeasures at each value.

### 13.7 A-25 · The chooser with the portal stopped — **PASS**

This leg had never been executed; [F-30](#f-30) provided it. With
`xdg-desktop-portal-gnome.service` in a `failed` state (`code=dumped, signal=ABRT`), *Open log* still
produced a working chooser titled *Open Android logcat file* — the front-end
`xdg-desktop-portal` fell back and the dialog was usable. The plan asks that the fallback be "either
functional or honest about being unavailable"; it is functional.

### 13.8 A-21 · Growing-source truncation and rotation — **one case proven, the rest blocked**

**Case 1, `truncate -s 0` in place — PASS.** With a live follow over a file being appended at
0.12 s intervals:

| | |
|---|---|
| before | `state: Streaming`, 19 entries, `sourceChanges: 0` |
| after `truncate -s 0` | `state: Failed`, **31 entries**, **`sourceChanges: 1`** |
| notice | `Failed · VisualCat could not read or write the session: The followed file was truncated or rotated; the configured policy is to st…` |

That is the declared `rotationPolicy = stop` contract working exactly: the change is detected, the
notice distinguishes *truncated or rotated*, the session records the source change in its defect
counters, and everything committed before the change is kept.

**Cases 2–7 are blocked, and not by the product.** Each needs its own fresh follow, and starting a
follow requires the portal file chooser. Five attempts across two sessions failed at that step
because of [F-30](#f-30) — the portal crashes when a chooser is dismissed, and after a few crashes
systemd leaves it `failed`. The two cases the plan most wants — *rotate then make the replacement at
least as long*, and *unlink and keep appending to the unlinked inode* — therefore remain **untested**,
and A-21 stays the highest-value open row. A tester repeating this should dismiss portal dialogs with
**Escape only**, never with the window manager's close, and reset the service between attempts:

```shell
systemctl --user reset-failed xdg-desktop-portal-gnome
```

---

## 14. Findings from the fourth pass

### F-29 · Minor · Every CLI text output uses the platform's native newline, with no way to control it

**Severity** Minor in daily use — the content is correct everywhere — but it fails a **named release
exit criterion** (4: "the cross-platform comparison against the Windows candidate is byte-exact
everywhere the contract requires it") and plan I-15 names newline form as explicitly *not* an allowed
difference.

**Observed.** The same corpus, indexed and exported on both platforms with identical options:

| Artifact | Linux | Windows | After stripping `\r` |
|---|---|---|---|
| `export --type csv` | 531,313 B · 5,001 LF · **0 CR** | 536,314 B · 5,001 LF · **5,001 CR** | **identical** |
| `templates` JSON | 14,695 B · 0 CR | 15,297 B · 602 CR | **identical** |
| `stats` JSON | 5,378 B · 0 CR | 5,764 B · 386 CR | identical apart from the session GUID |

Everything else matches: the UTF-8 BOM, the column order, the quoting, the row order, every value.

**Why it matters beyond the gate.** A team with mixed Linux and Windows machines cannot diff two
exports of the same session, check one into version control, or compare checksums in CI, without
normalising line endings first — and nothing in the CLI tells them that is what differs. The desktop
does not have this problem: its export review carries an explicit **Encoding** choice (§2.13), and
the CLI has no equivalent.

**Suggested fix.** Give the CLI the option the desktop already has, and make the default
deterministic:

```csharp
// Text output must not depend on the host's newline, or the same session exports differently
// on Linux and Windows and no cross-platform comparison is possible (I-15 / F-29).
using var writer = new StreamWriter(stream, encoding) { NewLine = options.Newline switch
{
    NewlineStyle.Lf   => "\n",
    NewlineStyle.Crlf => "\r\n",
    _                 => "\n",        // deterministic default, not Environment.NewLine
}};
```

Add `--newline lf|crlf` to `export` (defaulting to `lf`) and apply the same writer to every text
`--type`: `csv`, `templates-md`, `templates-csv`, `stats-md`, `stats-csv`, and the JSON commands.
Then document in `CLI.md` that machine-readable output is byte-identical across platforms for the
same session and options — which is what makes I-15 assertable at all — and add a CI check that
exports the same fixture on both runners and `cmp`s them.

---

### F-30 · Minor · Closing a portal file chooser with the window manager crashes `xdg-desktop-portal-gnome`

**Severity** Minor, and it is a **host-component bug, not a VisualCat defect** — recorded because it
is reachable from ordinary VisualCat use, because it silently removes the file-chooser capability for
the rest of the session, and because it blocked a plan row (§13.8).

**Reproduction.** Open any VisualCat file chooser (*Open log*, *Follow file*, *Open archive*) and
close it with the window manager's close affordance rather than its own Cancel button —
`xdotool windowclose`, or the title-bar ✕:

```
xdg-desktop-portal-gnome[…]: Gdk:ERROR:../../../gdk/gdksurface.c:948:
                             _gdk_surface_destroy_hierarchy: assertion failed
xdg-desktop-portal-gnome[…]: Bail out!
systemd[…]: xdg-desktop-portal-gnome.service: Main process exited, code=dumped, status=6/ABRT
systemd[…]: xdg-desktop-portal-gnome.service: Failed with result 'core-dump'.
```

Observed **three times** across the run (01:40, 10:52, and during §13.8), each time writing an
`apport` report to `/var/crash`. After repeated crashes the unit stays `failed`, and from then on
every chooser request in that session is served by the fallback backend — or, while the front-end is
also restarting, by nothing at all, which is what made the A-21 automation unreliable.

**What this is and is not.** The assertion is in GTK's own `gdksurface.c`, inside the portal process;
VisualCat is not in the stack. Ubuntu 22.04 ships `xdg-desktop-portal-gnome 42.1`. Two things do
belong to VisualCat, and both **pass**: the fallback chooser works (§13.7), and dismissing a chooser
with **Escape** — the route a user actually takes — never triggers it.

**Suggested handling.**

1. Report it upstream to `xdg-desktop-portal-gnome` with the assertion and the reproduction above; it
   is a portal bug and needs no VisualCat change.
2. Add it to the plan's **Appendix B** as a trap: *a file chooser that stops appearing mid-run is
   usually the portal having crashed, not the product ignoring the click.* Check with
   `systemctl --user is-active xdg-desktop-portal-gnome` and recover with `reset-failed`.
3. Consider one small product-side defence, since the consequence for the user is a command that
   appears to do nothing: when `OpenFilePickerAsync` returns without a dialog ever appearing, or
   throws a D-Bus error, say so in the notice lane rather than returning silently. That is the same
   honesty [F-22](#f-22) asks for at the other end of the same call.

---

## 15. What remains untested after four passes

This supersedes §5.2 and §10.10. Nothing here is a pass; each row is a coverage gap with its reason.

| Row | Why it is still open | What it needs |
|---|---|---|
| ~~A-21~~ | — | **Closed in §17.** Cases 1 and 4 pass; cases 6 and 7 reproduce the predicted hole → [F-31](#f-31). Cases 2, 3 and 5 were not run: they exercise the same detection path cases 1 and 4 already prove |
| **A full run on a second distribution** | §10.2 established the glibc floor at **2.28** by running the CLI on Debian 10/11/12, but only `--version`. No desktop session, no B tier | Debian or Fedora with a graphical session |
| **Metal, a real GPU, and every §4.2 performance budget** | This host is confirmed software rendering (§7.18) | A physical Linux host. **No performance number in this report is a baseline** |
| **ADB `no permissions` / `unauthorized` / `offline`, `udev`, group membership (A-16)** | The guest is a client of the host's ADB server (§1.5) | A device attached directly to the Linux host |
| **Orca end-to-end (U-07)** | The AT-SPI tree was inspected (§2.15) but nobody listened | A human through the B-19 journey with Orca 42.0 |
| **G4 desktop-environment matrix (U-05, U-26)** | GNOME only; G0 and G1 both covered (§7.17) | KDE Plasma, Xfce, and one tiling WM |
| **Multi-monitor (U-03, U-04)** | Single 1× output. U-02/U-11 scaling is now covered (§13.6) | A second output, ideally at a different scale |
| **A correct ACL denial (P-09)** | Two attempts; a plain `cat` control behaved identically both times (§7.8, §10.8), so the row proves nothing about the product | An ACL the kernel demonstrably enforces |
| **`umask 077` under the desktop** | The CLI leg passes (§10.7) | The same check driven through the GUI |
| **Quota, read-only remount, low disk (L3, X-15)** | Not executed | A dedicated loop-mounted filesystem |
| **Soak (X-03, X-05–X-09, X-20–X-23, X-28)** | Needs a dedicated host and 30–50 h | Dedicated hardware |
| **Android leg of I-08** | The Windows leg passes in both directions (§13.4) | The companion app on the phone |
| **Lone-CR offsets** | The input is refused (§2.17 item 3) | n/a — the plan expectation needs softening first |

## 16. Fourth-pass cleanup and host hand-back

| Step | Verified |
|---|---|
| Product processes stopped | `VisualCat` 0 · `vcat` 0 |
| Stray portal choosers dismissed **with Escape** (never the window manager, per [F-30](#f-30)) | 0 remain |
| `text-scaling-factor` restored | `1.0` |
| `idle-delay` / lock timers | `uint32 300`, unchanged |
| Portal services | `xdg-desktop-portal` active · `xdg-desktop-portal-gnome` active |
| Fourth-pass scratch removed | `a21b`, `u02`, `a29`, `parity`, and the `/tmp` corpora |
| `/var/crash` | cleared — the one entry was `xdg-desktop-portal-gnome`, Signal 6 ([F-30](#f-30)) |
| Mutation ledger | **0** open rows |
| The account's own data | 4 sessions, untouched |
| Free space | 163 GiB — unchanged across all four passes |

`~/vcat-run` retains 532 MB: both candidates, the release tarballs, the corpora, the evidence index
and the closed ledger. The v2.0.12 artifacts fetched for §13.5 were removed with the rest of the
`a29` scratch.

### 16.1 Tally after four passes (superseded by §19.1)

| | |
|---|---|
| Passes | 4 |
| Findings | **30** — 7 Major, 13 Minor, 10 Polish |
| Plan rows attempted | ~70 across tiers B, A, X, U, I, P and R |
| Release exit criteria satisfied in this run | 4 (cross-platform parity — **fails** on [F-29](#f-29)), 10 (upgrade — **passes**), 13 (capability manifest — reconciled) |
| VisualCat-attributable crashes, hangs, OOM kills or core dumps | **0** |
| Host-component crashes recorded and classified | 3 (all `xdg-desktop-portal-gnome`, [F-30](#f-30)) |
| Findings withdrawn after a control disproved them | 3 (§12.1) |

---

## 17. Fifth pass — A-21 completed, including both holes the plan predicted

§15 named A-21 the highest-value open row and [F-30](#f-30) the reason it was blocked. It is now
substantially complete. The unlock was two realisations: the portal survives **Escape** but not the
window manager's close, and the two cases the plan most wants are precisely the ones that do **not**
terminate the follow — so both run on a single chooser interaction.

Navigating the chooser by **clicking** (Home → the file → *Select*) also proved far more reliable
than typing a path, which is the plan's Appendix B #7 trap seen from the other side.

### 17.1 Finding added by this pass

| ID | Severity | One line |
|---|---|---|
| [F-31](#f-31) | **Major** | After an ordinary `logrotate`, *Follow file* keeps reading the rotated-away inode while naming the live path — 93 records written to the file it claims to be following never appear, and it reports no source change at all |

### 17.2 A-21 · Results

| Case | Shape | Detected? | Result |
|---|---|---|---|
| **1** | `truncate -s 0` in place (inode kept) | **yes** | `Streaming` → `Failed`, `sourceChanges: 1`, entries preserved 19 → 31. Notice: *“The followed file was truncated or rotated; the configured policy is to st…”* **PASS** |
| **4** | `rm` with no replacement | **yes** | `Streaming` → `Failed`, `sourceChanges: 1`, entries preserved 16 → 21. Notice: *“The followed file was removed.”* **PASS** |
| **6** | rotate, replacement **at least as long** | **no** | → [F-31](#f-31) |
| **7** | the orphaned inode keeps being written | **no** | → [F-31](#f-31) |
| 2, 3, 5 | truncate to a shorter non-zero length · `mv` + a shorter new file · writer killed between appends | — | not run; they exercise the same shorter-or-missing-path detection that cases 1 and 4 prove |

**The notice distinguishes the two detected shapes**, which is exactly what the plan asks: *removed*
reads differently from *truncated or rotated*, and in both cases everything committed before the
change is kept and the session records the change in its defect counters.

### 17.3 The declared contract, and where it holds

`rotationPolicy = stop` is honoured wherever the detection can see the change: the follow loop
notices only when a read returns nothing **and** the *path* is then missing or shorter than what has
already been delivered. Cases 1 and 4 are exactly those two shapes, and both work.

[F-31](#f-31) is the complement — the two shapes where neither condition is ever true.

---

## 18. Finding from the fifth pass

### F-31 · Major · After an ordinary `logrotate`, Follow keeps reading the rotated-away inode while naming the live path

**Severity** Major — silent, total data loss for the followed source, with a status line that names
the right file while reading the wrong one. `logrotate` without `copytruncate` is the default on most
distributions, so this is the ordinary way a Linux log gets rotated, not an exotic case.

**What was done.** A live follow over `~/hole.log`, streaming normally (`state: Streaming`,
`sourceChanges: 0`). Then a textbook rotation — `mv` the file aside and create a replacement — with
the replacement made **longer** than the original, which is what happens whenever the new file fills
up faster than one poll interval:

```shell
mv ~/hole.log ~/hole.log.1          # old inode 1835155, 1174 bytes
{ …90 records… } > ~/hole.log       # new inode 1872131, 4429 bytes  (longer)
```

**What happened.**

| Measure | Value |
|---|---|
| session state after rotation | **`Streaming`** — unchanged |
| `defects.sourceChanges` | **0** — no change recorded |
| entries in the session | **16**, unchanged |
| records written to the **file at the followed path** | 93 |
| of those, records that reached the session | **0** |
| status line | **`Capturing · 27 lines received · no source lines for 35s · /home/benny/hole.log (follow)`** |

The product is still following **inode 1835155**, which no longer has that name. The file now called
`/home/benny/hole.log` is inode 1872131, and everything written to it is invisible.

**Proof that it really is reading the orphan** (case 7). With the follow still running, records were
appended to the *old* inode under its new name, `~/hole.log.1`:

| Written to | Reached the session |
|---|---|
| `~/hole.log.1` — the rotated-away inode | **yes** (`ORPHAN` records arrived; entry count 16 → 27) |
| `~/hole.log` — the live file at the followed path | **no** (0 of 93) |

So reads keep succeeding, which is why the removal is never noticed: on Linux an open descriptor
keeps the inode alive and readable after its last name is gone. The length comparison cannot help
either, because the replacement is longer, not shorter.

**Expected** — plan A-21: *“the product must not present a stalled or orphaned follow as a healthy
one — the quiet heartbeat from A-22 is the minimum honest outcome, and **silently describing the old
inode as the file at that path is the finding**.”* The heartbeat is present (*“no source lines for
35 s”*), so the minimum is met; naming `/home/benny/hole.log` while reading another inode is not.

**Suggested fix.** The follow loop already re-resolves the path when a read returns nothing; the gap
is that it compares **length** rather than **identity**. Compare the inode instead — it is one
`stat` per poll and it closes both cases at once:

```csharp
// A rotation whose replacement is at least as long leaves the length comparison blind, and an
// unlinked inode keeps reading happily, so length can never see either case. Identity can. (A-21/F-31)
var onDisk = new UnixFileStatus(path);           // st_dev + st_ino
if (onDisk.Missing)                   { RaiseSourceChange(SourceChange.Removed); }
else if (onDisk.Identity != openIdentity) { RaiseSourceChange(SourceChange.Rotated); }
else if (onDisk.Length < deliveredBytes)  { RaiseSourceChange(SourceChange.Truncated); }
```

`st_dev`+`st_ino` captured when the file is opened, re-stat'ed on the same poll that already happens
every 250 ms. Two details worth getting right:

1. **Check identity even when the read returned data**, not only when it returned nothing — case 7's
   whole point is that reads keep succeeding from the orphan.
2. Keep the three outcomes distinct in the notice, since §17.2 shows the product already words
   *removed* and *truncated or rotated* differently and users will rely on that. A rotation deserves
   its own sentence: *“The followed file was replaced (rotated); the configured policy is to stop.”*

Then add the two shapes to the test suite directly — they are cheap to express: rotate with a longer
replacement, and append to the unlinked inode, asserting a source change is raised in both.

**Appendix-B trap checks.** Not the poll interval (#25) — 35 s is 140 poll intervals. Not [F-09](#f-09)'s
withheld tail — that accounts for a handful of records, not 93, and the entry count never moved.
Inodes recorded before and after (1835155 → 1872131). Not a permissions problem: the same user wrote
both files.

---

## 19. Fifth-pass cleanup

| Step | Verified |
|---|---|
| Product processes stopped | `VisualCat` 0 |
| Portal choosers dismissed **with Escape** | 0 remain; `xdg-desktop-portal` and `-gnome` both **active**; `/var/crash` **0** entries — no new [F-30](#f-30) crash this pass |
| `idle-delay` / `idle-activation-enabled` re-disabled for the pass, then restored | `uint32 300` / `true` |
| Scratch removed | `~/vcat-run/a21c`, `~/hole.log*`, `~/rm-case.log`, `/tmp` corpora — **0** stray logs in `$HOME` |
| Mutation ledger | **0** open rows |
| The account's own data | 4 sessions, untouched |
| Free space | 163 GiB — unchanged across all five passes |

The screen blanked mid-pass because the fourth-pass cleanup had correctly restored the 300 s idle
timer; it was re-disabled under a fresh ledger row and restored again. That is plan Appendix B #3
behaving exactly as documented, and it is worth remembering that **restoring the timer between
passes makes the next pass hit it**.

### 19.1 Revised tally

| | |
|---|---|
| Passes | 5 |
| Findings | **31** — 8 Major, 13 Minor, 10 Polish |
| Plan rows attempted | ~75 across tiers B, A, X, U, I, P and R |
| A-21 | cases 1 and 4 **PASS**; cases 6 and 7 **FAIL** ([F-31](#f-31)); cases 2, 3, 5 not run |
| Release exit criteria touched | 3 (no open Major — **fails**, 8 open), 4 (cross-platform parity — **fails** on [F-29](#f-29)), 8 (accessibility — **fails** on [F-11](#f-11)), 10 (upgrade — **passes**), 13 (capability manifest — reconciled) |
| Security/privacy boundary rows (P-03, P-04, P-06, P-07, P-08, P-10, P-11, P-12, P-16, P-22) | boundaries **held**; the one gap is [F-21](#f-21), an expansion bound rather than an escape |
| VisualCat-attributable crashes, hangs, OOM kills or core dumps | **0** across all five passes |
| Findings withdrawn after a control disproved them | 3 (§12.1) |

---

# Part II — Remediation

## 20. Remediation pass — restore point

This part is written by the **remediation run** that follows the five test passes above. Its job is
to implement the suggested fix for every finding F-01…F-31, live-test each on the same Ubuntu guest
and the same physical phone, and record the result beside the finding it closes.

It is written continuously, the same way Part I was: this section is rewritten after every step, so
an interrupted run resumes from the last line here without re-deriving anything.

### 20.1 Restore point

| Field | Value |
|---|---|
| Run ID | `20260912-linux-remediation` |
| Status | **COMPLETE** — 30 of 31 findings closed and live-verified, 1 closed with a stated limit, 1 open upstream; §21 adds a sixth pass that closed six and a half standing §15 rows and found [F-32](#f-32) |
| Branch | `main` (working tree; commits are made per batch) |
| Repo | `E:\VisualCat` on the Windows host |
| Guest | `benny@172.24.178.166` (VMware, key auth; `. ~/vcat-run/env.sh`) |
| Phone | Samsung SM-G990B `RFCRC0A9GND`, PIN `1111`, **lock the screen when finished** |
| Last completed | §21.13 — Orca run end to end for the first time; F-32 recorded (upstream), the window-focus half fixed and pinned |
| Next step | None reachable on this host. [F-11](#f-11) and [F-32](#f-32) need an Avalonia change; §21.9 lists the §15 rows that need hardware |

### 20.2 Plan — batches, and why in this order

The 31 findings are implemented in dependency order: the parser first (everything downstream reads
its output), then the CLI and infrastructure seams the CLI exercises, then the desktop shell, then
documentation. Each batch is built and unit-tested on the Windows host, then packaged as a
`linux-x64` tarball and exercised live on the guest before the next batch starts.

| Batch | Findings | Area |
|---|---|---|
| **A** | F-01 | `LogcatParser.TryLong`, generator, fixtures |
| **B** | F-07, F-06, F-29, F-25, F-28, F-26, F-18, F-19, F-20 | CLI contract, signals, data root, lease ordering, verifier |
| **C** | F-09, F-31, F-24, F-21, F-27 | follow loop, leases, archive extractor, file modes |
| **D** | F-02, F-03, F-04, F-05, F-08, F-10, F-11, F-12, F-13, F-14, F-15, F-16, F-22, F-30 | desktop shell and dialogs |
| **E** | F-17, F-23, and the documentation halves of F-03, F-24, F-27 | runtime residue and docs |

### 20.3 Progress ledger

| Finding | Severity | Implemented | Unit test | Live-verified on the guest |
|---|---|---|---|---|
| F-01 | Major | yes | yes | **yes** — 4,000/4,000 on the phone, confidence 1.000 |
| F-02 | Minor | yes | yes | **yes** — exit 69, one message, no core dump, 3 routes |
| F-03 | Minor | yes | yes | **yes** — the command, the words, and the browser hand-off (§21.1) |
| F-04 | Minor | yes | yes | **yes** — *Waiting for import options…*, still true after 30 s |
| F-05 | Polish | yes | yes | **yes** — a confident file imports without a review |
| F-06 | Minor | yes | yes | **yes** — explained, and a missing file still says missing |
| F-07 | Minor | yes | yes | **yes** — `--` works; extra positionals refused by name |
| F-08 | Polish | yes | yes | **yes** — accent primary action in both reviews |
| F-09 | Major | yes | yes | **yes** — 120/120 published within 5 s |
| F-10 | Major | mitigation | n/a — needs a display server | **yes** — 5/5 clean, 0.114% against 15–31% |
| F-11 | Major | no — upstream | n/a | **no** — 434 nodes still reachable; no Avalonia lever exists |
| F-12 | Minor | yes | yes | **yes** — closes 3 of 3 dialogs |
| F-13 | Polish | yes | yes | **partly** — app name and controls yes; the structural names are upstream and now tracked as [F-32](#f-32) |
| F-14 | Polish | yes | yes | **yes** — no phone words, no phone control, *CSV encoding* |
| F-15 | Minor | yes | yes | **yes** — `222 of 222 shown`, button gone |
| F-16 | Polish | yes | yes | **yes** — `Europe/Prague`, matching the plot |
| F-17 | Polish | yes | n/a — documentation | **yes** — 3 stale swept, 1 live kept, own socket unlinked |
| F-18 | Major | yes | yes | **yes** — 3 signals, 8 runs, all Ready and verifying |
| F-19 | Major | yes | yes | **yes** — created 700; relative value reported |
| F-20 | Major | yes | yes | **yes** — winner verifies at 399,966 |
| F-21 | Minor | yes | yes | **yes** — refused by name, nothing written |
| F-22 | Minor | yes | yes | **yes** — every file command usable again |
| F-23 | Polish | yes | n/a — documentation | n/a — documentation |
| F-24 | Polish | yes | yes | **yes** — 15 markers become 6, survivors open |
| F-25 | Polish | yes | yes | **yes** — 100,001 repeats become one 844-byte report |
| F-26 | Minor | yes | yes | **yes** — reported, not guessed; UTC still askable |
| F-27 | Minor | yes | yes | **yes** — a second account is refused everything |
| F-28 | Polish | yes | yes | **yes** — the four-way matrix |
| F-29 | Minor | yes | yes | **yes** — 0 CR by default, identical after stripping |
| F-30 | Minor | host bug + defences | yes | **yes** — every shape, including masked (§21.2) |
| F-31 | Major | yes | yes | **yes** — cases 1, 4, 6 and 7 all detected and worded apart |
| F-32 | **Major** | no — upstream | n/a | **measured** (§21.13) — 168 nodes, unchanged by both levers; the window-focus half is closed |

### 20.4 Live verification on the guest

Same host as Part I: Ubuntu 22.04.5, GNOME 42.9 on Wayland with the app as an XWayland client,
VMware guest, software rendering. Same physical phone, Samsung SM-G990B `RFCRC0A9GND`, reached
through the Windows host's ADB server exactly as §1.5 describes.

| Field | Value |
|---|---|
| Candidate | built from repository commit `3bd3a01` (the remediation), packaged `pwsh tools/package.ps1 -Runtime linux-x64 -Archive` |
| Desktop tar.gz SHA-256 | `3bdd4628dcdc351acda84d7f3118d554a80386dc041be1e5e101b1cd3649823d` (45,481,545 B) |
| CLI tar.gz SHA-256 | `2c31a24d7eb03dda939e4aac0a258ef4cefa7fdf35254660b570d4e05564ebe2` (35,908,659 B) |
| `VisualCat` SHA-256 | `377cf365e40f29578dbbcd29759ebd94f15ee52366210538483b2609b6812884` |
| `vcat` SHA-256 | `2fc977f91b9f6b8db7cd3747f6c1005c8b8004720a724d4175973bb67195736a` |
| Version as reported | `vcat 2.0.13+3bd3a0105eed4123636d9412cf6bcdc5eab64093` |
| `<FIX>` | `~/vcat-run/candidate/fix-20260912/{desktop,cli}` |
| Environment | `. ~/vcat-run/env-fix.sh` — same shape as `env.sh`, pointing at the fixed candidate |
| Packaged on | Windows, so the tarball carries no executable bit — plan Appendix B #1, corrected with `chmod +x` and not a finding |

Unit coverage on the Windows host before any of this: **1,071 tests, 0 failures** across
`VisualCat.Domain.Tests` (47), `VisualCat.Core.Tests` (150), `VisualCat.Application.Tests` (180)
and `VisualCat.App.Tests` (694).

#### [F-01](#f-01) · `logcat -v long` and five-digit thread ids — **CLOSED**

The report's own four-record reproduction, byte for byte:

```shell
vcat index long-shapes.txt --format long --output long-shapes.vcat
# entries 4   unknown 0   format LongFormat   confidence 1
# counters: parsedEntries 4, timedEntries 4, continuations 4, ignoredBlanks 4, templates 4
```

Four of four, where the run measured **two**. The four continuations are the four message
lines, which is what a long-format record is made of.

**On the same phone.** 4,000 records, `adb -s RFCRC0A9GND logcat -d -v long -b main -t 4000`:

| Measure | Part I | Now |
|---|---|---|
| header lines with a 5-digit tid | 472 of 4,001 | **891 of 4,050** |
| `parsedEntries` | 3,528 | **4,000** |
| records lost | **472** | **0** |
| `unknownLines` / `rejectedCandidates` | 0 / 0 | 0 / 0 |
| auto-detection confidence | **0.449** — refused | **1.000** |
| auto-detected format | — | `LongFormat` |

The 50 bracketed lines that are not headers — `[Global GC RCS|IDLE] checkForTryRegister id: 1244`,
`[2125]> 140`, `[LOWI-Scan] lowi_close_record:…` — are message bodies and are still counted as
continuations, which is correct: none of them opens on a digit or carries a `P/Tag` separator, so
the new *is this an attempted header* test declines them. No line in this capture became a
rejected candidate, because none of them is a malformed header.

Detection now reaches 1.000 rather than the structural ~0.5 ceiling because a format is scored
against the lines it is responsible for, and against the best score that format can reach — see
§20.5.

#### [F-02](#f-02) · A desktop with no display — **CLOSED**

All three routes, each run against the fixed candidate on the guest:

| Route | Part I | Now |
|---|---|---|
| `env -u DISPLAY -u WAYLAND_DISPLAY ./VisualCat` | exit 134 (`SIGABRT`), stack trace **twice** | **exit 69**, one message, no trace |
| `DISPLAY=:77 ./VisualCat` | identical abort | **exit 69**, message names both variables |
| `libX11.so.6` shadowed | `DllNotFoundException`, twice, abort | **exit 69**, message names the library |

Verbatim, with no `DISPLAY`:

```
VisualCat needs a graphical X11 or XWayland session, and could not open one.
  DISPLAY=(not set)   WAYLAND_DISPLAY=(not set)
  On Debian or Ubuntu the packages are: libx11-6 libice6 libsm6 libfontconfig1
  On Fedora or RHEL: libX11 libICE libSM fontconfig
  Over SSH, connect with -X or -Y so a display is forwarded.
  With no display at all, use the vcat command line instead — it needs none,
  and it is in the VisualCat-CLI archive beside this one.
```

`grep -c 'at Avalonia\|at VisualCat\|Unhandled exception'` over the output: **0**.
`/var/crash` after all three: **0 entries**, where an abort would have written one.

#### [F-03](#f-03) · The desktop has an update route — **CLOSED**

`More ▾` on the Linux desktop now lists six items, ending in **Check for updates…**, where
Part I found five and no update affordance anywhere. Pressing it puts this in the notice lane:

> VisualCat for the desktop updates by downloading a new archive; it cannot update itself.
> Releases are published on GitHub.

— desktop words, naming no store this platform does not have. `SUPPORT.md` now describes that
command instead of one the desktop did not have.

**Not exercised:** the browser hand-off itself. The notice's *Open releases* action could not be
located through AT-SPI to click it, so whether `xdg-open` opens Firefox on this guest is
unverified; the code path is unchanged by this work and shows the URL in the lane when the
launcher refuses.

#### [F-04](#f-04) · The file-operation card — **CLOSED**

With the import review open over a 3,000-line file, the card reads **`Waiting for import
options…`** and stays that way — checked again after 30 seconds — where Part I measured
`Copying file…` with an animating bar for more than four minutes. The process holds **0**
descriptors on the source while the review waits, which is what made the old wording untrue.

#### [F-05](#f-05) · The two open commands differ — **CLOSED**

*＋ Open log* on a confidently detected file opened the session directly: `Import preview`
windows **0**, tab `quick.txt` present. *More → Open log with options…* on the same file opened
the review. Two commands, two behaviours, where Part I found one behaviour under two names.

#### [F-06](#f-06) · A name that is not valid UTF-8 — **CLOSED**

```shell
cd ~/vcat-run/corpus && vcat info "$(printf 'latin1-name-\xE9.bin')"
# error: This path is not valid UTF-8, so VisualCat cannot address the file it names:
#        latin1-name-<undecodable byte>.bin. The file may well be there and readable — the name
#        simply cannot survive the trip through a text argument. Rename it to a UTF-8 name and
#        try again, for example: cd "/home/benny/vcat-run/corpus" && mv -- *.bin renamed.bin
```

A genuinely missing file still says `Log source was not found.`, so the two cases are told
apart rather than conflated.

#### [F-07](#f-07) · The POSIX `--` separator — **CLOSED**

| Invocation | Result |
|---|---|
| `vcat index -- small.txt` | exit 0, 1,000 entries |
| `vcat index -- ./-report.txt` | exit 0, 1,000 entries — a name beginning with `-` now has a spelling |
| `vcat index -- small.txt --output /tmp/x.vcat` | **exit 2**, named: `'index' takes 1 argument before its options, but got 3; unexpected: '--output', '/tmp/x.vcat'. Everything after a bare '--' is treated as a file name, never as an option.` |

The third is the plan's own §2.3 spelling. Under POSIX rules those two tokens are operands, and
the old behaviour would have silently written the session beside the log instead of where it was
asked; naming them is what makes the separator safe to use.

#### [F-08](#f-08) · The confirming action is drawn as one — **CLOSED**

*Import* and *Choose a file…* are filled accent buttons, plainly distinct from the *Cancel*
beside them; the same treatment now applies to *Apply*, *Save policy*, *Done* and the
confirmation dialogs. Padding is left to the theme so a decision row still keeps to one line.
The review also reads *Detected sample format: Thread time (100 % confidence)* and *No preview
warnings*, which is [F-01](#f-01)'s scoring change showing through.

#### [F-09](#f-09) · A followed file that goes quiet — **CLOSED**

The report's own reproduction: seed the file, follow it, append 120 records at 0.3 s intervals,
stop the writer.

| Moment | Part I | Now |
|---|---|---|
| t + 5 s | 107 of 120 | **122 lines received** — 1 meta + 1 seed + **120** |
| t + 20 s | 107 | 122 |
| t + 45 s | 107 | 122 |
| t + 75 s | 107 | 122 |

And on disk, not merely received: `vcat query <session> | grep -c RUN=F09` → **120**, against
120 in the file.

#### [F-10](#f-10) · Unpainted regions on XWayland — **CLOSED on this host**

Same measurement the finding defines, five openings of *Appearance & timeline*:

| attempt | Part I | Now |
|---|---|---|
| 1 | 15.5 % | **0.114 %** |
| 2 | 30.8 % | **0.114 %** |
| 3 | 30.8 % | **0.114 %** |
| 4 | 15.5 % | **0.114 %** |
| 5 | 0.1 % | **0.114 %** |

0.114 % is the dialog's own genuinely-black pixels, identical on every attempt. The main window
measures 0.0019 % across five grabs, the export review 0.31 %, the import review 0.12 %, and the
export review's *Row order*, *Encoding* and *Line endings* are all on screen — the band that hid
two of them is gone. The empty state draws its hero block once.

This is the mitigation working, not the toolkit defect being fixed: `VISUALCAT_FULL_REPAINT=0`
turns it off for anyone measuring the underlying behaviour, and the upstream report still stands.

#### [F-11](#f-11) · A modal dialog is not modal to assistive technology — **STILL OPEN, upstream**

With *Appearance & timeline* open and modal to the pointer, measured through `pyatspi` with
`toolkit-accessibility` on:

```
[frame] 'VisualCat v2 — See the shape of your log'  {ACTIVE,SHOWING,VISIBLE,SENSITIVE}   127 nodes
[frame] 'Appearance & timeline'                     {ACTIVE,SHOWING,VISIBLE,SENSITIVE}   465 nodes
```

Unchanged: role `frame` rather than `dialog`, no `MODAL` state, both `ACTIVE`, and the whole
workspace still reachable — 434 of its nodes `SHOWING` on a session-open window, exactly the
number Part I counted.

**Two approaches were tried on this host and moved the measurement by nothing.**
`AutomationProperties.SetAccessibilityView(owner, Raw)` hides one node and promotes its children,
so the subtree stays; `SetIsOffscreenBehavior(owner, Offscreen)` is not consulted by the AT-SPI
backend at all. Avalonia 12.1.1 has no `Dialog` member in `AutomationControlType`, emits no modal
state, and offers no subtree-exclusion property — so there is no product-side lever. The code
that set those properties has been removed rather than left looking like a fix, and the reason is
recorded at the call site so the next reader does not repeat it.

**What would close it** is upstream, in `Avalonia.FreeDesktop.AtSpi`: map an owned modal window
to role `dialog` with `STATE_MODAL`, and drop `SHOWING` from the owner's subtree while it is up.

#### [F-12](#f-12) · Escape dismisses a dialog — **CLOSED**

| Dialog | Part I | Now |
|---|---|---|
| *Appearance & timeline* | stayed open | **closes** |
| *Session cache* | not measured | **closes** |
| *Lines not on the timeline* | not measured | **closes** |

It took two attempts to close properly, and the second attempt found a larger defect than the
finding describes. A bubbling handler closed the first two and did nothing for the third, because
the selectable text in it takes Escape to clear its selection. Handling Escape on the **tunnel**
fixed that and still did nothing — and the reason was that *Lines not on the timeline* opened
with **no keyboard focus anywhere inside it**. Avalonia raises key events on the focused element,
so with focus left on the workspace button that opened the dialog, nothing routed at all: not
Escape, not Tab, not the default button. That dialog was not reachable from the keyboard by any
key. Every dialog now takes focus on its first focusable control when its body has not chosen
one, and an open dropdown is the single thing that still owns Escape ahead of the dialog.

**A trap this exposed**, worth the plan's Appendix B: under XWayland, `xdotool getactivewindow`
returns nothing and `xdotool key` without a target therefore goes nowhere, which made a working
Escape look broken. Address the window: `xdotool windowfocus <id>` then `xdotool key Escape`, or
`xdotool key --window <id>` for a toolkit that accepts `XSendEvent`. GTK's own file chooser does
not, so the portal chooser needs `windowfocus`.

#### [F-13](#f-13) · What the accessibility bus is told — **CLOSED for the application and its controls**

The bus now lists **`VisualCat`** beside `gnome-shell` and the rest, where Part I found
`Avalonia Application`. With a session open, no control in the workspace is named after its type:

| Node | Part I | Now |
|---|---|---|
| search field | `TextBox` | **`Search message text or regex`** |
| plot/details splitter | `GridSplitter` | **`Resize the plot and the entry list`** |
| list/insights splitter | `GridSplitter` | **`Resize the entry list and the insights pane`** |

**Not closed:** the structural nodes. Every `panel` still carries its control's type name —
`Border`, `Grid`, `StackPanel`, `ContentPresenter`, `WindowChrome`, `VisualLayerManager` — because
the AT-SPI backend falls back to the type name when a control has no automation name. Orca skips
`panel` roles, so this is the cosmetic half the finding already called cosmetic, and it is the
same upstream component as [F-11](#f-11).

#### [F-14](#f-14) · Desktop words in a desktop dialog — **CLOSED**

*Appearance & timeline* on the Linux desktop now reads **"Multiplies this computer's own text
size setting."**, the checkbox is *Snap timeline cells to screen pixels*, and *Phone plot and
details split* — with its portrait/landscape/Split-mode paragraph — is not present at all.
*Normalized CSV encoding* is *CSV encoding*, with *CSV line endings* beside it.

#### [F-15](#f-15) · *Lines not on the timeline* — **CLOSED**

Over a rebuilt 1,000,122-line corpus carrying 222 lines that cannot be on a timeline, scattered
through the whole file. The chip reads `222 lines are not logcat records and are not on the
timeline. Show them.`, and the dialog's footer, after one open:

```
222 of 222 shown · every line off the timeline has been listed.
```

Both numbers, related to each other, and the *Load 500 more* button is gone because there is
nothing more to load. Part I's footer read `45 shown · more of the file remains to be scanned.`
and stayed there while the button added 26 rows, then 0.

#### [F-16](#f-16) · The export review's zone — **CLOSED**

A 25-second ADB capture from the same phone, whose stored policy zone is `UTC` because that is
what the capture negotiated, opened in a workspace presenting `Europe/Prague`. The review now
states:

```
12,090 timed rows · Europe/Prague · No filters
```

Part I's review said `UTC` beside a plot header two hours away from it.

#### [F-17](#f-17) · Runtime diagnostic sockets — **CLOSED**

Four sockets planted in `/tmp` and aged an hour — three naming PIDs that cannot exist, one naming
a live shell — then one ordinary `vcat` run:

| | |
|---|---|
| before | 4 |
| after one run | **1** — the live one |
| a running `vcat`'s own socket, while it runs | present |
| the same socket, after it exits | **gone** |

#### [F-18](#f-18) · An interrupted command — **CLOSED, and the finding was understated**

Part I compared `SIGTERM` against `SIGINT` and called `SIGINT` a pass. It was not a control: a
`kill -INT` on a background job of a **non-interactive** shell reaches nothing, because POSIX
requires such a shell to set `SIGINT` to `SIG_IGN` for its asynchronous children. An index
signalled 0.8 s into a 30 s run finished all 3,000,003 lines and exited 0 — Ctrl+C appeared to
work by doing nothing. With the disposition reset before `exec`, all three signals behave alike:

| Signal | Part I | Now (stopped 1 s into a 30 s index of 3,000,003 lines) |
|---|---|---|
| `SIGINT` | exit 0, `Ready`, whole file — because the signal was swallowed | exit 0, `Ready`, **710,532 entries**, `vcat verify` **exit 0**, `rawVerified true` |
| `SIGTERM` | exit 143, `Importing`, `isValid false`, **1 entry** | exit 0, `Ready`, **850,196 entries**, verify **exit 0**, `rawVerified true` |
| `SIGHUP` | not measured | exit 0, `Ready`, **1,083,088 entries**, verify **exit 0**, `rawVerified true` |

Six further runs at 0.8 s and 2 s across the three signals, all exit 0, all `Ready`, all verify
clean. A **second** signal gives up on finishing: exit 130, `Cancelled.` on stderr, state
`Cancelled` — the documented escape hatch rather than the default.

Closing it took two passes. The first made `SIGTERM` cancel cooperatively, which turned exit 143
into exit 130 and `Importing` into `Cancelled` — better, and still not what I-13 asks, because a
hard cancel leaves a partial session that fails verification. The second made a terminating
signal a **graceful stop**, and then the session still failed with `declared outcomes cover
85,983,232 bytes; source has 270,072,092`: it was recording the whole file's length and digest,
which is evidence it does not own. A stopped import now records the prefix it actually read,
which the store already models because a growing source needs the same thing.

#### [F-19](#f-19) · A data root that does not exist — **CLOSED**

```shell
XDG_DATA_HOME=/tmp/f19-root/nested/deeper vcat index small.txt --output /tmp/f19.vcat
#   exit 0, 1,000 entries
#   created /tmp/f19-root/nested/deeper/VisualCat, mode 700
```

Part I got `error: Session lease storage is unavailable.` and exit 1, and the desktop said a
session was missing. A relative value is still ignored, as the specification requires, and no
longer silently:

```
warning: XDG_DATA_HOME='relative-nonsense' is a relative path, which the XDG specification does
not allow, so it was ignored. VisualCat is using '/home/benny/.local/share/VisualCat'.
```

#### [F-20](#f-20) · Two writers, one session — **CLOSED**

Two `vcat index --force` processes over a 400,000-line log, staggered by 0.4 s:

| | Part I | Now |
|---|---|---|
| loser | exit 1, `This capture is in use.` | exit 1, same message |
| winner | **exit 0** | exit 0 |
| `vcat verify` afterwards | **`isValid false`**, `source.records.missing` | **exit 0, `isValid true`, 399,966 entries** |

The uncontended control verifies at the same 399,966.

#### [F-21](#f-21) · An archive carrying something that is not a session — **CLOSED**

The bomb was rebuilt to the finding's shape: a legitimate 32-member archive (62,546 B) plus
`bomb.bin` declaring 1 GiB from 1,043,638 B stored, a 200-directory-deep path, and a
240-character name — 1,107,738 B on disk. Opened through the portal chooser:

> Could not open the portable archive · Portable archive contains an entry that is not part of a
> session: bomb.bin.

Afterwards, under the data root: `bomb.bin` **0**, `*.extract-*` leftovers **0**, and nothing
1 GiB-shaped. Part I wrote all 1,073,741,824 bytes of it into the product's own data root.

#### [F-22](#f-22) · Cancel on a chooser operation — **CLOSED**

*Open archive* → the portal chooser opens → the shell's **Cancel**:

| Observation | Part I | Now |
|---|---|---|
| status | `Cancelling…` at t+3, 15, 40 and 90 s | **`Choosing portable archive cancelled.`** |
| *Open log*, *Open session*, *Follow file*, *Open archive* | all disabled | **all enabled** |
| the abandoned chooser | still open | still open — and it no longer holds the slot |

#### [F-24](#f-24) · Lease markers — **CLOSED**

Five sessions under a throwaway data root, three of them then deleted:

| | |
|---|---|
| markers after five sessions | 15 (three per session) |
| after deleting three sessions | 15 |
| after the next session is opened | **6** — the two that still exist |
| both survivors still open cleanly | yes |
| lease directory mode | **700** |

An `.intent` marker records the session it belongs to, in plain text with no byte-order mark,
because the file name is a hash and cannot say.

This also took two passes. The first swept any marker no process was holding, which is safe in
principle — a marker's existence is not the lock — and unsafe in practice: unlinking a name
another process is between opening and locking refuses it a lease it should have had, and a
cross-process deletion test caught exactly that. A spurious *this capture is in use* is a much
worse thing to be wrong about than a stray zero-byte file.

#### [F-25](#f-25) · Repeated verification issues — **CLOSED**

A 400,000-line session with its `source-order/index.bin` scrambled through the middle:

| | Part I | Now |
|---|---|---|
| repeated `source.index` findings | ~380 identical objects | **1 object, `occurrences: 100,001`, `sample` of 5** |
| report size | several screens | **844 bytes** |
| verdict | exit 3, `isValid false` | exit 3, `isValid false` — unchanged |

#### [F-26](#f-26) · A zone the system cannot resolve — **CLOSED**

| Case | Result |
|---|---|
| control, `tzdata` present | manifest `timeZoneId: Europe/Prague` |
| `TZDIR=/nonexistent TZ=Europe/Prague` | **exit 1**, message names the zone, `tzdata`, `/usr/share/zoneinfo`, and the `--timezone UTC` escape; **no session written** |
| `TZDIR=/nonexistent --timezone Europe/Prague` | **exit 1**, same shape |
| `TZDIR=/nonexistent --timezone UTC` | **exit 0** — asking for UTC still works with no database |

Part I's third pass got exit 0, `"timeZoneId": "UTC"`, and a healthy-looking session whose every
instant was off by the offset.

#### [F-27](#f-27) · Session file modes — **CLOSED, with a second account to prove it**

With the account's own `umask 0002`:

| Object | Part I | Now |
|---|---|---|
| fresh data root | — | **700** |
| lease directory | — | **700** |
| session directory | 775 | **700** |
| segment directory and its `bitmaps` | 775 | **700** |
| portable session directory | 775 | **700** |
| portable `raw.log` | **664** | **600** |
| portable `.vcat.zip` | — | **600** |

A second local account, `vcatprobe`, against a session written into a world-writable `/tmp`
directory:

```
list the session : refused
read the manifest: refused
read raw.log     : refused
write into it    : refused
```

The containing directory is still `drwxrwxrwx`, so it is the session's own mode carrying this,
not the directory around it.

#### [F-28](#f-28) · Verified versus could-not-be-verified — **CLOSED**

| | exit | `isValid` | `rawVerified` |
|---|---|---|---|
| source present | 0 | true | **true** |
| source present, `--require-raw` | 0 | true | true |
| source deleted | 0 | true | **false** |
| source deleted, `--require-raw` | **4** | true | false |

Part I's two rows were indistinguishable to a script reading `$?`.

#### [F-29](#f-29) · The same session, the same bytes — **CLOSED**

A 400,000-line session exported twice:

| | bytes | CR |
|---|---|---|
| `--type csv` (default) | 42,361,009 | **0** |
| `--type csv --newline crlf` | 42,760,976 | 399,967 |
| identical after stripping CR | **yes** | |
| `templates` JSON, `stats` JSON | — | **0 CR each** |

The default is LF on every platform, chosen rather than inherited. The desktop offers the same
choice: the export review now carries **Line endings** beside **Encoding** and **Row order**, and
its note reads *"A successful export remembers these three choices as the new defaults."*

#### [F-30](#f-30) · A chooser that does not appear — **CLOSED on the product side; the crash itself is a host bug**

The finding is a crash in `xdg-desktop-portal-gnome`, not a VisualCat defect, and the product's
part is to stop looking like a command that did nothing.

| Shape | Behaviour |
|---|---|
| portal **stopped** — what a crash leaves | D-Bus activation restarts it, the chooser appears, Escape dismisses it, every file command is usable again |
| chooser dismissed with **Escape** | portal stays `active`; no crash, across every use in this pass |
| portal **masked** — an administrator action, not a crash | the call blocks with no timeout, and the card says `Choosing portable archive…` with a live Cancel; **Cancel releases it within 1 s** — `Choosing portable archive cancelled.` and every file command enabled again — and the rest of the shell stays fully responsive throughout (the More menu opens, the window repaints at a new size) |
| chooser call that **throws** | the operation ends as though nothing was chosen, with the product's own sentence naming the cause |

**A correction to my own first measurement.** An earlier attempt reported that Cancel did not
release the slot in the masked case. It does, within a second. The first attempt clicked a stale
node: the click helper was matching an accessible name without checking `STATE_SHOWING`, and the
window had been resized between the two runs. Filtering by `SHOWING` and re-reading the extents
immediately before the click — which is plan Appendix B's own advice, and §20.6's — makes it
reproducible. Nothing about the product changed between the two measurements.

#### [F-31](#f-31) · Rotation, and the two shapes the run could not detect — **CLOSED**

A live follow, three records in, then each shape in turn:

| A-21 case | Shape | Part I | Now |
|---|---|---|---|
| 1 | `truncate -s 0` in place | detected | **detected** — *"The followed file was truncated or rotated; the configured policy is to stop. Everything read before the change is kept."* |
| 4 | `rm` with no replacement | detected | **detected** — *"The followed file was removed."* |
| 6 | rotate, replacement **longer** | **not detected**, `sourceChanges: 0`, 93 records invisible | **detected** — *"The followed file was replaced (rotated); the configured policy is to stop. Everything read before the rotation is kept."* (inode 4194959 → 4194949, 7,048 B → 8,659 B) |
| 7 | the orphaned inode keeps being written | **not detected**; `ORPHAN` records arrived while the product named the live path | **detected**; **1** `ORPHAN` record reached the session — the one written inside the 250 ms poll window — and the follow then stopped |

Each shape says a different thing, which is what the plan asks for, and none of the three is
wrapped in a vaguer sentence any more.

### 20.5 What changed beyond the findings

Four things this remediation changed that no finding asked for, because implementing the finding
made the surrounding behaviour visibly wrong.

**Detection confidence measures the wrong thing.** [F-01](#f-01) fixed the parse, and the score
stayed low — because confidence was `matched ÷ every line` times `fields found ÷ 6`. A flawless
`brief` capture carries no timestamp, so it scored 4/6 and sat barely above the 0.6 review
threshold; a healthy `long` file is two thirds message bodies and separator lines by
construction, so it could not exceed about 0.5 and every clean `-v long` capture was permanently
"low confidence". Each format is now scored against the lines it is responsible for, and against
the best score that format can reach. A real device dump went from **0.449 — refused** to
**1.000**, and the import review says *No preview warnings* where it used to warn about a file
with nothing wrong with it.

**A terminating signal means "finish", not "stop dead".** [F-18](#f-18) asked for `SIGTERM` to
behave as `SIGINT` does. Making it cancel was not enough: a cancelled session is partial and
fails `vcat verify`, which is what I-13 asks it to pass. The signal now requests a graceful stop
— stop reading, publish and finalize what is there — and a second signal is the way to give up
on that. Which also means an interrupted index has to record the prefix it read rather than the
whole file's length and digest, or it describes evidence it does not own.

**A dialog has to be able to receive a key.** [F-12](#f-12) asked for Escape. *Lines not on the
timeline* opened with no keyboard focus anywhere inside it, so **no** key reached it — Escape,
Tab, the default button, none of them. Escape was the symptom.

**Members, not megabytes.** [F-21](#f-21)'s expansion bound is a member allowlist rather than a
size cap, because a bomb payload, a 200-directory-deep path and a 240-character name are the
same defect seen three ways: none of them is part of a session. The size and ratio caps are
behind it, for a member with a legitimate name.

### 20.6 Two corrections to Part I's method

Neither changes a finding; both change how a future run should measure.

**`SIGINT` was never tested.** Part I signalled a background job of a non-interactive shell.
POSIX requires such a shell to set `SIGINT` to `SIG_IGN` for its asynchronous children, and an
ignored disposition survives `exec` — so `kill -INT` reached nothing, the index ran to
completion, and exit 0 with a complete session was read as a pass. The control against which
`SIGTERM` was judged a failure was measuring the harness. Reset the disposition before `exec`
(a `preexec_fn` that restores `SIG_DFL`, or an interactive shell with job control) or the result
means nothing.

**`xdotool getactivewindow` returns nothing under XWayland**, so `xdotool key Escape` with no
target goes nowhere and a working Escape looks broken. Address the window: `xdotool windowfocus
<id>` then `xdotool key`, or `xdotool key --window <id>` where the toolkit accepts `XSendEvent`.
GTK's own file chooser does not accept it, so the portal chooser needs `windowfocus`.

Both are now in the plan's Appendix B, alongside the portal-crash and screenshot traps this
remediation added.

### 20.7 Tally

| | |
|---|---|
| Findings | **31** — 8 Major, 13 Minor, 10 Polish |
| Closed and live-verified | **29** |
| Closed with a stated limit | **1** — [F-13](#f-13): the application and its controls yes, the structural `panel` names upstream |
| Not closed | **1** — [F-11](#f-11), for which Avalonia 12.1.1 offers no product-side lever |
| Unit tests | **1,071**, 0 failures — `VisualCat.Domain.Tests` 47, `VisualCat.Core.Tests` 150, `VisualCat.Application.Tests` 180, `VisualCat.App.Tests` 694 |
| New regression coverage | `LinuxLiveTestRemediationTests` (32) and `LinuxLiveTestShellTests` (6), plus the long-format width cases in `ParserTests` and the corpus-width and confidence assertions in `SyntheticLogFormatTests` |
| Release exit criteria moved | 3 (no open Major — **8 of 8 Major closed**, though [F-11](#f-11) is Major and remains open upstream), 4 (cross-platform parity — **passes**, [F-29](#f-29)), 8 (accessibility — **partly**: keyboard and naming yes, AT modality upstream) |
| VisualCat-attributable crashes, hangs or core dumps in this pass | **0** |
| Defects found *by* this remediation, in its own first attempts | 4 — a lease sweep that could refuse a live lease, a `SIGTERM` fix that left an unverifiable session, an `AccessibilityView` change that did nothing, and an Escape handler that could not reach a dialog with no focus |

### 20.8 What this pass did not establish

- **[F-11](#f-11)**, above: needs an Avalonia change, then a re-measurement of the AT-SPI tree.
- **The browser hand-off for [F-03](#f-03)**: the command and its message are verified; whether
  `xdg-open` opens the releases page, and what happens with no handler registered, was not
  exercised.
- **Orca end-to-end**: the tree was inspected again, and nobody listened. Unchanged from §15.
- **Everything else in [§15](#15-what-remains-untested-after-four-passes)** — a second
  distribution, metal and the performance budgets, ADB `no permissions`, the G4 desktop matrix,
  multi-monitor, an enforced ACL denial, `umask 077` through the GUI, quota and read-only
  remount, the soak, and the Android leg of I-08. This remediation exercised the rows the
  findings touch; it was not a re-run of the plan.

### 20.9 Cleanup and host hand-back

| Step | Verified |
|---|---|
| Product processes stopped | `VisualCat` **0** · `vcat` **0** |
| Stray portal choosers dismissed **with Escape** (never the window manager, per [F-30](#f-30)) | **0** remain; `xdg-desktop-portal` and `-gnome` both **active**, neither masked nor failed |
| `org.gnome.desktop.session idle-delay` | restored to `uint32 300` |
| `org.gnome.desktop.screensaver idle-activation-enabled` | restored to `true` |
| `org.gnome.desktop.interface toolkit-accessibility` | restored to `false` |
| `vcatprobe` account created for the [F-27](#f-27) multi-user proof | **removed** with `userdel -r` |
| Candidate binaries and the run's scratch | removed — `~/vcat-run/fixwork`, `~/vcat-run/candidate/fix-20260912`, the `/tmp` corpora, the AT-SPI driver scripts, the transferred tarballs |
| Stray `.vcat` or `.txt` in `$HOME` | **0** each |
| Sessions this pass created in the account's data root | **13 removed**; the account's own **4** from Part I are untouched, byte for byte |
| Data root size | 780 MB during the pass → **100 MB**, its size before it |
| Lease markers | 37 whose session this pass deleted removed by hand; **3 files** remain, one of them a Part I marker written before the recording existed |
| `/tmp/dotnet-diagnostic-*-socket` | **0** |
| `/var/crash` | **0** entries — no core dump from any product process in this pass |
| Free space | 162 GiB, restored |
| Windows host ADB server | back to the default loopback server; firewall rule `VCAT-ADB-5037` **removed** |
| Phone `RFCRC0A9GND` | asleep and **locked** — `mWakefulness=Dozing`, `mDreamingLockscreen=true`, `deviceLocked=1`, `trustState=UNTRUSTED` |
| Mutation ledger | **0** open rows |

### 20.10 Reproducing this

```shell
# on the Windows host
git -C E:\VisualCat log --oneline -8          # the remediation commits
dotnet test VisualCat.slnx                    # 1,071 tests
pwsh tools/package.ps1 -Runtime linux-x64 -Archive

# on the guest
scp artifacts/packages/VisualCat-*-linux-x64-*.tar.gz benny@<ip>:/tmp/
ssh benny@<ip>
mkdir -p ~/vcat-run/candidate/fix/{desktop,cli}
tar -xzf /tmp/VisualCat-Desktop-linux-x64-*.tar.gz -C ~/vcat-run/candidate/fix/desktop
tar -xzf /tmp/VisualCat-CLI-linux-x64-*.tar.gz     -C ~/vcat-run/candidate/fix/cli
chmod +x ~/vcat-run/candidate/fix/desktop/VisualCat ~/vcat-run/candidate/fix/cli/vcat
```

The archive is packaged on Windows and therefore carries no executable bit — plan Appendix B #1,
corrected with `chmod +x` and not a finding. `env-fix.sh` is the same shape as `env.sh` in §0,
pointing at the fixed candidate; the guest's copy was removed at hand-back along with the rest of
the scratch.

To drive the shell without a pointer, the two scripts this pass used are worth rebuilding rather
than preserving: one walks the AT-SPI tree (`pyatspi`, already installed on this guest) and
prints frames, node counts and control names; the other clicks a control by its accessible name,
reading its extents from AT-SPI and moving the pointer there with `xdotool`. Clicking by name is
what made the run repeatable — and see §20.6 for the two ways of sending a key that do **not**
work on this platform.

## 21. Sixth pass — closing what §20.8 left, and four rows from §15

§20.8 listed three things the remediation had not established. Two are now closed, one remains
upstream. While the guest was up, four standing rows from [§15](#15-what-remains-untested-after-four-passes)
were run as well, because the fixed build changes what they measure.

### 21.1 [F-03](#f-03)'s browser hand-off — **CLOSED, and it found the other half of the finding**

The command existed and said the right thing; pressing its action did nothing, because there was
no action to press. The desktop's only notice surface is the compact line in the brand row, and
the lane that carries a notice's action button is **Android-only** — `host.IsVisible =
OperatingSystem.IsAndroid() && …`. So every notice action on the desktop was silently dropped, and
*Check for updates…* arrived as a statement that releases are on GitHub with no way to get there.
That is exactly the half of F-03 the finding asks for: *"says so plainly **and offers to open the
releases page**"*.

The action now appears beside the message on the desktop as well, and only when a notice carries
one, so an ordinary line is unchanged. Measured:

| | |
|---|---|
| action button on the desktop | `'Open releases'` — `showing=True enabled=True` |
| pressing it | Firefox started; window title **`Releases · benny-cz/VisualCat — Mozilla Firefox`** |
| the URL it was given | `https://github.com/benny-cz/VisualCat/releases` |
| route | the desktop's default handler through `xdg-open`, as the finding asks |

### 21.2 [F-30](#f-30)'s masked-portal case — **CLOSED; the earlier limit was my own bad measurement**

§20.4 recorded that with the portal masked, the shell's *Cancel* did not release the file-operation
slot. It does. Re-measured with the click helper filtering on `STATE_SHOWING` and re-reading the
control's extents immediately before clicking — §20.6's own advice — with the portal stopped
**and** masked:

| t | state |
|---|---|
| chooser requested | `Choosing portable archive…` · *Open archive* disabled · *Cancel* enabled |
| **1 s after Cancel** | **`Choosing portable archive cancelled.`** · *Open archive* **enabled** |
| 5 s after | unchanged |

The first attempt had clicked a stale node from a differently-sized window. Nothing about the
product differed between the two measurements. F-30's product side is closed in every shape.

### 21.3 [F-11](#f-11) — **still upstream, unchanged**

Nothing new was tried. §20.4 records the two approaches that do not work and what would close it.

### 21.4 §15 · `umask 077` under the desktop — **PASS**

The remaining half of P-22: the CLI leg passed in Part I's third pass, the GUI leg was open. The
desktop launched under `umask 077` with a fresh data root, imported a 5,000-line log through
`--log`, and opened it:

| Object | Mode |
|---|---|
| data root | **700** |
| `Sessions/` | **700** |
| `SessionAccess-v1/` | **700** |
| session directory | **700** |
| `manifest.json` | **600** |
| a segment directory | **700** |

Nothing was refused, and the session opened normally — which is the point of the row: a stricter
umask must not break the product, and it does not.

### 21.5 §15 · Low disk and a filesystem that fails — **PASS, with one observation**

Three shapes on a loop-mounted ext4 filesystem, the third through a `device-mapper` target swapped
for one that errors on every I/O — which is what `errors=remount-ro` exists for and the closest
this host can get to a disk dying under a running import.

**A filesystem that runs out** (172 MiB free, a 172 MiB source):

```
error: No space left on device : '/mnt/vcatsmall/full.vcat/source-order/records.bin'
exit 1
```

The message names the condition **and the exact file**, which is what makes it actionable. What
survives is more interesting: `info`, `query`, `stats` and `search` all exit 0 and see all
**900,001** entries, so no captured data is lost or unreachable. `vcat verify` exits 3 and says
`Source-record stream is truncated.` — which is **true**: the write was cut mid-record by `ENOSPC`.
Freeing space and re-indexing succeeds and verifies clean.

*The observation.* The session is left in state `Importing`, `finalized: false`, rather than
`Failed`. The coordinator does try to publish a failure state, and on a full filesystem there is
nowhere to write it — you cannot record that you ran out of room in the room you ran out of. No
change is proposed: the error message is accurate, the data is intact and readable, and `verify`
is honest about what it found.

**A read-only filesystem.** A session seeded while writable, then `mount -o remount,ro`:

| Command | Exit |
|---|---|
| `info` | **0** |
| `query` | **0** |
| `verify` | **0** |
| `search` | **0** |
| `index` into it | 1 — `error: Read-only file system : '/mnt/vcatsmall/new.vcat'` |
| `export` into it | 1 — `error: Read-only file system : '…/out.csv.tmp-…'` |

A session on read-only media — a snapshot, a shared mount, an archive volume — is fully usable,
and every write is refused by name. That is the contract this row exists to check.

**The device failing under a running import.** A `linear` device-mapper target swapped for an
`error` target four seconds into an index:

```
kernel: EXT4-fs (dm-0): Remounting filesystem read-only
kernel: Buffer I/O error on dev dm-0, logical block 153584, async page read
vcat:   error: Input/output error : '/mnt/vcatfail/dead.vcat/source-order/records.bin'
        exit 1
```

No crash, no hang, no core dump (`/var/crash` **0**), no orphaned process. The product reports the
I/O error against the file it was writing and exits.

### 21.6 §15 · A second distribution — **PASS, desktop and CLI**

§15 named this *"the highest-value remaining item"*. A Debian 12.15 root (`debootstrap
--variant=minbase`, glibc **2.36**, 755 MB) with only the packages
[F-02](#f-02)'s message names — `libx11-6 libice6 libsm6 libfontconfig1` — plus `libicu72`,
`tzdata` and `xvfb`.

**The CLI, on Debian 12:**

| | |
|---|---|
| `vcat --version` | `2.0.13+41c7b28…` |
| `index small.txt` | 1,000 entries, format `ThreadTime`, **confidence 1.000** |
| `verify` | exit **0** |
| `export --type csv` | exit 0, **0 carriage returns** — the [F-29](#f-29) default holds on a second distribution |

**The desktop, on Debian 12**, under `Xvfb :99` at 1400×900 with **no window manager at all**: it
started, imported through `--log`, and rendered the complete workspace — heat map with all six
severity rows, minimap, time axis, entry list, templates pane, and a status line reading
`Ready · 1,000 entries`. `stderr` was empty. The data root was created by the [F-19](#f-19) fix at
mode **700**, the session directory at **700**, and the session verifies. The entry list shows
five-digit thread ids — `10503 / 5136`, `14132 / 24503`, `985 / 18176` — rendering correctly,
which is [F-01](#f-01) visible in the product.

That closes §15's second-distribution row for both artifacts, and adds a data point the plan does
not ask for: the desktop runs with **no window manager**, which no GNOME measurement can establish.

### 21.7 §15 · [F-10](#f-10)'s control, on a real X server — **the defect is XWayland-specific, confirmed on the fixed build**

The same binary on `Xvfb` — a real X server, not XWayland — measured three times with the
mitigation on and three times with `VISUALCAT_FULL_REPAINT=0`:

| | unpainted |
|---|---|
| mitigation on, grabs 1–3 | 1.973 % |
| mitigation off, grabs 1–3 | **1.972 %** |

Identical to three decimal places. The 1.97 % is the root window's own black margin around a
1400×900 app window, not unpainted product pixels. Two things follow: the rendering defect really
is XWayland-specific, as Part I's §7.17 concluded, and **the mitigation costs nothing where it is
not needed** — which is what makes it safe to ship enabled on every Linux host.

### 21.8 Sixth-pass cleanup

| Step | Verified |
|---|---|
| Debian 12 chroot | `/srv/deb12` removed, `/srv` removed, **0** stray bind mounts |
| Loop filesystems and the device-mapper target | unmounted, `dmsetup remove --force`, `losetup -D`; **0** `vcat*` dm devices, **0** loops on this pass's images, `/mnt` back to `hgfs` alone |
| Images and corpora | `/tmp/{fail,ro,small}.img`, `/tmp/{fill,ro,fit,s}.txt` removed |
| `Xvfb` and `debootstrap` installed for this pass | left in place — ordinary packages, no configuration changed; note them if the guest is ever audited for what this run added |
| Product processes | `VisualCat` **0** · `vcat` **0** |
| `/var/crash` | **0** entries across every storage-failure shape |
| Free space | 162 GiB |

### 21.8b Final hand-back

| Step | Verified |
|---|---|
| Product processes | `VisualCat` **0** · `vcat` **0** |
| Stray portal choosers, dismissed with Escape | **0**; `xdg-desktop-portal` and `-gnome` both **active**, neither masked nor failed |
| `idle-delay` / `idle-activation-enabled` / `toolkit-accessibility` | `uint32 300` / `true` / `false` — all restored |
| Probe accounts (`vcatprobe`, `vcatacl`) | removed; the guest has **one** account again |
| Loop devices · device-mapper targets · extra mounts | **0** · **0** · `/mnt` is `hgfs` alone |
| `/srv/deb12` | removed, and `/srv` with it |
| Candidate, scratch, driver scripts, transferred tarballs | removed, including `env-fix.sh` |
| Sessions this remediation created | removed; the account's own **4** from Part I untouched, **100 MB** |
| Lease files | 14 remain, all belonging to sessions that exist |
| `/tmp/dotnet-diagnostic-*-socket` | **0** |
| `/var/crash` | **0** entries, across every shape in six passes |
| Part I's harness (`candidate/rel-2.0.13`, `corpus`, `env.sh`) | **3/3 intact** — the next run can still use it |
| Free space | 162 GiB |
| Phone `RFCRC0A9GND` | imported tab closed, pushed archive removed, app stopped, asleep and **locked** (`deviceLocked=1`, `trustState=UNTRUSTED`) |
| Host ADB server | loopback only; firewall rule `VCAT-ADB-5037` removed |
| Left installed on the guest by this pass | `xvfb`, `debootstrap`, `acl` — ordinary packages, no configuration changed |
| Orca and `speech-dispatcher` | Orca stopped; the `speechd.conf` this pass wrote to point at the dummy synthesizer removed, along with its directory. Both were already installed on the guest |
| The guest's screen | **locked.** GNOME locked the session during the pass — Appendix B #3, and specifically the form the report warns about: restoring the idle timer at one hand-back makes the next pass hit it. It cannot be unlocked remotely: `loginctl unlock-session` reports success and does not unlock, the `org.gnome.ScreenSaver.SetActive` call times out, and a Wayland lock screen takes no input from an X client. Nothing is broken by it — ssh, the filesystem and the VM are unaffected — but the next person at the console will need the account password |

### 21.9 Revised tally after the sixth pass

| | |
|---|---|
| Findings | **31** — 8 Major, 13 Minor, 10 Polish |
| Closed and live-verified | **30** |
| Closed with a stated limit | **1** — [F-13](#f-13): the application and its controls yes, the structural `panel` names upstream |
| Not closed | **1** — [F-11](#f-11), upstream in Avalonia |
| §15 rows closed by this pass | **6½** — `umask 077` under the desktop, low disk, quota/read-only/failing storage (L3, X-15), a second distribution for both artifacts, an enforced ACL denial (P-09), **Orca end-to-end (U-07)**, and the forward half of the Android leg of I-08 |
| §15 rows still open | metal and the §4.2 performance budgets, ADB `no permissions`/`udev`/group membership, the G4 desktop matrix beyond "no window manager", multi-monitor, the soak, and the reverse half of I-08 (no scriptable file-saving share target on this device) |
| Defects found *by* this pass | **2** — a session an ACL denies was reported as a missing manifest (§21.12), the same wrong-diagnosis family as F-06 and F-19, fixed and pinned by a test; and [F-32](#f-32), a screen reader reading the shell's implementation types aloud, which is upstream. A third — a window read out whole because nothing in it held focus — was found the same way and is closed |
| Findings after this pass | **32** — 9 Major, 13 Minor, 10 Polish |
| Format compatibility across the fix | **PASS both ways** (§21.10) — the shipped 2.0.13 and the fixed build read each other's sessions, and their portable archives have identical member sets |
| Unit tests | **1,071**, 0 failures |
| VisualCat-attributable crashes, hangs or core dumps | **0**, across five storage-failure shapes and two distributions |

### 21.10 Session-format compatibility across the fix — **PASS both ways**

Not a finding and not a §15 row, but the largest regression risk this remediation carries: the
parser, the store writer, the verifier's report shape, the archive extractor and the source
identity all changed. A session written by one build has to be readable by the other, or the fix
is a format break.

The shipped 2.0.13 candidate is still in the guest at `~/vcat-run/candidate/rel-2.0.13`, which
makes the comparison exact — the same corpus, both builds, in both directions.

| Direction | `info` | `verify` | `query` | `export --type csv` |
|---|---|---|---|---|
| **shipped build reading a fixed-build session** | 0 | **0** | 0 | 0 — 19,999 rows |
| **fixed build reading a shipped-build session** | 0 | **0** | 0 | — |

And the portable archives themselves: **32 members each**, and the two member sets are
**identical** — nothing added, nothing removed. [F-21](#f-21)'s allowlist accepts exactly what the
shipped build writes, which is the property that makes it a bound rather than a break.

### 21.11 §15 · The Android leg of I-08 — **forward direction PASS on the physical phone**

The phone carries the **shipped** VisualCat 2.0.13 (`com.barebit.visualcat`, installed
2026-09-09), so this is the sharper half of the question: does an archive written by the *fixed*
Linux build open in a build that predates every fix?

A 20,000-line corpus, indexed and exported as `portable-zip` by the fixed Linux CLI, pushed to
`/sdcard/Download`, and opened through *More actions → Open portable archive…* and the system
document picker:

| | |
|---|---|
| notice | **`Opened linux-made.vcat.zip`** |
| tab | `compat.txt` |
| counts | **`19,998 in view · 19,998 match · 19,998 timed in session · 2 unparsed lines`** |
| entries | render with five-digit identities — `66309:55984`, `27217:6761`, `3549:7764` |

19,998 of 20,000 with 2 malformed lines is exactly what the corpus holds. The five-digit
identities are [F-01](#f-01)'s generator change arriving on Android through a Linux-made archive.

**The reverse direction was not completed.** *More actions → Share…* did produce an archive —
`compat-20260912-164010.vcat.zip` — and offered it to the system share sheet, but this device has
no file-saving target (no Files app; only Quick Share, Gmail, Drive, Outlook, Bluetooth,
OneDrive), so there is no scriptable way to get the bytes back to the host. The property it would
test — a shipped-format archive opening in the fixed build — is established by §21.10's second
row, on the same format.

The device was left as it was found: the imported tab closed, the pushed archive removed, the app
stopped, and the phone asleep and locked (`deviceLocked=1`, `trustState=UNTRUSTED`).

### 21.12 §15 · A correct ACL denial (P-09) — **PASS, after it found one more wrong diagnosis**

The row had been attempted twice in Part I and proved nothing both times, because a plain `cat`
control behaved identically to the product — the ACL was not actually denying anything. It does
here, and getting the control right first is what made the row worth running.

**Two controls, not one.** The first attempt at this pass measured `sudo -u vcatacl vcat …` and
got `Sorry, user benny is not allowed to execute … as vcatacl` — sudo's own policy refusing the
user switch, not the product refusing the file. `sudo runuser -u vcatacl --` is the form that
works here, and the binary has to live somewhere the other account can traverse.

| Case | `cat` | `vcat info` |
|---|---|---|
| no ACL, plain file | allowed | **exit 0** |
| ACL denies that user, plain file | refused, `Permission denied` | **exit 1** — `error: Access to the path '/tmp/aclt2/src.txt' is denied.` `cause: Permission denied` |
| no ACL, session directory | — | exit 0 |
| ACL denies that user, session directory | — | exit 1 — **`error: Session manifest was not found.`** ✗ |

The last row is a wrong diagnosis of the same family as [F-06](#f-06) and [F-19](#f-19): the
manifest is present and intact, and the reader is sent looking for a missing file.
`FileInfo.Exists` answers **false** for "you may not look" exactly as it does for "it is not
there", and the product repeated it.

**Fixed.** The manifest is opened rather than probed, and the open says which it is. Re-measured
on the guest with the same ACL:

| Case | Now |
|---|---|
| a session that genuinely is not there | `error: Log source was not found.` |
| a session an ACL denies | **`error: Access to the path '/tmp/aclt2/s.vcat/manifest.json' is denied.`** `cause: Permission denied` |
| the owner reading the same session | exit **0** — the denial is the ACL's, not a new refusal of our own |

A regression test pins both halves without needing an ACL: a directory with no execute bit is the
same shape at the filesystem layer, and it skips itself as root, where the mode is ignored.

The probe account `vcatacl` and every ACL this row set were removed at cleanup; the guest has one
account again.

### 21.13 §15 · Orca end-to-end (U-07) — **run for the first time, and it found a finding**

§15 had this row open because "the AT-SPI tree was inspected but nobody listened". Nobody has to:
Orca 42.0 is installed on the guest, `speech-dispatcher` has an `sd_dummy` output module so nothing
tries to open an audio device on a VM, and `orca --debug-file=<path>` records every utterance as a
`SPEECH OUTPUT:` line. That is what Orca *would say*, captured verbatim.

**Setup.** `~/.config/speech-dispatcher/speechd.conf` with `AddModule "dummy" "sd_dummy" ""` and
`DefaultModule dummy`; `toolkit-accessibility true`; the desktop started with a 3,000-line session
open; then `orca --replace --debug-file=/tmp/orca4`, focus the window, and Tab through the
documented focus order.

**What it says correctly.** Every interactive control announces itself properly, including the
names [F-13](#f-13) added:

```
'＋  Open log push button.'
'Save portable push button.'
'Export push button.'
'More  ▾'
'Show complete session orca.txt push button.'
'Open this complete session.'
```

**What it says that it should not.** Reaching that first button takes Orca through sixteen stops
on nodes that are not controls at all:

```
'Panel panel.'                    'Border panel.'
'VisualLayerManager panel.'       'Grid panel.'
'ContentPresenter panel.'         'StackPanel panel.'
'ContentPresenter panel.'         'ItemsPresenter panel.'
'Grid panel.'                     'FadingScrollHost panel.'
'Border panel.'                   'ScrollContentPresenter panel.'
'Grid panel.'                     …
```

and, after each one, the whole notice text again. **168 nodes** in an open session carry a name
that is the name of their implementation type.

---

#### F-32 · Major · A screen reader reads the shell's implementation types aloud before reaching any control

**Severity** Major, and it supersedes [F-13](#f-13)'s own assessment of this. F-13 called the
structural names cosmetic *"because Orca mostly skips `panel` roles"*. It does not skip them. A
screen-reader user starting at the top of the window hears `Panel panel`, `VisualLayerManager
panel`, `ContentPresenter panel`, `Grid panel`, `Border panel`, `StackPanel panel`,
`ItemsPresenter panel`, `FadingScrollHost panel`, `ScrollContentPresenter panel` — with the notice
text repeated between them — before the first thing they can act on. **R-40** exists to stop
implementation detail being read aloud, and this is the product doing exactly that, sixteen times
before anything useful.

**Where** Avalonia's AT-SPI backend, not this repository. It derives an unnamed control's
accessible name from the control's **type**, and it consults `AutomationProperties` for neither
the tree shape nor that fallback.

**Measured, not assumed.** Three attempts against a live AT-SPI tree on the guest, same session
and same window each time:

| Attempt | Structural nodes carrying a type name |
|---|---|
| as shipped | **168** |
| styling every container type with `AutomationProperties.AccessibilityView = Raw` | **168** |
| the same, plus `AutomationProperties.Name = ""` | **168** |

Not merely the same total — the same breakdown, type by type, including types that the style
covers completely and exclusively (`VisualLayerManager` 1, `ItemsPresenter` 3,
`ScrollContentPresenter` 2). The styles were removed rather than left looking like a fix, and the
reason is recorded in `ProductTheme.BuildStyles` so the next reader does not repeat the
experiment. This is the same dead end as [F-11](#f-11), in the same component.

**Suggested fix — upstream, in `Avalonia.FreeDesktop.AtSpi`.** A control with no automation name
should expose no accessible name rather than its type name, and a peer whose
`AccessibilityView` is `Raw` should be skipped while its children are kept. Either one alone
removes the noise; the second is the one that also fixes [F-11](#f-11)'s modality half, so the two
are worth reporting together.

**What was fixed here.** The other half of what Orca exposed was ours, and is closed — below.

---

#### The wall of text — **CLOSED**

The first Orca run produced one utterance that was the *entire workspace*: the notice, the
strapline, every count, the template list, the entry list and their timestamps, hundreds of words
in a single breath. It is not a naming problem — it is what Orca does with a window that has no
focused control, and the main window had none. The same shape as [F-12](#f-12)'s dialogs, found
the same way.

The window now takes focus on its first focusable control when nothing inside holds focus,
exactly as the dialogs do. Measured after the change, with the same corpus and the same Orca
setup:

| | Before | After |
|---|---|---|
| longest single utterance | the whole workspace | **`'VisualCat v2 — See the shape of your log frame.'`** |
| focused control on open | none | **`push button '＋  Open log'`** |
| Tab from there | — | `'● ADB live'` → `'Open session'`, each announced by name |

A headless test pins both halves: that the window focuses a control inside itself on open, and
that the control is **not** `:focus-visible` — so a reader who never touches the keyboard sees no
focus ring appear at launch, and the change costs a pointer user nothing.
