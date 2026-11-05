# Contributing

The repository's root `CONTRIBUTING.md` says how to report a problem, how a change gets in, and
the conduct everyone follows. This page holds the bar for a change to the platform.

## Build and test

[Build from source](README.md#prerequisites-to-work-on-the-source) in the README says how to build
the solution, run its tests and run the apps. A change meets this bar:

- `dotnet build IniaAi.Platform.CI.slnf` passes. The build treats every compiler and analyzer
  warning as an error.
- `dotnet test --solution IniaAi.Platform.CI.slnf` passes. CI runs the same command.
- A bug fix comes with a test that fails before the fix and passes after it.
- A test uses real objects, real fixture files and a real database. It replaces only an outside
  service, such as a model: `ScriptedChatConnector` answers model calls, and
  `WordHashEmbeddingsConnector` answers embeddings calls. A test that needs the judgment of a real
  model carries `[PaidApiFact]` or `[PaidApiTheory]`, and runs only with `INIA_TEST_PAID_APIS=1`.
- A test waits on a signal, never on the clock. `FixedWaitGuardTests` refuses a fixed wait.
- A test that takes more than 30 seconds fails, unless it carries `[SlowTest("what the time buys")]`.
- A change to a package also writes the package list again: see "Licence and third-party notices"
  in the [README](README.md#licence-and-third-party-notices).

## Code standards

- Names follow `.editorconfig`: PascalCase for types and public members, `_camelCase` for private
  fields, `ALL_UPPER` for constants, and an `Async` suffix on async methods.
- Every `if`, `else`, `for`, `foreach` and `while` has curly braces.
- A name says what a thing is. Write a comment only for a constraint that the code cannot show.
- A repeated string is a constant.
- An entity is configured with attributes on the entity class. Do not edit a migration by hand:
  change the entity and generate the migration again, for PostgreSQL and for SQL Server.
- A new enum member goes at the end, because the database stores the number.
- A colour in `IniaAi.WebApp` lives in `IniaAi.WebApp/Theme/`. `ColourLiteralTests` refuses a colour
  anywhere else.
- A message that a user reads says what happened in the user's terms, names the file, block or
  field, and gives the next step.
- A new block type goes in its own project: `docs/add-a-block.md`, at the root of the repository,
  says how.

## Licence

The [Apache License 2.0](LICENSE) covers this repository. Section 5 of the licence says that it also
covers a contribution that you submit.
