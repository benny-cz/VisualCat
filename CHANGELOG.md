# Changelog

All notable changes to VisualCat are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Version numbers correspond to git tags and the GitHub [Releases](https://github.com/benny-cz/VisualCat/releases)
page.

The current stable release is `2.0.4`. Ongoing work is recorded under
`[Unreleased]`, and development builds carry a `-dev` version suffix so a
screenshot says which build it came from.

## [Unreleased]

## [2.0.4] - 2026-08-20

### Added
- The Android companion honours the device's own text size. Every font size in
  the product was a fixed logical value, so setting Android's accessibility text
  scale to 130% produced a pixel-identical app; the platform scale is now the
  baseline and the in-app *Text scale* setting multiplies it rather than
  replacing it. A reader who has already told the operating system they need
  larger text does not have to find a second switch six controls into a settings
  sheet.
- The entries list has a floor. It is what the product is for and it was the
  smallest thing on screen: 173 px of a 2340 px display in *Split*, less than one
  192 px row, and 60 px with a notice showing. The analysis pane now measures its
  own chrome and reserves four entry rows in *Split* and six in *Details*, and
  the plot gives way down to the smallest band it can still be read in.
- A capture is named for when it started. Every on-device capture was called
  "On-device logcat" — in the tab strip, in *Recent sessions*, in the empty
  state, in *Session cache* and in the suggested export filename — so two open
  tabs were two identical chips with nothing to choose between them.
- Settings choices with two or three options are shown as segments instead of
  dropdowns on a phone. A combo box answers by opening a popup over the page, and
  in the in-page sheet presentation that popup went somewhere unusable: tapping
  *Default export order* scrolled the form back to the top and drew its list over
  *Timeline normalization*, four fields away, while the control it belonged to had
  scrolled off the screen.
- The workspace can publish to the notice lane. `Copy raw`, the Insights *Copy*,
  and filtering or muting a template wrote their result somewhere off screen and
  said nothing at all; the lane was reachable only from the application shell.

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
- The command bar follows the theme. It painted one fixed near-black gradient in
  both variants as the application's identity band, which on a phone set to light
  is a `#0C1422` slab between a white system status bar and a white page, present
  from a cold start. The band keeps its shape and its wordmark; only the ground
  under them follows the variant, and the platform's own status and navigation
  bars follow it too.
- A theme change repaints the whole product rather than four surfaces. Switching
  a running app to light left the active session tab at 1.10:1, the timeline axis
  at 1.05:1 and the selected row's metadata at 1.11:1 — text that is not hard to
  read but invisible — with the minimap a solid navy rectangle on a white page,
  and only a restart put it right. The workspace palette is now a set of theme
  resources that styles name instead of capturing, both plots repaint on the
  variant change, and the whole path runs once more after the top level has
  settled its variant, so a cold start in light mode cannot be served dark
  values.
- Scrolling surfaces lay their content out beside the scrollbar instead of under
  it. Fluent draws the bar over the content and the bar takes pointer input: in
  *Appearance & timeline* its page-down region measured 718 px tall over the
  right edge of two combo boxes, so a tap squarely on a chevron paged the form
  instead of opening the dropdown.
- `Load next 500` is a footer under the list it extends, rather than a full touch
  row above it, and it says how many rows are left.
- `Filters`, the workspace mode selector and `Fit` share one touch row. Five
  buttons were taking 306 px of a screen where the entries list was getting 173,
  because the row measured 334 px against the 324 a portrait phone has and `Fit`
  fell to a second full-height row. The live-capture controls have a band of
  their own that exists only while there is a capture.
- Numbers and dates are formatted in the interface's own culture. On a Czech
  phone the English interface printed `19,76 min`, `34,63 KiB`, `18.08.2026` and
  `59 640` with a non-breaking-space separator — two conventions in one line,
  which makes both look accidental. Dates and times use ISO patterns, which is
  already what the timeline axis and the entry rows draw.
- Every list describes a stored session in the same words, and the words say what
  they mean: "complete" and "still being written" rather than "ready" and
  "partial", neither of which was explained anywhere in the product.
- A confirmation that records something durable — a file exported, a session
  shared, a diagnostic bundle written — stays on the notice lane until it is
  dismissed or replaced. Six seconds and then nothing left a reader who looked
  away during an export with no evidence that it had run.
- Time-axis labels print milliseconds only where a span can distinguish them. A
  twenty-minute view drew `.000` under every tick.

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
- An on-device capture no longer claims to be reading the whole device when it is
  not. On Android 13 and later the system asks for consent on every capture and
  its only affirmative grants one-time access; declining does not fail — `logcat`
  starts, the app receives its own process's records and nothing else, and every
  permission check the app can make still reports success. A declined capture
  went on describing its source as "On-device full-device logcat" while
  delivering 24 lines in 40 seconds. The source now says what it is actually
  seeing, from the stream it already has, and a restricted capture is reported on
  the notice lane as the failure it is, with the route to widening it.
- The entry row's metadata line is readable in the light theme. It resolved
  `TextMuted(dark)` — `#8FA5C4` on `#E9EFF7`, 2.17:1 — on a cold start in light
  mode, because the styles that own it captured a brush when the list was built
  and were never rebuilt.
- The Insights list and both stored-session lists announce their rows instead of
  their C# records. `TemplateSummary { TemplateId = 16, … }` and a
  `TemporarySessionInfo` complete with the private storage path and the session
  guid were being read out in full.
- The session status row's accessible description tracks the status. It kept
  whatever it was first given, so a finished session was announced as "Starting
  capture" and one of 59 640 entries as "Importing…".
- A sheet takes the workspace out of the accessibility tree. The scrim caught
  pointer input, so touch was safe, but nothing marked the sheet as modal and
  assistive technology walked straight past it into *Open log*, *Live*, the mode
  buttons and every entry row underneath.
- Numeric spinner buttons have names. All eight across two sheets announced
  themselves as `Avalonia.Controls.PathIcon`.
- *Recent sessions* no longer offers *Open* with nothing selected. Tapping it was
  accepted and produced no result, no message and no state change.
- The workspace mode survives a text-size or display-size change. Those are not
  in the activity's `configChanges` and should not be — every font size has to be
  re-measured — so Android recreates the activity, and the reader's choice of
  *Plot*, *Split* or *Details* went with the view that owned it.
- A nearly empty live capture no longer draws a plot at a precision it does not
  have. One entry produced a one-microsecond window, two axis labels reading the
  same instant, and a *Follow* that never widened as lines arrived. `Fit` is
  clamped to the resolution the plot has pixels for.
- `Fit` leaves with the plot. In *Details* it stayed present and enabled, acting
  on a surface that was not on screen and costing a share of the row it sits in.
- The filter drawer has padding at both ends of its travel. At rest the *Time
  lens* buttons were cut by the viewport edge; one swipe put the **QUERY** heading
  under the card's top border instead.
- Two sheets no longer cut their last control in half against a pinned decision
  row. There is a divider, a gap the last field finishes inside, and — on a
  touch device, where there is no pointer to hover — a scrollbar that stays
  visible to say there is more below.
- *Session cache* decides on one line. *Delete eligible sessions… / Cancel* sat
  on the first line with *Save policy* orphaned below the cancel, the destructive
  action widest and first.
- A disabled numeric spinner recedes like every other disabled control. The
  spin buttons are `RepeatButton`s, which the rule that removed Fluent's disabled
  fill everywhere else did not match, so a greyed-out decrement was the brightest
  thing in its row.
- The *Entry* tab's empty state keeps its button inside its own card, instead of
  drawing it half outside and through the rounded border.
- Cache-policy fields recede while automatic cleanup is switched off, rather than
  staying fully interactive under a switch that governs them.
- The session tab strip is inset to the same gutter as every other band, instead
  of running full-bleed with the first tab's rounded corner on the screen edge.
- A short viewport keeps the minimap, in a slimmer band beside the plot rather
  than under both panes, and the analysis pane clips to its own band instead of
  painting entry rows through the status line.
- `Entry ⤢` keeps its label in landscape instead of collapsing to a bare glyph
  with 200 px of the row unused beside it.
- The status line says that it can be expanded. Tapping it opens the whole
  sentence — the only route to the end of a clipped failure message — and only
  the accessible help text had ever mentioned it.
- *Export CSV…* describes the question it asks rather than promising one of the
  answers.
- The displayed version tracks the build. It read 2.0.3 on a build made long
  after 2.0.3 shipped; the version has moved on and a non-release build says so
  in the version itself.

- Closing a session tab can no longer crash the workspace. The view answers the
  view model through the dispatcher, so a change raised while the tab was alive
  could still be queued after it closed, and the redraw then read a session that
  was being torn down — `ObjectDisposedException` from inside the plot, which on
  the UI thread takes the application with it. A closed session no longer drives
  a view, the snapshot is unpublished before it is released so no reader can be
  handed a disposed one, and whether the plot needs an Unknown lane is answered
  once when a snapshot is published rather than by walking every segment's
  severity bitmaps on each redraw.
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

[Unreleased]: https://github.com/benny-cz/VisualCat/compare/v2.0.4...HEAD
[2.0.4]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.4
[2.0.3]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.3
[2.0.2]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.2
[2.0.1]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.1
[2.0.0]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.0
