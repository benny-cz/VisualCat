<#
.SYNOPSIS
    Generates a CycloneDX software bill of materials for the shipped
    components and reviews the licenses it reports.

.DESCRIPTION
    docs/THIRD-PARTY-NOTICES.md is a curated human summary. A self-contained
    archive ships a much larger transitive set than a person can maintain by
    hand, so the inventory is generated from the resolved packages instead.

    Strong copyleft licenses are incompatible with shipping a self-contained
    MIT binary and fail the run. Components whose upstream metadata declares no
    license are reported for review but do not fail, because absent metadata is
    common and is not by itself a licensing problem.
#>
[CmdletBinding()]
param(
    [string]$Version,

    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts\sbom'),

    # Skip generation and review an SBOM that already exists.
    [string]$ExistingSbom,

    # Pinned so a tool update cannot silently change release output.
    [string]$ToolVersion = '6.2.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

# [Path]::GetFullPath resolves against the process directory, which is not
# necessarily PowerShell's current location.
function Resolve-FullPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) {
        return [System.IO.Path]::GetFullPath($path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $PWD.Path $path))
}

if (-not $Version) {
    $props = Get-Content -Raw -LiteralPath (Join-Path $repository 'Directory.Build.props')
    if ($props -notmatch '<VersionPrefix>([^<]+)</VersionPrefix>') {
        throw 'Could not read <VersionPrefix> from Directory.Build.props.'
    }
    $Version = $Matches[1].Trim()
}

if ($ExistingSbom) {
    $sbomPath = Resolve-FullPath $ExistingSbom
} else {
    $output = Resolve-FullPath $OutputRoot
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $fileName = "VisualCat-sbom-v$Version.cdx.json"
    $sbomPath = Join-Path $output $fileName

    if (-not (Get-Command dotnet-CycloneDX -ErrorAction SilentlyContinue)) {
        dotnet tool install --global CycloneDX --version $ToolVersion
        if ($LASTEXITCODE -ne 0) {
            throw "Installing the CycloneDX tool $ToolVersion failed."
        }
    }

    dotnet CycloneDX (Join-Path $repository 'VisualCat.Desktop.slnx') `
        --output $output `
        --filename $fileName `
        --output-format Json `
        --set-name VisualCat `
        --set-version $Version `
        --exclude-test-projects `
        --exclude-dev
    if ($LASTEXITCODE -ne 0) {
        throw 'CycloneDX SBOM generation failed.'
    }
}

if (-not (Test-Path -LiteralPath $sbomPath -PathType Leaf)) {
    throw "The SBOM was not generated at '$sbomPath'."
}

$sbom = Get-Content -Raw -LiteralPath $sbomPath | ConvertFrom-Json
$components = @($sbom.components)
if ($components.Count -eq 0) {
    throw 'The SBOM contains no components.'
}

function Get-LicenseId($component) {
    if ($component.PSObject.Properties.Name -notcontains 'licenses' -or -not $component.licenses) {
        return 'unknown'
    }

    $ids = foreach ($entry in $component.licenses) {
        if ($entry.PSObject.Properties.Name -contains 'expression' -and $entry.expression) {
            $entry.expression
        } elseif ($entry.PSObject.Properties.Name -contains 'license' -and $entry.license) {
            if ($entry.license.PSObject.Properties.Name -contains 'id' -and $entry.license.id) {
                $entry.license.id
            } elseif ($entry.license.PSObject.Properties.Name -contains 'name' -and $entry.license.name) {
                $entry.license.name
            }
        }
    }

    if (-not $ids) { return 'unknown' }
    return (($ids | Sort-Object -Unique) -join ', ')
}

$rows = foreach ($component in $components) {
    [pscustomobject]@{
        Name = $component.name
        Version = $component.version
        License = (Get-LicenseId $component)
    }
}
$rows = @($rows | Sort-Object Name, Version)

$denied = @($rows | Where-Object {
        $_.License -match '(?i)(^|[^A-Za-z])(AGPL|SSPL|CC-BY-NC|GPL-2\.0|GPL-3\.0)' -and
        $_.License -notmatch '(?i)(LGPL|WITH\s|-exception|Classpath)'
    })
$unknown = @($rows | Where-Object { $_.License -eq 'unknown' })

Write-Host "SBOM: $sbomPath"
Write-Host "Components: $($rows.Count); without declared license metadata: $($unknown.Count)"
foreach ($row in $rows) {
    Write-Host ('  {0,-45} {1,-14} {2}' -f $row.Name, $row.Version, $row.License)
}

if ($env:GITHUB_STEP_SUMMARY) {
    $summary = [System.Collections.Generic.List[string]]::new()
    $summary.Add('### Software bill of materials')
    $summary.Add("$($rows.Count) shipped components; $($unknown.Count) without declared license metadata.")
    $summary.Add('')
    $summary.Add('| Component | Version | License |')
    $summary.Add('|---|---|---|')
    foreach ($row in $rows) {
        $summary.Add("| $($row.Name) | $($row.Version) | $($row.License) |")
    }
    $summary | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8
}

if ($unknown.Count -gt 0) {
    Write-Host ''
    Write-Host "Review these components manually; their upstream metadata declares no license:"
    foreach ($row in $unknown) {
        Write-Host "  - $($row.Name) $($row.Version)"
    }
}

if ($denied.Count -gt 0) {
    Write-Host ''
    foreach ($row in $denied) {
        Write-Host "DENIED: $($row.Name) $($row.Version) — $($row.License)"
    }
    Write-Error 'The SBOM contains components under licenses incompatible with an MIT-licensed self-contained distribution.'
    exit 1
}

Write-Host 'License review passed.'
