-- =====================================================
-- SQL Query to Check if Image Exists in Database
-- Container: TRHU8239270
-- Image Type: ASE
-- =====================================================

-- =====================================================
-- 1. CHECK ASE IMAGE (for container TRHU8239270)
-- =====================================================

-- Check if ASE scan exists and has image data
SELECT 
    Id,
    ContainerNumber,
    InspectionId,
    ScanTime,
    InspectionUuid,
    TruckPlate,
    ImageDisplayName,
    CASE 
        WHEN ScanImage IS NULL THEN 'NO IMAGE DATA (NULL)'
        WHEN DATALENGTH(ScanImage) = 0 THEN 'NO IMAGE DATA (EMPTY)'
        ELSE CONCAT('IMAGE EXISTS (', FORMAT(DATALENGTH(ScanImage) / 1024.0, 'N2'), ' KB)')
    END AS ImageStatus,
    DATALENGTH(ScanImage) AS ImageSizeBytes,
    SyncedAt,
    CreatedAt,
    UpdatedAt
FROM AseScans
WHERE ContainerNumber = 'TRHU8239270'
ORDER BY ScanTime DESC;

-- Summary: Count of ASE scans with/without images
SELECT 
    ContainerNumber,
    COUNT(*) AS TotalScans,
    SUM(CASE WHEN ScanImage IS NOT NULL AND DATALENGTH(ScanImage) > 0 THEN 1 ELSE 0 END) AS ScansWithImage,
    SUM(CASE WHEN ScanImage IS NULL OR DATALENGTH(ScanImage) = 0 THEN 1 ELSE 0 END) AS ScansWithoutImage
FROM AseScans
WHERE ContainerNumber = 'TRHU8239270'
GROUP BY ContainerNumber;

-- =====================================================
-- 2. CHECK FS6000 IMAGES (if needed for other containers)
-- =====================================================

-- Check FS6000 scan and images
SELECT 
    fs.Id AS ScanId,
    fs.ContainerNumber,
    fs.ScanDateTime,
    fs.FilePath,
    COUNT(fi.Id) AS ImageCount,
    STRING_AGG(fi.ImageType, ', ') AS ImageTypes
FROM FS6000Scans fs
LEFT JOIN FS6000Images fi ON fi.ScanId = fs.Id
WHERE fs.ContainerNumber = 'TRHU8239270'
GROUP BY fs.Id, fs.ContainerNumber, fs.ScanDateTime, fs.FilePath
ORDER BY fs.ScanDateTime DESC;

-- Detailed FS6000 images
SELECT 
    fs.ContainerNumber,
    fs.ScanDateTime,
    fi.Id AS ImageId,
    fi.ImageType,
    fi.FileName,
    CASE 
        WHEN fi.ImageData IS NULL THEN 'NO IMAGE DATA (NULL)'
        WHEN DATALENGTH(fi.ImageData) = 0 THEN 'NO IMAGE DATA (EMPTY)'
        ELSE CONCAT('IMAGE EXISTS (', FORMAT(DATALENGTH(fi.ImageData) / 1024.0, 'N2'), ' KB)')
    END AS ImageStatus,
    DATALENGTH(fi.ImageData) AS ImageSizeBytes,
    fi.FileSizeBytes,
    fi.CreatedAt
FROM FS6000Scans fs
INNER JOIN FS6000Images fi ON fi.ScanId = fs.Id
WHERE fs.ContainerNumber = 'TRHU8239270'
ORDER BY fs.ScanDateTime DESC, fi.ImageType;

-- =====================================================
-- 3. QUICK CHECK (Simple Yes/No)
-- =====================================================

-- Quick check for ASE image
SELECT 
    'ASE' AS ScannerType,
    ContainerNumber,
    CASE 
        WHEN EXISTS (
            SELECT 1 
            FROM AseScans 
            WHERE ContainerNumber = 'TRHU8239270' 
            AND ScanImage IS NOT NULL 
            AND DATALENGTH(ScanImage) > 0
        ) THEN 'YES - Image exists'
        ELSE 'NO - Image not found or empty'
    END AS ImageExists
FROM AseScans
WHERE ContainerNumber = 'TRHU8239270'
GROUP BY ContainerNumber;

-- Quick check for FS6000 images
SELECT 
    'FS6000' AS ScannerType,
    ContainerNumber,
    CASE 
        WHEN EXISTS (
            SELECT 1 
            FROM FS6000Scans fs
            INNER JOIN FS6000Images fi ON fi.ScanId = fs.Id
            WHERE fs.ContainerNumber = 'TRHU8239270'
            AND fi.ImageData IS NOT NULL
            AND DATALENGTH(fi.ImageData) > 0
        ) THEN CONCAT('YES - ', COUNT(DISTINCT fi.Id), ' image(s) exist')
        ELSE 'NO - No images found'
    END AS ImageExists
FROM FS6000Scans fs
LEFT JOIN FS6000Images fi ON fi.ScanId = fs.Id
WHERE fs.ContainerNumber = 'TRHU8239270'
GROUP BY ContainerNumber;

-- =====================================================
-- 4. COMPREHENSIVE CHECK (All Scanner Types)
-- =====================================================

-- Check all scanner types for the container
SELECT 
    'ASE' AS ScannerType,
    ContainerNumber,
    COUNT(*) AS RecordCount,
    SUM(CASE WHEN ScanImage IS NOT NULL AND DATALENGTH(ScanImage) > 0 THEN 1 ELSE 0 END) AS RecordsWithImage,
    MAX(ScanTime) AS LatestScanTime
FROM AseScans
WHERE ContainerNumber = 'TRHU8239270'
GROUP BY ContainerNumber

UNION ALL

SELECT 
    'FS6000' AS ScannerType,
    ContainerNumber,
    COUNT(DISTINCT fs.Id) AS RecordCount,
    COUNT(fi.Id) AS RecordsWithImage,
    MAX(fs.ScanDateTime) AS LatestScanTime
FROM FS6000Scans fs
LEFT JOIN FS6000Images fi ON fi.ScanId = fs.Id
WHERE fs.ContainerNumber = 'TRHU8239270'
GROUP BY ContainerNumber;

-- =====================================================
-- 5. DETAILED DIAGNOSTIC (Most Useful)
-- =====================================================

-- Detailed diagnostic for container TRHU8239270
DECLARE @ContainerNumber NVARCHAR(50) = 'TRHU8239270';

-- ASE Scan Details
SELECT 
    'ASE Scan Details' AS CheckType,
    Id AS RecordId,
    ContainerNumber,
    InspectionId,
    ScanTime,
    InspectionUuid,
    ImageDisplayName,
    CASE 
        WHEN ScanImage IS NULL THEN '❌ NULL'
        WHEN DATALENGTH(ScanImage) = 0 THEN '❌ EMPTY'
        ELSE CONCAT('✅ ', FORMAT(DATALENGTH(ScanImage) / 1024.0, 'N2'), ' KB')
    END AS ImageStatus,
    DATALENGTH(ScanImage) AS ImageSizeBytes,
    CreatedAt,
    UpdatedAt
FROM AseScans
WHERE ContainerNumber = @ContainerNumber

UNION ALL

-- FS6000 Scan Details
SELECT 
    'FS6000 Scan Details' AS CheckType,
    CAST(fs.Id AS NVARCHAR(50)) AS RecordId,
    fs.ContainerNumber,
    NULL AS InspectionId,
    fs.ScanDateTime AS ScanTime,
    NULL AS InspectionUuid,
    fs.FilePath AS ImageDisplayName,
    CASE 
        WHEN COUNT(fi.Id) = 0 THEN '❌ NO IMAGES'
        ELSE CONCAT('✅ ', COUNT(fi.Id), ' image(s)')
    END AS ImageStatus,
    SUM(DATALENGTH(fi.ImageData)) AS ImageSizeBytes,
    MIN(fs.CreatedAt) AS CreatedAt,
    MAX(fs.UpdatedAt) AS UpdatedAt
FROM FS6000Scans fs
LEFT JOIN FS6000Images fi ON fi.ScanId = fs.Id
WHERE fs.ContainerNumber = @ContainerNumber
GROUP BY fs.Id, fs.ContainerNumber, fs.ScanDateTime, fs.FilePath, fs.CreatedAt, fs.UpdatedAt

ORDER BY CheckType, ScanTime DESC;

