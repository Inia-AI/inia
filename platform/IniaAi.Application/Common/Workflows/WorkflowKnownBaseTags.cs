using IniaAi.Common.Constants;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// The single source for "which base replacement tags can resolve on this workflow" — block
/// tags, variable tags, document-class keys, reserved inputs — shared by the structural
/// validator's placeholder warnings and the agent-template grammar so the two sets never
/// drift. Signature fields are opt-in: their values inject only for signature-bearing blocks,
/// so the per-block structural warnings must keep flagging them on other blocks, while the
/// agent grammar treats a skill's field reference as legitimate (the rename refusal guards it).
/// </summary>
public static class WorkflowKnownBaseTags
{
    public static HashSet<string> Collect(Workflow workflow, bool includeSignatureFields)
    {
        var known = new HashSet<string>(StringComparer.FromComparison(ReplacementTags.Comparison));

        foreach (Block block in workflow.GetAllBlocks())
        {
            if (!string.IsNullOrWhiteSpace(block.ReplacementTag))
            {
                _ = known.Add(block.ReplacementTag);
            }
        }

        if (workflow.Variables is not null)
        {
            foreach (WorkflowVariable variable in workflow.Variables)
            {
                if (!string.IsNullOrWhiteSpace(variable.ReplacementTag))
                {
                    _ = known.Add(variable.ReplacementTag);
                }
            }
        }

        if (workflow.DocumentClasses is not null)
        {
            foreach (DocumentClass documentClass in workflow.DocumentClasses)
            {
                if (!string.IsNullOrWhiteSpace(documentClass.Key))
                {
                    _ = known.Add(documentClass.Key);
                }
            }
        }

        if (includeSignatureFields && workflow.SignatureFields is not null)
        {
            foreach (WorkflowSignatureField field in workflow.SignatureFields)
            {
                if (!string.IsNullOrWhiteSpace(field.Name))
                {
                    _ = known.Add(field.Name);
                }
            }
        }

        foreach (string reservedTag in SharedInputKeys.ReservedReplacementTags)
        {
            _ = known.Add(reservedTag);
        }

        return known;
    }
}
