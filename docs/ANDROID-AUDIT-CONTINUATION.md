# VisualCat — audit continuation log (Pixel 5, API 34)

**Purpose.** A durable, continuously-written record of an independent audit of
[`ANDROID-LIVE-TEST-REPORT.md`](ANDROID-LIVE-TEST-REPORT.md): whether every issue
that report raises is *actually* addressed in the current tree, and the
implementation + physical-device verification of anything that is not.

This file is written as work happens, not at the end. If the session is
interrupted, resume from §0 (restore point) — it always names the next action.

---

## 0. Restore point

| Field | Value |
|---|---|
| Started (host) | 2026-08-29 |
| Repo | `E:\VisualCat`, branch `main`, HEAD `d959187` *Release VisualCat 2.0.10* |
| Working tree at start | staged deletion of `MOBILE-TIMELINE-SPLITTER-IMPLEMENTATION-PLAN.md`; untracked `.codex-artifacts/`, `PLAY-IN-APP-UPDATE-PLAN.md` |
| Device | Google Pixel 5 `redfin`, serial `0A031FDD400365`, Android 14 / API 34 |
| Confirmed by | `getprop ro.product.model` = `Pixel 5`, `ro.serialno` = `0A031FDD400365` (not by the `adb devices` listing alone) |
| **Next action** | §1 — static audit of the report's claims against the current tree |

**Progress**

| # | Step | Status |
|---|---|---|
| 1 | Static audit: every finding's remediation still present in the tree | In progress |
| 2 | Identify genuinely open items (deferrals, gaps, post-audit regressions) | Not started |
| 3 | Implement fixes | Not started |
| 4 | Host gates (build + tests) | Not started |
| 5 | Physical-device verification on the Pixel | Not started |
| 6 | Commit & push | Not started |

---

## 1. Static audit

Working.

### 1.1 Method

The report's own ledger (§5.1) marks every finding F-01…F-48 `Done`, and §§24–26
close the divider work. Taking that at face value would only re-read the report,
so this audit does three independent things instead:

1. **Re-run the gates the report claims pass**, on the current tree.
2. **Re-read the declared-open items** — §1.1 coverage gaps and §5.4 deliberate
   deferrals — and decide whether each rationale still holds.
3. **Test the configuration the report never tested.** §§24–26 verified both new
   dividers on a Samsung and a Motorola. Both are API 36 and, as the sections
   record, **three-button navigation**. The Pixel 5 here is API 34 and
   `settings get secure navigation_mode` = `2` — **gesture navigation**, the
   exact platform on which F-28 ("Back steals the plot's drag and leaves the
   app") was found. The dividers have never met it.

### 1.2 Host gates, current tree

| Gate | Result |
|---|---|
| `dotnet build VisualCat.slnx -c Debug` | **Pass** — 0 warnings, 0 errors |
| `dotnet test VisualCat.slnx` | see §1.4 |
| `tools/verify-docs.ps1` | **FAIL** — see A-01 |

### 1.3 Audit findings

#### A-01 · The documentation gate fails on `main` at `d959187`

`tools/verify-docs.ps1` — a release gate that asserts "every relative Markdown
link and image resolves on disk" — fails on the released tree:

```text
FAIL: docs/ANDROID-LIVE-TEST-REPORT.md: link target
      '../MOBILE-TIMELINE-SPLITTER-IMPLEMENTATION-PLAN.md' does not exist.
Documentation and metadata checks found 1 problem(s).
```

§23 opens by linking to the implementation plan it executes. The release commit
deleted the plan (the deletion is staged in the working tree) and left the link.
So the report — the artifact this audit is about — is itself the thing breaking
the repository's own documentation gate.

**Status: open.** Fix in §3.

#### A-02 · The phone divider is not protected from the Back gesture, so dragging it near either edge leaves the app

**Severity: Major.** **Device: Pixel 5, API 34, gesture navigation, Release 2.0.10-dev.**

§24.3 deliberately widened the divider's target to the whole boundary — "the
visible 20 dp gap … is grabbable across the whole line". §§24–26 verified that on
a Samsung and a Motorola. Both run **three-button navigation**, so neither could
see what the whole-line target costs on a phone that does not.

F-28 established the rule for this product: a control whose gesture is a drag must
name itself to `View.setSystemGestureExclusionRects`, or the platform takes the
drag at the screen edges. `EdgeGestureGuard.Track` is called for exactly two
controls — `SessionWorkspaceView.cs:345-346`, the timeline and the minimap. **The
divider was never added**, and it now reaches both edges.

Measured on the device:

| Quantity | Value |
|---|---|
| System gesture strips | `type=systemGestures frame=[0,0][82,2340]` and `[998,0][1080,2340]` — **82 px = 29.8 dp** each side |
| Divider node | `[33,1162][1047,1294]` — 368.7 × 48.0 dp |
| Divider inside the left strip | x 33…82 — **49 px / 17.8 dp** |
| Divider inside the right strip | x 998…1047 — **49 px / 17.8 dp** |
| Published exclusions | `SkRegion((0,733,1080,1096)(0,1110,1080,1198))` — timeline 363 px + minimap 88 px = **451 px / 164 dp** of the device's own 200 dp budget |
| Headroom | **99 px / 36 dp unused** |

Probes (`input swipe`, 400 ms, divider centre `y=1228`):

| Gesture | Result |
|---|---|
| Centre `x=540`, straight down +100 px | divider moved **+100 px** |
| Left edge `x=45`, straight up −100 px | divider moved **−100 px** |
| **Left edge `x=45`, +155 px right and +60 px down** | divider **gone**; focus `com.google.android.apps.nexuslauncher` — **the app was left** |
| **Right edge `x=1035`, −155 px left and +60 px down** | divider **gone**; focus `nexuslauncher` — **the app was left** |
| Control: the *same* drifting gesture at `x=45` inside the **plot** (which is excluded) | focus stayed on VisualCat |

The control is the point: the identical finger movement is safe over the plot and
throws the reader out of the app over the divider, and the only difference is the
exclusion rectangle. A finger that grabs a horizontal line and pulls it down does
not travel in a perfectly vertical line; a small horizontal component is the
normal case, not an adversarial one.

**Status: open.** Fix in §3.

#### A-03 · The in-app *Text scale* setting never reaches an open session workspace

**Severity: Major (accessibility).** **Device: Pixel 5, API 34, Release 2.0.10-dev.**

Two routes change the same number, `TextScale.Effective`, and only one of them
reaches the workspace:

| Route | Path | Rebuilds the workspace? |
|---|---|---|
| Android's own text-size control | `MainActivity.OnConfigurationChanged` → `PublishDisplayConfigurationChanged` → `MainView.ApplyDisplayConfigurationChange` (`MainView.cs:363`) | **Yes** — `RebuildWorkspaceViews()` + `RefreshOverlays()` |
| **More → Appearance & timeline → Text scale** | `ShowAppearanceAsync` (`MainView.cs:2555`) → `ApplyAppearance()` | **No** — `ApplyAppearance` re-states four shell labels and the empty state and stops |

`RebuildWorkspaceViews`'s own documentation states the rule the second route
breaks: *"A workspace resolves every font size in it while it is being
constructed, so a scale the reader has just changed reaches the screen by
building the view again **and no other way**."* Forty-three `TextScale.Of` call
sites live in `SessionWorkspaceView*`; none of them is re-resolved.

Measured on the device with a 50,156-entry session open, raising **Text scale**
from `1.00×` to `1.25×` and tapping **Apply**:

| Control | Before | After | |
|---|---:|---:|---|
| `Open log` (shell) | 102.2 dp | **121.1 dp** | grew |
| `Capture this device's log` (shell) | 71.6 dp | **82.9 dp** | grew |
| `More actions` (shell) | 70.5 dp | **81.5 dp** | grew |
| Session tab title (shell) | 105.1 dp | **125.8 dp** | grew |
| `Ready · 50,156 entries` (shell) | 16.7 dp | **20.7 dp** | grew |
| `Filters` (workspace) | 76.0 dp | 76.0 dp | **unchanged** |
| `Plot` / `Details` (workspace) | 70.9 dp | 70.9 dp | **unchanged** |
| `Entries` / `Insights` / `Entry` (workspace) | 117.1 dp | 117.1 dp | **unchanged** |
| `Time ↑`, `Copy raw` (workspace) | 126.2 / 95.3 dp | 126.2 / 95.3 dp | **unchanged** |
| `Load 500 more · 49,656 remaining` | 328.7 dp | 328.7 dp | **unchanged** |
| `No filters · showing everything in view` | 13.1 dp | 13.1 dp | **unchanged** |

Then, without touching the app again, Android's own font scale was set to 1.15:

| Workspace text block | 1.00× | after in-app 1.25× | after system 1.15× |
|---|---:|---:|---:|
| `No filters · showing everything in view` | 13.1 dp | **13.1 dp** | **18.5 dp** |

13.1 × 1.25 × 1.15 = 18.8. The workspace jumped to the *whole* effective scale at
the moment the platform changed — collecting, in one step, the 1.25× the reader
had asked for earlier and had been shown no answer to. The machinery works; the
in-app route simply does not call it.

What the reader sees: they raise the app's text size because they cannot read the
log comfortably, the chrome around the log grows, and **the log does not**. The
one surface the setting exists to serve is the one that ignores it, until some
unrelated configuration change happens to flush it.

**Status: open.** Fix in §3.

#### A-04 · The compact count line cuts a number in half instead of dropping a fact

**Severity: Minor.** **Device: Pixel 5, landscape (850.9 × 392.7 dp), Release 2.0.10-dev.**

In the compact composition the count line shares the analysis tab strip's row and
is given whatever the tabs leave. `UpdateSummaryText`
(`SessionWorkspaceView.Presentation.cs:613`) builds one compact string,
`"{inView:N0} view · {total:N0}"`, and relies on `TextTrimming.CharacterEllipsis`
for the rest. On this device the row leaves 80.0 dp and the line renders

```text
50,156 view · 5…
```

The accessibility name and tooltip keep the whole string, so this is a sighted
reader's defect only — but a half-drawn *number* is worse than a half-drawn word:
`5…` is itself a plausible count. §25.3 already settled the rule for this product
when the landscape divider exposed the same shape in the plot header — "the
header now drops a whole fact rather than part of one" — and
`TimelineControl.NarrowestHeaderThatFits` is the idiom. The count line was not
given one.

**Status: open.** Fix in §3.

---

## 2. Restore point — after the static audit

| Field | Value |
|---|---|
| Findings raised | A-01 (docs gate), A-02 (divider vs. Back gesture), A-03 (in-app text scale), A-04 (count line) |
| Host gates before any change | build clean; **595 tests pass** (Domain 47, Core 101, Application 56, App 391); `verify-docs.ps1` **fails** on A-01 |
| **Next action** | §3 — implement, then re-verify on the Pixel |

## 3. Remediation

### 3.1 A-01 — the documentation gate

§23's opening link pointed at a working plan the release commit deleted. The
reference is kept and the link dropped, because the plan is gone on purpose and
§§23–26 are now its record. `.codex-artifacts/` — the local area this audit
writes device dumps and screenshots into — is added to `.gitignore`, following
the report's own practice of keeping device UI hierarchies out of the repository.

`tools/verify-docs.ps1`: **99 relative links across 44 Markdown files, all
consistent.**

### 3.2 A-02 — the divider claims its grab band

`EdgeGestureGuard` gained one concept: a tracked control may claim **part** of
itself rather than all of it, and may say that its claim is small enough to be
granted before the larger ones.

```csharp
internal interface IEdgeGestureSurface
{
    Rect EdgeGestureArea { get; }   // in the control's own coordinates; empty claims nothing
    bool ClaimedWhole { get; }      // served before the surfaces that can be trimmed
}
```

`MobilePaneSplitter` implements it, and the two axes answer differently:

- **Rows** (the stacked divider) claims the **20 dp grab band**, centred on the
  boundary, across its full width. Not the 48 dp target: away from the centred
  grip — which is exactly where the edges are — the band is all `HitTest`
  answers, and the plot pays for anything wider out of the same 200 dp budget.
- **Columns** (the side-by-side divider) claims **nothing**. The allocator never
  resolves it closer than `MinimumReadableTimelineWidth` (220 dp) to one edge or
  `MinimumUsableAnalysisWidth` (300 dp) to the other, so it cannot reach a strip
  about 30 dp wide — and a full-width claim as tall as the pane would take Back
  away from the whole workspace to protect nothing. Verified on the device: a
  horizontal drag at the bottom of the landscape divider moved it −167 px against
  a requested −168 px and never left the app.

`EdgeGestureGuard.Measure` translates the claimed rectangle rather than the whole
control, and `Publish` treats a `ClaimedWhole` surface the way it already treated
the minimap. `SessionWorkspaceView` tracks the row splitter beside the plot and
the minimap.

### 3.3 A-03 — one place decides that the text scale moved

The "did `TextScale.Effective` change?" test moved out of
`ApplyDisplayConfigurationChange` and into `ApplyAppearance`, so **both** routes —
Android's configuration change and the reader's own **Text scale** control — pass
through it. `ApplyDisplayConfigurationChange` is now `ApplyAppearance()` plus the
mobile-chrome reflow it always did.

When the scale has moved, `ApplyAppearance` rebuilds the workspaces and refreshes
any open sheet, and returns: `CreateWorkspaceView` already applies the display
settings and restores both stored split shares, so the per-tab loop it replaces
would only repeat what the new views were built with.

### 3.4 A-04 — the count line drops a whole fact

`NarrowestSummaryThatFits` is `TimelineControl.NarrowestHeaderThatFits` applied to
the other line that has to live in whatever width is left:

```text
50,156 view · 50,156   →   50,156 view   →   50,156
```

The room is what the tab strip leaves, less the 6 dp lead the line keeps off the
last tab; `MoveSummaryIntoTabStrip` records it on every pass, including the passes
where nothing else moves — a rotation and a divider drag both arrive as one of
those. `TextTrimming.CharacterEllipsis` stays as the last resort, for a room too
narrow even for the bare number.

### 3.5 Host gates after the change

| Gate | Result |
|---|---|
| `tools/verify-docs.ps1` | **Pass** — 99 links across 44 files |
| `PixelGestureAndTextScaleTests` | **8 passed** |
| `TheReadersOwnTextScaleReachesTheOpenSession` against the **unfixed** `MainView` | **Fails** — *"'No filters · showing everything in view' stayed at 11 (from 11) while the reader asked for 1.5x"* |
| `LiveTestRemediationTests.ThePlotAndTheMinimapClaimTheirOwnEdgeGestures` | updated: the claim count is **3**, not 2 — the assertion encoded the policy A-02 corrects |

**Next action** — §4: rebuild the Release APK and re-run the device probes.

### 3.6 One test-hygiene defect found while gating

The first full run after the change failed one test, and the second run failed a
*different* one — the signature of leaked state rather than a regression. The
cause was in the new A-03 test, not in the product: `await using var view` at
method scope disposes **after** the enclosing `finally`, so the temporary
directory was deleted while a `MainView` still held an open session under it, and
the disposal that followed ran against a store that no longer existed. The
assembly already carries a warning about exactly this shape
(`HeadlessTestApplication.cs`: *"parallel classes can make one another write into
a deleted session root"*). The view is now disposed inside a body the teardown
wraps, and `ConfigureTemporarySessionRoot(null)` is restored. Recorded because a
green suite that is green by luck is worse than a red one.

`VisualCat.App.Tests`: **399 passed, 0 failed.**

---

## 4. Device re-verification, and what it turned up

Corrected Release build, clean-installed over the previous one:
`com.barebit.visualcat-Signed.apk`, SHA-256
`316d7e16420da905e4999d76dda4da3f6cbcc1aec24f58ef9a3b24ba1feb7e83`,
`versionName=2.0.10-dev`, `versionCode=2001000`, no `DEBUGGABLE` flag.

### 4.1 A-02 — closed

`dumpsys window` on the same 50,156-entry portrait session now reports **three**
rectangles where it reported two:

```text
mSystemGestureExclusion=SkRegion((0,733,1080,1106)(0,1120,1080,1208)(0,1210,1080,1266))
                                  ^ timeline 373 px   ^ minimap 88 px   ^ divider band 56 px
```

The divider node is `[33,1172][1047,1304]`, centre `y=1238`; the band spans
1210–1266, centred on 1238 — the same band `HitTest` answers. 373 + 88 + 56 =
517 px = **188 dp of the 200 dp budget**, so nothing was trimmed to pay for it.

| Gesture | Before | After |
|---|---|---|
| Centre `x=540`, +100 px | +100 px | **+100 px** |
| **Left edge `x=45`, +155 px right and +60 px down** | **app left for the launcher** | **−60 px, app kept** |
| **Right edge `x=1035`, −155 px left and +60 px down** | **app left for the launcher** | **+60 px, app kept** |
| Left edge `x=45`, 30 ms flick +80 px | — | **+81 px** |
| Right edge `x=1035`, 25 ms flick −90 px | — | **−90 px** |
| Left edge, held past the stop | — | held at the stop (−101 of −120) |
| Left edge, straight back off the stop +140 px | — | **+140 px** |

The last two are §24's own regression pair, re-run at the edge rather than at the
centre: a drag held against a stop still comes straight back off it.

The landscape column divider was probed before the change and needed none: a
horizontal drag at the bottom of its band moved it **−167 px against a requested
−168**, and never left the app. Its structural distance from both strips is why.

### 4.2 A-03 — closed

Same device, same session, **Text scale 1.25× → 1.55×** through
More → Appearance & timeline → Apply, with no other change:

| Control | 1.25× | 1.55× | |
|---|---:|---:|---|
| `No filters · showing everything in view` (**workspace**) | 16.7 dp | **20.0 dp** | grew — it did not before |
| `Open search and timeline filters` (workspace) | 76.0 dp | 82.5 dp | grew |
| `Open log` (shell) | 121.1 dp | 143.6 dp | grew |
| `Ready · 50,156 entries` (shell) | 20.7 dp | 25.5 dp | grew |

The screenshot at 1.55× shows the whole workspace at the new size: the mode
strip, the chip line, the analysis tabs, the count line, the entry rows and the
status line. `Details` correctly becomes `Logs` at that width, which is the
existing compact-label rule doing its job.

### 4.3 A-05 · The footer's load-more label is clipped mid-glyph at a large text scale

**Severity: Minor. Found by the A-03 fix**, because raising the text scale is
what puts the label under pressure — and until A-03 the workspace never answered
a raised text scale at all, so this state was unreachable from that control.

At 1.55× on this 393 dp phone the footer read:

```text
Load 500 more · 49,656 remainir
```

— cut inside the last word, with no ellipsis and nothing to say the sentence
continued. Under the list the control stretches across the analysis pane, so its
width is the pane's and a label that outgrows the pane has nowhere to go.

**Fixed** with the rule §25.3 established and A-04 reuses: give up the remaining
count whole, then fall back to the compact `+500` the header row already uses.
`NarrowestThatFits` is now one helper shared by the count line and this label, and
the room is re-resolved whenever the band changes width — safe from a layout loop
because in the footer the control stretches, so its width does not answer its own
content.


### 4.4 A-06 · A short viewport decides its pane composition with the command row's question

**Severity: Major. Device: Pixel 5, landscape, Release 2.0.10-dev, 1.55× text scale.**

With **Split** selected, the landscape workspace drew the plot, the minimap and
the status line — and **no analysis pane at all**. No tab strip, no `Entries`,
no rows. The mode button stayed selected the whole time.

The chain:

| Step | Value |
|---|---|
| Viewport | 850.9 × 392.7 dp |
| Gate | `stackedCompact = … && !MobileWorkspaceLayout.SharesARow(width)` |
| `SharesARow` threshold | `600 × TextScale.Effective` = **930 dp** at 1.55× |
| So | 850.9 < 930 → **stacked**, not side by side |
| Workspace band, stacked | 402→1008 px = **143 dp** |
| `ResolveStacked` fallback | plot floor 117 dp + minimap 26 dp = 143 → **analysis ceiling 0 dp** |

`SharesARow` answers a different question — whether two **command groups** fit on
one row — and it needs 600 dp where two **panes** need 532 (`220` readable plot +
`300` usable analysis + `12` divider lane). Both scale with the reader's text
size, so the two thresholds separate as soon as that is raised, and the wrong one
was being asked.

Proved on the device by moving the wrong lever: `wm size 1080x2600` gives the
same phone a **945.5 × 392.7 dp** landscape — just past the *command-row*
threshold — and the analysis pane and its tab strip came straight back
(`Entries` at 231.3 × 48.0 dp). The viewport was never too narrow for two panes;
the question was wrong.

**Fixed** by asking the panes' own question. `MobilePaneAllocator.FitsSideBySide`
states it once, from the two minimums the allocator already owns plus the
divider's lane, scaled by the reader's text size for the same reason the command
row is — both minimums are made of text. Both call sites now use it:
`ApplyMobileLayout`'s `stackedCompact` and `ConfigureWideMobileComposition`'s
`splitTimeline`.

At 1.55× the Pixel's landscape needs 824.6 dp and has 850.9, so it composes side
by side, where the plot gets its own column and the analysis pane gets 300 dp+ of
its own — instead of both being squeezed into a band that can seat neither.


### 4.5 A-07 · The load-more band is drawn through its own middle when the pane cannot seat it

**Severity: Minor. Exposed by the A-06 fix**, which is what put an analysis pane
back on a 136 dp band in the first place.

With the details column restored at 1.55×, the pane measured
`[925,556][2291,930]` — 496.7 × **136.0 dp** — and had to seat a 48 dp tab strip,
a 48 dp action row and a 48 dp load-more band: 144 dp. The entry panel's rows are
`Auto,*,Auto`, and Avalonia gives an `Auto` row its desired height even when the
grid has less to give, so the list got **0 dp** and the band was arranged
`[958,832][2258,983]` — 53 px past the pane's own bottom edge, clipped through the
middle of its own label, across the status line below it. Same overrun F-32 was
about.

**Fixed** by `EnforceLoadMoreFooterFit`: the band is dropped when the list cannot
hold a row beside it, and returns only when the list can hold a row **and** the
band — hysteresis, because hiding the band is itself what makes room for it, so
the two states cannot alternate between layout passes. A list whose last row is
partly visible is ordinary; a button drawn through its middle is not.

Verified on the device: the same viewport now shows the log row that band was
covering, and the button is gone rather than half-drawn.

---

## 5. Final state

### 5.1 Device verification, at the reader's ordinary settings

Text scale returned to `1.00×` through the same sheet, session still open.

| Check | Portrait (393 × 777 dp) | Landscape (850.9 × 392.7 dp) |
|---|---|---|
| Interactive nodes under 48 dp | **0 of 22** | **0 of 22** |
| Overlapping clickable pairs | **0** | **0** |
| Count line | `50,156 in view · 50,156 match · 50,156 in session` | **`50,156 view`** — whole, was `50,156 view · 5…` |
| Load-more | `Load 500 more · 49,656 remaining`, full band | `+500` in the header row |
| Composition | stacked, divider present | side by side, divider present |
| Gesture exclusions | `SkRegion((0,836,1080,1241)(0,1254,1080,1343)(0,1345,1080,1401))` — plot, minimap, **divider band** | — |

Divider probes on the shipping build, at 1.00×:

| Gesture | Requested | Moved |
|---|---:|---:|
| Centre `x=540`, +100 px | +100 | **+100** |
| Left edge `x=45`, 155 px right drift | −60 | **−59** |
| Right edge `x=1035`, 155 px left drift | +60 | **+60** |

### 5.2 What this audit did not do

Declared, not promoted to passes:

1. **One device.** Pixel 5, API 34, `arm64-v8a`, gesture navigation, 440 dpi. The
   A-02 fix is platform behaviour and needs no second device to be correct, but
   the exclusion budget's arithmetic was only re-measured here. §§24–26 remain
   the Samsung/Motorola evidence for the dividers themselves.
2. **Debug-key Release.** The artifacts are locally built Release, signed with the
   Android debug key. Functional and UX findings transfer; signing and Play
   delivery do not.
3. **The §1.1 coverage gaps of the original run are untouched.** No endurance
   pass, no assistive-technology session, no locale/RTL transition, no upgrade or
   Play matrix. Those remain declared gaps of this report, not of this audit.
4. **§5.4's deliberate deferrals were re-read and stand.** F-01's version-code
   formula, F-24's notice lane, F-27's content-hash identity and F-40's residue —
   a `DialogBody`'s own `TextScale.Of` sizes going stale while the sheet around it
   answers — are still the right calls. A-03 narrows F-40's residue rather than
   widening it: the workspace under the sheet now answers both routes, and the
   only thing left stale is a body the reader is mid-edit in, which is exactly
   what §5.4 says must not be rebuilt under them.
5. **Extreme text scales in landscape remain tight.** At 1.55× on this phone the
   analysis pane is 136 dp and shows its tabs, its action row and a partly
   visible log row — no full row. That is the viewport, not a defect: the same
   scale in portrait shows the whole workspace. Below about 1.3× nothing here is
   near a limit.


---

## Pass 28 — Samsung SM-G990B, API 36, at the owner's own display size

Continuation of this log. §27 above is the Pixel 5 gesture-navigation audit; this
pass re-audits the whole report on a Samsung at a **viewport no pass in the
report has used**, and carries its own restore points.

### 28.0 Run header

| Field | Value |
|---|---|
| Date | 2026-08-29 |
| Device | Samsung Galaxy S21 FE / `SM-G990B` (`r9q`), serial `RFCRC0A9GND` |
| Android | 16 / API 36, `arm64-v8a` |
| Navigation | `settings get secure navigation_mode` = **0** — three-button |
| Display | 1080 × 2340 px, physical density 480, **override density 360** (2.25 px/dp) |
| Viewport | **480 × 1040 dp portrait**, 1040 × 480 dp landscape |
| Repo commit at start | `1840623` — *Answer the phone workspace's own settings and the platform's gestures* |

**Why this viewport is new.** Every earlier Samsung pass (§§6, 8, 13, 16, 17, 18,
23, 24, 25) recorded *480 dpi, 3.0 px/dp* → **360 × 780 dp**. The owner has since
set a display-size override of 360 dpi, so the same phone is now **480 × 1040 dp**
— wider than the Pixel's 393 dp and the Motorola's 434 dp, and the first pass to
land between the two thresholds A-06 turns on:
`MobilePaneAllocation.FitsSideBySide` = 532 dp and
`MobileWorkspaceLayout.SharesARow` = 600 dp × text scale. Portrait sits *below*
both; landscape sits *above* both by a wide margin. That is exactly the pair of
levers §27.6 got wrong once.

### 28.1 Checkpoint — report analysed, host gates run

Read §§1–27 and the §5.1 status table. Findings:

| # | Issue | Evidence | Status |
|---|---|---|---|
| **I-01** | **A-01 was one commit from recurring.** §27's own working log — this file — was **staged for deletion** in the working tree while §27 links to it twice. `verify-docs.ps1` failed on that tree: `link target 'ANDROID-AUDIT-CONTINUATION.md' does not exist` × 2. The deletion was never committed, so `main` itself is clean and CI never saw it. | `git status` → `D docs/ANDROID-AUDIT-CONTINUATION.md`; gate output at 28.1 | **Fixed** — deletion reverted, file restored from `HEAD` intact (514 lines), gate now reads *102 relative links across 45 Markdown files, all consistent*. |
| **I-02** | **A-06 and A-07 are absent from §5.1.** §5 calls that table "the single source of truth for remediation status", and §§27.6–27.7 record two defects found and fixed that never reached it. A resumer following §5's own "How to resume" instruction would not learn they exist. | §5.1 vs §27.6/§27.7 | Open — see 28.2 |
| **I-03** | Every §27 fix is present in the tree and covered by tests. `EdgeGestureGuard`/`IEdgeGestureSurface`, `FitsSideBySide`, `EnforceLoadMoreFooterFit`, `NarrowestThatFits`/`NarrowestSummaryThatFits`, `ApplyAppearance` all resolve, with `PixelGestureAndTextScaleTests` guarding them. | `grep` sweep at 28.1 | **No defect** |

**Host gates on the restored tree, before any change of mine:**

| Gate | Result |
|---|---|
| `dotnet build VisualCat.slnx` | **0 warnings, 0 errors** |
| `VisualCat.Domain.Tests` / `Core` / `Application` / `App` | **47 / 101 / 56 / 409 — 613 passed, 0 failed** |
| `tools/verify-docs.ps1` | **Pass** — 102 links, 45 files (§27's 99/44 plus this restored file's own 3) |

**Next action:** add A-06/A-07 to §5.1 (I-02), then install the current-tree
Release build on the device and sweep this new viewport in both orientations.

### 28.2 Device sweep on the current tree — what held

Clean install of the current tree's **Release** APK (`2.0.10-dev`, code `2001000`,
`flags=0x0` — no `DEBUGGABLE`), after `adb uninstall`. Session opened by
`ACTION_VIEW` on a MediaStore `content://` URI: **199,990 entries**, 18 MB.

| State | Result |
|---|---|
| Home, portrait 480 dp | 0 of 6 clickable nodes under 48 dp, 0 overlapping pairs |
| Workspace, portrait 480 × 1040 dp | 0 of 13 under 48 dp, 0 overlaps; footer reads `Load 500 more · 199,490 remaining` whole, with F-42's spaced separator |
| Workspace, landscape 1040 × 480 dp | 0 of 11 under 48 dp, 0 overlaps; panes side by side; count line `199,990 view`; load-more in the header as `+500` |
| Landscape at Android font scale **1.55×** | Command groups share one row (1040 ≥ 600 × 1.55); tabs **119.1 dp** each, grown with the scale — **F-41 confirmed on a second device** |

**A-06 regression-tested at the viewport it lived in.** `wm size 1080x1575` at
1.55× gives a **700 × 480 dp** landscape — deliberately *between* the two
thresholds: `FitsSideBySide` (532 dp) says two columns, `SharesARow`
(600 × 1.55 = 930 dp) says one command row cannot fit. That is exactly the window
where §27.6's defect drew no analysis pane at all. The device now draws the pane
side by side: `TabControl` **322.2 × 306.7 dp**, list **298.2 × 152.0 dp** with
rows, load-more band **298.2 × 48.0 dp** *inside* the pane. **A-06 and A-07 both
hold at a viewport and a device §27 never used.**

At 1.55× the middle analysis tab renders `Insig…`. That is F-41's *fix*, not a
defect: the header is a `CharacterEllipsis` `TextBlock` and the dump confirms the
automation name is the whole word `Insights`. §5.2's F-41 record states exactly
this contract.

### 28.3 I-04 — the time-lens `Zoom in` button is 47.6 dp wide

**A genuine recurrence of F-48's defect class at a control F-48's fix never
reached.** Measured in the filter drawer, on the Release build, at 2.25 px/dp:

| Control | Bounds (px) | Width |
|---|---|---|
| `Zoom out` | `[61,1221][169,1329]` | 108 px = **48.00 dp** |
| `Zoom in` | `[183,1221][290,1329]` | 107 px = **47.56 dp** |
| every severity chip (F-48's fix) | 111 px | **49.33 dp** |

Reproduced in **both orientations** — landscape `[282,903][389,1011]` is the same
107 px. `Zoom in`'s logical left lands at 81.11 dp → 182.5 px and its right at
129.11 dp → 290.5 px; Android rounds the two edges **independently and inward**,
which is precisely the mechanism §20.13 wrote down for F-48. `Zoom out` starts on
a whole pixel and survives.

F-48's remedy was `TouchTarget.Minimum + 1` written inline at **one** call site —
the severity chip. Four controls in the product size *themselves* to exactly the
floor and were never given the reserve:

| Control | Site |
|---|---|
| `zoomOut`, `zoomIn` | `SessionWorkspaceView.cs` — `MinWidth = 48` |
| `panLeft`, `panRight` (source context) | `SessionWorkspaceView.RawContext.cs` — `Width = 48` |

**Next action:** give the reserve a name in `TouchTarget`, apply it at every site
where a control sizes itself to the floor, add a failing-first host test, then
re-measure on the device.

### 28.4 I-04 — the remediation

**Decision.** F-48 wrote its remedy as a literal `+ 1` at the one control that had
been measured. The mechanism it documented is arithmetic, not a property of that
control, so the remedy is given a name and applied to the whole family that shares
the shape.

`TouchTarget.MinimumWithEdgeReserve` (= `Minimum + 1`) and `TouchTarget.SelfSized`
now state the rule where the floor itself is stated, with the reason on them: a
control that resolves its **own** width can lose part of a physical pixel at each
independently-rounded edge, and one logical dp is under half a physical pixel at
every density the product ships on — enough to survive the rounding, too little to
see. A control stretched to a row or column takes that container's edges and
needs nothing.

| Site | Was | Now |
|---|---|---|
| `SessionWorkspaceView.cs` — `zoomOut`, `zoomIn` | `MinWidth = 48` | `MinWidth = TouchTarget.MinimumWithEdgeReserve` |
| `SessionWorkspaceView.RawContext.cs` — `panLeft`, `panRight` | `Width = 48` | `Width = TouchTarget.MinimumWithEdgeReserve` |
| `SessionWorkspaceView.cs` — severity chip | `TouchTarget.Minimum + 1` (F-48's literal) | the named constant |
| `MainView.TabStrip.cs` — session close target | `TouchTarget.For(mobile, 26)` | `TouchTarget.SelfSized(mobile, 26)` |
| `MainView.TabStrip.cs` — strip trailing margin | `Minimum + 10` | `MinimumWithEdgeReserve + 10` — the margin is documented as *one close target plus a gutter*, so it follows the target it reserves for |

Deliberately **not** changed: `copyRaw` and `openInspector` state
`MinWidth = TouchTarget.Minimum` but are `HorizontalAlignment.Stretch` inside `*`
columns, so they take the grid's edges; and the many `MinHeight = 48` rows, whose
height is imposed by the row and which measured exactly 48.00 dp on the device in
both orientations. Widening those would move the product's vertical rhythm to buy
nothing measurable.

**Host contract, failing first.**
`SamsungResponsiveLayoutTests.PhoneSelfSizedTouchTargetsAllReserveForPlatformEdgeRounding`
names the family rather than rediscovering it (no property distinguishes
"self-sized" from "stretched"), and asserts on whichever of `Width`/`MinWidth` the
control actually states. Against the unfixed source it fails with all four:

```
Error: Pan source left by one page reserves only 48 dp
Error: Pan source right by one page reserves only 48 dp
Error: Zoom out reserves only 48 dp
Error: Zoom in reserves only 48 dp
```

**Host gates after the fix:** build **0 warnings, 0 errors**; suite **614 passed,
0 failed** (47 / 101 / 56 / **410** — the 409 baseline plus this contract).
`CHANGELOG.md` `[Unreleased] → Fixed` carries the entry, in the file's voice and
without finding IDs.

**Next action:** install the rebuilt Release APK and re-measure `Zoom in` on the
device, then sweep the panes no pass has opened at this viewport — the settings
sheets, the source-context view and the Insights/Entry tabs.

### 28.5 Device verification of I-04, and one measurement that was not a defect

Rebuilt Release APK installed with `adb install -r` (`2.0.10-dev`, `2001000`).
Filter drawer, portrait, same session:

| Control | Before | After |
|---|---:|---:|
| `Zoom in` | **47.56 dp** | **49.33 dp** |
| `Zoom out` | 48.00 dp | 49.33 dp |
| every severity chip | 49.33 dp | 49.33 dp (unchanged) |

**0 of 26 clickable nodes under 48 dp**, 0 overlapping pairs. I-04 is
device-verified.

**A false positive worth recording.** The first landscape re-measure reported
`Zoom out` and `Zoom in` at **49.33 × 17.78 dp** and flagged both. They were not
clipped: with two session tabs open at 1.55× the drawer's body had scrolled them
below its viewport, and Avalonia's automation peer reports a node's *layout*
bounds, not its visible ones. Swiping the drawer body up moved them to
`[160,868][271,976]` — **49.33 × 48.00 dp**, whole and reachable. This is exactly
the trap recorded after the fourth pass: compare a small number against the
scroller's own bounds and re-measure after scrolling before believing it.

### 28.6 I-05 — a cold `ACTION_VIEW` of an already-open document opens a second tab

**New finding, in the area §1.1 gap 9 declares unexecuted** ("Exact-URI
redelivery through Android's Downloads provider passed in a *warm* activity.
**Cold delivery** … remain unexecuted"). Severity **Minor**. Not fixed — see the
decision below.

**Oracle.** One session open from `content://media/external/file/1000000573`:

| Delivery | Tabs after |
|---|---|
| Same URI again, activity **warm** | **1** — correctly reused |
| `am force-stop`, then the same URI, activity **cold** | **2** — duplicate |

**Mechanism.** `MainActivity._consumedUris` is a process-local `HashSet<string>`
guarding `MaterializeIncomingAsync`. It is the whole of the exact-URI contract
§5.4/3 relies on, and it is scoped to the process while the sessions it dedupes
against are persisted across processes. On a cold start the set is empty, so the
URI is materialized again — into a fresh `{timestamp}-{guid}-{name}` cache copy,
because the destination name is deliberately unique — and imported as a new
session beside the restored one. Nothing downstream can notice: the app layer
only ever sees the cache path, and `SessionDescriptor.SourceDescription` records
that per-copy path, so two copies of one document are two unrelated identities.

**Why this is recorded rather than fixed.** A correct fix needs the session to
remember the *document* it came from, not the copy: an origin carried on
`IncomingFile`, persisted in `SessionDescriptor` — a change to the documented
session format in [`SESSION-FORMAT.md`](SESSION-FORMAT.md) — and matched on
restore. It also needs a staleness policy the current code never has to state,
because it copies every time: a document that has *grown* since the tab was
opened should not be silently answered with the old copy, so reuse has to be
conditioned on the provider's size and last-modified. Neither question is settled
by this audit's evidence, and a partial fix — deduping without answering
staleness — would replace an explainable behaviour with a wrong one. This is the
same call §5.4/5 made about F-40's residue: not a change to make from inside a
remediation.

**What it would take**, so a later session does not re-derive it: add
`Origin` to `IncomingFile`; thread it through `MainView.OpenIncomingAsync` into
`WorkspaceViewModel.ImportFileCoreAsync`; persist it on `SessionDescriptor`
alongside `SourceDescription`; on an incoming file, select an open tab whose
origin matches instead of importing, unless the provider reports the document
changed. `MainActivity._consumedUris` then becomes redundant.

### 28.7 Completing the rule — the numeric spin buttons

Sweeping the settings sheets (the panes the fourth pass recorded as *never swept*,
both of which failed then) found no defect at this viewport: **More** is 10 of 10
controls at or above the floor, and **Appearance & timeline** is 20 of 20 once
scrolled. Four apparent failures there were the same scrolled-node artefact as
28.5 — `Increase/Decrease minimum bar width` read `48.00 × 10.2 dp` clipped at the
screen edge and `48.00 × 48.00 dp` after a scroll, and the CSV-encoding pair read
28 dp for the same reason.

The spin buttons themselves measured **exactly 48.00 dp (108 px)** on both axes —
no defect. But they are the same shape I-04 is about: `PrepareSpinButtons` gives
them `TouchTarget.Here()`, and a spin button resolves its own box from its glyph.
Leaving them on the bare floor would repeat exactly the criticism this pass makes
of F-48 — a rule applied only to the control that was measured last. `Prepare`
now takes `TouchTarget.SelfSizedHere()`, and
`AndroidAuditFix2Tests.NumericSpinnersReserveAboveTheTouchFloorOnTouch` holds it
there. The test's own remark says this is the rule being applied where the shape
is, not a defect being chased, so a later reader does not go looking for a device
measurement that never existed.

**Host gates:** build **0 warnings, 0 errors**; suite **615 passed, 0 failed**
(47 / 101 / 56 / **411**).

**Next action:** install the rebuilt Release and re-sweep both orientations and
both settings sheets, then restore the device and run the documentation gate.

### 28.8 Final device verification and hand-back

Rebuilt Release installed (`2.0.10-dev`, `2001000`). Every pane swept on the final
build, both orientations:

| Pane | Clickable nodes | Under 48 dp | Overlapping pairs |
|---|---:|---:|---:|
| Workspace, portrait 480 × 1040 dp | 15 | **0** | **0** |
| Workspace, landscape 1040 × 480 dp | 15 | **0** | **0** |
| Filter drawer, portrait | 26 | **0** | **0** |
| Filter drawer, landscape (after scrolling to the time lens) | 26 | **0** | **0** |
| **More** sheet | 10 | **0** | — |
| **Session cache** sheet | 8 | **0** | — |
| **Appearance & timeline** sheet (scrolled) | 17 on-screen | **0** | — |

Measured deltas: `Zoom in` **47.56 → 49.33 dp**; `Zoom out` 48.00 → 49.33; every
numeric spin button **48.00 × 48.00 → 49.33 × 49.33 dp**; session close targets
49.33 and 49.78 dp; severity chips unchanged at 49.33.

**Hand-back.** Device restored to the baseline recorded in 28.0:
`font_scale 1.0`, `wm user-rotation free`, `wm size reset`, and — after a
`wm density reset` briefly cleared it — the owner's **override density 360**
put back. The 18 MB test corpus is removed from `/sdcard/Download` and its
MediaStore row deleted. The app is left installed at `2.0.10-dev` with the
sessions it holds; `.codex-artifacts/` is git-ignored, so no UI dump or
screenshot enters the repository.

**Status: this pass is complete.** I-01, I-02 and I-04 are fixed and recorded;
I-04 is device-verified; I-05 is reproduced, deferred and specified. §28 of the
report is the durable record; this log is its working detail.
