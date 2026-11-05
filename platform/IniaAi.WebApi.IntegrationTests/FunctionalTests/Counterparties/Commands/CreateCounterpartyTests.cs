using ErrorOr;
using IniaAi.Application.Commands.Counterparties.CreateCounterparty;
using IniaAi.Application.Commands.Counterparties.DeleteCounterparty;
using IniaAi.Application.Commands.Counterparties.UpdateCounterparty;
using IniaAi.Application.Queries.Counterparties.GetCounterparty;
using IniaAi.Application.Queries.Counterparties.ListCounterparties;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Counterparty;
using IniaAi.WebApi.IntegrationTests.Builders;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.OData.Edm;

namespace IniaAi.WebApi.IntegrationTests.FunctionalTests.Counterparties.Commands;

/// <summary>
/// Functional tests for full Counterparty CRUD, validation, and conflicts.
/// </summary>
public class CounterpartyTests
{
    private readonly ClientBuilder _apiBuilder;

    /// <summary>
    /// Initializes a new instance of the <see cref="CounterpartyTests"/> class.
    /// </summary>
    public CounterpartyTests()
    {
        _apiBuilder = new ClientBuilder();
    }

    /// <summary>
    /// Verifies that a new counterparty can be created and stored in the database.
    /// </summary>
    [Fact]
    public async Task ShouldCreateCounterparty()
    {
        // Arrange
        string counterpartyTypeId = await GetCounterpartyTypeId();
        CreateCounterpartyCommand command = TestDataFactory.BuildValidCreateCounterparty(id: Guid.NewGuid().ToString(), counterpartyTypeId: counterpartyTypeId);

        // Act
        ErrorOr<CounterpartyDto> created = await _apiBuilder.SendAsync(command);

        // Assert
        Assert.Equal(command.Id, created.Value.Id);

        Counterparty? entity = await _apiBuilder.FindAsync<Counterparty>(command.Id
            ?? throw new Exception("Test setup failure: CreateCounterpartyCommand did not return an ID. Ensure test data is seeded correctly."));
        Assert.NotNull(entity);
        Assert.Equal(command.Id, entity.Id);
        Assert.Equal(command.OrganizationId, entity.OrganizationId);
        Assert.Equal(command.Name, entity.Name);
        Assert.Equal(command.NameVariants, entity.NameVariants);
        Assert.Equal(command.Email, entity.Email);
        Assert.Equal(command.PhoneNumber, entity.PhoneNumber);
        Assert.Equal(command.Website, entity.Website);
        Assert.Equal(command.Address, entity.Address);
        Assert.Equal(command.LastFinancialAnalysisResult, entity.LastFinancialAnalysisResult);
    }

    /// <summary>
    /// Verifies that when no ID is provided, the system generates a new unique ID for the counterparty.
    /// </summary>
    [Fact]
    public async Task ShouldGenerateIdWhenNoneProvided()
    {
        // Arrange
        string counterpartyTypeId = await GetCounterpartyTypeId();
        CreateCounterpartyCommand command = TestDataFactory.BuildValidCreateCounterparty(id: null, counterpartyTypeId: counterpartyTypeId);

        // Act
        ErrorOr<CounterpartyDto> result = await _apiBuilder.SendAsync(command);

        // Assert
        Assert.False(result.IsError);

        CounterpartyDto dto = result.Value;
        Assert.NotEmpty(dto.Id);
        Assert.True(Guid.TryParse(dto.Id, out _), "Generated ID should be a valid Guid");

        Counterparty? entity = await _apiBuilder.FindAsync<Counterparty>(dto.Id);
        Assert.NotNull(entity);
        Assert.Equal(command.Id, entity.Id);
        Assert.Equal(command.OrganizationId, entity.OrganizationId);
        Assert.Equal(command.Name, entity.Name);
        Assert.Equal(command.NameVariants, entity.NameVariants);
        Assert.Equal(command.Email, entity.Email);
        Assert.Equal(command.PhoneNumber, entity.PhoneNumber);
        Assert.Equal(command.Website, entity.Website);
        Assert.Equal(command.Address, entity.Address);
        Assert.Equal(command.LastFinancialAnalysisResult, entity.LastFinancialAnalysisResult);
    }

    private async Task<string> GetCounterpartyTypeId()
    {
        CounterpartyType? result = await _apiBuilder.FindOneAsync<CounterpartyType>(x => x.Name == "Real Money Fund");
        return result?.Id
            ?? throw new Exception("Test setup failure: 'Real Money Fund' CounterpartyType not found in database. Ensure test data is seeded correctly.");
    }

    /// <summary>
    /// Verifies that a created counterparty can be retrieved by its ID.
    /// </summary>
    [Fact]
    public async Task ShouldGetCounterpartyById()
    {
        // Arrange
        string counterpartyTypeId = await GetCounterpartyTypeId();
        CreateCounterpartyCommand command = TestDataFactory.BuildValidCreateCounterparty(id: Guid.NewGuid().ToString(), counterpartyTypeId: counterpartyTypeId);
        _ = await _apiBuilder.SendAsync(command);

        // Act
        ErrorOr<CounterpartyDto> result = await _apiBuilder.SendAsync(new GetCounterpartyQuery(
            command.Id ?? throw new Exception("Test setup failure: CreateCounterpartyCommand did not return an ID. Ensure test data is seeded correctly."),
            command.OrganizationId ?? throw new Exception("Test setup failure: CreateCounterpartyCommand did not return an Organization ID. Ensure test data is seeded correctly.")));

        Assert.False(result.IsError);
        Assert.NotNull(result.Value);

        // Assert
        Assert.Equal(command.Id, result.Value.Id);
        Assert.Equal(command.Name, result.Value.Name);
        Assert.Equal(command.Email, result.Value.Email);
        Assert.Equal(command.NameVariants, result.Value.NameVariants);
        Assert.Equal(command.PhoneNumber, result.Value.PhoneNumber);
        Assert.Equal(command.Website, result.Value.Website);
        Assert.Equal(command.Address, result.Value.Address);
        Assert.Equal(command.LastFinancialAnalysisResult, result.Value.LastFinancialAnalysisResult);
    }

    /// <summary>
    /// Verifies that counterparties can be retrieved in a paginated result.
    /// </summary>
    [Fact]
    public async Task ShouldGetPaginatedCounterparties_WithFakeODataOptions()
    {
        // Arrange
        string counterpartyTypeId = await GetCounterpartyTypeId();
        CreateCounterpartyCommand createCommand = TestDataFactory.BuildValidCreateCounterparty(counterpartyTypeId: counterpartyTypeId);
        ErrorOr<CounterpartyDto> created = await _apiBuilder.SendAsync(createCommand);
        Assert.False(created.IsError);

        // Filter by created ID to avoid pollution from existing data
        IEdmModel model = ODataTestHelper.BuildEdmModelForDto();
        ODataQueryOptions<CounterpartyListItemDto> queryOptions = ODataTestHelper.BuildFakeODataOptions<CounterpartyListItemDto>(model, $"?$top=10&$filter=id in ('{created.Value.Id}')");

        var query = new ListCounterpartiesQuery("IniaAI", "test-user-id", queryOptions);

        // Act
        ErrorOr<ODataPaginatedResponse<CounterpartyListItemDto>> result = await _apiBuilder.SendAsync(query);

        // Assert
        Assert.False(result.IsError);
        Assert.NotNull(result.Value);
        Assert.NotEmpty(result.Value.Value);

        // Assert our specific test item is in the results
        Assert.Contains(result.Value.Value, x => x.Id == created.Value.Id);

        // Assert domain integrity
        Assert.True(result.Value.Value.All(x => !string.IsNullOrWhiteSpace(x.Id)));
        Assert.True(result.Value.Value.All(x => !string.IsNullOrWhiteSpace(x.Name)));
    }

    [Fact]
    public async Task ShouldGetPaginatedCounterparties_WithOrdering()
    {
        // Arrange — use unique names to avoid collisions with existing data
        string counterpartyTypeId = await GetCounterpartyTypeId();
        string suffix = Guid.NewGuid().ToString()[..5];

        CreateCounterpartyCommand first = TestDataFactory.BuildValidCreateCounterparty(counterpartyTypeId: counterpartyTypeId)
            with
        { Name = $"Gamma-{suffix}" };
        CreateCounterpartyCommand second = TestDataFactory.BuildValidCreateCounterparty(counterpartyTypeId: counterpartyTypeId)
            with
        { Name = $"Alpha-{suffix}" };
        CreateCounterpartyCommand third = TestDataFactory.BuildValidCreateCounterparty(counterpartyTypeId: counterpartyTypeId)
            with
        { Name = $"Beta-{suffix}" };

        _ = await _apiBuilder.SendAsync(first);
        _ = await _apiBuilder.SendAsync(second);
        _ = await _apiBuilder.SendAsync(third);

        // Filter by created IDs to avoid pollution from existing data
        IEdmModel model = ODataTestHelper.BuildEdmModelForDto();
        ODataQueryOptions<CounterpartyListItemDto> queryOptions = ODataTestHelper.BuildFakeODataOptions<CounterpartyListItemDto>(model, $"?$top=10&$orderby=name asc&$filter=id in ('{first.Id}','{second.Id}','{third.Id}')");

        var query = new ListCounterpartiesQuery("IniaAI", "test-user-id", queryOptions);

        // Act
        ErrorOr<ODataPaginatedResponse<CounterpartyListItemDto>> result = await _apiBuilder.SendAsync(query);

        // Assert
        Assert.False(result.IsError);
        Assert.NotEmpty(result.Value.Value);

        var names = result.Value.Value.Select(x => x.Name).ToList();
        Assert.Equal([$"Alpha-{suffix}", $"Beta-{suffix}", $"Gamma-{suffix}"], names);
    }

    [Fact]
    public async Task ShouldGetPaginatedCounterparties_WithFiltering()
    {
        // Arrange — use unique names to avoid collisions with existing data
        string counterpartyTypeId = await GetCounterpartyTypeId();
        string suffix = Guid.NewGuid().ToString()[..5];
        string targetName = $"SpecialFilter-{suffix}";
        string noiseName = $"Noise-{suffix}";

        CreateCounterpartyCommand target = TestDataFactory.BuildValidCreateCounterparty(counterpartyTypeId: counterpartyTypeId)
            with
        { Name = targetName };

        CreateCounterpartyCommand other = TestDataFactory.BuildValidCreateCounterparty(counterpartyTypeId: counterpartyTypeId)
            with
        { Name = noiseName };

        _ = await _apiBuilder.SendAsync(target);
        _ = await _apiBuilder.SendAsync(other);

        // Build fake ODataQueryOptions with $filter=Name eq '<targetName>'
        IEdmModel model = ODataTestHelper.BuildEdmModelForDto();
        ODataQueryOptions<CounterpartyListItemDto> queryOptions = ODataTestHelper.BuildFakeODataOptions<CounterpartyListItemDto>(model, $"?$top=10&$orderby=name asc&$filter=Name eq '{targetName}'");
        var query = new ListCounterpartiesQuery("IniaAI", "test-user-id", queryOptions);

        // Act
        ErrorOr<ODataPaginatedResponse<CounterpartyListItemDto>> result = await _apiBuilder.SendAsync(query);

        // Assert
        Assert.False(result.IsError);
        Assert.NotEmpty(result.Value.Value);
        Assert.Single(result.Value.Value, x => x.Name == targetName);
        Assert.DoesNotContain(result.Value.Value, x => x.Name == noiseName);
    }

    /// <summary>
    /// Verifies that an existing counterparty can be updated and persisted.
    /// </summary>
    [Fact]
    public async Task ShouldUpdateCounterparty()
    {
        // Arrange
        string counterpartyTypeId = await GetCounterpartyTypeId();
        CreateCounterpartyCommand command = TestDataFactory.BuildValidCreateCounterparty(id: Guid.NewGuid().ToString(), counterpartyTypeId: counterpartyTypeId);
        _ = await _apiBuilder.SendAsync(command);

        var updateCommand = new UpdateCounterpartyCommand(
            Id: command.Id
                ?? throw new Exception("Test setup failure: CreateCounterpartyCommand did not return an ID. Ensure test data is seeded correctly."),
            OrganizationId: command.OrganizationId,
            ParentId: null,
            CounterpartyTypeIds: command.CounterpartyTypeIds,
            Name: command.Name + " Updated",
            NameVariants: command.NameVariants + ",Updated",
            Email: "updated@example.com",
            PhoneNumber: "+999999999",
            Website: "https://updated.com",
            Address: "Updated Address",
            LastFinancialAnalysisResult: "Updated Result"
        );

        // Act
        _ = await _apiBuilder.SendAsync(updateCommand);

        // Assert
        Counterparty? entity = await _apiBuilder.FindAsync<Counterparty>(command.Id);
        Assert.NotNull(entity);
        Assert.Equal(updateCommand.Name, entity.Name);
        Assert.Equal(updateCommand.NameVariants, entity.NameVariants);
        Assert.Equal(updateCommand.Email, entity.Email);
        Assert.Equal(updateCommand.PhoneNumber, entity.PhoneNumber);
        Assert.Equal(updateCommand.Website, entity.Website);
        Assert.Equal(updateCommand.Address, entity.Address);
        Assert.Equal(updateCommand.LastFinancialAnalysisResult, entity.LastFinancialAnalysisResult);
    }

    /// <summary>
    /// Verifies that a counterparty can be deleted and is removed from the database.
    /// </summary>
    [Fact]
    public async Task ShouldDeleteCounterparty()
    {
        // Arrange
        string counterpartyTypeId = await GetCounterpartyTypeId();
        CreateCounterpartyCommand command = TestDataFactory.BuildValidCreateCounterparty(id: Guid.NewGuid().ToString(), counterpartyTypeId: counterpartyTypeId);
        _ = await _apiBuilder.SendAsync(command);

        // Act
        _ = await _apiBuilder.SendAsync(new DeleteCounterpartyCommand(
            command.Id ?? throw new Exception("Test setup failure: CreateCounterpartyCommand did not return an ID. Ensure test data is seeded correctly."),
            command.OrganizationId ?? throw new Exception("Test setup failure: CreateCounterpartyCommand did not return an Organization ID. Ensure test data is seeded correctly.")));

        // Assert
        Counterparty? entity = await _apiBuilder.FindAsync<Counterparty>(command.Id);
        Assert.Null(entity);
    }

    /// <summary>
    /// Verifies that creating a counterparty with missing required fields throws a validation exception.
    /// </summary>
    [Fact]
    public async Task ShouldRequireMinimumFields()
    {
        // Arrange
        var command = new CreateCounterpartyCommand(null, null, null, null, "", "", null, null, null, null, null);

        // Act
        ErrorOr<CounterpartyDto> result = await _apiBuilder.SendAsync(command);

        // Assert
        Assert.True(result.IsError);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Code == "Validation" || e.Description.Contains("Name"));
    }

    /// <summary>
    /// Verifies that creating two counterparties with the same ID fails due to conflict.
    /// </summary>
    [Fact]
    public async Task ShouldRejectDuplicateId()
    {
        string counterpartyTypeId = await GetCounterpartyTypeId();
        CreateCounterpartyCommand command = TestDataFactory.BuildValidCreateCounterparty(id: Guid.NewGuid().ToString(), counterpartyTypeId: counterpartyTypeId);
        ErrorOr<CounterpartyDto> first = await _apiBuilder.SendAsync(command);
        Assert.False(first.IsError);

        // Act
        ErrorOr<CounterpartyDto> duplicateResult = await _apiBuilder.SendAsync(command);

        // Assert
        Assert.NotNull(command.Id);
        Assert.True(duplicateResult.IsError);
        Assert.Single(duplicateResult.Errors, e =>
            e.Code == "Counterparty.DuplicateId" &&
            e.Description.Contains(command.Id));
    }

    /// <summary>
    /// Verifies that re-creating a counterparty whose ID is held by a soft-deleted record
    /// resurrects that record with the new values instead of failing on the retained primary key.
    /// </summary>
    [Fact]
    public async Task ShouldResurrectSoftDeletedCounterparty_WhenRecreatingWithSameId()
    {
        // Arrange: create, then soft-delete so the row keeps its ID but is hidden by the query filter
        string counterpartyTypeId = await GetCounterpartyTypeId();
        string id = Guid.NewGuid().ToString();
        CreateCounterpartyCommand original = TestDataFactory.BuildValidCreateCounterparty(id: id, counterpartyTypeId: counterpartyTypeId);
        ErrorOr<CounterpartyDto> created = await _apiBuilder.SendAsync(original);
        Assert.False(created.IsError);

        _ = await _apiBuilder.SendAsync(new DeleteCounterpartyCommand(
            id,
            original.OrganizationId ?? throw new Exception("Test setup failure: CreateCounterpartyCommand did not return an Organization ID. Ensure test data is seeded correctly.")));
        Assert.Null(await _apiBuilder.FindAsync<Counterparty>(id));

        // Act: recreate with the same ID and different values
        CreateCounterpartyCommand recreate = TestDataFactory.BuildValidCreateCounterparty(id: id, counterpartyTypeId: counterpartyTypeId);
        ErrorOr<CounterpartyDto> result = await _apiBuilder.SendAsync(recreate);

        // Assert: the deleted record was resurrected with the new values
        Assert.False(result.IsError);
        Assert.Equal(id, result.Value.Id);

        Counterparty? entity = await _apiBuilder.FindAsync<Counterparty>(id);
        Assert.NotNull(entity);
        Assert.False(entity.IsDeleted);
        Assert.Equal(recreate.Name, entity.Name);
        Assert.Equal(recreate.Email, entity.Email);
    }
}
