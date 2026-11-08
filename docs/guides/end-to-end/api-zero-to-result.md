---
type: guide
status: current
description: "Walks an API integrator from a system user through a token, discovery, a one-call submit, polling, reading the report tree, and resolving a citation to its source. Read when you wire an external service to the REST API for the first time."
---

# API: Zero to Result

**On this page:** Steps · Done when · Optionally: export the report as a document · Stuck? · Related pages

**Persona:** an integration engineer at the customer (or a partner) wiring an external service to the platform's REST API. You are not using the UI — you want a service to submit work and read structured results back.

**Goal:** go from "I have nothing" to "I retrieved a generated report, parsed it, and resolved a citation back to its source page."

**Prerequisites:**

- A reachable deployment base URL — `<base-url>` (e.g. `https://api.<customer>.example.com`) *(whoever runs your deployment provides this; there is no single hosted endpoint — every deployment has its own URL. On the [one-command Docker stack](../../getting-started/docker-stack.md#the-stack) it is `http://localhost:8080`, whose web server passes `/api/` to the API).*
- A tenant / organization id — `<tenant-id>` *(the organization you are integrating for).*
- The ability to provision (or be given) a **system user** — `<client-id>` / `<client-secret>` *(a platform admin provisions this; see step 1).*
- One or more input documents (PDF, image, or Office) for the workflow you intend to run.

> [!TIP]
> **On the one-command Docker stack**
>
> You run the deployment, so you have every value already. `<base-url>` is `http://localhost:8080`, `<tenant-id>` is `IniaAI` (the stack's one organization), and `<client-id>` is `service_user_iniaai`. The first start made the client secret. Read it from the folder of `compose.yaml`, inside the command that uses it:
>
> ```bash
> CLIENT_SECRET=$(docker compose exec -T api cat /run/inia/platform/OpenIddict__ClientSecret)
> ```
>
> The sample workflow `SampleAnnualReport` needs no counterparty and no document class, so it runs with `platform/samples/annual-report/annual-report.pdf` alone.

Each step links the reference page that owns its contract. This page is the order and the success criteria.

## Steps

### 1. Provision a system user

External services do **not** log in interactively. They authenticate as a **system user** (`IsSystem = true`) using OAuth2 client credentials. The system user must exist and have access to `<tenant-id>`. It needs **no** role: every call on this page takes only authentication plus tenant access.

→ [System Users](../../reference/api/system-users.md) — how to create one and what it can do.

**Checkpoint:** you hold a `<client-id>` and `<client-secret>` for a system user with access to `<tenant-id>`.

### 2. Get an access token

Exchange the client credentials for a bearer token. The token endpoint is anonymous and **tenant-agnostic** — it takes no `X-Tenant-Id` header; the organizations the token can act on come from the system user's assignments and ride in the `tenant_ids` claim.

```bash
TOKEN=$(curl -s -X POST "<base-url>/api/v1/connect/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials&client_id=<client-id>&client_secret=<client-secret>&scope=api" \
  | jq -r '.access_token')
```

A client-credentials token carries the configured lifetime, which the token response states in `expires_in`. When it nears expiry, request a fresh one the same way; it comes without a refresh token ([Authentication → Refresh Token Usage](../../reference/api/authentication.md#refresh-token-usage)).

→ [Authentication → Client Credentials Grant](../../reference/api/authentication.md) · [token lifecycle](../../reference/api/authentication.md#token-lifecycle-management)

**Checkpoint:** `echo $TOKEN` prints a JWT.

### 3. Discover what you can run

You need a **workflow type id** (e.g. `CorporatesCreditReport`) and its **document classes** before you can submit. Do not hard-code a guess — discover them for `<tenant-id>` so you assign the document classes the workflow actually expects.

→ [Discovery](../../reference/api/discovery.md) — list the workflow types your tenant can run and each one's document classes and variables.

**Checkpoint:** you have a `<workflow-type-id>` and the set of document classes it accepts.

### 4. Find the counterparty

A run is about one counterparty: the company the documents describe. Look it up by name and keep its `id`; create it only when the search finds nothing. Skip this step when the summary from step 3 says `"isCounterpartyRequired": false`: submit then takes no `counterpartyId`.

```bash
curl -s -G "<base-url>/api/v1/counterparties" \
  -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: <tenant-id>" \
  --data-urlencode "\$filter=contains(name,'Acme')" \
  --data-urlencode "\$top=20" | jq '.value[] | {id, name}'
```

→ [Counterparties](../../reference/api/counterparties.md) — list, filter, and create.

**Checkpoint:** you have a `<counterparty-id>`.

### 5. Submit the execution in one call

The submit endpoint collapses upload + start into a single multipart request — ideal for service-to-service use. Files are matched to the `metadata.files` array **by index**, and you only supply variables that differ from the workflow's defaults. Pass an `Idempotency-Key` so a retry does not create a duplicate run.

```bash
curl -X POST "<base-url>/api/v1/workflow-executions/submit" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Tenant-Id: <tenant-id>" \
  -H "Idempotency-Key: $(uuidgen)" \
  -F 'metadata={"workflowTypeId":"<workflow-type-id>","counterpartyId":"<counterparty-id>","files":[{"documentClass":"<doc-class>"}],"variables":{"reportingPeriod":"2025-Q4"}};type=application/json' \
  -F "files=@/path/to/report.pdf;type=application/pdf"
```

A `201 Created` returns a `WorkflowExecutionDto` whose `id` is the **execution id** you poll on. The endpoint validates required document classes and variables, so a `400` here means your metadata is wrong, not the server.


→ [Submit Workflow Execution](../../reference/api/submit-workflow-execution.md)

**Checkpoint:** you captured `<execution-id>` from the `201` response (`workflowState` will be `Queued`).

### 6. Poll until the execution finishes

Execution is asynchronous. Poll the execution's state until it leaves the in-flight states. A run lands in **`Draft`** on success (the analyst-review state — there is *no* `Completed`) or **`Failed`** on error.

```bash
while :; do
  STATE=$(curl -s "<base-url>/api/v1/workflow-reports/<execution-id>" \
    -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: <tenant-id>" \
    | jq -r '.workflowState')
  echo "state=$STATE"
  case "$STATE" in Draft|Failed|Cancelled) break;; esac
  sleep 5
done
```

`GET /workflow-reports/{id}` returns the execution DTO (state, type, timing). If you need *why* a `Failed` run failed, read the per-block logs.


→ [Workflow Reports → the execution](../../reference/api/reports.md#get-report-execution) · [Workflow Executions → lifecycle & states](../../reference/api/workflow-executions.md#workflow-execution-lifecycle) · [execution logs](../../reference/api/workflow-executions.md#get-execution-logs)

**Checkpoint:** the loop prints `state=Draft`.

### 7. Read the report tree

There is no `GET /workflow-executions/{id}` for output — the rendered result is read through the **Reports API**. Fetch the section/paragraph tree:

```bash
curl -s "<base-url>/api/v1/workflow-reports/<execution-id>/elements" \
  -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: <tenant-id>" | jq .
```

The response is an ordered tree of `ReportSection` → `ReportParagraph`. Each paragraph carries `content` (the body, with inline `<fact id="…">` markers for numerical facts), a `kind` discriminator (`Paragraph`, `Table`, `DynamicChart`, `Divider`), and its `sources` and `citations`.

→ [Workflow Reports → Get Report Sections](../../reference/api/reports.md#get-report-sections) · [report model](../../reference/api/reports.md#report-model)

If the workflow also sends **data** beside the report, read it with `GET /workflow-reports/<execution-id>/data`: each output carries its JSON as a JSON value under `data`, and a citation for each cited figure with the JSON Pointer of the value that holds it.

→ [Workflow Reports → Get Report Data Outputs](../../reference/api/reports.md#get-report-data-outputs)

**Checkpoint:** you can print the first section's title and its first paragraph's `content`.

### 8. Resolve a citation back to its source

A parsed result is not "done" until you can trace a fact to where it came from — that provenance is the point of the platform. Each `<fact id="cX">` marker in `content` matches a `citations[].id`; a numerical citation's `sources[]` carries a typed `Document` source with `pageNumber` and `documentName` (or a `Url`, or an upstream `Block` chain).

```jsonc
// from citations[] on a paragraph
{
  "id": "c0",
  "isNumerical": true,
  "sources": [
    { "type": "Document", "citedText": "Net revenue rose to $4.2B in FY2024.",
      "pageNumber": 18, "documentName": "Annual Report 2024.pdf" }
  ]
}
```

→ [Workflow Reports → sources vs citations](../../reference/api/reports.md#sources-vs-citations) · [Citations Architecture](../../architecture/citations/index.md)

## Done when

You have a parsed report tree in hand and, for at least one `<fact>` marker in a paragraph's `content`, you resolved its citation to a concrete source — a document page number, a URL, or an upstream block. That round trip (submit → poll → read → trace) is the full integration contract.

## Optionally: export the report as a document

If your service needs a ready-to-send file rather than JSON, export the report to Word (`.docx`) or Markdown (`.md`). The file is streamed synchronously.

→ [Workflow Reports → Export Report](../../reference/api/reports.md#export-report)

## Stuck?

A `401` means the token is missing/expired (step 2). A `403` means the token is valid but the tenant is wrong or the system user has no access to it (step 1). A `404` on submit (`Workflow not found.`) means no workflow template exists for that workflow type id: check the id against discovery (step 3). Submit does not check whether your tenant was granted the type ([where access is checked](../../architecture/workflow-engine/workflow-types.md#where-access-is-checked)), so use only the ids discovery returns. A `400` on submit means metadata validation failed — usually a missing required document class. If you need a base URL, tenant id, or a system user provisioned, ask whoever runs your deployment.


## Related pages

- [System Users](../../reference/api/system-users.md) · [Discovery](../../reference/api/discovery.md) — provision and discover.
- [Authentication](../../reference/api/authentication.md) — every grant type and the token lifecycle.
- [Submit Workflow Execution](../../reference/api/submit-workflow-execution.md) — the single-call multipart contract.
- [Workflow Executions](../../reference/api/workflow-executions.md) · [Workflow Reports](../../reference/api/reports.md) — lifecycle and output.
- [Citations Architecture](../../architecture/citations/index.md) — how provenance is built and traced.
