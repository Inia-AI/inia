using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Services.Documents;
using IniaAi.Services.Jobs;
using IniaAi.WebApp.Services;

namespace IniaAi.WebApp.Models;

/// <summary>The services a designer page gives its editor context once, for every test run's document load.</summary>
public sealed record EditorDocumentServices(
    ILogger Logger,
    AppState AppState,
    DocumentProcessingService DocumentProcessing,
    EmailIntakeJobStage EmailIntakeStage,
    IUserDataUnitOfWork UserDataUnitOfWork);
