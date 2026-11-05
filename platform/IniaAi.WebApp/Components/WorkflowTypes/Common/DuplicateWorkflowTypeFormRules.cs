using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.WebApp.Components.WorkflowTypes.Common;

public static class DuplicateWorkflowTypeFormRules
{
    public const int ID_MAX_LENGTH = 100;
    public const int NAME_MAX_LENGTH = WorkflowType.NAME_MAX_LENGTH;

    private const string COPY_SUFFIX = "Copy";
    private const int ID_SUFFIX_HEADROOM = 10;

    public static string? Validate(
        string sourceWorkflowTypeId,
        string? newWorkflowTypeId,
        string? name,
        IReadOnlyCollection<string> takenIds)
    {
        if (string.IsNullOrWhiteSpace(newWorkflowTypeId))
        {
            return "ID is required";
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return "Name is required";
        }

        if (newWorkflowTypeId.Length > ID_MAX_LENGTH)
        {
            return $"ID must be {ID_MAX_LENGTH} characters or fewer";
        }

        if (name.Length > NAME_MAX_LENGTH)
        {
            return $"Name must be {NAME_MAX_LENGTH} characters or fewer";
        }

        if (string.Equals(newWorkflowTypeId, sourceWorkflowTypeId, StringComparison.OrdinalIgnoreCase))
        {
            return "The copy needs an ID different from the one it is copied from";
        }

        if (takenIds.Any(taken => string.Equals(taken, newWorkflowTypeId, StringComparison.OrdinalIgnoreCase)))
        {
            return $"A workflow type with ID '{newWorkflowTypeId}' already exists";
        }

        return null;
    }

    public static string SuggestId(string sourceWorkflowTypeId, IReadOnlyCollection<string> takenIds)
    {
        string root = Clamp($"{sourceWorkflowTypeId}{COPY_SUFFIX}");
        if (!IsTaken(root, takenIds))
        {
            return root;
        }

        string suffixedRoot = ClampTo(
            $"{sourceWorkflowTypeId}{COPY_SUFFIX}",
            ID_MAX_LENGTH - ID_SUFFIX_HEADROOM);

        int suffix = 2;
        while (IsTaken($"{suffixedRoot}{suffix}", takenIds))
        {
            suffix++;
        }

        return $"{suffixedRoot}{suffix}";
    }

    private static bool IsTaken(string candidate, IReadOnlyCollection<string> takenIds)
    {
        return takenIds.Any(taken => string.Equals(taken, candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static string Clamp(string value)
    {
        return ClampTo(value, ID_MAX_LENGTH);
    }

    private static string ClampTo(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
