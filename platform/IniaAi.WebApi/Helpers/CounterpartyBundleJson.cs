using System.Text.Json;
using IniaAi.Common.Constants;
using IniaAi.Common.Text;
using IniaAi.Infrastructure.Persistence.Serialization;
using IniaAi.WebApi.Contracts.Dto;
using Microsoft.Net.Http.Headers;

namespace IniaAi.WebApi.Helpers;

/// <summary>
/// The counterparty bundle as a file: indented JSON in the API's casing, with the property
/// bag's keys as their authors typed them. The bag is case-insensitive by invariant, so
/// camelCasing its keys would only rewrite what a prompt engineer wrote.
/// </summary>
public static class CounterpartyBundleJson
{
    private const string CONTENT_TYPE = "application/json";
    private const string FALLBACK_NAME_PREFIX = CounterpartyTerms.PLURAL_LOWER;
    private const string EXTENSION = ".json";

    private static readonly JsonSerializerOptions _options = new(JsonDefaults.OptionsWithOriginalKeys)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Streams the bundle to the response as a JSON download named after the organization and the
    /// export date. Streaming keeps a large bundle off the heap a second time.
    /// </summary>
    public static async Task WriteDownloadAsync(HttpResponse response, CounterpartyBundleDto bundle, string organizationId, CancellationToken cancellationToken)
    {
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(ExportFileName(bundle, organizationId));

        response.ContentType = CONTENT_TYPE;
        response.Headers.ContentDisposition = disposition.ToString();

        await JsonSerializer.SerializeAsync(response.Body, bundle, _options, cancellationToken);
    }

    private static string ExportFileName(CounterpartyBundleDto bundle, string organizationId)
    {
        return DownloadFileName.Build(
            $"{CounterpartyTerms.PLURAL_LOWER} {bundle.SourceOrganizationName} {bundle.ExportedAt:yyyy-MM-dd}",
            organizationId,
            FALLBACK_NAME_PREFIX,
            EXTENSION);
    }
}
