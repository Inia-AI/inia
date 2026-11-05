using IniaAi.Common;

namespace IniaAi.Core.Services;

public class StringReplacerStrategy : IStringReplacerStrategy
{
    public string? ReplacePlaceholders(string? input, string? source, bool embeddedMode = false)
    {
        return string.IsNullOrEmpty(input)
            ? input
            : Utils.GetStringFromTag(source, input, processMultipleTags: true);
    }
}
