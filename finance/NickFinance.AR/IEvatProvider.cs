namespace NickFinance.AR;

/// <summary>
/// GRA e-VAT IRN issuance. Production wires the certified Hubtel /
/// Persol / Blue Skies partner once the contract lands (CFO + CTO
/// decision pending). Until then, hosts ship <see cref="StubEvatProvider"/>
/// which mints a deterministic IRN — useful for end-to-end tests but
/// must be swapped before any real customer-facing invoice goes out.
/// </summary>
public interface IEvatProvider
{
    /// <summary>Provider tag persisted on the invoice for audit.</summary>
    string Provider { get; }

    /// <summary>Submit an issued invoice and get back the GRA-allocated IRN + a QR-code payload.</summary>
    Task<EvatIssueResult> IssueAsync(EvatIssueRequest req, CancellationToken ct = default);
}

public sealed record EvatIssueRequest(
    Guid InvoiceId,
    string InvoiceNo,
    string CustomerName,
    string? CustomerTin,
    DateOnly InvoiceDate,
    string CurrencyCode,
    long NetMinor,
    long LeviesMinor,
    long VatMinor,
    long GrossMinor,
    long TenantId = 1);

public sealed record EvatIssueResult(
    bool Accepted,
    string? Irn,
    string? QrPayload,
    string? FailureReason);

/// <summary>Stub — returns a deterministic synthesised IRN. Tests can verify the IRN appears on the persisted invoice.</summary>
public sealed class StubEvatProvider : IEvatProvider
{
    public string Provider => "stub";

    public Task<EvatIssueResult> IssueAsync(EvatIssueRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        if (req.GrossMinor <= 0)
        {
            return Task.FromResult(new EvatIssueResult(false, null, null, "Gross amount must be positive."));
        }
        // IRN format mirrors the GRA convention "IRN-{yyyyMMdd}-{12 hex}"
        // so consumers don't need to special-case stub values in display.
        var ts = req.InvoiceDate.ToString("yyyyMMdd");
        var hex = req.InvoiceId.ToString("N")[..12].ToUpperInvariant();
        var irn = $"IRN-{ts}-{hex}";
        var qr = $"{irn}|{req.GrossMinor}|{req.CurrencyCode}";
        return Task.FromResult(new EvatIssueResult(true, irn, qr, null));
    }
}
