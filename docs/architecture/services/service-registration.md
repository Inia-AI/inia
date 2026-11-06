---
type: explanation
status: current
description: "Service lifetimes with real examples, the keyed, conditional, options and factory registration patterns, and how a closed module registers its services. Read when you choose a lifetime for a new service or debug a scoped/singleton mismatch."
---

# Service Registration

How services are registered across the layered DI extensions (see [Dependency Injection](dependency-injection.md)).

## Lifetimes

- **Transient** — most workflow, document, and AI-orchestration services (`AddTransientServices` in `ApplicationServicesConfiguration.cs`): `RagService`, `DocumentProcessingService`, `WorkflowHandler`, `ExaSearchService`, `OcrTranscriptionService`, the job executor, and every request handler, etc. A closed module registers its own services through one method that the hosts call.
- **Scoped** — per-request services and EF repositories/units of work: `IRequestContext`, `IExportService`, `ICoreUnitOfWork`, `IUserDataUnitOfWork`, the Ardalis repository wrappers.
- **Singleton** — long-lived, stateless or cache-backed services: `ICurrentUserService`, `IJobsService`, the AI connectors, `TokenSlidingWindowRateLimiter`, `IOutgoingMail`, and the keyed file-storage services.





## Registration patterns

- **Keyed registration** for provider selection: `AddKeyedSingleton<IFileStorageService, DatabaseStorageService>("Database")` (and `"Minio"` when configured), with `IFileStorageResolver` picking the key at runtime. Email is one `IOutgoingMail` singleton whose sender is chosen from `EmailSettings:Provider` ([Email Services](infrastructure/email-services.md)).
- **Keyed workflow handlers** by workflow type: the platform registers `GENERIC`, and a closed module can add its own types.
- **Conditional registration** of AI connectors driven by `PlatformSettings.DeploymentMode` (from `UsesModelGateway`, `IsRunningInAzure` and `IsRunningInAws`, in `AddModelConnectors`; in the gateway mode the closed module that supplies the gateway registers it and its embeddings connector first): chat resolves to Anthropic or AWS Bedrock; embeddings always resolve to a Cohere connector.
- **Options binding** via `services.Configure<T>(...)` and `AddOptions<T>().BindConfiguration(...).ValidateDataAnnotations().ValidateOnStart()`.
- **Factory lambdas** where construction needs runtime config (e.g. MinIO client, rate limiter).
