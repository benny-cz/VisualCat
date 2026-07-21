<#
.SYNOPSIS
    Publishes the self-contained desktop application and vcat CLI for one or
    more runtimes, using the same layout the release workflow ships.

.DESCRIPTION
    Every published directory receives LICENSE, THIRD-PARTY-NOTICES.md, and a
    README.txt through tools/stage-release-notices.ps1, so a maintainer
    packaging locally sees the same file layout a user extracts. Unix mode bits
    are authoritative only when the archive is created on Unix.

    With -Archive, the script also produces the release archives (zip for
    Windows, tar.gz elsewhere) and verifies each one with
    tools/verify-package-contents.ps1.

.EXAMPLE
    pwsh ./tools/package.ps1 -Runtime win-x64,linux-x64,osx-x64,osx-arm64 -Archive
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')]
    [string[]]$Runtime = @('win-x64'),

    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts'),

    # Version stamped into the binaries, notices, and archive names. Defaults to
    # the VersionPrefix in Directory.Build.props.
    [string]$Version,

    # Also create and verify the release archives.
    [switch]$Archive,

    # Publish only the desktop application.
    [switch]$DesktopOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$temporary = Join-Path $repository ".tmp/package/$PID"
New-Item -ItemType Directory -Path $temporary -Force | Out-Null
$env:TEMP = $temporary
$env:TMP = $temporary

# [Path]::GetFullPath resolves against the process directory, which is not
# necessarily PowerShell's current location.
$output = if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
    [System.IO.Path]::GetFullPath($OutputRoot)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $PWD.Path $OutputRoot))
}
New-Item -ItemType Directory -Path $output -Force | Out-Null

if (-not $Version) {
    $props = Get-Content -Raw -LiteralPath (Join-Path $repository 'Directory.Build.props')
    if ($props -notmatch '<VersionPrefix>([^<]+)</VersionPrefix>') {
        throw 'Could not read <VersionPrefix> from Directory.Build.props.'
    }
    $Version = $Matches[1].Trim()
}

$components = @(
    [pscustomobject]@{ Name = 'Desktop'; Project = 'src/VisualCat.Desktop/VisualCat.Desktop.csproj' }
    [pscustomobject]@{ Name = 'Cli'; Project = 'src/VisualCat.Cli/VisualCat.Cli.csproj' }
)
if ($DesktopOnly) {
    $components = $components | Where-Object { $_.Name -eq 'Desktop' }
}

$hostRid = if ($IsWindows) {
    'win-x64'
} elseif ($IsMacOS) {
    if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') { 'osx-arm64' } else { 'osx-x64' }
} else {
    'linux-x64'
}

$systemTar = if ($IsWindows) { Join-Path $env:SystemRoot 'System32\tar.exe' }
$tarCommand = if ($systemTar -and (Test-Path -LiteralPath $systemTar -PathType Leaf)) {
    $systemTar
} else {
    (Get-Command tar -ErrorAction Stop).Source
}

foreach ($rid in $Runtime) {
    foreach ($component in $components) {
        $project = Join-Path $repository $component.Project
        $destination = Join-Path $output "VisualCat-$($component.Name)-$rid"

        dotnet restore $project --runtime $rid
        if ($LASTEXITCODE -ne 0) {
            throw "Restore failed for $($component.Name) / $rid."
        }

        dotnet publish $project `
            --configuration Release `
            --runtime $rid `
            --self-contained true `
            --no-restore `
            --output $destination `
            -p:Version=$Version
        if ($LASTEXITCODE -ne 0) {
            throw "Publish failed for $($component.Name) / $rid."
        }

        & (Join-Path $PSScriptRoot 'stage-release-notices.ps1') `
            -Destination $destination `
            -Component $component.Name `
            -Version $Version `
            -Runtime $rid

        if (-not $Archive) {
            continue
        }

        $packages = Join-Path $output 'packages'
        New-Item -ItemType Directory -Path $packages -Force | Out-Null
        # Archive names must match the release workflow so local and published
        # artifacts are directly comparable.
        $label = if ($component.Name -eq 'Desktop') { 'Desktop' } else { 'CLI' }
        $baseName = "VisualCat-$label-$rid-v$Version"

        if ($rid -eq 'win-x64') {
            $archivePath = Join-Path $packages "$baseName.zip"
            Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
            Compress-Archive -Path (Join-Path $destination '*') -DestinationPath $archivePath
        } else {
            $archivePath = Join-Path $packages "$baseName.tar.gz"
            Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
            # Use a relative archive filename. GNU tar otherwise treats a
            # Windows drive-qualified -f path as a remote host:path target.
            Push-Location $packages
            try {
                & $tarCommand -C $destination -czf ([System.IO.Path]::GetFileName($archivePath)) .
                if ($LASTEXITCODE -ne 0) {
                    throw "Archiving failed for $($component.Name) / $rid."
                }
            } finally {
                Pop-Location
            }
        }

        & (Join-Path $PSScriptRoot 'verify-package-contents.ps1') `
            -Archive $archivePath `
            -Component $component.Name `
            -Version $Version `
            -Runtime $rid `
            -SkipExecution:($rid -ne $hostRid)
    }
}

Write-Host "Packaged VisualCat $Version for: $($Runtime -join ', ')"
