# VisualCat — macOS fix pass (pass 2 of the macOS live run)

Implementation of every fix and improvement [`MACOS-LIVE-TEST-REPORT.md`](MACOS-LIVE-TEST-REPORT.md)
recommends, built from this tree, **live-tested on the same Mac and the same physical Android
phone** the first pass used, and documented here as it happens.

This document is **context-agnostic and continuously written**: [§0](#0-restore-point--resume-here)
is rewritten after every unit of work, so an interrupted run resumes from the last line there
without needing a fact that exists only in a previous session.

---

## 0. Restore point — resume here

| Field | Value |
|---|---|
| Run ID | `20260914-macos-fix-pass` |
| Status | **IN PROGRESS** |
| Tree | branch `main`, working from `1338921` (the commit that closed pass 1) |
| Candidate under test | a **build of this tree**, not a release tarball — `2.0.13-dev+<commit>` |
| Last completed | §1 — harness restored, `main` cross-built for `osx-arm64` and deployed to the Mac |
| Next step | §2 — re-verify every finding against `main` to separate "already fixed" from "still open" |

### How the candidate is built and deployed

`dotnet` is **not installed on the Mac**; it does not need to be. .NET cross-publishes a
self-contained `osx-arm64` build from the Windows host, and the result runs natively on the M1
(proved: `vcat --version` → `2.0.13-dev+1338921eabbe…`).

```shell
# on Windows, from the repository root
dotnet publish src/VisualCat.Desktop/VisualCat.Desktop.csproj -c Release -r osx-arm64 \
    --self-contained true -o artifacts/mac-build/desktop
dotnet publish src/VisualCat.Cli/VisualCat.Cli.csproj -c Release -r osx-arm64 \
    --self-contained true -o artifacts/mac-build/cli
# then tar | scp | untar into ~/vcat-run/candidates/{desktop-main,cli-main} on the Mac
```

The deploy helper that does all of it in one command is recorded in [§1.2](#12-the-deploy-loop).

### Path tokens on the Mac

`~/vcat-run/env.sh` from pass 1 still defines `$EV`, `$CAND`, `$CORPUS`, `$VCAT`, `$VCLI` and
`$DATAHOME`. Pass 2 adds two more, appended to the same file:

```shell
export VCATM=$CAND/desktop-main/VisualCat     # the desktop built from this tree
export VCLIM=$CAND/cli-main/vcat              # the CLI built from this tree
```

### To resume

```shell
# from Windows
ssh -i %USERPROFILE%\.ssh\windows_claude_ed25519 benny@192.168.0.199
# on the Mac
. ~/vcat-run/env.sh
pkill -f shotd.sh
osascript -e 'tell application "Terminal" to do script "exec $HOME/vcat-run/shotd.sh"'
osascript -e 'tell application "System Events" to set visible of process "Terminal" to false'
```

---

## 1. Harness

### 1.1 State at the start of pass 2

| Thing | State |
|---|---|
| Mac | reachable over SSH, macOS 26.6.2 (25G83), `MacBookPro17,1` |
| `~/vcat-run/` | intact from pass 1 — candidates, corpus, evidence, helper scripts |
| Android phone `RFCRC0A9GND` | attached and still authorized; `adb devices` lists it as `device` |
| `adb` | `/opt/homebrew/bin/adb`, server restarted on `tcp:5037` |
| `dotnet` on the Mac | **absent** — the candidate is cross-built on Windows |

### 1.2 The deploy loop

One command rebuilds and redeploys both heads:

```shell
bash <scratch>/deploy-mac.sh both     # or: desktop | cli
```

It publishes `osx-arm64` self-contained, tars the publish directory, `scp`s it, and unpacks it
into `~/vcat-run/candidates/{desktop-main,cli-main}` with the executable bit restored.

---


## 2. Finding-by-finding status

Re-verified against **`main`** before anything was changed, so a fix is never written for a
defect that was already closed, and never claimed for one that was not. Every row below was
*executed*, not read off the source: the CLI rows against the `cli-main` build on the Mac, the
desktop rows against the `desktop-main` build driven through the accessibility API on the live
Aqua session.

**Six findings turned out to be already closed on `main`, in whole or in part**, by work that
landed after 2.0.13 was tagged. That is the most important number in this pass, and it sharpens
pass 1's release recommendation rather than weakening it: the gap between `main` and the current
release is now measured instead of assumed.

| # | Sev | On `main` before this pass | Verdict |
|---|---|---|---|
| F-01 | Minor | packaging template still emits `sha256sum` for every Unix RID | **open** |
| F-02 | Minor | `tar -C <publish> -czf … .` — still no wrapper directory | **open** |
| F-03 | Major | menu-bar title now reads **`VisualCat`** ✅; but `About Avalonia`, `count menu bar items` = **2**, `Hide Others` = **⌥⌘Q** | **partly open** |
| F-04 | Minor | a *newly created* data root is `0700` ✅; an existing `0755` root is never narrowed | **partly open** |
| F-05 | Minor | `SUPPORT.md` macOS row unchanged | **open** |
| F-06 | Major | `-v long`: 19 998/19 998 parsed, 0 unknown, 0 rejected, detected `LongFormat` at confidence **1** ✅ | **parse fixed; tests open** |
| F-07 | Minor | `/etc/localtime` → `Europe/Prague`, manifest says `Europe/Bratislava` | **open** |
| F-08 | Major | session directories `drwx------` ✅; `settings.json` `-rw-r--r--`, data root `drwxr-xr-x` | **partly open** |
| F-09 | Major | three real `CGEvent` clicks on the blocked parent, then close: **no replay, no chooser** ✅; minimise button now **disabled** ✅; `AXModal` still **false** | **mostly fixed** |
| F-10 | Major | `--adb /tmp` and `--adb /tmp/definitely-not-here` both exit **0** and list the phone | **open** |
| F-11 | Minor | real `adb` at `~/Library/Android/sdk/platform-tools/adb`, `PATH` stripped → `ADB was not found` | **open** |
| F-12 | Polish | message still `Set --adb, ANDROID_SDK_ROOT, or PATH.` | **open** |
| F-13 | Polish | no ancestor check at save time | **open** |
| F-14 | Major | `MainView.FileOperations.cs` still assigns `Text` before `Name` on the live region | **open** |
| F-15 | Minor | *Live ADB capture* measured live at **600 × 438** with content ending near 300 | **open** |
| F-16 | Polish | search field is named ✅; no help text; legend still outside the inspector's scroller | **partly open** |
| F-17 | Minor | `RestoreWorkspaceAsync` returns immediately unless `OperatingSystem.IsAndroid()` | **open** |
| F-18 | Major | 41 lines in the file → `41 in session`, `41 match the filter`, `Stopped · 41 entries kept` ✅; **`40 in view`** until *Fit* is pressed | **mostly fixed; viewport open** |
| F-19 | Minor | README template still links `blob/main` | **open** |
| F-20 | Minor | CLI default is chronological ✅; `ApplicationSettings.ExportOrder` still defaults to `SourceSequence` | **open** |
| F-21 | Polish | unchanged | **open** |
| F-22 | Minor | full screen at `0,0,1440,900` draws the **whole toolbar** correctly ✅ | **fixed; position open** |
| F-23 | Major | display asleep → exit **134**, the same trace printed **twice**, 38 lines | **open** |

### 2.1 The rows that were already closed, and the evidence

**F-03 — the application name.** `App.Initialize` sets `Name = "VisualCat"`, and Avalonia
carries that into `NSApplication`:

```shell
$ osascript -e 'tell application "System Events" to tell process "VisualCat" \
    to get name of every menu bar item of menu bar 1'
Apple, VisualCat            # 2.0.13 said: Apple, Avalonia Application
```

Everything else in that finding — the menu's *contents* — is unchanged:

```shell
$ … get name of every menu item of menu 1 of menu bar item 2 of menu bar 1
About Avalonia, missing value, Services, missing value, Hide VisualCat,
Hide Others, Show All, missing value, Quit
$ … count menu bar items of menu bar 1
2
$ … AXMenuItemCmdChar / AXMenuItemCmdModifiers of "Hide Others"
Q | 2                       # ⌥⌘Q, one modifier away from ⌘Q
```

**F-06 — the long-format parser.** Regenerated on the Mac from the `main` CLI:

| Measure | 2.0.13 | `main` |
|---|---|---|
| header lines in the file | 19 998 | 19 998 |
| `parsedEntries` | **6 650** | **19 998** |
| `continuations` | 33 348 | 20 000 |
| detected format / confidence | refused, 0.1596 | `LongFormat`, **1** |

All five generated formats now detect at confidence 1 (`threadtime`, `time`, `brief`, `long`,
`epoch`), so the assertion pass 1 asked for has something to assert.

**F-09 — the queued modal click.** The pass-1 sequence was repeated with `cliclick`, which posts
a real `CGEvent` rather than an accessibility action: three clicks at `65,117`, the measured
centre of *Open log* on the blocked parent, with *Live ADB capture* open, then Escape to close
the dialog. `get name of every sheet of window 1` returned **nothing** and the window list held
only the main window. The click is discarded now, not queued. The dialog's minimise button is
also disabled (`enabled` → `false`), which closes the second half of that finding's suggestion 3.
`AXModal` is still `false`, so assistive technology is still not told the window is modal — that
is what is left.

**F-18 — the stranded follow records.** 40 records appended at 0.3 s to a file already holding
one, then left idle:

```
40 in view  ·  41 match the filter  ·  41 in session  ·  05-15 14:13:37.496 — 05-15 14:20:00.000
Capturing · 41 lines received · no source lines for 1m 9s
```

and after *Stop capture*: `Stopped · 41 entries kept` beside `41 in session`. Store, footer and
status bar agree, through an idle period and across finalization — pass 1's `51 in view` next to
`61 entries kept` is gone. What remains is the **`40 in view`**: pressing *Fit the complete
session* changes it to `41 in view` with the same span printed beside it, so the live viewport's
left edge sits just inside the first entry. That is one defect with F-21's "not fitted on first
paint", not two.

**F-22 — the full-screen toolbar.** Entering full screen through `AXFullScreen` puts the window
at `0, 0, 1440, 900` and the toolbar renders complete and unobstructed — every command from
*Open log* to *More* fully visible (`f22-fs2.png`). What is still missing is that finding's
fourth suggestion: `settings.json` stores `windowWidth`/`windowHeight` but no origin, so the
window returns to `0, 30` every launch.

### 2.2 The rows that reproduced exactly

```shell
$ vcat adb-devices --adb /tmp                      # a directory
[ { "serial": "RFCRC0A9GND", "state": "Device", … } ]    exit=0
$ vcat adb-devices --adb /tmp/definitely-not-here
[ { "serial": "RFCRC0A9GND", "state": "Device", … } ]    exit=0     # F-10

$ ln -s $(which adb) ~/Library/Android/sdk/platform-tools/adb
$ env -i HOME=$HOME PATH=/usr/bin:/bin:/usr/sbin:/sbin vcat adb-devices
error: ADB was not found. Set --adb, ANDROID_SDK_ROOT, or PATH.     # F-11 and F-12

$ readlink /etc/localtime                          # Europe/Prague
$ vcat info … | grep timeZoneId                    # "Europe/Bratislava"   # F-07

$ stat -f '%Sp %N' "$HOME/Library/Application Support/VisualCat"/*
drwxr-xr-x  …/Diagnostics      drwxr-xr-x  …/SessionAccess-v1
drwxr-xr-x  …/Sessions         -rw-r--r--  …/settings.json          # F-08

$ pmset displaysleepnow && ./VisualCat ; echo $?
…InvalidOperationException… -6661 …  (printed twice, 38 lines)   134   # F-23
```

---

## 3. What this pass changes

Filled in as each change lands, with its live test beside it.
