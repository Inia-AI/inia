using Ardalis.Specification;
using ErrorOr;
using IniaAi.Application.Commands.WorkflowExecutions.Shared;
using IniaAi.Application.Errors;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Domain.Replay;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text.Json;

namespace IniaAi.Application.UnitTests.Commands.WorkflowExecutions.Shared;

public class ExecutionFreezeSourceResolverTests : FreezeSourceTestBase
{
    private readonly ExecutionFreezeSourceResolver _resolver;

    public ExecutionFreezeSourceResolverTests()
    {
        _resolver = new ExecutionFreezeSourceResolver(
            UnitOfWork,
            Substitute.For<ILogger<ExecutionFreezeSourceResolver>>());
    }

    [Fact]
    public async Task ResolveAsync_UnknownSource_ReturnsNotFound()
    {
        SourceIs(null);

        ErrorOr<IReadOnlyList<ReplayOutput>> result = await _resolver.ResolveAsync(
            ORG, SOURCE_ID, Target(), null, null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ExecutionFreezeErrorFactory.SOURCE_NOT_FOUND_CODE, result.FirstError.Code);
    }

    [Fact]
    public async Task ResolveAsync_SourceOfAnotherOrganization_IsNotFound()
    {
        SourceIs(CompletedSource());

        ErrorOr<IReadOnlyList<ReplayOutput>> result = await _resolver.ResolveAsync(
            "org-2", SOURCE_ID, Target(), null, null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ExecutionFreezeErrorFactory.SOURCE_NOT_FOUND_CODE, result.FirstError.Code);
    }

    [Fact]
    public async Task ResolveAsync_SourceStillRunning_ReturnsValidationError()
    {
        WorkflowExecution running = CompletedSource();
        running.WorkflowState = WorkflowState.Running;
        SourceIs(running);

        ErrorOr<IReadOnlyList<ReplayOutput>> result = await _resolver.ResolveAsync(
            ORG, SOURCE_ID, Target(), null, null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ExecutionFreezeErrorFactory.SOURCE_NOT_COMPLETED_CODE, result.FirstError.Code);
    }

    [Fact]
    public async Task ResolveAsync_WorkflowTypeMismatch_IsBlocked()
    {
        SourceIs(CompletedSource());

        ErrorOr<IReadOnlyList<ReplayOutput>> result = await _resolver.ResolveAsync(
            ORG, SOURCE_ID, Target(typeId: "another-type"), null, null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ExecutionFreezeErrorFactory.WORKFLOW_TYPE_MISMATCH_CODE, result.FirstError.Code);
    }

    [Fact]
    public async Task ResolveAsync_CounterpartyMismatch_IsBlocked()
    {
        SourceIs(CompletedSource(counterpartyId: "cp-a"));

        ErrorOr<IReadOnlyList<ReplayOutput>> result = await _resolver.ResolveAsync(
            ORG, SOURCE_ID, Target(), "cp-b", null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ExecutionFreezeErrorFactory.COUNTERPARTY_MISMATCH_CODE, result.FirstError.Code);
    }

    [Fact]
    public async Task ResolveAsync_VariableMismatch_IsBlockedAndNamesTheVariables()
    {
        SourceIs(CompletedSource(variables: new Dictionary<string, object> { ["country"] = "FR", ["year"] = 2024 }));

        ErrorOr<IReadOnlyList<ReplayOutput>> result = await _resolver.ResolveAsync(
            ORG, SOURCE_ID, Target(TYPE_ID, ("Country", "UK"), ("Year", null)), null,
            Values("""{"Country":"FR","Year":2025}"""), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ExecutionFreezeErrorFactory.VARIABLES_MISMATCH_CODE, result.FirstError.Code);
        Assert.Contains("Year", result.FirstError.Description);
        Assert.DoesNotContain("Country", result.FirstError.Description);
        Assert.Equal(["Year"], (IReadOnlyList<string>)result.FirstError.Metadata![ExecutionFreezeErrorFactory.VARIABLES_METADATA_KEY]);
    }

    [Fact]
    public async Task ResolveAsync_SourceOmittedAVariableWhoseDefaultChangedSince_IsBlocked()
    {
        SourceIs(CompletedSource(variables: new Dictionary<string, object> { ["year"] = 2025 }));
        var clone = new Workflow { Id = "clone-1" };
        _ = new WorkflowVariable(clone, "Country") { DefaultValue = "UK" };
        _ = new WorkflowVariable(clone, "Year");
        _ = WorkflowRepo.GetByIdAsync("clone-1", Arg.Any<CancellationToken>()).Returns(clone);

        ErrorOr<IReadOnlyList<ReplayOutput>> result = await _resolver.ResolveAsync(
            ORG, SOURCE_ID, Target(TYPE_ID, ("Country", "FR"), ("Year", null)), null,
            Values("""{"Year":2025}"""), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ExecutionFreezeErrorFactory.VARIABLES_MISMATCH_CODE, result.FirstError.Code);
        Assert.Equal(["Country"], (IReadOnlyList<string>)result.FirstError.Metadata![ExecutionFreezeErrorFactory.VARIABLES_METADATA_KEY]);
    }

    [Fact]
    public async Task ResolveAsync_MatchingSubmission_CapturesFreezeEligibleResponsesFromTheClone()
    {
        SourceIs(CompletedSource(
            counterpartyId: "cp-a",
            variables: new Dictionary<string, object> { ["country"] = "FR" }));

        var clone = new Workflow { Id = "clone-1" };
        _ = new WorkflowVariable(clone, "Country") { DefaultValue = "UK" };
        var section = new Section("", clone) { Title = "Fetches" };
        var frozenApi = new Block(BlockTypes.WebApiCall, section, "api") { Name = "API" };
        frozenApi.StableBlockId = "stable-api";
        _ = new BlockResponse(frozenApi) { RawResponse = "captured", JsonResponse = "{\"a\":1}" };
        var fxWithoutResponse = new Block(BlockTypes.ExchangeRate, section, "fx") { Name = "FX" };
        fxWithoutResponse.StableBlockId = "stable-fx";
        var aiQuery = new Block(BlockTypes.AiQuery, section, "ai") { Name = "AI" };
        aiQuery.StableBlockId = "stable-ai";
        _ = new BlockResponse(aiQuery) { RawResponse = "model output" };
        _ = WorkflowRepo.GetByIdAsync("clone-1", Arg.Any<CancellationToken>()).Returns(clone);

        ErrorOr<IReadOnlyList<ReplayOutput>> result = await _resolver.ResolveAsync(
            ORG, SOURCE_ID, Target(TYPE_ID, ("Country", "UK")), "cp-a",
            Values("""{"country":"FR"}"""), CancellationToken.None);

        Assert.False(result.IsError);
        ReplayOutput capture = Assert.Single(result.Value);
        Assert.Equal("stable-api", capture.StableBlockId);
        Assert.Equal("captured", capture.RawResponse);
        Assert.Equal("{\"a\":1}", capture.JsonResponse);
        Assert.Equal("Fetches", capture.SectionName);
        Assert.Equal("api", capture.ReplacementTag);
        Assert.Equal(BlockTypes.WebApiCall.ToString(), capture.BlockType);
    }

    [Fact]
    public async Task ResolveAsync_SourceWithoutLoadableClone_ReturnsAnEmptyReplaySet()
    {
        SourceIs(CompletedSource(workflowId: "gone"));
        _ = WorkflowRepo.GetByIdAsync("gone", Arg.Any<CancellationToken>()).Returns((Workflow?)null);

        ErrorOr<IReadOnlyList<ReplayOutput>> result = await _resolver.ResolveAsync(
            ORG, SOURCE_ID, Target(), null, null, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value);
    }
}
