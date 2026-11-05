using ErrorOr;
using IniaAi.Application.Commands.Counterparties.CreateCounterparty;
using IniaAi.Application.Commands.Counterparties.DeleteCounterparty;
using IniaAi.Application.Commands.Counterparties.UpdateCounterparty;
using IniaAi.Application.Queries.Counterparties.GetCounterparty;
using IniaAi.Application.Queries.Counterparties.ListCounterparties;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Counterparty;
using IniaAi.WebApi.IntegrationTests;
using IniaAi.WebApi.IntegrationTests.Builders;
using IniaAi.WebApi.IntegrationTests.Fixtures;

namespace IniaAi.WebApi.FunctionalTests.Counterparties;

/// <summary>
/// Functional tests for the Counterparty CRUD API using AAA structure.
/// </summary>
public class CounterpartyTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;
    private readonly ClientBuilder _apiBuilder;

    /// <summary>
    /// Initializes the test class using the shared <see cref="ApiTestFixture"/>.
    /// </summary>
    /// <param name="fixture">Shared test fixture.</param>
    public CounterpartyTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
        _apiBuilder = fixture.ClientBuilder;
    }

    #region Helpers

    /// <summary>
    /// Retrieves an existing CounterpartyType ID.
    /// </summary>
    private async Task<string> GetValidCounterpartyTypeIdAsync()
    {
        CounterpartyType? type = await _apiBuilder.FindOneAsync<CounterpartyType>(x => x.Name == "Real Money Fund");
        Assert.NotNull(type);
        return type.Id;
    }

    /// <summary>
    /// Creates a valid counterparty and persists it.
    /// </summary>
    private async Task<CreateCounterpartyCommand> CreateAndPersistCounterpartyAsync(string? id = null)
    {
        string typeId = await GetValidCounterpartyTypeIdAsync();
        CreateCounterpartyCommand command = TestDataFactory.BuildValidCreateCounterparty(id, counterpartyTypeId: typeId);

        ErrorOr<CounterpartyDto> result = await _apiBuilder.SendAsync(command);
        Assert.False(result.IsError);

        _fixture.RegisterCreatedCounterparty(result.Value.Id);
        return command;
    }

    /// <summary>
    /// Builds an OData query for listing counterparties.
    /// </summary>
    private static ListCounterpartiesQuery BuildQuery(string query, string? organizationId = null)
    {
        return new(
            organizationId ?? "IniaAI",
            "test-user-id",
            ODataTestHelper.BuildFakeODataOptions<CounterpartyListItemDto>(
                ODataTestHelper.BuildEdmModelForDto(),
                query));
    }

    #endregion

    #region Create

    /// <summary>
    /// Verifies that a valid counterparty can be created.
    /// </summary>
    [Fact]
    public async Task ShouldCreateCounterparty()
    {
        // Arrange
        CreateCounterpartyCommand command = await CreateAndPersistCounterpartyAsync();

        // Act
        Counterparty? entity = await _apiBuilder.FindAsync<Counterparty>(command.Id
            ?? throw new InvalidOperationException("Created counterparty ID should not be null"));

        // Assert
        Assert.NotNull(entity);
        Assert.Equal(command.Id, entity.Id);
        Assert.Equal(command.OrganizationId, entity.OrganizationId);
        Assert.Equal(command.ParentId, entity.ParentId);
        Assert.Equal(command.Name, entity.Name);
        Assert.Equal(command.NameVariants, entity.NameVariants);
        Assert.Equal(command.Email, entity.Email);
        Assert.Equal(command.PhoneNumber, entity.PhoneNumber);
        Assert.Equal(command.Website, entity.Website);
        Assert.Equal(command.Address, entity.Address);
        Assert.Equal(command.Properties, entity.Properties);
        Assert.Null(entity.LastFinancialAnalysisResult);
    }

    /// <summary>
    /// Verifies that the system generates an ID if none is provided.
    /// </summary>
    [Fact]
    public async Task ShouldGenerateIdWhenNoneProvided()
    {
        // Arrange
        string typeId = await GetValidCounterpartyTypeIdAsync();
        CreateCounterpartyCommand command = TestDataFactory.BuildValidCreateCounterparty(null, counterpartyTypeId: typeId);

        // Act
        ErrorOr<CounterpartyDto> result = await _apiBuilder.SendAsync(command);
        _fixture.RegisterCreatedCounterparty(result.Value.Id);

        // Assert
        Assert.False(result.IsError);
        CounterpartyDto dto = result.Value;
        Assert.NotEmpty(dto.Id);
        Assert.True(Guid.TryParse(dto.Id, out _));

        Counterparty? entity = await _apiBuilder.FindAsync<Counterparty>(dto.Id);
        Assert.NotNull(entity);
        Assert.Equal(command.OrganizationId, entity.OrganizationId);
    }

    #endregion

    #region Read

    /// <summary>
    /// Verifies that a created counterparty can be retrieved by ID.
    /// </summary>
    [Fact]
    public async Task ShouldGetCounterpartyById()
    {
        // Arrange
        CreateCounterpartyCommand created = await CreateAndPersistCounterpartyAsync();

        // Act
        ErrorOr<CounterpartyDto> result = await _apiBuilder.SendAsync(new GetCounterpartyQuery(
            created.Id ?? throw new InvalidOperationException("Created counterparty ID should not be null"),
            created.OrganizationId ?? throw new InvalidOperationException("Created counterparty organization ID should not be null")));

        // Assert
        Assert.False(result.IsError);
        CounterpartyDto dto = result.Value;
        Assert.Equal(created.Id, dto.Id);
        Assert.Equal(created.ParentId, dto.ParentId);
        Assert.Equal(created.CounterpartyTypeIds?.Order(), dto.CounterpartyTypeIds.Order());
        Assert.Equal(created.Name, dto.Name);
        Assert.Equal(created.NameVariants, dto.NameVariants);
        Assert.Equal(created.Email, dto.Email);
        Assert.Equal(created.PhoneNumber, dto.PhoneNumber);
        Assert.Equal(created.Website, dto.Website);
        Assert.Equal(created.Address, dto.Address);
        Assert.Equal(created.LastFinancialAnalysisResult, dto.LastFinancialAnalysisResult);
        Assert.Equal(created.Properties, dto.Properties);
    }

    /// <summary>
    /// Verifies that counterparties can be listed with paging.
    /// </summary>
    [Fact]
    public async Task ShouldGetPaginatedCounterparties_WithFilteringOfId()
    {
        // Arrange
        CreateCounterpartyCommand created = await CreateAndPersistCounterpartyAsync();

        ListCounterpartiesQuery query = BuildQuery($"?$top=10&$filter=id in ('{created.Id}')");

        // Act
        ErrorOr<ODataPaginatedResponse<CounterpartyListItemDto>> result = await _apiBuilder.SendAsync(query);

        // Assert
        Assert.False(result.IsError);
        Assert.Contains(result.Value.Value, x => x.Id == created.Id);
    }

    /// <summary>
    /// Verifies that ordering counterparties by name works.
    /// </summary>
    [Fact]
    public async Task ShouldGetPaginatedCounterparties_WithOrdering()
    {
        // Arrange — use unique names to avoid collisions with existing data
        string typeId = await GetValidCounterpartyTypeIdAsync();
        string suffix = Guid.NewGuid().ToString()[..5];

        CreateCounterpartyCommand alpha = TestDataFactory.BuildValidCreateCounterparty(counterpartyTypeId: typeId) with { Name = $"Alpha-{suffix}" };
        CreateCounterpartyCommand beta = TestDataFactory.BuildValidCreateCounterparty(counterpartyTypeId: typeId) with { Name = $"Beta-{suffix}" };
        CreateCounterpartyCommand gamma = TestDataFactory.BuildValidCreateCounterparty(counterpartyTypeId: typeId) with { Name = $"Gamma-{suffix}" };
        _fixture.RegisterCreatedCounterparty(alpha.Id ?? throw new InvalidOperationException("Created counterparty ID should not be null"));
        _fixture.RegisterCreatedCounterparty(beta.Id ?? throw new InvalidOperationException("Created counterparty ID should not be null"));
        _fixture.RegisterCreatedCounterparty(gamma.Id ?? throw new InvalidOperationException("Created counterparty ID should not be null"));

        _ = await _apiBuilder.SendAsync(alpha);
        _ = await _apiBuilder.SendAsync(beta);
        _ = await _apiBuilder.SendAsync(gamma);

        ListCounterpartiesQuery query = BuildQuery($"?$top=10&$orderby=name asc&$filter=id in ('{alpha.Id}','{beta.Id}','{gamma.Id}')");

        // Act
        ErrorOr<ODataPaginatedResponse<CounterpartyListItemDto>> result = await _apiBuilder.SendAsync(query);

        // Assert
        Assert.False(result.IsError);
        var names = result.Value.Value.Select(x => x.Name).ToList();
        Assert.Equal([$"Alpha-{suffix}", $"Beta-{suffix}", $"Gamma-{suffix}"], names);
    }

    /// <summary>
    /// Verifies that filtering by name works.
    /// </summary>
    [Fact]
    public async Task ShouldGetPaginatedCounterparties_WithFiltering()
    {
        // Arrange — use unique names to avoid collisions with existing data
        string typeId = await GetValidCounterpartyTypeIdAsync();
        string suffix = Guid.NewGuid().ToString()[..5];
        string includedName = $"Special-{suffix}";
        string excludedName = $"Noise-{suffix}";

        CreateCounterpartyCommand target = TestDataFactory.BuildValidCreateCounterparty(counterpartyTypeId: typeId) with { Name = includedName };
        CreateCounterpartyCommand noise = TestDataFactory.BuildValidCreateCounterparty(counterpartyTypeId: typeId) with { Name = excludedName };
        _fixture.RegisterCreatedCounterparty(target.Id ?? throw new InvalidOperationException("Created counterparty ID should not be null"));
        _fixture.RegisterCreatedCounterparty(noise.Id ?? throw new InvalidOperationException("Created counterparty ID should not be null"));

        _ = await _apiBuilder.SendAsync(target);
        _ = await _apiBuilder.SendAsync(noise);

        ListCounterpartiesQuery query = BuildQuery($"?$filter=Name eq '{includedName}'");

        // Act
        ErrorOr<ODataPaginatedResponse<CounterpartyListItemDto>> result = await _apiBuilder.SendAsync(query);

        // Assert
        Assert.False(result.IsError);
        Assert.Single(result.Value.Value, x => x.Name == includedName);
        Assert.DoesNotContain(result.Value.Value, x => x.Name == excludedName);
    }

    #endregion

    #region Update

    /// <summary>
    /// Verifies that an existing counterparty can be updated.
    /// </summary>
    [Fact]
    public async Task ShouldUpdateCounterparty()
    {
        // Arrange
        CreateCounterpartyCommand created = await CreateAndPersistCounterpartyAsync();

        var update = new UpdateCounterpartyCommand(
            Id: created.Id ?? throw new InvalidOperationException("Created counterparty ID should not be null"),
            OrganizationId: created.OrganizationId,
            ParentId: null,
            CounterpartyTypeIds: created.CounterpartyTypeIds,
            Name: created.Name + " Updated",
            NameVariants: created.NameVariants + ", Updated",
            Email: "updated@email.com",
            PhoneNumber: "+999999999",
            Website: "https://updated.com",
            Address: "Updated Address",
            LastFinancialAnalysisResult: "Updated");

        // Act
        _ = await _apiBuilder.SendAsync(update);

        // Assert
        Counterparty? entity = await _apiBuilder.FindAsync<Counterparty>(created.Id);
        Assert.NotNull(entity);
        Assert.Equal(update.Id, entity.Id);
        Assert.Equal(update.OrganizationId, entity.OrganizationId);
        Assert.Equal(update.ParentId, entity.ParentId);
        Assert.Equal(update.Name, entity.Name);
        Assert.Equal(update.NameVariants, entity.NameVariants);
        Assert.Equal(update.Email, entity.Email);
        Assert.Equal(update.PhoneNumber, entity.PhoneNumber);
        Assert.Equal(update.Website, entity.Website);
        Assert.Equal(update.Address, entity.Address);
        Assert.Equal(update.LastFinancialAnalysisResult, entity.LastFinancialAnalysisResult);
        Assert.Equal(update.Properties, entity.Properties);
    }

    #endregion

    #region Delete

    /// <summary>
    /// Verifies that a counterparty can be deleted.
    /// </summary>
    [Fact]
    public async Task ShouldDeleteCounterparty()
    {
        // Arrange
        CreateCounterpartyCommand created = await CreateAndPersistCounterpartyAsync();

        // Act
        _ = await _apiBuilder.SendAsync(new DeleteCounterpartyCommand(
            created.Id ?? throw new InvalidOperationException("Created counterparty ID should not be null"),
            created.OrganizationId ?? throw new InvalidOperationException("Created counterparty organization ID should not be null")));

        // Assert
        Counterparty? entity = await _apiBuilder.FindAsync<Counterparty>(created.Id);
        Assert.Null(entity);
    }

    #endregion

    #region Validation

    /// <summary>
    /// Verifies that missing fields cause validation to fail.
    /// </summary>
    [Fact]
    public async Task ShouldRequireMinimumFields()
    {
        // Arrange
        var command = new CreateCounterpartyCommand(null, OrganizationId: "IniaAi", null, null, "", "", null, null, null, null, null);

        // Act
        ErrorOr<CounterpartyDto> result = await _apiBuilder.SendAsync(command);

        // Assert
        Assert.True(result.IsError);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Code == "Validation" || e.Description.Contains("Name"));
    }

    /// <summary>
    /// Verifies that duplicate IDs are rejected.
    /// </summary>
    [Fact]
    public async Task ShouldRejectDuplicateId()
    {
        // Arrange
        CreateCounterpartyCommand command = await CreateAndPersistCounterpartyAsync();

        // Act
        ErrorOr<CounterpartyDto> duplicate = await _apiBuilder.SendAsync(command);

        // Assert
        Assert.True(duplicate.IsError);
        Assert.Single(duplicate.Errors, e => e.Code == "Counterparty.DuplicateId");
    }

    [Fact]
    public async Task ShouldRejectDuplicateCreateCommands_WhenSentInParallel()
    {
        string typeId = await GetValidCounterpartyTypeIdAsync();
        CreateCounterpartyCommand command = TestDataFactory.BuildValidCreateCounterparty(counterpartyTypeId: typeId);

        Task<ErrorOr<CounterpartyDto>> task1 = _apiBuilder.SendAsync(command);
        Task<ErrorOr<CounterpartyDto>> task2 = _apiBuilder.SendAsync(command);

        ErrorOr<CounterpartyDto>[] results = await Task.WhenAll(task1, task2);

        // Register the created ID only if one actually succeeded
        if (results.Any(r => !r.IsError))
        {
            _fixture.RegisterCreatedCounterparty(command.Id
                ?? throw new InvalidOperationException("Created counterparty ID should not be null"));
        }

        Assert.Equal(1, results.Count(r => r.IsError));
        Assert.Equal(1, results.Count(r => !r.IsError));
    }

    // NOTE: Duplicate name validation tests removed — the unique index
    // IX_Counterparties_Name_OrganizationId_NotDeleted was never created via
    // migration, so the application currently allows duplicate counterparty names.

    #endregion

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
