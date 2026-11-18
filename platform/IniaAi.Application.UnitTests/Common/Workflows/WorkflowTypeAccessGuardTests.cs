using System.Text.RegularExpressions;
using IniaAi.Application.Common.Workflows;
using IniaAi.Testing;

namespace IniaAi.Application.UnitTests.Common.Workflows;

/// <summary>
/// Only <see cref="WorkflowTypeAccess"/> decides which workflow types an organization reaches. A product file that
/// reads the access rows itself could answer differently from it, so every such file is on the list below with what
/// it does with the rows: writes them, maps them, or decides access in the one place.
/// </summary>
public sealed partial class WorkflowTypeAccessGuardTests
{
    private const string MIGRATIONS_PROJECT_PREFIX = "IniaAi.Migrations.";

    private static readonly Dictionary<string, string> _allowed = new(StringComparer.Ordinal)
    {
        ["IniaAi.Application/Common/Workflows/WorkflowTypeAccess.cs"] = "decides which types an organization reaches",
        ["IniaAi.Domain/Specifications/Workflows/WorkflowTypeAccessByOrgSpec.cs"] = "the query WorkflowTypeAccess runs for one type",
        ["IniaAi.Application/Commands/WorkflowTemplates/CreateWorkflowType/CreateWorkflowTypeCommandHandler.cs"] = "grants a new type to the organization that creates it",
        ["IniaAi.Application/Commands/WorkflowTemplates/SaveWorkflowTemplateContent/SaveWorkflowTemplateContentCommandHandler.cs"] = "grants the type a save creates, and takes the grant back when the save fails",
        ["IniaAi.Infrastructure/Persistence/Core/Contexts/CoreDbInitializer.cs"] = "seeds the default organization's grants",
        ["IniaAi.Infrastructure/Persistence/Core/Contexts/CoreDbContext.cs"] = "declares the table",
        ["IniaAi.Infrastructure/Persistence/Core/EntityConfigurations/WorkflowTypeOrganizationAccessConfiguration.cs"] = "maps the table",
        ["IniaAi.Infrastructure/Services/WorkflowTypeRetirement.cs"] = "deletes the rows of a type it retires, and purges them",
        ["IniaAi.WebApp/Components/WorkflowTypes/Components/WorkflowTypeTenantAssignmentDialog.razor"] = "edits the rows of a type",
        ["IniaAi.WebApp/Components/WorkflowTypes/Components/WorkflowTypeManagementCard.razor"] = "finds the organization that owns a type before it removes the type",
    };

    [Fact]
    public void OnlyTheListedFiles_ReadTheAccessRows()
    {
        string repoRoot = RepositoryRoot.Find();

        List<string> unlisted = [.. Readers(repoRoot).Where(reader => !_allowed.ContainsKey(reader.File)).Select(reader => reader.Line)];

        Assert.True(
            unlisted.Count == 0,
            "These lines read the workflow type access rows outside WorkflowTypeAccess, so they could answer differently "
            + "from it. Ask WorkflowTypeAccess instead; a file that only writes or maps the rows goes on the list in "
            + "this test with what it does:\n  " + string.Join("\n  ", unlisted));
    }

    [Fact]
    public void EveryListedFile_StillReadsTheAccessRows()
    {
        string repoRoot = RepositoryRoot.Find();
        HashSet<string> readers = [.. Readers(repoRoot).Select(reader => reader.File)];

        List<string> stale = [.. _allowed.Keys.Where(file => !readers.Contains(file))];

        Assert.True(stale.Count == 0, "These files no longer read the access rows. Take them off the list:\n  " + string.Join("\n  ", stale));
    }

    private static IEnumerable<(string File, string Line)> Readers(string repoRoot)
    {
        foreach (string file in SourceFiles.Product(repoRoot).Concat(SourceFiles.Product(repoRoot, "*.razor")))
        {
            string relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (relative.StartsWith(MIGRATIONS_PROJECT_PREFIX, StringComparison.Ordinal))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (!line.StartsWith("//", StringComparison.Ordinal) && AccessRowsRead().IsMatch(line))
                {
                    yield return (relative, $"{relative}:{index + 1}: {line}");
                }
            }
        }
    }

    [GeneratedRegex(@"\.OrganizationAccess\b|\b(I?Repository|Set|DbSet|IQueryable)<WorkflowTypeOrganizationAccess>|\.WorkflowTypeOrganizationAccess\b|\bWorkflowTypeAccessByOrgSpec\b")]
    private static partial Regex AccessRowsRead();
}
