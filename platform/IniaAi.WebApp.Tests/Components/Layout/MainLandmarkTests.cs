using AngleSharp.Dom;
using Bunit;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApp.Components.Layout;
using Microsoft.AspNetCore.Components;

namespace IniaAi.WebApp.Tests.Components.Layout;

public class MainLandmarkTests : SignedInRenderTest
{
    private const string PAGE_HEADING = "Workflow Editors";

    [Fact]
    public async Task TheLayout_WrapsThePageAndNothingElseInOneMainLandmark()
    {
        _ = await SignInAsync("editor@inia.ai", Roles.USER, Roles.WORKFLOW_EDITOR);
        Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));

        IRenderedComponent<MainLayout> layout = Render<MainLayout>(parameters => parameters
            .Add(p => p.Body, (RenderFragment)(builder => builder.AddMarkupContent(0, $"<h1>{PAGE_HEADING}</h1>"))));

        layout.WaitForAssertion(() => Assert.Contains(PAGE_HEADING, layout.Markup, StringComparison.Ordinal));
        IElement main = Assert.Single(layout.FindAll("main, [role=main]"));
        Assert.Equal(PAGE_HEADING, main.QuerySelector("h1")?.TextContent);
        Assert.Null(main.QuerySelector("header, nav, aside, .mud-appbar, .mud-drawer"));
    }
}
