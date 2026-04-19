# Phase 1 — Multi-tenancy migrations

> **Status:** Generated and reviewed in source. **NOT YET RUN against any database.**
>
> These migrations are designed to be run manually during a maintenance window
> by the operator (you), not auto-applied by EF Core. Run them in the order listed.

## What these scripts do

Each module database (`nickscan_production`, `nickhr`, `nick_comms`) gets:

1. **`tenant_id BIGINT NOT NULL DEFAULT 1`** added to every business table
2. **Composite indexes** on `(tenant_id, primary_key)` for hot-query performance
3. **PostgreSQL Row-Level Security (RLS)** enabled with a policy that filters by
   `current_setting('app.tenant_id', true)::bigint`. The platform tenancy library
   sets this session-local variable on every connection from the
   `TenantOwnedEntityInterceptor`.
4. **Backfill** — every existing row is set to tenant id 1 ("Nick TC-Scan Operations")

## Why raw SQL instead of EF Core migrations

- 177 tables across 3 databases. Adding `ITenantOwned` to every entity class
  would mean touching ~177 .cs files and risking breakage in unrelated code.
- Raw SQL is auditable in a single PR and can be reviewed by an operator
  before execution.
- The C# entity changes happen incrementally in later phases as each module is
  promoted to "tenant-aware" status. Until then, the columns exist at the DB
  level and RLS enforces isolation even if EF Core doesn't know about it.

## Pre-flight checklist

- [ ] All five Windows services stopped:
  - `net stop NSCIM_WebApp && net stop NSCIM_API`
  - `net stop NickHR_WebApp && net stop NickHR_API`
  - `net stop NickComms_Gateway`
- [ ] Backup taken: `pg_dump nickscan_production`, `pg_dump nickhr`, `pg_dump nick_comms`
- [ ] Maintenance window confirmed (~15 min downtime)
- [ ] Rollback script reviewed (`99-rollback-all.sql`)

## Execution order

```powershell
$env:PGPASSWORD = $env:NICKSCAN_DB_PASSWORD
$psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
$dir  = "C:\Shared\NSCIM_PRODUCTION\tools\migrations\phase1-tenancy"

# 1. Create the canonical platform DB and seed default tenant
& $psql -h localhost -U postgres -f "$dir\00-create-nick-platform-db.sql"
& $psql -h localhost -U postgres -d nick_platform -f "$dir\01-nick-platform-schema.sql"

# 2. Add tenant_id columns to each module DB (run in any order)
& $psql -h localhost -U postgres -d nickscan_production -f "$dir\10-nickscan-production-add-tenant-id.sql"
& $psql -h localhost -U postgres -d nickhr             -f "$dir\11-nickhr-add-tenant-id.sql"
& $psql -h localhost -U postgres -d nick_comms         -f "$dir\12-nick-comms-add-tenant-id.sql"

# 3. Enable Row-Level Security policies
& $psql -h localhost -U postgres -d nickscan_production -f "$dir\20-nickscan-production-rls.sql"
& $psql -h localhost -U postgres -d nickhr             -f "$dir\21-nickhr-rls.sql"
& $psql -h localhost -U postgres -d nick_comms         -f "$dir\22-nick-comms-rls.sql"

# 4. Verify
& $psql -h localhost -U postgres -d nickscan_production -f "$dir\90-verify.sql"
```

## Post-migration verification

The `90-verify.sql` script checks:

- Every table has a `tenant_id` column
- Every row has `tenant_id = 1`
- RLS is enabled on every table
- The `app.tenant_id` setting is honored

## Rollback

`99-rollback-all.sql` reverses everything: drops the `tenant_id` columns,
removes the RLS policies, drops the `nick_platform` database. Test this
against a copy first.

## After migration

Run the apps. They will continue to work because:

- The `TenantOwnedEntityInterceptor` (Phase 1d) sets `app.tenant_id = 1` on
  every connection via a session command before queries run
- Rows stay scoped to tenant 1 by RLS — no application code needed for the
  default-tenant case
- The middleware reads the JWT `tenant_id` claim and updates the session
  variable for multi-tenant scenarios (Phase 2 onwards)

## Files in this directory

| File | Purpose |
|---|---|
| `README.md` | This file |
| `00-create-nick-platform-db.sql` | `CREATE DATABASE nick_platform` |
| `01-nick-platform-schema.sql` | The `tenants`, `tenant_users`, `tenant_module_subscriptions` tables (mirrors the EF Core migration in `platform/NickERP.Platform.Tenancy.Database/Migrations/`) |
| `10-nickscan-production-add-tenant-id.sql` | Generated; adds `tenant_id` to all 75 tables in `nickscan_production` |
| `11-nickhr-add-tenant-id.sql` | Generated; adds `tenant_id` to all 97 tables in `nickhr` |
| `12-nick-comms-add-tenant-id.sql` | Generated; adds `tenant_id` to the 5 tables in `nick_comms` |
| `20-nickscan-production-rls.sql` | Enables RLS + creates per-table policies for `nickscan_production` |
| `21-nickhr-rls.sql` | Same for `nickhr` |
| `22-nick-comms-rls.sql` | Same for `nick_comms` |
| `90-verify.sql` | Post-migration sanity checks |
| `99-rollback-all.sql` | Full rollback script |
| `generate-tenant-migrations.ps1` | The PowerShell generator that produced the 10/11/12 and 20/21/22 SQL files (rerun if schema changes) |
