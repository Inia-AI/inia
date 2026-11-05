using IniaAi.Common.Interfaces.GitHub;
using IniaAi.Configuration.Platform;
using IniaAi.Infrastructure.GitHub;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;

namespace IniaAi.Infrastructure.Tests.GitHub;

public class GitHubWorkflowVersioningServiceTests
{
    private const string GITHUB_SECTION = "PlatformSettings:GitHub";

    [Fact]
    public async Task CommitWorkflowAsync_WhenDisabled_NoOpsWithoutCreatingAnHttpClient()
    {
        // Enabled is the kill-switch. With it off, the service must short-circuit before any
        // HTTP — even when a PAT is present — so callers that don't pre-check the flag (the
        // template-API save and copy-to-target) never commit to GitHub in disabled environments.
        var factory = new RecordingHttpClientFactory();
        GitHubWorkflowVersioningService service = CreateService(BuildConfiguration(enabled: false, pat: "a-token"), factory);

        GitHubCommitResult result = await CommitAsync(service);

        Assert.Equal(0, factory.CreateClientCalls);
        Assert.Equal(string.Empty, result.Sha);
        Assert.False(result.Created);
    }

    [Fact]
    public async Task CommitWorkflowAsync_AfterThePatIsRotatedInConfiguration_UsesTheNewPatWithoutARestart()
    {
        // The PAT lives in SSM, which the configuration reloads every few minutes. A Blazor
        // circuit holds one service instance for as long as the editor tab stays open, so the
        // service must read the PAT at call time or a rotation only lands after a restart.
        IConfigurationRoot configuration = BuildConfiguration(enabled: true, pat: "expired-pat");
        var gitHub = new GitHubContentsStub();
        GitHubWorkflowVersioningService service = CreateService(configuration, gitHub);

        await CommitAsync(service);
        configuration[$"{GITHUB_SECTION}:Pat"] = "rotated-pat";
        configuration.Reload();
        await CommitAsync(service);

        Assert.Equal(["expired-pat", "expired-pat", "rotated-pat", "rotated-pat"], gitHub.BearerTokens);
    }

    private static IConfigurationRoot BuildConfiguration(bool enabled, string pat)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{GITHUB_SECTION}:Enabled"] = enabled.ToString(),
                [$"{GITHUB_SECTION}:Owner"] = "owner",
                [$"{GITHUB_SECTION}:Repo"] = "repo",
                [$"{GITHUB_SECTION}:Pat"] = pat,
            })
            .Build();
    }

    private static GitHubWorkflowVersioningService CreateService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        IOptionsMonitor<GitHubSettings> settings = new ServiceCollection()
            .Configure<GitHubSettings>(configuration.GetSection(GITHUB_SECTION))
            .BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<GitHubSettings>>();

        return new GitHubWorkflowVersioningService(httpClientFactory, settings);
    }

    private static Task<GitHubCommitResult> CommitAsync(GitHubWorkflowVersioningService service)
    {
        return service.CommitWorkflowAsync(
            "WT-Report", area: "", function: "", content: "{}",
            commitMessage: "msg", authorName: "n", authorEmail: "e@example.com",
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private sealed class GitHubContentsStub : HttpMessageHandler, IHttpClientFactory
    {
        public List<string?> BearerTokens { get; } = [];

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(this, disposeHandler: false) { BaseAddress = new Uri("https://api.github.com/") };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            BearerTokens.Add(request.Headers.Authorization?.Parameter);

            HttpResponseMessage response = request.Method == HttpMethod.Get
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("""{"content":{"sha":"abc","html_url":"https://github.com/owner/repo"}}"""),
                };

            return Task.FromResult(response);
        }
    }

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        public int CreateClientCalls { get; private set; }

        public HttpClient CreateClient(string name)
        {
            CreateClientCalls++;
            return new HttpClient();
        }
    }
}
