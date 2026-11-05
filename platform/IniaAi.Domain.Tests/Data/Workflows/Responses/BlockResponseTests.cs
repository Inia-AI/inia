using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;

namespace IniaAi.Domain.Tests.Data.Workflows.Responses;

public class BlockResponseTests
{
    [Fact]
    public void RawResponse_RemovesNullCharacters()
    {
        BlockResponse response = CreateResponse();

        response.RawResponse = "scraped" + '\0' + " text" + '\0';

        Assert.Equal("scraped text", response.RawResponse);
    }

    [Fact]
    public void JsonResponse_RemovesNullCharacters()
    {
        BlockResponse response = CreateResponse();

        response.JsonResponse = "{\"value\":\"a" + '\0' + "b\"}";

        Assert.Equal("{\"value\":\"ab\"}", response.JsonResponse);
    }

    [Fact]
    public void RawResponse_NullAndCleanValues_PassThroughUnchanged()
    {
        BlockResponse response = CreateResponse();

        response.RawResponse = null;
        Assert.Null(response.RawResponse);

        response.RawResponse = "clean text";
        Assert.Equal("clean text", response.RawResponse);
    }

    private static BlockResponse CreateResponse()
    {
        var section = new Section("test", new Workflow("test"));
        var block = new Block(BlockTypes.WebApiCall, section);
        return new BlockResponse(block);
    }
}
