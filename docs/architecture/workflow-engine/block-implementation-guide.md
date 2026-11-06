---
type: guide
status: current
description: "Step-by-step guide for adding a new workflow block type, in the platform or in a project of its own: the type number, the typed properties and execution state, the catalog definition, the strategy and its registration, the editor component, label and hue, the extension setting, what happens to a block whose type is not installed, and the tests that prove it round-trips and runs. Read when you add a block type or a block type fails to load, save or appear in the editor."
---

# Block Implementation Guide

**On this page:** 1. Define the block type · 2. Add a typed properties class · 3. Add the type to the catalog · 4. Implement the strategy · 5. Register the strategy · 6. Add the editor UI · 7. Add tests · 8. Verify the block works · A block type in its own project · A block whose type is not installed · Conventions · Related Documentation

Steps for adding a new workflow block type. Each block type is defined by a type number, a typed properties class, an execution strategy, a Blazor editor component, and tests.

A block type lives in one of two places. A **platform type** is part of the platform repository, and the steps below add one. An **extension type** lives in a project of its own and changes no platform file: see [A block type in its own project](#a-block-type-in-its-own-project).

## 1. Define the block type

Add the value to the `BlockTypes` enum. **Always append** at the end — never insert in the middle, since the numeric values are persisted. A platform type stays below `BlockTypeCatalog.FIRST_EXTENSION_TYPE` (1000): extensions take the numbers from 1000 up, so a block an extension stored never takes the number of a later platform type. `BlockTypeCatalogTests` fails on a member at 1000 or above.

- `platform/IniaAi.Domain/Entities/Workflows/Enums/BlockTypes.cs`

There is one `BlockTypes` enum. The copy in `platform/IniaAi.WebApi.Contracts/Enums/` and its alignment test were removed because nothing used them.

```csharp
/// <summary>What the block does, in one sentence: the workflow schema and the editor agent read it.</summary>
[EnumMember(Value = nameof(YourBlock))]
YourBlock = 31,
```

The last value in use is `Verification = 30`. Give the new value an XML doc comment: the workflow JSON schema derives each block type's description from it (see [Step 3](#3-add-the-type-to-the-catalog)).

## 2. Add a typed properties class

Block configuration is strongly typed, not a string property bag. Create a properties class deriving from the base in `platform/IniaAi.Domain/Entities/Workflows/Blocks/Properties/Abstractions/BlockProperties.cs`:

- `YourBlockProperties.cs` in `platform/IniaAi.Domain/Entities/Workflows/Blocks/Properties/`

Template fields hold the **authored** value (with `{Block}` / `{variable}` placeholders) and are hidden from direct use via `[EditorBrowsable(Never)]`. The resolved value lives on a separate `ExecutionState` companion, populated at runtime by `ReplaceAllPlaceholders`. The base class is abstract, so a properties class must override `Clone`, `SetExecutionState`, `UpdateExecutionState`, and `ClearExecutionState`:

```csharp
public class YourBlockProperties : BlockProperties
{
    [JsonProperty(nameof(InputTemplate), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? InputTemplate { get; set; }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public YourBlockPropertiesExecutionState? ExecutionState { get; set; }

    public YourBlockProperties()
    {
        Type = BlockTypes.YourBlock;
    }

    public override BlockProperties Clone()
    {
        YourBlockProperties clone = new();
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = StringComparison.InvariantCultureIgnoreCase)
    {
        ExecutionState = new YourBlockPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = StringComparison.InvariantCultureIgnoreCase)
    {
        ExecutionState ??= new YourBlockPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void ClearExecutionState()
    {
        ExecutionState = null;
    }
}
```

Add the `ExecutionState` companion under `Properties/ExecutionStates/`. It derives from `BlockPropertiesExecutionStateBase<T>` (which supplies the reflection-based `ReplaceAllPlaceholders`), copies each template field into a resolved field in its source constructor, and keeps an internal parameterless constructor for deserialization:

- `YourBlockPropertiesExecutionState.cs` in `platform/IniaAi.Domain/Entities/Workflows/Blocks/Properties/ExecutionStates/`

```csharp
public class YourBlockPropertiesExecutionState
    : BlockPropertiesExecutionStateBase<YourBlockPropertiesExecutionState>
{
    [JsonProperty(nameof(Input), NullValueHandling = NullValueHandling.Ignore)]
    public string? Input { get; set; }

    [JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    internal YourBlockPropertiesExecutionState()
    {
    }

    public YourBlockPropertiesExecutionState(YourBlockProperties source)
    {
        Input = source.InputTemplate;
    }
}
```

A strategy then reads the resolved value from `props.ExecutionState.Input`, never from `props.InputTemplate` directly (see Step 4).

> [!TIP]
> **Flag execution-state fields that must conform to a syntax**
>
> `BlockPropertiesExecutionStateBase<T>` makes **every** public `string` property, `Dictionary<string, string>` value, and `List<string>` item templatable by default — placeholder substitution is reflection-driven, opt out with `[PlaceholderReplacementIgnore]`. When a resolved field must conform to a syntax (a JSON body, a URL, an HTTP header value), declare `[SanitizePlaceholderValues(...)]` on the execution-state property so the engine escapes each **substituted value** — never the template's own literals — at replacement time: `Json` escapes a value that lands inside a JSON string (a value-position or whole-template placeholder substitutes verbatim, so nested upstream JSON survives), `Url` percent-encodes, `HeaderValue` rejects control characters with a descriptive exception. Exactly one mode per property; the modes never combine. Flag it here rather than hand-escaping inside the strategy, which double-escapes. See [Placeholder substitution & sanitization](block-strategy-pattern.md#placeholder-substitution--sanitization).

## 3. Add the type to the catalog

`BlockTypeCatalog` (`platform/IniaAi.Domain/Entities/Workflows/Blocks/BlockTypeCatalog.cs`) holds one `BlockTypeDefinition` per block type: its number, the label a person sees, the class its settings read into, and the short label on the editor's canvas chip when it differs from the label. Add a line to `PlatformTypes()`:

```csharp
yield return BlockTypeDefinition.For<YourBlockProperties>(BlockTypes.YourBlock, "Your Block") with { ShortLabel = "Yours" };
```

That line is the whole registration in the Domain and Infrastructure layers. `Block.CreateDefaultProperties` creates a new block's settings from it, `BlockTypeLabels.Of` reads the label from it, and both settings serializers read the settings class from it. `BlockTypeCatalogTests` fails for an enum member with no definition.

The serializers need nothing per type. `BlockProperties` has no `[JsonDerivedType]` attributes: both converters read the `type` discriminator, as a name in any case or as a number, and look the settings class up with `BlockTypeCatalog.PropertiesTypeOf`.

- **Newtonsoft**, `BlockPropertiesNewtonsoftConverter` (`platform/IniaAi.Domain/Serialization/BlockPropertiesNewtonsoftConvertor.cs`), attached to `Block.Properties`: the workflow file and most tests.
- **System.Text.Json**, `BlockPropertiesJsonConverter` (`platform/IniaAi.Infrastructure/Persistence/Serialization/Blocks/BlockPropertiesJsonConverter.cs`), registered in `BlockPropertiesJsonDefaults`: the database column (`Options` for the UserData execution store, `CoreOptions` for Core templates).

A type the catalog lacks reads as `UnavailableBlockProperties`, which both converters write back unchanged ([A block whose type is not installed](#a-block-whose-type-is-not-installed)).

The catalog also feeds the workflow JSON schema (`WorkflowJsonSchema`), which the [editor agent](../../features/editor-agent.md) reads to learn what each block type and setting does. `WorkflowJsonSchemaTests.EveryBlockTypeAndEveryBlockSetting_HasADescription` fails with `Write a code comment for: …` until the enum value and every public setting on the properties class carry an XML doc comment. The schema accepts any type number in the discriminator, because the loader keeps the settings of a type it does not know.

> [!TIP]
> **No database migration needed**
>
> Adding a block type touches **no database schema**. `Block.Properties` is persisted through a value converter that serializes the whole properties object to a JSON column (`CoreDbContext` configures it with `BlockPropertiesJsonDefaults.CoreOptions`; the executed copy in UserData uses `Options`), and `Block.Type` is an `int` enum column with no DB-side check constraint or lookup table. A new enum value and a new properties class therefore round-trip into existing columns — there is nothing to migrate.

## 4. Implement the strategy

Create the strategy implementing `IBlockStrategy` (returns `BlockStrategyOutput`). The interface is public, so a strategy in another assembly implements it too. A service the block needs comes from `context.Services`, the scope the block runs in; `BlockExecutionContext` takes no new constructor parameter for it.

- `YourBlockStrategy.cs` in `platform/IniaAi.Core/Workflows/Strategies/`

```csharp
public class YourBlockStrategy : IBlockStrategy
{
    public async Task<BlockStrategyOutput> ExecuteBlockAsync(
        Block block,
        BlockExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        BlockStrategyOutput output = new();

        YourBlockProperties props = block.Properties as YourBlockProperties
            ?? throw new InvalidOperationException(
                $"Block properties must be of type {nameof(YourBlockProperties)}");

        props.ExecutionState ??= new(props);

        // ... do work via context services, then record output:
        _ = new BlockResponse(block) { RawResponse = result };

        return output;
    }
}
```

See [Block Strategy Pattern](block-strategy-pattern.md) for the interface and available `BlockExecutionContext` services.

> [!TIP]
> **Two execution states — `props.ExecutionState` vs `block.ExecutionState`**
>
> A strategy works with **two** distinct execution-state objects, both lazily created with the `??=` idiom at the top of `ExecuteBlockAsync` so the strategy never assumes the engine pre-populated them:
>
> - **`props.ExecutionState ??= new(props)`** — the *resolved configuration*. It carries each template field with `{Block}` / `{variable}` placeholders already substituted (Step 2). Read inputs from here, never from the raw `*Template` fields.
> - **`block.ExecutionState ??= new BlockExecutionState(block)`** — the *run record* (`platform/IniaAi.Domain/Entities/Workflows/Blocks/ExecutionStates/Abstractions/BlockExecutionState.cs`). Constructing it from the block seeds `OutputTitle` from `block.OutputTitleTemplate`; it then carries `Status` (`NotExecuted` → `Running` → `Completed`/`Failed`/`Skipped` via `MarkAsStarted`/`MarkAsCompleted`/`MarkAsFailed`/`MarkAsSkipped`), `StartedAt`/`CompletedAt`, `Error`, `Sources`, and `ConfidenceScore`.
>
> Both are `[NotMapped]` and rebuilt on each execution — they ride along inside the persisted workflow JSON rather than in their own columns, which is why neither needs a migration. The engine also touches `block.ExecutionState` (via `Block.SetExecutionState` / `UpdateExecutionState`) when it expands placeholders before the strategy runs, so the `??=` guard simply reuses whatever is already there.

## 5. Register the strategy

Add it to the platform's list in `platform/IniaAi.Core/Workflows/Strategies/BlockStrategies.cs`:

```csharp
new(BlockTypes.YourBlock, new YourBlockStrategy()),
```

`BlockStrategies` adds the `BlockStrategyRegistration` of each closed module and each extension to this list, and `WorkflowHandler` runs only these ([Registration](block-strategy-pattern.md#registration)). A block whose code belongs to a closed module registers through its module instead.

## 6. Add the editor UI

The UI is Blazor Server (interactive server rendering) with MudBlazor. A block editor component binds to the **template** fields on the typed properties and raises `BlockChanged` so the editor persists edits:

- `YourBlockBlock.razor` in `platform/IniaAi.WebApp/Components/WorkflowEditor/Components/Blocks/`

```razor
@using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block
@using IniaAi.Domain.Entities.Workflows.Blocks.Properties

<MudTextField Variant="Variant.Outlined"
              Label="Input"
              Class="editor-mono-field"
              Value="@_props.InputTemplate"
              ValueChanged="@(async (string v) => { _props.InputTemplate = v; await BlockChanged.InvokeAsync(); })" />

@code {
    [Parameter, EditorRequired]
    public Block Block { get; set; } = default!;

    [Parameter]
    public EventCallback BlockChanged { get; set; }

    private YourBlockProperties _props => Block.Properties as YourBlockProperties
        ?? throw new InvalidOperationException("Block properties must be of type YourBlockProperties");
}
```

Then wire it into `platform/IniaAi.WebApp/Components/WorkflowEditor/Components/BlockInteraction.razor` — add a render branch keyed on the block type (this is the only edit needed there; the block-type dropdown is generated by a loop, not a hand-maintained list):

```razor
@if (Block.Type is BlockTypes.YourBlock)
{
    <YourBlockBlock Block="Block"
                    BlockChanged="@OnChildBlockChanged" />
}
```

Finally, place the type in the block-type list and give it a colour. The label and the short label come from the catalog (Step 3).

- `BlockTypeVisuals.Selectable` (`platform/IniaAi.WebApp/Components/WorkflowEditor/Common/BlockTypeVisuals.cs`) — the order of the block-type dropdown. `SelectableFor` lists every type in the catalog in this order, so a type you leave out comes last. It hides the agent family while the agent runtime is off, and a type this build cannot run unless the block already has it.
- `EditorHues.Block(BlockTypes)` (`platform/IniaAi.WebApp/Theme/EditorHues.cs`) — the accent colour for the block's chip. Without an arm the block takes `EditorHues.FALLBACK` (`#94A3B8`, a neutral grey) and looks like no other type.

```csharp
// BlockTypeVisuals.cs
public static readonly IReadOnlyList<BlockTypes> Selectable =
[
    // ... existing entries ...
    BlockTypes.YourBlock,
];

// EditorHues.cs
public static string Block(BlockTypes type) => type switch
{
    // ... existing arms ...
    BlockTypes.YourBlock => "#6366F1",
    _ => BlockTypeCatalog.Find(type)?.Hue ?? FALLBACK
};
```

## 7. Add tests

Add a strategy test covering valid and invalid input:

- `YourBlockStrategyTests.cs` in `platform/IniaAi.Core.Tests/Workflows/Strategies/`

Use plain xUnit assertions and real instances/fixtures — the project does not use a mocking framework. Strategy tests build a real `BlockExecutionContext` from a test service provider and run the strategy against a real `Block`; see [C# Code Standards](../../getting-started/csharp-code-standards.md) for the testing rules.

> [!TIP]
> **Where the test fixture comes from**
>
> Strategy tests under `platform/IniaAi.Core.Tests/Workflows/` use `WorkflowsFixture<T>` (`platform/IniaAi.Core.Tests/Workflows/WorkflowsFixture.cs`, an `IClassFixture<T>`). Its `BuildTestServiceProvider(testOutputHelper)` stands up the real DI container via `TestServiceProviderBuilder` — database-backed services, a real `BlockExecutionContext`, and a seeded test `Organization` (resolved through `TestDatabaseServices.GetTestOrganization`). Resolve `BlockExecutionContext` from that provider, construct your `Block` with its typed `Properties`, run the strategy, and assert on `block.BlockResponse`. The full skeleton is in [Block Strategies → Testing](block-strategy-pattern.md#testing-strategies).

## 8. Verify the block works

A green build only proves the code compiles — it does **not** prove the block round-trips through JSON (Step 3) or executes (Step 4). Two real, runnable tests close that gap. Both use plain xUnit assertions, real instances, and no mocking framework, matching the project's [C# code standards](../../getting-started/csharp-code-standards.md).

**a. Round-trip test (proves Step 3).** `BlockTypeSerializationTests` (`platform/IniaAi.Core.Tests/Workflows/BlockExtensions/`) already sends the default settings of every type in the catalog through both serializers, so a new type is covered with no edit. Add a test of your own for the settings your block fills in. Model it on `platform/IniaAi.Core.Tests/Workflows/Strategies/StringManipulationBlockSerializationTests.cs`. It serializes a `Block` and deserializes it back through the Newtonsoft converter attached to `Block.Properties`:

```csharp
[Fact]
public void NewtonsoftRoundTrip_PreservesYourBlockProperties()
{
    var section = new Section("test-org", new Workflow("test-org"));
    var block = new Block(BlockTypes.YourBlock, section)
    {
        Name = "Your Block",
        Properties = new YourBlockProperties { InputTemplate = "hello" },
    };

    string json = JsonConvert.SerializeObject(block);
    Block? deserialized = JsonConvert.DeserializeObject<Block>(json);

    Assert.NotNull(deserialized);
    YourBlockProperties typed = Assert.IsType<YourBlockProperties>(deserialized.Properties);
    Assert.Equal("hello", typed.InputTemplate);
}
```

If the type is missing from the catalog, `new Block(BlockTypes.YourBlock, section)` throws `Unsupported block type` before the round trip starts.

**b. Execution test (proves Step 4).** Model it on `platform/IniaAi.Core.Tests/Workflows/Strategies/SwitchCaseStrategyTests.cs`. It builds a real `BlockExecutionContext` from the fixture's DI container, runs the strategy against a real `Block`, and asserts on the recorded `BlockResponse`. Unlike the round-trip test above, this one is **database-backed**: `WorkflowsFixture` stands up the real DI container and a seeded test organization, so it needs a running local Postgres (`iniaai-db-local`) before you run this test. (The round-trip test in **a** has no such dependency.)


> [!WARNING]
> **If your strategy calls the model, the execution test needs live AI credentials**
>
> A strategy that invokes the LLM (like `AiQueryStrategy`) makes a **real** model call when its execution test runs — the fixture's `IChatConnector` is a live connector, not a mock (`TestChatConnectorFactory`: `AwsBedrockChatProvider` when `PlatformSettings.IsRunningInAws`, otherwise `AnthropicChatConnector` with an API key). So before running it, supply AWS credentials with Bedrock access (an `AccessDenied` here is your shell's expired credentials, not a test failure), or an Anthropic API key for the non-AWS path. Strategies that compute their output locally (string manipulation, switch/case, formulas) need no AI credentials.
>
>

```csharp
public class YourBlockStrategyTests : IClassFixture<WorkflowsFixture<YourBlockStrategyTests>>
{
    private readonly IServiceProvider _testServiceProvider;

    public YourBlockStrategyTests(
        WorkflowsFixture<YourBlockStrategyTests> fixture,
        ITestOutputHelper outputHelper)
    {
        _testServiceProvider = fixture.BuildTestServiceProvider(outputHelper);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ValidInput_RecordsResponse()
    {
        var section = new Section("test-org", new Workflow("test-org"));
        var block = new Block(BlockTypes.YourBlock, section)
        {
            Name = "Your Block",
            Properties = new YourBlockProperties { InputTemplate = "hello" },
        };
        BlockExecutionContext context =
            _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new YourBlockStrategy();

        _ = await strategy.ExecuteBlockAsync(
            block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("hello", block.BlockResponse.RawResponse);
    }
}
```

Run just your block's tests (MTP filter syntax):

```bash
dotnet test --project IniaAi.Core.Tests/IniaAi.Core.Tests.csproj -- --filter-class "*YourBlock*"
```

Then run the schema test, which checks the code comments from Step 3:

```bash
dotnet test --project IniaAi.Domain.Tests/IniaAi.Domain.Tests.csproj -- --filter-class "*WorkflowJsonSchemaTests*"
```

**Done when you see** all three test classes pass — a `Passed!` summary with zero failures for the two `YourBlock*` test classes and `WorkflowJsonSchemaTests`. For a final manual confirmation, launch the WebApp, open the workflow editor, and confirm your block appears in the "add block" dropdown under the label from Step 3, can be added, edited, saved, and reloaded without error. At that point the block is verified end to end: it deserializes, executes, and is usable in the editor.


## A block type in its own project

[Add a Block Type](../../guides/blocks/add-a-block-type.md) is the one home of these steps: the project, the type number, the settings, the strategy, the editor, the `IBlockExtension`, the settings that load an extension from source and in the Docker stack, and the tests that cover it. `IniaAi.Samples.WordCount` is its example, a Count Words block, and `BlockExtensionTests` loads it the way a host does and runs it in a workflow.

What the platform does with an extension, for a reader of the platform's code:

- Each host loads the assemblies in `PlatformSettings:BlockExtensions` at startup and calls `Register` on every public `IBlockExtension` (`BlockTypeServiceCollectionExtensions.AddBlockExtensions`). An assembly with none stops the host with a message that names it ([Application configuration](../../operations/application-configuration.md)).
- `BlockTypeCatalog.Register` refuses a number below `BlockTypeCatalog.FIRST_EXTENSION_TYPE` (1000), settings whose `Type` is another number, an editor without the `Block` and `BlockChanged` parameters, and a second type on a taken number.
- The platform uses one strategy instance for every block of the type in every concurrent run, so a strategy keeps no state of a run in its fields.
- When a host lists any extension, it binds the extension's references to the platform assemblies it has loaded, whatever version each reference names (`BlockTypeServiceCollectionExtensions.LoadedAssembly`): an image built from source stamps its assemblies 0.0.0, and an extension built on a developer machine references 1.0.0, so the runtime would otherwise load the copies beside the extension and every platform type would exist twice. A host with no extension keeps the runtime's own binding.
- The editor offers the type after the platform's types, with its label, short label and hue, and shows its component in the block panel.

## A block whose type is not installed

A stored block keeps its type number whatever the build. When the build does not register the type (an extension that is not installed, a closed module's type in the open build, or a type a later release added):

- **Startup** keeps the block, its responses and its logs. No cleanup deletes blocks by type.
- **Both serializers** read the settings of a type the catalog lacks (an extension that is not installed, or a type a later release added) as `UnavailableBlockProperties` and write them back unchanged, so saving the workflow loses nothing. Only the failure setting is read, so a run still stops when the block fails if its author chose that; the editor offers no failure setting for such a block. A closed module's type stays in the catalog, so its settings read into their own class.
- **The editor** shows the block with `StubBlockPanel` and `UnavailableBlockTypes.EditorMessage`. The type selector keeps the block's own type.
- **A run** fails the block with `UnavailableBlockTypes.RunMessage`. Both messages say why the block cannot run and what to do next.

## Conventions

- Every `if`/`else`/`foreach`/etc. uses curly braces.
- No magic strings — extract repeated literals into `ALL_UPPER` `const` fields.
- Configure entities via attributes, not `OnModelCreating`.

## Related Documentation

- [Block Strategy Pattern](block-strategy-pattern.md) - The strategy interface and engine-level execution detail
- [Block Reference](../../guides/blocks/block-reference.md) - Configuration for shipped blocks
