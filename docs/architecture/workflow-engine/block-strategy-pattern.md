---
type: explanation
status: current
description: "Documents the IBlockStrategy interface, every BlockExecutionContext service and run field, how open and module strategies are registered, placeholder substitution and sanitization, the strategy catalog and the agent family, and how to test a strategy. Read when you write or change a block strategy, or need to know what a strategy can reach at run time."
---

# Block Strategy Pattern

**On this page:** The interface · BlockExecutionContext service reference · Registration · Execution shape · Placeholder substitution & sanitization · Strategy catalog · Testing strategies · Related Documentation

Every workflow block type maps to a single **strategy** that knows how to execute it. The workflow engine looks up the strategy for a block's `BlockTypes` value and runs it, so adding behaviour for a new block type means adding a strategy rather than branching inside the engine.

## The interface

Strategies implement `IBlockStrategy` (`platform/IniaAi.Core/Workflows/Interfaces/IBlockStrategy.cs`). The interface is public, so an extension's strategy in another assembly implements it too:

```csharp
public interface IBlockStrategy
{
    Task<BlockStrategyOutput> ExecuteBlockAsync(
        Block block,
        BlockExecutionContext context,
        CancellationToken cancellationToken = default);

    bool DispatchesNestedUnits(Block block)
    {
        return false;
    }
}
```

- `block` is the block being executed; read its typed configuration via `block.Properties as XProperties` (or `block.TryGetProperties<XProperties>()`).
- `context` (`BlockExecutionContext`) exposes the services and runtime data a strategy needs — see the [BlockExecutionContext service reference](#blockexecutioncontext-service-reference) below for every member.
- `BlockStrategyOutput` is the return value; its `Documents` (`List<DocumentInfo>`) carries any documents the block produced (e.g. a generated `.xlsx`/`.docx`).
- `DispatchesNestedUnits` defaults to false. A strategy that runs billable units of its own answers true, and the engine then runs its node without a concurrency permit ([where the permit binds](parallel-execution.md#where-the-concurrency-permit-binds)). It must be a pure read and must not throw.


## BlockExecutionContext service reference

`BlockExecutionContext` (`platform/IniaAi.Core/Workflows/BlockExecutionContext.cs`) is the single argument every strategy receives besides the block itself. It carries two kinds of member: **injected services** (constructor-supplied, shared for the whole execution) and **runtime data** (set by the engine per block before `ExecuteBlockAsync` runs). A strategy never news up a service — it reaches for it on `context`.

### Injected services

| Member | Type | What it does |
|--------|------|--------------|
| `Logger` | `ILogger` | Structured logger for the block. Use `context.Logger.LogDebug/LogWarning/LogError` with templated messages, passing `block` as a parameter. |
| `ChatConnector` | `IChatConnector` | Calls the AI model (Anthropic/Bedrock/Foundry behind the interface). `GetResponseAsync` / `GetResponseWithCachingAsync` / `GetThinkingResponseAsync`. |
| `RagService` | `RagService` | Retrieval over the execution's documents — filters/ranks pages for a block's query and tracks the tokens that retrieval itself spent. |
| `DocumentAnalysisService` | `DocumentAnalysisService` | Higher-level document reasoning over pages (e.g. generating per-document topics/overviews). |
| `ExchangeRateService` | `IExchangeRateService` | Currency conversion rate lookups (`GetExchangeRateAsync(from, to, date)`). |
| `ExaSearchService` | `IExaSearchService` | AI web search (`SearchAsync`) used by the ExaSearch block. |
| `ModelFamilyResolver` | `TenantModelFamilyResolver` | Resolves which model family / model override the caller's org should use for a given function (`GetModelFamilyForFunction`, `GetModelOverride`). |
| `FileStorageManager` | `IFileStorageManager` | File storage. `ReadDocumentBytesAsync` (`StoredDocumentBytes`) returns a document's bytes, loaded or stored; the Agent block stages its documents through it. |
| `PdfFormat` | `IPdfFormat` | PDF reading and writing for strategies that handle PDF bytes. |
| `TokenUsageService` | `ITokenUsageService` | Records LLM and embedding token usage for billing/quotas (`RecordLlmUsageAsync`, `RecordEmbeddingUsageAsync`). |
| `PlatformSettings` | `PlatformSettings` | The resolved platform configuration (limits, parallelism, feature flags) for the running deployment. |
| `Services` | `IServiceProvider` | The block's own service scope. A strategy that a closed module registers resolves the module's services here. The open strategies take what they need from the members above. |
| `AgentRunExecutor` | `IAgentRunExecutor?` | The shared agent-integration seam: one call runs a sidecar worker start → gated poll → trace collection → acknowledge-or-withhold (see [Agent Runtime Integration](../services/infrastructure/agent-runtime-integration.md)). The raw runtime client and trace collector are internals of this seam. **Null when `AgentRuntimeSettings.Enabled` is off** — a strategy that needs it must fail its own block descriptively instead of assuming it. |
| `StoredKeys` | `IStoredKeyResolver?` | Resolves an organization's [stored keys](../../features/user-management/stored-keys.md) for a Web API Call header. Null when the host registers none; a block that names a stored key then fails with a descriptive error. |

### Runtime data (set by the engine per block)

| Member | Type | What it holds |
|--------|------|---------------|
| `TextInputs` | `Dictionary<string, string?>` | The inputs already resolved for this block — workflow variables, system values, and **prior block outputs** keyed by their replacement tag. This is how a block reads what an earlier block produced. |
| `ReferencedBlocks` | `Dictionary<string, Block>` | The actual block objects this block's placeholders point at, when a strategy needs more than the resolved text. |
| `AllPages` | `List<Page>` | Every page of every document in the execution — the corpus RAG and document-analysis calls run against. |
| `Counterparty` / `PeerCompanies` | `Counterparty` / `List<Counterparty>` | The subject company and its peers, when the execution is scoped to one. |
| `AnonymisationMap` | `ConcurrentDictionary<string, string>` | Sensitive-value replacements applied during anonymisation. |
| `WorkflowTypeId`, `OrganizationId`, `UserId`, `WorkflowExecutionId`, `SectionId`, `WorkflowName`, `WorkflowVersion` | `string` | Identity/location of this block within the tenant and the executed snapshot. |
| `RagResultsCache` | `ConcurrentDictionary<…>` | Per-execution cache of filtered RAG pages so repeated retrievals over the same documents are not recomputed. |
| `ExecutionContext` | `ExecutionContext` (the token-usage enum, aliased `TokenExecutionContext`) | Whether this is a real `WorkflowExecution` or a `BlockTest`/`SectionTest` run — used for token-usage attribution. |
| `BlockAnnotations` / `IsCitationSource` | `BlockAnnotations` / `bool` | The citations and confidence a strategy records for its output, and whether this block's output is a citation source for the report. |
| `WebApiCookies` | `CookieContainer` | The cookie jar every Web API Call block of one run shares; another run never sees it. |
| `FlushBlockStateAsync` | `Func<CancellationToken, Task>?` | Persists the block's row mid-run as a checkpoint through the breaker-guarded writer; throws when the write did not land. Null when the run has no persisted execution (editor tests). |
| `VerificationRound`, `VerificationRedoLeg`, `BelongsToVerificationGroup` | `int`, `VerificationRedoLeg?`, `bool` | The verification leg this invocation runs under, the redo directive on a member's redo leg, and whether the block belongs to a verification group. |
| `TemplateVersionId`, `TemplateContentHash` | `string?` | The template version the execution was stamped with at submit. |
| `UnitPermits` | `IBlockUnitPermit` | Takes a concurrency permit around a billable unit the block dispatches itself; a block already inside a unit throws instead of waiting. |


There is **no** `SharedInputs` or `CancellationToken` on the context: inputs arrive pre-resolved as `TextInputs`, and the token is a parameter of `ExecuteBlockAsync`. See [Execution Context](execution-context.md) for how these values are populated.

### Common call sites

**Call the model.** `ChatConnector` returns a `ResponseContentSequence`; the simplest overload takes a prompt string:

```csharp
ResponseContentSequence response = await context.ChatConnector.GetResponseAsync(
    userInput: props.ExecutionState.Prompt,
    systemPrompt: props.ExecutionState.SystemPrompt,
    maxTokens: props.MaxTokens,
    shouldUseAdvancedModel: props.ShouldUseAdvancedModel,
    family: context.ModelFamilyResolver.GetModelFamilyForFunction(
        context.OrganizationId, FunctionType.WorkflowReasoning),
    cancellationToken: cancellationToken);
```

**Do RAG retrieval.** `GetFilteredPagesAsync` returns the pages the block's query selected, keyed by document class then page index:

```csharp
Dictionary<string, Dictionary<int, Page>> filteredPages = await context.RagService
    .GetFilteredPagesAsync(
        block,
        context.WorkflowTypeId,
        context.RagResultsCache,
        context.AllPages,
        cancellationToken: cancellationToken);
```

**Read a prior block output.** Earlier blocks' results are already resolved into `TextInputs`, keyed by the producing block's replacement tag (the same `{Tag}` the author wrote in this block's template):

```csharp
string? upstreamResult = context.TextInputs.GetValueOrDefault("Portfolio_Analysis");
```

**Convert a currency** (used by the ExchangeRate block):

```csharp
decimal rate = await context.ExchangeRateService.GetExchangeRateAsync(
    config.FromCurrency, config.ToCurrency, config.ExchangeDate);
```

### Extending the context with a new service

When a new block needs a service that isn't on the context yet, add it to `BlockExecutionContext` rather than resolving it some other way — that keeps strategies stateless and testable. Three edits:

1. **Add the property and constructor parameter** in `platform/IniaAi.Core/Workflows/BlockExecutionContext.cs`. There are **two** constructors (one takes `IOptions<PlatformSettings>`, the other a bare `PlatformSettings`); add the parameter to both and assign the property in the one that does the assignments.
2. **Register the service in DI** so it can be injected. `BlockExecutionContext` itself is registered `AddTransient` in three composition roots — `platform/IniaAi.WebApp/ServicesConfigurator.cs`, `platform/IniaAi.WebApi/Configuration/ApplicationServicesConfiguration.cs`, and the test builder `platform/IniaAi.Testing.DependencyInjection/TestServiceProviderBuilder.cs` — and the container must already know how to resolve your new dependency, so register it alongside the other Core services in those configurators.
3. **Resolve it in tests for free** — strategy tests build the context from the real DI container via `WorkflowsFixture` / `TestServiceProviderBuilder`, so once the service is registered, `_testServiceProvider.GetRequiredService<BlockExecutionContext>()` supplies it with no test changes.

Then read it off `context` in your strategy like any other member.

## Registration

`BlockStrategies` (`platform/IniaAi.Core/Workflows/Strategies/BlockStrategies.cs`) holds the strategy for each block type, and `WorkflowHandler` (`platform/IniaAi.Core/Workflows/WorkflowHandler.cs`) runs only these. A second strategy for one type fails the handler's build with a message that names the type. The strategies come from three sources:

- **The platform's own strategies**: the list in `BlockStrategies`. Each strategy holds no state, so the process shares one instance of each.
- **The closed modules**, each through a `BlockStrategyRegistration` (block type and strategy) in dependency injection, one registration per block type each owns ([Service Registration](../services/service-registration.md) names each module's method).
- **Extensions**: `AddBlockType(definition, strategy)`, which also registers a `BlockStrategyRegistration` and so shares one strategy instance across every run, called from an `IBlockExtension` that a host loads from `PlatformSettings:BlockExtensions` ([A block type in its own project](block-implementation-guide.md#a-block-type-in-its-own-project)). It also adds the type's definition to `BlockTypeCatalog`.

A block whose type has no strategy does not run. A closed module's type, an extension's type and a type the build does not know fail with `UnavailableBlockTypes.RunMessage`. For a closed module's type that is the stub message ([what the open build shows](../../guides/blocks/block-reference.md#blocks-that-only-the-paid-build-runs)). For any other type the message names the type's number and says to remove the block, or to install the extension that adds it (a number from 1000 up) or update the platform to a release that includes it (a lower number). A platform type with no strategy, such as a loop block left unexpanded ([Loop Processing](loop-processing.md)), fails with `No strategy found for block type: …`. `InstalledBlockTypes` answers which types this build can run, for the editor and the dependency map.

Loop blocks (`LoopStart` / `LoopEnd`) are not strategies — the handler expands them by cloning the enclosed blocks before strategy execution.

## Execution shape

A typical strategy:

1. Initializes execution state (`props.ExecutionState ??= new(props);` / `block.ExecutionState ??= new(block);`).
2. Reads its typed properties, returning early (with a logged warning) when required input is missing.
3. Performs its work using context services.
4. Records output by constructing a `BlockResponse(block) { RawResponse = ... }` and/or adding `Documents` to the returned `BlockStrategyOutput`.

Validation failures throw descriptive exceptions; the engine surfaces them as block errors.

## Placeholder substitution & sanitization

Before a strategy runs, the handler copies each template property onto its execution state and substitutes placeholders reflectively (`PlaceholderReflectionHelper.ReplaceAll`): every public `string` property, `Dictionary<string, string>` value, and `List<string>` item on the execution state is templatable by default (`[PlaceholderReplacementIgnore]` opts out). A property whose value must conform to a syntax declares `[SanitizePlaceholderValues(...)]` on the execution-state property, and the engine sanitizes each **substituted value** while leaving template literals untouched — `Url` percent-encodes, `Json` escapes a value only where it lands inside a JSON string (a placeholder in value position, a whole-placeholder template, and already-escaped values substitute verbatim, so nested upstream JSON survives intact; skips are recorded in the state's `SanitizationWarnings`), `HeaderValue` rejects control characters with a descriptive exception. A substituted value may itself be a template — a text input's value routinely embeds tags like `{Counterparty.Name}` for readability — so sanitized modes first resolve placeholders nested inside the value (plain replacement against the same inputs), then sanitize the composed result. See the [placeholder system](../../guides/blocks/block-reference.md#placeholder-system) for the author-facing behavior per block.

## Strategy catalog

The concrete strategies, by what they do. The open ones live under `platform/IniaAi.Core/Workflows/Strategies/`:

| Strategy class | `BlockTypes` value | Does |
|---|---|---|
| `AiQueryStrategy` | `AiQuery` | AI model queries with RAG support, output-format validation |
| `ExaSearchStrategy` | `IntelligentSearch` | AI-powered web search — see [ExaSearch Block](exa-search-block.md) |
| `RetrieveInformationFromDocumentStrategy` | `SimpleRag` | RAG-based document retrieval and extraction |
| `BuildTopicsForAllDocumentsStrategy` | `BuildTopicsForAllDocuments` | Topic generation across documents — an execution-order fence: it writes shared state, so the scheduler pins it at its authored position |
| `WebApiCallStrategy` | `WebApiCall` | Generic HTTP API calls (with optional poll-until-condition); retries 5xx/408/425 on a fixed schedule and 429 on a jittered, `Retry-After`-aware schedule, and sends through a process-wide per-host concurrency gate (`WebApiCallHostGate`, limits from `PlatformSettings:WebApiCall`). Every request and redirect hop goes through `OutboundAddressGuard`, which refuses an internal address unless `PlatformSettings:OutboundAccess` allows it; a refusal fails the block without a retry |
| `CalculateFormulaStrategy` | `CalculateNumber` | Mathematical calculations |
| `StringManipulationStrategy` | `StringManipulation` | String transformations |
| `MergeStrategy` | `Merge` | Concatenate text from multiple blocks |
| `MergeJsonStrategy` | `MergeJson` | Combine JSON inputs into one JSON value (objects deep-merge, arrays concatenate) |
| `SwitchCaseStrategy` | `SwitchCase` | Map an input value to a configured output value (a lookup, not a branch) |
| `ExchangeRateStrategy` | `ExchangeRate` | Currency conversion |
| `DynamicChartStrategy` | `DynamicChart` | Chart configuration from JSON data |
| `AgentBlockStrategy` | `Agent` | Runs a sandboxed worker on the agent-runtime sidecar: renders the block's skills under the strict double-brace agent-template grammar (single pass, substituted values spliced literally), scopes the documents it ships by class at execution time (`Block.GetClassScopedDocuments` — `None` ships nothing, empty/`Any` ship everything, concrete classes ship only documents whose current `DocumentInfo` still matches, and a concretely scoped block with no matches throws **before** the start request so no billed run happens), repairs document names and pre-parses the output schema (`AgentStagedNames`/`AgentOutputSchemas`) so a bad request fails before a billed run, then hands the run to the shared `IAgentRunExecutor` — start, gated polling under the block's deadline, trace collection, and acknowledge-or-withhold live there, not in the strategy (see [Agent Runtime Integration](../services/infrastructure/agent-runtime-integration.md)) — and finally stamps the returned trace rows with its block identity and materializes the text result as the `BlockResponse`, or, when the block lists output files, the answer plus those files from the settled run's `out/` rows (`AgentRunOutputFiles`, see [Output files](../../guides/blocks/block-reference.md#agent-output-files)) |




Loops (`LoopStart` / `LoopEnd`) have **no strategy classes** — the scheduler expands a loop section into per-iteration work structurally, before strategy dispatch.


In the open build two paths enqueue a workflow job (`IJobsService.Execute`), and both carry the pre-spend agent gate: the shared orchestrator (behind the API's submit and start endpoints) refuses the whole agent family with `AgentBlockGuard.DescribeSubmitRefusal` while `PlatformSettings:AgentRuntime:Enabled` is off, before any execution row exists, and the editor's Generate Report button (`CreateReportCard`) applies the same guard before it enqueues — necessary there because the button enqueues the in-memory editor workflow, which a file upload can fill without a save. A save carries the gate too: `AgentBlockGuard.ValidateGraph` refuses an agent-family save while the flag is off, and the block-type selector hides the family, which bounds what a saved template can carry.



The **agent family** — `Agent` and `Verification`, the two types `BlockTypes.IsAgentFamily()` answers true for — shares one run-declaration surface, `IAgentRunProperties`: the worker skills, the two prompt templates rendered under the double-brace agent-template grammar, the model/effort engine configuration, and the four declared fuses. Guards and grammar sweeps consume the family through that interface rather than switching per concrete type, and `Block.ShouldUseDocuments` answers for the family, so either type keeps its document classes. Criticality deliberately does not: `Block.AbortsExecutionOnFailure` falls back to `Agent` only. On both types the skills list is `[PlaceholderReplacementIgnore]`d on the properties and the execution state, so engine-side placeholder substitution never touches skill content — the strategy renders skills itself under the agent-template grammar. `VerificationProperties` additionally ignores its member and criteria lists for substitution while still surfacing the member tags from `GetAllPlaceholders`, so a verifier's members produce scheduling edges and dispatch inputs without ever being templated.

## Testing strategies

Strategy tests use **plain xUnit assertions with real instances and fixtures — no mocking framework** (see [C# Code Standards](../../getting-started/csharp-code-standards.md)). Build a real `BlockExecutionContext` from the test service provider, construct a real `Block` with typed properties, run the strategy, assert on the `BlockResponse`:

```csharp
public class YourBlockStrategyTests : IClassFixture<WorkflowsFixture<YourBlockStrategyTests>>
{
    private readonly IServiceProvider _testServiceProvider;

    public YourBlockStrategyTests(WorkflowsFixture<YourBlockStrategyTests> fixture, ITestOutputHelper output)
    {
        _testServiceProvider = fixture.BuildTestServiceProvider(output);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ValidInput_StoresResponse()
    {
        var section = new Section("test-org", new Workflow("test-org"));
        var block = new Block(BlockTypes.YourBlock, section)
        {
            Name = "YourBlock Test",
            Properties = new YourBlockProperties { InputTemplate = "value" }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new YourBlockStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("expected output", block.BlockResponse.RawResponse);
    }
}
```

Cover both valid input and invalid/edge cases (empty input, no match, placeholder resolution). For cross-block behaviour, loops, and output format, exercise the strategy through a full workflow context rather than in isolation.

## Related Documentation

- [Block Implementation Guide](block-implementation-guide.md) - Step-by-step for adding a block type
- [Execution Context](execution-context.md) - How the engine populates the context and dispatches blocks
