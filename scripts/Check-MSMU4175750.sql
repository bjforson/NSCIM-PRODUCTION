-- =====================================================
-- Check if MSMU4175750 exists in the database
-- =====================================================

DECLARE @ContainerNumber NVARCHAR(50) = 'MSMU4175750';

PRINT '=== Summary: Does MSMU4175750 exist? ===';

-- 1. AseScans (ASE scanner images - NS_CIS)
SELECT 
    'AseScans' AS Source,
    COUNT(*) AS RecordCount,
    MAX(ScanTime) AS LatestScan
FROM AseScans
WHERE ContainerNumber = @ContainerNumber;

-- 2. FS6000Scans (exact match or comma-separated list)
SELECT 
    'FS6000Scans' AS Source,
    COUNT(*) AS RecordCount,
    MAX(ScanTime) AS LatestScan
FROM FS6000Scans
WHERE ContainerNumber = @ContainerNumber
   OR ContainerNumber LIKE @ContainerNumber + ',%'
   OR ContainerNumber LIKE '%,' + @ContainerNumber + ',%'
   OR ContainerNumber LIKE '%,' + @ContainerNumber;

-- 3. ContainerCompletenessStatuses
IF OBJECT_ID('dbo.ContainerCompletenessStatuses', 'U') IS NOT NULL
SELECT 
    'ContainerCompletenessStatuses' AS Source,
    COUNT(*) AS RecordCount,
    MAX(UpdatedAt) AS LatestUpdate
FROM ContainerCompletenessStatuses
WHERE ContainerNumber = @ContainerNumber;

-- 4. ContainerScanQueues
IF OBJECT_ID('dbo.ContainerScanQueues', 'U') IS NOT NULL
SELECT 
    'ContainerScanQueues' AS Source,
    COUNT(*) AS RecordCount,
    MAX(QueuedAt) AS LatestQueued
FROM ContainerScanQueues
WHERE ContainerNumber = @ContainerNumber;

-- 5. IcumContainerData (in ICUMS DB - run separately: sqlcmd -d ICUMS)
IF OBJECT_ID('dbo.IcumContainerData', 'U') IS NOT NULL
SELECT 
    'IcumContainerData' AS Source,
    COUNT(*) AS RecordCount,
    MAX(UpdatedAt) AS LatestUpdate
FROM IcumContainerData
WHERE ContainerNumber = @ContainerNumber;

PRINT '';
PRINT '=== Detailed ASE scan(s) ===';
SELECT 
    Id, ContainerNumber, InspectionId, ScanTime, ImageDisplayName,
    CASE WHEN ScanImage IS NOT NULL AND DATALENGTH(ScanImage) > 0 
         THEN 'YES' ELSE 'NO' END AS HasImage
FROM AseScans
WHERE ContainerNumber = @ContainerNumber
ORDER BY ScanTime DESC;

PRINT '';
PRINT '=== Detailed FS6000 scan(s) ===';
SELECT 
    fs.Id, fs.ContainerNumber, fs.ScanTime, fs.FilePath,
    COUNT(fi.Id) AS ImageCount
FROM FS6000Scans fs
LEFT JOIN FS6000Images fi ON fi.ScanId = fs.Id
WHERE fs.ContainerNumber = @ContainerNumber
   OR fs.ContainerNumber LIKE @ContainerNumber + ',%'
   OR fs.ContainerNumber LIKE '%,' + @ContainerNumber + ',%'
   OR fs.ContainerNumber LIKE '%,' + @ContainerNumber
GROUP BY fs.Id, fs.ContainerNumber, fs.ScanTime, fs.FilePath
ORDER BY fs.ScanTime DESC;
