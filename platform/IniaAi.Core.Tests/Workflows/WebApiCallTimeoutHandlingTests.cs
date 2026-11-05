using IniaAi.Core.Tests.Workflows.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using WorkflowState = IniaAi.Domain.Entities.WorkflowExecutions.WorkflowState;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// Locks in the fix for the bug where an HttpClient internal timeout (signalled
/// by .NET as TaskCanceledException with TimeoutException inner) was being treated
/// as a workflow cancellation by WorkflowHandler — propagating up as
/// OperationCanceledException, tripping JobExecutor's cancellation arm, and
/// stranding the WorkflowExecution in Running.
///
/// The fix is in WebApiCallStrategy.ExecuteBlockAsync: a TaskCanceledException
/// whose inner is a TimeoutException is rewrapped as a plain TimeoutException
/// before it can escape the strategy.
/// </summary>
public class WebApiCallTimeoutHandlingTests : IClassFixture<WorkflowsFixture<WebApiCallTimeoutHandlingTests>>
{
    private readonly WorkflowsFixture<WebApiCallTimeoutHandlingTests> _fixture;
    private readonly WorkflowHandler _workflowHandler;
    private readonly IServiceProvider _testServiceProvider;
    private readonly IServiceScope _serviceScope;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    public WebApiCallTimeoutHandlingTests(
        WorkflowsFixture<WebApiCallTimeoutHandlingTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _testServiceProvider = _fixture.BuildTestServiceProvider(testOutputHelper);
        _workflowHandler = _testServiceProvider.GetRequiredService<WorkflowHandler>();
        _serviceScope = _testServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        _userDataUnitOfWork = _serviceScope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
    }

    /// <summary>
    /// When a WebApiCall block hits an HttpClient internal timeout, the strategy
    /// must rewrap the exception so the section-loop sees a normal block failure:
    /// the failing block ends up Failed, the section continues, and the downstream
    /// block runs to completion.
    /// </summary>
    [Fact]
    public async Task ExecuteWorkflowAsync_WebApiCallTimesOut_BlockFailsAndDownstreamBlockStillRuns()
    {
        IBlockStrategy fakeStrategy = new TimeoutSimulatingWebApiCallStrategy();
        IBlockStrategy originalStrategy = TestBlockStrategies.Swap(
            _workflowHandler,
            BlockTypes.WebApiCall,
            fakeStrategy);

        try
        {
            var workflow = new Workflow();
            var section = new Section("section", workflow);

            var failingBlock = new Block(BlockTypes.WebApiCall, section)
            {
                Name = "Timing Out API Call",
                Properties = new WebApiCallProperties
                {
                    UrlTemplate = "https://example.invalid/path",
                    HttpMethod = "GET",
                },
            };

            var downstreamBlock = new Block(BlockTypes.StringManipulation, section)
            {
                Name = "Downstream Block",
                Properties = new StringManipulationProperties
                {
                    InputTemplate = "downstream-runs",
                    Operation = StringManipulationOperation.ChangeCase,
                    CaseType = StringManipulationCaseType.Upper,
                },
            };

            Exception? caught = null;
            try
            {
                _ = await _workflowHandler.ExecuteWorkflowAsync(
                    workflow,
                    anonymisationMap: [],
                    organizationId: "testOrg",
                    jobId: Guid.NewGuid().ToString(),
                    userDataUnitOfWork: _userDataUnitOfWork,
                    inputs: [],
                    cancellationToken: TestContext.Current.CancellationToken);
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // Workflow does NOT bail out as cancellation — the block-loop's
            // `catch (Exception)` swallows the rewrapped TimeoutException and
            // continues to the next block.
            Assert.Null(caught);

            // Failing block is correctly marked Failed by the per-block catch.
            Assert.Equal(ExecutionStatus.Failed, failingBlock.ExecutionState?.Status);

            // Downstream block actually ran and produced output.
            Assert.Equal(ExecutionStatus.Completed, downstreamBlock.ExecutionState?.Status);
            Assert.NotNull(downstreamBlock.BlockResponse);
            Assert.Equal("DOWNSTREAM-RUNS", downstreamBlock.BlockResponse.RawResponse);
        }
        finally
        {
            _ = TestBlockStrategies.Swap(_workflowHandler, BlockTypes.WebApiCall, originalStrategy);
        }
    }

    /// <summary>
    /// End-to-end signal that the persisted WorkflowExecution doesn't get stuck
    /// in Running. We mirror JobExecutor.ProcessJobTypeAsync's success / cancel /
    /// error arms around the ExecuteWorkflowAsync call. Pre-fix, the workflow
    /// would throw OperationCanceledException and JobExecutor's cancel arm would
    /// fire (no state update -> stuck in Running). Post-fix, the workflow
    /// returns normally and the success arm transitions the row to Draft.
    /// </summary>
    [Fact]
    public async Task ExecuteWorkflowAsync_WebApiCallTimesOut_JobExecutorSuccessArmTakenAndStateBecomesDraft()
    {
        IBlockStrategy fakeStrategy = new TimeoutSimulatingWebApiCallStrategy();
        IBlockStrategy originalStrategy = TestBlockStrategies.Swap(
            _workflowHandler,
            BlockTypes.WebApiCall,
            fakeStrategy);

        try
        {
            var workflow = new Workflow();
            var section = new Section("section", workflow);

            _ = new Block(BlockTypes.WebApiCall, section)
            {
                Name = "Timing Out API Call",
                Properties = new WebApiCallProperties
                {
                    UrlTemplate = "https://example.invalid/path",
                    HttpMethod = "GET",
                },
            };

            string organizationId = "testOrg";
            string jobId = Guid.NewGuid().ToString();

            await _workflowHandler.CreateRootOutputAsync(
                _userDataUnitOfWork,
                jobId: jobId,
                rootWorkflow: workflow,
                organizationId: organizationId,
                userId: null);

            string? executionId = _workflowHandler.WorkflowExecutionId;
            Assert.NotNull(executionId);

            // Mirror JobExecutor.ProcessJobTypeAsync's three arms.
            bool tookCancellationArm = false;
            bool tookErrorArm = false;
            try
            {
                _ = await _workflowHandler.ExecuteWorkflowAsync(
                    workflow,
                    anonymisationMap: new(),
                    organizationId: organizationId,
                    jobId: jobId,
                    userDataUnitOfWork: _userDataUnitOfWork,
                    inputs: [],
                    cancellationToken: TestContext.Current.CancellationToken);

                // Success arm: JobExecutor.Execute transitions WorkflowState to Draft
                // when the job finishes without an unhandled exception escaping.
                await _workflowHandler.UpdateWorkflowStateAsync(
                    WorkflowState.Draft,
                    _userDataUnitOfWork);
            }
            catch (OperationCanceledException)
            {
                // Cancellation arm: job.Cancel(); NO WorkflowState update — this is
                // the path that strands the row in Running. Pre-fix, this is what
                // we hit. Post-fix, we should never get here.
                tookCancellationArm = true;
            }
            catch (Exception)
            {
                // Error arm: JobExecutor.HandleJobError sets WorkflowState to Failed.
                tookErrorArm = true;
                await _workflowHandler.UpdateWorkflowStateAsync(
                    WorkflowState.Failed,
                    _userDataUnitOfWork);
            }

            Assert.False(tookCancellationArm, "WorkflowHandler should not propagate the timeout as cancellation.");
            Assert.False(tookErrorArm, "Block-level timeout should be swallowed at the section loop, not bubble up as a hard error.");

            WorkflowExecution? persistedExecution = await _userDataUnitOfWork
                .WorkflowExecutions
                .GetByIdAsync(executionId, TestContext.Current.CancellationToken);

            Assert.NotNull(persistedExecution);
            Assert.Equal(WorkflowState.Draft, persistedExecution.WorkflowState);
        }
        finally
        {
            _ = TestBlockStrategies.Swap(_workflowHandler, BlockTypes.WebApiCall, originalStrategy);
        }
    }

    /// <summary>
    /// Subclass of the real strategy that simulates an HttpClient internal
    /// per-request timeout by throwing the exact exception shape .NET produces:
    /// TaskCanceledException with TimeoutException as the inner. The override
    /// is on the protected virtual ExecuteRequestAsync so the public
    /// ExecuteBlockAsync (and its rewrap try/catch — the fix being verified)
    /// runs unchanged.
    /// </summary>
    private sealed class TimeoutSimulatingWebApiCallStrategy : WebApiCallStrategy
    {
        protected override Task<string> ExecuteRequestAsync(
            HttpMessageHandler handler,
            string url,
            string method,
            string? payload,
            Dictionary<string, string> headers,
            string contentType,
            string acceptHeader,
            CancellationToken cancellationToken)
        {
            throw new TaskCanceledException(
                "Simulated HttpClient internal request timeout",
                new TimeoutException("HttpClient request timeout"));
        }

        protected override Task DelayBeforeRetryAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
