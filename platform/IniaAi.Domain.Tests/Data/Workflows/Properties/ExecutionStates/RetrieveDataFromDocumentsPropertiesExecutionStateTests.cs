using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties.ExecutionStates;

public class RetrieveDataFromDocumentsPropertiesExecutionStateTests
{
    [Fact]
    public void Constructor_FromSource_CreatesInstance()
    {
        // Arrange
        var props = new RetrieveDataFromDocumentsProperties();

        // Act
        var state = new RetrieveDataFromDocumentsPropertiesExecutionState(props);

        // Assert
        Assert.NotNull(state);
    }

    [Fact]
    public void SetExecutionState_OnProperties_CreatesExecutionState()
    {
        // Arrange
        var props = new RetrieveDataFromDocumentsProperties();

        // Act
        props.SetExecutionState(true, []);

        // Assert
        Assert.NotNull(props.ExecutionState);
    }

    [Fact]
    public void ClearExecutionState_WhenSet_ClearsIt()
    {
        // Arrange
        var props = new RetrieveDataFromDocumentsProperties();
        props.SetExecutionState(true, []);
        Assert.NotNull(props.ExecutionState);

        // Act
        props.ClearExecutionState();

        // Assert
        Assert.Null(props.ExecutionState);
    }
}
