using System.Globalization;
using IniaAi.Application.Commands.WorkflowExecutions.Shared;
using IniaAi.Application.Common.Interfaces.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Hosting;

/// <summary>
/// A control port on the loopback address only, through which a deploy pauses this process's run
/// intake and reads how many reports it still runs, before it replaces the process. Inside a
/// container only a command run in that container reaches it, so nothing new is exposed. The port
/// is <see cref="PORT_KEY"/>, <see cref="CONTAINER_PORT"/> by default in a container and off
/// outside one, where several instances share the machine's loopback address. The control port
/// serves only the run-intake paths, ahead of the rest of the pipeline, and the other ports serve
/// none of them.
/// </summary>
public static class RunIntakeControl
{
    public const string PORT_KEY = "PlatformSettings:RunIntake:ControlPort";

    internal const int CONTAINER_PORT = 5999;

    internal const string STATE_PATH = "/run-intake";

    internal const string PAUSE_PATH = "/run-intake/pause";

    internal const string OPEN_PATH = "/run-intake/open";

    internal const string SECONDS_PARAMETER = "seconds";

    /// <summary>The longest pause: a deploy drains for 30 minutes at most and applies within 15 more.</summary>
    internal const int MAX_PAUSE_SECONDS = 2 * 60 * 60;

    private const string DEFAULT_URL = "http://localhost:5000";

    /// <summary>Adds the control port to the addresses the server already listens on, and answers it first.</summary>
    public static WebApplicationBuilder AddRunIntakeControl(this WebApplicationBuilder builder)
    {
        bool inContainer = string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);
        if (Port(builder.Configuration, inContainer) is int port)
        {
            _ = builder.WebHost.UseUrls([.. ServerUrls(builder.Configuration), $"http://127.0.0.1:{port}"]);
            _ = builder.Services.AddSingleton<IStartupFilter>(new AnsweredFirst(port));
        }

        return builder;
    }

    /// <summary>The control port, or null when it is off: a configured 0 turns it off.</summary>
    internal static int? Port(IConfiguration configuration, bool inContainer)
    {
        int? configured = configuration.GetValue<int?>(PORT_KEY);
        if (configured is not null)
        {
            return configured > 0 ? configured : null;
        }

        return inContainer ? CONTAINER_PORT : null;
    }

    /// <summary>
    /// The addresses the server listens on without the control port, read the way the host reads
    /// them: the URLs, else the HTTP and HTTPS ports, else Kestrel's default. Naming any address
    /// replaces them all, so the control port is added to these.
    /// </summary>
    internal static IReadOnlyList<string> ServerUrls(IConfiguration configuration)
    {
        string? urls = configuration[WebHostDefaults.ServerUrlsKey];
        if (!string.IsNullOrWhiteSpace(urls))
        {
            return urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        string[] ports = [.. OnPorts(configuration[WebHostDefaults.HttpPortsKey], "http"), .. OnPorts(configuration[WebHostDefaults.HttpsPortsKey], "https")];
        return ports.Length > 0 ? ports : [DEFAULT_URL];
    }

    private static IEnumerable<string> OnPorts(string? ports, string scheme)
    {
        return (ports ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(port => $"{scheme}://*:{port}");
    }

    private static async Task AnswerAsync(HttpContext context)
    {
        RunIntake intake = context.RequestServices.GetRequiredService<RunIntake>();
        string? changed;
        switch (context.Request.Method, context.Request.Path.Value)
        {
            case ("GET", STATE_PATH):
                changed = null;
                break;
            case ("POST", PAUSE_PATH) when int.TryParse(context.Request.Query[SECONDS_PARAMETER], NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) && seconds is > 0 and <= MAX_PAUSE_SECONDS:
                intake.Pause(TimeSpan.FromSeconds(seconds));
                changed = $"paused until {intake.PausedUntil:O}";
                break;
            case ("POST", PAUSE_PATH):
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync($"Name how long the pause lasts, from 1 to {MAX_PAUSE_SECONDS} seconds: {PAUSE_PATH}?{SECONDS_PARAMETER}=<seconds>.", context.RequestAborted);
                return;
            case ("POST", OPEN_PATH):
                intake.Open();
                changed = "opened";
                break;
            default:
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
        }

        int running = (await context.RequestServices.GetRequiredService<IJobsService>().GetJobsAsync())
            .Count(job => job.ListedAsReport && !job.IsFinished);
        if (changed is not null)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(RunIntakeControl))
                .LogInformation("Run intake {Change} through the control port; {Running} report(s) still running", changed, running);
        }

        await context.Response.WriteAsJsonAsync(new RunIntakeState(intake.IsPaused, running, intake.PausedUntil), context.RequestAborted);
    }

    private sealed class AnsweredFirst(int port) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                _ = app.MapWhen(context => context.Connection.LocalPort == port, control => control.Run(AnswerAsync));
                next(app);
            };
        }
    }
}

/// <summary>
/// What the control port answers: whether intake is paused and until when, and how many reports
/// this process still runs.
/// </summary>
internal sealed record RunIntakeState(bool Paused, int Running, DateTimeOffset? PausedUntil);
