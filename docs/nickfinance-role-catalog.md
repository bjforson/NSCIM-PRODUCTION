# NickFinance Role Catalog (Recommended)

**Status:** Research draft, 2026-04-29. Pending sensei + CFO sign-off before any code or seed change.
**Supersedes:** the Wave 1A 6-role placeholder (`Custodian / Approver / SiteManager / FinanceLead / Auditor / Admin`) defined in `platform/NickERP.Platform.Identity/Entities.cs` (`RoleNames`).
**Scope:** NickFinance only. NickHR, NickComms and the legacy NSCIM family are out of scope; their identity flows are unaffected (NickHR is the identity provisioner — see `IIdentityProvisioningService` — and continues to grant whatever role names appear in `identity.roles`).

---

## 1. Executive summary

- **The 6-role placeholder is too coarse for the surfaces NickFinance now exposes.** It pre-dates Banking, Budgeting, FixedAssets, FX revaluation, the WHT certificate book, the iTaPS exporter, and the Hubtel e-VAT path. Twelve of the thirty-eight razor pages in `NickFinance.WebApp/Components/Pages` carry **no** `[Authorize(Policy=...)]` attribute today (e.g. `Banking.razor`, `BudgetsPage.razor`, `FixedAssets.razor`, `Customers.razor`, `ApBills.razor`, `WhtCertificateBook.razor`, `Aging.razor`, `BalanceSheet.razor`, `CashFlow.razor`); they fall back to "any authenticated user." That is wider than the SoD posture intends.
- **The placeholder violates two industry-standard SoD pairs.** `FinanceLead` today holds *both* `RecordReceipt` (cash side) and `PostJournal` (ledger side) and *both* `IssueInvoice` (revenue creation) and `RecordReceipt` (cash collection). NetSuite, Sage Intacct, D365 F&O and the AICPA SoD matrix all split these.
- **The new catalog has 12 roles**, all multi-grantable and most site-scopable. Replaces 6.
- **Recommended grant model:** **multi-role per user**, matching NetSuite / Sage Intacct / Odoo and what the `identity.user_roles` table already supports (the row-per-grant shape with nullable `site_id` already lets one user hold N roles, optionally site-scoped). A small Ghana operation will give two or three roles to most controllers; SoD is enforced by **forbidden-pair checks at grant time and at use time** (the `SeparationOfDutiesException` pattern in `PettyCashService.ApproveVoucherAsync` is the template).
- **Three Ghana-specific roles are non-negotiable:** `GraComplianceOfficer` (signs e-VAT IRNs / iTaPS exports / WHT certificates), `TreasuryOfficer` (FX rate entry + revaluation against the BoG provider), and `ExternalAuditor` (read-only, time-boxed, distinct from internal `Auditor`). The first two are required by GRA's e-VAT rollout (1st-phase live since 2022, all VAT-registered businesses since 2024) and by the standard internal-control posture for FX-bearing entities; the third is an audit-firm requirement.

---

## 2. Reference taxonomies (Track 1)

| System | Standard finance roles (canonical names) | Notable pattern |
| --- | --- | --- |
| **NetSuite** | A/P Clerk, A/R Clerk, Accountant, Bookkeeper, Controller, CFO, Auditor (as restricted role variants), Cash Manager, Tax Manager | Clerk vs Manager split per ledger area; Controller has period-close authority; multi-role stacking is standard. ([NetSuite Roles Overview](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N285436.html), [NetSuite Standard Roles Permissions Table](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N295396.html)) |
| **Microsoft D365 Finance & Ops** | Accounts payable clerk, Accounts payable manager, Accounts receivable clerk, Accounts receivable manager, General ledger accountant, Accounting manager, Treasurer, Tax accountant, Auditor | Roles are containers of *duties* which are containers of *privileges*; Microsoft ships 80+ standard roles and explicitly maps SoD violations between them. ([D365 Role-based security](https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/sysadmin/role-based-security), [Rand Group SoD overview](https://www.randgroup.com/insights/microsoft/dynamics-365/finance-operations/dynamics-365-finance-and-operations-security-and-segregation-of-duties/)) |
| **Sage Intacct** | Admin, Finance Manager, A/P Clerk, A/R Clerk, Auditor, plus user-built roles | Permission verbs are explicit (`View / Add / Edit / Delete` per list); roles "stack" so a user can hold many; explicit SoD example "AP clerk can add a vendor but only their manager enters bank info." ([Sage Intacct Roles & Permissions](https://www.crosscountry-consulting.com/insights/blog/12-days-of-sage-intacct-roles-and-permissions/), [Sage Intacct Permissions overview](https://www.intacct.com/ia/docs/en_US/help_action/Administration/Permissions/permissions-overview.htm)) |
| **Xero (Business)** | Adviser, Standard, Invoice Only, Read Only, plus toggles `Manage Users` and `Payroll Admin` | SMB-shaped; a single "role" plus binary toggles. Adviser ≈ accountant, Standard ≈ bookkeeper, Invoice Only ≈ billing clerk. Read Only excludes payroll by design. ([Xero User roles and permissions](https://central.xero.com/s/article/User-roles-and-permissions-in-Xero-Business-edition)) |
| **QuickBooks Online (Plus / Advanced)** | Primary Admin, Company Admin, Standard (all access / limited customer / limited vendor / limited customer & vendor), Reports Only, Time Tracking Only, plus custom roles in Advanced | Single-role per user is the SMB default; Advanced unlocks custom roles and named pre-defined ones (`Sales manager`, `Expense manager`). ([QBO User roles & access](https://quickbooks.intuit.com/learn-support/en-us/help-article/access-permissions/user-roles-access-rights-quickbooks-online/L66POfRrI_US_en_US), [QBO custom roles](https://quickbooks.intuit.com/learn-support/en-us/help-article/access-permissions/add-manage-custom-roles-quickbooks-online-advanced/L8Ugph7xl_US_en_US)) |
| **Odoo (Accounting)** | Billing, Accountant, Adviser/Financial Manager, Auditor (plus Read-only) | Hierarchical inclusion: Financial Manager ⊇ Accountant ⊇ Billing. Auditor is orthogonal. ([Odoo Accounting access rights](https://www.cybrosys.com/odoo/odoo-books/odoo-17-accounting/settings/access-rights-for-users-in-odoo-accounting/)) |
| **SAP Business One** | Predefined authorisation groups for finance, sales, purchasing, inventory; per-screen `Full / Read-only / No` | Group-based, small fixed library, copy-and-customise pattern; roles are "containers of authorisations" rather than named jobs. ([SAP B1 Authorizations how-to](https://help.sap.com/doc/04688cec5620478ea24be266ce0a1eda/10.0/en-US/c30f04de51bb4a6b810537a8e6a278d1.pdf)) |

**Recurring patterns we should adopt:**
1. Clerk-vs-Manager split per ledger area (AP, AR, GL, Treasury). NetSuite, D365, Sage Intacct.
2. Multi-role grants ("role stacking"). NetSuite, Sage Intacct, Odoo, D365.
3. Auditor as an explicit, orthogonal, read-only role (everyone). Often split internal vs external.
4. Treasurer as its own role wherever FX or multi-currency cash is significant. D365, NetSuite Cash Manager.
5. Tax / compliance officer split out wherever a regulated filing exists. D365 Tax accountant; this maps directly to GRA filings.

---

## 3. Forbidden-pair SoD matrix (Track 2)

| # | Pair (a single user must not hold both) | Why / standard | Currently enforced in NickFinance? |
| --- | --- | --- | --- |
| 1 | **Submit voucher** + **Approve voucher** | AICPA "no one initiates and approves" + Penn OACP operational controls. Self-approval fraud risk. | **Partially** — the `SubmitVoucher` policy lets `Custodian, Approver, SiteManager, FinanceLead, Admin` all submit, and the same group (minus Custodian) can approve. The grant model permits self-approval if a user holds both `Custodian` and `Approver`. No code-level submitter≠approver check found. |
| 2 | **Approve voucher** + **Disburse voucher** | Same; cash disbursement after self-approval is the classic AP fraud. | **Yes**, in `PettyCashService.ApproveVoucherAsync` line 418-421 throws `SeparationOfDutiesException` if `DecidedByUserId == custodianUserId`. |
| 3 | **Vendor create** + **Bill enter** | Phantom-vendor fraud (Stampli, HighRadius). Standard SOX SoD pair. | **No** — `ApService.UpsertVendorAsync` and `CaptureBillAsync` are both gated only by "anyone with vendor management." Today no separate `ManageVendor` policy exists. |
| 4 | **Vendor create** + **Payment run authorise** | Phantom vendor + self-pay. Highest-impact AP SoD pair. | **No**. `ApService.PayBillAsync` has no policy gate at all today. |
| 5 | **Bill enter** + **Payment run authorise** | The classic three-way-match break: enter the bill, then pay it without separation. | **No**. |
| 6 | **Customer create** + **Invoice issue** + **Receipt record** | The AICPA "credit / billing / collection / reconciliation" four-way split. ([SecurEnds AR SoD](https://www.securends.com/blog/segregation-of-duties-in-accounts-receivable/)) | **No** — `FinanceLead` currently holds all three. |
| 7 | **Manual journal post** + **Period close** | SOX expectation: the person who can hand-write a journal must not also be the one who freezes the period. ([SecurEnds SOX](https://www.securends.com/blog/segregation-of-duties-for-sox-compliance/)) | **No** — `FinanceLead` holds both `PostJournal` and `ClosePeriod`. |
| 8 | **Bank reconciliation** + **Manual journal post** | Reconciler creating a hiding journal is the canonical bank-recon fraud. (Eftsure, Penn OACP) | **No** — bank reconciliation is unauthorized today; whoever is given it shouldn't post journals. |
| 9 | **Bank reconciliation** + **Receipt record** | Reconciler must not have created the records being reconciled. | **No**. |
| 10 | **FX rate entry** + **FX revaluation run** | A user who can both set the rate and run the gain/loss can mint period-end profit. | **No** — both `FxRateNew.razor` and a future revaluation page would, today, be gated by `ManageFloats` (a misnamed bucket). |
| 11 | **Role grant** (`ManageUsers`) + **Self-use of granted role** | ISO 27001:2022 Annex A 5.3 (formerly A.6.1.2): conflicting duties separated; grants must be reviewable after the fact. ([ISO 27001:2022 A 5.3](https://www.isms.online/iso-27001/annex-a-2022/5-3-segregation-of-duties-2022/)) | **Partial** — provisioning is in NickHR (`IIdentityProvisioningService`), so the grantor is structurally a different user. The `SecurityAuditService` records grants. Gap: no policy denies "user X grants self role Y." |
| 12 | **WHT certificate sign** + **Bill enter for the same vendor** | GRA WHT certificates are evidence to the Commissioner; signer should not also be the one who originated the underlying bill. ([GRA WHT](https://gra.gov.gh/domestic-tax/tax-types/withholding-tax/)) | **No** — no policy on `WhtCertificateBook.razor`. |

**Top 5 most load-bearing pairs for NickFinance** (used in the migration plan): #2 (already enforced — keep), #6 (AR triangle), #7 (journal/close), #4 (vendor/pay), #10 (FX rate/reval). The other seven are tier-2: real, but lower frequency.

References:
- [AICPA / Eftsure SoD overview](https://www.eftsure.com/blog/processes/segregation-of-duties/), [HighRadius AP SoD](https://www.highradius.com/resources/Blog/segregation-of-duties-accounts-payable/), [SecurEnds AR SoD](https://www.securends.com/blog/segregation-of-duties-in-accounts-receivable/), [Stampli AP fraud](https://www.stampli.com/blog/accounts-payable-fraud/segregation-of-duties/), [Penn OACP operational controls](https://oacp.upenn.edu/audit/audit101/internal-controls-guidance/operational-internal-controls/), [ISO 27001:2022 Annex A 5.3](https://www.isms.online/iso-27001/annex-a-2022/5-3-segregation-of-duties-2022/), [Numeric SoD reference](https://www.numeric.io/blog/segregation-of-duties-accounting), [SecurEnds SOX](https://www.securends.com/blog/segregation-of-duties-for-sox-compliance/).

---

## 4. Recommended NickFinance role catalog

12 roles. All site-scopable except where marked. **Multi-role per user** is the recommended grant model.

| # | Role name | One-line scope | Granted permissions (Policies key) | Site-scopable? | Typical holder | SoD with |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | **SiteCashier** | Holds physical float, submits petty-cash vouchers and cash counts. | `SubmitVoucher`, `RecordCashCount` (new), `ViewReports` (own site only) | **Yes (required)** | Per-site cashier (Tema, Kotoka, Takoradi, Aflao, Paga, Elubo) | SiteApprover, FinanceController, GraComplianceOfficer (cannot approve own submission) |
| 2 | **SiteCustodian** | Disburses cash for an approved voucher. Must not be the approver. | `DisburseVoucher`, `ManageFloats` (own site, view + reconcile) | **Yes (required)** | Per-site senior cashier or yard supervisor | SiteApprover (enforced in code), SiteCashier-of-same-voucher |
| 3 | **SiteApprover** | Approves vouchers within float ceiling for one or many sites. | `ApproveVoucher` (own site), `ViewReports` (own site) | **Yes (required)** | Per-site approver, often the operations supervisor | SiteCashier (no self-approve), SiteCustodian (no approve+disburse) |
| 4 | **SiteManager** | Approves above-ceiling vouchers and signs off site reports. | `ApproveVoucher` (above ceiling, own site), `ViewReports` (own site, all dimensions), `ManageFloats` (own site) | **Yes (required)** | One per site (Tema HQ, Kotoka, etc.) | Same as SiteApprover; cannot post manual journals |
| 5 | **ApClerk** | Vendor maintenance + bill capture. Cannot pay. | `ManageVendor` (new), `EnterBill` (new) | Yes (optional) | Tema HQ AP team | ApManager (no vendor+pay self-pair, no bill+pay self-pair) |
| 6 | **ApManager** | Authorises payment runs and oversees AP. | `RunPaymentRun` (new), `VoidBill` (new), `ViewReports` | No (HQ-only) | Tema HQ AP lead | ApClerk-of-same-vendor (no enter+pay), Treasurer (no rate+pay), GraComplianceOfficer (must sign WHT separately or not at all on same bill) |
| 7 | **ArClerk** | Customer maintenance + draft invoices. Cannot issue, cannot record receipts. | `ManageCustomer` (new), `DraftInvoice` (new) | Yes (optional) | Tema HQ AR team | ArManager / ArCashier (no triangle) |
| 8 | **ArManager** | Issues invoices (mints GRA IRN), voids invoices, runs dunning. | `IssueInvoice`, `VoidInvoice`, `RunDunning` (new), `ViewReports` | No (HQ-only) | AR lead | ArCashier (no issue+receipt) |
| 9 | **ArCashier** | Records customer receipts only. | `RecordReceipt` | Yes (optional) | Front-desk receivables clerk | ArManager (no issue+receipt), TreasuryOfficer (no recon+receipt) |
| 10 | **TreasuryOfficer** | FX rate entry, FX revaluation, bank statement import + reconciliation. | `ManageFxRates` (new), `RunFxRevaluation` (new), `ImportBankStatement` (new), `ReconcileBank` (new) | No (HQ-only) | Treasury / FX lead | FinanceController (no recon+post-journal), ArCashier (no recon+receipt) |
| 11 | **FinanceController** | Manual journals, period close, depreciation, budget lock. The "controller" seat. | `PostJournal`, `ClosePeriod`, `RunDepreciation` (new), `LockBudget` (new), `ManageBudget` (new), `ViewReports` | No (HQ-only) | CFO or financial controller | TreasuryOfficer (no post-journal+recon), all clerks (no clerk + post-journal on same area) |
| 12 | **GraComplianceOfficer** | Files iTaPS exports, signs WHT certificates, authorised signatory for e-VAT. | `ExportItaps` (new), `IssueWhtCertificate` (new), `IssueInvoice` (e-VAT path is shared with `IssueInvoice` today — split if CFO wishes) | No (HQ-only) | Tax / compliance manager named on the GRA filing | ApClerk-on-same-bill (no enter-bill + sign-WHT for same vendor) |
| 13 | **InternalAuditor** | Read-only across journal, audit log, all reports. Never grants, never posts. | `ViewReports`, `ViewAudit` | No | Internal audit | Cannot also hold any "do" role on the same tenant. Conflict-free orthogonal seat. |
| 14 | **ExternalAuditor** | Time-boxed read-only — same surface as `InternalAuditor`, but with a mandatory grant expiry. | `ViewReports`, `ViewAudit`, with `ExpiresAt != null` enforced | No | External audit firm during fieldwork | Same as InternalAuditor |
| 15 | **PlatformAdmin** | Break-glass. Full access. Audited, alerted, never the day-to-day seat. | `Admin` (and via "admin always wins" in `RoleAuthorizationHandler`, every other policy) | No | Two named individuals max | Should not also hold any clerk/manager role for everyday work |

(Numbers go to 15 because three of those — InternalAuditor, ExternalAuditor, PlatformAdmin — are oversight roles, not finance-doer roles. The "12 finance roles + 3 oversight" framing is what reads cleanly to a CFO. If sensei prefers a flat 12-role count, drop ExternalAuditor and merge into InternalAuditor with a hard grant-expiry — but I recommend keeping them separate.)

### Per-role rationale (3-5 lines each)

**1. SiteCashier.** The on-the-floor float-holder. Submits vouchers, runs daily cash counts, sees their own site's reports. Must *not* approve, must *not* disburse. Splits the current `Custodian` role into the "submit + count" half. Site-scoped because it physically holds cash at one yard.

**2. SiteCustodian.** The other half of the current `Custodian`. Takes the approved voucher and pays it out. Most six-person sites will give this to the senior cashier or yard supervisor, while the junior cashier holds `SiteCashier`. The `approver != custodian` check in `PettyCashService.ApproveVoucherAsync` is the existing template; the new check needed is `submitter != approver`.

**3. SiteApprover.** A pure approval seat, scoped to one or more sites. This is the "Approver" of today's catalog, but with the float-ceiling concept made explicit so it's distinguishable from `SiteManager`. Below ceiling = `SiteApprover` is enough. Above ceiling = `SiteManager` required. Both today are folded into `Approver` in `Policies.ApproveVoucher`; they should be different policies (`ApproveVoucher` vs `ApproveVoucherAboveCeiling`) but I'd defer that split until the CFO confirms ceiling matters.

**4. SiteManager.** One per site, the operations + finance escalation point at the yard. Can do anything `SiteApprover` can but on bigger amounts and with more report visibility. Cannot post manual journals, cannot close periods. Is the site-level "Manager" in the NetSuite Clerk-vs-Manager pattern.

**5. ApClerk.** Mirror of NetSuite/Sage `A/P Clerk`. Owns vendor master records and bill capture. Must not pay. Critical SoD for fighting phantom-vendor fraud, which is the highest-impact AP fraud per HighRadius / Stampli. Today this surface (`ApBills.razor`, `Customers.razor`-equivalent for vendors) has *no* policy gate.

**6. ApManager.** Mirror of NetSuite `A/P Manager`. Authorises the payment run, voids bills, oversees the WHT mechanics on their side. Lives at HQ because cheque/transfer authority is centralized at Tema. Forbidden pair with `ApClerk` on the same vendor or same bill.

**7. ArClerk.** AR-side vendor-equivalent. Maintains customer records, drafts invoices. Must not issue (because issuing mints a GRA IRN — irreversible) and must not record receipts. Splits the current `FinanceLead`'s AR side into clerk vs manager.

**8. ArManager.** Issues invoices (the IRN-minting moment), voids invoices, runs dunning. Lives at HQ because the IRN is on the company's GRA TIN, not site-specific. Forbidden pair with `ArCashier` on the same invoice.

**9. ArCashier.** Records the customer receipt. Often a different person from the one who issued the invoice. Front-desk seat at HQ but also legitimately at sites where customers walk in. So site-scopable, even though `ArManager` is HQ-only.

**10. TreasuryOfficer.** FX rate maintenance + FX revaluation + bank statement import + reconciliation. The reason this is its own role is item #10 in the SoD table: a user who can both set the rate and run the revaluation can mint period-end profit. So `TreasuryOfficer` does the rate entry today, and `FinanceController` reviews the revaluation tomorrow — or the system requires a second `TreasuryOfficer` to confirm. Either way, the rate-entry person is not the journal-posting person.

**11. FinanceController.** The "controller" seat. Manual journals (the bypass), period close, depreciation, budget lock. This is the role that must *not* also do bank reconciliation (item #8 in SoD), must *not* also record receipts (#6), must *not* also enter bills (#5). Today's `FinanceLead` collapses this with `ArManager` and (effectively) `ApManager` — that is the biggest SoD violation in the placeholder model.

**12. GraComplianceOfficer.** The named individual on the GRA filings — iTaPS exports, e-VAT IRN minting (or co-signed with `ArManager`), WHT certificates. This person is legally accountable to GRA per the Withholding Tax administrative guidelines. ([GRA WHT](https://gra.gov.gh/domestic-tax/tax-types/withholding-tax/), [GRA E-VAT](https://gra.gov.gh/e-services/e-vat/)). It is a deliberately small and stable seat — Nick TC-Scan probably has 1-2 people in this role total. Forbidden pair with `ApClerk` on the same vendor (don't enter the bill *and* sign the WHT for the same vendor).

**13. InternalAuditor.** Read-only orthogonal role. Sees the security audit log, all reports, all journals. Never grants, never posts, never approves. Today's `Auditor` becomes this with no rename if we want backward compat.

**14. ExternalAuditor.** Same surface as `InternalAuditor` but with a non-null `ExpiresAt` enforced at grant time. Lives during audit fieldwork (typically 2-6 weeks per year for a Big-4 audit) and disappears after. Distinct from `InternalAuditor` because the auditor user is from a different firm and the grant must auto-revoke; otherwise you forget to revoke and they have permanent access.

**15. PlatformAdmin.** The break-glass `Admin` of today. Implicit super-grant in the `RoleAuthorizationHandler` ("Admin always wins"). Should be two named individuals max, alerted-on-grant. Should not also hold `FinanceController` etc. for day-to-day work — when an admin needs to do controller work, that's the moment to grant themselves the controller role explicitly so the audit trail records the controller action, not an admin-bypass.

---

## 5. Ghana-specific roles (Track 3)

| Role | Why Ghana-specific | Compliance basis |
| --- | --- | --- |
| **GraComplianceOfficer** | Issues e-VAT (Hubtel adapter mints IRNs against GRA's E-VAT system), files iTaPS exports, signs WHT certificates that go to the supplier as evidence to GRA. | E-VAT 1st phase live since 1 October 2022; expanded to all VAT-registered businesses by 2024. ([GRA E-VAT](https://gra.gov.gh/e-services/e-vat/)) WHT-VAT agents withhold 7% and issue a certificate to the supplier; certificates are filed by the 15th of the following month. ([GRA VAT Withholding](https://gra.gov.gh/domestic-tax/tax-types/vat-withholding/), [GRA WHT](https://gra.gov.gh/domestic-tax/tax-types/withholding-tax/)) |
| **TreasuryOfficer** | Functional currency is GHS but Nick TC-Scan holds USD/EUR/GBP/NGN bank accounts (border-port operations receive cross-border payments). Daily BoG rate is the canonical reference; FX gain/loss to GL accounts 7100/7110 at period-end is material. | BoG-published rate is the standard reference for GHS conversions; FX-revaluation discipline is a standard internal-control expectation for multi-currency entities. |
| **SiteCashier / SiteCustodian / SiteApprover / SiteManager** | Six border / port sites operate 24/7, physical cash floats live at each yard, and the senior person on-site at 02:00 must be able to approve a voucher within ceiling without phoning Tema. The site-scoped role grant is what makes this safe. | Operational reality + standard control: cash held at each location is reconciled by a person at that location, not the same person who approves at HQ. |
| **ExternalAuditor** | Ghana audit firms (per ICAG / Big-4 local affiliates) do annual audits; they need read-only access during fieldwork and *no* access between audits. | ICAG audit independence + practical access hygiene. |

The placeholder catalog had none of these as distinct seats. The current `FinanceLead` is implicitly playing all four; that is the legal-exposure problem.

---

## 6. Surface × role matrix (Track 4)

Legend: `Y` = permitted, `-` = denied, `S` = permitted but site-scoped, `C` = conditional (e.g. above-ceiling, or non-self-approval). Columns abbreviated; full names in section 4.

### Existing policies (today, in `NickFinance.WebApp/Identity/Policies.cs`)

| Surface (policy key) | SiteCashr | SiteCust | SiteAppr | SiteMgr | ApClerk | ApMgr | ArClerk | ArMgr | ArCashr | TreasOff | FinCtrl | GraComp | IntAud | ExtAud | PlatAdmin |
| --- | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: |
| `SubmitVoucher` | **S** | S | - | C(own site) | - | - | - | - | - | - | - | - | - | - | Y |
| `ApproveVoucher` (≤ ceiling) | - | - | **S** | S | - | - | - | - | - | - | - | - | - | - | Y |
| `ApproveVoucher` (> ceiling) | - | - | - | **S** | - | - | - | - | - | - | Y | - | - | - | Y |
| `DisburseVoucher` (must ≠ approver) | - | **S** | - | - | - | - | - | - | - | - | - | - | - | - | Y |
| `IssueInvoice` | - | - | - | - | - | - | - | **Y** | - | - | - | C(co-sign?) | - | - | Y |
| `VoidInvoice` | - | - | - | - | - | - | - | **Y** | - | - | Y | - | - | - | Y |
| `RecordReceipt` | - | - | - | - | - | - | - | - | **Y** (S optional) | - | - | - | - | - | Y |
| `PostJournal` | - | - | - | - | - | - | - | - | - | - | **Y** | - | - | - | Y |
| `ManageFloats` | - | S | - | S | - | - | - | - | - | - | Y | - | - | - | Y |
| `ClosePeriod` | - | - | - | - | - | - | - | - | - | - | **Y** | - | - | - | Y |
| `ManageUsers` | - | - | - | - | - | - | - | - | - | - | - | - | - | - | **Y** |
| `ViewReports` | S | S | S | S | Y | Y | Y | Y | Y | Y | Y | Y | **Y** | **Y** | Y |
| `ViewAudit` | - | - | - | - | - | - | - | - | - | - | - | - | **Y** | **Y** | Y |

### Proposed new policies (currently no `[Authorize]` gate, or wrong gate)

| Surface (proposed policy) | SiteCashr | SiteCust | SiteAppr | SiteMgr | ApClerk | ApMgr | ArClerk | ArMgr | ArCashr | TreasOff | FinCtrl | GraComp | IntAud | ExtAud | PlatAdmin |
| --- | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: |
| `RecordCashCount` | **Y(S)** | Y(S) | - | - | - | - | - | - | - | - | - | - | - | - | Y |
| `ManageVendor` | - | - | - | - | **Y** | Y | - | - | - | - | - | - | - | - | Y |
| `EnterBill` | - | - | - | - | **Y** | Y | - | - | - | - | - | - | - | - | Y |
| `RunPaymentRun` | - | - | - | - | - | **Y** | - | - | - | - | - | - | - | - | Y |
| `VoidBill` | - | - | - | - | - | **Y** | - | - | - | - | Y | - | - | - | Y |
| `ManageCustomer` | - | - | - | - | - | - | **Y** | Y | - | - | - | - | - | - | Y |
| `DraftInvoice` | - | - | - | - | - | - | **Y** | Y | - | - | - | - | - | - | Y |
| `RunDunning` | - | - | - | - | - | - | - | **Y** | - | - | - | - | - | - | Y |
| `ImportBankStatement` | - | - | - | - | - | - | - | - | - | **Y** | - | - | - | - | Y |
| `ReconcileBank` | - | - | - | - | - | - | - | - | - | **Y** | - | - | - | - | Y |
| `ManageFxRates` | - | - | - | - | - | - | - | - | - | **Y** | - | - | - | - | Y |
| `RunFxRevaluation` | - | - | - | - | - | - | - | - | - | C(if controller co-signs) | **Y** | - | - | - | Y |
| `RunDepreciation` | - | - | - | - | - | - | - | - | - | - | **Y** | - | - | - | Y |
| `ManageBudget` | - | - | - | C(own site lines) | - | - | - | - | - | - | **Y** | - | - | - | Y |
| `LockBudget` | - | - | - | - | - | - | - | - | - | - | **Y** | - | - | - | Y |
| `IssueWhtCertificate` | - | - | - | - | - | - | - | - | - | - | - | **Y** | - | - | Y |
| `ExportItaps` | - | - | - | - | - | - | - | - | - | - | - | **Y** | - | - | Y |

### Where today's 6-role placeholder gets it wrong (highlighted)

1. **`FinanceLead` holds the AR triangle** (`IssueInvoice` + `RecordReceipt` + implicit customer create via `ManageUsers`-or-just-DB). Splits to `ArManager` + `ArCashier` (+ `ArClerk`). [SoD #6]
2. **`FinanceLead` holds journal+close.** Splits to `FinanceController` posting and a different `FinanceController`-or-second-controller-pair-of-eyes for close. The split itself doesn't fix it within a single role; what fixes it is requiring a *different individual* close than post — that is a periodic SoD report, not a policy gate. Still, distinguishing the surfaces lets the report check "same user posted JE-X and closed period-Y" cleanly. [SoD #7]
3. **`FinanceLead` holds `ManageFloats`.** Splits to `TreasuryOfficer` (operational) + `FinanceController` (oversight). [SoD #10]
4. **`Admin` holds `ManageFloats` exclusively today** (line 38 of `PolicyRegistration.cs`). That's an over-restriction — `FinanceLead` and `SiteManager` legitimately need it. The new model fixes this.
5. **No policy gate on `Customers.razor`, `ApBills.razor`, `Banking.razor`, `BudgetsPage.razor`, `FixedAssets.razor`, `WhtCertificateBook.razor`, `Aging.razor`, `BalanceSheet.razor`, `CashFlow.razor`, `PettyCashList.razor`, `PettyCashDetail.razor`, `CustomerStatement.razor`, `ReceiptDetail.razor`, `ApReceiptPage.razor`, `Approvals.razor`, `CashCounts.razor`, `Delegations.razor`, `Search.razor`, `Home.razor`.** Today these fall back to "any authenticated user." After the rebuild, every mutating page gets one of the new policies; read-only pages (Aging, BalanceSheet, CashFlow, CustomerStatement, Home, Search) get `ViewReports`.

---

## 7. Migration plan

### Mapping today's 6 roles onto the new 15-role catalog

| Today | Recommended replacement | Action |
| --- | --- | --- |
| `Custodian` (does both submit + disburse) | **Splits into `SiteCashier` (submit) + `SiteCustodian` (disburse)** | **No automatic rename.** Every existing `Custodian` grant in `identity.user_roles` is reviewed by HR + the site manager, who decide which half (or both — see SoD note) to re-grant. The migration ships a report listing the existing grants, not a mass-update. |
| `Approver` | `SiteApprover` (1:1 rename, site grants preserved) | Schema update: `UPDATE identity.roles SET name = 'SiteApprover' WHERE name = 'Approver';`. All existing `[Authorize(Policy=...)]` decorators continue to work because policies map by role name on the `RoleRequirement` side. |
| `SiteManager` | `SiteManager` (kept as-is) | No change. |
| `FinanceLead` | **Splits into `FinanceController` (journals/close/depreciation/budget) + `ArManager` (issue/void/dunning) + `ApManager` (pay run, void bill, WHT-mech) + `TreasuryOfficer` (FX/bank/recon)** | This is the largest re-grant action. Today there are O(2-3) `FinanceLead` grants in production; sensei + CFO decide which of the four new roles each person gets. Default suggestion: the CFO gets `FinanceController` + `PlatformAdmin` break-glass; the financial controller gets `FinanceController`; AR/AP/Treasury seats get split per the org chart. |
| `Auditor` | `InternalAuditor` (1:1 rename) + new `ExternalAuditor` for audit-firm grants | Schema update: rename. New role added. |
| `Admin` | `PlatformAdmin` (rename, semantics unchanged — "admin always wins" stays) | Rename only. |

### New roles to add to `identity.roles` (do not write the SQL — just the shape)

```
INSERT INTO identity.roles (role_id, name, description, created_at) VALUES
  (gen_random_uuid(), 'SiteCashier',           'Holds float; submits petty-cash vouchers and cash counts.', now()),
  (gen_random_uuid(), 'SiteCustodian',         'Disburses cash for an approved voucher (≠ approver).',     now()),
  (gen_random_uuid(), 'ApClerk',               'Vendor master + bill capture. Cannot pay.',                  now()),
  (gen_random_uuid(), 'ApManager',             'Authorises payment runs and voids bills.',                  now()),
  (gen_random_uuid(), 'ArClerk',               'Customer master + draft invoices. Cannot issue or receipt.', now()),
  (gen_random_uuid(), 'ArManager',             'Issues + voids invoices, runs dunning.',                    now()),
  (gen_random_uuid(), 'ArCashier',             'Records customer receipts only.',                            now()),
  (gen_random_uuid(), 'TreasuryOfficer',       'FX rate + revaluation + bank statement import + reconcile.', now()),
  (gen_random_uuid(), 'FinanceController',     'Manual journals, period close, depreciation, budget lock.', now()),
  (gen_random_uuid(), 'GraComplianceOfficer',  'iTaPS export + e-VAT signatory + WHT certificate signer.',  now()),
  (gen_random_uuid(), 'ExternalAuditor',       'Time-boxed read-only access for audit firms.',              now());
-- Plus three renames: Approver→SiteApprover, Auditor→InternalAuditor, Admin→PlatformAdmin.
```

(Don't run this. The shape is for sensei to review.)

### Backward-compat of existing `[Authorize(Policy=...)]` decorations

| Decoration in code | Rename needed? | Why |
| --- | --- | --- |
| `Policies.SubmitVoucher` on `PettyCashNew.razor` | No | Same name, narrower role-set in `PolicyRegistration` (drop `Approver`, `SiteManager`, `FinanceLead` from the `RoleRequirement` for this policy — only `SiteCashier`, `SiteCustodian`, `Admin`). |
| `Policies.ApproveVoucher` on `DelegationNew.razor` | No | Role-set narrows to `SiteApprover, SiteManager, PlatformAdmin`. |
| `Policies.IssueInvoice` on `ArNew.razor` | No | Role-set narrows to `ArManager, GraComplianceOfficer, PlatformAdmin`. |
| `Policies.ManageFloats` on `Floats.razor`, `FloatNew.razor`, `CashCountNew.razor`, `FxRateNew.razor` | **Yes — split.** | `FxRateNew.razor` should move to a new `Policies.ManageFxRates`. `CashCountNew.razor` should move to a new `Policies.RecordCashCount`. The float pages stay on `ManageFloats` but the role-set adds `SiteCustodian, SiteManager, TreasuryOfficer`. |
| `Policies.PostJournal` on `JournalEntry*.razor` | No | Role-set narrows to `FinanceController, PlatformAdmin`. |
| `Policies.ClosePeriod` on `PeriodClose.razor` | No | Role-set narrows to `FinanceController, PlatformAdmin`. |
| `Policies.ViewReports` on `Reports.razor`, `SitePnL.razor`, `FxRates.razor` | No | Stays open to all authenticated users; site-scoping is handled per-row in the report query, not in the policy. |
| `Policies.ViewAudit` (no page decorated today; used by the audit-log view) | No | Role-set adds `ExternalAuditor`. |
| `Policies.ManageUsers` (no page in NickFinance — admin lives in NickHR) | No | Role-set narrows to `PlatformAdmin`. |
| `Policies.Admin` (escape hatch) | No | Role-set is just `PlatformAdmin`. |
| **New policies — see proposed list in section 6** | New code | Each gets a new `Policies.X` constant + a `RoleRequirement` in `PolicyRegistration`. |

Net: zero rename of existing policy keys, narrower role-sets in 7 of them, 13 new policy keys, 4 page decorations to add or change.

### HR-side UX impact (NSCIS-style multi-select panel)

`NickHR/src/NickHR.WebApp/Components/Shared/ModuleAccessPanel.razor` currently builds 6 rows in `BuildRoleRows` (lines 247-261). After the catalog change:

- The 6 hard-coded rows become **15** rows in the same shape: `RoleName`, `Description`, `IsSiteScoped`, plus the existing per-row site-picker for the site-scoped ones.
- Add a "Site scope" column that shows `(any site)` for tenant-wide grants and a `MudSelect<Site>` for site-scoped ones (already exists in the component for `Approver` / `SiteManager`).
- Add an "Expires" column (date-picker) — today the schema has `ExpiresAt` on `RoleGrant` but the panel ignores it. `ExternalAuditor` should require it (panel-side validation).
- Group the rows visually: "Site operations" (4), "AP" (2), "AR" (3), "Treasury & Control" (2), "Compliance" (1), "Oversight" (3). MudExpansionPanel per group, default collapsed except "Site operations".
- The "Custodian" row becomes two rows ("SiteCashier", "SiteCustodian"). Existing grants that read `Custodian` from the DB display under a dimmed banner "Legacy role — please re-grant as SiteCashier or SiteCustodian" until HR clicks through.

No data migration ships automatically. The panel does the legacy-role display + re-grant flow as a one-time HR walk-through.

---

## 8. Open questions for sensei + CFO

1. **GraComplianceOfficer split or single?** Should iTaPS export, e-VAT signatory, and WHT certificate signing be one role or three? GRA notice-of-appointment lists *one* named officer per filing type, so legally three names are possible. Operationally Nick TC-Scan probably uses one person. Recommendation: keep as one role; if a single-person bus-factor concern emerges, split later.

2. **Multi-role per user, or single-role like Xero/QuickBooks?** Recommendation: multi-role (matches NetSuite, Sage Intacct, D365, Odoo, and the current schema which already supports it via `identity.user_roles`). Risk: SoD violations are now check-time, not schema-time. Mitigation: forbidden-pair check at grant time (HR panel rejects a grant that would create a forbidden pair) plus a periodic SoD report. CFO call.

3. **Site-scoping default for new grants — tenant-wide unless explicitly site-scoped, or required-explicit?** Today tenant-wide is the default (`SiteId = null` works fine). For `SiteCashier / SiteCustodian / SiteApprover / SiteManager` I'd make site-scoping **required** (the panel won't let you save without a site). For `ApClerk / ArClerk / ArCashier` I'd make it **optional** (HQ team is tenant-wide; a site-only AR cashier is also valid). Confirm.

4. **InternalAuditor scope — does it include `security_audit_log` and payroll-adjacent journals?** Today `Auditor` policy `ViewAudit` includes the security audit log. Recommendation: keep, because that's the standard internal-audit posture. Payroll journals come from NickHR via `IGlSyncService` and end up as `INTRA01` journals in NickFinance. The auditor sees them via `JournalEntryList.razor` (currently gated by `PostJournal`, not `ViewReports`!) — that needs a separate fix; the auditor should see journals via a new `ViewJournal` policy that grants list/detail without the post button.

5. **"Deploy NickFinance" — a NickFinance role or only a server-admin?** Today there is no in-app deploy surface. Deploy is `Deploy.ps1` running on the host as the local admin account, gated by NSSM and Cloudflare Access. Recommendation: leave it out of the in-app role catalog. If a future admin page exposes a "rebuild" button, gate it on `PlatformAdmin` plus a separate "DeployModule" policy, but don't ship that policy now.

6. **Approver-vs-submitter — code check or policy check?** `Custodian + Approver` granted to one user today permits self-approval (the policy gate doesn't catch it; only the disburser-vs-approver check in `ApproveVoucherAsync` would). Recommendation: add a code-level `if (voucher.RequestedByUserId == approverUserId) throw new SeparationOfDutiesException(...)` to `ApproveVoucherAsync`, mirroring the existing pattern at line 418-421. Ask sensei whether to add that as part of this catalog rollout or in a follow-up SoD-hardening wave.

7. **External auditor mechanics — single account per firm or per individual?** Audit-firm hygiene says per-individual. Schema supports it (one row per user). Confirm with sensei whether to require the audit-firm name on the user record and if so where (NickHR `EmployeeProfile` or a free-text "audit firm" field on `identity.users`).

8. **Forbidden-pair enforcement — HR panel-time, code-time, both?** The right answer is "both" — HR panel rejects forbidden grants at save, and the code re-checks at use-time so a manually-INSERTed grant can't slip past. The wider question is how strict to be: do we forbid `ApClerk + ApManager` on the same user (yes — phantom vendor + self-pay), or just warn (because at a 50-person company you might need it temporarily)? Recommendation: hard-forbid the top 5 SoD pairs (table section 3), warn-only the next 7. CFO call.

---

*— end of document —*
