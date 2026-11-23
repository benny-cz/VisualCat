<#
.SYNOPSIS
    Refuses to continue unless a deployment environment is protected the way
    VisualCat's release signing credentials require.

.DESCRIPTION
    The release signing job takes its credentials from this environment. The
    environment must already exist, because a job that names a missing
    environment would create an unprotected one. It must require a reviewer's
    approval, allow deployments from v* tags and from nothing but v* tags and
    main, and not let
    administrators bypass those rules: agents can work with the owner's
    administrator token, so only the reviewer gate keeps a signing run waiting
    for the owner. Reads GitHub's REST API, with GH_TOKEN when it is set.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[\w.-]+/[\w.-]+$')]
    [string]$Repository,

    [ValidatePattern('^[\w.-]+$')]
    [string]$Environment = 'signing'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-GitHub([string]$Path) {
    $headers = @{ Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
    if ($env:GH_TOKEN) { $headers.Authorization = "Bearer $env:GH_TOKEN" }
    try {
        Invoke-RestMethod -Uri "https://api.github.com/$Path" -Headers $headers
    } catch {
        $response = $_.Exception.PSObject.Properties['Response']
        if ($response -and $response.Value -and [int]$response.Value.StatusCode -eq 404) { return $null }
        throw
    }
}
# The API omits some fields depending on the environment's configuration; a missing one reads as unset.
function Get-Field($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($property) { $property.Value } else { $null }
}

$settings = Get-GitHub "repos/$Repository/environments/$Environment"
if (-not $settings) {
    throw "The '$Environment' environment does not exist. Create it with a required reviewer before any signing run; a job that names a missing environment would create an unprotected one."
}
$problems = [System.Collections.Generic.List[string]]::new()
$reviewRule = @(Get-Field $settings 'protection_rules' | Where-Object { (Get-Field $_ 'type') -eq 'required_reviewers' }) | Select-Object -First 1
$reviewers = if ($reviewRule) { @(Get-Field $reviewRule 'reviewers').Count } else { 0 }
if ($reviewers -eq 0) { $problems.Add('no reviewer must approve its deployments') }
if ((Get-Field $settings 'can_admins_bypass') -ne $false) { $problems.Add('administrators may bypass its protection rules') }
$allowed = @()
$policy = Get-Field $settings 'deployment_branch_policy'
if (-not $policy) {
    $problems.Add('any branch or tag may deploy to it')
} elseif (-not (Get-Field $policy 'custom_branch_policies')) {
    $problems.Add('it follows branch protection instead of an explicit main and v* tag list')
} else {
    $allowed = @(Get-Field (Get-GitHub "repos/$Repository/environments/$Environment/deployment-branch-policies") 'branch_policies' |
        ForEach-Object { "$(Get-Field $_ 'type'):$(Get-Field $_ 'name')" })
    foreach ($entry in $allowed) {
        if ($entry -cne 'branch:main' -and $entry -cnotlike 'tag:v*') { $problems.Add("it allows deployments from $entry") }
    }
    # Releases are signed from their tag, so a v* tag rule must exist; main may be allowed too, as FileCat's is.
    if (-not @($allowed | Where-Object { $_ -clike 'tag:v*' }).Count) { $problems.Add('it does not allow v* tags, which every signed release runs from') }
}
if ($problems.Count) { throw "The '$Environment' environment is not protected enough for signing: $($problems -join '; ')." }
Write-Host "'$Environment' needs approval from one of $reviewers reviewer(s), allows only $($allowed -join ', ') and no administrator bypass."
