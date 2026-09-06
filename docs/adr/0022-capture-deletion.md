# ADR 0022: Deleting a capture is leased, staged and recoverable

**Context:** *Recent captures* could open a stored capture but never remove one. The only
routes out were the retention policy in **Session cache** and the single-action delete in the
recovered-capture review, and neither lets a reader pick a few unwanted captures out of the
list. Adding that action means answering three questions the product had never had to answer.

The first is *who else is using this capture*. `Directory.Delete` succeeds against a directory
another process is writing into on the platforms VisualCat ships on, and the shell's own view —
which tabs this workspace has open — describes one process. A second VisualCat, a CLI run, or
this app's own save/import work is invisible to it.

The second is *what "deleted" means when reclaiming the contents fails halfway*. Recursive
deletion is not transactional. A tree that is half gone is neither a capture that can be opened
nor storage that has been returned, and a list that still shows it is lying either way.

The third is *whether the capture at this path is still the capture the reader confirmed*.
Preparation, a confirmation the reader reads, and execution are separated by human time.
Timestamp and size are not identity: a capture can be replaced at the same path in between.

**Decision:**

1. **Shared-use and exclusive-delete leases, held by cooperating processes.**
   [`SessionAccess`](../../src/VisualCat.Core/Store/SessionAccess.cs) keeps three lease files per
   session outside the payload in `LocalApplicationData/VisualCat/SessionAccess-v1`. This stable
   per-user location is shared even when the GUI and CLI have different TEMP directories. Every
   new reader or writer briefly shares an `.intent` gate; readers hold `.read` shared, and one
   writer holds `.write`. Store snapshots, recording, saving, archive extraction and both reads
   and writes of the view sidecar participate. Deletion takes intent exclusively and refuses
   active writers, local read operations (export, save-source and verification), or foreign
   readers before closing any local tabs. Local read operations hold `ReadForWork` independently
   of their caller's snapshot; closing that snapshot cannot cancel their protection. The shell
   coalesces work-state notifications into inventory refreshes. It holds the OS read lease
   exclusively while its own existing readers drain, and checks that every local reference is
   gone before rename. A failed close cannot bypass this check on retry. A deletion that ends
   without removing the capture — a refused close, a changed identity, Stop — hands sole use
   back rather than keeping it: held on, the lease it took to close its own tabs under would go
   on refusing every other cooperating process a capture that is merely open here.
   Operating-system handles release on process death; files remain, but their existence is
   never a lock. Recovery takes deletion intent and no more: it reclaims a detached payload and
   retires obsolete metadata, and it never renames the source, so it does not ask for the
   pre-rename check that also refuses while any local reader holds the capture. Asking for it
   made recovery lose a race it did not need to enter — listing a root reads every capture in
   it, one at a time — and a record left by a rename that never committed survived pass after
   pass on a real desktop.

2. **Rename is the commit; reclaim is a separate, owned obligation.** An approved capture is
   moved to a sibling `.{guid:N}.vcat-deleting`, after a flushed ownership record naming the
   staging directory, the source, the identity and the size estimate has been published beside
   it. Successful rename is the point at which the capture is gone from the list; failing to
   reclaim the staged payload afterwards never restores it and never turns a committed removal
   into a pre-commit failure. Only a staged tree with a valid, matching ownership record is ever
   swept — a matching suffix is not authority to erase a directory. Recovery is scheduled by
   ordinary startup scans and foreground inventories without blocking them. Entry/time budgets,
   preserved descent cursors and round-robin metadata inspection let deep trees and later
   records progress. Corrupt records, including records with no payload, remain unchanged and
   appear in the unresolved-cleanup inventory. A file occupying a staging path is also an
   unresolved remnant, even beside a valid record. Inventories include the identities still
   awaiting owned cleanup, so results can settle individually without waiting for every other
   deletion in the root. An unavailable inventory cannot clear known cleanup status.

3. **Identity is a marker plus, where the storage has one, a birth time.** Preparation writes a
   `.capture-identity` file holding a random GUID into the capture and records it. Publication
   writes and flushes a unique temporary file before an atomic, non-overwriting move, so another
   process cannot observe a half-written marker. Execution
   revalidates it under the reservation before closing any tab, as well as before rename.
   Replacement at the same path yields `Changed` rather
   than substitution into the confirmed set. Whether directory creation time survives writes is
   *measured* once per storage root rather than assumed from the operating system: where it does
   not survive, the marker alone is the identity. VisualCat's save and archive-extraction routes
   omit identity markers so each copy receives a fresh one. An external restored copy that
   preserves the marker and has no distinguishable birth time remains outside this guarantee.

4. **No-follow validation, bounded by the storage root.** The source must be a direct `.vcat`
   child of the prepared root. The root itself must be a real directory rather than a link, and
   nothing inside the tree being walked or reclaimed is ever followed. Above the root is the
   reader's own filesystem layout, which this product does not own and does not inspect: a link
   there is resolved once by the operating system, consistently, for every call the operation
   makes. Refusing those as well made the feature unusable on whole platforms — macOS reaches
   its standard temporary directory through `/var`, a symlink, so every root beneath it reported
   unavailable storage — and it never closed the hostile-mutation gap it appeared to, since an
   ancestor can be swapped after any check. It also removes the need for a platform anchor:
   stopping at the root already means no stat call is made against SELinux-protected ancestors
   above an Android app's sandbox. On Windows, both UNC roots and mapped network drives are
   refused.

5. **Refresh and result lifetimes are separate from deletion.** The post-operation inventory
   has its own cancellable lifetime. A cancelled or older inventory cannot replace the list or
   settle cleanup results. A partial scan retains unlistable captures, but never an old identity
   at a path whose replacement it has successfully read. Lost checks are explained, preparation
   exclusions remain in Details, and cancelled, unattempted and unverified outcomes have
   separate counters. Stopping after tabs close leaves an informational notice about that
   side effect; a stop before any change remains quiet.

6. **An open dialog adapts without losing its state.** The in-page host owns the dialog
   directly, so platform text changes resize the body as well as the title. Deletion sheets
   may use the safe viewport height and more landscape width; compact actions share the full
   wrapping width and are remeasured when their labels or visibility change. Duplicate
   selection instructions yield before results, cleanup actions or the capture list do.

**Alternatives considered:** A single exclusive lease per session was simpler, and was rejected
because it would have stopped two VisualCat instances from *reading* the same capture, which
they can do today. An in-memory set of "paths this workspace is using" was rejected outright: it
describes one process and cannot close the interval between the check and the rename. Copy-then-
delete as a fallback for a failed move was rejected because it doubles the capture on disk and
can leave two of them.

**Consequences and validation:** The guarantee is scoped to cooperating processes on local,
controlled storage. An older build, an unrelated tool, hostile same-user mutation, and network
shares whose locking semantics differ are outside it, and the product says so rather than
implying more. No estimate and no successful recursive delete is a measurement of free disk
space: the interface reports approximate capture sizes, "storage cleanup is pending" and
"removed from the list" as three different things.

Covered by `CaptureDeletionTests` (ordered outcomes for pre-cancelled and mid-batch requests,
refusal of nested, relative, non-`.vcat`, root, volume-root and linked targets, verified-absent
versus denied roots, crash boundaries at record/rename/reclaim, staged folders excluded from
listings, unowned remnants refused, budgeted cleanup that drains without blocking a scan, size
index publication racing deletion, deep cleanup trees, path-spelling normalisation, and
two-process tests proving writer/reader protection, blocking new readers after deletion intent,
independence from TEMP settings, release on process death, sole use handed back after an
abandoned deletion, and a record left by a failed rename retired while the capture it names is
being read) and by `RecentCaptureDeletionTests` for the
shell's ordering: one capture at a time, its tabs closed only when its turn arrives, and Stop
leaving later captures *and their tabs* untouched. The UI suite also checks unknown-size copy,
known commits after a broken/cancelled batch, late progress after Stop, and touch actions inside
a constrained phone sheet at 1.8x text in light and dark themes, row state through container
recycling, a nested dialog owned by the dialog that raised it, and one end-to-end pass over real
storage in which the disk, the home card and the notice all have to agree afterwards. Physical-device acceptance is
recorded separately; headless geometry does not establish TalkBack or device-rendering results.
The 2026-09-05 pass on a Samsung SM-G990B and on Windows covered selection, confirmation and
cancellation, protection by a second process and by a running capture, tab closure, retry after
the holder released, unowned remnants refused, the empty state, and recovery from a process kill
during reclaim; it is recorded in `ANDROID-LIVE-TEST-PLAN.md` A-26 to A-30 and in
`WINDOWS-LIVE-TEST-PLAN.md` P-16.1.

See also [ADR 0014](0014-retention.md) for retention policy, and
[`SESSION-FORMAT.md`](../SESSION-FORMAT.md) for the reserved staging names and the ownership
record.
