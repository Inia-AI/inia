namespace IniaAi.Application.Common;

/// <summary>
/// The settings that hold the apps' public addresses. Mail builds its links from them, never from a
/// request, whose Host header the sender chooses.
/// </summary>
public static class AppAddresses
{
    /// <summary>The customer app's public address.</summary>
    public const string FRONTEND_BASE_URL_KEY = "FrontendBaseUrl";

    /// <summary>The admin app's public address.</summary>
    public const string ADMIN_BASE_URL_KEY = "AdminBaseUrl";
}
