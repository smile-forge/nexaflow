using System.IO;
using Nexaflow.Features.Common.Dependencies;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Video.Dependencies;

/// <summary>
/// The video player decodes through libvlc. The <c>VideoLAN.LibVLC.Windows</c> package copies the natives
/// beside the app, so on a healthy install this is always present — it is declared precisely because when
/// that payload goes missing (a partial install, an over-zealous cleaner, a blocked file) the failure
/// otherwise surfaces as a raw exception message with no hint that a component is what went wrong.
/// </summary>
public sealed class LibVlcDependency : IExternalDependency
{
    public const string DependencyId = "libvlc";

    /// <summary>Where <c>VideoLAN.LibVLC.Windows</c> puts the natives, and where <c>VlcInit</c> looks.</summary>
    private static string NativeDir => Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64");

    public string Id          => DependencyId;
    public string DisplayName => Str.Get("Video.Dependency.LibVlc.Name");

    public string Description => Str.Get("Video.Dependency.LibVlc.Description");

    public ExternalDependencyKind Kind => ExternalDependencyKind.Required;

    /// <summary>No install link: this ships with the app, so the fix is to repair the installation.</summary>
    public string? InstallUrl => null;

    [LocalDiskOnly("libVLC, shipped beside the app")]
    public ExternalDependencyStatus Probe()
    {
        var dir = NativeDir;
        if (!Directory.Exists(dir))
            return new ExternalDependencyStatus(ExternalDependencyState.Missing, null,
                Str.Format("Video.Dependency.LibVlc.MissingFolderFormat", dir));

        // The folder alone is not proof — the core DLL is what LibVLCSharp actually loads.
        return File.Exists(Path.Combine(dir, "libvlc.dll"))
            ? new ExternalDependencyStatus(ExternalDependencyState.Present, null, dir)
            : new ExternalDependencyStatus(ExternalDependencyState.Missing, null,
                Str.Format("Video.Dependency.LibVlc.MissingDllFormat", dir));
    }
}
