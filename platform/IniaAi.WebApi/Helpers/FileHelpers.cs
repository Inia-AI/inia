using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Reflection;

namespace IniaAi.WebApi.Helpers;

/// <summary>
/// Provides utility methods for processing and validating file uploads in the IniaAi Web API.
/// </summary>
public static class FileHelpers
{
    // Based on: https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/mvc/models/file-uploads/samples/3.x/SampleApp/Utilities/FileHelpers.cs
    // If you require a check on specific characters in the IsValidFileExtensionAndSignature
    // method, supply the characters in the _allowedChars field.
    private static readonly byte[] _allowedChars = [];

    // For more file signatures, see the File Signatures Database (https://www.filesignatures.net/)
    // and the official specifications for the file types you wish to add.
    private static readonly Dictionary<string, List<byte[]>> _fileSignature = new()
    {
        { ".pdf", new List<byte[]> { "%PDF-"u8.ToArray() } },
    };

    // **WARNING!**
    // In the following file processing methods, the file's content isn't scanned.
    // In most production scenarios, an anti-virus/anti-malware scanner API is
    // used on the file before making the file available to users or other
    // systems. For more information, see the topic that accompanies this sample
    // app.
    /// <summary>
    /// Processes a form file asynchronously, performing validation checks on file extension and signature.
    /// </summary>
    /// <typeparam name="T">The type of the model associated with the form file.</typeparam>
    /// <param name="formFile">The uploaded form file.</param>
    /// <param name="modelState">The model state dictionary to record validation errors.</param>
    /// <param name="permittedExtensions">An array of permitted file extensions.</param>
    /// <param name="sizeLimit">The maximum allowed file size in bytes.</param>
    /// <returns>A byte array containing the file data if valid; otherwise, an empty array.</returns>
    public static async Task<byte[]> ProcessFormFileAsync<T>(IFormFile formFile,
        ModelStateDictionary modelState, string[] permittedExtensions,
        long sizeLimit)
    {
        string fieldDisplayName = string.Empty;

        // Use reflection to obtain the display name for the model
        // property associated with this IFormFile. If a display
        // name isn't found, error messages simply won't show
        // a display name.
        MemberInfo? property =
            typeof(T).GetProperty(
                formFile.Name[(formFile.Name.IndexOf(".",
                StringComparison.Ordinal) + 1)..]);

        if (property is not null && property.GetCustomAttribute(typeof(DisplayAttribute)) is
                DisplayAttribute displayAttribute)
        {
            fieldDisplayName = $"{displayAttribute.Name} ";
        }

        // Don't trust the file name sent by the client. To display
        // the file name, HTML-encode the value.
        string trustedFileNameForDisplay = WebUtility.HtmlEncode(
            formFile.FileName);

        // Check the file length. This check doesn't catch files that only have
        // a BOM as their content.
        if (formFile.Length == 0)
        {
            modelState.AddModelError(formFile.Name,
                $"{fieldDisplayName}({trustedFileNameForDisplay}) is empty.");

            return [];
        }

        if (formFile.Length > sizeLimit)
        {
            long megabyteSizeLimit = sizeLimit / 1048576;
            modelState.AddModelError(formFile.Name,
                $"{fieldDisplayName}({trustedFileNameForDisplay}) exceeds " +
                $"{megabyteSizeLimit:N1} MB.");

            return [];
        }

        try
        {
            using var memoryStream = new MemoryStream();
            await formFile.CopyToAsync(memoryStream);

            // Check the content length in case the file's only
            // content was a BOM and the content is actually
            // empty after removing the BOM.
            if (memoryStream.Length == 0)
            {
                modelState.AddModelError(formFile.Name,
                    $"{fieldDisplayName}({trustedFileNameForDisplay}) is empty.");
            }

            if (!IsValidFileExtensionAndSignature(
                formFile.FileName, memoryStream, permittedExtensions))
            {
                modelState.AddModelError(formFile.Name,
                    $"{fieldDisplayName}({trustedFileNameForDisplay}) file " +
                    "type isn't permitted or the file's signature " +
                    "doesn't match the file's extension.");
            }
            else
            {
                return memoryStream.ToArray();
            }
        }
        catch (Exception ex)
        {
            modelState.AddModelError(formFile.Name,
                $"{fieldDisplayName}({trustedFileNameForDisplay}) upload failed. " +
                $"Please contact the Help Desk for support. Error: {ex.HResult}");
            // Log the exception
        }

        return [];
    }

    /// <summary>
    /// Processes a streamed file asynchronously, performing validation checks on file extension and signature.
    /// </summary>
    /// <param name="section">The multipart section containing the file data.</param>
    /// <param name="contentDisposition">The content disposition header value.</param>
    /// <param name="modelState">The model state dictionary to record validation errors.</param>
    /// <param name="permittedExtensions">An array of permitted file extensions.</param>
    /// <param name="sizeLimit">The maximum allowed file size in bytes.</param>
    /// <returns>A byte array containing the file data if valid; otherwise, an empty array.</returns>

    public static async Task<byte[]> ProcessStreamedFileAsync(
        MultipartSection section, ContentDispositionHeaderValue contentDisposition,
        ModelStateDictionary modelState, string[] permittedExtensions, long sizeLimit)
    {
        try
        {
            using var memoryStream = new MemoryStream();
            await section.Body.CopyToAsync(memoryStream);

            // Check if the file is empty or exceeds the size limit.
            if (memoryStream.Length == 0)
            {
                modelState.AddModelError("File", "The file is empty. Check that it is the right file, then upload it again.");
            }
            else if (memoryStream.Length > sizeLimit)
            {
                long megabyteSizeLimit = sizeLimit / 1048576;
                modelState.AddModelError("File",
                $"The file exceeds {megabyteSizeLimit:N1} MB.");
            }
            else if (!IsValidFileExtensionAndSignature(
                contentDisposition.FileName.Value, memoryStream,
                permittedExtensions))
            {
                modelState.AddModelError("File",
                    "The file type isn't permitted or the file's " +
                    "signature doesn't match the file's extension.");
            }
            else
            {
                return memoryStream.ToArray();
            }
        }
        catch (Exception ex)
        {
            modelState.AddModelError("File",
                "The upload failed. Please contact the Help Desk " +
                $" for support. Error: {ex.HResult}");
            // Log the exception
        }

        return [];
    }
    /// <summary>
    /// Validates the file extension and signature of the uploaded file.
    /// </summary>
    /// <param name="fileName">The name of the uploaded file.</param>
    /// <param name="data">The file data stream.</param>
    /// <param name="permittedExtensions">An array of permitted file extensions.</param>
    /// <returns><c>true</c> if the file extension and signature are valid; otherwise, <c>false</c>.</returns>
    private static bool IsValidFileExtensionAndSignature(string? fileName, Stream data, string[] permittedExtensions)
    {
        if (string.IsNullOrEmpty(fileName) || data is null || data.Length == 0)
        {
            return false;
        }

        string ext = Path.GetExtension(fileName).ToLowerInvariant();

        if (string.IsNullOrEmpty(ext) || !permittedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        data.Position = 0;

        using var reader = new BinaryReader(data);
        if (ext.Equals(".txt") || ext.Equals(".csv") || ext.Equals(".prn"))
        {
            if (_allowedChars.Length == 0)
            {
                // Limits characters to ASCII encoding.
                for (int i = 0; i < data.Length; i++)
                {
                    if (reader.ReadByte() > sbyte.MaxValue)
                    {
                        return false;
                    }
                }
            }
            else
            {
                // Limits characters to ASCII encoding and
                // values of the _allowedChars array.
                for (int i = 0; i < data.Length; i++)
                {
                    byte b = reader.ReadByte();
                    if (b > sbyte.MaxValue ||
                        !_allowedChars.Contains(b))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // Uncomment the following code block if you must permit
        // files whose signature isn't provided in the _fileSignature
        // dictionary. We recommend that you add file signatures
        // for files (when possible) for all file types you intend
        // to allow on the system and perform the file signature
        // check.
        /*
        if (!_fileSignature.ContainsKey(ext))
        {
            return true;
        }
        */

        // File signature check
        // --------------------
        // With the file signatures provided in the _fileSignature
        // dictionary, the following code tests the input content's
        // file signature.
        List<byte[]> signatures = _fileSignature[ext];
        byte[] headerBytes = reader.ReadBytes(signatures.Max(m => m.Length));

        return signatures.Any(signature =>
            headerBytes.Take(signature.Length).SequenceEqual(signature));
    }
}
