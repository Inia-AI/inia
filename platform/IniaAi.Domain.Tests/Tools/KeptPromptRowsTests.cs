using IniaAi.Domain.Entities.Screening;
using IniaAi.Domain.Tools.Reconciliation;
using IniaAi.Domain.Tools.Validation;

namespace IniaAi.Domain.Tests.Tools;

/// <summary>
/// The prompt tables of the closed modules stay in every build, so a new row of them carries no
/// module's prompt text: the defaults and their seeding belong to the module that reads them.
/// </summary>
public class KeptPromptRowsTests
{
    [Fact]
    public void ANewValidationPromptsRow_CarriesNoPromptText()
    {
        ValidationPrompts row = new();

        Assert.Null(row.ExtractionSystemPrompt);
        Assert.Null(row.ExtractionPrompt);
        Assert.Null(row.JudgeSystemPrompt);
        Assert.Null(row.JudgePrompt);
    }

    [Fact]
    public void ANewScreeningPromptsRow_CarriesNoPromptText()
    {
        ScreeningPrompts row = new();

        Assert.Null(row.JudgeSystemPrompt);
        Assert.Null(row.JudgePrompt);
    }

    [Fact]
    public void ANewReconciliationPromptsRow_CarriesNoHedgeFundPromptText()
    {
        ReconciliationPrompts row = new();

        Assert.Empty(row.HedgeFundsMonitoringFindMatchPrompt);
        Assert.Empty(row.HedgeFundsMonitoringCompareListsPrompt);
    }
}
