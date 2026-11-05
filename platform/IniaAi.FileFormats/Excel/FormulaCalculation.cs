namespace IniaAi.FileFormats.Excel;

/// <param name="Failure">
/// Why the calculation stopped before it finished, such as the time limit or an error of the engine; null when it
/// finished. A build with no calculation engine reports one Failure and lists no cell.
/// </param>
/// <param name="Uncalculated">
/// The cells whose formula the engine could not evaluate. Each keeps the value the file held before.
/// </param>
public sealed record FormulaCalculation(string? Failure, IReadOnlyList<SheetCell> Uncalculated)
{
    public static FormulaCalculation Finished { get; } = new(null, []);
}
