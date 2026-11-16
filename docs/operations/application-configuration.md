---
type: reference
status: current
description: "Reference of the runtime configuration keys of the API and admin apps: how the sources layer, connection strings, PlatformSettings and its subsections, authentication, JWT, file storage, email, data protection, logging and CORS, each with a placeholder value. Read when you configure a deployment, look up what a key does or its default, or a startup error names a configuration key."
---

# Application Configuration Reference

**On this page:** How configuration is layered · Connection strings · PlatformSettings · Minimal Bedrock configuration · Authentication and SSO · JWT · File storage · Email · Data protection · Logging (Serilog) · CORS · Response headers of the API · Other keys · Related

This is the public reference for the runtime configuration the platform reads from `appsettings.json` (and its environment-specific overlays). It lists the keys that matter for a deployment, each with a **placeholder value** and a one-line description.

> [!CAUTION]
> **Never commit real secrets**
>
> Every value on this page is a placeholder (`<...>`). Real API keys, connection strings, JWT signing keys, AWS account IDs, and OAuth client secrets must **never** be committed to source control or pasted into shared docs. Supply them per environment through the deploy pipeline's settings injection or your secrets store.
>

## How configuration is layered

Settings are resolved by `SetupConfigurationAsync` in `ConfigurationBuilderExtensions.cs` (`IniaAi.Configuration`), in increasing order of precedence:

1. `appsettings.json` — base defaults committed to the repo (no secret values).
2. **Cloud-mode sources** — when `FORCE_AWS_MODE` or `FORCE_AZURE_MODE` is `true`, the matching cloud overlay file and secret store load here. See [Cloud-mode configuration sources](#cloud-mode-configuration-sources).
3. `appsettings.{Environment}.json` — overlay per `ASPNETCORE_ENVIRONMENT` (`Development`, `LocalDevelopment`, `Staging`, …).
4. **Settings files** — each folder that `SETTINGS_DIRECTORIES` names (separated by `:` on Linux and macOS, `;` on Windows). Every file in the folder is one setting: the file name is the key, with `__` between sections (`PlatformSettings__Anthropic__ApiKey`), and the content is the value. One trailing line ending, `\n` or `\r\n`, is not part of the value, whichever system wrote the file. A container takes a secret this way as a mounted file, so `docker inspect` does not show it. A named folder that does not exist stops startup with `SETTINGS_DIRECTORIES names '<folder>', which does not exist.`
5. **Environment variables** — highest precedence. Nested keys use the double-underscore separator, e.g. `PlatformSettings__Anthropic__ApiKey`, `ConnectionStrings__CoreDbConnection`.

A list entry is an indexed key in every source: `Authentication__Oidc__AllowedDomains__0`, `__1`, … as environment variables, `…/Authentication/Oidc/AllowedDomains/0` in SSM, and `…--Authentication--Oidc--AllowedDomains--0` in Key Vault.

A handful of behavioural switches are read directly from environment variables rather than `appsettings.json`:

| Variable | Effect |
|----------|--------|
| `FORCE_AWS_MODE` | `true` selects native AWS Bedrock providers and enables the AWS configuration sources below; `false` disables AWS auto-detection. Unset, the app runs in AWS mode when it is not in Azure mode and finds itself on EC2 through the instance metadata (`CloudHost`). See [AI providers & connectors](../architecture/ai-integration/providers.md). |
| `FORCE_AZURE_MODE` | `true` selects Azure AI Foundry providers and enables the Azure configuration sources below. |
| `AZURE_KEY_VAULT_URI` | Vault URI (`https://<vault-name>.vault.azure.net/`). When set in Azure mode, secrets load from Azure Key Vault — see [Cloud-mode configuration sources](#cloud-mode-configuration-sources). |
| `RESEED_DB` | `true` wipes and recreates the database on startup (destructive — keep `false` outside throwaway environments). |
| `ASPNETCORE_ENVIRONMENT` | Selects the overlay file and DI wiring. A deployment runs `Production` or `Staging`. Only `LocalDevelopment`, the environment of a developer machine, serves the Swagger UI and the OpenAPI document. |
| `ASPNETCORE_URLS` | Kestrel bind addresses. |
| `SETTINGS_DIRECTORIES` | Folders whose files are settings, one file per key; see step 4 above. The platform repository's `compose.yaml` sets it to `/run/settings:/run/inia/platform`. |
| `CHROMEDRIVER_PATH` | The chromedriver that chart rendering and link downloads start Chrome with. The API and admin images set `/usr/local/bin/chromedriver`. Unset, Selenium runs Selenium Manager on each Chrome start, which looks the driver up on the internet, sends usage statistics, and has no arm64 Linux build. |

### Cloud-mode configuration sources

Both cloud modes insert their sources between the base `appsettings.json` and the environment overlay, so environment variables and the `{Environment}` overlay still override anything a secret store supplies. Both modes at once stop startup with `AWS and Azure modes are both active — set exactly one of FORCE_AWS_MODE and FORCE_AZURE_MODE.` The SSM and Key Vault sources reload every 5 minutes, but most settings are read once at startup, so a changed value is safe to rely on only after a restart. In both modes a `ServiceType` value (`api` or `admin`, baked into each service's `appsettings.json`, defaulting to `api`) selects the service-specific scope, and shared values load first so service-specific values override them.

**AWS mode** (`FORCE_AWS_MODE=true`): loads `appsettings.Aws.json` (Bedrock model tier, log sinks), then AWS Secrets Manager secrets named `iniaai/{env}/shared/secrets/*` and `iniaai/{env}/{serviceType}/secrets/*`, then SSM Parameter Store parameters under `/iniaai/{env}/shared/{config,secrets}` and `/iniaai/{env}/{serviceType}/{config,secrets}`. The Secrets Manager and SSM sources are optional — an unreachable store logs a warning and startup continues — but `appsettings.Aws.json` itself is required: a missing overlay file stops startup. A Secrets Manager that does not answer holds startup for at most 30 seconds. Then a warning names the limit, and startup goes on without the secrets it did not read.

**Azure mode** (`FORCE_AZURE_MODE=true`): loads `appsettings.Azure.json` (Serilog defaults), then — only when `AZURE_KEY_VAULT_URI` is set — Azure Key Vault secrets via `DefaultAzureCredential` (managed identity on a VM). Key Vault has no paths, so the scope is encoded in the secret name: `shared--*` loads first, `{serviceType}--*` overrides it, and `PrefixKeyVaultSecretManager` strips the scope prefix and maps each remaining `--` to the `:` config-key separator (secret `shared--PlatformSettings--Anthropic--ApiKey` becomes key `PlatformSettings:Anthropic:ApiKey`). Failure behaviour differs from AWS by design: an unset `AZURE_KEY_VAULT_URI` logs a warning and skips Key Vault, but a malformed URI or a set-but-unreachable vault (credential, RBAC, or private-endpoint DNS failure) stops startup with an error naming the cause.

## Connection strings

```json
{
  "ConnectionStrings": {
    "CoreDbConnection": "Server=<db-host>;Port=5432;Database=<core-db>;User Id=<db-user>;Password=<db-password>;SSL Mode=Prefer;Trust Server Certificate=true",
    "UserDataDbConnection": "Server=<db-host>;Port=5432;Database=<userdata-db>;User Id=<db-user>;Password=<db-password>;SSL Mode=Prefer;Trust Server Certificate=true"
  }
}
```

| Key | Description |
|-----|-------------|
| `ConnectionStrings:CoreDbConnection` | Connection string for the `Core` schema (workflow templates, prompt sets, pricing). |
| `ConnectionStrings:UserDataDbConnection` | Connection string for the `UserData` schema (executed workflows, documents, embeddings). |
| `PlatformSettings:CoreDbProvider` | Database provider for Core — `PostgreSQL` or `SqlServer`. |
| `PlatformSettings:UserDataDbProvider` | Database provider for UserData — `PostgreSQL` or `SqlServer`. |

Both contexts may point at the same physical database (different schemas) or at two separate servers; the topology is a deployment choice. See [Database Migrations](../getting-started/database-migrations.md).

## PlatformSettings

The root of application configuration. Most subsections below hang off `PlatformSettings`.

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `PlatformSettings:CustomerOrgId` | `<customer-org-id>` | The organization identifier seeded for the deployment's tenant. The shipped `appsettings.json` and the code default set the neutral `Default`; it must not be empty. |
| `PlatformSettings:InstanceName` | `<instance-name>` | The name this deployment states as itself on cross-instance artifacts: the `SourceInstance` in exported bundles, such as a counterparty bundle, and the instance label on [workflow-version currency cards](../features/workflow-template-versioning.md). When unset, bundle exports fall back to the request's Host header — so one deployment reachable through several hostnames exports under several identities — and the currency card falls back to the machine name. |
| `PlatformSettings:MessageProcessor:ApiKey` | `""` | Key that admits the message processor's client to the admin app's email-upload and file-download endpoints without a signed-in user. Empty (the default) turns key access off: those endpoints then admit only their roles. Set your own key only when a message processor calls them. |
| `PlatformSettings:ModelRouting:DefaultModelFamily` | `Anthropic` | Default chat model family. Only `Anthropic` is implemented. |
| `PlatformSettings:ModelRouting:TenantModelOverrides` | `{}` | Per-organization `AdvancedModel` / `BasicModel` model ids that replace the configured models for that organization's model calls. Each value must be a catalogue id: the connector resolves it to the id this deployment calls, and a call fails when the deployment cannot call that model ([Block model choice resolves per stack](../architecture/ai-integration/providers.md#block-model-choice-resolves-per-stack)). An entry may also set `AgentAdvancedModel` / `AgentBasicModel`, which replace `AgentRuntime:AdvancedModel` / `AgentRuntime:BasicModel` for that organization's Agent blocks; those are provider-neutral ids resolved against `AgentRuntime:AllowedModels`, and the AI Query pair does not affect them. |
| `PlatformSettings:RunIntake:ControlPort` | `5999` in a container, none outside one | The port of the run-intake control, which listens only on `127.0.0.1` beside the server's own addresses. A deploy pauses new runs through it before it replaces the process ([Background Jobs](../architecture/background-jobs/index.md#operational-troubleshooting)). `0` turns it off. Outside a container it is off unless set, because several local instances share one loopback address. The port joins the addresses the server already listens on (`ASPNETCORE_URLS`, else `ASPNETCORE_HTTP_PORTS` and `ASPNETCORE_HTTPS_PORTS`); a deployment that names Kestrel endpoints in configuration replaces those addresses, and the control port with them. |


### AI provider keys

These hold the API keys for each external AI / search / document service. Leave a key empty to disable that integration. **Values are placeholders only.**

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `PlatformSettings:Anthropic:ApiKey` | `<anthropic-api-key>` | Direct Anthropic public-API key (used when not in AWS/Azure mode). |
| `PlatformSettings:Anthropic:AdvancedModel` | `PublicSonnet5` | Model enum used when the advanced tier is requested. |
| `PlatformSettings:Anthropic:BasicModel` | `PublicHaiku5_5` | Model enum used for the basic tier. `appsettings.Aws.json` sets `UsAwsHaiku5_5`; an SSM value overrides both. |
| `PlatformSettings:Cohere:ApiKey` | `<cohere-api-key>` | Direct Cohere embeddings API key. Without it the connector sends nothing: page overviews keep the error vector and processing goes on. A block whose filter rule ranks pages by embeddings says that it needs a Cohere API key, and the server log names this setting ([Embedding Generation → Without a Cohere key](../architecture/rag/embedding-generation.md#without-a-cohere-key)). |
| `PlatformSettings:Exa:ApiKey` | `<exa-api-key>` | Exa search key for the ExaSearch block. |
| `PlatformSettings:Exa:ExaApiUrl` | `https://api.exa.ai` | Exa API base URL. |
| `PlatformSettings:Exa:TimeoutMinutes` | `5` | HTTP timeout of an Exa call. |
| `PlatformSettings:Exa:MaxRetries` | `3` | Retries of a failed Exa call. |
| `PlatformSettings:ExchangeRateApi:ApiKey` | `<exchange-rate-api-key>` | Key of the exchange-rate service the workflow engine converts currencies with (`ExchangeRateService`). `BaseUrl` (`https://v6.exchangerate-api.com`), `MaxRetryAttempts` (`3`), `RetryDelayMilliseconds` (`1000`) and `CacheExpirationMinutes` (`60`) tune it. |

There is no web search through Google or SerpAPI, and no PDF builder service. `PlatformSettings` has no `Google`, `SerpApi` or `PdfBuilder` section, so a value set under one of those names has no effect.

Model selection, temperatures, max-tokens, retry budgets, and rate-limit settings live in the strongly-typed `Anthropic` / `Cohere` settings classes and are documented in [AI providers & connectors](../architecture/ai-integration/providers.md#where-models-temperatures-and-retries-are-configured). Only the deployment-specific keys above need to be set per environment.

### AgentRuntime

Settings for the external agent-runtime (sidecar) integration, currently incubating. While `Enabled` is `false` — the shipped default — **agent-family blocks** such as `Agent` cannot be saved and no sidecar traffic occurs.

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `PlatformSettings:AgentRuntime:Enabled` | `false` | Master gate, and the mixed-version authoring gate: workflows containing **agent-family blocks** such as `Agent` are rejected at save time while it is off, by one refusal naming every family block it found. Flip per environment only after **every** host runs a build whose serializers know **every** agent-family block type — an older host fails whole queries on an unknown type. |
| `PlatformSettings:AgentRuntime:BaseUrl` | `http://localhost:8130` | Agent-runtime sidecar base URL. When enabled it must be an absolute URL without the runtime's API version segment, which the client adds, or startup validation fails. |
| `PlatformSettings:AgentRuntime:AuthToken` | `<agent-runtime-token>` | Bearer token for the sidecar; required when enabled unless the local-dev opt-out below is set. |
| `PlatformSettings:AgentRuntime:AllowUnauthenticatedSidecar` | `false` | Local-development-only opt-out for a sidecar running `AUTH_DISABLED`. Never set in a deployed environment. |
| `PlatformSettings:AgentRuntime:TimeoutSeconds` | `30` | HTTP timeout for sidecar calls. |
| `PlatformSettings:AgentRuntime:McpServerUrl` | `https://api.example.com/mcp` | The externally reachable MCP server URL for worker runs, sent as the run's `mcpServerUrl`. Optional; when set it must be an absolute `http` or `https` URL, or startup validation fails. No run sends it yet: deployed environments set it ahead of MCP support, so turning MCP on needs no configuration rollout. |
| `PlatformSettings:AgentRuntime:RunTokenValidityHours` | `24` | Validity of the per-run JWT minted for the runtime; long by design so a paused run can resume without re-minting. |
| `PlatformSettings:AgentRuntime:AllowedModels` | `[]` | The runtime's pinned model set mirrored into platform config, byte for byte the runtime's `ANTHROPIC_ALLOWED_MODELS`; agent-family blocks may only select these models, in any provider's form (a block stores the provider-neutral id and a run resolves it to the listed form), enforced when the workflow is saved and again when it is submitted. Must be non-empty when enabled — the runtime's own allowlist (a 400 on run start) is the backstop. **The value shape follows the deployment mode**: Bedrock inference-profile ids (`us.anthropic.claude-sonnet-5`) in AWS mode, Foundry deployment names (`claude-sonnet-5`) in Azure mode, and Anthropic API model ids (`claude-sonnet-5-5`) on a direct-API platform paired with the runtime's `anthropic` mode; an entry of the wrong shape for the mode refuses startup in AWS and Azure mode, and in direct-API mode the runtime refuses a Bedrock id or ARN at its own boot. Details for Azure: [The agent path in Azure mode](../architecture/ai-integration/azure-foundry.md#the-agent-path-in-azure-mode). |
| `PlatformSettings:AgentRuntime:AdvancedModel` | `claude-opus-5-5` | The model an `Agent` block runs when it names no specific model and its advanced tier is on (the default for a new block). Any provider's id of a model in `AllowedModels`; a run resolves it to the listed form. An organization's `ModelRouting:TenantModelOverrides:<org>:AgentAdvancedModel` wins over it, and blank falls back to the default. Not checked at startup: a deployment whose list lacks it still starts, and the save and submit gates refuse a block on this tier with the reason. |
| `PlatformSettings:AgentRuntime:BasicModel` | `claude-sonnet-5-5` | The same for the standard tier (advanced off), with `AgentBasicModel` as the organization override. |



#### Consumer patience and spend protection

There is no platform-side default for any run fuse. A fuse is declared only when a block author
sets one, and a blank fuse is left off the start request so the runtime's own deployed default
governs the run — see
[Agent Runtime Integration](../architecture/services/infrastructure/agent-runtime-integration.md).
The keys below govern something different: how much a consumer will spend protecting work that has
already been paid for. None of them rides the start request, and none of them limits a run.

They compound rather than cap: a fuse binds one run, and `MaxRunPasses` buys several, so a block's
worst-case spend and wall time multiply by the pass count — the arithmetic is stated plainly in
the [Agent block reference](../guides/blocks/block-reference.md#agent). That is the accepted price of
absorbing infrastructure failures instead of discarding what the workflow already spent upstream;
lower `MaxRunPasses` to tighten it.

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `PlatformSettings:AgentRuntime:MaxLoopAgentCopies` | `20` | The most Agent block copies one loop may expand to in a run. A loop's list is known only at run time and each copy buys its own run, so a longer list fails the run before any agent run starts (`WorkflowHandler.ThrowIfLoopExceedsAgentCopyCap`). |
| `PlatformSettings:AgentRuntime:AdmissionPatienceSeconds` | `600` | How long a consumer waits out capacity refusals before giving up on being admitted. Spent before a run exists, so it never eats the run's own deadline. Charged *per pass*, so it compounds with `MaxRunPasses`. `0` makes the first refusal fatal. |
| `PlatformSettings:AgentRuntime:MaxRunPasses` | `3` | How many runs one block may buy when each failure is retryable infrastructure trouble. Multiplies both worst-case spend and worst-case wall time: each pass is a fresh run entitled to the declared budget again, and to its own admission patience and poll deadline. `1` disables retries. |
| `PlatformSettings:AgentRuntime:UpstreamErrorRetryDelaySeconds` | `600` | How long an Agent block waits before its one outage retry, after a run fails `upstream_error`. Adds one wait to each leg's worst-case wall time. `0` turns the outage retry off. The rule: [Agent Runtime Integration](../architecture/services/infrastructure/agent-runtime-integration.md). |
| `PlatformSettings:AgentRuntime:PollErrorTolerance` | `3` | How many consecutive failed polls a live run survives before the consumer gives up on it. `0` restores voiding a run on the first errored poll. |
| `PlatformSettings:AgentRuntime:RunDeadlineMarginSeconds` | `120` | How much longer than a *declared* run deadline the consumer keeps polling, so it sees the runtime's verdict instead of racing it. Applies only when the block declared a deadline. |
| `PlatformSettings:AgentRuntime:MaxPollDeadlineMinutes` | `30` | How long the consumer keeps polling a run that declared **no** deadline, whose real deadline is the runtime's own and unknown to the platform. A bound on the consumer's patience, not a mirror of any runtime fuse. Must be positive. |
| `PlatformSettings:AgentRuntime:SendAdmissionClass` | `false` | When on, agent-conversation turns (the workflow editor agent) ask the runtime for its **chat** admission class: a small reserve of run slots that workflow runs cannot take, and a cap chat runs cannot pass. Workflow (Agent block) runs never send a class. A runtime older than the classes rejects the field with 400 and every chat turn fails, so turn this on only after every runtime the platform talks to runs an agent-runtime release with the admission classes, with `AGENT_RUNTIME_CHAT_RESERVED_SLOTS` and `AGENT_RUNTIME_CHAT_MAX_SLOTS` set on it (recommended 4 and 8; the reserve defaults to 0). |



### WorkflowExecutionJanitor

The stuck-execution janitor (a WebApi background service). Executions run on detached threads
pumped from an in-memory queue, and nothing re-reads active rows at startup — a process death
strands them and the agent runs they started keep burning on the runtime. The janitor fails
age-exceeded active rows (`Undefined` included) through the guarded terminal write with failure
kind `LifetimeExceeded`, and the terminal funnel then completes or cancels each run those rows
recorded. The lifetime is deliberately hours-scale — well past block deadlines times passes plus
document processing — and deliberately under the runtime's 24-hour retention reaper, so cleanup
still finds the runs it must settle.

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `PlatformSettings:WorkflowExecutionJanitor:Enabled` | `true` | Kill switch for the sweep loop. |
| `PlatformSettings:WorkflowExecutionJanitor:MaxLifetimeHours` | `12` | An active execution older than this (by `StartedAt`, falling back to `CreatedAt`) is failed on the next sweep. |
| `PlatformSettings:WorkflowExecutionJanitor:SweepIntervalMinutes` | `30` | How often the janitor sweeps. |
| `PlatformSettings:WorkflowExecutionJanitor:HostLossRecovery` | `false` | Turns on host-loss recovery, which fails the runs of a process that stopped within minutes instead of after `MaxLifetimeHours` ([Background Jobs](../architecture/background-jobs/index.md#operational-troubleshooting)). |

### TokenUsageTotals

The usage-totals sweep (a WebApi background service, `TokenUsageTotalsSweepService`). It adds new
`TokenUsages` ledger rows to the daily and per-execution totals that the usage pages and the
validation estimate sum ([how the totals are kept](../reference/database/user-data-database.md#how-the-usage-totals-are-kept)).
A usage read adds the ledger rows the sweep has not reached, so the interval sets how much a read
sums from the ledger, never whether the read is exact. Every WebApi host runs the loop; a database
session lock lets one host at a time do the work.

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `PlatformSettings:TokenUsageTotals:Enabled` | `true` | Kill switch for the sweep loop. With it off, the totals stop growing and every read sums the newer ledger rows itself. |
| `PlatformSettings:TokenUsageTotals:SweepIntervalSeconds` | `30` | How often the sweep runs. The first sweep starts 30 seconds after the host starts. |

### WebApiCall

Limits for the WebApiCall block's outbound HTTP calls (`WebApiCallSettings`). Parallel blocks
that target the same host share a process-wide semaphore of this size, keyed by host plus a hash
of the request's credential header, so one organisation's burst against a rate-limited API such as
Perplexity is spread out instead of tripping the key's limit and failing every block at once. The
429 backoff itself (up to 8 retries, `Retry-After` honoured, 120 s total budget) is fixed in code;
only the concurrency is configurable. Read once at first use of each host; a change takes effect
on the next process start.

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `PlatformSettings:WebApiCall:MaxConcurrentRequestsPerHost` | `8` | In-flight requests allowed per host (per credential) when the host has no override. |
| `PlatformSettings:WebApiCall:MaxConcurrentRequestsByHost` | `{ "api.perplexity.ai": 3 }` | Per-host overrides, keyed by host name (case-insensitive). Perplexity is seeded at 3, under its Tier 1 rate of 3 requests per second, so a deployment with no configuration still protects the one key its corporate workflows share. |

### ContentRefinement

The longest input a refine request may carry, in characters (`ContentRefinementSettings`). Refine
rewrites one report paragraph, and each follow-up message resends the conversation so far, so
these two keys bound what one request can cost. A request over either cap is refused with a
message that names the cap, such as "The paragraph is 62,000 characters, more than the 50,000 that
refine takes. Shorten the paragraph, then refine it again." The defaults are the limits every
deployment had before the keys existed. A value under 1 stops startup.


The Vue app also checks 100,000 and 500,000 before it sends, and shows the server's message when
a deployment sets lower caps.

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `PlatformSettings:ContentRefinement:MaxContentLength` | `100000` | The longest paragraph a request may ask to refine. |
| `PlatformSettings:ContentRefinement:MaxCombinedLength` | `500000` | The longest paragraph, instruction and conversation history together. |

### StoredKeys

The deployment's own values for stored key names (`StoredKeySettings`). A WebApiCall header that names a stored key uses the run organization's key, and this value only when the organization has none ([Stored Keys](../features/user-management/stored-keys.md#at-run-time)). Read at process start. The values are secrets: set them as secret settings, such as SSM SecureString parameters, never in a committed file.

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `PlatformSettings:StoredKeys:DeploymentKeys:<name>` | (none) | The value for the stored key `<name>`, such as `PerplexityApiKey` or `ParallelApiKey`, for every organization that has none. Empty by default, so each organization uses only its own keys. |

### OutboundAccess

> [!CAUTION]
> **Every entry goes through a process**
>
> An entry in `AllowedInternalHosts` opens an internal service to every workflow of every organization on the deployment. Add or remove one only through [Calling an Internal Service from a Workflow](internal-service-calls.md), which records each entry. An entry that covers a cloud metadata service (`169.254.169.254`, `fd00:ec2::254`, `168.63.129.16`) stops startup.

Where the requests that a user, a model answer or an email can steer may go
(`OutboundAccessSettings`): the WebApiCall block and the email link downloader. Both refuse every
loopback, private, carrier-grade NAT (`100.64.0.0/10`), link-local, unique-local, multicast
(`224.0.0.0/4`, `ff00::/8`), unspecified,
reserved (`192.0.0.0/24`, `240.0.0.0/4`), benchmarking (`198.18.0.0/15`), local-use NAT64
(`64:ff9b:1::/48`) and deprecated IPv4-compatible (`::/96`) address, and the Azure host platform
address `168.63.129.16`, judged on the address each host name resolves to, unless this list
names it. The
platform's own clients (model providers, storage, the agent runtime) are not limited. Each entry
is a host name, an IP address or a CIDR range. Set each entry as an indexed key, such as
`PlatformSettings__OutboundAccess__AllowedInternalHosts__0=reports.internal`: an entry that is
not a host name, an address or a range, or a list set as one value, stops startup. Behind an
egress proxy (`HTTP_PROXY`, `HTTPS_PROXY`) the proxy resolves the target: the platform still
refuses a target that resolves, from where the platform runs, to an internal address, and leaves
a name it cannot resolve itself to the proxy and its policy.

Requests a user can steer, and the check each meets:

- The WebApiCall block: the first request, every redirect hop (followed on purpose, up to 50, only to `http` or `https`, never from HTTPS down to HTTP), every poll and the completion URL. Each goes through this guard ([block reference](../guides/blocks/block-reference.md)).
- Email links (`EmailExpansion:ShouldTryDownloadUrls`, off by default): the HTTP client through this guard, and the headless browser through a checked proxy that applies it to every request the page makes, its pictures and redirects included. A link is taken only when it starts with `http://` or `https://`.
- Nothing else: the readers and converters load nothing a document names. A linked picture or field in a Word file, a remote picture or style sheet in a report, and an external DTD in an Office part are not fetched.

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `PlatformSettings:OutboundAccess:AllowedInternalHosts` | `[]` | The internal hosts that a WebApiCall block or a downloaded email link may reach on purpose. Empty by default, so none. |

### AgentRunSessionBundleReaper

The session-bundle retention sweep (a WebApi background service). A finished agent run that a
later run may continue leaves its session bundle in platform custody (`AgentRunSessionBundles`),
so the later run can continue that session instead of starting over. An execution releases its own bundles the moment it reaches a terminal (see
[Soft-Delete Cleanup](soft-delete-cleanup.md) for that rule), so this sweep is the
backstop, collecting only what a crashed or stranded execution left behind. The
window is generous against the stuck-execution janitor's twelve hours: an execution the janitor
never reached is already an anomaly, and a week leaves room to look at one before its evidence
goes. The interval is short against the window on purpose — the sweep reads one indexed column
and usually deletes nothing.


| Key | Placeholder | Description |
|-----|-------------|-------------|
| `PlatformSettings:AgentRunSessionBundleReaper:Enabled` | `true` | Kill switch for the sweep loop. |
| `PlatformSettings:AgentRunSessionBundleReaper:MaxAgeDays` | `7` | A bundle taken into custody longer ago than this is deleted with its bytes on the next sweep. |
| `PlatformSettings:AgentRunSessionBundleReaper:SweepIntervalMinutes` | `60` | How often the reaper sweeps. |

### EditorAgent

The [agent beside the workflow editor](../features/editor-agent.md). It is on only when `Enabled` is true **and**
`PlatformSettings:AgentRuntime:Enabled` is true; either off hides Agent mode and its **Work with the agent** button. How its turns run,
wait and settle: [Agent conversations](../architecture/services/infrastructure/agent-runtime-integration.md#agent-conversations).

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `PlatformSettings:EditorAgent:Enabled` | `false` | Feature gate. |
| `PlatformSettings:EditorAgent:RetentionDays` | `7` | A conversation ends this many days after its last message, and the next retention sweep deletes it. 1 to 365. |
| `PlatformSettings:EditorAgent:MaxRunningTurnsPerUser` | `2` | How many messages one user may have running at once across their editor conversations. A message waiting behind another in its conversation does not count. |
| `PlatformSettings:EditorAgent:ResumeSessions` | `false` | Whether a message continues the previous message's agent session. Off until the runtime bills a continuation by its own cost rather than the whole session's; off, every message starts fresh with the earlier messages in its prompt and no session bundle is kept. |
| `PlatformSettings:EditorAgent:MaxDraftMegabytes` | `50` | The largest workflow (export JSON with test-run outputs) a message may send; a bigger one is refused before anything is stored. Also the largest past report run the agent is given; a bigger run is left out and its header says so. |
| `PlatformSettings:EditorAgent:MaxReportDocumentsMegabytesPerMessage` | `150` | The most one message stages of its past reports' documents (originals and page text), across every report it reads. Held under the runtime's input budget once the workflow's own files are counted; a document that does not fit is listed in its report's header instead. 0 to 1024. |
| `PlatformSettings:EditorAgent:MaxReportDocumentMegabytes` | `50` | The largest original document of a past report that is staged; a bigger one is listed as left out. 1 to 1024. |
| `PlatformSettings:EditorAgent:MaxBudgetUsdPerMessage` | `3` | Per-message spend fuse, clamped to the runtime's published ceiling. |
| `PlatformSettings:EditorAgent:DailySpendBatchUsd` | `50` | An organization's daily batch of agent spend, used when the organization has no amount of its own (`Organization.EditorAgentDailySpendBatchUsd`, set in Administration > Tenants). Once the organization's spend today (UTC), counting each waiting or running message at `MaxBudgetUsdPerMessage`, reaches its batches, the next message warns and the user may unlock another batch. 0.01 to 100000. |
| `PlatformSettings:EditorAgent:MaxTurnsPerMessage` | `60` | Per-message agent turn fuse, clamped to the runtime's ceiling. |
| `PlatformSettings:EditorAgent:DeadlineMinutesPerMessage` | `15` | Per-message deadline, clamped to the runtime's ceiling. With `AdmissionPatienceSeconds` it is the deadline the overdue sweep measures. |
| `PlatformSettings:EditorAgent:AdmissionPatienceSeconds` | `120` | How long a message waits for a free runtime slot before it fails. |
| `PlatformSettings:EditorAgent:Effort` | `medium` | The reasoning effort every message runs at. |
| `PlatformSettings:EditorAgent:DefaultModelMatch` | `sonnet-5` | Used only when the runtime allowlist holds neither the platform's advanced nor its basic model. A new conversation normally starts on the advanced model (see [Editor Agent](../features/editor-agent.md)); in that case it starts on the first allowed runtime model whose id contains this text, or the first allowed model. |
| `PlatformSettings:EditorAgent:ExpertModel` | `us.anthropic.claude-opus-5-5` | The **Expert** model, listed first in the agent's model menu, above the Advanced and Standard tiers, and never the default. Offered in the form the runtime allowlist (`AgentRuntime:AllowedModels`) names it, so another region prefix or the public API id finds the allowlisted form. Hidden when the allowlist has no form of it, or when it is the same model as a tier. Empty hides it. |
| `PlatformSettings:EditorAgent:LiveStepsIntervalMilliseconds` | `1000` | How often the panel's live steps are refreshed from the agent's trace while a message runs. `0` falls back to the shared feed's pace, a refresh on the status poll at most every 5 seconds. 0 to 60000. |

### AgentConversations

Settings shared by every kind of agent conversation. Each purpose sets its own retention window and
running cap (the editor agent's are above).

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `PlatformSettings:AgentConversations:SweepIntervalMinutes` | `60` | How often the WebApi retention sweep ends idle conversations, clears the read evidence of turns settled longer ago than their purpose's retention window, and deletes ended conversations. Runs whether or not the agent runtime is enabled. |
| `PlatformSettings:AgentConversations:TurnSweepIntervalMinutes` | `2` | How often the WebApp (the host that runs turns) fails overdue turns and starts waiting turns nobody started. |
| `PlatformSettings:AgentConversations:OverdueMarginMinutes` | `10` | How long past its purpose's deadline a dispatched turn may stay in flight before the WebApp's turn sweep fails it and cancels its run. |
| `PlatformSettings:AgentConversations:HostInstanceId` | *(empty)* | The name a host stamps on the turns it runs, so startup recovery abandons only its own. Empty takes the machine name and the application name. Set it when two hosts share a machine name and a database. |


### ChartRendering

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `PlatformSettings:ChartRendering:RenderTimeoutMs` | `10000` | Maximum render time before timeout. |


### PdfReader

The open build reads each open PDF file in a worker process of its own ([document processing](../architecture/document-processing/index.md#the-pdfium-worker-process)).


| Key | Placeholder | Description |
|-----|-------------|-------------|
| `PlatformSettings:PdfReader:MaxCountingWorkers` | `2` | How many PDF reader processes counting the pages of uploads runs at once, apart from `MaxWorkers`, so an upload never waits for a report to close a file. The paid build counts with its own PDF reader, in the app process, and does not read this setting. |
| `PlatformSettings:PdfReader:MaxWorkers` | `8` | How many PDF reader processes one app process runs at once. A document past the limit waits until an open one closes. Each process holds its whole file and PDFium's memory for it. No run has measured the default yet. |

### Email expansion

Settings for [email expansion](../reference/api/submit-workflow-execution.md#email-expansion) (`EmailExpansionSettings`) — shared by the submit endpoint and the in-execution email intake stage behind interactive uploads.

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `PlatformSettings:EmailExpansion:ShouldTryDownloadUrls` | `false` | Whether expansion downloads PDFs from URLs found in email bodies. Off by default — server-side fetching of caller-controlled URLs is a deployment-level decision; deployments fed by trusted email pipelines opt in. A link to an internal address is skipped unless [`OutboundAccess`](#outboundaccess) allows it. When on, a `/submit` request that expands an email waits synchronously for every URL download (page-load delay plus retry budget, per URL); the start path pays that inside the execution job instead. |
| `PlatformSettings:EmailExpansion:ChromePageLoadDelayMs` | `30000` | Wait after Chrome navigation before checking for downloaded PDF content (URL downloads only). |
| `PlatformSettings:EmailExpansion:MaxArchiveEntries` | `100` | ZIP attachments with more entries are skipped whole and reported. The per-entry decompressed size cap reuses `PlatformSettings:IniaAi:MaxFileSizeMb`. |
| `PlatformSettings:EmailExpansion:SkippedLinkHosts` | `["linkedin.com"]` | Hosts whose links expansion never downloads, each host with its subdomains. Configured hosts add to the default. |



### Job concurrency

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `PlatformSettings:IniaAi:MaxConcurrentJobs` | `6` | How many jobs run at once in each app. The API and the admin app each run their own [job queue](../architecture/background-jobs/index.md), and the rest wait in it. |

Jobs wait on the model, not the CPU, so the model's rate limit and the container's memory set this value. Size both from a measured container run, never by guess. Lower the value when the deployment's model endpoint throttles the calls of 6 jobs.


Neither the API image nor the admin image adds a garbage-collector setting. In a container, .NET caps the heap at 75% of the memory limit, far above the measured heap.

## Minimal Bedrock configuration

For native AWS deployments, set `FORCE_AWS_MODE=true` and supply only the model tier — the Bedrock provider holds **no API key**. It authenticates with the ambient AWS credential chain (instance role / environment) and signs each request with SigV4. See [AI providers & connectors → AwsBedrockChatProvider](../architecture/ai-integration/providers.md#awsbedrockchatprovider-native-aws-bedrock) and [Customer-Owned Bedrock Setup](customer-bedrock-setup.md).


```json
{
  "PlatformSettings": {
    "Anthropic": {
      "BasicModel": "UsAwsHaiku5_5",
      "AdvancedModel": "UsAwsSonnet5"
    }
  }
}
```

```bash
FORCE_AWS_MODE=true
```

> [!NOTE]
> **Where credentials come from**
>
> The native Bedrock path does not read an access key from `appsettings.json`. The AWS SDK resolves credentials from the instance role (in EC2) or the standard `AWS_*` environment variables / shared credentials file locally. The IAM principal needs `bedrock:InvokeModel` on the Claude (and Cohere) model IDs in the deployment region.

This block is the AWS-mode parallel of the [Minimal Azure-mode `appsettings.json`](../architecture/ai-integration/azure-foundry.md#minimal-appsettingsjson-for-azure-mode). Pick exactly one of AWS mode, Azure mode, or direct-API mode per deployment.

## Authentication and SSO

The platform supports password login, Google OAuth, and a generic OIDC provider (Okta, Auth0, Azure AD / Entra ID, or any standards-compliant SSO). Each is toggled independently under the `Authentication` section.

**Both apps read this section**: the WebApi serves the SPA's token-based login, and the Blazor admin app runs its own cookie login (password form plus server-side Google/OIDC redirect) from the same keys. In IaC-managed environments the `Authentication:*` keys therefore live in the **shared** config scope (AWS SSM `shared/config|secrets`, Azure Key Vault `shared--*`) so the `api` and `admin` services both receive them; `TwoFactor:*` stays api-scoped. Behind a proxy the admin also needs forwarded-header settings, or its sign-in `redirect_uri` names the wrong scheme or host: see [Admin behind a reverse proxy](#admin-behind-a-reverse-proxy).

### Admin behind a reverse proxy

The admin builds its sign-in callback URLs (the Google and OIDC `redirect_uri`) and its other absolute URLs from the scheme and host of the request it receives. Behind a proxy those are the proxy's upstream values, so the admin has to be told which forwarded values to trust:

- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` makes it honor `X-Forwarded-Proto` and `X-Forwarded-For`, so a TLS-terminating proxy yields `https`.
- That variable does not cover `X-Forwarded-Host`. A proxy that connects to the admin under a different host name from the one the browser uses, e.g. a Teleport application whose address is `http://localhost:5118`, passes the browser's host only in that header, so without the key below the admin builds `https://localhost:5118/signin-oidc`, which no identity provider can send a browser back to. With it, the admin takes its host from `X-Forwarded-Host` when the header names a listed host (`ForwardedHostConfiguration.AddForwardedHosts`).


| Key | Placeholder | Description |
|-----|-------------|-------------|
| `ForwardedHeaders:AllowedHosts` | `["<admin-public-host>"]` | Admin app only. Host names, without a port, accepted from `X-Forwarded-Host`; any other value is ignored and the request keeps its own host. Empty (the default): `X-Forwarded-Host` is ignored. The admin stops at startup when the list could not take effect as written: without `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, set as one value instead of indexed entries, or with an entry that is `*`, `[::]` or `0.0.0.0` (any host), carries a port, or has surrounding whitespace. |

Put it in the admin's service scope; it is a list, so each entry is an [indexed key](#how-configuration-is-layered). A port in the forwarded header is kept, so a browser-facing host on a non-default port gets its callback URLs registered with that port: see [Admin App (Blazor) Login](../features/user-management/sso-oidc.md#admin-app-blazor-login).

On its own, `ASPNETCORE_FORWARDEDHEADERS_ENABLED` trusts these headers from any sender for one hop. Once `ForwardedHeaders:KnownNetworks` or `KnownProxies` lists the proxies (next section), the admin app takes them only from loopback and those senders, as the API does, and it logs a warning at startup while the list is empty.

### API behind a reverse proxy

The API reads the client's address for its sign-in limits, the second-factor token and the trusted-device record. Behind a proxy it sees the proxy, so it takes `X-Forwarded-For` and `X-Forwarded-Proto` only from the proxies these keys name, and from loopback (`ForwardedHeadersConfiguration.AddClientForwardedHeaders`). Trusted hops are unwound, up to ten, so a load balancer in front of the web container's nginx still yields the client. A forwarded header from any other sender is ignored, so a client cannot choose the address its limits count against.

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `ForwardedHeaders:KnownNetworks` | `["<proxy-network-cidr>"]` | Both apps, in the shared scope. Networks of the proxies in front of the API and the admin app, in CIDR form, as [indexed keys](#how-configuration-is-layered): for example the load balancer's subnet and the Docker network of the web container. An entry that is not a CIDR stops startup, and so does a list set as one value (`ForwardedHeaders__KnownNetworks=...`), which would bind to an empty list. |
| `ForwardedHeaders:KnownProxies` | `["<proxy-address>"]` | Both apps. Single proxy addresses, beside the networks, with the same startup checks. |


Do not set `ASPNETCORE_FORWARDEDHEADERS_ENABLED` on the API: it trusts any sender, which would let a client pick its own address.

### Sign-in limits

Each endpoint that takes a credential or sends a mail (`/connect/token`, `/connect/token/mfa`, `/connect/token/mfa/email`, `/passwords/forgot`, `/passwords/reset`) allows one client address a fixed number of requests per window, counted per endpoint, so another spelling of the same path (a trailing slash, `v1.0`) counts against the same limit. An IPv6 client counts by its /64 network, which one subscriber holds whole. The admin app applies the same limit, from the same keys, to its sign-in, two-factor, recovery-code, forgot-password, reset-password and resend-confirmation pages. Past it the API answers `429` ([Authentication API](../reference/api/authentication.md#429-too-many-requests)). One customer's users often share one address, and `/connect/token` also serves refreshes, so size the limit for the busiest office.

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `RateLimiting:SignIn:PermitLimit` | `30` | Requests one address may make to one endpoint in a window. Under 1 stops startup. |
| `RateLimiting:SignIn:WindowSeconds` | `60` | Window length in seconds. Under 1 stops startup. |

### Password policy

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `Authentication:Password:Enabled` | `true` | Allow username/password login. |
| `Authentication:Password:MaxFailedAccessAttempts` | `5` | Lockout threshold. |
| `Authentication:Password:LockoutDurationMinutes` | `15` | Lockout duration after threshold is hit. |

### Google OAuth

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `Authentication:Google:Enabled` | `false` | Enable Google sign-in. |
| `Authentication:Google:ClientId` | `<google-oauth-client-id>` | Google OAuth client ID. A Google token is accepted only when it was issued to this id, so the API refuses to start with `Enabled` on and this empty, naming the key. |
| `Authentication:Google:ClientSecret` | `<google-oauth-client-secret>` | Google OAuth client secret. |
| `Authentication:Google:AllowedDomains` | `["<allowed-domain>"]` | Workspace domains permitted to sign in; empty allows all. |
| `Authentication:Google:ProvenAddressesOnly` | `false` | Google sign-in, in both apps, accepts only a Gmail address or a Google Workspace account ([Authentication](../reference/api/authentication.md#4-google-and-oidc-grants-external-sign-in)). |

### OIDC / SSO

The OIDC integration works with any OIDC provider; the issuer URL and client ID are all that change between providers.

```json
{
  "Authentication": {
    "Oidc": {
      "Enabled": true,
      "Issuer": "<https://your-oidc-issuer/.../v2.0>",
      "ClientId": "<oidc-client-id>",
      "DisplayName": "Company SSO",
      "AllowedDomains": ["<allowed-domain>"]
    }
  }
}
```

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `Authentication:Oidc:Enabled` | `false` | Enable the SSO sign-in button. |
| `Authentication:Oidc:Issuer` | `<https://your-oidc-issuer>` | Authorization-server issuer URL (full path, e.g. an Okta `oauth2/default`, Auth0 tenant, or `login.microsoftonline.com/{tenant}/v2.0`). |
| `Authentication:Oidc:ClientId` | `<oidc-client-id>` | OAuth client ID; also used as the token audience. |
| `Authentication:Oidc:ClientSecret` | `""` | Optional. Admin app only: its server-side OIDC flow runs as a confidential client when set, as a public PKCE client when empty. |
| `Authentication:Oidc:DisplayName` | `SSO` | Label shown on the SSO button. |
| `Authentication:Oidc:AllowedDomains` | `["<allowed-domain>"]` | Email domains permitted via SSO; empty allows all. |
| `Authentication:Oidc:TrustUnverifiedEmail` | `false` | Trust the email of a provider that sends neither `email_verified` nor `xms_edov`. Set it only when your organization manages every address at the provider: see [Verified email](../features/user-management/sso-oidc.md#verified-email). |

The platform also hosts its own OAuth/token endpoint via OpenIddict (`/connect/token`), used by the browser SPA login, the API token flow, and Swagger. Its client secret is a deployment secret:

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `OpenIddict:ClientSecret` | `<openiddict-client-secret>` | Secret the seeder assigns to the built-in OAuth client(s). No default. |
| `Authentication:ServiceAccount:Enabled` | `true` | Accept the `client_credentials` grant, which gives a super admin token to whoever holds the client's secret. Off, the API does not register the grant and a request for it answers `400` `unsupported_grant_type`. Turn it off where no integration uses it. The API reads it when it starts. |

On startup `SystemUserSeeder` provisions one system user and one confidential OpenIddict client **per seeded organization**, with the client id `service_user_<org-id>` (lower-cased) and this secret. Two organizations are always seeded — `IniaAI` (the platform org) and the deployment's `PlatformSettings:CustomerOrgId` (`UserDataDbInitializer.SeedOrganizations`). Each client permits the `password`, `client_credentials`, and `refresh_token` grants and the `offline_access` + `api` scopes. The source carries no secret: with `OpenIddict:ClientSecret` unset or empty, the seeder creates the system users but no client, and `client_credentials` sign-in stays off until a secret is set and the API restarts. The system users have no password, and the seeder removes one an earlier build gave them; they sign in with their client's secret only. The real value is supplied by your deployment owner.


### First accounts

`Seeding:Staff` and `Seeding:DisableSuperAdmin` are read at every start, the password keys only when their account must be created. The first start on an empty database creates SuperAdmin (`Seeding:SuperAdminEmail`, `superadmin@inia.ai` unless set) and, when `PlatformSettings:CustomerOrgId` is not `IniaAI`, the customer organization's admin (`admin@example.com`). The source ships no password for either (`SeedAccountPasswords`):

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `Seeding:SuperAdminEmail` | `superadmin@inia.ai` | SuperAdmin's address. Read only when the account does not exist yet. The open build's one-command stack sets it from `INIA_ADMIN_EMAIL`, `superadmin@example.com` by default. |
| `Seeding:SuperAdminPassword` | `<first-superadmin-password>` | SuperAdmin's password. Read only when the account does not exist yet. |
| `Seeding:CustomerAdminPassword` | `<first-customer-admin-password>` | The customer admin's password. Read only when the account does not exist yet. |
| `Seeding:DisableSuperAdmin` | `false` | `true` disables SuperAdmin at every start, as the admin app's Disable does: a permanent lockout, a new security stamp, and its API tokens revoked. It runs after `Seeding:Staff`, and stops the start instead when no other enabled account holds `SuperAdmin`, because nobody could grant roles. For a deployment that signs in only through an identity provider. A value other than `true` or `false` stops the start. |
| `Seeding:Staff:<n>:Email`, `Seeding:Staff:<n>:Roles:<m>` | none | Staff accounts that every start makes sure exist, with at least these roles (`SuperAdmin`, `Admin`, `WorkflowEditor`, …). A missing account is created in the `IniaAI` organization with no password, to sign in through the identity provider; an existing account with that address keeps its organizations and gains the roles. A start adds missing accounts and roles, removes none, and re-enables no disabled account. An address that is not one usable user name, an entry with no role or with a role the deployment does not define, an address that is another account's user name, or a list set as one value stops the start with a message that names the entry. |


With password sign-in off (`Authentication:Password:Enabled` false, as on a stack with Google or OIDC sign-in), the first start creates both accounts with no password and reads neither key. When an account must be created and its key is unset, or its password is shorter than 8 characters or is one of the most commonly used passwords (the API's password rule, `PasswordPolicy`), the app stops at startup with a message that names the key. Once the accounts exist the keys are not read, so a running deployment needs neither. With password sign-in on, change both passwords after the first sign-in.


### Vue SPA sign-in (browser login)

The browser front end (`IniaAi.Web`, the Vue SPA) signs the user in at the same `/connect/token` endpoint but names no OAuth client. A browser cannot keep a client secret, and the token server accepts a request that names no client (`AcceptAnonymousClients`, pinned by `AuthenticationConfigurationTests`). The SPA POSTs `grant_type`, `scope` and the user's `username`/`password` (or an MFA code, a refresh token, a Google or OIDC credential), with no `client_id` and no `client_secret` (`src/api/authentication/service.ts`). Vite compiles its browser-side variables into the bundle at build time (`src/config/auth.ts`, `src/config/api.ts`). The production image sets none of them, so each keeps its default. The web container reads one variable at start:

| Variable | Default | Description |
|----------|-------------|-------------|
| `VITE_OAUTH_GRANT_TYPE` | `password` | Build time. OAuth grant the SPA uses for a password login. |
| `VITE_OAUTH_SCOPE` | `offline_access api` | Build time. Scopes requested. |
| `VITE_API_URL` | `/` | Build time. The API base the SPA calls. With `/`, the browser calls its own origin and nginx proxies `/api/` to the API. |
| `PROXY_BACKEND_URL` | `http://api:80` | Container start. The nginx proxy target for `/api/`, written into `nginx.conf` by the image's `docker/40-proxy-backend.sh`. |
| `CSP_CONNECT_SRC` | `'self' https:` | Container start. The `connect-src` sources of the Content-Security-Policy, for example `'self' https://accounts.google.com`. Sources that would widen it beyond the default are refused and stop the container. See [Build and deploy](../architecture/frontend/build-and-deploy.md). |
| `CSP_MODE` | `enforce` | Container start. `enforce` sends the policy with `CSP_CONNECT_SRC` applied. `report-only` keeps enforcing the default policy and sends the narrowed one as `Content-Security-Policy-Report-Only`, for testing a narrower policy before enforcing it. |

No deploy configuration passes an OAuth client to the web container, so `OpenIddict:ClientSecret` never reaches the browser: it is the secret of the service login, see [System Users](../reference/api/system-users.md).


> [!WARNING]
> **Sessions from the previous SPA**
>
> A refresh token that the previous SPA got while it sent the client cannot be redeemed without that client. After the upgrade, a person signed in on the previous SPA signs in once more.

### Two-factor authentication

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `TwoFactor:MfaTokenValidityMinutes` | `5` | Validity of an MFA challenge token. |
| `TwoFactor:TrustedDeviceDays` | `30` | How long a trusted device is remembered. |
| `TwoFactor:RecoveryCodeCount` | `10` | Number of recovery codes generated. |
| `TwoFactor:EnforceIpValidation` | `false` | When `true`, an MFA challenge token fails when the client address changed between sign-in and code entry. When `false`, a change is logged and the sign-in proceeds. |
| `TwoFactor:EmailOtp:CooldownSeconds` | `60` | Minimum interval between two email codes for one user. The code's length and lifetime are Identity's defaults and have no setting. |

## JWT

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `Jwt:Issuer` | `<https://api.your-domain>` | Token issuer claim. Without one, the API issues and accepts `http://localhost`. Each deployment sets its own. |
| `Jwt:SecretKey` | `<jwt-signing-secret>` | Symmetric key that signs session and run tokens; the key that encrypts refresh tokens is derived from it (HKDF), so sessions survive a restart and every API instance reads them, and changing it ends every session. **Secret — supply per environment.** No settings file ships one, and the API stops at startup, naming the key, when it is missing or shorter than 32 bytes. Generate one with `openssl rand -base64 48`. |
| `Jwt:TokenValidityInMinutes` | `1000` | Access-token lifetime in minutes, and the refresh token's when `Jwt:RefreshTokenValidityInMinutes` is not set. |
| `Jwt:RefreshTokenValidityInMinutes` | not set | Refresh-token lifetime in minutes: how long a session may sit unused before the user signs in again. Each refresh issues a new refresh token with this lifetime. Not set, it is `Jwt:TokenValidityInMinutes`. |

There is no `Jwt:Audience` setting. `JwtSettings` has no such property, and a value set for it has no effect: `TokenGenerationService` sets the audience per token, `api` on a session token and `agent-runtime` on an agent run token.

## File storage

The platform can persist uploaded documents and generated artifacts in the database (default) or in S3 / MinIO object storage.

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `FileStorage:DefaultProvider` | `Database` | `Database`, `S3`, or `MinIO`. |
| `FileStorage:S3:Region` | `<aws-region>` | S3 region. |
| `FileStorage:S3:AccessKeyId` | `<s3-access-key-id>` | S3 access key (omit to use the ambient AWS credential chain). |
| `FileStorage:S3:SecretAccessKey` | `<s3-secret-access-key>` | S3 secret key. |
| `FileStorage:S3:ServiceUrl` | `<https://s3-endpoint>` | Override endpoint (for S3-compatible stores). |
| `FileStorage:MinIO:Endpoint` | `<minio-endpoint>` | MinIO server endpoint. |
| `FileStorage:MinIO:AccessKey` | `<minio-access-key>` | MinIO access key. |
| `FileStorage:MinIO:SecretKey` | `<minio-secret-key>` | MinIO secret key. |
| `FileStorage:MinIO:UseSSL` | `false` | Connect to MinIO over TLS. |
| `FileStorage:MinIO:Region` | `""` | MinIO region, optional. |
| `FileStorage:MinIO:BucketName` | `<bucket-name>` | One fixed, pre-created bucket for every file, for an on-premises single-tenant deployment. Unset, each organization gets its own `{orgId}-{category}` bucket; set, that per-organization isolation is off. |
| `FileStorage:TenantOverrides:<orgId>` | `S3` | The provider for one organization, instead of `DefaultProvider`. See [File Storage](../architecture/file-storage/index.md). |
| `FileStorage:StorageBuckets:documents` | `docs` | Bucket/prefix for documents. The shipped file also maps `images` (`imgs`), `uploads` (`uploads`) and `temp` (`temp`). |
| `FileStorage:StorageBuckets:agent-session-bundles` | `agent-session-bundles` | Bucket/prefix for agent [session bundles](../reference/database/user-data-database.md#agentrunsessionbundles). Under the default `Database` provider each bundle is one row in the UserData `Files` table, materialized whole on store, on read and on delete — which is affordable only because the rows are short-lived and the runtime caps a bundle at 64 MiB. |

## Email

These keys turn on outbound mail: the second-factor code by email, the password-reset link and the admin app's account mail. They are empty by default, so mail is off and those features stay hidden until a provider is fully configured. See [Email Services](../architecture/services/infrastructure/email-services.md).

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `EmailSettings:Provider` | `""` | `Smtp`, `SendGrid` or `Ses`. Empty turns mail off. |
| `SendGrid:ApiKey` | `<sendgrid-api-key>` | SendGrid API key. Required for `SendGrid`. |
| `SendGrid:FromEmail` | `<from@your-domain>` | Sender address. Required for `SendGrid`. |
| `SendGrid:FromName` | `<sender-name>` | Sender display name. |
| `Ses:FromEmail` | `<from@your-domain>` | Sender address. Required for `Ses`. Its domain must be a verified Amazon SES identity in the host's AWS region. `Ses` has no key: it sends through the SES API with the host's own AWS credentials, whose role needs `ses:SendEmail` on that identity. |
| `Ses:FromName` | `<sender-name>` | Sender display name. |
| `Smtp:Host` | `<smtp-host>` | SMTP server host. Required for `Smtp`. |
| `Smtp:FromEmail` | `<from@your-domain>` | Sender address. Required for `Smtp`. |
| `Smtp:FromName` | `<sender-name>` | Sender display name. |
| `Smtp:Port` | `587` | SMTP port. |
| `Smtp:EnableSsl` | `true` | Use TLS on the SMTP connection. |
| `Smtp:Username` | `<smtp-username>` | SMTP username. |
| `Smtp:Password` | `<smtp-password>` | SMTP password. |
| `FrontendBaseUrl` | `<https://app.your-domain>` | The analyst app's address. A reset mail links to its `/reset-password` page; without it no reset mail is sent and the API logs an error. |
| `AdminBaseUrl` | `<https://admin.your-domain>` | The admin app's public address, where every link to it that the apps mail starts: the admin app's password reset and email confirmation, and the help request notices to the support inbox. Neither app builds a mailed link from the request's host, which the sender chooses. Without an absolute `http` or `https` address the admin app mails no link, and its **Forgot your password?** page says that reset by mail is off. |

## Data protection

ASP.NET Data Protection keys can be stored in the database (default), Azure Key Vault, or AWS SSM Parameter Store.

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `DataProtection:Provider` | `database` | `database`, `azurekeyvault` (or `azure`), or `awsssm` (or `aws`), case-insensitive. |
| `DataProtection:AzureKeyVault:KeyUri` | `<https://your-vault.vault.azure.net/keys/...>` | Key Vault key URI. |
| `DataProtection:AwsSsm:ParameterPath` | `/iniaai/dataprotection/keys` | SSM parameter path prefix. |
| `DataProtection:AwsSsm:KmsKeyId` | `<kms-key-id>` | KMS key used to encrypt the protection keys. |

## Logging (Serilog)

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `Serilog:MinimumLevel:Default` | `Information` | Default minimum log level. |
| `Serilog:WriteTo` | (sinks) | Console + rolling File in all modes (the cloud overlays configure the same sinks); log shipping to CloudWatch is handled by the Fluent Bit sidecar, not a Serilog sink. |

## CORS

| Key | Placeholder | Description |
|-----|-------------|-------------|
| `Cors:AllowedOrigins` | `[]` | API only. Origins allowed to call the API from a browser, as [indexed keys](#how-configuration-is-layered). Empty (the default) allows no cross-origin call. A `*` entry stops startup with a message that names the key, and so does a list set as one value (`Cors__AllowedOrigins=...`), which would bind to an empty list. |

The deployed analyst app calls the API on its own origin through its nginx, so a standard deployment lists no origin. The local launch profiles list the Vue dev server, `https://localhost:8080`. The admin app has no CORS policy: nothing calls it from another origin.

## Response headers of the API

The API sets these headers on every answer, including a refusal, a rate-limit answer, a preflight, a failure and an unknown path (`SecurityHeadersConfiguration`, registered first after the forwarded headers). No setting changes them.

| Header | Value |
|---|---|
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'`. An answer that sets its own policy keeps it: a file preview sends `sandbox`. The Swagger UI, served on a developer machine only, gets none. |
| `Cache-Control` | `no-store`, unless the answer sets its own rule: a page image sends `private, max-age=3600`. |

The API sends no `X-XSS-Protection` header: the OWASP HTTP Headers Cheat Sheet advises against it. Behind the web app's nginx, the API's answers also carry nginx's own policy; a browser applies both.

## Other keys

The other configuration sections the apps read. A section with a home elsewhere is documented there; this table names it and links to it.

| Section or key | Default | What it sets |
|---|---|---|
| `PlatformSettings:Anthropic:*`, `PlatformSettings:Cohere:*` (tuning) | see the page | Model ids, temperatures, token caps, retry budgets and rate limits of the chat and embeddings connectors: [AI providers & connectors](../architecture/ai-integration/providers.md#where-models-temperatures-and-retries-are-configured). |
| `PlatformSettings:AzureFoundry:*` | empty | The Azure-mode Claude and Cohere endpoints and keys: [Azure AI Foundry Integration](../architecture/ai-integration/azure-foundry.md). |
| `PlatformSettings:UsesModelGateway` | `false` | The open build's name for the gateway switch. No closed module supplies a gateway there, so a host that turns it on stops at startup and says so. |
| `PlatformSettings:OcrSettings:*` | see the page | OCR parallelism, the output-token cap and the transcription model: [OCR](../architecture/ocr/index.md). |
| `PlatformSettings:MaxBlockParallelism` | `4` | How many blocks of one workflow run at once: [Parallel Execution](../architecture/workflow-engine/parallel-execution.md). |
| `PlatformSettings:BlockExtensions` | empty | Assemblies that add block types, as paths relative to the application folder or absolute ones. Each host loads them at startup and calls every public `IBlockExtension` in them; an assembly with none stops startup with a message that names it. Set the same list on the API and the web app: [A block type in its own project](../architecture/workflow-engine/block-implementation-guide.md#a-block-type-in-its-own-project). |
| `PlatformSettings:WorkflowVersionRetention:*` | `Mode` `Disabled` | The sweep that clears old workflow-version payloads: [Workflow Template Versioning](../features/workflow-template-versioning.md). |
| `PlatformSettings:GitHub:*` | `Enabled` `false` | Saving workflows to a GitHub repository: [Workflow GitHub Save](../features/workflow-github-save.md). `CommitterName` and `CommitterEmail` name the committer of every version, `Workflow versioning` and `workflow-versioning@example.com` unless set; `ApiAuthorName` and `ApiAuthorEmail` name the author of a version saved through the template editing API, `Workflow API` and `workflow-api@example.com` unless set. |
| `PlatformSettings:ApiDocsContactEmail` | not set | The support address the API documentation (Swagger) names. Not set, the documentation names no contact. |
| `PlatformSettings:EnvironmentBanner:Enabled`, `Label`, `Color` | unset | The admin app's environment indicator. Unset, it shows on every environment but Production, labelled with the environment name; a `Label` or a `Color` (`#rgb` or `#rrggbb`) also shows it on Production, and `Enabled` forces it on or off (`EnvironmentIndicatorResolver`). |
| `PlatformSettings:IniaAi:MaxFileSizeMb` | `100` | The largest file an upload or an email attachment may be, and the per-entry cap of an expanded ZIP. |
| `PlatformSettings:IniaAi:MaxLoopItems` | not set | The most items one loop may run over. The engine counts the loop's list before it expands the loop, so a longer list fails the run before any block of the loop runs (`WorkflowHandler.ThrowIfLoopExceedsItemCap`). Not set, a loop has no item cap: [Loop Processing](../architecture/workflow-engine/loop-processing.md#validation-and-edge-cases). |
| `PlatformSettings:IniaAi:*` (other) | see `IniaAiSettings` | Document-processing tuning: `BatchSizeForEmbeddings` (`50`), `LlmMaxDegreeOfParallelism` (unset), `SummarizationModelType` (`Basic`), `MaxRetriesForTopicsGeneration` (`3`), `NoDataForPlaceHolderText` (`No data available`) and `WordCountPerPage` (`250`). |
| `PlatformSettings:ChartRendering:TempDirectory`, `DebugPreserveTempFiles` | unset, `false` | Where the server-side chart render writes its temporary files, and whether it keeps them for debugging (`ChartRenderingSettings`). The render always draws each chart as a picture; the former `UseHighchartsRendering`, `HighchartsModules`, `DefaultChartWidth`, `DefaultChartHeight` and `PostRenderDelayMs` keys are ignored when a deployment still sets them. |
| `PlatformSettings:PromptTemplates:UserPromptPreviewSystemPrompt` | built-in prompt | The system prompt of the privacy-safe prompt preview in the admin app's workflow editor (`PromptTemplateSettings`), the section's only key. |
| `Swagger:TokenUrl` | `/api/v1/connect/token` | The token endpoint the API's Swagger UI signs in at. |




## Related

- [AI providers & connectors](../architecture/ai-integration/providers.md) — how the active chat/embeddings provider is selected and where model tunables live (incl. the [valid `AiModel` enum → model-ID table](../architecture/ai-integration/providers.md#valid-aimodel-enum-values))
- [Data Flows & Subprocessors](../compliance/data-flows-and-subprocessors.md) — what content these provider keys cause to leave the deployment, and the retention posture of each
- [Azure AI Foundry Integration](../architecture/ai-integration/azure-foundry.md) — the Azure-mode configuration parallel
- [Database Migrations](../getting-started/database-migrations.md) — applying schema to the configured databases

