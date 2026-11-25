using System.IO;

namespace Nexaflow.Core.Services;

/// <summary>
/// Where this process keeps what it writes: its configuration (<see cref="BaseDir"/>) and what stays on this machine alone
/// (<see cref="LocalDir"/>). The session's configuration is kept in them (<see cref="ConfigManager"/>); the crash log and the
/// safety log, which can be written before the session opens, find them the same way.
/// </summary>
/// <param name="BaseDir">Where every config is kept, and the crash log.</param>
/// <param name="LocalDir">Where what is kept on this machine alone goes — caches too large to roam with the settings, and the
/// safety log.</param>
public sealed record AppDataFolders(string BaseDir, string LocalDir)
{
    /// <summary>The variable that sets a run's folders apart — a UI test's fresh root, a portable install's.</summary>
    public const string IsolatedRootVariable = "NEXAFLOW_CONFIG_DIR";

    /// <summary>
    /// This process's folders: under the root <see cref="IsolatedRootVariable"/> names, where it names one, with what stays on this
    /// machine kept beside the base as <c>local</c> so the root holds everything the run wrote; otherwise
    /// <c>%AppData%\Smile\nexaflow</c> and <c>%LocalAppData%\Smile\nexaflow</c>.
    /// </summary>
    public static AppDataFolders ForThisProcess()
    {
        var isolated = Environment.GetEnvironmentVariable(IsolatedRootVariable);
        return string.IsNullOrWhiteSpace(isolated)
            ? new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Smile", "nexaflow"),
                  Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Smile", "nexaflow"))
            : new(isolated, Path.Combine(isolated, "local"));
    }
}
