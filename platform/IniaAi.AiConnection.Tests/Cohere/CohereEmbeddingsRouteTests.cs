using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Cohere;
using IniaAi.AiConnection.Common;
using IniaAi.Common.Extensions;
using IniaAi.Configuration;
using IniaAi.Configuration.Enums.Cohere;
using IniaAi.Configuration.LLMProviders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.AiConnection.Tests.Cohere;

public class CohereEmbeddingsRouteTests
{
    private const string BEDROCK_PROVIDER = "cohere.";
    private const string EU_PROFILE_ID = "eu.cohere.embed-v4:0";
    private const string COHERE_API_ID = "embed-v4.0";

    public static TheoryData<AiModel> AllModels()
    {
        return [.. Enum.GetValues<AiModel>()];
    }

    [Fact]
    public void EuSetting_BindsToTheEuInferenceProfile()
    {
        // Arrange
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformSettings:Cohere:EmbeddingsModel"] = "EuAwsEmbedV40",
            })
            .Build();
        var settings = new PlatformSettings();

        // Act
        configuration.GetSection("PlatformSettings").Bind(settings);

        // Assert
        Assert.Equal(EU_PROFILE_ID, settings.Cohere.EmbeddingsModel.GetEnumMember());
    }

    [Theory]
    [InlineData(AiModel.EmbedEnglishV30, AiModel.AwsEmbedEnglishV30)]
    [InlineData(AiModel.EmbedMultilingualV30, AiModel.AwsEmbedMultilingualV30)]
    [InlineData(AiModel.EmbedV40, AiModel.AwsEmbedV40)]
    [InlineData(AiModel.AwsEmbedV40, AiModel.AwsEmbedV40)]
    [InlineData(AiModel.UsAwsEmbedV40, AiModel.UsAwsEmbedV40)]
    [InlineData(AiModel.EuAwsEmbedV40, AiModel.EuAwsEmbedV40)]
    public void BedrockForm_CallsTheBedrockModelAndKeepsAProfilesRegion(AiModel configured, AiModel expected)
    {
        // Act
        AiModel model = CohereEmbeddingsRoute.BedrockForm(configured);

        // Assert
        Assert.Equal(expected, model);
    }

    [Theory]
    [InlineData(AiModel.AwsEmbedEnglishV30, AiModel.EmbedEnglishV30)]
    [InlineData(AiModel.AwsEmbedMultilingualV30, AiModel.EmbedMultilingualV30)]
    [InlineData(AiModel.AwsEmbedV40, AiModel.EmbedV40)]
    [InlineData(AiModel.UsAwsEmbedV40, AiModel.EmbedV40)]
    [InlineData(AiModel.EuAwsEmbedV40, AiModel.EmbedV40)]
    public void CohereApiForm_CallsTheCohereApiModel(AiModel configured, AiModel expected)
    {
        // Act
        AiModel model = CohereEmbeddingsRoute.CohereApiForm(configured);

        // Assert
        Assert.Equal(expected, model);
    }

    [Theory]
    [MemberData(nameof(AllModels))]
    public void CohereApiForm_NeverSendsABedrockId(AiModel configured)
    {
        // Act
        AiModel model = CohereEmbeddingsRoute.CohereApiForm(configured);

        // Assert
        Assert.DoesNotContain(BEDROCK_PROVIDER, model.GetEnumMember());
    }

    [Fact]
    public void BedrockConnector_CallsTheEuProfileItIsConfiguredWith_OnTheBedrockRoute()
    {
        // Arrange
        IOptions<PlatformSettings> options = Options.Create(EuSettings());

        // Act
        var connector = new CohereAwsEmbeddingsConnector(NullLogger<IEmbeddingsConnector>.Instance, options);

        // Assert
        Assert.Equal(EU_PROFILE_ID, connector.ModelId);
        Assert.Equal(ModelProviders.AWS_BEDROCK, connector.Provider);
    }

    [Fact]
    public void CohereApiConnector_CallsThePlainModelForTheEuProfile_OnCoheresApi()
    {
        // Arrange
        IOptions<PlatformSettings> options = Options.Create(EuSettings());

        // Act
        var connector = new CohereEmbeddingsConnector(NullLogger<IEmbeddingsConnector>.Instance, options);

        // Assert
        Assert.Equal(COHERE_API_ID, connector.ModelId);
        Assert.Equal(ModelProviders.COHERE_API, connector.Provider);
    }

    [Fact]
    public void AzureConnector_RecordsThePlainModelForTheEuProfile_OnAzureFoundry()
    {
        // Arrange
        PlatformSettings settings = EuSettings();
        settings.AzureFoundry = new AzureFoundrySettings
        {
            CohereApiUrl = "https://test.services.ai.azure.com/models/embeddings",
            CohereApiKey = "test-api-key",
            CohereDeploymentName = "cohere-embed-v4",
        };

        // Act
        var connector = new AzureCohereEmbeddingsConnector(NullLogger<IEmbeddingsConnector>.Instance, Options.Create(settings));

        // Assert
        Assert.Equal(COHERE_API_ID, connector.ModelId);
        Assert.Equal(ModelProviders.AZURE_FOUNDRY, connector.Provider);
    }

    private static PlatformSettings EuSettings()
    {
        return new PlatformSettings { Cohere = new CohereSettings { EmbeddingsModel = AiModel.EuAwsEmbedV40 } };
    }
}
