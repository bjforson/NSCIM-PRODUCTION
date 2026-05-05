# Follow-up — Server-side disambiguation endpoint design (Theme E)

**Status:** Read-only design. No code edits, no DB writes.
**Audit refs:** `REPORT.md` Theme E, `06-frontend.md` 6.01-6.04, `01-topology.md` §E.
**Probes:** `C:\temp\nscim-probe\AgGroupTypeDistro.cs` (read-only).
**Scope:** v1 only.

Design feeder for Sprint Group F. The MasterBlNumber backfill (`REPORT.md`
§6 Group D step 4 — 5,721 rows) is **blocked** until Phase 1 lands; §5
explains why.

---

## 1. Current contract — what's wrong

### 1.1 The frontend is doing dispatch the API should be doing

The dialog `ImageAnalysisViewDialog.razor` receives an opaque tuple from the
queue table — `(GroupId, GroupIdentifier, ScannerType, IsConsolidated,
Containers[])` — and **branches on `IsConsolidated`** to choose:

- which URL shape to call (container-keyed vs declaration-keyed);
- which DB key to put on the wire (`GroupIdentifier` interpreted as
  ContainerNumber vs DeclarationNumber);
- which response shape to expect.

Concretely, the assumption "`IsConsolidated == true ⇒ GroupIdentifier IS
the container number`" is hardcoded in:

| File | Line(s) | Use |
|---|---|---|
| `Components/Operations/ImageAnalysisViewDialog.razor` | 194, 207-209 | `<ScannerDataTab ContainerNumber="@GroupIdentifier" />` and `<ImageDecisionView>` — passed as container number when consolidated, declaration otherwise |
| same | 838-872 | `BuildContainerEndpointUrl` centralises the assumption: consolidated → route by groupIdentifier; non-consolidated → route by `routeContainer`, attach `?declarationNumber=...` |
| same | 1413-1454 (`PreloadICUMSData`) | Early-return on `IsConsolidated`; only preloads non-consolidated path |
| same | 1463-1693 (`LoadCargoGroupForSummary`) | Branches on `IsConsolidated` to resolve container-number → Master BL, or treat as aggregate already |
| same | 1707-1730 (`LoadHouseBLs`) | `GET /api/consolidatedcargo/container/{GroupIdentifier}/housebls` |
| same | 1732-1785 (`LoadContainers`) | `GET /api/consolidatedcargo/declaration/{GroupIdentifier}/containers` |
| same | 1787-1840 (`LoadBOEDetailsForConsolidated`) | Container-keyed BOE lookup |
| same | 1842-1927 (`LoadBOEDetails`) | Declaration-keyed BOE lookup with first-container as route key |
| same | 1969-2030 (`RefreshOverallDecision`) | Decisions container-scoped (consolidated) vs per-container-of-declaration |
| `Pages/ImageAnalysis/Workbench.razor` | 803-825 | Builds dialog parameters, propagates `IsConsolidated` flag |

Each site silently 404s or returns empty `PagedResult` if upstream
`IsConsolidated` is wrong (mistagged ingest, admin SQL fix-up, race between
queue-entry write and BOE UPDATE). Yesterday's blank-tabs symptom on
`41225848361` was exactly this. 2.16.3 hardened the upstream queue-entry
tagging (`ReadyGroupsCacheService.UpsertQueueEntryAsync`), but the dialog's
disambiguation contract is unchanged.

### 1.2 Backend has the same shape twice

`ContainerDetailsController.GetICUMSData:826-947` already implements four
branches inside one endpoint: `?boeDocumentId=N`, `?declarationNumber=X`
(post-2.16.2 — `!IsConsolidated` filter removed at :891-902, sister fix to
`4c4931c`), no-qualifiers-consolidated, and no-qualifiers-non-consolidated
fallback via `ContainerCompletenessStatus`. It is a working dispatcher, but
it lives in the wrong place — it dispatches on `BoeDocument.IsConsolidated`,
which is a data-quality field, not a routing field. The container-keyed and
declaration-keyed paths also have different "no data" semantics (404 with
`?full=true`, 200+empty otherwise — finding 6.03).

### 1.3 The queue-entry's `IsConsolidated` is derived, not authoritative

`analysisqueueentries.isconsolidated` is populated post-2.16.3 from the BOE
row via `ReadyGroupsCacheService.UpsertQueueEntryAsync:469-475`. Probe
shows 10 of 10 active queue rows are `isconsolidated=False`. A mistag at
ingestion or an UPDATE dropping `MasterBlNumber` (finding 5.01) flows
straight into the queue row and into the dialog routing decision. Lossy
chain; not safe as a routing primitive.

### 1.4 Distribution of GroupType (probe)

`C:\temp\nscim-probe\AgGroupTypeDistro.cs` returned (live, today):

```
=== AG GroupType distribution ===
grouptype  n     distinct_scanners  with_norm  with_rcs
BL         2738  2                  2738       2046

=== AG -> RCS join shape (sample non-null RCS) ===
grouptype  scannertype  ags   rcs_ids  rcs_with_pattern_a
BL         ASE          1937  1937     560
BL         (null)       59    59       1
BL         FS6000       50    50       2

=== AG identifier shape ===
grouptype  total  looks_like_container  looks_like_declaration  long_form
BL         2738   676                   2046                    0

=== Pattern A: RCS rows with ContainerGroupKey set ===
pattern_a  not_pattern_a  total
2774       15821          18595
```

Three observations:

1. **All 2,738 AGs have `GroupType='BL'`.** The field is uniform — branching
   on `GroupType` is a no-op today. Real dispatch lives in
   `RecordCompletenessStatus` (`DeclarationNumber` + `BlNumber` +
   `ContainerGroupKey` + Pattern A) and in the **shape of `GroupIdentifier`**.
   Open question §6.
2. **75% of AGs link to RCS.** RCS is the canonical record per
   `RecordCompletenessStatus.cs:8-27`. The 25% without RCS are pre-1.15.0
   legacy, handled via fallback.
3. **2,774 RCS rows are Pattern A** (multi-decl single-container). The
   dialog has no branch for this today.

---

## 2. Data shape the dialog needs

The dialog has four tabs (Summary / Scanner Data / ICUMS Data / Image &
Decisions, plus House BLs as a sub-tab in the consolidated case) and
fires 5+ network requests on init across them. A unified DTO that pre-loads
everything in one round-trip:

```csharp
// src/NickScanCentralImagingPortal.Core/DTOs/CargoGroup/CargoGroupFullDto.cs
public sealed class CargoGroupFullDto
{
    // ── Identity (server-resolved, never the raw GroupIdentifier) ──
    public Guid              AnalysisGroupId          { get; init; }
    public string            DeclarationNumber        { get; init; } = "";
    public string?           MasterBlNumber           { get; init; }
    public string?           ContainerGroupKey        { get; init; }
    public CargoGroupingMode GroupingMode             { get; init; }
    public string?           ScannerType              { get; init; }
    public string?           ClearanceType            { get; init; }
    public string?           RegimeCode               { get; init; }

    // ── Membership ──
    public IReadOnlyList<string> ContainerNumbers     { get; init; } = Array.Empty<string>();
    public IReadOnlyList<HouseBLDetail> HouseBls       { get; init; } = Array.Empty<HouseBLDetail>();

    // ── Tab-shaped payloads ──
    public ScannerDataPayload      ScannerData         { get; init; } = new();
    public IcumsDataPayload        IcumsData           { get; init; } = new();
    public ImageDecisionsPayload   ImageDecisions      { get; init; } = new();
    public CargoSummaryPayload     Summary             { get; init; } = new();

    // ── Server-classified status (replaces "200 + empty == ???") ──
    public CargoGroupResolutionStatus Status           { get; init; }
    public IReadOnlyList<string> Diagnostics           { get; init; } = Array.Empty<string>();
}

public enum CargoGroupingMode
{
    SingleDeclarationSingleContainer,    // ~most common: 1 decl, 1 cn
    SingleDeclarationMultipleContainers, // non-consolidated, multi-cn
    ConsolidatedMultiHouseBl,            // 1 cn, many decls/HBLs (current "consolidated")
    PatternAUsedCars,                    // RCS.ContainerGroupKey set: multi-decl, 1 cn
    LegacyOrUnknown                      // pre-1.15.0 AG with no RCS link
}

public enum CargoGroupResolutionStatus
{
    Found,                  // canonical record located, all data present
    FoundButPartial,        // record found, some payloads empty (e.g. no scans yet)
    GroupIdentifierUnknown, // input did not resolve to any RCS or AG
    AmbiguousNeedsHint      // input matched multiple records; client must pass ?scannerType=
}
```

Why this shape:

- **Identity is server-resolved.** The client passes the opaque
  `GroupIdentifier` it has from the queue entry; the server returns
  every related identity it determined. The client never has to "figure
  out which key is which."
- **Pattern A is first-class** (`PatternAUsedCars`) instead of being
  silently squashed into the consolidated branch.
- **Status is explicit** (`CargoGroupResolutionStatus`). Distinguishes
  "container number unknown" (404-equivalent) from "container exists but
  no scans yet" — which fixes findings 6.02, 6.03 in one shot.
- **Tab-shaped sub-objects** match what the four UI tabs already render,
  so frontend integration is "set fields from response" not "re-derive."

---

## 3. Proposed endpoint

```
GET /api/cargogroup/{groupIdentifier}/full
    ?scannerType={ASE|FS6000}    (optional disambiguator; required when AmbiguousNeedsHint)
    &includeImages=true|false    (default true)
    &includeScannerData=true|false
    &includeIcums=true|false
    &pageSize=1000               (passes through to inner queries)
```

### 3.1 Controller pseudocode

```csharp
// src/NickScanCentralImagingPortal.API/Controllers/CargoGroupController.cs
[HttpGet("{groupIdentifier}/full")]
[Authorize]
[ProducesResponseType(typeof(CargoGroupFullDto), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(CargoGroupFullDto), StatusCodes.Status404NotFound)]
public async Task<ActionResult<CargoGroupFullDto>> GetFull(
    string groupIdentifier,
    [FromQuery] string? scannerType = null,
    [FromQuery] bool includeImages = true,
    [FromQuery] bool includeScannerData = true,
    [FromQuery] bool includeIcums = true,
    [FromQuery] int pageSize = 1000,
    CancellationToken ct = default)
{
    // 1) Resolve identity. Dispatch order, all in nickscan_production:
    //    a) Try AnalysisGroups by Id (when groupIdentifier is a Guid)
    //    b) Try AnalysisGroups by NormalizedGroupIdentifier
    //    c) Try AnalysisGroups by GroupIdentifier (date-suffixed display form)
    //    d) Try RecordCompletenessStatus by DeclarationNumber
    //    e) Try RecordCompletenessStatus by ContainerGroupKey  (Pattern A!)
    //    f) Try IcumDownloadsRepository.BOEDocuments by ContainerNumber
    //       (covers legacy AG-less rows)
    //    Each branch returns: AGId? + RCS? + ResolvedDeclaration + ResolvedMasterBl
    var resolution = await _resolver.ResolveAsync(
        groupIdentifier, scannerType, ct);

    if (resolution.Status == CargoGroupResolutionStatus.GroupIdentifierUnknown)
        return NotFound(new CargoGroupFullDto { Status = resolution.Status,
                                                Diagnostics = resolution.Diagnostics });

    // 2) Classify the grouping mode from RCS shape, NOT from BOE.IsConsolidated.
    //    SingleDeclSingleCn | SingleDeclMultiCn | ConsolMultiHbl | PatternA | Legacy
    var mode = ClassifyMode(resolution);

    // 3) Fan out to existing services in parallel.
    //    Membership: containers, HBLs.
    //    ScannerData: from Fs6000Scans / AseScans (filtered by ContainerNumbers).
    //    IcumsData:  from BOEDocuments (filtered by RCS-derived key set, NOT by
    //                a single container number when the mode is multi-cn).
    //    ImageDecisions: from ImageAnalysisDecisions (filtered by ContainerNumbers).
    //    Summary: ai cargo summary, status rollups.
    var (containers, hbls, scanner, icums, images, summary) =
        await Task.WhenAll(...);

    // 4) Build status: Found / FoundButPartial / AmbiguousNeedsHint.
    return Ok(new CargoGroupFullDto { ... });
}
```

### 3.2 Resolution rules (replaces frontend's `IsConsolidated` branch)

The classifier walks RCS first; **`BoeDocument.IsConsolidated` is no longer
authoritative for routing**:

| RCS state | Mode |
|---|---|
| RCS exists, `ContainerGroupKey` set | `PatternAUsedCars` |
| RCS exists, exactly 1 expected container, 1 BOE row | `SingleDeclarationSingleContainer` |
| RCS exists, >1 expected container, all share 1 declaration | `SingleDeclarationMultipleContainers` |
| RCS does not exist; container has many BOEs with same ContainerNumber and distinct HBLs | `ConsolidatedMultiHouseBl` |
| Nothing resolves but BOE rows exist | `LegacyOrUnknown` (best-effort) |

`BoeDocument.IsConsolidated` is read-only diagnostic data carried in the
response (not used as input to dispatch).

### 3.3 No new DB queries — re-use existing services

- `ICargoGroupService.GetCargoGroupAsync` already aggregates per-mode data
  for the existing `GET /api/cargogroup/{id}` endpoint
  (`CargoGroupController.cs:255-388`).
- `IIcumDownloadsRepository.GetBOEDocumentsByContainerNumberAsync` already
  returns the multi-row form needed for HBL aggregation.
- `IcumDownloadsDbContext.BOEDocuments.Where(b => b.DeclarationNumber == X)`
  is the post-2.16.2 declaration-keyed lookup used by
  `ContainerDetailsController.GetICUMSData:891-902` (`!IsConsolidated`
  filter already removed).
- `AuditReviewController.GetAuditGroup` (line 221+, in
  `Controllers/AuditReviewController.cs`) is the closest existing
  multi-tab aggregator; its container-completeness join is the model.

The new endpoint is a **composition** of those services with a new
classifier. No new SQL.

---

## 4. Migration path

### Phase 1 — Parallel implementation (Sprint Group F, ~2 days)

1. Add the `CargoGroupFullDto` and `CargoGroupingMode` types to
   `Core/DTOs/CargoGroup/`.
2. Add `IGroupResolver` + `GroupResolver` to `Services/CargoGrouping/`.
   Resolver lives behind an interface so legacy paths can adopt it
   incrementally.
3. Add `GetFull` action to the existing `CargoGroupController` (no new
   controller — re-uses `Authorize` + DI registrations).
4. Add a feature flag `CargoGroup:UseFullEndpoint` (default `false` in
   `appsettings.json`, `true` in `appsettings.Development.json`) so the
   dialog can switch over without coupling to an API release.
5. Keep all existing endpoints working unchanged.

**No frontend changes in Phase 1.** Backend ships dark, exercised by
integration tests + a manual probe (see §5 below).

### Phase 2 — Frontend cutover (Sprint Group F, ~1 day)

1. `ImageAnalysisViewDialog.razor`:
   - Replace the 5 `OnInitializedAsync` `Task.Run` calls (lines 1167-1234)
     with a single `await ApiService.GetAsync<CargoGroupFullDto>(
     $"/api/cargogroup/{Uri.EscapeDataString(GroupIdentifier)}/full?scannerType={ScannerType}")`.
   - Drop `BuildContainerEndpointUrl` (lines 838-872).
   - Drop `_groupIdentifierCache` (line 777-779) — server now does
     resolution; client cache becomes a tenant-leaky liability.
   - Remove `IsConsolidated` from the `[Parameter]` set; treat it as a
     display-only field surfaced by the response.
2. `Workbench.razor:803-825`: stop passing `IsConsolidated` into the dialog.
3. `ScannerDataTab.razor`: render server-resolved status instead of the
   "no data == unknown" surface (closes 6.02).
4. `ICUMSDataTab.razor`: render server-resolved status (closes 6.03).
5. Set `CargoGroup:UseFullEndpoint=true` in production
   `appsettings.json` and ship.

**Validation:** open the same record under both flag states, diff the
rendered tabs.

### Phase 3 — Deprecate / delete (next sprint)

1. Mark `[Obsolete]` on:
   - `GET /api/containerdetails/icums/{cn}` (the legacy paginated path)
   - `GET /api/consolidatedcargo/container/{cn}/housebls`
   - `GET /api/consolidatedcargo/declaration/{decl}/containers`
   - `GET /api/cargogroup/by-container/{cn}` (replaced by /full's identity
     resolution)
2. Audit log usages externally. Once two release windows pass with no hits
   to the obsolete endpoints, delete the controller actions and their
   service methods.
3. Delete `BuildContainerEndpointUrl` if any other call sites remain.
4. Remove `BoeDocument.IsConsolidated` from the routing-input role
   (it remains as a diagnostic column, not a router).

---

## 5. MasterBlNumber backfill safety post-Phase-1

`REPORT.md` Group D step 4 is "for every row with `IsConsolidated=true`
and `MasterBlNumber IS NULL`, parse `RawJsonData` for the master BL and
update." That backfill is **blocked today** because:

- The dialog's consolidated path keys off `IsConsolidated=true` and
  routes via `GroupIdentifier-as-container-number`.
- Backfilling `MasterBlNumber` does not change `IsConsolidated`, so no
  routing change. **Good** — but…
- Re-running ICUMS ingestion (or any path that re-derives the
  `IsConsolidated` flag from a now-non-null `MasterBlNumber`) would flip
  rows from `IsConsolidated=false` to `IsConsolidated=true`, re-routing
  them via the consolidated path. **That recreates yesterday's blank-tabs
  symptom for any container whose `IsConsolidated=true` is "true now,
  was false before."**

After Phase 1 lands, dispatch is server-side and keyed on RCS
(`DeclarationNumber`, `ContainerGroupKey`) plus `BoeDocument` cardinality,
not `BoeDocument.IsConsolidated`. The backfill can then proceed safely:
populating `MasterBlNumber` and refreshing `IsConsolidated` no longer
changes which API path the dialog calls or what data it shows.

**Concrete gate:** the backfill SQL can run after Phase 1 ships and the
feature flag is on in production. Phase 2 frontend cutover is a hard
dependency only because Phase 1 alone keeps the legacy path alive — and
the legacy path still has the routing fragility.

---

## 6. Open questions

1. **`AG.GroupType` uniformly `'BL'` across all 2,738 rows.** Brief asked
   to dispatch on `AG.GroupType + NormalizedGroupIdentifier`; field is
   single-valued in prod. Design dispatches on RCS shape instead and
   treats `GroupType` as legacy. Confirm with originator whether
   `GroupType` is reserved for future use.
2. **25% of AGs (692) have no RCS link** — pre-1.15.0 legacy. 59 also have
   no `ScannerType`. Handled via `CargoGroupingMode.LegacyOrUnknown`;
   confirm whether these should be backfilled or left orphaned.
3. **Pattern A is rare in active workflow.** 2,774 RCS rows have
   `ContainerGroupKey`, but only 563 surface as AGs (560 ASE, 1 unknown,
   2 FS6000). Phase 1 can render Pattern A the same as
   `ConsolidatedMultiHouseBl` until a real workflow exists.
4. **`(GroupIdentifier, ScannerType)` uniqueness.** Probe shows
   `scannertype` distinct=2. Verify in `ImageAnalysisOrchestratorService`
   that AGs are never merged across scanner types; if they can be,
   `?scannerType=` is a hint not a key (`AmbiguousNeedsHint`).
5. **Auth scope.** `[Authorize]` already on `CargoGroupController:17`.
   Confirm `Diagnostics` payload does not leak admin-only counts.
6. **Deprecation of `ConsolidatedCargoController`** — both actions are
   subsumed. Audit prod access logs in Phase 3 before deletion.
7. **Pagination.** `pageSize=1000` matches existing call sites; max
   observed ~12 HBL × 1 row well below cap. Add `Truncated` flag if
   doubt persists.
8. **`ContainerDetailsController.GetICUMSData` declaration path** stays
   useful as an internal service during Phase 2 — only `[Obsolete]` once
   `IIcumDownloadsRepository` is the canonical entry point.

---

## 7. Risk register

| Risk | Likelihood | Mitigation |
|---|---|---|
| Resolver ambiguity (`scannerType` not unique) | Med | Explicit `AmbiguousNeedsHint`; client retries with hint. |
| Flag ships off, never flipped | Low | Phase 2 PR is the flag flip. |
| Dialog lifecycle coupling | Med | Keep tab structure; only swap data sources behind `OnInitializedAsync`. |
| Resolver latency | Low | All lookups by indexed PK/FK; Phase 1 includes a 3 s probe. |
| Stale `_groupIdentifierCache` | Low | Phase 2 deletes it (closes 6.17). |
| Pattern A rendering | Med | Render same as Consolidated until a real workflow exists. |
| Backfill timing (§5) | High | Hard-gate behind Phase 1 ship; document in `DEFERRED_ACTIONS.md`. |

## 8. Pointers

- Probe: `C:\temp\nscim-probe\AgGroupTypeDistro.cs` (route
  `aggrouptypedistro`). Read-only.
- Sprint home: `REPORT.md` §6 Group F.
- Phase 1 unblocks: §5 backfill, 6.17 cache removal, 6.02/6.03 status
  distinguishability, 5.09 mistagging symptom.
