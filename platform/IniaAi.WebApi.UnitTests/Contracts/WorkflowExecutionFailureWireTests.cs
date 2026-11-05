using IniaAi.WebApi.Configuration;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using System.Text.Json;

namespace IniaAi.WebApi.UnitTests.Contracts;

/// <summary>
/// The failure pair on the report poll, on the wire, with the serializer settings the API runs:
/// a refused run writes both fields under their camelCase names, a run that did not fail writes
/// them as null, and a consumer written before the fields existed reads the response unchanged.
/// </summary>
public class WorkflowExecutionFailureWireTests
{
    private const string EXECUTION_ID = "exec-1";
    private const string KIND = "RejectedAtIntake";
    private const string DETAIL = "Missing document: Custody statement. The report cannot run without it, and none of the documents you sent is one.";

    [Fact]
    public void ARefusedRun_WritesItsKindAndDetail()
    {
        JsonElement json = JsonSerializer.SerializeToElement(RefusedRun(), ApiOptions());

        Assert.Equal(KIND, json.GetProperty("failureKind").GetString());
        Assert.Equal(DETAIL, json.GetProperty("failureDetail").GetString());
        Assert.Equal("Failed", json.GetProperty("workflowState").GetString());
    }

    [Fact]
    public void ARunThatDidNotFail_WritesBothFieldsAsNull()
    {
        JsonElement json = JsonSerializer.SerializeToElement(
            new WorkflowExecutionDto { Id = EXECUTION_ID, WorkflowState = WorkflowState.Draft },
            ApiOptions());

        Assert.Equal(JsonValueKind.Null, json.GetProperty("failureKind").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("failureDetail").ValueKind);
    }

    [Fact]
    public void AConsumerWrittenBeforeTheFields_ReadsTheResponseUnchanged()
    {
        string json = JsonSerializer.Serialize(RefusedRun(), ApiOptions());

        EarlierConsumerView view = JsonSerializer.Deserialize<EarlierConsumerView>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(EXECUTION_ID, view.Id);
        Assert.Equal("Failed", view.WorkflowState);
    }

    private static WorkflowExecutionDto RefusedRun()
    {
        return new WorkflowExecutionDto
        {
            Id = EXECUTION_ID,
            WorkflowState = WorkflowState.Failed,
            FailureKind = KIND,
            FailureDetail = DETAIL,
        };
    }

    private static JsonSerializerOptions ApiOptions()
    {
        var options = new JsonSerializerOptions();
        ControllerConfiguration.ConfigureJsonSerializer(options);
        return options;
    }

    public sealed record EarlierConsumerView(string Id, string WorkflowState);
}
