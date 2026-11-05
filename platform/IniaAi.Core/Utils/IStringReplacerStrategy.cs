namespace IniaAi.Core.Services;

public interface IStringReplacerStrategy
{
    string? ReplacePlaceholders(string input, string? source, bool embeddedMode = false);
}
