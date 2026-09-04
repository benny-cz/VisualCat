<#
.SYNOPSIS
    Runs the non-Linux CI test suite and turns TRX failures into GitHub annotations.

.DESCRIPTION
    GitHub's unauthenticated checks API exposes annotations but not raw Actions logs.
    Keeping the ordinary console output is useful to a person reading the job; emitting the
    same failure as an annotation also makes a platform-only failure diagnosable through the
    checks API. TRX files are retained by the workflow for the complete record.
#>
[CmdletBinding()]
param(
    [string]$ResultsDirectory = 'TestResults/ci'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solution = Join-Path $repository 'VisualCat.Desktop.slnx'
$results = [IO.Path]::GetFullPath((Join-Path $repository $ResultsDirectory))
$resultPrefix = "visualcat-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $results -Force | Out-Null

& dotnet test $solution --no-restore --no-build --configuration Release -m:1 `
    --logger "trx;LogFilePrefix=$resultPrefix" --results-directory $results
$testExitCode = $LASTEXITCODE

function ConvertTo-WorkflowData([string]$Value) {
    $result = $Value.Replace('%', '%25', [StringComparison]::Ordinal)
    $result = $result.Replace("`r", '%0D', [StringComparison]::Ordinal)
    $result.Replace("`n", '%0A', [StringComparison]::Ordinal)
}

function ConvertTo-WorkflowProperty([string]$Value) {
    $result = ConvertTo-WorkflowData $Value
    $result = $result.Replace(':', '%3A', [StringComparison]::Ordinal)
    $result.Replace(',', '%2C', [StringComparison]::Ordinal)
}

$reported = 0
foreach ($file in Get-ChildItem -LiteralPath $results -Recurse -Filter "$resultPrefix*.trx") {
    [xml]$document = Get-Content -Raw -LiteralPath $file.FullName
    $failures = $document.SelectNodes("//*[local-name()='UnitTestResult' and @outcome='Failed']")
    foreach ($failure in $failures) {
        $name = [string]$failure.GetAttribute('testName')
        $messageNode = $failure.SelectSingleNode("./*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']")
        $stackNode = $failure.SelectSingleNode("./*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='StackTrace']")
        $message = if ($messageNode) { $messageNode.InnerText.Trim() } else { 'The test failed without an error message.' }
        $stack = if ($stackNode) { $stackNode.InnerText.Trim() } else { '' }
        $detail = if ($stack) { "$message`n$stack" } else { $message }
        if ($detail.Length -gt 6000) { $detail = $detail.Substring(0, 6000) }
        Write-Host "::error title=$(ConvertTo-WorkflowProperty "Test failed: $name")::$(ConvertTo-WorkflowData $detail)"
        $reported++
    }
}

if ($testExitCode -ne 0 -and $reported -eq 0) {
    Write-Host "::error title=Test process failed::dotnet test exited with code $testExitCode but produced no failed UnitTestResult in its TRX output."
}

exit $testExitCode
