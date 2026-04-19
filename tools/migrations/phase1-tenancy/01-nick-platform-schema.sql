-- =============================================================================
-- NICKSCAN ERP SOLUTION — Phase 1 — Platform DB schema
--
-- Run AGAINST the nick_platform database (after 00-create-nick-platform-db.sql).
--
--   psql -h localhost -U postgres -d nick_platform -f 01-nick-platform-schema.sql
--
-- This script mirrors the EF Core migration in
-- platform/NickERP.Platform.Tenancy.Database/Migrations/. We apply it
-- manually here so the operator can review the exact statements before
-- running them.
-- =============================================================================
BEGIN;
SET LOCAL search_path = public;

-- ---- tenants ----
CREATE TABLE IF NOT EXISTS tenants (
    "Id" BIGINT GENERATED ALWAYS AS IDENTITY,
    "Code" VARCHAR(50) NOT NULL,
    "Name" VARCHAR(200) NOT NULL,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "BillingPlan" VARCHAR(50) NOT NULL DEFAULT 'internal',
    "TimeZone" VARCHAR(50) NOT NULL DEFAULT 'Africa/Accra',
    "Locale" VARCHAR(20) NOT NULL DEFAULT 'en-GH',
    "Currency" VARCHAR(3) NOT NULL DEFAULT 'GHS',
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" TIMESTAMP WITH TIME ZONE,
    CONSTRAINT pk_tenants PRIMARY KEY ("Id")
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_tenants_code ON tenants ("Code");

-- ---- tenant_users ----
CREATE TABLE IF NOT EXISTS tenant_users (
    "Id" BIGINT GENERATED ALWAYS AS IDENTITY,
    "TenantId" BIGINT NOT NULL,
    "UserId" VARCHAR(100) NOT NULL,
    "Username" VARCHAR(100),
    "IsPrimary" BOOLEAN NOT NULL DEFAULT TRUE,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" TIMESTAMP WITH TIME ZONE,
    CONSTRAINT pk_tenant_users PRIMARY KEY ("Id"),
    CONSTRAINT fk_tenant_users_tenants FOREIGN KEY ("TenantId") REFERENCES tenants ("Id") ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_tenant_users_tenant_user ON tenant_users ("TenantId", "UserId");
CREATE INDEX IF NOT EXISTS ix_tenant_users_user ON tenant_users ("UserId");

-- ---- tenant_module_subscriptions ----
CREATE TABLE IF NOT EXISTS tenant_module_subscriptions (
    "Id" BIGINT GENERATED ALWAYS AS IDENTITY,
    "TenantId" BIGINT NOT NULL,
    "ModuleName" VARCHAR(50) NOT NULL,
    "IsEnabled" BOOLEAN NOT NULL DEFAULT TRUE,
    "ExpiresAt" TIMESTAMP WITH TIME ZONE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" TIMESTAMP WITH TIME ZONE,
    CONSTRAINT pk_tenant_module_subscriptions PRIMARY KEY ("Id"),
    CONSTRAINT fk_tenant_module_subscriptions_tenants FOREIGN KEY ("TenantId") REFERENCES tenants ("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_tenant_module_subscriptions_tenant_module
    ON tenant_module_subscriptions ("TenantId", "ModuleName");

-- ---- Seed default tenant + module subscriptions ----
INSERT INTO tenants ("Code", "Name", "IsActive", "BillingPlan", "TimeZone", "Locale", "Currency")
VALUES ('nicktcscan', 'Nick TC-Scan Operations', TRUE, 'internal', 'Africa/Accra', 'en-GH', 'GHS')
ON CONFLICT ("Code") DO NOTHING;

INSERT INTO tenant_module_subscriptions ("TenantId", "ModuleName", "IsEnabled")
SELECT t."Id", m.name, TRUE
FROM tenants t
CROSS JOIN (VALUES ('nscis'), ('nickhr')) AS m(name)
WHERE t."Code" = 'nicktcscan'
ON CONFLICT ("TenantId", "ModuleName") DO NOTHING;

-- Mark this script as applied to the EF migrations history table so EF
-- doesn't try to run its equivalent migration later.
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" VARCHAR(150) NOT NULL,
    "ProductVersion" VARCHAR(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260406145804_InitialCreate', '9.0.0')
ON CONFLICT DO NOTHING;

COMMIT;

\echo
\echo === nick_platform schema applied ===
SELECT "Id", "Code", "Name", "BillingPlan" FROM tenants;
SELECT "TenantId", "ModuleName", "IsEnabled" FROM tenant_module_subscriptions;
