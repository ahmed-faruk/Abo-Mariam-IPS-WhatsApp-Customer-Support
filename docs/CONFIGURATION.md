# Runtime configuration (Issue #6)

Values the project baseline does not define must come from the deployment. This file names them and
their workflow; it introduces no business value of its own. The connection string workflow lives in
`docs/PERSISTENCE.md`.

## Catalog search policy

| Configuration key | Environment variable | Type | Valid range |
|---|---|---|---|
| `Catalog:Search:SizeToleranceInches` | `Catalog__Search__SizeToleranceInches` | decimal inches | `0` .. `50` |
| `Catalog:Search:SoftBudgetTolerance` | `Catalog__Search__SoftBudgetTolerance` | fraction | `0` .. `< 1` |
| `Catalog:Search:MaxResults` | `Catalog__Search__MaxResults` | integer | `1` .. `20`, default `20` |

`SizeToleranceInches` is the configured distance of the `:size_tolerance` filter of
`docs/TECHNICAL.md` section 10, bounded by the `ck_model_size` range of section 6.1.
`SoftBudgetTolerance` is the `softTolerance` of section 11, so a soft budget resolves to
`target * (1 + SoftBudgetTolerance)`; a hard budget ignores it completely and is never widened.
`MaxResults` cannot exceed the twenty-recommendation search policy of section 10.

The two tolerances are required. `Host.Web` validates them at startup, so a host without them stops
with an `OptionsValidationException` naming the missing setting instead of searching with a tolerance
the project baseline never defined.

## Local development workflow

Like the connection string, these values go to user-secrets or the environment, never into a
committed file. `src/Host.Web/appsettings.json` is the checked-in template that names both keys and
carries no numeric value.

```bash
# one-time setup, then set each required value
dotnet user-secrets init --project src/Host.Web
dotnet user-secrets set "Catalog:Search:SizeToleranceInches" "<inches>" --project src/Host.Web
dotnet user-secrets set "Catalog:Search:SoftBudgetTolerance" "<fraction>" --project src/Host.Web
```

```bash
# or per shell; environment variables win over appsettings.json
export Catalog__Search__SizeToleranceInches='<inches>'
export Catalog__Search__SoftBudgetTolerance='<fraction>'
```

## Intelligence AI profile

The Intelligence module reads its AI profile from the `Ai` section. The values below are the frozen
Controlled Demo Candidate of `docs/TECHNICAL.md` section 8.2 — the local model Issue #9 froze after
the Issue #8 benchmark — so unlike the tolerances above they are committed as defaults in
`src/Host.Web/appsettings.json`. They are still validated at startup: a different model, timeout,
temperature or context size is rejected instead of being run silently, because it would be an
unmeasured configuration rather than a general-purpose setting.

| Configuration key | Environment variable | Type | Frozen value |
|---|---|---|---|
| `Ai:Provider` | `Ai__Provider` | string | `Ollama` |
| `Ai:BaseUrl` | `Ai__BaseUrl` | absolute URL | `http://127.0.0.1:11434` |
| `Ai:Model` | `Ai__Model` | string | `qwen3.5:2b-q4_K_M` |
| `Ai:TimeoutSeconds` | `Ai__TimeoutSeconds` | integer seconds | `20` |
| `Ai:Temperature` | `Ai__Temperature` | number | `0` |
| `Ai:ContextTokens` | `Ai__ContextTokens` | integer tokens | `4096` |

The frozen request shape is not configurable: structured JSON schema output, `stream: false`,
`think: false`, one corrective retry maximum for a reply that is invalid, unparsable or schema-invalid,
no schema retry for a transport failure, and prompt `nlu-system-prompt-v3`. Local Ollama needs no
credentials, and it must never be exposed publicly; the demo runs Ollama on `127.0.0.1` only.

```bash
# optional override for a deliberate, documented change of AI profile
export Ai__Model='<model-tag>'
export Ai__TimeoutSeconds='<seconds>'
```
