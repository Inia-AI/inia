using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class DocumentClassMadeBySerializationTests
{
    private const string MAKER_TYPE = "CorpPart1TypeA";
    private const string MAKER_OUTPUT = "Financial Spread Output";

    [Fact]
    public void AClassMadeByNoWorkflow_SerializesWithoutTheLink()
    {
        var workflow = new Workflow("root");
        var documentClass = new DocumentClass(workflow, "CorporateFinancialModel");

        string json = JsonConvert.SerializeObject(documentClass);

        Assert.DoesNotContain(nameof(DocumentClass.MadeByWorkflowTypeId), json);
        Assert.DoesNotContain(nameof(DocumentClass.MadeByDocumentClassKey), json);
        Assert.False(documentClass.IsMadeByAnotherWorkflow);
    }

    [Fact]
    public void AClassMadeByAnotherWorkflow_RoundTripsThroughTheWorkflowFile()
    {
        var workflow = new Workflow("Part 2");
        _ = new DocumentClass(workflow, "CorporateFinancialModel") { MadeByWorkflowTypeId = MAKER_TYPE, MadeByDocumentClassKey = MAKER_OUTPUT };

        Workflow read = Workflow.FromJson(new WorkflowCanonicalizer().ToCanonicalJson(workflow))!;

        DocumentClass spread = Assert.Single(read.DocumentClasses);
        Assert.Equal(MAKER_TYPE, spread.MadeByWorkflowTypeId);
        Assert.Equal(MAKER_OUTPUT, spread.MadeByDocumentClassKey);
        Assert.True(spread.IsMadeByAnotherWorkflow);
    }

    [Fact]
    public void CloneToWorkflow_KeepsTheLink_SoANewVersionKeepsIt()
    {
        var source = new Workflow("Part 2");
        var documentClass = new DocumentClass(source, "CorporateFinancialModel") { MadeByWorkflowTypeId = MAKER_TYPE, MadeByDocumentClassKey = MAKER_OUTPUT };

        DocumentClass clone = documentClass.CloneToWorkflow(new Workflow("Part 2, next version"));

        Assert.Equal(MAKER_TYPE, clone.MadeByWorkflowTypeId);
        Assert.Equal(MAKER_OUTPUT, clone.MadeByDocumentClassKey);
    }

    [Fact]
    public void AnOutputClassThatCarriesALink_IsMadeByItsOwnWorkflow()
    {
        var documentClass = new DocumentClass(new Workflow("Part 2"), "Report") { IsOutputDocument = true, MadeByWorkflowTypeId = MAKER_TYPE, MadeByDocumentClassKey = MAKER_OUTPUT };

        Assert.False(documentClass.IsMadeByAnotherWorkflow);
    }

    [Theory]
    [InlineData(MAKER_TYPE, null)]
    [InlineData(null, MAKER_OUTPUT)]
    [InlineData(" ", MAKER_OUTPUT)]
    public void AHalfSetLink_IsNoLink(string? workflowTypeId, string? documentClassKey)
    {
        var documentClass = new DocumentClass(new Workflow("Part 2"), "CorporateFinancialModel") { MadeByWorkflowTypeId = workflowTypeId, MadeByDocumentClassKey = documentClassKey };

        Assert.False(documentClass.IsMadeByAnotherWorkflow);
    }
}
