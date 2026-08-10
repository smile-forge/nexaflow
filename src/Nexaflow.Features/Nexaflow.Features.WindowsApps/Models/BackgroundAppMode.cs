namespace Nexaflow.Features.WindowsApps.Models;

/// <summary>
/// What a Store app is allowed to do when it isn't in the foreground — the tri-state behind Windows'
/// "Let this app run in background". Persisted per package family; see
/// <see cref="Services.RegistryBackgroundAppAccess"/> for where it is stored.
/// </summary>
public enum BackgroundAppMode
{
    /// <summary>Windows decides (the default): background work is allowed but yields to battery saver.</summary>
    PowerOptimized,

    /// <summary>Background work keeps running even under battery saver.</summary>
    Always,

    /// <summary>The user has denied background execution outright.</summary>
    Never
}
