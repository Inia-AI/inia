using System.ComponentModel;
using System.Runtime.Serialization;

namespace IniaAi.Core.Dto.Exa.Search.Enums;

public enum ExaSearchType
{
    /// <summary>
    /// Exa's best search type, intelligently combines keyword and neural search.
    /// Automatically selects the best method based on the query.
    /// </summary>
    [EnumMember(Value = "auto")]
    [Description("Our best search, intelligently combines keyword and neural")]
    Auto,

    /// <summary>
    /// Streamlined keyword and neural search for faster results.
    /// Uses a simplified approach to quickly return relevant results.
    /// </summary>
    [EnumMember(Value = "fast")]
    [Description("Streamlined keyword and neural search for faster results")]
    Fast,

    /// <summary>
    /// Uses Google-like search to find results with matching keywords.
    /// This method focuses on exact keyword matches to return relevant content.
    /// </summary>
    [EnumMember(Value = "keyword")]
    [Description("Uses Google-like search to find results with matching keywords")]
    Keyword,

    /// <summary>
    /// Predicts the most relevant results based on query meaning.
    /// This method uses advanced neural networks to understand the intent
    /// behind the query and return results that are contextually relevant.
    /// </summary>
    [EnumMember(Value = "neural")]
    [Description("Predicts the most relevant results based on query meaning")]
    Neural
}
