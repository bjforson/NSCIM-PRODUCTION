# Memory Leak Investigation - Complete Summary

## Executive Summary

**Current Status:** API memory at 28.92 GB after 16.8 minutes (growth rate: ~1.7 GB/min)

**Actions Taken:**
1. ✅ Added safety limit to `GetPreComputedCompletenessDataAsync`
2. ✅ Created memory profiler setup guide and scripts
3. ✅ Investigated other potential memory leak sources

## 1. Code Fixes Applied

### ✅ GetPreComputedCompletenessDataAsync - Safety Limit Added

**File:** `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerCompletenessService.cs`
**Line:** 921

**Change:**
- Added `.Take(maxRecordsLimit)` before `.ToListAsync()` to limit loaded records to 10,000
- This prevents loading excessive records when filters are broad

**Before:**
```csharp
var filteredRecords = await query.ToListAsync();
```

**After:**
```csharp
const int maxRecordsLimit = 10000; // Safety limit: max 10,000 records
var filteredRecords = await query.Take(maxRecordsLimit).ToListAsync();
```

**Impact:** Prevents loading more than 10,000 records into memory for this API endpoint.

## 2. Memory Profiler Setup

### ✅ Created Setup Scripts and Documentation

**Files Created:**
- `scripts/Setup-MemoryProfiler.md` - Comprehensive guide for PerfView, dotMemory, VS Diagnostics, and dotnet-counters
- `scripts/Setup-PerfView-MemoryProfiler.ps1` - PowerShell script to download and install PerfView

**Recommended Tool:** PerfView (free, Microsoft tool)
- Download: https://github.com/Microsoft/perfview/releases
- Usage: See `scripts/Setup-MemoryProfiler.md` for detailed instructions

**Quick Start:**
1. Download PerfView from GitHub releases
2. Run `PerfView.exe` (may require elevation)
3. Click "Collect" → Select "Memory" → Start Collection
4. Let it run for 2-5 minutes while API is running
5. Stop collection and analyze the `.etl` file

## 3. Other Memory Leak Sources Investigated

### ✅ ServiceHealthMonitor (Already Fixed)
- **Status:** Has `CleanupOldMetrics()` method (line 266)
- **Usage:** Called every hour in `ImageAnalysisOrchestratorService` (line 287)
- **Cleanup:** Removes metrics older than 24 hours
- **Conclusion:** ✅ Not a leak source (already has cleanup)

### ✅ ReadyGroupsCacheService (Already Fixed)
- **Status:** Uses `IMemoryCache` with expiration
- **Limits:** Has `.Take(200)` limit on queries (line 72)
- **Expiration:** 30-second cache expiration
- **Conclusion:** ✅ Not a leak source (has limits and expiration)

### ✅ Cycle Cache (_cycleCache) (Already Fixed)
- **Status:** Set to `null` at start of each cycle
- **Usage:** `_cycleCache = new Dictionary<string, object>();` at cycle start
- **Conclusion:** ✅ Not a leak source (cleared each cycle)

### ✅ Other Singleton Services
- **AdaptivePollingHelper:** Stateless, no accumulation
- **ReadyGroupsCacheService:** Uses IMemoryCache (expiration)
- **ServiceHealthMonitor:** Has cleanup mechanism

## 4. Known Issues Already Fixed

1. ✅ **RunIntakeWorkflowAsync** - Has `.Take(maxCompletenessRowsPerCycle)` limit (5000 rows)
2. ✅ **GetContainersReadyForSubmissionAsync** - Has `.Take(Math.Min(limit, 20))` limit
3. ✅ **ContainersNeedingCheck** - Has `.Take(100)` limit
4. ✅ **ReadyGroupsCacheService** - Has `.Take(200)` limit
5. ✅ **ServiceHealthMonitor** - Has `CleanupOldMetrics()` method
6. ✅ **ChangeTracker.Clear()** - Added to repositories
7. ✅ **Image Streaming** - Using FileStream instead of loading bytes
8. ✅ **ArrayPool** - Using for base64 conversions
9. ✅ **JsonDocument Disposal** - Using `using` statements

## 5. Remaining Questions

### High Memory Growth Rate (1.7 GB/min)

Despite all fixes applied, memory is still growing at ~1.7 GB/min. This suggests:

1. **Unknown Leak Source:** There may be a leak source we haven't identified yet
2. **Large Dataset Processing:** Background services may be processing very large datasets
3. **Entity Framework Tracking:** DbContext instances may still be accumulating tracked entities
4. **Third-Party Libraries:** External libraries may have memory leaks
5. **Large Object Heap (LOH):** Large objects (>85KB) may be accumulating

### Next Steps

1. **Use Memory Profiler:**
   - Run PerfView or dotMemory to identify actual leak sources
   - Look for types with high allocations
   - Check for LOH accumulation
   - Monitor Gen2 GC collections

2. **Monitor Specific Services:**
   - Check which background services are most active
   - Monitor memory growth during specific workflows
   - Identify patterns in memory growth

3. **Temporary Disabling:**
   - Consider temporarily disabling non-critical background services
   - Monitor if memory growth decreases
   - Re-enable services one by one to identify the culprit

4. **Database Query Analysis:**
   - Check for queries that return large result sets
   - Monitor database query execution times
   - Look for queries without proper limits

## 6. Recommendations

### Immediate Actions

1. **Deploy the GetPreComputedCompletenessDataAsync fix**
2. **Run memory profiler** to identify actual leak sources
3. **Monitor memory growth** after profiler analysis

### Long-Term Actions

1. **Implement memory monitoring** in the application
2. **Add memory limits** to all unbounded queries
3. **Regular memory profiling** during development
4. **Review third-party libraries** for known memory issues

## 7. Files Modified

1. `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerCompletenessService.cs`
   - Added safety limit to `GetPreComputedCompletenessDataAsync`

## 8. Files Created

1. `scripts/Setup-MemoryProfiler.md` - Memory profiler setup guide
2. `scripts/Setup-PerfView-MemoryProfiler.ps1` - PerfView setup script
3. `scripts/MemoryLeak-Analysis-Summary.md` - Analysis summary
4. `scripts/MemoryLeak-Investigation-Complete.md` - This file

## Conclusion

We've applied all identified fixes and investigated potential leak sources. The remaining high memory growth rate requires memory profiling to identify the actual leak sources. The memory profiler will provide detailed information about what's consuming memory and help identify the root cause.

