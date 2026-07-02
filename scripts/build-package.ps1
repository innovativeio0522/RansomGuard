# RansomGuard Build and Package Script
$ErrorActionPreference = "Stop"

$env:BUILD_MSIX = "true"

$root = Get-Location
$config = "Release"
$platform = "x64"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Starting RansomGuard Build & Package"
Write-Host "========================================" -ForegroundColor Cyan

# Restore packages for win-x64 runtime
Write-Host "Restoring NuGet packages for runtime win-x64..." -ForegroundColor Cyan
dotnet restore RansomGuard.sln -r win-x64

# 1. Publish Watchdog (RGWorker) Project
Write-Host "`n[1/4] Publishing Watchdog Project..." -ForegroundColor Yellow
$watchdogPublishDir = "RansomGuard.Watchdog\bin\x64\$config\net8.0\win-x64\publish"
dotnet publish RansomGuard.Watchdog\RansomGuard.Watchdog.csproj `
    -c $config `
    -r win-x64 `
    --self-contained true `
    -o $watchdogPublishDir `
    -v q

# 2. Publish UI Project
Write-Host "`n[2/4] Publishing UI Project..." -ForegroundColor Yellow
$uiPublishDir = "bin\x64\$config\net8.0-windows\win-x64\publish"
dotnet publish RansomGuard.csproj `
    -c $config `
    -r win-x64 `
    --self-contained true `
    -o $uiPublishDir `
    -v q

Write-Host "  Copying Watchdog to UI folder..." -ForegroundColor Gray
Copy-Item -Path "$watchdogPublishDir\*" -Destination $uiPublishDir -Force

# 3. Publish Service Project
Write-Host "`n[3/4] Publishing Service Project..." -ForegroundColor Yellow
$servicePublishDir = "RansomGuard.Service\bin\x64\$config\net8.0-windows\win-x64\msixpublish"
dotnet publish RansomGuard.Service\RansomGuard.Service.csproj `
    -c $config `
    -r win-x64 `
    --self-contained true `
    -o $servicePublishDir `
    -v q

Write-Host "  Copying Watchdog to Service folder..." -ForegroundColor Gray
Copy-Item -Path "$watchdogPublishDir\*" -Destination $servicePublishDir -Force

# 4. Create MSIX Package
Write-Host "`n[4/4] Creating MSIX Package..." -ForegroundColor Yellow
$certPassword = if ($env:RANSOMGUARD_CERT_PASSWORD) { $env:RANSOMGUARD_CERT_PASSWORD } else { "RansomGuardDev123!" }
$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild RansomGuard.Package\RansomGuard.Package.wapproj `
    /restore `
    /p:Configuration=$config `
    /p:Platform=$platform `
    /p:AppxBundle=Always `
    /p:AppxPackageDir="$root\AppPackages" `
    /p:UapAppxPackageBuildMode=SideloadOnly `
    /p:PackageCertificatePassword=$certPassword `
    /t:"Clean;Build" `
    /v:m

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Build Complete!" -ForegroundColor Green
Write-Host "Package location: $root\AppPackages\"
Write-Host "========================================" -ForegroundColor Cyan