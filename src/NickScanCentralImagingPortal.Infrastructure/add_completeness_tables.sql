BEGIN TRANSACTION;
DROP TABLE [ContainerReviewDecisions];

DROP TABLE [BLReviewRecords];

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20251018204900_AddBLReviewTables';

ALTER TABLE [Users] DROP CONSTRAINT [FK_Users_Roles_RoleId];

DROP TABLE [ICUMSDownloadQueues];

DROP TABLE [PermissionAuditLogs];

DROP TABLE [RolePermissions];

DROP TABLE [UserPermissions];

DROP TABLE [Roles];

DROP TABLE [Permissions];

DROP INDEX [IX_Users_RoleId] ON [Users];

DECLARE @var sysname;
SELECT @var = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Users]') AND [c].[name] = N'LegacyRole');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [Users] DROP CONSTRAINT [' + @var + '];');
ALTER TABLE [Users] DROP COLUMN [LegacyRole];

DECLARE @var1 sysname;
SELECT @var1 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Users]') AND [c].[name] = N'RoleId');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Users] DROP CONSTRAINT [' + @var1 + '];');
ALTER TABLE [Users] DROP COLUMN [RoleId];

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20251018115544_AddICUMSDownloadQueueTable';

DROP TABLE [ContainerAnnotations];

DECLARE @var2 sysname;
SELECT @var2 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[FS6000Scans]') AND [c].[name] = N'HasImage');
IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [FS6000Scans] DROP CONSTRAINT [' + @var2 + '];');
ALTER TABLE [FS6000Scans] DROP COLUMN [HasImage];

DECLARE @var3 sysname;
SELECT @var3 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[FS6000Scans]') AND [c].[name] = N'ImageCount');
IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [FS6000Scans] DROP CONSTRAINT [' + @var3 + '];');
ALTER TABLE [FS6000Scans] DROP COLUMN [ImageCount];

DECLARE @var4 sysname;
SELECT @var4 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[FS6000Scans]') AND [c].[name] = N'ImageIngestedAt');
IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [FS6000Scans] DROP CONSTRAINT [' + @var4 + '];');
ALTER TABLE [FS6000Scans] DROP COLUMN [ImageIngestedAt];

DECLARE @var5 sysname;
SELECT @var5 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[FS6000Scans]') AND [c].[name] = N'ImageValidationError');
IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [FS6000Scans] DROP CONSTRAINT [' + @var5 + '];');
ALTER TABLE [FS6000Scans] DROP COLUMN [ImageValidationError];

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20251008182401_AddFS6000ImageCompletenessFields';

COMMIT;
GO

