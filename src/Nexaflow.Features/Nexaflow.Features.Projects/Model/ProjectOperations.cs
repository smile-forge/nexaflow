using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Projects.Model;

/// <summary>
/// Core local implementation of all project operations. Shared by the ToolManager (local mode) and the
/// MCP server. Per-project methods take a folder name relative to <see cref="RootPath"/>; the UI supplies
/// a <c>rootOverride</c> so the same code drives projects in any bucket (active / shelf / archive).
/// </summary>
public class ProjectOperations : IProjectTools
{
    private readonly ProjectsConfig _config;
    private readonly string? _rootOverride;

    // Reads dynamically so config changes are picked up without recreation.
    private string _rootPath => _rootOverride ?? _config.ProjectDirectory;

    /// <summary>The root directory under which this instance's project folders live.</summary>
    public string RootPath => _rootPath;

    private static readonly JsonSerializerOptions _jsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions _jsonWriteOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly TransactionalFileService _txService;

    /// <param name="config">Shared per-workspace config (project roots + backlog status defs).</param>
    /// <param name="rootOverride">When set, per-project methods resolve under this root instead of
    /// <see cref="ProjectsConfig.ProjectDirectory"/> — used to open shelf/archive projects.</param>
    public ProjectOperations(ProjectsConfig config, string? rootOverride = null, TransactionalFileService? txService = null)
    {
        _config       = config;
        _rootOverride = rootOverride;
        _txService    = txService ?? new TransactionalFileService();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private string ProjectFilePath(string folderName) => Path.Combine(_rootPath, folderName, ".project");

    private ProjectInfo LoadProject(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
            throw new ArgumentException("Project folder name must not be empty.", nameof(folderName));

        var projectDir = Path.Combine(_rootPath, folderName);
        if (!Directory.Exists(projectDir))
            throw new ArgumentException($"Project folder '{folderName}' does not exist.", nameof(folderName));

        return LoadProjectFile(projectDir, folderName);
    }

    /// <summary>
    /// Tolerant, read-only load of a <c>.project</c> at an absolute folder path. Always returns a v2
    /// model — a legacy file is mapped forward in memory (see <see cref="ProjectMigration"/>) WITHOUT
    /// writing, so browsing an old project is safe; only an explicit save/upgrade persists the migration.
    /// </summary>
    public static ProjectInfo LoadProjectFile(string projectDir, string? fallbackName = null)
    {
        var name = fallbackName ?? Path.GetFileName(projectDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var filePath = Path.Combine(projectDir, ".project");
        if (!File.Exists(filePath)) return new ProjectInfo { Name = name };
        return ParseTolerant(File.ReadAllText(filePath), name);
    }

    private static ProjectInfo ParseTolerant(string json, string fallbackName)
    {
        JsonObject? node;
        try { node = JsonNode.Parse(json) as JsonObject; }
        catch { return new ProjectInfo { Name = fallbackName }; }
        if (node is null) return new ProjectInfo { Name = fallbackName };

        if (SchemaVersionOf(node) >= 2)
        {
            try { return JsonSerializer.Deserialize<ProjectInfo>(json, _jsonReadOptions) ?? new ProjectInfo { Name = fallbackName }; }
            catch { return new ProjectInfo { Name = fallbackName }; }
        }
        return ProjectMigration.FromLegacy(node, fallbackName);
    }

    private static int SchemaVersionOf(JsonObject node)
    {
        foreach (var kv in node)
            if (string.Equals(kv.Key, "schemaVersion", StringComparison.OrdinalIgnoreCase))
            {
                try { return kv.Value?.GetValue<int>() ?? 1; }
                catch { return 1; }
            }
        return 1;
    }

    private void SaveProject(string folderName, ProjectInfo project)
        => SaveProjectAt(Path.Combine(_rootPath, folderName), project);

    /// <summary>Writes the project as the current schema version (the only place a migration is persisted).</summary>
    public static void SaveProjectAt(string projectDir, ProjectInfo project)
    {
        project.SchemaVersion = ProjectInfo.CurrentSchemaVersion;
        project.LastUpdate    = DateTime.UtcNow;
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, ".project"), JsonSerializer.Serialize(project, _jsonWriteOptions));
    }

    /// <summary>Persists a legacy project forward as v2 (re-saving the in-memory migration). No-op shape
    /// change for a project already at v2.</summary>
    public void UpgradeSchema(string folderName) => SaveProject(folderName, LoadProject(folderName));

    private string BacklogItemToMarkdown(BacklogItem item)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### [{_config.Resolve(item.StatusKey).Label}] {item.Title}");
        sb.AppendLine($"**ID:** {item.Id}");
        if (!string.IsNullOrWhiteSpace(item.Description))
        {
            sb.AppendLine();
            sb.AppendLine(item.Description);
        }
        return sb.ToString();
    }

    // ── Backlog status ──────────────────────────────────────────────────────

    /// <summary>The next status key in the live progression (skipping the terminal/cancel state), or null
    /// when the item's key is not in the progression (invalid, or already at the last non-terminal).</summary>
    private string? NextStatusKey(string currentKey)
    {
        var ordered = _config.BacklogStatuses.Where(s => !s.IsTerminalCancelled).ToList();
        var idx = ordered.FindIndex(s => s.Key == currentKey);
        if (idx < 0 || idx >= ordered.Count - 1) return null;
        return ordered[idx + 1].Key;
    }

    /// <summary>The default status for a new item — the first non-terminal configured status.</summary>
    private string FirstStatusKey()
        => _config.BacklogStatuses.FirstOrDefault(s => !s.IsTerminalCancelled)?.Key ?? "NotStarted";

    /// <summary>True when the "Progress" button can advance this status one step (it is a non-terminal
    /// status with a following non-terminal status).</summary>
    public bool CanProgress(string statusKey) => NextStatusKey(statusKey) is not null;

    /// <summary>Per-status backlog counts, configured order first then any invalid (deleted-key) groups.</summary>
    public IReadOnlyList<(BacklogStatusDef Def, int Count)> BacklogCountsByStatus(ProjectInfo p)
    {
        var result = new List<(BacklogStatusDef, int)>();
        foreach (var def in _config.BacklogStatuses)
        {
            var c = p.Backlog.Count(i => i.StatusKey == def.Key);
            if (c > 0) result.Add((def, c));
        }
        foreach (var g in p.Backlog
                     .Where(i => _config.BacklogStatuses.All(s => s.Key != i.StatusKey))
                     .GroupBy(i => i.StatusKey))
            result.Add((_config.Resolve(g.Key), g.Count()));
        return result;
    }

    /// <summary>A Mermaid <c>pie</c> block of the per-status backlog counts (empty when no items).</summary>
    public string GetBacklogStatusMarkdown(ProjectInfo p)
    {
        var counts = BacklogCountsByStatus(p);
        if (counts.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        sb.AppendLine("```mermaid");
        sb.AppendLine("pie showData title Backlog by status");
        foreach (var (def, count) in counts)
            sb.AppendLine($"    \"{def.Label.Replace('"', '\'')}\" : {count}");
        sb.AppendLine("```");
        return sb.ToString();
    }

    // ── Project list / details ─────────────────────────────────────────────

    /// <summary>Returns typed (FolderName, DisplayName) entries for every project folder under the root.</summary>
    public List<(string Folder, string Name)> GetProjectListTyped()
    {
        if (!Directory.Exists(_rootPath)) return [];
        return [.. Directory.GetDirectories(_rootPath)
            .Select(dir =>
            {
                var folder = Path.GetFileName(dir)!;
                try
                {
                    var info = LoadProject(folder);
                    return (folder, string.IsNullOrWhiteSpace(info.Name) ? folder : info.Name);
                }
                catch { return (folder, folder); }
            })];
    }

    public List<object> GetProjectList()
        => [.. Directory.GetDirectories(_rootPath)
            .Select(dir =>
            {
                var folder = Path.GetFileName(dir)!;
                var project = LoadProject(folder);
                return (object)new { Id = folder, project.Name };
            })];

    public ProjectInfo GetProjectInfo(string folderName) => LoadProject(folderName);

    public string GetProjectDetails(string folderName)
    {
        var p = LoadProject(folderName);
        var sb = new StringBuilder();
        sb.AppendLine($"# {p.Name}");
        if (!string.IsNullOrWhiteSpace(p.Description))
            sb.AppendLine($"\n## Description\n{p.Description}");
        if (p.CompletionCriteria.Count > 0)
        {
            sb.AppendLine("\n## Completion criteria");
            foreach (var c in p.CompletionCriteria)
                sb.AppendLine($"- [{CompletionStatusLabel(c.Status)}] {c.Text}");
        }
        if (!string.IsNullOrWhiteSpace(p.SolutionFile))
            sb.AppendLine($"\n**Solution File:** {p.SolutionFile}");
        if (p.LastUpdate.HasValue)
            sb.AppendLine($"\n**Last Updated:** {p.LastUpdate:yyyy-MM-dd HH:mm} UTC");

        sb.AppendLine("\n## Backlog");
        var counts = BacklogCountsByStatus(p);
        if (counts.Count == 0) sb.AppendLine("_No backlog items._");
        else foreach (var (def, count) in counts) sb.AppendLine($"- {def.Label}: {count}");
        return sb.ToString();
    }

    private static string CompletionStatusLabel(CompletionStatus s) => s switch
    {
        CompletionStatus.Shouldnt => "shouldn't",
        _ => s.ToString().ToLowerInvariant(),
    };

    // ── Project metadata ───────────────────────────────────────────────────

    public void ModifyProjectHeader(string folderName, string name, string description)
    {
        var p = LoadProject(folderName);
        p.Name        = name;
        p.Description = description;
        SaveProject(folderName, p);
    }

    /// <summary>Replaces the whole completion-criteria list.</summary>
    public void SetCompletionCriteria(string folderName, List<CompletionCriterion> criteria)
    {
        var p = LoadProject(folderName);
        p.CompletionCriteria = criteria ?? [];
        SaveProject(folderName, p);
    }

    // ── Backlog ────────────────────────────────────────────────────────────

    public string GetToDos(string folderName)
    {
        var p = LoadProject(folderName);
        if (p.Backlog.Count == 0) return "_No backlog items._";
        var sb = new StringBuilder();
        sb.AppendLine($"## Backlog – {p.Name}");
        foreach (var item in p.Backlog)
            sb.AppendLine(BacklogItemToMarkdown(item));
        return sb.ToString();
    }

    public Guid AddToDo(string folderName, string title, string? description = null)
    {
        var p = LoadProject(folderName);
        var item = new BacklogItem { Title = title, Description = description ?? string.Empty, StatusKey = FirstStatusKey() };
        p.Backlog.Add(item);
        SaveProject(folderName, p);
        return item.Id;
    }

    /// <summary>Advances the item one step through the configured progression; returns the new label.</summary>
    public string ProgressToDo(string folderName, Guid id)
    {
        var p = LoadProject(folderName);
        var item = p.Backlog.FirstOrDefault(i => i.Id == id)
                   ?? throw new ArgumentException($"Backlog item {id} not found.");
        var next = NextStatusKey(item.StatusKey);
        if (next is not null)
        {
            item.StatusKey = next;
            SaveProject(folderName, p);
        }
        return _config.Resolve(item.StatusKey).Label;
    }

    /// <summary>Sets the item to any configured status by key (the status dropdown).</summary>
    public void SetToDoStatus(string folderName, Guid id, string statusKey)
    {
        var p = LoadProject(folderName);
        var item = p.Backlog.FirstOrDefault(i => i.Id == id)
                   ?? throw new ArgumentException($"Backlog item {id} not found.");
        item.StatusKey = statusKey;
        SaveProject(folderName, p);
    }

    public void ModifyToDo(string folderName, Guid id, string? title = null, string? description = null)
    {
        var p = LoadProject(folderName);
        var item = p.Backlog.FirstOrDefault(i => i.Id == id)
                   ?? throw new ArgumentException($"Backlog item {id} not found.");
        if (title is not null)       item.Title = title;
        if (description is not null) item.Description = description;
        SaveProject(folderName, p);
    }

    /// <summary>Sets the item to the terminal "cancelled" status (a no-op if none is configured).</summary>
    public void CancelToDo(string folderName, Guid id)
    {
        if (_config.TerminalStatus?.Key is not { } terminal) return;
        var p = LoadProject(folderName);
        var item = p.Backlog.FirstOrDefault(i => i.Id == id)
                   ?? throw new ArgumentException($"Backlog item {id} not found.");
        item.StatusKey = terminal;
        SaveProject(folderName, p);
    }

    /// <summary>Removes an item from the backlog entirely.</summary>
    public void RemoveToDo(string folderName, Guid id)
    {
        var p = LoadProject(folderName);
        p.Backlog.RemoveAll(i => i.Id == id);
        SaveProject(folderName, p);
    }

    // ── File system ────────────────────────────────────────────────────────

    public string GetProjectFileStructure(string folderName)
    {
        var root = Path.Combine(_rootPath, folderName);
        if (!Directory.Exists(root)) throw new ArgumentException($"Project folder '{folderName}' not found.");
        var tree = BuildFileTree(root, depth: 0);
        return JsonSerializer.Serialize(tree, _jsonWriteOptions);
    }

    private const int MaxDepth = 4;
    private const int MaxItemsBeforeSummary = 20;

    private static object BuildFileTree(string dir, int depth)
    {
        var name = Path.GetFileName(dir);
        var summaryPath = Path.Combine(dir, ".aisummary");
        string? summary = File.Exists(summaryPath) ? File.ReadAllText(summaryPath) : null;

        object subdirectories = BuildSubdirectories(dir, depth);

        if (summary is not null)
            return new { name, summary, subdirectories };

        var allFiles = Directory.GetFiles(dir)
            .Where(f => Path.GetFileName(f) != ".aisummary")
            .Select(Path.GetFileName)
            .ToList();

        object files;
        if (allFiles.Count > MaxItemsBeforeSummary)
        {
            files = allFiles
                .GroupBy(f => Path.GetExtension(f!).ToLowerInvariant().TrimStart('.') is { Length: > 0 } ext ? $"*.{ext}" : "*")
                .Select(g => new { type = g.Key, count = g.Count() })
                .ToList<object>();
        }
        else
        {
            files = allFiles!;
        }

        return new { name, files, subdirectories };
    }

    private static object BuildSubdirectories(string dir, int depth)
    {
        var dirs = Directory.GetDirectories(dir);
        if (dirs.Length > MaxItemsBeforeSummary)
            return new { subfolderCount = dirs.Length };

        if (depth >= MaxDepth - 1)
            return dirs.Select(d => new { name = Path.GetFileName(d) }).ToList<object>();

        return dirs.Select(d => BuildFileTree(d, depth + 1)).ToList<object>();
    }

    public List<string> GetProjectDirectoryFileList(string folderName, string relativePath)
    {
        var dir = Path.Combine(_rootPath, folderName, relativePath);
        if (!Directory.Exists(dir)) throw new ArgumentException($"Directory '{relativePath}' not found.");
        return Directory.GetFiles(dir).Select(Path.GetFileName).ToList()!;
    }

    public string GetProjectFileContents(string folderName, string relativePath)
    {
        var filePath = ResolveProjectPath(folderName, relativePath);
        if (!File.Exists(filePath)) throw new ArgumentException($"File '{relativePath}' not found.");
        var info = new FileInfo(filePath);
        if (info.Length > 100 * 1024)
            throw new InvalidOperationException($"File is {info.Length / 1024} KB, which exceeds the 100 KB limit. Use GetPartialProjectFileContents instead.");
        return File.ReadAllText(filePath);
    }

    public object GetPartialProjectFileContents(string folderName, string relativePath, int chunkIndex = 0)
    {
        var filePath = ResolveProjectPath(folderName, relativePath);
        if (!File.Exists(filePath)) throw new ArgumentException($"File '{relativePath}' not found.");

        var isBinary = IsBinaryFile(filePath);
        if (isBinary)
        {
            const int blockSize = 1024;
            using var fs = File.OpenRead(filePath);
            var totalChunks = (int)Math.Ceiling((double)fs.Length / blockSize);
            fs.Seek((long)chunkIndex * blockSize, SeekOrigin.Begin);
            var buffer = new byte[Math.Min(blockSize, fs.Length - fs.Position)];
            var read = fs.Read(buffer, 0, buffer.Length);
            return new
            {
                chunkIndex,
                totalChunks,
                hasMore = chunkIndex < totalChunks - 1,
                content = Convert.ToBase64String(buffer, 0, read),
            };
        }
        else
        {
            const int linesPerChunk = 400;
            var lines = File.ReadAllLines(filePath);
            var totalChunks = (int)Math.Ceiling((double)lines.Length / linesPerChunk);
            var chunk = lines.Skip(chunkIndex * linesPerChunk).Take(linesPerChunk).ToArray();
            return new
            {
                chunkIndex,
                totalChunks,
                hasMore = chunkIndex < totalChunks - 1,
                content = string.Join(Environment.NewLine, chunk),
            };
        }
    }

    private static bool IsBinaryFile(string filePath)
    {
        var buffer = new byte[8192];
        using var fs = File.OpenRead(filePath);
        var read = fs.Read(buffer, 0, buffer.Length);
        return buffer.Take(read).Any(b => b == 0);
    }

    // ── Transaction management ──────────────────────────────────────────────

    public string StartTransaction(string folderName)
    {
        var projectRoot = Path.GetFullPath(Path.Combine(_rootPath, folderName));
        if (!Directory.Exists(projectRoot)) throw new ArgumentException($"Project folder '{folderName}' not found.");
        return _txService.StartTransaction(folderName, projectRoot);
    }

    public void CompleteTransaction(string transactionId) => _txService.Complete(transactionId);

    public string AbandonTransaction(string transactionId) => _txService.Abandon(transactionId);

    // ── Anchor-based editing ───────────────────────────────────────────────

    public List<string> FindAndLabel(string transactionId, string folderName, string relativePath, string searchString, string anchorBaseName)
    {
        var absPath = ResolveProjectPath(folderName, relativePath);
        return _txService.FindAndLabel(transactionId, absPath, searchString, anchorBaseName);
    }

    public string AnchorReplace(string transactionId, string anchorName, string newContent) =>
        _txService.AnchorReplace(transactionId, anchorName, newContent);

    public string AnchorDelete(string transactionId, string anchorName) =>
        _txService.AnchorDelete(transactionId, anchorName);

    public string AnchorInsertAfter(string transactionId, string anchorName, string content) =>
        _txService.AnchorInsertAfter(transactionId, anchorName, content);

    // ── File manipulation (transactional) ─────────────────────────────────

    public void DeleteProjectFile(string transactionId, string folderName, string relativePath)
    {
        var filePath = ResolveProjectPath(folderName, relativePath);
        if (!File.Exists(filePath)) throw new ArgumentException($"File '{relativePath}' not found.");
        _txService.BackupAndDelete(transactionId, filePath);
    }

    public void MoveProjectFile(string transactionId, string folderName, string sourceRelativePath, string destinationRelativePath)
    {
        var source = ResolveProjectPath(folderName, sourceRelativePath);
        var destination = ResolveProjectPath(folderName, destinationRelativePath);
        if (!File.Exists(source)) throw new ArgumentException($"Source file '{sourceRelativePath}' not found.");
        if (File.Exists(destination)) throw new InvalidOperationException($"Destination '{destinationRelativePath}' already exists.");
        _txService.BackupAndMove(transactionId, source, destination);
    }

    public string ReplaceProjectFileContents(string transactionId, string folderName, string relativePath, string oldString, string newString)
    {
        var filePath = ResolveProjectPath(folderName, relativePath);
        if (!File.Exists(filePath)) throw new ArgumentException($"File '{relativePath}' not found.");

        var content = File.ReadAllText(filePath);
        var firstIndex = content.IndexOf(oldString, StringComparison.Ordinal);
        if (firstIndex == -1) throw new ArgumentException("The search string was not found in the file.");
        var secondIndex = content.IndexOf(oldString, firstIndex + oldString.Length, StringComparison.Ordinal);
        if (secondIndex != -1) throw new InvalidOperationException("The search string matches more than one location. Provide a more specific string to ensure a unique match.");

        var updated = string.Concat(content.AsSpan(0, firstIndex), newString, content.AsSpan(firstIndex + oldString.Length));
        _txService.BackupAndWrite(transactionId, filePath, updated);

        var lines = updated.Split('\n');
        var charCount = 0;
        var editLine = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            charCount += lines[i].Length + 1;
            if (charCount > firstIndex) { editLine = i; break; }
        }
        var contextStart = Math.Max(0, editLine - 3);
        var contextEnd = Math.Min(lines.Length - 1, editLine + 3);
        var contextLines = lines[contextStart..(contextEnd + 1)];

        var sb = new StringBuilder();
        sb.AppendLine($"**Replaced in** `{relativePath}` **at line ~{editLine + 1}:**");
        sb.AppendLine("```");
        for (var i = 0; i < contextLines.Length; i++)
            sb.AppendLine($"{contextStart + i + 1,4}: {contextLines[i].TrimEnd('\r')}");
        sb.AppendLine("```");
        return sb.ToString();
    }

    public void WriteNewProjectFile(string transactionId, string folderName, string relativePath, string contents)
    {
        var filePath = ResolveProjectPath(folderName, relativePath);
        if (File.Exists(filePath)) throw new InvalidOperationException($"File '{relativePath}' already exists. Use ReplaceProjectFileContents to modify an existing file.");
        _txService.BackupAndWrite(transactionId, filePath, contents);
    }

    /// <summary>Resolves and validates a file path within a project folder, preventing path traversal outside the project root.</summary>
    private string ResolveProjectPath(string folderName, string relativePath)
    {
        var projectRoot = Path.GetFullPath(Path.Combine(_rootPath, folderName));
        var resolved = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        if (!resolved.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !resolved.Equals(projectRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Path '{relativePath}' resolves outside the project folder.");
        return resolved;
    }

    /// <summary>Resolves and validates a directory path within a project folder, preventing path traversal outside the project root.</summary>
    private string ResolveProjectDirectory(string folderName, string relativePath)
    {
        var projectRoot = Path.GetFullPath(Path.Combine(_rootPath, folderName));
        var resolved = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        if (!resolved.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !resolved.Equals(projectRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Path '{relativePath}' resolves outside the project folder.");
        return resolved;
    }

    // ── AI summaries ───────────────────────────────────────────────────────

    public void GenerateSummaryForFolder(string transactionId, string folderName, string relativePath, string description)
    {
        var dir = ResolveProjectDirectory(folderName, relativePath);
        if (!Directory.Exists(dir)) throw new ArgumentException($"Directory '{relativePath}' not found.");
        var summaryPath = Path.Combine(dir, ".aisummary");
        _txService.BackupAndWrite(transactionId, summaryPath, description);
        File.SetAttributes(summaryPath, File.GetAttributes(summaryPath) | FileAttributes.Hidden);
    }

    public void DeleteSummaryForFolder(string transactionId, string folderName, string relativePath)
    {
        var dir = ResolveProjectDirectory(folderName, relativePath);
        var summaryPath = Path.Combine(dir, ".aisummary");
        if (!File.Exists(summaryPath)) return;
        _txService.BackupAndDelete(transactionId, summaryPath);
    }

    // ── Direct summary helpers (no transaction) ────────────────────────────

    public string? ReadDirectorySummary(string directoryPath)
    {
        var path = Path.Combine(directoryPath, ".aisummary");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void WriteDirectorySummary(string directoryPath, string text)
    {
        var path = Path.Combine(directoryPath, ".aisummary");
        if (File.Exists(path))
            File.SetAttributes(path, FileAttributes.Normal);
        File.WriteAllText(path, text);
        try { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden); }
        catch (UnauthorizedAccessException) { /* best-effort; write already succeeded */ }
    }
}
