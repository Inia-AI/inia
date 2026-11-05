namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// Describes a single staff-authored signature field exposed on an AI Query block.
/// Clients override the value per workflow via
/// <c>AiQueryProperties.SignatureFieldValues</c>.
/// </summary>
/// <param name="DisplayName">Human-readable label shown to clients.</param>
/// <param name="Description">Help text explaining what this field controls.</param>
/// <param name="DefaultValue">Default value used when the client has not provided an override.</param>
public record SignatureFieldDefinition(
    string DisplayName,
    string Description,
    string DefaultValue);
