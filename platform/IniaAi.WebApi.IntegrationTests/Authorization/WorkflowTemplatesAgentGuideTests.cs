using IniaAi.WebApi.Controllers.v1;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

/// <summary>
/// Guards the embedded agent-guide resource: the endpoint reads it by manifest name at
/// runtime, so a rename or a dropped csproj EmbeddedResource entry would otherwise only
/// surface as a 500 in production.
/// </summary>
public class WorkflowTemplatesAgentGuideTests
{
    private const string AGENT_GUIDE_RESOURCE_NAME = "IniaAi.WebApi.Resources.WorkflowTemplatesAgentGuide.md";

    [Fact]
    public void AgentGuideResource_IsEmbeddedAndNonEmpty()
    {
        using Stream? stream = typeof(WorkflowTemplatesController).Assembly
            .GetManifestResourceStream(AGENT_GUIDE_RESOURCE_NAME);

        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!);
        string content = reader.ReadToEnd();

        Assert.Contains("Agent Guide", content);
        Assert.Contains("baseVersionId", content);
        Assert.Contains("SharedComponentId", content);
        Assert.Contains("shared-components", content);
        Assert.Contains("promote", content);
        Assert.Contains("materialize", content);
        Assert.Contains("reconcile", content);
        Assert.Contains("retire", content);
        Assert.Contains("restore", content);
        Assert.Contains("expectedHeadVersionId", content);
        Assert.Contains("acknowledgeSignatureOrphans", content);
        Assert.Contains("entryKey", content);
        Assert.Contains("expectedRefHeadVersionId", content);
        Assert.Contains("`Hide`", content);
        Assert.Contains("`Unhide`", content);
        Assert.Contains("/consumers", content);
        Assert.Contains("/duplicate", content);
        Assert.Contains("newWorkflowTypeId", content);
        Assert.Contains("skippedCarriers", content);
        Assert.Contains("WorkflowType.AlreadyExists", content);
        Assert.Contains("viaSharedComponentId", content);
        Assert.Contains("`ViaSection`", content);
        Assert.Contains("/consumer-matrix", content);
        Assert.Contains("isLocalOnly", content);
        Assert.Contains("`NotApplicable`", content);
        Assert.Contains("stableSectionId", content);
        Assert.Contains("/export", content);
        Assert.Contains("/import-preview", content);
        Assert.Contains("/import-apply", content);
        Assert.Contains("bundleFormatVersion", content);
        Assert.Contains("SharedComponent.DivergedLineage", content);
        Assert.Contains("acknowledgeUnrelatedHistory", content);
        Assert.Contains("workflowTypeName", content);
        Assert.Contains("localHeadVersionId", content);
        Assert.Contains("SharedComponent.BundleTypeNameMissing", content);
    }
}
