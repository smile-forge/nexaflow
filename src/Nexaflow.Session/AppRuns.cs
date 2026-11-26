namespace Nexaflow.Session;

/// <summary>
/// This run of the app against the one before: whether it is the first ever, or the first since the app was updated — until
/// <see cref="Stamp"/> records it as run, which the setup that greets a first run or an update does once it is done.
/// </summary>
/// <param name="shell">The shell's settings, which keep the version last run.</param>
/// <param name="config">The session's configuration: whether anything was read at all, and where the version last run is saved.</param>
/// <param name="version">The app's version, as this run is.</param>
public sealed class AppRuns(ShellConfig shell, ConfigManager config, string version)
{
    /// <summary>The app's version, as this run is.</summary>
    public string Version => version;

    /// <summary>No settings were found: the app has never run here.</summary>
    public bool IsFirstRun => config.IsFirstRun;

    /// <summary>The app last ran as another version, and this run has not been recorded yet.</summary>
    public bool IsUpdate => shell.LastRunVersion is { } last && last != version;

    /// <summary>Records this version as the one last run; nothing is written when it already is. A record that cannot be saved
    /// greets the next run again.</summary>
    public void Stamp()
    {
        if (shell.LastRunVersion == version) return;
        shell.LastRunVersion = version;
        try { config.Save(shell, shell.ConfigName); }
        catch (Exception) { /* unsaved, the next run is greeted again */ }
    }
}
