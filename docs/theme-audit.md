# NickERP Theme Audit & NickFinance Migration Plan

**Date:** 2026-04-28
**Scope:** Portal, NickHR.WebApp, NickFinance.WebApp, NickScanWebApp.New
**Decision required from:** project owner — see §6 *Risks / open questions*.

---

## 1. Verdict on the canonical theme

There is **no single canonical `app.css` today**. Each app has gone its own way:

| App | `app.css` size | What's actually in it | Verdict |
|---|---|---|---|
| `platform/NickERP.Portal/wwwroot/app.css` | 2,463 B | The default Blazor template (`.valid.modified`, `.invalid`, `.blazor-error-boundary`, error icon SVG). **Contains zero brand styling.** | Empty. The visual system lives elsewhere. |
| `NickHR/src/NickHR.WebApp/wwwroot/app.css` | 1,978 B | Inter font import + Blazor error boundary. Everything else is MudBlazor + scoped razor.css. | Even smaller. Theme is in `MainLayout.razor` `MudTheme` C# block. |
| `finance/NickFinance.WebApp/wwwroot/app.css` | 4,557 B | Hand-rolled bespoke design system: tokens, topnav, cards, tiles, tables, forms, pills. **The most complete `app.css` in the suite.** | Self-contained handcrafted system. |
| `src/NickScanWebApp.New/wwwroot/css/site.css` | 2,810 B | Bootstrap-leaning baseline with open-iconic and `#blazor-error-ui`. Real chrome lives in 6 other CSS files (~75 KB) and MudBlazor. | Legacy; hybrid Bootstrap + MudBlazor. |

**The actual canonical source-of-truth is split across two places:**

1. **`platform/NickERP.Platform.Web.Shared/Branding/`** — already exists, already designed:
   - `theme-tokens.css` — full `--nickerp-*` CSS custom property palette (primary 50–900, module accents, semantic colors, surfaces, sidebar, typography, spacing, radius, elevation, **dark mode `[data-theme="dark"]`**). This is the **declared** canonical token set.
   - `BrandConstants.cs` — `FullName="NICKSCAN ERP SOLUTION"`, module names, `Colors.Primary="#1d4ed8"`, `Colors.ModuleFinance="#059669"`.
2. **`platform/NickERP.Portal/Components/Pages/Home.razor`** (`<style>` block + `AppCard.razor` + `StatCard.razor`) — the **realised** look users see at `erp.nickscan.net`. Uses a parallel `--nep-*` token set with primary `#4F46E5` (indigo). Defines: navbar, hero, stat groups, app card grid, footer, breadcrumb pill, health pill, kbd chip, avatar.

These two **do not agree on colors** (`#1d4ed8` blue tokens vs `#4F46E5` indigo realised). Portal does not reference `Platform.Web.Shared`.

**Evidence the Portal Home.razor pattern is the de-facto canonical look:**
- It's at the entry point hostname (`erp.nickscan.net`).
- It's the most recently iterated and contains the richest component vocabulary (stat groups, app cards, hero with health pill).
- Its `--nep-*` variable naming is internally consistent across `Home.razor`, `AppCard.razor`, and `StatCard.razor`.
- Even though NickHR and NSCIM use MudBlazor and don't echo this CSS, Portal is what a new app should look like — it sets visual tone for the suite.

**Recommendation:** **promote Portal's `--nep-*` set into `Platform.Web.Shared/Branding/theme-tokens.css` as the canonical token set, replacing the current `--nickerp-*` declarations.** Then make Portal, NickFinance, and any future apps consume that file. The existing `theme-tokens.css` is already plumbed; we just need to align its values with reality and start using it.

---

## 2. Design tokens (canonical, after consolidation)

Extracted from `Portal/Components/Pages/Home.razor` `:root` block. These are the values to standardise on:

| Token | Value | Usage |
|---|---|---|
| `--nep-bg` | `#F8FAFC` | App background (slate-50) |
| `--nep-surface` | `#FFFFFF` | Cards, navbar, hero |
| `--nep-border` | `#E2E8F0` | Default card/input border |
| `--nep-border-soft` | `#F1F5F9` | Internal dividers |
| `--nep-text` | `#0F172A` | Primary text (slate-900) |
| `--nep-text-muted` | `#64748B` | Secondary text |
| `--nep-text-faint` | `#94A3B8` | Placeholder, captions |
| `--nep-primary` | `#4F46E5` | Indigo — links, active nav, focus ring |
| `--nep-primary-soft` | `#EEF2FF` | Tinted primary backgrounds |
| `--nep-success` | `#10B981` | "Operational", positive states |
| Brand gradient | `linear-gradient(135deg,#4F46E5,#7C3AED)` | Logo, avatars |
| Module-finance accent | `#059669` (from `BrandConstants`) | NickFinance card / pill |
| `--nep-shadow-sm` | `0 1px 2px rgba(15,23,42,0.04), 0 1px 3px rgba(15,23,42,0.06)` | Subtle |
| `--nep-shadow` | `0 1px 3px rgba(15,23,42,0.06), 0 4px 12px rgba(15,23,42,0.04)` | Default |
| `--nep-shadow-lg` | `0 4px 6px rgba(15,23,42,0.04), 0 12px 32px rgba(15,23,42,0.08)` | Hovered cards |
| Font family | `'Inter', -apple-system, 'Segoe UI', sans-serif` | Body — explicitly loaded via Google Fonts in Portal `App.razor` |
| Font weights used | 400, 500, 600, 700, 800 | |
| Radius (sm/md/lg/xl) | `5px` / `8px` / `10px` / `12px` (and `999px` for pills) | Cards = 12, inputs = 5–8 |
| Spacing pattern | 4 / 6 / 8 / 10 / 12 / 14 / 16 / 20 / 24 / 32 px | Multiples of 4; not strictly tokenised |
| Breakpoint | `@media (max-width: 768px)` | Single mobile rule on Portal |
| Dark mode | **Declared** in `Platform.Web.Shared` (`[data-theme="dark"]`); **not yet implemented** by Portal Home. NickHR has it via MudBlazor `IsDarkMode`. NickFinance has none. |

NickFinance's existing `app.css` already uses a near-compatible palette (`#f8fafc`, `#0f172a`, `#475569`, `#1d4ed8`) — but its primary is `#1d4ed8` blue, not Portal's `#4F46E5` indigo. This is the single biggest visible delta.

---

## 3. Component patterns

Where each pattern currently lives and what to share:

| Pattern | Portal | NickFinance | Promote to `Platform.Web.Shared`? |
|---|---|---|---|
| Card surface | `<div class="nep-card">` (in `AppCard.razor` scoped `<style>`) | `<section class="card">` (global `app.css`) | Yes — single `<NepCard>` Razor component |
| Stat tile | `StatCard.razor` (rich, with icon, badge, sublabel, loading skeleton, color tint) | `<div class="tile">` (3 lines of HTML, no icon) | **Yes — adopt Portal's `StatCard`** wholesale |
| App grid card | `AppCard.razor` (active/disabled, gradient, chip) | n/a | Yes — `<AppCard>` |
| Status pill | none global | `Pages/StatusPill.razor` + `.pill.ok / warn / bad` in `app.css` | Yes — Finance's pattern is the cleanest; promote |
| Tables | n/a (Portal is dashboard-only) | `<table>` styled in `app.css` (uppercase th, slate borders, `tr.total`, `.right`) | Yes — promote Finance's pattern; add `<NepTable>` thin wrapper |
| Form inputs | n/a | Plain `<input>` + `<label>` styled via `app.css` selectors | Yes — promote Finance's pattern |
| Buttons | `.nep-nav-icon-btn` (icon-only) | `button.primary`, `button.secondary`, `a.primary` | Merge: keep Finance's class names, add Portal's variants |
| Empty state | n/a | Inline `<tr><td colspan=".." class="muted">No vouchers yet…</td></tr>` | Yes — `<NepEmptyState>` component |
| Error banner | n/a | `<div class="error">@_error</div>`; `.card.warning` for sandbox notices | Yes |
| Toast | n/a (Portal hasn't needed) | n/a | Punt — pull MudBlazor `Snackbar` later, or roll one in shared |
| Page header | inline `<h1>` + `<p class="lede">` | same | Promote `<NepPageHeader Title Subtitle>` |
| Money formatter | `Money(decimal v) => $"GH₵{v:N0}"` (Portal, private) | `((v / 100m).ToString("N2"))` everywhere | **Yes — shared `<Money Minor>` component**; high-leverage |

---

## 4. Layout shape NickFinance should adopt

NickFinance's current `MainLayout.razor` is a thin top-nav + content layout. NickHR is full-app MudBlazor (drawer, app bar, snackbar). Portal Home.razor is a hand-rolled hero+grid. **NickFinance should keep its top-nav structure** — it's the right shape for a finance module — and only swap classes/tokens.

Target layout:

```razor
<div class="nep-page">                              @* swap "page" → "nep-page" *@
  <header class="nep-nav">                          @* swap "topnav" → "nep-nav" *@
     [logo gradient block] [nav-links] [search] [notifications] [user pill]
  </header>
  <main>
    <article class="nep-content">
      @Body
    </article>
  </main>
  <footer class="nep-footer">…</footer>
</div>
```

Specifically:
- Brand mark stays "NickFinance" with tagline "Finance · NickERP" — but render through a shared `<TopNav Brand="NickFinance" Module="@BrandConstants.Modules.Finance">` component so the mark/tag/avatar/user-pill come from one place.
- Add the **app switcher dropdown** that's listed as a "quick win" in `ROADMAP.md §6` (Track A pre-A.6 stopgap). Without it, jumping between Finance and HR is a manual URL change.
- Optional: bottom footer linking to `/admin/audit-logs` and version (matches Portal's nep-footer).
- The user identity block (`who-name` / `who-email`) keeps the same shape but moves into the shared `UserMenu` component, with a logout path through `/cdn-cgi/access/logout`.

---

## 5. Migration plan

Ordered by dependency. Sizing assumes one engineer.

### Phase 1 — token consolidation (½ day)
1. **Reconcile `theme-tokens.css`.** Replace the `--nickerp-*` palette in `platform/NickERP.Platform.Web.Shared/Branding/theme-tokens.css` with the realised `--nep-*` values from Portal `Home.razor`. Keep both prefix sets aliased (`--nickerp-primary: var(--nep-primary)`) so `BrandConstants.Colors.*` references don't break.
2. Add `<link href="_content/NickERP.Platform.Web.Shared/Branding/theme-tokens.css" rel="stylesheet" />` to NickFinance `App.razor` (and Portal `App.razor`). Static-web-asset wiring already works because the project type is `Microsoft.NET.Sdk.Razor`.

### Phase 2 — NickFinance project ref + token swap (½ day)
3. Add `<ProjectReference Include="..\..\platform\NickERP.Platform.Web.Shared\NickERP.Platform.Web.Shared.csproj" />` to `finance/NickFinance.WebApp/NickFinance.WebApp.csproj`.
4. Rewrite `finance/NickFinance.WebApp/wwwroot/app.css`: keep all the rules, but replace literal hex values with `var(--nep-*)` references. Specifically:
   - Primary `#1d4ed8` → `var(--nep-primary)` (`#4F46E5`) — **this is the visible change** and is intentional. (Confirm with owner; see §6.)
   - All `#f8fafc / #0f172a / #475569 / #e2e8f0 / #cbd5e1` → token vars.
   - Pill colours stay literal (semantic ok/warn/bad don't yet have tokens).
5. Load the Inter font in `App.razor` head (Portal already does this; NickFinance currently uses system default).

### Phase 3 — extract shared components (1–2 days, the highest-leverage move)
Promote Portal's `Home.razor` inline styles into proper components. Land them in `platform/NickERP.Platform.Web.Shared/Components/`:

| New component | Sourced from | Used by NickFinance for |
|---|---|---|
| `NepPage.razor` | Portal `.nep-page` wrapper | `MainLayout.razor` |
| `TopNav.razor` | Portal nep-nav (extract logo, nav-links slot, search, actions, user) | `MainLayout.razor` |
| `NepCard.razor` | Portal `.nep-card` + Finance `.card` | every page |
| `NepStat.razor` | Portal `StatCard.razor` (move whole-cloth) | `Home.razor` tile-row replacement |
| `NepPill.razor` | Finance `StatusPill` + Portal `.nep-chip` | `ArList`, `PettyCashList`, all approvals UIs |
| `NepTable.razor` | Finance `<table>` styling — thin wrapper accepting `<thead>`/`<tbody>` | every list page |
| `NepEmpty.razor` | Finance "No vouchers yet" pattern as parameterised stub | every list page |
| `NepPageHeader.razor` | `<h1>` + `<p class="lede">` pattern | every page |
| `Money.razor` | combines Portal's `Money()` + Finance's repeated `(minor / 100m).ToString("N2")` into one `<Money Minor=@x Currency="GHS" />` | every financial value |

This is **8 components**. Each is small (≤80 lines incl. styles). Total: 1–2 days incl. tests.

### Phase 4 — port NickFinance pages to shared components (2–3 days)
Touch every `finance/NickFinance.WebApp/Components/Pages/*.razor` (21 files). For each:
- `<h1>X</h1><p class="lede">Y</p>` → `<NepPageHeader Title="X" Subtitle="Y" />`
- `<section class="card">` → `<NepCard>`
- `<table>…</table>` → `<NepTable>…</NepTable>`
- "No X yet" rows → `<NepEmpty>`
- All `(x.Minor / 100m).ToString("N2")` → `<Money Minor="@x.Minor" />`
- `<StatusPill>` calls keep working (just upgrade the local component to wrap `<NepPill>`).

The 21 pages are mostly mechanical. Highest-risk pages: `PettyCashDetail.razor`, `ArDetail.razor`, `ArNew.razor` (forms with EditForm). Estimate ~10 minutes per simple list page, 30 minutes per detail/edit page. Total ≈ 6 hours work.

### Phase 5 — MainLayout swap (½ day)
Replace NickFinance's hand-rolled topnav with `<TopNav>` from shared. Keep the existing `NavLink` items as content slot. Add the app-switcher dropdown.

### Phase 6 — verification (½ day)
- Run `dotnet build NickscanERP.sln`. Should be clean.
- Run NickFinance, click every page, verify nothing broke visually.
- Compare side-by-side with Portal at `localhost:5400` and NickFinance at its dev port — they should now share identical chrome surface (header, brand mark style, card surface).
- Once green, lift the same `<TopNav>` into Portal `Home.razor` (eliminate the inline nav). That validates the shared component on its origin app.

### Phase 7 (optional, defer) — dark mode (1 day)
Hook `[data-theme="dark"]` body class to a toggle in `<UserMenu>`. The token file already has dark overrides; switching is "set the attribute." Don't ship in scope — it's a Track A.6 / B.2 item.

**Total estimate: 5–7 working days, 1 engineer.** Phase 3 (component extraction) is the multi-day bit and is the only "yak shave" — but everything after compounds off it, so it's the right yak.

---

## 6. Risks / open questions

1. **Color scheme decision: indigo (`#4F46E5`) or blue (`#1d4ed8`)?**
   `BrandConstants.Colors.Primary = "#1d4ed8"` (blue) was the declared brand; Portal Home.razor renders `#4F46E5` (indigo); NickFinance currently uses `#0071c1`/`#1d4ed8` (blue). **Pick one before Phase 1.** My recommendation: **indigo** — it's what's actually shipped to users at `erp.nickscan.net` and what the most recent code reflects. Update `BrandConstants.cs` to match.

2. **Module accent for Finance: `#059669` emerald?**
   `BrandConstants.Colors.ModuleFinance = "#059669"`. Currently NickFinance uses indigo for everything. Decision: keep indigo as primary (chrome / nav / focus), use emerald only for finance-specific accents (positive money values, "paid" pill, AR aging green band). Confirm or override.

3. **No logo asset exists.**
   No `logo*.svg` / `nickscan*.png` / `nickerp*.svg` was found anywhere in `wwwroot/` — Portal renders a `<MudIcon Icon="Hub">` glyph in a gradient box as its logo. NickHR has only `favicon.png`; NickFinance has no favicon. **Owner decision needed:** ship "icon-glyph-in-gradient-box" everywhere as the logo treatment, or commission/import a real logo SVG before Phase 5? My recommendation: treat the gradient-glyph as canonical for now; revisit when a real mark is commissioned.

4. **Favicon.**
   Only NickHR has one (1,148 B `favicon.png`). NickFinance is missing one entirely. Trivial fix in Phase 5: copy NickHR's `favicon.png` to NickFinance/wwwroot, or adopt a per-module module-coloured favicon.

5. **Portal still inlines its design system in `Home.razor`.**
   After Phase 3 we should refactor Portal to consume the shared components too — otherwise we end up with three styles (Portal inline, shared, NickFinance global). I've folded this into Phase 6 verification but if the owner wants to defer, that's fine — it works either way; just expect drift.

6. **MudBlazor entanglement.**
   Portal `App.razor` loads MudBlazor CSS+JS; `Home.razor` uses `<MudIcon>` everywhere. If `<TopNav>` from shared uses MudIcons, NickFinance must add `<PackageReference Include="MudBlazor" />`. Currently NickFinance uses **zero MudBlazor**. **Decision:** either (a) add MudBlazor to NickFinance just for icons (small cost, larger payload), or (b) replace `<MudIcon>` with raw inline SVG / Material Symbols in shared components (more work upfront, lighter runtime). Recommendation: (a) — Portal and HR already pay this cost; consistency wins.

7. **NickScanWebApp.New / `site.css` is out of scope of this plan** — its 75 KB of additional CSS, MudBlazor `ICUMSTheme`, and Domiex glass-toolbar styling is its own world. Migrating NSCIM v1 is Track C.2/C.3 work. Don't try to lift its styling into shared.

8. **NickFinance's existing `app.css` deviation is **valuable**, not accidental.** Its tokens, table styling, pill semantics, form patterns, and `tile-row` design all line up with the Portal direction. The only fix is renaming classes and swapping primary color — the IP underneath is sound.

---

## 7. Files referenced

- `C:\Shared\NSCIM_PRODUCTION\platform\NickERP.Portal\Components\Pages\Home.razor` — the realised canonical look
- `C:\Shared\NSCIM_PRODUCTION\platform\NickERP.Portal\Components\AppCard.razor`
- `C:\Shared\NSCIM_PRODUCTION\platform\NickERP.Portal\Components\StatCard.razor`
- `C:\Shared\NSCIM_PRODUCTION\platform\NickERP.Portal\Components\Layout\MainLayout.razor`
- `C:\Shared\NSCIM_PRODUCTION\platform\NickERP.Portal\Components\App.razor`
- `C:\Shared\NSCIM_PRODUCTION\platform\NickERP.Platform.Web.Shared\Branding\theme-tokens.css` — declared tokens, currently misaligned with Portal
- `C:\Shared\NSCIM_PRODUCTION\platform\NickERP.Platform.Web.Shared\Branding\BrandConstants.cs`
- `C:\Shared\NSCIM_PRODUCTION\platform\NickERP.Platform.Web.Shared\NickERP.Platform.Web.Shared.csproj`
- `C:\Shared\NSCIM_PRODUCTION\NickHR\src\NickHR.WebApp\Components\Layout\MainLayout.razor` — MudBlazor reference for dark mode + drawer
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\wwwroot\app.css` — current NickFinance theme (4,557 B; salvageable)
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Layout\MainLayout.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\App.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\NickFinance.WebApp.csproj`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\StatusPill.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\Home.razor`
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\ArList.razor` — representative table page
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\ArNew.razor` — representative form page
- `C:\Shared\NSCIM_PRODUCTION\finance\NickFinance.WebApp\Components\Pages\Reports.razor` — representative report page
- `C:\Shared\NSCIM_PRODUCTION\ROADMAP.md` — Track A.6 (`Platform.Web.Shared`) is the docked plan; this audit is its detailed brief

---

## 8. Recommended first commit boundary

A single PR titled **"feat(platform): align Web.Shared tokens with Portal realised palette + wire NickFinance"** containing:

- Updated `theme-tokens.css` (token reconciliation)
- `BrandConstants.cs` color update
- `NickFinance.WebApp.csproj` project ref
- `NickFinance.WebApp/wwwroot/app.css` rewritten to use `var(--nep-*)`
- `App.razor` head adds Inter font + tokens.css link
- New components in `Platform.Web.Shared/Components/` (8 of them)

That's Phases 1–3 in one commit. Phase 4 (per-page port) ships as a follow-up PR per page-cluster (AR / AP / Petty Cash / Banking / Reports). Phase 5 (TopNav swap) ships last.
