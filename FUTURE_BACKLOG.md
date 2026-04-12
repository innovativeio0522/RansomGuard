# RansomGuard – Future Fix Backlog

Identified during the Sentinel Design System (SDS) v4.2.0 audit on 2026-04-12. Items are ordered by priority.

---

## 🔴 Should Fix (Visible Quality Gaps)

### 1. Dark Tooltip Style
- **File**: `RansomGuard/Resources/Styles/ControlStyles.xaml`
- **Issue**: WPF default tooltip is white/yellow and completely breaks the dark Sentinel theme. Every `ToolTip="..."` in the app shows the system default popup.
- **Fix**: Add a global `<Style TargetType="ToolTip">` with `SurfaceContainerHighBrush` background, `OnSurfaceBrush` text, 8px `CornerRadius`, and a subtle `DropShadowEffect`.

### 2. Verify `TextButtonStyle` Exists
- **File**: `RansomGuard/Resources/Styles/ButtonStyles.xaml`
- **Issue**: `TextButtonStyle` is referenced in `SettingsView.xaml` (Documentation card links) but was not confirmed to exist in `ButtonStyles.xaml`. WPF silently falls back to the default button style, causing visual inconsistency.
- **Fix**: Audit `ButtonStyles.xaml` and add `TextButtonStyle` if missing — a transparent background button with `OnSurfaceVariant` text and a subtle underline-on-hover effect.

### 3. `TextStatusThreat` Color Mismatch
- **File**: `RansomGuard/Resources/Styles/TextStyles.xaml` (Line 65)
- **Issue**: Uses `TertiaryContainerBrush` (orange/amber) for threat status text. This is the "High Severity" accent — but "Low" threat items also use this style with the same bright orange, reducing visual hierarchy.
- **Fix**: Add a `TextStatusWarning` style using `TertiaryBrush` (softer) for low/medium items, and reserve `TertiaryContainerBrush` for critical/high only.

---

## 🟡 Nice to Have (Polish)

### 4. Hover Cursor on Interactive Elements
- **Files**: All view `.xaml` files
- **Issue**: Icon buttons and clickable cards don't set `Cursor="Hand"`. On a precision-tool UI this feels unfinished.
- **Fix**: Add `Cursor="Hand"` to `IconButton`, `PrimaryButton`, `SecondaryButton`, and `TextButtonStyle` in `ButtonStyles.xaml` globally.

### 5. Audit `OnboardingView.xaml`
- **File**: `RansomGuard/Views/OnboardingView.xaml` (19.8 KB)
- **Issue**: Not yet audited against SDS — likely carries old-generation design patterns (explicit borders, thick shadows, non-Sentinel typography).
- **Fix**: Full SDS pass — No-Line rule, typography standardization, icon weight normalization.

### 6. FileActivity & ProcessMonitor Border Audit
- **Files**: `FileActivityView.xaml`, `ProcessMonitorView.xaml`
- **Issue**: Were modernized in a previous session but may still contain 1px explicit border strokes on row separators — violating the No-Line rule.
- **Fix**: Replace `BorderThickness="0,1,0,0"` row separators with alternating background depth shifts (`SurfaceContainerLow` ↔ `SurfaceContainer`).

### 7. ToggleSwitch Hover/Focus States
- **File**: `RansomGuard/Resources/Styles/ControlStyles.xaml`
- **Issue**: The `ToggleSwitchStyle` has no explicit `IsMouseOver` or `IsFocused` state beyond the checked animation. The thumb doesn't give visual feedback on hover.
- **Fix**: Add a subtle `SurfaceVariantBrush` glow or scale transform on `IsMouseOver`.

---

## 📋 Summary Table

| # | Priority | Item | File |
|---|---|---|---|
| 1 | 🔴 | Dark Tooltip Style | `ControlStyles.xaml` |
| 2 | 🔴 | Verify `TextButtonStyle` | `ButtonStyles.xaml` |
| 3 | 🔴 | `TextStatusThreat` color hierarchy | `TextStyles.xaml` |
| 4 | 🟡 | `Cursor="Hand"` on all buttons | All button styles |
| 5 | 🟡 | Audit `OnboardingView` | `OnboardingView.xaml` |
| 6 | 🟡 | FileActivity/ProcessMonitor border audit | `FileActivityView.xaml`, `ProcessMonitorView.xaml` |
| 7 | 🟡 | ToggleSwitch hover/focus states | `ControlStyles.xaml` |
