using System.Runtime.Serialization;

namespace IniaAi.Domain.Enums;

/// <summary>
/// Represents the modes available for the Exchange Rate block.
/// </summary>
public enum ExchangeRateOperationMode
{
    [EnumMember(Value = "GetRate")]
    GetRate = 0,

    [EnumMember(Value = "Convert")]
    Convert = 1
}
