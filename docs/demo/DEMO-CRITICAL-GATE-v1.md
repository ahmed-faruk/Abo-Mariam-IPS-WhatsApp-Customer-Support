# Demo-Critical Scenario Gate — v1

Frozen acceptance script for the local controlled client demo of
docs/PLAN.md v3.2 section 16. It is the artifact referenced by docs/PLAN.md sections 13.3, 16 and
17 and by docs/TECHNICAL.md sections 25.1, 28, 29 and 31.

The gate accepts a **Controlled Demo Candidate** configuration only. It is not a general benchmark
and it is not pilot/production acceptance:

| item | value |
| --- | --- |
| Selected configuration | Controlled Demo Candidate — docs/TECHNICAL.md section 8.2 |
| Model | `qwen3.5:2b-q4_K_M` (Ollama, `http://127.0.0.1:11434`) |
| General Issue #8 benchmark | **FAIL** — intent 62.3% vs ≥ 90% |
| Issue #8 safety gates | schema 100%, hard budget 100% (10/10), median 6.41 s, p95 8.17 s |
| Gate version | v1 (this file) |
| Acceptance rule | 100% of the 13 required scenarios per run, in two consecutive post-warm runs |
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
| DEMO-04 Hard budget (CRITICAL) | `مش عايز أعدي 2500` | `Hard` budget with ceiling exactly 2500; no returned recommendation exceeds 2500 |
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
| DEMO-13 Ollama unavailable | simulate an unavailable model through the supported failure/test mechanism of docs/TECHNICAL.md section 30 | no guessed answer; the fixed safe fallback or human option; no fabricated commercial fact; health/failure behavior follows section 30 |

## D. Full-gate repeatability and acceptance rule

This gate has exactly **13 required executable scenarios per run**: DEMO-01 … DEMO-13 — the 11
scripted flows of section B and the two safety preflights of section C. Repeatability is the
acceptance rule below, not a fourteenth scenario.

```text
Run 1: DEMO-01 … DEMO-13 must ALL PASS  (13/13)
AND
Run 2: DEMO-01 … DEMO-13 must ALL PASS again as one complete consecutive
       post-warm run                    (13/13)
→ Controlled Client Demo Gate: PASS

Anything less than 100% in either run
→ Controlled Client Demo Gate: FAIL
```

Failures are never averaged, and a failed scenario is never re-run selectively within a run or
dropped from the denominator. Frozen inputs are not changed between runs. If execution must restart
after a failed scenario, the next acceptance attempt is a NEW complete run from DEMO-01 through
DEMO-13.

### Latency

The warm response time of every scenario is recorded. No critical interactive NLU turn may exceed
the local warm p95 engineering boundary of docs/TECHNICAL.md section 25.1 (≤ 12 s) without the
failure being surfaced before the presentation. The historical Issue #8 measurements (median
6.41 s, p95 8.17 s) are not redefined by this gate.

## E. Ownership and execution timing

| ticket | responsibility |
| --- | --- |
| Issue #9 | freeze the Controlled Demo Candidate configuration; version this gate; static/configuration validation |
| Issue #17 | live WhatsApp smoke and duplicate/live-safety behavior |
| Issue #18 | execute the two-request model pre-warm of docs/TECHNICAL.md section 8.4 |
| Issue #19 | execute this complete gate twice consecutively and record the final client-demo acceptance evidence |

This gate is executed after the pre-warm and before the client presentation. The 60-case general
benchmark of `benchmarks/Issue8.NluBenchmark` is not executed as part of this gate; its recorded
failure stays part of the documented limitations.

## F. Evidence recording

Record one row per scenario per run, using the raw values observed — never a reconstructed value:

| Run | Scenario | Input/Action | Expected | Actual | PASS/FAIL | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | DEMO-01 | `عندك ديل 24؟` | | | | |
| 1 | … | | | | | |
| 2 | DEMO-01 … DEMO-13 | complete gate re-run | | | | |

Final summary:

```text
Run 1: PASS/FAIL
Run 2: PASS/FAIL
Controlled Client Demo Gate: PASS/FAIL
Observed warm latency (per scenario, max and median):
Known limitations carried forward (Issue #8 general benchmark FAIL):
```

The completed evidence is attached to Issue #19 together with the frozen gate version used, the
catalogue/BusinessInfo values seeded for the run and the exact model tag observed.
