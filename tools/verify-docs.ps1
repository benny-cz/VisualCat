<#
.SYNOPSIS
    Checks that the repository's documentation and version metadata stay
    internally consistent.

.DESCRIPTION
    Routine renames and version bumps can silently leave public documentation
    misleading. This script verifies, without network access:

      * every relative Markdown link and image resolves on disk;
      * required repository files exist;
      * the changelog has an [Unreleased] section and its released versions and
        link definitions reference tags that exist, except for the one declared
        VersionPrefix that may be staged immediately before its tag;
      * README, changelog, and Directory.Build.props agree about the release
        state;
      * Google Play listing fields and release notes fit their form limits; and
      * the README does not advertise badges that cannot report real data yet.

    External URLs are deliberately not fetched: an intermittent third-party
    outage must not fail the build.

.PARAMETER ReleaseVersion
    When packaging a tagged release, the version being released. The changelog
    must then contain a matching released section rather than only [Unreleased].
#>
[CmdletBinding()]
param(
    [string]$ReleaseVersion
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$problems = [System.Collections.Generic.List[string]]::new()

function Add-Problem([string]$message) {
    $problems.Add($message)
}

# --- required files ------------------------------------------------------

$requiredFiles = @(
    'README.md'
    'LICENSE'
    'CHANGELOG.md'
    'CONTRIBUTING.md'
    'CODE_OF_CONDUCT.md'
    'global.json'
    'Directory.Build.props'
    'docs/SECURITY.md'
    'docs/PRIVACY.md'
    'docs/SUPPORT.md'
    'docs/THIRD-PARTY-NOTICES.md'
    'docs/RELEASE-NOTES.md'
    'docs/RELEASE-CHECKLIST.md'
    'docs/PLAY-LISTING.md'
    'tools/render-release-notes.ps1'
    '.github/release-targets.json'
    '.github/CODEOWNERS'
    '.github/dependabot.yml'
)
foreach ($file in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $repository $file) -PathType Leaf)) {
        Add-Problem "Required file '$file' is missing."
    }
}

# --- relative Markdown links and images ----------------------------------

$gitAvailable = $null -ne (Get-Command git -ErrorAction SilentlyContinue)

# Only tracked Markdown is public documentation. Local package caches and build
# output contain third-party readmes whose links are none of our business.
if ($gitAvailable) {
    $tracked = @(git -C $repository ls-files '*.md' '*.MD' 2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw 'git ls-files failed; run this script inside the repository.'
    }
    $markdown = @($tracked | ForEach-Object { Get-Item -LiteralPath (Join-Path $repository $_) })
} else {
    $excluded = @('bin', 'obj', 'node_modules', 'artifacts', 'packages', 'coverage-report', '.packages', '.tmp', '.nuget-scratch')
    $markdown = @(Get-ChildItem -LiteralPath $repository -Recurse -Filter '*.md' -File |
        Where-Object {
            $relative = $_.FullName.Substring($repository.Length).TrimStart('\', '/') -replace '\\', '/'
            $segments = $relative -split '/'
            -not ($segments | Where-Object { $excluded -contains $_ }) -and -not $relative.StartsWith('.git/')
        })
}

# Inline links and images: [text](target) and ![alt](target).
$linkPattern = [regex]'!?\[(?:[^\]\\]|\\.)*\]\(\s*(?<target>[^)\s]+)(?:\s+"[^"]*")?\s*\)'
# Reference definitions: [label]: target
$definitionPattern = [regex]'(?m)^\[(?<label>[^\]]+)\]:\s*(?<target>\S+)'
$linkCount = 0

foreach ($file in $markdown) {
    $text = Get-Content -Raw -LiteralPath $file.FullName
    $relativeFile = $file.FullName.Substring($repository.Length).TrimStart('\', '/') -replace '\\', '/'
    # Fenced code blocks contain shell samples whose parentheses are not links.
    $text = [regex]::Replace($text, '(?ms)^```.*?^```', '')

    $targets = @()
    $targets += $linkPattern.Matches($text) | ForEach-Object { $_.Groups['target'].Value }
    $targets += $definitionPattern.Matches($text) | ForEach-Object { $_.Groups['target'].Value }

    foreach ($target in $targets) {
        if ($target -match '^(https?|mailto|tel):' -or $target.StartsWith('#')) {
            continue
        }

        $linkCount++
        $path = ($target -split '#', 2)[0]
        if (-not $path) {
            continue
        }

        $path = [uri]::UnescapeDataString($path)
        $resolved = if ($path.StartsWith('/')) {
            Join-Path $repository $path.TrimStart('/')
        } else {
            Join-Path $file.DirectoryName $path
        }

        if (-not (Test-Path -LiteralPath $resolved)) {
            Add-Problem "${relativeFile}: link target '$target' does not exist."
        }
    }
}

# --- version and release-state consistency -------------------------------

$propsPath = Join-Path $repository 'Directory.Build.props'
$props = Get-Content -Raw -LiteralPath $propsPath
if ($props -notmatch '<VersionPrefix>\s*(?<version>[^<]+?)\s*</VersionPrefix>') {
    Add-Problem 'Directory.Build.props does not declare <VersionPrefix>.'
    $declaredVersion = $null
} else {
    $declaredVersion = $Matches['version']
    if ($declaredVersion -notmatch '^\d+\.\d+\.\d+$') {
        Add-Problem "Directory.Build.props <VersionPrefix> '$declaredVersion' is not a three-part version."
    }
}

$changelogPath = Join-Path $repository 'CHANGELOG.md'
$changelog = Get-Content -Raw -LiteralPath $changelogPath
if ($changelog -notmatch '(?m)^##\s*\[Unreleased\]') {
    Add-Problem 'CHANGELOG.md has no "## [Unreleased]" section.'
}

$releasedVersions = @([regex]::Matches($changelog, '(?m)^##\s*\[(?<version>\d+\.\d+\.\d+)\]') |
    ForEach-Object { $_.Groups['version'].Value })

# Tags referenced by changelog link definitions must exist. The sole exception
# is the current VersionPrefix when its release section has been staged for CI
# immediately before the annotated tag is created. This removes the impossible
# ordering where CI demanded the tag before allowing the release commit, while
# the release preflight demanded the commit before allowing the tag.
$tags = @()
if ($gitAvailable) {
    $tags = @(git -C $repository tag --list 2>$null)
}

$referencedTags = @([regex]::Matches($changelog, 'releases/tag/(?<tag>v[\w.+-]+)') |
    ForEach-Object { $_.Groups['tag'].Value })
$referencedTags += @([regex]::Matches($changelog, 'compare/(?<tag>v[\w.+-]+)\.\.\.') |
    ForEach-Object { $_.Groups['tag'].Value })

$pendingTag = $null
if ($declaredVersion -and $releasedVersions -contains $declaredVersion) {
    $candidate = "v$declaredVersion"
    if (-not $gitAvailable -or $tags -notcontains $candidate) {
        $pendingTag = $candidate
    }
}

foreach ($tag in ($referencedTags | Sort-Object -Unique)) {
    if ($gitAvailable -and $tags -notcontains $tag -and $tag -ne $pendingTag) {
        Add-Problem "CHANGELOG.md references tag '$tag', which does not exist in this repository."
    }
}

if ($gitAvailable) {
    foreach ($releasedVersion in $releasedVersions) {
        $releaseTag = "v$releasedVersion"
        if ($tags -notcontains $releaseTag -and $releaseTag -ne $pendingTag) {
            Add-Problem "CHANGELOG.md documents released version '$releasedVersion', but tag '$releaseTag' does not exist."
        }
    }
}

$readmePath = Join-Path $repository 'README.md'
$readme = Get-Content -Raw -LiteralPath $readmePath

if ($ReleaseVersion) {
    $normalized = $ReleaseVersion -replace '^v', ''
    $base = ($normalized -split '-', 2)[0]
    if ($declaredVersion -and $base -ne $declaredVersion) {
        Add-Problem "Release version '$normalized' does not match Directory.Build.props <VersionPrefix> '$declaredVersion'."
    }
    if ($normalized -eq $base -and $releasedVersions -notcontains $base) {
        Add-Problem "CHANGELOG.md has no '## [$base]' section for the release being published."
    }
} else {
    # A single pending section matching VersionPrefix may pass CI before its tag
    # is created. All other released sections were checked against tags above.
    $untaggedReleasedVersions = @($releasedVersions | Where-Object {
            -not $gitAvailable -or $tags -notcontains "v$_"
        })
    if ($untaggedReleasedVersions.Count -gt 1 -or
        ($untaggedReleasedVersions.Count -eq 1 -and $untaggedReleasedVersions[0] -ne $declaredVersion)) {
        Add-Problem ("CHANGELOG.md documents untagged release version(s) $($untaggedReleasedVersions -join ', '). " +
            "Only the declared VersionPrefix '$declaredVersion' may be staged while its release tag is pending.")
    }
}

# Badges must not be restored before they can report real data. The release
# checklist owns re-enabling them.
if ($gitAvailable -and $tags.Count -eq 0 -and $readme -match 'img\.shields\.io/github/v/release') {
    Add-Problem 'README.md shows a release badge, but the repository has no tags for it to report.'
}
if ($readme -match 'img\.shields\.io/github/license') {
    Add-Problem 'README.md uses the dynamic Shields license badge; use the static MIT badge so it does not depend on the GitHub API.'
}

# --- Google Play listing budgets -----------------------------------------

# Play truncates over-long listing fields at submission rather than rejecting
# them, so an overflow is only discovered by noticing that the published page
# ends mid-sentence. The budgets are checked here instead.
$playPath = Join-Path $repository 'docs/PLAY-LISTING.md'
if (Test-Path -LiteralPath $playPath -PathType Leaf) {
    $playLines = @(Get-Content -LiteralPath $playPath)
    $budgets = @(
        @{ Heading = 'App name'; Limit = 30 }
        @{ Heading = 'Short description'; Limit = 80 }
        @{ Heading = 'Full description'; Limit = 4000 }
    )

    foreach ($budget in $budgets) {
        $headingPattern = "^###\s+$([regex]::Escape($budget.Heading))\b"
        $headingIndex = -1
        for ($index = 0; $index -lt $playLines.Count; $index++) {
            if ($playLines[$index] -match $headingPattern) { $headingIndex = $index; break }
        }

        if ($headingIndex -lt 0) {
            Add-Problem "docs/PLAY-LISTING.md has no '### $($budget.Heading)' section."
            continue
        }

        if ($playLines[$headingIndex] -notmatch "$($budget.Limit)\s+characters") {
            Add-Problem ("docs/PLAY-LISTING.md heading '$($playLines[$headingIndex])' does not state Google Play's " +
                "$($budget.Limit)-character limit.")
        }

        $openIndex = -1
        for ($index = $headingIndex + 1; $index -lt $playLines.Count; $index++) {
            if ($playLines[$index] -match '^###?\s') { break }
            if ($playLines[$index] -match '^```') { $openIndex = $index; break }
        }

        if ($openIndex -lt 0) {
            Add-Problem "docs/PLAY-LISTING.md section '$($budget.Heading)' contains no fenced value block."
            continue
        }

        $closeIndex = -1
        for ($index = $openIndex + 1; $index -lt $playLines.Count; $index++) {
            if ($playLines[$index] -match '^```') { $closeIndex = $index; break }
        }

        if ($closeIndex -lt 0) {
            Add-Problem "docs/PLAY-LISTING.md section '$($budget.Heading)' has an unterminated value block."
            continue
        }

        $value = ($playLines[($openIndex + 1)..($closeIndex - 1)] -join "`n")
        if ($value.Length -gt $budget.Limit) {
            Add-Problem ("docs/PLAY-LISTING.md '$($budget.Heading)' is $($value.Length) characters; " +
                "Google Play allows $($budget.Limit).")
        }
        elseif ($value.Length -eq 0) {
            Add-Problem "docs/PLAY-LISTING.md '$($budget.Heading)' is empty."
        }
        else {
            Write-Host ("Play listing '$($budget.Heading)': $($value.Length)/$($budget.Limit) characters.")
        }
    }

    # Play's per-release "What's new" field accepts at most 500 characters.
    # Check every preserved version so a copy/paste-ready note cannot silently
    # overflow even when an older note is reused for a staged rollout.
    $releaseNoteLimit = 500
    for ($headingIndex = 0; $headingIndex -lt $playLines.Count; $headingIndex++) {
        if ($playLines[$headingIndex] -notmatch '^Release notes for .+:$') {
            continue
        }

        $heading = $playLines[$headingIndex]
        $openIndex = -1
        for ($index = $headingIndex + 1; $index -lt $playLines.Count; $index++) {
            if ($playLines[$index] -match '^Release notes for .+:$') { break }
            if ($playLines[$index] -match '^```text\s*$') { $openIndex = $index; break }
        }

        if ($openIndex -lt 0) {
            Add-Problem "docs/PLAY-LISTING.md '$heading' contains no text fence."
            continue
        }

        $closeIndex = -1
        for ($index = $openIndex + 1; $index -lt $playLines.Count; $index++) {
            if ($playLines[$index] -match '^```\s*$') { $closeIndex = $index; break }
        }

        if ($closeIndex -lt 0) {
            Add-Problem "docs/PLAY-LISTING.md '$heading' has an unterminated value block."
            continue
        }

        $value = ($playLines[($openIndex + 1)..($closeIndex - 1)] -join "`n")
        if ($value.Length -gt $releaseNoteLimit) {
            Add-Problem ("docs/PLAY-LISTING.md '$heading' is $($value.Length) characters; " +
                "Google Play allows $releaseNoteLimit.")
        }
        elseif ($value.Length -eq 0) {
            Add-Problem "docs/PLAY-LISTING.md '$heading' is empty."
        }
        else {
            Write-Host ("Play release notes '$heading': $($value.Length)/$releaseNoteLimit characters.")
        }
    }
}

# --- report --------------------------------------------------------------

if ($problems.Count -gt 0) {
    foreach ($problem in $problems) {
        Write-Host "FAIL: $problem"
    }
    Write-Error "Documentation and metadata checks found $($problems.Count) problem(s)."
    exit 1
}

Write-Host "Checked $linkCount relative links across $($markdown.Count) Markdown files, required files, and version metadata. All consistent."
