using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Samples.WordCount;
using System.Runtime.CompilerServices;

namespace IniaAi.Core.Tests.Workflows.BlockExtensions;

/// <summary>
/// Registers the sample's block type as the test assembly loads, before any test or any theory
/// data reads the catalog. The catalog is process-wide, so every test sees the same types.
/// </summary>
internal static class SampleBlockType
{
    [ModuleInitializer]
    internal static void Register()
    {
        BlockTypeCatalog.Register(WordCountExtension.Definition);
    }
}
