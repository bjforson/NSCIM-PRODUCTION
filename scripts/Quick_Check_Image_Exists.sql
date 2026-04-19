-- =====================================================
-- QUICK CHECK: Does Image Exist for Container?
-- Container: TRHU8239270
-- =====================================================
-- Replace 'TRHU8239270' with your container number
-- =====================================================

DECLARE @ContainerNumber NVARCHAR(50) = 'TRHU8239270';

-- ✅ SIMPLE CHECK: Does ASE image exist?
SELECT 
    CASE 
        WHEN EXISTS (
            SELECT 1 
            FROM AseScans 
            WHERE ContainerNumber = @ContainerNumber 
            AND ScanImage IS NOT NULL 
            AND DATALENGTH(ScanImage) > 0
        ) THEN '✅ YES - ASE image exists'
        ELSE '❌ NO - ASE image not found or empty'
    END AS ASE_ImageStatus;

-- ✅ DETAILED CHECK: Show ASE image details
SELECT 
    ContainerNumber,
    InspectionId,
    ScanTime,
    ImageDisplayName,
    CASE 
        WHEN ScanImage IS NULL THEN '❌ NULL'
        WHEN DATALENGTH(ScanImage) = 0 THEN '❌ EMPTY'
        ELSE CONCAT('✅ ', FORMAT(DATALENGTH(ScanImage) / 1024.0, 'N2'), ' KB')
    END AS ImageStatus,
    DATALENGTH(ScanImage) AS ImageSizeBytes
FROM AseScans
WHERE ContainerNumber = @ContainerNumber
ORDER BY ScanTime DESC;

-- ✅ CHECK FS6000 images (if applicable)
SELECT 
    fs.ContainerNumber,
    fs.ScanDateTime,
    COUNT(fi.Id) AS ImageCount,
    STRING_AGG(fi.ImageType, ', ') AS ImageTypes,
    CASE 
        WHEN COUNT(fi.Id) = 0 THEN '❌ NO IMAGES'
        ELSE CONCAT('✅ ', COUNT(fi.Id), ' image(s) found')
    END AS ImageStatus
FROM FS6000Scans fs
LEFT JOIN FS6000Images fi ON fi.ScanId = fs.Id
WHERE fs.ContainerNumber = @ContainerNumber
GROUP BY fs.ContainerNumber, fs.ScanDateTime
ORDER BY fs.ScanDateTime DESC;

