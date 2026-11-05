using FluentValidation.TestHelper;
using IniaAi.Application.Commands.WorkflowExecutions.StartWorkflowExecution;
using IniaAi.WebApi.Contracts.Requests;

namespace IniaAi.Application.UnitTests.Application.Validators.WorkflowExecutions;

/// <summary>
/// Unit tests for <see cref="StartWorkflowExecutionCommandValidator"/>.
/// </summary>
public class StartWorkflowExecutionCommandValidatorTests
{
    private readonly StartWorkflowExecutionCommandValidator _validator;

    /// <summary>
    /// Initializes a new instance of the <see cref="StartWorkflowExecutionCommandValidatorTests"/> class.
    /// </summary>
    public StartWorkflowExecutionCommandValidatorTests()
    {
        _validator = new StartWorkflowExecutionCommandValidator();
    }

    [Fact]
    public void Should_Have_Error_When_WorkflowId_Is_Empty()
    {
        var command = new StartWorkflowExecutionCommand(new StartWorkflowExecutionRequest
        {
            WorkflowId = "",
            Files =
        [
            new() { FileId = "file-1" }
        ]
        }, organizationId: "IniaAi", userId: "Test123", username: null, email: null);

        TestValidationResult<StartWorkflowExecutionCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor("Request.WorkflowId");
    }

    [Fact]
    public void Should_Have_Error_When_AttachmentsDocumentClass_TooLong()
    {
        var command = new StartWorkflowExecutionCommand(new StartWorkflowExecutionRequest
        {
            WorkflowId = "wf-123",
            ExpandEmails = true,
            AttachmentsDocumentClass = new string('a', 101),
        }, organizationId: "IniaAi", userId: "Test123", username: null, email: null);

        TestValidationResult<StartWorkflowExecutionCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor("Request.AttachmentsDocumentClass");
    }

    [Fact]
    public void Should_Have_Error_When_CounterpartyId_In_PeerCompanyIds()
    {
        var command = new StartWorkflowExecutionCommand(new StartWorkflowExecutionRequest
        {
            WorkflowId = "wf-123",
            Files =
        [
            new() { FileId = "file-1" }
        ],
            CounterpartyId = "cp-1",
            PeerCompanyIds = ["cp-1", "other-peer"]
        }, organizationId: "IniaAi", userId: "Test123", username: null, email: null);

        TestValidationResult<StartWorkflowExecutionCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor("Request.CounterpartyId")
            .WithErrorMessage("CounterpartyId cannot also be in the list of peer companies.");
    }

    [Fact]
    public void Should_Have_Error_When_Too_Many_PeerCompanies()
    {
        var command = new StartWorkflowExecutionCommand(new StartWorkflowExecutionRequest
        {
            WorkflowId = "wf-123",
            Files =
        [
            new() { FileId = "file-1" }
        ],
            PeerCompanyIds = ["peer-1", "peer-2", "peer-3", "peer-4"]
        }, organizationId: "IniaAi", userId: "Test123", username: null, email: null);

        TestValidationResult<StartWorkflowExecutionCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor("Request.PeerCompanyIds")
            .WithErrorMessage("A maximum of 3 peer companies is allowed.");
    }

    [Fact]
    public void Should_Have_Error_When_FileId_Is_Empty()
    {
        var command = new StartWorkflowExecutionCommand(new StartWorkflowExecutionRequest
        {
            WorkflowId = "wf-123",
            Files =
        [
            new() { FileId = "" }
        ]
        }, organizationId: "IniaAi", userId: "Test123", username: null, email: null);

        TestValidationResult<StartWorkflowExecutionCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor("Request.Files[0].FileId");
    }

    [Fact]
    public void Should_Not_Have_Error_When_Valid()
    {
        var command = new StartWorkflowExecutionCommand(new StartWorkflowExecutionRequest
        {
            WorkflowId = "wf-123",
            Files =
        [
            new() {
                FileId = "file-1",
                DocumentClass = "ClassA"
            }
        ],
            CounterpartyId = "cp-1",
            PeerCompanyIds = ["peer-1"]
        }, organizationId: "IniaAi", userId: "Test123", username: null, email: null);

        TestValidationResult<StartWorkflowExecutionCommand> result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Not_Have_Error_When_No_Files()
    {
        var command = new StartWorkflowExecutionCommand(new StartWorkflowExecutionRequest
        {
            WorkflowId = "wf-123"
        }, organizationId: "IniaAi", userId: "Test123", username: null, email: null);

        TestValidationResult<StartWorkflowExecutionCommand> result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }
}
