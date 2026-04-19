using Microsoft.EntityFrameworkCore;

namespace NickERP.Platform.Tenancy.Database;

/// <summary>
/// EF Core DbContext for the canonical <c>nick_platform</c> PostgreSQL database.
/// Owns the master tenants table, tenant ↔ user mappings, and tenant ↔ module
/// subscriptions.
///
/// Only the platform layer (and the tenant management UI in Phase 10) touches
/// this DbContext. Modules read tenant lists via the
/// <c>NickERP.Platform.Tenancy.Client</c> HTTP client (added in Phase 1d) — never
/// by referencing this assembly directly.
/// </summary>
public sealed class PlatformDbContext : DbContext
{
    public PlatformDbContext(DbContextOptions<PlatformDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();
    public DbSet<TenantModuleSubscription> TenantModuleSubscriptions => Set<TenantModuleSubscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).IsRequired().HasMaxLength(50);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.BillingPlan).HasMaxLength(50).HasDefaultValue("internal");
            e.Property(x => x.TimeZone).HasMaxLength(50).HasDefaultValue("Africa/Accra");
            e.Property(x => x.Locale).HasMaxLength(20).HasDefaultValue("en-GH");
            e.Property(x => x.Currency).HasMaxLength(3).HasDefaultValue("GHS");
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Seed the default tenant for the existing single-tenant deployment.
            e.HasData(new Tenant
            {
                Id = Tenant.DefaultTenantId,
                Code = Tenant.DefaultTenantCode,
                Name = "Nick TC-Scan Operations",
                BillingPlan = "internal",
                TimeZone = "Africa/Accra",
                Locale = "en-GH",
                Currency = "GHS",
                IsActive = true,
                CreatedAt = new DateTime(2026, 4, 6, 0, 0, 0, DateTimeKind.Utc)
            });
        });

        modelBuilder.Entity<TenantUser>(e =>
        {
            e.ToTable("tenant_users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.Property(x => x.UserId).IsRequired().HasMaxLength(100);
            e.Property(x => x.Username).HasMaxLength(100);
            e.Property(x => x.IsPrimary).HasDefaultValue(true);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TenantModuleSubscription>(e =>
        {
            e.ToTable("tenant_module_subscriptions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.HasIndex(x => new { x.TenantId, x.ModuleName }).IsUnique();
            e.Property(x => x.ModuleName).IsRequired().HasMaxLength(50);
            e.Property(x => x.IsEnabled).HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);

            // Seed default module subscriptions for the default tenant
            e.HasData(
                new TenantModuleSubscription { Id = 1, TenantId = Tenant.DefaultTenantId, ModuleName = "nscis", IsEnabled = true, CreatedAt = new DateTime(2026, 4, 6, 0, 0, 0, DateTimeKind.Utc) },
                new TenantModuleSubscription { Id = 2, TenantId = Tenant.DefaultTenantId, ModuleName = "nickhr", IsEnabled = true, CreatedAt = new DateTime(2026, 4, 6, 0, 0, 0, DateTimeKind.Utc) }
            );
        });
    }
}

/// <summary>
/// Design-time factory for <c>dotnet ef migrations add</c>. Reads connection
/// string from env var <c>NICKERP_PLATFORM_DB_CONNECTION</c> with a localhost
/// fallback for developer machines.
/// </summary>
public sealed class PlatformDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NICKERP_PLATFORM_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=nick_platform;Username=postgres;Password=designtime";

        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(PlatformDbContext).Assembly.GetName().Name))
            .Options;

        return new PlatformDbContext(options);
    }
}
