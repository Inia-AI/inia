using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Responses;
using Microsoft.Extensions.Logging;
using NCalc;
using NCalc.Exceptions;
using System.Globalization;
using System.Text;

namespace IniaAi.Core.Workflows.Strategies;

public class CalculateFormulaStrategy : IBlockStrategy
{
    private const string DATE_FORMAT = "yyyy-MM-dd";

    public async Task<BlockStrategyOutput> ExecuteBlockAsync(
        Block block,
        BlockExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        BlockStrategyOutput output = new();

        CalculateFormulaProperties props = block.Properties as CalculateFormulaProperties
            ?? throw new InvalidOperationException($"Block properties must be of type {nameof(CalculateFormulaProperties)}");

        props.ExecutionState ??= new(props);

        string? formula = props.ExecutionState.Formula;
        string noDataText = context.PlatformSettings.IniaAi.NoDataForPlaceHolderText;

        if (string.IsNullOrWhiteSpace(formula)
            || (!string.IsNullOrWhiteSpace(noDataText) && formula.Contains(noDataText)))
        {
            context.Logger.LogWarning(
                "{block} No formula available",
                block);

            return output;
        }

        // Placeholder substitution leaves bare ISO dates in the formula (e.g. 2025-04-15),
        // which NCalc would otherwise evaluate as subtraction. Wrap them in quotes so they
        // reach our date functions as strings.
        string sanitizedFormula = QuoteBareIsoDates(formula);

        object? evaluated;
        try
        {
            Expression expression = new(
                sanitizedFormula,
                ExpressionOptions.IgnoreCaseAtBuiltInFunctions,
                CultureInfo.InvariantCulture);

            ConfigureDateFunctions(expression);

            evaluated = expression.Evaluate();
        }
        catch (NCalcException ex)
        {
            context.Logger.LogError(
                "{block} Error calculating the formula: {ExceptionType}",
                block,
                ex.GetType().Name);

            throw new BlockFailureException($"Unable to process the formula '{formula}': {ex.Message}");
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new BlockFailureException(ex.Message);
        }

        string rawResult = FormatResult(evaluated, formula);

        _ = new BlockResponse(block)
        {
            RawResponse = rawResult
        };

        context.Logger.LogTrace(
            "{block} Calculated the formula: {Length} characters",
            block,
            rawResult.Length);

        await Task.CompletedTask;

        return output;
    }

    private static string FormatResult(object? evaluated, string formula)
    {
        return evaluated switch
        {
            null => throw new BlockFailureException($"Unable to process the formula '{formula}': evaluation returned null"),
            DateTime dt => dt.ToString(DATE_FORMAT, CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => evaluated.ToString() ?? string.Empty,
        };
    }

    private static void ConfigureDateFunctions(Expression expression)
    {
        // NCalc 6: evaluate a function argument via args.Evaluate(index); the indexer now returns
        // the unevaluated LogicalExpression.
        expression.Functions["addDays"] = args =>
            CoerceToDate(args.Evaluate(0)).AddDays(
                Convert.ToInt32(args.Evaluate(1), CultureInfo.InvariantCulture));

        expression.Functions["addMonths"] = args =>
            CoerceToDate(args.Evaluate(0)).AddMonths(
                Convert.ToInt32(args.Evaluate(1), CultureInfo.InvariantCulture));

        expression.Functions["addYears"] = args =>
            CoerceToDate(args.Evaluate(0)).AddYears(
                Convert.ToInt32(args.Evaluate(1), CultureInfo.InvariantCulture));

        expression.Functions["diffDays"] = args =>
            (int)(CoerceToDate(args.Evaluate(0)) - CoerceToDate(args.Evaluate(1))).TotalDays;

        expression.Functions["today"] = _ => DateTime.Today;

        expression.Functions["year"] = args => CoerceToDate(args.Evaluate(0)).Year;
        expression.Functions["month"] = args => CoerceToDate(args.Evaluate(0)).Month;
        expression.Functions["day"] = args => CoerceToDate(args.Evaluate(0)).Day;

        expression.Functions["startOfMonth"] = args =>
        {
            DateTime d = CoerceToDate(args.Evaluate(0));
            return new DateTime(d.Year, d.Month, 1);
        };

        expression.Functions["endOfMonth"] = args =>
        {
            DateTime d = CoerceToDate(args.Evaluate(0));
            return new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
        };
    }

    private static DateTime CoerceToDate(object? value)
    {
        return value switch
        {
            DateTime dt => dt,
            string s when DateTime.TryParseExact(
                s,
                DATE_FORMAT,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsed) => parsed,
            _ => throw new BlockFailureException($"Expected an ISO date ({DATE_FORMAT}) or DateTime value, got: '{value}'"),
        };
    }

    private static string QuoteBareIsoDates(string formula)
    {
        StringBuilder result = new(formula.Length + 16);
        int i = 0;
        while (i < formula.Length)
        {
            char c = formula[i];

            if (c is '\'' or '"' or '#')
            {
                int end = formula.IndexOf(c, i + 1);
                if (end < 0)
                {
                    result.Append(formula, i, formula.Length - i);
                    return result.ToString();
                }

                result.Append(formula, i, end - i + 1);
                i = end + 1;
                continue;
            }

            if (IsIsoDateAt(formula, i))
            {
                result.Append('\'');
                result.Append(formula, i, 10);
                result.Append('\'');
                i += 10;
                continue;
            }

            result.Append(c);
            i++;
        }

        return result.ToString();
    }

    private static bool IsIsoDateAt(string s, int pos)
    {
        if (pos + 10 > s.Length)
        {
            return false;
        }

        for (int j = 0; j < 4; j++)
        {
            if (!char.IsDigit(s[pos + j]))
            {
                return false;
            }
        }

        if (s[pos + 4] != '-'
            || !char.IsDigit(s[pos + 5])
            || !char.IsDigit(s[pos + 6])
            || s[pos + 7] != '-'
            || !char.IsDigit(s[pos + 8])
            || !char.IsDigit(s[pos + 9]))
        {
            return false;
        }

        if (pos > 0 && char.IsDigit(s[pos - 1]))
        {
            return false;
        }

        if (pos + 10 < s.Length && char.IsDigit(s[pos + 10]))
        {
            return false;
        }

        return true;
    }
}
