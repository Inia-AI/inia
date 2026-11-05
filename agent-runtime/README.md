# agent-runtime

A container that runs Claude agent jobs safely. Built on the Claude Agent SDK (TypeScript, on Bun),
it takes a prompt and optional documents over an HTTP API, runs the agent in a sandbox under a
spend fuse, a turn fuse and a deadline, and hands back the answer, the files the run produced and a
trace of what it did. It was built as the agent runtime of the Inia platform, and it runs on its own.

A run is an agent query over a deliberately narrow tool surface: `Bash` inside bubblewrap with no
network, a `Read` that serves only the run's own staged documents (rendered PDF pages included),
`Skill` when the caller ships skill files, and the caller's MCP tools when the caller names a
server. Runs can pause for an answer and resume.

## What you need

- **Docker** with BuildKit and Compose v2: Docker Desktop on macOS or Windows, or Docker Engine on
  Linux. Give Docker at least 8 GB of memory and 10 GB of disk: the dev service is capped at 6 GB
  and the image is about 3 GB.
- **An Anthropic API key**, or one of the other model routes under [Configure](#configure).
- **On Ubuntu 24.04 or later**, one host setting. Ubuntu restricts unprivileged user namespaces, and
  bubblewrap cannot build its sandbox under that restriction:

  ```bash
  sudo sysctl -w kernel.apparmor_restrict_unprivileged_userns=0
  # keep it across reboots:
  echo 'kernel.apparmor_restrict_unprivileged_userns=0' | sudo tee /etc/sysctl.d/60-agent-runtime.conf
  ```

  Without it the runtime starts, and every run fails before its first model call with the subtype
  `sandbox_unavailable` and a message that names the setting:
  `sandbox unavailable — the pre-run bwrap probe failed: unprivileged_userns_restricted — the host sysctl kernel.apparmor_restrict_unprivileged_userns=0: …`.
  Docker Desktop and hosts without AppArmor need nothing.
- **On Ubuntu 26.04**, also unload the AppArmor profile `bwrap-userns-restrict`. Ubuntu 26.04 loads
  it, the setting above does not reach it, and it takes every capability from what bubblewrap
  starts:

  ```bash
  sudo apparmor_parser -R /etc/apparmor.d/bwrap-userns-restrict
  # keep it unloaded across reboots:
  sudo ln -s /etc/apparmor.d/bwrap-userns-restrict /etc/apparmor.d/disable/
  ```

  Without it every run fails the same way, with `bwrap_apparmor_profile_loaded` in the message.

## Quickstart: from clone to a finished run

In the folder that holds this README:

```bash
export ANTHROPIC_API_KEY=sk-ant-...            # your own key
docker compose up --build agent-anthropic      # first build compiles the toolbelt image: a few minutes
```

The service listens on `127.0.0.1:8130` once `docker compose ps` shows it healthy. Stop it with
Ctrl-C, or with `docker compose --profile anthropic down`: a plain `docker compose down` skips a
service whose profile is not active. In a second shell, start a run and poll it until its `status`
is terminal:

```bash
curl -s -X POST localhost:8130/v1/runs -H 'content-type: application/json' \
  -d '{"executionId":"hello-1","prompt":"Use Bash to print the kernel version, then reply with it."}'
curl -s localhost:8130/v1/runs/hello-1          # repeat until status is succeeded, failed, cancelled or expired
curl -s -X POST localhost:8130/v1/runs/hello-1/cancel   # best-effort cancel, if you need it
```

The default model is `claude-haiku-4-5`, and a run may pick `claude-sonnet-5-5` or
`claude-opus-5-5` with a `"model"` field. Edit the `agent-anthropic` service in `compose.yaml` to
change either.

## Configure

The runtime reads its settings from environment variables, and it refuses to start on a missing or
contradictory one, naming the variable.

**Model route.** `AGENT_RUNTIME_MODEL_PROVIDER` picks one of three, and each refuses another route's
variables:

| Mode | You provide | Notes |
|---|---|---|
| `anthropic` | `ANTHROPIC_API_KEY`, `ANTHROPIC_MODEL` | The key stays in a separate loopback process; a run never holds it |
| `bedrock` (the default when unset) | `CLAUDE_CODE_USE_BEDROCK=1`, `ANTHROPIC_MODEL`, an AWS identity | Model ids are Bedrock inference-profile ids |
| `foundry` | `CLAUDE_CODE_USE_FOUNDRY=1`, a Foundry resource, `ANTHROPIC_MODEL` | Authenticates as the VM's Azure managed identity |



**Models.** `ANTHROPIC_MODEL` pins the default model. `ANTHROPIC_ALLOWED_MODELS` (comma-separated,
must include the default) lists what a request's `model` may choose; anything else answers `400`.

**Auth.** Set `AUTH_TOKEN`, and every API call must carry `Authorization: Bearer <token>`. The
runtime refuses to start without it unless `AUTH_DISABLED=true` says so explicitly. The compose
services set `AUTH_DISABLED=true` and publish the port on loopback only. Never expose them as they
are.

**Run limits.** A request may tighten its own spend, turn and time fuses (`maxBudgetUsd`,
`maxTurns`, `deadlineMs`) below the code-owned ceilings, never above them; `GET /v1/limits` lists the
defaults and ceilings. `AGENT_RUNTIME_MAX_BUDGET_USD`, `AGENT_RUNTIME_MAX_TURNS` and
`AGENT_RUNTIME_RUN_DEADLINE_MS` lower the defaults for runs that declare none.

**System prompt.** Every run's system prompt opens with the runtime's fixed environment section,
wrapped in `<environment>` tags: the sandbox, the workspace layout, the `Read` rules, where output and scratch files go,
how to invoke a skill, and the installed tools. A request's `systemPrompt` follows it; without one
the section is the whole system prompt. `GET /v1/environment` returns the exact text and the
runtime version.

## Security model

- **What a run can reach.** Its `Bash` runs inside bubblewrap with no network at all, its own
  per-run workspace, and a private `/tmp`; every other run's files and the runtime's own are hidden.
  Its `Read` serves only the documents staged for that run, from a private copy checked against the
  staged digest. Each run runs as its own uid with every capability dropped, and a pre-run probe
  fails the run, at no cost, when the sandbox cannot start. Model calls happen outside the sandbox,
  in the run's SDK process, and that process holds no model credential in the `anthropic` mode.
- **What the container relaxes, and why.** bubblewrap builds namespaces and mounts, which Docker's
  defaults forbid. The compose services therefore carry a custom seccomp profile
  (`sandbox/seccomp-bwrap.json`, Docker's default with the namespace syscalls allowed),
  `systempaths=unconfined` (bubblewrap mounts a fresh `/proc`) and `apparmor=unconfined`
  (Docker's AppArmor profile forbids every mount). They drop every capability except `SETUID` and
  `SETGID` for the supervisor (it starts each run as its own uid) and `KILL` for the init process,
  and set `no-new-privileges`. [sandbox/README.md](sandbox/README.md) has each control, the error
  you see without it, and why none can stand in for another.
- **The dev compose file is for one person on one machine.** Auth is off and the port is bound to
  `127.0.0.1`. A deployment sets `AUTH_TOKEN` and keeps the sandbox controls above.

## Quality gates

Everything builds and runs in the container; nothing runs on the host.
Before a pull request:

```bash
docker compose run --rm agent bun run check   # typecheck, lint and tests, all three always
./scripts/check-docs.sh                       # docs frontmatter, links and cited source paths
docker build --target prod .                  # the shippable image builds
```

`bun run check` needs no model access and no credential: the contract runs drive the real CLI
against local stubs. After a change to `tsconfig.json`, `biome.json` or `package.json`, run
`docker compose build` first, because those files are baked into the image.

## Running on Bedrock

The `agent` service runs in `bedrock` mode. `./scripts/dev.sh up --build` exports short-lived
credentials from your AWS profile (`aws configure export-credentials`) to an `imds-mock` sidecar,
so the agent container itself holds no AWS credential. It needs the AWS CLI and a profile with
session credentials, such as an SSO login.

- **Runs fail with auth errors after hours of working**: the session expired, and the sidecar keeps
  serving the credentials it started with (`./scripts/dev.sh` warns when under 15 minutes remain).
  Log in again, then rerun `./scripts/dev.sh up`; `docker compose logs imds-mock` shows what it
  serves.
- **`dev.sh` refuses: "no session token"**: the profile exports static keys, and the sidecar serves
  session credentials only. Use a profile that yields session credentials.

## Troubleshooting

- **Every run fails `sandbox_unavailable`**: the message names the control at fault. See
  [What you need](#what-you-need) for the Ubuntu setting, and [sandbox/README.md](sandbox/README.md)
  for the others.
- **`docker: unknown command: docker buildx` or `docker compose`**: Homebrew's `docker` formula
  ships the bare CLI. Install `docker-buildx` and `docker-compose` with Homebrew, then add
  `"cliPluginsExtraDirs": ["<brew prefix>/lib/docker/cli-plugins"]` to `~/.docker/config.json`
  (`brew --prefix` prints the prefix). Docker Desktop bundles both.
- **The runtime refuses to start**: its last log line is `configuration_error` and names the
  variable.
- **Port 8130 in use**: `lsof -i :8130`.

## Licence

The code in this repository is licensed under the Apache License 2.0, in [`LICENSE`](LICENSE).
[`NOTICE`](NOTICE) names the copyright holder, and
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) names every file and package that other people
wrote, with its licence. To report a vulnerability, follow [`SECURITY.md`](SECURITY.md).

`@anthropic-ai/claude-agent-sdk` and its platform packages, `@anthropic-ai/claude-agent-sdk-<platform>`,
which carry the `claude` binary, are Anthropic's proprietary software under Anthropic's terms, at
<https://code.claude.com/docs/en/legal-and-compliance>. The licence of this repository does not
cover them. This repository does not contain them: `bun install` fetches them from npm when you
build. Inia publishes no image that contains them. You build the image from this source, as the
Quickstart shows, and the image you build contains the SDK under Anthropic's terms.

When you add, remove or update a package, write the package list again with `python3
scripts/notices/notices.py write` (Python 3.11 or later), and commit it with the change. It reads
the licences of the bun and uv packages from the npm registry and PyPI. CI runs `python3
scripts/notices/notices.py check`. It fails when the list is out of date, or when a package's
licence is outside `scripts/notices/policy.json`, and its message says what to change. The script's
own description explains the policy. A new SDK version needs the version of its exception raised in
the same pull request.

