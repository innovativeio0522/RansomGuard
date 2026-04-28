# RansomGuard Build and Package Script
$ErrorActionPreference = "Stop"

$root = Get-Location
$config = "Release"
$platform = "x64"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Starting RansomGuard Build & Package"
Write-Host "========================================" -ForegroundColor Cyan

# 1. Publish UI Project
Write-Host "`n[1/3] Publishing UI Project..." -ForegroundColor Yellow
dotnet publish RansomGuard.csproj `
    -c $config `
    -r win-x64 `
    --self-contained true `
    -o "bin\x64\$config\net8.0-windows\win-x64\publish" `
    -v q

# 2. Publish Service Project
Write-Host "`n[2/3] Publishing Service Project..." -ForegroundColor Yellow
dotnet publish RansomGuard.Service\RansomGuard.Service.csproj `
    -c $config `
    -r win-x64 `
    --self-contained true `
    -o "RansomGuard.Service\bin\x64\$config\net8.0-windows\win-x64\msixpublish" `
    -v q

# 3. Create MSIX Package
Write-Host "`n[3/3] Creating MSIX Package..." -ForegroundColor Yellow
$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild RansomGuard.Package\RansomGuard.Package.wapproj `
    /p:Configuration=$config `
    /p:Platform=$platform `
    /p:AppxBundle=Always `
    /p:AppxPackageDir="$root\AppPackages" `
    /p:UapAppxPackageBuildMode=SideloadOnly `
    /t:"Clean;Build" `
    /v:m

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Build Complete!" -ForegroundColor Green
Write-Host "Package location: $root\AppPackages\"
Write-Host "========================================" -ForegroundColor Cyan