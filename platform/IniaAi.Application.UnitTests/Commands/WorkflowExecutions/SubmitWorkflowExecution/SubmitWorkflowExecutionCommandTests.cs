using IniaAi.Application.Commands.WorkflowExecutions.SubmitWorkflowExecution;
using IniaAi.WebApi.Contracts.Requests.WorkflowExecutions;

namespace IniaAi.Application.UnitTests.Commands.WorkflowExecutions.SubmitWorkflowExecution;

public class SubmitWorkflowExecutionCommandTests
{
    private const string SECRET_PASSWORD = "secret-pw-do-not-log";

    [Fact]
    public void ToString_DoesNotLeakFilePasswords()
    {
        var metadata = new SubmitWorkflowExecutionRequest
        {
            WorkflowTypeId = "wf-type",
            Files = [new SubmitFileMetadata { DocumentClass = "Report", Password = SECRET_PASSWORD }],
        };

        var command = new SubmitWorkflowExecutionCommand("Org", "User", "name", "mail@example.com", metadata, []);

        Assert.DoesNotContain(SECRET_PASSWORD, command.ToString());
    }
}
