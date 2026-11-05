# IniaAi.Platform

The platform turns a set of documents into a written report. An analyst designs the report once, as
a workflow. For each new case, a user uploads the documents, the platform runs the workflow on them,
and the user gets a draft report to review.

- **Who it is for:** teams that read many documents to write the same kind of report again and
  again, such as a credit review of a company from its annual report.
- **What a run produces:** a draft report in the Vue app, a log of each block, and an export as a
  Word file or as Markdown. Figures link to the page that states them: a block asks a Claude model
  for citations when its setting **Get Citations From Model** is on.
- **The model accounts it needs:** an Anthropic API key. A Cohere API key is optional: it gives the
  pages the embeddings that the filter rules which pick pages by meaning need.

## Documentation

- The guides sit in `docs/` at the root of the repository: the quickstart, the overview, the
  architecture, security, how to add a block type, and what the open build holds next to the full
  edition.
- [Contributing](CONTRIBUTING.md) and [reporting a vulnerability](SECURITY.md).
- The Vue app and the agent runtime sit beside this folder, in `web/` and `agent-runtime/`, under
  the root of the repository.

## Run it with Docker

One command builds the API, the WebApp, the Vue app and the agent runtime from source and starts
them with one PostgreSQL database. It runs at the root of the repository, the folder above this
one, and the root `README.md` gives its steps. The two sections below say what the host needs and
which keys turn on more.

### What the host needs

- Docker Engine 26 or later and Docker Compose 2.23.1 or later (tested with Compose 5.1): Docker
  Desktop on macOS or Windows, or Docker Engine on Linux. The images build for amd64 and arm64, so an Apple-silicon Mac builds them
  natively.
- 2 CPU cores, 4 GB of memory for Docker and 20 GB of free disk. On an Apple-silicon Mac, a
  first start and two workflow runs, one of them an Agent block, peaked at 1 GB of memory and one
  core in total. The images take 6 GB. The build leaves up to 14 GB of build cache,
  which `docker builder prune` frees. The agent runtime may use up to 2 cores and 12 GiB during
  large Agent runs: those are its limits, not its need.
- On Ubuntu 24.04 or later, one host setting. Ubuntu restricts unprivileged user namespaces, and
  the agent runtime cannot build its sandbox under that restriction:

  ```bash
  sudo sysctl -w kernel.apparmor_restrict_unprivileged_userns=0
  # keep it across reboots:
  echo 'kernel.apparmor_restrict_unprivileged_userns=0' | sudo tee /etc/sysctl.d/60-agent-runtime.conf
  ```

  Without it every Agent block fails with `sandbox_unavailable`. Docker Desktop and hosts without
  AppArmor need nothing.
- On Ubuntu 26.04, also unload the AppArmor profile `bwrap-userns-restrict`. Ubuntu 26.04 loads
  it, the setting above does not reach it, and it takes every capability from the runtime's
  sandbox:

  ```bash
  sudo apparmor_parser -R /etc/apparmor.d/bwrap-userns-restrict
  # keep it unloaded across reboots:
  sudo ln -s /etc/apparmor.d/bwrap-userns-restrict /etc/apparmor.d/disable/
  ```

  Without it every Agent block fails with `sandbox_unavailable` and `bwrap_apparmor_profile_loaded`.

### Optional keys

Add a key to `.env` at the root of the repository to turn on what it serves, then run
`docker compose up --wait --force-recreate` there.

| Value | What it adds |
|---|---|
| `COHERE_API_KEY` | Embeddings of the page overviews, for the AI blocks that pick a document's pages by meaning. Without it the pages keep no embeddings, and such a block says that it needs a Cohere API key: an AI Query block is skipped, and a Retrieve Data or Build Topics block fails. Every other block runs. |
| `EXA_API_KEY` | The Exa web search block. |
| `EXCHANGE_RATE_API_KEY` | The Exchange Rate block. |

`.env.example`, at the root of the repository, also names the ports and the version that the Vue
app shows. How the stack is built is in `docs/docker.md`, at the root of the repository.

## Prerequisites to work on the source

- The .NET 10 SDK: [download](https://dotnet.microsoft.com/download).
- To run the apps from source: PostgreSQL on `localhost:5432` and an Anthropic API key.

## Build

From this folder, `platform/`:

```bash
dotnet build IniaAi.Platform.CI.slnf
```

## Run the tests

```bash
dotnet test --solution IniaAi.Platform.CI.slnf
```

This is the command CI runs. It needs no cloud account, no API key and no database. A test that
needs something you have not set skips, and its skip reason names what to set.

The project uses **xUnit v3** with **Microsoft Testing Platform (MTP)**, configured in
`global.json`. Run one project with `--project`, and pass a filter after `--`:

```bash
dotnet test --project IniaAi.Core.Tests/IniaAi.Core.Tests.csproj -- --filter-class "*ExchangeRate*"
```

### Tests that need something more

The tests that call an outside API spend money, so they run only with `INIA_TEST_PAID_APIS=1`,
and a key alone never starts them. They read their keys from `PLATFORMSETTINGS__ANTHROPIC__APIKEY`,
`PLATFORMSETTINGS__COHERE__APIKEY`, `PLATFORMSETTINGS__EXA__APIKEY`,
`PLATFORMSETTINGS__EXCHANGERATEAPI__APIKEY` and `PERPLEXITY_API_KEY`. The AWS Bedrock tests also
need AWS credentials with Bedrock access.

These variables turn on the tests that need a local service:

| Variable | What it turns on |
|---|---|
| `INIA_TEST_PG` | The tests that apply migrations to a real PostgreSQL server, for example `Host=localhost;Port=5432;Username=postgres;Password=<password>;Database=postgres`. Each test creates and drops its own database. |
| `INIA_TEST_MSSQL` | The same tests on SQL Server, for example `Server=localhost,1433;User Id=sa;Password=<password>;TrustServerCertificate=True`. |
| `INIA_TEST_AGENT_RUNTIME` | The tests that run an Agent block on a live agent runtime: the runtime's base URL. They also need `INIA_TEST_PAID_APIS=1`. |

### Integration tests

`IniaAi.WebApi.IntegrationTests` starts the WebApi in the test process, so it needs no running
app. It needs a reachable PostgreSQL and the settings file `IniaAi.WebApi/appsettings.Tests.json`,
which git ignores. CI runs `IniaAi.Platform.CI.slnf`, which leaves the project out. To run it:

```bash
createdb -h localhost -U postgres iniaai-db-test
cp IniaAi.WebApi/appsettings.Tests.example.json IniaAi.WebApi/appsettings.Tests.json
```

Put the connection string of the test database in each of its four placeholders:
`UserDataDbConnection`, `CoreDbConnection`, `AdminUserDataDbConnection` and
`AdminCoreDbConnection`. The test host generates its own signing key, client secret and first
passwords. Then run:

```bash
dotnet test --project IniaAi.WebApi.IntegrationTests/IniaAi.WebApi.IntegrationTests.csproj
```

## Run the apps from source

The platform has two apps: the WebApi, which also runs the workflows, and the WebApp, the admin
app with the workflow editor. They share one database.

1. Create the database and trust the .NET development certificate:

   ```bash
   createdb -h localhost -U postgres iniaai-db-local
   dotnet dev-certs https --trust
   ```

2. Each app reads its launch profiles from a git-ignored `Properties/launchSettings.json`. Create
   both from the templates:

   ```bash
   cp IniaAi.WebApi/Properties/launchSettings.example.json IniaAi.WebApi/Properties/launchSettings.json
   cp IniaAi.WebApp/Properties/launchSettings.example.json IniaAi.WebApp/Properties/launchSettings.json
   ```

3. In the `API Postgres` profile of the WebApi file and the `RAZOR Postgres` profile of the
   WebApp file, replace each placeholder:

   - `YOUR_POSTGRES_CONNECTION_STRING_HERE`: the database, for example
     `Server=localhost;Port=5432;Database=iniaai-db-local;User Id=postgres;Password=<password>`.
   - `YOUR_ANTHROPIC_API_KEY_HERE`: your Anthropic API key.
   - `YOUR_FIRST_SUPERADMIN_PASSWORD_HERE` and `YOUR_FIRST_CUSTOMER_ADMIN_PASSWORD_HERE`: the
     passwords that the first accounts get when the app creates the database. Each needs at least
     8 characters, with an uppercase letter, a lowercase letter and a digit.
   - In the WebApi file only, `YOUR_SIGNING_KEY_HERE`: a key of at least 32 bytes, such as the
     output of `openssl rand -base64 48`, and `YOUR_SERVICE_CLIENT_SECRET_HERE`: the secret of
     the service clients that call the API.

4. Start each app from this folder, and name the profile:

   ```bash
   dotnet run --project IniaAi.WebApi --launch-profile "API Postgres"     # https://localhost:7557/swagger
   dotnet run --project IniaAi.WebApp --launch-profile "RAZOR Postgres"   # https://localhost:7119
   ```

   The first start creates the database schema and the first accounts.

5. Open https://localhost:7119 and sign in as `superadmin@inia.ai` with the password you gave
   `Seeding__SuperAdminPassword`.

For VS Code, copy `.vscode/launch.example.json` to `.vscode/launch.json` as is.

### Optional keys from source

Add a key to the `environmentVariables` of both profiles to turn on what it serves.

| Setting | What it adds |
|---|---|
| `PlatformSettings__Cohere__ApiKey` | Embeddings of the page overviews, for the AI blocks that pick a document's pages by meaning. Without it the pages keep no embeddings, and such a block says that it needs a Cohere API key: an AI Query block is skipped, and a Retrieve Data or Build Topics block fails. Every other block runs. |
| `PlatformSettings__Exa__ApiKey` | The Exa web search block. |
| `PlatformSettings__ExchangeRateApi__ApiKey` | The Exchange Rate block. |

The Agent block runs on the agent runtime, a separate service. The profiles expect it on
`http://localhost:8130`, with the models in `PlatformSettings__AgentRuntime__AllowedModels__*`.
Without a runtime there, the WebApi logs a refused connection now and then, and every other block
runs. To turn the Agent block off, set `PlatformSettings__AgentRuntime__Enabled` to `false`.

## Licence and third-party notices

The code is licensed under the Apache License 2.0, in [LICENSE](LICENSE). `NOTICE` names the copyright
holder. `THIRD-PARTY-NOTICES.md` names every file and package in the build that other people
wrote, with its licence. To report a vulnerability, follow `SECURITY.md`.

When you add, remove or update a package, write the package list again and commit it with the
change. The script needs Python 3.11 or later:

```bash
dotnet restore IniaAi.Platform.slnx
python3 scripts/notices/notices.py write
```

CI runs `python3 scripts/notices/notices.py check`. It fails when the list is out of date, or when
a package's licence is outside `scripts/notices/policy.json`, and its message says what to change.
The script's own description explains the policy.
