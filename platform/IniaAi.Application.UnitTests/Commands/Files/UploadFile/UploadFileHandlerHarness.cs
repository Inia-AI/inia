using ErrorOr;
using IniaAi.Application.Commands.Files.UploadFile;
using IniaAi.Application.Common.UploadLimits;
using IniaAi.Application.Documents;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.Documents;
using IniaAi.WebApi.Contracts.Dto;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace IniaAi.Application.UnitTests.Commands.Files.UploadFile;

/// <summary>
/// The API mouth against a real database, with the refusal recorder of the caller's choice, so
/// the platform's logging trail and the screening record are tested against the same handler.
/// </summary>
internal sealed class UploadFileHandlerHarness : IDisposable
{
    public const string ORGANIZATION_ID = "org-upload";
    public const string USER_ID = "user-upload";
    public const int MAX_FILE_SIZE_MB = 1;

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UploadFileCommandHandler _handler;

    public UploadFileHandlerHarness(IIntakeRefusalRecorder refusalRecorder)
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _serviceProvider = services.BuildServiceProvider();
        Context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        _ = FileStorageManager.GetProviderForTenant(Arg.Any<string>()).Returns(StorageProvider.Database);
        _ = FileStorageManager.StoreFileAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>())
            .Returns(callInfo => callInfo.ArgAt<string>(3));

        var intakeGate = new DocumentIntakeGate(
            refusalRecorder,
            FileFormatsUnderTest.Unlocker,
            FileFormatsUnderTest.ReadableFormats,
            Options.Create(new PlatformSettings { IniaAi = new IniaAiSettings { MaxFileSizeMb = MAX_FILE_SIZE_MB } }));

        _handler = new UploadFileCommandHandler(
            _serviceProvider.GetRequiredService<IUserDataUnitOfWork>(),
            NullLogger<UploadFileCommandHandler>.Instance,
            FileStorageManager,
            intakeGate,
            new NoUploadLimits());
    }

    public UserDataDbContext Context { get; }

    public IFileStorageManager FileStorageManager { get; } = Substitute.For<IFileStorageManager>();

    public async Task<ErrorOr<FileDto>> HandleAsync(byte[] content, string fileName, string? password = null)
    {
        return await HandleAsync(FormFileTestHelper.CreateFromBytes(content, fileName), password);
    }

    public async Task<ErrorOr<FileDto>> HandleAsync(IFormFile file, string? password = null)
    {
        return await _handler.Handle(new UploadFileCommand(file, ORGANIZATION_ID, USER_ID, FilePassword: password), TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }
}
