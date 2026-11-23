using IniaAi.Configuration.Enums.Cohere;

namespace IniaAi.AiConnection.Cohere;

public static class CohereEmbeddingsRoute
{
    public static AiModel BedrockForm(AiModel configured)
    {
        return configured switch
        {
            AiModel.EmbedEnglishV30 => AiModel.AwsEmbedEnglishV30,
            AiModel.EmbedMultilingualV30 => AiModel.AwsEmbedMultilingualV30,
            AiModel.EmbedV40 => AiModel.AwsEmbedV40,
            _ => configured,
        };
    }

    public static AiModel CohereApiForm(AiModel configured)
    {
        return configured switch
        {
            AiModel.AwsEmbedEnglishV30 => AiModel.EmbedEnglishV30,
            AiModel.AwsEmbedMultilingualV30 => AiModel.EmbedMultilingualV30,
            AiModel.AwsEmbedV40 or AiModel.UsAwsEmbedV40 or AiModel.EuAwsEmbedV40 => AiModel.EmbedV40,
            _ => configured,
        };
    }
}
