-- ============================================================================
-- Error Investigation System - Database Schema
-- Date: 2025-01-XX
-- Description: Creates tables for AI-powered error investigation and auto-fix system
-- ============================================================================

USE [NS_CIS]
GO

PRINT '========================================';
PRINT 'Creating Error Investigation System Tables';
PRINT '========================================';
PRINT '';

-- ============================================================================
-- Table: ErrorInvestigations
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ErrorInvestigations]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[ErrorInvestigations](
        [Id] [bigint] IDENTITY(1,1) NOT NULL,
        [InvestigationGroupId] [nvarchar](100) NOT NULL,
        [ErrorPattern] [nvarchar](500) NOT NULL,
        [ErrorCode] [nvarchar](50) NULL,
        [ServiceId] [nvarchar](200) NULL,
        [Operation] [nvarchar](200) NULL,
        [ExceptionType] [nvarchar](200) NULL,
        [OccurrenceCount] [int] NOT NULL DEFAULT 1,
        [FirstSeen] [datetime2](7) NOT NULL,
        [LastSeen] [datetime2](7) NOT NULL,
        [Status] [nvarchar](50) NOT NULL DEFAULT N'New',
        [Priority] [nvarchar](20) NOT NULL DEFAULT N'Medium',
        [InvestigationSummary] [nvarchar](max) NULL,
        [InvestigationDetails] [nvarchar](max) NULL,
        [RelatedLogIds] [nvarchar](max) NULL,
        [SampleErrorMessage] [nvarchar](max) NULL,
        [SampleStackTrace] [nvarchar](max) NULL,
        [HasProposedFix] [bit] NOT NULL DEFAULT 0,
        [ApprovedBy] [nvarchar](100) NULL,
        [ApprovedAt] [datetime2](7) NULL,
        [ApprovalNotes] [nvarchar](max) NULL,
        [FixBranchName] [nvarchar](200) NULL,
        [FixedAt] [datetime2](7) NULL,
        [IsVerified] [bit] NOT NULL DEFAULT 0,
        [VerifiedAt] [datetime2](7) NULL,
        [VerifiedBy] [nvarchar](100) NULL,
        [CreatedAt] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_ErrorInvestigations] PRIMARY KEY CLUSTERED ([Id] ASC)
    )
    
    PRINT '✅ ErrorInvestigations table created';
END
ELSE
BEGIN
    PRINT 'ℹ️ ErrorInvestigations table already exists - skipping';
END
GO

-- ============================================================================
-- Table: FixProposals
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[FixProposals]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[FixProposals](
        [Id] [bigint] IDENTITY(1,1) NOT NULL,
        [ErrorInvestigationId] [bigint] NOT NULL,
        [FixType] [nvarchar](50) NOT NULL DEFAULT N'CodeChange',
        [Title] [nvarchar](500) NOT NULL,
        [Description] [nvarchar](max) NOT NULL,
        [Rationale] [nvarchar](max) NULL,
        [ImpactAssessment] [nvarchar](max) NULL,
        [CodeChanges] [nvarchar](max) NULL,
        [ConfigurationChanges] [nvarchar](max) NULL,
        [AffectedFiles] [nvarchar](max) NULL,
        [RiskLevel] [nvarchar](20) NOT NULL DEFAULT N'Medium',
        [Status] [nvarchar](50) NOT NULL DEFAULT N'Proposed',
        [ApprovedBy] [nvarchar](100) NULL,
        [ApprovedAt] [datetime2](7) NULL,
        [ApprovalNotes] [nvarchar](max) NULL,
        [ImplementedAt] [datetime2](7) NULL,
        [BranchName] [nvarchar](200) NULL,
        [CommitHash] [nvarchar](100) NULL,
        [CreatedAt] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_FixProposals] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_FixProposals_ErrorInvestigations] FOREIGN KEY([ErrorInvestigationId])
            REFERENCES [dbo].[ErrorInvestigations] ([Id])
            ON DELETE CASCADE
    )
    
    PRINT '✅ FixProposals table created';
END
ELSE
BEGIN
    PRINT 'ℹ️ FixProposals table already exists - skipping';
END
GO

-- ============================================================================
-- Table: FixAuditLogs
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[FixAuditLogs]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[FixAuditLogs](
        [Id] [bigint] IDENTITY(1,1) NOT NULL,
        [ErrorInvestigationId] [bigint] NULL,
        [FixProposalId] [bigint] NULL,
        [ActionType] [nvarchar](50) NOT NULL,
        [PerformedBy] [nvarchar](100) NOT NULL DEFAULT N'System',
        [Description] [nvarchar](max) NOT NULL,
        [Details] [nvarchar](max) NULL,
        [IpAddress] [nvarchar](50) NULL,
        [UserAgent] [nvarchar](500) NULL,
        [CreatedAt] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_FixAuditLogs] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_FixAuditLogs_ErrorInvestigations] FOREIGN KEY([ErrorInvestigationId])
            REFERENCES [dbo].[ErrorInvestigations] ([Id])
            ON DELETE NO ACTION,
        CONSTRAINT [FK_FixAuditLogs_FixProposals] FOREIGN KEY([FixProposalId])
            REFERENCES [dbo].[FixProposals] ([Id])
            ON DELETE NO ACTION
    )
    
    PRINT '✅ FixAuditLogs table created';
END
ELSE
BEGIN
    PRINT 'ℹ️ FixAuditLogs table already exists - skipping';
END
GO

-- ============================================================================
-- Indexes for Performance
-- ============================================================================

-- ErrorInvestigations indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ErrorInvestigations]') AND name = N'IX_ErrorInvestigations_Status')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_ErrorInvestigations_Status] ON [dbo].[ErrorInvestigations]
    (
        [Status] ASC
    )
    PRINT '✅ Index IX_ErrorInvestigations_Status created';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ErrorInvestigations]') AND name = N'IX_ErrorInvestigations_GroupId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_ErrorInvestigations_GroupId] ON [dbo].[ErrorInvestigations]
    (
        [InvestigationGroupId] ASC
    )
    PRINT '✅ Index IX_ErrorInvestigations_GroupId created';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ErrorInvestigations]') AND name = N'IX_ErrorInvestigations_LastSeen')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_ErrorInvestigations_LastSeen] ON [dbo].[ErrorInvestigations]
    (
        [LastSeen] DESC
    )
    PRINT '✅ Index IX_ErrorInvestigations_LastSeen created';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ErrorInvestigations]') AND name = N'IX_ErrorInvestigations_Priority')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_ErrorInvestigations_Priority] ON [dbo].[ErrorInvestigations]
    (
        [Priority] ASC,
        [Status] ASC
    )
    PRINT '✅ Index IX_ErrorInvestigations_Priority created';
END
GO

-- FixProposals indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[FixProposals]') AND name = N'IX_FixProposals_ErrorInvestigationId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_FixProposals_ErrorInvestigationId] ON [dbo].[FixProposals]
    (
        [ErrorInvestigationId] ASC
    )
    PRINT '✅ Index IX_FixProposals_ErrorInvestigationId created';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[FixProposals]') AND name = N'IX_FixProposals_Status')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_FixProposals_Status] ON [dbo].[FixProposals]
    (
        [Status] ASC
    )
    PRINT '✅ Index IX_FixProposals_Status created';
END
GO

-- FixAuditLogs indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[FixAuditLogs]') AND name = N'IX_FixAuditLogs_ErrorInvestigationId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_FixAuditLogs_ErrorInvestigationId] ON [dbo].[FixAuditLogs]
    (
        [ErrorInvestigationId] ASC
    )
    PRINT '✅ Index IX_FixAuditLogs_ErrorInvestigationId created';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[FixAuditLogs]') AND name = N'IX_FixAuditLogs_CreatedAt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_FixAuditLogs_CreatedAt] ON [dbo].[FixAuditLogs]
    (
        [CreatedAt] DESC
    )
    PRINT '✅ Index IX_FixAuditLogs_CreatedAt created';
END
GO

PRINT '';
PRINT '========================================';
PRINT 'Error Investigation System Schema Complete!';
PRINT '========================================';
PRINT 'Tables Created:';
PRINT '  - ErrorInvestigations';
PRINT '  - FixProposals';
PRINT '  - FixAuditLogs';
PRINT 'Indexes Created: 8';
PRINT '';

