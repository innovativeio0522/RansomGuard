param (
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [switch]$CleanData
)

if ($CleanData) {
    $dataDir = "$env:ProgramData\RansomGuard"
    if (Test-Path $dataDir) {
        Write-Host "Cleaning application data at $dataDir..." -ForegroundColor Yellow
        Remove-Item -Path $dataDir -Recurse -Force
    }
}


Write-Host "Restoring NuGet packages for runtime win-$Platform..." -ForegroundColor Cyan
dotnet restore RansomGuard.sln -r win-$Platform

if ($LASTEXITCODE -ne 0) {
    Write-Host "Restore failed." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "Cleaning stale database..." -ForegroundColor Yellow
$dbPath = "$env:ProgramData\RansomGuard\activity_log.db"
if (Test-Path $dbPath) {
    Remove-Item $dbPath -Force -ErrorAction SilentlyContinue
}

# 1. Publish Main UI Project
Write-Host "Publishing RansomGuard UI..." -ForegroundColor Cyan

Write-Host "Building MSIX Package ($Configuration|$Platform)..." -ForegroundColor Cyan
msbuild RansomGuard.Package\RansomGuard.Package.wapproj /p:Configuration=$Configuration /p:Platform=$Platform /p:AppxBundle=Always /p:AppxBundlePlatforms="$Platform" /p:UapAppxPackageBuildMode=StoreUpload /restore

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nSuccessfully built the MSIX package!" -ForegroundColor Green
    Write-Host "The package is located in: RansomGuard.Package\AppPackages\" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed. Please check the msbuild output above." -ForegroundColor Red
}
