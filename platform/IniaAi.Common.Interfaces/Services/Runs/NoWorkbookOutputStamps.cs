using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Common.Interfaces.Services.Runs;

public sealed class NoWorkbookOutputStamps : IWorkbookOutputStamps
{
    public bool? Stamps(Workflow maker, string outputClassKey)
    {
        return null;
    }
}
