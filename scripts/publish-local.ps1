$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\WindowsAppRestarter\WindowsAppRestarter.csproj'
$output = Join-Path $repoRoot 'artifacts\publish\win-x64'

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $output

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Published to $output" -ForegroundColor Green
