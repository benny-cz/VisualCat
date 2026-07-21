# ADR 0008: Timestamp inference

**Decision:** Yearless files use source modification time, a declared zone, December/January rollover, and explicit ambiguous/invalid local-time policy. Inversions are flagged, not clamped.

**Alternatives:** Using the host's current year is irreproducible.

**Consequences and validation:** Policy is embedded in the manifest and previewed before import; rollover and DST cases belong in the golden corpus.
