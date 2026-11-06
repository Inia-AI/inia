---
type: explanation
status: current
description: "SMTP, SendGrid and Amazon SES IEmailSender providers behind IOutgoingMail, which picks one at startup and turns mail on only when that provider is fully configured; its senders are the password reset, the second-factor code, the admin app's account mail and any a closed module adds; no workflow-related email exists. Read when mail does not arrive, when you configure a mail provider, or when someone asks whether the platform emails about workflows."
---

# Email Services

Email sending is abstracted behind `IEmailSender` (`platform/IniaAi.Application/Common/Interfaces/Services/IEmailSender.cs`) with three interchangeable providers. Features never pick a provider themselves: they ask `IOutgoingMail` whether mail is on, and send through it.

## Providers

`AddOutgoingMail` in `InfrastructureServiceRegistration.cs` binds every provider's settings, and `OutgoingMail.ConfiguredSender` builds the one sender `EmailSettings:Provider` names, once, at startup:

- **SMTP** — `SmtpEmailSender` (`platform/IniaAi.Infrastructure/Email/SmtpEmailSender.cs`), bound to `SmtpSettings` from the `Smtp` configuration section.
- **SendGrid** — `SendGridEmailSender` (`platform/IniaAi.Infrastructure/Email/SendGridEmailSender.cs`), bound to `SendGridSettings` from the `SendGrid` section.
- **Amazon SES** — `SesEmailSender` (`platform/IniaAi.Infrastructure/Email/SesEmailSender.cs`), bound to `SesSettings` from the `Ses` section. It calls the SES API with the host's own AWS credentials and region, so it has no key or SMTP user: the host's role needs `ses:SendEmail` on the sending identity. SES takes an address only with an ASCII domain, so the sender writes an international domain in Punycode, and a sender name that is not plain ASCII text is quoted or encoded (RFC 2047). SES cannot deliver to a local part that is not ASCII. An SES account still in the sandbox delivers only to verified addresses.

No sender logs the recipient's address.

## Outgoing mail

`OutgoingMail` (`IOutgoingMail`, a singleton) reads `EmailSettings:Provider` once and turns mail on only when that provider has what it needs to send:

| `EmailSettings:Provider` | Mail is on when |
|---|---|
| `Smtp` | `Smtp:Host` and `Smtp:FromEmail` are set |
| `SendGrid` | `SendGrid:ApiKey` and `SendGrid:FromEmail` are set |
| `Ses` | `Ses:FromEmail` is set |
| empty or anything else | never |

`IsConfigured` says whether mail is on. `SendAsync` sends through the configured provider and refuses, naming `EmailSettings:Provider`, when mail is off. A feature that mails checks `IsConfigured` first and stays off, so no screen promises a mail that cannot go.

## Consumers

These paths send email, and each one is off until mail is on:

- **Password reset** — `PasswordController` sends `SendPasswordResetEmailCommand`, whose handler mails a link to `{FrontendBaseUrl}/reset-password` at the address stored on the account, never at the address the request typed. With no mail or no `FrontendBaseUrl` it sends nothing. A failed send is logged as an error and the request still answers 200, so the status code never shows whether the account exists. See [User Management](../../../features/user-management/index.md) for the whole flow.
- **Second-factor code** — `SendEmailOtpCommandHandler` mails the code Identity's email token provider generates. With mail off it refuses with `EmailOtp.Unavailable`, and the `email` method is not offered. See [MFA](../../../features/user-management/mfa.md#email-one-time-code).
- **Admin app account mail** — the WebApp registers `IdentityMailSender` as Identity's `IEmailSender<User>`, so the admin app's confirmation and reset mail goes through `IOutgoingMail`. `AccountLinks` builds every mailed link from `AdminBaseUrl`, never from the request's host. The forgot-password and resend-confirmation pages mail the address stored on the account, never the typed one, and send nothing for a system account. With mail off or no `AdminBaseUrl` it sends nothing and logs an error, and the admin app's forgot-password page says that reset by mail is off.


**There is no workflow-related email.** The platform sends no workflow-completion notifications, no error-alert emails, and no report-delivery emails — nothing in the workflow engine or `JobsService` references `IEmailSender` or `IOutgoingMail` (verified by enumerating every consumer in the platform repo; the paths above are all of them).



## Configuration status

Every provider ships unconfigured: `EmailSettings:Provider`, `SendGrid:ApiKey` and `Smtp:Host` are empty in `appsettings.json`, which has no `Ses` section. `SendGridEmailSender` does not check the SendGrid response status, so a rejected send is still logged as sent. The keys to supply are listed in [Application configuration](../../../operations/application-configuration.md#email).

