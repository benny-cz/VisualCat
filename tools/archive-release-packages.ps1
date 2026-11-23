<#
.SYNOPSIS
    Archives one runtime's published desktop and CLI folders into the two release
    archives, each holding exactly one top-level directory named after itself.

.DESCRIPTION
    The release workflow archives every runtime through this script: Linux and
    macOS in the desktop job, and Windows in the signing job once its payload is
    signed (tools/signing/Invoke-WindowsSigningRun.ps1 -Phase Package). One
    implementation keeps the archive layout identical whichever job makes it.

    PublishRoot must hold the published `desktop` and `cli` folders, with their
    notices already staged. Each folder is renamed to the archive's root name,
    archived, and renamed back, so the archive's single top-level entry is a
    directory named after the archive. Packing a publish directory's contents
    directly gave every member the name "./<file>", so the extraction the README
    implies scattered 240 generically-named .NET assemblies into whatever
    directory the user happened to be in (finding F-02).

    tools/verify-package-contents.ps1 then checks each archive in depth; the
    assertions here are the cheap structural ones that fail before anything is
    uploaded.

.PARAMETER PublishRoot
    The folder holding the published `desktop` and `cli` folders.

.PARAMETER Archive
    zip for Windows runtimes, tar.gz for every other, as .github/release-targets.json
    declares them.

.EXAMPLE
    pwsh ./tools/archive-release-packages.ps1 -PublishRoot artifacts -Runtime linux-x64 -Archive tar.gz -Version 2.1.1 -Destination packages
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishRoot,

    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')]
    [string]$Runtime,

    [Parameter(Mandatory)]
    [ValidateSet('zip', 'tar.gz')]
    [string]$Archive,

    [Parameter(Mandatory)]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (($Runtime -like 'win-*') -ne ($Archive -eq 'zip')) {
    throw "Runtime '$Runtime' must not be archived as '$Archive': Windows ships zip, every other runtime tar.gz."
}

# [Path]::GetFullPath resolves against the process directory, which is not
# necessarily PowerShell's current location.
function Resolve-FullPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path $PWD.Path $Path))
}
$publishRoot = Resolve-FullPath $PublishRoot
$destination = Resolve-FullPath $Destination
New-Item -ItemType Directory -Path $destination -Force | Out-Null

# Keep macOS tar from emitting AppleDouble ._ files for extended attributes
# attached to published binaries.
$env:COPYFILE_DISABLE = '1'

# GNU tar reads a drive-qualified archive operand as host:path, so a Windows host
# uses its own bsdtar, as tools/verify-package-contents.ps1 does.
$tar = $null
if ($Archive -eq 'tar.gz') {
    $systemTar = if ($IsWindows) { Join-Path $env:SystemRoot 'System32\tar.exe' }
    $tar = if ($systemTar -and (Test-Path -LiteralPath $systemTar -PathType Leaf)) {
        $systemTar
    } else {
        (Get-Command tar -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    }
}

$archives = [System.Collections.Generic.List[string]]::new()
foreach ($component in @(
        [pscustomobject]@{ Folder = 'desktop'; Root = "VisualCat-Desktop-$Runtime-v$Version" }
        [pscustomobject]@{ Folder = 'cli'; Root = "VisualCat-CLI-$Runtime-v$Version" })) {
    $source = Join-Path $publishRoot $component.Folder
    $named = Join-Path $publishRoot $component.Root
    $archivePath = Join-Path $destination "$($component.Root).$Archive"
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Published folder '$source' does not exist. Publish and stage notices before archiving."
    }
    if (Test-Path -LiteralPath $named) { throw "'$named' already exists; it would be archived in place of the publish output." }
    if (Test-Path -LiteralPath $archivePath) { throw "Archive '$archivePath' already exists." }

    Rename-Item -LiteralPath $source -NewName $component.Root
    try {
        if ($Archive -eq 'zip') {
            Compress-Archive -LiteralPath $named -DestinationPath $archivePath
        } else {
            & $tar -C $publishRoot -czf $archivePath $component.Root
            if ($LASTEXITCODE -ne 0) { throw "Archiving '$($component.Root)' with tar failed." }
        }
    } finally {
        Rename-Item -LiteralPath $named -NewName $component.Folder
    }
    $archives.Add($archivePath)
}

# Two assertions that make F-02 unable to regress: exactly one top-level entry per
# archive, and it is a directory.
foreach ($archivePath in $archives) {
    $entries = if ($Archive -eq 'zip') {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
        try { @($zip.Entries | ForEach-Object { $_.FullName }) } finally { $zip.Dispose() }
    } else {
        @(& $tar -tzf $archivePath)
        if ($LASTEXITCODE -ne 0) { throw "Listing '$archivePath' with tar failed." }
    }
    # A member below a directory names that directory; a member with no slash at all
    # is a file sitting at the archive root.
    $roots = @($entries |
        ForEach-Object { ($_ -replace '\\', '/') -replace '^\./', '' } |
        Where-Object { $_ } |
        ForEach-Object { $parts = $_ -split '/', 2; if ($parts.Count -gt 1) { "$($parts[0])/" } else { $parts[0] } } |
        Sort-Object -Unique)
    $name = Split-Path -Leaf $archivePath
    if ($roots.Count -ne 1) {
        throw "$name has $($roots.Count) top-level entries: $($roots -join ', '). Release archives must contain exactly one directory."
    }
    if (-not $roots[0].EndsWith('/')) {
        throw "$name's single top-level entry '$($roots[0])' is a file, not a directory."
    }
    if ($env:GITHUB_STEP_SUMMARY) { "$name → $($roots[0])" | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY }
    Write-Host "$name → $($roots[0])"
}

$archives
