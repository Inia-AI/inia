using AngleSharp.Dom;
using Bunit;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.WebApp.Components.WorkflowEditor.Components;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

public class WorkflowVariablesPanelTests : SignedInRenderTest
{
    private const string VARIABLE_TAG = "wfvar";

    [Fact]
    public async Task TheWorkflowVariablesHeader_IsADisclosureButtonThatSaysWhetherItIsOpen()
    {
        _ = await SignInAsync("editor@inia.ai", Roles.USER, Roles.WORKFLOW_EDITOR);
        var workflow = new Workflow("root");
        _ = new WorkflowVariable(workflow, VARIABLE_TAG);

        IRenderedComponent<WorkflowVariablesManager> manager = RenderInApp<WorkflowVariablesManager>(p => p.Add(c => c.Workflow, workflow));

        IElement header = Header(manager);
        Assert.Equal("false", header.GetAttribute("aria-expanded"));
        string controls = header.GetAttribute("aria-controls")!;
        Assert.NotNull(manager.Find($"#{controls}"));

        header.Click();

        Assert.Equal("true", Header(manager).GetAttribute("aria-expanded"));
        Assert.Contains(VARIABLE_TAG, manager.Find($"#{controls}").TextContent, StringComparison.Ordinal);

        Header(manager).Click();

        Assert.Equal("false", Header(manager).GetAttribute("aria-expanded"));
    }

    private static IElement Header(IRenderedComponent<WorkflowVariablesManager> manager)
    {
        return manager.FindAll("button[aria-expanded]").Single(button => button.TextContent.Contains("Workflow Variables", StringComparison.Ordinal));
    }
}
