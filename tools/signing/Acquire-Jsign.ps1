<#
.SYNOPSIS
    Acquires the pinned Jsign release (tools/signing/jsign.json) into a new tool
    directory and finds a Java runtime to run it.

.DESCRIPTION
    Jsign signs Windows PE files in place, with a local PKCS#12 key (rehearsals)
    or SSL.com eSigner's cloud key, to which it sends only digests. The jar is
    checked against its pinned size and SHA-256 before anything runs it.
    JarFile supplies an offline copy of the same pinned jar. Returns the verified
    jar and Java.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ToolRoot,

    [string]$JarFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not ([IO.Path]::IsPathRooted($ToolRoot) -and [IO.Path]::GetPathRoot($ToolRoot).Length -ge 3)) { throw 'ToolRoot must be an absolute path.' }
$ToolRoot = [IO.Path]::GetFullPath($ToolRoot)
if (Test-Path -LiteralPath $ToolRoot) { throw "ToolRoot already exists: $ToolRoot" }
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'jsign.json') -Raw | ConvertFrom-Json
[void][IO.Directory]::CreateDirectory($ToolRoot)
$jar = Join-Path $ToolRoot "jsign-$($pin.Version).jar"
if ($JarFile) { Copy-Item -LiteralPath $JarFile -Destination $jar } else { Invoke-WebRequest -Uri $pin.JarURL -OutFile $jar }
if ((Get-Item -LiteralPath $jar).Length -ne $pin.JarBytes -or
    (Get-FileHash -LiteralPath $jar -Algorithm SHA256).Hash.ToLowerInvariant() -cne $pin.JarSHA256) {
    Remove-Item -LiteralPath $jar -Force
    throw 'The Jsign jar differs from the pinned release.'
}

# An explicit VISUALCAT_JAVA wins; then the JDKs GitHub's Windows images publish; then JAVA_HOME; then PATH.
$candidates = [System.Collections.Generic.List[string]]::new()
if ($env:VISUALCAT_JAVA) { $candidates.Add($env:VISUALCAT_JAVA) }
$javaName = if ($IsWindows) { 'java.exe' } else { 'java' }
foreach ($javaHome in @($env:JAVA_HOME_21_X64, $env:JAVA_HOME_17_X64, $env:JAVA_HOME)) {
    if ($javaHome) { $candidates.Add((Join-Path $javaHome "bin/$javaName")) }
}
$onPath = Get-Command java -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($onPath) { $candidates.Add($onPath.Source) }
$java = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $java) { throw 'No Java runtime found for Jsign (set VISUALCAT_JAVA, JAVA_HOME or PATH).' }
# `java -version` writes to stderr, which a native command with $ErrorActionPreference
# 'Stop' would turn into a terminating error in older PowerShell; collect it as text.
$versionOutput = @(& $java -version 2>&1 | ForEach-Object { "$_" })
if ($LASTEXITCODE -ne 0) { throw "Java at $java does not run." }
$javaVersion = "$($versionOutput[0])".Trim()

[pscustomobject]@{
    Jar = $jar
    JarSHA256 = $pin.JarSHA256
    Version = $pin.Version
    Java = [IO.Path]::GetFullPath($java)
    JavaVersion = $javaVersion
}
