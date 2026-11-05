using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class CalculateFormulaHelpExamplesTests : IClassFixture<WorkflowsFixture<CalculateFormulaHelpExamplesTests>>
{
    private readonly IServiceProvider _testServiceProvider;

    public CalculateFormulaHelpExamplesTests(
        WorkflowsFixture<CalculateFormulaHelpExamplesTests> fixture,
        ITestOutputHelper outputHelper)
    {
        _testServiceProvider = fixture.BuildTestServiceProvider(outputHelper);
    }

    public static TheoryData<string, string> AssertableExamples()
    {
        var data = new TheoryData<string, string>();

        foreach (CalculateFormulaHelp.Section section in CalculateFormulaHelp.Sections)
        {
            foreach (CalculateFormulaHelp.Example example in section.Examples)
            {
                if (example.ExpectedResult is null)
                {
                    continue;
                }

                data.Add(example.Formula, example.ExpectedResult);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AssertableExamples))]
    public async Task HelpExample_EvaluatesToExpectedResultAsync(string formula, string expected)
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.CalculateNumber, workflow)
        {
            Name = "Calculate Formula Block",
            Properties = new CalculateFormulaProperties
            {
                FormulaTemplate = formula
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new CalculateFormulaStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Equal(expected, block.BlockResponse.RawResponse);
    }
}
