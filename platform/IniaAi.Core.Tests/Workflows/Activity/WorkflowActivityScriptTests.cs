using IniaAi.Core.Workflows.Activity;

namespace IniaAi.Core.Tests.Workflows.Activity;

public class WorkflowActivityScriptTests
{
    private static WorkflowActivityScript ThreeChapterScript()
    {
        return new WorkflowActivityScript(
            [
                new WorkflowActivityGroup(0, 1, "Checking the loaded reports"),
                new WorkflowActivityGroup(2, 3, "Extracting the profit and loss lines"),
                new WorkflowActivityGroup(4, 5, null),
                new WorkflowActivityGroup(6, 7, "Assembling the report"),
            ],
            ["a1", "a2", "b1", "b2", "c1", "c2", "d1", "d2"]);
    }

    [Fact]
    public void BeforeAnyBlockCompletesTheFirstLabeledStepIsCurrent()
    {
        WorkflowActivityProgress progress = ThreeChapterScript().ResolveProgress([]);

        Assert.Equal(0, progress.CurrentStepIndex);
        Assert.Equal(
            ["Checking the loaded reports", "Extracting the profit and loss lines", "Assembling the report"],
            progress.StepLabels);
    }

    [Fact]
    public void ACompletedGroupAdvancesTheCurrentStep()
    {
        WorkflowActivityProgress progress = ThreeChapterScript().ResolveProgress(["a1", "a2"]);

        Assert.Equal(1, progress.CurrentStepIndex);
    }

    [Fact]
    public void AStragglerHoldsItsStepEvenWhenLaterBlocksComplete()
    {
        WorkflowActivityProgress progress = ThreeChapterScript().ResolveProgress(["a1", "b1", "b2"]);

        Assert.Equal(0, progress.CurrentStepIndex);
    }

    [Fact]
    public void AnUnlabeledGroupResolvesToThePrecedingLabeledStep()
    {
        WorkflowActivityProgress progress = ThreeChapterScript().ResolveProgress(
            ["a1", "a2", "b1", "b2"]);

        Assert.Equal(1, progress.CurrentStepIndex);
    }

    [Fact]
    public void WhenEveryBlockHasCompletedTheLastLabeledStepIsCurrent()
    {
        WorkflowActivityProgress progress = ThreeChapterScript().ResolveProgress(
            ["a1", "a2", "b1", "b2", "c1", "c2", "d1", "d2"]);

        Assert.Equal(2, progress.CurrentStepIndex);
    }

    [Fact]
    public void CompletionsTheScriptDoesNotKnowAreIgnored()
    {
        WorkflowActivityProgress progress = ThreeChapterScript().ResolveProgress(["zz"]);

        Assert.Equal(0, progress.CurrentStepIndex);
    }

    [Fact]
    public void AnExpandedVariantCompletionCountsForItsTemplateBlock()
    {
        WorkflowActivityProgress progress = ThreeChapterScript().ResolveProgress(
            ["a1_AR", "a2_PAR_QR"]);

        Assert.Equal(1, progress.CurrentStepIndex);
    }

    [Fact]
    public void ASharedIdPrefixAloneNeverMatches()
    {
        WorkflowActivityProgress progress = ThreeChapterScript().ResolveProgress(["a11", "a21"]);

        Assert.Equal(0, progress.CurrentStepIndex);
    }

    [Fact]
    public void ABlockWithoutAStableIdNeverGatesProgress()
    {
        var script = new WorkflowActivityScript(
            [
                new WorkflowActivityGroup(0, 1, "Reading the filings"),
                new WorkflowActivityGroup(2, 2, "Assembling the report"),
            ],
            ["a1", null, "b1"]);

        WorkflowActivityProgress progress = script.ResolveProgress(["a1"]);

        Assert.Equal(1, progress.CurrentStepIndex);
    }

    [Fact]
    public void ConcurrentlyStartedGroupsAreAllActive()
    {
        WorkflowActivityProgress progress = ThreeChapterScript().ResolveProgress(["a1", "b1"]);

        Assert.Equal(
            [WorkflowActivityStepState.Active, WorkflowActivityStepState.Active, WorkflowActivityStepState.Pending],
            progress.Steps.Select(step => step.State));
        Assert.Equal(0, progress.CurrentStepIndex);
    }

    [Fact]
    public void ACompletedGroupIsDoneWhileTheStartedOneIsActive()
    {
        WorkflowActivityProgress progress = ThreeChapterScript().ResolveProgress(["a1", "a2", "b1"]);

        Assert.Equal(
            [WorkflowActivityStepState.Done, WorkflowActivityStepState.Active, WorkflowActivityStepState.Pending],
            progress.Steps.Select(step => step.State));
        Assert.Equal(1, progress.CurrentStepIndex);
    }

    [Fact]
    public void WithNothingStartedTheEarliestUnfinishedStepIsActive()
    {
        WorkflowActivityProgress progress = ThreeChapterScript().ResolveProgress([]);

        Assert.Equal(
            [WorkflowActivityStepState.Active, WorkflowActivityStepState.Pending, WorkflowActivityStepState.Pending],
            progress.Steps.Select(step => step.State));
    }

    [Fact]
    public void AVariantCompletionStartsItsGroup()
    {
        WorkflowActivityProgress progress = ThreeChapterScript().ResolveProgress(["a1_AR"]);

        Assert.Equal(WorkflowActivityStepState.Active, progress.Steps[0].State);
        Assert.Equal(WorkflowActivityStepState.Pending, progress.Steps[1].State);
    }

    [Fact]
    public void AnUnlabeledPartialGroupReactivatesTheBorrowedStepWithoutOverclaimingDone()
    {
        WorkflowActivityProgress progress = ThreeChapterScript().ResolveProgress(
            ["a1", "a2", "b1", "b2", "c1"]);

        Assert.Equal(
            [WorkflowActivityStepState.Done, WorkflowActivityStepState.Active, WorkflowActivityStepState.Pending],
            progress.Steps.Select(step => step.State));
        Assert.Equal(1, progress.CurrentStepIndex);
    }

    [Fact]
    public void WhenEveryBlockHasCompletedEveryStepIsDone()
    {
        WorkflowActivityProgress progress = ThreeChapterScript().ResolveProgress(
            ["a1", "a2", "b1", "b2", "c1", "c2", "d1", "d2"]);

        Assert.Equal(
            [WorkflowActivityStepState.Done, WorkflowActivityStepState.Done, WorkflowActivityStepState.Done],
            progress.Steps.Select(step => step.State));
    }

    [Fact]
    public void ALeadingUnlabeledGroupResolvesForwardToTheFirstLabeledStep()
    {
        var script = new WorkflowActivityScript(
            [
                new WorkflowActivityGroup(0, 0, null),
                new WorkflowActivityGroup(1, 1, "Reading the filings"),
            ],
            ["a1", "a2"]);

        WorkflowActivityProgress progress = script.ResolveProgress([]);

        Assert.Equal(0, progress.CurrentStepIndex);
        Assert.Equal(["Reading the filings"], progress.StepLabels);
    }

    [Fact]
    public void SerializationRoundTripsAndRefusesForeignShapes()
    {
        WorkflowActivityScript script = ThreeChapterScript();

        string json = WorkflowActivityScriptSerializer.Serialize(script);
        WorkflowActivityScript? restored = WorkflowActivityScriptSerializer.TryDeserialize(json);

        Assert.NotNull(restored);
        Assert.Equal(script.Groups, restored.Groups);
        Assert.Equal(script.BlockIds, restored.BlockIds);
        Assert.Null(WorkflowActivityScriptSerializer.TryDeserialize("not json"));
        Assert.Null(WorkflowActivityScriptSerializer.TryDeserialize("{\"schemaVersion\":999}"));
        Assert.Null(WorkflowActivityScriptSerializer.TryDeserialize(null));
    }

    [Theory]
    [InlineData("""{"schemaVersion":1,"groups":[{"fromIndex":-1,"toIndex":0,"label":"x"}],"blockIds":["a"]}""")]
    [InlineData("""{"schemaVersion":1,"groups":[{"fromIndex":0,"toIndex":5,"label":"x"}],"blockIds":["a"]}""")]
    [InlineData("""{"schemaVersion":1,"groups":[{"fromIndex":1,"toIndex":0,"label":"x"}],"blockIds":["a","b"]}""")]
    [InlineData("""{"schemaVersion":1,"groups":[{"fromIndex":0,"toIndex":0,"label":"x"}],"blockIds":["a","b"]}""")]
    [InlineData("""{"schemaVersion":1,"groups":[{"fromIndex":0,"toIndex":0,"label":"x"},{"fromIndex":2,"toIndex":2,"label":"y"}],"blockIds":["a","b","c"]}""")]
    public void AStoredScriptWhoseGroupsDoNotPartitionItsBlocksIsRefused(string json)
    {
        Assert.Null(WorkflowActivityScriptSerializer.TryDeserialize(json));
    }

    [Fact]
    public void TheDeclineSentinelRoundTripsToZeroSteps()
    {
        WorkflowActivityScript? cached = WorkflowActivityScriptSerializer.TryDeserialize(
            WorkflowActivityScriptSerializer.SerializeDeclined());

        Assert.NotNull(cached);
        Assert.Empty(cached.ResolveProgress([]).StepLabels);
    }
}
