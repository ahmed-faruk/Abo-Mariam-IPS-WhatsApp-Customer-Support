# WhatsApp AI Used-Monitor Sales Assistant — Lean Technical Design v3.2 (Intel Mac Demo)

**Document type:** Implementation-level technical design  
**Version:** 3.2  
**Date:** 2026-09-16  
**Derives from:** Lean MVP Plan v3.2 (Mac Demo Baseline)  
**Development/demo target:** Intel macOS 14+ · 2.3 GHz Quad-Core Intel Core i7 · 16 GB LPDDR4X  
**Application stack:** .NET 10 LTS · ASP.NET Core · EF Core 10 · Npgsql · PostgreSQL · Ollama  
**Deployment style:** Modular Monolith, one deployable host

> The architecture remains production-oriented even though the first runtime target is a client proof-of-concept on a local Intel Mac. Mac limitations change the runtime profile, not module boundaries, data ownership, testing, or reliability rules.

---

# 1. Runtime profiles

The design has two explicit profiles.

## 1.1 Demo profile — implemented now

```text
Hardware
-------
Intel Mac
2.3 GHz Quad-Core Intel Core i7
16 GB 3733 MHz LPDDR4X
Intel Iris Plus Graphics 1536 MB

OS
--
macOS 14 Sonoma or newer

Runtime
-------
.NET 10 SDK                       native
ASP.NET Core                      native
Ollama                            native, x86 CPU-only
Qwen3.5 2B Q4_K_M                initial NLU candidate
Docker Desktop Intel              local container engine
PostgreSQL 18                     Docker
cloudflared                       native
Playwright                        local test tooling
Git/GitHub                        source + CI
```

**Goal:** one-client proof-of-concept, not 24/7 uptime or high concurrency.

## 1.2 Pilot profile — later

Linux VPS running the same application design. Pilot sizing and load tests occur only after client acceptance.

---

# 2. Runtime architecture

```text
                              WhatsApp Customer
                                     │
                                     ▼
                         Meta WhatsApp Cloud API
                                     │ HTTPS
                                     ▼
                       Cloudflare Quick Tunnel
                             (demo only)
                                     │
                                     ▼
┌───────────────────────────────────────────────────────────────┐
│ Host.Web — ASP.NET Core / composition root                   │
│                                                               │
│  Messaging      Conversations      Catalog                   │
│  Inbox/Outbox   state/handoff      search/facts              │
│       │              │               │                       │
│       └──────────────┼───────────────┘                       │
│                      │                                       │
│              Intelligence     Storefront                     │
│               NLU adapter      business FAQ                  │
│                      │                                       │
│                    Ollama                                     │
│                                                               │
│              Identity → Admin MVC/Razor                      │
└───────────────────────────┬───────────────────────────────────┘
                            │ Npgsql
                            ▼
                    PostgreSQL 18 Docker
         catalog | conversations | messaging | storefront | identity
```

Runtime invariants:

1. Only Host.Web is exposed publicly.
2. PostgreSQL and Ollama remain local/private.
3. Webhook acknowledgement never waits for Ollama.
4. AI does not execute SQL.
5. Commercial facts come from module contracts/DB, not model prose.
6. Inbound idempotency is structural.
7. Outbound intent is durable before Meta HTTP send.
8. Cross-module DB access is forbidden.

---

# 3. Solution structure

```text
WhatsAppMonitorAssistant.slnx
│
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
│
├── src/
│   ├── Host.Web/
│   │   ├── Program.cs
│   │   ├── Composition/
│   │   ├── Admin/
│   │   └── Health/
│   │
│   ├── BuildingBlocks/
│   │   ├── Application/
│   │   ├── Domain/
│   │   └── Messaging/
│   │
│   └── Modules/
│       ├── Catalog/
│       │   ├── Domain/
│       │   ├── Features/
│       │   ├── Contracts/
│       │   └── Infrastructure/
│       ├── Conversations/
│       ├── Messaging/
│       ├── Intelligence/
│       ├── Storefront/
│       └── Identity/
│
└── tests/
    ├── Unit.Tests/
    ├── Architecture.Tests/
    ├── Integration.Tests/
    ├── Contract.Tests/
    ├── E2E.Tests/
    └── Performance.Tests/
```

The app is a single deployable artifact but not a single-project monolith.

---

# 4. Module dependency rules

## 4.1 Ownership

| Module | Owns |
|---|---|
| Catalog | product models, ports, variants, price, quantity, search, catalogue audit |
| Conversations | customers, conversations, state, AI/Human mode |
| Messaging | webhook envelope, Inbox, Outbox, Meta transport |
| Intelligence | NLU DTOs, schemas, model/runtime adapters |
| Storefront | business info/FAQ |
| Identity | users/authentication/authorization |

## 4.2 Forbidden dependencies

A module may not:

- inject another module's DbContext;
- query another module's schema;
- reference another module's Infrastructure namespace;
- share mutable domain entities;
- expose EF entities as cross-module contracts.

## 4.3 Allowed communication

- narrow Contracts interfaces;
- immutable DTOs/read models;
- internal domain events;
- explicit integration events where asynchronous reaction is useful.

Architecture.Tests enforce this on every PR.

---

# 5. Vertical Slice / CQRS-style application model

Example:

```text
Modules/Catalog/Features/SearchProducts/
    Query.cs
    Handler.cs
    Result.cs
    Rules.cs

Modules/Catalog/Features/UpdateVariantPrice/
    Command.cs
    Handler.cs
    Validator.cs

Modules/Conversations/Features/ProcessInboundTurn/
    Command.cs
    Handler.cs
```

Core interfaces:

```csharp
public interface ICommand<TResult> { }
public interface IQuery<TResult> { }

public interface ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken ct);
}
```

No mandatory MediatR dependency is required.

---

# 6. Database topology

One PostgreSQL database, schema per persistent module:

```sql
CREATE SCHEMA IF NOT EXISTS catalog;
CREATE SCHEMA IF NOT EXISTS conversations;
CREATE SCHEMA IF NOT EXISTS messaging;
CREATE SCHEMA IF NOT EXISTS storefront;
CREATE SCHEMA IF NOT EXISTS identity;
```

No foreign keys across module schemas.

## 6.1 Catalog tables

```sql
CREATE TABLE catalog.product_model (
    id                bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    model_code        text          NOT NULL UNIQUE,
    brand             text          NOT NULL,
    model             text          NOT NULL,
    display_name      text          NOT NULL,
    size_inches       numeric(4,1)  NOT NULL,
    panel_type        text          NOT NULL,
    resolution_width  int           NOT NULL,
    resolution_height int           NOT NULL,
    refresh_rate      int           NOT NULL,
    description       text,
    search_tags       text[]        NOT NULL DEFAULT '{}',
    is_active         boolean       NOT NULL DEFAULT true,
    created_at        timestamptz   NOT NULL DEFAULT now(),
    updated_at        timestamptz   NOT NULL DEFAULT now(),
    CONSTRAINT ck_model_size CHECK (size_inches BETWEEN 10 AND 60),
    CONSTRAINT ck_model_panel CHECK (panel_type IN ('IPS','TN','VA','OLED','Other')),
    CONSTRAINT ck_model_resolution CHECK (resolution_width > 0 AND resolution_height > 0),
    CONSTRAINT ck_model_refresh CHECK (refresh_rate BETWEEN 24 AND 500)
);

CREATE TABLE catalog.product_model_port (
    id               bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    product_model_id bigint NOT NULL REFERENCES catalog.product_model(id) ON DELETE CASCADE,
    port_type        text   NOT NULL,
    count            int    NOT NULL DEFAULT 1,
    CONSTRAINT uq_model_port UNIQUE (product_model_id, port_type),
    CONSTRAINT ck_port_count CHECK (count BETWEEN 1 AND 16)
);

CREATE TABLE catalog.product_variant (
    id               bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    product_model_id bigint         NOT NULL REFERENCES catalog.product_model(id) ON DELETE RESTRICT,
    sku              text           NOT NULL UNIQUE,
    grade            text           NOT NULL,
    selling_price    numeric(12,2)  NOT NULL,
    quantity         int            NOT NULL DEFAULT 0,
    warranty_days    int            NOT NULL DEFAULT 0,
    warranty_notes   text,
    cosmetic_notes   text,
    is_active        boolean        NOT NULL DEFAULT true,
    created_at       timestamptz    NOT NULL DEFAULT now(),
    updated_at       timestamptz    NOT NULL DEFAULT now(),
    CONSTRAINT uq_variant_model_grade UNIQUE (product_model_id, grade),
    CONSTRAINT ck_variant_grade CHECK (grade IN ('A','B','C')),
    CONSTRAINT ck_variant_price CHECK (selling_price >= 0),
    CONSTRAINT ck_variant_quantity CHECK (quantity >= 0),
    CONSTRAINT ck_variant_warranty CHECK (warranty_days >= 0)
);

CREATE TABLE catalog.audit_log (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    entity_type text NOT NULL,
    entity_id   bigint,
    action      text NOT NULL,
    old_json    jsonb,
    new_json    jsonb,
    user_id     text,
    created_at  timestamptz NOT NULL DEFAULT now()
);
```

Price/quantity/active-state admin changes write audit rows in the same module transaction.

## 6.2 Storefront

```sql
CREATE TABLE storefront.business_info (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    key         text NOT NULL UNIQUE,
    answer_ar   text NOT NULL,
    answer_en   text,
    is_active   boolean NOT NULL DEFAULT true,
    updated_at  timestamptz NOT NULL DEFAULT now()
);
```

## 6.3 Conversations

```sql
CREATE TABLE conversations.customer (
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    whatsapp_number text NOT NULL UNIQUE,
    display_name    text,
    first_seen_at   timestamptz NOT NULL DEFAULT now(),
    last_seen_at    timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE conversations.conversation (
    id                bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    customer_id       bigint NOT NULL REFERENCES conversations.customer(id) ON DELETE CASCADE,
    mode              text NOT NULL DEFAULT 'AI',
    window_expires_at timestamptz,
    started_at        timestamptz NOT NULL DEFAULT now(),
    last_inbound_at   timestamptz,
    last_outbound_at  timestamptz,
    closed_at         timestamptz,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_conversation_mode CHECK (mode IN ('AI','Human','Closed'))
);

CREATE UNIQUE INDEX ux_conversation_open_customer
ON conversations.conversation(customer_id)
WHERE mode <> 'Closed';

CREATE TABLE conversations.conversation_state (
    conversation_id bigint PRIMARY KEY REFERENCES conversations.conversation(id) ON DELETE CASCADE,
    state_json      jsonb NOT NULL DEFAULT '{}'::jsonb,
    expires_at      timestamptz NOT NULL,
    updated_at      timestamptz NOT NULL DEFAULT now()
);
```

`state_json` stores IDs/filters/reference state only. Current price/quantity is always reloaded from Catalog.

## 6.4 Messaging Inbox

```sql
CREATE TABLE messaging.webhook_envelope (
    id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    envelope_hash bytea       NOT NULL UNIQUE,
    raw_body      jsonb       NOT NULL,
    received_at   timestamptz NOT NULL DEFAULT now(),
    processed_at  timestamptz
);

CREATE TABLE messaging.inbox_message (
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    envelope_id         bigint NOT NULL REFERENCES messaging.webhook_envelope(id) ON DELETE CASCADE,
    provider_message_id text NOT NULL UNIQUE,
    customer_external_id text NOT NULL,
    conversation_id     bigint,
    message_type        text NOT NULL,
    body                text,
    provider_timestamp  timestamptz NOT NULL,
    processing_status   text NOT NULL DEFAULT 'Pending',
    partition_key       text NOT NULL,
    attempts            int NOT NULL DEFAULT 0,
    run_after           timestamptz NOT NULL DEFAULT now(),
    claimed_at          timestamptz,
    processed_at        timestamptz,
    last_error          text,
    received_at         timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_inbox_status CHECK (
      processing_status IN ('Pending','Claimed','Processed','Failed','DeadLettered'))
);

CREATE INDEX ix_inbox_claim
ON messaging.inbox_message(run_after, id)
WHERE processing_status = 'Pending';
```

## 6.5 Messaging Outbox

```sql
CREATE TABLE messaging.outbox_message (
    id                   bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    conversation_id      bigint NOT NULL,
    customer_external_id text NOT NULL,
    correlation_id       text NOT NULL,
    sender               text NOT NULL DEFAULT 'AI',
    body                 text NOT NULL,
    body_hash            bytea NOT NULL,
    provider_message_id  text UNIQUE,
    delivery_status      text NOT NULL DEFAULT 'Pending',
    partition_key        text NOT NULL,
    attempts             int NOT NULL DEFAULT 0,
    max_attempts         int NOT NULL DEFAULT 5,
    run_after            timestamptz NOT NULL DEFAULT now(),
    claimed_at           timestamptz,
    sent_at              timestamptz,
    last_error           text,
    created_at           timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_outbox_sender CHECK (sender IN ('AI','Agent','System')),
    CONSTRAINT ck_outbox_status CHECK (
      delivery_status IN ('Pending','Claimed','Sent','Failed','DeadLettered')),
    CONSTRAINT ck_outbox_body_hash CHECK (body_hash = sha256(body::bytea))
);

CREATE INDEX ix_outbox_claim
ON messaging.outbox_message(run_after, id)
WHERE delivery_status = 'Pending';
```

Outbound content is immutable after creation; only delivery bookkeeping changes.

---

# 7. EF Core configuration

Use one DbContext per persistent module:

```text
CatalogDbContext       → catalog
ConversationDbContext  → conversations
MessagingDbContext     → messaging
StorefrontDbContext    → storefront
IdentityDbContext      → identity
```

Example:

```csharp
services.AddDbContextPool<CatalogDbContext>(o =>
    o.UseNpgsql(connectionString,
        x => x.MigrationsHistoryTable("__ef_migrations", "catalog")));
```

Rules:

- Code First migrations per context;
- no `EnsureCreated()` outside disposable tests;
- no cross-module migration objects;
- no FK/view against another module schema.

---

# 8. AI adapter

## 8.1 Interface

```csharp
public interface IAiNluClient
{
    Task<NluResult> AnalyzeAsync(
        string message,
        ConversationContext context,
        CancellationToken ct);
}
```

No Catalog/Conversations code references Ollama/Qwen directly.

## 8.2 Controlled Demo Candidate (frozen)

The frozen local client-demo configuration is the **Controlled Demo Candidate** defined by
docs/PLAN.md section 13.3. It is deliberately not a general benchmark pass — Issue #8 measured
62.3% general intent accuracy against the ≥ 90% threshold, a documented general benchmark FAIL —
and it is not a pilot/production candidate.

```text
qwen3.5:2b-q4_K_M
```

Ollama on x86 Mac is CPU-only.

Use:

```json
{
  "Ai": {
    "Provider": "Ollama",
    "BaseUrl": "http://127.0.0.1:11434",
    "Model": "qwen3.5:2b-q4_K_M",
    "TimeoutSeconds": 20,
    "Temperature": 0,
    "ContextTokens": 4096
  }
}
```

The frozen candidate also fixes the request shape and lifecycle:

```text
stream: false
think: false
structured output: the committed JSON schema of section 8.3
PromptVersion: nlu-system-prompt-v3
PromptSha256: 2139120c08b3ad01a5389f986ae6a4a7e884da372591a951a6efaea225237d3a
retry: one corrective retry maximum, only when an actual model reply is invalid, unparsable or
       schema-invalid; a transport timeout or connectivity failure is never schema-retried and
       stays visible as an infrastructure failure
pre-warm: required before the client demo (section 29)
```

`PromptSha256` is the SHA-256 of the runtime UTF-8 bytes of `NluSystemPrompt.Text` (lowercase
hexadecimal), not the hash of the source file: the frozen prompt content is identified by the exact
value the application sends. `PromptVersion` is the version string carried by `NluContract`.

This section is the single place the demo AI configuration is written down. The Intelligence
adapter of Issue #10 consumes exactly these values and owns adding the `Ai` section to the host
configuration; alternative values are a new decision, not a default.

`ContextTokens` is an application-level cap/target, not a claim that the model cannot support more.

## 8.3 Structured output

Conceptual JSON schema:

```json
{
  "type": "object",
  "properties": {
    "intent": {"type":"string"},
    "brand": {"type":["string","null"]},
    "modelCode": {"type":["string","null"]},
    "sizeInches": {"type":["number","null"]},
    "panel": {"type":["string","null"]},
    "resolution": {"type":["string","null"]},
    "minRefreshRate": {"type":["integer","null"]},
    "requiredPorts": {"type":"array","items":{"type":"string"}},
    "grades": {"type":"array","items":{"type":"string"}},
    "budgetType": {"type":"string","enum":["None","Soft","Hard","Range"]},
    "budgetTarget": {"type":["number","null"]},
    "budgetMin": {"type":["number","null"]},
    "budgetMax": {"type":["number","null"]},
    "useCase": {"type":["string","null"]},
    "reference": {"type":["string","null"]}
  },
  "required": ["intent","requiredPorts","grades","budgetType"]
}
```

One retry maximum, then fixed clarification/handoff.

## 8.4 Demo warm-up

Before live demo:

1. call `/api/chat` with a tiny schema-constrained example;
2. wait for success;
3. call a second representative prompt;
4. verify latency;
5. keep the demo model loaded while the client session is active.

Do not judge first-turn cold-load time as normal warm response time.

---

# 9. Intent routing

```text
Greeting              → fixed/short safe reply
ProductSearch         → Catalog.SearchProducts
ProductDetails        → Catalog.GetProductDetails
ProductComparison     → current shortlist + Catalog facts
AvailabilityCheck     → Catalog.GetAvailability
PriceCheck            → Catalog.GetPrice
BusinessInfo          → Storefront.GetBusinessInfo
HumanHandoff          → Conversations.SetMode(Human)
UnsupportedMedia      → fixed response
OutOfScope            → fixed response
```

Ambiguous parsing → concise clarification; never guess.

---

# 10. Search implementation

Eligibility first:

```sql
SELECT
    m.id AS model_id,
    m.model_code,
    m.brand,
    m.model,
    m.display_name,
    m.size_inches,
    m.panel_type,
    m.resolution_width,
    m.resolution_height,
    m.refresh_rate,
    m.search_tags,
    v.id AS variant_id,
    v.sku,
    v.grade,
    v.selling_price,
    v.quantity,
    v.warranty_days,
    v.warranty_notes,
    v.cosmetic_notes
FROM catalog.product_variant v
JOIN catalog.product_model m ON m.id = v.product_model_id
WHERE m.is_active
  AND v.is_active
  AND v.quantity > 0
  AND (:brand IS NULL OR lower(m.brand) = lower(:brand))
  AND (:size IS NULL OR abs(m.size_inches - :size) <= :size_tolerance)
  AND (:panel IS NULL OR m.panel_type = :panel)
  AND (:min_refresh IS NULL OR m.refresh_rate >= :min_refresh)
  AND (:budget_min IS NULL OR v.selling_price >= :budget_min)
  AND (:budget_max IS NULL OR v.selling_price <= :budget_max)
  AND (:model_code IS NULL OR lower(m.model_code) = lower(:model_code))
ORDER BY
  CASE WHEN :model_code IS NOT NULL AND lower(m.model_code)=lower(:model_code) THEN 0 ELSE 1 END,
  CASE v.grade WHEN 'A' THEN 1 WHEN 'B' THEN 2 WHEN 'C' THEN 3 END,
  abs(v.selling_price - COALESCE(:budget_target, v.selling_price)),
  v.selling_price
LIMIT 20;
```

Required ports use `EXISTS`/set logic requiring all requested port types.

At most one recommendation item per model. Alternate grade may be mentioned inline.

No pgvector/embeddings in this version.

---

# 11. Budget semantics

```csharp
(decimal? min, decimal? max) ResolveBudget(BudgetInput budget)
{
    return budget.Type switch
    {
        BudgetType.Hard  => (null, budget.Target),
        BudgetType.Soft  => (null, budget.Target * (1 + softTolerance)),
        BudgetType.Range => (budget.Min, budget.Max),
        _                => (null, null)
    };
}
```

Invariant:

> No fallback path may increase a Hard budget maximum.

No match under hard ceiling → say no matching item exists; do not silently show higher prices.

---

# 12. Deterministic renderer

```csharp
public sealed record ProductResult(
    long ModelId,
    long VariantId,
    string DisplayName,
    string Grade,
    decimal Price,
    int Quantity,
    int WarrantyDays,
    IReadOnlyList<string> Ports,
    string? CosmeticNotes);
```

Renderer owns:

- name;
- price;
- quantity/availability text;
- grade;
- warranty;
- exact specs;
- business info values.

Immediately before Outbox enqueue:

- reload selected variants;
- verify active;
- verify quantity > 0 for availability claim;
- use current price;
- re-render if values changed.

---

# 13. Conversation state

State example:

```json
{
  "shortlist": [
    {"position":1,"modelId":10,"variantId":21},
    {"position":2,"modelId":11,"variantId":25}
  ],
  "lastModelId": 10,
  "lastVariantId": 21,
  "lastIntent": "ProductSearch",
  "lastFilters": {
    "brand":"Dell",
    "size":24,
    "budgetType":"Soft",
    "budget":3000
  }
}
```

State is UX context only. Commercial facts are always reloaded.

---

# 14. Webhook boundary

Verification GET handles current Meta verification parameters using a secret verify token.

POST path:

```text
read raw body
→ validate Meta authenticity/signature
→ parse provider message id / sender
→ hash raw body
→ transaction:
     INSERT webhook_envelope ON CONFLICT DO NOTHING
     INSERT inbox_message ON CONFLICT(provider_message_id) DO NOTHING
→ commit
→ return 200
```

No Ollama/DB search/business orchestration in the HTTP webhook request.

---

# 15. Inbox / Outbox workers

Use `BackgroundService` + PostgreSQL durable queues.

Inbox claim uses `FOR UPDATE SKIP LOCKED` and per-conversation `partition_key`.

Conceptual claim:

```sql
WITH claimable AS (
    SELECT i.id
    FROM messaging.inbox_message i
    WHERE i.processing_status='Pending'
      AND i.run_after <= now()
      AND NOT EXISTS (
          SELECT 1 FROM messaging.inbox_message c
          WHERE c.partition_key=i.partition_key
            AND c.processing_status='Claimed')
    ORDER BY i.id
    FOR UPDATE SKIP LOCKED
    LIMIT :batch
)
UPDATE messaging.inbox_message i
SET processing_status='Claimed',
    claimed_at=now(),
    attempts=attempts+1
FROM claimable
WHERE i.id=claimable.id
RETURNING i.*;
```

Outbox uses equivalent claiming.

The local Mac demo normally runs a single worker instance, but the logic remains horizontally safe for later VPS replicas.

---

# 16. Inbound orchestration

```text
Messaging worker
  ↓ IProcessInboundTurn
Conversations.ProcessInboundTurn
  ├─ get/create customer + conversation
  ├─ refresh service window from inbound provider timestamp
  ├─ Human? record only / stop AI
  ├─ unsupported media? fixed response
  ├─ load context
  ├─ Intelligence NLU
  ├─ route intent
  │    ├─ Catalog queries
  │    ├─ Storefront queries
  │    └─ Handoff command
  ├─ deterministic render
  ├─ reload current commercial facts
  ├─ save context
  └─ write Outbox
```

---

# 17. WhatsApp service-window behavior

`window_expires_at` updates only on inbound customer messages.

Before every reactive free-form send, ensure the window is still open.

Lean demo contains no proactive template workflow. If closed:

- do not send free-form;
- flag conversation;
- wait for another inbound customer message.

---

# 18. Admin UI

## Dashboard
- DB health
- Ollama health
- WhatsApp config/health
- models/variants
- out-of-stock variants
- conversations today
- recent errors

## Catalogue
- model CRUD
- variant CRUD
- price/quantity
- activate/deactivate
- audit entries

## Business Info
Allowlisted key/value editor.

## Conversations
- transcript
- mode
- take over
- manual reply while allowed
- release to AI

---

# 19. Security baseline

Local/demo:

- expose only ASP.NET through Cloudflare;
- PostgreSQL bound to localhost/private Docker only;
- Ollama bound to localhost;
- admin requires auth;
- Meta secrets via user-secrets/environment;
- no credentials in git;
- rate limit webhook processing;
- structured logs without tokens/full phone numbers at Info;
- global AI kill switch;
- liveness/readiness health endpoints.

Never expose ports `5432` or `11434` through the public tunnel/router.

---

# 20. Intel Mac resource profile

The design intentionally avoids running a large container stack.

## 20.1 Native processes during coding/demo

```text
ASP.NET Core
Ollama
cloudflared
IDE/editor
browser
```

## 20.2 Docker Desktop responsibilities

```text
PostgreSQL development DB
Testcontainers only when tests run
```

Do not enable local Kubernetes.

## 20.3 Docker Desktop memory policy

Because the Mac has 16 GB total RAM:

- start with a conservative Docker Desktop memory allocation (approximately 3–4 GB is sufficient for the database/test containers in this project);
- adjust only after observing real memory pressure;
- do not allocate most system RAM to Docker;
- stop disposable Testcontainers after test runs.

This is a local resource recommendation, not an application requirement.

## 20.4 AI memory/performance policy

- one local model at a time;
- initial Q4_K_M 2B model file is about 1.9 GB;
- actual runtime RSS depends on context/cache/runtime and must be measured;
- keep demo context 2K–4K;
- do not load 7B/8B models for the proof-of-concept;
- pre-warm before presentation.

---

# 21. PostgreSQL on Docker Desktop

`compose.yaml`:

```yaml
services:
  postgres:
    image: postgres:18
    container_name: monitor-postgres
    restart: unless-stopped
    environment:
      POSTGRES_DB: monitor_ai
      POSTGRES_USER: monitor_app
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    ports:
      - "127.0.0.1:5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U monitor_app -d monitor_ai"]
      interval: 5s
      timeout: 5s
      retries: 10

volumes:
  postgres_data:
```

For PostgreSQL 18+, mount the named volume at `/var/lib/postgresql`.

---

# 22. Local configuration

Use `dotnet user-secrets` for real credentials.

Configuration shape:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=127.0.0.1;Port=5432;Database=monitor_ai;Username=monitor_app;Password=FROM_SECRET"
  },
  "Ai": {
    "Provider": "Ollama",
    "BaseUrl": "http://127.0.0.1:11434",
    "Model": "qwen3.5:2b-q4_K_M",
    "TimeoutSeconds": 20,
    "Temperature": 0,
    "ContextTokens": 4096
  },
  "WhatsApp": {
    "ApiVersion": "CONFIGURE_CURRENT_META_VERSION",
    "PhoneNumberId": "FROM_SECRET",
    "WabaId": "FROM_SECRET"
  }
}
```

Do not hard-code a Meta Graph API version into architecture documents; configure the currently supported version at implementation time.

---

# 23. Cloudflare demo ingress

Local app:

```text
http://127.0.0.1:5000
```

Run:

```bash
cloudflared tunnel --url http://127.0.0.1:5000
```

Meta callback becomes:

```text
https://<random>.trycloudflare.com/api/whatsapp/webhook
```

Quick Tunnel is for development/demo only. Restart can change the hostname.

---

# 24. Automated testing design

Testing is part of the baseline, not a later hardening phase.

## 24.1 Unit.Tests

No PostgreSQL, no network, no Ollama.

Required coverage:

- normalization;
- aliases;
- hard/soft budget;
- state merge;
- shortlist resolver;
- ranking/grouping;
- renderer;
- window logic;
- NLU mapping/validation;
- handoff;
- safe fallback.

## 24.2 Architecture.Tests

ArchUnitNET rules:

```text
Domain/Application !→ Infrastructure
Module A !→ Module B.Infrastructure
Module A !→ Module B DbContext
Host.Web = composition root
Controllers/endpoints = transport only
Cross-module public API = .Contracts only
```

## 24.3 Integration.Tests

Use Testcontainers PostgreSQL.

Required:

- all migrations from empty DB;
- constraints/indexes;
- search translation/results;
- quantity zero exclusion;
- current price visibility;
- Inbox concurrent dedupe;
- Outbox single claim;
- SKIP LOCKED behavior;
- transaction rollback;
- handoff persistence;
- WebApplicationFactory HTTP paths.

The developer Mac runs these tests with Docker Desktop. They do not need to run continuously while coding or during client presentation.

## 24.4 Contract.Tests

Fake HTTP fixtures:

### Meta
- webhook verification;
- valid text event;
- duplicate;
- unsupported media;
- malformed payload;
- invalid signature;
- outbound 2xx/4xx/429/timeout.

### Ollama
- schema request;
- valid result;
- invalid/malformed result;
- timeout;
- server/model unavailable;
- retry/fallback.

## 24.5 E2E.Tests

```text
fake Meta inbound
→ real Host.Web
→ real PostgreSQL Testcontainer
→ real Inbox worker
→ fake deterministic Ollama
→ real Catalog/Storefront
→ real renderer
→ real Outbox worker
→ fake Meta outbound
```

Assert exactly one outbound response and correct DB state.

## 24.6 Playwright

Admin smoke:

1. login;
2. create model;
3. add Grade A;
4. edit price;
5. edit quantity;
6. deactivate/reactivate;
7. edit working hours;
8. view conversation;
9. take over;
10. release to AI.

Chromium headless smoke on each PR is sufficient initially.

---

# 25. Performance gates by phase

## 25.1 Mac client-demo gate

The Mac is not a load-test target.

Required:

- webhook request returns quickly after durable Inbox write;
- one real customer conversation works repeatedly;
- two near-simultaneous inbound turns remain ordered;
- duplicate delivery creates one reply;
- warm NLU median target ≤8s;
- warm NLU p95 target ≤12s;
- zero fact fabrication path.

The AI configuration measured by these gates is the Controlled Demo Candidate of section 8.2. Its
Issue #8 general benchmark failure stays documented, and passing these gates is client-demo
acceptance, not pilot/production acceptance.

## 25.2 Future pilot gate

After VPS provisioning:

- ≥20 concurrent active conversation simulations;
- workers ≥2;
- DB pool pressure;
- slow AI;
- Meta 429;
- duplicate burst;
- zero duplicate sends.

Do not reject the local proof-of-concept because it does not meet future production concurrency.

---

# 26. GitHub Actions

PR pipeline:

```text
checkout
→ setup .NET 10
→ restore --locked-mode
→ dotnet format --verify-no-changes
→ build -warnaserror
→ Unit.Tests
→ Architecture.Tests
→ Integration.Tests + PostgreSQL Testcontainer
→ Contract.Tests
→ E2E.Tests
→ Playwright critical smoke
→ coverage
→ dependency vulnerability scan
→ Docker image build
```

CI executes the heavy deterministic validation on Linux runners, so the 16 GB Mac remains a comfortable interactive development/demo machine.

Coverage policy:

- ≥80% line coverage for deterministic Domain/Application rule assemblies;
- critical invariants always require explicit named tests;
- periodic Stryker.NET mutation tests for budget, normalization, availability and renderer rules.

---

# 27. Observability

Use structured `ILogger` and OpenTelemetry-compatible correlation.

Correlation flows:

```text
WebhookEnvelope
→ Inbox
→ Conversation
→ NLU
→ Catalog/Storefront
→ Renderer
→ Outbox
→ Meta
```

Minimum metrics:

- NLU latency/failure;
- search latency;
- inbound duplicates;
- outbound failures;
- human handoffs;
- queue depth/time.

---

# 28. Mac benchmark procedure

Benchmark 50+ realistic prompts.

Store:

```json
{
  "id": "BUDGET-HARD-001",
  "input": "عايز ديل ومش عايز أعدي 3000",
  "expected": {
    "intent": "ProductSearch",
    "brand": "Dell",
    "budgetType": "Hard",
    "budgetTarget": 3000
  }
}
```

Record:

- valid schema;
- intent;
- filters;
- hard/soft;
- total duration;
- prompt/eval duration if returned;
- output token count.

Decision:

```text
Qwen3.5 2B Q4_K_M
    ├─ quality + latency pass → freeze for demo
    ├─ quality pass / latency fail → test Qwen3 1.7B
    └─ quality fail / latency headroom → optionally test Qwen3.5 4B
```

The tree above stays the general benchmark record. The Issue #8 measurement on the physical Intel
Mac produced:

```text
Qwen3.5 2B Q4_K_M  → quality FAIL (intent 62.3% vs >= 90%), latency PASS
                     (median 6.41 s, p95 8.17 s; schema 100%, hard budget 100% (10/10))
Qwen3.5 4B Q4_K_M  → warm-up blocked; no measured metrics
Qwen3 1.7B         → not tested; its branch (quality pass / latency fail) did not occur
```

**The general benchmark stays FAIL for every measured candidate.** The controlled-demo exception is
separate from the tree above:

```text
general quality fail
  + schema reliability pass (>= 98% after one retry)
  + hard-budget safety pass (100%)
  + acceptable local warm latency
  + no suitable stronger local candidate
→ the measured 2B configuration may be frozen as a Controlled Demo Candidate (section 8.2);
  the client presentation still requires the complete gate of docs/demo/DEMO-CRITICAL-GATE-v1.md to
  pass in two consecutive post-warm runs (section 29). That document is the authoritative
  operational definition of the gate — scenario list, between-run reset, exact run mechanics,
  latency calculation and evidence recording — and this section does not restate those mechanics.
```

This exception never re-labels the general benchmark as a pass and never promotes the configuration
to pilot/production; docs/PLAN.md section 19 keeps the ≥ 90% general gate for that step.

No model is accepted because it is newer/larger.

---

# 29. Client demo startup runbook

Do this before the meeting:

```text
1. Connect Mac to power
2. Use stable Wi-Fi/Ethernet where possible
3. Close unnecessary heavy apps/browser tabs
4. Start Docker Desktop
5. Start PostgreSQL container
6. Verify DB health
7. Start Ollama
8. Verify selected model exists
9. Pre-warm model with 2 requests
10. Apply/check EF migrations
11. Seed/verify demo catalogue and BusinessInfo
12. Start ASP.NET Core
13. Check /health/live and /health/ready
14. Start Cloudflare Quick Tunnel
15. Update Meta callback URL if hostname changed
16. Verify webhook challenge/subscription
17. Send a real WhatsApp smoke message
18. Verify exactly one correct response
19. Do not run Testcontainers/Playwright/load tests during presentation
20. Start client demo
```

Ownership of the AI steps above: Issue #18 executes the two-request pre-warm, and Issue #19
executes the complete `docs/demo/DEMO-CRITICAL-GATE-v1.md` twice consecutively and records the
client-demo acceptance evidence. The 60-case general benchmark is never run during the client
presentation.

---

# 30. Demo failure-safe behavior

If Ollama fails:

- do not guess;
- fixed short apology/human option;
- flag health as degraded.

If PostgreSQL fails:

- do not answer product/price/stock from memory;
- fixed failure/handoff response if possible.

If Cloudflare stops:

- demo endpoint is offline; restart tunnel and update Meta callback.
- this is acceptable because the environment is explicitly a demo, not production.

---

# 31. Client-demo Definition of Done

Ready when:

- macOS meets support precondition;
- .NET app builds on Intel Mac;
- Docker/PostgreSQL works;
- module migrations apply from zero;
- unit/architecture/integration/contract/E2E/Playwright CI gates pass;
- the Controlled Demo Candidate configuration of section 8.2 is frozen;
- the complete `docs/demo/DEMO-CRITICAL-GATE-v1.md` gate passes in two consecutive post-warm
  executions, with its operational mechanics — scenarios, between-run reset, timing and evidence —
  taken from that document;
- webhook HTTPS verification succeeds;
- live WhatsApp inbound/outbound succeeds;
- duplicate inbound gives one response;
- hard budget is preserved;
- current price/stock/specs come from PostgreSQL;
- business FAQ comes from Storefront;
- reference resolution works for demo cases;
- Human mode works;
- Ollama/DB failure does not fabricate facts;
- PostgreSQL/Ollama are private;
- two consecutive pre-demo smoke runs pass.

This definition of done is client-demo acceptance only. The frozen configuration is not a
pilot/production candidate, and the Issue #8 general benchmark failure stays documented next to
the demo evidence.

---

# 32. Future migration to Linux VPS

After client accepts the proof-of-concept:

1. provision Linux VPS;
2. install Docker/Ollama;
3. pull the selected model;
4. deploy same application artifact;
5. run module migrations;
6. restore/import real catalogue;
7. configure stable HTTPS;
8. update Meta callback;
9. benchmark the intended AI model on that host and confirm the general gate of docs/PLAN.md
   section 13.3 — general intent accuracy ≥ 90%, hard-budget classification 100%, structured
   schema success ≥ 98% after one retry — plus latency targets revalidated for that host;
10. run pilot load gate;
11. configure backup/monitoring;
12. run live smoke.

Step 9 is the pilot/production AI gate: until the intended model passes it on that host, the
environment stays a demo. A stronger model or a different provider may be chosen for that step; the
AI provider/model stays replaceable behind the adapter and its configuration, and business modules
and commercial-fact ownership do not change. No exact future VPS, model or vendor is prescribed
here.

Architecture/module/business code should not change for this move.

---

# 33. Explicit non-implementation list

Do not create these in v3.2 unless scope changes:

```text
product_model_embedding
pgvector / HNSW
embedding worker
product images/media pipeline
lead / purchase_request
stock_notification_subscription
notification_delivery_attempt
stock movement ledger
advanced import workflow
proactive template workflow
multi-tenant layer
message broker
Kubernetes
```

---

# 34. Technical references checked for Mac baseline

- .NET macOS support: https://learn.microsoft.com/en-us/dotnet/core/install/macos
- Docker Desktop Mac Intel: https://docs.docker.com/desktop/setup/install/mac-install/
- Docker Desktop release support: https://docs.docker.com/desktop/release-notes/
- Ollama macOS/x86: https://docs.ollama.com/macos
- PostgreSQL macOS amd64: https://www.postgresql.org/download/macosx/
- Qwen3.5 Ollama tags: https://ollama.com/library/qwen3.5/tags
- Cloudflare Quick Tunnel: https://developers.cloudflare.com/cloudflare-one/networks/connectors/cloudflare-tunnel/do-more-with-tunnels/trycloudflare/

---

# 35. Final technical baseline

```text
Architecture
------------
Modular Monolith
Vertical Slice / CQRS-style
schema-per-module PostgreSQL
Inbox/Outbox
replaceable AI adapter
deterministic commercial rendering

Quality
-------
Unit Tests
Architecture Tests
Integration Tests with PostgreSQL Testcontainers
Contract Tests
E2E Tests
Playwright Admin Smoke
GitHub Actions
coverage + vulnerability scan

Mac Demo Runtime
----------------
Intel Mac 2.3 GHz Quad-Core i7
16 GB LPDDR4X
macOS 14+
.NET 10 native
ASP.NET Core native
Ollama x86 CPU-only native
Qwen3.5 2B Q4_K_M initial candidate
Docker Desktop Intel
PostgreSQL 18 Docker
Cloudflare Quick Tunnel

Purpose
-------
prove the product idea to the client safely and credibly;
not prove production throughput or 24/7 availability.
```
