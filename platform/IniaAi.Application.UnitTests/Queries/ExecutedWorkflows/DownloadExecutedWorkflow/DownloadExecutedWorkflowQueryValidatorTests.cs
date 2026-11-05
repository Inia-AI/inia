using FluentValidation.TestHelper;
using IniaAi.Application.Queries.ExecutedWorkflows.DownloadExecutedWorkflow;

namespace IniaAi.Application.UnitTests.Queries.ExecutedWorkflows.DownloadExecutedWorkflow;

public class DownloadExecutedWorkflowQueryValidatorTests
{
    private readonly DownloadExecutedWorkflowQueryValidator _validator = new();

    [Fact]
    public void Should_Have_Error_When_OrganizationId_Is_Empty()
    {
        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: "",
            UserId: "user-1",
            ExecutionId: "exec-1",
            WorkflowId: null,
            JobId: null);

        TestValidationResult<DownloadExecutedWorkflowQuery> result = _validator.TestValidate(query);

        _ = result.ShouldHaveValidationErrorFor(x => x.OrganizationId);
    }

    [Fact]
    public void Should_Have_Error_When_UserId_Is_Empty()
    {
        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: "org-1",
            UserId: "",
            ExecutionId: "exec-1",
            WorkflowId: null,
            JobId: null);

        TestValidationResult<DownloadExecutedWorkflowQuery> result = _validator.TestValidate(query);

        _ = result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public void Should_Have_Error_When_No_Identifier_Is_Provided()
    {
        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: "org-1",
            UserId: "user-1",
            ExecutionId: null,
            WorkflowId: null,
            JobId: null);

        TestValidationResult<DownloadExecutedWorkflowQuery> result = _validator.TestValidate(query);

        _ = result.ShouldHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void Should_Have_Error_When_Multiple_Identifiers_Are_Provided()
    {
        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: "org-1",
            UserId: "user-1",
            ExecutionId: "exec-1",
            WorkflowId: "wf-1",
            JobId: null);

        TestValidationResult<DownloadExecutedWorkflowQuery> result = _validator.TestValidate(query);

        _ = result.ShouldHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void Should_Have_Error_When_All_Three_Identifiers_Are_Provided()
    {
        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: "org-1",
            UserId: "user-1",
            ExecutionId: "exec-1",
            WorkflowId: "wf-1",
            JobId: "job-1");

        TestValidationResult<DownloadExecutedWorkflowQuery> result = _validator.TestValidate(query);

        _ = result.ShouldHaveValidationErrorFor(x => x);
    }

    [Theory]
    [InlineData("exec-1", null, null)]
    [InlineData(null, "wf-1", null)]
    [InlineData(null, null, "job-1")]
    public void Should_Pass_With_Exactly_One_Identifier(string? executionId, string? workflowId, string? jobId)
    {
        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: "org-1",
            UserId: "user-1",
            ExecutionId: executionId,
            WorkflowId: workflowId,
            JobId: jobId);

        TestValidationResult<DownloadExecutedWorkflowQuery> result = _validator.TestValidate(query);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Treat_Whitespace_Identifier_As_Missing()
    {
        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId: "org-1",
            UserId: "user-1",
            ExecutionId: "   ",
            WorkflowId: null,
            JobId: null);

        TestValidationResult<DownloadExecutedWorkflowQuery> result = _validator.TestValidate(query);

        _ = result.ShouldHaveValidationErrorFor(x => x);
    }
}
