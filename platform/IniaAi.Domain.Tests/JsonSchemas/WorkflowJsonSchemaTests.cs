using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.JsonSchemas;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace IniaAi.Domain.Tests.JsonSchemas;

public class WorkflowJsonSchemaTests
{
    private const string FIXTURE_PATH = "TestResources/WorkflowHandlerFunctionalityTest.workflow.json";

    private static readonly CompiledJsonSchema _schema = JsonSchemaEvaluation.CompileSchema(WorkflowJsonSchema.Json);

    /// <summary>
    /// The editor agent learns what a block type and each of its settings does from these
    /// descriptions, which come from the code comments. A block type or setting without one is a
    /// guess for the agent: write its code comment.
    /// </summary>
    [Fact]
    public void EveryBlockTypeAndEveryBlockSetting_HasADescription()
    {
        JObject definitions = (JObject)JObject.Parse(WorkflowJsonSchema.Json)["definitions"]!;
        var missing = new List<string>();
        foreach (BlockTypeDefinition definition in BlockTypeCatalog.All.Where(static definition => !BlockTypeCatalog.IsExtensionType(definition.Type)))
        {
            if (XmlDocComments.Of(typeof(BlockTypes).GetField(definition.Type.ToString())!) is null)
            {
                missing.Add($"BlockTypes.{definition.Type}");
            }

            string name = WorkflowJsonSchema.DefinitionNameOf(definition.PropertiesType)!;
            foreach (JProperty setting in ((JObject)definitions[name]!["properties"]!).Properties())
            {
                if (!_undescribedSettings.Contains(setting.Name) && setting.Value["description"] is null)
                {
                    missing.Add($"{definition.PropertiesType.Name}.{setting.Name}");
                }
            }
        }

        Assert.True(missing.Count == 0, "Write a code comment for: " + string.Join(", ", missing.Distinct()));
    }

    /// <summary>Run evidence and the discriminator, which no edit sets.</summary>
    private static readonly HashSet<string> _undescribedSettings = new(StringComparer.Ordinal) { "ExecutionState", "Type" };

    [Fact]
    public void Fixture_ExportedWithRunTraces_MatchesTheSchema()
    {
        Workflow workflow = Workflow.FromExportJson(File.ReadAllText(FIXTURE_PATH))!;

        IReadOnlyList<string> violations = Validate(JObject.Parse(workflow.ToExportJson(includeRunTraces: true)));

        Assert.Empty(violations);
    }

    [Fact]
    public void WorkflowWithEveryBlockType_MatchesTheSchema()
    {
        IReadOnlyList<string> violations = Validate(JObject.Parse(WorkflowWithEveryBlockType().ToExportJson(includeRunTraces: true)));

        Assert.Empty(violations);
    }

    [Fact]
    public void EveryEnumValue_AsItsPropertyWritesIt_MatchesTheSchema()
    {
        List<EnumSite> sites = EnumSites();
        JsonSerializerSettings settings = Workflow.ExportWithRunTracesSettings;
        Dictionary<string, CompiledJsonSchema> definitionSchemas = [];
        JObject schemaJson = JObject.Parse(WorkflowJsonSchema.Json);
        List<string> violations = [];

        foreach (EnumSite site in sites)
        {
            string? definition = WorkflowJsonSchema.DefinitionNameOf(site.Owner);
            if (definition is null)
            {
                violations.Add($"{site.Owner.Name}.{site.PropertyName}: the schema has no definition for {site.Owner.Name}");
                continue;
            }

            if (!definitionSchemas.TryGetValue(definition, out CompiledJsonSchema? definitionSchema))
            {
                definitionSchema = JsonSchemaEvaluation.CompileSchema(new JObject
                {
                    ["$schema"] = schemaJson["$schema"],
                    ["definitions"] = schemaJson["definitions"],
                    ["$ref"] = $"#/definitions/{definition}",
                }.ToString());
                definitionSchemas[definition] = definitionSchema;
            }

            JsonSerializer serializer = JsonSerializer.Create(settings);
            if (site.Converter is not null)
            {
                serializer.Converters.Insert(0, site.Converter);
            }

            foreach (object value in Enum.GetValues(site.EnumType))
            {
                JToken written = JToken.FromObject(value, serializer);
                JToken placed = site.Shape switch
                {
                    EnumShape.ListItem => new JArray(written),
                    EnumShape.DictionaryValue => new JObject { ["key"] = written },
                    _ => written,
                };

                IReadOnlyList<string> found = JsonSchemaEvaluation.FindViolations(definitionSchema, new JObject { [site.PropertyName] = placed });
                violations.AddRange(found.Select(violation => $"{site.Owner.Name}.{site.PropertyName} = {written}: {violation}"));
            }
        }

        Assert.Contains(sites, site => site.EnumType == typeof(ModelEffort));
        Assert.Contains(sites, site => site.EnumType == typeof(MediaType));
        Assert.Contains(sites, site => site.Owner == typeof(BlockExecutionState));
        Assert.Contains(sites, site => site.Shape == EnumShape.DictionaryValue);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations.Distinct()));
    }

    [Theory]
    [InlineData("high")]
    [InlineData("High")]
    [InlineData("xhigh")]
    [InlineData("XHigh")]
    public void Effort_InTheExportSpellingOrTheMemberName_IsAccepted(string effort)
    {
        JObject json = FixtureJson();
        AiQueryProperties(json)["Effort"] = effort;

        Assert.Empty(Validate(json));
    }

    [Theory]
    [InlineData("Hihg")]
    [InlineData("HIGH")]
    public void Effort_Misspelled_IsRejected(string effort)
    {
        JObject json = FixtureJson();
        AiQueryProperties(json)["Effort"] = effort;

        Assert.NotEmpty(Validate(json));
    }

    [Fact]
    public void MisspelledPropertyKey_IsRejected()
    {
        JObject json = FixtureJson();
        FirstBlockProperties(json)["SystemPromptTemplat"] = "Summarise the filing.";

        IReadOnlyList<string> violations = Validate(json);

        Assert.Contains(violations, violation => violation.Contains("SystemPromptTemplat", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownBlockPropertiesType_IsRejected()
    {
        JObject json = FixtureJson();
        FirstBlockProperties(json)["Type"] = "NoSuchBlock";

        Assert.NotEmpty(Validate(json));
    }

    [Fact]
    public void ABlockOfATypeNoRegistrationKnows_IsAccepted_BecauseTheLoaderKeepsIt()
    {
        JObject json = FixtureJson();
        JObject block = json["Sections"]!.SelectMany(section => section["Blocks"]!).OfType<JObject>().First();
        block["Type"] = 4242;
        block["Properties"] = new JObject { ["Type"] = 4242, ["Threshold"] = 3 };

        Assert.Empty(Validate(json));
    }

    [Fact]
    public void ABlockOfATypeNoRegistrationKnows_IsAccepted_WithTheDatabasesKeyCase()
    {
        JObject json = FixtureJson();
        JObject block = json["Sections"]!.SelectMany(section => section["Blocks"]!).OfType<JObject>().First();
        block["Type"] = 4242;
        block["Properties"] = new JObject { ["type"] = 4242, ["threshold"] = 3 };

        Assert.Empty(Validate(json));
    }

    [Fact]
    public void APlatformTypeWrittenAsItsNumber_StillHasItsSettingsChecked()
    {
        JObject json = FixtureJson();
        JObject properties = json["Sections"]!.SelectMany(section => section["Blocks"]!)
            .Select(block => block["Properties"]).OfType<JObject>()
            .First(properties => properties["Type"]?.Value<string>() == nameof(BlockTypes.AiQuery));
        properties["Type"] = (int)BlockTypes.AiQuery;
        properties["Promt"] = "a misspelt setting";

        Assert.NotEmpty(Validate(json));
    }

    [Fact]
    public void WrongValueType_IsRejected()
    {
        JObject json = FixtureJson();
        json["Sections"]![0]!["Blocks"]![0]!["Order"] = "first";

        Assert.NotEmpty(Validate(json));
    }

    [Fact]
    public void UndefinedEnumInteger_IsAccepted_BecauseNewtonsoftLoadsIt()
    {
        JObject json = FixtureJson();
        JObject block = json["Sections"]!.SelectMany(section => section["Blocks"]!).OfType<JObject>().First(block => block["RagSettings"] is JArray { Count: > 0 });
        block["RagSettings"]![0]!["Type"] = 9;

        Assert.Empty(Validate(json));
    }

    [Fact]
    public void Schema_UsesOnlyKeywordsTheSandboxCheckerUnderstands()
    {
        JObject schema = JObject.Parse(WorkflowJsonSchema.Json);
        var definitionAndPropertyNames = schema.Descendants().OfType<JProperty>()
            .Where(property => property.Parent?.Parent is JProperty { Name: "definitions" or "properties" })
            .Select(property => property.Name)
            .ToHashSet();

        string[] unsupported = [.. schema.Descendants().OfType<JProperty>()
            .Where(property => property.Parent?.Parent is not JProperty { Name: "definitions" or "properties" })
            .Select(property => property.Name)
            .Where(name => !WorkflowJsonSchema.CheckerKeywords.Contains(name))
            .Distinct()];

        Assert.NotEmpty(definitionAndPropertyNames);
        Assert.Empty(unsupported);
    }

    private static Workflow WorkflowWithEveryBlockType()
    {
        var workflow = new Workflow("Every block type");
        var section = new Section("Blocks", workflow);
        foreach (BlockTypes type in Enum.GetValues<BlockTypes>())
        {
            _ = new Block(type, section);
        }

        return workflow;
    }

    /// <summary>
    /// Every enum the export can write, found from the contracts rather than from instances, so a property
    /// behind a null or empty object is still checked: a property's own value, a list item, or a
    /// dictionary value, each with the converter that writes it.
    /// </summary>
    private static List<EnumSite> EnumSites()
    {
        JsonSerializerSettings settings = Workflow.ExportWithRunTracesSettings;
        IContractResolver resolver = settings.ContractResolver ?? new DefaultContractResolver();
        List<EnumSite> sites = [];
        HashSet<Type> visited = [];
        Queue<Type> pending = new([typeof(Workflow), .. BlockTypeCatalog.All.Select(static definition => definition.PropertiesType)]);
        while (pending.Count > 0)
        {
            Type owner = pending.Dequeue();
            if (!visited.Add(owner) || resolver.ResolveContract(owner) is not JsonObjectContract contract)
            {
                continue;
            }

            foreach (JsonProperty property in contract.Properties)
            {
                if (property.Ignored || (!property.Readable && !property.Writable) || property.PropertyName is null || property.PropertyType is null)
                {
                    continue;
                }

                if (property.Converter is not null and not StringEnumConverter)
                {
                    continue;
                }

                Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (type.IsEnum)
                {
                    sites.Add(new EnumSite(owner, property.PropertyName, type, property.Converter ?? resolver.ResolveContract(type).Converter, EnumShape.Value));
                    continue;
                }

                if (settings.Converters.Any(converter => converter.CanConvert(type)))
                {
                    continue;
                }

                switch (resolver.ResolveContract(type))
                {
                    case JsonArrayContract { CollectionItemType: not null } array:
                        AddItem(property, array.CollectionItemType, property.ItemConverter ?? array.ItemConverter, EnumShape.ListItem);
                        break;
                    case JsonDictionaryContract { DictionaryValueType: not null } dictionary:
                        AddItem(property, dictionary.DictionaryValueType, property.ItemConverter ?? dictionary.ItemConverter, EnumShape.DictionaryValue);
                        break;
                    case JsonObjectContract when !type.IsAbstract && !type.IsInterface:
                        pending.Enqueue(type);
                        break;
                }
            }

            void AddItem(JsonProperty property, Type itemType, JsonConverter? itemConverter, EnumShape shape)
            {
                Type item = Nullable.GetUnderlyingType(itemType) ?? itemType;
                if (item.IsEnum)
                {
                    sites.Add(new EnumSite(owner, property.PropertyName!, item, itemConverter ?? resolver.ResolveContract(item).Converter, shape));
                }
                else if (itemConverter is null && resolver.ResolveContract(item) is JsonObjectContract && !item.IsAbstract && !item.IsInterface)
                {
                    pending.Enqueue(item);
                }
            }
        }

        return sites;
    }

    private enum EnumShape
    {
        Value,
        ListItem,
        DictionaryValue,
    }

    private sealed record EnumSite(Type Owner, string PropertyName, Type EnumType, JsonConverter? Converter, EnumShape Shape);

    private static JObject FixtureJson()
    {
        Workflow workflow = Workflow.FromExportJson(File.ReadAllText(FIXTURE_PATH))!;
        return JObject.Parse(workflow.ToExportJson(includeRunTraces: true));
    }

    private static JObject FirstBlockProperties(JObject json)
    {
        return json["Sections"]!.SelectMany(section => section["Blocks"]!)
            .Select(block => block["Properties"])
            .OfType<JObject>()
            .First();
    }

    private static JObject AiQueryProperties(JObject json)
    {
        return json["Sections"]!.SelectMany(section => section["Blocks"]!)
            .Select(block => block["Properties"])
            .OfType<JObject>()
            .First(properties => properties["Type"]?.Value<string>() == nameof(BlockTypes.AiQuery));
    }

    private static IReadOnlyList<string> Validate(JObject json)
    {
        return JsonSchemaEvaluation.FindViolations(_schema, json);
    }
}
