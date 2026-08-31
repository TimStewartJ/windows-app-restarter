$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
)

$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw 'Inno Setup compiler was not found. Install JRSoftware.InnoSetup with winget, then run this script again.'
}

$publishDir = Join-Path $repoRoot 'artifacts\publish\win-x64'
if (-not (Test-Path (Join-Path $publishDir 'WindowsAppRestarter.exe'))) {
    & (Join-Path $PSScriptRoot 'publish-local.ps1')
}

& $iscc `
    "/DSourceDir=$publishDir" `
    (Join-Path $repoRoot 'installer\WindowsAppRestarter.iss')

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$installerPath = Join-Path $repoRoot 'installer\Output\WindowsAppRestarterSetup.exe'
$hash = (Get-FileHash $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText((Join-Path $repoRoot 'installer\Output\SHA256SUMS.txt'), "$hash  WindowsAppRestarterSetup.exe`n")

Write-Host "Installer written to $installerPath" -ForegroundColor Green
Write-Host "SHA-256: $hash" -ForegroundColor Green
