---
type: runbook
status: current
description: "Customer-facing runbook for a deployment in the customer's own environment: what a release contains, how to obtain the images, host sizing, the secrets and TLS the operator supplies, bring-up on one Docker host, health checks, upgrades and rollback. Read when you deploy, upgrade or roll back the platform from release images in your own environment."
---

# Release Package Deployment (customer-facing)

**On this page:** What a release package contains · How you obtain the images · Minimum host spec · Prerequisites · Secrets and configuration you supply · TLS certificate options · Bring-up: single host (Docker Compose) · Health verification · Final step: First Login · Upgrades and rollback · Done when · Related pages

This page is the **customer-facing** deploy path: how an operator running the platform **in their own environment** obtains a release and brings it up. A deployment runs **pre-built images and config templates**; its hosts do not build from source.


> [!NOTE]
> **You deploy artifacts, not source**
>
> A release is a **versioned set of three pre-built Docker images** plus a compose stack and configuration templates. You supply your own secrets and credentials.
>

## What a release package contains

| Artifact | What it is |
|----------|------------|
| **API image** (`iniaai/api`) | The .NET backend (workflow engine, RAG, AI connectors). |
| **Web image** (`iniaai/web`) | The Vue SPA frontend, served by `nginx` (built `node:24-alpine` → `nginx:alpine`). |
| **Monitoring image** (`iniaai/monitoring`) | The admin/monitoring service (the "admin" component). |
| **Compose stack** | `docker-compose.yml` wiring the three services, ports, health checks, and volume mounts. |
| **Config templates** | `appsettings.json` and `.env` templates with placeholder tokens you fill in. |

All three images are versioned with a single clean `x.y.z` tag (the same version across the set). The api and admin images are built on a shared base image carrying the render toolchain (headless Chrome, fonts), and the web image on nginx, so no host-side dependencies are required beyond Docker.


## How you obtain the images

Your hosts pull the pinned `x.y.z` images from a registry that they can reach, such as your own ACR, ECR or private registry. To build the images yourself from the open repository, see [The Docker Stack → The images](../getting-started/docker-stack.md#the-images) for their recipes, then tag them with the release version and push them to your registry.


> [!WARNING]
> **Pin a version — do not deploy `:latest`**
>
> Always deploy an explicit `x.y.z` tag across all three images so the set is consistent and reproducible. `:latest` is a moving target: it moves whenever a build tags it. It must not be used for a customer environment, although the reference compose files fall back to it when `.env` names no image.
>

## Minimum host spec

Size the host from the container memory ceilings the compose stack actually sets. The release's reference single-host compose (`docker-compose.yml`) caps the two .NET services at **8 GB each** (`mem_limit: 8g` on `api` and `monitoring`) and the web container at **1 GB** — about **17 GB** for the three containers, with the database on a **separate** Postgres/RDS host. The HTTP-only `docker-compose-http.yml` caps `api` at **4 GB** instead.


| Scenario | Database | Minimum host | Disk |
|----------|----------|--------------|------|
| **Evaluation** | Postgres co-located on the VM | **2 vCPU, 16 GB RAM** | 64 GB SSD |
| **Production single-VM** | External (managed Postgres / RDS) | **4 vCPU, 32 GB RAM** — the reference compose ceilings are 8 GB (api) + 8 GB (admin) + 1 GB (web), plus OS and OCR/render headroom | 100 GB SSD |

> [!TIP]
> **`mem_limit` is a ceiling, not a reservation**
>
> The 8 GB caps bound peak usage (large-document OCR page rendering, headless-Chrome rendering, concurrent jobs); steady-state is lower. Size for the ceiling so a burst does not OOM-kill a container, and keep the database on its own host for a production deployment. The base image already carries the render toolchain (headless Chrome, fonts), so the host needs only Docker.

## Prerequisites

- A Linux host (single VM), with Docker and Docker Compose v2. See [Minimum host spec](#minimum-host-spec) above for sizing.
- Registry pull credentials for the release images (token or mirrored registry).
- **PostgreSQL** (or SQL Server) reachable from the host — one database can hold both the `Core` and `UserData` schemas, or you can use two. See [Database Migrations](../getting-started/database-migrations.md); the app applies pending migrations on startup.
- **TLS certificate** for the API and monitoring services (a `cert.pfx` + password), or a TLS-terminating reverse proxy in front.
- The **credentials your chosen AI provider mode needs** — see [Application Configuration → AI provider keys](application-configuration.md#ai-provider-keys) and [Data Flows & Subprocessors](../compliance/data-flows-and-subprocessors.md).


## Secrets and configuration you supply

Nothing sensitive ships in the package — every secret is **customer-supplied** at deploy time, through the `.env` file and the per-service `appsettings.json`. The full key reference (each with a placeholder value) is the [Application Configuration Reference](application-configuration.md). The minimum you must provide:

| Concern | Where | Notes |
|---------|-------|-------|
| Database connection strings | `appsettings.json` → `ConnectionStrings` | `Core` + `UserData`; set the matching `*DbProvider`. |
| AI provider mode + credentials | `appsettings.json` / env | Pick **one** mode. Bedrock needs no key (ambient IAM); direct/Azure need keys. See [Minimal Bedrock config](application-configuration.md#minimal-bedrock-configuration). |
| JWT signing key | `appsettings.json` → `Jwt:SecretKey` | Symmetric signing secret. |
| TLS certificate + password | mounted `cert.pfx`, `CERT_PASSWORD` in `.env` | Or terminate TLS upstream. |
| OAuth / SSO (optional) | `appsettings.json` → `Authentication` | Google OAuth or any OIDC provider. |
| Image tags + ports | `.env` | `API_IMAGE` / `WEB_IMAGE` / `MONITORING_IMAGE` pinned to `x.y.z`, plus host ports. |

> [!CAUTION]
> **Placeholders only in shared config**
>
> Keep real keys, connection strings, and signing secrets out of any committed or shared file. Inject them per environment from your secrets store.
>

## TLS certificate options

The `api` and `monitoring` services are .NET (Kestrel) apps. The default production compose runs them as **HTTPS endpoints that terminate TLS themselves**, which is why `cert.pfx` + `CERT_PASSWORD` are prerequisites. You have two supported models — pick one.

### Option A — Kestrel terminates TLS (the default compose)

The default `docker-compose.yml` sets, on **both** the `api` and `monitoring` services:

```yaml
environment:
  - ASPNETCORE_URLS=https://+:443;http://+:80
  - ASPNETCORE_Kestrel__Certificates__Default__Path=/app/certs/cert.pfx
  - ASPNETCORE_Kestrel__Certificates__Default__Password=${CERT_PASSWORD}
volumes:
  - ./docker/api/certs:/app/certs          # (and ./docker/monitoring/certs for monitoring)
```

So each service expects a PKCS#12 file at the in-container path **`/app/certs/cert.pfx`**, supplied by host-mounting `docker/<service>/certs/`, with its export password in **`CERT_PASSWORD`** (from `.env`). For production, drop in a CA-issued `cert.pfx` (one covering the hostnames you serve) and set `CERT_PASSWORD` to its export password.

For a **non-production** environment you can generate a self-signed PFX with `openssl`. From the directory holding your compose stack:

```bash
# Create the cert mount directories the compose stack expects
mkdir -p docker/{monitoring,api}/{logs,settings,certs,monitoring}

# Generate a self-signed PFX for each .NET service
for service in monitoring api; do
  openssl req -x509 -newkey rsa:4096 \
    -keyout docker/$service/certs/key.pem \
    -out    docker/$service/certs/cert.pem \
    -days 365 -nodes -subj "/CN=localhost"
  openssl pkcs12 -export \
    -out   docker/$service/certs/cert.pfx \
    -inkey docker/$service/certs/key.pem \
    -in    docker/$service/certs/cert.pem \
    -passout pass:<cert-password>
done
```

Then set `CERT_PASSWORD=<cert-password>` in `.env` to the same value you passed to `-passout pass:`. Replace `/CN=localhost` with the hostname clients will use if it is not `localhost`. A self-signed cert makes browsers and `curl` warn about an untrusted issuer — fine for evaluation, not for production.

> [!WARNING]
> **Self-signed is non-prod only**
>
> A self-signed `cert.pfx` is for trials and internal testing. For a customer-facing deployment, use a certificate from a trusted CA (or terminate TLS upstream, Option B) so clients do not see trust warnings.

### Option B — Terminate TLS upstream, run Kestrel HTTP-only

If a reverse proxy or load balancer (nginx, Caddy, an ALB, the customer's existing ingress) already terminates TLS, run the .NET services **HTTP-only behind it** and skip `cert.pfx` entirely. The release ships a ready-made `docker-compose-http.yml` for exactly this: it binds the services to **HTTP only** and drops both the cert volume and the Kestrel cert env vars:

```yaml
environment:
  - ASPNETCORE_URLS=http://+:80
# no cert volume, no ASPNETCORE_Kestrel__Certificates__* — TLS is terminated upstream
```

With this compose you do **not** need `cert.pfx` or `CERT_PASSWORD`. Put your proxy/LB in front, terminate HTTPS there, and forward to the `api` (and `monitoring`) container's port 80.

> [!TIP]
> **Make the proxy forward the original scheme**
>
> When TLS is terminated upstream, configure the proxy to send `X-Forwarded-Proto: https` (and `X-Forwarded-For`) so the app builds correct absolute URLs and treats the connection as secure. Most reverse proxies set these by default. `docker-compose-http.yml` sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` on `monitoring` so the admin honors them; if the proxy reaches `monitoring` under a different host name from the one the browser uses, see [Admin behind a reverse proxy](application-configuration.md#admin-behind-a-reverse-proxy).

## Bring-up: single host (Docker Compose)

The compose stack runs the three services. The `web` container waits for `api` to report healthy (`depends_on` with `condition: service_healthy`); `monitoring` and `api` start independently. Each .NET service mounts its `appsettings.json` and, in the HTTPS compose, its `cert.pfx`; the web container is configured purely through environment variables and reverse-proxies `/api/` to the `api` service (`PROXY_BACKEND_URL`, default `http://api:80`).

1. Place the release `docker-compose.yml` and your filled-in `.env` together on the host.
2. Drop your `appsettings.json` and `cert.pfx` into the mounted `docker/api/` and `docker/monitoring/` paths.
3. Authenticate to the registry and start the stack:

```bash
docker login <registry-host>
docker compose pull
docker compose up -d
```

4. The app applies any pending database migrations at startup (watch the logs for the migration step). Verify health (below).

The `.env` selects the pinned images and host ports, for example:

```dotenv
API_IMAGE=<registry-host>/iniaai/api:<x.y.z>
WEB_IMAGE=<registry-host>/iniaai/web:<x.y.z>
MONITORING_IMAGE=<registry-host>/iniaai/monitoring:<x.y.z>

CERT_PASSWORD=<cert-password>
# Optional: MONITORING_HTTP_PORT, MONITORING_HTTPS_PORT, API_HTTP_PORT, API_HTTPS_PORT, WEB_PORT, PROXY_BACKEND_URL
```

OAuth and SSO settings do not go in `.env`: they are `Authentication:*` keys in `appsettings.json` ([Authentication and SSO](application-configuration.md#authentication-and-sso)).


## Health verification

Each service exposes `/health`. After `up -d`, confirm all three report healthy before directing users at the deployment:

| Service | Check |
|---------|-------|
| Monitoring (admin) | `GET /health` |
| API | `GET /health` |
| Web | `GET /health` (nginx) |

The compose health checks gate startup order, so if the `api` container never becomes healthy the `web` container will not start — inspect the API logs first (most commonly a database connection or AI-credential misconfiguration).


## Final step: First Login

Three healthy containers do **not** mean the deployment is usable yet — nobody is signed in, and there is no "register the first admin" screen. The deploy is only complete once you have logged in with the seeded admin account and confirmed the app loads.

On first start against an empty database the app seeds an admin account for your organization (driven by `PlatformSettings:CustomerOrgId`, with the password from `Seeding:CustomerAdminPassword`) and, when `OpenIddict:ClientSecret` is set, the service OAuth client for integrations. **[First Login](first-login.md)** is the step-by-step finish: which URL to open, the seeded identity and where its password comes from, and the smoke test that tells you the deployment is genuinely ready. Do not hand the deployment to users until that page's "you are logged in when you see…" check passes.

## Upgrades and rollback

- **Upgrade (single host):** change the three image references in `.env` (`API_IMAGE`, `WEB_IMAGE`, `MONITORING_IMAGE`) to the new `x.y.z`, then `docker compose pull && docker compose up -d`. Migrations apply automatically when the new version starts.
- **Rollback:** set the references back to the previous `x.y.z` and run the same commands. Because every release pins all three images to one version, rollback is just re-pointing the references — there are no Azure Portal revisions to manage.


> [!TIP]
> **Database changes may not roll back cleanly**
>
> Application images roll back by tag, but a release that introduced a database migration has already altered the schema. Take a database backup before an upgrade so you can restore if a rollback needs the prior schema. See [Database Migrations](../getting-started/database-migrations.md).

## Done when

- `docker compose ps` lists the three platform services as running, and the two .NET services as `healthy`.
- `GET /health` answers `200` on each of the three services.
- `GET /api/v1/meta/version` on the API reports the `x.y.z` you deployed in `versionInformation`.
- You signed in with the seeded admin account, as [First Login](first-login.md) describes.


## Related pages

- [First Login](first-login.md) — the final step: seeded admin, login URL, and the "you're in" smoke test
- [Customer-Owned Bedrock Setup](customer-bedrock-setup.md) — pointing the deployment at the customer's own Bedrock: IAM policy (Claude + Cohere embeddings), region, console model access, smoke test
- [Application Configuration Reference](application-configuration.md) — every config key with placeholder values
- [Data Flows & Subprocessors](../compliance/data-flows-and-subprocessors.md) — what content leaves the deployment and to which providers
- [Database Migrations](../getting-started/database-migrations.md) — schema applied on startup

