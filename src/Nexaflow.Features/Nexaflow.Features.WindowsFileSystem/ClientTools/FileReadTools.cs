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
    public string Description => "List the files and folders in the current directory.";
    public IReadOnlyList<ClientToolParameter> Parameters => [];
    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
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

/// <summary>Reads the text content of a file in (or under) the current directory.</summary>
public sealed class GetFileContentsTool(FileSystemViewModel vm) : IClientTool
{
    private const int CapBytes = 256 * 1024;

    public string Name => "get_file_contents";
    public string Description => "Read the text contents of a file in the current directory.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("name", "File name (or path within the current folder) to read."),
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
            if (!File.Exists(full))
                return ToolResult.Error($"File not found: {name}");

            try
            {
                var info = new FileInfo(full);
                var take = (int)Math.Min(info.Length, CapBytes);
                var bytes = new byte[take];
                using (var fs = File.OpenRead(full))
                    fs.ReadExactly(bytes, 0, take);

                if (Array.IndexOf(bytes, (byte)0) >= 0)
                    return ToolResult.Error($"'{name}' looks like a binary file, not text.");

                var text  = Encoding.UTF8.GetString(bytes);
                var trunc = info.Length > CapBytes
                    ? $"\n…(truncated — first {CapBytes / 1024} KB of {info.Length} bytes)"
                    : string.Empty;
                return ToolResult.Ok($"read {Path.GetFileName(full)}",
                    $"Contents of {Path.GetFileName(full)}:\n{text}{trunc}") with { Attachments = [full] };
            }
            catch (Exception ex) { return ToolResult.Error($"Could not read '{name}': {ex.Message}"); }
        }, ct);
    }
}
