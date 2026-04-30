# NickFinance WebApp — UX Punch List

> **Audit date:** 2026-04-28
> **Scope:** every Razor page under `finance/NickFinance.WebApp/Components/Pages/` plus `MainLayout.razor`, `App.razor`, `Routes.razor`, and `wwwroot/app.css`.
> **Reviewer mode:** read-only walk-through, no code touched.
> **Audience that will hit this UI in the next few weeks:** custodians at Tema/Kotoka/Takoradi/Aflao/Paga/Elubo, finance leads at HQ, CFO reviewing journals.

This is a UX punch list, not a security or correctness audit. Bugs that would corrupt data are out of scope; what's in scope is "does it feel like a real product or a v0 admin form."

---

## 1. Top 10 punches (ranked by user-pain × frequency)

| # | Punch | File | Why it stings |
|---|-------|------|---------------|
| **1** | Float creation form takes raw GUID strings for **Site** and **Custodian**. The "Quick fill" buttons are hash-derived synthetic GUIDs explicitly described as a stopgap. | `FloatNew.razor` lines 14–24, 80–93 | A custodian or finance lead must paste two GUIDs to onboard a single site. There is no real picker. The "Tema HQ / Kotoka / …" buttons emit fabricated GUIDs that look real but are *not* the canonical site IDs anyone else will see. This is the first step in the entire petty-cash flow and it is broken-by-design until a real registry lands. |
| **2** | Every list page is a hard `Take(100)` with no pagination, filter, search, or sort. | `PettyCashList.razor:51`, `ArList.razor:63`, `ApBills.razor:45`, `Banking.razor:60` (`Take(50)`), `Floats.razor:50` (no take), `FixedAssets.razor:44` (no take) | Once vouchers cross 100, the 101st is invisible. There is no "older" link. Worst case the user opens `/petty-cash`, sees a familiar list, and silently misses the disbursement they're hunting for. Same for AR/AP. |
| **3** | All form failures surface raw `ex.Message` to the user. | `PettyCashNew.razor:109`, `PettyCashDetail.razor:124,135,150`, `FloatNew.razor:112`, `ArNew.razor:98`, `ArDetail.razor:100`, `ArReceiptPage.razor:89`, `Customers.razor:69` (×9 forms total) | A validation collision becomes "Voucher ledger event posting failed because the deferred balance trigger fired" or worse, a SQL constraint name. Custodians at the port will not know what that means and will keep clicking until something happens. |
| **4** | The Petty-Cash list shows only the first 8 hex chars of the requester's GUID, not their name. Same on Approvals queue, Floats list, Voucher detail header. | `PettyCashList.razor:26`, `Floats.razor:25-26`, `Approvals.razor:11,23`, `PettyCashDetail.razor:23` | "Voucher PV-000123 from `c4e7af8a` for GHS 800" tells a CFO nothing. They cannot tell who submitted it without opening the user table directly. This is the single biggest "this app feels like a dev tool" signal. |
| **5** | No pagination, filter, OR search on the AR list — and no per-row click-through to the invoice detail page. | `ArList.razor:14-50` | The header column lists `<th>No.</th>` but the cell renders `<code>...</code>` with no link. The detail route `/ar/{id}` exists (`ArDetail.razor`) but you can only reach it by typing the URL or via the `/ar/new` redirect. Custodians literally cannot drill into a historical invoice from the list. |
| **6** | Money is rendered with the pattern `((minor / 100m).ToString("N2"))` ad-hoc in **31 different lines across 12 files**, never with the currency code attached. | every list/detail/report page; e.g. `BalanceSheet.razor:27`, `CashFlow.razor:25`, `Reports.razor:36-38`, `PettyCashList.razor:29` | The numbers say `1,234.56` with no `GHS` prefix. The `Banking.razor` accounts table has a `Currency` column but the transactions table beside it omits the currency on each amount. A finance lead toggling between USD-denominated bills and GHS bills sees identical-looking numbers. The Cash Flow report is hard-coded `"GHS"` (`CashFlow.razor:64`) even though the company has a USD float option per `FloatNew.razor:34`. |
| **7** | No detail page exists for AP bills, Fixed Assets, Bank transactions, Customers, or Budgets. The list rows are dead-ends. | `ApBills.razor`, `FixedAssets.razor`, `Banking.razor`, `Customers.razor`, `BudgetsPage.razor` (and `ApBills.razor:10` literally admits it: "UI for create-and-pay is on the v1.3 roadmap") | A finance lead wanting to see line items on a vendor bill, depreciation schedule on a vehicle, or budget vs actual on an expense category cannot. They have to go to the API or the database. Half the navigation header is a Potemkin village. |
| **8** | Date formatting is `yyyy-MM-dd` everywhere — never localized, never relative, and timestamps show as `ToString("u")` — `2026-04-28 09:14:32Z` — with no Africa/Accra timezone awareness. | `PettyCashDetail.razor:29-32`, `Home.razor:20`, list pages passim | A custodian in Tema sees voucher created `2026-04-28 21:14:32Z` and the user thinks "it's 9pm? I submitted that this morning" — the UI never adjusts to UTC+0 (Ghana). The lack of relative dates ("3 minutes ago", "yesterday at 14:00") is a constant low-grade friction. |
| **9** | The receipt-recording form `/ar/{id}/receipt` hard-codes the cash account dropdown with five GL codes (`1010`, `1020`, `1030`, `1031`, `1032`). | `ArReceiptPage.razor:36-43` | If an HQ accountant adds a sixth bank, the dropdown does not update — there is no DB query. Worse, the GL codes are bare-coded and may not match a tenant's customized chart of accounts. The same dropdown does not exist on the petty-cash disburse path (`PettyCashDetail.razor:93` just says "Disburse (cash)" — no choice). |
| **10** | No confirmation dialogs anywhere. Approve, Reject, Issue, Disburse, Record receipt all fire on first click. | `PettyCashDetail.razor:70,76,93`, `ArDetail.razor:56` | Approving a GHS 50,000 voucher is a single click with no "are you sure?" — and once approved you cannot un-approve (the service rejects it). The Issue button posts a journal entry and submits an IRN to GRA in one click. There is no "preview the journal first" affordance. A muscle-memory misclick is now a real-money problem. |

---

## 2. Page-by-page table

| Page | Route | # of issues | Worst issue |
|------|-------|------|-------------|
| Home.razor | `/` | 5 | "What works today" feature-list reads like a README — wrong audience for a daily dashboard. No links to Approvals, no recent-activity feed, no "things needing my attention" section. |
| PettyCashList.razor | `/petty-cash` | 9 | `Take(100)` with no pagination/search/filter; requester shown as 8-char GUID. |
| PettyCashNew.razor | `/petty-cash/new` | 7 | Float dropdown shows `<8-char-site-guid> · custodian <8-char-user-guid> · GHS` — even *picking the float* requires recognizing GUID prefixes. Single line item only (the model takes `IEnumerable<VoucherLineInput>` but the form forces 1). Amount and Description are duplicated — the same number flows into two fields. |
| PettyCashDetail.razor | `/petty-cash/{id}` | 8 | Approve / Reject / Disburse buttons fire instantly; no preview of journal; reject input is inline (no modal); requester GUID-truncated; no audit-trail panel (who approved what when, with comments). |
| Floats.razor | `/petty-cash/floats` | 6 | Site & Custodian columns are 8-char GUIDs; no per-row click-through; no "close this float" action; no balance-remaining column. |
| FloatNew.razor | `/petty-cash/floats/new` | 7 | Raw GUID inputs (#1 above); the "Quick fill" stamp pattern silently overwrites whatever's in the field; Custodian defaults to *me* (likely never the right answer); no validation that an active float for that (site, currency) doesn't already exist. |
| Approvals.razor | `/approvals` | 4 | Requester is GUID-truncated; no bulk "approve all under GHS 200" action; no in-line approve (must navigate to detail); no count badge in nav. |
| ArList.razor | `/ar` | 8 | No row→detail link; no filter (overdue / draft / paid); no search by invoice no or customer; the "New invoice" CTA is missing entirely from this page (you have to know the URL `/ar/new`). |
| ArNew.razor | `/ar/new` | 7 | Revenue account is a free-text 32-char input (`<input type="text">` line 53), should be a CoA picker; only 1 line item supported; net amount has no live tax preview ("you'll be charged GHS X NHIL + Y GETFund + Z VAT = total Q"); no invoice-number preview. |
| ArDetail.razor | `/ar/{id}` | 7 | "Issue (compute taxes + IRN + post journal)" button does three irreversible things in one click; no Void / cancel button; no email / send / print / PDF; no line-item table even though `Include(i => i.Lines)` loads them. |
| ArReceiptPage.razor | `/ar/{id}/receipt` | 6 | Cash account dropdown hard-codes 5 GL codes; no payment-method picker (cheque vs MoMo vs cash); no MoMo reference number field; no over-payment guard; no receipt PDF output. |
| Customers.razor | `/ar/customers` | 7 | Whole-page form + table on one screen — no edit, no deactivate, no merge-duplicates; `Email` is `<input type="text">` not `type="email"`; phone number is missing entirely; no address; no pagination. |
| ApBills.razor | `/ap` | 7 | Read-only — `<p class="lede">` literally says "create-and-pay flows … on the v1.3 roadmap"; no row→detail link; no vendor master page in nav; no "approve / pay" actions. |
| Banking.razor | `/banking` | 9 | Read-only; transactions table has no filter / date range / account filter; no "import statement" button (statement import described in the lede); the amount column shows `+/-` prefix but no thousands separator on the whole concatenation; **no currency on transactions table even though accounts can be multi-currency**; no reconciliation page. |
| FixedAssets.razor | `/fixed-assets` | 7 | Read-only — no "add asset", no depreciation schedule, no disposal action, no category filter; tag column is GUID-like but unclear; no audit trail. |
| BudgetsPage.razor | `/budgets` | 7 | Read-only — no "create budget" button; no budget-vs-actual variance view (the headline use case); no drill-down from a row; no fiscal-year filter; lines count is shown but the line items themselves are unviewable. |
| Reports.razor | `/reports` | 5 | Two reports stacked on one page (TB + P&L) sharing a single "as-of" filter — the P&L is YTD, so the filter actually means "year-end" for that section but "snapshot" for TB; no export to CSV/Excel/PDF; no GL Detail report despite the home page mentioning it; no comparative period column. |
| Aging.razor | `/reports/aging` | 5 | No drill-down from a bucket to the constituent invoices; no aging-by-customer breakdown; the "as-of" date is implicit (today, UTC) with no override; no print. |
| BalanceSheet.razor | `/reports/bs` | 4 | No comparative period; no export; the "✓ Assets = Liabilities + Equity" green-tick is a single character so easy to miss; no expand/collapse on sections. |
| CashFlow.razor | `/reports/cash-flow` | 5 | Hard-coded `"GHS"` (line 64); no comparative period; no method label (direct vs indirect); no per-category drill-down; no export. |
| StatusPill.razor | (component) | 2 | The mapping omits `Draft`, `Pending`, and any AP-specific statuses → those render as default grey pill, indistinguishable from "unknown". `Voided` is mapped but `Void` is a separate enum value in some modules — case mismatch risk. |
| MainLayout.razor | (layout) | 6 | The nav has 9 top-level items and no grouping — Petty cash and Floats are siblings of "Approvals" but they're conceptually a single section; `/ar/customers`, `/ar/new`, `/reports/aging`, `/reports/bs`, `/reports/cash-flow`, `/reports/pnl` are not in the nav at all; no breadcrumbs anywhere; no notification badge on Approvals; the user widget shows email but no logout / impersonation / tenant-switcher. |
| App.razor | (host) | 2 | `<title>NickFinance</title>` is the static title in the head — every page that doesn't set `<PageTitle>` falls back to this; no favicon link declared; no `lang="en-GH"`. |
| Routes.razor | (router) | 2 | No `<NotFound>` template — a typo URL renders Blazor's default raw HTML page, not a friendly 404. No layout wrap on NotFound either. |

**Total Razor pages reviewed:** 19 page files + 4 layout/component files = **23**.

---

## 3. Cross-cutting issues (patterns that recur)

These are the ones to hit centrally — fix once, every page benefits.

1. **GUIDs as primary identifier in the UI.** Every entity reference (site, custodian, requester, approver, vendor, customer when shown by ID) is rendered as `Guid.ToString("N")[..8]` or as a raw GUID string in inputs. There is no `<EntityPicker>` component. This is the single most "feels rough" pattern in the codebase, and it touches at least 9 pages.

2. **No pagination, filtering, sorting, or searching on any list page.** Every list does `OrderBy*().Take(50|100).ToListAsync()`. There is no shared `PaginatedList<T>` component, no shared filter chip-row, no column-header sort. With production data the experience degrades silently.

3. **Money is not formatted as money.** `((x / 100m).ToString("N2"))` literally appears in 31 lines across the codebase. There is no `Money.Format()` extension, no `<Money>` component, no currency code in the rendered output. The Trial Balance currency comes from a hard-coded `"GHS"` argument in `Reports.razor:85-86`. Multi-currency tenants will see ambiguous numbers.

4. **Validation is server-only with `try/catch (Exception ex) { _error = ex.Message; }`.** No `<DataAnnotationsValidator>`, no `<ValidationSummary>`, no `<ValidationMessage>`, no client-side check until POST. Users wait for the round-trip and then read whatever the inner exception hands back. Found in 9 of 9 forms.

5. **No confirmation dialogs on irreversible actions.** Approve, Reject, Issue, Disburse, Record receipt all fire on first click. No `js confirm()`, no in-Blazor modal, no two-step "type APPROVE to confirm" pattern. Dangerous given that Issue posts a journal *and* mints an IRN at GRA in one shot.

6. **Detail pages are missing for half the modules.** AP bills, Bank transactions, Fixed Assets, Customers, Budgets — all list-only. The navigation header advertises 9 modules; only Petty Cash and AR have round-trip flows.

7. **No audit trail panel anywhere.** Voucher detail shows `CreatedAt`, `SubmittedAt`, `DecidedAt`, `DisbursedAt` as a flat dl-list with no actor names. There's no "Approved by Ama at 14:32 with comment 'OK for Tema run'" log even though that data exists in the `VoucherApproval` table the Approvals page reads from.

8. **Date / time presentation is raw UTC.** `ToString("yyyy-MM-dd")`, `ToString("u")`, and `DateTimeOffset.UtcNow.ToString("u")` everywhere. No timezone conversion to Africa/Accra (UTC+0 — close enough today, but the moment a user lands in another tz on travel they'll be confused). No relative dates. No friendly format.

9. **No primary/secondary action discipline on detail pages.** `PettyCashDetail.razor` has Approve, Reject, Disburse all rendered as primary/secondary `<button>`s with no visual hierarchy distinguishing the destructive (Reject) action. `ArDetail.razor` has just one giant blue button labelled "Issue (compute taxes + IRN + post journal)" — labels-as-prose-paragraph.

10. **Mobile is an afterthought.** The CSS uses `max-width: 1180px` and `grid-template-columns: 200px 1fr` on `dl`, plain `<table>` on every list with no responsive collapse, and `max-width: 480px` on form inputs. A custodian with a phone (likely the Tema use case) will horizontally scroll every list page.

---

## 4. Quick wins (each <1h)

Ordered by leverage — do them in this order.

1. **Add `<NotFound>` to `Routes.razor`** with a friendly "page not found, here's a link home" panel inside `MainLayout`. ~10 min.
2. **Move the AR "New invoice" CTA into `ArList.razor`** as a `card-actions` block (mirror `PettyCashList.razor:13-15`). ~10 min.
3. **Make every row in `ArList`, `ApBills`, `Floats`, `FixedAssets`, `Banking` clickable** — wrap the first cell in `<a href="/ar/{id}">` (works for AR today; for AP/Banking/Assets, link to a stub page that says "detail page is under construction" so the dead-end is at least informed). ~30 min.
4. **Wire a `MoneyExtensions.FormatGhs(this long minor)`** that returns `"GHS 1,234.56"` and replace the 31 inline `(x/100m).ToString("N2")` sites. Or better, ship a `<Money Minor="..." Currency="GHS" />` component. ~45 min.
5. **Add `<DataAnnotationsValidator />` and `<ValidationSummary />`** to every `<EditForm>` and put `[Required]`/`[Range]`/`[StringLength]` on the form models. Stops half the round-trips. ~45 min.
6. **Wrap `try/catch ex.Message`** in a small `MapToFriendly(Exception)` helper that recognizes the known `DomainException` / `ValidationException` types and falls back to "Something went wrong, try again or contact finance" for everything else. Log the original. ~30 min.
7. **Replace `<input type="text">` for the customer Email field** with `type="email"`; add `placeholder` attributes throughout (none present today); add `autocomplete` hints. ~15 min.
8. **Add a `js confirm()` (or simple two-stage state)** on the four destructive buttons: Reject, Issue, Disburse, Record receipt. ~30 min (per button × 4).
9. **Add an `Approvals` count badge** to the navigation by injecting a small "queue length" service. ~30 min.
10. **Display the requester / approver display name** on Petty Cash list, Approvals, Floats — even if it's just `User.Email.Split('@')[0]` until a proper user lookup lands. ~45 min.
11. **Set `<PageTitle>`** on every page (Floats list and Floats new are already done, others not). Confirm none fall through to the static `<title>NickFinance</title>` in `App.razor`. ~10 min.
12. **Hide `app.css` mobile breakpoints under `@media (max-width: 768px)`** for `.tile-row` (already responsive via `auto-fit`), `dl` (collapse to single-column), and tables (set `display: block; overflow-x: auto;`). ~30 min.
13. **Add a `<small class="muted">` line under each form button group** with what will happen — e.g. on the Issue button, "This will lock the invoice, post a journal entry, and request an IRN from GRA. You cannot undo this." ~30 min.
14. **Centralize the "no rows yet" empty state** as a tiny `<EmptyState Icon="..." Title="..." Action="..." />` component used on every list page. ~45 min.
15. **Add a "view all" link below each `Take(N)` list** that linkifies to a future `/petty-cash/all` page — even if that page is not built yet, the affordance signals "yes, there's more, we just hid it." ~10 min.

---

## 5. Bigger lifts (0.5–3 days each)

1. **`<EntityPicker>` shared component.** Typeahead, paged, filtered. Backed by a simple `IEntityLookup<T>` service registered per entity (Site, User, Customer, Vendor, GL account). Replaces every `<input type="text">` that takes a GUID and every `<select>` that lists 100s of items. **This is the single highest-leverage change in the entire UI.** ~2 days. Files: `FloatNew.razor` (Site, Custodian), `PettyCashNew.razor` (Float), `ArNew.razor` (Customer, Revenue account), `ArReceiptPage.razor` (Cash account), and *every future detail page*.
2. **`<DataTable>` shared component** with paging, sort, filter, search, optional bulk-select. Replaces the 12 ad-hoc `<table>` blocks. ~2 days.
3. **A real `MoneyFormatter` + `<Money>` component** that knows the active currency, applies `CultureInfo.GetCultureInfo("en-GH")` (or fallback `en-US`) for thousands, and shows the `GHS` / `USD` prefix consistently. ~0.5 day for the component, 0.5 day to migrate the 31 call sites.
4. **Build the AP create/approve/pay UI**. The lede on `ApBills.razor` admits this is missing. Mirror the AR pattern: list → new → detail with actions. ~2 days.
5. **Build the AR invoice line-item editor** (multi-line invoices). The service interface already supports `IEnumerable<DraftInvoiceLine>` but the form hard-codes one row. Add an "Add line" button and a per-line tax-preview. ~1.5 days.
6. **Audit trail panel as a shared component** (`<AuditTrail EntityType="Voucher" EntityId="@VoucherId" />`) backed by `LedgerEvent`/`VoucherApproval`. Drop it into every detail page. ~1 day.
7. **PDF / print-friendly invoice and voucher views.** A `/ar/{id}/print` route with a stripped-down `@layout` and a `window.print()` button. ~1 day per entity (do AR first, voucher second).
8. **Reports — export to CSV / Excel.** Each of TB, P&L, BS, Cash Flow, Aging needs a download button. Use `ClosedXML` or just CSV; the back-end already returns structured `Report` records. ~1.5 days.
9. **Mobile-first redesign of the list pages.** Convert tables to card-collapse on `<768px`, push action buttons to the bottom-right floating, simplify the top nav into a hamburger. ~2 days.
10. **Friendly error envelope.** Map domain exceptions to user-language strings; map period-locked / separation-of-duties / fraud-rule violations to dedicated banners with the right call-to-action ("ask Ama to approve", "open period 2026-04 first"). ~1.5 days.

---

## 6. Open questions

These are points where the right UX choice depends on info I do not have.

1. **Is multi-currency a real day-one concern?** The `FloatNew.razor` form supports `GHS` and `USD`, but `Reports.razor` and `CashFlow.razor` hard-code `"GHS"`. If the company books in GHS and only holds USD floats for cash counts, the current behaviour is actually correct (just confusing). If it's a true multi-currency shop, the Reports surface needs a currency switcher and FX-rate display.
2. **Who is the real persona at `/petty-cash/floats/new`?** A custodian onboarding themselves, or a finance admin onboarding all six border posts? The "Quick fill" buttons suggest the latter, but the auto-default of `_form.CustodianUserId = User.UserId` (line 69) suggests the former. Knowing this changes whether to keep the user-picker as a typeahead or to hide it under a "I'm onboarding myself" toggle.
3. **Is the Home page meant to be a dashboard or a status board?** The current "What works today" feature-list reads like onboarding copy. If the audience is daily operators (likely), the feature-list should be replaced with "Things needing your attention" and "Recent activity by your team."
4. **Should rejection of a voucher require a category as well as a free-text reason?** The current free-text input is fine for a manager who wants to write a paragraph, but bad for analytics ("how many rejections were due to missing receipts vs duplicate vs over-budget").
5. **What's the right "edit" story for issued AR invoices?** Today there's no edit and no void button on `ArDetail.razor`. Once a voucher / invoice is committed to a journal, the proper accounting move is a credit note, not an edit — but the UI does not surface that path.
6. **Should the Trial Balance be on the same page as the P&L?** They share one "as-of" date filter today which is misleading (TB is a snapshot, P&L is a range). Splitting is cleaner; keeping them together with two filters is also fine — depends on how often the CFO wants both side-by-side.
7. **Is the "SANDBOX" pill on AR rows appropriate for a live system?** It's correct today (the e-VAT provider is a stub) but the moment GRA goes live, that yellow pill needs to disappear cleanly. Worth checking that `StubEvatProvider.IsSandbox` handles the cutover.
8. **Tenant switching.** The `<MainLayout>` shows the user's email but no way to switch tenant. If the same user accesses multiple legal entities, they need a tenant picker. Today, tenant is determined entirely by `CurrentUser.TenantId` injection.
9. **What does the navigation look like at scale?** With 9 top-level modules already and AP / Vendors / Items / GL detail / Period close / Approvals-history / Recurring vouchers / Bank reconciliation still to come, the flat top-nav is going to become unusable. Decision: groups + dropdowns, sidebar, or breadcrumbs + module selector?
10. **Accessibility floor.** No `aria-label`s on icon-free buttons (none present yet, but coming), no `<label for=...>` pairs (all labels wrap inputs which is OK), color contrast on `.muted` (#64748b on #f8fafc) is borderline WCAG-AA. Worth a single accessibility pass.

---

## Files referenced

Absolute paths for all 23 files reviewed:

- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\App.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Routes.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\_Imports.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Layout\MainLayout.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\Home.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\PettyCashList.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\PettyCashNew.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\PettyCashDetail.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\Floats.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\FloatNew.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\Approvals.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\StatusPill.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\ArList.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\ArNew.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\ArDetail.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\ArReceiptPage.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\Customers.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\ApBills.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\Banking.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\FixedAssets.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\BudgetsPage.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\Reports.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\Aging.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\BalanceSheet.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\CashFlow.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\wwwroot\app.css`
