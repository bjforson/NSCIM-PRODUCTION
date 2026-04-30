-- Least-privilege grants for the nscim_app DB role on all NickFinance schemas.
-- Run as: psql -h localhost -U postgres -d nickhr -f grant-nscim-app-finance.sql
-- Idempotent — re-runnable.

DO $$
DECLARE
    s TEXT;
BEGIN
    FOREACH s IN ARRAY ARRAY['finance','petty_cash','coa','ar','ap','banking','fixed_assets','budgeting','identity','public']
    LOOP
        EXECUTE format('GRANT USAGE ON SCHEMA %I TO nscim_app', s);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO nscim_app', s);
        EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO nscim_app', s);
        EXECUTE format('GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA %I TO nscim_app', s);
        EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO nscim_app', s);
        EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT USAGE, SELECT ON SEQUENCES TO nscim_app', s);
        EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT EXECUTE ON FUNCTIONS TO nscim_app', s);
        RAISE NOTICE 'Granted on schema %', s;
    END LOOP;
END $$;
