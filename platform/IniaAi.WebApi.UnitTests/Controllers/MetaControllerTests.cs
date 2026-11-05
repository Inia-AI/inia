using System.Diagnostics;
using IniaAi.WebApi.Contracts.Responses.Meta;
using IniaAi.WebApi.Controllers.v1;
using IniaAi.WebApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace IniaAi.WebApi.UnitTests.Controllers;

public sealed class MetaControllerTests
{
    private const char VERSION_METADATA_SEPARATOR = '+';

    private static readonly TimeSpan _processStartTolerance = TimeSpan.FromSeconds(1);

    [Fact]
    public void GetUptime_CountsFromWhenTheProcessStarted()
    {
        DateTime askedAtUtc = DateTime.UtcNow;
        DateTime processStartedAtUtc = ReadProcessStartTimeUtc();
        MetaController controller = new(new RequestContext());

        ActionResult<GetMetaUptimeResponse> result = controller.GetUptime();

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        GetMetaUptimeResponse uptime = Assert.IsType<GetMetaUptimeResponse>(ok.Value);
        Assert.True(uptime.StartedAtUtc < askedAtUtc, $"The start time {uptime.StartedAtUtc:O} is not before the request at {askedAtUtc:O}.");
        Assert.InRange(uptime.StartedAtUtc, processStartedAtUtc - _processStartTolerance, processStartedAtUtc + _processStartTolerance);
        Assert.True(uptime.Uptime > TimeSpan.Zero, $"The uptime {uptime.Uptime} is not positive.");
    }

    [Fact]
    public void GetBuildInfo_ReportsTheCommitTheVersionNames()
    {
        string[] versionParts = ApplicationVersion.InformationalVersion.Split(VERSION_METADATA_SEPARATOR);
        string versionCommit = versionParts.Length > 1 ? versionParts[1] : string.Empty;
        MetaController controller = new(new RequestContext());

        ActionResult<GetMetaBuildInfoResponse> result = controller.GetBuildInfo();

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        GetMetaBuildInfoResponse build = Assert.IsType<GetMetaBuildInfoResponse>(ok.Value);
        Assert.Equal(versionCommit, build.CommitSha);
    }

    private static DateTime ReadProcessStartTimeUtc()
    {
        using Process process = Process.GetCurrentProcess();

        return process.StartTime.ToUniversalTime();
    }
}
