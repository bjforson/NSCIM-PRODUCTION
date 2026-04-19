BEGIN TRANSACTION;

-- Only mark migrations as applied; no schema changes
IF OBJECT_ID(N'__EFMigrationsHistory', N'U') IS NULL
BEGIN
    RAISERROR('Table __EFMigrationsHistory not found. Connect to NS_CIS DB.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20251101112951_AddImageAnalysisDomain')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20251101112951_AddImageAnalysisDomain', N'9.0.9');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20251101113059_AddImageAnalysisDomain2')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20251101113059_AddImageAnalysisDomain2', N'9.0.9');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20251101113136_AddImageAnalysisDomain3')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20251101113136_AddImageAnalysisDomain3', N'9.0.9');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20251101115557_SyncModelSnapshot_20251101')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20251101115557_SyncModelSnapshot_20251101', N'9.0.9');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20251101122421_BaselineSync')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20251101122421_BaselineSync', N'9.0.9');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20251101131003_BaselineClean')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20251101131003_BaselineClean', N'9.0.9');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20251101131034_ImageAnalysis_Init')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20251101131034_ImageAnalysis_Init', N'9.0.9');

COMMIT;
GO


