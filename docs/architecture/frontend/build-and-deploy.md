---
type: explanation
status: current
description: "Documents the Vite build and its chunks, the npm scripts, the build-time environment variables, the stale-chunk reload, and the nginx Docker image with its proxy target and routes. Read when you change the frontend build, an environment variable, the web image or its nginx config, or debug a deployed SPA that cannot reach the API."
---

# Build & Deployment

**On this page:** Vite build · npm scripts · Environment variables · Production deployment (nginx Docker image)

The IniaAi.Web frontend is a Vite-built Vue 3 single-page application. The repository produces a static `dist/` bundle, and its own `Dockerfile` builds the production artifact: an nginx image. One image serves any backend without a rebuild, because the bundle calls the API at its own origin and nginx proxies that path to the backend that the container names at start.

## Vite build

The build requires **Node >= 24.11.0** (`engines.node` in `package.json`; the Docker build stage uses `node:24-alpine`). `npm run build` runs two steps in parallel via `npm-run-all2` (`run-p`): a full TypeScript type-check (`vue-tsc --build --force`) and the actual `vite build` (`build-only`). Output lands in `dist/`; the files in `public/` (the browser-tab icons, see [Brand marks](components-and-design-system.md#brand-marks)) are copied to the root of `dist/` unchanged, and nginx serves them through `try_files $uri`. Local development runs `vite` directly on **port 8080** with [`vite-plugin-mkcert`](https://github.com/liuweiGL/vite-plugin-mkcert), which provisions a locally trusted HTTPS dev certificate (it is skipped under Vitest, where the trust-install step needs `sudo`).

Build configuration lives in `vite.config.ts`. The project builds on the Rolldown-based Vite toolchain, so code-splitting is configured through `build.rolldownOptions.output.codeSplitting.groups` rather than the classic `manualChunks`. Heavy `node_modules` dependencies are split into named vendor chunks:

| Chunk | Packages |
|---|---|
| `vendor-vue` | `vue`, `vue-router`, `pinia`, `@vueuse/core` |
| `vendor-ui` | `reka-ui`, `@lucide/vue` |
| `vendor-charts` | `echarts`, `zrender` |
| `vendor-forms` | `vee-validate`, `@vee-validate/zod`, `zod` |
| `vendor-icons` | `@fortawesome/*` |
| `vendor-data` | `@tanstack/vue-query`, `@tanstack/vue-table`, `axios` |
| `vendor-utils` | `date-fns`, `class-variance-authority`, `tailwind-merge` |

### The separate chart-builder build

A second, independent build target (`vite.config.chart-builder.ts`) compiles `src/api/dynamicChart/chartExport.global.ts`, with the ECharts code it uses, into a minified IIFE library, `chart-builder/charts.global.js` (global name `IniaCharts`), and writes the licence and notice files of ECharts and zrender beside it. This is a standalone artifact consumed outside the SPA — the folder is copied into the backend's services resources and the bundle is inlined into the Word-export render path. Because it is committed to the repo, `npm run verify:chart-builder` rebuilds it and fails the pipeline if the checked-in folder is stale. See [Charts](charts.md) for the chart rendering architecture.


## npm scripts

| Script | Command | Purpose |
|---|---|---|
| `dev` | `vite` | Dev server on port 8080 (HTTPS via mkcert) |
| `build` | `run-p type-check "build-only {@}" --` | Type-check + production build to `dist/` |
| `build-only` | `vite build` | Production bundle without type-check |
| `build:chart-builder` | `vite build --config vite.config.chart-builder.ts` | Build the standalone chart-builder IIFE |
| `verify:chart-builder` | build + `git status --porcelain -- chart-builder/` | Fail if the committed chart-builder folder is out of date, naming the files that differ |
| `type-check` | `vue-tsc --build --force` | Standalone TypeScript / template type-check |
| `lint` | `eslint . --fix` | Lint and auto-fix |
| `format` | `prettier --write src/` | Format `src/` |
| `test` | `vitest run` | Run the Vitest unit suite once |

See [Testing](testing.md) for the test setup.


## Environment variables

Vite compiles each `VITE_*` variable into the bundle at build time. `.env.example` documents the local development values.

| Variable | Read by | When unset |
|---|---|---|
| `VITE_API_URL` | `src/config/api.ts` | `/`: the app's own origin, where nginx proxies the API |
| `VITE_OAUTH_GRANT_TYPE` | `src/config/auth.ts` | `password` |
| `VITE_OAUTH_SCOPE` | `src/config/auth.ts` | `offline_access api` |
| `VITE_VERSION` | `src/components/layouts/DefaultLayout/DefaultLayout.vue` | the version line at the foot of the layout is absent |

No other `VITE_*` variable is read in the shared code: a search of `src/` outside `src/modules/` for `import.meta.env.VITE_` finds these four. The image build sets `VITE_VERSION`, `VITE_TERMS_URL` and `VITE_PRIVACY_URL` from the build arguments `VERSION`, `TERMS_URL` and `PRIVACY_URL`, each empty unless the build passes it; only the paid build's self-serve module reads the last two. The other variables keep their defaults, so one bundle serves every environment.



## Production deployment (nginx Docker image)

The production artifact is an nginx image. The frontend repository holds its recipe: `Dockerfile`, with the entrypoint script and the nginx config under `docker/`. It builds with the repository folder as its only context and needs no credential:

```bash
docker build --build-arg VERSION=1.2.3 -t inia-web .
```

The platform repository's `compose.yaml` builds the same image for the one-command stack; see [Docker builds](../../getting-started/docker-builds.md).

### Multi-stage Dockerfile

`Dockerfile` is a two-stage build:

```dockerfile
FROM --platform=$BUILDPLATFORM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
ARG VERSION=""
ARG TERMS_URL=""
ARG PRIVACY_URL=""
RUN VITE_VERSION="$VERSION" VITE_TERMS_URL="$TERMS_URL" VITE_PRIVACY_URL="$PRIVACY_URL" npm run build

FROM nginx:alpine@sha256:<digest>
COPY --from=build /app/dist /usr/share/nginx/html
RUN rm -f /docker-entrypoint.d/10-listen-on-ipv6-by-default.sh
RUN apk upgrade --no-cache && nginx -v
COPY --chmod=755 docker/40-proxy-backend.sh /docker-entrypoint.d/40-proxy-backend.sh
COPY docker/nginx.conf /etc/nginx/conf.d/default.conf
HEALTHCHECK ... CMD wget --quiet --tries=1 --spider http://127.0.0.1:80/health || exit 1
```

The first stage runs `npm ci` and `npm run build` on the build machine's own CPU, because the bundle is the same for amd64 and arm64. It passes the build arguments `VERSION`, `TERMS_URL` and `PRIVACY_URL` as `VITE_VERSION`, `VITE_TERMS_URL` and `VITE_PRIVACY_URL`. The second stage copies the resulting `dist/` into `nginx:alpine`, pinned by digest, at `/usr/share/nginx/html`. The pinned digest is a multi-architecture index, so the image builds for amd64 and arm64. The second stage deletes the stock `10-listen-on-ipv6-by-default.sh` entrypoint script, because that script checksums the packaged `default.conf` with `apk manifest nginx` before it edits it, which hangs on a host with no route out; the image replaces that file anyway. It upgrades the Alpine packages, so the image carries the fixes Alpine published after the pinned digest was built; the digest also pins the nginx version, which only a new digest moves (Dependabot proposes one each week). It prints `nginx -v` into the build log. A Docker `HEALTHCHECK` polls `/health` every 30s.

A new image holds only the chunks of its own build, so a deploy removes the chunks of the build before it. A tab opened before the deploy then fails to load its next lazy screen. `src/lib/staleChunkReload.ts` listens for Vite's `vite:preloadError` event and reloads the page once, so the tab picks up the new build. The reload keeps the current address, and the next click opens the screen. A second failure within ten seconds goes through without a reload, so a chunk that the new build also lacks cannot make the page reload in a loop.

### The proxy target at container start

`docker/40-proxy-backend.sh` is installed as `/docker-entrypoint.d/40-proxy-backend.sh`, so the stock nginx entrypoint runs it before nginx starts. It edits only the nginx config: it writes `PROXY_BACKEND_URL` (default `http://api:80`, trailing slash stripped) as the proxy target, and the container's own DNS server, the first `nameserver` in `/etc/resolv.conf`, as its `resolver`. It leaves the bundle alone, and it reads no OAuth client id or secret: the app names no OAuth client (see [Application configuration](../../operations/application-configuration.md)).


### nginx configuration

`docker/nginx.conf` serves the SPA and reverse-proxies the backend:

| Location | Behavior |
|---|---|
| `/api/` | `proxy_pass` to the backend; 300s read/send timeouts; standard `X-Forwarded-*` headers |
| `/` | SPA fallback: `try_files $uri $uri/ /index.html` |
| `= /health` | Returns `200 'ok'` as plain text (target of the Docker `HEALTHCHECK`). The exact match outranks the SPA fallback, so the SPA's own `/health` route never loads on a deployment; see the Health screen note in [API layer](api-layer.md#module-inventory) |

The `/api/` proxy target reads the variable `$api_backend`, which `40-proxy-backend.sh` sets to `PROXY_BACKEND_URL` at start. nginx resolves a variable target per request through `resolver` and caches the answer for 5 seconds, so a recreated api container is found at its new address. A literal target would be resolved only once, when nginx starts. The server also sets security headers on every response, errors included (`X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `X-XSS-Protection`, `Referrer-Policy` and a `Content-Security-Policy`, below), allows `client_max_body_size 100m` for document uploads, and enables gzip. The `/api/` proxy relationship is described from the API side in [API Architecture](../multi-tenant/api-architecture.md); how the app follows a run without server push is in [Realtime & Polling](realtime-and-polling.md).

> [!TIP]
> **Azure Static Web Apps fallback is legacy**
>
> The repo also ships `staticwebapp.config.json`, which only configures Azure Static Web Apps' `navigationFallback` to rewrite unmatched routes to `index.html` (excluding static asset extensions). This is a secondary/legacy serving path — the nginx Docker image above is the primary production mechanism, and it implements the same SPA fallback via `try_files`.

### Content-Security-Policy

The policy lets scripts run only from the app's own origin and from Google's sign-in library (`https://accounts.google.com/gsi/client`), so text from a model that slips past the app's sanitising cannot run. Google sign-in also gets its frames and stylesheet. `connect-src` allows `'self'` and any HTTPS origin, because each deployment names its OIDC issuer in the API's settings at runtime. A deployment can narrow it: `docker/41-content-security-policy.sh`, which the entrypoint runs after `40-proxy-backend.sh`, writes `CSP_CONNECT_SRC` into `connect-src` in place of `'self' https:`. With `CSP_MODE=report-only` the server keeps enforcing the default policy and also sends the narrowed one as `Content-Security-Policy-Report-Only`, so violations show in the browser console before the narrowed policy is enforced; there is no reporting endpoint. A value with characters that could leave the header or the nginx directive, or a source that would widen `connect-src` (`*`, `http:`, `ws:`, `wss:`, `data:`, `blob:`, an `http://` origin, `'unsafe-…'`), stops the container at start with a message that names the setting. A deployment that signs in through an OIDC issuer must list the issuer's origin in `CSP_CONNECT_SRC`. Previews and downloads open `blob:` URLs, which `img-src`, `frame-src` and `worker-src` allow. `object-src 'none'` and `frame-ancestors 'none'` close plug-ins and framing.

