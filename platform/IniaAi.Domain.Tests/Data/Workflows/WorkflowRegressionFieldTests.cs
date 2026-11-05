using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class WorkflowRegressionFieldTests
{
    [Fact]
    public void Workflow_CloneTo_CopiesRegressionFieldsAsNewInstances()
    {
        var source = new Workflow("source");
        var aum = new WorkflowRegressionField(source, "aum")
        {
            Type = RegressionFieldTypes.NUMERIC,
            Tolerance = 2.5m,
        };
        _ = new WorkflowRegressionField(source, "manager")
        {
            Type = RegressionFieldTypes.TEXT,
            Tolerance = 0,
        };

        var destination = new Workflow();
        source.CloneTo(destination);

        Assert.Equal(2, destination.RegressionFields.Count);

        WorkflowRegressionField aumClone = destination.RegressionFields.Single(f => f.Name == "aum");
        Assert.NotSame(aum, aumClone);
        Assert.NotEqual(aum.Id, aumClone.Id);
        Assert.Equal(RegressionFieldTypes.NUMERIC, aumClone.Type);
        Assert.Equal(2.5m, aumClone.Tolerance);
        Assert.Same(destination, aumClone.Workflow);
        Assert.Equal(destination.Id, aumClone.WorkflowId);

        WorkflowRegressionField managerClone = destination.RegressionFields.Single(f => f.Name == "manager");
        Assert.Equal(RegressionFieldTypes.TEXT, managerClone.Type);
        Assert.Equal(0m, managerClone.Tolerance);
    }

    [Fact]
    public void Workflow_CloneTo_ReplacesStaleRegressionFieldsInsteadOfAccumulating()
    {
        var source = new Workflow("source");
        _ = new WorkflowRegressionField(source, "fresh");

        var destination = new Workflow("destination");
        _ = new WorkflowRegressionField(destination, "stale");

        source.CloneTo(destination);

        WorkflowRegressionField field = Assert.Single(destination.RegressionFields);
        Assert.Equal("fresh", field.Name);
    }

    [Fact]
    public void ExportJson_RoundTripsRegressionFields()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        _ = new WorkflowRegressionField(workflow, "aum")
        {
            Type = RegressionFieldTypes.NUMERIC,
            Tolerance = 0.5m,
        };
        _ = new WorkflowRegressionField(workflow, "manager")
        {
            Type = RegressionFieldTypes.TEXT,
            Tolerance = 0,
        };

        Workflow restored = Workflow.FromJson(workflow.ToExportJson())!;

        Assert.Equal(2, restored.RegressionFields.Count);

        WorkflowRegressionField aum = restored.RegressionFields.Single(f => f.Name == "aum");
        Assert.Equal(RegressionFieldTypes.NUMERIC, aum.Type);
        Assert.Equal(0.5m, aum.Tolerance);
        Assert.Same(restored, aum.Workflow);
        Assert.Equal(restored.Id, aum.WorkflowId);

        WorkflowRegressionField manager = restored.RegressionFields.Single(f => f.Name == "manager");
        Assert.Equal(RegressionFieldTypes.TEXT, manager.Type);
        Assert.Equal(0m, manager.Tolerance);
    }
}
