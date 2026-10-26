using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Features.WindowsFileSystem.ClientTools;

/// <summary>Lists the files and folders the user is currently looking at.</summary>
public sealed class GetFileListTool(FileSystemViewModel vm) : IClientTool
{
    public string Name => "get_file_list";
    public string Description => "List the files and folders in the current directory, or in a subdirectory of it.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("path", "Optional subdirectory (within the current folder) to list instead of the current folder.", Required: false),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        // A subdirectory was requested → list it (confined to the current folder).
        var sub = ToolArgs.Str(arguments, "path", "subdirectory", "dir");
        if (!string.IsNullOrWhiteSpace(sub))
        {
            if (!FsTool.TryResolve(vm, sub, out var dir, out var error))
                return ToolResult.Error(error);
            try
            {
                if (!await vm.Vfs.IsDirectoryAsync(dir, ct)) return ToolResult.Error($"Folder not found: {sub}");
                var (folders, files) = await FsTool.ListAsync(vm, dir, ct);
                var listing = new StringBuilder("Contents of ").Append(dir).Append(":\n");
                foreach (var name in folders) listing.Append("[dir]  ").Append(name).Append('\n');
                foreach (var name in files)   listing.Append("[file] ").Append(name).Append('\n');
                return ToolResult.Ok($"{folders.Count} folders, {files.Count} files", listing.ToString().TrimEnd());
            }
            catch (Exception ex)
            {
                return ToolResult.Error($"Could not list '{sub}': {ex.Message}");
            }
        }

        if (vm.IsThisPcMode)
        {
            var drives = string.Join('\n', vm.Entries.Select(e => $"[drive] {e.Name}"));
            return ToolResult.Ok($"{vm.Entries.Count} drive(s)", "This PC drives:\n" + drives);
        }
        if (string.IsNullOrEmpty(vm.CurrentPath))
            return ToolResult.Ok("no folder open", "No folder is currently open.");

        var sb = new StringBuilder("Contents of ").Append(vm.CurrentPath).Append(":\n");
        foreach (var e in vm.Entries)
            sb.Append(e.IsDirectory ? "[dir]  " : "[file] ").Append(e.Name).Append('\n');

        var shownFolders = vm.Entries.Count(e => e.IsDirectory);
        return ToolResult.Ok($"{shownFolders} folders, {vm.Entries.Count - shownFolders} files", sb.ToString().TrimEnd());
    }
}

/// <summary>Total number of lines in a text file (so the agent can plan ranged reads).</summary>
public sealed class GetLineCountTool(FileSystemViewModel vm) : IClientTool
{
    public string Name => "get_line_count";
    public string Description => "Count the lines in a text file.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("name", "File name (or path within the current folder)."),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var name = ToolArgs.Str(arguments, "name", "path", "file");
        if (!FsTool.TryResolve(vm, name, out var full, out var error))
            return ToolResult.Error(error);

        try
        {
            if (await vm.Vfs.GetEntryAsync(full, ct) is not { IsDirectory: false }) return ToolResult.Error($"File not found: {name}");
            if (await FsTool.LooksBinaryAsync(vm, full, ct)) return ToolResult.Error($"'{name}' looks like a binary file, not text.");

            var count = 0;
            await using var stream = await vm.Vfs.OpenReadAsync(full, ReadIntent.Sequential, ct);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(ct) is not null) count++;
            return ToolResult.Ok($"{count} lines", $"{Path.GetFileName(full)} has {count} line(s).");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Could not read '{name}': {ex.Message}");
        }
    }
}

/// <summary>Metadata for a file or folder: size, timestamps, type.</summary>
public sealed class GetFileStatsTool(FileSystemViewModel vm) : IClientTool
{
    public string Name => "get_file_stats";
    public string Description => "Get a file or folder's size, type, and created / modified timestamps.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("name", "File or folder name (or path within the current folder)."),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var name = ToolArgs.Str(arguments, "name", "path", "file");
        if (!FsTool.TryResolve(vm, name, out var full, out var error))
            return ToolResult.Error(error);

        try
        {
            if (await vm.Vfs.GetEntryAsync(full, ct) is not { } entry) return ToolResult.Error($"Not found: {name}");
            var shown   = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar));
            var created = entry.Created is { } c ? $"{c:yyyy-MM-dd HH:mm}" : "(not kept here)";

            if (entry.IsDirectory)
            {
                var (folders, files) = await FsTool.ListAsync(vm, full, ct);
                return ToolResult.Ok($"folder {shown}",
                    $"{shown} — folder\nContains: {files.Count} file(s), {folders.Count} subfolder(s)\n" +
                    $"Created:  {created}\nModified: {entry.Modified:yyyy-MM-dd HH:mm}");
            }

            var ext = Path.GetExtension(full) is { Length: > 0 } e ? e : "(none)";
            return ToolResult.Ok($"{FsTool.FormatSize(entry.Size)} file",
                $"{shown} — file ({ext})\nSize:     {FsTool.FormatSize(entry.Size)} ({entry.Size:N0} bytes)\n" +
                $"Created:  {created}\nModified: {entry.Modified:yyyy-MM-dd HH:mm}\n" +
                $"Read-only: {entry.IsReadOnly}");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Could not stat '{name}': {ex.Message}");
        }
    }
}

/// <summary>Finds files/folders in the current directory whose name matches a pattern.</summary>
public sealed class FindFilesByNameTool(FileSystemViewModel vm) : IClientTool
{
    private const int MaxMatches = 200;

    public string Name => "find_files_by_name";
    public string Description => "Find files or folders in the current directory by name. " +
                                 "Accepts a glob (e.g. george*, *.txt) or a substring.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("pattern", "Glob (george*, *.txt) or plain substring to match against names."),
        new("recursive", "Search subfolders too.", Required: false, Type: "boolean"),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var pattern = ToolArgs.Str(arguments, "pattern", "name", "query");
        if (string.IsNullOrWhiteSpace(pattern))
            return ToolResult.Error("No pattern provided.");
        if (string.IsNullOrEmpty(vm.CurrentPath))
            return ToolResult.Error("No folder is open to search.");

        var basePath  = vm.CurrentPath;
        var recursive = ToolArgs.Bool(arguments, "recursive");
        var glob      = pattern.IndexOfAny(['*', '?']) >= 0 ? pattern : $"*{pattern}*";

        List<string> matches;
        try
        {
            matches = await FsTool.FindAsync(vm, basePath, glob, recursive, MaxMatches + 1, ct);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Search failed: {ex.Message}");
        }

        if (matches.Count == 0)
            return ToolResult.Ok("no matches", $"No files or folders match '{pattern}'.");

        var capped = matches.Count > MaxMatches;
        var listed = matches.Take(MaxMatches).Select(m => Path.GetRelativePath(basePath, m));
        var note   = capped ? $"\n…(more than {MaxMatches} matches; showing the first {MaxMatches})" : string.Empty;
        return ToolResult.Ok($"{Math.Min(matches.Count, MaxMatches)} match(es)",
            $"Matches for '{pattern}':\n" + string.Join('\n', listed) + note);
    }
}

/// <summary>
/// Reads a text file in fragments: by default the first <see cref="DefaultWindow"/> lines, or a
/// requested line range. Keeps large files from flooding the context — pair with get_line_count.
/// </summary>
public sealed class GetFileContentsTool(FileSystemViewModel vm) : IClientTool
{
    private const int DefaultWindow = 100;
    private const int MaxWindow     = 1000;

    public string Name => "get_file_contents";
    public string Description => "Read a text file in fragments. Defaults to the first 100 lines; pass " +
                                 "start_line / end_line for a specific range (use get_line_count to size it).";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("name", "File name (or path within the current folder) to read."),
        new("start_line", "First line to read (1-based). Default 1.", Required: false, Type: "integer"),
        new("end_line", "Last line to read (inclusive). Default start_line + 99.", Required: false, Type: "integer"),
    ];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var name = ToolArgs.Str(arguments, "name", "path", "file");
        if (!FsTool.TryResolve(vm, name, out var full, out var error))
            return ToolResult.Error(error);

        var start = Math.Max(1, ToolArgs.Int(arguments, "start_line", 1));
        var end   = ToolArgs.Int(arguments, "end_line", start + DefaultWindow - 1);
        if (end < start) end = start + DefaultWindow - 1;
        if (end - start + 1 > MaxWindow) end = start + MaxWindow - 1;   // cap a single read

        try
        {
            if (await vm.Vfs.GetEntryAsync(full, ct) is not { IsDirectory: false }) return ToolResult.Error($"File not found: {name}");
            if (await FsTool.LooksBinaryAsync(vm, full, ct)) return ToolResult.Error($"'{name}' looks like a binary file, not text.");

            var window = new List<string>(Math.Min(end - start + 1, 256));
            var idx = 0;
            var more = false;
            await using (var stream = await vm.Vfs.OpenReadAsync(full, ReadIntent.Sequential, ct))
            {
                using var reader = new StreamReader(stream);
                while (await reader.ReadLineAsync(ct) is { } line)
                {
                    idx++;
                    if (idx < start) continue;
                    if (idx > end) { more = true; break; }
                    window.Add(line);
                }
            }

            if (window.Count == 0)
                return ToolResult.Ok($"no lines {start}-{end}",
                    $"{Path.GetFileName(full)} has fewer than {start} lines (read to line {idx}).") with { Attachments = [full] };

            var shownEnd = start + window.Count - 1;
            var header   = $"{Path.GetFileName(full)} lines {start}-{shownEnd}:";
            var footer   = more
                ? $"\n…(more below — request start_line={shownEnd + 1} to continue)"
                : "\n…(end of file)";
            return ToolResult.Ok($"lines {start}-{shownEnd}",
                header + "\n" + string.Join('\n', window) + footer) with { Attachments = [full] };
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Could not read '{name}': {ex.Message}");
        }
    }
}
