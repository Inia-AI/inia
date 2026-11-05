using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace IniaAi.WebApp.Tests.Components;

/// <summary>
/// Makes each query yield before it runs, as a query to a database server waits on the network.
/// SQLite finishes a query before its await returns, which hides two components that use the
/// circuit's one DbContext at the same time.
/// </summary>
internal sealed class ServerLikeQueryInterceptor : DbCommandInterceptor
{
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        return result;
    }
}
