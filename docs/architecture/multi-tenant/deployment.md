---
type: explanation
status: current
description: "The processes one deployment runs (API, admin app, Vue SPA behind nginx, agent runtime, databases, object storage), what is not there (no Redis, no separate job worker), the health endpoints and every health check. Read when you need what a deployment consists of, or what /health and /readiness report."
---

# Multi-Tenant Deployment Architecture

**On this page:** Container Architecture · Configuration · Monitoring and Observability · Scaling

The IniaAi Platform is **not a hosted SaaS product**. It is deployed **on-premises or inside the customer's own cloud account**. Customer deployments use the customer's own infrastructure, identity provider, model credentials (Anthropic, AWS Bedrock, Azure AI Foundry, or a gateway), and storage. Multi-tenancy within a single deployment lets one installation serve several organizations of the same customer.


The platform targets **.NET 10** (`global.json` pins the SDK; `Directory.Build.props` sets `net10.0`) and runs on **PostgreSQL** or SQL Server, selected per `DbContext` (`PlatformSettings__CoreDbProvider` / `PlatformSettings__UserDataDbProvider`).

## Container Architecture

### Components

A deployment runs three containers of the platform's own:

- **`api`** — `IniaAi.WebApi`, the REST API (tenant middleware, CQRS, workflow engine, its own job service).
- **The admin app** — `IniaAi.WebApp`, the Blazor Server admin app (interactive server render mode + MudBlazor), which runs the platform in-process with its own job service. See [The two front ends](index.md#the-two-front-ends-and-tenant-context).
- **`web`** — nginx serving the Vue SPA and proxying `/api/` to the `api` container.

The open repository's `compose.yaml` runs them as the services `api`, `webapp` and `web`, beside `postgres`, `agent-runtime` and two helpers, `secrets` and `sample` ([The Docker Stack](../../getting-started/docker-stack.md)).


Beside them a deployment needs:

- **The agent runtime** — its own container ([Agent Runtime Integration](../services/infrastructure/agent-runtime-integration.md)), reached at `PlatformSettings:AgentRuntime:BaseUrl`.
- **A database server** — holding the `Core` and `UserData` schemas (the two `DbContext`s may share one physical database or be split — a deployment choice).
- **Object storage** — MinIO for document storage, or the database-backed provider for small and development deployments ([File Storage](../file-storage/index.md)).

There is **no Redis dependency** — tenant-lookup caching uses an in-memory distributed cache (`AddDistributedMemoryCache` + Finbuckle's `WithDistributedCacheStore`), not a separate cache server. There is **no separate background-jobs container**; background work runs in-process inside each .NET host ([Background Jobs](../background-jobs/index.md)).

The Dockerfiles live with the code they build: the platform repository's `docker/Dockerfile` builds the API and the admin app on the .NET 10 SDK and runtime images, and the frontend repository's `Dockerfile` builds the web image. How to build them yourself is on [Docker builds](../../getting-started/docker-builds.md).


## Configuration

Settings come from `appsettings.json` (mounted into each container), environment variables (`Section__Key`), and, on AWS and Azure, the secret store loaded at startup (AWS Secrets Manager or Azure Key Vault). Connection strings bind from `ConnectionStrings__CoreDbConnection` / `ConnectionStrings__UserDataDbConnection`. Every key, with its default, is on [Application Configuration](../../operations/application-configuration.md).

## Monitoring and Observability

Logging uses **Serilog**. There is no Prometheus `/metrics` endpoint in the application. Health is exposed through two endpoints mapped in `Program.cs`: `/health` (basic liveness) and `/readiness` (full report via the health-check UI response writer). Both bypass tenant and authentication middleware (see [API Architecture → Excluded Paths](api-architecture.md#excluded-paths)), so probes need no `X-Tenant-Id` or token. `/health` answers every caller. The readiness report lists every internal check and a failing check's error, so only a caller on the API's own host, such as `curl` inside the container, reads it (`MapHealthChecksWithHostOnlyReport`). Any other caller, such as a load balancer or a Kubernetes probe, gets the same status code with the overall status alone (`Healthy`, `Degraded` or `Unhealthy`), whatever case it writes the path in.

### Health checks

Health checks are registered in `platform/IniaAi.WebApi/Configuration/HealthCheckConfiguration.cs` (`AddHealthChecksConfigured`):

| Check | Fails as | What it verifies |
| ----- | -------- | ---------------- |
| `MemoryHealthCheck` ("Service Memory Check") | Unhealthy | Process memory pressure |
| `AddDbContextCheck<UserDataDbContext>` ("UserDataDbContext") | Unhealthy | `UserData` context connectivity |
| `AddDbContextCheck<CoreDbContext>` ("SecondDB") | Unhealthy | `Core` context connectivity |
| `DbContextFactoryHealthCheck<UserDataDbContext>` ("UserDataDbContext Factory") | Unhealthy | The pooled `IDbContextFactory` produces a working context |
| `BrowserSandboxHealthCheck` ("Browser sandbox") | Degraded | Degraded once a link from an email skipped the browser step because the Chrome sandbox does not start |
| `TokenUsageRecordingHealthCheck` ("Token usage recording") | Degraded | Degraded once this process failed to save a token usage record, with the count, the time and the newest failure's usage type, model and kind of error. That spend is missing from usage and billing; the log line beside it names the workflow type and the execution. It stays Degraded until the process restarts |
| `ObjectStoreHealthCheck` ("S3 Storage", "MinIO Storage") | Degraded | One per object store that is set up (`FileStorage:S3:BucketName`, `FileStorage:MinIO:Endpoint`): lists at most one object of the store's bucket through the store's own client, or the buckets when MinIO keeps a bucket per organization. Gives up after 3 seconds, and an answer stands for a minute ([File Storage](../file-storage/index.md#health-checks)) |

`Degraded` still answers HTTP 200, so no probe restarts the container on it. The admin app registers the token usage check too and reports it on its own `/health`. There is no Redis health check (no Redis) and no generic "external APIs" check.

## Scaling

A deployment scales vertically: more memory and CPU for the host, and the job concurrency settings ([Job concurrency](../../operations/application-configuration.md#job-concurrency)). Job state is in-process, so two API instances do not share a queue ([Background Jobs → Scalability](../background-jobs/index.md#scalability-considerations)).

No multi-region or global-load-balancer topology is implemented. Because the product runs in the customer's own environment, regional and high-availability topology is a per-customer infrastructure decision: a customer that needs multi-region runs the standard deployment in each region and provides its own load balancing and database replication.

