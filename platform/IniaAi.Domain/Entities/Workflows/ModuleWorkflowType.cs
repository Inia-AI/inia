namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// A workflow type a closed module adds. Core seeding adds the type with an access row for the
/// default organization, and on a fresh install its seed workflow; a build without the module seeds
/// none of them, and a row left in the database is an ordinary type there.
/// </summary>
/// <param name="Id">The workflow type id.</param>
/// <param name="Name">The name a fresh install gives the type.</param>
/// <param name="CreateSeedWorkflow">Builds the workflow a fresh install starts the type with.</param>
public sealed record ModuleWorkflowType(string Id, string Name, Func<Workflow> CreateSeedWorkflow)
{
    /// <summary>
    /// Whether the overview of a spreadsheet page also reads its neighbouring pages, as a PDF page's
    /// does. Other types summarise each sheet page on its own.
    /// </summary>
    public bool SpreadsheetOverviewsReadNeighbouringPages { get; init; }
}
