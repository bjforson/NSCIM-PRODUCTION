-- One-time HR cleanup: mark legitimate @nickscan.com NickHR accounts as
-- email-confirmed so they can sign in to NickFinance via /login.
--
-- Why: NickHR's `Users` table (ASP.NET Identity) ships with
-- EmailConfirmed=false until the user clicks a confirmation link, but
-- NickHR doesn't have SMTP wired up so the confirmation email never
-- gets sent. NickFinance's PasswordVerifier rejects unconfirmed
-- accounts (correct security default). One-time SQL flip is the
-- cleanest unblock until SMTP is configured.
--
-- Idempotent: safe to re-run.

\set ON_ERROR_STOP on
BEGIN;

\echo '== Before flip =='
SELECT "Email", "EmailConfirmed"
  FROM public."Users"
 WHERE "Email" IN ('jonathanforson@nickscan.com', 'angelaayanful@nickscan.com')
 ORDER BY "Email";

UPDATE public."Users"
   SET "EmailConfirmed" = true
 WHERE "Email" IN ('jonathanforson@nickscan.com', 'angelaayanful@nickscan.com')
   AND "EmailConfirmed" = false;

\echo '== After flip =='
SELECT "Email", "EmailConfirmed"
  FROM public."Users"
 WHERE "Email" IN ('jonathanforson@nickscan.com', 'angelaayanful@nickscan.com')
 ORDER BY "Email";

COMMIT;
