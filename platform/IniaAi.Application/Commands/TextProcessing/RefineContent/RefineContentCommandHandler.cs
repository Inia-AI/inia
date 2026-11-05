using ErrorOr;
using IniaAi.Application.Commands.TextProcessing.RefineContent.Models;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Core.Services.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using Microsoft.Extensions.Logging;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Application.Commands.TextProcessing.RefineContent;

/// <summary>
/// Handles the refinement of content using AI services.
/// </summary>
public class RefineContentCommandHandler : IRequestHandler<RefineContentCommand, ErrorOr<RefineContentResult>>
{
    private const string CONTENT_REFINEMENT_FALLBACK_MODEL_ID = "content-refinement";

    private readonly ILogger<RefineContentCommandHandler> _logger;
    private readonly IContentRefinementService _contentRefinementService;
    private readonly ITokenUsageService _tokenUsageService;

    public RefineContentCommandHandler(
        ILogger<RefineContentCommandHandler> logger,
        IContentRefinementService contentRefinementService,
        ITokenUsageService tokenUsageService)
    {
        _logger = logger;
        _contentRefinementService = contentRefinementService;
        _tokenUsageService = tokenUsageService;
    }

    public async Task<ErrorOr<RefineContentResult>> Handle(
        RefineContentCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing content refinement request. Content length: {ContentLength}, Instruction length: {InstructionLength}, History turns: {HistoryCount}",
            request.Content.Length,
            request.Instruction.Length,
            request.ConversationHistory.Count);

        try
        {
            _contentRefinementService.ResetTokenAccumulator();

            ContentRefinementOutput? output = await _contentRefinementService.RefineAsync(
                request.Content,
                request.Instruction,
                request.ConversationHistory,
                request.OrganizationId,
                cancellationToken);

            RecordTokenUsage(request.OrganizationId, request.UserId);

            if (output is null)
            {
                _logger.LogError("Content refinement service returned null result");
                return Error.Failure(
                    code: "ContentRefinement.UpstreamEmpty",
                    description: "Content refinement service returned an empty response.");
            }

            _logger.LogInformation(
                "Successfully completed content refinement. IsOutOfScope: {IsOutOfScope}",
                output.IsOutOfScope);

            return new RefineContentResult
            {
                RefinedContent = output.RefinedContent,
                Explanation = output.Explanation,
                IsOutOfScope = output.IsOutOfScope,
            };
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Content refinement request failed due to invalid input");
            return Error.Validation(description: ex.Message);
        }
        catch (OperationCanceledException)
        {
            // Let cancellation propagate so the framework can surface a 499/client-closed
            // response rather than misreporting it as a generic 500 failure.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred during content refinement");
            return Error.Failure(description: "An unexpected error occurred while processing the content refinement request.");
        }
    }

    private void RecordTokenUsage(string organizationId, string userId)
    {
        LlmUsageRecord? record = LlmUsageRecordFactory.FromAccumulator(
            _contentRefinementService,
            CONTENT_REFINEMENT_FALLBACK_MODEL_ID,
            organizationId,
            userId,
            UsageType.LlmContentRefinement,
            TokenExecutionContext.ApiCall,
            "Content refinement request");

        if (record is not null)
        {
            _ = _tokenUsageService.RecordLlmUsageAsync(record, CancellationToken.None);
        }
    }
}
