# NickERP.Platform.Identity

> Status: A.2.1–A.2.7 (read-side) shipped. Admin REST API (A.2.8),
> demo app (A.2.9), and finalised contract docs (A.2.10) open.
>
> See `ROADMAP.md §A.2` for the full task list.

---

## What this layer does

Owns the canonical identity of every actor in the suite — humans and
service callers alike — and exposes one resolver every NickERP service
consumes:

```csharp
public interface IIdentityResolver
{
    Task<ResolvedIdentity?> ResolveAsync(HttpContext ctx, CancellationToken ct = default);
}
```

`ResolveAsync` walks three sources in order:

1. `Cf-Access-Jwt-Assertion` header → human user (validated against
   CF JWKS, mapped/auto-provisioned to `IdentityUser`).
2. `Cf-Access-Client-Id` header → machine caller (looked up in
   `service_tokens.token_client_id`).
3. In Development only and only when `Identity:CfAccess:AllowDevBypass=true`:
   `X-Dev-User: someone@nickscan.com` → fake human, identical
   downstream behaviour including auto-provision.

Returns a `ResolvedIdentity` with the canonical id, email/display name,
tenant, kind, and active scope set. App code never parses the JWT.

## Domain model

| Entity | Role |
|---|---|
| `IdentityUser` | One row per real human. Email is the natural key. Soft-delete via `IsActive=false`. Auto-created on first valid JWT. |
| `AppScope` | A named permission registered by an app at install time, e.g. `Finance.PettyCash.Approver`. |
| `UserScope` | Link-table grant. Time-boundable (`ExpiresAt`), revocable (`RevokedAt`+`RevokedByUserId`), never deleted (audit retention). |
| `ServiceTokenIdentity` (+ `ServiceTokenScope`) | Non-human caller. Cloudflare Access service-token client id is the natural key. Has its own scope set; never collapses into a user. |

Schema: `identity` inside DB `nickerp_platform`. Migrations under
`Migrations/`; first one is `20260425205522_InitialIdentity`.

## Open contract questions

- [x] **JWKS caching policy** — answered in `Auth/CfJwksFetcher.cs`.
      Refresh every 1 hour; serve stale-but-recent (≤24 h) on transient
      fetch failure; `SemaphoreSlim` gate so only one refresh per
      consumer at a time.
- [x] **First-login auto-provisioning** — answered in
      `Resolver/IdentityResolver.cs`. Unknown email arriving with a
      validated CF JWT auto-creates an `IdentityUser` with **zero
      scopes** (Access already vouched for the email; we trust it).
      An admin grants scopes after.
- [ ] **Multi-email aliasing** — does `angela@nickscan.com` and
      `angela.ayanful@nickscan.com` ever resolve to the same user?
      Deferred until a real case forces it.
- [ ] **Tenant resolution from JWT** — Cloudflare Access doesn't carry
      tenant. Single-tenant for v1; revisit when Phase A.3 tenant
      bootstrap lands.

## Consumers

Apps wire this in via `AddNickErpIdentity(config)`:

```csharp
// Program.cs of any NickERP service
builder.Services.AddNickErpIdentity(builder.Configuration);
```

with config like:

```jsonc
{
  "ConnectionStrings": {
    "NickErpPlatform": "Host=localhost;Port=5432;Database=nickerp_platform;Username=...;Password=..."
  },
  "Identity": {
    "CfAccess": {
      "TeamDomain": "nickscan.cloudflareaccess.com",
      "Audience":   "529763cb8a01addfc0c75cccce3844f46c345bd2fedc5304815902c23ffdbc46",
      "AllowDevBypass": false
    }
  }
}
```

Then in any endpoint:

```csharp
app.MapGet("/api/secret", async (HttpContext ctx, IIdentityResolver resolver) =>
{
    var who = await resolver.ResolveAsync(ctx);
    if (who is null) return Results.Unauthorized();
    if (!who.HasScope("Finance.PettyCash.Approver")) return Results.Forbid();
    return Results.Ok($"Hello {who.DisplayName}");
});
```

No JWT parsing. No PBKDF2. No `[Authorize(Roles=...)]`. The resolver
owns it all.

## Header reference

| Header | Source | What it means |
|---|---|---|
| `Cf-Access-Jwt-Assertion` | Cloudflare Access edge | Signed JWT for an authenticated human. Validated against CF's JWKS, then mapped to an `IdentityUser` (auto-created on first sight). |
| `Cf-Access-Client-Id` | Cloudflare Access service token | Machine caller. Looked up against `service_tokens.token_client_id`. The matching `client-secret` is enforced by Access at the edge — we don't see it. |
| `X-Dev-User` | local dev only | Fake email; only honoured when `Identity:CfAccess:AllowDevBypass=true` AND `ASPNETCORE_ENVIRONMENT=Development`. Behaves identically to a real human path including auto-provisioning. |

If none of the three resolve cleanly, the resolver returns `null` and
the app returns 401.

## Module layout

```
NickERP.Platform.Identity/
├── Entities/
│   ├── IdentityUser.cs
│   ├── AppScope.cs
│   ├── UserScope.cs
│   └── ServiceTokenIdentity.cs   (+ ServiceTokenScope inside)
├── Auth/
│   ├── CfAccessOptions.cs        — config object
│   ├── CfJwksFetcher.cs          — cached JWKS retrieval
│   └── CfAccessJwtValidator.cs   — JwtSecurityTokenHandler wrapper
├── Resolver/
│   ├── ResolvedIdentity.cs       — what apps see
│   └── IdentityResolver.cs       — resolves principal -> canonical user/service token
├── Migrations/
│   ├── 20260425205522_InitialIdentity.cs
│   ├── 20260425205522_InitialIdentity.Designer.cs
│   └── IdentityDbContextModelSnapshot.cs
├── IdentityDbContext.cs          (+ design-time factory)
├── IdentityServiceCollectionExtensions.cs   (AddNickErpIdentity)
└── IDENTITY.md                   (this file)
```

## What's still open

- **A.2.7 service-token write-side / admin** — the resolver reads
  service-token rows but there's no API for an admin to create or
  rotate them yet. Currently you'd seed by direct SQL.
- **A.2.8 Identity admin REST API** — `POST /api/identity/users`,
  `PATCH .../scopes`, `DELETE` (soft) — none shipped.
- **A.2.9 Demo app** at `platform/demos/identity/` — proves the
  whole stack end-to-end behind real CF Access, with admin
  round-trip working.
- **A.2.10 Final docs polish** — once A.2.9 is green, walk this doc
  through the "could a new engineer wire a new app without asking"
  test and lock it.

## Out of scope

- Password storage. We don't have passwords; CF Access does email
  OTP (today) or SSO/SAML (later). The platform never sees a password.
- Group / role hierarchy beyond a flat scope list. If we need
  hierarchies, build the resolver that flattens them on read; don't
  put hierarchy in the schema.
- Per-record authorisation. That's app-level; this layer says
  "Angela has scope X", not "Angela can read row Y".

## Related docs

- `ROADMAP.md §A.2` — task list and acceptance criteria
- `platform/NickERP.Portal/SSO.md` — the original Option-A-now,
  Option-D-later decision that this layer enables
