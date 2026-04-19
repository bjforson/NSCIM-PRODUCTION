using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Infrastructure.Data
{
    public class IcumDownloadsDbContext : DbContext
    {
        public IcumDownloadsDbContext(DbContextOptions<IcumDownloadsDbContext> options) : base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            // Allow database updates even when EF detects pending model changes (we manage schema via controlled migrations)
            optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        }

        public DbSet<DownloadedFile> DownloadedFiles { get; set; }
        public DbSet<BOEDocument> BOEDocuments { get; set; }
        public DbSet<DownloadedManifestItem> ManifestItems { get; set; }
        public DbSet<IngestionLog> IngestionLogs { get; set; }
        public DbSet<VehicleImport> VehicleImports { get; set; }
        public DbSet<NickScanCentralImagingPortal.Core.Entities.ICUMSDownloadQueue> ICUMSDownloadQueue { get; set; }
        public DbSet<CMRRedownloadQueue> CMRRedownloadQueues { get; set; }
        public DbSet<CMRValidationMetrics> CMRValidationMetrics { get; set; }
        public DbSet<ContainerDownloadHistory> ContainerDownloadHistory { get; set; }
        public DbSet<FailedProcessingQueue> FailedProcessingQueue { get; set; }
        public DbSet<ArchivedFile> ArchivedFiles { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure DownloadedFile
            modelBuilder.Entity<DownloadedFile>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.FileName).IsRequired().HasMaxLength(500);
                entity.Property(e => e.FilePath).IsRequired().HasMaxLength(1000);
                entity.Property(e => e.FileHash).HasMaxLength(64); // ✅ FIX 4: SHA256 hash (64 hex chars)
                entity.Property(e => e.ProcessingStatus).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ErrorMessage).HasMaxLength(4000);
                entity.Property(e => e.LowestAccuracyContainer).HasMaxLength(50);
                entity.Property(e => e.VerificationDetails).HasColumnType("text");
                entity.HasIndex(e => e.FileName);
                entity.HasIndex(e => e.FileHash);
                entity.HasIndex(e => e.ProcessingStatus);
                entity.HasIndex(e => e.DownloadDate);
            });

            // Configure BOEDocument
            modelBuilder.Entity<BOEDocument>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ContainerNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ContainerDescription).HasMaxLength(4000);
                entity.Property(e => e.ContainerISO).HasMaxLength(20);
                entity.Property(e => e.ContainerSize).HasMaxLength(20);
                entity.Property(e => e.SealNumber).HasMaxLength(100);
                entity.Property(e => e.TruckPlateNumber).HasMaxLength(100);
                entity.Property(e => e.DriverName).HasMaxLength(200);
                entity.Property(e => e.DriverLicense).HasMaxLength(100);
                entity.Property(e => e.ContainerStatus).HasMaxLength(100);
                entity.Property(e => e.ContainerRemarks).HasMaxLength(4000);
                entity.Property(e => e.ImpName).HasMaxLength(500);
                entity.Property(e => e.CrmsLevel).HasMaxLength(50);
                entity.Property(e => e.ExpAddress).HasMaxLength(4000);
                entity.Property(e => e.DeclarationNumber).HasMaxLength(100);
                entity.Property(e => e.RegimeCode).HasMaxLength(20);
                entity.Property(e => e.CompOffRemarks).HasMaxLength(4000);
                entity.Property(e => e.DeclarantName).HasMaxLength(500);
                entity.Property(e => e.ExpName).HasMaxLength(500);
                entity.Property(e => e.ImpAddress).HasMaxLength(4000);
                entity.Property(e => e.ImpExpName).HasMaxLength(500);
                entity.Property(e => e.CcvrIntelRemarks).HasMaxLength(4000);
                entity.Property(e => e.ImpExpAddress).HasMaxLength(4000);
                entity.Property(e => e.DeclarationDate).HasMaxLength(50);
                entity.Property(e => e.ClearanceType).HasMaxLength(20);
                entity.Property(e => e.DeclarantAddress).HasMaxLength(4000);
                entity.Property(e => e.RotationNumber).HasMaxLength(100);
                entity.Property(e => e.ConsigneeName).HasMaxLength(500);
                entity.Property(e => e.CountryOfOrigin).HasMaxLength(100);
                entity.Property(e => e.MarksNumbers).HasMaxLength(4000);
                entity.Property(e => e.ShipperName).HasMaxLength(500);
                entity.Property(e => e.ShipperAddress).HasMaxLength(4000);
                entity.Property(e => e.BlNumber).HasMaxLength(100);
                entity.Property(e => e.DeliveryPlace).HasMaxLength(200);
                entity.Property(e => e.HouseBl).HasMaxLength(100);
                entity.Property(e => e.ConsigneeAddress).HasMaxLength(4000);
                entity.Property(e => e.GoodsDescription).HasMaxLength(4000);

                // 🔍 BULLETPROOF: Unmapped fields storage - Tier 1 (20 slots)
                entity.Property(e => e.UnmappedField1Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField1Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField2Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField2Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField3Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField3Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField4Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField4Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField5Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField5Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField6Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField6Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField7Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField7Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField8Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField8Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField9Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField9Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField10Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField10Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField11Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField11Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField12Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField12Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField13Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField13Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField14Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField14Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField15Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField15Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField16Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField16Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField17Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField17Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField18Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField18Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField19Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField19Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField20Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField20Value).HasMaxLength(4000);

                // 🔍 BULLETPROOF: Tier 2 - Complete backup
                entity.Property(e => e.RawJsonData).HasColumnType("text");
                entity.Property(e => e.UnmappedFieldsCount);
                entity.Property(e => e.UnmappedFieldsOverflow);

                entity.Property(e => e.ProcessingStatus).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ErrorMessage).HasMaxLength(4000);

                // CMR upgrade provenance (1.13.0)
                entity.Property(e => e.OriginalClearanceType).HasMaxLength(20);
                entity.Property(e => e.CmrUpgradedAt);

                entity.HasIndex(e => e.ContainerNumber);
                entity.HasIndex(e => e.DeclarationNumber); // ✅ PERFORMANCE: Index for GetReadyGroups query optimization
                entity.HasIndex(e => e.ProcessingStatus);
                entity.HasIndex(e => e.DownloadedFileId);

                // Add unique constraint to prevent duplicates based on ContainerNumber + DeclarationNumber
                entity.HasIndex(e => new { e.ContainerNumber, e.DeclarationNumber })
                      .IsUnique()
                      .HasDatabaseName("IX_BOEDocument_ContainerNumber_DeclarationNumber_Unique");

                // Additional guard: enforce uniqueness for rows where DeclarationNumber IS NULL
                entity.HasIndex(e => e.ContainerNumber)
                      .IsUnique()
                      .HasFilter("declarationnumber IS NULL")
                      .HasDatabaseName("IX_BOEDocument_Container_Unique_When_Declaration_Null");

                entity.HasOne(e => e.DownloadedFile)
                      .WithMany()
                      .HasForeignKey(e => e.DownloadedFileId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure DownloadedManifestItem
            modelBuilder.Entity<DownloadedManifestItem>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.HsCode).HasMaxLength(20);
                entity.Property(e => e.Description).HasMaxLength(4000);
                entity.Property(e => e.Unit).HasMaxLength(50);
                entity.Property(e => e.FobCurrency).HasMaxLength(10);
                entity.Property(e => e.CountryOfOrigin).HasMaxLength(100);
                entity.Property(e => e.Cpc).HasMaxLength(50);

                // 🔍 BULLETPROOF: Unmapped fields storage for ManifestItems - Tier 1 (20 slots)
                entity.Property(e => e.UnmappedField1Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField1Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField2Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField2Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField3Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField3Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField4Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField4Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField5Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField5Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField6Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField6Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField7Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField7Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField8Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField8Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField9Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField9Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField10Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField10Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField11Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField11Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField12Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField12Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField13Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField13Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField14Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField14Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField15Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField15Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField16Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField16Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField17Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField17Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField18Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField18Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField19Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField19Value).HasMaxLength(4000);
                entity.Property(e => e.UnmappedField20Label).HasMaxLength(200);
                entity.Property(e => e.UnmappedField20Value).HasMaxLength(4000);

                // 🔍 BULLETPROOF: Tier 2 - Complete backup for ManifestItems
                entity.Property(e => e.RawJsonData).HasColumnType("text");
                entity.Property(e => e.UnmappedFieldsCount);
                entity.Property(e => e.UnmappedFieldsOverflow);

                entity.Property(e => e.ProcessingStatus).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ErrorMessage).HasMaxLength(4000);

                entity.HasIndex(e => e.HsCode);
                entity.HasIndex(e => e.ProcessingStatus);
                entity.HasIndex(e => e.BOEDocumentId);

                entity.HasOne(e => e.BOEDocument)
                      .WithMany()
                      .HasForeignKey(e => e.BOEDocumentId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure IngestionLog
            modelBuilder.Entity<IngestionLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ProcessType).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ErrorMessage).HasMaxLength(4000);
                entity.Property(e => e.Details).HasMaxLength(4000);

                entity.HasIndex(e => e.DownloadedFileId);
                entity.HasIndex(e => e.ProcessType);
                entity.HasIndex(e => e.Status);

                entity.HasOne(e => e.DownloadedFile)
                      .WithMany()
                      .HasForeignKey(e => e.DownloadedFileId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure VehicleImport
            modelBuilder.Entity<VehicleImport>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.VIN).IsRequired().HasMaxLength(17);
                entity.Property(e => e.DeclarationNumber).HasMaxLength(50);
                entity.Property(e => e.ChassisNumber).HasMaxLength(50);
                entity.Property(e => e.VehicleType).HasMaxLength(200);
                entity.Property(e => e.Make).HasMaxLength(100);
                entity.Property(e => e.Model).HasMaxLength(100);
                entity.Property(e => e.VehicleYear).HasMaxLength(10);
                entity.Property(e => e.EngineCapacity).HasMaxLength(20);
                entity.Property(e => e.HSCode).HasMaxLength(20);
                entity.Property(e => e.CountryOfOrigin).HasMaxLength(10);
                entity.Property(e => e.FOBCurrency).HasMaxLength(10);
                entity.Property(e => e.ImporterName).HasMaxLength(500);
                entity.Property(e => e.ShipperName).HasMaxLength(500);
                entity.Property(e => e.ConsigneeName).HasMaxLength(500);
                entity.Property(e => e.BLNumber).HasMaxLength(100);
                entity.Property(e => e.HouseBL).HasMaxLength(100);
                entity.Property(e => e.RotationNumber).HasMaxLength(50);
                entity.Property(e => e.ClearanceType).HasMaxLength(10);
                entity.Property(e => e.CrmsLevel).HasMaxLength(20);
                entity.Property(e => e.ProcessingStatus).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
                entity.Property(e => e.Remarks).HasMaxLength(2000);
                entity.Property(e => e.ContainerNumber).HasMaxLength(20);

                // Indexes
                entity.HasIndex(e => e.VIN).IsUnique();
                entity.HasIndex(e => e.ChassisNumber);
                entity.HasIndex(e => e.DeclarationNumber);
                entity.HasIndex(e => e.ProcessingStatus);
                entity.HasIndex(e => e.ImportType);
                entity.HasIndex(e => e.CreatedAt);

                // Foreign key relationship
                entity.HasOne(e => e.BOEDocument)
                      .WithMany()
                      .HasForeignKey(e => e.BOEDocumentId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure ICUMSDownloadQueue
            modelBuilder.Entity<NickScanCentralImagingPortal.Core.Entities.ICUMSDownloadQueue>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ContainerNumber).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
                entity.Property(e => e.LastErrorMessage).HasMaxLength(1000);
                entity.Property(e => e.LastErrorCode).HasMaxLength(50);
                entity.Property(e => e.RequestedBy).HasMaxLength(100);
                entity.Property(e => e.RequestSource).HasMaxLength(50);
                entity.Property(e => e.Metadata).HasMaxLength(2000);

                // Indexes for efficient querying
                entity.HasIndex(e => e.ContainerNumber).IsUnique();
                entity.HasIndex(e => new { e.Status, e.Priority, e.QueuedAt })
                      .HasDatabaseName("IX_ICUMSDownloadQueue_StatusPriorityQueued");
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.QueuedAt);
                entity.HasIndex(e => e.CompletedAt);
            });

            // Configure CMRRedownloadQueue
            modelBuilder.Entity<CMRRedownloadQueue>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ContainerNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Reason).IsRequired().HasMaxLength(500);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
                entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
                entity.Property(e => e.ProcessedBy).HasMaxLength(100);
                entity.Property(e => e.Priority).HasMaxLength(50);
                entity.Property(e => e.OriginalDeclarationNumber).HasMaxLength(100);
                entity.Property(e => e.OriginalClearanceType).HasMaxLength(20);

                // Indexes for efficient querying
                entity.HasIndex(e => e.ContainerNumber);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.QueuedAt);
                entity.HasIndex(e => e.ProcessedAt);
                entity.HasIndex(e => new { e.Status, e.Priority, e.QueuedAt })
                      .HasDatabaseName("IX_CMRRedownloadQueue_StatusPriorityQueued");
            });

            // Configure CMRValidationMetrics
            modelBuilder.Entity<CMRValidationMetrics>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.RecordedAt);
                entity.HasIndex(e => e.CreatedAt);
            });

            // Configure ContainerDownloadHistory - Phase 1.2 Deduplication
            modelBuilder.Entity<ContainerDownloadHistory>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ContainerNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.DownloadSource).IsRequired().HasMaxLength(100);
                entity.Property(e => e.ErrorMessage).HasMaxLength(1000);

                // Indexes for efficient deduplication queries
                entity.HasIndex(e => new { e.ContainerNumber, e.DownloadedAt })
                      .HasDatabaseName("IX_ContainerDownloadHistory_ContainerNumber_DownloadedAt");
                entity.HasIndex(e => e.ContainerNumber);
                entity.HasIndex(e => e.DownloadedAt);
                entity.HasIndex(e => e.HasValidData);
            });

            // Configure FailedProcessingQueue - Phase 2.2 Dead-Letter Queue
            modelBuilder.Entity<FailedProcessingQueue>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.FileName).IsRequired().HasMaxLength(500);
                entity.Property(e => e.FilePath).IsRequired().HasMaxLength(1000);
                entity.Property(e => e.FailureReason).IsRequired().HasMaxLength(500);
                entity.Property(e => e.ErrorDetails).HasMaxLength(4000);
                entity.Property(e => e.FailureStage).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(50);

                // Indexes for efficient retry queries
                entity.HasIndex(e => new { e.Status, e.NextRetryAt })
                      .HasDatabaseName("IX_FailedProcessingQueue_Status_NextRetryAt");
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.NextRetryAt);
                entity.HasIndex(e => e.DownloadedFileId);
                entity.HasIndex(e => e.FailedAt);
            });

            // Configure ArchivedFile - Archive Solution
            modelBuilder.Entity<ArchivedFile>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.OriginalFileName).IsRequired().HasMaxLength(500);
                entity.Property(e => e.OriginalFilePath).IsRequired().HasMaxLength(1000);
                entity.Property(e => e.ArchiveFileName).IsRequired().HasMaxLength(500);
                entity.Property(e => e.ArchiveFilePath).IsRequired().HasMaxLength(1000);
                entity.Property(e => e.ArchiveDirectory).IsRequired().HasMaxLength(200);
                entity.Property(e => e.CompressionType).IsRequired().HasMaxLength(50);
                entity.Property(e => e.FileType).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ContainerNumbers).HasMaxLength(2000); // Comma-separated list

                // Indexes for efficient archive queries
                entity.HasIndex(e => e.DownloadedFileId);
                entity.HasIndex(e => e.ArchivedDate);
                entity.HasIndex(e => e.ProcessedDate);
                entity.HasIndex(e => e.FileType);
                entity.HasIndex(e => e.IsRestored);
                entity.HasIndex(e => new { e.ArchivedDate, e.FileType })
                      .HasDatabaseName("IX_ArchivedFile_ArchivedDate_FileType");
            });

            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                var tableName = entity.GetTableName();
                if (tableName != null) entity.SetTableName(tableName.ToLower());
                foreach (var property in entity.GetProperties())
                { var c = property.GetColumnName(); if (c != null) property.SetColumnName(c.ToLower()); }
                foreach (var key in entity.GetKeys())
                { var k = key.GetName(); if (k != null) key.SetName(k.ToLower()); }
                foreach (var fk in entity.GetForeignKeys())
                { var f = fk.GetConstraintName(); if (f != null) fk.SetConstraintName(f.ToLower()); }
                foreach (var index in entity.GetIndexes())
                { var i = index.GetDatabaseName(); if (i != null) index.SetDatabaseName(i.ToLower()); }
            }
        }
    }
}
