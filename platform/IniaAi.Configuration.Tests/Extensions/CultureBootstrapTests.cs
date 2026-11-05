using System.Globalization;
using IniaAi.Configuration.Extensions;

namespace IniaAi.Configuration.Tests.Extensions;

public class CultureBootstrapTests
{
    [Fact]
    public void PinInvariantCulture_OverridesNonInvariantDefault()
    {
        CultureInfo? originalCulture = CultureInfo.DefaultThreadCurrentCulture;
        CultureInfo? originalUICulture = CultureInfo.DefaultThreadCurrentUICulture;

        try
        {
            var french = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.DefaultThreadCurrentCulture = french;
            CultureInfo.DefaultThreadCurrentUICulture = french;

            CultureBootstrap.PinInvariantCulture();

            Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.DefaultThreadCurrentCulture);
            Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.DefaultThreadCurrentUICulture);
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = originalCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalUICulture;
        }
    }

    [Fact]
    public void PinInvariantCulture_IsIdempotent()
    {
        CultureInfo? originalCulture = CultureInfo.DefaultThreadCurrentCulture;
        CultureInfo? originalUICulture = CultureInfo.DefaultThreadCurrentUICulture;

        try
        {
            CultureBootstrap.PinInvariantCulture();
            CultureBootstrap.PinInvariantCulture();

            Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.DefaultThreadCurrentCulture);
            Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.DefaultThreadCurrentUICulture);
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = originalCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalUICulture;
        }
    }
}
