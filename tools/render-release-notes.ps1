<#
.SYNOPSIS
    Renders the versioned GitHub release body from docs/RELEASE-NOTES.md.

.DESCRIPTION
    The checked-in release notes are a reusable template. This script replaces
    its VERSION placeholder, rejects malformed versions and unresolved template
    tokens, and writes the exact Markdown passed to the GitHub release action.

.EXAMPLE
    pwsh ./tools/render-release-notes.ps1 -Version 2.0.9 -Destination release-notes.md
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$normalized = $Version -replace '^v', ''
$identifier = '(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)'
$versionPattern = "^(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:-$identifier(?:\.$identifier)*)?$"
if ($normalized -notmatch $versionPattern) {
    throw "Version '$Version' is not a supported three-part release version."
}

$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$templatePath = Join-Path $repository 'docs/RELEASE-NOTES.md'
$template = Get-Content -Raw -LiteralPath $templatePath

$rendered = $template.Replace('{{VERSION}}', $normalized)
if ($rendered -match '{{[^}]+}}') {
    throw "Release notes contain an unresolved template token: $($Matches[0])"
}

$destinationPath = if ([System.IO.Path]::IsPathRooted($Destination)) {
    [System.IO.Path]::GetFullPath($Destination)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $PWD.Path $Destination))
}
$parent = Split-Path -Parent $destinationPath
if ($parent) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
}

Set-Content -LiteralPath $destinationPath -Value ($rendered -replace "`r`n", "`n") -Encoding utf8NoBOM -NoNewline
Write-Host "Rendered release notes for VisualCat $normalized at $destinationPath"
