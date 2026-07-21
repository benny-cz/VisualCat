# ADR 0007: Rank bitmaps and cache

**Decision:** Persist seven severity bitmaps per segment. Build other filter bitmaps lazily in a 16-entry per-segment LRU cache; rank uses eight-word superblocks.

**Alternatives:** Per-entry filter scans for every cell do not meet viewport budgets.

**Consequences and validation:** Boolean/rank operations are checked against randomized naïve oracles and bitmap cardinality is verified on disk.
