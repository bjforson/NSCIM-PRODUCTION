# Database Structure Replication: SQL Server 2022 → SQL Server 2014

## Overview
This document describes the process to replicate database structures from SQL Server 2022 to SQL Server 2014.

## Prerequisites

1. **SQL Server 2022 Instance**: `localhost\NS_CIS` (source)
2. **SQL Server 2014 Instance**: `127.0.0.1,1433` (target - currently in use)
3. **Databases to Replicate**:
   - `NS_CIS`
   - `ICUMS`
   - `ICUMS_Downloads`

## Step 1: Restore/Attach Databases to SQL Server 2022

Before running the comparison, ensure the databases are available on the 2022 instance:

```sql
-- Check if databases exist
SELECT name FROM sys.databases 
WHERE name IN ('NS_CIS', 'ICUMS', 'ICUMS_Downloads');
```

If databases don't exist, restore or attach them:

```sql
-- Example: Restore database
RESTORE DATABASE [NS_CIS] 
FROM DISK = 'C:\Backups\NS_CIS.bak'
WITH REPLACE, RECOVERY;

-- Or attach database
CREATE DATABASE [NS_CIS] ON 
(FILENAME = 'C:\Data\NS_CIS.mdf'),
(FILENAME = 'C:\Data\NS_CIS_Log.ldf')
FOR ATTACH;
```

## Step 2: Run Comparison Script

Once databases are available on 2022, run:

```powershell
cd C:\Users\Administrator\Documents\GitHub\NICKSCAN-CENTRAL--IMAGE-PORTAL
powershell -ExecutionPolicy Bypass -File "scripts\GenerateReplicationScripts.ps1"
```

This will:
1. Connect to SQL Server 2022 (source)
2. Extract table and column definitions
3. Generate SQL scripts to create missing structures on SQL Server 2014
4. Save scripts to `scripts\sql\replication\`

## Step 3: Review Generated Scripts

Review the generated SQL scripts:
- `scripts\sql\replication\Replicate_NS_CIS_Structure.sql`
- `scripts\sql\replication\Replicate_ICUMS_Structure.sql`
- `scripts\sql\replication\Replicate_ICUMS_Downloads_Structure.sql`

## Step 4: Execute Scripts on SQL Server 2014

Run the generated scripts on the target server (2014):

```powershell
# For each database
sqlcmd -S "127.0.0.1,1433" -d NS_CIS -E -i "scripts\sql\replication\Replicate_NS_CIS_Structure.sql"
sqlcmd -S "127.0.0.1,1433" -d ICUMS -E -i "scripts\sql\replication\Replicate_ICUMS_Structure.sql"
sqlcmd -S "127.0.0.1,1433" -d ICUMS_Downloads -E -i "scripts\sql\replication\Replicate_ICUMS_Downloads_Structure.sql"
```

## Step 5: Verify Replication

Run the comparison script again to verify all structures match:

```powershell
powershell -ExecutionPolicy Bypass -File "scripts\CompareAndReplicateDatabaseStructure.ps1"
```

## Manual Comparison (Alternative)

If you prefer to compare manually:

### Compare Tables
```sql
-- On SQL Server 2022
SELECT TABLE_SCHEMA, TABLE_NAME 
FROM INFORMATION_SCHEMA.TABLES 
WHERE TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_SCHEMA, TABLE_NAME;

-- On SQL Server 2014
SELECT TABLE_SCHEMA, TABLE_NAME 
FROM INFORMATION_SCHEMA.TABLES 
WHERE TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_SCHEMA, TABLE_NAME;
```

### Compare Columns for a Specific Table
```sql
-- On SQL Server 2022
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'YourTableName'
ORDER BY ORDINAL_POSITION;

-- On SQL Server 2014 (compare results)
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'YourTableName'
ORDER BY ORDINAL_POSITION;
```

## Important Notes

1. **Data Migration**: This process only replicates STRUCTURE, not DATA
2. **Indexes**: Generated scripts may not include all indexes - review and add manually if needed
3. **Foreign Keys**: Foreign key constraints may need to be added separately
4. **Stored Procedures/Views**: These are not included in the comparison - add manually
5. **SQL Server 2014 Compatibility**: Ensure all SQL Server 2022 features used are compatible with 2014

## Troubleshooting

### "Database does not exist" Error
- Ensure databases are restored/attached to SQL Server 2022 instance
- Verify connection string: `localhost\NS_CIS`

### "Connection failed" Error
- Check SQL Server services are running
- Verify Windows Authentication is enabled
- Check firewall settings

### "Syntax errors" in generated scripts
- Review and manually fix any SQL Server 2022-specific syntax
- Some features may not be available in SQL Server 2014











