param([ValidateSet("win-x64", "win-arm64")][string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $ProjectRoot
try {
    dotnet run --project tests/DiskLens.Core.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw "Core regression tests failed." }
    dotnet publish src/DiskLens.App -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -o "artifacts/$Runtime"
    if ($LASTEXITCODE -ne 0) { throw "Windows publish failed." }
    Copy-Item docs/PORTABLE-README.txt "artifacts/$Runtime/README.txt"
    Copy-Item docs/WINDOWS-ACCEPTANCE.md "artifacts/$Runtime/WINDOWS-ACCEPTANCE.md"
    Write-Host "Portable build: $ProjectRoot/artifacts/$Runtime/DiskLens.exe"
} finally { Pop-Location }
