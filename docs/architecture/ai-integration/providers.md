---
type: explanation
status: current
description: "How the platform reaches Claude and Cohere: the chat and embeddings connector chosen per deployment, the auth per backend, the AiModel ids, model routing per stack, thinking and effort request shapes, refusals, rate limiting and retries. Read when you configure a deployment's models or provider, debug a model call that fails, or add a model to the catalogue."
---

# AI providers & connectors

**On this page:** Deployment model drives provider selection · The two abstractions · Chat providers · Authentication per backend · Embeddings provider (Cohere) · Where models, temperatures, and retries are configured · Valid `AiModel` enum values · Thinking mode · Prompt caching · Token rate limiting · Model refusals · Transient-failure handling · Key paths · Related


The `IniaAi.AiConnection` project is the single boundary between the platform and external AI services. Everything above it talks to two interfaces only:

- `IChatConnector` — text generation (Claude).
- `IEmbeddingsConnector` — vector embeddings for the RAG system (Cohere).

There is **one chat model family (Anthropic Claude)** and **one embeddings family (Cohere)**. There is **no OpenAI / GPT provider** anywhere in the codebase. "OpenAI" appears only in comments: the Azure embeddings response uses an OpenAI-compatible JSON shape (`Azure/ApiModel/EmbeddingsResponse.cs`), and the pricing enums (`PricingUnit`, `PricingComponentType`) describe OpenAI billing units. The `GptEncoding` in `AnthropicChatConnector` is the SharpToken `cl100k_base` tokenizer that estimates input tokens, not a model. Do not document or assume a GPT path.

What varies is the **backend** a connector reaches and the **auth** it uses. Both are chosen once at startup from `PlatformSettings`, never per request.

## Deployment model drives provider selection

The platform is **not a single hosted SaaS**. It is deployed on-prem or inside the customer's own cloud, and every provider is reached with **customer-owned credentials** (the customer's Bedrock IAM role, their Entra ID tenant, their direct Anthropic key, or their own model gateway).

Because of that, the active connector is decided by **where the process is running**. `PlatformSettings.DeploymentMode` names the mode once (`CloudHost.ModeFor`), and `AddModelConnectors` (`platform/IniaAi.Infrastructure/InfrastructureServiceRegistration.cs`), which both hosts call, registers that mode's connectors:

| Deployment mode | Chat connector | Embeddings connector |
|------------------|----------------|----------------------|
| `Gateway` (`UsesModelGateway == true`) | `AnthropicChatConnector` through the gateway a closed module registers | the module's own embeddings connector |
| `Azure` (`IsRunningInAzure == true`) | `AnthropicChatConnector` (Azure AI Foundry) | `AzureCohereEmbeddingsConnector` |
| `Aws` (`IsRunningInAws == true`) | `AwsBedrockChatProvider` (native Bedrock) | `CohereAwsEmbeddingsConnector` |
| `DirectApi` (otherwise) | `AnthropicChatConnector` (Anthropic public API) | `CohereEmbeddingsConnector` |

The flags come from `PlatformSettings` (`platform/IniaAi.Configuration/Platform/PlatformSettings.cs`), and the two cloud flags from `CloudHost` (`platform/IniaAi.Configuration/Platform/CloudHost.cs`):

- `UsesModelGateway` — an explicit config bool, the key `PlatformSettings:UsesModelGateway`. A host that sets it without a closed module that supplies a gateway stops at startup with `PlatformSettings:UsesModelGateway is on, but this build has no model gateway. …`.
- `IsRunningInAzure` — `true` only when the env var `FORCE_AZURE_MODE=true` (`CloudHost.IsAzure`).
- `IsRunningInAws` — `true` if `FORCE_AWS_MODE=true`, `false` if `FORCE_AWS_MODE=false`; otherwise `true` when the process is not in Azure mode and EC2 instance metadata answers (`CloudHost.IsAws`). The metadata lookup runs at most once per process, and never when a mode is named.

`CloudHost.ModeFor` checks the flags in the table's order, and for a gateway host it never asks which cloud the host runs in.

The gateway mode belongs to the paid build. A closed module registers the gateway and its embeddings connector before `AddModelConnectors` registers the chat connector. Shared code reaches the gateway only through `IModelGateway` (`platform/IniaAi.AiConnection/Abstractions/IModelGateway.cs`).


`AddModelConnectors` registers the embeddings connector for the same `DeploymentMode`; in the gateway mode the module registers its own. Tests build one through `EmbeddingsConnectorFactory.Create` (`platform/IniaAi.AiConnection/Cohere/EmbeddingsConnectorFactory.cs`), which refuses the gateway mode.

## The two abstractions

### IChatConnector

`platform/IniaAi.AiConnection/Abstractions/IChatConnector.cs`. All chat goes through these methods (`GetResponseAsync` also has an overload that takes a plain `string userInput`):

```csharp
Task<ResponseContentSequence> GetResponseAsync(
    RequestContentSequence content,
    string? systemPrompt = null,
    int maxTokens = -1,
    bool shouldUseAdvancedModel = true,
    string? model = null,
    ModelFamily? family = null,
    ModelEffort? effort = null,
    CancellationToken cancellationToken = default);

Task<ResponseContentSequence> GetResponseWithCachingAsync(
    RequestContentSequence content,
    RequestContentSequence cachedContent,
    /* ... */);

Task<ResponseContentSequence> GetThinkingResponseAsync(
    RequestContentSequence content,
    /* ... */
    int thinkingBudget = -1,
    /* ... */);
```

`shouldUseAdvancedModel` selects between the configured advanced and basic Claude models; passing an explicit `model` string overrides that. Errors are returned in-band as a content item prefixed with `IChatConnector.ERROR_PREFIX` (`"ERROR: "`), not thrown. `GetResponseWithCachingAsync` on `AnthropicChatConnector` always uses the advanced tier, whatever `shouldUseAdvancedModel` says.

Two more members answer questions about a call without making it: `GetModelId` returns the model id a call with these arguments would send, in this deployment's form, and `GetVisionTier` returns the largest image that model reads without downscaling it (the [OCR render phase](../ocr/index.md#models) sizes pages with it). Both throw like a call would when the named model is one the deployment cannot call.

The two thinking-mode methods have **default interface implementations that throw `NotSupportedException`** (`GetThinkingResponseAsync` and `GetThinkingResponseWithCachingAsync` in `IChatConnector.cs`). Both chat connectors override them.

### IEmbeddingsConnector

`platform/IniaAi.AiConnection/Abstractions/IEmbeddingsConnector.cs`. Embeddings expose the model identity (for pricing lookup), a running token counter, and single- or batch-document calls:

```csharp
string ModelName { get; }   // e.g. "Cohere: EmbedV40"
string ModelId { get; }     // normalized id matching ModelPricing
int TotalTokens { get; }

Task<List<double[]>> GetResponseAsync(string document, CancellationToken cancellationToken = default);
Task<List<double[]>> GetResponseAsync(List<string> documents, CancellationToken cancellationToken = default);
```

## Chat providers

### AnthropicChatConnector (public API / model gateway / Azure AI Foundry)

`platform/IniaAi.AiConnection/Anthropic/AnthropicChatConnector.cs` is a **single connector that targets three different Claude backends**, choosing one in its constructor. It always speaks the Anthropic Messages API shape; only the URL, version header, and auth differ.

1. **Model gateway** (`DeploymentMode.Gateway`): the `IModelGateway` that a closed module registers supplies the URL, the headers that authenticate each call, the read timeouts and whether calls skip the system proxy. The API version is forced to `bedrock-2023-05-31`, and requests are serialized as a Bedrock `InvokeModel` body that the gateway wraps in its own request format (`IModelGateway.WrapRequest`). The HTTP client disables the system proxy when the gateway asks, so internal endpoints are not routed through a corporate proxy (`CreateHttpClient` in `AnthropicChatConnector.cs`). The gateway reads a refusal in its own error format (`IModelGateway.ReadErrorText`) and keeps the gateway's error type in the `ERROR:` text.


2. **Azure AI Foundry** (`IsRunningInAzure`): URL from `AzureFoundry.AnthropicApiUrl`, API version from `Anthropic.ApiVersion`. Auth is an Entra ID bearer token (`UseEntraId`) or a static `AzureFoundry.AnthropicApiKey`. Azure API Management error bodies are parsed into actionable messages by `TryParseAzureApimError`, which `ReadErrorText` calls for a body that names no error type (both in `AnthropicChatConnector.cs`).
3. **Anthropic public API** (default): URL from `Anthropic.ApiUrl` (default `https://api.anthropic.com/v1/messages`), the `Anthropic.ApiKey` key, and the `anthropic-version` header from `Anthropic.ApiVersion` (default `2023-06-01`). Startup fails with `Anthropic API key not set.` when the key is missing.

### AwsBedrockChatProvider (native AWS Bedrock)

`platform/IniaAi.AiConnection/AwsBedrock/AwsBedrockChatProvider.cs` is used on native AWS deployments. It calls Bedrock through the AWS SDK (`AmazonBedrockRuntimeClient.InvokeModelAsync`) with **SigV4 request signing**, and wraps each call in a **Polly resilience pipeline** (`BedrockResiliencePipelineFactory.Create`). The pipeline retries with exponential backoff on throttling, service-unavailable, internal-server, model-timeout and model-stream errors, on HTTP request failures and timeouts, and on any Bedrock exception with status 429, 500, 502, 503, 504 or 529. It allows the larger of `Anthropic.MaxRateLimitRetryAttempts` and `Anthropic.MaxServiceUnavailableRetryAttempts`. Crucially the retry runs **above** the SDK signing layer: the `InvokeModelRequest` is built inside the retried delegate in `GetResponseInternalAsync`, so every attempt carries a **fresh SigV4 signature**. AWS signatures expire after 5 minutes and Bedrock inference can take minutes. The Bedrock HTTP client's timeout is `Anthropic.HttpTimeoutMinutes` (`BedrockHttpClientFactory`).

Bedrock model dispatch goes through `IModelFormatAdapter`; the constructor registers only the `Anthropic` family adapter (`AnthropicFormatAdapter`), so the provider serves Claude on Bedrock.

Native Bedrock mode needs almost no `appsettings.json` — set `FORCE_AWS_MODE=true` and the Bedrock-hosted model tier; the provider holds no key and authenticates with the ambient AWS credential chain:

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

See the [Minimal Bedrock configuration](../../operations/application-configuration.md#minimal-bedrock-configuration) reference for the settings. For pointing a self-hosted deployment at a **customer's own** Bedrock (the IAM principal and credential chain that signing relies on, region, console model-access enablement) and a copy-paste **post-deploy AI smoke test**, see [Customer-Owned Bedrock Setup](../../operations/customer-bedrock-setup.md).


## Authentication per backend

| Backend | Auth mechanism | Header(s) | Source |
|---------|----------------|-----------|--------|
| Anthropic public API | Static API key | `X-Api-Key`, `anthropic-version` | `Anthropic.ApiKey` |
| AWS Bedrock (native) | AWS IAM (SigV4 signing) | SDK-signed | ambient AWS credentials / instance role |
| Model gateway | the gateway's own | the headers `IModelGateway.AddCallHeadersAsync` adds per call | the closed module's settings |
| Azure AI Foundry | Entra ID bearer token *or* static key | `Authorization: Bearer …` or `X-Api-Key` | `IAzureTokenProvider` or `AzureFoundry.AnthropicApiKey` |


### Anthropic key

The simplest path: `Anthropic.ApiKey` is sent on every request in the `X-Api-Key` default header (`CreateHttpClient` in `AnthropicChatConnector.cs`).

### Bedrock IAM

The native Bedrock provider holds no key. It relies on the AWS SDK credential chain (instance role / environment), and the SDK signs each `InvokeModelRequest` with SigV4. This is why retries must re-create the request rather than replay an HTTP message.


### Azure Foundry Entra ID

When `AzureFoundry.UseEntraId` is true, an `IAzureTokenProvider` (`DefaultAzureTokenProvider`, backed by `DefaultAzureCredential`) is registered; otherwise `NullAzureTokenProvider` is used and the static `AzureFoundry.AnthropicApiKey` applies (the `IsRunningInAzure` branch of `ApplicationServicesConfiguration.AddSingletonServices`). The Entra path acquires a bearer token for the Cognitive Services scope (`https://cognitiveservices.azure.com/.default`) and sends it as `Authorization: Bearer …` per request (`Scopes` in `Azure/DefaultAzureTokenProvider.cs`, the internal `GetResponseAsync` in `AnthropicChatConnector.cs`). The SDK caches and refreshes the token internally.

## Embeddings provider (Cohere)

Embeddings are always Cohere; `AddModelConnectors` picks the concrete connector by deployment, reusing the chat backend's auth model:

- `AzureCohereEmbeddingsConnector` — Azure AI Foundry; Entra ID (`DefaultAzureTokenProvider`) when `UseEntraId`, else `NullAzureTokenProvider` + static key. The Azure embeddings endpoint uses the Azure AI Model Inference API and needs a `CohereDeploymentName` (the customer-chosen deployment name).
- `CohereAwsEmbeddingsConnector` — Cohere on AWS Bedrock.
- `CohereEmbeddingsConnector` — direct Cohere public API (`https://api.cohere.com/v2/embed`).

(`EmbeddingsConnectorFactory.Create`.) In the gateway mode the closed module registers its own embeddings connector.


## Where models, temperatures, and retries are configured

All tunables live in strongly-typed settings under `platform/IniaAi.Configuration/LLMProviders/`, hung off `PlatformSettings`. Connectors read these; they do not hard-code models.

### AnthropicSettings (`AnthropicSettings.cs`)

| Setting | Default | Meaning |
|---------|---------|---------|
| `AdvancedModel` | `PublicSonnet5` | model used when `shouldUseAdvancedModel` is true |
| `BasicModel` | `PublicHaiku5_5` | model used when `shouldUseAdvancedModel` is false |
| `AdvancedModelIdOverride` / `BasicModelIdOverride` | `null` | raw model-id override applied without changing the enum |
| `AdvancedModelTemperature` / `BasicModelTemperature` | `0.2` | sampling temperature per tier |
| `AdvancedModelMaxTokens` / `BasicModelMaxTokens` | `64000` / `8192` | output cap for a model whose `AiModel` entry has no `[MaxTokens]`; every current entry has one, so the entry's cap applies. `AwsBedrockChatProvider` uses `AdvancedModelMaxTokens` for a model id it cannot parse |
| `MaxRateLimitRetryAttempts` | `10` | retries on HTTP 429 |
| `MaxServiceUnavailableRetryAttempts` | `10` | retries on HTTP 503 |
| `MaxParallelCalls` | `5` | concurrent-call ceiling (concurrency semaphore) |
| `MaxInputTokensPerWindow` | `1000000` | token budget for the sliding-window limiter |
| `ApiUrl` / `ApiVersion` | `https://api.anthropic.com/v1/messages` / `2023-06-01` | endpoint and `anthropic-version` |
| `HttpTimeoutMinutes` | `10` | HTTP timeout of the Bedrock client. `AnthropicChatConnector` uses a fixed 10-minute timeout and does not read it |

These same settings feed **both** Claude connectors — `AnthropicChatConnector` and `AwsBedrockChatProvider` both resolve their model and temperature from `Anthropic.*` (`GetSettingsModel` and `DetermineTemperature` in `AwsBedrockChatProvider.cs`).

### CohereSettings (`CohereSettings.cs`)

| Setting | Default | Meaning |
|---------|---------|---------|
| `EmbeddingsModel` | `AwsEmbedV40` | embeddings model |
| `EmbeddingsOutputDimension` | `1024` | Matryoshka output dimension (256/512/1024/1536) for v4+ |
| `EmbeddingsApiUrl` | `https://api.cohere.com/v2/embed` | direct Cohere endpoint |
| `MaxRateLimitRetryAttempts` / `MaxServiceUnavailableRetryAttempts` | `3` / `5` | embeddings retry budget |

### AzureFoundrySettings (`AzureFoundrySettings.cs`)

Holds **only** endpoints, keys, and the auth toggle — `AnthropicApiUrl`, `AnthropicApiKey`, `CohereApiUrl`, `CohereApiKey`, `CohereDeploymentName`, and `UseEntraId`. Model choice, temperatures, and retries stay in `AnthropicSettings` / `CohereSettings`; the Foundry class does not duplicate them.


## Valid `AiModel` enum values

`Anthropic.AdvancedModel` / `Anthropic.BasicModel` (and the Cohere `EmbeddingsModel`) are **enum names**, not raw model IDs. The settings above use names like `PublicSonnet5` or `UsAwsHaiku5_5`; the table below is the key that maps each name to the **model-ID string** the connector actually sends — the same string you match against an AWS Bedrock inference-profile ID, an Azure deployment, or an Anthropic public model name on a quota dashboard.

The enum lives in `platform/IniaAi.Configuration/Enums/Anthropic/AiModel.cs`. Each entry carries:

- an `[EnumMember(Value = …)]` — the model-ID string sent to the backend,
- a `[MaxTokens(…)]` — the default output cap,
- an `[ActiveEnumEntry]` flag — whether the value is surfaced as **selectable** in the AI Query block's model dropdown (`AiQueryModelChoices`). Only the plain `claude-*` entries are active, one per current model, labelled "Claude Opus 5.5" and so on, in the `[VisualOrder]` tier and version order. The block stores the plain name, and the stack resolves it to its own id when the block runs (see [Block model choice resolves per stack](#block-model-choice-resolves-per-stack)). Every other value remains a valid enum usable via explicit config or `AdvancedModelIdOverride` / `BasicModelIdOverride`, but is not offered in the UI.
- newer models additionally carry `[AdaptiveThinkingModel]` — see [Adaptive-generation request shape](#adaptive-generation-request-shape) below.

The right deployment mode determines which IDs are usable: **AWS Bedrock** requires the region-prefixed inference-profile IDs (`us.` / `eu.` / `global.` / vendor-prefixed); **direct Anthropic public API** and **Azure AI Foundry** use the plain `claude-*` model names (on Azure, the value may instead be the model's deployment name).

### Adaptive-generation request shape

Models of the adaptive-thinking API generation (Opus 4.8, Opus 5, Opus 5.5, Sonnet 5, Sonnet 5.5, Haiku 5.5, Fable 5 — marked `[AdaptiveThinkingModel]` on the enum) reject two things the older request shape always sent, so both connectors (`AnthropicChatConnector` and `AwsBedrockChatProvider` via `AnthropicFormatAdapter`) special-case them:

- **No sampling parameters** — non-default `temperature` / `top_p` / `top_k` return a 400 (`ValidationException: 'temperature' is deprecated for this model`). The connectors omit `temperature` entirely for these models; `AdvancedModelTemperature` / `BasicModelTemperature` settings are ignored for them.
- **No `budget_tokens`** — thinking requests send `thinking: {"type": "adaptive", "display": "summarized"}` instead of `{"type": "enabled", "budget_tokens": N}`; the AI Query block's thinking-budget property is ignored for these models. Non-thinking requests send `thinking: {"type": "disabled"}` (Sonnet 5, Opus 5 and Haiku 5.5 otherwise think by default). Sonnet 5.5 rejects `disabled`, so its non-thinking requests send `thinking: {"type": "between_tools"}` with no other field (`isThinkingOffBetweenTools`). Fable 5 and Opus 5.5 cannot disable thinking at any effort (`isThinkingAlwaysOn`) — requests omit the `thinking` field and the model decides.
- **`output_config.effort`** — these models additionally accept `output_config: {"effort": "low" | "medium" | "high" | "xhigh" | "max"}` (API default `high`, except Opus 5.5 and Haiku 5.5, whose default is `medium` — the enum's `defaultEffort`), scaling how eagerly the model spends tokens across thinking, tool use, and output. The AI Query block's Effort property (`AiQueryProperties.Effort`, nullable = unset) feeds it; `ModelRequestShaper` writes it, omits the field when unset, and never emits it for non-adaptive models — models that predate `output_config` reject it. The one other writer is the low-effort resend below, which runs only for adaptive models. Applies whether thinking is on or off.
- **Opus 5, Sonnet 5.5 and Haiku 5.5 cap effort when thinking is off** — Opus 5 and Haiku 5.5 accept `thinking: {"type": "disabled"}`, and Sonnet 5.5 accepts `thinking: {"type": "between_tools"}`, only at effort `high` or below; all three return a 400 above it. The enum records the limit (`maxEffortWithoutThinking`), and `ModelRequestShaper` lowers a higher effort to `high` on a non-thinking request rather than send a request the model refuses. The editor shows the cap under the Effort dropdown.
- **Thinking that fills the whole output is sent again at low effort** — an adaptive model has no thinking budget, so at its default effort it can spend all of `max_tokens` on thinking and stop with no answer. The response then holds only the thinking text. When a request to an adaptive model stops at `max_tokens` with no answer text, both connectors send it once more at effort `low` (`ThinkingExhaustion`). The check sits on each connector's one send path, so it also covers a model that always thinks, such as Opus 5.5, on a request without thinking mode. This is Anthropic's documented remedy for an over-thought request ([Steering thinking: cost control](https://platform.claude.com/docs/en/build-with-claude/thinking-steering-and-cost#cost-control)). The returned response reports the tokens of both calls, so cost tracking counts the spent first call. A request already at `low`, a response with answer text, and a model that takes a thinking budget are not sent again. A Warning log names the model, the tokens spent and the effort. The resend pays for a new cache write, because a change of effort invalidates the prompt cache.
- **No forced tool choice on Opus 5.5** — Opus 5.5 returns a 400 on `tool_choice` `any` or `tool`. The platform's connectors send no `tool_choice` at all.
- **Refusals** — Fable 5 carries safety classifiers and can decline a request with `stop_reason: "refusal"` on an HTTP 200. A refusal with no content surfaces as an `ERROR: `-prefixed response.

**Fable 5 is not selectable by default:** Bedrock rejects Fable 5 invocations until the deployment account opts into Anthropic's 30-day data retention (`aws bedrock put-account-data-retention --mode provider_data_share`). After the opt-in, activate it by adding `[ActiveEnumEntry]` + `[VisualOrder]` to `PublicFable5`. The dropdown is the same on every deployment, so check that each stack that shows the choice can call Fable 5.

**Override strings that are not `AiModel` values:** when `AdvancedModelIdOverride`/`BasicModelIdOverride` is a raw string that doesn't map to an `AiModel` entry — e.g. an Azure Foundry deployment name — the request is sent to that model but the request shape falls back to the configured `AdvancedModel`/`BasicModel` enum. Keep those enum settings in sync with the model the deployment actually serves, or an adaptive-generation deployment behind a legacy-shaped request will return 400s. `AnthropicChatConnector` (direct API, Azure, gateway) also lets a settings override replace any model a call names, for every call of that tier: the named model is not resolved, and the request is shaped for the configured `AdvancedModel`/`BasicModel`. `AwsBedrockChatProvider` does not read these overrides.

### Block model choice resolves per stack

An AI Query block stores a model id in `ModelIdOverride`. `ModelRouteResolver` turns every model id a call names into the id the running stack calls, in `AnthropicChatConnector` (direct API, Azure, gateway) and in `AwsBedrockChatProvider`. The named ids come from AI Query blocks, from `ModelRouting:TenantModelOverrides`, and from signature-prompt replays of an old run. The stack's route is the id prefix of its configured `AdvancedModel` or `BasicModel`, so a block's route follows its **Use Advanced Model** setting.

- A plain name on a Bedrock stack runs as the same model under the stack's own prefix. On a stack configured with `UsAwsSonnet4_6`, `claude-opus-5-5` runs as `us.anthropic.claude-opus-5-5`.
- A Bedrock id on the direct API or Azure runs as the plain name. `us.anthropic.claude-opus-4-8` runs as `claude-opus-4-8`. Blocks saved with a Bedrock id before the plain-name dropdown therefore run on every stack that has the model.
- A Bedrock id of another region family runs under the stack's own family on an AWS Bedrock stack (`AwsBedrockChatProvider`): `us.anthropic.claude-sonnet-5-5` runs as `eu.anthropic.claude-sonnet-5-5` on a stack configured with `eu.` models, and a stored `global.` id runs as its `eu.` form there, never as `global.`. The stack's family always wins, in every direction. Nothing stored is rewritten; the mapping happens at call time, so a workflow exported from a `us.` stack runs on an `eu.` stack as it is. A Bedrock id on the stack's own family is unchanged.
- `AnthropicChatConnector` (direct API, Azure, and a model gateway, whose configured model is in Bedrock form) does not remap a region: a stored Bedrock id there resolves as before.
- An id that is not an `AiModel` value, or a model that the stack's route has no entry for, fails the block with `InvalidOperationException`: "Model '…' is not a model this deployment can call. Choose a model from the block's model list." On an AWS Bedrock stack, a known model that the stack's family does not offer fails instead with "Model 'us.anthropic.claude-haiku-4-5-20251001-v1:0' (Claude Haiku 4.5) is not offered in this deployment's region. Choose another model from the block's model list." Before this change, the direct API and Azure ran such a block on the default model without a sign.

On Azure the resolved plain name must also be a Foundry deployment name. A model without a deployment fails at Foundry, not at the resolver. A Bedrock stack needs the profile under its own prefix: the catalogue has no `global.` entry for Haiku 4.5, so a `global.` stack cannot run Claude Haiku 4.5 and gives the region message above. The catalogue's `eu.anthropic.claude-fable-5` entry has no profile in London (eu-west-2), which lists Fable only as `global.`: a Fable block on London fails at Bedrock rather than with that message.

When a block names its own model, its run log row shows the id the call was sent to, read from the response (`AiQueryPropertiesExecutionState.ModelCalled`), so a remapped call shows its `eu.` id; a block without its own model still shows **Advanced Model** or **Simple Model**. The application log line reads "Using explicitly specified model: … (requested as …)" with the stored id beside it.

A workflow exported from an instance on this version stores plain names. On an AWS instance that predates the resolver, those names reach Bedrock unchanged and fail, so upgrade an instance before it imports such a workflow.

### Bedrock-mode models (region-prefixed inference profiles)

| Enum name | Model ID (`EnumMember`) | Max output tokens | Selectable |
|-----------|-------------------------|-------------------|------------|
| `UsAwsHaiku4_5` | `us.anthropic.claude-haiku-4-5-20251001-v1:0` | 64000 | — (plain name resolves here) |
| `UsAwsHaiku5_5` | `us.anthropic.claude-haiku-5-5` | 128000 | — (plain name resolves here) |
| `UsAwsSonnet4_6` | `us.anthropic.claude-sonnet-4-6` | 64000 | — (plain name resolves here) |
| `UsAwsSonnet5` | `us.anthropic.claude-sonnet-5` | 128000 | — (plain name resolves here) |
| `UsAwsSonnet5_5` | `us.anthropic.claude-sonnet-5-5` | 128000 | — (plain name resolves here) |
| `UsAwsOpus4_8` | `us.anthropic.claude-opus-4-8` | 128000 | — (plain name resolves here) |
| `UsAwsOpus5` | `us.anthropic.claude-opus-5` | 128000 | — (plain name resolves here) |
| `UsAwsOpus5_5` | `us.anthropic.claude-opus-5-5` | 128000 | — (plain name resolves here) |
| `UsAwsFable5` | `us.anthropic.claude-fable-5` | 128000 | — (retention opt-in) |
| `UsAwsSonnet4_5` | `us.anthropic.claude-sonnet-4-5-20250929-v1:0` | 64000 | — (retired) |
| `UsAwsOpus4_5` | `us.anthropic.claude-opus-4-5-20251101-v1:0` | 32000 | — (retired) |
| `UsAwsOpus4_6` | `us.anthropic.claude-opus-4-6-v1` | 128000 | — (retired) |
| `AwsHaiku4_5` | `anthropic.claude-haiku-4-5-20251001-v1:0` | 64000 | — |
| `EuAwsHaiku4_5` | `eu.anthropic.claude-haiku-4-5-20251001-v1:0` | 64000 | — |
| `AwsHaiku5_5` | `anthropic.claude-haiku-5-5` | 128000 | — |
| `EuAwsHaiku5_5` | `eu.anthropic.claude-haiku-5-5` | 128000 | — |
| `GlobalAwsHaiku5_5` | `global.anthropic.claude-haiku-5-5` | 128000 | — |
| `AwsSonnet4_5` | `claude-sonnet-4-5-20250929-v1:0` | 64000 | — |
| `EuAwsSonnet4_5` | `eu.anthropic.claude-sonnet-4-5-20250929-v1:0` | 64000 | — |
| `GlobalAwsSonnet4_5` | `global.anthropic.claude-sonnet-4-5-20250929-v1:0` | 64000 | — |
| `AwsSonnet4_6` | `anthropic.claude-sonnet-4-6` | 64000 | — |
| `EuAwsSonnet4_6` | `eu.anthropic.claude-sonnet-4-6` | 64000 | — |
| `GlobalAwsSonnet4_6` | `global.anthropic.claude-sonnet-4-6` | 64000 | — |
| `AwsSonnet5` | `anthropic.claude-sonnet-5` | 128000 | — |
| `EuAwsSonnet5` | `eu.anthropic.claude-sonnet-5` | 128000 | — |
| `GlobalAwsSonnet5` | `global.anthropic.claude-sonnet-5` | 128000 | — |
| `AwsSonnet5_5` | `anthropic.claude-sonnet-5-5` | 128000 | — |
| `EuAwsSonnet5_5` | `eu.anthropic.claude-sonnet-5-5` | 128000 | — |
| `GlobalAwsSonnet5_5` | `global.anthropic.claude-sonnet-5-5` | 128000 | — |
| `AwsOpus4_5` | `anthropic.claude-opus-4-5-20251101-v1:0` | 32000 | — |
| `EuAwsOpus4_5` | `eu.anthropic.claude-opus-4-5-20251101-v1:0` | 32000 | — |
| `AwsOpus4_6` | `anthropic.claude-opus-4-6-v1` | 128000 | — |
| `EuAwsOpus4_6` | `eu.anthropic.claude-opus-4-6-v1` | 128000 | — |
| `AwsOpus4_8` | `anthropic.claude-opus-4-8` | 128000 | — |
| `EuAwsOpus4_8` | `eu.anthropic.claude-opus-4-8` | 128000 | — |
| `GlobalAwsOpus4_8` | `global.anthropic.claude-opus-4-8` | 128000 | — |
| `AwsOpus5` | `anthropic.claude-opus-5` | 128000 | — |
| `EuAwsOpus5` | `eu.anthropic.claude-opus-5` | 128000 | — |
| `GlobalAwsOpus5` | `global.anthropic.claude-opus-5` | 128000 | — |
| `AwsOpus5_5` | `anthropic.claude-opus-5-5` | 128000 | — |
| `EuAwsOpus5_5` | `eu.anthropic.claude-opus-5-5` | 128000 | — |
| `GlobalAwsOpus5_5` | `global.anthropic.claude-opus-5-5` | 128000 | — |
| `AwsFable5` | `anthropic.claude-fable-5` | 128000 | — |
| `EuAwsFable5` | `eu.anthropic.claude-fable-5` | 128000 | — |
| `GlobalAwsFable5` | `global.anthropic.claude-fable-5` | 128000 | — |

### Direct Anthropic / Azure Foundry models (plain `claude-*` names)

| Enum name | Model ID (`EnumMember`) | Max output tokens | Selectable |
|-----------|-------------------------|-------------------|------------|
| `Haiku4_5` | `claude-haiku-4-5-20251001` | 64000 | yes |
| `PublicHaiku5_5` | `claude-haiku-5-5` | 128000 | yes |
| `PublicSonnet4_5` | `claude-sonnet-4-5-20250929` | 64000 | — |
| `PublicSonnet4_6` | `claude-sonnet-4-6` | 64000 | yes |
| `PublicSonnet5` | `claude-sonnet-5` | 128000 | yes |
| `PublicSonnet5_5` | `claude-sonnet-5-5` | 128000 | yes |
| `Opus4_5` | `claude-opus-4-5-20251101` | 32000 | — |
| `PublicOpus4_6` | `claude-opus-4-6` | 128000 | — |
| `PublicOpus4_8` | `claude-opus-4-8` | 128000 | yes |
| `PublicOpus5` | `claude-opus-5` | 128000 | yes |
| `PublicOpus5_5` | `claude-opus-5-5` | 128000 | yes |
| `PublicFable5` | `claude-fable-5` | 128000 | — |

### Cohere embeddings models (`platform/IniaAi.Configuration/Enums/Cohere/AiModel.cs`)

`Cohere.EmbeddingsModel` uses a separate enum. The deployment-relevant values:

| Enum name | Model ID (`EnumMember`) | Backend |
|-----------|-------------------------|---------|
| `EmbedV40` | `embed-v4.0` | Direct Cohere public API |
| `AwsEmbedV40` | `global.cohere.embed-v4:0` | AWS Bedrock (global profile) |
| `UsAwsEmbedV40` | `us.cohere.embed-v4:0` | AWS Bedrock (US profile) |

The `embed-*-v3.0` / `embed-*-v2.0` entries (and their `cohere.embed-*-v3` Bedrock equivalents) remain valid for older deployments but are superseded by v4. `EmbeddingsOutputDimension` (256 / 512 / 1024 / 1536) applies only to v4+.

> [!TIP]
> **Matching a model to a provider quota dashboard**
>
> A request that fails with `AccessDenied` or `model-not-found` on Bedrock almost always means the IAM principal lacks `bedrock:InvokeModel` on **that exact inference-profile ID** in the deployment region — copy the `EnumMember` value from the table above into the AWS console's model-access / quota screen to confirm. On Azure, match the plain `claude-*` name (or your deployment name) against the deployed Foundry models.

## Thinking mode

Extended reasoning is **Claude-only**. `GetThinkingResponseAsync` / `GetThinkingResponseWithCachingAsync` are implemented on `AnthropicChatConnector` and `AwsBedrockChatProvider`; every other connector inherits the default that throws `NotSupportedException`.

Behavioural rules enforced in code:

- Temperature is forced to `1` when budget-based thinking is enabled — both for the public, Azure and gateway paths (`ApplyThinkingMode` in `AnthropicChatConnector.cs`) and Bedrock (`GetThinkingResponseAsync` and `GetThinkingResponseWithCachingAsync` in `AwsBedrockChatProvider.cs`), per Anthropic's requirement. Adaptive-generation models get no temperature at all (see [Adaptive-generation request shape](#adaptive-generation-request-shape)).
- The thinking budget must be **≥ 1024 tokens** and **≤ `maxTokens`**, validated by `ValidateThinkingBudget` in `AnthropicChatConnector.cs`; an invalid budget is returned as an `ERROR:` content item, not thrown to the caller.
- When thinking is on for a thinking-capable model (`[ThinkingModel]` on the enum), the output cap becomes `120000`, or the caller's `maxTokens` when that is smaller (`DetermineMaxTokensToUse` in `AnthropicChatConnector.cs` and in `AnthropicFormatAdapter.cs`). A thinking budget below 1 becomes the default of `4000` tokens (the `ThinkingSettings` constructor in `Anthropic/ApiModel/ThinkingSettings.cs`).
- On the gateway path, enabling thinking, or calling a model that always thinks, switches the read timeout to the gateway's thinking-mode value (`IModelGateway.ReadTimeoutSeconds`) and turns on TCP keep-alive for the retry policy (`CreateRequestBody` and `SetRetryPolicyEnableTcpKeepAlive` in `AnthropicChatConnector.cs`).

## Prompt caching

`GetResponseWithCachingAsync` uses Anthropic's `cache_control` to mark reusable content. The cache has a 5-minute lifetime that refreshes on each hit (the doc comment on `IChatConnector.GetResponseWithCachingAsync`). Cache-creation and cache-read tokens are tracked separately on the connector and logged per call (the internal `GetResponseAsync` in `AnthropicChatConnector.cs`).

## Token rate limiting

Across every Claude connector, calls pass through `BaseChatConnector.SendRequestWithGuardsAsync` (`Abstractions/BaseChatConnector.cs`), which enforces two guards in order:

1. A **concurrency semaphore** sized to `Anthropic.MaxParallelCalls`, one per connector instance.
2. A **token sliding-window limiter** (`TokenSlidingWindowRateLimiter`).

The limiter is a singleton built with `MaxInputTokensPerWindow` over a **1-minute window** (registered at the end of `ApplicationServicesConfiguration.AddSingletonServices`). Each request estimates its input tokens (BPE encode plus structural/image/fudge overhead, `EstimateTokensCore` in `AnthropicChatConnector.cs`), and `WaitForPermissionAsync` blocks until the rolling sum fits the budget — throwing `TokenBudgetExhaustedException` if the server reports the budget exhausted with a reset more than 120 s out, or `TimeoutException` (`WaitForPermissionAsync timed out after 5 minutes waiting for available tokens.`) after 5 minutes of waiting (`WaitForPermissionAsync` in `Common/TokenSlidingWindowRateLimiter.cs`).

The estimate is corrected from reality: after each Anthropic response the connector reads the `anthropic-ratelimit-input-tokens-remaining`, `-reset`, and `-limit` headers and calls `UpdateServerLimits`, which hard-syncs the local window to the server's actual usage (the internal `GetResponseAsync` in `AnthropicChatConnector.cs`, `UpdateServerLimits` in `TokenSlidingWindowRateLimiter.cs`). Stale server state (older than 5 minutes) is discarded and the limiter falls back to the local window.

## Model refusals

A model can decline a request. Current Claude models answer HTTP 200 with `stop_reason: "refusal"` and a `stop_details` object whose `category` names the policy area (`cyber`, `bio`, `frontier_llm`, `reasoning_extraction`, `general_harms`; an open set, and it can be null). The classifier can fire before any output or after partial output. A prompt that asks the model to write out its reasoning in the answer, for example in `<reasoning>` tags, invites a `reasoning_extraction` refusal on Sonnet 5.5 and Opus 5.5.

Both connectors parse the response with `ResponseBody.FormatResponseContent`, so they behave the same:

- The response carries a `ModelRefusal` (`ResponseContentSequence.Refusal`) with the category (`unspecified` when the provider named none), the explanation, the provider's message id and the model.
- Any partial answer or thinking is dropped. The content is one error text, `ERROR: The model refused to answer (refusal category: …).`, so every caller that checks the `ERROR: ` prefix treats it as a failure and never as an answer.
- The token counts stay, so the refused request is billed like any other.
- The connector writes one Warning log line that starts with `Model refusal:`, with the category, model, message id and the provider's request id (`x-amzn-RequestId` on Bedrock, `request-id` or the gateway's id elsewhere).
- Nothing is retried, and no other model is tried.

A model can also refuse in plain words and end its turn normally, which no stop reason marks. `WrittenRefusal.IsRefusal` treats an answer as a written refusal only when the whole answer is at most 600 characters, has no digits, opens by declining the task ("I can't transcribe", "I'm not able to extract", "I'm sorry, but I can't"), and either declines to transcribe or gives a reason framed as the model's own: its guidelines or a content policy, a person's personal information, privacy reasons, not being comfortable. Words that a finance or KYC answer uses every day, such as passport, identity, personal, private or policy on their own, do not count, so "I can't identify the fund's valuation policy" and "I cannot extract the figure because the statement is marked private" are answers. Its category is `written`. Where the platform finds a written refusal (an AI Query block or an OCR page) it writes the same `Model refusal:` line.


What a refusal does to an AI Query block is on [AI Query: a refused block](../../guides/blocks/block-reference.md#a-refused-block); what it does to an OCR page is on [OCR per-page outcomes](../ocr/index.md#per-page-outcomes).

## Transient-failure handling

- **AnthropicChatConnector** uses `HttpHandling/HttpRetryHandler` driven by a `RetryPolicy` built from `Anthropic.MaxRateLimitRetryAttempts` / `MaxServiceUnavailableRetryAttempts`, honouring the `retry-after` header (the internal `GetResponseAsync` in `AnthropicChatConnector.cs`).
- **AwsBedrockChatProvider** uses the Polly pipeline described above so each retry re-signs (`GetResponseInternalAsync` in `AwsBedrockChatProvider.cs`). Non-retryable Bedrock exceptions are converted to `ERROR:` responses at once: `ERROR: AccessDenied: …`, `ERROR: ModelNotFound: …`, `ERROR: InvalidRequest: …`. A transient exception that outlives every retry is converted the same way: `ERROR: RateLimitExceeded: …`, `ERROR: ServiceUnavailable: …`, `ERROR: InternalServerError: …`, `ERROR: ModelTimeout: …`.

## Key paths

| Concern | Location |
|---------|----------|
| Abstractions | `platform/IniaAi.AiConnection/Abstractions/` |
| Anthropic connector (3 backends) | `platform/IniaAi.AiConnection/Anthropic/AnthropicChatConnector.cs` |
| Native AWS Bedrock chat | `platform/IniaAi.AiConnection/AwsBedrock/AwsBedrockChatProvider.cs` |
| Cohere embeddings + factory | `platform/IniaAi.AiConnection/Cohere/` |
| Azure Entra ID token provider | `platform/IniaAi.AiConnection/Azure/DefaultAzureTokenProvider.cs` |
| Sliding-window rate limiter | `platform/IniaAi.AiConnection/Common/TokenSlidingWindowRateLimiter.cs` |
| Connector DI selection | `platform/IniaAi.Infrastructure/InfrastructureServiceRegistration.cs` (`AddModelConnectors`); the mode in `platform/IniaAi.Configuration/Platform/CloudHost.cs` (`ModeFor`) |
| Settings classes | `platform/IniaAi.Configuration/LLMProviders/` |


## Related

- [AI Integration overview](index.md)
- [Application Configuration Reference](../../operations/application-configuration.md) — every provider key with placeholder values, plus the minimal Bedrock and Azure config blocks
- [Data Flows & Subprocessors](../../compliance/data-flows-and-subprocessors.md) — what content each backend receives and the retention/training posture per provider
- [Customer-Owned Bedrock Setup](../../operations/customer-bedrock-setup.md) — the IAM role, region and model access that native Bedrock mode signs with
- [Azure AI Foundry Integration](azure-foundry.md) — the Azure-mode configuration parallel


