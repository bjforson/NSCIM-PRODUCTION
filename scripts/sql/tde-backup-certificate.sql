-- ============================================
-- TDE Certificate Backup Script
-- ============================================
-- This script backs up the TDE certificate and private key
-- IMPORTANT: Store backups securely and off-site
-- ============================================

USE master;
GO

-- Create backup directory if it doesn't exist
-- Note: This directory must exist before running this script
-- You may need to create it manually: C:\Certificates\TDE

-- Backup the certificate
PRINT 'Backing up TDE certificate...';
BACKUP CERTIFICATE TDE_Certificate 
TO FILE = 'C:\Certificates\TDE\TDE_Certificate.cer'
WITH PRIVATE KEY (
    FILE = 'C:\Certificates\TDE\TDE_Certificate_PrivateKey.pvk',
    ENCRYPTION BY PASSWORD = '***USE_STRONG_PASSWORD_FROM_ENV_VAR***'
);
GO

PRINT '✅ TDE certificate backed up successfully';
PRINT '';
PRINT 'Certificate file: C:\Certificates\TDE\TDE_Certificate.cer';
PRINT 'Private key file: C:\Certificates\TDE\TDE_Certificate_PrivateKey.pvk';
PRINT '';
PRINT '⚠️ IMPORTANT:';
PRINT '   - Store these files in secure, encrypted location';
PRINT '   - Store off-site for disaster recovery';
PRINT '   - Document the private key password securely';
PRINT '   - Without these files, encrypted databases cannot be restored';
GO

