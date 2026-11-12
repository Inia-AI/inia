using IniaAi.Testing;

namespace IniaAi.Services.Tests.Evaluation.MarkerInstruction;

public sealed class MarkerEvaluationSummaryTests(ITestOutputHelper output)
{
    private static readonly string[] _sample =
    [
        Line("G2C1", MarkerOutcome.MarkerAppeared, 0),
        Line("G2C1", MarkerOutcome.MarkerAppeared, 1),
        Line("G2C1", MarkerOutcome.Clean, 2),
        Line("G2C1", MarkerOutcome.Clean, 3),
        Line("G2C1", MarkerOutcome.Inconclusive, 4),
        Line("G2C5", MarkerOutcome.Clean, 0),
        string.Empty,
    ];

    [Fact]
    public void AFixedSample_GivesTheRatesAndIntervals_WithInconclusiveRunsApart()
    {
        IReadOnlyList<MarkerRate> rates = MarkerEvaluationSummary.Rates(MarkerEvaluationSummary.Read(_sample));
        output.WriteLine(MarkerEvaluationSummary.Format(rates));

        MarkerRate visible = rates.Single(rate => rate.Carrier == nameof(EvaluationCarrier.VisibleParagraph));
        Assert.Equal((2, 2, 1), (visible.Appeared, visible.Clean, visible.Inconclusive));
        Assert.Equal(0.5, visible.Rate);
        Assert.Equal(0.150, visible.Low, 3);
        Assert.Equal(0.850, visible.High, 3);
        MarkerRate pixels = rates.Single(rate => rate.Carrier == nameof(EvaluationCarrier.PixelsOnly));
        Assert.Equal(0.0, pixels.Rate);
        Assert.Equal(0.0, pixels.Low);
        Assert.Equal(0.793, pixels.High, 3);
    }

    [Fact]
    public void NoConclusiveRun_HasNoRate_AndTheWholeInterval()
    {
        IReadOnlyList<MarkerRate> rates = MarkerEvaluationSummary.Rates(MarkerEvaluationSummary.Read([Line("G2C3", MarkerOutcome.Inconclusive, 0)]));

        Assert.Null(Assert.Single(rates).Rate);
        Assert.Equal((0.0, 1.0), (rates[0].Low, rates[0].High));
    }

    private static string Line(string id, MarkerOutcome result, int repeat)
    {
        EvaluationCase evaluationCase = EvaluationCases.All.Single(candidate => candidate.Id == id);
        return MarkerEvaluationSummary.Line(new MarkerRunRecord("stand-in", "reader", id, evaluationCase.Goal.ToString(), evaluationCase.Carrier.ToString(), repeat, "model", null, null, result, 12));
    }
}
