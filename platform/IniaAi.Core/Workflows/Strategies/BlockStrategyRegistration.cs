using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Workflows.Strategies;

/// <summary>
/// The strategy a closed module or an extension runs for one block type. Each registers one for
/// each block type it owns, and <see cref="BlockStrategies"/> adds it to the platform's own. A block
/// of a type with no strategy is unavailable: the run fails it with
/// <see cref="UnavailableBlockTypes.RunMessage"/>.
/// </summary>
internal sealed record BlockStrategyRegistration(BlockTypes BlockType, IBlockStrategy Strategy);
