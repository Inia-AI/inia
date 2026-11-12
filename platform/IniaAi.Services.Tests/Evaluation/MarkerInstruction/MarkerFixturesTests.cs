using IniaAi.Services.Tests.Documents;
using IniaAi.Services.Tests.Workflows;
using IniaAi.Testing.DependencyInjection.Mocks;

namespace IniaAi.Services.Tests.Evaluation.MarkerInstruction;

/// <summary>
/// Every carrier's documents build and go through processing and the marker workflow. Whether the planted instruction
/// reaches a model at all is recorded per carrier: a carrier that never does needs no paid run.
/// </summary>
public sealed class MarkerFixturesTests(DocumentsFixture fixture, ITestOutputHelper output) : IClassFixture<DocumentsFixture>
{
    public static TheoryData<string> Carriers => [.. EvaluationCases.All.Where(evaluationCase => evaluationCase.Goal is EvaluationGoal.ResultField && !evaluationCase.IsControl).Select(evaluationCase => evaluationCase.Id)];

    [Theory]
    [MemberData(nameof(Carriers))]
    public async Task ACarriersDocuments_Build_AndRecordWhetherTheInstructionReachesAModel(string id)
    {
        EvaluationCase evaluationCase = EvaluationCases.All.Single(candidate => candidate.Id == id);
        IReadOnlyList<MarkerFile> files = MarkerFixtures.Build(evaluationCase);
        using var run = new MarkerRun(fixture, output, ScriptedChatConnector.Answering("<answer>Net assets are 1,234,567 USD.</answer>")) { Files = files };

        _ = await run.RunAsync(TestContext.Current.CancellationToken);

        bool asText = run.Model.Calls.Any(call => MarkerFixtures.CallHoldsMarker(call.Text, evaluationCase));
        bool withAnImage = run.Model.Calls.Any(call => call.CarriesAnImage);
        output.WriteLine($"{evaluationCase} {evaluationCase.Carrier}: {files.Count} file(s); reaches a model as text: {asText}; a call carries an image: {withAnImage}; {run.Model.Calls.Count} call(s)");
        Assert.NotEmpty(run.Model.Calls);
        if (evaluationCase.Carrier is EvaluationCarrier.VisibleParagraph)
        {
            Assert.True(asText, "the visible paragraph is the positive control: its instruction must reach the model");
        }
    }
}
