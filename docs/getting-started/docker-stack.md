---
type: reference
status: current
description: "Describes the Docker build of the open repository and its one-command stack: the images and their recipes, the services of compose.yaml, how the secrets reach each service, the agent runtime's sandbox settings, and the compose check. Read when you change the Docker build or the stack, a service of the stack does not start, or you need to know where a secret lives."
---

# The Docker Stack

**On this page:** The images · The stack · The compose check

`docker compose up --build --wait` builds every service from source and starts it. [Quickstart](quickstart.md) says
how to run it. This page says how it is built.

## The images

| Image | Recipe | Build context |
|---|---|---|
| API | `platform/docker/Dockerfile`, target `api` | `platform/` |
| WebApp | `platform/docker/Dockerfile`, target `webapp` | `platform/` |
| Vue app | `Dockerfile` in the Vue app's folder, `web/` | that folder |
| Agent runtime | `Dockerfile` in the runtime's folder, `agent-runtime/`, target `prod` | that folder |

Each recipe builds with its folder as the only context. The open build needs no registry
sign-in, no named build context and no build secret, and every `FROM` names a public image.

`platform/docker/Dockerfile` has three parts:

- `base`: the ASP.NET runtime image with Google Chrome, chromedriver and the fonts that documents
  name. The chart export and the link download drive Chrome. The stage installs the Chrome and the
  chromedriver build for the target CPU, amd64 or arm64.
- `build`: the .NET SDK. It runs on the build machine's own CPU and publishes for the target CPU,
  so a cross-platform build emulates no compiler.
- `api` and `webapp`: the publish output on `base`. A pipeline that keeps a prebuilt base passes
  it as the build argument `BASE_IMAGE`. Both set `CHROMEDRIVER_PATH`, so Chrome starts with the
  installed chromedriver. Without it, Selenium runs Selenium Manager on each start: that tool
  calls the internet, sends usage statistics, and has no arm64 Linux build.

To build one image:

```bash
docker build -f docker/Dockerfile --target api -t inia-api .
docker buildx build --platform linux/amd64 -f docker/Dockerfile --target webapp -t inia-webapp .
```

## The stack

`compose.yaml`, at the root of the repository, runs these services:

| Service | What it does | Host port |
|---|---|---|
| `secrets` | Makes the stack's secrets on the first start, then exits | none |
| `postgres` | PostgreSQL 17, one database for the Core and UserData schemas | none |
| `webapp` | The WebApp. It and the API migrate the database on start, one at a time | `127.0.0.1:8081` |
| `api` | The API and the workflow runner | none |
| `sample` | Loads each sample workflow of `platform/samples/` on the first start, then exits. Later starts leave a loaded sample as it is, edited or deleted. A failed load leaves the platform running without that sample, and the next start tries again | none |
| `web` | The Vue app. Its nginx proxies `/api/` and `/hubs/` to the API. It starts after `sample` completes | `127.0.0.1:8080` |
| `agent-runtime` | Runs the Agent block on your Anthropic key | none |

Every .NET service runs with `ASPNETCORE_ENVIRONMENT=Production` and `FORCE_AWS_MODE=false`. The
stack seeds one organization, so the first start creates one account: `superadmin@example.com`, or the address in `INIA_ADMIN_EMAIL`.

The apps show the release number in `INIA_VERSION`: compose builds it into the platform images
and the Vue app. Each release of this repository fills it in `.env.example`. A stack built from
the platform source leaves it unset, and its apps show `0.0.0.0`.

### How the secrets reach the services

No secret reaches a service as an environment variable, so `docker inspect` shows none.

- The values you give (`ANTHROPIC_API_KEY`, `INIA_ADMIN_PASSWORD` and the optional keys) are
  Compose configs, which reach each service as files under `/run/settings/`, readable by root
  only. Each file is named after the setting it sets, such as `PlatformSettings__Anthropic__ApiKey`.
  An optional key that is not set holds a newline, which the apps read as an empty value: Compose
  mounts the project folder in place of a config with empty content.
- Compose does not recreate a service when a config's content changes. After a change in `.env`,
  `docker compose up --wait --force-recreate` gives every service the new values.
- The `secrets` service makes the PostgreSQL password, the JWT signing key, the OAuth client
  secret and the runtime's token once, and keeps them in the `secrets` volume. Each service mounts
  only its own folder of that volume. PostgreSQL takes its password only when it creates its data,
  so the secrets live and die with the data: `docker compose down -v` deletes both.
- The API and the WebApp read every file in the folders that `SETTINGS_DIRECTORIES` names as a
  setting: the file name is the key, with `__` between sections, and the content is the value.
  Environment variables still override them.
- The runtime's file system is read-only, so Compose cannot mount a config into it. The `secrets`
  service copies the Anthropic key into the runtime's folder of the volume on every start. The
  runtime reads `AUTH_TOKEN` and `ANTHROPIC_API_KEY` from that folder, which its own
  `SETTINGS_DIRECTORIES` setting names.

### The agent runtime

The runtime refuses to start without its sandbox settings: a seccomp profile, `systempaths` and
`apparmor` unconfined, a pids limit of 4352, a memory limit of 12 GiB and an executable `/tmp`.
Docker reads the seccomp profile from a path on the host when it creates the container, so the
compose file names the runtime's own profile, `agent-runtime/sandbox/seccomp-bwrap.json`.

The API and the runtime allow the same Anthropic models. The runtime's `ANTHROPIC_MODEL` is the
default for an Agent block that names no model.

### Sources

The compose file sits at the root of the repository. It builds the platform from `platform/`,
the Vue app from `web/` and the runtime from `agent-runtime/`, so one clone holds everything the
build needs.

## The compose check

`platform/docker/check-compose.sh` reads the configuration that Docker Compose resolves. From the root of the
repository, run `platform/docker/check-compose.sh compose.yaml`. It fails when the stack breaks one
of these rules:

- Every .NET service runs in Production, not in AWS mode.
- No image comes from a private registry.
- No service gets an OAuth client id or secret.
- No secret reaches a service as an environment variable or a build argument.
- Every config has content, and every service reads its configs with mode 0400.
- No service shares the host network.
- PostgreSQL and the runtime publish no host port.
- Every published port binds to 127.0.0.1.
- The API and the runtime allow the same models.
- A service with no restart policy runs once, so another service waits for it to complete.
  Without that wait, `docker compose up --wait` takes its exit as a failure.
- `.env.example` sets no environment and no cloud mode.

CI runs the check on every pull request, and builds the API and WebApp images.
