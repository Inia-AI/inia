using IniaAi.Domain.Entities.Workflows.Enums;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IniaAi.Domain.Entities.Workflows.InputRules;

/// <summary>
/// Evaluates the input rules of a workflow's variables against a submission and writes their
/// messages. The run form in the web app evaluates the same rules with the same words; the cases in
/// <c>variable-input-rule-cases.json</c>, copied in both repositories, keep the two in step.
/// </summary>
/// <remarks>
/// A value is compared as text, ignoring case and surrounding spaces; a dropdown by its stored
/// option value. A variable the submission leaves out takes its default, as the run does. A stale
/// rule (one that names a document class or a variable the workflow does not have, a value that is
/// not an option, or a comparison its field cannot make) is skipped, never enforced.
/// </remarks>
public static partial class VariableInputRuleCheck
{
    private const string AND = " and ";
    private const string OR = " or ";
    private const string LIST_SEPARATOR = ", ";

    [GeneratedRegex(@"^\s*(?:[0-9]+(?:\.[0-9]+)*[.)]|[0-9]+(?:\.[0-9]+)+)\s+")]
    private static partial Regex LeadingNumbering();

    /// <summary>
    /// Checks every rule of every variable. <paramref name="submitted"/> holds the submitted
    /// values by replacement tag; <paramref name="attachedClassKeys"/> the document class of each
    /// document on the run.
    /// </summary>
    public static InputRuleOutcome Check(
        Workflow workflow,
        IReadOnlyDictionary<string, JsonElement>? submitted,
        IEnumerable<string?> attachedClassKeys)
    {
        HashSet<string> attached = new(attachedClassKeys.OfType<string>(), StringComparer.Ordinal);
        List<InputRuleViolation> violations = [];
        List<SkippedInputRule> skipped = [];

        foreach (WorkflowVariable variable in workflow.Variables)
        {
            foreach (VariableInputRule rule in variable.InputRules)
            {
                if (ProblemOf(workflow, variable, rule) is (InputRuleProblem kind, string reason))
                {
                    skipped.Add(new SkippedInputRule(variable.ReplacementTag, NameOf(variable), kind, reason));
                    continue;
                }

                bool applies = rule.Conditions.All(condition => Holds(workflow, condition, submitted, attached));
                if (applies && !IsMet(variable, rule.Requirement, submitted))
                {
                    violations.Add(new InputRuleViolation(variable.ReplacementTag, MessageOf(workflow, variable, rule)));
                }
            }
        }

        return new InputRuleOutcome(violations, skipped);
    }

    /// <summary>Every rule of the workflow that a run would skip, with the reason. An incomplete rule is among them.</summary>
    public static List<SkippedInputRule> FindStale(Workflow workflow)
    {
        List<SkippedInputRule> stale = [];
        foreach (WorkflowVariable variable in workflow.Variables)
        {
            foreach (VariableInputRule rule in variable.InputRules)
            {
                if (ProblemOf(workflow, variable, rule) is (InputRuleProblem kind, string reason))
                {
                    stale.Add(new SkippedInputRule(variable.ReplacementTag, NameOf(variable), kind, reason));
                }
            }
        }

        return stale;
    }

    /// <summary>
    /// What is missing from a rule that cannot be evaluated at all, or null when the rule is
    /// complete. A rule needs a condition; a document condition needs a class; a variable
    /// condition needs a field; a comparison needs a value.
    /// </summary>
    public static string? IncompletenessOf(VariableInputRule rule)
    {
        if (rule.Conditions is not { Count: > 0 } conditions)
        {
            return "it has no condition";
        }

        if (!Enum.IsDefined(rule.Requirement.Operator))
        {
            return "it has no requirement";
        }

        foreach (InputRuleCondition condition in conditions)
        {
            if (!Enum.IsDefined(condition.Operator))
            {
                return "a condition has no operator";
            }

            if (condition.IsOnDocuments())
            {
                if (condition.DocumentClassKeys is not { Count: > 0 } classes || classes.Any(string.IsNullOrWhiteSpace))
                {
                    return "a document condition names no document class";
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(condition.Variable))
            {
                return "a condition names no field";
            }

            if (condition.ComparesValue() && string.IsNullOrWhiteSpace(condition.Value))
            {
                return "a condition has no value to compare with";
            }
        }

        if (rule.Requirement.ComparesValue() && string.IsNullOrWhiteSpace(rule.Requirement.Value))
        {
            return "the requirement has no value to compare with";
        }

        return null;
    }

    /// <summary>The message a submission that breaks <paramref name="rule"/> is refused with.</summary>
    public static string MessageOf(Workflow workflow, WorkflowVariable variable, VariableInputRule rule)
    {
        string because = string.Join(AND, rule.Conditions.Select(condition => ClauseOf(workflow, condition)));
        return $"{RequirementOf(variable, rule.Requirement)} because {because}.";
    }

    /// <summary>The line a run's log gets for a rule it skipped.</summary>
    public static string LogLineOf(SkippedInputRule skipped)
    {
        return $"The rule on \"{skipped.VariableName}\" was not checked because {skipped.Reason}. Fix the rule on this field in the workflow editor.";
    }

    /// <summary>The warning a save gets for a rule that runs will skip.</summary>
    public static string SaveWarningOf(SkippedInputRule stale)
    {
        return $"A rule on \"{stale.VariableName}\" will not be checked because {stale.Reason}. Fix the rule or remove it.";
    }

    /// <summary>A display name as a message shows it: without a leading number such as "2." or "3.1".</summary>
    public static string WithoutNumbering(string displayName)
    {
        string stripped = LeadingNumbering().Replace(displayName, string.Empty).Trim();
        return stripped.Length == 0 ? displayName.Trim() : stripped;
    }

    /// <summary>Whether an operator that compares with a value can be used on a field of this type.</summary>
    public static bool ComparesWithValue(WorkflowVariableTypes type)
    {
        return type is WorkflowVariableTypes.String or WorkflowVariableTypes.Dropdown;
    }

    public static string NameOf(WorkflowVariable variable)
    {
        return WithoutNumbering(string.IsNullOrWhiteSpace(variable.DisplayName) ? variable.ReplacementTag : variable.DisplayName);
    }

    private static (InputRuleProblem Kind, string Reason)? ProblemOf(Workflow workflow, WorkflowVariable variable, VariableInputRule rule)
    {
        if (IncompletenessOf(rule) is string incomplete)
        {
            return (InputRuleProblem.Incomplete, incomplete);
        }

        if (rule.Requirement.ComparesValue() && ComparisonProblemOf(variable, rule.Requirement.Value!) is { } requirementProblem)
        {
            return requirementProblem;
        }

        foreach (InputRuleCondition condition in rule.Conditions)
        {
            if (condition.IsOnDocuments())
            {
                string? missingClass = condition.DocumentClassKeys!.FirstOrDefault(key => FindClass(workflow, key) is null);
                if (missingClass is not null)
                {
                    return (InputRuleProblem.MissingDocumentClass, $"it names the document class \"{missingClass.Trim()}\", which this workflow does not have");
                }

                continue;
            }

            WorkflowVariable? other = FindVariable(workflow, condition.Variable!);
            if (other is null)
            {
                return (InputRuleProblem.MissingVariable, $"it names the field \"{condition.Variable!.Trim()}\", which this workflow does not have");
            }

            if (condition.ComparesValue() && ComparisonProblemOf(other, condition.Value!) is { } conditionProblem)
            {
                return conditionProblem;
            }
        }

        return null;
    }

    private static (InputRuleProblem Kind, string Reason)? ComparisonProblemOf(WorkflowVariable variable, string value)
    {
        if (!ComparesWithValue(variable.Type))
        {
            return (InputRuleProblem.NotComparable, $"\"{NameOf(variable)}\" is not a text or dropdown field, so it cannot be compared with a value");
        }

        if (variable.Type is WorkflowVariableTypes.Dropdown && OptionKeyOf(variable, value) is null)
        {
            return (InputRuleProblem.NotAnOption, $"\"{value.Trim()}\" is not an option of \"{NameOf(variable)}\"");
        }

        return null;
    }

    private static bool Holds(
        Workflow workflow,
        InputRuleCondition condition,
        IReadOnlyDictionary<string, JsonElement>? submitted,
        HashSet<string> attached)
    {
        switch (condition.Operator)
        {
            case InputRuleConditionOperator.AnyAttached:
                return condition.DocumentClassKeys!.Any(key => attached.Contains(FindClass(workflow, key)!.Key));

            case InputRuleConditionOperator.NoneAttached:
                return !condition.DocumentClassKeys!.Any(key => attached.Contains(FindClass(workflow, key)!.Key));
        }

        WorkflowVariable other = FindVariable(workflow, condition.Variable!)!;
        JsonElement? value = EffectiveValueOf(other, submitted);

        return condition.Operator switch
        {
            InputRuleConditionOperator.EqualTo => IsSame(value, condition.Value!),
            InputRuleConditionOperator.NotEqualTo => !IsSame(value, condition.Value!),
            InputRuleConditionOperator.IsEmpty => IsEmpty(value),
            InputRuleConditionOperator.IsNotEmpty => !IsEmpty(value),
            _ => false,
        };
    }

    private static bool IsMet(
        WorkflowVariable variable,
        InputRuleRequirement requirement,
        IReadOnlyDictionary<string, JsonElement>? submitted)
    {
        JsonElement? value = EffectiveValueOf(variable, submitted);

        return requirement.Operator switch
        {
            InputRuleRequirementOperator.NotEmpty => !IsEmpty(value),
            InputRuleRequirementOperator.EqualTo => IsSame(value, requirement.Value!),
            InputRuleRequirementOperator.NotEqualTo => !IsSame(value, requirement.Value!),
            _ => true,
        };
    }

    private static JsonElement? EffectiveValueOf(WorkflowVariable variable, IReadOnlyDictionary<string, JsonElement>? submitted)
    {
        return WorkflowVariableDisplay.FindValue(submitted, variable.ReplacementTag) ?? variable.DefaultValueAsJson();
    }

    private static bool IsEmpty(JsonElement? value)
    {
        if (value is not { } element)
        {
            return true;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => true,
            JsonValueKind.String => string.IsNullOrWhiteSpace(element.GetString()),
            JsonValueKind.Array => element.GetArrayLength() == 0,
            _ => false,
        };
    }

    private static bool IsSame(JsonElement? value, string expected)
    {
        if (value is not { ValueKind: JsonValueKind.String } element)
        {
            return false;
        }

        return Normalized(element.GetString()!) == Normalized(expected);
    }

    private static string Normalized(string text)
    {
        return text.Trim().ToLowerInvariant();
    }

    private static string RequirementOf(WorkflowVariable variable, InputRuleRequirement requirement)
    {
        string name = NameOf(variable);
        return requirement.Operator switch
        {
            InputRuleRequirementOperator.EqualTo => $"\"{name}\" must be \"{LabelOf(variable, requirement.Value!)}\"",
            InputRuleRequirementOperator.NotEqualTo => $"\"{name}\" cannot be \"{LabelOf(variable, requirement.Value!)}\"",
            _ => $"Choose a value for \"{name}\"",
        };
    }

    private static string ClauseOf(Workflow workflow, InputRuleCondition condition)
    {
        if (condition.IsOnDocuments())
        {
            string classes = JoinWithOr([.. condition.DocumentClassKeys!.Select(key => $"\"{NameOf(FindClass(workflow, key)!)}\"")]);
            return condition.Operator is InputRuleConditionOperator.AnyAttached
                ? $"{classes} is attached"
                : $"no {classes} is attached";
        }

        WorkflowVariable other = FindVariable(workflow, condition.Variable!)!;
        string name = NameOf(other);
        return condition.Operator switch
        {
            InputRuleConditionOperator.EqualTo => $"\"{name}\" is \"{LabelOf(other, condition.Value!)}\"",
            InputRuleConditionOperator.NotEqualTo => $"\"{name}\" is not \"{LabelOf(other, condition.Value!)}\"",
            InputRuleConditionOperator.IsEmpty => $"\"{name}\" is empty",
            _ => $"\"{name}\" has a value",
        };
    }

    private static string JoinWithOr(IReadOnlyList<string> items)
    {
        return items.Count == 1
            ? items[0]
            : $"{string.Join(LIST_SEPARATOR, items.Take(items.Count - 1))}{OR}{items[^1]}";
    }

    private static string LabelOf(WorkflowVariable variable, string value)
    {
        return variable.Type is WorkflowVariableTypes.Dropdown && OptionKeyOf(variable, value) is string key
            ? variable.Options[key]
            : value.Trim();
    }

    private static string? OptionKeyOf(WorkflowVariable variable, string value)
    {
        string wanted = Normalized(value);
        return variable.Options.Keys.FirstOrDefault(key => Normalized(key) == wanted);
    }

    private static string NameOf(DocumentClass documentClass)
    {
        return WithoutNumbering(string.IsNullOrWhiteSpace(documentClass.DisplayName) ? documentClass.Key : documentClass.DisplayName);
    }

    private static DocumentClass? FindClass(Workflow workflow, string key)
    {
        string wanted = key.Trim();
        return workflow.DocumentClasses.FirstOrDefault(documentClass => string.Equals(documentClass.Key, wanted, StringComparison.Ordinal));
    }

    private static WorkflowVariable? FindVariable(Workflow workflow, string tag)
    {
        string wanted = tag.Trim();
        return workflow.Variables.FirstOrDefault(variable => string.Equals(variable.ReplacementTag, wanted, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>What <see cref="VariableInputRuleCheck.Check"/> found: the rules a submission breaks and the rules it skipped.</summary>
public sealed record InputRuleOutcome(IReadOnlyList<InputRuleViolation> Violations, IReadOnlyList<SkippedInputRule> Skipped);

/// <summary>A rule a submission breaks, with the message it is refused with.</summary>
public sealed record InputRuleViolation(string ReplacementTag, string Message);

/// <summary>A rule that cannot be checked, with the reason, for a run's log or a save warning.</summary>
public sealed record SkippedInputRule(string ReplacementTag, string VariableName, InputRuleProblem Kind, string Reason);

/// <summary>Why a rule cannot be checked.</summary>
public enum InputRuleProblem
{
    Incomplete,
    MissingDocumentClass,
    MissingVariable,
    NotAnOption,
    NotComparable,
}
