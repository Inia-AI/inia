namespace IniaAi.Common.Interfaces.Persistence;

/// <summary>
/// A module's prompts that Core seeding writes at startup, once the platform's own prompt rows are saved: a
/// prompt row of the module's own, added when no stored row carries the date of the module's code
/// defaults, or the module's columns of a kept platform row. The newest row wins at read time, so
/// an edited row stays in the history and a bumped default still reaches every deployment.
/// </summary>
public interface ICorePromptsSeed
{
    void Seed(ICoreDbContext context, IReadOnlyDictionary<string, bool> tableStatuses);
}
