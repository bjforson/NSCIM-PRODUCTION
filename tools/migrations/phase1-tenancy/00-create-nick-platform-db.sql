-- =============================================================================
-- NICKSCAN ERP SOLUTION — Phase 1 — Create the canonical platform database
--
-- Run as the postgres superuser, NOT inside an existing database connection.
--
--   psql -h localhost -U postgres -f 00-create-nick-platform-db.sql
--
-- This script is idempotent: re-running it does nothing if nick_platform
-- already exists.
-- =============================================================================

SELECT 'CREATE DATABASE nick_platform
    WITH ENCODING = ''UTF8''
         LC_COLLATE = ''en_US.UTF-8''
         LC_CTYPE   = ''en_US.UTF-8''
         TEMPLATE = template0'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'nick_platform') \gexec

COMMENT ON DATABASE nick_platform IS
'Canonical NICKSCAN ERP platform database. Owns the tenants table and platform-wide config. Phase 1 (multi-tenancy foundation).';
