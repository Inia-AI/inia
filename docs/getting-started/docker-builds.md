---
type: guide
status: current
description: "Walks through the one command that builds and starts the whole product, building the platform's three Docker images from their recipes, running one, the corporate proxy settings chart rendering needs, and reading the logs. Read when you build or run a platform image on your own machine, or an image build or a container start fails."
---

# Docker Builds

**On this page:** Prerequisites · The One-Command Stack · Building the Docker Image · Running the Docker Image · Accessing the Application · Corporate Proxy / Zscaler Configuration · Troubleshooting · Done when

This guide covers building, running, and troubleshooting Docker images for the IniaAi Platform.

## Prerequisites

### Docker

You need Docker Engine 26 or later with Docker Compose v2: Docker Desktop on macOS or Windows, or Docker Engine on Linux. If you don't have it, [download Docker Desktop](https://www.docker.com/products/docker-desktop). The images build for amd64 and arm64, so an Apple-silicon Mac builds them natively.


## The One-Command Stack

The compose file builds the API, the admin app, the Vue app and the agent runtime from source and starts them with one PostgreSQL database. It runs at the root of the repository and builds from the folders `platform/`, `web/` and `agent-runtime/`.


At that root, set `ANTHROPIC_API_KEY` and `INIA_ADMIN_PASSWORD` in `.env` (copy `.env.example`), then run:

```bash
docker compose up --build --wait
```

The command returns when every service reports healthy. After you change a value in `.env`, run `docker compose up --wait --force-recreate`: Compose does not restart a service for a changed value. The Vue app answers on `http://localhost:8080` and the admin app on `http://localhost:8081`. Sign in as `superadmin@example.com`, or as the address in `INIA_ADMIN_EMAIL`, with the password in `INIA_ADMIN_PASSWORD`. The platform's [README.md](../../platform/README.md) lists the host requirements and the optional keys, and [The Docker Stack](docker-stack.md) says how the stack makes and keeps its secrets.


## Building the Docker Image

Each recipe lives with the code it builds and takes its repository as the only build context:

| Recipe | Image | Builds |
|---|---|---|
| platform `docker/Dockerfile`, target `api` | `iniaai/api` | `IniaAi.WebApi`, the REST API |
| platform `docker/Dockerfile`, target `webapp` | `iniaai/monitoring` | `IniaAi.WebApp`, the Blazor admin and workflow editor app (the "admin" component, deployed under the registry name `monitoring`) |
| platform `docker/Dockerfile`, target `base` | `iniaai/base-image` | the ASP.NET runtime with Google Chrome, chromedriver and the document fonts, which the api and monitoring images run on |
| frontend `Dockerfile` | `iniaai/web` | the Vue SPA behind nginx; see [Build and deploy](../architecture/frontend/build-and-deploy.md) |

Docker Desktop must be running. Build the platform images from the platform repository root, and the web image from the frontend repository root:

```bash
# API image (IniaAi.WebApi)
docker build --target api -t iniaai/api:latest -f docker/Dockerfile .

# Monitoring image (IniaAi.WebApp)
docker build --target webapp -t iniaai/monitoring:latest -f docker/Dockerfile .

# Web image (the Vue SPA), from the frontend repository
docker build --build-arg VERSION=1.2.3 -t iniaai/web:latest .
```

| Flag | Purpose |
|------|---------|
| `--target api` / `webapp` / `base` | Picks the image the platform recipe builds |
| `--build-arg BASE_IMAGE=…` | Runs the api or monitoring image on a prebuilt base. Without it the build makes the base from the `base` stage |
| `--build-arg VERSION=…` | The web image's version line at the foot of the layout. Without it the line is absent |
| `--platform linux/amd64` | Builds for another CPU. The .NET SDK and the Vue build run on the build machine's own CPU, so only the steps that install system packages run under emulation |
| `-t iniaai/{image}:latest` | Tags the image |


### Native Assets and Runtime Trimming

`IniaAi.FileFormats` pulls in platform-specific native libraries — SkiaSharp (`SkiaSharp.NativeAssets.Linux.NoDependencies`, `.macOS`, `.Win32`) for image work, and PDFium (`bblanchon.PDFium.Linux`, `.macOS`, `.Win32`), which the PDF worker process loads. Cross-platform builds otherwise carry native assets for every RID.


When the platform recipe runs `dotnet publish`, it names the target CPU (`-a amd64` or `-a arm64`, which the SDK turns into the runtime `linux-x64` or `linux-arm64`) so only the Linux native assets ship. This keeps the image from bloating with macOS/Windows native binaries.

## Running the Docker Image

### Certificate Setup

You need a certificate in a `certs` folder. To create a self-signed certificate:

```bash
dotnet dev-certs https -ep certs/cert.pfx -p YOUR_CERTIFICATE_PASSWORD
```

### Run Commands

**bash:**

```bash
docker run \
  --env=ASPNETCORE_URLS='https://+:443;http://+:80' \
  --env=ASPNETCORE_Kestrel__Certificates__Default__Path=/app/certs/cert.pfx \
  --env=ASPNETCORE_Kestrel__Certificates__Default__Password=YOUR_CERTIFICATE_PASSWORD \
  --volume='/path/to/docker/logs':'/app/logs:rw' \
  --volume='/path/to/docker/certs':'/app/certs:rw' \
  --volume='/path/to/docker/settings/appsettings.json':'/app/appsettings.json:rw' \
  -p 80:80 \
  -p 443:443 \
  --restart=on-failure:0 \
  -d \
  iniaai/api:latest
```

**PowerShell:**

```powershell
docker run `
  --env=ASPNETCORE_URLS='https://+:443;http://+:80' `
  --env=ASPNETCORE_Kestrel__Certificates__Default__Path=/app/certs/cert.pfx `
  --env=ASPNETCORE_Kestrel__Certificates__Default__Password=YOUR_CERTIFICATE_PASSWORD `
  --volume='C:\path\to\docker\logs':'/app/logs:rw' `
  --volume='C:\path\to\docker\certs':'/app/certs:rw' `
  --volume='C:\path\to\docker\settings\appsettings.json':'/app/appsettings.json:rw' `
  -p 80:80 `
  -p 443:443 `
  --restart=on-failure:0 `
  -d `
  iniaai/api:latest
```

**Command Prompt:**

```cmd
docker run ^
  --env=ASPNETCORE_URLS='https://+:443;http://+:80' ^
  --env=ASPNETCORE_Kestrel__Certificates__Default__Path=/app/certs/cert.pfx ^
  --env=ASPNETCORE_Kestrel__Certificates__Default__Password=YOUR_CERTIFICATE_PASSWORD ^
  --volume='C:\path\to\docker\logs':'/app/logs:rw' ^
  --volume='C:\path\to\docker\certs':'/app/certs:rw' ^
  --volume='C:\path\to\docker\settings\appsettings.json':'/app/appsettings.json:rw' ^
  -p 80:80 ^
  -p 443:443 ^
  --restart=on-failure:0 ^
  -d ^
  iniaai/api:latest
```

> [!TIP]
> Replace the volume paths with the actual paths on your machine.

### Stopping the Container

```bash
docker stop $(docker ps -a -q --filter ancestor=iniaai/api:latest)
```


## Accessing the Application

Open a browser and navigate to `https://localhost`. You will see a warning that the connection is not secure. Click **Advanced** and then **Proceed to localhost (unsafe)**.

## Corporate Proxy / Zscaler Configuration

When deploying behind a corporate proxy (e.g., Zscaler), chart rendering requires specific environment configuration.

### NO_PROXY for Selenium

The Selenium ChromeDriver communicates with its Chromium process via `localhost`. If a corporate proxy intercepts `localhost` connections, the driver health check fails with `Cannot start the driver service`.

**Fix**: Set `NO_PROXY` to exclude localhost:

```bash
docker run \
  --env=NO_PROXY=localhost,127.0.0.1 \
  # ... other flags ...
  iniaai/api:latest
```

Or in Docker Compose / Helm values:

```yaml
environment:
  NO_PROXY: "localhost,127.0.0.1"
```

> [!WARNING]
> **Helm --set gotcha**
>
> Multiple `--set` calls for the same key overwrite each other — only the last value survives. Use comma-separated values (escaped with `\,`) or put them in a values file.

### Chart Rendering

The chart bundle, with ECharts inside, is inlined from an embedded resource directly into the chart HTML. This means chart rendering makes **zero network requests** — no CDN or server URLs are needed, and proxies cannot throttle it.

See [Chart Rendering Architecture](../architecture/chart-rendering/index.md) for details.

## Troubleshooting

If you have mapped the logs volume and your `appsettings.json` configures the Serilog logger to write to a file, you can check the logs in the `logs` folder.

Example Serilog configuration:

```json
{
  "Serilog": {
    "Using": ["Serilog.Sinks.Console", "Serilog.Sinks.File"],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/log-.log",
          "rollingInterval": "Day"
        }
      }
    ],
    "Enrich": ["FromLogContext", "WithMachineName", "WithThreadId"]
  }
}
```

With this configuration, Serilog writes logs to the `logs` folder in the application root and to the console. To check console logs, use the `docker logs` command or view them in Docker Desktop.

Each image carries a `HEALTHCHECK` that calls `/health` on port 80 every 30 seconds; `docker ps` shows `unhealthy` when it fails.

## Done when

- With the one-command stack: `docker compose up --build --wait` returns, `docker compose ps` shows every service `healthy`, and the first admin signs in on `http://localhost:8080` and `http://localhost:8081`.
- `docker images` lists the image you built under its tag.
- The container you started shows `healthy` in `docker ps`, and its address answers: `https://localhost` for the `docker run` above.

