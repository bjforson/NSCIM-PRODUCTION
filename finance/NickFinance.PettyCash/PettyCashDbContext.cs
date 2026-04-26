using Microsoft.EntityFrameworkCore;
using NickFinance.PettyCash.Approvals;

namespace NickFinance.PettyCash;

/// <summary>
/// EF Core context for the Petty Cash module. Owns the <c>petty_cash</c>
/// schema. The Ledger lives in a separate <see cref="Ledger.LedgerDbContext"/>
/// in the <c>finance</c> schema of the same physical database — modules
/// post to the ledger via <see cref="Ledger.ILedgerWriter"/>, never reaching
/// in directly.
/// </summary>
public class PettyCashDbContext : DbContext
{
    public const string SchemaName = "petty_cash";

    public PettyCashDbContext(DbContextOptions<PettyCashDbContext> options) : base(options) { }

    public DbSet<Float> Floats => Set<Float>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<VoucherLineItem> VoucherLines => Set<VoucherLineItem>();
    public DbSet<VoucherApproval> VoucherApprovals => Set<VoucherApproval>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.HasDefaultSchema(SchemaName);

        b.Entity<Float>(e =>
        {
            e.ToTable("floats");
            e.HasKey(x => x.FloatId);
            e.Property(x => x.FloatId).HasColumnName("float_id");
            e.Property(x => x.SiteId).HasColumnName("site_id").IsRequired();
            e.Property(x => x.CustodianUserId).HasColumnName("custodian_user_id").IsRequired();
            e.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3).IsRequired();
            e.Property(x => x.FloatAmountMinor).HasColumnName("float_amount_minor").IsRequired();
            e.Ignore(x => x.FloatAmount);
            e.Property(x => x.ReplenishThresholdPct).HasColumnName("replenish_threshold_pct").IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
            e.Property(x => x.ClosedAt).HasColumnName("closed_at");
            e.Property(x => x.ClosedByUserId).HasColumnName("closed_by_user_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();

            // One active float per (site, currency) — closed floats can stack underneath.
            e.HasIndex(x => new { x.TenantId, x.SiteId, x.CurrencyCode, x.IsActive })
             .HasFilter("\"is_active\" = TRUE")
             .IsUnique()
             .HasDatabaseName("ux_floats_active_per_site_currency");
        });

        b.Entity<Voucher>(e =>
        {
            e.ToTable("vouchers");
            e.HasKey(x => x.VoucherId);
            e.Property(x => x.VoucherId).HasColumnName("voucher_id");
            e.Property(x => x.VoucherNo).HasColumnName("voucher_no").HasMaxLength(64).IsRequired();
            e.Property(x => x.FloatId).HasColumnName("float_id").IsRequired();
            e.Property(x => x.RequesterUserId).HasColumnName("requester_user_id").IsRequired();
            e.Property(x => x.Category).HasColumnName("category").HasConversion<short>().IsRequired();
            e.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(500).IsRequired();
            e.Property(x => x.AmountRequestedMinor).HasColumnName("amount_requested_minor").IsRequired();
            e.Property(x => x.AmountApprovedMinor).HasColumnName("amount_approved_minor");
            e.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasConversion<short>().IsRequired();
            e.Property(x => x.PayeeName).HasColumnName("payee_name").HasMaxLength(200);
            e.Property(x => x.ProjectCode).HasColumnName("project_code").HasMaxLength(64);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.SubmittedAt).HasColumnName("submitted_at");
            e.Property(x => x.DecidedAt).HasColumnName("decided_at");
            e.Property(x => x.DisbursedAt).HasColumnName("disbursed_at");
            e.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id");
            e.Property(x => x.DecisionComment).HasColumnName("decision_comment").HasMaxLength(1000);
            e.Property(x => x.DisbursedByUserId).HasColumnName("disbursed_by_user_id");
            e.Property(x => x.LedgerEventId).HasColumnName("ledger_event_id");
            e.Property(x => x.TaxTreatment).HasColumnName("tax_treatment").HasConversion<short>().IsRequired();
            e.Property(x => x.WhtTreatment).HasColumnName("wht_treatment").HasConversion<short>().IsRequired();
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();

            e.HasIndex(x => new { x.TenantId, x.VoucherNo })
             .IsUnique()
             .HasDatabaseName("ux_vouchers_tenant_voucher_no");

            e.HasIndex(x => new { x.TenantId, x.FloatId, x.Status })
             .HasDatabaseName("ix_vouchers_tenant_float_status");

            e.HasIndex(x => new { x.TenantId, x.RequesterUserId, x.Status })
             .HasDatabaseName("ix_vouchers_tenant_requester_status");

            e.HasOne<Float>()
             .WithMany()
             .HasForeignKey(x => x.FloatId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
             .WithOne()
             .HasForeignKey(l => l.VoucherId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<VoucherLineItem>(e =>
        {
            e.ToTable("voucher_line_items");
            e.HasKey(x => x.VoucherLineId);
            e.Property(x => x.VoucherLineId).HasColumnName("voucher_line_id");
            e.Property(x => x.VoucherId).HasColumnName("voucher_id").IsRequired();
            e.Property(x => x.LineNo).HasColumnName("line_no").IsRequired();
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
            e.Property(x => x.GrossAmountMinor).HasColumnName("gross_amount_minor").IsRequired();
            e.Property(x => x.GlAccount).HasColumnName("gl_account").HasMaxLength(32).IsRequired();
            e.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3).IsRequired();

            e.HasIndex(x => new { x.VoucherId, x.LineNo })
             .IsUnique()
             .HasDatabaseName("ux_voucher_lines_voucher_lineno");
        });

        // -------------------------------------------------------------------
        // voucher_approvals — one row per step, ordered by step_no.
        // -------------------------------------------------------------------
        b.Entity<VoucherApproval>(e =>
        {
            e.ToTable("voucher_approvals");
            e.HasKey(x => x.VoucherApprovalId);
            e.Property(x => x.VoucherApprovalId).HasColumnName("voucher_approval_id");
            e.Property(x => x.VoucherId).HasColumnName("voucher_id").IsRequired();
            e.Property(x => x.StepNo).HasColumnName("step_no").IsRequired();
            e.Property(x => x.Role).HasColumnName("role").HasMaxLength(64).IsRequired();
            e.Property(x => x.AssignedToUserId).HasColumnName("assigned_to_user_id").IsRequired();
            e.Property(x => x.Decision).HasColumnName("decision").HasConversion<short>().IsRequired();
            e.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id");
            e.Property(x => x.Comment).HasColumnName("comment").HasMaxLength(1000);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.DecidedAt).HasColumnName("decided_at");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();

            e.HasIndex(x => new { x.VoucherId, x.StepNo })
             .IsUnique()
             .HasDatabaseName("ux_voucher_approvals_voucher_step");

            e.HasIndex(x => new { x.TenantId, x.AssignedToUserId, x.Decision })
             .HasDatabaseName("ix_voucher_approvals_assignee_decision");

            e.HasOne<Voucher>()
             .WithMany()
             .HasForeignKey(x => x.VoucherId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
