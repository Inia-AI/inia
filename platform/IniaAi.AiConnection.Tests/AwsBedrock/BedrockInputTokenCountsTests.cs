using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.Common.Extensions;
using IniaAi.Configuration.Enums.Cohere;
using IniaAi.Testing;
using System.Net;
using System.Text;

namespace IniaAi.AiConnection.Tests.AwsBedrock;

/// <summary>
/// Bedrock answers an InvokeModel call with its input token count in a response header. A local
/// server stands in for Bedrock, so the SDK's own pipeline reads the header.
/// </summary>
public sealed class BedrockInputTokenCountsTests
{
    private const string TOKEN_COUNT_HEADER = "X-Amzn-Bedrock-Input-Token-Count";

    [Theory]
    [InlineData(TOKEN_COUNT_HEADER)]
    [InlineData("x-amzn-bedrock-input-token-count")]
    public async Task AnAnswer_CarriesTheTokenCountOfItsHeaderAsync(string headerName)
    {
        (BedrockInputTokenCounts counts, List<InvokeModelResponse> responses) = await InvokeAsync(headerName, "7");

        Assert.Equal(7, counts.Of(Assert.Single(responses)));
    }

    [Fact]
    public async Task AnAnswerWithoutTheHeader_CountsNothingAsync()
    {
        (BedrockInputTokenCounts counts, List<InvokeModelResponse> responses) = await InvokeAsync(headerName: null, "7");

        Assert.Equal(0, counts.Of(Assert.Single(responses)));
    }

    [Fact]
    public async Task EachAnswer_KeepsItsOwnCountAsync()
    {
        (BedrockInputTokenCounts counts, List<InvokeModelResponse> responses) = await InvokeAsync(TOKEN_COUNT_HEADER, "3", "11");

        Assert.Equal([3, 11], responses.Select(counts.Of));
    }

    private static async Task<(BedrockInputTokenCounts Counts, List<InvokeModelResponse> Responses)> InvokeAsync(
        string? headerName,
        params string[] tokenCounts)
    {
        var counts = new BedrockInputTokenCounts();
        (HttpListener listener, string baseUrl) = LocalTestHttpServer.StartOnFreeLoopbackPort();
        using (listener)
        {
            Task answers = AnswerInTurnAsync(listener, headerName, tokenCounts);
            AmazonBedrockRuntimeClient client = counts.Track(new AmazonBedrockRuntimeClient(
                new BasicAWSCredentials("test", "test"),
                new AmazonBedrockRuntimeConfig { ServiceURL = baseUrl, AuthenticationRegion = "eu-west-2", MaxErrorRetry = 0 }));

            List<InvokeModelResponse> responses = [];
            foreach (string _ in tokenCounts)
            {
                responses.Add(await client.InvokeModelAsync(
                    new InvokeModelRequest
                    {
                        ModelId = AiModel.EuAwsEmbedV40.GetEnumMember(),
                        Body = new MemoryStream(Encoding.UTF8.GetBytes("{}")),
                        ContentType = "application/json",
                        Accept = "application/json",
                    },
                    TestContext.Current.CancellationToken));
            }

            await answers;

            return (counts, responses);
        }
    }

    private static async Task AnswerInTurnAsync(HttpListener listener, string? headerName, string[] tokenCounts)
    {
        byte[] body = Encoding.UTF8.GetBytes("{}");
        foreach (string tokenCount in tokenCounts)
        {
            HttpListenerContext context = await listener.GetContextAsync();
            if (headerName is not null)
            {
                context.Response.Headers[headerName] = tokenCount;
            }

            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body);
            context.Response.Close();
        }
    }
}
