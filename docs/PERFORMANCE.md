# Performance baseline

Section references such as `§12.4` point to numbered sections of the historical
[`design/PLAN.md`](design/PLAN.md); `R##` identifiers point to its requirement list.

## Initial public baseline — 2026-07-19

Reference machine:

- AMD Ryzen 9 5900X, 12 cores / 24 logical processors;
- Windows 11 Pro Insider Preview 10.0.26220, x64;
- Samsung SSD 990 PRO NVMe workspace drive;
- .NET SDK 10.0.101, Release configuration.

The checked `bench/VisualCat.Benchmarks` runner executes the complete file pipeline—including decoding, format detection, timestamp resolution, deterministic Drain mining, column/index writes, checksums, and final compaction—then runs unfiltered 2,000-column heat-map queries. Generated corpora are reproducible through `vcat generate-test-log`.

| Corpus | Bytes | Timed entries | End-to-end ingest | Heat map | Working set |
|---|---:|---:|---:|---:|---:|
| Seeded synthetic, 1,000,000 lines | 90,017,930 | 999,885 | 74,607 lines/s | 27.38 ms, 20 iterations | 370 MiB |

The million-line run is the initial reproducible end-to-end regression baseline.

## Optimization pass, 2026-07-19

Measured with the same harness and corpus, before and after, on the reference machine:

| Metric | Before | After | Change |
|---|---:|---:|---:|
| End-to-end ingest, mining enabled | 3.53 s | 2.81 s | 1.26× |
| Ingest throughput | 33,633 lines/s | 42,320 lines/s | 1.26× |
| Full-view heat map, 2,000 × 7 | 6.90 ms | 6.32 ms | 1.09× |
| CLI ingest, mining disabled (best of 5) | 1.81 s | 1.13 s | 1.60× |

What changed, in order of contribution:

- Column data is staged in pooled buffers and written once per column, and checksums are
  computed from those buffers instead of re-reading every column back off disk.
- Final compaction is skipped when the published segments are already globally sorted,
  which is the common case for logs that arrive in order.
- Timestamp resolution caches the UTC offset per local day instead of consulting
  `TimeZoneInfo` three to four times per entry.
- Template mining gates each mask on a cheap necessary condition, skipping most of the
  nine patterns per message, and the committer no longer materializes canonical text and
  parameter lists it never reads.
- The heat map composes `active AND severity` word-wise rather than rebuilding a bitmap
  from an index predicate once per severity row.

The mining stage itself became slightly more expensive, not less: the miner now honours
the `Depth` and `MaximumChildren` settings that the previous flat cluster list ignored,
which is required by §9.3 and produces finer clusters (2,651 vs 1,833 on this corpus).
That conformance was preferred over the throughput it costs; total ingest still improved.

## Current public baseline — 2026-07-20

Same harness, same corpora, same machine as the table above, so the numbers are directly
comparable to the 2026-07-19 baseline:

| Corpus | Ingest before | Ingest after | Heat map before | Heat map after |
|---|---:|---:|---:|---:|
| Seeded synthetic, 999,885 entries | 74,607 lines/s | 142,557 lines/s | 27.38 ms | 6.26 ms |

Ingest is 1.90× and the heat map is 4.4× faster on the public corpus. The full-view heat map now meets the §19.2 target of 8 ms at a million
entries, which the previous baseline missed by more than 3×.

What changed, in order of contribution:

- **Heat-map boundaries are found by galloping, not bisection.** A 2,000-column viewport
  asks each segment for monotonically increasing time boundaries, and each search now
  resumes where the previous one ended instead of bisecting the whole segment. Adjacent
  columns also share a boundary, so N+1 searches answer N cells where the previous form
  performed 2N. This is the whole of the query improvement and it grows with segment size,
  which is what makes the §12.4 cost model hold as sessions get longer.
- **Template mining moved off the commit thread onto the tag-sharded partitions of §5.5.**
  Profiling attributed 3.53 s of a 12 s import to masking and clustering running inline on
  the single committer. Mining a batch across shards before the commit walk makes it
  effectively free: the same import now costs what it did with `--no-templates`.
- **Per-line allocations removed from the commit path.** The source sequence is stamped by
  the parse worker rather than rebuilt on the committer, the template is mined before the
  entry is constructed instead of copying the entry to add it, `LineSlice` became a value
  type, and the per-line `await` — which allocated a state machine and a `Task` per line
  even when it completed synchronously — is gone.
- **Progressive manifests are no longer written through the OS cache.** Only the finalized
  manifest needs durability; atomicity comes from the temporary-file rename either way.

Determinism is unaffected and is now proven rather than assumed: `ShardCountDoesNotChangeTemplateOutput`
and `BatchSizeDoesNotChangeTemplateOutput` assert that both the per-entry template ids and
the resulting template table are byte-identical across 1–16 shards and 1–4,096-entry
batches, which is the §9.4 guarantee that permits the sharding in the first place.

Correctness remains a hard gate. Until distributions exist on controlled CI agents, performance is a regression gate:

- at least 120,000 full-pipeline lines/s on the seeded one-million-line corpus;
- no more than 10 ms average for its 2,000-column full-view heat map;
- no statistically meaningful regression beyond 20% from a rolling controlled-agent baseline.

The scheduled and `performance`-labelled
[`performance.yml`](../.github/workflows/performance.yml) workflow runs a
100,000-line deterministic smoke corpus on GitHub-hosted Linux. Hosted runners
are intentionally given much wider absolute floors (10,000 lines/s and 100 ms)
than the reference machine because their CPU and storage are shared and vary
between runs. This catches catastrophic regressions and preserves a JSON result
artifact; it does not replace the strict million-line reference-machine gates.

The original 1.5-million-lines/s full-pipeline target was not supported by measurement: it exceeds the observed safe, mining-enabled pipeline by roughly 20× even on a 12-core NVMe workstation. ADR 0018 therefore replaces it as a release gate with the measured targets above. It remains an optimization direction, not a claim. Larger 10 M / 40 M scale runs remain controlled benchmark jobs rather than source-controlled fixtures.

Generate and run the million-line public baseline outside source control:

```shell
mkdir -p .tmp
dotnet run --project src/VisualCat.Cli -c Release -- generate-test-log --output .tmp/synthetic-logcat.txt --lines 1000000 --seed 42
dotnet run --project bench/VisualCat.Benchmarks -c Release -- .tmp/synthetic-logcat.txt 20
```
