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
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251008102301_AddVehicleImportTable'
)
BEGIN
    CREATE TABLE [VehicleImports] (
        [Id] int NOT NULL IDENTITY,
        [VIN] nvarchar(17) NOT NULL,
        [BOEDocumentId] int NOT NULL,
        [DeclarationNumber] nvarchar(50) NULL,
        [ChassisNumber] nvarchar(50) NULL,
        [VehicleType] nvarchar(200) NULL,
        [Make] nvarchar(100) NULL,
        [Model] nvarchar(100) NULL,
        [VehicleYear] nvarchar(10) NULL,
        [EngineCapacity] nvarchar(20) NULL,
        [Weight] decimal(18,2) NULL,
        [Quantity] int NOT NULL,
        [HSCode] nvarchar(20) NULL,
        [CountryOfOrigin] nvarchar(10) NULL,
        [FOBValue] decimal(18,2) NULL,
        [FOBCurrency] nvarchar(10) NULL,
        [DutyPaid] decimal(18,2) NULL,
        [ImporterName] nvarchar(500) NULL,
        [ShipperName] nvarchar(500) NULL,
        [ConsigneeName] nvarchar(500) NULL,
        [BLNumber] nvarchar(100) NULL,
        [HouseBL] nvarchar(100) NULL,
        [RotationNumber] nvarchar(50) NULL,
        [ClearanceType] nvarchar(10) NULL,
        [CrmsLevel] nvarchar(20) NULL,
        [ProcessingStatus] nvarchar(50) NOT NULL,
        [ErrorMessage] nvarchar(1000) NULL,
        [Remarks] nvarchar(2000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [ProcessedAt] datetime2 NULL,
        [ImportType] int NOT NULL,
        [ContainerNumber] nvarchar(20) NULL,
        CONSTRAINT [PK_VehicleImports] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_VehicleImports_BOEDocuments_BOEDocumentId] FOREIGN KEY ([BOEDocumentId]) REFERENCES [BOEDocuments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251008102301_AddVehicleImportTable'
)
BEGIN
    CREATE INDEX [IX_VehicleImports_BOEDocumentId] ON [VehicleImports] ([BOEDocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251008102301_AddVehicleImportTable'
)
BEGIN
    CREATE INDEX [IX_VehicleImports_ChassisNumber] ON [VehicleImports] ([ChassisNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251008102301_AddVehicleImportTable'
)
BEGIN
    CREATE INDEX [IX_VehicleImports_CreatedAt] ON [VehicleImports] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251008102301_AddVehicleImportTable'
)
BEGIN
    CREATE INDEX [IX_VehicleImports_DeclarationNumber] ON [VehicleImports] ([DeclarationNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251008102301_AddVehicleImportTable'
)
BEGIN
    CREATE INDEX [IX_VehicleImports_ImportType] ON [VehicleImports] ([ImportType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251008102301_AddVehicleImportTable'
)
BEGIN
    CREATE INDEX [IX_VehicleImports_ProcessingStatus] ON [VehicleImports] ([ProcessingStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251008102301_AddVehicleImportTable'
)
BEGIN
    CREATE UNIQUE INDEX [IX_VehicleImports_VIN] ON [VehicleImports] ([VIN]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251008102301_AddVehicleImportTable'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251008102301_AddVehicleImportTable', N'9.0.9');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251009175300_AddICUMSDownloadQueue'
)
BEGIN
    CREATE TABLE [ICUMSDownloadQueue] (
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
        CONSTRAINT [PK_ICUMSDownloadQueue] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251009175300_AddICUMSDownloadQueue'
)
BEGIN
    CREATE INDEX [IX_ICUMSDownloadQueue_CompletedAt] ON [ICUMSDownloadQueue] ([CompletedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251009175300_AddICUMSDownloadQueue'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ICUMSDownloadQueue_ContainerNumber] ON [ICUMSDownloadQueue] ([ContainerNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251009175300_AddICUMSDownloadQueue'
)
BEGIN
    CREATE INDEX [IX_ICUMSDownloadQueue_QueuedAt] ON [ICUMSDownloadQueue] ([QueuedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251009175300_AddICUMSDownloadQueue'
)
BEGIN
    CREATE INDEX [IX_ICUMSDownloadQueue_Status] ON [ICUMSDownloadQueue] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251009175300_AddICUMSDownloadQueue'
)
BEGIN
    CREATE INDEX [IX_ICUMSDownloadQueue_StatusPriorityQueued] ON [ICUMSDownloadQueue] ([Status], [Priority], [QueuedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251009175300_AddICUMSDownloadQueue'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251009175300_AddICUMSDownloadQueue', N'9.0.9');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE TABLE [CMRRedownloadQueue] (
        [Id] int NOT NULL IDENTITY,
        [ContainerNumber] nvarchar(50) NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [QueuedAt] datetime2 NOT NULL,
        [ProcessedAt] datetime2 NULL,
        [ErrorMessage] nvarchar(1000) NULL,
        [RetryCount] int NOT NULL,
        [MaxRetries] int NOT NULL,
        [ProcessedBy] nvarchar(100) NULL,
        [Priority] nvarchar(50) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [OriginalDeclarationNumber] nvarchar(100) NULL,
        [OriginalClearanceType] nvarchar(20) NULL,
        CONSTRAINT [PK_CMRRedownloadQueue] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE TABLE [DownloadedFiles] (
        [Id] int NOT NULL IDENTITY,
        [FileName] nvarchar(500) NOT NULL,
        [FilePath] nvarchar(1000) NOT NULL,
        [FileSize] bigint NOT NULL,
        [DownloadDate] datetime2 NOT NULL,
        [ProcessedDate] datetime2 NULL,
        [ProcessingStatus] nvarchar(50) NOT NULL,
        [ErrorMessage] nvarchar(4000) NULL,
        [RecordCount] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_DownloadedFiles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE TABLE [ICUMSDownloadQueue] (
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
        CONSTRAINT [PK_ICUMSDownloadQueue] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE TABLE [BOEDocuments] (
        [Id] int NOT NULL IDENTITY,
        [DownloadedFileId] int NOT NULL,
        [DocumentIndex] int NOT NULL,
        [ContainerNumber] nvarchar(50) NOT NULL,
        [ContainerDescription] nvarchar(4000) NULL,
        [ContainerISO] nvarchar(20) NULL,
        [ContainerQuantity] int NULL,
        [ContainerWeight] decimal(18,2) NULL,
        [ImpName] nvarchar(500) NULL,
        [TotalDutyPaid] decimal(18,2) NULL,
        [CrmsLevel] nvarchar(50) NULL,
        [ExpAddress] nvarchar(4000) NULL,
        [DeclarationNumber] nvarchar(100) NULL,
        [RegimeCode] nvarchar(20) NULL,
        [NoOfContainers] int NULL,
        [CompOffRemarks] nvarchar(4000) NULL,
        [DeclarantName] nvarchar(500) NULL,
        [ExpName] nvarchar(500) NULL,
        [ImpAddress] nvarchar(4000) NULL,
        [ImpExpName] nvarchar(500) NULL,
        [CcvrIntelRemarks] nvarchar(4000) NULL,
        [DeclarationVersion] int NULL,
        [ImpExpAddress] nvarchar(4000) NULL,
        [DeclarationDate] nvarchar(50) NULL,
        [ClearanceType] nvarchar(20) NULL,
        [DeclarantAddress] nvarchar(4000) NULL,
        [RotationNumber] nvarchar(100) NULL,
        [ConsigneeName] nvarchar(500) NULL,
        [CountryOfOrigin] nvarchar(100) NULL,
        [MarksNumbers] nvarchar(4000) NULL,
        [ShipperName] nvarchar(500) NULL,
        [ShipperAddress] nvarchar(4000) NULL,
        [BlNumber] nvarchar(100) NULL,
        [DeliveryPlace] nvarchar(200) NULL,
        [HouseBl] nvarchar(100) NULL,
        [ConsigneeAddress] nvarchar(4000) NULL,
        [GoodsDescription] nvarchar(4000) NULL,
        [ProcessedAt] datetime2 NULL,
        [ProcessingStatus] nvarchar(50) NOT NULL,
        [ErrorMessage] nvarchar(4000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_BOEDocuments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BOEDocuments_DownloadedFiles_DownloadedFileId] FOREIGN KEY ([DownloadedFileId]) REFERENCES [DownloadedFiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE TABLE [IngestionLogs] (
        [Id] int NOT NULL IDENTITY,
        [DownloadedFileId] int NOT NULL,
        [ProcessType] nvarchar(50) NOT NULL,
        [Status] nvarchar(50) NOT NULL,
        [StartTime] datetime2 NOT NULL,
        [EndTime] datetime2 NULL,
        [RecordsProcessed] int NULL,
        [ErrorMessage] nvarchar(4000) NULL,
        [Details] nvarchar(4000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_IngestionLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_IngestionLogs_DownloadedFiles_DownloadedFileId] FOREIGN KEY ([DownloadedFileId]) REFERENCES [DownloadedFiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE TABLE [ManifestItems] (
        [Id] int NOT NULL IDENTITY,
        [BOEDocumentId] int NOT NULL,
        [ItemIndex] int NOT NULL,
        [HsCode] nvarchar(20) NULL,
        [Description] nvarchar(4000) NULL,
        [Quantity] decimal(18,2) NULL,
        [Unit] nvarchar(50) NULL,
        [Weight] decimal(18,2) NULL,
        [ItemFob] decimal(18,2) NULL,
        [ItemDutyPaid] decimal(18,2) NULL,
        [FobCurrency] nvarchar(10) NULL,
        [CountryOfOrigin] nvarchar(100) NULL,
        [ItemNo] int NULL,
        [Cpc] nvarchar(50) NULL,
        [ProcessedAt] datetime2 NULL,
        [ProcessingStatus] nvarchar(50) NOT NULL,
        [ErrorMessage] nvarchar(4000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ManifestItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ManifestItems_BOEDocuments_BOEDocumentId] FOREIGN KEY ([BOEDocumentId]) REFERENCES [BOEDocuments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE TABLE [VehicleImports] (
        [Id] int NOT NULL IDENTITY,
        [VIN] nvarchar(17) NOT NULL,
        [BOEDocumentId] int NOT NULL,
        [DeclarationNumber] nvarchar(50) NULL,
        [ChassisNumber] nvarchar(50) NULL,
        [VehicleType] nvarchar(200) NULL,
        [Make] nvarchar(100) NULL,
        [Model] nvarchar(100) NULL,
        [VehicleYear] nvarchar(10) NULL,
        [EngineCapacity] nvarchar(20) NULL,
        [Weight] decimal(18,2) NULL,
        [Quantity] int NOT NULL,
        [HSCode] nvarchar(20) NULL,
        [CountryOfOrigin] nvarchar(10) NULL,
        [FOBValue] decimal(18,2) NULL,
        [FOBCurrency] nvarchar(10) NULL,
        [DutyPaid] decimal(18,2) NULL,
        [ImporterName] nvarchar(500) NULL,
        [ShipperName] nvarchar(500) NULL,
        [ConsigneeName] nvarchar(500) NULL,
        [BLNumber] nvarchar(100) NULL,
        [HouseBL] nvarchar(100) NULL,
        [RotationNumber] nvarchar(50) NULL,
        [ClearanceType] nvarchar(10) NULL,
        [CrmsLevel] nvarchar(20) NULL,
        [ProcessingStatus] nvarchar(50) NOT NULL,
        [ErrorMessage] nvarchar(1000) NULL,
        [Remarks] nvarchar(2000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [ProcessedAt] datetime2 NULL,
        [ImportType] int NOT NULL,
        [ContainerNumber] nvarchar(20) NULL,
        CONSTRAINT [PK_VehicleImports] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_VehicleImports_BOEDocuments_BOEDocumentId] FOREIGN KEY ([BOEDocumentId]) REFERENCES [BOEDocuments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_BOEDocument_ContainerNumber_DeclarationNumber_Unique] ON [BOEDocuments] ([ContainerNumber], [DeclarationNumber]) WHERE [DeclarationNumber] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_BOEDocuments_ContainerNumber] ON [BOEDocuments] ([ContainerNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_BOEDocuments_DownloadedFileId] ON [BOEDocuments] ([DownloadedFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_BOEDocuments_ProcessingStatus] ON [BOEDocuments] ([ProcessingStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_CMRRedownloadQueue_ContainerNumber] ON [CMRRedownloadQueue] ([ContainerNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_CMRRedownloadQueue_ProcessedAt] ON [CMRRedownloadQueue] ([ProcessedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_CMRRedownloadQueue_QueuedAt] ON [CMRRedownloadQueue] ([QueuedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_CMRRedownloadQueue_Status] ON [CMRRedownloadQueue] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_CMRRedownloadQueue_StatusPriorityQueued] ON [CMRRedownloadQueue] ([Status], [Priority], [QueuedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_DownloadedFiles_DownloadDate] ON [DownloadedFiles] ([DownloadDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_DownloadedFiles_FileName] ON [DownloadedFiles] ([FileName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_DownloadedFiles_ProcessingStatus] ON [DownloadedFiles] ([ProcessingStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_ICUMSDownloadQueue_CompletedAt] ON [ICUMSDownloadQueue] ([CompletedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ICUMSDownloadQueue_ContainerNumber] ON [ICUMSDownloadQueue] ([ContainerNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_ICUMSDownloadQueue_QueuedAt] ON [ICUMSDownloadQueue] ([QueuedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_ICUMSDownloadQueue_Status] ON [ICUMSDownloadQueue] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_ICUMSDownloadQueue_StatusPriorityQueued] ON [ICUMSDownloadQueue] ([Status], [Priority], [QueuedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_IngestionLogs_DownloadedFileId] ON [IngestionLogs] ([DownloadedFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_IngestionLogs_ProcessType] ON [IngestionLogs] ([ProcessType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_IngestionLogs_Status] ON [IngestionLogs] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_ManifestItems_BOEDocumentId] ON [ManifestItems] ([BOEDocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_ManifestItems_HsCode] ON [ManifestItems] ([HsCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_ManifestItems_ProcessingStatus] ON [ManifestItems] ([ProcessingStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_VehicleImports_BOEDocumentId] ON [VehicleImports] ([BOEDocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_VehicleImports_ChassisNumber] ON [VehicleImports] ([ChassisNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_VehicleImports_CreatedAt] ON [VehicleImports] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_VehicleImports_DeclarationNumber] ON [VehicleImports] ([DeclarationNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_VehicleImports_ImportType] ON [VehicleImports] ([ImportType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE INDEX [IX_VehicleImports_ProcessingStatus] ON [VehicleImports] ([ProcessingStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    CREATE UNIQUE INDEX [IX_VehicleImports_VIN] ON [VehicleImports] ([VIN]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251019111646_AddCMRRedownloadQueue'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251019111646_AddCMRRedownloadQueue', N'9.0.9');
END;

COMMIT;
GO

