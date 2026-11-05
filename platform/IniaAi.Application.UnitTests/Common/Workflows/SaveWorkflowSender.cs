using FluentValidation;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.UnitTests.Common.Workflows;

internal static class SaveWorkflowSender
{
    public static ISender Create(WorkflowSavePipeline pipeline, ICoreDbContext coreDbContext, PlatformSettings? settings = null)
    {
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddRequestDispatching([typeof(SaveWorkflowCommandHandler)]);
        _ = services.AddSingleton(pipeline);
        _ = services.AddSingleton(coreDbContext);
        _ = services.AddSingleton<IValidator<SaveWorkflowCommand>>(
            new SaveWorkflowCommandValidator(Options.Create(settings ?? new PlatformSettings())));

        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }
}
