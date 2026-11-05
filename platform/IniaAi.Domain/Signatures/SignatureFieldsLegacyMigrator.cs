using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;

namespace IniaAi.Domain.Signatures;

/// <summary>
/// Hoists legacy per-block signature field dicts (<see cref="IHasSignatureFields.SignatureFields"/>)
/// into workflow-level <see cref="WorkflowSignatureField"/> definitions. Runs on every
/// <see cref="Workflow.FromJson"/> and on the editor save path, so old .workflow files, stored
/// WorkflowVersion payloads, cross-instance imports, and live editor saves all normalize; the DB
/// migration covers Core rows, this covers every JSON entry point. Policy (shared with block-template
/// import): identical definitions merge into one field; a divergent definition under an
/// already-taken name is renamed with the block's ReplacementTag suffix and the block's template
/// tokens are rewritten to follow, so no block's resolved prompt ever changes. Idempotent: hoisted
/// blocks carry a null dict and are skipped. The inline dict remains the permanent format for
/// block-template library rows, which never pass through here.
/// </summary>
public static class SignatureFieldsLegacyMigrator
{
    public static void Hoist(Workflow workflow)
    {
        IEnumerable<Block> orderedBlocks = workflow.Sections
            .OrderBy(s => s.Order)
            .SelectMany(s => s.Blocks.OrderBy(b => b.Order));

        foreach (Block block in orderedBlocks)
        {
            if (block.Properties is not IHasSignatureFields bearer
                || bearer.SignatureFields is null
                || bearer.SignatureFields.Count == 0)
            {
                continue;
            }

            foreach ((string name, SignatureFieldDefinition definition) in bearer.SignatureFields)
            {
                if (string.IsNullOrWhiteSpace(name) || definition is null)
                {
                    continue;
                }

                string finalName = HoistDefinition(workflow, name, definition, block.ReplacementTag);
                if (!string.Equals(finalName, name, StringComparison.Ordinal))
                {
                    block.Properties.RewriteToken(name, finalName, StringComparison.OrdinalIgnoreCase);
                }
            }

            bearer.SignatureFields = null;
        }
    }

    /// <summary>
    /// Lands one definition on the workflow under the merge-identical / rename-divergent policy and
    /// returns the name it landed under. Identical definitions (ordinal DisplayName/Description/
    /// DefaultValue) merge into the existing field. A divergent definition is renamed
    /// <c>name_replacementTag</c>, then <c>_2</c>, <c>_3</c>… until a free (or identical) name is
    /// found. Callers must rewrite the referencing block's template tokens when the returned name
    /// differs from the requested one.
    /// </summary>
    public static string HoistDefinition(
        Workflow workflow,
        string name,
        SignatureFieldDefinition definition,
        string? replacementTag)
    {
        foreach (string candidate in CandidateNames(name, replacementTag))
        {
            WorkflowSignatureField? existing = workflow.SignatureFields
                .FirstOrDefault(f => string.Equals(f.Name, candidate, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                _ = new WorkflowSignatureField(workflow, candidate)
                {
                    DisplayName = definition.DisplayName ?? string.Empty,
                    Description = definition.Description ?? string.Empty,
                    DefaultValue = definition.DefaultValue ?? string.Empty,
                };
                return candidate;
            }

            if (IsIdentical(existing, definition))
            {
                return existing.Name;
            }
        }

        throw new InvalidOperationException(
            $"Could not find a free name for signature field '{name}' after exhausting disambiguators.");
    }

    private static IEnumerable<string> CandidateNames(string name, string? replacementTag)
    {
        yield return name;

        string baseName = string.IsNullOrWhiteSpace(replacementTag)
            ? name
            : name + "_" + replacementTag;
        if (!string.IsNullOrWhiteSpace(replacementTag))
        {
            yield return baseName;
        }

        for (int i = 2; i < 100; i++)
        {
            yield return baseName + "_" + i;
        }
    }

    private static bool IsIdentical(WorkflowSignatureField existing, SignatureFieldDefinition definition)
    {
        return string.Equals(existing.DisplayName, definition.DisplayName ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(existing.Description, definition.Description ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(existing.DefaultValue, definition.DefaultValue ?? string.Empty, StringComparison.Ordinal);
    }
}
