namespace IniaAi.Core.Rag;

/// <summary>
/// A filter rule that ranks pages by embeddings cannot rank: the platform has no Cohere API key, or
/// no page of the documents has an embedding of its overview.
/// </summary>
public sealed class EmbeddingsUnavailableException(bool keyMissing) : InvalidOperationException(keyMissing
    ? $"The filter rule of this block picks pages by meaning, which needs a Cohere API key, and the platform has none. Ask the administrator to add the key, or {OTHER_RULES}."
    : $"The filter rule of this block picks pages by meaning, and no page of its documents has a page overview to compare. Give their document class an overview prompt and run the report again, or {OTHER_RULES}.")
{
    private const string OTHER_RULES = "choose a filter rule that reads the whole document, exact pages or the table of contents";
}
