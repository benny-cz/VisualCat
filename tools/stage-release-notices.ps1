<#
.SYNOPSIS
    Copies the license, third-party notices, and a component-specific README
    into a published output directory before it is archived.

.DESCRIPTION
    Release archives are made from raw publish directories, which contain only
    build output. An offline user who extracts an archive must still be able to
    find the license, the third-party notices, how to launch the program, how to
    verify the download, and where to report a problem.

    Both tools/package.ps1 and the release workflow call this script so a
    maintainer packaging locally sees exactly the layout users receive.
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
if ($Component -eq 'Desktop') {
    $executable = if ($targetIsWindows) { 'VisualCat.exe' } else { 'VisualCat' }
    $launch = if ($targetIsWindows) { ".\$executable" } else { "./$executable" }
    $firstRun = @"
Launch
------

  $launch

Open a log from the start page, or pass one directly:

  $launch --log path/to/logcat.txt
"@
} else {
    $executable = if ($targetIsWindows) { 'vcat.exe' } else { 'vcat' }
    $launch = if ($targetIsWindows) { ".\$executable" } else { "./$executable" }
    $firstRun = @"
Launch
------

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
clear the downloaded-file quarantine:

  xattr -dr com.apple.quarantine .
  chmod +x $executable
"@
} else {
    @"
Linux notes
-----------

Restore the executable bit if your extraction tool dropped it:

  chmod +x $executable
"@
}

$verify = if ($targetIsWindows) {
    '  (Get-FileHash -Algorithm SHA256 <archive>).Hash'
} else {
    '  sha256sum -c SHA256SUMS'
}

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

$platformNotes

Documentation and support
-------------------------

  Project         https://github.com/benny-cz/VisualCat
  Release notes   https://github.com/benny-cz/VisualCat/blob/main/docs/RELEASE-NOTES.md
  CLI reference   https://github.com/benny-cz/VisualCat/blob/main/docs/CLI.md
  Support matrix  https://github.com/benny-cz/VisualCat/blob/main/docs/SUPPORT.md
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
