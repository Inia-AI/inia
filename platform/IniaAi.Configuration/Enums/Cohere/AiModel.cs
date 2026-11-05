using System.Runtime.Serialization;

namespace IniaAi.Configuration.Enums.Cohere;

public enum AiModel
{
    [EnumMember(Value = "embed-english-v3.0")]
    EmbedEnglishV30 = 0,

    [EnumMember(Value = "embed-multilingual-v3.0")]
    EmbedMultilingualV30 = 1,

    [EnumMember(Value = "embed-english-light-v3.0")]
    EmbedEnglishLightV30 = 2,

    [EnumMember(Value = "embed-multilingual-light-v3.0")]
    EmbedMultilingualLightV30 = 3,

    [EnumMember(Value = "embed-english-v2.0")]
    EmbedEnglishV20 = 4,

    [EnumMember(Value = "embed-english-light-v2.0")]
    EmbedEnglishLightV20 = 5,

    [EnumMember(Value = "embed-multilingual-v2.0")]
    EmbedMultilingualV20 = 6,

    [EnumMember(Value = "cohere.embed-english-v3")]
    AwsEmbedEnglishV30 = 7,

    [EnumMember(Value = "cohere.embed-multilingual-v3")]
    AwsEmbedMultilingualV30 = 8,

    [EnumMember(Value = "embed-v4.0")]
    EmbedV40 = 9,

    [EnumMember(Value = "global.cohere.embed-v4:0")]
    AwsEmbedV40 = 10,

    [EnumMember(Value = "us.cohere.embed-v4:0")]
    UsAwsEmbedV40 = 11,
}
