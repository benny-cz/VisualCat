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
| Status | **COMPLETE** — every finding implemented and live-verified; the Mac and the phone handed back ([§5](#5-hand-back)) |
| Tree | branch `main`, working from `1338921` (the commit that closed pass 1) |
| Candidate under test | a **build of this tree**, not a release tarball — `2.0.13-dev+<commit>` |
| Last completed | §8 — I-15, I-16 and B-21 closed; everything in pass 1's standing list that a machine can settle is settled |
| Findings closed | all 23. F-09 as far as the product can (AXModal is upstream); F-13.2 needs a .app bundle this product does not ship |
| Tests | 1,092 pass (47 Domain, 151 Core, 194 Application, 700 App) |
| Next step | a release ([§6](#6-the-release-this-pass-argues-for)), and the four rows in [§7.3](#73-what-still-needs-a-person) that need a person at the Mac |

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

### 3.1 F-10, F-11, F-12 — the ADB locator and the three messages around it

**What changed.** `AdbLocator` now answers with a result rather than a guess.

* An **explicitly configured path is authoritative** on both surfaces. A directory, a path that
  does not exist, and a file without an execute bit are each refused *by name*, with the remedy
  in the same sentence. It never falls through to `PATH`.
* The **default SDK roots are the ones real machines have**: `~/Library/Android/sdk` on macOS,
  `~/Android/Sdk` and `~/.local/share/Android/Sdk` on Linux, **both letter cases** of the last
  segment — because `Sdk` and `sdk` differ only in case, which resolves on a case-insensitive
  APFS or NTFS volume and does not on a case-sensitive one. Homebrew's linked executable
  (`/opt/homebrew/bin/adb`, `/usr/local/bin/adb`) is probed directly, because Homebrew ships no
  `platform-tools` tree for the SDK probe to find and reaches the product today only through a
  `PATH` a Finder-launched process does not inherit. The Windows `LOCALAPPDATA` convention is
  kept, last, so profiles that rely on it keep working.
* The **"ADB was not found" text is generated from the search itself** — `SearchLocationSummary()`
  walks the same probe order the resolver walks — so the message cannot drift from the behaviour
  again, and it ends with how to obtain `adb` on this platform.
* A device that is attached but absent from `adb devices` now gets the **macOS remedy** beside
  the Linux `udev` one: an Apple silicon Mac asks once per new accessory, and until that prompt
  is answered the device is not listed at all — indistinguishable from a dead cable.
* Nothing composes user-facing text out of an exception. `AdbLocator.Resolve` returns an
  `AdbResolution` carrying the sentence, which is what keeps the views inside the repository's
  own guard (`NoViewComposesUserTextFromAFrameworkException`).

**Live, on the Mac, against the physical phone `RFCRC0A9GND`:**

```shell
$ vcat adb-devices --adb /tmp
error: The configured ADB path '/tmp' is a directory, not the adb executable.
       Point it at the file, usually '/tmp/platform-tools/adb'.                    exit=2
$ vcat adb-devices --adb /tmp/definitely-not-here
error: The configured ADB path '/tmp/definitely-not-here' does not exist. Correct it,
       or clear it to search the Android SDK locations and PATH instead.           exit=2
$ chmod 644 /tmp/p2-noexec-adb && vcat adb-devices --adb /tmp/p2-noexec-adb
error: The configured ADB path '/tmp/p2-noexec-adb' is not executable.
       Run 'chmod +x /tmp/p2-noexec-adb'.                                          exit=2
$ vcat adb-devices --adb /opt/homebrew/bin/adb
[ { "serial": "RFCRC0A9GND", "state": "Device", … } ]                              exit=0
```

Each of the first three exited **0** and listed the phone before this change.

```shell
$ ln -s $(which adb) ~/Library/Android/sdk/platform-tools/adb
$ env -i HOME=$HOME PATH=/usr/bin:/bin:/usr/sbin:/sbin vcat adb-devices
[ { "serial": "RFCRC0A9GND", … } ]        # F-11: the macOS SDK location, found

$ mv ~/Library/Android/sdk ~/Library/Android/sdk-off       # only Homebrew's link left
$ env -i HOME=$HOME PATH=/usr/bin:/bin:/usr/sbin:/sbin vcat adb-devices
[ { "serial": "RFCRC0A9GND", … } ]        # F-11: /opt/homebrew/bin/adb, off PATH
```

and with nothing findable at all:

```
error: ADB was not found. It was looked for in, in order:
         --adb <path> on the command line
         the ADB path in Appearance & timeline (desktop)
         ANDROID_SDK_ROOT or ANDROID_HOME pointing at an SDK directory
         /Users/benny/Library/Android/sdk/platform-tools/adb
         /Users/benny/Library/Application Support/Android/Sdk/platform-tools/adb
         /opt/homebrew/bin/adb
         /usr/local/bin/adb
         adb on PATH
       Install it with 'brew install --cask android-platform-tools', or from Android
       Studio, which puts it in ~/Library/Android/sdk/platform-tools.
```

**One extra defect found while testing this, and fixed.** With the resolved `adb` deliberately
moved out from under a running dialog, *Live ADB capture* reported

> Could not refresh devices · An error occurred trying to start process
> '…/platform-tools/adb' with working directory '/Users/benny'. No such file or directory

— the .NET `Process.Start` message, which names the working directory, the one thing that is not
the problem. The dialog now checks whether the executable is still there and says so instead.
That is F-10's third suggestion applied to the case the finding did not reach: an `adb` that was
valid when the dialog opened and is gone by the time it runs, which is what a `brew upgrade`
during a session produces.

---

### 3.2 F-23 — starting with no display

**What changed.** The desktop head asks CoreGraphics whether this Mac has an awake display
*before* Avalonia starts its CoreVideo display link, waits up to six seconds for one, and then
either starts normally or prints a sentence and exits `69` (`EX_UNAVAILABLE`). The race the
pre-flight cannot close — the display sleeping between the check and the backend's own
initialisation — is caught by message, and answered with the same sentence.

The wait is the part that matters most in practice: the common human case is clicking the icon as
the screen goes dark, or launching over SSH just after sending a wake.

Two smaller things went with it. The catch-all that printed an exception and then rethrew it is
**gone**, which is what doubled every unhandled start-up trace (this finding and
`LINUX-LIVE-TEST-REPORT` F-02). And the message **names `vcat`**, because the product has a
complete answer for the unattended case and a user staring at a crash has no way to know it.

**Live, with `pmset displaysleepnow`:**

| | `main` before | after |
|---|---|---|
| exit code | **134** (`SIGABRT`) | **69** |
| stderr | 38 lines, the same stack trace twice | **7 lines** |
| `~/Library/Logs/DiagnosticReports/VisualCat-*.ips` | **+1 per attempt** | **+0** |

```
VisualCat could not start because this Mac has no display it can draw on:
  the screen is asleep, locked, or no display is attached.
  Wake the display and try again.
  For an unattended or scheduled capture, use the vcat command line instead — it
  needs no display, and it is in the VisualCat-CLI archive beside this one:
      vcat capture-adb --serial <device> --output capture.vcat
      vcat index <logfile> --output session.vcat
```

**And the rescue path, which is the new behaviour rather than a better error.** Launched with the
display asleep, then woken two seconds into the six-second wait:

```shell
$ pmset displaysleepnow && sleep 3 && nohup ./VisualCat >/tmp/f23c.log 2>&1 &
$ sleep 2 && caffeinate -u -t 3
$ pgrep -fl VisualCat        # 4400 …/desktop-main/VisualCat
$ cat /tmp/f23c.log          # (empty)
$ osascript … get name of every window
VisualCat v2 — See the shape of your log
```

It started, with nothing on stderr. Before this change that launch was a crash report.

---

### 3.3 F-14 — live regions can no longer announce under an empty name

**What changed.** Every accessibility live region in the product now goes through one seam,
`LiveRegion`, which enforces two rules:

1. **`Attach(element, name, setting)`** — the accessible name is set *before* the live setting, so
   an element is never a live region without a name, not even for the instant between two
   statements.
2. **`Announce(element, text, fallbackName)`** — the name is set *before* the assignment that
   raises the change, and a cleared region falls back to its own name rather than to nothing.

Nineteen call sites were converted. The clearest instance was the one the finding names:
`MainView.FileOperations` created the announcement element with no text and no name, gave it a
live setting, and named it *after* the `Text` assignment that raised the change.

Two elements toggle their live setting per update and so could not use `Announce`; both are now
**named at construction** — `_searchProblem` through a factory that attaches the live region with
its name, and the search-match counter with a static name before `UpdateMatchPosition` ever
touches it. The ordering in `UpdateMatchPosition` is deliberately left as it was, because there
the *name change* is what raises the announcement and `Off` is how a non-arrival stays silent;
naming it at construction is what makes that order safe.

**A second, user-visible improvement fell out of it.** The Android notice lane was a live region
whose accessible name was the constant `"Application status"`, so a screen reader was told that
something had changed and never what. The lane now announces **the message**, falling back to
`"Application status"` only when it is empty.

**What is not claimed.** The crash itself was one observed abort in pass 1 and was never
reproduced, so this pass cannot demonstrate a fix by reproduction either. What it can show is
that the product no longer contains the shape that produces it, enforced in one place rather than
nineteen. The upstream half — `-[AvnAccessibilityElement raiseLiveRegionChanged]` building an
`NSDictionary` that can throw — remains an Avalonia issue and belongs on this repository's
upstream list with the `.ips` attached.

---

### 3.4 F-07 — the host time zone

**What changed.** `TimeZoneResolution.HostZoneId()` reads `/etc/localtime`'s link target on macOS
and prefers the identifier it names, falling back to `TimeZoneInfo.Local.Id` when the target does
not resolve to a real zone. Every place that recorded the host zone — the session's timestamp
policy, an ADB capture's declared zone, the desktop's display zone — goes through it.

The check that the identifier resolves is what keeps the fallback honest: a path that does not
name a zone never reaches a manifest.

---

### 3.5 F-04 and F-08 — the modes that were still wide

Sessions were already `700`/`600` on `main`. Three things under the data root were not, and are
now:

* the **data root itself**, when it was created by an older build. `Directory.CreateDirectory`'s
  mode overload applies only at creation, so a root laid down by 2.0.13 at the account's `umask`
  kept `0755` for ever. It is narrowed on start-up when it is wider than owner-only — the one
  moment an upgrade can correct it, and a directory this product owns is safe to narrow unasked.
* **`settings.json`**, which holds the session directory, the configured ADB path and a list of
  recently open session paths, and sat at `0644`.
* the **diagnostics directory**, which is precisely the artifact a user would not expect another
  local account to be able to read.

---

### 3.6 F-03 — the macOS menu bar

**What changed.** VisualCat has a menu bar. `count menu bar items` goes from **2** to **7**.

```shell
$ osascript -e 'tell application "System Events" to tell process "VisualCat" \
    to get name of every menu bar item of menu bar 1'
Apple, VisualCat, File, Edit, View, Window, Help
```

Two exports, not one, and confusing them is why the first attempt put *File* inside the
application menu: `NativeMenu.GetMenu(Application.Current)` is the **application** menu — the one
bearing the product's name — while the **menu bar** hangs off the window. Both have a timing
constraint that cost an iteration each: Avalonia synthesises the application menu when the app
declares none and stores it back on the `Application`, so replacing that property later changes a
value nothing reads again; and the macOS exporter reads a window's menu as the window becomes
key, so a menu set after the window is on screen is never exported. The application menu is
therefore **mutated in place**, and the menu bar is set **before the window is shown**.

**The application menu, measured live.** `About Avalonia` is gone, `Settings…` is where macOS
users look for it, and *Hide Others* is bound to **⌥⌘H**:

```
About VisualCat [–]      Settings… [⌘,]      Services
Hide VisualCat [⌘H]      Hide Others [⌥⌘H]   Show All      Quit [⌘Q]
```

The ⌥⌘Q binding that shipped is gone — it sat one modifier away from ⌘Q on a command people reach
for in a hurry, and hitting the wrong one cost an unsaved capture.

**The File menu is generated from the command descriptors** the toolbar and the *More* menu are
built from, so a command cannot exist in one presentation and be missing from another — the rule
the shell already applies between the desktop toolbar and the phone command sheet. Enablement
comes from the same predicates, so a menu never offers a command that would do nothing:

```
Open log… [⌘O]                    ADB live… [⇧⌘L]
Open log with options… [⌥⌘O]      Open session… [⇧⌘O]
Recent captures… [⇧⌘R]            Follow growing file…      Open portable archive…
——
Save session… [⌘S]        en=false      Save portable… [⇧⌘S]   en=false
Export CSV… [⌘E]          en=false      Lines not on the timeline…   en=false
——
Close Window [⌘W]
```

(The four `en=false` rows are correct: the screenshot was taken with no session open.)

**Edit, View, Window and Help**, and what macOS adds to them once they exist — which is the part
that shows the menus are real rather than decorative:

```
Edit    Cut [⌘X]  Copy [⌘C]  Paste [⌘V]  Select All [⌘A]  ·  Find… [⌘F]
        Find Next [⌘G]  Find Previous [⇧⌘G]
        · Writing Tools · AutoFill · Start Dictation… [⌃D] · Emoji & Symbols [⌃⌘Space]   ← added by macOS
View    Fit Session [⌘0]  Zoom In [⌘=]  Zoom Out [⌘−]
        · Enter Full Screen [⌃⌘F]                                                        ← added by macOS
Window  Minimize [⌘M]  Zoom
Help    VisualCat Help · Keyboard Shortcuts · Release Notes · Report a Bug
```

**⌘ instead of Ctrl.** `Platform.PlatformShortcuts.Primary` is `Meta` on macOS and `Control`
elsewhere, and both shortcut handlers route through it. macOS answers to Command **only**:
accepting Control as well would take ⌃F, ⌃A and ⌃E away from the text fields the system gives
them to, which is a worse trade than asking a Mac user to press the key their platform has always
used.

**About VisualCat** opens the product's own about box — icon, the full informational version
(`2.0.13-dev+307cc8cb54a7b9ac8971ac623591561fd65acf8f`, selectable, because the one thing anyone
does with a version string is paste it into a bug report), the strapline, the local-only privacy
line and the licence. Evidence: `f03-about.png`.

---

### 3.7 F-15 — the two half-empty dialogs

**What changed.** `DialogBody.SizesToContent` makes the desktop window take its height from the
content, with the declared preferred height as a **cap** rather than a fixed size, so a long form
still scrolls instead of growing past the screen. *Live ADB capture* — a `Window` rather than a
`DialogBody` — gets `SizeToContent.Height` with a `MaxHeight` directly.

Measured live through the accessibility API, in points:

| Dialog | pass 1 | now |
|---|---|---|
| *Live ADB capture* | 600 × **438** | 600 × **375** |
| *About VisualCat* | — (did not exist) | 460 × **195** |

`f15-adb-sized.png` shows the result: the decision row sits directly under the last control
instead of 130 points below it. The import review is opted in the same way, and grows when its
*Import options* disclosure opens rather than reserving the room while it is collapsed.

---

### 3.8 F-09 — what could and could not be closed

Pass 1's headline symptom — a click on the modally-blocked parent replayed after the dialog closed
— **does not reproduce on `main`** ([§2.1](#21-the-rows-that-were-already-closed-and-the-evidence)),
and the dialog's minimise button is already disabled. Of the finding's remaining parts:

* **`AXModal` is still `false`.** `AutomationProperties.SetAccessibilityView` was tried and
  changes nothing: Avalonia's macOS backend does not map an owned modal window to a modal
  accessibility state, exactly as its AT-SPI backend does not
  (`LINUX-LIVE-TEST-REPORT` F-11). This is upstream, and it is recorded here rather than
  papered over.
* **Sheets** (the finding's suggestion 3) are not implemented. Avalonia exposes no sheet
  presentation for a macOS window, so this would be an upstream feature request rather than a
  product change. The two problems the suggestion existed to solve are both closed by other
  means — the minimise trap is gone, and the sizing is fixed above.

### 3.9 F-18 and F-21 — the viewport, and why two correct numbers looked wrong

Pass 1 read `32 in view · 33 match the filter` with no filter active, and pass 2 read
`40 in view · 41 match the filter` — off by exactly one both times, which looked like an
off-by-one and was not. Follow keeps a **30-second window** on a session six minutes long, so the
one early record in each corpus sits outside it. Both numbers were right; nothing on screen said
they counted different populations, and the span printed at the end of the same line is the
*matching* range, which appears to cover all 41.

**What changed.** When the viewport is narrower than the matching range, the count says which
window it is counting: `40 in view (30 s) · 41 match the filter · 41 in session`. Two numbers
that looked inconsistent now obviously count different things.

**The second half was a real defect.** `FitViewport` used `MinimumViewportUs` — two seconds — as
its floor, and padded only the **left** edge. So importing a 1.501-second file opened at a
two-second window pinned to the session's end, leaving about a quarter of the plot blank in front
of the data, which a reader reads as a quiet period in their own log rather than as empty canvas
(finding F-21). Two floors that were one number are now two:

| Constant | Value | What it is for |
|---|---|---|
| `MinimumViewportUs` | 2 s | a **live tail**, so a session holding one record does not draw a one-microsecond span |
| `MinimumFitViewportUs` | 10 ms | a **fit**, so any real session fits exactly and a degenerate one is still drawable |

and the widened window is **centred** on the session rather than right-aligned. Ten milliseconds
across a 2,600-pixel plot is 3.8 µs per pixel — nowhere near claiming the microsecond precision
the two-second floor exists to prevent.

Three tests hold it: a one-entry import opens at a drawable span containing the whole session, an
ordinary import opens *exactly* fitted, and a short session is centred rather than pinned
(`AShortSessionIsCentredInTheFittedWindow`).

---

### 3.10 F-17 — after a crash, and after an ordinary quit

**What changed.** The desktop was writing `openSessionPaths` on every exit and reading it back
nowhere: `RestoreWorkspaceAsync` returned immediately unless `OperatingSystem.IsAndroid()`. So
after a crash — and, as pass 1 discovered, after an ordinary ⌘Q with three tabs open — the next
launch showed the empty start page, with nothing saying the captures were safe or where to find
them.

The start page now offers to reopen them, and says plainly when the previous run did not end
cleanly:

> **VisualCat closed unexpectedly. Your 2 captures are safe — reopen them, or find them under
> Recent captures.** \[Reopen\]

after a clean exit, simply:

> **3 captures were open when you last closed VisualCat.** \[Reopen\]

An **offer**, not an automatic restore, exactly as the finding asks: reopening a 17 MB capture
unasked is its own annoyance, and a reader whose last session ended in a crash may want to start
somewhere else. Android keeps restoring automatically — one stray Back press finishes the
activity there, so its workspace goes away by accident rather than by decision.

"Unexpectedly" is a fact, not a guess: a zero-byte marker under the data root is written when the
workspace is first persisted and deleted by the window's `Closed` handler, so its presence at
start-up means the previous process died without closing. Only paths that still exist and still
hold a `manifest.json` are offered, so a deleted capture never appears in the offer. The offer
and its outcome are recorded in the product's own diagnostics (`workspace.restore.offered`,
with `previousExitWasClean`), which is the finding's third suggestion: a diagnostic bundle
collected afterwards now contains the fact that a crash happened.

---

### 3.11 F-20, F-21 — the export defaults and the export result line

* **The desktop default is chronological**, the same as the CLI's and the same as the timeline's
  own order. Exporting the same session from the two surfaces with default options produced two
  different files and nothing said why.
* **The result line names the order and the folder.** The desktop remembers whichever order the
  reader last chose, so "the desktop default" is a per-user value no document can describe — and
  a CSV that differs from the CLI's for that reason looks like a defect rather than a preference.
  And on macOS the save panel may have been redirected by ⇧⌘G or by a remembered location, which
  makes the absolute path the useful half of the answer:

  ```
  Exported 1,000 timed rows · all timed entries in session · chronological order ·
  /Users/benny/vcat-run/evidence/…/desktop-export.csv
  ```

  where it read `Exported 1,000 timed rows · all timed entries in session · desktop-export.csv`.

---

### 3.12 F-21, F-16 — Recent captures, and the legend under the status bar

**Recent captures had two selection models and explained neither.** The checkbox column drives
the footer count and *Delete captures…*; the row highlight drives *Open*. Checking a single
capture left *Open* disabled, which reads as a broken button rather than as a different kind of
selection.

*Open* now acts on **a single ticked capture** as well as on a highlighted row — one tick is an
unambiguous answer to "open which one" — and both buttons say which selection they act on, in
their tooltip and their accessible help text:

* *Delete captures…* — "Deletes the 3 captures with a tick." / "Tick the captures to delete first."
* *Open* — "Opens the highlighted capture, or the single ticked one." / "More than one capture is
  ticked. Highlight the one to open, or untick the rest."

**The legend was painted over the status bar.** The desktop inspector pane had
`ClipToBounds = false`, so with *SELECTED ENTRY* open on a 795-point-tall window the outcome
legend (`en entry · mt marker · .. continuation · e? untimed · ?? unknown · !! rejected`) was
drawn into the same band as `Ready · 1,000 entries` and the two overlapped into each other.
Clipping alone would have *hidden* the legend, so the pane gets its own scroller — which is where
content that does not fit belongs — and then clips.

---

### 3.13 F-22 — the window comes back where it was

The full-screen half of this finding was already closed on `main`
([§2.1](#21-the-rows-that-were-already-closed-and-the-evidence)). Its fourth suggestion was not:
`settings.json` stored `windowWidth` and `windowHeight` but no origin, so a window moved to a
second display or to a corner of a large screen came back at `0, 30` every launch.

`windowLeft` and `windowTop` are stored beside the size and **validated against the display
arrangement that exists now**. A stored origin is a fact about an arrangement, and arrangements
change: a laptop undocked from a monitor that was to its left holds a negative x that names
nothing, and a window placed there is invisible with no way to reach it. The saved frame must
overlap an attached screen's working area by at least 120 × 40 points — enough title bar to grab
— or the platform's own placement is used, which is what happened every launch before this.

---

### 3.14 F-13 — a session cannot be saved inside another session

A `.vcat` session is a directory, so every platform's save panel navigates into one as readily as
into any folder. During pass 1 a *Save portable* landed at
`…/cli-capture.vcat/ADB RFCRC0A9GND 13h00m23-portable-….vcat`. Both sessions still verified, so
nothing was corrupted; what the outer session gained was seven megabytes its own manifest does
not describe, and a later cache-retention sweep of the outer session would take the inner one
with it.

`SessionSaveService` now walks up from the chosen destination — bounded to 24 levels, because a
session is never nested deeper than a handful and an unbounded walk costs a `stat` per level on
every save — and refuses a destination inside anything that looks like a session:

> That location is inside the session 'cli-capture.vcat'. Choose a directory outside it. A
> session is a directory, so a save panel will navigate into one, and a session nested inside
> another is carried along by the outer session's cache retention without its manifest
> describing it.

The finding's second suggestion — declaring a `UTExportedTypeDeclarations` package type so
Finder treats a session as one document — needs a `.app` bundle, which this product deliberately
does not ship. Its third is done: the macOS `README.txt` now says a saved session is a directory,
how to move one, and that *Open session* wants the directory rather than a file inside it.

---

### 3.15 F-01, F-02, F-19 — the release archives

Three packaging defects, all of which regenerate themselves on every release, and all of which
now have an assertion that stops them coming back.

**F-01 — `sha256sum` on macOS.** The verify line, whose whole purpose is to run *before* the
binary does, told every Unix user to run a GNU coreutils command that macOS did not ship under
that name until macOS 26. Both Unix artifacts now print

```
shasum -a 256 --ignore-missing -c SHA256SUMS      (run it beside the archives)
```

`shasum` is present on every supported macOS and on every mainstream Linux, so one line serves
both and there is one fewer branch to keep correct. `--ignore-missing` is there because a release
page carries a dozen assets and a user downloads one: without it both tools print
`FAILED open or read` for the eleven that are not there, which reads as a failed verification to
anyone who has not seen it before. `docs/RELEASE-NOTES.md` says the same thing, and
`verify-package-contents.ps1` fails any Unix archive whose README still says `sha256sum`.

**F-02 — the wrapper directory.** Every member of every archive was `./<name>`: one directory
member and 239 files beside it. The README's first instruction implied
`cd ~/Downloads && tar -xzf …`, which scattered 240 generically-named .NET assemblies into
whatever directory the user was standing in, mixed with everything already there — genuinely hard
to undo by hand. Every archive now carries exactly one top-level directory named after the
archive itself, on **every** platform including the Windows `.zip`, and the README opens with

```
tar -xzf VisualCat-Desktop-osx-arm64-v2.0.14.tar.gz
cd VisualCat-Desktop-osx-arm64-v2.0.14
./VisualCat
```

Two assertions make it unable to regress: the release workflow checks that each archive it is
about to upload has exactly one top-level entry and that it is a directory, and
`verify-package-contents.ps1` additionally requires that entry to be named after the archive and
to contain the expected members.

**F-19 — the documentation links.** Every link in a shipped `README.txt` pointed at `blob/main`,
so a 2.0.13 user following the link their own archive gave them read documentation for code they
did not have — and the better the live testing gets, the wider that gap grows, because each run
adds to `main` features the released binary refuses. The links now carry the release tag, and
`verify-package-contents.ps1` fails any README that still links `main`.

---

### 3.16 F-05, F-06, F-08, F-12, F-14 — the documents

| Document | What it now says |
|---|---|
| `SUPPORT.md` | the macOS row states **macOS 12 (Monterey) or later**, both architectures, Rosetta 2 for `osx-x64`, what a bare executable does not provide, and that the desktop head needs an awake display |
| `SUPPORT.md` | a new **Reporting a problem** section: where the build identifier is on each surface, where the crash evidence lives per platform (`~/Library/Logs/DiagnosticReports/VisualCat-*.ips` on macOS), and the display-asleep refusal with its exit code |
| `PRIVACY.md` | the `700`/`600` promise now says "on any volume that can express POSIX modes", names `settings.json` and the diagnostics directory, records that an older data root is narrowed on the next launch, and states plainly that a session written to exFAT, FAT or an SMB share cannot be protected this way |
| `KEYBOARD.md` | a **macOS column** for every shortcut, the rule that the primary modifier is ⌘ on macOS and Ctrl elsewhere, why macOS does not also answer to Ctrl, and the menu-bar-only commands (⌘0, ⌘±, ⌘,, ⌘W, ⌘M, ⌥⌘H) |
| `CLI.md` | a **Where ADB is looked for** section listing all five routes in order — it documented three — and stating that `--adb` is authoritative |
| `RELEASE-NOTES.md` | `shasum -a 256 --ignore-missing`, with the reason |

F-06's remaining two suggestions were **already on `main`**: `SyntheticLogFormatTests` requires
every generated format to detect as itself at confidence ≥ 0.9 — the assertion that would have
caught the original defect — and `test-data/golden-formats.txt` already carries the four
five-digit-thread-id fixtures with narrow and wide pid/tid combinations.

---

## 4. Live verification

Every row below was executed on the Mac against a build of this tree, with the phone attached.
Nothing here is inferred from the source.

### 4.1 The command line

| Finding | Command | Result |
|---|---|---|
| F-10 | `vcat adb-devices --adb /tmp` | `error: The configured ADB path '/tmp' is a directory, not the adb executable. Point it at the file, usually '/tmp/platform-tools/adb'.` · **exit 2** (was exit 0, listing the phone) |
| F-10 | `vcat adb-devices --adb /tmp/definitely-not-here` | `…does not exist. Correct it, or clear it to search the Android SDK locations and PATH instead.` · **exit 2** |
| F-10 | `--adb` at a file with mode 644 | `…is not executable. Run 'chmod +x /tmp/p2-noexec-adb'.` · **exit 2** |
| F-10 | `--adb /opt/homebrew/bin/adb` | the phone, **exit 0** — a correct pin still works |
| F-11 | `adb` at `~/Library/Android/sdk/platform-tools/adb`, `PATH` stripped to `/usr/bin:/bin:/usr/sbin:/sbin` | the phone is listed |
| F-11 | only `/opt/homebrew/bin/adb`, no SDK, `PATH` stripped | the phone is listed |
| F-12 | nothing findable | the eight-line list of every location searched, ending with `brew install --cask android-platform-tools` |
| F-12 | `capture-adb --serial NOSUCHDEVICE` | `…On macOS, check that the device was allowed to connect: an Apple silicon Mac asks once per new accessory, and until that is answered ADB does not list the device at all. The setting is System Settings › Privacy & Security › Allow accessories to connect.` |
| F-13 | `vcat index … --output /tmp/p2-outer.vcat/inner.vcat` | `error: That output path is inside the session 'p2-outer.vcat'…` · **exit 2** |
| F-13 | `vcat capture-adb … --output /tmp/p2-outer.vcat/cap.vcat` | the same refusal; a normal output path still exits 0 |
| F-07 | `readlink /etc/localtime` → `…/Europe/Prague`; `vcat index` then `vcat info` | `"timeZoneId": "Europe/Prague"` — was `Europe/Bratislava` on 2.0.13 **and** on `main` |
| — | a real 10-second ADB capture from `RFCRC0A9GND` | 1 112 source lines, 1 059 parsed entries, `ThreadTime`, session directory `drwx------` |

### 4.2 Starting with no display (F-23)

```shell
$ pmset displaysleepnow && sleep 3 && ./VisualCat ; echo $?
```

| | `main` | this tree |
|---|---|---|
| exit code | **134** (`SIGABRT`) | **69** (`EX_UNAVAILABLE`) |
| stderr | 38 lines, the same stack trace twice | **7 lines**, naming `vcat` |
| new `~/Library/Logs/DiagnosticReports/VisualCat-*.ips` | **+1 per attempt** | **+0** (10 before, 10 after) |

and the launch that now succeeds where it used to crash — asleep at launch, woken two seconds
into the six-second wait:

```shell
$ pmset displaysleepnow && sleep 3 && nohup ./VisualCat >/tmp/f23c.log 2>&1 &
$ sleep 2 && caffeinate -u -t 3
$ pgrep -fl VisualCat   → 4400 …/desktop-main/VisualCat
$ cat /tmp/f23c.log     → (empty)
$ osascript … get name of every window → VisualCat v2 — See the shape of your log
```

### 4.3 The macOS menu bar (F-03)

```shell
$ osascript -e 'tell application "System Events" to tell process "VisualCat" \
    to get name of every menu bar item of menu bar 1'
Apple, VisualCat, File, Edit, View, Window, Help          # was: Apple, VisualCat
```

Read off the live process, with accelerators and enablement:

```
VisualCat  About VisualCat · Settings… ⌘, · Services · Hide ⌘H · Hide Others ⌥⌘H
           · Show All · Quit ⌘Q
File       Open log… ⌘O · ADB live… ⇧⌘L · Open log with options… ⌥⌘O
           · Open session… ⇧⌘O · Recent captures… ⇧⌘R · Follow growing file…
           · Open portable archive… ·· Save session… ⌘S [disabled]
           · Save portable… ⇧⌘S [disabled] · Export CSV… ⌘E [disabled]
           · Lines not on the timeline… [disabled] ·· Close Window ⌘W
Edit       Cut ⌘X · Copy ⌘C · Paste ⌘V · Select All ⌘A ·· Find… ⌘F
           · Find Next ⌘G · Find Previous ⇧⌘G
           ·· Writing Tools · AutoFill · Start Dictation… · Emoji & Symbols   ← macOS adds these
View       Fit Session ⌘0 · Zoom In ⌘= · Zoom Out ⌘−
           ·· Enter Full Screen ⌃⌘F                                          ← macOS adds this
Window     Minimize ⌘M · Zoom
Help       VisualCat Help · Keyboard Shortcuts · Release Notes ·· Report a Bug
```

The four disabled File items were disabled because no session was open, which is the point:
enablement comes from the same predicates the toolbar uses.

**They work, not merely exist.**

| Command | Before | After |
|---|---|---|
| **About VisualCat** | — | the product's about box, with `2.0.13-dev+307cc8cb54a7b9ac8971ac623591561fd65acf8f` selectable (`f03-about.png`) |
| **View → Fit Session** | — | `30 s · 11.3 ms/px` → `6.38 min · 144.02 ms/px`, and the count `40 in view (30 s)` → `41 in view` |
| **View → Zoom In** | — | `6.38 min` → `3.19 min · 72.01 ms/px` |
| **Edit → Find…** | — | focus moves to `AXTextField "Search message text or regex"` |
| **⌘F** | nothing at all | the same field takes focus |

### 4.4 The workspace, the window and the counts

| Finding | Evidence |
|---|---|
| F-17 | after a `pkill`: **"VisualCat closed unexpectedly. Your 1 capture is safe — reopen it, or find it under Recent captures."** with a **Reopen** button on the start page. After ⌘Q: **"1 capture was open when you last closed VisualCat."** |
| F-17 | the clean-exit marker is deleted by ⌘Q and absent on the next launch, so "unexpectedly" is a fact rather than a guess |
| F-18 | 41 lines in the file → `40 in view (30 s) · 41 match the filter · 41 in session`, beside `Capturing · 41 lines received`, held through 60 s of idle; *Fit Session* then reads `41 in view` |
| F-21 | a 1.501-second import opens at **`1.501 s · 565.1 µs/px`**, filling the plot. Pass 1: `2 s · 753 µs/px` with a quarter of it blank |
| F-22 | window moved to `300, 200` at `1100 × 700`, ⌘Q, relaunch → **`300, 200, 1100, 700`**. Before: `0, 30` every time |
| F-16 | with *SELECTED ENTRY* open on a 1440 × 795 window, the inspector ends above `Stopped · 41 entries kept`, which has its own row (`f16-full.png`). Pass 1: the two overlapped into each other |
| F-04, F-08 | the data root was deliberately widened to `0755`; the next launch narrowed it to `drwx------`. After a clean quit: `settings.json` `-rw-------`, `Diagnostics` `drwx------`. Pass 1 and `main`: `drwxr-xr-x` and `-rw-r--r--` |
| F-07 | the plot header reads **`Europe/Prague`** (`f16-full.png`) where every earlier build read `Europe/Bratislava` |

### 4.5 Export (F-20, F-21)

The decisive test, because it is the one a script would notice:

```shell
$ shasum -a 256 <desktop default export> <cli default export>
c81f3de7a2bc60a0c50c5adbff08ea2235b3a6764d64d720c134101c45cf4d20  …/export-check.csv
c81f3de7a2bc60a0c50c5adbff08ea2235b3a6764d64d720c134101c45cf4d20  /tmp/p2-cli-default.csv
```

**Byte-identical.** Pass 1 recorded the same two exports differing from line 243. The dialog's
*Row order* now opens on **Chronological**, and the result line names the order and the folder:

```
Exported 1,000 timed rows · all timed entries in session · chronological order ·
/Users/benny/vcat-run/evidence/20260914-macos-arm64-m1/p2/export-check.csv
```

### 4.6 Recent captures (F-21)

Read from the live *Open* button as the ticks changed:

| State | `enabled` | `AXHelp` |
|---|---|---|
| nothing ticked, nothing highlighted | `false` | "Highlight a capture, or tick exactly one." |
| **one ticked** | **`true`** | "Opens the highlighted capture, or the single ticked one." |
| two ticked | `false` | "More than one capture is ticked. Highlight the one to open, or untick the rest." |
| *Delete captures…* with none ticked | `false` | "Tick the captures to delete first." |

Pressing *Open* with a single tick opened the capture. Before this change, ticking a capture left
*Open* disabled with nothing on screen explaining why.

### 4.7 Dialog sizing (F-15)

Measured through the accessibility API, in points:

| Dialog | pass 1 | now |
|---|---|---|
| *Live ADB capture* | 600 × **438** | 600 × **375** |
| *Export CSV* | 720 × **560** | 720 × **328** |
| *About VisualCat* | — | 460 × **195** |

### 4.8 Packaging (F-01, F-02, F-19)

A real `osx-arm64` package built by `tools/package.ps1 -Archive`, verified by
`verify-package-contents.ps1`:

```shell
$ tar -tzf VisualCat-Desktop-osx-arm64-v2.0.13.tar.gz | awk -F/ '{print $1}' | sort -u
VisualCat-Desktop-osx-arm64-v2.0.13          # one root, 240 members beneath it
$ tar -tzf VisualCat-CLI-osx-arm64-v2.0.13.tar.gz | awk -F/ '{print $1}' | sort -u
VisualCat-CLI-osx-arm64-v2.0.13              # one root, 208 members
```

and the README the same run produced opens with `tar -xzf … && cd …`, verifies with
`shasum -a 256 --ignore-missing -c SHA256SUMS`, and links `blob/v2.0.13/docs/…`.

### 4.9 Four defects found by testing the fixes, and fixed

Live testing earned its place three more times after the code was written:

1. **The About dialog crashed on open.** Composing the layout added the content stack to the
   window and *then* moved it into a row beside the icon, which Avalonia refuses — and the
   refusal reached the reader as "The control StackPanel already has a visual parent DockPanel".
   Composed once now.
2. **The clean-exit marker survived ⌘Q.** It was deleted after two `await`s in the window's
   `Closed` handler, and the process was gone before the continuation ran — so every launch after
   a normal quit would have reported a crash that never happened. It is deleted first, and
   synchronously.
3. **`Window.Position` answers `0, 0` on macOS.** Reading it back at close time stored the
   top-left corner however the reader had arranged the window, which is the defect F-22 is about.
   The frame is now remembered from the `PositionChanged` event while the window is alive.
4. **"1 capture were open."** Fixed to "was"/"is" for the singular.

None of these is visible to a unit test; all four came from driving the real application.

---

## 5. Hand-back

### 5.1 What this pass changed on the Mac, and its state now

| What | State at hand-back |
|---|---|
| `~/vcat-run/candidates/desktop-main`, `cli-main` | **left in place** — builds of this tree, and what [§0](#0-restore-point--resume-here) resumes with. Delete with `rm -rf ~/vcat-run/candidates/{desktop,cli}-main` |
| `~/vcat-run/env.sh` | two lines appended: `VCATM`, `VCLIM` |
| `~/vcat-run/` helper scripts | six added — `press`, `axpos`, `axbtn`, `axtext`, `axrow`, `axbox` `.applescript`. They are how the accessibility assertions in [§4](#4-live-verification) and [§7](#7-the-standing-list--what-pass-2-closed-and-what-still-needs-a-person) were made |
| `~/vcat-run/evidence/20260914-macos-arm64-m1/p2/` | this pass's screenshots and the export it checked; mirrored to the Windows host at `artifacts/live-test/20260914-macos-fix-pass/` |
| `/tmp/p2-*` and `/tmp/p3-*` — corpora, sessions, ADB stubs, scratch CSVs, the portable zip | **deleted** |
| `~/vcat-run/candidates/desktop-main-x64`, `cli-main-x64` | **left in place** — the Rosetta 2 candidates §7.1 used. Delete with `rm -rf ~/vcat-run/candidates/*-x64` |
| `~/Library/Android/sdk/platform-tools/adb` (an F-11 probe) | **deleted**, including the `~/Library/Android` tree it needed |
| `/opt/homebrew/bin/adb` | moved aside during an F-12 control and **restored**; it is the Caskroom symlink it always was |
| `~/Library/Application Support/VisualCat/` | **left in place** — now `drwx------` where it was `drwxr-xr-x`, with `settings.json` at `0600`. It is the evidence for [F-04 and F-08](#35-f-04-and-f-08--the-modes-that-were-still-wide) |
| `~/Library/Logs/DiagnosticReports/VisualCat-*.ips` | **10**, unchanged since the `main` baseline run. Two were added by that baseline — the deliberate display-asleep crash and the `pkill` before it — and **none by any build of this tree** |
| Display sleep | held awake with `caffeinate -dimsu`, slept twice with `pmset displaysleepnow` to test F-23, woken each time. `caffeinate` **killed**; `pmset` settings were never modified |
| The screenshot daemon and its Terminal window | **stopped and closed** |
| `adb` server | **stopped** (`adb kill-server`) |
| Android phone `RFCRC0A9GND` | **screen locked** — `mWakefulness=Dozing`, `mDreamingLockscreen=true`. Its USB-debugging authorization is left as pass 1 left it |
| macOS version, SIP, Gatekeeper, firewall, `umask`, `ulimit`, appearance, locale, display resolution, TCC grants | **never modified** |

### 5.2 What was deliberately not done

| Row | Why |
|---|---|
| **`AXModal` on the product's dialogs** (F-09) | Avalonia's macOS backend maps no modal accessibility state for an owned `ShowDialog` window. `AutomationProperties.SetAccessibilityView` was tried live and changed nothing. Upstream, like its AT-SPI equivalent in `LINUX-LIVE-TEST-REPORT` F-11 |
| **Presenting dialogs as sheets** (F-09, suggestion 3) | Avalonia exposes no sheet presentation for a macOS window. The two problems the suggestion existed to solve are closed by other means — the minimise trap was already gone on `main`, and the sizing is fixed in [§3.7](#37-f-15--the-two-half-empty-dialogs) |
| **`UTExportedTypeDeclarations` for `.vcat`** (F-13, suggestion 2) | needs a `.app` bundle, which this product deliberately does not ship. The cheap half — refusing the nesting — is done, on every surface |
| **Guarding the live-region name in `libAvaloniaNative`** (F-14, suggestion 2) | upstream. The product's half — never letting a live region announce under an empty name — is enforced in one place now |
| **Cutting 2.0.14** (F-06, F-08, suggestion 1) | a release is the maintainer's decision, not a code change. Everything it would carry is on `main`, and [§6](#6-the-release-this-pass-argues-for) states the case |

### 5.3 To resume, or to re-run any of this

```shell
# from Windows
ssh -i %USERPROFILE%\.ssh\windows_claude_ed25519 benny@192.168.0.199
# on the Mac
. ~/vcat-run/env.sh          # adds VCATM and VCLIM to pass 1's tokens
pkill -f shotd.sh
osascript -e 'tell application "Terminal" to do script "exec $HOME/vcat-run/shotd.sh"'
osascript -e 'tell application "System Events" to set visible of process "Terminal" to false'
caffeinate -dimsu &          # only while testing; kill it at hand-back
```

and from Windows, to rebuild and redeploy after any change:

```shell
dotnet publish src/VisualCat.Desktop/VisualCat.Desktop.csproj -c Release -r osx-arm64 \
    --self-contained true -o artifacts/mac-build/desktop
# then tar | scp | untar into ~/vcat-run/candidates/desktop-main
```

---

## 6. The release this pass argues for

Pass 1's recommendation was to cut **2.0.14**, because two Major findings — the `logcat -v long`
data loss and the world-readable sessions — were already fixed on `main` and still shipping in
2.0.13. That recommendation is now much stronger, because `main` has gained a great deal more
than those two fixes:

* the product has a **macOS menu bar** and an about box, where 2.0.13 calls itself "Avalonia
  Application" and offers two menu items;
* it **starts, or explains itself, when the display is asleep**, where 2.0.13 aborts and files a
  crash report per attempt;
* `--adb` **means what it says**, where 2.0.13 silently runs a different tool and reports success;
* the archives **extract into a directory**, the README's verify line **works on macOS**, and its
  links point at the release rather than at `main`;
* a session records the **time zone the user actually chose**;
* `settings.json` and the diagnostics bundle are **owner-only**;
* the desktop and the CLI export **byte-identical CSVs** by default.

Two of those — the archive layout and the README — only reach users through a release, because
they are properties of the artifact rather than of the code.

---

## 7. The standing list — what pass 2 closed, and what still needs a person

Pass 1's [§4](MACOS-LIVE-TEST-REPORT.md#4-standing-list--what-is-still-untested) is the honest
inventory of what one pass did not reach. Everything in it that a machine can settle has now been
settled; the rows that genuinely need a human are named in [§7.3](#73-what-still-needs-a-person).

Four of these checks found defects. They are recorded here rather than in [§3](#3-what-this-pass-changes)
because they were found by *testing*, not by reading the report — and three of them were fixed.

### 7.1 Closed by this pass

| Row | Result |
|---|---|
| **I-15 cross-platform byte parity** | **Closed.** See [§8.2](#82-i-15--cross-platform-byte-parity-windows-against-macos) |
| **I-16 architecture parity, in full** | **Closed.** See [§8.1](#81-i-16--architecture-parity-in-full) |
| **B-21 off-timeline evidence** | **Closed — and it found two defects.** See [§8.3](#83-b-21--off-timeline-evidence-and-the-two-counts-that-disagreed) |
| **U-06 documented shortcuts** | **Closed — and it found three defects.** See [§7.2](#72-the-keyboard-matrix-and-the-three-defects-it-found) |
| **B-17 startup argument dispatch** | **Closed.** All six launch shapes start and hold a window: no arguments, `--log`, `--session`, a bare path, a missing path, a bad flag. `--log`, `--session` and a bare path each open the session (`Ready · 500 entries`, 7–9 s). A missing path starts the shell and says **`Startup source not found: /tmp/p3-does-not-exist.txt`**. Pass 1's numbers for this row were void because the measurement that produced them uncovered [F-23](MACOS-LIVE-TEST-REPORT.md#f-23) |
| **I-16 architecture parity** | **Closed, and it mattered** — this pass added two P/Invoke paths. The `osx-x64` build under Rosetta 2 gets the same seven-item menu bar, the same corrected application menu, the same `--adb` refusal, the same `Europe/Prague`, and the same F-23 answer (**exit 69, 7 lines, no crash report**). *Hide VisualCat* from the menu — the `objc_msgSend` path — hides the application and *Show All* brings it back |
| **A-08 adversarial corpus sweep** | **Closed.** Every file in pass 1's corpus indexes and verifies: `MixedCase.txt`, `café-nfc`/`café-nfd`, `log-🐱-cat.txt`, `log with space.txt`, `log:with:colon.txt`, `crashy.txt` (1 004 parsed, 1 unknown, 1 rejected), `mixed-formats.txt` (20 000), `medium.txt` (99 992 + 8 unknown), `large.txt` (999 885 + 115 unknown). A file literally named `--` is refused with a product sentence rather than parsed as an option. **Notably, `main` now accepts `outcomes.txt`, which 2.0.13 refuses outright** with *"No supported logcat format could be detected in this file"* — the detection collapse [F-06](MACOS-LIVE-TEST-REPORT.md#f-06) describes |
| **B-15 `.vcat.zip` round trip** | **Closed.** `vcat export … --type portable-zip` writes the archive **`0600`**, and the desktop's *Open archive* opens it as a second tab with all 1 000 entries and a fitted plot |
| **X-01 desktop leg** | **Closed — the first macOS desktop import baseline this project has.** 90 MB, 1 000 001 lines, imported in about **23 seconds**; the plot opens **exactly fitted** at `24.98 min · 564.27 ms/px`, and the footer reads `999,885 in view · 999,885 match the filter · 999,885 timed in session · 115 unparsed lines` — every number agreeing with the CLI's, and the 115 unparsed lines surfaced rather than hidden |
| **Mouse selection of a log row** | **Not reproducible as a defect, and not provable either.** Real `CGEvent` clicks were posted into the entry table with `cliclick`. In the state under test the selected-entry inspector had taken the table's height, so the clicks landed on the inspector rather than on a row — which is what the screenshot shows. Selection itself demonstrably works: F3 moved it through 78 matches and the inspector followed each one. What is still unproven is specifically *pointer* selection, and a person with a trackpad settles it in five seconds |

### 7.2 The keyboard matrix, and the three defects it found

Pass 1 could not tell a dead shortcut from a window with no keyboard focus, because its probe
answered `no focused element` throughout. With ⌘ shortcuts working ([F-03](MACOS-LIVE-TEST-REPORT.md#f-03))
the probe finally answers, and the matrix could be run properly.

| Shortcut | Result |
|---|---|
| ⌘F | focus moves to `AXTextField "Search message text or regex"` |
| ⌘0 / ⌘= / ⌘− | `780.001 ms` (already fitted) → `390 ms` → `702 ms` |
| ⌘, | opens *Appearance & timeline* |
| ⌘E | opens *Export CSV* |
| F3 / ⇧F3 | `– / 78` → `40 / 78` → `41 / 78` → `40 / 78` |
| ⌥1 | the heat map |
| ⌥2 | an entry row, with its full spoken description |
| ⌥3 | a template row |
| ⌥4 | the *Find tags* button, after bringing the Facets tab forward |

The last three only read that way **after** this pass fixed them. As found:

1. **⌥2, ⌥3 and ⌥4 were inert on every platform.** `ListBox.Focus()` answers `false` — in Avalonia
   a `ListBox` is not itself focusable, only its items are — so three of the four documented pane
   shortcuts did nothing, while ⌥1, which targets a custom control that *is* focusable, worked.
   Proved with the numpad variants (`⌥-numpad-1` moved focus, `⌥-numpad-2/3/4` did not), which
   rules out a macOS key-mapping quirk. Fixed: the shortcut focuses the list's **selected** row —
   so arrow keys and J/K continue from where the reader is — and falls back to the first row, then
   to the list itself for an empty one.
2. **⌥4 could never work from another tab.** With Templates, Views or Session showing, the facet
   pane has no visible control at all. It now selects the Facets tab first.
3. **An Alt+digit typed its alternate character into whatever text field had focus.** On macOS
   ⌥1–⌥4 are `¡ ™ £ ¢`, and marking a `KeyDown` handled does not suppress the `TextInput` that
   follows it — so pressing the documented pane shortcut while the search box had focus moved the
   focus *and* appended a currency sign to the query. Measured: after four shortcuts the search
   field read **`¢™jj`**. Fixed by discarding the one text-input event that follows a claimed
   Alt+digit, armed for a single event and a single dispatcher turn.

After the fix, pressing ⌘F and then ⌥1, ⌥2, ⌥3, ⌥4 in sequence leaves the search field **empty**
and the chip bar reading `No filters · showing everything in view`.

### 7.3 What still needs a person

These four cannot be driven from here. Each is one short sitting.

| Row | Why a person, and what it takes |
|---|---|
| **Q6 / Q7 — the consent pass** | A TCC prompt is drawn by a system process and deliberately cannot be clicked by synthetic events. iTerm2 is installed for exactly this. It needs: a first launch of the desktop from iTerm2, then *Open log* from `~/Desktop`, `~/Documents`, `~/Downloads`, an external volume and a network share — recording which prompt appears, **what application name it names** (a bare executable has no bundle name, so this is the open question), and what the product says when the prompt is **denied**. Pass 1 calls this "the most important macOS-only consequence of shipping an unbundled executable" |
| **[F-14](MACOS-LIVE-TEST-REPORT.md#f-14) reproduction, U-07, U-25** | Enabling VoiceOver takes over the Mac's speech and is disruptive on the owner's daily machine, so it was not switched on unasked. The product's half of F-14 is fixed and cannot be demonstrated by reproduction anyway — the crash was seen once in pass 1 and never reproduced in about fifteen attempts. What a VoiceOver soak would still add is **speech order, verbosity, and the cost of the 25-level nesting**, none of which the accessibility API can answer |
| **Physical pointer selection of a log row** | Synthetic `CGEvent` clicks operate every button in the product, and selection works through every other route. One trackpad click on a row settles the last doubt |
| **`sudo` rows and a second local account** | P-10 (`fs_usage` temporary-file boundary), X-16 (`memory_pressure`), X-15 (low disk), a dedicated case-sensitive APFS volume (M4), P-17 (multi-user isolation) and P-09 (the ACL boundary) all need an administrator password or a second account on the owner's Mac |

Unchanged from pass 1 and not reachable on this hardware at all: S2/S3/U-03/U-04 (one built-in
60 Hz display), S5 (`MacBookPro17,1` has no camera housing), U-15 (no Touch Bar), M9 (not an
MDM-managed Mac), and A-29/M2/D3 (an upgrade from a genuine 2.0.12 data set, which this account
never had).

### 7.4 One observation, not a finding

A desktop launched with an **unrecognised flag** — `./VisualCat --nonsense` — starts normally and
says nothing. Nothing is lost and the flag is skipped rather than misread as a path, which is
defensible; it is recorded because it is the "enabled and silently does nothing" shape this
codebase works to remove elsewhere, and because a reader who types `--sessions` for `--session`
gets a silent no-op. Left alone deliberately: it is outside what pass 1 found, and a launcher that
refuses to start over a stray flag would be worse.

---

## 8. The parity and evidence rows

Three more of pass 1's untested rows, all machine-checkable, all now closed. Two of them found
defects.

### 8.1 I-16 — architecture parity, in full

Pass 1 compared a counter. The plan asks for every artifact. One session, indexed once on
`osx-arm64`, then read by **both** binaries:

| Artifact | arm64 vs x64 |
|---|---|
| `export --type csv` | **identical**, 106 428 bytes |
| `export --type raw` | **identical**, 90 356 bytes |
| `export --type templates-md` | **identical**, 5 702 bytes |
| `export --type stats-csv` | **identical**, 271 bytes |
| `stats` JSON | **identical**, 5 367 bytes |
| `templates` JSON | **identical**, 14 653 bytes |
| `search` JSON | **identical**, 7 102 bytes |
| `query` NDJSON | **identical**, 26 607 bytes |
| `info` JSON | **identical**, 4 051 bytes |
| `verify` JSON | **identical**, 179 bytes |

Ten of ten. The plan's other I-16 assertion — "a session written by one opens in the other,
verifies there, and reports the same figures" — is the same run: the `osx-x64` binary read a
session the `osx-arm64` binary wrote, and its `info` and `verify` output match byte for byte.

**A method note worth keeping.** Indexing *separately* on each architecture makes three of the
ten differ — at `char 37, line 3`, which is the `sessionId` GUID and the snapshot generation.
Those are per-import identities, not architecture differences. A parity comparison that indexes
on both sides is measuring the wrong thing; index once, read twice.

### 8.2 I-15 — cross-platform byte parity, Windows against macOS

The same corpus (`small.txt`, SHA-256 `26472b46…`, identical on both hosts), the same options,
`--timezone UTC` pinned:

| Artifact | Windows vs macOS |
|---|---|
| `export --type csv` | **identical** `f63ee113…` |
| `export --type raw` | **identical** `22d1c305…` |
| `export --type templates-csv` | **identical** `538c536d…` |
| `export --type templates-md` | **identical** `e7d105f3…` |
| `export --type stats-csv` | **identical** `ae6455fc…` |
| `templates` JSON | **identical** `09333955…` |
| `stats` JSON | differs on **one line** — the `sessionId` GUID |
| `query` NDJSON | **identical once the session GUID is normalised** |

That is the strongest form of the assertion the plan asks for: no newline difference, no path
separator, no culture-formatted number, no case-folded identifier — the four things it names as
never permitted. The only difference anywhere is a per-import GUID.

**And the host time zone, which [F-07](MACOS-LIVE-TEST-REPORT.md#f-07) asked to be measured.**
Unpinned, on the same corpus:

```
Windows :  "timeZoneId": "Central Europe Standard Time"
macOS   :  "timeZoneId": "Europe/Prague"
```

Both name the same zone correctly — the macOS one only since this pass; it said
`Europe/Bratislava` before. The identifiers are shaped by the platform's own database, so
`timeZoneId` is a field an I-15 comparison must normalise, exactly as that finding's third
suggestion says. It is now measured rather than assumed.

### 8.3 B-21 — off-timeline evidence, and the two counts that disagreed

**The accounting is exact.** With the explicit `threadtime` override the plan specifies:

| | `outcomes.txt` | `crashy.txt` |
|---|---|---|
| source lines | 9 | 1 011 |
| parsed (timed + untimed) | 3 (2 + 1) | 1 004 (1 003 + 1) |
| meta records | 1 | 2 |
| continuations | 2 | 3 |
| rejected candidates | 1 | 1 |
| unknown lines | 1 | 1 |
| **accounted** | **8 + 1 blank = 9** ✓ | **1 011** ✓ |

`outcomes.txt` reproduces pass 1's §3.1 oracle exactly, including its 1 rejected candidate and
1 unknown line. (Left to auto-detection instead of the override it detects `LongFormat` at 0.255
confidence and files those two lines as message body — correct for that format, and the reason
the desktop shows an import review below 0.6.)

Opened five times on the same file, the card gave the same figures every time: no drift.

**Two defects, both fixed.**

1. **The card's own footer read `6 of 3 shown`.** Its header names four populations —
   `1 unknown · 3 continuation · 1 rejected · 1 untimed` — and its listing shows all six lines,
   with the three continuations carrying their `..` gutter code. The denominator left
   continuations out, so it was smaller than its own numerator and smaller than the header
   above it. B-21 fails a count that is invented, and a total below the number of rows it
   claims to total is invented. It now reads **`6 of 6 shown · every line off the timeline has
   been listed.`**
2. **The count line and the chip used the same words for different sets.** The workspace footer
   said **`1 unparsed lines`** — mis-pluralised, and counting unknown lines alone — beside a chip
   reading **`1 untimed record and 5 unparsed lines are not on the timeline`**, which counts
   unknown lines, rejected candidates and continuations. Two numbers under one phrase, on screen
   together. The footer now reads **`5 unparsed lines`**, from the same accessor the chip uses,
   through the shared pluralisation helper. This is the plan's R-22 row — *"the number equals the
   chip and the summary line"* — which could not have been checked before this pass, because
   B-21 had never been run.
