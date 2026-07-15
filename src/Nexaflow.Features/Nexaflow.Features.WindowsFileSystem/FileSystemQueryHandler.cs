using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem;

/// <summary>
/// Handles path-navigation queries in a FileSystem tab.
/// Accepts: absolute paths (when root is This PC or same drive), paths relative to the
/// current root, and environment-variable paths (%AppData%, etc.).
/// Rejects anything containing wildcards — those belong to the search handlers.
/// Auto-discovered by FeatureManager because it lives in a Nexaflow.Features.* assembly.
/// </summary>
public sealed class FileSystemQueryHandler : IQueryHandler
{
    public string Symbol => ">";

    public string Description =>
        "Navigates the file browser to a directory. " +
        "Use when the user types a full path (C:\\data), a relative path, or an environment variable path (%AppData%).";

    public float CanProcess(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        if (pageVm is not FileSystemViewModel fs) return 0f;

        var trimmed = input.Trim();
        if (trimmed.Length == 0) return 0f;
        if (trimmed.Contains('*') || trimmed.Contains('?')) return 0f;

        // Environment variable path — expand and treat as absolute
        var expanded = Environment.ExpandEnvironmentVariables(trimmed);
        if (expanded != trimmed && Path.IsPathRooted(expanded))
            return IsAcceptableDrive(expanded, fs) ? 0.85f : 0f;

        if (Path.IsPathRooted(trimmed))
            return IsAcceptableDrive(trimmed, fs) ? 0.9f : 0f;

        // Relative path — accept only when it resolves to a directory that actually exists under the
        // current/root folder. Existence is the signal, not a no-spaces heuristic (which both rejected
        // real folders like "Program Files" and falsely claimed any single word).
        return ResolveRelative(trimmed, fs) is not null ? 0.6f : 0f;
    }

    public Task<string?> ProcessAsync(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        if (pageVm is not FileSystemViewModel fs)
            return Task.FromResult<string?>("No file system context available.");

        var trimmed = input.Trim();
        var expanded = Environment.ExpandEnvironmentVariables(trimmed);

        if (Path.IsPathRooted(expanded))
        {
            if (Directory.Exists(expanded))
            {
                fs.NavigateTo(expanded);
                return Task.FromResult<string?>(null);
            }
            return Task.FromResult<string?>($"Path not found: {expanded}");
        }

        if (ResolveRelative(trimmed, fs) is { } dir)
        {
            fs.NavigateTo(dir);
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult<string?>($"Path not found: {trimmed}");
    }

    // Resolves a relative path against the current folder, then the root, returning the first existing
    // directory — or null when not rooted in a folder, or neither candidate exists. Shared by CanProcess
    // (to gate on existence) and ProcessAsync (to navigate).
    private static string? ResolveRelative(string relative, FileSystemViewModel fs)
    {
        if (fs.IsThisPcMode || string.IsNullOrEmpty(fs.RootPath)) return null;

        string?[] candidates =
        [
            string.IsNullOrEmpty(fs.CurrentPath) ? null : SafeFullPath(fs.CurrentPath, relative),
            SafeFullPath(fs.RootPath, relative),
        ];
        foreach (var candidate in candidates)
            if (candidate is not null && Directory.Exists(candidate))
                return candidate;
        return null;
    }

    // Combine + normalise, returning null instead of throwing on invalid input — safe to call from
    // CanProcess on every keystroke.
    private static string? SafeFullPath(string basePath, string relative)
    {
        try { return Path.GetFullPath(Path.Combine(basePath, relative)); }
        catch { return null; }
    }

    // Absolute path is acceptable when:
    //   - in This PC mode (can navigate to any drive)
    //   - root is empty (navigated out of This PC, no fixed root)
    //   - the path's drive matches the current root's drive
    private static bool IsAcceptableDrive(string path, FileSystemViewModel fs)
    {
        if (fs.IsThisPcMode) return true;
        if (string.IsNullOrEmpty(fs.RootPath)) return true;

        var pathDrive = Path.GetPathRoot(path);
        var rootDrive = Path.GetPathRoot(fs.RootPath);
        return string.Equals(pathDrive, rootDrive, StringComparison.OrdinalIgnoreCase);
    }
}
