---
type: explanation
status: current
description: "The CQRS command-handler shape: the request record, the handler, the in-house dispatcher and its two dispatch paths, the validation step, and ErrorOr error handling. Read when you add or change a command, or a command's error does not reach the caller as expected."
---

# Command Handlers

Commands implement CQRS write operations as requests sent through `ISender`, the in-house dispatcher in `platform/IniaAi.Application/Common/Dispatching`. Each command lives under `IniaAi.Application/Commands/<Feature>/<Action>/` alongside its handler and validator (e.g. `Commands/DocumentCategories/CreateDocumentCategory/`); a closed module's commands sit in the module's own folder. A worked example is on [Clean Architecture](../../clean-architecture/index.md#commands-write-operations).

## Dispatch

`AddRequestDispatching` scans the Application assembly at startup, registers each handler as transient, and builds one dispatcher per request type: `ValidatingDispatcher<,>` for a request whose response is `ErrorOr<T>`, `HandlerOnlyDispatcher<,>` for any other response. A request type with two handlers stops startup with `<request type> has more than one handler: <handlers>.`

## Shape

- A command is a `record` implementing `IRequest<ErrorOr<TResult>>`. Per the multi-tenant contract, `OrganizationId` and `UserId` are the first two parameters.
- The handler implements `IRequestHandler<TCommand, ErrorOr<TResult>>` and uses `request.OrganizationId` / `request.UserId` directly (it does not inject `ICurrentUserService`).
- Handlers return `ErrorOr<T>`: a success value or an `Error` (validation, conflict, not-found, forbidden). Controllers turn the errors into an RFC 7807 problem response through `ApiController.Problem`.

## Validation

Validation runs ahead of the handler: `ValidatingDispatcher<,>` (`platform/IniaAi.Application/Common/Dispatching/ValidatingDispatcher.cs`) runs for every request whose response is `ErrorOr<T>`. If the matching `AbstractValidator` fails, it short-circuits and returns the validation errors as an `ErrorOr` without invoking the handler. See [Validation Services](validation-services.md).

## Error handling

Handlers express failures as `Error` values rather than throwing for expected conditions; unexpected exceptions bubble to the middleware. Cross-tenant access is guarded by scoping queries with `request.OrganizationId` (or, for shared workflow templates, the `WorkflowTypeOrganizationAccess` join).
