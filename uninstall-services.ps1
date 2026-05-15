# Uninstall All RansomGuard Services

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    Write-Host "Requesting administrator privileges..." -ForegroundColor Yellow
    Write-Host "An elevated PowerShell window will open to continue the uninstall." -ForegroundColor Yellow
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
        Write-Host "Elevation was cancelled. The uninstall cannot continue." -ForegroundColor Red
    }

    exit
}

$ErrorActionPreference = "SilentlyContinue"
$services = @("RGService", "RansomGuardSentinel")
$tasks = @("RansomGuardSilentStart", "RGWorkerTask")

Write-Host "Stopping and removing RansomGuard services..." -ForegroundColor Yellow

foreach ($serviceName in $services) {
    Write-Host "`nChecking service: $serviceName" -ForegroundColor Cyan
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

    if ($null -eq $service) {
        Write-Host "  Not installed." -ForegroundColor DarkGray
        continue
    }

    if ($service.Status -ne "Stopped") {
        Write-Host "  Stopping..." -ForegroundColor Cyan
        Stop-Service -Name $serviceName -Force
        Start-Sleep -Seconds 1
    }

    Write-Host "  Deleting..." -ForegroundColor Cyan
    sc.exe delete $serviceName | Out-Host
    Start-Sleep -Seconds 1
}

Write-Host "`nRemoving scheduled tasks..." -ForegroundColor Yellow
foreach ($taskName in $tasks) {
    Write-Host "  $taskName" -ForegroundColor Cyan
    schtasks.exe /delete /tn $taskName /f | Out-Null
}

Write-Host "`nStopping watchdog process..." -ForegroundColor Yellow
$watchdogs = Get-Process -Name "RGWorker" -ErrorAction SilentlyContinue
if ($watchdogs) {
    $watchdogs | Stop-Process -Force
    Write-Host "  RGWorker stopped." -ForegroundColor Green
}
else {
    Write-Host "  RGWorker not running." -ForegroundColor DarkGray
}

Write-Host "`nVerifying cleanup..." -ForegroundColor Yellow
$remainingServices = Get-Service -Name $services -ErrorAction SilentlyContinue
if ($remainingServices) {
    $remainingServices | Select-Object Name, DisplayName, Status | Format-Table -AutoSize
}
else {
    Write-Host "  No RansomGuard service entries remain." -ForegroundColor Green
}

Write-Host "`nCleanup complete." -ForegroundColor Green
Read-Host "Press Enter to exit"
