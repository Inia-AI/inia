using Bunit;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Components;
using IniaAi.WebApp.Services.Signatures;
using Microsoft.Extensions.DependencyInjection;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

public class SignatureFieldBlocksMenuTests : SignedInRenderTest
{
    private const string FIELD = "client_signature";
    private const int READERS = 4;

    public SignatureFieldBlocksMenuTests()
    {
        _ = Services.AddScoped<ISignatureOverrideReader, SignatureOverrideReader>();
    }

    [Fact]
    public async Task AClickOnTheBlocksChip_OpensTheListOfBlocksThatReadTheField()
    {
        _ = await SignInAsync("editor@inia.ai", Roles.USER, Roles.WORKFLOW_EDITOR);
        var workflow = new Workflow("root");
        workflow.SignatureFields.Add(new WorkflowSignatureField { Name = FIELD, DisplayName = "Client signature", Workflow = workflow });
        var section = new Section("Letters", workflow) { Title = "Letters" };
        for (int reader = 1; reader <= READERS; reader++)
        {
            _ = new Block(BlockTypes.AiQuery, section)
            {
                Name = $"Letter {reader}",
                Properties = new AiQueryProperties { UserPromptTemplate = $"Sign with {{{FIELD}}}." },
            };
        }

        IRenderedComponent<WorkflowSignatureFieldsManager> manager = RenderInApp<WorkflowSignatureFieldsManager>(p => p.Add(c => c.Workflow, workflow));

        manager.FindAll("button").Single(button => button.TextContent.Trim() == $"{READERS} blocks").Click();

        Popovers.WaitForAssertion(() => Assert.Equal(READERS, Popovers.FindAll(".mud-popover-open .mud-menu-item").Count));
    }
}
