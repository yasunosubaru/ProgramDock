[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SelfContained,
    [string]$Version = '1.0.0',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\ProgramDock\ProgramDock.csproj'
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $suffix = if ($SelfContained) { 'selfcontained' } else { 'framework-dependent' }
    $OutputRoot = Join-Path $PSScriptRoot "artifacts\ProgramDock-$Version-win-x64-$suffix"
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$arguments = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', 'win-x64',
    '-p:Platform=x64',
    "-p:Version=$Version",
    '-p:PublishTrimmed=false',
    '-p:PublishReadyToRun=false',
    '-o', $OutputRoot
)
if ($SelfContained) {
    $arguments += '--self-contained'
    $arguments += 'true'
} else {
    $arguments += '--self-contained'
    $arguments += 'false'
}

Write-Host "Publishing ProgramDock $Version to $OutputRoot" -ForegroundColor Cyan
& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Done: $OutputRoot" -ForegroundColor Green
