using ErrorOr;
using IniaAi.Common.Constants;

namespace IniaAi.Application.Errors;

/// <summary>
/// Provides reusable errors for counterparty-related operations.
/// </summary>
public static class CounterpartyErrorFactory
{
    public static Error OrganizationMissing =>
        Error.Validation("Counterparty.MissingOrganization", "Organization ID is missing or invalid.");

    public static Error DuplicateId(string id)
    {
        return Error.Conflict(
            "Counterparty.DuplicateId",
            $"A {CounterpartyTerms.SINGULAR_LOWER} with ID '{id}' already exists. Choose a different reference ID.");
    }

    public static Error DuplicateIdInOrganization(string id)
    {
        return Error.Conflict(
            "Counterparty.DuplicateId",
            $"A {CounterpartyTerms.SINGULAR_LOWER} with ID '{id}' already exists in your organization. " +
            $"Open the {CounterpartyTerms.PLURAL} tab to update the existing record, or choose a different reference ID.");
    }

    public static Error DuplicateName(string name, string organizationId)
    {
        return Error.Conflict(
            "Counterparty.DuplicateName",
            $"A {CounterpartyTerms.SINGULAR_LOWER} with the name '{name}' already exists in this organization. " +
            $"If it is classified under a different {CounterpartyTerms.SINGULAR_LOWER} type, open the {CounterpartyTerms.PLURAL} tab and assign the correct type.");
    }

    public static Error NotFound(string id)
    {
        return Error.NotFound("Counterparty.NotFound", $"{CounterpartyTerms.SINGULAR} with ID '{id}' was not found.");
    }
}
