# Security

This page says what to set before an install serves real users, and how sign-in, roles and
organizations work. To report a vulnerability, follow [SECURITY.md](../SECURITY.md).

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
- Sign in as the first admin, and change the password in the profile.
- Turn on two-factor sign-in for every admin.

## Sign-in

- A user signs in with an email address and a password. A password needs at least 8 characters,
  with an uppercase letter, a lowercase letter and a digit. After 5 failed attempts in a row, the
  account locks for 15 minutes. `Authentication:Password` holds both numbers.
- A user can add a second factor: an authenticator app, with recovery codes, or a one-time code by
  email. A device can be trusted, so that it skips the second factor for 30 days.
- An install can also sign users in with Google (`Authentication:Google`, limited to the domains in
  `AllowedDomains`) or with an OpenID Connect provider (`Authentication:Oidc`).
- The API gives a signed-in user an access token and a refresh token, from
  `POST api/v1/connect/token`. `Jwt:TokenValidityInMinutes` sets how long an access token lasts.

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
