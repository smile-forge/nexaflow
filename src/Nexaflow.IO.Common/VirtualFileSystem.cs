using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

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

    // ── Mounts ───────────────────────────────────────────────────────────────

    private const string MountPrefix = VirtualMount.Prefix;

    // Immutable snapshot swapped wholesale: reads vastly outnumber writes and every public method takes
    // one, so a lock here would serialise the whole facade. Kept sorted longest-RealRoot-first so the
    // innermost mount wins when two nest.
    private VirtualMount[] _mounts = [];

    public IReadOnlyList<VirtualMount> Mounts => Volatile.Read(ref _mounts);

    public event System.Action? MountsChanged;

    public void RegisterMount(VirtualMount mount)
    {
        if (string.IsNullOrWhiteSpace(mount.Id) || mount.Id.AsSpan().IndexOfAny(@"\/:") >= 0)
            throw new System.ArgumentException(
                $"Mount id '{mount.Id}' must be non-empty and free of \\ / : — it forms a single path segment.",
                nameof(mount));
        if (string.IsNullOrWhiteSpace(mount.RealRoot))
            throw new System.ArgumentException("Mount RealRoot must be non-empty.", nameof(mount));

        while (true)
        {
            var current = Volatile.Read(ref _mounts);
            if (current.Contains(mount)) return;   // record equality — nothing changed, stay silent
            var next = current.Where(m => !IdEquals(m.Id, mount.Id))
                              .Append(mount)
                              .OrderByDescending(m => m.RealRoot.Length)
                              .ToArray();
            if (ReferenceEquals(Interlocked.CompareExchange(ref _mounts, next, current), current)) break;
        }
        MountsChanged?.Invoke();
    }

    public void UnregisterMount(string id)
    {
        while (true)
        {
            var current = Volatile.Read(ref _mounts);
            if (!current.Any(m => IdEquals(m.Id, id))) return;
            var next = current.Where(m => !IdEquals(m.Id, id)).ToArray();
            if (ReferenceEquals(Interlocked.CompareExchange(ref _mounts, next, current), current)) break;
        }
        MountsChanged?.Invoke();
    }

    private static bool IdEquals(string a, string b)
        => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

    private static bool LooksMounted(string path)
        => path.StartsWith(MountPrefix, System.StringComparison.Ordinal);

    /// <summary>Splits <c>::{id}\rest</c> into its mount and the remainder. Mount is null when the id
    /// names nothing registered (a stale saved path, or a mount removed while a tab was open).</summary>
    private (VirtualMount? Mount, string Remainder) SplitMount(string path)
    {
        var body = path[MountPrefix.Length..];
        int sep  = body.IndexOfAny(['\\', '/']);
        var id   = sep < 0 ? body : body[..sep];
        var rest = sep < 0 ? string.Empty : body[(sep + 1)..].Trim('\\', '/');
        foreach (var m in Volatile.Read(ref _mounts))
            if (IdEquals(m.Id, id)) return (m, rest);
        return (null, rest);
    }

    /// <summary>
    /// Maps a mounted path onto its real equivalent; returns anything else unchanged. Called at the head
    /// of every public method, BEFORE the <see cref="File.Exists"/> fast paths — after which a mounted
    /// path simply <i>is</i> a real path, so every rule below (archive descent, materialisation, atomic
    /// writes) applies verbatim with no mount-specific branch.
    /// </summary>
    private string ToReal(string path)
    {
        if (string.IsNullOrEmpty(path) || !LooksMounted(path)) return path;
        var (mount, rest) = SplitMount(path);
        if (mount is null) return path;   // unresolvable: leave it to fail as a missing path
        return rest.Length == 0 ? mount.RealRoot : Path.Combine(mount.RealRoot, rest.Replace('/', '\\'));
    }

    public VirtualBacking GetBacking(string path)
    {
        if (string.IsNullOrEmpty(path)) return VirtualBacking.Real;
        bool mounted = LooksMounted(path) && SplitMount(path).Mount is not null;
        var real = ToReal(path);
        if (SplitRealContainer(real).Inner is not null) return VirtualBacking.Materialized;
        return mounted ? VirtualBacking.PassThrough : VirtualBacking.Real;
    }

    public string? TryResolveReal(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (LooksMounted(path) && SplitMount(path).Mount is null) return null;
        var real = ToReal(path);
        return SplitRealContainer(real).Inner is null ? real : null;
    }

    public string? TryToVirtual(string realPath)
    {
        if (string.IsNullOrEmpty(realPath) || LooksMounted(realPath)) return null;
        var probe = realPath.TrimEnd('\\', '/');
        foreach (var m in Volatile.Read(ref _mounts))   // longest root first → innermost mount wins
        {
            var root = m.RealRoot.TrimEnd('\\', '/');
            if (root.Length == 0) continue;
            if (probe.Length == root.Length && string.Equals(probe, root, System.StringComparison.OrdinalIgnoreCase))
                return MountPrefix + m.Id;
            if (probe.Length > root.Length
                && probe.StartsWith(root, System.StringComparison.OrdinalIgnoreCase)
                && (probe[root.Length] == '\\' || probe[root.Length] == '/'))
                return MountPrefix + m.Id + "\\" + probe[(root.Length + 1)..];
        }
        return null;
    }

    public string? GetParentPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (LooksMounted(path))
        {
            var body = path[MountPrefix.Length..].TrimEnd('\\', '/');
            int sep  = body.LastIndexOfAny(['\\', '/']);
            return sep < 0 ? null : MountPrefix + body[..sep];   // at the mount root → This PC
        }
        try { return Directory.GetParent(path)?.FullName; } catch { return null; }
    }

    public string GetDisplayName(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        if (LooksMounted(path))
        {
            var (mount, rest) = SplitMount(path);
            if (rest.Length > 0) return LastSegment(rest);
            return mount?.Label ?? path[MountPrefix.Length..];
        }
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return name.Length > 0 ? name : path;   // a drive root has no file name — show it whole
    }

    public IReadOnlyList<(string Label, string Path)> GetBreadcrumbs(string path)
    {
        var crumbs = new List<(string, string)>();
        if (string.IsNullOrEmpty(path)) return crumbs;

        string built;
        string remainder;

        if (LooksMounted(path))
        {
            var (mount, rest) = SplitMount(path);
            var id = mount?.Id ?? path[MountPrefix.Length..].Split('\\', '/')[0];
            built     = MountPrefix + id;
            remainder = rest;
            crumbs.Add((mount?.Label ?? id, built));
        }
        else
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) { crumbs.Add((path, path)); return crumbs; }
            built     = root;
            remainder = path.Length > root.Length ? path[root.Length..] : string.Empty;
            crumbs.Add((root.TrimEnd(Path.DirectorySeparatorChar), root));
        }

        foreach (var part in remainder.Split(['\\', '/'], System.StringSplitOptions.RemoveEmptyEntries))
        {
            built = LooksMounted(built) ? built + "\\" + part : Path.Combine(built, part);
            crumbs.Add((part, built));
        }
        return crumbs;
    }

    // ── Path classification ──────────────────────────────────────────────────

    public bool IsContainer(string path)
    {
        path = ToReal(path);
        return !string.IsNullOrEmpty(path) && File.Exists(path) && HandlerFor(Path.GetFileName(path)) is not null;
    }

    public bool IsContainerName(string fileName)
        => !string.IsNullOrEmpty(fileName) && HandlerFor(Path.GetFileName(fileName)) is not null;

    public ArchiveSummary? DescribeArchive(string containerPath)
    {
        containerPath = ToReal(containerPath);
        if (!IsContainer(containerPath)) return null;
        var handler = HandlerFor(Path.GetFileName(containerPath))!;
        using var session = OpenSession(containerPath, Path.GetFileName(containerPath));
        return new ArchiveSummary(handler.Name, handler.Capabilities, session.Entries, session.Comment, session.IsEncrypted);
    }

    /// <summary>Mount-aware wrapper: a mounted path is mapped to its real equivalent first, so a pure
    /// mount hit yields <c>(realPath, null)</c> — "not inside an archive" — while
    /// <c>::docs\a.zip\x.cs</c> still splits at the real archive exactly as the unmounted path would.</summary>
    public (string RealContainer, string? Inner) SplitOutermostContainer(string path)
        => SplitRealContainer(ToReal(path));

    private (string RealContainer, string? Inner) SplitRealContainer(string path)
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
        path = ToReal(path);
        if (File.Exists(path) || Directory.Exists(path)) return true;
        return GetEntryInfo(path) is not null;
    }

    public bool IsDirectory(string path)
    {
        path = ToReal(path);
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
        path = ToReal(path);
        if (File.Exists(path)) return new FileInfo(path).Length;
        var info = GetEntryInfo(path);
        if (info is null || info.IsDirectory) throw new FileNotFoundException("No such file.", path);
        return info.Size;
    }

    public VirtualEntry? GetEntryInfo(string path)
    {
        path = ToReal(path);
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

        using var session = OpenSession(resolved.RealContainer, resolved.ContainerFileName);
        if (session.SupportsLazyBrowse)
            return session.StatEntry(norm);
        return session.Entries.FirstOrDefault(e => PathEquals(e.Name, norm))
            ?? (session.Entries.Any(e => IsUnder(e.Name, norm))
                ? new VirtualEntry(LastSegment(norm), true, 0, 0, System.DateTime.Now)  // a directory inside the archive
                : null);
    }

    // ── Enumeration ──────────────────────────────────────────────────────────

    public IReadOnlyList<VirtualEntry> EnumerateEntries(string path)
    {
        path = ToReal(path);
        if (Directory.Exists(path)) return EnumerateRealDirectory(path);

        Resolved? resolved;
        if (IsContainer(path))
            resolved = new Resolved(path, Path.GetFileName(path), string.Empty, []);
        else
        {
            var (container, inner) = SplitOutermostContainer(path);
            if (inner is null) return [];
            resolved = ResolveToInnermost(container, inner);
            if (resolved is null) return [];
        }

        using var session = OpenSession(resolved.RealContainer, resolved.ContainerFileName);
        return session.SupportsLazyBrowse
            ? session.ListChildren(Normalize(resolved.Inner))
            : ChildrenOf(session.Entries, Normalize(resolved.Inner));
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
        path = ToReal(path);
        if (File.Exists(path))
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var real = MaterializeFile(path);
        return new FileStream(real, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    public Stream OpenRead(string path, int bufferSize, bool useAsync)
        => new FileStream(MaterializeFile(ToReal(path)), FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                          bufferSize, useAsync);

    public Stream OpenReadWrite(string path)
    {
        path = ToReal(path);
        if (File.Exists(path) || !IsVirtual(path))
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        var real = MaterializeFile(path);
        return new WriteBackStream(real, () => Replace(path, File.ReadAllBytes(real)));
    }

    public byte[] ReadAllBytes(string path)
    {
        path = ToReal(path);
        return File.Exists(path) ? File.ReadAllBytes(path) : File.ReadAllBytes(MaterializeFile(path));
    }

    public string ReadAllText(string path, Encoding? encoding = null)
    {
        path = ToReal(path);
        var real = File.Exists(path) ? path : MaterializeFile(path);
        return encoding is null ? File.ReadAllText(real) : File.ReadAllText(real, encoding);
    }

    public void WriteAllBytes(string path, byte[] bytes)
    {
        path = ToReal(path);
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
        path = ToReal(path);
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

    /// <summary>One step down into a nested container: the parent's real file (the outermost archive, or a
    /// materialised temp of a container above it) and the entry within it that <i>is</i> the child container.
    /// A write deep inside must rebuild each parent in turn, so the chain has to survive resolution.</summary>
    private sealed record ContainerLink(string ParentFile, string ParentFileName, string EntryPath);

    /// <summary><see cref="Chain"/> is empty when the path lives directly in the outermost real archive;
    /// otherwise <c>Chain[0].ParentFile</c> is that archive and each later link's parent is the temp copy
    /// of the container above it.</summary>
    private sealed record Resolved(string RealContainer, string ContainerFileName, string Inner,
                                   IReadOnlyList<ContainerLink> Chain);

    /// <summary>Descends through any nested containers named in <paramref name="inner"/>, materialising
    /// each intermediate container to a real temp file, and returns the innermost real container plus the
    /// remaining inner path within it. <paramref name="outerContainer"/> is the outermost real archive.</summary>
    private Resolved? ResolveToInnermost(string outerContainer, string inner)
    {
        var realContainer = File.Exists(outerContainer) ? outerContainer : MaterializeFile(outerContainer);
        var containerName = Path.GetFileName(outerContainer);
        var path = Normalize(inner);
        var chain = new List<ContainerLink>();

        while (path.Length > 0)
        {
            // Find the longest leading sub-path that is itself an entry AND a nested container.
            string? nestedEntry = null;
            using (var session = OpenSession(realContainer, containerName))
            {
                var segments = path.Split('/');
                // Include i == segments.Length so a nested container that is the *leaf* of the path
                // (e.g. listing inner.zip itself) is descended into, not just one with a remainder.
                for (int i = segments.Length; i >= 1; i--)
                {
                    var candidate = string.Join('/', segments.Take(i));
                    if (HandlerFor(LastSegment(candidate)) is null) continue;
                    // Lazy sessions stat the one candidate path; eager sessions scan their flat list.
                    bool isFileEntry = session.SupportsLazyBrowse
                        ? session.StatEntry(candidate) is { IsDirectory: false }
                        : session.Entries.Any(e => !e.IsDirectory && PathEquals(e.Name, candidate));
                    if (isFileEntry)
                    {
                        nestedEntry = candidate;
                        break;
                    }
                }
            }
            if (nestedEntry is null) break;   // 'path' lives directly within the current container

            var nestedReal = ExtractEntry(realContainer, containerName, nestedEntry);
            chain.Add(new ContainerLink(realContainer, containerName, nestedEntry));
            realContainer = nestedReal;
            containerName = LastSegment(nestedEntry);
            path = path[nestedEntry.Length..].TrimStart('/');
        }

        return new Resolved(realContainer, containerName, path, chain);
    }

    /// <summary>Returns a real on-disk path for the file the (possibly virtual, possibly nested) path
    /// points to, extracting and caching as needed. Public so a caller that needs an actual launchable
    /// file path — e.g. handing an in-archive entry to a shell "open" verb — can get one.</summary>
    public string MaterializeFile(string path)
    {
        // A mounted path maps to a real file, so this returns it as-is: no extraction, no temp, no cache
        // entry. That is what lets every shell-handoff site (RealizeForShell, file properties, Open With)
        // treat a mount exactly like a real path without knowing mounts exist.
        path = ToReal(path);
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

        using var session = OpenSession(realContainer, containerFileName);
        // Lazy sessions (disk images) resolve the path themselves in OpenEntry — no full-list scan; eager
        // archives look the entry up first to recover its exact-case name.
        string openName;
        if (session.SupportsLazyBrowse)
            openName = norm;
        else
            openName = session.Entries.FirstOrDefault(e => !e.IsDirectory && PathEquals(e.Name, norm))?.Name
                ?? throw new FileNotFoundException($"Entry '{entryPath}' not found in '{containerFileName}'.");

        var temp = NewTempPath(Path.GetFileName(norm));
        using (var src = session.OpenEntry(openName))
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

    private IArchiveSession OpenSession(string realContainer, string containerFileName)
    {
        var handler = HandlerFor(containerFileName)
            ?? throw new System.NotSupportedException($"No archive handler for '{containerFileName}'.");
        var stream = new FileStream(realContainer, FileMode.Open, FileAccess.Read, FileShare.Read);
        return handler.Open(stream, containerFileName);
    }

    /// <summary>
    /// Rewrites the archive owning <paramref name="path"/> with that entry's bytes replaced. For a nested
    /// path (<c>outer.zip\inner.zip\x.txt</c>) the innermost container is rebuilt first, and its bytes become
    /// the replacement content for its own entry in the container above — repeated outward until the real
    /// archive on disk is rewritten. Rebuilding only the innermost would leave the edit in a temp copy.
    /// </summary>
    private void WriteBackEntry(string path, byte[] newContent)
    {
        var (container, inner) = SplitOutermostContainer(path);
        if (inner is null) throw new FileNotFoundException("Path does not resolve.", path);
        var resolved = ResolveToInnermost(container, inner)
            ?? throw new FileNotFoundException("Path does not resolve.", path);

        // Every level must be writable before anything is committed — a read-only container two levels up
        // would otherwise surface only after the inner rebuilds had already run.
        RequireModifiable(resolved.ContainerFileName);
        foreach (var link in resolved.Chain) RequireModifiable(link.ParentFileName);

        // Fold the chain inward-to-outward: each level's rebuilt bytes are the next level's entry content.
        var content = newContent;
        var file    = resolved.RealContainer;
        var name    = resolved.ContainerFileName;
        var entry   = Normalize(resolved.Inner);

        for (int i = resolved.Chain.Count - 1; i >= 0; i--)
        {
            using var ms = new MemoryStream();
            RebuildContainer(file, name, entry, content, ms);
            content = ms.ToArray();

            var link = resolved.Chain[i];
            file  = link.ParentFile;
            name  = link.ParentFileName;
            entry = link.EntryPath;
        }

        // 'file' is now the outermost real archive: stream its rebuild to a temp and swap it in atomically.
        var tmp = file + ".nexatmp";
        using (var outStream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            RebuildContainer(file, name, entry, content, outStream);
        File.Replace(tmp, file, null);

        // Every container in the chain was rebuilt — drop the cached extractions taken from each.
        InvalidateContainer(resolved.RealContainer);
        foreach (var link in resolved.Chain) InvalidateContainer(link.ParentFile);
    }

    private void RequireModifiable(string containerFileName)
    {
        var handler = HandlerFor(containerFileName)
            ?? throw new System.NotSupportedException($"No archive handler for '{containerFileName}'.");
        if (!handler.Capabilities.HasFlag(ArchiveCapabilities.Modify))
            throw new System.NotSupportedException($"{handler.Name} archives are read-only.");
    }

    /// <summary>Writes the archive in <paramref name="containerFile"/> to <paramref name="output"/> with
    /// <paramref name="entryPath"/>'s bytes replaced by <paramref name="newContent"/>. One call per level of
    /// a container chain. An entry path that matches nothing rewrites the archive unchanged.</summary>
    private void RebuildContainer(string containerFile, string containerFileName, string entryPath,
                                  byte[] newContent, Stream output)
    {
        var handler = HandlerFor(containerFileName)
            ?? throw new System.NotSupportedException($"No archive handler for '{containerFileName}'.");

        var target = Normalize(entryPath);
        var rebuilt = new List<ArchiveWriteEntry>();
        using (var session = OpenSession(containerFile, containerFileName))
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
        handler.Write(output, containerFileName, rebuilt);
    }

    public void ExtractAll(string containerPath, string destinationDir)
    {
        containerPath  = ToReal(containerPath);
        destinationDir = ToReal(destinationDir);
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
        archivePath = ToReal(archivePath);
        sourceDir   = ToReal(sourceDir);
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
        sourcePath = ToReal(sourcePath);
        targetPath = ToReal(targetPath);
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
        containerPath = ToReal(containerPath);
        if (!IsContainer(containerPath)) throw new System.NotSupportedException("Not a recognised archive.");
        var name = Path.GetFileName(containerPath);
        var handler = HandlerFor(name) ?? throw new System.NotSupportedException("No archive handler.");
        if (!handler.Capabilities.HasFlag(ArchiveCapabilities.Modify))
            throw new System.NotSupportedException($"{handler.Name} archives are read-only.");

        // Sources may themselves be mounted (dragging a file from a cloud folder into a zip).
        var additions = files.ToDictionary(f => Normalize(f.EntryName), f => ToReal(f.SourcePath), System.StringComparer.OrdinalIgnoreCase);
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
