-- One-time cleanup: remove the smoke-test rows that landed under tenant 1
-- before the smoke runner was switched to tenant 999_999.
--
-- Strategy:
--   1. DELETE the petty_cash.* rows (vouchers, lines, approvals, floats).
--   2. LEAVE the corresponding 3 ledger events in place — finance.ledger_events
--      is append-only by Postgres trigger (forbids DELETE + UPDATE), so the
--      events stay as orphan 1 GHS journal postings tagged
--      source_module='petty_cash'. They net to 1 GHS each (well below any
--      materiality threshold) and are visibly tagged for audit traceback.
--
-- Idempotent. Safe to re-run (every WHERE is conservative).

\set ON_ERROR_STOP on
BEGIN;

\echo '== Smoke voucher rows in tenant 1 BEFORE cleanup =='
SELECT voucher_no, status, purpose
  FROM petty_cash.vouchers
 WHERE tenant_id = 1 AND purpose LIKE 'SMOKE TEST%'
 ORDER BY created_at;

DELETE FROM petty_cash.voucher_approvals
 WHERE voucher_id IN (
    SELECT voucher_id FROM petty_cash.vouchers
     WHERE tenant_id = 1 AND purpose LIKE 'SMOKE TEST%'
 );

DELETE FROM petty_cash.voucher_line_items
 WHERE voucher_id IN (
    SELECT voucher_id FROM petty_cash.vouchers
     WHERE tenant_id = 1 AND purpose LIKE 'SMOKE TEST%'
 );

DELETE FROM petty_cash.voucher_receipts
 WHERE voucher_id IN (
    SELECT voucher_id FROM petty_cash.vouchers
     WHERE tenant_id = 1 AND purpose LIKE 'SMOKE TEST%'
 );

DELETE FROM petty_cash.vouchers
 WHERE tenant_id = 1 AND purpose LIKE 'SMOKE TEST%';

DELETE FROM petty_cash.floats
 WHERE tenant_id = 1
   AND site_id = '76fd5c0c-38a1-a648-b945-16ca42056afb'
   AND currency_code = 'GHS';

\echo '== Smoke vouchers under tenant 1 AFTER cleanup (expect 0) =='
SELECT count(*) AS remaining FROM petty_cash.vouchers WHERE tenant_id = 1 AND purpose LIKE 'SMOKE TEST%';

\echo '== Smoke floats under tenant 1 AFTER cleanup (expect 0) =='
SELECT count(*) FROM petty_cash.floats WHERE tenant_id = 1 AND site_id = '76fd5c0c-38a1-a648-b945-16ca42056afb';

\echo '== Orphan ledger events left behind (expected: 3 small 1 GHS events tagged source_module=petty_cash) =='
SELECT event_id, narration, tenant_id
  FROM finance.ledger_events
 WHERE event_id IN (
    '726d5fea-2dfd-47f1-895c-952ce3184442'::uuid,
    '79932772-414b-499b-b6fb-aa9965b1645e'::uuid,
    'fcddde15-bae8-4155-8b38-acc08b7206ff'::uuid
 );

COMMIT;
