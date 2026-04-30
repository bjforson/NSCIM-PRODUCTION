# NickFinance Security & Auth Audit

Date: 2026-04-28
Scope: `finance/NickFinance.WebApp/` and the NickFinance suite as deployed
on TEST-SERVER (Windows Service `NickFinance_WebApp` on `localhost:5500`,
fronted by Cloudflare Access at `https://finance.nickscan.net`).

This audit is read-only. Severity uses Critical/High/Medium/Low.

---

## Authentication

### Gap: Service connects to Postgres as superuser, contradicting appsettings
- Severity: **Critical**
- Evidence: `scripts/install-nickfinance-service.ps1:38` — `$conn = "Host=localhost;Port=5432;Database=nickhr;Username=postgres;Password=$pw"`. The script writes that into the `ConnectionStrings__Finance` machine env var, which overrides `appsettings.json:11` (`Username=nscim_app`).
- Why it matters: Every SQL the WebApp issues runs as the Postgres superuser. The append-only / balanced-event triggers in `finance/NickFinance.Ledger/SchemaBootstrap.cs` rely on a non-superuser, because superusers bypass triggers in some configurations and can `DROP TRIGGER` directly. A WebApp RCE becomes a full database compromise (drop tables, alter ledger, exfil HR data).
- Recommendation: Edit the install script to use `Username=nscim_app`. Then `REVOKE` superuser powers from `nscim_app`, grant only schema-level CRUD on the `finance` and `nickhr` schemas. Re-run the SchemaBootstrap once with a privileged role to install triggers, then drop privileges.

### Gap: CF Access audience optional; signature-only validation acceptable per code
- Severity: **High**
- Evidence: `Services/CfAccessAuth.cs:82` — `ValidateAudience = !string.IsNullOrWhiteSpace(audience)`. If `NickFinance:CfAccess:Audience` is unset, audience is not validated.
- Why it matters: Any other Cloudflare Access app on the same team domain (e.g. `nickhr.nickscan.net`, `api.nickscan.net`) issues JWTs signed by the same JWKS. Without audience binding, a JWT minted for the *NickHR* app authenticates the holder to *NickFinance*. The XML doc-comment even calls this out: "ops should set this before the app sees real customer data."
- Recommendation: Treat `Audience` as required. Throw on startup when `TeamDomain` is set but `Audience` is not. Audit the production env var now.

### Gap: No replay protection on CF JWTs
- Severity: **Medium**
- Evidence: `Services/CfAccessAuth.cs` — `TokenValidationParameters` checks lifetime + signature only. No nonce / `jti` cache.
- Why it matters: A captured JWT is replayable until `exp`. CF default TTL is 24 h. Combined with the absence of CSP / device binding, a stolen header value gives 24 h of authenticated access from anywhere.
- Recommendation: Use the `cf-access-authenticated-user-email` and `Cf-Connecting-IP` headers as a soft pin: log + alert when the email-from-JWT and the email-from-header diverge. For replay across browsers, document acceptance — CF Access lacks server-side `jti` revocation and that's the threat model trade-off.

### Gap: Anonymous WhatsApp webhook allows no rate limit, no IP allowlist
- Severity: **High**
- Evidence: `Program.cs:251-260` — GET handler is `AllowAnonymous`. `Program.cs:263-342` — POST handler is `AllowAnonymous`, gated only by HMAC over body. No rate limiter, no Meta IP allowlist.
- Why it matters: An attacker without the secret can flood the endpoint to DoS the service or to grind on HMAC oracle / timing. A worse case: if `WHATSAPP_WEBHOOK_SECRET` ever leaks (env var on a LocalSystem service is visible to any admin and to any process running as SYSTEM), a single message flips a Submitted voucher to Approved with no second human in the loop. The phone-resolver currently no-ops, so production won't accept an APPROVE today, but the moment the resolver lands the surface is hot.
- Recommendation: (a) Add ASP.NET rate limiting on `/api/whatsapp/webhook` (e.g. 60/min per IP). (b) Add Meta's documented IP allowlist as a second gate — Meta publishes their egress ranges. (c) Reject before HMAC if the body is larger than ~64 KiB (Meta payloads are small). (d) When the resolver activates, require a second factor (e.g. only approve up to a hard cap; require web-UI for amounts above).

### Gap: Active circuit survives JWT expiry and CF Access revocation
- Severity: **High**
- Evidence: `Program.cs:173-206` — CurrentUser is captured once per Blazor circuit from the auth state provider. The circuit's SignalR socket survives indefinitely; the CF JWT TTL has no effect on it.
- Why it matters: An admin removes a user from the CF Access policy. The user keeps their browser tab open — they continue to approve, disburse, and edit data with the old principal until they refresh. For a finance system this is well below the bar.
- Recommendation: Add a hosted service that periodically calls `CircuitHandler.OnConnectionUpAsync` equivalent — or simpler, re-validate the JWT on every privileged service call by reading `IHttpContextAccessor.HttpContext?.Request.Headers["Cf-Access-Jwt-Assertion"]` from within the service-call path. The Blazor `CircuitHandler` lifecycle gives a hook to forcibly disconnect when the captured email no longer maps to an authenticated user.

### Gap: Dev-user fallback active when CF Access not wired
- Severity: **Medium** (Critical if accidentally enabled in production)
- Evidence: `Services/CfAccessAuth.cs:45-50` — when `TeamDomain` is unset, "auth is skipped and every request is treated as the configured dev user." `Program.cs:200-206` — falls back to `DevUser` config block (defaults to `00000000-0000-0000-0000-000000000001` / `dev@nickscan.com`).
- Why it matters: A botched config push (CF Access env vars wiped) silently downgrades production to "everyone is the dev user," with full write access. There is no fail-closed sentinel.
- Recommendation: When `ASPNETCORE_ENVIRONMENT == "Production"`, throw on startup if `TeamDomain` is null. The XML doc says "intended only for local dotnet run" — encode that as a runtime invariant.

---

## Authorization

### Gap: Zero RBAC — every authenticated user can do everything
- Severity: **Critical**
- Evidence: Grep for `[Authorize` in `finance/` returns one hit, in a markdown design doc (`finance/NickFinance.Ledger/FINANCE_KERNEL.md:358`). Razor pages have no `[Authorize(Policy=...)]`. `CfAccessAuth.cs:94-100` defines a single policy: `RequireAuthenticatedUser`.
- Why it matters: Anyone with an `@nickscan.com` email can: create floats (any custodian), approve their own teammate's voucher (only their *own* is blocked at service level), disburse any voucher they didn't approve, void invoices, post journals, run reports for any tenant. The petty-cash MVP relies on SoD encoded in service code (`PettyCashService.cs:394-397`, `PettyCashDetail.razor:64-66`) — but a malicious user with a second account can submit + approve in two browsers, bypassing it entirely.
- Recommendation: Define roles `Finance.Reader`, `Finance.Approver`, `Finance.Custodian`, `Finance.Admin`. Wire role claims via CF Access groups (CF Access publishes group memberships in JWT `groups` claim). Add `[Authorize(Roles="Finance.Approver")]` on `/approvals`, `Finance.Custodian` on disburse buttons, etc. Until then, even basic damage control: add an env-var allowlist of approver emails and check it in `ApproveVoucherAsync`.

### Gap: Tenant isolation by convention only, not enforced
- Severity: **High**
- Evidence: `PettyCashService.cs:227` — `f.FloatId == req.FloatId && f.TenantId == req.TenantId`. The `TenantId` comes from the *request*, defaulted to `1` (`SubmitVoucherRequest.TenantId = 1`). The Razor pages pass `User.TenantId`, which is read from configuration `NickFinance:DefaultTenantId`. Nothing prevents a future caller (script, webhook, or Page that forgets the filter) from passing a different tenant.
- Why it matters: As soon as tenant 2 onboards, a missed `WHERE TenantId = ?` clause in any new query leaks rows across tenants. Postgres has no row-level-security policy on these tables (`Grep ROW LEVEL SECURITY` in `finance/` returns zero hits).
- Recommendation: Enable Postgres RLS on every business table with `tenant_id` and a `current_setting('nickfinance.tenant_id')::bigint` predicate. Set the GUC at connection check-out. Until then, add EF query filters (`HasQueryFilter(e => e.TenantId == _currentUser.TenantId)`) on every entity — moves enforcement from caller to mapping layer.

### Gap: SoD checks rely on UserId derived from email hash; a name change breaks audit
- Severity: **Medium**
- Evidence: `Services/CfAccessAuth.cs:124-125` — UserId is `SHA-256("nickerp-cfaccess:" + (sub||email).ToLowerInvariant())`. Falls back to email when CF doesn't emit `sub`.
- Why it matters: If `sub` is unstable across CF identity-provider changes (CF Access stitches multiple IdPs by `sub`), the UserId mutates → the same human becomes two users → orphaned audit rows, broken SoD ("the *new* you can approve the *old* you's voucher"), broken Approvals queue (assigned-to a stale GUID).
- Recommendation: Add a real user table (NickERP.Platform.Identity is referenced in the XML doc-comment as the planned destination). Until then, log when the same email produces different UserIds across logins so we can detect the issue early.

### Gap: Custodian phone resolver no-op — disbursement gate effectively absent for WhatsApp
- Severity: Low (because resolver is a no-op today)
- Evidence: `finance/NickFinance.PettyCash/Approvals/IApproverPhoneResolver.cs` — `NoopApproverPhoneResolver` returns null in both directions. The webhook handler at `Program.cs:317-322` calls `ResolveUserIdByPhoneAsync` and ignores unknown phones — current behaviour.
- Why it matters: When the resolver gets wired, any phone number registered (via what mechanism? the doc doesn't say) becomes an approval credential equivalent to a CF JWT. The current architecture has no rate limit per approver, no per-message confirmation, no max-amount cap.
- Recommendation: Define amount caps for WhatsApp-channel approvals (e.g. ≤ GHS 500). Above the cap, force web UI. Bind phone to user via an explicit identity-team workflow with two-person verification when registering.

---

## Data layer

### Gap: Receipts stored unencrypted on disk under LocalSystem
- Severity: **High**
- Evidence: `appsettings.json:16` — `ReceiptStorageRoot: C:\Shared\NSCIM_PRODUCTION\Data\PettyCash\Receipts`. `Receipts/IReceiptStorage.cs` writes raw bytes via `File.WriteAllBytesAsync`. No encryption, no ACL beyond Windows defaults under LocalSystem.
- Why it matters: Receipt blobs frequently contain customer names, prices, ID numbers, mobile money phone numbers (PII under Ghana DPA 2012). The service runs as `LocalSystem`, which means any local admin can read; any backup script that ships them off-machine inherits the exposure.
- Recommendation: Move to S3-compatible object storage (per the doc-comment: "migration to object storage in Phase 5"). At minimum, set NTFS ACLs to `LocalSystem + Administrators` only and document the hash-on-disk integrity story. For DPA, add a deletion API (right to erasure).

### Gap: Customer email/phone stored in clear; no encryption-at-rest
- Severity: **Medium**
- Evidence: `NickFinance.AR/Entities.cs` (Customer) — `Email`, `Phone`, `Tin` all string columns. `appsettings.json` Postgres connection has no `Ssl Mode=Require`.
- Why it matters: A pg_dump leak (e.g. nightly backup misrouted) directly leaks PII. Postgres Transparent Data Encryption is not enabled (no `pg_tde` references in the codebase).
- Recommendation: Enable Postgres SSL for in-transit (the conn string omits `Ssl Mode`). For at-rest, evaluate `pg_tde` or full-disk encryption on the data volume. Mark Email/Phone in DPA records of processing.

### Gap: `nickhr-*.dump` backups land on the same disk as the DB; no off-host copy
- Severity: **High**
- Evidence: `scripts/backup-nickhr-nightly.ps1:78` — defaults to `C:\Backups`. No off-host shipment in the script. Step "Tested restore?" in the goal — no evidence of any restore test.
- Why it matters: A ransomware encrypter wipes both the DB and the backup in one stroke. The 14-day rotation is on the same volume; no off-site copy means RPO is "whatever's on the disk when it dies."
- Recommendation: Add an upload step to a separate object store (B2, R2, or Azure Blob) right after `pg_dump`. Test restore monthly into a `nickhr_dr_test` DB. Document the procedure in `PLATFORM.md`.

### Gap: SchemaBootstrap uses `ExecuteSqlRawAsync` (acceptable here, flagged for awareness)
- Severity: **Low**
- Evidence: `finance/NickFinance.Ledger/SchemaBootstrap.cs:72`.
- Why it matters: The SQL is a static string in source; no user input reaches it. Not an SQLi vector. The risk is future changes adding interpolation.
- Recommendation: Add a `// SAFETY:` comment + a unit test that asserts no `string.Format`/interpolation enters the SchemaBootstrap method.

---

## HTTP / network

### Gap: No security response headers
- Severity: **High**
- Evidence: `Program.cs` — only `UseHsts()` (production-only) and `UseAntiforgery()`. No `Content-Security-Policy`, `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`, `X-Frame-Options`. Grep for `UseSecurityHeaders` returns no matches.
- Why it matters: Blazor Server is XSS-resistant by design but receipt thumbnails / OCR output are user-controlled. Without CSP a successful injection becomes a full DOM takeover. `X-Frame-Options` absence allows clickjacking that cookie-jacks the SignalR session.
- Recommendation: Add `NetEscapades.AspNetCore.SecurityHeaders` (or hand-rolled middleware). Minimum: `Content-Security-Policy: default-src 'self'; script-src 'self' 'unsafe-eval'; img-src 'self' data:; frame-ancestors 'none'`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: same-origin`.

### Gap: No rate limiting anywhere in the app
- Severity: **High**
- Evidence: `Grep "UseRateLimiting"` in `finance/` — zero hits.
- Why it matters: A misbehaving (or hostile) approver can hammer the approve/disburse path; the WhatsApp webhook is unbounded (above); the Cloudflare edge does not currently rate-limit in front of `finance.nickscan.net` (no `cloudflare-ratelimit` references). At the service layer we have nothing.
- Recommendation: Wire `AddRateLimiter` with a fixed-window per-IP limiter on auth-anonymous endpoints (10/min) and a per-user limiter on mutating endpoints (60/min).

### Gap: Antiforgery applies to MVC/Blazor pages, not to the JSON webhook
- Severity: **Low** (handler is correctly anonymous + HMAC-gated)
- Evidence: `Program.cs:223` — `app.UseAntiforgery()` is registered, and Blazor's `@rendermode InteractiveServer` enforces the anti-forgery token by default. The webhook explicitly bypasses with `AllowAnonymous` and validates HMAC instead.
- Why it matters: The model is correct, but worth recording.
- Recommendation: Add a unit test that POST to `/api/whatsapp/webhook` without HMAC returns 401 in CI.

### Gap: ExceptionHandler points to `/Error` page that doesn't exist
- Severity: **Medium**
- Evidence: `Program.cs:218` — `app.UseExceptionHandler("/Error", createScopeForErrors: true)`. No file at `Components/Pages/Error.razor`.
- Why it matters: Production exceptions return 404 (the Error route 404s) followed by Blazor's default error wall — which historically can leak partial stack traces in development mode and spew SignalR reconnection loops in production. Worse, the exception itself isn't caught by anything else, so the request just dies oddly.
- Recommendation: Add a minimal `Error.razor` at the route `/Error` that displays a generic message + correlation id for support. Verify in production with a forced 500.

---

## Audit & observability

### Gap: No security audit log
- Severity: **High**
- Evidence: Business audit columns exist on entities (`CreatedAt`, `CreatedByUserId`, `DecidedAt`, etc.) but there is no separate audit table for auth events, role changes, failed logins, or admin actions. Grep for `audit_log`, `security_event` returns nothing in finance/.
- Why it matters: Forensics after a breach requires "who logged in, when, from where, what did they touch." Today CF Access logs the authentication; the app logs business state changes; nothing connects them with correlation ids — i.e. you can know "Sam logged in at 10:00" and "voucher PC-… approved by Sam at 10:05" but not stitched without manual correlation.
- Recommendation: Create `finance.security_audit_log` (event_type, actor_user_id, ip, ua, resource, outcome, correlation_id, created_at). Write on every: login, role grant/revoke, voucher approve/reject/disburse, ledger reverse, period close. Retain ≥ 6 years (Companies Act).

### Gap: Connection string + JWTs may leak via default ASP.NET error logging
- Severity: **Medium**
- Evidence: `appsettings.json:5` — Logging defaults `Microsoft.AspNetCore: Warning`, `EFCore.Database.Command: Warning`. No log scrubber. Serilog config (in `NickERP.Platform.Observability`) — needs separate review for any property destructuring of HttpContext.
- Why it matters: A Postgres connection error throws an exception that Npgsql formats *with* the connection string (password masked by Npgsql since v6). JWT inspection middleware can dump claims. If logs ship to a log aggregator, secrets and PII follow.
- Recommendation: Add a Serilog enricher that scrubs `Authorization`, `Cf-Access-Jwt-Assertion`, `password=`, `Password=`, `PGPASSWORD`. Test by running `Stop-Service postgresql` and inspecting the resulting log line.

---

## Secrets

### Gap: All secrets at machine env-var scope; visible to any LocalSystem service
- Severity: **High**
- Evidence: `scripts/install-nickfinance-service.ps1:33-46`, `scripts/setup-environment-variables.ps1`. `rotate-cf-api-token.md:90-95` table.
- Why it matters: Any process running as SYSTEM or under any LocalSystem service can read `NICKSCAN_DB_PASSWORD`, `NICKSCAN_JWT_SECRET_KEY`, `WHATSAPP_WEBHOOK_SECRET`. A foothold in any of the 5 LocalSystem services on the box (NSCIM_API, NickHR, NickFinance, NickComms, NSCIM_ImageDownloader if it exists) compromises all of them.
- Recommendation: Migrate secrets to Windows DPAPI (per-machine, decryption requires running on the same box) and load via a small `IConfigurationProvider`. Rotate `NICKSCAN_JWT_SECRET_KEY` annually as the rotate-cf doc claims, but no script automates it — write `rotate-jwt-secret.ps1`.

### Gap: Webhook secrets are placeholders; failure-closed but un-monitored
- Severity: **Medium**
- Evidence: `Program.cs:270-274` — when `WHATSAPP_WEBHOOK_SECRET` is empty, returns 503 with a warning. Good. But: no metric / alert fires.
- Why it matters: A misconfigured deploy silently breaks WhatsApp approvals. Operators will discover via complaint, not monitoring.
- Recommendation: On startup, check the env var — if missing, increment a Prometheus gauge `nickfinance_webhook_secret_unset 1` and emit a startup-WARN log line. Alert on it.

---

## Operational

### Gap: Service runs as LocalSystem; over-privileged
- Severity: **High**
- Evidence: `scripts/install-nickfinance-service.ps1` — sc.exe create with default account = LocalSystem. The script comment confirms "LocalSystem account" is the pattern.
- Why it matters: An RCE in the WebApp gives SYSTEM on the box. SYSTEM can read all env vars (above), tamper with the Postgres data dir, install a service rootkit. A non-privileged service account would contain blast radius.
- Recommendation: Create a dedicated `nickfinance$` Windows local user (or a gMSA), grant only "Log on as a service," `read+execute` on the publish dir, and `read+write` on `C:\Shared\NSCIM_PRODUCTION\Data\PettyCash\Receipts`. Remove broad rights.

### Gap: No documented disaster recovery runbook
- Severity: **Medium**
- Evidence: `scripts/rotate-postgres-password.md` exists; nothing equivalent for "the box is gone, rebuild from scratch."
- Recommendation: Write `docs/runbooks/dr-rebuild.md` covering: VM image baseline, env var reconstruction, secret restoration from operator vault, pg_restore, service install order, smoke tests.

---

## Identity model gaps

### Gap: No path to deactivate a user without going through CF Access
- Severity: **Medium**
- Evidence: `Services/CurrentUser.cs` — pure in-memory, no DB-backed user table. Entire identity is "if CF Access let you in, you're real."
- Why it matters: CF Access removal kicks in on the next JWT issuance (24 h default). A faster local-veto requires the planned `NickERP.Platform.Identity` to land. Today you wait for the next refresh.
- Recommendation: As the Identity track lands, add a `users` table with an `is_active` flag and check it in the CurrentUser scope. CF Access removal becomes a backstop, not the primary control.

### Gap: No delegation model for approvers on leave
- Severity: **Low** (operationally annoying, not a security issue per se)
- Evidence: `finance/NickFinance.PettyCash/Approvals/Delegation.cs` — file exists; check whether wired. (Out of audit scope — note as a follow-up.)
- Recommendation: Verify delegation engine is reachable from web UI; if not, either wire it or document the manual escalation procedure.

---

## Compliance

### Gap: Ghana DPA 2012 — no formal record of processing or DSAR endpoint
- Severity: **Medium**
- Evidence: PII fields (Email, Phone, Tin, GPS on receipts) collected without a documented retention/deletion path.
- Recommendation: Draft a one-page DPA summary covering: data categories, lawful basis, retention (link Companies-Act 6 years for invoices; longer if tax-disputed), erasure flow.

### Gap: Companies Act retention — verify 6-year rule encoded
- Severity: Low (likely compliant by default, just unverified)
- Evidence: No automated archival/deletion job. `LedgerEvent` is append-only — that's the *guarantee* side; we just lack a *when do we delete* policy.
- Recommendation: Add a documented retention schedule per entity (ledger 7+ yr, AR receipts 7+ yr, voucher receipts 6+ yr).

---

## Prioritized fix list (top items, ordered)

| # | Gap | Severity | Effort |
|---|-----|----------|--------|
| 1 | Connection string uses `postgres` superuser (install script) | Critical | 0.5 day — edit script + create `nscim_app` if needed + redeploy + smoke |
| 2 | Zero RBAC; everyone is admin | Critical | 3 days — define roles, wire CF Access groups, add `[Authorize]` on every page + service-side guards |
| 3 | CF Access audience optional; force on production startup | High | 2 hours — startup invariant + verify env var + redeploy |
| 4 | Tenant isolation by convention only; enable EF query filters or Postgres RLS | High | 2 days — query filters across all DbContexts + tests |
| 5 | Active circuit survives JWT expiry / CF revocation | High | 1 day — CircuitHandler hook + periodic re-check |
| 6 | WhatsApp webhook: rate limit + IP allowlist + max-amount cap | High | 1 day — `AddRateLimiter` + Meta IP gate + amount cap config |
| 7 | Service runs as LocalSystem; demote to gMSA / local service user | High | 1 day — create account, ACL the receipts dir, redeploy, smoke |
| 8 | Off-host backup + tested restore | High | 1 day — add B2/R2 upload step, schedule monthly restore drill |
| 9 | Security headers (CSP, X-Frame-Options, etc.) | High | 0.5 day — add `NetEscapades.AspNetCore.SecurityHeaders` |
| 10 | Receipts encrypted on disk (or migrated to object storage) | High | 1-2 days — Phase 5 plan exists, accelerate |
| 11 | Security audit log table + emitters | High | 1 day — schema + middleware + service hooks |
| 12 | Add `/Error` page; verify production exception path | Medium | 2 hours |
| 13 | Connection string scrubbing in logs | Medium | 4 hours — Serilog enricher + test |
| 14 | Dev-user fallback fail-closed in production | Medium | 1 hour — startup invariant |
| 15 | Add JWT-secret rotation script + 90-day calendar item | Medium | 2 hours |
