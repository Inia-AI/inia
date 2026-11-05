namespace IniaAi.Common.Reflection;

/// <summary>
/// Implemented by replacement targets that want to collect non-fatal sanitization notices
/// from <see cref="PlaceholderReflectionHelper.ReplaceAll"/> — e.g. a substituted value that
/// was already JSON-escaped and therefore substituted verbatim.
/// </summary>
public interface ISanitizationWarningSink
{
    void AddSanitizationWarning(string warning);
}
