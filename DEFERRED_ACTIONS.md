# Deferred Actions — 2026-04-22 Consolidation

Everything that code could close is closed. What remains needs human hands:
send an email, pick up a phone, decide when to flip a feature flag.

---

## 1. Send the ICUMS outreach email

**Why:** 324 real import declarations (IM, regimes 40/70/80/90) lack
`ManifestDetails.DeliveryPlace` in the source feed. We can't route those to
a scanner port until ICUMS either fills the field or tells us it's a lifecycle thing.

**Materials (local only, not in git):**
- `operational_outputs/icums_no_deliveryplace.csv` — 324 rows, attach as-is
- `operational_outputs/icums_outreach_email.md` — draft body, copy into your email client

**Suggested send window:** any weekday. Expect a week+ turnaround.

**When they reply:**
- If upstream gap → add a mitigation like tagging records with `upstream_missing` and excluding from port-match checks.
- If lifecycle (DP comes later) → ingest as-is, don't route until DP arrives.

---

## 2. Mobile smoke test on real devices

**Why:** `NickScanWebApp.Mobile` was retired. `.New` now serves every
viewport. 56 of 64 pages have responsive MudGrid markers (~87%); the
remaining 8 may look rough on small screens.

**Checklist:** `operational_outputs/mobile_smoke_test_checklist.md`
- Walk the 14 listed routes
- On a real phone (iOS Safari or Android Chrome, portrait)
- On a tablet (both orientations)
- Log any visual issues as GitHub issues labeled `mobile-responsive`

**Rollback instruction (worst case):** listed in the same checklist.

---

## 3. Flag-flip schedule

**Flags (currently OFF):** both in `src/NickScanCentralImagingPortal.API/appsettings.json`:
- `IcumIngestion:EnablePortAssignmentRule`
- `IcumIngestion:EnableFycoImportExportRule`

**Recommended cadence:**

| Day | Action |
|---|---|
| D+0 (today) | Deploy done. Flags OFF. |
| D+1..D+7 | Watch `/api/icums/batch/ingestion-health?hours=24` daily; check `WarningRatePct` and `FailedQueueDepth`. See §4 for query cheatsheet. |
| D+7 | Flip `EnablePortAssignmentRule: true` in **staging** (if applicable — if prod is the only env, flip in prod during low-traffic window). Restart NSCIM_API. |
| D+8..D+10 | Watch validation errors in `ContainerValidationController` responses. Verify legit containers still pass, mismatches fail with readable messages. |
| D+10 | Flip `EnableFycoImportExportRule: true`. Restart NSCIM_API. |
| D+14 | If no fires, consider this stable. |

**Kill switch:** set flag back to `false`, restart `NSCIM_API`. No data rollback needed.

---

## 4. Verify new writes are actually happening

These queries should all return > 0 after the first ingestion cycle post-deploy
(roughly the next scheduled batch run; depends on `ICUMS:BatchIntervalMinutes`).

```sql
-- Ingestion logs populating?
SELECT COUNT(*) AS rows, MAX(createdat) AS most_recent
FROM ingestionlogs
WHERE createdat >= NOW() - INTERVAL '24 hours';

-- Warning columns capturing signal?
SELECT COUNT(*) AS warned_boes
FROM boedocuments
WHERE hasingestionwarnings = true
  AND createdat >= NOW() - INTERVAL '24 hours';

-- What warning categories are firing?
SELECT
  regexp_replace(line, '^(.*?)(\\-|$).*', '\\1') AS kind,
  COUNT(*) AS n
FROM (
  SELECT UNNEST(string_to_array(ingestionwarnings, E'\n')) AS line
  FROM boedocuments
  WHERE hasingestionwarnings = true
) s
GROUP BY kind
ORDER BY n DESC;
```

**If `ingestionlogs.rows = 0` after 24 h of confirmed batch runs:**
something's wrong with the wiring I added to `ProcessSingleFileAsync`.
Open the API logs (`C:\Shared\NSCIM_PRODUCTION\logs\`) and grep for
`"Failed to create ingestion log"` — it'll tell us what blew up.

**If `warned_boes = 0` after a week of ingests:** likely the source is
just clean (which is what we saw in the historical audit — prod's data
is in good shape). Not necessarily a bug.

---

## 5. Push to origin on NSCIM-PRODUCTION

Already committed locally as of `bfd4d61`. Still not pushed — your call when:

```powershell
cd C:\Shared\NSCIM_PRODUCTION
git push origin main
```

If the remote push is authenticated via PAT/SSH, handle that first.

---

## 6. Delete stale `claude/festive-tharp` branch on retired repo

The work originally lived on `github.com/bjforson/NICKSCAN-CENTRAL--IMAGE-PORTAL`
branch `claude/festive-tharp`. That repo is retired; the branch serves no purpose.

```powershell
# If you can authenticate against that remote:
git push https://github.com/bjforson/NICKSCAN-CENTRAL--IMAGE-PORTAL.git --delete claude/festive-tharp
```

Or archive the whole repo via GitHub settings → Archive this repository.

---

## 7. Housekeeping notes

- **`_archive/` moved to `C:\Shared\_archive\2026-04-22\`** (out of repo). 629 MB of pre-consolidation tarballs + README. Keep for 30-90 days, then delete.
- **`operational_outputs/` untracked but still on disk** at `C:\Shared\NSCIM_PRODUCTION\operational_outputs\`. Contains ICUMS outreach CSV + email draft + mobile smoke test checklist. Delete when no longer needed.
- **Legacy `.rar` backups** in `C:\Users\Administrator\Documents\GitHub\` (`NICKSCAN-CENTRAL--IMAGE-PORTAL.rar`, `NS_CIM.rar`) were pre-existing, NOT touched by consolidation. Keep or delete to taste.
- **Empty directory skeletons** at `C:\Users\Administrator\Documents\GitHub\NICKSCAN-CENTRAL--IMAGE-PORTAL\.claude\worktrees\` will auto-clear when the last Claude session holding cwd handles exits.

---

## Anything else?

If you find a surprise in production that traces back to the 2026-04-22 work,
start here:
- Ingestion warnings feature: `IcumJsonIngestionService.ValidateCriticalFieldsAsync` + `ValidateIngestedDocumentAsync`
- Port-match / Fyco rules: `ContainerValidationService.ValidatePortMatchAsync` + `ValidateFycoImportExportAsync`
- Admin endpoints: `IcumBatchController.GetIngestionHealth` / `GetWarnings` / `GetRecentIngestionLogs`
- Admin panel: `Components/ICUMS/BOEIngestionMetadataPanel.razor`, wired in `Components/CargoGroup/CargoGroupICUMSDataTab.razor`
