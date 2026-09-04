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

The workflow runs that corpus twice over. The default one has seven tags and seven
message shapes, so it mines seventy-seven templates however long it runs and cannot
exercise any cost that scales with template diversity. The second is generated with
`--tags 1900 --templates 4000`, the proportions measured on a real device, and adds three
gates the first cannot meaningfully apply:

- `--max-manifest-bytes 262144`, because the manifest is rewritten in full on every
  published snapshot and its size is what decides whether a long capture stays openable.
  Carrying the template table inside it again puts this corpus near 2.7 MB, ten times the
  ceiling; the sidecar keeps it near 35 KB, seven times under it.
- `--min-export-entries-per-second 50000`. Paged entry reads are the one hot path that
  neither ingest nor the heat map can see, because both touch every entry exactly once
  while export walks the page cursor the way the phone's *Load all* does.
- `--max-bytes-per-line 8000`, which turns `bytesAllocatedPerLine` from a reported number
  into a bounded one.

The runner also reports `templates`, `tags`, `manifestBytes` and `templateSidecarBytes`
so a diversity regression is visible in the summary even where no gate fires.

## Search baseline — 2026-09-04

The retained runner now performs a cold literal miss and a cold regex match over a newly
opened snapshot for every sample. Its JSON records the corpus SHA-256, runtime/OS/CPU
identity, exact query, stable match count, one labelled warm-up, every measured elapsed time,
entries/s and allocated bytes, plus median throughput and p95 latency. Source identity is
reported as both the commit and `sourceRevision`; the latter gains a `+dirty` suffix when Git
finds tracked or untracked working-tree changes, with the nullable `workingTreeDirty` field
preserving the distinction between clean and unavailable Git status. The scheduled workflow
publishes both corpora and both search shapes in its summary.

Search is intentionally informational on hosted runners at first. The reference machine now
reads 6.08 million entries/s for a million-entry literal scan and 8.20 million on the
400,000-entry corpus (medians; see the A/B below), but choosing a Linux shared-runner floor
from a workstation median would turn machine variance into a product gate. After several scheduled artifacts establish the hosted distribution, pass the
runner's `--min-search-entries-per-second` option with a deliberately loose catastrophic-
regression floor. The deterministic app test independently protects the presentation-layer
invariant that one cold filter builds one active bitmap per segment; engine timing alone
cannot detect duplicated orchestration work.

## Mapped column reads — paired A/B, 2026-09-04

`MappedColumn` held a `MemoryMappedViewAccessor` and read each element through it, which
takes and releases a ref-count on the safe handle **per element**. It now acquires the
mapping pointer once for the object's lifetime and decodes payload UTF-8 straight out of the
mapping instead of through an intermediate `byte[]`.

The measurement below is a controlled A/B: one worktree at `ece618e` with *only*
`MappedColumn`/`SegmentSnapshot.ReadPayload` at their old form, the other the change, both
carrying the identical benchmark runner and both driven over the same two corpora
(SHA-256 recorded in each JSON and verified equal). Reference machine: AMD Ryzen 9 5900X,
Windows 11 Pro 10.0.26220 x64, .NET SDK 10.0.400, Release. Search figures are the median of
nine measured samples after one labelled warm-up, each on a freshly opened snapshot so the
per-segment caches start cold; every sample is retained in the JSON.

| Corpus | Measurement | Before | After | Ratio |
|---|---|---:|---:|---:|
| `clean.vcat` (399,955 entries, 4 segments) | literal search | 4,177,839 entries/s | 8,199,647 entries/s | **1.96x** |
| | regex search | 3,457,190 entries/s | 5,752,100 entries/s | **1.66x** |
| | search allocation | 60.2 MiB | 36.0 MiB | **0.60x** |
| | CSV export | 425,419 entries/s | 665,307 entries/s | **1.56x** |
| `diverse.vcat` (999,890 entries, 10 segments) | literal search | 3,498,754 entries/s | 6,084,969 entries/s | **1.74x** |
| | regex search | 2,865,884 entries/s | 4,960,434 entries/s | **1.73x** |
| | search allocation | 159.8 MiB | 98.8 MiB | **0.62x** |
| | CSV export | 628,182 entries/s | 663,714 entries/s | 1.06x |

Match counts were identical in both arms (0 for the literal miss, all 999,890 / 399,955 for
the regex), so the two arms answered the same question. The export ratio is corpus-dependent
— 1.56x where column reads dominate, 1.06x on the high-cardinality corpus where CSV
formatting does — and is reported as measured rather than averaged into one claim.

The pointer path retains a cheap unsigned per-element bounds check, and the rest of its safety
boundary is stated and tested rather than assumed: the constructor refuses a column whose file
length is not exactly `elementSize * expectedCount`, payload spans keep an explicit
overflow-safe bound check, and `SegmentSnapshot`'s reference count — not disposal order — owns
the mapping lifetime. `MappedColumnTests` and
`SessionStoreTests.MappingsSurviveEveryHeldReferenceAndAReadAfterTheLastReleaseIsAManagedFailure`
pin all four.

## Decode baseline — paired A/B, 2026-09-04

Invalid UTF-8 used to be detected by letting a strict `UTF8Encoding` throw
`DecoderFallbackException` and catching it, once per malformed line. `Utf8.IsValid` answers
the same question without the throw. The runner now measures whole-line
`LogcatParser.Parse` over 50,000 synthetic lines in two shapes — every line well-formed, and
every line carrying a malformed byte — asserting the fallback-marked count so an arm cannot
silently stop measuring the path it names.

| Shape | Before | After | Ratio |
|---|---:|---:|---:|
| Valid multi-byte UTF-8 | 260.9 ns/line | 251.0 ns/line | 1.04x |
| Invalid UTF-8 | 5,354.1 ns/line | 403.1 ns/line | **13.3x** |
| Invalid-shape allocation | 69.5 MB / 50k lines | 24.7 MB / 50k lines | **0.36x** |

The valid path is flat within run-to-run noise, which is the criterion that matters there —
the change must not buy the invalid path with the common one. The invalid figure is
whole-line parse, not decode in isolation, so it is smaller than a decode-only comparison
would be and is the number a device emitting binary records actually pays.

## Presentation layer — settled viewport change, 2026-09-03

Ingest, query and export are gated above; the view's own reaction to a query result never
was, which is why an audit measurement claiming that rooting the workspace multiplied a
settled viewport change by about 30x (2.6 ms to 152 ms) went unchallenged for as long as it
did. That measurement was headless, on Avalonia's null drawing backend, and its harness was
not retained.

`bench/VisualCat.UiBench` is the retained replacement. It is a real desktop application:
`UsePlatformDetect().UseSkia()`, a real window on the physical display, one process, one
50,000-entry session, and the same loop of settled viewport changes run against three
configurations back to back.

- **A** — the view model alone, no view.
- **B** — a `SessionWorkspaceView` constructed and subscribed, never rooted in a window.
- **C** — the same view as the content of a shown 1280 x 800 window.

A *settled* change is one whose queued work has drained to `DispatcherPriority.Background`;
layout, render and input all sit above it, so nothing the change queued is still outstanding
when the stopwatch stops. Every configuration uses that one definition, which is what makes
them comparable. The corpus is generated in-process and its SHA-256 is reported with the
results, along with the warm-up and per-batch numbers.

Reference machine as above, Release, three runs of 80 warm-up changes then 8 batches of 40:

| Configuration | Median ms/change | p95 ms/change |
|---|---:|---:|
| A — view model only | 1.0 – 2.1 | 2.3 – 2.8 |
| B — view constructed, never rooted | 4.8 – 5.0 | 5.8 – 6.4 |
| C — view shown in a window | 5.4 – 6.3 | 15.6 – 17.2 |

**The headless result does not reproduce on a real display.** Rooting the view costs about
0.6 – 1.3 ms of median time, not 148 ms, and a complete settled change costs about one 60 Hz
frame at p95. The ~30x figure was an artifact of the null drawing backend, and the number to
carry forward is this one.

What remains is the view's own reaction, A to B, at about 3 ms. Coalescing the thirteen
property notifications one refresh raises into a single dispatcher job — which removed
six redundant `UpdateEntryLoadControls` calls, two redundant `UpdateTimelines` calls and
three jobs matching no case at all — produced **no measurable change** at this scale on this
machine. It was kept because it is strictly less work and makes the view's reaction legible,
not because it bought time. The remaining A-to-B cost is the 500-row rebind of the bound
entry collection; nothing here justifies changing that yet.

This is deliberately **not** a CI gate. The harness needs a real window and a GPU, which the
hosted runners in [`performance.yml`](../.github/workflows/performance.yml) do not have, and
a ratio gate would be the wrong instrument: improving B can make `C / B` fail while the
reader's cost improves. Run it on a reference machine when the presentation layer changes:

```shell
dotnet run --project bench/VisualCat.UiBench -c Release -- --entries 50000 --warmup 80 --batches 8 --output .tmp/uibench.json
```


The original 1.5-million-lines/s full-pipeline target was not supported by measurement: it exceeds the observed safe, mining-enabled pipeline by roughly 20× even on a 12-core NVMe workstation. ADR 0018 therefore replaces it as a release gate with the measured targets above. It remains an optimization direction, not a claim. Larger 10 M / 40 M scale runs remain controlled benchmark jobs rather than source-controlled fixtures.

Generate and run the million-line public baseline outside source control:

```shell
mkdir -p .tmp
dotnet run --project src/VisualCat.Cli -c Release -- generate-test-log --output .tmp/synthetic-logcat.txt --lines 1000000 --seed 42
dotnet run --project bench/VisualCat.Benchmarks -c Release -- .tmp/synthetic-logcat.txt 20
```
