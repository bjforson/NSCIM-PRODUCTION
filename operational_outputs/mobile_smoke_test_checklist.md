# `.New` Mobile Smoke Test — Post-Retirement

**When to run:** After deploying the commit that retired Mobile to staging/prod.
**Why:** `.New` now serves mobile directly (no redirect). 56/64 pages have responsive markers (87.5%); the other 12.5% may look rough on small screens.
**Estimated time:** 15–20 minutes on a phone + 10 minutes on a tablet.

---

## Devices to test

- [ ] A real phone (iOS Safari or Android Chrome, portrait)
- [ ] A tablet (iPad Safari or Android tablet, portrait + landscape)
- [ ] Desktop Chrome DevTools "Responsive" mode (iPhone 13, Pixel 7, iPad Mini)

---

## Paths to walk on each device

Mark each row: ✅ works / ⚠ cosmetic / ❌ broken.

### Core pages (should all work)

| # | Path | Expected | Status |
|---|---|---|---|
| 1 | `/` (Home/Index) | Dashboard renders, nav drawer collapses | ☐ |
| 2 | `/login` (if logged out) | Login form usable on small screen | ☐ |
| 3 | `/containers` (Container list) | Table collapses at Sm breakpoint | ☐ |
| 4 | `/containers/<a-real-container>` (detail) | ICUMS/Scanner/Image tabs tap-friendly | ☐ |
| 5 | `/scanners` (ScannerOverview) | Overview cards stack | ☐ |
| 6 | `/scanners/ase` | ASE scanner detail readable | ☐ |
| 7 | `/scanners/fs6000` | FS6000 scanner detail readable | ☐ |
| 8 | `/scanners/heimann-smith` | Heimann detail readable | ☐ |
| 9 | `/icums` (ICUMSDashboard) | Dashboard cards/charts fit | ☐ |
| 10 | `/analytics` | Charts resize, no horizontal scroll | ☐ |
| 11 | `/operations/image-analysis` | Image viewer works on touch | ☐ |
| 12 | `/validation/image-analysis-management` | Table + actions usable | ☐ |
| 13 | Search box (top bar) | Opens + type-ahead works | ☐ |
| 14 | Profile/logout menu | Opens, logs out | ☐ |

### Known responsive-light pages (expect issues; note which)

8 pages in `.New/Pages/**` have NO `xs/sm/md` responsive markers. If any are in scope for mobile users, flag them.

```
Run: grep -L "xs=\|sm=\|md=" src/NickScanWebApp.New/Pages/**/*.razor
```

(Command doesn't work across subdirs in plain shell — use IDE "Find in Files" with `xs="\d"` and invert.)

### Cross-cutting behaviours

- [ ] Nav drawer opens/closes via hamburger icon
- [ ] Top bar title shortens or hides on small screens (no overflow)
- [ ] Modals/dialogs fit viewport (no horizontal scroll)
- [ ] `MudSelect` dropdowns usable (not cut off)
- [ ] Data tables switch to vertical/stacked layout at Sm breakpoint
- [ ] "Show empty fields" toggle on ICUMSDataTab (new in Part B) — visible + toggles correctly
- [ ] Admin-only `BOEIngestionMetadataPanel` (only visible to Admin role) — layout doesn't break on phone

---

## What to do with findings

Open a GitHub issue with the label `mobile-responsive` for each ⚠ or ❌:

**Issue template:**
```
Page: /containers/<example>
Device: iPhone 13, Safari, portrait (390 × 844)
Issue: <e.g. "Declaration Number text wraps off-screen, ICUMS tab unreachable">
Severity: minor / blocking
Screenshot: <attach>
```

## Rollback

If mobile looks catastrophically bad, flip back to the Mobile app:
```json
// src/NickScanWebApp.New/appsettings.json
"MobileApp": { "EnableDetection": true, "BaseUrl": "<mobile URL>" }
```
AND `git revert d1588ba` (only the retirement commit) to restore the middleware + Mobile project. Then redeploy the Mobile service via `07-register-services.ps1` with the pre-retirement version of the script.

This should never be needed, but it's the escape hatch.
