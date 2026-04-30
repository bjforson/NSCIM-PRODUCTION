using Microsoft.EntityFrameworkCore;
using NickERP.Platform.Identity;
using NickFinance.WebApp.Identity;
using Xunit;

namespace NickERP.Platform.Identity.Tests;

[Collection("Identity")]
public sealed class RoleServiceTests
{
    private readonly IdentityFixture _fx;
    public RoleServiceTests(IdentityFixture fx) { _fx = fx; }

    [Fact]
    public async Task GetRolesAsync_returns_all_active_grants()
    {
        await using var db = _fx.CreateIdentity();
        var svc = new RoleService(db);
        var (user, _) = await SetUpUserWithRolesAsync(db, RoleNames.Custodian, RoleNames.Approver);

        var roles = await svc.GetRolesAsync(user.InternalUserId);
        Assert.Contains(RoleNames.Custodian, roles);
        Assert.Contains(RoleNames.Approver, roles);
        Assert.Equal(2, roles.Count);
    }

    [Fact]
    public async Task GetRolesAsync_excludes_expired_grants()
    {
        await using var db = _fx.CreateIdentity();
        var (user, roleIds) = await SetUpUserWithRolesAsync(db, RoleNames.Approver);
        // Backdate the Approver expiry.
        var grant = await db.UserRoles.FirstAsync(ur => ur.UserId == user.InternalUserId);
        grant.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1);
        await db.SaveChangesAsync();

        var svc = new RoleService(db);
        var roles = await svc.GetRolesAsync(user.InternalUserId);
        Assert.DoesNotContain(RoleNames.Approver, roles);
    }

    [Fact]
    public async Task HasRoleAsync_matches_role_name()
    {
        await using var db = _fx.CreateIdentity();
        var (user, _) = await SetUpUserWithRolesAsync(db, RoleNames.FinanceLead);
        var svc = new RoleService(db);
        Assert.True(await svc.HasRoleAsync(user.InternalUserId, RoleNames.FinanceLead));
        Assert.False(await svc.HasRoleAsync(user.InternalUserId, RoleNames.Auditor));
    }

    [Fact]
    public async Task HasRoleAsync_with_site_id_accepts_tenant_wide_grants_too()
    {
        await using var db = _fx.CreateIdentity();
        var (user, _) = await SetUpUserWithRolesAsync(db, RoleNames.Approver);   // tenant-wide
        var svc = new RoleService(db);
        var someSite = Guid.NewGuid();
        Assert.True(await svc.HasRoleAsync(user.InternalUserId, RoleNames.Approver, siteId: someSite));
    }

    [Fact]
    public async Task HasRoleAsync_with_site_id_distinguishes_other_sites()
    {
        await using var db = _fx.CreateIdentity();
        var siteA = Guid.NewGuid();
        var siteB = Guid.NewGuid();
        var u = AddUser(db);
        await db.SaveChangesAsync();
        var roleId = await db.Roles.Where(r => r.Name == RoleNames.Approver).Select(r => r.RoleId).FirstAsync();
        db.UserRoles.Add(new UserRole
        {
            UserId = u.InternalUserId, RoleId = roleId, SiteId = siteA,
            GrantedByUserId = Guid.NewGuid(), GrantedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = new RoleService(db);
        Assert.True(await svc.HasRoleAsync(u.InternalUserId, RoleNames.Approver, siteId: siteA));
        Assert.False(await svc.HasRoleAsync(u.InternalUserId, RoleNames.Approver, siteId: siteB));
    }

    [Fact]
    public async Task GrantRoleAsync_is_idempotent()
    {
        await using var db = _fx.CreateIdentity();
        var u = AddUser(db);
        await db.SaveChangesAsync();
        var svc = new RoleService(db);
        var actor = Guid.NewGuid();

        await svc.GrantRoleAsync(u.InternalUserId, RoleNames.Custodian, actor);
        await svc.GrantRoleAsync(u.InternalUserId, RoleNames.Custodian, actor);
        await svc.GrantRoleAsync(u.InternalUserId, RoleNames.Custodian, actor);

        var count = await db.UserRoles.CountAsync(ur => ur.UserId == u.InternalUserId);
        Assert.Equal(1, count);
    }

    private async Task<(User user, List<short> roleIds)> SetUpUserWithRolesAsync(IdentityDbContext db, params string[] roleNames)
    {
        var u = AddUser(db);
        await db.SaveChangesAsync();
        var roleIds = await db.Roles.Where(r => roleNames.Contains(r.Name)).Select(r => r.RoleId).ToListAsync();
        var actor = Guid.NewGuid();
        foreach (var rid in roleIds)
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = u.InternalUserId, RoleId = rid,
                GrantedByUserId = actor, GrantedAt = DateTimeOffset.UtcNow
            });
        }
        await db.SaveChangesAsync();
        return (u, roleIds);
    }

    private static User AddUser(IdentityDbContext db)
    {
        var u = new User
        {
            CfAccessSub = "cf-" + Guid.NewGuid().ToString("N")[..10],
            Email = $"r-{Guid.NewGuid():N}@x.com",
            DisplayName = "Role Test",
            Status = UserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            TenantId = 1
        };
        db.Users.Add(u);
        return u;
    }
}
