using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.Counterparty;
using IniaAi.WebApi.Contracts.Dto;

namespace IniaAi.Application.Commands.Counterparties.CreateCounterparty;

/// <summary>
/// Command to create a new counterparty.
/// </summary>
/// <param name="Id">The unique identifier of the counterparty.</param>
/// <param name="OrganizationId">The organization to which the counterparty belongs.</param>
/// <param name="ParentId">The parent counterparty ID, if any.</param>
/// <param name="CounterpartyTypeIds">The type/category IDs of the counterparty.</param>
/// <param name="Name">The display name of the counterparty.</param>
/// <param name="NameVariants">Name variants or aliases of the counterparty.</param>
/// <param name="Email">The email address of the counterparty.</param>
/// <param name="PhoneNumber">The phone number of the counterparty.</param>
/// <param name="Website">The website URL of the counterparty.</param>
/// <param name="Address">The address of the counterparty.</param>
/// <param name="LastFinancialAnalysisResult">Latest financial analysis result.</param>
/// <param name="Properties">Freeform property bag; null leaves the created counterparty without one.</param>
public record CreateCounterpartyCommand(
    string? Id,
    string? OrganizationId,
    string? ParentId,
    List<string>? CounterpartyTypeIds,
    string Name,
    string NameVariants,
    string? Email,
    string? PhoneNumber,
    string? Website,
    string? Address,
    string? LastFinancialAnalysisResult,
    Dictionary<string, string>? Properties = null) : IRequest<ErrorOr<CounterpartyDto>>, ICounterpartyFields;
