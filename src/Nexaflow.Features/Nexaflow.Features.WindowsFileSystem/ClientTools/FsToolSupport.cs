using System;
using System.IO;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.IO.Common;
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
    /// Resolves <paramref name="nameOrPath"/> within what the tab shows — its security context, the page's
    /// <see cref="FileSystemViewModel.GetContextGrant"/>. A folder tab holds its folder and everything under it, never
    /// above or beside it; "This PC" holds every drive it shows. A relative name is taken from the folder the tab is in;
    /// every resolved path, absolute or relative, must fall inside the context, and anything that escapes it — an
    /// absolute path elsewhere, a <c>..\</c> climbing out, a device or stream — is rejected.
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

        var grant = vm.GetContextGrant();
        var scope = new PathScope(grant.Files, grant.Folders);
        if (scope.IsEmpty)
        {
            error = "This tab's location isn't backed by the file system, so it can't be used here.";
            return false;
        }

        // A caller may name a mounted path directly; map it before deciding if it's absolute. Under a mount
        // CurrentPath is "::id\…", so relative names are taken from the real folder the bytes are in.
        var mapped = vm.Vfs.TryResolveReal(nameOrPath) ?? nameOrPath;
        string candidate;
        if (Path.IsPathFullyQualified(mapped))
        {
            candidate = mapped;
        }
        else if (!string.IsNullOrEmpty(vm.CurrentPath) && vm.Vfs.TryResolveReal(vm.CurrentPath) is { } here)
        {
            candidate = Path.Combine(here, mapped);
        }
        else
        {
            error = "No folder is open to resolve a relative name against.";
            return false;
        }

        if (PathScope.Full(candidate) is null)
        {
            error = $"'{nameOrPath}' is not a valid path.";
            return false;
        }
        if (scope.Resolve(candidate) is not { } inside)
        {
            error = "That path is outside this tab's folder (its security context).";
            return false;
        }

        full = inside;
        return true;
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
