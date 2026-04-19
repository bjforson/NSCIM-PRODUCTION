CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE tenants (
    "Id" bigint GENERATED ALWAYS AS IDENTITY,
    "Code" character varying(50) NOT NULL,
    "Name" character varying(200) NOT NULL,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "BillingPlan" character varying(50) NOT NULL DEFAULT 'internal',
    "TimeZone" character varying(50) NOT NULL DEFAULT 'Africa/Accra',
    "Locale" character varying(20) NOT NULL DEFAULT 'en-GH',
    "Currency" character varying(3) NOT NULL DEFAULT 'GHS',
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_tenants" PRIMARY KEY ("Id")
);

CREATE TABLE tenant_module_subscriptions (
    "Id" bigint GENERATED ALWAYS AS IDENTITY,
    "TenantId" bigint NOT NULL,
    "ModuleName" character varying(50) NOT NULL,
    "IsEnabled" boolean NOT NULL DEFAULT TRUE,
    "ExpiresAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_tenant_module_subscriptions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_tenant_module_subscriptions_tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES tenants ("Id") ON DELETE CASCADE
);

CREATE TABLE tenant_users (
    "Id" bigint GENERATED ALWAYS AS IDENTITY,
    "TenantId" bigint NOT NULL,
    "UserId" character varying(100) NOT NULL,
    "Username" character varying(100),
    "IsPrimary" boolean NOT NULL DEFAULT TRUE,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_tenant_users" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_tenant_users_tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES tenants ("Id") ON DELETE RESTRICT
);

INSERT INTO tenants ("Id", "BillingPlan", "Code", "CreatedAt", "Currency", "IsActive", "Locale", "Name", "TimeZone", "UpdatedAt")
OVERRIDING SYSTEM VALUE
VALUES (1, 'internal', 'nicktcscan', TIMESTAMPTZ '2026-04-06T00:00:00Z', 'GHS', TRUE, 'en-GH', 'Nick TC-Scan Operations', 'Africa/Accra', NULL);

INSERT INTO tenant_module_subscriptions ("Id", "CreatedAt", "ExpiresAt", "IsEnabled", "ModuleName", "TenantId", "UpdatedAt")
OVERRIDING SYSTEM VALUE
VALUES (1, TIMESTAMPTZ '2026-04-06T00:00:00Z', NULL, TRUE, 'nscis', 1, NULL);
INSERT INTO tenant_module_subscriptions ("Id", "CreatedAt", "ExpiresAt", "IsEnabled", "ModuleName", "TenantId", "UpdatedAt")
OVERRIDING SYSTEM VALUE
VALUES (2, TIMESTAMPTZ '2026-04-06T00:00:00Z', NULL, TRUE, 'nickhr', 1, NULL);

CREATE UNIQUE INDEX "IX_tenant_module_subscriptions_TenantId_ModuleName" ON tenant_module_subscriptions ("TenantId", "ModuleName");

CREATE UNIQUE INDEX "IX_tenant_users_TenantId_UserId" ON tenant_users ("TenantId", "UserId");

CREATE INDEX "IX_tenant_users_UserId" ON tenant_users ("UserId");

CREATE UNIQUE INDEX "IX_tenants_Code" ON tenants ("Code");

SELECT setval(
    pg_get_serial_sequence('tenants', 'Id'),
    GREATEST(
        (SELECT MAX("Id") FROM tenants) + 1,
        nextval(pg_get_serial_sequence('tenants', 'Id'))),
    false);
SELECT setval(
    pg_get_serial_sequence('tenant_module_subscriptions', 'Id'),
    GREATEST(
        (SELECT MAX("Id") FROM tenant_module_subscriptions) + 1,
        nextval(pg_get_serial_sequence('tenant_module_subscriptions', 'Id'))),
    false);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260406145804_InitialCreate', '9.0.0');

COMMIT;

