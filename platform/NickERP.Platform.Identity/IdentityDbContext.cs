using Microsoft.EntityFrameworkCore;
using NickERP.Platform.Identity.Entities;

namespace NickERP.Platform.Identity;

/// <summary>
/// EF Core context for the canonical identity store. Schema = "identity"
/// inside the platform DB <c>nickerp_platform</c>. Deliberately a separate
/// DB from any v1 application data (NSCIM, HR) so identity isn't coupled
/// to the lifecycle of any specific module's database.
/// </summary>
public class IdentityDbContext : DbContext
{
    public const string SchemaName = "identity";

    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options) { }

    public DbSet<IdentityUser> Users => Set<IdentityUser>();
    public DbSet<AppScope> AppScopes => Set<AppScope>();
    public DbSet<UserScope> UserScopes => Set<UserScope>();
    public DbSet<ServiceTokenIdentity> ServiceTokens => Set<ServiceTokenIdentity>();
    public DbSet<ServiceTokenScope> ServiceTokenScopes => Set<ServiceTokenScope>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.HasDefaultSchema(SchemaName);

        mb.Entity<IdentityUser>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(254).IsRequired();
            e.Property(x => x.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(254).IsRequired();
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200);
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");

            // Email is the natural key within a tenant.
            e.HasIndex(x => new { x.TenantId, x.NormalizedEmail }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.IsActive });
            e.HasIndex(x => x.LastSeenAt);

            e.HasMany(x => x.Scopes)
                .WithOne(s => s.User)
                .HasForeignKey(s => s.IdentityUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<AppScope>(e =>
        {
            e.ToTable("app_scopes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(128).IsRequired();
            e.Property(x => x.AppName).HasColumnName("app_name").HasMaxLength(64).IsRequired();
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");

            // Code is the natural key. Uniqueness scoped to the tenant.
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.AppName, x.IsActive });
        });

        mb.Entity<UserScope>(e =>
        {
            e.ToTable("user_scopes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.IdentityUserId).HasColumnName("identity_user_id");
            e.Property(x => x.AppScopeCode).HasColumnName("app_scope_code").HasMaxLength(128).IsRequired();
            e.Property(x => x.GrantedAt).HasColumnName("granted_at");
            e.Property(x => x.GrantedByUserId).HasColumnName("granted_by_user_id");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            e.Property(x => x.RevokedByUserId).HasColumnName("revoked_by_user_id");
            e.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);
            e.Property(x => x.TenantId).HasColumnName("tenant_id");

            // A user can hold the same scope only once at a time. Filter excludes
            // historical (revoked) rows so re-granting after revocation is allowed.
            e.HasIndex(x => new { x.TenantId, x.IdentityUserId, x.AppScopeCode })
                .IsUnique()
                .HasFilter("revoked_at IS NULL");

            e.HasIndex(x => new { x.TenantId, x.AppScopeCode });
        });

        mb.Entity<ServiceTokenIdentity>(e =>
        {
            e.ToTable("service_tokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TokenClientId).HasColumnName("token_client_id").HasMaxLength(128).IsRequired();
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
            e.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(500);
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");

            e.HasIndex(x => new { x.TenantId, x.TokenClientId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.IsActive });

            e.HasMany(x => x.Scopes)
                .WithOne(s => s.ServiceToken)
                .HasForeignKey(s => s.ServiceTokenIdentityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<ServiceTokenScope>(e =>
        {
            e.ToTable("service_token_scopes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ServiceTokenIdentityId).HasColumnName("service_token_identity_id");
            e.Property(x => x.AppScopeCode).HasColumnName("app_scope_code").HasMaxLength(128).IsRequired();
            e.Property(x => x.GrantedAt).HasColumnName("granted_at");
            e.Property(x => x.GrantedByUserId).HasColumnName("granted_by_user_id");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            e.Property(x => x.RevokedByUserId).HasColumnName("revoked_by_user_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");

            e.HasIndex(x => new { x.TenantId, x.ServiceTokenIdentityId, x.AppScopeCode })
                .IsUnique()
                .HasFilter("revoked_at IS NULL");
        });
    }
}

/// <summary>
/// Design-time factory so <c>dotnet ef migrations add ...</c> can construct
/// a context without a running host. Reads the connection string from env
/// var <c>NICKERP_PLATFORM_DB</c>; falls back to a local default for
/// developer machines.
/// </summary>
public class IdentityDbContextDesignTimeFactory
    : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("NICKERP_PLATFORM_DB")
            ?? "Host=localhost;Port=5432;Database=nickerp_platform;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(conn, npg => npg.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.SchemaName))
            .Options;

        return new IdentityDbContext(options);
    }
}
