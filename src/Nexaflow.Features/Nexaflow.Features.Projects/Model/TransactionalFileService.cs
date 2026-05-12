namespace Nexaflow.Features.Projects.Model
{
    using System.Collections.Concurrent;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Provides copy-on-write transactional file operations over an arbitrary root directory.
    /// Not MCP-exposed; all access is via ProjectService.
    /// </summary>
    public sealed class TransactionalFileService : IDisposable
    {
        private readonly ConcurrentDictionary<string, TransactionState> _transactions = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string> _activeByRoot = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _counters = new(StringComparer.OrdinalIgnoreCase);
        private readonly Timer _timeoutTimer;
        private static readonly TimeSpan TransactionTimeout = TimeSpan.FromMinutes(5);

        public TransactionalFileService()
        {
            _timeoutTimer = new Timer(CheckTimeouts, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        // ── Transaction lifecycle ──────────────────────────────────────────────

        /// <summary>
        /// Starts a transaction scoped to <paramref name="rootPath"/>.
        /// Only one transaction can be active per root path at a time.
        /// Returns an ID of the form <c>FT_{scopeName}_{n}</c>.
        /// </summary>
        internal string StartTransaction(string scopeName, string rootPath)
        {
            var normalizedRoot = Path.GetFullPath(rootPath);
            if (_activeByRoot.ContainsKey(normalizedRoot))
                throw new InvalidOperationException($"A transaction is already active for '{scopeName}'. Complete or abandon it first.");

            var counter = _counters.AddOrUpdate(scopeName, 1, (_, n) => n + 1);
            var id = $"FT_{scopeName}_{counter}";
            var pendingFolder = Path.Combine(normalizedRoot, ".pending", id);
            Directory.CreateDirectory(Path.Combine(pendingFolder, "files"));
            Directory.CreateDirectory(Path.Combine(pendingFolder, "new"));
            _transactions[id] = new TransactionState(id, normalizedRoot, pendingFolder);
            _activeByRoot[normalizedRoot] = id;
            return id;
        }

        internal void Complete(string transactionId)
        {
            var t = RemoveTransaction(transactionId);
            _activeByRoot.TryRemove(t.RootPath, out _);
            t.Dispose();
            DeletePendingFolder(t.PendingFolder);
        }

        internal string Abandon(string transactionId)
        {
            var t = RemoveTransaction(transactionId);
            _activeByRoot.TryRemove(t.RootPath, out _);
            return Rollback(t);
        }

        // ── Anchor-based search and edit ───────────────────────────────────────

        internal List<string> FindAndLabel(string transactionId, string absPath, string searchString, string anchorBaseName)
        {
            var t = Touch(transactionId);
            ValidateInRoot(t, absPath);
            if (!File.Exists(absPath)) throw new ArgumentException($"File not found: {Path.GetFileName(absPath)}");

            var content = File.ReadAllText(absPath);
            var indices = FindAllIndices(content, searchString);
            if (indices.Count == 0) throw new ArgumentException($"'{searchString}' not found in the file.");

            var names = new List<string>(indices.Count);
            lock (t.Lock)
            {
                for (var i = 0; i < indices.Count; i++)
                {
                    var name = indices.Count == 1 ? anchorBaseName : $"{anchorBaseName}_{i}";
                    t.Anchors[name] = new AnchorEntry(name, absPath, indices[i], searchString.Length);
                    names.Add(name);
                }
            }
            AttachWatcher(t, absPath);
            return names;
        }

        internal string AnchorReplace(string transactionId, string anchorName, string newContent)
        {
            var t = Touch(transactionId);
            var anchor = GetAnchor(t, anchorName);
            var content = File.ReadAllText(anchor.AbsoluteFilePath);
            var updated = string.Concat(content.AsSpan(0, anchor.StartIndex), newContent, content.AsSpan(anchor.StartIndex + anchor.Length));
            BackupIfNeeded(t, anchor.AbsoluteFilePath);
            WriteProtected(t, anchor.AbsoluteFilePath, updated);
            lock (t.Lock)
            {
                ShiftAnchors(t, anchor.AbsoluteFilePath, anchor.StartIndex + anchor.Length, newContent.Length - anchor.Length, anchorName);
                anchor.Length = newContent.Length;
            }
            return ContextSnippet(anchor.AbsoluteFilePath, anchor.StartIndex, updated);
        }

        internal string AnchorDelete(string transactionId, string anchorName)
        {
            var t = Touch(transactionId);
            var anchor = GetAnchor(t, anchorName);
            var content = File.ReadAllText(anchor.AbsoluteFilePath);
            var updated = string.Concat(content.AsSpan(0, anchor.StartIndex), content.AsSpan(anchor.StartIndex + anchor.Length));
            BackupIfNeeded(t, anchor.AbsoluteFilePath);
            WriteProtected(t, anchor.AbsoluteFilePath, updated);
            lock (t.Lock)
            {
                ShiftAnchors(t, anchor.AbsoluteFilePath, anchor.StartIndex + anchor.Length, -anchor.Length, anchorName);
                t.Anchors.Remove(anchorName);
            }
            return ContextSnippet(anchor.AbsoluteFilePath, anchor.StartIndex, updated);
        }

        internal string AnchorInsertAfter(string transactionId, string anchorName, string content)
        {
            var t = Touch(transactionId);
            var anchor = GetAnchor(t, anchorName);
            var fileContent = File.ReadAllText(anchor.AbsoluteFilePath);
            var insertAt = anchor.StartIndex + anchor.Length;
            var updated = string.Concat(fileContent.AsSpan(0, insertAt), content, fileContent.AsSpan(insertAt));
            BackupIfNeeded(t, anchor.AbsoluteFilePath);
            WriteProtected(t, anchor.AbsoluteFilePath, updated);
            lock (t.Lock) ShiftAnchors(t, anchor.AbsoluteFilePath, insertAt, content.Length, anchorName);
            return ContextSnippet(anchor.AbsoluteFilePath, insertAt, updated);
        }

        // ── File write operations ──────────────────────────────────────────────

        internal void BackupAndWrite(string transactionId, string absFilePath, string content)
        {
            var t = Touch(transactionId);
            ValidateInRoot(t, absFilePath);
            BackupIfNeeded(t, absFilePath);
            WriteProtected(t, absFilePath, content);
            InvalidateFileAnchors(t, absFilePath);
            AttachWatcher(t, absFilePath);
        }

        internal void BackupAndDelete(string transactionId, string absFilePath)
        {
            var t = Touch(transactionId);
            ValidateInRoot(t, absFilePath);
            BackupIfNeeded(t, absFilePath);
            lock (t.Lock) { t.Writing.Add(absFilePath); t.LastWriteTime[absFilePath] = DateTime.UtcNow; }
            try { File.Delete(absFilePath); }
            finally { lock (t.Lock) t.Writing.Remove(absFilePath); }
            InvalidateFileAnchors(t, absFilePath);
        }

        internal void BackupAndMove(string transactionId, string srcAbsPath, string dstAbsPath)
        {
            var t = Touch(transactionId);
            ValidateInRoot(t, srcAbsPath);
            ValidateInRoot(t, dstAbsPath);
            BackupIfNeeded(t, srcAbsPath);
            MarkNew(t, dstAbsPath);
            var destDir = Path.GetDirectoryName(dstAbsPath)!;
            if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
            lock (t.Lock) { t.Writing.Add(srcAbsPath); t.LastWriteTime[srcAbsPath] = DateTime.UtcNow; }
            try { File.Move(srcAbsPath, dstAbsPath); }
            finally { lock (t.Lock) t.Writing.Remove(srcAbsPath); }
            InvalidateFileAnchors(t, srcAbsPath);
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private TransactionState GetTransaction(string id) =>
            _transactions.TryGetValue(id, out var t)
                ? t
                : throw new ArgumentException($"Transaction '{id}' not found or has expired.");

        private TransactionState Touch(string id)
        {
            var t = GetTransaction(id);
            t.LastActivity = DateTime.UtcNow;
            return t;
        }

        private TransactionState RemoveTransaction(string id) =>
            _transactions.TryRemove(id, out var t)
                ? t
                : throw new ArgumentException($"Transaction '{id}' not found or has expired.");

        private static void ValidateInRoot(TransactionState t, string absPath)
        {
            if (!absPath.StartsWith(t.RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Path is not within the transaction's root directory.");
        }

        private static void BackupIfNeeded(TransactionState t, string absPath)
        {
            bool added;
            lock (t.Lock) added = t.BackedUp.Add(absPath);
            if (!added) return;

            if (File.Exists(absPath))
            {
                var rel = Path.GetRelativePath(t.RootPath, absPath);
                var dest = Path.Combine(t.PendingFolder, "files", rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(absPath, dest, overwrite: true);
            }
            else
            {
                MarkNew(t, absPath);
            }
        }

        private static void MarkNew(TransactionState t, string absPath)
        {
            var rel = Path.GetRelativePath(t.RootPath, absPath);
            var marker = Path.Combine(t.PendingFolder, "new", rel);
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, string.Empty);
        }

        private static void WriteProtected(TransactionState t, string absPath, string content)
        {
            var dir = Path.GetDirectoryName(absPath);
            if (dir is not null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            lock (t.Lock) { t.Writing.Add(absPath); t.LastWriteTime[absPath] = DateTime.UtcNow; }
            try { File.WriteAllText(absPath, content); }
            finally { lock (t.Lock) t.Writing.Remove(absPath); }
        }

        private static void AttachWatcher(TransactionState t, string absPath)
        {
            var dir = Path.GetDirectoryName(absPath)!;
            var file = Path.GetFileName(absPath);
            lock (t.Lock)
            {
                if (t.Watchers.Any(w => w.Path.Equals(dir, StringComparison.OrdinalIgnoreCase) && w.Filter == file))
                    return;
                var w = new FileSystemWatcher(dir, file)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };
                w.Changed += (_, _) => OnExternalFileChange(t, absPath);
                w.Renamed += (_, _) => OnExternalFileChange(t, absPath);
                w.Deleted += (_, _) => OnExternalFileChange(t, absPath);
                t.Watchers.Add(w);
            }
        }

        private static void OnExternalFileChange(TransactionState t, string absPath)
        {
            lock (t.Lock)
            {
                if (t.Writing.Contains(absPath)) return;
                // Ignore FSW events arriving within 1 s of our own last write (delayed delivery race)
                if (t.LastWriteTime.TryGetValue(absPath, out var last) && DateTime.UtcNow - last < TimeSpan.FromSeconds(1)) return;
                foreach (var a in t.Anchors.Values.Where(a => a.AbsoluteFilePath.Equals(absPath, StringComparison.OrdinalIgnoreCase)))
                    a.IsInvalidated = true;
            }
        }

        private static void InvalidateFileAnchors(TransactionState t, string absPath)
        {
            lock (t.Lock)
            {
                foreach (var a in t.Anchors.Values.Where(a => a.AbsoluteFilePath.Equals(absPath, StringComparison.OrdinalIgnoreCase)))
                    a.IsInvalidated = true;
            }
        }

        // Caller must hold t.Lock
        private static void ShiftAnchors(TransactionState t, string absPath, int afterIndex, int delta, string excludeName)
        {
            foreach (var a in t.Anchors.Values.Where(a =>
                !a.IsInvalidated &&
                !a.AnchorName.Equals(excludeName, StringComparison.Ordinal) &&
                a.AbsoluteFilePath.Equals(absPath, StringComparison.OrdinalIgnoreCase) &&
                a.StartIndex >= afterIndex))
            {
                a.StartIndex += delta;
            }
        }

        private static AnchorEntry GetAnchor(TransactionState t, string name)
        {
            AnchorEntry? a;
            lock (t.Lock) t.Anchors.TryGetValue(name, out a);
            if (a is null) throw new ArgumentException($"Anchor '{name}' not found.");
            if (a.IsInvalidated) throw new InvalidOperationException($"Anchor '{name}' is invalidated – the file was modified externally. Re-run FindAndLabel.");
            return a;
        }

        private static List<int> FindAllIndices(string text, string search)
        {
            var result = new List<int>();
            var idx = 0;
            while (true)
            {
                idx = text.IndexOf(search, idx, StringComparison.Ordinal);
                if (idx < 0) break;
                result.Add(idx);
                idx += search.Length;
            }
            return result;
        }

        private static string ContextSnippet(string absPath, int charIndex, string content)
        {
            var lines = content.Split('\n');
            var cumLen = 0;
            var editLine = lines.Length - 1;
            for (var i = 0; i < lines.Length; i++)
            {
                cumLen += lines[i].Length + 1;
                if (cumLen > charIndex) { editLine = i; break; }
            }
            var from = Math.Max(0, editLine - 3);
            var to = Math.Min(lines.Length - 1, editLine + 3);
            var sb = new StringBuilder();
            sb.AppendLine($"**Edited** `{Path.GetFileName(absPath)}` **~line {editLine + 1}:**");
            sb.AppendLine("```");
            for (var i = from; i <= to; i++) sb.AppendLine($"{i + 1,4}: {lines[i].TrimEnd('\r')}");
            sb.AppendLine("```");
            return sb.ToString();
        }

        private static void DeletePendingFolder(string pendingFolder)
        {
            if (Directory.Exists(pendingFolder))
                try { Directory.Delete(pendingFolder, recursive: true); } catch { /* best effort */ }
        }

        private static string Rollback(TransactionState t)
        {
            var restored = new List<string>();
            var filesDir = Path.Combine(t.PendingFolder, "files");
            var newDir = Path.Combine(t.PendingFolder, "new");

            if (Directory.Exists(filesDir))
            {
                foreach (var backup in Directory.GetFiles(filesDir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(filesDir, backup);
                    var dest = Path.Combine(t.RootPath, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(backup, dest, overwrite: true);
                    restored.Add(rel);
                }
            }

            if (Directory.Exists(newDir))
            {
                foreach (var marker in Directory.GetFiles(newDir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(newDir, marker);
                    var target = Path.Combine(t.RootPath, rel);
                    if (File.Exists(target)) { File.Delete(target); restored.Add($"(removed new file) {rel}"); }
                }
            }

            t.Dispose();
            DeletePendingFolder(t.PendingFolder);
            return restored.Count == 0
                ? "Rolled back. No file changes to undo."
                : $"Rolled back. Files restored: {string.Join(", ", restored)}";
        }

        private void CheckTimeouts(object? _)
        {
            var expired = _transactions.Values
                .Where(t => DateTime.UtcNow - t.LastActivity > TransactionTimeout)
                .ToList();
            foreach (var t in expired)
                if (_transactions.TryRemove(t.Id, out var removed))
                {
                    _activeByRoot.TryRemove(removed.RootPath, out string? _);
                    Rollback(removed);
                }
        }

        public void Dispose()
        {
            _timeoutTimer.Dispose();
            foreach (var t in _transactions.Values) t.Dispose();
        }

        // ── Nested types ───────────────────────────────────────────────────────

        private sealed class TransactionState(string id, string rootPath, string pendingFolder) : IDisposable
        {
            public string Id { get; } = id;
            public string RootPath { get; } = rootPath;
            public string PendingFolder { get; } = pendingFolder;
            public DateTime LastActivity { get; set; } = DateTime.UtcNow;
            public Dictionary<string, AnchorEntry> Anchors { get; } = new(StringComparer.Ordinal);
            public HashSet<string> BackedUp { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Writing { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, DateTime> LastWriteTime { get; } = new(StringComparer.OrdinalIgnoreCase);
            public List<FileSystemWatcher> Watchers { get; } = [];
            public Lock Lock { get; } = new();

            public void Dispose()
            {
                foreach (var w in Watchers)
                    try { w.EnableRaisingEvents = false; w.Dispose(); } catch { /* best effort */ }
            }
        }

        private sealed class AnchorEntry(string anchorName, string absFilePath, int startIndex, int length)
        {
            public string AnchorName { get; } = anchorName;
            public string AbsoluteFilePath { get; } = absFilePath;
            public int StartIndex { get; set; } = startIndex;
            public int Length { get; set; } = length;
            public bool IsInvalidated { get; set; }
        }
    }
}
