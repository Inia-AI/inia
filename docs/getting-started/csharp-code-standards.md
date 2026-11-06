---
type: reference
status: current
description: "Reference of the C# house rules: naming, mandatory braces, layout, test rules, API contracts and DTO mapping, EF Core entity configuration, comments and validators. Read when you write or review platform C# code."
---

# C# Code Standards

**On this page:** Naming Conventions · Mandatory Curly Braces · Layout and Members · Tests · API Contracts and DTOs · EF Core Entity Configuration · Comments · Validators · Related pages

The house rules for C#/.NET code in the IniaAi Platform. These are enforced by `.editorconfig`; this page is the canonical reference for them. The aim is one consistent style across the whole solution so reviews focus on behaviour, not formatting.

## Naming Conventions

The naming rules below come straight from `.editorconfig` and are reported by the analyzers as you type.

| Kind | Convention | Example |
| ---- | ---------- | ------- |
| `const` fields | `ALL_UPPER` with `_` separator | `TYPE_FULL`, `FORMAT_CSV` |
| Interfaces | `I` + PascalCase | `IWorkflowHandler` |
| Types (class, struct, enum) | PascalCase | `BlockExecutionState` |
| Public members (property, field, event) | PascalCase | `OrganizationId` |
| Private fields / properties | `_camelCase` (underscore prefix) | `_logger`, `_validTypes` |
| Methods | PascalCase | `Handle` |
| Async methods | PascalCase + `Async` suffix | `ExecuteAsync` |
| Parameters | camelCase | `cancellationToken` |
| Locals | camelCase | `excludeWarnings` |

### No Magic Strings

Repeated string literals must be extracted into `const` fields named in `ALL_UPPER`. This is especially common for block property keys and format identifiers:

```csharp
public const string YOUR_PROPERTY = "01000-yourProperty";
public const string FORMAT_CSV = "csv";
```

## Mandatory Curly Braces

**Every** `if`, `else`, `for`, `foreach`, `while`, `switch`, `using`, and similar block carries curly braces — even when the body is a single statement. There are no single-line-body shortcuts and no exceptions.

```csharp
// Correct
if (block.ExecutionState is null)
{
    block.ExecutionState = new BlockExecutionState(block);
}

foreach (var document in documents)
{
    Process(document);
}
```

```csharp
// Not allowed — missing braces
if (block.ExecutionState is null)
    block.ExecutionState = new BlockExecutionState(block);
```

## Layout and Members

- **File-scoped namespaces** (`namespace IniaAi.Core.Workflows;`), not block-scoped.
- **`using` directives outside the namespace.**
- **`readonly` on fields that are never reassigned** — the analyzer raises this as a warning, so treat it as required.
- **Accessibility modifiers are explicit** on non-interface members (`private`, `public`, …); this is also a warning-level rule.
- **No multiple consecutive blank lines.**
- **Expression-bodied members** are fine for properties, indexers, and accessors, but **not** for methods or constructors — those use full block bodies.

## Tests

The platform uses **xUnit v3** on the Microsoft Testing Platform, with plain xUnit assertions. One rule is absolute:

### Never mock, fake, or stub a dependency

Tests use **real instances, real fixtures, and a real database** — a real `JObject`, a real `Workflow.FromJson`, a real `BlockProperties`, a real fixture file. Build the actual object graph and assert against real behaviour. NSubstitute is allowed only at an external boundary such as `IChatConnector`, and a test of the code around a model call or embeddings uses `ScriptedChatConnector` or `WordHashEmbeddingsConnector` instead of a paid call ([Testing Strategy](testing.md)).

```csharp
public class DateTimeExtensionsTest
{
    [Fact]
    public void ToMonthEnd_ZeroesTimeOfDay()
    {
        var date = new DateTime(2025, 3, 10, 14, 30, 45, DateTimeKind.Utc);

        DateTime result = date.ToMonthEnd();

        Assert.Equal(new DateTime(2025, 3, 31, 0, 0, 0, DateTimeKind.Utc), result);
    }
}
```

Every test must use **curly braces** the same way production code does. A test that touches **any** obsolete `Block` property counts as a legacy test regardless of what else it uses: migrate the whole test, not the part that fails. Test a legacy block as its author configured it; do not enable or toggle a setting to make the test run.

For how the suite is run, the CI matrix, and the integration-test environment variables, see [Testing Strategy](testing.md).

## API Contracts and DTOs

Request and Response DTOs belong in the **`IniaAi.WebApi.Contracts`** project — **never** in `platform/IniaAi.WebApi/Presentation/Contracts`.

| What | Where |
| ---- | ----- |
| Requests | `IniaAi.WebApi.Contracts/Requests/{Feature}/` |
| Responses | `IniaAi.WebApi.Contracts/Responses/{Feature}/` |
| Shared DTOs | `platform/IniaAi.WebApi.Contracts/Dto/` |
| Enums | `platform/IniaAi.WebApi.Contracts/Enums/` |

Reuse an existing DTO before adding a new one, and prefer immutable `record` types for request/response models.

An entity becomes a DTO in explicit C#; no mapping library takes part. A read that returns DTOs projects in the query with an `Expression<Func<TEntity, TDto>>`, so the database returns only the columns the DTO shows. Keep the projection with its area's code, as `XxxProjections` beside the handlers. A handler that already holds the entity, because it just created or changed it, maps it through that projection compiled once. A mapping that has no projection sits beside the one handler that uses it, or in `platform/IniaAi.Application/Common/Mappers` when several handlers share it.

## EF Core Entity Configuration

Configure entities with **data annotations on the entity class** — `[Required]`, `[StringLength]`, `[ForeignKey]`, `[Index]`, `[DeleteBehavior]` — and let EF Core conventions and navigation properties handle relationships.

**Do not** create explicit `IEntityTypeConfiguration<T>` classes and **do not** use `OnModelCreating` for entity configuration. Only reach for minimal Fluent API in genuinely exceptional cases that attributes cannot express (for example, a complex value conversion or a provider-specific SQL default).

```csharp
public class Counterparty
{
    [Required]
    [StringLength(250)]
    public string Name { get; set; } = string.Empty;

    [ForeignKey(nameof(Manager))]
    public string? ManagerId { get; set; }

    public Counterparty? Manager { get; set; }
}
```

When entity attributes change, regenerate migrations with the migration script rather than hand-editing migration files — see [Database Migrations](database-migrations.md).

## Comments

Default to **zero comments** — convey intent through naming. Add a comment only when it captures a genuinely hidden constraint or a non-obvious *why* that a reader cannot infer from the code (for example, a hard upstream size limit, or a workaround for a specific bug). Never narrate what the code does, restate a name, or reference the ticket that prompted the change. If a comment is explaining *what*, rename the identifier or extract a well-named helper instead.

## Validators

Every command and query has a FluentValidation validator, invoked automatically before the handler runs. Always validate `OrganizationId` and `UserId` first, then the domain-specific rules; use `.When(...)` for conditional rules and match `MaximumLength` to the database column constraint.

## Related pages

- [Testing Strategy](testing.md) — CI matrix, runner, and integration-test setup.
- [Database Migrations](database-migrations.md) — regenerating migrations after entity changes.
- [Contributing](../../CONTRIBUTING.md) — how a change gets in, and the bar for each part.
- [Glossary](../reference/glossary.md) — core platform terms.

