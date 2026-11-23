---
type: explanation
status: current
description: "The keyed file stores (Database, S3, MinIO), their resolver and manager, the startup check, the retries of failed deletes, the daily sweep and the storage health checks. Read when you need which storage services are registered and how one is chosen; the storage design itself is on File Storage."
---

# File Services

File storage is abstracted behind `IFileStorageService` with pluggable backends, all in `platform/IniaAi.Infrastructure/Storage` and registered in `InfrastructureServiceRegistration.cs`.

## Storage providers

Registered as keyed services and chosen at runtime by key:

- **`"Database"`** — `DatabaseStorageService` (always registered): stores file bytes in the database.
- **`"S3"`** — `ObjectStorageService` with `SharedBucketLayout`: registered only when `FileStorage:S3:BucketName` is set, on an S3 client from `ObjectStorageClients.ForS3`.
- **`"Minio"`** — `ObjectStorageService` with `FixedBucketLayout` or `BucketPerOrganizationLayout`: registered only when a MinIO endpoint is configured (`FileStorage:MinIO`), on an S3 client from `ObjectStorageClients.ForMinio` that skips the system proxy.

`FileStorageProviders.ThrowIfMisconfigured` stops startup when `FileStorage:DefaultProvider` or a `TenantOverrides` value names a store that is not registered.

## Resolution & management

- `IFileStorageResolver` / `FileStorageResolver` selects the backend by key, and refuses a key that is not registered.
- `IFileStorageManager` / `FileStorageManager` is the higher-level entry point for upload/download. `ReleaseFileAsync` deletes a file whose rows are gone and, when the store refuses, records it through `IStoredObjectDeletionRetries` (`StoredObjectDeletionRetries`) for a retry.
- `StoredObjectSweep` removes stored files that no row points at, once a day.
- File metadata is tracked separately through EF repositories in the `UserData` schema — `IFileRepository` and `IFileMetadataRepository` (see [Database Services](database-services.md)).

## Health

`HealthCheckConfiguration` adds one `ObjectStoreHealthCheck` per object store that is set up, "S3 Storage" and "MinIO Storage", tagged `Storage` and the store's name: Degraded on a failure, a 3-second timeout, and an answer cached for a minute. It lists through the store's own client, so the MinIO check skips the system proxy as the store does.
