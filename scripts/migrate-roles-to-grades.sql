-- ============================================================================
-- Phase 2 of the role-overhaul wave (2026-04-30)
-- ----------------------------------------------------------------------------
-- Adds the 10 grade rows (8 ops + 2 audit) from the new concentric catalogue
-- and prints a punch-list of every existing user_role grant under a legacy
-- 6-name or interim 14-name role so HR can re-grant each user into the new
-- grade catalogue via the HR-side UserDialog.
--
-- This script does NOT mutate any existing user_roles.role_id values. The
-- audit trail of historical grants is preserved intact, and legacy / interim
-- grant rows resolve to ZERO permissions because none of those role ids are
-- joined into identity.role_permissions — that's the right fail-closed default
-- during the migration window.
--
-- Idempotency: ON CONFLICT (name) DO NOTHING means a re-run is safe. The
-- script is also safe to run BEFORE the bootstrap CLI's
-- SeedGradesFromCatalogAsync step has executed (operator can choose either
-- order); when both run the bootstrap is the no-op.
--
-- Usage (from a host where psql can reach the NSCIM Postgres):
--     psql "$NICKERP_FINANCE_DB_CONNECTION" \
--          -f scripts/migrate-roles-to-grades.sql
-- ============================================================================

\set ON_ERROR_STOP on
BEGIN;

-- ----------------------------------------------------------------------------
-- 1) Insert the 10 new grade rows (ids 21..30 — strictly above the legacy
--    1..20 range so no FK collision with existing user_roles rows).
-- ----------------------------------------------------------------------------
INSERT INTO identity.roles (role_id, name, description) VALUES
  -- Ops ladder (8 grades, each strictly extending its parent)
  (21, 'Viewer',              'Read-only access to the home page.'),
  (22, 'SiteCashier',         'Site-scoped: submit petty-cash vouchers and run cash counts.'),
  (23, 'SiteSupervisor',      'Site-scoped: SiteCashier + approve site vouchers and view site reports.'),
  (24, 'Bookkeeper',          'HQ-side bookkeeper: SiteSupervisor + capture bills, draft invoices, master-data read.'),
  (25, 'Accountant',          'Bookkeeper + record receipts, run depreciation, full reports, petty-cash disburse.'),
  (26, 'SeniorAccountant',    'Accountant + issue/void invoices, payment runs, FX rates, bank reconciliation, WHT certificates, dunning, master-data write.'),
  (27, 'FinancialController', 'SeniorAccountant + manual journals, period close, FX revaluation, budget lock, iTaPS export, recurring vouchers.'),
  (28, 'SuperAdmin',          'FinancialController + manage NickFinance access, view security audit log. Break-glass; two named individuals max.'),
  -- Audit ring (2 grades, parallel ring)
  (29, 'InternalAuditor',     'Read-only across journals, audit log, and every operational page. No write verbs.'),
  (30, 'ExternalAuditor',     'Time-boxed read-only access for external audit firms (ExpiresAt + audit_firm required).')
ON CONFLICT (name) DO NOTHING;

-- ============================================================================
-- 2) Operator review report — lists every active grant under a legacy role
--    name so HR can work through the migration list.
-- ============================================================================

\echo
\echo '== Existing user_role grants under the LEGACY catalogues =='
\echo '   The legacy 6 (Phase 0) and interim 14 (Phase 1) names appear here.'
\echo '   Each user listed needs to be re-granted under one of the 10 new grades.'
\echo

SELECT u.email,
       r.name        AS legacy_role,
       ur.site_id,
       ur.granted_at,
       ur.expires_at,
       ur.audit_firm,
       ur.granted_by_user_id
  FROM identity.user_roles ur
  JOIN identity.users u ON u.internal_user_id = ur.user_id
  JOIN identity.roles r ON r.role_id = ur.role_id
 WHERE r.name IN (
       -- Legacy 6 (Phase 0)
       'Custodian', 'Approver', 'SiteManager', 'FinanceLead', 'Auditor', 'Admin',
       -- Interim 14 (Phase 1, 2026-04-29)
       'SiteCashier', 'SiteCustodian', 'SiteApprover',
       'ApClerk', 'ApManager',
       'ArClerk', 'ArManager', 'ArCashier',
       'TreasuryOfficer', 'FinanceController', 'GraComplianceOfficer',
       'InternalAuditor', 'ExternalAuditor', 'PlatformAdmin')
   AND ur.role_id < 21  -- only legacy / interim ids; the new grades start at 21
   AND (ur.expires_at IS NULL OR ur.expires_at > now())
 ORDER BY r.name, u.email;

\echo
\echo '== Suggested mapping (for HR review; not auto-applied) =='
\echo '   Phase 0 names ->'
\echo '     Custodian   -> Bookkeeper or Accountant (HR judges seniority)'
\echo '     Approver    -> SiteSupervisor (site grant) or Accountant (HQ)'
\echo '     SiteManager -> SiteSupervisor (kept as site-scoped supervisor)'
\echo '     FinanceLead -> FinancialController'
\echo '     Auditor     -> InternalAuditor (rename)'
\echo '     Admin       -> SuperAdmin (rename, two named individuals only)'
\echo
\echo '   Phase 1 names ->'
\echo '     SiteCashier         -> SiteCashier (kept; same name, different bundle)'
\echo '     SiteCustodian       -> Accountant (HQ-side disbursement role goes higher)'
\echo '     SiteApprover        -> SiteSupervisor (rename in spirit)'
\echo '     ApClerk / ArClerk   -> Bookkeeper (clerks consolidate into bookkeeper)'
\echo '     ApManager / ArMgr   -> SeniorAccountant (manager-level write verbs)'
\echo '     ArCashier           -> Accountant (receipt recording goes accountant)'
\echo '     TreasuryOfficer     -> SeniorAccountant (FX + bank rec)'
\echo '     FinanceController   -> FinancialController (kept)'
\echo '     GraComplianceOfficer-> SeniorAccountant or FinancialController (HR judges)'
\echo '     InternalAuditor     -> InternalAuditor (kept; same name, fresh bundle)'
\echo '     ExternalAuditor     -> ExternalAuditor (kept; needs ExpiresAt + audit_firm)'
\echo '     PlatformAdmin       -> SuperAdmin (rename)'

\echo
\echo '== HR action required =='
\echo '   1. Open NickHR > Users > each affected user'
\echo '   2. Pick a single grade from the dropdown per the suggested mapping'
\echo '      (the new UserDialog flips from multi-checkbox to single-grade picker)'
\echo '   3. Save — the new grant is checked by SodService (audit-vs-ops, etc.)'
\echo '   4. Revoke the legacy grant once the new one is in place'
\echo '   5. Until step 4, the legacy grant is harmless: zero permissions'

COMMIT;
