using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Common;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

/// <summary>
/// Opening a workflow file replaces everything in the editor, so before it does, the editor says
/// what the file holds and what it replaces, whichever control the file came through.
/// </summary>
public class WorkflowFileImportPreviewTests
{
    [Fact]
    public void ThePreview_NamesTheFileTheUploadedWorkflowAndTheWorkflowItReplaces()
    {
        Workflow open = WorkflowWith("Credit memo", sections: 2, blocksPerSection: 3);
        Workflow uploaded = WorkflowWith("Credit memo (draft)", sections: 1, blocksPerSection: 1);

        string preview = WorkflowFileImportPreview.Describe(open, uploaded, "credit-memo.workflow");

        Assert.Equal(
            "The file 'credit-memo.workflow' holds the workflow 'Credit memo (draft)': 1 section, 1 block. "
            + "Opening it replaces everything in this editor, now 'Credit memo': 2 sections, 6 blocks. "
            + "Nothing is saved until you press Save, so cancelling leaves this workflow untouched.",
            preview);
    }

    [Fact]
    public void AWorkflowWithoutAName_IsDescribedInPlainWords()
    {
        string preview = WorkflowFileImportPreview.Describe(
            WorkflowWith("", sections: 0, blocksPerSection: 0),
            WorkflowWith(" ", sections: 1, blocksPerSection: 2),
            "draft.workflow");

        Assert.StartsWith(
            "The file 'draft.workflow' holds a workflow without a name: 1 section, 2 blocks. "
            + "Opening it replaces everything in this editor, now a workflow without a name: 0 sections, 0 blocks.",
            preview);
    }

    [Fact]
    public void TheTitle_SaysWhichControlTheFileCameThrough()
    {
        Assert.Equal("Replace the editor content?", WorkflowFileImportPreview.TitleFor(fromBundleControl: false));
        Assert.Equal("That is a workflow file, not a bundle", WorkflowFileImportPreview.TitleFor(fromBundleControl: true));
    }

    private static Workflow WorkflowWith(string name, int sections, int blocksPerSection)
    {
        var workflow = new Workflow(name);
        for (int s = 0; s < sections; s++)
        {
            var section = new Section($"Section {s}", workflow);
            for (int b = 0; b < blocksPerSection; b++)
            {
                _ = new Block(BlockTypes.Merge, section);
            }
        }

        return workflow;
    }
}
