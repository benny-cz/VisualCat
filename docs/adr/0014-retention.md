# ADR 0014: Temporary sessions and retention

**Decision:** Temporary sessions live under platform-local application data (or a configured cache directory). Cache cleanup is disabled by default, visible in the UI, and deletes only complete direct-child `.vcat` sessions after explicit opt-in and confirmation. Live ADB capture can use explicit duration or raw-byte caps; it finalizes at the cap and does not discard already captured evidence. In-place leading-segment trimming is therefore not enabled.

**Alternatives:** Silent age/size trimming violates evidence invariants.

**Consequences and validation:** UI and documentation expose location and policy; deletion never promises forensic erasure. Path-confinement, reparse-point rejection, opt-in behavior, age, and size bounds are tested. If future in-place live retention is added, it must delete whole leading immutable segments and matching raw ranges and increment manifest loss counters.
