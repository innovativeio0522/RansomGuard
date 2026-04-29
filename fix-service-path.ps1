# Fix RansomGuard Service Path
# This script stops, removes, and reinstalls the service with the correct path

Write-Host "=== RansomGuard Service Path Fix ===" -ForegroundColor Green
Write-Host ""

# Check if running as administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "ERROR: This script must be run as Administrator!" -ForegroundColor Red
    Write-Host "Right-click PowerShell and select 'Run as Administrator', then run this script again." -ForegroundColor Yellow
    pause
    exit 1
}

Write-Host "Running with Administrator privileges" -ForegroundColor Green
Write-Host ""

# Stop the service if it's running
Write-Host "[1/4] Stopping RGService..." -ForegroundColor Cyan
try {
    Stop-Service -Name "RGService" -Force -ErrorAction SilentlyContinue
    Write-Host "  Service stopped" -ForegroundColor Gray
} catch {
    Write-Host "  Service was not running" -ForegroundColor Gray
}

# Remove the existing service
Write-Host "[2/4] Removing old service registration..." -ForegroundColor Cyan
try {
    sc.exe delete RGService
    Start-Sleep -Seconds 2
    Write-Host "  Old service removed" -ForegroundColor Gray
} catch {
    Write-Host "  No existing service found" -ForegroundColor Gray
}

# Find the correct service executable path
$possiblePaths = @(
    "RansomGuard.Service\bin\Debug\net8.0-windows\win-x64\RGService.exe",
    "RansomGuard.Service\publish\RGService.exe",
    "RansomGuard.Service\bin\Debug\net8.0-windows\RGService.exe"
)

$servicePath = $null
foreach ($path in $possiblePaths) {
    $fullPath = Join-Path $PSScriptRoot $path
    if (Test-Path $fullPath) {
        $servicePath = $fullPath
        break
    }
}

if (-not $servicePath) {
    Write-Host "ERROR: Could not find RGService.exe!" -ForegroundColor Red
    Write-Host "Please build the service project first using:" -ForegroundColor Yellow
    Write-Host "  dotnet build RansomGuard.Service/RansomGuard.Service.csproj" -ForegroundColor White
    pause
    exit 1
}

Write-Host "[3/4] Found service executable at:" -ForegroundColor Cyan
Write-Host "  $servicePath" -ForegroundColor White

# Install the service with the correct path
Write-Host "[4/4] Installing service with correct path..." -ForegroundColor Cyan
try {
    sc.exe create RGService binPath= "`"$servicePath`"" start= auto DisplayName= "RansomGuard Sentinel"
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  Service installed successfully" -ForegroundColor Green
        
        # Set service to run as LocalSystem
        sc.exe config RGService obj= LocalSystem
        
        # Set service description
        sc.exe description RGService "Real-time ransomware protection and file system monitoring service"
        
        Write-Host ""
        Write-Host "=== Service Installation Complete ===" -ForegroundColor Green
        Write-Host ""
        Write-Host "Service Details:" -ForegroundColor Yellow
        Write-Host "  Name: RGService" -ForegroundColor White
        Write-Host "  Display Name: RansomGuard Sentinel" -ForegroundColor White
        Write-Host "  Path: $servicePath" -ForegroundColor White
        Write-Host "  Start Type: Automatic" -ForegroundColor White
        Write-Host "  Account: LocalSystem" -ForegroundColor White
        Write-Host ""
        
        # Try to start the service
        Write-Host "Attempting to start the service..." -ForegroundColor Cyan
        try {
            Start-Service -Name "RGService" -ErrorAction Stop
            Write-Host "  Service started successfully!" -ForegroundColor Green
            
            # Check service status
            $service = Get-Service -Name "RGService"
            Write-Host ""
            Write-Host "Current Status: $($service.Status)" -ForegroundColor Green
        } catch {
            Write-Host "  WARNING: Service could not be started automatically" -ForegroundColor Yellow
            Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
            Write-Host ""
            Write-Host "Possible reasons:" -ForegroundColor Yellow
            Write-Host "  1. Missing dependencies (check Event Viewer)" -ForegroundColor White
            Write-Host "  2. Configuration file issues" -ForegroundColor White
            Write-Host "  3. Permission problems with data directories" -ForegroundColor White
            Write-Host ""
            Write-Host "Try starting manually with:" -ForegroundColor Yellow
            Write-Host "  Start-Service -Name RGService" -ForegroundColor White
            Write-Host ""
            Write-Host "Check Event Viewer for details:" -ForegroundColor Yellow
            Write-Host "  Windows Logs > Application" -ForegroundColor White
        }
    } else {
        Write-Host "  Service installation failed" -ForegroundColor Red
        Write-Host "  Error code: $LASTEXITCODE" -ForegroundColor Red
    }
} catch {
    Write-Host "  Installation error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
pause
