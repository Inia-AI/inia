using System.Runtime.Serialization;

namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Specifies the operation mode for the ExchangeRate block.
/// </summary>
public enum ExchangeRateOperationMode
{
    /// <summary>
    /// Only retrieve the exchange rate without converting any values.
    /// </summary>
    [EnumMember(Value = "GetRate")]
    GetRate = 0,

    /// <summary>
    /// Retrieve the exchange rate and convert the input value(s).
    /// </summary>
    [EnumMember(Value = "Convert")]
    Convert = 1
}
