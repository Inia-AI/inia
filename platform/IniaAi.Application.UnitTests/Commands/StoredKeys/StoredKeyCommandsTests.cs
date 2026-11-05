using ErrorOr;
using FluentValidation.Results;
using IniaAi.Application.Commands.StoredKeys.DeleteStoredKey;
using IniaAi.Application.Commands.StoredKeys.SetStoredKey;
using IniaAi.Application.Queries.StoredKeys.GetStoredKeys;
using IniaAi.Core.Workflows.StoredKeys;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.StoredKeys;
using IniaAi.Domain.StoredKeys;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.StoredKeys;
using IniaAi.Testing.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Commands.StoredKeys;

public class StoredKeyCommandsTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string OTHER_ORG_ID = "org-other";
    private const string USER_ID = "user-admin";
    private const string KEY_NAME = "ParallelAi";
    private const string KEY_VALUE = "fake test value";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly CoreDbContext _context;
    private readonly ICoreUnitOfWork _unitOfWork;
    private readonly IStoredKeyCipher _cipher;

    public StoredKeyCommandsTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _ = services.AddDataProtection().UseEphemeralDataProtectionProvider();
        _ = services.AddStoredKeys();
        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<CoreDbContext>();
        _unitOfWork = _serviceProvider.GetRequiredService<ICoreUnitOfWork>();
        _cipher = _serviceProvider.GetRequiredService<IStoredKeyCipher>();
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Set_ANewName_StoresTheValueEncrypted_AndTheResolverReadsItBack()
    {
        ErrorOr<Success> result = await SetAsync(ORG_ID, KEY_NAME, KEY_VALUE);

        Assert.False(result.IsError);
        OrganizationStoredKey stored = await _context.OrganizationStoredKeys.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ORG_ID, stored.OrganizationId);
        Assert.Equal(USER_ID, stored.UpdatedByUserId);
        Assert.DoesNotContain(KEY_VALUE, stored.ProtectedValue);
        Assert.Equal(KEY_VALUE, await ResolveAsync(ORG_ID, KEY_NAME));
    }

    [Fact]
    public async Task Set_AnExistingName_ReplacesTheValueInPlace()
    {
        _ = await SetAsync(ORG_ID, KEY_NAME, "old-value");

        _ = await SetAsync(ORG_ID, KEY_NAME, KEY_VALUE);

        Assert.Equal(1, await _context.OrganizationStoredKeys.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(KEY_VALUE, await ResolveAsync(ORG_ID, KEY_NAME));
    }

    [Fact]
    public async Task Resolve_TheSameNameInAnotherOrganization_FindsNothing()
    {
        _ = await SetAsync(ORG_ID, KEY_NAME, KEY_VALUE);

        Assert.Null(await ResolveAsync(OTHER_ORG_ID, KEY_NAME));
    }

    [Fact]
    public async Task Delete_RemovesTheRow_SoTheNameResolvesToNothing()
    {
        _ = await SetAsync(ORG_ID, KEY_NAME, KEY_VALUE);

        ErrorOr<Deleted> result = await new DeleteStoredKeyCommandHandler(
                _unitOfWork,
                NullLogger<DeleteStoredKeyCommandHandler>.Instance)
            .Handle(new DeleteStoredKeyCommand(ORG_ID, USER_ID, KEY_NAME), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(0, await _context.OrganizationStoredKeys.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
        Assert.Null(await ResolveAsync(ORG_ID, KEY_NAME));
    }

    [Fact]
    public async Task Delete_ANameTheOrganizationDoesNotHave_IsNotFound()
    {
        _ = await SetAsync(OTHER_ORG_ID, KEY_NAME, KEY_VALUE);

        ErrorOr<Deleted> result = await new DeleteStoredKeyCommandHandler(
                _unitOfWork,
                NullLogger<DeleteStoredKeyCommandHandler>.Instance)
            .Handle(new DeleteStoredKeyCommand(ORG_ID, USER_ID, KEY_NAME), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(KEY_VALUE, await ResolveAsync(OTHER_ORG_ID, KEY_NAME));
    }

    [Fact]
    public async Task GetStoredKeys_ListsOnlyThisOrganizationsNames_ByName()
    {
        _ = await SetAsync(ORG_ID, "SerpApi", "value-b");
        _ = await SetAsync(ORG_ID, KEY_NAME, KEY_VALUE);
        _ = await SetAsync(OTHER_ORG_ID, "OtherKey", "value-c");

        ErrorOr<IReadOnlyList<StoredKeySummary>> result = await new GetStoredKeysQueryHandler(_unitOfWork)
            .Handle(new GetStoredKeysQuery(ORG_ID, USER_ID), TestContext.Current.CancellationToken);

        Assert.Equal([KEY_NAME, "SerpApi"], result.Value.Select(key => key.Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1Password")]
    [InlineData("has space")]
    [InlineData("dots.not.allowed")]
    public void SetValidator_ANameOutsideTheRule_IsRejected(string name)
    {
        ValidationResult result = new SetStoredKeyCommandValidator()
            .Validate(new SetStoredKeyCommand(ORG_ID, USER_ID, name, KEY_VALUE));

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(SetStoredKeyCommand.Name));
    }

    [Fact]
    public async Task Set_AValuePastedWithSpaceAndALineBreak_StoresItTrimmed()
    {
        _ = await SetAsync(ORG_ID, KEY_NAME, $"  {KEY_VALUE}\n");

        Assert.Equal(KEY_VALUE, await ResolveAsync(ORG_ID, KEY_NAME));
    }

    [Fact]
    public void SetValidator_AValueWithALineBreakInside_IsRejected()
    {
        ValidationResult result = new SetStoredKeyCommandValidator()
            .Validate(new SetStoredKeyCommand(ORG_ID, USER_ID, KEY_NAME, "first-line\nsecond-line"));

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(SetStoredKeyCommand.Value));
    }

    [Fact]
    public void SetValidator_AnEmptyValue_IsRejected()
    {
        ValidationResult result = new SetStoredKeyCommandValidator()
            .Validate(new SetStoredKeyCommand(ORG_ID, USER_ID, KEY_NAME, string.Empty));

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(SetStoredKeyCommand.Value));
    }

    private Task<ErrorOr<Success>> SetAsync(string organizationId, string name, string value)
    {
        return new SetStoredKeyCommandHandler(_unitOfWork, _cipher, NullLogger<SetStoredKeyCommandHandler>.Instance)
            .Handle(new SetStoredKeyCommand(organizationId, USER_ID, name, value), TestContext.Current.CancellationToken);
    }

    private Task<string?> ResolveAsync(string organizationId, string name)
    {
        return new StoredKeyResolver(_unitOfWork, _cipher)
            .ResolveAsync(organizationId, name, TestContext.Current.CancellationToken);
    }
}
