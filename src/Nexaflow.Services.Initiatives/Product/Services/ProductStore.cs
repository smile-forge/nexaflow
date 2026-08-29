using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Model;

namespace Nexaflow.Services.Initiatives.Product.Services;

/// <summary>
/// Reader/writer for a product. The live working state lives in a <b>gitignored</b> <c>.product/</c>
/// folder (<c>product.json</c> + <c>tree.json</c>) — git is poor at rapidly-changing metadata. Durable,
/// git-integrated records are produced by <b>snapshots</b>: frozen <c>&lt;version&gt;.json</c> files plus a
/// <c>PRODUCT.md</c> dashboard written to the committed export dir (default <c>docs/product/</c>).
/// </summary>
/// <summary>
/// The one serializer configuration for every product file (tree, product, snapshots, integrity report).
/// snake_case so the committed snapshots stay hand-editable / greppable / diff-friendly; enums serialize
/// as snake_case names rather than integers, so a report reads as <c>missing_file</c>, not <c>1</c>.
/// </summary>
public static class ProductJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented          = true,
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters             = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };
}

public sealed class ProductStore
{
    public const string CurrentVersion = "Current";

    private static readonly JsonSerializerOptions Json = ProductJson.Options;

    private readonly string _root;         // the folder that contains .product/
    private readonly string _dir;          // <root>/.product
    private readonly string? _graphScope;  // a working tree's own name, when the graph is scoped to one

    public ProductStore(string productRoot) : this(productRoot, null) { }

    /// <param name="graphScope">
    /// A working tree's name, when the <b>derived graph</b> should be that tree's own rather than the shared
    /// one. The authored tree (product.json, tree.json) is unaffected and always shared — only the graph,
    /// which is a function of source and therefore differs per branch, is scoped.
    /// </param>
    public ProductStore(string productRoot, string? graphScope)
    {
        _root       = productRoot;
        _dir        = DotProductDir(productRoot);
        _graphScope = Sanitise(graphScope);
    }

    /// <summary>A directory-safe form of a working tree's name.</summary>
    private static string? Sanitise(string? scope)
    {
        if (scope is not { Length: > 0 }) return null;
        var safe = new string([.. scope.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)]);
        return safe.Trim('.', ' ') is { Length: > 0 } cleaned ? cleaned : null;
    }

    public static string DotProductDir(string productRoot) => Path.Combine(productRoot, ".product");
    public static bool Exists(string productRoot) => Directory.Exists(DotProductDir(productRoot));

    /// <summary>On-disk path of the live tree (for file-watching).</summary>
    public string TreeFilePath => Path.Combine(_dir, "tree.json");

    /// <summary>Creates <c>.product/</c>, seeds product.json + tree.json, and gitignores the folder.</summary>
    public void Initialize(string productName)
    {
        Directory.CreateDirectory(_dir);
        SaveProduct(new ProductDocument { Product = productName });
        SaveTree(new Dictionary<string, ProductNode>());
        EnsureGitignored();
    }

    /// <summary>Ensures the repo ignores the live <c>.product/</c> folder (it's local working state).</summary>
    public void EnsureGitignored()
    {
        var gitignore = Path.Combine(_root, ".gitignore");
        var lines = File.Exists(gitignore) ? File.ReadAllLines(gitignore).ToList() : [];
        if (lines.Any(l => l.Trim().TrimEnd('/') == ".product")) return;
        if (lines.Count > 0 && lines[^1].Length > 0) lines.Add(string.Empty);
        lines.Add("# Nexaflow Product Manager — live metadata (snapshots are committed under the export dir)");
        lines.Add(".product/");
        File.WriteAllText(gitignore, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    // ── Live state ──────────────────────────────────────────────────────────

    public ProductState Load() => new()
    {
        Product = Read<ProductDocument>(Path.Combine(_dir, "product.json")) ?? new ProductDocument(),
        Nodes   = Read<TreeDoc>(TreeFilePath)?.Nodes ?? []
    };

    public void SaveProduct(ProductDocument product) => Write(Path.Combine(_dir, "product.json"), product);

    public void SaveTree(IReadOnlyDictionary<string, ProductNode> nodes) =>
        Write(TreeFilePath, new TreeDoc { Nodes = nodes as Dictionary<string, ProductNode> ?? new(nodes) });

    // ── Integrity report (derived — safe to delete; regenerate by re-validating) ──

    /// <summary>On-disk path of the last snaplink-validation result.</summary>
    public string IntegrityFilePath => Path.Combine(_dir, "integrity.json");

    /// <summary>The last saved report, or null when validation has never run for this product.</summary>
    public IntegrityReport? LoadIntegrity() => Read<IntegrityReport>(IntegrityFilePath);

    public void SaveIntegrity(IntegrityReport report) => Write(IntegrityFilePath, report);

    // ── Test-coverage manifest (derived — regenerate with `scan-tests`) ──

    /// <summary>On-disk path of the last test-coverage scan (declared [CoversNode] → test refs).</summary>
    public string TestCoverageFilePath => Path.Combine(_dir, "test-coverage.json");

    /// <summary>The last saved coverage manifest, or null when <c>scan-tests</c> has never run.</summary>
    public TestCoverageManifest? LoadTestCoverage() => Read<TestCoverageManifest>(TestCoverageFilePath);

    public void SaveTestCoverage(TestCoverageManifest manifest) => Write(TestCoverageFilePath, manifest);

    // ── Knowledge graph (derived — regenerate with `graph`; the Graph viewer opens this file) ──

    /// <summary>
    /// Where the derived graph lives: <c>.product/</c> for the main checkout, or
    /// <c>.product/worktrees/&lt;branch&gt;/</c> when scoped to one.
    /// <para>
    /// The tree is authored and shared, so it stays in one place. The graph is <i>derived from source</i>,
    /// and source differs per branch — so one shared graph meant a worktree either read another branch's
    /// view of the code or overwrote it with its own. Giving each working tree its own copy makes "is this
    /// graph describing my code?" answerable with yes, which is the whole point of the freshness check.
    /// </para>
    /// </summary>
    private string GraphDir => _graphScope is { Length: > 0 } scope
        ? Path.Combine(_dir, "worktrees", scope)
        : _dir;

    /// <summary>On-disk path of the generated knowledge graph (the file the Graph viewer opens).</summary>
    public string GraphFilePath => Path.Combine(GraphDir, "graph.json");

    /// <summary>Per-file incremental build state (content hashes + cached contributions). Derived — safe to delete.</summary>
    public string GraphCacheFilePath => Path.Combine(GraphDir, "graph-cache.json");

    /// <summary>The last generated graph, or null when <c>graph</c> has never run for this product.</summary>
    public KnowledgeGraph? LoadGraph() => Read<KnowledgeGraph>(GraphFilePath);

    /// <summary>Writes the graph atomically — a whole-repo graph is large enough that a torn mid-scan write matters.</summary>
    public void SaveGraph(KnowledgeGraph graph) => WriteAtomic(GraphFilePath, graph);

    /// <summary>The last incremental build cache, or null when it has never been written (→ a full first scan).</summary>
    public Nexaflow.Services.Initiatives.Graph.GraphCache? LoadGraphCache() =>
        Read<Nexaflow.Services.Initiatives.Graph.GraphCache>(GraphCacheFilePath);

    /// <summary>Writes the incremental build cache atomically alongside the graph.</summary>
    public void SaveGraphCache(Nexaflow.Services.Initiatives.Graph.GraphCache cache) => WriteAtomic(GraphCacheFilePath, cache);

    // ── Snapshots (committed, in the export dir) ──────────────────────────────

    public string ExportPath(string exportDir) => Path.Combine(_root, exportDir);

    /// <summary>Every snapshot found in the export dir (newest first by date), for the version dropdown.</summary>
    public IReadOnlyList<ProductSnapshot> ListSnapshots(string exportDir)
    {
        var dir = ExportPath(exportDir);
        if (!Directory.Exists(dir)) return [];
        var list = new List<ProductSnapshot>();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
            try
            {
                var snap = JsonSerializer.Deserialize<ProductSnapshot>(File.ReadAllText(file), Json);
                if (snap is { Version.Length: > 0 }) list.Add(snap);
            }
            catch { /* skip non-snapshot json */ }
        return [.. list.OrderByDescending(s => s.Date ?? string.Empty)];
    }

    public ProductSnapshot? LoadSnapshot(string exportDir, string version) =>
        ListSnapshots(exportDir).FirstOrDefault(s => s.Version == version);

    /// <summary>Writes a snapshot file and returns its full path.</summary>
    public string WriteSnapshot(string exportDir, ProductSnapshot snapshot)
    {
        var path = Path.Combine(ExportPath(exportDir), Slug(snapshot.Version) + ".json");
        Write(path, snapshot);
        return path;
    }

    public string WriteExportText(string exportDir, string fileName, string content)
    {
        var path = Path.Combine(ExportPath(exportDir), fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Deletes a snapshot file; returns its path (or null if absent).</summary>
    public string? DeleteSnapshot(string exportDir, string version)
    {
        var path = Path.Combine(ExportPath(exportDir), Slug(version) + ".json");
        if (!File.Exists(path)) return null;
        File.Delete(path);
        return path;
    }

    // ── IO ─────────────────────────────────────────────────────────────────

    private static T? Read<T>(string fullPath) where T : class =>
        File.Exists(fullPath) ? JsonSerializer.Deserialize<T>(File.ReadAllText(fullPath), Json) : null;

    private static void Write(string fullPath, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(value, Json));
    }

    /// <summary>Serialize to a sibling <c>.tmp</c> then atomically replace — no half-written file on crash.</summary>
    private static void WriteAtomic(string fullPath, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var tmp = fullPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
        File.Move(tmp, fullPath, overwrite: true);
    }

    private static string Slug(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Trim().ToLowerInvariant())
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "snapshot" : slug;
    }

    private sealed class TreeDoc { public Dictionary<string, ProductNode> Nodes { get; set; } = []; }
}
