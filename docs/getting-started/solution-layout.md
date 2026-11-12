---
type: reference
status: current
description: "Maps the Clean Architecture layers to the actual project folders and files of the platform solution, project by project, with a where-does-X-live lookup, the test and support projects and the build output. Read when you look for where a kind of code lives in IniaAi.Platform or add a project."
---

# Solution Layout

**On this page:** The map at a glance · Source projects by layer · "Where does X live?" lookup · Test and support projects · Related pages

This page bridges the gap between the [Clean Architecture](../architecture/clean-architecture/index.md) concepts and the actual files on disk. If you have read the architecture docs and now think *"I understand the layers, but where does the code for X actually live?"* — this is the page that answers it.

The solution file is `IniaAi.Platform.slnx` at the platform's root, the `platform/` folder of the open repository. Every project below is one folder at that root named after its assembly (e.g. `IniaAi.Core/`). Source projects are grouped under `/src/` in the solution, test projects under `/tests/`, and the tools under `/tools/`.

> [!TIP]
> **The frontend is a separate project**
>
> The customer-facing single-page app (Vue) is **not** part of this .NET solution. It is a project of its own, the `web/` folder beside `platform/` in the open repository, and ships as the `iniaai/web` nginx image. See [Frontend Architecture](../architecture/frontend/index.md) for that codebase. Everything on this page is the .NET backend + Blazor admin solution.

## The map at a glance

| Project | Layer / concept | One-line purpose |
|---------|-----------------|------------------|
| [`IniaAi.Domain`](#iniaaidomain) | Domain | Entities, enums, repository interfaces, specifications |
| [`IniaAi.Application`](#iniaaiapplication) | Application | CQRS commands/queries, handlers, validators |
| [`IniaAi.Core`](#iniaaicore) | Core | Workflow engine, block strategies, RAG, business algorithms |
| [`IniaAi.Infrastructure`](#iniaaiinfrastructure) | Infrastructure | EF Core DbContexts, repositories, multi-tenant, storage, email, seeding |
| [`IniaAi.AiConnection`](#iniaaiaiconnection) | Infrastructure (AI) | Model-provider integrations (Bedrock, Anthropic, Cohere, Azure) |
| [`IniaAi.FileFormats`](#iniaaifileformats) | Infrastructure (docs) | The file-format interface, content inspection, image work, and the open implementation of every format but email |
| [`IniaAi.FileFormats.PdfiumWorker`](#iniaaifileformatspdfiumworker) | Infrastructure (docs) | The process that runs PDFium for the open PDF implementation |
| [`IniaAi.Services`](#iniaaiservices) | Application/Infra services | Cross-cutting service implementations (jobs, export, conversion) |
| [`IniaAi.Configuration`](#iniaaiconfiguration) | Cross-cutting | Strongly-typed settings and platform feature flags |
| [`IniaAi.Common`](#iniaaicommon-and-iniaaicommoninterfaces) | Cross-cutting | Shared helpers, serialization, text utilities |
| [`IniaAi.Common.Interfaces`](#iniaaicommon-and-iniaaicommoninterfaces) | Cross-cutting | Layer-agnostic interfaces (e.g. token usage, document processing) |
| [`IniaAi.WebApi`](#iniaaiwebapi) | Presentation | REST API controllers, middleware, healthchecks, background services |
| [`IniaAi.WebApi.Contracts`](#iniaaiwebapicontracts) | Presentation | API request/response DTOs and enums |
| [`IniaAi.WebApp`](#iniaaiwebapp) | Presentation | Blazor Server admin/prompt-engineering UI |
| [`IniaAi.Migrations.PostgreSql`](#migration-projects) | Infrastructure | EF Core migrations for PostgreSQL (Core + UserData) |
| [`IniaAi.Migrations.Mssql`](#migration-projects) | Infrastructure | EF Core migrations for SQL Server (Core + UserData) |
| [Test & support projects](#test-and-support-projects) | Tests | One `*.Tests` project per source project + shared test infra |


## Source projects by layer

### `IniaAi.Domain`

The innermost layer. Plain entities, enums, and the **interfaces** that outer layers implement. Has no dependency on EF Core, ASP.NET, or any provider SDK.

- **Implements:** Domain layer — see [Clean Architecture › Domain](../architecture/clean-architecture/index.md#1-domain-layer-iniaaidomain).
- **Open first:**
  - `Entities/Workflows/Workflow.cs` and `Entities/Workflows/Blocks/Block.cs` — the central [workflow](../reference/glossary.md#workflow-type-vs-workflow-vs-execution) aggregate, reused by **both** DbContexts.
  - `Entities/Workflows/Enums/BlockTypes.cs` — the enum every [block](../reference/glossary.md#block) strategy keys off.
- **Also here:** `Specifications/` (Ardalis.Specification queries, e.g. `Specifications/DocumentCategories/`), `Abstractions/` (`IIdentifiable`, `IOrganizationScoped`), `Entities/Documents/` ([Document, Page, EmbeddingVector](../architecture/rag/index.md)).

### `IniaAi.Application`

Use-case orchestration via CQRS. Commands (writes) and queries (reads) are requests sent through `ISender`; handlers and FluentValidation validators live beside them.

- **Implements:** Application layer — see [Clean Architecture › Application](../architecture/clean-architecture/index.md#2-application-layer-iniaaiapplication).
- **Open first:** any folder under `Commands/` or `Queries/` — each feature folder holds the command/query record, its handler, and its validator together. `DependencyInjection.cs` registers the request dispatchers and the validation step.
- **Convention:** every command/query takes `OrganizationId` and `UserId` as its first two parameters (see the [multi-tenant](../architecture/multi-tenant/index.md) model).

### `IniaAi.Core`

The richest business-logic layer: the **workflow engine** and the **RAG system**.

- **Implements:** Core layer — see [Clean Architecture › Core](../architecture/clean-architecture/index.md#3-core-layer-iniaaicore) and [Workflow Engine](../architecture/workflow-engine/index.md).
- **Open first:**
  - `Workflows/WorkflowHandler.cs` — runs the strategy of each block type and drives execution. `Workflows/Strategies/BlockStrategies.cs` lists the platform's own strategies; a closed module and an extension add theirs as a `BlockStrategyRegistration` (an extension through `Workflows/BlockExtensions/`).
  - `Workflows/Strategies/` — one file per open block type (`AiQueryStrategy.cs`, `ExaSearchStrategy.cs`, …).


  - `Rag/RagService.cs` and `Rag/Strategies/` — retrieval strategies (whole-document, top-N pages, …).
- **Also here:** `Documents/`, `AugmentedSearch/`.


### `IniaAi.Infrastructure`

Everything framework- and provider-specific: EF Core, multi-tenancy, storage, email, GitHub mirroring, and database seeding. This is where the Domain's interfaces get their concrete implementations.

- **Implements:** Infrastructure layer — see [Clean Architecture › Infrastructure](../architecture/clean-architecture/index.md#4-infrastructure-layer-iniaaiinfrastructure).
- **Open first:**
  - `Persistence/Core/Contexts/CoreDbContext.cs` — workflow **templates** (schema `Core`).
  - `Persistence/UserData/Contexts/UserDataDbContext.cs` — **executed** workflows and user data (schema `UserData`).
  - `Persistence/Seed/SystemUserSeeder.cs` — seeds the system/service user (relevant when [provisioning a system user](../reference/api/system-users.md) for API access).
- **Also here:** `Persistence/Setup/` (the startup migration runner), `MultiTenant/`, `Storage/` ([file storage](../architecture/file-storage/index.md)), `Email/` (outgoing mail), `GitHub/`.

### `IniaAi.AiConnection`

All model-provider integration lives here, behind abstractions so the rest of the platform never references a vendor SDK directly.

- **Implements:** Infrastructure (AI) — see [AI Integration](../architecture/ai-integration/index.md).
- **Open first:**
  - `AwsBedrock/AwsBedrockChatProvider.cs` — the Bedrock path (`FORCE_AWS_MODE=true`), calling Bedrock `InvokeModel`.
  - `Anthropic/AnthropicChatConnector.cs` — the direct Anthropic API path, when `FORCE_AWS_MODE` is off.
- **Also here:** `Cohere/` (embeddings), `Azure/` (AI Foundry), `Abstractions/` (with `IModelGateway`, the seam a closed module's model gateway plugs into), `TokenProviders/`, `HttpHandling/`.


### `IniaAi.FileFormats`

Turns uploaded files into extractable text and pages. OCR itself lives in `IniaAi.Core` (`OcrTranscriptionService`, in `platform/IniaAi.Core/Services/`) — this project supplies the parsing and page rendering it builds on.

- **Implements:** Infrastructure (documents) — see [Document Processing](../architecture/document-processing/index.md) and [OCR](../architecture/ocr/index.md).
- **Open first:** `OpenFileFormatsServices.cs`, where `AddOpenFileFormats` registers the open implementation: ClosedXML in `Excel/`, Open XML SDK in `Word/OpenXml/`, PDFium in `Pdf/Pdfium/` and password removal in `Unlock/`. Then `Images/`, and `Markdown/`, the open writer of the report's Markdown export (`MarkdownReportWriter`) and the Markdown rules of Json2Xlsx (`MarkdownSyntax`). `Excel/` and `Word/` also hold the interfaces Json2Xlsx writes through.

### `IniaAi.FileFormats.PdfiumWorker`

A console program that loads PDFium and answers one document's requests over its standard input and output. `PdfiumPdfFormat` and `OpenDocumentUnlocker` in `IniaAi.FileFormats` start one per open document, so a crash or a hang in PDFium ends the worker, not the platform. It references no project of ours; `IniaAi.FileFormats` references it, so its program and the PDFium natives reach every host.

- **Implements:** Infrastructure (documents) — see [Document Processing](../architecture/document-processing/index.md#the-pdfium-worker-process).
- **Open first:** `Program.cs`, then `platform/IniaAi.FileFormats/Pdf/Pdfium/PdfiumWorkerProtocol.cs`, the message format both sides share.


### `IniaAi.Services`

Service implementations that are shared across features and don't belong to a single Clean-Architecture layer — background jobs, document conversion, export, content refinement.

- **Implements:** see [Services Layer](../architecture/services/index.md).
- **Open first:** `Documents/DocumentProcessingService.cs`, `Jobs/`, `Export/`, `Converters/`.

### `IniaAi.Configuration`

Strongly-typed settings objects (bound from `appsettings.*.json` / env vars / Secrets Manager) and platform feature flags. No magic strings for config keys.

- **Implements:** cross-cutting — see [Application Configuration](../operations/application-configuration.md).
- **Open first:** `Platform/PlatformSettings.cs`, `Platform/OcrSettings.cs`, `Platform/ModelRoutingSettings.cs`.


### `IniaAi.Common` and `IniaAi.Common.Interfaces`

`IniaAi.Common` holds genuinely shared, layer-agnostic utilities (serialization, text/numeric helpers, data masking, embeddings helpers). `IniaAi.Common.Interfaces` holds interfaces that several layers depend on but that belong to none of them (e.g. `IDocumentProcessingService`, `ITokenUsageService`, `IRequestContext`).

- **Implements:** cross-cutting.
- **Open first:** `platform/IniaAi.Common.Interfaces/IDocumentProcessingService.cs`, `platform/IniaAi.Common/Serialization/`.

### `IniaAi.WebApi`

The REST API surface and the only HTTP entry point external integrators use.

- **Implements:** Presentation — see [Clean Architecture › Presentation](../architecture/clean-architecture/index.md#5-presentation-layer-iniaaiwebapi-iniaaiwebapp).
- **Open first:**
  - `Program.cs` — composition root and middleware pipeline.
  - `Controllers/v1/` — one controller per resource (`WorkflowExecutionsController.cs`, `DocumentsController.cs`, `AuthenticationController.cs`, `UsersController.cs`, …). These map 1:1 to the [API reference](../reference/api/index.md).
- **Also here:** `Middleware/` (tenant resolution, rejection, user-context), `Healthchecks/`, `BackgroundServices/`.

### `IniaAi.WebApi.Contracts`

The request/response DTOs the API exposes — a separate project so contracts can be referenced without dragging in the whole WebApi.

- **Implements:** Presentation (contracts).
- **Open first:** `Requests/<Feature>/`, `Responses/<Feature>/`, shared `Dto/` and `Enums/`. New API DTOs go **here**, never under `WebApi/Presentation/Contracts`.

### `IniaAi.WebApp`

The Blazor Server application — the internal UI where prompt engineers author workflows and admins manage the platform.

- **Implements:** Presentation — see [Clean Architecture › Presentation](../architecture/clean-architecture/index.md#5-presentation-layer-iniaaiwebapi-iniaaiwebapp).
- **Open first:** `Program.cs`, `Components/` (Razor components: `Account/`, `Admin/`, `Documents/`, `Dialogs/`, …), `Components/App.razor`.

### Migration projects

EF Core migrations are kept out of the runtime projects and split by provider. Each provider project has **both** context folders.

- `platform/IniaAi.Migrations.PostgreSql/Core/` and `.../UserData/` — the PostgreSQL migration history (the default provider for local and most deployments).
- `platform/IniaAi.Migrations.Mssql/Core/` and `.../UserData/` — the SQL Server equivalents.
- Generate migrations with `create_and_apply_migration.zsh`; see [Database Migrations](database-migrations.md). Do not hand-edit generated migration or snapshot files.

## "Where does X live?" lookup

| I'm looking for… | It lives in… |
|------------------|--------------|
| A new **workflow block strategy** | `platform/IniaAi.Core/Workflows/Strategies/` (and add it to `Workflows/Strategies/BlockStrategies.cs`) |
| A **block type** enum value | `platform/IniaAi.Domain/Entities/Workflows/Enums/BlockTypes.cs` |
| An **EF entity** | `IniaAi.Domain/Entities/<Area>/` |
| A **specification** (reusable query) | `IniaAi.Domain/Specifications/<Area>/` |
| A **command / query / handler / validator** | `platform/IniaAi.Application/Commands/` or `.../Queries/<Feature>/` |
| An **API controller** | `platform/IniaAi.WebApi/Controllers/v1/` |
| An **API request/response DTO** | `platform/IniaAi.WebApi.Contracts/Requests/` or `.../Responses/<Feature>/` |
| A **DbContext** | `IniaAi.Infrastructure/Persistence/{Core,UserData}/Contexts/` |
| A **repository implementation** | `IniaAi.Infrastructure/Persistence/{Core,UserData}/` |
| An **EF migration** | `IniaAi.Migrations.PostgreSql/{Core,UserData}/` (and the Mssql twin) |
| A **model-provider integration** | `IniaAi.AiConnection/{AwsBedrock,Anthropic,Cohere,Azure}/` |
| **RAG** retrieval logic | `platform/IniaAi.Core/Rag/` |
| **OCR / PDF / Word / Excel** extraction | `IniaAi.FileFormats/` |
| A **strongly-typed setting** / feature flag | `platform/IniaAi.Configuration/Platform/` |
| **Multi-tenant** middleware | `platform/IniaAi.WebApi/Middleware/` and `platform/IniaAi.Infrastructure/MultiTenant/` |
| The **system/service user** seeder | `platform/IniaAi.Infrastructure/Persistence/Seed/SystemUserSeeder.cs` |
| A **Blazor admin page** | `platform/IniaAi.WebApp/Components/` |
| The **WebApi composition root** | `platform/IniaAi.WebApi/Program.cs` |



## Test and support projects

Most source projects have a parallel test project under `/tests/` (e.g. `IniaAi.Core.Tests`, `IniaAi.FileFormats.Tests`, `IniaAi.Application.UnitTests`, `IniaAi.WebApi.UnitTests`); `IniaAi.Common.Interfaces`, `IniaAi.WebApi.Contracts` and the migration projects have none. Four projects are shared test infrastructure rather than test suites, and the integration tests are a suite of their own:

| Project | Purpose |
|---------|---------|
| `IniaAi.Testing` | Base fixtures, the test logger, and custom xUnit attributes (`TestsBaseFixture.cs`). It references no document library. |
| `IniaAi.Testing.Documents` | Writes PDF test files itself (`PdfFixtures`, password protection included), builds workbooks with ClosedXML (`OoxmlFixtures`, encrypted by `OfficeEncryption`) and Word test files with Open XML SDK (`WordFixtures`). The legacy `.xls` and macro workbooks are committed files in `Resources/`. It registers the build's file formats for every test (`AddFileFormatsUnderTest`, `FileFormatsUnderTest`), so `IniaAi.Testing.DependencyInjection` references it too. |
| `IniaAi.Testing.Ooxml` | Reads Word documents and workbooks as raw Office Open XML (`WordFile`, `OoxmlPackage`), so a test checks what any library wrote alike. It references no document library. |
| `IniaAi.Testing.DependencyInjection` | Builds a **real** service provider for tests (EF Core, real repositories, test seeders) and the test chat and embeddings connectors (`TestChatConnectorFactory`, `ScriptedChatConnector`, `WordHashEmbeddingsConnector`) — the real-instance approach the house rules require. |
| `IniaAi.Samples.WordCount` | A Count Words block type in a project of its own: the working example of an extension ([A block type in its own project](../architecture/workflow-engine/block-implementation-guide.md#a-block-type-in-its-own-project)). `IniaAi.Core.Tests` and `IniaAi.WebApp.Tests` reference it and register it as they load. |
| `IniaAi.WebApi.IntegrationTests` | Full-pipeline API tests; not in the CI filter `IniaAi.Platform.CI.slnf`. CI runs only their `*.Authorization` namespaces, in the isolation job with a PostgreSQL service (see [Testing](testing.md)). |


### Build output

Each test project is its own executable, so its output folder holds every package it uses, up to 275 MB for a project that reaches the document libraries. `HostRuntime.targets` builds each executable for the runtime of the machine that builds it, so the folder holds the native files of one platform instead of 29. A runtime given on the command line wins, as the Docker images give `linux-x64`, and the output stays in `artifacts/bin/<project>/<configuration>`. The PDFium worker writes no runtime configuration, because it runs under its host's, so it stays portable: built for the machine's runtime, it would not match a host published for another processor. `Directory.Build.props` makes the build hard-link those files to the NuGet cache instead of copying them, so the copies cost no disk; where the cache sits on another volume, the build copies them as before. `Directory.Build.targets` skips the native debug symbols that SkiaSharp ships for Windows, 80 MB per Windows architecture.

Tests use plain xUnit assertions with real instances; NSubstitute only at an external boundary such as `IChatConnector`. See [C# Code Standards](csharp-code-standards.md) and [Testing](testing.md).

## Related pages

- [Clean Architecture Overview](../architecture/clean-architecture/index.md) — the layer model this page maps to disk.
- [Workflow Engine](../architecture/workflow-engine/index.md) — how the strategies in `IniaAi.Core` execute.
- [Database Migrations](database-migrations.md) — generating migrations into the migration projects.
- [Glossary](../reference/glossary.md) — definitions for the domain terms used above.

