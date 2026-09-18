# Issue #8 — structured NLU benchmark (Intel Mac)

Measures the structured-NLU quality and warm latency of the demo candidates on the actual
Intel Mac, so Issue #9 can freeze a model on evidence. The harness lives outside the business
modules and depends on no production Intelligence code.

Source of truth:

- `docs/TECHNICAL.md` v3.2 section 8 (AI contract), section 9 (intent routing), section 28 (benchmark procedure);
- `docs/PLAN.md` v3.2 section 13 (candidate, dataset coverage, acceptance targets, decision path).

| gate | target (docs/PLAN.md section 13.3) |
| --- | --- |
| intent accuracy | >= 90% |
| hard-budget classification | 100% on the dedicated hard-budget cases |
| structured schema success | >= 98% after one retry |
| warm median latency | <= 8 s |
| warm p95 latency | <= 12 s |
| no model output path alters SQL commercial facts | enforced by the production architecture; the harness never writes model output to PostgreSQL |

## Requirements

- .NET 10 SDK (`global.json` pins 10.0.100 with `rollForward: latestFeature`).
- Ollama running natively on `http://127.0.0.1:11434` for live runs only.
- The candidate model pulled deliberately by a human, for example
  `ollama pull qwen3.5:2b-q4_K_M`. The harness never downloads models.

`validate`, `dry-run` and the unit tests need neither Ollama nor a network connection.

## Reproducibility inputs

| artefact | path |
| --- | --- |
| dataset (60 cases) | `data/v1/cases.jsonl` |
| JSON schema | `schemas/nlu-output.schema.json` |
| manifest (versions, hashes, thresholds) | `manifest.json` |
| raw run artifacts (gitignored) | `results/<run-id>.json` |
| final committed evidence | `reports/<model>-intel-mac.md` and `.json` |

`manifest.json` records the dataset, schema and prompt versions plus the dataset and schema
SHA-256 hashes and the exact PLAN thresholds. `validate` fails when a hash no longer matches,
so a stale manifest cannot silently change what a report means.

The schema is the documented conceptual schema with `additionalProperties: false` and the
documented `required` list (`intent`, `requiredPorts`, `grades`, `budgetType`). Extra keys a
model invents, for example a price or a stock count, therefore fail schema validation instead
of passing silently. The `intent` property stays a plain string because v3.2 does not constrain
it with an enum; intent values are scored against the documented routing table.

## Commands

```bash
# 1. Offline: dataset, schema, manifest, hashes, coverage tags, expected outputs
dotnet run --project benchmarks/Issue8.NluBenchmark -- validate

# 2. Offline wiring check: the whole pipeline against the fixture gateway (never evidence)
dotnet run --project benchmarks/Issue8.NluBenchmark -- dry-run

# 3. Live: verify Ollama and the model tag, then send two warm-up requests
dotnet run --project benchmarks/Issue8.NluBenchmark -- warmup --model qwen3.5:2b-q4_K_M

# 4. Live: first full pass
dotnet run --project benchmarks/Issue8.NluBenchmark -- run --model qwen3.5:2b-q4_K_M --run-id run1

# 5. Live: second full pass while the model is warm
dotnet run --project benchmarks/Issue8.NluBenchmark -- run --model qwen3.5:2b-q4_K_M --run-id run2

# 6. Combine both live runs into the committed report
dotnet run --project benchmarks/Issue8.NluBenchmark -- report --runs run1,run2
```

Exit codes: `0` = the harness completed and wrote its output, even when a benchmark gate
fails; `1` = usage error; `2` = invalid dataset/schema/manifest; `3` = Ollama or other
infrastructure failure. A model quality failure is report data, never a crash.

The live run writes `results/run1.json` and `results/run2.json` (gitignored) and the report
command writes `reports/<model-slug>-intel-mac.md` and `.json`, which are committed as the
Issue #8 evidence. The report command refuses dry-run artifacts so fixture numbers can never
be presented as measurements.

## Ollama request shape

`/api/chat` with `stream: false`, **`think: false`**, `format` set to the committed schema,
`options.temperature = 0` and `options.num_ctx = 4096`, over a short fixed structured-NLU system
prompt that never asks for prose, prices, stock or business policy. Every measured path (first
attempt and the corrective retry) sends `think: false`: this is short structured NLU, and
thinking-enabled replies on the x86 Mac did not return inside the 20 s timeout. The default
timeout is 20 s and can be overridden per command with `--timeout-seconds`. When Ollama returns
them, `total_duration`, `load_duration`, `prompt_eval_count`, `prompt_eval_duration`,
`eval_count` and `eval_duration` are stored with each attempt.

## Warm-up and run procedure

1. Start Ollama and confirm the model tag exists (`warmup` does both).
2. Run `warmup`: it sends exactly two small structured requests and prints their latency.
   It succeeds only when both requests return schema-valid structured output; a timeout,
   transport error, invalid JSON or schema-invalid reply after the one allowed retry prints the
   reason, exits `3` and never prints `Model is warm.`, so a measured pass cannot start from a
   model that is not actually answering.
3. Run the complete dataset twice (`run1`, then `run2`) without restarting Ollama.
4. Keep both raw artifacts, then generate the report from both.

Warm-up requests are never part of the metrics. `run` treats its own process as warm because
`warmup` already loaded the model; do not restart Ollama between the two passes.

## System prompt

The prompt (`nlu-system-prompt-v3`) is compact but explicit about the documented contract:

- extraction is stated-facts-only: every value the customer states must be extracted, and nothing
  may be inferred, assumed or defaulted; a use case never implies a port, grade, panel, resolution,
  refresh rate or budget;
- absence uses the schema's own convention: absent optional scalar = `null`, absent ports and
  grades = `[]`, and never an empty string or a `"unknown"`/`"N/A"`/`"default"` placeholder;
- the ten intent names are spelled exactly as in docs/TECHNICAL.md section 9 (never a
  `product_search`-style alias), with routing rules that keep a specification-bearing search as
  `ProductSearch`, reserve `ProductDetails` for details about an already identified item, and place
  availability, price, comparison, store-policy, handoff, greeting and out-of-scope messages;
- field ownership for every documented field, the curated use-case vocabulary, the model-code rule
  that price/size/resolution/refresh numbers are not model codes, and the budget wording that means
  Hard (`مش عايز أعدي`, `بحد أقصى`, `أقصى حاجة`, `مايزدش عن`), Soft (`في حدود`, `حوالي`), Range or None.

It contains no dataset case and no expected output, and the harness contains no rule that
rewrites model output to make a case pass. Normalization exists only in the comparison layer
(trim, whitespace collapse, case folding, port/grade sets, numeric equality), so the benchmark
measures whether the model follows the prompt rather than whether the harness repairs it.

## Scoring rules

- One retry maximum after invalid structured output (docs/TECHNICAL.md section 8.3), and the
  retry is corrective: it repeats the schema requirement and lists the validation problems.
  A semantic mismatch never triggers a retry; only invalid or unparsable JSON does.
- Comparison is deterministic and non-fuzzy. Strings are trimmed, internal whitespace runs are
  collapsed and case folded; `requiredPorts` and `grades` are compared as sets; numbers are
  compared numerically; `null` never matches a populated value; an omitted optional field is
  compared as `null`; resolution additionally treats `×` and spaces around the separator as
  `x`.
- Field statistics are reported per documented field. Only three gates are quality gates:
  intent accuracy, hard-budget classification and schema success. A case that ends without a
  schema-valid reply counts every documented field as mismatched, so malformed output cannot
  earn field matches through matching `null` values.
- A dedicated hard-budget case counts as correct only when `budgetType` is `Hard` and the
  resolved ceiling (`budgetTarget`, or `budgetMax` when the target is empty) equals the authored
  ceiling.
- Latency: the aggregate uses the wall clock of the attempt that produced the scored outcome
  (attempt 1 when it is valid, otherwise the retry). The first attempt, the retry and the total
  case duration are all stored so a retried case can be inspected. Percentiles are computed
  over the combined warm samples of both runs; per-run median/p95 are reported separately and
  p95 values are never averaged.
- The three observational cases in the dataset, where v3.2 defines no structured answer, are
  reported but excluded from every gate. Gate metrics therefore cover the 57 gating cases.

## Source-of-truth gaps this harness does not invent

- v3.2 has no `Clarification` intent, so ambiguous utterances are recorded as observational
  cases instead of being scored against an invented expectation.
- v3.2 defines no vocabulary for the `reference` field, so reference expectations are the
  literal follow-up token, and reference mismatches are reported but never gated.
- v3.2 defines no mapping from marketing resolution names (for example `Full HD`) to pixels,
  so resolution is compared as canonical text.
- v3.2 defines no `businessInfoKey` (or any FAQ subtype) field, so BusinessInfo cases are scored
  on the documented `BusinessInfo` intent only. The answer text always comes from Storefront.

## Model decision path (docs/PLAN.md section 13.3)

```text
qwen3.5:2b-q4_K_M
    ├─ quality + latency pass          → recommend for the Issue #9 freeze
    ├─ quality pass / latency fail     → benchmark qwen3:1.7b next
    ├─ quality fail / latency headroom → optional Qwen3.5 4B candidate, exact tag unconfirmed by the source of truth
    └─ both fail                       → benchmark qwen3:1.7b next
```

The report only states the branch it measured. Issue #8 does not freeze configuration, and the
harness never selects a model on its own.

## Tests

`tests/Unit.Tests/Benchmark` covers dataset loading and authoring rules, schema validation,
comparison and scoring, retry semantics, percentile math, gate and decision logic, report
rendering, request building and CLI/exit-code behaviour. Those tests use fixtures only: no
Ollama, no network, no PostgreSQL.
