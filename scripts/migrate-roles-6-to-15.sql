-- ============================================================================
-- Phase 1 of the role-overhaul wave (2026-04-29)
-- ----------------------------------------------------------------------------
-- Adds the 9 new roles from the 15-role catalog. Existing 6 legacy rows
-- (Custodian, Approver, SiteManager, FinanceLead, Auditor, Admin) stay in
-- place — HR re-grants existing users into the new role names via the
-- updated UserDialog over the next few days. The script ends with a report
-- of every existing user_role grant under a legacy role name so HR can work
-- through the list.
--
-- Idempotency: ON CONFLICT (name) DO NOTHING means a re-run is safe. The
-- script is also safe to run BEFORE the bootstrap CLI's 15-catalog seed
-- step has executed (operator can choose either order); when both run the
-- bootstrap is the no-op.
--
-- This script does NOT mutate any existing user_roles.role_id values. The
-- audit trail of old grants is preserved intact.
--
-- Usage (from a host where psql can reach the NSCIM Postgres):
--     psql "$NICKERP_FINANCE_DB_CONNECTION" \
--          -f scripts/migrate-roles-6-to-15.sql
-- ============================================================================

\set ON_ERROR_STOP on
BEGIN;

-- New roles. Ids 7..20 carve out the "post-Initial_Identity" range. They
-- match the deterministic ids assigned by the bootstrap CLI's
-- SeedNewRolesFromCatalogAsync step, so a re-run from either side is a
-- no-op rather than a conflict.
INSERT INTO identity.roles (role_id, name, description) VALUES
  (7,  'SiteCashier',          'Holds the petty-cash float; submits vouchers and cash counts.'),
  (8,  'SiteCustodian',        'Disburses cash for an approved voucher (must differ from approver).'),
  (9,  'ApClerk',              'Vendor master + bill capture. Cannot pay.'),
  (10, 'ApManager',            'Authorises payment runs and voids bills.'),
  (11, 'ArClerk',              'Customer master + draft invoices. Cannot issue or record receipts.'),
  (12, 'ArManager',            'Issues and voids invoices, runs dunning.'),
  (13, 'ArCashier',            'Records customer receipts only.'),
  (14, 'TreasuryOfficer',      'FX rate + revaluation + bank statement import + reconciliation.'),
  (15, 'FinanceController',    'Manual journals, period close, depreciation, budget lock.'),
  (16, 'GraComplianceOfficer', 'iTaPS export, e-VAT signatory, WHT certificate signer.'),
  (17, 'InternalAuditor',      'Read-only across journals, audit log, and reports.'),
  (18, 'ExternalAuditor',      'Time-boxed read-only access for external audit firms (ExpiresAt required).'),
  (19, 'SiteApprover',         'Approves vouchers within the float ceiling for one or more sites.'),
  (20, 'PlatformAdmin',        'Full break-glass access. Two named individuals max.')
ON CONFLICT (name) DO NOTHING;

-- ============================================================================
-- Operator review report.
-- Lists every active grant (non-expired) under one of the 6 legacy role
-- names. HR re-grants each user under the new catalog through the
-- HR-side UserDialog and revokes the legacy grant once the replacement
-- is in place. The legacy roles stay valid in the meantime.
-- ============================================================================

\echo
\echo '== Existing user_role grants under the LEGACY 6-role names =='
\echo '   These users need to be re-granted under the 15-role catalog.'
\echo
SELECT u.email,
       r.name        AS legacy_role,
       ur.site_id,
       ur.granted_at,
       ur.expires_at,
       ur.granted_by_user_id
  FROM identity.user_roles ur
  JOIN identity.users u ON u.internal_user_id = ur.user_id
  JOIN identity.roles r ON r.role_id = ur.role_id
 WHERE r.name IN ('Custodian', 'Approver', 'SiteManager', 'FinanceLead', 'Auditor', 'Admin')
   AND (ur.expires_at IS NULL OR ur.expires_at > now())
 ORDER BY r.name, u.email;

\echo
\echo '== Suggested mapping (for HR review; not auto-applied) =='
\echo '   Custodian   -> SiteCashier OR SiteCustodian  (HR splits per person)'
\echo '   Approver    -> SiteApprover                  (1:1 rename in spirit)'
\echo '   SiteManager -> SiteManager                   (kept as-is)'
\echo '   FinanceLead -> FinanceController + ApManager + ArManager + TreasuryOfficer'
\echo '                 (HR splits per person; sensei + CFO decide each split)'
\echo '   Auditor     -> InternalAuditor (rename) + new ExternalAuditor for firms'
\echo '   Admin       -> PlatformAdmin                 (rename)'

\echo
\echo '== HR action required =='
\echo '   1. Open the HR module > Users > each affected user'
\echo '   2. Add the new role grants per the suggested mapping above'
\echo '   3. Revoke the legacy grant once the replacement is in place'
\echo '   4. The 6 legacy roles remain authorised as policy fallbacks until then'

COMMIT;
