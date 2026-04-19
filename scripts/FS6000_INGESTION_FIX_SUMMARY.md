# FS6000 Ingestion Fix - Root Cause Found

**Date:** 2026-01-02  
**Issue:** Scanner overview shows 0 scans today and uptime N/A  
**Status:** ✅ FIXED

---

## 🔍 Root Cause

### The Bug
**File:** `src/NickScanCentralImagingPortal.Services.FS6000/IngestionService.cs`  
**Line:** 626  
**Method:** `GetDataFolders()`

**Problem Code:**
```csharp
var yearFolders = Directory.GetDirectories(_config.DestinationPath)
    .Where(d => Path.GetFileName(d).StartsWith("2025"))  // ❌ HARDCODED TO 2025 ONLY!
    .OrderBy(d => d)
    .ToList();
```

### Why This Caused the Issue

1. **Files are syncing correctly:**
   - FileSyncService successfully syncing files to `C:\NickScan\FS6000\Staging`
   - Recent syncs: `2026/0102/0029`, `2026/0102/0028`, etc.
   - 5,606 completed syncs

2. **Ingestion service is running:**
   - IngestionService starts successfully
   - Continuous ingestion loop is running
   - FileSystemWatcher is initialized

3. **But ingestion isn't processing files:**
   - `GetDataFolders()` only looks for folders starting with "2025"
   - All new files are in `2026/` folders
   - Result: 0 folders found → 0 files processed → 0 scans today

4. **Statistics show 0:**
   - `/api/FS6000/statistics` queries `FS6000Scans` table
   - Filters by `ScanTime >= todayStart AND ScanTime < todayEnd`
   - No new records = 0 scans today
   - Uptime calculation: `_uptime = _todayScans > 0 ? 99.5 : 0;` → shows "N/A"

---

## ✅ Fix Applied

**Changed:**
```csharp
// ✅ FIX: Process all year folders (2025+), not just 2025
var yearFolders = Directory.GetDirectories(_config.DestinationPath)
    .Where(d => {
        var yearName = Path.GetFileName(d);
        return yearName.Length == 4 && 
               int.TryParse(yearName, out var year) && 
               year >= 2025; // Process 2025 and above
    })
    .OrderBy(d => d)
    .ToList();
```

**What this does:**
- Processes all year folders from 2025 onwards (2025, 2026, 2027, etc.)
- Uses proper year validation instead of string prefix matching
- Future-proof for years beyond 2026

---

## 📊 Impact

**Before Fix:**
- Files synced: ✅ Working (5,606 completed)
- Files ingested: ❌ Not working (0 today)
- Today's scans: 0
- Uptime: N/A

**After Fix:**
- Files synced: ✅ Working
- Files ingested: ✅ Will process 2026 folders
- Today's scans: Should show actual count
- Uptime: Should show 99.5% (when scans > 0)

---

## 🚀 Next Steps

1. ✅ Fix applied and compiled
2. ⏳ Restart API to load new DLL
3. ⏳ Monitor ingestion logs for processing activity
4. ⏳ Verify today's scans count updates
5. ⏳ Check uptime calculation

---

## 📝 Technical Details

**Why the bug existed:**
- Code was written when only 2025 data existed
- Hardcoded "2025" prefix for performance (avoiding parsing)
- Not updated when 2026 data started arriving

**Why it wasn't caught:**
- Sync service logs showed success (files were being copied)
- Ingestion service logs showed "No new files to process" (at Debug level)
- No errors were thrown (just no results)
- Database had old 2025 data, so system appeared functional

**Detection:**
- Scanner overview showing 0 scans today
- Last scan date: 2025-12-31 (2 days ago)
- 3,556 scans stuck in "Pending" status (likely 2026 data that can't be processed)

