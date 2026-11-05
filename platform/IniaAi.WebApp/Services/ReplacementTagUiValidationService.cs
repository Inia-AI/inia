using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.WebApp.Services;

/// <summary>
/// UI helper to validate all text-bearing fields for a block using the shared ReplacementTagValidationService.
/// </summary>
public static class ReplacementTagUiValidationService
{
    /// <summary>
    /// Validates all string fields that may contain placeholders on a given block.
    /// </summary>
    public static List<string> ValidateAllTextFieldsForBlock(Block block, Workflow workflow)
    {
        return ValidateCandidateStrings(CollectCandidateStrings(block), workflow, block);
    }

    /// <summary>
    /// Returns every template string on the block that could contain a replacement tag, using the
    /// runtime's own field discovery (<see cref="Block.GetAllTemplateStrings"/>) so editor coverage
    /// always matches what the engine substitutes. Cheap relative to validation — usable for change
    /// detection before paying for the workflow-wide tag sets.
    /// </summary>
    public static List<string> CollectCandidateStrings(Block block)
    {
        return [.. block.GetAllTemplateStrings().Where(s => s.Contains('{') && s.Contains('}'))];
    }

    public static List<string> ValidateCandidateStrings(List<string> candidates, Workflow workflow, Block block)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        ReplacementTagValidationService.TagValidationContext context =
            ReplacementTagValidationService.BuildValidationContext(workflow, block);

        return ValidateCandidateStrings(candidates, context);
    }

    public static List<string> ValidateCandidateStrings(
        List<string> candidates,
        ReplacementTagValidationService.TagValidationContext context)
    {
        var invalidTags = new List<string>();

        foreach (string candidate in candidates)
        {
            List<string> invalid = ReplacementTagValidationService.ValidateReplacementTags(candidate, context);
            if (invalid.Count != 0)
            {
                invalidTags.AddRange(invalid);
            }
        }

        return invalidTags;
    }

    /// <summary>
    /// Hashes everything the validation outcome depends on — this block's candidate
    /// strings plus the workflow-level tag inputs (block/text-input/variable tags,
    /// parameterized configs, loop list titles) — without any JSON parsing, so callers
    /// can skip revalidation when nothing relevant changed.
    /// </summary>
    public static int ComputeValidationFingerprint(Block block, Workflow workflow, List<string> candidates)
    {
        var hash = new HashCode();

        foreach (string candidate in candidates)
        {
            hash.Add(candidate);
        }

        hash.Add(workflow.ParameterizedConfig);

        foreach (Section section in workflow.Sections)
        {
            hash.Add(section.ParameterizedConfig);

            foreach (Block b in section.Blocks)
            {
                hash.Add(b.ReplacementTag);

                foreach (TextInput textInput in b.TextInputs)
                {
                    hash.Add(textInput.ReplacementTag);
                }

                if (b.Properties is IniaAi.Domain.Entities.Workflows.Blocks.Properties.LoopStartProperties loopProps)
                {
                    hash.Add(loopProps.ListTitle);
                }
            }
        }

        if (workflow.Variables is not null)
        {
            foreach (WorkflowVariable variable in workflow.Variables)
            {
                hash.Add(variable.ReplacementTag);
            }
        }

        foreach (WorkflowSignatureField signatureField in workflow.SignatureFields)
        {
            hash.Add(signatureField.Name);
        }

        return hash.ToHashCode();
    }
}
