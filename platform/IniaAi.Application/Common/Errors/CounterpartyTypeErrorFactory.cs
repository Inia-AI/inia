using ErrorOr;
using IniaAi.Common.Constants;

namespace IniaAi.Application.Errors;

/// <summary>
/// Provides reusable errors for counterparty type operations.
/// </summary>
public static class CounterpartyTypeErrorFactory
{
    private const string BUILT_IN_CODE = "CounterpartyType.BuiltIn";

    public static Error NotFound(string id)
    {
        return Error.NotFound("CounterpartyType.NotFound", $"{CounterpartyTerms.SINGULAR} type with ID '{id}' was not found.");
    }

    public static Error DuplicateName(string name)
    {
        return Error.Conflict("CounterpartyType.DuplicateName", $"A {CounterpartyTerms.SINGULAR_LOWER} type named '{name}' already exists.");
    }

    public static Error BuiltInRenamed(string name)
    {
        return Error.Conflict(
            BUILT_IN_CODE,
            $"'{name}' is a built-in {CounterpartyTerms.SINGULAR_LOWER} type. The platform finds it by name, so it cannot be renamed. Its description can change.");
    }

    public static Error BuiltInDeleted(string name)
    {
        return Error.Conflict(
            BUILT_IN_CODE,
            $"'{name}' is a built-in {CounterpartyTerms.SINGULAR_LOWER} type. The platform finds it by name, so it cannot be deleted.");
    }
}
