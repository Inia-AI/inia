namespace IniaAi.Domain.Entities.Screening;

/// <summary>
/// The columns a row stores one judge verdict in: the live run's <see cref="ScreeningRecord"/> and
/// the backtest's <see cref="ScreeningBacktestDocument"/>. <c>ScreeningVerdictColumns</c> in Core
/// writes a verdict into them and reads it back, so both rows hold a verdict the same way.
/// </summary>
public interface IScreeningVerdictColumns
{
    /// <summary>How a period end reads wherever the judge's verdict is written or shown.</summary>
    const string PERIOD_END_FORMAT = "yyyy-MM-dd";

    string? MatchedClassesJson { get; set; }

    string? Reasoning { get; set; }

    string? EvidenceExcerpt { get; set; }

    bool PromptInjectionSuspected { get; set; }

    bool MaliciousContentSuspected { get; set; }

    string? ModelId { get; set; }

    DateOnly? PeriodEnd { get; set; }

    int? PeriodMonths { get; set; }
}
