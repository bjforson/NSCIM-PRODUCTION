-- ============================================
-- TDE Encryption Status Check Script
-- ============================================
-- Use this script to check the encryption status of all databases
-- ============================================

USE master;
GO

PRINT '========================================';
PRINT 'TDE Encryption Status Report';
PRINT 'Generated: ' + CONVERT(VARCHAR(30), GETDATE(), 120);
PRINT '========================================';
PRINT '';

-- Check master key status
PRINT 'Master Key Status:';
IF EXISTS (SELECT * FROM sys.symmetric_keys WHERE name = '##MS_DatabaseMasterKey##')
BEGIN
    PRINT '  ✅ Master key exists';
END
ELSE
BEGIN
    PRINT '  ❌ Master key does not exist';
END
GO

-- Check certificate status
PRINT '';
PRINT 'TDE Certificate Status:';
SELECT 
    name AS CertificateName,
    subject AS Subject,
    start_date AS StartDate,
    expiry_date AS ExpiryDate,
    CASE 
        WHEN expiry_date < GETDATE() THEN '❌ EXPIRED'
        WHEN expiry_date < DATEADD(MONTH, 3, GETDATE()) THEN '⚠️ EXPIRING SOON'
        ELSE '✅ Valid'
    END AS Status
FROM sys.certificates
WHERE name = 'TDE_Certificate';
GO

-- Check database encryption status
PRINT '';
PRINT 'Database Encryption Status:';
SELECT 
    DB_NAME(database_id) AS DatabaseName,
    CASE encryption_state
        WHEN 0 THEN 'No encryption'
        WHEN 1 THEN 'Unencrypted'
        WHEN 2 THEN 'Encryption in progress'
        WHEN 3 THEN 'Encrypted ✅'
        WHEN 4 THEN 'Key change in progress'
        WHEN 5 THEN 'Decryption in progress'
        WHEN 6 THEN 'Protection change in progress'
    END AS EncryptionStatus,
    percent_complete AS PercentComplete,
    CASE 
        WHEN percent_complete > 0 THEN 'In Progress'
        WHEN encryption_state = 3 THEN 'Complete'
        ELSE 'N/A'
    END AS ProgressStatus,
    key_algorithm AS Algorithm,
    key_length AS KeyLength,
    encryptor_type AS EncryptorType,
    create_date AS CreatedDate
FROM sys.dm_database_encryption_keys
WHERE database_id IN (DB_ID('NS_CIS'), DB_ID('ICUMS'), DB_ID('ICUMS_Downloads'))
ORDER BY DatabaseName;
GO

-- Check for any databases that should be encrypted but aren't
PRINT '';
PRINT 'Databases Requiring Encryption:';
SELECT 
    name AS DatabaseName,
    CASE 
        WHEN name IN ('NS_CIS', 'ICUMS', 'ICUMS_Downloads') 
             AND name NOT IN (
                 SELECT DB_NAME(database_id) 
                 FROM sys.dm_database_encryption_keys 
                 WHERE encryption_state = 3
             )
        THEN '⚠️ Should be encrypted'
        ELSE 'N/A'
    END AS Status
FROM sys.databases
WHERE name IN ('NS_CIS', 'ICUMS', 'ICUMS_Downloads')
  AND name NOT IN (
      SELECT DB_NAME(database_id) 
      FROM sys.dm_database_encryption_keys 
      WHERE encryption_state = 3
  );
GO

PRINT '';
PRINT '========================================';
PRINT 'End of Report';
PRINT '========================================';
GO

