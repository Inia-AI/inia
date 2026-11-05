using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows.Blocks;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Runtime.Loader;

namespace IniaAi.Core.Workflows.BlockExtensions;

public static class BlockTypeServiceCollectionExtensions
{
    private static int _bindsToLoadedAssemblies;

    /// <summary>
    /// Adds a block type from outside the platform: its definition joins the
    /// <see cref="BlockTypeCatalog"/>, and the workflow engine runs its blocks with
    /// <paramref name="strategy"/>.
    /// </summary>
    public static IServiceCollection AddBlockType(
        this IServiceCollection services,
        BlockTypeDefinition definition,
        IBlockStrategy strategy)
    {
        BlockTypeCatalog.Register(definition);

        return services.AddSingleton(new BlockStrategyRegistration(definition.Type, strategy));
    }

    /// <summary>
    /// Loads each assembly by path, relative to the application folder, and registers every block
    /// extension in it. An assembly listed twice registers once.
    /// </summary>
    public static IServiceCollection AddBlockExtensions(this IServiceCollection services, IEnumerable<string> assemblyPaths)
    {
        string[] paths = [.. assemblyPaths.Select(static path => Path.GetFullPath(path, AppContext.BaseDirectory)).Distinct(StringComparer.Ordinal)];
        if (paths.Length > 0 && Interlocked.Exchange(ref _bindsToLoadedAssemblies, 1) == 0)
        {
            AssemblyLoadContext.Default.Resolving += LoadedAssembly;
        }

        foreach (string path in paths)
        {
            Assembly assembly = Assembly.LoadFrom(path);
            IBlockExtension[] extensions = [.. assembly.GetExportedTypes()
                .Where(static type => type is { IsClass: true, IsAbstract: false } && typeof(IBlockExtension).IsAssignableFrom(type))
                .Select(static type => (IBlockExtension)Activator.CreateInstance(type)!)];

            if (extensions.Length == 0)
            {
                throw new InvalidOperationException(
                    $"The block extension assembly {path} holds no public class that implements {nameof(IBlockExtension)}, so it adds no block type. Remove it from PlatformSettings:BlockExtensions or add the class.");
            }

            foreach (IBlockExtension extension in extensions)
            {
                extension.Register(services);
            }
        }

        return services;
    }

    /// <summary>
    /// The assembly of the host that an extension's reference names, whatever its version. An
    /// extension is built against the platform the host runs, but its references carry the version
    /// of its own build: an image built from source stamps its assemblies 0.0.0, below a local
    /// build's 1.0.0. Without this, the runtime would load the copies beside the extension, and
    /// every platform type would exist twice.
    /// </summary>
    internal static Assembly? LoadedAssembly(AssemblyLoadContext context, AssemblyName reference)
    {
        string prefix = $"{reference.Name},";
        return context.Assemblies.FirstOrDefault(assembly =>
            assembly.FullName?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true);
    }
}
