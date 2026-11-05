namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// A sign-in method a module adds to the list the sign-in screen reads (<c>GET /auth/providers</c>).
/// A module registers one as a singleton; the platform's own methods are listed by their settings.
/// </summary>
/// <param name="SendsMail">Listed only while the deployment's mail is on, because the method mails the person.</param>
public sealed record AdditionalSignInProvider(string Name, string DisplayName, bool SendsMail = false);
