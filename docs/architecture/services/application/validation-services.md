---
type: explanation
status: current
description: "The FluentValidation step the in-house dispatcher runs before a handler, how validators are found, and the validator conventions. Read when a request is refused with a validation error, or you write a validator."
---

# Validation Services

Input validation uses **FluentValidation**. Commands and queries carry a matching `AbstractValidator<T>` co-located with the handler under `platform/IniaAi.Application/Commands/`, `platform/IniaAi.Application/Queries/` or a module folder (over 200 validators across the Application assembly); a request without one goes straight to its handler.

## Validation pipeline

Validators are wired automatically — `AddApplication` calls `AddValidatorsFromAssemblyContaining<StartWorkflowExecutionCommandValidator>()` and `AddRequestDispatching`. A request sent through `ISender` (`platform/IniaAi.Application/Common/Dispatching`) whose response is `ErrorOr<T>` goes through `ValidatingDispatcher<,>` first: it resolves the request's optional `IValidator<TRequest>`, runs `ValidateAsync`, and on failure converts each `ValidationFailure` into an `Error.Validation(code, description)`, with the property name as the code, returned as `ErrorOr` — the handler never executes. A send from inside another handler gets the same step. Requests without a validator, or whose response is not `ErrorOr`, go straight to their handler.

## Conventions

- Validate tenant identity first: `RuleFor(x => x.OrganizationId).NotEmpty();` then `RuleFor(x => x.UserId).NotEmpty();`.
- Domain rules follow, with `.MaximumLength(...)` matched to the database column constraints.
- Use `.When(...)` for conditional rules (e.g. validate an email only when present).
- Validation surfaces as `ErrorOr` errors, which controllers render via `Problem` (RFC 7807 problem details).
