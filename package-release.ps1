[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version = '1.0.0',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$releaseRoot = Join-Path $root 'artifacts\release'
$selfContainedRoot = Join-Path $root "artifacts\ProgramDock-$Version-win-x64-selfcontained"
$frameworkRoot = Join-Path $root "artifacts\ProgramDock-$Version-win-x64-framework-dependent"

Remove-Item -LiteralPath $releaseRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

Write-Host 'Publishing self-contained build...' -ForegroundColor Cyan
& (Join-Path $root 'build.ps1') -Configuration $Configuration -Version $Version -SelfContained -OutputRoot $selfContainedRoot
if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }

Write-Host 'Publishing framework-dependent build...' -ForegroundColor Cyan
& (Join-Path $root 'build.ps1') -Configuration $Configuration -Version $Version -OutputRoot $frameworkRoot
if ($LASTEXITCODE -ne 0) { throw 'Framework-dependent publish failed.' }

$selfContainedZip = Join-Path $releaseRoot "ProgramDock-$Version-win-x64-selfcontained.zip"
$frameworkZip = Join-Path $releaseRoot "ProgramDock-$Version-win-x64-framework-dependent.zip"
Compress-Archive -Path (Join-Path $selfContainedRoot '*') -DestinationPath $selfContainedZip -CompressionLevel Optimal
Compress-Archive -Path (Join-Path $frameworkRoot '*') -DestinationPath $frameworkZip -CompressionLevel Optimal

Copy-Item -LiteralPath (Join-Path $root 'RELEASE_NOTES.md') -Destination (Join-Path $releaseRoot 'RELEASE_NOTES.md') -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $releaseRoot 'LICENSE') -Force

$assets = @($selfContainedZip, $frameworkZip)
$checksums = foreach ($asset in $assets) {
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $asset).Hash
    "$hash  $(Split-Path -Leaf $asset)"
}
$checksums | Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding utf8

Write-Host "Release assets written to $releaseRoot" -ForegroundColor Green
Get-ChildItem -LiteralPath $releaseRoot | Select-Object Name, Length
