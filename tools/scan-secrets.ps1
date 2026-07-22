<#
.SYNOPSIS
    Scans the working tree, and optionally all reachable Git history, for
    credential-shaped content.

.DESCRIPTION
    Prefers a real scanner: if gitleaks is on PATH it is used, because a
    maintained rule set beats a hand-written one. Otherwise the script falls
    back to a small high-signal pattern set covering the material this project
    could plausibly leak — cloud and service tokens, private keys, Android
    signing material, and hard-coded passwords.

    The fallback is a guardrail, not a substitute for a dedicated scanner. A
    clean result here does not close the launch gate on its own; run gitleaks or
    TruffleHog over all refs before changing repository visibility, and review
    every finding rather than suppressing it. If a real credential ever entered
    Git, revoke or rotate it first — rewriting history is not sufficient.

    Reviewed false positives are suppressed in one of two places, never
    silently: an inline `gitleaks:allow` marker on the offending line, which
    the real gitleaks honors too, or an entry in
    tools/secret-scan-allowlist.txt for findings in immutable history that no
    longer have a line to mark.

.PARAMETER History
    Also scan every blob reachable from every ref, not only the checkout.

.PARAMETER Tool
    Force a specific implementation instead of auto-detecting.

.PARAMETER ReportPath
    Write a redacted report of the scan, including everything that was
    suppressed and why, for the launch-gate record.
#>
[CmdletBinding()]
param(
    [switch]$History,

    [ValidateSet('auto', 'gitleaks', 'builtin')]
    [string]$Tool = 'auto',

    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

if ($Tool -eq 'auto') {
    $Tool = if (Get-Command gitleaks -ErrorAction SilentlyContinue) { 'gitleaks' } else { 'builtin' }
}

if ($Tool -eq 'gitleaks') {
    Write-Host 'Scanning with gitleaks.'
    if ($History) {
        gitleaks detect --source $repository --redact --verbose
    } else {
        gitleaks detect --source $repository --no-git --redact --verbose
    }
    if ($LASTEXITCODE -ne 0) {
        Write-Error 'gitleaks reported findings. Review each one; revoke and rotate any real credential before rewriting history.'
        exit 1
    }
    Write-Host 'gitleaks reported no findings.'
    exit 0
}

Write-Host 'gitleaks is not installed; using the built-in pattern scan.'
Write-Host 'Install gitleaks for the authoritative pre-publication scan.'

# Each rule is deliberately narrow. Broad entropy heuristics produce noise that
# gets ignored, which is worse than a small set that is always reviewed.
$rules = @(
    @{ Name = 'Private key block'; Pattern = '-----BEGIN (RSA|DSA|EC|OPENSSH|PGP|ENCRYPTED)? ?PRIVATE KEY( BLOCK)?-----' }
    @{ Name = 'AWS access key id'; Pattern = '\b(A3T[A-Z0-9]|AKIA|ASIA|ABIA|ACCA)[A-Z0-9]{16}\b' }
    @{ Name = 'GitHub token'; Pattern = '\bgh[pousr]_[A-Za-z0-9]{36,}\b' }
    @{ Name = 'GitHub fine-grained token'; Pattern = '\bgithub_pat_[A-Za-z0-9_]{40,}\b' }
    @{ Name = 'Slack token'; Pattern = '\bxox[abposr]-[A-Za-z0-9-]{10,}\b' }
    @{ Name = 'Google API key'; Pattern = '\bAIza[0-9A-Za-z_-]{35}\b' }
    @{ Name = 'NuGet API key'; Pattern = '\boy2[a-z0-9]{43}\b' }
    @{ Name = 'Azure/Google service-account key'; Pattern = '"private_key_id"\s*:\s*"[0-9a-f]{20,}"' }
    @{ Name = 'JSON Web Token'; Pattern = '\beyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b' }
    @{ Name = 'Hard-coded password assignment'; Pattern = '(?i)\b(password|passwd|pwd|secret|api[_-]?key|token)\b\s*[:=]\s*["''][^"''$%{<\s]{8,}["'']' }
    @{ Name = 'Connection string with password'; Pattern = '(?i)(server|data source|host)\s*=[^;\r\n]{1,120};[^\r\n]{0,200}?password\s*=\s*[^;\s"''][^;\r\n]*' }
    @{ Name = 'Keystore material'; Pattern = '(?i)-p:AndroidSigningStorePass\s*=\s*(?!\$)\S+' }
)

# Placeholders and documentation examples are expected in a repository that
# documents how to configure secrets.
$allowed = @(
    '(?i)\$\{\{\s*secrets\.'
    '(?i)\$env:'
    '(?i)\bYOUR[_-]?[A-Z]*(KEY|TOKEN|SECRET|PASSWORD)\b'
    '(?i)\b(example|placeholder|redacted|dummy|sample|changeme|xxxxx|<[a-z-]+>)\b'
)

# Findings in history cannot be marked inline, because the blob is immutable.
# They are classified once, here, with the reasoning kept under review.
$allowlistPath = Join-Path $PSScriptRoot 'secret-scan-allowlist.txt'
$allowlist = @()
if (Test-Path -LiteralPath $allowlistPath -PathType Leaf) {
    $allowlist = @(Get-Content -LiteralPath $allowlistPath |
        Where-Object { $_.Trim() -and -not $_.TrimStart().StartsWith('#') })
}

$findings = [System.Collections.Generic.List[string]]::new()
$suppressed = [System.Collections.Generic.List[string]]::new()

function Test-Content([string]$location, [string]$content) {
    if ([string]::IsNullOrEmpty($content)) {
        return
    }

    $lines = $null
    foreach ($rule in $rules) {
        foreach ($match in [regex]::Matches($content, $rule.Pattern)) {
            $evidence = $match.Value
            if ($allowed | Where-Object { $evidence -match $_ }) {
                continue
            }

            $lineNumber = ($content.Substring(0, $match.Index) -split "`n").Count
            if ($null -eq $lines) {
                $lines = $content -split "`r?`n"
            }

            # A reviewed false positive is suppressed at the site, using the
            # same marker gitleaks honors, so both implementations agree and
            # the justification stays next to the code.
            $lineText = if ($lineNumber -le $lines.Count) { $lines[$lineNumber - 1] } else { '' }
            $redacted = if ($evidence.Length -gt 24) { $evidence.Substring(0, 12) + '...[redacted]' } else { '[redacted]' }
            $finding = "$($rule.Name) at ${location}:${lineNumber} -> $redacted"

            if ($lineText -match 'gitleaks:allow') {
                $suppressed.Add("$finding (inline gitleaks:allow)")
                continue
            }
            if ($allowlist | Where-Object { $finding -match $_ }) {
                $suppressed.Add("$finding (secret-scan-allowlist.txt)")
                continue
            }

            $findings.Add($finding)
        }
    }
}

# --- working tree --------------------------------------------------------

$files = @(git -C $repository ls-files 2>$null)
if ($LASTEXITCODE -ne 0) {
    throw 'git ls-files failed; run this script inside the repository.'
}

$binaryExtensions = @('.png', '.jpg', '.jpeg', '.gif', '.svg', '.ico', '.zip', '.gz', '.dll', '.exe', '.pdb', '.vcat', '.woff', '.woff2', '.ttf')
$scanned = 0
foreach ($file in $files) {
    if ($binaryExtensions -contains [System.IO.Path]::GetExtension($file).ToLowerInvariant()) {
        continue
    }

    $path = Join-Path $repository $file
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        continue
    }
    # On Unix, PowerShell treats dotfiles as hidden. Get-Item therefore needs
    # -Force even after Test-Path has confirmed that a tracked file exists.
    if ((Get-Item -LiteralPath $path -Force).Length -gt 8MB) {
        continue
    }

    $scanned++
    Test-Content -location $file -content (Get-Content -Raw -LiteralPath $path -ErrorAction SilentlyContinue)
}

Write-Host "Scanned $scanned tracked files in the working tree."

# --- all reachable history ----------------------------------------------

if ($History) {
    # rev-list emits "<sha> [path]"; cat-file --batch-check filters to blobs and
    # gives sizes so large binaries can be skipped without reading them.
    $catalog = git -C $repository rev-list --objects --all |
        git -C $repository cat-file --batch-check='%(objectname) %(objecttype) %(objectsize) %(rest)'

    $historyScanned = 0
    foreach ($entry in $catalog) {
        $parts = $entry -split ' ', 4
        if ($parts.Count -lt 3 -or $parts[1] -ne 'blob') {
            continue
        }

        $sha = $parts[0]
        $size = [int64]$parts[2]
        $path = if ($parts.Count -ge 4) { $parts[3] } else { $sha }
        if ($size -gt 2MB -or $size -eq 0) {
            continue
        }
        if ($binaryExtensions -contains [System.IO.Path]::GetExtension($path).ToLowerInvariant()) {
            continue
        }

        $historyScanned++
        $content = git -C $repository cat-file blob $sha
        Test-Content -location "history:$path@$($sha.Substring(0, 8))" -content ($content -join "`n")
    }

    Write-Host "Scanned $historyScanned blobs reachable from all refs."
} else {
    Write-Host 'History was not scanned. Re-run with -History before changing repository visibility.'
}

# --- report --------------------------------------------------------------

$unique = @($findings | Sort-Object -Unique)
$uniqueSuppressed = @($suppressed | Sort-Object -Unique)

if ($uniqueSuppressed.Count -gt 0) {
    Write-Host "Suppressed $($uniqueSuppressed.Count) reviewed finding(s):"
    foreach ($entry in $uniqueSuppressed) {
        Write-Host "  - $entry"
    }
}

if ($ReportPath) {
    $report = @(
        "VisualCat secret scan (built-in pattern set)"
        "Generated: $([DateTimeOffset]::UtcNow.ToString('u'))"
        "Scope: working tree$(if ($History) { ' and all reachable refs' } else { ' only' })"
        "Rules: $($rules.Count)"
        ''
        "Unclassified findings: $($unique.Count)"
    )
    $report += $unique | ForEach-Object { "  FINDING $_" }
    $report += ''
    $report += "Reviewed and suppressed: $($uniqueSuppressed.Count)"
    $report += $uniqueSuppressed | ForEach-Object { "  SUPPRESSED $_" }
    Set-Content -LiteralPath $ReportPath -Value $report -Encoding utf8NoBOM
    Write-Host "Wrote redacted report to $ReportPath"
}

if ($unique.Count -gt 0) {
    foreach ($finding in $unique) {
        Write-Host "FINDING: $finding"
    }
    Write-Error "The secret scan produced $($unique.Count) unclassified finding(s). Review each one; revoke and rotate any real credential before rewriting history."
    exit 1
}

Write-Host 'No unclassified credential-shaped content found.'
