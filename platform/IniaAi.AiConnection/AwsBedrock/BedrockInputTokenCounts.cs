using Amazon.BedrockRuntime;
using Amazon.Runtime;
using System.Runtime.CompilerServices;

namespace IniaAi.AiConnection.AwsBedrock;

/// <summary>
/// The input token count of each InvokeModel answer. Bedrock sends it only in a response header,
/// which the SDK does not map onto the response, and a Cohere embeddings body carries no usage.
/// </summary>
internal sealed class BedrockInputTokenCounts
{
    private const string INPUT_TOKEN_COUNT_HEADER = "X-Amzn-Bedrock-Input-Token-Count";

    private readonly ConditionalWeakTable<AmazonWebServiceResponse, StrongBox<int>> _counts = [];

    public AmazonBedrockRuntimeClient Track(AmazonBedrockRuntimeClient client)
    {
        client.AfterResponseEvent += Record;

        return client;
    }

    public int Of(AmazonWebServiceResponse response)
    {
        return _counts.TryGetValue(response, out StrongBox<int>? count) ? count.Value : 0;
    }

    private void Record(object sender, ResponseEventArgs e)
    {
        if (e is not WebServiceResponseEventArgs { Response: AmazonWebServiceResponse response } args)
        {
            return;
        }

        foreach (KeyValuePair<string, string> header in args.ResponseHeaders)
        {
            if (string.Equals(header.Key, INPUT_TOKEN_COUNT_HEADER, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(header.Value, out int count))
            {
                _counts.AddOrUpdate(response, new StrongBox<int>(count));
            }
        }
    }
}
