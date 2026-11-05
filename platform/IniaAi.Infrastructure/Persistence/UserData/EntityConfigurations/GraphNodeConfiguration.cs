using IniaAi.Domain.Graph;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.UserData.EntityConfigurations;

public class GraphNodeConfiguration : IEntityTypeConfiguration<GraphNode>
{
    public void Configure(EntityTypeBuilder<GraphNode> builder)
    {
        _ = builder.ToTable("graph_nodes");

        _ = builder.HasKey(n => new { n.RunId, n.Key });
    }
}
