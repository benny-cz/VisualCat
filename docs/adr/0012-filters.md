# ADR 0012: Filter semantics and generations

**Decision:** One immutable `FilterSpec` and stable SHA-256 fingerprint feeds every analytical query. Results carry session, snapshot, filter, and query generations.

**Alternatives:** Independent view filters cause irreconcilable counts and stale races.

**Consequences and validation:** Selected-cell counts are tested against details and presentation rejects superseded results.
