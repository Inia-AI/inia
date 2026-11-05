using ErrorOr;
using IniaAi.Common;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Agents;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Services;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class AgentBlockStrategyTests : IClassFixture<WorkflowsFixture<AgentBlockStrategyTests>>
{
    private const string WORKFLOW_EXECUTION_ID = "wfexec1";
    private const string ORGANIZATION_ID = "org-agent-tests";
    private const string MODEL = "claude-sonnet-4-6";

    private readonly IServiceProvider _testServiceProvider;

    public AgentBlockStrategyTests(WorkflowsFixture<AgentBlockStrategyTests> fixture, ITestOutputHelper outputHelper)
    {
        _testServiceProvider = fixture.BuildTestServiceProvider(outputHelper);
    }

    private sealed class ShortDeadlineAgentBlockStrategy : AgentBlockStrategy
    {
        protected override TimeSpan ResolvePollDeadline(AgentRuntimeSettings settings, int? declaredDeadlineMinutes)
        {
            return TimeSpan.FromMilliseconds(120);
        }
    }

    private (Block Block, BlockExecutionContext Context, RecordingAgentRunExecutor Executor) ArrangeWithRecordingExecutor(
        Action<AgentProperties>? configureProperties = null)
    {
        var executor = new RecordingAgentRunExecutor
        {
            OnExecute = static request => ErrorOrFactory.From(new AgentRunConclusion(
                Monitor(request.ExecutionId, AgentRunStatus.Succeeded, null, new AgentRunResult("out", null)),
                TraceFiles: null,
                CompleteWithheld: false,
                Attempts: [new AgentRunAttempt(1, request.ExecutionId, AgentRunStatus.Succeeded, null, 0m, Unacknowledged: false)])),
        };
        (Block block, BlockExecutionContext context, _) = Arrange(
            new Dictionary<string, string> { ["skill"] = "Do the work." },
            inputs: null,
            configureProperties,
            executor);

        return (block, context, executor);
    }

    private (Block Block, BlockExecutionContext Context, StubAgentRuntimeClient Stub) Arrange(
        Dictionary<string, string> workerSkills,
        Dictionary<string, string?>? inputs = null,
        Action<AgentProperties>? configureProperties = null,
        IAgentRunExecutor? executor = null)
    {
        var properties = new AgentProperties
        {
            WorkerSkills = [.. workerSkills.Select(static skill => new AgentWorkerSkill
            {
                Name = skill.Key,
                Description = $"Handles {skill.Key}.",
                Content = skill.Value,
            })],
            Model = MODEL,
        };
        configureProperties?.Invoke(properties);
        var block = new Block(BlockTypes.Agent, new Section("test", new Workflow("test")))
        {
            Name = "Agent",
            Properties = properties,
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var stub = Assert.IsType<StubAgentRuntimeClient>(
            _testServiceProvider.GetRequiredService<IAgentRuntimeClient>());
        if (executor is not null)
        {
            context = context.WithAgentRunExecutor(executor);
        }
        else
        {
            stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, TimeSpan.Zero);
            stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);
            stub.OnListRunFiles = _ => ErrorOrFactory.From<IReadOnlyList<AgentRunFileEntry>>(
                Array.Empty<AgentRunFileEntry>());
        }

        context.WorkflowExecutionId = WORKFLOW_EXECUTION_ID;
        context.OrganizationId = ORGANIZATION_ID;
        context.TextInputs = inputs ?? [];
        block.SetExecutionState(false, context.TextInputs);

        return (block, context, stub);
    }

    private static void ArrangeTraceManifest(StubAgentRuntimeClient stub, string path, byte[] content)
    {
        stub.OnListRunFiles = _ => ErrorOrFactory.From<IReadOnlyList<AgentRunFileEntry>>([
            new AgentRunFileEntry
            {
                Path = path,
                SizeBytes = content.LongLength,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
                Kind = AgentRunFileKind.Trace,
            }]);
        stub.OnGetRunFile = (_, requestedPath) => requestedPath == path
            ? content
            : Error.NotFound("AgentRun.NotFound", $"No such file: {requestedPath}");
    }

    private static AgentRunMonitor Monitor(
        string executionId,
        AgentRunStatus status,
        TimeSpan? pollInterval,
        AgentRunResult? result = null,
        AgentRunError? error = null)
    {
        return new AgentRunMonitor
        {
            ExecutionId = executionId,
            Status = status,
            Attempt = 1,
            PollInterval = pollInterval,
            Result = result,
            Error = error,
        };
    }

    /// <summary>
    /// Mirrors the upload path: <see cref="FileMetadata"/> splits the file name, so
    /// <see cref="Document.Name"/> is the extensionless display name and the extension
    /// lives only in the metadata — the exact shape the staging code must recompose.
    /// </summary>
    private static Document CreateInMemoryDocument(string fileName, string? documentClass = null)
    {
        var metadata = new FileMetadata("test-org", fileName, new byte[] { 1 });
        var document = new Document(metadata, Utils.DetermineMediaTypeFromExtension(Path.GetExtension(fileName)));
        if (documentClass is not null)
        {
            document.CurrentDocumentInfo = new DocumentInfo(document.Id) { DocumentClass = documentClass };
        }

        return document;
    }


    private const string CITED_PAGE_TEXT = "Total assets                36       4,027,553,656       3,318,824,587";
    private const string CITING_OUTPUT =
        "{\"fields\":{\"totalAssets\":4027553656},\"citations\":{\"totalAssets\":"
        + "{\"document\":\"Annual Report 2025.pdf\",\"page\":2,"
        + "\"as_printed\":\"Total assets 4,027,553,656 3,318,824,587\"}}}";

    private (Block Block, BlockExecutionContext Context, Page CitedPage) ArrangeCitingRun()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });
        Document document = CreateInMemoryDocument("Annual Report 2025.pdf");
        block.Documents.Add(document);
        var otherPage = new Page(document, 1, 1) { Text = "Nothing cited lives here" };
        var citedPage = new Page(document, 2, 2) { Text = CITED_PAGE_TEXT };
        context.AllPages.AddRange([otherPage, citedPage]);

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null,
            result: new AgentRunResult(CITING_OUTPUT, null));

        return (block, context, citedPage);
    }

    [Fact]
    public async Task ExecuteBlockAsync_OutputCitations_LandAsPageResolvedDocumentSourceFacts()
    {
        (Block block, BlockExecutionContext context, Page citedPage) = ArrangeCitingRun();

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Fact broad = Assert.Single(context.BlockAnnotations.Facts, static fact => !fact.IsNumerical);
        DocumentSource source = Assert.IsType<DocumentSource>(Assert.Single(broad.Sources));
        Assert.Equal(citedPage.Id, source.PageId);
        Assert.Equal(2, source.FilePageNumber);
        Assert.False(source.Unverified);
        Assert.Contains(
            context.BlockAnnotations.Facts,
            static fact => fact.IsNumerical && fact.Value == "4,027,553,656");
    }

    [Fact]
    public async Task ExecuteBlockAsync_BlockThatReachesNoParagraph_ProducesNoFacts()
    {
        (Block block, BlockExecutionContext context, _) = ArrangeCitingRun();
        context.IsCitationSource = false;

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Empty(context.BlockAnnotations.Facts);
    }

    [Fact]
    public async Task ExecuteBlockAsync_HappyPath_RendersStartsPollsMaterializesAndCompletes()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "Report on {{Counterparty.Name}} but keep {json} literal" },
            new Dictionary<string, string?> { ["Counterparty.Name"] = "Acme" });

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("agent output", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("agent output", block.BlockResponse.RawResponse);

        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        Assert.Equal($"{WORKFLOW_EXECUTION_ID}_{block.Id}_w_r1_1", request.ExecutionId);
        Assert.Equal(MODEL, request.Model);
        Assert.Null(request.Effort);
        Assert.Equal(
            "Process the provided documents and produce your deliverables.",
            request.Prompt);
        Assert.Equal(
            "<task_setup>\nAdded automatically by the system.\n- Skills: skills:skill\n</task_setup>",
            request.SystemPrompt);
        Assert.Null(request.OutputSchema);
        AgentRunSkillFile skill = Assert.Single(request.SkillFiles);
        Assert.Equal("skill", skill.Name);
        Assert.Equal("Report on Acme but keep {json} literal", skill.Content);
        Assert.Equal("Handles skill.", skill.Description);
        Assert.DoesNotContain("skill file", request.Prompt, StringComparison.OrdinalIgnoreCase);

        var props = Assert.IsType<AgentProperties>(block.Properties);
        Assert.NotNull(props.ExecutionState);
        Assert.Equal(
            "Report on Acme but keep {json} literal",
            props.ExecutionState.WorkerSkills.Single(skill => skill.Name == "skill").Content);

        Assert.Equal([request.ExecutionId], stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteBlockAsync_SkillAssets_RideTheStartRequestVerbatim()
    {
        const string tagShapedScript = "#!/usr/bin/env python3\nprint(\"{{Counterparty.Name}} and {json}\")";
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "Run the staged script on {{Counterparty.Name}}" },
            new Dictionary<string, string?> { ["Counterparty.Name"] = "Acme" },
            configureProperties: static props => props.WorkerSkills[0].Assets =
                [new AgentWorkerSkillAsset { Name = "verify.py", Content = tagShapedScript }]);

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("out", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        AgentRunSkillFile skill = Assert.Single(request.SkillFiles);
        AgentRunSkillAsset asset = Assert.Single(skill.Assets);
        Assert.Equal("verify.py", asset.Name);
        Assert.Equal(tagShapedScript, asset.Content);

        var props = Assert.IsType<AgentProperties>(block.Properties);
        AgentWorkerSkillAsset stateAsset = Assert.Single(
            props.ExecutionState!.WorkerSkills.Single(rendered => rendered.Name == "skill").Assets!);
        Assert.Equal(tagShapedScript, stateAsset.Content);
    }

    [Fact]
    public async Task ExecuteBlockAsync_FlaggedSkillAsset_HasItsTagsFilled_AndAnUnflaggedOneStaysByteIdentical()
    {
        const string flaggedScript = "FUND = \"{{Counterparty.Name}}\"\nprint(f\"{FUND}\")";
        const string unflaggedScript = "print(\"{{Counterparty.Name}}\")\nprint(f\"{{x}} {{{{y}}}}\")";
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "Run the staged scripts." },
            new Dictionary<string, string?> { ["Counterparty.Name"] = "Acme" },
            configureProperties: static props => props.WorkerSkills[0].Assets =
            [
                new AgentWorkerSkillAsset { Name = "filled.py", Content = flaggedScript, FillTags = true },
                new AgentWorkerSkillAsset { Name = "verbatim.py", Content = unflaggedScript },
            ]);

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("out", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunSkillFile skill = Assert.Single(Assert.Single(stub.StartRequests).SkillFiles);
        AgentRunSkillAsset filled = Assert.Single(skill.Assets, static asset => asset.Name == "filled.py");
        Assert.Equal("FUND = \"Acme\"\nprint(f\"{FUND}\")", filled.Content);
        AgentRunSkillAsset verbatim = Assert.Single(skill.Assets, static asset => asset.Name == "verbatim.py");
        Assert.Equal(unflaggedScript, verbatim.Content);

        var props = Assert.IsType<AgentProperties>(block.Properties);
        AgentWorkerSkillAsset stateAsset = Assert.Single(
            props.ExecutionState!.WorkerSkills.Single(rendered => rendered.Name == "skill").Assets!,
            static asset => asset.Name == "filled.py");
        Assert.True(stateAsset.FillTags);
        Assert.Equal(filled.Content, stateAsset.Content);
        Assert.Equal(flaggedScript, props.WorkerSkills[0].Assets![0].Content);
    }

    [Fact]
    public async Task ExecuteBlockAsync_RendersPromptAndSystemPromptAndCarriesTheSchema()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" },
            new Dictionary<string, string?> { ["Counterparty.Name"] = "Acme" },
            static props =>
            {
                props.PromptTemplate = "Analyse {{Counterparty.Name}} end to end";
                props.SystemPromptTemplate = "You audit {{Counterparty.Name}} only";
                props.OutputSchema = """{"type":"object","properties":{"MarkerCode":{"type":"string"}}}""";
            });

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        Assert.Equal("Analyse Acme end to end", request.Prompt);
        Assert.Equal(
            "<task_setup>\nAdded automatically by the system.\n- Skills: skills:skill\n"
            + "- The final answer is checked against an enforced output schema.\n</task_setup>\n\n"
            + "<instructions>\nYou audit Acme only\n</instructions>",
            request.SystemPrompt);
        Assert.NotNull(request.OutputSchema);
        Assert.NotNull(request.OutputSchema["properties"]?["MarkerCode"]);

        var props = Assert.IsType<AgentProperties>(block.Properties);
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("Analyse Acme end to end", props.ExecutionState.Prompt);
        Assert.Equal("You audit Acme only", props.ExecutionState.SystemPrompt);
    }

    /// <summary>
    /// The redo dispatch seam (D37a): a directive on the context rides the wire ahead of the
    /// member's own prompt with the authored template untouched, and both mode stamps land on
    /// the leg's run-pass row. The composed prompt is written back to the execution state so
    /// the editor's "User prompt after execution" panel keeps telling the truth about the wire.
    /// </summary>
    [Fact]
    public async Task ExecuteBlockAsync_WithARedoDirective_ComposesThePromptAndStampsTheModes()
    {
        const string DIRECTIVE = "Before you begin: address the findings below.";
        const string AUTHORED_TEMPLATE = "Analyse {{Counterparty.Name}} end to end";
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" },
            new Dictionary<string, string?> { ["Counterparty.Name"] = "Acme" },
            static props => props.PromptTemplate = AUTHORED_TEMPLATE);
        context.VerificationRound = 2;
        context.VerificationRedoLeg = new VerificationRedoLeg(
            DIRECTIVE, ConfiguredRedoMode: "Revise", ActualRedoMode: "Scratch");

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        Assert.Equal($"{DIRECTIVE}\n\nAnalyse Acme end to end", request.Prompt);
        Assert.EndsWith("_w_r2_1", request.ExecutionId);

        var props = Assert.IsType<AgentProperties>(block.Properties);
        Assert.Equal(AUTHORED_TEMPLATE, props.PromptTemplate);
        Assert.Equal(request.Prompt, props.ExecutionState!.Prompt);

        AgentRunPass pass = Assert.Single(props.ExecutionState.RunPasses);
        Assert.Equal(2, pass.Round);
        Assert.Equal("Revise", pass.ConfiguredRedoMode);
        Assert.Equal("Scratch", pass.ActualRedoMode);
    }

    /// <summary>
    /// A redo leg always carries a new round, and the round-keyed cleanups leave round 1's
    /// bookkeeping intact: the ledger row survives with its stamps absent, the attempt-output
    /// row survives beside round 2's, and both rounds stay distinct run identities.
    /// </summary>
    [Fact]
    public async Task ExecuteBlockAsync_ARoundTwoRedoLeg_LeavesRoundOnesLedgerAndAttemptRowIntact()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" },
            configureProperties: static props => props.PromptTemplate = "Do the work");
        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("round one out", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        context.VerificationRound = 2;
        context.VerificationRedoLeg = new VerificationRedoLeg(
            "Address the findings.", ConfiguredRedoMode: "Revise", ActualRedoMode: "Scratch");
        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("round two out", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        var props = Assert.IsType<AgentProperties>(block.Properties);
        List<AgentRunPass> ledger = props.ExecutionState!.RunPasses;
        Assert.Equal(2, ledger.Count);

        AgentRunPass roundOne = Assert.Single(ledger, static pass => pass.Round == 1);
        Assert.EndsWith("_w_r1_1", roundOne.ExecutionId);
        Assert.Null(roundOne.ConfiguredRedoMode);
        Assert.Null(roundOne.ActualRedoMode);

        AgentRunPass roundTwo = Assert.Single(ledger, static pass => pass.Round == 2);
        Assert.EndsWith("_w_r2_1", roundTwo.ExecutionId);
        Assert.Equal("Revise", roundTwo.ConfiguredRedoMode);
        Assert.Equal("Scratch", roundTwo.ActualRedoMode);

        Assert.Equal(2, block.AgentRunOutputs.Count);
        Assert.Equal("round one out", Assert.Single(block.AgentRunOutputs, static row => row.Round == 1).Output);
        Assert.Equal("round two out", Assert.Single(block.AgentRunOutputs, static row => row.Round == 2).Output);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithoutARedoDirective_ThePromptAndTheRowAreExactlyTodays()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" },
            new Dictionary<string, string?> { ["Counterparty.Name"] = "Acme" },
            static props => props.PromptTemplate = "Analyse {{Counterparty.Name}} end to end");

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        Assert.Equal("Analyse Acme end to end", request.Prompt);

        var props = Assert.IsType<AgentProperties>(block.Properties);
        AgentRunPass pass = Assert.Single(props.ExecutionState!.RunPasses);
        Assert.Null(pass.ConfiguredRedoMode);
        Assert.Null(pass.ActualRedoMode);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ConfiguredEffort_RidesTheStartRequest()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" },
            configureProperties: static props => props.Effort = "low");

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        Assert.Equal("low", request.Effort);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WhitespaceEffort_OmitsEffortFromTheStartRequest()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" },
            configureProperties: static props => props.Effort = "   ");

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        Assert.Null(request.Effort);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ModelAndEffortAreEngineConfig_NeverTemplated()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" },
            new Dictionary<string, string?> { ["Counterparty.Name"] = "Acme" },
            static props =>
            {
                props.Model = "{Counterparty.Name}";
                props.Effort = "{Counterparty.Name}";
            });

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        Assert.Equal("{Counterparty.Name}", request.Model);
        Assert.Equal("{Counterparty.Name}", request.Effort);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[1, 2]")]
    public async Task ExecuteBlockAsync_BadOutputSchema_FailsBeforeStarting(string schema)
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" },
            configureProperties: props => props.OutputSchema = schema);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains("output schema", exception.Message);
        Assert.Empty(stub.StartRequests);
    }

    [Fact]
    public async Task ExecuteBlockAsync_RenderedDescriptionOverTheCap_FailsBeforeStarting()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" },
            new Dictionary<string, string?> { ["Overflow"] = new string('x', 1401) },
            configureProperties: static props =>
                props.WorkerSkills[0].Description = "Uses {{Overflow}}.");

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains("1400", exception.Message);
        Assert.Empty(stub.StartRequests);
    }

    [Fact]
    public async Task ExecuteBlockAsync_BlankDescription_FailsBeforeStarting()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" },
            configureProperties: static props => props.WorkerSkills[0].Description = "   ");

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains("description", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(stub.StartRequests);
    }

    [Fact]
    public async Task ExecuteBlockAsync_BlockTestWithoutExecutionId_MintsAFreshTestIdentity()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });
        context.WorkflowExecutionId = null;
        context.ExecutionContext = TokenExecutionContext.BlockTest;

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        Assert.StartsWith("BlockTest-", request.ExecutionId);
        Assert.EndsWith($"_{block.Id}_w_r1_1", request.ExecutionId);

        var props = (AgentProperties)block.Properties!;
        AgentRunPass pass = Assert.Single(props.ExecutionState!.RunPasses);
        Assert.Equal(request.ExecutionId, pass.ExecutionId);
        Assert.False(pass.Unacknowledged);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ProductionRunWithoutExecutionId_FailsLoud()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });
        context.WorkflowExecutionId = null;

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains("workflow execution id", exception.Message);
        Assert.Empty(stub.StartRequests);
    }

    [Fact]
    public async Task ExecuteBlockAsync_StagesDocumentNamesOntoTheRuntimeAlphabet()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["notes"] = "work" });
        block.Documents.Add(CreateInMemoryDocument("Invoice (März) #2.pdf"));
        block.Documents.Add(CreateInMemoryDocument("notes"));
        block.Documents.Add(CreateInMemoryDocument("   "));

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        Assert.Equal(
            ["Invoice _M_rz_ _2.pdf", "notes_2", "document"],
            request.Documents.Select(static document => document.Name));
    }

    [Fact]
    public async Task ExecuteBlockAsync_StagedDocumentsCarryExtensionMediaTypeAndClass()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });
        block.Documents.Add(CreateInMemoryDocument("Annual Report 2025.pdf", "Annual Report"));
        block.Documents.Add(CreateInMemoryDocument("Annual Report 2025.pdf", "Annual Report"));

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        Assert.Equal(
            ["Annual Report 2025.pdf", "Annual Report 2025_2.pdf"],
            request.Documents.Select(static document => document.Name));
        Assert.All(request.Documents, static document =>
        {
            Assert.Equal("application/pdf", document.MediaType);
            Assert.Equal("Annual Report", document.DocumentClass);
        });
        Assert.NotNull(request.SystemPrompt);
        Assert.DoesNotContain("Annual Report 2025", request.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteBlockAsync_UnclassifiedOrUnknownDocuments_StageWithoutFabricatedIdentity()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });
        block.Documents.Add(CreateInMemoryDocument("raw.xyz"));
        block.Documents.Add(CreateInMemoryDocument("blank.pdf", string.Empty));

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        AgentRunDocument unknown = request.Documents[0];
        Assert.Equal("raw.xyz", unknown.Name);
        Assert.Equal("application/octet-stream", unknown.MediaType);
        Assert.Null(unknown.DocumentClass);
        AgentRunDocument unclassified = request.Documents[1];
        Assert.Equal("blank.pdf", unclassified.Name);
        Assert.Equal("application/pdf", unclassified.MediaType);
        Assert.Null(unclassified.DocumentClass);
    }

    [Fact]
    public async Task ExecuteBlockAsync_LongDocumentNames_TruncateAndDedupKeepingTheExtension()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });
        block.Documents.Add(CreateInMemoryDocument(new string('a', 240) + ".pdf"));
        block.Documents.Add(CreateInMemoryDocument(new string('a', 240) + ".pdf"));

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        Assert.Equal(2, request.Documents.Count);
        Assert.All(request.Documents, static document => Assert.Equal(200, document.Name.Length));
        Assert.EndsWith(".pdf", request.Documents[0].Name);
        Assert.EndsWith("_2.pdf", request.Documents[1].Name);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ComposedReservedName_GetsTheReservedPrefix()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });
        block.Documents.Add(CreateInMemoryDocument("manifest.json"));

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        Assert.Equal(["doc-manifest.json"], request.Documents.Select(static document => document.Name));
    }

    [Fact]
    public async Task ExecuteBlockAsync_RenamedDocumentAlreadyCarryingItsExtension_DoesNotDoubleIt()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });
        Document renamed = CreateInMemoryDocument("scan.pdf");
        renamed.Name = "Renamed Report.PDF";
        block.Documents.Add(renamed);

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        Assert.Equal(["Renamed Report.PDF"], request.Documents.Select(static document => document.Name));
    }

    [Fact]
    public async Task ExecuteBlockAsync_ScopedClassesWithNoMatchingDocuments_FailsWithoutStartingARun()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["notes"] = "work" });
        block.SupportedDocumentClasses.Add("Annual Report");
        block.Documents.Add(CreateInMemoryDocument("other.pdf", "Lender Report"));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains("Annual Report", exception.Message);
        Assert.Empty(stub.StartRequests);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ScopedClasses_ShipsOnlyMatchingDocuments()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["notes"] = "work" });
        block.SupportedDocumentClasses.Add("Annual Report");
        block.Documents.Add(CreateInMemoryDocument("keep.pdf", "Annual Report"));
        block.Documents.Add(CreateInMemoryDocument("skip.pdf", "Lender Report"));
        block.Documents.Add(CreateInMemoryDocument("noinfo.pdf"));

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        Assert.Equal(["keep.pdf"], request.Documents.Select(static document => document.Name));
    }

    [Fact]
    public async Task ExecuteBlockAsync_NoneClass_StartsWithoutDocuments()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["notes"] = "work" });
        block.SupportedDocumentClasses.Add("None");
        block.Documents.Add(CreateInMemoryDocument("ignored.pdf", "Annual Report"));

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        Assert.Empty(request.Documents);
    }

    [Fact]
    public async Task ExecuteBlockAsync_RunFailure_FailsTheBlockWithTheSubtypeAndStillCompletes()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Failed, pollInterval: null, error: new AgentRunError("errored_result"));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains("errored_result", exception.Message);
        _ = Assert.Single(stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteBlockAsync_SuccessfulRun_CollectsTracesOntoTheBlockThenCompletes()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("agent output", null));
        byte[] envelope = "trace-envelope"u8.ToArray();
        ArrangeTraceManifest(stub, "trace/trace.ndjson", envelope);

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunTraceFile trace = Assert.Single(block.AgentRunTraceFiles);
        Assert.Equal(block.Id, trace.BlockId);
        Assert.Equal(ORGANIZATION_ID, trace.OrganizationId);
        Assert.Equal($"{WORKFLOW_EXECUTION_ID}_{block.Id}_w_r1_1", trace.RunExecutionId);
        Assert.Equal(1, trace.Attempt);
        Assert.Equal("trace/trace.ndjson", trace.Path);
        Assert.Equal(envelope, trace.Content);
        _ = Assert.Single(stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteBlockAsync_FailedRun_StillCollectsTheTraceEvidence()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Failed, pollInterval: null, error: new AgentRunError("errored_result"));
        byte[] envelope = "trace-envelope"u8.ToArray();
        ArrangeTraceManifest(stub, "trace/trace.ndjson", envelope);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        AgentRunTraceFile trace = Assert.Single(block.AgentRunTraceFiles);
        Assert.Equal(block.Id, trace.BlockId);
        Assert.Equal(envelope, trace.Content);
        _ = Assert.Single(stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteBlockAsync_EditorReRun_ReplacesThePriorRunsTraces()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("agent output", null));
        ArrangeTraceManifest(stub, "trace/trace.ndjson", "run-one"u8.ToArray());
        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        byte[] secondEnvelope = "run-two"u8.ToArray();
        ArrangeTraceManifest(stub, "trace/trace.ndjson", secondEnvelope);
        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunTraceFile trace = Assert.Single(block.AgentRunTraceFiles);
        Assert.Equal(secondEnvelope, trace.Content);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ThrowingCollector_WithholdsCompleteInsteadOfDoubleLoss()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });
        BlockExecutionContext throwingContext = context.WithAgentRunExecutor(new AgentRunExecutor(
            stub, new AgentRunPollGate(), NullLogger<AgentRunExecutor>.Instance, new ThrowingCollector()));
        throwingContext.WorkflowExecutionId = WORKFLOW_EXECUTION_ID;
        throwingContext.OrganizationId = ORGANIZATION_ID;
        var priorEvidence = new AgentRunTraceFile { Path = "trace/trace.ndjson", Content = "prior-run"u8.ToArray() };
        block.AgentRunTraceFiles.Add(priorEvidence);

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("agent output", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, throwingContext, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        AgentRunTraceFile kept = Assert.Single(block.AgentRunTraceFiles);
        Assert.Same(priorEvidence, kept);
        Assert.Empty(stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteBlockAsync_UnknownStatus_FailsTheBlockAndWithholdsComplete()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });

        stub.OnGetRun = executionId => Monitor(executionId, AgentRunStatus.Unknown, TimeSpan.Zero);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains("unknown status", exception.Message);
        Assert.Empty(stub.CompletedExecutionIds);
    }

    private sealed class ThrowingCollector : IAgentRunTraceCollector
    {
        public Task<AgentRunTraceCollectionResult> CollectAsync(
            string executionId,
            int attempt,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Collector contract violation");
        }
    }

    [Fact]
    public async Task ExecuteBlockAsync_CollectionFailure_WithholdsCompleteAndTheBlockStillSucceeds()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("agent output", null));
        stub.OnListRunFiles = _ => Error.Unexpected("AgentRun.Unavailable", "listing failed");

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("agent output", block.BlockResponse.RawResponse);
        Assert.Empty(block.AgentRunTraceFiles);
        Assert.Empty(stub.CompletedExecutionIds);

        var props = (AgentProperties)block.Properties!;
        AgentRunPass pass = Assert.Single(props.ExecutionState!.RunPasses);
        Assert.Equal(nameof(AgentRunStatus.Succeeded), pass.Status);
        Assert.True(pass.Unacknowledged);
    }

    [Fact]
    public async Task ExecuteBlockAsync_StartRejection_FailsWithoutCompleting()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });

        stub.OnStartRun = _ => Error.Conflict("AgentRun.PayloadConflict", "same id, different payload");

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains("AgentRun.PayloadConflict", exception.Message);
        Assert.Empty(stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteBlockAsync_DeadlineExceeded_FailsTheBlockAndCompletes()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" },
            configureProperties: static props => props.DeadlineMinutes = 1);

        stub.OnGetRun = executionId => Monitor(executionId, AgentRunStatus.Running, TimeSpan.FromMilliseconds(40));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ShortDeadlineAgentBlockStrategy().ExecuteBlockAsync(
                block, context, TestContext.Current.CancellationToken));

        Assert.Contains("deadline", exception.Message);
        _ = Assert.Single(stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteBlockAsync_PauseForInput_FailsTheBlockAndCompletes()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });

        stub.OnGetRun = executionId => Monitor(executionId, AgentRunStatus.AwaitingInput, TimeSpan.Zero);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains("paused for input", exception.Message);
        _ = Assert.Single(stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteBlockAsync_CancellationDuringPolling_PropagatesAfterTheSettledTeardown()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });

        using var cancellation = new CancellationTokenSource();
        bool cancelSent = false;
        stub.OnGetRun = executionId =>
        {
            if (cancelSent)
            {
                return Monitor(executionId, AgentRunStatus.Cancelled, pollInterval: null) with
                {
                    EstimatedCostUsd = 0.07m,
                };
            }

            cancellation.Cancel();
            return Monitor(executionId, AgentRunStatus.Running, TimeSpan.FromMilliseconds(1));
        };
        stub.OnCancelRun = executionId =>
        {
            cancelSent = true;
            return Monitor(executionId, AgentRunStatus.Cancelling, TimeSpan.FromMilliseconds(1));
        };

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, cancellation.Token));

        _ = Assert.Single(stub.CancelledExecutionIds);
        _ = Assert.Single(stub.CompletedExecutionIds);

        var props = (AgentProperties)block.Properties!;
        AgentRunPass pass = Assert.Single(props.ExecutionState!.RunPasses);
        Assert.Equal(1, pass.Pass);
        Assert.False(string.IsNullOrEmpty(pass.ExecutionId));
        Assert.Equal(nameof(AgentRunStatus.Cancelled), pass.Status);
        Assert.Equal(0.07m, pass.EstimatedCostUsd);
        Assert.False(pass.Unacknowledged);

        var usage = Assert.IsType<TestTokenUsageService>(context.TokenUsageService);
        LlmUsageRecord record = Assert.Single(usage.LlmRecords);
        Assert.Equal(0.07m, Assert.Single(record.Components).CostUsd);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithoutSkills_FailsBeforeStarting()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange([]);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains("at least one worker skill", exception.Message);
        Assert.Empty(stub.StartRequests);
    }

    [Fact]
    public async Task ExecuteBlockAsync_DisabledRuntime_FailsTheBlockDescriptively()
    {
        (Block block, _, _) = Arrange(new Dictionary<string, string> { ["skill"] = "work" });
        BlockExecutionContext disabledContext = _testServiceProvider
            .GetRequiredService<BlockExecutionContext>()
            .WithAgentRuntimeClient(null);
        disabledContext.WorkflowExecutionId = WORKFLOW_EXECUTION_ID;

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, disabledContext, TestContext.Current.CancellationToken));

        Assert.Contains("disabled", exception.Message);
    }

    [Fact]
    public async Task ExecuteBlockAsync_UndeclaredFuses_LeaveEveryFuseOffTheWire()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(executor.Request);
        Assert.Null(executor.Request.DeadlineMs);
        Assert.Null(executor.Request.MaxBudgetUsd);
        Assert.Null(executor.Request.MaxTurns);
        Assert.Null(executor.Request.MaxRetainedOutputBytes);
    }

    [Fact]
    public async Task ExecuteBlockAsync_OverriddenFuses_DeclareTheAuthorsValues()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor(static props =>
            {
                props.DeadlineMinutes = 3;
                props.MaxBudgetUsd = 0.15m;
                props.MaxTurns = 12;
                props.MaxRetainedOutputMib = 4;
            });

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(executor.Request);
        Assert.Equal(0.15m, executor.Request.MaxBudgetUsd);
        Assert.Equal(12, executor.Request.MaxTurns);

        const long DECLARED_THREE_MINUTES_AS_WIRE_MS = 180_000;
        const long DECLARED_FOUR_MIB_AS_WIRE_BYTES = 4_194_304;
        Assert.Equal(DECLARED_THREE_MINUTES_AS_WIRE_MS, executor.Request.DeadlineMs);
        Assert.Equal(DECLARED_FOUR_MIB_AS_WIRE_BYTES, executor.Request.MaxRetainedOutputBytes);
    }

    [Fact]
    public async Task ExecuteBlockAsync_HugeDeadline_RidesTheWireIntactForTheRuntimeToRefuse()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor(static props => props.DeadlineMinutes = 71_583);

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        const long WOULD_WRAP_NEGATIVE_IN_INT_MATH = 4_294_980_000;
        Assert.NotNull(executor.Request);
        Assert.Equal(WOULD_WRAP_NEGATIVE_IN_INT_MATH, executor.Request.DeadlineMs);
    }

    [Fact]
    public async Task ExecuteBlockAsync_OneDeclaredFuse_LeavesTheOthersOffTheWire()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor(static props => props.MaxTurns = 12);

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(executor.Request);
        Assert.Equal(12, executor.Request.MaxTurns);
        Assert.Null(executor.Request.DeadlineMs);
        Assert.Null(executor.Request.MaxBudgetUsd);
        Assert.Null(executor.Request.MaxRetainedOutputBytes);
    }

    [Fact]
    public async Task ExecuteBlockAsync_PollDeadline_IsTheDeclaredRunDeadlinePlusAMargin()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor(static props => props.DeadlineMinutes = 3);
        AgentRuntimeSettings settings = context.PlatformSettings.AgentRuntime;

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(executor.Options);
        Assert.Equal(
            TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(settings.RunDeadlineMarginSeconds),
            executor.Options.Deadline);
    }

    [Fact]
    public async Task ExecuteBlockAsync_UndeclaredDeadline_PollsForTheConsumersOwnPatience()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        AgentRuntimeSettings settings = context.PlatformSettings.AgentRuntime;

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(executor.Options);
        Assert.Equal(TimeSpan.FromMinutes(settings.MaxPollDeadlineMinutes), executor.Options.Deadline);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ExecutionOptions_CarryPatiencePassesAndPollTolerance()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        AgentRuntimeSettings settings = context.PlatformSettings.AgentRuntime;

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(executor.Options);
        Assert.Equal(TimeSpan.FromSeconds(settings.AdmissionPatienceSeconds), executor.Options.AdmissionPatience);
        Assert.Equal(settings.MaxRunPasses, executor.Options.MaxPasses);
        Assert.Equal(settings.PollErrorTolerance, executor.Options.PollErrorTolerance);
        Assert.Equal(
            TimeSpan.FromSeconds(settings.UpstreamErrorRetryDelaySeconds),
            executor.Options.UpstreamErrorRetryDelay);
        Assert.NotNull(executor.Options.ExecutionIdForPass);
        Assert.Equal($"{WORKFLOW_EXECUTION_ID}_{block.Id}_w_r1_2", executor.Options.ExecutionIdForPass(2));
    }

    [Fact]
    public async Task ExecuteBlockAsync_NeutralStoredModel_StartsAndBillsUnderTheAllowlistForm()
    {
        const string bedrockForm = "us.anthropic." + MODEL;
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        executor.OnExecute = static request => new AgentRunConclusion(
            Monitor(request.ExecutionId, AgentRunStatus.Succeeded, null, new AgentRunResult("out", null)),
            TraceFiles: null,
            CompleteWithheld: false,
            Attempts: [new AgentRunAttempt(1, request.ExecutionId, AgentRunStatus.Succeeded, null, 0.05m, Unacknowledged: false)]);
        AgentRuntimeSettings settings = context.PlatformSettings.AgentRuntime;
        List<string> priorAllowlist = settings.AllowedModels;
        settings.AllowedModels = [bedrockForm];
        try
        {
            _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);
        }
        finally
        {
            settings.AllowedModels = priorAllowlist;
        }

        Assert.NotNull(executor.Request);
        Assert.Equal(bedrockForm, executor.Request.Model);
        Assert.Equal(MODEL, ((AgentProperties)block.Properties).Model);
        var usage = Assert.IsType<TestTokenUsageService>(context.TokenUsageService);
        Assert.Equal(bedrockForm, Assert.Single(usage.LlmRecords).ModelId);
    }

    [Theory]
    [InlineData(true, "us.anthropic.claude-opus-5-5")]
    [InlineData(false, "us.anthropic.claude-sonnet-5-5")]
    public async Task ExecuteBlockAsync_BlankModel_StartsAndBillsUnderTheTierModelsAllowlistForm(
        bool shouldUseAdvancedModel, string expectedRunModel)
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        executor.OnExecute = static request => SucceededConclusion(request);
        var props = (AgentProperties)block.Properties;
        props.Model = null;
        props.ShouldUseAdvancedModel = shouldUseAdvancedModel;
        AgentRuntimeSettings settings = context.PlatformSettings.AgentRuntime;
        List<string> priorAllowlist = settings.AllowedModels;
        settings.AllowedModels = ["us.anthropic.claude-opus-5-5", "us.anthropic.claude-sonnet-5-5"];
        try
        {
            _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);
        }
        finally
        {
            settings.AllowedModels = priorAllowlist;
        }

        Assert.NotNull(executor.Request);
        Assert.Equal(expectedRunModel, executor.Request.Model);
        Assert.Null(props.Model);
        var usage = Assert.IsType<TestTokenUsageService>(context.TokenUsageService);
        Assert.Equal(expectedRunModel, Assert.Single(usage.LlmRecords).ModelId);
    }

    [Fact]
    public async Task ExecuteBlockAsync_BlankModel_RunsTheExecutingOrganizationsTierOverride()
    {
        const string foundrySonnet = "claude-sonnet-5-5";
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        executor.OnExecute = static request => SucceededConclusion(request);
        ((AgentProperties)block.Properties).Model = null;
        AgentRuntimeSettings settings = context.PlatformSettings.AgentRuntime;
        Dictionary<string, TenantModelOverride> overrides = context.PlatformSettings.ModelRouting.TenantModelOverrides;
        List<string> priorAllowlist = settings.AllowedModels;
        settings.AllowedModels = ["claude-opus-5-5", foundrySonnet];
        overrides[ORGANIZATION_ID] = new TenantModelOverride { AgentAdvancedModel = "us.anthropic.claude-sonnet-5-5" };
        try
        {
            _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);
        }
        finally
        {
            settings.AllowedModels = priorAllowlist;
            _ = overrides.Remove(ORGANIZATION_ID);
        }

        Assert.NotNull(executor.Request);
        Assert.Equal(foundrySonnet, executor.Request.Model);
    }

    private static AgentRunConclusion SucceededConclusion(AgentRunStartRequest request)
    {
        return new AgentRunConclusion(
            Monitor(request.ExecutionId, AgentRunStatus.Succeeded, null, new AgentRunResult("out", null)),
            TraceFiles: null,
            CompleteWithheld: false,
            Attempts: [new AgentRunAttempt(1, request.ExecutionId, AgentRunStatus.Succeeded, null, 0.05m, Unacknowledged: false)]);
    }

    [Fact]
    public async Task ExecuteBlockAsync_SettledRun_RecordsWhatTheRuntimeSaysItSpent()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        executor.OnExecute = request => new AgentRunConclusion(
            Monitor(request.ExecutionId, AgentRunStatus.Succeeded, null, new AgentRunResult("out", null)),
            TraceFiles: null,
            CompleteWithheld: false,
            Attempts:
            [
                new AgentRunAttempt(1, "run-1", AgentRunStatus.Failed, AgentRunSubtypes.INTERNAL_ERROR, 0.02m, Unacknowledged: false),
                new AgentRunAttempt(2, request.ExecutionId, AgentRunStatus.Succeeded, null, 0.08m, Unacknowledged: false),
            ]);

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        var usage = Assert.IsType<TestTokenUsageService>(context.TokenUsageService);
        LlmUsageRecord record = Assert.Single(usage.LlmRecords);
        Assert.Equal(UsageType.LlmAgentRun, record.UsageType);
        Assert.Equal(BlockTypes.Agent, record.BlockType);
        Assert.Equal(block.Id, record.BlockId);
        Assert.Equal(ORGANIZATION_ID, record.OrganizationId);
        Assert.Equal(MODEL, record.ModelId);
        UsageComponent component = Assert.Single(record.Components);
        Assert.Equal(PricingComponentType.AgentRunSpend, component.ComponentType);
        Assert.Equal(2, component.Units);
        Assert.Equal(0.10m, component.CostUsd);
    }

    [Fact]
    public async Task ExecuteBlockAsync_RedoLeg_MintsRoundKeyedIdentityAndKeepsPriorRoundBookkeeping()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        context.VerificationRound = 2;
        var props = Assert.IsType<AgentProperties>(block.Properties);
        props.ExecutionState!.RunPasses.Add(new AgentRunPass
        {
            Round = 1,
            Pass = 1,
            ExecutionId = $"{WORKFLOW_EXECUTION_ID}_{block.Id}_w_r1_1",
            Status = nameof(AgentRunStatus.Running),
            EstimatedCostUsd = 0.75m,
            Unacknowledged = true,
        });
        props.ExecutionState.RunPasses.Add(new AgentRunPass
        {
            Round = 2,
            Pass = 1,
            ExecutionId = $"{WORKFLOW_EXECUTION_ID}_{block.Id}_w_r2_1",
            Unacknowledged = true,
        });
        props.ExecutionState.RunPasses.Add(new AgentRunPass
        {
            Round = 2,
            Pass = 2,
            ExecutionId = $"{WORKFLOW_EXECUTION_ID}_{block.Id}_w_r2_2",
            Unacknowledged = true,
        });

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(executor.Request);
        Assert.Equal($"{WORKFLOW_EXECUTION_ID}_{block.Id}_w_r2_1", executor.Request!.ExecutionId);
        Assert.Equal($"{WORKFLOW_EXECUTION_ID}_{block.Id}_w_r2_2", executor.Options!.ExecutionIdForPass!(2));

        Assert.Equal(2, props.ExecutionState!.RunPasses.Count);
        AgentRunPass firstRound = Assert.Single(props.ExecutionState.RunPasses, pass => pass.Round == 1);
        Assert.Equal(0.75m, firstRound.EstimatedCostUsd);
        Assert.True(firstRound.Unacknowledged);
        AgentRunPass redo = Assert.Single(props.ExecutionState.RunPasses, pass => pass.Round == 2);
        Assert.False(redo.Unacknowledged);
        Assert.Equal(nameof(AgentRunStatus.Succeeded), redo.Status);
        Assert.DoesNotContain(
            props.ExecutionState.RunPasses,
            pass => pass.ExecutionId == $"{WORKFLOW_EXECUTION_ID}_{block.Id}_w_r2_2");
    }

    [Fact]
    public async Task ExecuteBlockAsync_ASettledRun_RecordsItsAttemptOutputRowAndSettlesTheResponseFromIt()
    {
        (Block block, BlockExecutionContext context, _) = ArrangeWithRecordingExecutor();

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunOutput attempt = Assert.Single(block.AgentRunOutputs);
        Assert.Equal(block.Id, attempt.BlockId);
        Assert.Equal(ORGANIZATION_ID, attempt.OrganizationId);
        Assert.Equal(1, attempt.Round);
        Assert.Equal($"{WORKFLOW_EXECUTION_ID}_{block.Id}_w_r1_1", attempt.RunExecutionId);
        Assert.Equal("out", attempt.Output);
        Assert.NotNull(block.BlockResponse);
        Assert.Equal(attempt.Output, block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_FailedRun_NamesTheRunPassSubtypeAndSpendInTheFailure()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        executor.OnExecute = request => new AgentRunConclusion(
            Monitor(
                request.ExecutionId, AgentRunStatus.Failed, null,
                error: new AgentRunError(AgentRunSubtypes.BUDGET_EXHAUSTED)),
            TraceFiles: null,
            CompleteWithheld: false,
            Attempts: [new AgentRunAttempt(1, request.ExecutionId, AgentRunStatus.Failed, AgentRunSubtypes.BUDGET_EXHAUSTED, 0.5m, Unacknowledged: false)]);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains(AgentRunSubtypes.BUDGET_EXHAUSTED, exception.Message);
        Assert.Contains($"{WORKFLOW_EXECUTION_ID}_{block.Id}_w_r1_1", exception.Message);
        Assert.Contains("pass 1", exception.Message);
        Assert.Contains("0.5", exception.Message);

        var props = Assert.IsType<AgentProperties>(block.Properties);
        Assert.NotNull(props.ExecutionState);
        AgentRunPass pass = Assert.Single(props.ExecutionState.RunPasses);
        Assert.Equal(1, pass.Pass);
        Assert.Equal($"{WORKFLOW_EXECUTION_ID}_{block.Id}_w_r1_1", pass.ExecutionId);
        Assert.Equal(nameof(AgentRunStatus.Failed), pass.Status);
        Assert.Equal(AgentRunSubtypes.BUDGET_EXHAUSTED, pass.FailureSubtype);
        Assert.Equal(0.5m, pass.EstimatedCostUsd);
    }

    [Fact]
    public async Task ExecuteBlockAsync_FailedRun_StillRecordsWhatItSpent()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        executor.OnExecute = request => new AgentRunConclusion(
            Monitor(
                request.ExecutionId, AgentRunStatus.Failed, null,
                error: new AgentRunError(AgentRunSubtypes.BUDGET_EXHAUSTED)),
            TraceFiles: null,
            CompleteWithheld: false,
            Attempts: [new AgentRunAttempt(1, request.ExecutionId, AgentRunStatus.Failed, AgentRunSubtypes.BUDGET_EXHAUSTED, 0.5m, Unacknowledged: false)]);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        var usage = Assert.IsType<TestTokenUsageService>(context.TokenUsageService);
        LlmUsageRecord record = Assert.Single(usage.LlmRecords);
        Assert.Equal(0.5m, Assert.Single(record.Components).CostUsd);
    }

    [Fact]
    public async Task ExecuteBlockAsync_MachineryError_RecordsTheLedgerItCarries()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        executor.OnExecute = request => Error.Failure(
            AgentRunExecutorErrors.DEADLINE_EXCEEDED,
            "ran out of time",
            new Dictionary<string, object>
            {
                [AgentRunExecutorErrors.ATTEMPTS_METADATA_KEY] = new List<AgentRunAttempt>
                {
                    new(1, request.ExecutionId, AgentRunStatus.Running, null, 0.33m, Unacknowledged: false),
                },
            });

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        var usage = Assert.IsType<TestTokenUsageService>(context.TokenUsageService);
        LlmUsageRecord record = Assert.Single(usage.LlmRecords);
        Assert.Equal(0.33m, Assert.Single(record.Components).CostUsd);

        var props = Assert.IsType<AgentProperties>(block.Properties);
        AgentRunPass pass = Assert.Single(props.ExecutionState!.RunPasses);
        Assert.Equal(0.33m, pass.EstimatedCostUsd);
        Assert.Equal(nameof(AgentRunStatus.Running), pass.Status);
    }

    [Fact]
    public async Task ExecuteBlockAsync_MachineryErrorOnALaterPass_NamesTheRunThatActuallyFailed()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        executor.OnExecute = request => Error.Failure(
            AgentRunExecutorErrors.POLL_FAILED,
            "socket closed",
            new Dictionary<string, object>
            {
                [AgentRunExecutorErrors.ATTEMPTS_METADATA_KEY] = new List<AgentRunAttempt>
                {
                    new(1, request.ExecutionId, AgentRunStatus.Failed, AgentRunSubtypes.INTERNAL_ERROR, 0.1m, Unacknowledged: false),
                    new(2, $"{WORKFLOW_EXECUTION_ID}_{block.Id}_w_r1_2", AgentRunStatus.Running, null, 0m, Unacknowledged: false),
                },
            });

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains($"Polling agent run '{WORKFLOW_EXECUTION_ID}_{block.Id}_w_r1_2'", exception.Message);
        Assert.Contains("pass 2", exception.Message);
        Assert.DoesNotContain("_w_r1_1", exception.Message);
    }

    [Fact]
    public async Task ExecuteBlockAsync_MachineryError_AttachesTheTraceEvidenceItCarries()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        executor.OnExecute = request => Error.Failure(
            AgentRunExecutorErrors.POLL_FAILED,
            "socket closed",
            new Dictionary<string, object>
            {
                [AgentRunExecutorErrors.ATTEMPTS_METADATA_KEY] = new List<AgentRunAttempt>
                {
                    new(1, request.ExecutionId, AgentRunStatus.Failed, null, 0.1m, Unacknowledged: false),
                },
                [AgentRunExecutorErrors.TRACE_FILES_METADATA_KEY] = new List<AgentRunTraceFile>
                {
                    new() { Path = "trace/trace.ndjson", Content = [1] },
                },
            });

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        AgentRunTraceFile attached = Assert.Single(block.AgentRunTraceFiles);
        Assert.Equal("trace/trace.ndjson", attached.Path);
    }

    [Fact]
    public async Task ExecuteBlockAsync_CollectedTrace_RecordsTheTokenSplitBesideTheSpend()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        executor.OnExecute = request => new AgentRunConclusion(
            Monitor(request.ExecutionId, AgentRunStatus.Succeeded, null, new AgentRunResult("out", null)),
            TraceFiles:
            [
                UsageTraceFile(
                    request.ExecutionId,
                    UsageLine("us.anthropic.claude-sonnet-4-6-20260101-v1:0", 100, 40, 500, 2000),
                    UsageLine("us.anthropic.claude-haiku-4-5-20251001-v1:0", 7, 3, 0, 0)),
            ],
            CompleteWithheld: false,
            Attempts: [new AgentRunAttempt(1, request.ExecutionId, AgentRunStatus.Succeeded, null, 0.08m, Unacknowledged: false)]);

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        var usage = Assert.IsType<TestTokenUsageService>(context.TokenUsageService);
        Assert.Equal(3, usage.LlmRecords.Count);

        LlmUsageRecord spend = Assert.Single(
            usage.LlmRecords,
            record => record.Components.Any(component => component.ComponentType == PricingComponentType.AgentRunSpend));
        Assert.Equal(0.08m, Assert.Single(spend.Components).CostUsd);
        Assert.NotNull(spend.Metadata);
        Assert.Contains(AgentTokenProvenance.FROM_TRACE, spend.Metadata);

        LlmUsageRecord sonnet = Assert.Single(
            usage.LlmRecords, record => record.ModelId == "claude-sonnet-4-6-20260101");
        Assert.Equal(UsageType.LlmAgentRun, sonnet.UsageType);
        Assert.Equal(BlockTypes.Agent, sonnet.BlockType);
        Assert.Equal(block.Id, sonnet.BlockId);
        Assert.Equal(ORGANIZATION_ID, sonnet.OrganizationId);
        Assert.Equal(100, UnitsOf(sonnet, PricingComponentType.Input));
        Assert.Equal(40, UnitsOf(sonnet, PricingComponentType.Output));
        Assert.Equal(500, UnitsOf(sonnet, PricingComponentType.CacheWrite5m));
        Assert.Equal(2000, UnitsOf(sonnet, PricingComponentType.CacheRead));
        Assert.All(sonnet.Components, static component => Assert.Equal(0m, component.CostUsd));

        LlmUsageRecord haiku = Assert.Single(
            usage.LlmRecords, record => record.ModelId == "claude-haiku-4-5-20251001");
        Assert.Equal(7, UnitsOf(haiku, PricingComponentType.Input));
        Assert.Equal(3, UnitsOf(haiku, PricingComponentType.Output));
    }

    /// <summary>
    /// With a pricing row for every model the run called, the bill is the tokens at the
    /// row's prices, and the runtime's own figure (here far above it) is kept on the spend record's
    /// metadata only. The lookup completes asynchronously, as against the database, and the records
    /// are there when the block returns: the block's service scope ends with it, so billing that
    /// outlived the block lost every record (seen on the first live run).
    /// </summary>
    [Fact]
    public async Task ExecuteBlockAsync_CollectedTraceWithPricingRows_BillsTheTokensNotTheRuntimesFigure()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        var usage = Assert.IsType<TestTokenUsageService>(context.TokenUsageService);
        usage.PricingLookupYields = true;
        usage.Pricings[("anthropic-api", MODEL)] = new ModelPricing
        {
            Provider = "anthropic-api",
            ModelId = MODEL,
            PricingType = PricingType.Llm,
            Components =
            [
                new ModelPricingComponent { ComponentType = PricingComponentType.Input, Cost = 3.00m },
                new ModelPricingComponent { ComponentType = PricingComponentType.Output, Cost = 15.00m },
            ],
        };
        executor.OnExecute = request => new AgentRunConclusion(
            Monitor(request.ExecutionId, AgentRunStatus.Succeeded, null, new AgentRunResult("out", null)),
            TraceFiles: [UsageTraceFile(request.ExecutionId, UsageLine(MODEL, 1_000_000, 100_000, 0, 0))],
            CompleteWithheld: false,
            Attempts: [new AgentRunAttempt(1, request.ExecutionId, AgentRunStatus.Succeeded, null, 40m, Unacknowledged: false)]);

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        LlmUsageRecord spend = Assert.Single(
            usage.LlmRecords,
            record => record.Components.Any(component => component.ComponentType == PricingComponentType.AgentRunSpend));
        Assert.Equal(0m, Assert.Single(spend.Components).CostUsd);
        Assert.Contains(AgentTokenProvenance.COST_FROM_TOKENS, spend.Metadata);
        LlmUsageRecord tokens = Assert.Single(usage.LlmRecords, record => record.ModelId == MODEL && record != spend);
        Assert.Equal(4.50m, tokens.Components.Sum(static component => component.CostUsd));
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithheldTrace_KeepsTheCostAndSaysTheTokensAreUnknown()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        executor.OnExecute = request => new AgentRunConclusion(
            Monitor(request.ExecutionId, AgentRunStatus.Succeeded, null, new AgentRunResult("out", null)),
            TraceFiles: null,
            CompleteWithheld: true,
            Attempts: [new AgentRunAttempt(1, request.ExecutionId, AgentRunStatus.Succeeded, null, 0.08m, Unacknowledged: true)]);

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        var usage = Assert.IsType<TestTokenUsageService>(context.TokenUsageService);
        LlmUsageRecord record = Assert.Single(usage.LlmRecords);
        UsageComponent component = Assert.Single(record.Components);
        Assert.Equal(PricingComponentType.AgentRunSpend, component.ComponentType);
        Assert.Equal(0.08m, component.CostUsd);
        Assert.NotNull(record.Metadata);
        Assert.Contains(AgentTokenProvenance.UNKNOWN, record.Metadata);
    }

    [Fact]
    public async Task ExecuteBlockAsync_MachineryError_RecordsTheTokenSplitOfTheTraceItCarries()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        executor.OnExecute = request => Error.Failure(
            AgentRunExecutorErrors.POLL_FAILED,
            "socket closed",
            new Dictionary<string, object>
            {
                [AgentRunExecutorErrors.ATTEMPTS_METADATA_KEY] = new List<AgentRunAttempt>
                {
                    new(1, request.ExecutionId, AgentRunStatus.Failed, null, 0.1m, Unacknowledged: false),
                },
                [AgentRunExecutorErrors.TRACE_FILES_METADATA_KEY] = new List<AgentRunTraceFile>
                {
                    UsageTraceFile(request.ExecutionId, UsageLine(MODEL, 60, 12, 0, 0)),
                },
            });

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        var usage = Assert.IsType<TestTokenUsageService>(context.TokenUsageService);
        LlmUsageRecord tokens = Assert.Single(
            usage.LlmRecords,
            record => record.Components.Any(component => component.ComponentType == PricingComponentType.Input));
        Assert.Equal(MODEL, tokens.ModelId);
        Assert.Equal(60, UnitsOf(tokens, PricingComponentType.Input));
        Assert.Equal(12, UnitsOf(tokens, PricingComponentType.Output));
    }

    private static int UnitsOf(LlmUsageRecord record, PricingComponentType componentType)
    {
        return record.Components
            .Where(component => component.ComponentType == componentType)
            .Sum(static component => component.Units);
    }

    private static string UsageLine(string model, int input, int output, int cacheCreation, int cacheRead)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["uuid"] = "u",
            ["at"] = "2026-08-19T12:00:00Z",
            ["type"] = AgentTraceRecordTypes.USAGE,
            ["messageId"] = "m",
            ["model"] = model,
            ["usage"] = new Dictionary<string, int>
            {
                ["input_tokens"] = input,
                ["output_tokens"] = output,
                ["cache_creation_input_tokens"] = cacheCreation,
                ["cache_read_input_tokens"] = cacheRead,
            },
        });
    }

    private static AgentRunTraceFile UsageTraceFile(string runExecutionId, params string[] lines)
    {
        return new AgentRunTraceFile
        {
            Path = AgentRunTraceTokens.TRACE_FILE_PATH,
            RunExecutionId = runExecutionId,
            Attempt = 1,
            Content = Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n"),
        };
    }

    [Fact]
    public async Task ExecuteBlockAsync_ZeroCostRun_RecordsNothingRatherThanAnEmptyRow()
    {
        (Block block, BlockExecutionContext context, _) = ArrangeWithRecordingExecutor();

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        var usage = Assert.IsType<TestTokenUsageService>(context.TokenUsageService);
        Assert.Empty(usage.LlmRecords);
    }

    [Fact]
    public async Task ExecuteBlockAsync_SuccessfulStart_FlushesBookkeepingBeforePolling()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });

        var events = new List<string>();
        var flushedPasses = new List<AgentRunPass>();
        stub.OnGetRun = executionId =>
        {
            events.Add("poll");

            return Monitor(
                executionId, AgentRunStatus.Succeeded, pollInterval: null,
                result: new AgentRunResult("agent output", null));
        };
        context.FlushBlockStateAsync = _ =>
        {
            events.Add("flush");
            var state = ((AgentProperties)block.Properties!).ExecutionState!;
            flushedPasses.AddRange(state.RunPasses.Select(static pass => new AgentRunPass
            {
                Pass = pass.Pass,
                ExecutionId = pass.ExecutionId,
                Status = pass.Status,
                Unacknowledged = pass.Unacknowledged,
            }));

            return Task.CompletedTask;
        };

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(["flush", "poll"], events.Take(2));
        AgentRunPass flushed = Assert.Single(flushedPasses);
        Assert.Equal(1, flushed.Pass);
        Assert.Equal($"{WORKFLOW_EXECUTION_ID}_{block.Id}_w_r1_1", flushed.ExecutionId);
        Assert.True(flushed.Unacknowledged);
        Assert.Null(flushed.Status);

        var props = (AgentProperties)block.Properties!;
        AgentRunPass settled = Assert.Single(props.ExecutionState!.RunPasses);
        Assert.Equal(nameof(AgentRunStatus.Succeeded), settled.Status);
        Assert.False(settled.Unacknowledged);
    }

    [Fact]
    public async Task ExecuteBlockAsync_EditorReRun_ReplacesThePriorRunsPasses()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            new Dictionary<string, string> { ["skill"] = "work" });
        context.WorkflowExecutionId = null;
        context.ExecutionContext = TokenExecutionContext.BlockTest;
        Assert.Null(context.FlushBlockStateAsync);

        stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);
        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        var props = (AgentProperties)block.Properties!;
        AgentRunPass pass = Assert.Single(props.ExecutionState!.RunPasses);
        Assert.Equal(stub.StartRequests[^1].ExecutionId, pass.ExecutionId);
        Assert.Equal(2, stub.StartRequests.Count);
    }

    [Fact]
    public async Task ExecuteBlockAsync_JobPath_AnchorsTraceEvidenceToTheBlockAndOrganization()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(executor.Options);
        Assert.NotNull(executor.Options.TraceAnchor);
        Assert.Equal(ORGANIZATION_ID, executor.Options.TraceAnchor.OrganizationId);
        Assert.Equal(block.Id, executor.Options.TraceAnchor.BlockId);
    }

    /// <summary>
    /// The gate that keeps custody from costing anything on the common path: a plain Agent block
    /// is never sent back to redo its work, so its session bundle is left where it is.
    /// </summary>
    [Fact]
    public async Task ExecuteBlockAsync_BlockOutsideAnyVerificationGroup_DeclaresNoSessionBundleAnchor()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(executor.Options);
        Assert.Null(executor.Options.SessionBundleAnchor);
    }

    [Fact]
    public async Task ExecuteBlockAsync_VerificationGroupMember_AnchorsItsSessionBundleToTheBlockAndRound()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        context.BelongsToVerificationGroup = true;
        context.VerificationRound = 2;

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(executor.Options);
        Assert.NotNull(executor.Options.SessionBundleAnchor);
        Assert.Equal(ORGANIZATION_ID, executor.Options.SessionBundleAnchor.OrganizationId);
        Assert.Equal(block.Id, executor.Options.SessionBundleAnchor.BlockId);
        Assert.Equal(2, executor.Options.SessionBundleAnchor.Round);
    }

    /// <summary>
    /// A leg with nowhere to file a pointer row files none, group or not — an editor test runs
    /// against a template block that has no UserData row for the custody row to hang off.
    /// </summary>
    [Fact]
    public async Task ExecuteBlockAsync_GroupMemberInAnEditorTest_DeclaresNoSessionBundleAnchor()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        context.BelongsToVerificationGroup = true;
        context.WorkflowExecutionId = null;
        context.ExecutionContext = TokenExecutionContext.BlockTest;

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(executor.Options);
        Assert.Null(executor.Options.SessionBundleAnchor);
    }

    [Fact]
    public async Task ExecuteBlockAsync_EditorTest_DeclaresNoDurableTraceAnchor()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        context.WorkflowExecutionId = null;
        context.ExecutionContext = TokenExecutionContext.BlockTest;

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(executor.Options);
        Assert.Null(executor.Options.TraceAnchor);
    }

    [Fact]
    public async Task ExecuteBlockAsync_JobPathWithoutAnOrganization_DeclaresNoDurableTraceAnchor()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        context.OrganizationId = null;

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(executor.Options);
        Assert.Null(executor.Options.TraceAnchor);
    }

    [Fact]
    public async Task ExecuteBlockAsync_CancelledRunWithoutSpend_WritesNoUsageRecord()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        executor.OnExecute = request =>
        {
            executor.Options!.OnAttempt!(new AgentRunAttempt(
                1, request.ExecutionId, AgentRunStatus.Cancelled, null, 0m, Unacknowledged: false));
            throw new OperationCanceledException();
        };

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        var usage = Assert.IsType<TestTokenUsageService>(context.TokenUsageService);
        Assert.Empty(usage.LlmRecords);
    }

    [Fact]
    public async Task ExecuteBlockAsync_CancelledRunWithSpend_StillRecordsTheUsage()
    {
        (Block block, BlockExecutionContext context, RecordingAgentRunExecutor executor) =
            ArrangeWithRecordingExecutor();
        executor.OnExecute = request =>
        {
            executor.Options!.OnAttempt!(new AgentRunAttempt(
                1, request.ExecutionId, AgentRunStatus.Cancelled, null, 0.42m, Unacknowledged: false));
            throw new OperationCanceledException();
        };

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        var usage = Assert.IsType<TestTokenUsageService>(context.TokenUsageService);
        LlmUsageRecord record = Assert.Single(usage.LlmRecords);
        Assert.Equal(0.42m, Assert.Single(record.Components).CostUsd);

        var props = Assert.IsType<AgentProperties>(block.Properties);
        AgentRunPass pass = Assert.Single(props.ExecutionState!.RunPasses);
        Assert.Equal(nameof(AgentRunStatus.Cancelled), pass.Status);
        Assert.Equal(0.42m, pass.EstimatedCostUsd);
        Assert.False(pass.Unacknowledged);
    }
}
