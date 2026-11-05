using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Workflows.BlockExtensions;

/// <summary>
/// The entry point of an assembly that adds block types. A host loads each assembly that
/// <c>PlatformSettings:BlockExtensions</c> lists and calls <see cref="Register"/> on every public
/// implementation in it, which adds the types with <see cref="BlockTypeServiceCollectionExtensions.AddBlockType"/>.
/// </summary>
public interface IBlockExtension
{
    void Register(IServiceCollection services);
}
