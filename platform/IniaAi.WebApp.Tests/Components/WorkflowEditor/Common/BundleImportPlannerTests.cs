using IniaAi.Application.Commands.WorkflowTransfer.ImportWorkflowTemplateApply;
using IniaAi.Application.Queries.WorkflowTransfer.ImportWorkflowTemplatePreview;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;
using IniaAi.WebApp.Components.WorkflowEditor.Common;
using Arm = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferArm;
using DomainKind = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentKind;
using Method = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferCorrelationMethod;
using Relation = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferRelation;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class BundleImportPlannerTests
{
    private const string TYPE = "WT-Target";
    private const string ORIGIN = "WT-Origin";

    private static ImportComponentPreviewRow Row(
        Relation relation,
        string id = "c1",
        string? localId = "local-1",
        string? localHeadVersionId = "head-1",
        IReadOnlyList<string>? refWarnings = null)
    {
        return new ImportComponentPreviewRow(
            id,
            relation == Relation.New ? null : localId,
            DomainKind.Block,
            "Comp",
            relation,
            relation == Relation.New ? Method.Create : Method.HashTrail,
            null,
            "bundle-hash",
            relation == Relation.New ? null : "local-hash",
            relation == Relation.New ? null : localHeadVersionId,
            ["bundle-hash"],
            relation == Relation.New ? null : ["local-hash"],
            refWarnings);
    }

    private static ImportTemplatePreviewRow TemplateRow(Relation relation)
    {
        return new ImportTemplatePreviewRow(relation, null, ["tpl-bundle"], ["tpl-local"]);
    }

    private static ImportWorkflowTemplatePreviewResult Preview(
        ImportTemplatePreviewRow template,
        params ImportComponentPreviewRow[] rows)
    {
        return new ImportWorkflowTemplatePreviewResult(TYPE, ORIGIN, "base-1", template, rows, []);
    }

    private static WorkflowTransferBundle Bundle()
    {
        return new WorkflowTransferBundle(
            1, ORIGIN, "Origin Type", null, DateTime.UtcNow, "u1", "{}", [], [], []);
    }

    [Theory]
    [InlineData(Relation.New, BundleRowControl.AutoCreate, Arm.TakeTheirs)]
    [InlineData(Relation.InSync, BundleRowControl.NoOp, Arm.KeepMine)]
    [InlineData(Relation.LocalAhead, BundleRowControl.Choice, Arm.KeepMine)]
    [InlineData(Relation.BundleAhead, BundleRowControl.Choice, Arm.TakeTheirs)]
    [InlineData(Relation.LocalRetired, BundleRowControl.LockedKeepMine, Arm.KeepMine)]
    public void PlanComponents_MapsRelationToControlAndDefault(
        Relation relation,
        BundleRowControl expectedControl,
        Arm expectedDefault)
    {
        ImportWorkflowTemplatePreviewResult preview =
            Preview(TemplateRow(Relation.InSync), Row(relation));

        BundleImportRowPlan plan = Assert.Single(BundleImportPlanner.PlanComponents(preview));

        Assert.Equal(expectedControl, plan.Control);
        Assert.Equal(expectedDefault, plan.DefaultArm);
    }

    [Fact]
    public void PlanComponents_Diverged_ForcesExplicitPick()
    {
        ImportWorkflowTemplatePreviewResult preview =
            Preview(TemplateRow(Relation.InSync), Row(Relation.Diverged));

        BundleImportRowPlan plan = Assert.Single(BundleImportPlanner.PlanComponents(preview));

        Assert.Equal(BundleRowControl.Choice, plan.Control);
        Assert.Null(plan.DefaultArm);
        Assert.False(BundleImportPlanner.IsReadyToApply(
            BundleImportPlanner.PlanTemplate(preview), null, [plan], new Dictionary<string, Arm>()));
        Assert.True(BundleImportPlanner.IsReadyToApply(
            BundleImportPlanner.PlanTemplate(preview),
            null,
            [plan],
            new Dictionary<string, Arm> { ["c1"] = Arm.KeepMine }));
    }

    [Fact]
    public void PlanComponents_LocalRetired_DisablesTakeTheirs()
    {
        ImportWorkflowTemplatePreviewResult preview =
            Preview(TemplateRow(Relation.InSync), Row(Relation.LocalRetired));

        BundleImportRowPlan plan = Assert.Single(BundleImportPlanner.PlanComponents(preview));

        Assert.True(plan.TakeTheirsDisabled);
        Assert.NotNull(plan.TakeTheirsDisabledReason);
    }

    [Fact]
    public void PlanComponents_RefWarnings_DisableTakeTheirsAndDegradeDefaults()
    {
        string warning = "References component 'x' ... [SharedComponent.KindMismatch]";
        ImportWorkflowTemplatePreviewResult preview = Preview(
            TemplateRow(Relation.InSync),
            Row(Relation.BundleAhead, id: "c1", refWarnings: [warning]),
            Row(Relation.Diverged, id: "c2", localId: "local-2", localHeadVersionId: "head-2", refWarnings: [warning]));

        IReadOnlyList<BundleImportRowPlan> plans = BundleImportPlanner.PlanComponents(preview);

        BundleImportRowPlan bundleAhead = plans.Single(p => p.Row.BundleComponentId == "c1");
        Assert.True(bundleAhead.TakeTheirsDisabled);
        Assert.Contains("SharedComponent.KindMismatch", bundleAhead.TakeTheirsDisabledReason);
        Assert.Equal(Arm.KeepMine, bundleAhead.DefaultArm);

        BundleImportRowPlan diverged = plans.Single(p => p.Row.BundleComponentId == "c2");
        Assert.Equal(Arm.KeepMine, diverged.DefaultArm);
        Assert.True(BundleImportPlanner.IsReadyToApply(
            BundleImportPlanner.PlanTemplate(preview), null, plans, new Dictionary<string, Arm>()));
    }

    [Theory]
    [InlineData(Relation.New, true, BundleRowControl.AutoCreate)]
    [InlineData(Relation.InSync, false, BundleRowControl.NoOp)]
    [InlineData(Relation.LocalAhead, false, BundleRowControl.Choice)]
    [InlineData(Relation.BundleAhead, false, BundleRowControl.Choice)]
    public void PlanTemplate_MapsRelation(Relation relation, bool willCreate, BundleRowControl control)
    {
        ImportWorkflowTemplatePreviewResult preview = Preview(TemplateRow(relation));

        BundleTemplatePlan plan = BundleImportPlanner.PlanTemplate(preview);

        Assert.Equal(willCreate, plan.WillCreate);
        Assert.Equal(control, plan.Control);
    }

    [Fact]
    public void PlanTemplate_Diverged_BlocksApplyUntilPicked()
    {
        ImportWorkflowTemplatePreviewResult preview = Preview(TemplateRow(Relation.Diverged));
        BundleTemplatePlan plan = BundleImportPlanner.PlanTemplate(preview);

        Assert.Null(plan.DefaultArm);
        Assert.False(BundleImportPlanner.IsReadyToApply(plan, null, [], new Dictionary<string, Arm>()));
        Assert.True(BundleImportPlanner.IsReadyToApply(plan, Arm.TakeTheirs, [], new Dictionary<string, Arm>()));
    }

    [Fact]
    public void BuildApplyCommand_SendsDecisionsOnlyForTakeTheirsWithHeadTokens()
    {
        ImportWorkflowTemplatePreviewResult preview = Preview(
            TemplateRow(Relation.BundleAhead),
            Row(Relation.BundleAhead, id: "c-take", localId: "local-t", localHeadVersionId: "head-t"),
            Row(Relation.LocalAhead, id: "c-keep", localId: "local-k", localHeadVersionId: "head-k"),
            Row(Relation.New, id: "c-new", localId: null, localHeadVersionId: null),
            Row(Relation.Diverged, id: "c-pick", localId: "local-d", localHeadVersionId: "head-d"));

        ImportWorkflowTemplateApplyCommand command = BundleImportPlanner.BuildApplyCommand(
            "org-1",
            "user-1",
            TYPE,
            Bundle(),
            BundleImportPlanner.PlanComponents(preview),
            preview.BaseVersionId,
            Arm.TakeTheirs,
            new Dictionary<string, Arm> { ["c-pick"] = Arm.TakeTheirs },
            acknowledgeUnrelatedHistory: true,
            acknowledgeSignatureOrphans: false);

        Assert.Equal("org-1", command.OrganizationId);
        Assert.Equal("user-1", command.UserId);
        Assert.Equal(TYPE, command.WorkflowTypeId);
        Assert.Equal("base-1", command.BaseVersionId);
        Assert.Equal(Arm.TakeTheirs, command.TemplateArm);
        Assert.True(command.AcknowledgeUnrelatedHistory);
        Assert.False(command.AcknowledgeSignatureOrphans);

        Assert.Equal(2, command.ComponentDecisions.Count);
        ImportComponentDecision takeDefault = command.ComponentDecisions.Single(d => d.ComponentId == "c-take");
        Assert.Equal(Arm.TakeTheirs, takeDefault.Arm);
        Assert.Equal("head-t", takeDefault.ExpectedLocalHeadVersionId);
        ImportComponentDecision picked = command.ComponentDecisions.Single(d => d.ComponentId == "c-pick");
        Assert.Equal("head-d", picked.ExpectedLocalHeadVersionId);
    }

    [Fact]
    public void EffectiveArm_CoercesDisabledTakeTheirsToKeepMine()
    {
        ImportWorkflowTemplatePreviewResult preview = Preview(
            TemplateRow(Relation.InSync),
            Row(Relation.BundleAhead, refWarnings: ["blocked [SharedComponent.Retired]"]));

        BundleImportRowPlan plan = Assert.Single(BundleImportPlanner.PlanComponents(preview));
        Arm effective = BundleImportPlanner.EffectiveArm(
            plan, new Dictionary<string, Arm> { ["c1"] = Arm.TakeTheirs });

        Assert.Equal(Arm.KeepMine, effective);
    }
}
