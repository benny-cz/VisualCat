# Changelog

All notable changes to VisualCat are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Version numbers correspond to git tags and the GitHub [Releases](https://github.com/benny-cz/VisualCat/releases)
page.

The current stable release is `2.0.2`. Ongoing work is recorded under
`[Unreleased]`.

## [Unreleased]

### Added
- `tools/VisualCat.DemoLog` writes the deterministic demo capture used by every
  screenshot and demo in the documentation: 1,000,156 synthetic `threadtime`
  records over two hours, with a boot burst, intermittent idle windows, a
  network-failure patch, minutes of genuine silence during doze, a memory
  squeeze, an ANR, two Java crashes, and a native tombstone. The device, the app,
  and its hosts are invented; nothing derives from a real capture.
- `docs/assets/android-demo.mp4`, `android-demo.gif`, and `android-companion.jpg`
  document the Android companion, recorded on a physical device.

### Changed
- Every documentation screenshot is recaptured against the million-line demo
  capture instead of the 1,000-line quick-start fixture, so the README shows the
  density the heat map is built for.
- The README covers the Android companion in its own section, with the recorded
  walkthrough, the install and log-permission steps, and the measured on-device
  import rate.

### Removed
- `docs/design/UX-IMPROVEMENTS.md`. Its implemented changes are already recorded
  in the `2.0.2` entry below, and its backlog is tracked outside the repository.

## [2.0.2] - 2026-08-14

### Added
- The workspace now marks the selected entry's instant on the timeline in its
  severity color, keeping plot and table orientation connected while reading.
- `docs/design/UX-IMPROVEMENTS.md` records the implemented interface changes,
  physical-device observations, and a prioritized backlog for future UX work.

### Changed
- Timeline lanes now follow the active severity filter, so the remaining levels
  use the available plot height instead of leaving empty lanes behind. Unknown
  lane membership remains stable while panning because it comes from the full
  session snapshot.
- Facets and Templates now state whether their counts describe the full session
  or the current viewport.
- Entry rows carry a compact severity ribbon, restrained warning/error/fatal
  tints, and bounded in-message highlighting for the active text or regex
  search.

### Fixed
- Android source context now survives app resume and activity reattachment,
  retries interrupted reads, and can read live sidecar files while capture is
  still writing them.
- Android release packaging now verifies the Google Play upload certificate
  fingerprint before accepting an AAB, preventing a valid but unregistered
  keystore from producing another rejected Play upload.

## [2.0.1] - 2026-08-13

### Changed
- Android live capture now shows distinct preparing, connecting, and waiting
  states until the first entries arrive, and the empty timeline explains what
  the capture is doing instead of asking the user to start it again.
- The Android Filters, Plot, Split, Details, Follow, and capture controls now
  center their labels vertically for consistent touch targets.
- The Android application ID is now `com.barebit.visualcat`, replacing
  `com.visualcat.app`, so the companion can be published on Google Play under a
  namespace the maintainer controls. Android treats this as a different
  application: a `2.0.0` APK installed from GitHub is not upgraded in place and
  must be uninstalled, and a previously issued
  `pm grant … android.permission.READ_LOGS` has to be repeated for the new ID.
- The Android target API level is pinned to 36 instead of following the
  installed workload, and `versionCode` is derived from the release version
  (`2.0.1` → `20001`) rather than from a build counter, so Google Play sees a
  strictly increasing code that matches the published version.

### Added
- `tools/package-android.ps1` builds and verifies the signed Android App Bundle
  and APK for a release, and the release workflow publishes the bundle as a
  build artifact for Google Play submission.
- `docs/PLAY-LISTING.md` records the exact Google Play store-listing text and
  app-content answers, and `tools/generate-play-assets.ps1` regenerates the
  store icon, feature graphic, and phone screenshots into `artifacts/play/`.

### Fixed
- Low-volume live sources publish a completed line after the configured latency
  even when no second source chunk arrives, and the first live batch is made
  visible immediately instead of remaining buffered indefinitely.
- Progressive live snapshots no longer relabel the operation as an import or
  hide Stop capture while acquisition is still active.

## [2.0.0] - 2026-07-22

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

[Unreleased]: https://github.com/benny-cz/VisualCat/compare/v2.0.2...HEAD
[2.0.2]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.2
[2.0.1]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.1
[2.0.0]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.0
