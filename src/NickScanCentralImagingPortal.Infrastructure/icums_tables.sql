BEGIN TRANSACTION;
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

COMMIT;
GO

