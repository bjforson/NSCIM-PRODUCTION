# NickERP Portal — SSO decision log

## Context

The portal at `erp.nickscan.net` is the landing page for the NickERP suite.
Users arrive at the portal, click an app card, and navigate to the sub-app
(NickHR, NSCIM, …). Two auth layers are already in play:

1. **Cloudflare Access** (edge) — email-OTP gate for `@nickscan.com` on
   `hr.nickscan.net`, `scan.nickscan.net`, `lan.nickscan.net`,
   `api.nickscan.net`, and (once deployed) `erp.nickscan.net`. All five
   hostnames are **the same Access application** so one Access login covers
   the whole suite for the session duration (24h).
2. **App-level login** — NickHR and NSCIM each have their own user store
   and login form behind Access. A user visiting a sub-app still has to
   sign in there even after clearing Access.

## Options that were on the table

| ID | Model | Pros | Cons |
|---|---|---|---|
| **A** | Portal links only, each app keeps its own login | Zero code changes, ship immediately | Users re-login at each app the first time; sessions are long so it's mostly one-time |
| **B** | Shared cookie across `.nickscan.net` | One login at any app covers all | Per-app config change for cookie domain + name; risk of session bleed between apps |
| **C** | Portal mints a JWT, apps trust it | True SSO | Every sub-app needs JWT-acceptance code + signing-key sharing |
| **D** | Sub-apps trust Cloudflare Access `CF-Access-Jwt-Assertion` header | CF is the identity provider; sub-app login pages can be removed | Code change per app: validate JWT, map email → internal user, bypass login form |

## Decision — v1 = Option A

For the v1 rollout of the portal:

- **No code changes to sub-apps.**
- Portal itself requires no login — Cloudflare Access is the gate.
- Clicking a card in the portal opens the sub-app in the same tab; the
  user then signs in with their sub-app credentials if their sub-app
  session isn't already active.

**Why A now:** the Cloudflare Access session length is 24h, and the
NickHR / NSCIM session cookies themselves last long enough that day-to-day
friction is minimal. Zero risk of breaking either app.

## Target — Option D (deferred)

Long-term, D is the right endgame. Rationale:

- **One login, everywhere.** User signs in once via Access (email OTP for
  now, SSO provider later). Sub-apps auto-sign-in the user from the
  `CF-Access-Jwt-Assertion` header. No extra login pages.
- **One identity source of truth.** The email on the CF-Access JWT
  becomes the canonical user identifier. Sub-apps map that to their
  internal user record; if no match, they can auto-provision (or deny).
- **Stronger auth.** CF Access supports MFA, device posture, geo-policies,
  and IdP federation (Google Workspace, M365, Okta, SAML). Any hardening
  we pick up at Access applies to every app without code.
- **Lower attack surface.** The `[AllowAnonymous]` audit we deferred
  matters less once Access is the gate and app logins are gone entirely
  (no public login pages to rate-limit or lock out).

## What D will involve (rough plan)

1. Add `Microsoft.AspNetCore.Authentication.JwtBearer` middleware to
   NickHR + NSCIM APIs that validates the `CF-Access-Jwt-Assertion`
   header against CF's JWKS endpoint (well-known keys).
2. Auto-sign-in middleware: on first authenticated request, look up the
   user by email; if not found, either auto-provision a minimal user
   record or return a 403 with a "request access" page.
3. Delete the NickHR login page and the NSCIM login flow — unreachable
   behind Access anyway.
4. Service-to-service: machine clients use Cloudflare Access **service
   tokens** (client_id + client_secret) instead of browser cookies.
5. Local dev: Access can't intercept `localhost`. Add a dev-only
   middleware that injects a fake JWT when `ASPNETCORE_ENVIRONMENT ==
   Development`.

## Migration safety

- We won't remove the existing login pages until D is live and tested.
- During transition, both auth paths can coexist: if a CF-Access JWT is
  present, trust it; otherwise fall through to the current app login.
- Rollback = remove the middleware; app logins still work.

## Triggers for revisiting

Move to D when:
- A second or third app joins the suite (paying the D cost once benefits N apps)
- Users complain about having to log in multiple times
- We want to roll out real MFA without touching every app
- We want to federate against an external IdP (Google/M365)
