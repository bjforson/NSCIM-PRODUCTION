# `platform/` — v1 platform layer (deploy-staging copy)

**Repo**: `C:\Shared\NSCIM_PRODUCTION\` ← v1 (`github.com/bjforson/NSCIM-PRODUCTION`)

## Read this before editing

This tree contains v1's `NickERP.Platform.*` building blocks
(`Core`, `Identity`, `Observability`, `Web.Shared`, `Logging`, `Telemetry`,
`Tenancy`, etc.). Some of these — specifically `Core`, `Identity`,
`Observability`, `Web.Shared` — are referenced by v1 NickFinance and so
have been cloned into the v2 repo at
`C:\Shared\ERP V2\v1-clone\platform\`.

## Two flavours of `NickERP.Platform.*`

- **v1 design** (this tree, plus the clone in `ERP V2/v1-clone/`):
  flat, mature, already running. NickFinance's WebApp + bootstrap CLI
  + NickHR's WebApp all link against these. Today's role-overhaul
  added the `Permission` + `RolePermission` entities and the
  `Add_Permissions_RolePermissions` migration to v1's
  `NickERP.Platform.Identity/`.

- **v2 greenfield design** (in v2's top-level `platform/`,
  `C:\Shared\ERP V2\platform\NickERP.Platform.*`): different shape
  altogether — separate `Auth/`, `Entities/`, `Services/` subdirs,
  different namespace conventions. **Not yet built**. These are the
  fresh-start identity / audit / tenancy / etc. layers. They will
  eventually replace the v1 design but are unrelated to today's
  running system.

## Editing rules

- **Edits that support running NickFinance** → make them in
  `C:\Shared\ERP V2\v1-clone\platform\...`, push to ERP-V2 origin,
  then mirror back to this `NSCIM_PRODUCTION/platform/` for the
  next deploy.
- **Edits that support NickHR or other v1 NSCIM services** → fine to
  edit here (this is v1's platform). Just stage by explicit path so
  unrelated NickFinance drift doesn't sweep in.
- **Edits to v2's greenfield platform design** → those go in
  `C:\Shared\ERP V2\platform\` (the top-level v2 platform), not here
  and not in `v1-clone/platform/`.
- **Do NOT** `git add .` / `git commit` v2-flavoured platform changes
  onto the v1 origin.

## Today's role-overhaul (2026-04-30)

`NickERP.Platform.Identity/Entities.cs` gained `Permission` +
`RolePermission` entities; `IdentityDbContext.cs` got matching DbSets +
relationships; `Migrations/20260430120000_Add_Permissions_RolePermissions.{cs,Designer.cs}`
+ snapshot are new. These mirror into `ERP V2/v1-clone/platform/NickERP.Platform.Identity/`.

Plan of record: `~/.claude/plans/lovely-sleeping-metcalfe.md`.
