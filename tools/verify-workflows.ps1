<#
.SYNOPSIS
    Parses every shell body embedded in a GitHub Actions workflow.

.DESCRIPTION
    A workflow step that does not parse runs none of itself. The v2.0.14 release
    proved what that costs: the release workflow's "Archive packages" step held a
    parenthesised group containing two statements — which PowerShell reads as an
    unterminated expression — so the tar and Compress-Archive calls above the two
    archive assertions never executed either, and all four desktop jobs failed at
    once on Windows, Linux and both macOS architectures. The step was only ever
    reachable from a tag, so a pull request could not have caught it and the first
    execution in its life was the release itself.

    This reads each workflow's `run:` bodies, resolves which shell each one will
    actually be given, and syntax-checks it:

      * PowerShell bodies go through the PowerShell parser itself.
      * Bash bodies go through `bash -n` when a bash is available.

    Shell resolution follows GitHub's own precedence — a step's `shell:`, then the
    job's `defaults.run.shell`, then the workflow's, then the runner's platform
    default, which is pwsh on Windows and bash elsewhere. A matrix job that
    includes a Windows runner therefore has its unmarked bodies checked as
    PowerShell *and* as bash, because both interpreters really do run them.

    It never runs a single line of what it checks.

.PARAMETER Path
    Directory holding the workflows. Defaults to .github/workflows.

.PARAMETER MinimumBodies
    Fail if fewer than this many bodies were found. An extractor that silently
    stops matching would otherwise report success over an empty list, which is
    the one way a gate like this dies without anyone noticing.

.EXAMPLE
    pwsh ./tools/verify-workflows.ps1
#>
[CmdletBinding()]
param(
    [string]$Path,
    [int]$MinimumBodies = 10
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Path) { $Path = Join-Path $repository '.github/workflows' }
if (-not (Test-Path -LiteralPath $Path)) {
    throw "Workflow directory '$Path' does not exist."
}

function Get-Indent {
    param([string]$Line)
    return $Line.Length - $Line.TrimStart(' ').Length
}

# A YAML block scalar owns every following line indented deeper than its key, plus
# any blank line between them. `|` keeps the newlines; `>` folds a run of non-empty
# lines into spaces, which is how performance.yml writes its long dotnet commands —
# and a folded body is a different string from the literal one, so checking the
# literal spelling would check something the runner never sees.
function Read-BlockScalar {
    param(
        [string[]]$Lines,
        [int]$StartIndex,
        [int]$KeyIndent,
        [string]$Style
    )

    $body = [System.Collections.Generic.List[string]]::new()
    $index = $StartIndex + 1
    while ($index -lt $Lines.Count) {
        $line = $Lines[$index]
        if ($line.Trim().Length -eq 0) { $body.Add(''); $index++; continue }
        if ((Get-Indent $line) -le $KeyIndent) { break }
        $body.Add($line)
        $index++
    }

    while ($body.Count -gt 0 -and $body[$body.Count - 1] -eq '') { $body.RemoveAt($body.Count - 1) }
    if ($body.Count -eq 0) { return [pscustomobject]@{ Text = ''; NextIndex = $index } }

    $contentIndents = @($body | Where-Object { $_ -ne '' } | ForEach-Object { Get-Indent $_ })
    $strip = ($contentIndents | Measure-Object -Minimum).Minimum
    $stripped = @($body | ForEach-Object { if ($_ -eq '') { '' } else { $_.Substring($strip) } })

    if ($Style -eq '>') {
        $folded = [System.Collections.Generic.List[string]]::new()
        $current = ''
        foreach ($line in $stripped) {
            if ($line -eq '') {
                $folded.Add($current); $current = ''; $folded.Add('')
            } elseif ($current -eq '') {
                $current = $line
            } else {
                $current = "$current $($line.Trim())"
            }
        }
        if ($current -ne '') { $folded.Add($current) }
        $text = ($folded -join "`n")
    } else {
        $text = ($stripped -join "`n")
    }

    return [pscustomobject]@{ Text = $text; NextIndex = $index }
}

# GitHub expressions are substituted before the shell ever sees the body, and their
# values are unknowable here. A bare placeholder keeps the surrounding syntax intact
# without pretending to know what it expands to; quoted uses stay quoted, so a value
# containing a space cannot make this gate disagree with the runner about tokens.
function Remove-GitHubExpressions {
    param([string]$Text)
    return [regex]::Replace($Text, '\$\{\{[^}]*\}\}', 'GITHUB_EXPRESSION')
}

function Get-JobRegions {
    param([string[]]$Lines)

    $regions = [System.Collections.Generic.List[object]]::new()
    $jobsIndent = -1
    $jobIndent = -1
    $current = $null
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        $line = $Lines[$index]
        if ($line.Trim().Length -eq 0 -or $line.TrimStart().StartsWith('#')) { continue }
        $indent = Get-Indent $line

        if ($jobsIndent -lt 0) {
            if ($line -match '^jobs:\s*$') { $jobsIndent = $indent }
            continue
        }

        if ($indent -le $jobsIndent -and $line -notmatch '^\s*$') {
            if ($current) { $current.End = $index - 1; $regions.Add($current); $current = $null }
            $jobsIndent = -1
            continue
        }

        if ($jobIndent -lt 0 -and $indent -gt $jobsIndent -and $line -match '^\s*[A-Za-z0-9_.-]+:\s*$') {
            $jobIndent = $indent
        }

        if ($indent -eq $jobIndent -and $line -match '^\s*(?<name>[A-Za-z0-9_.-]+):\s*$') {
            if ($current) { $current.End = $index - 1; $regions.Add($current) }
            $current = [pscustomobject]@{ Name = $Matches['name']; Start = $index; End = $Lines.Count - 1 }
        }
    }

    if ($current) { $regions.Add($current) }
    return $regions
}

# Which interpreters a body will really be handed. More than one is the honest
# answer for an unmarked body in a job whose matrix spans Windows and Unix.
function Resolve-Shells {
    param(
        [string[]]$Lines,
        [int]$RunIndex,
        [int]$RunIndent,
        [object]$Job,
        [string]$WorkflowDefaultShell
    )

    # A step's own `shell:` sits at the same indentation as its `run:`, inside the
    # same list item. Walk out to the item's bounds rather than guessing a
    # direction: `shell:` is written above `run:` here and below it elsewhere.
    $stepStart = $RunIndex
    for ($index = $RunIndex; $index -ge 0; $index--) {
        $line = $Lines[$index]
        if ($line.Trim().Length -eq 0) { continue }
        if ((Get-Indent $line) -lt $RunIndent) { break }
        if ((Get-Indent $line) -eq $RunIndent - 2 -and $line.TrimStart().StartsWith('- ')) { $stepStart = $index; break }
        $stepStart = $index
    }
    $stepEnd = $Lines.Count - 1
    for ($index = $stepStart + 1; $index -lt $Lines.Count; $index++) {
        $line = $Lines[$index]
        if ($line.Trim().Length -eq 0) { continue }
        $indent = Get-Indent $line
        if ($indent -lt $RunIndent) { $stepEnd = $index - 1; break }
        if ($indent -eq $RunIndent - 2 -and $line.TrimStart().StartsWith('- ')) { $stepEnd = $index - 1; break }
    }

    for ($index = $stepStart; $index -le $stepEnd; $index++) {
        if ($Lines[$index] -match '^\s{' + $RunIndent + '}shell:\s*(?<shell>\S+)\s*$') {
            return , @($Matches['shell'])
        }
    }

    $jobLines = $Lines[$Job.Start..$Job.End]
    $jobDefault = $null
    for ($index = 0; $index -lt $jobLines.Count - 2; $index++) {
        if ($jobLines[$index] -match '^\s*defaults:\s*$' -and $jobLines[$index + 1] -match '^\s*run:\s*$' -and
            $jobLines[$index + 2] -match '^\s*shell:\s*(?<shell>\S+)\s*$') {
            $jobDefault = $Matches['shell']
            break
        }
    }
    if ($jobDefault) { return , @($jobDefault) }
    if ($WorkflowDefaultShell) { return , @($WorkflowDefaultShell) }

    # No shell was declared anywhere, so the runner's operating system decides.
    $runsOn = ($jobLines | Where-Object { $_ -match '^\s*runs-on:\s*(?<value>.+?)\s*$' } | Select-Object -First 1)
    $platforms = @()
    if ($runsOn -and $runsOn -match '^\s*runs-on:\s*(?<value>.+?)\s*$') {
        $value = $Matches['value']
        if ($value -match '\$\{\{') {
            # Matrix-driven. Every os the matrix lists really does run this body.
            foreach ($line in $jobLines) {
                if ($line -match '^\s*os:\s*\[(?<list>[^\]]+)\]') {
                    $platforms += ($Matches['list'] -split ',' | ForEach-Object { $_.Trim().Trim('"', "'") })
                }
            }
            if (-not $platforms) { $platforms = @('ubuntu-latest', 'windows-latest', 'macos-latest') }
        } else {
            $platforms = @($value.Trim('"', "'"))
        }
    }

    $shells = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($platform in $platforms) {
        if ($platform -match 'windows') { [void]$shells.Add('pwsh') } else { [void]$shells.Add('bash') }
    }
    if ($shells.Count -eq 0) { [void]$shells.Add('bash') }
    return , @($shells)
}

$bash = Get-Command bash -ErrorAction SilentlyContinue
$workflows = @(Get-ChildItem -LiteralPath $Path -Filter '*.yml' -File | Sort-Object Name)
if ($workflows.Count -eq 0) { throw "No workflows found under '$Path'." }

$checked = 0
$skipped = 0
$failures = [System.Collections.Generic.List[string]]::new()
$rows = [System.Collections.Generic.List[object]]::new()

foreach ($workflow in $workflows) {
    $lines = [IO.File]::ReadAllLines($workflow.FullName)
    $jobs = Get-JobRegions -Lines $lines

    $workflowDefaultShell = $null
    for ($index = 0; $index -lt $lines.Count - 2; $index++) {
        if ($lines[$index] -match '^defaults:\s*$' -and $lines[$index + 1] -match '^\s*run:\s*$' -and
            $lines[$index + 2] -match '^\s*shell:\s*(?<shell>\S+)\s*$') {
            $workflowDefaultShell = $Matches['shell']
            break
        }
    }

    $index = 0
    while ($index -lt $lines.Count) {
        $line = $lines[$index]
        if ($line -notmatch '^(?<indent>\s*)run:\s*(?<style>[|>])(?<chomp>[-+]?)\s*$') { $index++; continue }

        $runIndent = $Matches['indent'].Length
        $style = $Matches['style']
        $block = Read-BlockScalar -Lines $lines -StartIndex $index -KeyIndent $runIndent -Style $style
        $body = Remove-GitHubExpressions -Text $block.Text
        $job = $jobs | Where-Object { $index -ge $_.Start -and $index -le $_.End } | Select-Object -First 1
        if (-not $job) { $job = [pscustomobject]@{ Name = '(top level)'; Start = 0; End = $lines.Count - 1 } }

        $shells = Resolve-Shells -Lines $lines -RunIndex $index -RunIndent $runIndent -Job $job -WorkflowDefaultShell $workflowDefaultShell
        $location = "$($workflow.Name):$($index + 1) [$($job.Name)]"

        foreach ($shell in $shells) {
            $normalized = $shell.ToLowerInvariant()
            if ($normalized -in @('pwsh', 'powershell')) {
                $errors = $null
                [void][System.Management.Automation.Language.Parser]::ParseInput($body, [ref]$null, [ref]$errors)
                if ($errors -and $errors.Count -gt 0) {
                    foreach ($parseError in $errors) {
                        $failures.Add("$location as $normalized -> line $($parseError.Extent.StartLineNumber): $($parseError.Message)")
                    }
                }
                $checked++
                $rows.Add([pscustomobject]@{ Where = $location; Shell = $normalized; Result = if ($errors -and $errors.Count) { 'FAIL' } else { 'ok' } })
            } elseif ($normalized -in @('bash', 'sh')) {
                if (-not $bash) { $skipped++; continue }

                # The body goes in on stdin rather than as a temporary file. A Git Bash
                # on Windows cannot open a native `C:\...` path handed to it as an
                # argument, so a file-based check reports "No such file or directory"
                # for every body and reads exactly like a workflow full of syntax errors.
                $output = ($body + "`n") | & $bash.Source -n 2>&1
                $ok = $LASTEXITCODE -eq 0
                if (-not $ok) { $failures.Add("$location as bash -> $($output -join '; ')") }
                $checked++
                $rows.Add([pscustomobject]@{ Where = $location; Shell = 'bash'; Result = if ($ok) { 'ok' } else { 'FAIL' } })
            } else {
                $skipped++
            }
        }

        $index = $block.NextIndex
    }
}

$rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

# Every third-party action is pinned to a commit, not to a tag. A tag is a movable
# label on someone else's repository, so `uses: someone/action@v4` is a standing
# grant to run whatever they point v4 at tomorrow, with this workflow's token. The
# repository already pins every action by hand; this is the assertion that keeps it
# true, because the one that gets added in a hurry is the one that will not be.
$unpinned = [System.Collections.Generic.List[string]]::new()
$pinned = 0
foreach ($workflow in $workflows) {
    $lineNumber = 0
    foreach ($line in [IO.File]::ReadAllLines($workflow.FullName)) {
        $lineNumber++
        if ($line -notmatch '^\s*-?\s*uses:\s*(?<ref>\S+)') { continue }
        $reference = $Matches['ref']

        # A local composite action is this repository's own code, already reviewed.
        if ($reference.StartsWith('./') -or $reference.StartsWith('.\')) { $pinned++; continue }

        if ($reference -match '@[0-9a-f]{40}$') { $pinned++; continue }
        $unpinned.Add("$($workflow.Name):$lineNumber uses '$reference', which is a tag or branch rather than a 40-character commit SHA.")
    }
}

if ($unpinned.Count -gt 0) {
    $unpinned | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }
    $failures.Add("$($unpinned.Count) action reference(s) are not pinned to a commit SHA.")
} else {
    Write-Host "All $pinned action reference(s) are pinned to a commit SHA." -ForegroundColor Green
}

if ($checked -lt $MinimumBodies) {
    throw "Only $checked shell bodies were checked across $($workflows.Count) workflow(s); at least $MinimumBodies were expected. The extractor is probably no longer matching, which would make this gate pass over nothing."
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }
    throw "$($failures.Count) workflow shell body/bodies do not parse. A step that does not parse runs none of itself."
}

Write-Host "Checked $checked shell body/bodies across $($workflows.Count) workflow(s); $skipped skipped. All parse." -ForegroundColor Green
