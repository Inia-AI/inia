using IniaAi.AiConnection.Common;
using IniaAi.Configuration.Platform;

namespace IniaAi.Core.Services.AgentRuntime;

/// <summary>
/// How a model id an agent consumer names maps onto the runtime's allowlist. The same model has
/// a different id per provider (<c>us.anthropic.claude-sonnet-5-5</c> on Bedrock,
/// <c>claude-sonnet-5-5</c> as a Foundry deployment), so a stored id is provider-neutral and
/// resolves to the deployment's own form only when a run starts. Any form of an allowlisted
/// model is accepted, so a workflow moves between deployments without edits.
/// <para>
/// Forms match through <see cref="ModelIdNormalizer.Normalize(string?)"/>. A Foundry deployment
/// is matched only when it is named after its model: a registered deployment name normalizes to
/// itself, so a deployment called <c>sonnet-prod</c> matches no other provider's id. A dated id
/// (<c>claude-haiku-4-5-20251001</c>) does not match an undated one. When the allowlist holds
/// several forms of one model, a stored neutral id runs as the first listed.
/// </para>
/// </summary>
public static class AgentRuntimeModels
{
    /// <summary>
    /// The allowlist's id for <paramref name="modelId"/>: the id itself when listed, else the
    /// first listed id of the same model. Null when the allowlist has no form of it.
    /// </summary>
    public static string? RuntimeIdFor(AgentRuntimeSettings runtime, string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        List<string> allowedModels = AllowedModelsOf(runtime);
        if (allowedModels.Contains(modelId, StringComparer.Ordinal))
        {
            return modelId;
        }

        string neutral = ModelIdNormalizer.Normalize(modelId);

        return allowedModels.FirstOrDefault(model =>
            !string.IsNullOrWhiteSpace(model) && ModelIdNormalizer.Normalize(model) == neutral);
    }

    /// <summary>
    /// The id a run is started and billed under: the allowlist's form of
    /// <paramref name="modelId"/>, or the id as given when the allowlist has none, so the
    /// runtime's own 400 refuses it.
    /// </summary>
    public static string? RunIdFor(AgentRuntimeSettings runtime, string? modelId)
    {
        return RuntimeIdFor(runtime, modelId) ?? modelId;
    }

    /// <summary>
    /// The id to store for <paramref name="modelId"/>: its provider-neutral form when the
    /// allowlist has a form of the model, else the id unchanged so a save gate reports it as
    /// written.
    /// </summary>
    public static string? StoredIdFor(AgentRuntimeSettings runtime, string? modelId)
    {
        return RuntimeIdFor(runtime, modelId) is null
            ? modelId
            : ModelIdNormalizer.Normalize(modelId);
    }

    /// <summary>
    /// The allowlist's models, one entry per model, by their provider-neutral ids in allowlist
    /// order. What an author picks from.
    /// </summary>
    public static List<string> NeutralAllowedModels(AgentRuntimeSettings runtime)
    {
        return [.. AllowedModelsOf(runtime)
            .Where(static model => !string.IsNullOrWhiteSpace(model))
            .Select(static model => ModelIdNormalizer.Normalize(model))
            .Distinct(StringComparer.Ordinal)];
    }

    // The configuration binder sets the list to null for an explicit JSON null, whatever its
    // declared type, and the save gate must then refuse the model rather than throw.
    private static List<string> AllowedModelsOf(AgentRuntimeSettings runtime)
    {
        return runtime.AllowedModels ?? [];
    }
}
