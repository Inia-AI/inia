using Bunit;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Components.Blocks;
using IniaAi.WebApp.Tests.Components;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Blocks;

/// <summary>
/// A Web API call's address must name its host directly: the field says so while the author types,
/// before the save refuses it.
/// </summary>
public class WebApiCallAddressFieldTests : SignedInRenderTest
{
    private const string EDITOR = "editor@example.test";
    private const string BLOCK_NAME = "Company search";

    public WebApiCallAddressFieldTests()
        : base(new ServerLikeQueryInterceptor())
    {
    }

    [Fact]
    public async Task AWebAddressWhoseHostIsAPlaceholder_ShowsTheProblemOnItsField()
    {
        _ = await SignInAsync(EDITOR, Roles.USER, Roles.WORKFLOW_EDITOR);
        var properties = new WebApiCallProperties { UrlTemplate = "https://{{Host}}/v1/search" };

        IRenderedComponent<WebApiCallBlock> editor = RenderBlock(properties);

        string problem = properties.FixedHostProblems(BLOCK_NAME).Single();
        editor.WaitForAssertion(() => Assert.Contains(problem, editor.Markup));
    }

    [Fact]
    public async Task AWebAddressWithItsHostWrittenOut_ShowsNoProblem()
    {
        _ = await SignInAsync(EDITOR, Roles.USER, Roles.WORKFLOW_EDITOR);
        var properties = new WebApiCallProperties { UrlTemplate = "https://api.example.com/v1/{Id}?q={Query}" };

        IRenderedComponent<WebApiCallBlock> editor = RenderBlock(properties);

        editor.WaitForAssertion(() => Assert.Contains(WebApiCallProperties.URL_TEMPLATE_LABEL, editor.Markup));
        Assert.DoesNotContain("must name its host directly", editor.Markup);
    }

    private IRenderedComponent<WebApiCallBlock> RenderBlock(WebApiCallProperties properties)
    {
        var block = new Block(BlockTypes.WebApiCall, new Section("test", new Workflow("test")))
        {
            Name = BLOCK_NAME,
            Properties = properties,
        };

        return RenderInApp<WebApiCallBlock>(parameters => parameters
            .Add(component => component.Block, block)
            .Add(component => component.BlockChanged, () => { }));
    }
}
