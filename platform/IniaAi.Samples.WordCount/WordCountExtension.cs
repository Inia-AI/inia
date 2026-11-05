using IniaAi.Core.Workflows.BlockExtensions;
using IniaAi.Domain.Entities.Workflows.Blocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IniaAi.Samples.WordCount;

/// <summary>Adds the Count Words block type. A host loads it when <c>PlatformSettings:BlockExtensions</c> names this assembly.</summary>
public sealed class WordCountExtension : IBlockExtension
{
    public static BlockTypeDefinition Definition { get; } =
        BlockTypeDefinition.For<WordCountProperties>(WordCountProperties.TYPE, "Count Words") with
        {
            ShortLabel = "Words",
            Hue = "#06B6D4",
            EditorComponent = typeof(WordCountEditor),
        };

    public void Register(IServiceCollection services)
    {
        services.TryAddScoped<IWordCounter, WordCounter>();

        _ = services.AddBlockType(Definition, new WordCountStrategy());
    }
}
