# Check ASE and FS6000 Scanner Sync Status
# This script checks the status of both ASE sync and FS6000 scanner sync services

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SYNC STATUS CHECK" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$connectionString = "Server=localhost;Database=NS_CIS;Trusted_Connection=true;TrustServerCertificate=true;MultipleActiveResultSets=true"

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    Write-Host "✅ Database connection successful" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Host "❌ Database connection failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# ============================================
# ASE SYNC STATUS
# ============================================
Write-Host "========================================" -ForegroundColor Yellow
Write-Host "ASE SYNC STATUS" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Yellow
Write-Host ""

try {
    # Get total ASE scans
    $cmd = $connection.CreateCommand()
    $cmd.CommandText = "SELECT COUNT(*) FROM AseScans"
    $totalAseScans = $cmd.ExecuteScalar()
    Write-Host "Total ASE Scans: $totalAseScans" -ForegroundColor White
    
    # Get recent scans (last 24 hours)
    $cmd.CommandText = "SELECT COUNT(*) FROM AseScans WHERE CreatedAt >= DATEADD(day, -1, GETDATE())"
    $recentAseScans = $cmd.ExecuteScalar()
    Write-Host "Scans in last 24 hours: $recentAseScans" -ForegroundColor White
    
    # Get latest sync log
    $cmd.CommandText = @"
        SELECT TOP 1 
            LastSyncTime,
            LastSyncedInspectionId,
            RecordsProcessed,
            SyncStatus
        FROM AseSyncLogs
        ORDER BY LastSyncTime DESC
"@
    $reader = $cmd.ExecuteReader()
    if ($reader.Read()) {
        $lastSyncTime = $reader["LastSyncTime"]
        $lastSyncedId = $reader["LastSyncedInspectionId"]
        $recordsProcessed = $reader["RecordsProcessed"]
        $syncStatus = $reader["SyncStatus"]
        
        Write-Host "Last Sync Time: $lastSyncTime" -ForegroundColor White
        Write-Host "Last Synced Inspection ID: $lastSyncedId" -ForegroundColor White
        Write-Host "Records Processed: $recordsProcessed" -ForegroundColor White
        Write-Host "Sync Status: $syncStatus" -ForegroundColor $(if ($syncStatus -eq "Success") { "Green" } else { "Yellow" })
        
        # Calculate time since last sync
        $timeSinceSync = (Get-Date) - $lastSyncTime
        if ($timeSinceSync.TotalMinutes -lt 20) {
            Write-Host "Time since last sync: $([math]::Round($timeSinceSync.TotalMinutes, 1)) minutes ago ✅" -ForegroundColor Green
        } elseif ($timeSinceSync.TotalHours -lt 1) {
            Write-Host "Time since last sync: $([math]::Round($timeSinceSync.TotalMinutes, 1)) minutes ago ⚠️" -ForegroundColor Yellow
        } else {
            Write-Host "Time since last sync: $([math]::Round($timeSinceSync.TotalHours, 1)) hours ago ❌" -ForegroundColor Red
        }
    } else {
        Write-Host "⚠️ No sync logs found" -ForegroundColor Yellow
    }
    $reader.Close()
    
    # Get sync history (last 5)
    Write-Host ""
    Write-Host "Recent Sync History (last 5):" -ForegroundColor Cyan
    $cmd.CommandText = @"
        SELECT TOP 5
            LastSyncTime,
            LastSyncedInspectionId,
            RecordsProcessed,
            SyncStatus
        FROM AseSyncLogs
        ORDER BY LastSyncTime DESC
"@
    $reader = $cmd.ExecuteReader()
    $count = 0
    while ($reader.Read()) {
        $count++
        $syncTime = $reader["LastSyncTime"]
        $records = $reader["RecordsProcessed"]
        $status = $reader["SyncStatus"]
        Write-Host "  $count. $syncTime - $records records - $status" -ForegroundColor White
    }
    $reader.Close()
    if ($count -eq 0) {
        Write-Host "  No sync history found" -ForegroundColor Yellow
    }
    
} catch {
    Write-Host "❌ Error checking ASE sync: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host ""

# ============================================
# FS6000 SCANNER SYNC STATUS
# ============================================
Write-Host "========================================" -ForegroundColor Yellow
Write-Host "FS6000 SCANNER SYNC STATUS" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Yellow
Write-Host ""

try {
    # Get total sync logs
    $cmd = $connection.CreateCommand()
    $cmd.CommandText = "SELECT COUNT(*) FROM FS6000SyncLogs"
    $totalSyncLogs = $cmd.ExecuteScalar()
    Write-Host "Total Sync Logs: $totalSyncLogs" -ForegroundColor White
    
    # Get completed syncs
    $cmd.CommandText = "SELECT COUNT(*) FROM FS6000SyncLogs WHERE SyncStatus = 'Completed'"
    $completedSyncs = $cmd.ExecuteScalar()
    Write-Host "Completed Syncs: $completedSyncs" -ForegroundColor Green
    
    # Get failed syncs
    $cmd.CommandText = "SELECT COUNT(*) FROM FS6000SyncLogs WHERE SyncStatus = 'Failed'"
    $failedSyncs = $cmd.ExecuteScalar()
    if ($failedSyncs -gt 0) {
        Write-Host "Failed Syncs: $failedSyncs" -ForegroundColor Red
    } else {
        Write-Host "Failed Syncs: $failedSyncs" -ForegroundColor Green
    }
    
    # Get in-progress syncs
    $cmd.CommandText = "SELECT COUNT(*) FROM FS6000SyncLogs WHERE SyncStatus = 'InProgress'"
    $inProgressSyncs = $cmd.ExecuteScalar()
    if ($inProgressSyncs -gt 0) {
        Write-Host "In-Progress Syncs: $inProgressSyncs" -ForegroundColor Yellow
    }
    
    # Get latest completed sync
    $cmd.CommandText = @"
        SELECT TOP 1
            DestinationPath,
            CompletedAt,
            SyncStatus
        FROM FS6000SyncLogs
        WHERE SyncStatus = 'Completed'
        ORDER BY CompletedAt DESC
"@
    $reader = $cmd.ExecuteReader()
    if ($reader.Read()) {
        $lastPath = $reader["DestinationPath"]
        $lastCompleted = $reader["CompletedAt"]
        Write-Host ""
        Write-Host "Last Completed Sync:" -ForegroundColor Cyan
        Write-Host "  Path: $lastPath" -ForegroundColor White
        Write-Host "  Completed: $lastCompleted" -ForegroundColor White
        
        # Calculate time since last sync
        if ($lastCompleted -is [DateTime]) {
            $timeSinceSync = (Get-Date) - $lastCompleted
            if ($timeSinceSync.TotalMinutes -lt 10) {
                Write-Host "  Time since last sync: $([math]::Round($timeSinceSync.TotalMinutes, 1)) minutes ago ✅" -ForegroundColor Green
            } elseif ($timeSinceSync.TotalHours -lt 1) {
                Write-Host "  Time since last sync: $([math]::Round($timeSinceSync.TotalMinutes, 1)) minutes ago ⚠️" -ForegroundColor Yellow
            } else {
                Write-Host "  Time since last sync: $([math]::Round($timeSinceSync.TotalHours, 1)) hours ago ❌" -ForegroundColor Red
            }
        }
    } else {
        Write-Host ""
        Write-Host "⚠️ No completed syncs found" -ForegroundColor Yellow
    }
    $reader.Close()
    
    # Get recent sync history (last 5)
    Write-Host ""
    Write-Host "Recent Sync History (last 5):" -ForegroundColor Cyan
    $cmd.CommandText = @"
        SELECT TOP 5
            DestinationPath,
            SyncStatus,
            CompletedAt,
            CreatedAt
        FROM FS6000SyncLogs
        ORDER BY CreatedAt DESC
"@
    $reader = $cmd.ExecuteReader()
    $count = 0
    while ($reader.Read()) {
        $count++
        $path = $reader["DestinationPath"]
        $status = $reader["SyncStatus"]
        $completed = $reader["CompletedAt"]
        $created = $reader["CreatedAt"]
        $statusColor = if ($status -eq "Completed") { "Green" } elseif ($status -eq "Failed") { "Red" } else { "Yellow" }
        Write-Host "  $count. $status - $path" -ForegroundColor $statusColor
        if ($completed -is [DateTime]) {
            Write-Host "     Completed: $completed" -ForegroundColor White
        } else {
            Write-Host "     Created: $created" -ForegroundColor White
        }
    }
    $reader.Close()
    if ($count -eq 0) {
        Write-Host "  No sync history found" -ForegroundColor Yellow
    }
    
    # Get FS6000 scans count
    Write-Host ""
    $cmd.CommandText = "SELECT COUNT(*) FROM FS6000Scans"
    $totalFs6000Scans = $cmd.ExecuteScalar()
    Write-Host "Total FS6000 Scans: $totalFs6000Scans" -ForegroundColor White
    
    # Get recent scans (last 24 hours)
    $cmd.CommandText = "SELECT COUNT(*) FROM FS6000Scans WHERE CreatedAt >= DATEADD(day, -1, GETDATE())"
    $recentFs6000Scans = $cmd.ExecuteScalar()
    Write-Host "Scans in last 24 hours: $recentFs6000Scans" -ForegroundColor White
    
} catch {
    Write-Host "❌ Error checking FS6000 sync: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "CHECK COMPLETE" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$connection.Close()

