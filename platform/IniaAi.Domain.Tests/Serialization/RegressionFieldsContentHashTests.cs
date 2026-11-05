using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Serialization;

namespace IniaAi.Domain.Tests.Serialization;

/// <summary>
/// The pinned hash is the one the build with validation computed before validation became a
/// removable module. A build without the module must compute the same hash for a stored template,
/// or re-saving an unchanged template there adds a version.
/// </summary>
public class RegressionFieldsContentHashTests
{
    private const string FIXTURE = "RegressionFieldsTemplate.workflow.json";
    private const string PINNED_HASH = "04d5ceb01029cc70b4afa2f8d808fee0c93d4815d9d1050c23a1b115fc9be153";

    [Fact]
    public void ATemplateWithRegressionFieldsAndALegacyBinding_HashesAsBefore()
    {
        Workflow workflow = Workflow.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Serialization", "Fixtures", FIXTURE)))!;

        Assert.Equal(["aum", "manager", "revenue"], workflow.RegressionFields.Select(field => field.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("Tolerance", workflow.GetAllBlocks().Single(block => block.ReplacementTag == "legacy").RegressionSchema);
        Assert.Equal(PINNED_HASH, new WorkflowCanonicalizer().ComputeContentHash(workflow));
    }
}
