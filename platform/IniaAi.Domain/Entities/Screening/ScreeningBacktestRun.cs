using IniaAi.Common;
using IniaAi.Domain.Common;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace IniaAi.Domain.Entities.Screening;

public enum ScreeningBacktestRunOutcome
{
    Pending = 0,
    WouldRun = 1,
    WouldBeRefused = 2,
}

/// <summary>
/// One input document of a backtest run: the class its user chose, whether the judge read it,
/// and, once the run is decided, the class selection gives it or the reason it leaves it out.
/// </summary>
public sealed record ScreeningBacktestRunDocument(
    string DocumentId,
    string FileName,
    string? UserClass,
    bool Judged,
    string? AssignedClass = null,
    string? ReasonCode = null)
{
    /// <summary>Whether selection would give the document another class than its user did, or leave it out; false until selection has run, which gives every judged document a class or a reason.</summary>
    [JsonIgnore]
    public bool SelectionChangesIt => Judged
        && (AssignedClass is not null || ReasonCode is not null)
        && !string.Equals(AssignedClass, UserClass, StringComparison.Ordinal);
}

/// <summary>
/// One past run of a backtest: with no class from its user, what selection gives each of its
/// input documents, and whether the run would run or be refused, in the live stage's words.
/// Planned when the backtest starts and decided once every judged document of the run has a
/// verdict. Carries no foreign key, like <see cref="ScreeningRecord"/>.
/// </summary>
[Index(nameof(BacktestId), nameof(Sequence))]
public class ScreeningBacktestRun : Entity<ScreeningBacktestRun>
{
    public const int REFUSAL_MAX_LENGTH = 4000;

    [Required]
    [StringLength(450)]
    public string BacktestId { get; set; } = string.Empty;

    public int Sequence { get; set; }

    [Required]
    [StringLength(450)]
    public string WorkflowExecutionId { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    public DateTime RunCreatedAt { get; set; }

    /// <summary>Lowercase hex SHA-256 of the run's variables; with a document id it names the document row the run reads.</summary>
    [Required]
    [StringLength(64)]
    public string RunVariablesSha256 { get; set; } = string.Empty;

    /// <summary>
    /// Whether the run was confidential, read from its anonymised documents. Its variables never
    /// reach the judge, because the stand-ins the run used are not kept; its rows are judged with
    /// none, and the page says the variables were withheld.
    /// </summary>
    public bool VariablesWithheld { get; set; }

    public ScreeningBacktestRunOutcome Outcome { get; set; } = ScreeningBacktestRunOutcome.Pending;

    /// <summary>What the live stage would tell the analyst; null unless the run would be refused.</summary>
    [StringLength(REFUSAL_MAX_LENGTH)]
    public string? RefusalReason
    {
        get;
        set => field = value.Truncate(REFUSAL_MAX_LENGTH);
    }

    /// <summary>The run's input documents, as <see cref="ScreeningBacktestRunDocument"/> JSON.</summary>
    public string DocumentsJson { get; set; } = "[]";

    public DateTime? DecidedAt { get; set; }

    public IReadOnlyList<ScreeningBacktestRunDocument> Documents()
    {
        return ScreeningJsonList.Read<ScreeningBacktestRunDocument>(DocumentsJson);
    }

    public void SetDocuments(IEnumerable<ScreeningBacktestRunDocument> documents)
    {
        DocumentsJson = ScreeningJsonList.Write(documents);
    }
}
