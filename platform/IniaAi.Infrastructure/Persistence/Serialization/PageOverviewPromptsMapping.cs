using IniaAi.Domain.Entities.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace IniaAi.Infrastructure.Persistence.Serialization;

public static class PageOverviewPromptsMapping
{
    /// <summary>
    /// Stores a document class's overview prompts as one JSON text column, in both the template and
    /// the execution databases. Prompts with no usable text are stored as null.
    /// </summary>
    public static void MapDocumentClassPageOverviews(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentClass>()
            .Property(documentClass => documentClass.PageOverviews)
            .HasConversion(
                prompts => Stored(prompts) == null ? null : JsonSerializer.Serialize(prompts, JsonDefaults.Options),
                json => string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<PageOverviewPrompts>(json, JsonDefaults.Options))
            .Metadata.SetValueComparer(new StoredPromptsComparer());
    }

    private static PageOverviewPrompts? Stored(PageOverviewPrompts? prompts)
    {
        return prompts is { IsEmpty: false } ? prompts : null;
    }

    private static bool AreStoredSame(PageOverviewPrompts? left, PageOverviewPrompts? right)
    {
        return PageOverviewPrompts.AreSame(Stored(left), Stored(right));
    }

    private static int StoredHash(PageOverviewPrompts? prompts)
    {
        return Stored(prompts) is { } stored
            ? HashCode.Combine(stored.Pages?.UserPrompt, stored.SinglePage?.UserPrompt)
            : 0;
    }

    private sealed class StoredPromptsComparer : ValueComparer<PageOverviewPrompts?>
    {
        public StoredPromptsComparer()
            : base(
                (left, right) => AreStoredSame(left, right),
                prompts => StoredHash(prompts),
                prompts => prompts == null ? null : prompts.Clone())
        {
        }

        /// <summary>
        /// EF change detection calls this object overload, and the base one returns before the
        /// equality lambda when either side is null, so a null-loaded class given empty prompts would
        /// count as changed.
        /// </summary>
        public override bool Equals(object? left, object? right)
        {
            return AreStoredSame(left as PageOverviewPrompts, right as PageOverviewPrompts);
        }
    }
}
