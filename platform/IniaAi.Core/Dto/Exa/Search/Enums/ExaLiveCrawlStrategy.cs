using System.ComponentModel;
using System.Runtime.Serialization;

namespace IniaAi.Core.Dto.Exa.Search.Enums;

public enum ExaLiveCrawlStrategy
{
    [EnumMember(Value = "never")]
    [Description("Never live-crawl, only use cache")]
    Never,

    [EnumMember(Value = "fallback")]
    [Description("Use cache when available, live-crawl only for pages that aren't cached")]
    Fallback,

    [EnumMember(Value = "always")]
    [Description("Always live-crawl, ignore cache")]
    Always,

    [EnumMember(Value = "preferred")]
    [Description("Prefer live-crawl over cache when available")]
    Preferred
}
