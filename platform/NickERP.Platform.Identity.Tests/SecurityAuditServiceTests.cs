using Microsoft.EntityFrameworkCore;
using NickERP.Platform.Identity;
using Xunit;

namespace NickERP.Platform.Identity.Tests;

/// <summary>
/// Direct-write tests against the audit log table. The full
/// <c>SecurityAuditService</c> needs an HTTP context to derive the
/// actor; here we exercise the table shape and the no-op fallback,
/// which is what the kernel modules use.
/// </summary>
[Collection("Identity")]
public sealed class SecurityAuditServiceTests
{
    private readonly IdentityFixture _fx;
    public SecurityAuditServiceTests(IdentityFixture fx) { _fx = fx; }

    [Fact]
    public async Task Audit_event_round_trips_with_full_field_coverage()
    {
        await using var db = _fx.CreateIdentity();
        var ev = new SecurityAuditEvent
        {
            UserId = Guid.NewGuid(),
            Action = SecurityAuditAction.VoucherApproved,
            TargetType = "Voucher",
            TargetId = Guid.NewGuid().ToString(),
            Ip = "127.0.0.1",
            UserAgent = "xunit",
            OccurredAt = DateTimeOffset.UtcNow,
            DetailsJson = "{\"voucherNo\":\"PC-X\"}",
            Result = SecurityAuditResult.Allowed,
            TenantId = 1
        };
        db.SecurityAuditEvents.Add(ev);
        await db.SaveChangesAsync();

        var loaded = await db.SecurityAuditEvents.AsNoTracking().FirstAsync(e => e.EventId == ev.EventId);
        Assert.Equal(SecurityAuditAction.VoucherApproved, loaded.Action);
        Assert.Equal(SecurityAuditResult.Allowed, loaded.Result);
        Assert.Equal("xunit", loaded.UserAgent);
        Assert.Contains("PC-X", loaded.DetailsJson);
    }

    [Fact]
    public async Task Noop_audit_service_completes_without_db_write()
    {
        ISecurityAuditService noop = new NoopSecurityAuditService();
        // Should not throw; should not write anything.
        await noop.RecordAsync(SecurityAuditAction.Login, "User", Guid.NewGuid().ToString());
        await using var db = _fx.CreateIdentity();
        // Expect zero login rows from this test (we wrote via the no-op).
        var anyLogin = await db.SecurityAuditEvents.AsNoTracking().AnyAsync(e => e.Action == SecurityAuditAction.Login);
        Assert.False(anyLogin);
    }
}
