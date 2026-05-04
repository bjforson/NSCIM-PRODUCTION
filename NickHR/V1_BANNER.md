# V1_BANNER — read this before editing NickHR in this tree

**This NickHR tree is now a v2-mirrored deploy-staging copy as of
2026-05-04.** The canonical going-forward source for NickHR lives in
the v2 repo:

- **v2 source-of-truth**: `C:\Shared\ERP V2\v1-clone\nickhr\`
- **v2 origin**: `github.com/bjforson/ERP-V2` (private)
- **This tree** (the v1 deploy-staging copy): `C:\Shared\NSCIM_PRODUCTION\NickHR\`
- **v1 origin**: `github.com/bjforson/NSCIM-PRODUCTION`

## DON'T edit NickHR files in this tree

Edit them in `ERP V2\v1-clone\nickhr\…` instead. After you commit
there, mirror the changed files back into this directory and run the
deploy script (still in this tree) to ship.

## Why

The directional v1/v2 separation rule pins v2 work to the v2 origin.
NickHR grew up inside v1 and continues to deploy from here pre-pilot —
but the source-of-truth for going-forward edits is the v2 v1-clone.
Pilot strategy locked 2026-05-02 has three modules co-deployed under
the v2 portal (inspection v2-native + NickFinance v1-clone + NickHR
v1-clone); the v1-clones get folded into v2-native architecture
post-pilot (~6-10 sprints per module).

This is the same exception NickFinance got on 2026-04-30 — see
`C:\Shared\NSCIM_PRODUCTION\finance\V1_BANNER.md`.

## What's still allowed here

- **Deploying** from this tree (deploy script + service binPaths still
  point here).
- **Mirroring** changes back from the v2 clone (robocopy from
  `ERP V2\v1-clone\nickhr` → here, then deploy).
- **Running migrations** against the live `nickhr` Postgres DB via
  this tree's `tools/migration-runner` (until v2 migration framework
  lands). Note `nscim_app` cannot run DDL — use `Username=postgres`.
- **Editing this `V1_BANNER.md`** — the only file you can edit
  directly here without first editing in the v2 clone.

## What's NOT allowed here

- Editing source files (`*.cs`, `*.csproj`, `*.razor`, `appsettings.*`,
  bootstrap CLI scripts, etc.) directly. Those go to the v2 clone first.
- Committing to v1 origin without checking that the matching v2 commit
  has already landed on `github.com/bjforson/ERP-V2`. The v2 clone is
  the source-of-truth; v1 is the deploy artefact.

## Coexisting platform copies

Both `NSCIM_PRODUCTION/platform/NickERP.Platform.{Core,Identity,Observability,Tenancy,Web.Shared}/`
and `ERP V2/v1-clone/platform/NickERP.Platform.{Core,Identity,Observability,Tenancy,Web.Shared}/`
exist; they share the same v1 shape. Edit in v2 first, mirror back.

The greenfield v2 platform stack at `ERP V2/platform/NickERP.Platform.*/`
is a DIFFERENT design (RLS interceptors, `ITenantOwned`, audit register,
`SetSystemContext` + sentinel `'-1'`). Don't confuse the two; this
deploy-staging tree never references it.

## Reciprocal V2 banner

`C:\Shared\ERP V2\v1-clone\nickhr\V2_BANNER.md` carries the full
clone-day context: what was cloned, what wasn't, ProjectReference
resolution, the editing flow, the post-pilot refactor plan.
