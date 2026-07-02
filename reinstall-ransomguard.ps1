# reinstall-ransomguard.ps1
# Requires Administrator privileges. If not running as Admin, requests elevation.

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    Write-Host "Requesting administrator privileges..." -ForegroundColor Yellow
    Write-Host "An elevated PowerShell window will open to continue the reinstall." -ForegroundColor Yellow
    $scriptPath = [System.IO.Path]::GetFullPath($PSCommandPath)
    $command = "Set-Location -LiteralPath '$PSScriptRoot'; & '$scriptPath'"
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "powershell.exe"
    $psi.Arguments = "-NoExit -ExecutionPolicy Bypass -Command `"$command`""
    $psi.Verb = "runas"
    $psi.WorkingDirectory = $PSScriptRoot

    try {
        [System.Diagnostics.Process]::Start($psi) | Out-Null
        Write-Host "Elevation accepted. Continue in the new Administrator PowerShell window." -ForegroundColor Green
    }
    catch {
        Write-Host "Elevation was cancelled. Reinstallation cannot continue." -ForegroundColor Red
    }

    exit
}

$ErrorActionPreference = "SilentlyContinue"

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " RansomGuard Complete Reinstallation Script" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

# 1. Stop and Delete Services
Write-Host "[1/7] Stopping and removing services..." -ForegroundColor Yellow
$services = @("RGService", "RansomGuardSentinel")
foreach ($serviceName in $services) {
    Write-Host "  Service: $serviceName" -ForegroundColor Cyan
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($null -ne $service) {
        if ($service.Status -ne "Stopped") {
            Write-Host "    Stopping..." -ForegroundColor DarkGray
            Stop-Service -Name $serviceName -Force
            Start-Sleep -Seconds 1
        }
        Write-Host "    Deleting..." -ForegroundColor DarkGray
        sc.exe delete $serviceName | Out-Null
        Start-Sleep -Seconds 1
    } else {
        Write-Host "    Not found." -ForegroundColor DarkGray
    }
}

# 2. Stop processes
Write-Host "`n[2/7] Terminating running processes..." -ForegroundColor Yellow
$processes = @("RGUI", "RGWorker", "RansomGuard", "RansomGuard.Watchdog")
foreach ($procName in $processes) {
    $proc = Get-Process -Name $procName -ErrorAction SilentlyContinue
    if ($proc) {
        Write-Host "  Stopping $procName..." -ForegroundColor Cyan
        $proc | Stop-Process -Force
    }
}
Start-Sleep -Seconds 1

# 3. Remove scheduled tasks
Write-Host "`n[3/7] Cleaning up scheduled tasks..." -ForegroundColor Yellow
$tasks = @("RansomGuardSilentStart", "RGWorkerTask")
foreach ($taskName in $tasks) {
    Write-Host "  Removing task: $taskName" -ForegroundColor Cyan
    schtasks.exe /delete /tn $taskName /f | Out-Null
}

# 4. Uninstall MSIX package
Write-Host "`n[4/7] Uninstalling existing Appx/MSIX package..." -ForegroundColor Yellow
$packageNames = @("RGCoreEssentials", "*RansomGuard*")
foreach ($pkgName in $packageNames) {
    $packages = Get-AppxPackage -Name $pkgName -AllUsers -ErrorAction SilentlyContinue
    if ($packages) {
        foreach ($pkg in $packages) {
            Write-Host "  Removing package: $($pkg.PackageFullName)" -ForegroundColor Cyan
            Remove-AppxPackage -Package $pkg.PackageFullName -ErrorAction SilentlyContinue
            Remove-AppxPackage -Package $pkg.PackageFullName -AllUsers -ErrorAction SilentlyContinue
        }
    }
}

# 5. Delete database files
Write-Host "`n[5/7] Deleting historical activity database..." -ForegroundColor Yellow
$dbPaths = @(
    "$env:ProgramData\RGCoreEssentials\activity_log.db",
    "$env:ProgramData\RGCoreEssentials\activity_log.db-shm",
    "$env:ProgramData\RGCoreEssentials\activity_log.db-wal",
    "$env:ProgramData\RansomGuard\activity_log.db",
    "$env:ProgramData\RansomGuard\activity_log.db-shm",
    "$env:ProgramData\RansomGuard\activity_log.db-wal"
)
foreach ($dbPath in $dbPaths) {
    if (Test-Path $dbPath) {
        Write-Host "  Deleting: $dbPath" -ForegroundColor Cyan
        Remove-Item -Path $dbPath -Force
    }
}

# 6. Locate and install latest package
Write-Host "`n[6/7] Finding latest installation package..." -ForegroundColor Yellow
$packageRoot = Join-Path $PSScriptRoot "RansomGuard.Package\AppPackages"
if (-not (Test-Path $packageRoot)) {
    $packageRoot = Join-Path $PSScriptRoot "AppPackages"
}
if (-not (Test-Path $packageRoot)) {
    Write-Host "ERROR: Could not find AppPackages directory (checked in RansomGuard.Package\AppPackages and AppPackages)." -ForegroundColor Red
    Write-Host "Please build the package first using scripts\build-package.ps1!" -ForegroundColor Red
    Read-Host "`nPress Enter to exit"
    exit 1
}

$installFolders = Get-ChildItem -Path $packageRoot -Recurse -Filter "Add-AppDevPackage.ps1" | 
                  ForEach-Object { $_.Directory } | 
                  Where-Object { 
                      (Get-ChildItem -Path $_.FullName -Filter "*.cer" -ErrorAction SilentlyContinue) -and 
                      (Get-ChildItem -Path $_.FullName -Filter "*.msix*" -ErrorAction SilentlyContinue) 
                  } | 
                  Sort-Object LastWriteTime -Descending

if ($null -eq $installFolders -or $installFolders.Count -eq 0) {
    Write-Host "ERROR: No sideloading packages found." -ForegroundColor Red
    Write-Host "Ensure you run build-package.ps1 first to generate the MSIX package." -ForegroundColor Red
    Read-Host "`nPress Enter to exit"
    exit 1
}

$latestFolder = $installFolders[0]
Write-Host "  Latest package folder found: $($latestFolder.FullName)" -ForegroundColor Green

# Locate Certificate
$cerFiles = Get-ChildItem -Path $latestFolder.FullName -Filter "*.cer"
if ($cerFiles) {
    $cerPath = $cerFiles[0].FullName
    Write-Host "  Found certificate: $($cerFiles[0].Name)" -ForegroundColor Cyan
    Write-Host "  Importing certificate to Local Machine stores..." -ForegroundColor Cyan
    try {
        Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\LocalMachine\Root -ErrorAction Stop
        Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople -ErrorAction Stop
        Write-Host "    Certificate imported successfully." -ForegroundColor Green
    } catch {
        Write-Warning "    Failed to import certificate. Package installation might fail if not trusted. Error: $_"
    }
} else {
    Write-Warning "  No certificate (.cer) file found in the package folder."
}

# Locate MSIX/MSIXBundle file
$bundleFiles = Get-ChildItem -Path $latestFolder.FullName -Filter "*.msixbundle"
if (-not $bundleFiles) {
    $bundleFiles = Get-ChildItem -Path $latestFolder.FullName -Filter "*.msix"
}

if (-not $bundleFiles) {
    Write-Host "ERROR: No .msixbundle or .msix package file found in $($latestFolder.FullName)." -ForegroundColor Red
    Read-Host "`nPress Enter to exit"
    exit 1
}

$packagePath = $bundleFiles[0].FullName
Write-Host "  Installing package: $($bundleFiles[0].Name)..." -ForegroundColor Cyan
try {
    Add-AppxPackage -Path $packagePath -ForceApplicationShutdown -ErrorAction Stop
    Write-Host "  MSIX Package installed successfully!" -ForegroundColor Green
} catch {
    Write-Host "  Direct Add-AppxPackage failed: $_" -ForegroundColor Yellow
    Write-Host "  Falling back to running Add-AppDevPackage.ps1..." -ForegroundColor Yellow
    try {
        Set-Location $latestFolder.FullName
        & ".\Add-AppDevPackage.ps1"
    } catch {
        Write-Host "ERROR: Failed to run Add-AppDevPackage.ps1: $_" -ForegroundColor Red
    } finally {
        Set-Location $PSScriptRoot
    }
}

# 7. Post-Installation Launch
Write-Host "`n[7/7] Launching RansomGuard..." -ForegroundColor Yellow

# Start the service
$service = Get-Service -Name "RGService" -ErrorAction SilentlyContinue
if ($service) {
    Write-Host "  Starting RGService..." -ForegroundColor Cyan
    Start-Service -Name "RGService" -ErrorAction SilentlyContinue
}

# Launch GUI using PackageFamilyName
$pkg = Get-AppxPackage -Name "RGCoreEssentials" -ErrorAction SilentlyContinue
if ($null -eq $pkg) {
    $pkg = Get-AppxPackage -Name "*RansomGuard*" -ErrorAction SilentlyContinue | Select-Object -First 1
}
if ($pkg) {
    $familyName = $pkg.PackageFamilyName
    Write-Host "  Launching GUI app (FamilyName: $familyName)..." -ForegroundColor Cyan
    Start-Process "explorer.exe" -ArgumentList "shell:AppsFolder\$($familyName)!App"
    Write-Host "  Successfully triggered launch." -ForegroundColor Green
} else {
    Write-Warning "  Could not find installed package family name to launch GUI."
}

Write-Host "`n=============================================" -ForegroundColor Green
Write-Host " Reinstallation and database reset complete!" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
Write-Host ""
Read-Host "Press Enter to close this window"
