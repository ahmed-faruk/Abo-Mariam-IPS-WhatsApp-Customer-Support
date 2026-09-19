# NLU benchmark report — qwen3.5:2b-q4_K_M

| field | value |
| --- | --- |
| Model | qwen3.5:2b-q4_K_M |
| Mode | live |
| Generated (UTC) | 2026-09-19T03:14:32.5334380+00:00 |
| Harness | issue8-nlu-benchmark-1 |
| Source of truth | docs/TECHNICAL.md v3.2 section 8/9/28, docs/PLAN.md v3.2 section 13.3 |
| Dataset | v1 · sha256 a00232ad824548eb806ac0a79141f2d9f12f0e70f4b0b962b283f838a6c3570b |
| Schema | nlu-output-v1 · sha256 fb9eacee28dcf31f6438fbe63092a8b48abb42cf5c592f4edd06874b2f1d4302 |
| Prompt | nlu-system-prompt-v3 |
| Temperature / context / timeout / retry | 0 / 4096 tokens / 20 s / one-retry-maximum |

## Environment

| field | value |
| --- | --- |
| macOS | 26.7 |
| Architecture | x86_64 |
| CPU | Intel(R) Core(TM) i7-1068NG7 CPU @ 2.30GHz |
| Memory | 16.0 GB |
| Ollama | 0.34.2 |
| Collected (UTC) | 2026-09-19T02:30:03.5576390+00:00 |

## Runs

| run | completed (UTC) | cases | schema-valid % | intent % | hard-budget % | median s | p95 s | retries | malformed |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| v3-run1 | 2026-09-19T02:36:34.4031130+00:00 | 57 | 100 | 61.4 | 100 | 6.35 | 7.35 | 0 | 0 |
| v3-run2 | 2026-09-19T02:57:12.3883840+00:00 | 57 | 100 | 63.2 | 100 | 6.64 | 8.97 | 0 | 0 |

## Combined gates (docs/PLAN.md section 13.3)

| gate | threshold | measured | result |
| --- | --- | --- | --- |
| Intent accuracy | >= 90% | 62.3% | FAIL |
| Hard-budget classification | = 100% on dedicated hard-budget cases | 100% (10/10) | PASS |
| Structured schema success after one retry | >= 98% | 100% | PASS |
| Warm median latency | 8 s | 6.41 s | PASS |
| Warm p95 latency | 12 s | 8.17 s | PASS |

Overall: **FAIL**

Gate metrics use the 114 gating cases; 6 observational case(s) are reported but excluded from every gate.

## Field statistics

| field | matched | compared | % |
| --- | --- | --- | --- |
| intent | 71 | 114 | 62.3 |
| brand | 102 | 114 | 89.5 |
| modelCode | 100 | 114 | 87.7 |
| sizeInches | 104 | 114 | 91.2 |
| panel | 106 | 114 | 93 |
| resolution | 110 | 114 | 96.5 |
| minRefreshRate | 108 | 114 | 94.7 |
| requiredPorts | 90 | 114 | 78.9 |
| grades | 105 | 114 | 92.1 |
| budgetType | 102 | 114 | 89.5 |
| budgetTarget | 108 | 114 | 94.7 |
| budgetMin | 112 | 114 | 98.2 |
| budgetMax | 112 | 114 | 98.2 |
| useCase | 93 | 114 | 81.6 |
| reference | 105 | 114 | 92.1 |

## Retry and malformed output

- retries (gating cases): 0 (0%)
- malformed after one retry: 0

## Token metrics (Ollama-reported)

| run | prompt-eval tokens | output tokens | prompt-eval ms | output ms | load ms |
| --- | --- | --- | --- | --- | --- |
| v3-run1 | 78159 | 3002 | 250406 | 139030 | 299 |
| v3-run2 | 78159 | 2938 | 266411 | 144203 | 6147 |

Values come from Ollama's own timing fields; `n/a` means Ollama did not return them. The harness never manufactures a token count.

## Representative mismatches

| case | input | reason |
| --- | --- | --- |
| PS-001 | عايز Dell 24 بوصة IPS فيها HDMI | brand: expected 'Dell', got null; sizeInches: expected '24', got null; panel: expected 'IPS', got null |
| PS-002 | في شاشات OLED؟ | panel: expected 'OLED', got null; useCase: expected null, got 'None' |
| PS-003 | عايز شاشة 1920x1080 | resolution: expected '1920x1080', got null; requiredPorts: expected [], got ['hdmi']; useCase: expected null, got 'Office' |
| PS-004 | عايز 2560x1440 144Hz | resolution: expected '2560x1440', got null; minRefreshRate: expected '144', got null; requiredPorts: expected [], got ['hdmi']; useCase: expected null, got 'Gaming' |
| PS-005 | محتاج ١٢٠Hz للجيمنج | minRefreshRate: expected '120', got null; requiredPorts: expected [], got ['hdmi'] |
| PS-007 | عايز شاشة فيها DisplayPort و HDMI و VGA | requiredPorts: expected ['displayport', 'hdmi', 'vga'], got ['hdmi', 'vga'] |
| PS-008 | عايز HDMI و DisplayPort و HDMI | useCase: expected null, got 'Office' |
| PS-010 | محتاج شاشة للكاميرات | requiredPorts: expected [], got ['hdmi'] |
| PS-011 | شاشة للشغل والمكتب | requiredPorts: expected [], got ['hdmi']; useCase: expected 'Office', got null |
| PS-012 | عايز شاشة للتصميم | intent: expected 'ProductSearch', got 'ProductDetails' |
| PS-014 | عايز ديل 27 IPS 144Hz HDMI | brand: expected 'Dell', got null; sizeInches: expected '27', got null; panel: expected 'IPS', got null; minRefreshRate: expected '144', got null; useCase: expected null, got 'Gaming' |
| GRADES-001 | عايز حاجة Grade A بس | intent: expected 'ProductSearch', got 'ProductDetails' |

## Model decision (input for Issue #9)

- outcome: `quality-fail-latency-pass`
- recommendation: qwen3.5:2b-q4_K_M latency is acceptable but quality is insufficient; PLAN allows an optional Qwen3.5 4B candidate, but no exact 4B tag is defined by the source of truth, so confirm the tag before running it.

## Commercial-fact invariant

The harness only compares structured NLU output; it never writes model output to PostgreSQL. docs/PLAN.md section 13.3 requires that no model output path can alter SQL commercial facts, which the production architecture enforces by reloading Catalog/Storefront facts before rendering.

---

# Issue #8 final outcome (curated evidence)

> The measured sections above are harness output for `report --runs v3-run1,v3-run2` with prompt
> `nlu-system-prompt-v3`. Everything below is curated from the same raw artifacts
> (`results/v3-run1.json`, `results/v3-run2.json`) plus the recorded 4B warm-up attempt. It adds
> no measurement, changes no gate and does not alter the model's raw results.

## A. Final 2B candidate: qwen3.5:2b-q4_K_M

| item | value |
| --- | --- |
| Prompt | nlu-system-prompt-v3 |
| Dataset | v1 · sha256 a00232ad824548eb806ac0a79141f2d9f12f0e70f4b0b962b283f838a6c3570b (60 cases: 57 gating, 3 observational, 5 dedicated hard-budget) |
| Schema | nlu-output-v1 · sha256 fb9eacee28dcf31f6438fbe63092a8b48abb42cf5c592f4edd06874b2f1d4302 |
| Raw artifacts | results/v3-run1.json, results/v3-run2.json |
| Combined samples | 114 gating evaluations (57 × 2 runs), 0 retries, 0 malformed |

| gate (docs/PLAN.md section 13.3) | threshold | measured | result |
| --- | --- | --- | --- |
| Intent accuracy | >= 90% | 62.3% | FAIL |
| Hard-budget classification | = 100% on dedicated hard-budget cases | 100% (10/10) | PASS |
| Structured schema success after one retry | >= 98% | 100% | PASS |
| Warm median latency | 8 s | 6.41 s | PASS |
| Warm p95 latency | 12 s | 8.17 s | PASS |

Thresholds are quoted from the harness gate output: the quality thresholds are lower bounds and the
latency thresholds are upper bounds.

Per run: v3-run1 intent 61.4%, median 6.35 s, p95 7.35 s; v3-run2 intent 63.2%, median 6.64 s,
p95 8.97 s. Prompt v3 raised the extracted fields over the earlier prompt-v2 measurement
(requiredPorts 56.1% → 78.9%, grades 74.6% → 92.1%, useCase 63.2% → 81.6%, reference 76.3% → 92.1%,
intent 56.1% → 62.3%) while schema, hard budget and latency stayed passing; prompt-eval cost grew
from 44,139 to 78,159 tokens per run and warm median was still inside the 8 s target.

What still fails is extraction quality, not scoring: explicitly stated brand/size/panel/resolution/
refresh values are sometimes returned as null (`PS-001`, `PS-003`, `PS-004`, `PS-005`, `PS-014`),
ports are still inferred from use-case-only messages (`PS-003`, `PS-004`, `PS-005`, `PS-010`,
`PS-011`), useCase is invented or emitted as a `'None'` string (`PS-002`, `PS-003`, `PS-004`,
`PS-008`), and two search requests were routed to ProductDetails (`PS-012`, `GRADES-001`).

## B. Representative inspection

### Dedicated hard-budget cases (expected vs actual)

| run | case | input | expected budgetType / budgetTarget | actual budgetType / budgetTarget | correct |
| --- | --- | --- | --- | --- | --- |
| v3-run1 | BUD-HARD-001 | مش عايز أعدي 3000 | Hard / 3000 | Hard / 3000 | yes |
| v3-run1 | BUD-HARD-002 | أقصى حاجة ٢٥٠٠ | Hard / 2500 | Hard / 2500 | yes |
| v3-run1 | BUD-HARD-003 | ميزانيتي بحد أقصى 4000 | Hard / 4000 | Hard / 4000 | yes |
| v3-run1 | BUD-HARD-004 | مش أكتر من ٣٥٠٠ جنيه | Hard / 3500 | Hard / 3500 | yes |
| v3-run1 | BUD-HARD-005 | عايز شاشة IPS مش عايز أعدي 4500 | Hard / 4500 | Hard / 4500 | yes |
| v3-run2 | BUD-HARD-001 | مش عايز أعدي 3000 | Hard / 3000 | Hard / 3000 | yes |
| v3-run2 | BUD-HARD-002 | أقصى حاجة ٢٥٠٠ | Hard / 2500 | Hard / 2500 | yes |
| v3-run2 | BUD-HARD-003 | ميزانيتي بحد أقصى 4000 | Hard / 4000 | Hard / 4000 | yes |
| v3-run2 | BUD-HARD-004 | مش أكتر من ٣٥٠٠ جنيه | Hard / 3500 | Hard / 3500 | yes |
| v3-run2 | BUD-HARD-005 | عايز شاشة IPS مش عايز أعدي 4500 | Hard / 4500 | Hard / 4500 | yes |

All ten evaluations returned `budgetType = Hard` with the exact ceiling in `budgetTarget`,
`budgetMax = null` and `retried = false`; the Arabic-Indic ceilings ٢٥٠٠ and ٣٥٠٠ were converted to
2500 and 3500. This matches the 100% (10/10) hard-budget gate.

### Reference / follow-up cases (expected vs actual)

| run | case | input | expected intent / reference | actual intent / reference | intent correct |
| --- | --- | --- | --- | --- | --- |
| v3-run1 | PRICE-003 | طب سعر التانية؟ | PriceCheck / التانية | PriceCheck / null | yes |
| v3-run1 | DETAILS-003 | الشاشة دي كام بوصة؟ | ProductDetails / دي | ProductDetails / null | yes |
| v3-run1 | AVAIL-001 | الديل اللي قولتلي عليها لسه موجودة؟ | AvailabilityCheck / الديل | AvailabilityCheck / الديل | yes |
| v3-run1 | AVAIL-002 | دي متاحة؟ | AvailabilityCheck / دي | AvailabilityCheck / null | yes |
| v3-run1 | COMPARE-001 | الفرق بين الأولى والتانية؟ | ProductComparison / null | ProductComparison / null | yes |
| v3-run1 | PRICE-001 | سعرها كام؟ | PriceCheck / null | PriceCheck / null | yes |
| v3-run2 | PRICE-003 | طب سعر التانية؟ | PriceCheck / التانية | PriceCheck / null | yes |
| v3-run2 | DETAILS-003 | الشاشة دي كام بوصة؟ | ProductDetails / دي | ProductDetails / null | yes |
| v3-run2 | AVAIL-001 | الديل اللي قولتلي عليها لسه موجودة؟ | AvailabilityCheck / الديل | AvailabilityCheck / null | yes |
| v3-run2 | AVAIL-002 | دي متاحة؟ | AvailabilityCheck / دي | AvailabilityCheck / null | yes |
| v3-run2 | COMPARE-001 | الفرق بين الأولى والتانية؟ | ProductComparison / null | ProductComparison / null | yes |
| v3-run2 | PRICE-001 | سعرها كام؟ | PriceCheck / null | PriceCheck / null | yes |

All twelve inspected intents matched, so reference routing is not the intent-accuracy problem. The
literal `reference` value is only echoed when the customer says الديل (once in v3-run1, never in
v3-run2); the other follow-up tokens (التانية, دي) come back as null. docs/TECHNICAL.md v3.2 defines
no `reference` vocabulary, so the benchmark compares the literal token, reports the field
(92.1% over the combined runs, because most expectations are null) and never gates on it.

## C. Optional 4B candidate: qwen3.5:4b-q4_K_M — blocked during warm-up

PLAN section 13.3 allows the optional 4B candidate because the 2B latency was acceptable while its
quality was insufficient. The candidate was installed locally and attempted on the same machine
(Intel Core i7-1068NG7, 16 GB RAM, Ollama 0.34.2) with:

```text
dotnet run --project benchmarks/Issue8.NluBenchmark -- warmup --model qwen3.5:4b-q4_K_M
```

```text
ollama 0.34.2 at http://127.0.0.1:11434 · model qwen3.5:4b-q4_K_M
warm-up failed: warm-up 1 of 2 failed: Ollama timed out on every attempt after 40.04 s. The model
is not warm and no measured run may start from this state.
```

- The configured request timeout stays 20 s, the authoritative value in docs/TECHNICAL.md section 8.2.
- The harness used its allowed second attempt and exhausted it (40.04 s across both attempts), so
  warm-up failed, no measured run was started and **no 4B metrics exist**. None are reported here.
- docs/PLAN.md section 13.3 targets a warm p95 <= 12 s, so raising the request timeout would not
  make a candidate that cannot answer within 40 s satisfy the documented demo latency target.
- The 4B candidate is therefore recorded as blocked/unsuitable on this Intel Mac baseline before
  any measured run.

## D. Candidate not tested: qwen3:1.7b

PLAN section 13.3 assigns `qwen3:1.7b` to the quality-pass / latency-fail branch. The measured 2B
outcome is quality-fail / latency-pass, so that branch did not occur and `qwen3:1.7b` was not
benchmarked. It is not a blocker for Issue #8, and it is not a recommendation.

## E. Conclusion for Issue #9

**No tested candidate satisfies all PLAN v3.2 acceptance gates on the physical Intel Mac baseline.**

| candidate | status | evidence |
| --- | --- | --- |
| qwen3.5:2b-q4_K_M | measured; schema, hard budget and latency pass, intent quality fails | 62.3% intent vs >= 90%, 100% schema, 100% (10/10) hard budget, 6.41 s median, 8.17 s p95 |
| qwen3.5:4b-q4_K_M | not measured; warm-up blocked | warm-up failed after the allowed retry path (`Ollama timed out on every attempt after 40.04 s`) |
| qwen3:1.7b | not tested | PLAN assigns this fallback to the quality-pass / latency-fail branch, which did not occur |

Issue #8 does not freeze a model. Issue #9 owns model freezing and must resolve this documented
blocker before any demo configuration is fixed.
