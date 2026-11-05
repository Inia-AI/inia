using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Dto;

namespace IniaAi.Application.Queries.Workflows.ValidateDocumentClasses;

/// <summary>
/// Query to validate document classes for a workflow execution.
/// </summary>
/// <param name="OrganizationId">The organization identifier.</param>
/// <param name="UserId">The user identifier.</param>
/// <param name="WorkflowId">The workflow identifier.</param>
/// <param name="DocumentIds">The list of document identifiers to validate.</param>
public sealed record ValidateDocumentClassesQuery(
    string OrganizationId,
    string UserId,
    string WorkflowId,
    List<string> DocumentIds) : IRequest<ErrorOr<DocumentClassValidationResultDto>>;
