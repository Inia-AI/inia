using ErrorOr;
using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.Signatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.Signatures.TestSignaturePrompt;

/// <summary>
/// Handles <see cref="TestSignaturePromptCommand"/>. See the command doc comment for the
/// flow overview. Read-only: no DB writes, no workflow execution, no RAG, no downstream blocks.
/// </summary>
public sealed class TestSignaturePromptCommandHandler
    : IRequestHandler<TestSignaturePromptCommand, ErrorOr<SignaturePromptTestResultDto>>
{
    internal const string ERROR_EXECUTION_NOT_FOUND = "Signatures.TestReplay.ExecutionNotFound";
    internal const string ERROR_BLOCK_NOT_FOUND = "Signatures.TestReplay.BlockNotFound";
    internal const string ERROR_BLOCK_NOT_AI_QUERY = "Signatures.TestReplay.BlockNotAiQuery";
    internal const string ERROR_PROMPT_MISSING = "Signatures.TestReplay.PromptMissing";
    internal const string ERROR_PRE_CAPTURE_RUN = "Signatures.TestReplay.PreCaptureRun";
    internal const string ERROR_PLACEHOLDER_NOT_CAPTURED = "Signatures.TestReplay.PlaceholderNotCaptured";
    internal const string ERROR_NO_CHANGE = "Signatures.TestReplay.NoChange";
    internal const string ERROR_OLD_VALUE_NOT_FOUND = "Signatures.TestReplay.OldValueNotFoundInPrompt";

    // Captured-values dict and replay prompt are both deliberately kept off the
    // wire, but we still need to inject a separator between them when we build
    // the full AI payload. No magic string.
    private const string INPUT_CONTEXT_SEPARATOR = "\n\n";

    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly IChatConnector _chatConnector;
    private readonly ILogger<TestSignaturePromptCommandHandler> _logger;

    public TestSignaturePromptCommandHandler(
        IUserDataUnitOfWork userDataUnitOfWork,
        IChatConnector chatConnector,
        ILogger<TestSignaturePromptCommandHandler> logger)
    {
        _userDataUnitOfWork = userDataUnitOfWork ?? throw new ArgumentNullException(nameof(userDataUnitOfWork));
        _chatConnector = chatConnector ?? throw new ArgumentNullException(nameof(chatConnector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ErrorOr<SignaturePromptTestResultDto>> Handle(
        TestSignaturePromptCommand request,
        CancellationToken cancellationToken)
    {
        Workflow? snapshot = await _userDataUnitOfWork.Workflows
            .GetAll()
            .AsNoTracking()
            .Where(w => w.Id == request.WorkflowExecutionId
                && w.OrganizationId == request.OrganizationId)
            .Include(w => w.Sections)
                .ThenInclude(s => s.Blocks)
            .AsSplitQuery()
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshot is null)
        {
            return Error.NotFound(ERROR_EXECUTION_NOT_FOUND, "Workflow execution not found.");
        }

        Block? targetBlock = snapshot.FindBlockById(request.BlockId);
        if (targetBlock is null)
        {
            return Error.NotFound(
                ERROR_BLOCK_NOT_FOUND,
                $"Block '{request.BlockId}' was not found in this execution.");
        }

        if (targetBlock.Type is not BlockTypes.AiQuery
            || targetBlock.Properties is not AiQueryProperties aiProps)
        {
            return Error.Validation(
                ERROR_BLOCK_NOT_AI_QUERY,
                $"Block '{request.BlockId}' is not an AI Query block.");
        }

        AiQueryPropertiesExecutionState? state = aiProps.ExecutionState;
        if (state is null || string.IsNullOrEmpty(state.UserPrompt))
        {
            return Error.Validation(
                ERROR_PROMPT_MISSING,
                "This block has no captured prompt to replay against.");
        }

        // Two distinct failure modes, two distinct error codes so the FE can say
        // something accurate:
        //   - ResolvedSignatureFieldValues is null → the entire run predates the
        //     capture code. Advice: re-run the workflow.
        //   - dict exists but no entry for this placeholder → something went
        //     wrong for this one field (stale FE, typo, oversized-and-skipped,
        //     signature field renamed/removed between capture and replay).
        //     Advice: refresh, don't re-run.
        if (state.ResolvedSignatureFieldValues is null)
        {
            return Error.Conflict(
                ERROR_PRE_CAPTURE_RUN,
                "This execution predates signature replay. Run the workflow again to use this feature.");
        }

        if (!state.ResolvedSignatureFieldValues.TryGetValue(request.PlaceholderName, out string? oldValue)
            || string.IsNullOrEmpty(oldValue))
        {
            return Error.Conflict(
                ERROR_PLACEHOLDER_NOT_CAPTURED,
                $"No captured value for placeholder '{request.PlaceholderName}' on this execution.");
        }

        if (string.Equals(oldValue, request.TestValue, StringComparison.Ordinal))
        {
            return Error.Validation(
                ERROR_NO_CHANGE,
                "Test value is identical to the captured value — nothing to replay.");
        }

        int occurrencesInUser = CountOccurrences(state.UserPrompt, oldValue);
        int occurrencesInSystem = CountOccurrences(state.SystemPrompt, oldValue);
        int totalOccurrences = occurrencesInUser + occurrencesInSystem;

        if (totalOccurrences == 0)
        {
            return Error.Conflict(
                ERROR_OLD_VALUE_NOT_FOUND,
                "The captured signature value was not found in the stored prompt.");
        }

        // Modified prompts are built locally and never leave this method.
        string modifiedUserPrompt = state.UserPrompt.Replace(oldValue, request.TestValue, StringComparison.Ordinal);
        string? modifiedSystemPrompt = state.SystemPrompt is null
            ? null
            : state.SystemPrompt.Replace(oldValue, request.TestValue, StringComparison.Ordinal);

        // Prepend the captured page / document context that was sent to Anthropic
        // during the original execution. Without it the model has nothing to answer
        // against and spits a "no data was provided" response. InputContext is the
        // text dump of the RAG-filtered pages that ran through the block — not as
        // rich as the image attachments the live pipeline also sends, but enough
        // to give the model grounded content to work with.
        string contextBlock = string.IsNullOrWhiteSpace(state.InputContext)
            ? modifiedUserPrompt
            : state.InputContext + INPUT_CONTEXT_SEPARATOR + modifiedUserPrompt;

        ResponseContentSequence response = await _chatConnector.GetResponseAsync(
            contextBlock,
            modifiedSystemPrompt,
            maxTokens: aiProps.MaxTokens,
            shouldUseAdvancedModel: aiProps.ShouldUseAdvancedModel,
            model: state.ModelIdOverride,
            family: null,
            cancellationToken: cancellationToken);

        string responseText = response.GetPlainText() ?? string.Empty;

        // Use the exact model id the provider echoed back in the response
        // metadata (set by the Anthropic connector via ModelIdNormalizer.
        // MODEL_METADATA_KEY). That's the ground truth for "which model
        // actually handled this replay". state.ModelIdOverride is only the
        // per-block override and is null on most blocks; falling back to it
        // would give the FE `null` for virtually every call.
        string modelUsed = ExtractModelUsed(response) ?? state.ModelIdOverride ?? string.Empty;

        _logger.LogInformation(
            "TestSignaturePrompt: execution={ExecutionId} block={BlockId} placeholder={Placeholder} occurrencesReplaced={Occurrences} modelUsed={ModelUsed}",
            LogSanitizer.ForLog(request.WorkflowExecutionId),
            LogSanitizer.ForLog(request.BlockId),
            LogSanitizer.ForLog(request.PlaceholderName),
            totalOccurrences,
            modelUsed);

        return new SignaturePromptTestResultDto(
            OldValueUsed: oldValue,
            NewValueUsed: request.TestValue,
            OccurrencesReplaced: totalOccurrences,
            ResponseText: responseText,
            InputTokens: response.InputTokens,
            OutputTokens: response.OutputTokens,
            ModelUsed: modelUsed);
    }

    // The Anthropic chat connector writes the exact model id the provider
    // echoed back into ResponseContentSequence.Metadata under the well-known
    // key `ModelIdNormalizer.MODEL_METADATA_KEY`. Pulling from that guarantees
    // we always return the model that actually handled the call, not the
    // per-block override (which is null on most blocks).
    private static string? ExtractModelUsed(ResponseContentSequence response)
    {
        if (response.Metadata.TryGetValue(ModelIdNormalizer.MODEL_METADATA_KEY, out object? raw)
            && raw is string id
            && !string.IsNullOrWhiteSpace(id))
        {
            return id;
        }

        return null;
    }

    private static int CountOccurrences(string? haystack, string needle)
    {
        if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
        {
            return 0;
        }

        int count = 0;
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
