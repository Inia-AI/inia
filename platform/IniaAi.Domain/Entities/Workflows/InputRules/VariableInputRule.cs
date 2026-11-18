using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Diagnostics.CodeAnalysis;

namespace IniaAi.Domain.Entities.Workflows.InputRules;

/// <summary>
/// A rule on a workflow variable: when every condition holds, the variable's value must meet the
/// requirement, or the run does not start. <see cref="VariableInputRuleCheck"/> evaluates it.
/// </summary>
public sealed class VariableInputRule
{
    [JsonProperty(nameof(Conditions), ObjectCreationHandling = ObjectCreationHandling.Replace)]
    [AllowNull]
    public List<InputRuleCondition> Conditions
    {
        get;
        set => field = value is null ? [] : [.. value.OfType<InputRuleCondition>()];
    } = [];

    [JsonProperty(nameof(Requirement))]
    [AllowNull]
    public InputRuleRequirement Requirement
    {
        get;
        set => field = value ?? new() { Operator = InputRuleRequirement.NO_OPERATOR };
    } = new();

    public VariableInputRule Clone()
    {
        return new VariableInputRule
        {
            Conditions = [.. Conditions.Select(condition => condition.Clone())],
            Requirement = Requirement.Clone(),
        };
    }
}

/// <summary>
/// One condition of a <see cref="VariableInputRule"/>. A document condition names
/// <see cref="DocumentClassKeys"/>; a variable condition names <see cref="Variable"/> and, for
/// <see cref="InputRuleConditionOperator.EqualTo"/> and <see cref="InputRuleConditionOperator.NotEqualTo"/>,
/// a <see cref="Value"/>.
/// </summary>
public sealed class InputRuleCondition
{
    [JsonProperty(nameof(Operator))]
    [JsonConverter(typeof(StringEnumConverter))]
    public InputRuleConditionOperator Operator { get; set; }

    [JsonProperty(nameof(DocumentClassKeys), NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? DocumentClassKeys { get; set; }

    [JsonProperty(nameof(Variable), NullValueHandling = NullValueHandling.Ignore)]
    public string? Variable { get; set; }

    [JsonProperty(nameof(Value), NullValueHandling = NullValueHandling.Ignore)]
    public string? Value { get; set; }

    public bool IsOnDocuments()
    {
        return Operator is InputRuleConditionOperator.AnyAttached or InputRuleConditionOperator.NoneAttached;
    }

    public bool ComparesValue()
    {
        return Operator is InputRuleConditionOperator.EqualTo or InputRuleConditionOperator.NotEqualTo;
    }

    public InputRuleCondition Clone()
    {
        return new InputRuleCondition
        {
            Operator = Operator,
            DocumentClassKeys = DocumentClassKeys is null ? null : [.. DocumentClassKeys],
            Variable = Variable,
            Value = Value,
        };
    }
}

/// <summary>What a <see cref="VariableInputRule"/> asks of its variable's value.</summary>
public sealed class InputRuleRequirement
{
    /// <summary>The operator of a requirement the content left out, which the save refuses as incomplete.</summary>
    public const InputRuleRequirementOperator NO_OPERATOR = (InputRuleRequirementOperator)(-1);

    [JsonProperty(nameof(Operator))]
    [JsonConverter(typeof(StringEnumConverter))]
    public InputRuleRequirementOperator Operator { get; set; }

    [JsonProperty(nameof(Value), NullValueHandling = NullValueHandling.Ignore)]
    public string? Value { get; set; }

    public bool ComparesValue()
    {
        return Operator is InputRuleRequirementOperator.EqualTo or InputRuleRequirementOperator.NotEqualTo;
    }

    public InputRuleRequirement Clone()
    {
        return new InputRuleRequirement
        {
            Operator = Operator,
            Value = Value,
        };
    }
}

public enum InputRuleConditionOperator
{
    AnyAttached,
    NoneAttached,
    EqualTo,
    NotEqualTo,
    IsEmpty,
    IsNotEmpty,
}

public enum InputRuleRequirementOperator
{
    NotEmpty,
    EqualTo,
    NotEqualTo,
}
