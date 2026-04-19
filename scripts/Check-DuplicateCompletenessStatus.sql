-- Query to find duplicate ContainerCompletenessStatus records
-- These duplicates could cause issues with queue item processing

-- Find duplicates by ContainerNumber + ScannerType + InspectionId
SELECT 
    ContainerNumber,
    ScannerType,
    InspectionId,
    COUNT(*) AS RecordCount,
    STRING_AGG(CAST(Id AS VARCHAR), ', ') AS Ids,
    MIN(CreatedAt) AS FirstCreated,
    MAX(CreatedAt) AS LastCreated,
    STRING_AGG(Status, ', ') AS Statuses
FROM ContainerCompletenessStatuses
GROUP BY ContainerNumber, ScannerType, InspectionId
HAVING COUNT(*) > 1
ORDER BY RecordCount DESC, ContainerNumber;

-- Get detailed view of duplicates for a specific container (example)
-- Replace 'TCLU7851490' with actual container number from above query
/*
SELECT 
    Id,
    ContainerNumber,
    ScannerType,
    InspectionId,
    Status,
    HasICUMSData,
    CreatedAt,
    UpdatedAt,
    LastCheckedAt
FROM ContainerCompletenessStatuses
WHERE ContainerNumber = 'TCLU7851490'
    AND ScannerType = 'ASE'
    AND InspectionId = '69983'
ORDER BY CreatedAt;
*/

