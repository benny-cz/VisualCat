<#
.SYNOPSIS
    Copies the license, third-party notices, and a component-specific README
    into a published output directory before it is archived.

.DESCRIPTION
    Release archives are made from raw publish directories, which contain only
    build output. An offline user who extracts an archive must still be able to
    find the license, the third-party notices, how to launch the program, how to
    verify the download, and where to report a problem.

    Both tools/package.ps1 and the release workflow call this script so local
    packages contain the same files users receive. Unix permissions are only
    authoritative when the archive is built on a Unix runner.
#>
[CmdletBinding()]
param(
    # Published output directory to stage notices into.
    [Parameter(Mandatory)]
    [string]$Destination,

    [Parameter(Mandatory)]
    [ValidateSet('Desktop', 'Cli')]
    [string]$Component,

    [Parameter(Mandatory)]
    [string]$Version,

    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')]
    [string]$Runtime
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

# [Path]::GetFullPath resolves against the process directory, which is not
# necessarily PowerShell's current location.
$destination = if ([System.IO.Path]::IsPathRooted($Destination)) {
    [System.IO.Path]::GetFullPath($Destination)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $PWD.Path $Destination))
}

if (-not (Test-Path -LiteralPath $destination -PathType Container)) {
    throw "Publish directory '$destination' does not exist. Publish before staging notices."
}

Copy-Item -LiteralPath (Join-Path $repository 'LICENSE') -Destination (Join-Path $destination 'LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $repository 'docs/THIRD-PARTY-NOTICES.md') -Destination (Join-Path $destination 'THIRD-PARTY-NOTICES.md') -Force

$targetIsWindows = $Runtime -eq 'win-x64'

# The archive's single top-level directory, and the command that produces it. The README used to
# open with "./VisualCat" and say nothing about extracting, so a user following the terminal path
# it implies dropped 240 files into ~/Downloads (finding F-02). Naming the directory is also what
# makes the extraction reversible with one `rm -rf`.
$archiveRoot = "VisualCat-$(if ($Component -eq 'Desktop') { 'Desktop' } else { 'CLI' })-$Runtime-v$Version"
$extract = if ($targetIsWindows) {
    "Expand-Archive $archiveRoot.zip -DestinationPath ."
} else {
    "tar -xzf $archiveRoot.tar.gz"
}

if ($Component -eq 'Desktop') {
    $executable = if ($targetIsWindows) { 'VisualCat.exe' } else { 'VisualCat' }
    $launch = if ($targetIsWindows) { ".\$executable" } else { "./$executable" }
    $firstRun = @"
Extract and launch
------------------

  $extract
  cd $archiveRoot
  $launch

Open a log from the start page, or pass one directly:

  $launch --log path/to/logcat.txt
"@
} else {
    $executable = if ($targetIsWindows) { 'vcat.exe' } else { 'vcat' }
    $launch = if ($targetIsWindows) { ".\$executable" } else { "./$executable" }
    $firstRun = @"
Extract and launch
------------------

  $extract
  cd $archiveRoot
  $launch --version
  $launch help

Index a log and inspect it:

  $launch index path/to/logcat.txt --output session.vcat
  $launch stats session.vcat
"@
}

$platformNotes = if ($targetIsWindows) {
    @'
Windows notes
-------------

This build is not code-signed. SmartScreen may warn on first launch; after
verifying the checksum above, choose "More info" then "Run anyway".
'@
} elseif ($Runtime -like 'osx-*') {
    @"
macOS notes
-----------

This build is not signed or notarized. After verifying the checksum above,
clear the downloaded-file quarantine. This archive contains a terminal-launched
executable, not a Finder .app bundle, so there is no Dock icon, no Launch
Services registration and no file association:

  xattr -dr com.apple.quarantine .
  chmod +x $executable

The desktop head needs a display that is awake. For an unattended or scheduled
capture on a Mac whose screen sleeps or locks, use the vcat command line, which
needs none; it is in the VisualCat-CLI archive beside this one.

A saved session is a directory, not a single file. Copy or zip the whole .vcat
directory to move one, and point Open session at the directory rather than at a
file inside it. For a single-file hand-off, use Save portable and then
Export to a portable zip.
"@
} else {
    @"
Linux notes
-----------

This release is a tarball, not a distribution package. A graphical X11/XWayland
session, fonts, and the platform equivalents of libX11, libICE, libSM, and
fontconfig are required. See the support matrix below for package names.

Restore the executable bit if your extraction tool dropped it:

  chmod +x $executable
"@
}

# macOS ships no `sha256sum`: the BSD userland provides `shasum`, and the Darwin
# re-implementation of `sha256sum` only appeared in macOS 26. On macOS 15 and earlier the
# instruction whose whole purpose is to let a user verify a download before executing it
# answered `zsh: command not found` (finding F-01). `shasum -a 256` is present on every
# supported macOS and on every mainstream Linux, so both Unix artifacts use it and there is
# one fewer branch to keep correct.
#
# --ignore-missing, because a release page carries a dozen assets and a user downloads one:
# without it both tools print `FAILED open or read` for the eleven that are not there, which
# reads as a failed verification to anyone who has not seen it before.
$verify = if ($targetIsWindows) {
    '  (Get-FileHash -Algorithm SHA256 <archive>).Hash'
} else {
    '  shasum -a 256 --ignore-missing -c SHA256SUMS      (run it beside the archives)'
}

# The tag, not the branch. Every link in a shipped README pointed at `main`, so a 2.0.13 user
# who followed the link their own archive gave them read documentation for code they do not
# have — and the better the live testing gets, the wider that gap grows, because each run adds
# features to `main` that the released binary refuses (finding F-19).
$docsRef = "v$Version"

$title = "VisualCat $(if ($Component -eq 'Desktop') { 'Desktop' } else { 'CLI' }) $Version ($Runtime)"
$readme = @"
$title
$('=' * $title.Length)

VisualCat turns huge Android logcat files and live adb streams into an
interactive severity-by-time heat map. Processing is local: no telemetry, and
no log content leaves the machine.

This archive is self-contained. No separate .NET installation is required.

$firstRun

Verify this download
--------------------

Compare the archive against SHA256SUMS on the release page:

$verify

Releases also carry GitHub build provenance attestations, which tie these bytes
to the source commit and workflow that produced them:

  gh attestation verify <archive> --repo benny-cz/VisualCat

That command prints nothing at all when it succeeds and stdout is not a terminal,
which is indistinguishable from doing nothing, so ask it for the answer instead:

  gh attestation verify <archive> --repo benny-cz/VisualCat --format json | jq '.[0].verificationResult.signature.certificate
     | {sourceRepositoryURI, buildSignerURI, sourceRepositoryDigest}'

sourceRepositoryDigest is the commit this build came from; it matches the version
VisualCat shows in Session info and that "vcat --version" prints.

$platformNotes

Documentation and support
-------------------------

  Project         https://github.com/benny-cz/VisualCat
  Release notes   https://github.com/benny-cz/VisualCat/blob/$docsRef/docs/RELEASE-NOTES.md
  CLI reference   https://github.com/benny-cz/VisualCat/blob/$docsRef/docs/CLI.md
  Support matrix  https://github.com/benny-cz/VisualCat/blob/$docsRef/docs/SUPPORT.md
  Report a bug    https://github.com/benny-cz/VisualCat/issues

Report a security vulnerability privately, never as a public issue:

  https://github.com/benny-cz/VisualCat/security/advisories/new

License
-------

VisualCat is available under the MIT License; see LICENSE. Bundled third-party
components and their licenses are listed in THIRD-PARTY-NOTICES.md.
"@

$readmePath = Join-Path $destination 'README.txt'
Set-Content -LiteralPath $readmePath -Value ($readme -replace "`r`n", "`n") -Encoding utf8NoBOM -NoNewline

foreach ($required in @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'README.txt')) {
    $path = Join-Path $destination $required
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required notice file '$required' is missing from '$destination'."
    }
    if ((Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Required notice file '$required' in '$destination' is empty."
    }
}

Write-Host "Staged LICENSE, THIRD-PARTY-NOTICES.md, and README.txt into $destination"
