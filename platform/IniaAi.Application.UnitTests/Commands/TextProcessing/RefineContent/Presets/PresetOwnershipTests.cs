using ErrorOr;
using IniaAi.Application.Commands.TextProcessing.RefineContent.Presets.DeletePreset;
using IniaAi.Application.Commands.TextProcessing.RefineContent.Presets.UpdatePreset;
using IniaAi.Domain.Entities.Tools;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Models.Responses.ContentRefinement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Commands.TextProcessing.RefineContent.Presets;

/// <summary>
/// Another user's private preset answers as an unknown id does, so its id tells a caller nothing; a
/// shared preset, which the whole organization sees, still answers that only its owner may change it.
/// </summary>
public sealed class PresetOwnershipTests : IDisposable
{
    private const string ORG_ID = "org-presets";
    private const string OWNER_ID = "owner";
    private const string OTHER_USER_ID = "colleague";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;

    public PresetOwnershipTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    [Fact]
    public async Task AnotherUsersPrivatePreset_AnswersLikeAnUnknownId_OnDeleteAndUpdate_AndIsLeftAsItWas()
    {
        RefinementPreset preset = await SeedAsync(isShared: false);

        ErrorOr<Success> deleted = await DeleteAsync(OTHER_USER_ID, preset.Id);
        ErrorOr<Success> unknownDeleted = await DeleteAsync(OTHER_USER_ID, Guid.NewGuid().ToString());
        ErrorOr<RefinementPresetResponse> updated = await UpdateAsync(OTHER_USER_ID, preset.Id);
        ErrorOr<RefinementPresetResponse> unknownUpdated = await UpdateAsync(OTHER_USER_ID, Guid.NewGuid().ToString());

        Assert.Equal(unknownDeleted.FirstError, deleted.FirstError);
        Assert.Equal(unknownUpdated.FirstError, updated.FirstError);
        _context.ChangeTracker.Clear();
        RefinementPreset kept = _context.Set<RefinementPreset>().Single(row => row.Id == preset.Id);
        Assert.False(kept.IsDeleted);
        Assert.Equal("Original", kept.Name);
    }

    [Fact]
    public async Task AnotherUsersSharedPreset_IsRefusedAsNotTheirs()
    {
        RefinementPreset preset = await SeedAsync(isShared: true);

        ErrorOr<Success> deleted = await DeleteAsync(OTHER_USER_ID, preset.Id);

        Assert.Equal(ErrorType.Forbidden, deleted.FirstError.Type);
    }

    private async Task<RefinementPreset> SeedAsync(bool isShared)
    {
        var preset = new RefinementPreset { OrganizationId = ORG_ID, UserId = OWNER_ID, Name = "Original", Instruction = "Shorter.", IsShared = isShared };
        _ = await _context.Set<RefinementPreset>().AddAsync(preset, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();
        return preset;
    }

    private Task<ErrorOr<Success>> DeleteAsync(string userId, string presetId)
    {
        var handler = new DeleteRefinementPresetCommandHandler(_serviceProvider.GetRequiredService<IUserDataUnitOfWork>(), NullLogger<DeleteRefinementPresetCommandHandler>.Instance);
        return handler.Handle(new DeleteRefinementPresetCommand(ORG_ID, userId, presetId), TestContext.Current.CancellationToken);
    }

    private Task<ErrorOr<RefinementPresetResponse>> UpdateAsync(string userId, string presetId)
    {
        var handler = new UpdateRefinementPresetCommandHandler(_serviceProvider.GetRequiredService<IUserDataUnitOfWork>(), NullLogger<UpdateRefinementPresetCommandHandler>.Instance);
        return handler.Handle(new UpdateRefinementPresetCommand(ORG_ID, userId, presetId, "Changed", "Longer.", IsShared: false), TestContext.Current.CancellationToken);
    }
}
