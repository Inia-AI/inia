using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Signatures;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Diff;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.UnitTests.Common.Workflows;

internal sealed class ThrowingWorkflowSavePipeline : WorkflowSavePipeline
{
    private readonly Func<Exception?> _hook;
    private bool _fired;

    public ThrowingWorkflowSavePipeline(SharedComponentsTestHarness harness, Func<Exception?> hook)
        : base(
            harness.UnitOfWork,
            harness.Context,
            harness.Canonicalizer,
            new WorkflowDiffService(),
            new WorkflowNarrativeGenerator(),
            Options.Create(new AgentRuntimeSettings()),
            NullLogger<WorkflowSavePipeline>.Instance,
            harness.SuperAdmins)
    {
        _hook = hook;
    }

    public override Task<ErrorOr<SaveWorkflowResult>> SaveCoreAsync(
        SaveWorkflowCommand request,
        string? narrativeOverride,
        CancellationToken cancellationToken)
    {
        if (!_fired)
        {
            _fired = true;
            Exception? toThrow = _hook();
            if (toThrow is not null)
            {
                throw toThrow;
            }
        }

        return base.SaveCoreAsync(request, narrativeOverride, cancellationToken);
    }
}
