<#
.SYNOPSIS
    Answers "is this commit mechanically ready to package?" with one command.

.DESCRIPTION
    Composes the checks that already exist rather than reimplementing them:
    formatting, build, tests, CLI help, documentation and version consistency,
    vulnerable packages, packaging with the notice files users receive, a
    CycloneDX SBOM and license-policy review, and a secret scan.

    The script is read-only with respect to the repository: it never tags,
    pushes, publishes, or rewrites anything. Output goes to a scratch directory
    that is safe to delete. It exits non-zero naming the first failing stage.

.EXAMPLE
    pwsh ./tools/verify-public-release.ps1

.EXAMPLE
    # Full pre-release sweep, including every shipped runtime and Git history.
    pwsh ./tools/verify-public-release.ps1 -AllRuntimes -ScanHistory
#>
[CmdletBinding()]
param(
    # Package every shipped runtime instead of only this machine's.
    [switch]$AllRuntimes,

    # Scan all reachable Git history for secrets, not only the working tree.
    [switch]$ScanHistory,

    # Stages to skip, for iterating quickly on one area.
    [ValidateSet('Format', 'Build', 'Test', 'Cli', 'Docs', 'Vulnerable', 'Package', 'Sbom', 'Secrets')]
    [string[]]$Skip = @(),

    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts\verify')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solution = Join-Path $repository 'VisualCat.Desktop.slnx'
$output = [System.IO.Path]::GetFullPath($OutputRoot)
$temporary = Join-Path $output 'temp'
New-Item -ItemType Directory -Path $temporary -Force | Out-Null
# Some service and sandbox shells inherit a system temp directory they cannot
# write. Child dotnet, MSBuild, archive, and scanning processes all receive the
# dedicated ignored scratch directory owned by this preflight process.
$env:TEMP = $temporary
$env:TMP = $temporary
$results = [System.Collections.Generic.List[object]]::new()
$failed = $null

function Invoke-Stage {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    if ($script:failed) {
        $script:results.Add([pscustomobject]@{ Stage = $Name; Result = 'skipped'; Seconds = 0 })
        return
    }
    if ($Skip -contains $Name) {
        Write-Host "== $Name : skipped by request" -ForegroundColor DarkGray
        $script:results.Add([pscustomobject]@{ Stage = $Name; Result = 'skipped'; Seconds = 0 })
        return
    }

    Write-Host ''
    Write-Host "== $Name" -ForegroundColor Cyan
    $started = [Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action
        $started.Stop()
        Write-Host "== $Name : ok ($([int]$started.Elapsed.TotalSeconds)s)" -ForegroundColor Green
        $script:results.Add([pscustomobject]@{ Stage = $Name; Result = 'ok'; Seconds = [int]$started.Elapsed.TotalSeconds })
    } catch {
        $started.Stop()
        Write-Host "== $Name : FAILED — $($_.Exception.Message)" -ForegroundColor Red
        $script:results.Add([pscustomobject]@{ Stage = $Name; Result = 'failed'; Seconds = [int]$started.Elapsed.TotalSeconds })
        $script:failed = $Name
    }
}

function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$Description,
        [Parameter(Mandatory)][scriptblock]$Command
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

Write-Host "VisualCat public-release preflight"
Write-Host "Repository: $repository"

Invoke-Stage 'Format' {
    Invoke-Native 'dotnet restore' { dotnet restore $solution }
    Invoke-Native 'dotnet format --verify-no-changes' {
        dotnet format $solution --no-restore --verify-no-changes
    }
}

Invoke-Stage 'Build' {
    Invoke-Native 'dotnet build (Release)' {
        dotnet build $solution --no-restore --configuration Release
    }
}

Invoke-Stage 'Test' {
    Invoke-Native 'dotnet test' {
        dotnet test $solution --no-restore --no-build --configuration Release
    }
}

Invoke-Stage 'Cli' {
    $session = Join-Path $output 'preflight.vcat'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    Remove-Item -LiteralPath $session -Recurse -Force -ErrorAction SilentlyContinue

    $cli = Join-Path $repository 'src/VisualCat.Cli'
    $sample = Join-Path $repository 'samples/logcat_small.txt'
    Invoke-Native 'vcat index' {
        dotnet run --project $cli --configuration Release --no-build -- index $sample --output $session
    }
    Invoke-Native 'vcat verify' {
        dotnet run --project $cli --configuration Release --no-build -- verify $session
    }
    Invoke-Native 'vcat --version' {
        dotnet run --project $cli --configuration Release --no-build -- --version
    }
    Invoke-Native 'verify-cli-help.ps1' {
        & (Join-Path $PSScriptRoot 'verify-cli-help.ps1')
    }
}

Invoke-Stage 'Docs' {
    Invoke-Native 'verify-docs.ps1' {
        & (Join-Path $PSScriptRoot 'verify-docs.ps1')
    }
    Invoke-Native 'render-release-notes.ps1' {
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        & (Join-Path $PSScriptRoot 'render-release-notes.ps1') `
            -Version '2.0.8-preview.local' `
            -Destination (Join-Path $output 'release-notes.md')
    }
}

Invoke-Stage 'Vulnerable' {
    # `dotnet list package --vulnerable` exits 0 even when it reports packages,
    # so the output has to be inspected.
    $report = & dotnet list $solution package --vulnerable --include-transitive 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet list package --vulnerable failed with exit code $LASTEXITCODE."
    }
    $text = $report -join "`n"
    if ($text -match 'has the following vulnerable packages') {
        Write-Host $text
        throw 'Vulnerable packages were reported.'
    }
    Write-Host 'No known vulnerable direct or transitive packages.'
}

Invoke-Stage 'Package' {
    $runtimes = if ($AllRuntimes) {
        @('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')
    } elseif ($IsWindows) {
        @('win-x64')
    } elseif ($IsMacOS) {
        if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') { @('osx-arm64') } else { @('osx-x64') }
    } else {
        @('linux-x64')
    }

    Invoke-Native 'package.ps1 -Archive' {
        & (Join-Path $PSScriptRoot 'package.ps1') -Runtime $runtimes -OutputRoot (Join-Path $output 'packages') -Archive
    }
}

Invoke-Stage 'Sbom' {
    Invoke-Native 'generate-sbom.ps1' {
        & (Join-Path $PSScriptRoot 'generate-sbom.ps1') `
            -OutputRoot (Join-Path $output 'sbom')
    }
}

Invoke-Stage 'Secrets' {
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    Invoke-Native 'scan-secrets.ps1' {
        & (Join-Path $PSScriptRoot 'scan-secrets.ps1') -History:$ScanHistory -ReportPath (Join-Path $output 'secret-scan-report.txt')
    }
}

Write-Host ''
$results | Format-Table -AutoSize | Out-String | Write-Host

if ($failed) {
    Write-Error "Public-release preflight failed at stage '$failed'."
    exit 1
}

Write-Host 'Public-release preflight passed.' -ForegroundColor Green
Write-Host 'This checks machine-verifiable readiness only. The remaining launch gates'
Write-Host 'are in docs/RELEASE-CHECKLIST.md.'
