using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json;

namespace IniaAi.Domain.Tests.Serialization;

public class DateLikeStringSerializationTests
{
    private const string PRECISE_DATE = "2024-03-12T00:00:00.000Z";

    [Theory]
    [InlineData("2023-01-01T00:00:00Z")]
    [InlineData(PRECISE_DATE)]
    [InlineData("2024-03-12")]
    public void ADateInAStringProperty_KeepsItsTextWhenTheWorkflowJsonIsRead(string publishedDate)
    {
        Workflow? read = Workflow.FromJson(SearchWorkflow(publishedDate).ToJson(pretty: false));

        Assert.Equal(publishedDate, SearchProperties(read).StartPublishedDate);
    }

    [Fact]
    public void ADateInAStringProperty_KeepsItsText_WhenTheCallerReadsWithNewtonsoftDefaults()
    {
        Workflow? read = JsonConvert.DeserializeObject<Workflow>(SearchWorkflow(PRECISE_DATE).ToJson(pretty: false));

        Assert.Equal(PRECISE_DATE, SearchProperties(read).StartPublishedDate);
    }

    [Fact]
    public void ADateInAStringProperty_KeepsItsTextWhenAWorkflowFileIsImported()
    {
        Workflow? read = Workflow.FromExportJson(SearchWorkflow(PRECISE_DATE).ToExportJson(includeRunTraces: true));

        Assert.Equal(PRECISE_DATE, SearchProperties(read).StartPublishedDate);
    }

    [Fact]
    public void ADateInAStringProperty_KeepsItsTextInTheCanonicalJson()
    {
        string canonical = new WorkflowCanonicalizer().ToCanonicalJson(SearchWorkflow(PRECISE_DATE));

        Assert.Contains($"\"{PRECISE_DATE}\"", canonical);
    }

    [Fact]
    public void ADateInAStringProperty_KeepsItsTextWhenALibraryDefinitionIsNormalised()
    {
        string definition = $$"""{"Type":"IntelligentSearch","Properties":{"StartPublishedDate":"{{PRECISE_DATE}}"},"SignatureFields":[]}""";

        Assert.Contains($"\"{PRECISE_DATE}\"", SharedComponentContentNormalizer.WithoutSignatureFields(definition));
    }

    private static Workflow SearchWorkflow(string publishedDate)
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.IntelligentSearch, section, "search")
        {
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "term",
                StartPublishedDate = publishedDate,
            },
        };

        return workflow;
    }

    private static IntelligentSearchProperties SearchProperties(Workflow? workflow)
    {
        Assert.NotNull(workflow);

        return Assert.IsType<IntelligentSearchProperties>(workflow.Sections.Single().Blocks.Single().Properties);
    }
}
