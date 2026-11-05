using IniaAi.Domain.Graph;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.UserData.EntityConfigurations;

public class GraphEdgeConfiguration : IEntityTypeConfiguration<GraphEdge>
{
    public void Configure(EntityTypeBuilder<GraphEdge> builder)
    {
        _ = builder.ToTable("graph_edges");

        _ = builder.HasKey(n => new { n.RunId, n.SourceId, n.TargetId });
    }
}
