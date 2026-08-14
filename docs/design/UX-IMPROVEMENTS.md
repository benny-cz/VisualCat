# VisualCat — highest-ROI UX/UI improvements

**Status:** living assessment and implementation record. Four items are implemented and
retained after physical-device validation on 2026-08-14 — UX-1A (explicit count-scope
labels), UX-3 (filter-driven timeline lanes), UX-2A (severity continuity and search
marking in the entry table) and UX-4A (selected-entry caret) — and the rest remains
backlog input.
**Written against:** VisualCat `2.0.1` (post-release `main`); the implemented candidate
work is released in `2.0.2` after physical-device validation on 2026-08-14.
**Audience:** anyone picking up VisualCat's interface work — maintainer, contributor,
or an agent session with no prior context. This document is self-contained: §1 and
Appendix A give enough orientation to act without reading anything else first.

---

## 0. How to read this document

This is an analysis of where VisualCat's interface costs its users time, followed by a
ranked set of improvements and the validation record for work already selected. Each
proposal states the problem, the evidence in the implementation, a concrete design, where
the code change lands, its cost, its risks, and how you would know it worked. Sections
marked **Implemented** describe the 2026-08-14 candidate; unmarked proposals remain backlog.

Items are ranked by **return on investment**, not by ambition:

```
ROI  ≈  (impact on the core loop  ×  how often it is hit)  ÷  effort
```

so a small change that improves every second of use outranks a large change that improves
a rare one. The single most *impressive* feature in this document (UX-6, guided incident
navigation) is deliberately **not** ranked first, because five cheaper items each buy more
per unit of work. That ranking is a recommendation, not a constraint — §10 offers a
sequencing that lets the headline feature be pulled forward without stranding the cheap
wins.

Effort sizes:

| Size | Meaning |
|---|---|
| **S** | One or two files, no new query, no new persisted state. Hours. |
| **M** | Several files, a new presentation concept or query shape, needs tests. Days. |
| **L** | New subsystem or persisted format change. A week or more. |

Every proposal is checked against the project's binding constraints (§1.4). Anything that
would violate one is in §8, "deliberately not recommended", with the reason.

---

## 1. The product in one page

### 1.1 What VisualCat is

A local-first Android **logcat analyzer**. It ingests a logcat file, a growing file, a live
`adb logcat` stream, or an on-device Android capture; indexes it into a memory-mapped
columnar session store; and presents it as an interactive **severity × time heat map** —
six lanes (Fatal, Error, Warn, Info, Debug, Verbose, plus Unknown when present), time on
the x-axis, event density as bar height and opacity.

Tagline: *"See the shape of your log."* The promise is that you spot crash storms, error
bursts, and quiet gaps without scrolling through millions of lines.

It ships as a cross-platform Avalonia desktop application, a reduced Android companion
(Google Play, `com.barebit.visualcat`), and a `vcat` CLI over the same engine.

### 1.2 Who uses it

An Android or platform engineer holding a large log and one question: **what went wrong,
when, and what led up to it.** They are usually working against a deadline, often with a
log someone else captured, and they need to end up with evidence they can paste into a bug
report.

### 1.3 The core loop

Every improvement in this document is judged against this loop:

```
 open a source
      ↓
 read the shape        ← "where is something interesting?"
      ↓
 zoom to the moment    ← "show me that 200 ms"
      ↓
 narrow the population ← "only this tag / process / severity"
      ↓
 read actual lines     ← "what does it say"
      ↓
 explain and hand off  ← "copy / export / share the evidence"
```

The loop is iterative: steps 2–5 are re-entered many times per investigation. Cost paid in
those steps is paid dozens of times per session, which is why cheap fixes there dominate
the ranking.

### 1.4 Constraints that bound every proposal

These are settled project decisions. A proposal that violates one is not viable.

| Constraint | Source | Consequence for UX work |
|---|---|---|
| **No telemetry, ever, by default** | `docs/adr/0017-telemetry.md`, `docs/PRIVACY.md` | Success cannot be measured by analytics. See §11. |
| **Local-first, no log upload** | `docs/PRIVACY.md` | No cloud sharing, no hosted permalinks. |
| **One immutable `FilterSpec` feeds every query** | `docs/adr/0012-filters.md` | New filtering UI extends `FilterSpec`; it must not introduce a second, view-local filter. |
| **Pure queries over immutable snapshots** | `PLAN.md` §5.6, §12.1 | New panels ask the engine; they never accumulate their own state. |
| **No per-cell allocation in the render path** | `PLAN.md` §19.3 | Timeline additions use cached immutable brushes/pens, like `LevelPalette`. |
| **Frame budget: full-view heat map ≤ 8 ms at 1M entries** | `docs/PERFORMANCE.md` | New drawing must be O(columns), not O(entries). |
| **Color is never the only signal** | `docs/adr/0013-density.md`, `PLAN.md` §14.14 | Every color-carried state also needs a label, shape, position, or count. |
| **Dark and light are both first-class** | `PLAN.md` §14.1 | New surfaces read theme via `WorkspacePalette`, not hardcoded hex. |
| **The UI is built in C#, not XAML** | `src/VisualCat.App/Views/*.cs` | Changes are code edits, not markup; there is no style system to lean on. |
| **Desktop and Android share one view layer** | `SessionWorkspaceView` `_mobile` branches | Every desktop addition needs an explicit mobile answer, even if that answer is "not on phones". |

### 1.5 Where the interface lives

Full map in **Appendix A**. The five files that matter most:

| File | Role |
|---|---|
| `src/VisualCat.App/Views/MainView.cs` | Shell: command bar, tabs, start page, settings dialogs, global shortcuts. |
| `src/VisualCat.App/Views/SessionWorkspaceView*.cs` | The workspace: filter bar, chips, timeline host, entry table, insights panes, status bar. Six partial files. |
| `src/VisualCat.App/Timeline/TimelineControl.cs` | The heat map: drawing, hit testing, hover readout, keyboard. |
| `src/VisualCat.App/Timeline/MinimapControl.cs` | Whole-session density strip and viewport brush. |
| `src/VisualCat.App/Presentation/SessionTabViewModel.cs` | Per-session state and query orchestration. |

---

## 2. What the current interface already does well

This matters: several proposals below are *completions* of good existing ideas, not
replacements, and the analysis is not credible if it only lists faults.

- **The heat map is genuinely well built.** Intensity is double-encoded as opacity *and*
  bar height above a baseline, so a dense row reads as a profile rather than a solid block
  (`TimelineControl.Render`). Sub-pixel bars are widened to stay visible and clickable, and
  hover snaps to the nearest occupied column, so a one-pixel Fatal bar is reachable
  (`TimelineBars.SnapToOccupiedColumn`).
- **The hover readout is excellent.** Exact half-open interval, total, per-level counts
  drawn in each level's own color, the hovered row's share of the cell, and a debounced
  per-cell dominant-template lookup whose late results are discarded if the pointer has
  moved on. That last detail is the kind of correctness most tools get wrong.
- **The minimap already carries a severity pulse** — a fixed tick along the top edge when a
  column contains any Fatal or Error, so a lone fatal drowning in verbose traffic stays
  findable. This is the seed UX-6 grows from.
- **Severity toggles double as the legend.** They carry each level's color on a tinted
  plate, with a custom `ControlTheme` written specifically because Fluent's default painted
  every checked toggle the same accent blue.
- **Query correctness is visible in the UI.** Results carry snapshot/filter/query
  generations and superseded results are dropped, so counts across panes cannot disagree.
- **Live capture states are honest.** Preparing, connecting, starting, and capturing are
  distinct, and the empty timeline explains which one it is instead of implying failure.
- **Accessibility groundwork exists.** Automation names on interactive elements, a keyboard
  path through the primary analysis flow, configurable text scale, a high-contrast mode
  that thickens the selection outline rather than relying on color.
- **Error copy has been thought about.** Cancellation is explicitly *not* rendered as a
  failure, and failures are labelled as failures rather than dumping a bare framework
  message.

The gap is not craftsmanship. It is that the interface **shows** the log well but does not
yet **guide**, and that several correct engine capabilities never reach the screen.

### 2.1 Independent research — 2026-08-14

The code audit was checked against current platform guidance, incumbent log-analysis
products, and visualization research rather than treating VisualCat's existing design as
the only frame of reference.

| External evidence | Product implication |
|---|---|
| [Datadog Log Explorer facets](https://docs.datadoghq.com/logs/explorer/facets/) summarizes values for the active search, while [Google Cloud Logs Explorer](https://cloud.google.com/logging/docs/view/overview) makes its fields and results respond to the selected time range. | Strongly validates UX-1: insight counts should expose their scope, and the useful default is the current investigation scope rather than an unexplained whole-session census. |
| [Android Studio Logcat](https://developer.android.com/studio/debug/logcat) supports explicit field queries for package, process, tag, message, level and age, including negation and regular expressions. | Validates VisualCat's facets, chips, severity controls and search as the right primitives. A composable query syntax is a more evidence-backed future direction than prioritizing a command palette. |
| An empirical study of [overview+detail time-series visualization](https://www.sciencedirect.com/science/article/pii/S1877050921003343) found that its effectiveness depends on task and data. | Keep the minimap, but do not assume that an overview alone guides an investigation; UX-6 still needs explicit, testable incident ranking. |
| Android's [window-size-class guidance](https://developer.android.com/develop/adaptive-apps/guides/use-window-size-classes) treats the current viewport—not a device label—as the basis for layout and requires testing dynamic compact, medium and expanded states. | Validates the existing adaptive workspace and makes reclaimed vertical space in UX-3 especially valuable on phones and split-screen windows. |
| Android recommends [at least 48 dp touch targets](https://developer.android.com/guide/topics/ui/accessibility/views/apps-views), and WCAG requires that [color not be the only visual means](https://www.w3.org/WAI/WCAG22/Understanding/use-of-color.html). | Preserve the existing severity letters and full-size toggles when changing lane layout; do not replace them with color-only or miniature controls. |
| Google Cloud lets users [configure the timestamp column](https://cloud.google.com/logging/docs/view/overview) specifically to recover horizontal space. | Supports UX-2's adaptive time treatment, but not before search highlighting and virtualized-row cost are designed and measured together. |

The research strengthened the case for UX-1, but the code audit changed its sizing: a full
scope selector is **M**, not S, because `Statistics` also supplies the stable session total
and fit-to-matches behavior. Replacing it with a viewport query would silently change other
semantics and add a pan/zoom query lifecycle. Explicit scope labels are the safe S-sized
first phase. UX-3 remains the highest-confidence immediate win because it reclaims scarce
plot space without changing query results, persistence, or the engine.

---

## 3. Where the core loop leaks

| Loop step | What happens today | Friction | Fixed by |
|---|---|---|---|
| Open | Start page offers Open log / ADB live / Reopen session | Nothing to open if you're evaluating; no hint of what the app can do | UX-7 |
| Read the shape | Six lanes, per-row log normalization | Every lane is normalized to its own max, so nothing stands out as *unusual*; the user must scan | UX-6 |
| Zoom | Wheel, drag, right-drag, double-click, keys | Excellent — but undiscoverable, and there is no way back to the previous view | UX-7, UX-10 |
| Narrow | Severity toggles, facets, chips, search | In 2.0.1, Facets count the **whole session** while Templates count the **viewport** in adjacent tabs without a header label; UX-1A now labels both scopes | UX-1A implemented; selector remains |
| Narrow | Hiding Verbose/Debug | In 2.0.1, emptied lanes still occupy a third of the plot; the candidate removes them | UX-3 implemented |
| Read lines | Monospace table, message last | In 2.0.1, no color, no search highlighting, no quality flags — a uniform wall of text; UX-2A adds severity continuity and search marking, quality flags remain | UX-2A implemented; flags and time column remain |
| Read lines | Selecting a table row | In 2.0.1, the plot did not mark where that row is; UX-4A now carets it | UX-4A implemented; template markers remain |
| Interpret | `PER-ROW LOGARITHMIC` printed at 10 px | The setting that defines what the picture *means* is static text; the control is three menus away | UX-5 |
| Explain | Copy raw, export CSV, share portable | Fine for data, nothing for a finding | UX-13 |
| Anything fails | One ellipsized grey line in the top-right corner | Errors are easy to miss and vanish on the next action | UX-8 |

---

## 4. Ranked summary

| # | Improvement | Impact | Effort | ROI | Wave |
|---|---|---|---|---|---|
| **UX-1** | Make every count state its scope, and let the user switch it | 5 | S labels / M selector | ★★★★★ | 1 — *labels done* |
| **UX-2** | Entry-table legibility pass | 5 | S–M | ★★★★★ | 1 — *severity + search marking done* |
| **UX-3** | Lanes follow the severity filter | 4 | S | ★★★★★ | 1 — *done* |
| **UX-4** | Two-way plot ↔ table ↔ template linkage | 4 | S | ★★★★☆ | 1 — *entry caret done* |
| **UX-5** | Make the plot self-explaining and directly tunable | 4 | S | ★★★★☆ | 1 |
| **UX-6** | Guided incident navigation ("next thing worth looking at") | 5 | M | ★★★★☆ | 2 |
| **UX-7** | First-run: a sample session, a shortcut sheet, gesture hints | 4 | S | ★★★★☆ | 1 |
| **UX-8** | One real notification surface, with actionable errors | 4 | S–M | ★★★☆☆ | 2 |
| **UX-9** | Finish the template explorer | 4 | M | ★★★☆☆ | 2 |
| **UX-10** | Navigation history (Back / Forward over viewport + filter) | 3 | M | ★★★☆☆ | 2 |
| UX-11 | Entry inspector with quality flags | 3 | M | ★★★☆☆ | 3 |
| UX-12 | Log-quality ribbon (drops, gaps, out-of-order) | 4 | M | ★★★☆☆ | 3 |
| UX-13 | "Copy finding" — a pasteable evidence snippet | 3 | S | ★★★★☆ | 2 |
| UX-14 | Status bar as session health | 2 | S | ★★★☆☆ | 3 |
| UX-15 | Filter bar grouping and button hierarchy | 3 | S | ★★★☆☆ | 3 |
| UX-16 | Persist workspace layout | 2 | S | ★★★☆☆ | 3 |
| UX-17 | Command palette | 3 | M | ★★☆☆☆ | 3 |
| UX-18 | Range A/B comparison | 4 | L | ★★☆☆☆ | 3 |
| UX-19 | Severity palette separation and contrast audit | 2 | S | ★★★☆☆ | 3 |

### 4.1 Feasibility gate and selected work

#### Pass 1 — 2026-08-14

| Candidate | Value and feasibility finding | Decision |
|---|---|---|
| UX-1 scope labels | Removes an analytic ambiguity in adjacent panes; presentation-only, accessible and low-risk. | **Implement now and keep.** |
| UX-1 scope selector | High eventual value, but requires a separate scoped-statistics result, cache key, cancellation/debounce behavior, and preservation of session-total consumers. | **Keep as the next scoped-query feature; do not disguise it as an S.** |
| UX-3 filter-driven lanes | Improves the core plot on every severity narrowing action; no engine, store, query or persisted-state change. | **Implement now and keep if the physical A/B test shows material space gain.** |
| UX-2 full table pass | Valuable, but search highlighting needs bounded matching and virtualized-row performance work; adaptive timestamps change comparison behavior. | **Backlog as a measured, coherent pass.** |
| UX-5 duration/readout work | Small code change, but mobile range selection is not yet equivalently discoverable or touch-accessible. | **Do not ship a desktop-only core-loop improvement in this pass.** |
| UX-6 incident navigation | Highest differentiating value, but anomaly thresholds need representative-corpus validation and false-positive design. | **Research/prototype next; not the low-risk implementation pick.** |

The implementation pick was therefore **UX-3 plus UX-1A**: the strongest immediate visual
gain and the smallest correctness fix. The physical-device gate in §12 determined that they
stayed.

#### Pass 2 — 2026-08-14, after UX-1A and UX-3

The remaining Wave 1 items were re-ranked against one added requirement: an improvement in
this pass has to be **verifiable on the physical device**, which means it has to live in
the shared view layer rather than in desktop-only chrome.

| Candidate | Value and feasibility finding | Decision |
|---|---|---|
| **UX-2A** — severity continuity + search marking | The two table defects that cost the most per session, both on the surface the user's eyes are on longest. The data is already on `NormalizedEntry`, the marking predicate can reuse the engine's own compiled regex, and both changes reach the mobile card as well as the desktop grid. | **Implement now and keep.** |
| **UX-4A** — selected-entry caret | The one unimplemented item of `PLAN.md` §14.9, one method on the control plus one wiring line, and worth more on a phone than on a desktop because the split view puts plot and list on one screen. | **Implement now and keep.** |
| UX-2 adaptive time column | Changes what two rows' timestamps mean relative to each other, and the header-click cycling is a mode with no mobile equivalent. Needs its own design pass, not a ride-along. | **Backlog; do not fold into a legibility pass.** |
| UX-2 quality-flag column | Needs a column-width change, a glyph vocabulary, and a tooltip story that has no touch equivalent. Genuinely valuable — it is what makes the tool forensic — but it is its own feature. | **Backlog as a distinct item, with UX-11/UX-12.** |
| UX-2 row hover band | Fluent already supplies a pointer-over background for `ListBoxItem`, and overriding it needs a template-part selector against theme resources. Desktop-only, so not device-verifiable. | **Drop: it mostly exists, and the rest is not worth the fragility.** |
| UX-4 template occurrence markers | Needs a bounded per-template marker query that does not exist and a marker-channel budget shared with search and incidents. | **Backlog with UX-9, where the query belongs.** |
| UX-5 / UX-7 | Unchanged from pass 1: still desktop-weighted, still not the cheapest device-verifiable win. | **Backlog.** |

The pick is **UX-2A plus UX-4A**: together they close the two adjacent leaks in the middle
of the loop — *read actual lines* and *keep your place while reading them*.

---

## 5. Tier 1 — the highest-ROI improvements

### UX-1 · Make every count state its scope, and let the user switch it

**Impact 5 · Effort S (labels) / M (selector) · Wave 1**

**Decision (2026-08-14): UX-1A scope labels implemented and kept; the full selector is
feasible but deferred until it has an independent scoped-statistics result.**

#### Problem (2.0.1 baseline)

The workspace shows counts from three different scopes and mostly does not say which is
which:

| Surface | Actual scope | Stated in 2.0.1? |
|---|---|---|
| Summary line above the table | Viewport (`MatchesInView`) + session total | Yes — "in view" / "in session" |
| Entry table | Viewport, or one selected cell | Partly |
| **Templates pane** | **Viewport** | **No** |
| **Facets pane** | **Whole session** | Only in a 10 px paragraph |
| Timeline lane labels | Viewport | No |

Templates and Facets are **adjacent tabs in the same `TabControl`** and answer different
questions. Evidence: `SessionTabViewModel.RefreshAsync` calls
`QueryStatistics(snapshot, filter, …)` with no time range (session-wide) but
`ScheduleTemplateRefresh(filterKey, viewport.Value, filter, …)` keyed on the viewport.

The consequence is the most common analytic question in the product going unanswered.
The user zooms into an error spike and asks *"which tag is causing this?"* — and the Facets
pane answers about the whole session, so a tag responsible for 100% of the spike but 2% of
the session ranks nowhere near the top.

The 2.0.1 mitigation — a paragraph of 10 px explanatory text at the top of the facet pane
(`SessionWorkspaceView.Panes.cs`, `BuildFacetPane`) — is a smell: prose is compensating
for a control that should exist.

#### Proposal

1. Add a **scope selector** to the insights pane header, applying to Facets *and*
   Templates: `[ This view ] [ Whole session ]`, default **This view**.
2. Every scoped pane shows its scope in its header at all times, not in body prose.
3. When a timeline cell or dragged range is selected, the scope selector gains a third,
   auto-selected state: `[ Selection ]`.
4. Delete the explanatory paragraph; keep the include/exclude semantics note as a tooltip
   on the `+`/`−` buttons, where it is already partly duplicated.

#### Why it remains high ROI

The explicit-label half is nearly free and prevents a user from comparing unlike counts.
The selector would convert the facet pane from a static session census into the answer to
the question the user is asking on each zoom, but the presentation mechanism is only one
part of that work. In the current architecture `Statistics` also drives the stable session
total and `FitToMatchesAsync`; overwriting it with a viewport result would be a correctness
regression. The selector therefore needs a separate result and is M-sized.

#### Implementation

Phase A, now implemented:

- Add persistent, accessible pane-header labels: **COUNTS · THIS VIEW** for Templates and
  **COUNTS · WHOLE SESSION** for Facets.
- Replace the long facet paragraph with a concise action-semantics note while preserving
  the include/exclude explanation.

For the remaining selector work:

- Do **not** replace the existing session-wide `Statistics`. Add an independently
  generation-checked scoped-statistics result for the Facets pane, passing
  `filter with { TimeRange = viewport }` (or the detail range) to `QueryStatistics` when
  the scope is "This view". `FilterSpec.TimeRange` already exists, so the engine needs no
  semantic change.
- Cache key: `_statisticsCacheKey` must incorporate the scope and the viewport bounds, the
  way `ScheduleTemplateRefresh` already composes `$"{filterKey}|{start}:{end}"`.
- Statistics currently runs on every refresh when the filter changes; scoping it to the
  viewport makes it re-run on pan/zoom too. Debounce it on the same cadence as the template
  refresh, and keep the existing `_renderedFacets` reference check in
  `SessionWorkspaceView.Presentation.cs` `UpdateStatistics` so panning does not rebuild a
  hundred facet rows per frame.
- The facet rebuild already restores scroll offset and pins active values to the top of
  their group; both behaviours become more important once the list changes on pan.

#### Risks

- **Cost on pan.** `QueryStatistics` over a narrow viewport is cheaper than over the
  session, but it now runs more often. Debounce (≈250 ms after motion stops) and keep the
  last result on screen while the new one computes rather than blanking the pane.
- **Ranking instability.** A facet list that reshuffles while the user pans is disorienting.
  Keep the existing "active values pinned to the top" rule, and consider freezing the row
  order while the pointer is inside the pane.

#### Done when

- [ ] Zooming into a burst and opening Facets ranks the tags responsible for *that burst*.
- [x] The adjacent Facets and Templates panes display their different scopes without the
  user reading body prose.
- [ ] Panning does not visibly stutter on a 1M-entry session after the selector is added.

---

### UX-2 · Entry-table legibility pass

**Impact 5 · Effort S–M · Wave 1**

**Decision (2026-08-14): UX-2A — severity continuity and search marking — implemented,
tested on a physical Android device, and kept. The adaptive time column and the
quality-flag column remain backlog; the row hover band was dropped as already provided by
the theme.**

#### Problem

The entry table is where the user finally reads the log, and it is the least designed
surface in the workspace. Today it is a fixed eight-column monospace grid — the track sizes are one shared literal,
`EntryColumns = "165,32,112,56,68,96,52,*"` in `SessionWorkspaceView.cs`, used by both the
header and the row template — in which the only color is a single letter in a 32-pixel
column. Specifically:

1. **No severity color continuity.** The plot teaches the user a color language — Fatal is
   hot pink, Error coral, Warn amber — and the table drops it. An error row and a verbose
   row are visually identical apart from one character.
2. **Search terms are not highlighted.** Grep for `AndroidRuntime`, get 500 rows, and you
   still have to find the match inside each message by eye. Evidence: message cells are
   plain `TextBlock`s (`Cell(entry.Message.Split('\r','\n')[0], 7)`); the string
   "highlight" appears nowhere in the view layer.
3. **The time column is oversized and fixed.** 165 px of `MM-dd HH:mm:ss.ffffff` on every
   row, including when the viewport spans hours and microseconds are noise. The *timeline
   axis* already adapts its format to the span (`TimelineControl.FormatTick`); the table
   does not.
4. **Per-entry quality flags are invisible.** `EntryAttributes` carries
   `InferredTimestamp`, `LowTimestampConfidence`, `OutOfOrder`, `EncodingFallback`,
   `LongLineOverflow`, `Chatty`, `ContinuationGroup`, `ReconnectDuplicate` per entry — and
   the UI surfaces them only as aggregate counts in the Session tab. A row whose timestamp
   was *guessed* looks exactly as trustworthy as one read from the log. For a forensic
   tool this is the wrong default.
5. **No row hover or banding.** Tracking one row across eight columns on a 4K display is
   pure eye work.

#### Proposal

A single coherent pass over the row template:

- **Severity edge.** A 3 px vertical bar at the row's left edge in `LevelPalette.ColorOf`,
  plus a very low-alpha row tint for Warn/Error/Fatal only. Verbose/Debug/Info stay
  untinted so the treatment means "notable", not "decorated". Keep the `L` letter column —
  color must not be the only signal (ADR 0013).
- **Search highlight.** Render the message cell as inline runs with matched spans on an
  accent-tinted background. Same treatment in the source-context pane. For regex searches,
  reuse the compiled regex the search query already built.
- **Adaptive time column.** Format by viewport span the way the axis does; drop the
  redundant `MM-dd` when the viewport does not cross a day, and trim sub-second digits when
  the span is minutes or more. Clicking the `TIME` header cycles
  *absolute → relative to viewport start → delta from previous row*. Relative time is what
  you want when measuring a startup or an ANR, and today it requires mental arithmetic.
- **Flag glyphs.** A narrow column of small monospace marks (`~` inferred time, `!` low
  confidence, `↺` out of order, `≡` continuation group, `×` chatty drop) with a tooltip
  spelling each one out. Zero-width for clean rows.
- **Row hover.** A one-line hover background. Cheap, and it is the difference between
  reading a table and decoding one.

#### Why it is high ROI

This is the surface the user's eyes are on for most of the session, it is currently the
weakest, and none of the fixes require an engine change — the data is already on
`NormalizedEntry`.

#### Implementation of UX-2A, as shipped

- **Severity edge and row tint.** `BuildDesktopEntryTemplate` puts a 3 px level-colored
  `Border` in column 0, inside the 4 px gutter the time cell's own margin already leaves —
  so the ribbon costs no width and every value stays under its column header, and the
  shared `EntryColumns` literal is untouched. The row `Background` is
  `LevelPalette.Fill(level, dark ? 14 : 26)` for Warn, Error and Fatal only; Verbose, Debug
  and Info stay untinted so the treatment means *notable*, not *decorated*. The `L` letter
  column stays, so color is never the only carrier (ADR 0013), and it now draws with
  `LevelPalette.BrushOf` instead of allocating a `SolidColorBrush` per row.
- **Search marking.** `EntryHighlight.Match` is a pure function from (message, `TextSearchSpec`)
  to non-overlapping spans. It is bounded twice: only the first 512 characters are scanned,
  because the cell renders one ellipsized line and a match past that is not on screen to
  mark, and at most 24 runs are produced. A literal search uses the same ordinal comparison
  the engine uses; a regex search calls `SessionQueryEngine.CompileSearchRegex`, so the
  marking predicate is the *same compiled instance* the query used to select the row —
  "marked" and "matched" cannot disagree, no row pays a compile, and the search timeout is
  honoured (a pattern too slow on one message degrades to plain text, not to a stalled
  frame). Matched runs render as inline `Run`s in the timeline's own search-tick magenta,
  with an explicit dark foreground and bold weight, so the mark is legible in both themes
  and carries a non-color signal too.
- **One channel for one concept.** The mark deliberately reuses `#FF3FE0`, the color of the
  search ticks under the plot axis, so a hit in the plot and a hit in the text read as one
  answer rather than two unrelated marks.
- **Mobile.** The card template gets the same edge, tint and marking. While there, its
  hardcoded `#223650` and `#8EA2BE` were replaced with `WorkspacePalette` reads (§9.10).
- **Theme.** Rows resolve theme colors when they are realized, and a theme change does not
  re-realize them, so `ApplyThemeSurfaces` reinstalls the template — which rebuilds every
  container exactly once. A *search* change needs no such nudge: it re-queries the entry
  list, and the repopulation re-realizes rows on its own.

#### Physical-device result

Tested on a Motorola edge 60 pro (Android 16 / API 36, 1220×2712, density 450) against a
deterministic 4,000-entry `vcat generate-test-log --seed 42` sample, in portrait and
landscape, and on the desktop build in both dark and light themes.

Severity now reads off the card edge without parsing the letter, and the Warn/Error/Fatal
tint separates notable rows from background traffic at a glance. Searching `Connection`
returned **553 matches**, and every visible card carried the term marked in place — the
difference between scanning 553 messages for a substring and being handed it. The light
theme was verified separately: the tint at alpha 26 is present without dominating, and the
mark stays legible because it carries its own foreground.

#### Risks

- **Over-decoration.** The current density is a feature. Tint alphas stay very low (14 dark
  / 26 light) and were verified in both themes; row height and rows-per-screen are
  unchanged.
- **Highlighting cost on wide messages.** Bounded by the 512-character scan window and the
  24-run cap, both covered by `EntryHighlightTests`.

#### Done when

- [x] An error row is identifiable from across the room; a verbose row is not shouting.
- [x] A search term is visible inside every matching message without reading it.
- [ ] An entry with an inferred timestamp is visibly distinguishable from one without.
- [x] Row height and rows-per-screen are unchanged.

---

### UX-3 · Lanes follow the severity filter

**Impact 4 · Effort S · Wave 1**

**Decision (2026-08-14): implemented, tested on a physical Android device, and kept.**

#### Problem (2.0.1 baseline)

The 2.0.1 heat map always draws six lanes (seven when the viewport contains Unknown entries),
regardless of which severities the filter includes. Evidence:
`TimelineControl.DisplayLevels` returns a fixed array; `QueryHeatMap` *skips* excluded
levels, leaving their arrays at zero.

The most common narrowing action in the product is "hide Verbose and Debug so I can see the
errors" — and its reward is **two blank lanes occupying a third of the plot's height**, with
the remaining signal squeezed into the rest.

There is a second, subtler defect in the same code: the lane count changes with
`HasUnknown`, which is computed **per viewport**. Panning into a region containing an
unparsed line silently switches the plot from six lanes to seven; every lane resizes and
the row the user was tracking moves under the pointer.

#### Proposal

- Lay out lanes over the **included** levels only. Hiding Verbose and Debug gives the
  remaining four lanes 50% more height each.
- Make the Unknown lane's presence a **session** property, not a viewport property, so the
  row count is stable while panning.
- Preserve the cell selection across a lane-count change when its level is still shown;
  drop it when it is not (the control already drops selections that leave the viewport).
- Optional refinement: when a hidden level still has entries in view, draw its label in the
  gutter as a thin, dimmed 8-pixel strip with its count, so hiding a severity does not hide
  the *fact* that it fired. This preserves the tool's honesty while spending almost no
  space.

#### Implementation

- `TimelineLevelLayout.Resolve` is the single pure policy for converting the included-level
  set plus session-wide Unknown availability into fixed-order visible lanes. An unconstrained
  filter retains all normal levels; an Unknown-only filter retains one explanatory empty
  lane even when the session has no Unknown entries.
- `TimelineControl.SetDisplayLevels` owns the resolved instance state. Every transform,
  draw and hit-test path uses that state; a selection survives if its level is still visible
  and clears—along with stale hover insight—if the level is hidden.
- `SessionWorkspaceView.UpdateTimelineLevels` derives Unknown availability from the immutable
  session snapshot's severity bitmaps, not the viewport heat map, and pushes changes both
  during timeline refresh and immediately after a `Filter` property change.
- `SessionTabViewModel.SetLevelAsync` clears a cell detail scope when its severity is hidden,
  preventing a stale table/plot scope from outliving the visible lane.
- `TimelineTransformTests` covers unconstrained session stability, filtered display order,
  and the Unknown-only empty-state behavior.

#### Physical-device result

Tested on a Motorola edge 60 pro running Android 16 (API 36), at 1220×2712 and 450 dpi,
with the same 1,000-entry log before and after. With Debug and Verbose disabled, both builds
reported **665 in view / 665 in session**. The 2.0.1 build still rendered six lanes, including
empty D and V rows; the candidate rendered only F, E, W and I. Each useful lane therefore
gained **50% height** in the same plot area without changing results. The layout remained
clear in portrait and landscape, and the candidate produced no fatal Android runtime logs.
The improvement passed the usefulness gate and was retained.

#### Risks

- Lanes jumping on every toggle could feel unstable. Mitigate by keeping the vertical order
  fixed (F→V, never reordered) so a lane only ever grows or disappears — never swaps places.
- The minimap's dominant-level coloring should keep using all levels, or a filtered minimap
  stops being a whole-session reference.

#### Done when

- [x] Hiding Verbose and Debug makes the Error and Fatal lanes visibly taller (confirmed
  on the physical device: six lanes → four, +50% per useful lane).
- [x] Panning cannot resize lanes merely by crossing Unknown-bearing data; lane membership
  is session-derived, with unit coverage for both session states.

---

### UX-4 · Two-way plot ↔ table ↔ template linkage

**Impact 4 · Effort S · Wave 1**

**Decision (2026-08-14): UX-4A — the selected-entry caret — implemented, tested on a
physical Android device, and kept. Template occurrence markers remain backlog with UX-9;
the "Show in timeline" affordance was dropped as unreachable, for the reason below.**

#### Problem

The links between the three main surfaces are one-directional:

| Action | Effect today |
|---|---|
| Click a timeline cell | Table scopes to that cell ✔ |
| Right-drag a range | Range actions appear ✔ |
| **Select a table row** | **Nothing happens on the plot ✘** |
| **Select a template** | **Nothing happens on the plot ✘** |
| Search | Markers drawn under the axis ✔ |

`PLAN.md` §14.9 explicitly requires "selection marker synchronized with the timeline", and
it is the one item of that list not implemented — `_entries.SelectionChanged` only triggers
the raw-context load (`SessionWorkspaceView.Interactions.cs`).

The result: the moment you start scrolling the table, you lose your position in the plot.
The two views stop being one workspace and become two lists.

#### Proposal

1. **Selected-entry caret.** Selecting a table row draws a thin vertical caret at that
   entry's instant across all lanes, with a small filled marker in the row of its severity.
   Clears when selection clears.
2. **Follow-selection.** If the selected entry falls outside the viewport (e.g. after
   *Load all* and a long scroll), show a one-click "Show in timeline" affordance rather than
   auto-panning — auto-panning while someone reads is hostile.
3. **Template occurrence markers.** Selecting a template in the Templates pane draws its
   occurrences as markers under the axis, in the same channel as search markers but visually
   distinct. This turns the template list from a frequency table into a *temporal* one:
   "this pattern fires only during the spike" is the single most useful thing a template
   list can tell you, and the query (`QueryTopTemplates` with representative entry ids)
   already returns enough to start.
4. **Hovering a template row** dims non-matching bars, or highlights matching columns —
   whichever profiles better.

#### Implementation of UX-4A, as shipped

- `TimelineControl.SetSelectedEntry(InstantUs?, LogLevel?)` holds the mark; `Render` draws
  it after the cell outline and before the hover readout, so nothing the pointer is doing
  is obscured by it. Three parts: a 1 px caret spanning every lane, a solid 2 px stub in
  the entry's own lane, and a down-pointing marker in the gutter above the plot. All three
  are in the entry's **severity color**, from `LevelPalette`'s cached brushes and a new
  cached `CaretPen` — so the mark cannot be confused with the magenta search ticks or the
  white cell outline, and it names the lane to look in even when that lane is filtered
  away. The gutter glyph is one `StreamGeometry` built once and placed by transform, never
  per frame.
- `SessionWorkspaceView.Interactions.cs` calls it from `_entries.SelectionChanged`, and
  clears it when the selection clears. An untimed entry marks nothing rather than guessing
  a position.

#### Why "Show in timeline" was dropped

The original proposal included an out-of-viewport affordance. It cannot be reached in this
architecture, and building it would have been chrome nobody could see: `RefreshAsync`
queries entries with `GetEntries(snapshot, detailRange ?? viewport.Value, …)`, so **a
listed row is always inside the viewport**, and any viewport change re-queries the list —
`Entries.Clear()` then repopulate — which drops the `ListBox` selection with it. This was
confirmed on the device: zooming in four steps took the table from `4,000 view` to
`28 view` and left no row selected.

Two consequences worth recording rather than re-deriving:

- The caret is correct but **transient across navigation**: it survives scrolling the
  table, which is the case the leak was about, and disappears on pan or zoom.
- Preserving the table selection across a refresh, when the selected entry is still in the
  refreshed page, is a small and worthwhile follow-up. It would make the caret durable and
  would make an out-of-viewport indicator meaningful for the first time. It belongs with
  UX-10 (navigation history), which has the same "what survives a navigation" question at
  its centre.

#### Remaining work

- Template occurrence instants need a query. Start with `RepresentativeEntryIds` (already
  on `TemplateSummary`); if that proves too sparse, add a bounded
  `QueryTemplateMarkers(snapshot, viewport, templateId, limit)` mirroring
  `SearchResult.Markers`, including its `MarkersTruncated` flag. Draw them from
  `_templates.SelectionChanged`.

#### Risks

- Marker channel crowding. Search markers, template markers, and (later) incident pins all
  want the strip under the axis. Budget that space now: search = magenta ticks below the
  axis, templates = hollow ticks above the axis, incidents = pins in the minimap frame
  (UX-6). Do not stack three tick rows. The selected-entry caret deliberately spends **no**
  budget in that strip — it lives in the plot body and the gutter above it.

#### Done when

- [x] Selecting any table row shows where it sits in the plot.
- [ ] Selecting a template shows *when* it happens.

---

### UX-5 · Make the plot self-explaining and directly tunable

**Impact 4 · Effort S · Wave 1**

#### Problem

Three separate expressions of the same issue: the settings that define what the picture
*means* are far from the picture, and the picture under-reports itself.

1. The plot header prints `EVENT DENSITY · 2.754 s · 738.7 µs/px · PER-ROW LOGARITHMIC` at
   10 px as **static text**. Intensity scale and normalization completely change how the
   image should be read, and changing them takes four clicks through
   *More → Appearance & timeline… → dropdown → OK*, in a modal that also holds unrelated
   settings.
2. **A dragged range does not report its duration.** `_rangeText` shows
   `start — end` (`SessionWorkspaceView.Interactions.cs`) and leaves the user to subtract
   two microsecond-precision timestamps by hand. "How long did that take?" is a first-class
   question in log analysis, and the answer is one string interpolation away.
3. **There is no intensity legend.** Bar height and opacity encode a count, and nothing on
   screen says what a full-height bar means. Under per-row normalization it means "this
   row's maximum in this viewport", which is not guessable.

#### Proposal

- **Make the header a control.** Clicking `PER-ROW LOGARITHMIC` opens a small popup:
  normalization (per-row / global), intensity scale (linear / sqrt / log), and a "what this
  means" line. Same values, same persistence, three clicks closer. Leave them in the
  Appearance dialog too.
- **Duration in every range readout.** `12:04:31.120 — 12:04:32.602 · 1.482 s` in the
  range-actions bar, and live while right-dragging inside the plot so the drag itself is a
  measuring tool.
- **Compact intensity legend** in the gutter beneath the lane labels: a short gradient with
  `0` and the current per-row (or global) maximum. It also silently teaches which
  normalization is active.
- **Session-boundary shading.** When the viewport extends past the session (permitted
  overscroll), tint the out-of-session area so "empty" is distinguishable from "no data
  here". Today a leading quiet period and off-the-end space look identical — visible in the
  project's own hero screenshot, where the left 40% of the plot is empty and the reader
  cannot tell whether that is silence or absence.

#### Implementation

- Header hit target: `TimelineControl` currently draws the header with `DrawText`; either
  hit-test the header rectangle in `OnPointerPressed` and raise a
  `DisplayOptionsRequested` event, or overlay a transparent `Button` in
  `SessionWorkspaceView.Build` above the timeline row. The overlay is simpler and keeps the
  control free of dialog concerns.
- Duration formatting already exists twice — `FormatDuration` in `TimelineControl` and
  `FormatSpan` in `SessionWorkspaceView.Presentation.cs`. Consolidate rather than add a
  third.

#### Done when

- A user can change normalization without opening a menu.
- Every range readout answers "how long".
- Nothing in the plot's chrome is an unexplained abbreviation.

---

### UX-6 · Guided incident navigation — *the headline feature*

**Impact 5 · Effort M · Wave 2**

#### Problem

VisualCat's promise is *"spot crash storms, error bursts, and quiet gaps without scrolling
through millions of lines"*. Today it renders a picture in which those things are
**visible if you look**, but nothing **points**. Per-row normalization actively works
against the user here: each lane is scaled to its own maximum, so a Verbose lane at steady
traffic renders as brightly as a Fatal lane with three events. The user does the anomaly
detection with their eyes, on every session.

This is the largest remaining gap between what the engine knows and what the interface says.

#### Proposal

An **incident strip** plus **jump-to-incident navigation**, built entirely on data already
resident in memory.

`SessionTabViewModel.Overview` is a whole-session heat map at 512 columns, cached per
(snapshot generation, filter) — evidence: `RefreshAsync` builds it with
`new Viewport(snapshot.TimedRange ?? viewport.Value, 512)`. That is 512 × 7 counts already
computed. Scoring it is microseconds of arithmetic over an array that is already there.

**Score each overview column with explainable, objective rules** — not a fuzzy anomaly
score:

| Rule | Trigger | Label |
|---|---|---|
| Fatal | any Fatal in the column | `3 fatal` |
| Error burst | error count ≥ *k* × the session's median non-zero error count | `error burst ×14` |
| Silence | total rate < 5% of the trailing baseline for ≥ *n* consecutive columns after sustained traffic | `42 s silence` |
| Onset | first non-zero column of Error or Fatal after a quiet stretch | `first error` |
| Volume spike | total rate ≥ *k* × trailing median | `18× traffic` |

Then:

- **Pins.** Draw incidents as labelled pins in the minimap frame, colored by the severity
  that caused them. This extends the existing fatal/error pulse ticks
  (`MinimapControl.Render`) rather than inventing a new visual language.
- **Navigation.** `]` / `[` jump to next/previous incident, centering the viewport on it
  and preserving the current span. A toolbar `Next incident ▸` button gives the same
  action a discoverable form.
- **A one-line "what am I looking at" strip** above the plot when an incident is focused:
  `Incident 2 of 7 · 12:04:31.120 · error burst ×14 · top tag AndroidRuntime`.
- **Explainability is mandatory.** Every pin says *why* it is a pin. An unexplained "AI
  found something here" marker that turns out to be nothing destroys trust in one use; a
  marker that says `3 fatal` is verifiable at a glance and is never wrong.

#### Why the effort is contained

No new query, no new store format, no new persisted state. The scoring pass is
`O(columns × levels)` ≈ 3,600 operations, recomputed only when `Overview` is recomputed
(which is already cached by filter). The novel work is the UI and the threshold tuning,
not the computation.

#### Risks and guardrails

- **False confidence.** If pins are wrong, the feature is worse than nothing. Ship only
  rules that are locally verifiable from the displayed counts, and keep every threshold in
  `ApplicationSettings` so they can be tuned without a rebuild.
- **Noise on healthy logs.** A log with 4,000 errors should not produce 400 pins. Cap the
  strip (e.g. 40 pins), merge adjacent columns into one incident, and rank by severity then
  magnitude.
- **Filter interaction.** Incidents must be computed from the *filtered* overview, so that
  hiding a noisy tag also removes its incidents. Since `Overview` is already filter-keyed,
  this comes free.
- **Scope discipline.** Resist "first-seen template" detection in v1; it needs a
  per-column template query that does not exist yet. Add it in a second pass once the
  navigation UI has proven itself.

#### Done when

- Opening a crash log and pressing `]` lands on the crash.
- Every pin's label is verifiable against the hover readout for that column.
- A clean log produces few or no pins.

---

### UX-7 · First-run: a sample session, a shortcut sheet, gesture hints

**Impact 4 · Effort S · Wave 1**

#### Problem

Three distinct first-run failures:

1. **You cannot try the product without a log.** The start page offers Open log, ADB live,
   and Reopen session (`MainView.BuildHeroActions`). Someone evaluating VisualCat from a
   GitHub release with no logcat handy has nothing to click. For a visual tool, "see it
   working in 5 seconds" is the entire conversion funnel.
2. **The interaction model is undiscoverable.** Wheel-zoom, drag-pan, right-drag-range,
   double-click-zoom, click-to-scope, `J`/`K`, `0`, `F`, `Home`/`End` — all implemented, all
   documented in `docs/KEYBOARD.md`, and **none discoverable in the application**. There is
   no help affordance anywhere in the UI; the only in-app hints are automation help text
   that sighted mouse users never hear.
3. **The empty timeline does not teach.** `SetEmptyState` produces good *status* copy
   ("Live capture is running") but never *capability* copy.

#### Proposal

- **"Try a sample log" on the start page.** `SyntheticLogGenerator` lives in
  `VisualCat.Core` (`src/VisualCat.Core/Generation/SyntheticLogGenerator.cs`) and is
  already used by `vcat generate-test-log`, so the app can generate a deterministic,
  interesting demo log in-process — no shipped asset, no new dependency, no privacy
  question. Seed it to contain a crash storm, an error burst, and a quiet gap, so the first
  thing a new user sees is the product doing the thing the tagline claims.
- **Shortcut sheet on `F1` / `?`.** A modal or overlay listing the same table that
  `docs/KEYBOARD.md` contains, grouped as Navigate / Filter / Inspect / Session. Generate
  it from one source shared with the docs so the two cannot drift.
- **A one-line gesture hint under the timeline** on the first few sessions:
  *Drag to pan · Wheel to zoom · Right-drag to select a range · Click a bar to list it*,
  with a dismiss that persists in `ApplicationSettings`.
- **A `?` button in the command bar** next to More, opening the shortcut sheet. One
  affordance, permanently discoverable.

#### Implementation

- `MainView.BuildHeroActions` and `BuildActionToolbar` for the entry points.
- A new `ShortcutSheetDialog` alongside the existing dialogs in `Views/SessionDialogs.cs`.
- The hint strip belongs in `SessionWorkspaceView.Build` between the chip bar and the
  timeline, sharing the chip bar's collapse behaviour.
- New settings: `SampleSessionOffered`, `GestureHintDismissed`.

#### Risks

- The sample must be unmistakably synthetic — label the tab and the session pane clearly —
  so nobody ever mistakes generated data for a real capture.

#### Done when

- A new user with no logcat sees a populated heat map within two clicks of launch.
- Every shortcut in `docs/KEYBOARD.md` is reachable from inside the app.

---

### UX-8 · One real notification surface, with actionable errors

**Impact 4 · Effort S–M · Wave 2**

#### Problem

Every non-fatal message in the application — failures, save confirmations, ADB not found,
startup settings errors, cleanup warnings — is written to **one ellipsized grey
`TextBlock` in the top-right of the brand row** (`MainView._message`, foreground
`#9EB1CB`, `TextTrimming.CharacterEllipsis`). It is cleared on the next action.

So: the lowest-contrast text in the window, truncated, in the corner furthest from where
the user is looking, transient. Messages that deserve better include
`"ADB was not found. Install Android platform-tools or set ANDROID_SDK_ROOT."` and
`"Could not complete that action · Access to the path is denied."`

Several are also **diagnoses without a remedy**. "ADB was not found" is exactly the moment
to offer *Locate adb…* — a file picker writing `ApplicationSettings.AdbPath`, which the
settings record already supports.

#### Proposal

- A **transient notification strip** below the command bar: severity-colored left edge
  (info / warning / error, using `LevelPalette` so the language is consistent), full text
  with wrapping, an optional action button, and a dismiss. Errors persist until dismissed;
  confirmations auto-fade.
- **Attach actions to the errors that have one:**
  | Message | Action |
  |---|---|
  | ADB not found | *Locate adb…* → writes `AdbPath` |
  | Session folder not a filesystem path (Android) | *Open portable archive…* |
  | Save/export failed | *Choose another location…* |
  | Startup source not found | *Open log…* |
  | Temporary cleanup left sessions | *Open session cache…* |
- Keep a **message history** (last ~20) reachable from the notification strip, so a
  message that scrolled past is recoverable. This is also the honest place to surface
  degraded-session and recoverable-defect states from `PLAN.md` §14.13.

#### Implementation

- `MainView`: replace `_message` with a `NotificationHost` control docked under the command
  bar; route `ReportFailure`, `ReportCancelled`, and the 14 direct `_message.Text =` sites
  through it.
- Workspace-level failures currently land in `SessionWorkspaceView._status` (the bottom
  status bar) via `RunUiActionAsync`. Route genuine failures to the same host so there is
  one error surface, not two.

#### Done when

- No user-facing message can be truncated into meaninglessness.
- Every error that has an obvious remedy offers it inline.

---

### UX-9 · Finish the template explorer

**Impact 4 · Effort M · Wave 2**

#### Problem

Template mining is VisualCat's most distinctive analytic asset — deterministic, tag-isolated
Drain clustering that turns 40,000 near-identical lines into one row. The pane presenting it
implements roughly half of `PLAN.md` §14.10:

| §14.10 requirement | State |
|---|---|
| Ranked templates for viewport and filter | ✔ |
| Count, first/last time | ✔ |
| Filter to / mute / copy | ✔ |
| Prevalence bar | ✔ (a nice addition beyond spec) |
| **Highlight `<*>` parameters** | **✘ — canonical text renders as plain text** |
| **Trend sparkline** | **✘** |
| Representative examples | ✘ (ids are returned, never shown) |
| Inspect example entries | ✘ |
| Pin the pane | Partial (Hide insights only) |

#### Proposal

- **Highlight `<*>` parameters** in the canonical text — dimmed, italic, or bracketed in the
  accent color. Nearly free (inline runs), and it makes the difference between reading
  `uid=<*>(com.example) identical <*> lines` as a pattern versus as a corrupted message.
- **Sparkline per template.** A 40 × 12 px density strip over the current viewport. The
  metric that matters is *shape*: "constant background noise" versus "only during the
  spike" is the single most decision-relevant property of a template, and a count cannot
  express it.
- **"New in this window."** Mark templates whose first occurrence falls inside the current
  viewport. When something breaks, the messages that were never there before are the
  evidence. `TemplateSummary.First` is already returned — for the *scoped* query — so a
  session-wide first-seen lookup is the only new data needed.
- **Click a representative example** to select that entry in the table (completing the
  linkage from UX-4).
- Once UX-1 lands, the templates pane also gains an explicit scope label, removing the
  current silent viewport/session mismatch.

#### Implementation

- Template row template lives in `SessionWorkspaceView.Panes.cs` `BuildTemplatePane`.
- Sparkline data: either a small per-template heat query, or bucket the representative ids.
  Prefer a bounded query so the pane's cost stays proportional to the ~20 visible rows.
- Watch the pane budget: the code comments already note that Fluent's `ProgressBar`
  `MinWidth` was stealing width from the canonical message. A sparkline must not repeat that
  mistake — fixed width, and the message stays the widest element.

#### Done when

- A template's temporal shape is readable without clicking it.
- Templates new to the current window are visually distinct.

---

### UX-10 · Navigation history (Back / Forward)

**Impact 3 · Effort M · Wave 2**

#### Problem

Investigation is a search tree: zoom in, filter, realize it is the wrong thread, back out,
try another branch. VisualCat has no back. `Escape` unwinds a fixed priority chain (mobile
filters → search → cell scope → filters, per `HandleEscapeAsync`) but cannot restore a
*previous* viewport, and no key restores a previous filter. Re-finding a range you were
looking at two minutes ago means re-deriving it.

`PLAN.md` §14.5 anticipates this: *"Zoom history, if exposed as Back/Forward, stores
semantic viewports and filters, never aggregate snapshots."*

#### Proposal

- A bounded (≈50) per-tab stack of `(viewport, filter, detailRange, detailLevel)` tuples.
- `Alt+←` / `Alt+→`, plus mouse buttons 4/5, plus small `‹ ›` buttons next to the zoom
  controls.
- Push on *committed* navigations only — a completed drag, a wheel-zoom gesture that has
  settled, a filter change, a cell selection — never on every intermediate frame. Coalesce
  by time (e.g. 400 ms of quiescence) so one wheel gesture is one history entry.
- Explicitly **not** persisted across restarts; saved views already cover durable state.

#### Implementation

- `SessionTabViewModel`: a `NavigationHistory` list plus `CanGoBack`/`CanGoForward`
  notifications. Restoring calls the existing `SetViewportAsync` and `UpdateFilterAsync`
  paths, so no new query shape is needed.
- The tricky part is *push discipline*, not storage: `SetViewportAsync` is called from
  wheel, drag, minimap, keyboard, search navigation, fit-to-matches, and follow-latest.
  Follow-latest advances must never enter history. A `manual` flag already exists on
  `SetViewportAsync` — use it as the gate.

#### Done when

- Back returns to the exact previous viewport *and* filter.
- A live capture in follow mode never fills the history.

---

## 6. Tier 2 — worth doing after Tier 1

### UX-11 · Entry inspector with quality flags · Impact 3 · Effort M

The Source Context pane shows raw bytes with a `▶` marker on the target line — correct and
byte-faithful, but it is not a *reading* surface. There is no view that presents one entry
as a structured record: full multi-line message with wrapping, all fields, resolved process
name, template with parameters, and the quality flags from `EntryAttributes`.

Add a compact inspector beside (not replacing) the raw pane, with per-field copy. Give
special treatment to the timestamp when `InferredTimestamp` or `LowTimestampConfidence` is
set — state *how* it was derived, using `TimestampProvenance`, which the entry already
carries and the UI never shows.

Also make the raw context radius adjustable; ±5 lines is hardcoded
(`LoadRawContextAsync(entry, before: 5, after: 5)`) and a stack trace is longer than that.

### UX-12 · Log-quality ribbon · Impact 4 · Effort M

The session pane counts `ChattyDeclaredDrops`, `ReconnectGaps`, `ReconnectDuplicates`,
`OutOfOrderEntries`, `LateSegmentEntries`, `EncodingFallbacks`, `LongLineOverflows`. These
are **time-located events reported as scalars**. "The log lied to you *here*" is precisely
what a time-based visualizer should draw.

Add a thin ribbon between the plot and the axis marking where drops, reconnect gaps, and
out-of-order arrivals occurred. Per-entry `EntryAttributes.Chatty` and `ReconnectDuplicate`
make the locations queryable. This is a real differentiator: most log viewers cannot tell
you that the gap you are staring at is missing data rather than silence — and VisualCat's
own promise mentions "quiet gaps" specifically.

### UX-13 · "Copy finding" · Impact 3 · Effort S · High ROI

The end of the loop is handing evidence to someone else. Today: copy raw lines, export CSV,
save portable, Android share sheet — all *data* exports, no *finding* export.

Add **Copy finding** to the range actions, producing pasteable Markdown:

```markdown
**app.log** — 12:04:31.120 → 12:04:32.602 (1.482 s)
Filter: levels E,F · tag = AndroidRuntime
41 entries · 3 fatal · 38 error

12:04:31.120 F AndroidRuntime  FATAL EXCEPTION: main
12:04:31.120 F AndroidRuntime  Process: com.example, PID: 9731
…
```

`ExportService` already has `ExportTemplateReportAsync` and `ExportStatisticsAsync` with
Markdown output for the CLI (`--type templates-md`, `stats-md`) — the formatting muscle
exists and only needs a clipboard target and a UI entry point.

### UX-14 · Status bar as session health · Impact 2 · Effort S

The status bar is one muted line (`Ready · 1,000 entries · snapshot 2`) plus a right-aligned
search status. Meanwhile the Session tab holds real health data nobody looks at.

Promote a **health chip** into the status bar: green when all defect counters are zero,
amber otherwise, with the top offender named and a click to the Session tab. During capture
show live throughput and elapsed time. Zero new data; better placement.

### UX-15 · Filter bar grouping and button hierarchy · Impact 3 · Effort S

The desktop filter bar is a single `WrapPanel` of fourteen controls — search box, two
checkboxes, Search, six severity toggles, three zoom buttons, a readout, Follow, New data,
Stop capture — with uniform spacing and no grouping, wrapping arbitrarily at narrow widths.

Add subtle separators between the three functional groups (query · severity · time lens —
names the mobile layout already uses for the same groups), let the search box flex, and give
the buttons a hierarchy. In the workspace every button is default Fluent grey: *Clear all*,
*Fit to matches*, *Load all*, *Stop capture*, and *Zoom range* have identical visual weight
despite very different consequences. The command bar already solved this with
primary/secondary styling (`MainView.ActionButton(primary:)`); reuse it.

### UX-16 · Persist workspace layout · Impact 2 · Effort S

`PLAN.md` §14.1 requires "persisted layout per user or saved view". Today only window size
and maximized state persist (`ApplicationSettings.WindowWidth/Height/Maximized`). Splitter
positions, insights-pane visibility, insights tab selection, raw-context expansion, and
entry order all reset every launch. Add them to `ApplicationSettings` (and, better, to
saved views, which already persist filter + viewport).

### UX-17 · Command palette · Impact 3 · Effort M

`Ctrl+K` over every command, saved view, tag, process, and template: "type `AndroidRuntime`,
press Enter, filter applied". This is the standard power-user accelerator and it composes
well with everything above. Ranked below the others only because it serves users who have
already learned the app, while Tier 1 serves everyone.

### UX-18 · Range A/B comparison · Impact 4 · Effort L

`PLAN.md` §14.10 lists "optional range A/B count-delta comparison after the core explorer is
complete". Pin range A, pin range B, show which templates/tags/processes appear, vanish, or
change rate between them. This is the most powerful analytic idea still unbuilt — "what is
different about the bad run" is the actual question behind most log analysis — but it needs
the template explorer finished (UX-9), a pinning model, and a comparison presentation. Do it
after Wave 2, not instead of it.

### UX-19 · Severity palette separation and contrast audit · Impact 2 · Effort S

The palette (`LevelPalette.BuildColors`) is well chosen overall, with two caveats:

- **Error `#FF5A5F` and Fatal `#FF2D68` are close in hue** — the two levels the user cares
  about most are the hardest pair to tell apart, which is visible in the project's own hero
  screenshot where the F and E lanes read as one band. Positional lanes and letter labels
  carry the meaning, so this is not a correctness bug, but pushing Fatal further (toward
  white-hot or magenta-violet) would make the most important distinction the easiest one.
- **Run a documented contrast pass** in both themes and in high contrast, covering the 9–10 px
  muted text used in the plot header, template timestamps, and facet counts. `RELEASE-CHECKLIST.md`
  already calls for manual contrast review; record the measured ratios so regressions are detectable.

Also verify the palette under deuteranopia and protanopia simulation. The design already
satisfies "color is not the only signal", so the goal is comfort, not correctness.

---

## 7. Android companion

The Android app shares `SessionWorkspaceView` with the desktop and diverges through
`_mobile` branches and `MobileWorkspaceLayout`'s three size classes. The three-mode
Plot/Split/Details selector is a sound answer to a phone-sized workspace, and touch targets
are handled deliberately (48 px minimums throughout).

Priorities for Android, in order:

1. **Inherit the Tier 1 wins that are size-independent** — severity color in the entry card
   (UX-2), lanes following the filter (UX-3), scope labelling (UX-1), range duration
   (UX-5). These are the same code paths.
2. **The phone's job is capture → triage → share**, not deep analysis. Optimize that path:
   after a capture stops, offer *Share portable archive* as a prominent next step rather
   than a toolbar item among many.
3. **Incident navigation (UX-6) is more valuable on a phone than on a desktop**, because
   scanning a plot on a 6-inch screen is far more expensive than on a 4K monitor. `]`/`[`
   becomes a pair of large buttons.
4. Do **not** port the command palette, A/B comparison, or navigation history to phones.

---

## 8. Deliberately not recommended

Recording rejected ideas is as useful as recording accepted ones.

| Idea | Why not |
|---|---|
| **Rewrite the code-built UI as XAML + MVVM bindings** | A large, risky refactor with zero user-visible improvement. The code-built approach has real costs (theme handling is manual, there is no style system, `SessionWorkspaceView` is ~3,700 lines across seven partials) but those are maintainability costs, and paying them down should be incremental and driven by feature work — never as a standalone "modernization" project. |
| **Adopt a charting library for the timeline** | The custom control exists because the frame budget and the interaction model demand it. No general library will meet `PLAN.md` §19's 8 ms full-view budget at a million entries. |
| **Animated transitions on zoom/pan** | Directly conflicts with the frame budget and with `PLAN.md` §14.14's reduced-animation requirement. A one-frame redraw at 60 fps already reads as smooth. |
| **Cloud sharing / hosted permalinks for sessions** | Violates the local-first, no-upload guarantee in `docs/PRIVACY.md`. Portable `.vcat.zip` is the sharing story. |
| **Usage analytics to prioritize UX work** | Prohibited by ADR 0017. See §11 for what to do instead. |
| **AI-generated log summaries** | Would require sending log content off-device (privacy violation) or bundling a model (size, and non-deterministic output in a forensic tool). If ever revisited, it must be local, optional, and clearly labelled — and UX-6's *explainable rules* deliver most of the same value with none of the cost. |
| **A settings page for everything** | The current settings dialogs are already at the edge of useful. New display controls belong next to the thing they control (UX-5), not in a growing modal. |
| **Reordering or hiding entry-table columns via a layout editor** | High build cost, low frequency of use. The adaptive time column and flag column in UX-2 solve the actual complaints (wasted width, missing information) for a fraction of the effort. |

---

## 9. Cross-cutting principles for this work

Apply these to every item above; they are what make the result feel like one product rather
than a pile of features.

1. **State the scope of every number.** A count without a stated population is a bug. This
   is UX-1 generalized into a rule.
2. **Never let color be the only carrier** (ADR 0013). Every new color-coded state also
   needs a label, glyph, position, or count.
3. **Put the control next to the thing it controls.** Distance from artifact to setting is
   a UX cost; the plot's scale controls belong on the plot.
4. **Explain, don't just indicate.** A marker that says *why* it exists is trusted; one that
   says only *something is here* is disbelieved after its first false positive.
5. **Prose in the UI is a design smell.** The facet pane's explanatory paragraph exists
   because a control is missing. When you find yourself writing instructions on a panel,
   consider whether the panel should have a switch instead.
6. **Never move things under the user's pointer.** The facet pane already restores scroll
   offset and pins active values for this reason; UX-1 and UX-3 must uphold it.
7. **Never auto-scroll a surface the user is reading.** Offer "Show in timeline"; do not pan
   for them. (Follow-latest is the explicit, opt-in exception.)
8. **Density is a feature.** This is a professional tool for large datasets. Every addition
   must justify the vertical space it takes, and default to the smallest form that works.
9. **Every desktop addition needs an explicit mobile decision** — port, adapt, or omit — made
   at design time, not discovered at build time.
10. **Theme through `WorkspacePalette` and `LevelPalette`.** No new hardcoded hex in views.
    (Several literals already leak — `#2C4361`, `#223650`, `#111C2D`, `#304F7199` — and each
    new one costs a light-theme bug later.)

---

## 10. Suggested sequencing

### Wave 1 — "the workspace answers the right question" (small, safe, immediate)

UX-1 (scope) → UX-3 (lanes) → UX-5 (plot self-explanation) → UX-4 (linkage) → UX-2 (table)
→ UX-7 (onboarding)

Every item is S or S–M, none touches the engine or the store format, and together they
remove the most frequently paid costs in the loop. UX-1 and UX-3 first because they change
what the other items are laid out around.

**Progress, 2026-08-14.** UX-1A, UX-3, UX-2A and UX-4A are in. What is left of Wave 1 is
UX-5, UX-7, the UX-2 time column and quality flags, and the UX-1 scope selector. UX-4A
surfaced one new small item worth pulling in early: **preserve the entry-table selection
across a refresh when the selected entry is still in the page**, which makes the caret
durable across pan and zoom and unblocks the out-of-viewport affordance UX-4 originally
proposed.

### Wave 2 — "the tool guides" (the differentiators)

UX-6 (incident navigation) → UX-9 (template explorer) → UX-13 (copy finding) →
UX-8 (notifications) → UX-10 (history)

UX-6 is the headline; it lands better after Wave 1 because incident pins need a plot whose
lanes and scope already behave. UX-13 is deliberately dropped in the middle — it is an S
that closes the loop's last step and can ship any time it fits.

### Wave 3 — depth and polish

UX-12 (quality ribbon) → UX-11 (inspector) → UX-14/15/16/19 (polish batch) →
UX-17 (command palette) → UX-18 (A/B comparison)

### A note on batching

UX-14, UX-15, UX-16 and UX-19 are each individually small and individually low-impact. Doing
them as one deliberate "workspace polish" pass produces a step change in perceived quality
that none of them delivers alone, and it is the natural moment to clean up the leaked hex
literals and the duplicated duration formatters noted in §9.

---

## 11. How to know it worked, without telemetry

ADR 0017 forbids shipping analytics, so success has to be measured deliberately and locally.

**Task-based self-testing.** Keep a fixed set of investigation tasks against a fixed corpus
(`vcat generate-test-log` produces deterministic, seeded logs, so the corpus is reproducible
and shareable without privacy concerns). Time them before and after a change:

| Task | Metric |
|---|---|
| Find the first fatal in a 1M-line log | Seconds from launch to the crash on screen |
| Identify the tag responsible for the largest error burst | Seconds, and number of clicks |
| Measure the duration of a startup sequence | Seconds, and whether arithmetic was needed |
| Produce a pasteable evidence snippet for a bug report | Seconds, and number of applications used |
| Do all of the above with the keyboard only | Possible / impossible |

**Interaction-cost counting.** For each task, count clicks, keystrokes, and pane switches
before and after. This is a proxy that needs no instrumentation and catches regressions that
timing hides.

**A first-run stopwatch.** Time from launching a fresh install to a populated heat map on
screen. UX-7 should move this from "however long it takes to find a logcat" to under ten
seconds.

**Accessibility gates.** `docs/RELEASE-CHECKLIST.md` already requires manual keyboard,
screen-reader, contrast, and text-scaling verification per platform. Every item above adds
to that checklist rather than assuming existing coverage — headless CI covers focusable
composition and interaction behaviour, not assistive technology.

**Qualitative signal.** GitHub issues and Play Store reviews are the only user feedback
channel a no-telemetry product has. Tag them by loop step (open / read / zoom / narrow /
read lines / hand off) and let the distribution inform the next wave.

---

## 12. Implementation and validation record

### 12.1 Pass 1 — 2026-08-14

#### Shipped in the candidate

| Change | Robustness boundary | Result |
|---|---|---|
| UX-1A pane scope headers | Presentation-only; tooltip and automation name included; query meaning unchanged | **Keep** |
| UX-3 filter-driven lanes | Pure layout policy; fixed severity order; session-stable Unknown membership; stale hidden-level selection cleared | **Keep** |

#### Automated verification

- `dotnet test tests/VisualCat.App.Tests/VisualCat.App.Tests.csproj --no-restore` — **52/52
  passed**, including three new lane-policy tests plus headless assertions for stale-detail
  cleanup and both accessible scope labels.
- `dotnet test VisualCat.slnx --no-restore` — **160/160 passed** across App, Application,
  Core and Domain.
- `dotnet build src/VisualCat.Android/VisualCat.Android.csproj -c Debug
  -f net10.0-android36.0 -p:EmbedAssembliesIntoApk=true --no-restore` — **succeeded with
  zero warnings**. Embedding assemblies produced a standalone APK suitable for direct ADB
  installation rather than IDE fast deployment.

#### Physical-device protocol and observations

The baseline was the installed Google Play-signed 2.0.1 app. The candidate had a different
debug signature, so the existing package and its app data were removed with explicit user
authorization, then the standalone candidate was installed. Test device: Motorola edge 60
pro, Android 16/API 36, 1220×2712, density 450.

1. Opened the same 1,000-entry sample in baseline and candidate.
2. Disabled Debug and Verbose and confirmed the same **665 / 665** result counts.
3. Compared the plot: baseline kept six rows with two empty; candidate used four useful
   rows, producing the expected 50% per-lane height gain.
4. Opened Templates and Facets. **COUNTS · THIS VIEW** and **COUNTS · WHOLE SESSION** were
   both legible and present in the Android automation tree.
5. Repeated the filtered plot and Facets checks in landscape, then restored the device's
   original rotation settings.
6. Checked Android runtime logs after interaction; there were no fatal application errors.

The changes were retained because they made the existing output easier to interpret without
changing the output itself. The optional hidden-level gutter strip was not implemented: the
visible off-state toggles already communicate exclusion, while another miniature channel
would add complexity without evidence of additional value.

### 12.2 Pass 2 — 2026-08-14

#### Shipped in the candidate

| Change | Robustness boundary | Result |
|---|---|---|
| UX-2A severity edge + notable-row tint | Cached `LevelPalette` brushes only; edge drawn inside the existing column-0 gutter, so `EntryColumns` and header alignment are untouched; letter column retained | **Keep** |
| UX-2A search marking | Pure, unit-tested span function; 512-char scan window and 24-run cap; regex path reuses the engine's compiled instance and honours its timeout; unmarked text keeps the plain single-string block | **Keep** |
| UX-4A selected-entry caret | One `O(1)` addition to `Render`; cached pens and one prebuilt glyph, no per-frame geometry; untimed entries mark nothing | **Keep** |
| UX-4A out-of-viewport arrow | Unreachable: the entry list is viewport-scoped and any viewport change drops the selection | **Dropped before shipping** |
| UX-2 row hover band | Fluent already supplies a pointer-over background | **Dropped, not built** |

#### Automated verification

- `dotnet test tests/VisualCat.App.Tests/VisualCat.App.Tests.csproj` — **62/62 passed**
  (was 52), adding ten `EntryHighlightTests` cases — literal and regex matching, case
  sensitivity, non-overlapping runs, the scan-window boundary in both directions, the run
  cap, zero-width regex termination, and an invalid pattern marking nothing rather than
  throwing — plus headless assertions that the timeline receives and releases the selected
  entry's instant and level, and that marked runs appear in realized rows.
- `dotnet test VisualCat.slnx` — **170/170 passed** across App, Application, Core and Domain.
- `dotnet build src/VisualCat.Android/VisualCat.Android.csproj -c Debug
  -f net10.0-android36.0 -p:EmbedAssembliesIntoApk=true` — **succeeded with zero warnings**.

#### Desktop protocol

Same device-independent build, run against a deterministic
`vcat generate-test-log --seed 42 --lines 4000` sample.

1. Dark theme: confirmed the severity ribbon on every row, the tint on Warn/Error/Fatal
   only, and unchanged column alignment against the header.
2. Applied `package`: the term is marked in place in every matching row.
3. Selected a row: the caret, in-lane stub and gutter marker appeared at that entry's
   instant, in the entry's severity color.
4. Light theme (via `settings.json`, restored afterwards): the tint at alpha 26 reads
   without dominating, the mark stays legible because it carries its own foreground, and
   the caret and marker remain visible over the light plot ground.

#### Physical-device protocol and observations

Same device as pass 1. The installed package was already the pass-1 debug build, so the
candidate was installed in place with `adb install -r`, which preserved app data — no
uninstall was needed.

1. Opened the 4,000-entry sample. The mobile cards carry the severity edge, and the Warn
   tint is clearly distinguishable from untinted Info and Debug cards.
2. Searched `AndroidRuntime` → **0 matches**, correctly: it is a tag, and message search
   does not match tags. Searched `Connection` → **553 matches**, with the term marked in
   place in every visible card.
3. Confirmed the marking magenta is the same color as the search ticks under the plot
   axis — the intended single channel, visible in one screen.
4. Tapped an Error card in Split mode: a coral caret spanned all six lanes with a coral
   gutter marker, matching the entry's severity.
5. Zoomed four steps: the table re-scoped from `4,000 view` to `28 view` and lost its
   selection, which is what established that the out-of-viewport arrow is unreachable. It
   was removed from the candidate and the candidate rebuilt and reinstalled.
6. Repeated in landscape: marking, ribbon, tint and a Warn-colored caret all held. Restored
   the device's original rotation settings.
7. Checked the crash and main log buffers: no fatal application errors and no managed
   exceptions.

All three retained changes were kept because each was independently visible on the device
and none altered a single count: `553 view · 553 session` before and after, and the same
plot. The one thing this pass found and did *not* fix is recorded in UX-4 above — the entry
table drops its selection on every pan and zoom, which is worth fixing on its own merits.

---

## Appendix A · Where to change what

```
src/VisualCat.App/
├── App.cs                          application setup, high-contrast flag
├── Platform/
│   ├── PlatformSourceRegistry.cs   host↔app hooks (launch files, share, on-device source)
│   └── StorageFileBridge.cs        picker → filesystem materialization
├── Presentation/
│   ├── WorkspaceViewModel.cs       tabs, import/capture orchestration, progress, status text
│   └── SessionTabViewModel.cs      per-session state, all query orchestration, saved views
├── Timeline/
│   ├── TimelineControl.cs          heat map: render, hit test, hover readout, keyboard      ← UX-3,4,5,6
│   ├── TimelineLevelLayout.cs      pure filter/session → visible-lane policy                ← UX-3
│   ├── MinimapControl.cs           whole-session density + viewport brush                    ← UX-6
│   ├── TimelineTransform.cs        pure instant↔x, level↔y, pan/zoom (property-tested)      ← UX-3
│   ├── TimelineBars.cs             minimum bar width, run detection, column snapping
│   ├── NiceTicks.cs                1–2–5 axis interval selection
│   └── LevelPalette.cs             severity colors + cached brushes; WorkspacePalette        ← UX-19
└── Views/
    ├── MainView.cs                 shell, command bar, start page, settings, `_message`      ← UX-7,8
    ├── EntryHighlight.cs           pure search-term span matcher for entry rows              ← UX-2
    ├── SessionWorkspaceView.cs               layout, filter bar, chip bar, panes wiring      ← UX-5,15
    ├── SessionWorkspaceView.Interactions.cs  entry table template, wiring, search debounce   ← UX-2,4
    ├── SessionWorkspaceView.Presentation.cs  summary/status text, timeline refresh           ← UX-1,5
    ├── SessionWorkspaceView.Facets.cs        chips, facet rows, severity toggle painting     ← UX-1
    ├── SessionWorkspaceView.Panes.cs         facets/templates, saved views, session info     ← UX-1,9
    ├── SessionWorkspaceView.RawContext.cs    source-context pane                             ← UX-11
    ├── SessionWorkspaceView.Mobile.cs        mobile recomposition                            ← §7
    ├── MobileWorkspaceLayout.cs              phone size classes and display modes
    ├── AdbCaptureDialog.cs                   device/buffer selection
    ├── ImportPreviewDialog.cs                format/timestamp preview
    └── SessionDialogs.cs                     appearance, cache, recent, confirmation         ← UX-7

src/VisualCat.Core/Query/SessionQueryEngine.cs   heat map, statistics, entries, templates,
                                                 search, raw context — all pure, snapshot-based
src/VisualCat.Core/Generation/SyntheticLogGenerator.cs   deterministic demo data              ← UX-7
src/VisualCat.Application/UseCases/ExportService.cs      raw/CSV/Markdown export              ← UX-13
src/VisualCat.Infrastructure/Configuration/SettingsStore.cs  ApplicationSettings              ← UX-7,16
tests/VisualCat.App.Tests/                       headless workspace, transform, layout tests
docs/KEYBOARD.md                                 shortcut reference (source for UX-7's sheet)
docs/design/PLAN.md                              §14 is the original UX specification
```

---

## Appendix B · Evidence index

Specific observations in the `2.0.1` codebase that this document rests on. Re-verify before
acting; line numbers drift.

| # | Observation | Location |
|---|---|---|
| B1 | Statistics/facets are queried session-wide; templates are queried viewport-wide; both render in adjacent tabs | `SessionTabViewModel.RefreshAsync`, `ScheduleTemplateRefresh` |
| B2 | In 2.0.1, the facet pane explained its scope in 10 px prose; UX-1A replaced that with explicit Facets and Templates scope headers | `SessionWorkspaceView.Panes.cs` → `BuildFacetPane`, `BuildTemplatePane`, `CountScopeLabel` |
| B3 | In 2.0.1, `DisplayLevels` was a fixed 6- or 7-element array and excluded levels rendered as empty lanes; UX-3 now resolves included lanes explicitly | `TimelineLevelLayout.Resolve`, `TimelineControl.SetDisplayLevels` |
| B4 | In 2.0.1, `HasUnknown` was computed per viewport; UX-3 now derives lane membership from the session snapshot's Unknown bitmap | `SessionWorkspaceView.Presentation.cs` → `UpdateTimelineLevels` |
| B5 | In 2.0.1, entry-table selection did not notify the timeline; UX-4A now pushes the entry's instant and level to the plot | `SessionWorkspaceView.Interactions.cs` → `_entries.SelectionChanged`; `TimelineControl.SetSelectedEntry` |
| B6 | In 2.0.1, no search-term highlighting existed anywhere in the view layer; UX-2A adds a bounded, pure matcher shared with the engine's compiled regex | `EntryHighlight.Match`; `SessionQueryEngine.CompileSearchRegex` |
| B7 | In 2.0.1, message cells were plain `TextBlock`s with the first line only; they now carry inline marked runs when a search is active | `BuildDesktopEntryTemplate` → `MessageCell` → `HighlightedText` |
| B7a | The entry list is queried over the viewport and cleared on every refresh, so a selected row is always in view and no selection survives a pan or zoom | `SessionTabViewModel.RefreshAsync` → `GetEntries(snapshot, detailRange ?? viewport.Value, …)`, `Entries.Clear()` |
| B8 | `EntryAttributes` flags are per-entry but surface only as aggregate counts | `LogEntries.cs`; `SessionWorkspaceView.Panes.cs` → `UpdateSessionInfo` |
| B9 | Range selection reports start and end but not duration | `SessionWorkspaceView.Interactions.cs` → `_timeline.RangeSelected` |
| B10 | Intensity scale and normalization are drawn as static header text | `TimelineControl.Render` header composition |
| B11 | The whole-session overview is already computed and cached at 512 columns | `RefreshAsync` → `new Viewport(snapshot.TimedRange ?? viewport.Value, 512)` |
| B12 | The minimap already draws Fatal/Error pulse ticks per column | `MinimapControl.Render` |
| B13 | All application messages route to one ellipsized `TextBlock` in the brand row | `MainView._message`, `ReportFailure` |
| B14 | The synthetic log generator lives in Core and is reachable from the app | `src/VisualCat.Core/Generation/SyntheticLogGenerator.cs` |
| B15 | Markdown report export already exists for templates and statistics | `ExportService.ExportTemplateReportAsync` / `ExportStatisticsAsync`; `docs/CLI.md` `--type templates-md`, `stats-md` |
| B16 | No in-app help affordance exists; shortcuts live only in `docs/KEYBOARD.md` and automation help text | `MainView.BuildActionToolbar`, `SessionWorkspaceView` constructor |
| B17 | Only window size and maximized state persist; splitters and pane state do not | `ApplicationSettings`, `PersistWindowStateAsync` |
| B18 | Raw context radius is hardcoded to ±5 lines | `SessionTabViewModel.LoadRawContextAsync(entry, before: 5, after: 5)` |
| B19 | `<*>` template parameters render as plain text | `SessionWorkspaceView.Panes.cs` → `BuildTemplatePane` canonical `TextBlock` |
| B20 | `SetViewportAsync` already distinguishes manual from automatic navigation | `SessionTabViewModel.SetViewportAsync(TimeRange, bool manual = true)` |
