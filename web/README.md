# Inia AI Web

The browser app of the Inia AI platform. Analysts start workflow runs, read and edit the reports, and manage documents, counterparties and signatures. The app calls the platform's WebApi for all of its data.

## Stack

TypeScript, Vue 3 with the Composition API, Vite, Pinia and Vue Router. The interface uses Tailwind CSS and reka-ui, the text uses Vue I18n, and the tests run on Vitest with jsdom.

## Before you start

You need these on your machine:

- Node.js 24.11 or later. `package.json` sets this floor in `engines`, and `.nvmrc` names Node 24.

- A running platform WebApi. The app calls the address in `VITE_API_URL`, which is `https://localhost:7557` in `.env.example`. The platform repository explains how to start the WebApi.

- A user on that WebApi. On the first start of an empty database, the WebApi creates the account `superadmin@inia.ai`. Its password is the value of the WebApi setting `Seeding:SuperAdminPassword`, and the first start stops when that setting is missing.

- Permission for the app's address. The WebApi answers browser calls only from the addresses in its `Cors:AllowedOrigins` setting, so that list must hold `https://localhost:8080`. The platform's launch template already holds it.

## Set up and run

1. Clone the repository and open its `web` folder, where this README sits.

2. Copy the settings file: `cp .env.example .env`. Change `VITE_API_URL` when your WebApi runs at another address.

3. Install the packages: `npm install`.

4. Start the development server: `npm run dev`. The first start downloads mkcert and installs a local certificate authority, so it asks for your administrator password. Later starts reuse that certificate.

5. Open `https://localhost:8080` and sign in as `superadmin@inia.ai`.

## Commands

| Command | What it does |
| --- | --- |
| `npm run dev` | Starts the development server on `https://localhost:8080`, with hot reload. |
| `npm test` | Runs the Vitest suite once. `npm run test:watch` keeps it running. |
| `npm run type-check` | Checks the types with `vue-tsc`. |
| `npm run lint` | Runs ESLint and applies its fixes. |
| `npm run build` | Checks the types and builds the production files into `dist/`. |
| `npm run verify:chart-builder` | Builds `chart-builder/` again and fails when the committed copy differs. The platform's Word export draws charts with a copy of that folder. |

## Layout

```
index.html                 the page that loads the app
src/main.ts                starts the app
src/App.vue                the root component
src/register.ts            registers the shared components
src/plugins/router/        the routes and the sign-in guard
src/store/                 the Pinia stores
src/api/                   one service per WebApi area, each built on BaseService
src/views/                 the pages
src/components/            atoms, molecules, organisms, layouts, and the reka-ui parts in ui/
src/composables/           composition functions
src/lib/                   helpers, among them the HTML sanitiser in renderContent.ts
src/modules/               the module registry and the extension points that the screens read
src/assets/locales/        the translations
chart-builder/             the chart option builder that the platform's Word export runs
```

Every call to the WebApi goes through a service class that extends `BaseService`. HTML reaches the page only through the `v-safe-html` directive in `src/lib/safeHtml.ts`, which sanitises it first. The lint rules refuse `v-html`.

## Docker image

`Dockerfile` builds the production image with this folder as the only context. nginx serves the app and proxies `/api/` to the WebApi at `PROXY_BACKEND_URL`, so the app calls the API at its own origin. The build argument `VERSION` sets the version line at the foot of the layout.

```bash
docker build --build-arg VERSION=1.2.3 -t inia-web .
docker run -p 8080:80 -e PROXY_BACKEND_URL=http://host.docker.internal:5775 inia-web
```

The platform repository's `compose.yaml` builds this image together with the rest of the product.

### Content Security Policy

nginx sends a Content Security Policy on every response (`docker/nginx.conf`). Two container settings change it when the container starts. A bad value stops the container from starting, with a message that names the setting and the fix.

| Variable | Default | What it does |
|---|---|---|
| `CSP_CONNECT_SRC` | `'self' https:` | The sources of the policy's `connect-src`, separated by spaces, for example `'self' https://accounts.google.com`. Each one is a quoted keyword such as `'self'`, or a scheme or host. A value that would allow more than the default is refused: `*`, `http:`, `ws:`, `wss:`, `data:`, `blob:`, an `http://` origin, or an `'unsafe-…'` keyword. So is a quote outside a keyword, a semicolon, a double quote, a `$`, a backslash, a tab or a line break. |
| `CSP_MODE` | `enforce` | `enforce` sends one `Content-Security-Policy` header with `CSP_CONNECT_SRC` applied. `report-only` keeps enforcing the default policy and adds a `Content-Security-Policy-Report-Only` header with `CSP_CONNECT_SRC` applied: the browser blocks only what the default policy blocks, and reports in its console each connection the narrower sources would block. No reporting endpoint is set. |

With both unset or empty, the header is the one every deployment sent before these settings existed, byte for byte. A new value applies when the container is recreated; a restart keeps the value it started with.

To narrow `connect-src` safely, start with `CSP_MODE=report-only`, use the app (sign-in, upload, preview, download) with the browser console open, add any source it reports, then switch to `CSP_MODE=enforce`. `'self'` covers the API, which nginx serves at the app's own origin. Google sign-in fetches from `https://accounts.google.com/gsi/`. A deployment that uses an OIDC sign-in provider needs that provider's issuer origin too, because the app fetches the issuer's discovery document and token endpoint from the page.

## Contributing

Each pull request runs the checks in `.github/actions/checks` and `.github/actions/tests`. Run the same gate on your machine before you push:

```bash
npm run type-check && npm run lint && npm run verify:chart-builder && python3 scripts/notices/notices.py check && npm test && npm run build
```

## Licence and third-party notices

The code is licensed under the Apache License 2.0, in `LICENSE`. `NOTICE` names the copyright holder. `THIRD-PARTY-NOTICES.md` names every file and package in the build that other people wrote, with its licence. To report a vulnerability, follow `SECURITY.md`.

When you add, remove or update a package, write the package list again with `python3 scripts/notices/notices.py write` (Python 3.11 or later), and commit it with the change. CI runs `python3 scripts/notices/notices.py check`. It fails when the list is out of date, or when a package's licence is outside `scripts/notices/policy.json`, and its message says what to change. The script's own description explains the policy.
