# Inia AI

Inia AI turns a set of documents into a written report. An analyst designs the report once, as a
workflow. For each new case, a user uploads the documents, the platform runs the workflow on them,
and the user gets a draft report whose figures link to the pages that state them.

## What this repository holds

| Folder | What it holds |
|---|---|
| [platform/](platform/README.md) | The API, the admin app with the workflow editor, the sample workflows, and the Docker build of both apps. |
| [web/](web/README.md) | The Vue app, where users upload documents, run workflows and read reports. |
| [agent-runtime/](agent-runtime/README.md) | The service that runs the Agent block in a sandbox. |

Each release adds one commit and one tag. The platform and the Vue app share the release version.
The agent runtime keeps its own version, and the release notes name it.

## Run it with Docker

One command builds the three parts from their folders and starts them with one PostgreSQL database.
You need Docker and an Anthropic API key. The first start makes every other secret.

1. In the root folder of this repository, copy the settings template:

   ```bash
   cp .env.example .env
   ```

2. In `.env`, set the two required values:

   - `ANTHROPIC_API_KEY`: your Anthropic API key. Every model call uses it.
   - `INIA_ADMIN_PASSWORD`: the password of the first admin, `superadmin@example.com` unless you set `INIA_ADMIN_EMAIL`. It needs at least
     8 characters, with an uppercase letter, a lowercase letter and a digit.

3. Build and start the stack:

   ```bash
   docker compose up --build --wait
   ```

   The command returns when every service reports healthy. If a value is missing, the command stops
   before any service starts and names the value.

4. Open the apps and sign in as `superadmin@example.com` with the password you set:

   | App | Address |
   |---|---|
   | The Vue app, where users upload documents and run workflows | http://localhost:8080 |
   | The admin app, with the workflow editor | http://localhost:8081 |

The stack serves plain HTTP on 127.0.0.1, for use on one machine. Before other machines reach it,
put a TLS proxy in front of it, and set `INIA_BIND_ADDRESS` in `.env`.

- [What the host needs](platform/README.md#what-the-host-needs): Docker versions, memory, disk and
  the settings an Ubuntu host needs for the Agent block.
- [Optional keys](platform/README.md#optional-keys): the keys that turn on embeddings, web search
  and exchange rates.
- [The Docker build and the one-command stack](docs/getting-started/docker-stack.md): the images,
  the services and how secrets reach them.

## Documentation

- [Quickstart](docs/getting-started/quickstart.md): from a clone to a report with cited figures.
- [All the docs](docs/index.md): the overview, the architecture, security, and how to add a block
  type. [docs/llms.txt](docs/llms.txt) lists every page with what it holds and when to read it.
- [The Vue app](web/README.md) and [the agent runtime](agent-runtime/README.md).
- [Contributing](CONTRIBUTING.md), the [code of conduct](CODE_OF_CONDUCT.md) and
  [reporting a vulnerability](SECURITY.md).

## Licence

The Apache License 2.0, in [LICENSE](LICENSE). [NOTICE](NOTICE) names the copyright holder. Each
folder carries its own `NOTICE` and `THIRD-PARTY-NOTICES.md`, which list the files and packages
that others wrote.
