-- Script to check BOE 40825476816 for all attached containers and find which have images
-- This version checks FS6000Images (via FS6000Scans), AseScans, and ImageCaches tables

-- Step 1: Get all containers for this BOE/Declaration from ICUMS_Downloads database
USE ICUMS_Downloads;
GO

PRINT '========================================';
PRINT 'BOE Container Image Checker';
PRINT '========================================';
PRINT 'BOE/Declaration Number: 40825476816';
PRINT '';

-- Get containers from BOEDocuments - use global temp table (##) so it persists across database switches
IF OBJECT_ID('tempdb..##BOEContainers') IS NOT NULL
    DROP TABLE ##BOEContainers;

SELECT 
    ContainerNumber,
    DeclarationNumber,
    IsConsolidated,
    BlNumber,
    ClearanceType,
    ConsigneeName,
    RotationNumber
INTO ##BOEContainers
FROM BOEDocuments
WHERE DeclarationNumber = '40825476816'
    AND ContainerNumber IS NOT NULL
    AND ContainerNumber != '';

DECLARE @ContainerCount INT;
SELECT @ContainerCount = COUNT(DISTINCT ContainerNumber) FROM ##BOEContainers;
PRINT 'Found ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' container record(s) for BOE 40825476816';
PRINT 'Checking ' + CAST(@ContainerCount AS NVARCHAR(10)) + ' unique container(s) for images...';
PRINT '';

-- Step 2: Check which containers have images in NS_CIS database
USE NS_CIS;
GO

-- Create temp table for image counts
IF OBJECT_ID('tempdb..##ImageCounts') IS NOT NULL
    DROP TABLE ##ImageCounts;

CREATE TABLE ##ImageCounts (
    ContainerNumber NVARCHAR(50),
    FS6000ImageCount INT DEFAULT 0,
    AseImageCount INT DEFAULT 0,
    CacheImageCount INT DEFAULT 0
);

-- Insert container numbers
INSERT INTO ##ImageCounts (ContainerNumber)
SELECT DISTINCT ContainerNumber FROM ##BOEContainers;

-- Update counts from FS6000Images (join through FS6000Scans)
IF OBJECT_ID('FS6000Images', 'U') IS NOT NULL AND OBJECT_ID('FS6000Scans', 'U') IS NOT NULL
BEGIN
    UPDATE ic
    SET ic.FS6000ImageCount = img.Cnt
    FROM ##ImageCounts ic
    INNER JOIN (
        SELECT fs.ContainerNumber, COUNT(*) AS Cnt
        FROM FS6000Images fi
        INNER JOIN FS6000Scans fs ON fi.ScanId = fs.Id
        WHERE fs.ContainerNumber IN (SELECT ContainerNumber FROM ##BOEContainers)
        GROUP BY fs.ContainerNumber
    ) img ON ic.ContainerNumber = img.ContainerNumber;
END

-- Update counts from AseScans (if table exists)
IF OBJECT_ID('AseScans', 'U') IS NOT NULL
BEGIN
    UPDATE ic
    SET ic.AseImageCount = img.Cnt
    FROM ##ImageCounts ic
    INNER JOIN (
        SELECT ContainerNumber, COUNT(*) AS Cnt
        FROM AseScans
        WHERE ContainerNumber IN (SELECT ContainerNumber FROM ##BOEContainers)
            AND ScanImage IS NOT NULL
        GROUP BY ContainerNumber
    ) img ON ic.ContainerNumber = img.ContainerNumber;
END

-- Update counts from ImageCaches (if table exists)
IF OBJECT_ID('ImageCaches', 'U') IS NOT NULL
BEGIN
    UPDATE ic
    SET ic.CacheImageCount = img.Cnt
    FROM ##ImageCounts ic
    INNER JOIN (
        SELECT ContainerNumber, COUNT(*) AS Cnt
        FROM ImageCaches
        WHERE ContainerNumber IN (SELECT ContainerNumber FROM ##BOEContainers)
        GROUP BY ContainerNumber
    ) img ON ic.ContainerNumber = img.ContainerNumber;
END

-- Main results query
SELECT 
    bc.ContainerNumber,
    bc.DeclarationNumber,
    bc.IsConsolidated,
    bc.ClearanceType,
    bc.ConsigneeName,
    CASE 
        WHEN (ic.FS6000ImageCount + ic.AseImageCount + ic.CacheImageCount) > 0 THEN 1
        ELSE 0
    END AS HasImages,
    (ic.FS6000ImageCount + ic.AseImageCount + ic.CacheImageCount) AS TotalImageCount,
    ISNULL(c.ScannerType, 'N/A') AS ContainerScannerType
FROM ##BOEContainers bc
LEFT JOIN ##ImageCounts ic ON bc.ContainerNumber = ic.ContainerNumber
LEFT JOIN Containers c ON bc.ContainerNumber = c.ContainerId
ORDER BY HasImages DESC, bc.ContainerNumber;

-- Summary
PRINT '';
PRINT '========================================';
PRINT 'SUMMARY';
PRINT '========================================';

DECLARE @WithImages INT;
DECLARE @WithoutImages INT;
DECLARE @TotalImages INT;
DECLARE @TotalContainers INT;

SELECT @TotalContainers = COUNT(DISTINCT ContainerNumber) FROM ##BOEContainers;

SELECT 
    @WithImages = COUNT(DISTINCT CASE 
        WHEN (ic.FS6000ImageCount + ic.AseImageCount + ic.CacheImageCount) > 0
        THEN bc.ContainerNumber 
    END),
    @WithoutImages = COUNT(DISTINCT CASE 
        WHEN (ic.FS6000ImageCount + ic.AseImageCount + ic.CacheImageCount) = 0
        THEN bc.ContainerNumber 
    END),
    @TotalImages = SUM(ic.FS6000ImageCount + ic.AseImageCount + ic.CacheImageCount)
FROM ##BOEContainers bc
LEFT JOIN ##ImageCounts ic ON bc.ContainerNumber = ic.ContainerNumber;

PRINT 'Total Containers: ' + CAST(@TotalContainers AS NVARCHAR(10));
PRINT 'Containers WITH Images: ' + CAST(@WithImages AS NVARCHAR(10));
PRINT 'Containers WITHOUT Images: ' + CAST(@WithoutImages AS NVARCHAR(10));
PRINT 'Total Images: ' + CAST(@TotalImages AS NVARCHAR(10));
PRINT '';

-- List containers WITH images
PRINT 'Containers WITH Images:';
SELECT DISTINCT
    bc.ContainerNumber,
    ISNULL(c.ScannerType, 'N/A') AS ScannerType,
    (ic.FS6000ImageCount + ic.AseImageCount + ic.CacheImageCount) AS ImageCount
FROM ##BOEContainers bc
LEFT JOIN ##ImageCounts ic ON bc.ContainerNumber = ic.ContainerNumber
LEFT JOIN Containers c ON bc.ContainerNumber = c.ContainerId
WHERE (ic.FS6000ImageCount + ic.AseImageCount + ic.CacheImageCount) > 0
ORDER BY bc.ContainerNumber;

-- List containers WITHOUT images
PRINT '';
PRINT 'Containers WITHOUT Images:';
SELECT DISTINCT
    bc.ContainerNumber,
    ISNULL(bc.ClearanceType, 'N/A') AS ClearanceType,
    ISNULL(bc.ConsigneeName, 'N/A') AS ConsigneeName,
    ISNULL(c.ScannerType, 'N/A') AS ContainerScannerType
FROM ##BOEContainers bc
LEFT JOIN ##ImageCounts ic ON bc.ContainerNumber = ic.ContainerNumber
LEFT JOIN Containers c ON bc.ContainerNumber = c.ContainerId
WHERE (ic.FS6000ImageCount + ic.AseImageCount + ic.CacheImageCount) = 0
ORDER BY bc.ContainerNumber;

-- Cleanup
DROP TABLE ##BOEContainers;
DROP TABLE ##ImageCounts;

PRINT '';
PRINT '========================================';
PRINT 'Query completed successfully!';
PRINT '========================================';
