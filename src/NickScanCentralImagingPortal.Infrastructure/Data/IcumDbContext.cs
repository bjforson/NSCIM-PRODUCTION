using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Infrastructure.Data
{
    public class IcumDbContext : DbContext
    {
        public IcumDbContext(DbContextOptions<IcumDbContext> options) : base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            // Allow database updates even when EF detects pending model changes (we manage schema via controlled migrations)
            optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        }

        public DbSet<IcumContainerData> IcumContainerData { get; set; }
        public DbSet<IcumDocument> IcumDocuments { get; set; }
        public DbSet<IcumBatchLog> IcumBatchLogs { get; set; }
        public DbSet<IcumManifestItem> IcumManifestItems { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure IcumContainerData
            modelBuilder.Entity<IcumContainerData>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ContainerNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.BoeData).HasColumnType("text");
                entity.Property(e => e.Status).HasMaxLength(50);
                entity.HasIndex(e => e.ContainerNumber).IsUnique();

                // Add indexes for new structured fields
                entity.HasIndex(e => e.MasterBlNumber);
                entity.HasIndex(e => e.HouseBl);
                entity.HasIndex(e => e.RotationNumber);
                entity.HasIndex(e => e.ConsigneeName);
                entity.HasIndex(e => e.ShipperName);
                entity.HasIndex(e => e.CountryOfOrigin);
                entity.HasIndex(e => e.CrmsLevel);
                entity.HasIndex(e => e.ClearanceType);
                entity.HasIndex(e => e.DeclarationNumber);

                // Configure relationships
                entity.HasMany(e => e.ManifestItems)
                      .WithOne(mi => mi.IcumContainerData)
                      .HasForeignKey(mi => mi.IcumContainerDataId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure IcumDocument
            modelBuilder.Entity<IcumDocument>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ContainerNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.DocumentType).IsRequired().HasMaxLength(100);
                entity.Property(e => e.DocumentData).HasColumnType("text");
            });

            // Configure IcumBatchLog
            modelBuilder.Entity<IcumBatchLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
            });

            // Configure IcumManifestItem
            modelBuilder.Entity<IcumManifestItem>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.HsCode).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Description).HasMaxLength(2000);
                entity.Property(e => e.Unit).HasMaxLength(50);
                entity.Property(e => e.FobCurrency).HasMaxLength(10);
                entity.Property(e => e.CountryOfOrigin).HasMaxLength(10);
                entity.Property(e => e.Cpc).HasMaxLength(20);

                // Add indexes for common queries
                entity.HasIndex(e => e.HsCode);
                entity.HasIndex(e => e.CountryOfOrigin);
                entity.HasIndex(e => e.IcumContainerDataId);

                // Ensure foreign key relationship
                entity.HasOne(e => e.IcumContainerData)
                      .WithMany(ic => ic.ManifestItems)
                      .HasForeignKey(e => e.IcumContainerDataId)
                      .OnDelete(DeleteBehavior.Cascade);
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
