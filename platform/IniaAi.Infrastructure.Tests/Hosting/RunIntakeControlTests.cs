using System.Net;
using System.Net.Http.Json;
using IniaAi.Application.Commands.WorkflowExecutions.Shared;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Infrastructure.Hosting;
using IniaAi.Services.Models.Jobs;
using IniaAi.Services.Models.Jobs.Enums;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Infrastructure.Tests.Hosting;

/// <summary>
/// The control port a deploy drains through: on the loopback address beside the server's own
/// addresses, it pauses run intake for the time it is given and opens it again, counts the reports
/// still running, serves nothing else, and the server's own addresses serve none of it. A real
/// server whose every other request needs a signed-in user, because the rules are about which
/// address answers what and that the control port answers ahead of the rest of the pipeline.
/// </summary>
public sealed class RunIntakeControlTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Afternoon = new(2026, 11, 6, 14, 0, 0, TimeSpan.Zero);

    private readonly int _controlPort = LocalTestHttpServer.ReserveFreeLoopbackPort();
    private int _publicPort;
    private readonly RecordingJobsService _jobs = new();
    private readonly RunIntake _intake = new(new SettableClock(Afternoon));
    private readonly HttpClient _client = new(new HttpClientHandler { AllowAutoRedirect = false });
    private WebApplication _app = null!;

    public async ValueTask InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = ["--urls", "http://127.0.0.1:0", $"--{RunIntakeControl.PORT_KEY}={_controlPort}"],
        });
        _ = builder.Services.AddSingleton(_intake).AddSingleton<IJobsService>(_jobs);
        _ = builder.Services.AddAuthentication().AddCookie();
        _ = builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        _ = builder.AddRunIntakeControl();

        _app = builder.Build();
        _ = _app.UseAuthentication();
        _ = _app.UseAuthorization();
        _ = _app.MapGet("/health", () => "Healthy").AllowAnonymous();
        await _app.StartAsync(TestContext.Current.CancellationToken);
        _publicPort = _app.Urls.Select(url => new Uri(url).Port).Single(port => port != _controlPort);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task TheControlPort_PausesIntakeForTheTimeGiven_OpensIt_AndCountsOnlyTheReportsStillRunning()
    {
        _jobs.ExecutedJobs.Add(Report(JobState.Processing));
        _jobs.ExecutedJobs.Add(Report(JobState.Queued));
        _jobs.ExecutedJobs.Add(Report(JobState.Done));
        _jobs.ExecutedJobs.Add(new DelegatedJob("org", "user", "Export", (_, _) => Task.CompletedTask));

        Assert.Equal(new RunIntakeState(false, 2, null), await ControlAsync(HttpMethod.Get, RunIntakeControl.STATE_PATH));
        Assert.Equal(new RunIntakeState(true, 2, Afternoon.AddMinutes(35)), await ControlAsync(HttpMethod.Post, $"{RunIntakeControl.PAUSE_PATH}?seconds=2100"));
        Assert.True(_intake.IsPaused);
        Assert.Equal(new RunIntakeState(false, 2, null), await ControlAsync(HttpMethod.Post, RunIntakeControl.OPEN_PATH));
        Assert.False(_intake.IsPaused);
    }

    [Fact]
    public async Task APauseWithoutALengthFromOneSecondToTwoHours_IsRefused_AndPausesNothing()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, _controlPort, RunIntakeControl.PAUSE_PATH)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, _controlPort, $"{RunIntakeControl.PAUSE_PATH}?seconds=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, _controlPort, $"{RunIntakeControl.PAUSE_PATH}?seconds={RunIntakeControl.MAX_PAUSE_SECONDS + 1}")).StatusCode);
        Assert.False(_intake.IsPaused);
    }

    [Fact]
    public async Task TheServersOwnAddress_ServesNoControlPath_AndTheControlPortServesNothingElse()
    {
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, _publicPort, "/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await SendAsync(HttpMethod.Post, _publicPort, $"{RunIntakeControl.PAUSE_PATH}?seconds=60")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, _controlPort, "/health")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, _controlPort, RunIntakeControl.PAUSE_PATH)).StatusCode);
        Assert.False(_intake.IsPaused);
    }

    private static Job Report(JobState state)
    {
        var job = new Job("org", "workflow-type") { WorkflowExecutionId = Guid.NewGuid().ToString() };
        job.SetState(state);
        return job;
    }

    private async Task<RunIntakeState?> ControlAsync(HttpMethod method, string path)
    {
        using HttpResponseMessage response = await SendAsync(method, _controlPort, path);
        _ = response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RunIntakeState>(TestContext.Current.CancellationToken);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, int port, string path)
    {
        return _client.SendAsync(new HttpRequestMessage(method, $"http://127.0.0.1:{port}{path}"), TestContext.Current.CancellationToken);
    }
}

/// <summary>
/// Where the control port listens: on by default only in a container, off when configured as 0, and
/// beside every address the server already listens on.
/// </summary>
public sealed class RunIntakeControlAddressTests
{
    [Theory]
    [InlineData(null, false, null)]
    [InlineData(null, true, RunIntakeControl.CONTAINER_PORT)]
    [InlineData("6100", false, 6100)]
    [InlineData("0", true, null)]
    public void ThePort_IsOnByDefaultOnlyInAContainer_AndZeroTurnsItOff(string? configured, bool inContainer, int? expected)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [RunIntakeControl.PORT_KEY] = configured })
            .Build();

        Assert.Equal(expected, RunIntakeControl.Port(configuration, inContainer));
    }

    [Theory]
    [InlineData("https://localhost:7557;http://localhost:5775", null, "https://localhost:7557|http://localhost:5775")]
    [InlineData(null, "80", "http://*:80")]
    [InlineData(null, null, "http://localhost:5000")]
    public void TheControlPortJoins_TheAddressesTheServerAlreadyListensOn(string? urls, string? httpPorts, string expected)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [WebHostDefaults.ServerUrlsKey] = urls, [WebHostDefaults.HttpPortsKey] = httpPorts })
            .Build();

        Assert.Equal(expected, string.Join('|', RunIntakeControl.ServerUrls(configuration)));
    }
}
