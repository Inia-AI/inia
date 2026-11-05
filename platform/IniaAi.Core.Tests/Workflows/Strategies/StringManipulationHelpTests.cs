using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class StringManipulationHelpTests
{
    [Fact]
    public void Sections_ContainsOneEntryPerOperation()
    {
        StringManipulationOperation[] allOps = Enum.GetValues<StringManipulationOperation>();
        HashSet<StringManipulationOperation> documented = [.. StringManipulationHelp.Sections
            .Select(s => s.Operation)];

        foreach (StringManipulationOperation op in allOps)
        {
            Assert.Contains(op, documented);
        }
    }

    [Fact]
    public void Sections_HaveUniqueOperations()
    {
        int sectionCount = StringManipulationHelp.Sections.Count;
        int uniqueCount = StringManipulationHelp.Sections.Select(s => s.Operation).Distinct().Count();
        Assert.Equal(sectionCount, uniqueCount);
    }

    [Fact]
    public void Sections_AllHaveNonEmptyTitleAndDescription()
    {
        foreach (StringManipulationHelp.Section section in StringManipulationHelp.Sections)
        {
            Assert.False(string.IsNullOrWhiteSpace(section.Title), $"Section for {section.Operation} has empty title");
            Assert.False(string.IsNullOrWhiteSpace(section.Description), $"Section for {section.Operation} has empty description");
        }
    }

    [Fact]
    public void Sections_AllHaveAtLeastOneExample()
    {
        foreach (StringManipulationHelp.Section section in StringManipulationHelp.Sections)
        {
            Assert.NotEmpty(section.Examples);
        }
    }
}
