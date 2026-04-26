# NSCIM cert trust — for end users

If your browser shows **"Not Secure"** or a red warning when you visit the
NSCIM apps (`https://10.0.1.254:5206` / `https://10.0.1.254:5300`), this
folder fixes it.

## What's in here

- `nscim-rootCA.pem` — the public certificate of the NSCIM internal CA.
  Safe to share. Contains no private keys.
- `trust-nscim-cert.ps1` — installs that CA into your machine's trust
  store so browsers treat NSCIM URLs as legitimate HTTPS.

## How to install (one-time, ~30 seconds)

1. Copy this whole `ops/` folder to your local machine (e.g. Desktop).
2. **Right-click** PowerShell → **Run as administrator**.
3. In that PowerShell window:
   ```powershell
   cd C:\path\to\ops
   Set-ExecutionPolicy -Scope Process Bypass -Force
   .\trust-nscim-cert.ps1
   ```
4. Restart your browser. Visit the NSCIM URL. No more warning.

## Who needs to run this

- Anyone whose browser shows the "Not Secure" warning when opening NSCIM.
- Domain-joined users may eventually get this trust pushed via Group
  Policy; until then, run the script.

## Removing the trust later

If you ever need to remove it:
```powershell
Get-ChildItem Cert:\LocalMachine\Root |
  Where-Object { $_.Subject -like '*mkcert*' } |
  Remove-Item
```

## Troubleshooting

**"Cannot be loaded because running scripts is disabled on this system"**
Run `Set-ExecutionPolicy -Scope Process Bypass -Force` in the same window
before re-running the script.

**Browser still shows the warning after restart**
Fully close all browser windows (including background processes via Task
Manager → "End task" on `chrome.exe` / `msedge.exe` / `firefox.exe`),
then reopen.

**Firefox specifically**
Firefox keeps its own trust store. The script flips the
`ImportEnterpriseRoots` policy automatically; just restart Firefox.

**Mobile devices / non-Windows machines**
The script is Windows-only. For other devices, see the IT lead — or ask
about the upcoming Path C migration (DNS-named cert from a real public
CA), which will eliminate this manual step entirely.
