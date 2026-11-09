namespace Nexaflow.AiHost.Client;

/// <summary>
/// Where the AI host is installed: <c>AiHost\</c> beside whatever runs it — the app, or a test suite — each copying it
/// there as a helper (<c>HelperFolder="AiHost"</c> on its project reference).
/// </summary>
public static class AiHostInstall
{
    /// <summary>The host's executable, as the launcher starts it.</summary>
    public static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe");
}
