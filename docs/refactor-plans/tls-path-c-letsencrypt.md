# TLS Path C: Real DNS Name + Let's Encrypt

**Status:** PLAN ONLY — Path A (mkcert) is in place today as the interim fix.
**Target:** Move NSCIM apps off `https://10.0.1.254:5xxx` (private IP, locally-trusted-only mkcert cert) to `https://<name>.nickscan.com:5xxx` (real DNS name, publicly-trusted Let's Encrypt cert).

**Why bother:** Path A requires per-machine root CA install. New users, contractors, and mobile devices each need that step. Path C is universal — works on any browser, any device, no trust-store fiddling.

---

## Prereqs (must all be true before starting)

1. **You own a public DNS domain** — we have `nickscan.com` (referenced in `Program.cs:473`). ✅
2. **You can edit DNS records for that domain** — either a registrar dashboard (GoDaddy/Cloudflare/etc.) or an API token. Required for the DNS-01 challenge.
3. **Server can reach the internet outbound on 443** — to talk to Let's Encrypt's ACME endpoint.
4. **You can serve internal DNS for the chosen subdomain** — internal users need `nscim.nickscan.com` (or whatever) to resolve to `10.0.1.254`. Either:
   - Add an A record to `nickscan.com`'s public DNS pointing to `10.0.1.254` (works but exposes the internal IP publicly via DNS — usually fine, but confirm policy)
   - OR add a "split-horizon" entry to your internal DNS resolver (Active Directory DNS / pi-hole / Unbound)
5. **No port-80 inbound required** — using DNS-01 challenge, not HTTP-01.

---

## Pick a name

| Option | Pros | Cons |
|---|---|---|
| `nscim.nickscan.com` | Short, obvious | Slight info leak (the name says "nscim" exists) |
| `portal.nickscan.com` | Generic | Same |
| `cargo.nickscan.com` | Customer-facing | Same |
| `internal-nscim.nickscan.com` | Signals "not for the public" | Longer to type |

Recommendation: **`nscim.nickscan.com`** — short, accurate. If concerned about info leak, the wildcard option below covers it.

If you'll add more internal apps later, get a wildcard up front:
- **Wildcard cert:** `*.internal.nickscan.com` covers `nscim.internal.nickscan.com`, `portal.internal.nickscan.com`, `etc.internal.nickscan.com`. One cert, one renewal.
- Wildcards REQUIRE DNS-01 challenge; HTTP-01 won't work for `*.foo.com`.

---

## Tooling choice

| Tool | OS | Why |
|---|---|---|
| **win-acme** | Windows-native, GUI + CLI, scheduled task auto-renew | Recommended for this stack |
| Certify the Web | GUI, paid commercial | Overkill for a single cert |
| posh-acme | PowerShell module | Good if you're scripting heavily |
| Certbot | Linux-native | Not for this Windows server |

Going with **win-acme** below.

---

## Sequence of operations

### Phase 1: DNS first (5 min)

1. Add to public DNS (whichever provider hosts `nickscan.com`):
   ```
   nscim.nickscan.com   A   10.0.1.254   TTL 300
   ```
2. (Optional, recommended for wildcard) Delegate `internal.nickscan.com` to a small DNS server you control, or add CNAME `_acme-challenge.internal.nickscan.com` for DNS-01 challenge automation.
3. Wait for DNS propagation. Verify:
   ```powershell
   nslookup nscim.nickscan.com 8.8.8.8
   ```

### Phase 2: install win-acme on the NSCIM server (10 min)

```powershell
# As admin
choco install win-acme -y
# OR download from https://www.win-acme.com/ and unzip to C:\win-acme\
```

### Phase 3: get the cert (DNS-01 challenge) (15 min)

```powershell
cd C:\ProgramData\win-acme   # or wherever you installed it
.\wacs.exe --target manual `
           --host nscim.nickscan.com `
           --validation dns-01 `
           --validationmode dns-script `
           --dnsscript "C:\win-acme\Scripts\dns-cloudflare.ps1" `
           --installation iis,certificatestore `
           --emailaddress ops@nickscan.com `
           --accepttos
```

If your DNS provider has a published win-acme integration (Cloudflare, AWS Route53, Azure DNS, etc.), wire it into `--dnsscript`. Otherwise:
- Use `--validation dns-01 --validationmode manual` and win-acme will pause and tell you the TXT record to add manually. One-time pain for the first issuance, then automate later.

Cert lands in `LocalMachine\My` automatically with auto-renewal scheduled (Windows Task Scheduler entry created by win-acme).

### Phase 4: switch NSCIM apps to use it (5 min)

**API** — edit `src/NickScanCentralImagingPortal.API/appsettings.json`:
```json
"Kestrel": {
  "Endpoints": {
    "Http":  { "Url": "http://0.0.0.0:5205" },
    "Https": { "Url": "https://0.0.0.0:5206" }
  }
},
"SslCertificates": {
  "ApiCertificate": {
    "Source": "Store",
    "StoreLocation": "LocalMachine",
    "StoreName": "My",
    "Thumbprint": "<thumbprint of the new Let's Encrypt cert>"
  }
}
```
Set the env var:
```powershell
[Environment]::SetEnvironmentVariable('NICKSCAN_API_CERT_THUMBPRINT', '<thumbprint>', 'Machine')
```
(Or store the thumbprint directly in appsettings — it's not secret.)

**WebApp** — edit `src/NickScanWebApp.New/appsettings.json`:
```json
"Https": {
  "Url": "https://0.0.0.0:5300",
  "Certificate": {
    "Subject": "nscim.nickscan.com",
    "Store": "My",
    "Location": "LocalMachine",
    "AllowInvalid": false
  }
}
```
(Subject lookup is fine here — only one cert in the store will match `nscim.nickscan.com`.)

### Phase 5: update internal links + restart (10 min)

- Find/replace `https://10.0.1.254:5206` → `https://nscim.nickscan.com:5206` in:
  - `appsettings.json` `AllowedOrigins` arrays
  - `appsettings.json` `PublicBaseUrl`
  - Any hardcoded URLs in WebApp Razor / config
- Restart `NSCIM_API` and `NSCIM_WebApp` Windows services.
- Test from a clean browser (one that does NOT have the mkcert root CA installed) — should show clean lock icon, no warnings.

### Phase 6: roll back mkcert trust (optional, 5 min — wait 1-2 weeks)

Once you're confident Path C is stable across all users:
```powershell
# On each user machine that ran trust-nscim-cert.ps1:
Get-ChildItem Cert:\LocalMachine\Root |
  Where-Object { $_.Subject -like '*mkcert*' } |
  Remove-Item
```
The mkcert root CA on the server can stay — harmless if no apps are using it.

---

## Auto-renewal

win-acme installs a scheduled task at `\win-acme renew (acme-v02.api.letsencrypt.org)`. It runs daily, renews when <30 days remaining (Let's Encrypt cert lifetime is 90 days). After renewal:
- Cert is replaced in `LocalMachine\My` under the same Subject (different thumbprint)
- If you used `Subject` lookup in appsettings, no app config change needed — Kestrel picks up the new cert on next restart
- If you used `Thumbprint` lookup, **Kestrel will keep using the old (soon-expiring) cert until you update the thumbprint and restart** — write a renewal hook script for win-acme to update the env var + restart the service

Recommended renewal hook (drop in `win-acme/Scripts/post-renewal-restart-nscim.ps1`):
```powershell
param([string]$Thumbprint, [string]$CommonName)
[Environment]::SetEnvironmentVariable('NICKSCAN_API_CERT_THUMBPRINT', $Thumbprint, 'Machine')
Restart-Service NSCIM_API
Restart-Service NSCIM_WebApp
```
Wire it via `--script` flag on win-acme.

---

## Risks

| Risk | Mitigation |
|---|---|
| DNS propagation delay before first issuance | Give it 30 min; verify with `nslookup` against an external resolver |
| Public DNS exposes `10.0.1.254` IP | Either accept it (very common pattern) or use split-horizon DNS |
| Renewal silently fails, cert expires | Monitor: query `Cert:\LocalMachine\My` on a schedule; alert if any cert in scope has < 14 days |
| Internal users can't resolve the new name | Confirm split-horizon resolves correctly from a user workstation BEFORE the service restart |
| Path C and Path A live side-by-side, browsers cache HSTS / cert pinning | Force-clear: in Chrome/Edge `chrome://net-internals/#hsts` → Delete domain security policies |

---

## Cost

- Public DNS hosting for `nickscan.com`: already paying (it's your domain).
- Let's Encrypt cert: **free**.
- win-acme: **free**.
- Time: ~45 min once DNS access is confirmed.

---

## When to start

**Start when:**
- Path A trust install becomes a noticeable IT support burden (≥ 3 user requests)
- You add a non-Windows device to the user pool (mobile, Mac, Linux laptop)
- You add an external user (contractor, customer) who can't run admin scripts

**Don't start yet if:**
- Team is < 10 people, all on managed Windows boxes, Path A install has been done
- The `nickscan.com` DNS isn't accessible to whoever's doing this work
