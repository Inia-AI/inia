using IniaAi.Application.Commands.WorkflowTransfer.ImportWorkflowTemplateApply;
using IniaAi.Application.Queries.WorkflowTransfer.ImportWorkflowTemplatePreview;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

public enum BundleRowControl
{
    AutoCreate = 0,
    NoOp = 1,
    LockedKeepMine = 2,
    Choice = 3,
}

public sealed record BundleImportRowPlan(
    ImportComponentPreviewRow Row,
    BundleRowControl Control,
    SharedComponentTransferArm? DefaultArm,
    string? TakeTheirsDisabledReason)
{
    public bool TakeTheirsDisabled => TakeTheirsDisabledReason is not null;
}

public sealed record BundleTemplatePlan(
    ImportTemplatePreviewRow Row,
    BundleRowControl Control,
    SharedComponentTransferArm? DefaultArm)
{
    public bool WillCreate => Control == BundleRowControl.AutoCreate;
}

public static class BundleImportPlanner
{
    private const string RETIRED_REASON =
        "The local component is retired; unretire it in the Library to adopt the bundle's version.";

    public static BundleTemplatePlan PlanTemplate(ImportWorkflowTemplatePreviewResult preview)
    {
        ImportTemplatePreviewRow row = preview.Template;

        return row.Relation switch
        {
            SharedComponentTransferRelation.New =>
                new BundleTemplatePlan(row, BundleRowControl.AutoCreate, SharedComponentTransferArm.TakeTheirs),
            SharedComponentTransferRelation.InSync =>
                new BundleTemplatePlan(row, BundleRowControl.NoOp, SharedComponentTransferArm.KeepMine),
            SharedComponentTransferRelation.LocalAhead =>
                new BundleTemplatePlan(row, BundleRowControl.Choice, SharedComponentTransferArm.KeepMine),
            SharedComponentTransferRelation.BundleAhead =>
                new BundleTemplatePlan(row, BundleRowControl.Choice, SharedComponentTransferArm.TakeTheirs),
            SharedComponentTransferRelation.Diverged =>
                new BundleTemplatePlan(row, BundleRowControl.Choice, null),
            _ =>
                new BundleTemplatePlan(row, BundleRowControl.Choice, SharedComponentTransferArm.KeepMine),
        };
    }

    public static IReadOnlyList<BundleImportRowPlan> PlanComponents(ImportWorkflowTemplatePreviewResult preview)
    {
        return [.. preview.Components.Select(PlanRow)];
    }

    public static bool IsReadyToApply(
        BundleTemplatePlan template,
        SharedComponentTransferArm? templatePick,
        IReadOnlyList<BundleImportRowPlan> rows,
        IReadOnlyDictionary<string, SharedComponentTransferArm> componentPicks)
    {
        if (template.Control == BundleRowControl.Choice && template.DefaultArm is null && templatePick is null)
        {
            return false;
        }

        foreach (BundleImportRowPlan row in rows)
        {
            if (row.Control == BundleRowControl.Choice
                && row.DefaultArm is null
                && !componentPicks.ContainsKey(row.Row.BundleComponentId))
            {
                return false;
            }
        }

        return true;
    }

    public static SharedComponentTransferArm EffectiveArm(
        BundleImportRowPlan row,
        IReadOnlyDictionary<string, SharedComponentTransferArm> componentPicks)
    {
        if (row.Control != BundleRowControl.Choice)
        {
            return row.DefaultArm ?? SharedComponentTransferArm.KeepMine;
        }

        SharedComponentTransferArm chosen =
            componentPicks.TryGetValue(row.Row.BundleComponentId, out SharedComponentTransferArm picked)
                ? picked
                : row.DefaultArm ?? SharedComponentTransferArm.KeepMine;

        return row.TakeTheirsDisabled && chosen == SharedComponentTransferArm.TakeTheirs
            ? SharedComponentTransferArm.KeepMine
            : chosen;
    }

    public static ImportWorkflowTemplateApplyCommand BuildApplyCommand(
        string organizationId,
        string userId,
        string workflowTypeId,
        WorkflowTransferBundle bundle,
        IReadOnlyList<BundleImportRowPlan> rowPlans,
        string? baseVersionId,
        SharedComponentTransferArm templateArm,
        IReadOnlyDictionary<string, SharedComponentTransferArm> componentPicks,
        bool acknowledgeUnrelatedHistory,
        bool acknowledgeSignatureOrphans)
    {
        List<ImportComponentDecision> decisions = [];
        foreach (BundleImportRowPlan row in rowPlans)
        {
            if (row.Control == BundleRowControl.Choice
                && EffectiveArm(row, componentPicks) == SharedComponentTransferArm.TakeTheirs)
            {
                decisions.Add(new ImportComponentDecision(
                    row.Row.BundleComponentId,
                    SharedComponentTransferArm.TakeTheirs,
                    row.Row.LocalHeadVersionId));
            }
        }

        return new ImportWorkflowTemplateApplyCommand(
            organizationId,
            userId,
            workflowTypeId,
            bundle,
            baseVersionId,
            templateArm,
            acknowledgeUnrelatedHistory,
            acknowledgeSignatureOrphans,
            decisions);
    }

    private static BundleImportRowPlan PlanRow(ImportComponentPreviewRow row)
    {
        bool refBlocked = row.RefWarnings is { Count: > 0 };
        string? refReason = refBlocked ? string.Join(" ", row.RefWarnings!) : null;

        return row.Relation switch
        {
            SharedComponentTransferRelation.New => new BundleImportRowPlan(
                row, BundleRowControl.AutoCreate, SharedComponentTransferArm.TakeTheirs, null),
            SharedComponentTransferRelation.InSync => new BundleImportRowPlan(
                row, BundleRowControl.NoOp, SharedComponentTransferArm.KeepMine, null),
            SharedComponentTransferRelation.LocalRetired => new BundleImportRowPlan(
                row, BundleRowControl.LockedKeepMine, SharedComponentTransferArm.KeepMine, RETIRED_REASON),
            SharedComponentTransferRelation.LocalAhead => new BundleImportRowPlan(
                row, BundleRowControl.Choice, SharedComponentTransferArm.KeepMine, refReason),
            SharedComponentTransferRelation.BundleAhead => new BundleImportRowPlan(
                row,
                BundleRowControl.Choice,
                refBlocked ? SharedComponentTransferArm.KeepMine : SharedComponentTransferArm.TakeTheirs,
                refReason),
            SharedComponentTransferRelation.Diverged => new BundleImportRowPlan(
                row,
                BundleRowControl.Choice,
                refBlocked ? SharedComponentTransferArm.KeepMine : null,
                refReason),
            _ => new BundleImportRowPlan(
                row, BundleRowControl.NoOp, SharedComponentTransferArm.KeepMine, null),
        };
    }
}
