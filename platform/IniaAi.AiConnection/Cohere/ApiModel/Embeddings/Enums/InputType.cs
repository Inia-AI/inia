using System.Runtime.Serialization;

namespace IniaAi.AiConnection.Cohere.ApiModel.Embeddings.Enums;

internal enum InputType
{
    [EnumMember(Value = "search_document")]
    SearchDocument,

    [EnumMember(Value = "search_query")]
    SearchQuery,

    [EnumMember(Value = "classification")]
    Classification,

    [EnumMember(Value = "clustering")]
    Clustering
}
