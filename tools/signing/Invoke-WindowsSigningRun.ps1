<#
.SYNOPSIS
    One phase of signing VisualCat's Windows release packages. Each workflow step
    runs one phase, so each step gets only what it needs.

.DESCRIPTION
    The release workflow's win-x64 desktop job publishes the desktop and CLI
    payloads, stages their notices and runs Record. It never sees a credential.
    A separate signing job, on another machine, downloads that payload and runs
    the other phases, which share state through WorkDirectory/run.json. Signing
    credentials reach only the Sign phase, which runs no build code.

      Record   (desktop job) writes payload-manifest.json beside the desktop and
               cli payload folders and reports its SHA-256 as a step output
      Prepare  checks the downloaded payload against that SHA-256, the manifest
               and this checkout's commit, acquires the pinned Jsign and, for
               TestCertificate, makes the throwaway test signer
      Sign     Sign-WindowsPayload.ps1 on both payload folders: each distinct
               unsigned PE file is signed once and vendor-signed files stay
               byte-for-byte; with a pinned signer one canary file comes first
      Package  archives both payloads exactly as every runtime is archived
               (tools/archive-release-packages.ps1), then checks each archive with
               tools/verify-package-contents.ps1, which runs vcat from it
      Verify   Test-WindowsSignatures.ps1 on the archives, with negative controls

    Modes:
      TestCertificate  a throwaway certificate made in Prepare; no secrets.
                       Release rehearsals use it, so every phase runs before a tag.
      ESigner          the release certificate, held in SSL.com eSigner and pinned
                       by VISUALCAT_EXPECTED_SIGNER_SHA256. Tagged releases use it,
                       in the protected `signing` environment. Verification then
                       checks revocation and prints nothing that names the signer.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Record', 'Prepare', 'Sign', 'Package', 'Verify')]
    [string]$Phase,

    # Record: the folder holding the published desktop and cli folders. Every other
    # phase: the signing job's work directory, into which that folder was downloaded.
    [Parameter(Mandatory)]
    [string]$WorkDirectory,

    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',

    # Record: the release version.
    [string]$Version,

    # Prepare: the signing mode, and the payload-manifest SHA-256 that Record reported.
    [ValidateSet('TestCertificate', 'ESigner')]
    [string]$Mode,

    [ValidatePattern('^$|^[0-9A-Fa-f]{64}$')]
    [string]$PayloadManifestSha256
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not ([IO.Path]::IsPathRooted($WorkDirectory) -and [IO.Path]::GetPathRoot($WorkDirectory).Length -ge 3)) {
    $WorkDirectory = Join-Path $PWD.Path $WorkDirectory
}
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
$payloads = @((Join-Path $WorkDirectory 'desktop'), (Join-Path $WorkDirectory 'cli'))
$manifestFile = Join-Path $WorkDirectory 'payload-manifest.json'
$stateFile = Join-Path $WorkDirectory 'run.json'
$signingDirectory = Join-Path $WorkDirectory 'signing'
$packageDirectory = Join-Path $WorkDirectory 'packages'

function Read-State { Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json }
function Save-State($State) { [IO.File]::WriteAllText($stateFile, ($State | ConvertTo-Json -Depth 6)) }
function Get-Sha256([string]$File) { (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash }
function Get-SourceCommit {
    $commit = "$(git -C $repository rev-parse HEAD)".Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Could not read the checkout''s commit.' }
    $commit
}
function Get-PayloadInventory {
    foreach ($root in $payloads) {
        $label = Split-Path -Leaf $root
        foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName) {
            [pscustomobject]@{
                Root = $label
                Path = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
                Bytes = $file.Length
                SHA256 = Get-Sha256 $file.FullName
            }
        }
    }
}
# The signing identity reaches Invoke-Jsign.ps1 through the environment, never a command line.
function Set-SigningEnvironment($State) {
    $env:VISUALCAT_JSIGN_JAR = $State.Jsign.Jar
    $env:VISUALCAT_JAVA = $State.Jsign.Java
    $env:VISUALCAT_SIGN_MODE = $State.Mode
    if ($State.Mode -eq 'TestCertificate') {
        $env:VISUALCAT_SIGN_PFX = $State.TestCertificate.Pfx
        if (-not $env:VISUALCAT_SIGN_PFX_PASSWORD) {
            $local = $State.TestCertificate.PSObject.Properties['LocalPfxPassword']
            if (-not $local) { throw 'VISUALCAT_SIGN_PFX_PASSWORD is not set; Prepare exports it to later steps of the same job.' }
            $env:VISUALCAT_SIGN_PFX_PASSWORD = $local.Value
        }
        $env:VISUALCAT_EXPECTED_SIGNER_SHA256 = $State.ExpectedSignerSHA256
    } elseif ("$env:VISUALCAT_EXPECTED_SIGNER_SHA256" -notmatch '^[0-9A-Fa-f]{64}$') {
        throw 'VISUALCAT_EXPECTED_SIGNER_SHA256 must pin the release certificate (its SHA-256) in ESigner mode.'
    }
}

switch ($Phase) {
    'Record' {
        if (-not $Version) { throw 'Record needs -Version.' }
        foreach ($payload in $payloads) {
            if (-not (Test-Path -LiteralPath $payload -PathType Container)) { throw "Record expects the published payload folder $payload." }
        }
        if (Test-Path -LiteralPath $manifestFile) { throw "A payload manifest already exists: $manifestFile" }
        $manifest = [ordered]@{
            Schema = 'visualcat-payload-manifest/1'
            SourceCommit = Get-SourceCommit
            Version = $Version
            Runtime = $Runtime
            Files = @(Get-PayloadInventory)
        }
        [IO.File]::WriteAllText($manifestFile, ($manifest | ConvertTo-Json -Depth 4))
        $sha = Get-Sha256 $manifestFile
        Write-Host "Unsigned $Runtime payload: $($manifest.Files.Count) files; payload-manifest.json SHA-256 $sha"
        if ($env:GITHUB_OUTPUT) { Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "payload-manifest-sha256=$sha" }
    }
    'Prepare' {
        if (-not $Mode -or -not $PayloadManifestSha256) { throw 'Prepare needs -Mode and -PayloadManifestSha256.' }
        if (Test-Path -LiteralPath $stateFile) { throw "This work directory was already prepared: $stateFile" }
        # The payload must be exactly what Record saw: the manifest by the hash Record reported, every file by the manifest.
        if ((Get-Sha256 $manifestFile) -ne $PayloadManifestSha256.ToUpperInvariant()) { throw 'payload-manifest.json differs from the one the desktop job reported.' }
        $manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
        if ($manifest.Runtime -cne $Runtime) { throw "The payload is for $($manifest.Runtime), not $Runtime." }
        $expected = @($manifest.Files | ForEach-Object { "$($_.Root)/$($_.Path)|$($_.Bytes)|$($_.SHA256)" })
        $actual = @(Get-PayloadInventory | ForEach-Object { "$($_.Root)/$($_.Path)|$($_.Bytes)|$($_.SHA256)" })
        $difference = @(Compare-Object -ReferenceObject $expected -DifferenceObject $actual -CaseSensitive)
        if ($difference.Count) { throw "The downloaded payload differs from its manifest in $($difference.Count) entries." }
        $sourceCommit = Get-SourceCommit
        if ($manifest.SourceCommit -cne $sourceCommit) { throw "The payload was built from $($manifest.SourceCommit), not this checkout ($sourceCommit)." }
        Write-Host "Payload verified: $($expected.Count) files match the desktop job's manifest."

        $jsign = & (Join-Path $PSScriptRoot 'Acquire-Jsign.ps1') -ToolRoot (Join-Path $WorkDirectory 'tools/jsign')
        $state = [ordered]@{
            Schema = 'visualcat-signing-run/1'
            Mode = $Mode
            Runtime = $Runtime
            Version = $manifest.Version
            SourceCommit = $sourceCommit
            PayloadManifestSHA256 = $PayloadManifestSha256.ToUpperInvariant()
            Jsign = [ordered]@{ Jar = $jsign.Jar; Java = $jsign.Java; Version = $jsign.Version; JarSHA256 = $jsign.JarSHA256; JavaVersion = $jsign.JavaVersion }
            TestCertificate = $null
            ExpectedSignerSHA256 = $null
            Signed = $false
            Packages = @()
        }
        if ($Mode -eq 'TestCertificate') {
            $certificate = & (Join-Path $PSScriptRoot 'New-TestCodeSigningCertificate.ps1') -OutputDirectory (Join-Path $WorkDirectory 'test-ca')
            $state.TestCertificate = [ordered]@{ Pfx = $certificate.Pfx; RootCertificate = $certificate.RootCertificate; SignerSHA256 = $certificate.SignerSha256 }
            if ($env:GITHUB_ENV) {
                # Later steps of this job get the throwaway key's password; it never reaches a file.
                Write-Host "::add-mask::$($certificate.PfxPassword)"
                Add-Content -LiteralPath $env:GITHUB_ENV -Value "VISUALCAT_SIGN_PFX_PASSWORD=$($certificate.PfxPassword)"
            } else {
                $state.TestCertificate.LocalPfxPassword = $certificate.PfxPassword
            }
            $state.ExpectedSignerSHA256 = $certificate.SignerSha256
            Write-Host "Test signer SHA-256 $($certificate.SignerSha256); test root SHA-256 $($certificate.RootSha256)"
        }
        Save-State $state
        Write-Host "Prepared $Mode signing of $($manifest.Version) at $sourceCommit with Jsign $($jsign.Version) on $($jsign.JavaVersion)."
    }
    'Sign' {
        $state = Read-State
        if ($state.Signed) { throw 'This payload was already signed.' }
        Set-SigningEnvironment $state
        if ($state.Mode -eq 'ESigner') { $state.ExpectedSignerSHA256 = $env:VISUALCAT_EXPECTED_SIGNER_SHA256.ToUpperInvariant() }
        $result = & (Join-Path $PSScriptRoot 'Sign-WindowsPayload.ps1') -Path $payloads -ManifestDirectory $signingDirectory
        if ($result.SignerSHA256 -cne $state.ExpectedSignerSHA256) { throw 'The payload was signed by a certificate other than the pinned signer.' }
        $state.Signed = $true
        Save-State $state
        Write-Host "Signed $($result.DistinctFilesSigned) distinct files into $($result.FilesSigned) places; $($result.VendorSigned) vendor-signed files unchanged."
        if ($env:GITHUB_STEP_SUMMARY) {
            Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value "Signed $($result.DistinctFilesSigned) distinct files ($($state.Mode)) into $($result.FilesSigned) places; $($result.VendorSigned) vendor-signed files unchanged."
        }
    }
    'Package' {
        $state = Read-State
        if (-not $state.Signed) { throw 'Package runs after Sign: nothing unsigned is archived for release.' }
        $archives = @(& (Join-Path $repository 'tools/archive-release-packages.ps1') -PublishRoot $WorkDirectory -Runtime $Runtime -Archive zip `
                -Version $state.Version -Destination $packageDirectory)
        foreach ($component in 'Desktop', 'Cli') {
            $archive = Join-Path $packageDirectory "VisualCat-$(if ($component -eq 'Desktop') { 'Desktop' } else { 'CLI' })-$Runtime-v$($state.Version).zip"
            if ($archive -notin $archives) { throw "The archive step did not produce $archive." }
            & (Join-Path $repository 'tools/verify-package-contents.ps1') -Archive $archive -Component $component -Version $state.Version -Runtime $Runtime
        }
        $state.Packages = @($archives)
        Save-State $state
    }
    'Verify' {
        $state = Read-State
        if (-not @($state.Packages).Count) { throw 'Verify runs after Package.' }
        $verify = @{
            Path = @($state.Packages)
            ExpectedSignerSha256 = $state.ExpectedSignerSHA256
            SigningManifest = @(Join-Path $signingDirectory 'signed-outputs.json')
            ReportPath = Join-Path $WorkDirectory 'verification.json'
        }
        if ($state.Mode -eq 'TestCertificate') {
            $verify.TrustedRootFile = $state.TestCertificate.RootCertificate
        } else {
            $verify.CheckRevocation = $true
            $verify.PrivateIdentity = $true
        }
        & (Join-Path $PSScriptRoot 'Test-WindowsSignatures.ps1') @verify
    }
}
