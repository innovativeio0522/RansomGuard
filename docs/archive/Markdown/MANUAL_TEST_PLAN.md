# RansomGuard — Manual Test Plan

> **Version:** 1.0.1.4  
> **Date:** April 25, 2026  
> **Purpose:** Comprehensive manual testing guide covering all application functionality

---

## Table of Contents

1. [Installation Tests](#1-installation-tests)
2. [Dashboard Tests](#2-dashboard-tests)
3. [Settings Page Tests](#3-settings-page-tests)
4. [Threat Alerts Page Tests](#4-threat-alerts-page-tests)
5. [Real-Time Detection Tests](#5-real-time-detection-tests)
6. [Quarantine Tests](#6-quarantine-tests)
7. [Self-Healing / Watchdog Tests](#7-self-healing--watchdog-tests)
8. [Background Service Tests](#8-background-service-tests)
9. [Configuration Persistence Tests](#9-configuration-persistence-tests)
10. [System Tray Tests](#10-system-tray-tests)
11. [Navigation Tests](#11-navigation-tests)
12. [Edge Case / Stress Tests](#12-edge-case--stress-tests)
13. [Uninstall Tests](#13-uninstall-tests)
14. [Quick Smoke Test Checklist](#14-quick-smoke-test-checklist)

---

## 1. Installation Tests

| # | Test | Steps | Expected Result |
|---|------|-------|-----------------|
| 1.1 | MSIX Install via script | Right-click `Add-AppDevPackage.ps1` → Run with PowerShell | Installs without errors |
| 1.2 | Certificate install | Double-click `.cer` → Install to Trusted Root CA | Certificate accepted, no warning |
| 1.3 | App appears in Start Menu | Search "RansomGuard" in Windows Search | App found and launchable |
| 1.4 | App launches | Click app from Start Menu | UI opens, no crash |
| 1.5 | Processes running after launch | Open Task Manager | `MaintenanceUI.exe` and `MaintenanceWorker.exe` both visible |

---

## 2. Dashboard Tests

| # | Test | Steps | Expected Result |
|---|------|-------|-----------------|
| 2.1 | Dashboard loads | Launch app, go to Dashboard | Stats visible: Files Monitored, Threats Blocked, Active Alerts |
| 2.2 | Threat risk ring | Observe the risk score ring | Ring animates, score between 5–15 on a clean system |
| 2.3 | CPU / RAM telemetry | Watch CPU and RAM stats | Updates every 2 seconds with real system values |
| 2.4 | Files per hour counter | Create or modify files in a monitored folder | Counter increments accordingly |
| 2.5 | Heuristics status | No threats present | Shows "PASS" |
| 2.6 | Behavioral status | No active alerts | Shows "STABLE" |
| 2.7 | Entropy score | Observe entropy value | Shows a decimal value e.g. "2.4" |
| 2.8 | Active paths list | Check paths shown on dashboard | Matches paths configured in Settings |
| 2.9 | Search bar | Type a filename in the search box | Recent activities filter in real time |
| 2.10 | View All Logs button | Click "View All Logs" | Navigates to Threat Alerts page |
| 2.11 | NEW badge — no alerts | When no active alerts exist | Badge shows "CLEAR" |
| 2.12 | NEW badge — with alerts | When one or more alerts exist | Badge shows "X NEW" with correct count |

---

## 3. Settings Page Tests

### 3a. Monitored Paths

| # | Test | Steps | Expected Result |
|---|------|-------|-----------------|
| 3.1 | Default paths loaded | Open Settings page | Documents, Desktop, Pictures etc. pre-populated |
| 3.2 | Add custom path | Click Add Path → select a folder | Folder appears in the monitored paths list |
| 3.3 | Remove custom path | Click remove on a non-standard path | Path removed from list |
| 3.4 | Cannot remove standard path | Try to remove Documents or Desktop | Remove button disabled or hidden for standard folders |
| 3.5 | Config persists after restart | Add a custom path → close app → reopen | Custom path still present in list |

### 3b. Protection Toggles

| # | Test | Steps | Expected Result |
|---|------|-------|-----------------|
| 3.6 | Real-Time Protection OFF | Toggle Real-Time Protection to OFF | File monitoring stops — no new activity on Dashboard |
| 3.7 | Real-Time Protection ON | Toggle Real-Time Protection back to ON | File monitoring resumes |
| 3.8 | Auto Quarantine toggle | Toggle Auto Quarantine ON | Setting saved, auto-quarantine enabled |
| 3.9 | Network Isolation toggle | Toggle Network Isolation ON | Setting saved |
| 3.10 | Emergency Shutdown toggle | Toggle Emergency Shutdown ON | Setting saved (**do not trigger an actual shutdown test**) |
| 3.11 | All toggles persist | Change any toggle → restart app | All toggles retain their saved state |

### 3c. Sensitivity Level

| # | Test | Steps | Expected Result |
|---|------|-------|-----------------|
| 3.12 | Sensitivity level 1 | Move slider to level 1 | Label shows "LOW" |
| 3.13 | Sensitivity level 2 | Move slider to level 2 | Label shows "MEDIUM" |
| 3.14 | Sensitivity level 3 | Move slider to level 3 | Label shows "HIGH" |
| 3.15 | Sensitivity level 4 | Move slider to level 4 | Label shows "PARANOID" |
| 3.16 | Sensitivity persists | Set to HIGH → restart app | Still shows HIGH after restart |

### 3d. Watchdog Toggle

| # | Test | Steps | Expected Result |
|---|------|-------|-----------------|
| 3.17 | Enable Watchdog | Toggle Watchdog ON | `MaintenanceWorker.exe` appears in Task Manager |
| 3.18 | Disable Watchdog | Toggle Watchdog OFF | `MaintenanceWorker.exe` disappears from Task Manager |

### 3e. Service Management

| # | Test | Steps | Expected Result |
|---|------|-------|-----------------|
| 3.19 | Service already installed | Open Settings when service is running | "Install Service" button hidden, green checkmark shown |
| 3.20 | Install Service | Click Install Service (run app as admin) | Service installs, button hides, success message shown |
| 3.21 | Uninstall Service | Click Uninstall Service → confirm Yes | Service removed, confirmation message shown |
| 3.22 | Uninstall cancel | Click Uninstall → confirm No | Service remains installed, no change |
| 3.23 | License Info popup | Click License Info button | Popup shows license details (Node ID, expiry etc.) |

---

## 4. Threat Alerts Page Tests

| # | Test | Steps | Expected Result |
|---|------|-------|-----------------|
| 4.1 | Page loads | Navigate to Threat Alerts | Page shows with severity counters at top |
| 4.2 | Severity counters — clean | No threats present | All counters (Critical, High, Medium, Low) show 0 |
| 4.3 | Search filter | Type in the search box | List filters by filename or process name in real time |
| 4.4 | Severity filter | Select "Critical" from dropdown | Only critical threats shown |
| 4.5 | Date range filter | Select "Last 7 Days" | Only threats from the last 7 days shown |
| 4.6 | Pagination — next | More than 20 threats exist | Next button appears and loads next page |
| 4.7 | Pagination — previous | On page 2 or beyond | Previous button appears and navigates back |
| 4.8 | Sync Logs button | Click Sync | List refreshes with latest data |
| 4.9 | Quarantine from list | Click Quarantine on a threat | Threat removed from list, severity counter decreases |
| 4.10 | Ignore threat | Click Ignore on a threat | Threat removed from list |
| 4.11 | No alerts state | When list is empty | Shows "No alerts" as pagination text |

---

## 5. Real-Time Detection Tests

> ⚠️ **Warning:** Perform these tests in a dedicated test folder only. Do NOT use real personal data folders.

### 5a. Suspicious Extension Detection

| # | Test | Steps | Expected Result |
|---|------|-------|-----------------|
| 5.1 | `.locked` extension | Create `test.locked` in a monitored folder | Alert appears on Dashboard |
| 5.2 | `.encrypted` extension | Create `test.encrypted` in a monitored folder | Alert appears on Dashboard |
| 5.3 | `.ransom` extension | Create `test.ransom` in a monitored folder | Alert appears on Dashboard |
| 5.4 | Normal `.txt` file | Create `test.txt` in a monitored folder | Activity logged, no threat alert raised |

### 5b. File Activity Monitoring

| # | Test | Steps | Expected Result |
|---|------|-------|-----------------|
| 5.5 | File created | Create any file in a monitored folder | Appears in Recent Activities on Dashboard |
| 5.6 | File modified | Edit and save a file in a monitored folder | Appears in Recent Activities |
| 5.7 | File deleted | Delete a file in a monitored folder | Appears in Recent Activities |
| 5.8 | File renamed | Rename a file in a monitored folder | Appears in Recent Activities |
| 5.9 | Unmonitored folder | Create a file outside all monitored paths | No activity logged on Dashboard |

### 5c. Mass Encryption Detection (Velocity Check)

| # | Test | Steps | Expected Result |
|---|------|-------|-----------------|
| 5.10 | Mass file changes | Run the test script below to create 35 files rapidly | **CRITICAL** alert: "MASSIVE FILE ENCRYPTION ACTION DETECTED" |
| 5.11 | Critical alert popup | When critical threat fires | `ShieldUpAlert` popup window appears on screen |
| 5.12 | Risk score spikes | After critical alert | Risk ring jumps to high value (85–95) |
| 5.13 | Behavioral status changes | After critical alert | Dashboard shows "ALERT" for Behavioral Status |

**PowerShell script for test 5.10** — run this pointing at a monitored test folder:

```powershell
$testFolder = "C:\TestFolder"   # Change to a monitored folder path
New-Item -ItemType Directory -Force $testFolder
1..35 | ForEach-Object {
    Set-Content "$testFolder\file$_.txt" "data$_"
    Start-Sleep -Milliseconds 100
}
```

---

## 6. Quarantine Tests

| # | Test | Steps | Expected Result |
|---|------|-------|-----------------|
| 6.1 | Quarantine from Dashboard | Click Quarantine on an active alert | Alert removed from Dashboard, Threats Blocked count increases |
| 6.2 | Quarantine from Threat Alerts | Click Quarantine on a threat in the list | Threat removed from list, counter decreases |
| 6.3 | Auto-quarantine | Enable Auto Quarantine in Settings, then trigger a suspicious file | File automatically quarantined without manual action |
| 6.4 | Quarantine persists | Quarantine a file, restart app | File still shown as quarantined after restart |

---

## 7. Self-Healing / Watchdog Tests

| # | Test | Steps | Expected Result |
|---|------|-------|-----------------|
| 7.1 | Watchdog running | Open Task Manager after launch | `MaintenanceWorker.exe` visible |
| 7.2 | UI auto-restarts | Kill `MaintenanceUI.exe` via Task Manager → wait 10 seconds | UI automatically relaunches |
| 7.3 | Watchdog survives UI close | Close the app window normally | `MaintenanceWorker.exe` still running in Task Manager |
| 7.4 | Watchdog check script | Run `.\check-selfhealing.ps1` in PowerShell | Reports watchdog status correctly |

---

## 8. Background Service Tests

| # | Test | Steps | Expected Result |
|---|------|-------|-----------------|
| 8.1 | Service installed | Run `Get-Service -Name WinMaintenance` in PowerShell | Service found |
| 8.2 | Service running | Check service status | Status = Running |
| 8.3 | Service starts on boot | Restart PC with service installed | Service auto-starts, no manual action needed |
| 8.4 | Service survives UI close | Close the UI, check service | `WinMaintenance` service still running |
| 8.5 | Event log entries | Open Event Viewer → Windows Logs → Application | WinMaintenance entries visible |

---

## 9. Configuration Persistence Tests

| # | Test | Steps | Expected Result |
|---|------|-------|-----------------|
| 9.1 | All settings save | Change every setting → close app → reopen | All settings exactly as left |
| 9.2 | Config file location (MSIX) | Check `%LocalAppData%\RansomGuard\config.json` | File exists with correct values |
| 9.3 | Config file location (traditional) | Check `%ProgramData%\RansomGuard\config.json` | File exists with correct values |
| 9.4 | Corrupt config recovery | Delete `config.json` → restart app | App recreates config with defaults, no crash |

---

## 10. System Tray Tests

| # | Test | Steps | Expected Result |
|---|------|-------|-----------------|
| 10.1 | Tray icon present | Launch app | RansomGuard icon visible in system tray |
| 10.2 | Minimize to tray | Close or minimize app window | App minimizes to tray, not taskbar |
| 10.3 | Restore from tray | Double-click tray icon | App window restores |
| 10.4 | Tray right-click menu | Right-click tray icon | Context menu appears with options |

---

## 11. Navigation Tests

| # | Test | Steps | Expected Result |
|---|------|-------|-----------------|
| 11.1 | Dashboard nav | Click Dashboard in sidebar | Dashboard view loads |
| 11.2 | Threat Alerts nav | Click Threat Alerts in sidebar | Threat Alerts view loads |
| 11.3 | Settings nav | Click Settings in sidebar | Settings view loads |
| 11.4 | Process Monitor nav | Click Process Monitor in sidebar | Process list loads |
| 11.5 | Active nav highlight | Click each nav item | Active item is highlighted in the sidebar |

---

## 12. Edge Case / Stress Tests

| # | Test | Steps | Expected Result |
|---|------|-------|-----------------|
| 12.1 | Add duplicate path | Try adding the same folder twice | Second add is ignored, no duplicate in list |
| 12.2 | Add non-existent path | Manually enter an invalid path | Watcher not created, no crash |
| 12.3 | Large number of files | Monitor a folder with 10,000+ files | App remains responsive, no freeze |
| 12.4 | Rapid settings changes | Toggle settings on and off rapidly | No crash, final state saved correctly |
| 12.5 | Multiple app instances | Try launching the app twice | Only one instance runs |
| 12.6 | Low disk space | Run with less than 100 MB free disk | App handles gracefully, no crash |
| 12.7 | Run without admin rights | Launch without administrator privileges | App runs, shows warning for features needing elevation |

---

## 13. Uninstall Tests

| # | Test | Steps | Expected Result |
|---|------|-------|-----------------|
| 13.1 | Uninstall via Windows Settings | Settings → Apps → RansomGuard → Uninstall | App removed cleanly |
| 13.2 | Processes stopped | After uninstall, check Task Manager | No `MaintenanceUI.exe` or `MaintenanceWorker.exe` |
| 13.3 | Service removed | After uninstall, run `Get-Service WinMaintenance` | Service not found |
| 13.4 | Start Menu cleaned | Search for RansomGuard after uninstall | App not found in Start Menu |

---

## 14. Quick Smoke Test Checklist

Run these after every new build to confirm nothing is broken. Should take under 5 minutes.

- [ ] App launches without crash
- [ ] Dashboard shows real CPU and RAM values
- [ ] Settings page opens without error
- [ ] Toggle Real-Time Protection OFF and ON — setting saves correctly
- [ ] Add a folder path — appears in the monitored paths list
- [ ] Create a `.locked` file in a monitored folder — alert fires on Dashboard
- [ ] Quarantine the alert — removed from Dashboard, Threats Blocked count increases
- [ ] `MaintenanceWorker.exe` visible in Task Manager
- [ ] `WinMaintenance` service running (check via `Get-Service -Name WinMaintenance`)
- [ ] Kill `MaintenanceUI.exe` — UI restarts automatically within 10 seconds

---

## Test Environment Setup

Before running tests, prepare the following:

```powershell
# 1. Create a dedicated test folder
New-Item -ItemType Directory -Force "C:\RansomGuardTestFolder"

# 2. Add it to monitored paths via Settings page

# 3. Verify service is running
Get-Service -Name WinMaintenance

# 4. Verify watchdog is running
Get-Process -Name MaintenanceWorker
```

---

*Test Plan generated for RansomGuard v1.0.1.4 — April 25, 2026*
