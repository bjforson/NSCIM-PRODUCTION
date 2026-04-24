# Spike 1 — Tally Prime XML round-trip

> Goal: prove we can hand-craft a journal in Tally XML format, import
> it into Tally Prime Gold, have the journal appear with balances
> intact, and export it back in a format whose ledger balances
> reconcile against our source. If round-trip is faithful,
> `NickFinance.GlSync` (module 6.9) is feasible. If not, we pick a
> different GL of record.
>
> **Blocked until a Tally Prime Gold install is available.** This doc
> is the test harness that will run when it is.

---

## Status

☐ Tally Prime Gold not installed on any Nick machine as of the spike
  kickoff. Acquisition + install needs to happen first (Tally partner
  onboarding, licence purchase).

Until then:
- [x] Draft the sample journal XML we'd submit (below)
- [x] Draft the import + export test script (below)
- [x] Draft the reconciliation checklist (below)
- [ ] Actually execute once Tally is on a machine

---

## What we're testing

Six round-trips, each designed to catch a specific compatibility risk:

| # | Journal | What it stresses |
|---|---|---|
| 1 | Simple two-line GHS journal (DR cash, CR revenue) | Baseline — does anything import at all |
| 2 | Multi-line (5 lines, 2 DRs + 3 CRs, still balanced) | Many-to-many leg handling |
| 3 | Journal with site + cost-centre dimensions | Does Tally preserve our analytic dimensions or collapse them |
| 4 | VAT + NHIL + GETFund + COVID split per our compound calc | Does Tally accept pre-computed levy splits or insist on its own calc |
| 5 | USD-denominated journal with FX to GHS reporting | Multi-currency round-trip |
| 6 | Reversal pair (original event + its reversal) | Does Tally see these as two journals or one net-zero |

Each journal gets a unique `UDF:Narration` tag that we round-trip
search for.

---

## Sample journal XML (round-trip #1, baseline)

Tally uses a proprietary XML import format. The envelope shape is
`<ENVELOPE><HEADER>...</HEADER><BODY>...<DATA>...</DATA></BODY></ENVELOPE>`
with an `IMPORTDATA` request type.

```xml
<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Import Data</TALLYREQUEST>
  </HEADER>
  <BODY>
    <IMPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Vouchers</REPORTNAME>
        <STATICVARIABLES>
          <SVCURRENTCOMPANY>Nick TC-Scan Ltd</SVCURRENTCOMPANY>
        </STATICVARIABLES>
      </REQUESTDESC>
      <REQUESTDATA>
        <TALLYMESSAGE xmlns:UDF="TallyUDF">
          <VOUCHER REMOTEID="NFRL-00000001-5B3A"
                   VCHKEY="NFRL-00000001"
                   VCHTYPE="Journal"
                   ACTION="Create"
                   OBJVIEW="Accounting Voucher View">
            <DATE>20260915</DATE>
            <EFFECTIVEDATE>20260915</EFFECTIVEDATE>
            <VOUCHERTYPENAME>Journal</VOUCHERTYPENAME>
            <VOUCHERNUMBER>NFRL-00000001</VOUCHERNUMBER>
            <NARRATION>NickFinance roundtrip #1 / src=petty_cash / evt=7F3C...</NARRATION>
            <REFERENCE>NFRL-00000001</REFERENCE>
            <ISINVOICE>No</ISINVOICE>
            <ALLLEDGERENTRIES.LIST>
              <LEDGERNAME>Petty Cash - Tema</LEDGERNAME>
              <ISDEEMEDPOSITIVE>Yes</ISDEEMEDPOSITIVE>
              <AMOUNT>-15000.00</AMOUNT>
            </ALLLEDGERENTRIES.LIST>
            <ALLLEDGERENTRIES.LIST>
              <LEDGERNAME>Cash at Bank - GCB Current</LEDGERNAME>
              <ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE>
              <AMOUNT>15000.00</AMOUNT>
            </ALLLEDGERENTRIES.LIST>
          </VOUCHER>
        </TALLYMESSAGE>
      </REQUESTDATA>
    </IMPORTDATA>
  </BODY>
</ENVELOPE>
```

**Quirks to note from Tally docs:**

- Date format `YYYYMMDD`, no separators.
- `ISDEEMEDPOSITIVE=Yes` means debit; `No` means credit. Amounts on
  debit side carry a leading minus sign in the AMOUNT tag. This is the
  single weirdest bit of Tally XML and the #1 source of import failures.
- `VCHKEY` must be globally unique across all imports. We'll map our
  ledger `event_id` → `VCHKEY` 1:1 and carry our `event_id` in
  `NARRATION` for reverse lookup.
- `LEDGERNAME` must match an existing ledger in the Tally company
  exactly (case, spaces, trailing whitespace). A one-line difference
  and the import silently creates a new ledger named differently —
  this is the second-most-common breakage. We handle with a
  pre-import "ledger name validator" pass.

---

## Round-trip test plan

Once Tally is installed:

### Preconditions
1. Install Tally Prime Gold (latest stable, 5.x as of 2026).
2. Create company `Nick TC-Scan Ltd - Sandbox`.
3. Seed minimal COA: `Petty Cash - Tema`, `Cash at Bank - GCB Current`,
   `Scan Fee Revenue`, `VAT Output`, `NHIL`, `GETFund`, `COVID Levy`,
   `Customers - Test Co`.
4. Note the ODBC port Tally listens on (default `9000`) for import.

### Execute
```powershell
# From NickFinance.GlSync spike project (create alongside spike):
dotnet run --project finance/spikes/TallyRoundtripSpike -- import roundtrip-1.xml
```

The spike tool POSTs the XML to `http://localhost:9000` (Tally's
built-in HTTP listener). Response is an XML envelope with counts.

### Verify
1. In Tally UI: `Gateway > Display > Day Book`. Should see the journal
   for 2026-09-15.
2. Inspect the ledger balances:
   - `Petty Cash - Tema` shows DR 15,000.00
   - `Cash at Bank - GCB Current` shows CR 15,000.00
3. Export back via Tally: `Gateway > Display > Day Book > Export >
   Format: XML`. Save to `out-1.xml`.
4. Run comparison:
   ```powershell
   dotnet run --project finance/spikes/TallyRoundtripSpike -- compare roundtrip-1.xml out-1.xml
   ```
   Compares every `ALLLEDGERENTRIES.LIST` pair. Must match exactly on
   LEDGERNAME, ISDEEMEDPOSITIVE, AMOUNT, DATE, NARRATION (for the
   ones we submitted).

### Pass criteria
- [ ] All 6 round-trips import without error
- [ ] All 6 round-trip export XMLs match our submitted XML on the
      compared fields
- [ ] No duplicate ledgers were silently created
- [ ] Dimensions (site / cost-centre) either survive or have a
      documented loss we accept
- [ ] Reversal pair (#6) appears as two linked vouchers, not collapsed

### Fail criteria → escalation
If any of these, escalate before committing to Tally:
- Ledger names silently duplicated (requires strict pre-import check)
- Multi-currency (test #5) loses FX rate
- Compound-levy split (test #4) rounded differently by Tally than us
  (sub-pesewa drift — acceptable if documented; full-pesewa drift —
  deal-breaker)
- Round-trip takes > 10 seconds per journal at scale (projection:
  we'll post ~2,000 journals/day; > 30 min/day on import is not OK)

---

## If Tally round-trip fails

Fallback, in order of preference:

1. **QuickBooks Online** — IIF or CSV import. Less Ghana-auditor
   familiarity but widely accepted. Fewer quirks than Tally XML.
2. **Sage 50** — CSV import. Strong Ghana partner presence.
3. **Odoo Community** — open source, full API, extensible. Longer
   implementation but most modern of the alternatives.
4. **Full native build** — skip GL-of-record entirely, NickFinance
   becomes GL. 2–3× the build cost; deferred per `NICKFINANCE_PLATFORM.md`.

Record whichever we pick in the decision log.

---

## Decisions pending

- [ ] Who owns Tally licence procurement?
- [ ] Who is the designated Tally administrator post-install? (SoD
      from NickFinance devs.)
- [ ] Which Ghana Tally partner? (Tally Solutions Ghana, Peritum
      Partners, etc.)
- [ ] Can the partner provide a sandbox company pre-configured with
      Ghana COA templates + GRA e-VAT add-on?
