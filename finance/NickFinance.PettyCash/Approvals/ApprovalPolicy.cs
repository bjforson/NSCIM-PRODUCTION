namespace NickFinance.PettyCash.Approvals;

/// <summary>
/// In-memory representation of the Petty Cash approval policy. Built either
/// programmatically (tests, simple consumers) or by parsing the YAML DSL
/// in <see cref="ApprovalPolicyYamlLoader"/>.
/// </summary>
/// <remarks>
/// <para>
/// The policy is keyed by <see cref="VoucherCategory"/>. Each category has
/// a list of <see cref="ApprovalBand"/> rows ordered by ascending
/// <see cref="ApprovalBand.MaxAmountMinor"/>. The first band whose
/// <c>MaxAmountMinor</c> meets or exceeds the voucher amount is the
/// matching band; its <see cref="ApprovalBand.Steps"/> are the sequential
/// approval roles needed.
/// </para>
/// <para>
/// Roles are case-insensitive identifier strings (<c>line_manager</c>,
/// <c>site_supervisor</c>, <c>finance</c>, …); the host resolves a role
/// + voucher into an actual user id via <see cref="IApproverResolver"/>.
/// </para>
/// </remarks>
public sealed record ApprovalPolicy(
    string Version,
    IReadOnlyDictionary<VoucherCategory, IReadOnlyList<ApprovalBand>> Categories)
{
    /// <summary>
    /// Find the band that matches a voucher's amount. Returns
    /// <see langword="null"/> when the category has no policy or no band
    /// covers the amount — caller decides whether that's "auto-approve",
    /// "fail loudly", or "fall back to single-step".
    /// </summary>
    public ApprovalBand? BandFor(VoucherCategory category, long amountMinor)
    {
        if (!Categories.TryGetValue(category, out var bands)) return null;
        foreach (var band in bands)
        {
            if (amountMinor <= band.MaxAmountMinor) return band;
        }
        return null;
    }
}

/// <summary>One band row inside a <see cref="ApprovalPolicy"/>.</summary>
public sealed record ApprovalBand(
    long MaxAmountMinor,
    IReadOnlyList<string> Steps);
