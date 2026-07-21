# Changelog

All notable changes to VisualCat are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Version numbers correspond to git tags and the GitHub [Releases](https://github.com/benny-cz/VisualCat/releases)
page. Until the first release is tagged, dated entries describe the current
baseline and **Unreleased** collects changes staged on top of it.

## [Unreleased]

### Added
- `LICENSE` (MIT), `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, a `CHANGELOG.md`, and
  GitHub issue/PR templates.
- Assembly and package metadata (authors, product, description, repository URL,
  SPDX license) plus SourceLink in `Directory.Build.props`.
- README pitch, full-resolution analysis and start-page screenshots,
  a real-app demo loop, project branding, download guidance, and
  CI/CodeQL/coverage/license/.NET/release badges.
- A current implementation guide in `ARCHITECTURE.md` and a desktop-only
  solution for contributors who do not need the Android workload.
- Automated checksummed desktop and CLI archives, a release-key-signed APK,
  dependency updates, CodeQL analysis, and coverage reporting.

### Changed
- Renamed `scripts/` to `samples/`; the authoritative plan now lives at
  `docs/design/PLAN.md`.
- Updated tests, performance instructions, sample generation, and third-party
  notices for the new public repository layout.
- Made synthetic fixtures byte-identical across platforms, aligned CI with the
  documented solution-level commands, and exposed the build version in the CLI
  and desktop start page.

### Removed
- Personal `concat*` helpers, `global.json.old`, three superseded `V2-PLAN*`
  drafts, and the large committed log fixtures (regenerate them with
  `tools/VisualCat.GenerateLogs`).

## [2.0.0] - 2026-07-21

Initial public baseline — the greenfield .NET 10 rewrite described in
[docs/design/PLAN.md](docs/design/PLAN.md). This entry documents the state
captured for the first public release; the `v2.0.0` tag and downloadable
binaries are pending.

### Added
- Span-oriented parsers for `threadtime`, `time`, `brief`, `long`, and `epoch`
  formats, with year and microsecond timestamps and full raw-byte coverage.
- Reproducible year/time-zone inference, rollover handling, and unclamped
  out-of-order timestamps.
- Bounded channel-based ingestion with deterministic ordering, progressive
  manifests, cancellation, and recoverable partial sessions.
- A checksummed, little-endian, memory-mapped column store with immutable sorted
  segments, stable compaction, string tables, and seven severity rank bitmaps.
- Pure snapshot queries for heat maps, named buckets, facets, statistics,
  search, templates, raw context, and keyset-paged details.
- Deterministic tag-isolated Drain-style message mining.
- Verified standard and portable saves, saved views, recent sessions, opt-in
  cache retention, raw/CSV/report exports, and traversal-safe `.vcat.zip`
  transport.
- File, growing-file, in-memory fault-injection, host ADB, and Android on-device
  sources, with persisted PID/name ranges and reconnect evidence.
- A cross-platform Avalonia/Skia desktop workspace and a reduced Android
  companion.
- The `vcat` CLI, seeded log generator, verification tool, test suite, and
  benchmark harness.
- A written [security model](docs/SECURITY.md), [privacy statement](docs/PRIVACY.md),
  [support matrix](docs/SUPPORT.md), [session-format spec](docs/SESSION-FORMAT.md),
  and 18 architecture decision records.

[Unreleased]: https://github.com/benny-cz/VisualCat/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.0
