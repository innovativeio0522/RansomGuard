# RansomGuard – Future Fix Backlog

Identified during the Sentinel Design System (SDS) v4.2.0 audit on 2026-04-12. Items are ordered by priority.

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
