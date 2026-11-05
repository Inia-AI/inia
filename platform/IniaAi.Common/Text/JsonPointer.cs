namespace IniaAi.Common.Text;

/// <summary>JSON Pointer paths (RFC 6901).</summary>
public static class JsonPointer
{
    /// <summary>Appends one reference token to a path, escaping "~" before "/" as the RFC requires.</summary>
    public static string Append(string path, string token)
    {
        return $"{path}/{token.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)}";
    }
}
