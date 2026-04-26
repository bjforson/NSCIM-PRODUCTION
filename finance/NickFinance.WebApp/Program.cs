using Microsoft.EntityFrameworkCore;
using NickFinance.AR;
using NickFinance.Coa;
using NickFinance.Ledger;
using NickFinance.PettyCash;
using NickFinance.PettyCash.Approvals;
using NickFinance.PettyCash.Receipts;
using NickFinance.Reporting;
using NickFinance.WebApp.Components;
using NickFinance.WebApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Connection string — required.
var connectionString = builder.Configuration.GetConnectionString("Finance")
    ?? throw new InvalidOperationException("ConnectionStrings:Finance is required.");

builder.Services.AddDbContext<LedgerDbContext>(opts => opts.UseNpgsql(connectionString));
builder.Services.AddDbContext<PettyCashDbContext>(opts => opts.UseNpgsql(connectionString));
builder.Services.AddDbContext<CoaDbContext>(opts => opts.UseNpgsql(connectionString));
builder.Services.AddDbContext<ArDbContext>(opts => opts.UseNpgsql(connectionString));

// Domain services
builder.Services.AddScoped<ILedgerWriter, LedgerWriter>();
builder.Services.AddScoped<ILedgerReader, LedgerReader>();
builder.Services.AddScoped<IPeriodService, PeriodService>();
builder.Services.AddScoped<ICoaService, CoaService>();
builder.Services.AddScoped<IFinancialReports, FinancialReports>();
builder.Services.AddScoped<IApprovalEngine, SingleStepApprovalEngine>();
builder.Services.AddScoped<IPettyCashService, PettyCashService>();
builder.Services.AddScoped<IArService, ArService>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

// Receipt storage rooted at the configured path.
var receiptRoot = builder.Configuration.GetValue<string>("NickFinance:ReceiptStorageRoot")
    ?? Path.Combine(AppContext.BaseDirectory, "receipts");
builder.Services.AddSingleton<IReceiptStorage>(_ => new LocalDiskReceiptStorage(receiptRoot));
builder.Services.AddScoped<IReceiptService, ReceiptService>();

// External integrations — pinned to sandbox/no-op/offline defaults until
// the corresponding credentials decisions land. See finance/DEFERRED.md.
//   * e-VAT  — IEvatProvider          → StubEvatProvider (sandbox IRN)
//   * OCR    — IOcrEngine             → NoopOcrEngine
//   * MoMo   — IDisbursementChannel   → OfflineCashChannel
// When each decision lands, swap the line below in this Program.cs.
builder.Services.AddSingleton<NickFinance.AR.IEvatProvider, NickFinance.AR.StubEvatProvider>();
builder.Services.AddSingleton<NickFinance.PettyCash.Receipts.IOcrEngine, NickFinance.PettyCash.Receipts.NoopOcrEngine>();
builder.Services.AddSingleton<NickFinance.PettyCash.Disbursement.IDisbursementChannel, NickFinance.PettyCash.Disbursement.OfflineCashChannel>();

// Current user — for v1, the configured dev user. Production wires to
// NickERP.Platform.Identity.
builder.Services.AddScoped(sp =>
{
    var section = builder.Configuration.GetSection("NickFinance:DevUser");
    var userId = Guid.TryParse(section["UserId"], out var g) ? g : Guid.NewGuid();
    var name = section["DisplayName"] ?? "Local Dev";
    var email = section["Email"] ?? "dev@nickscan.com";
    var tenant = builder.Configuration.GetValue<long?>("NickFinance:DefaultTenantId") ?? 1L;
    return new CurrentUser(userId, name, email, tenant);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
