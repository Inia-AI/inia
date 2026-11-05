namespace IniaAi.WebApi.Contracts.Responses.ReportFeedback;

/// <summary>
/// One keyset page of feedback items, newest first. <see cref="HasMore"/> signals that older
/// items exist beyond this page; pass <see cref="NextCursor"/> back as the <c>cursor</c> query
/// parameter to fetch them.
/// </summary>
/// <param name="Items">The items on this page.</param>
/// <param name="HasMore">Whether older items exist beyond this page.</param>
/// <param name="NextCursor">The opaque cursor for the next page; null when <paramref name="HasMore"/> is false.</param>
public record ReportFeedbackPageDto(
    IReadOnlyList<ReportFeedbackDto> Items,
    bool HasMore,
    string? NextCursor);
