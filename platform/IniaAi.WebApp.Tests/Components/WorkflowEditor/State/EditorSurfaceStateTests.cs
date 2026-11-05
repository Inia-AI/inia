using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.State;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.State;

public class EditorSurfaceStateTests
{
    private static Section NewSection(int blockCount = 2, string name = "section")
    {
        var workflow = new Workflow("wf") { Title = "wf" };
        return NewSectionIn(workflow, blockCount, name);
    }

    private static Section NewSectionIn(Workflow workflow, int blockCount, string name)
    {
        var section = new Section(name, workflow);
        for (int i = 0; i < blockCount; i++)
        {
            _ = new Block(BlockTypes.AiQuery, section, $"{name}_tag{i}");
        }

        return section;
    }

    private static EditorSurfaceState FullEditor() =>
        new(EditorSurfaceCapabilities.FullEditor);

    [Fact]
    public void FullEditor_EnablesEveryCapability()
    {
        EditorSurfaceCapabilities caps = EditorSurfaceCapabilities.FullEditor;

        Assert.True(caps.ShowCounterpartySettings);
        Assert.True(caps.CanExecute);
        Assert.True(caps.CanCopyToCore);
        Assert.True(caps.CanMoveAcrossSections);
        Assert.True(caps.ShowFailureSetting);
    }

    [Fact]
    public void Library_DisablesEveryCapability()
    {
        EditorSurfaceCapabilities caps = EditorSurfaceCapabilities.Library;

        Assert.False(caps.ShowCounterpartySettings);
        Assert.False(caps.CanExecute);
        Assert.False(caps.CanCopyToCore);
        Assert.False(caps.CanMoveAcrossSections);
        Assert.False(caps.ShowFailureSetting);
    }

    [Fact]
    public void Constructor_ExposesCapabilities()
    {
        var caps = EditorSurfaceCapabilities.FullEditor with { CanExecute = false };
        var state = new EditorSurfaceState(caps);

        Assert.Same(caps, state.Capabilities);
        Assert.False(state.Capabilities.CanExecute);
    }

    [Fact]
    public void AddBlock_InsertsAtIndex_WithAnyDocumentClass_AndReturnsBlock()
    {
        Section section = NewSection(blockCount: 2);
        EditorSurfaceState state = FullEditor();

        Block added = state.AddBlock(section, index: 1);

        Assert.Equal(3, section.Blocks.Count);
        Assert.Same(section, added.Section);
        Assert.Equal(1, added.Order);
        Assert.Equal(BlockTypes.AiQuery, added.Type);
        Assert.Contains(DocumentClass.KEY_ANY, added.SupportedDocumentClasses);
    }

    [Fact]
    public void AddBlock_RaisesSectionChangedForThatSection_ButNotDocumentClasses()
    {
        Section section = NewSection();
        EditorSurfaceState state = FullEditor();
        var changed = new List<Section>();
        bool docClassesRaised = false;
        state.SectionChanged += changed.Add;
        state.DocumentClassesChanged += () => docClassesRaised = true;

        _ = state.AddBlock(section, index: 0);

        Assert.Equal([section], changed);
        Assert.False(docClassesRaised);
    }

    [Fact]
    public void RemoveBlock_RemovesFromSection_AndRaisesSectionAndDocumentClassesChanged()
    {
        Section section = NewSection(blockCount: 3);
        Block target = section[1];
        EditorSurfaceState state = FullEditor();
        var changed = new List<Section>();
        bool docClassesRaised = false;
        state.SectionChanged += changed.Add;
        state.DocumentClassesChanged += () => docClassesRaised = true;

        state.RemoveBlock(target);

        Assert.Equal(2, section.Blocks.Count);
        Assert.DoesNotContain(target, section.Blocks);
        Assert.Equal([section], changed);
        Assert.True(docClassesRaised);
    }

    [Fact]
    public void RemoveBlock_SectionlessBlock_IsNoOp_AndRaisesNothing()
    {
        Section section = NewSection(blockCount: 1);
        Block orphan = section[0];
        orphan.Section = null;
        EditorSurfaceState state = FullEditor();
        bool anyEvent = false;
        state.SectionChanged += _ => anyEvent = true;
        state.DocumentClassesChanged += () => anyEvent = true;

        state.RemoveBlock(orphan);

        Assert.False(anyEvent);
    }

    [Fact]
    public void DuplicateBlock_AddsCopyToSection_AndRaisesSectionChangedOnly()
    {
        Section section = NewSection(blockCount: 2);
        Block source = section[0];
        EditorSurfaceState state = FullEditor();
        var changed = new List<Section>();
        bool docClassesRaised = false;
        state.SectionChanged += changed.Add;
        state.DocumentClassesChanged += () => docClassesRaised = true;

        Block? copy = state.DuplicateBlock(source);

        Assert.NotNull(copy);
        Assert.Equal(3, section.Blocks.Count);
        Assert.EndsWith("_copy", copy!.ReplacementTag);
        Assert.Equal([section], changed);
        Assert.False(docClassesRaised);
    }

    [Fact]
    public void DuplicateBlock_SectionlessBlock_ReturnsNull_AndRaisesNothing()
    {
        Section section = NewSection(blockCount: 1);
        Block orphan = section[0];
        orphan.Section = null;
        EditorSurfaceState state = FullEditor();
        bool anyEvent = false;
        state.SectionChanged += _ => anyEvent = true;

        Block? copy = state.DuplicateBlock(orphan);

        Assert.Null(copy);
        Assert.False(anyEvent);
    }

    [Fact]
    public void MoveBlock_SectionlessBlock_DoesNothing_AndRaisesNothing()
    {
        Section section = NewSection(blockCount: 1);
        Block orphan = section[0];
        orphan.Section = null;
        EditorSurfaceState state = FullEditor();
        bool anyEvent = false;
        state.SectionChanged += _ => anyEvent = true;

        state.MoveBlock(orphan, section, newIndex: 0);

        Assert.False(anyEvent);
    }

    [Fact]
    public void MoveBlock_SameSectionReorder_ChangesOrder_AndRaisesSectionChangedOnce()
    {
        Section section = NewSection(blockCount: 3);
        Block first = section[0];
        EditorSurfaceState state = FullEditor();
        var changed = new List<Section>();
        state.SectionChanged += changed.Add;

        state.MoveBlock(first, section, newIndex: 2);

        Assert.Equal(2, first.Order);
        Assert.Equal([section], changed);
    }

    [Fact]
    public void MoveBlock_CrossSection_RelocatesBlock_AndRaisesSectionChangedForBothSections()
    {
        var workflow = new Workflow("wf") { Title = "wf" };
        Section source = NewSectionIn(workflow, blockCount: 2, name: "source");
        Section destination = NewSectionIn(workflow, blockCount: 1, name: "destination");
        Block moving = source[0];
        EditorSurfaceState state = FullEditor();
        var changed = new List<Section>();
        state.SectionChanged += changed.Add;

        state.MoveBlock(moving, destination, newIndex: 0);

        Assert.Same(destination, moving.Section);
        Assert.DoesNotContain(moving, source.Blocks);
        Assert.Contains(moving, destination.Blocks);
        Assert.Equal(2, changed.Count);
        Assert.Contains(source, changed);
        Assert.Contains(destination, changed);
    }

    [Fact]
    public void MoveBlock_CrossSection_WhenNotAllowed_Throws_AndDoesNotMove()
    {
        var workflow = new Workflow("wf") { Title = "wf" };
        Section source = NewSectionIn(workflow, blockCount: 2, name: "source");
        Section destination = NewSectionIn(workflow, blockCount: 1, name: "destination");
        Block moving = source[0];
        var state = new EditorSurfaceState(
            EditorSurfaceCapabilities.FullEditor with { CanMoveAcrossSections = false });

        Assert.Throws<InvalidOperationException>(() => state.MoveBlock(moving, destination, newIndex: 0));
        Assert.Same(source, moving.Section);
        Assert.Contains(moving, source.Blocks);
    }

    [Fact]
    public void MoveBlock_SameSection_WhenCrossSectionNotAllowed_StillReorders()
    {
        Section section = NewSection(blockCount: 3);
        Block first = section[0];
        var state = new EditorSurfaceState(
            EditorSurfaceCapabilities.FullEditor with { CanMoveAcrossSections = false });

        state.MoveBlock(first, section, newIndex: 2);

        Assert.Equal(2, first.Order);
    }

    [Fact]
    public void ToggleSectionCitations_EnablesWhenAllOff_AndRaisesSectionChanged()
    {
        Section section = NewSection(blockCount: 2);
        EditorSurfaceState state = FullEditor();
        var changed = new List<Section>();
        state.SectionChanged += changed.Add;

        bool result = state.ToggleSectionCitations(section);

        Assert.True(result);
        Assert.All(section.Blocks, block =>
            Assert.True(((AiQueryProperties)block.Properties).ShouldGetCitationsFromModel));
        Assert.Equal([section], changed);
    }

    [Fact]
    public void ToggleSectionCitations_DisablesWhenAnyOn()
    {
        Section section = NewSection(blockCount: 2);
        ((AiQueryProperties)section[0].Properties).ShouldGetCitationsFromModel = true;
        EditorSurfaceState state = FullEditor();

        bool result = state.ToggleSectionCitations(section);

        Assert.False(result);
        Assert.All(section.Blocks, block =>
            Assert.False(((AiQueryProperties)block.Properties).ShouldGetCitationsFromModel));
    }

    [Fact]
    public void NotifyBlockChanged_RaisesBlockChangedOnly()
    {
        Section section = NewSection(blockCount: 1);
        Block block = section[0];
        EditorSurfaceState state = FullEditor();
        var blockChanged = new List<Block>();
        bool otherRaised = false;
        state.BlockChanged += blockChanged.Add;
        state.SectionChanged += _ => otherRaised = true;
        state.DocumentClassesChanged += () => otherRaised = true;

        state.NotifyBlockChanged(block);

        Assert.Equal([block], blockChanged);
        Assert.False(otherRaised);
    }

    [Fact]
    public void NotifySectionChanged_RaisesSectionChangedOnly()
    {
        Section section = NewSection(blockCount: 1);
        EditorSurfaceState state = FullEditor();
        var changed = new List<Section>();
        bool otherRaised = false;
        state.SectionChanged += changed.Add;
        state.BlockChanged += _ => otherRaised = true;
        state.DocumentClassesChanged += () => otherRaised = true;

        state.NotifySectionChanged(section);

        Assert.Equal([section], changed);
        Assert.False(otherRaised);
    }

    [Fact]
    public void NotifyDocumentClassesChanged_RaisesDocumentClassesChangedOnly()
    {
        EditorSurfaceState state = FullEditor();
        bool docClassesRaised = false;
        bool otherRaised = false;
        state.DocumentClassesChanged += () => docClassesRaised = true;
        state.SectionChanged += _ => otherRaised = true;
        state.BlockChanged += _ => otherRaised = true;

        state.NotifyDocumentClassesChanged();

        Assert.True(docClassesRaised);
        Assert.False(otherRaised);
    }
}
