using System.Text;
using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Enums;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Enums;
using NSubstitute;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class AiQueryCitationSourceTests
{
    private const string PAGE_TEXT = "Revenue was $5.0m in the year.";

    [Fact]
    public async Task BlockThatIsACitationSource_SendsCitablePagesAndKeepsTheirFactsAsync()
    {
        List<RequestContentSequence> requests = [];
        (Block block, AiQueryProperties props, BlockExecutionContext context) = AiQueryChunkScenario.Build(Capturing(requests));

        await AiQueryStrategy.ExecuteChunksSequentiallyAsync(
            block, props, context, OnePage(), new StringBuilder(), TestContext.Current.CancellationToken);

        Assert.Contains(Assert.Single(requests), content => content.IsInputContext);
        Assert.NotEmpty(context.BlockAnnotations.Facts);
    }

    [Fact]
    public async Task BlockThatReachesNoParagraph_AsksForNoCitationsAndKeepsNoFactsAsync()
    {
        List<RequestContentSequence> requests = [];
        (Block block, AiQueryProperties props, BlockExecutionContext context) = AiQueryChunkScenario.Build(Capturing(requests));
        context.IsCitationSource = false;

        await AiQueryStrategy.ExecuteChunksSequentiallyAsync(
            block, props, context, OnePage(), new StringBuilder(), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(Assert.Single(requests), content => content.IsInputContext);
        Assert.Empty(context.BlockAnnotations.Facts);
    }

    private static Dictionary<string, Dictionary<int, Page>> OnePage()
    {
        var document = new Document(new FileMetadata("org", "annual-report.pdf", 0, StorageProvider.Database), MediaType.ApplicationPdf);

        return new Dictionary<string, Dictionary<int, Page>>(StringComparer.Ordinal)
        {
            ["chunk1"] = new Dictionary<int, Page> { [1] = new Page(document, 1, 1) { RawText = PAGE_TEXT } },
        };
    }

    private static IChatConnector Capturing(List<RequestContentSequence> requests)
    {
        IChatConnector connector = Substitute.For<IChatConnector>();
        _ = connector.GetResponseAsync((RequestContentSequence)null!, null, 0, false, null, null, null, default)
            .ReturnsForAnyArgs(call =>
            {
                requests.Add(call.ArgAt<RequestContentSequence>(0));
                var response = new ResponseContentSequence();
                response.Add(new ResponseCommonContent("Revenue was $5.0m.")
                {
                    SourceReferences = [new SourceReference { Index = 0, CitedText = PAGE_TEXT }],
                });

                return Task.FromResult(response);
            });

        return connector;
    }
}
