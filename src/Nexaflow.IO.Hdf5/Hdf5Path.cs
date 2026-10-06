namespace Nexaflow.IO.Hdf5;

/// <summary>Absolute HDF5 object paths: <c>/</c> is the root group, segments are separated by <c>/</c>.</summary>
public static class Hdf5Path
{
    public const string Root = "/";

    /// <summary>A leading slash, no trailing or doubled one: <c>a//b/</c> becomes <c>/a/b</c>.</summary>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrEmpty(path)) return Root;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? Root : "/" + string.Join('/', segments);
    }

    public static string Combine(string group, string name)
    {
        var g = Normalize(group);
        return g == Root ? "/" + name : g + "/" + name;
    }

    public static string Parent(string path)
    {
        var p = Normalize(path);
        int slash = p.LastIndexOf('/');
        return slash <= 0 ? Root : p[..slash];
    }

    public static string NameOf(string path)
    {
        var p = Normalize(path);
        return p == Root ? Root : p[(p.LastIndexOf('/') + 1)..];
    }
}
