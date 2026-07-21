# Release checklist

## One command first

```shell
pwsh ./tools/verify-public-release.ps1 -AllRuntimes -ScanHistory
```

This answers "is this commit mechanically ready to package?" It composes the
existing checks — formatting, Release build, tests, CLI help, documentation and
version consistency, vulnerable packages, packaging with the notice files users
receive, and a secret scan — and exits non-zero naming the first failing stage.
It never tags, pushes, or publishes anything.

Everything below is what a machine cannot decide.

## Enforced automatically

These are gates, not reminders. CI enforces them on every pull request, and the
release workflow's `preflight` job enforces them again on the exact commit being
packaged, so a release cannot be published from a commit that would fail a pull
request:

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
- [ ] Four-hour ADB soak and reconnect/disconnect matrix complete without an orphan process.
- [ ] File, portable, growing-file, partial, degraded, and incompatible sessions are manually exercised.
- [ ] Keyboard, contrast, text scaling, focus, and screen-reader labels are reviewed.
- [ ] Windows package is signed; macOS package is signed/notarized; Linux packages are validated.
- [ ] Android own-app and granted full-device modes are tested on physical hardware.
- [ ] Privacy, support matrix, known limits, migration policy, and third-party notices are current.
- [ ] Components the SBOM reports without license metadata have been resolved by
      hand and explained in `docs/THIRD-PARTY-NOTICES.md`.

## Release rehearsal

Run the release workflow with `workflow_dispatch` before creating the first
tag. Dispatch runs are versioned `2.0.0-preview.<run>` and never publish a
GitHub release or push to NuGet, so the packaging path can be exercised
repeatedly and safely.

Download and inspect the resulting artifacts: Windows, Linux, Intel macOS, and
Apple-silicon macOS desktop archives; the matching CLI archives; the `.nupkg`;
the SBOM; `SHA256SUMS`; and the APK if Android release signing is enabled.
Test at least the primary Windows artifact and one Unix artifact on a clean
machine. Confirm that checksums, file permissions, archive names, embedded
versions, license files, and the documented launch steps agree.

Reproduce the same layout locally with:

```shell
pwsh ./tools/package.ps1 -Runtime win-x64,linux-x64,osx-x64,osx-arm64 -Archive
```

Keep a short release record noting any item intentionally deferred, especially
code signing, notarization, physical Android testing, and soak tests.

Tag only after a rehearsal you were satisfied with. A tag starts publication and
should represent artifacts that are ready to keep available; do not create one to
make a badge green.

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

## Signing and publication secrets

Android releases are installable APKs signed by a persistent release key. Set
the `ANDROID_RELEASE_ENABLED` repository variable to `true` and configure these
GitHub Actions repository secrets before tagging:

- `ANDROID_KEYSTORE_BASE64` — base64-encoded keystore bytes;
- `ANDROID_KEYSTORE_PASSWORD`;
- `ANDROID_KEY_ALIAS`;
- `ANDROID_KEY_PASSWORD`.

The workflow fails closed if any signing value is absent and never uploads an
unsigned or ephemeral debug-signed Android package. Back up the keystore and
passwords outside GitHub; losing the key prevents users from upgrading an
installed APK. Without `ANDROID_RELEASE_ENABLED`, the workflow deliberately
ships desktop, CLI, NuGet, SBOM, and checksum assets without an APK.

Set `NUGET_API_KEY` to publish the `VisualCat.Cli` global-tool package to
nuget.org. Without it, the `.nupkg` is still built, checksummed, and attached to
the GitHub release for inspection and manual publication.

Desktop signing, macOS notarization, store submission, physical-device
validation, and multi-hour soak gates require release infrastructure or hardware
and must be recorded explicitly when deferred.
