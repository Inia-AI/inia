using System.Text.RegularExpressions;
using System.Web;

namespace IniaAi.FileFormats.Word;

/// <summary>The images that report content carries inline, as <c>&lt;img src="data:image/...;base64,..."&gt;</c>.</summary>
public static partial class DataImages
{
    /// <summary>Each inline image's bytes, in order. Base64 that a model or an editor mangled is repaired first.</summary>
    public static IEnumerable<byte[]> Decode(string content)
    {
        foreach (Match match in Base64ImageRegex().Matches(content))
        {
            string data = match.Groups["data"].Value;
            if (data.Length > 0)
            {
                yield return Convert.FromBase64String(Cleaned(data));
            }
        }
    }

    /// <summary>Removes whitespace, undoes URL encoding, maps URL-safe base64 to standard base64 and restores the padding.</summary>
    private static string Cleaned(string base64Data)
    {
        string cleaned = base64Data.Replace(" ", "")
                                   .Replace("\r", "")
                                   .Replace("\n", "")
                                   .Replace("\t", "");

        if (cleaned.Contains('%'))
        {
            cleaned = HttpUtility.UrlDecode(cleaned);
        }

        cleaned = cleaned.Replace('-', '+').Replace('_', '/');

        return (cleaned.Length % 4) switch
        {
            2 => cleaned + "==",
            3 => cleaned + "=",
            _ => cleaned,
        };
    }

    [GeneratedRegex(@"<img\b[^>]*?\bsrc\s*=\s*[""']data:image/(?<type>[^;]+);base64,(?<data>[^""']*)[""'][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Base64ImageRegex();
}
