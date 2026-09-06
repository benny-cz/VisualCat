# Recent captures deletion audit — 2026-09-06

Reviewed the implementation at `18c7250` against the feature's implementation plan —
kept outside version control as a working document — then closed the following gaps.
The decisions this record depends on are in [ADR 0022](adr/0022-capture-deletion.md);
what changed for the reader is in the [changelog](../CHANGELOG.md).

| Gap | Change and regression evidence |
|---|---|
| Export relied on its caller's idle snapshot, allowing deletion to close its tab during work. Save-source and verification had the same lifetime distinction. | `SessionAccess.ReadForWork` holds independent protection before any tab closure. Work transitions refresh the dialog through its existing debounce. Five real pipeline tests exercise raw/context/CSV export, save and verification; a shell test proves tabs remain untouched until work ends. |
| A capture could be replaced between the initial inspection and reservation, closing a replacement's tab before the final filesystem refusal. | Identity is checked again under deletion intent before enumerating tabs to close. The shell regression replaces the directory in that exact interval and verifies both its payload and tabs survive. |
| A cancelled or stale refresh could publish a late snapshot, and the post-delete refresh could not be stopped. | Cancellation and generation are checked before applying inventory or settling cleanup. Post-delete reconciliation owns a separate cancellable token. Two UI tests return a snapshot after cancellation and verify the previous list/results survive. |
| An incomplete scan could retain both old and new identities at the same path. | Successfully listed paths replace their obsolete rows even when another capture is unreadable. The regression also verifies the replacement arrives unchecked. |
| Refresh silently removed checks when captures became busy. | The status explains lost selections after refresh and preserves the announcement alongside results. A regression keeps the other eligible capture checked. |
| Preparation could exclude several captures, then lose their reasons after refresh. | Every exclusion remains in scrollable Details with its safe label and reason. A seven-capture test verifies none is hidden. |
| Losing access to storage cleared known cleanup status and its retry. | Unavailable inventories retain the last known cleanup counts. The empty-list regression keeps Retry storage cleanup reachable. |
| Cleanup remained pending for every deleted capture until the entire root was clean. | The inventory reports owned pending identities. Results settle individually while unresolved remnants conservatively retain uncertainty; a two-capture regression proves the counts. |
| A file replacing a staged payload was invisible to cleanup reporting. | Both files and directories at staging paths are inspected. The filesystem regression verifies an owned journal beside an unowned file is unresolved and preserves the file through retry. |
| Cancelled and never-attempted captures shared a counter; unknown outcomes were counted as confirmed failures. | Separate counters and result sentences preserve the distinction. Stopping after tab closure leaves an informational notice. Accounting tests reject claims about outcomes that remain unknown. |
| Mapped network drives bypassed the existing UNC refusal. | Windows drive type is checked before deletion can close tabs or mutate storage. Local-storage and existing confinement tests continue to pass; an actual mapped share was not available for device testing. |
| On the phone, changing system text size enlarged the open sheet's title but left its body at the old size. | Removed a redundant content wrapper that hid the dialog from the host's scale update. Three regressions publish an actual platform configuration callback with selection, confirmation or Details open, checking larger type and preserved state. |
| On the phone, a newly appearing cleanup retry pushed Close beyond the compact card's right edge. | Recompute footer orientation after child layout, including label/visibility changes that do not resize the footer itself. Two regressions introduce cleanup while the sheet is open at normal and enlarged text. |
| At 1.8× text in landscape, the combined selection/status/cleanup controls could consume the entire list and clip the last footer row. | Deletion sheets can use the safe viewport inside their frame and widen in landscape. Compact actions share one wrapping row, and duplicate selection guidance yields to the list. A real-host regression checks a 96 dp minimum list and every action inside a 948 × 450 dp viewport with cleanup present. |

Validation: **870** tests pass across the four projects (47 Domain, 128 Core, 135
Application, 560 App). The full app suite was repeated after the device-found
layout fixes; the final uncertainty wording assertion also passed separately.
This includes **79** Recent-capture UI/shell tests, **31** filesystem deletion
tests and **22** new regression cases from this audit. The changed C# files have
been formatted with the repository configuration.

Release preflight: all nine stages pass (`tools/verify-public-release.ps1`), as does
`tools/verify-docs.ps1`.

Physical Android validation, 2026-09-06, Samsung SM-G990B (Android 16, 360 dpi
override), debug APK built from this working tree:

| Checked | Result |
|---|---|
| System text size raised from 1.0 to 1.8 with the sheet open | The whole sheet scales, not only its title: the heading grew 40 -> 72 px and a row's name 37 -> 67 px, with the list, its selection and the sheet's own state preserved. |
| Landscape at 1.8 | The card takes 900 x 434 dp instead of 620 x 82% of the viewport. The list measures 111 dp and every action sits inside the card. |
| Cleanup appearing while the sheet is open, landscape at 1.8 | Details, Refresh, Retry storage cleanup, Select and Close share one row, all inside the card; the list keeps its 111 dp. |
| The same in portrait at 1.8 | The row wraps rather than overflowing: Details and Refresh, then Retry storage cleanup, with Select and Close still on the card. |
| A capture recording | Refused by every route, with *This capture is being recorded. Stop the capture first.* |
| Deleting six captures with an unowned remnant present | All six removed. The result reads *Deleted 6 captures. About 20.03 MiB of captures removed.* with no pending-cleanup claim against them, and the remnant is reported separately as unverified. This is the per-capture settling above, on real storage. |
| Details after that deletion | Every capture carries its full safe label and *Deleted from temporary storage.*, alongside the unverified-cleanup entry. |
| Retry storage cleanup against the unowned remnant | Refused. The directory and its payload survive byte-for-byte, and the status becomes *Some storage cleanup is still outstanding.* |
| Removing the remnant and refreshing | The outstanding sentence and Retry both disappear; the deletion result stays. |
| The notice lane at 1.8 | *Deleted 6 captures from temporary storage.* over two complete lines with **More** offered, nothing clipped. |

The device was left locked with empty capture storage, text size 1.0 and rotation
unlocked.

The plan's human TalkBack speech/gesture pass and physical Windows keyboard pass
remain manual acceptance items. Headless accessibility/keyboard assertions and
ADB-driven device interaction do not establish those results.
