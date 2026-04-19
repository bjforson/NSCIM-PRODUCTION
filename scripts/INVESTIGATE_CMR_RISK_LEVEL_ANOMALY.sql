-- =====================================================
-- CMR RISK LEVEL ANOMALY INVESTIGATION
-- =====================================================
-- Purpose: Find CMR clearance type records that have risk levels
-- (CMR records should NOT have risk levels as risk levels are only
-- available when BOE is ready)
-- =====================================================

USE ICUMS_Downloads;
GO

-- =====================================================
-- SCENARIO 1: CMR records with non-null CrmsLevel
-- =====================================================
PRINT '========================================';
PRINT 'SCENARIO 1: CMR Records with Risk Levels';
PRINT '========================================';
GO

SELECT 
    COUNT(*) AS TotalCMRWithRiskLevel,
    COUNT(DISTINCT ContainerNumber) AS UniqueContainers
FROM BOEDocuments
WHERE ClearanceType = 'CMR'
  AND CrmsLevel IS NOT NULL
  AND CrmsLevel != '';
GO

-- Detailed breakdown by risk level
SELECT 
    CrmsLevel,
    COUNT(*) AS RecordCount,
    COUNT(DISTINCT ContainerNumber) AS UniqueContainers,
    COUNT(DISTINCT DeclarationNumber) AS UniqueDeclarations
FROM BOEDocuments
WHERE ClearanceType = 'CMR'
  AND CrmsLevel IS NOT NULL
  AND CrmsLevel != ''
GROUP BY CrmsLevel
ORDER BY RecordCount DESC;
GO

-- Sample records with full details
SELECT TOP 20
    Id,
    ContainerNumber,
    ClearanceType,
    CrmsLevel,
    DeclarationNumber,
    RotationNumber,
    RegimeCode,
    DownloadDate,
    DownloadedFileId,
    DocumentIndex
FROM BOEDocuments
WHERE ClearanceType = 'CMR'
  AND CrmsLevel IS NOT NULL
  AND CrmsLevel != ''
ORDER BY DownloadDate DESC;
GO

-- =====================================================
-- SCENARIO 2: Records that might have been updated
-- Check if CMR records have DeclarationNumbers (they shouldn't)
-- =====================================================
PRINT '';
PRINT '========================================';
PRINT 'SCENARIO 2: CMR Records with Declaration Numbers';
PRINT '(CMR should NOT have DeclarationNumber - indicates possible update issue)';
PRINT '========================================';
GO

SELECT 
    COUNT(*) AS CMRWithDeclarationNumber,
    COUNT(DISTINCT ContainerNumber) AS UniqueContainers
FROM BOEDocuments
WHERE ClearanceType = 'CMR'
  AND DeclarationNumber IS NOT NULL
  AND DeclarationNumber != '';
GO

-- Detailed breakdown
SELECT TOP 20
    Id,
    ContainerNumber,
    ClearanceType,
    DeclarationNumber,
    CrmsLevel,
    RotationNumber,
    DownloadDate,
    DownloadedFileId
FROM BOEDocuments
WHERE ClearanceType = 'CMR'
  AND DeclarationNumber IS NOT NULL
  AND DeclarationNumber != ''
ORDER BY DownloadDate DESC;
GO

-- =====================================================
-- SCENARIO 3: Check ContainerCompletenessStatus
-- =====================================================
PRINT '';
PRINT '========================================';
PRINT 'SCENARIO 3: ContainerCompletenessStatus Records';
PRINT '========================================';
GO

USE NickScanCentralImagingPortal;
GO

-- Check if ContainerCompletenessStatus has CMR with risk levels
-- (RiskLevel might be stored in consolidation details JSON)
SELECT 
    c.Id,
    c.ContainerNumber,
    c.ClearanceType,
    c.BOEDocumentId,
    c.Status,
    c.UpdatedAt,
    b.ClearanceType AS BOE_ClearanceType,
    b.CrmsLevel AS BOE_RiskLevel,
    b.DeclarationNumber AS BOE_DeclarationNumber
FROM ContainerCompletenessStatuses c
LEFT JOIN ICUMS_Downloads.dbo.BOEDocuments b ON c.BOEDocumentId = b.Id
WHERE c.ClearanceType = 'CMR'
  AND b.CrmsLevel IS NOT NULL
  AND b.CrmsLevel != ''
ORDER BY c.UpdatedAt DESC;
GO

-- =====================================================
-- SCENARIO 4: Timeline Analysis
-- Check if records are being updated from CMR to IM/EX
-- =====================================================
PRINT '';
PRINT '========================================';
PRINT 'SCENARIO 4: Timeline Analysis - Same Container with Different Clearance Types';
PRINT '========================================';
GO

USE ICUMS_Downloads;
GO

-- Find containers that appear as both CMR and IM/EX
WITH ContainerClearanceHistory AS (
    SELECT 
        ContainerNumber,
        ClearanceType,
        CrmsLevel,
        DeclarationNumber,
        DownloadDate,
        DownloadedFileId,
        ROW_NUMBER() OVER (PARTITION BY ContainerNumber ORDER BY DownloadDate DESC) AS RowNum
    FROM BOEDocuments
    WHERE ContainerNumber IS NOT NULL
)
SELECT 
    c1.ContainerNumber,
    c1.ClearanceType AS CurrentClearanceType,
    c1.CrmsLevel AS CurrentRiskLevel,
    c1.DeclarationNumber AS CurrentDeclarationNumber,
    c1.DownloadDate AS CurrentDownloadDate,
    (SELECT TOP 1 ClearanceType 
     FROM BOEDocuments 
     WHERE ContainerNumber = c1.ContainerNumber 
       AND ClearanceType != c1.ClearanceType 
       AND ClearanceType IS NOT NULL
     ORDER BY DownloadDate DESC) AS PreviousClearanceType,
    (SELECT TOP 1 DownloadDate 
     FROM BOEDocuments 
     WHERE ContainerNumber = c1.ContainerNumber 
       AND ClearanceType != c1.ClearanceType 
       AND ClearanceType IS NOT NULL
     ORDER BY DownloadDate DESC) AS PreviousDownloadDate
FROM ContainerClearanceHistory c1
WHERE c1.RowNum = 1
  AND EXISTS (
      SELECT 1 
      FROM BOEDocuments c2 
      WHERE c2.ContainerNumber = c1.ContainerNumber 
        AND c2.ClearanceType != c1.ClearanceType 
        AND c2.ClearanceType IS NOT NULL
  )
  AND c1.ClearanceType = 'CMR'
  AND c1.CrmsLevel IS NOT NULL
  AND c1.CrmsLevel != '';
GO

-- =====================================================
-- SCENARIO 5: Source Data Analysis
-- Check the raw JSON data to see if source has CrmsLevel for CMR
-- =====================================================
PRINT '';
PRINT '========================================';
PRINT 'SCENARIO 5: Sample Raw JSON Analysis';
PRINT '========================================';
GO

-- Get sample raw JSON for CMR records with risk levels
SELECT TOP 5
    b.Id,
    b.ContainerNumber,
    b.ClearanceType,
    b.CrmsLevel,
    LEFT(b.RawJsonData, 500) AS JsonSample
FROM BOEDocuments b
WHERE b.ClearanceType = 'CMR'
  AND b.CrmsLevel IS NOT NULL
  AND b.CrmsLevel != ''
  AND b.RawJsonData IS NOT NULL
ORDER BY b.DownloadDate DESC;
GO

-- =====================================================
-- SUMMARY STATISTICS
-- =====================================================
PRINT '';
PRINT '========================================';
PRINT 'SUMMARY STATISTICS';
PRINT '========================================';
GO

SELECT 
    'Total CMR Records' AS Metric,
    COUNT(*) AS Count
FROM BOEDocuments
WHERE ClearanceType = 'CMR'

UNION ALL

SELECT 
    'CMR with Risk Level',
    COUNT(*)
FROM BOEDocuments
WHERE ClearanceType = 'CMR'
  AND CrmsLevel IS NOT NULL
  AND CrmsLevel != ''

UNION ALL

SELECT 
    'CMR with Declaration Number',
    COUNT(*)
FROM BOEDocuments
WHERE ClearanceType = 'CMR'
  AND DeclarationNumber IS NOT NULL
  AND DeclarationNumber != ''

UNION ALL

SELECT 
    'Total IM/EX Records',
    COUNT(*)
FROM BOEDocuments
WHERE ClearanceType IN ('IM', 'EX')

UNION ALL

SELECT 
    'IM/EX with Risk Level',
    COUNT(*)
FROM BOEDocuments
WHERE ClearanceType IN ('IM', 'EX')
  AND CrmsLevel IS NOT NULL
  AND CrmsLevel != '';
GO

