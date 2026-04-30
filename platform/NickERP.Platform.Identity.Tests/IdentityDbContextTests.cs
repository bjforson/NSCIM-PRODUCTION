using Microsoft.EntityFrameworkCore;
using NickERP.Platform.Identity;
using Xunit;

namespace NickERP.Platform.Identity.Tests;

[Collection("Identity")]
public sealed class IdentityDbContextTests
{
    private readonly IdentityFixture _fx;
    public IdentityDbContextTests(IdentityFixture fx) { _fx = fx; }

    [Fact]
    public async Task Migration_creates_identity_schema()
    {
        await using var db = _fx.CreateIdentity();
        var sql = "SELECT 1 FROM information_schema.schemata WHERE schema_name = 'identity'";
        var rows = await db.Database.SqlQueryRaw<int>(sql).ToListAsync();
        Assert.Single(rows);
    }

    [Fact]
    public async Task Migration_seeds_six_canonical_roles()
    {
        await using var db = _fx.CreateIdentity();
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.RoleId).ToListAsync();
        Assert.Equal(6, roles.Count);
        Assert.Contains(roles, r => r.Name == RoleNames.Custodian);
        Assert.Contains(roles, r => r.Name == RoleNames.Approver);
        Assert.Contains(roles, r => r.Name == RoleNames.SiteManager);
        Assert.Contains(roles, r => r.Name == RoleNames.FinanceLead);
        Assert.Contains(roles, r => r.Name == RoleNames.Auditor);
        Assert.Contains(roles, r => r.Name == RoleNames.Admin);
    }

    [Fact]
    public async Task Roles_have_stable_ids_one_through_six()
    {
        await using var db = _fx.CreateIdentity();
        var byName = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Name, r => r.RoleId);
        Assert.Equal((short)1, byName[RoleNames.Custodian]);
        Assert.Equal((short)2, byName[RoleNames.Approver]);
        Assert.Equal((short)3, byName[RoleNames.SiteManager]);
        Assert.Equal((short)4, byName[RoleNames.FinanceLead]);
        Assert.Equal((short)5, byName[RoleNames.Auditor]);
        Assert.Equal((short)6, byName[RoleNames.Admin]);
    }

    [Fact]
    public async Task User_can_be_created_and_round_tripped()
    {
        await using var db = _fx.CreateIdentity();
        var u = new User
        {
            CfAccessSub = "cf-sub-" + Guid.NewGuid().ToString("N")[..8],
            Email = $"u-{Guid.NewGuid():N}@x.com",
            DisplayName = "Round Trip",
            Status = UserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            TenantId = 1
        };
        db.Users.Add(u);
        await db.SaveChangesAsync();

        var loaded = await db.Users.AsNoTracking().FirstAsync(x => x.InternalUserId == u.InternalUserId);
        Assert.Equal("Round Trip", loaded.DisplayName);
        Assert.Equal(UserStatus.Active, loaded.Status);
    }

    [Fact]
    public async Task UserPhone_unique_constraint_blocks_duplicate_e164()
    {
        await using var db = _fx.CreateIdentity();
        var u1 = AddSyntheticUser(db);
        var u2 = AddSyntheticUser(db);
        await db.SaveChangesAsync();

        var phone = "+233244" + Random.Shared.Next(100000, 999999);
        db.UserPhones.Add(new UserPhone { UserId = u1.InternalUserId, PhoneE164 = phone, Verified = false, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        db.UserPhones.Add(new UserPhone { UserId = u2.InternalUserId, PhoneE164 = phone, Verified = false, CreatedAt = DateTimeOffset.UtcNow });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static User AddSyntheticUser(IdentityDbContext db, long tenantId = 1)
    {
        var u = new User
        {
            CfAccessSub = "cf-sub-" + Guid.NewGuid().ToString("N")[..10],
            Email = $"u-{Guid.NewGuid():N}@x.com",
            DisplayName = "Synth",
            Status = UserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            TenantId = tenantId
        };
        db.Users.Add(u);
        return u;
    }
}
