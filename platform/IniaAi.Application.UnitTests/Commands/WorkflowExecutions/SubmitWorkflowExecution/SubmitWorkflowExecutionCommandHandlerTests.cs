using System.Text.Json;
using ErrorOr;
using IniaAi.Application.Commands.WorkflowExecutions.Shared;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Testing.Fixtures;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Contracts.Requests;
using IniaAi.WebApi.Contracts.Requests.WorkflowExecutions;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using NSubstitute;
using Harness = IniaAi.Application.UnitTests.Commands.WorkflowExecutions.SubmitWorkflowExecution.SubmitWorkflowExecutionHarness;

namespace IniaAi.Application.UnitTests.Commands.WorkflowExecutions.SubmitWorkflowExecution;

public sealed class SubmitWorkflowExecutionCommandHandlerTests : IDisposable
{
    private const string TEST_FILE = "test.pdf";

    private readonly Harness _harness = new();

    public void Dispose()
    {
        _harness.Dispose();
    }

    private static (string Tag, string? DefaultValue, bool IsRequired, WorkflowVariableTypes Type) Text(string tag, string? defaultValue, bool isRequired = false)
    {
        return (tag, defaultValue, isRequired, WorkflowVariableTypes.String);
    }

    private static JsonElement Str(string value)
    {
        return JsonSerializer.SerializeToElement(value);
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_ReturnsNotFound()
    {
        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync(
            new SubmitWorkflowExecutionRequest { WorkflowTypeId = "wf-missing" },
            [Harness.Pdf(TEST_FILE)]);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task WhileADeployPausesRuns_ASubmissionIsRefusedBeforeItsFilesAreStored()
    {
        await _harness.SeedWorkflowAsync(Harness.WorkflowWith());
        _harness.Intake.Pause(TimeSpan.FromMinutes(30));

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync([Harness.Pdf(TEST_FILE)]);

        Assert.Equal(RunIntake.PAUSED_CODE, Assert.Single(result.Errors).Code);
        Assert.Empty(await _harness.StoredFileNamesAsync());
        Assert.Empty(_harness.Orchestrator.ReceivedCalls());
    }

    [Fact]
    public async Task WhileADeployPausesRuns_ARetryWithItsIdempotencyKey_IsLeftToTheRunsOwnCheck()
    {
        await _harness.SeedWorkflowAsync(Harness.WorkflowWith());
        _harness.Intake.Pause(TimeSpan.FromMinutes(30));

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync([Harness.Pdf(TEST_FILE)], idempotencyKey: "retry-me");

        Assert.False(result.IsError);
        Assert.Equal("retry-me", _harness.SubmittedIdempotencyKey());
    }

    [Fact]
    public async Task Handle_WhenTheOrganizationHasNoAccessToTheType_AnswersAsForAnUnknownWorkflow_AndStartsNothing()
    {
        await _harness.SeedWorkflowWithoutAccessAsync(Harness.WorkflowWith());

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync([Harness.Pdf(TEST_FILE)]);

        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("Workflow not found.", error.Description);
        Assert.Empty(await _harness.StoredFileNamesAsync());
        Assert.Empty(_harness.Orchestrator.ReceivedCalls());
    }

    [Fact]
    public async Task Handle_ByTheWorkflowRecordId_StillChecksTheOrganizationsAccessToItsType()
    {
        await _harness.SeedWorkflowWithoutAccessAsync(Harness.WorkflowWith());

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync(
            new SubmitWorkflowExecutionRequest { WorkflowTypeId = Harness.WORKFLOW_ID },
            [Harness.Pdf(TEST_FILE)]);

        Assert.Equal(ErrorType.NotFound, Assert.Single(result.Errors).Type);
        Assert.Empty(_harness.Orchestrator.ReceivedCalls());
    }

    [Fact]
    public async Task Handle_WhenFileUploadFails_ReturnsErrorWithFileIndex()
    {
        await _harness.SeedWorkflowAsync(Harness.WorkflowWith());

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync([Harness.Pdf("good.pdf"), Harness.File("bad.pdf", Harness.NotAPdf)]);

        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal("files[1]", error.Code);
        Assert.StartsWith("bad.pdf: ", error.Description, StringComparison.Ordinal);
        Assert.Empty(await _harness.StoredFileNamesAsync());
        Assert.Empty(_harness.Orchestrator.ReceivedCalls());
    }

    [Fact]
    public async Task Handle_WhenFileUploadFails_LeavesAReusedDocumentInPlace()
    {
        await _harness.SeedWorkflowAsync(Harness.WorkflowWith());
        FileDto known = await _harness.UploadAsync(Harness.Pdf("known.pdf", "Known report"));

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync(
            [Harness.Pdf("known.pdf", "Known report"), Harness.File("bad.pdf", Harness.NotAPdf)]);

        Error error = Assert.Single(result.Errors);
        Assert.Equal("files[1]", error.Code);
        Assert.Equal(["known.pdf"], await _harness.FileNamesOfAsync([known.Id]));
        Assert.Empty(_harness.Orchestrator.ReceivedCalls());
    }

    [Fact]
    public async Task Handle_WhenEveryFileIsRefused_NamesEachFile_AndStoresNothing()
    {
        await _harness.SeedWorkflowAsync(Harness.WorkflowWith());

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync(
            [Harness.File("first.exe", HostileBytes.WindowsExecutable()), Harness.File("second.pdf", Harness.NotAPdf)]);

        Assert.Equal(2, result.Errors.Count);
        Assert.All(result.Errors, e => Assert.Equal(ErrorType.Validation, e.Type));
        Assert.Equal(["files[0]", "files[1]"], result.Errors.Select(e => e.Code));
        Assert.StartsWith("first.exe: ", result.Errors[0].Description, StringComparison.Ordinal);
        Assert.StartsWith("second.pdf: ", result.Errors[1].Description, StringComparison.Ordinal);
        Assert.Empty(await _harness.StoredFileNamesAsync());
        Assert.Empty(_harness.Orchestrator.ReceivedCalls());
    }

    /// <summary>
    /// A blank execution identifier beside a case names the case: a blank string carries nothing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Handle_NamingAFreezeSourceCase_InABuildWithoutValidation_RefusesAndStartsNothing(string? executionId)
    {
        await _harness.SeedWorkflowAsync(Harness.WorkflowWith());

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync(
            new SubmitWorkflowExecutionRequest
            {
                WorkflowTypeId = Harness.WORKFLOW_TYPE_ID,
                FreezeSourceExecutionId = executionId,
                FreezeSourceCaseId = "case-1",
            },
            [Harness.Pdf(TEST_FILE)]);

        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(NoCaseFreezeSources.NOT_IN_BUILD_CODE, error.Code);
        Assert.Equal("Replay from a validation case is not part of this build.", error.Description);
        Assert.Empty(await _harness.StoredFileNamesAsync());
        Assert.Empty(_harness.Orchestrator.ReceivedCalls());
    }

    /// <summary>
    /// The validator answers this 400 before the handler runs, so reaching here is a programming
    /// error — and dropping one of the two silently would stamp the execution as if no case were
    /// involved.
    /// </summary>
    [Fact]
    public async Task Handle_NamingBothFreezeSources_IsAProgrammingError()
    {
        await _harness.SeedWorkflowAsync(Harness.WorkflowWith());

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.SubmitAsync(
            new SubmitWorkflowExecutionRequest
            {
                WorkflowTypeId = Harness.WORKFLOW_TYPE_ID,
                FreezeSourceExecutionId = "exec-1",
                FreezeSourceCaseId = "case-1",
            },
            [Harness.Pdf(TEST_FILE)]));
    }

    [Fact]
    public async Task Handle_MergesVariableDefaults_WhenNotSupplied()
    {
        await _harness.SeedWorkflowAsync(Harness.WorkflowWith(
            Text("requiredVar", "default-value", isRequired: true),
            Text("optionalVar", "opt-default")));

        _ = await _harness.SubmitAsync([Harness.Pdf(TEST_FILE)]);

        Dictionary<string, JsonElement> variables = _harness.SubmittedVariables();
        Assert.True(variables.ContainsKey("requiredVar"));
        Assert.True(variables.ContainsKey("optionalVar"));
    }

    [Fact]
    public async Task Handle_HappyPath_UploadsFilesAndCallsOrchestrator()
    {
        await _harness.SeedWorkflowAsync(Harness.WorkflowWith());
        var metadata = new SubmitWorkflowExecutionRequest
        {
            WorkflowTypeId = Harness.WORKFLOW_TYPE_ID,
            CounterpartyId = "cp-1",
            Files =
            [
                new SubmitFileMetadata { DocumentClass = "Image" },
                new SubmitFileMetadata { DocumentClass = "Report" },
            ],
        };

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync(
            metadata,
            [Harness.Pdf("doc1.pdf", "First report"), Harness.Pdf("doc2.pdf", "Second report")],
            idempotencyKey: "idem-key-1");

        Assert.False(result.IsError);
        Assert.Equal("exec-1", result.Value.Id);
        Assert.Equal(Harness.WORKFLOW_ID, _harness.SubmittedWorkflow().Id);
        List<PostDocumentInfoRequest> files = _harness.SubmittedFiles();
        Assert.Equal(["doc1.pdf", "doc2.pdf"], await _harness.FileNamesOfAsync(files.Select(f => f.FileId)));
        Assert.Equal(["Image", "Report"], files.Select(f => f.DocumentClass));
        Assert.Equal((Harness.ORGANIZATION_ID, Harness.USER_ID, Harness.USERNAME, Harness.EMAIL, "cp-1", false), _harness.SubmittedRequester());
        Assert.Equal("idem-key-1", _harness.SubmittedIdempotencyKey());
    }

    [Fact]
    public async Task Handle_FilesWithoutMetadata_AssignEmptyDocumentClass()
    {
        await _harness.SeedWorkflowAsync(Harness.WorkflowWith());

        _ = await _harness.SubmitAsync([Harness.Pdf(TEST_FILE)]);

        PostDocumentInfoRequest file = Assert.Single(_harness.SubmittedFiles());
        Assert.Equal(string.Empty, file.DocumentClass);
    }

    [Fact]
    public async Task Handle_MergesDecimalNumberDefaults_AsJsonNumbers()
    {
        await _harness.SeedWorkflowAsync(Harness.WorkflowWith(
            ("decimalVar", "3.14", false, WorkflowVariableTypes.Number),
            ("intVar", "42", false, WorkflowVariableTypes.Number)));

        _ = await _harness.SubmitAsync([Harness.Pdf(TEST_FILE)]);

        Dictionary<string, JsonElement> variables = _harness.SubmittedVariables();
        Assert.Equal(JsonValueKind.Number, variables["decimalVar"].ValueKind);
        Assert.Equal(3.14m, variables["decimalVar"].GetDecimal());
        Assert.Equal(JsonValueKind.Number, variables["intVar"].ValueKind);
        Assert.Equal(42, variables["intVar"].GetInt32());
    }

    [Fact]
    public async Task Handle_MergesDefaults_WithSpecialCharactersInValues()
    {
        await _harness.SeedWorkflowAsync(Harness.WorkflowWith(
            Text("stringWithQuotes", "He said \"hello\""),
            Text("pathWithBackslash", "C:\\Users\\test"),
            ("dateVar", "2025-04-15", false, WorkflowVariableTypes.DateTime)));

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync([Harness.Pdf(TEST_FILE)]);

        Assert.False(result.IsError);
        Dictionary<string, JsonElement> variables = _harness.SubmittedVariables();
        Assert.Equal("He said \"hello\"", variables["stringWithQuotes"].GetString());
        Assert.Equal("C:\\Users\\test", variables["pathWithBackslash"].GetString());
        Assert.Equal("2025-04-15T00:00", variables["dateVar"].GetString());
    }

    [Theory]
    [InlineData("InterimPeriod", "NA", "interimPeriod", "12")]
    [InlineData("FundNumber", "1", "fundNumber", "1")]
    [InlineData("InterimPeriod", "NA", "InterimPeriod", "12")]
    public async Task Handle_WhenSubmittedKeyMatchesATagInAnyCasing_BindsTheSingleCanonicalKey(string tag, string defaultValue, string submittedKey, string value)
    {
        await _harness.SeedWorkflowAsync(Harness.WorkflowWith(Text(tag, defaultValue)));

        _ = await _harness.SubmitAsync([Harness.Pdf(TEST_FILE)], variables: new() { [submittedKey] = Str(value) });

        KeyValuePair<string, JsonElement> bound = Assert.Single(_harness.SubmittedVariables());
        Assert.Equal(tag, bound.Key);
        Assert.Equal(value, bound.Value.GetString());
    }

    [Fact]
    public async Task Handle_WhenVariableGenuinelyAbsent_InjectsDefault()
    {
        await _harness.SeedWorkflowAsync(Harness.WorkflowWith(Text("InterimPeriod", "NA")));

        _ = await _harness.SubmitAsync([Harness.Pdf(TEST_FILE)], variables: new() { ["SomethingElse"] = Str("x") });

        Dictionary<string, JsonElement> effective = _harness.SubmittedVariables();
        Assert.Equal("NA", effective["InterimPeriod"].GetString());
        Assert.True(effective.ContainsKey("SomethingElse"));
    }

    [Fact]
    public async Task Handle_WhenSameVarSuppliedUnderTwoCasings_ExactCaseWins_SingleKey()
    {
        await _harness.SeedWorkflowAsync(Harness.WorkflowWith(Text("InterimPeriod", "NA")));

        _ = await _harness.SubmitAsync(
            [Harness.Pdf(TEST_FILE)],
            variables: new()
            {
                ["interimPeriod"] = Str("12"),
                ["InterimPeriod"] = Str("99"),
            });

        KeyValuePair<string, JsonElement> bound = Assert.Single(_harness.SubmittedVariables());
        Assert.Equal("InterimPeriod", bound.Key);
        Assert.Equal("99", bound.Value.GetString());
    }
}
