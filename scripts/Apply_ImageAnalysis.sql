IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [AseScans] (
    [Id] uniqueidentifier NOT NULL,
    [InspectionId] int NOT NULL,
    [ScanTime] datetime2 NOT NULL,
    [InspectionUuid] nvarchar(50) NOT NULL,
    [ContainerNumber] nvarchar(50) NULL,
    [TruckPlate] nvarchar(20) NULL,
    [ScanImage] varbinary(max) NULL,
    [ImageDisplayName] nvarchar(100) NULL,
    [SyncedAt] datetime2 NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_AseScans] PRIMARY KEY ([Id])
);

CREATE TABLE [AseSyncLogs] (
    [Id] int NOT NULL IDENTITY,
    [LastSyncedInspectionId] int NOT NULL,
    [LastSyncTime] datetime2 NOT NULL,
    [RecordsProcessed] int NOT NULL,
    [SyncStatus] nvarchar(20) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_AseSyncLogs] PRIMARY KEY ([Id])
);

CREATE TABLE [Containers] (
    [Id] int NOT NULL IDENTITY,
    [ContainerId] nvarchar(50) NOT NULL,
    [ScannerType] nvarchar(20) NOT NULL,
    [ScannerId] nvarchar(100) NOT NULL,
    [ScanDateTime] datetime2 NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    [ProcessingStatus] nvarchar(20) NOT NULL DEFAULT N'Pending',
    CONSTRAINT [PK_Containers] PRIMARY KEY ([Id])
);

CREATE TABLE [FS6000FileProcessings] (
    [Id] uniqueidentifier NOT NULL,
    [FilePath] nvarchar(500) NOT NULL,
    [FileName] nvarchar(255) NOT NULL,
    [FileType] nvarchar(10) NOT NULL,
    [ProcessingStatus] nvarchar(20) NOT NULL,
    [ErrorMessage] nvarchar(1000) NULL,
    [ProcessedAt] datetime2 NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_FS6000FileProcessings] PRIMARY KEY ([Id])
);

CREATE TABLE [FS6000Scans] (
    [Id] uniqueidentifier NOT NULL,
    [ContainerNumber] nvarchar(50) NOT NULL,
    [ScanTime] datetime2 NOT NULL,
    [PicNumber] nvarchar(50) NOT NULL,
    [FycoPresent] nvarchar(100) NULL,
    [VesselName] nvarchar(100) NULL,
    [OperatorId] nvarchar(50) NULL,
    [ScanResult] nvarchar(50) NULL,
    [GoodsDescription] nvarchar(500) NULL,
    [ShippingCompany] nvarchar(100) NULL,
    [Consignee] nvarchar(100) NULL,
    [FilePath] nvarchar(500) NULL,
    [SyncStatus] nvarchar(20) NOT NULL DEFAULT N'Pending',
    [CreatedAt] datetime2 NOT NULL,
    [ProcessedAt] datetime2 NULL,
    CONSTRAINT [PK_FS6000Scans] PRIMARY KEY ([Id])
);

CREATE TABLE [FS6000SyncLogs] (
    [Id] uniqueidentifier NOT NULL,
    [SourcePath] nvarchar(500) NOT NULL,
    [DestinationPath] nvarchar(500) NOT NULL,
    [SyncStatus] nvarchar(20) NOT NULL,
    [ErrorMessage] nvarchar(1000) NULL,
    [RetryCount] int NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [LastRetryAt] datetime2 NULL,
    [CompletedAt] datetime2 NULL,
    CONSTRAINT [PK_FS6000SyncLogs] PRIMARY KEY ([Id])
);

CREATE TABLE [HeimannSmithScannerData] (
    [Id] int NOT NULL IDENTITY,
    [ContainerId] nvarchar(50) NOT NULL,
    [ScannerId] nvarchar(100) NOT NULL,
    [ScanDateTime] datetime2 NOT NULL,
    [RawData] nvarchar(max) NULL,
    [ImagePath] nvarchar(max) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [ProcessedAt] datetime2 NULL,
    [ProcessingStatus] nvarchar(20) NOT NULL,
    CONSTRAINT [PK_HeimannSmithScannerData] PRIMARY KEY ([Id])
);

CREATE TABLE [NuctechScannerData] (
    [Id] int NOT NULL IDENTITY,
    [ContainerId] nvarchar(50) NOT NULL,
    [ScannerId] nvarchar(100) NOT NULL,
    [ScanDateTime] datetime2 NOT NULL,
    [RawData] nvarchar(max) NULL,
    [ImagePath] nvarchar(max) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [ProcessedAt] datetime2 NULL,
    [ProcessingStatus] nvarchar(20) NOT NULL,
    CONSTRAINT [PK_NuctechScannerData] PRIMARY KEY ([Id])
);

CREATE TABLE [ContainerImages] (
    [Id] int NOT NULL IDENTITY,
    [ContainerId] int NOT NULL,
    [ImagePath] nvarchar(255) NOT NULL,
    [ImageType] nvarchar(50) NOT NULL,
    [FileSizeBytes] bigint NOT NULL,
    [OriginalFileName] nvarchar(100) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [ProcessedAt] datetime2 NULL,
    [ProcessingStatus] nvarchar(20) NOT NULL DEFAULT N'Pending',
    CONSTRAINT [PK_ContainerImages] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ContainerImages_Containers_ContainerId] FOREIGN KEY ([ContainerId]) REFERENCES [Containers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [ProcessingResults] (
    [Id] int NOT NULL IDENTITY,
    [ContainerId] int NOT NULL,
    [ResultType] nvarchar(50) NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [ResultData] nvarchar(max) NULL,
    [ErrorMessage] nvarchar(max) NULL,
    [ProcessedAt] datetime2 NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_ProcessingResults] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ProcessingResults_Containers_ContainerId] FOREIGN KEY ([ContainerId]) REFERENCES [Containers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [FS6000Images] (
    [Id] uniqueidentifier NOT NULL,
    [ScanId] uniqueidentifier NOT NULL,
    [ImageType] nvarchar(20) NOT NULL,
    [FileName] nvarchar(200) NOT NULL,
    [ImageData] varbinary(max) NULL,
    [FileSizeBytes] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_FS6000Images] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_FS6000Images_FS6000Scans_ScanId] FOREIGN KEY ([ScanId]) REFERENCES [FS6000Scans] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_AseScans_ContainerNumber] ON [AseScans] ([ContainerNumber]);

CREATE UNIQUE INDEX [IX_AseScans_InspectionId] ON [AseScans] ([InspectionId]);

CREATE INDEX [IX_AseScans_InspectionUuid] ON [AseScans] ([InspectionUuid]);

CREATE INDEX [IX_AseScans_ScanTime] ON [AseScans] ([ScanTime]);

CREATE INDEX [IX_AseSyncLogs_LastSyncTime] ON [AseSyncLogs] ([LastSyncTime]);

CREATE INDEX [IX_AseSyncLogs_SyncStatus] ON [AseSyncLogs] ([SyncStatus]);

CREATE INDEX [IX_ContainerImages_ContainerId] ON [ContainerImages] ([ContainerId]);

CREATE UNIQUE INDEX [IX_Containers_ContainerId] ON [Containers] ([ContainerId]);

CREATE INDEX [IX_Containers_ProcessingStatus] ON [Containers] ([ProcessingStatus]);

CREATE INDEX [IX_Containers_ScannerType] ON [Containers] ([ScannerType]);

CREATE INDEX [IX_FS6000FileProcessings_FileName] ON [FS6000FileProcessings] ([FileName]);

CREATE INDEX [IX_FS6000FileProcessings_ProcessingStatus] ON [FS6000FileProcessings] ([ProcessingStatus]);

CREATE INDEX [IX_FS6000Images_ScanId] ON [FS6000Images] ([ScanId]);

CREATE INDEX [IX_FS6000Scans_ContainerNumber] ON [FS6000Scans] ([ContainerNumber]);

CREATE INDEX [IX_FS6000Scans_PicNumber] ON [FS6000Scans] ([PicNumber]);

CREATE INDEX [IX_FS6000Scans_ScanTime] ON [FS6000Scans] ([ScanTime]);

CREATE INDEX [IX_FS6000Scans_SyncStatus] ON [FS6000Scans] ([SyncStatus]);

CREATE INDEX [IX_FS6000SyncLogs_CompletedAt] ON [FS6000SyncLogs] ([CompletedAt]);

CREATE INDEX [IX_FS6000SyncLogs_SourcePath] ON [FS6000SyncLogs] ([SourcePath]);

CREATE INDEX [IX_ProcessingResults_ContainerId] ON [ProcessingResults] ([ContainerId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20250916115430_AddAseTables', N'9.0.9');

DROP INDEX [IX_FS6000SyncLogs_CompletedAt] ON [FS6000SyncLogs];

DROP INDEX [IX_FS6000SyncLogs_SourcePath] ON [FS6000SyncLogs];

DROP INDEX [IX_FS6000Scans_ContainerNumber] ON [FS6000Scans];

DROP INDEX [IX_FS6000Scans_PicNumber] ON [FS6000Scans];

DROP INDEX [IX_FS6000Scans_ScanTime] ON [FS6000Scans];

DROP INDEX [IX_FS6000Scans_SyncStatus] ON [FS6000Scans];

DROP INDEX [IX_FS6000FileProcessings_FileName] ON [FS6000FileProcessings];

DROP INDEX [IX_FS6000FileProcessings_ProcessingStatus] ON [FS6000FileProcessings];

DROP INDEX [IX_Containers_ContainerId] ON [Containers];

DROP INDEX [IX_Containers_ProcessingStatus] ON [Containers];

DROP INDEX [IX_Containers_ScannerType] ON [Containers];

DROP INDEX [IX_AseSyncLogs_LastSyncTime] ON [AseSyncLogs];

DROP INDEX [IX_AseSyncLogs_SyncStatus] ON [AseSyncLogs];

DROP INDEX [IX_AseScans_ContainerNumber] ON [AseScans];

DROP INDEX [IX_AseScans_InspectionId] ON [AseScans];

DROP INDEX [IX_AseScans_InspectionUuid] ON [AseScans];

DROP INDEX [IX_AseScans_ScanTime] ON [AseScans];

DECLARE @var sysname;
SELECT @var = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[FS6000Scans]') AND [c].[name] = N'SyncStatus');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [FS6000Scans] DROP CONSTRAINT [' + @var + '];');

DECLARE @var1 sysname;
SELECT @var1 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[FS6000Scans]') AND [c].[name] = N'ShippingCompany');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [FS6000Scans] DROP CONSTRAINT [' + @var1 + '];');
ALTER TABLE [FS6000Scans] ALTER COLUMN [ShippingCompany] nvarchar(200) NULL;

DECLARE @var2 sysname;
SELECT @var2 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[FS6000Scans]') AND [c].[name] = N'PicNumber');
IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [FS6000Scans] DROP CONSTRAINT [' + @var2 + '];');
ALTER TABLE [FS6000Scans] ALTER COLUMN [PicNumber] nvarchar(100) NOT NULL;

DECLARE @var3 sysname;
SELECT @var3 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[FS6000Scans]') AND [c].[name] = N'Consignee');
IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [FS6000Scans] DROP CONSTRAINT [' + @var3 + '];');
ALTER TABLE [FS6000Scans] ALTER COLUMN [Consignee] nvarchar(200) NULL;

DECLARE @var4 sysname;
SELECT @var4 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[FS6000FileProcessings]') AND [c].[name] = N'FileName');
IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [FS6000FileProcessings] DROP CONSTRAINT [' + @var4 + '];');
ALTER TABLE [FS6000FileProcessings] ALTER COLUMN [FileName] nvarchar(200) NOT NULL;

DECLARE @var5 sysname;
SELECT @var5 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Containers]') AND [c].[name] = N'ProcessingStatus');
IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [Containers] DROP CONSTRAINT [' + @var5 + '];');

DECLARE @var6 sysname;
SELECT @var6 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ContainerImages]') AND [c].[name] = N'ProcessingStatus');
IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [ContainerImages] DROP CONSTRAINT [' + @var6 + '];');

DECLARE @var7 sysname;
SELECT @var7 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AseSyncLogs]') AND [c].[name] = N'SyncStatus');
IF @var7 IS NOT NULL EXEC(N'ALTER TABLE [AseSyncLogs] DROP CONSTRAINT [' + @var7 + '];');
ALTER TABLE [AseSyncLogs] ALTER COLUMN [SyncStatus] nvarchar(max) NOT NULL;

DECLARE @var8 sysname;
SELECT @var8 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AseScans]') AND [c].[name] = N'TruckPlate');
IF @var8 IS NOT NULL EXEC(N'ALTER TABLE [AseScans] DROP CONSTRAINT [' + @var8 + '];');
ALTER TABLE [AseScans] ALTER COLUMN [TruckPlate] nvarchar(max) NULL;

DECLARE @var9 sysname;
SELECT @var9 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AseScans]') AND [c].[name] = N'InspectionUuid');
IF @var9 IS NOT NULL EXEC(N'ALTER TABLE [AseScans] DROP CONSTRAINT [' + @var9 + '];');
ALTER TABLE [AseScans] ALTER COLUMN [InspectionUuid] nvarchar(max) NOT NULL;

DECLARE @var10 sysname;
SELECT @var10 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AseScans]') AND [c].[name] = N'ImageDisplayName');
IF @var10 IS NOT NULL EXEC(N'ALTER TABLE [AseScans] DROP CONSTRAINT [' + @var10 + '];');
ALTER TABLE [AseScans] ALTER COLUMN [ImageDisplayName] nvarchar(max) NULL;

DECLARE @var11 sysname;
SELECT @var11 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AseScans]') AND [c].[name] = N'ContainerNumber');
IF @var11 IS NOT NULL EXEC(N'ALTER TABLE [AseScans] DROP CONSTRAINT [' + @var11 + '];');
ALTER TABLE [AseScans] ALTER COLUMN [ContainerNumber] nvarchar(max) NULL;

CREATE TABLE [ImageCaches] (
    [Id] int NOT NULL IDENTITY,
    [ContainerNumber] nvarchar(50) NOT NULL,
    [ScannerType] nvarchar(20) NOT NULL,
    [ImageData] varbinary(MAX) NOT NULL,
    [MimeType] nvarchar(50) NOT NULL DEFAULT N'image/jpeg',
    [Width] int NOT NULL,
    [Height] int NOT NULL,
    [FileSizeBytes] bigint NOT NULL,
    [ScanTime] datetime2 NOT NULL,
    [CachedAt] datetime2 NOT NULL,
    [ProcessingPipeline] nvarchar(100) NOT NULL,
    [Quality] nvarchar(50) NOT NULL DEFAULT N'High',
    CONSTRAINT [PK_ImageCaches] PRIMARY KEY ([Id])
);

CREATE INDEX [IX_ImageCaches_ContainerNumber] ON [ImageCaches] ([ContainerNumber]);

CREATE INDEX [IX_ImageCaches_ContainerNumber_ScannerType] ON [ImageCaches] ([ContainerNumber], [ScannerType]);

CREATE INDEX [IX_ImageCaches_ScannerType] ON [ImageCaches] ([ScannerType]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20250916173810_AddMissingTables', N'9.0.9');

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20250916175227_InitialCreate', N'9.0.9');

CREATE TABLE [ContainerBOERelations] (
    [Id] int NOT NULL IDENTITY,
    [ContainerNumber] nvarchar(50) NOT NULL,
    [ScannerDataId] int NOT NULL,
    [ScannerType] nvarchar(20) NOT NULL,
    [ICUMSBOEId] int NOT NULL,
    [RelationType] nvarchar(20) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [Notes] nvarchar(500) NULL,
    [IsActive] bit NOT NULL,
    [LastValidatedAt] datetime2 NULL,
    CONSTRAINT [PK_ContainerBOERelations] PRIMARY KEY ([Id])
);

CREATE TABLE [ContainerCompletenessStatuses] (
    [Id] int NOT NULL IDENTITY,
    [ContainerNumber] nvarchar(50) NOT NULL,
    [ScannerType] nvarchar(20) NOT NULL,
    [ScanDate] datetime2 NOT NULL,
    [HasICUMSData] bit NOT NULL,
    [ICUMSDataDate] datetime2 NULL,
    [Status] nvarchar(20) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    [ErrorMessage] nvarchar(1000) NULL,
    [RetryCount] int NOT NULL,
    [LastCheckedAt] datetime2 NULL,
    CONSTRAINT [PK_ContainerCompletenessStatuses] PRIMARY KEY ([Id])
);

CREATE TABLE [ICUMSSubmissionQueues] (
    [Id] int NOT NULL IDENTITY,
    [ContainerNumber] nvarchar(50) NOT NULL,
    [ScannerType] nvarchar(20) NOT NULL,
    [ImagePaths] nvarchar(max) NOT NULL,
    [ReportData] nvarchar(max) NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [Priority] int NOT NULL,
    [SubmittedAt] datetime2 NULL,
    [ICUMSResponseId] nvarchar(100) NULL,
    [ErrorMessage] nvarchar(1000) NULL,
    [RetryCount] int NOT NULL,
    [NextRetryAt] datetime2 NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    [SubmittedBy] nvarchar(50) NULL,
    [CompletedAt] datetime2 NULL,
    CONSTRAINT [PK_ICUMSSubmissionQueues] PRIMARY KEY ([Id])
);

CREATE TABLE [ManualBOERequests] (
    [Id] int NOT NULL IDENTITY,
    [ContainerNumber] nvarchar(50) NOT NULL,
    [RequestDate] datetime2 NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [ICUMSResponseId] nvarchar(100) NULL,
    [ErrorMessage] nvarchar(1000) NULL,
    [RetryCount] int NOT NULL,
    [CompletedAt] datetime2 NULL,
    [NextRetryAt] datetime2 NULL,
    [RequestedBy] nvarchar(50) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_ManualBOERequests] PRIMARY KEY ([Id])
);

CREATE TABLE [Users] (
    [Id] int NOT NULL IDENTITY,
    [Username] nvarchar(50) NOT NULL,
    [Email] nvarchar(100) NOT NULL,
    [PasswordHash] nvarchar(100) NOT NULL,
    [FirstName] nvarchar(50) NOT NULL,
    [LastName] nvarchar(50) NOT NULL,
    [Role] int NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [LastLoginAt] datetime2 NULL,
    [CreatedBy] nvarchar(50) NULL,
    [UpdatedAt] datetime2 NULL,
    [UpdatedBy] nvarchar(50) NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
);

CREATE INDEX [IX_ContainerBOERelations_ContainerNumber] ON [ContainerBOERelations] ([ContainerNumber]);

CREATE INDEX [IX_ContainerBOERelations_ICUMSBOEId] ON [ContainerBOERelations] ([ICUMSBOEId]);

CREATE INDEX [IX_ContainerBOERelations_IsActive] ON [ContainerBOERelations] ([IsActive]);

CREATE INDEX [IX_ContainerBOERelations_ScannerType] ON [ContainerBOERelations] ([ScannerType]);

CREATE INDEX [IX_ContainerCompletenessStatuses_ContainerNumber] ON [ContainerCompletenessStatuses] ([ContainerNumber]);

CREATE INDEX [IX_ContainerCompletenessStatuses_ContainerNumber_ScannerType] ON [ContainerCompletenessStatuses] ([ContainerNumber], [ScannerType]);

CREATE INDEX [IX_ContainerCompletenessStatuses_HasICUMSData] ON [ContainerCompletenessStatuses] ([HasICUMSData]);

CREATE INDEX [IX_ContainerCompletenessStatuses_ScannerType] ON [ContainerCompletenessStatuses] ([ScannerType]);

CREATE INDEX [IX_ContainerCompletenessStatuses_Status] ON [ContainerCompletenessStatuses] ([Status]);

CREATE INDEX [IX_ICUMSSubmissionQueues_ContainerNumber] ON [ICUMSSubmissionQueues] ([ContainerNumber]);

CREATE INDEX [IX_ICUMSSubmissionQueues_NextRetryAt] ON [ICUMSSubmissionQueues] ([NextRetryAt]);

CREATE INDEX [IX_ICUMSSubmissionQueues_Priority] ON [ICUMSSubmissionQueues] ([Priority]);

CREATE INDEX [IX_ICUMSSubmissionQueues_Status] ON [ICUMSSubmissionQueues] ([Status]);

CREATE INDEX [IX_ManualBOERequests_ContainerNumber] ON [ManualBOERequests] ([ContainerNumber]);

CREATE INDEX [IX_ManualBOERequests_NextRetryAt] ON [ManualBOERequests] ([NextRetryAt]);

CREATE INDEX [IX_ManualBOERequests_RequestDate] ON [ManualBOERequests] ([RequestDate]);

CREATE INDEX [IX_ManualBOERequests_Status] ON [ManualBOERequests] ([Status]);

CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);

CREATE INDEX [IX_Users_IsActive] ON [Users] ([IsActive]);

CREATE UNIQUE INDEX [IX_Users_Username] ON [Users] ([Username]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20251003105725_AddContainerDataCompletenessTables', N'9.0.9');

ALTER TABLE [FS6000Scans] ADD [HasImage] bit NOT NULL DEFAULT CAST(0 AS bit);

ALTER TABLE [FS6000Scans] ADD [ImageCount] int NOT NULL DEFAULT 0;

ALTER TABLE [FS6000Scans] ADD [ImageIngestedAt] datetime2 NULL;

ALTER TABLE [FS6000Scans] ADD [ImageValidationError] nvarchar(500) NULL;

CREATE TABLE [ContainerAnnotations] (
    [Id] bigint NOT NULL IDENTITY,
    [ContainerNumber] nvarchar(50) NOT NULL,
    [Type] nvarchar(50) NOT NULL,
    [X1] float NOT NULL,
    [Y1] float NOT NULL,
    [X2] float NOT NULL,
    [Y2] float NOT NULL,
    [Color] nvarchar(20) NOT NULL DEFAULT N'#ff0000',
    [Width] int NOT NULL DEFAULT 2,
    [Text] nvarchar(1000) NULL,
    [Comment] nvarchar(2000) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [CreatedBy] nvarchar(100) NOT NULL,
    [UpdatedAt] datetime2 NULL,
    [UpdatedBy] nvarchar(100) NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAt] datetime2 NULL,
    [DeletedBy] nvarchar(100) NULL,
    CONSTRAINT [PK_ContainerAnnotations] PRIMARY KEY ([Id])
);

CREATE INDEX [IX_ContainerAnnotations_ContainerNumber] ON [ContainerAnnotations] ([ContainerNumber]);

CREATE INDEX [IX_ContainerAnnotations_CreatedAt] ON [ContainerAnnotations] ([CreatedAt]);

CREATE INDEX [IX_ContainerAnnotations_CreatedBy] ON [ContainerAnnotations] ([CreatedBy]);

CREATE INDEX [IX_ContainerAnnotations_IsDeleted] ON [ContainerAnnotations] ([IsDeleted]);

CREATE INDEX [IX_ContainerAnnotations_Type] ON [ContainerAnnotations] ([Type]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20251008182401_AddFS6000ImageCompletenessFields', N'9.0.9');

ALTER TABLE [Users] ADD [LegacyRole] int NOT NULL DEFAULT 0;

ALTER TABLE [Users] ADD [RoleId] int NULL;

CREATE TABLE [ICUMSDownloadQueues] (
    [Id] int NOT NULL IDENTITY,
    [ContainerNumber] nvarchar(20) NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [Priority] int NOT NULL,
    [QueuedAt] datetime2 NOT NULL,
    [FirstAttemptAt] datetime2 NULL,
    [LastAttemptAt] datetime2 NULL,
    [CompletedAt] datetime2 NULL,
    [RetryCount] int NOT NULL,
    [MaxRetries] int NOT NULL,
    [LastErrorMessage] nvarchar(1000) NULL,
    [LastErrorCode] nvarchar(50) NULL,
    [RequestedBy] nvarchar(100) NULL,
    [RequestSource] nvarchar(50) NULL,
    [Metadata] nvarchar(2000) NULL,
    CONSTRAINT [PK_ICUMSDownloadQueues] PRIMARY KEY ([Id])
);

CREATE TABLE [Permissions] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(100) NOT NULL,
    [DisplayName] nvarchar(200) NOT NULL,
    [Description] nvarchar(500) NULL,
    [Category] nvarchar(50) NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Permissions] PRIMARY KEY ([Id])
);

CREATE TABLE [Roles] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(100) NOT NULL,
    [DisplayName] nvarchar(200) NOT NULL,
    [Description] nvarchar(500) NULL,
    [BaseRole] int NULL,
    [IsSystemRole] bit NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [CreatedBy] nvarchar(50) NULL,
    [UpdatedAt] datetime2 NULL,
    [UpdatedBy] nvarchar(50) NULL,
    CONSTRAINT [PK_Roles] PRIMARY KEY ([Id])
);

CREATE TABLE [UserPermissions] (
    [Id] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [PermissionId] int NOT NULL,
    [IsGranted] bit NOT NULL,
    [GrantedAt] datetime2 NOT NULL,
    [GrantedBy] nvarchar(50) NULL,
    [ExpiresAt] datetime2 NULL,
    [Reason] nvarchar(500) NULL,
    CONSTRAINT [PK_UserPermissions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_UserPermissions_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [Permissions] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_UserPermissions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [PermissionAuditLogs] (
    [Id] int NOT NULL IDENTITY,
    [Action] nvarchar(50) NOT NULL,
    [EntityType] nvarchar(50) NOT NULL,
    [EntityId] int NOT NULL,
    [PermissionId] int NULL,
    [RoleId] int NULL,
    [UserId] int NULL,
    [Result] bit NULL,
    [IPAddress] nvarchar(50) NULL,
    [Timestamp] datetime2 NOT NULL,
    [Details] nvarchar(4000) NULL,
    CONSTRAINT [PK_PermissionAuditLogs] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PermissionAuditLogs_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [Permissions] ([Id]) ON DELETE SET NULL,
    CONSTRAINT [FK_PermissionAuditLogs_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE SET NULL,
    CONSTRAINT [FK_PermissionAuditLogs_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
);

CREATE TABLE [RolePermissions] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] int NOT NULL,
    [PermissionId] int NOT NULL,
    [GrantedAt] datetime2 NOT NULL,
    [GrantedBy] nvarchar(50) NULL,
    CONSTRAINT [PK_RolePermissions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RolePermissions_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [Permissions] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_RolePermissions_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_Users_RoleId] ON [Users] ([RoleId]);

CREATE INDEX [IX_ICUMSDownloadQueues_ContainerNumber] ON [ICUMSDownloadQueues] ([ContainerNumber]);

CREATE INDEX [IX_ICUMSDownloadQueues_ContainerNumber_Status] ON [ICUMSDownloadQueues] ([ContainerNumber], [Status]);

CREATE INDEX [IX_ICUMSDownloadQueues_Priority] ON [ICUMSDownloadQueues] ([Priority]);

CREATE INDEX [IX_ICUMSDownloadQueues_QueuedAt] ON [ICUMSDownloadQueues] ([QueuedAt]);

CREATE INDEX [IX_ICUMSDownloadQueues_Status] ON [ICUMSDownloadQueues] ([Status]);

CREATE INDEX [IX_PermissionAuditLogs_Action] ON [PermissionAuditLogs] ([Action]);

CREATE INDEX [IX_PermissionAuditLogs_EntityId] ON [PermissionAuditLogs] ([EntityId]);

CREATE INDEX [IX_PermissionAuditLogs_EntityType] ON [PermissionAuditLogs] ([EntityType]);

CREATE INDEX [IX_PermissionAuditLogs_PermissionId] ON [PermissionAuditLogs] ([PermissionId]);

CREATE INDEX [IX_PermissionAuditLogs_Result] ON [PermissionAuditLogs] ([Result]);

CREATE INDEX [IX_PermissionAuditLogs_RoleId] ON [PermissionAuditLogs] ([RoleId]);

CREATE INDEX [IX_PermissionAuditLogs_Timestamp] ON [PermissionAuditLogs] ([Timestamp]);

CREATE INDEX [IX_PermissionAuditLogs_UserId] ON [PermissionAuditLogs] ([UserId]);

CREATE INDEX [IX_Permissions_Category] ON [Permissions] ([Category]);

CREATE INDEX [IX_Permissions_IsActive] ON [Permissions] ([IsActive]);

CREATE UNIQUE INDEX [IX_Permissions_Name] ON [Permissions] ([Name]);

CREATE INDEX [IX_RolePermissions_PermissionId] ON [RolePermissions] ([PermissionId]);

CREATE INDEX [IX_RolePermissions_RoleId] ON [RolePermissions] ([RoleId]);

CREATE UNIQUE INDEX [IX_RolePermissions_RoleId_PermissionId] ON [RolePermissions] ([RoleId], [PermissionId]);

CREATE INDEX [IX_Roles_IsActive] ON [Roles] ([IsActive]);

CREATE INDEX [IX_Roles_IsSystemRole] ON [Roles] ([IsSystemRole]);

CREATE UNIQUE INDEX [IX_Roles_Name] ON [Roles] ([Name]);

CREATE INDEX [IX_UserPermissions_ExpiresAt] ON [UserPermissions] ([ExpiresAt]);

CREATE INDEX [IX_UserPermissions_IsGranted] ON [UserPermissions] ([IsGranted]);

CREATE INDEX [IX_UserPermissions_PermissionId] ON [UserPermissions] ([PermissionId]);

CREATE INDEX [IX_UserPermissions_UserId] ON [UserPermissions] ([UserId]);

CREATE UNIQUE INDEX [IX_UserPermissions_UserId_PermissionId] ON [UserPermissions] ([UserId], [PermissionId]);

ALTER TABLE [Users] ADD CONSTRAINT [FK_Users_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE SET NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20251018115544_AddICUMSDownloadQueueTable', N'9.0.9');

CREATE TABLE [BLReviewRecords] (
    [Id] int NOT NULL IDENTITY,
    [MasterBlNumber] nvarchar(100) NOT NULL,
    [ReviewStartedAt] datetime2 NOT NULL,
    [ReviewCompletedAt] datetime2 NULL,
    [ReviewedBy] nvarchar(100) NOT NULL,
    [ReviewStatus] nvarchar(50) NOT NULL,
    [FinalDecision] nvarchar(50) NOT NULL,
    [BLComments] nvarchar(2000) NOT NULL,
    [TotalContainers] int NOT NULL,
    [ReviewedContainers] int NOT NULL,
    [NormalContainers] int NOT NULL,
    [AbnormalContainers] int NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_BLReviewRecords] PRIMARY KEY ([Id])
);

CREATE TABLE [ContainerReviewDecisions] (
    [Id] int NOT NULL IDENTITY,
    [BLReviewRecordId] int NOT NULL,
    [ContainerNumber] nvarchar(50) NOT NULL,
    [Decision] nvarchar(50) NOT NULL,
    [Comments] nvarchar(1000) NOT NULL,
    [ReviewedBy] nvarchar(100) NOT NULL,
    [ReviewedAt] datetime2 NULL,
    [HasScanner] bit NOT NULL,
    [HasICUMS] bit NOT NULL,
    [HasImages] bit NOT NULL,
    [ScannerType] nvarchar(50) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_ContainerReviewDecisions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ContainerReviewDecisions_BLReviewRecords_BLReviewRecordId] FOREIGN KEY ([BLReviewRecordId]) REFERENCES [BLReviewRecords] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_BLReviewRecords_FinalDecision] ON [BLReviewRecords] ([FinalDecision]);

CREATE INDEX [IX_BLReviewRecords_MasterBlNumber] ON [BLReviewRecords] ([MasterBlNumber]);

CREATE INDEX [IX_BLReviewRecords_ReviewCompletedAt] ON [BLReviewRecords] ([ReviewCompletedAt]);

CREATE INDEX [IX_BLReviewRecords_ReviewedBy] ON [BLReviewRecords] ([ReviewedBy]);

CREATE INDEX [IX_BLReviewRecords_ReviewStartedAt] ON [BLReviewRecords] ([ReviewStartedAt]);

CREATE INDEX [IX_BLReviewRecords_ReviewStatus] ON [BLReviewRecords] ([ReviewStatus]);

CREATE INDEX [IX_ContainerReviewDecisions_BLReviewRecordId] ON [ContainerReviewDecisions] ([BLReviewRecordId]);

CREATE INDEX [IX_ContainerReviewDecisions_ContainerNumber] ON [ContainerReviewDecisions] ([ContainerNumber]);

CREATE INDEX [IX_ContainerReviewDecisions_Decision] ON [ContainerReviewDecisions] ([Decision]);

CREATE INDEX [IX_ContainerReviewDecisions_ReviewedAt] ON [ContainerReviewDecisions] ([ReviewedAt]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20251018204900_AddBLReviewTables', N'9.0.9');

CREATE TABLE [IcumContainerData] (
    [Id] int NOT NULL IDENTITY,
    [ContainerNumber] nvarchar(50) NOT NULL,
    [BoeData] nvarchar(max) NULL,
    [MasterBlNumber] nvarchar(100) NULL,
    [HouseBl] nvarchar(100) NULL,
    [RotationNumber] nvarchar(50) NULL,
    [ConsigneeName] nvarchar(200) NULL,
    [ShipperName] nvarchar(200) NULL,
    [CountryOfOrigin] nvarchar(100) NULL,
    [TotalDutyPaid] decimal(18,2) NULL,
    [CrmsLevel] nvarchar(50) NULL,
    [ClearanceType] nvarchar(50) NULL,
    [DeclarationNumber] nvarchar(100) NULL,
    [ContainerWeight] decimal(18,2) NULL,
    [ContainerQuantity] int NULL,
    [ContainerISO] nvarchar(20) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    [Status] nvarchar(50) NULL,
    CONSTRAINT [PK_IcumContainerData] PRIMARY KEY ([Id])
);

CREATE TABLE [IcumManifestItems] (
    [Id] int NOT NULL IDENTITY,
    [IcumContainerDataId] int NOT NULL,
    [HsCode] nvarchar(20) NOT NULL,
    [Description] nvarchar(500) NOT NULL,
    [Quantity] decimal(18,2) NOT NULL,
    [Unit] nvarchar(20) NOT NULL,
    [Weight] decimal(18,2) NOT NULL,
    [ItemFob] decimal(18,2) NOT NULL,
    [ItemDutyPaid] decimal(18,2) NOT NULL,
    [FobCurrency] nvarchar(10) NOT NULL,
    [CountryOfOrigin] nvarchar(100) NOT NULL,
    [ItemNo] int NOT NULL,
    [Cpc] nvarchar(20) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_IcumManifestItems] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_IcumManifestItems_IcumContainerData_IcumContainerDataId] FOREIGN KEY ([IcumContainerDataId]) REFERENCES [IcumContainerData] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_IcumContainerData_ContainerNumber] ON [IcumContainerData] ([ContainerNumber]);

CREATE INDEX [IX_IcumContainerData_CreatedAt] ON [IcumContainerData] ([CreatedAt]);

CREATE INDEX [IX_IcumContainerData_HouseBl] ON [IcumContainerData] ([HouseBl]);

CREATE INDEX [IX_IcumContainerData_MasterBlNumber] ON [IcumContainerData] ([MasterBlNumber]);

CREATE INDEX [IX_IcumContainerData_RotationNumber] ON [IcumContainerData] ([RotationNumber]);

CREATE INDEX [IX_IcumContainerData_Status] ON [IcumContainerData] ([Status]);

CREATE INDEX [IX_IcumManifestItems_HsCode] ON [IcumManifestItems] ([HsCode]);

CREATE INDEX [IX_IcumManifestItems_IcumContainerDataId] ON [IcumManifestItems] ([IcumContainerDataId]);

CREATE INDEX [IX_IcumManifestItems_ItemNo] ON [IcumManifestItems] ([ItemNo]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20251019084637_AddICUMSContainerDataTables', N'9.0.9');

CREATE TABLE [SystemSettings] (
    [Id] int NOT NULL IDENTITY,
    [Category] nvarchar(50) NOT NULL,
    [SettingKey] nvarchar(100) NOT NULL,
    [SettingValue] nvarchar(max) NOT NULL,
    [DataType] nvarchar(20) NOT NULL,
    [Description] nvarchar(500) NULL,
    [DefaultValue] nvarchar(1000) NULL,
    [IsEncrypted] bit NOT NULL,
    [RequiresRestart] bit NOT NULL,
    [AllowedRoles] nvarchar(200) NULL,
    [IsActive] bit NOT NULL,
    [DisplayOrder] int NOT NULL,
    [ValidationRules] nvarchar(max) NULL,
    [LastModifiedBy] nvarchar(100) NULL,
    [LastModifiedAt] datetime2 NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_SystemSettings] PRIMARY KEY ([Id])
);

CREATE TABLE [UserPreferences] (
    [Id] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [PreferenceKey] nvarchar(100) NOT NULL,
    [PreferenceValue] nvarchar(max) NOT NULL,
    [DataType] nvarchar(20) NOT NULL,
    [Description] nvarchar(500) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_UserPreferences] PRIMARY KEY ([Id])
);

CREATE TABLE [SettingsHistory] (
    [Id] int NOT NULL IDENTITY,
    [SystemSettingId] int NOT NULL,
    [Category] nvarchar(50) NOT NULL,
    [SettingKey] nvarchar(100) NOT NULL,
    [OldValue] nvarchar(max) NULL,
    [NewValue] nvarchar(max) NOT NULL,
    [ChangedBy] nvarchar(100) NOT NULL,
    [Reason] nvarchar(500) NULL,
    [IpAddress] nvarchar(50) NULL,
    [ChangedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_SettingsHistory] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SettingsHistory_SystemSettings_SystemSettingId] FOREIGN KEY ([SystemSettingId]) REFERENCES [SystemSettings] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_SettingsHistory_Category] ON [SettingsHistory] ([Category]);

CREATE INDEX [IX_SettingsHistory_ChangedAt] ON [SettingsHistory] ([ChangedAt]);

CREATE INDEX [IX_SettingsHistory_ChangedBy] ON [SettingsHistory] ([ChangedBy]);

CREATE INDEX [IX_SettingsHistory_SystemSettingId] ON [SettingsHistory] ([SystemSettingId]);

CREATE INDEX [IX_SystemSettings_Category] ON [SystemSettings] ([Category]);

CREATE UNIQUE INDEX [IX_SystemSettings_Category_SettingKey] ON [SystemSettings] ([Category], [SettingKey]);

CREATE INDEX [IX_SystemSettings_IsActive] ON [SystemSettings] ([IsActive]);

CREATE INDEX [IX_SystemSettings_LastModifiedAt] ON [SystemSettings] ([LastModifiedAt]);

CREATE INDEX [IX_UserPreferences_UserId] ON [UserPreferences] ([UserId]);

CREATE UNIQUE INDEX [IX_UserPreferences_UserId_PreferenceKey] ON [UserPreferences] ([UserId], [PreferenceKey]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20251019130032_AddSystemSettingsTables', N'9.0.9');

ALTER TABLE [IcumManifestItems] ADD [HouseBl] nvarchar(max) NULL;

ALTER TABLE [ContainerCompletenessStatuses] ADD [HasImageData] bit NOT NULL DEFAULT CAST(0 AS bit);

ALTER TABLE [ContainerCompletenessStatuses] ADD [HasScannerData] bit NOT NULL DEFAULT CAST(0 AS bit);

ALTER TABLE [ContainerCompletenessStatuses] ADD [ICUMSDataCompleteness] int NOT NULL DEFAULT 0;

ALTER TABLE [ContainerCompletenessStatuses] ADD [ImageDataCompleteness] int NOT NULL DEFAULT 0;

ALTER TABLE [ContainerCompletenessStatuses] ADD [OverallCompleteness] int NOT NULL DEFAULT 0;

ALTER TABLE [ContainerCompletenessStatuses] ADD [ScannerDataCompleteness] int NOT NULL DEFAULT 0;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20251023171136_AddPreComputedCompletenessFields', N'9.0.9');

ALTER TABLE [Users] ADD [Department] nvarchar(100) NULL;

ALTER TABLE [Users] ADD [PhoneNumber] nvarchar(20) NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20251026002706_AddDepartmentAndPhoneToUser', N'9.0.9');

COMMIT;
GO

