# Add a block type

A block type can live in its own project. The platform loads it from a setting, and the new type
changes no file of the platform. This page builds one, step by step, on the example in
`IniaAi.Samples.WordCount`: a **Count Words** block whose output is the number of words in a text.

## What a block type is made of

| Piece | What it does | The example |
|---|---|---|
| Settings | What the author sets on a block of the type. The workflow file and the database store it as JSON. | `platform/IniaAi.Samples.WordCount/WordCountProperties.cs` |
| Execution state | The settings of one run, with the outputs of earlier blocks filled in. | `platform/IniaAi.Samples.WordCount/WordCountExecutionState.cs` |
| Strategy | Runs a block of the type, and writes its output. | `platform/IniaAi.Samples.WordCount/WordCountStrategy.cs` |
| Editor | The Razor component that edits the settings in the workflow editor. | `platform/IniaAi.Samples.WordCount/WordCountEditor.razor` |
| Extension | Tells the platform about the type, and registers it. | `platform/IniaAi.Samples.WordCount/WordCountExtension.cs` |
| A service, when the block needs one | Work the strategy asks for. The example counts words with it. | `platform/IniaAi.Samples.WordCount/WordCounter.cs` |

## 1. Pick a number

Every block type has a number. Workflows store it with each block, so a number never changes after a
workflow uses it. The platform's own types take numbers below 1000, and the platform refuses an
extension type below 1000 (`BlockTypeCatalog.FIRST_EXTENSION_TYPE`). Pick a number from 1000 up. Two
extensions on one install need two numbers: the platform refuses a second type on a number that is
taken. The example takes 1000.

## 2. Create the project

Create a class library beside the platform's projects. The example's project file:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
    <ItemGroup>
        <FrameworkReference Include="Microsoft.AspNetCore.App" />
    </ItemGroup>
    <ItemGroup>
        <PackageReference Include="MudBlazor" />
    </ItemGroup>
    <ItemGroup>
        <ProjectReference Include="..\IniaAi.Core\IniaAi.Core.csproj" />
    </ItemGroup>
</Project>
```

The Razor SDK builds the editor component. `Directory.Packages.props` sets the version of MudBlazor,
which the workflow editor uses. Add the project to `IniaAi.Platform.slnx`, and to
`IniaAi.Platform.CI.slnf` so that CI builds it.

## 3. Write the settings and the execution state

The settings class derives from `BlockProperties`, and its constructor sets `Type` to the type's
number. The platform refuses a settings class whose `Type` is another number.

```csharp
public sealed class WordCountProperties : BlockProperties
{
    public const BlockTypes TYPE = (BlockTypes)1000;

    [JsonProperty(nameof(TextTemplate))]
    public string? TextTemplate { get; set; }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public WordCountExecutionState? ExecutionState { get; set; }

    public WordCountProperties()
    {
        Type = TYPE;
    }
}
```

It also overrides `Clone`, `SetExecutionState`, `UpdateExecutionState` and
`ClearExecutionState`. The engine calls them to copy a block and to fill in the outputs of earlier
blocks: a setting that names `{a_replacement_tag}` gets the output of the block with that tag. The
execution state, `WordCountExecutionState`, derives from `BlockPropertiesExecutionStateBase<T>` and
copies each setting that can hold such a tag. Copy the example's overrides, and change the types.

## 4. Write the strategy

The strategy implements `IBlockStrategy`. It reads the filled-in settings, does the work, and writes
the output as a `BlockResponse` of the block. A service that the block needs comes from
`context.Services`, the scope of the run.

```csharp
public sealed class WordCountStrategy : IBlockStrategy
{
    public Task<BlockStrategyOutput> ExecuteBlockAsync(Block block, BlockExecutionContext context, CancellationToken cancellationToken = default)
    {
        WordCountProperties properties = block.Properties as WordCountProperties
            ?? throw new InvalidOperationException($"Block properties must be of type {nameof(WordCountProperties)}");

        properties.ExecutionState ??= new WordCountExecutionState(properties);
        int words = context.Services.GetRequiredService<IWordCounter>().Count(properties.ExecutionState.Text ?? string.Empty);

        _ = new BlockResponse(block)
        {
            RawResponse = words.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        return Task.FromResult(new BlockStrategyOutput());
    }
}
```

The platform uses one strategy object for every run, and runs blocks at the same time. Keep no state
in the strategy's fields: keep it in the block's execution state or in a scoped service.

## 5. Write the editor

The editor is a Razor component with two parameters: `Block`, the block it edits, and
`BlockChanged`, a callback to call after each change. The platform refuses an editor without both.

```razor
<MudTextField T="string" Label="Text to count" Value="@Properties.TextTemplate" ValueChanged="@OnTextChanged" />

@code {
    [Parameter, EditorRequired]
    public Block Block { get; set; } = default!;

    [Parameter, EditorRequired]
    public EventCallback BlockChanged { get; set; }

    private WordCountProperties Properties => (WordCountProperties)Block.Properties;

    private async Task OnTextChanged(string? text)
    {
        Properties.TextTemplate = text;
        await BlockChanged.InvokeAsync();
    }
}
```

## 6. Write the extension

The extension implements `IBlockExtension`. Its definition gives the number, the label in the
editor's type list, and the short label, the colour and the editor of the block on the canvas.
`Register` adds the services the block needs, then the type with its strategy.

```csharp
public sealed class WordCountExtension : IBlockExtension
{
    public static BlockTypeDefinition Definition { get; } =
        BlockTypeDefinition.For<WordCountProperties>(WordCountProperties.TYPE, "Count Words") with
        {
            ShortLabel = "Words",
            Hue = "#06B6D4",
            EditorComponent = typeof(WordCountEditor),
        };

    public void Register(IServiceCollection services)
    {
        services.TryAddScoped<IWordCounter, WordCounter>();

        _ = services.AddBlockType(Definition, new WordCountStrategy());
    }
}
```

Pick a hue that reads on a light and on a dark canvas.

## 7. Load it

Each app loads the assemblies that `PlatformSettings:BlockExtensions` lists, and registers every
public `IBlockExtension` in them. A path is relative to the app's folder, or absolute. Load the
extension in both apps: the WebApp shows the type in the workflow editor, and both apps run
workflows.

To run the apps from source, build the project, then add the setting to the `environmentVariables`
of the launch profile of each app:

```bash
dotnet build IniaAi.Samples.WordCount/IniaAi.Samples.WordCount.csproj
```

```json
"PlatformSettings__BlockExtensions__0": "../../IniaAi.Samples.WordCount/debug/IniaAi.Samples.WordCount.dll"
```

The build writes each project to `artifacts/bin/<project>/debug/`, so this path leads from an app's
folder to the example's assembly.

To run the one-command stack with the extension, build the project in Release from `platform/`,
and put this `compose.override.yaml` beside `compose.yaml`, at the root of the
repository. Docker Compose reads it with `compose.yaml`, and its paths start from the root.

```bash
dotnet build IniaAi.Samples.WordCount/IniaAi.Samples.WordCount.csproj -c Release
```

```yaml
services:
  api:
    environment:
      PlatformSettings__BlockExtensions__0: /extensions/IniaAi.Samples.WordCount.dll
    volumes:
      - ./platform/artifacts/bin/IniaAi.Samples.WordCount/release:/extensions:ro
  webapp:
    environment:
      PlatformSettings__BlockExtensions__0: /extensions/IniaAi.Samples.WordCount.dll
    volumes:
      - ./platform/artifacts/bin/IniaAi.Samples.WordCount/release:/extensions:ro
```

Then run `docker compose up --build --wait`. Build the extension from the same commit as the images,
so that it runs on the platform it was built against.

## 8. Test it

The test projects register the example's type as they load, so that every test sees it:
`platform/IniaAi.Core.Tests/Workflows/BlockExtensions/SampleBlockType.cs` and
`platform/IniaAi.WebApp.Tests/Components/WorkflowEditor/Common/SampleBlockType.cs`. Register your definition
in both, the same way. Then these tests cover your type with no more code:

- `BlockTypeSerializationTests` writes the default settings of every registered type through the
  workflow file's serializer and through the database's serializer, reads them back, and compares.
- `BlockTypeVisualsTests` and `EditorHuesTests` check that every type the editor offers, yours
  included, has a label, a short label and its own hue. A type with no short label shows its label.

Write a test that runs your block in a real workflow, as `BlockExtensionTests` does for the example:
it loads the extension with `AddBlockExtensions`, runs a workflow where an earlier block feeds the
new one, and checks the new block's output. Run the tests:

```bash
dotnet test --project IniaAi.Core.Tests/IniaAi.Core.Tests.csproj -- --filter-namespace "*BlockExtensions*"
dotnet test --project IniaAi.WebApp.Tests/IniaAi.WebApp.Tests.csproj -- --filter-class "*BlockTypeVisualsTests*" --filter-class "*EditorHuesTests*"
```

## When an install lacks the extension

A workflow can reach an install that does not load your extension, for example when an author
imports it. The platform keeps each block of your type with its stored settings, and a later save
writes them back unchanged. The editor shows the block as unavailable, and a run fails the block with
a message that names the type's number and tells the reader to install the extension that adds it.
Startup never deletes such a block.
