namespace IniaAi.Domain.Abstractions.Entities;

/// <summary>
/// Marks a domain entity that carries templated string fields whose resolved
/// values live on a separate execution-state snapshot. Implementations expose
/// operations to replace placeholders, materialize execution state, and query
/// for placeholders referenced by the template.
/// </summary>
public interface ITemplatable
{
    /// <summary>
    /// Be cautious when calling this method as it performs reflection-based
    /// replacement on all string, dictionary, and list properties of the
    /// implementing class.
    /// </summary>
    /// <param name="replacements">A dictionary where keys are placeholders
    /// to be replaced and values are the corresponding replacement strings.
    /// The method will search for occurrences of the placeholders in the
    /// implementing class's properties and replace them with the provided
    /// values.</param>
    /// <param name="stringComparison">Specifies the rules for string comparison.
    /// The default is <see cref="IniaAi.Common.Constants.ReplacementTags.Comparison"/>
    /// (<see cref="StringComparison.Ordinal"/>).</param>
    void ReplaceAll(
        Dictionary<string, string?> replacements,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison);

    void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison);

    void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison);

    void ClearExecutionState();

    bool HasPlaceholder(
        string placeholder,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison);

    List<string> GetAllPlaceholders();
}
