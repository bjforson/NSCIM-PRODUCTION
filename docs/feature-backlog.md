# NickFinance Feature Backlog

**Date:** 2026-04-28
**Status:** Research-only — no code modified
**Scope:** Triangulation of standard ERP finance features vs. what NickFinance ships today vs. Ghana / customs-scanning operational needs.

---

## 1. Executive summary

NickFinance is an unusually complete first-pass ERP: Ledger, AR, AP, Banking, Fixed Assets, Budgeting, Petty Cash, Reporting, Tax Engine and an iTaPS exporter all ship and post to a single event-sourced GL. The biggest *missing structural pieces* relative to mid-market ERPs (Sage / Xero / NetSuite / Odoo / SAP B1) are (a) a manual journal-entry surface — there is no `IJournalService` for accountants to post adjusting / closing entries, only module-driven postings; (b) a multi-currency FX revaluation pipeline — `Money` already refuses cross-currency arithmetic and the kernel forbids multi-currency events, so USD bank balances never get translated into GHS reporting; and (c) a period-close workflow UI — the C# `IPeriodService` exposes `SoftClose` / `HardClose` but no Razor page drives the end-to-end month-end checklist. The top three operational features for a Ghana customs-scanning operator are PDF generation (e-VAT compliant invoice / WHT certificate / receipt PDFs — already pre-wired with a "QuestPDF lands as a dependency" comment in `ICustomerStatementService.cs:6`), an ICUMS-tied scan-volume revenue recognition service (the `ScanCompletedAsync` hook exists in `IArService` but the production wiring is "Phase 4" per the inline comment), and site-level P&L since `site_id` is already a first-class dimension on every ledger line. Three "sounds appealing but not worth building" anti-features: full multi-entity consolidation (Nick TC-Scan is a single legal entity), treasury / sweep automation (cash holdings are too small relative to GhIPSS settlement times to repay the engineering), and the WASM offline PWA already documented in `DEFERRED.md` §6 — the Starlink workaround is good enough for the volume actually seen.

---

## 2. Ranked backlog

ROI scoring: Value (3=High,2=Med,1=Low) ÷ Effort (XS=1, S=2, M=4, L=8, XL=16). Higher = better.

| # | Feature | Category | Effort | Value | Dependencies | ROI | Notes |
|---|---|---|---|---|---|---|---|
| 1 | Manual journal entry UI + `IJournalService` | Must-have | M | High | LedgerWriter exists | 0.75 | Accountants need this for accruals, reclassifications, year-end |
| 2 | PDF rendering (invoices, receipts, statements, WHT certs) via QuestPDF | Must-have / Ghana | M | High | none — comment already pins QuestPDF | 0.75 | `ICustomerStatementService.cs:6` already flags this; e-VAT requires it |
| 3 | Period-close workflow page (checklist, close button, lock) | Must-have | S | High | `IPeriodService` exists | 1.5 | The biggest day-2 ERP gap |
| 4 | Multi-currency FX revaluation (USD → GHS at month-end) | Must-have / Ghana | L | High | new FxRate table, kernel V2 design | 0.375 | Aflao/Elubo USD float exists (CoA 1040); without revaluation the balance sheet drifts |
| 5 | Site-level P&L report | Should-have / Op | S | High | site_id dimension already on all events | 1.5 | Six profit centres; CFO needs this for site-by-site cost discipline |
| 6 | GRA VAT return form V1 generator (PDF + CSV) | Ghana compliance | S | High | Itaps exporter has CSV; needs V1 layout | 1.5 | Filing aid; iTaPS CSV is one half, V1 PDF is the other |
| 7 | Bulk operations (bulk approve, bulk void, bulk export) | QoL | S | Med | row-level service methods exist | 1.0 | Approver triages 30+ vouchers/day |
| 8 | Global search (voucher#, invoice#, IRN, vendor, customer, GL) | QoL | M | High | shared search index needed | 0.75 | Currently auditors must navigate per module |
| 9 | Drill-down: TB → GL detail → source document | QoL | S | High | `GlDetailAsync` exists; needs link wiring | 1.5 | Auditor flow |
| 10 | Saved searches / saved filters on lists | QoL | S | Med | per-user saved filters table | 1.0 | Power users will demand it once volume grows |
| 11 | Excel export on every list / report | QoL | S | High | ClosedXML or EPPlus dependency | 1.5 | Year-end accountants live in Excel |
| 12 | Customer credit limits + on-issue check | Should-have | S | Med | Customer entity already exists | 1.0 | Prevents truck operators with bad debt building further exposure |
| 13 | Recurring invoices (monthly retainer, fixed-fee scan customers) | Should-have | S | Med | mirror `RecurringVoucherTemplate` shape | 1.0 | Recurring petty cash already shipped — same pattern in AR |
| 14 | Bank rules / auto-categorisation engine | Should-have | M | Med | Banking module has matching | 0.5 | Reduce manual reconciliation |
| 15 | ICUMS scan-volume revenue recognition tie-in | Ghana-specific / Op | M | High | `ScanCompletedAsync` stub + ICUMS event source | 0.75 | Largest revenue line; right now it's manual |
| 16 | Cash flow forecast (12-month, not just 13-week) | Should-have | S | Med | `CashForecast13WeeksAsync` exists | 1.0 | Extend window + scenario layer |
| 17 | Budget revisions / forecasts (rolling re-budget) | Should-have | S | Low | `AnnualBudget` exists | 0.5 | Useful but not blocking |
| 18 | Vendor portal (vendor self-serve invoice upload + status) | Nice / Ghana | L | Med | new Razor area, identity layer | 0.25 | Low ROI before Ghana vendors are digitally onboarded |
| 19 | Customer portal (statement download, pay link) | Nice | M | Med | PDF + auth subset | 0.5 | Reduces "where's my receipt?" calls |
| 20 | GhIPSS direct bank rails (replace CSV import) | Should-have / Ghana | XL | Med | bank API contracts unknown | 0.125 | Most Ghana banks have only file-drop; CSV import is fine |
| 21 | Year-end WHT certificate PDF book (per-vendor) | Ghana compliance | S | High | WhtCertificate entity + PDF | 1.5 | Vendors need these to claim |
| 22 | NPS Tier 2 & Tier 3 contribution feed in iTaPS | Ghana compliance | S | Med | `ISsnitFeed` interface exists for Tier 1/2 | 1.0 | Tier 3 voluntary — useful if executives use Tier 3 |
| 23 | GhanaPost GPS digital address on customer / vendor | Ghana compliance | XS | Low | Customer.Address is free-text today | 1.0 | Nice signal for AML; not blocking |
| 24 | FIC AML reporting (>GHS 50K cash threshold) | Ghana compliance | M | High | CoA 2190 reserved; no logic yet | 0.75 | Memo account exists; reporting flow doesn't |
| 25 | Audit log (who changed what, when) — non-ledger writes | Must-have | M | High | EF ChangeTracker hook | 0.75 | Companies Act 6yr retention demands this beyond ledger immutability |
| 26 | Role-based authorisation (CFO / Custodian / Approver / Auditor) | Must-have | M | High | `CurrentUser` is single-role today | 0.75 | Currently every Razor page is open to any authenticated user |
| 27 | Email / WhatsApp digest (daily summary) | QoL | S | Med | NickComms wired | 1.0 | "Yesterday: 14 vouchers, 3 awaiting you" |
| 28 | Send invoice as PDF email attachment | QoL | S | High | requires #2 + comms gateway | 1.5 | Closes the AR loop |
| 29 | Inter-site cash transfer voucher type | Op | XS | Med | new VoucherCategory enum value | 2.0 | Tema sweeps cash to Aflao when Aflao under-funded |
| 30 | Multi-currency cash holding (USD float at Aflao/Elubo) | Op / Ghana | M | Med | depends on #4 | 0.5 | USD trickles in at borders today untracked |
| 31 | Fuel-tracking sub-ledger (genset litres × site × month) | Op | S | Med | extends petty cash receipts | 1.0 | Nice for site-cost-control |
| 32 | Per-trip / per-scan revenue attribution | Op | M | Med | depends on #15 | 0.5 | Useful for pricing decisions |
| 33 | Daily cash-count reconciliation page (driven by `ICashCountService`) | Op | S | High | `CashCount` entity exists | 1.5 | Currently service-only, no UI |
| 34 | Read-only public API for accountants (year-end work) | QoL | M | High | AspNetCore Web API host | 0.75 | Lets external tax accountants pull TB / GL detail |
| 35 | Notifications inbox (in-app) | QoL | M | Low | new Razor page | 0.25 | Email digest covers most of this |
| 36 | Approval delegation calendar UI (already-modelled, no UI) | QoL | XS | Med | `Delegation.cs` ships already | 2.0 | High-leverage; just expose it |
| 37 | Bulk import journals (CSV/Excel upload) | Should-have | S | Med | depends on #1 | 1.0 | Migration aid; year-end accountant tool |
| 38 | Drill-down on aging report (bucket → list of overdue invoices) | QoL | XS | Med | aging report exists | 2.0 | Trivial |
| 39 | Keyboard shortcuts (g+i invoices, g+v vouchers etc) | QoL | XS | Low | front-end only | 1.0 | Heavy users will appreciate |
| 40 | Tally Prime XML export → also support Sage / Xero export | Nice | M | Low | `TallyXmlExporter.cs` ships; add adapters | 0.25 | Only matters if NickHR migrates GL hosts |

**Top of backlog by ROI (>= 1.5):** 3 (period close UI), 5 (site P&L), 6 (V1 generator), 9 (drill-down), 11 (Excel export), 21 (year-end WHT books), 28 (invoice email), 29 (inter-site transfer voucher), 33 (cash count UI), 36 (delegation UI), 38 (aging drill-down).

---

## 3. Top 5 detailed write-ups

### 3.1 Manual journal entry UI + `IJournalService`

**Why it matters.** Every other feature works around the fact that today only modules can post (`IPettyCashService`, `IArService`, `IApService`, `IFixedAssetService`). When the auditor finds a mis-classification, when the CFO needs a year-end accrual, when payroll posts the JV from NickHR, when an inter-bank transfer needs booking — there is no path. `LedgerWriter` is fully capable; what's missing is the wrapper that lets a finance user compose lines, validate balance, attach narration + supporting docs, and route through approval. Every mid-market ERP has this as a core surface.

**Acceptance criteria.**
- A `/journals/new` Razor page with line-item grid (account, dr, cr, dimensions: site, project, cost centre).
- Real-time balance check on the form; cannot save until SUM(dr) == SUM(cr).
- Submitter / Approver workflow re-using the petty cash `IApprovalEngine` shape (band by amount).
- On Approve, posts to `LedgerWriter` with `source_module = "manual_journal"` and the journal id as `source_entity_id`.
- Reversal action that calls `LedgerWriter.ReverseAsync` with a reason captured.
- List page with filters (period, status, posted-by) + Excel export.
- Audit log of all transitions.

**Effort.** ~10–14 dev days. The kernel and approval engine are reusable; the work is mostly UI + a thin `IJournalService` + DTOs + tests + docs.

**Dependencies / risks.** Depends on the role-based authorisation work (#26 in the backlog) — without it, anyone can post a journal. Risk: a careless free-form journal hits a control account (1100 AR control, 2000 AP control, the 21xx tax accounts). Mitigation: respect the existing `Account.IsControl` flag — block manual posts to control accounts unless the caller has an explicit `journal.override_controls` permission.

---

### 3.2 PDF rendering (invoices, WHT certificates, customer statements, receipts)

**Why it matters.** The codebase has a literal pin for this — `ICustomerStatementService.cs:6` says `"PDF generation comes when QuestPDF lands as a dependency."` GRA-issued e-VAT invoices need to be issued as a printable document with the IRN and QR — Hubtel returns the IRN but the customer-facing artefact has to be generated on our side. WHT certificates are similar: GRA accepts a self-printed certificate per vendor per payment, and at year-end every vendor expects a consolidated book. Without this feature the AR module produces a database row and a CSV, not the document the tax authority and the customer expect.

**Acceptance criteria.**
- QuestPDF or PdfSharp dependency landed in `NickFinance.AR`, `NickFinance.AP`, `NickFinance.PettyCash`.
- Templated GRA-compliant tax invoice PDF: company header, customer block, invoice no + IRN + QR, line items, levies / VAT / gross totals, tin, footer with statutory text.
- WHT certificate PDF per `WhtCertificate` row.
- Customer statement PDF (mirror the existing `CustomerStatementService.RenderCsvAsync` shape).
- Year-end WHT certificate book PDF (one PDF per vendor with all certs in the year).
- A `Download PDF` button on every relevant page; back-end pre-signed URL for emailing.
- A test renders a fixture invoice and asserts the IRN, the line totals, and the QR scan-decodes to the expected URL.

**Effort.** ~12–18 dev days for the library landing + four template families + tests. Reusable templating component lowers later cost.

**Dependencies / risks.** None blocking. Risk: GRA changes the e-VAT invoice format — mitigate with a `IInvoiceTemplate` interface + a versioned template name on each issued invoice so historical re-renders match.

---

### 3.3 Period-close workflow page

**Why it matters.** Closing a period is the heartbeat of every ERP. Today `IPeriodService` exposes `SoftCloseAsync` / `HardCloseAsync` methods, and the `LedgerWriter` will reject postings to a HardClosed period — but there is no UI to drive close, no checklist of "have all bank recs been closed?", "have all AP bills been approved?", "has depreciation been posted?", "has the FX revaluation run?". Without this, period close is a tribal-knowledge phone call. Users either close prematurely (missing accruals) or never close (unbounded retroactive postings, audit trail nightmare).

**Acceptance criteria.**
- A `/periods` page listing every period with status pill (Open / SoftClosed / HardClosed).
- Clicking a period shows a checklist:
  - Bank reconciliations: open count, closed count
  - AP bills awaiting approval: count
  - AR invoices awaiting issuance: count
  - Depreciation posted? (yes/no, calls `IFixedAssetService.PostMonthlyDepreciationAsync` if no)
  - FX revaluation posted? (depends on #4)
  - Trial balance balanced? (calls `TrialBalanceAsync`, asserts `IsBalanced`)
- "Soft close" and "Hard close" buttons, gated on the checklist (cannot HardClose if TB not balanced).
- A pre-close exception report (any voucher / bill / invoice with effective_date in the period but status not yet posted).
- Tests assert posting to a HardClosed period throws.

**Effort.** ~5–7 dev days; the back-end services are all in place.

**Dependencies / risks.** Light dependency on FX revaluation (#4) and depreciation posting trigger. If those are deferred, the checklist gracefully marks them "not configured". Risk: users HardClose then realise they missed an accrual — the kernel's prior-period-adjustment story (`FINANCE_KERNEL.md` §) needs a UI hook; document the policy clearly.

---

### 3.4 Multi-currency FX revaluation

**Why it matters.** The CoA already has `1040 Bank — USD account`. The kernel forbids multi-currency events; it permits per-line currency. So a USD bank statement imports today as USD-denominated rows hitting account 1040, but there is no monthly FX revaluation event that translates the closing USD balance into GHS for reporting purposes. The Reporting module asks for a `currencyCode` parameter — meaning Trial Balance / P&L / Balance Sheet are computed *per currency*, not consolidated. For a Ghana entity with GHS as functional currency, every USD asset must be revalued to closing-rate GHS at month-end and the gain/loss posted to the P&L. Without this, the reported balance sheet is wrong as soon as USD enters the picture (which is *every day* at Aflao/Elubo).

**Acceptance criteria.**
- New `FxRate` table: (date, base_ccy, quote_ccy, rate, source). Seed from Bank of Ghana daily mid-rate.
- `IFxRateProvider` interface; first impl is `BogDailyRateProvider` (bank-of-ghana-rates.com or the BoG public CSV).
- `IFxRevaluationService.RevaluateMonthEndAsync(year, month, tenantId)` — for every non-functional-currency monetary balance (1040, USD MoMo wallets if any, USD payables/receivables), compute (closing_balance_in_native × month_end_rate) − (carrying_value_in_GHS), post a single journal: DR/CR (asset/liability) line + offset to `7110 FX gain/loss` (new account).
- Idempotent on (year, month, tenantId).
- A `/reports/fx-positions` page showing every non-functional-ccy account with its native balance, current rate, GHS equivalent, last revaluation date.
- Acceptance: import a USD statement at rate 12.0; revalue at rate 12.5; the journal posts a 0.5/USD GHS gain.

**Effort.** ~15–20 dev days. Most of the cost is in the kernel V2 work documented as a "post-V1" non-goal in `FINANCE_KERNEL.md` §.

**Dependencies / risks.** Largest single piece on the backlog. Risk: BoG rate scrape brittleness — wrap the provider in a routing pattern (real-source / manual-override) following the same `RoutingX` shape used for e-VAT and OCR. Risk: realised vs. unrealised FX bookkeeping confusion — document the policy, prefer "translation gain/loss" in equity for operating-cycle items per IAS 21.

---

### 3.5 Site-level P&L report

**Why it matters.** Each of the six sites (Tema, Kotoka, Takoradi, Aflao, Paga, Elubo) is a distinct profit centre. Custodian floats are tagged by `site_id`. Every voucher posts with `site_id` on the line. AR invoices generated from `ScanCompletedAsync` should carry `site_id` (currently only via `ProjectCode`). The reporting module already has `GlDetailAsync` capable of dimension filtering — but there is no "P&L by site" report. The CFO has been told the data is there; she cannot see it. This is a 5-day feature that turns six border crossings from a single P&L line into six profit-centre P&Ls.

**Acceptance criteria.**
- `IFinancialReports.ProfitAndLossBySiteAsync(currencyCode, from, to, siteId, tenantId)` returns the same `ProfitAndLossReport` shape filtered to lines where `site_id = @site`.
- Reports page gains a "by site" toggle and a site dropdown (Tema / Kotoka / …) — fed from a `Sites` reference table once it lands, hardcoded list until then per `FloatNew.razor`'s `StableSiteGuid` pattern.
- Comparative view: 6-column table, one per site, shared P&L row schema; footer = "All sites" total = the existing `ProfitAndLossAsync` value (validation invariant).
- Tests: post one synthetic voucher per site for an arbitrary expense account; assert the breakdown rows sum to the all-sites total.

**Effort.** ~5–7 dev days. Trivial schema additions; mostly query + UI.

**Dependencies / risks.** Tiny dependency on a real Sites registry — workable today via the deterministic `StableSiteGuid` helper. Risk: AR `ScanCompletedAsync` doesn't yet stamp `site_id` on its journal lines — expand it as part of this feature so revenue is correctly attributed (today scan revenue would only show "All sites").

---

## 4. Anti-features — what NOT to build

| Anti-feature | Reasoning |
|---|---|
| **Multi-entity consolidation** | Nick TC-Scan Ltd is a single legal entity. Consolidation is a NetSuite / SAP-multi-co feature for groups; building it speculatively bloats schema and burns weeks. Add when (and only when) a sister company materialises. |
| **Treasury management / cash sweep automation** | Total cash holdings across 6 sites + 3 banks + 3 MoMo wallets are well under the threshold where sweep optimisation pays back the engineering. Manual transfer + the proposed `inter-site cash transfer voucher type` (#29) is sufficient. |
| **Full WMS / inventory management** | No inventory turnover beyond consumables (stationery, scanner spares). The `1200 Inventory — consumables` row is enough; do not build cycle counting, lot tracking, or warehouse layouts. |
| **Project-cost time-tracking module** | The team is small; engineers and operators don't bill by the hour. Borrow the `ProjectCode` dimension already on every ledger line if needed. |
| **WASM offline PWA (DEFERRED.md §6)** | Already documented as deferred-by-design. The Starlink workaround (paper voucher + next-online entry) is sufficient at current border-site volumes. Re-evaluate if voucher volume × outage hours × custodian salary exceeds the 3–4 week build cost. |
| **Sage / Xero / NetSuite GL export adapters** | `TallyXmlExporter` already ships. Those adapters only matter if Nick HR or another sibling switches GL platforms. The single user is internal — do not turn NickFinance into a generic exporter. |
| **In-app chat / collaboration features** | WhatsApp + email cover the comms surface today. An in-app chat costs 2-4 weeks for marginal value. |
| **Crypto / stablecoin payment rails** | Bank of Ghana stance is uncertain; vendor / customer adoption is zero. Skip. |
| **Advanced budget allocation engines (driver-based, top-down with override)** | Linear monthly budget per (account, site) is enough for a 10-line P&L. Driver-based budgeting is a feature for $50M+ revenue companies. |
| **Predictive ML on cash forecast / fraud beyond the existing 8 rules** | The 8 rules in `FraudDetector.cs` cover the realistic abuse vectors. ML adds explanation cost and false-positive triage burden disproportionate to the catch rate. |
| **OCR for non-receipt documents (contracts, statements)** | Azure Document Intelligence is wired for receipts only; broader OCR is a different workflow with much higher false-positive cost. Do not generalise. |
| **Direct API integration to GhIPSS** | Most Ghanaian banks' interface is still file drop / SFTP. CSV import (already shipped) is the realistic posture; revisit when a major bank exposes a real REST contract. |
| **White-label tenant theming** | NickFinance is multi-tenant in schema only; one operator is the customer. Theming work has zero ROI. |

---

## 5. Open questions for CFO / COO / CTO

1. **Period-close cadence?** Does the CFO want monthly hard-close (typical) or quarterly hard-close (lighter-touch but riskier for accruals)? This affects the UI urgency on #3 and the FX revaluation cadence on #4.
2. **Functional vs. presentation currency.** Confirm GHS is the *functional* currency for reporting (likely yes — Ghana entity, GHS revenue base). Are there debt instruments in USD that would push us to consider USD-denominated borrowing under IAS 21? If so, the FX revaluation scope expands.
3. **ICUMS revenue model.** Is scan revenue billed (a) per-declaration to the importer's customs broker, (b) flat-fee per truck to the truck-operator, or (c) a hybrid? Current `ScanCompletedAsync` assumes (a). The data model decides whether #15 is "wire ICUMS event bus to AR" (1 week) or "rebuild billing engine" (4 weeks).
4. **Site profit-centre granularity.** Should the P&L break out by site only, or site × scan-lane / site × shift? The latter is what NSCIM tracks operationally; if reporting parity is wanted, the dimension story expands.
5. **WHT certificate distribution.** Today vendors receive a CSV row in iTaPS; do they expect a per-payment PDF certificate, a monthly summary, or a year-end book? Drives prioritisation between #21 and a per-payment auto-generated certificate.
6. **Auditor access.** Does the external auditor (year-end) want a read-only login, an Excel pack, or a restored DB snapshot? Drives whether #34 (read-only API) is needed or whether a quarterly Excel export is fine.
7. **AML / FIC threshold.** Is the GHS 50K cash-deposit threshold for FIC reporting confirmed, and is it currently being met manually? `2190 FIC reportable transactions queue` exists in the CoA but no code populates it — needs CFO/compliance officer alignment before #24 is built.
8. **Mobile money networks that are actually used.** AirtelTigo Money is on the CoA (1022), but is anyone *receiving* there? Same question for Telecel (1021). If only MTN is live, deprecate the unused float wallets to reduce CoA noise.
9. **Bank-statement format coverage.** Banking parsers exist as a registry. Which banks (Standard Chartered? GCB? Ecobank? Fidelity?) have parsers today and which are placeholders? Drives #14 priority.
10. **Approver staffing model.** `Delegation.cs` ships; the UI does not. Are CFO / COO actually using delegation today, or routing approvals manually? If yes, #36 jumps in priority.
11. **Year-end financial statement format.** Are statutory accounts produced inside NickFinance (would require IFRS-for-SMEs disclosures, notes, formatting) or off-system in Excel by an external accountant? If on-system, that's an XL feature and should be queued; today the assumption is off-system.
12. **Document retention policy.** Companies Act mandates 6-year retention of accounting records. Is the audit-trail strategy "rely on append-only ledger + file-system receipt store" or do we need a separate immutable archive (S3 Object Lock, etc.)? Drives #25.
13. **Multi-user concurrency on approvals.** What happens if two approvers click "Approve" on the same voucher simultaneously? The service likely handles it (last-write-wins / unique constraint), but the UX needs verification.
14. **Cash count cadence per site.** Daily? Weekly? At-shift-change? Drives whether #33 is a single page or a scheduled reminder workflow.
15. **GhanaPost GPS adoption.** Do customers actually have GPS addresses on file today, or is it still the "near the lorry park" free-text? If adoption is < 20%, #23 is effectively a data-entry tax with low value.

---

## Appendix A — what's *already* shipped (read carefully before adding)

This list is built from the codebase, not from prior docs. Use as a "do not double-build" reference.

- **Ledger kernel** — append-only event-sourced GL, deferred constraint triggers, period status enforcement (`NickFinance.Ledger`).
- **Money type** — minor units, ISO-4217, banker's rounding, compile-time cross-currency safety (`Money.cs`).
- **Chart of Accounts** — Ghana standard 60+ accounts, control flag, account types (`NickFinance.Coa`).
- **Tax engine** — Ghana compounded VAT (NHIL+GETFund+COVID before VAT15%), Sixth Schedule WHT rates including residence-aware rate-for-supply-of-goods (`NickFinance.TaxEngine`).
- **AR module** — customer master, draft / issue / receipt state machine, e-VAT IRN integration via `RoutingEvatProvider` → `HubtelEvatProvider`, dunning service (4 tones), customer statement service (CSV today, PDF pinned), aging report, scan-to-invoice hook (`NickFinance.AR`).
- **AP module** — vendor master, bill capture (PreTax / GhanaInclusive / None tax treatments), approve, pay-with-WHT-deduction, WHT certificate auto-generation, aging report (`NickFinance.AP`).
- **Banking** — bank account master, statement import via `BankCsvParserRegistry`, auto-match against AP payments + AR receipts with date/amount tolerance, reconciliation session model (`NickFinance.Banking`).
- **Fixed Assets** — register, straight-line + declining-balance, monthly depreciation posting (idempotent on period), disposal flow (`NickFinance.FixedAssets`).
- **Budgeting** — annual budget per (year, department), per-account-per-month lines, variance vs. live ledger, 13-week cash forecast (`NickFinance.Budgeting`).
- **Petty Cash** — float, voucher state machine (Draft → Submitted → Approved → Disbursed), policy-driven multi-step approval engine, YAML policy DSL, delegation, MoMo disbursement via `RoutingDisbursementChannel` → `NickCommsMomoChannel`, OCR via `RoutingOcrEngine` → `AzureFormRecognizerOcrEngine`, WhatsApp approval notifier, recurring vouchers, cash counts, 8-rule fraud detector (`NickFinance.PettyCash`).
- **Reports** — Trial Balance, Balance Sheet, P&L, GL Detail, Cash Flow Statement (indirect method) (`NickFinance.Reporting`).
- **iTaPS exporter** — VAT return CSV, WHT return CSV, SSNIT schedule CSV (Tier 1 + Tier 2) (`NickFinance.Itaps`).
- **GlSync** — Tally Prime XML export envelope (`NickFinance.GlSync`).
- **WebApp** — 21 Razor pages: Home dashboard, Petty cash list/new/detail, Floats list/new, Approvals queue, AR list/detail/new + receipt, AP bills, Customers, Banking, Fixed assets, Budgets, Reports, Cash flow, Aging, status pill component (`NickFinance.WebApp`).
- **Identity** — Cloudflare Access JWT validation against CF JWKS, claims map to audit columns (`CfAccessAuth.cs`).
- **Bootstrap** — `NickFinance.Database.Bootstrap` CLI with smoke runner, 10/10 green against live `nickhr` database.

## Appendix B — confirmed gaps quick list (cross-reference for the table above)

| Gap | Surface today | What's missing |
|---|---|---|
| Manual journal entry | `LedgerWriter` only | UI + `IJournalService` + approval routing |
| PDF rendering | CSV in `CustomerStatementService` | QuestPDF dep + 4 templates |
| Period close UI | `IPeriodService` C# only | Razor checklist page |
| FX revaluation | `Money` rejects cross-ccy | `FxRate` table + `IFxRevaluationService` + BoG provider |
| Site P&L | `site_id` on every line | One report query + UI toggle |
| Audit log (non-ledger writes) | Ledger is immutable, but customer/vendor master edits are not | EF SaveChanges interceptor → audit_events table |
| Role-based authz | Single `CurrentUser` shape | Roles enum + `[Authorize(Policy=…)]` per page |
| Bulk operations | Per-row services | Bulk endpoints + checkbox UI |
| Excel export | None | ClosedXML or EPPlus |
| Global search | None | Cross-module search index |
| AR recurring invoices | Recurring vouchers exist for petty cash | Mirror `RecurringVoucherTemplate` in AR |
| Customer credit limit | Field absent on Customer | One column + on-issue check |
| Year-end WHT book | Per-row CSV in iTaPS | PDF assembly per vendor |
| GhanaPost GPS | Free-text Address only | Optional structured field |
| FIC AML reporting | Memo account 2190 | Detection rule + report |
| ICUMS revenue tie-in | `ScanCompletedAsync` stub | NSCIM event bus subscription + invoice generation |
| Inter-site cash transfer | Use generic voucher | Dedicated category + journal pattern |
| Multi-currency cash holding | Per-line ccy works | Ties to FX revaluation #4 |
| Daily cash count UI | `ICashCountService` | Razor page |
| Public read-only API | Razor only | ASP.NET Core API host |
| Approval delegation UI | `Delegation.cs` shipped | Razor calendar UI |

---

*End of report.*
