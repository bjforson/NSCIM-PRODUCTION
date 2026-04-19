-- ============================================
-- Transparent Data Encryption (TDE) Setup Script
-- Phase 3: Database Encryption Implementation
-- ============================================
-- This script implements TDE for NCIP databases
-- Prerequisites: SQL Server 2016+ (Standard or Enterprise)
-- ============================================

USE master;
GO

-- ============================================
-- Step 1: Create Master Key for TDE
-- ============================================
-- The master key is used to protect the database encryption keys
-- This should be backed up immediately after creation

IF NOT EXISTS (SELECT * FROM sys.symmetric_keys WHERE name = '##MS_DatabaseMasterKey##')
BEGIN
    PRINT 'Creating master key for TDE...';
    CREATE MASTER KEY ENCRYPTION BY PASSWORD = '***USE_STRONG_PASSWORD_FROM_ENV_VAR***';
    PRINT '✅ Master key created successfully';
END
ELSE
BEGIN
    PRINT 'ℹ️ Master key already exists';
END
GO

-- ============================================
-- Step 2: Create Certificate for TDE
-- ============================================
-- The certificate is used to encrypt the database encryption keys
-- Certificate should be backed up and stored securely

IF NOT EXISTS (SELECT * FROM sys.certificates WHERE name = 'TDE_Certificate')
BEGIN
    PRINT 'Creating TDE certificate...';
    CREATE CERTIFICATE TDE_Certificate
    WITH SUBJECT = 'TDE Certificate for NCIP Databases',
         EXPIRY_DATE = '2026-11-03';
    PRINT '✅ TDE certificate created successfully';
    PRINT '⚠️ IMPORTANT: Back up certificate immediately!';
    PRINT '   Command: BACKUP CERTIFICATE TDE_Certificate TO FILE = ''C:\Certificates\TDE_Certificate.cer'' WITH PRIVATE KEY (FILE = ''C:\Certificates\TDE_Certificate_PrivateKey.pvk'', ENCRYPTION BY PASSWORD = ''***USE_STRONG_PASSWORD***'');';
END
ELSE
BEGIN
    PRINT 'ℹ️ TDE certificate already exists';
END
GO

-- ============================================
-- Step 3: Enable TDE on NS_CIS Database
-- ============================================
USE NS_CIS;
GO

-- Create database encryption key
IF NOT EXISTS (SELECT * FROM sys.dm_database_encryption_keys WHERE database_id = DB_ID('NS_CIS'))
BEGIN
    PRINT 'Creating database encryption key for NS_CIS...';
    CREATE DATABASE ENCRYPTION KEY
    WITH ALGORITHM = AES_256
    ENCRYPTION BY SERVER CERTIFICATE TDE_Certificate;
    PRINT '✅ Database encryption key created for NS_CIS';
END
ELSE
BEGIN
    PRINT 'ℹ️ Database encryption key already exists for NS_CIS';
END
GO

-- Enable TDE
ALTER DATABASE NS_CIS SET ENCRYPTION ON;
GO

-- Check encryption status
SELECT 
    DB_NAME(database_id) AS DatabaseName,
    encryption_state,
    CASE encryption_state
        WHEN 0 THEN 'No encryption'
        WHEN 1 THEN 'Unencrypted'
        WHEN 2 THEN 'Encryption in progress'
        WHEN 3 THEN 'Encrypted'
        WHEN 4 THEN 'Key change in progress'
        WHEN 5 THEN 'Decryption in progress'
        WHEN 6 THEN 'Protection change in progress'
    END AS EncryptionStateDescription,
    percent_complete,
    key_algorithm,
    key_length,
    encryptor_type
FROM sys.dm_database_encryption_keys
WHERE database_id = DB_ID('NS_CIS');
GO

PRINT '✅ TDE enabled for NS_CIS database';
PRINT '⚠️ Note: Encryption may take time depending on database size';
GO

-- ============================================
-- Step 4: Enable TDE on ICUMS Database
-- ============================================
USE ICUMS;
GO

-- Create database encryption key
IF NOT EXISTS (SELECT * FROM sys.dm_database_encryption_keys WHERE database_id = DB_ID('ICUMS'))
BEGIN
    PRINT 'Creating database encryption key for ICUMS...';
    CREATE DATABASE ENCRYPTION KEY
    WITH ALGORITHM = AES_256
    ENCRYPTION BY SERVER CERTIFICATE TDE_Certificate;
    PRINT '✅ Database encryption key created for ICUMS';
END
ELSE
BEGIN
    PRINT 'ℹ️ Database encryption key already exists for ICUMS';
END
GO

-- Enable TDE
ALTER DATABASE ICUMS SET ENCRYPTION ON;
GO

-- Check encryption status
SELECT 
    DB_NAME(database_id) AS DatabaseName,
    encryption_state,
    CASE encryption_state
        WHEN 0 THEN 'No encryption'
        WHEN 1 THEN 'Unencrypted'
        WHEN 2 THEN 'Encryption in progress'
        WHEN 3 THEN 'Encrypted'
        WHEN 4 THEN 'Key change in progress'
        WHEN 5 THEN 'Decryption in progress'
        WHEN 6 THEN 'Protection change in progress'
    END AS EncryptionStateDescription,
    percent_complete,
    key_algorithm,
    key_length,
    encryptor_type
FROM sys.dm_database_encryption_keys
WHERE database_id = DB_ID('ICUMS');
GO

PRINT '✅ TDE enabled for ICUMS database';
GO

-- ============================================
-- Step 5: Enable TDE on ICUMS_Downloads Database
-- ============================================
USE ICUMS_Downloads;
GO

-- Create database encryption key
IF NOT EXISTS (SELECT * FROM sys.dm_database_encryption_keys WHERE database_id = DB_ID('ICUMS_Downloads'))
BEGIN
    PRINT 'Creating database encryption key for ICUMS_Downloads...';
    CREATE DATABASE ENCRYPTION KEY
    WITH ALGORITHM = AES_256
    ENCRYPTION BY SERVER CERTIFICATE TDE_Certificate;
    PRINT '✅ Database encryption key created for ICUMS_Downloads';
END
ELSE
BEGIN
    PRINT 'ℹ️ Database encryption key already exists for ICUMS_Downloads';
END
GO

-- Enable TDE
ALTER DATABASE ICUMS_Downloads SET ENCRYPTION ON;
GO

-- Check encryption status
SELECT 
    DB_NAME(database_id) AS DatabaseName,
    encryption_state,
    CASE encryption_state
        WHEN 0 THEN 'No encryption'
        WHEN 1 THEN 'Unencrypted'
        WHEN 2 THEN 'Encryption in progress'
        WHEN 3 THEN 'Encrypted'
        WHEN 4 THEN 'Key change in progress'
        WHEN 5 THEN 'Decryption in progress'
        WHEN 6 THEN 'Protection change in progress'
    END AS EncryptionStateDescription,
    percent_complete,
    key_algorithm,
    key_length,
    encryptor_type
FROM sys.dm_database_encryption_keys
WHERE database_id = DB_ID('ICUMS_Downloads');
GO

PRINT '✅ TDE enabled for ICUMS_Downloads database';
GO

-- ============================================
-- Step 6: Verify Encryption Status for All Databases
-- ============================================
USE master;
GO

PRINT '========================================';
PRINT 'TDE Encryption Status Summary';
PRINT '========================================';

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
    key_algorithm AS Algorithm,
    key_length AS KeyLength,
    encryptor_type AS EncryptorType,
    create_date AS CreatedDate,
    modify_date AS ModifiedDate
FROM sys.dm_database_encryption_keys
WHERE database_id IN (DB_ID('NS_CIS'), DB_ID('ICUMS'), DB_ID('ICUMS_Downloads'))
ORDER BY DatabaseName;
GO

PRINT '========================================';
PRINT '✅ TDE Setup Complete!';
PRINT '========================================';
PRINT '';
PRINT '⚠️ IMPORTANT NEXT STEPS:';
PRINT '1. Back up the TDE certificate and private key';
PRINT '2. Store backups in secure, off-site location';
PRINT '3. Document certificate location and passwords';
PRINT '4. Test backup and restore procedures with TDE';
PRINT '5. Monitor encryption progress (may take hours for large databases)';
PRINT '';
GO

