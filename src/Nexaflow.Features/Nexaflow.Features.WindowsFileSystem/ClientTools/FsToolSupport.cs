using System;
using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Features.WindowsFileSystem.ViewModels;

namespace Nexaflow.Features.WindowsFileSystem.ClientTools;

/// <summary>
/// Shared helpers for the file-system client tools: tolerant argument reading and current-folder
/// path resolution with traversal guarding.
/// </summary>
internal static class FsTool
{
    /// <summary>First present argument among <paramref name="keys"/>, read as a string.</summary>
    public static string? Str(JsonObject args, params string[] keys)
    {
        foreach (var k in keys)
            if (args.TryGetPropertyValue(k, out var n) && n is not null)
                return n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n.ToString();
        return null;
    }

    /// <summary>Reads a boolean argument; false when absent or non-boolean.</summary>
    public static bool Bool(JsonObject args, string key)
        => args.TryGetPropertyValue(key, out var n) && n is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    /// <summary>
    /// Resolves <paramref name="nameOrPath"/> against the page's current folder. Absolute paths are
    /// honoured; relative names must stay inside the current folder (no <c>..\</c> escapes).
    /// </summary>
    public static bool TryResolve(FileSystemViewModel vm, string? nameOrPath, out string full, out string error)
    {
        full  = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(nameOrPath))
        {
            error = "No name or path was provided.";
            return false;
        }

        try
        {
            if (Path.IsPathFullyQualified(nameOrPath))
            {
                full = Path.GetFullPath(nameOrPath);
                return true;
            }

            var basePath = vm.CurrentPath;
            if (string.IsNullOrEmpty(basePath))
            {
                error = "No folder is open to resolve a relative name against.";
                return false;
            }

            var root      = Path.GetFullPath(basePath);
            var candidate = Path.GetFullPath(Path.Combine(root, nameOrPath));
            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                error = "That path is outside the current folder.";
                return false;
            }

            full = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            error = $"'{nameOrPath}' is not a valid path.";
            return false;
        }
    }

    /// <summary>Path display relative to the current folder, falling back to the file name.</summary>
    public static string Display(FileSystemViewModel vm, string full)
    {
        if (!string.IsNullOrEmpty(vm.CurrentPath))
        {
            try { return Path.GetRelativePath(vm.CurrentPath, full); }
            catch { /* different volume — fall through */ }
        }
        return Path.GetFileName(full);
    }

    /// <summary>Recursively copies a directory tree.</summary>
    public static void CopyDirectory(string sourceDir, string destDir, bool overwrite)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite);
        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)), overwrite);
    }
}
