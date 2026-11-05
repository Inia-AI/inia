namespace IniaAi.WebApi.Contracts.Responses.Signatures;

/// <summary>
/// Result of a signature test-replay. Deliberately carries NO prompt fields — the modified
/// user/system prompts are Inia IP and stay server-side. The FE only needs the signature
/// values it already had plus the new block output.
/// </summary>
/// <param name="OldValueUsed">The exact captured value that was swapped out of the prompt.</param>
/// <param name="NewValueUsed">The user-provided replacement value.</param>
/// <param name="OccurrencesReplaced">How many times <paramref name="OldValueUsed"/> was found and replaced in the captured prompts. > 1 means the signature appeared in both the user and system prompt (or coincidentally in other resolved content).</param>
/// <param name="ResponseText">The text body returned by the AI call.</param>
/// <param name="InputTokens">Input token count reported by the provider, if available.</param>
/// <param name="OutputTokens">Output token count reported by the provider, if available.</param>
/// <param name="ModelUsed">The resolved model identifier used for the call.</param>
public sealed record SignaturePromptTestResultDto(
    string OldValueUsed,
    string NewValueUsed,
    int OccurrencesReplaced,
    string ResponseText,
    int? InputTokens,
    int? OutputTokens,
    string? ModelUsed);
