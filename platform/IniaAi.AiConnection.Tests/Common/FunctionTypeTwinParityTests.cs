using System.Reflection;
using IniaAi.AiConnection.Common;
using AiFunctionType = IniaAi.AiConnection.Common.FunctionType;
using ConfigFunctionType = IniaAi.Configuration.Enums.FunctionType;

namespace IniaAi.AiConnection.Tests.Common;

/// <summary>
/// <see cref="AiFunctionType"/> and <see cref="ConfigFunctionType"/> are deliberate twins
/// (the Configuration copy avoids a circular dependency) with no explicit member values,
/// so an insertion or a one-sided append silently shifts ordinals and reroutes model
/// families. These guards pin the twins member-for-member and pin the resolver's
/// name-preserving mapping between them.
/// </summary>
public class FunctionTypeTwinParityTests
{
    [Fact]
    public void TwinEnums_DeclareIdenticalNamesAndValues()
    {
        Dictionary<string, int> aiMembers = Enum.GetValues<AiFunctionType>()
            .ToDictionary(m => m.ToString(), m => (int)m);
        Dictionary<string, int> configMembers = Enum.GetValues<ConfigFunctionType>()
            .ToDictionary(m => m.ToString(), m => (int)m);

        Assert.Equal(aiMembers, configMembers);
    }

    [Fact]
    public void Validation_IsDeclaredInBothTwins()
    {
        Assert.True(Enum.IsDefined(AiFunctionType.Validation));
        Assert.True(Enum.IsDefined(ConfigFunctionType.Validation));
    }

    [Fact]
    public void ConvertToConfigFunctionType_MapsEveryMemberToItsNamesake()
    {
        MethodInfo convert = typeof(TenantModelFamilyResolver).GetMethod(
            "ConvertToConfigFunctionType",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "TenantModelFamilyResolver.ConvertToConfigFunctionType was not found — update this guard alongside the resolver.");

        foreach (AiFunctionType member in Enum.GetValues<AiFunctionType>())
        {
            var mapped = (ConfigFunctionType)convert.Invoke(null, [member])!;

            Assert.Equal(member.ToString(), mapped.ToString());
        }
    }
}
