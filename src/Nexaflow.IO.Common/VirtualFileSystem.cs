using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nexaflow.IO.Common;

/// <summary>
/// The process-wide <see cref="IVirtualFileSystem"/>. Real paths pass straight through to
/// <see cref="System.IO"/> (byte-identical); paths that descend into a registered archive resolve the
/// entry by materialising it (and every container above it) to a cached temp file, so callers always
/// see an ordinary seekable file. Thread-safe.
/// </summary>
public sealed class VirtualFileSystem : IVirtualFileSystem
{
    public static VirtualFileSystem Instance { get; } = new();

    private readonly List<IArchiveHandler> _handlers = [];
    private readonly object _handlersLock = new();

    // Materialised temp files keyed by container-identity + inner entry path.
    private readonly ConcurrentDictionary<string, string> _materialized = new(System.StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _temps = new(System.StringComparer.OrdinalIgnoreCase);
    private readonly string _tempRoot;

    // LRU budget for the materialised temps: without it a long session browsing many archive entries
    // accumulates full-size extracted copies under %TEMP% until process exit. Least-recently-used
    // entries are evicted (temp deleted) once the total passes the cap; an evicted entry simply
    // re-extracts on next access.
    private const long MaxMaterializedBytes = 512L * 1024 * 1024;
    private readonly object _lruLock = new();
    private readonly Dictionary<string, (string Temp, long Bytes, long Stamp)> _lru = new(System.StringComparer.Ordinal);
    private long _materializedBytes;
    private long _lruStamp;

    /// <summary>Internal so tests can build an isolated instance with their own handlers without
    /// polluting the process-wide <see cref="Instance"/>.</summary>
    internal VirtualFileSystem()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "nexaflow-vfs");
        try { Directory.CreateDirectory(_tempRoot); } catch { /* best effort */ }
        System.AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupTemps();
    }

    // ── Handler registry ─────────────────────────────────────────────────────

    public void RegisterHandler(IArchiveHandler handler)
    {
        lock (_handlersLock)
            if (!_handlers.Contains(handler)) _handlers.Add(handler);
    }

    /// <summary>Number of registered handlers — logged at startup to catch the silent "provider DLL
    /// not named <c>Nexaflow.Features.Compressed.*</c>" footgun.</summary>
    public int HandlerCount { get { lock (_handlersLock) return _handlers.Count; } }

    private IArchiveHandler? HandlerFor(string fileName)
    {
        lock (_handlersLock)
            foreach (var h in _handlers)
                if (h.CanHandle(fileName)) return h;
        return null;
    }

    // ── Single-stream codec registry (gzip / brotli / zstd / lz4 / bzip2 …) ────

    private readonly List<IStreamCodec> _codecs = [];

    public void RegisterCodec(IStreamCodec codec)
    {
        lock (_handlersLock)
            if (!_codecs.Contains(codec)) _codecs.Add(codec);
    }

    /// <summary>Number of registered single-stream codecs (logged at startup alongside handlers).</summary>
    public int CodecCount { get { lock (_handlersLock) return _codecs.Count; } }

    /// <summary>The compressing codec whose extension is the trailing extension of <paramref name="fileName"/>
    /// (e.g. <c>x.tar.zst</c> or <c>x.zst</c> → the zstd codec), or null. Decode-only codecs are ignored.</summary>
    private IStreamCodec? CompressingCodecFor(string fileName)
    {
        var lower = Path.GetFileName(fileName).ToLowerInvariant();
        lock (_handlersLock)
            foreach (var c in _codecs)
                if (c.CanCompress && lower.EndsWith(c.Extension, System.StringComparison.Ordinal))
                    return c;
        return null;
    }

    // ── Path classification ──────────────────────────────────────────────────

    public bool IsContainer(string path)
        => !string.IsNullOrEmpty(path) && File.Exists(path) && HandlerFor(Path.GetFileName(path)) is not null;

    public bool IsContainerName(string fileName)
        => !string.IsNullOrEmpty(fileName) && HandlerFor(Path.GetFileName(fileName)) is not null;

    public ArchiveSummary? DescribeArchive(string containerPath)
    {
        if (!IsContainer(containerPath)) return null;
        var handler = HandlerFor(Path.GetFileName(containerPath))!;
        using var session = OpenSession(new Resolved(containerPath, Path.GetFileName(containerPath), string.Empty));
        return new ArchiveSummary(handler.Name, handler.Capabilities, session.Entries, session.Comment, session.IsEncrypted);
    }

    public (string RealContainer, string? Inner) SplitOutermostContainer(string path)
    {
        if (string.IsNullOrEmpty(path)) return (path, null);
        // Fast path: a real file/dir with nothing virtual below it (the overwhelmingly common case).
        if (File.Exists(path) || Directory.Exists(path)) return (path, null);

        var (realFile, remainder) = FindFirstRealFile(path);
        if (realFile is null || string.IsNullOrEmpty(remainder)) return (path, null);
        if (HandlerFor(Path.GetFileName(realFile)) is null) return (path, null); // real file, but not an archive
        return (realFile, remainder);
    }

    /// <summary>Walks <paramref name="path"/> from the root inward, returning the first segment that is
    /// an existing real <b>file</b> and the (back-slashed) remainder after it. Returns (null, null) when
    /// the path is all real directories, or diverges from the real file system before any file.</summary>
    private static (string? RealFile, string? Remainder) FindFirstRealFile(string path)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;
        var rest = path.Length > root.Length ? path[root.Length..] : string.Empty;
        var segments = rest.Split(['\\', '/'], System.StringSplitOptions.RemoveEmptyEntries);

        var prefix = root;
        for (int i = 0; i < segments.Length; i++)
        {
            prefix = prefix.Length == 0 ? segments[i] : Path.Combine(prefix, segments[i]);
            if (File.Exists(prefix))
                return (prefix, string.Join('\\', segments.Skip(i + 1)));
            if (!Directory.Exists(prefix))
                return (null, null);
        }
        return (null, null);
    }

    public bool Exists(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) return true;
        return GetEntryInfo(path) is not null;
    }

    public bool IsDirectory(string path)
    {
        if (Directory.Exists(path)) return true;
        if (IsContainer(path)) return true;                    // a real archive browses like a folder
        var info = GetEntryInfo(path);
        if (info is null) return false;
        if (info.IsDirectory) return true;
        // A nested archive entry (e.g. inner.zip inside another archive) is also browsable.
        return IsContainerName(Path.GetFileName(path));
    }

    public long GetLength(string path)
    {
        if (File.Exists(path)) return new FileInfo(path).Length;
        var info = GetEntryInfo(path);
        if (info is null || info.IsDirectory) throw new FileNotFoundException("No such file.", path);
        return info.Size;
    }

    public VirtualEntry? GetEntryInfo(string path)
    {
        if (Directory.Exists(path))
        {
            var di = new DirectoryInfo(path);
            return new VirtualEntry(di.Name, true, 0, 0, di.LastWriteTime);
        }
        if (File.Exists(path))
        {
            var fi = new FileInfo(path);
            return new VirtualEntry(fi.Name, false, fi.Length, fi.Length, fi.LastWriteTime);
        }

        var (container, inner) = SplitOutermostContainer(path);
        if (inner is null) return null;
        var resolved = ResolveToInnermost(container, inner);
        if (resolved is null) return null;
        var norm = Normalize(resolved.Inner);
        if (norm.Length == 0)
            return new VirtualEntry(Path.GetFileName(resolved.RealContainer), true, 0, 0, System.DateTime.Now);

        using var session = OpenSession(resolved);
        return session.Entries.FirstOrDefault(e => PathEquals(e.Name, norm))
            ?? (session.Entries.Any(e => IsUnder(e.Name, norm))
                ? new VirtualEntry(LastSegment(norm), true, 0, 0, System.DateTime.Now)  // a directory inside the archive
                : null);
    }

    // ── Enumeration ──────────────────────────────────────────────────────────

    public IReadOnlyList<VirtualEntry> EnumerateEntries(string path)
    {
        if (Directory.Exists(path)) return EnumerateRealDirectory(path);

        Resolved? resolved;
        if (IsContainer(path))
            resolved = new Resolved(path, Path.GetFileName(path), string.Empty);
        else
        {
            var (container, inner) = SplitOutermostContainer(path);
            if (inner is null) return [];
            resolved = ResolveToInnermost(container, inner);
            if (resolved is null) return [];
        }

        using var session = OpenSession(resolved);
        return ChildrenOf(session.Entries, Normalize(resolved.Inner));
    }

    private static IReadOnlyList<VirtualEntry> EnumerateRealDirectory(string path)
    {
        var result = new List<VirtualEntry>();
        foreach (var fsi in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            bool isDir = (fsi.Attributes & FileAttributes.Directory) != 0;
            long len = isDir ? 0 : ((FileInfo)fsi).Length;
            result.Add(new VirtualEntry(fsi.Name, isDir, len, len, fsi.LastWriteTime));
        }
        return result;
    }

    /// <summary>Direct children of the directory <paramref name="dir"/> ("" = archive root) within a flat
    /// entry list, synthesising directory entries for intermediate path segments.</summary>
    private static IReadOnlyList<VirtualEntry> ChildrenOf(IReadOnlyList<VirtualEntry> all, string dir)
    {
        var prefix = dir.Length == 0 ? string.Empty : dir + "/";
        var files = new List<VirtualEntry>();
        var dirs = new Dictionary<string, VirtualEntry>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var e in all)
        {
            var name = e.Name;
            if (!name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) continue;
            var tail = name[prefix.Length..].TrimEnd('/');
            if (tail.Length == 0) continue;

            int slash = tail.IndexOf('/');
            if (slash < 0)
            {
                if (e.IsDirectory) dirs.TryAdd(tail, e with { Name = tail });
                else files.Add(e with { Name = tail });
            }
            else
            {
                var childDir = tail[..slash];
                dirs.TryAdd(childDir, new VirtualEntry(childDir, true, 0, 0, e.Modified));
            }
        }
        return [.. dirs.Values, .. files];
    }

    // ── Reading ──────────────────────────────────────────────────────────────

    public Stream OpenRead(string path)
    {
        if (File.Exists(path))
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var real = MaterializeFile(path);
        return new FileStream(real, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    public Stream OpenReadWrite(string path)
    {
        if (File.Exists(path) || !IsVirtual(path))
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        var real = MaterializeFile(path);
        return new WriteBackStream(real, () => Replace(path, File.ReadAllBytes(real)));
    }

    public byte[] ReadAllBytes(string path)
        => File.Exists(path) ? File.ReadAllBytes(path) : File.ReadAllBytes(MaterializeFile(path));

    public string ReadAllText(string path, Encoding? encoding = null)
    {
        var real = File.Exists(path) ? path : MaterializeFile(path);
        return encoding is null ? File.ReadAllText(real) : File.ReadAllText(real, encoding);
    }

    public void WriteAllBytes(string path, byte[] bytes)
    {
        if (!IsVirtual(path)) { File.WriteAllBytes(path, bytes); return; }
        Replace(path, bytes);
    }

    public void WriteAllText(string path, string contents, Encoding? encoding = null)
    {
        // Match File.WriteAllText: emit the encoding's preamble (BOM) when it has one.
        var enc      = encoding ?? new UTF8Encoding(false);
        var preamble = enc.GetPreamble();
        var body     = enc.GetBytes(contents);
        WriteAllBytes(path, preamble.Length == 0 ? body : [.. preamble, .. body]);
    }

    public void Replace(string path, byte[] newContent)
    {
        if (!IsVirtual(path))
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + ".nexatmp";
            File.WriteAllBytes(tmp, newContent);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
            return;
        }
        WriteBackEntry(path, newContent);
    }

    private bool IsVirtual(string path) => SplitOutermostContainer(path).Inner is not null;

    // ── Archive resolution + materialisation ─────────────────────────────────

    private sealed record Resolved(string RealContainer, string ContainerFileName, string Inner);

    /// <summary>Descends through any nested containers named in <paramref name="inner"/>, materialising
    /// each intermediate container to a real temp file, and returns the innermost real container plus the
    /// remaining inner path within it. <paramref name="outerContainer"/> is the outermost real archive.</summary>
    private Resolved? ResolveToInnermost(string outerContainer, string inner)
    {
        var realContainer = File.Exists(outerContainer) ? outerContainer : MaterializeFile(outerContainer);
        var containerName = Path.GetFileName(outerContainer);
        var path = Normalize(inner);

        while (path.Length > 0)
        {
            // Find the longest leading sub-path that is itself an entry AND a nested container.
            string? nestedEntry = null;
            using (var session = OpenSession(new Resolved(realContainer, containerName, string.Empty)))
            {
                var segments = path.Split('/');
                // Include i == segments.Length so a nested container that is the *leaf* of the path
                // (e.g. listing inner.zip itself) is descended into, not just one with a remainder.
                for (int i = segments.Length; i >= 1; i--)
                {
                    var candidate = string.Join('/', segments.Take(i));
                    if (HandlerFor(LastSegment(candidate)) is not null &&
                        session.Entries.Any(e => !e.IsDirectory && PathEquals(e.Name, candidate)))
                    {
                        nestedEntry = candidate;
                        break;
                    }
                }
            }
            if (nestedEntry is null) break;   // 'path' lives directly within the current container

            var nestedReal = ExtractEntry(realContainer, containerName, nestedEntry);
            realContainer = nestedReal;
            containerName = LastSegment(nestedEntry);
            path = path[nestedEntry.Length..].TrimStart('/');
        }

        return new Resolved(realContainer, containerName, path);
    }

    /// <summary>Returns a real on-disk path for the file the (possibly virtual, possibly nested) path
    /// points to, extracting and caching as needed.</summary>
    private string MaterializeFile(string path)
    {
        if (File.Exists(path)) return path;
        var (container, inner) = SplitOutermostContainer(path);
        if (inner is null) throw new FileNotFoundException("Path does not resolve.", path);

        var resolved = ResolveToInnermost(container, inner)
            ?? throw new FileNotFoundException("Path does not resolve.", path);
        if (Normalize(resolved.Inner).Length == 0)
            return resolved.RealContainer;   // the path names a container itself
        return ExtractEntry(resolved.RealContainer, resolved.ContainerFileName, resolved.Inner);
    }

    private string ExtractEntry(string realContainer, string containerFileName, string entryPath)
    {
        var norm = Normalize(entryPath);
        var key = CacheKey(realContainer, norm);
        if (_materialized.TryGetValue(key, out var cached) && File.Exists(cached))
        {
            TouchLru(key);
            return cached;
        }

        using var session = OpenSession(new Resolved(realContainer, containerFileName, string.Empty));
        var entry = session.Entries.FirstOrDefault(e => !e.IsDirectory && PathEquals(e.Name, norm))
            ?? throw new FileNotFoundException($"Entry '{entryPath}' not found in '{containerFileName}'.");

        var temp = NewTempPath(Path.GetFileName(norm));
        using (var src = session.OpenEntry(entry.Name))
        using (var dst = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            src.CopyTo(dst);

        _temps[temp] = 0;
        _materialized[key] = temp;
        RecordMaterialized(key, temp);
        return temp;
    }

    private void TouchLru(string key)
    {
        lock (_lruLock)
            if (_lru.TryGetValue(key, out var e))
                _lru[key] = e with { Stamp = ++_lruStamp };
    }

    private void RecordMaterialized(string key, string temp)
    {
        long bytes = 0;
        try { bytes = new FileInfo(temp).Length; } catch { /* size unknown → counts as 0 */ }

        lock (_lruLock)
        {
            if (_lru.Remove(key, out var old)) _materializedBytes -= old.Bytes;   // re-extract of the same key
            _lru[key] = (temp, bytes, ++_lruStamp);
            _materializedBytes += bytes;

            // Evict least-recently-used until under budget — never the entry just added. A victim
            // whose file is still open just fails its delete (best effort) and re-extracts later.
            while (_materializedBytes > MaxMaterializedBytes && _lru.Count > 1)
            {
                var victim = _lru.Where(kv => kv.Key != key).OrderBy(kv => kv.Value.Stamp).First();
                _lru.Remove(victim.Key);
                _materializedBytes -= victim.Value.Bytes;
                _materialized.TryRemove(victim.Key, out _);
                TryDeleteTemp(victim.Value.Temp);
            }
        }
    }

    private void ForgetLru(string key)
    {
        lock (_lruLock)
            if (_lru.Remove(key, out var e)) _materializedBytes -= e.Bytes;
    }

    private IArchiveSession OpenSession(Resolved resolved)
    {
        var handler = HandlerFor(resolved.ContainerFileName)
            ?? throw new System.NotSupportedException($"No archive handler for '{resolved.ContainerFileName}'.");
        var stream = new FileStream(resolved.RealContainer, FileMode.Open, FileAccess.Read, FileShare.Read);
        return handler.Open(stream, resolved.ContainerFileName);
    }

    /// <summary>Rewrites the archive owning <paramref name="path"/> with that entry's bytes replaced.</summary>
    private void WriteBackEntry(string path, byte[] newContent)
    {
        var (container, inner) = SplitOutermostContainer(path);
        if (inner is null) throw new FileNotFoundException("Path does not resolve.", path);
        var resolved = ResolveToInnermost(container, inner)
            ?? throw new FileNotFoundException("Path does not resolve.", path);

        var handler = HandlerFor(resolved.ContainerFileName)
            ?? throw new System.NotSupportedException($"No archive handler for '{resolved.ContainerFileName}'.");
        if (!handler.Capabilities.HasFlag(ArchiveCapabilities.Modify))
            throw new System.NotSupportedException($"{handler.Name} archives are read-only.");

        var target = Normalize(resolved.Inner);
        var rebuilt = new List<ArchiveWriteEntry>();
        using (var session = handler.Open(
            new FileStream(resolved.RealContainer, FileMode.Open, FileAccess.Read, FileShare.Read),
            resolved.ContainerFileName))
        {
            foreach (var e in session.Entries)
            {
                if (e.IsDirectory) { rebuilt.Add(new ArchiveWriteEntry { Path = e.Name, IsDirectory = true, Modified = e.Modified }); continue; }
                if (PathEquals(e.Name, target))
                    rebuilt.Add(new ArchiveWriteEntry { Path = e.Name, Modified = System.DateTime.Now, OpenContent = () => new MemoryStream(newContent) });
                else
                {
                    var bytes = ReadEntryBytes(session, e.Name);
                    rebuilt.Add(new ArchiveWriteEntry { Path = e.Name, Modified = e.Modified, OpenContent = () => new MemoryStream(bytes) });
                }
            }
        }

        var tmp = resolved.RealContainer + ".nexatmp";
        using (var outStream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            handler.Write(outStream, resolved.ContainerFileName, rebuilt);
        File.Replace(tmp, resolved.RealContainer, null);

        // The container's bytes changed — drop cached extractions from it.
        InvalidateContainer(resolved.RealContainer);
    }

    public void ExtractAll(string containerPath, string destinationDir)
    {
        var summary = DescribeArchive(containerPath)
            ?? throw new System.NotSupportedException("Not a recognised archive.");
        var handler = HandlerFor(Path.GetFileName(containerPath))!;
        Directory.CreateDirectory(destinationDir);
        var fullDest = Path.GetFullPath(destinationDir);

        using var session = handler.Open(
            new FileStream(containerPath, FileMode.Open, FileAccess.Read, FileShare.Read), Path.GetFileName(containerPath));
        foreach (var e in summary.Entries)
        {
            if (e.IsDirectory) continue;
            var target = Path.GetFullPath(Path.Combine(fullDest, e.Name.Replace('/', Path.DirectorySeparatorChar)));
            // Zip-slip guard: never write outside the destination root.
            if (target != fullDest &&
                !target.StartsWith(fullDest + Path.DirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var src = session.OpenEntry(e.Name);
            using var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
            src.CopyTo(dst);
        }
    }

    public bool CanCreate(string archiveFileName)
    {
        var name  = Path.GetFileName(archiveFileName);
        var lower = name.ToLowerInvariant();
        if (CompressingCodecFor(lower) is { } codec)
        {
            // .tar.<codec> needs a tar writer; a single-stream .<codec> always can be written.
            if (lower[..^codec.Extension.Length].EndsWith(".tar", System.StringComparison.Ordinal))
                return HandlerFor("a.tar") is { } th && th.CanWrite("a.tar");
            return true;
        }
        return HandlerFor(name) is { } h && h.CanWrite(name);
    }

    public void CreateArchive(string archivePath, string sourceDir)
    {
        var name = Path.GetFileName(archivePath);
        var handler = HandlerFor(name) ?? throw new System.NotSupportedException($"No archive handler for '{name}'.");
        if (!handler.CanWrite(name))
            throw new System.NotSupportedException($"{handler.Name} cannot create '{Path.GetExtension(name)}' archives.");

        var fullSource = Path.GetFullPath(sourceDir);
        var entries = new List<ArchiveWriteEntry>();
        foreach (var file in Directory.EnumerateFiles(fullSource, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(fullSource, file).Replace('\\', '/');
            var captured = file;
            entries.Add(new ArchiveWriteEntry
            {
                Path = rel,
                Modified = File.GetLastWriteTime(captured),
                OpenContent = () => File.OpenRead(captured),
            });
        }

        var dir = Path.GetDirectoryName(archivePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = archivePath + ".nexatmp";
        using (var outStream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            handler.Write(outStream, name, entries);
        if (File.Exists(archivePath)) File.Replace(tmp, archivePath, null);
        else File.Move(tmp, archivePath);
    }

    public void Repackage(string sourcePath, string targetPath, ArchiveWriteOptions? options = null)
    {
        if (!IsContainer(sourcePath)) throw new System.NotSupportedException("Not a recognised archive.");
        var srcName = Path.GetFileName(sourcePath);
        var srcHandler = HandlerFor(srcName)!;

        var entries = new List<ArchiveWriteEntry>();
        using (var session = srcHandler.Open(
            new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read), srcName, new ArchiveOpenOptions { Password = options?.Password }))
        {
            foreach (var e in session.Entries)
            {
                if (e.IsDirectory) { entries.Add(new ArchiveWriteEntry { Path = e.Name, IsDirectory = true, Modified = e.Modified }); continue; }
                var bytes = ReadEntryBytes(session, e.Name);
                entries.Add(new ArchiveWriteEntry { Path = e.Name, Modified = e.Modified, OpenContent = () => new MemoryStream(bytes) });
            }
        }

        WriteEntries(targetPath, entries, options);
        InvalidateContainer(targetPath);
    }

    /// <summary>Writes <paramref name="entries"/> to <paramref name="targetPath"/>, choosing the backend by
    /// extension: a container handler for <c>.zip</c>/<c>.tar</c>; the tar handler + a codec for
    /// <c>.tar.&lt;codec&gt;</c>; or a single codec stream for a one-file <c>.&lt;codec&gt;</c> target.</summary>
    private void WriteEntries(string targetPath, List<ArchiveWriteEntry> entries, ArchiveWriteOptions? options)
    {
        var name  = Path.GetFileName(targetPath);
        var lower = name.ToLowerInvariant();
        var codec = CompressingCodecFor(lower);
        bool isTar = lower.EndsWith(".tar", System.StringComparison.Ordinal);

        // .tar.<codec> — tar to a temp, then compress that temp with the codec.
        if (codec is not null && lower[..^codec.Extension.Length].EndsWith(".tar", System.StringComparison.Ordinal))
        {
            var tarHandler = HandlerFor("a.tar")
                ?? throw new System.NotSupportedException("No tar handler is registered to build a .tar.* archive.");
            var tmpTar = targetPath + ".tar.nexatmp";
            try
            {
                using (var ts = new FileStream(tmpTar, FileMode.Create, FileAccess.Write, FileShare.None))
                    tarHandler.Write(ts, "archive.tar", entries, options);
                CompressFileWithCodec(tmpTar, targetPath, codec);
            }
            finally { try { File.Delete(tmpTar); } catch { /* best effort */ } }
            return;
        }

        // .<codec> single stream — holds exactly one file.
        if (codec is not null && !isTar)
        {
            var files = entries.FindAll(e => !e.IsDirectory);
            if (files.Count != 1)
                throw new System.NotSupportedException(
                    $"A {codec.Extension} file holds a single stream, but this archive has {files.Count} files — " +
                    $"convert to .tar{codec.Extension} instead.");
            var tmp = targetPath + ".nexatmp";
            using (var outStream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var cs = codec.Compress(outStream))
            using (var src = files[0].OpenContent!())
                src.CopyTo(cs);
            Commit(tmp, targetPath);
            return;
        }

        // Plain container handler (.zip / .tar).
        var handler = HandlerFor(name)
            ?? throw new System.NotSupportedException($"No archive handler for '{name}'.");
        if (!handler.CanWrite(name))
            throw new System.NotSupportedException($"{handler.Name} cannot create '{Path.GetExtension(name)}' archives.");
        var t = targetPath + ".nexatmp";
        using (var outStream = new FileStream(t, FileMode.Create, FileAccess.Write, FileShare.None))
            handler.Write(outStream, name, entries, options);
        Commit(t, targetPath);
    }

    private static void CompressFileWithCodec(string sourceFile, string targetPath, IStreamCodec codec)
    {
        var tmp = targetPath + ".nexatmp";
        using (var inStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var outStream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var cs = codec.Compress(outStream))
            inStream.CopyTo(cs);
        Commit(tmp, targetPath);
    }

    private static void Commit(string tmp, string targetPath)
    {
        if (File.Exists(targetPath)) File.Replace(tmp, targetPath, null);
        else
        {
            var d = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);
            File.Move(tmp, targetPath);
        }
    }

    public void AddFiles(string containerPath, IReadOnlyList<(string SourcePath, string EntryName)> files)
    {
        if (!IsContainer(containerPath)) throw new System.NotSupportedException("Not a recognised archive.");
        var name = Path.GetFileName(containerPath);
        var handler = HandlerFor(name) ?? throw new System.NotSupportedException("No archive handler.");
        if (!handler.Capabilities.HasFlag(ArchiveCapabilities.Modify))
            throw new System.NotSupportedException($"{handler.Name} archives are read-only.");

        var additions = files.ToDictionary(f => Normalize(f.EntryName), f => f.SourcePath, System.StringComparer.OrdinalIgnoreCase);
        var rebuilt = new List<ArchiveWriteEntry>();
        var replaced = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        using (var session = handler.Open(
            new FileStream(containerPath, FileMode.Open, FileAccess.Read, FileShare.Read), name))
        {
            foreach (var e in session.Entries)
            {
                if (e.IsDirectory) { rebuilt.Add(new ArchiveWriteEntry { Path = e.Name, IsDirectory = true, Modified = e.Modified }); continue; }
                var norm = Normalize(e.Name);
                if (additions.TryGetValue(norm, out var src))
                {
                    replaced.Add(norm);
                    rebuilt.Add(new ArchiveWriteEntry { Path = e.Name, Modified = System.DateTime.Now, OpenContent = () => File.OpenRead(src) });
                }
                else
                {
                    var bytes = ReadEntryBytes(session, e.Name);
                    rebuilt.Add(new ArchiveWriteEntry { Path = e.Name, Modified = e.Modified, OpenContent = () => new MemoryStream(bytes) });
                }
            }
        }
        foreach (var kv in additions)
            if (!replaced.Contains(kv.Key))
                rebuilt.Add(new ArchiveWriteEntry { Path = kv.Key, Modified = System.DateTime.Now, OpenContent = () => File.OpenRead(kv.Value) });

        var tmp = containerPath + ".nexatmp";
        using (var outStream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            handler.Write(outStream, name, rebuilt);
        File.Replace(tmp, containerPath, null);
        InvalidateContainer(containerPath);
    }

    private static byte[] ReadEntryBytes(IArchiveSession session, string entryPath)
    {
        using var s = session.OpenEntry(entryPath);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    // ── Temp cache plumbing ──────────────────────────────────────────────────

    private static string CacheKey(string realContainer, string innerEntry)
    {
        long mtime = 0, len = 0;
        try { var fi = new FileInfo(realContainer); mtime = fi.LastWriteTimeUtc.Ticks; len = fi.Length; } catch { }
        return $"{realContainer.ToLowerInvariant()}|{mtime}|{len}|{innerEntry.ToLowerInvariant()}";
    }

    private string NewTempPath(string name)
    {
        var safe = string.Concat(name.Split(Path.GetInvalidFileNameChars()));
        if (safe.Length == 0) safe = "entry";
        return Path.Combine(_tempRoot, $"{System.Guid.NewGuid():N}-{safe}");
    }

    private void InvalidateContainer(string realContainer)
    {
        var prefix = realContainer.ToLowerInvariant() + "|";
        foreach (var k in _materialized.Keys.Where(k => k.StartsWith(prefix, System.StringComparison.Ordinal)).ToList())
            if (_materialized.TryRemove(k, out var temp))
            {
                ForgetLru(k);
                TryDeleteTemp(temp);
            }
    }

    private void TryDeleteTemp(string temp)
    {
        _temps.TryRemove(temp, out _);
        try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
    }

    private void CleanupTemps()
    {
        foreach (var t in _temps.Keys.ToList()) TryDeleteTemp(t);
        try { if (Directory.Exists(_tempRoot) && !Directory.EnumerateFileSystemEntries(_tempRoot).Any()) Directory.Delete(_tempRoot); }
        catch { /* best effort */ }
    }

    // ── In-archive path helpers (forward-slash, ordinal-ignore-case) ─────────

    private static string Normalize(string p) => p.Replace('\\', '/').Trim('/');
    private static bool PathEquals(string a, string b)
        => string.Equals(Normalize(a), Normalize(b), System.StringComparison.OrdinalIgnoreCase);
    private static bool IsUnder(string entry, string dir)
        => Normalize(entry).StartsWith(Normalize(dir) + "/", System.StringComparison.OrdinalIgnoreCase);
    private static string LastSegment(string p)
    {
        var n = Normalize(p);
        var i = n.LastIndexOf('/');
        return i < 0 ? n : n[(i + 1)..];
    }
}

/// <summary>A read-write <see cref="FileStream"/> over a materialised temp file that pushes its bytes
/// back into the owning archive when closed.</summary>
file sealed class WriteBackStream(string tempPath, System.Action onClose)
    : FileStream(tempPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)
{
    private bool _closed;
    protected override void Dispose(bool disposing)
    {
        bool flush = disposing && !_closed;
        base.Dispose(disposing);
        if (flush) { _closed = true; onClose(); }
    }
}
