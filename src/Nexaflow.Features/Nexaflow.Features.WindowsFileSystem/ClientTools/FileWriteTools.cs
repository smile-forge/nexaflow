using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.WindowsFileSystem.ViewModels;

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

        if (File.Exists(full) && !overwrite)
            return ToolResult.Error($"'{name}' already exists. Pass overwrite:true to replace it.");

        try
        {
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(full, content, ct);
        }
        catch (Exception ex) { return ToolResult.Error($"Could not create '{name}': {ex.Message}"); }

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

        if (Directory.Exists(full)) return ToolResult.Error($"'{name}' already exists.");
        if (File.Exists(full))      return ToolResult.Error($"A file named '{name}' already exists.");

        try { await Task.Run(() => Directory.CreateDirectory(full), ct); }
        catch (Exception ex) { return ToolResult.Error($"Could not create folder '{name}': {ex.Message}"); }

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
        var isDir     = Directory.Exists(src);
        if (!isDir && !File.Exists(src)) return ToolResult.Error($"Source not found: {src}");

        // A destination that is an existing folder means "copy into it".
        if (Directory.Exists(dst)) dst = Path.Combine(dst, Path.GetFileName(src));

        try
        {
            await Task.Run(() =>
            {
                if (isDir) FsTool.CopyDirectory(src, dst, overwrite);
                else       File.Copy(src, dst, overwrite);
            }, ct);
        }
        catch (Exception ex) { return ToolResult.Error($"Copy failed: {ex.Message}"); }

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

        var isDir = Directory.Exists(src);
        if (!isDir && !File.Exists(src)) return ToolResult.Error($"Source not found: {src}");
        if (Directory.Exists(dst)) dst = Path.Combine(dst, Path.GetFileName(src));

        try
        {
            await Task.Run(() =>
            {
                if (isDir) Directory.Move(src, dst);
                else       File.Move(src, dst);
            }, ct);
        }
        catch (Exception ex) { return ToolResult.Error($"Move failed: {ex.Message}"); }

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

        var isDir = Directory.Exists(src);
        if (!isDir && !File.Exists(src)) return ToolResult.Error($"Not found: {src}");

        var dst = Path.Combine(Path.GetDirectoryName(src)!, newName);
        if (File.Exists(dst) || Directory.Exists(dst))
            return ToolResult.Error($"'{newName}' already exists.");

        try
        {
            await Task.Run(() =>
            {
                if (isDir) Directory.Move(src, dst);
                else       File.Move(src, dst);
            }, ct);
        }
        catch (Exception ex) { return ToolResult.Error($"Rename failed: {ex.Message}"); }

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

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        if (!FsTool.TryResolve(vm, ToolArgs.Str(arguments, "name", "path", "file"), out var full, out var error))
            return ToolResult.Error(error);
        if (!File.Exists(full) && !Directory.Exists(full))
            return ToolResult.Error($"Not found: {full}");

        bool ok;
        try { ok = await Task.Run(() => NativeMethods.RecycleFiles(new[] { full }), ct); }
        catch (Exception ex) { return ToolResult.Error($"Delete failed: {ex.Message}"); }

        if (!ok) return ToolResult.Error($"Could not recycle '{Path.GetFileName(full)}'.");

        vm.Refresh();
        return ToolResult.Ok($"recycled {Path.GetFileName(full)}",
            $"Sent '{Path.GetFileName(full)}' to the Recycle Bin.");
    }
}
