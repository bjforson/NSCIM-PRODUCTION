BEGIN TRANSACTION;
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

COMMIT;
GO

