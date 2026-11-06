---
type: reference
status: current
description: "Reference for the read-only Jobs API: listing and fetching the caller's own background jobs, the JobDto shape, the state values and the errors. Read when you poll a run's progress through /jobs or wonder why a job is missing from the list."
---

# Background Jobs API

**On this page:** Which jobs the Jobs API sees · Endpoints · JobDto fields · Error Responses · Related pages

The Background Jobs API exposes two read-only endpoints for monitoring long-running operations in the IniaAi Platform. Jobs are **not** created through this API — they are started internally as a side effect of workflow execution. The API only lets you list jobs and fetch a single job.

> [!WARNING]
> **Removed: PDF-generation jobs and the download endpoint**
>
> Earlier versions documented a `PdfGeneration` job type and `GET /jobs/{id}/download`. Both were removed together with the document-generation feature — `JobsController` now has exactly the two actions below. Execution outputs are read via [Workflow Reports](reports.md) and files via the [Files API](files.md).

## Which jobs the Jobs API sees

The Jobs API reads the in-memory job list of the WebApi process (`JobsService`): there is no database table and no retention, and the list empties when the process restarts. The admin app (WebApp) keeps a job list of its own, so a run started from the admin app is not listed here. How jobs are queued, run and cancelled: [Background Jobs Service](../../architecture/background-jobs/index.md).

Both endpoints show the caller only their own jobs in the organization named by `X-Tenant-Id`: the list filters by organization and user. A single job of another organization answers `404`, as an unknown id does, and a job of another user in the caller's organization answers `403`. No role widens this, so a SuperAdmin also sees only their own jobs.

## Endpoints

### List Background Jobs

#### `GET /api/v1/jobs`

Lists the caller's jobs in the current organization, with OData query support ([OData list conventions](index.md#odata-list-conventions)). The list has no default page size: without `$top` it returns every job.

**Query Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `$filter` | string | OData filter expression |
| `$orderby` | string | OData ordering expression |
| `$top` | integer | Maximum number of results |
| `$skip` | integer | Number of results to skip |
| `$count` | boolean | Include total record count |

**Example Request:**

```bash
curl -X GET "https://api.example.com/api/v1/jobs?\$top=10&\$orderby=createdAt desc" \
  -H "Authorization: Bearer {token}" \
  -H "X-Tenant-Id: {organization_id}"
```

**Response (200 OK):** `ODataPaginatedResponse<JobDto>`

```json
{
  "value": [
    {
      "id": "job-12345",
      "state": "Done",
      "type": "WorkflowExecution",
      "error": null,
      "createdAt": "2025-01-15T10:30:00Z",
      "completedAt": "2025-01-15T10:32:15Z",
      "progress": 100,
      "description": "Quarterly report",
      "userId": "user-789"
    }
  ],
  "@odata.count": 1,
  "@odata.skip": 0,
  "@odata.top": 10
}
```

### Get Background Job

#### `GET /api/v1/jobs/{id}`

Retrieves one of the caller's jobs by ID.

**Path Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | string | Yes | Background job ID |

**Response (200 OK):** a `JobDto`

```json
{
  "id": "job-12345",
  "state": "Processing",
  "type": "WorkflowExecution",
  "error": null,
  "createdAt": "2025-01-15T10:30:00Z",
  "completedAt": null,
  "progress": 75,
  "description": "Counterparty onboarding",
  "userId": "user-789"
}
```

The `JobDto` shape is defined in `platform/IniaAi.WebApi.Contracts/Responses/Jobs/JobDto.cs` and mapped from the domain entity by `BackgroundJobMapper.MapToResponse`. `state` is the `JobState` name as a string, `type` the `BackgroundJobType` name, and `progress` a number from 0 to 100.

## JobDto fields

### `type`: the job types

`BackgroundJobType` (`platform/IniaAi.WebApi.Contracts/Enums/BackgroundJobType.cs`) holds three values:

| Type | Description |
|------|-------------|
| `None` | Default / unspecified |
| `WorkflowExecution` | A workflow run: document loading, model calls, output post-processing |
| `Delegated` | In-process work that is not a workflow run, today an editor agent conversation turn. It runs in the admin app's process, so this API never lists one |

### `state`: the status values

`JobState` holds the job states a `JobDto` reports. A job is finished at `Done`, `Error` or `Cancelled`; the transitions between the states are on [Background Jobs Service](../../architecture/background-jobs/index.md).

| State | Description |
|-------|-------------|
| `None` | Initial state before scheduling |
| `Queued` | Waiting in the in-memory queue |
| `Preparing` | A worker picked up the job |
| `Anonymizing` | Redacting confidential values in the input documents (confidential jobs only, in the full edition: the open build refuses a confidential run) |
| `Processing` | Processing and loading the input documents, then executing the workflow |
| `Error` | Failed; `error` holds the message |
| `Cancelled` | Cancelled by user or system |
| `Done` | Completed successfully |

Poll `GET /jobs/{id}` to observe `state` and `progress`, then read the results through [Workflow Reports](reports.md) once the job is `Done`.

## Error Responses

The Jobs API answers an error with RFC 7807 `ProblemDetails` (produced by `ApiController.Problem`):

| Status | When | `title` |
|---|---|---|
| `404` | No job with this ID in the WebApi process, or the job belongs to another organization | `Job not found.` |
| `403` | The job belongs to another user in the caller's organization | `You are not authorized to access this job.` |

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Job not found.",
  "status": 404,
  "code": "General.NotFound",
  "traceId": "00-..."
}
```

## Related pages

- [Background Jobs Service](../../architecture/background-jobs/index.md) — the authoritative description of the job state machine, the `Anonymizing → Processing` ordering, concurrency control, and the in-memory/no-persistence model.
- [Submit Workflow Execution](submit-workflow-execution.md) — the call that creates a `WorkflowExecution` job.
- [Workflow Executions](workflow-executions.md) — the execution that a job runs once dequeued.
