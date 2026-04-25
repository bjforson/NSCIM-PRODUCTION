# Spike 2 — GRA e-VAT partner discovery

> Goal: pick the integration partner we'll route AR invoices through
> for GRA Certified Invoice Registration (CTC). Decision outputs: one
> partner selected, API docs in hand, sandbox creds issued, one test
> invoice round-tripped end-to-end. Blocks `NickFinance.AR` (module
> 6.2) — don't start that module without this done.

---

## Status

☐ Awaiting kickoff. External outreach required — can't complete from
  inside the repo. Everything here is the playbook.

---

## Background — why a partner, not direct

GRA's e-VAT certification programme requires:

1. A **Direct Integration Model** (DIM) software vendor is audited and
   approved by GRA. The vendor's system becomes a "certified invoice
   registration solution".
2. Alternatively, use a **Third-Party Integrator (TPI)** — a certified
   middleman that relays invoice registration calls on your behalf.

For a single-operator like Nick, TPI is cheaper and faster:

| Path | Effort | Time | Cost |
|---|---|---|---|
| Direct DIM certification | Audit + pen-test + site inspection | 6–12 months | ~GHS 50K + annual fees |
| Via TPI | Integrate their REST/SOAP API | 2–3 weeks | ~GHS 300–1,500 / month depending on volume |

TPI is the default. Revisit direct certification only if Nick wants to
sell NickERP to other customs operators.

---

## Candidate partners

Based on GRA's published certified-integrator list plus industry
knowledge as of April 2026. Verify current certification status at
kickoff — the list changes quarterly.

### 1. Hubtel

- **Website**: hubtel.com / developers.hubtel.com
- **Why interesting**: already integrated with Nick (NickComms uses
  Hubtel for SMS + OTP and will use it for MoMo disbursement). One
  less vendor.
- **e-VAT offering**: Hubtel Invoice — certified TPI for GRA e-VAT.
  Part of their broader merchant suite.
- **API style**: REST / JSON. Same auth model as their SMS API
  (`X-Api-Key` header — we know this flow).
- **Pricing**: likely tiered by invoices/month. Not published; ask.
- **Local support**: Accra HQ, same-timezone support.
- **Risk**: vendor concentration — if Hubtel has an outage, comms AND
  invoicing both fail.

### 2. Persol Systems

- **Website**: persolsystems.com
- **Why interesting**: established Tally Prime Ghana partner; bundles
  e-VAT with Tally integration. If we pick Tally as GL of record
  (per `NICKFINANCE_PLATFORM.md`), Persol is the natural cross-product.
- **e-VAT offering**: "Persol e-Invoice" TPI + direct Tally plug-in.
- **API style**: REST. They also offer a Tally ODBC add-on that
  transparently registers invoices on Tally save — could bypass our
  own integration if we trust it.
- **Pricing**: bundle with Tally support typically ~GHS 1,200/month.
- **Local support**: Accra HQ.

### 3. Blue Skies Solutions

- **Website**: blueskies-gh.com (confirm)
- **Why interesting**: specialist GRA integrator; multiple large
  Ghanaian firms use them.
- **e-VAT offering**: certified TPI with a compliance-focused product.
- **API style**: REST.
- **Pricing**: enterprise-leaning; may be higher than needed for our
  volume.

### 4. iTaps.gg / similar GRA-adjacent providers

- Lower-tier providers exist; due-diligence heavier. Generally skip
  unless top three rule themselves out.

---

## What to ask in discovery calls

Same question set to each partner, so we can compare apples to apples.

### Commercial

1. Monthly price for ~2,000 invoices/month? ~10,000?
2. Per-invoice overage?
3. Setup fee?
4. Minimum term / exit clause?
5. SLA (uptime %, response time to outage)?
6. Does the quoted price cover sandbox access during our build?
7. Support channel (email / phone / WhatsApp) and hours?

### Technical

1. API protocol (REST / SOAP)? Request/response samples?
2. Auth model (API key / OAuth / mTLS)?
3. Rate limits?
4. Webhook or polling for async confirmation of IRN assignment?
5. What's the latency p50 / p99 for GRA to return an IRN?
6. What's the failure mode when GRA's e-VAT backend is down? (GRA has
   had multi-hour outages.) Do they queue or reject?
7. Can we register credit notes, proforma invoices, export invoices?
8. Is there a bulk-registration endpoint for catch-up after an outage?
9. Do they provide a QR-code renderer for the customer-facing invoice
   PDF, or is that our job?

### Compliance

1. Current GRA certification status (certificate number, expiry)?
2. Reference customers we can call?
3. Data residency — is invoice data stored in Ghana or offshore?
4. What's their audit trail — can we pull our own logs of every
   registration call?
5. GDPR / Ghana Data Protection Act 843 stance?

### Operational

1. How do they handle our invoice schema evolution (new line-item
   fields, e.g., if GRA mandates HS codes on every line)?
2. Roadmap — any planned API breaking changes in next 12 months?
3. Can we use their sandbox for development / QA / staging — separate
   accounts or one?
4. Onboarding timeline from signed contract to production-ready?

---

## Outreach template

```
Subject: NickERP / Nick TC-Scan Ltd — GRA e-VAT partner discovery

Hi [name],

I'm reaching out from Nick TC-Scan Ltd — we operate customs-scanning
services at six sites across Ghana (Tema, Kotoka, Takoradi, Aflao,
Paga) and are building an internal accounting module that will need
to register VAT invoices with GRA.

Projected volume: ~2,000 invoices/month at start, likely scaling to
~10,000 over 18 months as scan operations automate invoice issuance.

We're evaluating certified integrators and would like to understand:

  1. Your commercial terms (pricing tiers, setup, SLA)
  2. API specs and sandbox access
  3. A couple of reference customers

Could we schedule a 30-minute discovery call in the next two weeks?

Best,
[name], Nick TC-Scan Ltd
```

Send to:
- Hubtel: partnerships@hubtel.com
- Persol: sales@persolsystems.com
- Blue Skies: info@blueskies-gh.com (verify)

Follow up on LinkedIn if no reply in 3 business days.

---

## Evaluation rubric

Score each partner 1–5 on:

| Criterion | Weight |
|---|---|
| Ghana partner fit (local support, same TZ, language) | 0.15 |
| API quality (modern, documented, webhooks) | 0.20 |
| Price at projected volume | 0.15 |
| SLA + outage handling | 0.15 |
| Reference customers (scale + happiness) | 0.15 |
| Cross-product fit with rest of NickERP stack | 0.10 |
| GRA certification confidence (longevity, depth) | 0.10 |

Weighted highest wins. Publish the scoring sheet in this file when
done.

---

## Sandbox spike

Once a partner is tentatively selected and sandbox creds issued:

1. Write a tiny console app `finance/spikes/EVatSandbox/` that:
   - Builds a fake invoice payload for Nick TC-Scan → a mock customer
   - Posts to the partner's sandbox
   - Polls for IRN (or waits on webhook)
   - Prints IRN, QR string, timestamps
2. Record latencies for 100 consecutive sandbox calls.
3. Try the "GRA outage" scenario (partner should have a switch).
4. Try a malformed invoice (wrong TIN, line-item tax mismatch) and
   record the error responses — we need to handle these gracefully
   in AR.

If sandbox call succeeds and latency p99 < 5 seconds, partner passes
the technical bar.

---

## Outputs of this spike

- [ ] Partner discovery call notes (3+ partners)
- [ ] Scoring rubric filled in
- [ ] Partner selected, justification recorded
- [ ] Sandbox access live
- [ ] Round-trip test invoice succeeded
- [ ] Latency + error-handling characterised
- [ ] Commercial terms summary → goes to CFO for sign-off

---

## Decisions pending

- [ ] Who makes the outreach calls — CTO, CFO, or ops?
- [ ] Sign an NDA first? (Partners often request before sharing
      pricing + API docs.)
- [ ] If we pick Hubtel for e-VAT AND keep them for SMS / MoMo, what's
      the contingency plan for a Hubtel outage? Secondary SMS
      provider? Paper fallback? Discuss separately.
