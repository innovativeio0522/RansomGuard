# RansomGuard WPF Project Structure

## Project Overview
- **Framework**: .NET 8 WPF
- **Architecture**: MVVM (CommunityToolkit.Mvvm)
- **Theme**: Dark (Reference screen exact colors)
- **Target**: Windows Desktop (1280x800 minimum)

## Color Scheme (Exact from Reference Screens)
```
Background:           #0f131d
Surface Container:    #171b26, #1c1f2a, #262a35, #313540
Primary (Blue):       #adc6ff
Secondary (Green):    #4edea3 (Safe/Protected status)
Tertiary (Red):       #ff5451, #ffb3ad (Threats/Danger)
Text:                 #dfe2f1 (primary), #c2c6d6 (variant)
Outline:              #424754
```

## Project Structure
```
RansomGuard/
├── RansomGuard.csproj          # .NET 8 WPF project file
├── app.manifest                 # Admin privileges + DPI awareness
├── App.xaml                     # Application entry point + resources
├── App.xaml.cs                  # Application logic
│
├── Resources/
│   └── Styles/
│       ├── Colors.xaml          # Color definitions
│       ├── Brushes.xaml         # SolidColorBrush definitions
│       ├── TextStyles.xaml      # TextBlock styles
│       ├── ButtonStyles.xaml    # Button styles (flat, native)
│       └── ControlStyles.xaml   # Other control styles
│
├── Views/
│   ├── MainWindow.xaml          # Main shell with sidebar, header, statusbar
│   ├── MainWindow.xaml.cs
│   ├── DashboardView.xaml       # Screen 1
│   ├── ThreatAlertsView.xaml    # Screen 2
│   ├── QuarantineView.xaml      # Screen 3
│   ├── ProcessMonitorView.xaml  # Screen 4
│   ├── FileActivityView.xaml    # Screen 5
│   ├── ReportsView.xaml         # Screen 6
│   ├── SettingsView.xaml        # Screen 7
│   └── OnboardingView.xaml      # Screen 8
│
├── ViewModels/
│   ├── ViewModelBase.cs         # Base ViewModel class
│   ├── MainViewModel.cs
│   ├── DashboardViewModel.cs
│   ├── ThreatAlertsViewModel.cs
│   ├── QuarantineViewModel.cs
│   ├── ProcessMonitorViewModel.cs
│   ├── FileActivityViewModel.cs
│   ├── ReportsViewModel.cs
│   ├── SettingsViewModel.cs
│   └── OnboardingViewModel.cs
│
├── Models/                      # Data models
│   ├── Threat.cs
│   ├── Process.cs
│   ├── FileActivity.cs
│   ├── QuarantineItem.cs
│   └── Report.cs
│
├── Services/                    # Business logic services
│   ├── FileMonitorService.cs
│   ├── ProcessMonitorService.cs
│   ├── ThreatDetectionService.cs
│   ├── QuarantineService.cs
│   ├── DatabaseService.cs       # SQLite operations
│   └── YaraRuleService.cs
│
└── Assets/
    └── Icons/
        └── RansomGuard.ico      # Application icon
```

## NuGet Packages
- **LiveChartsCore.SkiaSharpView.WPF**: Charts and graphs
- **Microsoft.Data.Sqlite**: Embedded database
- **CommunityToolkit.Mvvm**: MVVM helpers
- **SharpYara**: YARA rule engine
- **BouncyCastle.Cryptography**: Entropy analysis

## UI Design Rules
- Font: Segoe UI ONLY (11-12px body, 13px headers)
- Layout: Dense tables, narrow sidebar (64px), menu bar, status bar
- Row Height: 22-24px compact rows
- Buttons: Flat native-style (NO pill/rounded/web buttons)
- Charts: Small inline only
- No Web Elements: No gradients, hero images, floating cards

## Next Steps
1. Create individual View XAML files (8 screens)
2. Create ViewModels for each view
3. Implement navigation system
4. Create data models
5. Implement core services
