using FluentValidation.TestHelper;
using IniaAi.Application.Commands.WorkflowExecutions.SubmitWorkflowExecution;
using IniaAi.WebApi.Contracts.Requests.WorkflowExecutions;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace IniaAi.Application.UnitTests.Application.Validators.WorkflowExecutions;

public class SubmitWorkflowExecutionCommandValidatorTests
{
    private readonly SubmitWorkflowExecutionCommandValidator _validator;

    public SubmitWorkflowExecutionCommandValidatorTests()
    {
        _validator = new SubmitWorkflowExecutionCommandValidator();
    }

    private static IFormFile CreateMockFile(string fileName = "test.pdf", long length = 1024)
    {
        IFormFile file = Substitute.For<IFormFile>();
        _ = file.FileName.Returns(fileName);
        _ = file.Length.Returns(length);
        return file;
    }

    private static SubmitWorkflowExecutionCommand CreateValidCommand(
        SubmitWorkflowExecutionRequest? metadata = null,
        IReadOnlyList<IFormFile>? files = null)
    {
        metadata ??= new SubmitWorkflowExecutionRequest { WorkflowTypeId = "wf-123" };
        files ??= [CreateMockFile()];

        return new SubmitWorkflowExecutionCommand(
            OrganizationId: "IniaAI",
            UserId: "user-1",
            Username: "testuser",
            Email: "test@test.com",
            Metadata: metadata,
            Files: files);
    }

    [Fact]
    public void Should_Have_Error_When_OrganizationId_Is_Empty()
    {
        SubmitWorkflowExecutionCommand command = CreateValidCommand() with { OrganizationId = "" };

        TestValidationResult<SubmitWorkflowExecutionCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor(x => x.OrganizationId);
    }

    [Fact]
    public void Should_Have_Error_When_UserId_Is_Empty()
    {
        SubmitWorkflowExecutionCommand command = CreateValidCommand() with { UserId = "" };

        TestValidationResult<SubmitWorkflowExecutionCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public void Should_Have_Error_When_Metadata_Is_Null()
    {
        SubmitWorkflowExecutionCommand command = CreateValidCommand() with { Metadata = null! };

        TestValidationResult<SubmitWorkflowExecutionCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor(x => x.Metadata);
    }

    [Fact]
    public void Should_Have_Error_When_WorkflowId_Is_Empty()
    {
        var metadata = new SubmitWorkflowExecutionRequest { WorkflowTypeId = "" };
        SubmitWorkflowExecutionCommand command = CreateValidCommand(metadata: metadata);

        TestValidationResult<SubmitWorkflowExecutionCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor("Metadata.WorkflowTypeId");
    }

    [Fact]
    public void Should_Have_Error_When_Files_Is_Empty()
    {
        SubmitWorkflowExecutionCommand command = CreateValidCommand(files: Array.Empty<IFormFile>());

        TestValidationResult<SubmitWorkflowExecutionCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor(x => x.Files);
    }

    [Fact]
    public void Should_Have_Error_When_Metadata_Files_Count_Exceeds_Actual_Files()
    {
        var metadata = new SubmitWorkflowExecutionRequest
        {
            WorkflowTypeId = "wf-123",
            Files =
            [
                new SubmitFileMetadata { DocumentClass = "ClassA" },
                new SubmitFileMetadata { DocumentClass = "ClassB" },
                new SubmitFileMetadata { DocumentClass = "ClassC" },
            ]
        };
        IReadOnlyList<IFormFile> files = [CreateMockFile(), CreateMockFile("test2.pdf")];
        SubmitWorkflowExecutionCommand command = CreateValidCommand(metadata: metadata, files: files);

        TestValidationResult<SubmitWorkflowExecutionCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor(x => x.Files);
    }

    [Fact]
    public void Should_Have_Error_When_PeerCompanyIds_Exceed_Maximum()
    {
        var metadata = new SubmitWorkflowExecutionRequest
        {
            WorkflowTypeId = "wf-123",
            PeerCompanyIds = ["p1", "p2", "p3", "p4"]
        };
        SubmitWorkflowExecutionCommand command = CreateValidCommand(metadata: metadata);

        TestValidationResult<SubmitWorkflowExecutionCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor("Metadata.PeerCompanyIds");
    }

    [Fact]
    public void Should_Have_Error_When_CounterpartyId_In_PeerCompanyIds()
    {
        var metadata = new SubmitWorkflowExecutionRequest
        {
            WorkflowTypeId = "wf-123",
            CounterpartyId = "cp-1",
            PeerCompanyIds = ["cp-1", "other"]
        };
        SubmitWorkflowExecutionCommand command = CreateValidCommand(metadata: metadata);

        TestValidationResult<SubmitWorkflowExecutionCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor("Metadata.CounterpartyId");
    }

    [Fact]
    public void Should_Not_Have_Error_When_Valid()
    {
        var metadata = new SubmitWorkflowExecutionRequest
        {
            WorkflowTypeId = "wf-123",
            CounterpartyId = "cp-1",
            PeerCompanyIds = ["peer-1"],
            Files =
            [
                new SubmitFileMetadata { DocumentClass = "Image" }
            ]
        };
        SubmitWorkflowExecutionCommand command = CreateValidCommand(metadata: metadata);

        TestValidationResult<SubmitWorkflowExecutionCommand> result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Not_Have_Error_When_Metadata_Files_Is_Null()
    {
        var metadata = new SubmitWorkflowExecutionRequest
        {
            WorkflowTypeId = "wf-123",
            Files = null
        };
        SubmitWorkflowExecutionCommand command = CreateValidCommand(metadata: metadata);

        TestValidationResult<SubmitWorkflowExecutionCommand> result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Not_Have_Error_When_Fewer_Metadata_Files_Than_Actual_Files()
    {
        var metadata = new SubmitWorkflowExecutionRequest
        {
            WorkflowTypeId = "wf-123",
            Files =
            [
                new SubmitFileMetadata { DocumentClass = "Image" }
            ]
        };
        IReadOnlyList<IFormFile> files = [CreateMockFile(), CreateMockFile("test2.pdf")];
        SubmitWorkflowExecutionCommand command = CreateValidCommand(metadata: metadata, files: files);

        TestValidationResult<SubmitWorkflowExecutionCommand> result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_IdempotencyKey_Exceeds_MaxLength()
    {
        SubmitWorkflowExecutionCommand command = CreateValidCommand() with
        {
            IdempotencyKey = new string('x', 65)
        };

        TestValidationResult<SubmitWorkflowExecutionCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor(x => x.IdempotencyKey);
    }

    [Fact]
    public void Should_Not_Have_Error_When_IdempotencyKey_Is_64_Characters()
    {
        SubmitWorkflowExecutionCommand command = CreateValidCommand() with
        {
            IdempotencyKey = new string('x', 64)
        };

        TestValidationResult<SubmitWorkflowExecutionCommand> result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("monitoring.eml")]
    [InlineData("monitoring.MSG")]
    [InlineData("message.dat")]
    [InlineData("report.pdf")]
    public void Should_Leave_The_Bytes_To_The_Handler_When_ExpandEmail_Is_Set(string fileName)
    {
        var metadata = new SubmitWorkflowExecutionRequest
        {
            WorkflowTypeId = "wf-123",
            Files = [new SubmitFileMetadata { ExpandEmail = true }],
        };
        SubmitWorkflowExecutionCommand command = CreateValidCommand(metadata, [CreateMockFile(fileName)]);

        TestValidationResult<SubmitWorkflowExecutionCommand> result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_DocumentClass_Exceeds_Max_Length()
    {
        var metadata = new SubmitWorkflowExecutionRequest
        {
            WorkflowTypeId = "wf-123",
            Files = [new SubmitFileMetadata { DocumentClass = new string('x', 101) }],
        };
        SubmitWorkflowExecutionCommand command = CreateValidCommand(metadata);

        TestValidationResult<SubmitWorkflowExecutionCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor("Metadata.Files[0].DocumentClass");
    }

    [Fact]
    public void Should_Have_Error_When_AttachmentsDocumentClass_Exceeds_Max_Length()
    {
        var metadata = new SubmitWorkflowExecutionRequest
        {
            WorkflowTypeId = "wf-123",
            Files = [new SubmitFileMetadata { ExpandEmail = true, AttachmentsDocumentClass = new string('x', 101) }],
        };
        SubmitWorkflowExecutionCommand command = CreateValidCommand(metadata, [CreateMockFile("monitoring.eml")]);

        TestValidationResult<SubmitWorkflowExecutionCommand> result = _validator.TestValidate(command);

        _ = result.ShouldHaveValidationErrorFor("Metadata.Files[0].AttachmentsDocumentClass");
    }
}
