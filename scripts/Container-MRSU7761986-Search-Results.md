# Container MRSU7761986 - Search Results

**Date:** January 2026  
**Container Number:** MRSU7761986

---

## 🔍 **SEARCH RESULTS SUMMARY**

### ❌ **NO DATA FOUND** for container MRSU7761986 in any database table.

---

## 📊 **DETAILED SEARCH RESULTS**

### 1. **FS6000 Scanner Data**
- **Table:** `NS_CIS.dbo.FS6000Scans`
- **Result:** ❌ **0 records found**
- **Status:** Container does not exist in FS6000 scanner records

### 2. **ASE Scanner Data**
- **Table:** `NS_CIS.dbo.AseScans`
- **Result:** ❌ **0 records found**
- **Status:** Container does not exist in ASE scanner records

### 3. **ICUMS Container Data (Main Database)**
- **Table:** `ICUMS.dbo.IcumContainerData`
- **Result:** ❌ **0 records found**
- **Status:** Container does not exist in ICUMS main database

### 4. **ICUMS Downloads Database (Comprehensive Check)**
- **Table:** `ICUMS_Downloads.dbo.BOEDocuments`
  - **Result:** ❌ **0 records found**
  - **Status:** Container not found in BOE documents
- **Table:** `ICUMS_Downloads.dbo.DownloadedFiles`
  - **Result:** ❌ **0 records found**
  - **Status:** No download files for this container
- **Table:** `ICUMS_Downloads.dbo.ManifestItems`
  - **Result:** ❌ **0 records found**
  - **Status:** No manifest items for this container
- **Table:** `ICUMS_Downloads.dbo.IngestionLogs`
  - **Result:** ❌ **0 records found**
  - **Status:** No ingestion logs mentioning this container
- **Table:** `ICUMS_Downloads.dbo.ICUMSDownloadQueue`
  - **Result:** ❌ **0 records found**
  - **Status:** Container not in download queue
- **Table:** `ICUMS_Downloads.dbo.ContainerDownloadHistory`
  - **Result:** ❌ **0 records found**
  - **Status:** No download history for this container
- **Table:** `ICUMS_Downloads.dbo.CMRRedownloadQueue`
  - **Result:** ❌ **0 records found**
  - **Status:** Container not in CMR redownload queue
- **Raw JSON Search:**
  - **Result:** ❌ **0 records found**
  - **Status:** Container number not found in any raw JSON data

### 5. **Container Completeness Status**
- **Table:** `NS_CIS.dbo.ContainerCompletenessStatuses`
- **Result:** ❌ **0 records found**
- **Status:** Container has not been tracked by the completeness service

---

## 📋 **VERIFICATION QUERIES**

### FS6000 Scans Query
```sql
SELECT COUNT(*) AS RecordCount 
FROM FS6000Scans 
WHERE ContainerNumber = 'MRSU7761986'
-- Result: 0
```

### ASE Scans Query
```sql
SELECT COUNT(*) AS RecordCount 
FROM AseScans 
WHERE ContainerNumber = 'MRSU7761986'
-- Result: 0
```

### ICUMS Container Data Query
```sql
SELECT COUNT(*) AS RecordCount 
FROM ICUMS.dbo.IcumContainerData 
WHERE ContainerNumber = 'MRSU7761986'
-- Result: 0
```

### ICUMS Downloads Database - Comprehensive Queries

**BOEDocuments:**
```sql
SELECT COUNT(*) AS RecordCount 
FROM ICUMS_Downloads.dbo.BOEDocuments 
WHERE ContainerNumber = 'MRSU7761986'
-- Result: 0
```

**DownloadedFiles:**
```sql
SELECT COUNT(*) AS RecordCount 
FROM ICUMS_Downloads.dbo.DownloadedFiles 
WHERE FileName LIKE '%MRSU7761986%' OR FilePath LIKE '%MRSU7761986%'
-- Result: 0
```

**ManifestItems:**
```sql
SELECT COUNT(*) AS RecordCount 
FROM ICUMS_Downloads.dbo.ManifestItems 
WHERE Description LIKE '%MRSU7761986%' OR RawJsonData LIKE '%MRSU7761986%'
-- Result: 0
```

**ICUMSDownloadQueue:**
```sql
SELECT COUNT(*) AS RecordCount 
FROM ICUMS_Downloads.dbo.ICUMSDownloadQueue 
WHERE ContainerNumber = 'MRSU7761986'
-- Result: 0
```

**ContainerDownloadHistory:**
```sql
SELECT COUNT(*) AS RecordCount 
FROM ICUMS_Downloads.dbo.ContainerDownloadHistory 
WHERE ContainerNumber = 'MRSU7761986'
-- Result: 0
```

**CMRRedownloadQueue:**
```sql
SELECT COUNT(*) AS RecordCount 
FROM ICUMS_Downloads.dbo.CMRRedownloadQueue 
WHERE ContainerNumber = 'MRSU7761986'
-- Result: 0
```

**Raw JSON Search:**
```sql
SELECT COUNT(*) AS RecordCount 
FROM ICUMS_Downloads.dbo.BOEDocuments 
WHERE UPPER(RawJsonData) LIKE '%MRSU7761986%'
-- Result: 0
```

### Container Completeness Query
```sql
SELECT COUNT(*) AS RecordCount 
FROM ContainerCompletenessStatuses 
WHERE ContainerNumber = 'MRSU7761986'
-- Result: 0
```

---

## 🔎 **COMPARISON WITH SIMILAR CONTAINERS**

The following MRSU containers **DO exist** in the database:

### NS_CIS Database (Scanner Data)
| Container Number | Scanner Type | Scan Date | Has ICUMS Data | Status |
|------------------|--------------|-----------|----------------|--------|
| MRSU0585695 | FS6000 | 2026-01-16 19:52:39 | No | Missing |
| MRSU0348651 | FS6000 | 2026-01-16 15:25:59 | No | Missing |
| MRSU8675408 | ASE | 2026-01-16 01:07:20 | Yes | Complete |

### ICUMS_Downloads Database (Sample MRSU Containers)
| Container Number | Source |
|------------------|--------|
| MRSU5874258 | BOEDocuments |
| MRSU5447446 | BOEDocuments |
| MRSU4624850 | BOEDocuments |
| MRSU3435240 | BOEDocuments |
| MRSU4753251 | BOEDocuments |

**Database Statistics:**
- **Total BOEDocuments:** 264,395 records
- **MRSU Containers in BOEDocuments:** 10,737 records
- **MRSU7761986:** 0 records ❌

**Note:** Other MRSU containers exist, confirming the query system is working correctly.

---

## 💡 **CONCLUSIONS**

1. ❌ **Container MRSU7761986 has never been scanned** by either FS6000 or ASE scanners
2. ❌ **Container MRSU7761986 has NO ICUMS data** in any table:
   - Not in BOEDocuments (checked 264,395 records)
   - Not in DownloadedFiles
   - Not in ManifestItems
   - Not in ICUMSDownloadQueue
   - Not in ContainerDownloadHistory
   - Not in CMRRedownloadQueue
   - Not found in any RawJsonData
3. ❌ **Container MRSU7761986 has not been tracked** by the completeness service
4. ✅ **Database queries are working correctly** - other MRSU containers were found (10,737 MRSU containers exist in ICUMS_Downloads)

---

## 🎯 **POSSIBLE REASONS**

1. **Container Number Typo:** Verify the container number is correct (MRSU7761986)
2. **Not Yet Scanned:** Container may not have been scanned yet
3. **Different Scanner:** Container might be on a different scanner system (Heimann Smith?)
4. **Historical Data:** Container might be from before the database tracking started
5. **Different Format:** Container number might be stored with different formatting

---

## 📝 **RECOMMENDATIONS**

1. **Double-check the container number** for any typos
2. **Check Heimann Smith scanner** if applicable
3. **Verify the container was scanned** on the intended scanner
4. **Check if container is in manual processing queue**
5. **Search with partial container number** to see if similar containers exist

---

**Generated:** January 2026  
**Database:** NS_CIS, ICUMS, ICUMS_Downloads  
**Query Tool:** SQL Server via sqlcmd

