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
            // Outbox claim path: WHERE status='queued' AND next_attempt_at <= NOW()
            // ORDER BY created_at — this composite index keeps that scan O(log n)
            // even with millions of historical sent rows.
            e.HasIndex(m => new { m.Status, m.NextAttemptAt })
             .HasDatabaseName("ix_sms_messages_outbox");
        });

        modelBuilder.Entity<EmailMessage>(e =>
        {
            e.HasIndex(m => m.BatchId).HasFilter("batch_id IS NOT NULL");
            e.HasIndex(m => new { m.ClientApp, m.CreatedAt }).IsDescending(false, true);
            e.HasIndex(m => m.ToEmail);
            e.HasIndex(m => new { m.Status, m.NextAttemptAt })
             .HasDatabaseName("ix_email_messages_outbox");
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
