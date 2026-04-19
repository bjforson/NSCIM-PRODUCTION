-- Create BusinessRules table
-- Migration: 20260104192318_AddBusinessRulesTable
-- Execute against NS_CIS database

USE [NS_CIS];
GO

-- Check if table already exists
IF OBJECT_ID(N'[dbo].[BusinessRules]', N'U') IS NOT NULL
BEGIN
    PRINT 'Table BusinessRules already exists. Skipping creation.';
END
ELSE
BEGIN
    PRINT 'Creating BusinessRules table...';
    
    CREATE TABLE [dbo].[BusinessRules] (
        [Id] int NOT NULL IDENTITY(1,1),
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NOT NULL,
        [Category] nvarchar(100) NOT NULL,
        [Priority] nvarchar(20) NOT NULL,
        [ConditionExpression] nvarchar(2000) NOT NULL,
        [ActionType] nvarchar(50) NOT NULL,
        [ActionMessage] nvarchar(1000) NOT NULL,
        [IsActive] bit NOT NULL,
        [ExecutionOrder] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_BusinessRules] PRIMARY KEY ([Id])
    );
    
    -- Create indexes
    CREATE INDEX [IX_BusinessRules_Category] ON [dbo].[BusinessRules] ([Category]);
    CREATE INDEX [IX_BusinessRules_Priority] ON [dbo].[BusinessRules] ([Priority]);
    CREATE INDEX [IX_BusinessRules_IsActive] ON [dbo].[BusinessRules] ([IsActive]);
    CREATE INDEX [IX_BusinessRules_ExecutionOrder] ON [dbo].[BusinessRules] ([ExecutionOrder]);
    CREATE INDEX [IX_BusinessRules_IsActive_ExecutionOrder] ON [dbo].[BusinessRules] ([IsActive], [ExecutionOrder]);
    
    PRINT 'BusinessRules table created successfully.';
END
GO

-- Mark migration as applied in EF Core migration history
IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260104192318_AddBusinessRulesTable')
    BEGIN
        INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
        VALUES (N'20260104192318_AddBusinessRulesTable', N'9.0.9');
        PRINT 'Migration marked as applied in __EFMigrationsHistory.';
    END
    ELSE
    BEGIN
        PRINT 'Migration already exists in __EFMigrationsHistory.';
    END
END
ELSE
BEGIN
    PRINT 'Warning: __EFMigrationsHistory table not found. Migration will not be marked as applied.';
END
GO

PRINT 'BusinessRules table migration completed.';
GO

