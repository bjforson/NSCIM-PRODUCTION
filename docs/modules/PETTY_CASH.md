# NickFinance · Petty Cash Module — Plan

> Module: `NickFinance.PettyCash`
> Placement in ROADMAP: new module, Phase 1.x of platform roadmap
> Status: planning / no code written yet
> Related docs: `ROADMAP.md`, `SSO.md`, `PLATFORM.md`

---

## 1. Executive summary

Petty cash management for Nick TC-Scan Ltd — a Ghana customs-scanning
operation with 147 employees across six sites (Tema Port HQ, Kotoka
Airport, Takoradi Harbour, Aflao Border, Paga Border, Tema Main/Sentry).
The module runs as a NickERP sub-app, inherits Cloudflare Access for
edge auth, integrates with NickHR for org hierarchy, with NickComms
for email/SMS/mobile-money disbursement, and exports to the unified
audit log when that lands.

**What it delivers:**
- Imprest-system float management per site and per custodian
- Configurable multi-layer approval chains (amount + category + org-path)
- Mobile-money disbursement via Hubtel Merchant API (re-uses NickComms)
- Receipt capture (photo upload + OCR + duplicate detection)
- Ghana tax handling (VAT/NHIL/GETFund/COVID levies, WHT)
- Fraud signals: salami-slicing, ghost payees, Benford anomalies,
  duplicate receipts, GPS mismatch
- Journal-entry export for downstream accounting (Tally / QuickBooks / Sage)
- Mobile-first UI (responsive web) with offline queue for border sites

**What it deliberately does not do (v1):**
- Full general-ledger replacement
- Corporate card issuance (Ramp/Pleo-style) — Ghana card rails don't
  support this cheaply yet
- Non-petty procurement (>GHS 10,000) — routes to separate procurement
  workflow when that module exists

---

## 2. Business context

### Who uses it

| Role | Responsibility | Typical count at Nick |
|---|---|---|
| **Requester** | Any employee who needs cash for a business purpose | ~147 |
| **Line manager** | 1st-level approver in their reportee chain | ~15 |
| **Site supervisor** | Approves disbursements tied to their site | 6 |
| **Finance officer** | Policy compliance, GL coding, replenishment approval | 2–3 |
| **Custodian** | Physically holds cash / MoMo float at a site | 1 per site = 6 |
| **Internal auditor** | Read-only + flags, surprise counts | 1 |
| **Admin** | Configure policies, floats, categories, approvers | 1–2 |

### Where it lives in the suite

```
https://erp.nickscan.net          →  portal (add PettyCash card)
https://finance.nickscan.net      →  NickFinance sub-app (new)
   /petty-cash                    →  module root
   /petty-cash/vouchers           →  list + filters
   /petty-cash/vouchers/new       →  submit
   /petty-cash/vouchers/{id}      →  detail + approvals timeline
   /petty-cash/floats             →  custodian view
   /petty-cash/reconciliation     →  daily cash count
   /petty-cash/admin/policies     →  approval matrix editor
   /petty-cash/reports            →  spend analytics, fraud flags
```

Single new hostname `finance.nickscan.net` protected by the existing
Cloudflare Access app (just add to `self_hosted_domains`).

---

## 3. Architectural decisions (proposed)

| Decision | Chosen | Alternative considered | Rationale |
|---|---|---|---|
| **Standalone module vs extension of NickHR** | Standalone (`NickFinance.PettyCash`) | HR sub-page | ERP direction is modular; petty cash is the seed of a Finance module family (later: AP, AR, GL). |
| **New project vs shared project** | New Blazor Server project `platform/NickFinance.PettyCash/` | Page in portal | Clean separation; grows into NickFinance suite. |
| **DB** | New Postgres DB `nickscan_finance`, schema `petty_cash` | New schema inside `nickscan_production` | Finance data has different RPO/retention than scan data; separate DB keeps backup/restore independent. |
| **User identity** | Reads from canonical `identity.users` (ROADMAP Phase 1) | Own user store | Avoids a 3rd user store; Phase 1 blocker if not ready in time — fallback is read from `nickhr.Employees` directly by email. |
| **Org hierarchy source** | Reads from NickHR `Employees` / `Positions` / `OrgUnits` | Local copy | Single source of truth for manager chains. |
| **Mobile-money disbursement** | Extend `NickComms.Gateway` with `/api/disburse` endpoint backed by Hubtel Merchant API | Direct integration in NickFinance | NickComms already integrates Hubtel (for SMS/OTP) — same vendor, same auth, same settlement account. One place to rotate credentials. |
| **OCR** | Azure Form Recognizer *prebuilt-receipt* model (cheap, ~$0.30/1000), Tesseract fallback | Google Document AI / AWS Textract / local Tesseract only | Best accuracy on Ghanaian thermal receipts; offline fallback so border sites work when Starlink drops. |
| **Receipts storage** | Local disk `C:\Shared\NSCIM_PRODUCTION\Data\PettyCash\Receipts\{yyyy}\{mm}\{voucher-id}-{ordinal}.jpg` | Azure Blob / S3 | Matches existing file-handling pattern; migrate to object store when platform goes HA (ROADMAP Phase 5). |
| **Workflow engine** | Built-in state machine (enum + DB-backed transitions table) | Elsa / WF Core / OPA | Configurable enough for v1 without pulling in a heavy engine. |
| **Policy-as-code** | YAML policy in `appsettings.PettyCash.yaml` (hot-reload), with an admin editor that writes YAML | Only DB-backed rules | Readable in git; reviewable via PR; still editable in UI. |
| **Mobile** | Responsive web (matches NickHR.Mobile retirement decision) | Separate mobile app | Until roadmap revisits mobile, stay responsive-only. |
| **WhatsApp approvals** | Phase 3 (deferred) — via WhatsApp Business Cloud API | Day-1 | Audit-trail requirement; Cloud API webhooks give us that. |

### Tech stack

- .NET 10 Blazor Server, MudBlazor 8.13.0 (matches portal + sub-apps)
- EF Core 10, Npgsql 10
- Serilog → Seq (ROADMAP 2.1)
- MassTransit or plain `LISTEN/NOTIFY` for approval events (defer decision)
- Azure Form Recognizer SDK for OCR
- Hubtel Merchant API for MoMo (via NickComms extension)

---

## 4. Data model

Schema: `petty_cash` in DB `nickscan_finance`. Canonical currency: `GHS`
(ISO 4217), amounts in **minor units (pesewa = GHS/100) as `bigint`** to
avoid float drift. Multi-currency fields included but only GHS active v1.

### 4.1 Entities

```
petty_cash.floats
  id                 uuid pk
  site_id            uuid fk -> identity.sites      -- reuse from NickHR sites
  custodian_user_id  uuid fk -> identity.users
  currency_code      char(3)   default 'GHS'
  float_amount       bigint    -- GHS in pesewa (e.g. 500000 = GHS 5,000)
  replenish_threshold_pct smallint default 25
  is_active          bool      default true
  created_at         timestamptz
  created_by         uuid
  closed_at          timestamptz null
  closed_by          uuid null
  -- invariants: only one active float per (site_id, currency_code)

petty_cash.float_topups
  id                 uuid pk
  float_id           uuid fk
  amount             bigint
  requested_by       uuid
  approved_by        uuid
  approved_at        timestamptz
  journal_ref        text       -- link to GL entry
  notes              text

petty_cash.vouchers
  id                 uuid pk
  voucher_no         text unique      -- human "PC-TEMA-2026-00421" auto-generated
  float_id           uuid fk
  requester_user_id  uuid fk
  category_id        uuid fk
  purpose            text            -- free text; mandatory
  amount             bigint          -- requested (pesewa)
  amount_approved    bigint null     -- may differ from requested
  currency_code      char(3)
  request_date       timestamptz
  needed_by          date null
  payee_type         text            -- 'self' | 'employee' | 'vendor'
  payee_user_id      uuid null       -- if employee
  payee_vendor_id    uuid null
  payee_momo_number  text null       -- if cashed via MoMo
  payee_momo_network text null       -- 'MTN','VODA','ATM'
  project_code       text null       -- optional cost-centre tag
  policy_version     text            -- snapshot of policy in force at submit
  status             text            -- see state machine
  submitted_at       timestamptz null
  disbursed_at       timestamptz null
  reconciled_at      timestamptz null
  created_at         timestamptz

petty_cash.voucher_line_items
  id                 uuid pk
  voucher_id         uuid fk
  description        text
  quantity           numeric(12,3)
  unit_amount        bigint
  gross_amount       bigint
  vat_amount         bigint           -- 15% slice
  nhil_amount        bigint
  getfund_amount     bigint
  covid_amount       bigint
  wht_amount         bigint           -- withholding
  net_amount         bigint
  gl_account         text             -- chart-of-accounts code

petty_cash.voucher_approvals
  id                 uuid pk
  voucher_id         uuid fk
  step_no            smallint         -- 1,2,3...
  approver_user_id   uuid
  is_delegated       bool
  delegated_from_user_id uuid null
  decision           text             -- 'pending','approved','rejected','escalated','skipped'
  decision_at        timestamptz null
  comment            text null
  idempotency_key    text unique      -- prevents double-clicks / email-link replay

petty_cash.voucher_receipts
  id                 uuid pk
  voucher_id         uuid fk
  ordinal            smallint
  file_path          text
  file_hash_sha256   text
  perceptual_hash    bigint           -- pHash for duplicate detection
  ocr_vendor         text null
  ocr_amount         bigint null
  ocr_date           date null
  ocr_raw_text       text null
  ocr_confidence     smallint null    -- 0..100
  uploaded_by        uuid
  uploaded_at        timestamptz
  gps_lat            numeric(9,6) null
  gps_lng            numeric(9,6) null

petty_cash.categories
  id                 uuid pk
  code               text unique       -- 'TRANSPORT','FUEL','WELFARE','OFFICE_SUPPLIES',...
  name               text
  default_gl_account text
  is_active          bool
  requires_fields    jsonb             -- e.g. {'destination':'required','km':'required'} for TRANSPORT
  max_amount         bigint null       -- cap per voucher

petty_cash.approval_policies
  id                 uuid pk
  effective_from     date
  policy_yaml        text              -- full rule set, version-controlled
  created_by         uuid
  created_at         timestamptz

petty_cash.approval_delegations
  id                 uuid pk
  user_id            uuid              -- who is away
  delegate_user_id   uuid              -- who covers
  valid_from         timestamptz
  valid_until        timestamptz
  reason             text

petty_cash.cash_counts
  id                 uuid pk
  float_id           uuid fk
  counted_at         timestamptz
  counted_by         uuid
  physical_amount    bigint
  system_amount      bigint
  variance           bigint generated always as (physical_amount - system_amount) stored
  variance_reason    text null
  witness_user_id    uuid null

petty_cash.vendors
  id                 uuid pk
  name               text
  tin_ghana_card     text null         -- Ghana Card PIN
  is_vat_registered  bool
  default_gl_account text null
  momo_number        text null
  bank_account       text null
  wht_exempt         bool default false

petty_cash.budgets
  id                 uuid pk
  scope_type         text              -- 'site','category','user','site_category'
  scope_id           text
  period_start       date
  period_end         date
  amount             bigint
  consumed           bigint            -- rolling sum of approved vouchers
  alert_threshold_pct smallint default 80

petty_cash.audit_events          -- shadows platform audit; mirrored there too
  ...                            -- see Section 11
```

### 4.2 Voucher state machine

```
           ┌─── submit (requester) ───►  Submitted
 Draft ────┤
           └─── abandon ─────────────►  Withdrawn

 Submitted ─── decision step n ────►  InApproval
                                          │
              all steps approved          │ any step rejected
                    ▼                     ▼
                Approved               Rejected
                    │                     │
   disburse (custodian, cash or MoMo)     │ requester may clone & resubmit
                    ▼
                Disbursed
                    │
   receipts attached + OCR matched
                    ▼
                Reconciled
                    │
   period close (auto)
                    ▼
               Closed (immutable)
```

Additional terminal states: `Cancelled` (admin override, reason required),
`ExpiredNotDisbursed` (approved but no disbursement within N days — returns budget).

---

## 5. Approval engine design

### 5.1 Policy DSL (YAML, hot-reloadable)

```yaml
version: 2026-04-24
defaults:
  currency: GHS
  escalation_hours: 48
  require_receipts_above: 10000        # pesewa; GHS 100

categories:
  TRANSPORT:
    gl_account: "7410-TRANSPORT"
    require_fields: [destination]
    bands:
      - { max: 20000,  steps: [line_manager] }                         # ≤ GHS 200
      - { max: 100000, steps: [line_manager, site_supervisor] }        # ≤ GHS 1,000
      - { max: 500000, steps: [line_manager, site_supervisor, finance] } # ≤ GHS 5,000

  FUEL:
    gl_account: "7420-FUEL"
    bands:
      - { max: 50000,  steps: [site_supervisor] }
      - { max: 300000, steps: [site_supervisor, finance] }

  EMERGENCY:
    require_dual: true                 # two approvers at same step
    bands:
      - { max: 1000000, steps: [[site_supervisor, finance]] }          # both required

escalation:
  no_action_hours: 48
  escalate_to: next_up_org_hierarchy

delegation:
  allowed_for_roles: [line_manager, site_supervisor, finance]

```

`steps` entries resolve at runtime against the canonical identity
service: `line_manager` = requester's direct manager from HR; `finance`
= anyone with `AppScope: Finance_Approver`; `site_supervisor` =
supervisor of requester's current site.

### 5.2 Runtime flow

1. Voucher submitted → engine snapshots current policy into
   `voucher.policy_version` (immutable once set).
2. Engine resolves approver chain: expands role names to user IDs via
   Identity + HR graph. Rejects submission if any role can't be
   resolved (e.g. no line manager recorded) with a helpful error.
3. Approval rows (`voucher_approvals`) pre-populated with
   `decision='pending'` for each step.
4. Notification sent (email + SMS + in-app) to step 1 approver(s).
5. Approver clicks link in email → authenticates via Access →
   decision page → approve/reject with comment.
6. Engine advances to next step, or marks voucher `Approved` /
   `Rejected`.
7. Idempotency: decision endpoint requires
   `idempotency_key` (issued in notification link) so email-link replay
   or double-click can't create double decisions.
8. On timeout (`escalation_hours`): auto-escalate to approver's manager;
   log escalation to audit.

### 5.3 Delegation

- Manager going on leave inserts `approval_delegations` row with
  date range and covering user.
- Engine checks active delegation before assigning step;
  `is_delegated=true` and `delegated_from_user_id` recorded for audit.
- UI surfaces "Approving on behalf of X" prominently so covering
  approver knows the context.

### 5.4 Separation of duties (hard-enforced at DB/API level)

- Requester cannot appear in their own approval chain (engine skips
  with `decision='skipped'`, reason logged).
- Custodian cannot disburse their own voucher.
- Same user cannot do both approval and reconciliation on the same
  voucher.
- Admin override is possible but always flagged in audit and requires
  a justification comment.

---

## 6. UI pages & UX notes

All pages use the shared `TopNav` from `NickERP.Platform.Web.Shared`
(ROADMAP Phase 3) so nav is consistent with portal + HR + NSCIM.

| Route | Audience | Key elements |
|---|---|---|
| `/petty-cash` | Everyone | Summary: my open vouchers, my pending approvals, my site's float runway |
| `/petty-cash/vouchers` | Everyone | Filterable list (status, date, site, amount, requester); bulk export |
| `/petty-cash/vouchers/new` | Requesters | Wizard: category → details → line items → receipts (optional at submit) → preview → submit |
| `/petty-cash/vouchers/{id}` | Everyone involved | Timeline of events, approvals status, receipts, GL coding, disbursement action (if custodian), reconciliation action (if finance) |
| `/petty-cash/approvals` | Approvers | "Awaiting my decision" queue with inline approve/reject |
| `/petty-cash/floats` | Custodians, Finance | Float balance, recent disbursements, replenishment request button |
| `/petty-cash/reconciliation` | Custodians | Daily cash count entry (physical vs system), variance justification |
| `/petty-cash/admin/policies` | Admins | YAML editor with live validation + "diff vs current" view |
| `/petty-cash/admin/categories` | Admins | CRUD for categories + GL mapping |
| `/petty-cash/admin/delegations` | Self-service | Set "away" dates + delegate |
| `/petty-cash/admin/vendors` | Finance | Vendor registry (Ghana Card / TIN / MoMo) |
| `/petty-cash/admin/budgets` | Finance | Per-site / per-category monthly budgets + consumption |
| `/petty-cash/reports` | Finance, Auditor, Admin | Spend by site/category/time; fraud-flag list; float forecasts |

### UX particulars

- **Mobile-first submit**: single-column form, camera-capture button for
  receipts, auto-crop edges, GPS tagged.
- **Field-offline mode**: if Starlink drops at Paga/Aflao, submit is
  queued in IndexedDB + Service Worker and synced when back online.
  Voucher gets `queued_offline=true` flag for audit.
- **Inline policy preview**: while filling the form, show live
  "Will need approval from: Mary (line manager), Kofi (site supervisor)"
  so the requester knows what to expect.
- **Receipt matching badge**: after OCR, shows "✓ Amount matches voucher"
  or "⚠️ OCR amount GHS 450 differs from voucher GHS 500 — please
  reconcile". Pure UX guidance; engine flags for auditor.

---

## 7. Features ladder

### 7.1 MVP (v1.0 — the thing that ships)

- Float setup, one per site
- Voucher submit → approve → disburse → reconcile
- Fixed 2-step approval (Line manager, then Finance above threshold)
- Categories (5 seed: Transport, Fuel, Office Supplies, Staff Welfare, Emergency)
- Receipt upload (no OCR yet)
- GHS only
- Email + in-app notifications
- CSV export for accounting
- Basic spend report

### 7.2 v1.1 (immediate next — weeks 4–6)

- Policy DSL (YAML) with hot reload
- Multi-step approval chains including parallel approvers
- Delegation
- Mobile money disbursement via Hubtel (through NickComms)
- OCR on receipts (Azure Form Recognizer)
- Ghana tax breakdown (VAT/NHIL/GETFund/COVID) per line item
- Withholding tax calculation on vendor payments

### 7.3 v1.2 (weeks 7–9)

- Recurring vouchers (weekly security allowance, daily transport)
- Bulk payouts (CSV upload → one voucher per row, all via MoMo)
- Advance vouchers (cash first, receipts later, auto-reminder)
- Budget tracking with 80% alert
- Fraud signals: salami, ghost payee, duplicate-receipt pHash,
  GPS mismatch, Benford on weekly totals
- Vendor registry
- SMS notifications

### 7.4 v1.3 (weeks 10–12)

- Offline mode (PWA + service worker)
- Multi-currency (add USD float for Aflao cross-border supplies)
- GRA e-VAT invoice registration on supplier payments above threshold
- Journal export to Tally / Sage (CSV or direct API if available)
- Internal audit module (read-only + flag + comment workflow)

### 7.5 v2.0 (later)

- WhatsApp approval flow (Meta Cloud API, audit-complete)
- ML-based vendor matching & GL auto-coding
- Forecast: "based on trends, Tema float will run out Wed — replenish"
- Mobile app for custodians (native, if responsive-web-PWA is insufficient)
- Cross-period analytics + CFO dashboard

---

## 8. Integration map

```
┌────────────────────────┐        ┌────────────────────────┐
│ NickERP.Portal         │        │ NickHR                 │
│ - card "Petty cash"    │        │ - Employees            │
│ - stats row            │        │ - OrgUnits / Positions │
│ - federated search     │        │ - Departments          │
└──────────┬─────────────┘        └────────────┬───────────┘
           │ link                               │ read-only queries
           ▼                                    ▼
┌────────────────────────────────────────────────────────────┐
│ NickFinance.PettyCash                                      │
│ ┌───────────┐  ┌─────────┐  ┌──────────┐  ┌─────────────┐  │
│ │ Web UI    │  │ API     │  │ Workers  │  │ Policy YAML │  │
│ └───────────┘  └─────────┘  └──────────┘  └─────────────┘  │
└────────────┬──────────┬─────────────────┬──────────────────┘
             │          │                 │
             ▼          ▼                 ▼
  identity.users   NickComms.Gateway    petty_cash schema
  (ROADMAP P1)     (SMS/Email/MoMo)     (nickscan_finance DB)
                        │
                        ▼
                   Hubtel Merchant API
                   (MoMo disbursement)
```

**Consumers of NickFinance events:**
- Unified audit log (ROADMAP Phase 4) — every voucher event mirrored
- Reporting DB (ROADMAP Phase 4) — for cross-app analytics
- Payroll (future) — advance recovery from payroll cycle

---

## 9. Ghana-specific implementation

### 9.1 Currency handling

- All amounts stored as `bigint` pesewa (minor units). Presentation
  layer formats as `GH₵ 5,000.00` / `GHS 5,000.00`.
- Rounding: banker's rounding on tax calculations to 2 decimals.
- `currency_code` everywhere for future multi-currency even though v1
  is GHS-only.

### 9.2 Tax engine

A single source of tax rates in config so changes to Ghana's levy
schedule require no code. Default (April 2026):

```yaml
tax_rates:
  VAT:     0.150   # 15%
  NHIL:    0.025   # 2.5%
  GETFUND: 0.025   # 2.5%
  COVID:   0.010   # 1%
  WHT_GOODS:    0.075    # 7.5% on goods/works from unregistered suppliers >GHS 2,000
  WHT_SERVICES: 0.20     # 20% on services without exemption cert
```

Computation on each line item (per VAT Act 870, as amended):
```
NHIL_COVID_GETFUND_base = gross
NCG = gross * (NHIL + GETFUND + COVID)       # levies compound on gross
VAT_base = gross + NCG
VAT     = VAT_base * VAT
total_taxes = NCG + VAT
net_of_taxes = gross + total_taxes
WHT = applicable_base * WHT_rate   (if vendor not exempt)
payable_to_vendor = total - WHT
```

Stored per line item; aggregated per voucher for reporting.

### 9.3 Mobile money disbursement (via NickComms)

New NickComms endpoint:
```
POST /api/disburse/momo
Headers: X-Api-Key: <nickfinance service token>
Body: { voucherId, momoNumber, network, amountPesewa, reference, clientReference }
```

NickComms proxies to Hubtel Merchant API, persists transaction state
(`momo_disbursements` table in `nick_comms`), emits webhook on
settlement. NickFinance listens on webhook to flip voucher from
`Approved` → `Disbursed`.

Rotation: Hubtel merchant credentials in env vars, placeholder
pattern — same approach as ROADMAP 2.11.

### 9.4 Ghana Card (NIA) identity

- Vendor registry stores optional Ghana Card PIN (replaces TIN per
  NIA Act 750 / GRA guidance).
- Employee requester verification: pulled from NickHR record if
  present; nudge to capture at onboarding.
- WHT threshold decisions consult `vendor.tin_ghana_card` presence +
  `vendor.is_vat_registered` flag.

### 9.5 GRA e-VAT

For voucher line items above the e-VAT threshold (currently ~GHS 200
per invoice per GRA guidance), Phase 1.3 adds an optional
"Register with GRA e-VAT portal" action — calls GRA's Electronic VAT
Invoicing API, stores the returned CTC (Compliance Transaction Code)
on the line item. v1 skips this; flagged in policy as
`gra_evat_register: false`.

---

## 10. Fraud controls

Detection pipeline runs nightly (and on every voucher submit for the
first four signals):

| # | Signal | Detection | Action |
|---|---|---|---|
| F1 | Salami-slicing | Same requester, same payee, sum of approved ≥ threshold in rolling 24h | Flag + require Finance review; auto-reject if sum >2× threshold |
| F2 | Ghost payee | Payee MoMo number never seen before AND amount > GHS 1,000 | Flag only; don't block |
| F3 | Duplicate receipt | pHash similarity >0.95 vs any prior receipt | Block disbursement until reviewed |
| F4 | GPS mismatch | Submitted GPS >20 km from requester's assigned site + not on travel | Flag for supervisor |
| F5 | Benford deviation | Weekly voucher amounts deviate from Benford's leading-digit law (χ² >25) | Weekly report to auditor |
| F6 | Weekend/after-hours | Voucher created outside 07:00–19:00 local | Flag; allowed if requester on roster that shift |
| F7 | Approver-requester collusion | Same pair appearing in >20 vouchers/month | Monthly report; auditor-only |
| F8 | Round-number cluster | >5 vouchers at exact threshold boundary in month | Monthly report |

Signals land in `petty_cash.fraud_flags` table with severity, auto-action,
and human-review status. Auditor dashboard shows unreviewed flags.

---

## 11. Security & compliance

### 11.1 Auth / Z

- Edge: Cloudflare Access — `finance.nickscan.net` already has its own
  Access app (`NickFinance`, AUD `4fb33ade…`); reuse it. (As of 2026-04-29
  each hostname has its own dedicated Access app — do **not** add
  finance to `NickScan Services`.)
- App: trusts `CF-Access-Jwt-Assertion` header (Option D from `SSO.md`)
- Authorization scopes (stored in Identity service, ROADMAP P1):
  - `PettyCash.Requester` — default for all employees
  - `PettyCash.Approver.Line` — line managers (derived from HR)
  - `PettyCash.Approver.Site` — site supervisors
  - `PettyCash.Approver.Finance`
  - `PettyCash.Custodian`
  - `PettyCash.Auditor`
  - `PettyCash.Admin`

### 11.2 Separation of duties

Enforced in three layers:
1. **Policy engine**: skip self in approval chain
2. **API**: reject request if acting user == requester for disburse/reconcile
3. **DB**: CHECK constraints on `voucher_approvals` and `cash_counts`

### 11.3 Audit trail

Every state transition + every config change writes an immutable row to
`petty_cash.audit_events`:
```
id, ts, actor_user_id, actor_ip, actor_session_id, action,
entity_type, entity_id, before jsonb, after jsonb, policy_version
```
Table has `GRANT SELECT, INSERT` to `nscim_app`; explicit `REVOKE
UPDATE, DELETE` to enforce append-only.

When ROADMAP Phase 4 ships unified audit, mirror rows to
`identity.audit_events`. Dual-write for one release, then cut over.

### 11.4 Data retention

- Voucher + line items: 7 years (tax + company law; Ghana Companies
  Act 992 requires 6, add safety margin)
- Receipt files: 7 years on disk (WORM-ish via `attrib +r`
  and ACLs), then archived to cold storage
- Audit events: 10 years (Companies Act 992 §125)
- Soft-delete only; hard-delete forbidden except via DBA ticket with
  approval from CFO

### 11.5 PII

- Ghana Card PIN and employee National IDs are PII — stored plain (no
  hash), but access gated behind `PettyCash.Admin` + `PettyCash.Finance`
  scopes. All reads logged.
- Personal MoMo numbers: same treatment.

---

## 12. Phased implementation plan

Time estimates assume one developer + part-time finance reviewer.
Calendar weeks, not working days.

### Phase A — Foundations (weeks 1–2)

| # | Task | File / Area |
|---|---|---|
| A.1 | Scaffold `platform/NickFinance.PettyCash/` project (.NET 10, MudBlazor 8.13.0, Blazor Server) | new project |
| A.2 | Add to `NickscanERP.sln` under new solution folder `finance` | solution |
| A.3 | Add service: register Windows service `NSCIM_Finance` (port 5500) | deployment |
| A.4 | Create Postgres DB `nickscan_finance`, schema `petty_cash`, grant to `nscim_app` | DBA |
| A.5 | EF Core migrations for base entities (floats, vouchers, line items, approvals, categories, receipts) | code |
| A.6 | Seed: 6 floats (one per site), 5 categories, Nick Ghana tax rates | migration |
| A.7 | CF Access: add `finance.nickscan.net` to existing app; DNS CNAME via CF API | CF |
| A.8 | Cloudflared ingress rule for `finance.nickscan.net` → `http://localhost:5500` | config |
| A.9 | Shared `TopNav` integration (even as stub until ROADMAP Phase 3 lands) | code |
| A.10 | Portal card: add "Petty Cash" to `Apps.Cards` in portal appsettings | config |

**Acceptance**: `https://finance.nickscan.net` serves a blank module
shell behind Access; users with `PettyCash.Requester` scope see empty
dashboard.

### Phase B — MVP core (weeks 3–5)

| # | Task |
|---|---|
| B.1 | Voucher submit wizard (category → details → line items → submit) |
| B.2 | Receipt upload (image + PDF), stored to disk path pattern; SHA-256 hash |
| B.3 | Approver queue page with approve/reject + comment |
| B.4 | Fixed 2-step approval (line manager + finance above threshold) in code |
| B.5 | Disburse action (custodian only; cash-only; marks voucher Disbursed) |
| B.6 | Reconcile action (finance only; moves to Reconciled) |
| B.7 | Email notifications via NickComms on each state transition |
| B.8 | Spend report (CSV export + on-screen table) |
| B.9 | Float balance calculation + replenishment request |
| B.10 | Basic admin: list/add/edit categories, floats, custodians |

**Acceptance**: end-to-end voucher can be submitted, approved twice,
disbursed in cash, and reconciled. Audit trail is complete.

### Phase C — Policy engine + MoMo (weeks 6–8)

| # | Task |
|---|---|
| C.1 | YAML policy loader + hot reload |
| C.2 | Resolver: role names → user IDs via NickHR query |
| C.3 | Multi-step chain execution (sequential + parallel) |
| C.4 | Delegation UI + runtime check |
| C.5 | Escalation timer (background service, `IHostedService`) |
| C.6 | NickComms extension: `POST /api/disburse/momo` → Hubtel Merchant API |
| C.7 | MoMo webhook listener in NickFinance; flip voucher state on settlement |
| C.8 | Tax engine (line-item level); Ghana rates from config |
| C.9 | WHT handling for vendor payments (registry flag) |
| C.10 | Admin: policy YAML editor with live validation |

**Acceptance**: a GHS 800 transport voucher auto-routes to line
manager → site supervisor → finance; when approved, NickComms
disburses to requester's MoMo; settlement webhook flips state.

### Phase D — OCR, fraud, budgets (weeks 9–11)

| # | Task |
|---|---|
| D.1 | Azure Form Recognizer integration (receipt model) |
| D.2 | OCR result persistence + amount-mismatch warning in UI |
| D.3 | Perceptual hash on receipt images; duplicate detection across vouchers |
| D.4 | Fraud-signal pipeline (F1 through F8 from §10) |
| D.5 | Fraud-flag dashboard for auditors |
| D.6 | Budget CRUD + consumption counter + 80% email alert |
| D.7 | Recurring voucher templates |
| D.8 | Bulk payout CSV import |
| D.9 | Advance voucher workflow (cash first, receipts later, reminder job) |

**Acceptance**: uploading the same receipt twice blocks the second
disbursement; a salami-slicing attempt (5× GHS 450 to same payee
same day) auto-rejects; Finance gets a weekly Benford report.

### Phase E — Polish & compliance (weeks 12–14)

| # | Task |
|---|---|
| E.1 | Offline-mode PWA + service worker queue |
| E.2 | USD float (multi-currency plumbing active) |
| E.3 | GRA e-VAT registration API integration |
| E.4 | Journal export to Tally / QuickBooks format |
| E.5 | Dashboards (spend heatmap, float runway) |
| E.6 | User training docs + cheat sheets per role |
| E.7 | UAT with Finance + 2 pilot sites (Tema + Paga) |
| E.8 | Production rollout all 6 sites |

**Acceptance**: UAT sign-off from CFO + 2 site custodians. 30-day
production soak. Zero Sev-1 incidents. Period close tested twice.

---

## 13. Open questions / decisions needed

Answer these before starting Phase A:

1. **Finance approval threshold** — above what amount does Finance
   have to approve? Suggest GHS 500 (5,000 pesewa) for v1.
2. **Site float sizes** — 6 sites, 6 floats. What initial amount each?
   Tema is HQ so probably highest (GHS 10k?); border sites (GHS 3–5k)?
3. **MoMo vs cash split** — policy preference? Most progressive firms
   in Ghana default MoMo, cash on exception. Agree?
4. **Advance recovery** — if an employee takes a GHS 1,000 advance and
   only submits GHS 800 of receipts, how is the GHS 200 recovered?
   Payroll deduction? Written off? Ask CFO.
5. **Hubtel merchant account** — does Nick already have one set up
   (for MoMo collections in NSCIM? I don't think so)? If not, setup
   takes 1–2 weeks with KYC docs.
6. **Accounting system destination** — Tally? QuickBooks? Sage? In-house?
   Determines Phase E.4 effort.
7. **Chart of accounts** — do we reuse Nick's existing COA codes?
   Need the list.
8. **Category seed list** — confirm the 5 MVP categories (Transport,
   Fuel, Office Supplies, Staff Welfare, Emergency). Add any?
9. **Custodian per site** — confirm who currently holds petty cash at
   each site; need those 6 names to seed.
10. **Data migration from legacy** — is there an existing Excel tracker
    or paper log to import? If yes, scope & fidelity.

---

## 14. Risk register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Identity service (ROADMAP P1) slips, blocks Phase A.9 | Medium | Medium | Fallback: read `nickhr.Employees` directly by email for v1; swap to Identity service when it lands (1-day change) |
| Hubtel merchant setup delays MoMo | Medium | Low | Ship v1 cash-only; MoMo is Phase C |
| Policy YAML gets too hairy (users want a GUI only) | Low | Medium | Offer both: YAML is source of truth, UI editor writes YAML |
| GRA e-VAT API changes | Low | Medium | Gate behind feature flag; update independent of voucher core |
| Border site offline mode tricky (IndexedDB + sync) | High | Low | Ship online-only for v1; PWA offline is Phase E |
| Custodian resists digital tooling (paper habit) | Medium | High | Pilot at Tema + one border site with the most digital-comfortable custodians; iterate UX; don't force-migrate the others for 2 months |
| Ghana tax legislation changes mid-build | Low | Low | Rates in config, not code |
| Advance vouchers go unreconciled | High | Medium | 14-day auto-reminder; at 30 days auto-flag CFO; at 60 days move to payroll recovery queue |

---

## 15. Quick-win prototypes (optional, 1–2 days each)

Before committing to the 14-week plan, de-risk with tiny spikes:

- **Spike 1**: Hit Hubtel sandbox from a console app, disburse a test
  GHS 1 to a test MTN MoMo number. Confirms API works. (4 hrs)
- **Spike 2**: Upload 10 typical Ghanaian thermal receipts to Azure
  Form Recognizer, measure accuracy. If <80%, plan Tesseract fallback
  seriously. (2 hrs)
- **Spike 3**: Mock out the YAML policy + resolve one chain against
  actual NickHR data. Confirm the manager graph is clean. (1 day)

---

## 16. Sources consulted

- ACCA F8 (Audit & Assurance) on imprest controls
- COSO 2013 internal-control framework
- Ghana VAT Act 870 (as amended 2023); Income Tax Act 896
- ICAG (Institute of Chartered Accountants Ghana) SMP practice notes
- Bank of Ghana Payment Systems Act 987 (mobile money)
- NIA Act 750 (Ghana Card)
- Hubtel Merchant API docs (`developers.hubtel.com`)
- Azure Form Recognizer — prebuilt-receipt model
- Comparative review: SAP Concur, Oracle Fusion Expenses, Zoho Expense,
  Ramp, Pleo, Coupa — approval-chain patterns
