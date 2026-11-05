using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

/// <summary>
/// In-dialog validation for signature field names, mirroring the save-time structural
/// validator so staff fail fast: non-empty, unique per workflow (case-insensitive), and no
/// collision with a block ReplacementTag or workflow variable tag (the <c>{token}</c> would
/// be ambiguous). Pure static logic so it is unit-testable.
/// </summary>
public static class SignatureFieldNameRules
{
    /// <summary>
    /// Returns an error message, or null when <paramref name="name"/> is valid.
    /// <paramref name="existingField"/> is the field being edited (excluded from the
    /// uniqueness check); null when creating.
    /// </summary>
    public static string? Validate(Workflow workflow, string? name, WorkflowSignatureField? existingField)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Field name is required";
        }

        bool nameTaken = workflow.SignatureFields.Any(f =>
            !ReferenceEquals(f, existingField)
            && f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (nameTaken)
        {
            return $"A signature field named '{name}' already exists";
        }

        foreach (Block block in workflow.GetAllBlocks())
        {
            if (string.Equals(block.ReplacementTag, name, StringComparison.OrdinalIgnoreCase))
            {
                return $"'{name}' collides with a block ReplacementTag — the {{{name}}} token would be ambiguous";
            }
        }

        foreach (WorkflowVariable variable in workflow.Variables)
        {
            if (string.Equals(variable.ReplacementTag, name, StringComparison.OrdinalIgnoreCase))
            {
                return $"'{name}' collides with a workflow variable tag — the {{{name}}} token would be ambiguous";
            }
        }

        return null;
    }
}
