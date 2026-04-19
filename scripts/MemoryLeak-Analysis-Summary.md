# Memory Leak Analysis Summary

## Critical Finding: Unbounded Query in GetPreComputedCompletenessDataAsync

**Location:** `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerCompletenessService.cs`
**Method:** `GetPreComputedCompletenessDataAsync`
**Line:** 921

### Issue
The method loads ALL filtered records into memory using `.ToListAsync()` before applying pagination in memory. While it has filters, if there are many records matching the filters, this could load thousands or tens of thousands of records into memory.

```csharp
// Line 921: Loads ALL filtered records into memory
var filteredRecords = await query.ToListAsync();

// Then filters in memory (lines 923-933)
var validRecords = filteredRecords.Where(...).ToList();

// Then groups in memory (lines 936-942)
var mostRecentScans = validRecords.GroupBy(...).Select(...).ToList();

// Finally applies pagination in memory (lines 948-952)
var completenessData = mostRecentScans.Skip(skip).Take(pageSize).ToList();
```

### Recommendation
**Option 1 (Preferred):** Apply pagination at the database level before loading records:
- Use `.Skip()` and `.Take()` on the query before `.ToListAsync()`
- But note: This is complicated because of the in-memory filtering (`char.IsLetter()` and `invalidContainerNumbers.Contains()`)

**Option 2:** Add a hard limit to the query:
- Add `.Take(10000)` before `.ToListAsync()` to prevent loading more than 10,000 records
- This is a safety measure while maintaining current logic

**Option 3:** Refactor to move the in-memory filtering to SQL:
- Create a SQL function or use a more SQL-friendly approach
- This would allow proper database-level pagination

## Other Queries Reviewed

### ✅ Already Fixed
1. **RunIntakeWorkflowAsync** (`ImageAnalysisOrchestratorService.cs:439-446`)
   - Has `.Take(maxCompletenessRowsPerCycle)` limit (5000 rows)
   - ✅ Already fixed

2. **GetContainersReadyForSubmissionAsync** (`ContainerDataMapperService.cs:350-362`)
   - Has `.Take(Math.Min(limit, 20))` limit
   - ✅ Already fixed

3. **ContainersNeedingCheck** (`ContainerCompletenessService.cs:426-439`)
   - Has `.Take(100)` limit
   - ✅ Already fixed

### ✅ Acceptable (Query-Specific)
1. **GetContainerCompletenessStatusAsync** (`ContainerCompletenessService.cs:782-784`)
   - Queries for a specific container: `Where(c => c.ContainerNumber == containerNumber && c.ScannerType == scannerType)`
   - Should return a small number of records per container
   - ✅ Acceptable

## HttpClient URI Error Investigation

### Error Message
"An invalid request URI was provided. Either the request URI must be an absolute URI or BaseAddress must be set."

### Possible Causes
1. HttpClient created without BaseAddress
2. Relative URI used with HttpClient that doesn't have BaseAddress
3. HttpClientFactory client not properly configured

### Search Results
- No direct HttpClient instantiation found in API codebase
- HttpClientFactory is used via `IHttpClientFactory`
- All HttpClient registrations in `Program.cs` and `ServiceConfiguration.cs` have `BaseAddress` set

### Recommendation
1. Check application logs for the exact location of the error
2. Search for any direct `new HttpClient()` instantiation
3. Check if any code is using `HttpClient` with relative URIs without BaseAddress
4. Review any third-party libraries that might be creating HttpClient instances

## Next Steps

1. **Fix GetPreComputedCompletenessDataAsync:**
   - Add `.Take(10000)` limit as a safety measure (Option 2 above)
   - Or refactor to database-level pagination (Option 3 - more complex)

2. **Monitor Memory:**
   - Use memory profiler to identify actual leak sources
   - Monitor memory growth after fixes

3. **HttpClient URI Error:**
   - Check application logs for exact error location
   - Search codebase for HttpClient usage patterns
   - Check third-party libraries

