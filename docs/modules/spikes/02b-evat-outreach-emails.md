# Spike 2b — GRA e-VAT partner outreach emails

> Three personalised drafts ready to send. Each is tuned to the
> partner's positioning per `02-gra-evat-partner-discovery.md`. Send all
> three on the same day so replies arrive close together for fair
> comparison.
>
> Action: copy each block, set the To line, send from a real address
> with a meaningful signature block. Track replies in a shared sheet
> for the rubric scoring.

---

## Email 1 — Hubtel (existing relationship; biggest existing context)

**To:** `partnerships@hubtel.com`
**CC:** account manager if Hubtel has assigned us one for our SMS account
**Subject:** `Hubtel as our GRA e-VAT integrator — Nick TC-Scan Ltd`

Hi team,

Nick TC-Scan Ltd already runs on Hubtel for SMS, OTP, and shortly
for mobile-money disbursements through our internal `NickComms`
gateway. We're now planning the AR side of our customs-scanning
operation and need a certified GRA e-VAT integrator to register
invoices ahead of delivery.

Volume: ~2,000 invoices/month at start, scaling to ~10,000 within
18 months as we automate scan-to-invoice on the customs-scanning
pipeline. Six sites: Tema Port (HQ), Tema Main, Kotoka Airport,
Takoradi Harbour, Aflao Border, Paga Border.

We're evaluating a small set of certified TPIs and Hubtel is on the
shortlist precisely because consolidating with one vendor cuts
ops complexity. We'd like a 30-minute discovery call covering:

1. Hubtel Invoice — current GRA certification status, IRN issuance,
   QR-code generation, audit trail per registration.
2. Pricing for 2k → 10k invoices/month; setup fee and SLA.
3. API specs (REST/JSON envelope, auth model, webhook vs polling for
   IRN, rate limits, behaviour during GRA outages).
4. References — two large Ghanaian customers using Hubtel Invoice
   we could speak to.

Sandbox access during build would also be valuable so our engineering
team can integrate before commercial commitment.

Could we get a call in the next two weeks? Happy to share more
specifics under NDA if needed.

Best,
[Your name, role]
Nick TC-Scan Ltd
[Phone]
[Email]

---

## Email 2 — Persol Systems (Tally Prime native; cross-product fit)

**To:** `sales@persolsystems.com`
**CC:** —
**Subject:** `Persol e-Invoice for Nick TC-Scan — Tally Prime + GRA e-VAT bundle`

Hi team,

Nick TC-Scan Ltd is a customs-scanning operation across six Ghana
sites (Tema, Kotoka, Takoradi, Aflao, Paga, Tema Main). We're
finalising the GL-of-record decision and Tally Prime Gold is the
front-runner — for the obvious reasons: ICAG-friendly, every
auditor in Ghana speaks Tally, and the Persol Tally + e-Invoice
bundle would consolidate two important integrations.

Separately on the engineering side, we're building an in-house
Blazor + Postgres operational layer that will issue AR invoices
from our scan pipeline (~2,000/month at start, ~10,000 in 18 months).
We need GRA e-VAT registration before delivery and journal sync into
Tally nightly.

Three things we'd like to cover in a 30-minute call:

1. Persol e-Invoice — pricing tiers for our volume, certification
   status, sandbox access, API specs.
2. Tally Prime Gold — licensing for ~10 finance users, your AMC
   structure, implementation timeline, Ghana COA template + PAYE/
   SSNIT/VAT localisation pack.
3. Whether the e-Invoice + Tally bundle gives us a discount over
   buying them separately, and whether there's a Tally ODBC plug-in
   that auto-registers invoices on save (we've seen this mentioned).

We're ready to engage commercially in the next quarter once we
finalise our build plan.

Best,
[Your name, role]
Nick TC-Scan Ltd
[Phone]
[Email]

---

## Email 3 — Blue Skies Solutions (compliance-specialist)

**To:** `info@blueskies-gh.com` (verify; LinkedIn check first)
**CC:** —
**Subject:** `GRA e-VAT integrator evaluation — Nick TC-Scan Ltd`

Hi team,

Nick TC-Scan Ltd is shortlisting GRA-certified e-VAT integrators for
our scan-to-invoice pipeline. We operate at six Ghana sites with
projected volume of ~2,000 invoices/month at launch, scaling to
~10,000 within 18 months. Blue Skies came up in our research as a
specialist with deep GRA relationships — that's specifically what
attracts us.

A 30-minute discovery call to cover the following would help us
evaluate:

1. Your current GRA certification (number, expiry, scope of services
   covered).
2. Pricing structure for our volume; setup, SLA, support hours.
3. API surface (auth model, IRN issuance, QR-code rendering, error
   semantics, rate limits, outage handling).
4. Two reference customers in Ghana at our scale or larger we could
   speak to.
5. Data residency (in-country vs offshore) and your stance on
   Ghana Data Protection Act 843.

Our build kicks off late this year; partner onboarding in the next
two months would land us cleanly.

Best,
[Your name, role]
Nick TC-Scan Ltd
[Phone]
[Email]

---

## Tracking the replies

Once all three are sent, log each reply in a shared sheet with these
columns so we can score consistently:

| Partner | Replied within | Pricing 2k/mo | Pricing 10k/mo | Setup | SLA | Sandbox latency p99 | Reference customers | Notes |
|---|---|---|---|---|---|---|---|---|

Score per the weighted rubric in `02-gra-evat-partner-discovery.md
§ Evaluation rubric`.

---

## After-call follow-up checklist

For whichever partner advances:

- [ ] NDA signed (most partners will require before sharing pricing
      + API docs in detail)
- [ ] Sandbox credentials issued
- [ ] Sample invoice round-tripped end-to-end via the
      `finance/spikes/EVatSandbox/` test app
- [ ] Latency for 100 sandbox calls captured
- [ ] Outage scenario tested (partner switch + error responses
      documented)
- [ ] Commercial proposal received in writing
- [ ] Internal sign-off (CFO + CTO) before contracting
