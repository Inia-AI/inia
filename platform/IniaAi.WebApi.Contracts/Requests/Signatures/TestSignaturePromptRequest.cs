namespace IniaAi.WebApi.Contracts.Requests.Signatures;

/// <summary>
/// Body for POST /workflow-executions/{id}/test-signature-prompt. The user is replaying
/// a captured AI Query block with one signature value swapped for <see cref="TestValue"/>.
/// </summary>
/// <param name="BlockId">Execution-clone block id from GetExecutionBlocks.</param>
/// <param name="PlaceholderName">The signature field placeholder to swap.</param>
/// <param name="TestValue">The new value the user wants to try.</param>
public sealed record TestSignaturePromptRequest(
    string BlockId,
    string PlaceholderName,
    string TestValue);
