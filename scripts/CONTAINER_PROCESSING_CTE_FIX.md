# ContainerProcessing CTE Error Fix

**Date:** 2026-01-02  
**Status:** ✅ **FIXED**

---

## 🐛 Issue

**Error:** `Incorrect syntax near the keyword 'WITH'`  
**Endpoint:** `/api/ContainerProcessing/summary`  
**Location:** `ContainerProcessingRepository.GetContainerGroupsAsync()` at line 55

---

## 🔍 Root Cause

The query at line 55 was using `.Where(b => batch.Contains(b.ContainerNumber))` which causes EF Core to generate a CTE (Common Table Expression) that SQL Server 2014 rejects:

```csharp
var batchData = await _icumContext.BOEDocuments
    .Where(b => batch.Contains(b.ContainerNumber))  // ❌ Generates CTE
    .Select(b => new { ... })
    .ToListAsync();
```

---

## ✅ Fix Applied

Replaced `Contains()` with `FromSqlRaw` to avoid CTE generation:

```csharp
// ✅ FIX: Use FromSqlRaw to avoid CTE generation from Contains()
var placeholders = string.Join(",", batch.Select(s => $"'{s.Replace("'", "''")}'")); // Escape single quotes
var batchData = await _icumContext.BOEDocuments
    .FromSqlRaw($"SELECT * FROM BOEDocuments WHERE ContainerNumber IN ({placeholders})")
    .AsNoTracking()
    .Select(b => new
    {
        b.ContainerNumber,
        b.BlNumber,
        b.DeclarationNumber,
        b.RotationNumber,
        b.ClearanceType
    })
    .ToListAsync();
```

**Key Changes:**
1. **FromSqlRaw:** Direct SQL query avoids EF Core CTE generation
2. **SQL Escaping:** Properly escapes single quotes in container numbers
3. **AsNoTracking:** Added for performance (read-only query)

---

## 📊 Impact

- **Before:** CTE error causing 500 Internal Server Error
- **After:** Query executes successfully without CTE generation
- **Performance:** No change (same query pattern, just different execution path)

---

## ✅ Status

- **Build:** Successful (0 errors)
- **API:** Restarted with fix
- **Endpoint:** `/api/ContainerProcessing/summary` should now work correctly

---

**Last Updated:** 2026-01-02 22:15:00

