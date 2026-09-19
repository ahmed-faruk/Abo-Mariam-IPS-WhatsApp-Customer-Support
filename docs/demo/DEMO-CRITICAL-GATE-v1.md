# Demo-Critical Scenario Gate — v1

Frozen acceptance script for the local controlled client demo of
docs/PLAN.md v3.2 section 16. It is the artifact referenced by docs/PLAN.md sections 13.3, 16 and
17 and by docs/TECHNICAL.md sections 25.1, 28, 29 and 31.

This document is the **authoritative operational definition** of the gate: the scenario list, the
between-run reset, the exact run mechanics, the latency calculation and the evidence recording live
here and are not restated elsewhere. docs/PLAN.md and docs/TECHNICAL.md keep only the policy-level
invariant that a Controlled Demo Candidate must pass this versioned gate before the client
presentation.

The gate accepts a **Controlled Demo Candidate** configuration only. It is not a general benchmark
and it is not pilot/production acceptance:

| item | value |
| --- | --- |
| Selected configuration | Controlled Demo Candidate — docs/TECHNICAL.md section 8.2 |
| Model | `qwen3.5:2b-q4_K_M` (Ollama, `http://127.0.0.1:11434`) |
| General Issue #8 benchmark | **FAIL** — intent 62.3% vs ≥ 90% |
| Issue #8 safety gates | schema 100%, hard budget 100% (10/10), median 6.41 s, p95 8.17 s |
| Gate version | v1 (this file) |
| Acceptance rule | 100% of the 13 required scenarios per run, plus the latency limits of section D, in two consecutive post-warm runs |
| Execution owner | Issue #19, after the pre-warm of Issue #18 |

Commercial facts (price, quantity/availability, grade, warranty, exact specs and Storefront business
values) are owned by .NET and PostgreSQL. The model only interprets language into structured NLU.
No scenario below may be satisfied by a model-authored commercial fact.

## A. Operator setup and preconditions

All of the following must be true before Run 1 starts:

1. the selected configuration is the frozen Controlled Demo Candidate of docs/TECHNICAL.md section
   8.2, unchanged;
2. Ollama is already pre-warmed with the two-request procedure of docs/TECHNICAL.md section 8.4
   (Issue #18);
3. the ASP.NET host is healthy (`/health/live`, `/health/ready`);
4. PostgreSQL is healthy and module migrations are applied;
5. the demo catalogue is seeded (20–40 representative product/variant rows, docs/PLAN.md section
   16) and the expected result ordering for the demo queries is known;
6. Storefront working hours are configured as the value used by DEMO-08 and DEMO-09;
7. for a live gate: Cloudflare Quick Tunnel and the Meta callback are active and webhook
   verification succeeds;
8. PostgreSQL and Ollama are not publicly exposed;
9. every input and operator action below is frozen **before** acceptance execution.

The utterances and operator actions of this file are not edited between a failed attempt and a
passing attempt. A scenario may not be reworded, reordered or replaced to make the gate pass; a
necessary change is a new gate version with a new full two-run execution.

## B. User-facing scenarios (DEMO-01 … DEMO-11)

| id | input | expected |
| --- | --- | --- |
| DEMO-01 Brand + size search | `عندك ديل 24؟` | `ProductSearch` routing; brand Dell and the 24-inch constraint extracted; results come from Catalog |
| DEMO-02 Follow-up filters | `عايز IPS وفي HDMI` | the IPS constraint and the required HDMI port are applied to the previous search; no invented commercial fact |
| DEMO-03 Soft budget | `في حدود 3000` | `Soft` budget with target 3000; the configured soft-budget policy of docs/TECHNICAL.md section 11 applies; deterministic Catalog search |
| DEMO-04 Hard budget (CRITICAL) | `مش عايز أعدي 2500` | `ProductSearch` routing; `Hard` budget type with the ceiling exactly 2500; the deterministic Catalog path runs and no returned recommendation exceeds 2500. Exactly one of two business outcomes is a PASS: **(A)** one or more catalogue-backed recommendations at a price ≤ 2500, or **(B)** no qualifying product exists and the application returns its documented deterministic "no match under this hard ceiling" behaviour. A FAIL is: an empty or unexplained reply, a generic fallback, an unrelated reply, a clarification caused by failing to extract the explicit ceiling, a recommendation above 2500, or a model-authored price |
| DEMO-05 Multi-turn reference + price | `سعر الأولى كام؟` | the first displayed result is resolved as the reference; the price returned is the current PostgreSQL value; the model authors no price |
| DEMO-06 Live price change | operator changes the referenced product's price in Admin, then asks `سعر الأولى كام؟` again | the new database price is returned; no stale price |
| DEMO-07 Live stock/availability change | operator sets the referenced Dell product's quantity to 0, then asks `الديل اللي قولتلي عليها لسه موجودة؟` | `AvailabilityCheck`; the referenced product resolves correctly; the current database quantity decides the answer; quantity 0 reports unavailable |
| DEMO-08 Business FAQ | `مواعيدكم إيه؟` | the answer comes from Storefront BusinessInfo; the model authors no working hours |
| DEMO-09 Live FAQ change | operator edits working hours in Admin, then asks `مواعيدكم إيه؟` again | the new Storefront value is returned immediately |
| DEMO-10 Human takeover | `عايز أكلم حد` | `HumanHandoff`; the conversation enters Human mode; automatic AI responses stop |
| DEMO-11 Admin transcript | operator opens the conversation in Admin | the conversation, its messages and the current mode/status are visible |

## C. Safety preflights (DEMO-12 … DEMO-13)

These are safety checks, not staged conversation. They may be executed as preflight before the
client session and must still be recorded as part of the gate.

| id | setup | expected |
| --- | --- | --- |
| DEMO-12 Duplicate inbound | replay the same inbound message and message id | durable deduplication; exactly one outbound reply |
| DEMO-13 Ollama unavailable | make the native Ollama service unavailable with the operator procedure of section C.1, verify it is really down, then send the frozen inbound `بكام الديل 24؟` through the normal application flow | no guessed answer; the fixed safe fallback or human option of docs/TECHNICAL.md section 30; no fabricated commercial fact; health reported degraded/unavailable; no schema retry on the transport failure |

### C.1 DEMO-13 outage injection and restoration (canonical local procedure)

DEMO-13 uses the native Intel-Mac Ollama runtime only. No mock service, outage switch or production
feature is added for this; the operator makes the real local service unavailable and brings it back.

**Setup**

1. Confirm Ollama is healthy: `curl http://127.0.0.1:11434/api/version` answers with a version.
2. Make the native Ollama service unavailable the way it was started:
   - Ollama menu-bar app: quit the Ollama app; or
   - foreground `ollama serve`: stop that process (Ctrl-C in its terminal).

   No other Ollama instance may keep port 11434 bound.
3. Verify the outage is real before sending anything: `curl http://127.0.0.1:11434/api/version`
   must fail with a connection refused or an equivalent transport error.

**Execution**

4. Send the frozen DEMO-13 inbound `بكام الديل 24؟` through the normal application flow — the same
   path a real WhatsApp message takes, not a direct call to the model API.

PASS requires:

- the application does not guess;
- no price, stock, specification or FAQ value is fabricated by the model or by any fallback;
- the fixed safe fallback / human option of docs/TECHNICAL.md section 30 is produced;
- the AI/Ollama health state is reported as degraded/unavailable wherever the application exposes
  that state;
- the transport failure receives no schema retry: a timeout or connectivity failure is never
  schema-retried and stays visible as an infrastructure failure.

**Restoration**

5. Start Ollama again the way it was stopped (Ollama menu-bar app, or `ollama serve`) and verify
   `http://127.0.0.1:11434/api/version` answers again.
6. Re-run the two-request pre-warm of docs/TECHNICAL.md section 8.4 before any further gate run or
   the client presentation.

## D. Timing and latency

The timed NLU scenarios are DEMO-01 … DEMO-10: the ten interactive model-backed turns of section B.
DEMO-11 (Admin transcript), DEMO-12 (duplicate processing) and DEMO-13 (deliberately unavailable
Ollama) are not timed.

The measured interval is the complete `IAiNluClient.AnalyzeAsync` operation duration for the turn as
the application observes it — so a corrective schema retry inside the turn is never hidden and a
turn that exhausts its retry is timed as well. Wall-clock time around the application's own call to
the AI adapter is the operator-facing reading; no other production SLA is defined by this gate.

For each complete run, over those ten warm measured turns:

- warm median must be **<= 8 s**;
- warm p95 must be **<= 12 s**;
- the percentile rule is deterministic and already implemented by
  `benchmarks/Issue8.NluBenchmark/Scoring/PercentileCalculator.cs`: values are sorted ascending and
  the rank is linearly interpolated between the two closest ranks (the inclusive definition,
  spreadsheet `PERCENTILE.INC`). Median is p50 of the same sample.

A breach of either limit FAILS that complete run, even when all 13 scenarios pass. The historical
Issue #8 measurements (median 6.41 s, p95 8.17 s) are not redefined by this gate.

## E. Between-run reset (required before Run 2)

Run 1 deliberately mutates live state, so Run 2 may not simply repeat DEMO-01 … DEMO-13. Before Run 2
starts, the operator restores the exact frozen initial state of section A:

| mutated by Run 1 | reset action before Run 2 |
| --- | --- |
| DEMO-06 price change | restore the demo catalogue price used by DEMO-05 and DEMO-06 to its frozen Run 1 starting value |
| DEMO-07 quantity set to 0 | restore the referenced Dell product's quantity to its frozen pre-DEMO-07 value |
| DEMO-09 working-hours change | restore the original Storefront working-hours value used by DEMO-08 and DEMO-09 |
| DEMO-13 simulated outage | start Ollama again, verify it is healthy, and re-run the two-request pre-warm of docs/TECHNICAL.md section 8.4 (Issue #18) |
| DEMO-10 Human mode | start a fresh conversation in AI mode with no conversation, reference or Human-mode state carried over from Run 1; no prior reference state may leak into Run 2 |
| inbound/provider message ids | use fresh run-scoped inbound and provider message identifiers for Run 2. DEMO-12 still deliberately replays one identical id *within* its own run |
| process health | re-verify `/health/live`, `/health/ready` and PostgreSQL health |

Run 1's evidence is never deleted or rewritten by the reset. The completed reset is recorded as
evidence (section H) before Run 2 starts. If any reset step fails, Run 2 must not start.

This deterministic reset between Run 1 and Run 2 does not break "two consecutive runs".
Consecutive means: the same frozen gate version, the same model/configuration, the same frozen
dataset and business setup, in a continuous acceptance attempt — no failed acceptance attempt
inserted between the two runs and no scenario, input, dataset or configuration modified between
them. Resetting the mutated runtime state is part of the gate, not a new attempt.

## F. Full-gate repeatability and acceptance rule

This gate has exactly **13 required executable scenarios per run**: DEMO-01 … DEMO-13 — the 11
scripted flows of section B and the two safety preflights of section C. Repeatability is the
acceptance rule below, not a fourteenth scenario.

```text
Run 1: DEMO-01 … DEMO-13 must ALL PASS                  (13/13)
       and median <= 8 s and p95 <= 12 s over DEMO-01 … DEMO-10
AND
Run 2: DEMO-01 … DEMO-13 must ALL PASS again as one complete consecutive
       post-warm run, after the section E reset          (13/13)
       and median <= 8 s and p95 <= 12 s over DEMO-01 … DEMO-10
→ Controlled Client Demo Gate: PASS

Anything less than 100% in either run, or a latency breach in either run
→ Controlled Client Demo Gate: FAIL
```

Failures are never averaged, and a failed scenario is never re-run selectively within a run or
dropped from the denominator. Frozen inputs are not changed between runs. If execution must restart
after a failed scenario, the next acceptance attempt is a NEW complete run from DEMO-01 through
DEMO-13, including the section E reset before its second run.

## G. Ownership and execution timing

| ticket | responsibility |
| --- | --- |
| Issue #9 | freeze the Controlled Demo Candidate configuration; version this gate; static/configuration validation |
| Issue #17 | live WhatsApp smoke and duplicate/live-safety behavior |
| Issue #18 | execute the two-request model pre-warm of docs/TECHNICAL.md section 8.4 |
| Issue #19 | execute this complete gate twice consecutively and record the final client-demo acceptance evidence |

This gate is executed after the pre-warm and before the client presentation. The 60-case general
benchmark of `benchmarks/Issue8.NluBenchmark` is not executed as part of this gate; its recorded
failure stays part of the documented limitations.

## H. Evidence recording

Record one row per scenario per run, using the raw values observed — never a reconstructed value:

| Run | Scenario | Input/Action | Expected | Actual | Duration (s) | PASS/FAIL | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | DEMO-01 | `عندك ديل 24؟` | | | | | |
| 1 | … | | | | | | |
| 2 | DEMO-01 … DEMO-13 | complete gate re-run after the section E reset | | | | | |

Timing is recorded for DEMO-01 … DEMO-10 in each run (the other scenarios leave the column blank);
each run's median and p95 are computed from those ten durations with the section D rule.

Final summary:

```text
Run 1: PASS/FAIL    functional 13/13? __   median __ s (<= 8 s)   p95 __ s (<= 12 s)
Run 2: PASS/FAIL    functional 13/13? __   median __ s (<= 8 s)   p95 __ s (<= 12 s)
Controlled Client Demo Gate: PASS/FAIL
Reset completed before Run 2 (timestamp and confirmed state):
Known limitations carried forward (Issue #8 general benchmark FAIL):
```

The completed evidence is attached to Issue #19 together with the frozen gate version used, the
catalogue/BusinessInfo values seeded for the run and the exact model tag observed.
