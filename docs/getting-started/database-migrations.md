---
type: guide
status: current
description: "Walks through creating, cleaning up and applying EF Core migrations for the Core and UserData contexts on both providers with the migration script, how startup applies them, and how to squash them into one InitialCreate. Read when you change an entity and need a migration, a migration is empty, conflicting or missing on one provider, or you squash the migrations."
---

# Database Migrations

**On this page:** Database Context Architecture · Prerequisites · Step-by-Step Migration Process · Migration Application · Conflict Prevention and Management · Best Practices · Common Commands Reference · Deployment Pipeline · Squash the migrations · Done when

The IniaAi Platform uses Entity Framework Core with two separate database contexts and supports both MSSQL and PostgreSQL. This guide covers the migration creation process, cleanup, conflict prevention, and best practices.

## Database Context Architecture

### Core Database (`CoreDbContext`)

Stores platform configuration and templates:

- Tool settings
- Workflow templates
- Instructions
- Platform configuration

### UserData Database (`UserDataDbContext`)

Stores execution-generated and user-specific data:

- Executed workflows
- Generated reports
- User accounts
- Organizations
- Counterparties
- AI query evaluation results

## Prerequisites

1. Z shell (zsh) terminal -- the migration script is optimized for zsh
2. .NET SDK installed
3. Access to the platform repository
4. Understanding of which database context your changes belong to

## Step-by-Step Migration Process

### 1. Navigate to Repository Root

```bash
cd platform   # in a clone of the platform alone, its root
```

### 2. Source the Migration Script

```bash
source create_and_apply_migration.zsh
```

This will display available commands and usage instructions.

### 3. Create Migrations

Create migrations across all contexts and providers:

```bash
create_migration_for_all "YourMigrationName"
```

> [!TIP]
> **Migration Naming Rules**
>
> - Starts with a letter
> - Contains only letters, numbers, and underscores
> - Clearly describes what the migration does
> - Must be unique (duplicate names will cause failures)
> - Use PascalCase (e.g., `AddAIQueryEvaluationTable`)
> - Include action and target: `Add`, `Remove`, `Update`, `Rename`

### 4. Wait for Completion

For each provider, the script sets the `ASPNETCORE_ENVIRONMENT`, `PlatformSettings__UserDataDbProvider`, and `PlatformSettings__CoreDbProvider` environment variables (via `setup_environment`), runs `dotnet ef migrations add`, and then clears those variables (`cleanup_environment`). It does **not** edit any project files. It will:

1. Create migrations for MSSQL UserData
2. Create migrations for MSSQL Core
3. Create migrations for PostgreSQL UserData
4. Create migrations for PostgreSQL Core

> [!CAUTION]
> Do not modify any files while the script is running!

### 5. Clean Up Empty Migrations

Since your changes likely affect only one context, empty migrations will be created for the other context. You **must** remove these before committing.

#### How to Identify Empty Migrations

Each migration consists of two files:

- `YYYYMMDDHHMMSS_MigrationName.cs` -- The actual migration
- `YYYYMMDDHHMMSS_MigrationName.Designer.cs` -- The model the migration targets (EF's metadata; the context's `…ModelSnapshot.cs` is a separate file)

Check the **non-Designer file** to determine if a migration is empty:

```csharp
// Empty migration example -- both methods have no content
public partial class AddAIQueryEvaluationTable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // No content here means empty migration
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No content here either
    }
}
```

> [!NOTE]
> Designer files are never empty (they contain snapshots). Always check the non-Designer file.

#### Remove Empty Migrations

For each empty migration, delete both the migration file and its Designer file.

Example structure after cleanup (if your table belongs in UserData):

```
IniaAi.Migrations.Mssql/
├── Core/                    # Removed empty migrations here
└── UserData/                # Kept your migration here
    ├── 20240115120000_AddAIQueryEvaluationTable.cs
    ├── 20240115120000_AddAIQueryEvaluationTable.Designer.cs
    └── UserDataDbContextModelSnapshot.cs

IniaAi.Migrations.PostgreSql/
├── Core/                    # Removed empty migrations here
└── UserData/                # Kept your migration here
    ├── 20240115120000_AddAIQueryEvaluationTable.cs
    ├── 20240115120000_AddAIQueryEvaluationTable.Designer.cs
    └── UserDataDbContextModelSnapshot.cs
```

## Migration Application

Migrations are applied automatically when the application starts: each environment's app migrates its own databases, so a local run migrates the database its launch profile names and a deployed app migrates that deployment's databases.

On startup, pending EF migrations are applied for UserData and Core (`context.Database.Migrate()`). The destructive alternative is `RESEED_DB=true`, which instead drops and recreates the databases from scratch (`EnsureDeleted` + `EnsureCreated`) — used only by the `RE-SEED` local launch profiles, never in deployed environments.

Before the pending-migration check, startup moves a database that was migrated through the migrations a squash replaced onto the squashed history. How that step works and when it stops startup: [Database Services](../architecture/services/infrastructure/database-services.md#migration--startup).

## Conflict Prevention and Management

### Before Creating Migrations

1. **Always rebase to latest develop:**

   ```bash
   git fetch origin
   git rebase origin/develop
   ```

2. **Check for existing migrations** -- review recent migrations in both contexts to avoid conflicts

### Types of Conflicts

**Non-conflicting migrations** (can be applied in any order):

- Adding different tables
- Adding columns to different tables
- Modifying different entities

**Conflicting migrations** (require careful coordination):

- Modifying the same column
- One migration deletes a column another modifies
- Changing the same table structure
- Data migrations affecting the same records

### Resolving Conflicts

If conflicts occur:

1. Coordinate with team members who created conflicting migrations
2. Determine the correct order of operations
3. Consider combining migrations if necessary
4. Test thoroughly in a development environment

## Best Practices

### Choose the Correct Context

- **Core**: Platform configuration, templates, settings
- **UserData**: Execution results, user-generated content, reports

### Test Migrations Thoroughly

- Run migrations locally first
- Verify data integrity
- Test both `Up` and `Down` methods
- Be extra careful with data-deleting migrations

### Commit Guidelines

- Never commit empty migrations
- Include all migration files (both `.cs` and `.Designer.cs`)
- Commit migrations separately from code changes when possible
- Write clear commit messages describing the schema changes

### Review Before Deployment

- Double-check migration SQL with `dotnet ef migrations script`
- Ensure backups exist before applying to production
- Coordinate with DevOps for production deployments

## Common Commands Reference

```bash
# Create migrations for all contexts and providers
create_migration_for_all "MigrationName"

# Create migration for specific provider and context
setup_environment MSSQL
create_migration MSSQL "MigrationName" "UserData"
cleanup_environment

# Remove the latest migration for both contexts, both providers
remove_migration_for_all

# Remove the latest migration for a single context, both providers
remove_core_migration_for_all
remove_userdata_migration_for_all

# Apply migrations to database
update_database_for_all

# Rollback to specific migration
downgrade_database MSSQL "UserData" "PreviousMigrationName"
```

`create_migration` and `update_database` set no provider themselves: only the `…_for_all` functions and `downgrade_database` call `setup_environment`. Called on its own, either one builds the model with the app's default provider, PostgreSQL in `platform/IniaAi.WebApp/appsettings.json`, so run `setup_environment MSSQL` (or `PostgreSQL`) first and `cleanup_environment` after, as above. A name that is not a C# identifier stops the script with `Migration name must be a valid C# identifier (start with letter, contain only letters, numbers, and underscores).`

## Deployment Pipeline

1. Code is committed to the repository
2. GitHub Actions builds Docker images
3. Images are published to the registry
4. Deployment scripts pull images and deploy
5. **Migrations run automatically on application startup**

> [!CAUTION]
> Database migrations can permanently affect data. Always test thoroughly and maintain backups before applying migrations to production environments.

## Squash the migrations

A squash replaces the migrations up to one release with one `InitialCreate` migration per context and provider. The migrations after that release stay. Squash only up to a release that every deployment runs, or runs before the release with the squash: a database whose history ends earlier stops startup. The current squash and the startup step that moves old databases onto it: [Database Services](../architecture/services/infrastructure/database-services.md#migration--startup).

Two consequences follow from the moved history. A build from before the squash cannot start on a moved database, because it reads its squashed migrations as pending: to go back, restore the database to its restore point, not only the image. The same holds for a local database that a branch from before the squash and one after it share. And `dotnet ef database update` and `update_database_for_all` do not move a history: on an old local database, start the app once first.

1. Find the last migration of the squash release in each of the four folders:

   ```bash
   RELEASE=v2.2.0
   for d in Mssql/Core Mssql/UserData PostgreSql/Core PostgreSql/UserData; do
     git ls-tree --name-only "$RELEASE" IniaAi.Migrations.$d/ | grep -E '/[0-9]{14}_[A-Za-z0-9]+\.cs$' | sort | tail -1
   done
   ```

2. Choose the timestamp of the new migration. EF Core orders migrations by id, so the `InitialCreate` id must sort after the last squashed migration and before the first kept migration, in all four folders.
3. In a worktree at the release tag, delete every migration file and the snapshot in each of the four folders. `dotnet ef migrations add` has no option for the id, so add this temporary class to `IniaAi.WebApp` with your timestamp in `GenerateId`, and add `compile;` to the `IncludeAssets` of the `Microsoft.EntityFrameworkCore.Design` reference in `IniaAi.WebApp.csproj`. Never commit either change.

   ```csharp
   using Microsoft.EntityFrameworkCore.Design;
   using Microsoft.EntityFrameworkCore.Migrations;
   using Microsoft.EntityFrameworkCore.Migrations.Internal;
   using Microsoft.Extensions.DependencyInjection;

   namespace IniaAi.WebApp;

   public sealed class FixedMigrationIdDesignTimeServices : IDesignTimeServices
   {
       public void ConfigureDesignTimeServices(IServiceCollection serviceCollection)
       {
           _ = serviceCollection.AddSingleton<IMigrationsIdGenerator, FixedMigrationIdGenerator>();
       }
   }

   #pragma warning disable EF1001
   public sealed class FixedMigrationIdGenerator : MigrationsIdGenerator
   {
       public override string GenerateId(string name)
       {
           return "<timestamp>_" + name;
       }
   }
   #pragma warning restore EF1001
   ```

   Then generate the migration:

   ```bash
   source create_and_apply_migration.zsh
   create_migration_for_all InitialCreate
   ```

   The four generated snapshots must be the same as the snapshots at the tag. If they differ, the model at the tag does not match its migrations: stop and find out why.
4. On the branch, delete the migrations up to the last squashed migration, and copy in the four `InitialCreate` migrations with their designer files. Keep the later migrations and the four snapshots unchanged.
5. In `DatabaseMigrationHelper`, set `SQUASH_RELEASE` and the four entries of `_lastSquashedMigrations`. The step takes the `InitialCreate` id from the migrations assembly: it is the first migration. In `MigrationsApplyFromEmptyTests`, set the same four ids in `_lastSquashedMigration`. The two copies are typed apart on purpose, so a typo in one fails the tests.
6. Check the result:
   - `dotnet ef migrations has-pending-model-changes -c CoreDbContext -s ./IniaAi.WebApp`, and the same for `UserDataDbContext`, report `No changes have been made to the model since the last migration.` after `setup_environment MSSQL` and after `setup_environment PostgreSQL`.
   - The old and the new migrations build the same schema. Set `INIA_TEST_MSSQL` and `INIA_TEST_PG` as [Testing → Database-backed integration tests](testing.md#database-backed-integration-tests-local-only) describes. Run this from a checkout before the squash with `DB=squash_old`, then from the branch with `DB=squash_new`:

     ```bash
     DB=squash_old
     source create_and_apply_migration.zsh
     for provider in MSSQL PostgreSQL; do
       if [ "$provider" = MSSQL ]; then CS="$INIA_TEST_MSSQL;Database=$DB"; else CS="$INIA_TEST_PG;Database=$DB"; fi
       setup_environment "$provider"
       for context in UserData Core; do
         dotnet ef database update -c "${context}DbContext" -s ./IniaAi.WebApp --connection "$CS"
       done
       cleanup_environment
     done
     ```

     Then compare the two. The tool exits with 0 when the only differences are column defaults that old `AddColumn` migrations left, and lists every difference. Set `SQLCMD` when `sqlcmd` runs in a container, as the script's header describes.

     ```bash
     python3 tools/schema-compare/compare.py squash_old squash_new
     ```

   - `MigrationsApplyFromEmptyTests` passes with `INIA_TEST_MSSQL` and `INIA_TEST_PG` set.

## Done when

- Each provider project (`IniaAi.Migrations.Mssql`, `IniaAi.Migrations.PostgreSql`) holds your migration, with its `.Designer.cs`, under the context it changes, and no empty migration under the other context.
- `dotnet build` passes, and starting the app with a local launch profile applies the migration: the startup log shows no migration error and the change is in the local database.
