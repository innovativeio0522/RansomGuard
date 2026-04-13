# 🛡️ RansomGuard Pro
**Military-Grade Ransomware Defense for Windows**

RansomGuard Pro is a multi-tier, proactive security engine designed to detect, block, and mitigate ransomware attacks in real-time. Unlike traditional signature-based antivirus, RansomGuard uses behavioral heuristics and proactive "Canary" traps to stop unknown threats before they can encrypt your data.

## 🚀 Key Features
- **Sentinel Background Service**: A dedicated background engine that runs with `SYSTEM` privileges, ensuring protection starts even before you log in.
- **Behavioral Detection**: Monitors file activity velocity; triggers a critical response if mass-encryption is detected (>15 modifications per 5 seconds).
- **Proactive Honey Pots**: Deploys hidden canaries in your files. Any interaction with these files instantly isolates the source process.
- **VSS Shield**: Actively protects Windows Volume Shadow Copies from deletion, preserving your "Time Machine" recovery options.
- **Emergency Panic Mode**: Supports instant network lockdown and emergency system power-off to save data during a catastrophic breach.
- **Sentinel UI**: A modern, high-fidelity Dashboard built on the **InnovativeSentinel SDS 4.2.0** design system.

## 🏗️ Architecture
The solution is built with a decoupled architecture for maximum stability and security:
- **RansomGuard.Service**: The "Muscle" – handles low-level heuristics and automated response.
- **RansomGuard.Dashboard**: The "Brain" – a reactive WPF interface for monitoring and control.
- **RansomGuard.Core**: The "Link" – shared models and high-performance IPC (Named Pipes) bridge.

## 🛠️ Requirements
- Windows 10/11
- .NET 8.0 Runtime
- Administrative Privileges (for Service Installation and Network Lockdown)

## 📦 Installation
1. Build the solution using `dotnet build`.
2. Launch `RansomGuard.exe` as Administrator.
3. Navigate to **Settings** > **System Maintenance**.
4. Click **INSTALL SERVICE** to register the background sentinel.

---
*Developed with the goal of providing zero-compromise, proactive data protection.*
