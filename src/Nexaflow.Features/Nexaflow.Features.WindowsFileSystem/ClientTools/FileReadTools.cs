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
    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        // A subdirectory was requested → enumerate it directly (confined to the current folder).
        var sub = FsTool.Str(arguments, "path", "subdirectory", "dir");
        if (!string.IsNullOrWhiteSpace(sub))
        {
            if (!FsTool.TryResolve(vm, sub, out var dir, out var error))
                return Task.FromResult(ToolResult.Error(error));
            return Task.Run(() =>
            {
                if (!Directory.Exists(dir)) return ToolResult.Error($"Folder not found: {sub}");
                try
                {
                    var di  = new DirectoryInfo(dir);
                    var sub2 = new StringBuilder("Contents of ").Append(dir).Append(":\n");
                    var d = 0; var f = 0;
                    foreach (var e in di.EnumerateDirectories()) { sub2.Append("[dir]  ").Append(e.Name).Append('\n'); d++; }
                    foreach (var e in di.EnumerateFiles())       { sub2.Append("[file] ").Append(e.Name).Append('\n'); f++; }
                    return ToolResult.Ok($"{d} folders, {f} files", sub2.ToString().TrimEnd());
                }
                catch (Exception ex) { return ToolResult.Error($"Could not list '{sub}': {ex.Message}"); }
            }, ct);
        }

        if (vm.IsThisPcMode)
        {
            var drives = string.Join('\n', vm.Entries.Select(e => $"[drive] {e.Name}"));
            return Task.FromResult(ToolResult.Ok($"{vm.Entries.Count} drive(s)", "This PC drives:\n" + drives));
        }
        if (string.IsNullOrEmpty(vm.CurrentPath))
            return Task.FromResult(ToolResult.Ok("no folder open", "No folder is currently open."));

        var sb = new StringBuilder("Contents of ").Append(vm.CurrentPath).Append(":\n");
        foreach (var e in vm.Entries)
            sb.Append(e.IsDirectory ? "[dir]  " : "[file] ").Append(e.Name).Append('\n');

        var folders = vm.Entries.Count(e => e.IsDirectory);
        var files   = vm.Entries.Count - folders;
        return Task.FromResult(ToolResult.Ok($"{folders} folders, {files} files", sb.ToString().TrimEnd()));
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
    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var name = FsTool.Str(arguments, "name", "path", "file");
        if (!FsTool.TryResolve(vm, name, out var full, out var error))
            return Task.FromResult(ToolResult.Error(error));

        return Task.Run(() =>
        {
            if (!File.Exists(full)) return ToolResult.Error($"File not found: {name}");
            if (FsTool.LooksBinary(full)) return ToolResult.Error($"'{name}' looks like a binary file, not text.");
            try
            {
                var count = File.ReadLines(full).Count();
                return ToolResult.Ok($"{count} lines", $"{Path.GetFileName(full)} has {count} line(s).");
            }
            catch (Exception ex) { return ToolResult.Error($"Could not read '{name}': {ex.Message}"); }
        }, ct);
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
    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var name = FsTool.Str(arguments, "name", "path", "file");
        if (!FsTool.TryResolve(vm, name, out var full, out var error))
            return Task.FromResult(ToolResult.Error(error));

        return Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(full))
                {
                    var di = new DirectoryInfo(full);
                    var fileCount = di.EnumerateFiles().Count();
                    var dirCount  = di.EnumerateDirectories().Count();
                    return ToolResult.Ok($"folder {di.Name}",
                        $"{di.Name} — folder\nContains: {fileCount} file(s), {dirCount} subfolder(s)\n" +
                        $"Created:  {di.CreationTime:yyyy-MM-dd HH:mm}\nModified: {di.LastWriteTime:yyyy-MM-dd HH:mm}");
                }
                if (File.Exists(full))
                {
                    var fi = new FileInfo(full);
                    var ext = string.IsNullOrEmpty(fi.Extension) ? "(none)" : fi.Extension;
                    return ToolResult.Ok($"{FsTool.FormatSize(fi.Length)} file",
                        $"{fi.Name} — file ({ext})\nSize:     {FsTool.FormatSize(fi.Length)} ({fi.Length:N0} bytes)\n" +
                        $"Created:  {fi.CreationTime:yyyy-MM-dd HH:mm}\nModified: {fi.LastWriteTime:yyyy-MM-dd HH:mm}\n" +
                        $"Read-only: {fi.IsReadOnly}");
                }
                return ToolResult.Error($"Not found: {name}");
            }
            catch (Exception ex) { return ToolResult.Error($"Could not stat '{name}': {ex.Message}"); }
        }, ct);
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
    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var pattern = FsTool.Str(arguments, "pattern", "name", "query");
        if (string.IsNullOrWhiteSpace(pattern))
            return Task.FromResult(ToolResult.Error("No pattern provided."));
        if (string.IsNullOrEmpty(vm.CurrentPath))
            return Task.FromResult(ToolResult.Error("No folder is open to search."));

        var basePath  = vm.CurrentPath;
        var recursive = FsTool.Bool(arguments, "recursive");

        return Task.Run(() =>
        {
            var glob = pattern.IndexOfAny(['*', '?']) >= 0 ? pattern : $"*{pattern}*";
            var opt  = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            List<string> matches;
            try
            {
                matches = Directory.EnumerateFileSystemEntries(basePath, glob, opt)
                                   .Take(MaxMatches + 1).ToList();
            }
            catch (Exception ex) { return ToolResult.Error($"Search failed: {ex.Message}"); }

            if (matches.Count == 0)
                return ToolResult.Ok("no matches", $"No files or folders match '{pattern}'.");

            var capped = matches.Count > MaxMatches;
            var listed = matches.Take(MaxMatches).Select(m => Path.GetRelativePath(basePath, m));
            var note   = capped ? $"\n…(more than {MaxMatches} matches; showing the first {MaxMatches})" : string.Empty;
            return ToolResult.Ok($"{Math.Min(matches.Count, MaxMatches)} match(es)",
                $"Matches for '{pattern}':\n" + string.Join('\n', listed) + note);
        }, ct);
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
    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var name = FsTool.Str(arguments, "name", "path", "file");
        if (!FsTool.TryResolve(vm, name, out var full, out var error))
            return Task.FromResult(ToolResult.Error(error));

        var start = Math.Max(1, FsTool.Int(arguments, "start_line", 1));
        var end   = FsTool.Int(arguments, "end_line", start + DefaultWindow - 1);
        if (end < start) end = start + DefaultWindow - 1;
        if (end - start + 1 > MaxWindow) end = start + MaxWindow - 1;   // cap a single read

        return Task.Run(() =>
        {
            if (!File.Exists(full)) return ToolResult.Error($"File not found: {name}");
            if (FsTool.LooksBinary(full)) return ToolResult.Error($"'{name}' looks like a binary file, not text.");

            try
            {
                var window = new List<string>(Math.Min(end - start + 1, 256));
                var idx = 0;
                var more = false;
                foreach (var line in File.ReadLines(full))
                {
                    idx++;
                    if (idx < start) continue;
                    if (idx > end) { more = true; break; }
                    window.Add(line);
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
            catch (Exception ex) { return ToolResult.Error($"Could not read '{name}': {ex.Message}"); }
        }, ct);
    }
}
