# RansomGuard Pro Project Structure

## Project Overview
- **Framework**: .NET 8 (WPF + Windows Service)
- **Architecture**: Three-Tier Sentinel Architecture
- **Design System**: InnovativeSentinel SDS 4.2.0 (No-Line, Depth-based)
- **Security Grade**: Proactive (Honey Pot, VSS Shield, Behavioral Analysis)

## Project Structure
The solution is organized into three specialized projects to separate the high-privileged detection engine from the user interface.

```
RansomGuard.sln
│
├── RansomGuard.Core/            # Shared logic, interfaces, and models
│   ├── Interfaces/              # ISystemMonitorService
│   ├── Models/                  # Threat, FileActivity, ProcessInfo
│   └── IPC/                     # IpcModels, TelemetryData
│
├── RansomGuard.Service/         # Background Windows Service (The "Sentinel")
│   ├── Engine/                  # Heuristic engines
│   │   ├── SentinelEngine.cs    # Core detection & Telemetry
│   │   ├── HoneyPotService.cs   # Trap/Bait management
│   │   └── VssShieldService.cs  # Shadow copy protection
│   ├── Communication/           # NamedPipeServer (IPC Hub)
│   └── Worker.cs                # Service entry & orchestration
│
└── RansomGuard/                 # Main WPF Dashboard (The "Control Center")
    ├── Services/                # ServicePipeClient (IPC Bridge)
    ├── ViewModels/              # MVVM logic wired to IPC
    ├── Views/                   # Sentinel Design System UI
    └── Resources/               # Global styles & themes
```

## Proactive Defense Systems
- **Honey Pot Trap**: Deploys hidden bait files in User documents to catch encryption earlier.
- **VSS Shield**: Monitors the Volume Shadow Copy service to prevent local backup deletion.
- **Behavioral Analysis**: Monitors file modification velocity to catch mass-encryption in progress (>15 changes/5s).
- **Active Response**: Automatic process termination, quarantine, and network lockdown.

## IPC Communication (Named Pipes)
The Dashboard communicates with the Sentinel service via a high-performance Named Pipe bridge.
- **Heartbeat**: 2-second telemetry push (CPU, Memory, Shield Status, Storage).
- **Events**: Real-time push of File Activities and Threat Alerts.
- **Commands**: Admin commands from UI (Scan, Kill, Service Install).

## Color Scheme (Sentinel SDS 4.2.0)
```
Background:           #0f131d
Surface Container:    #171b26, #1c1f2a, #262a35, #313540
Primary (Blue):       #adc6ff
Secondary (Green):    #4edea3 (Safe/Protected status)
Tertiary (Red):       #ff5451, #ffb3ad (Threats/Danger)
Text:                 #dfe2f1 (primary), #c2c6d6 (variant)
Outline:              #424754
```

## NuGet Dependencies
- `CommunityToolkit.Mvvm`: MVVM orchestration.
- `System.Management`: Windows management API integration.
- `System.IO.Pipes`: IPC communication.
- `Microsoft.Extensions.Hosting`: Service lifecycle management.
- `LiveChartsCore.SkiaSharpView.WPF`: Dashboard telemetry visualization.
