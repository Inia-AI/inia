---
type: explanation
status: current
description: "The CQRS query-handler shape and its reads through Ardalis specifications and projections. Read when you add or change a query, or a read is slow or returns another organization's rows."
---

# Query Handlers

Queries implement CQRS read operations as requests sent through `ISender`, on the same two dispatchers as [commands](command-handlers.md). Each query lives under `IniaAi.Application/Queries/<Feature>/` with its handler and validator.

## Shape

- A query is a `record` implementing `IRequest<ErrorOr<TResult>>`, with `OrganizationId` / `UserId` as the leading parameters for tenant scoping.
- The handler implements `IRequestHandler<TQuery, ErrorOr<TResult>>` and returns DTOs from `IniaAi.WebApi.Contracts` (or domain projections), not tracked entities.

## Data access

Reads go through the **Specification pattern** (`Ardalis.Specification`, specs in `platform/IniaAi.Domain/Specifications/`). Specs declare `.Include()` chains to prevent N+1, and many use `AsNoTracking()` for read-only queries. Handlers reach the generic repository through their database's unit of work (`Repository<T>()`, an `IRepository<T>`) or a feature repository, and call `GetFirstOrDefaultAsync(spec, ct)`, `FindAsync(spec, ct)` or `GetAllAsync(spec, ct)`; a specification with a selector (`ISpecification<T, TResult>`) projects in the query. OData list endpoints project in explicit C# as well (`*Projections` classes); there is no mapping library.

## Performance

- Project to DTOs in the query rather than materializing whole aggregates.
- Reads land in the context that owns the data — template reads hit `CoreDbContext`, execution/history reads hit `UserDataDbContext` (see [Database Services](../infrastructure/database-services.md)).
- The same validation step validates queries before the handler runs.
