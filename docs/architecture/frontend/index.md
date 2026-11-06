---
type: explanation
status: current
description: "Overview of the Vue 3 SPA: its tech stack, repository layout, bootstrap flow, local dev quickstart and the desk preview page. Read when you start work in the Vue app or need to know which library or folder owns a concern."
---

# Frontend (IniaAi.Web — Vue SPA)

**On this page:** Two user interfaces, one API · Tech stack · Repository layout · Bootstrap flow · Dev quickstart · Where to go next

IniaAi.Web is the user-facing single-page application of the IniaAi platform: a Vue 3 + TypeScript SPA where end users run workflows, read generated reports, and manage counterparties and templates. It lives apart from the .NET platform solution, in `web/` of the open repository, and communicates with the backend exclusively over the REST API. This section documents its architecture for maintainers.

> [!NOTE]
> **Separate code tree**
>
> The Vue SPA is not part of the .NET solution. It is built, versioned, and deployed independently and consumes the same REST API described in [API Architecture](../multi-tenant/api-architecture.md). Source paths in this section (`src/...`) are relative to the Vue app's folder, `web/` in the open repository, not to the platform.


## Two user interfaces, one API

The platform ships two distinct front ends that target different audiences but call the **same REST API**:

| UI | Tech | Audience | Purpose |
|---|---|---|---|
| **IniaAi.Web** (this section) | Vue 3 SPA | End users | Run workflows, view reports/dashboards, manage counterparties and templates |
| **IniaAi.WebApp** | Blazor Server | Admins / prompt engineers | Author and edit workflow templates and prompts |

The Blazor admin/editor UI is documented separately in [Multi-Tenant → The two front ends](../multi-tenant/index.md#the-two-front-ends-and-tenant-context). The two are independent applications; this page covers only the Vue SPA.

## Tech stack

Versions below are taken from `package.json`.

| Concern | Library | Version |
|---|---|---|
| Framework | Vue (Composition API) | 3.5.x |
| Language | TypeScript (`strict`) | ~6.0.x |
| Build tool | Vite | 8.x |
| State | Pinia | 3.x |
| Routing | Vue Router | 5.x |
| Server state / caching | `@tanstack/vue-query` | 5.x |
| Tables | `@tanstack/vue-table` | 8.x |
| Styling | Tailwind CSS (via `@tailwindcss/vite`, no `tailwind.config`) | v4 |
| UI primitives | reka-ui | 2.x |
| Forms / validation | vee-validate + `@vee-validate/zod` + zod | 4.x / 3.x |
| i18n | vue-i18n | 11.x |
| Charts | Apache ECharts | 6.x |
| HTTP | axios | ^1.16 |
| Single sign-on | `oidc-client-ts` | 3.x |
| Markdown and HTML sanitising | `marked` + DOMPurify | 18.x / 3.x |
| Composition utilities | `@vueuse/core` | 14.x |
| Rich text | TipTap | 3.x |
| Icons | Font Awesome + `@lucide/vue` | 7.x / 1.x |

> [!TIP]
> **Tailwind v4 has no config file**
>
> Tailwind is wired entirely through the `@tailwindcss/vite` plugin in `vite.config.ts`; there is no `tailwind.config.js`. Theme tokens are CSS custom properties defined in `src/assets/main.css`.

## Repository layout

The application source lives under `src/`:

| Path | Contents |
|---|---|
| `src/api/` | Per-domain API service modules (`base/`, `authentication/`, `workflows/`, `counterparties/`, `documents/`, …), each with `index.ts` / `service.ts` / `schema.ts` |
| `src/components/atoms/` | Basic building blocks (e.g. `IniaIcon`, `IniaLoader`) |
| `src/components/molecules/` | Simple compositions (selects, `WorkflowStatus`) |
| `src/components/organisms/` | Complex components (data tables, forms, `SourceChainPanel`) |
| `src/components/ui/` | reka-ui-based primitives (button, dialog, form, table, …) |
| `src/components/layouts/` | `DefaultLayout`, `BlankLayout` |
| `src/composables/` | Reusable Composition-API hooks (e.g. `refreshToken`, `useDebounce`) |
| `src/config/` | API and auth settings read from the environment (`api.ts`, `auth.ts`) |
| `src/constants/` | Shared constants: role names and the composite role gates, the AI model ids, the dropdown render cap |
| `src/utils/` | Pure helpers without Vue state: citations, OData, focus, page-preview and source-chain events |
| `src/test/helpers/` | Shared test helpers; see [Testing](testing.md) |
| `src/store/` | Pinia stores |
| `src/plugins/` | Plugin wiring: `router/`, `i18n/` |
| `src/views/` | Route-level page components |
| `src/tables/` | Table column / config definitions |
| `src/enums/` | Shared enums (`workflow`, `counterparty`, `api`, …) |
| `src/types/` | Shared TypeScript types and `.d.ts` declarations |
| `src/lib/` | Domain helpers and query builders (TanStack Query helpers, formatters), colocated with `*.test.ts` |
| `src/assets/locales/` | vue-i18n message catalogs (`en/`) |
| `src/modules/` | The paid modules, one folder each, and the extension points the open screens read (`extensionPoints.ts`); the open build holds only the extension points |

Path aliases are configured in `vite.config.ts`: `@/` → `src/`, `@atoms/`, `@layouts/`, `@ui/`.

## Bootstrap flow

`src/main.ts` first installs the stale-chunk reload, then either finishes a single sign-on popup or boots the app:

```ts
reloadOnStaleChunk()

if (isOidcPopupCallback()) {
  window.name = ''
  import('@/lib/oidc')
    .then(({ completeOidcPopupCallback }) => completeOidcPopupCallback())
    .catch(() => window.close())
} else {
  const app = createApp(App)

  registerModules(router)
  app.use(createPinia())
  app.use(router)
  app.use(i18n)
  app.use(VueQueryPlugin, {
    queryClient: new QueryClient({
      defaultOptions: { queries: { retry: retryUnlessClientError } },
    }),
  })

  registerPlugins(app)

  app.mount('#app')
}
```

- `reloadOnStaleChunk` (`src/lib/staleChunkReload.ts`) reloads the page once when a chunk of an older build fails to load; [Build and deploy](build-and-deploy.md) owns the details.
- `isOidcPopupCallback` recognises the window that a single sign-on provider redirected back to. That window relays the response to its opener and never boots the app; [SSO / OIDC](../../features/user-management/sso-oidc.md) owns the flow.
- `retryUnlessClientError` (`src/lib/queryRetry.ts`) is the default retry of every TanStack query: a 4xx answer fails at once, and a network fault or a 5xx answer is tried up to three times.

`registerModules` (`src/modules/index.ts`) registers the paid modules before the router starts; the open build registers none. `registerPlugins` (`src/register.ts`) loads the Font Awesome icon library and globally registers every component exported from `@/components/atoms` and `@/components/organisms`.

`App.vue` gates the first render on the router being ready, then selects a layout per route:

```ts
router.isReady().then(() => {
  isLoading.value = false
})

watch(route, () => {
  const queryLayout = router?.currentRoute?.value?.query?.layout?.toString()
  const metaLayout = router?.currentRoute?.value?.meta?.layout?.toString()

  if (isSelf) {
    layout.value = queryLayout || metaLayout || 'default'
  }
})
```

The chosen layout maps to `DefaultLayout`, `BlankLayout`, or a bare `<RouterView />` (when `layout` is neither `default` nor `blank`). The `?layout` query parameter overrides `route.meta.layout`, and layout switching is only applied when the app is the top-level window (`isSelf`), so embedded/iframe contexts fall through to a bare router view.

Beside the layout, `App.vue` mounts three app-wide pieces: the `Toaster` ([Notifications and errors](notifications-and-errors.md)), the `SourceChainPanel` ([Source chain and citations](source-chain-and-citations.md)) and the `AgentActivityRail`, which polls the in-flight reports for agent activity ([Realtime and polling](realtime-and-polling.md)). The rail is not mounted on a public route (`meta.isPublic`, the sign-in pages) or before the router resolves the first route, because its poll would answer 403 without a session. `App.vue` also starts the refresh-token timer (`useRefreshToken`, see [State, routing and auth](state-routing-and-auth.md)).

## Dev quickstart

> [!WARNING]
> **Prerequisites**
>
> Node **>=24.11.0** is required (`engines.node` in `package.json`; `.nvmrc` pins major version `24`). Use `nvm use` to match.

In the open repository the SPA is `web/`: run the commands below from that folder.


```bash
cp .env.example .env
npm install
npm run dev
```

> [!WARNING]
> **`npm run dev` needs a running backend — it is not standalone**
>
> The dev server only serves the SPA; it has no data of its own. Every screen calls the REST API, so without a backend at `VITE_API_URL` the app loads but login and every query fail. Point `VITE_API_URL` at a backend:
>
> - **A local .NET WebApi** you run yourself — start `IniaAi.WebApi` (Swagger at `https://localhost:7557/swagger`). The shipped `.env.example` already sets `VITE_API_URL=https://localhost:7557`, which matches that WebApi, so the default works once the API is up.
>
>
> Whichever you choose, `VITE_API_URL` must point at the backend you sign in to, because authentication runs against that API too. The WebApi must be reachable from the browser and accept the dev origin (it runs over HTTPS with a trusted local certificate).

`npm run dev` starts Vite on **port 8080**. The `vite-plugin-mkcert` plugin provisions a locally trusted certificate so the dev server runs over HTTPS automatically (the mkcert step is skipped under Vitest). The backend base URL is configured through the `VITE_API_URL` environment variable; all HTTP requests route through `BaseService` (`src/api/base/`), which resolves paths against that base. Sign-in also reads `VITE_OAUTH_GRANT_TYPE` and `VITE_OAUTH_SCOPE`, which default to `password` and `offline_access api`. Copying `.env.example` to `.env` (above) sets `VITE_API_URL` to the local WebApi; see the [environment variable table](build-and-deploy.md#environment-variables) for what each one feeds.

### Desk preview

The desk preview is a second page, `preview.html`, that the dev server serves at `https://localhost:8080/preview.html`. Its entry, `src/preview-main.ts`, mounts the home desk (`HomeView`) inside `DefaultLayout` with the API services replaced by canned in-flight and draft reports and their agent activity, so the desk and its agent panel render without a backend or a sign-in. `?idle` shows the desk with no run in flight, and `?dark` turns on the dark theme. `vite build` reads only `index.html`, so the preview never ships.

### Local gate

The full local gate mirrored by CI (all six steps — `verify:chart-builder` and the notices check are real CI gates, do not skip them):

```bash
npm run type-check && npm run lint && npm run verify:chart-builder && python3 scripts/notices/notices.py check && npm test && npm run build
```

## Where to go next

- [API layer](api-layer.md) — `BaseService`, per-domain modules, axios + zod
- [Components and design system](components-and-design-system.md) — atomic design, reka-ui, Tailwind tokens
- [Forms, tables, and composables](forms-tables-and-composables.md) — vee-validate/zod, TanStack Table, hooks
- [State, routing, and auth](state-routing-and-auth.md) — Pinia, Vue Router guards, JWT/refresh flow
- [Charts](charts.md) — ECharts, the chart builder and the chart bundle
- [Realtime and polling](realtime-and-polling.md) — query polling, and why there is no live push
- [Internationalization](i18n.md) — vue-i18n catalogs and conventions
- [Testing](testing.md) — Vitest + `@vue/test-utils` + jsdom
- [Build and deploy](build-and-deploy.md) — Vite build, code splitting, nginx


### Features & guides

- [Signatures & overrides](signatures.md) — the signature-field override UI and playground
- [Documents: upload & viewer](document-handling.md) — upload, preview, and the page-image viewer
- [Rich text editor](rich-text-editor.md) — the TipTap content editor
- [Notifications & errors](notifications-and-errors.md) — the toast system and error flow
- [Source chain & citations](source-chain-and-citations.md) — provenance, citations, and the tracer UI
- [Adding a feature](contributing-a-feature.md) — end-to-end contribution guide

