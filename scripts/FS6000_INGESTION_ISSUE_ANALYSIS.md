# FS6000 Ingestion Issue Analysis

**Date:** 2026-01-02  
**Issue:** Scanner overview shows 0 scans today and uptime N/A

---

## 🔍 Root Cause Identified

### Problem Summary
- ✅ **File Sync Service:** Working correctly (5,606 completed syncs)
- ❌ **Ingestion Service:** NOT processing synced files into database

### Evidence

1. **Database Status:**
   - Total scans in database: 4,458
   - Scans with SyncStatus = "Pending": 3,556 (79.8% of all scans!)
   - Today's scans: 0
   - Last scan date: 2025-12-31 (2 days ago)

2. **File System Status:**
   - Files are being synced to: `C:\NickScan\FS6000\Staging`
   - Staging directory contains: 659 folders, 1,025 files
   - Files are present but NOT being ingested

3. **Statistics Endpoint:**
   - `/api/FS6000/statistics` queries `FS6000Scans` table
   - Filters by `ScanTime >= todayStart AND ScanTime < todayEnd`
   - Returns 0 because no new records are being ingested

4. **Uptime Calculation:**
   - Frontend code: `_uptime = _todayScans > 0 ? 99.5 : 0;`
   - Since `_todayScans = 0`, uptime shows as 0 or "N/A"

---

## 🔧 What Should Happen

### Expected Workflow:
1. **FileSyncService** copies files from `Z:\` to `C:\NickScan\FS6000\Staging` ✅ (Working)
2. **IngestionService** processes files from staging directory into database ❌ (Not working)
3. **Database** gets new `FS6000Scans` records with `ScanTime` from XML files ❌ (Not happening)
4. **Statistics endpoint** counts today's scans from database ❌ (Returns 0)

### Current State:
- Files are synced but NOT ingested
- 3,556 scans stuck in "Pending" status
- No new scans being added to database since 2025-12-31

---

## 🚨 Immediate Actions Needed

1. **Check IngestionService Status:**
   - Verify if `IngestionService.StartIngestionAsync()` is being called
   - Check if ingestion loop is running
   - Look for errors in API logs

2. **Check for Ingestion Errors:**
   - Review API logs for ingestion-related errors
   - Check if files are being processed but failing
   - Verify database connectivity from ingestion service

3. **Verify Service Configuration:**
   - Check if `FS6000BackgroundService` is enabled
   - Verify ingestion service is registered in DI container
   - Confirm processing directory path is correct

4. **Manual Processing Test:**
   - Try manually triggering ingestion via API endpoint
   - Test processing a single folder to identify errors

---

## 📊 Impact

- **User Experience:** Scanner overview shows incorrect data (0 scans, N/A uptime)
- **Data Integrity:** 3,556 scans not properly ingested
- **System Health:** Ingestion service appears to be non-functional

---

## 🔍 Next Steps

1. Check API logs for ingestion service errors
2. Verify IngestionService is running
3. Test manual folder processing
4. Review ingestion service code for issues
5. Check database for any constraints preventing inserts

