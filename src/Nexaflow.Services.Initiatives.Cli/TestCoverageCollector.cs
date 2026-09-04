using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

namespace Nexaflow.Services.Initiatives.Cli;

/// <summary>
/// Reflects the built test assemblies for <c>[CoversNode]</c>/<c>[NoCoverage]</c> declarations and builds a
/// <see cref="TestCoverageManifest"/>. Reads <b>metadata only</b> via <see cref="MetadataLoadContext"/> — it
/// never executes the assembly, so a <c>net10.0-windows</c> test DLL's WPF/WinRT dependencies are not
/// loaded — plus the sibling portable PDB to recover each declaration's source file (needed so the
/// Integrity page can turn a ref into a <em>verifiable</em> <c>code</c> snaplink).
/// </summary>
internal static class TestCoverageCollector
{
    private const string CoversNode = "Nexaflow.Tests.Fixtures.CoversNodeAttribute";
    private const string NoCoverage = "Nexaflow.Tests.Fixtures.NoCoverageAttribute";

    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static TestCoverageManifest Collect(IReadOnlyList<string> testDlls, string repoRoot, string generatedIso)
    {
        var manifest = new TestCoverageManifest { Generated = generatedIso };

        var dlls = testDlls.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (dlls.Count == 0) return manifest;

        // A test DLL built inside a linked worktree carries that checkout's absolute paths in its PDB. Those
        // name the same repo files, so they are re-rooted rather than recorded — a manifest full of
        // ".claude/worktrees/<name>/…" would otherwise seed the Integrity page's "Add link" suggestions with
        // snaplinks that die the moment the branch merges.
        var paths = new RepoPaths(repoRoot, GitWorktrees.Roots(repoRoot));

        using var mlc = new MetadataLoadContext(BuildResolver(dlls));
        foreach (var dll in dlls)
        {
            // Recorded for every assembly considered, including one that then fails to load. Provenance
            // answers "is this still the file I looked at", not "did it contribute declarations" — and a
            // skipped assembly left unrecorded would read as a new one on the next check, so the manifest
            // would be judged stale forever and rescan on every single validate. Some are always skipped
            // here: the discovery walks every .csproj under the tests directory, which includes the
            // dependency-only libraries that have no tests to find.
            manifest.Assemblies.Add(Provenance(dll, repoRoot));

            try { ScanAssembly(mlc, dll, paths, manifest); manifest.ScannedAssemblies++; }
            catch (Exception ex) { Console.Error.WriteLine($"warn: skipped {Path.GetFileName(dll)}: {ex.Message}"); }
        }

        // Stable ordering so the manifest diffs cleanly between scans.
        manifest.Assemblies = [.. manifest.Assemblies.OrderBy(r => r.Path, StringComparer.Ordinal)];
        manifest.Coverage = manifest.Coverage
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value
                .OrderBy(r => r.Class, StringComparer.Ordinal)
                .ThenBy(r => r.Method ?? string.Empty, StringComparer.Ordinal)
                .ToList());
        manifest.NoCoverage = [.. manifest.NoCoverage.OrderBy(r => r.Class, StringComparer.Ordinal)];
        return manifest;
    }

    /// <summary>
    /// How an assembly looked when it was read. Size and write time together are a cheap, stable identity:
    /// a rebuild changes at least one of them, and — unlike a source file's write time — neither is
    /// disturbed by git restoring content, because git does not write build output.
    /// </summary>
    private static ScannedAssemblyRef Provenance(string dll, string repoRoot)
    {
        var info = new FileInfo(dll);
        return new ScannedAssemblyRef
        {
            Path         = Path.GetRelativePath(repoRoot, dll).Replace('\\', '/'),
            Size         = info.Exists ? info.Length : 0,
            WriteTimeUtc = info.Exists ? info.LastWriteTimeUtc.ToString("o") : string.Empty
        };
    }

    /// <summary>
    /// True when <paramref name="manifest"/> no longer describes what is on disk — an assembly was rebuilt
    /// or removed, or a test project has appeared that the scan never saw. One stat per assembly, so it is
    /// cheap enough to ask before every reconcile rather than leaving it to whoever remembers to rescan.
    /// </summary>
    public static bool NeedsRescan(TestCoverageManifest? manifest, IReadOnlyList<string> dlls, string repoRoot)
    {
        // No manifest is not staleness — coverage checks are simply skipped, as on a clean CI checkout.
        if (manifest is null) return false;

        // Written before provenance was recorded: refresh once, and it self-heals from then on.
        if (manifest.Assemblies.Count == 0) return dlls.Count > 0;

        var recorded = new Dictionary<string, ScannedAssemblyRef>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in manifest.Assemblies) recorded[a.Path] = a;

        if (recorded.Count != dlls.Count) return true;

        foreach (var dll in dlls)
        {
            var now = Provenance(dll, repoRoot);
            if (!recorded.TryGetValue(now.Path, out var then)) return true;
            if (then.Size != now.Size) return true;
            if (!string.Equals(then.WriteTimeUtc, now.WriteTimeUtc, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// Every <c>*.dll</c> beside each target, plus the host runtime dir (for the core assembly) — but
    /// <b>one path per assembly identity</b>. A resolver with the same identity at two paths makes
    /// <see cref="MetadataLoadContext"/> throw "already loaded"; that happens constantly here because each
    /// test bin dir carries its own copy of shared deps (Fixtures, the BCL) and many also sit in the runtime
    /// dir. Dedupe by file name, bin copies before runtime (app versions win).
    /// </summary>
    private static PathAssemblyResolver BuildResolver(IEnumerable<string> dlls)
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string f) { var n = Path.GetFileName(f); if (!byName.ContainsKey(n)) byName[n] = f; }

        foreach (var dll in dlls)
            foreach (var f in Directory.GetFiles(Path.GetDirectoryName(dll)!, "*.dll"))
                Add(f);
        foreach (var f in Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
            Add(f);

        return new PathAssemblyResolver(byName.Values.ToArray());
    }

    private static void ScanAssembly(MetadataLoadContext mlc, string dll, RepoPaths repoPaths, TestCoverageManifest manifest)
    {
        var asm = mlc.LoadFromAssemblyPath(dll);
        var asmName = asm.GetName().Name ?? Path.GetFileNameWithoutExtension(dll);
        using var pdb = PdbLookup.TryOpen(dll);

        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = [.. ex.Types.Where(t => t is not null)!]; }

        foreach (var type in types)
        {
            if (type is null) continue;

            IList<CustomAttributeData> typeAttrs;
            try { typeAttrs = type.GetCustomAttributesData(); }
            catch { continue; }

            var noCov = typeAttrs.FirstOrDefault(a => a.AttributeType.FullName == NoCoverage);
            if (noCov is not null)
                manifest.NoCoverage.Add(new NoCoverageRef
                {
                    Assembly = asmName, Class = type.FullName ?? type.Name, Reason = FirstStringArg(noCov)
                });

            // Class-level [CoversNode] — resolve the file once from any method that has debug info.
            var classCovers = typeAttrs.Where(a => a.AttributeType.FullName == CoversNode).ToList();
            var classFile = classCovers.Count > 0 ? ResolveTypeFile(type, pdb, repoPaths) : null;
            foreach (var a in classCovers)
                Add(manifest, FirstStringArg(a),
                    new TestRef { Assembly = asmName, Class = type.FullName ?? type.Name, Method = null, File = classFile ?? string.Empty });

            // Method-level [CoversNode] — resolve from that method, falling back to the type's file.
            MethodInfo[] methods;
            try { methods = type.GetMethods(AllDeclared); }
            catch { continue; }
            foreach (var m in methods)
            {
                List<CustomAttributeData> covers;
                try { covers = [.. m.GetCustomAttributesData().Where(a => a.AttributeType.FullName == CoversNode)]; }
                catch { continue; }
                if (covers.Count == 0) continue;

                var file = ResolveMethodFile(m, pdb, repoPaths) ?? classFile ?? ResolveTypeFile(type, pdb, repoPaths) ?? string.Empty;
                foreach (var a in covers)
                    Add(manifest, FirstStringArg(a),
                        new TestRef { Assembly = asmName, Class = type.FullName ?? type.Name, Method = m.Name, File = file });
            }
        }
    }

    private static string FirstStringArg(CustomAttributeData a) =>
        a.ConstructorArguments.Count > 0 ? a.ConstructorArguments[0].Value as string ?? string.Empty : string.Empty;

    private static void Add(TestCoverageManifest m, string nodeId, TestRef r)
    {
        if (string.IsNullOrWhiteSpace(nodeId)) return;
        if (!m.Coverage.TryGetValue(nodeId, out var list)) m.Coverage[nodeId] = list = [];
        list.Add(r);
    }

    private static string? ResolveTypeFile(Type type, PdbLookup? pdb, RepoPaths repoPaths)
    {
        if (pdb is null) return null;
        MethodInfo[] methods;
        try { methods = type.GetMethods(AllDeclared); }
        catch { return null; }
        foreach (var m in methods)
            if (ResolveMethodFile(m, pdb, repoPaths) is { Length: > 0 } f) return f;
        return null;
    }

    private static string? ResolveMethodFile(MethodInfo method, PdbLookup? pdb, RepoPaths repoPaths)
    {
        if (pdb is null) return null;
        int token;
        try { token = method.MetadataToken; }
        catch { return null; }
        return pdb.DocumentName(token) is { } name ? repoPaths.ToRepoRelative(name) : null;
    }

    /// <summary>The repo a scan records paths against: its root plus that root's linked worktrees.</summary>
    private readonly record struct RepoPaths(string Root, IReadOnlyList<string> Worktrees)
    {
        /// <summary>
        /// Normalizes a PDB document path to a forward-slash repo-relative path (matching how snaplink
        /// <c>doc</c> values are stored). Handles a path inside a linked worktree (a DLL built on a branch —
        /// re-rooted, because that is the same repo file), a real absolute local path (the usual local-build
        /// case) and a deterministic/SourceLink-mapped <c>/_/…</c> path; returns "" when it can't be re-rooted.
        /// </summary>
        public string ToRepoRelative(string docName)
        {
            if (GitWorktrees.TryReRoot(docName, Root, Worktrees, out var reRooted)) return reRooted;

            var p = docName.Replace('\\', '/');
            var root = Root.Replace('\\', '/').TrimEnd('/');
            if (p.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) return p[(root.Length + 1)..];
            if (p.StartsWith("/_/", StringComparison.Ordinal)) return p[3..];
            var i = p.IndexOf("/src/", StringComparison.OrdinalIgnoreCase);
            return i >= 0 ? p[(i + 1)..] : string.Empty;
        }
    }

    /// <summary>Reads a portable PDB and maps a methoddef token to its source document name.</summary>
    private sealed class PdbLookup : IDisposable
    {
        private readonly MetadataReaderProvider _provider;
        private readonly MetadataReader _reader;
        private readonly Stream _stream;

        /// <summary>
        /// Kickoff methoddef token → the document its body was written in, for methods whose own debug
        /// info has none.
        /// <para>
        /// An <c>async</c> method compiles to a stub that starts a state machine; the sequence points —
        /// and therefore the document — belong to the generated <c>MoveNext</c>, so asking the PDB about
        /// the method the test author actually wrote returns nothing. That is most UI and IO tests, and it
        /// left them out of the manifest entirely when every method in a class was async, since the
        /// class-level fallback probes those same methods. The PDB records the link in the other
        /// direction, so this walks it once per assembly and inverts it.
        /// </para>
        /// </summary>
        private readonly Dictionary<int, string> _stateMachineDocuments = [];

        private PdbLookup(MetadataReaderProvider provider, MetadataReader reader, Stream stream)
        {
            _provider = provider; _reader = reader; _stream = stream;
            IndexStateMachines();
        }

        private void IndexStateMachines()
        {
            foreach (var handle in _reader.MethodDebugInformation)
            {
                try
                {
                    var mdi = _reader.GetMethodDebugInformation(handle);
                    if (mdi.Document.IsNil) continue;

                    var kickoff = mdi.GetStateMachineKickoffMethod();
                    if (kickoff.IsNil) continue;

                    var token = MetadataTokens.GetToken(kickoff);
                    if (!_stateMachineDocuments.ContainsKey(token))
                        _stateMachineDocuments[token] = _reader.GetString(_reader.GetDocument(mdi.Document).Name);
                }
                catch { /* one unreadable entry must not cost the whole index */ }
            }
        }

        public static PdbLookup? TryOpen(string dll)
        {
            var pdbPath = Path.ChangeExtension(dll, ".pdb");
            if (!File.Exists(pdbPath)) return null;
            try
            {
                var stream = File.OpenRead(pdbPath);
                var provider = MetadataReaderProvider.FromPortablePdbStream(stream, MetadataStreamOptions.PrefetchMetadata);
                return new PdbLookup(provider, provider.GetMetadataReader(), stream);
            }
            catch { return null; }
        }

        public string? DocumentName(int methodToken)
        {
            try
            {
                var handle = MetadataTokens.MethodDefinitionHandle(methodToken & 0x00FFFFFF);
                var mdi = _reader.GetMethodDebugInformation(handle);
                if (mdi.Document.IsNil)
                    return _stateMachineDocuments.GetValueOrDefault(methodToken);   // async/iterator
                return _reader.GetString(_reader.GetDocument(mdi.Document).Name);
            }
            catch { return null; }
        }

        public void Dispose() { _provider.Dispose(); _stream.Dispose(); }
    }
}
