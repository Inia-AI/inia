using IniaAi.WebApp.Components.Pages;
using Microsoft.AspNetCore.Components;
using System.Reflection;

namespace IniaAi.WebApp.Tests.Components;

/// <summary>
/// The counterparty, counterparty type and document screens live in the Vue app, and the Prompt
/// Library is gone. No page of the editor app answers their old addresses.
/// </summary>
public class RetiredScreensTests
{
    [Theory]
    [InlineData("/counterparties")]
    [InlineData("/counterparties/types")]
    [InlineData("/documents-management")]
    [InlineData("/document-viewer/{DocumentId}")]
    [InlineData("/prompt-library")]
    public void NoPageAnswers_ARetiredAddress(string address)
    {
        IEnumerable<string> templates = typeof(Dashboard).Assembly.GetTypes()
            .SelectMany(type => type.GetCustomAttributes<RouteAttribute>())
            .Select(route => route.Template);

        Assert.DoesNotContain(address, templates, StringComparer.OrdinalIgnoreCase);
    }
}
