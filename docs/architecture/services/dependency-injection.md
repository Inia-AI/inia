---
type: explanation
status: current
description: "The DI registration entry points of each layer and host, and the registration patterns in use (keyed storage and workflow handlers, conditional connectors, options). Read when you register a new service or look for where an existing one is registered."
---

# Dependency Injection

The platform uses the built-in `Microsoft.Extensions.DependencyInjection` container. Registration is split per layer, each exposing an `IServiceCollection` extension method that each host composes at startup: the API in `platform/IniaAi.WebApi/Program.cs` and `ApplicationServicesConfiguration`, the admin app in `platform/IniaAi.WebApp/ServicesConfigurator.cs`.

## Registration entry points

- `AddApplication` — `platform/IniaAi.Application/DependencyInjection.cs`. Registers the in-house request dispatcher (`AddRequestDispatching`: exactly one handler per request, scanned from the Application assembly, with the validation step for `ErrorOr` responses), all FluentValidation validators, and application services such as `WorkflowCanonicalizer`, `WorkflowDiffService`, `WorkflowSavePipeline` and the editor agent's services, then `AddSeamDefaults` for the seams a closed module fills.
- `AddInfrastructure` — `platform/IniaAi.Infrastructure/InfrastructureServiceRegistration.cs`. Binds settings (`FileStorageSettings` from `FileStorage`, `GitHubSettings`), calls `AddOutgoingMail` (the `SmtpSettings` and `SendGridSettings` and the one `IOutgoingMail`), and registers the MinIO client, the keyed file-storage services, the intake gate and the other infrastructure services. `AddAgentRuntimeClient` registers the agent runtime client and validates its settings.
- `AddIniaAiDatabases` — `platform/IniaAi.Infrastructure/Persistence/Setup/DatabaseConfigurator.cs`. Registers the two DbContexts, repositories, and units of work (see [Database Services](infrastructure/database-services.md)).
- WebApi composition — `platform/IniaAi.WebApi/Configuration/ApplicationServicesConfiguration.cs` exposes `AddTransientServices`, `AddScopedServices`, and `AddSingletonServices`, plus `HealthCheckConfiguration`, `AuthenticationConfiguration`, and the other `Configuration/*.cs` extensions.

## Patterns in use

- **Constructor injection** everywhere; the service-locator pattern is avoided except inside factory lambdas that resolve keyed services.
- **Keyed services** for swappable providers — `IFileStorageService` keyed by `"Database"`/`"Minio"` behind `IFileStorageResolver`, and `IWorkflowHandler` keyed by workflow type. Email is not keyed: `OutgoingMail.ConfiguredSender` builds the one sender `EmailSettings:Provider` names.
- **Conditional registration** of AI connectors based on `PlatformSettings` (Anthropic / AWS Bedrock / Azure AI Foundry for chat; Cohere for embeddings).
- **Options pattern** (`IOptions<T>` / `IOptionsMonitor<T>`) for configuration-bound settings.

See [Service Registration](service-registration.md) for lifetimes and conventions.
