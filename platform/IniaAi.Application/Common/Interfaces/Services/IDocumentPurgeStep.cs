namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// One step the soft-delete sweep runs when it hard-deletes documents, for a row that outlives
/// its document by design and must therefore forget the document's content itself. The closed
/// modules' tables are kept in every build, so their steps are registered in every build too
/// (<c>AddKeptTablePurgeSteps</c>); the sweep runs every registered step in the same pass and
/// logs the count under <see cref="Name"/>.
/// </summary>
public interface IDocumentPurgeStep
{
    string Name { get; }

    /// <summary>
    /// What the sweep does with each foreign-key-free row the feature owns: the rows this step
    /// clears, and any the feature keeps whole, so the feature declares all of its rows in one place.
    /// </summary>
    IReadOnlyList<LooseReferenceDisposition> Dispositions { get; }

    Task<int> RunAsync(IQueryable<string> purgedDocumentIds, CancellationToken cancellationToken);
}
