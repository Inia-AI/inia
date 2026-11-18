namespace IniaAi.WebApi.Models.Dto;

/// <summary>
/// A rule on a workflow variable: when every condition holds, the variable's value must meet the
/// requirement, or the run does not start. Values compare as text, ignoring case and surrounding
/// spaces; a dropdown compares by its stored option value.
/// </summary>
public sealed class WorkflowVariableInputRuleDto
{
    /// <summary>The conditions that must all hold for the rule to apply.</summary>
    public List<InputRuleConditionDto> Conditions { get; set; } = [];

    /// <summary>What the variable's value must meet when the rule applies.</summary>
    public required InputRuleRequirementDto Requirement { get; set; }
}

/// <summary>One condition of a <see cref="WorkflowVariableInputRuleDto"/>.</summary>
public sealed class InputRuleConditionDto
{
    /// <summary>
    /// <c>AnyAttached</c> or <c>NoneAttached</c> (with <see cref="DocumentClassKeys"/>), or
    /// <c>EqualTo</c>, <c>NotEqualTo</c>, <c>IsEmpty</c> or <c>IsNotEmpty</c> (with <see cref="Variable"/>).
    /// </summary>
    public required string Operator { get; set; }

    /// <summary>The document class keys a document condition names.</summary>
    public List<string>? DocumentClassKeys { get; set; }

    /// <summary>The replacement tag of the variable a variable condition names.</summary>
    public string? Variable { get; set; }

    /// <summary>The value <c>EqualTo</c> and <c>NotEqualTo</c> compare with; for a dropdown, the stored option value.</summary>
    public string? Value { get; set; }
}

/// <summary>What a <see cref="WorkflowVariableInputRuleDto"/> asks of its variable's value.</summary>
public sealed class InputRuleRequirementDto
{
    /// <summary><c>NotEmpty</c>, <c>EqualTo</c> or <c>NotEqualTo</c>.</summary>
    public required string Operator { get; set; }

    /// <summary>The value <c>EqualTo</c> and <c>NotEqualTo</c> compare with; for a dropdown, the stored option value.</summary>
    public string? Value { get; set; }
}
