---
type: explanation
status: current
description: "Overview of multi-tenancy inside one deployment: the organization as tenant, membership and roles, how a request gets its tenant, what is shared and what is per organization, the one deliberate cross-tenant read, how organizations are created and deactivated, and the audit records. Read when you need the tenancy model before touching data access, or look for which multi-tenant page owns a detail."
---

# Multi-Tenant Architecture

**On this page:** Overview · Multi-Tenancy Model · How a request gets its tenant · Shared vs. Tenant-Isolated Data · Creating and deactivating an organization · Audit records · The two front ends and tenant context · Constraints on future evolution · Related Documentation

The IniaAi Platform is deployed **on-premises or inside each customer's own cloud**. Within a single deployment, several organizations can share the same application instance while keeping their data apart.


## Overview

The platform implements an **organization-scoped multi-tenancy** model. Tenant data lives in the `UserData` schema with an `OrganizationId` on every tenant-owned row. Isolation is enforced at the **application layer**: the API resolves the active tenant in middleware and passes it into every command and query as `OrganizationId`, and handlers filter on it. There is **no global EF Core tenant query filter** and no database row-level security: the only model-wide `HasQueryFilter` is the soft-delete filter (`IsDeleted == false`). Do not assume queries are auto-scoped; [Data isolation](data-isolation.md) explains why there is no tenant filter.

| Page | Owns |
|---|---|
| [API Architecture](api-architecture.md) | The request pipeline, the tenant header, the exact 403 responses |
| [Data Isolation](data-isolation.md) | Where `OrganizationId` scoping happens, foreign keys, the admin app's tenant switch and by-id reads |
| [Database Design](database-design.md) | The two contexts, what each holds, template access, the tenant descriptor, migrations |
| [Security Model](security-model.md) | Authentication, the threat model, encryption |
| [Deployment](deployment.md) | The processes of one deployment and their health checks |
| [Risk Assessment](risk-assessment.md) | Isolation risks and their controls |

## Multi-Tenancy Model

### Organization-Based Tenancy

**Tenant Unit**: the `Organization` entity in the UserData database (`platform/IniaAi.Domain/Entities/UserManagement/Organization.cs`): a `Name`, an `IsActive` flag, the organization's own web-search keys (`ParallelApiKey`, `PerplexityApiKey`, `FmpApiKey`), its `SignatureReviewThreshold` and its `EditorAgentDailySpendBatchUsd`.

- Each organization is a separate tenant.
- Users can belong to several organizations through the `UserOrganization` join table (`IsDefault`, `IsActive`, `JoinedAt`, `LeftAt`). `User.OrganizationId` is a legacy direct link.
- Data is scoped by an `OrganizationId` column; shared infrastructure, separated rows.

### Roles

The join table carries **membership only — there is no `Role` column**. Roles are ASP.NET Identity roles, global per user: a role granted in one organization applies in every organization the user belongs to. The platform defines `SuperAdmin`, `Admin`, `User`, `WorkflowEditor`, `SignatureContributor` and `Operator`. An organization `Admin` may grant only the roles that act within organizations (`User`, `Admin`, `SignatureContributor`, and the module roles marked so); every other role, `WorkflowEditor` included, only a `SuperAdmin` grants (`RoleGrants`). The gates each role opens are on [Authorization](../../features/user-management/authorization.md).



## How a request gets its tenant

On the REST API the client names the active organization in the `X-Tenant-Id` header on every call. Finbuckle.MultiTenant resolves it (`HeaderTenantStrategy` reads the header, `OrganizationTenantStore` looks the organization up in the UserData database and refuses an inactive one, and the result is cached for 10 minutes); `EarlyTenantRejectionMiddleware` refuses a protected request without a resolved tenant before the token is validated; `TenantUserContextMiddleware` then checks the caller's membership claims (`tenant_ids`, else `tenant_id`, else `organization_id`) and fills `IRequestContext`. A `SuperAdmin` reaches any organization. There is no switch-organization endpoint: the same token works for every organization the user belongs to. The pipeline, step by step, is on [API Architecture](api-architecture.md).

## Shared vs. Tenant-Isolated Data

The platform uses **two EF Core DbContexts** that share the same `Workflow`/`Block` entity classes but persist different shapes: `CoreDbContext` (schema `Core`) holds workflow **templates** and deployment-wide configuration; `UserDataDbContext` (schema `UserData`) holds **executed** workflow instances and all tenant data. Their contents are listed on [Database Design](database-design.md#two-dbcontexts-not-two-databases).

**Templates are shared by access, not owned.** A `WorkflowType` is reachable by the *set* of organizations that have a `WorkflowTypeOrganizationAccess` row for it; among them the live template is one shared `Workflow` row, and `Workflow.OrganizationId` is only a "who last saved" stamp. Per-organization variation comes from `SignatureFieldOverride`, never from the template.

**One deliberate cross-tenant read**: `GET /workflow-types/{workflowTypeId}/feedback` (`WorkflowTypeFeedbackController`, handled by `ListWorkflowTypeFeedbackQueryHandler`) returns every organization's [report feedback](../../features/report-feedback.md) on a workflow type, each item stamped with its `organizationId`. It exists because the template is shared across the organizations that have access to the type, so the people who tune it need the feedback of all of them. It is SuperAdmin only: the controller carries `Roles.SUPER_ADMIN`, and the handler answers Forbidden unless the controller marked the caller as a SuperAdmin. It is the only read of `ReportFeedbackItems` that does not filter by the caller's `OrganizationId`; the per-report reads and the status change (`ReportFeedbackByExecutionSpec`, `ReportFeedbackByIdTrackedSpec`) stay scoped to the caller's organization. Any new cross-organization read must be listed here.

Other surfaces read across organizations by design, outside the tenant header: background jobs, the operator pages, and the shared Core templates.


## Creating and deactivating an organization

The first start seeds the platform organization, `IniaAI`, and the customer organization (`UserDataDbInitializer`). After that, organizations are managed on the admin app's **Tenant Management** card (`TenantManagementCard.razor`, through `TenantInfoDialog`): **Create** adds one, **Edit** changes its name, its signature review threshold, its editor agent daily budget and its **Active** flag. There is no delete. An organization whose `IsActive` is false stops resolving as a tenant (`OrganizationTenantStore` returns nothing), so every API call naming it is refused with `403 Tenant context required`. Granting an organization access to a workflow type adds a `WorkflowTypeOrganizationAccess` row through the **Manage Tenants** dialog ([Workflow types](../workflow-engine/workflow-types.md)); adding a user to it adds a `UserOrganization` row.

Data of a removed user or document follows the soft-delete and cleanup rules on [Soft-Delete Cleanup](../../operations/soft-delete-cleanup.md).

## Audit records

There is **no generic audit table**. Operational events, authentication events included (`LoggingAuthenticationAuditLog`), go to the application log with organization and user context as structured properties. The persisted audit records are per subsystem:

- `SignatureFieldOverrideAuditLog` (Core) — proposals and votes around signature overrides.
- `DocumentUnlockAuditLogs` (UserData) — every password-protected upload the platform unlocked.




## The two front ends and tenant context

**`IniaAi.WebApp`** is the **Blazor Server** admin and workflow-editor app. It does **not** call the REST API: it runs the platform's services in its own process, holds the selected organization in `AppState`, and checks it itself — see [Tenant Switching](data-isolation.md#tenant-switching-in-the-blazor-admin-ui) and [By-id reads](data-isolation.md#by-id-reads-in-the-blazor-webapp). Its sign-in is ASP.NET Core Identity with cookies.

The user-facing **Vue 3 SPA** (`IniaAi.Web`) calls the REST API with a bearer token and the `X-Tenant-Id` header — see [Frontend (Vue SPA)](../frontend/index.md). The SPA reads workflow execution progress by polling the same API; there is no SignalR hub, so every API path but the health checks and the development-only Swagger passes the tenant middleware.

## Constraints on future evolution

Recorded so any future change respects them (no committed timelines here):

- **Not a hosted SaaS.** Any future feature must work without a central control plane and must assume that each deployment holds its own credentials.
- **Workflow authoring stays in the customer environment.** Cross-instance coordination has to be peer-to-peer (e.g. file-based), not via a shared authoring server.
- Open directions the design supports: per-customer high-availability or multi-region topologies (an infrastructure choice per deployment — see [Deployment](deployment.md)), and broader model-provider coverage as customers bring their own platforms.

---

## Related Documentation

- [Data Isolation](data-isolation.md) - Detailed isolation patterns
- [Security Model](security-model.md) - Authentication and authorization
- [Database Design](database-design.md) - Multi-tenant database schema
- [File Storage Architecture](../file-storage/index.md) - Multi-tenant file storage
- [API Architecture](api-architecture.md) - Organization context handling
- [Risk Assessment](risk-assessment.md) - Isolation risks and mitigations
