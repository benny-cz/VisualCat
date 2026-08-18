# Changelog

All notable changes to VisualCat are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Version numbers correspond to git tags and the GitHub [Releases](https://github.com/benny-cz/VisualCat/releases)
page.

The current stable release is `2.0.3`. Ongoing work is recorded under
`[Unreleased]`.

## [Unreleased]

### Added
- The Android companion reaches its secondary commands through a bottom sheet
  instead of a flyout menu. A flyout is a popup: with the menu open and its eight
  items plainly on screen, an accessibility dump contained none of them, so with
  a screen reader Open session, Open portable archive, Recent sessions, Share,
  Export CSV, Appearance, Session cache and the diagnostic bundle were
  unreachable, and synthetic taps closed the menu without activating anything.
  The sheet is ordinary content: every command is a named control with a
  description, a command that needs a session says so instead of being tappable
  and silent, and the system Back gesture closes it.
- Dialogs work on the phone. Recent sessions, Appearance & timeline, Session
  cache and the diagnostic-bundle confirmation were `Window`s guarded by a
  desktop-only check, so on Android each returned immediately and did nothing.
  Each is now a dialog body the host presents — a modal window on the desktop, an
  in-page card on a platform that has no windows.
- The empty state lists the captures this device already holds. A process
  restart drops every open tab, and the first screen showed only a static
  severity legend while the sessions sat on disk behind a menu item inside a
  flyout; the four most recent are now one tap from the screen a cold start
  opens, named after the capture rather than after their storage folder.
- `Fit` sits beside the Plot/Split/Details selector on a phone. Zooming is not
  filtering, and fitting the session is the most frequent thing anyone does to a
  plot — it lived only inside a drawer labelled "Filters", two taps deep.
- An on-device capture explains itself before Android asks. Tapping `Live` went
  straight to the system's "Allow VisualCat to access all device logs?" prompt,
  whose only affirmative is one-time access, with nothing having said what the
  log was for or where the data goes. Shown once and remembered.
- A failed import shows what happened in the workspace instead of building a
  complete set of inert panes over an empty store: the whole reason, the step
  that platform can actually offer, and the two actions worth taking. The
  format-detection message no longer advises phone users to use a desktop
  dialog — the reason is stated by the application layer and the remedy by
  whoever is talking to the user.
- A debug deploy of the Android companion grants `READ_LOGS` over adb as part of
  installing. The permission is `signature|privileged|development`, so Android
  never prompts for it and the app cannot request it, while a deploy uninstalls
  first and drops it — every `dotnet run` therefore left the on-device capture
  silently restricted to the app's own log lines. Debug configurations only; set
  `GrantReadLogsOnDeploy=false` to opt out.
- A restricted capture now says so. The session pane carries a `Log scope` row
  explaining that only this app's own lines are visible and giving the adb
  command that widens it, and the status bar adds `own-app scope only` once a
  capture has been silent long enough to look broken rather than merely quiet.

### Changed
- The product owns its accent colour. Fluent picks the platform accent up as
  `SystemAccentColor`, so on Android every selection highlight, focus border and
  tab underline took the device's Material You colour — a brick red on the phone
  this was found on, which reads as an error tint under every selected row and
  cannot be tested from a screenshot taken on another device. Selection, focus
  and list surfaces now come from the workspace palette in both themes, which
  also replaces Fluent's neutral grey list background (`#2B2B2B`) that painted a
  slab into the navy workspace.
- Tall buttons centre their labels. `VerticalContentAlignment` defaults to
  stretch, which leaves a label in the top third of any control taller than its
  text — every 48 dp touch target — and it had been fixed one control at a time.
  It is a style now, so the next tall button added is right by default.
- Session tabs are one scrolling row of chips. The built-in strip wraps and gives
  each item the full width, so three open sessions became three full-width rows —
  about 300 px of a phone viewport gone before any content — and each close
  button sat outside its own tab. Titles are truncated in the middle, so
  `northlight-transit-20260812.txt` keeps the date that distinguishes two
  captures of the same app.
- The status bar speaks about logs rather than about storage. "Snapshot 16",
  "Committing", "import capacity" and "committed" are column-store words; a
  reader watching an import wants to know how much is readable and how fast the
  rest is arriving. The live line also puts the rate before the source
  description, because the ellipsis takes whatever is last and the rate is the
  most volatile number in the app.
- Entry rows say what they count. `5 view · 5 session` above a status bar reading
  `Ready · 10 entries` labelled three different numbers with two words; each now
  carries what it counts, and a selected timeline bar is "in this bar".
- Facet counts state their whole scope in the visible label. The qualifier was in
  a tooltip, which a touch device never shows, so a phone only ever read the
  misleading half: with a search active, `COUNTS · WHOLE SESSION` sat above
  counts that were already filtered.
- The source pane defaults to scrolling on a touch screen, labels its toggle with
  the action it performs, and states the current mode in visible text rather than
  in a tooltip. The first natural "scroll the trace" swipe used to select a block
  of text.
- Timestamps show the precision the capture actually carries. Logcat prints
  milliseconds unless a capture asked for microseconds, so three constant zeros
  were taking width from the message on the row where the message is already
  being clipped. The first entry with sub-millisecond detail widens the column
  for the whole session.

### Fixed
- An import ends showing the whole session. The viewport was seeded from the
  first progressive snapshot, when the session genuinely held one entry, and
  nothing ever re-fitted it — so every import finished with one row and an empty
  plot beside a minimap already drawing the whole capture, and recovering meant
  finding `Fit` inside a drawer labelled "Filters". A viewport nobody has touched
  follows the session; the first zoom or pan hands it to the reader for good.
- Entry rows fill their width. A one-line budget under word wrapping draws the
  text up to the last break opportunity that fits rather than the text that fits,
  so rows ellipsised at a third of their width with two thirds empty beside them
  — and whether it happened depended on where the break opportunities fell, which
  is why it looked arbitrary: `Intent {` forbids a break after the brace and
  filled the row, `Zntent Z` offered one and clipped. Only the selected row,
  which has a real multi-line budget, wraps.
- Double-tapping the plot zooms without also re-scoping the entry list. The press
  that zoomed returned early without recording a drag origin, so the release that
  ended it looked like a stationary click and selected a cell — one gesture both
  zoomed and silently replaced the table with the contents of one bar, complete
  with a chip the reader never asked for.
- The source pane always resolves. A read that was superseded, or that outlived
  the pane it was started for, left "Reading the source bytes around this entry…"
  on screen with no timeout, no error and no way to ask again; there is now a
  floor under every read, an interrupted read says so, and a failed one offers
  Retry.
- The inspector's context line describes the selection as it stands. It was
  written once, from the cell count the load carried, so it went on reading
  "First of 27 in the selected bar" after the bar had been released and the row
  had been picked straight from the table.
- Landscape is a layout rather than a squeezed portrait. The display-cutout inset
  rendered as a pure white band down the whole edge of a dark-themed app, because
  the window background was AppCompat's default; lane labels were drawn at a
  fixed size that overflowed their own lanes and sat half a row from the stripe
  they named; and the analysis rail pinned each item to a width narrower than its
  label, laying two tabs side by side and the third beneath.
- Axis labels are no longer drawn underneath the minimap. The plot demanded a
  minimum height, which a star-sized row cannot refuse, so with three tabs open
  or a capture row added the control was arranged taller than its cell and its
  own bottom — where the labels live — was overdrawn. The plot's bands give way
  instead.
- The time axis always reads as a scale. Choosing a tick interval is not enough
  to guarantee two labels: where the aligned instants fall decides how many land
  inside the viewport, two that do can still be too close together to print, and
  at very narrow spans no interval can help — a one-microsecond viewport contains
  exactly one whole microsecond. A narrow plot therefore still showed a single
  label, an instant with nothing to say what a given width represents. When the
  ticks cannot supply two labels the viewport's own ends are labelled instead,
  which is a scale by construction. Label spacing continues to follow the width
  of the labels themselves, so the dates a whole-session view prints do not
  overlap.
- A screen reader hears log entries instead of debug dumps. A row with no
  automation name of its own falls back to its content's `ToString()`, and the
  content is a record whose generated text is a field-by-field dump including the
  session guid and the raw span; rows now announce level, tag, time and message.
- Session-dependent commands are enabled only when there is a session. Share,
  Export CSV and Save each returned silently with no session loaded while their
  controls stayed fully enabled, and the empty state offered a `SHARE` link that
  by definition could never do anything.
- The system Back gesture works. The workspace claimed every Back press whether
  or not it had anything to dismiss, so the app could not be left with the
  gesture at all — only with Home. Back now closes the sheet, the dialog or the
  filter drawer, and falls through to the platform when there is nothing to
  close.
- Contextual actions keep their slots. `Load next 500` appearing pushed `Copy
  raw` onto a second line, so two taps in the same place hit different controls;
  the always-present actions have a row of their own and the chip strip's height
  is reserved for any session that can be filtered.
- Live captures stop offering to follow a source that has closed. `Follow` and
  `↓ New data` survived the end of a capture, where neither means anything.
- Smaller fixes: the empty plot no longer tells a reader who has just opened a
  file to open a file; the severity chips carry a visible legend on mobile,
  including `?`; the process facet group no longer restates the PID group when no
  process name could be resolved; the plot's caret no longer marks an entry the
  current view has filtered out; the redundant `Search` button on mobile is a
  clear affordance instead, and the soft keyboard resizes the workspace rather
  than sliding the drawer's footer out of reach; the selected source line lands
  inside the view with the lines that follow it; the empty minimap frame no
  longer floats over a session with nothing to overview.
- Re-engaging Follow opens a window on the live edge instead of keeping a
  whole-session span. Fitting the session releases Follow, so turning it back on
  is the ordinary way to return to the live edge — and it preserved the span it
  found, leaving the capture "following" across the whole session, where a
  second of new data occupies a fraction of a pixel and the plot is
  indistinguishable from one that has stopped. A narrower span is a deliberate
  choice of how much history to keep beside the live edge and is still
  preserved.
- A live capture no longer fails to finalize. Publishing a progressive snapshot and
  finalizing the session both rewrote the manifest through one fixed temporary
  path with no sequencing, so two writes in flight together opened the same file
  exclusively and the second failed the whole ingest with
  `UnauthorizedAccessException` — reliably on a capture short enough for the two
  to overlap. Manifest writes are serialized, each takes a temporary of its own,
  readers no longer block the replace, and the replace itself tolerates the
  millisecond a scanner or reader can hold the destination.
- An ADB capture is parsed in the zone the device actually agreed to write. The
  format ladder degrades one modifier at a time and can land below the `UTC`
  modifier, where the device emits local time, but the policy was pinned to UTC
  regardless — so on any device that fell that far, every timestamp was silently
  wrong by the device's offset. The format is now settled before the policy is
  chosen, and a followed file is read in the local zone like an imported one
  rather than being assumed to be UTC.
- Live captures are read in the device's own clock. They are parsed in UTC
  because that is the format logcat is asked for, but rendering them back in UTC
  put the newest entry a whole UTC offset in the past — two hours, on a UTC+2
  device — so a running capture with Follow engaged looked like it had stopped.
  Storage is unchanged; imported files keep their policy zone so a rendered row
  still agrees with the raw line behind it. The session pane names both zones.
- The capture status no longer claims lines are arriving after a source falls
  silent. The rate is measured over the last second instead of averaged across
  the whole session, and a heartbeat reports how long it has been quiet —
  previously a burst at connect time left `36/s` on screen indefinitely, because
  nothing reports progress while a source produces nothing.

## [2.0.3] - 2026-08-16

### Added
- An entry inspector completes §14.9's "full logical/raw content in an
  inspector". Selecting a row now shows its whole message: the selected row
  alone opens to four wrapped lines on a phone and two on the desktop, while
  every other row keeps its single clipped line, and the `Full entry` action —
  a second tap on the selected row, a double-click, or `Enter` — opens the
  inspector, which carries the entry's identity, its complete message, `Copy
  message`, and the source bytes below. Until now the table showed one clipped
  line and the rest of a message was unreachable, which for `logcat -v long`
  captures meant every body line of a multi-line record was invisible.
- `tools/VisualCat.DemoLog` writes the deterministic demo capture used by every
  screenshot and demo in the documentation: 1,000,156 synthetic `threadtime`
  records over two hours, with a boot burst, intermittent idle windows, a
  network-failure patch, minutes of genuine silence during doze, a memory
  squeeze, an ANR, two Java crashes, and a native tombstone. Lines arrive in
  bursts from one subsystem at a time rather than on a fixed cadence, so the
  capture stays ragged at milliseconds per pixel instead of collapsing into a
  solid band once a reader zooms in. The device, the app, and its hosts are
  invented; nothing derives from a real capture.
- `docs/assets/android-demo.mp4`, `android-demo.gif`, and `android-companion.jpg`
  document the Android companion, recorded on a physical device.

### Changed
- The mobile `Source` tab is now `Entry`, and the desktop `SOURCE CONTEXT` pane
  is `SELECTED ENTRY`: both lead with the message and keep the raw bytes as a
  collapsible section beneath it. On a phone the pane is one scrolling column,
  so the message and the bytes no longer compete for the same space.
- The selected entry's own line in the source dump is drawn bold in its
  severity color and scrolled into view. The `▶` marker it already carried
  disappeared as soon as the lines wrapped, leaving the reader to find their
  line by eye.
- The desktop message column shows the full message as a tooltip when the cell
  is clipped.
- Every documentation screenshot is recaptured against the million-line demo
  capture instead of the 1,000-line quick-start fixture, so the README shows the
  density the heat map is built for.
- The README covers the Android companion in its own section, with the recorded
  walkthrough, the install and log-permission steps, and the measured on-device
  import rate.

### Fixed
- A live snapshot no longer deselects the entry being read. Refreshing replaces
  the entry collection, which cleared the list selection and with it the
  timeline's caret for that row; the inspector now holds its own entry and the
  selection is restored by entry id once the rows are back.

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

[Unreleased]: https://github.com/benny-cz/VisualCat/compare/v2.0.3...HEAD
[2.0.3]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.3
[2.0.2]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.2
[2.0.1]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.1
[2.0.0]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.0
