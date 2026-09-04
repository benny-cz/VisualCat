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
| `VisualCat.Android` | Android composition root, direct and Wireless-ADB on-device sources, encrypted pairing identity, intent/file-provider integration, permissions, and the reduced mobile layout. |
| `VisualCat.Android.AdbBinding` | Narrow .NET Android binding for the pinned LibADB Android BC transport used only by the guided Wireless-debugging capture path. |
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

### Android full-device capture

Android has two full-device transports and one restricted fallback. If the app
already holds `READ_LOGS` (for example after an explicit developer-side ADB
grant), `OnDeviceLogSource` runs the direct local `logcat` process. A normal
Play-style install does not try to self-grant that privileged permission:
`WirelessAdbService` instead connects to Android's user-enabled Wireless
debugging daemon and `WirelessAdbLogSource` reads one fixed full-device
`logcat -b all` service as the authenticated ADB `shell` identity. If the user
does not enable that capability, the direct source can still capture VisualCat's
own UID only.

The Wireless ADB boundary is deliberately smaller than the underlying library.
The shared UI can pair/reconnect and request an `ILogSource`; it cannot submit a
shell command. Pairing ports and six-digit codes are validated and passed only to
the pairing handshake. The logcat destination is generated from a fixed command;
the only variable is a strictly validated timestamp used to resume after a
transport interruption. The pairing code is neither persisted nor logged.

Android remembers the ADB public identity across captures. VisualCat stores the
corresponding RSA private identity encrypted in `NoBackupFilesDir` with
AES-256-GCM; the wrapping key is non-exportable in Android Keystore. Wireless
debugging must remain enabled during Live capture, the ADB connection is closed
on Stop/Dispose, and reconnect gaps are counted as source defects. The transport
uses short-lived Wi-Fi multicast access only while discovering Android's ADB TLS
service. See [ADR 0016](docs/adr/0016-android.md).

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
[ADR 0007](docs/adr/0007-bitmaps.md). Every raw reader verifies the manifest's
complete recorded prefix on the same open handle it reads, accepts safe append-only
growth, and refuses changed evidence; see
[ADR 0020](docs/adr/0020-verified-raw-evidence.md).

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
(facets, interactions, mobile layout, panes, presentation, raw context, and the
failure state), keeping each behavior area reviewable without introducing a
second UI tree.

Every phone pane size is decided in one place. `Views/MobilePaneAllocation.cs`
holds a pure resolver with no Avalonia in it: it takes the workspace band, the
size-class weights, the measured analysis chrome and the reader's stored share,
and returns the grid tracks for the plot, the minimap, the divider lane and the
details pane. `ApplyMobileLayout` applies that result and writes nothing of its
own. The reason is the one a `GridSplitter` cannot satisfy — the plot is two grid
tracks, the timeline and its minimap, so the boundary the reader moves is not the
boundary between the two tracks a splitter sits between, and mobile recomposition
rewrites those tracks on rotation, mode changes and overview arrival anyway. With
one writer the drag path and the recomposition path resolve through identical
code and cannot disagree, and every limit — the readable plot minimum, the
rendering cliffs, the entry-row floors — is unit-testable without a visual tree.
`Views/MobilePaneSplitter.cs` is the control that reports the drag; it owns no
sizes. The two orientations are separate axes with separate stored shares,
because a height share is bounded by lane bands and entry rows while a width
share is bounded by the plot's label gutter and the message column beside it.

Dialogs are `DialogBody<TResult>` content rather than windows, because one of the
two platforms has no windows: `MainView` is the `IDialogHost` and presents a body
either as a modal `Window` (desktop) or as an in-page card on the overlay layer
it also uses for the Android command sheet. That layer is ordinary content in the
ordinary tree, so automation can walk it and the Android Back gesture — handled
through `TopLevel.BackRequested` — can dismiss it. Product theming lives in
`VisualCat.App/Theme`: Fluent's palette is overridden per variant so selection,
focus, and list surfaces come from the product palette instead of from whatever
accent the device happens to be using.

Desktop and Android are thin hosts around `VisualCat.App`. Platform-specific
files and capture implementations are registered through `PlatformSourceRegistry`
and `StorageFileBridge`. UI/graphics selection and the smaller Android scope are
recorded in [ADR 0002](docs/adr/0002-ui.md) and
[ADR 0016](docs/adr/0016-android.md).

Google Play's in-app update check follows that same seam, and is worth naming
because it is the one platform capability that is not about capture. The Play
client lives entirely in `VisualCat.Android/PlayAppUpdateService.cs` and is owned
by the activity rather than by the view, since Android rebuilds `MainView` on
every recreation while a Play offer can start exactly one flow. Everything that
decides *behaviour* — the per-channel throttles, the snooze, and the rule that an
update may never end a live capture — is in `VisualCat.App/Platform/AppUpdatePolicy.cs`,
which has no Avalonia and no Android in it. That split is not tidiness: Play never
offers an update to a build it did not install, so the real path cannot run in CI
or in the developer loop, and the policy layer is the only part that can be held
to account by a test. See [ADR 0019](docs/adr/0019-app-updates.md).

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
