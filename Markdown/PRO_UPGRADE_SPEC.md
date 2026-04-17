# RansomGuard: Pro-Service Upgrade Specification

This document outlines the architectural and functional upgrades to transform RansomGuard from a reactive monitor into a proactive, professional-grade defense system.

## 1. Professional Service Architecture
*   **[x] Split-Process Model:**
    *   **[x] RansomGuard.Service (The Muscle):** A headless Windows Background Service (.NET 8) running as `SYSTEM`. It starts before user login and handles the core detection/response logic.
    *   **[x] RansomGuard.Dashboard (The Brain):** The WPF user interface that monitors and controls the background service.
*   **[x] Inter-Process Communication (IPC):** Use of high-speed **Named Pipes** to stream live telemetry and threat alerts from the Service to the Dashboard.

## 2. Proactive Detection (The "Tripwire" System)
*   **[x] Honey Pot (Canary) Module:** Automatically deploys hidden "bait" files in critical directories (Desktop, Documents, Downloads).
*   **[x] Behavioral Analysis:** Monitors for high-speed file operations (e.g., >10 modifications/second) to catch unknown ransomware patterns.
*   **[x] VSS Shield:** Actively monitors and blocks attempts to delete Windows Volume Shadow Copies (`vssadmin`, `powershell`, etc.), ensuring "Time Machine" recovery remains available.

## 3. Active Response (The "Kill Switch")
*   **[x] Automated Process Termination:** Instantly kills any process that triggers a honey pot or reaches a behavioral threat threshold.
*   **[x] Threat Quarantine:** Moves malicious binaries to a secure, encrypted folder (`C:\RansomGuard\Quarantine`) and renames them to prevent re-execution.
*   **[x] Persistence Scrubbing:** Automatically cleans Registry `Run` keys and `Startup` folders associated with a detected threat.

## 4. Emergency Panic Mode
*   **[x] Network Lockdown:** Ability to instantly disable all network adapters to prevent data exfiltration (stealing files) or communication with Command & Control servers.
*   **[x] System Shutdown:** Optional user-enabled feature to force-shutdown the PC if a massive encryption event is detected and cannot be stopped in time.
*   **[x] Shield-Up Alert:** A full-screen, high-impact UI overlay that appears during an attack, requiring explicit user action to restore network or system access.

## 5. Deployment & Stealth
*   **[x] Silent Admin Startup:** Registration via Windows Task Scheduler to bypass UAC prompts, allowing the Dashboard to start with full privileges at login.
*   **[x] Self-Healing Service:** The Windows Service is configured to "Restart on Failure" to resist termination attempts by malware.

---
**Status:** Fully Implemented & Verified.
**Updated:** 2026-04-13
