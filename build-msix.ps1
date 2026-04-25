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

Write-Host "Stopping running instances to release file locks..." -ForegroundColor Yellow
Stop-Process -Name "MaintenanceUI" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "MaintenanceWorker" -Force -ErrorAction SilentlyContinue

Write-Host "Cleaning stale configuration and database..." -ForegroundColor Yellow
$dataDir = "$env:ProgramData\RansomGuard"
if (Test-Path $dataDir) {
    Remove-Item -Path $dataDir -Recurse -Force -ErrorAction SilentlyContinue
}

# 1. Publish Watchdog Project
Write-Host "Publishing RansomGuard Watchdog..." -ForegroundColor Cyan
$watchdogPublishDir = "RansomGuard.Watchdog\bin\$Configuration\net8.0\win-$Platform\publish\"
dotnet publish RansomGuard.Watchdog\RansomGuard.Watchdog.csproj -c $Configuration -r win-$Platform --self-contained true -o $watchdogPublishDir

# 2. Publish Main UI Project
Write-Host "Publishing RansomGuard UI..." -ForegroundColor Cyan
$uiPublishDir = "bin\$Configuration\net8.0-windows\win-$Platform\publish\"
dotnet publish RansomGuard.csproj -c $Configuration -r win-$Platform --self-contained true -o $uiPublishDir

# Sync Watchdog into UI folder
Write-Host "Syncing Watchdog into UI folder..." -ForegroundColor Yellow
Copy-Item -Path "$watchdogPublishDir\*" -Destination $uiPublishDir -Force

# 3. Publish Service Project
Write-Host "Publishing RansomGuard Service..." -ForegroundColor Cyan
$servicePublishDir = "RansomGuard.Service\bin\$Platform\$Configuration\net8.0-windows\win-$Platform\msixpublish\"
dotnet publish RansomGuard.Service\RansomGuard.Service.csproj -c $Configuration -r win-$Platform --self-contained true -o $servicePublishDir

# Sync Watchdog into Service folder
Write-Host "Syncing Watchdog into Service folder..." -ForegroundColor Yellow
Copy-Item -Path "$watchdogPublishDir\*" -Destination $servicePublishDir -Force

Write-Host "Building MSIX Package ($Configuration|$Platform)..." -ForegroundColor Cyan
msbuild RansomGuard.Package\RansomGuard.Package.wapproj /p:Configuration=$Configuration /p:Platform=$Platform /p:AppxBundle=Always /p:AppxBundlePlatforms="$Platform" /p:UapAppxPackageBuildMode=StoreUpload /restore

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nSuccessfully built the MSIX package!" -ForegroundColor Green
    Write-Host "The package is located in: RansomGuard.Package\AppPackages\" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed. Please check the msbuild output above." -ForegroundColor Red
}
