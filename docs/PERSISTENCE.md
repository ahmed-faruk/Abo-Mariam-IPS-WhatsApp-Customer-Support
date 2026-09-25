# Persistence baseline (Issue #4)

PostgreSQL 18 runs locally in Docker Desktop. One database, one schema and one migration
history per persistent module, exactly as defined in `docs/TECHNICAL.md` sections 4, 6 and 7.

## Local setup: one password, two places

Docker Compose and the application both need the same password. Compose reads
`POSTGRES_PASSWORD`; the application reads its own connection string. Set both in the shell from a
single value so they cannot drift:

```bash
# The password for the `monitor_app` role inside the container.
export POSTGRES_PASSWORD='<local password>'

# The complete connection string the application connects with, using the same password.
export ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=5432;Database=monitor_ai;Username=monitor_app;Password=$POSTGRES_PASSWORD"
```

`compose.yaml` interpolates `POSTGRES_PASSWORD` for the PostgreSQL container. The application reads
its connection string from configuration, in this order:

```text
ConnectionStrings__DefaultConnection   (environment variable, used above)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<connection string>"
src/Host.Web/appsettings.json          (local, password-free template)
```

`ConnectionStrings__DefaultConnection` replaces the whole value from `appsettings.json`, so it must
include the password; the password-free template is a fallback only. The app refuses to start when
the connection string is missing or blank.

Nothing secret is committed. Docker Compose also reads a git-ignored `.env` file in the repository
root, so exporting is only needed per shell; .NET does not read `.env`, so the application still
needs the environment variable, or user-secrets. To use user-secrets instead, run
`dotnet user-secrets init --project src/Host.Web` once (the project has no secrets id yet), then set
the same complete connection string with `dotnet user-secrets set`.

## Start and stop PostgreSQL

```bash
docker compose up -d
docker compose ps          # postgres must report healthy
docker compose down        # keeps the named volume
docker compose down -v     # also drops the named volume and all data
```

The port is bound to `127.0.0.1:5432` only, so PostgreSQL is never exposed off the machine.

## Migration commands

Migrations are owned by the module that owns the schema. One `DbContext` per module:

| Module | DbContext | Schema | Migrations folder |
|---|---|---|---|
| Catalog | `CatalogDbContext` | `catalog` | `src/Modules/Catalog/Infrastructure/Migrations` |
| Conversations | `ConversationDbContext` | `conversations` | `src/Modules/Conversations/Infrastructure/Migrations` |
| Messaging | `MessagingDbContext` | `messaging` | `src/Modules/Messaging/Infrastructure/Migrations` |
| Storefront | `StorefrontDbContext` | `storefront` | `src/Modules/Storefront/Infrastructure/Migrations` |
| Identity | `IdentityDbContext` | `identity` | `src/Modules/Identity/Infrastructure/Migrations` |

Each module has a design-time factory, so a module can be both the project and the startup
project for EF tools. No database connection is needed to scaffold a migration.

Add a migration (replace the module, context and name):

```bash
dotnet ef migrations add <MigrationName> \
  --project src/Modules/Catalog \
  --startup-project src/Modules/Catalog \
  --context CatalogDbContext \
  --output-dir Infrastructure/Migrations
```

Apply migrations. EF CLI:

```bash
dotnet ef database update \
  --project src/Modules/Catalog \
  --startup-project src/Modules/Catalog \
  --context CatalogDbContext
```

`dotnet ef` only connects when a database operation needs it. By default the design-time factory
points at `127.0.0.1:5432/monitor_ai` with `monitor_app` and no password; set
`MONITOR_DESIGN_TIME_CONNECTION` for a real target, for example:

```bash
MONITOR_DESIGN_TIME_CONNECTION="Host=127.0.0.1;Port=5432;Database=monitor_ai;Username=monitor_app;Password=$POSTGRES_PASSWORD" \
  dotnet ef database update --project src/Modules/Catalog --startup-project src/Modules/Catalog --context CatalogDbContext
```

Repeat per module. Migrations are never applied automatically at application startup and
`EnsureCreated()` is not used outside disposable tests.

After scaffolding, run `dotnet format WhatsAppMonitorAssistant.slnx --no-restore` once: EF writes
generated files with a byte-order mark, and the repository `.editorconfig` requires plain UTF-8.

## Run the Issue #4 integration tests

The suite uses Testcontainers, starts its own PostgreSQL 18 container, creates a throwaway
database per test and applies every module migration from an empty database.

```bash
docker compose up -d          # not required: Testcontainers starts its own container
dotnet test tests/Integration.Tests
```

Docker Desktop must be running. The tests never use an in-memory or mocked database.

## Pinned EF tool (Issue #15)

The `dotnet-ef` CLI is pinned in `.config/dotnet-tools.json` to the repository's EF Core version.
Run `dotnet tool restore` once per clone; `dotnet ef` then uses the pinned 10.0.8 tool instead of a
global one. The controlled-demo database procedure (migrate from empty, seed, reset, verify) is in
`docs/demo/FAST-TRACK-RUNBOOK.md`.
