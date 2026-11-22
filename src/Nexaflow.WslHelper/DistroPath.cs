using Nexaflow.IO.Location.Wsl.Protocol;

namespace Nexaflow.WslHelper;

/// <summary>Paths as the distro writes them: absolute, separated by '/', compared byte for byte.</summary>
internal static class DistroPath
{
    /// <summary>The path a request names, without trailing separators. Anything not absolute is refused.</summary>
    public static string Checked(string path)
    {
        if (path.Length == 0 || path[0] != '/' || path.Contains('\0'))
            throw new HelperFailure(WslFailure.Other, $"'{path}' is not an absolute path");

        int end = path.Length;
        while (end > 1 && path[end - 1] == '/') end--;
        return end == path.Length ? path : path[..end];
    }

    public static string Join(string folder, string name) => folder == "/" ? "/" + name : string.Concat(folder, "/", name);

    /// <summary>The last segment of a path, or "/" for the root.</summary>
    public static string NameOf(string path) => path == "/" ? "/" : path[(path.LastIndexOf('/') + 1)..];

    /// <summary>The folder holding a path; the root holds itself.</summary>
    public static string ParentOf(string path)
    {
        int last = path.LastIndexOf('/');
        return last <= 0 ? "/" : path[..last];
    }
}
