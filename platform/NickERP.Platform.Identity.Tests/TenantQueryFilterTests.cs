using Microsoft.EntityFrameworkCore;
using NickERP.Platform.Identity;
using NickFinance.Ledger;
using NickFinance.PettyCash;
using Xunit;

namespace NickERP.Platform.Identity.Tests;

/// <summary>
/// End-to-end check that EF query filters scope rows to the requesting
/// tenant. We seed the same row in two tenants and assert that a
/// context bound to tenant A doesn't see tenant B's row, even by id.
/// </summary>
[Collection("Identity")]
public sealed class TenantQueryFilterTests
{
    private readonly IdentityFixture _fx;
    public TenantQueryFilterTests(IdentityFixture fx) { _fx = fx; }

    [Fact]
    public async Task Floats_are_filtered_by_tenant_when_accessor_is_set()
    {
        // Insert via a no-filter context (mirrors the bootstrap CLI path).
        var siteA = Guid.NewGuid();
        var siteB = Guid.NewGuid();
        var custodian = Guid.NewGuid();

        await using (var pc = _fx.CreatePettyCash(tenant: null))
        {
            pc.Floats.Add(new Float
            {
                SiteId = siteA, CustodianUserId = custodian, CurrencyCode = "GHS",
                FloatAmountMinor = 100_00, IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = custodian, TenantId = 1
            });
            pc.Floats.Add(new Float
            {
                SiteId = siteB, CustodianUserId = custodian, CurrencyCode = "GHS",
                FloatAmountMinor = 200_00, IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = custodian, TenantId = 2
            });
            await pc.SaveChangesAsync();
        }

        // Tenant 1's view: only the tenant-1 row.
        await using (var pc = _fx.CreatePettyCash(tenant: new FixedTenantAccessor(1)))
        {
            var floats = await pc.Floats.AsNoTracking().ToListAsync();
            Assert.All(floats, f => Assert.Equal(1L, f.TenantId));
            Assert.Contains(floats, f => f.SiteId == siteA);
            Assert.DoesNotContain(floats, f => f.SiteId == siteB);
        }

        // Tenant 2's view: only the tenant-2 row.
        await using (var pc = _fx.CreatePettyCash(tenant: new FixedTenantAccessor(2)))
        {
            var floats = await pc.Floats.AsNoTracking().ToListAsync();
            Assert.All(floats, f => Assert.Equal(2L, f.TenantId));
            Assert.Contains(floats, f => f.SiteId == siteB);
            Assert.DoesNotContain(floats, f => f.SiteId == siteA);
        }
    }

    [Fact]
    public async Task Null_accessor_disables_the_filter()
    {
        // Same setup as above.
        var custodian = Guid.NewGuid();
        await using (var pc = _fx.CreatePettyCash(tenant: null))
        {
            pc.Floats.Add(new Float
            {
                SiteId = Guid.NewGuid(), CustodianUserId = custodian, CurrencyCode = "USD",
                FloatAmountMinor = 50_00, IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = custodian, TenantId = 7
            });
            pc.Floats.Add(new Float
            {
                SiteId = Guid.NewGuid(), CustodianUserId = custodian, CurrencyCode = "USD",
                FloatAmountMinor = 60_00, IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = custodian, TenantId = 8
            });
            await pc.SaveChangesAsync();
        }
        await using (var pc = _fx.CreatePettyCash(tenant: null))
        {
            var rows = await pc.Floats.AsNoTracking().Where(f => f.CurrencyCode == "USD").ToListAsync();
            Assert.True(rows.Count >= 2, "no-filter context must see all tenants");
            Assert.Contains(rows, r => r.TenantId == 7);
            Assert.Contains(rows, r => r.TenantId == 8);
        }
    }

    [Fact]
    public async Task Identity_users_filtered_by_tenant_when_accessor_is_set()
    {
        await using (var id = _fx.CreateIdentity(tenant: null))
        {
            id.Users.Add(new User
            {
                CfAccessSub = "cf-tA-" + Guid.NewGuid().ToString("N")[..8],
                Email = $"a-{Guid.NewGuid():N}@x.com", DisplayName = "Alice",
                Status = UserStatus.Active, CreatedAt = DateTimeOffset.UtcNow, TenantId = 11
            });
            id.Users.Add(new User
            {
                CfAccessSub = "cf-tB-" + Guid.NewGuid().ToString("N")[..8],
                Email = $"b-{Guid.NewGuid():N}@x.com", DisplayName = "Bob",
                Status = UserStatus.Active, CreatedAt = DateTimeOffset.UtcNow, TenantId = 12
            });
            await id.SaveChangesAsync();
        }
        await using (var id = _fx.CreateIdentity(tenant: new FixedTenantAccessor(11)))
        {
            var users = await id.Users.AsNoTracking().Where(u => u.DisplayName == "Alice" || u.DisplayName == "Bob").ToListAsync();
            Assert.Single(users);
            Assert.Equal("Alice", users[0].DisplayName);
        }
    }
}
