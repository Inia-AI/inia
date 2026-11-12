using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;

namespace IniaAi.Infrastructure.Tests.Services;

public class AgentRunOutputFilesTests
{
    private static AgentRunRecarriedFile OutFile(string outRelativePath, string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        return new AgentRunRecarriedFile(outRelativePath, bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    [Fact]
    public void JsonAnswer_IsEmbeddedAsJson_AndFileBytesPassThroughVerbatim()
    {
        const string ROWS = """[{"amount":1250.10,"memo":"Café rent"},{"amount":-3.50e2,"memo":"fee"}]""";

        ErrorOr<string> composed = AgentRunOutputFiles.Compose(
            """{"status":"ok"}""",
            ["out/sub_1_rows.json"],
            [OutFile("sub_1_rows.json", ROWS)]);

        Assert.False(composed.IsError);
        Assert.Equal("{\"answer\":{\"status\":\"ok\"},\"files\":{\"sub_1_rows\":" + ROWS + "}}", composed.Value);
    }

    [Fact]
    public void TextAnswer_IsEmbeddedAsAJsonString()
    {
        ErrorOr<string> composed = AgentRunOutputFiles.Compose(
            "Done: 3 rows, see \"numbers\".",
            ["out/numbers.json"],
            [OutFile("numbers.json", "[1,2,3]")]);

        Assert.False(composed.IsError);
        using JsonDocument parsed = JsonDocument.Parse(composed.Value);
        Assert.Equal("Done: 3 rows, see \"numbers\".", parsed.RootElement.GetProperty("answer").GetString());
        Assert.Equal(3, parsed.RootElement.GetProperty("files").GetProperty("numbers").GetArrayLength());
    }

    [Fact]
    public void MultipleFiles_InNestedFolders_KeyedByStemInDeclaredOrder_OthersIgnored()
    {
        ErrorOr<string> composed = AgentRunOutputFiles.Compose(
            "{}",
            ["out/checks/totals.json", "out/rows.json"],
            [
                OutFile("rows.json", "{\"n\":2}"),
                OutFile("checks/totals.json", "{\"sum\":7}"),
                OutFile("scratch.json", "{\"ignored\":true}"),
            ]);

        Assert.False(composed.IsError);
        Assert.Equal("{\"answer\":{},\"files\":{\"totals\":{\"sum\":7},\"rows\":{\"n\":2}}}", composed.Value);
    }

    [Fact]
    public void FileWithUtf8Bom_IsAccepted_WithoutTheBom()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("{\"a\":1}")];
        var file = new AgentRunRecarriedFile("a.json", bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)));

        ErrorOr<string> composed = AgentRunOutputFiles.Compose("x", ["out/a.json"], [file]);

        Assert.False(composed.IsError);
        Assert.Equal("{\"answer\":\"x\",\"files\":{\"a\":{\"a\":1}}}", composed.Value);
    }

    [Fact]
    public void MissingFile_FailsNamingTheFile()
    {
        ErrorOr<string> composed = AgentRunOutputFiles.Compose(
            "ok", ["out/numbers.json"], [OutFile("other.json", "{}")]);

        Assert.True(composed.IsError);
        Assert.Equal(AgentRunOutputFiles.ERROR_FILE_MISSING, composed.FirstError.Code);
        Assert.Contains("out/numbers.json", composed.FirstError.Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"a\":1")]
    [InlineData("")]
    [InlineData("{\"a\":1} trailing")]
    [InlineData("1,2")]
    public void InvalidJsonFile_FailsNamingTheFile(string content)
    {
        ErrorOr<string> composed = AgentRunOutputFiles.Compose(
            "ok", ["out/numbers.json"], [OutFile("numbers.json", content)]);

        Assert.True(composed.IsError);
        Assert.Equal(AgentRunOutputFiles.ERROR_FILE_NOT_JSON, composed.FirstError.Code);
        Assert.Contains("out/numbers.json", composed.FirstError.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void FileNestedPastWhatTheComposedOutputCanCarry_IsNotJson()
    {
        string deep = new string('[', 63) + new string(']', 63);

        ErrorOr<string> composed = AgentRunOutputFiles.Compose(
            "ok", ["out/deep.json"], [OutFile("deep.json", deep)]);

        Assert.True(composed.IsError);
        Assert.Equal(AgentRunOutputFiles.ERROR_FILE_NOT_JSON, composed.FirstError.Code);
    }

    [Fact]
    public void OverCap_Fails()
    {
        string big = "[" + string.Join(",", Enumerable.Repeat("123456789", 200)) + "]";

        ErrorOr<string> composed = AgentRunOutputFiles.Compose(
            "ok", ["out/big.json"], [OutFile("big.json", big)], maxBytes: 1024);

        Assert.True(composed.IsError);
        Assert.Equal(AgentRunOutputFiles.ERROR_OVER_CAP, composed.FirstError.Code);
    }

    [Fact]
    public void JustUnderCap_Composes()
    {
        ErrorOr<string> composed = AgentRunOutputFiles.Compose(
            "ok", ["out/a.json"], [OutFile("a.json", "[1]")], maxBytes: 64);

        Assert.False(composed.IsError);
        Assert.True(Encoding.UTF8.GetByteCount(composed.Value) <= 64);
    }

    [Theory]
    [InlineData("out/report.json\n")]
    [InlineData("out/../report.json")]
    [InlineData("/out/report.json")]
    public void APathThatIsNotAnOrdinaryJsonFileUnderOut_IsAPathProblem(string path)
    {
        Assert.NotNull(AgentRunOutputFiles.DescribeFirstPathProblem([path]));
    }

    [Fact]
    public void StemCollision_IsAPathProblem()
    {
        string? problem = AgentRunOutputFiles.DescribeFirstPathProblem(["out/a/rows.json", "out/rows.json"]);

        Assert.NotNull(problem);
        Assert.Contains("'rows'", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskInstruction_NamesEveryFile()
    {
        string instruction = AgentRunOutputFiles.TaskInstruction(["out/a.json", "out/b/c.json"]);

        Assert.Contains("out/a.json, out/b/c.json", instruction, StringComparison.Ordinal);
    }
}
