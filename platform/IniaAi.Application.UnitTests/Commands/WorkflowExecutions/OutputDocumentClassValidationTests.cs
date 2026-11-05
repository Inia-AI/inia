using IniaAi.Application.Commands.WorkflowExecutions.Shared;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Application.UnitTests.Commands.WorkflowExecutions;

/// <summary>
/// Guards <see cref="WorkflowExecutionOrchestrator.FindOutputBlocksMissingDocumentClass"/>: a block
/// is flagged only when its own <see cref="IProducesDocumentBlockProperties.RequiresDocumentClass"/>
/// policy is true and the class is blank. Json2Xlsx in the inject direction requires one;
/// Json2Xlsx-extract and IntelligentSearch do not.
/// </summary>
public class OutputDocumentClassValidationTests
{
    private static Block AddBlock(Section section, BlockTypes type, BlockProperties props)
    {
        return new Block(type, section)
        {
            Name = type.ToString(),
            Properties = props,
        };
    }

    [Fact]
    public void Json2XlsxWithoutDocumentClass_IsFlagged()
    {
        var workflow = new Workflow();
        var section = new Section("S", workflow);
        _ = AddBlock(section, BlockTypes.Json2Xlsx, new Json2XlsxProperties { CreatesDocumentClass = "" });

        List<string> missing = WorkflowExecutionOrchestrator.FindOutputBlocksMissingDocumentClass(workflow);

        _ = Assert.Single(missing);
    }

    [Fact]
    public void IntelligentSearchWithoutDocumentClass_IsNotFlagged()
    {
        var workflow = new Workflow();
        var section = new Section("S", workflow);
        _ = AddBlock(section, BlockTypes.IntelligentSearch, new IntelligentSearchProperties { CreatesDocumentClass = null });

        List<string> missing = WorkflowExecutionOrchestrator.FindOutputBlocksMissingDocumentClass(workflow);

        Assert.Empty(missing);
    }

    [Fact]
    public void IntelligentSearchWithDocumentClass_IsNotFlagged()
    {
        var workflow = new Workflow();
        var section = new Section("S", workflow);
        _ = AddBlock(section, BlockTypes.IntelligentSearch, new IntelligentSearchProperties { CreatesDocumentClass = "search-results" });

        List<string> missing = WorkflowExecutionOrchestrator.FindOutputBlocksMissingDocumentClass(workflow);

        Assert.Empty(missing);
    }

    [Fact]
    public void Json2XlsxWithDocumentClass_IsNotFlagged()
    {
        var workflow = new Workflow();
        var section = new Section("S", workflow);
        _ = AddBlock(section, BlockTypes.Json2Xlsx, new Json2XlsxProperties { CreatesDocumentClass = "FinancialModel" });

        List<string> missing = WorkflowExecutionOrchestrator.FindOutputBlocksMissingDocumentClass(workflow);

        Assert.Empty(missing);
    }

    [Fact]
    public void Json2XlsxExtractDirectionWithoutDocumentClass_IsNotFlagged()
    {
        var workflow = new Workflow();
        var section = new Section("S", workflow);
        _ = AddBlock(section, BlockTypes.Json2Xlsx, new Json2XlsxProperties
        {
            Direction = Json2XlsxDirection.Extract,
            CreatesDocumentClass = "",
        });

        List<string> missing = WorkflowExecutionOrchestrator.FindOutputBlocksMissingDocumentClass(workflow);

        Assert.Empty(missing);
    }
}
