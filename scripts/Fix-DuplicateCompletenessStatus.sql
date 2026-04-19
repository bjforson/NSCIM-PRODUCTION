-- Script to fix duplicate ContainerCompletenessStatus records
-- Keeps the most recent record and deletes older duplicates
-- WARNING: Review the results of Check-DuplicateCompletenessStatus.sql first!

-- Step 1: Identify duplicates (run first to see what will be deleted)
WITH DuplicateRecords AS (
    SELECT 
        Id,
        ContainerNumber,
        ScannerType,
        InspectionId,
        ROW_NUMBER() OVER (
            PARTITION BY ContainerNumber, ScannerType, InspectionId 
            ORDER BY CreatedAt DESC, Id DESC
        ) AS RowNum
    FROM ContainerCompletenessStatuses
)
SELECT 
    Id,
    ContainerNumber,
    ScannerType,
    InspectionId,
    CreatedAt,
    Status
FROM DuplicateRecords
WHERE RowNum > 1
ORDER BY ContainerNumber, ScannerType, InspectionId, CreatedAt DESC;

-- Step 2: Delete duplicates (keeps the most recent record)
-- UNCOMMENT AND RUN AFTER REVIEWING STEP 1 RESULTS
/*
WITH DuplicateRecords AS (
    SELECT 
        Id,
        ROW_NUMBER() OVER (
            PARTITION BY ContainerNumber, ScannerType, InspectionId 
            ORDER BY CreatedAt DESC, Id DESC
        ) AS RowNum
    FROM ContainerCompletenessStatuses
)
DELETE FROM ContainerCompletenessStatuses
WHERE Id IN (
    SELECT Id FROM DuplicateRecords WHERE RowNum > 1
);
*/

-- Step 3: Verify no duplicates remain
SELECT 
    ContainerNumber,
    ScannerType,
    InspectionId,
    COUNT(*) AS RecordCount
FROM ContainerCompletenessStatuses
GROUP BY ContainerNumber, ScannerType, InspectionId
HAVING COUNT(*) > 1;

