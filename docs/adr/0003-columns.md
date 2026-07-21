# ADR 0003: Column layout and timestamps

**Decision:** Immutable segments store explicit little-endian fixed-width columns. Canonical time is signed UTC microseconds; original text and provenance remain separate payload/columns.

**Alternatives:** Object graphs and SQLite were rejected for scale and mapped query predictability.

**Consequences and validation:** The layout favors validation and direct mapping over aggressive premature packing. The verifier checks every dimension and stable order.
