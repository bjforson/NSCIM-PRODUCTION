# Diagnose duplicate download issue
param(
    [string]$ConnectionString = ""
)

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "Duplicate Download Diagnostic" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

if ([string]::IsNullOrEmpty($ConnectionString)) {
    $apiPath = "src\NickScanCentralImagingPortal.API"
    $appsettingsPath = Join-Path $apiPath "appsettings.json"
    $appsettingsDevPath = Join-Path $apiPath "appsettings.Development.json"
    
    if (Test-Path $appsettingsPath) {
        $appsettings = Get-Content $appsettingsPath | ConvertFrom-Json
        $ConnectionString = $appsettings.ConnectionStrings.ICUMS_Downloads_Connection
        
        if (Test-Path $appsettingsDevPath) {
            $appsettingsDev = Get-Content $appsettingsDevPath | ConvertFrom-Json
            if ($appsettingsDev.ConnectionStrings.ICUMS_Downloads_Connection) {
                $ConnectionString = $appsettingsDev.ConnectionStrings.ICUMS_Downloads_Connection
            }
        }
    }
}

if ([string]::IsNullOrEmpty($ConnectionString)) {
    Write-Host "ERROR: Connection string not found. Please provide it as a parameter:" -ForegroundColor Red
    Write-Host "   .\DiagnoseDuplicateDownloads.ps1 -ConnectionString 'Server=...;Database=ICUMS_Downloads;...'" -ForegroundColor Yellow
    exit 1
}

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
    $connection.Open()

    Write-Host "1. Checking unique constraints on BOEDocuments..." -ForegroundColor Yellow
    $constraintQuery = @"
    SELECT 
        i.name AS IndexName,
        i.is_unique AS IsUnique,
        i.is_unique_constraint AS IsUniqueConstraint,
        STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS Columns,
        i.filter_definition AS FilterDefinition
    FROM sys.indexes i
    INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
    INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
    WHERE i.object_id = OBJECT_ID('BOEDocuments')
      AND i.is_unique = 1
    GROUP BY i.name, i.is_unique, i.is_unique_constraint, i.filter_definition
    ORDER BY i.name
"@
    $commandConstraint = New-Object System.Data.SqlClient.SqlCommand($constraintQuery, $connection)
    $adapterConstraint = New-Object System.Data.SqlClient.SqlDataAdapter($commandConstraint)
    $datasetConstraint = New-Object System.Data.DataSet
    $adapterConstraint.Fill($datasetConstraint) | Out-Null
    
    if ($datasetConstraint.Tables[0].Rows.Count -eq 0) {
        Write-Host "   WARNING: No unique constraints found on BOEDocuments!" -ForegroundColor Red
    } else {
        Write-Host "   Found unique constraints:" -ForegroundColor Cyan
        foreach ($row in $datasetConstraint.Tables[0].Rows) {
            $indexName = $row["IndexName"]
            $columns = $row["Columns"]
            $filter = $row["FilterDefinition"]
            Write-Host "     - $indexName on ($columns)" -ForegroundColor Green
            if (![string]::IsNullOrEmpty($filter)) {
                Write-Host "       Filter: $filter" -ForegroundColor Gray
            }
        }
    }
    Write-Host ""

    Write-Host "2. Checking for duplicate ContainerNumbers (ignoring DeclarationNumber)..." -ForegroundColor Yellow
    $duplicateQuery = @"
    SELECT 
        ContainerNumber,
        COUNT(*) AS DocumentCount,
        COUNT(DISTINCT DeclarationNumber) AS DeclarationCount,
        MIN(CreatedAt) AS FirstCreated,
        MAX(CreatedAt) AS LastCreated,
        STRING_AGG(CAST(Id AS VARCHAR), ', ') WITHIN GROUP (ORDER BY CreatedAt) AS DocumentIds,
        STRING_AGG(ISNULL(DeclarationNumber, 'NULL'), ', ') WITHIN GROUP (ORDER BY CreatedAt) AS DeclarationNumbers
    FROM BOEDocuments
    WHERE CreatedAt >= DATEADD(HOUR, -24, GETUTCDATE())
    GROUP BY ContainerNumber
    HAVING COUNT(*) > 1
    ORDER BY COUNT(*) DESC, MAX(CreatedAt) DESC
"@
    $commandDup = New-Object System.Data.SqlClient.SqlCommand($duplicateQuery, $connection)
    $adapterDup = New-Object System.Data.SqlClient.SqlDataAdapter($commandDup)
    $datasetDup = New-Object System.Data.DataSet
    $adapterDup.Fill($datasetDup) | Out-Null
    
    if ($datasetDup.Tables[0].Rows.Count -eq 0) {
        Write-Host "   OK: No duplicate ContainerNumbers found in last 24 hours" -ForegroundColor Green
    } else {
        Write-Host "   Found $($datasetDup.Tables[0].Rows.Count) container(s) with multiple BOE documents:" -ForegroundColor Red
        Write-Host ""
        
        $totalDuplicates = 0
        foreach ($row in $datasetDup.Tables[0].Rows) {
            $container = $row["ContainerNumber"]
            $docCount = $row["DocumentCount"]
            $declCount = $row["DeclarationCount"]
            $firstCreated = $row["FirstCreated"]
            $lastCreated = $row["LastCreated"]
            $docIds = $row["DocumentIds"]
            $declNumbers = $row["DeclarationNumbers"]
            
            $totalDuplicates += ($docCount - 1) # Count extra documents
            
            Write-Host "   Container: $container" -ForegroundColor White
            Write-Host "     Documents: $docCount" -ForegroundColor $(if ($docCount -gt 2) { "Red" } else { "Yellow" })
            Write-Host "     Declaration Numbers: $declCount unique" -ForegroundColor $(if ($declCount -gt 1) { "Cyan" } else { "Yellow" })
            Write-Host "     Time Range: $firstCreated to $lastCreated" -ForegroundColor Gray
            Write-Host "     Document IDs: $docIds" -ForegroundColor Gray
            Write-Host "     Declaration Numbers: $declNumbers" -ForegroundColor Gray
            Write-Host ""
        }
        
        Write-Host "   Total extra documents (duplicates): $totalDuplicates" -ForegroundColor Red
    }
    Write-Host ""

    Write-Host "3. Checking download sources for duplicates..." -ForegroundColor Yellow
    $sourceQuery = @"
    SELECT 
        b.ContainerNumber,
        b.DeclarationNumber,
        b.CreatedAt,
        df.FileName,
        CASE 
            WHEN df.FileName LIKE 'BatchData_%' THEN 'IcumBackgroundService-BatchAPI'
            WHEN df.FileName LIKE 'Queue_%' THEN 'ICUMSDownloadBackgroundService-IndividualAPI'
            ELSE 'Unknown'
        END AS DownloadSource
    FROM BOEDocuments b
    INNER JOIN DownloadedFiles df ON b.DownloadedFileId = df.Id
    WHERE b.CreatedAt >= DATEADD(HOUR, -24, GETUTCDATE())
      AND b.ContainerNumber IN (
          SELECT ContainerNumber 
          FROM BOEDocuments 
          WHERE CreatedAt >= DATEADD(HOUR, -24, GETUTCDATE())
          GROUP BY ContainerNumber 
          HAVING COUNT(*) > 1
      )
    ORDER BY b.ContainerNumber, b.CreatedAt
"@
    $commandSource = New-Object System.Data.SqlClient.SqlCommand($sourceQuery, $connection)
    $adapterSource = New-Object System.Data.SqlClient.SqlDataAdapter($commandSource)
    $datasetSource = New-Object System.Data.DataSet
    $adapterSource.Fill($datasetSource) | Out-Null
    
    if ($datasetSource.Tables[0].Rows.Count -gt 0) {
        Write-Host "   Download sources for duplicate containers:" -ForegroundColor Cyan
        $currentContainer = ""
        foreach ($row in $datasetSource.Tables[0].Rows) {
            $container = $row["ContainerNumber"]
            $decl = $row["DeclarationNumber"]
            $created = $row["CreatedAt"]
            $source = $row["DownloadSource"]
            $fileName = $row["FileName"]
            
            if ($container -ne $currentContainer) {
                if ($currentContainer -ne "") { Write-Host "" }
                $currentContainer = $container
                Write-Host "   $container :" -ForegroundColor White
            }
            
            Write-Host "     - $created : $source ($fileName)" -ForegroundColor $(if ($source -like "*Batch*") { "Yellow" } else { "Cyan" })
            if (![string]::IsNullOrEmpty($decl)) {
                Write-Host "       Declaration: $decl" -ForegroundColor Gray
            }
        }
        Write-Host ""
    }

    Write-Host "4. Checking timing patterns..." -ForegroundColor Yellow
    $timingQuery = @"
    SELECT 
        ContainerNumber,
        COUNT(*) AS DownloadCount,
        MIN(CreatedAt) AS FirstDownload,
        MAX(CreatedAt) AS LastDownload,
        DATEDIFF(MINUTE, MIN(CreatedAt), MAX(CreatedAt)) AS MinutesBetween
    FROM BOEDocuments
    WHERE CreatedAt >= DATEADD(HOUR, -24, GETUTCDATE())
    GROUP BY ContainerNumber
    HAVING COUNT(*) > 1
    ORDER BY COUNT(*) DESC
"@
    $commandTiming = New-Object System.Data.SqlClient.SqlCommand($timingQuery, $connection)
    $adapterTiming = New-Object System.Data.SqlClient.SqlDataAdapter($commandTiming)
    $datasetTiming = New-Object System.Data.DataSet
    $adapterTiming.Fill($datasetTiming) | Out-Null
    
    if ($datasetTiming.Tables[0].Rows.Count -gt 0) {
        Write-Host "   Timing analysis:" -ForegroundColor Cyan
        $sameDeclCount = 0
        $diffDeclCount = 0
        
        foreach ($row in $datasetTiming.Tables[0].Rows) {
            $container = $row["ContainerNumber"]
            $count = $row["DownloadCount"]
            $first = $row["FirstDownload"]
            $last = $row["LastDownload"]
            $minutes = $row["MinutesBetween"]
            
            # Check if same declaration number
            $declCheckQuery = "SELECT COUNT(DISTINCT DeclarationNumber) AS DeclCount FROM BOEDocuments WHERE ContainerNumber = @Container AND CreatedAt >= DATEADD(HOUR, -24, GETUTCDATE())"
            $commandDecl = New-Object System.Data.SqlClient.SqlCommand($declCheckQuery, $connection)
            $param = $commandDecl.Parameters.AddWithValue("@Container", $container)
            $declCount = [int]$commandDecl.ExecuteScalar()
            
            if ($declCount -eq 1) {
                $sameDeclCount++
                Write-Host "     $container : $count downloads, $minutes minutes apart (SAME DeclarationNumber - TRUE DUPLICATE)" -ForegroundColor Red
            } else {
                $diffDeclCount++
                Write-Host "     $container : $count downloads, $minutes minutes apart ($declCount different DeclarationNumbers - may be valid)" -ForegroundColor Yellow
            }
        }
        
        Write-Host ""
        Write-Host "   Summary:" -ForegroundColor Cyan
        Write-Host "     True duplicates (same DeclarationNumber): $sameDeclCount" -ForegroundColor $(if ($sameDeclCount -gt 0) { "Red" } else { "Green" })
        Write-Host "     Different DeclarationNumbers: $diffDeclCount" -ForegroundColor $(if ($diffDeclCount -gt 0) { "Yellow" } else { "Green" })
    }
    Write-Host ""

    Write-Host "5. Checking if both services are downloading same containers..." -ForegroundColor Yellow
    $serviceQuery = @"
    WITH SourceTypes AS (
        SELECT DISTINCT
            b.ContainerNumber,
            CASE 
                WHEN df.FileName LIKE 'BatchData_%' THEN 'Batch'
                WHEN df.FileName LIKE 'Queue_%' THEN 'Queue'
                ELSE 'Unknown'
            END AS SourceType,
            CASE 
                WHEN df.FileName LIKE 'BatchData_%' THEN 'IcumBackgroundService-BatchAPI'
                WHEN df.FileName LIKE 'Queue_%' THEN 'ICUMSDownloadBackgroundService-IndividualAPI'
                ELSE 'Unknown'
            END AS SourceName
        FROM BOEDocuments b
        INNER JOIN DownloadedFiles df ON b.DownloadedFileId = df.Id
        WHERE b.CreatedAt >= DATEADD(HOUR, -24, GETUTCDATE())
          AND b.ContainerNumber IN (
              SELECT ContainerNumber 
              FROM BOEDocuments 
              WHERE CreatedAt >= DATEADD(HOUR, -24, GETUTCDATE())
              GROUP BY ContainerNumber 
              HAVING COUNT(*) > 1
          )
    )
    SELECT 
        ContainerNumber,
        COUNT(DISTINCT SourceType) AS SourceCount,
        STRING_AGG(SourceName, ', ') WITHIN GROUP (ORDER BY SourceName) AS Sources
    FROM SourceTypes
    GROUP BY ContainerNumber
    HAVING COUNT(DISTINCT SourceType) > 1
"@
    $commandService = New-Object System.Data.SqlClient.SqlCommand($serviceQuery, $connection)
    $adapterService = New-Object System.Data.SqlClient.SqlDataAdapter($commandService)
    $datasetService = New-Object System.Data.DataSet
    $adapterService.Fill($datasetService) | Out-Null
    
    if ($datasetService.Tables[0].Rows.Count -gt 0) {
        Write-Host "   WARNING: Found containers downloaded by multiple services:" -ForegroundColor Red
        foreach ($row in $datasetService.Tables[0].Rows) {
            $container = $row["ContainerNumber"]
            $sources = $row["Sources"]
            Write-Host "     $container : $sources" -ForegroundColor Yellow
        }
        Write-Host ""
        Write-Host "   This indicates a race condition or deduplication failure!" -ForegroundColor Red
    } else {
        Write-Host "   OK: No containers downloaded by multiple services" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "Diagnostic complete!" -ForegroundColor Green

} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.Exception.StackTrace -ForegroundColor Red
    exit 1
} finally {
    if ($connection.State -eq 'Open') {
        $connection.Close()
    }
}

