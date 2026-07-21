# Changelog

All notable changes to VisualCat are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Version numbers correspond to git tags and the GitHub [Releases](https://github.com/benny-cz/VisualCat/releases)
page.

No version has been released yet. The entries below describe the work that the
first tagged release will carry as `2.0.0`; until that tag exists, VisualCat is
a source preview.

## [Unreleased]

Initial public baseline — the greenfield .NET 10 rewrite described in
[docs/design/PLAN.md](docs/design/PLAN.md).

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
- Project branding, screenshots, an animated product demo, and platform-specific
  download and verification guidance.
- A contributor-friendly desktop solution that does not require the Android
  workload, plus a current implementation guide in `ARCHITECTURE.md`.
- Automated checksummed desktop and CLI archives, optional release-key-signed
  Android packages, CodeQL analysis, and per-layer coverage reporting.
- A written [security model](docs/SECURITY.md), [privacy statement](docs/PRIVACY.md),
  [support matrix](docs/SUPPORT.md), [session-format spec](docs/SESSION-FORMAT.md),
  and 18 architecture decision records.
- Release preflight, final-archive smoke tests, notice files inside every
  archive, a CycloneDX SBOM, build provenance attestations, and the
  `tools/verify-public-release.ps1` one-command local preflight.

[Unreleased]: https://github.com/benny-cz/VisualCat/commits/main
