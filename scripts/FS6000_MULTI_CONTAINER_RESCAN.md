# ✅ FS6000 Multi-Container Rescan Script

**Date**: January 4, 2026  
**Purpose**: Rescan and update 2026 FS6000 files with missing container details from comma-separated values

---

## 🎯 **THE PROBLEM**

### **Issue**:
- FS6000 XML files can contain **multiple containers in one record** (comma-separated)
- The multi-container fix was implemented, but **existing records from 2026** were ingested before the fix
- These records only have **one container** when there should be **two or more**
- Example: Container `MSNU3682162` has 2 containers in the XML, but only one was ingested

### **Root Cause**:
- Records were ingested using the old logic that only extracted the first container number
- The new multi-container logic was implemented but hasn't been applied to existing records

---

## ✅ **THE SOLUTION**

### **Rescan Script**: `scripts/Rescan-FS6000MultiContainer2026.ps1`

This PowerShell script:
1. **Finds all XML files** from the specified year (default: 2026)
2. **Re-parses each XML** using the new multi-container extraction logic
3. **Identifies missing containers** by comparing XML containers with database records
4. **Creates new FS6000Scan records** for missing containers using the same metadata

---

## 🔄 **HOW IT WORKS**

### **Step 1: Find XML Files**
- Searches `C:\NickScan\FS6000\Staging\{Year}` and `C:\NickScan\FS6000\Processed\{Year}`
- Finds all `.xml` files recursively

### **Step 2: Parse XML Files**
- Extracts **ALL** container numbers from `UNITID` and `container_no` fields
- Handles comma-separated values (e.g., `"MSNU3682162, ABC1234567"`)
- Cross-validates between `UNITID` and `container_no` fields
- Returns all matching container numbers

### **Step 3: Check Database**
- Queries existing `FS6000Scans` records by `PicNumber`
- Compares XML containers with database containers
- Identifies missing containers

### **Step 4: Create Missing Records**
- Uses the first existing record as a template (same metadata)
- Creates new `FS6000Scan` records for each missing container
- Preserves all fields: `ScanTime`, `PicNumber`, `VesselName`, etc.

---

## 📊 **USAGE**

### **Dry Run** (Recommended First):
```powershell
.\scripts\Rescan-FS6000MultiContainer2026.ps1 -DryRun
```

**Output**: Shows what would be updated without making changes

### **Live Run** (Updates Database):
```powershell
.\scripts\Rescan-FS6000MultiContainer2026.ps1
```

### **Scan Different Year**:
```powershell
.\scripts\Rescan-FS6000MultiContainer2026.ps1 -Year 2025
```

### **Custom Paths**:
```powershell
.\scripts\Rescan-FS6000MultiContainer2026.ps1 `
    -SourcePath "C:\NickScan\FS6000\Staging" `
    -ProcessedPath "C:\NickScan\FS6000\Processed" `
    -Year 2026
```

---

## 📈 **EXAMPLE OUTPUT**

### **Dry Run**:
```
========================================
FS6000 Multi-Container Rescan Script
Year: 2026
Mode: DRY RUN (no changes will be made)
========================================

Searching for XML files from year 2026...
Found 1250 XML files from 2026

Connecting to database...
✅ Connected to database

Processing files...

[1/1250] Multi-container file: 23301FS01202601040003.xml
  Containers found: MSNU3682162, ABC1234567
  Existing in DB: MSNU3682162
  Missing containers: ABC1234567
  [DRY RUN] Would create 1 new scan record(s)

[2/1250] Multi-container file: 23301FS01202601040004.xml
  Containers found: DEF7890123, GHI4567890
  Existing in DB: DEF7890123, GHI4567890
  ✅ All containers already in database

...

========================================
Rescan Complete!
========================================

Statistics:
  Files Processed: 1250
  Files with Multi-Container: 45
  Missing Containers Found: 67
  Records Would Be Updated: 67
  Errors: 0
```

### **Live Run**:
```
[1/1250] Multi-container file: 23301FS01202601040003.xml
  Containers found: MSNU3682162, ABC1234567
  Existing in DB: MSNU3682162
  Missing containers: ABC1234567
  ✅ Created record for container: ABC1234567

...

Statistics:
  Files Processed: 1250
  Files with Multi-Container: 45
  Missing Containers Found: 67
  Records Updated: 67
  Errors: 0
```

---

## 🔍 **WHAT THE SCRIPT DOES**

### **1. XML Parsing Logic**:
- Extracts `UNITID` and `container_no` from XML
- Splits comma-separated values
- Cross-validates to find matching containers
- Returns all valid container numbers

### **2. Database Matching**:
- Queries `FS6000Scans` by `PicNumber` (unique identifier per XML file)
- Compares XML containers with existing database records
- Identifies containers that exist in XML but not in database

### **3. Record Creation**:
- Uses first existing record as template
- Creates new records with:
  - **Same metadata**: `ScanTime`, `PicNumber`, `VesselName`, `OperatorId`, etc.
  - **Different container**: New `ContainerNumber` from XML
  - **New GUID**: Each record gets a unique `Id`

---

## ⚠️ **IMPORTANT NOTES**

### **Before Running**:
1. ✅ **Backup database** (recommended)
2. ✅ **Run dry run first** to see what will be updated
3. ✅ **Verify FS6000 Ingestion Service** is using the new multi-container logic

### **After Running**:
1. ✅ **Verify records** were created correctly
2. ✅ **Check logs** for any errors
3. ✅ **Monitor** container completeness status updates

### **Limitations**:
- Script processes files **sequentially** (may take time for large datasets)
- Only processes files from **specified year** (default: 2026)
- Requires **XML files** to still exist in Staging or Processed folders

---

## 🚀 **NEXT STEPS**

### **1. Run Dry Run**:
```powershell
.\scripts\Rescan-FS6000MultiContainer2026.ps1 -DryRun
```

### **2. Review Output**:
- Check how many files have multi-container records
- Verify missing containers are correctly identified
- Ensure no unexpected errors

### **3. Run Live**:
```powershell
.\scripts\Rescan-FS6000MultiContainer2026.ps1
```

### **4. Verify Results**:
```sql
-- Check for newly created records
SELECT ContainerNumber, PicNumber, ScanTime, CreatedAt 
FROM FS6000Scans 
WHERE YEAR(CreatedAt) = 2026 
  AND CreatedAt > DATEADD(hour, -1, GETUTCDATE())
ORDER BY CreatedAt DESC
```

---

## 🎉 **SUMMARY**

### **What This Script Does**:
✅ Rescans FS6000 XML files from 2026  
✅ Extracts ALL container numbers (not just first)  
✅ Identifies missing containers in database  
✅ Creates new FS6000Scan records for missing containers  

### **Impact**:
📊 **Complete data** - All containers from XML files are now in database  
🔄 **No data loss** - Missing containers are recovered  
⏱️ **Automated** - No manual intervention needed  

---

**The rescan script is ready to use!** 🚀

