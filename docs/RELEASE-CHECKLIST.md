# Release checklist

## One command first

```shell
pwsh ./tools/verify-public-release.ps1 -AllRuntimes -ScanHistory
```

This answers "is this commit mechanically ready to package?" It composes the
existing checks — formatting, Release build, tests, CLI help, documentation and
version consistency, vulnerable packages, packaging with the notice files users
receive, CycloneDX SBOM generation and license review, and a secret scan — and
exits non-zero naming the first failing stage.
It never tags, pushes, or publishes anything.

Everything below is what a machine cannot decide.

## Enforced automatically

These are gates, not reminders. CI enforces them on every pull request, and the
release workflow's `preflight` job enforces them again on the exact commit being
packaged. Tagged commits must also be reachable from `main`, so a release cannot
be published from an unmerged commit or one that would fail a pull request:

- formatting, Release build with warnings as errors, and the full test suite on
  Windows, Linux, and macOS;
- `vcat` help matching `docs/CLI-HELP.txt`, checked by `tools/verify-cli-help.ps1`;
- relative Markdown links, required repository files, and README/changelog/
  `Directory.Build.props` version agreement, checked by `tools/verify-docs.ps1`;
- a secret scan over the working tree and all reachable Git history,
  `tools/scan-secrets.ps1`;
- a tag matching `vMAJOR.MINOR.PATCH` with an optional prerelease suffix, and a
  changelog section for the version being released;
- `LICENSE`, `THIRD-PARTY-NOTICES.md`, and a `README.txt` staged into every
  desktop and CLI publish directory before archiving;
- extraction of each finished archive, verifying its layout, notice files, and
  reported version, and running the archived CLI where the runner can execute
  it;
- installation of the packed `.nupkg` from a local feed, then running it;
- a CycloneDX SBOM whose licenses are reviewed, failing on licenses
  incompatible with an MIT-licensed self-contained distribution; and
- GitHub build provenance attestations over every published artifact.

## Manual gates

- [ ] `global.json` resolves to the recorded stable SDK.
- [ ] Sanitized golden corpus and sample scripts reconcile reviewed counts.
- [ ] Corruption checks reject altered manifest, column, bitmap, and raw data.
- [ ] Reference ingest, reopen, heat-map, search, cancellation, and memory measurements are recorded.
- [ ] Four-hour host-ADB soak and Android Wireless-ADB soak/reconnect/disconnect matrices complete without an orphan process, leaked stream, persistent debugging connection, or sustained post-warm-up memory growth. Exercise enough log traffic to prove the bounded Wireless-ADB receive pump recycles/reconnects rather than allowing the third-party queue to grow without limit.
- [ ] File, portable, growing-file, partial, degraded, and incompatible sessions are manually exercised.
- [ ] Keyboard, contrast, text scaling, focus, and screen-reader labels are reviewed.
- [ ] Windows package is signed; macOS package is signed/notarized; Linux packages are validated.
- [ ] Android own-app and Wireless-debugging full-device modes are tested on physical hardware, including first pairing, saved reconnect, Stop/disconnect, background/resume, rotation, and revoked/stale pairing recovery.
- [ ] Cancel is exercised during Wireless-ADB discovery/connection and during the low-level pairing handshake. Discovery/connection must unwind promptly; pairing may remain visibly `Cancelling…` until LibADB's local socket handshake returns, but Live must not start afterward and no authenticated ADB connection may remain.
- [ ] Android Live warning/setup UX matches the Release transport: the scope chooser contains no normal-Play `READ_LOGS` promise/jargon, choosing a scope does not trigger a redundant second disclosure before capture, saved pairing hides the new-code form until explicit recovery, and Back/scrim dismissal during pairing follows the same visible `Cancelling…` lifecycle as the Cancel button.
- [ ] Privacy, support matrix, known limits, migration policy, and third-party notices are current.
- [ ] Components the SBOM reports without license metadata have been resolved by
      hand and explained in `docs/THIRD-PARTY-NOTICES.md`.
- [ ] The exact Play AAB manifest has `INTERNET` and `CHANGE_WIFI_MULTICAST_STATE`, does **not** contain `android.permission.READ_LOGS`, and has no unexpected sensitive permission.
- [ ] The exact Play AAB is inspected for Android-only Maven/JNI dependencies and licenses, including `libadb-android-bc`, Bouncy Castle, and every bundled/transitive pairing component.
- [ ] Wireless ADB pairing code is absent from logs, diagnostics, persisted files, backups, and crash artifacts; the saved ADB identity is encrypted in `NoBackupFilesDir` and removal/clear-data behavior is verified.
- [ ] Play Console Data Safety, App access, permissions, privacy policy, and store description match the audited AAB rather than an older direct-`READ_LOGS` build.
- [ ] If targetSdk is ever raised to 37+, Android 17 local-network permission behavior is re-designed and physically tested before release.

## Release records

> **Historical transport note:** the records through v2.0.6 below predate the
> unreleased Wireless ADB production transport. Their full-device results validate
> the old externally granted `READ_LOGS` path only. They remain immutable release
> evidence, but they do **not** sign off the current Wireless debugging path. A new
> candidate must satisfy the manual Wireless ADB gates above and record a new
> physical-device run before Play publication.

### v2.0.6 — 2026-08-22

- The release-signed APK was installed on a Google Pixel 5 running Android
  14/API 34 and reported application ID `com.barebit.visualcat`, version
  `2.0.6`, and version code `20006`.
- Without `READ_LOGS`, the clean-install explanation accurately said that the
  capture would contain only VisualCat's own records. The capture stayed in
  own-app scope through its quiet heartbeat and finalized cleanly with four
  entries.
- With the adb grant and Android's one-time consent, the same APK resolved to
  full-device scope, initially reported 117 lines/s, and finalized cleanly at
  1,303 entries. Rotation kept the same process and running capture alive.
- In portrait and landscape, Filters, Plot, Split, Details, Fit, Follow and Stop
  capture remained vertically centred, fully labelled and at least 48 dp tall.
  Stop removed the live-only controls, both completed sessions returned after a
  forced process restart, and no crash or ANR was logged.
- The AAB and APK passed the release packager's application ID, API level, ABI,
  16 KB alignment, version, signature-scheme, and pinned Google Play upload
  certificate checks. The temporary `READ_LOGS` grant was revoked after testing.
- The accessibility review, macOS hardware validation, and Windows/macOS code
  signing remain intentionally deferred. The four-hour ADB soak was completed
  during the 2.0.6 development cycle and is recorded in the Android live-test
  report.

### v2.0.5 — 2026-08-21

- The release-signed APK was clean-installed on a Samsung SM-G990B running
  Android 16/API 36 and reported application ID `com.barebit.visualcat`, version
  `2.0.5`, and version code `20005`.
- Without `READ_LOGS`, live capture stayed honestly in own-app scope beyond the
  eight-second decision window, reported a quiet heartbeat after 19 seconds,
  and kept the restricted-scope guidance visible instead of falsely switching
  to full-device.
- With the adb grant and Android's one-time consent, the same build resolved to
  full-device, reported 852 lines at 112/s after seven seconds, and finalized
  cleanly at 1,970 entries. No crash or ANR was logged in either mode.
- The AAB and APK passed the release packager's application ID, API level, ABI,
  16 KB alignment, version, signature-scheme, and pinned Google Play upload
  certificate checks. The temporary `READ_LOGS` grant was revoked after testing.
- The four-hour ADB soak, accessibility review, macOS hardware validation, and
  Windows/macOS code signing remain intentionally deferred.

### v2.0.4 — 2026-08-20

- The release-signed APK was clean-installed on a Motorola edge 60 pro running
  Android 16/API 36 and reported application ID `com.barebit.visualcat`, version
  `2.0.4`, and version code `20004`.
- The More actions sheet exposed its full command set to Android accessibility,
  the system Back gesture closed it, and the first live capture showed the
  privacy and one-time-permission explanation before capture began.
- Full-device capture reached 1,038 entries. The status reported the resolved
  scope, current rate, and quiet heartbeat honestly; capture survived an app
  background/resume cycle, and re-engaging Follow returned to a 30-second live
  edge.
- Light-theme repainting, vertically centred touch controls, Details mode's
  expanded entry list, and the absence of Fit when its plot was hidden were
  visually reviewed on the device. Stop capture finalized cleanly, removed the
  live-only controls, and the named completed capture remained on the home
  screen after a forced process restart. No crash or ANR was logged.
- The AAB and APK passed the release packager's application ID, API level, ABI,
  16 KB alignment, version, signature-scheme, and pinned Google Play upload
  certificate checks. Android left no persistent `READ_LOGS` grant behind.
- The four-hour ADB soak, accessibility review, macOS hardware validation, and
  Windows/macOS code signing remain intentionally deferred.

### v2.0.3 — 2026-08-16

- The release-signed APK was clean-installed on a Motorola edge 60 pro running
  Android 16/API 36 and reported application ID `com.barebit.visualcat`, version
  `2.0.3`, and version code `20003`. Live capture reached 56 entries with a
  visible capture status, then continued updating normally.
- With Follow disabled, entry 59 stayed selected across live snapshots and an
  app background/resume cycle. Its row expanded to show the complete wrapped
  message, and the Entry inspector exposed the full message, copy action, and
  exact source context with the selected raw line clearly highlighted.
- Stop capture finalized cleanly at 163 entries/snapshot 12. No crash or ANR was
  logged during clean install, launch, capture, inspection, resume, or shutdown.
- The AAB and APK passed the release packager's application ID, API level, ABI,
  16 KB alignment, version, signature-scheme, and pinned Google Play upload
  certificate checks.
- The four-hour ADB soak, accessibility review, macOS hardware validation, and
  Windows/macOS code signing remain intentionally deferred.

### v2.0.2 — 2026-08-14

- The release-signed APK was clean-installed on a Motorola edge 60 pro running
  Android 16/API 36 and reported application ID `com.barebit.visualcat`, version
  `2.0.2`, and version code `20002`. Live capture reached its first inspected
  state with 56 entries and a visible capture status instead of an idle prompt.
- Two background/resume cycles preserved the active capture and selected Source
  context. Raw source remained readable while its sidecar grew, Stop capture
  finalized cleanly at 825 entries/snapshot 39, and no crash or ANR was logged.
- The centered workspace controls, filter-driven severity lanes, row severity
  ribbons/tints, and selected-entry/source orientation were visually reviewed on
  the physical device.
- The AAB and APK passed the release packager's application ID, API level, ABI,
  16 KB alignment, version, signature-scheme, and pinned Google Play upload
  certificate checks.
- The four-hour ADB soak, accessibility review, macOS hardware validation, and
  Windows/macOS code signing remain intentionally deferred.

### v2.0.1 — 2026-08-13

- The Android companion was exercised on a Motorola edge 60 pro running
  Android 16/API 36 after the live-capture status and batching changes. The
  application launched, entered capture promptly, displayed incoming own-app
  records, and stopped cleanly without a crash or ANR.
- The four-hour ADB soak, accessibility review, macOS hardware validation, and
  Windows/macOS code signing remain intentionally deferred.

### v2.0.0 — 2026-07-22

- Published Linux x64 CLI and desktop archives passed their checksums and were
  exercised on Ubuntu 24.04 under WSL2/WSLg. CLI indexing, verification,
  statistics, queries, search, and export passed; the desktop completed a
  20-second launch smoke with no missing native dependency or fatal log.
- The published release-signed APK was tested on a Motorola edge 60 pro running
  Android 16/API 36. The installed APK was byte-identical to the release asset,
  cold-launched successfully, and passed both platform-restricted own-app
  capture and an explicitly granted full-device `READ_LOGS` capture. No crash
  or ANR was detected, and `READ_LOGS` was revoked after validation.
- The four-hour ADB soak, accessibility review, macOS hardware validation, and
  Windows/macOS code signing remain intentionally deferred. See the
  [first-release plan](../FIRST-RELEASE-PLAN.md) for exact versions, hashes, and
  observed throughput.

## Release rehearsal

Run the release workflow with `workflow_dispatch` before creating the first
tag. Dispatch runs derive `<VersionPrefix>-preview.<run>` from the checkout and
never publish a GitHub release or push to NuGet, so the packaging path can be
exercised repeatedly and safely.

Download and inspect the resulting artifacts: Windows, Linux, Intel macOS, and
Apple-silicon macOS desktop archives; the matching CLI archives; the `.nupkg`;
the SBOM; `SHA256SUMS`; and the APK if Android release signing is enabled.
Test at least the primary Windows artifact and one Unix artifact on a clean
machine. Confirm that checksums, file permissions, archive names, embedded
versions, license files, and the documented launch steps agree.

Reproduce the same file layout locally with:

```shell
pwsh ./tools/package.ps1 -Runtime win-x64,linux-x64,osx-x64,osx-arm64 -Archive
```

When this runs on Windows, cross-built Unix tarballs are layout checks only:
Windows cannot faithfully create or validate Unix executable mode bits. The
Linux and macOS workflow runners are authoritative for permissions. Run the
command from a normal PowerShell session; on Windows, `tools/package.ps1`
selects the system `tar.exe` explicitly and uses a relative archive filename so
Git for Windows' GNU `tar` cannot misread a drive-qualified path as `host:path`.

Keep a short release record noting any item intentionally deferred, especially
code signing, notarization, physical Android testing, and soak tests.

Tag only after a rehearsal you were satisfied with. A tag starts publication and
should represent artifacts that are ready to keep available; do not create one to
make a badge green.

For the stable release, promote the current changelog entries to the dated
`## [MAJOR.MINOR.PATCH]` section and merge that commit before tagging it.
`tools/verify-docs.ps1` permits exactly the declared `VersionPrefix` to be staged
as the pending release while its tag does not yet exist; older released sections
and tag links remain checked. After CI is green on that commit, create the
annotated tag on the same commit and push it. This avoids an intentionally red
branch build and ensures the tag points at a commit already tested on `main`.

## First public release only

These cannot be represented in the working tree and must be done once, in the
GitHub repository settings.

### Before changing visibility

- [ ] Repository description, topics (`android`, `adb`, `logcat`,
      `log-analysis`, `avalonia`, `dotnet`, `desktop`, `local-first`), and
      `docs/assets/social-preview.jpg` as the social preview are set.
- [ ] Issues are enabled; the wiki stays disabled while repository Markdown is
      the documentation source of truth; Discussions only if the maintainer
      wants another channel to monitor.
- [ ] The default branch and owner match the hard-coded URLs in package
      metadata, badges, security links, and `CODEOWNERS`.
- [ ] Stale automation branches and pull requests are resolved or deleted, so
      the initial activity feed shows current work rather than release
      preparation residue.
- [ ] A maintained scanner (Gitleaks or TruffleHog) has been run over all
      reachable refs, in addition to `tools/scan-secrets.ps1`, and every
      finding is classified rather than blindly suppressed. If a real
      credential ever entered Git, revoke or rotate it first — rewriting
      history is not sufficient.

### After changing visibility

- [ ] Private vulnerability reporting is enabled, so the links in
      `docs/SECURITY.md` and the issue chooser work.
- [ ] Secret scanning and push protection are enabled.
- [ ] Dependabot alerts and security updates are enabled, in addition to the
      existing scheduled version updates in `.github/dependabot.yml`.
- [ ] GitHub Actions defaults to read-only permissions, and first-time external
      contributor workflows require approval.
- [ ] A `main` ruleset blocks force-push and deletion and requires the stable CI
      checks. For a single-maintainer project, keep an explicit administrator
      bypass so a ruleset mistake cannot deadlock the repository, and never
      require a check that only runs conditionally — such a check leaves pull
      requests permanently pending.
- [ ] Every README badge and link works from a signed-out browser, checked once
      immediately and again after badge caches refresh.

### Restoring hidden badges

The README deliberately shows only badges that report real data. Restore each
one only after it has a successful public run to report, and never accept
`unknown`, `repo not found`, or a stale red state as a launch artifact.

- **CodeQL** — trigger `codeql.yml` manually, confirm the analysis and the
  result upload both succeed on `main`, then add:

  ```markdown
  [![CodeQL](https://github.com/benny-cz/VisualCat/actions/workflows/codeql.yml/badge.svg?branch=main)](https://github.com/benny-cz/VisualCat/actions/workflows/codeql.yml)
  ```

  Pin it to `main` but not to `push`, so a failed scheduled scan stays visible.

- **Codecov** — authorize the public repository, confirm OIDC is accepted, and
  confirm a processed report exists for the current `main` commit. During
  diagnosis set `verbose: true`, `disable_search: true`, and
  `fail_ci_if_error: true` on the upload step. Then add:

  ```markdown
  [![non-UI coverage](https://codecov.io/gh/benny-cz/VisualCat/branch/main/graph/badge.svg)](https://codecov.io/gh/benny-cz/VisualCat)
  ```

  Do not commit a private Codecov badge token to make a badge render. Decide the
  steady-state failure policy separately: the most transparent setup is a
  non-required coverage-upload job whose own failures are visible and whose
  Codecov action uses `fail_ci_if_error: true`, keeping a provider outage from
  blocking builds without silently claiming a successful upload. The local HTML
  coverage artifact remains the provider-independent result.

- **Release** — restore only after a signed-out visitor can see a real release:

  ```markdown
  [![Release](https://img.shields.io/github/v/release/benny-cz/VisualCat?display_name=tag&sort=semver)](https://github.com/benny-cz/VisualCat/releases)
  ```

`tools/verify-docs.ps1` fails if the release badge reappears while the
repository has no tags, or if the dynamic Shields license badge replaces the
static one.

## Maintainer identity

Reviewed 2026-07-22 and accepted deliberately: the maintainer's real name and
personal address appear in `LICENSE`, package metadata, `CODE_OF_CONDUCT.md`,
and human commit metadata. VisualCat is a single-maintainer project and a
conduct report should reach a person rather than an unmonitored alias. Security
reports go through GitHub private vulnerability reporting instead.

Revisit this only with a plan: changing it after publication requires rewriting
every human commit's author and committer metadata with a purpose-built tool,
updating or deleting every remote branch, and coordinating the force-push while
there are no public forks or contributor clones. A `.mailmap` changes display in
some tools but does not remove addresses from commit objects.

## Google Play

The Android companion is published to Google Play as `com.barebit.visualcat`.
The Play AAB uses Wireless debugging for full-device Live capture and must not
declare `READ_LOGS`. Debug and controlled non-Play builds can opt into the old
direct permission path, so never infer the Play manifest from a Debug APK.
`docs/PLAY-LISTING.md` is the source of truth for every field of the store
listing and every app-content answer; Play Console is where it is pasted, not
where it is decided.

Build and verify the upload artifact locally:

```shell
pwsh ./tools/package-android.ps1 -Format both `
  -Keystore <path>/visualcat-upload.keystore -KeyAlias visualcat-upload -StorePassword <secret>
```

This produces `artifacts/android/VisualCat-Android-v<version>.aab` for Play and
the matching `.apk` for direct installation, then proves what Play checks after
upload: application ID, versionCode, versionName, `minSdkVersion`,
`targetSdkVersion`, 64-bit code, a real release signature, and 16 KB page
alignment of every shipped ELF. It also rejects any signing key except the
registered VisualCat upload certificate
(`SHA1 37:5C:8D:64:4F:BF:BD:07:DE:4C:1A:71:95:10:6C:94:4B:C6:B8:14`). The
release workflow runs the same script and
uploads the bundle as the separate `google-play-bundle` artifact, deliberately
not as a public release asset — the bundle is not installable and Play re-signs
it, so offering it for download would only confuse.

Before the first submission:

- [ ] The upload keystore and its passwords are backed up off the build machine.
      With Play App Signing, Google holds the app signing key and can reset a
      lost upload key, but the reset still blocks every update until it
      completes.
- [ ] Play App Signing is enabled with a Google-generated app signing key.
- [ ] `docs/PLAY-LISTING.md` matches what is actually in Play Console, including
      the privacy policy URL and the data-safety answers.
- [ ] The store assets in `artifacts/play/` were regenerated from a capture of
      the exact build being submitted.
- [ ] The pre-launch report on the internal or closed track shows no crash, ANR,
      or accessibility blocker before promoting to production.

A personal Play developer account registered after 13 November 2023 must run a
closed test with at least twelve testers opted in continuously for fourteen days
before it can apply for production access. Check which case applies before
planning a launch date; on an affected account, the first upload starts a
two-week clock rather than a release.

Renaming the application ID is a one-way door: Play permanently binds a listing
to its package name, and a rename means a new listing with no reviews, no
install base, and no upgrade path from the old app.

## Signing and publication secrets

Android releases are installable APKs signed by a persistent release key. Set
the `ANDROID_RELEASE_ENABLED` repository variable to `true` and configure these
GitHub Actions repository secrets before tagging:

- `ANDROID_KEYSTORE_BASE64` — base64-encoded keystore bytes;
- `ANDROID_KEYSTORE_PASSWORD`;
- `ANDROID_KEY_ALIAS`;
- `ANDROID_KEY_PASSWORD`.

The workflow fails closed if any signing value is absent and never uploads an
unsigned, debug-signed, or wrong-certificate Android package. Back up the
keystore and passwords outside GitHub; losing the key prevents users from
upgrading an installed APK. Without `ANDROID_RELEASE_ENABLED`, the workflow
deliberately ships desktop, CLI, NuGet, SBOM, and checksum assets without an APK.

Set `NUGET_API_KEY` to publish the `VisualCat.Cli` global-tool package to
nuget.org. Without it, the `.nupkg` is still built, checksummed, and attached to
the GitHub release for inspection and manual publication.

Desktop signing, macOS notarization, store submission, physical-device
validation, and multi-hour soak gates require release infrastructure or hardware
and must be recorded explicitly when deferred. The published CycloneDX SBOM
covers the desktop solution's resolved packages; it does not enumerate the
embedded .NET runtime or Android-only dependency graph.
