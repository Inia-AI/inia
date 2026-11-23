---
type: explanation
status: current
description: "Where the platform keeps the files customers upload and runs produce: the Database, S3 and MinIO stores, where each puts a file, the startup check that refuses a store not set up, streamed reads and writes, the move of the database's files into another store, deletes with their retries, the daily sweep of files no row points at, and the health checks. Read when you configure where a deployment stores files, move files between stores, or debug a file that cannot be read back or was not deleted."
---

# File Storage

**On this page:** The stores · Choosing the store · Where a file lives · Streaming · Moving files out of the database · Deleting files · Health checks · Configuration · Database tables · Known issues · Related documentation

Every stored file has a row, `FileMetadata`, that names its store (`StorageProvider`) and its key in that store (`ExternalStoragePath`). A session bundle of a finished agent run has its own row with the same two fields ([Agent Runtime Integration](../services/infrastructure/agent-runtime-integration.md)). Code reaches a store only through `IFileStorageManager`, which picks the store by the name the row records.

## The stores

| Store | Name in a row and in settings | Where the bytes live | Set up when |
|---|---|---|---|
| Database | `Database` | the `Files` table of the UserData database | always |
| S3 | `S3` | one bucket that the stack creates, under each organization's prefix | `FileStorage:S3:BucketName` is set |
| MinIO | `Minio` (any letter case) | a MinIO server, in a fixed bucket or a bucket per organization | `FileStorage:MinIO:Endpoint` is set |

The S3 and MinIO stores are the same code, `ObjectStorageService`, on the AWS SDK. MinIO speaks the S3 API, so the client only needs its address, path-style addresses and the checksum setting under [Known issues](#known-issues). The same code works with any other S3-compatible server through `FileStorage:S3:ServiceUrl`.

## Choosing the store

A new file goes to the store `FileStorageManager.GetProviderForTenant` names: the organization's entry in `FileStorage:TenantOverrides`, or else `FileStorage:DefaultProvider`. The file's row records that store, and every later read and delete uses it, so files in several stores work side by side.

Startup stops when the default store or an override names a store that the platform does not know, or one the deployment does not set up (`FileStorageProviders.ThrowIfMisconfigured`). It also stops when the S3 store has a bucket but no `FileStorage:S3:Region` and no service URL. The message names the missing setting, for example `FileStorage:S3:BucketName`. A row that names a store the deployment does not set up fails its read with *"This file is stored in S3, which this deployment does not set up."* No store falls back to another: a fallback would write a file to one store while its row named a different one.

## Where a file lives

A file's key is unique on its own: the id of its `FileMetadata` row, or a generated key for a session bundle. A store that leaves the category out of the location can never misplace a file whose caller names the wrong category.

| Store | Bucket | Object key | Category used |
|---|---|---|---|
| S3 | the stack's bucket | `{organizationId}/{key}` | no |
| MinIO with `FileStorage:MinIO:BucketName` | that bucket | `{key}` | no |
| MinIO without a fixed bucket | `{organizationId}-{category}`, lower-cased, `_` and `.` as `-`, created on the first upload | `{key}` | yes |

The bucket per organization stays only so that the files already stored that way remain readable. The categories are the constants in `StorageCategories` (`documents`, `json2xlsx-templates`, `agent-session-bundles`), and a caller can name the wrong one: the cleanup job deletes every file as `documents`, and the move out of the database stores every file as `documents`. So in that layout a read tries the named category's bucket first and then the bucket of each other category, and a delete clears the key in all of them. A self-serve or other multi-tenant deployment uses S3: the platform never creates a bucket there, so its role needs no right to create one.

## Streaming

Stores and downloads pass a file as a stream; only code that needs every byte at once holds the whole file.

- **Database.** An upload sends the caller's stream as the insert's parameter (PostgreSQL and SQL Server stream it; SQLite, used only by tests, gets the bytes). A read holds the row open with sequential access and returns the column as a stream, which keeps its database connection until the reader disposes it. A download therefore holds one connection of the pool for as long as the client takes to receive the file, so many slow downloads at once can use up the pool.
- **S3 and MinIO.** An upload sends the caller's stream with its length. A read returns the store's response stream.
- **The size before the first byte.** Every read returns a `StoredObjectStream`, which cannot seek but knows the stored size. `RetrieveFileBytesAsync` uses it to allocate the whole file once, at that size, for code that needs every byte (a PDF renderer, the screening of an upload).
- **Downloads.** `GET api/v1/files/{id}` and the admin app's `FileDownload` endpoints stream the file to the answer with its `Content-Length`. An error in the store after the first byte breaks the answer; it cannot become an error status any more.

A store needs a stream's length before it sends it. A stream that can seek, or a `StoredObjectStream` read from another store, says it; `FileStorageManager` buffers any other stream once before the upload.

## Moving files out of the database

When the default store is not the database, the API runs `FileStorageMigrationService` once at startup. It moves every file whose row names the database, oldest first, one at a time, into its organization's store: the store its `TenantOverrides` entry names, or else the default store. An organization whose override names the database keeps its files there.

1. It streams the file from the database into the default store, under the same key, as `documents`. No store but MinIO without a fixed bucket uses the category, and that layout finds a file in any category's bucket.
2. It points the row at the new store, only if the row still names the database.
3. It deletes the database copy.

A file that fails is logged with its id and passed over, and the next start tries it again; a row with no storage key is passed over. The service waits 100 ms after each file so the database keeps room for the app's own work. Logs: `Starting file storage migration service - Database -> {Provider}`, `Migration progress: …` every 10 files, and `File storage migration completed! …` with the counts.

A crash between two steps leaves a copy that no row points at, which the [sweep](#the-sweep) removes.

It then settles the database copies of files whose row names S3 or MinIO. Such a copy comes from a release older than this design, which recorded a store it did not have and wrote the bytes to the database, or from that release's move to MinIO, which kept the copy. A copy the row's store lacks is stored there first; then the database copy goes, unless a row of the Database store names it too. The log reads `Settled {CopyCount} database copies of files recorded in an object store`.

## Deleting files

A delete removes the rows first and then the stored file, after the rows commit: a file whose rows are gone cannot be read, and a document never becomes undeletable because its store failed. The cleanup job (`SoftDeleteCleanupService`) deletes the stored files of the rows it purges in the same way ([Soft-Delete Cleanup](../../operations/soft-delete-cleanup.md)).

### Retries of a failed delete

A caller whose rows are already gone deletes the stored file through `FileStorageManager.ReleaseFileAsync`. When the store refuses, it records the file in the `StoredObjectDeletions` table and returns the failure, which the caller logs. A caller that keeps its row until the file is gone (a session bundle being released) uses `DeleteFileAsync`, which throws, and retries on its own. Every 15 minutes the API retries the deletes that are due (`StoredObjectDeletionRetryBackgroundService`): the first retry waits 15 minutes, and each failure doubles the wait, up to a day. From the 8th failed attempt on, each failure logs `StoredObjectDeletionStuck` with the store and the key, which an alarm can read; the retries go on.

### The sweep

Once a day the API removes stored files that no row points at (`StoredObjectSweep`), from the stores the platform owns:

- **Database:** a row of the `Files` table whose key no `FileMetadata` row and no session bundle holds, in batches of 500.
- **S3:** an object of the stack's bucket whose key no `FileMetadata` row and no session bundle holds.

A row keeps its file whatever store it records: keys are unique across stores, and a row an older release recorded in one store can hold its bytes in another. The original of a PDF the platform repaired on upload is one such file with no row: the row points at the repaired copy, so the sweep removes the original a day after the upload.

A row that is deleted but not purged yet still counts, so its file stays until the cleanup job purges it. A file younger than a day is left, so an upload whose row is not saved yet survives. The sweep logs `Stored file sweep removed {DatabaseCount} database copies and {BucketCount} bucket objects that no row points at`.

A MinIO bucket is not swept: the infrastructure team of the deployment provides it, and it can hold more than the platform's files.

## Health checks

`/health` checks each object store that is set up, `S3 Storage` and `MinIO Storage`, with `ObjectStoreHealthCheck`, through the store's own client: it lists at most one object of the store's bucket, or the buckets when MinIO keeps a bucket per organization. So the check needs the same list right as the [sweep](#the-sweep). A store that does not answer within 3 seconds, or refuses, reports Degraded, which still answers HTTP 200: it shows on the health page without taking the host out of a load balancer, and every request that touches no file still works. The load balancer probes every 30 seconds, so an answer stands for a minute. The other checks are on [Multi-Tenant Deployment](../multi-tenant/deployment.md).

## Configuration

Every key is under `FileStorage`, listed on [Application Configuration](../../operations/application-configuration.md). A deployment on the stack's bucket sets three:

```json
{
  "FileStorage": {
    "DefaultProvider": "S3",
    "S3": {
      "BucketName": "files-bucket-example",
      "Region": "eu-west-2"
    }
  }
}
```

The keys are read once, at startup. On AWS and Azure the platform loads them, with the other settings, from the deployment's settings store ([Cloud-mode configuration sources](../../operations/application-configuration.md#cloud-mode-configuration-sources)).

## Database tables

The UserData database holds the three tables; every column is on the [UserData database reference](../../reference/database/user-data-database.md).

- **`FileMetadata`:** one row per stored file, for every store: the organization, the name and extension, the size, `StorageProvider` (`NotDefined` 0, `Database` 1, `S3` 2, `MinIO` 3) and `ExternalStoragePath`, the key in that store. `NotDefined` reads as the database.
- **`Files`:** the bytes of the files the Database store holds, by key (`Id`).
- **`StoredObjectDeletions`:** a file whose rows are gone and whose delete failed, until a retry deletes it.

## Known issues

### .NET does not match a CIDR range in NO_PROXY

.NET's `HttpClient` matches `NO_PROXY` entries only as literal host names, so a MinIO endpoint given as an internal IP address goes to a corporate proxy, which answers `502 Bad Gateway` or fails to resolve it. The MinIO client, which its health check uses too, skips the system proxy (`UseProxy = false`): MinIO is internal infrastructure, so no proxy is ever needed. To confirm the cause, check the proxy variables in the container and `curl` the MinIO health endpoint from it: curl matches CIDR ranges, so curl works where the app fails.

- [dotnet/runtime HttpEnvironmentProxy source](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/HttpEnvironmentProxy.cs): string matching only
- [HttpClient.DefaultProxy docs](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.defaultproxy)

### S3-compatible servers and the SDK's checksums

The AWS SDK sends a CRC32 checksum with every upload and checks one on every download ([Data Integrity Protections for Amazon S3](https://docs.aws.amazon.com/sdkref/latest/guide/feature-dataintegrity.html)). Some S3-compatible servers reject those requests, so a client with a service URL, and the MinIO client, compute and check checksums only where an operation requires one. Amazon S3 itself keeps the default.

## Related documentation

- [C4 Container Diagram](../c4-model/container.md): file storage in the system
- [Multi-Tenant Architecture](../multi-tenant/index.md): tenant isolation
- [Soft-Delete Cleanup](../../operations/soft-delete-cleanup.md): when deleted rows and their files are purged
