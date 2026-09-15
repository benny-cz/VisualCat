# Keyboard and accessibility

VisualCat keeps its main analysis path operable without a pointer. Shortcuts are
ignored while typing unless they use the primary modifier, Alt, Escape, or a
function key.

**The primary modifier is the platform's own.** Windows and Linux use **Ctrl**;
macOS uses **⌘**. Written against Ctrl everywhere, ⌘F did nothing on a Mac and a
reader concluded that search did not exist — and ⌃F is already spoken for on
macOS, where the system gives it to text fields to move the caret forward
(finding F-03). macOS therefore answers to ⌘ only, and every macOS shortcut is
also on the menu bar, which is where a Mac user looks for it.

## Global shortcuts

| Windows / Linux | macOS | Action |
|---|---|---|
| Ctrl+O | ⌘O | Open a log file |
| — | ⌥⌘O | Open a log with import options |
| Ctrl+Shift+O | ⇧⌘O | Open a `.vcat` session |
| — | ⇧⌘R | Recent captures |
| — | ⇧⌘L | Live ADB capture |
| — | ⌘S | Save the session |
| — | ⇧⌘S | Save a portable session |
| Ctrl+E | ⌘E | Export the selected session |
| Ctrl+F | ⌘F | Focus and select the search field |
| F3 or N | F3, N or ⌘G | Select the next search match |
| Shift+F3 or Shift+N | ⇧F3, ⇧N or ⇧⌘G | Select the previous search match |
| Ctrl+G | — | Open **Go to match** and select a match by number |
| Alt+Home | ⌥Home | Select the first search match |
| Alt+End | ⌥End | Select the last search match |
| Escape | Escape | Close mobile filters, clear focused search, clear a selected timeline scope, or clear filters (in that order); ignored when there is nothing to dismiss |
| Alt+1 | ⌥1 | Focus the timeline |
| Alt+2 | ⌥2 | Focus the entry list |
| Alt+3 | ⌥3 | Focus the template list |
| Alt+4 | ⌥4 | Focus the first facet control |
| — | ⌘0 | Fit the whole session in the plot |
| — | ⌘= / ⌘− | Zoom the plot in / out |
| — | ⌘, | Settings |
| — | ⌘W | Close the window |
| — | ⌘M | Minimise |

macOS also gets the platform's own Edit menu — ⌘X, ⌘C, ⌘V, ⌘A — and whatever the
system adds to it (Writing Tools, Dictation, Emoji & Symbols), plus
**⌃⌘F** for full screen from the View menu. **Hide Others is ⌥⌘H**, the macOS
standard; the stock Avalonia menu bound it to ⌥⌘Q, one modifier away from Quit.


Search navigation selects an exact record, not a position on the plot. The
counter reads `k / N` over **every** match in the session, and `– / N` when no
match is selected — after a pan or zoom, or before the first step.

Matches are ordered by time, then by their order in the source, so records that
share a timestamp still have separate places in the sequence; **Go to match**
says `Search order: time` beside its field. Stepping wraps at the first and last
match in both directions, and preserves the zoom you chose. When the whole
session is already on screen, arriving at a match opens a readable window around
it instead of leaving the view unchanged.

`Ctrl+G`, `Alt+Home` and `Alt+End` work from the workspace and from the search
field, and are the keyboard route to the **first**, **last** and counter buttons
already in the search stepper. With no matches they are inert and say why.

## Timeline shortcuts

The timeline itself is focusable and has an accessible help description.

| Shortcut | Action |
|---|---|
| Left / Right | Pan by 10% of the visible span |
| Plus / Minus | Zoom in / out around the center |
| 0 | Fit the complete session |
| Home / End | Move to the start / end of the session |
| F | Toggle follow-latest mode |
| J / K | Select the next / previous matching entry |

## Phone Split divider

When the plot/details grip is focused in a phone Split workspace:

| Shortcut | Action |
|---|---|
| Up / Down | Move the stacked (portrait) divider by 16 dp |
| Left / Right | Move the side-by-side (landscape) divider by 16 dp |
| Home | Return that orientation to responsive automatic sizing |

Each orientation has its own divider and its own remembered position, so
resizing one never moves the other.

The divider is exposed to automation as a named range control, so assistive
technology can also set its value directly. The same reset is available without
a keyboard under **Appearance & timeline**.

## Recent captures

These act inside the list of stored captures; **Escape** is the exception and works from
anywhere in the dialog, including the decision row. A focused button, checkbox or text
selection keeps its own keys, and nothing here is claimed while a confirmation or the results
view owns the keyboard.

| Shortcut | Action |
|---|---|
| Arrows / Home / End | Move through the list; no check changes |
| Space | Toggle the focused row's check, when that capture can be deleted |
| Ctrl+A | Check every capture that can be deleted |
| Ctrl+Shift+A | Clear every check |
| Delete | Confirm deletion of the checked captures |
| Enter or double-click | Open the highlighted capture |
| Escape | Clear checks; a second press closes the dialog |

After a deletion the keyboard stays where it was working: a reader who deleted from the list
lands on the nearest surviving capture, and one who used the buttons stays at the decision row.
When the last capture goes, focus moves to the action that is left.

The highlight and the checks are independent: **Open** acts on the highlighted capture, and
**Delete** acts on the checked ones. Deleting always confirms, the confirmation's initial focus
and default action is **Cancel**, and Enter there never deletes.

On Android the system Back gesture does this instead: it leaves selection first, and closes the
dialog on a second press. Escape is not additionally claimed there, because Android delivers
Back as an Escape key-down followed by the platform callback, and answering both would take two
steps for one press.

## Accessibility behavior

- Interactive filters, panes, lists, source controls, and timeline actions have
  explicit automation names; shortcut help is attached to the timeline and
  search field.
- Severity controls combine labels with the same colors used in the heat map;
  high-contrast mode increases selection contrast rather than relying on color
  alone.
- Source context is a selectable read-only surface, not an editable text box.
- Touch targets expand in the Android layout, while desktop focus order follows
  search → severity → timeline → analysis panes.

Before a release, manually verify the keyboard, screen-reader labels, contrast,
and text scaling items in [`RELEASE-CHECKLIST.md`](RELEASE-CHECKLIST.md) on each
supported desktop platform. Headless CI covers focusable composition and core
interaction behavior; it does not replace platform assistive-technology tests.
