-- ========================================
-- Find Practical Example: Consolidated Cargo with Multiple BOE Records
-- ========================================
-- This query finds containers that have multiple BOE documents
-- and shows how ContainerCompletenessStatus handles them

USE NS_CIS;
GO

-- Step 1: Find containers with multiple BOE records (consolidated cargo)
PRINT '========================================';
PRINT 'STEP 1: Find Consolidated Containers with Multiple BOE Records';
PRINT '========================================';
GO

SELECT 
    b.ContainerNumber,
    COUNT(DISTINCT b.Id) AS BOE_Count,
    COUNT(DISTINCT b.DeclarationNumber) AS Declaration_Count,
    COUNT(DISTINCT b.HouseBl) AS HouseBL_Count,
    MIN(b.CreatedAt) AS First_BOE_Downloaded,
    MAX(b.CreatedAt) AS Last_BOE_Downloaded,
    STRING_AGG(DISTINCT b.DeclarationNumber, ', ') AS All_Declarations,
    STRING_AGG(DISTINCT b.HouseBl, ', ') AS All_HouseBLs
FROM ICUMS_Downloads.dbo.BOEDocuments b
WHERE b.IsConsolidated = 1
    AND b.ContainerNumber IS NOT NULL
    AND b.ContainerNumber != ''
GROUP BY b.ContainerNumber
HAVING COUNT(DISTINCT b.Id) > 1  -- Multiple BOE records
ORDER BY BOE_Count DESC, Last_BOE_Downloaded DESC
OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY;
GO

-- Step 2: Pick a specific container and show detailed BOE records
PRINT '';
PRINT '========================================';
PRINT 'STEP 2: Detailed BOE Records for Example Container';
PRINT '========================================';
PRINT 'Replace @ContainerNumber with a container from Step 1';
GO

DECLARE @ContainerNumber NVARCHAR(50) = 'MSDU1234567'; -- Replace with actual container from Step 1

-- Show all BOE records for this container
SELECT 
    b.Id AS BOE_Id,
    b.ContainerNumber,
    b.DeclarationNumber,
    b.HouseBl AS HouseBL,
    b.BlNumber AS MasterBL,
    b.IsConsolidated,
    b.ConsigneeName,
    b.CrmsLevel,
    b.ClearanceType,
    b.CreatedAt AS BOE_CreatedAt,
    b.UpdatedAt AS BOE_UpdatedAt
FROM ICUMS_Downloads.dbo.BOEDocuments b
WHERE b.ContainerNumber = @ContainerNumber
ORDER BY b.CreatedAt ASC;  -- Show in chronological order
GO

-- Step 3: Show ContainerCompletenessStatus for this container
PRINT '';
PRINT '========================================';
PRINT 'STEP 3: ContainerCompletenessStatus Record';
PRINT '========================================';
GO

DECLARE @ContainerNumber NVARCHAR(50) = 'MSDU1234567'; -- Replace with actual container from Step 1

SELECT 
    c.Id,
    c.ContainerNumber,
    c.ScannerType,
    c.GroupIdentifier,
    c.IsConsolidated,
    c.TotalHouseBLs,
    c.CompleteHouseBLs,
    c.BOEDocumentId AS Primary_BOE_Id,
    c.HasICUMSData,
    c.HasImageData,
    c.HasScannerData,
    c.Status,
    c.WorkflowStage,
    c.CreatedAt AS Status_CreatedAt,
    c.UpdatedAt AS Status_UpdatedAt,
    c.LastCheckedAt,
    -- Show ConsolidationDetails (first 500 chars)
    LEFT(c.ConsolidationDetails, 500) AS ConsolidationDetails_Preview
FROM ContainerCompletenessStatuses c
WHERE c.ContainerNumber = @ContainerNumber
ORDER BY c.ScannerType;
GO

-- Step 4: Verify the primary BOE matches the most recent BOE
PRINT '';
PRINT '========================================';
PRINT 'STEP 4: Verify Primary BOE is Most Recent';
PRINT '========================================';
GO

DECLARE @ContainerNumber NVARCHAR(50) = 'MSDU1234567'; -- Replace with actual container from Step 1

-- Get the primary BOE from ContainerCompletenessStatus
SELECT 
    'ContainerCompletenessStatus Primary BOE' AS Source,
    c.BOEDocumentId AS BOE_Id,
    b.DeclarationNumber,
    b.HouseBl AS HouseBL,
    b.CreatedAt AS BOE_CreatedAt
FROM ContainerCompletenessStatuses c
INNER JOIN ICUMS_Downloads.dbo.BOEDocuments b ON c.BOEDocumentId = b.Id
WHERE c.ContainerNumber = @ContainerNumber
    AND c.ScannerType = (SELECT TOP 1 ScannerType FROM ContainerCompletenessStatuses WHERE ContainerNumber = @ContainerNumber ORDER BY CreatedAt DESC)

UNION ALL

-- Get the most recent BOE for this container
SELECT 
    'Most Recent BOE (by CreatedAt)' AS Source,
    b.Id AS BOE_Id,
    b.DeclarationNumber,
    b.HouseBl AS HouseBL,
    b.CreatedAt AS BOE_CreatedAt
FROM ICUMS_Downloads.dbo.BOEDocuments b
WHERE b.ContainerNumber = @ContainerNumber
    AND b.CreatedAt = (
        SELECT MAX(CreatedAt) 
        FROM ICUMS_Downloads.dbo.BOEDocuments 
        WHERE ContainerNumber = @ContainerNumber
    );
GO

-- Step 5: Show all House BLs in ConsolidationDetails
PRINT '';
PRINT '========================================';
PRINT 'STEP 5: Parse ConsolidationDetails JSON';
PRINT '========================================';
PRINT 'This shows all House BLs stored in ConsolidationDetails';
GO

DECLARE @ContainerNumber NVARCHAR(50) = 'MSDU1234567'; -- Replace with actual container from Step 1

-- Try to parse ConsolidationDetails as JSON (SQL Server 2016+)
SELECT 
    c.ContainerNumber,
    c.GroupIdentifier,
    c.ConsolidationDetails,
    -- Extract House BLs from JSON if possible
    CASE 
        WHEN c.ConsolidationDetails LIKE '%HouseBL%' THEN 'Contains House BL data'
        ELSE 'No House BL data in JSON format'
    END AS Has_HouseBL_Data
FROM ContainerCompletenessStatuses c
WHERE c.ContainerNumber = @ContainerNumber
    AND c.IsConsolidated = 1;
GO

-- Step 6: Check Image Analysis Grouping
PRINT '';
PRINT '========================================';
PRINT 'STEP 6: Image Analysis Grouping';
PRINT '========================================';
PRINT 'Shows how this container is grouped for image analysis';
GO

DECLARE @ContainerNumber NVARCHAR(50) = 'MSDU1234567'; -- Replace with actual container from Step 1

SELECT 
    c.ContainerNumber,
    c.ScannerType,
    c.GroupIdentifier,
    c.IsConsolidated,
    c.Status,
    c.WorkflowStage,
    -- Count other containers in the same group
    (SELECT COUNT(*) 
     FROM ContainerCompletenessStatuses c2 
     WHERE c2.GroupIdentifier = c.GroupIdentifier 
       AND c2.ScannerType = c.ScannerType
       AND c2.Status = 'Complete') AS Containers_In_Same_Group
FROM ContainerCompletenessStatuses c
WHERE c.ContainerNumber = @ContainerNumber;
GO

PRINT '';
PRINT '========================================';
PRINT 'INVESTIGATION COMPLETE';
PRINT '========================================';
PRINT '';
PRINT 'Key Points to Verify:';
PRINT '1. Container has multiple BOE records (Step 1)';
PRINT '2. All BOE records are for the same container (Step 2)';
PRINT '3. ContainerCompletenessStatus has ONE record (Step 3)';
PRINT '4. Primary BOE matches most recent BOE (Step 4)';
PRINT '5. ConsolidationDetails contains all House BLs (Step 5)';
PRINT '6. GroupIdentifier = ContainerNumber (for consolidated) (Step 6)';
PRINT '';
GO

