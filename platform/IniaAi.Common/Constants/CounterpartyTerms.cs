namespace IniaAi.Common.Constants;

/// <summary>
/// The word every screen and every message uses for a counterparty. Code, routes, API fields,
/// error codes and template placeholders keep the name counterparty; only text a person reads
/// takes these, so a rename of the term happens here. The capitalised forms start a sentence or
/// a label; the lower-case forms go mid-sentence.
/// </summary>
public static class CounterpartyTerms
{
    public const string SINGULAR = "Company";

    public const string PLURAL = "Companies";

    public const string SINGULAR_LOWER = "company";

    public const string PLURAL_LOWER = "companies";
}
