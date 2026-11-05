using ErrorOr;
using IniaAi.Common.Serialization;
using Microsoft.AspNetCore.Components.Forms;

namespace IniaAi.WebApp.Components.ToolsSettings.Components;

public static class PromptsUpload
{
    public const long MAX_FILE_SIZE = 10L * 1024 * 1024;

    public static async Task<ErrorOr<TPrompts>> ReadAsync<TPrompts>(
        IBrowserFile file,
        CancellationToken cancellationToken = default)
        where TPrompts : JsonSerializable<TPrompts>
    {
        if (file.Size > MAX_FILE_SIZE)
        {
            return Error.Validation(description: $"File {file.Name} is too large ({file.Size} bytes)");
        }

        using StreamReader reader = new(file.OpenReadStream(MAX_FILE_SIZE, cancellationToken));
        string json = await reader.ReadToEndAsync(cancellationToken);

        TPrompts? prompts = JsonSerializable<TPrompts>.FromJson(json);

        return prompts is null
            ? Error.Validation(description: "Failed to parse prompts from JSON")
            : prompts;
    }
}
