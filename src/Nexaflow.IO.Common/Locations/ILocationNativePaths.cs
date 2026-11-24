namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// A provider whose locations this machine also reaches as Windows folders — a WSL distro as
/// <c>\\wsl.localhost\&lt;distro&gt;</c>. A reader that can only open a file by a path of its own is handed that path with no
/// copy, and a path typed or pasted in that form opens the location. Optional: a provider without it keeps nothing on this
/// disk, and its files reach a path-only reader through the content cache.
/// </summary>
public interface ILocationNativePaths
{
    /// <summary>The Windows folders the connection's root is reachable as: the first is the one handed out, and every one
    /// is read back as the location. Empty when this connection has none.</summary>
    IReadOnlyList<string> GetNativeRoots(ConnectionProfile connection);

    /// <summary>The name a remote name has under a native root — where Windows stands in for a character no path segment
    /// can hold.</summary>
    string ToNativeName(string name);

    /// <summary>The remote name a name under a native root stands for — the inverse of <see cref="ToNativeName"/>.</summary>
    string FromNativeName(string nativeName);
}
