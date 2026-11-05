using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Extensions;

public static class TextInputCollectionExtensions
{
    public static bool TryGetValue(
        this ICollection<TextInput> textInputs,
        string replacementTag,
        out string? value)
    {
        TextInput? textInput = textInputs
            .FirstOrDefault(ti => ti.ReplacementTag.Equals(replacementTag, StringComparison.OrdinalIgnoreCase));

        if (textInput?.ExecutionState?.Value is not null)
        {
            value = textInput.ExecutionState.Value;
            return true;
        }

        value = null;
        return false;
    }
}
