namespace IniaAi.Core.Workflows.Strategies;

public static class CalculateFormulaHelp
{
    public sealed record Example(
        string Formula,
        string? ExpectedResult = null,
        string? Caption = null);

    public sealed record Section(
        string Title,
        string Description,
        IReadOnlyList<Example> Examples);

    public static IReadOnlyList<Section> Sections { get; } =
    [
        new Section(
            "Placeholders & inputs",
            "Formulas can reference workflow variables and upstream block outputs using placeholders. "
                + "Dates must resolve to ISO format (yyyy-MM-dd). Bare ISO dates are auto-quoted, so "
                + "addDays({someDate}, 10) works without you writing the quotes.",
            [
                new Example(
                    Formula: "addDays({maturityDate}, 30)",
                    Caption: "resolves to e.g. addDays('2025-04-15', 30) → 2025-05-15"),
                new Example(
                    Formula: "{amount} * 1.2",
                    Caption: "scales a numeric variable by 20%"),
            ]),

        new Section(
            "Dates — add, subtract, diff",
            "Add or subtract days, months, or years (use a negative count to subtract). "
                + "diffDays returns the whole-day difference a − b.",
            [
                new Example("addDays('2024-01-01', 30)", "2024-01-31"),
                new Example("addDays('2024-01-01', -7)", "2023-12-25", "negative count subtracts"),
                new Example("addMonths('2024-01-31', 1)", "2024-02-29", "clamps to the last valid day"),
                new Example("addMonths('2023-01-31', 1)", "2023-02-28", "non-leap year clamping"),
                new Example("addYears('2024-02-29', 1)", "2025-02-28", "Feb-29 rolls to Feb-28"),
                new Example("diffDays('2024-03-01', '2024-01-01')", "60"),
            ]),

        new Section(
            "Dates — today, parts, boundaries",
            "Access today's date, pull out year/month/day as numbers, or snap to the first/last day of the month.",
            [
                new Example(
                    Formula: "today()",
                    Caption: "returns the current date as yyyy-MM-dd"),
                new Example("year('2024-06-15')", "2024"),
                new Example("month('2024-06-15')", "6"),
                new Example("day('2024-06-15')", "15"),
                new Example("startOfMonth('2024-06-15')", "2024-06-01"),
                new Example("endOfMonth('2024-02-15')", "2024-02-29", "respects leap years"),
            ]),

        new Section(
            "Conditionals",
            "Pick between values based on a test. if takes one condition; ifs chains multiple conditions with a default.",
            [
                new Example("if(5 > 3, 10, 20)", "10"),
                new Example("if(month('2024-03-15') <= 3, 'Q1', 'other')", "Q1"),
                new Example("ifs(3 < 1, 'a', 3 < 5, 'b', 'default')", "b", "first matching branch wins"),
                new Example("in(2, 1, 2, 3)", "True", "true when the value is in the list"),
            ]),

        new Section(
            "Math & rounding",
            "Standard numeric helpers. All function names are case-insensitive.",
            [
                new Example("abs(-7)", "7"),
                new Example("round(3.14159, 2)", "3.14"),
                new Example("min(5, 2)", "2", "two-argument form only"),
                new Example("max(5, 2)", "5"),
                new Example("max(max(5, 2), 8)", "8", "nest calls for more than two values"),
                new Example("sqrt(16)", "4"),
                new Example("pow(2, 10)", "1024"),
                new Example("floor(3.9)", "3"),
                new Example("ceiling(3.1)", "4"),
            ]),

        new Section(
            "Operators",
            "Arithmetic: + − * / %. Comparison: < <= > >= = (or ==) != (or <>). Logical: and/&&, or/||, not/!.",
            [
                new Example("(10 + 5) * 2", "30"),
                new Example("7 % 3", "1", "modulo"),
                new Example("5 > 3 and 2 < 4", "True"),
                new Example("not (1 = 2)", "True"),
                new Example("5 <> 3", "True"),
            ]),
    ];
}
