---
type: explanation
status: current
description: "Documents workflow types and their categories, the reserved and module types, which organizations may use a type (WorkflowTypeOrganizationAccess) and where that is and is not checked, the Workflow Type Management page with its tenant dialog, creating a type through the template API, duplicating a type, and deleting one through the API. Read when you add, duplicate, delete or grant a workflow type, or need to know who can see or run one."
---

# Workflow Types & Categories

**On this page:** Overview · Entity Structure · Organization Access · Management Interface · Creating, Duplicating and Deleting Types · API and Database · Template Versioning · Related Documentation

A workflow type names one live workflow template, groups it under a category, and decides which organizations may use it.

## Overview

Each workflow type has exactly one live template: the Core `Workflow` row whose `WorkflowTypeId` is the type's id. Every organization granted access to the type runs the same template; per-organization variation comes from [signature overrides](../../features/signature-overrides.md), never from a forked copy. Saving the template appends to the type's [version history](../../features/workflow-template-versioning.md).

> [!TIP]
> **Who manages types**
>
> The Workflow Type Management page admits the `WorkflowEditor`, `SuperAdmin` and `Operator` roles (`[Authorize(Roles = ROLES)]` on `WorkflowTypeManagement`). Inside the page, editors (`WorkflowEditor`, `SuperAdmin`) add, edit, duplicate and delete types and categories, and operators (`Operator`, `SuperAdmin`) choose which organizations may use a type (**Manage Tenants**). An organization `Admin` does not reach the page: a workflow type is shared by every organization that runs it.

## Entity Structure

### WorkflowType

`WorkflowType` (`platform/IniaAi.Domain/Entities/Workflows/WorkflowType.cs`) is a lightweight classification record. Unlike most entities it extends `JsonSerializable<WorkflowType>`, not the audited `Entity<T>` base, so it carries **no `CreatedAt`/`UpdatedAt` columns**.

| Property | Type | Notes |
|---|---|---|
| `Id` | string | Primary key, at most 100 characters (`ID_MAX_LENGTH`), e.g. `QuarterlyReview` |
| `Name` | string | Required, at most 250 characters (`NAME_MAX_LENGTH`) |
| `Description` | string? | Unbounded |
| `CategoryId` | string? | The type's `WorkflowTypeCategory` |
| `DuplicatedFromWorkflowTypeId` | string? | The source type when this type was made by [duplication](#duplicating-a-type); indexed |

Navigation collections: `Workflows`, `CounterpartyTypeMappings` (the counterparty types the type works on, `WorkflowTypeCounterpartyTypeMap`), `OrganizationAccess`, and `Category`.

The entity exposes the platform's reserved type ids as constants, `GENERIC` (`Generic`) and `TEST_WORKFLOW_TYPE` (`TestWorkflowType`); `WorkflowType.UserFacing` is the shared filter that keeps them, and retired types, out of every list of real templates. `CoreDbInitializer` seeds both, with an access row for the default organization. A closed module declares its own types as `ModuleWorkflowType` records, each with a seed workflow: core seeding adds each type, with an access row for the default organization and, on a fresh install, its seed workflow. A build without a module seeds none of its types; a stored row of such a type stays an ordinary type, and `JobExecutor` runs its workflows on the generic handler, the fallback for any type without a handler of its own.


### WorkflowTypeCategory

`WorkflowTypeCategory` groups types for display. Categories are **flat**: a category has no parent, and a type belongs to at most one category. Categories extend the audited `Entity<T>` base, so unlike `WorkflowType` they carry `CreatedAt`/`UpdatedAt` and soft-delete columns.

| Property | Type | Notes |
|---|---|---|
| `Id` | string | Primary key, e.g. `financial-analysis` |
| `Name` | string | Display name |
| `Description` | string? | |
| `DisplayOrder` | int? | Categories sort by this value, then by name |

`CoreDbInitializer` seeds a monitoring category (for `Generic` and the module types) and a test category (for `TestWorkflowType`).

## Organization Access

### WorkflowTypeOrganizationAccess

`WorkflowTypeOrganizationAccess` is the join between a type and an organization that may use it. It lives in the **Core** database but references organizations in the **UserData** database, so `OrganizationId` is a plain string with **no foreign key**; only `WorkflowTypeId` has one (cascade delete). A unique index on `(WorkflowTypeId, OrganizationId)` prevents duplicate grants. The table's columns are on [Core Database](../../reference/database/core-database.md).

### Where access is checked

The access boundary is "does the caller's organization have a `WorkflowTypeOrganizationAccess` row for this type?" — never `Workflow.OrganizationId`.

- **Browsing.** The category and summary queries behind `GET /workflow-categories`, `GET /workflow-categories/summaries` and `GET /workflow-templates/{workflowTypeId}/summary` return only types the caller's organization can access, so the run page lists only those.
- **Reading and re-running past work.** `WorkflowTypeAccessGuard.HasAccessAsync` gates the re-run inputs of an execution, peer candidates, and the editor agent for a non-SuperAdmin; the version-history queries filter by the same rows.
- **Not the template-editing API.** `GET /workflow-templates`, `GET` and `PUT /workflow-templates/{workflowTypeId}/content` are deliberately not gated by access rows (`ListWorkflowTemplatesQuery`, `GetWorkflowTemplateContentQuery`): templates are authored deployment-wide, and the `WorkflowEditor`/`SuperAdmin` role gate is the authoring boundary.
- **Not run submission.** `SubmitWorkflowExecutionCommandHandler` and `StartWorkflowExecutionCommandHandler` load the template by type id (`GetWorkflowAsync`) and check no access row, and neither does `WorkflowExecutionOrchestrator`: a caller that knows a type id can submit a run of it whether or not its organization has been granted the type.


## Management Interface

The Workflow Type Management page is a Blazor Server page (`WorkflowTypeManagement.razor` under `platform/IniaAi.WebApp/Components/WorkflowTypes/Pages/`, route `AppRoutes.WORKFLOW_TYPE_MANAGEMENT`) with two tabs. It reads through `CoreUnitOfWork` directly, with no API in between; Add, Duplicate and the retiring Delete send a command.

### Workflow Types tab

`WorkflowTypeManagementCard` lists every type with its **Id**, **Name**, **Description**, **Category** and **Workflows Count**.

- **Add Workflow Type** and **Edit** (editors) open `WorkflowTypeInfoDialog`: ID, Name, Description, Category, and **Allowed Company Types** (the type's counterparty-type mappings; every screen calls a counterparty a company). On **Create** the dialog sends `CreateWorkflowTypeCommand`, which saves the type, its mappings and a `WorkflowTypeOrganizationAccess` row for the organization the editor is working in, in one unit of work: the same access a template save grants when it creates a type, and no other organization's. An id that a type already holds answers `A workflow type with the id '{id}' already exists. Choose a different id.`, and the id of a deleted type that the weekly cleanup has not removed yet answers `The id '{id}' belongs to a workflow type that was deleted, and it stays taken until the weekly cleanup removes that type. Choose a different id.`; the dialog stays open with the message. Analysts reach a type only under its category, and only once it has a saved workflow, so a type created with **None** as its category is never offered to them (the Category field says so).
- **Duplicate** (editors) opens `DuplicateWorkflowTypeDialog` (see [Duplicating a type](#duplicating-a-type)).
- **Manage Tenants** (operators) opens the tenant dialog below.
- **Delete** (editors) removes the type after a confirmation. A type that exactly one organization can reach is retired, as the [template API's delete](#deleting-a-type-through-the-template-api) does, through `DeleteWorkflowTypeCommand`, and only while the editor acts in that organization: for another organization's type a snackbar asks the editor to switch to that organization first, because retiring deletes that organization's runs and documents. A refusal (a run in progress, for instance) shows in a snackbar. Any other type is deleted at once through `CoreUnitOfWork.WorkflowTypes.Remove`, which removes the `WorkflowTypes` row and its loaded templates with no retention window.
- **Remove All Workflow Types** (SuperAdmin only) removes every type and, as its confirmation says, their workflows.

### Categories tab

`WorkflowTypeCategoryManagementCard` lists categories with **Id**, **Name**, **Description**, **Display Order** and **Workflow Types Count**. Editors add and edit categories through `WorkflowTypeCategoryInfoDialog` and delete them; a category that still has workflow types is refused with `Cannot delete category "<name>" because it has associated workflow types. Please reassign or remove the workflow types first.` A SuperAdmin-only **Remove All** removes only the categories with no workflow types.

### Tenant dialog (Manage Tenants)

`WorkflowTypeTenantAssignmentDialog` shows the type's **Current Tenants**, each with a **Remove** button, and an **Add Tenant** picker listing the active organizations not yet granted. Changes are staged — the dialog lists each pending `+ Add` and `- Remove` — and **Save Changes** applies the diff: an `AddAsync` of a new access row per added organization, a `HardRemove` of the existing row per removed one, and one `CompleteAsync`. Grants left unchanged are never touched. There is no API endpoint for tenant assignment.

## Creating, Duplicating and Deleting Types

### Creating a type through the template API

`PUT /workflow-templates/{workflowTypeId}/content` with a type id that does not exist creates the type before saving its first version: `SaveWorkflowTemplateContentCommandHandler` adds the `WorkflowType` (named by the request's `NewTypeName`, falling back to the id, with `NewTypeDescription` and the `NewTypeCategoryId` category) and grants the caller's organization access. If the save then fails, the new type and its access row are removed again. The request contract is on the [API reference](../../reference/api/index.md).

### Duplicating a type

**Duplicate** on the management page, and `POST /workflow-templates/{workflowTypeId}/duplicate`, run `DuplicateWorkflowTypeCommand`: a new type (`DuplicatedFromWorkflowTypeId` set to the source) whose first version is a clone of the source template. No organization can use the duplicate until **Manage Tenants** grants one. What it carries, what it deliberately leaves behind, and its errors: [Duplicating a template into a new type](../../features/workflow-template-versioning.md#duplicating-a-template-into-a-new-type).

### Deleting a type through the template API

`DELETE /workflow-templates/{workflowTypeId}` runs `DeleteWorkflowTypeCommand`, which retires a type that only the caller's organization can reach (`WorkflowTypeRetirement.RetireAsync`). It refuses:

- a type the platform owns, which is a reserved type (`Generic`, `TestWorkflowType`) or a type a module declares (`ModuleWorkflowTypes`), because the core seeding would recreate it: 400 `WorkflowType.Reserved`;
- a type that does not exist, that the caller's organization cannot reach, or that is already retired: 404 `WorkflowType.NotFound`;
- a type another organization can also reach: 409 `WorkflowType.SharedWithOtherOrganizations`;
- a type with a run of the caller's organization in an active state (`WorkflowExecution.ActiveStates`): 409 `WorkflowType.RunInProgress`.

Otherwise it answers 204. In UserData it soft-deletes the organization's runs, executed workflows, reports and validation cases of the type, and the documents of the type that no run of another type used, with their `FileMetadata` rows so that the cleanup releases the stored files; it deletes the `UserWorkflowTypeVisibility` display filters that name the type. Then in Core it stamps `WorkflowType.RetiredAt` and soft-deletes the access rows, the template, the versions, the signature overrides, the pending overrides and the validation schemas, so that no read of a version or an override reaches the type. Every soft-deleted row carries the deleting user in `DeletedByUserId`. UserData goes first because the two databases share no transaction: a repeat call redoes that step, while a type already stamped retired answers 404.

A retired type is gone from every list: `CoreWorkflowTypeRepository` serves no retired type, and `WorkflowType.UserFacing` excludes it for the surfaces that read the table directly; only `ExistsAsync` still sees it. The existence checks of the version history, the export and the editor agent use `WorkflowType.NotRetired`. It cannot take content again either: `WorkflowSavePipeline.SaveCoreAsync`, which every template save goes through, the template save's rehearsal, and both steps of a bundle import read `WorkflowTypePresenceQuery` and answer 404 `WorkflowType.NotFound` for it, and its id stays taken until the weekly cleanup removes the type: [Retired workflow types](../../operations/soft-delete-cleanup.md#retired-workflow-types).

## API and Database

The read endpoints (`/workflow-categories`, `/workflow-templates/{workflowTypeId}/summary`) and the template editing endpoints are listed with their role gates on the [API reference](../../reference/api/index.md). The `WorkflowTypes`, `WorkflowTypeCategories`, `WorkflowTypeOrganizationAccess` and `WorkflowTypeCounterpartyTypeMap` tables are on [Core Database](../../reference/database/core-database.md).

## Template Versioning

Because a type's live template is shared by every organization granted the type, every save appends an immutable snapshot to one append-only version chain per type (`WorkflowVersion`), and a save names the version it was edited from (`baseVersionId`) so a stale save is refused with 409. How the history, deduplication, retention and the editor's history drawer work: [Workflow Template Versioning](../../features/workflow-template-versioning.md).

## Related Documentation

- [Workflow Engine Architecture](index.md) - The Workflow → Section → Block hierarchy
- [Workflow Template Versioning](../../features/workflow-template-versioning.md) - The version chain behind every template save
- [Workflow Executions API](../../reference/api/workflow-executions.md) - Workflow execution API reference
- [Multi-Tenant Architecture](../multi-tenant/index.md) - Multi-tenancy implementation
