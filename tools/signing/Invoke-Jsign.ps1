<#
.SYNOPSIS
    Signs Windows PE files in place with the configured signing identity,
    through the pinned Jsign.

.DESCRIPTION
    Used by Sign-WindowsPayload.ps1. Every signature is SHA-256 with an RFC 3161
    timestamp. Configuration comes from the environment, so no secret appears on
    a command line or in a log:

      VISUALCAT_SIGN_MODE             TestCertificate | ESigner
      VISUALCAT_JSIGN_JAR, VISUALCAT_JAVA
                                      the jar and Java from Acquire-Jsign.ps1
                                      (the jar is re-checked against its pin)
      TestCertificate                 VISUALCAT_SIGN_PFX and VISUALCAT_SIGN_PFX_PASSWORD
                                      (New-TestCodeSigningCertificate.ps1)
      ESigner                         VISUALCAT_ESIGNER_USERNAME, VISUALCAT_ESIGNER_PASSWORD,
                                      VISUALCAT_ESIGNER_CREDENTIAL_ID and
                                      VISUALCAT_ESIGNER_TOTP_SECRET (the base64 secret
                                      shown with the eSigner QR code, not the PIN)
      VISUALCAT_SIGN_TSA              optional comma-separated RFC 3161 servers
                                      (default SSL.com, then DigiCert)
      VISUALCAT_JSIGN_RETRY_DELAY_SECONDS
                                      optional base delay between attempts (default 30)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, ValueFromRemainingArguments)]
    [string[]]$File
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'jsign.json') -Raw | ConvertFrom-Json
$jar = $env:VISUALCAT_JSIGN_JAR
$java = $env:VISUALCAT_JAVA
if (-not $jar -or -not $java) { throw 'VISUALCAT_JSIGN_JAR and VISUALCAT_JAVA must name the jar and Java from Acquire-Jsign.ps1.' }
if ((Get-FileHash -LiteralPath $jar -Algorithm SHA256).Hash.ToLowerInvariant() -cne $pin.JarSHA256) { throw 'The Jsign jar differs from its pin.' }

function Get-Required([string]$Name) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrEmpty($value)) { throw "$Name is not set for VISUALCAT_SIGN_MODE=$env:VISUALCAT_SIGN_MODE." }
    $value
}

$arguments = [System.Collections.Generic.List[string]]::new()
$arguments.AddRange([string[]]@('-jar', $jar))
switch -CaseSensitive ($env:VISUALCAT_SIGN_MODE) {
    'TestCertificate' {
        [void](Get-Required 'VISUALCAT_SIGN_PFX_PASSWORD')
        $arguments.AddRange([string[]]@('--storetype', 'PKCS12', '--keystore', (Get-Required 'VISUALCAT_SIGN_PFX'), '--storepass', 'env:VISUALCAT_SIGN_PFX_PASSWORD'))
    }
    'ESigner' {
        # Jsign takes the SSL.com username and password as one keystore password, "<username>|<password>". It lives
        # only in this process's environment, which the Java child inherits; the TOTP secret lets Jsign compute each
        # one-time password itself.
        $env:VISUALCAT_JSIGN_STOREPASS = (Get-Required 'VISUALCAT_ESIGNER_USERNAME') + '|' + (Get-Required 'VISUALCAT_ESIGNER_PASSWORD')
        [void](Get-Required 'VISUALCAT_ESIGNER_TOTP_SECRET')
        $arguments.AddRange([string[]]@('--storetype', 'ESIGNER', '--storepass', 'env:VISUALCAT_JSIGN_STOREPASS', '--keypass', 'env:VISUALCAT_ESIGNER_TOTP_SECRET',
            '--alias', (Get-Required 'VISUALCAT_ESIGNER_CREDENTIAL_ID')))
    }
    default { throw "VISUALCAT_SIGN_MODE must be TestCertificate or ESigner, not '$env:VISUALCAT_SIGN_MODE'." }
}
$tsa = if ($env:VISUALCAT_SIGN_TSA) { $env:VISUALCAT_SIGN_TSA } else { 'http://ts.ssl.com,http://timestamp.digicert.com' }
$arguments.AddRange([string[]]@('--alg', 'SHA-256', '--tsaurl', $tsa, '--tsmode', 'RFC3161', '--tsretries', '3', '--tsretrywait', '10',
        '--name', 'VisualCat', '--url', 'https://github.com/benny-cz/VisualCat', '--replace'))

# Jsign recognises PE files by extension. VisualCat ships no other kind of PE file, so anything else is a mistake in
# the caller rather than something to sign under a borrowed name.
$list = [System.Collections.Generic.List[string]]::new()
foreach ($item in $File) {
    $full = (Resolve-Path -LiteralPath $item.Trim('"')).Path
    if ([IO.Path]::GetExtension($full) -notin '.exe', '.dll') { throw "Only .exe and .dll files are signed: $full" }
    $list.Add($full)
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) ('VisualCat-jsign-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
try {
    $listFile = Join-Path $scratch 'files.txt'
    $arguments.Add("@$listFile")
    # A signing service can fail transiently: eSigner has answered 504 on its pre-signing hash scan. Jsign signs files
    # in place one at a time and retries only timestamps, so each retry here sends only the files still unsigned and
    # no file costs a second signing.
    $attempts = 4
    $delay = if ($env:VISUALCAT_JSIGN_RETRY_DELAY_SECONDS) { [int]$env:VISUALCAT_JSIGN_RETRY_DELAY_SECONDS } else { 30 }
    $pending = @($list)
    for ($attempt = 1; ; $attempt++) {
        [IO.File]::WriteAllLines($listFile, [string[]]$pending)
        # Jsign's per-file lines go to the log, not into the output of whichever script called this one.
        & $java @arguments | Out-Host
        $exitCode = $LASTEXITCODE
        if ($exitCode -eq 0) { break }
        $pending = @($pending | Where-Object { -not (Get-AuthenticodeSignature -LiteralPath $_).SignerCertificate })
        if (-not $pending.Count) { throw "Jsign failed with exit code $exitCode although every file now carries a signature; refusing to trust them." }
        if ($attempt -ge $attempts) { throw "Jsign failed with exit code $exitCode; $($pending.Count) files are still unsigned after $attempts attempts." }
        Write-Host "Jsign failed with exit code $exitCode; retrying the $($pending.Count) unsigned files in $($delay * $attempt) seconds (attempt $($attempt + 1) of $attempts)."
        Start-Sleep -Seconds ($delay * $attempt)
    }
} finally {
    Remove-Item Env:\VISUALCAT_JSIGN_STOREPASS -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}
