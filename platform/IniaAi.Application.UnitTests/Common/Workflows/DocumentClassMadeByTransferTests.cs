using ErrorOr;
using IniaAi.Application.Commands.WorkflowTransfer.ImportWorkflowTemplateApply;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Queries.WorkflowTransfer.ExportWorkflowTemplate;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;
using Microsoft.EntityFrameworkCore;
using Arm = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferArm;

namespace IniaAi.Application.UnitTests.Common.Workflows;

/// <summary>
/// A workflow moves between deployments by a transfer bundle or a workflow file, and its input keeps the workflow type
/// and output it is made by: the transfer keeps the type's id, so the link names the same maker there. A workflow with
/// no link serializes and hashes exactly as before the link existed, so the transfer still finds it related to its
/// copies on other deployments.
/// </summary>
public class DocumentClassMadeByTransferTests
{
    private const string ORGANIZATION_ID = "org-made-by-transfer";
    private const string USER_ID = "user-made-by-transfer";
    private const string WORKFLOW_TYPE_ID = "CorpPart2TypeA";
    private const string MAKER_TYPE_ID = "CorpPart1TypeA";
    private const string MAKER_OUTPUT = "Financial Spread Output";
    private const string MADE_BY_KEY = "\"MadeBy";

    [Fact]
    public async Task ATemplateBundle_CarriesTheLink_ToADeploymentThatHasNoMakerYet()
    {
        using var london = new SharedComponentsTestHarness();
        WorkflowTransferBundle bundle = await ExportAsync(madeBy: true);

        ErrorOr<ImportWorkflowTemplateApplyResult> imported = await new ImportWorkflowTemplateApplyCommandHandler(london.CreateFanOutService()).Handle(
            new ImportWorkflowTemplateApplyCommand(ORGANIZATION_ID, USER_ID, bundle.OriginWorkflowTypeId, bundle, null, Arm.TakeTheirs, false, false, []),
            TestContext.Current.CancellationToken);

        Assert.False(imported.IsError, string.Join("; ", imported.ErrorsOrEmptyList.Select(error => error.Description)));
        WorkflowVersion head = await london.Context.WorkflowVersions
            .SingleAsync(version => version.Id == imported.Value.Template.VersionId, TestContext.Current.CancellationToken);
        AssertMadeByMaker(Workflow.FromJson(head.WorkflowJson)!);
        AssertMadeByMaker((await london.UnitOfWork.Workflows.GetByTypeIdAsync(WORKFLOW_TYPE_ID))!);
    }

    [Fact]
    public void AWorkflowFile_CarriesTheLink()
    {
        string file = BuildWorkflow(madeBy: true).ToExportJson(includeRunTraces: true);

        AssertMadeByMaker(Workflow.FromJson(file)!);
    }

    [Fact]
    public void AWorkflowWithoutALink_SerializesAndHashesExactlyAsBefore_AndALinkChangesTheHash()
    {
        var canonicalizer = new WorkflowCanonicalizer();
        Workflow workflow = BuildWorkflow(madeBy: false);
        string json = canonicalizer.ToCanonicalJson(workflow);
        string hash = canonicalizer.ComputeContentHash(workflow);

        Assert.DoesNotContain(MADE_BY_KEY, json, StringComparison.Ordinal);
        Assert.DoesNotContain(MADE_BY_KEY, workflow.ToExportJson(includeRunTraces: true), StringComparison.Ordinal);
        Assert.NotEqual(hash, canonicalizer.ComputeContentHash(BuildWorkflow(madeBy: true)));
    }

    private static async Task<WorkflowTransferBundle> ExportAsync(bool madeBy)
    {
        using var staging = new SharedComponentsTestHarness();
        _ = staging.Context.Add(new WorkflowType(WORKFLOW_TYPE_ID, "INIA Corporate Analysis - Part 2"));
        _ = await staging.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        (_, SaveWorkflowCommandHandler saveHandler) = staging.CreateRealSaveSender();
        ErrorOr<SaveWorkflowResult> saved = await saveHandler.Handle(
            new SaveWorkflowCommand(ORGANIZATION_ID, USER_ID, WORKFLOW_TYPE_ID, BuildWorkflow(madeBy)),
            TestContext.Current.CancellationToken);
        Assert.False(saved.IsError, string.Join("; ", saved.ErrorsOrEmptyList.Select(error => error.Description)));

        ErrorOr<ExportWorkflowTemplateResult> exported = await new ExportWorkflowTemplateQueryHandler(staging.UnitOfWork)
            .Handle(new ExportWorkflowTemplateQuery(ORGANIZATION_ID, USER_ID, WORKFLOW_TYPE_ID), TestContext.Current.CancellationToken);
        Assert.False(exported.IsError, string.Join("; ", exported.ErrorsOrEmptyList.Select(error => error.Description)));

        return exported.Value.Bundle;
    }

    private static void AssertMadeByMaker(Workflow workflow)
    {
        DocumentClass spreads = Assert.Single(workflow.DocumentClasses);
        Assert.Equal(MAKER_TYPE_ID, spreads.MadeByWorkflowTypeId);
        Assert.Equal(MAKER_OUTPUT, spreads.MadeByDocumentClassKey);
    }

    private static Workflow BuildWorkflow(bool madeBy)
    {
        var workflow = new Workflow("Part 2") { Title = "Part 2" };
        _ = new DocumentClass(workflow, "CorporateFinancialModel")
        {
            DisplayName = "Financial Spreads",
            MadeByWorkflowTypeId = madeBy ? MAKER_TYPE_ID : null,
            MadeByDocumentClassKey = madeBy ? MAKER_OUTPUT : null,
        };
        var section = new Section("Body", workflow) { Title = "Body" };
        _ = new Block(BlockTypes.AiQuery, section, "summary")
        {
            Name = "Summary",
            Properties = new AiQueryProperties { UserPromptTemplate = "Summarise the documents." },
        };

        return workflow;
    }
}
