-- Mark existing schema as applied for ApplicationDbContext
-- Execute against the NS_CIS database (connection: NS_CIS_Connection)
-- Safe baseline: inserts only; no schema changes.

IF OBJECT_ID(N'__EFMigrationsHistory', N'U') IS NULL
BEGIN
    RAISERROR('Table __EFMigrationsHistory not found in this database. Ensure you are connected to the correct DB.', 16, 1);
    RETURN;
END

-- Helper: insert if not exists
IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = N'20250916115430_AddAseTables')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES (N'20250916115430_AddAseTables', N'9.0.9');

IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = N'20250916173810_AddMissingTables')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES (N'20250916173810_AddMissingTables', N'9.0.9');

IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = N'20250916175227_InitialCreate')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES (N'20250916175227_InitialCreate', N'9.0.9');

IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = N'20251003105725_AddContainerDataCompletenessTables')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES (N'20251003105725_AddContainerDataCompletenessTables', N'9.0.9');

IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = N'20251008182401_AddFS6000ImageCompletenessFields')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES (N'20251008182401_AddFS6000ImageCompletenessFields', N'9.0.9');

IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = N'20251019084637_AddICUMSContainerDataTables')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES (N'20251019084637_AddICUMSContainerDataTables', N'9.0.9');

IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = N'20251023171136_AddPreComputedCompletenessFields')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES (N'20251023171136_AddPreComputedCompletenessFields', N'9.0.9');

-- Already-applied (per list) are not re-inserted
-- 20251018115544_AddICUMSDownloadQueueTable
-- 20251018204900_AddBLReviewTables
-- 20251019130032_AddSystemSettingsTables
-- 20251026002706_AddDepartmentAndPhoneToUser

PRINT '✅ Baseline markers inserted (if missing). You can now run EF migrations normally.';


