namespace IniaAi.Configuration.LLMProviders;

public class AzureFoundrySettings
{
    /// <summary>
    /// Full Claude endpoint URL, e.g. https://{resource}.services.ai.azure.com/anthropic/v1/messages
    /// </summary>
    public string? AnthropicApiUrl { get; set; }

    /// <summary>
    /// Azure API key for Claude. Null when using Entra ID.
    /// </summary>
    public string? AnthropicApiKey { get; set; }

    /// <summary>
    /// When true, uses Entra ID (DefaultAzureCredential) instead of API key for authentication.
    /// </summary>
    public bool UseEntraId { get; set; } = false;

    /// <summary>
    /// Full Cohere embeddings endpoint URL using the Azure AI Model Inference API,
    /// e.g. https://{resource}.services.ai.azure.com/models/embeddings?api-version=2024-05-01-preview
    /// </summary>
    public string? CohereApiUrl { get; set; }

    /// <summary>
    /// Azure API key for Cohere. Null when using Entra ID. May differ from Claude key (different Azure resources).
    /// </summary>
    public string? CohereApiKey { get; set; }

    /// <summary>
    /// The Azure deployment name for the Cohere embeddings model (e.g. "cohere-embed-v4").
    /// This is the customer-chosen name assigned at deployment time in Azure AI Foundry.
    /// Required when using Azure Cohere embeddings.
    /// </summary>
    public string? CohereDeploymentName { get; set; }
}
