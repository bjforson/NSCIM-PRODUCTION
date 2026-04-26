using Microsoft.EntityFrameworkCore;
using NickFinance.Ledger;
using NickFinance.PettyCash;
using NickFinance.PettyCash.Approvals;
using Xunit;

namespace NickFinance.PettyCash.Tests;

[Collection("PettyCash")]
public class ApprovalPolicyTests
{
    private static Money Ghs(long minor) => new(minor, "GHS");
    private readonly PettyCashFixture _fx;
    public ApprovalPolicyTests(PettyCashFixture fx) => _fx = fx;

    // ------------------------------------------------------------------
    // YAML loader
    // ------------------------------------------------------------------

    [Fact]
    public void Yaml_Parses_BasicShape()
    {
        var yaml = """
            version: "2026-04-26"
            categories:
              Transport:
                bands:
                  - { max: 20000,  steps: [line_manager] }
                  - { max: 100000, steps: [line_manager, site_supervisor] }
                  - { max: 500000, steps: [line_manager, site_supervisor, finance] }
              Fuel:
                bands:
                  - { max: 50000,  steps: [site_supervisor] }
                  - { max: 300000, steps: [site_supervisor, finance] }
            """;

        var p = ApprovalPolicyYamlLoader.Load(yaml);

        Assert.Equal("2026-04-26", p.Version);
        Assert.Equal(2, p.Categories.Count);

        var transportBands = p.Categories[VoucherCategory.Transport];
        Assert.Equal(3, transportBands.Count);
        Assert.Equal(20000, transportBands[0].MaxAmountMinor);
        Assert.Equal(new[] { "line_manager" }, transportBands[0].Steps);
        Assert.Equal(new[] { "line_manager", "site_supervisor", "finance" }, transportBands[2].Steps);
    }

    [Fact]
    public void Yaml_Rejects_AscendingBandViolation()
    {
        var yaml = """
            version: "v1"
            categories:
              Transport:
                bands:
                  - { max: 100000, steps: [line_manager] }
                  - { max: 50000,  steps: [finance] }
            """;
        Assert.Throws<PettyCashException>(() => ApprovalPolicyYamlLoader.Load(yaml));
    }

    [Fact]
    public void Yaml_Rejects_UnknownCategory()
    {
        var yaml = """
            version: "v1"
            categories:
              Bogus:
                bands:
                  - { max: 1000, steps: [line_manager] }
            """;
        Assert.Throws<PettyCashException>(() => ApprovalPolicyYamlLoader.Load(yaml));
    }

    [Fact]
    public void Yaml_IgnoresUnknownTopLevelKeys()
    {
        var yaml = """
            version: "v1"
            defaults:
              currency: GHS
              escalation_hours: 48
            delegation:
              allowed_for_roles: [line_manager, finance]
            categories:
              Transport:
                bands:
                  - { max: 1000, steps: [line_manager] }
            """;
        var p = ApprovalPolicyYamlLoader.Load(yaml);
        Assert.Equal("v1", p.Version);
    }

    [Fact]
    public void BandFor_PicksFirstMatchingByAscendingMax()
    {
        var p = new ApprovalPolicy("v", new Dictionary<VoucherCategory, IReadOnlyList<ApprovalBand>>
        {
            [VoucherCategory.Transport] = new[]
            {
                new ApprovalBand(20000, new[] { "line_manager" }),
                new ApprovalBand(100000, new[] { "line_manager", "site_supervisor" }),
                new ApprovalBand(500000, new[] { "line_manager", "site_supervisor", "finance" })
            }
        });
        Assert.Equal(20000, p.BandFor(VoucherCategory.Transport, 19_999)!.MaxAmountMinor);
        Assert.Equal(20000, p.BandFor(VoucherCategory.Transport, 20_000)!.MaxAmountMinor);
        Assert.Equal(100000, p.BandFor(VoucherCategory.Transport, 20_001)!.MaxAmountMinor);
        Assert.Null(p.BandFor(VoucherCategory.Transport, 500_001));
        Assert.Null(p.BandFor(VoucherCategory.Fuel, 100));
    }

    // ------------------------------------------------------------------
    // PolicyApprovalEngine end-to-end via PettyCashService
    // ------------------------------------------------------------------

    [Fact]
    public async Task ThreeStep_HappyPath_AllStepsApproveAndVoucherDisburses()
    {
        var ctx = await BuildContext();
        var (svc, fl, requester, lm, ss, finance, custodian, periodId, pc, lg) =
            (ctx.Svc, ctx.Fl, ctx.Requester, ctx.LineManager, ctx.SiteSupervisor, ctx.Finance, ctx.Custodian, ctx.PeriodId, ctx.PcCtx, ctx.LgCtx);

        // GHS 2,500 (250 000 pesewa) -> three-step band (line_manager, site_supervisor, finance)
        var v = await svc.SubmitVoucherAsync(new SubmitVoucherRequest(
            fl.FloatId, requester, VoucherCategory.Transport, "trip",
            Ghs(250_000),
            new[] { new VoucherLineInput("a", Ghs(250_000)) }));
        Assert.Equal(VoucherStatus.Submitted, v.Status);

        // Step 1 — line manager approves; voucher stays Submitted
        v = await svc.ApproveVoucherAsync(v.VoucherId, lm, null, "lm OK");
        Assert.Equal(VoucherStatus.Submitted, v.Status);

        // Step 2 — site supervisor approves; voucher stays Submitted
        v = await svc.ApproveVoucherAsync(v.VoucherId, ss, null, "ss OK");
        Assert.Equal(VoucherStatus.Submitted, v.Status);

        // Step 3 — finance approves; voucher transitions to Approved
        v = await svc.ApproveVoucherAsync(v.VoucherId, finance, null, "fin OK");
        Assert.Equal(VoucherStatus.Approved, v.Status);
        Assert.Equal(250_000, v.AmountApprovedMinor);

        // Disburse — non-approver custodian
        v = await svc.DisburseVoucherAsync(v.VoucherId, custodian, new DateOnly(2026, 4, 15), periodId);
        Assert.Equal(VoucherStatus.Disbursed, v.Status);
        Assert.NotNull(v.LedgerEventId);

        // All three approval rows are Approved.
        await using var pc2 = _fx.CreatePettyCash();
        var rows = await pc2.VoucherApprovals
            .Where(s => s.VoucherId == v.VoucherId)
            .OrderBy(s => s.StepNo).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(ApprovalDecision.Approved, r.Decision));
        Assert.Equal(new Guid?[] { lm, ss, finance }, rows.Select(r => r.DecidedByUserId).ToArray());

        await pc.DisposeAsync(); await lg.DisposeAsync();
    }

    [Fact]
    public async Task RejectionMidChain_ShortCircuitsToRejected()
    {
        var ctx = await BuildContext();
        var (svc, fl, requester, lm, ss, finance, pc, lg) =
            (ctx.Svc, ctx.Fl, ctx.Requester, ctx.LineManager, ctx.SiteSupervisor, ctx.Finance, ctx.PcCtx, ctx.LgCtx);

        var v = await svc.SubmitVoucherAsync(new SubmitVoucherRequest(
            fl.FloatId, requester, VoucherCategory.Transport, "trip",
            Ghs(250_000),
            new[] { new VoucherLineInput("a", Ghs(250_000)) }));

        // Step 1 approves
        await svc.ApproveVoucherAsync(v.VoucherId, lm, null, "lm OK");
        // Step 2 rejects -> voucher rejected, step 3 stays pending forever
        v = await svc.RejectVoucherAsync(v.VoucherId, ss, "wrong category");
        Assert.Equal(VoucherStatus.Rejected, v.Status);

        await using var pc2 = _fx.CreatePettyCash();
        var rows = await pc2.VoucherApprovals
            .Where(s => s.VoucherId == v.VoucherId)
            .OrderBy(s => s.StepNo).ToListAsync();
        Assert.Equal(ApprovalDecision.Approved, rows[0].Decision);
        Assert.Equal(ApprovalDecision.Rejected, rows[1].Decision);
        Assert.Equal(ApprovalDecision.Pending,  rows[2].Decision);   // never decided

        // Step 3 finance can't approve a Rejected voucher.
        await Assert.ThrowsAsync<InvalidVoucherTransitionException>(() =>
            svc.ApproveVoucherAsync(v.VoucherId, finance, null, null));

        await pc.DisposeAsync(); await lg.DisposeAsync();
    }

    [Fact]
    public async Task WrongApproverForStep_IsRejectedAsSeparationOfDuties()
    {
        var ctx = await BuildContext();
        var (svc, fl, requester, finance, pc, lg) =
            (ctx.Svc, ctx.Fl, ctx.Requester, ctx.Finance, ctx.PcCtx, ctx.LgCtx);

        var v = await svc.SubmitVoucherAsync(new SubmitVoucherRequest(
            fl.FloatId, requester, VoucherCategory.Transport, "trip",
            Ghs(250_000),
            new[] { new VoucherLineInput("a", Ghs(250_000)) }));

        // Step 1 expects line_manager — finance can't jump the queue.
        await Assert.ThrowsAsync<SeparationOfDutiesException>(() =>
            svc.ApproveVoucherAsync(v.VoucherId, finance, null, null));

        await pc.DisposeAsync(); await lg.DisposeAsync();
    }

    [Fact]
    public async Task BandSelection_PicksByAmount()
    {
        var ctx = await BuildContext();
        var (svc, fl, requester, lm, pc, lg) =
            (ctx.Svc, ctx.Fl, ctx.Requester, ctx.LineManager, ctx.PcCtx, ctx.LgCtx);

        // GHS 100 (10 000 pesewa) -> single-step band (line_manager only)
        var v = await svc.SubmitVoucherAsync(new SubmitVoucherRequest(
            fl.FloatId, requester, VoucherCategory.Transport, "small trip",
            Ghs(10_000),
            new[] { new VoucherLineInput("cab", Ghs(10_000)) }));

        // Line manager alone can clear it.
        v = await svc.ApproveVoucherAsync(v.VoucherId, lm, null, null);
        Assert.Equal(VoucherStatus.Approved, v.Status);

        await pc.DisposeAsync(); await lg.DisposeAsync();
    }

    [Fact]
    public async Task UnfillableRole_AutoRejectsAtSubmit()
    {
        // Build a resolver that knows lm + ss but NOT finance.
        var lm = Guid.NewGuid();
        var ss = Guid.NewGuid();
        var resolver = new StaticApproverResolver(new Dictionary<string, Guid>
        {
            ["line_manager"] = lm,
            ["site_supervisor"] = ss,
            // ["finance"] missing -> Resolve("finance") returns Guid.Empty
        });
        var policy = ApprovalPolicyYamlLoader.Load("""
            version: "v1"
            categories:
              Transport:
                bands:
                  - { max: 500000, steps: [line_manager, site_supervisor, finance] }
            """);

        var pc = _fx.CreatePettyCash();
        var lg = _fx.CreateLedger();
        var period = await new PeriodService(lg).CreateAsync(2026, 7);
        var svc = new PettyCashService(pc, new LedgerWriter(lg), new PolicyApprovalEngine(policy, resolver));

        var requester = Guid.NewGuid();
        var fl = await svc.CreateFloatAsync(Guid.NewGuid(), Guid.NewGuid(), Ghs(1_000_000), Guid.NewGuid());
        var v = await svc.SubmitVoucherAsync(new SubmitVoucherRequest(
            fl.FloatId, requester, VoucherCategory.Transport, "trip",
            Ghs(250_000),
            new[] { new VoucherLineInput("a", Ghs(250_000)) }));

        Assert.Equal(VoucherStatus.Rejected, v.Status);
        Assert.Contains("could not be filled", v.DecisionComment, StringComparison.OrdinalIgnoreCase);

        await pc.DisposeAsync(); await lg.DisposeAsync();
    }

    [Fact]
    public async Task RequesterAsApprover_IsAlwaysExcludedFromChain()
    {
        // Build a resolver where the requester IS the line_manager.
        var requester = Guid.NewGuid();
        var ss = Guid.NewGuid();
        var finance = Guid.NewGuid();
        var resolver = new StaticApproverResolver(new Dictionary<string, Guid>
        {
            ["line_manager"] = requester,   // <-- the requester themselves!
            ["site_supervisor"] = ss,
            ["finance"] = finance
        });
        var policy = ApprovalPolicyYamlLoader.Load("""
            version: "v1"
            categories:
              Transport:
                bands:
                  - { max: 500000, steps: [line_manager, site_supervisor, finance] }
            """);

        var pc = _fx.CreatePettyCash();
        var lg = _fx.CreateLedger();
        await new PeriodService(lg).CreateAsync(2026, 8);
        var svc = new PettyCashService(pc, new LedgerWriter(lg), new PolicyApprovalEngine(policy, resolver));

        var fl = await svc.CreateFloatAsync(Guid.NewGuid(), Guid.NewGuid(), Ghs(1_000_000), Guid.NewGuid());
        var v = await svc.SubmitVoucherAsync(new SubmitVoucherRequest(
            fl.FloatId, requester, VoucherCategory.Transport, "trip",
            Ghs(250_000),
            new[] { new VoucherLineInput("a", Ghs(250_000)) }));

        // line_manager step is unfillable (requester=approver) so the
        // voucher is auto-rejected.
        Assert.Equal(VoucherStatus.Rejected, v.Status);

        await pc.DisposeAsync(); await lg.DisposeAsync();
    }

    [Fact]
    public async Task NoBandCoversAmount_ThrowsAtSubmit()
    {
        var pc = _fx.CreatePettyCash();
        var lg = _fx.CreateLedger();
        await new PeriodService(lg).CreateAsync(2026, 9);

        var policy = ApprovalPolicyYamlLoader.Load("""
            version: "v1"
            categories:
              Transport:
                bands:
                  - { max: 100000, steps: [line_manager] }
            """);
        var resolver = new StaticApproverResolver(new Dictionary<string, Guid>
        {
            ["line_manager"] = Guid.NewGuid()
        });
        var svc = new PettyCashService(pc, new LedgerWriter(lg), new PolicyApprovalEngine(policy, resolver));
        var fl = await svc.CreateFloatAsync(Guid.NewGuid(), Guid.NewGuid(), Ghs(10_000_000), Guid.NewGuid());

        // Above the largest band (100_000) -> engine throws.
        await Assert.ThrowsAsync<PettyCashException>(() =>
            svc.SubmitVoucherAsync(new SubmitVoucherRequest(
                fl.FloatId, Guid.NewGuid(), VoucherCategory.Transport, "huge trip",
                Ghs(500_000),
                new[] { new VoucherLineInput("a", Ghs(500_000)) })));

        await pc.DisposeAsync(); await lg.DisposeAsync();
    }

    // ------------------------------------------------------------------
    // Helpers — set up a happy-path service with a 3-step Transport policy
    // ------------------------------------------------------------------

    private async Task<HappyPathContext> BuildContext()
    {
        var pc = _fx.CreatePettyCash();
        var lg = _fx.CreateLedger();
        var period = await new PeriodService(lg).CreateAsync(2026, 4);

        var lm = Guid.NewGuid();
        var ss = Guid.NewGuid();
        var fin = Guid.NewGuid();
        var resolver = new StaticApproverResolver(new Dictionary<string, Guid>
        {
            ["line_manager"] = lm,
            ["site_supervisor"] = ss,
            ["finance"] = fin
        });
        var policy = ApprovalPolicyYamlLoader.Load("""
            version: "2026-04-26"
            categories:
              Transport:
                bands:
                  - { max: 20000,  steps: [line_manager] }
                  - { max: 100000, steps: [line_manager, site_supervisor] }
                  - { max: 500000, steps: [line_manager, site_supervisor, finance] }
              Fuel:
                bands:
                  - { max: 50000,  steps: [site_supervisor] }
                  - { max: 300000, steps: [site_supervisor, finance] }
            """);

        var svc = new PettyCashService(pc, new LedgerWriter(lg), new PolicyApprovalEngine(policy, resolver));
        var requester = Guid.NewGuid();
        var custodian = Guid.NewGuid();
        var fl = await svc.CreateFloatAsync(Guid.NewGuid(), custodian, Ghs(1_000_000), Guid.NewGuid());

        return new HappyPathContext(svc, fl, requester, lm, ss, fin, custodian, period.PeriodId, pc, lg);
    }

    private sealed record HappyPathContext(
        PettyCashService Svc,
        Float Fl,
        Guid Requester,
        Guid LineManager,
        Guid SiteSupervisor,
        Guid Finance,
        Guid Custodian,
        Guid PeriodId,
        PettyCashDbContext PcCtx,
        LedgerDbContext LgCtx);
}
