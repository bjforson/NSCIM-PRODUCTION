# NickFinance — pinned items awaiting external decisions

> Status (2026-04-26): the NickFinance suite is feature-complete. Six
> production-cutover items are pinned because they need a decision or a
> credential the engineering team can't provide on its own. Everything
> below is wired with a sandbox / no-op default that keeps the system
> functioning end-to-end; the swap-in for each is one DI registration
> in `finance/NickFinance.WebApp/Program.cs` (or, for the WhatsApp /
> mobile items, a separate workstream).

---

## 1. GRA e-VAT integration

**Pin:** the certified-partner choice belongs to the CFO + CTO. Until
the contract lands, AR invoices issue through `StubEvatProvider`,
which mints visibly-sandbox IRNs prefixed `SANDBOX-IRN-`.

**Risk if forgotten:** an invoice issued through the sandbox is **not
submitted to GRA** and is not legally compliant. The UI surfaces this
via a yellow `SANDBOX` pill on every affected invoice in `/ar` and a
banner on the home page when the stub provider is registered. Tests
explicitly assert `StubEvatProvider.IsSandbox(...)` rather than
matching a real IRN format, so anyone reading the test suite sees the
distinction.

**To unpin:**

1. Pick the partner — Hubtel, Persol Systems, or Blue Skies Solutions
   (existing outreach drafts are at `docs/modules/spikes/02b-evat-outreach-emails.md`).
2. Implement `IEvatProvider` against their sandbox. Sketch:
   ```csharp
   public sealed class HubtelEvatProvider : IEvatProvider
   {
       public string Provider => "hubtel";
       public async Task<EvatIssueResult> IssueAsync(EvatIssueRequest req, CancellationToken ct)
       {
           // POST to https://evat.hubtel.com/api/invoices
           // with X-Api-Key and the partner-required body shape.
           // Map the response IRN + QR back to EvatIssueResult.
       }
   }
   ```
3. Replace the registration in `Program.cs`:
   ```diff
   - builder.Services.AddSingleton<NickFinance.AR.IEvatProvider, NickFinance.AR.StubEvatProvider>();
   + builder.Services.AddHttpClient<NickFinance.AR.IEvatProvider, HubtelEvatProvider>(http =>
   +     http.BaseAddress = new Uri(builder.Configuration["NickFinance:Evat:BaseUrl"]!));
   ```
4. Run an end-to-end sandbox-to-sandbox round-trip against the
   partner's test environment before flipping production.
5. Existing AR invoices issued under `SANDBOX-IRN-*` stay flagged
   forever — they were never real. New invoices issue under the
   partner's IRN format.

**Acceptance:** an invoice issued in the live UI receives a real
GRA-allocated IRN within 30 seconds; the SANDBOX pill goes away;
end-of-month VAT return matches the iTaPS view.

---

## 2. Hubtel MoMo disbursement

**Pin:** NickComms.Gateway's `/api/disburse/momo` endpoint isn't yet
live (it's specced in `services/NickComms.Gateway` but not built).
Until it ships, voucher disbursement uses `OfflineCashChannel` — the
custodian hands cash physically and the journal posts immediately.

**To unpin:**

1. Build the `/api/disburse/momo` endpoint in NickComms.Gateway. It
   wraps Hubtel Merchant API and writes to its own outbox so retries
   are idempotent on `clientReference`.
2. Set `NICKCOMMS_API_KEY_NICKFINANCE` (machine env var) to the
   per-app key issued by NickComms.
3. Swap registration:
   ```diff
   - builder.Services.AddSingleton<IDisbursementChannel, OfflineCashChannel>();
   + builder.Services.AddHttpClient<IDisbursementChannel, NickCommsMomoChannel>(http =>
   +     {
   +         http.BaseAddress = new Uri(builder.Configuration["NickFinance:Momo:GatewayBaseUrl"]!);
   +         http.DefaultRequestHeaders.Add("X-Api-Key",
   +             Environment.GetEnvironmentVariable("NICKCOMMS_API_KEY_NICKFINANCE")
   +             ?? throw new InvalidOperationException("NICKCOMMS_API_KEY_NICKFINANCE not set"));
   +     });
   ```
4. The voucher detail page already routes through whichever channel
   is registered — no UI change needed.

**Acceptance:** a voucher with `PayeeMomoNumber + PayeeMomoNetwork` set
disburses to a sandbox MTN MoMo number, the rail reference returned
by Hubtel lands on `voucher.disbursement_reference`, and
NickComms.Gateway's settlement webhook closes the loop on the
banking side.

---

## 3. Receipt OCR

**Pin:** Azure AI Document Intelligence (formerly Form Recognizer)
needs a subscription key. Until set, receipts upload + dedupe but the
amount/date fields stay null.

**To unpin:**

1. Provision Azure AI Document Intelligence (S0 tier is fine for the
   ~2,000 receipts/month projection). Capture
   `AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT` + `..._KEY`.
2. Add a new class:
   ```csharp
   public sealed class AzureFormRecognizerOcrEngine : IOcrEngine
   {
       public string Vendor => "azure-document-intelligence";
       public async Task<OcrResult> RecogniseAsync(byte[] content, string contentType, CancellationToken ct)
       {
           // Use Azure.AI.DocumentIntelligence.DocumentIntelligenceClient
           // with the prebuilt-receipt model. Map the strongest signals
           // (Total field, Transaction date) back to OcrResult.
       }
   }
   ```
   plus the `Azure.AI.DocumentIntelligence` package.
3. Swap registration:
   ```diff
   - builder.Services.AddSingleton<IOcrEngine, NoopOcrEngine>();
   + builder.Services.AddSingleton<IOcrEngine>(_ => new AzureFormRecognizerOcrEngine(
   +     endpoint: builder.Configuration["NickFinance:Ocr:Endpoint"]!,
   +     apiKey: Environment.GetEnvironmentVariable("AZURE_DOCUMENT_INTELLIGENCE_KEY")!));
   ```

**Acceptance:** uploading a Ghana-format receipt photo (typical
thermal-printer JPEG) populates `OcrAmountMinor` and `OcrDate` with
≥80% confidence, and the dashboard "Receipt OCR" pinned-item banner
goes away.

---

## 4. Production rollout against the live `nickhr` database

**Pin:** the bootstrap CLI is verified against a throwaway DB
(commit `46f7336` — see `finance/NickFinance.Database.Bootstrap/BOOTSTRAP.md`).
Running it against the live `nickhr` database is a DBA operation
gated by an explicit go-ahead.

**To unpin:** follow the §"Live nickhr DB rollout" steps in
`BOOTSTRAP.md`:

1. `pg_dump` of `nickhr` to `C:\Backups\nickhr-pre-finance-{ts}.dump`.
2. Confirm no schema collisions on `finance` / `coa` / `petty_cash` / `ar`.
3. Run the CLI with `--seed-coa`.
4. Verify the 7 finance/petty_cash tables, 3 coa/ar tables, 2 trigger
   functions land cleanly.
5. Point the WebApp's connection string at `nickhr` and bring it up.

**Acceptance:** the WebApp's home page renders against the live DB
with all four metric tiles populated, and a sandbox-IRN-tagged test
invoice survives a round-trip from create → issue → receipt without
touching any NickHR table.

---

## Status surfaces

The web UI raises this list automatically:

* `/` (home) shows a yellow card listing all currently-pinned items.
  Each item resolves to a one-line explanation of the swap.
* `/ar` shows a `SANDBOX` pill next to every IRN minted by the stub
  provider, plus the IRN itself in muted text for traceability.
* The home-page card disappears item-by-item as each registration
  swaps in `Program.cs`. When the card is gone, the suite is fully
  in-cutover state.

---

## 5. WhatsApp approval flow

**Pin:** Meta WhatsApp Business Cloud API requires a verified Meta
business account, a Cloud API access token, a phone-number id, and a
publicly-reachable webhook (HTTPS) for delivery callbacks. None of
these exist for Nick TC-Scan yet.

Until they do, AP payment runs and large petty-cash vouchers route
approvals through the in-UI approvals queue (`/petty-cash` + the
upcoming `/approvals` page); CFO approves on-screen rather than via
WhatsApp.

**To unpin:**

1. Provision the Meta Business assets and capture
   `WHATSAPP_CLOUD_API_TOKEN` + `WHATSAPP_PHONE_NUMBER_ID`.
2. Implement `IWhatsAppApprovalChannel` (sketch — not in tree yet) on
   top of NickComms.Gateway's `/api/whatsapp/template-message`
   endpoint (which itself needs to ship at NickComms first).
3. Wire it into the approval engine via a new `IApprovalNotifier` hook:
   ```csharp
   builder.Services.AddSingleton<IApprovalNotifier, WhatsAppApprovalNotifier>();
   ```
4. Webhook handler maps the user's reply ("APPROVE INV-2026-04-00012")
   back to `IPettyCashService.ApproveVoucherAsync` with a token-derived
   actor user id.

**Acceptance:** CFO receives a WhatsApp template message for any
voucher >GHS 100k or any AP payment run, taps Approve in the chat,
the voucher transitions to Approved, and the WhatsApp delivery
metadata lands on the `voucher_approvals.comment` for audit.

---

## 6. Mobile / PWA offline mode (border sites)

**Pin:** Aflao + Paga + Elubo lose Starlink frequently. The Blazor
Server UI requires a live SignalR connection, so it goes down with the
network. A true offline-first PWA needs Blazor WebAssembly with
IndexedDB-backed queueing — that's a non-trivial port (server-side
DI / DbContext don't transfer to WASM directly).

**Workaround in the meantime:** custodians at border sites use the
WebApp during the daily Starlink window (typically 2-3 hours);
voucher submissions outside that window are recorded on paper and
entered the next online morning.

**To unpin (when offline becomes a hard requirement):**

1. Stand up a `NickFinance.WebApp.Wasm` companion project with
   per-page Blazor Render Modes set to `InteractiveAuto` (server +
   WASM hybrid). The shell stays Blazor Server; only the
   submit/approve pages flip to Auto so they keep working when the
   circuit drops.
2. Build a service worker (`wwwroot/service-worker.js`) caching the
   submit page assets + a JS-side queue in IndexedDB.
3. Implement `IPettyCashOfflineQueue` that POSTs to a
   `NickFinance.WebApp.Api` endpoint when online, queues otherwise.
4. Voucher carries a `queued_offline=true` flag on submit when from
   the queue, so audit can see which vouchers came in via the
   offline path.

**Acceptance:** custodian submits a voucher mid-network-blackout,
the page UI confirms "queued, will submit when online", Starlink
returns 40 minutes later, the queued voucher posts automatically,
and a Submitted status lands on the server-side row with
`queued_offline=true`.

---

## Tracker

| # | Item | Owner | Decision needed by | Status |
|---|---|---|---|---|
| 1 | e-VAT partner choice | CFO + CTO | Before first real customer invoice | Pinned |
| 2 | NickComms /api/disburse/momo build | NickComms team | Before first MoMo-rail voucher | Pinned |
| 3 | Azure Document Intelligence subscription | CTO | Before fraud signal F3 (duplicate-receipt) goes live for production audit | Pinned (low urgency) |
| 4 | Live `nickhr` rollout window | DBA + ops | When 1 + 2 are in place | Pinned |
| 5 | WhatsApp approval flow (Meta Cloud API) | CFO + CTO | Quality-of-life — defer to Phase 2 | Pinned |
| 6 | Mobile / PWA offline (border sites) | CTO | When border-site network drops cause real loss | Pinned (low urgency) |

Update the table when each pin lifts. Delete this file when zero rows
remain.
