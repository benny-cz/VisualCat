# ADR 0006: Reorder, late data, and compaction

**Decision:** Preserve source order and original time independently. Published segments are stably time-sorted; finite finalization performs a bounded k-way stable merge.

**Alternatives:** Timestamp clamping falsifies evidence; a single mutable array invalidates readers.

**Consequences and validation:** Queries fan across immutable segments during ingest. Property and integration tests compare multisets and ordering.
