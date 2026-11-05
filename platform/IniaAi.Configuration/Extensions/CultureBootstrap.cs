using System.Globalization;

namespace IniaAi.Configuration.Extensions;

/// <summary>
/// Process-wide culture bootstrap for the WebApi and WebApp entry points.
/// </summary>
/// <remarks>
/// Called from <c>Program.Main</c> before <c>WebApplication.CreateBuilder</c> so that
/// every request thread sees the same formatting/parsing rules across dev machines and
/// customer deployments. We do this here instead of via
/// <c>&lt;InvariantGlobalization&gt;true&lt;/InvariantGlobalization&gt;</c> in MSBuild
/// because that flag strips ICU from the runtime entirely, which breaks
/// <c>Microsoft.Data.SqlClient</c> at connection-open time on SQL Server customers.
/// </remarks>
public static class CultureBootstrap
{
    /// <summary>
    /// Pins <see cref="CultureInfo.DefaultThreadCurrentCulture"/> and
    /// <see cref="CultureInfo.DefaultThreadCurrentUICulture"/> to
    /// <see cref="CultureInfo.InvariantCulture"/>. Idempotent.
    /// </summary>
    public static void PinInvariantCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
    }
}
