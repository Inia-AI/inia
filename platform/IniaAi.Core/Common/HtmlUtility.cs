namespace IniaAi.Core.Common;

public static class HtmlUtility
{
    public static string HtmlEncode(string input)
    {
        return System.Net.WebUtility.HtmlEncode(input);
    }
}
