# VisualCat first binary release — blockers and execution plan

This document is self-contained. It assumes no knowledge beyond the repository
itself and can be read months from now by anyone with push access.

It answers three questions:

1. What must exist for VisualCat to have a real, downloadable `v2.0.0` release
   on GitHub for every supported platform?
2. What is actually stopping that today, with the evidence for each claim?
3. In what exact order should the remaining work be done?

**Audit basis:** branch `public-release-hardening`, commit `26e9335`
("Make the public-release path verifiable and internally consistent"), audited
2026-07-22 on Windows 11 with .NET SDK 10.0.101. Every fact labelled *verified*
below was produced by running a command during that audit. Re-verify before
acting: dependencies, GitHub settings, and remote refs change independently of
this file.

## Implementation status — 2026-07-22

The repository-side items in Phase 0 have been implemented on
`public-release-hardening`, together with the following hardening discovered
while executing the plan:

- release targets now come from one checked-in matrix used by both packaging and
  final asset verification;
- tagged release commits must be reachable from `main`, and prerelease tags are
  explicitly published as GitHub prereleases;
- release notes are rendered with the exact version, Android APK version codes
  increase between workflow runs, and provenance covers every uploaded asset;
- the changelog check permits only the declared `VersionPrefix` as a pending
  stable release, removing the commit/tag ordering deadlock without allowing
  older untagged releases;
- SBOM generation invokes its pinned tool directly, and the one-command local
  preflight now includes SBOM and license-policy validation;
- Windows local packaging explicitly selects the system `tar.exe`, while macOS
  CI disables AppleDouble metadata; and
- platform documentation now states the unsigned/notarized, bare-macOS,
  Linux-dependency, packaging-format, and SBOM coverage limitations.

The full plan was completed on 2026-07-22. The hardening was merged to `main`,
the repository was made public, Android release signing was enabled with an
independently backed-up key, and NuGet.org publication was explicitly deferred
while keeping the verified `.nupkg` on GitHub Releases. A manual rehearsal and
the `v2.0.0-rc.1` prerelease both passed before the stable `v2.0.0` tag was
published from commit `c0cce515351cad5febdb437131a1649557eaff0b`.

The stable release workflow, main CI, and CodeQL completed successfully. The
published checksum manifest, representative desktop and CLI archives, locally
installed `.nupkg`, CycloneDX SBOM, Android package identity and signing
certificate, signed-out release page, and SLSA provenance attestation were then
verified independently. The sections below retain the original audit evidence
and execution rationale so future maintainers can understand why each change
exists.

Post-publication operating-system and hardware validation also completed on
2026-07-22:

- the exact published Linux desktop and CLI archives passed their release
  checksums under Ubuntu 24.04 on WSL2. Both were executable x64 ELF binaries
  with no missing `ldd` dependencies. The CLI reported
  `2.0.0+c0cce515351cad5febdb437131a1649557eaff0b` and passed index, verify,
  stats, query, search, and CSV-export workflows; the desktop remained running
  through a 20-second WSLg smoke test without a fatal error or leaked process;
- the exact published APK (SHA-256
  `cfc8ee215dad5f78e88e7017830d189a251a7b8a6fe663ac9a509fc354625e05`)
  was installed on a Motorola edge 60 pro running Android 16/API 36 after the
  maintainer authorized removal of the old debug-signed build and its data.
  Android reported version name `2.0.0`, version code `3`, target SDK 36, and a
  non-debuggable package; pulling the installed base APK back from the device
  produced the same SHA-256 hash. Cold launches completed in about 1.3 seconds
  with no fatal exception or ANR; and
- restricted own-app capture ingested and rendered 59 real log lines, while a
  separately authorized `READ_LOGS` test explicitly reported full-device scope
  and ingested roughly 1,900 lines at about 190 lines per second. The elevated
  permission was revoked after the test and the stable app was left installed.

These results close the Linux artifact and physical Android gates for this
release. The four-hour ADB soak, accessibility review, macOS hardware testing,
and desktop signing/notarization remain separate manual work rather than claims
made by `v2.0.0`.

> **Historical Android note:** this first-release evidence predates the
> Play-oriented Wireless ADB transport. The `READ_LOGS` observations above remain
> valid evidence for v2.0.0 and must not be rewritten, but they do not validate
> current full-device capture. A candidate containing Wireless ADB requires the
> current gates in `docs/RELEASE-CHECKLIST.md`. The newer pairing, reconnect and
> full-device record is maintained separately in §§12–14 of
> `docs/ANDROID-LIVE-TEST-REPORT.md`.

---

## 1. What the first release must produce

A `v*` tag runs `.github/workflows/release.yml`, which is expected to publish
this asset set to a single GitHub release:

| Asset | Produced by | Required |
|---|---|---|
| `VisualCat-Desktop-win-x64-v<version>.zip` | `desktop` job | yes |
| `VisualCat-CLI-win-x64-v<version>.zip` | `desktop` job | yes |
| `VisualCat-Desktop-linux-x64-v<version>.tar.gz` | `desktop` job | yes |
| `VisualCat-CLI-linux-x64-v<version>.tar.gz` | `desktop` job | yes |
| `VisualCat-Desktop-osx-x64-v<version>.tar.gz` | `desktop` job | yes |
| `VisualCat-CLI-osx-x64-v<version>.tar.gz` | `desktop` job | yes |
| `VisualCat-Desktop-osx-arm64-v<version>.tar.gz` | `desktop` job | yes |
| `VisualCat-CLI-osx-arm64-v<version>.tar.gz` | `desktop` job | yes |
| `VisualCat.Cli.<version>.nupkg` (.NET global tool `vcat`) | `nuget` job | yes |
| `VisualCat-sbom-v<version>.cdx.json` (CycloneDX) | `sbom` job | yes |
| `SHA256SUMS` | `publish` job | yes |
| Build provenance attestations | `publish` job | yes |
| `VisualCat-Android-v<version>.apk` | `android` job | **opt-in** |
| `VisualCat.Cli` on nuget.org | `publish` job | **opt-in** |

The `publish` job hard-codes the expected asset names and fails if any is
missing, so the list above is enforced rather than aspirational.

Every desktop and CLI archive is self-contained (no .NET install required) and
carries `LICENSE`, `THIRD-PARTY-NOTICES.md`, and a generated `README.txt`.

**Expected total payload:** roughly 350 MB across the eight archives. Measured
locally: Windows desktop 72.1 MB zip (204.9 MB extracted, 242 entries), Windows
CLI 35.7 MB (77.7 MB), Linux desktop 42.8 MB tar.gz (101.9 MB, 240 entries),
Linux CLI 34.1 MB (79.5 MB, 209 entries). macOS archives were not built during
the audit and are estimated at similar sizes. This is well inside GitHub's 2 GB
per-asset limit.

"All platforms" therefore means **four desktop targets plus Android**. Android
is the only one that is off by default.

---

## 2. Verified current state

These checks were run against `26e9335` and **passed**:

| Check | Command | Result |
|---|---|---|
| Formatting | `dotnet format VisualCat.Desktop.slnx --verify-no-changes` | clean |
| Release build | `dotnet build VisualCat.Desktop.slnx -c Release` | 0 warnings, 0 errors (warnings are errors) |
| Tests | `dotnet test VisualCat.Desktop.slnx -c Release` | 155 passed, 0 failed (Domain 11, Core 52, Application 44, App 48) |
| CLI smoke + help contract | `tools/verify-cli-help.ps1` | matches `docs/CLI-HELP.txt` |
| Docs/version consistency (untagged) | `tools/verify-docs.ps1` | consistent |
| Vulnerable packages | `dotnet list package --vulnerable --include-transitive` | none |
| Packaging + archive verification | `tools/package.ps1 -Archive` (native `pwsh`) | archives verified |
| SBOM + license policy | `tools/generate-sbom.ps1` | 28 components, 1 without license metadata, policy passed |
| Secret scan incl. history | `tools/scan-secrets.ps1 -History` | 189 tracked files, 244 reachable blobs, 3 reviewed suppressions, no unclassified findings |
| Android Release build | `dotnet build src/VisualCat.Android -c Release` | succeeded |
| Dependency availability | nuget.org flat container | `Avalonia 12.1.0` published |
| Package ID availability | nuget.org flat container | `VisualCat.Cli` is **unclaimed** (HTTP 404) |

**The code is not the blocker.** Everything below is release mechanics,
repository configuration, and provisioning.

---

## 3. Blockers

Ordered by whether they stop the release outright.

### 3.1 Hard blockers — the release cannot succeed until these are fixed

#### B1. The changelog has no `## [2.0.0]` section

*Evidence (verified).* `./tools/verify-docs.ps1 -ReleaseVersion '2.0.0'` exits 1
with:

```text
FAIL: CHANGELOG.md has no '## [2.0.0]' section for the release being published.
```

*Impact.* The `preflight` job of `release.yml` runs exactly this command on a
tagged run. Pushing `v2.0.0` today fails preflight, and every packaging job
`needs: preflight`, so **nothing is built and no release is created**. The tag
still exists on the remote and must be deleted before retrying.

*Fix.* Promote the `[Unreleased]` content to a dated `## [2.0.0]` section,
leaving an empty `## [Unreleased]` heading in place (`verify-docs.ps1` requires
that heading to remain). See §4 for the ordering constraint — this fix cannot be
merged on its own without tripping a different check.

#### B2. `actions/attest-build-provenance` is pinned to a tag object, not a commit

*Evidence (verified).* `.github/workflows/release.yml` line 337 pins
`actions/attest-build-provenance@78e6cbd37d0ac1a40113c04f2037dacf1ea3f12e # v4`.

- `GET /repos/actions/attest-build-provenance/git/ref/tags/v4` returns
  `{"object": {"sha": "78e6cbd3…", "type": "tag"}}` — an **annotated tag
  object**, not a commit.
- `GET /repos/actions/attest-build-provenance/commits/78e6cbd3…` returns
  **HTTP 422**: there is no commit with that SHA.
- Peeling the tag gives the real commit:
  `0f67c3f4856b2e3261c31976d6725780e5e4c373`.

*Impact.* Actions resolves a 40-character `uses:` ref as a commit SHA. The
runner downloads every action referenced by a job during "Set up job",
regardless of step `if:` conditions, so this fails the whole `publish` job —
after all packaging work has already completed and been paid for.

*Fix.*

```yaml
- uses: actions/attest-build-provenance@0f67c3f4856b2e3261c31976d6725780e5e4c373 # v4
```

*Every other action pin in the repository was audited and is correct:*

| Action | Pinned SHA | Verdict |
|---|---|---|
| `actions/checkout` v7 | `3d3c42e5…` | correct (lightweight tag → commit) |
| `actions/setup-dotnet` v6 | `a98b5685…` | correct |
| `actions/upload-artifact` v7 | `043fb46d…` | correct |
| `actions/download-artifact` v8 | `3e5f45b2…` | correct |
| `softprops/action-gh-release` v3 | `3d0d9888…` | correct (annotated tag correctly peeled) |
| `codecov/codecov-action` v7 | `fb8b3582…` | correct (annotated tag correctly peeled) |
| `github/codeql-action` v4 | `e0647621…` | correct |
| `actions/attest-build-provenance` v4 | `78e6cbd3…` | **wrong — tag object** |

#### B3. The hardened release path exists only on `public-release-hardening`

*Evidence (verified).* `git rev-list --left-right --count origin/main...HEAD`
returns `0 1` — the branch is one commit ahead of `main`. `git diff --stat
origin/main..HEAD` shows that `main` does **not** contain
`tools/verify-docs.ps1`, `tools/scan-secrets.ps1`, `tools/generate-sbom.ps1`,
`tools/stage-release-notices.ps1`, `tools/verify-package-contents.ps1`, or
`tools/verify-public-release.ps1`, and that its `release.yml` is 204 lines
older.

*Impact.* A `workflow_dispatch` rehearsal launched from `main` runs the **old**
workflow and proves nothing about the path that will actually publish. (The
dispatch button itself does appear, because the older `release.yml` on `main`
already declares `workflow_dispatch`; a dispatch against a non-default ref uses
that ref's workflow file, which is a usable but easy-to-forget workaround.)

*Fix.* Merge `public-release-hardening` into `main` before rehearsing or
tagging. Do this **first** — it is the prerequisite for almost everything else.

#### B4. The repository is private, and build provenance requires a public repository

*Evidence (verified).* An unauthenticated request to
`https://github.com/benny-cz/VisualCat` returns **HTTP 404**, which is how
GitHub represents a private repository to anonymous clients.

*Impact.* Three separate consequences:

- **Attestations.** `actions/attest-build-provenance` is available for public
  repositories; private repositories need GitHub Enterprise Cloud. On a personal
  private repository the `publish` job's attestation step is expected to fail —
  again, after all packaging has completed. Confirm this in the rehearsal before
  assuming it.
- **Instructions that would not work.** `docs/RELEASE-NOTES.md` and the
  generated `README.txt` inside every archive both tell users to run
  `gh attestation verify <archive> --repo benny-cz/VisualCat`. That command
  cannot succeed against a private repository or against an unattested release.
- **Badges and downloads.** The CI badge, the release badge, and Codecov all
  read as broken or missing to signed-out visitors.

*Fix.* Make the repository public **before** tagging, having completed the
pre-visibility items in `docs/RELEASE-CHECKLIST.md` ("Before changing
visibility"). If the repository must stay private for the first release, then
delete the attestation step and remove the `gh attestation verify` instructions
from `docs/RELEASE-NOTES.md` and `tools/stage-release-notices.ps1` — do not ship
verification instructions that cannot work.

---

### 3.2 Provisioning blockers — required for "all platforms", and only a human can supply them

#### B5. Android is disabled and has no signing key

*Evidence.* `release.yml` gates the `android` job on
`if: vars.ANDROID_RELEASE_ENABLED == 'true'`, and the job fails closed unless
all four signing secrets are present. Neither the variable nor the secrets are
set (they cannot be, from the working tree).

*Impact.* Without them, the release ships desktop, CLI, NuGet, SBOM, and
checksums but **no APK**. The `publish` job explicitly tolerates a skipped
`android` job, so this degrades the release rather than failing it — which is
also why it is easy to ship the first release without noticing Android is
absent.

*Fix.* Generate a persistent release keystore, store it outside GitHub, and
configure:

```powershell
keytool -genkeypair -v `
  -keystore visualcat-release.keystore `
  -alias visualcat `
  -keyalg RSA -keysize 4096 -validity 10000
[Convert]::ToBase64String([IO.File]::ReadAllBytes('visualcat-release.keystore')) |
  Set-Clipboard
```

Repository → Settings → Secrets and variables → Actions:

- variable `ANDROID_RELEASE_ENABLED` = `true`
- secrets `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`,
  `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`

**Losing this key permanently prevents upgrading any installed APK.** Back it up
before the first tag, not after.

*Unproven.* The Android publish path (`dotnet publish … --no-restore
-p:AndroidPackageFormat=apk` with signing properties, then locating
`*-Signed.apk`) has never run in CI. A local Release *build* succeeds, which is
weaker evidence than a signed *publish*. Exercise it in a rehearsal with the
variable enabled.

#### B6. NuGet publication is unconfigured

*Evidence.* `publish` skips `dotnet nuget push` when `NUGET_API_KEY` is empty,
logging a step-summary note. The package ID `VisualCat.Cli` is currently
**unclaimed** on nuget.org (flat-container index returns 404).

*Impact.* The `.nupkg` is still built, verified by installing it from a local
feed, checksummed, and attached to the release — it simply is not on nuget.org,
so `dotnet tool install --global VisualCat.Cli` (advertised in `README.md`)
would not work.

*Fix.* Create a scoped nuget.org API key limited to pushing `VisualCat.Cli` and
store it as the `NUGET_API_KEY` secret. Either do this before the first tag or
remove the global-tool install instruction from `README.md` until it is true.
Note that an unclaimed ID is claimable by anyone; pushing early reserves it.

#### B7. Repository settings that cannot live in the working tree

`docs/RELEASE-CHECKLIST.md` §"First public release only" is the authority. The
release-relevant subset:

- Actions must be enabled with permission for a job to request `contents: write`
  (the default read-only setting still allows an explicit per-job escalation, so
  no change is normally needed — confirm it).
- Private vulnerability reporting must be enabled, or the links in
  `docs/SECURITY.md` and `.github/ISSUE_TEMPLATE/config.yml` 404.
- A `main` ruleset with an explicit administrator bypass. Without the bypass, a
  single-maintainer project can deadlock itself; with a required-check
  misconfiguration, the release commit cannot be pushed at all (see §4).

---

### 3.3 Defects that degrade the release without stopping it

#### D1. The release body contains a link that cannot resolve

`docs/RELEASE-NOTES.md` is passed to `softprops/action-gh-release` as
`body_path`. Near the end it links to `SUPPORT.md` by that bare relative path.
`tools/verify-docs.ps1` resolves it against `docs/` on disk and passes, but on
the rendered release page the target does not exist. Use an absolute URL in this
file: `https://github.com/benny-cz/VisualCat/blob/main/docs/SUPPORT.md`.

Any relative link added to `docs/RELEASE-NOTES.md` has this problem. It is the
one Markdown file in the repository whose links are rendered outside the
repository, and the automated link check cannot see that.

#### D2. The release body is static and never mentions the version

Every release gets the same `docs/RELEASE-NOTES.md` text plus GitHub's
auto-generated commit notes. Neither references `CHANGELOG.md`. Add a link to
the changelog section for the released version.

#### D3. A prerelease tag would publish as a full release

`softprops/action-gh-release` is invoked without a `prerelease` input. If the
first real end-to-end test is `v2.0.0-rc.1` (recommended — see §4), set
`prerelease: true` explicitly rather than relying on auto-detection, or the
release-candidate becomes "Latest release" on the repository landing page.

#### D4. macOS artifacts are bare executables, not `.app` bundles

*Evidence.* No `Info.plist`, no `.icns`, and no bundling step exist anywhere in
the repository; `src/VisualCat.Desktop/Assets` contains only `visualcat.ico` and
a PNG.

macOS users get an executable to run from a terminal after clearing quarantine.
It will not have a proper dock icon, application menu name, or Finder
double-click behaviour. This is a known, acceptable v1 limitation, but
`docs/SUPPORT.md` currently does not say so. State it explicitly.

#### D5. macOS tarballs may contain AppleDouble junk

The `desktop` job runs `tar -czf` on `macos-15`. Apple's `tar` writes `._name`
AppleDouble entries for files carrying extended attributes, and macOS adds
`com.apple.provenance` to binaries. `tools/verify-package-contents.ps1` would
not fail on this — it only asserts that the expected executable is present at
the archive root. Pre-empt it by exporting `COPYFILE_DISABLE=1` for the archive
step, and inspect a macOS tarball listing during the rehearsal.

#### D6. The SBOM covers the desktop solution only

`tools/generate-sbom.ps1` runs CycloneDX against `VisualCat.Desktop.slnx`, which
excludes `src/VisualCat.Android`. If the APK ships, its AndroidX dependencies
are absent from the published inventory. The self-contained .NET runtime shipped
inside every archive is likewise not enumerated. Both are defensible; both
should be stated rather than implied.

#### D7. The release preflight is weaker than CI

`preflight` builds and tests only on `ubuntu-latest`, while `ci.yml` tests on
Windows, Linux, and macOS. A tagged release therefore proves less than a pull
request does. Acceptable, given the tag is expected to point at a commit that
already passed full CI on `main` — but that expectation is not enforced
anywhere. Do not tag a commit that has not had a green CI run.

#### D8. Adding a RID requires editing two places

The `desktop` matrix and the `expected` asset list in `publish` are independent.
Adding or removing a platform in one without the other either silently drops an
artifact or fails the release at the last step.

---

### 3.4 Local tooling traps

These do not affect CI, but they will waste a maintainer's time.

#### T1. `tools/package.ps1` fails if Git for Windows' `tar` precedes the system `tar`

*Evidence (verified).* Running the documented command with `/usr/bin/tar` (GNU
tar, from Git for Windows) ahead of `C:\Windows\System32\tar.exe` on `PATH`:

```text
tar (child): Cannot connect to C: resolve failed
== Package : FAILED - Archiving failed for Desktop / linux-x64.
```

GNU tar reads the `C:\…` output path as a remote `host:path` specification. The
same command in a normal PowerShell session, where `tar` resolves to
`System32\tar.exe` (bsdtar), succeeds. Run packaging from a plain `pwsh`
session, or pass a relative `-OutputRoot`.

#### T2. Windows-built Unix archives are not executable

*Evidence (verified).* `tar -tvzf` on a `linux-x64` archive produced on Windows:

```text
-rw-rw-rw-  0 0  0  78256 vcat
```

The executable bit is absent. `tools/verify-package-contents.ps1` deliberately
asserts that bit only when the verifying machine is Unix, so local Windows
verification cannot catch it.

*Consequence for the plan.* `docs/RELEASE-CHECKLIST.md` presents
`pwsh ./tools/package.ps1 -Runtime win-x64,linux-x64,osx-x64,osx-arm64 -Archive`
as reproducing the release layout, and `tools/stage-release-notices.ps1` claims
"a maintainer packaging locally sees exactly what a user extracts". From
Windows, that is true for the Windows zip only. **Local packaging on Windows
cannot validate the Linux or macOS archives.** Only the workflow rehearsal can,
because it builds each Unix archive on a Unix runner.

#### T3. `gitleaks` is not installed locally

`tools/scan-secrets.ps1` reports:

```text
gitleaks is not installed; using the built-in pattern scan.
Install gitleaks for the authoritative pre-publication scan.
```

The built-in scan passed, but `docs/RELEASE-CHECKLIST.md` requires a maintained
scanner over all reachable refs before changing visibility. Install `gitleaks`
(or TruffleHog) and run it once. The three existing suppressions are a test
fixture password in `tests/VisualCat.Application.Tests/SessionPersistenceTests.cs`
and its historical copies; classify them, do not blanket-suppress.

#### T4. The `sbom` job assumes `~/.dotnet/tools` is on `PATH`

`tools/generate-sbom.ps1` installs the CycloneDX global tool and then invokes
`dotnet CycloneDX`, which requires the global tool directory on `PATH`. This
works locally and is expected to work on GitHub-hosted runners, but it has never
been exercised there. If the `sbom` job fails to find the tool, add
`$HOME/.dotnet/tools` to `GITHUB_PATH` after installation. Watch for it in the
rehearsal.

---

## 4. The changelog/tag ordering deadlock

This is subtle enough to derail a release day, so it gets its own section.

`tools/verify-docs.ps1` enforces two rules that pull in opposite directions:

```powershell
# Rule A — untagged runs (CI on every push and pull request)
if ($releasedVersions.Count -gt 0 -and $gitAvailable -and $tags.Count -eq 0) {
    Add-Problem "CHANGELOG.md documents released version(s) … but the repository has no tags."
}

# Rule B — tagged runs (release preflight)
if ($normalized -eq $base -and $releasedVersions -notcontains $base) {
    Add-Problem "CHANGELOG.md has no '## [$base]' section for the release being published."
}
```

- Rule B means **the tag cannot be pushed before the changelog section exists.**
- Rule A means **the changelog section cannot be merged before a tag exists** —
  a pull request adding `## [2.0.0]` fails CI's `repository` job, because
  `actions/checkout` with `fetch-depth: 0` fetches tags and finds none.

There is no ordering of two separate pushes that satisfies both. The escape is
to make them a **single push**:

```powershell
# On main, working tree clean, CI already green on this commit.
git switch main
git pull --ff-only

# 1. Edit CHANGELOG.md: promote [Unreleased] to "## [2.0.0] - 2026-07-22",
#    keep an empty "## [Unreleased]" heading, and add the link definitions:
#      [Unreleased]: https://github.com/benny-cz/VisualCat/compare/v2.0.0...HEAD
#      [2.0.0]: https://github.com/benny-cz/VisualCat/releases/tag/v2.0.0
git add CHANGELOG.md
git commit -m "Release 2.0.0"

# 2. Tag locally FIRST, so local verification can pass.
git tag -a v2.0.0 -m "VisualCat 2.0.0"

# 3. Now both checks can pass locally. Confirm before anything is public.
pwsh ./tools/verify-docs.ps1                       # Rule A: tag now exists
pwsh ./tools/verify-docs.ps1 -ReleaseVersion 2.0.0 # Rule B: section now exists

# 4. Push commit and tag together.
git push origin main --follow-tags
```

Note that step 1's changelog link definitions **also** require the tag to exist:
`verify-docs.ps1` fails on any `releases/tag/v…` or `compare/v….` reference to a
tag that is not in the local repository. Creating the tag before running any
verification (step 2 before step 3) is what makes the whole sequence work.

Two consequences:

- **A `main` ruleset that requires pull requests will block this.** Keep the
  administrator bypass described in `docs/RELEASE-CHECKLIST.md`, or perform the
  release commit through a pull request that is merged in the same minute the
  tag is pushed, accepting one red CI run in between.
- **A small race exists.** The `main` push and the tag push arrive together, but
  the CI run for the branch push may check out before the tag ref is visible. If
  CI's `repository` job goes red on Rule A immediately after a release push,
  re-run it before investigating anything else.

*Optional hardening.* Rule A could accept a released section whose version
equals `Directory.Build.props`'s `VersionPrefix` when no tag exists yet, which
would remove the deadlock entirely. That is a behaviour change to a gate and
should be a deliberate decision, not a release-day patch.

---

## 5. Execution plan

Each phase ends in a verifiable state. Do not start a phase before its
predecessor is green.

### Phase 0 — fix the working tree (branch `public-release-hardening`)

1. Repoint the attestation action to the real commit (B2):

   ```yaml
   - uses: actions/attest-build-provenance@0f67c3f4856b2e3261c31976d6725780e5e4c373 # v4
   ```

2. Replace the relative `SUPPORT.md` link in `docs/RELEASE-NOTES.md` with an
   absolute URL, and add a changelog link (D1, D2).
3. Optional but cheap: export `COPYFILE_DISABLE=1` around the macOS archive step
   (D5); record the macOS `.app`-bundle limitation in `docs/SUPPORT.md` (D4).
4. Re-run the full local preflight from a plain `pwsh` session (see T1):

   ```powershell
   pwsh ./tools/verify-public-release.ps1 -AllRuntimes -ScanHistory
   ```

   Expect every stage `ok`. Do not proceed on a failure.

**Exit criterion:** preflight green, working tree clean.

### Phase 1 — get the hardened path onto `main` (B3)

Merge `public-release-hardening` into `main` and confirm CI is green on `main`
across Windows, Linux, and macOS. Until this is done, no rehearsal means
anything.

**Exit criterion:** `main` contains the hardened `release.yml` and all seven
`tools/*.ps1` scripts, with a green CI run.

### Phase 2 — provision secrets and settings (B5, B6, B7)

Decide, and record the decision, for each:

| Decision | Ship in v2.0.0? | Requires |
|---|---|---|
| Android APK | | keystore + 4 secrets + `ANDROID_RELEASE_ENABLED` |
| nuget.org publication | | `NUGET_API_KEY` |
| Public repository | | pre-visibility checklist complete |
| Windows code signing | (deferred) | certificate + workflow changes |
| macOS notarization | (deferred) | Apple Developer account + workflow changes |

An unticked box is a legitimate choice, but it must be *stated* in the release
notes rather than discovered by a user.

**Exit criterion:** the intended secrets exist; the checklist's "Before changing
visibility" items are done.

### Phase 3 — rehearse (mandatory)

Run `release.yml` via **Actions → Release packages → Run workflow** against
`main`. Dispatch runs are versioned `2.0.0-preview.<run number>` and never
create a GitHub release or push to NuGet.

What a rehearsal **does** prove:

- every job's action references resolve (this is where B2 surfaces, because
  actions are downloaded during "Set up job" regardless of step conditions);
- all four desktop RIDs publish, archive, and pass `verify-package-contents.ps1`
  — including the Unix executable bit, which local Windows packaging cannot
  check (T2);
- the `.nupkg` installs from a local feed and runs;
- the SBOM generates and passes the license policy (T4 surfaces here);
- the `publish` job assembles every expected asset and produces `SHA256SUMS`.

What a rehearsal **does not** prove, because those steps are gated on
`is-tagged-release == 'true'`:

- that attestation signing succeeds (B4 — the private-repository question);
- that GitHub release creation succeeds;
- that `dotnet nuget push` succeeds.

Download the `release-assets` artifact. Extract and run the Windows desktop
build and at least one Unix build **on a clean machine**. Verify checksums,
archive names, embedded versions, notice files, and the documented launch steps.
Inspect a macOS tarball listing for `._` entries (D5).

**Exit criterion:** a rehearsal you were satisfied with, on `main`, with
artifacts inspected on real hardware.

### Phase 4 — close the coverage gap the rehearsal leaves

Because the three most consequential steps never run in a rehearsal, make the
first *tagged* run a release candidate:

```powershell
git tag -a v2.0.0-rc.1 -m "VisualCat 2.0.0-rc.1"
git push origin v2.0.0-rc.1
```

A prerelease tag skips Rule B of `verify-docs.ps1` entirely — the check applies
only when the version has no prerelease suffix — so no changelog change is
needed yet. This exercises attestation, release creation, and NuGet publication
for real, at a version that can be deleted without embarrassment.

Set `prerelease: true` on the release step first (D3), or expect the RC to be
labelled "Latest release".

If anything fails, delete the tag and the release and retry:

```powershell
git push origin :refs/tags/v2.0.0-rc.1
git tag -d v2.0.0-rc.1
# then delete the GitHub release in the web UI
```

**Exit criterion:** a real release exists, its attestation verifies, and the RC
`.nupkg` is on nuget.org (unlist it afterwards).

### Phase 5 — publish v2.0.0

1. Confirm `main` is green and the working tree is clean.
2. Execute the exact sequence in §4 (changelog + local tag + verify + single
   push). This is the step most likely to go wrong; do not improvise it.
3. Watch the run. `preflight` must pass before any packaging starts; if it
   fails, delete the remote tag before retrying.
4. After the release appears, verify it as a stranger would:

   ```shell
   sha256sum -c SHA256SUMS
   gh attestation verify VisualCat-Desktop-linux-x64-v2.0.0.tar.gz --repo benny-cz/VisualCat
   dotnet tool install --global VisualCat.Cli
   vcat --version
   ```

5. Restore the README release badge, per
   `docs/RELEASE-CHECKLIST.md` §"Restoring hidden badges" — `verify-docs.ps1`
   blocks that badge until a tag exists, so this must come after the tag, not
   before.
6. Update the README "Status: source preview" block, which currently states that
   no release exists.

**Exit criterion:** a signed-out browser can download, verify, and run VisualCat
on every platform the release claims to support.

---

## 6. Rollback

Releases are hard to un-publish. Know the exits before you need them.

| Artifact | Reversible? | How |
|---|---|---|
| GitHub release | yes | delete the release, then `git push origin :refs/tags/v2.0.0` |
| Attestations | no | they are permanent transparency-log entries; a deleted release does not remove them |
| nuget.org package | **no deletion** | only `dotnet nuget delete` → *unlist*. The version number is burned forever; ship `2.0.1`, never re-push `2.0.0` |
| Android APK | effectively no | anyone who installed it keeps it; only a higher `versionCode` upgrades it |

This asymmetry is the argument for Phase 4. A release candidate costs an hour;
a bad `2.0.0` on nuget.org is permanent.

---

## 7. Explicitly deferred

State these in the release notes rather than leaving users to discover them:

- Windows binaries are unsigned; SmartScreen will warn.
- macOS binaries are unsigned and un-notarized, ship as bare executables rather
  than `.app` bundles, and require clearing the quarantine attribute.
- Linux ships as a tarball only — no `.deb`, `.rpm`, AppImage, Flatpak, or
  `.desktop` entry. Avalonia/Skia's runtime dependencies (X11, fontconfig) are
  not documented in `docs/SUPPORT.md`; add them.
- The SBOM covers the desktop solution, not the Android package or the embedded
  .NET runtime (D6).
- Multi-hour ADB soak testing, physical Android validation, and accessibility
  review are manual gates in `docs/RELEASE-CHECKLIST.md` that no automation
  covers.

---

## 8. One-line summary

The software is ready; the release path is not. Four things are hard blockers —
**a missing `## [2.0.0]` changelog section, an action pinned to a tag object
instead of a commit, the hardened workflow not being on `main`, and the
repository still being private** — and everything else is either provisioning
(Android keystore, NuGet key) or polish. The single highest-risk moment is the
changelog/tag ordering in §4; the single highest-value action is the Phase 3
rehearsal followed by a Phase 4 release candidate.
