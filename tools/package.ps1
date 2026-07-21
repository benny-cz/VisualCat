[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')]
    [string[]]$Runtime = @('win-x64'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts')
)

$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $output -Force | Out-Null

foreach ($rid in $Runtime) {
    $destination = Join-Path $output "VisualCat-$rid"
    dotnet restore (Join-Path $repository 'src/VisualCat.Desktop/VisualCat.Desktop.csproj') `
        --runtime $rid
    if ($LASTEXITCODE -ne 0) {
        throw "Restore failed for $rid."
    }

    dotnet publish (Join-Path $repository 'src/VisualCat.Desktop/VisualCat.Desktop.csproj') `
        --configuration Release `
        --runtime $rid `
        --self-contained true `
        --no-restore `
        --output $destination
    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for $rid."
    }
}
