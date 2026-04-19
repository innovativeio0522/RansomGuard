# RansomGuard – Future Fix Backlog

Identified during the Sentinel Design System (SDS) v4.2.0 audit on 2026-04-12 and the Performance & Security Audit on 2026-04-14. Items are ordered by priority.

---

## 🔴 Should Fix (Visible Quality Gaps)

### 1. ✅ Dark Tooltip Style
- **Status**: COMPLETED
- **File**: `RansomGuard/Resources/Styles/ControlStyles.xaml`
- **Issue**: WPF default tooltip was white/yellow.
- **Fix**: Added global style with `SurfaceContainerHighBrush`, 8px `CornerRadius`, and `DropShadowEffect`.

### 2. ✅ Verify `TextButtonStyle` Exists
- **Status**: COMPLETED
- **File**: `RansomGuard/Resources/Styles/ButtonStyles.xaml`
- **Fix**: Audited and confirmed existence; ensured `Cursor="Hand"` is applied.

### 3. ✅ `TextStatusThreat` Color Mismatch
- **Status**: COMPLETED
- **File**: `RansomGuard/Resources/Styles/TextStyles.xaml`
- **Fix**: Standardized `TextStatusThreat` (High) and `TextStatusWarning` (Low/Medium) styles.

---

## 🟡 Nice to Have (Polish)

### 4. ✅ Hover Cursor on Interactive Elements
- **Status**: COMPLETED
- **Fix**: Added `Cursor="Hand"` to all button and toggle styles.

### 5. ✅ Audit `OnboardingView.xaml`
- **Status**: COMPLETED
- **Fix**: SDS pass completed (8px radii, border removal, standardized resources).

### 6. ✅ FileActivity & ProcessMonitor Border Audit
- **Status**: COMPLETED
- **Fix**: Implemented No-Line rule & Zebra striping in both views.

### 7. ✅ ToggleSwitch Hover/Focus States
- **Status**: COMPLETED
- **Fix**: Added thumb scaling and refined focus feedback.

---

## 🔐 Performance & Security Optimization (2026-04-14)

### 8. ✅ IPC Thread Safety & Async Broadcasting
- **Status**: COMPLETED
- **File**: `RansomGuard.Service/Communication/NamedPipeServer.cs`
- **Issue**: Synchronous writes from multiple threads can corrupt the pipe.
- **Goal**: Implement a queued, single-writer pattern for IPC.

### 9. ✅ UI Throttling for High Activity
- **Status**: COMPLETED
- **File**: `ViewModels/DashboardViewModel.cs`
- **Issue**: UI thread can freeze during mass file activity events.
- **Goal**: Implement buffered updates (250ms batching) for file activity UI.

### 10. ✅ Missing IPC Command Handlers
- **Status**: COMPLETED
- **File**: `NamedPipeServer.cs`, `IpcModels.cs`
- **Issue**: `ToggleShield` and `UpdatePaths` commands are sent but not handled by service.
- **Goal**: Complete the command loop implementation.

### 11. ✅ Quarantine Command Mismatch
- **Status**: COMPLETED
- **File**: `Services/ServicePipeClient.cs`
- **Issue**: `QuarantineFile` mistakenly sends `ToggleShield` command.
- **Goal**: Align client and server command enums and handling.

### 12. ✅ Real Shannon Entropy Calculation
- **Status**: COMPLETED
- **File**: `RansomGuard.Service/Engine/SentinelEngine.cs`
- **Issue**: Entropy is currently simulated in the UI.
- **Goal**: Implement real byte-level entropy check for suspicious files in the service.

### 13. ✅ Tune Mass-Change Thresholds
- **Status**: COMPLETED
- **File**: `SentinelEngine.cs`
- **Issue**: `ChangeThreshold = 15` is too low (false positive risk).
- **Goal**: Increase threshold and optimize detection window.

### 14. ✅ Periodic Threat Cache Cleanup
- **Status**: COMPLETED
- **File**: `SentinelEngine.cs`
- **Issue**: `_reportedThreats` HashSet never clears (minor memory leak).
- **Goal**: Implement 24-hour cleanup for the deduplication set.

---

## 🔬 Deep Code Audit (Round 3 – 2026-04-14)

### 15. ✅ Event Handler Leak in `MainViewModel.Dispose()`
- **Status**: COMPLETED
- **File**: `ViewModels/MainViewModel.cs`
- **Issue**: Unsubscribing with a new lambda instance — C# event unsubscription requires the *same delegate reference*. The handler is never actually removed, causing a memory/event leak.
- **Goal**: Store the handler in a field and unsubscribe using that field.

### 16. ✅ Blocking Async Call (Deadlock Risk) in `ConfigurationService.Save()`
- **Status**: COMPLETED
- **File**: `RansomGuard.Core/Services/ConfigurationService.cs`
- **Issue**: `File.WriteAllTextAsync(...).GetAwaiter().GetResult()` inside a `lock {}` block. Can deadlock on a thread-pool starved scheduler.
- **Goal**: Replace with synchronous `File.WriteAllText`.

### 17. ✅ `QuarantineViewModel` Shows Wrong Data
- **Status**: COMPLETED
- **File**: `ViewModels/QuarantineViewModel.cs`
- **Issue**: `LoadData()` populates `QuarantinedItems` from `GetRecentThreats()` — it shows threat history, not actual quarantined disk files.
- **Goal**: Use `GetQuarantinedFiles()` to populate the list from the actual quarantine directory.

### 18. ✅ Missing `InitializeWatchers()` on `ISystemMonitorService` Interface
- **Status**: COMPLETED
- **File**: `RansomGuard.Core/Interfaces/ISystemMonitorService.cs`, `NamedPipeServer.cs`
- **Issue**: `NamedPipeServer.HandleCommand` calls `_monitorService.InitializeWatchers()` but this method is not on the interface — compile error risk.
- **Goal**: Add `InitializeWatchers()` to the interface and implement a no-op on `ServicePipeClient`.

### 19. ✅ Race Condition on `_threatHistory` / `_activityHistory` in `SentinelEngine`
- **Status**: COMPLETED
- **File**: `RansomGuard.Service/Engine/SentinelEngine.cs`
- **Issue**: `GetRecentThreats()` and `GetRecentFileActivities()` read lists without acquiring `_historyLock`, while writes to those same lists do hold the lock.
- **Goal**: Wrap the reads in `lock (_historyLock)`.

### 20. ✅ `FileActivity.Entropy` Never Populated in `SentinelEngine`
- **Status**: COMPLETED
- **File**: `SentinelEngine.cs`, `RansomGuard.Core/Models/FileActivity.cs`
- **Issue**: `CalculateShannonEntropy()` is called but the result is only stored in `_lastEntropyScore`. The `FileActivity.Entropy` property remains `0`, so the FileActivity view always shows zero entropy.
- **Goal**: Set `activity.Entropy = entropy` in `OnFileChanged`.

### 21. ✅ `FileActivityViewModel` UI Thread Saturation (Same as #9)
- **Status**: COMPLETED
- **File**: `ViewModels/FileActivityViewModel.cs`
- **Issue**: Every single file event causes a `Dispatcher.Invoke` — identical to the bug fixed in `DashboardViewModel`. This second view was missed.
- **Goal**: Apply the same `ConcurrentQueue` + buffer timer pattern as `DashboardViewModel`.

### 22. ✅ `ReportsViewModel` Scan Count is Incorrectly Estimated
- **Status**: COMPLETED
- **File**: `ViewModels/ReportsViewModel.cs`
- **Issue**: `TotalScans` is computed as `daysSinceFirstScan` from the *last scan time*, giving a misleading count (e.g., 1 day after scanning = 1 scan, which may be wrong).
- **Goal**: Persist a proper `TotalScansCount` in `ConfigurationService` and increment it on each scan.

### 23. ✅ Blocking Async Call (Deadlock Risk) in `HoneyPotService`
- **Status**: COMPLETED
- **File**: `RansomGuard.Service/Engine/HoneyPotService.cs`
- **Issue**: Identical to #16, calling `File.WriteAllTextAsync().GetAwaiter().GetResult()` synchronously can cause severe thread-pool starvation during service startup.
- **Goal**: Replaced with synchronous `File.WriteAllText`.

---


## 📋 Summary Table

| # | Priority | Item | File |
|---|---|---|---|
| 1 | ✅ | Dark Tooltip Style | `ControlStyles.xaml` |
| 2 | ✅ | Verify `TextButtonStyle` | `ButtonStyles.xaml` |
| 3 | ✅ | `TextStatusThreat` color hierarchy | `TextStyles.xaml` |
| 4 | ✅ | `Cursor="Hand"` on all buttons | All button styles |
| 5 | ✅ | Audit `OnboardingView` | `OnboardingView.xaml` |
| 6 | ✅ | FileActivity/ProcessMonitor border audit | `FileActivityView.xaml`, `ProcessMonitorView.xaml` |
| 7 | ✅ | ToggleSwitch hover/focus states | `ControlStyles.xaml` |
| 8 | ✅ | IPC Thread Safety | `NamedPipeServer.cs` |
| 9 | ✅ | UI Throttling (Dashboard) | `DashboardViewModel.cs` |
| 10 | ✅ | IPC Command Handlers | `NamedPipeServer.cs` |
| 11 | ✅ | Quarantine Mismatch | `ServicePipeClient.cs` |
| 12 | ✅ | Shannon Entropy | `SentinelEngine.cs` |
| 13 | ✅ | Mass-Change Tuning | `SentinelEngine.cs` |
| 14 | ✅ | Cache Cleanup | `SentinelEngine.cs` |
| 15 | ✅ | Event Handler Leak | `MainViewModel.cs` |
| 16 | ✅ | Blocking Async / Deadlock Risk | `ConfigurationService.cs` |
| 17 | ✅ | Quarantine Shows Wrong Data | `QuarantineViewModel.cs` |
| 18 | ✅ | Missing Interface Member | `ISystemMonitorService.cs` |
| 19 | ✅ | Race Condition on History Lists | `SentinelEngine.cs` |
| 20 | ✅ | `FileActivity.Entropy` Always Zero | `SentinelEngine.cs` |
| 21 | ✅ | UI Thread Saturation (FileActivity) | `FileActivityViewModel.cs` |
| 22 | ✅ | Scan Count Miscalculation | `ReportsViewModel.cs` |
| 23 | ✅ | Deadlock Risk in HoneyPotService | `HoneyPotService.cs` |
| 24 | ✅ | Hardcoded Drive Paths | `ConfigurationService.cs`, `SentinelEngine.cs` |
| 25 | ✅ | Sensitivity Level Disconnected from Heuristics | `SentinelEngine.cs`, `SettingsViewModel.cs` |
| 26 | ✅ | Export to CSV / PDF Not Implemented | `ReportsViewModel.cs` |
| 27 | ✅ | Exponential Backoff for IPC Reconnect | `ServicePipeClient.cs` |
| 28 | ✅ | `GetTelemetry()` Calls `Process.GetProcesses()` Every 2s | `SentinelEngine.cs` |
| 29 | ✅ | SentinelEngine God Object — Extract Sub-Services | `SentinelEngine.cs` |
| 30 | ✅ | `ReportsViewModel` Missing `IDisposable` | `ReportsViewModel.cs` |
| 31 | ✅ | `random` Allocated Per Call in `GetActiveProcesses()` | `SentinelEngine.cs` |
| 32 | ✅ | `CheckSuspiciousExtension` / `IsMediaFile` Use Inline Arrays | `SentinelEngine.cs` |
| 33 | ✅ | `NamedPipeServer` Unsafe Cast to `SentinelEngine` | `NamedPipeServer.cs` |

---

## 🔬 Deep Code Audit (Round 4 – 2026-04-18)

Items identified during the April 18, 2026 full-codebase walkthrough.

### 24. ✅ Hardcoded Drive Paths Cause Breakage on Other Machines
- **Status**: COMPLETED
- **Files**: `RansomGuard.Core/Services/ConfigurationService.cs`, `SentinelEngine.cs`
- **Issue**: `ConfigurationService.PopulateDefaultFolders()` and `IsStandardProtectedFolder()` both contain hardcoded paths:
  ```
  MonitoredPaths.Add(@"F:\Github Projects\RansomGuard");
  standardFolders.Add(@"F:\Github Projects\RansomGuard");
  ```
  This fallback path only exists on the original developer's machine. On any other machine the application will attempt to monitor a non-existent directory and fail silently.
- **Fix**: Replace with `AppDomain.CurrentDomain.BaseDirectory` or `Environment.CurrentDirectory`.

### 25. ✅ Sensitivity Level Setting is Wired to UI but Ignored by the Security Engine
- **Status**: COMPLETED
- **Files**: `RansomGuard.Service/Engine/SentinelEngine.cs`, `ViewModels/SettingsViewModel.cs`
- **Issue**: `ConfigurationService.SensitivityLevel` is stored (1=Low … 4=Paranoid) and persisted, but `SentinelEngine.OnFileChanged()` uses **hardcoded thresholds** (`6.0` for normal files, `7.5` for media). Changing the sensitivity slider in Settings has zero effect on actual detection behaviour.
- **Fix**: Derive the entropy threshold from `SensitivityLevel`:
  ```
  Level 1 (Low)     → threshold = 7.2
  Level 2 (Medium)  → threshold = 6.5
  Level 3 (High)    → threshold = 6.0  (current default)
  Level 4 (Paranoid)→ threshold = 5.0
  ```
  Also update `ChangeThreshold` for `CheckMassChangeVelocity()` accordingly.

### 26. ✅ Export to CSV / PDF Buttons Have No Implementation
- **Status**: COMPLETED — CSV export fully implemented. PDF deferred pending QuestPDF dependency (shows friendly prompt offering CSV instead).
- **File**: `ViewModels/ReportsViewModel.cs`
- **Issue**: The "Export to CSV" and "Export to PDF" relay commands contain empty bodies. The buttons render correctly but clicking them does nothing.
- **Fix**:
  - **CSV**: Use `System.Text.StringBuilder` to serialize threat history and file activity; write to `%AppData%/RansomGuard/Exports/report_<datetime>.csv`.
  - **PDF**: Use a lightweight PDF library (e.g., `QuestPDF` or `PdfSharp`) to generate a formatted report card.

### 27. ✅ IPC Reconnection Uses a Fixed 2-Second Delay (No Exponential Backoff)
- **Status**: COMPLETED — Implemented exponential backoff with ±10% jitter (2s → 4s → 8s → 16s → cap 30s). Resets to 2s on successful reconnect.
- **File**: `Services/ServicePipeClient.cs` – `ConnectLoop()`
- **Issue**: When the background service is absent, `ConnectLoop` will spin and fail every 2 seconds indefinitely, creating unnecessary CPU overhead and log noise.
- **Fix**: Implement exponential backoff with jitter (e.g., 2s → 4s → 8s → 16s → cap at 30s) and reset on successful connection.

### 28. ✅ `GetTelemetry()` Calls `Process.GetProcesses()` on Every Tick
- **Status**: COMPLETED — Process/thread stats are now cached in 4 fields (`_cachedProcessCount`, `_cachedThreadCount`, `_cachedTrustedPercent`, `_cachedSuspiciousCount`) and updated inside the existing 2s telemetry timer. `GetTelemetry()` reads the cache — zero extra OS calls per IPC broadcast.
- **File**: `RansomGuard.Service/Engine/SentinelEngine.cs` – `GetTelemetry()`
- **Issue**: `GetTelemetry()` is called on a 2-second timer via `TelemetryBroadcastLoop`. Each call calls `Process.GetProcesses()` to count threads — a heavy OS call that is also being duplicated by `GetActiveProcesses()` in a separate 3-second loop.
- **Fix**: Cache thread/process count in a shared field updated by the existing telemetry timer. Remove the duplicate `Process.GetProcesses()` call from `GetTelemetry()`.

### 29. ✅ `SentinelEngine` is a God Object (850+ Lines)
- **Status**: COMPLETED — Extracted three dedicated sub-services:
  - `EntropyAnalysisService.cs` — Shannon entropy, extension & rename-pattern checks (uses `HashSet` for O(1) lookup, also fixes #32)
  - `ProcessIdentityService.cs` — `DetermineProcessIdentity()` logic
  - `QuarantineService.cs` — all quarantine I/O (isolate, restore, delete, purge)
  - `SentinelEngine` delegates to all three and acts as a pure orchestrator
  - Also fixed #31: replaced `new Random()` per-call with `Random.Shared`
- **File**: `RansomGuard.Service/Engine/SentinelEngine.cs`
- **Issue**: The `SentinelEngine` class manages: file-system watchers, CPU/memory counters, Shannon entropy calculation, process identity checks, heuristic threat scoring, quarantine I/O, VSS, and telemetry serialization — all in one 867-line class.
- **Fix**: Extract into focused sub-services:
  - `FileMonitorService` — Manages `FileSystemWatcher` instances.
  - `ProcessIdentityService` — `DetermineProcessIdentity()` logic.
  - `EntropyAnalysisService` — `CalculateShannonEntropy()` logic.
  - `QuarantineService` — Quarantine/restore/delete operations.
  `SentinelEngine` becomes an orchestrator only.

### 30. 🟡 `ReportsViewModel` Does Not Implement `IDisposable` — Event Leak Risk
- **Status**: PENDING
- **File**: `ViewModels/ReportsViewModel.cs`
- **Issue**: `ReportsViewModel` subscribes to telemetry events from `ISystemMonitorService` but never unsubscribes. When the user navigates away and back, a new subscription can be created on top of the old one (in `Refresh()`), leading to duplicate UI updates and a potential memory leak over time.
- **Fix**: Implement `IDisposable`, unsubscribe from all events in `Dispose()`. Have `MainViewModel.Dispose()` call it.

### 31. 🔵 `new Random()` Allocated Per Iteration Inside `GetActiveProcesses()`
- **Status**: PENDING
- **File**: `RansomGuard.Service/Engine/SentinelEngine.cs` – Line ~486
- **Issue**: `IoRate = Math.Round(new Random().NextDouble() * 5, 2)` allocates a new `Random` instance for every process on every 3-second broadcast. This also means all processes may get the same `IoRate` value (since consecutive `Random` instances seeded by the system clock can produce identical sequences).
### 30. ✅ `ReportsViewModel` Misses Event Unsubscription During Dispose
- **Status**: COMPLETED — MainViewModel now correctly casts and calls `Dispose()` on the `_reportsVM` instance during app shutdown.
- **File**: `ViewModels/MainViewModel.cs`, `ViewModels/ReportsViewModel.cs`
- **Issue**: `ReportsViewModel` declares `IDisposable` and implements `Dispose()` to unsubscribe from service events, but `MainViewModel` fails to call it alongside the other child ViewModels.
- **Fix**: Added `(_reportsVM as IDisposable)?.Dispose();` to `MainViewModel.Dispose()`.

### 31. ✅ `new Random()` Allocation Inside Heavy Logging Loop
- **Status**: COMPLETED — Replaced with `Random.Shared.NextDouble()` during the God Object extraction (#29).
- **File**: `RansomGuard.Service/Engine/SentinelEngine.cs` — `GetActiveProcesses()`
- **Issue**: `var random = new Random();` is allocated every 3 seconds inside `GetActiveProcesses()`. Inside the inner `Select` block, `new Random().NextDouble()` is allocated *again* for every single process on the system. High GC pressure.
- **Fix**: Use `Random.Shared` everywhere.

### 32. ✅ `CheckSuspiciousExtension` and `IsMediaFile` Use Inline Arrays
- **Status**: COMPLETED — Replaced with static readonly `HashSet<string>` with `StringComparer.OrdinalIgnoreCase` inside the newly extracted `EntropyAnalysisService` (#29).
- **File**: `RansomGuard.Service/Engine/SentinelEngine.cs`
- **Issue**: These methods are called on *every single file modification event*. Inside, they declare `string[] blocked = { ... }` which allocates a new array on every call, followed by a linear `Contains()` scan.
- **Fix**: Moved to static `HashSet<string>` fields within a central heuristic configuration object.

### 33. ✅ `NamedPipeServer` Unsafely Casts Interface to Concrete `SentinelEngine`
- **Status**: COMPLETED — Removed the unsafe cast since `ScanCompleted` is a native member of `ISystemMonitorService`.
- **File**: `RansomGuard.Service/Communication/NamedPipeServer.cs`
- **Issue**: At line 52: `(_monitorService as SentinelEngine)!.ScanCompleted += ...`. If the service is ever mocked or wrapped (e.g. for testing), this will throw a NullReferenceException.
- **Fix**: `ScanCompleted` is already part of the `ISystemMonitorService` interface. Call it directly on the interface.

---

## 🔬 Deep Code Audit (Round 5 – Best Practices Review)

### 34. 🔴 15 Instances of Empty Catch Blocks Remain (Silent Failures)
- **Status**: PENDING
- **Files**: `ServicePipeClient.cs`, `QuarantineViewModel.cs`, `ProcessMonitorViewModel.cs`, `SentinelEngine.cs`, `ConfigurationService.cs`, `NativeMemory.cs`
- **Issue**: Despite previous cleanup, 15 instances of `catch { }` still exist, silently swallowing exceptions. This makes debugging catastrophic failures extremely difficult.
- **Goal**: Inject an `ILogger` implementation or use existing telemetry infrastructure to log these exceptions instead of swallowing them.

### 35. ✅ Blocking Async Call / Deadlock Risk
- **Status**: COMPLETED - Converted synchronous `.Wait()` to `await` inside an `async Task.Run` delegate.
- **File**: `RansomGuard.Service/Engine/QuarantineService.cs`
- **Issue**: Line 126 calls `DeleteQuarantinedFile(file).Wait();`. Using `.Wait()` on an async method can cause thread-pool starvation and deadlocks, especially in background services.
- **Goal**: Convert the calling method to `async Task` and use `await DeleteQuarantinedFile(file);`.

### 36. ✅ The "Shadow Engine" Anti-Pattern (SRP Violation)
- **Status**: COMPLETED - Ripped ~350 lines of fallback logic from `ServicePipeClient.cs`. The UI is now a thin client.
- **File**: `Services/ServicePipeClient.cs`

### 37. ✅ Tight Coupling & Static Dependencies
- **Status**: COMPLETED - Implemented `IEntropyAnalyzer` and `IProcessIdentityClassifier` and injected them into `SentinelEngine` via constructor DI.
- **File**: `RansomGuard.Service/Engine/SentinelEngine.cs`
- **Goal**: Define interfaces (`IEntropyAnalyzer`, `IProcessIdentityClassifier`) and inject them into `SentinelEngine` via the constructor (Dependency Injection).

### 38. ✅ Missing `ConfigureAwait(false)` in Core/Service Libraries
- **Status**: COMPLETED - Appended `.ConfigureAwait(false)` to all `await` calls in `RansomGuard.Service` across five major components (Worker, Engine, Communication, Services).
- **Files**: `RansomGuard.Core` and `RansomGuard.Service`
- **Goal**: Append `.ConfigureAwait(false)` to all `await` calls in UI-agnostic projects to prevent capturing the synchronization context, improving performance and mitigating deadlock risks.

### 39. ✅ Sub-Optimal `async void` Usage
- **Status**: COMPLETED - Wrapped the `OnStartup` async logic in a global `try/catch` block within `App.xaml.cs` to ensure fatal initialization errors are reported before the process exits.
- **File**: `App.xaml.cs`

### 40. ✅ Magic Strings in System Health Assessment
- **Status**: COMPLETED - Implemented `AuthenticodeVerifier` using native `WinVerifyTrust` and integrated it into `ProcessIdentityService`.
- **File**: `RansomGuard.Service/Engine/ProcessIdentityService.cs`
- **Goal**: Check valid Authenticode digital signatures to reliably identify native Microsoft executables instead of relying on string matching.
