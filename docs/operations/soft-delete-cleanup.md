---
type: explanation
status: current
description: "How the background service hard-deletes expired soft-deleted UserData records on a per-org schedule: the schedule and settings, the ordered delete, the rows with no foreign key, the Core purge of retired workflow types, configuration, the schema drift tests and the log lines. Read when soft-deleted data must go or must stay, a deleted workflow type must go, you add a UserData table or foreign key, or the cleanup's schema test fails."
---

# Soft-Delete Cleanup Service

**On this page:** Overview · How It Works · Retired workflow types · Configuration · Schema Drift Detection · Monitoring · Key Files

## Overview

The platform uses soft-delete across ~30 entities in the UserData schema. Records are marked with `IsDeleted = true` and `DeletedAt` timestamp rather than being physically removed. Over time, these accumulate and degrade database performance.

The **SoftDeleteCleanupService** periodically hard-deletes soft-deleted records older than a configurable retention window. It runs as a `BackgroundService` in the WebApi container.

## How It Works

### Schedule

The service waits 5 minutes after the WebApi starts, then checks hourly whether it's time to run cleanup. Cleanup triggers when:

1. Current UTC day-of-week matches the configured day
2. Current UTC hour matches the configured hour
3. Last cleanup was more than 23 hours ago (prevents double-runs)

**Default schedule**: Sunday at 02:00 UTC, 30-day retention.

### Settings Fallback

Settings are resolved in order:

1. **Org-specific** `OrganizationCleanupSettings` record with `HasExplicitSettings = true`
2. **Global** `OrganizationCleanupSettings` record where `OrganizationId = NULL`
3. **Hardcoded defaults**: Sunday 02:00 UTC, 30 days retention, enabled

When the organization has no explicit record, the retention of step 2 or 3 is replaced by the one `IDeletedRecordRetention` gives the organization, when it gives one (`OrganizationCleanupSchedule`). The platform's default gives none. Rows no single organization owns, counterparty types and blocks with no section, and retired workflow types, always go by the global or default retention, never by an organization's own or a module's shorter one (`SharedRetentionDays`). The schedule (day and hour) is unchanged by a module's retention. An explicit record for the organization still wins.


Auto-created tracking records (used to store `LastCleanupAt`) have `HasExplicitSettings = false` and are skipped during settings resolution, so they don't shadow global config changes. `LastCleanupAt` moves only after a run with no errors. The hourly check lands in the scheduled hour about once, so a run that failed is in practice tried again at the next scheduled day and hour.

### Deletion Order

Records are deleted in FK dependency order within a single transaction. The transaction runs inside the database's [retrying execution strategy](../architecture/services/infrastructure/database-services.md#transient-fault-resilience), so a transient failure replays the whole cleanup transaction rather than failing the run:

| Phase | What | Why |
|-------|------|-----|
| **Phase 0** | Nullify `TokenUsage` FKs to WorkflowExecutions and Documents | Preserve usage tracking |
| **Layer 1** | Pages, EmbeddingVectors (orphaned), DocumentInfos, BlockResponses, RagSettings, TextInputs | Deepest leaf tables |
| **Layer 2** | Nullify the customer text on the FK-free rows that outlive their parent (`VerificationCriterionRecord.Feedback` and `EvidenceSpans` by execution; then every registered `IDocumentPurgeStep`, which is how `ScreeningRecord.EvidenceExcerpt` and `Reasoning` go by document), then WorkflowExecutionLogs, WorkflowExecutionCounterparties, the execution graph (`graph_edges`, then `graph_nodes`, by `RunId`), the `ReportFeedbackItems` of the purged executions (feedback quotes the report's text), then the report tree leaf-first (nullify `WorkflowExecutionLog.ReportParagraphId`, ReportParagraphs, ReportSections, ReportDataOutputs, WorkflowReports) | Children of WorkflowExecutions |
| **Layer 3** | CounterpartyCounterpartyType (raw SQL), nullify `WorkflowExecution.CounterpartyId` and `WorkflowReport.CounterpartyId` | Clear inbound FKs before Counterparties are deleted |
| **Layer 4** | nullify `ReportParagraph.BlockId` and `ReportDataOutput.BlockId`, Blocks, DocumentClasses, WorkflowVariables, WorkflowRegressionFields, WorkflowSignatureFields, WorkflowExecutions, Workflows, Documents, Json2XlsxTemplates, Json2XlsxTemplateVersions, FileMetadata, Files, CounterpartyTypes, Counterparties | Self-deleted parent entities |
| **Layer 5** | ValidationRuns of the purged cases, self-deleted ValidationRuns, ValidationCases, ValidationSuites | Validation rows; runs go before cases, the one real foreign key among them |
| **After commit** | External file storage (MinIO/S3) | Files deleted only after DB transaction succeeds |

The executed-output report tree (`WorkflowReport` → `ReportSection` → `ReportParagraph`, all in the UserData schema) replaces the former `WorkflowOutputElement` table. It is purged in Layer 2 in leaf-to-root order: the service first nullifies the shadow `ReportParagraphId` foreign key on `WorkflowExecutionLogs`, then deletes `ReportParagraphs`, then `ReportSections`, then the reports' `ReportDataOutputs`, then the `WorkflowReports` roots. Because the service deletes with set-based `ExecuteDelete`/`ExecuteUpdate` calls, EF Core's change-tracker cascade behavior never runs, so every inbound FK to a row being deleted must be cleared by hand first — hence the explicit nullify of `WorkflowExecutionLog.ReportParagraphId` before the paragraphs go. Reports tied to a deleted `WorkflowExecution` are purged alongside reports that were soft-deleted directly. In Layer 4, any surviving `ReportParagraph` or `ReportDataOutput` whose `BlockId` points at a `Block` about to be deleted has that `BlockId` nullified first, for the same reason. `Sections` are not deleted explicitly — they are removed by the `Workflow` delete cascading down their FK to `Workflows`.

`AgentRunTraceFiles`, `AgentRunOutputs` and `AgentRunSessionBundles` — the Agent block's collected trace evidence, per-attempt output history and session-bundle custody rows — and `BlockCitationTraces`, each executed block's citation trace ([UserData reference](../reference/database/user-data-database.md)), keep their rows out of the delete layers entirely: their FKs to `Blocks` carry a **database-level `ON DELETE CASCADE`**, so the Layer-4 `Block` delete removes their rows in the same statement. This is deliberate: the schema-drift test skips cascade FKs ([Schema Drift Detection](#schema-drift-detection)), so run evidence and attempt history follow their block with zero cleanup code, and the retention story stays "the record lives exactly as long as the thing it records". The one thing the cascade cannot reach is bytes stored outside the database, which is why the session-bundle table alone gets a byte-release pass below.

`AgentRunSessionBundles` carries a second retention rule on top of that cascade, because it is the one agent-run table whose rows also name bytes outside the database ([UserData reference](../reference/database/user-data-database.md#agentrunsessionbundles)). A bundle exists to let a verification member redo its work by continuing its own session, so it stops being worth its bytes the moment its execution can no longer redo anything. Two sweeps close that window. The execution's own terminal releases them: every terminal but `Failed` deletes the lot — the successful one included — and `Failed` keeps the last leg of each block, so a person can still read what each member had reached. Then the [AgentRunSessionBundleReaper](application-configuration.md#agentrunsessionbundlereaper) collects, after seven days, whatever a crashed or stranded execution never released. Both sweeps delete the row first and the bytes second: a stranded blob costs storage, while a row naming bytes that are already gone would answer a resume with a bundle nobody can read.

The cleanup service itself is the third releaser, because the cascade and the byte store have different owners: hard-deleting a `Block` takes its `AgentRunSessionBundles` rows with it in the same statement, and the rows name bytes the cascade cannot reach. Before the transaction opens, the service reads the storage locations of every bundle whose block it is about to purge; after the transaction commits, it deletes those bytes through the same post-commit file-cleanup phase that handles `FileMetadata` — a byte-level failure is logged into the run's error list and does not stop the sweep. Two crash windows still exist, inherent to writing bytes and row in separate steps: a host that dies between storing a bundle's bytes and committing its row, and one that dies between a supersede's commit and the delete of the bytes it replaced. Both leave a `Files` row nothing points at, and nothing collects it. In practice both windows are minutes wide and rare: bundles are released at their execution's terminal, and the [7-day reaper](application-configuration.md#agentrunsessionbundlereaper) collects whatever a crashed execution never released.

A new document has the same window. Every intake stores a document's bytes before the commit that first saves the document, so no reader finds a document without its bytes, and two intakes of the same file never store it twice under one key. A host that dies, or a commit that fails, between the two leaves bytes that nothing points at, and nothing collects them.

`VerificationCriterionRecords` — the verification failure record ([UserData reference](../reference/database/user-data-database.md#verificationcriterionrecords)) — deliberately **outlives** the execution it records: it has no foreign keys at all, so nothing cascades it away. When Layer 2 identifies the executions being purged, the service `ExecuteUpdate`s the record rows of those executions to null their two text columns (`Feedback`, `EvidenceSpans`) while keeping every identifier, counter and verdict — text follows the customer, aggregates are permanent.

#### Screening records

`ScreeningRecords` — the intake screening audit row ([UserData reference](../reference/database/user-data-database.md#screeningrecords)) — outlives its document the same way: no foreign key, so nothing cascades it away. The service itself knows nothing about screening: in the same layer it runs every `IDocumentPurgeStep` registered in the host with the ids it is purging, and logs each step's count under the step's name. The platform registers one for the screening record, `ScreeningRecordRetention` (`platform/IniaAi.Infrastructure/Services/`, registered by `AddInfrastructure` through `AddKeptTablePurgeSteps()`), which `ExecuteUpdate`s the rows whose `DocumentId` or `OriginatingDocumentId` is among the documents being purged, nulling the two columns of customer content, `EvidenceExcerpt` and `Reasoning`, stamping `ExcerptClearedAt` in the same update when an excerpt was there, and keeping every other column. Two ids are matched because a confidential run screens an anonymised derivative of the document the caller sent, and either may be purged first. A `Refused` row carries neither id, so the sweep cannot reach it; a refusal must therefore never store an excerpt.

The screening backtest tables follow the same rule through a second step, `ScreeningBacktestRetention`, registered by the same call. Both run in every build, the open one included: the screening tables stay under the one schema both builds share, so their excerpts must go with their documents even where the screening module is not installed. It `ExecuteUpdate`s the `ScreeningBacktestDocuments` rows whose `DocumentId` is among the documents being purged, nulling the judge's `Reasoning` and the `EvidenceExcerpt` it cited, stamping `ContentClearedAt`, and keeping the row and its verdict, so a backtest's counts never move. A `ScreeningBacktestRuns` row holds no document content and is kept whole. A backtest reads the document a run points at, so on a confidential run the purged id is the anonymised copy's, the one the row names.


#### Rows with no foreign key

A row that carries the id of something the sweep purges, but has no foreign key to it, is invisible to the foreign-key walk that guards the rest of this page. Each such table therefore declares what the sweep does with it. The declaration is `SoftDeleteCleanupService.LooseReferenceDispositions`, plus the `Dispositions` of each registered `IDocumentPurgeStep`, which list every table the feature owns, the ones its step clears and the ones it keeps whole:

| Table | Loose id | Disposition |
|-------|----------|-------------|
| `DocumentUnlockAuditLogs` | `DocumentId` | Row kept whole. An append-only audit trail that survives document deletion by design. |
| `Files` | `DocumentId` | Row deleted by its own soft delete (Layer 4, `Files`). The id is a legacy column. |
| `graph_nodes`, `graph_edges` | `RunId` (the execution id) | Rows deleted with their execution (Layer 2). The graph is a cache built from the executed workflow. |
| `ReportFeedbackItems` | `WorkflowExecutionId` | Rows deleted with their execution (Layer 2), because the feedback quotes the report's text. |
| `ValidationCases` | `BaselineDocumentId`, `BaselineExecutionId`, `SourceCaseId` | Row kept whole. The case snapshots its baseline, and a purged source reads as "source document removed". |
| `ValidationReferenceAuditLogs` | `CaseId` | Row kept whole. An append-only audit trail. |
| `ValidationSuites` | `SourceSuiteId` | Row kept whole. The id names the suite on the instance it was imported from. |
| `VerificationCriterionRecords` | `WorkflowExecutionId`, `RunExecutionId`, `VerifierBlockId`, `MemberBlockId` | `Feedback` and `EvidenceSpans` nulled, every other column kept. |
| `ScreeningRecords` | `DocumentId`, `OriginatingDocumentId`, `WorkflowExecutionId` | `EvidenceExcerpt` and `Reasoning` nulled by `ScreeningRecordRetention`, every other column kept. |
| `ScreeningBacktestDocuments` | `DocumentId`, `WorkflowExecutionId` | `Reasoning` and `EvidenceExcerpt` nulled by `ScreeningBacktestRetention`, every other column kept. |
| `ScreeningBacktestRuns` | `WorkflowExecutionId` | Row kept whole, declared on `ScreeningBacktestRetention`. A measurement of a past run outlives the run. |
| `WorkflowBaselineRegistrations` | `CounterpartyId`, `ExecutionId`, `SourceExecutionId` | Row kept whole. An immutable, supersession-chained audit trail of blessed baselines. |
| `WorkflowRegressionRuns` | `BaselineExecutionId`, `RerunExecutionId` | Row kept whole, with its `WorkflowRegressionBlockResults` and their compared values. |

### Organization Scoping

Every deletion step is scoped to a single organization via:

- Direct `OrganizationId` filter (e.g., Workflows, Documents, Counterparties)
- Parent entity subquery (e.g., Blocks scoped via their Section's parent Workflow `OrganizationId`)
- Orphaned entities have no org scope (e.g., EmbeddingVectors, Blocks with null `SectionId`)

`Block.SectionId` is nullable by design — a block that is not currently parented to a section (template/transient/orphan rows) is valid and is not treated as corruption.

### Excluded Entities

| Entity | Reason |
|--------|--------|
| Monitoring entities (HedgeFunds/PrivateEquity observations and audits) | Separate domain, not part of cleanup scope |
| Organization | Tenant root, never soft-deleted |
| TokenUsages | Preserved for billing/usage tracking (FKs nullified instead) |
| TokenUsageDailyTotals, TokenUsageExecutionTotals | Untouched: a purged execution keeps its totals row, so its spend still counts in its workflow type's execution count and average cost |

## Retired workflow types

The sweep above clears UserData only. A workflow type, its template and its version history live in Core, so the background service runs a second step for each organization right after that organization's sweep, with the same retention: `IWorkflowTypeRetirement.PurgeRetiredAsync` (`WorkflowTypeRetirement`). It takes every type whose `RetiredAt` is past the cutoff, that this organization retired (the organization's access row is soft-deleted) and that no organization still reaches. For each such type, one Core transaction deletes, past the soft-delete filter, what retiring soft-deleted: the pending signature overrides (their votes cascade), the signature field overrides, the validation schemas, every `WorkflowVersion` of the type, the template workflows (sections, blocks, document classes and block templates cascade), and then the `WorkflowTypes` row (access rows and counterparty mappings cascade). `SignatureFieldOverrideAuditLogs` rows stay: that log is append-only. A type whose purge fails is logged and counted as an error, which leaves `LastCleanupAt` where it was, so the next scheduled run tries again.

Only an explicit retirement starts this. A type that no organization can reach, or whose access rows were removed by hand, keeps `RetiredAt` null and is never purged. Retiring a type also soft-deletes what the organization ran with it, including the `FileMetadata` of its documents, so the sweep above removes those UserData rows and releases the stored files in the same scheduled run once the window has passed. How a type is retired: [Deleting a type through the template API](../architecture/workflow-engine/workflow-types.md#deleting-a-type-through-the-template-api).


## Configuration

### Database Table

`UserData.OrganizationCleanupSettings`:

| Column | Type | Default | Description |
|--------|------|---------|-------------|
| `OrganizationId` | string(450), nullable | null | Null = global default |
| `RetentionDays` | int | 30 | Days before soft-deleted records are hard-deleted |
| `CleanupDayOfWeek` | DayOfWeek | Sunday | 0=Sunday, 6=Saturday |
| `CleanupHourUtc` | int (0-23) | 2 | Hour in UTC |
| `IsEnabled` | bool | true | Enable/disable cleanup for this org |
| `HasExplicitSettings` | bool | false | True = admin-configured, false = auto-created tracking record |
| `LastCleanupAt` | DateTime? | null | Last successful cleanup timestamp |

### Creating a Global Default

```sql
INSERT INTO "UserData"."OrganizationCleanupSettings"
("Id", "OrganizationId", "RetentionDays", "CleanupDayOfWeek", "CleanupHourUtc", "IsEnabled", "HasExplicitSettings", "IsDeleted", "CreatedAt")
VALUES
(gen_random_uuid()::text, NULL, 30, 0, 2, true, true, false, NOW());
```

### Per-Organization Override

```sql
INSERT INTO "UserData"."OrganizationCleanupSettings"
("Id", "OrganizationId", "RetentionDays", "CleanupDayOfWeek", "CleanupHourUtc", "IsEnabled", "HasExplicitSettings", "IsDeleted", "CreatedAt")
VALUES
(gen_random_uuid()::text, 'ExampleOrg', 90, 6, 3, true, true, false, NOW());
```

### Disabling Cleanup for an Org

```sql
INSERT INTO "UserData"."OrganizationCleanupSettings"
("Id", "OrganizationId", "IsEnabled", "HasExplicitSettings", "IsDeleted", "CreatedAt")
VALUES
(gen_random_uuid()::text, 'SomeOrg', false, true, false, NOW());
```

## Schema Drift Detection

`SoftDeleteCleanupSchemaTests` in `IniaAi.Infrastructure.Tests` uses EF Core model reflection to verify that every FK relationship pointing to a soft-deletable entity is either handled by the cleanup service or delegated to the database: FKs declared `DeleteBehavior.Cascade` are skipped by design, because the DB removes those rows in the same delete.

If you add a new entity or FK relationship to a soft-deletable entity, the test will fail with a message listing unhandled relationships. To fix:

1. Add the relationship to `SoftDeleteCleanupService.HandledRelationships`
2. Add the corresponding deletion/nullification code in `CleanupAsync`
3. Run the test to verify

A second test covers the tables with no foreign key. It reads the entities a purge deletes from the queryables of `PurgeSelection`, leaving out counterparty types. It then derives, from the EF model, every string column of an entity with no foreign key whose name ends in the id of one of those entities: the full name (`DocumentId`), a shorter tail of it (`CaseId`, `RunId`), or its head noun after a qualifier (`BaselineExecutionId`). It fails when those columns and the declared loose-id columns in [Rows with no foreign key](#rows-with-no-foreign-key) differ in either direction, and names each column as `Entity.Column`. The test reads the purge steps through `AddKeptTablePurgeSteps()`, the registration the host runs. A third test fails when a declaration nulls a column that is not a nullable column of its entity. To fix a failure, declare the new column with what the sweep does to its row and write the code that carries it out, or remove the declaration of a column that is gone.

## Monitoring

All cleanup activity is logged at `Information` level with the `SoftDeleteCleanup:` prefix:

```
SoftDeleteCleanup: Org ExampleOrg — starting cleanup (scheduled Sunday 2:00 UTC)
SoftDeleteCleanup: Starting cleanup for org ExampleOrg, retention 30 days (cutoff: ...)
SoftDeleteCleanup: Org ExampleOrg — Deleted 149 Counterparties
SoftDeleteCleanup: Org ExampleOrg — Complete: 3010 records from 14 tables, 13 files in 584ms
SoftDeleteCleanup: Org ExampleOrg — purged 1 retired workflow type(s), 0 errors
```

Errors are logged at `Error` level: `SoftDeleteCleanup: Org <org> — Cleanup transaction failed` when the delete rolls back, and `SoftDeleteCleanup: Failed for org <org>, continuing with next` when the sweep moves on. Per-file storage failures are logged at `Warning` level and don't fail the overall cleanup.

## Key Files

| File | Purpose |
|------|---------|
| `platform/IniaAi.Domain/Entities/DataManagement/OrganizationCleanupSettings.cs` | Configuration entity |
| `platform/IniaAi.Application/Common/Interfaces/Services/ISoftDeleteCleanupService.cs` | Service interface |
| `platform/IniaAi.Infrastructure/Services/SoftDeleteCleanupService.cs` | Core deletion logic |
| `platform/IniaAi.Infrastructure/Services/PurgeSelection.cs` | What one purge deletes: the weekly sweep's selection, or one or more execution trees |
| `platform/IniaAi.WebApi/BackgroundServices/SoftDeleteCleanupBackgroundService.cs` | Scheduling and orchestration |
| `platform/IniaAi.WebApi/BackgroundServices/OrganizationCleanupSchedule.cs` | One organization's effective settings: explicit, global or default, and the retention a module gives |
| `platform/IniaAi.Application/Common/DataManagement/IDeletedRecordRetention.cs` | The seam a closed module fills to shorten an organization's retention |
| `platform/IniaAi.Infrastructure.Tests/SoftDeleteCleanupSchemaTests.cs` | Schema drift detection |
