using System;
using System.Collections.Generic;
using System.IO;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Features.WindowsFileSystem.ClientTools;

/// <summary>
/// Shared helpers for the file-system client tools: tolerant argument reading and current-folder
/// path resolution with traversal guarding.
/// </summary>
internal static class FsTool
{
    /// <summary>Cheap heuristic: a NUL byte in the first 8 KB means the file is binary, not text.</summary>
    public static async Task<bool> LooksBinaryAsync(FileSystemViewModel vm, string path, CancellationToken ct)
    {
        try
        {
            await using var stream = await vm.Vfs.OpenReadAsync(path, ReadIntent.Sequential, ct);
            var buf  = new byte[8192];
            int read = 0, n;
            while (read < buf.Length && (n = await stream.ReadAsync(buf.AsMemory(read), ct)) > 0) read += n;
            return Array.IndexOf(buf, (byte)0, 0, read) >= 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>The folders and files directly inside <paramref name="folder"/>, by name — its companions left out, as the
    /// browser leaves them out: they go wherever their file goes.</summary>
    public static async Task<(IReadOnlyList<string> Folders, IReadOnlyList<string> Files)> ListAsync(
        FileSystemViewModel vm, string folder, CancellationToken ct)
    {
        var folders    = new List<string>();
        var files      = new List<FileSystemEntry>();
        var companions = vm.Companions.Listing();
        await foreach (var entry in vm.Vfs.EnumerateAsync(folder, ct))
        {
            if (entry.IsDirectory) { folders.Add(entry.Name); continue; }
            var row = new FileSystemEntry { Name = entry.Name, FullPath = Path.Combine(folder, entry.Name) };
            if (!companions.Hold(row)) files.Add(row);
        }
        files.AddRange(companions.Finish());
        return (folders, [.. files.Select(f => f.Name)]);
    }

    /// <summary>
    /// Paths under <paramref name="basePath"/> whose name matches <paramref name="glob"/> (Windows wildcards, any case) —
    /// in that folder, or in every folder below it when <paramref name="recursive"/> — companions left out, stopping once
    /// <paramref name="max"/> are found. A subfolder that cannot be read is passed over; the folder searched must be readable.
    /// </summary>
    public static async Task<List<string>> FindAsync(FileSystemViewModel vm, string basePath, string glob, bool recursive,
                                                     int max, CancellationToken ct)
    {
        var found   = new List<string>();
        var pending = new Queue<string>([basePath]);
        while (pending.Count > 0 && found.Count < max)
        {
            var folder     = pending.Dequeue();
            var companions = vm.Companions.Listing();
            var rows       = new List<FileSystemEntry>();
            try
            {
                await foreach (var entry in vm.Vfs.EnumerateAsync(folder, ct))
                {
                    var row = new FileSystemEntry { Name = entry.Name, FullPath = Path.Combine(folder, entry.Name), IsDirectory = entry.IsDirectory };
                    if (entry.IsDirectory && recursive) pending.Enqueue(row.FullPath);
                    if (!companions.Hold(row)) rows.Add(row);
                }
            }
            catch (Exception ex) when (folder != basePath && ex is IOException or UnauthorizedAccessException) { continue; }
            rows.AddRange(companions.Finish());
            found.AddRange(rows.Where(r => System.IO.Enumeration.FileSystemName.MatchesWin32Expression(glob, r.Name, ignoreCase: true)).Select(r => r.FullPath));
        }
        return found.Count > max ? found.GetRange(0, max) : found;
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

    /// <summary>Copies a directory tree, through the file system, wherever its two ends are.</summary>
    public static async Task CopyDirectoryAsync(IVirtualFileSystem vfs, string sourceDir, string destDir, bool overwrite,
                                                CancellationToken ct)
    {
        await vfs.CreateDirectoryAsync(destDir, ct);
        var children = new List<LocationEntry>();
        await foreach (var entry in vfs.EnumerateAsync(sourceDir, ct)) children.Add(entry);

        foreach (var child in children)
        {
            var from = Path.Combine(sourceDir, child.Name);
            var to   = Path.Combine(destDir, child.Name);
            if (child.IsDirectory) await CopyDirectoryAsync(vfs, from, to, overwrite, ct);
            else await vfs.CopyFileAsync(from, to, replace: overwrite, ct);
        }
    }
}
