# VisualCat Android companion — UX/UI audit, second pass

A hands-on walkthrough of the Android build on a physical device, after the first audit's
fixes landed. Everything below was observed on the device, not inferred from the source;
where a cause is named, it was confirmed by reading the code afterwards.

**Build under test:** `com.barebit.visualcat`, debug, built from `main` at
`Answer the Android companion's UX audit, finding by finding` (a7e68d8), installed fresh
over the previous build.

**Device:** Samsung SM-G990B (Galaxy S21 FE), Android 16 (SDK 36), 1080 × 2340 at
480 dpi (3.0×), three-button navigation, system locale Czech, system theme dark unless a
finding says otherwise.

**Method:** synthetic taps and swipes via `adb shell input`, screenshots via
`adb exec-out screencap`, accessibility tree via `uiautomator dump`, contrast measured
from the screenshot pixels using the WCAG 2.x relative-luminance formula. Coordinates in
this document are device pixels unless stated otherwise.

Findings are grouped by kind and ordered by severity within each group. Each has a
heading you can quote, what was seen, why it matters, and — where it was cheap to
establish — where it comes from.

---

## Contents

- [A. Severe](#a-severe)
- [B. Accessibility](#b-accessibility)
- [C. Functional](#c-functional)
- [D. Layout and visual](#d-layout-and-visual)
- [E. Copy and consistency](#e-copy-and-consistency)
- [F. What already works](#f-what-already-works)
- [Suggested order of work](#suggested-order-of-work)

---

## A. Severe

### A1. The light theme is broken in three separate ways

Three distinct defects share the theme system as their home, and any one of them makes the
light theme unusable.

**A1a — The command bar is hard-coded dark.** The top bar carrying *Open log*, *Live* and
*More* paints a fixed gradient with no reference to the theme variant:

```
MainView.cs:275–290
Background = new LinearGradientBrush { ... #111C2D → #0B1220 },
BorderBrush = new SolidColorBrush(Color.Parse("#243753")),
```

In light mode this is a near-black band (#0C1422 measured) between a white system status
bar and a white page (#F4F7FC). It is present on a **cold start** in light mode, so it is
not a theme-change artefact — the bar has no light appearance at all.

**A1b — A live theme change repaints only four things.** `MainView.ApplyThemeSurfaces()`
(MainView.cs:198) is the whole response to `ActualThemeVariantChanged`, and it touches the
root background, the system bar, the empty state and the notice lane. The command bar, the
session tab strip, the timeline, the minimap, the entry rows and every muted label keep the
palette they were built with. Switching the system to light while the app is running
produces:

| Element | Foreground | Background | Contrast |
|---|---|---|---|
| Active session tab label | `#EAF2FF` | `#D4EAFD` | **1.10 : 1** |
| Timeline axis label | `#EAF2FE` | `#F4F7FC` | **1.05 : 1** |
| Selected entry row metadata | `#F8FAFD` | `#E9EFF7` | **1.11 : 1** |

At those ratios the text is not "hard to read", it is not visible. The minimap stays a
solid dark-navy rectangle in the middle of a white page. Only restarting the app fixes it.

**A1c — Even a cold start in light mode leaves the entry metadata line at 2.17 : 1.** The
row's secondary line renders with `TextMuted(dark)` = `#8FA5C4` on `#E9EFF7`. WCAG AA asks
4.5 : 1 for text this size; this does not reach even the 3 : 1 large-text floor. The
correct value exists — `LevelPalette.TextMuted(light)` is `#54647A`, which measures about
5 : 1 — it simply is not the one used.

The mechanism behind A1b and A1c is the same: theme-dependent brushes are resolved once,
when a view is constructed, and captured into styles and data templates that are never
rebuilt. `SessionWorkspaceView.MetadataLineStyle` and `BuildMobileEntryTemplate` each open
with `var dark = ActualThemeVariant != ThemeVariant.Light;` and bake the result into a
`Setter` / a `SolidColorBrush`. `SessionWorkspaceView.ApplyThemeSurfaces` does not
re-create them.

**Fix shape:** make the command bar theme-aware; extend both `ApplyThemeSurfaces` methods
to rebuild every brush they own, including the entry item template, the two metadata-line
styles, the timeline and the minimap; and re-run the whole path once after the top level
has settled its variant, so a cold start in light mode cannot be served dark values.

---

### A2. The log itself gets less than one row of the screen

The entries list is what the product is for, and it is the smallest thing on screen.
Measured from the accessibility tree, portrait, *Split* mode, on a 2340 px-tall display:

| State | Entries viewport | One row |
|---|---|---|
| Split, portrait | **173 px** | 192 px |
| Split, portrait, with a notice showing | **60 px** | 192 px |
| Split, landscape | **106 px** | 173 px |
| Details, portrait | ~700 px (about 4 rows) | 192 px |

In *Split* the reader sees a fraction of one log line. In landscape — where the two-column
composition should be an improvement — the detail column is 415 px tall and the pane chrome
takes 309 of it.

Where the height goes in portrait (device pixels, of 2160 usable):

| Band | Height |
|---|---|
| Command bar (Open log / Live / More) | 144 |
| Session tab strip | 159 |
| Mode buttons — **two** rows of 144 | 306 |
| Filter summary chip ("No filters · showing everything in view") | ~100 |
| Timeline heat map | 364 |
| Minimap | 162 |
| Entries / Insights / Entry tab headers | 143 |
| Count line + sort combo + Copy/Entry + Load next 500 | 332 |
| Status line | 100 |
| **Left for entries** | **173** |

Three of those are avoidable rather than intrinsic:

- **The mode group wraps.** *Filters · Plot · Split · Details* measure 852 px against 972 px
  of width; adding *Fit* needs 1038, so *Fit* falls to a second full-height row. 306 px for
  five buttons.
- **"Load next 500" occupies a full 144 px row above the list.** It is a footer action — it
  means "extend the list you are at the end of" — and it is placed where it costs the most
  and means the least.
- **The count line, the sort combo and the two action buttons are three separate bands.**

**Fix shape:** treat the entries pane as the thing that must clear a minimum (say four rows
in Split, six in Details) and let the plot, the minimap and the chip bar give way to it;
fold *Fit* into the mode row or move it onto the plot; move *Load next 500* to the end of
the list.

---

### A3. The scrollbar sits on top of the content and steals taps from it

Every scrolling surface in the product — the Appearance & timeline sheet, the Session cache
sheet, the Filters pane — lays its content out to the full width and then draws the
`ScrollViewer`'s bar over the last ~48 px of it.

In *Appearance & timeline*, content spans x 87–993 and the scrollbar's regions occupy
x 945–993. The visible consequence is a thin vertical line running through the right edge
of the combo boxes and through the body copy under *Minimum bar width*. The invisible
consequence is worse: the bar's `Page down` repeat button is a **718 px-tall hit target**
(`[945,1148]–[993,1866]`) covering the right edge of the *Timeline intensity scale* and
*Timeline normalization* combos — that is, covering their chevrons.

Verified: a tap at (965, 1475), squarely on the *Timeline intensity scale* chevron, paged
the form down instead of opening the dropdown.

**Fix shape:** reserve the scrollbar's width in the scrolling content's padding, or use an
overlay bar that does not take pointer input.

---

### A4. Combo dropdowns inside an in-page dialog open in the wrong place

Tapping the *Default export order* combo (on the body, not the chevron) scrolled the form
back to the top and rendered its popup floating over *Timeline normalization* — while the
control the popup belongs to had scrolled off screen entirely. The reader is shown a
detached list of *Source order / Chronological* over unrelated fields.

This affects every `ComboBox` in the in-page sheet presentation, which is the presentation
Android always gets.

Secondary: the highlighted item in that popup is a solid accent slab, the Fluent default
that the first audit replaced with a tint-and-outline everywhere else. `ComboBoxItem` was
not covered.

---

## B. Accessibility

### B1. Two lists announce their raw C# records

The entries list gets this right — `ContainerPrepared` sets a readable name per row
("Verbose WindowManager at 08-18 20:29:59.886931: Remove Window{…}"). The same treatment
was not applied to the other two lists, so their `ListBoxItem`s fall back to the record's
generated `ToString()`.

**Insights** announces:

```
TemplateSummary { TemplateId = 16, CanonicalText = ViewPostIme pointer <*>, Count = 2,
First = 2026-08-18T18:57:05.9123350+00:00, Last = 2026-08-18T18:57:05.9168380+00:00,
RepresentativeEntryIds = System.Collections.Generic.List`1[System.Int64] }
```

**Recent sessions** announces — and this one also leaks the private storage path and the
session guid that the first audit worked to keep out of user-visible names:

```
TemporarySessionInfo { Path = /data/user/0/com.barebit.visualcat/files/VisualCat/Sessions/
20260818-185535-On-device logcat-edd2cb30f9f144c5b36304f05ad55b13.vcat,
UpdatedUtc = 18.08.2026 18:57:05 +00:00, SizeBytes = 35460, Finalized = True }
```

Sighted readers see a clean two-line row in both cases.

---

### B2. The status row's accessible description is frozen at its first value

The session status row's `content-desc` keeps whatever the status was when the row was
first built, forever. Observed across two sessions and a relaunch:

| Visible text | Announced description |
|---|---|
| `Capturing · 23 lines received · 1/s · On-device full-device logcat` | `Starting capture · On-device full-device logcat` |
| `Capturing · 24 lines received · no source lines for 40s · …` | `Starting capture · On-device full-device logcat` |
| `Ready · 25 entries` | `Starting capture · On-device full-device logcat` |
| `Ready · 59 640 entries` | `Importing…` |

A screen-reader user is told a finished session is still importing.

The distinguishing detail, which points straight at the fix: the containing `DockPanel`'s
help text **does** update ("Tap to show the whole status line." ⇄ "Tap to shorten the status
line."), and that one is set with an explicit `AutomationProperties.SetHelpText`. The
`TextBlock`'s is derived from `ToolTip.SetTip` in
`SessionWorkspaceView.Presentation.cs:RefreshPresentation`, and the platform node keeps the
first value it read. Setting `HelpText` explicitly, as the DockPanel does, is the reliable
route.

---

### B3. Numeric spinner buttons have no accessible name

Every `NumericUpDown` increment/decrement button reports `Avalonia.Controls.PathIcon` as
its name. That is six buttons in *Appearance & timeline* (Text scale, Live UI refresh
limit, Maximum zoom precision) and two in *Session cache* (Maximum age, Maximum total
size).

---

### B4. Content behind a modal sheet stays reachable

With the command sheet open — and with any dialog open — every control of the workspace
underneath is still reported `clickable="true" enabled="true"` in the accessibility tree:
*Open log*, *Live*, *More actions*, the mode buttons, *Load next 500*, the entry rows.

The scrim catches pointer input, so touch is safe. Accessibility traversal is not: nothing
marks the sheet as modal, so assistive technology walks straight past the scrim into the
page behind it and can activate things there.

**Fix shape:** while an overlay is up, take the workspace host out of the accessibility
tree (`AutomationProperties.AccessibilityView = Raw`, or an equivalent "inert" flag) and
restore it when the last overlay is removed.

---

### B5. The app ignores the system font scale

Setting `font_scale` to 1.3 — the ordinary Android accessibility control for text size —
produced a **pixel-identical** app. Every size in the product is a fixed logical value.

The product does have a *Text scale* setting, but it is the sixth control inside
*More → Appearance & timeline*, and a reader who has already told the OS they need larger
text has no reason to expect the app to have its own switch.

**Fix shape:** honour `FontScale` from the platform as the baseline, and let the in-app
*Text scale* multiply it rather than replace it.

---

## C. Functional

### C1. Declining Android's log-access consent is silently absorbed

On Android 13+, `READ_LOGS` alone is not enough: the system shows a per-request consent
dialog whose only affirmative option is **one-time** access. So the dialog appears every
single time *Live* is tapped, and *Live* gives no warning that it is coming.

Declining does not stop the capture. The app starts a session anyway, receives only its own
process's logs, and keeps describing the source as **"On-device full-device logcat"** in the
status line. After 40 seconds the session held 24 raw lines and 1 parsed entry. The only
signal that something is wrong is the fragment `no source lines for 40s`, buried in a status
line that is clipped to one line by default.

Nothing tells the reader that they declined, what they lost, or how to grant it. The notice
lane — built precisely for this — is not used; `MainView.cs:1311` has an
`"On-device log access is unavailable."` failure notice, but this path does not reach it
because `logcat` did technically start.

**Fix shape:** tell the reader before the system dialog what it is for; detect the
app-only outcome and say so on the notice lane as a failure; and stop naming the source
"full-device" when it is not.

---

### C2. Sessions are indistinguishable from each other

Every on-device capture is called "On-device logcat" — in the tab strip, in *Recent
sessions*, in the empty state's list, in *Session cache*, and in the suggested export
filename. During this walkthrough there were two tabs and three stored sessions, all with
that same name.

The tab strip is the worst case: two identical tabs with nothing to tell them apart. The
other lists at least carry a timestamp and a size.

**Fix shape:** give a capture a name at creation — the start time is the obvious
discriminator — and use that name everywhere the session is referred to.

---

### C3. *Recent sessions* → *Open* is enabled with nothing selected, and does nothing

The dialog opens with no row selected and *Open* enabled. Tapping it is accepted and
produces no result, no message and no state change. Either disable it until a row is
chosen, or make a row's tap open it directly.

---

### C4. Copy actions confirm nothing

`SessionWorkspaceView.CopySelectedRawAsync` writes the clipboard and returns. There is no
notice, no status change, and Android showed no clipboard chip for it. The Insights pane's
*Copy* is the same. Confirmed by tapping *Copy raw* with an entry selected and dumping the
tree immediately: no notice node, no status change.

The root cause is structural: the notice lane is driven only from `MainView` (15 call
sites), and `SessionWorkspaceView` has no route to it.

**Fix shape:** give the workspace a way to publish a notice, and use it for every action
whose only evidence is off-screen — copy, mute, filter-from-template.

---

### C5. Changing the system font size resets the workspace mode

Reproduced twice: with the workspace in *Plot*, setting `font_scale` to 1.3 returned it to
*Split*; with the workspace in *Details*, setting it back to 1.0 returned it to *Split*.
Rotation does **not** do this — the process id is unchanged across a rotation and the mode
survives.

The manifest declares
`configChanges="keyboardHidden|orientation|smallestScreenSize|screenLayout|screenSize|uiMode"`
(`MainActivity.cs:21`). `fontScale` and `density` are not in that list, so a text-size or
display-size change is handled differently from a rotation. `MobileWorkspaceState` is
explicitly written to hold the reader's choice against layout recomposition, so the choice
is being lost with the view that owned it, not overwritten by a size rule.

---

### C6. A nearly empty live session draws a degenerate plot

With one entry captured, the plot header read `DENSITY · 1 µs · 1,34 ns/px` and the two
axis labels read `20:55:38.430703` and `20:55:38.430704` — the same instant to the
microsecond. *Follow* was on and did not widen the window as more lines arrived.

A session with one point has no meaningful span; showing a one-microsecond window states a
precision that does not exist. A sensible floor (a few seconds) would read honestly.

---

### C7. *Fit* stays enabled in *Details* mode

*Details* hides the plot. *Fit* — "Fit the complete session in the plot" — remains present
and enabled, acting on a surface that is not on screen. It also keeps costing a 144 px
layout row there (see A2).

---

## D. Layout and visual

### D1. The Filters pane is about 40 px shorter than its content

At rest, the *Time lens* − and + buttons (y 1787–1931) are cut by the viewport edge at
~1908. Swiping the pane moves it those ~40 px and then clips the **QUERY** heading under the
pane's top border instead. There is no top or bottom padding inside the scroller, so the
pane always looks broken at one end or the other.

### D2. Two sheets clip their last control against the pinned decision row

*Appearance & timeline* cuts *Maximum zoom precision*'s field in half against the
Cancel/Apply row; *Session cache* cuts the third session row in half against its buttons.
With no divider, shadow or fade between the scrolling body and the fixed footer, a
half-drawn control reads as a rendering fault rather than as "there is more below".

### D3. *Session cache*'s decision row wraps to two lines

*Delete eligible sessions… / Cancel* on the first line, *Save policy* alone and
left-aligned on the second. The destructive action is the widest and comes first; the
confirm action is orphaned below the cancel.

### D4. *Session cache*'s disabled spinner is the brightest thing in its row

*Maximum total size* is at its minimum, so its decrement button is disabled — and it is
drawn as a filled light-grey block, the most prominent element in the row. This is the
exact pattern the first audit removed elsewhere ("disabled recedes"); the `NumericUpDown`
spinner template was not covered by it.

### D5. The *Entry* tab's empty state has a button straddling its card border

*Choose an entry* is drawn half inside and half outside the "No entry selected" card,
cutting through its rounded border.

### D6. The mode button group wraps, stranding *Fit* on a row of its own

See A2. 306 px for five buttons, on a screen where the entries list gets 173.

### D7. Cache-policy fields stay enabled while automatic cleanup is off

*Enable automatic temporary-session cleanup* is unchecked, and *Maximum age (days)* and
*Maximum total size* below it are fully interactive. Either they should be disabled with
the switch, or the copy should say what they still govern.

### D8. The session tab strip is full-bleed while everything else is inset

The strip spans x 0–1080; every other band is inset to 36–1044. The first tab's left
border sits on the screen edge with no rounded corner.

### D9. Landscape drops the minimap and overruns the status row

The minimap is absent in landscape with nothing said about it, so the session-wide
navigation aid disappears when the plot is at its widest. At the bottom, the entries list
extends past the status row into the same band.

### D10. Landscape reduces *Entry ⤢* to a bare glyph

The label collapses to "⤢" while roughly 200 px of width sits unused to its right. Its
accessible name is intact, so this is a purely visual identification problem — but a bare
glyph with no label and no tooltip on a touch device is not identifiable.

---

## E. Copy and consistency

**E1. Numbers and dates follow the device locale while the UI is English.** On a Czech
device: `19,76 min`, `1,59 s/px`, `1,00×`, `1,0 µs/px`, `34,63 KiB`, `4,3k`, `18.08.2026`,
and `59 640` with a non-breaking-space group separator. Either localise the UI or format
numbers with the UI's culture; mixing them makes both look accidental.

**E2. The two "recent captures" lists disagree.** The empty state prints
`18.08.2026 20:57 · 34,63 KiB` for a finalized session and `… · partial` for the others;
*Recent sessions* prints `… · ready` for the same finalized one. Same data, two vocabularies.

**E3. "ready" and "partial" are never explained.** Neither list nor any tooltip says what a
partial session is or what it costs the reader.

**E4. *Export CSV…* promises one scope and then asks about another.** The sheet's
description is "Write the filtered entries as CSV"; the dialog that opens defaults to
*Entries in view*, a different and usually much smaller set. Align the description with the
question, or default the dialog to the scope the description promised.

**E5. The empty state reports version 2.0.3**, which predates the current build. Worth a
check that the displayed version tracks the build rather than the last release.

**E6. Time-axis labels always print `.000` milliseconds** even at a 20-minute span, where
three zeros carry no information.

**E7. The status line's own truncation is a hidden affordance.** Tapping the row expands it
— which is genuinely useful, and is how you discover "Capturing · 23 lines received · 1/s ·
On-device full-device logcat" behind an ellipsis — but nothing on the row says it is
tappable. Only the accessible help text does.

**E8. Confirmations vanish after six seconds with no history.** A reader who looks away
during an export has no record that it happened, and the status line does not carry it.

---

## F. What already works

Stated so a later pass does not re-litigate them:

- **Every command is reachable and walkable.** The command sheet replaced the flyout, and
  `uiautomator` now enumerates all eight commands with names and help text.
- **Back peels one layer, then leaves.** Back from the workspace backgrounds the task
  (verified: the launcher becomes the resumed activity) rather than being swallowed.
- **The workspace survives a cold start.** Force-stop and relaunch restored the open
  session and the selected tab.
- **Session names carry no materialization guid** anywhere they are shown, including the
  suggested export filename.
- **Entries-list rows announce a readable sentence**, built on `ContainerPrepared` so it
  survives virtualization.
- **The export scope dialog** asks the right question before the save picker, with a row
  estimate for each option, and its confirmation names both the count and the scope.
- **Selection is a tint with an outline**, and disabled controls recede — everywhere except
  the spinner template (D4).
- **The empty state** is genuinely good: brand, one-sentence proposition, the severity
  vocabulary, three routes in, and the device's own recent captures as tap targets.
- **Plot mode** is the product at its best: six severity lanes with live counts, a legible
  minimap, and a readout of span and resolution.
- **Timeline gestures work.** Double-tap to zoom took the view from 59 640 to 24 847
  entries in view; pan and pinch behave.
- **The notice lane is real and accessible.** It appears in the accessibility tree with a
  name, the message text, and a named Dismiss button.

---

## Suggested order of work

1. **A1** — the light theme. Three defects, one subsystem, and the product is currently
   unusable for anyone whose phone is in light mode.
2. **A2** — give the entries list a floor. This is the difference between a log viewer and
   a plot with a caption.
3. **A3 / A4** — the scrolling panes. A chevron that pages the form instead of opening its
   dropdown is a bug a reader cannot work around or understand.
4. **B1 / B2 / B4** — the accessibility regressions. Cheap to fix, and two of them leak
   internal paths and guids into speech.
5. **C1 / C2** — honesty about the capture source, and names that tell two sessions apart.
6. Everything else, in roughly the order it is listed.
