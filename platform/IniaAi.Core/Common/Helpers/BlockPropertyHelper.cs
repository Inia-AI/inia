using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;

namespace IniaAi.Core.Common.Helpers;

/// <summary>
/// Provides helper methods for safely working with strongly-typed block property objects.
/// </summary>
public static class BlockPropertyHelper
{
    /// <summary>
    /// Gets the strongly-typed <typeparamref name="T"/> properties object from a <see cref="Block"/>.
    /// </summary>
    /// <typeparam name="T">The expected type of the block properties.</typeparam>
    /// <param name="block">The block instance containing the properties.</param>
    /// <returns>The properties object cast to type <typeparamref name="T"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the block is null.</exception>
    /// <exception cref="InvalidCastException">Thrown if the block's properties are not of type <typeparamref name="T"/>.</exception>
    public static T GetProperties<T>(this Block block)
        where T : BlockProperties
    {
        ArgumentNullException.ThrowIfNull(block);

        return block.Properties is T props ? props : throw new InvalidCastException($"Block.Properties is not of type {typeof(T).Name}.");
    }

    /// <summary>
    /// Determines whether the block's properties are of the specified type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type to check against.</typeparam>
    /// <param name="block">The block instance.</param>
    /// <returns><c>true</c> if the properties are of type <typeparamref name="T"/>; otherwise, <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the block is null.</exception>
    public static bool IsPropertiesType<T>(this Block block)
        where T : BlockProperties
    {
        ArgumentNullException.ThrowIfNull(block);
        return block.Properties is T;
    }

    /// <summary>
    /// Tries to cast the block's properties to the specified type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The desired target type of the properties.</typeparam>
    /// <param name="block">The block instance containing the properties.</param>
    /// <returns>The casted properties if successful; otherwise, <c>null</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the block is null.</exception>
    public static T? TryGetProperties<T>(this Block block)
        where T : BlockProperties
    {
        ArgumentNullException.ThrowIfNull(block);
        return block.Properties as T;
    }
}
