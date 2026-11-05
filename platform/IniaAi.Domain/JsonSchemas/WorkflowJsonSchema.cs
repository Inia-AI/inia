using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System.Reflection;

namespace IniaAi.Domain.JsonSchemas;

/// <summary>
/// A JSON Schema for a workflow as its export JSON spells it, run evidence included. It is derived
/// from the Newtonsoft contracts that read and write that JSON, so the two cannot drift. An object key
/// the contracts do not know is rejected: Newtonsoft drops it silently on load, so a misspelled key
/// would otherwise pass for a working edit. Draft-07, because the sandbox checker supports no later
/// draft. Each definition, setting and enum value carries its code comment as a <c>description</c>,
/// so the editor agent can read what a block or setting does, not only its name.
/// </summary>
public static class WorkflowJsonSchema
{
    private const string DRAFT_07 = "http://json-schema.org/draft-07/schema#";
    private const string DEFINITIONS = "definitions";
    private const string REF = "$ref";
    private const string TYPE = "type";
    private const string PROPERTIES = "properties";
    private const string ADDITIONAL_PROPERTIES = "additionalProperties";
    private const string ANY_OF = "anyOf";
    private const string ENUM = "enum";
    private const string NULL = "null";
    private const string STRING = "string";
    private const string INTEGER = "integer";
    private const string NUMBER = "number";
    private const string BOOLEAN = "boolean";
    private const string OBJECT = "object";
    private const string ARRAY = "array";
    private const string ITEMS = "items";
    private const string DESCRIPTION = "description";
    private const string BLOCK_PROPERTIES_TYPE = nameof(BlockProperties.Type);
    private const string STORED_BLOCK_PROPERTIES_TYPE = "type";

    private static readonly Lazy<(string Json, IReadOnlyDictionary<Type, string> DefinitionNames)> _built = new(() =>
    {
        var builder = new SchemaBuilder(Workflow.ExportWithRunTracesSettings);
        string json = builder.BuildRoot(typeof(Workflow)).ToString(Formatting.Indented);
        return (json, builder.DefinitionNames);
    });

    public static string Json => _built.Value.Json;

    /// <summary>The definition under <c>definitions</c> that describes <paramref name="type"/>, if any.</summary>
    internal static string? DefinitionNameOf(Type type)
    {
        return _built.Value.DefinitionNames.GetValueOrDefault(type);
    }

    /// <summary>
    /// The schema keywords <see cref="Json"/> may use: the editor agent's dependency-free Python
    /// checker understands only these.
    /// </summary>
    public static IReadOnlySet<string> CheckerKeywords { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "$schema", DEFINITIONS, REF, TYPE, PROPERTIES, ADDITIONAL_PROPERTIES, "required", ITEMS,
        ENUM, "const", ANY_OF, "allOf", "if", "then", DESCRIPTION,
    };

    private sealed class SchemaBuilder(JsonSerializerSettings settings)
    {
        private readonly IContractResolver _resolver = settings.ContractResolver ?? new DefaultContractResolver();
        private readonly IList<JsonConverter> _settingsConverters = settings.Converters;
        private readonly NullabilityInfoContext _nullability = new();
        private readonly JObject _definitions = [];
        private readonly Dictionary<Type, string> _definitionNames = [];

        public IReadOnlyDictionary<Type, string> DefinitionNames => _definitionNames;

        public JObject BuildRoot(Type rootType)
        {
            JObject root = Ref(rootType);
            return new JObject
            {
                ["$schema"] = DRAFT_07,
                [REF] = root[REF],
                [DEFINITIONS] = _definitions,
            };
        }

        /// <param name="itemConverter">A property's <c>ItemConverterType</c>, which Newtonsoft applies to
        /// the property's collection items ahead of the collection contract's own item converter.</param>
        private JObject SchemaFor(Type type, JsonConverter? propertyConverter, JsonConverter? itemConverter = null)
        {
            Type? underlying = Nullable.GetUnderlyingType(type);
            if (underlying is not null)
            {
                return OrNull(SchemaFor(underlying, propertyConverter, itemConverter));
            }

            if (type == typeof(BlockProperties) || propertyConverter is BlockPropertiesNewtonsoftConverter)
            {
                return Ref(typeof(BlockProperties));
            }

            if (propertyConverter is StringEnumConverter propertyEnumConverter && type.IsEnum)
            {
                return EnumSchema(type, propertyEnumConverter);
            }

            if (propertyConverter is not null || _settingsConverters.Any(converter => converter.CanConvert(type)))
            {
                return [];
            }

            JsonContract contract = _resolver.ResolveContract(type);
            if (contract.Converter is not null)
            {
                return contract.Converter is StringEnumConverter contractEnumConverter && type.IsEnum ? EnumSchema(type, contractEnumConverter) : [];
            }

            return contract switch
            {
                JsonStringContract => new JObject { [TYPE] = STRING },
                JsonPrimitiveContract => PrimitiveSchema(type),
                JsonArrayContract array => new JObject
                {
                    [TYPE] = ARRAY,
                    [ITEMS] = array.CollectionItemType is null ? new JObject() : SchemaFor(array.CollectionItemType, itemConverter ?? array.ItemConverter),
                },
                JsonDictionaryContract dictionary => new JObject
                {
                    [TYPE] = OBJECT,
                    [ADDITIONAL_PROPERTIES] = dictionary.DictionaryValueType is null ? new JObject() : SchemaFor(dictionary.DictionaryValueType, itemConverter ?? dictionary.ItemConverter),
                },
                JsonObjectContract when !type.IsAbstract && !type.IsInterface => Ref(type),
                _ => [],
            };
        }

        private JObject Ref(Type type)
        {
            if (!_definitionNames.TryGetValue(type, out string? name))
            {
                name = UniqueName(type);
                _definitionNames[type] = name;
                _definitions[name] = type == typeof(BlockProperties) ? BlockPropertiesDefinition() : ObjectDefinition(type);
            }

            return new JObject { [REF] = $"#/{DEFINITIONS}/{name}" };
        }

        private JObject ObjectDefinition(Type type)
        {
            var contract = (JsonObjectContract)_resolver.ResolveContract(type);
            var properties = new JObject();
            var definition = new JObject
            {
                [TYPE] = OBJECT,
                [PROPERTIES] = properties,
                [ADDITIONAL_PROPERTIES] = contract.ExtensionDataSetter is not null,
            };
            if (XmlDocComments.Of(type) is string typeDescription)
            {
                definition[DESCRIPTION] = typeDescription;
            }

            foreach (JsonProperty property in contract.Properties)
            {
                if (property.Ignored || (!property.Readable && !property.Writable) || property.PropertyName is null || property.PropertyType is null)
                {
                    continue;
                }

                JObject schema = SchemaFor(property.PropertyType, property.Converter, property.ItemConverter);
                JObject described = AllowsNull(property) ? OrNull(schema) : schema;
                if (MemberOf(property) is MemberInfo member && XmlDocComments.Of(member) is string description)
                {
                    described[DESCRIPTION] = described[DESCRIPTION] is JValue values ? $"{description}\n{values}" : description;
                }

                properties[property.PropertyName] = described;
            }

            return definition;
        }

        /// <summary>
        /// Mirrors <see cref="BlockPropertiesNewtonsoftConverter"/>: the <c>Type</c> discriminator picks the
        /// properties class, and each class rejects keys it does not know. A platform type is written by
        /// name or by number, and an extension's type by number. The settings of a type this build does
        /// not register keep the text they were stored with, and the database stores the discriminator
        /// as <c>type</c>: the loader keeps such settings unread, so the schema accepts them too.
        /// </summary>
        private JObject BlockPropertiesDefinition()
        {
            var serializer = new JsonSerializer();
            serializer.Converters.Add(new StringEnumConverter());

            var branches = new JArray();
            foreach (BlockTypeDefinition definition in BlockTypeCatalog.All)
            {
                JToken written = JToken.FromObject(definition.Type, serializer);
                branches.Add(new JObject
                {
                    ["if"] = new JObject
                    {
                        ["required"] = new JArray(BLOCK_PROPERTIES_TYPE),
                        [PROPERTIES] = new JObject
                        {
                            [BLOCK_PROPERTIES_TYPE] = new JObject
                            {
                                [ENUM] = written.Type is JTokenType.Integer ? new JArray(written) : new JArray(written, (int)definition.Type),
                            },
                        },
                    },
                    ["then"] = Ref(definition.PropertiesType),
                });
            }

            return new JObject
            {
                [TYPE] = OBJECT,
                [ANY_OF] = new JArray(
                    new JObject { ["required"] = new JArray(BLOCK_PROPERTIES_TYPE) },
                    new JObject { ["required"] = new JArray(STORED_BLOCK_PROPERTIES_TYPE) }),
                [PROPERTIES] = new JObject { [BLOCK_PROPERTIES_TYPE] = EnumSchema(typeof(BlockTypes), new StringEnumConverter()) },
                ["allOf"] = branches,
            };
        }

        private static MemberInfo? MemberOf(JsonProperty property)
        {
            return property.DeclaringType?
                .GetMember(property.UnderlyingName ?? string.Empty, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault();
        }

        private bool AllowsNull(JsonProperty property)
        {
            if (property.PropertyType is null || (property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) is null))
            {
                return false;
            }

            MemberInfo? member = MemberOf(property);

            NullabilityInfo? info = member switch
            {
                PropertyInfo propertyInfo => _nullability.Create(propertyInfo),
                FieldInfo fieldInfo => _nullability.Create(fieldInfo),
                _ => null,
            };

            return info is null || info.ReadState != NullabilityState.NotNull;
        }

        private string UniqueName(Type type)
        {
            string baseName = type.IsGenericType
                ? $"{type.Name[..type.Name.IndexOf('`')]}Of{string.Join("And", type.GetGenericArguments().Select(argument => argument.Name))}"
                : type.Name;

            string name = baseName;
            for (int suffix = 2; _definitionNames.ContainsValue(name); suffix++)
            {
                name = $"{baseName}{suffix}";
            }

            return name;
        }

        private static JObject PrimitiveSchema(Type type)
        {
            if (type.IsEnum)
            {
                return EnumSchema(type, new StringEnumConverter());
            }

            return Type.GetTypeCode(type) switch
            {
                TypeCode.Boolean => new JObject { [TYPE] = BOOLEAN },
                TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32
                    or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 => new JObject { [TYPE] = INTEGER },
                TypeCode.Single or TypeCode.Double or TypeCode.Decimal => new JObject { [TYPE] = NUMBER },
                TypeCode.String or TypeCode.Char or TypeCode.DateTime => new JObject { [TYPE] = STRING },
                _ => type == typeof(byte[]) || type == typeof(Guid) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Uri)
                    ? new JObject { [TYPE] = STRING }
                    : [],
            };
        }

        /// <summary>
        /// Allows each name as the converter writes it (an <c>EnumMember</c> value or a naming strategy) and
        /// as the C# member name, which the loader also reads. Newtonsoft also reads any integer, defined or
        /// not, with or without a string converter. Saved templates carry such integers, so rejecting them
        /// would fail data that loads.
        /// </summary>
        private static JObject EnumSchema(Type type, StringEnumConverter converter)
        {
            var serializer = new JsonSerializer();
            serializer.Converters.Add(converter);
            string[] names = [.. Enum.GetValues(type)
                .Cast<object>()
                .Select(value => JToken.FromObject(value, serializer))
                .Where(static token => token.Type == JTokenType.String)
                .Select(static token => token.Value<string>()!)
                .Concat(Enum.GetNames(type))
                .Distinct(StringComparer.Ordinal)];

            return Described(
                new JObject
                {
                    [ANY_OF] = new JArray(
                        new JObject { [ENUM] = new JArray(names) },
                        new JObject { [TYPE] = INTEGER }),
                },
                type);
        }

        /// <summary>
        /// An enum's schema with each value's code comment, or its <c>Description</c> attribute when it
        /// has no comment, as "Value: what it does." lines.
        /// </summary>
        private static JObject Described(JObject schema, Type enumType)
        {
            List<string> values = [.. enumType.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(static field => (XmlDocComments.Of(field) ?? field.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description) is string text
                    ? $"{field.Name}: {text}"
                    : null)
                .OfType<string>()];
            if (values.Count > 0)
            {
                schema[DESCRIPTION] = string.Join("\n", values);
            }

            return schema;
        }

        private static JObject OrNull(JObject schema)
        {
            return schema.Count == 0
                ? schema
                : new JObject { [ANY_OF] = new JArray(schema, new JObject { [TYPE] = NULL }) };
        }
    }
}
