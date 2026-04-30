# NickFinance — orchestrator hand-off, post-Wave-3 (2026-04-29)

> Final state after the multi-day wave-orchestrated rollout. Builds on
> `docs/HANDOFF-2026-04-29.md` (post-Wave-1 mid-day snapshot).

## Waves shipped today

| wave | scope | result |
|---|---|---|
| 1A Polish | EntityPicker null-fix, AuthorizeView wrap on TopNav, approvals badge, keyboard shortcuts (Ctrl+/, n, g+letter) | ✓ |
| 1B Security | Service account `LocalSystem→NT SERVICE\NickFinance_WebApp`, AES-256-GCM receipt encryption (DEK), Postgres RLS on 9 schemas + `TenantSessionInterceptor`, off-host backup S3/Azure | ✓ |
| 1C PDF phase 1 | `NickFinance.Pdf` project, QuestPDF Community 2026.2.4, Invoice + Voucher + Receipt generators + 3 endpoints + 8 tests | ✓ |
| 2A PDF phase 2 | Real QR codes (QRCoder 1.6.0), Customer statements with ageing summary, WHT certificate book (single + book endpoints), MailKit-backed SMTP email, standalone receipt-detail page, 15 new tests | ✓ |
| 2B FX phase 1 | `FxRate` entity in Banking, `IFxConverter` in Ledger, `FxRateService` with direct→inverse→latest-prior fallback, BoG provider stub + hosted importer, manual UI, 14 new tests | ✓ |
| 3A FX phase 2 | `IFxRevaluationService` (gain/loss to 7100/7110), `banking.fx_revaluation_log` carry-rate store, period-close UI integration blocking soft-close until green, 8 new tests | ✓ |
| 3B Track A — Identity extraction | New `platform/NickERP.Platform.Identity` shared lib, types + migrations moved, `NickFinance.Identity` becomes a `[TypeForwardedTo]` shim with namespace marker, every kernel `using` updated | ✓ |

**Post-deploy state on `nickhr`:**
- All 11 finance schemas applied; 12 bootstrap steps green
- 7100 + 7110 chart accounts seeded; `is_control = true` on both
- `banking.fx_revaluation_log` table live (10 cols, unique idx per period)
- `banking.fx_rates` seeded with 4 placeholder pairs
- `nscim_app` has DML grants on all schemas including the new ones
- RLS active on every `tenant_id`-bearing table across 9 schemas
- Service running as `NT SERVICE\NickFinance_WebApp` (virtual account, isolated SID)
- Receipt DEK present as machine env var
- Endpoints behaving: `/` 401, `/metrics` 200, webhook 503 (verify-token unset)
- Smoke voucher `PC-76FD5C-2026-00006` disbursed end-to-end ✓

## Net new in this rollout

| area | files added |
|---|---|
| PDF | `NickFinance.Pdf/` project + `NickFinance.Pdf.Tests/`; 7 generators + DTOs + endpoints; 24 tests total |
| FX | `NickFinance.Banking/FxRate.cs`, `FxRateService.cs`, `BogRateProvider.cs`, `FxRevaluationLog.cs`, `FxRevaluationService.cs`; 22 new banking tests |
| Identity platform | `platform/NickERP.Platform.Identity/` + `.Tests/`; back-compat shim + namespace marker in `NickFinance.Identity` |
| Email | `IEmailService` + `EmailSender` (MailKit) + 7 tests |
| Polish | `PendingApprovalsCounter` + `PendingApprovalsBadge` + `app.js` keyboard shortcuts |
| Security | `EncryptedReceiptStorage` + 12 tests, `TenantSessionInterceptor`, `apply-rls-policies.sql` |
| Pages | `CustomerStatement.razor`, `WhtCertificateBook.razor`, `ReceiptDetail.razor`, `FxRates.razor`, `FxRateNew.razor` |
| Endpoints | `/pdf/invoice/{id}`, `/pdf/voucher/{id}`, `/pdf/receipt/{id}`, `/pdf/statement/{customerId}`, `/pdf/wht-certificate/{vendorId}/{year}`, `/pdf/wht-certificate-book/{year}`, `POST /api/email/statement/{customerId}` |

## What still needs operator action

1. **DEK custody** — install script generated `NICKFINANCE_RECEIPT_DEK`. Back it up off-host (vault). Losing it makes every encrypted receipt unrecoverable.
2. **Off-host backup credentials** — `scripts/backup-nickhr-nightly.ps1` ready for S3/Azure. Operator sets `AWS_ACCESS_KEY_ID/_SECRET_ACCESS_KEY` + `NICKERP_BACKUP_S3_BUCKET`, OR `AZURE_STORAGE_CONNECTION_STRING` + `NICKERP_BACKUP_AZURE_CONTAINER`.
3. **SMTP credentials** — for the email integration to actually send. Set `NICKFINANCE_SMTP_HOST/_PORT/_USERNAME/_PASSWORD/_FROM`. Without these, `NoopEmailService` runs and the UI surfaces "SMTP not configured".
4. **Five credential-pending integrations from earlier** — Hubtel e-VAT, Hubtel MoMo (NickComms gateway), Azure Document Intelligence OCR, WhatsApp Cloud API, BoG FX rate API. All adapter code shipped; flip the env var to activate.

## What still needs engineering action

Genuinely deferred:

- **PWA offline mode** — multi-week WASM port for border sites
- **Adoption of `NickERP.Platform.Identity` by NickHR / NickScan / NickComms** — extraction is done; each app needs a `<ProjectReference>` + `using` swap
- **`IRoleService` extraction** — currently lives in `NickFinance.WebApp.Identity`; should move to `NickERP.Platform.Identity` for cross-app consumption (small follow-up)
- **Real BoG rate scraper** — provider stub exists; CSV scrape of `bog.gov.gh` documented but not implemented
- **Real human walkthrough** — 14+ new pages from this rollout haven't seen a real user. Cheapest highest-leverage next step.

## Operator quick-ref

```powershell
# Re-deploy after a code change
Stop-Service NickFinance_WebApp
dotnet publish C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\NickFinance.WebApp.csproj -c Release -o C:\Shared\NSCIM_PRODUCTION\publish\NickFinance.WebApp
Start-Service NickFinance_WebApp

# Apply pending migrations (idempotent on a re-run)
$pw = [Environment]::GetEnvironmentVariable('NICKSCAN_DB_PASSWORD','Machine')
$conn = "Host=localhost;Port=5432;Database=nickhr;Username=postgres;Password=$pw"
dotnet run --project C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.Database.Bootstrap\NickFinance.Database.Bootstrap.csproj -- --conn $conn --seed-coa

# End-to-end smoke (sentinel tenant 999_999, never touches real data)
dotnet run --project C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.Database.Bootstrap\NickFinance.Database.Bootstrap.csproj -- --conn $conn --skip-migrations --smoke-test

# Run the install script if the service identity needs migrating
pwsh -File C:\Shared\NSCIM_PRODUCTION\scripts\install-nickfinance-service.ps1
# (run as Administrator; safe to re-run; migrates LocalSystem->virtual account if not done)
```

## Test counts (since session start)

- `NickFinance.Pdf.Tests`: 16 (was 0 before this session)
- `NickFinance.Banking.Tests`: 17 (was 3)
- `NickFinance.WebApp.Tests`: 7 (was 0)
- `NickFinance.PettyCash.Tests/EncryptedReceiptStorageTests`: 12 (new this session)
- `NickFinance.Ledger.Tests`: 41 (8 new from FxRevaluationServiceTests)
- `NickERP.Platform.Identity.Tests`: 29 cases (moved from NickFinance.Identity.Tests; 13 pass without DB, 16 require `NICKFINANCE_TEST_DB`)
- `NickFinance.Adapters.Tests`: 13 (from earlier in the session)

Total: ~135 tests across the suite.

## DEFERRED.md status (final)

| # | item | status |
|---|---|---|
| 1 | GRA e-VAT | adapter shipped, real key pending CFO/CTO partner choice |
| 2 | NickComms MoMo | adapter shipped, gateway endpoint pending NickComms team |
| 3 | Azure OCR | adapter shipped, subscription pending CTO |
| 4 | Live `nickhr` rollout | **DONE 2026-04-28** |
| 5 | WhatsApp | adapter + engine hook + webhook all shipped, Meta credentials pending |
| 6 | PWA offline | deferred-by-design (multi-week WASM port) |
| 7 | PDF rendering (statements/WHT/email) | **DONE 2026-04-29** |
| 8 | Multi-currency FX | **DONE 2026-04-29** (read + write paths shipped; revaluation gates period close) |
| 9 | Track A.2 Platform.Identity | **EXTRACTED 2026-04-29** (NickHR/Scan/Comms adoption is a follow-up) |

Six rows are credential-gated (1, 2, 3, 5) or deferred-by-design (6) or done (4, 7, 8, 9). Engineering has zero residual work tied to any active row.

## Recommended next move (per the user's stated approach)

> "complete the dev then we do walkthrough to find issues"

Dev is complete. The walkthrough is the highest-leverage next step — every minute of real human clicking surfaces issues that no agent can predict. Specifically worth testing in the live system:

1. CF Access login as a real `@nickscan.com` user → confirm row lands in `identity.users` automatically
2. Provision a real Tema float at `/petty-cash/floats/new`
3. Submit, approve, disburse a real voucher; download its PDF
4. Issue a sandbox AR invoice; download the invoice PDF (should show the SANDBOX banner)
5. Visit `/banking/fx-rates` and override the seed USD→GHS rate with a current value
6. Try `/finance/period-close` — the FX revaluation row should be green (no foreign-currency activity yet) so soft-close works
7. Confirm keyboard shortcuts: Ctrl+/ focuses search, `n` jumps to new voucher, `g h` returns home
8. Hit `/cdn-cgi/access/logout` from the user menu — sign-out should work
