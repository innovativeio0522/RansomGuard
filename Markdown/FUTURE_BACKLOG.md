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
