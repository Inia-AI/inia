using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.Counterparty;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;

namespace IniaAi.WebApi.IntegrationTests;

/// <summary>
/// Provides EDM models for ODataQueryOptions tests.
/// </summary>
public static class ODataTestHelper
{
    /// <summary>
    /// Builds an EDM model for the DTO type (CounterpartyListItemDto).
    /// Use when projecting first, then applying OData to the DTO.
    /// </summary>
    public static IEdmModel BuildEdmModelForDto()
    {
        var builder = new ODataConventionModelBuilder();
        _ = builder.EntitySet<CounterpartyListItemDto>("Counterparties");
        return builder.GetEdmModel();
    }

    /// <summary>
    /// Builds an EDM model for <see cref="WorkflowExecutionSummaryDto"/>.
    /// </summary>
    public static IEdmModel BuildEdmModelForWorkflowExecutionSummary()
    {
        var builder = new ODataConventionModelBuilder();
        _ = builder.EntitySet<WorkflowExecutionSummaryDto>("WorkflowExecutionSummaries");
        return builder.GetEdmModel();
    }

    public static ODataQueryOptions<T> BuildFakeODataOptions<T>(IEdmModel edmModel, string query)
    {
        return ODataTestOptions.Build<T>(edmModel, query);
    }
}
