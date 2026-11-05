using IniaAi.Core.Workflows.BlockExtensions;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Samples.WordCount;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows.BlockExtensions;

/// <summary>
/// An extension's block type takes a number of its own from the extension range, and its settings
/// class carries that number. Registering the same type again, as a second host in the process
/// does, changes nothing. The test assembly registers the sample once, as it loads.
/// </summary>
public class BlockTypeCatalogRegistrationTests
{
    private static BlockTypeDefinition Sample(BlockTypes type)
    {
        return BlockTypeDefinition.For<WordCountProperties>(type, "Count Words");
    }

    [Fact]
    public void APlatformNumber_IsRefused()
    {
        _ = Assert.Throws<ArgumentException>(() => BlockTypeCatalog.Register(Sample(BlockTypes.Merge)));
    }

    [Fact]
    public void SettingsThatDoNotCarryTheirNumber_AreRefused()
    {
        _ = Assert.Throws<ArgumentException>(() => BlockTypeCatalog.Register(Sample((BlockTypes)1999)));
    }

    [Fact]
    public void AnEditorThatDoesNotTakeTheBlockAndItsCallback_IsRefused()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => BlockTypeCatalog.Register(Sample((BlockTypes)1999) with { EditorComponent = typeof(object) }));

        Assert.Contains("editor component", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAssemblyListedTwice_RegistersItsTypesOnce()
    {
        string sample = typeof(WordCountExtension).Assembly.Location;

        IServiceCollection services = new ServiceCollection().AddBlockExtensions([sample, sample]);

        _ = Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(BlockStrategyRegistration));
    }

    [Fact]
    public void TheSameTypeRegisteredAgain_ChangesNothing()
    {
        BlockTypeDefinition builtAgain = BlockTypeDefinition.For<WordCountProperties>(WordCountProperties.TYPE, "Count Words") with
        {
            ShortLabel = "Words",
            Hue = "#06B6D4",
            EditorComponent = typeof(WordCountEditor),
        };

        BlockTypeCatalog.Register(builtAgain);

        Assert.Same(WordCountExtension.Definition, BlockTypeCatalog.Find(WordCountProperties.TYPE));
    }

    [Fact]
    public void AnotherTypeOnATakenNumber_IsRefused()
    {
        _ = Assert.Throws<InvalidOperationException>(() => BlockTypeCatalog.Register(BlockTypeDefinition.For<WordCountProperties>(WordCountProperties.TYPE, "Count Letters")));
    }
}
