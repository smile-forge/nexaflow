using System.Diagnostics.CodeAnalysis;

namespace Nexaflow.AiHost.Provisioning;

/// <summary>
/// Paths inside a model store, as a catalog and a request name them: relative, with no root, no drive and no <c>..</c>, so none
/// reaches past the store — whatever wrote it.
/// </summary>
public static class StorePath
{
    /// <summary>Whether <paramref name="relative"/> is a plain path inside a store.</summary>
    public static bool IsPlain([NotNullWhen(true)] string? relative)
        => !string.IsNullOrWhiteSpace(relative) && !Path.IsPathRooted(relative) && !relative.Contains(':')
           && !relative.Split('/', '\\').Any(segment => segment is ".." or "." or "");

    /// <summary>Where <paramref name="relative"/> lies under <paramref name="root"/>.</summary>
    /// <exception cref="InvalidDataException">It is not a plain path inside a store.</exception>
    public static string Resolve(string root, string relative)
        => IsPlain(relative) ? Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))
           : throw new InvalidDataException($"'{relative}' is not a plain path inside the model store.");
}
