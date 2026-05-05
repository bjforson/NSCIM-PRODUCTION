# 03 — Match-correctness pipeline

**Audit:** NSCIM v1 cold audit, 2026-05-05
**Agent:** Match-correctness pipeline
**Scope:** Cold, exhaustive read of the system that decides whether a container scan can legitimately bind to a BOE document.

## Scope confirmation

This pass exhaustively read the ingestion-time mapping bridge in
`IcumJsonIngestionService.cs` (CMR→BOE upgrade, cascade-to-CCS, `documenttype`
tagging), the mapper service `ContainerDataMapperService.MapContainerDataAsync`
(belt-and-braces port + fyco rules, CBR INSERT site), the queue-driven and
re-check paths in `ContainerCompletenessService.cs`, the rule library in
`ContainerValidationService.cs` (`ValidatePortMatchAsync`,
`ValidateFycoImportExportAsync`, `IsExportFlag`), the helpers in
`ContainerScanQueue.cs` (`ScannerLocationMap`, `FycoClassifier`,
`RegimeDirectionMap`), the `MatchQualityFlag` entity + admin
`AdminMatchCorrectionController` endpoints, and the live data state in
`nickscan_production` (3,092 CBR rows, 4,261 CCS rows, 391 MQF rows) and
`nickscan_downloads` (119,668 BOE rows). 4 standalone probes (`MatchProbe`,
`MatchProbe2`, `MatchProbe3`, `MatchProbe4` under `C:\temp\nscim-probe`) ran 30+
queries covering MQF lifecycle, CBR uniqueness, CCS denorm drift, regime
distribution, fyco classifier coverage, hash-collision blast radius, and
specific container drilldowns (UETU3063684, MEDU7718311 and siblings).

Out of scope (explicit boundaries from charter): BOE field/label completeness
at ingest (Agent 5), AnalysisGroup creation/state-machine (Agent 4), frontend
filter behavior on the MatchCorrections page (Agent 6), and schema-level RLS
on flag tables (Agent 7). Cross-cutting concerns surfaced for those agents at
the end of the narrative.

## Findings

| ID | Severity | File:Line | Issue | Evidence | Proposed Fix | Effort | Risk of Fix |
|---|---|---|---|---|---|---|---|
| 3.01 | **P0** | `ImageAnalysisOrchestratorService.cs:2148-2241` + `ICUMSSubmissionService.cs:113-145` | **Layer 5 (submission-time port rule) does not exist in code.** Topology §G suspected this; cold reading of every submission path confirms it. The Outbox writer (`SubmitPayloadsToIcumsAsync` at line 2148) and the legacy `ICUMSSubmissionService.SubmitToICUMSAsync` (line 113) both POST payloads with zero re-validation. Only `LiveSubmitEnabled` is checked. CHANGELOG 2.16.0 line 155 explicitly claims "Cardinal port runs at queue time AND at mapper-INSERT time AND at submission-validation time" — claim is false for the third gate. | `SubmitPayloadsToIcumsAsync` reads `payloadFile`, builds HTTP request, POSTs. No call to `ValidatePortMatchAsync`/`ValidateFycoImportExportAsync` between read and POST. Probe shows `Submission.LiveSubmitEnabled=true` in `systemsettings` (overrides `appsettings.json:208`'s `false`) → live HTTP submission is currently ACTIVE; CCS row count `WorkflowStage=Submitted` = 119, `Acknowledged/` payload-archive directory exists. Today, an upstream regression in the queue/mapper would propagate to ICUMS unchecked. | Add a final `ContainerValidationService.IsReadyForSubmissionAsync(container, clearance)` gate inside the foreach at `:2185` before HTTP POST. Skip + log + flag instead of submit if it fails. The validator already exists (private `ValidatePortMatchAsync` and `ValidateFycoImportExportAsync` at lines 775/842) — needs to be promoted to public and invoked. | M | Med |
| 3.02 | **P1** | `ContainerCompletenessService.cs:975-1000` | **Step 2 (re-check) port-mismatch branch does not write `MatchQualityFlag`.** The location gate at line 981 zeroes `hasICUMSData` and clears state, but unlike Step 1 (line 410-447) it does NOT call `WriteMatchQualityFlagAsync`. Same comment-block at line 994-998 for null DP. Asymmetric coverage — every Step-2 mismatch is silently re-blocked with no admin visibility. | Live probe: 26 containers had `Status='Missing'` updated in last 7 days; 24 of them have NO open MQF rows at all. Comparison with Step 1 (lines 410-447 do call `WriteMatchQualityFlagAsync`) confirms the omission. | Add `await WriteMatchQualityFlagAsync(...)` calls inside both `if (!IsLocationMatch)` and `else if (NullDP)` branches at lines 982-999, mirroring the Step-1 contract. | S | Low |
| 3.03 | **P1** | `ContainerCompletenessService.cs:975-1000` | **Step 2 (re-check) has no fyco rule at all.** Step 1 fyco rule at lines 462-557 is replicated in `ContainerDataMapperService.cs:236-286` (mapper belt-and-braces) but the re-check path skips the entire fyco gate. A previously-Complete container that gets re-checked after BOE clearancetype changes (e.g. CMR→IM upgrade lands while the CCS re-check is mid-flight) will keep a stale `hasICUMSData=true` even if direction now disagrees. | Compare lines 462-557 (Step 1, fyco rule with 3 layers) vs. lines 1000-1023 (Step 2, only date-proximity check). Step 2 has only port + date checks — the fyco rule is missing entirely. | Hoist the fyco-rule block from Step 1 (lines 462-557) into a private helper, call it from both Step 1 and Step 2. Mirror the mapper's structure at `ContainerDataMapperService.cs:236-286`. | M | Med |
| 3.04 | **P1** | `ContainerDataMapperService.cs:189-310` | **Mapper hashes Guid `scan.Id` to int32 via `GetHashCode()`** (lines 598, 603, 652) and stores in `ContainerBOERelation.ScannerDataId`. Birthday collisions are statistically guaranteed at this scale. Consequence: the mapper's existence-check at line 196-199 (`r.ScannerDataId == scannerDataId`) treats two distinct scans with colliding hashes as "already mapped" → silent dedup of legitimate scans. Inversely, the same scan's hash recomputed by a different code path (e.g. ASE's negative hashes) can produce duplicate active rows. | Probe: 18 distinct containers carry 2-3 active CBR rows for the same `(containernumber, icumsboeid)` pair but different `ScannerDataId` values. Worst case `MRKU2369877` has 3 active CBRs. `ASE` rows: 2,797 distinct hash IDs across 3,053 rows; 1,567 negative hashes. Pattern of "different hash, same ICUMSBOEId" indicates a re-mapping cycle. | Replace `int ScannerDataId` with `Guid? ScannerDataId` (FS6000 + ASE) — schema change. Or as an interim: store `(ScannerType, ScannerNativeId)` composite key with a string ID column. EF migration required. | L | Med |
| 3.05 | **P1** | `IcumJsonIngestionService.cs:794-820` (and 754-792) | **`CascadeCMRUpgradeAsync` is NOT called on the `cmrUpgraded=true` path** at lines 754-792. It IS called at line 871 in the alternate "no existing doc" path. Result: when a CMR record already exists in `boedocuments` and gets upgraded in-place by the implicit/lifecycle path, the cascade to update `containercompletenessstatuses.HasICUMSData` and `ClearanceType` does NOT fire. Downstream CCS rows go stale. | Source: line 794 `if (cmrUpgraded) { ... continue; }` — no `await CascadeCMRUpgradeAsync(...)` between `cmrUpgraded=true` (line 777) and `continue` (line 819). Probe: 5 containers (MEDU7718311, MSBU1802425, MSBU3815020, MSNU3656034, MSNU1772462) sit in `Export-Hold` with `hasicumsdata=true` but `boedocumentid=NULL`, `clearancetype=NULL`, `groupidentifier=NULL` — classic missed-cascade signature. Plus `cmrupgradedat IS NOT NULL` count = 1,706 BOE rows, only some have run through cascade. | Move `CascadeCMRUpgradeAsync(boeDocument)` invocation up — call it inside the `if (cmrUpgraded)` block at line 794 before the `continue`. Wrap in try/catch like the existing call site at line 869-877. | XS | Low |
| 3.06 | P1 | `ContainerCompletenessService.cs:391` (mapper writes `r.ICUMSBOEId`) | **Mapper's BOE document selection uses `OrderByDescending(b => b.CreatedAt).FirstOrDefault()`** (Step 1 line 391, Step 2 line 972). Picks the most recently-created BOE per container, which may not be the canonical/active one if multiple BOE rows exist. Doesn't honor `ProcessingStatus="Transferred"` filter that the mapper's pending-mappings query DOES use (line 694). | Read line 391 vs. line 691-694: pending-mappings filters `ProcessingStatus == "Transferred"`; CCS-time selects most-recent unconditionally. Containers with multiple BOE rows (CMR pre-decs + later upgrades) can race: CMR row created 2026-04-29, IM-upgraded row appended 2026-05-04 — completeness picks the latest one, but the mapper may target a different one. Probe: BOE rows where `length(containernumber)<>11` = 9,295 (VINs / 17-char declarations land here). | Add `b.ProcessingStatus == "Transferred"` filter to BOE selection in both Step 1 and Step 2. Or extract a single helper `GetCanonicalBOEAsync(container)` and call from all sites. | S | Med |
| 3.07 | P1 | `ContainerScanQueue.cs:200-216` (`RegimeDirectionMap`) + `IcumJsonIngestionService.cs:710-743` | **Implicit CMR→IM upgrade is regime-blind for unknown regimes.** Ingestion-side switch at line 718-726 routes by first-char of regime: `4/7/9 → IM`, `1/3 → EX`, `2/5/6 → IM`, anything else → null (skip). 24-codes-only WCO map at `RegimeDirectionMap.cs:225-271` does NOT cover regimes 24, 27, 30, 35, 37, 45, 47, 48, 49, 57, 59, 72, 75, 77, 79, 94, 95, 97, 99 — those are valid future codes per memory. Today they have no live rows, but if one arrives (regime 27 = "Temp Export following Warehousing", an export-direction code), the switch will mis-classify it as IM (first-char `'2'`). | Source: `IcumJsonIngestionService.cs:718-726` `firstChar switch` clause uses `_ when ... '2' or '5' or '6' => "IM"`. The comment at line 724 admits "2* contains both EX/IM". Also missed: regime 27 is export, regime 24 is export, but the first-char rule lumps them with imports. | Replace the first-char heuristic with a direct lookup against `RegimeDirectionMap.IsExport(regime)` / `IsTransit(regime)` / explicit map of full regime codes. Falls through to null (no upgrade) for unknown — fail-closed. | S | Low |
| 3.08 | P1 | `ContainerScanQueue.cs:165-179` (`FycoClassifier`) | **`FycoClassifier.Classify` and `IsExportFlag` are out of sync.** `Classify` (line 169) returns `Export` for `WAYBILL`, `WABILL`, `EXPORT`. `IsExportFlag` (`ContainerValidationService.cs:951`) uses regex `\bex(p)?ort\b` plus the literal `1/true/Y/YES`. The classifier rejects "WAYBILL/EXPOT" (single typo "EXPOT") because it doesn't contain "EXPORT"; the regex rejects "WA" because no "EXPORT" token. Live FS6000 has 8 unclassified strings + `WAYBILL EXPOT`, `WAY-BILL/EXPORT`, `WAYBILL/.EXPORT`, etc. that fall through. Also: `Classify` is called from `ContainerCompletenessService.cs:296-306` and `ContainerDataMapperService.cs:254`; `IsExportFlag` is called from `ContainerValidationService.cs`. Two sources of truth. | `FycoClassifier.Classify` (line 173) uses substring `Contains("EXPORT")` etc. `IsExportFlag` uses regex `\bex(p)?ort\b`. Probe distinct values: "WAYBILL/EXPOT", "WAYBILL/EXPROT", "WAYBILLL/EXPORT" all classified as Export by `Classify` via "WAYBILL" substring; but "EXPOR" / "EXPRO" / "EPORT" all rejected. The regex catches "EXPOR" via `ex(p)?ort` — wait, `\bex(p)?ort\b` does NOT match "EXPOR" (missing T). 8 records with "EXPOR" / "EXPOT" / "EXPROT" / "EPORT" / "EXORT" slip through both. | Unify on a single `FycoClassifier.IsExport(string)` and `IsImport(string)` API. Use the broader regex; expand to handle T-elision (`(?i)(?:export?|expor[t]?)`). Have `IsExportFlag` simply call `FycoClassifier`. | S | Low |
| 3.09 | P2 | `ContainerCompletenessService.cs:298-348` (Step-1 export-detect) | **FS6000+EXPORT scans land in `Export-Pending` immediately, BEFORE the port rule fires.** Logic at line 308-343: if `FycoClassifier.Classify(fycoPresent) == Export`, the container is marked `Status="Export-Pending"`, `WorkflowStage="Export-Hold"`, `HasICUMSData=false`, and queue item is marked completed (`continue`). The cardinal port rule at line 395 never executes for those containers. As a result, an FS6000 export scan that DOES match a TKD-port BOE never gets ICUMS data attached even if the BOE arrives. | Lines 296-343: control flow guarantees `continue` on Export detection. Manual cross-check: 1,107 CCS rows in `WorkflowStage='Export-Hold'` (probe). Of those, 13 have `hasicumsdata=true` (likely from manual cascade); rest have `hasicumsdata=false`. Memory `reference_port_match_rules_enabled_2026_05_02.md` notes 1,030 export scans with no BOE — exactly the population this branch produces. | Extend export-detect to query for an export-direction BOE before quitting. If found and port matches and regime is in export set, treat as match. Otherwise stay Export-Hold. | M | Med |
| 3.10 | P2 | `AdminMatchCorrectionController.cs:303-461` (Rematch) | **Admin Rematch has no fyco-rule audit-trail flag.** It has a port-mismatch soft-warn at line 421 (writes a `Critical PortMismatch` flag with `Resolution="Confirmed"`). No equivalent for fyco-direction violations. An admin can rematch a container whose scan fyco contradicts the chosen BOE's clearancetype/regime, and the audit log only captures the port disagreement. | Read 423-446 (port soft-warn block) — only port. No fyco evaluation. Probe: 0 rows with `Resolution='Confirmed'` exist anywhere — admin rematch hasn't been used for port overrides; if/when used, fyco mismatches would not be visible. | Mirror the port soft-warn pattern: classify `request.ScannerType + latest fycopresent` vs. `targetBoe.ClearanceType + RegimeCode`. If disagreement, write a second `Critical FycoMismatch` flag with `Resolution="Confirmed"`. | XS | Low |
| 3.11 | P2 | `AdminMatchCorrectionController.cs:355` (Rematch creates new CBR) | **Admin Rematch sets `RelationType = request.ScannerType ?? "ADMIN"`** which is NOT one of the documented enum values in `ContainerBOERelation.cs:26` (`'ASE'`, `'FS6000'`, `'NUCTECH'`) NOR matches the live convention (`'Primary'`/`'Consolidated-HouseBL'` set by mapper at line 295). Three different writers, three different value spaces. | Live probe: distinct `relationtype` values in CBR are `'Primary'` (3,048) + `'Consolidated-HouseBL'` (44). Mapper writes those (line 295). Rematch writes scanner-name. Entity comment claims a different scheme entirely. | Pick one convention. Mapper's `'Primary'`/`'Consolidated-HouseBL'` is the live one — Rematch should follow. Update entity comment. | XS | Low |
| 3.12 | P2 | `ContainerValidationService.cs:779-833` (`ValidatePortMatchAsync`) | **`ValidatePortMatchAsync` queries BOE by container number (not by active CBR.icumsboeid).** Uses `OrderByDescending(b => b.Id).FirstOrDefault()` to pick the BOE — same risk as 3.06: latest BOE may not be the one CBR points at. Also: `ValidateContainerAsync` (the only caller of this method, via `ValidateBusinessRulesAsync`) is itself only invoked from `GatewayOrchestrationService.cs:262` for the `/container/{containerNumber}/complete` aggregator endpoint — NOT from any submission path. So `ValidatePortMatchAsync` is currently a read-only telemetry function, not a gate. | Source: line 779-784 reads BOE by container, ordered by Id desc, ignores CBR. `ValidateContainerAsync` callers (Grep): `GatewayOrchestrationService.cs:262`, internal recursion in `ValidationService` at lines 393, 415, 521. None of these are on the submission, mapping, or completeness path. | Either (a) wire `ValidateContainerAsync` into the submission flow (closes 3.01), or (b) explicitly mark the method as advisory-only. Either way, fix the BOE selector to prefer the active CBR's `ICUMSBOEId` first. | M | Med |
| 3.13 | P2 | `IcumJsonIngestionService.cs:1448-1466` (cascade port gate) | **Cascade port gate uses `ScannerLocationMap.IsLocationMatch` which returns `true` for null DP.** When the upgraded BOE has null DeliveryPlace, the cascade flips `HasICUMSData=true` regardless of scanner port. The fyco rule downstream may still catch the mismatch, but the optimistic gate weakens the layer-3 cascade gate noted in CHANGELOG and topology §G. | Source: `ScannerLocationMap.IsLocationMatch(scannerType, dp)` at `ContainerScanQueue.cs:146-152` returns `true` if dp `IsNullOrWhiteSpace`. The cascade at `IcumJsonIngestionService.cs:1448` uses this method, so null-DP cargo gets `HasICUMSData=true`. Probe: BOE table has 354 null-DP rows. | Use a stricter "ports actually agree" check inside cascade — explicit `ExtractPortCode != null && matches expected`. Null DP should leave cascade off + write a `NullDeliveryPlace` flag instead. | S | Low |
| 3.14 | P2 | `ContainerCompletenessService.cs:391` (Step-1 BOE selection) | **`primaryBOE = boeRecords.OrderByDescending(b => b.CreatedAt).FirstOrDefault()`** picks newest by `CreatedAt`, but a CMR row often has an earlier `createdat` than the upgraded BOE row even though they share an Id (UpgradeCMRToBOEAsync updates in place). The ordering may be inconsistent across re-checks. Cross-check with Mapper's `OrderByDescending(b => b.CreatedAt)` at `ContainerDataMapperService.cs:386` and `ValidatePortMatchAsync`'s `OrderByDescending(b => b.Id)` at line 782 — three different orderings, three call sites. | 3 distinct orderings used: Step 1 line 386/391 = `CreatedAt`, Mapper line 386 = `CreatedAt`, ValidatePortMatchAsync line 782 = `Id desc`. CCS Step 2 line 967 = `CreatedAt`. Inconsistency means different gates may pick different BOEs for the same container at the same instant. | Standardize on a single ordering (preferably `Id desc` since it's monotonic and uncorrupted by CMR-upgrade timestamp logic). Extract a `GetPrimaryBOEAsync(container, ctx)` helper. | S | Low |
| 3.15 | P2 | `MatchQualityFlag.cs` + `ContainerCompletenessService.WriteMatchQualityFlagAsync:55-103` | **`MatchQualityFlag.ContainerNumber` has no FK to a single source-of-truth.** Flags are keyed only by `containernumber` string. Cross-scanner case: a container scanned by both FS6000 (TKD) and ASE (TMA) is ambiguous — which leg's port does the flag describe? Currently `ScannerType` is captured but the dedup query at line 67-71 is per `(container, flagtype)` regardless of scanner — so a single PortMismatch flag covers both legs. | Probe drilldown: UETU3063684 has FS6000 CCS row (Status=Export-Pending) AND an active ASE CBR (id=3342). MQF row 376 has `ScannerType=FS6000` and points at BOE 35974 (TMA port). The ASE CBR is correctly TMA-port-matched. Result: a "Critical PortMismatch" flag exists on a container whose active CBR is fine. False alarm. 4,246 of 4,261 CCS distinct containers have rows from BOTH FS6000 and ASE (probe `match4`). | Make MQF dedup key `(ContainerNumber, ScannerType, FlagType)` not just `(ContainerNumber, FlagType)`. Update `WriteMatchQualityFlagAsync` and `Upsert*FlagAsync` accordingly. Update Admin list endpoint to surface scanner. | S | Low |
| 3.16 | P2 | `ContainerDataMapperService.cs:222` (mapper port rule) | **Mapper port-rule does not write a flag when `boeDocument == null`.** Path at line 209-234: if BOE lookup fails (`b.Id == icumsDataId` returns nothing), the mapper just continues to INSERT a CBR with no port check, no fyco check. The `pendingMappings` query at line 691-701 already filters to `ProcessingStatus="Transferred"`, but a race between query and INSERT can leave a stale ICUMSBOEId. | Source line 211: `var boeDocument = await ... .FirstOrDefaultAsync(b => b.Id == icumsDataId);`. Lines 222 and 245 both gate on `boeDocument != null`. No `else` branch. Result: failed BOE lookup is silently allowed. | Add an early-return + warning if `boeDocument == null` after `icumsDataId > 0`. Belt-and-braces with a low-severity `OrphanBOEReference` flag. | XS | Low |
| 3.17 | P2 | live data | **Regime-80 BOE rows tagged `clearancetype="IM"` natively from ICUMS.** 6,442 of 6,553 regime-80 rows have `clearancetype="IM"` — only 111 are CMR. The transit-skip carve-out in `RegimeDirectionMap.IsTransit()` (used only by ingest-side CMR→IM upgrade switch) doesn't fix this — they arrive already labeled IM. The fyco rule (layer 2) then fires correctly on `fyco=Export + clearance=IM`, blocking matches. Memory note documents this as "upstream conflation we can't fix here" but the rule layer treats them as anomalies; today there's no analogue carve-out for fyco rule even though `IsTransit` exists. | Probe: regime=80, clearancetype=IM count = 6,442; clearancetype=CMR = 111. Documenttype=Transit count = 6,552; null=1. Memory notes 5,434 historic IM-typed regime-80 rows from upstream. | This is upstream data quality; in-system the rule correctly catches them. P2 not P1. Document explicitly + add metric on `boedocuments` table for regime-80-IM count over time, and include a "transit-typed" exemption only if business confirms. | S | Low |
| 3.18 | P3 | `ContainerCompletenessService.cs:559-579` | **Date-proximity check (>90 days) is parallel to but not coordinated with the cardinal/fyco rules.** Triggers `hasICUMSData=false` but writes NO MQF row. Also does not apply if `ScanDate=default` (sentinel). | Lines 569-578: zeroes hasICUMSData on >90-day gap; no `WriteMatchQualityFlagAsync` call. | Add `DateProximity` flag type to MatchQualityFlag enum; persist for visibility. | XS | Low |
| 3.19 | P3 | `ContainerCompletenessService.cs:559-579` (date-proximity) and Step 2 line 1003-1022 | **Date-proximity uses `BOE.DeclarationDate` parsed via `DateTime.TryParse`** with no culture / format spec. ICUMS dates are inconsistent across sources. Failed parse = silent skip. | Source: line 564 `DateTime.TryParse(primaryBOE.DeclarationDate, out var parsedBoeDate)`. No culture, no format string. Sample BOE.DeclarationDate values can include '20240329', '2024-03-29', '29/03/2024', etc. — all parse differently. | Use `DateTime.TryParseExact` with the canonical ICUMS format(s) tried in order. Log on parse-failure for visibility. | XS | Low |
| 3.20 | P3 | `ContainerScanQueue.cs:264-271` (`BoeRegimes` set) | **`BoeRegimes` set hard-codes 25 codes in `RegimeDirectionMap`** but is duplicated logic with `ExportRegimes` (10 codes also in BoeRegimes set). Adding a new regime requires updating both sets in lockstep. | `BoeRegimes` includes "10", "19", "20", "24", "27", "30", "34", "35", "37", "39" which are also in `ExportRegimes`. Dual maintenance. | Define `ExportRegimes`, `ImportRegimes`, `TransitRegimes`, `FreeZoneRegimes`. `BoeRegimes` becomes `Export ∪ Import` (computed). `ClassifyDocumentType` derives bucket from set membership. | XS | Low |
| 3.21 | P3 | `ContainerCompletenessService.cs:592-659` (DuplicateImage detection) | **DuplicateImage flag uses `FS6000Image.FileName` substring match** with no scanner correlation. A future scanner that re-uses filenames (or the FS6000 sequence-number rolls over) will mass-flag legitimate scans. | Lines 610-620: query is `Where(i => i.FileName == fileName && !ownScanIds.Contains(i.ScanId))`. No date / day-of-scan filter. | Add scoping: `&& i.UploadedAt >= scanDate.AddDays(-1)` etc. Or hash image bytes for a stronger match key. | S | Low |
| 3.22 | P3 | `ContainerCompletenessService.cs:1102-1131` (PREVENTIVE FIX block) | **Preventive-fix block silently mutates CCS state without an audit trail.** The "GroupIdentifier mismatch" (line 1107), "missing BOEDocumentId" (line 1115), "both NULL but data present" (line 1124) auto-corrections happen in Step 2; no MQF row written, no audit log line. | Source 1102-1131. Three distinct silent overrides. Logged as `LogWarning` only. | At minimum, write a `Warning`-severity `AutoRepair` MQF row per fix. | XS | Low |

## Narrative

The match-correctness pipeline has six gates as documented (CHANGELOG 2.16.0,
topology §G), but **only four of them actually run code**, and several are
asymmetrically coverage-degraded.

**The Layer 5 finding is the headline (3.01).** Every prior memory and
CHANGELOG entry asserts a port rule fires at submission time. There is none.
`SubmitPayloadsToIcumsAsync` reads payload files and POSTs them with no
re-validation; `ICUMSSubmissionService.SubmitToICUMSAsync` likewise. Today
this is not theoretical — `systemsettings.Submission.LiveSubmitEnabled=true`
in the live DB (probe `match4`), overriding the `false` in `appsettings.json`.
HTTP submissions are landing in production. Layer 5's absence means a
regression in the queue or mapper layer (e.g. via the regime-blind upgrade
switch in 3.07, the cascade-not-fired path in 3.05, or the hash-collision
duplicate active CBRs in 3.04) propagates straight to ICUMS without a final
gate. The validator code already exists (`ValidatePortMatchAsync`,
`ValidateFycoImportExportAsync`) — it just isn't called.

**Step 2 (re-check) is materially weaker than Step 1 (3.02, 3.03).** The
re-check loop at `ContainerCompletenessService.cs:856+` zeroes out
`hasICUMSData` on port mismatch but writes no MQF and runs no fyco rule. So a
container that initially matched cleanly, was promoted to Complete, and is
now being re-checked because the BOE's clearancetype changed (or got
upgraded), can have its match silently broken without any admin-visible
trail. 24 such cases in the last 7 days have no MQF coverage (probe
`match4`).

**The mapper's `ScannerDataId = scan.Id.GetHashCode()` (3.04) is the
sleeper bug.** Mapping a Guid to int32 via `GetHashCode` is collision-prone;
1,567 negative-hash rows are already in CBR. The mapper's "already mapped"
check uses this hash as a key, so collisions cause both false positives
(legitimate new scans treated as already mapped, silently dropped) and false
negatives (one scan hashed twice creates duplicate active CBRs). 18 distinct
containers carry duplicate-active CBRs today; `MRKU2369877` has three.

**The CMR→BOE cascade has a missing-call hole (3.05).** When the existing-doc
upgrade path fires (line 754-792), it sets `cmrUpgraded=true` but does NOT
call `CascadeCMRUpgradeAsync` before `continue`. Only the alternate "no
existing doc" path (line 871) does. The five Export-Hold containers (MEDU7718311
+ siblings) with `hasicumsdata=true, boedocumentid=null, clearancetype=null`
are the smoking-gun signature of this hole — the BOE got upgraded, the row
got the upgrade, but the CCS denorm wasn't refreshed.

**Cross-scanner ambiguity (3.15) explains the 1 active PortMismatch.** UETU3063684
has both an FS6000 and an ASE leg. Its FS6000 CCS row legitimately failed the
port rule (FS6000 + TMA-port BOE) and got flagged. But its ASE CBR is fine.
The MQF dedup query at `WriteMatchQualityFlagAsync:67-71` uses only
`(container, flagtype)` — so a container with both legs gets a single flag
covering both, even when one is correct. 4,246 of 4,261 distinct containers
have both-scanner CCS rows.

**Several smaller landmines:** the regime-blind first-char switch (3.07)
will mis-flag regime 27 if it ever arrives; `FycoClassifier` and
`IsExportFlag` use different parsers for the same data (3.08); admin Rematch
audits port disagreements but not fyco (3.10); the date-proximity gate
silently zeroes matches with no flag (3.18-3.19); preventive-fix blocks
silently mutate state (3.22).

The 6-layer model in topology §G holds up at the design level but the
implementation has more holes than the description implies. None of these
are P0 in isolation (Layer 5 is the worst, and it's been latently absent
for the entire 2.16.x arc), but cumulatively they erode the
"belt-and-braces" claim.

## Open questions

1. Was Layer 5 ever implemented and reverted, or did the docs just
   over-specify it? Git log on `ImageAnalysisOrchestratorService.cs` and
   `ICUMSSubmissionService.cs` for any `ValidatePort`-shaped commit since
   the rule layer landed in `bfd4d61` (2026-04-22) would settle this.

2. The 1,706 BOE rows with `OriginalClearanceType="CMR"` — how many of them
   reached the cascade path that fires (line 871) vs. the missed path (line
   794)? A quick join from `boedocuments.cmrupgradedat` to
   `containercompletenessstatuses.updatedat` would quantify the blast radius
   of finding 3.05.

3. What's the intended canonical `ContainerBOERelation.RelationType` enum?
   Three writers each use a different vocabulary (mapper: Primary /
   Consolidated-HouseBL; admin Rematch: scanner-name / "ADMIN"; entity
   comment: ASE / FS6000 / NUCTECH). Pick one and migrate.

4. Cross-scanner containers (FS6000 + ASE both scanned) — is this
   semantically a "transit" / "single shipment scanned twice" event? The
   current rule layer treats each leg independently, but operators may want
   a single source-of-truth match per container. Worth a product
   conversation; affects MQF dedup contract (3.15).

## Cross-cutting concerns for other agents

- **Agent 4 (Assignment):** the cross-scanner ambiguity (3.15) and the
  duplicate-active CBR pattern (3.04) feed into AnalysisGroup creation —
  the mapper's CBR INSERTs drive what the orchestrator considers
  "matchable" downstream. If two CBRs exist for the same container, which
  one does the AG-creation logic pick? Worth confirming.

- **Agent 5 (ICUMS):** finding 3.07 (regime-blind switch) is squarely in
  ICUMS-ingestion territory. Finding 3.13 (cascade null-DP gate weakness)
  also crosses Agent 5's scope. Layer-5-missing (3.01) is a *submission-side*
  bug but the missing validator code is in `ImageAnalysisOrchestratorService` —
  Agent 5 should confirm the live `LiveSubmitEnabled=true` finding and whether
  the upstream gating is sufficient in light of 3.04/3.05/3.07.
