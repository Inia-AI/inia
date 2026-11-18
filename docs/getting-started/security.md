---
type: reference
status: current
description: "Lists what to set before an install of the platform serves real users, and says how sign-in, roles, the separation of organizations and documents that carry instructions work. Read when you prepare an install for production, need the rules for passwords, second factors, roles or organizations, or check that a run does not obey a document."
---

# Security

**On this page:** Before production · Documents that carry instructions · Sign-in · Roles · Organizations

This page says what to set before an install serves real users, and how sign-in, roles and
organizations work. To report a vulnerability, follow [SECURITY.md](../../SECURITY.md).

## Before production

The source ships no password, key or secret. Set each of these for every install. The one-command
stack makes the signing key, the client secret and the database password on its first start, and
takes the first admin's password from `INIA_ADMIN_PASSWORD`. An install that you run another way
reads them from its settings.

| Setting | What it is | Without it |
|---|---|---|
| `Seeding:SuperAdminEmail` | The first admin's address. The one-command stack sets it from `INIA_ADMIN_EMAIL`, `superadmin@example.com` by default. | The first admin is `superadmin@inia.ai`. |
| `Seeding:SuperAdminPassword` | The password of the first admin. The app sets it when it creates the database, and it follows the password rule under [Sign-in](#sign-in). | The first start stops and names the setting. |
| `Jwt:SecretKey` | The key that signs the sign-in tokens. It needs at least 32 bytes: generate one with `openssl rand -base64 48`. Keep it secret: anyone who has it can sign a token. | The API does not start, and names the setting. |
| `OpenIddict:ClientSecret` | The secret of the service clients, one for each organization, named `service_user_<organization>`. Another system uses a service client to call the API with no user. | The app creates no service client, and logs a warning. Users still sign in. |
| `Cors:AllowedOrigins` | The origins whose web pages may call the API from a browser, such as the address of the Vue app when it is on another host than the API. | No browser page on another origin can call the API. A `*` entry stops startup. |

Also, before other machines reach the install:

- Put a TLS proxy in front of it. The one-command stack serves plain HTTP on 127.0.0.1 only.
- Once every path to the admin app is HTTPS, set `AdminApp:HttpsOnlyCookies` to `true`, so its
  sign-in cookies are `Secure` and bound to its host ("Response headers and cookies of the admin
  app" in [application configuration](../operations/application-configuration.md)).
- Sign in as the first admin, and change the password in the profile.
- Turn on two-factor sign-in for every admin.

## Documents that carry instructions

A document that a user uploads can carry text addressed to the model, such as an instruction to
report a false figure. The platform hands a document to the model in three ways: page reading, when
the model reads a scanned page; an AI Query block; and an Agent block. How the model treats such text
depends on the prompts of the workflow. Their authors own and test those prompts, and the platform
adds nothing to them.

`platform/security-samples/hidden-instruction` checks an install. Its document hides an instruction
on each page: white text on a page with a text layer, and visible text on a scanned page. Its
workflow states two figures, once from an AI Query block and once from an Agent block. The check
fails a step that gives a false figure, leaves out a true one, or prints its own instructions. A step
that only mentions the hidden text passes. Load `workflow.json` through the workflow template API
(`PUT /api/v1/workflow-templates/SecurityHiddenInstruction/content`), as the stack loads its samples,
then run the checker on the folder:

```bash
INIA_ADMIN_PASSWORD='<the password you set>' python3 platform/scripts/clean-machine/check.py --samples platform/security-samples
```

## Sign-in

- A user signs in with an email address and a password. A password needs at least 8 characters,
  and must not be one of the most commonly used passwords. After 5 failed attempts in a row, the
  account locks for 15 minutes. `Authentication:Password` holds both numbers.
- A user can add a second factor: an authenticator app, with recovery codes, or a one-time code by
  email. A device can be trusted, so that it skips the second factor for 30 days.
- An install can also sign users in with Google (`Authentication:Google`, limited to the domains in
  `AllowedDomains`) or with an OpenID Connect provider (`Authentication:Oidc`). With
  `Authentication:Google:ProvenAddressesOnly` on, Google sign-in accepts only a Gmail address or a
  Google Workspace account.
- The service account's `client_credentials` sign-in gives a super admin token to whoever holds its
  secret. Turn it off with `Authentication:ServiceAccount:Enabled` where no integration uses it.
- The API gives a signed-in user an access token and a refresh token, from
  `POST api/v1/connect/token`. `Jwt:TokenValidityInMinutes` sets how long an access token lasts.
  A user ends every own session, on every device, with `POST api/v1/profiles/sign-out-everywhere`:
  every API token at once, and the admin app's sign-in at its next security stamp check.

## Roles

A user holds one or more roles. Each surface of the apps admits the roles it names.

| Role | What it allows |
|---|---|
| User | Start runs and read reports. |
| WorkflowEditor | Create and edit workflows. |
| Admin | Manage the organization: its users and its settings. |
| SignatureContributor | Open the Signatures page, propose a value for a signature field, and vote on proposals. |
| Operator | Run the install day to day: the server log, the downloads of executed workflows, model pricing, token usage and data clean-up. These surfaces read across organizations, so only a super admin grants this role. |
| SuperAdmin | Everything, in every organization. |

## Organizations

One install serves many organizations from one database. The API keeps them apart in three steps:

1. Every request that needs a signed-in user names its organization in the `X-Tenant-Id` header.
   The API refuses a request with no valid organization before it reads the token.
2. After it reads the token, the API checks that the token lists the organization among the user's
   memberships. A super admin may act in any organization. Otherwise the API answers 403.
3. The entities that an organization owns carry its id (`IOrganizationScoped`). The application
   layer reads them through specifications that filter by the organization of the request.
