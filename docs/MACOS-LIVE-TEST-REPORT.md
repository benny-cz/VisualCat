# VisualCat — macOS live test report (macOS 26.6.2 "Tahoe", Apple M1, arm64)

Live execution of [`MACOS-LIVE-TEST-PLAN.md`](MACOS-LIVE-TEST-PLAN.md) against a real
Mac, driving the **shipped `osx-arm64` and `osx-x64` release tarballs** through a real
Aqua session with a real window server, a real Gatekeeper/TCC decision on an unsigned,
un-notarized binary, real APFS semantics, a real Metal GPU, and a **physical Android
phone** on the Mac's own USB bus.

This report is **context-agnostic**: every path, hash, setting, and command it depends on
is recorded here, so a reader who has never seen the run can reproduce or continue it
without a single fact that exists only in a previous session.

The run is documented **continuously**. [§0](#0-restore-point--resume-here) is the restore
point and is rewritten after every scenario, so an interrupted run resumes from the last
line there. Findings are appended to [§3](#3-findings) the moment they are observed.

> **This is the first macOS run in the project's history.**
> [`RELEASE-CHECKLIST.md`](RELEASE-CHECKLIST.md) records macOS hardware validation as
> intentionally deferred for every release from `2.0.0` to `2.0.13`. There is therefore
> **no accepted macOS baseline**: every number below is a first observation, not a
> regression measurement.

---

## 0. Restore point — resume here

| Field | Value |
|---|---|
| Run ID | `20260914-macos-arm64-m1` |
| Status | **IN PROGRESS** |
| Last completed | §2.19 — B-05 import + TCC, B-06 byte-exact source context, B-07 filters, B-08 search, U-06 focus order, crash recovery |
| Next step | B-16 CSV export and line endings; B-13 follow a growing file; B-19 window state; P-01 network |
| Findings | 17 open (F-01 … F-17): **5 Major**, 8 Minor, 4 Polish |

**To resume.**

```shell
# from Windows
ssh -i %USERPROFILE%\.ssh\windows_claude_ed25519 benny@192.168.0.199
# on the Mac: load every path token this report uses
. ~/vcat-run/env.sh
# restart the screenshot daemon (it must live inside Terminal.app - see 1.4)
pkill -f shotd.sh
osascript -e 'tell application "Terminal" to do script "exec $HOME/vcat-run/shotd.sh"'
osascript -e 'tell application "System Events" to set visible of process "Terminal" to false'
```

Per-scenario evidence stays on the Mac at `~/vcat-run/evidence/20260914-macos-arm64-m1/`;
screenshots are mirrored to the Windows host at
`artifacts/live-test/20260914-macos-arm64-m1/` (git-ignored, like the Windows and Linux
runs). Every number and quoted string in this report is reproduced verbatim in the text,
so the report stands on its own without them.

---

## 1. Run header

### 1.1 Host, session, and policy

Recorded per plan §2.2 before the first scenario.

| Field | Value |
|---|---|
| Model | `MacBookPro17,1` — MacBook Pro 13-inch, M1, 2020 |
| Chip | Apple M1 · 8 cores (`hw.perflevel0.logicalcpu=4` performance, `hw.perflevel1.logicalcpu=4` efficiency) · 8-core GPU · Metal 4 |
| Memory | 17 179 869 184 bytes (16 GiB) |
| macOS | **26.6.2**, build **25G83**, `Darwin 25.6.0 … RELEASE_ARM64_T8103` |
| Architecture | `arm64`; `sysctl.proc_translated = 0` for the shell |
| Rosetta 2 | **present** (`oahd` running) — so `osx-x64` is testable on this host |
| SIP | `System Integrity Protection status: enabled.` |
| Gatekeeper | `spctl --status` → `assessments enabled` |
| Account | `uid=501(benny)`, member of `admin` — **an administrator account**; see [§1.6](#16-deviations-from-the-plan) |
| `umask` | `022` |
| Shell | `zsh` (`/bin/zsh`), non-login non-interactive over SSH; `~/.zshenv` puts Homebrew on `PATH` for `ssh host 'cmd'` |
| `ulimit -n` | **256** (the stock macOS soft limit) · `launchctl limit maxfiles` → `256 unlimited` · `kern.maxfilesperproc=61440` |
| Console owner | `benny` — the SSH account owns the live Aqua session |
| `launchctl managername` | **`Background`** in every SSH shell (never `Aqua`) |
| Appearance | **Dark** (`AppleInterfaceStyle=Dark`), auto-switch `false`, accent `multicolour`, highlight unset |
| Accessibility defaults | `reduceMotion=0 reduceTransparency=0 increaseContrast=0 differentiateWithoutColor=0`; `AppleKeyboardUIMode` unset (full keyboard access **off**) |
| Locale | `AppleLocale=en_US@rg=czzzzz`; `AppleLanguages=("en-US","cs-CZ")`; SSH shell `LANG=""`, `LC_COLLATE=LC_CTYPE=C` |
| Time zone | `Europe/Prague` (`/var/db/timezone/zoneinfo/Europe/Prague`) |
| Volume | APFS `Macintosh HD`, **case-insensitive** (proved live: `touch CaSe.txt` then `ls case.txt` succeeds) |
| Free space | 798 GiB available on `/System/Volumes/Data` |
| `TMPDIR` | `/var/folders/zy/bsrr65210bj1s85fr6g0l0dr0000gn/T/` |
| Spotlight | `mdutil -s /` → `Indexing enabled.` |
| Power | `sleep 0` (prevented), `displaysleep 10`, `lowpowermode 0`, on AC |
| Background load | The machine is the owner's daily Mac: Safari, Claude desktop, Visual Studio, Realm Browser, Notes, Activity Monitor and Finder were resident throughout. Recorded, not eliminated — see [§1.6](#16-deviations-from-the-plan) |

### 1.2 Display and window-server state (plan §2.7)

| Field | Value |
|---|---|
| Displays | one, built-in `Color LCD`, Retina, internal, main, mirror off |
| Panel | 2560 × 1600 |
| **Framebuffer** | **2880 × 1800** — measured from `screencapture` output dimensions |
| **Logical desktop** | **1440 × 900 points** — measured from `tell application "Finder" to get bounds of window of desktop` → `0, 0, 1440, 900` |
| Backing scale | 2.0 |
| Effective state | **S1 — non-integer "scaled" resolution.** macOS renders at 2880×1800 and downsamples to the 2560×1600 panel. This Mac's *default* mode would be "looks like 1280 × 800"; that is not the mode under test |
| Menu bar height | 30 points (macOS 26) |
| Usable height | 795 points with the Dock shown (`900 − 30 menu bar − 75 Dock`) |
| Displays have separate Spaces | on (`com.apple.spaces spans-displays` unset) |
| Stage Manager | off (`com.apple.WindowManager GloballyEnabled` unset) |
| Notch | **absent** — `MacBookPro17,1` has no camera housing. S5 is N/A by recorded hardware absence, not by assumption |
| ProMotion | absent — 60 Hz fixed panel. S3's adaptive-refresh half is N/A on this host |

`system_profiler SPDisplaysDataType` on macOS 26 prints only `Resolution: 2560 x 1600
Retina` and **no `UI Looks like:` line**, so the scaled mode is invisible to the command
the plan's §2.2 suggests. The framebuffer and point sizes above were measured instead.

### 1.3 Candidate identity and provenance (plan §2.4)

Release **v2.0.13**, tag `v2.0.13`, published `2026-09-09T18:22:41Z`.
Downloaded with `curl -fsSL` from
`https://github.com/benny-cz/VisualCat/releases/download/v2.0.13/` directly on the Mac,
which is plan state **Q0 — no quarantine** (`xattr -l` on all four archives returns
nothing).

| Asset | Size | SHA-256 | vs `SHA256SUMS` |
|---|---|---|---|
| `VisualCat-Desktop-osx-arm64-v2.0.13.tar.gz` | 45 242 509 | `b1da1ee843448bdabd8256c04d67934f577312a34039b6955a1ab0ed6e14c21a` | **OK** |
| `VisualCat-CLI-osx-arm64-v2.0.13.tar.gz` | 32 769 962 | `2caec2be9bef5e15dbdf322b642763e9bff357fdff3bacf5371a19227e13c9d2` | **OK** |
| `VisualCat-Desktop-osx-x64-v2.0.13.tar.gz` | 47 439 658 | `0de090b0dd9cfa818b432b5c8a4f64620749b0f79f61a70c60464ae69041be81` | **OK** |
| `VisualCat-CLI-osx-x64-v2.0.13.tar.gz` | 34 988 655 | `0ce688525162d8c2688c6dc1a5b7dee684d22191ab28ac54c49c6e2ee56e2ff1` | **OK** |

Extracted executables:

| File | SHA-256 | Mode |
|---|---|---|
| `desktop-osx-arm64/VisualCat` | `3fe3360ea5cc34f7a98d25184de73c5f2728be6a66892b37872f5f20b82551e7` | `0755` |
| `cli-osx-arm64/vcat` | `ac3034a7cef1e46bca49f029f58c38ff09c86a1516585a48994b9a448b3a14c4` | `0755` |

Visible product identity, read off the running desktop's empty state:
**`VisualCat 2.0.13+0670981 · local-first · no telemetry`**.

### 1.4 How the Mac was driven

The plan's §2.1 requires "a real logged-in graphical (Aqua) session owned by the account
under test". That is satisfied — `stat -f '%Su' /dev/console` is `benny` — but every
command in this report is issued over **SSH**, where `launchctl managername` always
reports `Background`. Three consequences shaped the harness and are recorded because they
change what the evidence means:

1. **The desktop binary still gets a window from a `Background` session.** This is the
   opposite of the Linux behaviour the plan anticipates, and is [§2.2](#22-s7--the-desktop-launches-from-a-background-ssh-session)'s
   result.
2. **UI automation** needs the *Accessibility* TCC grant on `/usr/libexec/sshd-keygen-wrapper`.
   It was granted during this run (`kTCCServiceAccessibility|/usr/libexec/sshd-keygen-wrapper|2`
   in `/Library/Application Support/com.apple.TCC/TCC.db`). Without it every
   `System Events` call fails with `osascript is not allowed assistive access. (-1728)`.
3. **`screencapture` cannot be run from SSH at all** on this host — it exits with
   `could not create image from display`, because `sshd-keygen-wrapper` holds no
   *Screen Recording* grant and, being a non-GUI process, is never offered the prompt.
   Terminal.app **does** hold `kTCCServiceScreenCapture`, so every screenshot in this
   report is taken by a small queue daemon (`~/vcat-run/shotd.sh`) that runs *inside*
   Terminal.app and is driven from SSH through a request directory. The helpers are
   `~/vcat-run/shot.sh` (full screen or `-R` region) and `~/vcat-run/shotwin.sh`
   (reads a window's AX `position`/`size` and captures exactly that rectangle).

### 1.5 TCC grants in force (plan §2.6)

Read from the live TCC databases, not from a screenshot of System Settings.

| Service | Client | Value | Why it matters |
|---|---|---|---|
| `kTCCServiceAppleEvents` | `/usr/libexec/sshd-keygen-wrapper` | 2 (allow) | Lets SSH drive AppleScript at all |
| `kTCCServiceAccessibility` | `/usr/libexec/sshd-keygen-wrapper` | 2 (allow) | Lets SSH read and click UI elements |
| `kTCCServiceScreenCapture` | `com.apple.Terminal` | 2 (allow) | The only usable screenshot path (§1.4) |
| `kTCCServiceScreenCapture` | `/usr/libexec/sshd-keygen-wrapper` | **absent** | Why §1.4's daemon exists |
| `kTCCServiceSystemPolicyDesktopFolder` | `com.apple.Terminal` | 2 | Terminal already reaches `~/Desktop` |
| `kTCCServiceSystemPolicyDocumentsFolder` | `com.apple.Terminal` | 2 | …and `~/Documents` |
| `kTCCServiceSystemPolicyDownloadsFolder` | `com.apple.Terminal` | 2 | …and `~/Downloads` |
| `kTCCServiceSystemPolicyAllFiles` | *(no VisualCat-relevant client)* | absent | Full Disk Access is **not** granted to Terminal, so Q6 consent is genuinely testable |

This is exactly the situation the plan's §2.6 note warns about: **consent is attributed to
the launching process, never to VisualCat**, and this Mac's Terminal already holds the
three Files-and-Folders grants a log import would need.

### 1.6 Deviations from the plan

| Plan requirement | What was actually used | Effect on the evidence |
|---|---|---|
| "An ordinary non-administrator account for the primary run" (§2.1) | `benny`, an **administrator** | Elevation was never used or needed; but this run cannot prove that a non-admin account behaves identically |
| A quiet machine | The owner's daily Mac, with Safari, Claude, Visual Studio and others resident | Absolute timings are upper bounds and are labelled as such; no timing here is a clean-room benchmark |
| **S0** — default scaling as the visual baseline | **S1** — "looks like 1440 × 900" scaled mode | Every screenshot is a 2× render of a scaled mode. Noted per assertion where it matters |
| `sudo` for `fs_usage`, `memory_pressure`, APFS volume creation | Not available (password-protected) | Those rows are recorded Blocked-no-privilege rather than skipped silently |

---

## 2. Results

### 2.1 B-01 / B-02 / P-13 — artifact identity, archive safety, signature

**Verdict: PASS**, with two documentation findings ([F-01](#f-01), [F-02](#f-02)).

Checksums for all four macOS assets matched `SHA256SUMS` byte for byte (§1.3). All four
archives carried **no extended attributes at all** after a `curl` download, which is
plan state **Q0**.

Archive safety, desktop `osx-arm64` (`tar -tvzf`):

| Check | Result |
|---|---|
| Members | 240 |
| Absolute paths or `..` components | none |
| Symlinks, hard links, devices, FIFOs | none |
| setuid / setgid / sticky bits | none |
| AppleDouble `._*` or `.DS_Store` members | none |
| Owner in the archive | `runner staff` — a macOS workflow runner, as `RELEASE-CHECKLIST.md` requires |
| `./VisualCat` mode | `-rwxr-xr-x` (0755) |
| `./vcat` mode | `-rwxr-xr-x` (0755) |
| `LICENSE`, `README.txt`, `THIRD-PARTY-NOTICES.md` | present, `-rw-r--r--` |

The executable bit survives extraction with stock `bsdtar` and `umask 022`: both
`VisualCat` and `vcat` land as `0755`. The README's `chmod +x` line is therefore a remedy
for a lossy extractor, not a shipped defect.

**Archive layout.** Every member is `./<name>` — there is **no wrapper directory**. A user
who runs `tar -xzf` in `~/Downloads` scatters 239 loose files (240 for the desktop
archive, 207 for the CLI) directly into it. See [F-02](#f-02).

**Code signature** (`codesign -dv --verbose=4`):

| Field | `VisualCat` | `vcat` |
|---|---|---|
| `Identifier` | **`apphost`** | **`apphost`** |
| `Format` | `Mach-O thin (arm64)` | `Mach-O thin (arm64)` |
| `CodeDirectory` | `v=20400 size=1152 flags=0x2(adhoc) hashes=31+2` | same shape |
| `Signature` | `adhoc` | `adhoc` |
| `TeamIdentifier` | `not set` | `not set` |
| `CDHash` | `9fcabffc851baa5d1f47b88a9d225166c2913d1d` | — |
| `codesign --verify --strict` | `valid on disk` / `satisfies its Designated Requirement`, exit 0 | — |
| `lipo -archs` | `arm64` | `arm64` |

`spctl --assess --type execute` returns **`rejected`** (exit 3) for the desktop binary.
That is the documented, expected state for an unsigned, un-notarized artifact and is
**not** a finding.

Bundled native libraries: 16 `.dylib` files, all `codesign --verify --strict` clean.
Their signers are not uniform, and this is worth recording because the plan asks for it:

| Library group | Signing identity |
|---|---|
| `libcoreclr`, `libclrjit`, `libclrgc`, `libclrgcexp`, `libhostfxr`, `libhostpolicy`, `libmscordaccore`, `libmscordbi`, `libSystem.*` (6 files), `createdump` | Signed with `TeamIdentifier=UBF8T346G9` (Microsoft) |
| `libSkiaSharp.dylib`, `libHarfBuzzSharp.dylib` | Signed, identifier `libSkiaSharp-5555…`, `libHarfBuzzSharp-5555…` |
| `libAvaloniaNative.dylib` | **ad-hoc**, identifier `libAvalonia.Native.OSX` |

So only the two first-party-ish binaries (`VisualCat`/`vcat`) and Avalonia's native shim
are ad-hoc; the .NET runtime arrives pre-signed by Microsoft. Nothing fails verification.

### 2.2 S7 — the desktop launches from a `Background` SSH session

**Verdict: PASS — and it contradicts the plan's expectation.**

Plan §2.7 S7 expects a failure whose message names what is wrong, and warns that the
product's startup-failure explanation "is written for X11 and names `DISPLAY`,
`WAYLAND_DISPLAY`, and Debian package names". On macOS **there is no failure to
explain**:

```shell
$ launchctl managername
Background
$ cd ~/vcat-run/candidates/desktop-osx-arm64 && ./VisualCat &
# 8 s later
$ pgrep -x VisualCat
6166
$ osascript -e 'tell application "System Events" to tell process "VisualCat" \
    to get {name, size, position} of every window'
VisualCat v2 — See the shape of your log, 1440, 795, 0, 30
```

stdout and stderr were **empty**. macOS grants window-server access on console
ownership, not on an environment variable, so a `Background` session owned by the console
user is a perfectly good graphical context. The Linux-shaped S7 hazard does not exist
here, and the row should be re-scoped for macOS: the real negative case is an SSH session
whose account does **not** own `/dev/console`.

One real observation did fall out of it: the window opened **behind** the frontmost
application (Safari) and was never brought forward. For a user that is correct
behaviour — a background launch should not steal focus — but it means the process is
running with no visible sign in the foreground, and (see [F-03](#f-03)) the Dock entry it
does create is unlabelled and iconless.

### 2.3 B-04 — desktop empty state and command inventory

**Verdict: PASS for the in-window inventory; FAIL for the macOS application menu
([F-03](#f-03)).**

Window title: `VisualCat v2 — See the shape of your log`.
Geometry at first launch on a 1440 × 900-point desktop: position `0, 30`, size
`1440 × 795` — i.e. the window filled the entire available workspace. Evidence:
`b04-empty-state.png`.

Command bar, left to right, with enablement:

| Command | State |
|---|---|
| `+ Open log` | enabled (primary, filled) |
| `• ADB live` | enabled (accent outline) |
| `Open session` | enabled |
| `Recent` | enabled |
| `Follow file` | enabled |
| `Open archive` | enabled |
| `Save` | **disabled** |
| `Save portable` | **disabled** |
| `Export` | **disabled** |
| `More ▾` | enabled |

Empty state body: headline `SEE THE SHAPE OF YOUR LOG`, subtitle `Turn raw Android logcat
into a navigable severity × time signal.`, the six severity chips
`FATAL ERROR WARN INFO DEBUG VERBOSE`, three text actions `OPEN LOG · ADB LIVE · RECENT
CAPTURES`, and the identity line `VisualCat 2.0.13+0670981 · local-first · no telemetry`.
The version matches the candidate, so B-04's identity assertion passes.

Text and chips render crisply at backing scale 2 with no clipping at 1440 × 795.

### 2.4 P-02 / P-22 — where the product actually stores data

Before first launch there was no `VisualCat` directory anywhere under `$HOME`. After the
first launch:

```
/Users/benny/Library/Application Support/VisualCat
/Users/benny/Library/Application Support/VisualCat/Diagnostics
```

This matches the path [`PRIVACY.md`](PRIVACY.md) publishes
(`~/Library/Application Support/VisualCat`), so the documented location is correct.

Modes as created, with `umask 022`:

```
drwxr-xr-x  /Users/benny/Library/Application Support/VisualCat
drwxr-xr-x  /Users/benny/Library/Application Support/VisualCat/Diagnostics
```

Both are **0755**, i.e. world-readable. See [F-04](#f-04) — carried forward to the full
P-22 pass once sessions exist.

### 2.5 B-03 — runtime dependency and self-containment

**Verdict: PASS**, with one documentation gap ([F-05](#f-05)) and one hygiene note.

`otool -L` on the apphost:

```
/Users/benny/vcat-run/candidates/desktop-osx-arm64/VisualCat:
	/usr/lib/libSystem.B.dylib (compatibility version 1.0.0, current version 1351.0.0)
	/usr/lib/libc++.1.dylib   (compatibility version 1.0.0, current version 1900.180.0)
```

Two macOS system libraries and nothing else. Sweeping `otool -L` across the apphost and
all 16 bundled `.dylib` files and filtering out `/usr/lib/`, `/System/Library/`, `@rpath`,
`@loader_path` and `@executable_path` leaves **no external dependency** — no Homebrew, no
Xcode component, no system .NET. The product also runs with Homebrew absent from `PATH`
(every command in this report that used `$VCLI` ran from an SSH shell whose `PATH` was the
system default plus Homebrew; removing Homebrew changed nothing).

**Declared minimum macOS** (`otool -l | grep -A4 LC_BUILD_VERSION`):

| Binary group | `minos` |
|---|---|
| `VisualCat`, `vcat` (both architectures) | **12.0** |
| All 13 .NET runtime / `libSystem.*` dylibs and `createdump` | **12.0** |
| `libSkiaSharp`, `libHarfBuzzSharp`, `libAvaloniaNative` | 11.0 |

So the effective floor is **macOS 12.0 (Monterey)**. [`SUPPORT.md`](SUPPORT.md) states no
macOS version floor at all — see [F-05](#f-05).

**Hygiene note (not a finding on this host).** `libAvaloniaNative.dylib` carries the
install name `/usr/local/lib/libAvalonia.Native.OSX.dylib` rather than an
`@rpath`-relative one:

```shell
$ otool -D candidates/desktop-osx-arm64/libAvaloniaNative.dylib
… (architecture arm64):
/usr/local/lib/libAvalonia.Native.OSX.dylib
```

The app loads it by explicit path, not by install name, and `/usr/local/lib` does not
exist on this Mac (`/usr/local` is `drwxr-xr-x root wheel`), so nothing resolves there
today. It is recorded because P-12 asks about loader integrity and because an absolute
`/usr/local/lib` install name is the shape that becomes a hijack if any future code path
resolves the library by name. The same file is also a **universal binary** (`x86_64` and
`arm64`) inside the `osx-arm64` archive — 1.5 MB of which half is dead weight for that RID.

### 2.6 B-02 second half / M5 / X-30 — the `osx-x64` build under Rosetta 2

**Verdict: PASS.** This is the **first execution of an `osx-x64` VisualCat artifact
anywhere** — `.github/release-targets.json` marks that target `executable: false`, so CI
has never run it.

| Check | Result |
|---|---|
| `lipo -archs desktop-osx-x64/VisualCat` | `x86_64` — matches the artifact name |
| `codesign -dv` | `Identifier=apphost`, `Format=Mach-O thin (x86_64)`, `Signature=adhoc`, `TeamIdentifier=not set` |
| `codesign --verify --strict` | exit 0 |
| `minos` | 12.0 |
| Rosetta present | yes — `arch -x86_64 /usr/sbin/sysctl -n sysctl.proc_translated` → `1`, `/usr/libexec/rosetta/oahd` running as pid 886 |
| `cli-osx-x64/vcat --version` | `vcat 2.0.13+06709815674a2ea19dfdb08cd1c552836378c27b` — identical string to the arm64 build |
| `desktop-osx-x64/VisualCat` launch | started as pid 10037, window `VisualCat v2 — See the shape of your log`, **empty stdout and stderr** |
| Native + translated instances together | both ran simultaneously with no interference (partial M8) |

**Translation cost, measured on this host** (`/usr/bin/time -p`, wall seconds for
`vcat --version`):

| Run | `osx-x64` under Rosetta | `osx-arm64` native |
|---|---|---|
| 1st ever (cold, AOT translation) | **4.83** | 0.09 |
| 2nd | 0.17 | 0.08 |
| 3rd | 0.08 | 0.02 |
| 4th | 0.08 | 0.02 |

The first translated launch costs ~4.8 s of one-time Rosetta AOT work; once the
translation cache is warm the x64 CLI settles at ~4× the native process cost on a trivial
command. This is a **first observation with no baseline**, taken on a machine with the
owner's normal applications resident.

**Analytical parity (B-02's `Fail if` clause).** Indexing `gen-threadtime.txt`
(1 800 267 bytes, 20 001 lines, seed 42) with each build produced **identical counters**:

```
sourceBytes 1800267 · sourceLines 20001 · parsedEntries 19998 · timedEntries 19998
metaRecords 1 · unknownLines 2 · rejectedCandidates 0 · continuations 0
untimedEntries 0 · ignoredBlanks 0 · templates 77
```

`COUNTERS EQUAL: True`. The two architectures agree.

### 2.7 I-01 / I-14 — CLI identity and the five-format generator matrix

`vcat --version` → `vcat 2.0.13+06709815674a2ea19dfdb08cd1c552836378c27b`, exit 0.
`vcat help` lists 13 commands and exits 0. The desktop's empty state reports
`2.0.13+0670981`, the same commit prefix — desktop and CLI identities agree.

Generating 20 000 lines at seed 42 in each of the five documented formats and asking the
CLI to detect each file:

| Format | Generated lines | Bytes | Detected as | Confidence |
|---|---|---|---|---|
| `threadtime` | 20 001 | 1 800 267 | `ThreadTime` + `usec` | **1.0** |
| `time` | 20 001 | 1 522 829 | `Time` | **1.0** |
| `brief` | 20 001 | 1 142 867 | `Brief` | **0.667** |
| `long` | 59 997 | 1 702 811 | `LongFormat` | **0.1596** |
| `epoch` | 20 001 | 1 602 821 | `Epoch` + `usec` | **1.0** |

`brief` at 0.667 is structural — brief format carries no timestamp, so the detector cannot
reach 1.0 on it; that is a plan expectation to soften, not a defect. **`long` at 0.1596 is
below the 0.6 auto-detect threshold and is a real defect** — see [F-06](#f-06).

### 2.8 Corpus and oracle (plan §3.1)

Built on the Mac with the candidate CLI, `~/vcat-run/build-corpus.sh`, hashes in
`~/vcat-run/corpus/SHA256SUMS.corpus`, shapes in `corpus-shape.txt`. Every file is LF-only
(`tr -dc '\r' | wc -c` = 0 for all of them) and every original is `chmod a-w` and
`xattr -c`.

| Corpus | Bytes | Lines |
|---|---|---|
| `small.txt` | 90 384 | 1 001 |
| `medium.txt` | 8 998 984 | 100 001 |
| `fmt-threadtime.txt` | 451 040 | 5 001 |
| `fmt-time.txt` | 381 791 | 5 001 |
| `fmt-brief.txt` | 286 791 | 5 001 |
| `fmt-long.txt` | 426 791 | 15 001 |
| `fmt-epoch.txt` | 401 791 | 5 001 |
| `mixed-formats.txt` | 1 566 413 | 30 004 |
| `outcomes.txt` | 411 | 9 |
| `crashy.txt` | 90 952 | 1 011 |
| `quiet-live-seed.txt` | 83 | 2 |

`mixed-formats.ranges.txt` (the byte-range oracle, recorded at composition time and never
re-derived from the product):

```
threadtime starts at 0 for 451040 bytes
brief      starts at 451040 for 286791 bytes
long       starts at 737831 for 426791 bytes
epoch      starts at 1164622 for 401791 bytes
```

`crashy.txt`'s crash block starts at byte **45 312**.

**The `outcomes.txt` oracle matches the plan exactly.** Plan §3.1 requires its 9 source
lines to account as 2 timed + 1 untimed + 1 meta + 2 continuations + 1 unknown + 1
rejected + 1 ignored blank. `vcat index outcomes.txt --format threadtime` reports:

```
sourceBytes 411 · sourceLines 9 · parsedEntries 3 · timedEntries 2 · metaRecords 1
unknownLines 1 · rejectedCandidates 1 · continuations 2 · untimedEntries 1
ignoredBlanks 1 · templates 3
```

2 + 1 + 1 + 2 + 1 + 1 + 1 = 9. Every source line is accounted for exactly once. This is a
strong result: the parser's *accounting* is honest on content designed to hit every
disposition — which is precisely what makes [F-06](#f-06) worse, because there the same
accounting reports zero losses while losing two thirds of the records.

### 2.9 Plan corrections found while executing it

The plan asks for discrepancies to be recorded rather than silently fixed. Three of its
own recipes are wrong on this host.

| Plan text | What actually happens on macOS 26.6.2 | Suggested plan edit |
|---|---|---|
| §2.1, §3: "`sha256sum` … **not** present unless GNU coreutils has been installed" | `/sbin/sha256sum` **exists** — `sha256sum (Darwin) 1.0`, one of six hard links to `/sbin/md5`, and its `-c` mode works against `SHA256SUMS` | Say "absent before macOS 26; present as a Darwin re-implementation from macOS 26". It does not change the README finding ([F-01](#f-01)), which is about macOS 15 and earlier |
| §3.1: `chmod a-w -- *.txt` | **fails**: `chmod: --: No such file or directory`. BSD `chmod` has no `--` end-of-options marker and treats it as a file name | Drop the `--` for `chmod` (keep it for `tar`, `rm`, `shasum`, which do support it). `xattr -c -- *.txt` *does* work |
| §2.2: `system_profiler SPDisplaysDataType` to record "the *looks like* scaled resolution" | macOS 26 prints only `Resolution: 2560 x 1600 Retina` and **no `UI Looks like:` line**, so a scaled mode is invisible to it | Measure instead: `osascript -e 'tell application "Finder" to get bounds of window of desktop'` gives the point size, and the pixel size of a full-screen `screencapture` gives the framebuffer. Their ratio is the backing scale; framebuffer ≠ panel means a scaled mode |

### 2.10 B-11 / I-05 — host ADB discovery and capture against a physical phone

**Verdict: PASS**, with exact marker reconciliation.

Device under test: **Samsung `SM-G990B` (Galaxy S21 FE), serial `RFCRC0A9GND`, Android 16
(API 36), `arm64-v8a`**, fingerprint
`samsung/r9qxeea/r9q:16/BP2A.250605.031.A3/G990BXXSKIZF1:user/release-keys`, attached to the
**Mac's own USB bus** (`ioreg -p IOUSB` → `USB Product Name = SAMSUNG_Android`,
`USB Serial Number = RFCRC0A9GND`).

`adb` used: `/opt/homebrew/bin/adb` → `…/Caskroom/android-platform-tools/37.0.1/platform-tools/adb`,
SHA-256 `1811e253b21b12cbfda7201ebaf86c10e7ddcb5c606a7a81f7c82b4c429c2d3b`,
`Android Debug Bridge version 1.0.41 / 37.0.1-15733141`, **universal binary**
(`lipo -archs` → `x86_64 arm64`), installed for this run with
`brew install --cask android-platform-tools`.

**The `unauthorized` state is real and was met head-on.** The device first appeared as
`RFCRC0A9GND  unauthorized usb:1048576X transport_id:1` and stayed that way through a full
`adb kill-server` / `start-server` cycle with the phone unlocked — macOS's new ADB key had
been generated that minute and the phone never raised its dialog. Clearing
*Developer options → Revoke USB debugging authorizations* and re-plugging produced the
prompt. Worth recording as a genuine first-contact trap: the product's own message for
this state is correct (see [§2.12](#212-a-16--adb-device-state-matrix)), and the failure
was entirely on the device side.

**Desktop capture (the B-11 row).** *ADB live* → the dialog listed the device as
`RFCRC0A9GND · SM_G990B · Device` with the status line
`1 device detected. Unauthorized devices must be approved on the device.` — an accurate,
platform-neutral sentence that names **no Linux mechanism**, which is B-11's macOS-specific
`Fail if`. Default buffers `main`, `system`, `crash` checked; `events`, `radio` unchecked.
Capture ran **11:00:23 → 11:08:35 UTC (8 min 12 s)**.

Live status line samples, read out of the accessibility tree while it ran:

```
Capturing ·    858 lines received · 190/s · ADB device RFCRC0A9GND
Capturing · 14,851 lines received · 157/s · ADB device RFCRC0A9GND
Capturing · 21,270 lines received ·  50/s · ADB device RFCRC0A9GND
Capturing · 34,712 lines received · 169/s · ADB device RFCRC0A9GND
Capturing · 45,814 lines received · 210/s · ADB device RFCRC0A9GND
```

Tab name `ADB RFCRC0A9GND 13h00m23`; timeline, minimap, facets and the entry list all drew
live; `3,262 in view · 18,618 match the filter` mid-run. Process cost during capture:
**37.3 % CPU / 308 MB RSS** at peak, **15.5 % / 345 MB** between bursts (first observation,
on a machine with the owner's normal applications resident).

**Marker reconciliation (plan §3.4).** Two bursts of 300 `log -p i -t VCATTEST` records at
0.2 s, each bracketed by BEGIN/END markers:

| Measure | Value |
|---|---|
| Records emitted on the device | 600 (2 × 300) |
| `vcat search <session> "RUN=<run-id> steady"` | **602** — 600 records + the 2 `adbd` echo lines that quote the loop command |
| `RUN=<run-id> BEGIN` / `END` matches | 4 / 4 (one marker + one `adbd` echo per burst) |
| `chattyDeclaredDrops` in the manifest | **0** |
| `chatty` lines in the device's own buffer | **0** |
| `reconnectGaps` / `reconnectDuplicates` | 0 / 0 |

**600 of 600 delivered, nothing lost, nothing declared.**

Full accounting in the finalized session: `sourceLines 50839 = parsedEntries 49012 +
metaRecords 1827`, with `unknownLines 0`, `rejectedCandidates 0`, `continuations 0`.
`outOfOrderEntries 166` — expected for a multi-buffer merge, and counted rather than hidden.

**What the manifest records about the capture** — this is A-17 and A-18's "records what it
settled on" assertion, and it is complete:

```json
"captureSettings": {
  "requestedBuffers": ["main","system","crash"],
  "preRollSeconds": 0, "includesBufferHistory": false,
  "durationLimitSeconds": null, "byteLimit": null,
  "negotiatedFormat": "threadtime,year,UTC,usec", "logTimeZoneId": "UTC",
  "adbVersion": "Android Debug Bridge version 1.0.41",
  "deviceModel": "SM_G990B",
  "deviceFingerprint": "samsung/r9qxeea/r9q:16/BP2A.250605.031.A3/G990BXXSKIZF1:user/release-keys"
}
```

Note `logTimeZoneId: "UTC"` — a *captured* session correctly records UTC because the
negotiated format carries it. A *file import* on the same host records
`Europe/Bratislava` ([F-07](#f-07)); the two paths do not share the defect.

**CLI leg (I-05).** An 80 s `vcat capture-adb --serial RFCRC0A9GND --buffers main
--pre-roll-seconds 0` run against 300 markers gave the same exact result: 8 148 entries,
`vcat search "RUN=<run-id> steady"` → **301** (300 records + 1 `adbd` echo), matching the
device's own `adb logcat -d | grep -c` of 301 exactly. `vcat verify` → `"isValid": true`,
`"issues": []`. Desktop and CLI discovery agree: both list
`RFCRC0A9GND / Device / SM_G990B / r9qxeea / transport 2`.

**One behaviour worth knowing about `vcat search`:** it matches **message text only, not the
tag**. `search "VCATTEST"` returns 3 (the `adbd` lines that mention the tag in their
message), while `search "steady" --tags VCATTEST` returns 302. That is defensible design —
there is a dedicated `--tags` filter — but a user who types a tag name into the box and
gets 3 hits instead of 302 will conclude the capture is broken. See
[F-12](#f-12).

### 2.11 B-12 — stop is answered, sticky, and complete

**Verdict: PASS.**

*Stop capture* pressed **once**, at 11:08:35 UTC after 8 min 12 s and 49 012 entries.
Within the first sample (**≤ 9.8 s**, and the true latency is below that — the sampler's own
accessibility walk costs seconds) the status read:

```
Stopped · 49,012 entries kept
```

and stayed there across seven consecutive samples spanning 113 s. It never reverted to
`Capturing`. The *Stop capture* button disappeared from the accessibility tree; *Save* and
*Save portable*, disabled before, became enabled.

| Assertion | Result |
|---|---|
| Acknowledged within budget | yes |
| Control never springs back to *Stop* | yes — 7/7 samples `Stopped` |
| Status never returns to *Capturing* | yes |
| `vcat verify` on the finalized session | `"isValid": true`, `"issues": []`, 49 012 entries / 50 839 source records |
| No `adb` child outlives the capture | yes — `pgrep -P <visualcat-pid>` empty; only the shared `adb` **server** (pid 5097, parent `1`) remains, which VisualCat did not start and must not kill |

**Not established:** B-12 also asks that the status "leads with a visibly advancing elapsed
clock and names the stage — draining, compacting, writing the index, reopening". On a
49 000-entry session the finalize completed between two samples, so **no intermediate stage
was ever observed**. This is recorded as not-established rather than passed; it needs a
much larger capture (X-05) or a sub-second sampler to settle.

### 2.12 A-16 — ADB device-state matrix

Driven with purpose-built `adb` stubs that emit realistic space-padded `adb devices -l`
output, so each state is reached deliberately rather than waited for. **Messages from the
shipped 2.0.13 CLI:**

| Device state | Product response |
|---|---|
| `device` | lists `serial / Device / SM_G990B`, capture proceeds |
| `unauthorized` | `Device 'RFCRC0A9GND' has not authorized this computer. Accept the USB debugging prompt on the device and retry.` |
| `offline` | `Device 'RFCRC0A9GND' is offline. Reconnect it or restart the ADB server, then retry.` |
| `no permissions` | `Device 'RFCRC0A9GND' is not ready for capture (state: Unknown).` — parsed as `Unknown`, see below |
| two devices attached | both listed with distinct serials and models; capture binds to the named serial |
| serial not present | `Device 'NOSUCHSERIAL' was not found (connected devices: RFCRC0A9GND). Connect the device and enable USB debugging.` — **returned immediately**, so pre-flight rejects a missing serial before spawning `logcat`, exactly as A-16 requires |

Every message is platform-neutral. **No macOS message names `udev`, a `plugdev` group, or a
rules reload** — verified twice: by running the states above, and by searching the shipped
assemblies, where `udev` and `plugdev` appear **zero** times (`No devices detected` and
`Unauthorized devices must be approved` are both present as UTF-16 strings, so the search
method is sound).

**Checked ahead for `main`, and it is correct too.** `main` adds `UsbDeviceAccess` with a
`udev`/`plugdev` remedy for the Linux-only case where ADB hides a device whose USB node the
account cannot read. Reading the call sites: `UsbDeviceAccess.UnopenableAdbDevices()`
returns `[]` when `!OperatingSystem.IsLinux()`, so `MissingDeviceExplanation()` is `null` off
Linux and both `AdbCaptureDialog.SetNormalDeviceStatus()` and `AdbLogSource.NotFoundMessage()`
take their plain branch; `NoPermissionsMessage()` gates its remedy behind
`OperatingSystem.IsLinux()` explicitly. The macOS text stays generic. **No finding** — recorded
because B-11's macOS `Fail if` targets exactly this, and a reader should know it was checked
rather than assumed.

`no permissions` being parsed as `Unknown` is a 2.0.13-only gap; `main` has
`AdbDeviceState.NoPermissions`. It matters little on macOS, where USB access is not
group-gated — the macOS analogue is the *Allow accessory to connect* prompt on Apple-silicon
laptops, which the product names nowhere. See [F-12](#f-12).

### 2.13 A-15 / I-05 — ADB locator precedence on macOS

Every route was exercised with `PATH` stripped to `/usr/bin:/bin:/usr/sbin:/sbin` so a
fallback cannot mask a failure.

| Route | Probed? | Result |
|---|---|---|
| `--adb <valid path>` | yes | device listed |
| `ANDROID_SDK_ROOT/platform-tools/adb` | yes | device listed |
| `ANDROID_HOME/platform-tools/adb` | **yes** — though `CLI.md` and the error message never mention it | device listed |
| `<LocalApplicationData>/Android/Sdk/platform-tools/adb`, i.e. `~/Library/Application Support/Android/Sdk/…` | yes | device listed |
| the same path spelled `…/Android/sdk/…` | yes **on this case-insensitive volume** | device listed — would fail on a case-sensitive APFS volume |
| each `PATH` entry in order | yes | device listed |
| **`~/Library/Android/sdk/platform-tools/adb`** — where Android Studio installs the SDK on macOS | **no** | `error: ADB was not found.` See [F-11](#f-11) |

Negative paths for an explicit `--adb`:

| `--adb` value | Behaviour | Correct? |
|---|---|---|
| a file that is not executable (`0644`) | `error: An error occurred trying to start process '/tmp/noexec-adb' … Permission denied`, exit 1 | acceptable — late, but specific |
| a dangling symlink | `… No such file or directory`, exit 1 | yes |
| an executable that exits non-zero | `error: ADB device discovery failed: FAKE-ADB-WAS-RUN`, exit 1 | yes |
| **a directory (`/tmp`)** | **silently ignored — falls through to `PATH`, lists the device, exit 0** | **no** — [F-10](#f-10) |
| **a path that does not exist** | **silently ignored — falls through to `PATH`, lists the device, exit 0** | **no** — [F-10](#f-10) |

### 2.14 B-14 / P-22 — saving, and the file modes that come with it

*Save* and *Save portable* both work through a **native macOS save sheet** (`sheet 1` of the
main window, not a free-floating panel — the correct platform presentation), and both
produced sessions that `vcat verify` accepts:

| Save | `isValid` | entries | source records |
|---|---|---|---|
| standard | true | 49 012 | 50 839 |
| portable | true | 49 012 | 50 839 |

No extended attributes were attached to either (`xattr -lr` empty), so nothing about the
save is quarantined or Finder-tagged.

**But the modes violate the published privacy contract — see [F-08](#f-08).**
[`PRIVACY.md`](PRIVACY.md) promises session directories `700` and a portable `raw.log`
`600`, *"wherever the session is written"*, and says explicitly that VisualCat "does not
leave it to the account's `umask`". With this Mac's `umask 022`, every single object is
world-readable:

```
drwxr-xr-x  <saved>.vcat
-rw-r--r--  <saved>.vcat/raw.log          6 716 288 bytes of this phone's log
-rw-r--r--  <saved>.vcat/manifest.json
-rw-r--r--  <saved>.vcat/source-order/records.bin
drwxr-xr-x  <portable>.vcat
-rw-r--r--  <portable>.vcat/raw.log       ← PRIVACY.md says 600
```

**One more macOS-shaped observation, filed as an improvement rather than a defect.** A
`.vcat` session is a plain directory, and macOS has a first-class concept for
"a directory the user should treat as one document" — a *package*. Because VisualCat ships
no `.app` bundle and declares no exported UTI, Finder shows a session as an ordinary folder
and the **save panel lets you navigate into one**. That is not hypothetical: during this run
a portable save landed *inside* `cli-capture.vcat/`, producing a session nested in a
session. Both still verified (`verify` ignores unknown subdirectories), so nothing was
corrupted — but a user can do this by accident, and on macOS a session is also the thing
they will drag between machines. See [F-13](#f-13).

### 2.15 U-07 / U-08 — the accessibility tree is genuinely good

Recorded deliberately as a **strength**, because a first impression suggested the opposite.
A depth-9 walk of the main window returns nothing but unnamed `AXGroup`s, which looks like a
catastrophic accessibility failure. It is not — the content simply sits **11 to 25 levels
deep**, and a walker that stops early sees nothing. At full depth the tree is rich and
correctly labelled:

```
AXButton :: [＋  Open log]                                     d=11
AXButton :: [●  ADB live]                                      d=11
AXButton :: [Open session] / [Recent] / [Follow file] …        d=11
AXButton :: [Show in progress session ADB RFCRC0A9GND 13h00m23] d=15
AXButton :: [Close session ADB RFCRC0A9GND 13h00m23]           d=15
AXCheckBox:: [Regex] / [Case-sensitive]                        d=18
AXButton :: [Apply the query]                                  d=18
AXButton :: [Fatal level] … [Verbose level] / [Unknown level]  d=19
AXButton :: [Zoom out] / [Fit the complete session] / [Zoom in] d=18
AXButton :: [Follow: on] / [Stop capture]                      d=18
AXButton :: [Show the full message of the selected entry]      d=22
AXRadioButton :: [Templates] / [Facets] / [Views] / [Session]  d=23
AXButton :: [Filter to selected template] / [Mute …] / [Copy …] d=25
```

The names are *descriptive of the action*, not just the visible glyph —
`Show the full message of the selected entry`, `Fit the complete session`,
`Close session ADB RFCRC0A9GND 13h00m23` — which is what a screen-reader user actually
needs. The ADB dialog is equally well covered: `AXPopUpButton "Android device"` carrying
`RFCRC0A9GND · SM_G990B · Device`, five named `AXCheckBox` buffers, three named
`AXIncrementor`s, and `Refresh devices` / `Cancel` / `Start capture`.

The remaining gap is **depth**, not labelling: 25 levels of nesting is a lot of `VO-→` for a
VoiceOver user to traverse, and the intermediate containers carry no `AXTitle`, so there are
no landmarks to jump between. A full VoiceOver pass is still outstanding.

### 2.16 B-05 — importing through the macOS file chooser, and what TCC actually did

**Verdict: PASS for the import; the Q6 consent story came out differently from the plan's
expectation, and that difference is the result.**

`Open log` → the native chooser appeared as a **sheet attached to the main window**
(`sheet 1 of window 1` — the correct macOS presentation, and notably *not* how the
product's own dialogs are presented, see [F-09](#f-09)). ⇧⌘G accepted a typed path.
Time from click to chooser: **3.32 s** — slow enough to notice, recorded as a first
observation with no baseline.

The file opened was `~/Desktop/vcat-b05-small.txt`, a copy of the 1 000-entry `small.txt`.

**No privacy prompt appeared, for any of the three protected folders.** That is not a
product defect; it is the §2.6 note playing out exactly as written, and it is worth stating
precisely because a tester could easily record it as "TCC works":

```
kTCCServiceSystemPolicyAllFiles | /usr/libexec/sshd-keygen-wrapper | 2   ← Full Disk Access
kTCCServiceSystemPolicyAllFiles | com.apple.Terminal                | 0   ← denied
kTCCServiceSystemPolicyDesktopFolder   | com.apple.Terminal | 2
kTCCServiceSystemPolicyDocumentsFolder | com.apple.Terminal | 2
kTCCServiceSystemPolicyDownloadsFolder | com.apple.Terminal | 2
```

VisualCat was launched from an SSH shell, so its responsible process is
`sshd-keygen-wrapper`, which holds **Full Disk Access** on this Mac. VisualCat therefore
inherited unrestricted access to every protected folder without a single prompt, and
nothing on screen ever named VisualCat. Launching from Terminal.app instead would inherit
Desktop/Documents/Downloads but not Full Disk Access. **On this host there is no launch
path that produces a prompt naming the product**, because a bare executable has no identity
of its own to prompt about.

A true Q6 pass needs a launching terminal with no grants at all. iTerm2 was installed
during this run for that purpose; the pass itself is **still outstanding** and is on the
[§4](#4-standing-list--what-is-still-untested) list.

**Import preview.** The review appeared even though detection was at 100 % confidence:

```
vcat-b05-small.txt
Preview of up to the first 200 lines · 200 complete lines · 17.7 KiB retained
Detected sample format: Thread time (100 % confidence)
Parsing preview with auto-detection
199 parsed · 0 unknown · 0 rejected
Sample time span: 2026-05-15 14:13:37.000000 +02:00 — 2026-05-15 14:13:37.305000 +02:00 · Europe/Bratislava
Year follows source reference date 2026-09-14
No preview warnings.
```

B-05 expects that "a confidently detected file is not interrupted by the review". It was.
Whether *Open log* is meant to always preview — with *Open log with options…* existing for
the same purpose — is a question for the product owner rather than an obvious defect, so it
is recorded as an observation, not filed. The dialog's own layout is a finding
([F-15](#f-15)). `Europe/Bratislava` appears here too, wrapped across two lines
([F-07](#f-07)).

Import completed in **< 1.9 s** including dismissing the dialog. The tab took the file's
name, and the status read **`Ready · 1,000 entries`**.

### 2.17 B-06 / B-07 / B-08 — the analysis path, checked against an oracle the product did not produce

Every number below was predicted by an `awk` pass over the corpus **before** the product was
asked, so the product is never its own oracle.

**Independent oracle** (`awk` on the threadtime header shape, no product code):

```
meta=1  headers=1000  blank=0  other=0
level V=188  D=145  I=164  W=182  E=165  F=156
tags: Camera=161 Network=155 AndroidRuntime=147 SurfaceFlinger=145 ActivityManager=135
      chatty=130 VisualCat=127   (7 distinct)
first: 05-15 14:13:37.000000   last: 05-15 14:13:38.501000
```

**B-06 · heat map to exact source bytes — PASS, byte-exact.**

The six severity rows read `F 156 · E 165 · W 182 · I 164 · D 145 · V 188`, and the footer
`1,000 in view · 1,000 match the filter · 1,000 in session · 05-15 14:13:37.000 — 05-15
14:13:38.501`. Every one matches the oracle.

Selecting the entry `Warn ActivityManager at 05-15 14:13:37.001: FATAL EXCEPTION: main` and
opening **SOURCE CONTEXT** produced a gutter that starts at line 1, carries a per-line
disposition code with a legend (`en entry · mt marker · .. continuation · e? untimed ·
?? unknown · !! rejected`), and marks the selected line with `▶`:

```
 1 mt │ --------- beginning of main
 2 en │ 05-15 14:13:37.000000 10503  5136 D Camera          : Rendering surface 0x000043D5
 3 en │ 05-15 14:13:37.001000  6472 11490 D VisualCat       : Started process 81413 for package com.example.app
▶4 en │ 05-15 14:13:37.001000 14132 24503 W ActivityManager : FATAL EXCEPTION: main
 5 en │ 05-15 14:13:37.003000   926  9315 W Camera          : Started process 53572 for package com.example.app
…
```

Verified against the file with a byte-oriented reader, not a line tool. An independent
scan put line 4 at **offset 215, length 75**:

```shell
$ dd if=~/Desktop/vcat-b05-small.txt bs=1 skip=215 count=75 | xxd
00000000: 3035 2d31 3520 3134 3a31 333a 3337 2e30  05-15 14:13:37.0
00000010: 3031 3030 3020 3134 3133 3220 3234 3530  01000 14132 2450
00000020: 3320 5720 4163 7469 7669 7479 4d61 6e61  3 W ActivityMana
00000030: 6765 7220 3a20 4641 5441 4c20 4558 4345  ger : FATAL EXCE
00000040: 5054 494f 4e3a 206d 6169 6e              PTION: main
```

Byte for byte what the product displayed. The inspector header
(`W ActivityManager · 05-15 14:13:37.001 · 14132:24503 · main · tpl 3`) names the same
record. Gutter numbering agrees with an independent line count.

**B-07 · severity filters and clear semantics — PASS, exactly.**

| Action | Product | Oracle |
|---|---|---|
| baseline | 1 000 | 1 000 |
| hide **F** | 844 | 1 000 − 156 = 844 |
| hide **F, E** | 679 | − 165 = 679 |
| hide **F, E, W** | 497 | − 182 = 497 = I 164 + D 145 + V 188 |

Every active filter is named in the chip bar (`levels: hiding F,E,W`, with a
`Remove filter levels: hiding F,E,W` button beside `Clear all`). The time span narrowed
correctly while filtered (`…38.500` instead of `…38.501`, because the last Fatal entry was
hidden) and returned to `…38.501` on clear. After **Clear all**: 1 000 in view, chip bar
back to `No filters · showing everything in view`, **no residual filter buttons in the
accessibility tree**, and all seven level toggles back to `true`. No residue.

**B-08 · text and regex search — PASS, with good failure behaviour.**

| Query | Mode | Product | Independent oracle |
|---|---|---|---|
| `FATAL EXCEPTION` | text | 139 | `grep -c` → 139 |
| `FATAL EXCEPTION` | regex | 139 | 139 |
| `^Rendering surface` | regex | 155 | `awk` on the message field → 155 |
| `Connection [0-9]+ to 10\.0\.0\.[0-9]+` | regex | 148 | `grep -cE` → 148 |
| `(a+)+b` | regex | 1 | correct — matches `…EDAB` case-insensitively, and **returned promptly**; no catastrophic backtracking |
| `[` | regex | *rejected* | — |

The invalid regex is handled well, and this is worth quoting because it is the behaviour a
user meets when they mistype:

```
Not a valid regular expression: a "[" character class was never closed with "]" (position 1).
```

— precise, positional, and **the previous query stays applied**: the chip bar still read
`regex = (a+)+b` with an × to remove it, and the view was not destroyed. That is the right
call.

The search field exposes `AXPlaceholderValue = "Search message text or regex…"` but has no
`AXTitle` or `AXDescription`. VoiceOver reads a placeholder only while the field is empty,
so once the user types, the field announces as an unnamed text field. One line to fix; see
[F-16](#f-16).

### 2.18 U-06 — the keyboard contract

**Focus order: PASS, and better than the plan requires.** Tab cycles **37 stops** and wraps
cleanly. Every stop carries a descriptive accessible name, including the two custom-drawn
surfaces:

```
 1 ＋  Open log            14 (search field)         27 Zoom in
 2 ●  ADB live            15 Regex                  28 Severity by time heat map
 3 Open session           16 Case-sensitive          29 Full-session minimap and viewport brush
 4 Recent                 17 Apply the query         30 (splitter handle)
 5 Follow file            18 Fatal level             31 Hide insights
 6 Open archive           19 Error level             32 More entry actions
 7 Save                   20 Warn level              33 (entry row, named per entry)
 8 Save portable          21 Info level              34 Toggle the selected entry inspector
 9 Export                 22 Debug level             35 (splitter handle)
10 More ▾                 23 Verbose level           36 Templates
11 Show complete session <name>  24 Unknown level    37 (template row, named per template)
12 Close session <name>   25 Zoom out
13 …per open session…     26 Fit the complete session
```

Row names are full sentences —
`Debug Camera at 05-15 14:13:37.000: Rendering surface 0x000043D5` and
`33 entries: Rendering surface <*>, from 05-15 14:13:37.105 to 05-15 14:13:38.496` — which
is exactly what a screen-reader user needs. Arrow keys move the selection inside a list.

**Not established: the documented shortcuts.** [`KEYBOARD.md`](KEYBOARD.md) documents
`Ctrl+O`, `Ctrl+Shift+O`, `Ctrl+E`, `Ctrl+F`, `Ctrl+G`, `F3`, and states no macOS mapping.
Neither `Ctrl+F` nor `⌘F` moved focus to the search field in this harness, and neither
`Ctrl+O` nor `⌘O` opened a chooser — but the accessibility focus probe also reported
`no focused element` throughout, so the harness cannot distinguish "the shortcut did
nothing" from "the app had no keyboard focus to give it to". **This row needs a human at
the keyboard** and is on the [§4](#4-standing-list--what-is-still-untested) list. What is
*not* in doubt is the documentation gap: `KEYBOARD.md` has no macOS column, and on macOS the
primary modifier is ⌘ — see [F-03](#f-03), suggestion 4.

The same caveat applies to mouse selection: synthetic clicks operate buttons reliably
(every dialog in this report was driven that way) but did **not** change the entry-list
selection, while setting `AXSelected` through the accessibility API did. That is most likely
a synthetic-input artifact rather than a product defect, and it is recorded as
not-established rather than filed.

### 2.19 A-11 / X-28 — what survived the crash

The crash in [F-14](#f-14) is also, accidentally, a real crash-recovery test. On relaunch:

- **Both sessions survived.** *Recent* listed `vcat-b05-small · 2026-09-14 13:24 ·
  191.44 KiB · complete` and `ADB RFCRC0A9GND 13h00m23 · 2026-09-14 13:08 · 17.25 MiB ·
  complete` — including the 8-minute ADB capture, which had been finalized but whose tab
  was open when the process aborted. Reopening `vcat-b05-small` from *Recent* worked and
  gave back 1 000 entries.
- **The workspace did not.** The app came back to the empty state with no tabs, although
  `visualcat-20260914-000.jsonl` records
  `{"Name":"workspace.persisted","Properties":{"openSessionCount":"2","selectedIndex":"1"}}`
  written 17 minutes before the crash. So the workspace *is* persisted and simply is not
  restored after an abnormal exit. Whether that is deliberate is a product decision; either
  way, a user who loses the app mid-analysis gets their data back but not their place, and
  is not told that *Recent* is where to look. Filed as [F-17](#f-17).
- **`Recent captures` itself is well built**: the explanatory line *"These captures are
  stored in temporary storage. Saving a capture keeps a copy in a location you choose."*,
  `Select all` / `Clear`, per-capture checkboxes whose accessible names carry name, date and
  size, a `Capture states` filter, and `Delete captures…` separated from `Close` / `Open`.

---

## 3. Findings

### F-14 · Major · VisualCat aborts inside Avalonia's macOS accessibility bridge while announcing a live-region change

**Severity** Major — an unhandled Objective-C exception aborts the process with no product
message, no managed stack, and no chance to save. It sits on the accessibility path, so the
users most exposed are the ones least able to recover.

**Where** `libAvaloniaNative.dylib`, `-[AvnAccessibilityElement raiseLiveRegionChanged]`,
reached from every `AutomationProperties.SetLiveSetting(…)` surface in the product —
`MainView.Notice.cs:223,451`, `MainView.FileOperations.cs:98`,
`AdbCaptureDialog.cs:70,71`, `ImportPreviewDialog.cs:123-125`,
`FacetBrowserDialog.cs:305,306`, `NumberPromptDialog.cs:101`. Avalonia **12.1.1**.

**What happened.** After ~2 h 40 m of ordinary use — an 8-minute ADB capture, a standard
save, a portable save, a file import, filter and search work — with an accessibility client
attached, the process died:

```
Exception Type:  EXC_CRASH (SIGABRT)
Termination:     SIGNAL 6  Abort trap: 6
asi:             libsystem_c.dylib: "abort() called"
Faulting thread: 0  com.apple.main-thread
```

The last Objective-C exception backtrace names the frame exactly:

```
  __exceptionPreprocess
  objc_exception_throw
  -[__NSPlaceholderDictionary initWithObjects:forKeys:count:]
  +[NSDictionary dictionaryWithObjects:forKeys:count:]
  -[AvnAccessibilityElement raiseLiveRegionChanged]       ← libAvaloniaNative.dylib
  …managed frames…
```

`+[NSDictionary dictionaryWithObjects:forKeys:count:]` raises `NSInvalidArgumentException`
when **any key or value is nil**. `raiseLiveRegionChanged` builds the `userInfo` dictionary
for `NSAccessibilityPostNotificationWithUserInfo` — `NSAccessibilityAnnouncementKey` plus
`NSAccessibilityPriorityKey`. If the element's accessibility label resolves to `nil` at the
moment the live region fires, that literal throws, nothing catches it, and the runtime
aborts. Evidence: `~/Library/Logs/DiagnosticReports/VisualCat-2026-09-14-134131.ips`
(64 400 bytes), pid 6166, `parentProc launchd`, `translated false`.

**Not reproduced.** Roughly fifteen targeted attempts failed to trigger it again: the exact
pre-crash keystroke sequence, repeated invalid-regex notices (which *are* an `Assertive`
live region), filter changes, search, session reopen, and the shortcut matrix. So this is
**one observed abort with a definitive native stack**, not a recipe. It is filed as Major
rather than Blocker on that basis, and the [§4](#4-standing-list--what-is-still-untested)
list carries the reproduction attempt.

**Why it matters more than a one-off crash normally would.**

- The crash is **on the accessibility path**, so it fires for exactly the users who depend
  on VoiceOver, Switch Control, Voice Control, or Dictation — and for any automation. A
  sighted user with no assistive technology running may never see it, which is also why it
  could ship unnoticed.
- macOS's own crash dialog reads **"Avalonia Application quit unexpectedly."** (captured in
  `p161-recent.png`). A user cannot tell which application died, cannot search for it, and
  cannot file a useful report. This is [F-03](#f-03) turning a bad moment into an
  unreportable one.
- An abort during a live ADB capture would end the capture with no notice.

**Suggested fix.** Two independent layers, because either alone leaves a gap.

1. **In the product — never let a live region have an empty accessible name.** Every
   element that carries `AutomationLiveSetting` should be given a non-empty
   `AutomationProperties.Name` *before* the live setting is attached, and should never be
   allowed to fall back to empty. The clearest instance is
   `MainView.FileOperations.cs`, where the announcement element is constructed with no text
   and no name, given a live setting at line 98, and only named at line 221 — **after** the
   `Text` assignment at line 220 that raises the change:
   ```csharp
   // Name first: setting Text raises the live region, and on macOS a live region whose
   // accessible name is nil aborts the process inside NSDictionary (F-14).
   if (!string.Equals(stage, _fileOperationAnnouncement.Text, StringComparison.Ordinal))
   {
       AutomationProperties.SetName(_fileOperationAnnouncement, stage);
       _fileOperationAnnouncement.Text = stage;
   }
   ```
   and give it a non-empty name at construction. Apply the same ordering wherever a live
   region's text and name are set together, and add an assertion in a debug build that a
   live-region element's effective name is never null or empty.
2. **Upstream — the framework must not build a dictionary that can throw.** The correct
   shape is to bail out rather than post an announcement with no text:
   ```objc
   NSString* announcement = [self accessibilityLabel];
   if (announcement.length == 0) { return; }   // nothing to say; never throw
   NSAccessibilityPostNotificationWithUserInfo(
       self, NSAccessibilityAnnouncementRequestedNotification,
       @{ NSAccessibilityAnnouncementKey : announcement,
          NSAccessibilityPriorityKey     : @(priority) });
   ```
   This repository already tracks upstream Avalonia findings (`99e3947 Re-check the two
   upstream findings against the newest Avalonia`), so this belongs on that list with the
   `.ips` attached — it is a one-line guard with a clear crash report behind it.
3. **Make the crash reportable.** Whatever the cause, a user should be able to say *what*
   crashed. Fixing the application name ([F-03](#f-03)) changes the dialog from
   "Avalonia Application quit unexpectedly" to "VisualCat quit unexpectedly", and the
   `.ips` from `procName VisualCat` with `app_version ""` to one carrying the real version —
   note that `app_version` and `build_version` are both **empty strings** in this report,
   because a bare executable has no `Info.plist`. Add a mention of
   `~/Library/Logs/DiagnosticReports/` to [`SUPPORT.md`](SUPPORT.md)'s bug-reporting
   section so a macOS user knows where the evidence is.

**Appendix-B trap checks.** Not a translated-execution artifact — `translated: false`, this
was the native `osx-arm64` build. Not an out-of-memory or jetsam kill — the termination is
`SIGNAL 6 Abort trap` from `abort()` after an uncaught ObjC exception, not `EXC_RESOURCE` or
a jetsam event. Not a forced kill — `byProc: VisualCat`, `byPid: 6166`, i.e. the process
aborted itself. Not a display-sleep or screen-lock artifact — the display was held awake by
`caffeinate -dimsu` from 11:1x onward, and the crash is at 11:41:31 UTC.

---

### F-15 · Minor · Two dialogs reserve roughly half their height for nothing

**Severity** Minor — pure layout waste, but on a 1440 × 900-point desktop it pushes the
buttons a long way from the content the user is reading, and it makes both dialogs look
broken.

**Where** `ImportPreviewDialog`, `AdbCaptureDialog`.

**What happens.**

| Dialog | Size | Content ends at | Empty |
|---|---|---|---|
| `Import preview — vcat-b05-small.txt` | 720 × 688 pt | ~250 pt | **~64 %** |
| `Live ADB capture` | 600 × 438 pt | ~300 pt | **~31 %** |

Evidence: `b05-import-preview.png`, `b11-adb-dialog.png`. In the import preview the eight
lines of summary sit at the top, the collapsed *Import options* disclosure sits under them,
and then there are roughly 430 points of nothing before *Cancel* and *Import* in the bottom
right corner. The user reads at the top and clicks 430 points lower.

The height is presumably reserved for *Import options* when expanded. Reserving it while
collapsed is the defect: the dialog should size to its current content and grow when the
disclosure opens, which is what every macOS disclosure does.

**Suggested fix.**

1. Let both dialogs size to content (`SizeToContent="WidthAndHeight"` with a sensible
   `MaxHeight`), and let the disclosure's expansion resize the window. If a jump on expand
   is unwanted, animate the height change rather than pre-reserving it.
2. Put the action buttons directly under the content rather than anchored to the window
   bottom, so they stay with what they act on at every size.
3. While here: both dialogs are free-floating windows with their own traffic lights, and
   the *Live ADB capture* dialog has an **enabled minimise button** while it is modal. See
   [F-09](#f-09) suggestion 3 — presenting them as sheets fixes the sizing, the ownership
   and the minimise trap in one change, and matches the native file chooser this same
   application already presents correctly.

**Appendix-B trap checks.** Not a scaled-resolution artifact — the measurements are in
points from the accessibility API (`AXSize`), not pixels from the screenshot, so the
display's 1440 × 900 scaled mode does not enter into them. Not a font-fallback artifact —
the text renders at the expected size and is not clipped.

---

### F-16 · Polish · The search field has no accessible name, and the selected-entry legend collides with the status bar

**Severity** Polish — two small blemishes on an otherwise strong accessibility and layout
story, grouped because each is a one-line fix.

**1. The search field announces as an unnamed text field once it has content.**
The field exposes `AXPlaceholderValue = "Search message text or regex…"` but no `AXTitle`
and no `AXDescription`. VoiceOver reads a placeholder only while the field is empty, so a
user who types a query and tabs away and back hears "text field" with no name. Every other
control in the window is named; this is the one gap. Fix:

```csharp
AutomationProperties.SetName(_searchBox, "Search message text or regex");
AutomationProperties.SetHelpText(_searchBox,
    "Matches message text. Use the Regex checkbox for a regular expression, and Tags to match a tag.");
```

The help text also carries the answer to [F-12](#f-12)'s second point, where a user expects
a tag search and gets none.

**2. The selected-entry legend is drawn under the status bar.** With the *SELECTED ENTRY*
inspector open on a 795-point-tall window, the legend line
`en entry · mt marker · .. continuation · e? untimed · ?? unknown · !! rejected` is painted
in the same band as `Ready · 1,000 entries`, and the two overlap (visible in
`b06-selected.png` and `b06-sel4.png`). Nothing is lost — the legend is also reachable by
scrolling — but the overlap makes both unreadable at the one window size this Mac's default
scaled resolution gives a maximised window. Fix: put the legend inside the inspector's own
scroller rather than letting it extend past the pane's bottom edge, and give the status bar
a real row in the layout grid so nothing can be painted over it.

**Appendix-B trap checks.** The AX attributes were read from the live process, not inferred.
The overlap is visible in two independent captures taken minutes apart, at the same window
size, and is not a capture-timing artifact.

---

### F-17 · Minor · After a crash the sessions come back but the workspace does not, and nothing says where to look

**Severity** Minor — no data is lost, which is the important part. The cost is that a user
whose app died mid-analysis is shown an empty start page and has to work out for themselves
that their work is under *Recent*.

**Where** Workspace restoration on start-up.

**What happens.** The crash in [F-14](#f-14) killed a process with two open tabs. The
diagnostics log shows the workspace *was* persisted:

```json
{"TimestampUtc":"2026-09-14T11:24:11.817623+00:00","Subsystem":"main-view",
 "Name":"workspace.persisted","Properties":{"openSessionCount":"2","selectedIndex":"1"}}
```

On relaunch the app showed the **empty state** — headline, chips, and three text actions.
Both sessions were intact and reachable through *Recent captures* (`vcat-b05-small ·
191.44 KiB · complete` and `ADB RFCRC0A9GND 13h00m23 · 17.25 MiB · complete`), and
reopening one restored all 1 000 entries. So recovery works; only the handoff is missing.

**Expected.** Either the workspace is restored, or the user is told in one sentence that the
previous session ended unexpectedly and where their captures are.

**Suggested fix**, in increasing order of ambition:

1. **Say it.** On a start-up that follows a persisted workspace which was never closed
   cleanly, show a notice on the empty state:
   *"VisualCat closed unexpectedly. Your 2 captures are safe — open them from Recent."*
   with *Recent* as the action. One notice, one button, and the user never has to guess.
   Set a "clean shutdown" marker beside the persisted workspace and clear it on an orderly
   exit; its absence is the trigger.
2. **Offer the restore.** Add *Restore previous session* beside it, reopening the tabs that
   `workspace.persisted` recorded. Keep it an offer rather than automatic — reopening a
   17 MB capture unasked is its own annoyance.
3. Record the crash in the product's own diagnostics on the *next* start, so a diagnostic
   bundle collected afterwards contains the fact that a crash happened. Today the
   `.jsonl` simply stops, and the only trace is the macOS `.ips` the user does not know
   about.

**Appendix-B trap checks.** The persisted-workspace line was read from the product's own
diagnostics file, not inferred. The sessions' survival was verified by reopening one and
comparing its entry count with the pre-crash value.

---

### F-01 · Minor · The shipped macOS `README.txt` tells the user to run a command macOS did not have until macOS 26

**Severity** Minor — a documentation defect in the *release artifact itself*, on the one
instruction whose whole purpose is to let a user verify the download before executing it.

**Where** `README.txt` inside `VisualCat-Desktop-osx-*-v2.0.13.tar.gz` and
`VisualCat-CLI-osx-*-v2.0.13.tar.gz`; generated by the packaging step.

**What it says**

```
Verify this download
--------------------

Compare the archive against SHA256SUMS on the release page:

  sha256sum -c SHA256SUMS
```

**What happens.** `sha256sum` is a GNU coreutils command. The BSD userland that ships with
macOS provides `shasum` and, historically, nothing named `sha256sum` at all. On this host
it happens to work:

```shell
$ ls -la /sbin/sha256sum
-rwxr-xr-x  6 root  wheel  136288 Aug 13 04:51 /sbin/sha256sum
$ /sbin/sha256sum --version
sha256sum (Darwin) 1.0
```

— because macOS 26 ships a Darwin re-implementation as one of six hard links to `/sbin/md5`.
On macOS 15 and earlier the same line is `zsh: command not found: sha256sum`, and the user's
only remaining option is to skip verification or go find the right spelling themselves.

**Expected.** The macOS README should print the macOS spelling. The plan itself (§2.1,
§3) states flatly that `sha256sum` is "**not** present unless GNU coreutils has been
installed", which is the assumption the packaging step should have been written against.

**Suggested fix.**

1. In the packaging template that emits `README.txt`, make the verify line
   platform-specific rather than shared:
   ```
   shasum -a 256 -c SHA256SUMS        # macOS
   sha256sum -c SHA256SUMS            # Linux
   ```
   Emit only the line for the artifact's own RID. `shasum` is present on every supported
   macOS and on every mainstream Linux with Perl, so a single `shasum` line for both Unix
   artifacts is also acceptable and is one fewer branch.
2. `SHA256SUMS` uses the `*filename` (binary-mode) marker. `shasum -a 256 -c` accepts it;
   so does the Darwin `sha256sum`. No change needed there, but the README should say to
   run the command **in the directory holding the archives**, because both tools report
   `FAILED open or read` for the nine assets the user did not download and a user who has
   not seen that before reads it as a failed verification. A `--ignore-missing` hint costs
   one word:
   ```
   shasum -a 256 --ignore-missing -c SHA256SUMS
   ```

**Appendix-B trap checks.** Not a shell artifact — reproduced from `/bin/zsh` and from
`/bin/bash`. Not a PATH artifact — `/sbin` is on the default macOS `PATH`. The `-c`
behaviour was confirmed live on this host, and the archive's README bytes were read
directly out of the extracted tree, not from the repository.

---

### F-02 · Minor · The macOS tarballs have no wrapper directory, so a normal extraction scatters 240 files

**Severity** Minor — first-run friction and a cleanup hazard, on the platform where the
product ships no installer and no `.app`, so this *is* the installation experience.

**Where** Release packaging for `VisualCat-Desktop-osx-*.tar.gz` and
`VisualCat-CLI-osx-*.tar.gz`.

**What happens.** Every archive member is `./<name>`:

```shell
$ tar -tzf VisualCat-Desktop-osx-arm64-v2.0.13.tar.gz | awk -F/ '{print $1}' | sort -u
.
$ tar -tzf VisualCat-Desktop-osx-arm64-v2.0.13.tar.gz | grep -c '^\./[^/]*/'
0
```

There is exactly one directory member, `./`, and 239 file members beside it. The README
says nothing about extracting into a directory; its first instruction after the download
is `./VisualCat`. A user who double-clicks the archive in Finder gets the Archive Utility
behaviour (a containing folder, because Archive Utility adds one when the archive has no
single root), but a user who follows the terminal path the README implies —
`cd ~/Downloads && tar -xzf VisualCat-Desktop-osx-arm64-v2.0.13.tar.gz` — drops 240 files
into `~/Downloads` mixed with everything already there. Undoing that by hand is genuinely
hard, because the names are generic .NET assembly names.

**Expected.** Release archives conventionally contain a single top-level directory named
after the artifact, so that extraction is self-contained and reversible with one `rm -rf`.
The plan's §2.4 explicitly lists "wrapper-directory surprises" as something to reject at
the trust boundary.

**Suggested fix.**

1. Pack from the parent so the archive has one root:
   ```shell
   # instead of: tar -czf out.tar.gz -C publish .
   mv publish "VisualCat-Desktop-osx-arm64-v$VERSION"
   tar -czf out.tar.gz "VisualCat-Desktop-osx-arm64-v$VERSION"
   ```
   Keep the root name identical to the archive basename minus `.tar.gz`, which is what
   users expect and what makes `tar -tzf | head -1` a useful sanity check.
2. Update both macOS `README.txt` templates to open with
   `tar -xzf VisualCat-Desktop-osx-arm64-v2.0.13.tar.gz && cd VisualCat-Desktop-osx-arm64-v2.0.13`.
3. Apply the same change to the Linux tarballs for consistency; the Windows `.zip` should
   get the same treatment for the same reason.
4. Add a release-workflow assertion that every `tar.gz` has exactly one top-level entry
   and that it is a directory — a two-line check that makes this unable to regress.

**Appendix-B trap checks.** Not an extraction-tool artifact: the member list was read
straight from the archive with `tar -tzf`, before any extraction. Not a Windows-packaging
artifact: the archive's members are owned by `runner staff` and carry Unix modes, so it
was produced on the macOS workflow runner.

---

### F-03 · Major · On macOS the product identifies itself as "Avalonia Application" in the menu bar, the About box, Hide, and Force Quit — and ships no menus at all

**Severity** Major — on macOS the menu bar *is* the application's identity and its primary
command surface. This is the first thing a Mac user sees after launching, it is wrong, and
it is wrong in a way that names a third-party framework rather than the product.

**Where** macOS desktop head. Avalonia's `NativeMenu` / `NSApplication` integration is left
at its stock default; nothing in the app sets the macOS application name or installs a
menu.

**What happens.** With VisualCat frontmost:

```shell
$ osascript -e 'tell application "System Events" to tell process "VisualCat" \
    to get name of every menu bar item of menu bar 1'
Apple, Avalonia Application

$ osascript -e 'tell application "System Events" to tell process "VisualCat" \
    to get name of every menu item of menu 1 of menu bar item 2 of menu bar 1'
About Avalonia, missing value, Services, missing value, Hide Avalonia Application,
Hide Others, Show All, missing value, Quit
```

Evidence: `u28-app-menu.png`.

Four separate defects live in that one output.

1. **The application menu is titled `Avalonia Application`.** That string is what macOS
   shows in the menu bar, in ⌘-Tab's tooltip, and in *Force Quit* — the Apple menu of
   this very session reads `Force Quit Avalonia Application`. The product's own name never
   appears anywhere in the system UI.
2. **`About Avalonia`** opens Avalonia's about box, not the product's. The version string
   the empty state shows (`VisualCat 2.0.13+0670981`) is unreachable from the menu bar.
3. **There is no File, Edit, View, Window or Help menu — `count menu bar items` is 2.**
   On macOS that removes, at minimum: ⌘W (close window), ⌘M (minimise), ⌘, (Settings),
   ⌘O (open), ⌘S (save), the Window menu's window list, the Help menu's searchable help,
   and — most importantly — the **Edit menu**, whose presence is what lets macOS route
   ⌘X/⌘C/⌘V/⌘A and the Services menu correctly and what assistive technology enumerates
   to find those commands. [`KEYBOARD.md`](KEYBOARD.md) documents Ctrl-based shortcuts and
   `F3` with no macOS mapping at all, so on this platform the documented shortcuts and the
   platform conventions disagree and neither is reachable from a menu.
4. **`Hide Others` is bound to ⌥⌘Q, not the macOS-standard ⌥⌘H.** ⌥⌘Q is one modifier away
   from ⌘Q — a user reaching for "hide everything else" can quit the app instead, losing
   an unsaved capture. This is an Avalonia stock-menu defect that ships unchanged.

**Expected.** [`SUPPORT.md`](SUPPORT.md) lists the macOS desktop as a supported surface
("Shared Avalonia/Skia application … bare executable rather than a `.app` bundle"). Being
a bare executable removes the *bundle*, and with it the Dock icon and Launch Services
registration; it does **not** remove the menu bar, which `NSApplication` synthesises for
any process that connects to the window server. A supported macOS surface must at least
name itself correctly and offer the standard application and edit menus.

**Suggested fix.** In order of value per line of code:

1. **Name the application.** Avalonia reads the macOS application name from the
   `CFBundleName` of the enclosing bundle and falls back to the framework default when
   there is none. For a bare executable, set it explicitly at startup before
   `NSApplication` finishes launching — in the macOS branch of the app builder:
   ```csharp
   // macOS shows this string in the menu bar, Cmd-Tab and Force Quit. Without it,
   // NSApplication falls back to Avalonia's default and calls the product
   // "Avalonia Application".
   builder.With(new MacOSPlatformOptions { /* … */ });
   // and, before the first window is shown:
   if (OperatingSystem.IsMacOS())
       NSApplicationNameShim.Set("VisualCat");   // sets the process's CFBundleName
   ```
   The minimal, dependency-free spelling is to write `CFBundleName` into the process's
   `Info.plist` dictionary at runtime via `NSBundle.mainBundle.infoDictionary`, which
   Avalonia's `AvaloniaNativePlatformExtensions` already reaches for.
2. **Install a real `NativeMenu`.** Avalonia supports `NativeMenu.SetMenu(Application, …)`;
   on Windows and Linux the same menu can be left unset so nothing changes there. The
   minimum macOS-correct menu bar is:
   - **VisualCat** — About VisualCat (showing `2.0.13+0670981`), Settings… ⌘,,
     Services, Hide VisualCat ⌘H, **Hide Others ⌥⌘H**, Show All, Quit VisualCat ⌘Q
   - **File** — Open log… ⌘O, Open log with options…, Open session…, Open archive…,
     Follow file…, Recent ▸, Save ⌘S, Save portable ⇧⌘S, Export…, Close ⌘W
   - **Edit** — Undo/Redo (or omit), Cut ⌘X, Copy ⌘C, Paste ⌘V, Select All ⌘A, Find… ⌘F,
     Find Next ⌘G, Find Previous ⇧⌘G
   - **View** — Zoom In ⌘+, Zoom Out ⌘−, Fit ⌘0, severity filter toggles
   - **Window** — Minimise ⌘M, Zoom, Bring All to Front
   - **Help** — VisualCat Help (opens the docs URL), Release Notes, Report a Bug
3. **Fix the `Hide Others` accelerator to ⌥⌘H** as part of step 2; once the app installs
   its own menu, the stock one with the ⌥⌘Q binding is gone.
4. **Map the documented shortcuts to ⌘ on macOS.** `KEYBOARD.md` should grow a macOS
   column, and the key bindings should use the platform's primary modifier
   (`KeyGesture` with `KeyModifiers.Meta` on macOS) rather than `Control`. A user who
   presses ⌘F on a Mac and gets nothing will conclude search does not exist. This is the
   single most valuable UX fix on the platform after the menu itself.
5. Add a macOS smoke assertion to the release workflow that the running process's
   `menu bar item 2` is named `VisualCat`; it is one `osascript` line and it makes the
   whole class unable to regress.

**Appendix-B trap checks.** Not an automation artifact — the same strings are visible in
`u28-app-menu.png`, a real screen capture. Not a focus artifact — the process was made
frontmost first and `frontmost is true` was confirmed. Not specific to the SSH launch
path: the menu bar is owned by `NSApplication` in the app's own process, independent of
what started it; confirmed again after activating the app through the Dock.

---

### F-04 · Minor · The macOS data root is created world-readable (0755)

**Severity** Minor — provisionally filed; the full P-22 assertion needs session content,
which this pass has not created yet.

**Where** Data-root creation on first launch.

**What happens.** With `umask 022`, first launch creates

```
drwxr-xr-x  /Users/benny/Library/Application Support/VisualCat
drwxr-xr-x  /Users/benny/Library/Application Support/VisualCat/Diagnostics
```

Both are mode **0755**: any other local user on the Mac can list and read them.
[`PRIVACY.md`](PRIVACY.md) promises `700` for a session directory and `600` for a portable
`raw.log`; it makes no promise about the root itself, which is why this is Minor rather
than a contract violation — but the root is where diagnostics land, and a diagnostic
bundle is exactly the artifact a user would not expect to be readable by another account.

macOS's own `~/Library` is `drwx------`, so on a single-user Mac the parent hides the
child. On a multi-user Mac, `~/Library` stays `0700` but the home directory's ACL and the
`/Users/<name>/Public` convention mean administrators routinely traverse it, and `0755`
inside is a needless widening.

**Expected.** The product creates its own directories at `0700` regardless of `umask`, as
it already promises to do for session directories.

**Suggested fix.** Create the root and every product-owned directory with an explicit
mode rather than inheriting `umask`:

```csharp
// The user's umask can be anything; product data is never world-readable.
Directory.CreateDirectory(root);
if (!OperatingSystem.IsWindows())
    File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
```

and assert the mode in the existing privacy test so the promise is enforced rather than
described. Re-verify against `Sessions/`, `SessionAccess-v1/` and `Diagnostics/` once
[§2](#2-results) creates them.

---

### F-05 · Minor · `SUPPORT.md` states no macOS version floor, while the binaries declare one

**Severity** Minor — a support-matrix gap. A user on macOS 11 downloads an artifact that
cannot load, and nothing published tells them so in advance.

**Where** [`SUPPORT.md`](SUPPORT.md), the platform table.

**What happens.** `SUPPORT.md`'s macOS row reads, in full:

> | macOS desktop | Shared Avalonia/Skia application; CI build/test; bare executable rather than a `.app` bundle |

No version, no architecture list, no Rosetta statement. The shipped binaries are less
vague:

```shell
$ otool -l candidates/desktop-osx-arm64/VisualCat | grep -A4 LC_BUILD_VERSION
      cmd LC_BUILD_VERSION
 platform 1
    minos 12.0
      sdk 15.5
```

Every .NET runtime library and both apphosts declare `minos 12.0`; Skia, HarfBuzz and
Avalonia's native shim declare `11.0`. The effective floor is therefore **macOS 12.0
(Monterey)** and the release was built against the **macOS 15.5 SDK** on a `macos-15`
runner.

By contrast, the Linux row does say `x64 tarball only` and the Android row gives an exact
API range (`Android 12+ (API 31) to API 36, arm64-v8a and x86_64`). macOS is the only
supported desktop with no floor published, and it is the platform where the floor is
enforced by the dynamic loader rather than by a friendly message.

**Expected.** The support matrix names the minimum macOS version and the published
architectures, as it does for Android.

**Suggested fix.**

1. Change the `SUPPORT.md` macOS row to state the facts the artifacts already encode:
   ```
   | macOS desktop | Shared Avalonia/Skia application; CI build/test; **macOS 12 (Monterey)
   or later**; `osx-arm64` and `osx-x64` tarballs; `osx-x64` runs on Apple silicon under
   Rosetta 2; bare executable rather than a `.app` bundle, so there is no Dock icon, no
   Launch Services registration and no file association |
   ```
2. Derive that number rather than hard-coding it: add a release-workflow step that reads
   `minos` out of the published Mach-O and fails if it is newer than the documented floor.
   The floor moves whenever the .NET target framework moves, and a hand-written number
   will drift.
3. State the Rosetta expectation in [`RELEASE-NOTES.md`](RELEASE-NOTES.md) too — this run
   is the first evidence anywhere that the `osx-x64` artifact executes at all
   ([§2.6](#26-b-02-second-half--m5--x-30--the-osx-x64-build-under-rosetta-2)), and the
   `.github/release-targets.json` entry still says `executable: false`.

**Appendix-B trap checks.** Not a toolchain artifact — `minos` was read from the shipped
binary, not from a local build. Not an architecture artifact — both `osx-arm64` and
`osx-x64` apphosts report `minos 12.0`.

---

### F-06 · Major · `logcat -v long` silently loses two thirds of its records on macOS too — the release still ships the bug

**Severity** Major — a documented, user-selectable format produces **silently wrong
results**: records vanish, every counter that could report the loss says zero, and the
file then fails automatic detection outright.

**Where** Parser. `src/VisualCat.Core/Parsing/LogcatParser.cs`, `TryLong`.

**Status** This is the same defect as `LINUX-LIVE-TEST-REPORT.md` [F-01], reproduced
independently on macOS against the shipped `osx-arm64` 2.0.13 candidate. **The fix is
already on `main`** — `LogcatParser.cs:433` now carries the comment *"Android prints the
identity field as `%5d:%5d`, so a five-digit thread id fills the…"* and splits on
`idsToken.LastIndexOf(':')` at line 439 — but **2.0.13 is the current release and still
has it**. It is recorded here because a macOS report must stand alone, and because it
changes the release recommendation.

**Measured on this Mac**, `vcat generate-test-log --lines 20000 --seed 42 --format long`:

| Measure | Value |
|---|---|
| Header lines in the file (`grep -c '^\['`) | 19 998 |
| Headers with a 5-digit tid and no space after the colon | **13 348** |
| `parsedEntries` | **6 650** (= 19 998 − 13 348) |
| `unknownLines` | **0** |
| `rejectedCandidates` | **0** |
| `continuations` | 33 348 |
| Auto-detect confidence on the raw file | **0.1596** — below the 0.6 threshold |

**66.7 % of the records are gone** from the timeline, the counts, the facets, the
templates and every export, and the two counters a reader would check to notice both read
zero. The lost content is not even reachable through *Lines not on the timeline…*, because
it is filed as a *continuation*, which asserts it belongs to the entry above it. It does
not.

The user-facing half is the same as on Linux: because so many headers fail, the detector's
score collapses, so an ordinary unmodified `logcat -v long` dump is refused outright.

**Contrast with the same build's honest accounting.** On `outcomes.txt`
([§2.8](#28-corpus-and-oracle-plan-31)) the same parser accounts for all 9 source lines
exactly once, including 1 rejected candidate and 1 unknown line. The machinery to report
a loss exists and works; the long-format path simply never uses it.

**Suggested fix.** The parse fix is already on `main`. What is still missing, and what
this run recommends:

1. **Ship it.** Cut 2.0.14 (or backport to a 2.0.13.1) rather than leaving a
   silent-data-loss defect as the current release, and say so in
   [`RELEASE-NOTES.md`](RELEASE-NOTES.md) under a *Fixed* heading that names the symptom a
   user would recognise: *"`logcat -v long` files imported with most records missing and
   no warning."*
2. **Make a failed header a rejected candidate, not a continuation.** A line that begins
   `[`, ends `]`, and carries a date and a `PRIORITY/Tag` is self-evidently an attempted
   header; silently reclassifying it as message text of the previous record is what turned
   a parse bug into a *silent data-loss* bug. Rejected candidates are counted, surfaced in
   the chip bar, and reachable from *Lines not on the timeline…*; continuations are not.
   This guard is worth having independently of the parse fix, because it converts the next
   parser bug of this shape into a visible one.
3. **Add the I-14 assertion that would have caught it.** Require every generated format to
   detect at or above the auto-detect threshold, with `long` expected at ≥ 0.9 and `brief`
   exempted at ≥ 0.6 (brief carries no timestamp, so 0.667 is its structural ceiling —
   see [§2.7](#27-i-01--i-14--cli-identity-and-the-five-format-generator-matrix)).
4. Add the four-line width fixture from the Linux report to `test-data/golden-formats.txt`
   with its expected counts.

**Appendix-B trap checks.** Not a VM or platform artifact — this is byte-identical
behaviour to the Linux run on a different OS, CPU architecture and filesystem. Not a
locale artifact — the SSH shell ran with `LC_ALL` unset and `LC_CTYPE=C`. Not a
line-ending artifact — `tr -dc '\r' < fmt-long.txt | wc -c` is 0. Not a generator artifact
— the Linux run confirmed the same shape on unmodified real-device `adb logcat -d -v long`
output.

---

### F-07 · Minor · On macOS the session records a time zone the user never set (`Europe/Bratislava` for a Mac set to `Europe/Prague`)

**Severity** Minor — instants are correct, because the two zones are byte-identical rules.
But the session manifest, and anything that displays or compares it, states a country the
user did not choose, and the same log indexed on Windows, Linux and macOS produces
**different session metadata**, which breaks the cross-platform parity assertions in I-11
and I-15.

**Where** Time-zone resolution for `timestampPolicy.timeZoneId` in the session descriptor.

**What happens.**

```shell
$ readlink /etc/localtime
/var/db/timezone/zoneinfo/Europe/Prague

$ vcat index gen-time.txt --output gen-time.vcat && vcat info gen-time.vcat | grep timeZoneId
      "timeZoneId": "Europe/Bratislava",

$ TZ=Europe/Prague vcat index gen-time.txt --output /tmp/tz1.vcat && vcat info /tmp/tz1.vcat | grep timeZoneId
      "timeZoneId": "Europe/Prague",

$ TZ=UTC vcat index gen-time.txt --output /tmp/tz2.vcat && vcat info /tmp/tz2.vcat | grep timeZoneId
      "timeZoneId": "UTC",
```

So the value is right when `TZ` is set and wrong when it is inherited from the system.

**Why.** macOS does not store zoneinfo as symlinked aliases the way Linux does — every
zone is its own regular file:

```shell
$ ls -l /var/db/timezone/zoneinfo/Europe/Bratislava /var/db/timezone/zoneinfo/Europe/Prague
-rw-r--r--  1 root  wheel  2301 Jul 15 23:50 …/Europe/Bratislava
-rw-r--r--  1 root  wheel  2301 Jul 15 23:50 …/Europe/Prague
```

Two byte-identical 2301-byte files. .NET's `TimeZoneInfo.Local` on Unix reads
`/etc/localtime`'s **contents** and finds the matching id by scanning the zoneinfo tree; it
can only take the id from the symlink path when the link target lies under the default
zoneinfo directory it was compiled with. On macOS `/etc/localtime` points into
`/var/db/timezone/zoneinfo` while the default directory string is `/usr/share/zoneinfo`
(itself a symlink to the former), the prefix comparison misses, and the content scan
returns the first alphabetical match — `Bratislava` before `Prague`.

The same class of aliasing hits many users, not an exotic few: `Europe/Oslo` resolves
before `Europe/Stockholm`/`Europe/Copenhagen`, `America/Toronto` before `America/Nassau`,
`Asia/Kuala_Lumpur` before `Asia/Singapore`, and so on. The user sees a neighbouring
country's zone in their own session file.

**Expected.** A session records the zone the host is actually configured for, and the same
log produces the same `timeZoneId` on every platform.

**Suggested fix.**

1. Resolve the id from the symlink before falling back to `TimeZoneInfo.Local.Id`, and do
   it once at startup:
   ```csharp
   // macOS keeps every zone as its own file, so .NET's content-match can return an alias
   // (Europe/Bratislava for a Mac set to Europe/Prague). The /etc/localtime symlink
   // target carries the id the user actually chose; prefer it when it resolves.
   static string ResolveHostTimeZoneId()
   {
       if (OperatingSystem.IsMacOS())
       {
           var target = File.ResolveLinkTarget("/etc/localtime", returnFinalTarget: true)?.FullName;
           if (target is not null)
           {
               var i = target.IndexOf("/zoneinfo/", StringComparison.Ordinal);
               if (i >= 0)
               {
                   var id = target[(i + "/zoneinfo/".Length)..];
                   if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out _)) return id;
               }
           }
       }
       return TimeZoneInfo.Local.Id;
   }
   ```
   `TryFindSystemTimeZoneById` keeps the fallback honest: a path that does not name a real
   zone never reaches the manifest.
2. **Record the UTC offset beside the id** in `timestampPolicy`, and show the offset — not
   just the id — wherever the import review and the session details display a zone. An id
   is an opinion about naming; the offset is the thing that actually moved the timestamps,
   and it makes a residual alias mismatch harmless to a reader.
3. For I-15 byte parity, treat `timeZoneId` as a host-dependent field the comparison
   normalises on (alongside paths, session GUIDs and creation instants), and say so in the
   plan — otherwise the parity row fails for a reason that is not a product difference.

**Appendix-B trap checks.** Not a `TZ`-environment artifact — the SSH shell had no `TZ`
set, and setting it explicitly produces the correct id, which is the control. Not a
tzdata-version artifact — both files come from the same `Jul 15 23:50` tzdata drop. Not a
corpus artifact — reproduced on two different generated corpora.

---

### F-08 · Major · Every saved session on macOS is world-readable, contradicting `PRIVACY.md` in the exact words it uses to promise otherwise

**Severity** Major — a published privacy guarantee is false on the shipped release, and the
data it fails to protect is other people's log content, which is the premise of the
guarantee.

**Where** Session, portable-session, lease and settings creation. The fix exists on `main`
(`src/VisualCat.Core/Store/SessionFileModes.cs`, `OwnerOnlyDirectory` = `700`,
`OwnerOnlyFile` = `600`) and in `src/VisualCat.Domain/ProductDataRoot.cs`. **Neither is in
2.0.13.**

**Status** Same root cause as `LINUX-LIVE-TEST-REPORT.md` [F-27], which that run closed on
`main`. Reproduced here against the shipped `osx-arm64` 2.0.13 candidate, on the artifact
`PRIVACY.md` names by name.

**What `PRIVACY.md` promises** (lines 121–127, quoted in full because the wording is the
finding):

> **Session file modes.** A session is, by construction, log content that is often not the
> operator's own, so VisualCat **does not leave it to the account's `umask`**: session
> directories are created `700` and a portable session's embedded `raw.log` is `600`,
> **wherever the session is written**. On a shared machine that is what stops another local
> account reading a capture saved to `/tmp` or to a group-writable project directory.

**What actually happens**, with this Mac's `umask 022` — the macOS default, unchanged:

```shell
$ stat -f '%Sp %z %N' "<saved>.vcat" "<saved>.vcat/raw.log"
drwxr-xr-x   288      <saved>.vcat
-rw-r--r--   6716288  <saved>.vcat/raw.log

$ stat -f '%Sp %N' "<portable>.vcat/raw.log"
-rw-r--r--   <portable>.vcat/raw.log

$ stat -f '%Sp %N' "$HOME/Library/Application Support/VisualCat"/*
drwxr-xr-x  …/Diagnostics
drwxr-xr-x  …/SessionAccess-v1
drwxr-xr-x  …/Sessions
-rw-r--r--  …/settings.json
```

Every directory `0755`, every file `0644` — including the 6.7 MB `raw.log` holding this
phone's captured traffic (process names, package names, a device fingerprint, redacted and
unredacted system messages), every lease file, the diagnostics `.jsonl`, and `settings.json`.
The value is exactly `umask`-derived, which is the one thing the document says it is not.

**Why it is not hidden by `~/Library`.** On a single-user Mac the parent directory happens
to be `drwx------`, so nothing leaks today. That is the desktop's protection, not the
product's, and `PRIVACY.md`'s own scenario is the one where it does not apply: a session
saved to `/tmp`, to `/Users/Shared`, or to a project directory — which is where a user
saves a session they intend to send to a colleague. This run's own saves went to
`~/vcat-run/evidence/…`, a `0755` path, and are readable by every account on the Mac.

**Suggested fix.** The code is written; the gap is that it has never shipped.

1. **Release it.** `SessionFileModes` and `ProductDataRoot`'s `700` creation are on `main`
   and covered by the Linux run's verification. Cut 2.0.14 — together with
   [F-06](#f-06), which is the other already-fixed, never-released defect, and which is the
   stronger of the two reasons.
2. **Make the promise testable on macOS, not only Linux.** The Linux run proved the fix with
   a second local account. Add a unit assertion that runs on every platform where
   `File.SetUnixFileMode` is available:
   ```csharp
   // PRIVACY.md promises 700/600 regardless of umask. Prove it under the permissive one.
   [Theory] [InlineData(0)] [InlineData(0b000_010_010)]   // umask 000 and 022
   public void SessionModesIgnoreUmask(int umask) { … assert 0700 dir, 0600 raw.log … }
   ```
   `umask 022` is the *default* on macOS while many Linux distributions use `002` or `077`,
   so a test that only ever runs under one umask can pass while the promise is broken.
3. **Cover the whole set, not just the session.** `settings.json` (`0644`) can contain custom
   session-directory paths and ADB paths; `Diagnostics/*.jsonl` is precisely the artifact a
   user would not expect to be world-readable; the `SessionAccess-v1` lease files are
   world-**readable** cross-process state. Apply `OwnerOnlyFile` to all three.
4. **Say what happens on a filesystem that has no POSIX modes.** A session written to exFAT,
   FAT, or an SMB share cannot carry `700`. `PRIVACY.md` currently promises it "wherever the
   session is written"; it should instead promise `700`/`600` on a mode-capable volume and
   say plainly that a volume without modes cannot be protected this way. Better still, warn
   in the UI at save time — that is a two-line check (`File.GetUnixFileMode` after write,
   compare, notice if it did not stick) and it turns a silent broken promise into an
   informed choice.

**Appendix-B trap checks.** Not a `umask` artifact of the test harness — `022` is this
account's untouched default and is what a stock macOS gives every user. Not an extraction
artifact — these files were created by the running product, not unpacked. Not specific to
the evidence directory — the same `0755`/`0644` appears under
`~/Library/Application Support/VisualCat`, which the product creates itself. Not a
Finder-metadata artifact — `xattr -lr` on the saved sessions is empty.

---

### F-09 · Major · A click on the modally-blocked main window is queued and replayed after the dialog closes

**Severity** Major — it breaks the one invariant a modal dialog exists to provide, and it
fires the deferred action against a *different* application state from the one the user was
looking at when they clicked.

**Where** Avalonia modal-window handling on macOS (`ShowDialog` on the parent window).
Observed on the shipped 2.0.13 `osx-arm64` desktop.

**What happens.** Reproduced live during [§2.10](#210-b-11--i-05--host-adb-discovery-and-capture-against-a-physical-phone):

1. *ADB live* opened the modal **Live ADB capture** dialog (a separate top-level window at
   `420, 121`, size `600 × 438`).
2. With the dialog open, the parent window's **Open log** button was clicked once.
   **Nothing happened** — no file chooser, no notice, and `get name of every window` returned
   only the two existing windows for the next 5 s. The parent is correctly blocked at the
   input level.
3. *Start capture* was pressed. The dialog closed and the ADB capture started normally.
4. **The file chooser then opened by itself**, on top of the running capture — the native
   sheet titled `Open Android logcat file` — and the status bar showed a progress notice
   reading `Opening log…` with a *Cancel* button.

So the click was neither delivered nor discarded: it was **held and replayed** once the modal
window went away. Evidence: `b11-capturing-early.png` shows the chooser sheet and
`Opening log…` over a live capture reporting `Capturing · 858 lines received · 190/s`.

**Why it matters.** The user's mental model when they clicked was "empty app, no session".
The action ran against "a live ADB capture in progress". Three concrete consequences:

- A user who clicks the blocked window a few times — which is exactly what people do when a
  window does not respond — gets a **burst of unexpected actions** after the dialog closes.
- The deferred action can be destructive in its own right. *Close session*, *Recent
  captures* deletion and *Export* are all on the same blocked surface.
- On macOS the convention is unambiguous and users rely on it: clicking a window blocked by a
  modal makes the modal **bounce**, and the click is dropped. Nothing is remembered.

**Expected.** A click on a window blocked by an application-modal dialog is discarded, and
the dialog signals that it is the thing wanting attention.

**Suggested fix.**

1. **Discard, do not queue.** Whatever the current path is (Avalonia disables the parent's
   input but the platform still enqueues the event, and it is drained on re-enable), the
   parent's input queue must be **flushed** when the modal closes. In the macOS backend, the
   modal session should be run with the parent's `NSWindow` genuinely disabled for mouse
   events rather than merely ignoring them, so AppKit never records them.
2. **Give the click somewhere to go.** macOS's own affordance costs nothing and tells the
   user precisely what is wrong: bounce the modal window when a blocked window is clicked.
   Avalonia does not do this automatically for a non-sheet dialog, so add it in the dialog
   host — on receiving a blocked-parent click, activate the dialog and run a short
   shake/bounce.
3. **Present these dialogs as sheets.** The native file chooser in this same app *is* a sheet
   (`sheet 1 of window 1`, confirmed live), and it behaves perfectly. The product's own
   dialogs are free-floating `AXStandardWindow`s with their own traffic lights, which is why
   this class of bug is reachable at all. A sheet is attached to its parent, cannot be
   separated from it, cannot be minimised away from it, and gets the bounce behaviour for
   free. This also fixes the two smaller problems found beside this one: the dialog reports
   **`AXModal = false`** to assistive technology (so a screen reader is not told it is
   modal), and it carries an **enabled minimise button**, which lets a user minimise a modal
   dialog and leave the application with a blocked main window and no visible way back.
4. Add a regression test at the harness level: open a modal, synthesise a click on the
   parent, close the modal, assert no command executed.

**Appendix-B trap checks.** Not a synthetic-input artifact: `System Events`' `click at`
posts a real `CGEvent` to the window under the cursor, which is what a physical click is;
the same click *did* execute, just later, so it was plainly delivered to the application.
Not a slow-chooser artifact — the window list was polled for 5 s before *Start capture* and
showed no chooser, and the chooser appeared only after the modal closed. Not a
screen-lock artifact — the sequence completed before the display slept, and the chooser was
still on screen after the display was woken.

---

### F-10 · Major · `--adb` pointing at a missing path or a directory is silently ignored, and a different `adb` is used instead

**Severity** Major for scripted use — the flag whose entire purpose is to pin a specific
tool is advisory, and when it is wrong the run **succeeds** with a different tool and exit
code 0. On a machine with several `adb` builds (Android Studio's, Homebrew's, a vendored
one — the normal state of a Mac Android developer's machine) the user is never told which
one ran.

**Where** `src/VisualCat.Infrastructure/Adb/AdbLocator.cs`, `Find`.

**What happens.**

```csharp
public static string? Find(string? explicitPath = null)
{
    if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
    {
        return Path.GetFullPath(explicitPath);
    }
    // …falls through to ANDROID_SDK_ROOT, ANDROID_HOME, the default SDK dir, then PATH
```

When `explicitPath` is supplied but `File.Exists` is false — a typo, a moved SDK, or a
**directory**, for which `File.Exists` is false by definition — the `if` is skipped and the
method continues to the ambient probes. Measured live:

| Command | Exit | Device listed? |
|---|---|---|
| `vcat adb-devices --adb /tmp` (a directory) | **0** | **yes** — via `PATH` |
| `vcat adb-devices --adb /tmp/definitely-not-here` | **0** | **yes** — via `PATH` |
| `vcat capture-adb --serial RFCRC0A9GND --adb /tmp/definitely-not-here --duration-seconds 2` | **0** | **captured a full 2 s session** |
| the same two with `PATH` stripped | 2 | `error: ADB was not found. Set --adb, ANDROID_SDK_ROOT, or PATH.` |

That last row is the sharpest part: with no fallback available the product reports that ADB
was not found and lists the places to set it — **without ever saying that the `--adb` value
the user passed on the command line was rejected**. The user reads "set `--adb`" while
looking at the `--adb` they just set.

For contrast, the paths that *do* fail, fail well: a non-executable file gives
`Permission denied` and a dangling symlink gives `No such file or directory`, both exit 1 —
because those reach `Process.Start` rather than being dropped by `File.Exists`.

**Expected.** Plan §2.11 and A-15: an explicitly configured path is authoritative. It either
works or it fails with its own specific reason; it never silently becomes a different
binary.

**Suggested fix.**

```csharp
public static string? Find(string? explicitPath = null)
{
    if (!string.IsNullOrWhiteSpace(explicitPath))
    {
        // An explicit path is a decision, not a hint: if it is wrong, say so. Falling back
        // to PATH here runs a different adb than the caller pinned, and reports success.
        if (Directory.Exists(explicitPath))
            throw new AdbLocatorException($"The configured ADB path '{explicitPath}' is a directory, not the adb executable.");
        if (!File.Exists(explicitPath))
            throw new AdbLocatorException($"The configured ADB path '{explicitPath}' does not exist.");
        if (!OperatingSystem.IsWindows() &&
            !File.GetUnixFileMode(explicitPath).HasFlag(UnixFileMode.UserExecute))
            throw new AdbLocatorException($"The configured ADB path '{explicitPath}' is not executable. Run 'chmod +x {explicitPath}'.");
        return Path.GetFullPath(explicitPath);
    }
    …
}
```

Three further points that make the fix complete:

1. Apply the same rule to the **desktop's configured ADB path** setting. `MainView.cs` already
   has the better message for that case — *"Correct it in Appearance & timeline, install
   Android platform-tools, or set `ANDROID_SDK_ROOT`"* — so the two surfaces should share it.
2. **Say which `adb` was chosen.** Record the resolved absolute path and version in the
   session manifest beside the `adbVersion` that is already there, and show it in the ADB
   dialog's status line. A user with three `adb` installations currently has no way to know
   which one produced a capture.
3. Check the executable bit up front rather than at `Process.Start`. It turns a .NET
   exception message that quotes a working directory into one sentence naming the remedy.

**Appendix-B trap checks.** Not a quoting artifact — the same values were passed through a
shell function that quotes every argument, and the failing and succeeding cases differ only
in the path. Not a `PATH` artifact — the control run with `PATH` stripped proves the
fallback is what produced the success. Reproduced on both `adb-devices` and `capture-adb`.

---

### F-11 · Minor · The one SDK location an Android developer's Mac actually has is the one the product never looks in

**Severity** Minor — first-run friction on precisely the machine most likely to run this
product.

**Where** `src/VisualCat.Infrastructure/Adb/AdbLocator.cs`, `AndroidSdkRoots`.

**What happens.** After the two environment variables, the only default probed is

```csharp
var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var defaultPath = Path.Combine(local, "Android", "Sdk");
```

On macOS `LocalApplicationData` resolves to `~/Library/Application Support`, so the product
probes **`~/Library/Application Support/Android/Sdk/platform-tools/adb`**. Verified live:
place an `adb` there and it is found.

That path is the **Windows** Android Studio convention (`%LOCALAPPDATA%\Android\Sdk`)
transplanted to macOS. Android Studio on macOS installs the SDK at
**`~/Library/Android/sdk`**, and Homebrew installs `adb` at `/opt/homebrew/bin/adb` on Apple
silicon or `/usr/local/bin/adb` on Intel. Verified live: with `PATH` stripped and a real
`adb` present at `~/Library/Android/sdk/platform-tools/adb`, the product reports
`error: ADB was not found.`

Homebrew's location is reached today only because Homebrew puts it on `PATH` — and plan §2.7
records why that is thin ice on macOS: a process launched from Finder or from a wrapper
inherits the **login session's** environment, not a shell's, so a `PATH` entry that
`.zprofile` adds is invisible to it. A Mac user who installs platform-tools with Homebrew and
launches VisualCat by double-clicking will find no devices, while the same build run from
Terminal finds them immediately.

**Also observed, and worth one line:** the probe's `Sdk` differs from Android Studio's `sdk`
**only in case**. On this case-insensitive APFS volume both spellings resolve; on a
case-sensitive APFS volume only `Sdk` would. A path that works on one Mac and not another
for that reason is the hardest kind of bug to report.

**Suggested fix.**

1. Make the default roots platform-specific and include the real ones:
   ```csharp
   private static IEnumerable<string> AndroidSdkRoots()
   {
       foreach (var name in new[] { "ANDROID_SDK_ROOT", "ANDROID_HOME" }) { … }

       var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
       if (OperatingSystem.IsMacOS())
       {
           // Android Studio's macOS location, and where `brew install --cask
           // android-platform-tools` links adb. LocalApplicationData/Android/Sdk is the
           // Windows convention and exists on no ordinary Mac.
           yield return Path.Combine(home, "Library", "Android", "sdk");
           yield return "/opt/homebrew/share/android-platform-tools";   // Apple silicon
           yield return "/usr/local/share/android-platform-tools";      // Intel
       }
       else if (OperatingSystem.IsLinux())
       {
           yield return Path.Combine(home, "Android", "Sdk");
           yield return Path.Combine(home, ".local", "share", "Android", "Sdk");
       }
       var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
       …keep the existing probe last, for compatibility…
   }
   ```
   and additionally probe the bare `/opt/homebrew/bin/adb` and `/usr/local/bin/adb`, since
   Homebrew links the executable rather than shipping a `platform-tools` tree the product
   would recognise.
2. Compare the SDK directory name **case-insensitively on macOS and Windows, exactly** on
   Linux, or simply probe both spellings. One extra `yield return` removes a whole class of
   "works on my Mac".
3. Update [`CLI.md`](CLI.md), which documents the locator as "`--adb`, `ANDROID_SDK_ROOT`, or
   `PATH`" — it omits `ANDROID_HOME` and the default SDK directory, both of which work
   today. See [F-12](#f-12) for the message that repeats the same omission.

**Appendix-B trap checks.** Each route was proved in isolation with `PATH` stripped to
`/usr/bin:/bin:/usr/sbin:/sbin`, so no probe could be satisfied by a fallback. The `adb`
placed at each location was a symlink to the same real binary, so a positive and a negative
result differ only in the path searched.

---

### F-12 · Polish · Three small ADB messages that each send the reader one step in the wrong direction

**Severity** Polish — no data is lost and nothing is wrong on screen; each one just costs a
user minutes.

**Where** `src/VisualCat.Cli/Program.cs:541,554`; `docs/CLI.md`; the desktop search box.

**1. The "ADB was not found" message under-reports what works and offers macOS nothing.**

```
error: ADB was not found. Set --adb, ANDROID_SDK_ROOT, or PATH.
```

`ANDROID_HOME` works and is not mentioned. The default SDK directory works and is not
mentioned. And on the platform where the message is most likely to appear — a Mac with no
Android tooling — it names no way to *get* `adb`. Suggested:

```
error: ADB was not found. Pass --adb <path>, set ANDROID_SDK_ROOT or ANDROID_HOME to an SDK
       directory, or put adb on PATH.
       macOS: brew install --cask android-platform-tools
       Android Studio installs it at ~/Library/Android/sdk/platform-tools (macOS).
```

Build the list from the locator's own probe order so the message cannot drift from the code
again — the same list, rendered, is also the right content for the desktop's equivalent
notice.

**2. `vcat search` matches the message but not the tag, and says nothing about it.**
Searching a capture for `VCATTEST` returns **3** matches — the `adbd` lines that happen to
quote the tag in their message text — while the 301 records actually *tagged* `VCATTEST` are
not matched. `search "steady" --tags VCATTEST` finds all 302. The behaviour is defensible;
the silence is not. Suggested: when a text search returns few or no matches and the query
exactly equals a known tag or process name in the session, add one line —
`No message matched "VCATTEST". 301 entries carry that tag — search with --tags VCATTEST.`
In the desktop, offer it as a clickable chip in the empty-result state. This is the highest
value-per-line item in this finding.

**3. On macOS, nothing names the macOS-specific device remedy.** The state messages are
correct and platform-neutral ([§2.12](#212-a-16--adb-device-state-matrix)), and `main`
correctly gates its `udev` advice behind `OperatingSystem.IsLinux()`. But macOS has its own
equivalent of "the device is attached and ADB still cannot see it": on Apple-silicon
laptops the **first** USB connection of a new device raises an *Allow accessory to connect*
prompt, and until it is answered the device is absent from `adb devices` — indistinguishable
from a dead cable. Suggested, in the same `OperatingSystem` switch that already carries the
Linux branch:

```csharp
else if (OperatingSystem.IsMacOS())
{
    message += " On macOS, check that the device was allowed to connect — an Apple silicon " +
               "Mac asks once per new device, and until that is answered ADB does not list it " +
               "at all. System Settings › Privacy & Security › Allow accessories to connect.";
}
```

Also worth adding on macOS: `adb kill-server` after a `brew upgrade` of platform-tools, for
the same reason the Linux branch mentions it — a running server keeps the credentials and
the binary it started with.

**Appendix-B trap checks.** All three were read off the shipped 2.0.13 binaries, not from
the repository: the "ADB was not found" text was produced live with `PATH` stripped, the
search counts come from `vcat search` on a real 49 012-entry capture, and the absence of
macOS remedy text was confirmed by searching the shipped assemblies as well as by running
the states.

---

### F-13 · Polish · A `.vcat` session is a folder on macOS, so the save panel lets you save a session inside another session

**Severity** Polish — nothing was corrupted, and the trigger needs a user to navigate into a
session directory. It is filed because on macOS the platform has a purpose-built answer and
the product is not using it.

**Where** Session-on-disk format, plus the absence of any macOS document-type declaration.

**What happens.** A `.vcat` session is a directory (`manifest.json`, `raw.log`,
`segments-final-*/`, `source-order/`, `templates-final.jsonl`, `view.json`, `diagnostics/`).
macOS has a first-class concept for "a directory the user should treat as one document" — a
**package** — and Finder, the open panel and the save panel all honour it. VisualCat declares
none, because it ships as a bare executable with no `.app` bundle and therefore no
`UTExportedTypeDeclarations`. Consequences, all observed:

- Finder shows a session as an ordinary folder of eight-ish opaque items. A user copying a
  session to a colleague must know to take the whole folder.
- The **save panel navigates into sessions**. During this run a *Save portable* landed at
  `…/cli-capture.vcat/ADB RFCRC0A9GND 13h00m23-portable-20260914-131517.vcat` — a session
  nested inside another session. Both still verified afterwards (`vcat verify` reported
  `"isValid": true` for each, and the outer session's entry count was unchanged at 8 148), so
  the store tolerates it; but the outer session now silently carries 7 MB of unrelated data
  that its own manifest does not describe, and a later cache-retention sweep of the outer
  session would take the inner one with it.
- The open panel has the same property in reverse: *Open session* must be pointed at a
  directory, which is unusual enough on macOS that it is worth a word in the README.

**Suggested fix**, cheapest first:

1. **Refuse the nesting.** At save time, walk up from the chosen destination and refuse if any
   ancestor contains a `manifest.json` that parses as a session:
   `"That location is inside the session '<name>'. Choose a directory outside it."`
   This is a few lines, needs no platform work, and is also the right guard on Windows and
   Linux.
2. **Declare the package type.** If a `.app` bundle is ever shipped (it is the natural fix for
   [F-03](#f-03) too, since a bundle is also what gives the app a name, an icon and a Dock
   identity), add to `Info.plist`:
   ```xml
   <key>UTExportedTypeDeclarations</key>
   <array><dict>
     <key>UTTypeIdentifier</key><string>com.barebit.visualcat.session</string>
     <key>UTTypeConformsTo</key><array><string>com.apple.package</string></array>
     <key>UTTypeTagSpecification</key>
     <dict><key>public.filename-extension</key><array><string>vcat</string></array></dict>
   </dict></array>
   ```
   Finder then shows a session as one document, the save panel stops descending into it, and
   double-clicking one opens VisualCat.
3. Until then, say so in the macOS `README.txt`: *"A saved session is a directory, not a
   single file. Copy or zip the whole `.vcat` directory. Use `Save portable` and then
   `Export → portable-zip` for a single-file hand-off."*

**Appendix-B trap checks.** Not a harness artifact in the part that matters: whatever put the
save panel inside `cli-capture.vcat`, the product accepted the destination and wrote a
complete session there without a word. The package-declaration half is a static fact about
the shipped artifact, confirmed by the absence of any `Info.plist` in the tarball.

---

## 4. Standing list — what is still untested

*(populated at the end of each pass)*
