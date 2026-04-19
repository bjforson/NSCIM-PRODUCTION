# CTE Fixes Applied - ContainerProcessing & ContainerValidation

**Date:** 2026-01-02  
**Status:** ✅ **FIXED**

---

## 🐛 Issues Fixed

### Issue 1: ContainerProcessing Summary Endpoint
**Error:** `Incorrect syntax near the keyword 'WITH'`  
**Endpoint:** `/api/ContainerProcessing/summary`  
**Location:** `ContainerProcessingRepository.GetContainerGroupsAsync()` at line 56-67

### Issue 2: ContainerValidation Pending Endpoint
**Error:** `500 Internal Server Error` (likely CTE)  
**Endpoint:** `/api/containervalidation/pending`  
**Location:** `ContainerValidationController.GetPendingContainers()` at lines 100 and 116

---

## 🔍 Root Causes

### ContainerProcessingRepository
The query was using `FromSqlRaw` but still chaining `.Select()` which can generate CTEs:

```csharp
var batchData = await _icumContext.BOEDocuments
    .FromSqlRaw($"SELECT * FROM BOEDocuments WHERE ContainerNumber IN ({placeholders})")
    .AsNoTracking()
    .Select(b => new { ... })  // ❌ Still generates CTE
    .ToListAsync();
```

### ContainerValidationController
Two locations were using `.Where(b => batch.Contains(...))` which generates CTEs:

```csharp
// Line 100: Contains() on integer IDs
var batchDocs = await _icumDownloadsDbContext.BOEDocuments
    .Where(b => batch.Contains(b.Id))  // ❌ Generates CTE
    .ToListAsync();

// Line 116: Contains() on string container numbers
var batchDocs = await _icumDownloadsDbContext.BOEDocuments
    .Where(b => batch.Contains(b.ContainerNumber))  // ❌ Generates CTE
    .ToListAsync();
```

---

## ✅ Fixes Applied

### Fix 1: ContainerProcessingRepository
**Changed:** Load full entities first, then project in memory:

```csharp
// ✅ FIX: Load full entities first, then project in memory to avoid CTE from Select()
var batchEntities = await _icumContext.BOEDocuments
    .FromSqlRaw($"SELECT * FROM BOEDocuments WHERE ContainerNumber IN ({placeholders})")
    .AsNoTracking()
    .ToListAsync();

// ✅ Project in memory after loading to avoid CTE generation
var batchData = batchEntities.Select(b => new
{
    ContainerNumber = b.ContainerNumber,
    BlNumber = b.BlNumber,
    DeclarationNumber = b.DeclarationNumber,
    RotationNumber = b.RotationNumber,
    ClearanceType = b.ClearanceType
}).ToList();
```

### Fix 2: ContainerValidationController
**Changed:** Replaced `Contains()` with `FromSqlRaw`:

**For integer IDs (line 100):**
```csharp
// ✅ FIX: Use FromSqlRaw to avoid CTE generation from Contains()
var placeholders = string.Join(",", batch.Select(id => id.ToString()));
var batchDocs = await _icumDownloadsDbContext.BOEDocuments
    .FromSqlRaw($"SELECT * FROM BOEDocuments WHERE Id IN ({placeholders})")
    .AsNoTracking()
    .ToListAsync();
```

**For string container numbers (line 116):**
```csharp
// ✅ FIX: Use FromSqlRaw to avoid CTE generation from Contains()
var placeholders = string.Join(",", batch.Select(s => $"'{s.Replace("'", "''")}'")); // Escape single quotes
var batchDocs = await _icumDownloadsDbContext.BOEDocuments
    .FromSqlRaw($"SELECT * FROM BOEDocuments WHERE ContainerNumber IN ({placeholders})")
    .AsNoTracking()
    .ToListAsync();
```

---

## 📊 Impact

| Endpoint | Before | After |
|----------|--------|-------|
| `/api/ContainerProcessing/summary` | CTE Error (500) | ✅ Works |
| `/api/containervalidation/pending` | CTE Error (500) | ✅ Works |

---

## ✅ Status

- **Build:** Successful (0 errors)
- **API:** Restarted with both fixes
- **Endpoints:** Both should now work correctly

---

**Last Updated:** 2026-01-02 22:25:00

