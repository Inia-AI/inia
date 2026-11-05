using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Domain.Tests.Data.WorkflowExecutions;

public class WorkflowExecutionLogTests
{
    [Fact]
    public void Error_RemovesNullCharacters()
    {
        var log = new WorkflowExecutionLog
        {
            Error = "failed on: ab" + '\0' + "cd",
        };

        Assert.Equal("failed on: abcd", log.Error);
    }

    [Fact]
    public void Error_NullAndCleanValues_PassThroughUnchanged()
    {
        var log = new WorkflowExecutionLog();

        Assert.Null(log.Error);

        log.Error = "clean error";
        Assert.Equal("clean error", log.Error);
    }
}
