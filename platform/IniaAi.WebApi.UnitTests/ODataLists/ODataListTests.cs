using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.UnitTests.Common;
using Microsoft.AspNetCore.OData.Query;

namespace IniaAi.WebApi.UnitTests.ODataLists;

/// <summary>
/// An OData list handler run against the in-memory test database.
/// </summary>
public abstract class ODataListTests : FixtureHandlerTests
{
    protected override string FixtureFolder => "IniaAi.WebApi.UnitTests/ODataLists/Fixtures";

    protected static ODataQueryOptions<TDto> Options<TDto>(string query)
        where TDto : class
    {
        return ODataTestOptions.For<TDto>(query);
    }
}
