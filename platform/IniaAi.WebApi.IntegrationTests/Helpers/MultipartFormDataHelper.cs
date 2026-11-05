using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IniaAi.WebApi.Contracts.Requests.WorkflowExecutions;

namespace IniaAi.WebApi.IntegrationTests.Helpers;

/// <summary>
/// Helper for building multipart/form-data requests for the submit workflow execution endpoint.
/// </summary>
public static class MultipartFormDataHelper
{
    /// <summary>
    /// Builds a <see cref="MultipartFormDataContent"/> with a JSON metadata part and file parts.
    /// </summary>
    /// <param name="metadata">The submit workflow execution request metadata.</param>
    /// <param name="files">Tuples of (filePath, contentType) for each file to include.</param>
    /// <returns>A configured <see cref="MultipartFormDataContent"/>.</returns>
    public static MultipartFormDataContent BuildSubmitRequest(
        SubmitWorkflowExecutionRequest metadata,
        params (string filePath, string contentType)[] files)
    {
        var content = new MultipartFormDataContent();

        // Add the JSON metadata part
        string metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        var metadataPart = new StringContent(metadataJson, Encoding.UTF8, "application/json");
        content.Add(metadataPart, "metadata");

        // Add file parts
        foreach ((string filePath, string contentType) in files)
        {
            byte[] fileBytes = File.ReadAllBytes(filePath);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            string fileName = Path.GetFileName(filePath);
            content.Add(fileContent, "files", fileName);
        }

        return content;
    }

    /// <summary>
    /// Builds a <see cref="MultipartFormDataContent"/> with only a JSON metadata part and no files.
    /// </summary>
    public static MultipartFormDataContent BuildSubmitRequestWithoutFiles(
        SubmitWorkflowExecutionRequest metadata)
    {
        var content = new MultipartFormDataContent();

        string metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        var metadataPart = new StringContent(metadataJson, Encoding.UTF8, "application/json");
        content.Add(metadataPart, "metadata");

        return content;
    }

    /// <summary>
    /// Builds a <see cref="MultipartFormDataContent"/> with files but no metadata part.
    /// </summary>
    public static MultipartFormDataContent BuildSubmitRequestWithoutMetadata(
        params (string filePath, string contentType)[] files)
    {
        var content = new MultipartFormDataContent();

        foreach ((string filePath, string contentType) in files)
        {
            byte[] fileBytes = File.ReadAllBytes(filePath);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            string fileName = Path.GetFileName(filePath);
            content.Add(fileContent, "files", fileName);
        }

        return content;
    }
}
