# ADR 0014: Temporary sessions and retention

**Decision:** Temporary sessions live under platform-local application data (or a configured cache directory). Cache cleanup is disabled by default, visible in the UI, and deletes only direct-child `.vcat` sessions after explicit opt-in and confirmation. Live ADB capture can use explicit duration or raw-byte caps; it finalizes at the cap and does not discard already captured evidence. In-place leading-segment trimming is therefore not enabled.

**Alternatives:** Silent age/size trimming violates evidence invariants.

**Consequences and validation:** UI and documentation expose location and policy; deletion never promises forensic erasure. Path-confinement, reparse-point rejection, opt-in behavior, age, and size bounds are tested. If future in-place live retention is added, it must delete whole leading immutable segments and matching raw ranges and increment manifest loss counters.

## Amendment (2.0.13): explicit deletion, and what "complete only" meant

Two things about this decision needed correcting rather than restating.

**Eligibility.** The original wording said cleanup "deletes only complete sessions". The
implementation has never filtered on `finalized`: `SelectEligible` selects by age and by total
size, and an interrupted capture is as eligible as a finished one. The wording, not the
behaviour, was wrong — an interrupted capture is exactly the kind a stale cache accumulates, and
excluding it would have made the size bound unreachable. **Eligibility is age and size, not
completeness**, and this amendment records that rather than silently changing what the policy
selects.

**Explicit deletion.** A reader can now select captures in *Recent captures* and delete them.
That route is not policy: it is confirmed, per-capture, and reports one typed outcome for every
target. It shares one filesystem primitive with the exact-delete route, and that primitive is
now leased, staged and recoverable — see [ADR 0022](0022-capture-deletion.md) for the
reservation, identity and cleanup contracts, and for the boundary of what cooperating processes
can promise. Policy cleanup runs through the same staged primitive, so a cleanup that fails
halfway leaves an owned, retryable obligation instead of a half-deleted directory.

Neither route promises forensic erasure, and neither reports bytes returned to the filesystem.
The interface distinguishes a capture removed from the list, a capture whose storage cleanup is
still pending, and a capture that was already missing.
