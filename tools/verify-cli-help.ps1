[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$expectedPath = Join-Path $repositoryRoot 'docs/CLI-HELP.txt'
$projectPath = Join-Path $repositoryRoot 'src/VisualCat.Cli/VisualCat.Cli.csproj'

$actualLines = @(& dotnet run --project $projectPath --configuration Release --no-build -- help)
if ($LASTEXITCODE -ne 0) {
    throw "vcat help failed with exit code $LASTEXITCODE."
}

if ($actualLines.Count -eq 0) {
    throw 'vcat help produced no output.'
}

$actualLines[0] = 'VisualCat v2 command line (<version>)'
$actual = (($actualLines -join "`n").TrimEnd()) + "`n"
$expected = ((Get-Content -Raw -LiteralPath $expectedPath).Replace("`r`n", "`n").TrimEnd()) + "`n"

if ($actual -cne $expected) {
    Write-Error 'CLI help changed. Update PrintHelp(), docs/CLI.md, and docs/CLI-HELP.txt together.'
}

Write-Host 'CLI help matches docs/CLI-HELP.txt.'
