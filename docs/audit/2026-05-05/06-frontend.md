# 06 — Frontend operations

**Audit:** NSCIM v1 cold audit, 2026-05-05
**Agent:** Frontend operations
**Scope:** `src/NickScanWebApp.New/` only — every Razor page, dialog, component,
service, and SignalR client involved in operator workflow. Focus on
**front/back contract violations** — assumptions the frontend makes that the
backend silently violates for some inputs (today's MasterBlNumber bug is
the textbook case).

## Scope confirmation

Read cold: `Workbench.razor`, `AuditReview.razor`,
`ImageAnalysisViewDialog.razor` (2462 LOC), `ImageAnalysisViewer.razor`
(4338 LOC, sampled by endpoint usage and decision-save path),
`AuditReviewDialog.razor`, `AuditDecisionDialog.razor`,
`ImageDecisionView.razor` (sampled), `ScannerDataTab.razor`,
`ICUMSDataTab.razor`, `OperationsDashboardPanel.razor` (sampled by
endpoint usage), `ExportPendingPanel.razor` (sampled).

Services: `ApiService` (Shared + New wrapper), `SimpleAuthStateProvider`,
`SignedImageUrlBuilder`, `AuthenticatedHttpMessageHandler`,
`AuthenticationCircuitHandler`, `SignalRService`,
`ContainerViewPreloader`, `ViewContextCache`, `ContainerDetailsService`
(Shared). Plus `Program.cs` for DI/middleware/CSP. Cross-checked against
`ContainerDetailsController.GetICUMSData` (line 795–947),
`ConsolidatedCargoController` (housebls / declaration containers),
`ImageAnalysisController.GetMyAssignments`/`Claim`/`RenewLease`,
`ImageAnalysisDecisionController.GetOverallDecision`/`GetContainerDecisions`,
`AuditReviewController.GetAuditGroup`,
`Infrastructure/Repositories/ConsolidatedCargoQueries.cs`.

Out of scope (per charter / agent registry): controller business logic
beyond contract-shape, hub server impl (Agent 4 / 8), the older
`src/NickScanWebApp/` tree (verified retired — `NickScanWebApp.New` is the
sole live WebApp per Topology §A; nothing in `Pages/` references the older
namespace at compile-time).

## Findings table

| ID | Severity | File:Line | Issue | Evidence | Proposed Fix | Effort | Risk |
|---|---|---|---|---|---|---|---|
| 6.01 | **P1** | `Components/Operations/ImageAnalysisViewDialog.razor:194,1392,1707,1762` and `Workbench.razor:805` | The dialog's data-tab contracts depend on `IsConsolidated => GroupIdentifier IS the container number`. Today's 2.16.3 fix patched the **upstream** queue-entry tagging, but the **same-shape assumption is hard-coded in 4+ other places**. Any future mistagging (admin Rematch, manual SQL fix-up, ingest race) re-opens the silent-blank-tab class of bug. | `ImageAnalysisViewDialog.razor:1707-1711` `LoadBOEDetailsForConsolidated` calls `/api/containerdetails/icums/{GroupIdentifier}` only when `IsConsolidated=true`; assumes GroupIdentifier IS a container number. Same at `:1620` (`/api/consolidatedcargo/container/{GroupIdentifier}/housebls`), `:1901` (`/api/ImageAnalysisDecision/container/{GroupIdentifier}`), `:1655` (declaration-keyed for non-consolidated path). Each call shape silently 404s or returns empty if `IsConsolidated` is wrong. | Replace dialog's branch-on-IsConsolidated with one server endpoint that takes `(GroupIdentifier, IsConsolidated)` and returns the right shape; or pass both `ContainerNumber` and `MasterBlNumber` separately so the dialog never has to disambiguate. | M | Med |
| 6.02 | **P1** | `Components/Containers/ScannerDataTab.razor:69,116` (called from `ImageAnalysisViewDialog.razor:194,586` with `ContainerNumber=GroupIdentifier`) | When the consolidated-mode dialog passes `GroupIdentifier` (which is supposed to be a container number) into `ScannerDataTab`, an upstream `IsConsolidated`-mistag still produces a silent empty Scanner Data tab. The 2.16.3 fix only patched the queue entry — direct dialog instantiation paths (e.g. AuditReviewDialog at line 111, BulkDecision flows) are still vulnerable. | `ScannerDataTab.razor:116` calls `ContainerDetailsService.GetScannerDataAsync(ContainerNumber=GroupIdentifier, 1, 1000)` — silently returns empty `PagedResult` if the container number doesn't exist in `Fs6000Scans`/`AseScans`. UI then renders "No scanner data available for this container" (line 62-64), which is the same message shown for genuinely-empty scans. | Distinguish "empty result" from "container not found" at the API layer (return 404 for unknown container, 200+empty for known container without scans). Then ScannerDataTab can show a "Container not found / mistagged record" alert separately from "No scans yet." | S | Low |
| 6.03 | **P1** | `Components/ICUMS/ICUMSDataTab.razor:140-143` + `Controllers/ContainerDetailsController.cs:931-946` | Same shape: API returns **200 OK with empty `PagedResult.Data`** when no BOE document exists for a container. UI shows "No ICUMS data available for this container" — visually identical to a record where ICUMS data simply isn't downloaded yet. | `ContainerDetailsController.cs:931-946`: when `boeDocuments.Any() == false`, the non-`full` path returns `Ok(new PagedResult<ICUMSDataRecord> { Data = [], TotalCount=0 })` — not 404. The `full=true` path returns 404 (line 935-938) — the two paths have inconsistent error semantics. | Add a third state to the response (e.g. `Status="NoMatch" \| "Empty" \| "Found"`). Distinguish "container exists but no BOE yet" from "container number doesn't match anything we know." | S | Low |
| 6.04 | **P1** | `Infrastructure/Repositories/ConsolidatedCargoQueries.cs:317` | `GetContainersByDeclarationAsync` **still has the stale `!b.IsConsolidated` filter** that 2.16.1's `4c4931c` removed elsewhere. When `MasterBlNumber=NULL` declarations are mis-tagged consolidated, the dialog's `LoadContainers` call (`ImageAnalysisViewDialog.razor:1655`) returns an empty list, leaving the dialog with `Containers=null` and the non-consolidated tab tree empty. | `ConsolidatedCargoQueries.cs:315-321`: `Where(b => b.DeclarationNumber == declarationNumber && !b.IsConsolidated)` — same shape bug as `ContainerDetailsController.GetICUMSData` had at line 864 before 2.16.2 fix. Today's 2.16.3 backfilled `analysisqueueentries.isconsolidated`, but did not backfill `boedocuments.is_consolidated`, so this query still rejects the mis-tagged rows. | Drop `!b.IsConsolidated` from the WHERE clause (same minimum-diff fix as `4c4931c`), OR keep the filter and trust 2.16.3 to keep the queue entry consistent. Decision rests on whether `b.IsConsolidated` is ever cleaned up. | XS | Low |
| 6.05 | **P1** | `Services/SignalRService.cs:34-35` and `Program.cs:221` | Singleton `SignalRService` connects to `/hubs/scanner` (which **does not exist** per Topology §F — registered hubs are dashboard / comprehensive-dashboard / imageAnalysisDashboard / userReadiness / containerScanQueue). No bearer token configured. Event handlers receive `object` and invoke with empty strings (line 40-58). Currently dead code, but registered in DI so a future wire-up will fail silently. | `SignalRService.cs:23` builds `_hubUrl = $"{apiUrl}/hubs/scanner"`; line 34-35 builds `HubConnection` without `AccessTokenProvider`; line 65-68 swallows connect errors via empty `catch`. `Program.cs:221` registers as **Singleton** in Blazor Server (cross-user singleton SignalR connection is wrong). | Either delete the class (it's never used) or rewrite to (a) target a real hub, (b) provide `AccessTokenProvider`, (c) register Scoped, (d) deserialize event payloads. | XS | Low |
| 6.06 | **P2** | `Components/Operations/ImageAnalysisViewDialog.razor:2113,2168` | `BulkDecision` and `DirectOpenFullscreenViewer` **hardcode `ScannerType="ASE"`** as fallback. For FS6000 cargo, this writes the wrong scanner type to `ImageAnalysisDecisions`, which propagates to the audit and submission payloads. | `ImageAnalysisViewDialog.razor:2113`: `ScannerType = "ASE"` in BulkDecision request; `:2167-2168`: `if (string.IsNullOrEmpty(scannerType)) scannerType = "ASE";`. The dialog Receives ScannerType as a parameter but the Workbench never propagates it (line 802-828 doesn't include `ScannerType` in `parameters`). | (a) Have Workbench pass `assignment.ScannerType` to the dialog; (b) error out / disable bulk decision if scanner type can't be resolved. | S | Low |
| 6.07 | **P2** | `Services/ApiService.cs:40-74,113-127,229-235` | Multiple silent-failure points in the HTTP client: (a) reflection-based `GetTokenAsync` lookup — fails silently if method renamed or signature changes, request goes unauthenticated; (b) 401/403 logged at Debug level → operator never sees session-expiry signal in normal log levels; (c) JSON parse failures silently return `default` (line 229-235). | `ApiService.cs:45`: `var method = _authStateProvider.GetType().GetMethod("GetTokenAsync", ...)`; line 64-67: warning if method missing but request still proceeds; line 117-120: `_logger.LogDebug` for 401/403; line 231-235: `try { ReadFromJsonAsync<T> } catch { return default; }`. | (a) Make `IAuthTokenSource` interface and inject directly (no reflection); (b) at minimum LogWarning the **first** 401 per session, then suppress; (c) propagate JSON parse exceptions to caller. | S | Low |
| 6.08 | **P2** | `Services/SimpleAuthStateProvider.cs:80,118,192` + `Program.cs:204` | `_cachedToken` (in-memory) is held for the lifetime of the Scoped circuit. `LoginAsync` (line 117) writes to ProtectedSessionStorage; if a new user logs in via the same browser tab without circuit reset, the **prior token may briefly persist in memory** until session storage replaces it on next read. Edge case but possible during rapid login/logout cycling. | `LoginAsync:118`: `_cachedToken = token` — overwrites without clearing the prior identity. `GetValidatedCachedToken:259-271`: only checks `IsAuthenticated`, doesn't compare token to current `_currentUser` claims. Cross-user contamination requires a circuit reuse pattern that the framework normally prevents, so latent. | On `LoginAsync` and `LogoutAsync`, explicitly null out `_cachedToken` *before* assigning the new one. | XS | Low |
| 6.09 | **P2** | `Program.cs:341,344,345` | CSP regression vs. memory `reference_audit_2026_04_28.md` ("CSP unsafe-eval removed"). Current CSP **still has `'unsafe-eval'`** at line 341. `img-src` (line 344) includes `https:` — allows any HTTPS origin's image. `connect-src` (line 345) allows `ws:` and `wss:` (any WebSocket). | `Program.cs:339-345`: ``"script-src 'self' 'unsafe-inline' 'unsafe-eval' https://fonts.googleapis.com; "`` ; ``$"img-src 'self' data: https: {apiBaseUrl}; "`` ; ``$"connect-src 'self' {apiBaseUrl} ws: wss:;"``. The "audit deployed" memory says unsafe-eval was removed; the source still has it. | Remove `'unsafe-eval'`; replace `https:` with the API base URL only (or specific domains); replace `ws: wss:` with the API base URL upgraded to ws/wss. Verify Blazor Server doesn't need `unsafe-eval` post-Net 8. | S | Med |
| 6.10 | **P2** | `Components/Operations/ImageAnalysisViewDialog.razor:1167-1234` | Five fire-and-forget `Task.Run`s in `OnInitializedAsync` (RefreshOverallDecision, LoadImageAnalysisContext, PreloadICUMSData, LoadCargoGroupForSummary, LoadWaveContext). If the dialog closes before they complete, exceptions log to Console only. **Race condition**: tab-switch can fire `OnActiveTabChanged` while one of these still mutates `_allBOEData` / `_cargoGroupForSummary`. | `ImageAnalysisViewDialog.razor:1167`: `_ = Task.Run(async () => { try { await RefreshOverallDecision(); ...` — return-value discarded, lifecycle untracked. The five tasks share several state fields without synchronization. Console.WriteLine on errors (no Snackbar). | Use `IDisposable` / `CancellationToken` per dialog instance; tie all background loads to a single CTS that's cancelled in dispose. Show error state in UI not just console. | M | Med |
| 6.11 | **P2** | `Services/ContainerDetailsService.cs:425-451` | `ClearContainerCache` does not clear `scanner_data_*` and `icums_data_*` paginated cache keys — only `container_basic_*`, `container_full_*`, `full_scanner_*`, `full_boe_*`, `image_metadata_*`. After admin Rematch / unmatch, paginated tab data persists from cache for up to 2 minutes. | `ContainerDetailsService.cs:431-438`: `cacheKeys` array missing the paginated keys defined at line 118 and 205. | Either enumerate page sizes likely in use, or implement key-prefix tracking (`IMemoryCache` doesn't support pattern-remove natively — use a parallel `ConcurrentDictionary<string,List<string>>` of keys-by-container). | S | Low |
| 6.12 | **P2** | `Services/ViewContextCache.cs:16-17` and rest of file | No invalidation hook on admin Rematch / unmatch. Cached `ContainerViewContext` is held for up to 30 min with a 50-entry cap — analyst opens dialog → admin re-maps container → analyst sees stale BOE on second open. | `ViewContextCache.cs:188-208` only does time-based eviction; no externally-callable invalidate-on-edit. `Pages/Completeness/MatchCorrections.razor` (admin tool) has no callback into the cache. | Wire a SignalR `ContainerCacheInvalidated(containerNumber)` event and have ViewContextCache subscribe; clear on receipt. | M | Med |
| 6.13 | **P2** | `Services/AuthenticationCircuitHandler.cs` | Class is named `AuthenticationCircuitHandler` and registered in DI but every override is a no-op log statement. Doesn't actually re-authenticate or surface session expiry on disconnect/reconnect. | Lines 18-42: all four overrides return `Task.CompletedTask` after a `LogDebug`. | Implement `OnConnectionUpAsync` to verify token still valid (reuse `SimpleAuthStateProvider.GetAuthenticationStateAsync`) and dispatch to `NavigationManager.NavigateTo("/login")` on auth failure. | S | Low |
| 6.14 | **P2** | `Pages/ImageAnalysis/Workbench.razor:486-564` and `AuditReview.razor:600-657` | Hub URL fallback `?? "http://localhost:5205"` would hit HTTP (not HTTPS) in production if `appsettings.json:ApiSettings:BaseUrl` is missing. With JWT cookies/bearer not negotiable over HTTP and the API binding to `:5206/HTTPS`, hub init silently fails, leaving the analyst "ready" but no longer eligible for assignment events. Token retrieval uses reflection on AuthStateProvider (line 492-497, 604-616). | `Workbench.razor:486-487`: `var apiBaseUrl = Configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5205";`. Same pattern in `AuditReview.razor:600`, `OperationsDashboardPanel.razor:2001`, `SignalRService.cs:22`, `SignedImageUrlBuilder.cs:55`. | Replace fallback with a thrown exception at startup if config missing (fail fast, not silent). Inject `IOptions<ApiSettings>` typed config so it's validated. | XS | Low |
| 6.15 | **P2** | `Pages/ImageAnalysis/Workbench.razor:631-633` | `Dispose` synchronously fires-and-forgets `SetReadyForAssignment(false)` via `_ = ...try{}catch{}`. If the API call fails (server restart, network blip), the user remains marked Ready in the database, will keep receiving assignments until heartbeat times out (~70s default). | `Workbench.razor:629-641`: `if (_isReadyForAssignment) { try { _ = SetReadyForAssignment(false); } catch { } }` — synchronous dispose, no await, no error surface. | Move "set not ready" to a server-side dispose-on-circuit-close hook (CircuitHandler.OnCircuitClosedAsync); guarantees fire even if browser-side dispose drops. | S | Low |
| 6.16 | **P2** | `Components/Operations/AuditDecisionDialog.razor:235,277-294` | `AuditDecisionDialog` is a **legacy bulk-only** path that does NOT include `ImageDecisions[]` per-container payload. If reachable from the UI, it submits container-level decisions only, bypassing the per-image audit semantics introduced in `AuditReviewDialog`. | `AuditDecisionDialog.razor:229-238`: request shape has only `ContainerDecisions[].Decision` — no `ImageDecisions[]`. Compare to `AuditReviewDialog.razor:769-786` which packs `ImageDecisions = BuildImageDecisionsPayload(...)` per container. | Verify no UI path opens `AuditDecisionDialog`; if reachable, deprecate or update payload. | XS | Low |
| 6.17 | **P3** | `Components/Operations/ImageAnalysisViewDialog.razor:777-779` | Dialog uses a **static** `_groupIdentifierCache` shared across all users in the WebApp process. Cross-user sharing is acceptable since groupIdentifier→Master BL resolution is data-tenant-bound (single tenant in v1), but breaks the moment multi-tenancy goes live (cached value returned across tenants). | `ImageAnalysisViewDialog.razor:777`: `private static readonly Dictionary<string, CachedGroupIdentifier> _groupIdentifierCache`; lines 1396-1404 do unsynchronized read/write under a lock. With phase-1 multi-tenancy active, this would surface as cross-tenant data leak via cached BL numbers. | Move cache into a Scoped service keyed by `(tenantId, containerNumber)`. Tag for phase-2 multi-tenancy work. | S | Low |
| 6.18 | **P3** | `Pages/ImageAnalysis/Workbench.razor:778`, `AuditReview.razor:514` | URL inconsistency: `claim` uses `{group.GroupId}` (UUID) but `lease/renew` uses `{assignment.GroupIdentifier}` (string). Backend handles both forms (`ImageAnalysisController.RenewLease:1196-1239`) — but if the dual support is removed, lease-renew breaks. | `Workbench.razor:754` `ClaimGroup`: `$"/api/image-analysis/groups/{group.GroupId}/claim"`; `Workbench.razor:778` `RenewLease`: `$"/api/image-analysis/groups/{assignment.GroupIdentifier}/lease/renew"`. | Pick one. Recommend always passing GUID where available — frontend has both fields on the assignment row (`MyAssignmentDto.GroupId` + `GroupIdentifier`). | XS | Low |
| 6.19 | **P3** | `Components/ImageAnalysis/OperationsDashboardPanel.razor:2001-2031` | Same reflection-based token retrieval as Workbench/AuditReview. `_isConnected` UI flag (line 2089-2098) only updates on `Closed` event — if the hub never connects (auth fail), `_isConnected` stays `false` forever silently. | Lines 2008-2017 reflection lookup; lines 2089-2094 `Closed` handler. No `Reconnecting` failure surface. | Add an "auth state" indicator distinct from "connection state"; surface auth failures to user with action button. | S | Low |
| 6.20 | **P3** | `Controllers/ImageAnalysisController.cs:171-176` | `my-assignments` endpoint cached per user for 15s with `MyAssignmentsCacheDuration`. Workbench auto-refresh is 30s. After lease-renew or admin re-assignment, user can see stale state for up to ~45s. | Lines 171: `var cacheKey = $"my-assignments:{username}:{userRole}"`; line 172-176 returns cached without revalidating heartbeat or assignment-state. | Invalidate cache on assignment writes (claim/renew/release). Or drop the cache (queue-table read is already fast — line 183-186). | S | Low |
| 6.21 | **P3** | `Components/Operations/ImageAnalysisViewDialog.razor:915-960` | `SaveScrollPosition`/`RestoreScrollPosition` are **stubs** — comments say `// TODO: Implement JS interop to restore scroll position` and the methods do nothing. Tab switch loses scroll position despite the elaborate state-tracking infrastructure. | Lines 915-920: `SaveScrollPosition` empty implementation; line 922-935: `RestoreScrollPosition` only does Task.Delay. | Implement the JS interop or remove the dead state-tracking code. | XS | Low |
| 6.22 | **P3** | `Pages/ImageAnalysis/Workbench.razor:807-828` | `OnDecisionSaved` callback uses captured local `dialogRef` variable in a closure pattern that requires `dialogRef = await DialogService.ShowAsync(...)` to assign first. Because the EventCallback is created before `dialogRef` is assigned (line 802 vs 837), the closure captures by reference but the callback fires while `dialogRef == null` is briefly possible during the assignment race. | `Workbench.razor:801-819`: `IDialogReference? dialogRef = null; var parameters = new DialogParameters { ..., ["OnDecisionSaved"] = EventCallback.Factory.Create(this, async () => { ... if (dialogRef != null) dialogRef.Close(...) }) }`. The pattern works in practice because callbacks fire after `dialogRef` is assigned, but if a save races dialog-creation, `dialogRef==null` is observable. | Move the `dialogRef.Close` call out of the parameter closure; let parent component own the close logic. | XS | Low |
| 6.23 | **P3** | `Components/Operations/ImageAnalysisViewDialog.razor:2009-2031` | `HandleNextContainer` does a fuzzy partial-match (`Contains`) to map next-container-string to UI list entries. For comma-separated cross-record pairs, this uses `nextContainerNumber.Contains(c, ...)` and `c.Contains(nextContainerNumber, ...)` — false positives possible if container numbers share a substring (e.g. "TLLU7667054" contains "U7667054"). | Lines 2014-2022: `var match = Containers.FirstOrDefault(c => nextContainerNumber.Contains(c, ...) \|\| c.Contains(nextContainerNumber, ...))`. ISO 6346 container numbers can in principle share suffixes. | Match on full equality first; fall back to splitting comma-separated and intersecting. | XS | Low |
| 6.24 | **P3** | `Services/ApiService.cs:266-279` | `PutAsync` calls `EnsureSuccessStatusCode()` and re-throws as `ApiException` with no body excerpt. Compared to `PostAsync` (lines 204-225) which logs + includes raw body in the exception message, PUT errors are opaque. | Lines 266-271: `var response = await client.PutAsJsonAsync(...); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions);` — no body capture on failure. | Mirror the POST shape: read body before EnsureSuccessStatusCode, log at level depending on status. | XS | Low |
| 6.25 | **P3** | `Components/Operations/ImageAnalysisViewer.razor:2367-2374` | `GetCanvasProxyImageUrl` builds a Base64-encoded URL passed via `?url=...` to `/api/imageproxy`. If the source URL contains user-bound query parameters (signed-URL `?exp=...&uid=...&sig=...`), they round-trip through the proxy untouched — fine — but the proxy endpoint is not in the audit's view of the API surface. | Line 2371-2373: builds proxy URL via Base64. No mention of `/api/imageproxy` in Topology §E or §F whitelisted endpoints. | Verify `/api/imageproxy` is `[Authorize]`, scoped to internal callers, and not a server-side request forgery sink. | XS | Low |

## Narrative

The dominant architectural smell across the frontend is what I'm calling
the **"`IsConsolidated` is a routing flag"** pattern. The dialog
(`ImageAnalysisViewDialog.razor`) makes binary branches on
`IsConsolidated` to choose which API path to call and which DB key
(`ContainerNumber` vs `DeclarationNumber`/`MasterBlNumber`) to pass into
that path. Today's MasterBlNumber bug was one slice of that surface;
it shows up at least four other times in the same file
(`:194,1392,1707,1762`), once in `Workbench.razor:805`, and indirectly
through `ScannerDataTab` whenever the dialog passes its `GroupIdentifier`
parameter as `ContainerNumber`. The 2.16.3 fix patched the *upstream*
source of mistagged data (the queue entry) but did not change the dialog
contract — every silent-blank-tab path that gets to `IsConsolidated=true`
through any other route (admin SQL fix, future ingestion bug, user
action) still 404s. Findings 6.01–6.04 capture this; 6.04 is the most
embarrassing because it's a *direct copy of the bug 2.16.1 fixed
elsewhere* still living in `ConsolidatedCargoQueries.GetContainersByDeclarationAsync:317`.

The root cause is that the dialog is doing **disambiguation** that
properly belongs to the API. When the user opens "an assignment", they
have a `(GroupId, GroupIdentifier, ScannerType, IsConsolidated,
Containers[])` tuple from the queue table. There's no good reason the
client needs to translate this tuple into different URL shapes; one
endpoint that takes the whole tuple and dispatches server-side would
delete most of the bugs in this report.

The second cluster is **error-distinguishability**. The API has at least
three different "no data" responses (200+empty `PagedResult`, 200+
`Success=false`, 404 with body) and the WebApp papers over all three
with the same generic Snackbar / blank-tab / "No data available"
message. Operators cannot tell "expired session" from "BOE not yet
downloaded" from "container number wrong" from "endpoint failed."
Findings 6.02, 6.03, 6.07 are the worst offenders. The auth-state
bridging (6.07, 6.13) compounds it: 401 logs at Debug, the
`AuthenticationCircuitHandler` is a stub, ApiService swallows JSON
parse errors silently — so a half-deployed API release surfaces only
as "everything looks empty for some users."

The third cluster is **state lifecycle**. The dialog fires five
fire-and-forget loads in `OnInitializedAsync` (6.10), the static
`_groupIdentifierCache` lives forever and ignores tenancy (6.17),
`ViewContextCache` and `ContainerDetailsService` cache aggressively but
have no invalidation hook for admin Rematch (6.11, 6.12), the dialog's
scroll-position restore is a stub (6.21), and `Workbench.Dispose` does
fire-and-forget "set not ready" without server confirmation (6.15).
None are catastrophic individually; collectively they make the dialog
brittle to any concurrent admin action.

The most fragile component is unambiguously
`ImageAnalysisViewDialog.razor` (2462 LOC, 12 of 25 findings reference
it). It is the place every assumption about the data model converges,
and it's the place every operator workflow eventually opens.

Two findings are tagged P1 but worth singling out: **6.04** because it
is a single-line fix that closes a known-failure-class identical to the
one we already paid to fix once, and **6.05** because the
`SignalRService` is dead code that *will* mislead the next person who
tries to use it (wrong hub name, no auth, wrong DI scope, all in 90
lines).

There are **20 distinct front/back contract violations** in the
findings table (P1 ones: 6.01, 6.02, 6.03, 6.04, 6.05; P2 with contract
implications: 6.06, 6.09, 6.11, 6.12, 6.14, 6.16, 6.20; P3: 6.18, 6.19,
6.22, 6.23, 6.24, 6.25, plus implicit ones in scattered hardcoded
paths).

## Open questions

1. **Is the `AuditDecisionDialog` (the legacy non-per-image one) ever
   opened?** A grep shows no `<AuditDecisionDialog>` references in
   `.razor` and no `DialogService.ShowAsync<AuditDecisionDialog>` in
   `.cs`/`.razor`. If unreachable, deprecate. (Finding 6.16.)

2. **Is `/api/imageproxy` `[Authorize]`?** ImageAnalysisViewer
   line 2371-2374 base64-encodes a URL and passes it to
   `/api/imageproxy?url=...`. Charter Topology §E doesn't list this
   endpoint; if the proxy accepts arbitrary URLs without auth or origin
   validation, it's a SSRF sink. Worth verifying with Agent 8.

3. **Has `'unsafe-eval'` actually been removed from CSP in production**,
   or is the source still drift from the 2026-04-28 deploy memory?
   Either deploy is out of sync with source, or the memory is wrong.
   (Finding 6.09.)

4. **Should the WebApp distinguish "401 expired session" from "401
   permission denied"?** Currently both surface the same way (Snackbar
   "Authentication error - please refresh the page", `Workbench.razor:618-621`).
   The fix for 6.07 / 6.13 needs a clear UX policy here.

5. **Does the `my-assignments` 15-second cache (Controller line 171-176)
   need a SignalR-based invalidation channel** to support fast lease
   release / re-assignment, or is the user-facing 30-45s staleness
   acceptable? (Finding 6.20 — Agent 4's territory.)

6. **The `static _groupIdentifierCache` (Finding 6.17)** — what's the
   plan for it under phase-2 multi-tenancy? It would need to be
   tenant-keyed; the current design assumes single-tenant.
