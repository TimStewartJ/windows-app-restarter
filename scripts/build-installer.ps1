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

Write-Host "Installer written to $(Join-Path $repoRoot 'installer\Output\WindowsAppRestarterSetup.exe')" -ForegroundColor Green
