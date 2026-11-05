using IniaAi.Core.Workflows.Graph;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Graph;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;

namespace IniaAi.Core.Tests.Workflows.Graphs;

/// <summary>
/// The provenance graph of a stored run is built from the blocks as the database holds them, so a
/// link written only in a block's text input or RAG settings, which live in their own tables, must
/// reach the graph. A real relational database, not a stand-in repository.
/// </summary>
public sealed class GraphBuilderServiceDatabaseTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly string _organizationId;

    public GraphBuilderServiceDatabaseTests()
    {
        _services = new ServiceCollection()
            .AddSqliteTestDatabaseServices()
            .AddScoped<IGraphRepository, GraphRepository>()
            .AddSingleton<IWorkflowGraphBuilder, WorkflowGraphBuilder>()
            .BuildServiceProvider();

        using IServiceScope scope = _services.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        var organization = new Organization("Graph organization");
        _ = context.Organizations.Add(organization);
        _ = context.SaveChanges();
        _organizationId = organization.Id;
    }

    public void Dispose()
    {
        _services.Dispose();
    }

    [Fact]
    public async Task ABlockLinkedOnlyThroughATextInput_HasItsProducerUpstream()
    {
        Workflow clone = Clone(out Section producers, out Section readers);
        Block producer = Merge(producers, "text_source", "source text");
        Block reader = Merge(readers, "reads_text_input", "{question}");
        _ = new TextInput(reader, "question", "Summarise {text_source.answer}");

        WorkflowGraph graph = await BuildGraphAsync(clone);

        Assert.Contains(graph.Edges, edge => edge.SourceId == producer.Id && edge.TargetId == reader.Id);
    }

    [Fact]
    public async Task ABlockLinkedOnlyThroughARagSetting_HasItsProducerUpstream()
    {
        Workflow clone = Clone(out Section producers, out Section readers);
        Block producer = Merge(producers, "identify_notes", "notes pages");
        Block reader = new(BlockTypes.AiQuery, readers, replacementTag: "notes_figure")
        {
            Properties = new AiQueryProperties { UserPromptTemplate = "Read the pension note." },
        };
        _ = new RagSettings(reader) { Type = RagTypes.RagAgent, QueryTemplate = "Find the notes pages listed in {identify_notes.answer}" };

        WorkflowGraph graph = await BuildGraphAsync(clone);

        Assert.Contains(graph.Edges, edge => edge.SourceId == producer.Id && edge.TargetId == reader.Id);
    }

    [Fact]
    public async Task ABlockLinkedThroughItsProperties_StillHasItsProducerUpstream()
    {
        Workflow clone = Clone(out Section producers, out Section readers);
        Block producer = Merge(producers, "plain_source", "plain");
        Block reader = Merge(readers, "reads_properties", "{plain_source.answer}");

        WorkflowGraph graph = await BuildGraphAsync(clone);

        Assert.Contains(graph.Edges, edge => edge.SourceId == producer.Id && edge.TargetId == reader.Id);
    }

    private Workflow Clone(out Section producers, out Section readers)
    {
        var clone = new Workflow { Id = Guid.NewGuid().ToString(), Name = "executed clone", OrganizationId = _organizationId };
        producers = new Section("Producers", clone) { Id = Guid.NewGuid().ToString(), Order = 0 };
        readers = new Section("Readers", clone) { Id = Guid.NewGuid().ToString(), Order = 1 };

        return clone;
    }

    private static Block Merge(Section section, string tag, string template)
    {
        return new Block(BlockTypes.Merge, section, replacementTag: tag)
        {
            Properties = new MergeProperties { TextTemplate = template },
        };
    }

    private async Task<WorkflowGraph> BuildGraphAsync(Workflow clone)
    {
        var execution = new WorkflowExecution { OrganizationId = _organizationId, WorkflowId = clone.Id, WorkflowState = WorkflowState.Draft };

        using (IServiceScope seed = _services.CreateScope())
        {
            UserDataDbContext context = seed.ServiceProvider.GetRequiredService<UserDataDbContext>();
            _ = context.Workflows.Add(clone);
            _ = context.WorkflowExecutions.Add(execution);
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using IServiceScope scope = _services.CreateScope();
        var service = new GraphBuilderService(
            scope.ServiceProvider.GetRequiredService<IGraphRepository>(),
            scope.ServiceProvider.GetRequiredService<IWorkflowGraphBuilder>(),
            scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>(),
            NullLogger<GraphBuilderService>.Instance);

        return await service.GetOrBuildGraphAsync(execution.Id, TestContext.Current.CancellationToken);
    }
}
