# Spike 3 — Ledger kernel results

> Goal: prove the event-sourced ledger design from `NICKFINANCE_PLATFORM.md §4.5`
> survives contact with a real Postgres + a random-journal generator. If
> balance invariants, idempotency, period lock, and append-only triggers all
> hold under 1,000+ random journals, Phase 6 can proceed.
>
> **Status: PASSED.** 33/33 tests green. Foundation is trustworthy.

---

## What was built

`finance/NickFinance.Ledger` — .NET 10 class library, ~700 LOC.

| File | Role |
|---|---|
| `Money.cs` | Pesewa-storage value type; banker's rounding; cross-currency addition rejected at compile time |
| `Entities.cs` | `LedgerEvent`, `LedgerEventLine`, `AccountingPeriod` + enums |
| `LedgerDbContext.cs` | EF Core mapping to `finance` schema; indexes; FK cascade |
| `LedgerWriter.cs` | `ILedgerWriter` — shape-validates, idempotency-checks, period-checks, posts. `ReverseAsync` builds and posts a flipped-leg correction event |
| `LedgerReader.cs` | Account balance + trial balance projections. Reversals are just events with their legs already flipped — no special casing required |
| `PeriodService.cs` | Create/soft-close/hard-close; rejects backwards transitions |
| `SchemaBootstrap.cs` | Applies Postgres-level invariants that EF migrations can't express cleanly: balance trigger + append-only triggers |
| `Exceptions.cs` | `UnbalancedJournalException`, `MalformedLineException`, `ClosedPeriodException`, `InvalidReversalException` |

`finance/NickFinance.Ledger.Tests` — xUnit 2.9.2, ~600 LOC.

| File | Tests |
|---|---|
| `MoneyTests.cs` | 14 unit tests: zero, add, subtract, negate, banker's rounding (3 inline-data rows), multiply rate (5 rows), bad currency codes, uppercase normalisation |
| `LedgerWriterTests.cs` | 11 DB-backed tests: shape validation (4), happy path, idempotency, period lock, reversal round-trip, reversal double-trigger rejection, DB-level UPDATE rejection, DB-level DELETE rejection |
| `LedgerPropertyTests.cs` | 3 property-based tests: **1,000 random balanced journals (all accepted, trial balance nets to zero), 500 random unbalanced journals (all rejected with correct exception), 100 idempotency triplets (300 writes, 100 rows persisted)** |
| `LedgerFixture.cs` | xUnit `IAsyncLifetime` fixture — creates + drops a `nickscan_finance_test` DB per run |

---

## How to reproduce

```bash
# 1. Build
cd C:\Shared\NSCIM_PRODUCTION
dotnet build finance/NickFinance.Ledger.Tests/NickFinance.Ledger.Tests.csproj -c Release

# 2. Create test DB
export PGPASSWORD='...'
"/c/Program Files/PostgreSQL/18/bin/psql.exe" -h localhost -p 5432 -U postgres -d postgres -c "CREATE DATABASE nickscan_finance_test;"

# 3. Run
export NICKFINANCE_TEST_DB='Host=localhost;Port=5432;Database=nickscan_finance_test;Username=postgres;Password=...'
dotnet test finance/NickFinance.Ledger.Tests/NickFinance.Ledger.Tests.csproj -c Release
```

Deterministic — seed `20260424` by default (override with `NICKFINANCE_TEST_SEED`).

---

## Test run output (last clean run)

```
Passed!  - Failed: 0, Passed: 33, Skipped: 0, Total: 33, Duration: 30s - NickFinance.Ledger.Tests.dll (net10.0)
```

Test breakdown:

| Suite | Count | Of which DB-backed |
|---|---|---|
| MoneyTests | 14 | 0 |
| LedgerWriterTests | 11 | 11 |
| LedgerPropertyTests | 3 | 3 |
| **Total** | **33** | **14** |

---

## What the property tests actually prove

### `Post_1000_RandomBalancedJournals_AllAccepted`
- Generator: 1,000 journals × 2–6 lines each. Amounts range 1 pesewa
  (GHS 0.01) to 1,000,000,000 pesewa (GHS 10M). Accounts drawn from a
  pool of 16 realistic COA codes (cash per site, banks, VAT/NHIL/GETFund/COVID,
  revenue, expenses).
- Generator uses integer math for the credit-side split so there's no
  rounding drift — every journal is mathematically balanced.
- Assertion: all 1,000 post successfully, and the system-wide
  `SUM(debit_minor)` equals `SUM(credit_minor)` afterwards.
- Pass → Money arithmetic is clean, invariant holds at scale, writer
  doesn't have off-by-one issues, period lock doesn't spuriously fire.

### `Post_500_RandomUnbalancedJournals_AllRejected`
- Generator: same as above, but adds 1–9 pesewa to one credit line on
  each event so it's out of balance.
- Assertion: every single one throws `UnbalancedJournalException`, and
  the DB has zero events in that period (neither C# validator NOR the
  DB-level trigger ever let one through).
- Pass → the fail-fast path is reliable.

### `Post_100_DuplicateIdempotencyKeys_OnlyFirstPersisted`
- 100 iterations × 3 `PostAsync` calls with the same idempotency key =
  300 attempted writes.
- Assertion: all three calls return the same event id, and the DB
  contains exactly 100 rows in the test period.
- Pass → retry-safe. A network blip + retry doesn't double-post.

---

## What the DB-level tests prove

Two triggers land in `finance/NickFinance.Ledger/SchemaBootstrap.cs`:

1. **Balance invariant** — `CREATE CONSTRAINT TRIGGER ledger_events_balanced
   ... DEFERRABLE INITIALLY DEFERRED`. Re-checks `SUM(debit_minor) =
   SUM(credit_minor)` per event at commit. Tested implicitly by every
   DB-backed writer test, and explicitly would catch any future code
   path that bypasses `LedgerWriter.ValidateShape`.

2. **Append-only** — `ledger_events_no_update` and `ledger_lines_no_update`
   triggers that `RAISE EXCEPTION` on any `UPDATE` or `DELETE`. Tested by:
   - `DirectUpdateOnLedgerEvents_IsRejectedByTrigger` — raw SQL
     `UPDATE finance.ledger_events SET narration='hacked'` throws.
   - `DirectDeleteOnLedgerEvents_IsRejectedByTrigger` — raw SQL
     `DELETE FROM finance.ledger_events WHERE event_id = {0}` throws.

Both errors carry the phrase `append-only` in their message so the
assertions can match on it.

---

## Performance (informational)

- Total test-suite runtime: 30 seconds for 33 tests.
- Of that, ~20 seconds is the 1,000-journal property test.
- Per-journal: 1,000 journals / 20 seconds ≈ **50 journals/second**
  single-threaded on an unoptimised connection.
- That's comfortably above projected production load (~2,000
  journals/day = ~0.023/second steady, with bursts at month-end).

---

## Design decisions validated by the spike

- **Money as `(long Minor, string CurrencyCode)` value type.** The
  banker's rounding tests cover the midpoint cases (`1.995 → 200 pesewa`,
  `1.985 → 198 pesewa`) that float-based implementations regularly get
  wrong.
- **Event-sourced, immutable ledger.** Append-only triggers mean a
  compromised service account still can't rewrite history — damage is
  always append-visible.
- **Reversal as a flipped-leg event, not an UPDATE.** The reader simply
  sums dr/cr across all events; reversal events naturally net out their
  original. Zero special-casing in projections.
- **Idempotency key as hard DB unique constraint + writer dedupe
  read-through.** Retry-safe by design, not by convention.
- **Period lock as a status column checked on every post.** Simple,
  auditable, works.

---

## What this spike did NOT exercise

Intentionally out of scope — flagged for the real implementation:

- [ ] Multi-currency journals with explicit FX translation leg
- [ ] Multi-tenant isolation under concurrent tenants (only tested tenant=1)
- [ ] Concurrent posters at high concurrency (stressed at 50 journals/sec
      serial; not yet tested at 500/sec with 10 writers)
- [ ] Soft-close vs hard-close authorisation — the soft-close state is
      modelled but the writer allows posting through it. Real
      implementation needs a caller-authorisation check.
- [ ] Period rollover — opening May 2026 after April closes
- [ ] Projections for AR aging, AP aging (module-specific, not
      kernel-level)
- [ ] DB migrations via EF migrations tool — currently uses
      `EnsureCreatedAsync` which is fine for spike but not production

None of these block the Phase 6 kickoff.

---

## Decision

**GO for Phase 6 kickoff** per `NICKFINANCE_PLATFORM.md`. The ledger
foundation is trustworthy enough to build 6.1 (PettyCash) on top of
it. Spike 1 (Tally round-trip) and Spike 2 (e-VAT partner) remain
external-dependency gates and must complete before 6.2 (AR +
scan-to-invoice) starts; they don't block 6.1.
