using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NickERP.Platform.Identity;
using NickFinance.WebApp.Identity;
using Xunit;

namespace NickERP.Platform.Identity.Tests;

/// <summary>
/// Verifies the policy registration matches the role-set documented in
/// the brief. We resolve each policy from the registered
/// <see cref="IAuthorizationPolicyProvider"/> and inspect the
/// <see cref="RoleRequirement"/> attached to it. The handler is tested
/// separately via integration; here we just assert the wiring.
/// </summary>
public sealed class PolicyTests
{
    private static IAuthorizationPolicyProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNickFinanceAuthorization();
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IAuthorizationPolicyProvider>();
    }

    [Theory]
    [InlineData(Policies.SubmitVoucher,   new[] { RoleNames.Custodian, RoleNames.Approver, RoleNames.SiteManager, RoleNames.FinanceLead, RoleNames.Admin })]
    [InlineData(Policies.ApproveVoucher,  new[] { RoleNames.Approver, RoleNames.SiteManager, RoleNames.FinanceLead, RoleNames.Admin })]
    [InlineData(Policies.DisburseVoucher, new[] { RoleNames.Custodian, RoleNames.FinanceLead, RoleNames.Admin })]
    [InlineData(Policies.IssueInvoice,    new[] { RoleNames.FinanceLead, RoleNames.Admin })]
    [InlineData(Policies.VoidInvoice,     new[] { RoleNames.FinanceLead, RoleNames.Admin })]
    [InlineData(Policies.RecordReceipt,   new[] { RoleNames.FinanceLead, RoleNames.Admin })]
    [InlineData(Policies.PostJournal,     new[] { RoleNames.FinanceLead, RoleNames.Admin })]
    [InlineData(Policies.ClosePeriod,     new[] { RoleNames.FinanceLead, RoleNames.Admin })]
    [InlineData(Policies.ManageFloats,    new[] { RoleNames.Admin })]
    [InlineData(Policies.ManageUsers,     new[] { RoleNames.Admin })]
    [InlineData(Policies.ViewAudit,       new[] { RoleNames.Auditor, RoleNames.Admin })]
    [InlineData(Policies.Admin,           new[] { RoleNames.Admin })]
    public async Task Policy_resolves_to_expected_role_set(string policyName, string[] expectedRoles)
    {
        var provider = BuildProvider();
        var policy = await provider.GetPolicyAsync(policyName);
        Assert.NotNull(policy);
        var req = policy!.Requirements.OfType<RoleRequirement>().Single();
        Assert.Equal(expectedRoles.Order(), req.AllowedRoles.Order());
    }

    [Fact]
    public async Task ViewReports_requires_only_authentication()
    {
        var provider = BuildProvider();
        var policy = await provider.GetPolicyAsync(Policies.ViewReports);
        Assert.NotNull(policy);
        // ViewReports has no role requirement — only the standard "must be authenticated".
        Assert.DoesNotContain(policy!.Requirements, r => r is RoleRequirement);
    }
}
