using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Entities.ASE;
using NickScanCentralImagingPortal.Core.Entities.FS6000;
using NickScanCentralImagingPortal.Core.Entities.HR;
using NickScanCentralImagingPortal.Core.Entities.Review;
using NickScanCentralImagingPortal.Core.Entities.ShiftAttendance;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Infrastructure.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            // Allow database updates even when EF detects pending model changes (we manage schema via controlled migrations)
            optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        }

        // Main tables
        public DbSet<Container> Containers { get; set; }
        public DbSet<ContainerImage> ContainerImages { get; set; }
        public DbSet<ProcessingResult> ProcessingResults { get; set; }

        // Image Processing tables
        public DbSet<ImageCache> ImageCaches { get; set; }

        // Scanner-specific tables
        public DbSet<NuctechScannerData> NuctechScannerData { get; set; }
        public DbSet<HeimannSmithScannerData> HeimannSmithScannerData { get; set; }

        // Original scan audit records
        public DbSet<OriginalScanRecord> OriginalScanRecords { get; set; }

        // FS6000 tables
        public DbSet<FS6000Scan> FS6000Scans { get; set; }
        public DbSet<FS6000Image> FS6000Images { get; set; }
        public DbSet<FS6000SyncLog> FS6000SyncLogs { get; set; }
        public DbSet<FS6000FileProcessing> FS6000FileProcessings { get; set; }

        // ASE tables
        public DbSet<AseScan> AseScans { get; set; }
        public DbSet<AseSyncLog> AseSyncLogs { get; set; }

        // User Management tables
        public DbSet<User> Users { get; set; }

        // Role-Based Access Control tables
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<RolePermission> RolePermissions { get; set; }
        public DbSet<UserPermission> UserPermissions { get; set; }
        public DbSet<PermissionAuditLog> PermissionAuditLogs { get; set; }

        // Notifications tables
        public DbSet<SystemNotification> Notifications { get; set; }

        // Audit Logs
        public DbSet<AuditLog> AuditLogs { get; set; }

        // Image Analysis Decisions (Localized to Image Analysis feature)
        public DbSet<ImageAnalysisDecision> ImageAnalysisDecisions { get; set; }

        // Audit Decisions (Second-tier review/verification)
        public DbSet<AuditDecision> AuditDecisions { get; set; }

        // Image Analysis Service (new domain)
        public DbSet<AnalysisGroup> AnalysisGroups { get; set; }
        // Sprint 5G2 / Bridge B1 — append-only audit row written by AnalysisGroupStateMachine
        // on every successful analysisgroups.status transition. Backed by the
        // analysis_group_status_transitions table (deploy migration in tools/migrations/sprint-5G2/).
        public DbSet<AnalysisGroupStatusTransition> AnalysisGroupStatusTransitions
            => Set<AnalysisGroupStatusTransition>();
        public DbSet<AnalysisRecord> AnalysisRecords { get; set; }
        public DbSet<AnalysisAssignment> AnalysisAssignments { get; set; }
        public DbSet<AnalysisQueueEntry> AnalysisQueueEntries { get; set; }
        public DbSet<AnalysisSubmission> AnalysisSubmissions { get; set; }
        public DbSet<AnalysisSettings> AnalysisSettings { get; set; }
        public DbSet<AnalysisParentGroup> AnalysisParentGroups { get; set; }
        public DbSet<WavePendingContainer> WavePendingContainers { get; set; }
        public DbSet<DecisionAgentSettings> DecisionAgentSettings { get; set; }
        public DbSet<DecisionAgentCondition> DecisionAgentConditions { get; set; }
        public DbSet<DecisionAgentAuditLog> DecisionAgentAuditLogs { get; set; }
        public DbSet<UserReadiness> UserReadiness { get; set; }

        // Container Data Completeness Service tables
        public DbSet<ContainerCompletenessStatus> ContainerCompletenessStatuses { get; set; }

        // 1.14.0 — Record Completeness (proactive ICUMS-driven record state)
        public DbSet<RecordCompletenessStatus> RecordCompletenessStatuses { get; set; }
        public DbSet<RecordExpectedContainer> RecordExpectedContainers { get; set; }
        public DbSet<RecordReconciliationState> RecordReconciliationStates { get; set; }
        public DbSet<ManualBOERequest> ManualBOERequests { get; set; }
        public DbSet<ContainerBOERelation> ContainerBOERelations { get; set; }
        public DbSet<CrossRecordScan> CrossRecordScans { get; set; }
        public DbSet<ContainerScanQueue> ContainerScanQueues { get; set; }

        // ICUMS Queue Management tables
        public DbSet<ICUMSDownloadQueue> ICUMSDownloadQueues { get; set; }
        public DbSet<ICUMSSubmissionQueue> ICUMSSubmissionQueues { get; set; }

        // Enhanced Container Validation tables
        public DbSet<ContainerAnnotation> ContainerAnnotations { get; set; }

        // BL Review tables
        public DbSet<BLReviewRecord> BLReviewRecords { get; set; }
        public DbSet<ContainerReviewDecision> ContainerReviewDecisions { get; set; }

        // Settings Management tables
        public DbSet<SystemSetting> SystemSettings { get; set; }
        public DbSet<SettingsHistory> SettingsHistories { get; set; }
        public DbSet<UserPreference> UserPreferences { get; set; }

        // Business Rules table
        public DbSet<BusinessRule> BusinessRules { get; set; }

        // Error Investigation System tables
        public DbSet<ErrorInvestigation> ErrorInvestigations { get; set; }
        public DbSet<FixProposal> FixProposals { get; set; }
        public DbSet<FixAuditLog> FixAuditLogs { get; set; }

        // Assistive AI / training lineage (Phase 0+)
        public DbSet<AiImageAnalysisSuggestion> AiImageAnalysisSuggestions { get; set; }
        public DbSet<AiDatasetSnapshot> AiDatasetSnapshots { get; set; }

        // Controlled-vocabulary finding categories (AI training flywheel — Gap 1a)
        public DbSet<ThreatCategory> ThreatCategories { get; set; }
        public DbSet<RevenueAnomalyCategory> RevenueAnomalyCategories { get; set; }

        // Manifest snapshot at decision time (AI training flywheel — Gap 0)
        public DbSet<ManifestSnapshot> ManifestSnapshots { get; set; }

        // Match quality flags (Match Correction Tool — replaces log-only anomaly tracking)
        public DbSet<MatchQualityFlag> MatchQualityFlags { get; set; }

        // Dashboard alerts (Sprint 5G3 / audit 8.25 — persist + email on-call when
        // critical alerts fire, instead of broadcasting to SignalR only).
        public DbSet<DashboardAlertEntity> DashboardAlerts { get; set; }

        // Per-image audit verdicts (deferred plan request 1 — child of AuditDecision)
        public DbSet<AuditImageDecision> AuditImageDecisions { get; set; }

        // (Vestigial IcumContainerData / IcumManifestItems DbSets removed on main —
        // see c8b9beb3. The Gap 0 ManifestSnapshotService reads from
        // IcumDownloadsDbContext.BOEDocuments / ManifestItems instead.)

        // HR & Corporate Management tables
        public DbSet<Organization> Organizations { get; set; }
        public DbSet<OrgUnit> OrgUnits { get; set; }
        public DbSet<Site> Sites { get; set; }
        public DbSet<Lane> Lanes { get; set; }
        public DbSet<ScannerAsset> ScannerAssets { get; set; }
        public DbSet<Employee> Employees { get; set; }
        public DbSet<Position> Positions { get; set; }
        public DbSet<EmployeePosition> EmployeePositions { get; set; }

        // Shift & Attendance tables
        public DbSet<ShiftTemplate> ShiftTemplates { get; set; }
        public DbSet<ShiftAssignment> ShiftAssignments { get; set; }
        public DbSet<AttendanceRecord> AttendanceRecords { get; set; }
        public DbSet<ShiftCoverageRequirement> ShiftCoverageRequirements { get; set; }
        public DbSet<LeaveRequest> LeaveRequests { get; set; }
        public DbSet<ShiftSwapRequest> ShiftSwapRequests { get; set; }

        // Endpoint Usage Monitoring tables
        public DbSet<EndpointUsageLog> EndpointUsageLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // AnalysisGroup configuration
            modelBuilder.Entity<AnalysisGroup>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.GroupIdentifier).IsRequired().HasMaxLength(150);
                entity.Property(e => e.NormalizedGroupIdentifier).HasMaxLength(150);
                entity.Property(e => e.GroupType).HasMaxLength(50);
                entity.Property(e => e.ScannerType).HasMaxLength(20);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
                // ✅ FIX: Changed from unique index on GroupIdentifier only to composite unique index on (GroupIdentifier, ScannerType)
                // This allows separate groups for same GroupIdentifier with different ScannerTypes (prevents mixing)
                entity.HasIndex(e => new { e.GroupIdentifier, e.ScannerType }).IsUnique();
                // Keep non-unique index on GroupIdentifier for performance
                entity.HasIndex(e => e.GroupIdentifier);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => new { e.Status, e.Priority });
                entity.HasIndex(e => new { e.NormalizedGroupIdentifier, e.ScannerType }); // For joins with ContainerCompletenessStatus
                // Wave Processing fields
                entity.Property(e => e.WaveCreatedReason).HasMaxLength(50);
                entity.HasIndex(e => e.ParentGroupId);

                // 1.15.0 — Record completeness linkage (new canonical parent)
                entity.HasIndex(e => e.RecordCompletenessStatusId);
            });

            // AnalysisRecord configuration
            modelBuilder.Entity<AnalysisRecord>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ContainerNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ScannerType).HasMaxLength(20);
                entity.Property(e => e.ImageUrl).HasMaxLength(500);
                entity.Property(e => e.MetadataRef).HasMaxLength(200);
                entity.Property(e => e.CompletenessRef).HasMaxLength(200);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
                entity.HasIndex(e => new { e.GroupId, e.ContainerNumber }).IsUnique();
                entity.HasIndex(e => e.Status);
                entity.Property(e => e.SplitPosition).HasMaxLength(10);
                entity.Property(e => e.SplitStatus).HasMaxLength(20);
                entity.HasIndex(e => e.SplitJobId).HasFilter("\"SplitJobId\" IS NOT NULL");
                entity.HasIndex(e => e.SplitStatus).HasFilter("\"SplitStatus\" IS NOT NULL");
            });

            // AnalysisAssignment configuration
            modelBuilder.Entity<AnalysisAssignment>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.AssignedTo).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Role).IsRequired().HasMaxLength(20);
                entity.Property(e => e.State).IsRequired().HasMaxLength(20);
                entity.HasIndex(e => new { e.GroupId, e.Role, e.State });
                entity.HasIndex(e => e.AssignedTo);
            });

            // AnalysisQueueEntry — materialized assignment queue for fast GetMyAssignments
            modelBuilder.Entity<AnalysisQueueEntry>(entity =>
            {
                entity.HasKey(e => e.AssignmentId);
                entity.Property(e => e.AssignedTo).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Role).IsRequired().HasMaxLength(20);
                entity.Property(e => e.GroupIdentifier).IsRequired().HasMaxLength(150);
                entity.Property(e => e.GroupStatus).IsRequired().HasMaxLength(30);
                entity.Property(e => e.ContainersJson).HasColumnType("text");

                // Phase-1 tenancy retrofit (Sprint 5G1 / audit 7.02). Column added
                // by tools/migrations/sprint-5G1/02-aqe-tenant-id-rls.sql with a
                // DB-level DEFAULT that pulls from app.tenant_id, so existing
                // INSERT paths (raw-SQL ExecuteSqlInterpolatedAsync in
                // ReadyGroupsCacheService.UpsertQueueEntryAsync) continue to work
                // without modification — the default kicks in for the omitted column.
                entity.Property(e => e.TenantId).HasColumnName("tenant_id");

                entity.HasIndex(e => new { e.AssignedTo, e.Role })
                      .HasDatabaseName("ix_analysisqueueentries_assignedto_role");
                entity.HasIndex(e => e.GroupId)
                      .HasDatabaseName("ix_analysisqueueentries_groupid");
                entity.HasIndex(e => new { e.TenantId, e.AssignmentId })
                      .HasDatabaseName("ix_analysisqueueentries_tenant_id");
            });

            // AnalysisSubmission configuration
            modelBuilder.Entity<AnalysisSubmission>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.PayloadPath).HasMaxLength(500);
                entity.Property(e => e.PayloadHash).HasMaxLength(128);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
                entity.Property(e => e.LastError).HasMaxLength(1000);
                entity.HasIndex(e => new { e.GroupId, e.Status });
            });

            // AnalysisSettings configuration
            modelBuilder.Entity<AnalysisSettings>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            // 1.14.0 — RecordCompletenessStatus configuration
            modelBuilder.Entity<RecordCompletenessStatus>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.DeclarationNumber).IsRequired().HasMaxLength(100);
                entity.Property(e => e.ClearanceType).HasMaxLength(20);
                entity.Property(e => e.RegimeCode).HasMaxLength(20);
                entity.Property(e => e.RotationNumber).HasMaxLength(100);
                entity.Property(e => e.BlNumber).HasMaxLength(100);
                entity.Property(e => e.ContainerGroupKey).HasMaxLength(150);
                entity.Property(e => e.ScannerType).HasMaxLength(20);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
                entity.Property(e => e.WorkflowStage).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ArchivalReason).HasMaxLength(50);
                entity.Property(e => e.DeclarationsJson).HasColumnType("jsonb");

                // Note: the full unique index on (declarationnumber, COALESCE(scannertype,''))
                // is created via raw SQL in the migration script because EF can't express the
                // COALESCE expression. We keep a non-unique composite index here for query perf.
                entity.HasIndex(e => new { e.DeclarationNumber, e.ScannerType });
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.WorkflowStage);
                entity.HasIndex(e => e.ContainerGroupKey);
                entity.HasIndex(e => e.LastNewContainerAtUtc);
            });

            // 1.14.0 — RecordExpectedContainer configuration
            modelBuilder.Entity<RecordExpectedContainer>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ContainerNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
                entity.Property(e => e.HouseBl).HasMaxLength(100);
                entity.Property(e => e.ConsigneeName).HasMaxLength(500);
                entity.Property(e => e.InspectionId).HasMaxLength(50);
                entity.Property(e => e.ScannerType).HasMaxLength(20);

                entity.HasIndex(e => new { e.RecordId, e.ContainerNumber }).IsUnique();
                entity.HasIndex(e => e.ContainerNumber);
                entity.HasIndex(e => e.Status);

                entity.HasOne(e => e.Record)
                      .WithMany(r => r.ExpectedContainers)
                      .HasForeignKey(e => e.RecordId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // AnalysisParentGroup configuration
            modelBuilder.Entity<AnalysisParentGroup>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.GroupIdentifier).IsRequired().HasMaxLength(150);
                entity.Property(e => e.ScannerType).HasMaxLength(20);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
                entity.HasIndex(e => e.Status);
            });

            // WavePendingContainer configuration
            modelBuilder.Entity<WavePendingContainer>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ContainerNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ScannerType).HasMaxLength(20);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
                entity.HasIndex(e => e.ParentGroupId);

                // Wave #1 (1.11.0): declare the ParentGroupId relationship to
                // AnalysisParentGroup so EF scaffolds a real foreign key.
                // Historically this was a bare Guid column with no FK, which
                // meant WavePendingContainer rows could be orphaned if their
                // parent was deleted. Cascade-delete is the right behaviour
                // because the whole point of a pending container is that it's
                // subordinate state belonging to exactly one parent group.
                entity.HasOne<NickScanCentralImagingPortal.Core.Entities.Analysis.AnalysisParentGroup>()
                      .WithMany()
                      .HasForeignKey(e => e.ParentGroupId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // UserReadiness configuration
            modelBuilder.Entity<UserReadiness>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Role).IsRequired().HasMaxLength(20);
                entity.Property(e => e.ChangedBy).HasMaxLength(50);
                entity.Property(e => e.SessionId).HasMaxLength(100);

                // Unique constraint: one readiness record per user per role
                entity.HasIndex(e => new { e.Username, e.Role }).IsUnique();

                // Index for efficient querying: role + readiness + heartbeat
                entity.HasIndex(e => new { e.Role, e.IsReady, e.LastHeartbeat })
                    .HasDatabaseName("IX_UserReadiness_Role_Ready_Heartbeat");

                entity.HasIndex(e => e.Username);
                entity.HasIndex(e => e.LastHeartbeat);
            });

            // ImageCache configuration
            modelBuilder.Entity<ImageCache>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ContainerNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ScannerType).IsRequired().HasMaxLength(20);
                entity.Property(e => e.ImageData).IsRequired();
                entity.Property(e => e.MimeType).HasMaxLength(50).HasDefaultValue("image/jpeg");
                entity.Property(e => e.ProcessingPipeline).HasMaxLength(100);
                entity.Property(e => e.Quality).HasMaxLength(50).HasDefaultValue("High");

                entity.HasIndex(e => e.ContainerNumber);
                entity.HasIndex(e => e.ScannerType);
                entity.HasIndex(e => new { e.ContainerNumber, e.ScannerType });
            });

            // User configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Email).IsRequired().HasMaxLength(100);
                entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(100);
                entity.Property(e => e.FirstName).HasMaxLength(50);
                entity.Property(e => e.LastName).HasMaxLength(50);
                entity.Property(e => e.UserNumber).HasMaxLength(20); // ✅ UserNumber for anonymized reporting
                entity.Property(e => e.CreatedBy).HasMaxLength(50);
                entity.Property(e => e.UpdatedBy).HasMaxLength(50);

                entity.HasIndex(e => e.Username).IsUnique();
                entity.HasIndex(e => e.Email).IsUnique();
                entity.HasIndex(e => e.UserNumber).IsUnique(); // ✅ Unique index for UserNumber
                entity.HasIndex(e => e.IsActive);
                entity.HasIndex(e => e.RoleId);

                // Relationship to Role
                entity.HasOne(e => e.AssignedRole)
                    .WithMany(r => r.Users)
                    .HasForeignKey(e => e.RoleId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // Permission configuration
            modelBuilder.Entity<Permission>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Description).HasMaxLength(500);
                entity.Property(e => e.Category).HasMaxLength(50);

                entity.HasIndex(e => e.Name).IsUnique();
                entity.HasIndex(e => e.Category);
                entity.HasIndex(e => e.IsActive);
            });

            // Role configuration
            modelBuilder.Entity<Role>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Description).HasMaxLength(500);
                entity.Property(e => e.CreatedBy).HasMaxLength(50);
                entity.Property(e => e.UpdatedBy).HasMaxLength(50);

                entity.HasIndex(e => e.Name).IsUnique();
                entity.HasIndex(e => e.IsSystemRole);
                entity.HasIndex(e => e.IsActive);
            });

            // RolePermission configuration
            modelBuilder.Entity<RolePermission>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.GrantedBy).HasMaxLength(50);

                entity.HasIndex(e => new { e.RoleId, e.PermissionId }).IsUnique();
                entity.HasIndex(e => e.RoleId);
                entity.HasIndex(e => e.PermissionId);

                // Relationships
                entity.HasOne(e => e.Role)
                    .WithMany(r => r.RolePermissions)
                    .HasForeignKey(e => e.RoleId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Permission)
                    .WithMany(p => p.RolePermissions)
                    .HasForeignKey(e => e.PermissionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // UserPermission configuration
            modelBuilder.Entity<UserPermission>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.GrantedBy).HasMaxLength(50);
                entity.Property(e => e.Reason).HasMaxLength(500);

                entity.HasIndex(e => new { e.UserId, e.PermissionId }).IsUnique();
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.PermissionId);
                entity.HasIndex(e => e.ExpiresAt);
                entity.HasIndex(e => e.IsGranted);

                // Relationships
                entity.HasOne(e => e.User)
                    .WithMany(u => u.UserPermissions)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Permission)
                    .WithMany(p => p.UserPermissions)
                    .HasForeignKey(e => e.PermissionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // PermissionAuditLog configuration
            modelBuilder.Entity<PermissionAuditLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Action).IsRequired().HasMaxLength(50);
                entity.Property(e => e.EntityType).IsRequired().HasMaxLength(50);
                entity.Property(e => e.IPAddress).HasMaxLength(50);
                entity.Property(e => e.Details).HasMaxLength(4000);

                entity.HasIndex(e => e.Action);
                entity.HasIndex(e => e.EntityType);
                entity.HasIndex(e => e.EntityId);
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.Result);

                // Relationships (optional, can be null)
                entity.HasOne(e => e.Permission)
                    .WithMany()
                    .HasForeignKey(e => e.PermissionId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.Role)
                    .WithMany()
                    .HasForeignKey(e => e.RoleId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // ContainerCompletenessStatus configuration
            modelBuilder.Entity<ContainerCompletenessStatus>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ContainerNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ScannerType).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
                entity.Property(e => e.ErrorMessage).HasMaxLength(1000);

                entity.HasIndex(e => e.ContainerNumber);
                entity.HasIndex(e => e.ScannerType);
                entity.HasIndex(e => new { e.ContainerNumber, e.ScannerType });
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.HasICUMSData);
                // Index for COUNT queries: Status LIKE 'Complete%' AND WorkflowStage IN ('', 'Pending', 'ImageAnalysis')
                entity.HasIndex(e => new { e.Status, e.WorkflowStage }).HasDatabaseName("IX_ContainerCompletenessStatuses_Status_WorkflowStage");
            });

            // SystemNotification configuration - index for unread count queries
            modelBuilder.Entity<SystemNotification>(entity =>
            {
                entity.HasIndex(e => new { e.IsRead, e.TargetUser, e.TargetRole })
                    .HasDatabaseName("IX_Notifications_IsRead_TargetUser_TargetRole");
            });

            // ManualBOERequest configuration
            modelBuilder.Entity<ManualBOERequest>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ContainerNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
                entity.Property(e => e.ICUMSResponseId).HasMaxLength(100);
                entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
                entity.Property(e => e.RequestedBy).HasMaxLength(50);

                entity.HasIndex(e => e.ContainerNumber);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.RequestDate);
                entity.HasIndex(e => e.NextRetryAt);
            });

            // ContainerBOERelation configuration
            modelBuilder.Entity<ContainerBOERelation>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ContainerNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ScannerType).IsRequired().HasMaxLength(20);
                entity.Property(e => e.RelationType).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Notes).HasMaxLength(500);

                entity.HasIndex(e => e.ContainerNumber);
                entity.HasIndex(e => e.ScannerType);
                entity.HasIndex(e => e.ICUMSBOEId);
                entity.HasIndex(e => e.IsActive);
            });

            // ICUMSDownloadQueue configuration
            modelBuilder.Entity<ICUMSDownloadQueue>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ContainerNumber).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
                entity.Property(e => e.LastErrorMessage).HasMaxLength(1000);
                entity.Property(e => e.LastErrorCode).HasMaxLength(50);
                entity.Property(e => e.RequestedBy).HasMaxLength(100);
                entity.Property(e => e.RequestSource).HasMaxLength(50);
                entity.Property(e => e.Metadata).HasMaxLength(2000);

                entity.HasIndex(e => e.ContainerNumber);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.Priority);
                entity.HasIndex(e => e.QueuedAt);
                entity.HasIndex(e => new { e.ContainerNumber, e.Status });
            });

            // ICUMSSubmissionQueue configuration
            modelBuilder.Entity<ICUMSSubmissionQueue>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ContainerNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ScannerType).IsRequired().HasMaxLength(20);
                entity.Property(e => e.ImagePaths).IsRequired();
                entity.Property(e => e.ReportData).IsRequired();
                entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
                entity.Property(e => e.ICUMSResponseId).HasMaxLength(100);
                entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
                entity.Property(e => e.SubmittedBy).HasMaxLength(50);

                entity.HasIndex(e => e.ContainerNumber);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.Priority);
                entity.HasIndex(e => e.NextRetryAt);
            });

            // ContainerAnnotation configuration
            modelBuilder.Entity<ContainerAnnotation>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ContainerNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Type).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Color).HasMaxLength(20).HasDefaultValue("#ff0000");
                entity.Property(e => e.Width).HasDefaultValue(2);
                entity.Property(e => e.Text).HasMaxLength(1000);
                entity.Property(e => e.Comment).HasMaxLength(2000);
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(100);
                entity.Property(e => e.UpdatedBy).HasMaxLength(100);
                entity.Property(e => e.DeletedBy).HasMaxLength(100);

                entity.HasIndex(e => e.ContainerNumber);
                entity.HasIndex(e => e.Type);
                entity.HasIndex(e => e.CreatedAt);
                entity.HasIndex(e => e.CreatedBy);
                entity.HasIndex(e => e.IsDeleted);
                // Gap 2: index the optional decision linkage so per-decision queries
                // (COCO export, audit trail) hit an index instead of a scan.
                entity.HasIndex(e => e.ImageAnalysisDecisionId);
            });

            // BL Review configuration
            modelBuilder.Entity<BLReviewRecord>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.MasterBlNumber).IsRequired().HasMaxLength(100);
                entity.Property(e => e.ReviewedBy).IsRequired().HasMaxLength(100);
                entity.Property(e => e.ReviewStatus).IsRequired().HasMaxLength(50);
                entity.Property(e => e.FinalDecision).IsRequired().HasMaxLength(50);
                entity.Property(e => e.BLComments).HasMaxLength(2000);

                entity.HasIndex(e => e.MasterBlNumber);
                entity.HasIndex(e => e.ReviewStatus);
                entity.HasIndex(e => e.FinalDecision);
                entity.HasIndex(e => e.ReviewedBy);
                entity.HasIndex(e => e.ReviewStartedAt);
                entity.HasIndex(e => e.ReviewCompletedAt);
            });

            // Container Review Decision configuration
            modelBuilder.Entity<ContainerReviewDecision>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ContainerNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Decision).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Comments).HasMaxLength(1000);
                entity.Property(e => e.ReviewedBy).IsRequired().HasMaxLength(100);
                entity.Property(e => e.ScannerType).HasMaxLength(50);

                entity.HasIndex(e => e.BLReviewRecordId);
                entity.HasIndex(e => e.ContainerNumber);
                entity.HasIndex(e => e.Decision);
                entity.HasIndex(e => e.ReviewedAt);

                // Relationship configuration
                entity.HasOne(e => e.BLReviewRecord)
                    .WithMany(b => b.ContainerDecisions)
                    .HasForeignKey(e => e.BLReviewRecordId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Settings Management configuration
            modelBuilder.Entity<SystemSetting>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Category).IsRequired().HasMaxLength(50);
                entity.Property(e => e.SettingKey).IsRequired().HasMaxLength(100);
                entity.Property(e => e.SettingValue).IsRequired();
                entity.Property(e => e.DataType).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Description).HasMaxLength(500);
                entity.Property(e => e.DefaultValue).HasMaxLength(1000);
                entity.Property(e => e.AllowedRoles).HasMaxLength(200);
                entity.Property(e => e.LastModifiedBy).HasMaxLength(100);

                // Unique constraint on Category + SettingKey
                entity.HasIndex(e => new { e.Category, e.SettingKey }).IsUnique();
                entity.HasIndex(e => e.Category);
                entity.HasIndex(e => e.IsActive);
                entity.HasIndex(e => e.LastModifiedAt);
            });

            modelBuilder.Entity<SettingsHistory>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Category).IsRequired().HasMaxLength(50);
                entity.Property(e => e.SettingKey).IsRequired().HasMaxLength(100);
                entity.Property(e => e.NewValue).IsRequired();
                entity.Property(e => e.ChangedBy).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Reason).HasMaxLength(500);
                entity.Property(e => e.IpAddress).HasMaxLength(50);

                entity.HasIndex(e => e.SystemSettingId);
                entity.HasIndex(e => e.Category);
                entity.HasIndex(e => e.ChangedBy);
                entity.HasIndex(e => e.ChangedAt);

                // Relationship
                entity.HasOne(e => e.SystemSetting)
                    .WithMany(s => s.History)
                    .HasForeignKey(e => e.SystemSettingId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UserPreference>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.PreferenceKey).IsRequired().HasMaxLength(100);
                entity.Property(e => e.PreferenceValue).IsRequired();
                entity.Property(e => e.DataType).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Description).HasMaxLength(500);

                // Unique constraint on UserId + PreferenceKey
                entity.HasIndex(e => new { e.UserId, e.PreferenceKey }).IsUnique();
                entity.HasIndex(e => e.UserId);
            });

            // HR & Corporate Management configurations
            modelBuilder.Entity<Organization>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Type).HasMaxLength(20);
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.HasIndex(e => e.Code).IsUnique();
                entity.HasIndex(e => e.Status);
            });

            modelBuilder.Entity<OrgUnit>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Type).HasMaxLength(20);
                entity.Property(e => e.CostCenterCode).HasMaxLength(50);
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.HasIndex(e => new { e.OrganizationId, e.Code });
                entity.HasIndex(e => e.Status);
            });

            modelBuilder.Entity<Site>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Type).HasMaxLength(20);
                entity.Property(e => e.Country).HasMaxLength(100);
                entity.Property(e => e.City).HasMaxLength(100);
                entity.Property(e => e.Address).HasMaxLength(200);
                entity.Property(e => e.Timezone).HasMaxLength(50);
                entity.Property(e => e.OperationalHours).HasMaxLength(500);
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.HasIndex(e => e.Code).IsUnique();
                entity.HasIndex(e => e.Status);
            });

            modelBuilder.Entity<Lane>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Direction).HasMaxLength(20);
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.HasIndex(e => new { e.SiteId, e.Code });
                entity.HasIndex(e => e.Status);
            });

            modelBuilder.Entity<ScannerAsset>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Manufacturer).HasMaxLength(100);
                entity.Property(e => e.Model).HasMaxLength(100);
                entity.Property(e => e.SerialNumber).HasMaxLength(100);
                entity.Property(e => e.ScannerType).HasMaxLength(20);
                entity.Property(e => e.EnergyType).HasMaxLength(50);
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.HasIndex(e => e.Code).IsUnique();
                entity.HasIndex(e => e.Status);
            });

            modelBuilder.Entity<Employee>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.EmployeeNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.OtherNames).HasMaxLength(100);
                entity.Property(e => e.Gender).HasMaxLength(20);
                entity.Property(e => e.NationalId).HasMaxLength(50);
                entity.Property(e => e.Email).HasMaxLength(100);
                entity.Property(e => e.Phone).HasMaxLength(20);
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.Property(e => e.EmploymentType).HasMaxLength(20);
                entity.Property(e => e.Notes).HasMaxLength(1000);
                entity.HasIndex(e => e.EmployeeNumber).IsUnique();
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.OrganizationId);
            });

            modelBuilder.Entity<Position>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Grade).HasMaxLength(20);
                entity.Property(e => e.PositionType).HasMaxLength(20);
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.HasIndex(e => e.Code).IsUnique();
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.OrgUnitId);
            });

            modelBuilder.Entity<EmployeePosition>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.HasIndex(e => new { e.EmployeeId, e.PositionId, e.Status });
                entity.HasIndex(e => e.EffectiveFrom);
                entity.HasIndex(e => e.EffectiveTo);
            });

            // Shift & Attendance configurations
            modelBuilder.Entity<ShiftTemplate>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.BreakRules).HasMaxLength(2000);
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.HasIndex(e => e.Code).IsUnique();
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.SiteId);
            });

            modelBuilder.Entity<ShiftAssignment>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.Property(e => e.ShiftType).HasMaxLength(20);
                entity.Property(e => e.Notes).HasMaxLength(1000);
                entity.HasIndex(e => new { e.EmployeeId, e.SiteId, e.Date, e.ShiftTemplateId });
                entity.HasIndex(e => e.EmployeeId);
                entity.HasIndex(e => e.SiteId);
                entity.HasIndex(e => e.Date);
                entity.HasIndex(e => e.Status);
            });

            modelBuilder.Entity<AttendanceRecord>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Source).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.Property(e => e.Remarks).HasMaxLength(1000);
                entity.Property(e => e.ApprovedBy).HasMaxLength(100);
                entity.HasIndex(e => e.EmployeeId);
                entity.HasIndex(e => e.SiteId);
                entity.HasIndex(e => e.Date);
                entity.HasIndex(e => e.ShiftAssignmentId);
            });

            modelBuilder.Entity<ShiftCoverageRequirement>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.RequiredRole).HasMaxLength(50);
                entity.HasIndex(e => e.SiteId);
                entity.HasIndex(e => e.LaneId);
                entity.HasIndex(e => e.ShiftTemplateId);
                entity.HasIndex(e => new { e.IsActive, e.EffectiveFrom, e.EffectiveTo });
            });

            modelBuilder.Entity<LeaveRequest>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.LeaveType).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.Property(e => e.RequestedBy).IsRequired().HasMaxLength(100);
                entity.Property(e => e.ApprovedBy).HasMaxLength(100);
                entity.Property(e => e.RejectionReason).HasMaxLength(1000);
                entity.Property(e => e.Remarks).HasMaxLength(1000);
                entity.HasIndex(e => e.EmployeeId);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => new { e.StartDate, e.EndDate });
            });

            modelBuilder.Entity<ShiftSwapRequest>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.Property(e => e.ApprovedBy).HasMaxLength(100);
                entity.Property(e => e.RejectionReason).HasMaxLength(1000);
                entity.Property(e => e.Remarks).HasMaxLength(1000);
                entity.HasIndex(e => e.RequestingEmployeeId);
                entity.HasIndex(e => e.Status);
            });

            // EndpointUsageLog configuration
            modelBuilder.Entity<EndpointUsageLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Endpoint).IsRequired().HasMaxLength(500);
                entity.Property(e => e.Method).IsRequired().HasMaxLength(10);
                entity.Property(e => e.IpAddress).HasMaxLength(50);
                entity.Property(e => e.UserAgent).HasMaxLength(500);
                entity.HasIndex(e => new { e.Endpoint, e.Timestamp });
                entity.HasIndex(e => new { e.IsDeprecated, e.Timestamp });
                entity.HasIndex(e => new { e.IsPhase3Route, e.Timestamp });
                entity.HasIndex(e => e.Timestamp);
            });

            // BusinessRule configuration
            modelBuilder.Entity<BusinessRule>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Description).IsRequired().HasMaxLength(1000);
                entity.Property(e => e.Category).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Priority).IsRequired().HasMaxLength(20);
                entity.Property(e => e.ConditionExpression).IsRequired().HasMaxLength(2000);
                entity.Property(e => e.ActionType).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ActionMessage).IsRequired().HasMaxLength(1000);
                entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(100);
                entity.Property(e => e.UpdatedBy).HasMaxLength(100);

                entity.HasIndex(e => e.Category);
                entity.HasIndex(e => e.Priority);
                entity.HasIndex(e => e.IsActive);
                entity.HasIndex(e => e.ExecutionOrder);
                entity.HasIndex(e => new { e.IsActive, e.ExecutionOrder });
            });

            // OriginalScanRecord - raw audit trail for scanner ingestion
            modelBuilder.Entity<OriginalScanRecord>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ScannerType).IsRequired().HasMaxLength(20);
                entity.Property(e => e.OriginalContainerNumbers).IsRequired().HasMaxLength(500);
                entity.Property(e => e.PicNumber).HasMaxLength(100);
                entity.Property(e => e.InspectionId).HasMaxLength(100);
                entity.Property(e => e.RawData).HasColumnType("text");
                entity.Property(e => e.SourceFilePath).HasMaxLength(1000);

                entity.HasIndex(e => e.ScannerType);
                entity.HasIndex(e => e.OriginalContainerNumbers);
                entity.HasIndex(e => e.PicNumber);
                entity.HasIndex(e => e.InspectionId);
                entity.HasIndex(e => e.IngestedAt);
            });

            // FS6000Scan -> OriginalScanRecord FK
            modelBuilder.Entity<FS6000Scan>(entity =>
            {
                entity.HasOne(e => e.OriginalScanRecord)
                      .WithMany()
                      .HasForeignKey(e => e.OriginalScanRecordId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // AseScan -> OriginalScanRecord FK
            modelBuilder.Entity<AseScan>(entity =>
            {
                entity.HasOne(e => e.OriginalScanRecord)
                      .WithMany()
                      .HasForeignKey(e => e.OriginalScanRecordId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<AiImageAnalysisSuggestion>(entity =>
            {
                entity.HasIndex(e => new { e.ContainerNumber, e.ScannerType });
                entity.HasIndex(e => e.AnalysisGroupId);
                entity.HasIndex(e => e.ResolvedAtUtc);
            });

            modelBuilder.Entity<AiDatasetSnapshot>(entity =>
            {
                entity.HasIndex(e => e.CreatedAtUtc);
            });

            // ── Controlled-vocabulary finding categories (Gap 1a — AI training flywheel) ──
            modelBuilder.Entity<ThreatCategory>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Code).IsRequired().HasMaxLength(64);
                entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(120);
                entity.Property(e => e.Description).HasMaxLength(500);
                entity.HasIndex(e => e.Code).IsUnique();
                entity.HasIndex(e => e.IsActive);
                entity.HasIndex(e => e.SortOrder);
            });

            modelBuilder.Entity<RevenueAnomalyCategory>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Code).IsRequired().HasMaxLength(64);
                entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(120);
                entity.Property(e => e.Description).HasMaxLength(500);
                entity.HasIndex(e => e.Code).IsUnique();
                entity.HasIndex(e => e.IsActive);
                entity.HasIndex(e => e.SortOrder);
            });

            // Manifest snapshot at decision time (Gap 0 — AI training flywheel)
            modelBuilder.Entity<ManifestSnapshot>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Source).IsRequired().HasMaxLength(20);
                entity.Property(e => e.SnapshotTakenAtUtc).IsRequired();

                // One snapshot per analyst decision. Decision is the parent.
                entity.HasOne(e => e.ImageAnalysisDecision)
                      .WithMany()
                      .HasForeignKey(e => e.ImageAnalysisDecisionId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.ImageAnalysisDecisionId);
                entity.HasIndex(e => e.BOEDocumentId);
                entity.HasIndex(e => e.ContainerNumber);
                entity.HasIndex(e => e.SnapshotTakenAtUtc);
                entity.HasIndex(e => e.Source);
            });

            // Per-image audit verdicts (child of AuditDecision)
            modelBuilder.Entity<AuditImageDecision>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ContainerNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ScannerType).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Decision).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Notes).HasMaxLength(500);
                entity.Property(e => e.AuditedBy).IsRequired().HasMaxLength(100);

                entity.HasOne(e => e.AuditDecision)
                      .WithMany()
                      .HasForeignKey(e => e.AuditDecisionId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.AuditDecisionId);
                entity.HasIndex(e => e.ContainerNumber);
                entity.HasIndex(e => new { e.AuditDecisionId, e.ImageIndex });
            });

            // Match quality flags (Match Correction Tool)
            modelBuilder.Entity<MatchQualityFlag>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ContainerNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.FlagType).IsRequired().HasMaxLength(64);
                entity.Property(e => e.Severity).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Description).HasMaxLength(1000);
                entity.Property(e => e.ResolvedBy).HasMaxLength(100);
                entity.Property(e => e.Resolution).HasMaxLength(20);
                entity.Property(e => e.ResolutionNotes).HasMaxLength(1000);
                entity.Property(e => e.ScannerType).HasMaxLength(20);

                entity.HasIndex(e => e.ContainerNumber);
                entity.HasIndex(e => e.FlagType);
                entity.HasIndex(e => e.IsResolved);
                entity.HasIndex(e => e.Severity);
                entity.HasIndex(e => e.CreatedAtUtc);
                // Composite for the admin "open flags" landing-page query
                entity.HasIndex(e => new { e.IsResolved, e.Severity, e.CreatedAtUtc });
            });

            // Dashboard alerts (Sprint 5G3 / audit 8.25 — persisted alert log)
            modelBuilder.Entity<DashboardAlertEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Type).IsRequired().HasMaxLength(64);
                entity.Property(e => e.Severity).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Description).HasMaxLength(2000);
                entity.Property(e => e.Source).HasMaxLength(200);
                entity.Property(e => e.AcknowledgedBy).HasMaxLength(100);

                // Phase-1 tenancy: DB default = current_setting('app.tenant_id')::bigint
                // is applied via raw SQL in the migration (EF can't express
                // server-side function defaults reliably across providers).
                entity.Property(e => e.TenantId).HasColumnName("tenant_id");

                entity.HasIndex(e => e.Type);
                entity.HasIndex(e => e.Severity);
                entity.HasIndex(e => e.RaisedAtUtc);
                entity.HasIndex(e => e.AcknowledgedAtUtc);
                // Dedupe support — IDashboardAlertService probes
                // (Type, Title, RaisedAtUtc) within a 30-minute window.
                entity.HasIndex(e => new { e.Type, e.Title, e.RaisedAtUtc })
                      .HasDatabaseName("ix_dashboardalerts_type_title_raisedatutc");
                // Tenancy index — matches the phase-1 pattern
                entity.HasIndex(e => new { e.TenantId, e.Id })
                      .HasDatabaseName("ix_dashboardalerts_tenant_id");
            });

            // Decision Agent configuration
            modelBuilder.Entity<DecisionAgentCondition>(entity =>
            {
                entity.HasIndex(e => e.ConditionKey);
                entity.HasIndex(e => e.Enabled);
            });

            modelBuilder.Entity<DecisionAgentAuditLog>(entity =>
            {
                entity.HasIndex(e => e.GroupId);
                entity.HasIndex(e => e.CreatedAtUtc);
                entity.HasIndex(e => e.Decision);
            });

            // PostgreSQL: lowercase all table and column names so unquoted raw SQL works
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                var tableName = entity.GetTableName();
                if (tableName != null)
                    entity.SetTableName(tableName.ToLower());

                foreach (var property in entity.GetProperties())
                {
                    var colName = property.GetColumnName();
                    if (colName != null)
                        property.SetColumnName(colName.ToLower());
                }

                foreach (var key in entity.GetKeys())
                {
                    var keyName = key.GetName();
                    if (keyName != null)
                        key.SetName(keyName.ToLower());
                }

                foreach (var fk in entity.GetForeignKeys())
                {
                    var fkName = fk.GetConstraintName();
                    if (fkName != null)
                        fk.SetConstraintName(fkName.ToLower());
                }

                foreach (var index in entity.GetIndexes())
                {
                    var idxName = index.GetDatabaseName();
                    if (idxName != null)
                        index.SetDatabaseName(idxName.ToLower());
                }
            }
        }
    }
}
