<#
.SYNOPSIS
    Verifies a finished release archive by extracting it and exercising its
    contents.

.DESCRIPTION
    Building successfully is not the same as shipping a usable archive. This
    script inspects the bytes that will actually be uploaded:

      * archive entries contain no absolute paths, drive letters, parent
        traversal, or unexpected top-level nesting;
      * the expected executable and every notice file are present;
      * a CLI archive reports the expected version and prints help; and
      * the extracted size is reported so sudden bloat is visible.

    It is used by the release workflow after packaging and by
    tools/verify-public-release.ps1 locally.
#>
[CmdletBinding()]
param(
    # Release archive to verify (.zip or .tar.gz).
    [Parameter(Mandatory)]
    [string]$Archive,

    [Parameter(Mandatory)]
    [ValidateSet('Desktop', 'Cli')]
    [string]$Component,

    [Parameter(Mandatory)]
    [string]$Version,

    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')]
    [string]$Runtime,

    # Skip executing the archived binary. Required when the archive targets a
    # different architecture or operating system than the verifying machine.
    [switch]$SkipExecution
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# [Path]::GetFullPath resolves against the process directory, which is not
# necessarily PowerShell's current location.
$archivePath = if ([System.IO.Path]::IsPathRooted($Archive)) {
    [System.IO.Path]::GetFullPath($Archive)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $PWD.Path $Archive))
}

if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
    throw "Archive '$archivePath' does not exist."
}

$isWindowsRuntime = $Runtime -eq 'win-x64'
$executableName = switch ($Component) {
    'Desktop' { if ($isWindowsRuntime) { 'VisualCat.exe' } else { 'VisualCat' } }
    'Cli' { if ($isWindowsRuntime) { 'vcat.exe' } else { 'vcat' } }
}

# --- archive entry names -------------------------------------------------

if ($archivePath.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase)) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $entries = @($zip.Entries | ForEach-Object { $_.FullName })
    } finally {
        $zip.Dispose()
    }
} else {
    $entries = @(tar -tzf $archivePath)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list entries of '$archivePath'."
    }
}

if ($entries.Count -eq 0) {
    throw "Archive '$archivePath' is empty."
}

foreach ($entry in $entries) {
    $normalized = $entry -replace '\\', '/'
    if ($normalized.StartsWith('/') -or $normalized -match '^[A-Za-z]:') {
        throw "Archive '$archivePath' contains an absolute path: $entry"
    }
    if (($normalized -split '/') -contains '..') {
        throw "Archive '$archivePath' contains a parent-traversal path: $entry"
    }
}

# Files must sit at the archive root so extracting gives a usable directory
# directly, without hunting through a wrapper folder.
$topLevel = @($entries |
    ForEach-Object { ($_ -replace '\\', '/') -replace '^\./', '' } |
    Where-Object { $_ } |
    ForEach-Object { ($_ -split '/')[0] } |
    Sort-Object -Unique)
if ($topLevel -notcontains $executableName) {
    throw "Archive '$archivePath' does not contain '$executableName' at its root. Top-level entries: $($topLevel -join ', ')"
}

# --- extracted contents --------------------------------------------------

$extractRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("visualcat-verify-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
try {
    if ($archivePath.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase)) {
        Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot -Force
    } else {
        tar -xzf $archivePath -C $extractRoot
        if ($LASTEXITCODE -ne 0) {
            throw "Extraction of '$archivePath' failed."
        }
    }

    foreach ($required in @($executableName, 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'README.txt')) {
        $path = Join-Path $extractRoot $required
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Extracted archive '$(Split-Path -Leaf $archivePath)' is missing '$required'."
        }
        if ((Get-Item -LiteralPath $path).Length -eq 0) {
            throw "Extracted archive '$(Split-Path -Leaf $archivePath)' contains an empty '$required'."
        }
    }

    $readme = Get-Content -Raw -LiteralPath (Join-Path $extractRoot 'README.txt')
    if ($readme -notmatch [regex]::Escape($Version)) {
        throw "README.txt in '$(Split-Path -Leaf $archivePath)' does not mention version $Version."
    }

    $executablePath = Join-Path $extractRoot $executableName
    if (-not $isWindowsRuntime) {
        # tar preserves the executable bit; Compress-Archive/Expand-Archive do
        # not, which is why Unix runtimes are never packaged as zip.
        if ($archivePath.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unix runtime '$Runtime' must not be packaged as a zip archive; the executable bit would be lost."
        }

        # Only a Unix filesystem records the bit, so this can be asserted only
        # when the verifying machine is itself Unix.
        if (-not $IsWindows) {
            $mode = (Get-Item -LiteralPath $executablePath).UnixMode
            if ($mode -notmatch 'x') {
                throw "Archived '$executableName' is not executable after extraction (mode '$mode'). Users would have to chmod it."
            }
        }
    }

    if ($SkipExecution) {
        Write-Host "Skipping execution of $executableName ($Runtime) on this machine."
    } elseif ($Component -eq 'Cli') {
        $reported = & $executablePath --version
        if ($LASTEXITCODE -ne 0) {
            throw "'$executableName --version' from the archive failed with exit code $LASTEXITCODE."
        }
        $reported = ($reported | Select-Object -First 1)
        # InformationalVersion appends '+<commit>' through SourceLink.
        if ($reported -ne "vcat $Version" -and $reported -notlike "vcat $Version+*") {
            throw "Archived CLI reports '$reported' but the release version is '$Version'."
        }

        $help = @(& $executablePath help)
        if ($LASTEXITCODE -ne 0) {
            throw "'$executableName help' from the archive failed with exit code $LASTEXITCODE."
        }
        if ($help.Count -lt 5) {
            throw "'$executableName help' from the archive printed unexpectedly little output."
        }
        Write-Host "Archived CLI reports: $reported"
    } else {
        # Desktop startup needs a display server, so it is not launched here.
        # Presence, layout, and notices are verified instead.
        Write-Host "Desktop executable present: $executableName"
    }

    $bytes = (Get-ChildItem -LiteralPath $extractRoot -Recurse -File | Measure-Object -Property Length -Sum).Sum
    $archiveBytes = (Get-Item -LiteralPath $archivePath).Length
    $summary = '{0}: archive {1:N1} MB, extracted {2:N1} MB, {3} entries' -f `
        (Split-Path -Leaf $archivePath), ($archiveBytes / 1MB), ($bytes / 1MB), $entries.Count
    Write-Host $summary
    if ($env:GITHUB_STEP_SUMMARY) {
        "- $summary" | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8
    }
} finally {
    Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Verified $(Split-Path -Leaf $archivePath)."
