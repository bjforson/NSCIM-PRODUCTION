# Postgres password rotation runbook

Rotates the password for Postgres role `nscim_app` (the non-superuser
role all 5 services use to talk to Postgres) and the matching
`NICKSCAN_DB_PASSWORD` machine-scope env var.

## When to run

- Routine hygiene (recommended every 90 days).
- After any suspected leak (backup file, log dump, env-var dump in CI).
- Before handing over the box to a new operator.

## Prerequisites

1. **Maintenance window.** All 5 services restart during the rotation;
   expect ~30–60 s of unavailability per service.
2. **Run as Administrator** (env-var write at Machine scope + `sc.exe`
   need it).
3. **Current `NICKSCAN_DB_PASSWORD` env var must be correct** — the
   script uses it to authenticate the `ALTER USER` call. A stale value
   means the very first step fails before anything is changed.
4. **All 5 services running and healthy before rotation** — otherwise
   you can't tell if a post-rotation failure is rotation-caused or
   pre-existing.

## Dry run first

```powershell
# from repo root, elevated:
pwsh -File scripts/rotate-postgres-password.ps1
```

Without `-Apply`, the script:

- Probes Postgres connectivity with the current password.
- Generates a new 32-byte URL-safe random password (so you can see what
  it would do).
- Writes a rollback file to `$env:TEMP` containing the **old** password.
- Prints the planned step-by-step.
- Exits without touching anything.

If the dry run passes, schedule the maintenance window.

## Apply

```powershell
pwsh -File scripts/rotate-postgres-password.ps1 -Apply
```

The script then runs in this order, with brief pauses between phases to
let services transition cleanly:

| # | Step | Failure mode |
|---|---|---|
| 1 | `ALTER USER nscim_app WITH PASSWORD '<new>'` | Old password rejected; rollback via the rollback file |
| 2 | Set `NICKSCAN_DB_PASSWORD` machine-scope | Should never fail under elevated shell |
| 3 | Stop NickHR_WebApp + NSCIM_WebApp | Stop hangs are usually harmless |
| 4 | Stop NSCIM_NickComms + NickHR_API + NSCIM_API | Same |
| 5 | Start NSCIM_API + NickHR_API + NSCIM_NickComms (APIs first) | Will fail if env var didn't propagate to the new process — restart again from a fresh shell |
| 6 | Start NSCIM_WebApp + NickHR_WebApp | Same |
| 7 | Health-probe each service | Surface the failed one in red |

## Health probes

The script hits these endpoints with a 5 s timeout:

- `http://localhost:5205/api/health` — NSCIM_API
- `http://localhost:5215/api/_module/manifest` — NickHR_API
- `http://localhost:5220/api/health` — NickComms

(NSCIM_WebApp + NickHR_WebApp don't have public health endpoints; their
state is implied by whether the API behind them is healthy and the
service is `Running` in `sc query`.)

## Rollback

The script writes the old password to a temp file at the start
(filename printed). If anything fails:

1. `psql -U postgres -d postgres -c "ALTER USER nscim_app WITH PASSWORD '<old>';"`
2. `[Environment]::SetEnvironmentVariable('NICKSCAN_DB_PASSWORD', '<old>', 'Machine')`
3. Restart all 5 services again.
4. Delete the temp file.

After a **successful** rotation, **delete the temp file immediately**
— it contains the now-revoked but still-recently-valid password and is
a small leak risk.

## What the rotation doesn't cover

- The `postgres` superuser password (separate concern; rotate via the
  PG admin tooling and the OS-level scheduled-tasks credential store).
- Any per-service-app secrets (`NICKHR_JWT_KEY`, `NICKSCAN_JWT_SECRET_KEY`,
  `NICKSCAN_IMAGE_SIGNING_KEY`, `NICKCOMMS_API_KEY_NICKHR`) — those have
  their own rotation paths.
- The `nick_platform` DB connection string (if any service uses a
  different role there).

If a future module ever introduces a new DB role beyond `nscim_app`, add
the corresponding `ALTER USER` line + env-var update to the script.
