using ErrorOr;
using IniaAi.Application.Queries.TokenUsage.GetWorkflowTypeUsageSummary;
using IniaAi.Application.Queries.Workflows.ValidateDocumentClasses;
using IniaAi.Common.Enums;
using IniaAi.Core.Services;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Enums;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Contracts.Responses.TokenUsage;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApi.UnitTests.ReadPaths;

/// <summary>
/// A workflow type reaches an organization only through its access row, so a read that takes a
/// workflow or a type by id answers for a type the organization was never given as for one that
/// does not exist.
/// </summary>
public class WorkflowTypeAccessReadTests : ReadPathTests
{
    private const string ORGANIZATION_ID = "org-type-access";
    private const string OTHER_ORGANIZATION_ID = "org-type-access-other";
    private const string USER_ID = "user-type-access";
    private const string WITHHELD_TYPE_ID = "TypeAccessWithheld";

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        _ = services.AddSingleton<IDocumentClassValidationService, DocumentClassValidationService>();
    }

    [Fact]
    public async Task CheckingDocuments_AgainstAWorkflowOfATypeTheOrganizationWasNotGiven_AnswersNotFound()
    {
        var workflow = new Workflow("Withheld") { Id = "workflow-type-access-withheld", WorkflowTypeId = WITHHELD_TYPE_ID, OrganizationId = OTHER_ORGANIZATION_ID };
        _ = new DocumentClass(workflow, "WithheldClass") { DisplayName = "Withheld class" };
        await AddWithheldTypeAsync(workflow);
        _ = UserData.Documents.Add(new Document(new FileMetadata(ORGANIZATION_ID, "document-type-access.pdf", 100, StorageProvider.Database), MediaType.ApplicationPdf)
        {
            Id = "document-type-access",
            OrganizationId = ORGANIZATION_ID,
        });
        _ = await UserData.SaveChangesAsync(TestContext.Current.CancellationToken);

        ErrorOr<DocumentClassValidationResultDto> result = await Handler<ValidateDocumentClassesQueryHandler>().Handle(
            new ValidateDocumentClassesQuery(ORGANIZATION_ID, USER_ID, workflow.Id, ["document-type-access"]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError, "The check named the document classes of a type the organization was not given.");
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task AUsageSummary_OfATypeTheOrganizationWasNotGivenAndNeverUsed_AnswersNotFound()
    {
        await AddWithheldTypeAsync(workflow: null);

        ErrorOr<WorkflowTypeUsageSummaryDto> result = await Handler<GetWorkflowTypeUsageSummaryQueryHandler>().Handle(
            new GetWorkflowTypeUsageSummaryQuery(ORGANIZATION_ID, USER_ID, WITHHELD_TYPE_ID),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError, "The summary named a type the organization was not given and never used.");
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    private async Task AddWithheldTypeAsync(Workflow? workflow)
    {
        _ = Core.WorkflowTypes.Add(new WorkflowType(WITHHELD_TYPE_ID, "Withheld type")
        {
            OrganizationAccess = [new WorkflowTypeOrganizationAccess { WorkflowTypeId = WITHHELD_TYPE_ID, OrganizationId = OTHER_ORGANIZATION_ID }],
        });
        if (workflow is not null)
        {
            _ = Core.Workflows.Add(workflow);
        }

        _ = await Core.SaveChangesAsync(TestContext.Current.CancellationToken);
        Core.ChangeTracker.Clear();
    }
}
