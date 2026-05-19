using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.Monitoring;
using Xunit;

namespace NickScanCentralImagingPortal.Integration.Tests.Monitoring;

public class DashboardAlertEmailDedupeTests
{
    [Fact]
    public async Task CriticalAlert_QueuesEmailOnlyOnceForSameAlertKey()
    {
        await using var db = NewInMemoryDb();
        var comms = new RecordingNickCommsClient();
        var service = NewService(db, comms);

        db.DashboardAlerts.Add(new DashboardAlertEntity
        {
            Type = "Bottleneck",
            AlertKey = "Bottleneck:Ready",
            Severity = "Critical",
            Title = "High queue in Ready stage",
            Description = "Prior occurrence",
            Source = "test",
            RaisedAtUtc = DateTime.UtcNow.AddDays(-2),
            AcknowledgedAtUtc = DateTime.UtcNow.AddDays(-1),
            AcknowledgedBy = "test",
            EmailSentAtUtc = DateTime.UtcNow.AddDays(-2)
        });
        await db.SaveChangesAsync();

        var alert = await service.RaiseAsync(
            type: "Bottleneck",
            severity: "Critical",
            title: "Growing queue in Ready stage",
            description: "Fresh occurrence of the same Ready-stage alert",
            source: "test");

        Assert.Equal("Bottleneck:Ready", alert.AlertKey);
        Assert.Null(alert.EmailSentAtUtc);
        Assert.Equal(0, comms.SingleEmailCalls + comms.BulkEmailCalls);
    }

    [Fact]
    public async Task CriticalAlert_FirstOccurrenceUsesStableDedupeClientReference()
    {
        await using var db = NewInMemoryDb();
        var comms = new RecordingNickCommsClient();
        var service = NewService(db, comms);

        var alert = await service.RaiseAsync(
            type: "AuditPoolEmpty",
            severity: "Critical",
            title: "Audit pool empty - no auditor Ready while backlog grows",
            description: "Backlog is waiting for an auditor",
            source: "test");

        Assert.NotNull(alert.EmailSentAtUtc);
        Assert.Equal(1, comms.SingleEmailCalls);
        Assert.StartsWith("dedupe:dashboardalert:", comms.LastClientReference);
    }

    [Fact]
    public async Task CriticalAlert_DifferentAlertKeyStillQueuesEmail()
    {
        await using var db = NewInMemoryDb();
        var comms = new RecordingNickCommsClient();
        var service = NewService(db, comms);

        db.DashboardAlerts.Add(new DashboardAlertEntity
        {
            Type = "Bottleneck",
            AlertKey = "Bottleneck:Ready",
            Severity = "Critical",
            Title = "High queue in Ready stage",
            Description = "Prior occurrence",
            Source = "test",
            RaisedAtUtc = DateTime.UtcNow.AddDays(-2),
            AcknowledgedAtUtc = DateTime.UtcNow.AddDays(-1),
            AcknowledgedBy = "test",
            EmailSentAtUtc = DateTime.UtcNow.AddDays(-2)
        });
        await db.SaveChangesAsync();

        var alert = await service.RaiseAsync(
            type: "AuditPoolEmpty",
            severity: "Critical",
            title: "Audit pool empty - no auditor Ready while backlog grows",
            description: "Different critical alert",
            source: "test");

        Assert.Equal("AuditPoolEmpty", alert.AlertKey);
        Assert.NotNull(alert.EmailSentAtUtc);
        Assert.Equal(1, comms.SingleEmailCalls);
    }

    private static DashboardAlertService NewService(ApplicationDbContext db, INickCommsClient comms)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:AdminRecipients:0"] = "ops@example.com"
            })
            .Build();

        return new DashboardAlertService(
            db,
            comms,
            configuration,
            NullLogger<DashboardAlertService>.Instance);
    }

    private static ApplicationDbContext NewInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"DashboardAlertEmailDedupe_{Guid.NewGuid():N}")
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class RecordingNickCommsClient : INickCommsClient
    {
        public int SingleEmailCalls { get; private set; }
        public int BulkEmailCalls { get; private set; }
        public string? LastClientReference { get; private set; }

        public Task<NickCommsEmailResult> SendEmailAsync(
            string to,
            string subject,
            string htmlBody,
            bool isHtml = true,
            IEnumerable<NickCommsAttachment>? attachments = null,
            string? clientReference = null,
            CancellationToken ct = default)
        {
            SingleEmailCalls++;
            LastClientReference = clientReference;
            return Task.FromResult(new NickCommsEmailResult
            {
                Success = true,
                MessageId = Guid.NewGuid()
            });
        }

        public Task<NickCommsEmailResult> SendBulkEmailAsync(
            IEnumerable<string> recipients,
            string subject,
            string htmlBody,
            bool isHtml = true,
            IEnumerable<NickCommsAttachment>? attachments = null,
            string? clientReference = null,
            CancellationToken ct = default)
        {
            BulkEmailCalls++;
            LastClientReference = clientReference;
            return Task.FromResult(new NickCommsEmailResult
            {
                Success = true,
                BatchId = Guid.NewGuid(),
                AcceptedCount = recipients.Count()
            });
        }

        public Task<NickCommsSmsResult> SendSmsAsync(
            string phoneNumber,
            string message,
            string? clientReference = null,
            CancellationToken ct = default)
            => Task.FromResult(new NickCommsSmsResult { Success = true, MessageId = Guid.NewGuid() });

        public Task<NickCommsHistoryPage> GetHistoryAsync(
            NickCommsHistoryQuery query,
            CancellationToken ct = default)
            => Task.FromResult(new NickCommsHistoryPage());

        public Task<bool> PingAsync(CancellationToken ct = default)
            => Task.FromResult(true);
    }
}
