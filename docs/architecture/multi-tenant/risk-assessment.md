---
type: explanation
status: current
description: "Catalogs multi-tenant isolation risks and their mitigations, e.g. tenant forgery, missed query scoping, cross-tenant template exposure, and the accepted residual items. Read when you assess a change's isolation risk or review what isolation leaves to convention."
---

# Risk Assessment

Multi-tenant isolation risks and the controls that mitigate them. The platform applies defense-in-depth so that no single failure exposes another organization's data.

| Risk | Mitigation |
| ---- | ---------- |
| Forged or missing tenant context | `X-Tenant-Id` is resolved by Finbuckle (`OrganizationTenantStore` accepts only an existing, active organization) and `EarlyTenantRejectionMiddleware` refuses a protected request without a resolved tenant before authentication, with an immediate 403. |
| Authenticated user accessing another org | `TenantUserContextMiddleware` checks the validated JWT claims against the requested tenant after authentication; mismatch returns 403. |
| Missed isolation in a query | **No automatic backstop exists** — there is no EF Core global tenant filter (only soft-delete is registered). Every handler/specification must carry an explicit `OrganizationId` predicate, enforced by convention (commands/queries take `OrganizationId` as their first parameter) and review. See [Data Isolation](data-isolation.md). |
| Tenant enumeration / brute force | Cached tenant lookups plus early rejection keep invalid-tenant traffic cheap and uniform. |
| Cross-tenant template exposure | Template **consumption** (execution submission, org-user surfaces) is gated by `WorkflowTypeOrganizationAccess` (the type-access join), not by `Workflow.OrganizationId`. Template **authoring** (the editor and the editor-role template API) is deliberately role-gated only: templates are deployment-wide artifacts maintained by staff and agent editors, so no org-access check applies there. |
| A path that skips the tenant checks | Only the health checks and the development-only Swagger skip them (`MiddlewareConstants.ExcludedPaths`). The API maps no SignalR hub, so no WebSocket path bypasses the tenant middleware. |
| Compromised provider credentials | Each deployment holds its own AI (Anthropic/Bedrock/Foundry), storage (MinIO/S3), and identity credentials; the platform ships none. |

## Residual / accepted items

- **Template authoring and version history are deployment-wide** (one chain per `WorkflowTypeId`; `LatestWorkflowVersionByTypeSpec` keys by type alone). Editor-role callers list, read, and edit every template and its history regardless of org access rows — the `WorkflowEditor` role is the authoring boundary. It is a deployment-wide role: roles are global per user, and only a `SuperAdmin` may grant it (`RoleGrants`). Org access rows scope consumption only.
- **Another organization's job or export id can be confirmed.** The job and export handlers answer a row of another organization with `Forbidden` and a missing row with `NotFound`, so a caller can learn that such an id exists elsewhere; the row itself is never returned. The document, page, page-image and file reads answer both cases with `NotFound` ([Data Isolation → Command/Query Validation](data-isolation.md#commandquery-validation)).
- **Cross-context transactions are not guaranteed.** `Core` and `UserData` may be separate databases, so consistency across them is coordinated at the application layer rather than by a distributed transaction.

For the layered request pipeline that implements these controls, see [API Architecture](api-architecture.md).
