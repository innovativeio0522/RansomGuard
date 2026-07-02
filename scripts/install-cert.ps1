# Check for Administrator privileges; self-elevate if not running as Admin
$myWindowsID = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$myWindowsPrincipal = New-Object System.Security.Principal.WindowsPrincipal($myWindowsID)
$adminRole = [System.Security.Principal.WindowsBuiltInRole]::Administrator

if (-not $myWindowsPrincipal.IsInRole($adminRole)) {
    Write-Host "This script requires Administrator privileges to import certificates to the Local Machine store." -ForegroundColor Yellow
    Write-Host "Relaunching in an elevated window..." -ForegroundColor Yellow
    $args = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"")
    Start-Process powershell -ArgumentList $args -Verb RunAs
    exit
}

$pfxPath = Join-Path $PSScriptRoot "..\RansomGuard.Package\RansomGuard_TemporaryKey.pfx"
$passwordRaw = if ($env:RANSOMGUARD_CERT_PASSWORD) { $env:RANSOMGUARD_CERT_PASSWORD } else { "RansomGuardDev123!" }
$password = ConvertTo-SecureString -String $passwordRaw -Force -AsPlainText

Write-Host "Importing main developer PFX certificate to Local Machine Root..." -ForegroundColor Cyan
try {
    Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation Cert:\LocalMachine\Root -Password $password -ErrorAction Stop
    Write-Host "Successfully imported PFX to Local Machine Root." -ForegroundColor Green
} catch {
    Write-Error "Failed to import PFX to Local Machine Root: $_"
}

Write-Host "Importing main developer PFX certificate to Local Machine Trusted People..." -ForegroundColor Cyan
try {
    Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople -Password $password -ErrorAction Stop
    Write-Host "Successfully imported PFX to Local Machine Trusted People." -ForegroundColor Green
} catch {
    Write-Error "Failed to import PFX to Local Machine Trusted People: $_"
}

# Recursively find and import all generated .cer files in AppPackages
$appPackagesPath = Join-Path $PSScriptRoot "..\RansomGuard.Package\AppPackages"
if (Test-Path $appPackagesPath) {
    Write-Host "`nScanning for generated .cer files in AppPackages..." -ForegroundColor Cyan
    $cerFiles = Get-ChildItem -Path $appPackagesPath -Filter *.cer -Recurse
    foreach ($cer in $cerFiles) {
        Write-Host "Importing certificate: $($cer.Name)..." -ForegroundColor Cyan
        try {
            Import-Certificate -FilePath $cer.FullName -CertStoreLocation Cert:\LocalMachine\TrustedPeople -ErrorAction Stop
            Write-Host "Successfully imported $($cer.Name) to Local Machine Trusted People." -ForegroundColor Green
        } catch {
            Write-Error "Failed to import $($cer.Name): $_"
        }
    }
}

Write-Host "`nDone! You can now install the MSIX bundle." -ForegroundColor Green
Start-Sleep -Seconds 5
