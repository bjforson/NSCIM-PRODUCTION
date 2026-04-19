# Post-hotpatch operator config (2026-04-19)

On 2026-04-19 a set of operational fixes landed in source (CHANGELOG
`[2.9.0]`). Most of them are code changes and are in this repo. A few are
**machine state** that lives outside the source tree and therefore outside
git — these notes explain what has to be applied manually (or re-applied
after a cold rebuild of the server) so the fixes survive.

`Deploy.ps1` Phase 3.5 now handles the two that are safe to automate:

| Thing | Automated by `Deploy.ps1`? |
| --- | --- |
| `rollForward = latestMajor` in every `*.runtimeconfig.json` under `publish/` | Yes (Phase 3.5) |
| `NICKSCAN_FS6000_SHARE` NSSM env var on `NSCIM_ImageSplitter` | Yes (Phase 3.5) |
| `FS6000__NetworkSharePath` **machine** env var (for NSCIM_API) | No — manual (below) |
| `decisionagentsettings` DB tuning | No — manual (below) |
| WebApp HTTPS cert selection (PFX path vs. `Subject=10.0.1.254`) | No — see caveat below |

## 1. Machine env var — `FS6000__NetworkSharePath`

The API reads `FS6000:NetworkSharePath` via config. LocalSystem cannot see
the operator's mapped `Z:\`, so a UNC path is hardcoded into a machine-level
env var.

```powershell
[System.Environment]::SetEnvironmentVariable(
    'FS6000__NetworkSharePath',
    '\\172.16.1.1\Image\23301FS01',
    [System.EnvironmentVariableTarget]::Machine)
```

Restart `NSCIM_API` after setting it (services read env vars at process
start, not live).

Verify:

```powershell
[System.Environment]::GetEnvironmentVariable(
    'FS6000__NetworkSharePath',
    [System.EnvironmentVariableTarget]::Machine)
```

## 2. Decision agent tuning

Loosen the abnormal threshold and re-enable normal decisions so the agent
doesn't flag 100 % of records as abnormal.

```sql
-- Before:  allownormaldecisions=false, abnormalthreshold=0.35 -> 100 % abnormal
-- After:   allownormaldecisions=true,  abnormalthreshold=0.50 -> auto-clear low risk
UPDATE public.decisionagentsettings
SET    allownormaldecisions = TRUE,
       abnormalthreshold    = 0.50;
```

Column names are lowercase per Npgsql default naming; adjust if the schema
has been migrated to a different casing. Run this against the
`nickscan_production` database, then restart `NSCIM_API` so it reloads the
cached settings.

## 3. WebApp HTTPS cert — PFX vs. Subject binding

`src/NickScanWebApp.New/appsettings.json` currently pins the Kestrel HTTPS
cert to:

```json
"Certificate": {
  "Path": "C:\\Certificates\\nscim-production.pfx",
  "Password": "NscimProd2026!"
}
```

On the 2026-04-19 hot-patch window the PFX file was missing on the target,
and the binding was swapped live to a subject-store binding:

```json
"Certificate": {
  "Subject": "10.0.1.254",
  "Store": "My",
  "Location": "LocalMachine"
}
```

**Not** applied in source here because:
- the target server still shows the PFX block in its `appsettings.json`
  (the subject binding appears to have been applied as a runtime override,
  not a file edit), and
- other environments may have a valid PFX at that path.

If a cold rebuild of a server is needed, choose one:

- **Keep PFX:** ensure the file exists at `C:\Certificates\nscim-production.pfx`
  and the password matches, OR
- **Subject binding:** replace the `Certificate` block in
  `src/NickScanWebApp.New/appsettings.json` with the subject variant above
  and ensure the cert is imported to `LocalMachine\My` with Subject
  `CN=10.0.1.254` (or whichever hostname the server presents).

## 4. Anything else?

- `runtimeconfig.json` rollForward is handled by `Deploy.ps1` Phase 3.5;
  re-applied on every publish.
- `NICKSCAN_FS6000_SHARE` NSSM env is handled by `Deploy.ps1` Phase 3.5;
  idempotent.
- The three URL fixes baked into `src/NickScanWebApp.New/appsettings.json`
  (ApiSettings.BaseUrl, MobileApp.BaseUrl → localhost) were committed
  alongside these notes and no longer drift at publish time.
- Mobile Kestrel (`0.0.0.0:5280` / `0.0.0.0:5281`) is already in
  `src/NickScanWebApp.Mobile/appsettings.json`; source matches target.
