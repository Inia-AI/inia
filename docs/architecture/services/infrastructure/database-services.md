---
type: explanation
status: current
description: "The two EF Core DbContexts, the dual-provider setup, the transient-fault retry policy and what it demands of explicit transactions, the repositories and units of work, and how migrations and seeding run at startup. Read when you open a transaction, update an entity, add a migration, or debug a startup migration or a retried command."
---

# Database Services

Entity Framework Core access is configured in `platform/IniaAi.Infrastructure/Persistence/Setup/DatabaseConfigurator.cs` (`AddIniaAiDatabases`).

## Two DbContexts

The platform uses two contexts that share the same entity classes but persist different shapes:

- **`CoreDbContext`** (schema `Core`) — workflow templates and authoring data.
- **`UserDataDbContext`** (schema `UserData`) — executed workflow instances, documents, embeddings, and OpenIddict tables (`UseOpenIddict`).

The two may share one physical database (different schemas) or be split; treat each as its own unit of work.

## Dual provider

Each context binds to PostgreSQL (`UseNpgsql`) or SQL Server (`UseSqlServer`) based on `PlatformSettings.CoreDbProvider` / `UserDataDbProvider`. Migrations live in separate assemblies — `IniaAi.Migrations.PostgreSql` and `IniaAi.Migrations.Mssql` — each with its own `__EFMigrationsHistory` table per schema. A 300-second command timeout is set on both.

## Transient-fault resilience

Both provider helpers (`ConfigureNpgsqlProvider` / `ConfigureSqlServerProvider`) call `EnableRetryOnFailure`, so every command on either context runs under a retrying execution strategy: up to 6 retries with exponentially growing delays capped at 10 seconds each — sized to ride out a ~30-second transient outage (an RDS storage stall, a failover blip) while keeping worst-case time-to-hard-failure at one to two minutes. SQL Server relies on the provider's built-in transient-error list. PostgreSQL additionally opts in error `57P01` (`admin_shutdown`) via `errorCodesToAdd` — the error a Postgres failover or restart raises on established connections — because Npgsql does not classify it as transient.

A retrying strategy changes the transaction contract: EF Core rejects a user-initiated `BeginTransaction` outside a strategy callback. Every site that opens an explicit transaction therefore obtains the strategy from `IBaseDbContext.CreateExecutionStrategy()` (or `Database.CreateExecutionStrategy()`) and runs the whole transactional body inside `IExecutionStrategy.ExecuteAsync`, so the unit retries as a whole. The sites that do so: `SaveWorkflowCommandHandler`, `SharedComponentFanOutService`, the shared-component create and promote handlers (`CreateSharedComponentCommand`, `PromoteSharedComponentCommandHandler`), `DuplicateWorkflowTypeCommandHandler`, `SoftDeleteCleanupService`, the startup migration `PageOverviewPromptsMigration`, `StartupDatabaseLock`, `TokenUsageTotalsSweep`, and the startup cleanup helper `ObsoleteRagSettingsCleanupHelper` run by `DatabaseMigrationHelper`. A new site joins the list by calling `CreateExecutionStrategy()`; grep for it to see the current set.


A retry replays the delegate on the same scoped `DbContext`, and a rolled-back attempt leaves its entities tracked — `Add` ignores an already-tracked entity, so a replayed attempt could silently persist a partial graph. `IBaseDbContext.ClearChangeTracker()` exists for exactly this: a retryable transactional body detaches all tracked entities as its first step (`SaveWorkflowCommandHandler.SaveInTransactionAsync` is the exemplar; `SharedComponentFanOutService` and `PageOverviewPromptsMigration` do the same).

## Repositories & Unit of Work

- Generic repositories built on `Ardalis.Specification`: `ICoreRepository<>` → `CoreRepositoryWrapper<>`, `IUserDataRepository<>` → `UserDataRepositoryWrapper<>`.
- Feature repositories (e.g. `IDocumentRepository`, `IWorkflowExecutionRepository`, `IUserRepository`, `IOrganizationRepository`).
- Units of work: `ICoreUnitOfWork` / `CoreUnitOfWork` and `IUserDataUnitOfWork` / `UserDataUnitOfWork`.
- A singleton `IDbContextFactory<UserDataDbContext>` (`PooledDbContextFactory`) supports background/parallel work.

The generic surface has no update method: `IRepository<T>` (and its marker `IUserDataRepository<T>`) exposes queries, `AddAsync`, and the `Remove` family — **no `Update`, no `Attach`** — and the units of work add none. A few feature repositories carry targeted update methods (`IWorkflowExecutionRepository.UpdateReplayOutcomeAsync` and `UpdateCounterpartyIdAsync`; the Core `IToolsSettingsRepository`'s `Update…PromptsAsync`), but none takes an arbitrary entity. An update is therefore a mutation of an entity the context already tracks, persisted by `CompleteAsync`. `IUserDataUnitOfWork.RevertAsync(entity)` sets the entry's state back to `Unchanged` so pending modifications are not persisted, and `UntrackAsync` detaches it. The corollary: an entity loaded with `AsNoTracking`, or through a scope disposed after the read, is detached and has no write path — mutating it and calling `CompleteAsync` reports success while persisting nothing.

## Migration & startup

`InitializeDatabases` (called from `Program.cs`) runs `MigrateDatabase` for UserData then Core on every startup via `DatabaseMigrationHelper`. The destructive `RESEED_DB=true` path (`EnsureDeleted` + `EnsureCreated` through the admin contexts) is opt-in only. Health is monitored by `AddDbContextCheck` for both contexts plus a DbContext-factory check.

Hosts that start together take turns. For each database, `DatabaseMigrationHelper` holds one session-level lock (`StartupDatabaseLock`, a `DatabaseSessionLock`: a PostgreSQL advisory lock or a SQL Server application lock, held on an unpooled connection so a dying host's session releases it) while it migrates and seeds. The WebApi's `SystemUserSeeder` takes the same lock on the UserData database. EF Core's own migration locks are not enough: on PostgreSQL the lock ends at each migration's commit while the pending list is read once, and on SQL Server it ends before seeding starts. A database that does not exist yet is created once: a host whose create fails waits up to a minute for the other host's database to come online.

`DatabaseSessionLock` serves any work that one host at a time must do. Each lock has its own name (`DatabaseLockName`: an advisory-lock key and an application-lock resource). The startup lock waits for its holder; the [usage-totals sweep](../../../reference/database/user-data-database.md#how-the-usage-totals-are-kept) tries its lock without waiting and skips its round when another host holds it. On SQLite and the in-memory provider, whose databases serve one process, every caller gets a lock that holds nothing.

The migrations up to release 2.2.0 are squashed. For each context and provider, one `20261016100000_InitialCreate` migration replaces every migration up to `WorkflowNotes`, the last migration of 2.2.0. The migrations after 2.2.0 stay as they were. On startup, under the same lock and before the pending-migration check, `DatabaseMigrationHelper.MoveToSquashedHistory` moves a database that was migrated through the old chain onto the squashed history:

- A history that holds the last 2.2.0 migration and no `InitialCreate` loses the rows of every migration up to that one and gets one `InitialCreate` row, in one transaction. Rows of later migrations stay. EF Core then applies the later migrations that are still pending, so a database at 2.2.0 and one at a later release both arrive at the same history.
- A fresh database and a database that already has the `InitialCreate` row skip the step.
- A history without the last 2.2.0 migration stops startup with an `InvalidOperationException` whose message starts `<Core or UserData>: the migration history does not include '<last 2.2.0 migration>', the last migration of release 2.2.0;`. Start release 2.2.0, or a later release from before the squash, on that database once, then start the new release.

The step takes the `InitialCreate` id from the migrations assembly, where it is the first migration, and writes its SQL through EF Core's `IHistoryRepository` scripts, so the history table name and schema come from the context configuration. It stays in the code until the next squash replaces its ids, because a deployment that upgrades later still needs its history moved. The procedure for a squash is in [Database Migrations](../../../getting-started/database-migrations.md#squash-the-migrations).

A database migrated through the old chain keeps a database default on 26 columns that a later migration added as `NOT NULL` with `defaultValue` (for example `UserData.ValidationRuns.Attempt` and `Core.Blocks.RunsAlone`). A database created from `InitialCreate` has no default on them, because the model declares none. Tables, column types, nullability, indexes, filters, keys and check constraints are the same on both, on SQL Server and on PostgreSQL. `tools/schema-compare/compare.py` in the platform repository lists these differences for two databases. The difference has no effect on EF Core, which sends a value for every non-nullable column on insert. No raw SQL `INSERT` in the platform repository writes to these tables.
