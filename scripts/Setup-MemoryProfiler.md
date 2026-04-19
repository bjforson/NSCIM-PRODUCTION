# Memory Profiler Setup Guide

## Overview
The API is experiencing critical memory growth (~1.7 GB/min, now at 28.92 GB). This guide helps set up memory profiling to identify the leak sources.

## Option 1: PerfView (Free, Microsoft Tool)

### Installation
1. Download PerfView from: https://github.com/Microsoft/perfview/releases
2. Extract to a folder (e.g., `C:\Tools\PerfView`)
3. Run `PerfView.exe` (may require elevation)

### Usage
1. **Start the API** (if not already running)
2. **Open PerfView**
3. **Collect Memory Data:**
   - Click "Collect" button
   - Select "Memory" checkbox
   - Set "Data File" (e.g., `C:\Temp\API_Memory.etl`)
   - Click "Start Collection"
   - Let it run for 2-5 minutes
   - Click "Stop Collection"

4. **Analyze Results:**
   - Double-click the `.etl` file
   - Navigate to "GC Stats" view
   - Look for "Heap Size" column - identify what's growing
   - Navigate to "GC Heap Alloc Ignore Free" view
   - Sort by "Inc" (increment) column
   - Look for types with high allocations

### Key Things to Look For
- **Large Object Heap (LOH):** Objects > 85KB that aren't being collected
- **Gen2 Collections:** If these are infrequent, memory is accumulating
- **Type Allocations:** Types with high "Inc" values are likely leak sources
- **Entity Framework Types:** `DbSet`, `InternalDbSet`, tracked entities

## Option 2: dotMemory (JetBrains - 30-day Trial)

### Installation
1. Download from: https://www.jetbrains.com/dotmemory/
2. Install dotMemory Standalone

### Usage
1. **Attach to Running Process:**
   - Start dotMemory
   - Click "Attach to Process"
   - Select `NickScanCentralImagingPortal.API`
   - Click "Start"

2. **Take Snapshots:**
   - Let it run for 2-5 minutes
   - Click "Get Snapshot"
   - Wait 2-5 more minutes
   - Click "Get Snapshot" again

3. **Analyze:**
   - Compare snapshots
   - Look at "Memory Traffic" view
   - Check "Types" view - sort by "Retained Size"
   - Look for types with high retained memory
   - Check "Back References" to see what's holding objects

### Key Things to Look For
- **Retained Size:** Objects that aren't being released
- **GC Roots:** Objects that prevent garbage collection
- **Entity Framework:** Tracked entities, DbContext instances
- **Caching:** Dictionary, ConcurrentDictionary, MemoryCache

## Option 3: Visual Studio Diagnostic Tools (If Available)

### Usage
1. Open the solution in Visual Studio
2. Set breakpoint or start debugging
3. Go to **Debug > Performance Profiler**
4. Select **Memory Usage**
5. Click **Start**
6. Let it run for 2-5 minutes
7. Click **Stop Collection**

### Analyze
- View "Heap Size" over time
- Take snapshots and compare
- Look at "Objects (Diff)" view
- Sort by "Size (Bytes)" to find largest objects

## Option 4: dotnet-counters (CLI Tool - Quick Check)

### Installation
```powershell
dotnet tool install --global dotnet-counters
```

### Usage
```powershell
# List available processes
dotnet-counters ps

# Monitor memory for API process
dotnet-counters monitor --process-id <PID> System.Runtime
```

### Key Metrics to Watch
- **GC Heap Size:** Total managed heap size
- **Gen 2 Collections:** Number of Gen2 GCs (should be low for leaks)
- **Exception Count:** Unexpected exceptions can cause leaks
- **Thread Count:** Thread leaks can cause memory issues

## Quick Diagnostic Commands

### Check Memory Growth Rate
```powershell
.\scripts\Analyze-MemoryUsage.ps1 -ProcessName "NickScanCentralImagingPortal.API" -DurationMinutes 5 -IntervalSeconds 30
```

### Check Background Services
```powershell
.\scripts\Check-BackgroundServices.ps1
```

### Check Process Memory
```powershell
Get-Process -Name "NickScanCentralImagingPortal.API" | Select-Object Id, @{Name="Memory(GB)";Expression={[math]::Round($_.WS / 1GB, 2)}}, StartTime, @{Name="Uptime(Minutes)";Expression={[math]::Round(((Get-Date) - $_.StartTime).TotalMinutes, 1)}}
```

## Common Memory Leak Sources to Check

1. **Entity Framework Change Tracking:**
   - Missing `ChangeTracker.Clear()` after `SaveChangesAsync()`
   - DbContext instances not being disposed
   - Tracking enabled when `AsNoTracking()` should be used

2. **Unbounded Queries:**
   - `.ToListAsync()` without `.Take()` limits
   - Loading entire tables into memory
   - N+1 query problems

3. **Caching:**
   - Unbounded caches (Dictionary, ConcurrentDictionary)
   - MemoryCache without size limits
   - Singleton services caching too much data

4. **Background Services:**
   - Services processing large datasets
   - Services not disposing resources
   - Services accumulating state

5. **Large Object Heap (LOH):**
   - Images loaded into memory
   - Large byte arrays from base64 conversions
   - Large JSON documents

## Next Steps

1. **Run the profiler** for 2-5 minutes while the API is running
2. **Identify the leak sources** from the profiler results
3. **Fix the identified issues** in the code
4. **Monitor memory** after fixes to verify improvement

