using System.Runtime.Serialization;

namespace IniaAi.Common.Enums;

public enum NamedEntityType
{
    [EnumMember(Value = nameof(Person))]
    Person,

    [EnumMember(Value = nameof(Country))]
    Country,

    [EnumMember(Value = nameof(Url))]
    Url,

    [EnumMember(Value = nameof(Email))]
    Email,

    [EnumMember(Value = nameof(Organization))]
    Organization,

    [EnumMember(Value = nameof(Location))]
    Location,

    [EnumMember(Value = nameof(Fund))]
    Fund,

    [EnumMember(Value = nameof(All))]
    All,
}
