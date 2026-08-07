using System;
using System.IO;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Features.WindowsFileSystem.ClientTools;

/// <summary>
/// Shared helpers for the file-system client tools: tolerant argument reading and current-folder
/// path resolution with traversal guarding.
/// </summary>
internal static class FsTool
{
    /// <summary>Cheap heuristic: a NUL byte in the first 8 KB means the file is binary, not text.</summary>
    public static bool LooksBinary(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var buf  = new byte[(int)Math.Min(8192, fs.Length)];
            var read = fs.Read(buf, 0, buf.Length);
            for (var i = 0; i < read; i++) if (buf[i] == 0) return true;
            return false;
        }
        catch { return false; }
    }

    /// <summary>Formats a byte count as a human-readable size (e.g. 1.4 MB).</summary>
    public static string FormatSize(long bytes) => SizeFormatter.FormatBytes(bytes);

    /// <summary>
    /// Resolves <paramref name="nameOrPath"/> within the page's current folder — the tab's security
    /// context. EVERY resolved path, absolute or relative, must stay inside that folder; anything that
    /// escapes it (including absolute paths to elsewhere on disk and <c>..\</c> traversals) is rejected.
    /// The one exception is an unconfined tab with no current folder (e.g. "This PC"): there is no
    /// boundary, so absolute paths are honoured anywhere and relative names have nothing to resolve
    /// against. Such tabs are surfaced to the user as High security risk.
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
            // Resolve the tab's location to where the bytes actually are before using it as the
            // confinement root. Under a mount CurrentPath is "::id\…", which Path.GetFullPath cannot
            // parse — it would throw into the catch below and turn every file tool into "not a valid
            // path". Confining on the real path is equivalent (a mount is a bijection over its subtree)
            // and it is the form every File/Directory call downstream needs anyway.
            var basePath = vm.CurrentPath;
            var confined = !string.IsNullOrEmpty(basePath);
            string? root = null;
            if (confined)
            {
                if (vm.Vfs.TryResolveReal(basePath) is not { } realBase)
                {
                    error = "This tab's location isn't backed by the file system, so it can't be used here.";
                    return false;
                }
                root = Path.GetFullPath(realBase);
            }

            // A caller may name a mounted path directly; map it before deciding if it's absolute.
            var mapped = vm.Vfs.TryResolveReal(nameOrPath) ?? nameOrPath;

            string candidate;
            if (Path.IsPathFullyQualified(mapped))
            {
                candidate = Path.GetFullPath(mapped);
            }
            else
            {
                if (!confined)
                {
                    error = "No folder is open to resolve a relative name against.";
                    return false;
                }
                candidate = Path.GetFullPath(Path.Combine(root!, nameOrPath));
            }

            if (confined && !IsWithin(candidate, root!))
            {
                error = "That path is outside this tab's folder (its security context).";
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

    /// <summary>
    /// True when <paramref name="candidate"/> equals <paramref name="root"/> or sits beneath it,
    /// compared on path-segment boundaries so <c>C:\foo</c> does not match <c>C:\foobar</c>.
    /// </summary>
    private static bool IsWithin(string candidate, string root)
    {
        char[] seps = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
        var c = candidate.TrimEnd(seps);
        var r = root.TrimEnd(seps);
        return string.Equals(c, r, StringComparison.OrdinalIgnoreCase)
            || c.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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
