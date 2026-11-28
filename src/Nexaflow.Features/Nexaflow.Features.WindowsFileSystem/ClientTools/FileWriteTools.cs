using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.WindowsFileSystem.Operations;
using Nexaflow.Features.WindowsFileSystem.Model;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Localization;

namespace Nexaflow.Features.WindowsFileSystem.ClientTools;

/// <summary>Creates a new text file in the current directory.</summary>
public sealed class CreateTextFileTool(FileSystemModel model) : IClientTool
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
        if (!FsTool.TryResolve(model, name, out var full, out var error))
            return ToolResult.Error(error);

        var content   = ToolArgs.Str(arguments, "content", "text") ?? string.Empty;
        var overwrite = ToolArgs.Bool(arguments, "overwrite");

        try
        {
            if (!overwrite && await model.Vfs.ExistsAsync(full, ct))
                return ToolResult.Error($"'{name}' already exists. Pass overwrite:true to replace it.");

            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) await model.Vfs.CreateDirectoryAsync(dir, ct);
            await model.Vfs.WriteAllTextAsync(full, content, ct: ct);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Could not create '{name}': {ex.Message}");
        }

        model.Navigation.Refresh();
        return ToolResult.Ok($"created {Path.GetFileName(full)}", $"Created '{FsTool.Display(model, full)}'.")
            with { Attachments = [full] };
    }
}

/// <summary>Creates a new folder in (or under) the current directory.</summary>
public sealed class CreateDirectoryTool(FileSystemModel model) : IClientTool
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
        if (!FsTool.TryResolve(model, name, out var full, out var error))
            return ToolResult.Error(error);

        try
        {
            if (await model.Vfs.GetEntryAsync(full, ct) is { } there)
                return ToolResult.Error(there.IsDirectory ? $"'{name}' already exists." : $"A file named '{name}' already exists.");
            await model.Vfs.CreateDirectoryAsync(full, ct);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Could not create folder '{name}': {ex.Message}");
        }

        model.Navigation.Refresh();
        return ToolResult.Ok($"created folder {Path.GetFileName(full)}", $"Created folder '{FsTool.Display(model, full)}'.");
    }
}

/// <summary>Copies a file or folder, like Explorer's Copy/Paste.</summary>
public sealed class CopyTool(FileSystemModel model) : IClientTool
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
        if (!FsTool.TryResolve(model, ToolArgs.Str(arguments, "source", "from"), out var src, out var e1))
            return ToolResult.Error(e1);
        if (!FsTool.TryResolve(model, ToolArgs.Str(arguments, "destination", "to", "dest"), out var dst, out var e2))
            return ToolResult.Error(e2);

        var overwrite = ToolArgs.Bool(arguments, "overwrite");
        try
        {
            if (await model.Vfs.GetEntryAsync(src, ct) is not { } source) return ToolResult.Error($"Source not found: {src}");

            // A destination that is an existing folder means "copy into it".
            if (await model.Vfs.IsDirectoryAsync(dst, ct)) dst = Path.Combine(dst, Path.GetFileName(src));

            if (source.IsDirectory) await FsTool.CopyDirectoryAsync(model.Vfs, src, dst, overwrite, ct);
            else
            {
                await model.Vfs.CopyFileAsync(src, dst, replace: overwrite, ct);
                await model.Companions.FollowAsync(model.Vfs, src, dst, move: false, ct);
            }
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Copy failed: {ex.Message}");
        }

        model.Navigation.Refresh();
        return ToolResult.Ok($"copied {Path.GetFileName(src)}",
            $"Copied '{Path.GetFileName(src)}' to '{FsTool.Display(model, dst)}'.") with { Attachments = [dst] };
    }
}

/// <summary>Moves a file or folder.</summary>
public sealed class MoveTool(FileSystemModel model) : IClientTool
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
        if (!FsTool.TryResolve(model, ToolArgs.Str(arguments, "source", "from"), out var src, out var e1))
            return ToolResult.Error(e1);
        if (!FsTool.TryResolve(model, ToolArgs.Str(arguments, "destination", "to", "dest"), out var dst, out var e2))
            return ToolResult.Error(e2);

        try
        {
            if (await model.Vfs.GetEntryAsync(src, ct) is not { } source) return ToolResult.Error($"Source not found: {src}");
            if (await model.Vfs.IsDirectoryAsync(dst, ct)) dst = Path.Combine(dst, Path.GetFileName(src));

            await model.Vfs.MoveAsync(src, dst, replace: false, ct);
            if (!source.IsDirectory) await model.Companions.FollowAsync(model.Vfs, src, dst, move: true, ct);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Move failed: {ex.Message}");
        }

        model.Navigation.Refresh();
        return ToolResult.Ok($"moved {Path.GetFileName(src)}",
            $"Moved '{Path.GetFileName(src)}' to '{FsTool.Display(model, dst)}'.") with { Attachments = [dst] };
    }
}

/// <summary>Renames a file or folder in place.</summary>
public sealed class RenameTool(FileSystemModel model) : IClientTool
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
        if (!FsTool.TryResolve(model, ToolArgs.Str(arguments, "name", "source", "from"), out var src, out var error))
            return ToolResult.Error(error);

        var newName = ToolArgs.Str(arguments, "new_name", "newName", "to");
        if (string.IsNullOrWhiteSpace(newName))
            return ToolResult.Error("No new name was provided.");
        var folder = model.Vfs.GetParentPath(src) ?? Path.GetDirectoryName(src)!;
        if (!model.Vfs.CanName(folder, newName))
            return ToolResult.Error($"'{newName}' cannot name a file here.");

        var dst = model.Vfs.ChildPath(folder, newName);
        try
        {
            if (await model.Vfs.GetEntryAsync(src, ct) is not { } source) return ToolResult.Error($"Not found: {src}");
            if (await model.Vfs.ExistsAsync(dst, ct)) return ToolResult.Error($"'{newName}' already exists.");

            await model.Vfs.MoveAsync(src, dst, replace: false, ct);
            if (!source.IsDirectory) await model.Companions.FollowAsync(model.Vfs, src, dst, move: true, ct);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Rename failed: {ex.Message}");
        }

        model.Navigation.Refresh();
        return ToolResult.Ok($"renamed to {newName}", $"Renamed '{model.Vfs.GetDisplayName(src)}' to '{newName}'.")
            with { Attachments = [dst] };
    }
}

/// <summary>Sends a file or folder to the Recycle Bin (soft delete only).</summary>
public sealed class DeleteTool(FileSystemModel model) : IClientTool
{
    public string Name => "delete_file";
    public string Description => "Delete a file or folder. On this PC it goes to the Recycle Bin. On a location (a WSL distro, a " +
                                 "server), which has no Recycle Bin, it is deleted for good — the user is asked in those words first.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("name", "File or folder to delete."),
    ];
    public ToolSafety Safety => ToolSafety.RequiresApproval;
    public bool Parallelizable => true;

    /// <summary>The words the approval shows: "delete file a.txt", and "delete file a.txt for good" on a location, which has no
    /// Recycle Bin. Asked before anything runs, so it is answered at once.</summary>
    public ToolCallAnnouncement? Announce(JsonObject arguments)
    {
        string name = Path.GetFileName(ToolArgs.Str(arguments, "name", "path", "file") ?? "");
        if (name.Length == 0) return null;
        bool resolved  = FsTool.TryResolve(model, name, out var full, out _);
        bool forGood   = resolved && model.Vfs.IsOnLocation(full);
        bool folder    = resolved && IsFolder(full);
        string target  = (folder, forGood) switch
        {
            (true, true)   => "WindowsFileSystem.Announce.FolderForGoodFormat",
            (true, false)  => "WindowsFileSystem.Announce.FolderFormat",
            (false, true)  => "WindowsFileSystem.Announce.FileForGoodFormat",
            (false, false) => "WindowsFileSystem.Announce.FileFormat",
        };
        return folder
            ? new(Str.Get("WindowsFileSystem.Announce.Delete"), Str.Format(target, name),
                  Str.Format("WindowsFileSystem.Announce.DeletingFolderFormat", name),
                  Str.Get("WindowsFileSystem.Announce.FolderDeleted"), Str.Get("WindowsFileSystem.Announce.FolderUnchanged"))
            : new(Str.Get("WindowsFileSystem.Announce.Delete"), Str.Format(target, name),
                  Str.Format("WindowsFileSystem.Announce.DeletingFileFormat", name),
                  Str.Get("WindowsFileSystem.Announce.FileDeleted"), Str.Get("WindowsFileSystem.Announce.FileUnchanged"));
    }

    /// <summary>Whether <paramref name="full"/> is a folder, said at once: as the tab lists it, or as this disk has it. A
    /// location is asked nothing on the caller's thread, so an entry the tab does not list there reads as a file.</summary>
    [LocalDiskOnly("a path off a location, which the tab's security context resolved to this disk")]
    private bool IsFolder(string full)
    {
        var comparison = model.Vfs.GetNameComparison(full);
        if (model.Listing.Entries.FirstOrDefault(entry => string.Equals(entry.FullPath, full, comparison)) is { } listed) return listed.IsDirectory;
        return !model.Vfs.IsOnLocation(full) && Directory.Exists(full);
    }

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        if (!FsTool.TryResolve(model, ToolArgs.Str(arguments, "name", "path", "file"), out var full, out var error))
            return ToolResult.Error(error);

        if (model.Vfs.IsOnLocation(full)) return await DeleteForGoodAsync(full, ct);

        bool ok;
        try
        {
            if (!await model.Vfs.ExistsAsync(full, ct)) return ToolResult.Error($"Not found: {full}");
            // The Recycle Bin is this machine's, and so is everything left that the tab's security context resolves to.
            ok = await Task.Run(() => NativeMethods.RecycleFiles(model.Companions.WithCompanions([full])), ct);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Delete failed: {ex.Message}");
        }

        if (!ok) return ToolResult.Error($"Could not recycle '{Path.GetFileName(full)}'.");

        model.Navigation.Refresh();
        return ToolResult.Ok($"recycled {Path.GetFileName(full)}",
            $"Sent '{Path.GetFileName(full)}' to the Recycle Bin.");
    }

    /// <summary>A delete on a location, which has no Recycle Bin — approved as for good (<see cref="Announce"/>) — made as the
    /// person's own Delete makes it there: through the location, its companions with it, a row in the operations panel.</summary>
    private async Task<ToolResult> DeleteForGoodAsync(string full, CancellationToken ct)
    {
        string name = model.Vfs.GetDisplayName(full);
        try
        {
            if (!await model.Vfs.ExistsAsync(full, ct)) return ToolResult.Error($"Not found: {full}");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Delete failed: {ex.Message}");
        }

        if (model.Operations.EnqueueDelete([full], permanent: true) is not { } operation)
            return ToolResult.Error($"Could not delete '{name}'.");
        await operation.Completion.WaitAsync(ct);

        model.Navigation.Refresh();
        return operation.State == FileOperationState.Completed
            ? ToolResult.Ok($"deleted {name} for good", $"Deleted '{name}' for good: a location has no Recycle Bin.")
            : ToolResult.Error($"Could not delete '{name}': {string.Join(" ", operation.Problems.DefaultIfEmpty(operation.Detail))}");
    }
}
