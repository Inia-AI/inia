---
type: reference
status: current
description: "The canonical role-gate matrix of the v1 API, the role catalog and who may grant each role, the checks handlers make beyond role gates, and how the admin app gates its pages. Read when you need to know which role opens an endpoint or a page, grant a role, or add a gate to new code."
---

# Authorization

**On this page:** The role catalog · Roles are global; membership is per-organization · Capabilities · Checks inside handlers · Authorization in the Admin App (Blazor) · Authorization Patterns · What is not built · Related Documentation

The IniaAi Platform authorizes a request in four layers, all enforced in code:

1. **Tenant checks** on every protected API request: the `X-Tenant-Id` header must name an open organization the token carries ([API Reference → Tenant Access Control](../../reference/api/index.md#tenant-access-control)). `TenantUserContextMiddleware` asks `ITenantAccessPolicy` whether the user may act in it; the platform's `MembershipTenantAccessPolicy` admits a member, and a super admin in any organization. A refusal answers `403` with `Invalid tenant context`. A closed module can replace the policy.


2. **Role gates**: `[Authorize(Roles = ...)]` on controllers, actions and Blazor pages, backed by global ASP.NET Core Identity roles. [The canonical role-gate matrix](#the-canonical-role-gate-matrix-v1-api) lists every gated API surface.
3. **Capability gates**: `[RequiresCapability(...)]` on the endpoints a closed module can take away from an organization ([Capabilities](#capabilities)). The platform grants every capability but `delete-own-documents`, which only a closed module adds, so on its own this layer refuses only a plain user's document deletes.
4. **Handler checks** for rules a role cannot express: every query carries an explicit `OrganizationId` predicate, and a few handlers decide access themselves ([Checks inside handlers](#checks-inside-handlers)).

> [!WARNING]
> **Granular / Casbin RBAC is a design, not the live system**
>
> A Casbin-based authorization design and a Granular RBAC model (`resource:action:scope` permissions, a permission catalog) were proposed and **not** built. No authorization policy is registered: the only `AddPolicy` calls in the code configure CORS and the sign-in rate limit. Access control is role gates plus tenant isolation, as this page describes.

## The role catalog

The platform defines **6 global roles** in `platform/IniaAi.Domain/Entities/UserManagement/Roles.cs`. A closed module adds its own as a `ModuleRole`. `RoleGrants.DefinedRoles` lists the platform's roles and the roles of every module the host registers; the seeder and the Roles tab both read that list, so a build without a module neither seeds nor offers its roles. A module role marked `GrantedToTheCustomerAdmin` is granted to the seeded customer admin (`RoleGrants.CustomerAdminModuleRoles`).



| Constant | Value | Grants (beyond base authentication) |
|----------|-------|-------------------------------------|
| `Roles.SUPER_ADMIN` | `SuperAdmin` | Every gated surface — the only role present in every `[Authorize(Roles = ...)]` gate. Kept for break-glass work: the Tenants tab, user administration across organizations, granting `SuperAdmin`, `WorkflowEditor` and `Operator`, every "Remove All" control, and acting in an organization without membership |
| `Roles.ADMIN` | `Admin` | The organization's administrator: its users and their organization roles, counterparty bundles, document deletion, the token-usage dashboard; **not** template authoring, **not** workflow types, **not** signature review |
| `Roles.USER` | `User` | Nothing extra — the baseline role for running workflows and viewing results |
| `Roles.WORKFLOW_EDITOR` | `WorkflowEditor` | Deployment-wide authoring: workflow templates (`WorkflowTemplatesController`) and every designer, Tools Settings prompts, workflow types and their categories, counterparty types, and document categories |
| `Roles.OPERATOR` | `Operator` | Deployment operations (`OPERATOR_ROLES`): the server log, executed-workflow download and the Executed Workflows page, model pricing, tenant model settings, the token-usage dashboard, and which organizations may run a workflow type |
| `Roles.SIGNATURE_CONTRIBUTOR` | `SignatureContributor` | Signature-override review surfaces (view, propose, vote) |






**There is no role hierarchy** — roles are independent flags (see [the canonical matrix](#the-canonical-role-gate-matrix-v1-api)). Names such as *RealMoneyFunds*, *Corporates*, *Sovereigns*, or *IMF Data* are **workflow types / categories**, not roles the code defines. Some older databases still hold role rows by the first three names; they gate nothing, and the Roles tab no longer offers them.

## Roles are global; membership is per-organization

Roles are **not** scoped per organization. A user's roles are global ASP.NET Core Identity roles stored in `AspNetUserRoles`; what varies per organization is *membership* — a `UserOrganization` row (`UserId`, `OrganizationId`, `IsDefault`, `IsActive`, `JoinedAt`, `LeftAt`) with **no role column**.

At sign-in, `AuthenticationController.AddRoleClaimsAsync` reads the user's global roles via `_userManager.GetRolesAsync(user)` and emits one `role` claim per role into the access token; tenant context travels in separate `tenant_ids` / `tenant_id` (preferred) / `organization_id` (legacy) claims ([Authentication → Multi-Tenant Token Claims](../../reference/api/authentication.md#multi-tenant-token-claims)). The API reads roles from the token alone, so a role granted or removed reaches the API only at the user's next sign-in: neither an existing access token nor a refresh picks it up ([Refresh Token Grant](../../reference/api/authentication.md#3-refresh-token-grant)).

### How roles are created and assigned

- **Role rows are seeded**, not created at runtime: `UserDataDbInitializer` (`platform/IniaAi.Infrastructure/Persistence/UserData/Contexts/`) adds every role in `RoleGrants.DefinedRoles` that the database lacks, on every start, and assigns seed users' roles by inserting `AspNetUserRoles` rows directly. A new role therefore reaches an existing deployment on its next start.
- **Who may grant what** (`RoleGrants.CanGrant`): a super admin grants any defined role; an organization admin grants only the organization roles — `User`, `Admin`, `SignatureContributor`, and each module role marked `ActsWithinOrganizations`. `SuperAdmin`, `WorkflowEditor` and `Operator` reach across organizations, so only a super admin grants them.


- **Who an organization admin manages** (`RoleGrants.CanManage`): no other user who holds `SuperAdmin`, `WorkflowEditor` or `Operator`, or who also belongs to an organization outside the admin's: no edit, password reset, disable, membership change or role change. Roles are global, so a role or a password the admin set would act in that other organization too. The Users tab shows a lock on that row instead, and lists only the organizations the admin belongs to, never another customer's name. The admin's own row stays open to them, since the rule guards other people's accounts.
- **An organization admin works inside their organizations**: Create User lists only the organizations the admin belongs to, and the membership dialog removes a user only from those organizations.
- **Runtime assignment happens only in the Blazor admin UI**: `RoleManagementCard.razor` (`platform/IniaAi.WebApp/Components/Admin/Components/`) adds/removes `IdentityUserRole` rows through the UserData unit-of-work — it writes the join table directly rather than going through `UserManager.AddToRoleAsync` (which has zero call sites).
- **The v1 REST API has no role management and no user CRUD** — `UsersController` is list/me-tenants/resolve only. An integrator cannot assign roles over the API.

## Capabilities

A capability is a neutral name for something an organization may do (`OrganizationCapabilities`). `IOrganizationCapabilities` answers an organization's set; the platform's default, `EveryCapability`, grants all of them, so every deployment behaves as before. A closed module can take some away from an organization.


- **The server decides.** `[RequiresCapability]` on an endpoint or its controller gives that endpoint a `CapabilityGateFilter`, an authorization filter that runs before the request body is read; an endpoint without the attribute gets no filter. When the organization lacks the capability, the request answers `403` with the problem title `Not included`, the detail `Your account does not include this feature. Contact support if you need it.` (or the reason the capabilities give) and a `capability` field naming the missing capability. A request that names no organization has no capabilities.
- **The client hides.** `GET /users/me/tenants` lists each organization's `capabilities` ([Users API](../../reference/api/users.md)), and the Vue app hides each menu entry, button and page a missing capability removes; an address behind one says the account does not include it ([State, routing and auth](../../architecture/frontend/state-routing-and-auth.md#capabilities-what-the-organization-may-use)).


| Capability | Endpoints |
|---|---|
| `run` | `POST /workflow-executions`, `POST /workflow-executions/submit`, `GET /workflow-executions/{id}/rerun-inputs` |
| `upload` | `POST /files`, `POST /documents/{id}/process`, `POST /documents/text` |
| `explore` | every `/workflow-executions/{runId}/graph` endpoint |
| `refine` | every `/ai/content-refinement` endpoint, the presets included |
| `run-logs` | `GET /workflow-executions/{id}/logs`, `.../logs/{logId}`, `.../logs/{logId}/placeholders`, `GET /workflow-executions/blocks/{blockId}/agent-activity` |
| `run-evidence` | `GET /executed-workflows/download`, `GET /workflow-executions/{id}/agent-runs`, `.../agent-runs/{blockId}/trace`, `.../agent-runs/{blockId}/file`, `GET /workflow-reports/{id}/cost` |
| `usage-cost` | `GET /token-usage/me` |
| `standalone-processing` | `POST /documents/{id}/process`, `POST /documents/text`, which also need `upload`: they store or page a document, so an organization that cannot upload cannot use them either |
| `team-view` | `GET /users` |
| `signatures` | every `/workflow-types/{workflowTypeId}/signatures` endpoint, `GET /workflow-executions/{id}/blocks`, `POST /workflow-executions/{id}/test-signature-prompt` |
| `admin-app` | No API endpoint: the Blazor admin app's sign-in. `AdminAppSignInManager.CanSignInAsync`, which every sign-in method asks first (password, second factor, recovery code, an outside provider), refuses a user whose organization lacks it, and `SignInWithClaimsAsync`, where every sign-in that issues the cookie ends, an outside provider's and a refresh included, issues none for that user. The outside-provider page also asks `CanSignInAsync` before it signs in, and sends the user back to sign-in with "This account cannot sign in to the admin app. Sign in to the Inia app instead, or ask an administrator if you need the admin app." |
| `delete-own-documents` | `DELETE /documents/{id}` and `POST /documents/bulk-delete`: `Admin`, `Operator` and `SuperAdmin` delete any document of the organization; anyone else passes `[StaffOrCapability]` only with this capability, and deletes only the documents they uploaded (another user's answers as an unknown id). It is not in `OrganizationCapabilities.All`, so the platform grants it to no one; the self-serve module adds it |

In the open build, `baselines`, `validation`, `report-templates` and `monitoring` gate no endpoint: the endpoints they gate belong to closed modules.


A capability gate adds to the role gate; it never opens an endpoint the roles close. `CapabilityGateMatrixTests` and the module tests beside them pin each row that names endpoints.

## Checks inside handlers

Some access rules live in the handler, not in a role attribute:

- **Run evidence** (`RunEvidenceAccess`): the executed-workflow download, the agent run files and the run cost carry no role attribute. The handler admits `Operator` and `SuperAdmin` for any run in the organization (the cost: `SuperAdmin` only) and the organization's service user (`User.IsSystem`) for a run it started; anyone else gets `403` (`Only a staff role or the service user that started this run can read its evidence.`), and a run of another organization answers `404`.
- **Other organizations' rows** answer `404`, as for an unknown id: most by-id reads filter by `OrganizationId` in the query, and the document, page, page-image, file, job, export and run graph handlers check the row's organization after the read and answer `404` too, with the text an unknown id gets (`Document not found.`, `Page not found.`, `Job not found.`, `Workflow execution not found.`). There is no EF Core global tenant filter to fall back on — only the soft-delete filter is registered ([Data Isolation](../../architecture/multi-tenant/data-isolation.md)).
- **Cross-organization feedback** (`GET /workflow-types/{workflowTypeId}/feedback`): the controller requires `SuperAdmin`, and the handler answers `403` again unless the controller marked the caller as a super admin. It leaves out every organization closed to staff (`IOrganizationsClosedToStaff.ClosedToAllStaff`, which closes none by default; the self-serve module closes every self-serve organization), whatever support grants the caller holds, because a grant lets one person read one account and never a read across organizations. The editor agent's feedback across organizations leaves them out the same way, and so does a super admin's reach in the admin app's file downloads (`CallerOrganizations`). The admin app's pages that list or act across organizations leave out every closed organization whatever grants the staff member holds, because a grant lets one person read one account and these pages delete runs, add members and create users: the Executed Workflows page, the Tenants, Users and Roles tabs, the Edit User dialog's memberships, user creation, membership changes, and the tenant switcher. `AppState` filters the switcher's list at sign-in and reads the closed set again when one of these pages loads, so a customer who signed up since is left out too. One page keeps them on purpose: the workflow type's organization assignment dialog on **Workflow Type Management** lists every active organization, customers included, because it is how a customer is offered its report types today. It shows organization names and changes which types an organization may run, never a customer's content.


- **A report's content** (`GET /workflow-reports/{id}/elements`, `.../data`, `.../export/{format}`, and every `/workflow-executions/{runId}/graph` endpoint): `[ReportContent]` asks `IReportContentRule`, which hides nothing by default, and fails the request when its route lacks the value it names.


- **The editor agent** (`EditorAgentAccess` in the WebApp, and the same check inside `WorkflowEditorAgentService`): `WorkflowEditor` or `SuperAdmin`, on a deployment that switched it on, and the organization must have access to the workflow type (a super admin needs no access row). Some editor pages also admit module roles, so the page's own gate is not enough; **Admin is not included**.

### Workflow type access

Workflow templates are shared across organizations, and `WorkflowTypeOrganizationAccess` (`platform/IniaAi.Domain/Entities/Workflows/WorkflowTypeOrganizationAccess.cs`) says which organizations may use a type; `Workflow.OrganizationId` is a "who last saved" stamp, not an ownership key. The join is checked where a type is listed or described: the category listings, `GET /workflow-templates/{workflowTypeId}/summary`, the re-run check, the peer candidates and the signature reads (`WorkflowTypeAccessGuard.HasAccessAsync` or an equivalent query).

**Starting a run does not check it.** `POST /workflow-executions` and `POST /workflow-executions/submit` start any workflow type whose workflow exists, even a type the organization's listings leave out ([Submit Workflow Execution](../../reference/api/submit-workflow-execution.md#system-user-requirements)). The template endpoints are deliberately not gated by it either: templates are authored deployment-wide by the editor roles.

## Authorization in the Admin App (Blazor)

The admin app gates a page with `@attribute [Authorize(Roles = ...)]` and a control inside a page with `<AuthorizeView Roles="...">`.

**Where the page gate is the only gate.** Tools Settings (`ToolsSettings.razor`, `EDITOR_ROLES`: `WorkflowEditor`, `SuperAdmin`) edits the platform-wide prompt sets — OCR, Excel analysis, Topics, Reconciliation, Use Auto-Detected ToC and Graph RAG ([Tools Settings](../tools-settings.md)) — and no v1 API endpoint serves any of them: the page binds the entity and saves through EF change tracking. The tabs render only inside this page, and the router checks only the routed page's attribute, so the page's gate is the tabs' gate.




**The editor pages.** The Workflow Editors gallery (`/editors`, `WorkflowDesigners.razor`) admits any signed-in user. A workflow type's designer page (`/editors/{WorkflowTypeId}`, `WorkflowDesignerPage.razor`), which hosts the editor, admits `WorkflowEditor` and `SuperAdmin` — the same roles as the template API. `Admin` does not open it: a template is shared by every organization that runs its type. A closed module can add a designer page of its own for a type, as a `ModuleDesignerPage` with its roles and tile icon. The pages gate on the roles `DesignerPages` gives for each type, and the gallery checks the same roles, so it shows a tile only when its page opens for the user. A user whose roles open no designer sees a note instead of tiles. The gallery's **Compare across deployments** and **Import workflow bundle** buttons show only to `WorkflowEditor` and `SuperAdmin`, the roles their pages admit. `DesignerPagesTests` pins the pages to those roles and the tile rule.


**The left menu shows each link to exactly the roles its page admits.** Every role-gated page the menu links to states its roles once, as a `public const string ROLES`, gates on it with `[Authorize(Roles = ROLES)]`, and the menu (`NavMenu.razor`) wraps the link in an `AuthorizeView` on the same constant: Component Library and Tools Settings (`WorkflowEditor`, `SuperAdmin`), Workflow Type Management (`WorkflowEditor`, `SuperAdmin`, `Operator`), Executed Workflows (`Operator`, `SuperAdmin`), and Administration (`SuperAdmin`, `Admin`).


**Controls inside a page carry their own gate.** On Workflow Type Management, adding, editing, duplicating and deleting a type or a category is for editors, **Manage Tenants** is for operators, and every "Remove All" button is for `SuperAdmin`. **Clear Workflows** on Executed Workflows and **Restart Application** on Administration are for `SuperAdmin` only. The editor's **Log** button is for operators. `ControlRoleGatesTests` renders each card as every role (bUnit) and pins which controls each role sees; `AdministrationTests` does the same for **Restart Application**. Dashboard and Workflow Editors admit every signed-in user and have no wrapper. `NavMenuRoleGatesTests` renders the menu as every role and pins its links to the pages that role opens, so a user never sees an entry that ends in Access denied — a `WorkflowEditor`-only user, for example, sees no Executed Workflows entry.

**A refused page.** When the router refuses a page, a signed-in user goes to `/Account/AccessDenied`, the page a direct visit's 302 also reaches. Only a visitor who is not signed in goes to the sign-in form (`RedirectUnauthorized`, wired in `Routes.razor`), so a missing role never reads as an expired session.

### API-key access to admin-app controllers

The admin app's MVC controller `FileDownloadController` (`/FileDownload`, any signed-in user) carries `RoleOrMessageProcessorApiKeyAuthorizeAttribute` (`platform/IniaAi.WebApp/Attributes/`). It admits a signed-in caller who holds one of the named roles, **or** a request whose `X-API-Key` header equals `PlatformSettings.MessageProcessor.ApiKey`, compared in constant time (`CryptographicOperations.FixedTimeEquals`). The key is empty by default, so key access stays off until a deployment sets one.


A refused request answers `401` with `{ "error": "..." }`:

| Refusal | `error` |
|---|---|
| No role match and no key configured | `Access denied. Required roles: {roles} or valid API key.` |
| No role match and no `X-API-Key` header | `Access denied. Required roles: {roles} or include X-API-Key header.` |
| A wrong key | `Invalid API Key.` |

A request admitted by key rather than by role is marked, and `AdmittedByApiKey` reads the mark. The key carries no user, so a controller asks `CallerOrganizations.ReadableAsync`, which limits such a request to `PlatformSettings.CustomerOrgId` ([By-id reads in the Blazor WebApp](../../architecture/multi-tenant/data-isolation.md#by-id-reads-in-the-blazor-webapp)).

## Authorization Patterns

### The canonical role-gate matrix (v1 API)

This table is transcribed from the `[Authorize]` attributes on the v1 controllers — it is the **only** permission matrix in these docs; other pages link here instead of restating it. Everything not listed carries just the base `[Authorize]` from `ApiController` (any authenticated user in a valid tenant).

| API surface | Controllers / endpoints | Roles beyond authentication |
|---|---|---|
| Workflow template authoring | `WorkflowTemplatesController` (`EDITOR_ROLES` on every action **except** `GET /{workflowTypeId}/summary`, which any authenticated user can call — it feeds the CreateWorkflow page) | `WorkflowEditor`, `SuperAdmin` — **not Admin** |
| Shared-component Library | `SharedComponentsController` — `EDITOR_ROLES` on every action | `WorkflowEditor`, `SuperAdmin` — **not Admin** |
| Document categories | `DocumentCategoriesController` — `POST`, `PUT /{id}` and `DELETE /{id}` carry `EDITOR_ROLES`; the list is ungated. Categories have no organization, so a change reaches every organization | writes: `WorkflowEditor`, `SuperAdmin` |
| Document deletion | `DELETE /documents/{id}` and `POST /documents/bulk-delete` on `DocumentsController`; every other document endpoint is ungated | `Admin`, `Operator`, `SuperAdmin`, or a `User` whose organization has `delete-own-documents`, for the documents they uploaded |
| Organization token usage | `GET /token-usage/dashboard`, `GET /token-usage` and `GET /token-usage/workflow-type/{workflowTypeId}` on `TokenUsageController`; `GET /token-usage/me` stays open to every user | `Operator`, `Admin`, `SuperAdmin` |
| Signature review | `SignaturesController` (controller-level, 7 actions), plus `GET /{id}/blocks` and `POST /{id}/test-signature-prompt` on `WorkflowExecutionsController` | `SuperAdmin`, `SignatureContributor` — **not Admin** |
| Operations | `TenantModelSettingsController`, `ModelPricingController`, `DiagnosticsController` (all controller-level or sole-action) | `Operator`, `SuperAdmin` |
| Run evidence | `GET /executed-workflows/download`, `GET /workflow-executions/{id}/agent-runs`, `.../agent-runs/{blockId}/trace`, `.../agent-runs/{blockId}/file`: no role attribute (`RoleGateMatrixTests` pins them open); the handler admits staff for any run in the organization and the organization's service user for a run it started, 403 for anyone else | `Operator`, `SuperAdmin`, or the service user that started the run |
| Run cost | `GET /workflow-reports/{id}/cost`: no role attribute; the handler admits a super admin for any run in the organization and the organization's service user for a run it started, 403 for anyone else | `SuperAdmin`, or the service user that started the run |
| Counterparty types | `POST /counterparty-types`, `PUT /counterparty-types/{id}` and `DELETE /counterparty-types/{id}` on `CounterpartyTypesController` — a type is shared by every organization, so changing one is authoring work; the built-in types ([Counterparty types](../../reference/api/counterparties.md#counterparty-types)) keep their names and cannot be deleted, for any role | `WorkflowEditor`, `SuperAdmin` |
| Counterparty bundles | `GET /counterparties/export` and `POST /counterparties/import` on `CounterpartiesController` — the export reveals every email and financial analysis of the organization, the import rewrites its master data | `Admin`, `SuperAdmin` |
| Report feedback | `WorkflowReportsController`: `POST` and `GET /workflow-reports/{reportId}/feedback` and `DELETE /workflow-reports/{reportId}/feedback/{feedbackId}` carry no role gate (the delete is org-scoped like the list); `PATCH /workflow-reports/{reportId}/feedback/{feedbackId}/status` carries `EDITOR_ROLES`. `WorkflowTypeFeedbackController` (controller-level, one action: `GET /workflow-types/{workflowTypeId}/feedback`) carries `SUPER_ADMIN` and reads every organization's items. It is the one deliberate cross-tenant read, and its handler also answers 403 to a caller the controller did not mark as a SuperAdmin; see [Report Feedback](../report-feedback.md) | leave, list and delete: none; status change: `WorkflowEditor`, `SuperAdmin`; cross-organization read: `SuperAdmin` only, **not Admin, not WorkflowEditor** |
| Everything else — counterparties (except the bundle export and import), counterparty type reads, documents (except deletion), files, jobs, users, profiles, 2FA, a user's own token usage, workflow executions (except the signature routes), reports (except the feedback status change), content refinement | all remaining v1 controllers | none — any authenticated user |


`RoleGateMatrixTests` (`IniaAi.WebApi.UnitTests`) pins the document-category, counterparty-type, operations, token-usage, document-deletion and report-feedback gates. The endpoints that stay open are on the open list below. The anonymous endpoints are the token endpoints, `GET /auth/providers`, `POST /passwords/forgot`, `POST /passwords/reset`, and the three `/meta/*` routes.

**No endpoint ships open by accident.** `EndpointGateTests` (`IniaAi.WebApi.UnitTests`) lists every action of every API controller and fails on one that has no role gate and is not on the open list, `OpenEndpoints`, which names each open endpoint, whether it is anonymous, and why it is open. A new endpoint therefore gets a role gate or an entry a reviewer reads. The failure names the route and both fixes. A second check fails on an entry whose endpoint is gone or has gained a gate, so the list stays exact. The list covers role gates only; the capability that keeps a self-serve organization out is named in an entry's reason where it applies ([Capabilities](#capabilities)). The health checks (`/health`, `/readiness`) are not controller actions and are outside the test.

**There is no strict role hierarchy.** Roles are independent flags: `SuperAdmin` appears in every gate, but `Admin` is deliberately excluded from template authoring and signature review. "SuperAdmin > Admin > WorkflowEditor > User" is a fiction the codebase does not implement — do not design against it. ASP.NET role checks have no hierarchy either, so a gate spells out every role it admits: use the `*_ROLES` constants (`EDITOR_ROLES`, `ADMIN_ROLES`, `OPERATOR_ROLES`, `ADMIN_AND_OPERATOR_ROLES`), never a bare role constant.


**There is no user or organization CRUD in the v1 API.** `UsersController` has exactly three endpoints (`GET /users`, `GET /users/me/tenants`, `POST /users/resolve`), none role-gated; user and organization administration happens in the Blazor admin UI, not through the v1 API.

**The per-user [workflow type visibility](workflow-type-visibility.md) filter is not authorization.** `UserWorkflowTypeVisibility` rows narrow what `GET /workflow-categories/summaries` and the executions listing return, but gate nothing: no rows means everything the organization reaches, rows only narrow, and an execution hidden from the list is still fully reachable by direct id.

## What is not built

- Named authorization policies, claims-based handlers and resource-based handlers: none is registered or used.
- A permission catalog or `resource:action:scope` permissions (the Granular RBAC design).
- An authorization audit log: a refused request leaves only ordinary log lines, such as the tenant middleware's warnings.

## Related Documentation

- [Authentication](../../reference/api/authentication.md) - User authentication and JWT tokens
- [User Management](index.md) - User profiles and organization relationships
- [Multi-Tenant Architecture](../../architecture/multi-tenant/index.md) - Organization-based tenancy
- [Data Isolation](../../architecture/multi-tenant/data-isolation.md) - Multi-tenant data separation
