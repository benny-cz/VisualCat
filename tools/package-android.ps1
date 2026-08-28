<#
.SYNOPSIS
    Builds and verifies the signed Android artifacts VisualCat publishes: the
    Google Play App Bundle and the directly installable APK.

.DESCRIPTION
    Google Play rejects an upload after the fact and with little detail, so this
    script proves the properties Play checks before anything is uploaded:

      * application ID, versionCode, versionName, minSdkVersion, and
        targetSdkVersion actually baked into the package;
      * a real release signature and the certificate fingerprint that Play will
        pin the app to forever;
      * 64-bit native code; and
      * 16 KB memory-page alignment of every shipped ELF, which Play has
        required of packages targeting Android 15 and later since
        1 November 2025.

    The App Bundle is what Google Play consumes. The APK is the same build in
    the form a person can side-load from the GitHub release, and it is what
    aapt2 and apksigner can inspect directly, so building both also verifies
    the bundle's inputs.

.PARAMETER Format
    Which packages to produce: 'aab' for Google Play, 'apk' for direct
    installation, or 'both' (default).

.PARAMETER Keystore
    Path to the signing keystore. Defaults to $env:ANDROID_KEYSTORE_PATH.

.PARAMETER KeyAlias
    Key alias inside the keystore. Defaults to $env:ANDROID_KEY_ALIAS.

.PARAMETER StorePassword
    Keystore password. Defaults to $env:ANDROID_KEYSTORE_PASSWORD.

.PARAMETER KeyPassword
    Key password. Defaults to $env:ANDROID_KEY_PASSWORD, then to StorePassword,
    which is how PKCS12 keystores are normally created.

.PARAMETER Version
    Release version (versionName). Defaults to the VersionPrefix declared in
    Directory.Build.props.

.PARAMETER VersionCode
    Play's ordering integer. Defaults to the value the project derives from the
    release version and VisualCatBuildNumber:

        major * 1000000 + minor * 10000 + patch * 100 + build

    so 2.0.10 -> 2001000 and 2.1.0 built with -VisualCatBuildNumber 3 -> 2010003.
    Play refuses a code it has already seen on any track, so a second build of the
    same version needs the build counter bumped rather than this parameter set;
    overriding here is for re-uploading an unchanged version.

.PARAMETER VisualCatBuildNumber
    The 0-99 counter reserved for rebuilding the same three-part release version.
    Defaults to VisualCatBuildNumber in Directory.Build.props, then zero. This is
    passed explicitly to MSBuild so a tag whose prerelease suffix differs from the
    checked-in version still receives the intended, reproducible versionCode.

.PARAMETER Output
    Directory that receives the final named artifacts. Defaults to
    artifacts/android.

.PARAMETER SkipBuild
    Verify artifacts already present in the output directory instead of
    rebuilding them. Signing values are still required, because the keystore's
    certificate is what the packages are checked against.

.EXAMPLE
    pwsh ./tools/package-android.ps1 `
        -Keystore ~/.visualcat-signing/visualcat-upload.keystore `
        -KeyAlias visualcat-upload -StorePassword '...'
#>
[CmdletBinding()]
param(
    [ValidateSet('aab', 'apk', 'both')]
    [string]$Format = 'both',
    [string]$Keystore = $env:ANDROID_KEYSTORE_PATH,
    [string]$KeyAlias = $env:ANDROID_KEY_ALIAS,
    [string]$StorePassword = $env:ANDROID_KEYSTORE_PASSWORD,
    [string]$KeyPassword = $env:ANDROID_KEY_PASSWORD,
    [string]$Version,
    [string]$VersionCode,
    [int]$VisualCatBuildNumber = -1,
    [string]$Output = 'artifacts/android',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repository 'src/VisualCat.Android/VisualCat.Android.csproj'
$expectedApplicationId = 'com.barebit.visualcat'
# Google Play requires new apps and updates to target this level from
# 31 August 2026. The project pins it; this is the independent assertion.
$requiredTargetSdk = 36
$requiredPageAlignment = 16384
$requiredReleasePermissions = @(
    'android.permission.INTERNET',
    'android.permission.CHANGE_WIFI_MULTICAST_STATE',
    'android.permission.FOREGROUND_SERVICE',
    'android.permission.FOREGROUND_SERVICE_DATA_SYNC',
    'android.permission.POST_NOTIFICATIONS'
)
$forbiddenReleasePermissions = @(
    'android.permission.READ_LOGS',
    'android.permission.READ_PHONE_STATE',
    'android.permission.READ_EXTERNAL_STORAGE',
    'android.permission.WRITE_EXTERNAL_STORAGE'
)
# Public fingerprint of VisualCat's Google Play upload certificate. A valid
# signature from any other keystore is still an invalid Play upload.
$requiredUploadCertificateSha256 = 'a715b0309589aa83dd21548d1959af4bb97b8df06d97fdae32715fbd6530e184'

# --- inputs --------------------------------------------------------------

$props = Get-Content -Raw -LiteralPath (Join-Path $repository 'Directory.Build.props')
if (-not $Version) {
    if ($props -notmatch '<VersionPrefix>\s*(?<version>[^<]+?)\s*</VersionPrefix>') {
        throw 'Directory.Build.props does not declare <VersionPrefix>, so -Version is required.'
    }
    $Version = $Matches['version']
}

$semanticVersionPattern = '^(?<prefix>(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*))(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'
if ($Version -notmatch $semanticVersionPattern) {
    throw "Version '$Version' is not a three-part semantic version with an optional prerelease/build suffix."
}
$versionPrefix = $Matches['prefix']
$versionMajor = [int64]$Matches['major']
$versionMinor = [int64]$Matches['minor']
$versionPatch = [int64]$Matches['patch']

if ($VisualCatBuildNumber -eq -1) {
    $VisualCatBuildNumber = if ($props -match '<VisualCatBuildNumber>\s*(?<build>\d+)\s*</VisualCatBuildNumber>') {
        [int]$Matches['build']
    } else {
        0
    }
}
if ($VisualCatBuildNumber -lt 0 -or $VisualCatBuildNumber -gt 99) {
    throw "VisualCatBuildNumber is '$VisualCatBuildNumber'; the Android version-code scheme allows 0-99."
}
if ($versionMinor -gt 99 -or $versionPatch -gt 99) {
    throw "Version '$Version' cannot use the Android version-code scheme; minor and patch must each be 0-99."
}

$derivedVersionCode = ($versionMajor * 1000000) + ($versionMinor * 10000) + ($versionPatch * 100) + $VisualCatBuildNumber
$expectedVersionCode = if ($VersionCode) {
    if ($VersionCode -notmatch '^[1-9]\d*$') { throw "VersionCode '$VersionCode' is not a positive integer." }
    [int64]$VersionCode
} else {
    $derivedVersionCode
}
if ($expectedVersionCode -gt 2100000000) {
    throw "Android versionCode $expectedVersionCode exceeds Google Play's 2100000000 ceiling."
}

if (-not $KeyPassword) { $KeyPassword = $StorePassword }

$missing = @(
    if (-not $Keystore) { 'Keystore (ANDROID_KEYSTORE_PATH)' }
    if (-not $KeyAlias) { 'KeyAlias (ANDROID_KEY_ALIAS)' }
    if (-not $StorePassword) { 'StorePassword (ANDROID_KEYSTORE_PASSWORD)' }
)
if ($missing.Count -gt 0) {
    throw "Signing is required and these values are missing: $($missing -join ', '). See docs/RELEASE-CHECKLIST.md."
}

$Keystore = [IO.Path]::GetFullPath($Keystore)
if (-not (Test-Path -LiteralPath $Keystore -PathType Leaf)) {
    throw "The keystore '$Keystore' does not exist."
}

$outputRoot = [IO.Path]::GetFullPath((Join-Path $repository $Output))
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$formats = if ($Format -eq 'both') { @('aab', 'apk') } else { @($Format) }

# --- Android SDK tooling -------------------------------------------------

<#
    The SDK, build-tools, and JDK the build actually used are recorded by the
    Android targets in obj/. Reading them back is exact, where probing
    ANDROID_HOME guesses at a second installation that may not be the one that
    produced these bytes.
#>
function Get-AndroidBuildEnvironment {
    param([Parameter(Mandatory)][string]$IntermediatePath)

    $propsPath = Join-Path $IntermediatePath 'build.props'
    if (-not (Test-Path -LiteralPath $propsPath)) {
        throw "The Android build did not record '$propsPath'."
    }

    $values = @{}
    foreach ($line in Get-Content -LiteralPath $propsPath) {
        $separator = $line.IndexOf('=')
        if ($separator -gt 0) {
            $values[$line.Substring(0, $separator)] = $line.Substring($separator + 1)
        }
    }

    $sdk = $values['androidsdkpath']
    $buildTools = Join-Path $sdk "build-tools/$($values['androidsdkbuildtoolsversion'])"
    $jdk = $values['javasdkpath']
    $androidNetSdkVersion = $values['androidnetsdkversion']
    $extension = if ($IsWindows -or $env:OS -eq 'Windows_NT') { '.exe' } else { '' }
    $batch = if ($IsWindows -or $env:OS -eq 'Windows_NT') { '.bat' } else { '' }

    $tools = [ordered]@{
        Aapt2     = Join-Path $buildTools "aapt2$extension"
        ApkSigner = Join-Path $buildTools "apksigner$batch"
        JarSigner = Join-Path $jdk "bin/jarsigner$extension"
        Java       = Join-Path $jdk "bin/java$extension"
    }

    # bundletool is shipped by the exact .NET Android workload that produced the package. Use
    # that copy to decode an AAB's protobuf manifest instead of treating protobuf bytes as UTF-8.
    $dotnetRoot = Split-Path -Parent (Get-Command dotnet -ErrorAction Stop).Source
    $bundleTool = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'packs') -Filter 'bundletool.jar' -Recurse |
        Where-Object { $_.FullName -match [regex]::Escape("$androidNetSdkVersion") } |
        Select-Object -First 1
    if (-not $bundleTool) {
        throw "Could not find bundletool.jar for .NET Android workload $androidNetSdkVersion."
    }
    $tools.BundleTool = $bundleTool.FullName
    foreach ($tool in $tools.Keys) {
        if (-not (Test-Path -LiteralPath $tools[$tool])) {
            throw "Required tool '$tool' was not found at '$($tools[$tool])'."
        }
    }

    return [pscustomobject]$tools
}

function Invoke-Tool {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string[]]$Arguments,
        [string]$Description = 'tool'
    )

    $output = & $Path @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code ${LASTEXITCODE}:`n$output"
    }

    return $output
}

# --- 16 KB page alignment ------------------------------------------------

<#
    A 16 KB device can only load a shared object whose PT_LOAD segments are
    aligned to at least 16 KB. Parsing the ELF program headers directly keeps
    this check honest on a machine without the NDK, where llvm-readelf is
    absent and its absence would otherwise silently skip the check.
#>
function Get-ElfLoadAlignment {
    param([Parameter(Mandatory)][byte[]]$Bytes)

    if ($Bytes.Length -lt 64 -or
        $Bytes[0] -ne 0x7F -or $Bytes[1] -ne 0x45 -or $Bytes[2] -ne 0x4C -or $Bytes[3] -ne 0x46) {
        return $null
    }

    $is64Bit = $Bytes[4] -eq 2
    $isLittleEndian = $Bytes[5] -eq 1
    if (-not $is64Bit -or -not $isLittleEndian) {
        # 32-bit ABIs are not shipped, and a big-endian Android target does not
        # exist. Reporting rather than assuming keeps a surprise visible.
        return @{ Unsupported = "class=$($Bytes[4]) data=$($Bytes[5])" }
    }

    $programHeaderOffset = [BitConverter]::ToUInt64($Bytes, 32)
    $programHeaderSize = [BitConverter]::ToUInt16($Bytes, 54)
    $programHeaderCount = [BitConverter]::ToUInt16($Bytes, 56)

    $alignments = [Collections.Generic.List[uint64]]::new()
    for ($index = 0; $index -lt $programHeaderCount; $index++) {
        $entry = [int]$programHeaderOffset + ($index * $programHeaderSize)
        if ($entry + $programHeaderSize -gt $Bytes.Length) { break }
        $type = [BitConverter]::ToUInt32($Bytes, $entry)
        if ($type -eq 1) {
            # PT_LOAD
            $alignments.Add([BitConverter]::ToUInt64($Bytes, $entry + 48))
        }
    }

    if ($alignments.Count -eq 0) { return $null }
    return @{ Minimum = ($alignments | Measure-Object -Minimum).Minimum }
}

function Test-PackagePageAlignment {
    param(
        [Parameter(Mandatory)][string]$Package,
        [Parameter(Mandatory)][string]$LibraryPrefix
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        $entries = @($archive.Entries | Where-Object {
                $_.FullName.StartsWith($LibraryPrefix, [StringComparison]::Ordinal) -and
                $_.FullName.EndsWith('.so', [StringComparison]::Ordinal)
            })
        if ($entries.Count -eq 0) {
            throw "$([IO.Path]::GetFileName($Package)) contains no native libraries under '$LibraryPrefix'."
        }

        $misaligned = [Collections.Generic.List[string]]::new()
        $unreadable = [Collections.Generic.List[string]]::new()
        foreach ($entry in $entries) {
            # Only the ELF header and program headers are needed, but entries are
            # deflated, so the prefix has to be inflated rather than seeked to.
            $stream = $entry.Open()
            try {
                $buffer = [byte[]]::new(64 * 1024)
                $read = 0
                while ($read -lt $buffer.Length) {
                    $chunk = $stream.Read($buffer, $read, $buffer.Length - $read)
                    if ($chunk -le 0) { break }
                    $read += $chunk
                }
                if ($read -lt $buffer.Length) { $buffer = $buffer[0..([Math]::Max($read, 1) - 1)] }
            }
            finally {
                $stream.Dispose()
            }

            $result = Get-ElfLoadAlignment -Bytes $buffer
            if ($null -eq $result) {
                $unreadable.Add($entry.FullName)
            }
            elseif ($result.ContainsKey('Unsupported')) {
                $unreadable.Add("$($entry.FullName) ($($result.Unsupported))")
            }
            elseif ($result.Minimum -lt $requiredPageAlignment) {
                $misaligned.Add("$($entry.FullName) (2^$([Math]::Log($result.Minimum, 2)) = $($result.Minimum) bytes)")
            }
        }

        if ($unreadable.Count -gt 0) {
            throw ("These entries in $([IO.Path]::GetFileName($Package)) are not readable 64-bit ELF objects: " +
                ($unreadable -join ', '))
        }
        if ($misaligned.Count -gt 0) {
            throw ("Google Play requires 16 KB page alignment. These libraries in " +
                "$([IO.Path]::GetFileName($Package)) are aligned below $requiredPageAlignment bytes: " +
                ($misaligned -join ', '))
        }

        return $entries.Count
    }
    finally {
        $archive.Dispose()
    }
}

function Get-PackageEntry {
    param(
        [Parameter(Mandatory)][string]$Package,
        [Parameter(Mandatory)][string]$EntryName
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        $entry = $archive.GetEntry($EntryName)
        if (-not $entry) { return $null }
        $stream = $entry.Open()
        try {
            $memory = [IO.MemoryStream]::new()
            $stream.CopyTo($memory)
            return $memory.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

<#
    Google Play authenticates an upload using its registered upload
    certificate, so the identity that matters is the certificate itself rather
    than the fact that some signature verified. This reads the signer
    certificate out of the package's PKCS#7 block and reports the same SHA-256
    fingerprint that keytool, apksigner, and Play Console show.
#>
function Get-SignerCertificateDigest {
    param([Parameter(Mandatory)][string]$Package)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        $block = @($archive.Entries | Where-Object {
                $_.FullName -match '^META-INF/[^/]+\.(RSA|DSA|EC)$'
            }) | Select-Object -First 1
        if (-not $block) {
            throw "$([IO.Path]::GetFileName($Package)) contains no META-INF signature block and is unsigned."
        }

        $stream = $block.Open()
        try {
            $memory = [IO.MemoryStream]::new()
            $stream.CopyTo($memory)
            $signature = [Security.Cryptography.Pkcs.SignedCms]::new()
            $signature.Decode($memory.ToArray())
            $certificate = $signature.SignerInfos[0].Certificate
            if (-not $certificate) {
                throw "$([IO.Path]::GetFileName($Package)) carries a signature without an embedded certificate."
            }

            return [pscustomobject]@{
                Digest  = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($certificate.RawData)).ToLowerInvariant()
                Subject = $certificate.Subject
                Expires = $certificate.NotAfter
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

# --- build ---------------------------------------------------------------

$results = [Collections.Generic.List[object]]::new()
$signingSecretDirectory = $null
$storePasswordFile = $null
$keyPasswordFile = $null
try {
    if (-not $SkipBuild) {
        # The .NET Android signer accepts the documented file: prefix for both APKs and
        # App Bundles. Literal passwords are unsafe here in addition to being fragile:
        # jarsigner tokenizes an MSBuild property containing whitespace and reports the
        # password fragments as extra aliases. Files preserve every character and keep
        # credentials out of the child process's command line.
        $signingSecretDirectory = Join-Path ([IO.Path]::GetTempPath()) "visualcat-signing-$([Guid]::NewGuid().ToString('N'))"
        [IO.Directory]::CreateDirectory($signingSecretDirectory) | Out-Null
        $storePasswordFile = Join-Path $signingSecretDirectory 'store-password.txt'
        $keyPasswordFile = Join-Path $signingSecretDirectory 'key-password.txt'
        $utf8NoBom = [Text.UTF8Encoding]::new($false)
        [IO.File]::WriteAllText($storePasswordFile, $StorePassword, $utf8NoBom)
        [IO.File]::WriteAllText($keyPasswordFile, $KeyPassword, $utf8NoBom)
    }

foreach ($packageFormat in $formats) {
    $artifact = Join-Path $outputRoot "VisualCat-Android-v$Version.$packageFormat"

    if ($SkipBuild) {
        if (-not (Test-Path -LiteralPath $artifact)) {
            throw "-SkipBuild was requested but '$artifact' does not exist."
        }
        $results.Add([pscustomobject]@{ Format = $packageFormat; Path = $artifact })
        continue
    }

    Write-Host "==> Publishing signed .$packageFormat for $expectedApplicationId $Version"

    $stagingPath = Join-Path $outputRoot "publish-$packageFormat"
    if (Test-Path -LiteralPath $stagingPath) {
        Remove-Item -LiteralPath $stagingPath -Recurse -Force
    }

    $arguments = @(
        'publish', $project
        '--configuration', 'Release'
        '--output', $stagingPath
        "-p:Version=$Version"
        "-p:VersionPrefix=$versionPrefix"
        "-p:VisualCatBuildNumber=$VisualCatBuildNumber"
        "-p:ApplicationDisplayVersion=$Version"
        "-p:AndroidPackageFormat=$packageFormat"
        '-p:AndroidKeyStore=true'
        "-p:AndroidSigningKeyStore=$Keystore"
        "-p:AndroidSigningStorePass=file:$storePasswordFile" # gitleaks:allow -- only a temporary filename; the credential is never embedded.
        "-p:AndroidSigningKeyAlias=$KeyAlias"
        "-p:AndroidSigningKeyPass=file:$keyPasswordFile" # gitleaks:allow -- only a temporary filename; the credential is never embedded.
    )
    if ($VersionCode) { $arguments += "-p:ApplicationVersion=$VersionCode" }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Publishing the .$packageFormat failed." }

    $signed = Get-ChildItem -LiteralPath $stagingPath -Recurse -Filter "*-Signed.$packageFormat" |
        Select-Object -First 1
    if (-not $signed) {
        # An unsigned package here means signing was skipped rather than failed,
        # which Play would only reveal at upload time.
        throw "The build produced no signed .$packageFormat in '$stagingPath'."
    }

    Copy-Item -LiteralPath $signed.FullName -Destination $artifact -Force
    $results.Add([pscustomobject]@{ Format = $packageFormat; Path = $artifact })
}
}
finally {
    # Each file is an exact path created above; remove it individually so cleanup never
    # performs a recursive delete against a computed directory.
    if ($storePasswordFile -and (Test-Path -LiteralPath $storePasswordFile)) {
        Remove-Item -LiteralPath $storePasswordFile -Force
    }
    if ($keyPasswordFile -and (Test-Path -LiteralPath $keyPasswordFile)) {
        Remove-Item -LiteralPath $keyPasswordFile -Force
    }
    if ($signingSecretDirectory -and (Test-Path -LiteralPath $signingSecretDirectory)) {
        Remove-Item -LiteralPath $signingSecretDirectory -Force
    }
}

# --- verify --------------------------------------------------------------

# Read from the project rather than repeated here, so pinning a new Android API
# level cannot leave the verification step looking in a directory the build no
# longer writes to.
$projectText = Get-Content -Raw -LiteralPath $project
if ($projectText -notmatch '<TargetFramework>\s*(?<framework>[^<]+?)\s*</TargetFramework>') {
    throw 'VisualCat.Android.csproj does not declare a single <TargetFramework>.'
}

$intermediate = Join-Path $repository "src/VisualCat.Android/obj/Release/$($Matches['framework'])"
$tools = Get-AndroidBuildEnvironment -IntermediatePath $intermediate

$signingCertificate = $null
$summary = [Collections.Generic.List[string]]::new()

foreach ($result in $results) {
    $name = [IO.Path]::GetFileName($result.Path)
    $size = '{0:N1} MB' -f ((Get-Item -LiteralPath $result.Path).Length / 1MB)
    Write-Host "==> Verifying $name"

    if ($result.Format -eq 'apk') {
        $badging = Invoke-Tool -Path $tools.Aapt2 -Arguments @('dump', 'badging', $result.Path) -Description 'aapt2 dump badging'

        if ($badging -notmatch "package: name='(?<id>[^']+)' versionCode='(?<code>\d+)' versionName='(?<version>[^']+)'") {
            throw "aapt2 did not report a package line for $name."
        }
        $applicationId = $Matches['id']
        $versionCodeFound = $Matches['code']
        $versionNameFound = $Matches['version']

        if ($badging -notmatch "sdkVersion:'(?<min>\d+)'") { throw "$name declares no minSdkVersion." }
        $minSdk = $Matches['min']
        if ($badging -notmatch "targetSdkVersion:'(?<target>\d+)'") { throw "$name declares no targetSdkVersion." }
        $targetSdk = [int]$Matches['target']
        $abis = @([regex]::Matches($badging, "native-code: (?<abis>.+)") |
                ForEach-Object { $_.Groups['abis'].Value.Trim() -replace "'", '' }) -join ' '
        $permissions = @([regex]::Matches($badging, "uses-permission: name='(?<permission>[^']+)'") |
                ForEach-Object { $_.Groups['permission'].Value })

        foreach ($permission in $requiredReleasePermissions) {
            if ($permissions -notcontains $permission) {
                throw "$name is missing required Play/Release permission '$permission'."
            }
        }
        foreach ($permission in $forbiddenReleasePermissions) {
            if ($permissions -contains $permission) {
                throw "$name unexpectedly declares forbidden Play/Release permission '$permission'. Release must use Wireless debugging instead of READ_LOGS."
            }
        }
        $allowedReleasePermissions = @(
            $requiredReleasePermissions
            "$applicationId.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION"
        )
        $unexpectedPermissions = @($permissions | Where-Object { $allowedReleasePermissions -notcontains $_ })
        if ($unexpectedPermissions.Count -gt 0) {
            throw "$name declares unexpected permissions: $($unexpectedPermissions -join ', '). Update the explicit Release allowlist only after reviewing their product and Play impact."
        }

        if ($applicationId -ne $expectedApplicationId) {
            throw "$name declares application ID '$applicationId' but '$expectedApplicationId' is required."
        }
        if ($versionNameFound -ne $Version) {
            throw "$name declares versionName '$versionNameFound' but '$Version' was requested."
        }
        if ($targetSdk -lt $requiredTargetSdk) {
            throw "$name targets API $targetSdk. Google Play requires at least API $requiredTargetSdk."
        }
        if ($abis -notmatch 'arm64-v8a') {
            throw "$name ships no arm64-v8a code. Google Play requires 64-bit support."
        }
        if ($expectedVersionCode -and $versionCodeFound -ne $expectedVersionCode) {
            throw "$name declares versionCode $versionCodeFound but $expectedVersionCode was requested."
        }
        $expectedVersionCode = $versionCodeFound

        # Which scheme applies is decided by minSdkVersion, not by preference:
        # at API 31 apksigner emits v3 alone and leaves v1 and v2 unsigned. The
        # requirement is therefore that some APK Signature Scheme verified, not
        # a particular one. Only v1-alone would be a real finding.
        $signatures = Invoke-Tool -Path $tools.ApkSigner -Arguments @('verify', '--print-certs', '--verbose', $result.Path) -Description 'apksigner verify'
        $schemes = @([regex]::Matches($signatures, 'Verified using (?<scheme>v[\d.]+) scheme[^:]*: true') |
                ForEach-Object { $_.Groups['scheme'].Value })
        if (@($schemes | Where-Object { $_ -ne 'v1' }).Count -eq 0) {
            throw "$name is not signed with any APK Signature Scheme:`n$signatures"
        }
        if ($signatures -notmatch 'Signer #1 certificate SHA-256 digest: (?<digest>[0-9a-f]+)') {
            throw "apksigner reported no signing certificate for $name."
        }
        $certificate = $Matches['digest']
        if ($signingCertificate -and $certificate -ne $signingCertificate) {
            throw "$name is signed by $certificate, but another package in this release is signed by $signingCertificate."
        }
        $signingCertificate = $certificate

        $libraries = Test-PackagePageAlignment -Package $result.Path -LibraryPrefix 'lib/'

        $summary.Add("- ``$name`` ($size): $applicationId $versionNameFound (versionCode $versionCodeFound), " +
            "API $minSdk-$targetSdk, $abis, $libraries native libraries 16 KB aligned")
        $summary.Add("  - signed with APK Signature Scheme $($schemes -join ', ')")
        $summary.Add("  - permissions: $($permissions -join ', ')")
    }
    else {
        # An App Bundle is a signed JAR whose manifest is protobuf rather than binary XML.
        # Decode it with the workload's bundletool so permission verification is semantic and
        # exhaustive instead of searching arbitrary protobuf bytes for a few known strings.
        $manifest = Get-PackageEntry -Package $result.Path -EntryName 'base/manifest/AndroidManifest.xml'
        if (-not $manifest) { throw "$name has no base/manifest/AndroidManifest.xml." }
        $manifestText = Invoke-Tool -Path $tools.Java -Arguments @(
            '-jar', $tools.BundleTool, 'dump', 'manifest', "--bundle=$($result.Path)", '--module=base'
        ) -Description 'bundletool dump manifest'
        if ($manifestText -notmatch [regex]::Escape($expectedApplicationId)) {
            throw "$name does not declare application ID '$expectedApplicationId'."
        }
        if ($manifestText -notmatch [regex]::Escape($Version)) {
            throw "$name does not declare versionName '$Version'."
        }
        if ($manifestText -notmatch 'android:versionCode="(?<code>\d+)"') {
            throw "$name does not declare an Android versionCode in its base manifest."
        }
        $versionCodeFound = [int64]$Matches['code']
        if ($versionCodeFound -ne $expectedVersionCode) {
            throw "$name declares versionCode $versionCodeFound but $expectedVersionCode was requested."
        }
        foreach ($permission in $requiredReleasePermissions) {
            if ($manifestText -notmatch [regex]::Escape($permission)) {
                throw "$name is missing required Play/Release permission '$permission' in its base manifest."
            }
        }
        foreach ($permission in $forbiddenReleasePermissions) {
            if ($manifestText -match [regex]::Escape($permission)) {
                throw "$name unexpectedly contains forbidden Play/Release permission '$permission' in its base manifest. Release must use Wireless debugging instead of READ_LOGS."
            }
        }
        $bundlePermissions = @([regex]::Matches(
                $manifestText,
                '<uses-permission\b[^>]*\bandroid:name="(?<permission>[^"]+)"') |
            ForEach-Object { $_.Groups['permission'].Value })
        $allowedBundlePermissions = @(
            $requiredReleasePermissions
            "$expectedApplicationId.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION"
        )
        $unexpectedBundlePermissions = @($bundlePermissions | Where-Object { $allowedBundlePermissions -notcontains $_ })
        if ($unexpectedBundlePermissions.Count -gt 0) {
            throw "$name declares unexpected permissions: $($unexpectedBundlePermissions -join ', '). Update the explicit Release allowlist only after reviewing their product and Play impact."
        }
        if ($manifestText -notmatch 'android:name="com\.barebit\.visualcat\.CaptureForegroundService"') {
            throw "$name does not declare VisualCat's Live capture foreground service."
        }
        # bundletool renders a compiled enum as its numeric value (dataSync = bit 0),
        # while older versions sometimes retain the symbolic XML spelling. Accept
        # both representations, but no other service type.
        if ($manifestText -notmatch '<service\b(?=[^>]*android:name="com\.barebit\.visualcat\.CaptureForegroundService")(?=[^>]*android:foregroundServiceType="(?:dataSync|0x0*1)")[^>]*>') {
            throw "$name does not declare the Live capture service with foregroundServiceType=dataSync."
        }
        if (-not (Get-PackageEntry -Package $result.Path -EntryName 'BundleConfig.pb')) {
            throw "$name has no BundleConfig.pb and is not a valid App Bundle."
        }

        # -strict is deliberately not used: it fails any self-signed certificate,
        # and every Android signing certificate is self-signed by design, so it
        # would report a permanent, meaningless error instead of a signature
        # problem.
        $verification = Invoke-Tool -Path $tools.JarSigner -Arguments @('-verify', $result.Path) -Description 'jarsigner -verify'
        if ($verification -notmatch 'jar verified') {
            throw "$name is not correctly signed:`n$verification"
        }

        $signer = Get-SignerCertificateDigest -Package $result.Path
        if ($signingCertificate -and $signer.Digest -ne $signingCertificate) {
            throw "$name is signed by $($signer.Digest), but another package in this release is signed by $signingCertificate."
        }
        $signingCertificate = $signer.Digest

        $libraries = Test-PackagePageAlignment -Package $result.Path -LibraryPrefix 'base/lib/'

        $summary.Add("- ``$name`` ($size): signed App Bundle for $expectedApplicationId $Version " +
            "(versionCode $versionCodeFound), " +
            "$libraries native libraries 16 KB aligned")
        $summary.Add("  - signed by $($signer.Subject), valid until $($signer.Expires.ToString('yyyy-MM-dd'))")
        $summary.Add("  - Play/Release permissions verified: Wireless ADB and user-visible background-capture permissions present; READ_LOGS absent")
    }
}

if ($signingCertificate -ne $requiredUploadCertificateSha256) {
    throw "The packages use upload certificate SHA-256 '$signingCertificate', but Google Play expects '$requiredUploadCertificateSha256'."
}

foreach ($result in $results) {
    $stagingPath = Join-Path $outputRoot "publish-$($result.Format)"
    if (Test-Path -LiteralPath $stagingPath) {
        Remove-Item -LiteralPath $stagingPath -Recurse -Force
    }
}

$summary.Add("- Google Play upload certificate SHA-256 ``$signingCertificate``")

Write-Host ''
Write-Host 'Android packages ready for Google Play:'
foreach ($line in $summary) { Write-Host $line }

if ($env:GITHUB_STEP_SUMMARY) {
    @('### Android packages') + $summary | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
}
