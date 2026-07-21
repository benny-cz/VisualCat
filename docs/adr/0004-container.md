# ADR 0004: Container versioning and atomicity

**Decision:** A `.vcat` session is a versioned directory with an atomically replaced JSON manifest and checksummed immutable files. Major versions fail closed.

**Alternatives:** A monolithic archive complicates live append and recovery.

**Consequences and validation:** Incomplete directories are recognizable; portable copies are verified before publication.
