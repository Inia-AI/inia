using IniaAi.AiConnection.AwsBedrock;
using IniaAi.Core.Services;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.XunitAttributes;

namespace IniaAi.Core.Tests.Services;

public class ReconciliationServiceTests : IClassFixture<DocumentsFixture>
{
    private const string MODEL_JUDGES_THE_MATCH = "The model judges the match";

    private readonly ITestOutputHelper _testOutputHelper;
    private readonly DocumentsFixture _fixture;
    private readonly ReconciliationService _reconciliationService;

    public ReconciliationServiceTests(
        DocumentsFixture fixture,
        ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _fixture = fixture;
        _reconciliationService = new ReconciliationService(
            _fixture.PlatformSettings.IniaAi,
            TestsLogger.CreateLogger<ReconciliationService>(_testOutputHelper),
            _fixture.GetChatConnector(_testOutputHelper),
            new ServiceScopeFactory());
    }

    [PaidApiFact(MODEL_JUDGES_THE_MATCH, alsoInCi: true)]
    public async Task FindMatchFromListAsync_WithValidInput_ReturnsNonEmptyAnswerAsync()
    {
        // Arrange
        string identifiedName = "John Doe";
        List<string> potentialNames =
        [
            "John Doe",
            "Jonathan Doe",
            "Jane Doe"
        ];

        // Act
        string? result = await _reconciliationService.FindMatchFromListAsync(
            identifiedName,
            potentialNames,
            ModelFamily.Anthropic);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(result));
        IEnumerable<string> expectedOptions = potentialNames.Append("NA");
        Assert.Contains(result, expectedOptions);
    }
}
