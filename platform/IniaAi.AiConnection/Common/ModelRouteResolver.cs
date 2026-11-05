using IniaAi.Common;
using IniaAi.Common.Extensions;
using IniaAi.Configuration.Enums.Anthropic;

namespace IniaAi.AiConnection.Common;

/// <summary>
/// Resolves the model a request names to the id the running stack calls. A block may name a
/// model by any of its catalogue ids: the plain Anthropic name, which the Inia Azure stacks also
/// use as the Foundry deployment name, or a Bedrock inference profile. A stack calls every model
/// through the route of its configured default model, so a plain name runs as that route's
/// Bedrock profile on AWS and a Bedrock id runs as the plain name on Azure or the direct API.
/// <see cref="Resolve"/> leaves a Bedrock id on its own region; the AWS Bedrock provider calls
/// <see cref="ResolveInStackRegion"/> instead, which runs a Bedrock id of another region family
/// (<c>us.</c>, <c>eu.</c>, <c>global.</c>) under the stack's own prefix, because a Bedrock
/// stack calls only its own family's profiles.
/// </summary>
public static class ModelRouteResolver
{
    /// <summary>
    /// The catalogue entry the stack calls for <paramref name="requestedModelId"/>, or null when
    /// the id is not a known model or the stack's route has no form of it. A Bedrock id keeps
    /// its own region on a Bedrock-form stack.
    /// </summary>
    public static AiModel? Resolve(string requestedModelId, AiModel stackModel)
    {
        return Utils.TryParseEnumFromEnumMemberAttribute(requestedModelId, out AiModel requested)
            ? ResolveEntry(requested, stackModel, shouldUseStackRegion: false)
            : null;
    }

    public static AiModel ResolveOrThrow(string requestedModelId, AiModel stackModel)
    {
        return Resolve(requestedModelId, stackModel)
            ?? throw new InvalidOperationException(UnknownModelMessage(requestedModelId));
    }

    /// <summary>
    /// As <see cref="Resolve"/>, but on a Bedrock stack a Bedrock id of another region family
    /// maps to the same model under the stack model's prefix, or to null when that family has
    /// no form of it.
    /// </summary>
    public static AiModel? ResolveInStackRegion(string requestedModelId, AiModel stackModel)
    {
        return Utils.TryParseEnumFromEnumMemberAttribute(requestedModelId, out AiModel requested)
            ? ResolveEntry(requested, stackModel, shouldUseStackRegion: true)
            : null;
    }

    public static AiModel ResolveInStackRegionOrThrow(string requestedModelId, AiModel stackModel)
    {
        if (!Utils.TryParseEnumFromEnumMemberAttribute(requestedModelId, out AiModel requested))
        {
            throw new InvalidOperationException(UnknownModelMessage(requestedModelId));
        }

        return ResolveEntry(requested, stackModel, shouldUseStackRegion: true)
            ?? throw new InvalidOperationException(IsBedrockRoute(stackModel)
                ? $"Model '{requestedModelId}' ({ModelLabel(requested)}) is not offered in this deployment's region. Choose another model from the block's model list."
                : UnknownModelMessage(requestedModelId));
    }

    private static AiModel? ResolveEntry(AiModel requested, AiModel stackModel, bool shouldUseStackRegion)
    {
        bool isStackOnBedrock = IsBedrockRoute(stackModel);
        string stackRoutePrefix = RoutePrefix(stackModel);

        if (IsBedrockRoute(requested) == isStackOnBedrock
            && (!isStackOnBedrock || !shouldUseStackRegion || RoutePrefix(requested) == stackRoutePrefix))
        {
            return requested;
        }

        string modelName = ModelName(requested);

        return Enum.GetValues<AiModel>()
            .Where(candidate => IsBedrockRoute(candidate) == isStackOnBedrock
                && RoutePrefix(candidate) == stackRoutePrefix
                && ModelName(candidate) == modelName)
            .Cast<AiModel?>()
            .FirstOrDefault();
    }

    private static string UnknownModelMessage(string requestedModelId)
    {
        return $"Model '{requestedModelId}' is not a model this deployment can call. Choose a model from the block's model list.";
    }

    private static string ModelLabel(AiModel model)
    {
        string modelName = ModelName(model);

        return Utils.TryParseEnumFromEnumMemberAttribute(modelName, out AiModel plainModel)
            ? plainModel.GetDescription()
            : modelName;
    }

    private static bool IsBedrockRoute(AiModel model)
    {
        return ModelIdNormalizer.IsBedrockFormat(model.GetEnumMember());
    }

    private static string ModelName(AiModel model)
    {
        return ModelIdNormalizer.Normalize(model.GetEnumMember(), ModelDeploymentTable.Empty);
    }

    private static string RoutePrefix(AiModel model)
    {
        string modelId = model.GetEnumMember();

        return modelId[..modelId.IndexOf(ModelName(model), StringComparison.Ordinal)];
    }
}
