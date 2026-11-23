using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Features.Wsl;

/// <summary>Which places are a WSL distro's, read from the locations the runtime has mounted — without the network.</summary>
internal static class DistroPlaces
{
    /// <summary>The kind a WSL distro's location has (<see cref="MountedLocation.Kind"/>).</summary>
    public const string Kind = "wsl";

    /// <summary>Whether <paramref name="path"/> is in a mounted distro: its root or anything under it.</summary>
    public static bool Holds(ILocationDirectory locations, string path)
        => !string.IsNullOrEmpty(path) && locations.Mounted.Any(l => l.Kind == Kind && IsUnder(path, l.Root));

    private static bool IsUnder(string path, string root)
        => path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
           && (path.Length == root.Length || path[root.Length] is '\\' or '/');
}
