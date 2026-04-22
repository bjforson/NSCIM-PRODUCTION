# ICUMS Integration — DeliveryPlace Missing on Import Declarations

**To:** ICUMS integration contact  
**From:** NICKSCAN Central Imaging Portal team  
**Date:** 2026-04-22  
**Attachment:** `icums_no_deliveryplace.csv` (324 records)

---

## Summary

During a recent ingestion audit we found that **324 out of 28,560 real import declarations (1.1%)** received from your ICUMS batch feed have no `ManifestDetails.DeliveryPlace` value — neither in the mapped field nor anywhere in the payload.

We use `DeliveryPlace` as the port-of-entry signal for routing (Takoradi vs Tema). When it's missing we can't route these declarations automatically, so we're raising this to understand whether it's expected or a feed gap we should code around.

## Scope

| Metric | Count |
|---|---:|
| Total BOE records audited | 91,506 |
| Records with `DeliveryPlace` populated | 91,182 (99.65%) |
| Real declarations (non-CMR, `RegimeCode` set) missing `DeliveryPlace` | **324** |

All 324 are import declarations (`ClearanceType = "IM"`), broken down by regime:

| Regime | Meaning | Count |
|---:|---|---:|
| 40 | Direct entry for home consumption | 258 |
| 70 | Warehouse entry | 41 |
| 90 | Temporary admission | 19 |
| 80 | Transit | 6 |

Most also have no `BLNumber`, `RotationNumber`, or `ConsigneeName` — suggesting these records reach us before the manifest data is attached on the ICUMS side.

## Questions for your team

1. **Is `ManifestDetails.DeliveryPlace` guaranteed to be present** for Regime-40/70/80/90 import declarations, or is it populated only at a later stage in the ICUMS lifecycle (e.g. after CMR matching)?
2. **If later-stage**, is there a downstream message we should subscribe to that carries the port once it's assigned?
3. **For the 324 records** in the attached CSV, can you confirm whether they have `DeliveryPlace` on your side now, or are they genuinely missing?

## Attachment

`icums_no_deliveryplace.csv` — one row per affected BOE with:
- `boe_id`, `containernumber`, `declarationnumber`
- `clearancetype`, `regimecode`, `declarationdate`
- `blnumber`, `rotationnumber`, `consigneename`
- `crmslevel`, `createdat`

## Mitigation on our side (already in place)

Until we hear back, our portal flags these records with `HasIngestionWarnings = true` but still ingests them. They remain unroutable to scanner images (FS6000/ASE) until `DeliveryPlace` is present.

Happy to hop on a call to talk through the pattern.

— NICKSCAN Central Imaging Portal
