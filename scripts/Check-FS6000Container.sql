-- Check FS6000 Container Data
-- Usage: Run this query in SQL Server Management Studio, replacing 'MRSU7761986' with your container number
-- Or: sqlcmd -S 127.0.0.1 -d NS_CIS -E -i Check-FS6000Container.sql

DECLARE @ContainerNumber NVARCHAR(50) = 'MRSU7761986';

-- Check if container exists in FS6000Scans
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
WHERE ContainerNumber = @ContainerNumber
ORDER BY ScanTime DESC;

-- Check image count for this container
SELECT 
    fs.Id AS ScanId,
    fs.ContainerNumber,
    fs.ScanTime,
    COUNT(fi.Id) AS ImageCount,
    SUM(CASE WHEN fi.ImageData IS NOT NULL THEN 1 ELSE 0 END) AS ImagesWithData
FROM FS6000Scans fs
LEFT JOIN FS6000Images fi ON fs.Id = fi.ScanId
WHERE fs.ContainerNumber = @ContainerNumber
GROUP BY fs.Id, fs.ContainerNumber, fs.ScanTime;

