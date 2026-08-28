# Keyboard and accessibility

VisualCat keeps its main analysis path operable without a pointer. Shortcuts are
ignored while typing unless they use Ctrl, Alt, Escape, or a function key.

## Global shortcuts

| Shortcut | Action |
|---|---|
| Ctrl+O | Open a log file |
| Ctrl+Shift+O | Open a `.vcat` session |
| Ctrl+E | Export the selected session |
| Ctrl+F | Focus and select the search field |
| F3 or N | Move the viewport to the next search marker |
| Shift+F3 or Shift+N | Move to the previous search marker |
| Escape | Close mobile filters, clear focused search, clear a selected timeline scope, or clear filters (in that order); ignored when there is nothing to dismiss |
| Alt+1 | Focus the timeline |
| Alt+2 | Focus the entry list |
| Alt+3 | Focus the template list |
| Alt+4 | Focus the first facet control |

Search-marker navigation wraps at the first and last match and preserves the
current zoom span.

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
