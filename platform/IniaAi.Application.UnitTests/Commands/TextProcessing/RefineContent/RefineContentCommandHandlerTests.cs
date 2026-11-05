using ErrorOr;
using IniaAi.AiConnection.Common;
using IniaAi.Application.Commands.TextProcessing.RefineContent;
using IniaAi.Application.Commands.TextProcessing.RefineContent.Models;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Configuration;
using IniaAi.Services.ContentRefinement;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.UnitTests.Commands.TextProcessing.RefineContent;

/// <summary>
/// The refine handler over the real refinement service, with the model answered by a script.
/// </summary>
public class RefineContentCommandHandlerTests
{
    private const string ORG = "org-1";
    private const string USER = "user-1";
    private const string REFINED = "<refined_content>refined</refined_content><explanation>explanation</explanation>";

    private readonly TestTokenUsageService _tokenUsage = new();
    private readonly RequestModelUse _modelUse = new();

    [Fact]
    public async Task Handle_WhenTheModelRefines_ReturnsTheRefinedContent()
    {
        ErrorOr<RefineContentResult> result = await HandleAsync(ScriptedChatConnector.Answering(REFINED));

        Assert.False(result.IsError);
        Assert.Equal("refined", result.Value.RefinedContent);
        Assert.Equal("explanation", result.Value.Explanation);
        Assert.False(result.Value.IsOutOfScope);
    }

    [Fact]
    public async Task Handle_WhenTheModelSaysOutOfScope_PropagatesIsOutOfScope()
    {
        ErrorOr<RefineContentResult> result = await HandleAsync(ScriptedChatConnector.Answering("<out_of_scope>true</out_of_scope>"));

        Assert.False(result.IsError);
        Assert.True(result.Value.IsOutOfScope);
        Assert.Null(result.Value.RefinedContent);
        Assert.Null(result.Value.Explanation);
    }

    [Fact]
    public async Task Handle_WhenTheModelAnswersNothingUsable_ReturnsUpstreamEmptyError()
    {
        ErrorOr<RefineContentResult> result = await HandleAsync(ScriptedChatConnector.Answering(string.Empty));

        Assert.True(result.IsError);
        // The controller layer relies on this exact code to translate to HTTP 502.
        Assert.Equal("ContentRefinement.UpstreamEmpty", result.FirstError.Code);
        Assert.Equal(ErrorType.Failure, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_WhenTheContentIsEmpty_ReturnsValidationError_WithoutCallingTheModel()
    {
        ScriptedChatConnector model = ScriptedChatConnector.Answering(REFINED);

        ErrorOr<RefineContentResult> result = await HandleAsync(model, content: " ");

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Contains("Content is required", result.FirstError.Description);
        Assert.Empty(model.Calls);
        Assert.False(_modelUse.ModelCalled);
        Assert.Empty(_tokenUsage.LlmRecords);
    }

    [Fact]
    public async Task Handle_WhenCancelled_Propagates()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NewHandler(ScriptedChatConnector.Answering(REFINED)).Handle(Command("current text"), cancelled.Token));
    }

    [Fact]
    public async Task Handle_WhenTheModelCallThrows_ReturnsFailure_AndTheRequestShowsAModelCall()
    {
        var model = new ScriptedChatConnector(_ => throw new InvalidOperationException("The provider closed the stream after billing."));

        ErrorOr<RefineContentResult> result = await HandleAsync(model);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Failure, result.FirstError.Type);
        Assert.True(_modelUse.ModelCalled);
    }

    [Fact]
    public async Task Handle_RecordsTheUsageOfEachRequestOnce_WithTheCommandIdentity()
    {
        var model = ScriptedChatConnector.Answering(REFINED);
        RefineContentCommandHandler handler = NewHandler(model);

        _ = await handler.Handle(Command("current text"), TestContext.Current.CancellationToken);
        _ = await handler.Handle(Command("current text"), TestContext.Current.CancellationToken);

        Assert.Equal(2, _tokenUsage.LlmRecords.Count);
        Assert.All(_tokenUsage.LlmRecords, record =>
        {
            Assert.Equal(ORG, record.OrganizationId);
            Assert.Equal(USER, record.UserId);
        });
        Assert.Equal(_tokenUsage.LlmRecords[0].Components.Sum(c => c.Units), _tokenUsage.LlmRecords[1].Components.Sum(c => c.Units));
    }

    private Task<ErrorOr<RefineContentResult>> HandleAsync(ScriptedChatConnector model, string content = "current text")
    {
        return NewHandler(model).Handle(Command(content), TestContext.Current.CancellationToken);
    }

    private RefineContentCommandHandler NewHandler(ScriptedChatConnector model)
    {
        var service = new ContentRefinementService(
            NullLogger<ContentRefinementService>.Instance,
            model,
            new TenantModelFamilyResolver(Options.Create(new PlatformSettings()), NullLogger<TenantModelFamilyResolver>.Instance),
            _modelUse);

        return new RefineContentCommandHandler(NullLogger<RefineContentCommandHandler>.Instance, service, _tokenUsage);
    }

    private static RefineContentCommand Command(string content)
    {
        return new RefineContentCommand(ORG, USER, content, "make it formal.", []);
    }
}
