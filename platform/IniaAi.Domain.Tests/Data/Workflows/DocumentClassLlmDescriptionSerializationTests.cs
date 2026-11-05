using IniaAi.Domain.Entities.Workflows;
using Newtonsoft.Json;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class DocumentClassLlmDescriptionSerializationTests
{
    [Fact]
    public void AClassWithoutAModelDescription_SerializesWithoutTheKey()
    {
        var workflow = new Workflow("root");
        var documentClass = new DocumentClass(workflow, "statement");

        string json = JsonConvert.SerializeObject(documentClass);

        Assert.DoesNotContain(nameof(DocumentClass.LlmDescription), json);
    }

    [Fact]
    public void AClassWithAModelDescription_RoundTrips()
    {
        var workflow = new Workflow("root");
        var documentClass = new DocumentClass(workflow, "statement") { LlmDescription = "not a trade confirmation" };

        DocumentClass? read = JsonConvert.DeserializeObject<DocumentClass>(JsonConvert.SerializeObject(documentClass));

        Assert.Equal("not a trade confirmation", read?.LlmDescription);
    }

    [Fact]
    public void CloneToWorkflow_CopiesTheModelDescription()
    {
        var source = new Workflow("source");
        var documentClass = new DocumentClass(source, "statement") { LlmDescription = "not a trade confirmation" };

        DocumentClass clone = documentClass.CloneToWorkflow(new Workflow("destination"));

        Assert.Equal("not a trade confirmation", clone.LlmDescription);
    }
}
