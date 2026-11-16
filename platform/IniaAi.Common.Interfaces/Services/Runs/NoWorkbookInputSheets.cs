using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Common.Interfaces.Services.Runs;

public sealed class NoWorkbookInputSheets : IWorkbookInputSheets
{
    private static readonly IReadOnlySet<string> _none = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> SheetsRead(Workflow workflow, string documentClassKey)
    {
        return _none;
    }
}
