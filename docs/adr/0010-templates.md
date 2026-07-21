# ADR 0010: Template mining

Section references such as `§5.5` point to numbered sections of the historical
[`../design/PLAN.md`](../design/PLAN.md); `R##` identifiers point to its
requirement list.

**Decision:** Use versioned Drain-style clustering with ordered masks, state keyed by tag, sequential session-local IDs, representative examples, and bounded clusters.

Mining runs as the tag-sharded partition stage of §5.5 rather than inline on the commit
coordinator: the committer hands each source-ordered batch to a fixed set of single-writer
miners, routed by a stable hash of the tag, and walks the result afterwards.

**Alternatives:** Hash-only templates risk collisions; a tree shared per worker makes output depend on worker count.

Mining inline on the committer was the original arrangement and is simpler, but profiling a
two-million-line import attributed 3.53 s of 12 s to masking and clustering on that one
thread — the largest single cost in the pipeline, sitting on its only serialized stage.

**Consequences and validation:** Replay determinism, tag isolation, masks, and high-cardinality behavior are automated tests.

Sharding is sound only because two properties hold, and both are asserted rather than
assumed:

- *Clustering does not depend on the shard count*, because every tag owns its own Drain
  tree and a shard is only an execution container for a set of tags. Routing chooses where
  work runs, never what it produces.
- *Numbering does not depend on completion order*, because shards do not assign identities
  at all. Each cluster carries an unassigned global id until the committer's single-threaded,
  source-ordered pass stamps it, so ids reflect first appearance in the source.

`ShardCountDoesNotChangeTemplateOutput` and `BatchSizeDoesNotChangeTemplateOutput` compare
both the per-entry ids and the emitted template table against a single-shard run, across
1–16 shards and 1–4,096-entry batches. An earlier revision of this change numbered clusters
as the shards created them; it satisfied an assignment-only comparison while still
renumbering the template table, which is why both halves are checked.

Long-format sessions keep the inline path: a `long` record's message is not complete until
its body lines have been read, so it cannot be mined with the rest of its batch.
