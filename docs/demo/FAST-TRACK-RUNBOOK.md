# Controlled Client Demo Fast Track — operator runbook (Issue #15)

This runbook takes a fresh clone to a running demo host: PostgreSQL, migrations, the frozen demo
data, Ollama, the public listener, the Cloudflare Quick Tunnel and the WhatsApp callback. It is the
procedure the human Phase 0.5 first-light checkpoint follows, and the reset procedure Issue #19 uses
between gate runs. It implements docs/TECHNICAL.md section 36; it does not change the gate
(`docs/demo/DEMO-CRITICAL-GATE-v1.md`) or the frozen AI profile of section 8.2.

Everything listens on `127.0.0.1`. Only the public listener `127.0.0.1:5000` is ever tunnelled.
PostgreSQL (`5432`) and Ollama (`11434`) are never exposed.

## 1. Prerequisites

- macOS with Docker Desktop running, the .NET SDK of `global.json`, `cloudflared` and Ollama.
- The frozen model is pulled: `ollama list` shows `qwen3.5:2b-q4_K_M`.
- Meta: an app with WhatsApp, a test or production phone number, its Phone Number ID, the WhatsApp
  Business Account ID, a currently valid access token (record its expiry; never call it permanent),
  the app secret, and the demo phone(s) registered as allowed recipients.
- Do not update Ollama or Docker Desktop between the final acceptance run and the client demo.

Every command below runs from the repository root. Evidence and logs go to `~/demo-evidence`,
outside the repository, so they can never be committed:

```bash
mkdir -p ~/demo-evidence
```

## 2. Local environment file

Create `.env.demo` in the repository root. It is git-ignored (`.env.*`) and must never be committed
or pasted anywhere.

```bash
# PostgreSQL: one password, used by Docker Compose and by the application.
POSTGRES_PASSWORD='<local password>'
ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=5432;Database=monitor_ai;Username=monitor_app;Password=<local password>"
MONITOR_DESIGN_TIME_CONNECTION="Host=127.0.0.1;Port=5432;Database=monitor_ai;Username=monitor_app;Password=<local password>"

# Catalog search tolerances: controlled-demo values only, not pilot or production defaults.
Catalog__Search__SizeToleranceInches=0.5
Catalog__Search__SoftBudgetTolerance=0.10

# WhatsApp Cloud API (values from the Meta app dashboard).
WhatsApp__ApiVersion='<Graph API version shown in the API Setup sample request, for example v23.0>'
WhatsApp__PhoneNumberId='<phone number id>'
WhatsApp__WabaId='<WhatsApp Business Account id>'
WhatsApp__VerifyToken='<random string: openssl rand -hex 24>'
WhatsApp__AppSecret='<app secret>'
WhatsApp__AccessToken='<access token>'

# The public listener. Only this address is tunnelled.
ASPNETCORE_URLS=http://127.0.0.1:5000
```

Load it into every shell that runs a command below:

```bash
set -a; . ./.env.demo; set +a
```

## 3. Start PostgreSQL

```bash
docker compose up -d
docker compose ps          # monitor-postgres must report (healthy)
```

## 4. Restore the pinned EF tool

```bash
dotnet tool restore
dotnet ef --version        # 10.0.8
```

## 5. Apply every module migration

Each module owns its schema and migration history. Apply all five from an empty or existing
database; applying an up-to-date module is a no-op.

```bash
for module in Catalog:CatalogDbContext Conversations:ConversationDbContext Messaging:MessagingDbContext Storefront:StorefrontDbContext Identity:IdentityDbContext; do
  project="src/Modules/${module%%:*}"; context="${module##*:}"
  dotnet ef database update --project "$project" --startup-project "$project" --context "$context" || break
done
```

Confirm nothing is pending: every migration listed by the command below must appear without
`(Pending)`.

```bash
for module in Catalog:CatalogDbContext Conversations:ConversationDbContext Messaging:MessagingDbContext Storefront:StorefrontDbContext Identity:IdentityDbContext; do
  project="src/Modules/${module%%:*}"; context="${module##*:}"
  dotnet ef migrations list --project "$project" --startup-project "$project" --context "$context"
done
```

## 6. Seed, reset and verify the demo data

Build the operator tool once and define a shell helper for it:

```bash
dotnet build tools/DemoOps
demoops() { dotnet tools/DemoOps/bin/Debug/net10.0/DemoOps.dll "$@"; }
```

```bash
demoops seed
demoops reset  --customer <main wa_id> [--customer <preflight wa_id>]
demoops verify --customer <main wa_id> [--customer <preflight wa_id>]
```

- `<wa_id>` is the WhatsApp number exactly as Meta sends it: digits only, country code first, no
  `+` (for example `2010xxxxxxxx`).
- `seed` only inserts what is missing: the 20 demo models with 25 graded variants and the seven
  approved business-info answers. It never overwrites a value.
- `reset` removes the named customers' Messaging and Conversations rows (so no reference, shortlist
  or Human mode is carried into the next run) and restores every demo price, quantity, active state
  and business-info answer through the audited module contracts (actor `demo-reset`). It refuses,
  changing nothing, while any of the customers' messages is still Pending, Claimed or Failed: wait
  for the workers to finish and run it again.
- `verify` prints `PASS`/`FAIL` per check, the hard-budget (DEMO-04) and soft-budget (DEMO-03)
  search results, and a `BASELINE` block with every frozen value. Keep its output as evidence.

Exit codes: `0` success, `1` unexpected error, `2` reset refused, `3` verify failed,
`4` webhook send failed, `64` usage error.

## 7. Start Ollama for the demo

Quit the Ollama menu-bar app first, so only this server owns the port and keeps the model resident.

```bash
OLLAMA_KEEP_ALIVE=-1 ollama serve
```

In another shell:

```bash
curl -s http://127.0.0.1:11434/api/version
curl -s http://127.0.0.1:11434/api/tags      # must list qwen3.5:2b-q4_K_M
```

Record the version. The two-request pre-warm of docs/TECHNICAL.md section 8.4 belongs to the final
acceptance (Issue #19); it sends no `keep_alive` field because residency comes from
`OLLAMA_KEEP_ALIVE=-1`:

```bash
curl -s http://127.0.0.1:11434/api/chat -d '{"model":"qwen3.5:2b-q4_K_M","stream":false,"think":false,"options":{"temperature":0,"num_ctx":4096},"format":{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"]},"messages":[{"role":"user","content":"Return {\"ok\":true}"}]}'
curl -s http://127.0.0.1:11434/api/chat -d '{"model":"qwen3.5:2b-q4_K_M","stream":false,"think":false,"options":{"temperature":0,"num_ctx":4096},"format":{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"]},"messages":[{"role":"user","content":"عندك ديل 24؟"}]}'
curl -s http://127.0.0.1:11434/api/ps        # the model is resident
```

No periodic warm pings.

## 8. Start the host

```bash
set -a; . ./.env.demo; set +a
dotnet run --project src/Host.Web --no-launch-profile 2>&1 | tee ~/demo-evidence/host-$(date +%Y%m%d-%H%M%S).log
```

In another shell:

```bash
curl -i http://127.0.0.1:5000/health/live      # 200
curl -i http://127.0.0.1:5000/health/ready     # 200 only when PostgreSQL and the frozen model are ready
```

`/health/ready` answers 503 while PostgreSQL is unreachable or the frozen model is missing from
Ollama; its body names the failing check.

## 9. Open the tunnel (public listener only)

```bash
cloudflared tunnel --url http://127.0.0.1:5000
```

Record the generated `https://<name>.trycloudflare.com` hostname. Never tunnel `5432`, `11434` or
any other local port. A new tunnel gets a new hostname, so every tunnel start or restart repeats
section 10 and a real smoke message.

## 10. Verify the WhatsApp callback

Local and through the tunnel, the verification challenge must be echoed back:

```bash
curl -s "http://127.0.0.1:5000/api/whatsapp/webhook?hub.mode=subscribe&hub.verify_token=$WhatsApp__VerifyToken&hub.challenge=fl-check"
curl -s "https://<name>.trycloudflare.com/api/whatsapp/webhook?hub.mode=subscribe&hub.verify_token=$WhatsApp__VerifyToken&hub.challenge=fl-check"
```

Both print `fl-check`. Then, in the Meta app dashboard (WhatsApp › Configuration): Callback URL
`https://<name>.trycloudflare.com/api/whatsapp/webhook`, Verify token = `$WhatsApp__VerifyToken`,
**Verify and save**, and subscribe to the `messages` field. Meta notes that some webhooks are not
sent while an app is in Development mode: if a real message produces no inbound request, switch the
app to Live.

## 11. Signed webhook capture and replay

A capture file holds one exact Meta text-message payload. Sending it posts those exact bytes with a
fresh `X-Hub-Signature-256`, so sending the same file twice is a real duplicate delivery (same bytes,
same provider message id) that must produce exactly one reply.

```bash
demoops capture create --from <wa_id> --text 'السلام عليكم' --out ~/demo-evidence/dup.json [--id <provider_message_id>]
demoops capture send --file ~/demo-evidence/dup.json      # prints SEND HTTP 200
demoops capture send --file ~/demo-evidence/dup.json      # the duplicate: HTTP 200, no second reply
```

`capture create` refuses to overwrite an existing file. `capture send` posts to
`http://127.0.0.1:5000/api/whatsapp/webhook` unless `--url` is given, and exits `4` unless the
answer is HTTP 200. The persisted `jsonb` envelope is never used for replay: it cannot reproduce the
original byte stream.

## 12. Phase 0.5 first-light checklist

1. Sections 2 to 10 done; `demoops verify --customer <main wa_id>` exits `0`.
2. From the main demo phone, send one real message: `عندك ديل 24؟`.
3. Exactly one reply arrives on the phone.
4. The database shows one Processed Inbox row and one Sent Outbox row with a provider id:

   ```bash
   docker exec monitor-postgres psql -U monitor_app -d monitor_ai -c \
     "SELECT processing_status, count(*) FROM messaging.inbox_message GROUP BY 1;" -c \
     "SELECT delivery_status, provider_message_id FROM messaging.outbox_message ORDER BY id;"
   ```

5. Record the evidence on Issue #14 (see the fast-track plan): time, `main` SHA, Ollama version,
   tunnel hostname, Meta verification result, provider ids and row statuses, a masked phone
   screenshot, the `/health/ready` response, and
   `lsof -nP -iTCP -sTCP:LISTEN | egrep ':(5000|5432|11434)'` showing loopback bindings only.
6. Finish with section 13.

## 13. Reset between sessions

```bash
demoops reset  --customer <main wa_id> [--customer <preflight wa_id>]
demoops verify --customer <main wa_id> [--customer <preflight wa_id>]
```

Both must exit `0` before the next session. If `reset` exits `2`, a message of those customers is
still being processed: wait a few seconds and run it again. Keep the `verify` output as the reset
record.

## 14. NLU timing evidence for Gate section D

For every interpreted text turn the host writes exactly one Information line around the complete
`IAiNluClient.AnalyzeAsync` call (docs/TECHNICAL.md section 36.6):

```text
NLU analysis for inbound <provider message id> finished with <status> in <milliseconds> ms
```

It carries only the provider message id, the NLU status (`Success`, `InvalidModelOutput`,
`AiUnavailable` or `Timeout`) and the elapsed milliseconds: never the customer's text, number or any
commercial value. Unsupported media, empty messages and Human-mode conversations are not interpreted
and log no line.

After each timed gate turn, take the single new line from the host log of section 8:

```bash
grep 'NLU analysis for inbound' ~/demo-evidence/host-<timestamp>.log
```

Record its provider message id and milliseconds against the scenario. Gate section D computes the
median and p95 over the ten timed turns of one run.
