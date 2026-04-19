# Check FS6000 Container Data
# Usage: .\Check-FS6000Container.ps1 -ContainerNumber "MRSU7761986"

param(
    [Parameter(Mandatory=$true)]
    [string]$ContainerNumber
)

# Database connection (from appsettings.json)
$connectionString = "Server=127.0.0.1,1433;Database=NS_CIS;Trusted_Connection=true;MultipleActiveResultSets=true;Encrypt=true;TrustServerCertificate=true;"

$sqlQuery = @"
SELECT 
    Id,
    ContainerNumber,
    ScanTime,
    FilePath,
    SyncStatus,
    HasImage,
    CreatedAt,
    UpdatedAt
FROM FS6000Scans
WHERE ContainerNumber = '$ContainerNumber'
ORDER BY ScanTime DESC;
"@

try {
    Write-Host "Checking FS6000 data for container: $ContainerNumber" -ForegroundColor Cyan
    Write-Host ""
    
    # Create SQL connection
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    # Execute query
    $command = $connection.CreateCommand()
    $command.CommandText = $sqlQuery
    
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $command
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    
    $connection.Close()
    
    $results = $dataset.Tables[0]
    
    if ($results.Rows.Count -eq 0) {
        Write-Host "NO DATA FOUND" -ForegroundColor Red
        Write-Host "   Container '$ContainerNumber' does not exist in FS6000Scans table." -ForegroundColor Yellow
    } else {
        $count = $results.Rows.Count
        Write-Host "FOUND $count RECORD(S)" -ForegroundColor Green
        Write-Host ""
        Write-Host "FS6000 Scan Records:" -ForegroundColor Cyan
        Write-Host ("-" * 120)
        
        foreach ($row in $results.Rows) {
            Write-Host "Id:              $($row['Id'])" -ForegroundColor White
            Write-Host "Container:       $($row['ContainerNumber'])" -ForegroundColor White
            Write-Host "Scan Time:       $($row['ScanTime'])" -ForegroundColor White
            Write-Host "File Path:       $($row['FilePath'])" -ForegroundColor White
            Write-Host "Sync Status:     $($row['SyncStatus'])" -ForegroundColor White
            Write-Host "Has Image:       $($row['HasImage'])" -ForegroundColor White
            Write-Host "Created:         $($row['CreatedAt'])" -ForegroundColor White
            Write-Host "Updated:         $($row['UpdatedAt'])" -ForegroundColor White
            Write-Host ("-" * 120)
        }
    }
    
    # Also check if there are images
    Write-Host ""
    Write-Host "Checking for images..." -ForegroundColor Cyan
    
    $imageQuery = @"
SELECT 
    fs.Id AS ScanId,
    fs.ContainerNumber,
    fs.ScanTime,
    COUNT(fi.Id) AS ImageCount,
    SUM(CASE WHEN fi.ImageData IS NOT NULL THEN 1 ELSE 0 END) AS ImagesWithData
FROM FS6000Scans fs
LEFT JOIN FS6000Images fi ON fs.Id = fi.ScanId
WHERE fs.ContainerNumber = '$ContainerNumber'
GROUP BY fs.Id, fs.ContainerNumber, fs.ScanTime;
"@
    
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    $command = $connection.CreateCommand()
    $command.CommandText = $imageQuery
    
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $command
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    
    $connection.Close()
    
    $imageResults = $dataset.Tables[0]
    
    if ($imageResults.Rows.Count -gt 0) {
        foreach ($row in $imageResults.Rows) {
            Write-Host "Scan ID: $($row['ScanId']) - Images: $($row['ImageCount']) total, $($row['ImagesWithData']) with data" -ForegroundColor Green
        }
    } else {
        Write-Host "   No images found for this container." -ForegroundColor Yellow
    }
    
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "   Stack Trace: $($_.ScriptStackTrace)" -ForegroundColor Red
}

