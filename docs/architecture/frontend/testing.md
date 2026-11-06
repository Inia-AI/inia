---
type: explanation
status: current
description: "Documents the Vue app's Vitest stack and config, the colocated-test convention, the shared setup (Storage polyfill, auto-unmount, scrolling stubs), every shared test helper, where tests exist and the discipline expected. Read when you write or debug a frontend test, or look for an existing helper before writing one."
---

# Testing

**On this page:** Stack · Colocated `*.test.ts` convention · Shared setup: the Storage polyfill · Test helpers · Coverage scope · Discipline · CI gate · Related pages

The IniaAi.Web frontend ships a [Vitest](https://vitest.dev/) unit-test suite that runs against a jsdom DOM and Vue components rendered with `@vue/test-utils`. Tests are colocated with the code they cover, the config lives in `vite.config.ts`, and the suite is one of the five gates CI runs on every pull request. This page documents the stack, conventions, the shared setup that makes jsdom usable, the test helpers, and the discipline expected when you touch code.

## Stack

| Concern | Tool | Version (`package.json`) |
|---|---|---|
| Test runner | `vitest` | `^4.1.11` |
| Component mounting | `@vue/test-utils` | `^2.4.10` |
| DOM environment | `jsdom` | `^29.1.1` |
| Assertions | Vitest built-in `expect` | (bundled) |

Vitest is configured directly inside the Vite config (`vite.config.ts`) via the `vitest/config` entry point, so tests and the app share the same resolver, plugins, and path aliases (`@`, `@atoms`, `@layouts`, `@ui`):

```ts
test: {
  environment: 'jsdom',
  pool: 'threads',
  globals: true,
  include: ['src/**/*.{test,spec}.{ts,vue}'],
  setupFiles: ['src/test-setup.ts'],
  css: { include: [/\.css\?raw$/] },
},
```

`pool: 'threads'` runs test files on worker threads. `css.include` lets a test import a stylesheet as text with `?raw`, as the contrast guard does for `main.css` (`src/test/helpers/themeColors.ts`).


`globals: true` means `describe` / `it` / `expect` / `beforeEach` are available without imports (most files import them anyway for clarity). The Vue plugin runs under Vitest, so `.vue` SFCs compile in tests exactly as they do in the app. The `mkcert` dev-cert plugin is skipped when `process.env.VITEST` is set, because its trust-install step needs sudo and is irrelevant to tests.

Run commands:

```bash
npm test          # vitest run — single pass, used by CI
npm run test:watch # vitest — interactive watch mode
```

## Colocated `*.test.ts` convention

Tests live next to the code they cover, named `*.test.ts` (or `*.spec.ts`, and `.vue` test variants). For example `src/lib/utils.test.ts` sits beside `src/lib/utils.ts`, and component tests live inside the component's own folder (`src/components/atoms/ThemeToggle/ThemeToggle.test.ts`).

These files are compiled by Vitest but **excluded from `vue-tsc`** so the production type-check (`npm run type-check`) does not type-check test code. `tsconfig.app.json` carries the exclusions:

```json
"exclude": [
  "src/**/__tests__/*",
  "src/**/*.test.ts",
  "src/**/*.spec.ts",
  "src/**/*.test.vue",
  "src/**/*.spec.vue",
  "src/test-setup.ts"
]
```

> [!TIP]
> Because tests are outside the `vue-tsc` graph, a type error that only exists in a test file will surface when you run `npm test`, not `npm run type-check`. Run the full gate before calling work done.

## Shared setup: the Storage polyfill

`src/test-setup.ts` is wired in through `setupFiles` and runs once before the suite. Its job is a workaround: under Vitest 4 + jsdom 29 in this repo, the `localStorage` / `sessionStorage` exposed on the test global are missing their `Storage` prototype methods (`getItem`, `setItem`, `clear`, …), so calls throw "is not a function". A bare `new JSDOM()` does not reproduce it, so the fault is in the Vitest/jsdom integration, not jsdom itself.

Rather than pin versions or chase the upstream bug, the setup installs a Map-backed `Storage` implementation and defines it on both `globalThis` and `window`:

```ts
class MapStorage implements Storage {
  private store = new Map<string, string>()
  get length() { return this.store.size }
  clear() { this.store.clear() }
  getItem(key: string) { return this.store.has(key) ? (this.store.get(key) ?? null) : null }
  key(index: number) { return Array.from(this.store.keys())[index] ?? null }
  removeItem(key: string) { this.store.delete(key) }
  setItem(key: string, value: string) { this.store.set(key, String(value)) }
}
```

A `beforeEach` hook calls `localStorage.clear()` and `sessionStorage.clear()`, so each test starts with empty storage — important because auth tokens are stored in `localStorage` and many tests seed one.

The setup does three more things:

- `enableAutoUnmount(afterEach)` unmounts every component a test mounted, because a view left mounted keeps watching shared state and recomputes whenever a later test in the file changes it.
- It stubs `Element.prototype.scrollIntoView`, which jsdom does not implement, so a component that scrolls an element into view runs instead of throwing.
- It stubs `Range.prototype.getClientRects` and `getBoundingClientRect`, because jsdom gives a range no rects and the rich-text editor scrolls its caret into view on focus.

> [!WARNING]
> Do not remove the polyfill without re-verifying that `localStorage.clear()` works in a plain jsdom test. The inert-Storage failure is silent until a test actually calls a Storage method.

## Test helpers

Shared helpers live in `src/test/helpers/`.

### JWT factory (`jwt.ts`)

Authentication is JWT-based and the production `hasRole` / `getDecodedToken` / `isTokenValid` utilities only **decode** the token — they never verify the signature. That means a syntactically valid JWT with a base64url-encoded payload is enough to exercise role-gated paths:

```ts
import { setAuthToken, makeJwt, clearAuthToken } from '@/test/helpers/jwt'

setAuthToken({ role: 'ADMIN' })                 // writes accessToken to localStorage
setAuthToken({ role: ['ADMIN', 'OPERATOR'] })    // multi-role
makeJwt({ sub: 'user-1', exp: 9999999999 })      // raw token string
clearAuthToken()
```

`setAuthToken` defaults `exp` to a far-future value and stores the token under the `accessToken` key — the same key the app reads. `makeJwt` UTF-8-encodes the payload before `btoa` so non-Latin claims survive. `utils.test.ts` uses these helpers to cover `hasRole`, `hasMultipleTenants`, `isTokenValid`, and expired-token handling.

### reka-ui / lucide stubs (`rekaStubs.ts`)

Component tests that mount molecules built on reka-ui primitives (Popover, Command, ScrollArea) stub those primitives with lightweight pass-through components so the test focuses on the unit's own logic rather than the third-party widget's internals. `lucideModule(names)` generates empty stub components for named lucide icons. These are plain Vue components rendered for real (slots forwarded, `v-model` wired on the command input) — they are stand-ins, not mocks.

### The other helpers

| Helper | What it gives a test |
|---|---|
| `axiosError.ts` | An API error answer with a status and a body, shaped as axios rejects it |
| `messages.ts` | `EN`: the open messages merged with every module's copy, as the app merges them |
| `reportsListMocks.ts` | A mocked reports-list query (`reportRows`, the last query parameters) and `mountReportsList`, which mounts the list through `withReportsListExtensions` |
| `uploaderMocks.ts` | A mocked `@/api` whose upload call (`uploadMutateMock`) the run uploader uses |
| `layoutMenus.ts` | A mocked `vue-router` (`pushMock`) for mounting `DefaultLayout` and its menus |
| `tiptapEditor.ts` | Caret and selection helpers for the rich-text editor tests (`positionOf`, `replaceWords`) |
| `rowControls.ts` | `describeRowControls`, the row-control guard of each `tables/` folder ([Row actions and selection](forms-tables-and-composables.md#row-actions-and-selection)) |
| `sourceFiles.ts`, `templateAst.ts`, `cssRules.ts` | The repository's source files, a parsed template tree and parsed CSS rules, for the guards that read source instead of a rendered page |
| `themeColors.ts`, `colorUsages.ts`, `targetSizes.ts`, `headingOutline.ts` | The contrast, target-size and heading guards |


## Coverage scope

The suite has test files in every layer:

| Layer | Examples | Notes |
|---|---|---|
| Utilities (`src/lib/`, `src/utils/`) | `utils.test.ts`, `formatBytes`, `renderContent`, `counterpartiesTree`, `reportLayout`, `workflowReRun*` | Pure functions, easy to test exhaustively |
| Composables (`src/composables/`) | `useDebounce`, `useLiveDuration`, `useDocumentUpload`, `useCancelExecution`, query-state hooks | |
| Stores (`src/store/`) | `theme`, `createWorkflowPrefill`, `agentActivity` | |
| Components | atoms (`ThemeToggle`, `HighlightedText`), molecules (`IniaSelect`, `SearchableSelect`, several dialogs), organisms (`DataTable`, `TiptapEditor`, the uploader), some `ui` primitives | reka-ui-backed pieces use the stubs above |
| API services (`src/api/`) | `documents`, `pages`, `executedWorkflows`, `files`, `authentication`, `dynamicChart/buildChartOptions`, `dynamicChart/chartExport` | Most service modules have no test of their own |
| Views (`src/views/`) | `DocumentsList`, `DocumentDetails`, `WorkflowDetails`, `CreateWorkflow`, `signatures/helpers` | About twenty view tests |
| Guards (`src/*.test.ts`) | headings, text contrast, target size, table headers, label in name, app shell, browser-tab icons | Read the source, not a rendered page |


No coverage reporting is configured: `@vitest/coverage-*` is not a dependency and `npm test` produces no coverage report. "Coverage" here means breadth of files with tests, not a measured percentage.

> [!NOTE]
> The API layer and views are the least-covered surfaces — most API modules and many route-level views have no tests. When you change one, add the test rather than treating its absence as license to skip.

## Discipline

Two testing expectations apply:

- **TDD is the default.** When fixing a bug or adding behaviour, write a failing Vitest test first, then make it pass. Any non-trivial change should ship with new or updated tests.
- **If you genuinely can't test a change** (wiring a dev-only tool, a purely visual Tailwind tweak), say so explicitly in the PR description rather than silently skipping.

Two further conventions are observed in the existing tests rather than written down:

- **Real app components, stubbed third-party primitives.** Tests mount real application components with real fixtures; only heavy third-party UI primitives (reka-ui, lucide) are swapped for lightweight stubs via the shared test helpers. Application behaviour itself is not mocked.
- **No narration comments** in test files, the same as in source — intent goes in `describe` / `it` names, following the repo-wide comment rule.

## CI gate

CI runs the same five commands you should run locally before calling work done. One job runs type-check, lint, `verify:chart-builder` and build, and four `test` shards run the suite; locally, run them in this order:

```bash
npm run type-check && npm run lint && npm run verify:chart-builder && npm test && npm run build
```

Tests are **not** in the Husky pre-commit hook (kept fast — the hook runs only type-check and lint), so the suite first runs against your branch in CI. Don't skip hooks, disable rules, or mark tests `.skip` to get past a red state — fix the root cause. See [Build & Deploy](build-and-deploy.md) for what the `build` step produces.


## Related pages

- [Frontend Overview](index.md) — stack and project layout
- [API Layer](api-layer.md) — the `BaseService` pattern the API service tests exercise
- [State, Routing & Auth](state-routing-and-auth.md) — the JWT/role model the test helpers stand in for

