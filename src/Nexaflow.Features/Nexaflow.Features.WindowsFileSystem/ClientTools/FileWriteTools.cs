using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.WindowsFileSystem.ClientTools;

/// <summary>Creates a new text file in the current directory.</summary>
public sealed class CreateTextFileTool(FileSystemViewModel vm) : IClientTool
{
    public string Name => "create_text_file";
    public string Description => "Create a new text file in the current directory.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("name", "File name to create (e.g. notes.txt)."),
        new("content", "Text to write into the file.", Required: false),
        new("overwrite", "Replace the file if it already exists.", Required: false, Type: "boolean"),
    ];
    public ToolSafety Safety => ToolSafety.RequiresApproval;
    public bool Parallelizable => true;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var name = ToolArgs.Str(arguments, "name", "path", "filename");
        if (!FsTool.TryResolve(vm, name, out var full, out var error))
            return ToolResult.Error(error);

        var content   = ToolArgs.Str(arguments, "content", "text") ?? string.Empty;
        var overwrite = ToolArgs.Bool(arguments, "overwrite");

        try
        {
            if (!overwrite && await vm.Vfs.ExistsAsync(full, ct))
                return ToolResult.Error($"'{name}' already exists. Pass overwrite:true to replace it.");

            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) await vm.Vfs.CreateDirectoryAsync(dir, ct);
            await vm.Vfs.WriteAllTextAsync(full, content, ct: ct);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Could not create '{name}': {ex.Message}");
        }

        vm.Refresh();
        return ToolResult.Ok($"created {Path.GetFileName(full)}", $"Created '{FsTool.Display(vm, full)}'.")
            with { Attachments = [full] };
    }
}

/// <summary>Creates a new folder in (or under) the current directory.</summary>
public sealed class CreateDirectoryTool(FileSystemViewModel vm) : IClientTool
{
    public string Name => "create_directory";
    public string Description => "Create a new folder in the current directory (parent folders are created as needed).";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("name", "Folder name (or relative path) to create within the current folder."),
    ];
    public ToolSafety Safety => ToolSafety.RequiresApproval;
    public bool Parallelizable => true;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var name = ToolArgs.Str(arguments, "name", "path", "folder");
        if (!FsTool.TryResolve(vm, name, out var full, out var error))
            return ToolResult.Error(error);

        try
        {
            if (await vm.Vfs.GetEntryAsync(full, ct) is { } there)
                return ToolResult.Error(there.IsDirectory ? $"'{name}' already exists." : $"A file named '{name}' already exists.");
            await vm.Vfs.CreateDirectoryAsync(full, ct);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Could not create folder '{name}': {ex.Message}");
        }

        vm.Refresh();
        return ToolResult.Ok($"created folder {Path.GetFileName(full)}", $"Created folder '{FsTool.Display(vm, full)}'.");
    }
}

/// <summary>Copies a file or folder, like Explorer's Copy/Paste.</summary>
public sealed class CopyTool(FileSystemViewModel vm) : IClientTool
{
    public string Name => "copy_file";
    public string Description => "Copy a file or folder to a destination.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("source", "File or folder to copy (name within, or path under, the current folder)."),
        new("destination", "Destination folder, or the new path/name for the copy."),
        new("overwrite", "Overwrite existing files at the destination.", Required: false, Type: "boolean"),
    ];
    public ToolSafety Safety => ToolSafety.RequiresApproval;
    public bool Parallelizable => true;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        if (!FsTool.TryResolve(vm, ToolArgs.Str(arguments, "source", "from"), out var src, out var e1))
            return ToolResult.Error(e1);
        if (!FsTool.TryResolve(vm, ToolArgs.Str(arguments, "destination", "to", "dest"), out var dst, out var e2))
            return ToolResult.Error(e2);

        var overwrite = ToolArgs.Bool(arguments, "overwrite");
        try
        {
            if (await vm.Vfs.GetEntryAsync(src, ct) is not { } source) return ToolResult.Error($"Source not found: {src}");

            // A destination that is an existing folder means "copy into it".
            if (await vm.Vfs.IsDirectoryAsync(dst, ct)) dst = Path.Combine(dst, Path.GetFileName(src));

            if (source.IsDirectory) await FsTool.CopyDirectoryAsync(vm.Vfs, src, dst, overwrite, ct);
            else
            {
                await vm.Vfs.CopyFileAsync(src, dst, replace: overwrite, ct);
                await vm.Companions.FollowAsync(vm.Vfs, src, dst, move: false, ct);
            }
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Copy failed: {ex.Message}");
        }

        vm.Refresh();
        return ToolResult.Ok($"copied {Path.GetFileName(src)}",
            $"Copied '{Path.GetFileName(src)}' to '{FsTool.Display(vm, dst)}'.") with { Attachments = [dst] };
    }
}

/// <summary>Moves a file or folder.</summary>
public sealed class MoveTool(FileSystemViewModel vm) : IClientTool
{
    public string Name => "move_file";
    public string Description => "Move a file or folder to a destination.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("source", "File or folder to move."),
        new("destination", "Destination folder, or the new path for the item."),
    ];
    public ToolSafety Safety => ToolSafety.RequiresApproval;
    public bool Parallelizable => true;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        if (!FsTool.TryResolve(vm, ToolArgs.Str(arguments, "source", "from"), out var src, out var e1))
            return ToolResult.Error(e1);
        if (!FsTool.TryResolve(vm, ToolArgs.Str(arguments, "destination", "to", "dest"), out var dst, out var e2))
            return ToolResult.Error(e2);

        try
        {
            if (await vm.Vfs.GetEntryAsync(src, ct) is not { } source) return ToolResult.Error($"Source not found: {src}");
            if (await vm.Vfs.IsDirectoryAsync(dst, ct)) dst = Path.Combine(dst, Path.GetFileName(src));

            await vm.Vfs.MoveAsync(src, dst, replace: false, ct);
            if (!source.IsDirectory) await vm.Companions.FollowAsync(vm.Vfs, src, dst, move: true, ct);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Move failed: {ex.Message}");
        }

        vm.Refresh();
        return ToolResult.Ok($"moved {Path.GetFileName(src)}",
            $"Moved '{Path.GetFileName(src)}' to '{FsTool.Display(vm, dst)}'.") with { Attachments = [dst] };
    }
}

/// <summary>Renames a file or folder in place.</summary>
public sealed class RenameTool(FileSystemViewModel vm) : IClientTool
{
    public string Name => "rename_file";
    public string Description => "Rename a file or folder in the current directory.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("name", "Existing file or folder name."),
        new("new_name", "New name (no path)."),
    ];
    public ToolSafety Safety => ToolSafety.RequiresApproval;
    public bool Parallelizable => true;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        if (!FsTool.TryResolve(vm, ToolArgs.Str(arguments, "name", "source", "from"), out var src, out var error))
            return ToolResult.Error(error);

        var newName = ToolArgs.Str(arguments, "new_name", "newName", "to");
        if (string.IsNullOrWhiteSpace(newName))
            return ToolResult.Error("No new name was provided.");
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return ToolResult.Error($"'{newName}' is not a valid file name.");

        var dst = Path.Combine(Path.GetDirectoryName(src)!, newName);
        try
        {
            if (await vm.Vfs.GetEntryAsync(src, ct) is not { } source) return ToolResult.Error($"Not found: {src}");
            if (await vm.Vfs.ExistsAsync(dst, ct)) return ToolResult.Error($"'{newName}' already exists.");

            await vm.Vfs.MoveAsync(src, dst, replace: false, ct);
            if (!source.IsDirectory) await vm.Companions.FollowAsync(vm.Vfs, src, dst, move: true, ct);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Rename failed: {ex.Message}");
        }

        vm.Refresh();
        return ToolResult.Ok($"renamed to {newName}", $"Renamed '{Path.GetFileName(src)}' to '{newName}'.")
            with { Attachments = [dst] };
    }
}

/// <summary>Sends a file or folder to the Recycle Bin (soft delete only).</summary>
public sealed class DeleteTool(FileSystemViewModel vm) : IClientTool
{
    public string Name => "delete_file";
    public string Description => "Send a file or folder to the Recycle Bin.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("name", "File or folder to delete (it goes to the Recycle Bin, not permanently)."),
    ];
    public ToolSafety Safety => ToolSafety.RequiresApproval;
    public bool Parallelizable => true;

    /// <summary>The words the approval shows. Asked before anything runs, so it is answered at once: the tab's security
    /// context resolves every name to this disk, where telling a folder from a file is a local question.</summary>
    [LocalDiskOnly("a path the tab's security context resolved, which is always on this disk")]
    public ToolCallAnnouncement? Announce(JsonObject arguments)
    {
        string name = Path.GetFileName(ToolArgs.Str(arguments, "name", "path", "file") ?? "");
        if (name.Length == 0) return null;
        bool folder = FsTool.TryResolve(vm, name, out var full, out _) && Directory.Exists(full);
        return folder
            ? new(Str.Get("WindowsFileSystem.Announce.Delete"), Str.Format("WindowsFileSystem.Announce.FolderFormat", name),
                  Str.Format("WindowsFileSystem.Announce.DeletingFolderFormat", name),
                  Str.Get("WindowsFileSystem.Announce.FolderDeleted"), Str.Get("WindowsFileSystem.Announce.FolderUnchanged"))
            : new(Str.Get("WindowsFileSystem.Announce.Delete"), Str.Format("WindowsFileSystem.Announce.FileFormat", name),
                  Str.Format("WindowsFileSystem.Announce.DeletingFileFormat", name),
                  Str.Get("WindowsFileSystem.Announce.FileDeleted"), Str.Get("WindowsFileSystem.Announce.FileUnchanged"));
    }

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        if (!FsTool.TryResolve(vm, ToolArgs.Str(arguments, "name", "path", "file"), out var full, out var error))
            return ToolResult.Error(error);

        bool ok;
        try
        {
            if (!await vm.Vfs.ExistsAsync(full, ct)) return ToolResult.Error($"Not found: {full}");
            // The Recycle Bin is this machine's, and so is everything the tab's security context resolves to.
            ok = await Task.Run(() => NativeMethods.RecycleFiles(vm.Companions.WithCompanions([full])), ct);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Delete failed: {ex.Message}");
        }

        if (!ok) return ToolResult.Error($"Could not recycle '{Path.GetFileName(full)}'.");

        vm.Refresh();
        return ToolResult.Ok($"recycled {Path.GetFileName(full)}",
            $"Sent '{Path.GetFileName(full)}' to the Recycle Bin.");
    }
}
