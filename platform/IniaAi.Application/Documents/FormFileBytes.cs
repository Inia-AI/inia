using Microsoft.AspNetCore.Http;

namespace IniaAi.Application.Documents;

/// <summary>
/// Reads an upload into one array of its declared length, so the copy costs the file once and
/// nothing more. Call the gate's declared-size refusal first; this reads whatever it is given.
/// </summary>
public static class FormFileBytes
{
    public static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        byte[] bytes = new byte[file.Length];
        await using (Stream upload = file.OpenReadStream())
        {
            await upload.ReadExactlyAsync(bytes, cancellationToken);
        }

        return bytes;
    }
}
