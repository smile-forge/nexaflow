using System.Text.Json;
using System.Text.RegularExpressions;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Syntax;

namespace Nexaflow.Services.Initiatives.Cli;

/// <summary>
/// <c>nfi</c> — headless access to the initiatives backend.
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
    private const int WholeFilePreview = 40;   // describe --code: cap for a whole-file (no class/method) snaplink

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help") return Usage();

        return args[0] switch
        {
            "validate"    => Validate(args[1..]),
            "find"        => Find(args[1..]),
            "query"       => Query(args[1..]),
            "describe"    => Describe(args[1..]),
            "tree"        => Tree(args[1..]),
            "diff"        => Diff(args[1..]),
            "remap"       => Remap(args[1..]),
            "scan-tests"  => ScanTests(args[1..]),
            "add-node"    => AddNode(args[1..]),
            "set-status"  => SetStatus(args[1..]),
            "set-concern" => SetConcern(args[1..]),
            "remove-concern" => RemoveConcern(args[1..]),
            "add-snaplink"=> AddSnaplink(args[1..]),
            "remove-snaplink" => RemoveSnaplink(args[1..]),
            "set-node"    => SetNode(args[1..]),
            "move"        => Move(args[1..]),
            "rename"      => Rename(args[1..]),
            "remove"      => Remove(args[1..]),
            "batch"       => Batch(args[1..]),
            "lint"        => Lint(args[1..]),
            "doctor"      => Doctor(args[1..]),
            "graph"       => Graph(args[1..]),
            _ => Usage($"unknown command '{args[0]}'")
        };
    }

    private static int Usage(string? error = null)
    {
        if (error is not null) Console.Error.WriteLine($"error: {error}");
        Console.WriteLine("""
            nfi — product tracker tooling

            usage:
              nfi validate   [<root>] [--json] [--save]
              nfi find       <term> [<root>] [--json]
              nfi query      [<root>] [--under <id>] [--concern <tag>] [--status <s>] [--leaf|--panel] [--json]
              nfi describe   <node-id> [<root>] [--json] [--code]
              nfi tree       <node-id> [<root>] [--depth <n>] [--full] [--json]
              nfi diff       [<root>] [--from <version>]
              nfi remap      <old-path> <new-path> [<root>] [--class <name>] [--method <name>]
              nfi scan-tests [<root>] [--test-dll <path>]... [--suggest-attributes]
              nfi add-node   <parent-id> <title> [<root>] [--id <slug>] [--desc <text>] [--status <s>]
              nfi set-status  <node-id> <status> [<root>]
              nfi set-concern <node-id> <tag> <status> [<root>]
              nfi remove-concern <node-id> <tag> [<root>]
              nfi add-snaplink <node-id> --type <code|markdown|node|url> [<root>] [--concern <tag>]
                                                [--doc <p>] [--class <c>] [--method <m>] [--target <id>] [--url <u>] [--title-path a>b] [--status <s>]
              nfi remove-snaplink <node-id> [<root>] [--concern <tag>] [--index <n>]
              nfi set-node   <node-id> [<root>] [--title <t>] [--desc <d>] [--note <n>]
              nfi move       <node-id> <new-parent-id> [<root>]
              nfi rename     <old-id> <new-id> [<root>]
              nfi remove     <node-id> [<root>] [--recursive]
              nfi batch      <script-file> [<root>] [--dry-run]
              nfi lint       [<root>] [--under <id>] [--json]
              nfi doctor     [<root>] [--fix]
              nfi graph      [<root>] [--json] [--product-anchored]   (see: graph help — build + explore)

            validate   Checks every snaplink still points at a real target (file exists, md heading resolves,
                       class/method declared, URL well formed) and that no RequiresSnaplink concern is
                       done/faulted with nothing backing it. --save writes .product/integrity.json.
            find       Lists nodes whose id/title/description contains <term> — "where is feature X".
            query      Lists nodes filtered by subtree/concern/status/leafness — "which leaves still owe a
                       test". --under <id> limits to that node's subtree; --concern <tag> keeps nodes carrying
                       it and shows its status + snaplink count; --status matches the concern's status (or, with
                       no --concern, the node's DERIVED status — a parent rolls up its children, exactly like the
                       UI); --leaf / --panel keep only leaves / only parents.
                       --unbacked (with --concern) keeps only nodes carrying it with NO snaplink.
                       e.g. query --under git --concern tests --status done --unbacked → "tests done" with
                       nothing naming the test; drop --status to see every unbacked tests concern.
            describe   Prints one node: path, status, concerns, and its code/test/doc snaplinks.
                       --code also resolves each code snaplink to its actual source block (the class/method
                       via tree-sitter, or a whole-file preview), read from your working tree — so "show me
                       the code behind this node" is one call; a link that no longer resolves prints why.
            tree       Prints a whole subtree as an indented outline — the "show me this entire feature" view
                       (describe answers one node at a time). Each line is id [status] title + its concerns;
                       --full also prints each node's about/note and every snaplink, so a feature's tree/code
                       alignment is one call. --depth <n> caps the walk (the node itself is 0).
            diff       What changed in the live tree since the last committed release snapshot
                       (<export-dir>/<version>.json): nodes added/removed, node-status and concern-status
                       changes. --from <version> diffs against a specific snapshot instead of the newest.
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
            set-status Sets a node's status (should|done|shouldnt|faulted), cascading into should-only
                       descendant leaves + every should concern in the subtree (deliberate shouldnt/faulted and
                       already-done are protected). The typed, safe alternative to editing tree.json by hand.
            set-concern Adds or updates a node's link to a concern <tag> (must be in the product vocabulary),
                       setting its status. Creates the concern link if absent.
            remove-concern Drops a node's link to concern <tag> entirely (e.g. an is_default concern that
                       auto-attached where it doesn't belong, like AI Ready on a leaf).
            add-snaplink Attaches a snaplink to the node — or, with --concern <tag>, to that concern's link.
                       --type picks the shape: code (--doc/--class/--method), markdown (--doc/--title-path),
                       node (--target = another node id), url (--url). --status is optional.
            set-node   Edits a node's scalar fields (--title/--desc/--note); an empty --desc/--note clears it.
            move       Reparents <node-id> (and its subtree) under <new-parent-id> — detaches it from its old
                       parent and re-lists it under the new one, rejecting a move that would make a cycle. The
                       safe way to restructure the tree.
            rename     Changes a node's ID, retargeting its parent's children[], its children's parent back-refs
                       and every node-type snaplink pointing at it. Ids are ONE flat global namespace, so this
                       is how you specialise a too-generic one ('run' → 'dotnet-verb-run'). It cannot reach a
                       [CoversNode("<old-id>")] in test source — update those too (NXCOV002 flags them).
            remove     Deletes <node-id> (a leaf) from the tree and its parent's children[]. Refuses a node that
                       still has children unless --recursive, which deletes the whole subtree.
            batch      Applies a whole script of instructions to the tree in ONE load/save/validate — the batch
                       replacement for hand-editing tree.json. One instruction per line, each the same syntax as
                       a standalone verb minus <root>: set-status / set-concern / add-snaplink / set-node /
                       remove-concern / add-snaplink / remove-snaplink / add-node / move / remove. Blank lines and '#' comments
                       are skipped; "quote" a value with spaces. It is
                       transactional — if any line is invalid, NOTHING is written (the error names the line).
                       --dry-run parses + applies in memory and reports, writing nothing.
            lint       Checks a feature subtree against the modelling rules in docs/feature-tree-and-tests.md
                       §1-§4: the UI/Functionality/AI backbone, 'AI Ready' only on a feature root, panels and
                       state nodes journey-covered (no 'tests' concern), every leaf unit-tested, and a done
                       'tests' concern naming its test. ADVISORY — roles are inferred from position, so a
                       finding is a prompt to look, not a verdict; nothing here fails a build.
                       Scope it with --under <id> (e.g. lint --under git). exit: 0 always.
            doctor     Checks structural integrity — every child id resolves, every node is listed by its parent
                       — and with --fix rebuilds each parent's children[] from the child→parent back-references
                       (splitting an accidentally-concatenated id, re-attaching orphans). Also re-roots any
                       snaplink whose doc goes through a linked git worktree (.claude/worktrees/<name>/…) back
                       onto the repo's own copy. Use it after any tree corruption, or after linking work done
                       in a worktree. exit: 0 clean/fixed, 1 issues found without --fix.
            graph      Builds the knowledge graph (product tree ⊕ code AST ⊕ snaplinks) → .product/graph.json,
                       the file the Graph viewer opens. --json writes it to stdout instead of the file.
                       --product-anchored limits the code layer to snaplinked files (default: whole repo).
                       Sub-commands explore a built graph: stats / search / list / node / walk / code — see `graph help`.

            <root> defaults to the current directory. exit: 0 = ok, 1 = broken snaplinks, 2 = usage/IO error
            """);
        return error is null ? Clean : Error;
    }

    /// <summary>The product root from the first non-flag arg (or "."), resolved to where <c>.product/</c>
    /// actually lives — see <see cref="ResolveProductRoot"/> (which follows a git worktree to its main checkout).</summary>
    private static string ResolveRoot(IEnumerable<string> args) =>
        ResolveProductRoot(args.FirstOrDefault(a => !a.StartsWith('-')) ?? ".");

    private static bool _rootNoteShown;

    /// <summary>Resolves the directory that holds the (gitignored) <c>.product/</c> tree, so the CLI "just works"
    /// from anywhere — including a git worktree, whose <c>.product/</c> lives only in the main checkout. Order:
    /// the given path if it already has <c>.product/</c>; else, walking up to the enclosing git repo, the main
    /// working tree it links to; else the given path unchanged (the caller then reports "no .product/"). This is
    /// what lets you drop the trailing <c>&lt;root&gt;</c> and call the exe directly from any checkout or worktree.</summary>
    private static string ResolveProductRoot(string candidate)
    {
        candidate = Path.GetFullPath(candidate);
        if (ProductStore.Exists(candidate)) return candidate;
        if (TryFindMainCheckout(candidate, out var main) && ProductStore.Exists(main))
        {
            if (!_rootNoteShown && !PathsEqual(main, candidate))
            {
                _rootNoteShown = true;
                Console.Error.WriteLine($"note: using the .product/ tree in the main checkout {main} " +
                    "(you're in a linked worktree — tree edits land there, and any dumped source is that copy).");
            }
            return main;
        }
        return candidate;
    }

    /// <summary>Walks up from <paramref name="start"/> to the enclosing git working tree and returns the MAIN
    /// checkout: a <c>.git</c> directory marks the main tree itself; a <c>.git</c> file marks a linked worktree —
    /// follow its <c>gitdir:</c> pointer to <c>&lt;main&gt;/.git/worktrees/&lt;name&gt;</c>, read its <c>commondir</c>
    /// to reach the shared <c>&lt;main&gt;/.git</c>, whose parent is the main checkout. Pure file reads, no git process.</summary>
    private static bool TryFindMainCheckout(string start, out string mainRoot)
    {
        mainRoot = "";
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var dotGit = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(dotGit)) { mainRoot = dir.FullName; return true; }
            if (!File.Exists(dotGit)) continue;

            var gitdir = ReadGitdirPointer(dotGit, dir.FullName);
            if (gitdir is null) return false;
            var commonFile = Path.Combine(gitdir, "commondir");
            var commonRel  = File.Exists(commonFile) ? File.ReadAllText(commonFile).Trim() : "../..";
            var sharedGit  = Path.GetFullPath(Path.Combine(gitdir, commonRel));   // <main>/.git
            mainRoot = Path.GetDirectoryName(sharedGit.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "";
            return mainRoot.Length > 0;
        }
        return false;
    }

    /// <summary>The absolute target of a linked-worktree <c>.git</c> file's <c>gitdir: &lt;path&gt;</c> line.</summary>
    private static string? ReadGitdirPointer(string dotGitFile, string baseDir)
    {
        foreach (var line in File.ReadAllLines(dotGitFile))
            if (line.StartsWith("gitdir:", StringComparison.Ordinal))
            {
                var p = line["gitdir:".Length..].Trim();
                return Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(baseDir, p));
            }
        return null;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                      b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                      StringComparison.OrdinalIgnoreCase);

    private static int Validate(string[] args)
    {
        if (!TryRead(Specs.Validate, args, out var a, out var root, out var parseCode)) return parseCode;
        var json = a.Has("--json");
        var save = a.Has("--save");

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
            var store = new ProductStore(root);
            var state = store.Load();
            // The coverage manifest gates [CoversNode] ids that no longer exist. It is derived, gitignored
            // state, so LoadTestCoverage() returning null (clean CI checkout, or scan-tests never run) simply
            // means that check is skipped — never a failure, and never a false all-clear either.
            report = SnaplinkValidator.Validate(state, root, FileRootsFor(root), store.LoadTestCoverage());
            if (save) store.SaveIntegrity(report);
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

        // Same text the AI tool returns; the CLI adds the stream + exit-code convention on top, since a
        // broken tree has to fail a build script while the model just needs to read the verdict.
        var text = ProductReport.Validate(report);
        if (report.IsClean) { Console.WriteLine(text); return Clean; }

        Console.Error.WriteLine(text);
        return Broken;
    }

    // ── graph: product ⊕ code AST ⊕ snaplinks → .product/graph.json (the Graph viewer opens it) ──

    // ── graph: build, then explore (walk / query / fetch code) the generated knowledge graph ──

    private static int Graph(string[] args)
    {
        if (args.Length > 0)
            switch (args[0])
            {
                case "stats":                 return GraphStats(args[1..]);
                case "node":                  return GraphNode(args[1..]);
                case "search":                return GraphSearch(args[1..]);
                case "list":                  return GraphList(args[1..]);
                case "walk":                  return GraphWalk(args[1..]);
                case "context" or "ctx":      return GraphContext(args[1..]);
                case "grep":                  return GraphGrep(args[1..]);
                case "code" or "cat":         return GraphCode(args[1..]);
                case "build":                 return GraphBuild(args[1..]);
                case "help" or "-h" or "--help": return GraphUsage();
            }
        return GraphBuild(args);   // `graph [<root>] [--flags]` still builds (back-compat)
    }

    private static int GraphBuild(string[] args)
    {
        if (!TryRead(Specs.GraphBuild, args, out var a, out var root, out var parseCode)) return parseCode;
        var json = a.Has("--json");
        var wholeRepo = !a.Has("--product-anchored");
        var incremental = !a.Has("--no-incremental");

        if (!Directory.Exists(root)) return Usage($"no such directory: {root}");
        if (!ProductStore.Exists(root))
        {
            if (!json) Console.WriteLine($"No .product/ under {root} — nothing to graph.");
            return Clean;
        }

        var codeRoot = GraphCodeRoot(root, a);
        if (codeRoot is not null && !json)
            Console.Error.WriteLine($"note: building the code layer from the working tree {codeRoot} (the branch " +
                "you're on); the product tree + graph.json stay in the main checkout, and the content-addressed " +
                "cache re-parses only the files that differ from it.");

        KnowledgeGraph graph;
        try
        {
            var store = new ProductStore(root);
            var state = store.Load();
            var options = new GraphBuildOptions
            {
                Scope = wholeRepo ? GraphScope.WholeRepo : GraphScope.ProductAnchored,
                Incremental = incremental,
                CodeRoot = codeRoot,
                GeneratedAt = DateTime.Now.ToString("o"),
            };
            var cache = incremental ? store.LoadGraphCache() : null;   // reuse unchanged files' extraction
            var built = GraphBuilder.BuildWithCache(state, root, options, cache);
            graph = built.Graph;
            if (!json)
            {
                store.SaveGraph(graph);
                store.SaveGraphCache(built.Cache);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: could not build graph for {root}: {ex.Message}");
            return Error;
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(graph, ProductJson.Options));
            return Clean;
        }

        var m = graph.Metadata;
        Console.WriteLine($"Graph written to {new ProductStore(root).GraphFilePath} — " +
                          $"{m.NodeCount:N0} nodes, {m.EdgeCount:N0} edges, {m.HyperEdgeCount:N0} hyperedges.");
        return Clean;
    }

    // ── graph explore: walk / query the generated graph, and fetch a node's source ──

    private static int GraphUsage()
    {
        Console.WriteLine("""
            graph — build + explore the knowledge graph (.product/graph.json)

              graph [<root>] [--no-incremental] [--product-anchored] [--main|--code-root <dir>] [--json]   (re)build the graph
              graph stats  [<root>]                                             counts + type/edge/community breakdown
              graph search <term> [<root>] [--type <t>] [--limit N]             nodes whose id/label matches <term>
              graph list   [<root>] [--type <t>] [--community N] [--file <f>] [--limit N]   bulk list with filters
              graph node   <id> [<root>] [--limit N]                            one node + ALL its edges/hyperedges
              graph walk   <id> [<root>] [--hops N] [--types a,b] [--limit N]   BFS neighbourhood out to N hops
              graph context <id> [<root>] [--lines N] [--limit N]               ONE-SHOT: node + source + neighbours + owning feature
              graph grep <pat> [<root>] [--from <id>] [--hops N] [--mode index|content] [--type t] [--limit N]
                                                                                regex the graph (index=graph text; content=nodes' source)
              graph code   <id> [<root>] [--lines A-B]                          code:<file>#<ast> → its block; file:<relpath> → the file

            node types: product | file | type | member | external
            edge rels:  contains extends implements imports calls references instantiates tests documents view_of depends_on
            hyper rels: signature (member→return,params)  annotated (target→attr)  calls (caller→callee,args)
            ids:  product:<id>   file:<relpath>   code:<relpath>#<astpath>   external:<name>
            worktree: run from a linked worktree and the code layer defaults to THAT branch's source — build
                  re-parses only files that differ from the main checkout, and code/context/grep dump the branch's
                  copy. --main forces the main-checkout source; `graph --code-root <dir>` points it anywhere.
            tip:  `graph context <id>` is the fastest way to understand a node. `graph grep <pat> --from <id> --hops 2 --mode content`
                  searches the source of a feature's neighbourhood. .xaml → its class is a `view_of` edge; .csproj deps are `depends_on`.
            """);
        return Clean;
    }

    private static bool TryLoadGraph(string root, out KnowledgeGraph graph, out int code)
    {
        graph = null!;
        if (!Directory.Exists(root)) { Console.Error.WriteLine($"error: no such directory: {root}"); code = Error; return false; }
        var loaded = new ProductStore(root).LoadGraph();
        if (loaded is null)
        {
            Console.Error.WriteLine($"error: no graph at {new ProductStore(root).GraphFilePath} — build it first with: graph {root}");
            code = Error; return false;
        }
        graph = loaded; code = Clean; return true;
    }

    private static int TypeRank(string type) => type switch
    {
        NodeType.Product => 0, NodeType.Type => 1, NodeType.File => 2, NodeType.Member => 3, _ => 4,
    };

    /// <summary>One compact line for a node: id, type (+ code kind), label, and file:line when it has one.</summary>
    private static string NodeLine(GraphNode n)
    {
        var kind = (n.Type is NodeType.Type or NodeType.Member) && n.Metadata?.GetValueOrDefault("kind") is { Length: > 0 } k ? "/" + k : "";
        var loc = n.FilePath is { Length: > 0 } f
            ? $"  ({f}{(n.Metadata?.GetValueOrDefault("line") is { Length: > 0 } ln ? ":" + ln : "")})"
            : "";
        return $"  [{n.Type}{kind}] {n.Label}{loc}\n      {n.Id}";
    }

    private static int GraphStats(string[] args)
    {
        if (!TryRead(Specs.GraphStats, args, out _, out var root, out var parseCode)) return parseCode;
        if (!TryLoadGraph(root, out var g, out var code)) return code;

        var byType = g.Nodes.GroupBy(n => n.Type).OrderByDescending(x => x.Count());
        var byRel = g.Edges.GroupBy(e => e.Relationship).OrderByDescending(x => x.Count());
        var byHyper = g.HyperEdges.GroupBy(h => h.Relationship).OrderByDescending(x => x.Count());
        var comm = g.Nodes.Where(n => n.Community is not null).GroupBy(n => n.Community!.Value)
                          .OrderByDescending(x => x.Count()).Take(12);

        Console.WriteLine($"{g.Metadata.ProductName ?? "graph"} — {g.Nodes.Count:N0} nodes, {g.Edges.Count:N0} edges, "
                        + $"{g.HyperEdges.Count:N0} hyperedges, {g.Metadata.CommunityCount} communities (scope {g.Metadata.Scope}).");
        Console.WriteLine("nodes:  " + string.Join("  ", byType.Select(x => $"{x.Key}={x.Count():N0}")));
        Console.WriteLine("edges:  " + string.Join("  ", byRel.Select(x => $"{x.Key}={x.Count():N0}")));
        if (g.HyperEdges.Count > 0) Console.WriteLine("hyper:  " + string.Join("  ", byHyper.Select(x => $"{x.Key}={x.Count():N0}")));
        Console.WriteLine("biggest communities: " + string.Join("  ", comm.Select(x =>
        {
            var rep = x.OrderBy(n => n.Type == NodeType.Product ? 0 : 1).ThenBy(n => n.Label?.Length ?? 99).First();
            return $"#{x.Key}={x.Count()}({rep.Label})";
        })));
        Console.WriteLine("next:   graph search <term> | graph node product:<feature> | graph list --type file");
        return Clean;
    }

    private static int GraphSearch(string[] args)
    {
        if (!TryRead(Specs.GraphSearch, args, out var a, out var root, out var parseCode)) return parseCode;
        var term = a[0];
        if (!TryIntOpt(a, "--limit", 40, out var limit)) return Error;
        if (!TryLoadGraph(root, out var g, out var code)) return code;

        var hits = GraphQuery.Search(g, term, a.Value("--type"));
        Console.WriteLine(GraphReport.Search(hits, term, limit));
        return Clean;
    }

    private static int GraphList(string[] args)
    {
        if (!TryRead(Specs.GraphList, args, out var a, out var root, out var parseCode)) return parseCode;
        if (!TryIntOpt(a, "--limit", 60, out var limit)) return Error;
        if (!TryLoadGraph(root, out var g, out var code)) return code;

        var type = a.Value("--type");
        var file = a.Value("--file");
        int? comm = null;
        if (a.Value("--community") is { } cs)
        {
            if (!int.TryParse(cs, out var c)) return VerbUsage($"--community must be a number (got '{cs}')");
            comm = c;
        }
        var hits = g.Nodes.Where(n =>
                (type is null || n.Type == type) &&
                (comm is null || n.Community == comm) &&
                (file is null || (n.FilePath?.Contains(file, StringComparison.OrdinalIgnoreCase) ?? false)))
            .OrderBy(n => n.Id, StringComparer.Ordinal).ToList();

        foreach (var n in hits.Take(limit)) Console.WriteLine(NodeLine(n));
        Console.WriteLine($"{hits.Count} node(s)" + (hits.Count > limit ? $" — showing {limit} (raise --limit or add a filter)" : "") + ".");
        return Clean;
    }

    private static int GraphNode(string[] args)
    {
        if (!TryRead(Specs.GraphNode, args, out var a, out var root, out var parseCode)) return parseCode;
        var id = a[0];
        if (!TryIntOpt(a, "--limit", 30, out var limit)) return Error;
        if (!TryLoadGraph(root, out var g, out var code)) return code;

        var byId = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        foreach (var n in g.Nodes) byId[n.Id] = n;
        if (!byId.TryGetValue(id, out var node)) { Console.Error.WriteLine($"error: no node '{id}' (try: graph search)."); return Error; }
        string Label(string nid) => byId.TryGetValue(nid, out var o) ? o.Label : "?";

        Console.WriteLine($"{node.Id}\n  [{node.Type}]  {node.Label}");
        if (node.FilePath is { Length: > 0 }) Console.WriteLine($"  file:      {node.FilePath}{(node.Metadata?.GetValueOrDefault("line") is { Length: > 0 } ln ? ":" + ln : "")}");
        if (node.Language is { Length: > 0 }) Console.WriteLine($"  language:  {node.Language}");
        if (node.Community is { } cm) Console.WriteLine($"  community: #{cm}");
        if (node.Confidence < 0.999) Console.WriteLine($"  confidence:{node.Confidence:0.##}");
        if (node.Metadata is { Count: > 0 } md)
            foreach (var kv in md.Where(kv => kv.Key is not ("line" or "ast")))
                Console.WriteLine($"  {kv.Key}: {kv.Value}");

        void PrintEdges(char arrow, List<GraphEdge> edges, bool outgoing)
        {
            foreach (var grp in edges.GroupBy(e => e.Relationship).OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {arrow} {grp.Key} ({grp.Count()}):");
                foreach (var e in grp.Take(limit))
                {
                    var other = outgoing ? e.Target : e.Source;
                    Console.WriteLine($"      {Label(other)}{(e.Confidence < 0.999 ? $" ~{e.Confidence:0.##}" : "")}  {other}");
                }
                if (grp.Count() > limit) Console.WriteLine($"      … +{grp.Count() - limit} more (--limit)");
            }
        }

        var outE = g.Edges.Where(e => e.Source == id).ToList();
        var inE = g.Edges.Where(e => e.Target == id).ToList();
        var hyper = g.HyperEdges.Where(h => h.Endpoints.Any(p => p.Node == id)).ToList();
        if (outE.Count > 0) PrintEdges('→', outE, true);
        if (inE.Count > 0) PrintEdges('←', inE, false);
        foreach (var h in hyper.Take(limit))
            Console.WriteLine($"  ⬡ {h.Relationship}: " + string.Join(", ", h.Endpoints.Select(p => $"{p.Role}={Label(p.Node)}")));
        if (hyper.Count > limit) Console.WriteLine($"  ⬡ … +{hyper.Count - limit} more");
        if (outE.Count == 0 && inE.Count == 0 && hyper.Count == 0) Console.WriteLine("  (no edges)");
        return Clean;
    }

    private static int GraphWalk(string[] args)
    {
        if (!TryRead(Specs.GraphWalk, args, out var a, out var root, out var parseCode)) return parseCode;
        var id = a[0];
        if (!TryIntOpt(a, "--hops", 2, out var hops)) return Error;
        if (!TryIntOpt(a, "--limit", 150, out var limit)) return Error;
        if (!TryLoadGraph(root, out var g, out var code)) return code;

        var byId = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        foreach (var n in g.Nodes) byId[n.Id] = n;
        if (!byId.ContainsKey(id)) { Console.Error.WriteLine($"error: no node '{id}'."); return Error; }

        var types = a.Value("--types")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);

        var adj = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Link(string a, string b)
        {
            if (!adj.TryGetValue(a, out var s)) adj[a] = s = new HashSet<string>(StringComparer.Ordinal);
            s.Add(b);
        }
        foreach (var e in g.Edges) { Link(e.Source, e.Target); Link(e.Target, e.Source); }
        foreach (var h in g.HyperEdges)
            for (var i = 0; i < h.Endpoints.Count; i++)
                for (var j = i + 1; j < h.Endpoints.Count; j++) { Link(h.Endpoints[i].Node, h.Endpoints[j].Node); Link(h.Endpoints[j].Node, h.Endpoints[i].Node); }

        var dist = new Dictionary<string, int>(StringComparer.Ordinal) { [id] = 0 };
        var queue = new Queue<string>(); queue.Enqueue(id);
        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            if (dist[u] >= hops || !adj.TryGetValue(u, out var ns)) continue;
            foreach (var v in ns) if (!dist.ContainsKey(v)) { dist[v] = dist[u] + 1; queue.Enqueue(v); }
        }

        var reached = dist.Keys
            .Where(k => byId.ContainsKey(k) && (types is null || types.Contains(byId[k].Type)))
            .OrderBy(k => dist[k]).ThenBy(k => byId[k].Type, StringComparer.Ordinal).ThenBy(k => k, StringComparer.Ordinal)
            .ToList();

        foreach (var grp in reached.GroupBy(k => dist[k]))
        {
            Console.WriteLine($"— hop {grp.Key} ({grp.Count()}) —");
            foreach (var k in grp.Take(limit)) Console.WriteLine(NodeLine(byId[k]));
        }
        Console.WriteLine($"{reached.Count} node(s) within {hops} hop(s) of {byId[id].Label}" + (reached.Count > limit ? " (per-hop capped by --limit)" : "") + ".");
        return Clean;
    }

    private static int GraphCode(string[] args)
    {
        if (!TryRead(Specs.GraphCode, args, out var a, out var root, out var parseCode)) return parseCode;
        var id = a[0];

        // A code:<file>#<ast> id fetches that AST node's block; a file:<path> id (or a bare path) fetches the file.
        string rel; string? ast = null;
        if (id.StartsWith("code:", StringComparison.Ordinal) && id.Contains('#'))
        {
            var hash = id.IndexOf('#');
            rel = id["code:".Length..hash];
            ast = id[(hash + 1)..];
        }
        else rel = id.StartsWith("file:", StringComparison.Ordinal) ? id["file:".Length..] : id;

        var full = CodeFilePath(root, rel, a.Has("--main"));
        if (full is null) { Console.Error.WriteLine($"error: file not found: {rel}"); return Error; }
        var lines = File.ReadAllText(full).Replace("\r\n", "\n").Split('\n');

        int s0, e0;
        if (ast is not null)   // AST block
        {
            var lang = TreeSitterLanguages.ForFile(rel);
            var start = lang is null ? null : new CodeStructureExtractor().ResolveLine(lang, string.Join('\n', lines), ast);
            if (start is null) { Console.Error.WriteLine($"error: ast path '{ast}' not found in {rel} (regenerate the graph?)."); return Error; }
            s0 = start.Value - 1;
            e0 = BlockEnd(lines, s0, 400);
        }
        else if (a.Value("--lines") is { } range)   // a slice of a file
        {
            if (!TryRange(range, out var from, out var to))
                return VerbUsage($"--lines must be N or A-B (got '{range}')");
            s0 = Math.Max(0, from - 1);
            e0 = Math.Min(lines.Length - 1, to - 1);
        }
        else { s0 = 0; e0 = lines.Length - 1; }   // whole file

        Console.WriteLine($"// {rel}:{s0 + 1}-{e0 + 1}   {id}");
        for (var i = Math.Max(0, s0); i <= e0 && i < lines.Length; i++)
            Console.WriteLine($"{i + 1,5}  {lines[i]}");
        return Clean;
    }

    private static bool TryRange(string s, out int a, out int b)
    {
        a = b = 0;
        var dash = s.IndexOf('-');
        if (dash > 0 && int.TryParse(s[..dash], out a) && int.TryParse(s[(dash + 1)..], out b)) return true;
        if (int.TryParse(s, out a)) { b = a; return true; }
        return false;
    }

    /// <summary>The last line of the block starting at <paramref name="start"/>: brace-matched (skipping strings +
    /// comments) for a braced body, else to the terminating <c>;</c>, else a small window. Good enough for reading —
    /// not a compiler.</summary>
    private static int BlockEnd(string[] lines, int start, int maxLines)
    {
        int depth = 0; bool opened = false;
        bool inBlockComment = false, inString = false, inChar = false, verbatim = false;
        for (var i = start; i < lines.Length && i - start <= maxLines; i++)
        {
            var line = lines[i];
            bool inLineComment = false;
            for (var j = 0; j < line.Length; j++)
            {
                var ch = line[j];
                var next = j + 1 < line.Length ? line[j + 1] : '\0';
                if (inLineComment) break;
                if (inBlockComment) { if (ch == '*' && next == '/') { inBlockComment = false; j++; } continue; }
                if (inString)
                {
                    if (verbatim) { if (ch == '"' && next == '"') j++; else if (ch == '"') inString = false; }
                    else if (ch == '\\') j++;
                    else if (ch == '"') inString = false;
                    continue;
                }
                if (inChar) { if (ch == '\\') j++; else if (ch == '\'') inChar = false; continue; }
                switch (ch)
                {
                    case '/' when next == '/': inLineComment = true; break;
                    case '/' when next == '*': inBlockComment = true; j++; break;
                    case '"': inString = true; verbatim = j > 0 && line[j - 1] == '@'; break;
                    case '\'': inChar = true; break;
                    case '{': depth++; opened = true; break;
                    case '}': depth--; if (opened && depth <= 0) return i; break;
                    case ';' when !opened && depth == 0: return i;
                }
            }
        }
        return Math.Min(lines.Length - 1, start + Math.Min(maxLines, 40));
    }

    private static string[]? TryReadLines(string productRoot, string rel, bool main)
    {
        var full = CodeFilePath(productRoot, rel, main);
        return full is not null ? File.ReadAllText(full).Replace("\r\n", "\n").Split('\n') : null;
    }

    /// <summary>Resolves a repo-relative code file to an absolute path, most-specific first: the caller's own
    /// working tree (so a graph query from a linked worktree shows the code on the branch being edited), then the
    /// product root — unless <paramref name="main"/> forces the product (main-checkout) copy. Returns null when the
    /// file is in neither. In a normal checkout the two roots are the same directory, so this changes nothing.</summary>
    private static string? CodeFilePath(string productRoot, string rel, bool main)
    {
        var roots = main ? new[] { productRoot } : FileRootsFor(productRoot);
        foreach (var r in roots)
        {
            var full = Path.Combine(r, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) return full;
        }
        return null;
    }

    private static Dictionary<string, HashSet<string>> BuildAdjacency(KnowledgeGraph g)
    {
        var adj = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Link(string a, string b) { if (!adj.TryGetValue(a, out var s)) adj[a] = s = new HashSet<string>(StringComparer.Ordinal); s.Add(b); }
        foreach (var e in g.Edges) { Link(e.Source, e.Target); Link(e.Target, e.Source); }
        foreach (var h in g.HyperEdges)
            for (var i = 0; i < h.Endpoints.Count; i++)
                for (var j = i + 1; j < h.Endpoints.Count; j++) { Link(h.Endpoints[i].Node, h.Endpoints[j].Node); Link(h.Endpoints[j].Node, h.Endpoints[i].Node); }
        return adj;
    }

    private static Dictionary<string, int> Bfs(Dictionary<string, HashSet<string>> adj, string start, int hops)
    {
        var dist = new Dictionary<string, int>(StringComparer.Ordinal) { [start] = 0 };
        var q = new Queue<string>(); q.Enqueue(start);
        while (q.Count > 0)
        {
            var u = q.Dequeue();
            if (dist[u] >= hops || !adj.TryGetValue(u, out var ns)) continue;
            foreach (var v in ns) if (!dist.ContainsKey(v)) { dist[v] = dist[u] + 1; q.Enqueue(v); }
        }
        return dist;
    }

    /// <summary>One call that packs everything about a node an agent needs to act: identity + metadata, its source
    /// (budgeted), its immediate relationships, and the product feature(s) that own it — so a single fetch replaces a
    /// node+code+walk sequence.</summary>
    private static int GraphContext(string[] args)
    {
        if (!TryRead(Specs.GraphContext, args, out var a, out var root, out var parseCode)) return parseCode;
        var id = a[0];
        if (!TryIntOpt(a, "--limit", 6, out var near)) return Error;
        if (!TryIntOpt(a, "--lines", 60, out var sourceLines)) return Error;
        if (!TryLoadGraph(root, out var g, out var code)) return code;

        var byId = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        foreach (var n in g.Nodes) byId[n.Id] = n;
        if (!byId.TryGetValue(id, out var node)) { Console.Error.WriteLine($"error: no node '{id}' (try: graph search)."); return Error; }
        string Label(string nid) => byId.TryGetValue(nid, out var o) ? o.Label : "?";

        // 1 · identity
        Console.WriteLine($"{node.Id}\n  [{node.Type}] {node.Label}");
        if (node.FilePath is { Length: > 0 } fp) Console.WriteLine($"  file: {fp}{(node.Metadata?.GetValueOrDefault("line") is { Length: > 0 } l ? ":" + l : "")}");
        if (node.Community is { } cm) Console.WriteLine($"  community: #{cm}");
        if (node.Metadata is { Count: > 0 } md)
            foreach (var kv in md.Where(k => k.Key is not ("line" or "ast"))) Console.WriteLine($"  {kv.Key}: {kv.Value}");

        // 2 · source (code nodes) — budgeted
        if (node.FilePath is { Length: > 0 } rel && node.Metadata?.GetValueOrDefault("line") is { } lnStr
            && int.TryParse(lnStr, out var startLine) && TryReadLines(root, rel, a.Has("--main")) is { } lines)
        {
            var s0 = startLine - 1;
            var full = BlockEnd(lines, s0, 400);
            var e0 = Math.Min(full, s0 + sourceLines - 1);
            Console.WriteLine($"  --- source {rel}:{s0 + 1}-{e0 + 1} ---");
            for (var i = Math.Max(0, s0); i <= e0 && i < lines.Length; i++) Console.WriteLine($"  {i + 1,5}  {lines[i]}");
            if (full > e0) Console.WriteLine($"  … +{full - e0} more line(s) — graph code {node.Id}");
        }

        // 3 · immediate relationships
        foreach (var grp in g.Edges.Where(e => e.Source == id).GroupBy(e => e.Relationship).OrderBy(x => x.Key, StringComparer.Ordinal))
            Console.WriteLine($"  → {grp.Key} ({grp.Count()}): " + string.Join(", ", grp.Take(near).Select(e => Label(e.Target))) + (grp.Count() > near ? " …" : ""));
        foreach (var grp in g.Edges.Where(e => e.Target == id).GroupBy(e => e.Relationship).OrderBy(x => x.Key, StringComparer.Ordinal))
            Console.WriteLine($"  ← {grp.Key} ({grp.Count()}): " + string.Join(", ", grp.Take(near).Select(e => Label(e.Source))) + (grp.Count() > near ? " …" : ""));
        foreach (var h in g.HyperEdges.Where(h => h.Endpoints.Any(p => p.Node == id)).Take(near))
            Console.WriteLine($"  ⬡ {h.Relationship}: " + string.Join(", ", h.Endpoints.Select(p => $"{p.Role}={Label(p.Node)}")));

        // 4 · owning feature(s) — nearest product nodes
        var dist = Bfs(BuildAdjacency(g), id, 3);
        var owners = dist.Keys.Where(k => byId.TryGetValue(k, out var o) && o.Type == NodeType.Product)
                              .OrderBy(k => dist[k]).ThenBy(k => k, StringComparer.Ordinal).Take(5).ToList();
        if (owners.Count > 0) Console.WriteLine("  owning feature(s): " + string.Join(", ", owners.Select(k => $"{Label(k)} <{k}>")));
        return Clean;
    }

    /// <summary>Regex-grep the graph. <c>--mode index</c> (default) matches node id/label/metadata (the graph's own
    /// text); <c>--mode content</c> fetches the source block each in-scope code node refers to and greps that. Scope
    /// is the whole graph, or — with <c>--from &lt;id&gt; --hops N</c> — that node's neighbourhood.</summary>
    private static int GraphGrep(string[] args)
    {
        if (!TryRead(Specs.GraphGrep, args, out var a, out var root, out var parseCode)) return parseCode;
        var pattern = a[0];
        if (!TryIntOpt(a, "--hops", 2, out var hops)) return Error;

        var mode = a.Value("--mode") ?? "index";
        if (mode is not ("index" or "content")) return VerbUsage($"--mode must be index or content (got '{mode}')");
        if (!TryIntOpt(a, "--limit", mode == "content" ? 60 : 200, out var limit)) return Error;

        if (!TryLoadGraph(root, out var g, out var code)) return code;

        Regex rx;
        try { rx = new Regex(pattern, RegexOptions.IgnoreCase); }
        catch (Exception ex) { Console.Error.WriteLine($"error: bad regex: {ex.Message}"); return Error; }

        var byId = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        foreach (var n in g.Nodes) byId[n.Id] = n;
        var type = a.Value("--type");

        IEnumerable<GraphNode> scope = g.Nodes;
        if (a.Value("--from") is { } from)
        {
            if (!byId.ContainsKey(from)) { Console.Error.WriteLine($"error: no node '{from}'."); return Error; }
            var set = new HashSet<string>(Bfs(BuildAdjacency(g), from, hops).Keys, StringComparer.Ordinal);
            scope = g.Nodes.Where(n => set.Contains(n.Id));
        }
        if (type is not null) scope = scope.Where(n => n.Type == type);

        if (mode == "content")
        {
            var codeNodes = scope.Where(n => n.FilePath is { Length: > 0 } && n.Metadata != null
                                             && n.Metadata.ContainsKey("line") && n.Metadata.ContainsKey("ast"))
                                 .OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
            var fileCache = new Dictionary<string, string[]?>(StringComparer.Ordinal);
            int scanned = 0, matchedNodes = 0;
            foreach (var n in codeNodes)
            {
                if (scanned >= limit)
                {
                    Console.WriteLine($"… content-scan cap {limit} hit ({codeNodes.Count} code nodes in scope) — narrow --from/--hops/--type or raise --limit.");
                    break;
                }
                scanned++;
                if (!fileCache.TryGetValue(n.FilePath!, out var lines)) fileCache[n.FilePath!] = lines = TryReadLines(root, n.FilePath!, a.Has("--main"));
                if (lines is null || !int.TryParse(n.Metadata!["line"], out var startLine)) continue;
                var s0 = startLine - 1;
                var e0 = BlockEnd(lines, s0, 400);
                var hits = new List<(int Line, string Text)>();
                for (var i = Math.Max(0, s0); i <= e0 && i < lines.Length; i++) if (rx.IsMatch(lines[i])) hits.Add((i + 1, lines[i]));
                if (hits.Count == 0) continue;
                matchedNodes++;
                Console.WriteLine(NodeLine(n));
                foreach (var (line, text) in hits.Take(6)) Console.WriteLine($"      {line,5}: {text.Trim()}");
                if (hits.Count > 6) Console.WriteLine($"      … +{hits.Count - 6} more matching line(s)");
            }
            Console.WriteLine($"{matchedNodes} code node(s) with source matches (scanned {scanned}).");
        }
        else
        {
            bool Matches(GraphNode n) => rx.IsMatch(n.Id) || (n.Label is { } lb && rx.IsMatch(lb))
                                         || (n.Metadata is { } m && m.Values.Any(v => rx.IsMatch(v)));
            var hits = scope.Where(Matches).OrderBy(n => TypeRank(n.Type)).ThenBy(n => n.Id, StringComparer.Ordinal).ToList();
            foreach (var n in hits.Take(limit)) Console.WriteLine(NodeLine(n));
            Console.WriteLine($"{hits.Count} node(s) whose graph text matches /{pattern}/" + (hits.Count > limit ? $" — showing {limit}" : "") + ".");
        }
        return Clean;
    }

    // ── find / describe: the "where is feature X, and its code/tests/docs" index ──

    private static int Find(string[] args)
    {
        if (!TryRead(Specs.Find, args, out var a, out var root, out var parseCode)) return parseCode;
        var term = a[0];
        if (!TryLoad(root, out var state, out var code)) return code;

        var hits = ProductQuery.Find(state, term);
        if (a.Has("--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(hits, ProductJson.Options));
            return Clean;
        }
        Console.WriteLine(ProductReport.Find(hits, term));
        return Clean;
    }

    private static int Query(string[] args)
    {
        if (!TryRead(Specs.Query, args, out var a, out var root, out var parseCode)) return parseCode;

        var under   = a.Value("--under");
        var concern = a.Value("--concern");
        Status? status = null;
        if (a.Value("--status") is { } statusS)
        {
            if (!TryParseStatus(statusS, out var st)) return Usage($"unknown status '{statusS}' (should|done|shouldnt|faulted)");
            status = st;
        }
        if (a.Has("--leaf") && a.Has("--panel")) return Usage("--leaf and --panel are mutually exclusive");
        bool? leafOnly = a.Has("--leaf") ? true : a.Has("--panel") ? false : null;

        if (!TryLoad(root, out var state, out var code)) return code;

        if (under is { Length: > 0 } && !state.Nodes.ContainsKey(under))
            return Usage($"no node '{under}' (try: find).");

        var hits = ProductQuery.Query(state, under, concern, status, leafOnly);

        // --unbacked: the concern is carried but nothing evidences it. Needs a concern to be meaningful.
        if (a.Has("--unbacked"))
        {
            if (concern is null) return VerbUsage("--unbacked needs --concern <tag> (which concern is unbacked?)");
            hits = [.. hits.Where(h => h.ConcernSnaplinks == 0)];
        }

        if (a.Has("--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(hits, ProductJson.Options));
            return Clean;
        }
        Console.WriteLine(ProductReport.Query(hits, concern));
        return Clean;
    }

    private static int Describe(string[] args)
    {
        if (!TryRead(Specs.Describe, args, out var a, out var root, out var parseCode)) return parseCode;
        var id = a[0];
        if (!TryLoad(root, out var state, out var code)) return code;

        var d = ProductQuery.Describe(state, id);
        if (d is null) { Console.Error.WriteLine($"error: no node '{id}' (try: find)."); return Error; }

        if (a.Has("--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(d, ProductJson.Options));
            return Clean;
        }

        Console.WriteLine(ProductReport.Describe(d));

        if (a.Has("--code") && state.Nodes.TryGetValue(d.Id, out var raw))
        {
            // Resolve snaplink source from the CALLER's working tree first (so a worktree shows the code on
            // the branch you're editing, incl. files not yet in the main checkout), then the product root.
            var caller = WorkingTreeRootOf(Directory.GetCurrentDirectory());
            var fileRoots = new[] { caller, root }.Where(r => r is { Length: > 0 }).Distinct().ToArray()!;
            PrintResolvedCode(fileRoots!, raw);
        }
        return Clean;
    }

    private static int Tree(string[] args)
    {
        if (!TryRead(Specs.Tree, args, out var a, out var root, out var parseCode)) return parseCode;
        var id = a[0];

        int? maxDepth = null;
        if (a.Value("--depth") is { } depthS)
        {
            if (!int.TryParse(depthS, out var d) || d < 0)
                return Usage($"--depth must be a non-negative integer (got '{depthS}').");
            maxDepth = d;
        }

        if (!TryLoad(root, out var state, out var code)) return code;

        var rows = ProductQuery.Outline(state, id, maxDepth);
        if (rows is null) { Console.Error.WriteLine($"error: no node '{id}' (try: find)."); return Error; }

        if (a.Has("--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(rows, ProductJson.Options));
            return Clean;
        }

        Console.WriteLine(ProductReport.Outline(rows, a.Has("--full")));
        return Clean;
    }

    /// <summary>
    /// Where a snaplink's file is looked for, most-specific first: the caller's own working tree (so a linked
    /// worktree validates the code on the branch being edited, not the main checkout's copy), then the product
    /// root. In a normal checkout — including the installer's release gate — both are the same directory, so
    /// this changes nothing there.
    /// </summary>
    private static string[] FileRootsFor(string productRoot)
    {
        var caller = WorkingTreeRootOf(Directory.GetCurrentDirectory());
        return caller is { Length: > 0 } ? [caller, productRoot] : [productRoot];
    }

    /// <summary>The git working-tree root enclosing <paramref name="dir"/> — the directory that holds a <c>.git</c>
    /// entry (a linked worktree's own root, or the main checkout), or null if not in a repo.</summary>
    private static string? WorkingTreeRootOf(string dir)
    {
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, ".git")) || File.Exists(Path.Combine(d.FullName, ".git")))
                return d.FullName;
        return null;
    }

    /// <summary>
    /// The directory <c>graph build</c> reads source from: <c>--main</c> forces the product (main-checkout) copy;
    /// <c>--code-root &lt;path&gt;</c> overrides explicitly; otherwise the caller's own working tree when it differs
    /// from the product root — so building from a linked worktree graphs the branch you're on. Null means "the
    /// product root", which is also the case for a normal checkout (the two are the same directory), so CI and the
    /// release gate are unaffected.
    /// </summary>
    private static string? GraphCodeRoot(string productRoot, VerbArgs a)
    {
        if (a.Has("--main")) return null;
        if (a.Value("--code-root") is { Length: > 0 } explicitRoot) return Path.GetFullPath(explicitRoot);
        var caller = WorkingTreeRootOf(Directory.GetCurrentDirectory());
        return caller is { Length: > 0 } && !PathsEqual(caller, productRoot) ? caller : null;
    }

    // ── describe --code: resolve every code snaplink to the actual source, so "what backs this node" is
    //    one call. A link that no longer resolves prints WHY — a targeted validate over just this node. ──

    private static void PrintResolvedCode(string[] fileRoots, ProductNode node)
    {
        var links = new List<(string? Concern, Snaplink Link)>();
        foreach (var l in node.Snaplinks ?? []) links.Add((null, l));
        foreach (var c in node.Concerns ?? []) foreach (var l in c.Snaplinks ?? []) links.Add((c.Tag, l));
        var code = links.Where(x => x.Link.Type == "code").ToList();

        Console.WriteLine();
        if (code.Count == 0) { Console.WriteLine("  --code: no code snaplinks on this node."); return; }

        foreach (var (concern, link) in code)
        {
            var member = link.Class is { Length: > 0 } cls
                ? $"  {cls}{(link.Method is { Length: > 0 } m ? "." + m : "")}"
                : "";
            Console.WriteLine($"  --- [{concern ?? "node"}] {link.Doc}{member} ---");
            var (ok, header, body) = ResolveBlock(fileRoots, link);
            Console.WriteLine(ok ? $"  {header}" : $"  !! {header}");
            foreach (var line in body) Console.WriteLine(line);
        }
    }

    /// <summary>Resolves one code snaplink to its actual source block (header + numbered lines), or a reason it
    /// no longer resolves. The file is taken from the first of <paramref name="fileRoots"/> that has it (caller's
    /// working tree first). Class/method are located via the same tree-sitter outline the validator checks
    /// against, so "resolves here" and "passes validate" agree.</summary>
    private static (bool Ok, string Header, IReadOnlyList<string> Body) ResolveBlock(string[] fileRoots, Snaplink link)
    {
        if (string.IsNullOrWhiteSpace(link.Doc)) return (false, "code snaplink has no doc path", []);
        var full = Path.IsPathRooted(link.Doc) ? link.Doc
            : fileRoots.Select(r => Path.Combine(r, link.Doc)).FirstOrDefault(File.Exists)
              ?? Path.Combine(fileRoots.FirstOrDefault() ?? ".", link.Doc);
        if (!File.Exists(full)) return (false, $"file not found: {link.Doc}", []);
        var text = SnaplinkTargets.ReadText(full);
        if (text is null) return (false, $"unreadable or too large: {link.Doc}", []);
        var lines = text.Replace("\r\n", "\n").Split('\n');

        int s0, e0;
        if (!string.IsNullOrWhiteSpace(link.Class) || !string.IsNullOrWhiteSpace(link.Method))
        {
            var outline = SnaplinkTargets.Outline(full, text, Path.GetDirectoryName(full));
            if (outline is not { HasContent: true }) return (false, $"no tree-sitter structure for {link.Doc} (unverifiable)", []);
            var line = ResolveMemberLine(outline, link.Class, link.Method);
            if (line is null)
            {
                var what = link.Class is { Length: > 0 } c ? $"{c}{(link.Method is { Length: > 0 } mm ? "." + mm : "")}" : link.Method;
                return (false, $"'{what}' not found in {link.Doc}", []);
            }
            s0 = line.Value - 1;
            e0 = BlockEnd(lines, s0, 400);
        }
        else   // whole-file link — preview only; the member-scoped blocks are the point of --code
        {
            s0 = 0;
            e0 = Math.Min(lines.Length - 1, WholeFilePreview - 1);
        }

        var body = new List<string>();
        for (var i = Math.Max(0, s0); i <= e0 && i < lines.Length; i++) body.Add($"  {i + 1,5}  {lines[i]}");
        var more = lines.Length - 1 - e0;
        var header = link.Class is null && link.Method is null && more > 0
            ? $"{link.Doc}:1-{e0 + 1}  (whole file — +{more} more lines: graph cat file:{link.Doc})"
            : $"{link.Doc}:{s0 + 1}-{e0 + 1}";
        return (true, header, body);
    }

    /// <summary>The declaration line of <paramref name="cls"/>.<paramref name="method"/> in an outline (the class
    /// line when no method; the top-level function when no class), or null if it isn't declared.</summary>
    private static int? ResolveMemberLine(CodeOutline outline, string? cls, string? method)
    {
        if (!string.IsNullOrWhiteSpace(cls))
        {
            var type = outline.Types.FirstOrDefault(t => t.Name == cls);
            if (type is null) return null;
            if (string.IsNullOrWhiteSpace(method)) return type.Line;
            return outline.Types.Where(t => t.Name == cls)
                .SelectMany(t => t.Members).FirstOrDefault(mem => mem.Name == method)?.Line;
        }
        return outline.TopLevel.FirstOrDefault(mem => mem.Name == method)?.Line;
    }

    // ── diff: what changed in the tree since the last committed release snapshot (docs/product/<ver>.json) ──

    private static int Diff(string[] args)
    {
        if (!TryRead(Specs.Diff, args, out var a, out var root, out var parseCode)) return parseCode;
        if (!TryLoad(root, out var state, out var code)) return code;

        var exportDir = Path.Combine(root, state.Product.ExportDir);
        if (!Directory.Exists(exportDir)) { Console.Error.WriteLine($"error: no export dir {state.Product.ExportDir} (nothing to diff against)."); return Error; }

        var want = a.Value("--from");
        var snapshots = Directory.GetFiles(exportDir, "*.json").OrderBy(p => p, StringComparer.Ordinal).ToList();
        var file = want is not null
            ? snapshots.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).Contains(want, StringComparison.OrdinalIgnoreCase))
            : snapshots.LastOrDefault();
        if (file is null) { Console.Error.WriteLine($"error: no release snapshot{(want is null ? "" : $" matching '{want}'")} under {state.Product.ExportDir}."); return Error; }

        ProductSnapshot? snap;
        try { snap = JsonSerializer.Deserialize<ProductSnapshot>(File.ReadAllText(file), ProductJson.Options); }
        catch (Exception ex) { Console.Error.WriteLine($"error: can't read {Path.GetFileName(file)}: {ex.Message}"); return Error; }
        if (snap is null) { Console.Error.WriteLine($"error: empty snapshot {Path.GetFileName(file)}."); return Error; }

        var old = snap.Nodes;
        var cur = state.Nodes;
        static string S(Status s) => s.ToString().ToLowerInvariant();

        Console.WriteLine($"diff: {snap.Version} ({snap.Date}) -> current tree   [{cur.Count} nodes vs {old.Count}]");

        var added   = cur.Keys.Where(k => !old.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var removed = old.Keys.Where(k => !cur.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();

        if (added.Count > 0)
        {
            Console.WriteLine($"\nADDED since {snap.Version} ({added.Count}):");
            foreach (var k in added) Console.WriteLine($"  + {k}  [{S(cur[k].Status)}]  {cur[k].Title}");
        }
        if (removed.Count > 0)
        {
            Console.WriteLine($"\nREMOVED since {snap.Version} ({removed.Count}):");
            foreach (var k in removed) Console.WriteLine($"  - {k}  {old[k].Title}");
        }

        var statusChanges = new List<string>();
        var concernChanges = new List<string>();
        foreach (var k in cur.Keys.Where(old.ContainsKey).OrderBy(k => k, StringComparer.Ordinal))
        {
            var (o, n) = (old[k], cur[k]);
            if (o.Status != n.Status) statusChanges.Add($"  ~ {k}: {S(o.Status)} -> {S(n.Status)}");

            var oc = (o.Concerns ?? []).ToDictionary(c => c.Tag, c => c.Status);
            var nc = (n.Concerns ?? []).ToDictionary(c => c.Tag, c => c.Status);
            foreach (var tag in oc.Keys.Union(nc.Keys).OrderBy(t => t, StringComparer.Ordinal))
            {
                var had = oc.TryGetValue(tag, out var os);
                var has = nc.TryGetValue(tag, out var ns);
                if (had && has && os != ns) concernChanges.Add($"  ~ {k} [{tag}]: {S(os)} -> {S(ns)}");
                else if (!had && has)       concernChanges.Add($"  + {k} [{tag}]: {S(ns)} (new concern)");
                else if (had && !has)       concernChanges.Add($"  - {k} [{tag}]: was {S(os)} (concern removed)");
            }
        }

        if (statusChanges.Count > 0)  { Console.WriteLine($"\nNODE STATUS CHANGES ({statusChanges.Count}):");  statusChanges.ForEach(Console.WriteLine); }
        if (concernChanges.Count > 0) { Console.WriteLine($"\nCONCERN CHANGES ({concernChanges.Count}):"); concernChanges.ForEach(Console.WriteLine); }

        if (added.Count + removed.Count + statusChanges.Count + concernChanges.Count == 0)
            Console.WriteLine("  (identical — the tree matches the release snapshot.)");
        return Clean;
    }

    // ── remap: follow a rename/move without hand-editing tree.json ──

    private static int Remap(string[] args)
    {
        if (!TryRead(Specs.Remap, args, out var a, out var root, out var parseCode)) return parseCode;
        var oldPath = a[0];
        var newPath = a[1];
        if (!TryLoad(root, out var state, out var code)) return code;

        var changed = SnaplinkRemapper.Remap(state, oldPath, newPath, a.Value("--class"), a.Value("--method"));
        if (changed == 0)
        {
            Console.WriteLine($"No snaplink referenced '{oldPath}' - nothing remapped.");
            return Clean;
        }

        new ProductStore(root).SaveTree(state.Nodes);   // canonical serializer, same as the in-app editor

        // Re-validate so the effect is visible immediately (this is the point of a safe edit command).
        var report = SnaplinkValidator.Validate(state, root, FileRootsFor(root));
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
        if (!TryRead(Specs.ScanTests, args, out var a, out var root, out var parseCode)) return parseCode;
        if (!Directory.Exists(root)) return Usage($"no such directory: {root}");
        if (!ProductStore.Exists(root)) { Console.Error.WriteLine($"error: no .product/ under {root}."); return Error; }

        if (a.Has("--suggest-attributes")) return SuggestAttributes(root);

        var explicitDlls = a.All("--test-dll");
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

    // ── add-node: grow the tree finer (a sub-node under an existing node) without hand-editing tree.json ──

    private static int AddNode(string[] args) => RunOne(Specs.AddNode, args, ApplyAddNode);

    private static (bool Ok, string Message) ApplyAddNode(ProductState state, VerbArgs a)
    {
        var parentId = a[0];
        var title    = a[1];
        if (!state.Nodes.TryGetValue(parentId, out var parent)) return (false, $"no parent node '{parentId}'");

        string id;
        if (a.Value("--id") is { Length: > 0 } explicitId)
        {
            id = Slug(explicitId);
            if (state.Nodes.ContainsKey(id)) return (false, $"node id '{id}' already exists");
        }
        else id = UniqueId(state, Slug(title));

        var defaults = state.Product.Concerns.Where(c => c.IsDefault).Select(c => c.Name).ToList();
        state.Nodes[id] = new ProductNode
        {
            Title       = title,
            Description = a.Value("--desc"),
            Status      = ParseStatus(a.Value("--status")),
            Parent      = parentId,
            Children    = [],
            Concerns    = defaults.Count > 0 ? [.. defaults.Select(n => new ConcernLink { Tag = n, Status = Status.Should })] : null
        };
        parent.Children.Add(id);
        return (true, $"Added node '{id}' under '{parentId}': {title}");
    }

    // ── move: reparent a node (and its subtree) — the safe way to restructure without hand-editing tree.json ──

    private static int Rename(string[] args) => RunOne(Specs.Rename, args, ApplyRename);

    private static (bool Ok, string Message) ApplyRename(ProductState state, VerbArgs a)
    {
        var (oldId, newId) = (a[0], a[1]);

        return ProductTreeOps.Rename(state, oldId, newId) switch
        {
            ProductTreeOps.RenameError.NoSuchNode => (false, $"no node '{oldId}' (try: find)"),
            ProductTreeOps.RenameError.IdTaken    => (false, $"'{newId}' is already a node id — ids are one flat namespace"),
            ProductTreeOps.RenameError.IdInvalid  => (false, $"'{newId}' is not a usable id (non-empty, no whitespace, and different from '{oldId}')"),
            // A [CoversNode] naming the old id lives in test source the tree can't reach — say so rather than
            // leave the caller to discover it as an NXCOV002 build warning.
            _ => (true, $"Renamed '{oldId}' → '{newId}' (retargeted parent, children and node snaplinks). "
                      + $"Any [CoversNode(\"{oldId}\")] in test source still needs updating."),
        };
    }

    private static int Move(string[] args) => RunOne(Specs.Move, args, ApplyMove);

    private static (bool Ok, string Message) ApplyMove(ProductState state, VerbArgs a)
    {
        var id = a[0]; var newParentId = a[1];
        if (!state.Nodes.TryGetValue(id, out var node)) return (false, $"no node '{id}'");
        if (!state.Nodes.ContainsKey(newParentId)) return (false, $"no parent node '{newParentId}'");
        if (id == newParentId) return (false, "a node can't be its own parent");
        if (ProductTreeOps.IsAncestorOrSelf(state, id, newParentId))
            return (false, $"'{newParentId}' is inside '{id}' — moving there would make a cycle");
        if (node.Parent == newParentId) return (true, $"'{id}' is already under '{newParentId}'");

        ProductTreeOps.Reparent(state, id, newParentId);
        return (true, $"Moved '{id}' under '{newParentId}'");
    }

    // ── remove: delete a node (a leaf, or a whole subtree with --recursive) ──

    private static int Remove(string[] args) => RunOne(Specs.Remove, args, ApplyRemove);

    private static (bool Ok, string Message) ApplyRemove(ProductState state, VerbArgs a)
    {
        var id = a[0];
        if (!state.Nodes.TryGetValue(id, out var node)) return (false, $"no node '{id}'");
        var recursive = a.Has("--recursive");
        if (node.Children.Count > 0 && !recursive)
            return (false, $"'{id}' has {node.Children.Count} child node(s) — remove them first, or pass --recursive to delete the subtree");

        var removed = ProductTreeOps.Remove(state, id, recursive) ?? [];
        return (true, $"Removed '{id}'" + (removed.Count > 1 ? $" + {removed.Count - 1} descendant(s)" : ""));
    }

    // ── Verb specs ──────────────────────────────────────────────────────────────────────────────
    // What each verb accepts, declared once and used for parsing AND its error text. Anything not listed
    // here is rejected rather than ignored — see VerbArgs.

    private static class Specs
    {
        private static readonly string[] None = [];

        public static readonly VerbSpec Validate = new("validate", 0, None, ["--json", "--save"],
            "validate [<root>] [--json] [--save]");
        public static readonly VerbSpec Find = new("find", 1, None, ["--json"],
            "find <term> [<root>] [--json]");
        public static readonly VerbSpec Query = new("query", 0, ["--under", "--concern", "--status"],
            ["--leaf", "--panel", "--unbacked", "--json"],
            "query [<root>] [--under <id>] [--concern <tag>] [--status <s>] [--leaf|--panel] [--unbacked] [--json]");
        public static readonly VerbSpec Describe = new("describe", 1, None, ["--json", "--code"],
            "describe <node-id> [<root>] [--json] [--code]");
        public static readonly VerbSpec Tree = new("tree", 1, ["--depth"], ["--full", "--json"],
            "tree <node-id> [<root>] [--depth <n>] [--full] [--json]");
        public static readonly VerbSpec Diff = new("diff", 0, ["--from"], None,
            "diff [<root>] [--from <version>]");
        public static readonly VerbSpec Remap = new("remap", 2, ["--class", "--method"], None,
            "remap <old-path> <new-path> [<root>] [--class <name>] [--method <name>]");
        public static readonly VerbSpec ScanTests = new("scan-tests", 0, ["--test-dll"], ["--suggest-attributes"],
            "scan-tests [<root>] [--test-dll <path>]... [--suggest-attributes]");
        public static readonly VerbSpec Batch = new("batch", 1, None, ["--dry-run"],
            "batch <script-file> [<root>] [--dry-run]");
        public static readonly VerbSpec Doctor = new("doctor", 0, None, ["--fix"],
            "doctor [<root>] [--fix]");

        // Mutations — these are the ones batch also runs, so their specs are reused via .InBatch.
        public static readonly VerbSpec SetStatus = new("set-status", 2, None, None,
            "set-status <node-id> <status> [<root>]   (status: should|done|shouldnt|faulted)");
        public static readonly VerbSpec SetConcern = new("set-concern", 3, None, None,
            "set-concern <node-id> <tag> <status> [<root>]   (a note goes on the node: set-node <id> --note ...)");
        public static readonly VerbSpec RemoveConcern = new("remove-concern", 2, None, None,
            "remove-concern <node-id> <tag> [<root>]");
        public static readonly VerbSpec AddSnaplink = new("add-snaplink", 1,
            ["--type", "--concern", "--doc", "--class", "--method", "--ast", "--target", "--url", "--title-path", "--status"],
            None,
            "add-snaplink <node-id> --type <code|markdown|node|url> [<root>] [--concern <tag>] "
            + "[--doc <p>] [--class <c>] [--method <m>] [--target <id>] [--url <u>] [--title-path a>b] [--status <s>]");
        public static readonly VerbSpec RemoveSnaplink = new("remove-snaplink", 1, ["--concern", "--index"], None,
            "remove-snaplink <node-id> [<root>] [--concern <tag>] [--index <n>]");
        public static readonly VerbSpec SetNode = new("set-node", 1, ["--title", "--desc", "--note"], None,
            "set-node <node-id> [<root>] [--title <t>] [--desc <d>] [--note <n>]");
        public static readonly VerbSpec AddNode = new("add-node", 2, ["--id", "--desc", "--status"], None,
            "add-node <parent-id> <title> [<root>] [--id <slug>] [--desc <text>] [--status <s>]");
        public static readonly VerbSpec Move = new("move", 2, None, None,
            "move <node-id> <new-parent-id> [<root>]");
        public static readonly VerbSpec Rename = new("rename", 2, None, None,
            "rename <old-id> <new-id> [<root>]");
        public static readonly VerbSpec Remove = new("remove", 1, None, ["--recursive"],
            "remove <node-id> [<root>] [--recursive]");

        public static readonly VerbSpec Lint = new("lint", 0, ["--under"], ["--json"],
            "lint [<root>] [--under <id>] [--json]");

        // graph subcommands — same strictness as everything else.
        public static readonly VerbSpec GraphBuild = new("graph", 0, ["--code-root"],
            ["--json", "--product-anchored", "--no-incremental", "--main"],
            "graph [<root>] [--no-incremental] [--product-anchored] [--main | --code-root <dir>] [--json]");
        public static readonly VerbSpec GraphStats = new("graph stats", 0, None, None, "graph stats [<root>]");
        public static readonly VerbSpec GraphSearch = new("graph search", 1, ["--type", "--limit"], None,
            "graph search <term> [<root>] [--type <t>] [--limit N]");
        public static readonly VerbSpec GraphList = new("graph list", 0,
            ["--type", "--community", "--file", "--limit"], None,
            "graph list [<root>] [--type <t>] [--community N] [--file <f>] [--limit N]");
        public static readonly VerbSpec GraphNode = new("graph node", 1, ["--limit"], None,
            "graph node <id> [<root>] [--limit N]");
        public static readonly VerbSpec GraphWalk = new("graph walk", 1, ["--hops", "--limit", "--types"], None,
            "graph walk <id> [<root>] [--hops N] [--types a,b] [--limit N]");
        public static readonly VerbSpec GraphContext = new("graph context", 1, ["--lines", "--limit"], ["--main"],
            "graph context <id> [<root>] [--lines N] [--limit N] [--main]");
        public static readonly VerbSpec GraphGrep = new("graph grep", 1,
            ["--from", "--hops", "--mode", "--type", "--limit"], ["--main"],
            "graph grep <pattern> [<root>] [--from <id>] [--hops N] [--mode index|content] [--type t] [--limit N] [--main]");
        public static readonly VerbSpec GraphCode = new("graph code", 1, ["--lines"], ["--main"],
            "graph code <id> [<root>] [--lines A-B] [--main]");
    }

    /// <summary>
    /// A positive-integer option, or <paramref name="dflt"/> when absent. A non-numeric or non-positive value
    /// is an <em>error</em>: silently falling back to the default (what the old <c>OptInt</c> did) hides a typo
    /// behind plausible-looking output.
    /// </summary>
    private static bool TryIntOpt(VerbArgs a, string flag, int dflt, out int value)
    {
        value = dflt;
        if (a.Value(flag) is not { } raw) return true;
        if (int.TryParse(raw, out var n) && n > 0) { value = n; return true; }
        VerbUsage($"{flag} must be a positive integer (got '{raw}')");
        return false;
    }

    /// <summary>Reports a verb's argument error. Unlike <see cref="Usage(string?)"/> this prints only the
    /// problem and that verb's own usage line — the full command list would bury the one thing that's wrong.</summary>
    private static int VerbUsage(string error)
    {
        Console.Error.WriteLine($"error: {error}");
        Console.Error.WriteLine("  (run with no arguments for the full command list)");
        return Error;
    }

    /// <summary>Parses a read-only verb's arguments and resolves its <c>&lt;root&gt;</c>, or reports the
    /// argument error and yields the exit code.</summary>
    private static bool TryRead(VerbSpec spec, string[] args, out VerbArgs parsed, out string root, out int code)
    {
        root = string.Empty;
        if (!VerbArgs.TryParse(spec, args, out parsed, out var error))
        {
            code = VerbUsage(error);
            return false;
        }
        root = ResolveProductRoot(parsed.Root ?? ".");
        code = Clean;
        return true;
    }

    // ── typed tree mutations (set-status / set-concern / add-snaplink / set-node / doctor) ──
    // All go through the typed model + ProductStore.SaveTree (the canonical serializer), so a hand-edit's
    // structural hazards (a stray string concat in children[], a malformed concern) simply can't happen.

    private static bool TryParseStatus(string? s, out Status status)
    {
        (status, var ok) = s?.Trim().ToLowerInvariant() switch
        {
            "should"   => (Status.Should, true),
            "done"     => (Status.Done, true),
            "shouldnt" => (Status.Shouldnt, true),
            "faulted"  => (Status.Faulted, true),
            _          => (Status.Should, false),
        };
        return ok;
    }

    /// <summary>Persist the mutated tree via the canonical serializer, re-validate, and print the outcome —
    /// the same "edit then show the effect" contract as remap/add-node.</summary>
    private static int SaveAndValidate(ProductState state, string root, string message)
    {
        var store = new ProductStore(root);
        store.SaveTree(state.Nodes);
        var report = SnaplinkValidator.Validate(state, root, FileRootsFor(root));
        store.SaveIntegrity(report);
        Console.WriteLine(message);
        Console.WriteLine(report.IsClean
            ? $"Snaplinks OK — scanned {report.ScannedSnaplinks}."
            : $"{report.IssueCount} broken snaplink(s) — run: validate .");
        return report.IsClean ? Clean : Broken;
    }

    /// <summary>The standalone-verb path: parse against the verb's spec, load, apply one mutation, then save
    /// + re-validate. Parsing happens before the tree is even loaded, so a bad command line never touches it.</summary>
    private static int RunOne(VerbSpec spec, string[] args, Func<ProductState, VerbArgs, (bool Ok, string Message)> apply)
    {
        if (!VerbArgs.TryParse(spec, args, out var parsed, out var parseError)) return VerbUsage(parseError);
        var root = ResolveProductRoot(parsed.Root ?? ".");
        if (!TryLoad(root, out var state, out var code)) return code;
        var (ok, msg) = apply(state, parsed);
        if (!ok) { Console.Error.WriteLine($"error: {msg}"); return Error; }
        return SaveAndValidate(state, root, msg);
    }

    private static int SetStatus(string[] args) => RunOne(Specs.SetStatus, args, ApplySetStatus);

    private static (bool Ok, string Message) ApplySetStatus(ProductState s, VerbArgs a)
    {
        if (!TryParseStatus(a[1], out var status)) return (false, $"unknown status '{a[1]}' (should|done|shouldnt|faulted)");
        if (!s.Nodes.ContainsKey(a[0])) return (false, $"no node '{a[0]}' (try: find)");
        ProductTreeOps.CascadeStatus(s, a[0], status);
        return (true, $"Set '{a[0]}' → {status.ToString().ToLowerInvariant()} (cascaded into should-only descendants + concerns).");
    }

    private static int SetConcern(string[] args) => RunOne(Specs.SetConcern, args, ApplySetConcern);

    private static (bool Ok, string Message) ApplySetConcern(ProductState s, VerbArgs a)
    {
        if (!TryParseStatus(a[2], out var status)) return (false, $"unknown status '{a[2]}' (should|done|shouldnt|faulted)");
        if (!s.Nodes.ContainsKey(a[0])) return (false, $"no node '{a[0]}' (try: find)");
        if (!s.Product.Concerns.Any(c => c.Name == a[1]))
            return (false, $"'{a[1]}' is not a concern (valid: {string.Join(", ", s.Product.Concerns.Select(c => c.Name))})");
        ProductTreeOps.SetConcern(s, a[0], a[1], status);
        return (true, $"Set concern '{a[1]}' on '{a[0]}' → {status.ToString().ToLowerInvariant()}.");
    }

    private static int RemoveConcern(string[] args) => RunOne(Specs.RemoveConcern, args, ApplyRemoveConcern);

    private static (bool Ok, string Message) ApplyRemoveConcern(ProductState s, VerbArgs a)
    {
        if (!s.Nodes.ContainsKey(a[0])) return (false, $"no node '{a[0]}' (try: find)");
        return ProductTreeOps.RemoveConcern(s, a[0], a[1])
            ? (true, $"Removed concern '{a[1]}' from '{a[0]}'.")
            : (false, $"'{a[0]}' has no '{a[1]}' concern to remove");
    }

    private static int RemoveSnaplink(string[] args) => RunOne(Specs.RemoveSnaplink, args, ApplyRemoveSnaplink);

    private static (bool Ok, string Message) ApplyRemoveSnaplink(ProductState s, VerbArgs a)
    {
        var id = a[0];
        if (!s.Nodes.ContainsKey(id)) return (false, $"no node '{id}' (try: find)");

        int? index = null;
        if (a.Value("--index") is { } raw)
        {
            if (!int.TryParse(raw, out var i)) return (false, $"--index must be a number (got '{raw}')");
            index = i;
        }
        var concern = a.Value("--concern");

        var removed = ProductTreeOps.RemoveSnaplink(s, id, concern, index);
        var where = concern is null ? $"'{id}'" : $"'{id}' concern '{concern}'";
        return removed > 0
            ? (true, $"Removed {removed} snaplink(s) from {where}.")
            : (false, $"no matching snaplink to remove on {where}");
    }

    private static int AddSnaplink(string[] args) => RunOne(Specs.AddSnaplink, args, ApplyAddSnaplink);

    private static (bool Ok, string Message) ApplyAddSnaplink(ProductState s, VerbArgs a)
    {
        var id = a[0];
        if (!s.Nodes.ContainsKey(id)) return (false, $"no node '{id}' (try: find)");

        var type = a.Value("--type") ?? "code";
        var link = new Snaplink { Type = type };
        if (a.Value("--status") is { } st)
        {
            if (!TryParseStatus(st, out var sv)) return (false, $"unknown --status '{st}'");
            link.Status = sv;
        }
        switch (type)
        {
            case "code":
                link.Doc = a.Value("--doc"); link.Class = a.Value("--class");
                link.Method = a.Value("--method"); link.Ast = a.Value("--ast");
                if (string.IsNullOrWhiteSpace(link.Doc)) return (false, "code snaplink needs --doc <file> [--class <c>] [--method <m>]");
                break;
            case "markdown":
                link.Doc = a.Value("--doc");
                if (a.Value("--title-path") is { Length: > 0 } tp)
                    link.TitlePath = [.. tp.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
                if (string.IsNullOrWhiteSpace(link.Doc)) return (false, "markdown snaplink needs --doc <file> [--title-path a>b]");
                break;
            case "node":
                link.Target = a.Value("--target");
                if (string.IsNullOrWhiteSpace(link.Target)) return (false, "node snaplink needs --target <node-id>");
                break;
            case "url":
                link.Target = a.Value("--url") ?? a.Value("--target");
                if (string.IsNullOrWhiteSpace(link.Target)) return (false, "url snaplink needs --url <url>");
                break;
            default:
                return (false, $"unknown snaplink --type '{type}' (code|markdown|node|url)");
        }

        var concern = a.Value("--concern");
        if (!ProductTreeOps.AddSnaplink(s, id, link, concern))
            return (false, $"node '{id}' has no concern '{concern}' — add it first: set-concern {id} {concern} should");
        var where = concern is null ? "the node" : $"concern '{concern}'";
        return (true, $"Added {type} snaplink to {where} of '{id}': {link.Display}.");
    }

    private static int SetNode(string[] args) => RunOne(Specs.SetNode, args, ApplySetNode);

    private static (bool Ok, string Message) ApplySetNode(ProductState s, VerbArgs a)
    {
        var id = a[0];
        if (!s.Nodes.ContainsKey(id)) return (false, $"no node '{id}' (try: find)");
        if (a.Value("--title") is null && a.Value("--desc") is null && a.Value("--note") is null)
            return (false, "set-node needs at least one of --title / --desc / --note");
        ProductTreeOps.EditNode(s, id, a.Value("--title"), a.Value("--desc"), a.Value("--note"));
        return (true, $"Edited '{id}'.");
    }

    // ── batch: apply a whole script of instructions in one load/save/validate (transactional) ──

    /// <summary>
    /// Dispatches one instruction line (verb + its args, no <c>&lt;root&gt;</c>) to its Apply* core, parsing
    /// it against the same <see cref="VerbSpec"/> the standalone verb uses — so a batch script gets identical
    /// strictness. Since batch is all-or-nothing, an unknown option on line 40 aborts before anything is written.
    /// </summary>
    private static (bool Ok, string Message) ApplyOne(ProductState state, string[] args)
    {
        // Parse against the BATCH form of the spec (no trailing <root> — the run has one), then run the
        // same core the standalone verb uses.
        (bool Ok, string Message) Parsed(
            VerbSpec spec, string[] rest, Func<ProductState, VerbArgs, (bool, string)> apply) =>
            VerbArgs.TryParse(spec.InBatch, rest, out var parsed, out var error)
                ? apply(state, parsed)
                : (false, error);

        return args switch
        {
            [] => (false, "empty instruction"),
            ["set-status",      .. var r] => Parsed(Specs.SetStatus,      r, ApplySetStatus),
            ["set-concern",     .. var r] => Parsed(Specs.SetConcern,     r, ApplySetConcern),
            ["remove-concern",  .. var r] => Parsed(Specs.RemoveConcern,  r, ApplyRemoveConcern),
            ["add-snaplink",    .. var r] => Parsed(Specs.AddSnaplink,    r, ApplyAddSnaplink),
            ["remove-snaplink", .. var r] => Parsed(Specs.RemoveSnaplink, r, ApplyRemoveSnaplink),
            ["set-node",        .. var r] => Parsed(Specs.SetNode,        r, ApplySetNode),
            ["add-node",        .. var r] => Parsed(Specs.AddNode,        r, ApplyAddNode),
            ["move",            .. var r] => Parsed(Specs.Move,           r, ApplyMove),
            ["rename",          .. var r] => Parsed(Specs.Rename,         r, ApplyRename),
            ["remove",          .. var r] => Parsed(Specs.Remove,         r, ApplyRemove),
            [var verb, ..] => (false, $"unknown instruction '{verb}' (batch supports: set-status, set-concern, remove-concern, add-snaplink, remove-snaplink, set-node, add-node, move, rename, remove)"),
        };
    }

    private static int Batch(string[] args)
    {
        if (!TryRead(Specs.Batch, args, out var a, out var root, out var parseCode)) return parseCode;
        var dryRun = a.Has("--dry-run");
        var file = a[0];
        if (!File.Exists(file)) return Usage($"no such script file: {file}");
        if (!TryLoad(root, out var state, out var code)) return code;

        var lines = File.ReadAllLines(file);
        var applied = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;   // blank lines + # comments
            var (ok, msg) = ApplyOne(state, [.. Tokenize(line)]);
            if (!ok)
            {
                Console.Error.WriteLine($"error: line {i + 1}: {msg}");
                Console.Error.WriteLine($"  >> {line}");
                Console.Error.WriteLine($"Aborted — {applied.Count} earlier instruction(s) parsed but NOTHING was written (batch is all-or-nothing).");
                return Error;
            }
            applied.Add(msg);
        }

        if (applied.Count == 0) { Console.WriteLine($"No instructions in {Path.GetFileName(file)} (blank/comments only)."); return Clean; }
        foreach (var m in applied) Console.WriteLine($"  ✓ {m}");
        if (dryRun)
        {
            Console.WriteLine($"Dry run — {applied.Count} instruction(s) valid, nothing written. Drop --dry-run to apply.");
            return Clean;
        }
        return SaveAndValidate(state, root, $"Applied {applied.Count} instruction(s) from {Path.GetFileName(file)}.");
    }

    /// <summary>
    /// Shell-lite tokenizer: splits on whitespace, with double-quotes grouping a value that contains spaces
    /// (e.g. <c>--desc "two words"</c>). No escape handling — tree text rarely needs it.
    /// <para>
    /// An explicitly quoted <c>""</c> yields an <b>empty</b> token rather than none, so a batch line can
    /// clear a field the way the standalone verb documents (<c>set-node &lt;id&gt; --note ""</c>). Without
    /// that the quotes collapsed to nothing and the option looked like it was missing its value.
    /// </para>
    /// </summary>
    internal static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var sb = new System.Text.StringBuilder();
        var inQuote = false;
        var quoted = false;   // this token carried quotes — emit it even if it ends up empty
        foreach (var ch in line)
        {
            if (ch == '"') { inQuote = !inQuote; quoted = true; continue; }
            if (char.IsWhiteSpace(ch) && !inQuote)
            {
                if (sb.Length > 0 || quoted) { tokens.Add(sb.ToString()); sb.Clear(); quoted = false; }
            }
            else sb.Append(ch);
        }
        if (sb.Length > 0 || quoted) tokens.Add(sb.ToString());
        return tokens;
    }

    // ── lint: does this feature follow the modelling rules? (advisory — see StructureLinter) ──

    private static int Lint(string[] args)
    {
        if (!TryRead(Specs.Lint, args, out var a, out var root, out var parseCode)) return parseCode;
        if (!TryLoad(root, out var state, out var code)) return code;

        var under = a.Value("--under");
        if (under is { Length: > 0 } && !state.Nodes.ContainsKey(under)) return VerbUsage($"no node '{under}' (try: find)");

        var findings = StructureLinter.Lint(state, under);
        if (a.Has("--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(findings, ProductJson.Options));
            return Clean;
        }

        Console.WriteLine(ProductReport.Lint(findings, under));
        // Advisory by design: exit 0 so this can be run freely without tripping a script's error handling.
        return Clean;
    }

    private static int Doctor(string[] args)
    {
        if (!TryRead(Specs.Doctor, args, out var a, out var root, out var parseCode)) return parseCode;
        var fix = a.Has("--fix");
        if (!TryLoad(root, out var state, out var code)) return code;

        var repairs = ProductTreeOps.RepairChildren(state, apply: fix);
        var missingParent = state.Nodes
            .Where(kv => kv.Value.Parent is { } p && !state.Nodes.ContainsKey(p))
            .Select(kv => (Node: kv.Key, Parent: kv.Value.Parent!))
            .ToList();

        // Snaplink docs that go through a linked worktree instead of the repo's own copy. Detected always,
        // written back only under --fix, so a bare `doctor` stays a read-only diagnosis.
        var worktreeLinks = SnaplinkRemapper.NormalizeWorktreePaths(state, root);

        if (repairs.Count == 0 && missingParent.Count == 0 && worktreeLinks.Count == 0)
        {
            Console.WriteLine("Tree structure OK — every child id resolves, every node is listed by its parent, "
                            + "and no snaplink points into a linked worktree.");
            return Clean;
        }

        foreach (var r in repairs)
        {
            Console.WriteLine($"  {r.Parent}");
            Console.WriteLine($"      before: [{string.Join(", ", r.Before)}]");
            Console.WriteLine($"      after:  [{string.Join(", ", r.After)}]");
            if (r.Dropped.Count > 0) Console.WriteLine($"      dropped (unrecoverable): {string.Join(", ", r.Dropped)}");
        }
        foreach (var (node, parent) in missingParent)
            Console.Error.WriteLine($"  {node}: parent '{parent}' does not exist — re-parent it in-app (structural, not a children[] issue).");

        if (worktreeLinks.Count > 0)
        {
            Console.WriteLine($"  {worktreeLinks.Count} snaplink(s) point into a linked git worktree:");
            foreach (var (before, after) in worktreeLinks.DistinctBy(c => c.Before).OrderBy(c => c.Before, StringComparer.Ordinal))
                Console.WriteLine($"      {before}\n        -> {after}");
        }

        if (!fix)
        {
            var needs = new List<string>();
            if (repairs.Count > 0)        needs.Add($"{repairs.Count} parent(s)");
            if (worktreeLinks.Count > 0)  needs.Add($"{worktreeLinks.Count} worktree snaplink(s)");
            if (needs.Count > 0) Console.Error.WriteLine($"{string.Join(" and ", needs)} need repair — re-run with --fix.");
            return needs.Count > 0 ? Broken : Clean;
        }

        var store = new ProductStore(root);
        store.SaveTree(state.Nodes);
        var report = SnaplinkValidator.Validate(state, root, FileRootsFor(root));
        store.SaveIntegrity(report);
        Console.WriteLine($"Repaired {repairs.Count} parent(s) and re-rooted {worktreeLinks.Count} worktree snaplink(s)."
                        + (report.IsClean
                            ? $" Snaplinks OK — scanned {report.ScannedSnaplinks}."
                            : $" ({report.IssueCount} pre-existing snaplink issue(s) remain — unrelated.)"));
        return Clean;
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
