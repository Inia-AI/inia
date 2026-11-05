using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Configuration;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Testing.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace IniaAi.Core.Tests.Workflows.Strategies;

/// <summary>An AI Query block that asks for citations, with a context around a substituted chat connector.</summary>
internal static class AiQueryChunkScenario
{
    public static (Block Block, AiQueryProperties Props, BlockExecutionContext Context) Build(IChatConnector connector)
    {
        var section = new Section("s", new Workflow("w"));
        var props = new AiQueryProperties
        {
            UserPromptTemplate = "Summarise the pages.",
            SystemPromptTemplate = "You are helpful.",
            ShouldUseAdvancedModel = false,
            ShouldGetCitationsFromModel = true,
            MaxTokens = 100,
        };
        props.ExecutionState ??= new(props);

        var block = new Block(BlockTypes.AiQuery, section)
        {
            Name = "Overview",
            Properties = props,
        };

        var context = new BlockExecutionContext(
            logger: NullLogger<BlockExecutionContext>.Instance,
            chatConnector: connector,
            ragService: null!,
            exchangeRateService: Substitute.For<IExchangeRateService>(),
            documentAnalysisService: null!,
            exaSearchService: null!,
            modelFamilyResolver: new TenantModelFamilyResolver(
                Options.Create(new PlatformSettings()),
                NullLogger<TenantModelFamilyResolver>.Instance),
            fileStorageManager: null!,
            pdfFormat: FileFormatsUnderTest.Pdf,
            tokenUsageService: Substitute.For<ITokenUsageService>(),
            platformSettings: new PlatformSettings(),
            services: null!);

        return (block, props, context);
    }
}
