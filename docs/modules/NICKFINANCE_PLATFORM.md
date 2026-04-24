# NickFinance Suite — Platform Plan

> **Position in `ROADMAP.md`: new Phase 6.** Depends on Phases 1 (Identity),
> 2 (Operational hygiene), 4 (unified audit + event bus). Starts after
> Phase 3; overlaps with Phase 5.
>
> Related docs: `ROADMAP.md`, `docs/modules/PETTY_CASH.md`,
> `platform/NickERP.Portal/SSO.md`, `PLATFORM.md`.

---

## 0. TL;DR

Extend NickERP into a full accounting suite. Four questions up front:

1. **Build full suite in-house?** 25–40 engineer-months, ~$250K–$400K, real
   compliance risk if we underestimate GRA e-VAT or IFRS edge cases.
2. **Buy an off-the-shelf ERP (NetSuite / Sage X3)?** $200K–$400K year one,
   6–18 month implementation, strips the differentiation surface we've
   been building.
3. **Hybrid: Tally Prime Gold (or QuickBooks) as GL of record, build
   native operational modules (AR, AP, PettyCash, cash positioning) on
   top of NickERP, nightly journal export?** ~$150K–$250K engineering
   + ~$5–15K/yr for Tally + Ghana partner support. **Recommended.**
4. **Full build minus fixed-assets/consolidation?** Middle option.
   $150K–$220K if we skip inventory and FA; still misses the statutory
   filing muscle that Tally has for GRA.

**This document specifies option 3 (hybrid)** as the default path, with
option 4 (narrower build) kept as a fallback if the GL-of-record vendor
choice drags.

---

## 1. Where this sits in the platform roadmap

`ROADMAP.md` today has five phases plus Phase 2.5 (performance). The
ROADMAP's current gap list includes nothing about finance/accounting
because that scope starts here.

### Proposed ROADMAP insert

Add this after Phase 5:

```markdown
### Phase 6 — NickFinance suite (quarters, not weeks)

Goal: make NickERP the system of record for the business's money, not
just its operations. Full plan in docs/modules/NICKFINANCE_PLATFORM.md.
Ships one module at a time; each module is independently useful.

Sequence (modules in order — each 6–10 weeks):
6.1 Petty cash — docs/modules/PETTY_CASH.md
6.2 Accounts Receivable + scan-to-invoice integration
6.3 Accounts Payable + OCR + Hubtel payment runs
6.4 Banking & reconciliation (CSV + MoMo webhook auto-match)
6.5 Tax engine (GRA e-VAT, WHT, SSNIT, NHIL/GETFund/COVID)
6.6 Fixed assets & depreciation
6.7 Financial reporting (P&L, BS, CF, trial balance)
6.8 Budgeting & forecasting
6.9 Journal sync to GL of record (Tally Prime / QuickBooks)

Feeds GL of record nightly; Tally remains statutory source for audit
and GRA filings until 6.10 (optional): NickFinance takes full GL
ownership once audit trust is earned (year 2+).
```

### Dependency chain within the broader roadmap

| Dependency | Why NickFinance needs it | Blocking? |
|---|---|---|
| **Phase 1** — Identity service | Approvers, requesters, custodians, auditors must be canonical users across NickHR + NickFinance + NSCIM. Without it, we re-solve the two-user-stores problem inside Finance. | Yes, hard block |
| **Phase 2** — Seq centralized logging | Finance events must be debuggable across processes. Missing a posted journal is worse than missing a scan event. | Strong preference |
| **Phase 2** — CI/deploy pipeline | Finance code demands zero-defect deploys. Manual robocopy is untenable once money is on the line. | Strong preference |
| **Phase 2** — Backup policy | Mandatory before go-live. Companies Act 992 §126 requires 6-year retention of financial records. | Hard block on go-live |
| **Phase 3** — Shared TopNav, federated search, notifications | Finance inherits for free. Without it, the UX regresses to "another separate app". | Soft dependency |
| **Phase 4** — Event bus + unified audit log | AR auto-invoice from scan completion (6.2) needs the event bus. Audit log is a compliance requirement for any accounting subsystem. | Hard block on 6.2 + audit features |
| **Phase 5** — Tenancy | If we ever sell NickERP to a second agency, Finance has the most tenant-sensitive data (their GL). Phase 6 schemas must carry `tenant_id` from day 1 even if Phase 5 ships later. | Design-time block |
| **Phase 5** — HA + tracing | Finance SLA is higher than ops. Month-end close can't be single-server-down. | Pre-GA block |

**Earliest sensible start for 6.1 (Petty Cash):** when Phase 1 is ~70%
done and Phase 2.6 (backups) is live. Likely Q3 2026 by current
trajectory. 6.1 can run to GA independently; 6.2 onwards waits for
Phase 4 event bus.

---

## 2. Strategic decisions (the ones worth tabling for the CFO)

| Decision | Options | Recommended | Reasoning |
|---|---|---|---|
| **GL of record** | (a) NickFinance native  (b) Tally Prime Gold  (c) QuickBooks Online  (d) Sage 50  (e) Odoo Community | **Tally Prime Gold** for year 1; revisit (a) in year 2 | Every auditor in Ghana knows Tally. GRA e-VAT via certified Tally partners. Cheapest audit-safe option. |
| **Payroll GL posting** | (a) NickHR posts directly  (b) NickHR → NickFinance → Tally | (b) | Keeps Finance as single integration point to GL of record; NickHR doesn't need to know Tally exists. |
| **AR invoice authority** | (a) Tally issues, NickFinance mirrors  (b) NickFinance issues, pushes to Tally  (c) GRA e-VAT first, both systems mirror | **(c)** — GRA e-VAT is authoritative per 2024 mandate; both NickFinance and Tally carry the GRA-returned IRN | Legal requirement; no choice. |
| **AP invoice capture** | (a) Manual entry in Tally  (b) Native NickFinance with OCR, post to Tally | (b) | 3-way match, approval chain, MoMo disbursement all want modern tooling. Tally's AP flow is dated. |
| **Banking / reconciliation** | (a) Manual in Tally  (b) NickFinance native with CSV + Hubtel webhooks | (b) | Tally bank recon is painful. CSV parsing for GCB/Ecobank/Stanbic/etc is a one-time investment. |
| **Fixed assets** | (a) Tally  (b) NickFinance native | **(a)** year 1 | Low volume for Nick (~6 sites' equipment). Tally is adequate. Revisit if asset count grows. |
| **Financial reports** | (a) Tally only  (b) NickFinance operational dashboards + Tally for statutory | (b) | Live dashboards are the whole point of modern ERP; statutory statements stay in Tally. |
| **GRA e-VAT integration** | (a) Through Tally partner  (b) Direct | **(a)** — via certified partner (Blue Skies, Persol, Hubtel) | Certification cost + GRA relationship management isn't core to Nick. |
| **Consolidation / multi-entity** | Not applicable today | Defer | Nick TC-Scan is one entity per current structure. |
| **Currency scope** | GHS + USD | GHS primary, USD as transactional | USD floats exist for Aflao cross-border; full multi-currency GL is overkill. |

---

## 3. Ghana compliance surface (non-negotiable)

Every module must handle the following, either natively or via
delegation to the GL of record:

| Requirement | Source | NickFinance responsibility | GL-of-record responsibility |
|---|---|---|---|
| **GRA e-VAT** (IRN generation per invoice, QR code on customer copy) | VAT Act 870 as amended; mandatory for VAT-registered entities since 2024 | Call partner API pre-send, stamp IRN on invoice | Retain for statutory filing |
| **VAT return (monthly, 21st)** | VAT Act 870 | Export VAT schedule CSV | File via iTaPS |
| **NHIL / GETFund / COVID compound calc** | Act 870 as amended | In tax engine (MVP) | Reconcile at month-end |
| **Withholding tax (7.5% goods, 20% services default; 3%/8%/15%/20% per Sixth Schedule)** | Income Tax Act 896 Sixth Schedule | Withhold at AP payment; generate WHT credit certificate per vendor | File monthly WHT return (iTaPS) |
| **PAYE (progressive bands 0–35%)** | Act 896 | NickHR payroll calculates; NickFinance posts accrual | File monthly PAYE return |
| **SSNIT Tier 1 (13.5% employer + 5.5% employee), Tier 2 (5% private)** | National Pensions Act 766 | NickHR; post accrual | File monthly schedule at e-SSNIT |
| **Ghana Card as TIN** | NIA Act 750; GRA 2021 transition | Capture on every vendor/employee; optional NIA verification via Margins | N/A |
| **FIC cash transaction report (>GHS 50,000)** | AML Act 1044 | Flag on voucher/invoice submit; queue report | Export for manual submission |
| **Companies Act 992 — 6-year record retention** | Act 992 §126 | 7-year retention in `petty_cash.vouchers`, `finance.ar_invoices`, etc.; WORM-ish receipts | Tally retains its own copies |
| **IFRS for SMEs financials** | ICAG mandate | Feed data | Produce statements |

**Rates & brackets live in config**, not code (see `PETTY_CASH.md §9.2`
for the pattern). Annual budget changes the VAT/PAYE rates — we want
a config edit, not a deploy.

---

## 4. Architecture (what we build, what we reuse)

### 4.1 The suite shape

```
                      ┌──────────────────────────┐
                      │   NickERP.Portal         │
                      │  (adds Finance card,     │
                      │   stats row, search)     │
                      └──────────────┬───────────┘
                                     │
          ┌──────────────────────────┼──────────────────────────┐
          │                          │                          │
          ▼                          ▼                          ▼
┌────────────────┐      ┌──────────────────────┐      ┌────────────────┐
│ NickHR         │      │ NickFinance (new)    │      │ NSCIM          │
│ - Employees    │      │ 6.1 PettyCash        │      │ - Scan portal  │
│ - Payroll      │◄────►│ 6.2 AR               │◄────►│ - ICUMS        │
│ - Attendance   │      │ 6.3 AP               │      │ - Containers   │
└────────────────┘      │ 6.4 Banking/Recon    │      └────────────────┘
                        │ 6.5 Tax Engine       │
                        │ 6.6 Fixed Assets     │
                        │ 6.7 Reporting        │
                        │ 6.8 Budgeting        │
                        │ 6.9 Journal Sync     │
                        └──────────┬───────────┘
                                   │ nightly CSV/API
                                   ▼
                          ┌────────────────┐
                          │ Tally Prime    │◄───► GRA iTaPS
                          │ (GL of record) │      SSNIT e-portal
                          └────────────────┘      External auditors
```

### 4.2 Project layout

```
platform/
  NickERP.Platform.Identity/        (ROADMAP 1.x - exists after Phase 1)
  NickERP.Platform.Tenancy/         (exists)
  NickERP.Platform.Web.Shared/      (ROADMAP 3.1)
  NickERP.Platform.Events/          (ROADMAP 4.5)
  NickERP.Portal/                   (exists)

finance/                            (new top-level folder)
  NickFinance.Core/                 (shared entities, money types, tax engine, chart of accounts)
  NickFinance.Ledger/               (event-sourced journal kernel used by all modules)
  NickFinance.WebApp/               (Blazor Server; port 5500)
  NickFinance.API/                  (REST for mobile / workflow webhooks; port 5505)
  NickFinance.PettyCash/            (6.1)
  NickFinance.AccountsReceivable/   (6.2)
  NickFinance.AccountsPayable/      (6.3)
  NickFinance.Banking/              (6.4)
  NickFinance.TaxEngine/            (6.5 — Ghana-specific rules)
  NickFinance.FixedAssets/          (6.6 — may skip for year 1)
  NickFinance.Reporting/            (6.7)
  NickFinance.Budgeting/            (6.8)
  NickFinance.GlSync/               (6.9 — CSV/API export to Tally/QBO)

services/
  NickComms.Gateway/                (extend with /api/disburse/momo — PETTY_CASH §9.3)
```

### 4.3 Data stores

- **`nickscan_finance`** (new Postgres DB, same cluster) owns:
  - schema `finance` — GL-native tables (accounts, journals, lines, periods)
  - schema `petty_cash` — per PETTY_CASH.md
  - schema `ar` — customers, invoices, receipts, credit notes
  - schema `ap` — vendors, bills, payments, WHT
  - schema `banking` — bank accounts, statements, reconciliations
  - schema `tax` — rate tables, e-VAT queue, WHT credit certs
  - schema `reporting` — materialized views fed by above
- **`nickscan_reporting`** (ROADMAP 4.3) — cross-app facts joined with finance
- **Tally Prime** — separate Windows install; its own SQLite/ODBC data store; receives journal exports

### 4.4 Money type

Canonical `Money` value type used across all modules:

```csharp
public readonly record struct Money
{
    public long Minor { get; init; }          // pesewa for GHS, cents for USD
    public string CurrencyCode { get; init; } // ISO 4217

    public Money Add(Money other) { ... }      // requires same currency
    public Money Multiply(decimal rate) { ... } // banker's rounding
    public static Money Zero(string ccy) { ... }
}
```

Postgres columns: `amount_minor BIGINT NOT NULL`, `currency_code CHAR(3)
NOT NULL`. Never `DECIMAL`. Never `DOUBLE`. Banker's rounding on every
conversion. Residuals from allocations tracked explicitly.

### 4.5 Event-sourced ledger kernel

`NickFinance.Ledger` is the heart. Every financial fact is a
`LedgerEvent` — append-only, immutable, partitioned by period.
Projections (account balances, trial balance, AR aging) rebuild on
demand from events up to a watermark.

```
ledger_events
  event_id           uuid pk
  ts                 timestamptz           -- wall clock
  effective_date     date                  -- accounting date
  period_id          uuid                  -- which period
  source_module      text                  -- 'petty_cash','ar','ap',...
  source_entity_type text
  source_entity_id   uuid
  idempotency_key    text unique           -- prevent double-post
  event_type         text                  -- 'Posted','Reversed','Reclassified'
  actor_user_id      uuid
  payload            jsonb                 -- the actual journal: lines, accounts, dimensions

ledger_event_lines    -- unpacked for query performance
  event_id           uuid fk
  line_no            smallint
  account_code       text
  dr_minor           bigint                -- one of dr_minor / cr_minor will be 0
  cr_minor           bigint
  currency_code      char(3)
  -- dimensions (hybrid of first-class columns + jsonb for arbitrary)
  site_id            uuid null
  project_code       text null
  cost_center_code   text null
  dims_extra         jsonb null
  PRIMARY KEY (event_id, line_no)
```

Invariant enforced with a deferred constraint trigger:
`SUM(dr_minor) = SUM(cr_minor)` per `event_id`. No UPDATE, no DELETE on
committed events — corrections go through a *Reversal* event.

Period lock is a row in `accounting_periods` with status
`OPEN | SOFT_CLOSED | HARD_CLOSED`. Posting validates status; soft-close
allows controller with `Finance.PeriodAdjust` scope; hard-close is
irreversible except via a prior-period adjustment journal.

### 4.6 Dimension-based accounting

COA stays flat: `4000-REVENUE-SCAN-FEE` not
`4000-TEMA-SCAN-REVENUE-IMPORT`. Site, project, department are
**dimensions** on each line, stored as first-class FK columns where
known (`site_id`, `project_code`) and open-ended jsonb for the rest.
This matches Sage Intacct / Odoo's analytic-account pattern and
prevents COA explosion across six sites × N projects × M departments.

### 4.7 Integration map (which module calls what)

| From | To | Via |
|---|---|---|
| Scan completion (NSCIM) | AR auto-invoice (NickFinance.AR 6.2) | Event bus `scan.completed` (ROADMAP 4.5) |
| AR invoice issued | GRA e-VAT IRN | Partner API (Tally integrator) |
| Payroll run (NickHR) | GL accrual | Event `payroll.run.approved` → NickFinance.Ledger |
| AP bill approved | MoMo disbursement | `NickComms.Gateway /api/disburse/momo` (extends petty-cash work) |
| Hubtel settlement webhook | Bank reconciliation auto-match | NickComms → event bus → `NickFinance.Banking` |
| Customer pays invoice via MoMo | AR receipt | Same webhook path; match on invoice ref in metadata |
| Month-end close | Tally journal sync | `NickFinance.GlSync` CSV export |
| Daily | Cash position dashboard (Portal) | `/api/finance/cash-position` |
| WhatsApp approval of payment run | Event back to AP | NickComms WhatsApp webhook (Phase 6.3) |

---

## 5. Module-by-module plan

Each module is ~6–10 weeks. The whole suite is 12–18 months with one
dev pair + part-time finance reviewer. Ship in sequence; each module
is independently useful.

### 6.1 Petty cash (spec: `docs/modules/PETTY_CASH.md`)

*14-week plan already detailed; see that doc.* This is the pathfinder
module — establishes `NickFinance.Core`, `NickFinance.Ledger`, the
Money type, the dimension pattern, the Hubtel MoMo extension to
NickComms, the tax engine scaffold. Everything after 6.1 reuses these.

**Deliverable for downstream modules:**
- Working Ledger with posted events, balanced invariant, period lock
- Tax engine with GHS rates (VAT/NHIL/GETFund/COVID/WHT)
- MoMo disbursement through NickComms
- Approval engine with YAML DSL
- Receipt/OCR pipeline (Azure Form Recognizer)

### 6.2 Accounts Receivable + scan-to-invoice (8–10 weeks)

**Entities:** `customers`, `ar_invoices`, `ar_invoice_lines`, `ar_receipts`,
`ar_credit_notes`, `dunning_schedules`, `aging_snapshots`.

**The differentiated feature:** scan completion → AR invoice auto-creation.

```
scan.completed event
  → lookup customer from declaration_number
  → compute fee from scan_type + container_size + customer_tariff
  → draft invoice
  → call GRA e-VAT partner API, await IRN
  → send invoice PDF via NickComms (email + WhatsApp) with MoMo pay link
  → post journal: DR Accounts Receivable, CR Scan Fee Revenue, CR VAT Output, CR NHIL, CR GETFund, CR COVID
```

Customer pays via MoMo → Hubtel webhook → AR receipt auto-created →
journal DR Cash-MoMo-Clearing, CR Accounts Receivable → invoice closed.

**Other AR:**
- Manual invoicing for non-scan services
- Credit notes with reason codes
- Aging report (0/30/60/90/90+)
- Dunning automation via NickComms SMS+email
- Customer statements
- Customer portal (phase 2) — customers see their own invoices, download, pay

**Tasks (high level):**
- [ ] Customer master (pulls from ICUMS known-consignee list where possible)
- [ ] Invoice model + state machine (Draft → Issued → Paid/PartiallyPaid/Overdue/Void)
- [ ] GRA e-VAT integration via Tally partner (or direct if we get certified)
- [ ] Scan-to-invoice automation (depends on Phase 4 event bus)
- [ ] Receipt auto-match (depends on Hubtel webhook from 6.1)
- [ ] Aging report + dunning workflow
- [ ] Month-end: write-off, bad-debt provisioning

**Acceptance:** a scan completed for a known consignee on Tuesday
auto-generates an e-VAT invoice by Wednesday morning, customer gets
WhatsApp with a pay link, pays on Thursday via MTN MoMo, AR closed
automatically by Friday. Zero human touches.

### 6.3 Accounts Payable + AP automation (8–10 weeks)

**Entities:** `vendors`, `ap_bills`, `ap_bill_lines`, `purchase_orders`,
`goods_received_notes`, `ap_payments`, `wht_credit_certificates`.

**Key features:**
- Vendor master with Ghana Card PIN / TIN, VAT-registered flag, default
  MoMo number, default GL account
- Bill capture: OCR via Azure Form Recognizer; GRA e-VAT invoices
  parsed from their QR code (no OCR needed — QR contains IRN + amount)
- 3-way match: PO ↔ GRN ↔ bill (soft enforcement v1, hard v2)
- Approval routing reuses the engine from 6.1 (PettyCash policy DSL)
- Payment runs: batch of approved bills → cash/MoMo/bank transfer →
  WHT auto-withheld based on vendor category → WHT credit certificate
  generated + PDF-emailed to vendor
- 1099-equivalent: annual WHT summary per vendor for iTaPS filing

**Differentiated features:**
- WhatsApp approval of payment runs (Meta Cloud API) — CFO approves
  GHS 500K payment run from their phone, audit trail in webhook logs
- TIN validation: call Margins (NIA) to verify Ghana Card matches
  name at bill capture; flag mismatches
- Duplicate bill detection via pHash on receipt image + amount/date
  match across the vendor's history

**Acceptance:** bill uploaded → OCR extracts vendor/date/total within 5s
→ matches PO if one exists → routes to approvers per policy → CFO
approves on WhatsApp → batch pays 8 vendors via Hubtel → WHT
certificates emailed → all journals posted. One-click process from
CFO's side.

### 6.4 Banking & Reconciliation (6–8 weeks)

**Entities:** `bank_accounts`, `bank_statements`, `bank_transactions`,
`reconciliation_sessions`, `matching_rules`.

**Inputs:**
- Manual CSV upload from GCB NetBank, Ecobank Omni, Stanbic Business
  Online, Fidelity, Cal Bank, Zenith (format varies — per-bank parser)
- Hubtel MoMo settlement webhook (already wired in 6.1 for disburse;
  extend to receipts)
- GhIPSS instant pay webhook (if we connect it)
- Ecobank API (one of the few Ghana banks with public REST)

**Matching engine:**
- Exact: amount + date + reference → auto-match to AR receipt or AP
  payment
- Fuzzy: amount ± GHS 0.50, date ± 2 days, partial reference match →
  suggest match, require human confirm
- Rules: user-defined ("any transfer from MTN MoMo with 'SCAN' in
  narrative → match to AR")
- Unmatched → staging area for GL posting

**Cash position dashboard (portal tile):**
- Each of 6 sites × currency × account type (cash box, MoMo wallet,
  bank) = a live cell. Click to drill.
- Transfers in transit highlighted (multi-day settlement).
- Forecast: 13-week cash flow from scheduled AR receipts + AP
  payments + payroll.

**Acceptance:** upload GCB statement for yesterday → 90%+ auto-match →
unmatched items in exception queue → Finance clears in <15 minutes.
Today's cash position visible on portal within 3 seconds.

### 6.5 Tax engine (4–6 weeks, largely done as part of 6.1–6.3)

Centralizes what 6.1–6.3 each implemented:
- Rate tables (config file, hot-reload)
- VAT/NHIL/GETFund/COVID compound calculator (unit-tested to the cent)
- WHT classification by vendor type + transaction type
- iTaPS schedule generation (VAT return, WHT return, annual income tax
  helper) — CSV format that iTaPS accepts
- e-SSNIT contribution schedule (Excel format)
- GRA e-VAT integration (direct if certified, else via partner)
- Annual VAT reconciliation report

**Acceptance:** end-of-month, Finance clicks "Generate VAT return" →
CSV downloads → upload to iTaPS, accepted without edits. Same for WHT
and SSNIT.

### 6.6 Fixed Assets (4 weeks — optional year 1)

For Nick's volume, Tally does this adequately. If we build:
- Asset register (scanner equipment, vehicles, buildings, IT)
- Depreciation methods: straight-line, declining balance
- IFRS 16 lease accounting (if we ever lease premises)
- Disposal/impairment with auto-journal
- Asset barcode/QR for physical verification (fits our existing
  scanner infrastructure!)

**Recommendation:** defer to year 2 unless a specific business need
arises.

### 6.7 Financial Reporting (6–8 weeks)

- Trial balance (drillable)
- P&L — current period, YTD, prior-year comparison, by dimension
- Balance sheet — same
- Cash flow statement — indirect method v1, direct v2
- Custom report builder (pick dimensions, accounts, periods)
- Export to Excel / PDF
- Board pack — 5–10 canned reports CFO runs for monthly board meeting

Lives in `NickFinance.Reporting` project. Reads from `reporting` schema
materialized views. **Does not replace Tally's statutory statements**
— those remain the legal filings.

### 6.8 Budgeting & Forecasting (4–6 weeks)

- Annual budget by dept × month × account
- Budget vs actual with variance
- Rolling 13-week cash forecast (fed from AR + AP + payroll schedule)
- Scenario modeling: "what if we add a 7th site?"
- Driver-based planning lite: revenue driver = scan volume × tariff,
  cost driver = headcount × average cost

**Acceptance:** CFO enters 2027 budget in Excel, imports → monthly
variance reports auto-generate from actual postings.

### 6.9 GL of record sync (2–3 weeks)

`NickFinance.GlSync` — nightly (or on-demand) export job.
- Summarizes the day's posted journals
- Produces a Tally-importable XML (Tally uses an XML import format)
  OR QuickBooks-compatible IIF/CSV
- Job handles: idempotency (don't re-post), reconciliation (if Tally
  already has something with our idempotency key, skip)
- Failure alerts via Seq → Finance Slack/email

Year 2 option: remove Tally, NickFinance becomes GL of record. Requires
ICAG audit sign-off and GRA e-VAT direct certification.

---

## 6. Phased rollout (quarters, not weeks)

| Quarter | Scope | Cumulative value |
|---|---|---|
| **Q3 2026** | 6.1 Petty Cash GA across all 6 sites | Digital petty cash; cash-in-field goes away |
| **Q4 2026** | 6.2 AR + scan-to-invoice; 6.5 tax engine | Hands-free invoicing; e-VAT compliance |
| **Q1 2027** | 6.3 AP + OCR + WhatsApp approval | Hands-free payables; WHT handled |
| **Q2 2027** | 6.4 Banking & reconciliation; 6.9 GL sync | Cash visible; Tally stays authoritative |
| **Q3 2027** | 6.7 Reporting; 6.8 Budgeting | CFO has live P&L, variance reports |
| **Q4 2027** | 6.6 Fixed assets (optional) | Full suite feature-complete |
| **2028** | Optional 6.10: cut over from Tally to NickFinance as GL of record | Own the full stack |

---

## 7. Engineering budget

**Assumptions:** two senior .NET devs + one part-time finance reviewer
(CPA/CA-equivalent, 10 hrs/week); Ghana senior dev rate GHS 20K/month
≈ USD 1,700/month; finance reviewer GHS 10K/month ≈ USD 850/month.

| Cost line | Year 1 (2026) | Year 2 (2027) | Year 3 |
|---|---|---|---|
| 2 × senior devs | $40,800 | $40,800 | $20,400 (one dev post-GA) |
| Finance reviewer | $10,200 | $10,200 | $5,100 |
| Tally Prime Gold + Ghana partner AMC | $3,000 | $3,000 | $3,000 |
| Azure Form Recognizer | $600 | $1,200 | $1,800 |
| GRA e-VAT partner integration | $5,000 one-time | — | — |
| Hubtel merchant (already in use) | 0 | 0 | 0 |
| NIA verification (Margins) | $1,200 | $1,200 | $1,200 |
| OpenExchangeRates (FX) | $1,164 | $1,164 | $1,164 |
| Seq (already in roadmap Phase 2) | 0 | 0 | 0 |
| **Total** | **~$62K** | **~$58K** | **~$33K** |
| **Cumulative** | | **~$120K** | **~$153K** |

Compare to:
- **NetSuite implementation**: $150–400K year one
- **Sage X3 implementation**: $100–250K year one
- **Full in-house rebuild (no Tally)**: $250–400K year one

---

## 8. Risk register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| GRA e-VAT partner onboarding drags | Medium | High (6.2 blocker) | Start partner conversations in 6.1 alongside PettyCash; don't wait for 6.2 |
| Ledger kernel has a subtle balance-invariant bug | Low | Catastrophic | Exhaustive property-based testing (FsCheck); parallel-run against Tally for first month; daily variance report that fails loud |
| Tally integration format drifts | Medium | Low | Pin Tally version; regression tests on sample journals |
| Hubtel settlement webhook double-fires | High (common) | Medium | Idempotency on `clientReference` at webhook handler |
| CFO prefers Excel over NickFinance reporting | High | Medium | Excel export on every report from day 1; treat Excel as first-class output, not consolation prize |
| Ghana tax law changes mid-build | Medium | Low | Rates in config; dedicate a 1-week "tax refresh" cycle after each year's national budget |
| Engineering team loses a dev mid-build | Medium | High | Pair programming; every module documented as it ships; no "hero knowledge" |
| Auditor rejects NickFinance journals | Low | High | Hybrid approach keeps Tally as audit source; NickFinance is operational only in year 1 |
| Offline border sites drop transactions | Medium | Medium | PWA queue (inherited from PettyCash); daily sync reconciliation report |
| FX rate ingestion fails | Low | Low | Cache last-known rates; allow manual override |

---

## 9. Differentiation summary (what makes this worth building)

From the research brief, these are features no off-the-shelf vendor has
because no off-the-shelf vendor runs a customs-scanning business:

| # | Feature | Off-the-shelf equivalent | Value |
|---|---|---|---|
| 1 | **Scan → AR invoice** automation (ICUMS declaration → e-VAT invoice → pay link, no humans) | None | Eliminates 90%+ of invoicing labour; DSO drops by days |
| 2 | **MoMo-native bank reconciliation** (Hubtel webhook → auto-match) | Manual CSV recon | 10× faster; eliminates month-end recon pain |
| 3 | **Geo-tagged cash count** (PettyCash custodian must be at site per NickHR GPS) | None | Anti-collusion control |
| 4 | **Multi-site cash positioning** (live, per-currency, per-site) | End-of-day batch | Treasury decisions same day |
| 5 | **WhatsApp payment-run approval** (CFO on phone, full audit trail) | Email + portal login | Removes "CFO travelling" bottleneck |
| 6 | **Ghana Card vendor KYC** (NIA API verification at onboarding) | Manual TIN entry | Ghost-vendor fraud blocker |
| 7 | **TIN-aware auto-WHT** (rate determined by vendor's GRA registration status) | Manual rate picker | Errors reduce to zero |
| 8 | **Scan-invoice dispute resolution** (invoice links back to X-ray + declaration) | Paper file hunt | Cuts dispute resolution from days to minutes |
| 9 | **Customs-aware GL** (HS-code-driven VAT computation for scan fees) | Flat-rate entry | Accuracy with scale |

Items 3, 5, 6, 7, 8 alone are worth more than a Tally subscription.

---

## 10. Decisions needed before 6.1 starts

Same 10 listed in `PETTY_CASH.md §13`, plus these platform-level ones:

| # | Decision | Who decides |
|---|---|---|
| P1 | Tally Prime vs QuickBooks vs Sage 50 as GL of record | CFO |
| P2 | GRA e-VAT partner choice (Blue Skies / Persol / Hubtel / direct certification) | CFO + CTO |
| P3 | NIA verification provider (Margins vs others) | CTO |
| P4 | Banking list — which Ghana banks do we maintain CSV parsers for? (GCB, Ecobank, Stanbic are baseline; add Fidelity/Cal/Zenith?) | CFO |
| P5 | Budget cadence — annual only, or rolling quarterly? Changes 6.8 scope | CFO |
| P6 | Multi-currency — USD only, or also EUR/GBP? | CFO |
| P7 | Consolidation — will Nick acquire subsidiaries in the next 2 years? If yes, design multi-entity now | CEO |
| P8 | GL-of-record sunset — year 2 target to replace Tally? Or Tally forever? | CFO + auditor |

---

## 11. Immediate next actions

Before committing the 12–18 month path, run three de-risk spikes (each
≤1 week):

1. **Tally import format spike** — hand-craft a journal XML, import
   into Tally Prime Gold sandbox, verify round-trip integrity. Confirms
   6.9 feasibility.
2. **GRA e-VAT partner spike** — reach out to 2–3 certified partners
   (Blue Skies, Persol, Hubtel), get integration docs + pricing,
   submit a test invoice through their sandbox.
3. **Ledger kernel spike** — implement `NickFinance.Ledger` core with
   posted-event insert + balance invariant + period lock. Post 1,000
   random journals via a generator; assert invariants. 2–3 days of
   work, proves the foundation.

If all three green-light, kick off **ROADMAP Phase 1 in full** and
begin 6.1 scaffolding in parallel. Identity lands mid-way through 6.1;
we're fine falling back to NickHR user lookup until it does.

---

## 12. Addendum to `ROADMAP.md`

Propose this diff to the main roadmap:

```diff
### Infrastructure that landed recently ✅
+ [ ] (add when 6.1 ships) Petty Cash module GA — docs/modules/PETTY_CASH.md
+ [ ] (add when first external audit of NickFinance journals passes)

  ### What's still not right
  ...

+ | 23 | No finance module (GL, AR, AP, banking, tax) — business runs
+      | on spreadsheets + Tally outside NickERP | Finance |
+ | 24 | Scan → invoice is manual; DSO wastes days per scan              | Revenue |
+ | 25 | No statutory tax filing tooling — VAT/WHT/SSNIT done by hand  | Compliance |

### Phase plan
...

+### Phase 6 — NickFinance suite (quarters)
+See docs/modules/NICKFINANCE_PLATFORM.md. Depends on Phases 1, 2, 4.
+Modules 6.1 → 6.9; Q3 2026 → Q4 2027; ~$120K year 1–2 engineering.
```

Applied separately when this plan is approved.

---

## 13. Sources

Research brief used as input to this plan cited:
- ACCA F8 (Audit & Assurance); COSO 2013
- VAT Act 870 (as amended 2023); Income Tax Act 896
- National Pensions Act 766 (SSNIT Tier 1/2/3)
- NIA Act 750 (Ghana Card)
- Companies Act 992 §126
- AML Act 1044 (2020 amendment)
- ICAG IFRS-for-SMEs templates
- Bank of Ghana Payment Systems Act 987
- Hubtel Merchant API docs
- GRA e-VAT Certified Invoice Registration spec
- Vendor comparison: Sage Intacct, NetSuite, Xero, Zoho Books,
  QuickBooks Online, Tally Prime, Odoo, D365 Business Central
- OCR vendor survey: Rossum, Mindee, Azure AI Document Intelligence,
  AWS Textract
