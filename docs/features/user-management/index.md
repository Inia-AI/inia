---
type: explanation
status: current
description: "Explains how users are created, imported, disabled and given organizations, the two password policies, password resets, and what the platform records about accounts. Read when you administer users, need to know what disabling or removing a user does, or debug a refused password."
---

# User Management

**On this page:** The user record · Organization memberships · Creating users · Password policies · Password resets · Disabling and enabling users · Workflow type visibility · Sign-in and account security · What the platform records about accounts · Related Documentation

The IniaAi Platform keeps its users in ASP.NET Core Identity, with organization memberships for multi-tenancy. The admin app (the Blazor `IniaAi.WebApp`) is where users are created and changed; the REST API only reads them.

## The user record

A user is a `User` (`IniaAi.Domain.Entities.UserManagement.User`), an ASP.NET Core `IdentityUser` with these additions:

- `OrganizationId`: the user's **home organization**, set when the user is created.
- `FirstName`, `MiddleName`, `LastName`; `GetFullName()` joins them.
- `IsSystem`: marks the per-organization service account an integration signs in as ([Provisioning a System User](../../reference/api/system-users.md)). A system user has no password and no role, and no authorization check skips it; it may read the evidence of the runs it started itself.
- `LockoutEnabled` is on for every user the platform builds, so failed sign-ins count toward the lockout.

The `AspNetUsers` and `UserOrganizations` columns are on [UserData Database](../../reference/database/user-data-database.md).

## Organization memberships

A user belongs to organizations through `UserOrganizations` rows (`UserOrganization`): `UserId`, `OrganizationId`, `IsDefault`, `IsActive`, `JoinedAt`, `LeftAt`. The home organization and the memberships are separate, and each one drives something different:

| What | Reads |
|---|---|
| The organizations a token may act in (`tenant_ids`) | Active memberships in open organizations ([Authentication](../../reference/api/authentication.md#multi-tenant-token-claims)) |
| The token's default organization (`tenant_id`) | The membership marked `IsDefault`, else the first one; none when no active membership exists, so the token names no organization |
| `GET /api/v1/users/me/tenants` | Active memberships in open organizations, read live |
| `GET /api/v1/users` | Users whose **home** organization is the current one ([Users API](../../reference/api/users.md#list-users)) |

Roles are **not** stored on the membership. A user's roles are global ASP.NET Identity roles (`AspNetUserRoles`) and apply in every organization the user belongs to; [Authorization](authorization.md) lists them and who may grant each.

Memberships are changed in the admin app's user list (`UserOrganizationManagementDialog`):

- **Add** offers the open organizations the admin may act in and the user does not belong to yet.
- **Remove** deletes the membership row. The last membership cannot be removed (`User must be assigned to at least one organization`), and removing the default one makes another membership the default.
- **Set as default** moves the `IsDefault` mark.

The dialog never changes the home organization. Saving with a membership removed revokes every API token of the user, so the next request answers `401` and the user signs in again without that organization; an added membership reaches the token at the next refresh. See [Revocation and Sign-Out](../../reference/api/authentication.md#revocation-and-sign-out).

## Creating users

There is no REST endpoint that creates, edits or deletes a user: the `UsersController` (`/api/v1/users`) only lists users, lists the caller's organizations and resolves user names ([Users API](../../reference/api/users.md)). Users are created in the admin app, by `Admin` and `SuperAdmin`:

- **Create User** (`CreateUser.razor`) builds one confirmed user (`EmailConfirmed = true`) whose user name is its email, with the `User` role, in the organization the admin picks: that organization becomes the user's home and default membership. Its password must pass the admin-set rules below.
- **Import Users** (`UserCsvImportDialog`, mapped by `UserCsvMapper`) provisions many users from a CSV file:
  - Recognized columns: `Email` (required), `Password`, `FirstName`, `LastName`, `MiddleName`, `UserName`, `PhoneNumber`. The dialog offers a downloadable CSV template.
  - **Any unrecognized column rejects the whole file** (`CsvImportService`).
  - Imported users get normalized Identity fields, `EmailConfirmed`, the `User` role, and a default `UserOrganization` membership in the importing organization; their home organization is that organization too.
  - If a `Password` column is present, every row must supply a password that passes the admin-set rules (it is hashed, so the user can sign in at once); omit the column entirely for SSO-only users.
  - Insert-only: rows whose email or user name already exists — or repeats within the file — are rejected with per-row validation errors.
  - A `UserName` may hold only letters, digits and `- . _ @ +`, the characters ASP.NET Core Identity accepts; a row with any other character is refused (`User name '…' may hold only letters, digits and - . _ @ +`). Without the column the user name is the email.

## Password policies

One password rule applies wherever a password is set: a user's own change and self-service reset, the seeded first accounts, the admin app's Create User and Reset Password, and the CSV import. A password needs at least 8 characters, an uppercase letter (A to Z), a lowercase letter (a to z) and a digit (0 to 9); no symbol is required. `PasswordPolicy` holds the rule. It configures ASP.NET Core Identity in both apps, and the paths that set a password without Identity (Create User, Reset Password, the CSV import and the seeder) check it through `PasswordPolicy.Problems`, which names each rule the password breaks: `Password must be at least 8 characters`, `Password must contain an uppercase letter (A to Z)`, `Password must contain a lowercase letter (a to z)`, `Password must contain a digit (0 to 9)`, or `Password is required!` for an empty one. The analyst app checks the same rule before it sends a new password (`PASSWORD_MIN_LENGTH` in `src/lib/passwordPolicy.ts`).

## Password resets

**By an admin.** An admin resets the password of a user they manage from the user list (`ResetUserPasswordDialog`, `UserAccountAdminService.ResetPasswordAsync`), which checks the password rule, replaces the hash, rotates the security stamp and revokes the user's API tokens; no mail is involved. An organization admin does not manage a user who holds `SuperAdmin`, `WorkflowEditor` or `Operator`, or who also belongs to another organization ([who manages whom](authorization.md#how-roles-are-created-and-assigned)).

**By the user.** Self-service reset runs only when the deployment can mail: it needs a mail provider and `FrontendBaseUrl`, the analyst app's address (`SendPasswordResetEmailCommandHandler.CanSend`).

1. `POST /api/v1/passwords/forgot` with `{ "email": "..." }` mails a link to the analyst app's `/reset-password` page. The mail and the link go to the address stored on the account the typed address found, never to the typed address: the lookup also matches Unicode lookalikes of the stored address (CVE-2026-70175). It always answers `200`, whether or not the address belongs to an account, the account is a system user, or the mail failed.
2. The page sends the link's address, the token and the new password to `POST /api/v1/passwords/reset`. A wrong, spent or expired link and an unknown address all answer `400` with `PasswordReset.InvalidLink` (`This reset link is not valid any more. Ask for a new one.`), so the page reveals no accounts; a password that breaks `PasswordPolicy` answers `400` with ASP.NET Core Identity's own error codes.

**A new password ends every API session.** A successful reset from the mailed link (`ResetPasswordCommandHandler`, and the admin app's `Account/ResetPassword` page) and a user's own password change (`POST /api/v1/profiles/change-password`, and the admin app's `Account/Manage/ChangePassword` page) revoke every stored API token of the user, as an admin reset does ([Authentication](../../reference/api/authentication.md)). The change endpoint revokes the token that called it too, so the analyst app's profile page signs the user out after a change and opens the sign-in page with `Password updated. Sign in again with your new password.` The admin app keeps its own sign-in cookie after a change on its Account page.

Both endpoints are anonymous and share the sign-in rate limit ([Authentication → 429](../../reference/api/authentication.md#429-too-many-requests)). The admin app's own reset mail also needs `AdminBaseUrl`, its public address, because it never builds a mailed link from the request's host. Its forgot-password and resend-confirmation pages also mail only the stored address, and answer a system account as they answer an unknown address, with no mail. Without mail, the analyst app hides **Forgot Password?** (`LoginForm`) and the admin app's forgot-password page says `Password reset by mail is off on this deployment. Ask an administrator to reset your password.`; an admin resets the password instead.

## Disabling and enabling users

Admins can **disable and re-enable users** from the same list (`UserManagementCard`, `UserAccountAdminService`). Disable is an administrative lockout — `LockoutEnabled = true` with `LockoutEnd = DateTimeOffset.MaxValue` (`User.Disable`) plus a security-stamp rotation; the user's data, roles and organization memberships are kept, so enable is a one-click restore (`LockoutEnd = null`). An admin cannot disable their own account, and disabled users are hidden from the list behind a **Show disabled** toggle.

What a disable stops:

- Every token grant refuses a locked-out user: the password and refresh grants through `ValidateUserCanAuthenticateAsync`, the Google and OIDC grants through `ExternalLoginUserResolver`. The MFA step refuses one before checking its code.
- The admin app's own sign-in refuses the user, and its open sessions end at their next security-stamp check, at most 30 minutes later (`IdentityRevalidatingAuthenticationStateProvider`).
- Every API token already issued to the user is revoked: the next API request with it answers `401`, and its refresh token is refused ([Revocation and Sign-Out](../../reference/api/authentication.md#revocation-and-sign-out)). Removing a role and a user deleting their own account revoke the same way.

## Workflow type visibility

Admins can also set a user's **[workflow type visibility](workflow-type-visibility.md)** from the same list (`UserWorkflowTypeVisibilityDialog`, opened by the filter button on the user row). The dialog's own two states say it exactly: *"This user sees every workflow type their organizations have access to."* or *"This user sees only the workflow types listed below."* This is a decluttering control, not a permission — it narrows the user's pickers and report lists and never changes what they can access or run. Like the other admin dialogs, it writes repository-direct from the Blazor server; there is no v1 API endpoint for it.

## Sign-in and account security

User management feeds the [Authentication](../../reference/api/authentication.md) system:

- **Email confirmation**: the API's password grant refuses an account whose email is not confirmed, and the admin app requires a confirmed account (`RequireConfirmedAccount = true`). Users created by the admin app or the CSV import are confirmed.
- **Lockout**: failed passwords and second-factor codes count toward a lockout of `Authentication:Password:LockoutDurationMinutes` after `Authentication:Password:MaxFailedAccessAttempts` failures ([Brute-force lockout](mfa.md#brute-force-lockout)).
- **Two-factor authentication**: TOTP, email codes, recovery codes and trusted devices ([MFA](mfa.md)).
- **Outside sign-in**: Google and [OIDC SSO](sso-oidc.md) match an existing account and never create one.

## What the platform records about accounts

The platform records authentication events and nothing else about accounts. `LoggingAuthenticationAuditLog` writes one log line per sign-in success, refusal, lockout, second-factor prompt, and two-factor change (setup, enable, disable, authenticator reset, recovery-code regeneration, trusted-device removal), naming the user id, the method and the client address, never an email, a password or a code. There is no audit table: creating a user, changing a role or a membership, resetting a password and disabling a user leave no record beyond ordinary application log lines.

## Related Documentation

- [Authentication](../../reference/api/authentication.md) - User authentication and JWT tokens
- [OIDC SSO](sso-oidc.md) - Enterprise SSO with Okta, Auth0, Azure AD, Keycloak
- [Authorization](authorization.md) - Roles and the endpoints each one opens
- [Multi-Tenant Architecture](../../architecture/multi-tenant/index.md) - Organization-based tenancy
- [Users API Reference](../../reference/api/users.md) - API endpoint documentation
