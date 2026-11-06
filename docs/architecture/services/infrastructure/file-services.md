---
type: explanation
status: current
description: "The keyed file-storage backends (Database, MinIO; S3 not wired), their resolver and manager, and the MinIO health check. Read when you need which storage services are registered and how one is chosen; the storage design itself is on File Storage."
---

# File Services

File storage is abstracted behind `IFileStorageService` with pluggable backends, all in `platform/IniaAi.Infrastructure/Storage` and registered in `InfrastructureServiceRegistration.cs`.

## Storage providers

Registered as keyed services and chosen at runtime by key:

- **`"Database"`** — `DatabaseStorageService` (always registered): stores file bytes in the database.
- **`"Minio"`** — `MinioStorageService`: registered only when a MinIO endpoint is configured (`FileStorage:MinIO`); uses a `MinioClient` built with a proxy-bypassing `HttpClient`.
- `S3StorageService` exists for AWS S3 but is **not currently wired** — its keyed registration is commented out in both `InfrastructureServiceRegistration.cs` and `platform/IniaAi.WebApp/ServicesConfigurator.cs`.

## Resolution & management

- `IFileStorageResolver` / `FileStorageResolver` selects the active backend by key.
- `IFileStorageManager` / `FileStorageManager` is the higher-level entry point for upload/download.
- File metadata is tracked separately through EF repositories in the `UserData` schema — `IFileRepository` and `IFileMetadataRepository` (see [Database Services](database-services.md)).

## Health

When MinIO is configured, `HealthCheckConfiguration` adds a `/minio/health/ready` URL health check (10-second timeout, proxy bypassed) tagged `Storage`/`MinIO`.
