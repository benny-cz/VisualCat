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
defect that was already closed. Filled in as each row is checked.

| # | Sev | Pass-1 state (2.0.13) | On `main` before this pass | Action |
|---|---|---|---|---|
| F-01 | Minor | `sha256sum` in macOS README | — | — |
| F-02 | Minor | no wrapper directory | — | — |
| F-03 | Major | "Avalonia Application", no menus | — | — |
| F-04 | Minor | data root 0755 | — | — |
| F-05 | Minor | no macOS floor published | — | — |
| F-06 | Major | `-v long` data loss | — | — |
| F-07 | Minor | `Europe/Bratislava` | — | — |
| F-08 | Major | sessions world-readable | — | — |
| F-09 | Major | click queued past a modal | — | — |
| F-10 | Major | `--adb` silently ignored | — | — |
| F-11 | Minor | macOS SDK path never probed | — | — |
| F-12 | Polish | three misleading ADB messages | — | — |
| F-13 | Polish | session saved inside a session | — | — |
| F-14 | Major | abort in `raiseLiveRegionChanged` | — | — |
| F-15 | Minor | two half-empty dialogs | — | — |
| F-16 | Polish | unnamed search field, legend overlap | — | — |
| F-17 | Minor | workspace never restored | — | — |
| F-18 | Major | follow strands the newest records | — | — |
| F-19 | Minor | README links `main` | — | — |
| F-20 | Minor | CSV order defaults differ | — | — |
| F-21 | Polish | three UI frictions | — | — |
| F-22 | Minor | full-screen toolbar under title bar | — | — |
| F-23 | Major | will not start with the display asleep | — | — |
