using System.Text.Json;

namespace IniaAi.WebApi.Contracts.Responses.SharedComponents;

/// <summary>
/// The definition content of a shared component's head version plus the tokens identifying it.
/// </summary>
/// <param name="Id">The component identifier.</param>
/// <param name="HeadVersionId">The head version identifier the content was read from.</param>
/// <param name="HeadContentHash">The content hash of the head version.</param>
/// <param name="Content">The verbatim definition JSON (PascalCase domain representation, passed through unmodified).</param>
public record SharedComponentContentResponse(
    string Id,
    string HeadVersionId,
    string HeadContentHash,
    JsonElement Content);
