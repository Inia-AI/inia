using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using Microsoft.OData.UriParser;

namespace IniaAi.Testing.DependencyInjection;

/// <summary>
/// OData query options built the way a controller receives them, so a list handler is tested
/// without a server. The request carries a scheme and a host because OData builds a next-page
/// link from it, and on Windows a link with neither is not a valid URI.
/// </summary>
public static class ODataTestOptions
{
    private const string TEST_HOST = "localhost";

    public static IEdmModel ModelFor<T>()
        where T : class
    {
        var builder = new ODataConventionModelBuilder();
        _ = builder.EntitySet<T>(typeof(T).Name);
        return builder.GetEdmModel();
    }

    public static ODataQueryOptions<T> Build<T>(IEdmModel model, string query)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = Uri.UriSchemeHttps;
        context.Request.Host = new HostString(TEST_HOST);
        context.Request.QueryString = new QueryString(query);
        return new ODataQueryOptions<T>(new ODataQueryContext(model, typeof(T), new ODataPath()), context.Request);
    }

    public static ODataQueryOptions<T> For<T>(string query)
        where T : class
    {
        return Build<T>(ModelFor<T>(), query);
    }
}
