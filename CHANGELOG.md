# Changelog

All notable changes to VisualCat are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Version numbers correspond to git tags and the GitHub [Releases](https://github.com/benny-cz/VisualCat/releases)
page.

The current stable release is `2.0.10`. Ongoing work is recorded under
`[Unreleased]`, and development builds carry a `-dev` version suffix so a
screenshot says which build it came from.

## [Unreleased]

### Added
- **Unparsed lines…** in the More menu opens the lines a logcat parse could not
  read as records — in practice every frame of every crash log's stack traces.
  They were always kept, byte for byte, and the only way to see one was to select
  a neighbouring entry, open its source context, and recognise an undecoded `??`.
  A 1,800-line crash log reported "600 entries" on every surface and said nothing
  about the other 1,200. The count line names them now, an import says so once,
  and the card lists them.
- The two-letter codes in a source gutter have a legend on screen — `en entry ·
  mt marker · .. continuation · e? untimed · ?? unknown · !! rejected` — shown
  when a code other than `en` is actually visible. The explanation had only ever
  existed as a tooltip, which a touch device cannot open, so a screen reader could
  hear it and a sighted phone user could not.
- **Load all** on the phone. A 999,885-entry session offered `Load 500 more` and
  nothing else, so reaching the end took 1,999 taps. Above 100,000 outstanding
  rows it asks first, naming the count and that every row is held in memory, and
  it can be cancelled at any point.
- **⏮ / ⏭** beside the search stepper. Reaching the first of 7,181 matches used
  to take 3,578 taps, and the first occurrence of something is the most common
  reason anyone searches a log.
- *Export CSV…* offers the scopes that differ, with their row counts, including
  **everything in this session ignoring the filter** — which the product could
  not previously produce at all. The menu promised a choice and skipped straight
  to the save dialog whenever the plot happened to be fitted.
- `vcat generate-test-log --format` is accepted, and `--format long` now actually
  produces long format instead of silently falling back to threadtime.
- Tapping the search counter — `3,579 / 7,181` — asks which match to go to and
  goes there. It was a label, so the only way to reach a particular match was to
  step to it.
- A chip beside the count row names the records no time-based view can show
  (`1,199 untimed`, `6 unparsed`, or the sum of both) and opens the card that
  lists them. They were counted by the filter and drawn by nothing, so the only
  way to reach them was to already know they existed. The card covers untimed
  records as well as unparsed lines now, and tells them apart by their gutter
  code.

### Fixed
- Dragging the phone Split divider near either edge of the screen no longer
  leaves the app. On a phone using gesture navigation the system's own Back
  strips are about 30 dp wide and the divider — whose whole boundary line became
  draggable in 2.0.10 — reaches into both, so a drag with any sideways component
  went Back and dropped the reader on the launcher. The divider now claims its
  grab band from the platform the way the plot and the minimap already do.
- The app's own **Text scale** setting now reaches the session you are reading,
  not only the chrome around it. Raising it grew the command bar, the tab titles
  and the status line and left the log at exactly the size it was, until some
  unrelated configuration change happened to flush it; Android's own text-size
  control had always worked. Both now take the same path.
- A short, wide workspace composes its plot and details side by side whenever
  both fit, rather than when two command groups happen to fit on one row. The two
  are different widths and both grow with your text size, so at a large text
  scale a landscape phone stacked them into a band that could seat neither and
  **Split** drew a plot with no details at all.
- Three labels that had to live in whatever width was left now drop a whole fact
  rather than being cut through the middle of a word or a number: the compact
  count line (`50,156 view · 5…`), the load-more button
  (`Load 500 more · 49,656 remainir`), and the load-more band itself, which was
  arranged past the bottom of a short details pane and drawn across the status
  line.
- Small square phone controls that size themselves — the time lens's zoom pair
  and the source view's two pan buttons — no longer report a touch target below
  the 48 dp floor. Android rounds a control's two edges to whole pixels
  independently, so one that asks for exactly 48 dp and happens to start
  mid-pixel loses part of a pixel at each end: on a Samsung the **Zoom in**
  button measured 47.6 dp while **Zoom out**, one gap away, measured 48.0. The
  reserve that a severity chip was given in 2.0.8 is now a named rule the whole
  family shares, rather than a number written at the one control that had been
  measured.

- **Choose what Live captures** puts its two options above the disclosure that
  supports them. At Android's larger text sizes the 90-word paragraph pushed both
  radio buttons off a card that gave no sign it scrolled: at 180 % the
  VisualCat-only option left the accessibility tree entirely, and at 200 % neither
  option was painted, leaving cancel or a Wireless-debugging pairing flow as the
  only discoverable actions. The disclosure is one labelled control away and the
  sentence that answers "is this safe" stays on the card.
- One Back press takes down one layer. On a phone, Back both closed the Live
  scope card and left the app, because the card's Cancel answered the key event
  and the platform's own callback then found nothing left to close. A stock
  edge-Back gesture did the same to the More sheet for a different reason: the
  gesture begins as an ordinary touch, so the scrim dismissed the sheet on
  touch-down and the same gesture arrived as Back a moment later.
- The heat map releases its claim on the platform's edge gesture while a sheet or
  a dialog is over it, and while the session is fitted — a viewport that already
  spans the session cannot be panned, so the claim took system Back away and gave
  nothing back.
- Removing a filter is a 48 dp target. The chip's `×` measured 15.6 × 16.4 dp,
  the label beside it did nothing at all, and **Clear all** — the recovery from
  missing that target — was 40 dp tall. The whole chip is now the control.
- The entries list keeps its four-row floor as text grows. The pane budgeted four
  *design* rows at every text size while the drawn row grew by half, so at 200 %
  a fifty-thousand-entry log showed two and a half rows; where four whole rows
  genuinely cannot fit beside a readable plot, the workspace composes Details and
  the mode row says why.
- The count line no longer contradicts itself. `3,425 match` beside `2,225 in
  session` was true — the middle number counts records a time range cannot hold —
  and now says which population each number counts.
- `Copy raw` and **Entry** agree about whether the entry they are both named for
  is still in scope, and an entry kept across a filter change says so and offers
  the way back. One was disabled while the other opened an Error record under a
  Fatal-only filter with nothing on screen admitting it.
- A confirmation no longer moves the control that raised it. Copying an entry
  raised a notice that took 140 px out of the workspace, so a second tap at the
  same place opened the entry instead of copying again.
- The welcome screen centres its hero instead of ending 45 % down the display
  with the rest left blank, and **Recent captures** with nothing stored says so
  and offers to start a capture, rather than explaining a three-way status
  taxonomy for items you do not have beside a disabled *Open*.
- Reopening a finished capture shows the whole capture. It came back on Follow's
  30-second live-edge window — an empty plot and six zero counters, which reads
  as "this recorded nothing" — because that window had been written into the
  stored view.
- An empty file says it is empty, once, instead of reporting the same
  "no supported logcat format" sentence in three places at the same time.
- The clipped leftmost session tab can be closed. Its close button was disabled
  and silently ignored taps; one tap now brings the tab into view and the next
  closes it.
- Panning to either end of a zoomed plot no longer shows four seconds of time the
  session does not contain — at a close zoom that was four-fifths of the screen,
  under an axis for an interval in which the log did not yet exist.
- The live capture's status line leads with its scope when the scope is
  restricted, so the clause that explains why almost nothing is arriving is not
  the one the ellipsis takes; the notice's remedy is a button rather than the tail
  of a paragraph, and it changes tense when the capture stops.
- The notice lane costs the workspace two lines instead of four and offers
  **More** when there is more, rather than being a scroll container tall enough
  to take 122 dp of the screen and still cut its last line in half. Expanding it
  sizes to the message.
- On Android 14 and earlier the status and navigation bars carry the app's own
  ground instead of a grey platform scrim. The window was asking for transparent
  bars while two window flags told Android to paint its own backing and ignore
  the request, so a light-themed app showed a band measuring rgb(133,137,142)
  where it had asked for #F4F7FC. Android 15 and later enforce this themselves
  and were never affected.

## [2.0.10] - 2026-08-28

### Added
- Phone Split workspaces now have a touch-sized, accessible divider between the
  plot and details, in **both orientations** — a horizontal one in portrait and a
  vertical one between the landscape columns. The whole boundary line is
  draggable, not just the marked grip. Each orientation keeps its own remembered
  position, because their limits are unrelated: portrait is bounded by readable
  lane bands and entry rows, landscape by the plot's label gutter and the message
  column beside it. Both preserve the minimap with the plot, survive mode
  changes, rotation and restarts, support keyboard and range automation, and
  return to responsive automatic sizing by double tap, **Home**, or
  **Appearance & timeline**.

### Fixed
- The timeline header no longer runs past the right edge of a narrow plot. It
  drops a whole fact — the resolution, then the duration — rather than letting
  the last one be cut mid-glyph, which the landscape divider made reachable.
- The phone plot/details divider now follows a fast drag. It tracked the pointer
  by summing per-event deltas, which are measured inside the divider itself and
  so are only correct when a layout pass lands between two touch events: a quick
  flick moved the divider a fraction of the distance or not at all, and a drag
  taken past a limit could not be brought back. Positions are now measured
  against the press in a frame that does not move with the divider.
- Android back-gesture exclusions now stay within the platform height budget,
  keep the minimap whole, protect the useful lower plot band, span the narrow
  edge margins reliably, and are republished when the activity resumes.

## [2.0.9] - 2026-08-26

### Added
- Android builds installed from Google Play now notice when a newer VisualCat is
  published and offer it in the status lane. The check is an inter-process call
  into the Play Store app; VisualCat opens no socket, sends no identifier and
  reaches no VisualCat server, and a build Play did not install is never checked
  automatically. A new **Check for updates…** command asks on demand on both
  platforms — and on a side-loaded build says so honestly and offers the GitHub
  releases page instead of pretending Play can help.
- An update is never allowed to cost a recording. No offer is raised over a
  running Live capture, the Play flow that takes over the screen is never started
  during one, and an update that has finished downloading waits, saying that
  stopping the capture is what installs it, because installing restarts the app.
- Added a concise, privacy-safe Android video for Google Play's
  `FOREGROUND_SERVICE_DATA_SYNC` declaration. It demonstrates user-started Live
  capture continuing while VisualCat is backgrounded, then stopping and saving
  the local session.

### Changed
- The Android version-code scheme gained an explicit build counter
  (`major*1000000 + minor*10000 + patch*100 + build`, so 2.0.9 is `2000900`).
  The previous scheme could not express two builds of one version, so a second
  `2.1.0-alpha` upload would have been refused by Google Play. Every new code is
  higher than every code the old scheme could produce, so no installed build is
  stranded.

### Fixed
- An update path with no safe flexible-download flow no longer offers to open the
  Play Store during a Live capture. The Store's own Install action can restart the
  process just like an immediate in-app update, so the app now waits for Stop in
  both cases and re-checks capture state when the button is tapped.
- Update downloads and manual checks now use a persistent progress treatment,
  failures use the error treatment, and action buttons ignore double taps while a
  Play operation is already running. A Store or browser launch that fails is
  reported instead of leaving the reader with an inert tap.
- Disposed Play clients no longer publish a late listener callback into a rebuilt
  activity, and listener teardown tolerates the Store process disappearing first.
- Android release packaging now passes signing passwords through temporary
  password files. This preserves whitespace exactly for `jarsigner`, keeps the
  values off the child-process command line, and fixes AAB signing with the real
  Play upload credentials.
- Release packaging derives `VersionPrefix` from the requested semantic version,
  passes the checked-in Android build counter explicitly, and verifies the AAB's
  version code as well as the APK's. Release builds also fail closed if the
  device-only fake Play manager is enabled accidentally.
- CI and release preflight now serialize test projects as well as tests inside
  the headless UI assembly. Running the store stress suites beside Avalonia could
  starve its atomic manifest replacements into intermittent Windows file-lock
  failures and destabilize the Ubuntu desktop-core job.
- The Avalonia headless suite now keeps one compositor for the test assembly, as
  recommended by Avalonia 12 for flaky teardown/re-initialization. Long runs no
  longer occasionally rebuild the renderer from a thread that does not own it.
- Finalizing a session, saving it, or extracting a portable archive now waits
  through a bounded Windows search-indexer or antivirus lock. A brief scan of a
  newly written manifest or directory can no longer discard an otherwise
  complete import at its final atomic rename, and cancellation remains prompt.
- Dismissing a downloaded update now means something. The prompt to install one
  is repeated on every return to the app for as long as the update is pending, so
  Dismiss used to be undone by the next glance at the screen — and the only answer
  that would have ended it was the restart the reader was declining. It now waits
  the same per-channel interval an offer does, and a manual check still reaches it.
- Waving away a failure or a download's progress no longer counts as declining an
  update, which had been silencing the next real offer for a week.
- A long status message no longer runs off the right edge of the Android command
  bar. It was in a column sized to its own content, so the ellipsis it asked for
  could never apply; on a phone the text painted straight through the wordmark
  and off the screen. The same message is also no longer echoed into the command
  bar on Android, where the status lane below already shows it in full.
- Statistics and facet counts no longer rescan the whole session on every
  published snapshot. A published segment cannot change, so its contribution to
  the level totals and to each facet tally is cached on the segment and folded
  instead of recomputed, and the per-refresh cost stops growing with the
  capture. One statistics pass over six million entries fell from about 1.4
  seconds to about 3 milliseconds, and a live capture reaching twenty million
  entries completed 656 view refreshes where it had managed 59. The process
  facet is keyed on the session's process-name table as well, so a pid observed
  under a new name retires the tallies that resolved it under the old one.
- Following a growing file no longer allocates its one-mebibyte read buffer on
  every poll. An idle follow spent about four mebibytes a second — some fifteen
  gibibytes an hour — on the large-object heap, and the continuous gen2
  collections that implies, to deliver nothing at all.
- The growing-file regression gate now counts buffer construction directly
  instead of sampling a process-wide allocation counter. Coverage collectors
  and test-host background work could contaminate that counter and make the
  Windows and Ubuntu CI jobs fail even when the source reused its buffer.
- Stateful headless UI tests now run serially and evaluate responsive
  breakpoints against an explicit text scale. Parallel tests were replacing the
  same platform callbacks, accessibility scale, and temporary-session root,
  intermittently failing Windows CI or writing into a directory another test
  had already removed.
- Closing a session tab, or the application, while **Load all** is still walking
  the view no longer waits for that walk to finish. The session lifetime is
  cancelled before disposal takes the load lock, so the tab closes at once
  rather than staying on screen and unclosable until the last row arrives, and
  shutdown no longer hangs with no window left to explain why. A reader's own
  Stop still means what it meant, and the rows already loaded stay.
- Android no longer leaves the previous `MainView` attached to the platform when
  it recreates the activity. Two views were answering resume and pause: both
  resumed live views and re-ran queries for a workspace nobody could see, and
  both wrote their own open-workspace list to the same settings file, so the
  abandoned view's stale tab set could be the one restored. The newest view
  takes the subscriptions over, and the view it replaces stops watching its
  workspace.
- The diagnostics sink is now unpublished before it is closed, so a failure
  recorded after shutdown no longer writes into a disposed logger and the static
  handle no longer holds it for the life of the process.

## [2.0.8] - 2026-08-24

### Added
- Android Live capture can continue while VisualCat is backgrounded or the
  screen is locked. A private ongoing notification shows the capture state and
  provides a **Stop and save** action; notification permission remains optional
  and capture also appears in Android's Active apps surface when it is denied.

### Fixed
- Wireless-ADB recovery now resumes from the latest genuine logcat timestamp
  with a one-second overlap and a timezone-safe numeric cursor. It stays bounded
  after repeated out-of-order entries or a device-clock rollback, avoiding both
  large replays and silent gaps.
- Android 15 and later now end an overlong background `dataSync` capture
  gracefully at the operating system's six-hour limit, preserving the session
  instead of letting the foreground service fail abruptly.
- Android release verification now recognizes the numeric `dataSync` enum that
  current `bundletool` emits for compiled App Bundle manifests, so a correctly
  declared foreground service no longer fails packaging after a successful
  signed build.
- Android Wireless-debugging setup no longer implies that split screen can keep
  every OEM's short-lived pairing code alive. It names Android's **Pairing
  unsuccessful** result, asks for only one fresh-code retry, and offers the
  immediate VisualCat-only fallback instead of trapping readers in an expired-
  code loop.
- Android in-page sheets now consume the keyboard's actual occlusion rectangle
  when `AdjustResize` leaves the app viewport unchanged. The whole sheet and its
  pinned decision row move above the IME, and Wireless-debugging setup scrolls
  the active port or code field wholly into the remaining space. If a landscape
  keyboard leaves less room than the sheet header and 48 dp actions can
  physically occupy, the sheet preserves a top-aligned editor viewport and
  reveals its deferred actions again when the keyboard closes.
- Wireless-debugging pairing codes now request the proven digit-only Android
  keyboard and carry explicit password masking, so the temporary value is not
  rendered or exported as plain accessibility text on affected devices.
- The Android home hero now scrolls when enlarged text exceeds a short
  landscape viewport, keeping its final action and build/provenance line above
  the gesture bar while preserving the centered layout at ordinary sizes.
- Mobile severity-filter chips now reserve one extra logical pixel so Android's
  independent accessibility-edge rounding cannot shrink a nominal 48 dp touch
  target below the platform floor on fractional-density devices.

## [2.0.7] - 2026-08-23

### Added
- Android full-device Live capture can now use the device's own **Wireless
  debugging** connection on clean Play-style installs. The guided setup normally
  pairs once with Android's pairing-code panel, stores the reusable ADB identity
  encrypted with an Android-Keystore-protected AES-GCM key, and reconnects with
  the saved pairing on later captures.
- The Android capture transport now has an explicit full-device / VisualCat-only
  choice, accessible pairing fields, a returning-user **Connect saved pairing**
  default action, Developer-options shortcut, and detailed failure/re-pairing
  guidance.
- Wireless ADB live streams record reconnect gaps and resume from the latest
  validated logcat timestamp after a transport interruption instead of silently
  presenting a discontinuous stream as complete.

### Changed
- A normal Android install no longer depends on obtaining `READ_LOGS` for
  full-device capture. VisualCat never self-grants that privileged permission;
  Wireless debugging runs the fixed `logcat` stream as Android's authenticated
  ADB shell. An externally granted `READ_LOGS` remains supported as an advanced
  developer shortcut and continues to select the direct on-device source.
- Wireless ADB capture disconnects when Live stops. Pairing is reusable, but
  Wireless debugging must remain enabled only while a Wireless ADB capture is
  active. A dedicated transport pump continuously drains LibADB into VisualCat's
  own bounded 16 × 64 KiB receive queue; queue pressure recycles the stream and
  resumes from the last complete timestamp instead of allowing LibADB's internal
  receive queue to grow without bound. Connection/discovery operations are
  serialized, and cancellation interrupts the Java waits that the upstream API
  exposes. The upstream pairing socket itself has no cancellable timeout, so its
  local handshake is treated as a short operation and remains a physical-device
  release-test gate.
- Direct `READ_LOGS` copy now distinguishes Android 12 from Android 13+: only
  newer Android versions may show the separate one-time device-log consent, and
  the UI no longer promises that a consent sheet appears on every capture.
- Android Live access sheets now match the Wireless-debugging transport end to
  end: the scope chooser uses plain language instead of privileged-permission
  jargon, a completed scope/setup disclosure no longer triggers a redundant
  third confirmation, saved-pairing users see re-pairing only as an explicit
  recovery action, and Back/scrim dismissal waits for an in-flight pairing
  attempt to return before the sheet closes.

### Fixed
- Phone sheets and status notices now show a complete lower edge instead of
  looking clipped against the gesture-navigation boundary. Sheets are fully
  bordered and rounded with a small screen-edge inset; long notices retain all
  of their text in an internal scroller while preserving the compact workspace.
- The landscape analysis actions now keep the Time picker and equal-width Copy
  and Entry buttons wholly inside their pane, including at 130% Android text.
  The entry inspector owns its clipping and scrolling boundaries, and narrow
  short layouts stack plot and analysis content rather than painting them into
  the same row.
- Phone home content now uses stable 3 × 2 severity and 2 + 1 action grids so
  OEM density and font differences cannot leave orphaned or partially visible
  controls. Its hero headline also wraps at enlarged Android text instead of
  retaining the full accessible name while visibly clipping its last word.
- Wireless-debugging pairing errors now appear directly beside the port or code
  field they explain and scroll into view with that field. They precede the
  editor so both remain visible above taller OEM keyboards; on narrow phones at
  enlarged text, validation no longer lands after the whole form where the
  pinned action footer can hide it.

### Security
- The runtime Wireless ADB API exposes no general-purpose shell execution.
  Pairing codes are transient and never logged or persisted; the only command
  destination is a fixed `logcat` invocation with a shape-validated resume
  timestamp. The ADB dependency is version-pinned and its final Release AAB is a
  mandatory dependency/license/native-artifact audit item.

### Tests
- Added headless UX contracts for first-use disclosure, saved-pairing behavior,
  default reconnect action, and accessible Wireless-debugging pairing fields.
- Added physical-device release gates for first pairing, saved reconnect,
  transport interruption/recovery, revoke-and-repair, high-volume soak, own-app
  fallback, externally granted direct capture, and sensitive-data logging checks.

## [2.0.6] - 2026-08-22

### Fixed
- Every button in the analysis pane is a full touch target on a landscape phone.
  The compact layout a short screen uses gave its own controls a lower floor than
  the rest of the app: the Entries, Insights and Entry tabs, Copy, the entry
  inspector, the sort selector and "Load more" were all 42 dp tall against the
  48 dp the platform asks for and every other control in the product already met.
  Landscape is the orientation you turn a phone to in order to read a log, and
  those are the whole command set of the pane you read it in. They now meet the
  same floor as everything else, which costs the list about a quarter of one row.
- Changing the device's text size, or closing a session, no longer leaves a copy
  of the workspace's command row behind. On a short screen the workspace's
  buttons - Filters, Plot, Split, Details, Fit - move up beside the app's own, into
  a row the app owns. Changing the system text size rebuilds the workspace, because
  every text size in it is settled while it is being built, and the rebuilt one
  added its buttons beside the old ones instead of in place of them: one extra copy
  per change, stacked exactly on top, each belonging to a workspace that had stopped
  listening to its session. Four changes left five copies, and a screen reader
  walked all of them. Closing a session did the same thing for the same reason. The
  row now holds one set of buttons - the selected session's - and a workspace that
  is finished with hands back everything it was holding.
- The workspace commands are reachable on a short, narrow screen. On a screen that
  is short but wide - a phone in landscape - the app's own buttons and the
  workspace's own buttons share one row, which saves a band of height worth having.
  A screen can be short without being wide, though: in split-screen, in a small
  window, or under a tall notice. There the shared row had about half the width the
  two groups need, and rather than clipping they were drawn on top of each other -
  the row read "Plot", "s", "Spl", "Fit", "ils", with Filters underneath the mode
  buttons and no reachable touch point at all, so the filter drawer could not be
  opened. Sharing a row is now decided by whether the row is wide enough to be
  shared, and by how large the reader's text is, since both groups are made of
  labels. A landscape phone keeps the row it had; a narrow one gives the workspace
  commands a band of their own, which is what a tall portrait screen already did.
- Follow and Stop capture stay usable while recording on a landscape phone. The
  app's buttons and the workspace's buttons share one row when the screen is wide
  enough, and the capture controls join them - but how wide the row is was measured
  on the whole screen, and the row does not get the whole screen: the app's own
  buttons take their share first. On a common landscape phone that left the row
  about 500 dp for roughly 640 dp of controls, and Follow was squeezed to 23 dp,
  less than half a usable touch target, while a recording was running. The decision
  now asks the row how wide it actually is. A screen with room keeps everything on
  one line; one without gives the capture controls a line of their own.
- The filter drawer shows its filters on a short screen. Its severity toggles and
  time-lens controls sat in two columns, which suits a landscape screen and leaves
  about 175 dp per column on a narrow one - enough to wrap seven severity toggles
  into three rows and push the last one off the bottom of the screen, where not
  even a screen reader could find it. The scrolling part of the drawer had also
  been squeezed to a single line, so the card showed the words "TIME LENS" and
  "SEVERITY" and none of the controls they name. The columns now depend on the
  width available, and when the card is short the drawer spends its own chrome -
  the query caption and some padding - to keep a full row of severity toggles
  visible. All seven now fit on one row.
- "Load more" stays inside the entry list it belongs to. The header row it moves
  into on a short screen was reserving width for the count line beside it, except
  that on a short screen the count line has already moved to the tab strip - so
  the reservation protected nothing and pushed the last control in the row past
  the right edge of the pane instead. It was measured 34 dp wide against its
  natural 49, ending exactly on the edge of the screen, and it overflowed in
  landscape too, which had gone unnoticed because a fully loaded session has no
  "Load more" to show. The header is now laid out for what is actually in it.
- A capture can be stopped while the app is telling you something. A notice takes
  its height out of the workspace, and a long one - the notice shown when Android's
  log-access consent is declined, for instance - made the workspace short enough to
  select the compact layout built for landscape. That layout packs control rows
  side by side, which is right on an 800 dp landscape screen and wrong on a 360 dp
  portrait one: the packed row ran off the edge of the screen and left Stop
  capture 15 dp wide, a sliver against the right edge that would not answer a tap.
  The same squeeze put the plot and the entry list into two columns of a 360 dp
  screen, leaving the list about 131 dp and clipping its actions to 12 dp.
  Whether to pack rows side by side is now decided by the width available rather
  than by the height, which is what it always depended on. A landscape phone keeps
  the layout it had; a short, narrow workspace gives the capture controls their own
  full-width row back and stacks the plot above the list, the way an ordinary
  portrait workspace already did.
- A notice no longer drops the sentence it exists to deliver. It was capped at six
  drawn lines with nothing to say more had been cut, and the declined-consent
  notice needs about eleven on a phone - so the words that never appeared were
  "Tap Live again and choose the option that allows access.", which is the entire
  point of showing it. A screen reader had always been given the whole message; now
  the eye can reach it too, by scrolling, and the notice still takes no more of the
  screen than it did before.
- Zooming into a session that holds a single entry no longer takes the whole app
  down with it. A one-entry session has no duration, so the plot opened with the
  same instant at both ends of its axis, and the next zoom asked for a viewport
  narrower than nothing, which the transform could not build. It was reachable two
  ways: a large import whose first progressive snapshot held one row, and a
  one-line file opened directly. Both are closed at once now. A fitted viewport is
  widened to a drawable span before it is ever shown, so the degenerate state is
  not reachable on open; and every route that zooms - double-tap, pinch, the
  wheel, the keyboard, Fit - goes through one bounds check instead of five, so a
  sixth route cannot be added without it. Zooming in on a session too short to
  zoom into now simply stops, which is what it should always have done.
- A development build no longer calls itself the released version. Release-ness
  was inferred from the build configuration, which made the one configuration a
  release candidate has to be tested in the only one that claimed to be the
  release - so a screenshot of a build made from unreleased code was
  indistinguishable from a screenshot of the release itself. It is an explicit
  signal now, one that only the release pipeline passes; everything else, Release
  builds included, is a development build and says so. The empty state also names
  the build rather than only the version, so a screenshot answers "which build is
  this?" on its own.
- Failures no longer show the reader the framework's own words - or, in a trimmed
  Release build, its untranslated resource keys. An invalid search pattern
  produced "MakeException, (unclosed, 9, InsufficientClosingParentheses" on the
  phone, and the same class of text could reach the screen from any action that
  failed. Search patterns are validated where they are typed now, and a pattern
  that cannot compile is refused with a sentence - there are more "(" than ")"
  (position 9) - shown beside the field, in error ink, with the previous results,
  the filter chips and the status line all left exactly as they were, because none
  of them has stopped being true. Everywhere else, a message that still looks like
  a resource key is replaced by a product sentence and a stable code, and the raw
  text goes to the diagnostic bundle, where it is useful.
- Restoring a session at startup no longer paints a cancellation as a failure. Two
  refreshes racing at launch could leave one of them cancelled while it was still
  queued, and that cancellation escaped as far as the shell, which showed "Startup
  settings: The operation was canceled." over a workspace that had in fact
  restored perfectly - and left the tab reading "Opening" for the rest of its life,
  with every row already on screen. A superseded refresh is silent for its whole
  life now rather than only most of it, a caller who genuinely cancels still hears
  about it, and no route can leave a session stuck in the opening tense.
- The status line, the session tab, Session info and Recent captures now agree
  with each other about what a session is. A live capture reported itself as
  Importing; a capture killed mid-recording reported Ready in the workspace,
  interrupted in the list, and Importing in the details. Completion is one derived
  fact now that every surface phrases from, so an interrupted session says so
  everywhere and opens with what it actually recovered - and offers to keep it,
  export it, or delete it. Transient messages travel one route now too, so what is
  on screen and what a screen reader is told are the same string, and a message
  about a query that has been superseded goes away instead of standing over
  results it no longer describes.
- Every control a thumb lands on is at least 48 dp. The empty state's three hero
  links measured 18.8 dp - on the first screen anyone sees - and session tabs,
  their close buttons, the filter drawer's clear action and, most recently, the
  spin buttons on every number field in Settings were all under the floor. The
  glyphs are unchanged; the hit areas are not. The floor can be measured in a
  headless test now, which is what let the last twelve of them be found at all.
- A landscape phone no longer hides the rows and the controls it was asked for.
  The entries list dropped below its own row floor and let the status line clip
  its last row; the soft keyboard covered the whole filter drawer, including Reset
  and Done; and on a short landscape screen the keyboard sliced the query field it
  had just been raised for, across the middle. The workspace keeps three whole
  rows and a visible gap above the status line now, at heights down to 341 dp, and
  the query row is the drawer's own first band in every state - so it is never the
  thing that gets cut, and never reparented, which is what silently withdrew the
  keyboard from a field the reader was trying to type into.
- On a phone using gesture navigation, dragging the plot near either edge no
  longer leaves the app. The heat map runs to within 12 dp of both edges and its
  main gesture is a horizontal drag, which put it underneath the system's Back
  gesture: a pan begun in the plot's outer strip went Back, to the home screen.
  The plot and the minimap brush claim those two rectangles for themselves now,
  and Back keeps working everywhere else on the screen.
- An on-device capture labels each record with the buffer it actually came from.
  About four records in five were attributed to a buffer they had never been in,
  which made the buffer facet worse than useless for narrowing a capture down.
- The pre-capture explanation no longer promises a consent sheet that cannot
  appear. Without the log-read permission no sheet is coming - it is not one an
  app can ask for - and the dialog says so now, gives the exact adb command that
  grants it, and offers to copy it; with the permission held, the sheet appears
  exactly when the copy says it will.
- A live capture costs much less while nobody is watching it. Publication cadence
  was the same whether or not the screen was on, so a capture left running
  overnight spent hours redrawing a plot, re-running its queries and rewriting its
  manifest for nobody. Refreshing stops now when the workspace leaves the screen,
  and resumes immediately - and up to date - when it comes back. The capture
  itself never pauses and no record is lost either way.
- Pressing the capture control while a capture is already running no longer starts
  a second one. Two captures could be running with only one of them visible, so
  stopping the one on screen left the other recording invisibly; the control takes
  you to the running capture now, and says that is what it will do.
- Follow no longer clears the entry you are reading when its window moves past it.
  A selected row ageing out of the live 30-second window is not a deselection, and
  the entry, its source bytes and the timeline caret survive it now - with a line
  saying where the entry went, and an action to go back to it.
- Deleting cached sessions says how much it will delete before it does anything,
  protects every session that is open, and no longer runs its automatic pass
  before the sessions it must protect have been restored.
- Smaller things: captures are named by an unambiguous start time rather than one
  that reads as a date, and that name fits a phone tab whole; counted nouns agree
  with their number, so a one-entry capture no longer says "1 entries kept"; a
  filter that matches nothing explains itself and offers to clear itself instead
  of rendering a blank pane; search-match navigation is reachable by touch and by
  a screen reader, not only by keyboard; the source-context gutter counts from 1,
  like every other tool that numbers lines; files opened from another app show the
  provider's own display name rather than an internal cache filename;
  stored-capture cards no longer expose an app-private path to accessibility
  services; a notice arriving under a thumb no longer turns a second tap into an
  unrelated action; and "vcat generate-test-log --help" prints its help instead of
  writing a 90 MB file.
- Stop capture now answers the press, keeps answering it, and says what became
  of the recording. On a capture large enough to matter — four hours and 543,767
  lines, on the phone this was found on — the button appeared to do nothing at
  all. It sprang back from "Stopping…" to "Stop capture" within a second, and the
  status line went back to reading "Capturing", because the two things that
  describe a running capture both kept firing after the source had been told to
  end: the pipeline's progress reports, which continue for as long as the
  read-ahead takes to drain, and the one-second heartbeat, which speaks up
  whenever the source has gone quiet — and a stopped source is quiet by
  definition. Nothing about the screen distinguished a stop that was working from
  a press that had not registered, and the only thing left to try was pressing
  the button again.
  Worse than the confusion, the state never resolved. A capture the reader
  stopped fell through every branch that reports an ending, on the reasoning that
  the reader was looking at the button they had just pressed — but the status
  line, Follow and Stop capture all read off that state. So a session whose
  manifest had been written, whose 543,763 entries were complete and reopenable
  on disk, went on presenting itself as a live capture with a Stop button that by
  then did nothing at all, and went on doing so for as long as the workspace took
  to reopen it: over two hours, in the case this was written for.
  A stop is now sticky — once begun, nothing that describes a running capture can
  undo it — and it says which part of the ending it is waiting on, counting the
  seconds as it goes: draining the lines still in the pipeline, compacting,
  writing the session index, and reopening the finished session. It leads with
  the elapsed clock, before anything a phone's one-line status bar can clip away,
  because a number that visibly moves is the answer to "is this stuck?". Once the
  manifest is written the line says so — "capture saved" — since the question
  behind a second press is whether the recording is safe. Every capture then
  lands somewhere final and says what it kept, and the controls for a capture
  that has ended go away. A capture that reaches its own stated duration is given
  the same account of itself, and a view query that supersedes the last refresh
  no longer turns a finished capture into a failed one.

## [2.0.5] - 2026-08-21

### Fixed
- An on-device capture no longer says it is reading the whole device while it is
  reading only its own log lines. The Android companion settled that question by
  reading the pid off each arriving record, and it took the pid to be the third
  whitespace-separated token — which is where `-v threadtime` puts it, but not
  where `-v threadtime,UTC` does, because the zone modifier inserts the offset as
  a token of its own. `+0000` then read back as pid 0, no process on a device has
  pid 0, and so every record on the phone — the app's own included — looked as
  though somebody else had written it. A capture restricted to this app's own
  records announced itself as full-device 0.4 seconds in, latched the verdict
  where nothing could revise it, and then sat delivering nothing: 84 lines
  against the 62,909 a real full-device capture would have taken in the same
  seventeen minutes, while the status line, the session name and the session
  details all agreed it was seeing everything. Worse than the wrong label, it
  threw away the only thing that would have helped — the notice saying that
  `READ_LOGS` has to be granted over adb, and the command that grants it.
  The pid is now read by a record's shape rather than by counting to three, under
  the rules the log parser already followed, and no record is read at all unless
  its whole prefix is present and every part of it is what it should be. A
  capture that does not hold `READ_LOGS` no longer asks the question at all:
  logd never hands an app another uid's records, so there is nothing in that
  stream to find and no way left to find it wrongly.

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
  not. On Android 13 and later the system can require separate per-use consent for
  direct device-log access; declining does not fail — `logcat`
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

[Unreleased]: https://github.com/benny-cz/VisualCat/compare/v2.0.10...HEAD
[2.0.10]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.10
[2.0.9]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.9
[2.0.8]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.8
[2.0.7]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.7
[2.0.6]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.6
[2.0.5]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.5
[2.0.4]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.4
[2.0.3]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.3
[2.0.2]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.2
[2.0.1]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.1
[2.0.0]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.0
