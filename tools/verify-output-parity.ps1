<#
.SYNOPSIS
    Proves every text VisualCat writes is byte-identical on every platform.

.DESCRIPTION
    Every text export and every JSON result used to be written with the host's own
    newline, so the same session exported on Linux and on Windows differed by 5,001
    carriage returns and nothing else. Two machines could not diff one session's
    export, commit it, or compare checksums — and nothing said why, because each
    file was perfectly correct on the machine that wrote it. The fix made LF the
    default everywhere, with `--newline crlf` for the reader who wants the other
    one. That is a promise about bytes, and a promise about bytes is only kept if
    something compares bytes.

    Two modes, because the failure has two shapes:

      -Record writes a fingerprint of every export type this platform produces,
       and fails here and now if a text export contains a carriage return the
       reader did not ask for. That catches the wrong platform on the wrong
       platform, with a message naming the file.

      -Compare reads the fingerprints every platform recorded and fails if any
       two disagree. That catches everything else — a field ordered by a culture,
       a float formatted by a locale, a path separator reaching a document — none
       of which any single machine can see on its own.

    The corpus is generated from a fixed seed, so the fingerprint is a property of
    the code rather than of the run.

.PARAMETER Record
    File to write this platform's fingerprint to.

.PARAMETER Compare
    Directory of fingerprints, one per platform, to check against each other.

.PARAMETER Cli
    The vcat entry point to drive. Defaults to the built Release CLI.

.EXAMPLE
    pwsh ./tools/verify-output-parity.ps1 -Record fingerprint-Linux.txt

.EXAMPLE
    pwsh ./tools/verify-output-parity.ps1 -Compare ./fingerprints
#>
[CmdletBinding(DefaultParameterSetName = 'Record')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Record')]
    [string]$Record,

    [Parameter(Mandatory, ParameterSetName = 'Compare')]
    [string]$Compare,

    [Parameter(ParameterSetName = 'Record')]
    [string]$Cli
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

# Every export type whose bytes are a function of the session alone. `query` is
# deliberately absent: it prints the session id, which is a fresh GUID per import
# and would make the fingerprint differ from itself on one machine.
$exportTypes = @('raw', 'csv', 'templates-csv', 'templates-md', 'stats-csv', 'stats-md')

# Types that are text a human reads or a tool diffs, and therefore carry the
# newline promise. `raw` is the log's own bytes reproduced verbatim, so its line
# endings are the source's and not this product's to normalise.
$newlineGoverned = @('csv', 'templates-csv', 'templates-md', 'stats-csv', 'stats-md')

if ($PSCmdlet.ParameterSetName -eq 'Compare') {
    $root = [IO.Path]::GetFullPath($Compare)
    if (-not (Test-Path -LiteralPath $root)) { throw "Fingerprint directory '$root' does not exist." }

    # Recursive: downloaded artifacts arrive one directory deep, named per platform.
    $files = @(Get-ChildItem -LiteralPath $root -Filter '*.txt' -File -Recurse | Sort-Object Name)
    if ($files.Count -lt 2) {
        throw "Found $($files.Count) fingerprint(s) under '$root'. Comparing fewer than two platforms proves nothing, so this is a failure rather than a pass."
    }

    $byPlatform = [ordered]@{}
    foreach ($file in $files) {
        $entries = [ordered]@{}
        foreach ($line in [IO.File]::ReadAllLines($file.FullName)) {
            if ($line -match '^(?<name>\S+)\s+(?<hash>[0-9a-f]{64})\s+(?<bytes>\d+)$') {
                $entries[$Matches['name']] = [pscustomobject]@{ Hash = $Matches['hash']; Bytes = [long]$Matches['bytes'] }
            }
        }
        if ($entries.Count -eq 0) { throw "Fingerprint '$($file.Name)' contains no entries." }
        $byPlatform[$file.BaseName] = $entries
    }

    $platforms = @($byPlatform.Keys)
    $reference = $platforms[0]
    $names = @($byPlatform[$reference].Keys)
    $differences = [System.Collections.Generic.List[string]]::new()

    foreach ($platform in $platforms[1..($platforms.Count - 1)]) {
        $theirs = $byPlatform[$platform]
        foreach ($name in $names) {
            if (-not $theirs.Contains($name)) {
                $differences.Add("$name is missing from $platform.")
                continue
            }
            $mine = $byPlatform[$reference][$name]
            $other = $theirs[$name]
            if ($mine.Hash -ne $other.Hash) {
                $delta = $other.Bytes - $mine.Bytes
                $hint = if ($delta -ne 0) { " ($reference $($mine.Bytes) B, $platform $($other.Bytes) B, a difference of $delta)" } else { ' (same length, different bytes)' }
                $differences.Add("$name differs between $reference and $platform$hint.")
            }
        }
        foreach ($name in $theirs.Keys) {
            if ($names -notcontains $name) { $differences.Add("$name is present on $platform but not on $reference.") }
        }
    }

    Write-Host "Compared $($names.Count) output(s) across $($platforms.Count) platform(s): $($platforms -join ', ')"
    if ($differences.Count -gt 0) {
        $differences | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }
        throw "$($differences.Count) output(s) are not byte-identical across platforms. The product promises they are."
    }

    Write-Host 'Every recorded output is byte-identical on every platform.' -ForegroundColor Green
    return
}

if (-not $Cli) {
    $Cli = Join-Path $repository 'src/VisualCat.Cli/bin/Release/net10.0/vcat.dll'
}

function Invoke-Vcat {
    param([string[]]$Arguments)
    if ([IO.Path]::GetExtension($Cli) -eq '.dll') {
        $output = & dotnet $Cli @Arguments 2>&1
    } else {
        $output = & $Cli @Arguments 2>&1
    }
    if ($LASTEXITCODE -ne 0) {
        throw "vcat $($Arguments -join ' ') failed with exit code $LASTEXITCODE.`n$($output -join [Environment]::NewLine)"
    }
    return $output
}

$work = Join-Path ([IO.Path]::GetTempPath()) ("visualcat-parity-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
try {
    # A fixed seed and a fixed name: the file name reaches the statistics document, so
    # a temporary name would make the fingerprint differ from itself between runs.
    $log = Join-Path $work 'log.txt'
    $session = Join-Path $work 'session.vcat'
    Invoke-Vcat @('generate-test-log', '--output', $log, '--lines', '2000', '--seed', '20260916') | Out-Null
    Invoke-Vcat @('index', $log, '--output', $session) | Out-Null

    $lines = [System.Collections.Generic.List[string]]::new()
    $carriageReturns = [System.Collections.Generic.List[string]]::new()
    foreach ($type in $exportTypes) {
        $destination = Join-Path $work "export.$type"
        Invoke-Vcat @('export', $session, $destination, '--type', $type) | Out-Null
        if (-not (Test-Path -LiteralPath $destination)) { throw "Export '$type' produced no file." }

        $bytes = [IO.File]::ReadAllBytes($destination)
        if ($bytes.Length -eq 0) { throw "Export '$type' produced an empty file; the fingerprint would prove nothing." }

        if ($newlineGoverned -contains $type -and ($bytes -contains [byte]13)) {
            $carriageReturns.Add($type)
        }

        $hash = [BitConverter]::ToString([Security.Cryptography.SHA256]::HashData($bytes)).Replace('-', '').ToLowerInvariant()
        $lines.Add("$type $hash $($bytes.Length)")
    }

    if ($carriageReturns.Count -gt 0) {
        throw "These exports contain carriage returns nobody asked for: $($carriageReturns -join ', '). The default is LF on every platform; CRLF is available through --newline crlf."
    }

    $destinationPath = if ([IO.Path]::IsPathRooted($Record)) { $Record } else { Join-Path $PWD.Path $Record }
    $parent = Split-Path -Parent $destinationPath
    if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }

    # The fingerprint file itself is written with explicit LF. A fingerprint whose own
    # newlines differed per platform would be a comparison that never agrees.
    [IO.File]::WriteAllText($destinationPath, (($lines -join "`n") + "`n"))

    $lines | ForEach-Object { Write-Host "  $_" }
    Write-Host "Recorded $($lines.Count) output fingerprint(s) at $destinationPath" -ForegroundColor Green
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
