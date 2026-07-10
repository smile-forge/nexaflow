using System.Text.Json;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

namespace Nexaflow.Services.Initiatives.Cli;

/// <summary>
/// <c>nexaflow-initiatives</c> — headless access to the initiatives backend.
/// </summary>
/// <remarks>
/// Exit codes are the contract the installer build (and any script) relies on:
/// <c>0</c> clean or nothing to do, <c>1</c> broken snaplinks found, <c>2</c> usage/IO error.
/// A missing <c>.product/</c> is <b>not</b> a failure — it is gitignored working state, so a clean CI
/// checkout simply has nothing to validate.
/// </remarks>
internal static class Program
{
    private const int Clean = 0, Broken = 1, Error = 2;

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help") return Usage();

        return args[0] switch
        {
            "validate"   => Validate(args[1..]),
            "find"       => Find(args[1..]),
            "describe"   => Describe(args[1..]),
            "remap"      => Remap(args[1..]),
            "scan-tests" => ScanTests(args[1..]),
            "add-node"   => AddNode(args[1..]),
            _ => Usage($"unknown command '{args[0]}'")
        };
    }

    private static int Usage(string? error = null)
    {
        if (error is not null) Console.Error.WriteLine($"error: {error}");
        Console.WriteLine("""
            nexaflow-initiatives — product tracker tooling

            usage:
              nexaflow-initiatives validate   [<root>] [--json] [--save]
              nexaflow-initiatives find       <term> [<root>] [--json]
              nexaflow-initiatives describe   <node-id> [<root>] [--json]
              nexaflow-initiatives remap      <old-path> <new-path> [<root>] [--class <name>] [--method <name>]
              nexaflow-initiatives scan-tests [<root>] [--test-dll <path>]... [--suggest-attributes]
              nexaflow-initiatives add-node   <parent-id> <title> [<root>] [--id <slug>] [--desc <text>] [--status <s>]

            validate   Checks every snaplink still points at a real target (file exists, md heading resolves,
                       class/method declared, URL well formed) and that no RequiresSnaplink concern is
                       done/faulted with nothing backing it. --save writes .product/integrity.json.
            find       Lists nodes whose id/title/description contains <term> — "where is feature X".
            describe   Prints one node: path, status, concerns, and its code/test/doc snaplinks.
            remap      Rewrites snaplink doc paths from <old-path> to <new-path> (an exact file, or a
                       directory prefix) — the safe way to follow a rename/move — then re-validates.
                       --class/--method also set those on every affected link (single-file remaps).
            scan-tests Reflects the built test DLLs for [CoversNode] declarations → .product/test-coverage.json
                       (the cross-check the Integrity page reconciles against). Discovers test assemblies under
                       src/Nexaflow.Tests unless --test-dll is given. --suggest-attributes instead prints the
                       [CoversNode(...)] to add, derived from the tree's existing tests snaplinks (a bootstrap aid).
            add-node   Adds a child node under <parent-id> (id defaults to a slug of <title>) — the headless way
                       to grow the tree finer when a leaf needs sub-nodes. Attaches the default concerns, then
                       re-validates. --status defaults to 'should'.

            <root> defaults to the current directory. exit: 0 = ok, 1 = broken snaplinks, 2 = usage/IO error
            """);
        return error is null ? Clean : Error;
    }

    /// <summary>The first non-flag arg after those already consumed, or "." — the product root.</summary>
    private static string ResolveRoot(IEnumerable<string> args) =>
        Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith('-')) ?? ".");

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int Validate(string[] args)
    {
        var json = args.Contains("--json");
        var save = args.Contains("--save");
        var root = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? ".");

        if (!Directory.Exists(root)) return Usage($"no such directory: {root}");

        // .product/ is gitignored working state — absent means "nothing to validate", not "broken".
        if (!ProductStore.Exists(root))
        {
            if (!json) Console.WriteLine($"No .product/ under {root} — nothing to validate.");
            return Clean;
        }

        IntegrityReport report;
        try
        {
            var state = new ProductStore(root).Load();
            report = SnaplinkValidator.Validate(state, root);
            if (save) new ProductStore(root).SaveIntegrity(report);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: could not validate {root}: {ex.Message}");
            return Error;
        }

        if (json)
        {
            // Same serializer as the on-disk report, so `--json` and integrity.json are byte-comparable.
            Console.WriteLine(JsonSerializer.Serialize(report, ProductJson.Options));
            return report.IsClean ? Clean : Broken;
        }

        foreach (var i in report.Issues)
            Console.Error.WriteLine($"  {i.NodeId} [{i.Scope}] #{i.Index}  {i.Detail}");

        var scanned = $"scanned {report.ScannedSnaplinks} snaplinks across {report.ScannedNodes} nodes";
        if (report.IsClean)
        {
            Console.WriteLine($"Snaplinks OK — {scanned}.");
            return Clean;
        }

        var nodes = report.Issues.Select(i => i.NodeId).Distinct().Count();
        Console.Error.WriteLine($"{report.IssueCount} broken snaplink(s) across {nodes} node(s) — {scanned}.");
        return Broken;
    }

    // ── find / describe: the "where is feature X, and its code/tests/docs" index ──

    private static int Find(string[] args)
    {
        var term = args.FirstOrDefault(a => !a.StartsWith('-'));
        if (string.IsNullOrWhiteSpace(term)) return Usage("find needs a <term>.");
        var rest = args.Where(a => a != term).ToArray();
        if (!TryLoad(ResolveRoot(rest), out var state, out var code)) return code;

        var hits = ProductQuery.Find(state, term);
        if (args.Contains("--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(hits, ProductJson.Options));
            return Clean;
        }
        if (hits.Count == 0) { Console.WriteLine($"No nodes match '{term}'."); return Clean; }
        foreach (var h in hits)
            Console.WriteLine($"  {h.Id,-28} [{h.Status.ToString().ToLowerInvariant()}]  {h.Title}"
                            + $"   ({string.Join(" > ", h.Path.Take(h.Path.Count - 1).Select(c => c.Title))})");
        Console.WriteLine($"{hits.Count} match(es).");
        return Clean;
    }

    private static int Describe(string[] args)
    {
        var id = args.FirstOrDefault(a => !a.StartsWith('-'));
        if (string.IsNullOrWhiteSpace(id)) return Usage("describe needs a <node-id>.");
        var rest = args.Where(a => a != id).ToArray();
        if (!TryLoad(ResolveRoot(rest), out var state, out var code)) return code;

        var d = ProductQuery.Describe(state, id);
        if (d is null) { Console.Error.WriteLine($"error: no node '{id}' (try: find)."); return Error; }

        if (args.Contains("--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(d, ProductJson.Options));
            return Clean;
        }

        Console.WriteLine($"{d.Id}  [{d.Status.ToString().ToLowerInvariant()}]  {d.Title}");
        Console.WriteLine($"  path:    {string.Join(" > ", d.Path.Select(c => c.Title))}");
        if (!string.IsNullOrWhiteSpace(d.Description)) Console.WriteLine($"  about:   {d.Description}");
        if (!string.IsNullOrWhiteSpace(d.Note))        Console.WriteLine($"  note:    {d.Note}");
        if (d.Concerns.Count > 0)
            Console.WriteLine("  concerns: " + string.Join("  ", d.Concerns.Select(c => $"{c.Tag}={c.Status.ToString().ToLowerInvariant()}")));
        foreach (var g in d.Snaplinks.GroupBy(l => l.Kind).OrderBy(g => g.Key))
            foreach (var l in g)
                Console.WriteLine($"  {g.Key,-6} {l.Display}");
        if (d.Children.Count > 0)
            Console.WriteLine("  children: " + string.Join(", ", d.Children.Select(c => c.Id)));
        return Clean;
    }

    // ── remap: follow a rename/move without hand-editing tree.json ──

    private static int Remap(string[] args)
    {
        var positional = args.Where(a => !a.StartsWith('-') && !IsOptionValue(args, a)).ToArray();
        if (positional.Length < 2) return Usage("remap needs <old-path> <new-path>.");
        var oldPath = positional[0];
        var newPath = positional[1];
        var root = ResolveRoot(positional.Skip(2));
        if (!TryLoad(root, out var state, out var code)) return code;

        var changed = SnaplinkRemapper.Remap(state, oldPath, newPath, Option(args, "--class"), Option(args, "--method"));
        if (changed == 0)
        {
            Console.WriteLine($"No snaplink referenced '{oldPath}' - nothing remapped.");
            return Clean;
        }

        new ProductStore(root).SaveTree(state.Nodes);   // canonical serializer, same as the in-app editor

        // Re-validate so the effect is visible immediately (this is the point of a safe edit command).
        var report = SnaplinkValidator.Validate(state, root);
        Console.WriteLine($"Remapped {changed} snaplink(s): {oldPath} -> {newPath}.");
        Console.WriteLine(report.IsClean
            ? $"Snaplinks OK — scanned {report.ScannedSnaplinks}."
            : $"{report.IssueCount} broken snaplink(s) remain — run: validate .");
        new ProductStore(root).SaveIntegrity(report);
        return report.IsClean ? Clean : Broken;
    }

    // ── scan-tests: harvest declared test↔node coverage into the manifest the Integrity page reconciles ──

    private static int ScanTests(string[] args)
    {
        var root = ResolveRoot(args);
        if (!Directory.Exists(root)) return Usage($"no such directory: {root}");
        if (!ProductStore.Exists(root)) { Console.Error.WriteLine($"error: no .product/ under {root}."); return Error; }

        if (args.Contains("--suggest-attributes")) return SuggestAttributes(root);

        var explicitDlls = OptionValues(args, "--test-dll");
        var dlls = explicitDlls.Count > 0 ? explicitDlls : DiscoverTestDlls(root);
        if (dlls.Count == 0)
        {
            Console.Error.WriteLine("error: no test assemblies found — build the test projects first, or pass --test-dll <path>.");
            return Error;
        }

        TestCoverageManifest manifest;
        try { manifest = TestCoverageCollector.Collect(dlls, root, DateTime.Now.ToString("o")); }
        catch (Exception ex) { Console.Error.WriteLine($"error: scan failed: {ex.Message}"); return Error; }

        var store = new ProductStore(root);
        store.SaveTestCoverage(manifest);

        var refs = manifest.Coverage.Sum(kv => kv.Value.Count);
        var unresolved = manifest.Coverage.Sum(kv => kv.Value.Count(r => r.File.Length == 0));
        Console.WriteLine($"Scanned {manifest.ScannedAssemblies} test assembl{(manifest.ScannedAssemblies == 1 ? "y" : "ies")}: "
                        + $"{refs} declaration(s) across {manifest.Coverage.Count} node(s), {manifest.NoCoverage.Count} opt-out(s)"
                        + (unresolved > 0 ? $", {unresolved} with an unresolved file" : "") + $" → {store.TestCoverageFilePath}");
        return Clean;
    }

    /// <summary>Prints the [CoversNode] attributes implied by the tree's existing tests snaplinks — a bootstrap aid.</summary>
    private static int SuggestAttributes(string root)
    {
        if (!TryLoad(root, out var state, out var code)) return code;

        var byClass = new SortedDictionary<string, (string? File, SortedSet<string> Nodes)>(StringComparer.Ordinal);
        foreach (var (id, node) in state.Nodes)
            foreach (var l in node.Concerns?.FirstOrDefault(c => c.Tag == "tests")?.Snaplinks ?? [])
            {
                if (l.Type != "code" || string.IsNullOrWhiteSpace(l.Class)) continue;
                if (!byClass.TryGetValue(l.Class!, out var e))
                    byClass[l.Class!] = e = (l.Doc, new SortedSet<string>(StringComparer.Ordinal));
                e.Nodes.Add(id);
                if (e.File is null && l.Doc is not null) byClass[l.Class!] = (l.Doc, e.Nodes);
            }

        if (byClass.Count == 0) { Console.WriteLine("No tests-concern code snaplinks in the tree to derive suggestions from."); return Clean; }

        Console.WriteLine($"# {byClass.Count} test class(es) already linked in the tree — add these attributes:");
        foreach (var (cls, e) in byClass)
        {
            Console.WriteLine();
            if (e.File is not null) Console.WriteLine($"# {e.File}  (class {cls})");
            foreach (var n in e.Nodes) Console.WriteLine($"[CoversNode(\"{n}\")]");
        }
        return Clean;
    }

    private static List<string> DiscoverTestDlls(string root)
    {
        var testsDir = Path.Combine(root, "src", "Nexaflow.Tests");
        var result = new List<string>();
        if (!Directory.Exists(testsDir)) return result;
        foreach (var name in new[] { "Nexaflow.Tests.Core", "Nexaflow.Tests.Features", "Nexaflow.Tests.Providers" })
        {
            var binDir = Path.Combine(testsDir, name, "bin");
            if (!Directory.Exists(binDir)) continue;
            var newest = Directory.EnumerateFiles(binDir, name + ".dll", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (newest is not null) result.Add(newest);
        }
        return result;
    }

    /// <summary>All values following each occurrence of <paramref name="name"/> (a repeatable option).</summary>
    private static List<string> OptionValues(string[] args, string name)
    {
        var vals = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name) vals.Add(args[i + 1]);
        return vals;
    }

    // ── add-node: grow the tree finer (a sub-node under an existing node) without hand-editing tree.json ──

    private static int AddNode(string[] args)
    {
        var positional = args.Where(a => !a.StartsWith('-') && !FollowsFlag(args, a, "--id", "--desc", "--status")).ToArray();
        if (positional.Length < 2) return Usage("add-node needs <parent-id> <title>.");
        var parentId = positional[0];
        var title    = positional[1];
        var root     = ResolveRoot(positional.Skip(2));
        if (!TryLoad(root, out var state, out var code)) return code;

        if (!state.Nodes.TryGetValue(parentId, out var parent))
        {
            Console.Error.WriteLine($"error: no parent node '{parentId}' (try: find).");
            return Error;
        }

        string id;
        if (Option(args, "--id") is { Length: > 0 } explicitId)
        {
            id = Slug(explicitId);
            if (state.Nodes.ContainsKey(id)) { Console.Error.WriteLine($"error: node id '{id}' already exists."); return Error; }
        }
        else
        {
            id = UniqueId(state, Slug(title));
        }

        var defaults = state.Product.Concerns.Where(c => c.IsDefault).Select(c => c.Name).ToList();
        state.Nodes[id] = new ProductNode
        {
            Title       = title,
            Description = Option(args, "--desc"),
            Status      = ParseStatus(Option(args, "--status")),
            Parent      = parentId,
            Children    = [],
            Concerns    = defaults.Count > 0 ? [.. defaults.Select(n => new ConcernLink { Tag = n, Status = Status.Should })] : null
        };
        parent.Children.Add(id);

        var store = new ProductStore(root);
        store.SaveTree(state.Nodes);
        var report = SnaplinkValidator.Validate(state, root);
        store.SaveIntegrity(report);

        Console.WriteLine($"Added node '{id}' under '{parentId}': {title}");
        Console.WriteLine(report.IsClean
            ? $"Snaplinks OK — scanned {report.ScannedSnaplinks}."
            : $"{report.IssueCount} broken snaplink(s) remain — run: validate .");
        return report.IsClean ? Clean : Broken;
    }

    /// <summary>True when <paramref name="arg"/> is the value immediately following one of <paramref name="flags"/>.</summary>
    private static bool FollowsFlag(string[] args, string arg, params string[] flags)
    {
        var i = Array.IndexOf(args, arg);
        return i > 0 && flags.Contains(args[i - 1]);
    }

    /// <summary><paramref name="baseId"/> if free, else the first <c>baseId-2</c>, <c>baseId-3</c>… that isn't taken.</summary>
    private static string UniqueId(ProductState state, string baseId)
    {
        if (!state.Nodes.ContainsKey(baseId)) return baseId;
        for (var n = 2; ; n++)
            if (!state.Nodes.ContainsKey($"{baseId}-{n}")) return $"{baseId}-{n}";
    }

    /// <summary>Kebab-cases text into a node-id slug (lowercase alphanumerics, single hyphens between).</summary>
    private static string Slug(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s.Trim().ToLowerInvariant())
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        return sb.ToString().Trim('-') is { Length: > 0 } slug ? slug : "node";
    }

    private static Status ParseStatus(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "done"     => Status.Done,
        "shouldnt" => Status.Shouldnt,
        "faulted"  => Status.Faulted,
        _          => Status.Should
    };

    /// <summary>True when <paramref name="arg"/> is the value that follows a known option flag.</summary>
    private static bool IsOptionValue(string[] args, string arg)
    {
        var i = Array.IndexOf(args, arg);
        return i > 0 && args[i - 1] is "--class" or "--method";
    }

    /// <summary>Loads the tree, or emits the right message + exit code when there is nothing to load.</summary>
    private static bool TryLoad(string root, out ProductState state, out int code)
    {
        state = new ProductState();
        if (!Directory.Exists(root)) { Console.Error.WriteLine($"error: no such directory: {root}"); code = Error; return false; }
        if (!ProductStore.Exists(root)) { Console.Error.WriteLine($"error: no .product/ under {root}."); code = Error; return false; }
        try { state = new ProductStore(root).Load(); code = Clean; return true; }
        catch (Exception ex) { Console.Error.WriteLine($"error: {ex.Message}"); code = Error; return false; }
    }
}
