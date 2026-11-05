using AngleSharp.Dom;
using Bunit;
using ErrorOr;
using IniaAi.Application.Commands.StoredKeys.SetStoredKey;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Core.Workflows.StoredKeys;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.StoredKeys;
using IniaAi.WebApp.Components.Admin.Pages;
using IniaAi.WebApp.Components.WorkflowEditor.Components.Blocks;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.WebApp.Tests.Components.Admin;

/// <summary>
/// An admin keeps the organization's API keys on the Stored keys tab, and a web API call block
/// chooses one by name, so the workflow never holds the key.
/// </summary>
public class StoredKeysTests : SignedInRenderTest
{
    private const string ADMIN = "admin@example.test";
    private const string KEY_NAME = "ParallelAi";
    private const string KEY_VALUE = "fake test value";
    private const string STORED_KEYS_TAB = "Stored keys";
    private const string CREDENTIAL_WARNING = "This header carries a credential.";

    public StoredKeysTests()
        : base(new ServerLikeQueryInterceptor())
    {
    }

    [Fact]
    public async Task AnAdmin_AddsAKey_AndTheTabListsItsNameButNeverItsValue()
    {
        _ = await SignInAsync(ADMIN, Roles.USER, Roles.ADMIN);
        IRenderedComponent<MudDialogProvider> dialogs = Render<MudDialogProvider>();
        IRenderedComponent<Administration> page = await OpenStoredKeysTabAsync();

        await page.InvokeAsync(() => Button(page, "Add Key").Click());
        dialogs.WaitForAssertion(() => Assert.NotNull(DialogField(dialogs, "Value")));
        await dialogs.InvokeAsync(() => DialogField(dialogs, "Name")!.Input(KEY_NAME));
        await dialogs.InvokeAsync(() => DialogField(dialogs, "Value")!.Input(KEY_VALUE));
        await dialogs.InvokeAsync(() => Button(dialogs, "Save Key").Click());

        page.WaitForAssertion(() => Assert.True(Shows(page, $"Delete {KEY_NAME}")));
        Assert.DoesNotContain(KEY_VALUE, page.Markup);
        Assert.Equal(KEY_VALUE, await ResolveAsync(KEY_NAME));
    }

    [Fact]
    public async Task AnAdmin_DeletesAKey_AfterConfirming()
    {
        _ = await SignInAsync(ADMIN, Roles.USER, Roles.ADMIN);
        await StoreKeyAsync(KEY_NAME, KEY_VALUE);
        IRenderedComponent<MudDialogProvider> dialogs = Render<MudDialogProvider>();
        IRenderedComponent<Administration> page = await OpenStoredKeysTabAsync();
        page.WaitForAssertion(() => Assert.True(Shows(page, $"Delete {KEY_NAME}")));

        await page.InvokeAsync(() => page.Find($"[aria-label='Delete {KEY_NAME}']").Click());
        dialogs.WaitForAssertion(() => Assert.NotNull(Button(dialogs, "Delete")));
        await dialogs.InvokeAsync(() => Button(dialogs, "Delete").Click());

        page.WaitForAssertion(() => Assert.Contains("This organization has no stored keys.", page.Markup));
        Assert.Null(await ResolveAsync(KEY_NAME));
    }

    [Fact]
    public async Task ATypedValueInACredentialHeader_ShowsTheWarning()
    {
        _ = await SignInAsync(ADMIN, Roles.USER, Roles.ADMIN);
        Block block = WebApiCallBlock(new WebApiCallProperties
        {
            AdditionalHeaders = new Dictionary<string, string> { ["X-API-Key"] = KEY_VALUE },
        });

        IRenderedComponent<WebApiCallBlock> editor = RenderBlock(block);

        editor.WaitForAssertion(() => Assert.Contains(CREDENTIAL_WARNING, editor.Markup));
    }

    [Fact]
    public async Task ATypedValueInAnOrdinaryHeader_ShowsNoWarning()
    {
        _ = await SignInAsync(ADMIN, Roles.USER, Roles.ADMIN);
        Block block = WebApiCallBlock(new WebApiCallProperties
        {
            AdditionalHeaders = new Dictionary<string, string> { ["Content-Language"] = "en" },
        });

        IRenderedComponent<WebApiCallBlock> editor = RenderBlock(block);

        editor.WaitForAssertion(() => Assert.Contains("Content-Language", editor.Markup));
        Assert.DoesNotContain(CREDENTIAL_WARNING, editor.Markup);
    }

    [Fact]
    public async Task InsertingAStoredKey_PutsItsPlaceholderInTheHeaderValue_AndClearsTheWarning()
    {
        _ = await SignInAsync(ADMIN, Roles.USER, Roles.ADMIN);
        await StoreKeyAsync(KEY_NAME, KEY_VALUE);
        var properties = new WebApiCallProperties
        {
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " },
        };
        IRenderedComponent<WebApiCallBlock> editor = RenderBlock(WebApiCallBlock(properties));
        editor.WaitForAssertion(() => Assert.Contains(CREDENTIAL_WARNING, editor.Markup));

        await editor.InvokeAsync(() => editor.Find("[aria-label='Insert a stored key into Authorization'] button, button[aria-label='Insert a stored key into Authorization']").Click());
        Popovers.WaitForAssertion(() => Assert.Contains(Popovers.FindAll(".mud-menu-item"), item => item.TextContent.Trim() == KEY_NAME));
        await Popovers.InvokeAsync(() => Popovers.FindAll(".mud-menu-item").First(item => item.TextContent.Trim() == KEY_NAME).Click());

        editor.WaitForAssertion(() => Assert.Equal($"Bearer {StoredKeyPlaceholders.Token(KEY_NAME)}", properties.AdditionalHeaders["Authorization"]), TimeSpan.FromSeconds(2));
        editor.WaitForAssertion(() => Assert.DoesNotContain(CREDENTIAL_WARNING, editor.Markup));
    }

    [Fact]
    public async Task AStoredKeyHeader_ShowsThePlaceholderAfterARun_NotTheValue()
    {
        _ = await SignInAsync(ADMIN, Roles.USER, Roles.ADMIN);
        await StoreKeyAsync(KEY_NAME, KEY_VALUE);
        Block block = WebApiCallBlock(new WebApiCallProperties
        {
            AdditionalHeaders = new Dictionary<string, string> { ["x-api-key"] = StoredKeyPlaceholders.Token(KEY_NAME) },
        });
        block.SetExecutionState(true, []);

        IRenderedComponent<WebApiCallBlock> editor = RenderBlock(block);

        editor.WaitForAssertion(() => Assert.Contains("Header Value After Execution", editor.Markup));
        Assert.Contains(StoredKeyPlaceholders.Token(KEY_NAME), editor.Markup);
        Assert.DoesNotContain(KEY_VALUE, editor.Markup);
    }

    private async Task<IRenderedComponent<Administration>> OpenStoredKeysTabAsync()
    {
        IRenderedComponent<Administration> page = RenderInApp<Administration>();
        page.WaitForAssertion(() => Assert.NotNull(Tab(page, STORED_KEYS_TAB)));
        await page.InvokeAsync(() => Tab(page, STORED_KEYS_TAB)!.Click());
        page.WaitForAssertion(() => Assert.True(Shows(page, "Add Key")));
        return page;
    }

    private IRenderedComponent<WebApiCallBlock> RenderBlock(Block block)
    {
        return RenderInApp<WebApiCallBlock>(parameters => parameters
            .Add(component => component.Block, block)
            .Add(component => component.BlockChanged, () => { }));
    }

    private static Block WebApiCallBlock(WebApiCallProperties properties)
    {
        properties.UrlTemplate = "https://api.example.test/tasks";
        return new Block(BlockTypes.WebApiCall, new Section("test", new Workflow("test")))
        {
            Name = "Call",
            Properties = properties,
        };
    }

    private async Task StoreKeyAsync(string name, string value)
    {
        using IServiceScope scope = Services.CreateScope();
        ErrorOr<Success> result = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new SetStoredKeyCommand(ORGANIZATION_ID, "seed", name, value), Xunit.TestContext.Current.CancellationToken);
        Assert.False(result.IsError);
    }

    private async Task<string?> ResolveAsync(string name)
    {
        using IServiceScope scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStoredKeyResolver>()
            .ResolveAsync(ORGANIZATION_ID, name, Xunit.TestContext.Current.CancellationToken);
    }

    private static IElement? Tab(IRenderedComponent<Administration> page, string text)
    {
        return page.FindAll(".mud-tab").FirstOrDefault(tab => tab.TextContent.Trim() == text);
    }

    private static IElement? DialogField(IRenderedComponent<MudDialogProvider> dialogs, string label)
    {
        return dialogs.FindAll(".mud-input-control")
            .FirstOrDefault(control => control.QuerySelector("label")?.TextContent.Trim().TrimEnd('*') == label)
            ?.QuerySelector("input");
    }
}
