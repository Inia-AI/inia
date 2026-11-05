using IniaAi.Common.Interfaces.GitHub;
using IniaAi.Configuration.Platform;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace IniaAi.Infrastructure.GitHub;

public sealed class GitHubWorkflowVersioningService : IGitHubWorkflowVersioningService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<GitHubSettings> _settings;
    private static readonly JsonSerializerOptions _camelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GitHubWorkflowVersioningService(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<GitHubSettings> settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
    }

    public async Task<GitHubCommitResult> CommitWorkflowAsync(
        string workflowTypeId,
        string area,
        string function,
        string content,
        string commitMessage,
        string authorName,
        string authorEmail,
        CancellationToken cancellationToken = default)
    {
        // Read per call: the PAT rotates in SSM, which the configuration reloads while the
        // app runs, and a Blazor circuit keeps this service alive for the whole editor session.
        GitHubSettings settings = _settings.CurrentValue;

        // Central kill-switch: when GitHub mirroring is disabled, no-op without any HTTP so
        // every caller (template-API saves, the editor, copy-to-target) honors the flag.
        if (!settings.Enabled)
        {
            return new GitHubCommitResult();
        }

        string safeWorkflowId = (workflowTypeId ?? string.Empty).Trim('/');
        string relativePath = string.Join("/", new[] { "workflows", safeWorkflowId, $"{safeWorkflowId}.workflow" });
        HttpClient client = _httpClientFactory.CreateClient("GitHubContents");

        if (string.IsNullOrEmpty(settings.Pat))
        {
            throw new InvalidOperationException("GitHub PAT is not configured in app settings.");
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.Pat);

        string? owner = settings.Owner;
        string? repo = settings.Repo;
        string branch = settings.DefaultBranch ?? "main";
        string getUrl = $"repos/{owner}/{repo}/contents/{relativePath}?ref={branch}";

        if (string.IsNullOrEmpty(commitMessage))
        {
            commitMessage = $"No commit message provided. Saved workflow {workflowTypeId}";
        }

        string base64Content = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
        string putUrl = $"repos/{owner}/{repo}/contents/{relativePath}";

        // Read-modify-write against the GitHub contents API. A concurrent save of the same
        // file invalidates the sha we just read, which GitHub rejects with 409; retry once
        // with a freshly fetched sha so a race does not silently drop the mirror update.
        for (int attempt = 0; ; attempt++)
        {
            string? existingSha = null;
            using (HttpResponseMessage getResponse = await client.GetAsync(getUrl, cancellationToken))
            {
                if (getResponse.IsSuccessStatusCode)
                {
                    string json = await getResponse.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);

                    existingSha = doc.RootElement.TryGetProperty("sha", out JsonElement shaProp) && shaProp.ValueKind is JsonValueKind.String
                        ? shaProp.GetString()
                        : throw new InvalidOperationException("GitHub GET returned success but no sha was found.");
                }
            }

            var requestBody = new
            {
                message = commitMessage,
                content = base64Content,
                branch,
                author = new
                {
                    name = authorName,
                    email = authorEmail
                },
                committer = new
                {
                    name = settings.CommitterName,
                    email = settings.CommitterEmail
                },
                sha = existingSha
            };

            string jsonBody = JsonSerializer.Serialize(requestBody, _camelCaseJsonOptions);

            using HttpResponseMessage putResponse = await client.PutAsync(
                putUrl,
                new StringContent(jsonBody, Encoding.UTF8, "application/json"),
                cancellationToken);

            if (putResponse.IsSuccessStatusCode)
            {
                string resultJson = await putResponse.Content.ReadAsStringAsync(cancellationToken);
                using var resultDoc = JsonDocument.Parse(resultJson);

                return new GitHubCommitResult
                {
                    Sha = resultDoc.RootElement.GetProperty("content").GetProperty("sha").GetString() ?? string.Empty,
                    HtmlUrl = resultDoc.RootElement.GetProperty("content").GetProperty("html_url").GetString() ?? string.Empty,
                    Created = putResponse.StatusCode is HttpStatusCode.Created
                };
            }

            int status = (int)putResponse.StatusCode;

            // Stale sha from a concurrent writer — re-read the latest sha and try once more.
            if (status == 409 && attempt == 0)
            {
                continue;
            }

            string body = await putResponse.Content.ReadAsStringAsync(cancellationToken);

            throw status switch
            {
                401 => new InvalidOperationException("GitHub: Unauthorized (401). Check PAT value and scope (repo:contents)."),
                403 => new InvalidOperationException("GitHub: Forbidden (403). PAT may lack repo:contents or branch protection blocks updates."),
                404 => new InvalidOperationException("GitHub: Not Found (404). Check owner/repo/branch/path existence."),
                409 => new InvalidOperationException("GitHub: Conflict (409). The sha may be stale; try fetching latest and retry."),
                _ => new InvalidOperationException($"GitHub PUT failed ({status}): {body}"),
            };
        }
    }
}
