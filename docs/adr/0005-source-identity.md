# ADR 0005: Source identity and portable raw data

**Decision:** Imported files record canonical path, length, modification time, and SHA-256. Portable/live sessions contain `raw.log`.

**Alternatives:** Path-only identity silently accepts changed evidence.

**Consequences and validation:** Reopen uses cheap metadata to enter degraded mode; `verify` performs the full content check.
