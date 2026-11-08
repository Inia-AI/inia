---
type: explanation
status: current
description: "Documents the frontend API service layer: BaseService, its interceptors, the per-domain triad, the API singleton, error messages, OData, timestamps, file transfer and the inventory of every service module. Read when you add or change a call to the platform API from the Vue app, or need to know which frontend module calls an endpoint."
---

# API Service Layer

**On this page:** BaseService · Interceptors · The per-domain triad · The `API` singleton (`src/api/index.ts`) · Validation: Zod is selective · OData support · Timestamps · File upload and download · Module inventory

The IniaAi.Web frontend talks to the platform WebApi exclusively through a thin service layer rooted at `src/api/`. Every domain gets the same triad — a `service.ts` of axios methods, a `schema.ts` of TypeScript contracts, and an `index.ts` of TanStack Query hooks — and every service extends a single `BaseService` that centralizes the axios instance, tenancy and auth headers, the API version prefix, and 401 handling. No component or store is allowed to reach for raw `axios` or `fetch`; routing every call through `BaseService` is what keeps the SPA working behind the production nginx reverse proxy.

## BaseService

`src/api/base/index.ts` defines `BaseService`. Each domain service subclasses it and calls `super(params)` so they all share one configured axios instance.

```ts
export class BaseService {
  instance: AxiosInstance
  protected apiVersion = 'v1'

  constructor({ base, withCredentials }: ServiceParams = { base: '', withCredentials: true }) {
    this.instance = axios.create({
      baseURL: `${base || getApiUrl()}`,
      withCredentials,
    })
    // request + response interceptors registered here
  }

  protected getApiPath(path: string): string {
    return this.apiVersion ? `api/${this.apiVersion}/${path}` : path
  }
}
```

Key points:

- **Version prefix.** `apiVersion` is `'v1'`, so `getApiPath('documents')` resolves to `api/v1/documents`. Domain services always wrap their relative path in `getApiPath(...)` — they never hard-code `api/v1`.
- **Base URL.** The axios `baseURL` comes from `getApiUrl()` (`src/config/api.ts`), which reads `VITE_API_URL` at build time. When the variable is unset it is `/`, the app's own origin, where the image's nginx proxies `/api/` to the API. The production image leaves it unset — see [Build & Deploy](build-and-deploy.md).
- **Credentials.** `withCredentials` defaults to `true`, so cookies ride along with cross-origin requests.

> [!WARNING]
> **Never call raw axios or fetch**
>
> In production the SPA is served by nginx using **root-relative paths**, and an nginx `location /api/` block reverse-proxies API calls to the backend. Requests must resolve against the configured `baseURL` for that proxy to catch them, and they must carry the tenant and auth headers the interceptors inject. A bare `axios.get('/api/...')` or `fetch(...)` bypasses both — it will miss `X-Tenant-Id`/`Authorization` and can break under the deployed path layout. Always go through a `BaseService` subclass.

## Interceptors

`BaseService` registers two axios interceptors on the shared instance.

**Request interceptor.** The request interceptor of `BaseService` injects the multi-tenant and auth headers on every outbound call:

```ts
config.headers['X-Tenant-Id'] =
  localStorage.getItem('selectedOrgId') || getDecodedToken()?.organization_id
config.headers.Authorization = `Bearer ${localStorage.getItem('accessToken')}`
```

`X-Tenant-Id` prefers the explicitly selected organization (`localStorage.selectedOrgId`, set by the org switcher) and falls back to the `organization_id` claim decoded from the JWT. This header is what the backend's tenant-resolution middleware keys on — see [API Architecture](../multi-tenant/api-architecture.md).

**Response interceptor.** The response interceptor of `BaseService` handles expired sessions. On a `401`, it clears the stored tokens and redirects to the login route, unless the request is a sign-in step:

```ts
if (error.response?.status === 401 && !error.config?.signInStep) {
  localStorage.removeItem('accessToken')
  localStorage.removeItem('refreshToken')
  localStorage.removeItem('expiration')
  const { default: router } = await import('@/plugins/router') // lazy to avoid circular dep
  router.push({ name: 'Login' })
}
```

A sign-in step is a request sent with `signInStep: true`, a field `src/api/base/index.ts` adds to axios's `AxiosRequestConfig`. The `Authentication` service sets it on the password exchange (`connect/token`), the second-factor exchange (`connect/token/mfa`) and the email code request (`connect/token/mfa/email`). Those endpoints answer `401` for a wrong password, a wrong code or a lockout, and the sign-in screen shows the reason instead of being sent back to itself.

> [!NOTE]
> **Lazy router import**
>
> The router is imported dynamically inside the interceptor rather than at module top level. The router module imports guards that in turn depend on the API layer, so a static import would create a circular dependency. The `await import('@/plugins/router')` defers resolution until a 401 actually occurs.

See [State, Routing & Auth](state-routing-and-auth.md) for token storage and the router guards.

## The per-domain triad

Each directory under `src/api/<domain>/` follows the same three-file shape (`diagnostics` and `meta` have no `schema.ts`, and `signaturePlayground` keeps its types in `types.ts`):

| File | Responsibility |
|---|---|
| `service.ts` | A `Service extends BaseService` whose async methods call `this.instance.<verb>(this.getApiPath(...))` and return typed data. |
| `schema.ts` | TypeScript `interface`/`type` request and response contracts (and, in a few modules, Zod schemas). |
| `index.ts` | A class exposing TanStack Query hooks (`useGetX`, `useCreateX`, …) wrapping the service. |

The hooks layer is built on `@tanstack/vue-query`. Read hooks use `useQuery` with **reactive query keys** (wrapped in `computed(...)` so they re-run when their reactive inputs change); write hooks use `useMutation` and call `queryClient.invalidateQueries` in `onSuccess` to refresh dependent reads. From `src/api/documents/index.ts`:

```ts
useGetDocuments(params: Ref<DocumentsQueryParams>, config = {}) {
  return useQuery<OData<DocumentSummary> | null>({
    queryKey: computed(() => ['documents', 'list', JSON.stringify(params.value)]),
    queryFn: () => this.service.getDocuments(params.value),
    ...config,
  })
}

useDeleteDocument() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => this.service.deleteDocument(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['documents'] }),
  })
}
```

Query keys are namespaced by domain (`['documents', 'list', ...]`), so a mutation can invalidate an entire domain by its prefix. Detail queries gate themselves with `enabled: computed(() => Boolean(id.value))` so they don't fire on empty ids. More on this pattern in [Forms, Tables & Composables](forms-tables-and-composables.md).

One key is shared across domains on purpose: `['workflow-category-summaries']`. `WorkflowCategories.useGetWorkflowCategorySummaries`, `Workflows.useGetWorkflowTypeOptions` and `useMyReviewCounts` all read it, and every one of them fetches through `API.WorkflowCategories.service.getWorkflowCategorySummaries()`, so a page that needs both the summaries and the type options sends one request. `useGetWorkflowTypeOptions` derives its options from the cached summaries with `select: toWorkflowTypeOptions`. A new reader of that key fetches through the same service call, because whichever reader subscribes first runs its fetch for all of them.

## The `API` singleton (`src/api/index.ts`)

Components never `new` a domain class themselves. `src/api/index.ts` composes every per-domain hooks class into a single `API` class, instantiates it once, and **default-exports that instance**. Views and layouts import it as a singleton and reach a domain by its property name:

```ts
import API from '@/api'

const { data: counterparties, isLoading } =
  API.Counterparties.useGetCounterparties(queryParams)
```

This is the load-bearing line that makes `API.Counterparties.useGetX(...)` resolve. The folder triad from the previous section is necessary but **not sufficient** — until the class is registered here, `API.<Domain>` is `undefined` and the view fails at runtime with "cannot read property of undefined". Registration is the step that connects a new module to the rest of the app.

The aggregator is three parallel lists — an import, a typed field, and a constructor instantiation — that every module appears in exactly once:

```ts
// src/api/index.ts
import CounterpartyTypes from './counterpartyTypes'   // 1. import the default-exported class
// …

class API {
  CounterpartyTypes: CounterpartyTypes               // 2. declare the typed field
  // …
  constructor() {
    this.CounterpartyTypes = new CounterpartyTypes() // 3. instantiate once
    // …
  }
}

const instance = new API()
export default instance                              // the singleton every component imports
```

### Naming map: one domain, four casings

A single domain shows up under four spellings, and mixing them up is the most common reason a freshly added module won't resolve. They are derived mechanically:

| Place | Casing | Example |
|---|---|---|
| Folder under `src/api/` | `camelCase` (plural) | `src/api/counterpartyTypes/` |
| `API` property / class name in `index.ts` | `PascalCase` (plural) | `API.CounterpartyTypes` |
| Schema interface in `<domain>/schema.ts` | `PascalCase` (singular) | `CounterpartyType` |
| Backend path passed to `getApiPath(...)` | `kebab-case` | `counterparty-types` |
| Route + URL (see [Adding a Feature](contributing-a-feature.md#6-navigation-make-the-route-reachable)) | `kebab-case` | `/counterparty-management` |

> [!WARNING]
> **Two barrels, not one**
>
> There are **two** index/barrel files in play and they hold different things — do not confuse them. `src/api/index.ts` is the runtime **singleton** above (`import API from '@/api'`). `src/api/schema.ts` is a **type-only** barrel that re-exports a subset of per-domain `schema.ts` files (`export * from './counterparties/schema'`), so shared types like `ODataQueryParams` are importable from `@/api/schema`. A module's interfaces are reachable either through that barrel **or** directly from `@/api/<domain>/schema` — both resolve; the barrel only exists for the handful of cross-cutting types worth a short import.

### Error handling

The 401 handling in the response interceptor is the only error logic baked into `BaseService`. Beyond that, error handling is the hook's job: apart from list services that swallow failures and return `null` (see [OData support](#odata-support)), service methods (detail reads, creates, updates, deletes) let the axios error propagate. TanStack Query catches it and surfaces it on the hook — components read `isError`/`error` from a `useQuery` result and `isPending`/`error` from a `useMutation`, then render the failure state (toast, inline message) or disable controls while a mutation is in flight. No global error toast exists; each call site decides how to react.

A call site that shows the server's reason reads it with `extractApiErrorMessage(error, fallback)` from `src/api/base/errors.ts`. The platform answers errors as `ProblemDetails`, and the helper picks the first message in `errors` (a validation failure), then `title` (a conflict such as a duplicate counterparty puts its message there), then `detail`, and returns `fallback` when none holds text. It skips the generic titles ASP.NET puts on a validation or unhandled-error response ("One or more validation errors occurred", "An unhandled error occurred"), so a localized fallback is not replaced by them. `problemDetailsMessage(data)` is the same pick for a caller that already holds a parsed body, such as an error read back from a blob response.

## Validation: Zod is selective

Most contracts are plain TypeScript interfaces — they exist only at compile time and are not enforced at runtime. A small set of modules additionally use [Zod](https://zod.dev/) where runtime parsing of dynamic or untrusted shapes is worth the cost:

| Module | Why Zod |
|---|---|
| `dynamicChart/schema.ts` | Parses chart configuration produced by a workflow block. A schema beside the option builder, not an API module. |
| `documentCategories` | Category contracts. |
| `graphRag` | Graph-RAG query/response shapes. |

Everywhere else, trust the TypeScript types; do not assume a runtime guard exists.

## OData support

List endpoints that page, filter, and sort speak OData. The shared shapes live in `src/api/base/schema.ts`:

```ts
export interface ODataResponse<T> {
  '@odata.count': number
  '@odata.skip': number
  '@odata.top': number
  value: T[]
}

export interface ODataQueryParams {
  $skip?: number
  $top?: number
  $filter?: string
  $orderby?: string
  $select?: string
  $expand?: string
  $count?: boolean
}
```

The wire format uses `@odata.*` keys; the rest of the app prefers a cleaner `OData<T>` (`count`/`skip`/`top`/`value`). The adapter in `src/api/base/adapter.ts` bridges them:

```ts
export const toOdata = <T>(data: ODataResponse<T>): OData<T> => ({
  count: data['@odata.count'],
  skip: data['@odata.skip'],
  top: data['@odata.top'],
  value: data.value,
})
```

A service passes `ODataQueryParams` straight through axios `{ params }` and runs the response through `toOdata`. OData is the norm for list endpoints, not the exception: **eight shared modules** consume the adapter — `counterparties`, `counterpartyTypes`, `documents`, `jobs`, `modelPricing`, `tokenUsage`, `users`, and `workflowReports`. List services typically `try`/`catch` and return `null` on failure so the UI can render an empty state rather than throw.



## Timestamps

Every timestamp the API writes is UTC, and the wire format says so on every deployment: the API registers `UtcDateTimeJsonConverter` (in `AddControllerConfiguration`, `ControllerConfiguration.cs`), which writes an Unspecified-kind `DateTime` with a `Z` marker, so a SQL Server deployment (whose `datetime2` carries no offset) serializes `createdAt` the same way a Postgres deployment does. The frontend still guards its own side: `parseServerDate` in `src/lib/utils.ts` appends the `Z` when a marker is missing, and every formatter and every elapsed-time computation (`useLiveDuration`, the desk's started clock, the table date columns) goes through it. Parse a server timestamp with `parseServerDate(value)`, never with `new Date(value)`: a zone-less string read as local time lands hours away from the instant the server meant, and the size of the error is the viewer's offset from UTC. A calendar bucket the server pins at UTC midnight (a summary month, a summary day, a reporting month, a test date) is a different thing from an instant: render it with `asCalendarDay(value)`, which reads the UTC fields, so a viewer west of UTC does not see the evening before. `formatMonth` already does this. Date-only values (`2026-09-17`) are not timestamps and keep their own handling.

## File upload and download

Binary transfer departs from the JSON default in two ways, both shown in `src/api/files/service.ts`.

**Upload** posts `FormData` as `multipart/form-data` and reports progress via axios `onUploadProgress`:

```ts
async uploadDocument(formData: FormData, onUploadProcessChange: (percent: number) => void) {
  const { data } = await this.instance.post<FileDto>(this.getApiPath('files'), formData, {
    headers: { 'Content-Type': 'multipart/form-data' },
    onUploadProgress: e => {
      if (e.lengthComputable && e.total && e.loaded) {
        onUploadProcessChange(Math.round((e.loaded / e.total) * 100))
      }
    },
  })
  return data
}
```

**Download** sets `responseType: 'blob'` and returns the raw axios response so the caller can build an object URL:

```ts
async getFile(fileId: string, disposition: 'preview' | 'download' = 'download') {
  return this.instance.get(this.getApiPath(`files/${fileId}`), {
    params: { disposition },
    responseType: 'blob',
  })
}
```

See [Documents API](../../reference/api/documents.md) for the backend contract behind these calls.


## Module inventory

The shared `API` object (`src/api/index.ts`) registers **28 service modules**, one per folder under `src/api/` except `base/`, which holds the abstraction itself, and `dynamicChart/`, which is the chart option builder and calls no endpoint ([Charts](charts.md)). The path column is the relative path passed to `getApiPath(...)`, i.e. the call resolves to `api/v1/<path>`. There is no `aiProofreading` module: proofreading was removed with the WebScraper feature family.


### Shared modules (`src/api/`)

| Module | Primary path (`api/v1/…`) | Purpose |
|---|---|---|
| `agentActivity` | `workflow-executions/blocks/{blockId}/agent-activity` | The activity digest of one agent block (`getBlockActivity`, with an optional `round`), read by the desk's agent activity rail. |
| `authentication` | `connect/token`, `connect/token/mfa`, `connect/token/mfa/email`, `auth/providers`, `auth/2fa/…` | Token issuance and the second-factor sign-in step, the list of sign-in providers, and the two-factor settings: status, authenticator setup, enable and disable, recovery codes, trusted devices. |
| `contentRefinement` | `ai/content-refinement` | AI content-refinement operations. |
| `counterparties` | `counterparties` | Counterparty records (OData list), create, update, delete, and the bundle export and import (`counterparties/export`, `counterparties/import`). |
| `counterpartyTypes` | `counterparty-types` | Counterparty type definitions: list, create, update, delete. |
| `diagnostics` | `diagnostics/logs/download` | Diagnostic log retrieval. |
| `documentCategories` | `document-categories` | Document category taxonomy (Zod). |
| `documents` | `documents` | Documents + pages, OData list, patch (the static mark), one page's full text, the text download, process, bulk delete. |
| `executedWorkflows` | `executed-workflows/download` | Downloading executed-workflow artifacts. |
| `files` | `files` | Multipart upload + blob download, and `uploadEndpoint`, the absolute upload URL the uploader prints in its diagnostics. |
| `graphRag` | `workflow-executions/{runId}/graph/query` + `workflow-executions/{runId}/graph/validate/{targetTag}` | Graph-RAG provenance queries (`queryProvenance`) and target-tag validation (`validateTarget`) over an execution (Zod). |
| `health` | `health` (no prefix — see note) | Backend health probe. |
| `jobs` | `jobs` | Background job listing/status (OData). |
| `meta` | `meta/version` | Build/version metadata. |
| `modelPricing` | `model-pricing` | Model pricing table (OData). |
| `pages` | `pages/{id}/image` | Page image retrieval. |
| `password` | `passwords/forgot`, `passwords/reset` | The forgot-password request and the password reset. |
| `peerCandidates` | `counterparties/peer-candidates` | The counterparties a run can name as peers, searched by name and filtered by workflow type. |
| `profile` | `profiles/change-password` | Current-user profile / password change. |
| `refinementPresets` | `ai/content-refinement/presets` | Content-refinement presets. |
| `signaturePlayground` | `workflow-executions/{id}/blocks`, `workflow-executions/{id}/test-signature-prompt` | The blocks of a past run and a test of a signature prompt against one of them. |
| `signatures` | `workflow-types/{id}/signatures` | Signature field overrides per workflow type, their pending proposals and votes, and their history. |
| `tokenUsage` | `token-usage`, `token-usage/me`, `token-usage/dashboard`, `token-usage/workflow-type/{id}` | Token-usage accounting (OData list, the caller's summary, the organization dashboard, one workflow type's summary), and one report's run cost (`workflow-reports/{id}/cost`, read by `RunCostDialog` on the report page for super admins). |
| `users` | `users`, `users/me/tenants`, `users/resolve` | User administration (OData), the caller's organizations with each one's `capabilities`, and usernames resolved to full names. `useGetTenants(enabled, staleTime)` shares one query (`TENANTS_QUERY_KEY`) between the organization switcher and `useCapabilities()`; `useGetUsers(params, enabled)` skips the read where the organization lacks `team-view`. |
| `workflowCategories` | `workflow-categories/summaries` | Workflow category summaries. |
| `workflowExecutions` | `workflow-executions` | Workflow execution records — start, cancel (`workflow-executions/{id}/state`), logs and their placeholders, and the re-run check (`getReRunInputs`, `workflow-executions/{id}/rerun-inputs`). Not OData. |
| `workflowReports` | `workflow-reports` | Workflow reports (OData), their sections and paragraphs, their elements, the export, and report feedback. |
| `workflows` | `workflow-templates/{workflowTypeId}/summary` | Workflow type options derived from the category summaries (`useGetWorkflowTypeOptions`, which shares the summaries query described above and fetches through `WorkflowCategories`) and the per-type workflow summary (`getWorkflowSummaryByType`). |


> [!TIP]
> **One documented exception**
>
> `health/service.ts` calls `this.instance.get('health')` **directly, without `getApiPath(...)`**, so it hits `health` rather than `api/v1/health`. This is the single intentional deviation from the version-prefix convention; every other service routes through `getApiPath`.

The Health screen (`HealthView`, route `/health`) is the only reader of `getHealth`, and it does not show the API's health on a deployment. The API maps `/health` with `MapHealthChecks` in `Program.cs`, which answers the plain text `Healthy`, not the `HealthResponse` JSON that `src/api/health/schema.ts` declares. Behind the web image, `VITE_API_URL` is `/` and the image's nginx answers `location = /health` itself with the plain text `ok`: a direct visit to `/health` shows that text instead of the SPA's screen, and the screen's probe reads nginx's answer, never the API's. [Build and deploy](build-and-deploy.md) owns the nginx routes.
