-- ============================================================
-- Container Pairs Analysis Query
-- Analyzes how ASE scanner multi-container pairs are processed
-- ============================================================

-- PART 1: BOE Documents for Each Container
-- Shows how many BOE documents exist for each container and their declarations
SELECT 
    'BOE Documents' AS AnalysisType,
    b.ContainerNumber,
    b.DeclarationNumber,
    b.ConsigneeName,
    b.ClearanceType,
    b.CrmsLevel,
    b.BlNumber AS MasterBL,
    b.RotationNumber,
    b.ProcessingStatus,
    b.CreatedAt,
    b.DownloadedFileId,
    df.FileName AS SourceFile
FROM ICUMS_Downloads.dbo.BOEDocuments b
LEFT JOIN ICUMS_Downloads.dbo.DownloadedFiles df ON b.DownloadedFileId = df.Id
WHERE b.ContainerNumber IN (
    'TEMU0245328', 'TRHU2315120',
    'APZU3253676', 'CMAU2290095',
    'GLDU9481593', 'CAIU3114992',
    'NYKU3592875', 'NYKU3683358'
)
ORDER BY b.ContainerNumber, b.DeclarationNumber;

-- PART 2: Summary Count by Container
-- Shows total BOE documents per container
SELECT 
    'Container Summary' AS AnalysisType,
    ContainerNumber,
    COUNT(*) AS TotalBOEDocuments,
    COUNT(DISTINCT DeclarationNumber) AS UniqueDeclarations,
    STRING_AGG(DISTINCT DeclarationNumber, ', ') AS AllDeclarations,
    STRING_AGG(DISTINCT ConsigneeName, ' | ') AS AllConsignees,
    STRING_AGG(DISTINCT ClearanceType, ', ') AS AllClearanceTypes
FROM ICUMS_Downloads.dbo.BOEDocuments
WHERE ContainerNumber IN (
    'TEMU0245328', 'TRHU2315120',
    'APZU3253676', 'CMAU2290095',
    'GLDU9481593', 'CAIU3114992',
    'NYKU3592875', 'NYKU3683358'
)
GROUP BY ContainerNumber
ORDER BY ContainerNumber;

-- PART 3: Cross-Record Scan Detection
-- Shows if any CrossRecordScan entries exist for these container pairs
SELECT 
    'Cross-Record Scans' AS AnalysisType,
    crs.Id,
    crs.OriginalScanRecord,
    crs.ScannerType,
    crs.ScanDateTime,
    crs.Container1,
    crs.Container1_BOE,
    crs.Container1_Consignee,
    crs.Container1_CRMS,
    crs.Container1_ClearanceType,
    crs.Container2,
    crs.Container2_BOE,
    crs.Container2_Consignee,
    crs.Container2_CRMS,
    crs.Container2_ClearanceType,
    crs.CrossRecordType,
    crs.Severity,
    crs.SameDeclaration,
    crs.SameConsignee,
    crs.SameMasterBL,
    crs.SameCRMS,
    crs.SameClearanceType,
    crs.ReviewStatus,
    crs.CreatedAt
FROM CrossRecordScans crs
WHERE (crs.Container1 IN (
    'TEMU0245328', 'TRHU2315120',
    'APZU3253676', 'CMAU2290095',
    'GLDU9481593', 'CAIU3114992',
    'NYKU3592875', 'NYKU3683358'
) OR crs.Container2 IN (
    'TEMU0245328', 'TRHU2315120',
    'APZU3253676', 'CMAU2290095',
    'GLDU9481593', 'CAIU3114992',
    'NYKU3592875', 'NYKU3683358'
))
ORDER BY crs.ScanDateTime DESC;

-- PART 4: Container Pair Analysis
-- Shows which containers belong to which ASE scan pair and their relationship
WITH ContainerPairs AS (
    SELECT 'TEMU0245328' AS Container1, 'TRHU2315120' AS Container2, 'Pair 1' AS PairName
    UNION ALL SELECT 'APZU3253676', 'CMAU2290095', 'Pair 2'
    UNION ALL SELECT 'GLDU9481593', 'CAIU3114992', 'Pair 3'
    UNION ALL SELECT 'NYKU3592875', 'NYKU3683358', 'Pair 4'
),
BOE1 AS (
    SELECT 
        ContainerNumber,
        DeclarationNumber,
        ConsigneeName,
        ClearanceType,
        CrmsLevel,
        BlNumber,
        RotationNumber
    FROM ICUMS_Downloads.dbo.BOEDocuments
    WHERE ContainerNumber IN (SELECT Container1 FROM ContainerPairs)
),
BOE2 AS (
    SELECT 
        ContainerNumber,
        DeclarationNumber,
        ConsigneeName,
        ClearanceType,
        CrmsLevel,
        BlNumber,
        RotationNumber
    FROM ICUMS_Downloads.dbo.BOEDocuments
    WHERE ContainerNumber IN (SELECT Container2 FROM ContainerPairs)
)
SELECT 
    'Pair Analysis' AS AnalysisType,
    cp.PairName,
    cp.Container1,
    b1.DeclarationNumber AS Container1_BOE,
    b1.ConsigneeName AS Container1_Consignee,
    b1.ClearanceType AS Container1_ClearanceType,
    b1.CrmsLevel AS Container1_CRMS,
    cp.Container2,
    b2.DeclarationNumber AS Container2_BOE,
    b2.ConsigneeName AS Container2_Consignee,
    b2.ClearanceType AS Container2_ClearanceType,
    b2.CrmsLevel AS Container2_CRMS,
    CASE 
        WHEN b1.DeclarationNumber IS NULL OR b2.DeclarationNumber IS NULL THEN 'Pending BOE Data'
        WHEN b1.DeclarationNumber = b2.DeclarationNumber THEN 'Same Declaration (Same Record)'
        WHEN b1.BlNumber = b2.BlNumber AND b1.BlNumber IS NOT NULL THEN 'Same Master BL (Consolidated)'
        WHEN b1.ConsigneeName != b2.ConsigneeName THEN 'Different Importers (CROSS-RECORD)'
        WHEN b1.ClearanceType != b2.ClearanceType THEN 'Different Clearance Types (CROSS-RECORD)'
        WHEN b1.CrmsLevel != b2.CrmsLevel THEN 'Different CRMS Levels (CROSS-RECORD)'
        ELSE 'Different BOEs (CROSS-RECORD)'
    END AS RelationshipStatus,
    CASE 
        WHEN b1.DeclarationNumber IS NULL OR b2.DeclarationNumber IS NULL THEN 'Pending'
        WHEN b1.DeclarationNumber = b2.DeclarationNumber OR (b1.BlNumber = b2.BlNumber AND b1.BlNumber IS NOT NULL) THEN 'Normal'
        ELSE 'Cross-Record'
    END AS Classification
FROM ContainerPairs cp
LEFT JOIN BOE1 b1 ON cp.Container1 = b1.ContainerNumber
LEFT JOIN BOE2 b2 ON cp.Container2 = b2.ContainerNumber
ORDER BY cp.PairName;

-- PART 5: ICUMS Download Queue Status
-- Shows if any of these containers are still in the download queue
SELECT 
    'Download Queue' AS AnalysisType,
    q.ContainerNumber,
    q.Status,
    q.Priority,
    q.RetryCount,
    q.CreatedAt AS QueuedAt,
    q.UpdatedAt AS LastUpdated
FROM ICUMS_Downloads.dbo.ICUMSDownloadQueue q
WHERE q.ContainerNumber IN (
    'TEMU0245328', 'TRHU2315120',
    'APZU3253676', 'CMAU2290095',
    'GLDU9481593', 'CAIU3114992',
    'NYKU3592875', 'NYKU3683358'
)
ORDER BY q.ContainerNumber, q.CreatedAt DESC;

-- PART 6: Downloaded Files for These Containers
-- Shows which JSON files contain data for these containers
SELECT 
    'Downloaded Files' AS AnalysisType,
    df.Id,
    df.FileName,
    df.FilePath,
    df.DownloadDate,
    df.ProcessingStatus,
    df.RecordCount,
    df.FileSize,
    COUNT(b.Id) AS BOEDocumentsInFile
FROM ICUMS_Downloads.dbo.DownloadedFiles df
INNER JOIN ICUMS_Downloads.dbo.BOEDocuments b ON df.Id = b.DownloadedFileId
WHERE b.ContainerNumber IN (
    'TEMU0245328', 'TRHU2315120',
    'APZU3253676', 'CMAU2290095',
    'GLDU9481593', 'CAIU3114992',
    'NYKU3592875', 'NYKU3683358'
)
GROUP BY df.Id, df.FileName, df.FilePath, df.DownloadDate, df.ProcessingStatus, df.RecordCount, df.FileSize
ORDER BY df.DownloadDate DESC;

