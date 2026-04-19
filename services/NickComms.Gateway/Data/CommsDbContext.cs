using Microsoft.EntityFrameworkCore;
using NickComms.Gateway.Entities;

namespace NickComms.Gateway.Data;

public class CommsDbContext : DbContext
{
    public CommsDbContext(DbContextOptions<CommsDbContext> options) : base(options) { }

    public DbSet<SmsMessage> SmsMessages => Set<SmsMessage>();
    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    public DbSet<OtpSession> OtpSessions => Set<OtpSession>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SmsMessage>(e =>
        {
            e.HasIndex(m => m.BatchId).HasFilter("batch_id IS NOT NULL");
            e.HasIndex(m => new { m.ClientApp, m.CreatedAt }).IsDescending(false, true);
            e.HasIndex(m => m.Recipient);
        });

        modelBuilder.Entity<EmailMessage>(e =>
        {
            e.HasIndex(m => m.BatchId).HasFilter("batch_id IS NOT NULL");
            e.HasIndex(m => new { m.ClientApp, m.CreatedAt }).IsDescending(false, true);
            e.HasIndex(m => m.ToEmail);
        });

        modelBuilder.Entity<OtpSession>(e =>
        {
            e.HasIndex(o => new { o.PhoneNumber, o.CreatedAt }).IsDescending(false, true);
        });

        modelBuilder.Entity<ApiKey>(e =>
        {
            e.HasIndex(k => k.AppName).IsUnique();
            e.HasIndex(k => k.KeyHash);
        });
    }
}
