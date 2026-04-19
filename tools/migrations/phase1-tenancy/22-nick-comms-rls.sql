-- =============================================================================
-- NICKSCAN ERP SOLUTION - Phase 1 - Enable Row-Level Security on nick_comms
-- 
-- Each table gets a policy that filters by current_setting('app.tenant_id').
-- The TenantOwnedEntityInterceptor sets this session variable on every
-- connection. Bypass for the postgres superuser via the BYPASSRLS attribute
-- (already on by default for postgres role).
-- =============================================================================
BEGIN;
SET LOCAL search_path = public;

-- api_keys
ALTER TABLE "api_keys" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_api_keys" ON "api_keys";
CREATE POLICY "tenant_isolation_api_keys" ON "api_keys"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- email_messages
ALTER TABLE "email_messages" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_email_messages" ON "email_messages";
CREATE POLICY "tenant_isolation_email_messages" ON "email_messages"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- otp_sessions
ALTER TABLE "otp_sessions" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_otp_sessions" ON "otp_sessions";
CREATE POLICY "tenant_isolation_otp_sessions" ON "otp_sessions"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- sms_messages
ALTER TABLE "sms_messages" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_sms_messages" ON "sms_messages";
CREATE POLICY "tenant_isolation_sms_messages" ON "sms_messages"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

COMMIT;
