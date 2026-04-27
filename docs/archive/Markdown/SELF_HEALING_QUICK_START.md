# Self-Healing Quick Start Guide

## 🚀 Quick Fix (3 Steps)

### Step 1: Rebuild with Updated Script
```bash
.\build-and-run.bat
```

### Step 2: Verify Installation
```powershell
.\verify-installation.ps1
```

### Step 3: Check Self-Healing Status
```powershell
.\check-selfhealing.ps1
```

---

## ✅ Expected Results

### After Step 1 (Build)
```
[2.5/5] Building and copying Watchdog...
[+] Watchdog copied successfully
```

### After Step 2 (Verify)
```
[1] Checking Required Files...
    ✓ RGUI.exe - 1234.56 KB
    ✓ RGWorker.exe - 234.56 KB  ← This should be present now
    ✓ RansomGuard.Core.dll - 456.78 KB

✓ All required files are present!
```

### After Step 3 (Check)
```
[1] Checking Watchdog Process...
    ✓ Watchdog is RUNNING (PID: 12345)

[2] Checking UI Process...
    ✓ UI is RUNNING (PID: 67890)
```

---

## 🧪 Test Self-Healing

### Test 1: Kill UI and Watch It Restart
```bash
# Kill the UI
taskkill /IM RGUI.exe /F

# Wait 5-10 seconds
# UI should automatically restart!
```

### Test 2: View Watchdog Activity
```powershell
# See what Watchdog is doing
Get-Content "$env:ProgramData\RansomGuard\Logs\watchdog.log" -Tail 10
```

---

## 📚 Need More Help?

- **Detailed Guide**: `Markdown/SELF_HEALING_TROUBLESHOOTING.md`
- **Fix Summary**: `Markdown/SELF_HEALING_FIX_SUMMARY.md`
- **Diagnostic Script**: `.\check-selfhealing.ps1`

---

## 🎯 What Was Fixed?

**Problem:** RGWorker.exe (Watchdog) was not being built during development

**Solution:** Updated `build-and-run.bat` to automatically build and copy the Watchdog executable

**Result:** Self-healing now works out of the box! 🎉
