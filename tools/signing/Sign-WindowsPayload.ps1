<#
.SYNOPSIS
    Signs every unsigned PE file of VisualCat's Windows payload folders with the
    configured signing identity (Invoke-Jsign.ps1).

.DESCRIPTION
    Before anything is signed it writes unsigned-inputs.json: every file of every
    folder with its size and SHA-256, and for each PE file whether it is unsigned
    or carries a vendor signature Windows trusts. Vendor-signed files stay
    byte-for-byte unchanged. A PE file whose existing signature Windows rejects
    stops the run, so nothing is ever signed over a broken signature.

    Unsigned PE files with identical bytes (the desktop and CLI payloads share
    VisualCat's core libraries) are signed once and the signed bytes copied to the
    other places, so each distinct file costs one signing. signed-outputs.json then
    records every file's input and output SHA-256 and the signer that was used.

    Third-party files that arrive unsigned (Avalonia's, among others) are signed
    too: Smart App Control checks each binary as it loads and blocks an unknown
    unsigned one.

    When VISUALCAT_EXPECTED_SIGNER_SHA256 pins the signer, one canary file is
    signed and checked before the rest, so a credential that resolves to another
    certificate costs a single signature.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$Path,

    [Parameter(Mandatory)]
    [string]$ManifestDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'Import-AuthenticodeInspector.ps1')
$A = [VisualCat.Signing.Authenticode]
$ManifestDirectory = [IO.Path]::GetFullPath($ManifestDirectory)
if (Test-Path -LiteralPath (Join-Path $ManifestDirectory 'unsigned-inputs.json')) { throw "Signing manifests already exist in $ManifestDirectory." }
[void][IO.Directory]::CreateDirectory($ManifestDirectory)

$roots = [ordered]@{}
foreach ($item in $Path) {
    $full = (Resolve-Path -LiteralPath $item).Path
    if (-not (Test-Path -LiteralPath $full -PathType Container)) { throw "Not a payload folder: $item" }
    $label = Split-Path -Leaf $full
    if ($roots.Contains($label)) { throw "Two payload folders are both named $label." }
    $roots[$label] = $full
}

$files = [System.Collections.Generic.List[object]]::new()
foreach ($label in $roots.Keys) {
    foreach ($file in Get-ChildItem -LiteralPath $roots[$label] -Recurse -File | Sort-Object FullName) {
        $facts = $A::Inspect($file.FullName, $false)
        $classification = if (-not $facts.IsPe) { 'NotPE' }
            elseif ($facts.Error) { throw "Cannot read the PE signature data of $($file.FullName): $($facts.Error)" }
            elseif ($facts.SignatureBlobs -eq 0) { 'Unsigned' }
            elseif ($facts.TrustResult -eq 0) { 'Vendor' }
            else { throw ('{0} carries a signature Windows rejects (0x{1:X8}); refusing to sign over it.' -f $file.FullName, $facts.TrustResult) }
        $files.Add([pscustomobject]@{
            Root = $label
            Path = [IO.Path]::GetRelativePath($roots[$label], $file.FullName).Replace('\', '/')
            FullName = $file.FullName
            Bytes = $file.Length
            InputSHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            Classification = $classification
            VendorSigner = if ($classification -eq 'Vendor') { $facts.Signer.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) } else { $null }
        })
    }
}

function Write-Manifest([string]$Name, [object[]]$Entries, [hashtable]$Extra = @{}) {
    $manifest = [ordered]@{
        Schema = 'visualcat-signing-manifest/1'
        UTC = [DateTime]::UtcNow.ToString('o')
        Mode = $env:VISUALCAT_SIGN_MODE
        Roots = @($roots.Keys)
    }
    foreach ($key in $Extra.Keys) { $manifest[$key] = $Extra[$key] }
    $manifest.Files = @($Entries)
    [IO.File]::WriteAllText((Join-Path $ManifestDirectory $Name), ($manifest | ConvertTo-Json -Depth 5))
}
Write-Manifest 'unsigned-inputs.json' @($files | Select-Object Root, Path, Bytes, InputSHA256, Classification, VendorSigner)

# One signing per distinct unsigned file; the first place it occurs is the one signed.
$groups = @($files | Where-Object Classification -eq 'Unsigned' | Group-Object InputSHA256)
$representatives = @($groups | ForEach-Object { $_.Group[0].FullName })
$expected = "$env:VISUALCAT_EXPECTED_SIGNER_SHA256".ToUpperInvariant()
if ($representatives.Count -and $expected) {
    # With a pinned signer, one canary signature comes first: a credential that resolves to another certificate costs
    # one signing and stops the run before anything else is signed.
    & (Join-Path $PSScriptRoot 'Invoke-Jsign.ps1') $representatives[0]
    $canary = $A::Inspect($representatives[0], $false)
    if (-not $canary.Signer -or $canary.Signer.GetCertHashString([System.Security.Cryptography.HashAlgorithmName]::SHA256) -cne $expected) {
        throw 'The canary signature is not by the pinned signer; nothing else was signed.'
    }
    if ($representatives.Count -gt 1) { & (Join-Path $PSScriptRoot 'Invoke-Jsign.ps1') @($representatives | Select-Object -Skip 1) }
} elseif ($representatives.Count) {
    & (Join-Path $PSScriptRoot 'Invoke-Jsign.ps1') @representatives
}

$signers = @{}
foreach ($group in $groups) {
    $signed = $group.Group[0]
    $facts = $A::Inspect($signed.FullName, $false)
    if ($facts.SignatureBlobs -ne 1 -or -not $facts.Signer) { throw "$($signed.FullName) has no signature after signing." }
    $signers[$facts.Signer.GetCertHashString([System.Security.Cryptography.HashAlgorithmName]::SHA256)] = $facts.Signer.Subject
    foreach ($copy in $group.Group | Select-Object -Skip 1) {
        Copy-Item -LiteralPath $signed.FullName -Destination $copy.FullName -Force
        $copy | Add-Member -NotePropertyName SignedFrom -NotePropertyValue "$($signed.Root)/$($signed.Path)"
    }
}
if ($signers.Count -gt 1) { throw "The payload was signed by more than one certificate: $($signers.Keys -join ', ')" }
foreach ($file in $files) {
    $file | Add-Member -NotePropertyName OutputSHA256 -NotePropertyValue (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    if (-not $file.PSObject.Properties['SignedFrom']) { $file | Add-Member -NotePropertyName SignedFrom -NotePropertyValue $null }
    if ($file.Classification -ne 'Unsigned' -and $file.OutputSHA256 -cne $file.InputSHA256) { throw "$($file.FullName) changed although it was not signed." }
}
$signer = @($signers.Keys)
Write-Manifest 'signed-outputs.json' @($files | Select-Object Root, Path, Bytes, InputSHA256, OutputSHA256, Classification, VendorSigner, SignedFrom) @{
    Signer = if ($signer.Count) { [ordered]@{ SHA256 = $signer[0]; Subject = $signers[$signer[0]] } } else { $null }
    DistinctFilesSigned = $groups.Count
}
[pscustomobject]@{
    FilesSigned = @($files | Where-Object Classification -eq 'Unsigned').Count
    DistinctFilesSigned = $groups.Count
    VendorSigned = @($files | Where-Object Classification -eq 'Vendor').Count
    SignerSHA256 = if ($signer.Count) { $signer[0] } else { $null }
    Manifests = @((Join-Path $ManifestDirectory 'unsigned-inputs.json'), (Join-Path $ManifestDirectory 'signed-outputs.json'))
}
