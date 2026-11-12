---
type: reference
status: current
description: "Describes the platform's CI test run, the test runner, the rules that keep tests fast, reading a red run, the opt-in for tests that call paid APIs, the local database-backed integration tests, and editor-app component tests with bUnit. Read when you write, run or debug a platform test, or a CI test run fails."
---

# Testing Strategy

**On this page:** PR Testing Process · Performance and Cost Optimization · Troubleshooting · Related pages

This guide covers the automated PR testing process and local developer testing for the IniaAi Platform.


## PR Testing Process

### Automated CI/CD Testing

#### GitHub Actions Pipeline

- The open repository's `Platform` workflow ([`.github/workflows/platform.yml`](../../.github/workflows/platform.yml)) runs the tests on **Ubuntu** for each pull request and each push to `main` that touches `platform/`. It needs no secret, so a pull request from a fork runs them too. A draft pull request runs no tests, and marking it ready starts the run
- CI runs `dotnet test --solution IniaAi.Platform.CI.slnf`, which scopes the run to the unit/component test projects


- `IniaAi.WebApi.IntegrationTests` (the full-pipeline API tests, which host the API in-process through `CustomWebApplicationFactory`) is **excluded** from the main CI run via the `IniaAi.Platform.CI.slnf` solution filter; run it locally, with the database servers some of its tests need ([below](#database-backed-integration-tests-local-only)). One part of it runs on every pull request: the **Organization isolation tests** job of `.github/workflows/Run tests.yml` starts a PostgreSQL service and runs the project's `*.Authorization` namespaces, the tests of who may reach which organization's data

> [!TIP]
> **Test runner**
>
> The solution uses **xUnit v3** on the **Microsoft Testing Platform (MTP)**, configured in `global.json`. Run the whole suite with `dotnet test`, a single project with `dotnet test --project <proj>.csproj`, and filter with MTP args after `--` (e.g. `-- --filter-class "*SomeTests*"`). VSTest flags such as `--logger` and `--filter` are silently ignored in MTP mode; MTP's own options, such as `--results-directory` and `--report-xunit-trx`, work. CI passes `-p:XunitTrxReport=true`, which names each project's TRX report after the project, so no report overwrites another.

> [!WARNING]
> **Cost Considerations**
>
> - Tests marked `[PaidApiFact]` spend **model, embeddings, Bedrock and Exa credits**, and run only when you opt in. The other tests make no paid call (see [Tests that call paid APIs](#tests-that-call-paid-apis))
>
>
> - **GitHub Actions minutes** are billed to GitHub
> - Each push to a ready pull request starts a full test run and cancels the run of the push before it, so 15 separate pushes pay for up to 15 runs

**Best Practices:**

- Mark PRs as **draft** while still working to prevent test runs
- **Batch commits** before pushing to remote
- **Cancel running actions** in the Actions tab if you forgot to mark as draft

#### Tests stay fast

A test that waits a fixed time is slow on every run and flaky on a busy one. Write tests to these rules:

- **Wait on a signal, never on the clock.** Await the task, a `TaskCompletionSource`, a channel or an injected `TimeProvider`. A `Task.Delay` may only cap a wait (inside `Task.WhenAny` or `.WaitAsync`) or hang until cancelled (`Timeout.Infinite`). When only polling can see the condition, use `Eventually.UntilAsync`; when a test double must take time, use `SimulatedWork.TakesAsync` (both in `IniaAi.Testing`). `FixedWaitGuardTests` fails on any other `Thread.Sleep` or `Task.Delay` in test code, unless a `// Waits on purpose: <reason>` comment on that line or the line above says why.
- **Make the code under test awaitable.** A background job keeps its task instead of discarding it. Retry, backoff and polling wait through an overridable method or an injected `TimeProvider`, so a test skips the wait.
- **Keep the network out of the test path.** Code that detects its host does it once per process. A test reaches a model, web search or another outside API only as a paid test. The exchange-rate API is answered at the network edge by `CannedExchangeRatesHandler` (`IniaAi.Testing`), both in the test services and in the API host that `CustomWebApplicationFactory` starts.
- **Use the smallest fixture that shows the rule**, and build expensive read-only setup once, in a class or collection fixture. A SQLite user-data database comes from `AddSqliteTestDatabaseServices` or `SqliteUserDataSchema`, which copy one schema built per test process: a test never runs `EnsureCreated` for it. An API test that starts its own in-process host builds it with `TestWebApplication.CreateBuilder`, which reads no settings files or environment variables and adds no logging providers. The default builder does all three for each host.

CI enforces the time side. A test over 30 seconds fails unless it carries `[SlowTest("what the time buys")]`. A test too slow for every pull request carries `[NightlyFact("what makes it slow")]`: CI runs it only in the nightly run, and a local run always runs it. Each test project has a budget in `test-time-budgets.json` in the platform's `.github/` folder, twice the slowest time CI has seen for it (the nightly paid tests included), and a project over its budget fails the run. The job summary lists every test over 2 seconds.

#### Interpreting Test Results


- `Check test times` fails the leg when a project runs over its budget in `test-time-budgets.json`, even when every test passed.

### Local Testing Setup

#### Prerequisites

Run both applications of the platform locally:

- **IniaAi.WebApi**
- **IniaAi.WebApp** (Admin/Editor interface)

The end-user frontend is a **separate project** (`iniaai-web`, a Vue + Vite app), not part of the platform: the `web/` folder beside `platform/` in the open repository. Run it in a separate terminal from its folder ([Frontend dev quickstart](../architecture/frontend/index.md#dev-quickstart)):

```bash
# From the frontend's folder
npm install
npm run dev   # runs Vite
```



#### Where the tests get their settings

The test fixtures read their settings from environment variables. When `INIAAI_SSM_ENVIRONMENT` names a Parameter Store environment, such as `unittests`, they first read `/iniaai/<environment>/shared/secrets` with the AWS session, and environment variables override what it returns. Unset, they read no Parameter Store and need no AWS credentials: `dotnet test --solution IniaAi.Platform.CI.slnf` passes on a machine with only the .NET SDK. A test that needs a stored value then skips and names what to set.

#### Tests that call paid APIs

A `dotnet test` makes no paid calls unless it opts in, locally or on a pull request. A test of the code around a model call gets a `ScriptedChatConnector` (`platform/IniaAi.Testing.DependencyInjection/Mocks/`): it answers from the request, and it keeps every call, so the test can check the prompt and pages that reached the model. A test of the code around embeddings gets a `WordHashEmbeddingsConnector` from the same folder: it builds each vector from the words of the text, so texts that share words score as similar, and every run gets the same vectors. A test whose assertion is the judgment of a model, the embeddings or a web search carries `[PaidApiFact]` or `[PaidApiTheory]`.

| Where it runs | `[PaidApiFact("reason")]` | `[PaidApiFact("reason", alsoInCi: true)]` |
|---|---|---|
| Local, default | Skipped | Skipped |
| Local, `INIA_TEST_PAID_APIS=1` | Runs | Runs |
| CI, pull request | Skipped | Skipped |


Without the opt-in, `TestChatConnectorFactory` and `TestEmbeddingsConnectorFactory` hand out connectors that refuse every call, and the test configuration replaces each stored setting and each configured credential (a setting named `…ApiKey` or `Pat`, one exported in the shell included), except the free exchange-rate key, with a placeholder. A call that reaches the paid model by accident throws an error that names the opt-in, instead of spending: `A test called the real model, and local test runs make no paid calls. Give the test a ScriptedChatConnector, or mark it [PaidApiFact] and run it with INIA_TEST_PAID_APIS=1.` (for embeddings, `A test called the real embeddings model, …`). A skipped paid test reports `Calls a paid API (<reason>). Set INIA_TEST_PAID_APIS=1 to run it.`, and in the nightly run a test without `alsoInCi` reports `Ignored in CI: <reason>`. Some services catch that error and carry on, so the test can still pass: when you add a test near a model call, read its output for that error.

```bash
INIA_TEST_PAID_APIS=1 dotnet test --project IniaAi.Core.Tests/IniaAi.Core.Tests.csproj \
  -- --filter-class "*ReconciliationServiceTests*"
```

#### Database-backed integration tests (local only)

Some `IniaAi.WebApi.IntegrationTests` apply real EF Core migrations against live database servers. They read connection strings **only** from the `INIA_TEST_MSSQL` and `INIA_TEST_PG` environment variables and **skip** (rather than fail) when those are unset, so they never block a normal `dotnet test`. Credentials are never hard-coded in the repo — supply them through the environment. The `Database` is overridden per test (a throwaway database created and dropped on teardown), so only the host and credentials matter in the value you supply. Replace `<password>` with your local server password:

```bash
INIA_TEST_MSSQL="Server=localhost,1433;User Id=sa;Password=<password>;TrustServerCertificate=True" \
INIA_TEST_PG="Host=localhost;Port=5432;Username=postgres;Password=<password>;Database=postgres" \
dotnet test --project IniaAi.WebApi.IntegrationTests/IniaAi.WebApi.IntegrationTests.csproj \
  -- --filter-class "*MigrationsApplyFromEmptyTests*"
```

A test whose server is missing skips with `No local PostgreSQL reachable (set INIA_TEST_PG to enable).` or `No SQL Server reachable (set INIA_TEST_MSSQL to enable).`

`MigrationsApplyFromEmptyTests` applies the full migration set to an empty database on **both** providers — the guard that catches provider-specific DDL breakage (for example SQL Server's "multiple cascade paths") that a Postgres-only run misses.

#### Editor-app component tests (bUnit)

`IniaAi.WebApp.Tests` renders Blazor components with [bUnit](https://bunit.dev) and asserts what a signed-in user sees. A test class derives from `SignedInRenderTest` (`platform/IniaAi.WebApp.Tests/Components/`), which sets up MudBlazor, the real `AppState` over a SQLite user-data database, and bUnit's authentication state:

```csharp
public class SomeCardTests : SignedInRenderTest
{
    [Fact]
    public async Task AnOperator_SeesManageTenants()
    {
        _ = await SignInAsync("someone@example.com", Roles.USER, Roles.OPERATOR);

        IRenderedComponent<SomeCard> card = RenderInApp<SomeCard>(parameters => parameters.Add(p => p.Items, items));

        card.WaitForAssertion(() => Assert.True(Shows(card, "Manage tenants of workflow type Credit Review")));
    }
}
```

- bUnit stands in for the browser: JavaScript interop (loose mode, because MudBlazor makes calls of its own), navigation, and the sign-in state. That is the external boundary the no-mocks rule allows. The database, `AppState` and the app's services are real.
- `SeedUserAsync` and `SeedAsync` write to the test database; `SignInAsync` seeds a user and signs them in. Seed and sign in before the first render.
- `RenderInApp` renders the component beside MudBlazor's popover provider, as the app's layout does; menu items and tooltips appear in `Popovers`. A layout that renders its own providers (`MainLayout`) is rendered with `Render`.
- Register extra services in the derived class's constructor. bUnit refuses a registration after the first render or the first database read.
- Find controls by accessible name or button text (`Shows`), not by MudBlazor CSS classes, which change between MudBlazor releases.
- Wait for anything a component loads in `OnInitializedAsync` (`WaitForAssertion`, `WaitForElement`); never sleep.
- Rendering a page directly ignores its `[Authorize]` attribute, which the router enforces. Page gates are attribute tests (`PageRoleGatesTests`); render tests cover what shows inside a page.
- Use the bUnit 2 API (`BunitContext`, `Render<T>`). Most examples online are bUnit 1 (`TestContext`, `RenderComponent<T>`). In a file that imports both `Bunit` and `Xunit`, write `Xunit.TestContext.Current`.


## Performance and Cost Optimization


> [!WARNING]
> **Token Usage Optimization**
>
> - Use **small test documents** (10 pages with minimal content)
> - **Mark documents as static** to avoid re-uploading
> - **Batch commits** to avoid multiple CI runs
> - **Use draft PRs** during development
> - **Cancel unnecessary runs** in the Actions tab

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| `A test called the real model, and local test runs make no paid calls.` | The code under test reached a paid connector | Give the test a `ScriptedChatConnector` (or a `WordHashEmbeddingsConnector`), or mark it `[PaidApiFact]` |
| A test passes but its output shows that refusal | A service caught the refusal and carried on | Same fix: the test exercised less than it claims |
| `Check test times` fails with every test green | A project ran over its budget in `test-time-budgets.json` | Find the slow test in the run summary; speed it up, or raise the budget with the reason in the pull request |
| A test over 30 seconds fails | It has no `[SlowTest("…")]` | Speed it up, mark it `[SlowTest("what the time buys")]`, or move it to `[NightlyFact("…")]` |
| `FixedWaitGuardTests` fails | Test code calls `Thread.Sleep` or `Task.Delay` | Wait on a signal, `Eventually.UntilAsync` or `SimulatedWork.TakesAsync`, or add a `// Waits on purpose: <reason>` comment |
| A VSTest flag such as `--filter` has no effect | MTP ignores VSTest flags | Pass MTP options after `--`, such as `-- --filter-class "*SomeTests*"` |



## Related pages

- [C# Code Standards](csharp-code-standards.md) — test rules (plain xUnit, real instances, NSubstitute only at an external boundary).
- [Contributing](../../CONTRIBUTING.md) — how a change gets in, and the bar for each part.
- [Database Migrations](database-migrations.md) — migrations applied by the integration tests.

