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
the Issue #8 benchmark — so they are committed as defaults in `src/Host.Web/appsettings.json`.

For the current Controlled Demo Candidate every one of these values is **frozen**:

| Configuration key | Environment variable | Type | Frozen value |
|---|---|---|---|
| `Ai:Provider` | `Ai__Provider` | string | `Ollama` |
| `Ai:BaseUrl` | `Ai__BaseUrl` | absolute URL | `http://127.0.0.1:11434` |
| `Ai:Model` | `Ai__Model` | string | `qwen3.5:2b-q4_K_M` |
| `Ai:TimeoutSeconds` | `Ai__TimeoutSeconds` | integer seconds | `20` |
| `Ai:Temperature` | `Ai__Temperature` | number | `0` |
| `Ai:ContextTokens` | `Ai__ContextTokens` | integer tokens | `4096` |

The environment-variable names exist because that is how ASP.NET Core configuration addresses the
section, not because the local demo supports overriding it. `Host.Web` validates the profile at
startup and rejects a different provider, model, timeout, temperature or context size instead of
running an unmeasured configuration.

Changing any frozen value — a different model, timeout, temperature, context size or
provider/profile — is therefore **not a supported local-demo override**. It needs a new explicit
architecture and acceptance decision plus the benchmark procedure of `docs/TECHNICAL.md` section 28,
because the model, the prompt and the schema are one measured identity. The base URL stays with the
frozen profile for the same reason: the controlled demo reaches Ollama on `127.0.0.1` only.

The frozen request shape is not configurable: structured JSON schema output, `stream: false`,
`think: false`, one corrective retry maximum for a reply that is invalid, unparsable or schema-invalid,
no schema retry for a transport failure, and prompt `nlu-system-prompt-v3`. Local Ollama needs no
credentials, and it must never be exposed publicly; the demo runs Ollama on `127.0.0.1` only.

## WhatsApp Cloud API

The Messaging module reads Meta WhatsApp settings from the `WhatsApp` section.

| Configuration key | Environment variable | Type | Secret |
|---|---|---|---|
| `WhatsApp:ApiVersion` | `WhatsApp__ApiVersion` | Graph API version, for example `v23.0` | No |
| `WhatsApp:PhoneNumberId` | `WhatsApp__PhoneNumberId` | WhatsApp phone number id | No |
| `WhatsApp:WabaId` | `WhatsApp__WabaId` | WhatsApp Business Account id | No |
| `WhatsApp:VerifyToken` | `WhatsApp__VerifyToken` | webhook verification token | Yes |
| `WhatsApp:AppSecret` | `WhatsApp__AppSecret` | app secret used for `X-Hub-Signature-256` | Yes |
| `WhatsApp:AccessToken` | `WhatsApp__AccessToken` | Graph API bearer token | Yes |
| `WhatsApp:TimeoutSeconds` | `WhatsApp__TimeoutSeconds` | outbound HTTP timeout seconds, default `20` | No |
| `WhatsApp:MaxWebhookBodyBytes` | `WhatsApp__MaxWebhookBodyBytes` | webhook POST body limit, default `3145728` | No |
| `WhatsApp:WebhookPermitLimit` | `WhatsApp__WebhookPermitLimit` | built-in webhook rate-limit permits per window, default `120` | No |
| `WhatsApp:WebhookWindowSeconds` | `WhatsApp__WebhookWindowSeconds` | rate-limit window seconds, default `60` | No |

Real secrets belong in user-secrets or environment variables, never in committed configuration.

```bash
dotnet user-secrets set "WhatsApp:ApiVersion" "<graph-version>" --project src/Host.Web
dotnet user-secrets set "WhatsApp:PhoneNumberId" "<phone-number-id>" --project src/Host.Web
dotnet user-secrets set "WhatsApp:WabaId" "<waba-id>" --project src/Host.Web
dotnet user-secrets set "WhatsApp:VerifyToken" "<verify-token>" --project src/Host.Web
dotnet user-secrets set "WhatsApp:AppSecret" "<app-secret>" --project src/Host.Web
dotnet user-secrets set "WhatsApp:AccessToken" "<access-token>" --project src/Host.Web
```
