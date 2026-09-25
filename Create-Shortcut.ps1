[CmdletBinding()]
param(
    [switch]$Desktop,
    [switch]$StartMenu
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$artifactRoot = Join-Path $projectRoot 'artifacts'
$exe = $null

$candidates = @(
    Get-ChildItem -LiteralPath $artifactRoot -Directory -Filter 'ProgramDock-*-win-x64-selfcontained' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending
    Get-ChildItem -LiteralPath $artifactRoot -Directory -Filter 'ProgramDock-*-win-x64-framework-dependent' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending
)
foreach ($candidate in $candidates) {
    $candidateExe = Join-Path $candidate.FullName 'ProgramDock.exe'
    if (Test-Path -LiteralPath $candidateExe) {
        $exe = $candidateExe
        break
    }
}

if ([string]::IsNullOrWhiteSpace($exe)) {
    throw '找不到 ProgramDock.exe，请先运行 .\package-release.ps1'
}

$shell = New-Object -ComObject WScript.Shell
$targets = @()
if ($Desktop) {
    $targets += Join-Path ([Environment]::GetFolderPath('Desktop')) 'ProgramDock.lnk'
}
if ($StartMenu) {
    $targets += Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\ProgramDock.lnk'
}
if ($targets.Count -eq 0) {
    $targets = @(
        (Join-Path ([Environment]::GetFolderPath('Desktop')) 'ProgramDock.lnk'),
        (Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\ProgramDock.lnk')
    )
}

foreach ($target in $targets) {
    $parent = Split-Path -Parent $target
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    $shortcut = $shell.CreateShortcut($target)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = Split-Path -Parent $exe
    $shortcut.Description = 'ProgramDock program launcher'
    $shortcut.Save()
    Write-Host "已创建快捷方式：$target" -ForegroundColor Green
}
