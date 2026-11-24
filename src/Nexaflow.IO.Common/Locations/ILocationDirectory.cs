namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// The locations a runtime has mounted, as a page lists them — This PC's rows — and the ones it could not mount, with why.
/// Read without touching the network; a page reaches a location's files through the file system, never past it. A
/// feature is given the active runtime's.
/// </summary>
public interface ILocationDirectory
{
    /// <summary>Every mounted location.</summary>
    IReadOnlyList<MountedLocation> Mounted { get; }

    /// <summary>The connections mounted nowhere, and why: no backend reaches their kind, or their id is already taken.</summary>
    IReadOnlyList<UnreachableLocation> Unreachable { get; }

    /// <summary>Raised when either list has changed. May fire on any thread.</summary>
    event Action? Changed;
}

/// <summary>A mounted location as a page shows it.</summary>
/// <param name="Kind">The backend's kind — <c>"wsl"</c>, <c>"sftp"</c> — which a page groups and badges by.</param>
/// <param name="Root">Its root path, <c>::{Id}</c>, which opens it.</param>
/// <param name="DisplayPath">Its root as people read it — <c>wsl://Ubuntu/</c>.</param>
public sealed record MountedLocation(string Id, string Label, string Kind, string Root, LocationCapabilities Capabilities,
                                     string DisplayPath);
