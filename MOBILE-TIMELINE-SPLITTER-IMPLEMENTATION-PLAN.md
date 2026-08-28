# Mobile timeline/details splitter implementation plan

## Document purpose

This document is a standalone implementation plan for adding a user-adjustable divider between VisualCat's timeline/minimap area and the mobile analysis area containing the **Entries**, **Insights**, and **Entry** tabs.

It records the current implementation, physical-device evidence, the reason a direct `GridSplitter` insertion is insufficient, the recommended architecture, persistence and accessibility requirements, test coverage, rollout risks, and acceptance criteria. It intentionally does not implement the feature.

### Verification status

Every code claim in this document has been checked against the tree at commit `1ca869b`. The measurements, the geometry arithmetic, the row definitions, the size-class weights, the Avalonia version, and the four referenced test files are all confirmed accurate.

Five corrections were applied after that check, and they are the parts most worth reading:

1. **[Edge-gesture budget](#edge-gesture-budget)** — the feature's own success breaks the back-gesture guard. Android honours 200 dp of exclusion per edge; a 214 dp timeline plus the minimap needs 262 dp. Previously unrecorded.
2. **[Timeline hard minimum](#timeline-hard-minimum)** — the earlier recommendation to give `TimelineControl` a minimum on a star-sized row would have reintroduced finding 18, which `_timeline.MinHeight = 0` exists to prevent.
3. **[Why not a nested pane grid](#why-not-a-nested-pane-grid)** — the nested-host architecture solves a problem the required allocator already removes, while pulling in the root column model and both overlay row spans. The recommendation is now to keep the root grid.
4. **[Phase 0](#phase-0-ship-the-accounting-fix-on-its-own)** — the accounting fix delivers most of the value on its own and is now sequenced first instead of buried inside the splitter work.
5. **[Existing timeline interaction](#existing-timeline-interaction)** — the claim that render-width updates are wired to width changes was wrong; they are wired to `SizeChanged`, and a 24-pixel dead band in the view model is what actually prevents a query storm.

Two smaller corrections: `CompactEntryRowFloor` is unreachable dead code rather than live behaviour to preserve, and the observed device geometry was produced by the plot ceiling rather than the entries floor.

## Executive conclusion

The feature is feasible with moderate UI-layout work and no changes to log ingestion, query execution, session storage, or timeline data generation.

VisualCat already uses Avalonia `GridSplitter` controls in the desktop workspace. The mobile workspace omits the horizontal splitter and assigns its corresponding root row a height of zero. However, enabling that row and dropping in the desktop splitter would not produce a reliable mobile feature:

- A stock `GridSplitter` does its own arithmetic on the two row definitions it sits between. The minimap owns a row of its own between the timeline and the analysis pane, so the splitter's "previous" row is the minimap, not the aggregate plot area the reader is actually moving.
- `ApplyMobileLayout` rewrites the relevant row definitions whenever the mobile composition changes, so any length the splitter wrote directly is overwritten on the next recomposition.
- `EnforceEntriesFloor` currently gives the analysis row a dynamic minimum height equivalent to four entry rows in ordinary Split mode. On the inspected phone, the analysis pane is already at approximately that minimum, so a splitter constrained by the existing minimum would have little or no useful upward travel.
- A thin desktop-style five-pixel splitter is not a viable phone touch target.
- The splitter must disappear or change behavior in Plot-only, Details-only, filter-drawer, failure, and side-by-side compact-height compositions.

The recommended implementation keeps the existing root grid and replaces the splitter's arithmetic rather than the layout tree:

1. Add a pure allocator that owns every mobile height decision for rows 2-5, so there is exactly one writer.
2. Drive it from a `Thumb`-based divider in the existing empty root row 4, which reports drag deltas instead of computing row lengths. Row 4 already sits exactly on the plot/analysis boundary in every mobile composition.
3. Express the aggregate plot pane as *the band minus the analysis pane*: the analysis row takes the resolved pixel height and the timeline row stays star-sized, so the timeline absorbs all residual and the minimap keeps its fixed size-class band.
4. Keep the current automatic entries-row floor as a preferred default allocation, but use smaller hard minima after the user explicitly resizes the panes.
5. Store the user's choice as a normalized plot-pane share, not an absolute height.
6. Preserve the stored share while temporary layouts clamp or ignore it.
7. Expose a visibly thin divider with a minimum 48 dp touch target and verify its actual Android accessibility bounds on a device.
8. Keep the timeline's own minimum inside the allocator, never as a `MinHeight` on the control or on a star-sized row.

Step 3 is what makes this a small change: `ApplyMobileLayout` already writes exactly these row heights, so the allocator replaces the arithmetic in a method that exists rather than reparenting the workspace. Nothing about the root grid's shape, its column model, or the drawer and failure overlays' row spans changes.

The single highest-value change in this document is not the divider at all. It is the one-line accounting fix in [Analysis floor](#analysis-floor), which raises the inspected device's timeline from about 85 dp to about 132 dp on its own. That ships as Phase 0, before any new control exists.

## Scope

### In scope

- A vertical drag divider in stacked mobile Split mode.
- Resizing the aggregate plot pane, meaning the timeline plus its minimap, against the analysis pane.
- Touch, mouse, stylus, and keyboard operation through Avalonia pointer/`Thumb` behavior.
- Dynamic minimum and maximum limits based on the current viewport and measured analysis chrome.
- Restoring the user's chosen share after rotation, activity recreation, app restart, text-scale rebuilds, and temporary mode changes.
- A reset-to-automatic action.
- Android physical-device verification, including the currently connected Samsung configuration.
- Desktop regression protection.

### Out of scope

- Resizing the Entries/Insights/Entry tabs relative to one another.
- Changing the horizontal timeline viewport, heat-map query resolution, timeline lane count, or pinch algorithm.
- Adding a width splitter to the side-by-side landscape composition. That is a reasonable follow-up but is a different interaction and persistence axis. **(Since implemented — see [`docs/ANDROID-LIVE-TEST-REPORT.md`](docs/ANDROID-LIVE-TEST-REPORT.md) section 25. It is a separate axis exactly as this line says: its own divider, its own limits, and its own stored share.)**
- Per-session splitter positions. The recommendation is one reader preference shared by mobile workspaces, matching the existing Plot/Split/Details preference.
- Replacing the existing Plot, Split, and Details workspace modes.
- Redesigning the filter drawer, status line, command strip, minimap gestures, or desktop pane layout.

## Current physical-device evidence

The implementation should be evaluated against the real configuration that exposed the issue, not only a headless nominal viewport.

### Inspected device

| Property | Observed value |
|---|---|
| Device | Samsung SM-G990B |
| Android | 16 / API 36 |
| App | VisualCat 2.0.9, version code 2000900 |
| Physical display | 1080 x 2340 px |
| Physical density | 480 dpi |
| Active density override | 360 dpi |
| Effective logical display | 480 x 1040 dp |
| Font scale | 1.0 |
| Orientation | Portrait |
| App state | Split mode, active wireless logcat capture |

The density override matters to the QA matrix: the feature must be checked at both the device's native density and the currently active override because hit testing and measured bounds are part of the requirement.

### Measured Split-mode geometry

A live screenshot and a same-session Android accessibility snapshot showed approximately the following bounds:

| Surface | Physical bounds/size | Logical size at 360 dpi |
|---|---:|---:|
| Timeline heat map | 1026 x 191 px | 456 x 84.9 dp |
| Minimap control | 824 x 86 px | 366.2 x 38.2 dp |
| Analysis tab pane | 990 x 948 px | 440 x 421.3 dp |
| Filtered entries list | 936 x 558 px | 416 x 248 dp |

The entries list is therefore almost exactly four 64 dp rows.

It is worth being precise about *which* clamp produced that, because the two candidates are about seven dp apart and only one of them is the defect:

```text
chrome   = 421.3 - 248            ~= 173.3 dp
wanted   = 173.3 + (4 * 64)       ~= 429.3 dp   (the entries floor)
band     ~= 84.9 + 48 + 421.3     ~= 554.2 dp
ceiling  = band - 132             ~= 422.2 dp   (the plot safeguard)
applied  = min(wanted, ceiling)   ~= 422.2 dp
```

The `ceiling` won. The analysis pane is pinned at `band - 132`, which is the same as saying the timeline *plus its minimap* were given exactly 132 dp — 84.9 for the plot and 48 for the minimap, matching the measured bounds to within rounding. So the observed geometry is a direct readout of the accounting defect described in [Analysis floor](#analysis-floor), not of the four-row preference. The four-row floor was about to bind anyway, which is why both numbers appear in the measurement; correcting the ceiling alone is what moves this device.

### Why the timeline is difficult to use at that height

`TimelineControl.Geometry()` divides its own height into a header, axis, and lane band:

```text
header = min(36 dp, control height * 0.22)
axis   = min(34 dp, control height * 0.26)
lanes  = control height - header - axis
```

At the observed 84.9 dp timeline height:

```text
header ~= 18.7 dp
axis   ~= 22.1 dp
lanes  ~= 44.1 dp
six severity lanes ~= 7.4 dp each
```

The control still renders because its total lane band clears the 36 dp internal minimum, but each individual severity lane is only about 7 dp high. This explains the difficulty selecting a heat-map cell. The whole pinch surface is also only about 85 dp high, making a two-finger gesture unnecessarily awkward.

The timeline's own preferred measurement is 214 dp: 36 dp header + 34 dp axis + 144 dp for the lanes. This is not a requirement that every Split layout can satisfy, but it is a useful target for manual enlargement.

## Current code architecture

### Root layout

`SessionWorkspaceView.Build()` in `src/VisualCat.App/Views/SessionWorkspaceView.cs` creates a seven-row root grid.

Current mobile row meanings are:

| Root row | Current mobile role | Typical sizing |
|---:|---|---|
| 0 | Filter/workspace command shell | Auto |
| 1 | Active filter chips/search status | Auto |
| 2 | Timeline | Star |
| 3 | Minimap | Fixed pixels or zero |
| 4 | Reserved desktop splitter row | Zero on mobile |
| 5 | Analysis pane | Star with a dynamic minimum |
| 6 | Status line | Auto |

Desktop uses a five-pixel `GridSplitter` in row 4. Mobile does not create that splitter and initializes row 4 to zero.

### Mobile size classes and display modes

`src/VisualCat.App/Views/MobileWorkspaceLayout.cs` defines:

- `MobileWorkspaceMode.TallPortrait`
- `MobileWorkspaceMode.CompactPortrait`
- `MobileWorkspaceMode.CompactHeight`
- `MobileWorkspaceDisplayMode.Plot`
- `MobileWorkspaceDisplayMode.Split`
- `MobileWorkspaceDisplayMode.Details`

The current automatic timeline/analysis weights are:

| Size class | Timeline weight | Analysis weight | Minimap height |
|---|---:|---:|---:|
| Tall portrait | 2.0 | 3.0 | 48 dp |
| Compact portrait | 1.7 | 3.3 | 42 dp |
| Compact height | 2.1 | 2.9 | 26 dp |

The weights do not determine the observed final split by themselves because the analysis row's dynamic minimum wins when its tab/header/footer chrome plus the preferred entry rows require more room.

### Mobile recomposition

`ApplyMobileLayout()` in `SessionWorkspaceView.Mobile.cs`:

- Selects a size class from the settled viewport.
- Keeps the current Plot/Split/Details choice across rotation.
- Hides the plot while the filter drawer is open.
- Sets root rows 2, 3, and 5 for the active composition.
- Moves the plot and analysis side by side in sufficiently wide compact-height layouts.
- Shows or hides the minimap based on overview availability.
- Calls `EnforceEntriesFloor()` after composing the rows.

Any splitter state that directly mutates root grid lengths without participating in this method will be overwritten by later mobile recomposition.

### Analysis floor

`EnforceEntriesFloor()` measures the difference between the analysis pane height and the entries list height to derive the pane's current chrome. It then reserves:

- Four entry rows in ordinary Split mode.
- Six entry rows in Details mode.
- Three entry rows in compact-height mode.

The minimum is placed on root row 5. The method tries to leave `MinimumReadablePlotHeight`, currently 132 dp, for the plot.

There is an accounting defect in that safeguard. The band it subtracts from spans rows 2-5, so the residual 132 dp covers the timeline, the minimap row, *and* the splitter lane together:

```csharp
var band = Bounds.Height - _root.RowDefinitions[0].ActualHeight
           - _root.RowDefinitions[1].ActualHeight
           - _root.RowDefinitions[6].ActualHeight;   // rows 2,3,4,5
var ceiling = band > 0 ? Math.Max(0, band - MinimumReadablePlotHeight) : wanted;
```

On the inspected tall-portrait layout, 48 dp of that goes to the minimap row, leaving about 85 dp for `TimelineControl`. The observed device geometry matches this arithmetic exactly.

Two further facts about the current code constrain the design and are easy to miss:

**`CompactEntryRowFloor` is unreachable.** The guard clause returns `SetEntriesFloor(0)` when `_mobileLayoutMode == MobileWorkspaceMode.CompactHeight`, so the later `_mobileLayoutMode == CompactHeight ? CompactEntryRowFloor : ...` branch can never be taken. Compact height has **no** entries floor today, by design — the wide composition makes the analysis pane a column spanning every row, where a row minimum would grow the grid rather than take from a neighbour and would push the status line off the bottom. Any plan that claims to "preserve three rows in compact height" is preserving behaviour that does not exist.

**The timeline yields entirely below about 69 dp.** `TimelineControl.Geometry()` returns `null` when the lane band falls under `MinimumLaneBandHeight` (36 dp), and `Render` then draws *"Not enough height to draw the plot."* instead of a heat map. Below the 130.8 dp point where the axis band stops growing, `lanes = height * 0.52`, so the cliff is at `36 / 0.52 ~= 69.2 dp`. That is the hard floor below which the control shows nothing; 132 dp is a *readability* target well above it. Both numbers matter — one is a correctness bound for the allocator's clamps, the other is a quality bound for its preferred allocation.

### Existing state persistence

`ApplicationSettings.WorkspaceDisplayMode` stores the user's Plot/Split/Details choice. `MainView.Workspace.cs` coalesces updates and persists only the newest workspace snapshot. `SessionWorkspaceView` restores the value when a workspace is created or rebuilt.

The splitter preference should follow this established path rather than introduce a second settings writer.

### Existing timeline interaction

`TimelineControl` already supports:

- Pointer drag to pan.
- Pinch to zoom.
- Double tap to zoom.
- Cell selection.
- Keyboard navigation on desktop.

Changing only the control's height does not change heat-map query width — but not for the reason it first appears, and the difference is load-bearing.

The render-width update is wired to `SizeChanged`, which Avalonia raises when *either* dimension changes:

```csharp
_timeline.SizeChanged += (_, eventArgs) =>
{
    var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
    _ = _viewModel.SetRenderWidthAsync(
        Math.Max(64, (int)Math.Round((eventArgs.NewSize.Width - 88) * scale)));
};
```

So a purely vertical drag *does* call `SetRenderWidthAsync` on every layout pass. What prevents a heat-map query storm is a dead band inside the view model, not the wiring:

```csharp
var width = Math.Clamp(devicePixelWidth, 64, 4096);
if (Math.Abs(width - _renderWidth) < 24)
{
    return Task.CompletedTask;
}
```

The conclusion in the original assessment holds — vertical resizing will not re-query columns — but it rests on `SetRenderWidthAsync`'s 24-device-pixel guard. That guard is therefore an invariant this feature depends on, and it should be pinned by a test rather than left as an incidental optimisation someone could later remove while "cleaning up" the view model.

### Android edge-gesture exclusions

`EdgeGestureGuard.Track` is called for exactly two controls, `_timeline` and `_minimap`. It publishes their window rectangles through `PlatformSourceRegistry.SetGestureExclusions`, and `MainActivity.ApplyGestureExclusions` hands them to `View.SystemGestureExclusionRects`.

This is directly relevant to a feature whose stated goal is a taller timeline, because the platform imposes a budget:

> Android honours at most 200 dp of exclusion height per edge and silently keeps the lowest rectangles past that, so the shared side is deliberate about what it sends: the plot and the minimap, and nothing else on the screen.

Today the plot and minimap total roughly 133-145 dp, comfortably inside the budget. At the 214 dp timeline this document sets as its target, the pair totals `214 + 48 = 262 dp` and **exceeds it**. Android then drops the overflow from the top, which is precisely the part of the enlarged timeline the reader has just created. See [Edge-gesture budget](#edge-gesture-budget) for the required mitigation.

## Feasibility assessment

### What can be reused

- Avalonia 12.1.0's `GridSplitter` supports row resizing, drag increments, keyboard movement, preview mode, and row min/max constraints.
- VisualCat already uses `GridSplitter` on desktop, so no new dependency is required.
- Existing mobile state and settings persistence patterns can carry the new preference.
- Existing headless phone fixtures can measure actual pane and touch-target bounds.
- Existing Android gesture-exclusion infrastructure keeps tracking the timeline and minimap unchanged, because neither control is reparented. Its 200 dp budget still needs the work described in [Edge-gesture budget](#edge-gesture-budget).

### Why a stock `GridSplitter` in row 4 is not enough

The problem is the control's arithmetic, not the row's position. Row 4 is geometrically correct: it sits between the minimap row and the analysis row, which is exactly the plot/analysis boundary in every stacked mobile composition, and it stays correct when the minimap collapses to zero because the boundary simply moves up with it.

What fails is letting `GridSplitter` compute the result. Avalonia's splitter resizes the two definitions adjacent to it according to `ResizeBehavior`; it does not understand that the timeline and minimap form one conceptual pane across two rows. It would shrink the minimap rather than the plot. It would also fight `ApplyMobileLayout`, which rewrites those same lengths on every recomposition, and its star/pixel conversion would be undone on the next pass.

The fix is to keep the row and replace the control's arithmetic: a divider that reports drag deltas, and one allocator that owns every length. Adjacency stops being a constraint once the splitter is no longer doing the maths.

### Why not a nested pane grid

Grouping the timeline and minimap into a nested plot pane is the obvious way to make a stock splitter's adjacency correct, and it is worth stating explicitly why this document does not recommend it.

Once a pure allocator exists — which every version of this design requires — adjacency no longer needs solving, so the nested grid buys nothing and costs a great deal:

- **The root grid's column model would have to move with it.** `ConfigureWideMobileComposition` implements the side-by-side layout at the *root* level: it sets `_root.ColumnDefinitions` to `"21*,29*"` and then manages `Grid.SetColumnSpan` on the command shell, the filter drawer, the chip bar, and the status bar so they continue to span both columns. Moving the panes into a nested host makes all four of those spans wrong and the root columns redundant.
- **Both overlays span the rows being replaced.** The filter drawer is `Grid.SetRow(drawer, 2)` with `Grid.SetRowSpan(drawer, 4)`, and the failure card is row 0 spanning 6. Collapsing rows 2-5 into one host row changes both, and the failure card's span is what guarantees it covers the workspace band.
- **It reparents four controls** whose gesture, exclusion-tracking, and clipping behaviour is already tuned, for no behavioural gain.
- **It puts the timeline in a star row with a hard minimum**, which is the exact shape of a fixed regression. See [Timeline hard minimum](#timeline-hard-minimum).

Keeping the root grid means the drawer span, the failure span, the column model, the exclusion tracking, and the desktop build are all untouched, and the diff is concentrated in the one method that already owns these row heights.

### Complexity estimate

The change is moderate rather than trivial because it crosses layout composition, state, settings, accessibility, and regression tests. The core resizing behavior itself is small. Most work is in preserving all existing mobile modes and defining correct dynamic constraints.

Phase 0 alone — the accounting fix — is a few lines and is independently shippable.

No data migration is required if the new settings value is nullable and additive.

## Recommended product behavior

### When the divider is present

Show and enable the divider only when all of the following are true:

- The app is using the mobile composition.
- The workspace mode is Split.
- The timeline and analysis panes are both visible.
- The panes are stacked vertically.
- The filter drawer is closed.
- The failure/empty-session takeover is not active.
- The available band can satisfy both panes' hard minimums with some nonzero travel.

### When the divider is absent

- Plot mode: timeline/minimap receive the complete workspace band.
- Details mode: analysis receives the complete workspace band.
- Wide compact-height Split mode: plot and analysis remain side by side; retain but do not apply the stored height share.
- Filter drawer: divider is hidden and removed from the accessibility tree.
- Failure state: divider is hidden and removed from the accessibility tree.
- Insufficient stacked height: hide or disable the divider rather than presenting a control that cannot move. Plot and Details remain the escape hatches.

### Drag semantics

- Drag upward: enlarge the analysis pane and reduce the aggregate plot pane.
- Drag downward: enlarge the aggregate plot pane and reduce the analysis pane.
- The minimap remains attached to the plot pane and keeps its fixed size for the current mobile size class.
- The timeline receives all plot-pane height not consumed by the visible minimap and its frame margins.
- Resizing is live unless device testing proves continuous layout too expensive. Preview-only resizing is a fallback, not the initial recommendation.

### Reset semantics

Provide an explicit route back to automatic sizing:

- Double tap the divider handle to reset to automatic sizing.
- Support a keyboard reset key such as Home when the divider is focused.
- Include the reset instruction in help text and in the tooltip.
- Ship the **Appearance & timeline** sheet action from the start, not as a contingency.

The last point is a change of emphasis. A double tap on a 12 dp lane is undiscoverable: nothing on screen advertises it, and a reader who has dragged the divider somewhere unhelpful has no visible way back. Treating the sheet entry as a fallback that appears only if the gesture proves unreliable leaves the common case — a reader who does not know the gesture exists — with no route at all. The sheet already hosts the related appearance preferences, so a *Reset plot and details split* action belongs there regardless of how well the gesture works, and the gesture becomes the shortcut rather than the mechanism.

Disable or hide the sheet action when no override is stored, so it also serves as the indicator that the layout is currently manual.

Reset stores `null`, not the current default ratio. This allows future changes to automatic weights and size-class behavior to take effect.

## Recommended architecture

### 1. Keep the root grid; make the analysis row the one moving part

No reparenting. The root grid keeps its seven rows and its current children. The mobile row meanings are unchanged except that row 4 stops being dead:

| Root row | Mobile role | Sizing under this design |
|---:|---|---|
| 2 | Timeline | **Star, always.** Absorbs whatever the band has left. |
| 3 | Minimap | Fixed size-class height, or zero when there is no overview. |
| 4 | Divider lane | Fixed lane height when interactive, otherwise zero. |
| 5 | Analysis | Star in automatic mode; **resolved pixel height** once the reader has overridden it. |

The reader is moving one boundary, so exactly one length needs to encode their choice. Making that length the analysis row's height gives the design several properties for free:

- **The plot pane needs no representation at all.** It is whatever remains: `timeline = band - minimap - lane - analysis`. Because row 2 is star-sized, the grid computes that subtraction, so the aggregate plot pane cannot drift out of sync with the sum of its parts.
- **The timeline never needs a `MinHeight`.** Its floor is enforced by capping how large the analysis row may become. This is what keeps the design clear of the star-row regression in [Timeline hard minimum](#timeline-hard-minimum).
- **Minimap appearance and disappearance already work.** Row 3 changing between `48` and `0` reflows into row 2 automatically, with no recalculation and no change to the stored share, which is the behaviour the state matrix requires.
- **It replaces a write that already exists.** `EnforceEntriesFloor` writes `_root.RowDefinitions[5].MinHeight` today; the override path writes `_root.RowDefinitions[5].Height` instead. One row, one owner, one call site.
- **Rounding has nowhere to accumulate.** Only one row carries an absolute value; the star row takes the residual exactly.

In wide compact-height Split mode nothing changes at all: `ConfigureWideMobileComposition` keeps its current root-column composition, row 4 stays at zero, and the divider is hidden.

The desktop build is untouched — it keeps its direct root-row children and both existing splitters. Do not refactor desktop and mobile simultaneously unless tests demonstrate a compelling common abstraction.

### 2. Use a model-driven splitter state

Add a small state object next to `MobileWorkspaceState`, proposed name `MobilePaneSplitState`.

It should hold:

- `double? TimelineShare`: normalized share of the resizable band owned by the aggregate plot pane.
- `bool HasUserOverride`: true when the share is non-null.
- Restore/validate logic.
- Update-from-arranged-heights logic.
- Reset logic.

The normalized share must be calculated from the combined plot and analysis region after excluding the splitter lane:

```text
share = plotPaneHeight / (plotPaneHeight + analysisPaneHeight)
```

The plot share includes the minimap. This matches the boundary the user is moving and avoids a stored value changing meaning when the minimap appears or disappears.

Do not persist a physical pixel or dp height. Absolute values fail across rotation, split-screen, density changes, system bars, notices, text scale, and different phones.

### 3. Centralize allocation math

Introduce a pure resolver, either in `MobileWorkspaceLayout.cs` or a focused new file such as `MobilePaneAllocation.cs`.

Proposed inputs:

- Available mobile pane-band height.
- Automatic timeline and analysis weights for the current size class.
- Visible minimap occupied height, including the frame's vertical margins.
- Splitter layout-lane height.
- Measured/cached analysis chrome height.
- Entry row minimum height.
- Preferred entry-row count for the current mode.
- Hard minimum entry-row count for manual resizing.
- Optional stored plot share.
- Whether the layout is stacked, wide, Plot-only, Details-only, or unavailable.

Proposed output record:

- Plot-pane height or star weight.
- Analysis-pane height or star weight.
- Timeline-control minimum.
- Analysis hard/preferred minimum.
- Splitter visibility and enabled state.
- Effective clamped share.
- Whether the stored value was temporarily clamped.

Keep this resolver free of Avalonia controls so edge cases can be covered with ordinary unit tests.

### 4. Define preferred and hard constraints separately

The existing analysis floor is an automatic-layout preference, not a suitable hard limit after the user deliberately asks for more plot space.

Recommended constraints:

#### Timeline hard minimum

Treat the existing 132 dp `MinimumReadablePlotHeight` as a minimum for `TimelineControl` itself, not for timeline plus minimap.

```text
plotPaneHardMinimum = timelineControlHardMinimum
                    + visibleMinimapOccupiedHeight
```

This fixes the current accounting error. The exact 132 dp value may remain initially, but name it so its unit and target are unambiguous, for example `MinimumReadableTimelineHeight`.

The 214 dp timeline desired size remains a quality target, not a hard requirement.

> **This minimum lives in the allocator only.** Do not express it as `MinHeight` on `TimelineControl`, and do not express it as `MinHeight` on a star-sized row containing it. `ApplyMobileLayout` sets `_timeline.MinHeight = 0` deliberately, with the reasoning recorded at the call site:
>
> > No minimum height. A star-sized row cannot refuse one, so the control was arranged taller than its cell whenever the chrome grew — and its own bottom, which is where the axis labels live, was then drawn underneath the minimap that follows it in the grid. The plot's bands give way instead (finding 18); the row weights above are what keep it a useful size.
>
> A star row hands the control its share of the residual; a `MinHeight` cannot enlarge that share, it only makes the control arrange itself larger than the cell it was given, and the overflow is drawn over the neighbour below. Reintroducing it would restore finding 18 — axis labels under the minimap — while doing nothing to protect the timeline. The line stays as it is.
>
> Under this design the timeline's floor is enforced entirely by the ceiling on the analysis row's height, which is a bound the grid genuinely honours. The [Insufficient-room rule](#insufficient-room-rule) covers the case where even that is not satisfiable.

#### Timeline rendering cliff

Separately from the readability minimum, the allocator must never resolve a timeline height below the point at which the control refuses to draw:

```text
timelineRenderingFloor = MinimumLaneBandHeight / 0.52   ~= 69.2 dp
```

Below this, `Geometry()` returns `null` and the control renders *"Not enough height to draw the plot."* This is not reachable through the divider, whose clamp is the 132 dp readability floor, but it is reachable through the compact fallback path when the viewport is genuinely too short. Assert it as an invariant of every allocator output that reports the timeline as visible, so a future change to the weights or to the fallback cannot silently produce a blank plot.

#### Analysis preferred minimum

Preserve the current policy as an automatic preference:

```text
analysisPreferredMinimum = measuredAnalysisChrome
                         + preferredRows * entryRowMinimumHeight
```

Preferred rows remain four in ordinary Split and six in Details, where the splitter is absent.

Compact height keeps **no** floor, which is its current behaviour rather than the three rows `CompactEntryRowFloor` appears to promise — the guard clause returns before that branch can be reached. The reason is sound and must survive this change: in the wide composition the analysis pane is a column spanning every row, so a row minimum adds to the grid's height instead of taking from a neighbour, and the status line goes off the bottom. Either delete `CompactEntryRowFloor` as part of this work or leave it with a comment saying it is unreachable; do not let the allocator start honouring it and reintroduce that overflow.

#### Analysis hard minimum while manually resized

Use measured analysis chrome plus at least one complete entry row:

```text
analysisHardMinimum = measuredAnalysisChrome
                    + manualRows * entryRowMinimumHeight
```

Start with one full row as the proposed manual hard floor. Device and headless tests should verify that the Entries controls, selected tab, footer behavior, Insights scroller, and Entry inspector remain reachable. If one row makes the pane's fixed controls unusable, raise the floor to two rows based on evidence rather than retaining four by default.

Details mode still provides a full-height route whenever the user wants to work primarily in the analysis pane.

#### Insufficient-room rule

Let:

```text
R = available band - splitter lane
Pmin = timeline hard minimum + visible minimap occupied height
Amin = analysis hard minimum
```

If `R < Pmin + Amin`, the splitter has no valid range. Do not overwrite the stored preference. Use the existing compact fallback allocation and hide or disable the divider until more room is available.

### 5. Allocation order

For automatic sizing with no user override:

1. Reserve the fixed shell rows (0, 1, and 6) outside the resizable band.
2. Reserve the splitter lane only when it can be interactive.
3. Satisfy the plot and analysis hard minima.
4. Try to satisfy the current preferred entry-row floor.
5. Distribute remaining height according to `TimelineWeight` and `AnalysisWeight`.
6. Correctly subtract the minimap from the aggregate plot pane before assigning the final `TimelineControl` height.

For a stored user share:

1. Calculate the requested aggregate plot-pane height from the normalized share.
2. Clamp it to `[Pmin, R - Amin]`.
3. Apply the clamped value for the current viewport.
4. Keep the original stored share unchanged when clamping is temporary.

When the viewport later grows, the original preference should be applied again rather than the temporary clamp becoming the new preference.

### 6. Rework `EnforceEntriesFloor`

Do not leave `EnforceEntriesFloor()` independently writing a preferred `MinHeight` after the splitter has applied a user choice. That would cause snapping, oscillation, or an apparently stuck divider.

Refactor it into one of these forms:

- Preferred: it measures/caches analysis chrome and asks the centralized allocation resolver to recompute the pane allocation.
- Acceptable: it retains the current method name but branches explicitly between automatic preferred allocation and user-override hard allocation.

Required safeguards:

- Ignore zero or invalid measurements from tabs that are currently unrealized.
- Cache the last valid analysis chrome value.
- Only invalidate layout when the resolved values change by more than a small tolerance, preserving the current one-extra-pass settling behavior.
- Do not update persisted state from layout corrections.
- Do not turn a viewport clamp into a user override.

## Splitter control and visual design

### Control choice

Use a small custom `MobilePaneSplitter` built on `Thumb`, placed in root row 4. This is the primary recommendation rather than a fallback, because the allocator — which this design needs regardless — already removes the only thing `GridSplitter` would contribute.

- `Thumb` provides pointer capture, `DragStarted` / `DragDelta` / `DragCompleted`, and is the same base class `GridSplitter` itself derives from, so the touch behaviour is not a downgrade.
- It reports a delta. It never writes a row length, so it cannot fight `ApplyMobileLayout`.
- Its drag deltas feed `MobilePaneSplitState` and the pure resolver, which means the drag path and the recomposition path resolve through identical code and cannot disagree.
- There is no star/pixel conversion to go unstable, and no `ResizeBehavior` semantics to reason about.

Configure:

- Vertical drag only; ignore the horizontal component entirely.
- A modest drag increment, proposed 2-4 dp, applied in the resolver rather than the control.
- A keyboard increment, proposed 12-16 dp.
- A north/south resize cursor where cursors exist.
- `ShowsPreview` has no equivalent and is not needed; see [Performance validation](#performance-validation) for the live-resize position.

`GridSplitter` remains a reasonable substitute if a prototype shows it hit-tests and drags acceptably on Android, but it would have to be given the same allocator-driven treatment, at which point it is only supplying a template.

### Thin visual lane with a phone-sized target

Do not allocate a full 48 dp root row solely to the divider; on the inspected viewport that would make the initial timeline smaller before the user moves anything.

Recommended presentation:

- A **12 dp** visual lane at the boundary (root row 4's height when interactive).
- A clearly visible centered grip: a short horizontal pill, roughly 32-40 dp wide and 4 dp high.
- A transparent hit target at least 48 dp high and 72-96 dp wide, centered on the grip.
- `ClipToBounds = false` and an explicit `ZIndex` above the timeline and analysis pane, so the target extends past the thin lane without costing layout.
- Theme-aware high-contrast colors and a distinct pressed/dragging state.

The 12 dp lane is a deliberate change from a 6-10 dp one. The target has to reach 48 dp somehow, and every dp the lane does not supply is overflow into a neighbour that already consumes drags. At a 6 dp lane the target spills about 21 dp in each direction; at 12 dp it spills about 18 dp. The six dp bought is cheap — well under a fifth of an entry row — and it comes straight off the most contested surface in the layout.

Which neighbour absorbs the overflow is worth choosing rather than inheriting. Directly above the boundary is the minimap, whose entire width is a horizontal drag surface: *"Drag the brush to pan; drag either edge to resize the timeline viewport."* Directly below is the analysis pane's top margin and tab strip. If device testing shows the centered target interfering with the brush, bias the target downward — for example 14 dp above the lane and 22 dp below — rather than shrinking it. The tab strip is a discrete tap target that tolerates a reduced top edge far better than a continuous drag surface tolerates a hole in its middle.

Avalonia community guidance for enlarging a splitter's hit target uses an outer transparent target with an inner visual border and clipping disabled. Treat that as a prototype to verify, not as sufficient evidence: the final Android node and the actual drag-start area must be measured on the device.

<a id="edge-gesture-budget"></a>

### Edge-gesture budget

This feature can break the existing back-gesture guard, and it does so precisely when it succeeds.

`EdgeGestureGuard` publishes exclusion rectangles for `_timeline` and `_minimap`. Android honours at most **200 dp of exclusion height per edge** and silently keeps the lowest rectangles past that limit. Today the pair totals roughly 133-145 dp and fits. At this document's own 214 dp target the pair totals about 262 dp, and the platform discards the excess from the top — the top of the newly enlarged timeline.

The failure this produces is the one `EdgeGestureGuard` exists to prevent, reported as finding F-28: a horizontal pan starting in the unprotected band is delivered to the system as Back, and with no overlay open that leaves the workspace for the home screen. The reader would experience it as *"making the timeline bigger makes the app quit."*

Required mitigation:

- Make the exclusion set budget-aware rather than unbounded. `EdgeGestureGuard.Publish` should cap the total published height at 200 dp and decide explicitly what to keep.
- Prefer the minimap in full — it is small, it is entirely a drag surface, and it sits low where Android would keep it anyway — and give the timeline the remainder, trimmed from its top.
- Trimming the timeline's top is the right sacrifice: the pan gesture is available across the whole control, the header band at the top is a label area rather than the primary target, and the alternative is an unpredictable platform-side truncation of the same rectangle.
- Apply the cap per edge, in dp, using the same `RenderScaling` the guard already reads.

This is a change to `EdgeGestureGuard`, not to the splitter, and it is worth making in Phase 0 alongside the accounting fix: the accounting fix alone raises the timeline to about 132 dp, which takes the pair to about 180 dp and leaves only 20 dp of headroom before the budget binds.

Verification is a device scenario, not a unit test. The headless suite can assert the published rectangles total no more than 200 dp; only the device can confirm Back no longer fires.

### Avoiding gesture conflicts

- Keep the divider's large hit target centered rather than extending to Android's left/right back-gesture strips.
- Do not add the splitter to the existing horizontal edge-gesture exclusions unless device testing shows it is necessary.
- Verify that the target does not steal the minimap's horizontal brush drag outside the visibly marked grip.
- Verify that it does not block the top portion of the Entries/Insights/Entry tabs outside the marked grip.
- A drag beginning on the splitter must resize panes and must not pan the minimap or select an analysis tab.
- A drag beginning outside the splitter must retain the existing minimap, timeline, tab, and list behaviors.

## Accessibility requirements

The divider is an interaction, not decoration. It must be discoverable independently of the visual grip.

Set at least:

- Automation name: `Resize plot and details`
- Help text: `Drag up or down to resize the plot and details. Double tap to restore automatic sizing.`
- Tooltip with equivalent concise wording on pointer platforms.
- `Focusable = true` when interactive.
- No accessibility control when hidden or noninteractive.

Keyboard behavior:

- Up/down arrows adjust by `KeyboardIncrement`.
- Home restores automatic sizing.
- Escape cancels an in-progress preview if preview mode is ever enabled.

Android/TalkBack verification must determine what automation role Avalonia exposes for `GridSplitter`/`Thumb`. If it does not expose an adjustable value or usable actions, add a focused automation peer or accessible increment/decrement/reset buttons reachable from the Appearance & timeline sheet. Do not assume an automation name alone makes the control operable with TalkBack.

The Android node's measured target must clear VisualCat's existing 48 dp touch floor even if the visible line remains thin.

## Persistence design

### Settings field

Add an additive nullable field to `ApplicationSettings`, proposed name:

```csharp
double? MobileTimelineShare = null
```

Meaning:

- `null`: automatic size-class allocation.
- finite value between zero and one: requested aggregate plot-pane share in stacked Split mode.

Validation should reject non-finite values and values outside a broad safe storage range. Runtime dynamic constraints still perform the final clamp. A suggested storage range is 0.05-0.95; invalid values should become `null` rather than being silently converted into a surprising extreme.

`ApplicationSettings` is a **positional** record, so the new parameter must be appended after `UpdateLastCheckedUtc` rather than inserted near the related workspace fields. Anything else silently renumbers the positional parameters. Add it at the end with the explanatory comment beside it, matching how `WorkspaceDisplayMode` and the update fields are documented in place.

Normalisation belongs in `SettingsStore.Validate`, beside the existing clamps, in the same `settings with { ... }` expression:

```csharp
MobileTimelineShare = settings.MobileTimelineShare is { } share &&
                      double.IsFinite(share) &&
                      share is >= 0.05 and <= 0.95
    ? share
    : null,
```

The settings version can remain 1 because System.Text.Json tolerates the missing additive property and old files naturally deserialize to `null`. Note that `Validate` discards everything when `Version != 1`, so no migration path is needed either.

### View events and restore path

Add a view event such as:

```text
SplitShareChanged(double? share)
```

Emit it only when:

- A user drag completes.
- A keyboard adjustment completes.
- The user resets to automatic.

Do not emit it for:

- Layout clamping.
- Rotation.
- Switching Plot/Split/Details.
- Opening/closing Filters.
- Overview/minimap appearance.
- A settings restore.

Extend `MainView.CreateWorkspaceView()` to:

1. Restore the persisted share before the first settled mobile layout.
2. Subscribe to `SplitShareChanged`.
3. Update `_settings` and reuse `PersistOpenWorkspaceAsync`'s versioned, coalesced writer.

Keep one value for all sessions, matching `WorkspaceDisplayMode`. When text-scale or display-size changes rebuild workspace views, pass the same setting into each replacement.

### Write frequency

Never write settings on every drag delta. Update the in-memory visual state continuously, then persist once on drag completion. Android pause persistence will still synchronously commit the latest in-memory settings snapshot if the process is backgrounded immediately afterward.

## Composition state matrix

| Situation | Plot pane | Divider | Analysis pane | Stored share behavior |
|---|---|---|---|---|
| Tall portrait, Split | Visible | Visible/enabled if range exists | Visible | Apply/clamp |
| Compact portrait, Split | Visible | Visible/enabled if range exists | Visible | Apply/clamp |
| Narrow compact-height, stacked Split | Visible | Only if useful range exists | Visible | Apply/clamp |
| Wide compact-height, side-by-side Split | Visible as left column | Hidden, row 4 at zero | Visible as right column | Preserve, do not apply |
| Plot mode | Full band | Hidden | Hidden | Preserve |
| Details mode | Hidden | Hidden | Full band | Preserve |
| Filters open | Hidden/covered by drawer | Hidden | Hidden/covered by drawer | Preserve |
| No overview yet | Timeline visible, minimap collapsed | Visible if otherwise valid | Visible | Recalculate with zero minimap height; preserve requested ratio |
| Overview appears | Timeline + minimap visible | Visible if otherwise valid | Visible | Recalculate with minimap height; preserve requested ratio |
| Failure takeover | Hidden | Hidden | Hidden | Preserve |
| Viewport too short for hard minima | Existing compact fallback | Hidden/disabled | Existing compact fallback | Preserve; do not overwrite |
| IME open in filter drawer | Drawer behavior unchanged | Hidden | Hidden | Preserve |
| Rotation back to stacked portrait | Visible | Visible/enabled | Visible | Restore requested share |

## File-by-file implementation plan

### `src/VisualCat.App/Views/MobileWorkspaceLayout.cs`

- Add `MobilePaneSplitState` or an equivalent focused state type.
- Add pure validation, restore, set, and reset behavior.
- Add a pure allocation result type and resolver, or place the resolver in a new adjacent file.
- Keep existing size-class breakpoint behavior unchanged.
- Define the semantics of the normalized share in XML comments.
- Add constants for hard timeline height, splitter visual extent, and manual entry-row floor only if they truly belong to size-class policy. Keep visual-only constants with the splitter control.

### `src/VisualCat.App/Views/SessionWorkspaceView.cs`

- Add fields for the mobile splitter and the split state. No pane-host or plot-pane fields are needed.
- In the mobile branch, add `MobilePaneSplitter` to root row 4 — the branch that currently adds the desktop `GridSplitter` in the `if (!_mobile)` arm.
- Give it automation metadata, theme hooks, a drag-completion persistence callback, and reset behavior.
- Set `ClipToBounds = false` and an explicit `ZIndex` above the timeline and analysis pane.
- Leave the root `RowDefinitions` string, the drawer's `Grid.SetRow(drawer, 2)` / `SetRowSpan(drawer, 4)`, the failure card's row 0 / span 6, and the desktop splitters untouched.

### `src/VisualCat.App/Views/SessionWorkspaceView.Mobile.cs`

- Make `ApplyMobileLayout()` obtain rows 2, 3, 4, and 5 from one allocation result instead of assigning them inline across three branches.
- Apply the state matrix above.
- Leave `ConfigureWideMobileComposition()` alone. Its root-column composition, its four `Grid.SetColumnSpan` consumers, and its row spans are all still correct; the divider is simply hidden and row 4 is zero in the wide composition.
- Refactor `EnforceEntriesFloor()` into measured input for the allocator, and either delete `CompactEntryRowFloor` or mark it unreachable.
- Cache the last valid analysis chrome measurement.
- Write the analysis row's `Height` for a user override and its `MinHeight` for the automatic preference; do not use both at once.
- Leave `_timeline.MinHeight = 0` exactly as it is, with its finding-18 comment.
- Guard all writes with tolerance checks to avoid layout loops.
- Ensure splitter visibility, hit testing, focusability, and automation visibility change together.

### `src/VisualCat.App/Platform/EdgeGestureGuard.cs`

- Cap total published exclusion height at the platform's 200 dp per-edge budget.
- Prefer the minimap in full and trim the timeline's top band with whatever remains.
- Keep the existing one-recompute-per-dispatcher-turn coalescing and the `Same()` short circuit.

### `src/VisualCat.App/Views/SessionWorkspaceView.Presentation.cs`

- Keep desktop minimap row sizing unchanged.
- For mobile, continue delegating minimap visibility to `ApplyMobileLayout()`. The minimap stays in root row 3, so existing assumptions hold.
- Confirm overview appearance/disappearance triggers a pane allocation refresh.

### `src/VisualCat.App/Views/SessionWorkspaceView.Failure.cs`

- Add the mobile splitter to the existing hide/show set, which already lists `_timeline`, `_minimapFrame`, `_rowSplitter`, and `_analysisGrid`.
- Ensure a failure cannot leave a focusable invisible splitter in the automation tree.

### `src/VisualCat.Infrastructure/Configuration/SettingsStore.cs`

- Add nullable `MobileTimelineShare` to `ApplicationSettings`.
- Validate finite range and normalize invalid data to `null`.
- Preserve backward compatibility with settings files lacking the property.

### `src/VisualCat.App/Views/MainView.Workspace.cs`

- Add a persistence handler parallel to `PersistWorkspaceDisplayMode`.
- Update `_settings` in memory immediately.
- Reuse `_workspacePersistVersion`, `_lastWorkspacePersist`, and `PersistOpenWorkspaceAsync` so rapid workspace changes coalesce correctly.
- Ensure on-pause synchronous persistence includes the latest share.

### `src/VisualCat.App/Views/MainView.cs`

- Pass the persisted share into each newly created/rebuilt workspace.
- Subscribe to the workspace's share-change event.
- Avoid duplicate subscriptions when text scale rebuilds workspace views.

### Tests

Primary files to extend:

- `tests/VisualCat.App.Tests/MobileWorkspaceLayoutTests.cs`
- `tests/VisualCat.App.Tests/AndroidAuditFix2Tests.cs`
- `tests/VisualCat.App.Tests/SamsungResponsiveLayoutTests.cs`
- `tests/VisualCat.App.Tests/AppUpdateSettingsTests.cs` or a new focused settings-validation test file

A new `MobilePaneSplitTests.cs` is preferable if the interaction and allocation cases make existing audit files harder to navigate.

### Documentation

- Add a short user-facing changelog entry under `[Unreleased]` after implementation.
- Update Android live-test documentation with measured before/after geometry and touch results.
- Update keyboard help if external-keyboard resizing is part of the shipped interaction.

## Implementation sequence

### Phase 0: Ship the accounting fix on its own

The defect and the feature are separable, and separating them is the main scheduling recommendation in this document.

1. Split `MinimumReadablePlotHeight` into a timeline-only `MinimumReadableTimelineHeight` and subtract the visible minimap's occupied height separately when computing the ceiling in `EnforceEntriesFloor`.
2. Make `EdgeGestureGuard` budget-aware, capping published exclusions at 200 dp per edge and trimming the timeline's top rather than letting Android truncate unpredictably.
3. Update `TheEntriesListClearsItsFloorInSplit` and add a regression asserting that `TimelineControl` itself — not timeline plus minimap — clears the intended minimum in the inspected tall-portrait class.
4. Verify on the Samsung and record the before/after geometry.

This is a few lines of arithmetic plus a bounded change to the exclusion guard. It carries no new control, no new setting, no new gesture, and no new accessibility surface.

What it delivers on the inspected device:

```text
before:  timeline ~= 84.9 dp   (132 dp shared with a 48 dp minimap)
after:   timeline ~= 132 dp    (+48 dp, about +56%)
cost:    analysis  -48 dp      (~0.75 of a 64 dp entry row)
```

That cost is real and should be accepted deliberately rather than discovered: the analysis pane on this device drops from about 421 dp to about 373 dp, and the entries list from roughly four rows to roughly three and a quarter. The four-row preference is a preference, and this is the case it was written to yield in — the plot was below its own stated minimum the whole time. Details mode remains the full-height route for readers who want the list back.

Shipping this first means the highest-value, lowest-risk change is not gated behind the riskiest work, and every later phase starts from a layout whose arithmetic is already correct.

Exit criterion: the timeline clears 132 dp on the inspected device, published exclusions stay within budget, and no other layout changes.

### Phase 1: Lock behavior with pure tests

1. Add the split state and pure allocation model without wiring UI.
2. Test null/default state, restore, invalid values, reset, user overrides, and temporary clamps.
3. Test the current tall portrait, compact portrait, Samsung landscape, Pixel landscape, and short-notice viewport classes.
4. Test minimap-present and minimap-absent allocation.
5. Test insufficient-room output and confirm it never mutates the stored share.

Exit criterion: allocation decisions are deterministic without an Avalonia visual tree.

### Phase 2: Route the existing layout through the allocator, with no visible change

1. Make `ApplyMobileLayout` and `EnforceEntriesFloor` obtain rows 2, 3, and 5 from the allocator instead of computing them inline.
2. Keep row 4 at zero and add no divider.
3. Reproduce the Phase 0 weights and floors exactly, including the compact-height no-floor case.
4. Run the full existing mobile layout test slice unchanged — this is the regression gate for the whole feature.
5. Verify desktop still builds and its splitter bounds are unchanged.

No reparenting happens in this phase or any later one. The root grid keeps its children, its column model, and both overlay row spans.

Exit criterion: every existing mobile layout test passes with the allocator as the only writer of rows 2, 3, and 5.

### Phase 3: Add the divider and the hard constraint model

1. Add `MobilePaneSplitter` in root row 4 with the 12 dp lane and the measured hit target.
2. Give row 4 its lane height only when the divider is interactive.
3. Feed drag deltas through `MobilePaneSplitState` into the allocator; the analysis row takes the resolved pixel height and the timeline row stays star-sized.
4. Convert the entries floor from an unconditional row minimum into an automatic preference, with the separate manual hard floor.
5. Add reset behavior, the Appearance & timeline action, and dynamic enabled/visible logic.

Exit criterion: a headless drag changes both pane heights, stops at both hard bounds, survives a layout pass without snapping back, and `_timeline.MinHeight` is still zero.

### Phase 4: Add persistence

1. Add and validate `MobileTimelineShare`.
2. Restore it before initial mobile layout.
3. Persist only on completed user changes/reset.
4. Verify activity/workspace rebuild, rotation, and app restart.

Exit criterion: a chosen share returns after recreation and a temporary clamp does not replace it.

### Phase 5: Accessibility and visual polish

1. Apply theme-aware grip visuals and pressed state.
2. Verify 48 dp Android target bounds.
3. Verify focus, arrow keys, reset, automation name/help, and TalkBack behavior.
4. Check overlap against minimap and analysis tabs.

Exit criterion: the divider is both usable and nonintrusive at 0.85x, 1.0x, and 1.3x text scales.

### Phase 6: Physical-device validation and documentation

1. Build and install the same configuration intended for release.
2. Execute the device matrix below.
3. Record screenshots, UI-tree bounds, and gesture results.
4. Run targeted and full automated suites.
5. Add changelog and live-test evidence.

Exit criterion: all acceptance criteria and regression checks pass.

## Automated test plan

### Pure allocation tests

Cover at least:

- Default automatic allocation uses size-class weights when both preferences fit.
- Automatic allocation honors the preferred entry-row count when possible.
- Timeline hard minimum is applied to the timeline itself, with minimap added separately.
- Stored share is applied to the aggregate plot pane.
- Stored share is clamped by dynamic hard minima.
- Temporary clamp does not alter `TimelineShare`.
- Minimap appearance reduces timeline height within the same aggregate share.
- Minimap disappearance returns that height to the timeline.
- Insufficient room yields a noninteractive splitter result.
- NaN, infinities, zero/negative band sizes, and invalid stored shares are safe.
- Every result reporting a visible timeline resolves it above the 69.2 dp rendering cliff, including the compact fallback path.
- Compact height resolves with no entries floor, matching current behaviour.

### State and persistence tests

- Restore a valid share.
- Reject invalid or out-of-range settings values.
- Reset returns to `null`/automatic.
- Switching size classes preserves the user override.
- Side-by-side mode ignores but preserves the height share.
- A settings JSON file from before the field existed loads successfully.
- A valid share round-trips through `SettingsStore`.
- Multiple rapid completions persist only the newest value through the existing versioned writer.
- A text-scale workspace rebuild receives the stored share.

### Headless visual-tree tests

At representative viewports, assert:

- Divider exists only in stacked Split mode.
- Divider is absent from Plot, Details, Filters, failure, and wide side-by-side modes.
- Divider's actual hit bounds meet the 48 dp height floor.
- Timeline and analysis are on opposite sides of the divider.
- Minimap remains in root row 3, above the divider, and moves with the plot rather than independently.
- Dragging downward increases timeline bounds and decreases analysis bounds by the same effective amount.
- Dragging upward performs the inverse.
- Dragging stops at timeline and analysis hard minima.
- After drag completion plus `UpdateLayout`, the split does not snap back.
- The entries list and analysis container remain clipped to their pane.
- The status band remains visible and is not overpainted.
- The filter drawer still spans rows 2-5 and does not expose the splitter.
- Overview appearance/disappearance recomputes allocation without losing the preference.
- Switching to Details and back to Split restores the chosen share.
- Rotating stacked portrait -> wide landscape -> stacked portrait restores the chosen share.
- Double tap/Home reset returns to automatic allocation, as does the Appearance & timeline action.
- Desktop `GridSplitter` and insights splitter remain present and functional.
- `_timeline.MinHeight` remains zero in every mobile composition, including while a user override is applied. This is the finding-18 guard and belongs in the assertion set, not only in review.
- The root grid still has seven rows, the drawer still spans rows 2-5, and the failure card still spans rows 0-5 after every composition change.

### Invariant tests

These pin assumptions this feature depends on that live outside its own files:

- `SetRenderWidthAsync` ignores a width change under 24 device pixels. A vertical-only drag must produce no `RefreshAsync`; assert the query count is unchanged across a full drag.
- `EdgeGestureGuard` publishes no more than 200 dp of total exclusion height per edge, at the default layout and at an enlarged timeline.
- The minimap's published exclusion rectangle survives the budget cap intact.

### Update existing floor tests

`TheEntriesListClearsItsFloorInSplit` currently treats four rows as an unconditional result. Revise the contract to match the new policy:

- Automatic mode prefers four rows when the viewport can satisfy that preference and the timeline's own hard minimum.
- Manual mode may reduce analysis to the documented hard floor.
- The list always clips inside its pane.
- Plot and Details modes remain available when the user wants one surface to own the screen.

Add a complementary regression that fails on the current implementation: in the inspected tall-portrait class, the timeline control itself—not timeline plus minimap—must clear the intended minimum, or the divider must permit it to be enlarged to the 214 dp target while retaining a usable analysis pane.

## Physical-device test plan

### Required configurations

1. Samsung SM-G990B, API 36, 1080 x 2340, native 480 dpi.
2. The same Samsung with the observed 360 dpi override, effective 480 x 1040 dp.
3. At least one gesture-navigation device because the splitter target sits near existing horizontal gesture surfaces.
4. Existing 393/434 dp reference device or emulator used by the Android audit.
5. Landscape widths around the 600 dp shared-row breakpoint.
6. Font scale 1.0 and 1.3; user text scale 0.85x, 1.0x, and a larger supported value.

### Device scenarios

For each stacked Split configuration:

1. Open a session with an overview and enough entries to show the footer.
2. Record default timeline, minimap, divider, analysis, list, and status bounds.
3. Drag the divider downward to make the plot approximately 214 dp high.
4. Confirm heat-map cells can be tapped across all six lanes.
5. Perform pinch-in and pinch-out gestures in the enlarged timeline.
6. Confirm the timeline pans when dragging away from the divider.
7. Confirm the minimap brush still pans/resizes outside the grip.
8. Confirm Entries/Insights/Entry tabs remain tappable outside the grip.
9. Drag to both extremes and verify hard stops with no overlap or blank status band.
10. Select each analysis tab at both extremes and scroll its content.
11. Switch Plot -> Split -> Details -> Split and confirm restoration.
12. Open/close Filters and confirm the splitter is unavailable while covered.
13. Rotate to landscape and back; confirm restoration.
14. Background/foreground the activity; confirm restoration.
15. Kill/relaunch after a completed drag; confirm persisted restoration.
16. Change system text/display size so the activity rebuilds; confirm restoration and correct clamp.
17. Reset to automatic; relaunch and confirm the reset persists.

### Evidence to capture

- Before/after screenshots.
- Android UI hierarchy with divider automation name and exact bounds.
- Calculated dp dimensions using active density.
- Short screen recording of drag, pinch, and mode/rotation restoration.
- `dumpsys window` orientation/density configuration.
- Targeted test output and full solution test summary.

## Performance validation

- Resize repeatedly during an active capture and watch for dropped input, visible layout oscillation, or stale clipping.
- Confirm no heat-map query storm is generated by drag deltas. Note that `SetRenderWidthAsync` *is* called on every drag frame, because the handler is wired to `SizeChanged` rather than to width changes; what makes this free is the 24-device-pixel dead band inside the view model, which returns `Task.CompletedTask`. Verify the guard still holds rather than assuming the call does not happen.
- Check that layout settles after drag completion and that `LayoutUpdated` does not continuously rewrite row lengths.
- If live resizing visibly janks, first throttle visual updates to one per dispatcher/render turn. Use `ShowsPreview` only if coalescing is insufficient because preview-only behavior is less direct on touch.
- Persist only after completion, never during each delta.

## Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| **Enlarged timeline exceeds Android's 200 dp exclusion budget** | **Back gesture fires inside the plot and exits the workspace — caused by the feature working** | **Budget-aware `EdgeGestureGuard`: keep the minimap whole, trim the timeline's top; device-verify** |
| **Timeline given a `MinHeight` on a star row** | **Reintroduces finding 18: axis labels drawn under the minimap** | **Keep the minimum in the allocator; assert `_timeline.MinHeight == 0` in tests** |
| **Nested pane grid drags in the root column model and both overlay spans** | **Wide composition and drawer/failure coverage regress far from the change** | **Keep the root grid; move only the allocation arithmetic** |
| Existing entries floor makes splitter immovable | Feature appears broken | Separate automatic preferred floor from manual hard floor |
| Minimap is counted as timeline height again | Timeline remains too small | Model aggregate plot pane and timeline control separately |
| `ApplyMobileLayout` overwrites drag | Pane snaps back | Make stored split state an input to the centralized allocator |
| Allocator honours the unreachable `CompactEntryRowFloor` | Status line pushed off a short viewport | Compact height keeps no floor; delete or mark the constant |
| Fallback path resolves a timeline under 69.2 dp | Plot renders "Not enough height to draw the plot." | Assert the rendering cliff on every visible-timeline result |
| Reset is gesture-only | Reader cannot undo an unhelpful drag | Ship the Appearance & timeline action from the start |
| Splitter target overflows into the minimap brush | Pan and resize compete mid-control | 12 dp lane, centered target, downward bias if device testing requires |
| Render-width dead band removed later | Vertical drag becomes a query storm | Pin the 24 px guard with an invariant test |
| LayoutUpdated oscillation | Jank/high CPU | Cache valid measurements and use tolerance-guarded writes |
| Thin splitter misses touches | Original usability issue persists | 48 dp measured target with thin visual grip |
| Large target steals minimap/tab touches | Regresses adjacent controls | Center target, mark it visibly, test hit regions on device |
| Android back gesture wins | App exits during resize | Keep target away from edges; use exclusion only if measured necessary |
| Share becomes wrong after rotation | Surprising restoration | Normalize against resizable band and preserve unclamped value |
| Very short viewport has no legal travel | Stuck/overlapping UI | Hide or disable divider and retain Plot/Details modes |
| Analysis tab other than Entries has no current list measurement | Invalid floor | Cache last valid chrome or measure fixed chrome independently |
| Settings writes on every delta | I/O churn/races | Persist on completion through existing coalesced writer |
| Desktop layout regresses during shared refactor | Cross-platform breakage | Keep the divider and allocator mobile-only; run desktop splitter tests |
| TalkBack cannot operate stock splitter | Accessibility gap | Verify automation role/actions; add peer or settings fallback |
| Density override affects hit-test scaling | Device-only miss | Test native and overridden Samsung density explicitly |

## Acceptance criteria

The implementation is complete only when all of the following are true:

### Functional

- In stacked mobile Split mode, a visible divider adjusts plot and analysis heights.
- The plot pane includes both timeline and minimap; the minimap does not resize independently.
- The divider has useful travel in the inspected Samsung portrait layout.
- The user can enlarge the timeline to approximately its 214 dp preferred height while retaining a usable analysis pane on the inspected 480 x 1040 dp logical display.
- The divider respects documented hard minima and never causes content to paint into the status band.
- Plot, Details, Filters, failure, no-overview, and wide compact-height behavior matches the state matrix.
- Switching modes/orientation and returning to stacked Split restores the chosen share.
- Reset returns to automatic allocation.

### Persistence

- The share survives activity recreation, text/display scale rebuild, app restart, and pause/resume.
- Old settings files load without error.
- Invalid values safely return to automatic.
- Temporary clamping does not overwrite the stored preference.

### Touch and accessibility

- The measured Android divider target is at least 48 dp high.
- Drag starts reliably with one finger at native and overridden density.
- Enlarged timeline cells can be selected and pinch zoom works on the connected Samsung.
- Adjacent minimap and tab gestures remain available outside the marked grip.
- The divider has an automation name/help description and a verified keyboard/TalkBack route.
- A horizontal pan anywhere inside the enlarged timeline pans the plot and never triggers the Android back gesture.
- Total published gesture exclusions stay within the 200 dp per-edge budget at every divider position.

### Quality

- `_timeline.MinHeight` is zero in every mobile composition; finding 18 has not been reintroduced.
- The root grid retains seven rows, and the drawer and failure overlays retain their current row spans.
- No continuous layout invalidation or query storm occurs during drag.
- Targeted allocation, persistence, mobile layout, and interaction tests pass.
- Full solution tests pass.
- Desktop timeline/analysis and insights splitters retain their existing behavior.
- Changelog and device-test evidence are updated.

## Definition of done checklist

Implemented and verified on the Samsung SM-G990B; see
[`docs/ANDROID-LIVE-TEST-REPORT.md`](docs/ANDROID-LIVE-TEST-REPORT.md) sections
23 and 24. Section 24 records one defect this plan did not anticipate: `Thumb`
reports a cumulative drag vector in its own moving coordinate space, so summing
it only tracks the finger while a layout pass lands between two touch events.

- [x] **Phase 0 shipped independently: accounting defect corrected and gesture budget capped.**
- [x] Pure split-state/allocation model implemented and tested.
- [x] Allocator is the only writer of mobile rows 2, 3, 4, and 5.
- [x] Root grid, column model, and both overlay row spans unchanged.
- [x] `_timeline.MinHeight` still zero; finding 18 guarded by test.
- [x] Divider implemented in root row 4 with verified 48 dp Android hit target.
- [x] `EdgeGestureGuard` budget-aware and device-verified against the back gesture.
- [x] Automatic preferred floor and manual hard floor separated.
- [x] `CompactEntryRowFloor` deleted or documented as unreachable.
- [x] Reset available from the Appearance & timeline sheet, not only by gesture.
- [x] Split share restored and persisted through existing settings pipeline.
- [x] Reset-to-automatic route implemented.
- [x] All mode/filter/failure/overview transitions covered.
- [x] Rotation, recreation, and text-scale rebuild covered.
- [x] Android native-density and override-density checks completed.
- [x] Timeline click and pinch usability confirmed on Samsung SM-G990B.
- [x] Automated targeted and full suites passing.
- [x] Desktop regression checks passing.
- [x] User-facing changelog and Android test evidence updated.

## Reference material

- Avalonia GridSplitter documentation: <https://docs.avaloniaui.net/controls/layout/panels/gridsplitter>
- Avalonia GridSplitter API: <https://docs.avaloniaui.net/api/avalonia/controls/gridsplitter>
- Avalonia GridSplitter source: <https://github.com/AvaloniaUI/Avalonia/blob/main/src/Avalonia.Controls/GridSplitter.cs>
- Avalonia pointer capture guidance: <https://docs.avaloniaui.net/docs/input-interaction/pointer>
- Avalonia discussion about enlarging splitter hit targets without enlarging the visual line: <https://github.com/AvaloniaUI/Avalonia/discussions/17694>
