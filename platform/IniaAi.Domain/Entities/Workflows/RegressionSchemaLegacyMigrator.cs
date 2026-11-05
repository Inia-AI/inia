using IniaAi.Domain.Entities.Workflows.Blocks;
using System.Text.Json;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// Hoists legacy per-block regression schemas (Type/Tolerance stored on the block) into workflow-level
/// <see cref="WorkflowRegressionField"/> definitions plus binding-only block JSON. Runs on every
/// <see cref="Workflow.FromJson"/> so old .workflow files, stored WorkflowVersion payloads, and
/// cross-instance imports normalize on load; the DB migration covers Core rows, this covers every JSON
/// entry point. Idempotent: binding-only JSON has no legacy keys and is left untouched. Executed
/// UserData snapshots deserialized through FromJson hoist in-memory too, which is harmless because the
/// comparison reads definitions from the current Core template.
/// </summary>
public static class RegressionSchemaLegacyMigrator
{
    private static readonly JsonSerializerOptions _legacyReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static void Hoist(Workflow workflow)
    {
        var boundNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Block block in OrderedBlocks(workflow))
        {
            if (string.IsNullOrWhiteSpace(block.RegressionSchema))
            {
                continue;
            }

            if (!RegressionSchemaBindings.AnyLegacyKeys(block.RegressionSchema))
            {
                // Already binding-only: claim its names so a later legacy block can't double-bind them.
                if (RegressionSchemaBindings.TryParse(block.RegressionSchema, out var existingBindings))
                {
                    boundNames.UnionWith(existingBindings.Keys);
                }

                continue;
            }

            Dictionary<string, RegressionSchemaField>? legacySchema = ParseLegacy(block.RegressionSchema);
            if (legacySchema is null)
            {
                // Malformed JSON is the validator's problem, not the hoist's.
                continue;
            }

            var bindings = new Dictionary<string, RegressionFieldBinding>(StringComparer.OrdinalIgnoreCase);
            foreach ((string fieldName, RegressionSchemaField legacyField) in legacySchema)
            {
                if (string.IsNullOrWhiteSpace(fieldName))
                {
                    continue;
                }

                bool defined = workflow.RegressionFields
                    .Any(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase));
                if (!defined)
                {
                    _ = new WorkflowRegressionField(workflow, fieldName)
                    {
                        Type = RegressionFieldTypes.IsNumeric(legacyField?.Type)
                            ? RegressionFieldTypes.NUMERIC
                            : RegressionFieldTypes.TEXT,
                        Tolerance = legacyField?.Tolerance ?? 0,
                    };
                }

                // First binder wins; a duplicate name on a later block loses its binding entirely so
                // the hoisted workflow passes the one-block-per-field save validation.
                if (boundNames.Add(fieldName))
                {
                    bindings[fieldName] = new RegressionFieldBinding(legacyField?.Source, legacyField?.Locator);
                }
            }

            block.RegressionSchema = RegressionSchemaBindings.Serialize(bindings);
        }
    }

    private static IEnumerable<Block> OrderedBlocks(Workflow workflow)
    {
        return workflow.Sections
            .OrderBy(s => s.Order)
            .SelectMany(s => s.Blocks.OrderBy(b => b.Order));
    }

    private static Dictionary<string, RegressionSchemaField>? ParseLegacy(string schemaJson)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, RegressionSchemaField>>(
                schemaJson, _legacyReadOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
