# VisualCat architecture

This document describes the implementation in the working tree. The historical
product/design specification remains in [`docs/design/PLAN.md`](docs/design/PLAN.md),
and focused decisions and trade-offs live in [`docs/adr/`](docs/adr/).
Section references such as `§12.4` in source comments and documents point to
numbered sections of that plan; `R##` identifiers point to its requirement list.

## Design center

VisualCat imports finite files or live Android logcat streams into a durable,
versioned session. Every visible result is a query over an immutable snapshot:
the timeline, statistics, templates, search results, facets, and detail pages do
not rewrite or reinterpret source data when the viewport changes.

The implementation is local-first. Logs and sessions remain on the user's
machine, and no project contains an analytics or telemetry client. See
[ADR 0017](docs/adr/0017-telemetry.md).

## Layer map

Dependencies point inward and are enforced by project references:

```text
VisualCat.Domain
       ↑
VisualCat.Core
       ↑
VisualCat.Application
       ↑
VisualCat.Infrastructure
       ↑
VisualCat.App
      ↗ ↖
Desktop Android

CLI → Application + Infrastructure
```

| Project | Responsibility |
|---|---|
| `VisualCat.Domain` | Immutable value types and contracts for time, entries, filters, queries, sessions, templates, and product identity. It has no project dependencies. |
| `VisualCat.Core` | Logcat parsing and format detection, timestamp resolution, Drain mining, the column store, verification, bitmaps, and pure snapshot queries. |
| `VisualCat.Application` | Use cases and orchestration: bounded ingest, session lifetime, save/export, portable archives, import preview, and application ports. |
| `VisualCat.Infrastructure` | File/growing-file/ADB sources plus settings, retention, diagnostics, and the process-backed ADB client. It implements application ports. |
| `VisualCat.App` | Shared code-only Avalonia presentation, view models, dialogs, timeline/minimap controls, platform bridges, and workspace composition. |
| `VisualCat.Desktop` | Desktop composition root and native Avalonia lifetime. |
| `VisualCat.Android` | Android composition root, on-device source, intent/file-provider integration, permissions, and the reduced mobile layout. |
| `VisualCat.Cli` | Scriptable composition root for index, query, search, export, verify, generation, and ADB commands. |

`tests/` mirrors the testable layers. `bench/VisualCat.Benchmarks` runs the real
ingest/query path, while `tools/VisualCat.GenerateLogs` owns deterministic test
corpus generation.

## Ingest data flow

```text
ILogSource
  → SourceChunk stream
  → byte-faithful line batching
  → bounded raw channel
  → parallel parse/timestamp workers
  → bounded parsed channel
  → deterministic commit order + sharded Drain mining
  → SessionStoreWriter
  → immutable segments + progressive manifest
  → finalized SessionSnapshot
```

`SessionCoordinator` owns this pipeline. `ILogSource` supplies byte chunks and
metadata; implementations exist for finite files, growing files, host ADB,
Android on-device capture, and fault-injection tests. The channels are bounded,
so a fast source cannot grow memory without limit. Parse work may complete in
parallel, but batches are committed in source-sequence order.

Format detection and timestamp policy are explicit. The parser records raw
offsets, original timestamp text, provenance, confidence, continuations,
unknown lines, and ordering defects instead of silently normalizing evidence.
See [ADR 0008](docs/adr/0008-time-policy.md) and
[ADR 0009](docs/adr/0009-continuations.md).

The commit stage assigns deterministic template identities through tag-sharded
Drain miners, appends entries to `SessionStoreWriter`, and periodically publishes
a new manifest generation. Cancellation or source failure leaves a recoverable
partial session. Finalization merges finite input into globally chronological
segments when needed. Live capture retains loss/reconnect evidence; see
[ADR 0011](docs/adr/0011-adb-overflow.md).

## Session storage

A `.vcat` session is a directory. Its `manifest.json` identifies the source,
ingest policy, parser/template versions, snapshot generation, string tables,
template table, process-name ranges, counters, defects, and segment checksums.

Each immutable segment stores fixed-width columns for timestamps, source
sequence, raw offsets, PID/TID, level, string-table IDs, template ID, flags,
confidence, and format. Variable messages and original timestamp text live in a
payload column. Per-level rank bitmaps accelerate filtered counts and selection.
Source-order records separately cover every input byte, including untimed or
unparsed lines.

Standard sessions may refer to an external log. Portable sessions embed
`raw.log`; `.vcat.zip` is only a transport envelope. Save and extraction use
temporary destinations, reject links and traversal, enforce size/count bounds,
verify the complete result, and publish it atomically. The byte-level contract
is specified in [`docs/SESSION-FORMAT.md`](docs/SESSION-FORMAT.md); storage
choices are explained by [ADR 0003](docs/adr/0003-columns.md),
[ADR 0004](docs/adr/0004-container.md), and
[ADR 0007](docs/adr/0007-bitmaps.md).

## Query path

```text
SessionSnapshot + FilterSpec + viewport
  → per-segment active rank bitmap
  → timestamp boundaries / bitmap rank
  → heat map, buckets, facets, statistics, templates, or entry page
  → immutable result tagged with query generation
```

`SessionStore.OpenAsync` validates a manifest and memory-maps segment columns
into a `SessionSnapshot`. `SessionQueryEngine` is stateless: it combines rank
bitmaps for severity/facets, intersects them with time ranges, and returns heat
maps, named buckets, statistics, keyset-paged entries, top templates, searches,
or raw context. Search uses bounded regex timeouts. UI generations prevent a
late async result from replacing a newer filter or viewport.

Time boundaries use sorted timestamp columns; severity counts use bitmap rank
rather than scanning records. Detail paging uses stable keys rather than row
offsets, so inserts from a live snapshot do not scramble navigation. Filter and
density semantics are captured in [ADR 0012](docs/adr/0012-filters.md) and
[ADR 0013](docs/adr/0013-density.md).

## Presentation and platform composition

The shared UI is code-only Avalonia rather than AXAML. This keeps desktop and
Android composition in one tree and lets the custom Skia-backed timeline own
its drawing and interaction math directly. `WorkspaceViewModel` manages tabs;
`SessionTabViewModel` owns one snapshot, filters, viewport, query generations,
and exported state. `MainView` supplies application chrome and platform file
pickers, while `SessionWorkspaceView` composes the timeline, minimap, facets,
templates, details, raw context, and session metadata panes. Its code-only
composition is split across `SessionWorkspaceView*.cs` partials by concern
(facets, interactions, mobile layout, panes, presentation, and raw context),
keeping each behavior area reviewable without introducing a second UI tree.

Desktop and Android are thin hosts around `VisualCat.App`. Platform-specific
files and capture implementations are registered through `PlatformSourceRegistry`
and `StorageFileBridge`. UI/graphics selection and the smaller Android scope are
recorded in [ADR 0002](docs/adr/0002-ui.md) and
[ADR 0016](docs/adr/0016-android.md).

## Where to make a change

| Change | Start here | Also update |
|---|---|---|
| New logcat syntax or format | `VisualCat.Core/Parsing` and domain `LogcatFormat` | Golden parser fixtures, detection tests, CLI format parsing, session compatibility notes |
| New input source | Implement `ILogSource` in `VisualCat.Infrastructure` or the platform host | Source metadata/defect interfaces, coordinator tests, UI/CLI composition |
| New query or aggregation | Domain query/result model, then `SessionQueryEngine` | `SessionTabViewModel`, focused correctness tests, export if applicable |
| New filter dimension | `FilterSpec` and bitmap/query composition | Saved-view validation, chips/facets UI, ADR 0012 semantics, tests |
| New export type | `Application/UseCases/ExportService` | CLI dispatch/help, desktop picker/action, docs and tests |
| Session-format change | Core store manifest/segment contract | Verifier, migration/refusal behavior, `SESSION-FORMAT.md`, new ADR for compatibility |
| New pane or workspace interaction | `VisualCat.App/Views` plus `Presentation` | Keyboard/accessibility behavior and `VisualCat.App.Tests` |
| New desktop RID or package | `tools/package.ps1` and release workflow | `SUPPORT.md`, release notes, ADR 0015 |

## Invariants worth preserving

- Every source byte remains attributable; parsing never invents or drops raw
  evidence silently.
- Published snapshots and segments are immutable.
- Parallelism cannot change entry, template, or query results.
- All untrusted lengths, offsets, paths, archives, regexes, and processes are
  bounded or validated.
- A partial/degraded state is explicit and recoverable.
- Platform code stays outside Domain/Core/Application.
- New public behavior comes with tests and updated format/security/privacy docs
  where relevant.
