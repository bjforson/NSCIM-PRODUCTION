namespace NickFinance.PettyCash.Approvals;

/// <summary>
/// Multi-step engine driven by an <see cref="ApprovalPolicy"/>. For each
/// voucher it picks the matching <see cref="ApprovalBand"/> by amount,
/// then resolves each band step's role into a user via
/// <see cref="IApproverResolver"/>. Sequential approval — every step must
/// land Approved in order for the voucher to clear; any Rejection short-
/// circuits.
/// </summary>
/// <remarks>
/// Parallel-approver bands (the <c>[[role1, role2]]</c> shorthand from the
/// PETTY_CASH spec) are not yet supported — those land in v1.2 alongside
/// delegation and escalation timeouts. v1.1 is sequential-only.
/// </remarks>
public sealed class PolicyApprovalEngine : IApprovalEngine
{
    private readonly ApprovalPolicy _policy;
    private readonly IApproverResolver _resolver;
    private readonly TimeProvider _clock;

    public PolicyApprovalEngine(
        ApprovalPolicy policy,
        IApproverResolver resolver,
        TimeProvider? clock = null)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _clock = clock ?? TimeProvider.System;
    }

    public IReadOnlyList<VoucherApproval> Plan(Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(voucher);

        var band = _policy.BandFor(voucher.Category, voucher.AmountRequestedMinor)
            ?? throw new PettyCashException(
                $"No approval band covers a {voucher.AmountRequestedMinor}-minor "
                + $"{voucher.Category} voucher under policy version {_policy.Version}. "
                + "Either the amount exceeds the largest band or the category isn't policy-mapped.");

        var now = _clock.GetUtcNow();
        var steps = new List<VoucherApproval>(band.Steps.Count);
        short n = 1;
        foreach (var role in band.Steps)
        {
            var assignee = _resolver.Resolve(role, voucher);
            // Separation of duties — the requester can never appear in their
            // own chain. We mark such a step UnfillableRole so the service
            // can pre-reject the voucher and a human can investigate (the
            // requester probably submitted under the wrong category).
            var unfillable = assignee == Guid.Empty || assignee == voucher.RequesterUserId;

            steps.Add(new VoucherApproval
            {
                VoucherId = voucher.VoucherId,
                StepNo = n++,
                Role = role,
                AssignedToUserId = unfillable ? Guid.Empty : assignee,
                Decision = unfillable ? ApprovalDecision.UnfillableRole : ApprovalDecision.Pending,
                CreatedAt = now,
                TenantId = voucher.TenantId
            });
        }
        return steps;
    }

    public ApprovalAdvance Decide(
        Voucher voucher,
        IReadOnlyList<VoucherApproval> steps,
        Guid deciderUserId,
        string? comment,
        bool reject)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        ArgumentNullException.ThrowIfNull(steps);

        // If any step was unfillable we should never have reached "Decide"
        // for that voucher — the service is supposed to mark it Rejected
        // up-front.
        if (steps.Any(s => s.Decision == ApprovalDecision.UnfillableRole))
        {
            throw new PettyCashException("Voucher has an unfillable approval step; cannot decide.");
        }

        // Locate the next pending step in order.
        var step = steps
            .Where(s => s.Decision == ApprovalDecision.Pending)
            .OrderBy(s => s.StepNo)
            .FirstOrDefault()
            ?? throw new PettyCashException("No pending approval step found.");

        if (step.AssignedToUserId != deciderUserId)
        {
            throw new SeparationOfDutiesException(
                $"Step {step.StepNo} ({step.Role}) is assigned to {step.AssignedToUserId}; user {deciderUserId} cannot decide it.");
        }
        if (deciderUserId == voucher.RequesterUserId)
        {
            // Defence in depth — Plan() already excluded this case.
            throw new SeparationOfDutiesException("The requester cannot decide on their own voucher.");
        }

        step.DecidedByUserId = deciderUserId;
        step.Comment = comment;
        step.DecidedAt = _clock.GetUtcNow();
        step.Decision = reject ? ApprovalDecision.Rejected : ApprovalDecision.Approved;

        if (reject)
        {
            return new ApprovalAdvance(ApprovalAdvanceOutcome.Rejected, step.StepNo);
        }

        var anyPendingLeft = steps.Any(s => s.Decision == ApprovalDecision.Pending);
        return new ApprovalAdvance(
            anyPendingLeft ? ApprovalAdvanceOutcome.StillInApproval : ApprovalAdvanceOutcome.FullyApproved,
            step.StepNo);
    }
}
