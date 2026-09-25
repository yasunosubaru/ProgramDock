[CmdletBinding()]
param(
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$releaseRoot = Join-Path $PSScriptRoot 'artifacts\release'
$expected = @(
    "ProgramDock-$Version-win-x64-selfcontained.zip",
    "ProgramDock-$Version-win-x64-framework-dependent.zip",
    'SHA256SUMS.txt',
    'RELEASE_NOTES.md'
)

foreach ($name in $expected) {
    $path = Join-Path $releaseRoot $name
    if (-not (Test-Path -LiteralPath $path)) {
        throw "缺少 Release 资产：$path"
    }
}

$checksumFile = Join-Path $releaseRoot 'SHA256SUMS.txt'
foreach ($line in Get-Content -LiteralPath $checksumFile) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $parts = $line -split '\s+', 2
    if ($parts.Count -ne 2) { throw "无效的校验行：$line" }
    $path = Join-Path $releaseRoot $parts[1]
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
    if ($actual -ne $parts[0]) {
        throw "SHA-256 校验失败：$($parts[1])"
    }
}

Write-Host "Release assets verified for ProgramDock $Version" -ForegroundColor Green
