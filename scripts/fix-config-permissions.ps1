#!/usr/bin/env pwsh
# Fix RansomGuard config file permissions
# Must run as Administrator

#Requires -RunAsAdministrator

$configPath = "C:\ProgramData\RansomGuard\config.json"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Fix RansomGuard Config Permissions" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $configPath)) {
    Write-Host "Config file not found: $configPath" -ForegroundColor Red
    exit 1
}

Write-Host "Current permissions:" -ForegroundColor Yellow
Get-Acl $configPath | Select-Object -ExpandProperty Access | Format-Table IdentityReference, FileSystemRights, AccessControlType

Write-Host ""
Write-Host "Adding write permissions for Users..." -ForegroundColor Yellow

try {
    # Get current ACL
    $acl = Get-Acl $configPath
    
    # Create new rule: Users get Modify rights (Read + Write + Delete)
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        "BUILTIN\Users",
        "Modify",
        "Allow"
    )
    
    # Add the rule
    $acl.SetAccessRule($rule)
    
    # Apply the ACL
    Set-Acl $configPath $acl
    
    Write-Host "SUCCESS! Permissions updated." -ForegroundColor Green
    Write-Host ""
    
    Write-Host "New permissions:" -ForegroundColor Yellow
    Get-Acl $configPath | Select-Object -ExpandProperty Access | Format-Table IdentityReference, FileSystemRights, AccessControlType
    
    Write-Host ""
    Write-Host "Now try toggling settings in the UI!" -ForegroundColor Green
    
} catch {
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
