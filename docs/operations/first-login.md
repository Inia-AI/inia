---
type: runbook
status: current
description: "Final deployment step: the two seeded admin accounts and where their passwords come from, which URL to open for the analyst app and the admin app, the signed-in check, and login troubleshooting with the exact startup errors. Read when a fresh deployment is up and nobody has signed in yet, or a first sign-in fails."
---

# First Login (after a fresh deploy)

**On this page:** You do not create the first account — it is seeded · Which URL do I open? · Log in to the end-user app (Vue SPA) · Log in to the admin app (Blazor) · After first login — make it yours · Done when · Troubleshooting: cannot log in · Related pages

This is the **final step of a customer deployment**. [Release Package Deployment](release-package-deployment.md) leaves you with three healthy containers — but "three containers report healthy" is not the same as "someone can log in and use the platform." This page closes that gap: how the **first** account exists, where its password comes from, which URL to open, and how to confirm you are actually in.

> [!NOTE]
> **Audience: the customer operator who just deployed**
>
> You are reading this because you brought the stack up in your own environment and now need to get a human signed in.
>

## You do not create the first account — it is seeded

There is no "register the first admin" screen. On every startup the API runs database seeding (`UserDataDbInitializer.Initialize`, invoked from `DatabaseConfigurator.InitializeDatabases`) **after** migrations are applied. That seeder guarantees two things exist before anyone logs in:

1. The organizations — an internal **Inia AI** org plus **your customer organization**.
2. Two login accounts — a cross-environment **super-admin** and a **per-customer admin**.

This runs idempotently on each boot, so a fresh deployment always comes up with a working admin account; you do not run a separate provisioning command. The passwords of those two accounts come from your configuration, set before the first start (see below).

### Which organization gets created

The customer organization is taken from one configuration value: `PlatformSettings:CustomerOrgId` (see [Application Configuration](application-configuration.md)). The shipped `appsettings.json` and the code default (`PlatformSettings.CustomerOrgId`) both set it to **`Default`**, a neutral organization; a customer deployment sets its own short identifier.

> [!WARNING]
> **`CustomerOrgId` decides the admin's organization — set it before first boot**
>
> The seeded customer admin, `admin@example.com`, belongs to the organization this value names. If you boot once with the wrong `CustomerOrgId`, the seeder creates the org and places the admin in it, and changing the value later does **not** move the already-seeded account. A later start under another value keeps that account and seeds no second customer admin, because the customer admin always takes the same user id; the rest of the seeding still runs. Confirm `CustomerOrgId` is correct in your config **before** the first startup.

### The two seeded accounts

| Account | Email (username for login) | Organization | Roles | Created when |
|---------|----------------------------|--------------|-------|--------------|
| **Super-admin** | `superadmin@inia.ai` | Inia AI | `SuperAdmin`, `Admin`, `User`, `WorkflowEditor` | Always |
| **Customer admin** | `admin@example.com` | your customer org | `Admin`, `User`, `WorkflowEditor`, and each module role granted to the customer admin | When `CustomerOrgId` is **not** `IniaAI` |

Both accounts are seeded with `EmailConfirmed = true`, so neither needs an email round-trip before it can sign in. A module role reaches the customer admin when its module registers it with `GrantedToTheCustomerAdmin` (`ModuleRole`). Source: `UserDataDbInitializer.SeedUsers` and `SeedUserRoles`; role constants in `Roles.cs`.


### Where the passwords come from

The source ships no password. Before the first start on an empty database, set both keys in the configuration of the API and the admin app (both seed): `Seeding:SuperAdminPassword` for the super-admin and `Seeding:CustomerAdminPassword` for the customer admin ([Application Configuration → First accounts](application-configuration.md#first-accounts)). An app that has to create an account and finds no password for it stops at startup with `Seeding:SuperAdminPassword is required to create superadmin@inia.ai on a new database. Set it in configuration, for example the environment variable Seeding__SuperAdminPassword.` (or the same for `Seeding:CustomerAdminPassword` and `admin@example.com`); a password that breaks the rule stops it with `Seeding:SuperAdminPassword must be at least 8 characters, and not one of the most commonly used passwords.` Once both accounts exist, the keys are not read again, so later starts need neither.

> [!CAUTION]
> **Change both passwords at first sign-in**
>
> The boot passwords sat in configuration, so treat them as placeholders: change both (and ideally enable two-factor) before the deployment is used for anything real. See [Authentication API → Two-Factor](../reference/api/authentication.md#5-two-factor-authentication-mfa).
>
> Never paste a real password into a shared file, ticket, or this kind of document.

## Which URL do I open?

The deployment ships **two** human-facing surfaces, both backed by the same seeded accounts. Which one a person uses depends on what they are doing:

| Surface | Container image | Default compose port | Login path | Who uses it |
|---------|-----------------|----------------------|------------|-------------|
| **End-user app** (Vue SPA) | `iniaai/web` | host `80` | app login screen → posts to the API token endpoint | Analysts: run workflows, upload documents, read reports |
| **Admin / workflow editor** (Blazor) | `iniaai/monitoring` | `7119` (HTTPS) / `5118` (HTTP) | `/Account/Login` | Admins and prompt engineers: edit workflow templates, manage users |

The ports above are the **compose defaults** (`docker-compose.yml`); a real deployment usually fronts these with a domain name and a TLS-terminating reverse proxy, so the address you actually open is the one your operator assigned. If you do not know it, ask the operator who deployed the stack.


For the very first login, use whichever surface matches your role. The walkthrough below covers both.

## Log in to the end-user app (Vue SPA)

1. Open the deployment's web address (the `iniaai/web` service — compose default host port `80`).
2. On the login screen, enter the seeded admin **email as the username** and its password:
   - Username: `admin@example.com`, or `superadmin@inia.ai`.
   - Password: the value you set in `Seeding:CustomerAdminPassword` or `Seeding:SuperAdminPassword`.
3. Submit.

Behind the scenes the SPA performs an OAuth **password grant** against the API (`POST /api/v1/connect/token`, documented in [Authentication API](../reference/api/authentication.md#1-password-grant-user-authentication)). It sends your user credentials and names no OAuth client ([Application Configuration → Vue SPA sign-in](application-configuration.md#vue-spa-sign-in-browser-login)). The web image builds the grant type and the scope into the app, so the web container needs no setting for them. If login fails with an error that is not a wrong-password error, check first that the web container reaches the API, see [Troubleshooting](#troubleshooting-cannot-log-in).

> [!TIP]
> **You are logged in when you see…**
>
> A successful login lands you on the application home, with the navigation menu (Dashboard, Reports, Documents, and the entries your roles open) and your user shown as signed in — **not** the login screen. If two-factor is enabled for the account, you will first be prompted for a 6-digit code (the login call returns `202` and you complete it at `/connect/token/mfa`); you are done once you reach the app home.

## Log in to the admin app (Blazor)

1. Open the monitoring service's address (the `iniaai/monitoring` service — compose default `https://<host>:7119`) and navigate to **`/Account/Login`**.
2. Enter the same seeded admin email and password as above.
3. Submit.

The admin login page honors the same `Authentication:*` configuration as the SPA: the password form appears only while `Authentication:Password:Enabled` is true, and a button is shown for each configured external provider (Google, or the OIDC provider under its configured display name). External sign-in uses a server-side redirect and requires the user to already exist in the platform — see [SSO / OIDC](../features/user-management/sso-oidc.md#admin-app-blazor-login). For the *first* login of a fresh deployment the seeded password account is the way in; password-mode is the shipped default.

> [!TIP]
> **You are logged in when you see…**
>
> You reach the workflow-template editor / admin home instead of the login form, and the seeded admin's name appears in the account menu. From here an `Admin` or `SuperAdmin` can create the real human users and edit workflow templates.

## After first login — make it yours

The seeded accounts are a bootstrap, not the steady state. Once you are in:

1. **Change the seeded passwords** (super-admin and customer admin) and enable two-factor — see [Authentication API → MFA](../reference/api/authentication.md#5-two-factor-authentication-mfa).
2. **Create real named users** for the actual people, in your customer organization, and assign roles (`Admin`, `WorkflowEditor`, `User`, etc. — see [Roles in the Users API](../reference/api/users.md)). Do day-to-day work as a named user, not as the seeded admin.
3. **Provision a system user for API/integration access** if you will call the platform programmatically — see [System Users](../reference/api/system-users.md).
4. **Verify a workflow runs end to end** — discover what is available with [Workflow Discovery](../reference/api/discovery.md) and submit a run via [Submit Workflow Execution](../reference/api/submit-workflow-execution.md). For the full integrator path from zero, see [API: zero to a result](../guides/end-to-end/api-zero-to-result.md).

## Done when

- You signed in to the end-user app as a seeded admin and reached the application home, not the login screen.
- You signed in to the admin app at `/Account/Login` and reached the admin home.
- You changed both seeded passwords.

## Troubleshooting: cannot log in

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| Login screen rejects `admin@example.com` | An older build first seeded the database, and it named the customer admin `admin@<CustomerOrgId>.com` | Sign in as `admin@<CustomerOrgId>.com`, with the `PlatformSettings:CustomerOrgId` value of that first start. |
| `superadmin@inia.ai` also fails | Seeding did not run / migrations failed | Check the API logs for the migration and seeding steps: a finished seed logs `User data database initialized`, and a failed one logs `An error occurred seeding the UserData database.` with the exception, and the app keeps running. A failed migration aborts startup; see [Database Migrations](../getting-started/database-migrations.md). |
| The API or the admin app stops at startup naming `Seeding:SuperAdminPassword` or `Seeding:CustomerAdminPassword` | A new database, and no boot password for an account the first start must create | Set the key it names, then start again. |
| The API stops at startup with `Jwt:SecretKey is required.` or `Jwt:SecretKey must be at least 32 bytes.` | No signing key, or one shorter than 32 bytes | Generate one with `openssl rand -base64 48`, see [Application Configuration → JWT](application-configuration.md#jwt). |
| A correct login is rejected as a wrong password, and the browser network tab shows `POST /connect/token` answering 400 with `ClientId` / `ClientSecret` field errors | The API predates the change that lets the SPA sign in without a client | Upgrade the API and the web image together, see [Application Configuration → Vue SPA sign-in](application-configuration.md#vue-spa-sign-in-browser-login). |
| Login fails with a network error, and the browser network tab shows `POST /api/v1/connect/token` answering 502 | The web container's nginx cannot reach the API | Set the web container's `PROXY_BACKEND_URL` to the API's address (default `http://api:80`); the web container's log names the address it tried. A web image built from the deployment repository's old recipe also needs `VITE_API_URL=/`. |
| Login succeeds but every API call returns `403` | The `X-Tenant-Id` sent does not match the user's organization | Send the user's org id (`<CustomerOrgId>`) as `X-Tenant-Id` — see [Authentication API → Multi-Tenant](../reference/api/authentication.md#multi-tenant-token-claims). |
| "Email not confirmed" | A **manually created** user without confirmed email (the seeded accounts are pre-confirmed) | Confirm the email, or create the user with `EmailConfirmed` set. |
| None of the above | Need a value only the team has (the actual URL, the boot passwords) | Ask the operator who deployed the stack. Do not guess. |


## Related pages

- [Release Package Deployment](release-package-deployment.md) — the deploy that precedes this step
- [Application Configuration](application-configuration.md) — `CustomerOrgId`, JWT, AI provider keys
- [Authentication API](../reference/api/authentication.md) — the token endpoint, grants, MFA, and tenant claims
- [System Users](../reference/api/system-users.md) — the seeded OAuth client and provisioning API access
- [Users API](../reference/api/users.md) — creating real users and assigning roles

