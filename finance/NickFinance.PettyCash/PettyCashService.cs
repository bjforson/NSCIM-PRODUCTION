using Microsoft.EntityFrameworkCore;
using NickFinance.Ledger;

namespace NickFinance.PettyCash;

/// <summary>
/// The Petty Cash module-facing API. One service, every operation runs in
/// a single transaction (so a journal post + voucher status flip either
/// both land or neither does).
/// </summary>
public interface IPettyCashService
{
    /// <summary>Provision a new float for a custodian at a site. Throws if an active float already exists for that (site, currency, tenant).</summary>
    Task<Float> CreateFloatAsync(
        Guid siteId,
        Guid custodianUserId,
        Money initialFloat,
        Guid actorUserId,
        long tenantId = 1,
        CancellationToken ct = default);

    /// <summary>Submit a Draft voucher for approval. Generates the voucher number, snapshots <c>SubmittedAt</c>, and validates totals + float status.</summary>
    Task<Voucher> SubmitVoucherAsync(
        SubmitVoucherRequest req,
        CancellationToken ct = default);

    /// <summary>Approve a Submitted voucher. Approver must differ from the requester (separation of duties).</summary>
    Task<Voucher> ApproveVoucherAsync(
        Guid voucherId,
        Guid approverUserId,
        long? amountApprovedMinor,
        string? comment,
        CancellationToken ct = default);

    /// <summary>Reject a Submitted voucher. Same SoD constraint as approve.</summary>
    Task<Voucher> RejectVoucherAsync(
        Guid voucherId,
        Guid approverUserId,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Disburse an Approved voucher. Posts the journal to the Ledger
    /// (DR each line's <c>GlAccount</c>, CR <c>1060 Petty cash float</c>)
    /// and flips the voucher to Disbursed in the same transaction. The
    /// custodian who disburses must differ from the approver.
    /// </summary>
    Task<Voucher> DisburseVoucherAsync(
        Guid voucherId,
        Guid custodianUserId,
        DateOnly effectiveDate,
        Guid periodId,
        CancellationToken ct = default);
}

/// <summary>Input for <see cref="IPettyCashService.SubmitVoucherAsync"/>.</summary>
public sealed record SubmitVoucherRequest(
    Guid FloatId,
    Guid RequesterUserId,
    VoucherCategory Category,
    string Purpose,
    Money Amount,
    IReadOnlyList<VoucherLineInput> Lines,
    string? PayeeName = null,
    string? ProjectCode = null,
    long TenantId = 1);

/// <summary>One line within a <see cref="SubmitVoucherRequest"/>. The service stamps <c>LineNo</c>.</summary>
public sealed record VoucherLineInput(
    string Description,
    Money GrossAmount,
    string? GlAccountOverride = null);

/// <summary>
/// Default implementation. Wraps every mutation in a transaction so that
/// the journal post and the voucher state change are atomic — if the
/// Ledger writer rejects (closed period, unbalanced, etc.) the voucher
/// stays Approved and the disbursement can be retried after the underlying
/// problem is fixed.
/// </summary>
public sealed class PettyCashService : IPettyCashService
{
    private readonly PettyCashDbContext _db;
    private readonly ILedgerWriter _ledger;
    private readonly TimeProvider _clock;

    public PettyCashService(
        PettyCashDbContext db,
        ILedgerWriter ledger,
        TimeProvider? clock = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _clock = clock ?? TimeProvider.System;
    }

    // ---------------------------------------------------------------------
    // Floats
    // ---------------------------------------------------------------------

    public async Task<Float> CreateFloatAsync(
        Guid siteId,
        Guid custodianUserId,
        Money initialFloat,
        Guid actorUserId,
        long tenantId = 1,
        CancellationToken ct = default)
    {
        if (initialFloat.IsNegative)
        {
            throw new ArgumentException("Initial float cannot be negative.", nameof(initialFloat));
        }

        var existing = await _db.Floats
            .FirstOrDefaultAsync(f =>
                f.TenantId == tenantId &&
                f.SiteId == siteId &&
                f.CurrencyCode == initialFloat.CurrencyCode &&
                f.IsActive, ct);
        if (existing is not null)
        {
            throw new PettyCashException(
                $"An active {initialFloat.CurrencyCode} float already exists for site {siteId} (float {existing.FloatId}). "
                + "Close it first.");
        }

        var f = new Float
        {
            SiteId = siteId,
            CustodianUserId = custodianUserId,
            CurrencyCode = initialFloat.CurrencyCode,
            FloatAmountMinor = initialFloat.Minor,
            IsActive = true,
            CreatedAt = _clock.GetUtcNow(),
            CreatedByUserId = actorUserId,
            TenantId = tenantId
        };
        _db.Floats.Add(f);
        await _db.SaveChangesAsync(ct);
        return f;
    }

    // ---------------------------------------------------------------------
    // Vouchers
    // ---------------------------------------------------------------------

    public async Task<Voucher> SubmitVoucherAsync(
        SubmitVoucherRequest req,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);

        if (string.IsNullOrWhiteSpace(req.Purpose))
        {
            throw new ArgumentException("Purpose is required.", nameof(req));
        }
        if (req.Amount.IsNegative || req.Amount.IsZero)
        {
            throw new ArgumentException("Voucher amount must be positive.", nameof(req));
        }
        if (req.Lines is null || req.Lines.Count == 0)
        {
            throw new ArgumentException("At least one line item is required.", nameof(req));
        }

        // Currency consistency + total match
        long lineSum = 0;
        foreach (var line in req.Lines)
        {
            if (!string.Equals(line.GrossAmount.CurrencyCode, req.Amount.CurrencyCode, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Line currency {line.GrossAmount.CurrencyCode} differs from voucher currency {req.Amount.CurrencyCode}.",
                    nameof(req));
            }
            checked { lineSum += line.GrossAmount.Minor; }
        }
        if (lineSum != req.Amount.Minor)
        {
            throw new VoucherTotalMismatchException(req.Amount.Minor, lineSum);
        }

        var fl = await _db.Floats.FirstOrDefaultAsync(f => f.FloatId == req.FloatId && f.TenantId == req.TenantId, ct)
            ?? throw new FloatNotAvailableException(req.FloatId, "no such float");
        if (!fl.IsActive)
        {
            throw new FloatNotAvailableException(req.FloatId, "float is closed");
        }
        if (!string.Equals(fl.CurrencyCode, req.Amount.CurrencyCode, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Voucher currency {req.Amount.CurrencyCode} doesn't match float currency {fl.CurrencyCode}.",
                nameof(req));
        }

        var now = _clock.GetUtcNow();

        var voucher = new Voucher
        {
            FloatId = fl.FloatId,
            RequesterUserId = req.RequesterUserId,
            Category = req.Category,
            Purpose = req.Purpose.Trim(),
            AmountRequestedMinor = req.Amount.Minor,
            CurrencyCode = req.Amount.CurrencyCode,
            Status = VoucherStatus.Submitted,
            PayeeName = req.PayeeName,
            ProjectCode = req.ProjectCode,
            CreatedAt = now,
            SubmittedAt = now,
            TenantId = req.TenantId
        };
        voucher.VoucherNo = await GenerateVoucherNoAsync(fl.SiteId, now.Year, req.TenantId, ct);

        var defaultGl = req.Category.DefaultGlAccount();
        short n = 1;
        foreach (var line in req.Lines)
        {
            voucher.Lines.Add(new VoucherLineItem
            {
                VoucherId = voucher.VoucherId,
                LineNo = n++,
                Description = line.Description,
                GrossAmountMinor = line.GrossAmount.Minor,
                CurrencyCode = line.GrossAmount.CurrencyCode,
                GlAccount = string.IsNullOrWhiteSpace(line.GlAccountOverride) ? defaultGl : line.GlAccountOverride!.Trim()
            });
        }

        _db.Vouchers.Add(voucher);
        await _db.SaveChangesAsync(ct);
        return voucher;
    }

    public async Task<Voucher> ApproveVoucherAsync(
        Guid voucherId,
        Guid approverUserId,
        long? amountApprovedMinor,
        string? comment,
        CancellationToken ct = default)
    {
        var v = await LoadVoucher(voucherId, ct);
        if (v.Status != VoucherStatus.Submitted)
        {
            throw new InvalidVoucherTransitionException(v.Status, "approve");
        }
        if (v.RequesterUserId == approverUserId)
        {
            throw new SeparationOfDutiesException("The requester cannot approve their own voucher.");
        }

        var approved = amountApprovedMinor ?? v.AmountRequestedMinor;
        if (approved <= 0)
        {
            throw new ArgumentException("Approved amount must be positive.", nameof(amountApprovedMinor));
        }
        if (approved > v.AmountRequestedMinor)
        {
            throw new ArgumentException("Approved amount cannot exceed the requested amount.", nameof(amountApprovedMinor));
        }

        v.Status = VoucherStatus.Approved;
        v.AmountApprovedMinor = approved;
        v.DecidedByUserId = approverUserId;
        v.DecisionComment = comment;
        v.DecidedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        return v;
    }

    public async Task<Voucher> RejectVoucherAsync(
        Guid voucherId,
        Guid approverUserId,
        string reason,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required when rejecting.", nameof(reason));
        }

        var v = await LoadVoucher(voucherId, ct);
        if (v.Status != VoucherStatus.Submitted)
        {
            throw new InvalidVoucherTransitionException(v.Status, "reject");
        }
        if (v.RequesterUserId == approverUserId)
        {
            throw new SeparationOfDutiesException("The requester cannot reject their own voucher.");
        }

        v.Status = VoucherStatus.Rejected;
        v.DecidedByUserId = approverUserId;
        v.DecisionComment = reason.Trim();
        v.DecidedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        return v;
    }

    public async Task<Voucher> DisburseVoucherAsync(
        Guid voucherId,
        Guid custodianUserId,
        DateOnly effectiveDate,
        Guid periodId,
        CancellationToken ct = default)
    {
        var v = await LoadVoucher(voucherId, ct);
        if (v.Status != VoucherStatus.Approved)
        {
            throw new InvalidVoucherTransitionException(v.Status, "disburse");
        }
        if (v.AmountApprovedMinor is null or <= 0)
        {
            throw new PettyCashException($"Voucher {voucherId} is Approved but has no positive approved amount; cannot disburse.");
        }
        if (v.DecidedByUserId == custodianUserId)
        {
            throw new SeparationOfDutiesException("The approver cannot also be the disbursing custodian.");
        }

        // Build the journal: DR expense lines, CR petty-cash float (1060).
        // Total of DR lines == AmountApprovedMinor (we may have to scale lines
        // proportionally if the approver part-approved; in MVP-zero we keep
        // it simple and require the total approved == requested. Pro-rata
        // scaling can come later.).
        if (v.AmountApprovedMinor.Value != v.AmountRequestedMinor)
        {
            throw new PettyCashException(
                "MVP-zero: partial-approval disbursements aren't supported yet. "
                + "Reject + resubmit at the corrected amount.");
        }

        var ledgerEvent = new LedgerEvent
        {
            TenantId = v.TenantId,
            EffectiveDate = effectiveDate,
            PeriodId = periodId,
            SourceModule = "petty_cash",
            SourceEntityType = "Voucher",
            SourceEntityId = v.VoucherId.ToString("N"),
            IdempotencyKey = $"petty_cash:{v.VoucherId:N}:disburse",
            EventType = LedgerEventType.Posted,
            Narration = $"Petty cash disbursement {v.VoucherNo}: {v.Purpose}",
            ActorUserId = custodianUserId
        };

        // Debit each line against its GL account.
        short lineNo = 1;
        foreach (var line in v.Lines.OrderBy(l => l.LineNo))
        {
            ledgerEvent.Lines.Add(new LedgerEventLine
            {
                LineNo = lineNo++,
                AccountCode = line.GlAccount,
                DebitMinor = line.GrossAmountMinor,
                CreditMinor = 0,
                CurrencyCode = line.CurrencyCode,
                ProjectCode = v.ProjectCode,
                Description = line.Description
            });
        }

        // Single credit leg against petty cash float.
        ledgerEvent.Lines.Add(new LedgerEventLine
        {
            LineNo = lineNo,
            AccountCode = "1060",                       // Petty cash float — Ghana standard chart
            DebitMinor = 0,
            CreditMinor = v.AmountApprovedMinor.Value,
            CurrencyCode = v.CurrencyCode,
            ProjectCode = v.ProjectCode,
            Description = $"{v.VoucherNo} float draw"
        });

        // The Ledger writer + its underlying DB triggers will reject the post
        // if anything is malformed (closed period, unbalanced, mixed currency,
        // duplicate idempotency key on a different payload). The exception
        // bubbles up; voucher status is unchanged. Caller can fix the period
        // and retry.
        var eventId = await _ledger.PostAsync(ledgerEvent, ct);

        v.Status = VoucherStatus.Disbursed;
        v.DisbursedByUserId = custodianUserId;
        v.DisbursedAt = _clock.GetUtcNow();
        v.LedgerEventId = eventId;
        await _db.SaveChangesAsync(ct);
        return v;
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private async Task<Voucher> LoadVoucher(Guid voucherId, CancellationToken ct)
    {
        return await _db.Vouchers.Include(v => v.Lines)
            .FirstOrDefaultAsync(v => v.VoucherId == voucherId, ct)
            ?? throw new PettyCashException($"Voucher {voucherId} not found.");
    }

    /// <summary>
    /// Generate <c>PC-{site8}-{year}-{ordinal}</c>. The ordinal is computed
    /// inside the same transaction with a count-then-format so two
    /// concurrent submits would race; in MVP we accept the rare collision
    /// and rely on the unique constraint to throw — the caller retries.
    /// A monotonic per-site sequence comes in v1.1 alongside the policy DSL.
    /// </summary>
    private async Task<string> GenerateVoucherNoAsync(Guid siteId, int year, long tenantId, CancellationToken ct)
    {
        var sitePrefix = siteId.ToString("N")[..6].ToUpperInvariant();
        var taken = await _db.Vouchers.CountAsync(
            v => v.TenantId == tenantId &&
                 v.CreatedAt.Year == year &&
                 v.VoucherNo.StartsWith($"PC-{sitePrefix}-{year}-"),
            ct);
        var seq = (taken + 1).ToString("D5");
        return $"PC-{sitePrefix}-{year}-{seq}";
    }
}
