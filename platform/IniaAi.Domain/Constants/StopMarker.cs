using IniaAi.Domain.Entities.Workflows.Blocks;
using System.Text.RegularExpressions;

namespace IniaAi.Domain.Constants;

/// <summary>
/// The marker a workflow author puts into a section or block input to skip it at runtime. It counts only
/// as a whole token: letters, digits, "_" and "-" around it make it part of something else, such as an id
/// or a hash, and then it does not skip. Workflow templates store the value, so it never changes.
/// </summary>
public static partial class StopMarker
{
    public const string VALUE = "AF11";

    public const string SKIP_REASON = "Stop term '" + VALUE + "' found in execution input";

    public static bool IsIn(string? text)
    {
        return !string.IsNullOrEmpty(text) && StandaloneMarker().IsMatch(text);
    }

    public static bool IsInInputsOf(Block block)
    {
        return block.TextInputs.Any(input => IsIn(input.ExecutionState?.Value));
    }

    [GeneratedRegex(@"(?<![\p{L}\p{N}_-])" + VALUE + @"(?![\p{L}\p{N}_-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StandaloneMarker();
}
