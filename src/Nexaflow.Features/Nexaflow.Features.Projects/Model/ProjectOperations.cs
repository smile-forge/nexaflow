namespace Nexaflow.Features.Projects.Model
{
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using global::Nexaflow.Features.Projects;

    /// <summary>
    /// Core local implementation of all project operations.
    /// Shared by both the ToolManager (local mode) and the MCP server.
    /// </summary>
    public class ProjectOperations : IProjectTools
    {
        private readonly ProjectsConfig _config;

        // Reads dynamically so config changes are picked up without recreation
        private string _rootPath => _config.ProjectDirectory;

        /// <summary>The root directory under which all project folders live.</summary>
        public string RootPath => _config.ProjectDirectory;

        /// <summary>
        /// Returns typed (FolderName, DisplayName) entries for every project folder.
        /// The display name falls back to the folder name when the project has no name set.
        /// </summary>
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

        private static readonly JsonSerializerOptions _jsonReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        private static readonly JsonSerializerOptions _jsonWriteOptions = new()
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        private readonly TransactionalFileService _txService;

        public ProjectOperations(ProjectsConfig config, TransactionalFileService txService)
        {
            _config   = config;
            _txService = txService;
        }

        public ProjectOperations(ProjectsConfig config)
        {
            _config    = config;
            _txService = new TransactionalFileService();
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private string ProjectFilePath(string folderName) =>
            Path.Combine(_rootPath, folderName, ".project");

        private ProjectInfo LoadProject(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
                throw new ArgumentException("Project folder name must not be empty.", nameof(folderName));

            var projectDir = Path.Combine(_rootPath, folderName);
            if (!Directory.Exists(projectDir))
                throw new ArgumentException($"Project folder '{folderName}' does not exist.", nameof(folderName));

            var filePath = ProjectFilePath(folderName);
            if (!File.Exists(filePath)) return new ProjectInfo { Name = folderName };
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<ProjectInfo>(json, _jsonReadOptions) ?? new ProjectInfo { Name = folderName };
        }

        private void SaveProject(string folderName, ProjectInfo project)
        {
            project.LastUpdate = DateTime.UtcNow;
            var filePath = ProjectFilePath(folderName);
            File.WriteAllText(filePath, JsonSerializer.Serialize(project, _jsonWriteOptions));
        }

        private static string BacklogItemToMarkdown(BacklogItem item)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"### [{item.Status}] {item.Title}");
            sb.AppendLine($"**ID:** {item.Id}");
            if (!string.IsNullOrWhiteSpace(item.Notes))
                sb.AppendLine($"**Notes:** {item.Notes}");
            if (!string.IsNullOrWhiteSpace(item.ImpPlan))
                sb.AppendLine($"**Implementation Plan:** {item.ImpPlan}");
            if (!string.IsNullOrWhiteSpace(item.TestPlan))
                sb.AppendLine($"**Test Plan:** {item.TestPlan}");
            return sb.ToString();
        }

        private static BacklogStatus NextStatus(BacklogStatus current) => current switch
        {
            BacklogStatus.NotStarted => BacklogStatus.AwaitingDesign,
            BacklogStatus.AwaitingDesign => BacklogStatus.AwaitingDesignReview,
            BacklogStatus.AwaitingDesignReview => BacklogStatus.AwaitingImplementation,
            BacklogStatus.AwaitingImplementation => BacklogStatus.AwaitingImplementationReview,
            BacklogStatus.AwaitingImplementationReview => BacklogStatus.AwaitingTestImplementation,
            BacklogStatus.AwaitingTestImplementation => BacklogStatus.AwaitingTestReview,
            BacklogStatus.AwaitingTestReview => BacklogStatus.AwaitingFinalisation,
            _ => current
        };

        // ── Project list / details ─────────────────────────────────────────────

        public List<object> GetProjectList()
        {
            return [.. Directory.GetDirectories(_rootPath)
                .Select(dir =>
                {
                    var folder = Path.GetFileName(dir)!;
                    var project = LoadProject(folder);
                    return (object)new { Id = folder, project.Name };
                })];
        }

        public ProjectInfo GetProjectInfo(string folderName) => LoadProject(folderName);

        public string GetProjectDetails(string folderName)
        {
            var p = LoadProject(folderName);
            var sb = new StringBuilder();
            sb.AppendLine($"# {p.Name}");
            if (!string.IsNullOrWhiteSpace(p.Description))
                sb.AppendLine($"\n## Description\n{p.Description}");
            if (!string.IsNullOrWhiteSpace(p.Scope))
                sb.AppendLine($"\n## Scope\n{p.Scope}");
            if (p.Objectives.Count > 0)
            {
                sb.AppendLine("\n## Objectives");
                foreach (var o in p.Objectives) sb.AppendLine($"- {o}");
            }
            if (!string.IsNullOrWhiteSpace(p.SolutionFile))
                sb.AppendLine($"\n**Solution File:** {p.SolutionFile}");
            if (p.LastUpdate.HasValue)
                sb.AppendLine($"\n**Last Updated:** {p.LastUpdate:yyyy-MM-dd HH:mm} UTC");

            var activeCount = p.Backlog.Count(i => i.Status != BacklogStatus.Cancelled);
            var cancelledCount = p.Backlog.Count(i => i.Status == BacklogStatus.Cancelled);
            sb.AppendLine($"\n## Backlog\n{activeCount} active item(s), {cancelledCount} cancelled.");
            return sb.ToString();
        }

        // ── Objectives ─────────────────────────────────────────────────────────

        public void AddObjectives(string folderName, List<string> objectives)
        {
            var p = LoadProject(folderName);
            p.Objectives.AddRange(objectives);
            SaveProject(folderName, p);
        }

        public void ClearObjectives(string folderName)
        {
            var p = LoadProject(folderName);
            p.Objectives.Clear();
            SaveProject(folderName, p);
        }

        // ── Project metadata ───────────────────────────────────────────────────

        public void ModifyScope(string folderName, string scope)
        {
            var p = LoadProject(folderName);
            p.Scope = scope;
            SaveProject(folderName, p);
        }

        public void ModifyProjectHeader(string folderName, string name, string description)
        {
            var p = LoadProject(folderName);
            p.Name = name;
            p.Description = description;
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

        public Guid AddToDo(string folderName, string title, string? notes = null)
        {
            var p = LoadProject(folderName);
            var item = new BacklogItem { Title = title, Notes = notes };
            p.Backlog.Add(item);
            SaveProject(folderName, p);
            return item.Id;
        }

        public string ProgressToDo(string folderName, Guid id)
        {
            var p = LoadProject(folderName);
            var item = p.Backlog.FirstOrDefault(i => i.Id == id)
                       ?? throw new ArgumentException($"Backlog item {id} not found.");
            item.Status = NextStatus(item.Status);
            SaveProject(folderName, p);
            return item.Status.ToString();
        }

        public void ModifyToDoImpPlan(string folderName, Guid id, string impPlan)
        {
            var p = LoadProject(folderName);
            var item = p.Backlog.FirstOrDefault(i => i.Id == id)
                       ?? throw new ArgumentException($"Backlog item {id} not found.");
            item.ImpPlan = impPlan;
            SaveProject(folderName, p);
        }

        public void ModifyToDoTestPlan(string folderName, Guid id, string testPlan)
        {
            var p = LoadProject(folderName);
            var item = p.Backlog.FirstOrDefault(i => i.Id == id)
                       ?? throw new ArgumentException($"Backlog item {id} not found.");
            item.TestPlan = testPlan;
            SaveProject(folderName, p);
        }

        public void ModifyToDo(string folderName, Guid id, string? title = null, string? notes = null)
        {
            var p = LoadProject(folderName);
            var item = p.Backlog.FirstOrDefault(i => i.Id == id)
                       ?? throw new ArgumentException($"Backlog item {id} not found.");
            if (title is not null) item.Title = title;
            if (notes is not null) item.Notes = notes;
            SaveProject(folderName, p);
        }

        public void CancelToDo(string folderName, Guid id)
        {
            var p = LoadProject(folderName);
            var item = p.Backlog.FirstOrDefault(i => i.Id == id)
                       ?? throw new ArgumentException($"Backlog item {id} not found.");
            item.Status = BacklogStatus.Cancelled;
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

            // Build subdirectory info regardless of .aisummary
            object subdirectories = BuildSubdirectories(dir, depth);

            if (summary is not null)
            {
                return new
                {
                    name,
                    summary,
                    subdirectories
                };
            }

            // Files
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

            return new
            {
                name,
                files,
                subdirectories
            };
        }

        private static object BuildSubdirectories(string dir, int depth)
        {
            var dirs = Directory.GetDirectories(dir);
            if (dirs.Length > MaxItemsBeforeSummary)
                return new { subfolderCount = dirs.Length };

            if (depth >= MaxDepth - 1)
            {
                // At max depth just list names without recursing
                return dirs.Select(d => new { name = Path.GetFileName(d) }).ToList<object>();
            }

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
                    content = Convert.ToBase64String(buffer, 0, read)
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
                    content = string.Join(Environment.NewLine, chunk)
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
            {
                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
            }
            File.WriteAllText(path, text);
            try { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden); }
            catch (UnauthorizedAccessException) { /* best-effort; write already succeeded */ }
        }
    }
}
