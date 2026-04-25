# Rotate the Cloudflare API token used during Access setup

> ROADMAP `C.1.5`. Always-on; ship as soon as anyone has 5 free
> minutes. The current token is the one I used to create the
> `NickScan Services` Access app + the `api.nickscan.net` DNS
> CNAME. It still has write scope on Zero Trust + DNS for
> `nickscan.net` — anyone who exfiltrated it could
> reconfigure the entire Access policy.

## Why we can't fully automate this

Cloudflare doesn't expose an API endpoint to mint API tokens —
that's deliberately a dashboard-only operation. The rotation flow
is therefore:

1. Create a new token (dashboard).
2. Verify the new token works (CLI).
3. Update any tooling/scripts that reference the old token.
4. Revoke the old token (dashboard).

## Step-by-step

### 1. Create the replacement token

1. Open <https://dash.cloudflare.com/profile/api-tokens>.
2. Click **Create Token** → **Create Custom Token**.
3. Name: `nickscan-platform-ops` (or similar; date-stamping helps:
   `nickscan-platform-ops-2026-04`).
4. **Permissions** — match what we used the original for, no
   broader:
   - Account → Access: Apps and Policies → Edit
   - Account → Access: Organizations, Identity Providers, and Groups → Edit
   - Account → Cloudflare Tunnel → Edit
   - Zone → DNS → Edit
5. **Account Resources:** Include → `Bjforson@gmail.com's Account`
   (the only account this token should touch).
6. **Zone Resources:** Include → Specific zone → `nickscan.net`.
7. **Client IP filtering:** leave default (no filter) unless we
   want to lock to TEST-SERVER's WAN IP — we don't right now,
   keep flexibility.
8. **TTL:** 90 days from today. Forces the next rotation onto the
   calendar; better than letting it live forever.
9. **Continue → Create Token.**
10. **Copy the token immediately.** Cloudflare only shows it once.

### 2. Verify the new token works

```bash
export CF_NEW='<paste the new token>'
curl -sS -H "Authorization: Bearer $CF_NEW" \
  https://api.cloudflare.com/client/v4/user/tokens/verify
# should print {"result":{"id":"...","status":"active"},"success":true,...}
```

### 3. Update consumers

The original token only ever lived in the active terminal of the
operator who ran the Access setup commands; no scripts reference it,
no env var holds it, no service stores it. So step 3 is just:

- [ ] Update any 1Password / Bitwarden record holding it.
- [ ] If a future automation script needs it, plumb via env var
      `CF_API_TOKEN` rather than hardcoding.

### 4. Revoke the old token

1. <https://dash.cloudflare.com/profile/api-tokens>
2. Find `nickscan-access-tunnel` (the original token created
   2026-04-21).
3. **... menu → Roll** (regenerates the same record with a new
   secret) **OR Delete**.
4. Prefer **Delete** — clean slate. Roll is for cases where you
   can't update consumers.

### 5. Sanity check

After revocation, attempt the verify call with the OLD token —
should now return `{"success":false, ...}` with auth error. If it
still works, you didn't revoke; go back to step 4.

## Recurring schedule

Add to the platform calendar: rotate this token every 90 days,
calendar-aligned with our other secret rotations. Document the
last rotation date in `PLATFORM.md` under "Operations / Secrets".

## Related rotations on this server

| Secret | Where stored | Rotation cadence |
|---|---|---|
| `NICKSCAN_DB_PASSWORD` (Postgres `nscim_app`) | machine env var + `pg_hba.conf` | yearly |
| `NICKCOMMS_API_KEY_NICKHR` | machine env var + DB hash | per-incident; see `rotate-nickcomms-key.ps1` |
| `NICKSCAN_JWT_SECRET_KEY` | machine env var | yearly |
| `NICKSCAN_SUPERADMIN_PASSWORD` | machine env var + Identity user | yearly |
| Hubtel merchant credentials (when wired) | machine env var | per Hubtel policy |
| Cloudflare API token (this doc) | operator vault only | 90 days |
