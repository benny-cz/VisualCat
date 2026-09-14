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
| Last completed | §2.9 — artifact/signature/Rosetta, self-containment, CLI identity, format matrix, corpus + oracle |
| Next step | B-05 file import through the macOS chooser; ADB rows once the phone authorizes |
| Findings | 7 open (F-01 … F-07): 2 Major, 5 Minor |

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

---

## 3. Findings

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

## 4. Standing list — what is still untested

*(populated at the end of each pass)*
