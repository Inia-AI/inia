using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows;

/// <summary>
/// A stored block keeps its type's number, and its stored settings name the type. A renumbered
/// member turns every stored block of its type into another type; a renamed or removed one makes
/// their settings unavailable. The open build keeps the members of the closed modules' types too. A
/// new member goes at the end with a number of its own, and into this list.
/// </summary>
public class BlockTypeValuesTests
{
    private static readonly Dictionary<string, int> _stored = new()
    {
        ["AiQuery"] = 0,
        ["Merge"] = 3,
        ["CalculateNumber"] = 6,
        ["SimpleRag"] = 9,
        ["LoopStart"] = 10,
        ["LoopEnd"] = 11,
        ["BuildTopicsForAllDocuments"] = 15,
        ["IntelligentSearch"] = 19,
        ["ExchangeRate"] = 20,
        ["WebApiCall"] = 21,
        ["DynamicChart"] = 23,
        ["Json2Xlsx"] = 24,
        ["SwitchCase"] = 25,
        ["MergeJson"] = 26,
        ["StringManipulation"] = 27,
        ["Agent"] = 28,
        ["PeerFinancials"] = 29,
        ["Verification"] = 30,
    };

    [Fact]
    public void EveryBlockType_KeepsItsStoredNameAndNumber()
    {
        Assert.Equal(_stored, Enum.GetValues<BlockTypes>().ToDictionary(static type => type.ToString(), static type => (int)type));
    }
}
