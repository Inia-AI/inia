using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.Signatures;

namespace IniaAi.Application.Commands.Signatures.TestSignaturePrompt;

/// <summary>
/// Replays a single AI Query block from a past <see cref="Domain.Entities.WorkflowExecutions.WorkflowExecution"/>
/// with one signature value swapped out. The captured prompts are read from the execution
/// snapshot, the known-old captured signature value is replaced with <see cref="TestValue"/>
/// in both user + system prompt, and the modified prompt is sent to the AI provider via
/// <see cref="AiConnection.Abstractions.IChatConnector"/>. No DB writes, no RAG, no downstream blocks.
/// </summary>
public record TestSignaturePromptCommand(
    string OrganizationId,
    string UserId,
    string WorkflowExecutionId,
    string BlockId,
    string PlaceholderName,
    string TestValue) : IRequest<ErrorOr<SignaturePromptTestResultDto>>;
