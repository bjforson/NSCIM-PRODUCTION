# `finance/` — v1 NickFinance (deploy-staging copy)

**Repo**: `C:\Shared\NSCIM_PRODUCTION\` ← v1 (`github.com/bjforson/NSCIM-PRODUCTION`)

## Read this before editing

This `finance/` tree is the **runtime deploy source** for NickFinance
(`finance.nickscan.net`, `NickFinance_WebApp` Windows service, the
`NickFinance.Database.Bootstrap` CLI). The service binPath points at
`C:\Shared\NSCIM_PRODUCTION\publish\NickFinance.WebApp\` which is
populated by `Deploy.ps1 -NickFinanceOnly` from this directory.

But **per the directional v1/v2 separation rule, NickFinance is v2**:
> NickFinance lives in v2 (`C:\Shared\ERP V2\`) and pushes to
> `github.com/bjforson/ERP-V2`. Never let those commits land on the v1
> origin (`github.com/bjforson/NSCIM-PRODUCTION`).

The canonical NickFinance source now lives at
**`C:\Shared\ERP V2\v1-clone\finance\`** (cloned 2026-04-30 — see that
dir's `README.md` for context). This `NSCIM_PRODUCTION\finance\` tree is
the deploy-staging copy until services are repointed at v2.

## Editing rules

- **Greenfield NickFinance dev work** → edit at
  `C:\Shared\ERP V2\v1-clone\finance\...`, commit + push to
  `github.com/bjforson/ERP-V2`.
- **Deploying** → after editing in v2, mirror the changed files back to
  this v1 path, run `Deploy.ps1 -NickFinanceOnly` from the
  `NSCIM_PRODUCTION` root.
- **Do NOT** `git add .` / `git commit` the contents of this tree onto
  the v1 origin. Today's working tree is full of v2-flavoured drift; a
  bulk commit would push v2 work to the wrong remote.
- **Genuine v1 fixes** (e.g. legacy NSCIM API/WebApp issues) commit
  normally on v1; just don't sweep adjacent NickFinance edits in.

## Today's role-overhaul (2026-04-30)

The role-overhaul work that landed in this tree today is mirrored in the
v2 clone at `C:\Shared\ERP V2\v1-clone\finance\`. The corresponding
NickHR-side wrapper changes (single-grade dropdown, audit-vs-ops check)
stay in v1 at `NSCIM_PRODUCTION/NickHR/` because NickHR is v1's own
HR module.

Plan of record: `~/.claude/plans/lovely-sleeping-metcalfe.md`.
