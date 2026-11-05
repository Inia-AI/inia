using IniaAi.Application.Common.OData;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Documents;
using System.Linq.Expressions;

namespace IniaAi.Application.UnitTests.Common.OData;

/// <summary>
/// A date literal in <c>$filter</c> reaches the database as the UTC instant the caller wrote.
/// Read in the server's local time zone, it becomes a local <see cref="DateTime"/> that
/// PostgreSQL refuses, which failed every date filter on every OData list on a server outside UTC.
/// Every list goes through the one helper that reads dates as UTC, and a guard keeps it that way.
/// </summary>
public class ODataQueryTests
{
    private const string HELPER_PATH = "IniaAi.Application/Common/OData/ODataQuery.cs";
    private const string ODATA_QUERY_USING = "using Microsoft.AspNetCore.OData.Query;";
    private const string APPLY_TO_CALL = ".ApplyTo(";
    private const string NEW_QUERY_SETTINGS = "new ODataQuerySettings";
    private const string PROJECT_PATTERN = "IniaAi.*";
    private const string MIGRATIONS_PROJECT_PREFIX = "IniaAi.Migrations.";

    [Fact]
    public void ADateLiteral_ReachesTheQueryAsTheUtcInstantTheCallerWrote()
    {
        IQueryable<DocumentSummaryDto> filtered = new List<DocumentSummaryDto>().AsQueryable().Filter(
            ODataTestOptions.For<DocumentSummaryDto>("?$filter=createdAt ge 2026-09-15T00:00:00Z"));

        DateTime literal = Assert.Single(DateTimeValuesIn(filtered.Expression));
        Assert.Equal(DateTimeKind.Utc, literal.Kind);
        Assert.Equal(new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc), literal);
    }

    [Fact]
    public async Task ATopAboveTheConfiguredMaximum_IsAppliedAsSent()
    {
        IQueryable<DocumentSummaryDto> documents = Enumerable.Range(0, 200)
            .Select(index => new DocumentSummaryDto { Id = $"document-{index:D3}" })
            .AsQueryable();

        ODataPaginatedResponse<DocumentSummaryDto> page = await documents.ToODataPageAsync(
            ODataTestOptions.For<DocumentSummaryDto>("?$top=150&$orderby=id"),
            TestContext.Current.CancellationToken);

        Assert.Equal(150, page.Value.Count);
        Assert.Equal(150, page.Top);
        Assert.Equal("document-149", page.Value[^1].Id);
    }

    [Fact]
    public void EveryODataList_AppliesItsQueryOptionsThroughTheHelper()
    {
        string repoRoot = RepositoryRoot.Find();

        List<string> direct = [.. Directory
            .EnumerateDirectories(repoRoot, PROJECT_PATTERN)
            .Where(project => IsProductionProject(Path.GetFileName(project)))
            .SelectMany(project => Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .Where(path => path != HELPER_PATH && AppliesODataQueryOptions(System.IO.File.ReadAllText(Path.Combine(repoRoot, path))))
            .Order()];

        Assert.True(
            direct.Count == 0,
            "These files apply OData query options themselves instead of through ODataQuery, so their date filters read in the server's time zone:\n  "
            + string.Join("\n  ", direct));
    }

    private static bool AppliesODataQueryOptions(string source)
    {
        return source.Contains(ODATA_QUERY_USING, StringComparison.Ordinal)
            && (source.Contains(APPLY_TO_CALL, StringComparison.Ordinal) || source.Contains(NEW_QUERY_SETTINGS, StringComparison.Ordinal));
    }

    private static bool IsProductionProject(string projectName)
    {
        return !projectName.EndsWith("Tests", StringComparison.Ordinal)
            && !projectName.StartsWith(MIGRATIONS_PROJECT_PREFIX, StringComparison.Ordinal);
    }

    private static List<DateTime> DateTimeValuesIn(Expression expression)
    {
        var collector = new DateTimeCollector();
        _ = collector.Visit(expression);
        return collector.Values;
    }

    private sealed class DateTimeCollector : ExpressionVisitor
    {
        public List<DateTime> Values { get; } = [];

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Type == typeof(DateTime) && node.Expression is ConstantExpression)
            {
                Values.Add(Expression.Lambda<Func<DateTime>>(node).Compile()());
                return node;
            }

            return base.VisitMember(node);
        }
    }
}
