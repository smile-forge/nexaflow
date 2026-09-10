using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Syntax;
using Nexaflow.Services.Initiatives.Cli.Daemon;
using Nexaflow.Services.Initiatives.Hosting;

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

    /// <summary>
    /// Every invocation hands its command to the resident process for this tree, which holds the graph in
    /// memory. That is the whole of the difference: the verbs below are unchanged, and a caller sees what it
    /// always saw. The one exception is the hidden mode that <i>is</i> that process — see
    /// <see cref="DaemonServer"/>, which cannot be started any other way.
    /// </summary>
    private static int Main(string[] args)
    {
        // Say what the output actually is. The console's default code page cannot represent the em dashes and
        // arrows this tool quotes out of source and prints in its own messages, so they arrived as question
        // marks or hyphens — an error message that quotes a line of code back at you is no use if it quotes it
        // wrong. Guarded because a redirected or absent console rejects the assignment.
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { /* not a console we can set */ }

        if (args.Length > 0 && args[0] == DaemonServer.ModeArgument) return DaemonServer.Run(args);

        WarnIfPublishedExeIsStale();

        // Usage needs no tree and no graph, so it is answered here rather than paying to start a process.
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help") return Usage();

        try
        {
            // Silently here: the client resolves the root only to key the pipe, and the daemon prints the note
            // itself when it runs the command. Saying it in both places said it twice.
            _rootNoteShown = true;
            var productRoot = ResolveRoot(args.Skip(1));

            // Answered by the client rather than sent to the daemon: it is a question about that process, and
            // it has to be answerable when the process is busy, wedged, or not there at all.
            if (args[0] == "daemon") return DaemonClient.Report(productRoot, stop: args.Contains("stop"));
            return DaemonClient.Run(args, productRoot, CallerWorkingTree(productRoot));
        }
        catch (DaemonUnavailableException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return Error;
        }
    }

    /// <summary>
    /// The caller's own working tree when it is a worktree of this product, else null for the main checkout.
    /// The daemon cannot work this out for itself — it is started once and asked from everywhere — so the
    /// client states it, using the same rule every verb already applies to decide whose source it is reading.
    /// </summary>
    private static string? CallerWorkingTree(string productRoot)
    {
        var caller = WorkingTreeRootOf(CallerDirectory);
        if (caller is not { Length: > 0 } || PathsEqual(caller, productRoot)) return null;
        return TryFindMainCheckout(caller, out var main) && PathsEqual(main, productRoot) ? caller : null;
    }

    /// <summary>
    /// The directory the caller ran this command in. Inside the resident process that is a property of
    /// the request, not of the process — several callers are served at once and they are not all in the
    /// same place. In a one-shot process it is simply the process directory, which is the same answer.
    /// </summary>
    private static string CallerDirectory => CallerPath.Directory;

    /// <summary>
    /// The per-file material for this root, from the warm snapshot when there is one. Taking it from the
    /// workspace matters twice over: it is the expensive half of the archive to read, and it is the same
    /// object the warm graph was assembled from — so an edit mutates what the next caller will be handed
    /// rather than a private copy that the workspace would later overwrite.
    /// </summary>
    private static GraphCache EditCache(string root, bool main, ProductStore store) =>
        Workspace(root, main, store)?.Current()?.Cache ?? store.LoadGraphCache() ?? new GraphCache();

    /// <summary>
    /// Records a change to the graph. With a warm workspace the objects just mutated ARE its snapshot, so
    /// this only has to say so and let it flush; without one there is nothing holding them and the write
    /// happens here. Writing directly in both cases would leave the warm copy describing the file it had
    /// before, which is the one way a resident process can be worse than no process at all.
    /// </summary>
    private static void SaveGraphChange(string root, bool main, ProductStore store,
                                        KnowledgeGraph graph, GraphCache cache)
    {
        if (Workspace(root, main, store) is { } workspace) workspace.MarkChanged();
        else store.SaveSnapshot(graph, cache);
    }

    /// <summary>
    /// The tree a verb works on: always an instance nobody else holds. Resident, that is a working copy of the
    /// host's — the parse is still shared, which is what makes a query cheap, but the object is not.
    /// <para>
    /// Every verb mutates the state it is handed and only the write decides whether to persist it, so a shared
    /// instance turns each refusal into a silent edit: a <c>--dry-run</c>, a snaplink refused for naming a file
    /// that is not there, a batch that aborted on line 3 — all of them printed "nothing was written" and left
    /// their changes in the tree the daemon served to every later call, until the next write persisted them.
    /// One copy per command makes the refusal path cost nothing to get right.
    /// </para>
    /// </summary>
    private static ProductState LoadTree(string root, ProductStore store) =>
        Host is { } host && PathsEqual(host.ProductRoot, root) ? host.WorkingCopy() : store.Load();

    /// <summary>
    /// Writes the tree and tells whoever is holding one what it now says.
    /// <para>
    /// Telling it directly, rather than letting the file watcher discover the write, is the difference
    /// between the graph agreeing with the command that just ran and agreeing a debounce later — and it
    /// skips re-reading a file we produced to learn something we already knew.
    /// </para>
    /// <para>
    /// It is also where a branch's deferred links come back off. Everything loaded through
    /// <see cref="TryLoad"/> arrives with the caller's pending set overlaid, and the shared tree must never
    /// see it — so peeling it off belongs to the write itself, not to each verb that makes one. Leaving it
    /// to the verbs is exactly how it failed: only the snaplink verbs restored anything, and only the sets
    /// their own arguments named, so <c>set-status</c>, <c>add-node</c> and <c>doctor --fix</c> each wrote
    /// one branch's unmerged links into the tree every other worktree reads.
    /// </para>
    /// </summary>
    private static void SaveTree(string root, ProductStore store, ProductState state)
    {
        var deferred = PendingBranch(root) is { } branch ? PendingStoreFor(root).Load(branch) : null;
        if (deferred is { IsEmpty: false }) RestoreSharedLinks(state, store.Load(), [.. deferred.Targets]);

        store.SaveTree(state.Nodes);
        if (Host is { } host && PathsEqual(host.ProductRoot, root)) host.TreeSaved(state);

        // The caller goes on working with — and reporting on — the branch's own view, which is not what the
        // file now says. TreeSaved has already taken its copy, so putting it back publishes nothing.
        deferred?.ApplyTo(state);
    }

    /// <summary>What a command was piped, when it is running inside the daemon and there is no console to
    /// read it from. Null in a one-shot process, where <see cref="ReadStdin"/> reads the real thing.</summary>
    internal static string? StandardInput { get; set; }

    /// <summary>The warm host, when running inside the daemon. Null in a one-shot process, which builds its
    /// own for the length of the command — same object, shorter life.</summary>
    internal static InitiativesHost? Host { get; set; }

    /// <summary>The verb dispatch. Called by the daemon per request, and by nothing else.</summary>
    internal static int Execute(string[] args)
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
            "set-snaplink" => SetSnaplink(args[1..]),
            "remove-snaplink" => RemoveSnaplink(args[1..]),
            "set-node"    => SetNode(args[1..]),
            "move"        => Move(args[1..]),
            "rename"      => Rename(args[1..]),
            "remove"      => Remove(args[1..]),
            "batch"       => Batch(args[1..]),
            "lint"        => Lint(args[1..]),
            "doctor"      => Doctor(args[1..]),
            "pending"     => Pending(args[1..]),
            "promote"     => Promote(args[1..]),
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
              nfi describe   <node-id>[,<node-id>...] [<root>] [--json] [--code]
              nfi tree       [<node-id>] [<root>] [--depth <n>] [--full] [--json]
              nfi diff       [<root>] [--from <version>]
              nfi remap      <old-path> <new-path> [<root>] [--class <name>] [--method <name>]
              nfi remap      --from-git <rev-range> [<root>] [--dry-run]
              nfi scan-tests [<root>] [--test-dll <path>]... [--suggest-attributes]
              nfi add-node   <parent-id> <title> [<root>] [--id <slug>] [--desc <text>] [--status <s>]
              nfi set-status  <node-id> <status> [<root>]
              nfi set-concern <node-id> <tag> <status> [<root>]
              nfi remove-concern <node-id> <tag> [<root>]
              nfi add-snaplink <node-id> --type <code|markdown|node|url> [<root>] [--concern <tag>]
                                                [--doc <p>] [--class <c>] [--method <m>] [--target <id>] [--url <u>] [--title-path a>b] [--status <s>]
              nfi set-snaplink <node-id> --index <n> [<root>] [--concern <tag>] [--clear <f,f>] [--expect <t>]
              nfi remove-snaplink <node-id> [<root>] [--concern <tag>]
                                                [--type <t>] [--doc <p>] [--class <c>] [--method <m>] [--target <id>] | [--index <n>]
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
                       --class/--method also set those on every affected link (single-file remaps). Also a
                       batch instruction: a move that shifts several files lands as one transaction, and
                       --dry-run shows what it would rewrite before anything is written.
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
            set-snaplink Edits one existing snaplink in place, by --index (see: describe <node-id>).
                       --clear <fields> unsets them (class,method,ast,doc,target,title-path,status) - the one
                       edit remove+add cannot make without losing the link's other fields and its position.
            remove-snaplink Drops snaplinks from the node — or, with --concern <tag>, from that concern's link.
                       Name the link by what it IS: --type/--doc/--class/--method/--target, every one given
                       having to agree (--doc alone drops every link into that file, adding --method narrows
                       it to one; paths compare slash- and case-insensitively). --index <n> removes by
                       position instead — fragile, since any other edit reorders the list — and the two are
                       mutually exclusive. With neither, it clears the list, so say what you mean.
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
                       a standalone verb minus <root>: set-status / set-concern / remove-concern /
                       add-snaplink / set-snaplink / remove-snaplink / remap / set-node / add-node / move /
                       rename / remove. Blank lines and '#' comments are skipped; "quote" a value with spaces.
                       It is transactional — if any line is invalid, NOTHING is written (the error names the line).
                       --dry-run parses + applies in memory and reports, writing nothing. set-snaplink must carry
                       --expect <text> here: it addresses by --index, and an add/remove-snaplink on the same list
                       renumbers every later index, so each line pins itself to what you read.
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
            graph      Builds the knowledge graph (product tree ⊕ code AST ⊕ snaplinks) → .product/graph.bin,
                       the file the Graph viewer opens. --json writes it to stdout instead of the file.
                       --product-anchored limits the code layer to snaplinked files (default: whole repo).
                       Sub-commands explore a built graph: stats / search / list / node / walk / code — see `graph help`.
            daemon     Says what the resident process for this tree is doing: whether one is answering, every
                       command it has on the books with how long each has waited and run, and the path of its
                       log. Nothing else needs it — commands start and reuse one on their own — but when one
                       seems stuck this answers immediately, because it reads a dictionary and never queues
                       behind the work it is reporting on. `daemon stop` ends it; the next command starts
                       another.

            <root> defaults to the current directory. exit: 0 = ok, 1 = broken snaplinks, 2 = usage/IO error
            """);
        return error is null ? Clean : Error;
    }

    /// <summary>
    /// The product root from the first non-flag argument — but only when it names a directory the caller can
    /// actually see; otherwise the caller's own directory. Resolved to where <c>.product/</c> lives, see
    /// <see cref="ResolveProductRoot"/> (which follows a git worktree to its main checkout).
    /// <para>
    /// The directory test is the whole of it. <c>&lt;root&gt;</c> is a directory by definition, and taking any
    /// first positional for one meant <c>batch</c>, whose first positional is a <b>file</b>, named its script as
    /// the root: the daemon was then started with a file for a working directory, could not start at all, and
    /// the command that was meant to rewrite the tree reported a process failure instead.
    /// </para>
    /// </summary>
    internal static string ResolveRoot(IEnumerable<string> args) =>
        ResolveProductRoot(args.FirstOrDefault(a => !a.StartsWith('-')) is { } first
                        && CallerPath.IsDirectory(first) ? first : ".");

    private static bool _rootNoteShown;

    /// <summary>Resolves the directory that holds the (gitignored) <c>.product/</c> tree, so the CLI "just works"
    /// from anywhere — including a git worktree, whose <c>.product/</c> lives only in the main checkout. Order:
    /// the given path if it already has <c>.product/</c>; else, walking up to the enclosing git repo, the main
    /// working tree it links to; else the given path unchanged (the caller then reports "no .product/"). This is
    /// what lets you drop the trailing <c>&lt;root&gt;</c> and call the exe directly from any checkout or worktree.</summary>
    private static string ResolveProductRoot(string candidate)
    {
        candidate = CallerPath.Of(candidate);
        if (ProductStore.Exists(candidate)) return candidate;
        if (TryFindMainCheckout(candidate, out var main) && ProductStore.Exists(main))
        {
            // Once per repository, not once per command: it is orientation for someone who has just
            // arrived in a worktree, and after that it is a line of noise in front of every answer.
            // The resident process holds this flag, so "once" now means once, rather than once per
            // invocation of a program that forgot everything each time.
            if (!_rootNoteShown && !PathsEqual(main, candidate))
            {
                _rootNoteShown = true;
                Console.Error.WriteLine(
                    $"note: the authored product tree lives in the main checkout {main}, so tree edits "
                  + "land there. The graph, and the source every other answer is read from, are this "
                  + "worktree's own.");
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

        // The pending sets that apply where the caller stands, overlaid IN MEMORY below so the verdict is the one
        // the tree gives once they are folded in. Reported, never folded: validate used to consolidate merged sets
        // and commit the removal, which made the verb everyone runs — the installer gate, the Product page, every
        // agent — move whatever branch its caller was standing on, main included. Folding in is `promote`.
        var pending = PendingHere(root);


        IntegrityReport report;
        try
        {
            var store = new ProductStore(root);
            var state = LoadTree(root, store);
            foreach (var set in pending) set.ApplyTo(state);
            // The coverage manifest gates [CoversNode] ids that no longer exist. It is derived, gitignored
            // state, so LoadTestCoverage() returning null (clean CI checkout, or scan-tests never run) simply
            // means that check is skipped — never a failure, and never a false all-clear either.
            // From a linked worktree, validate against THAT tree alone. Falling back to the main checkout —
            // which is what the two-root form does — answers "does this file exist somewhere", and the
            // question a branch needs answered is "does it exist here". The fallback hid the one failure
            // that is genuinely yours: a file you moved away still resolved through main, so the branch read
            // clean while its links were stale. Main's own view is still available with --main.
            var roots = a.Has("--main") ? [root] : FileRootsFor(root);

            // Two different kinds of out-of-date, and only one of them is fixable from here. The manifest
            // going behind the build is refreshed silently; the build going behind its own source cannot be
            // — that needs a compiler — so it is reported as an issue instead.
            var testDlls = DiscoverTestDlls(root);
            report = SnaplinkValidator.Validate(state, root, [roots[0]], RefreshedCoverage(root, store, testDlls));
            FlagStaleTestBuilds(root, testDlls, report);
            if (save) store.SaveIntegrity(report);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: could not validate {root}: {ex.Message}");
            return Error;
        }

        // On stderr, so it never lands in --json output or changes an exit code: this is a note about work
        // waiting, not a verdict on the tree. Only in the main checkout — a set in a worktree is the branch's
        // own and has not merged, so there is nothing there to fold in.
        if (PendingBranch(root) is null && pending.Count > 0)
            Console.Error.WriteLine(
                $"note: {pending.Count} merged link set(s) ({string.Join(", ", pending.Select(p => p.Branch))}) are "
              + "included above but not yet in the shared tree. Fold them in with: nfi promote");

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

        // A snaplink whose file does not exist is an error, full stop — including from a worktree, and
        // including a file that is in neither this tree nor the main checkout. That last case used to be
        // exempt on the theory that it was some other branch's not-yet-merged work and no branch should
        // answer for another. It does not hold any more: a snaplink written on a branch is deferred into
        // docs/product/pending/<branch>.json and overlaid only for that branch (see PendingHere
        // above), so the shared tree gets a link only once the file it names has merged. What the exemption
        // actually bought, therefore, was silence about a link naming a file that exists nowhere at all —
        // which is exactly the state it was meant to catch.
        //
        // The split is still printed, because it is the fastest way to tell "I moved this file" from "this
        // was never here"; it just no longer changes the verdict.
        if (!a.Has("--main") && FileRootsFor(root) is [var here, var main] && !PathsEqual(here, main))
        {
            var (mine, elsewhere) = Partition(report, here, main);
            if (elsewhere.Count > 0)
            {
                Console.Error.WriteLine(text);
                Console.Error.WriteLine();
                // Named, not counted. A bare tally leaves the reader to work out WHICH five out of a list of
                // thirty-six by hand, which is the one thing they need and the one thing the partition knows.
                Console.Error.WriteLine(
                    $"{elsewhere.Count} of the above name files that are in neither this worktree nor the main "
                  + "checkout — nothing here can satisfy them:");
                foreach (var issue in elsewhere.Take(10))
                    Console.Error.WriteLine($"  {issue.NodeId} [{issue.Scope}] #{issue.Index}  {issue.Link.Doc}");
                if (elsewhere.Count > 10) Console.Error.WriteLine($"  … and {elsewhere.Count - 10} more");
                // Deliberately not "files the main checkout has and this branch does not". The partition only
                // knows about a file being absent from both trees; everything else lands here, including a
                // heading that moved, a method that was renamed and a stale [CoversNode] id — none of which
                // are about a file being anywhere. Naming the bucket after the one kind it can prove sent the
                // reader looking for a missing file that was never the problem.
                if (mine.Count > 0)
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine($"{mine.Count} name(s) this tree can still reach — the target inside is what broke:");
                    foreach (var issue in mine.Take(10))
                        Console.Error.WriteLine($"  {issue.NodeId} [{issue.Scope}] #{issue.Index}  {issue.Detail}");
                    if (mine.Count > 10) Console.Error.WriteLine($"  … and {mine.Count - 10} more");
                }
                return Broken;
            }
        }

        Console.Error.WriteLine(text);
        return Broken;
    }

    /// <summary>
    /// Splits file-missing issues by where the file could still be found: the ones the main checkout has and
    /// this working tree does not, and the ones that are absent everywhere. Both are gating — this only
    /// orders the output, so a reader can tell a file they moved from one that exists nowhere.
    /// </summary>
    private static (List<IntegrityIssue> Mine, List<IntegrityIssue> Elsewhere) Partition(
        IntegrityReport report, string here, string main)
    {
        List<IntegrityIssue> mine = [], elsewhere = [];
        foreach (var issue in report.Issues)
        {
            var doc = issue.Link.Doc;
            var absentEverywhere = issue.Kind == IntegrityKind.MissingFile
                                && doc is { Length: > 0 }
                                && !File.Exists(Path.Combine(here, doc.Replace('/', Path.DirectorySeparatorChar)))
                                && !File.Exists(Path.Combine(main, doc.Replace('/', Path.DirectorySeparatorChar)));
            (absentEverywhere ? elsewhere : mine).Add(issue);
        }
        return (mine, elsewhere);
    }

    // ── graph: product ⊕ code AST ⊕ snaplinks → .product/graph.bin (the Graph viewer opens it) ──

    // ── graph: build, then explore (walk / query / fetch code) the generated knowledge graph ──

    private static int Graph(string[] args)
    {
        if (args.Length > 0)
            switch (args[0])
            {
                case "stats":                 return GraphStats(args[1..]);
                case "orphans":               return GraphOrphans(args[1..]);
                case "paths" or "path":       return GraphPaths(args[1..]);
                case "rank":                  return GraphRank(args[1..]);
                // Every read of the graph ends by saying whether the graph still describes the working tree.
                // Wrapped here rather than repeated in each verb so no query can quietly forget to say.
                case "node":                  return Answered(GraphNode(args[1..]));
                case "search":                return Answered(GraphSearch(args[1..]));
                case "list":                  return Answered(GraphList(args[1..]));
                case "walk":                  return Answered(GraphWalk(args[1..]));
                case "context" or "ctx":      return Answered(GraphContext(args[1..]));
                case "grep":                  return Answered(GraphGrep(args[1..]));
                case "code" or "cat":         return Answered(GraphCode(args[1..]));
                case "edit":                  return GraphEditVerb(args[1..]);
                case "build":                 return GraphBuild(args[1..]);
                case "help" or "-h" or "--help": return GraphUsage();
            }
        return GraphBuild(args);   // `graph [<root>] [--flags]` still builds (back-compat)
    }

    /// <summary>A query's exit code, after saying whether what it just reported is current.</summary>
    private static int Answered(int code)
    {
        EndFreshness();
        return code;
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
                "you're on); the product tree + the graph archive stay in the main checkout, and the content-addressed " +
                "cache re-parses only the files that differ from it.");

        // Say when there was nothing to do, rather than spending two and a half minutes proving it. Queries
        // fold their own drift in now, so an explicit build is usually someone checking rather than fixing —
        // and "already current" is the useful answer to that. --no-incremental still rebuilds regardless:
        // that is the form you reach for precisely when you suspect the archive itself, and a check that
        // reads the archive cannot be the thing that clears it.
        if (incremental && !json && GraphIsAlreadyCurrent(root, a.Has("--main")) is { } current)
        {
            Console.WriteLine(current);
            return Clean;
        }

        KnowledgeGraph graph;
        try
        {
            var store = GraphStore(root, a.Has("--main"));
            var state = LoadTree(root, store);
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
                // Hand the result to the warm workspace rather than writing round the back of it. A build
                // that only wrote the file left the resident process serving the snapshot it loaded at
                // startup — for the life of that daemon. The symptom was not staleness but confident
                // wrongness: `graph search` answered "no nodes match" for types that plainly existed, and
                // the freshness line reported identical drift after every rebuild, because the build fixed
                // the archive and the answer came from memory. Two full rebuilds could not shift it.
                //
                // Replace persists it too, so this is the same one write it was before — and because the
                // workspace now holds exactly what was just built, nothing re-reads it afterwards.
                if (Workspace(root, a.Has("--main"), store) is { } workspace)
                    workspace.Replace(graph, built.Cache);
                else
                    store.SaveSnapshot(graph, built.Cache);
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
        Console.WriteLine($"Graph written to {GraphStore(root, a.Has("--main")).GraphFilePath} — " +
                          $"{m.NodeCount:N0} nodes, {m.EdgeCount:N0} edges, {m.HyperEdgeCount:N0} hyperedges.");
        return Clean;
    }

    // ── graph explore: walk / query the generated graph, and fetch a node's source ──

    private static int GraphUsage()
    {
        Console.WriteLine("""
            graph — build + explore the knowledge graph (.product/graph.bin)

              graph [<root>] [--no-incremental] [--product-anchored] [--main|--code-root <dir>] [--json]   (re)build the graph
              graph stats  [<root>]                                             counts + type/edge/community breakdown
              graph search <term> [<root>] [--type <t>] [--limit N]             nodes whose id/label matches <term>
              graph list   [<root>] [--type <t>] [--community N] [--file <f>] [--unparsed] [--limit N]   bulk list with filters
              graph paths  <from-id> <to-id> [<root>] [--hops N] [--undirected] [--limit N]   routes between two nodes
              graph rank   [<root>] [--by fanin|fanout] [--type <t>] [--under <path>] [--limit N]   most-depended-on nodes
              graph node   <id> [<root>] [--limit N]                            one node + ALL its edges/hyperedges
              graph walk   <id> [<root>] [--hops N] [--types a,b] [--limit N]   BFS neighbourhood out to N hops
              graph context <id> [<root>] [--lines N] [--limit N]               ONE-SHOT: node + source + neighbours + owning feature
              graph grep <pat> [<root>] [--from <id>] [--hops N | --scope owned] [--mode index|content] [--type t] [--limit N]
                                                                                regex the graph (index=graph text; content=nodes' source)
              graph code   <id> [<root>] [--lines A-B]                          code:<file>#<ast> → its block; file:<relpath> → the file
              graph edit <op> <id> [<root>] [--text T | --text-escaped T | --file F | --stdin] [--to NAME]
                         [--expect S] [--with-trivia] [--dry-run]               structural edit, verified against the parse

            edit ops: replace | delete | signature | body | rename --to <name> | insert-before | insert-after
                      | append (into a type's body) | doc | substitute --find S (a find/replace that CANNOT
                      leave the declaration — literal unless --regex, refuses unless it matches exactly once
                      unless --all; the safe form of `sed -i` on one member)
            editing: the graph names the declaration; the parse of the file IN HAND says where it is. The edit
                  is refused unless the AST path still resolves AND the parser agrees the declaration there is
                  still the one the graph labelled — so a stale graph cannot overwrite whatever now occupies
                  those lines. The result is re-parsed before writing, and an edit that would break the file is
                  refused. `signature` keeps the body byte-for-byte and `body` keeps the signature; both are
                  checked afterwards, not assumed. Line endings, indentation and BOMs are the tool's problem:
                  write the replacement flush-left with \n and it lands indented, with the file's own endings.
                  --dry-run prints the hunk and writes nothing. --expect S refuses unless the block still
                  contains S, for a caller pinning an edit to what it read.

            node types: product | file | type | member | external
            edge rels:  contains extends implements imports calls references instantiates tests documents view_of depends_on
            hyper rels: signature (member→return,params)  annotated (target→attr)  calls (caller→callee,args)
            ids:  product:<id>   file:<relpath>   code:<relpath>#<astpath>   external:<name>
            worktree: run from a linked worktree and the code layer defaults to THAT branch's source — build
                  re-parses only files that differ from the main checkout, and code/context/grep dump the branch's
                  copy. --main forces the main-checkout source; `graph --code-root <dir>` points it anywhere.
            tip:  `graph context <id>` is the fastest way to understand a node — it also names the files that node
                  owns and prints the grep command for them. To search a FEATURE's code use ownership, not radius:
                  `graph grep <pat> --from product:<slug> --scope owned --mode content` covers every file the
                  feature's snaplinks land in, whereas --hops 2 from a product node typically finds nothing and
                  --hops 3 has already left the feature. Use --hops when you mean "near THIS code" (callers of a
                  type), --scope owned when you mean "inside this feature". With no --from, content mode greps
                  every code node in the repo — the graph-native replacement for a blanket text search, and the
                  one that still reports the owning type/member of each hit.
                  .xaml → its class is a `view_of` edge; .csproj deps are `depends_on`.
            """);
        return Clean;
    }

    /// <summary>
    /// The graph for this root, from the warm host when there is one and from disk when there is not.
    /// <para>
    /// The two are the same object either way — <see cref="GraphWorkspace"/> holds exactly what
    /// <c>LoadGraph</c> would have returned, and checks it against the files on disk before handing it over. So
    /// a command answers identically whether it ran inside the resident process or a one-shot one; the only
    /// difference is that the resident one already had it.
    /// </para>
    /// </summary>
    private static bool TryLoadGraph(string root, out KnowledgeGraph graph, out int code, bool main = false)
    {
        graph = null!;
        if (!Directory.Exists(root)) { Console.Error.WriteLine($"error: no such directory: {root}"); code = Error; return false; }

        var store  = GraphStore(root, main);
        var loaded = Workspace(root, main, store)?.Graph ?? store.LoadGraph();

        if (loaded is null)
        {
            Console.Error.WriteLine($"error: no graph at {store.GraphFilePath} — build it first with: graph {root}");
            code = Error; return false;
        }
        graph = loaded; code = Clean; return true;
    }

    /// <summary>
    /// This root's warm workspace, or null when nothing is holding one — a one-shot process, or a test calling
    /// straight into a verb. The store is passed in already resolved because working out <i>which</i> archive a
    /// worktree reads, and seeding it from the main checkout the first time, is the CLI's rule and stays here.
    /// </summary>
    private static GraphWorkspace? Workspace(string root, bool main, ProductStore store) =>
        Host is { } host ? host.Workspace(CodeRootOrNull(root, main), store) : null;

    // TypeRank / NodeLine / BlockEnd / BuildAdjacency / Bfs were once declared here as private copies of the
    // GraphQuery and GraphReport originals. They are gone, and call sites use the library directly: this file
    // is a shell over that library, not a second implementation of it, and CliHasNoPrivateGraphTwinsTests
    // keeps the copies from coming back. BlockEnd itself is gone from both - where a declaration stops is
    // SourceSpans' question now, answered by the tree-sitter parse rather than by a second C# lexer.

    /// <summary>Routes between two nodes - the question `walk` cannot answer. See GraphQuery.Paths.</summary>
    private static int GraphPaths(string[] args)
    {
        if (!TryRead(Specs.GraphPaths, args, out var a, out var root, out var parseCode)) return parseCode;
        if (!TryIntOpt(a, "--hops", 6, out var hops)) return Error;
        if (!TryIntOpt(a, "--limit", 10, out var limit)) return Error;
        if (!TryLoadGraph(root, out var g, out var code)) return code;

        var from = a[0];
        var to = a[1];
        if (!g.Nodes.Any(n => n.Id == from)) return Usage($"graph paths: no node '{from}' (try: graph search)");
        if (!g.Nodes.Any(n => n.Id == to)) return Usage($"graph paths: no node '{to}' (try: graph search)");

        var undirected = a.Has("--undirected");
        Console.WriteLine(GraphReport.Paths(GraphQuery.Paths(g, from, to, hops, limit, undirected), from, to, hops, undirected));
        return Clean;
    }

    /// <summary>Nodes ordered by fan-in or fan-out - "which components are central", read rather than eyeballed.</summary>
    private static int GraphRank(string[] args)
    {
        if (!TryRead(Specs.GraphRank, args, out var a, out var root, out var parseCode)) return parseCode;
        if (!TryIntOpt(a, "--limit", 25, out var limit)) return Error;
        if (!TryLoadGraph(root, out var g, out var code)) return code;

        var by = a.Value("--by") ?? "fanin";
        if (by is not ("fanin" or "fanout"))
            return Usage($"graph rank: --by must be 'fanin' or 'fanout' (got '{by}')");

        var type = a.Value("--type");
        var under = a.Value("--under");
        Console.WriteLine(GraphReport.Rank(GraphQuery.Rank(g, by == "fanin", type, under, limit), by == "fanin", under));
        return Clean;
    }

    /// <summary>Declarations nothing appears to reach. A lead to look at, not a verdict - see GraphQuery.Orphans.</summary>
    private static int GraphOrphans(string[] args)
    {
        if (!TryRead(Specs.GraphOrphans, args, out var a, out var root, out var parseCode)) return parseCode;
        if (!TryLoadGraph(root, out var g, out var code)) return code;

        var type = a.Value("--type") ?? NodeType.Type;
        if (type is not (NodeType.Type or NodeType.Member))
            return Usage($"graph orphans: --type must be 'type' or 'member' (got '{type}')");

        if (!TryIntOpt(a, "--limit", 200, out var limit)) return Error;
        var orphans = GraphQuery.Orphans(g, type, a.Value("--under") ?? "src/", a.Has("--all"), limit);

        Console.WriteLine(GraphReport.Orphans(orphans, type, a.Has("--all")));
        return Clean;
    }

    private static int GraphStats(string[] args)
    {
        if (!TryRead(Specs.GraphStats, args, out _, out var root, out var parseCode)) return parseCode;
        if (!TryLoadGraph(root, out var g, out var code)) return code;

        Console.WriteLine(GraphReport.Stats(g));
        return Clean;
    }

    private static int GraphSearch(string[] args)
    {
        if (!TryRead(Specs.GraphSearch, args, out var a, out var root, out var parseCode)) return parseCode;
        var term = a[0];
        if (!TryIntOpt(a, "--limit", 40, out var limit)) return Error;
        if (!TryLoadGraph(root, out var g, out var code)) return code;
        BeginFreshness(root, a.Has("--main"), g);
        RefreshStaleFiles(root, a.Has("--main"), g, forced: a.Has("--refresh"));

        var hits = GraphQuery.Search(g, term, a.Value("--type"));
        Console.WriteLine(GraphReport.Search(hits, term, limit));
        return Clean;
    }

    private static int GraphList(string[] args)
    {
        if (!TryRead(Specs.GraphList, args, out var a, out var root, out var parseCode)) return parseCode;
        if (!TryIntOpt(a, "--limit", 60, out var limit)) return Error;
        if (!TryLoadGraph(root, out var g, out var code)) return code;
        BeginFreshness(root, a.Has("--main"), g);
        RefreshStaleFiles(root, a.Has("--main"), g, forced: a.Has("--refresh"));

        var type = a.Value("--type");
        var file = a.Value("--file");
        int? comm = null;
        if (a.Value("--community") is { } cs)
        {
            if (!int.TryParse(cs, out var c)) return VerbUsage($"--community must be a number (got '{cs}')");
            comm = c;
        }
        // --unparsed: a file node with no language is one nothing could read - it is in the graph as a name
        // and nothing more. Answering "what is not understood here" used to mean leaving the graph entirely.
        var unparsed = a.Has("--unparsed");
        var hits = g.Nodes.Where(n =>
                (type is null || n.Type == type) &&
                (comm is null || n.Community == comm) &&
                (!unparsed || (n.Type == NodeType.File && string.IsNullOrEmpty(n.Language))) &&
                (file is null || (n.FilePath?.Contains(file, StringComparison.OrdinalIgnoreCase) ?? false)))
            .OrderBy(n => n.Id, StringComparer.Ordinal).ToList();

        foreach (var n in hits.Take(limit)) Console.WriteLine(GraphReport.NodeLine(n));
        Console.WriteLine($"{hits.Count} node(s)" + (hits.Count > limit ? $" — showing {limit} (raise --limit or add a filter)" : "") + ".");
        if (unparsed)
            Console.WriteLine("These have no grammar and no structured extractor, so they are located but not "
                            + "understood - no types, no members, no edges. Extensions, not files, are what to fix.");
        return Clean;
    }

    private static int GraphNode(string[] args)
    {
        if (!TryRead(Specs.GraphNode, args, out var a, out var root, out var parseCode)) return parseCode;
        var id = a.Positionals.Count > 0 ? a[0] : string.Empty;
        if (!TryIntOpt(a, "--limit", 30, out var limit)) return Error;
        if (!TryLoadGraph(root, out var g, out var code)) return code;
        BeginFreshness(root, a.Has("--main"), g);
        RefreshStaleFiles(root, a.Has("--main"), g, forced: a.Has("--refresh"));

        if (GraphQuery.Node(g, id) is not { } hood)
        { Console.Error.WriteLine($"error: no node '{id}' (try: graph search)."); return Error; }

        Console.WriteLine(GraphReport.Node(hood, limit));
        return Clean;
    }

    private static int GraphWalk(string[] args)
    {
        if (!TryRead(Specs.GraphWalk, args, out var a, out var root, out var parseCode)) return parseCode;
        var id = a.Positionals.Count > 0 ? a[0] : string.Empty;
        if (!TryIntOpt(a, "--hops", 2, out var hops)) return Error;
        if (!TryIntOpt(a, "--limit", 150, out var limit)) return Error;
        if (!TryLoadGraph(root, out var g, out var code)) return code;
        BeginFreshness(root, a.Has("--main"), g);
        RefreshStaleFiles(root, a.Has("--main"), g, forced: a.Has("--refresh"));

        var types = a.Value("--types")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .ToHashSet(StringComparer.Ordinal);

        if (GraphQuery.Walk(g, id, hops, types) is not { } reached)
        { Console.Error.WriteLine($"error: no node '{id}'."); return Error; }

        Console.WriteLine(GraphReport.Walk(reached, GraphQuery.Index(g)[id].Label, hops, limit));
        return Clean;
    }

    private static int GraphCode(string[] args)
    {
        if (!TryRead(Specs.GraphCode, args, out var a, out var root, out var parseCode)) return parseCode;
        var id = a.Positionals.Count > 0 ? a[0] : string.Empty;

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

        AdoptFileIfUnknown(root, a.Has("--main"), rel);

        var lines = File.ReadAllText(full).Replace("\r\n", "\n").Split('\n');

        int s0, e0;
        if (ast is not null)   // AST block
        {
            var spans = new SourceSpans();
            // Resolved separately from the block below so an AST path that no longer exists says so, rather
            // than silently falling back to whatever declaration happens to surround the graph's stale line.
            if (spans.Resolve(rel, lines, ast) is null)
            { Console.Error.WriteLine($"error: ast path '{ast}' not found in {rel} (regenerate the graph?)."); return Error; }
            (s0, e0) = spans.Block(rel, lines, ast, 0, GraphQuery.BlockScanLines);
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

    /// <summary>
    /// Structural editing: <c>graph edit &lt;op&gt; &lt;node-id&gt;</c>. The graph names the declaration; the
    /// parse of the file in hand says where it is and whether it is still the declaration the graph
    /// describes. See <see cref="GraphEdit"/> for what is verified before anything is written.
    /// </summary>
    private static int GraphEditVerb(string[] args)
    {
        if (!TryRead(Specs.GraphEdit, args, out var a, out var root, out var parseCode)) return parseCode;

        // `create` names a path that does not exist yet, so there is no node to look up and no graph to load.
        if (a[0] is "create" or "new") return GraphCreateFile(a, root);

        if (!TryLoadGraph(root, out var graph, out var loadCode)) return loadCode;

        var op = a[0] switch
        {
            "replace"       => StructuralEdit.Op.Replace,
            "delete"        => StructuralEdit.Op.Delete,
            "signature"     => StructuralEdit.Op.Signature,
            "body"          => StructuralEdit.Op.Body,
            "rename"        => StructuralEdit.Op.Rename,
            "insert-before" => StructuralEdit.Op.InsertBefore,
            "insert-after"  => StructuralEdit.Op.InsertAfter,
            "append"        => StructuralEdit.Op.Append,
            "doc"           => StructuralEdit.Op.Doc,
            "substitute" or "sub"  => StructuralEdit.Op.Substitute,
            "import" or "using"    => StructuralEdit.Op.Import,
            _               => (StructuralEdit.Op?)null,
        };
        if (op is null)
            return VerbUsage($"unknown edit op '{a[0]}' — expected replace | delete | signature | body | "
                           + "rename | insert-before | insert-after | append | doc | substitute | import");

        WarnIfShellRewritesArguments();

        if (!TryEditText(a, out var text, out var textError)) return VerbUsage(textError!);

        if (!TryEditFind(a, out var find, out var findError)) return VerbUsage(findError!);

        
        var main  = a.Has("--main");
        var store = GraphStore(root, main);
        var cache = EditCache(root, main, store);
        var stale = !a.Has("--no-refresh");

        // Bring the graph's record of the target file up to date BEFORE looking anything up. One file's
        // parse costs milliseconds against the ninety seconds a whole-repo walk takes, and it is the
        // difference between "the graph might be stale" being something the caller has to reason about and
        // it not being one.
        var dirty = stale
                 && FileOfNodeId(a[1]) is { } target
                 && GraphBuilder.RefreshFile(graph, cache, root, target, CodeRootOrNull(root, main));



        var options = new StructuralEdit.Options(a.Has("--with-trivia"), a.Value("--expect"),
                                                 find, a.Has("--regex"), a.Has("--all"));
        var result  = GraphEdit.Plan(graph, a[1], op.Value, text, rel => ReadRaw(root, rel, main)?.Text,
                                     options, a.Value("--to"));

        if (!result.Ok)
        {
            // The refresh above may have learned something real — the file changed — and that is worth
            // keeping even though the edit itself is not going ahead.
            if (dirty) SaveGraphChange(root, main, store, graph, cache);
            Console.Error.WriteLine($"error: {result.Message}");
            return Error;
        }

        // --quiet keeps the confirmation and drops the diff, for a caller that wants the outcome and not the
        // change; --show is the opposite trade and they compose.
        if (!a.Has("--quiet")) foreach (var change in result.Changes) PrintHunk(change);
        foreach (var note in result.Notes) Console.Error.WriteLine($"note: {note}");

        if (a.Has("--dry-run"))
        {
            Console.WriteLine($"dry run — {result.Message}; nothing written.");
            return Clean;
        }

        foreach (var change in result.Changes)
        {
            // Re-resolved rather than remembered, so the write lands on the branch the read came from.
            var full = CodeFilePath(root, change.RelativePath, main);
            var raw  = full is null ? null : SourceFile.Read(full);
            if (full is null || raw is null)
            {
                Console.Error.WriteLine($"error: {change.RelativePath} vanished between planning and writing.");
                return Error;
            }

            if (SourceFile.WriteIfUnchanged(full, change.OriginalText, change.NewText, raw.Value.Encoding) is { } refused)
            {
                Console.Error.WriteLine($"error: {refused}");
                return Error;
            }
        }

        // …and again afterwards, so the graph describes what was just written. Both refreshes share one
        // save, and neither costs more than parsing the file that changed.
        foreach (var change in result.Changes)
            if (stale) dirty |= GraphBuilder.RefreshFile(graph, cache, root, change.RelativePath,
                                                        CodeRootOrNull(root, main));
        if (dirty)
        {
            SaveGraphChange(root, main, store, graph, cache);
        }

        // Deliberately no "now rebuild the graph": the file just edited has already been merged back in, and
        // saying it anyway only teaches the caller to distrust the tool between builds. A full `graph build`
        // is for the cross-file passes (call and inheritance resolution), not for editing.
        // The declaration as it now stands, so that checking an edit is part of making it rather than the next
        // command. Not for a delete or a rename — there is nothing at that id any more — and not for a
        // file-level target, where "the declaration" is the whole file.
        if (a.Has("--show") && op is not (StructuralEdit.Op.Delete or StructuralEdit.Op.Rename)
                            && a[1].StartsWith("code:", StringComparison.Ordinal))
        {
            Console.WriteLine();
            GraphCode(a.Has("--main") ? [a[1], "--main"] : [a[1]]);
            Console.WriteLine();
        }

        Console.WriteLine($"{result.Message}.");
        return Clean;
    }

    // ── Is the graph still describing what is on disk? ──────────────────────

    /// <summary>
    /// The freshness check for this invocation, started before the graph is even loaded so its ~0.7s runs
    /// against the query's own work rather than after it. A CLI process answers one question, so a single
    /// static is the whole lifetime.
    /// </summary>
    private static Task<GraphFreshness.Report>? _freshness;

    /// <summary>
    /// Kicks the check off against the graph the verb has already loaded, so it costs a stat per known file
    /// and a walk of the project directories — and nothing is read twice. Taking the file list from the
    /// cache index instead would mean reading the archive's per-file section, which holds every file's extracted
    /// nodes and costs more than the query it was meant to be describing.
    /// </summary>
    private static void BeginFreshness(string root, bool main, KnowledgeGraph graph)
    {
        var codeRoot  = CodeRootOrNull(root, main) ?? root;
        var known     = KnownFiles(graph);
        var graphFile = GraphStore(root, main).GraphFilePath;

        _freshness = Task.Run(() => GraphFreshness.Check(known, codeRoot, graphFile));
    }

    /// <summary>
    /// Only the files the builder actually read. A file node is also synthesized for an import target that
    /// never resolved to anything on disk, and those carry the REFERRING file as their source — so counting
    /// them made a clean main checkout report hundreds of files "removed".
    /// </summary>
    private static string[] KnownFiles(KnowledgeGraph graph) =>
        [.. graph.Nodes
            .Where(n => n.Type == NodeType.File
                     && n.FilePath is { Length: > 0 }
                     && string.Equals(n.Source, n.FilePath, StringComparison.OrdinalIgnoreCase))
            .Select(n => n.FilePath!)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// The "nothing to do" line when the archive already describes this tree, or null when there is work.
    /// Asking costs the same read a query does, which is nothing beside the build it saves.
    /// </summary>
    private static string? GraphIsAlreadyCurrent(string root, bool main)
    {
        var store = GraphStore(root, main);
        if (!File.Exists(store.GraphFilePath)) return null;         // never built: there is certainly work

        if (!TryLoadGraph(root, out var graph, out _)) return null;

        GraphFreshness.Report report;
        try { report = GraphFreshness.Check(KnownFiles(graph), CodeRootOrNull(root, main) ?? root, store.GraphFilePath); }
        catch { return null; }                                      // cannot tell: build rather than claim

        return report.Available && report.IsCurrent
            ? $"Graph already current — {report.Known:N0} files, nothing changed since it was built. "
            + "Nothing to rebuild (--no-incremental forces one anyway)."
            : null;
    }

    /// <summary>
    /// Prints the verdict — to stderr, because it is a note about the answer rather than part of it, and a
    /// caller piping results should not have to strip it. Always says something definite: either the graph
    /// is current or these files have moved on. Leaving it unsaid is what makes a reader assume the worst.
    /// </summary>
    private static void EndFreshness()
    {
        if (_freshness is null) return;
        try
        {
            var report = _freshness.Result;
            if (report.Available) Console.Error.WriteLine(report.Summary());
        }
        catch { }   // a freshness check that fails must never fail the query it was describing
    }

    /// <summary>
    /// How much drift a query will fold in on its own before deciding that rebuilding is the honest answer.
    /// <para>
    /// The same judgement, and the same number, as the warm workspace's own limit: dozens of files is
    /// somebody working and costs a parse each; thousands is a checkout seeded from somewhere else, and
    /// re-parsing thousands is the two-and-a-half-minute build nobody asked for by typing <c>graph node</c>.
    /// </para>
    /// </summary>
    private const int AutoRefreshLimit = 200;

    /// <summary>
    /// Folds every file that has moved on back into the graph before the query reads it, so the answer is
    /// about the code as it is rather than a warning about code as it was.
    /// <para>
    /// This runs whether or not <c>--refresh</c> was passed. Detecting staleness and then answering from it
    /// anyway put the reader in the worst position available: a confident answer, a note saying it might be
    /// wrong, and a flag they had to know to type. <c>--refresh</c> now means only "do it however much there
    /// is" — past <see cref="AutoRefreshLimit"/> an unforced query leaves the work alone and the freshness
    /// line says a build is wanted, because that much re-parsing is not something to do to someone who asked
    /// for one node.
    /// </para>
    /// </summary>
    private static void RefreshStaleFiles(string root, bool main, KnowledgeGraph graph, bool forced = true)
    {
        if (_freshness is null) return;

        GraphFreshness.Report report;
        try { report = _freshness.Result; } catch { return; }
        if (!report.Available || report.IsCurrent) return;
        if (!forced && report.Stale.Count > AutoRefreshLimit) return;

        var store = GraphStore(root, main);
        var cache = EditCache(root, main, store);

        // Having re-read these is itself worth recording, whether or not any of them turned out to differ.
        // A file restored by git parses to exactly what was already in the graph, so nothing changes, and
        // when nothing changed nothing was written — leaving the stamp at its old value, so the file read
        // as stale on the next query too, and every one after it. --refresh did not clear it either.
        var dirty = report.Stale.Count > 0;

        foreach (var rel in report.Stale)
            GraphBuilder.RefreshFile(graph, cache, root, rel, CodeRootOrNull(root, main));

        // Files the graph names that this tree does not have. Dropping them is only safe because the graph
        // being updated is this tree's own — from a shared one they could as easily be a parallel branch's
        // work in progress, and pruning would delete it.
        var pruned = 0;
        if (GraphIsLocal(root, main))
            foreach (var rel in report.Absent)
                if (GraphBuilder.ForgetFile(graph, cache, rel)) { pruned++; dirty = true; }

        if (!dirty) return;

        SaveGraphChange(root, main, store, graph, cache);
        Console.Error.WriteLine(
            $"graph: refreshed {report.Stale.Count} file(s)"
          + (pruned > 0 ? $" and dropped {pruned} not in this tree" : "") + " before answering.");
        _freshness = null;                       // the report it would print is now out of date itself
    }

    /// <summary>
    /// Makes sure the graph knows the file being read, and says so when it did not.
    /// <para>
    /// This verb answers from the file on disk, so it worked perfectly well on a file the graph had never
    /// heard of — which reads as confirmation that the graph holds it. It is not, and the wrong conclusion
    /// is an expensive one to draw: a file missing from the graph is invisible to <c>search</c>, <c>node</c>
    /// and every other query, and the one command that would have exposed that was instead concealing it.
    /// So the file is folded in and the note is printed; the next query finds it.
    /// </para>
    /// </summary>
    private static void AdoptFileIfUnknown(string root, bool main, string rel)
    {
        var store = GraphStore(root, main);
        if (!File.Exists(store.GraphFilePath)) return;      // nothing built yet: not this verb's business
        if (!TryLoadGraph(root, out var graph, out _)) return;

        if (graph.Nodes.Exists(n => n.Type == NodeType.File
                                 && string.Equals(n.FilePath, rel, StringComparison.OrdinalIgnoreCase)))
            return;

        var cache = EditCache(root, main, store);
        if (!GraphBuilder.RefreshFile(graph, cache, root, rel, CodeRootOrNull(root, main))) return;

        SaveGraphChange(root, main, store, graph, cache);
        Console.Error.WriteLine($"graph: {rel} was not in the graph — added it, so queries can find it too.");
    }

    /// <summary>The repo-relative file a node id names, or null when the id names neither.</summary>
    private static string? FileOfNodeId(string id)
    {
        if (id.StartsWith("file:", StringComparison.Ordinal)) return id["file:".Length..];
        if (!id.StartsWith("code:", StringComparison.Ordinal)) return null;
        var hash = id.IndexOf('#');
        return hash < 0 ? id["code:".Length..] : id["code:".Length..hash];
    }

    /// <summary>The tree source should be read from — the caller's worktree, or null to mean the product root.</summary>
    private static string? CodeRootOrNull(string productRoot, bool main) =>
        main ? null : FileRootsFor(productRoot) is [var here, _] ? here : null;

    /// <summary>
    /// The store the <b>derived graph</b> is read from and written to — this working tree's own when we are
    /// in a worktree, the shared one in the main checkout.
    /// <para>
    /// A graph is a function of source, and source differs per branch, so one shared graph forced a bad
    /// choice: a worktree either read the main checkout's view of code it does not have, or wrote its own
    /// view into a file every other session reads. The second is the worse half — it hands a parallel
    /// session this branch's idea of the codebase, and a file that branch is only halfway through creating
    /// looks to everyone else like a file that exists.
    /// </para>
    /// <para>
    /// Per-tree graphs make "does this graph describe my code?" answerable with yes, which is what the
    /// freshness check needs in order to say anything useful. It also makes pruning safe: a file absent
    /// from a tree really is absent from that tree's graph, rather than possibly being someone else's work
    /// in progress. The authored product tree is untouched by this and stays shared — it is written, not
    /// derived, and it is deliberately forward-looking.
    /// </para>
    /// </summary>
    private static ProductStore GraphStore(string productRoot, bool main)
    {
        if (CodeRootOrNull(productRoot, main) is not { } here || PathsEqual(here, productRoot))
            return new ProductStore(productRoot);

        var scoped = new ProductStore(productRoot, Path.GetFileName(here.TrimEnd('/', '\\')));
        if (File.Exists(scoped.GraphFilePath)) return scoped;

        // First use in this worktree. Start from the main checkout's graph rather than a ninety-second
        // build: most of a branch is the same code, so a clone plus a refresh of what differs is the cheap
        // way in — and until it is refreshed the freshness line says exactly how far off it is.
        try
        {
            var shared = new ProductStore(productRoot);
            if (File.Exists(shared.GraphFilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(scoped.GraphFilePath)!);
                File.Copy(shared.GraphFilePath, scoped.GraphFilePath);
                Console.Error.WriteLine(
                    "graph: this worktree had none, so the main checkout's was cloned for it — `--refresh` "
                  + "brings it onto your branch, and nothing here writes to the shared one.");
            }
        }
        catch { }   // a failed clone just means an empty local graph, which the next build fills

        return scoped;
    }

    /// <summary>Whether the graph being updated is this working tree's own, and so may have files absent
    /// from the tree pruned out of it.</summary>
    private static bool GraphIsLocal(string productRoot, bool main) =>
        !main && CodeRootOrNull(productRoot, main) is { } here && !PathsEqual(here, productRoot);

    /// <summary>
    /// Writes a new file. It belongs on this verb rather than being left to whatever else can write a file,
    /// because the same two things should be true of a created file as of an edited one: it has to parse, and
    /// it is written with the line endings the repository uses rather than the caller's. An existing file is
    /// refused — overwriting one is an edit, and there are nine operations for that.
    /// </summary>
    private static int GraphCreateFile(VerbArgs a, string root)
    {
        var rel = a[1].Replace('\\', '/');
        if (Path.IsPathRooted(rel)) return VerbUsage($"give a repo-relative path, not '{rel}'");

        if (!TryEditText(a, out var text, out var textError)) return VerbUsage(textError!);
        if (text is null) return VerbUsage("creating a file needs its content (--text, --file or --stdin)");

        var target = Path.Combine(CodeRootFor(root, a.Has("--main")),
                                  rel.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(target))
        {
            Console.Error.WriteLine($"error: {rel} already exists — use an edit op to change it.");
            return Error;
        }

        // Same bar as an edit: a file that does not parse must not be written, because the next tool to read
        // it sees a root ERROR node and every declaration in it vanishes from the graph.
        var grammar = TreeSitterLanguages.ForFile(rel);
        if (grammar is { Length: > 0 } && !new DeclarationAnchors().ParsesCleanly(grammar, text))
        {
            Console.Error.WriteLine($"error: that content does not parse as {grammar}, so {rel} was not created.");
            return Error;
        }

        // A new file has no endings of its own, so it takes its neighbours' rather than the machine's.
        var newline = SourceFile.NewlineFor(target, CodeRootFor(root, a.Has("--main")));
        var body    = string.Join(newline, SourceText.BlockOf(text)) + newline;

        if (a.Has("--dry-run"))
        {
            Console.WriteLine($"--- {rel} (new, {SourceText.Of(body).Lines.Count} lines)");
            foreach (var line in SourceText.Of(body).Lines) Console.WriteLine("+ " + line);
            Console.WriteLine("dry run — nothing written.");
            return Clean;
        }

        try
        {
            if (Path.GetDirectoryName(target) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
            File.WriteAllText(target, body, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: could not create {rel}: {ex.Message}");
            return Error;
        }

        Console.WriteLine($"created {rel} ({SourceText.Of(body).Lines.Count} lines). It is editable straight "
                        + $"away — code:{rel}#<astpath> works without a graph build.");
        return Clean;
    }

    /// <summary>The root a new file should be created under — the caller's working tree, so a file created
    /// from a worktree lands on that branch.</summary>
    private static string CodeRootFor(string productRoot, bool main) =>
        main ? productRoot : FileRootsFor(productRoot)[0];

    /// <summary>A single backslash, written by code point because a literal one has to survive a shell, a
    /// heredoc and an escape decoder to get into this file, and it does not always manage it.</summary>
    private const char Backslash = '\u005C';

    /// <summary>
    /// A warning that this exe is the published copy and the CLI has been changed since it was published, or
    /// nothing when it is not that copy or is current.
    /// <para>
    /// tools/graph-cli/ is gitignored and refreshed by hand, so it drifts behind the source it was built from
    /// and then fails with "unknown option" on a switch the documentation describes. That reads as the docs
    /// being wrong rather than the binary being old, and costs a detour every time. Only the published copy is
    /// checked: a dev build IS its source, and warning about one would be noise on every call.
    /// </para>
    /// </summary>
    private static void WarnIfPublishedExeIsStale()
    {
        var exe = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetFileName(exe), "graph-cli", StringComparison.OrdinalIgnoreCase)) return;

        var tools = Path.GetDirectoryName(exe);
        if (tools is null || !string.Equals(Path.GetFileName(tools), "tools", StringComparison.OrdinalIgnoreCase))
            return;

        var repo = Path.GetDirectoryName(tools);
        if (repo is null) return;

        var built = File.GetLastWriteTimeUtc(Path.Combine(exe, "nfi.dll"));
        string[] sources =
        [
            Path.Combine(repo, "src", "Nexaflow.Services.Initiatives.Cli"),
            Path.Combine(repo, "src", "Nexaflow.Services.Initiatives"),
            Path.Combine(repo, "src", "Nexaflow.Syntax"),
        ];

        var newest = sources.Where(Directory.Exists)
                            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
                            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                            .Select(File.GetLastWriteTimeUtc)
                            .DefaultIfEmpty(built)
                            .Max();

        if (newest <= built) return;

        Console.Error.WriteLine(
            $"note: this nfi.exe was published on {built.ToLocalTime():yyyy-MM-dd HH:mm} and the CLI source has "
          + $"changed since ({newest.ToLocalTime():yyyy-MM-dd HH:mm}) — an option the docs describe may not exist "
          + "in it yet. Refresh with tools/publish-graph-cli.ps1, from the main checkout.");
    }

    /// <summary>
    /// A warning that this shell is rewriting arguments before the process ever sees them, or nothing when it
    /// is not. Git Bash and MSYS convert anything that looks like a POSIX path: a value beginning with a slash
    /// is re-rooted at the Git installation, and a leading double slash collapses to one. The second is the
    /// dangerous half — a C-style comment passed as replacement text arrives with one slash, which is a syntax
    /// error the edit then refuses for a reason that has nothing to do with what the caller wrote. Setting
    /// MSYS2_ARG_CONV_EXCL turns the whole thing off, so the presence of MSYSTEM without it is the exact
    /// condition worth saying something about.
    /// </summary>
    private static void WarnIfShellRewritesArguments()
    {
        if (Environment.GetEnvironmentVariable("MSYSTEM") is not { Length: > 0 }) return;
        if (Environment.GetEnvironmentVariable("MSYS2_ARG_CONV_EXCL") is { Length: > 0 }) return;

        Console.Error.WriteLine(
            "note: this is a Git Bash / MSYS shell, which rewrites arguments that look like POSIX paths — a "
          + "leading // becomes /, so a C-style comment passed as --text or --find arrives short a slash. "
          + "Export MSYS2_ARG_CONV_EXCL to '*' for the session, or pass payloads through --file / --find-file.");
    }

    /// <summary>
    /// The replacement text, from whichever source was given. Four sources rather than one because the shell
    /// is the awkward part of this: <c>--file</c> and <c>--stdin</c> pass code through untouched,
    /// <c>--text</c> is literal, and <c>--text-escaped</c> exists for the caller who can only send one line
    /// and needs a backslash-n to mean something.
    /// </summary>
    private static bool TryEditText(VerbArgs a, out string? text, out string? error) =>
        TryPayload(a, "--text", "--text-escaped", "--file", "--stdin", out text, out error);

    /// <summary>
    /// The same four sources for <c>--find</c>. It used to have only the two inline ones, which made a
    /// multi-line fragment containing an apostrophe — a possessive or a contraction in a comment — unsendable
    /// from a POSIX shell, and pushed callers into regexes they had no other reason to write.
    /// <para>
    /// One trailing newline is dropped, exactly as the replacement side drops one: a file or a pipe ends with
    /// a line break that is how text files are written rather than part of what the caller means to match.
    /// Keeping it swallowed the line after the fragment, and a two-line comment replaced by one line took the
    /// following statement with it — which parses, sometimes, and is why this is not left to the caller.
    /// </para>
    /// </summary>
    private static bool TryEditFind(VerbArgs a, out string? find, out string? error)
    {
        if (!TryPayload(a, "--find", "--find-escaped", "--find-file", "--find-stdin", out find, out error))
            return false;

        if (find is { } f && f.EndsWith('\n')) find = f[..^(f.EndsWith("\r\n", StringComparison.Ordinal) ? 2 : 1)];
        return true;
    }

    /// <summary>
    /// One payload from exactly one of four sources: literal, backslash-escaped, a file, or standard input.
    /// Null when none was given, which every caller reads as "not asked for" rather than as empty.
    /// </summary>
    private static bool TryPayload(VerbArgs a, string literalOpt, string escapedOpt, string fileOpt,
                                   string stdinOpt, out string? text, out string? error)
    {
        text  = null;
        error = null;

        string?[] sources = [a.Value(literalOpt), a.Value(escapedOpt), a.Value(fileOpt),
                             a.Has(stdinOpt) ? "" : null];
        if (sources.Count(s => s is not null) > 1)
        {
            error = $"give exactly one of {literalOpt}, {escapedOpt}, {fileOpt} or {stdinOpt}";
            return false;
        }

        if (a.Has("--stdin") && a.Has("--find-stdin"))
        {
            error = "--stdin and --find-stdin both read standard input, so only one of them can be used";
            return false;
        }

        if (a.Value(literalOpt) is { } literal) text = literal;
        else if (a.Value(escapedOpt) is { } esc)
        {
            if (MangledEscapes(escapedOpt, esc) is { } mangled) { error = mangled; return false; }
            text = SourceText.Unescape(esc);
        }
        else if (a.Value(fileOpt) is { } path)
        {
            // Caller-relative: a payload is nearly always named as a path from where the command was typed, and
            // the resident process serving it stands somewhere else entirely.
            var full = CallerPath.Of(path);
            if (!File.Exists(full)) { error = $"no such file: {path}"; return false; }
            text = File.ReadAllText(full);
        }
        else if (a.Has(stdinOpt)) text = ReadStdin();

        return true;
    }

    /// <summary>
    /// Standard input decoded as UTF-8 rather than as the console's code page. <c>Console.In</c> uses the
    /// latter, so a payload piped in with any character outside ASCII — an em dash, which this codebase writes
    /// constantly — arrived as something else and the edit then failed to match, or worse, matched and wrote
    /// the wrong character. Opening the raw stream sidesteps the console entirely.
    /// </summary>
    private static string ReadStdin()
    {
        // Inside the daemon there is no console to open: the client read the pipe's worth and carried it.
        if (StandardInput is { } carried) return carried;

        using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Why an escaped payload looks like the shell got to it first, or null when it does not.
    /// <para>
    /// Git Bash and MSYS rewrite an argument they take for a POSIX path, and the damage is not always loud. A
    /// find value starting with a slash comes back rooted at the Git installation and simply fails to match,
    /// which is fine. But a doc comment written with escapes arrives with its backslashes turned round, and
    /// that still parses, still applies, and reports success — it is found by eye, in the file, afterwards.
    /// </para>
    /// <para>
    /// The signature is exact enough to act on: an <em>escaped</em> payload whose whole point is its
    /// backslashes, carrying none, while carrying a slash where each escape would have been. A caller who
    /// really means those two characters wants the literal option, which is what the refusal says.
    /// </para>
    /// </summary>
    internal static string? MangledEscapes(string option, string value)
    {
        if (value.Contains(Backslash)) return null;
        if (!value.Contains("/n") && !value.Contains("/t") && !value.Contains("/r")) return null;

        var literal = option.Replace("-escaped", string.Empty);
        return $"{option} carries no backslash at all yet contains /n, /t or /r where its escapes would have "
             + "been — a shell (Git Bash or MSYS) has almost certainly turned them round while converting the "
             + "value to a Windows path. Applying it would write those two characters into the file and report "
             + "success. Export MSYS2_ARG_CONV_EXCL to '*' to stop the conversion, pass the text through "
             + $"{literal}-file or {literal}-stdin, or use {literal} if you really did mean a slash and an n.";
    }

    /// <summary>A file's raw text plus the encoding to write it back with, resolved the same worktree-first
    /// way every other graph read is. See <see cref="SourceFile"/>.</summary>
    private static (string Text, Encoding Encoding)? ReadRaw(string productRoot, string rel, bool main) =>
        CodeFilePath(productRoot, rel, main) is { } full ? SourceFile.Read(full) : null;

    /// <summary>The changed lines, the way a diff shows them — enough to see what an edit did without
    /// re-reading the file.</summary>
    private static void PrintHunk(GraphEdit.FileChange change)
    {
        Console.WriteLine($"--- {change.RelativePath}:{change.Hunk.Line}");
        foreach (var line in change.Hunk.Removed) Console.WriteLine($"- {line}");
        foreach (var line in change.Hunk.Added)   Console.WriteLine($"+ {line}");
    }

    private static bool TryRange(string s, out int a, out int b)
    {
        a = b = 0;
        var dash = s.IndexOf('-');
        if (dash > 0 && int.TryParse(s[..dash], out a) && int.TryParse(s[(dash + 1)..], out b)) return true;
        if (int.TryParse(s, out a)) { b = a; return true; }
        return false;
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

    /// <summary>One call that packs everything about a node an agent needs to act: identity + metadata, its source
    /// (budgeted), its immediate relationships, and the product feature(s) that own it — so a single fetch replaces a
    /// node+code+walk sequence.</summary>
    private static int GraphContext(string[] args)
    {
        if (!TryRead(Specs.GraphContext, args, out var a, out var root, out var parseCode)) return parseCode;
        var id = a.Positionals.Count > 0 ? a[0] : string.Empty;
        if (!TryIntOpt(a, "--limit", 6, out var near)) return Error;
        if (!TryIntOpt(a, "--lines", 60, out var sourceLines)) return Error;
        if (!TryLoadGraph(root, out var g, out var code)) return code;
        BeginFreshness(root, a.Has("--main"), g);
        RefreshStaleFiles(root, a.Has("--main"), g, forced: a.Has("--refresh"));

        // The library reads source through a callback precisely so the CLI can resolve the caller's working
        // tree first (and --main can override it) without the query layer knowing anything about worktrees.
        if (GraphQuery.Context(g, id, rel => TryReadLines(root, rel, a.Has("--main")), sourceLines) is not { } ctx)
        { Console.Error.WriteLine($"error: no node '{id}' (try: graph search)."); return Error; }

        Console.WriteLine(GraphReport.Context(ctx, near));
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

        // --hops and --scope owned are two different answers to "what counts as near", so taking both would
        // mean silently ignoring one. Strict beats convenient here, as everywhere else in this parser.
        var scope = a.Value("--scope") ?? "hops";
        if (scope is not ("hops" or "owned")) return VerbUsage($"--scope must be hops or owned (got '{scope}')");
        if (scope == "owned" && a.Value("--from") is null)
            return VerbUsage("--scope owned needs --from <id> - ownership is relative to a node");
        if (scope == "owned" && a.Value("--hops") is not null)
            return VerbUsage("--scope owned ignores radius - drop --hops, or drop --scope");

        var mode = a.Value("--mode") ?? "index";
        if (mode is not ("index" or "content")) return VerbUsage($"--mode must be index or content (got '{mode}')");
        // --limit bounds what is REPORTED; --scan-cap bounds how far the content scan walks. They used to be
        // one number, defaulted to 60, which meant a content search stopped after 60 of ~46,000 code nodes
        // and reported "0 matches" — indistinguishable from "not present", and the reason this verb was
        // easier to abandon than to narrow. Scanning is cheap (files are read once and cached); it is output
        // that needs a bound, so the cap is now a runaway guard rather than the working limit.
        //
        // That split was right and the implementation did not honour it: hitting --limit `break`ed the scan
        // loop, so the "output bound" silently ended discovery too and the total was whatever had been found
        // by then. A whole-repo sweep for SupportsMultipleFiles reported 40 matching nodes; there were 122,
        // and the two consumers that decide whether a multi-selection may run an action were both past the
        // fortieth. "Nothing reads this flag" is the conclusion that reads out of that, and it is wrong.
        // --limit now trims printing only; the scan runs on and the total is the true one.
        //
        // The cap defaulted to 50,000 against 64,230 code nodes, so the runaway guard had quietly become a
        // truncation of the ordinary case. An uncapped sweep is ~3s. A guard that fires on the normal path
        // is not a guard, so the default is now unbounded and --scan-cap is opt-in.
        if (!TryIntOpt(a, "--limit", mode == "content" ? 40 : 200, out var limit)) return Error;
        if (!TryIntOpt(a, "--scan-cap", int.MaxValue, out var scanCap)) return Error;

        if (!TryLoadGraph(root, out var g, out var code)) return code;
        BeginFreshness(root, a.Has("--main"), g);
        RefreshStaleFiles(root, a.Has("--main"), g, forced: a.Has("--refresh"));

        Regex rx;
        try { rx = new Regex(pattern, RegexOptions.IgnoreCase); }
        catch (Exception ex) { Console.Error.WriteLine($"error: bad regex: {ex.Message}"); return Error; }

        var from = a.Value("--from");
        if (from is not null && !GraphQuery.Index(g).ContainsKey(from))
        { Console.Error.WriteLine($"error: no node '{from}'."); return Error; }

        IEnumerable<GraphNode> searched = GraphQuery.Scope(
            g, from, hops,
            scope == "owned" ? GraphQuery.GrepScope.Owned : GraphQuery.GrepScope.Hops,
            out var ownedButEmpty);
        if (ownedButEmpty)
            Console.WriteLine($"note: '{from}' owns no files - it has no snaplinks to code. Falling back to the whole graph.");

        if (a.Value("--type") is { } type) searched = searched.Where(n => n.Type == type);

        if (mode == "content")
        {
            var codeNodes = searched.Where(n => n.FilePath is { Length: > 0 } && n.Metadata != null
                                             && n.Metadata.ContainsKey("line") && n.Metadata.ContainsKey("ast"))
                                 .OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
            // A file is read once, regex-tested once as a whole, and parsed at most once. The whole-file test
            // comes first because it is the cheap half: a file with no matching line anywhere cannot hold a
            // matching block, so every node in it is dismissed without a parse - and most files match nothing.
            var fileCache = new Dictionary<string, string[]?>(StringComparer.Ordinal);
            var interesting = new Dictionary<string, bool>(StringComparer.Ordinal);
            var spans = new SourceSpans();
            List<(int Line, string Text)> HitsIn(GraphNode n)
            {
                var none = new List<(int, string)>();
                if (!fileCache.TryGetValue(n.FilePath!, out var lines)) fileCache[n.FilePath!] = lines = TryReadLines(root, n.FilePath!, a.Has("--main"));
                if (lines is null || !int.TryParse(n.Metadata!["line"], out var startLine)) return none;
                if (!interesting.TryGetValue(n.FilePath!, out var any))
                    interesting[n.FilePath!] = any = lines.Any(rx.IsMatch);
                if (!any) return none;
                var (s0, e0) = spans.Block(n.FilePath!, lines, n.Metadata!["ast"], startLine - 1, GraphQuery.BlockScanLines);
                var hits = new List<(int Line, string Text)>();
                for (var i = Math.Max(0, s0); i <= e0 && i < lines.Length; i++) if (rx.IsMatch(lines[i])) hits.Add((i + 1, lines[i]));
                return hits;
            }

            var tally = ScanContent(codeNodes, limit, scanCap, HitsIn, (n, hits) =>
            {
                Console.WriteLine(GraphReport.NodeLine(n));
                foreach (var (line, text) in hits.Take(6)) Console.WriteLine($"      {line,5}: {text.Trim()}");
                if (hits.Count > 6) Console.WriteLine($"      … +{hits.Count - 6} more matching line(s)");
            });

            // Say plainly when the answer is partial. "Raise --limit to see more" reads as pagination, and a
            // sweep asking "does anything still do X" is then answered by a number that is not the answer.
            if (tally.Capped)
                Console.WriteLine($"… INCOMPLETE: --scan-cap {scanCap} stopped the scan at {tally.Scanned} of "
                                  + $"{codeNodes.Count} code nodes. Absence from this list is not absence from the repo.");
            var shown = tally.Matched > tally.Reported ? $" — showing {tally.Reported}, raise --limit for the rest" : "";
            Console.WriteLine($"{tally.Matched} code node(s) with source matches (scanned {tally.Scanned}){shown}.");
        }
        else
        {
            bool Matches(GraphNode n) => rx.IsMatch(n.Id) || (n.Label is { } lb && rx.IsMatch(lb))
                                         || (n.Metadata is { } m && m.Values.Any(v => rx.IsMatch(v)));
            var hits = searched.Where(Matches).OrderBy(n => GraphQuery.TypeRank(n.Type)).ThenBy(n => n.Id, StringComparer.Ordinal).ToList();
            foreach (var n in hits.Take(limit)) Console.WriteLine(GraphReport.NodeLine(n));
            Console.WriteLine($"{hits.Count} node(s) whose graph text matches /{pattern}/" + (hits.Count > limit ? $" — showing {limit}" : "") + ".");
        }
        return Clean;
    }

    /// <summary>
    /// Walks <paramref name="codeNodes"/>, emitting at most <paramref name="limit"/> of the matches but
    /// counting every one of them.
    /// <para>
    /// The distinction is the whole point. <c>--limit</c> bounds output; <c>--scan-cap</c> bounds work. When
    /// the two were one loop condition, reaching the output limit ended the search, so the reported total was
    /// "however many turned up before we stopped looking" while reading exactly like a total. A sweep for
    /// <c>SupportsMultipleFiles</c> answered 40; it is 124, and the two call sites that decide whether a
    /// multi-selection may run a file action were both past the fortieth.
    /// </para>
    /// </summary>
    internal static (int Matched, int Reported, int Scanned, bool Capped) ScanContent(
        IReadOnlyList<GraphNode> codeNodes,
        int limit,
        int scanCap,
        Func<GraphNode, List<(int Line, string Text)>> hitsIn,
        Action<GraphNode, List<(int Line, string Text)>> emit)
    {
        int scanned = 0, matched = 0, reported = 0;
        foreach (var n in codeNodes)
        {
            if (scanned >= scanCap) return (matched, reported, scanned, true);
            scanned++;

            var hits = hitsIn(n);
            if (hits.Count == 0) continue;

            matched++;
            if (reported >= limit) continue;   // keep counting; only the printing is bounded
            reported++;
            emit(n, hits);
        }
        return (matched, reported, scanned, false);
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

        // One id, or several comma-separated. Comma rather than a variadic positional because the verb also
        // takes an optional trailing <root>: `describe a b` cannot mean both "two nodes" and "node a in
        // repo b". Reading a set of nodes is the common case when working out what a group of them covers,
        // and one call beats N round trips.
        var ids = a[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (ids.Length == 0) return Usage("describe needs at least one node id");
        if (!TryLoad(root, out var state, out var code)) return code;

        var found   = new List<ProductQuery.Detail>();
        var missing = new List<string>();
        foreach (var one in ids)
        {
            var got = ProductQuery.Describe(state, one);
            if (got is null) missing.Add(one); else found.Add(got);
        }

        foreach (var m in missing) Console.Error.WriteLine($"error: no node '{m}' (try: find).");
        if (found.Count == 0) return Error;

        if (a.Has("--json"))
        {
            // A single id keeps its object shape; several become an array, so a caller that asked for one
            // thing is not handed a collection it did not ask for.
            Console.WriteLine(found.Count == 1
                ? JsonSerializer.Serialize(found[0], ProductJson.Options)
                : JsonSerializer.Serialize(found, ProductJson.Options));
            return missing.Count > 0 ? Error : Clean;
        }

        for (var i = 0; i < found.Count; i++)
        {
            if (i > 0) Console.WriteLine();
            Console.WriteLine(ProductReport.Describe(found[i]));
            if (a.Has("--code") && state.Nodes.TryGetValue(found[i].Id, out var each))
                PrintResolvedCodeForCaller(root, each);
        }
        return missing.Count > 0 ? Error : Clean;
    }

    /// <summary>
    /// Prints a node's snaplinked source, resolved from the CALLER's working tree first (so a worktree shows
    /// the code on the branch you're editing, including files not yet in the main checkout), then the
    /// product root.
    /// </summary>
    private static void PrintResolvedCodeForCaller(string root, ProductNode node)
    {
        var caller = WorkingTreeRootOf(CallerDirectory);
        var fileRoots = new[] { caller, root }.Where(r => r is { Length: > 0 }).Distinct().ToArray()!;
        PrintResolvedCode(fileRoots!, node);
    }

    private static int Tree(string[] args)
    {
        if (!TryRead(Specs.Tree, args, out var a, out var root, out var parseCode)) return parseCode;
        var id = a.Positionals.Count > 0 ? a[0] : string.Empty;

        int? maxDepth = null;
        if (a.Value("--depth") is { } depthS)
        {
            if (!int.TryParse(depthS, out var d) || d < 0)
                return Usage($"--depth must be a non-negative integer (got '{depthS}').");
            maxDepth = d;
        }

        if (!TryLoad(root, out var state, out var code)) return code;

        // No node id: outline every root. On a tree that has none this prints nothing and says so, which is
        // the honest answer for a repo whose .product/ holds only a graph.
        if (string.IsNullOrEmpty(id))
        {
            var roots = ProductAggregator.Roots(state).ToList();
            if (roots.Count == 0)
            {
                Console.WriteLine($"No product tree under {root} — the graph is there, but nothing describes what it is for.");
                Console.WriteLine("next: nfi graph stats | nfi graph search <term> | nfi add-node <parent-id> \"<title>\"");
                return Clean;
            }
            foreach (var r in roots)
                if (ProductQuery.Outline(state, r, maxDepth) is { } rootRows)
                    Console.WriteLine(ProductReport.Outline(rootRows, a.Has("--full")));
            return Clean;
        }

        var rows = ProductQuery.Outline(state, id, maxDepth);
        if (rows is null)
        {
            // Name the tree that was searched. Without it, running from the wrong directory produces a true
            // but useless sentence about a node that does exist — somewhere else.
            Console.Error.WriteLine($"error: no node '{id}' in the tree under {root} (try: find).");
            return Error;
        }

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
        // Same distinction as GraphCodeRoot: the caller's tree is only a better place to look when it is a
        // worktree OF this product root. Pointed at another repository, preferring the caller's tree would
        // resolve that repo's snaplinks against this one's files and quietly validate the wrong source.
        var caller = WorkingTreeRootOf(CallerDirectory);
        if (caller is not { Length: > 0 } || PathsEqual(caller, productRoot)) return [productRoot];
        return TryFindMainCheckout(caller, out var callerMain) && PathsEqual(callerMain, productRoot)
            ? [caller, productRoot]
            : [productRoot];
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
    /// The repository a git-reading verb should run in: the <b>caller's</b> working tree, falling back to the
    /// product root's.
    /// <para>
    /// The two differ exactly when you are standing in a linked worktree, and there the product root is the
    /// MAIN checkout — whose HEAD does not know your commits. Handing it a range ending at <c>HEAD</c> is not
    /// an error there, just empty, so <c>remap --from-git</c> used to report "git recorded no renames" and
    /// rewrite nothing on the very branch that had done the renaming. Silence is the one failure mode a remap
    /// tool must not have, and <c>validate</c> could not catch it afterwards: it resolves a snaplink against
    /// the product root when the working tree misses it, so the moved-away paths still resolved in the main
    /// checkout and the tree looked clean while every one of those links was stale.
    /// </para>
    /// </summary>
    internal static string GitRepoFor(string callerDir, string productRoot) =>
        WorkingTreeRootOf(callerDir) ?? WorkingTreeRootOf(productRoot) ?? productRoot;

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
        if (a.Value("--code-root") is { Length: > 0 } explicitRoot) return CallerPath.Of(explicitRoot);
        var caller = WorkingTreeRootOf(CallerDirectory);
        if (caller is not { Length: > 0 } || PathsEqual(caller, productRoot)) return null;

        // "The caller's tree differs from the product root" describes two completely different situations, and
        // only one of them means "graph the branch I am on". The other is `nfi graph <some-other-repo>` run
        // from here, where the caller's tree is not a worktree of that repo at all — and taking it silently
        // graphed THIS repository into the other one's .product/, complete with a note explaining the wrong
        // thing it had just done. The test that separates them is whether the caller is a linked worktree
        // whose main checkout IS this product root.
        return TryFindMainCheckout(caller, out var callerMain) && PathsEqual(callerMain, productRoot)
            ? caller
            : null;
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
            var path = ResolveMemberPath(outline, link.Class, link.Method);
            if (path is null)
            {
                var what = link.Class is { Length: > 0 } c ? $"{c}{(link.Method is { Length: > 0 } mm ? "." + mm : "")}" : link.Method;
                return (false, $"'{what}' not found in {link.Doc}", []);
            }
            // Both ends off the outline that just located the member - the parse is already paid for.
            (s0, e0) = SourceSpans.BlockOf(outline, lines.Length, path, 0, GraphQuery.BlockScanLines);
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

    /// <summary>The AST path of <paramref name="cls"/>.<paramref name="method"/> in an outline (the class itself
    /// when no method; the top-level function when no class), or null if it isn't declared. A path rather than a
    /// line because the same outline then yields both ends of its span.</summary>
    private static string? ResolveMemberPath(CodeOutline outline, string? cls, string? method)
    {
        if (!string.IsNullOrWhiteSpace(cls))
        {
            var type = outline.Types.FirstOrDefault(t => t.Name == cls);
            if (type is null) return null;
            if (string.IsNullOrWhiteSpace(method)) return type.AstPath;
            return outline.Types.Where(t => t.Name == cls)
                .SelectMany(t => t.Members).FirstOrDefault(mem => mem.Name == method)?.AstPath;
        }
        return outline.TopLevel.FirstOrDefault(mem => mem.Name == method)?.AstPath;
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
        // The bulk form: git already recorded every rename, so a move that shifted N files needs no
        // hand-written mapping. Dispatched on the flag because it takes no <old> <new> positionals.
        if (args.Contains("--from-git")) return RemapFromGit(args);

        if (!TryRead(Specs.Remap, args, out var a, out var root, out var parseCode)) return parseCode;
        if (!TryLoad(root, out var state, out var code)) return code;

        // Standalone, a no-op remap is an answer rather than a failure: nothing referenced that path.
        // Inside batch it IS a failure (ApplyRemap) - there the path comes from a move the author already
        // made, so a miss means the script is wrong and the whole transaction should abort.
        var changed = SnaplinkRemapper.Remap(state, a[0], a[1], a.Value("--class"), a.Value("--method"));
        if (changed == 0)
        {
            Console.WriteLine($"No snaplink referenced '{a[0]}' - nothing remapped.");
            return Clean;
        }
        return SaveAndValidate(state, root, $"Remapped {changed} snaplink(s): {a[0]} -> {a[1]}.", written: LinksAt(state, [a[1]]));
    }

    private static (bool Ok, string Message) ApplyRemap(ProductState s, VerbArgs a)
    {
        var changed = SnaplinkRemapper.Remap(s, a[0], a[1], a.Value("--class"), a.Value("--method"));
        return changed > 0
            ? (true, $"Remapped {changed} snaplink(s): {a[0]} -> {a[1]}.")
            : (false, $"no snaplink referenced '{a[0]}' - nothing to remap");
    }

    /// <summary>
    /// Rewrites every snaplink whose file git says moved within a revision range, as one transaction.
    /// <para>
    /// Moving a directory of files is the usual way snaplinks break en masse, and the mapping needed to fix
    /// them is one git already holds: <c>--diff-filter=R</c> lists every rename it detected. Deriving it
    /// beats hand-writing a batch script, which is both tedious and a place to mistype a path that then
    /// silently remaps nothing.
    /// </para>
    /// A rename git records but no snaplink references is silently fine — most moved files are not linked.
    /// What this cannot know is a file that was <i>deleted</i> and its content folded into another (a merge
    /// or a collapse); those still need an explicit remap naming the survivor.
    /// </summary>
    private static int RemapFromGit(string[] args)
    {
        if (!TryRead(Specs.RemapFromGit, args, out var a, out var root, out var parseCode)) return parseCode;
        if (!TryLoad(root, out var state, out var code)) return code;

        var range = a.Value("--from-git");
        if (string.IsNullOrWhiteSpace(range)) return Usage("--from-git needs a revision range, e.g. v1.4.0..HEAD");

        var repo = GitRepoFor(CallerDirectory, root);
        var log = RunGit(repo, "log", "--diff-filter=R", "--name-status", "--format=", "-M", range!);
        if (log is null)
        {
            Console.Error.WriteLine($"error: could not read git renames for '{range}' (is '{repo}' a git repo, and the range valid?).");
            return Error;
        }

        var renames = new List<(string Old, string New)>();
        foreach (var line in log)
        {
            // R<similarity>\t<old>\t<new>
            if (line.Length == 0 || line[0] != 'R') continue;
            var parts = line.Split('\t');
            if (parts.Length >= 3) renames.Add((parts[1].Replace('\\', '/'), parts[2].Replace('\\', '/')));
        }
        if (renames.Count == 0) { Console.WriteLine($"git recorded no renames in '{range}' - nothing to remap."); return Clean; }

        var dryRun = a.Has("--dry-run");
        var total = 0; var touched = 0; var destinations = new List<string>();
        foreach (var (oldPath, newPath) in renames)
        {
            var changed = SnaplinkRemapper.Remap(state, oldPath, newPath, null, null);
            if (changed == 0) continue;
            total += changed; touched++; destinations.Add(newPath);
            Console.WriteLine($"  {changed} snaplink(s): {oldPath} -> {newPath}");
        }

        if (total == 0)
        {
            Console.WriteLine($"{renames.Count} rename(s) in '{range}', none of them referenced by a snaplink - nothing remapped.");
            return Clean;
        }
        if (dryRun)
        {
            Console.WriteLine($"Dry run - {total} snaplink(s) across {touched} renamed file(s) would be rewritten, nothing written.");
            return Clean;
        }
        return SaveAndValidate(state, root, $"Remapped {total} snaplink(s) across {touched} renamed file(s) from git '{range}'.",
                               written: LinksAt(state, destinations));
    }

    /// <summary>Runs git in <paramref name="repo"/> and returns its stdout lines, or null if it failed.</summary>
    private static List<string>? RunGit(string repo, params string[] args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = repo,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);

            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 ? [.. stdout.Split('\n').Select(l => l.TrimEnd('\r'))] : null;
        }
        catch { return null; }   // git absent, or not a repo
    }

    // ── scan-tests: harvest declared test↔node coverage into the manifest the Integrity page reconciles ──

    private static int ScanTests(string[] args)
    {
        if (!TryRead(Specs.ScanTests, args, out var a, out var root, out var parseCode)) return parseCode;
        if (!Directory.Exists(root)) return Usage($"no such directory: {root}");
        if (!ProductStore.Exists(root)) { Console.Error.WriteLine($"error: no .product/ under {root}."); return Error; }

        if (a.Has("--suggest-attributes")) return SuggestAttributes(root);

        // Caller-relative, like every other path typed on the command line: "--test-dll bin/x64/Debug/T.dll"
        // is how this is written, and the resident process is not standing in the caller's directory.
        var explicitDlls = a.All("--test-dll").Select(CallerPath.Of).ToList();
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

    /// <summary>
    /// The coverage manifest, rescanned first when the build has moved past it. Done without asking: the
    /// scan is seconds, and the alternative is a release gate reconciling against whatever the last
    /// remembered build happened to say. Derived state that relies on someone remembering to refresh it is
    /// exactly the state that goes stale.
    /// </summary>
    private static TestCoverageManifest? RefreshedCoverage(string root, ProductStore store, List<string> testDlls)
    {
        var manifest = store.LoadTestCoverage();

        // No manifest is not staleness: the coverage checks are skipped entirely, as on a clean CI checkout.
        if (manifest is null) return null;
        if (!TestCoverageCollector.NeedsRescan(manifest, testDlls, root)) return manifest;

        try
        {
            var rescanned = TestCoverageCollector.Collect(testDlls, root, DateTime.Now.ToString("o"));
            store.SaveTestCoverage(rescanned);
            return rescanned;
        }
        catch (Exception ex)
        {
            // A scan that cannot run leaves the stored manifest in place rather than failing the whole
            // validation — the snaplink half of the report is still worth having.
            Console.Error.WriteLine($"warn: could not refresh the coverage manifest ({ex.Message}) — "
                                  + "reconciling against the stored one.");
            return manifest;
        }
    }

    /// <summary>
    /// Gates each test project whose compiled output is behind its own source. Rescanning neither fixes
    /// nor detects this — the scan would re-read the same stale assembly and report its declarations as
    /// confidently as ever — which is why it is a separate check rather than part of the refresh above.
    /// </summary>
    private static void FlagStaleTestBuilds(string root, List<string> testDlls, IntegrityReport report)
    {
        foreach (var stale in TestBuildFreshness.Check(testDlls, root))
        {
            var shown = string.Join(", ", stale.ChangedFiles.Take(3));
            var more  = stale.ChangedFiles.Count > 3 ? $" +{stale.ChangedFiles.Count - 3} more" : string.Empty;

            report.Issues.Add(new IntegrityIssue
            {
                NodeId    = string.Empty,
                NodeTitle = string.Empty,
                Concern   = TestCoverageReconciler.TestsConcern,
                Index     = -1,
                Kind      = IntegrityKind.StaleCoverageBuild,
                Detail    = $"{stale.Assembly} is behind its own source ({shown}{more}) — its coverage was read from an older compile, so validate cannot answer for it yet. Build it, then re-run",
                Link      = new Snaplink()
            });
        }
    }

    private static List<string> DiscoverTestDlls(string root)
    {
        var testsDir = Path.Combine(root, "src", "Nexaflow.Tests");
        var result = new List<string>();
        if (!Directory.Exists(testsDir)) return result;

        // Every test project there is, found by its .csproj — NOT a hard-coded list. The list this replaced
        // named three projects and was written before the feature suite was split into .Viewers/.WindowsOS
        // /.Architecture and before .UIJourneys existed, so it silently scanned three assemblies out of ten
        // and the manifest was built from under a third of the declarations. A discovery rule that has to be
        // edited whenever a suite is added will go stale again the next time one is.
        foreach (var proj in Directory.EnumerateFiles(testsDir, "*.csproj", SearchOption.AllDirectories)
                                      .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var name   = Path.GetFileNameWithoutExtension(proj);
            var binDir = Path.Combine(Path.GetDirectoryName(proj)!, "bin");
            if (!Directory.Exists(binDir)) continue;

            // Newest wins: a repo can hold Debug and Release, x64 and a stale pre-pin AnyCPU tree.
            var newest = Directory.EnumerateFiles(binDir, name + ".dll", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}",
                                        StringComparison.OrdinalIgnoreCase))   // reference assemblies carry no IL
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
        var id = a.Positionals.Count > 0 ? a[0] : string.Empty; var newParentId = a[1];
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
        var id = a.Positionals.Count > 0 ? a[0] : string.Empty;
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

        // No --no-promote: validate does not promote. It reads, and says what is waiting to be folded in.
        public static readonly VerbSpec Validate = new("validate", 0, None, ["--json", "--save", "--main"],
            "validate [<root>] [--json] [--save] [--main]");
        public static readonly VerbSpec Find = new("find", 1, None, ["--json"],
            "find <term> [<root>] [--json]");
        public static readonly VerbSpec Query = new("query", 0, ["--under", "--concern", "--status"],
            ["--leaf", "--panel", "--unbacked", "--json"],
            "query [<root>] [--under <id>] [--concern <tag>] [--status <s>] [--leaf|--panel] [--unbacked] [--json]");
        public static readonly VerbSpec RemapFromGit = new("remap", 0, ["--from-git"], ["--dry-run"],
            "remap --from-git <rev-range> [<root>] [--dry-run]");
        public static readonly VerbSpec Describe = new("describe", 1, None, ["--json", "--code"],
            "describe <node-id>[,<node-id>...] [<root>] [--json] [--code]");
        // The node id is optional: "show me the tree" is a complete request, and defaulting to the roots is
        // also what makes `nfi tree <some-repo>` mean the obvious thing instead of searching this repo for a
        // node named after a directory.
        public static readonly VerbSpec Tree = new("tree", 1, ["--depth"], ["--full", "--json"],
            "tree [<node-id>] [<root>] [--depth <n>] [--full] [--json]", MinPositionals: 0);
        public static readonly VerbSpec Diff = new("diff", 0, ["--from"], None,
            "diff [<root>] [--from <version>]");
        // remap is a mutation too: a move usually breaks several paths at once, and batch is the only way
        // to land them as one validated, all-or-nothing transaction. So this spec is reused via .InBatch.
        public static readonly VerbSpec Remap = new("remap", 2, ["--class", "--method"], None,
            "remap <old-path> <new-path> [<root>] [--class <name>] [--method <name>]");
        public static readonly VerbSpec ScanTests = new("scan-tests", 0, ["--test-dll"], ["--suggest-attributes"],
            "scan-tests [<root>] [--test-dll <path>]... [--suggest-attributes]");
        public static readonly VerbSpec Pending = new("pending", 0, None, ["--all"],
            "pending [<root>] [--all]");
        public static readonly VerbSpec Promote = new("promote", 0, ["--branch"], ["--dry-run", "--no-commit"],
            "promote [<root>] [--branch <name>] [--dry-run] [--no-commit]");
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
        public static readonly VerbSpec RemoveSnaplink = new("remove-snaplink", 1,
            ["--concern", "--index", "--type", "--doc", "--class", "--method", "--target"], ["--all"],
            "remove-snaplink <node-id> [<root>] [--concern <tag>] "
            + "[--type <t>] [--doc <p>] [--class <c>] [--method <m>] [--target <id>] | [--index <n>] | --all");
        public static readonly VerbSpec SetSnaplink = new("set-snaplink", 1,
            ["--index", "--concern", "--doc", "--class", "--method", "--ast", "--target", "--title-path", "--status", "--clear", "--expect"], None,
            "set-snaplink <node-id> --index <n> [<root>] [--concern <tag>] "
            + "[--doc <p>] [--class <c>] [--method <m>] [--ast <a>] [--target <t>] [--title-path a>b] [--status <s>] "
            + "[--clear <f,f>] [--expect <text>]");
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
        public static readonly VerbSpec GraphOrphans = new("graph orphans", 0,
            ["--type", "--limit", "--under"], ["--all"],
            "graph orphans [<root>] [--type type|member] [--under <path>] [--all] [--limit N]");
        public static readonly VerbSpec GraphSearch = new("graph search", 1, ["--type", "--limit"], ["--refresh"],
            "graph search <term> [<root>] [--type <t>] [--limit N] [--refresh]");
        public static readonly VerbSpec GraphList = new("graph list", 0,
            ["--type", "--community", "--file", "--limit"], ["--unparsed", "--refresh"],
            "graph list [<root>] [--type <t>] [--community N] [--file <f>] [--unparsed] [--limit N] [--refresh]");
        public static readonly VerbSpec GraphPaths = new("graph paths", 2,
            ["--hops", "--limit"], ["--undirected"],
            "graph paths <from-id> <to-id> [<root>] [--hops N] [--undirected] [--limit N]");
        public static readonly VerbSpec GraphRank = new("graph rank", 0,
            ["--by", "--type", "--under", "--limit"], None,
            "graph rank [<root>] [--by fanin|fanout] [--type <t>] [--under <path>] [--limit N]");
        public static readonly VerbSpec GraphNode = new("graph node", 1, ["--limit"], ["--refresh"],
            "graph node <id> [<root>] [--limit N] [--refresh]");
        public static readonly VerbSpec GraphWalk = new("graph walk", 1, ["--hops", "--limit", "--types"], ["--refresh"],
            "graph walk <id> [<root>] [--hops N] [--types a,b] [--limit N] [--refresh]");
        public static readonly VerbSpec GraphContext = new("graph context", 1, ["--lines", "--limit"], ["--main", "--refresh"],
            "graph context <id> [<root>] [--lines N] [--limit N] [--main] [--refresh]");
        public static readonly VerbSpec GraphGrep = new("graph grep", 1,
            ["--from", "--hops", "--scope", "--mode", "--type", "--limit", "--scan-cap"], ["--main", "--refresh"],
            "graph grep <pattern> [<root>] [--from <id>] [--hops N | --scope owned] [--mode index|content] [--type t] [--limit N] [--scan-cap N] [--main] [--refresh]");
        public static readonly VerbSpec GraphCode = new("graph code", 1, ["--lines"], ["--main", "--refresh"],
            "graph code <id> [<root>] [--lines A-B] [--main] [--refresh]");
        public static readonly VerbSpec GraphEdit = new("graph edit", 2,
            ["--text", "--text-escaped", "--file", "--to", "--expect", "--find", "--find-escaped", "--find-file"],
            ["--stdin", "--find-stdin", "--with-trivia", "--regex", "--all", "--dry-run", "--main", "--no-refresh",
             "--show", "--quiet"],
            "graph edit <op> <node-id> [<root>] [--text T | --text-escaped T | --file F | --stdin] "
          + "[--to NAME] [--find S | --find-escaped S | --find-file F | --find-stdin] [--regex] [--all] "
          + "[--expect S] [--with-trivia] "
          + "[--dry-run] [--main] [--show] [--quiet]");
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

        // A directory where an id belongs is a misplaced <root>, and saying so beats the alternative. Left
        // alone, `nfi tree <some-repo>` reads the path as a node id, resolves <root> from the CURRENT
        // directory, searches a DIFFERENT repository's tree, and reports "no node '<some-repo>'" — an answer
        // that is true, useless, and about the wrong repository. One rule at the single parse chokepoint, so
        // every verb behaves the same rather than `tree` growing a special case.
        //
        // The test is `Directory.Exists`, not "looks like a path": real ids contain slashes all the time
        // (code:src/Foo.cs#T:Bar), and none of them names a directory on disk.
        for (var i = 0; i < parsed.Positionals.Count; i++)
        {
            if (!CallerPath.IsDirectory(parsed.Positionals[i])) continue;

            var fixedArgs = spec.TakesRoot && parsed.Root is null
                ? $"  did you mean:  nfi {spec.Verb} <{Slot(spec, i)}> {parsed.Positionals[i]}"
                : $"  <{Slot(spec, i)}> takes an id, not a path — see: nfi {spec.Verb}";
            Console.Error.WriteLine($"error: {spec.Verb}: '{parsed.Positionals[i]}' is a directory, not a <{Slot(spec, i)}>.");
            Console.Error.WriteLine($"  usage: {spec.Usage}");
            Console.Error.WriteLine(fixedArgs);
            code = Error;
            return false;
        }

        root = ResolveProductRoot(parsed.Root ?? ".");
        code = Clean;
        return true;
    }

    /// <summary>The name a verb's usage line gives its i-th positional, so the error can point at the actual
    /// slot ("node-id", "from-id") instead of saying "argument 1".</summary>
    private static string Slot(VerbSpec spec, int index)
    {
        var names = Regex.Matches(spec.Usage, @"<([a-z][a-z0-9-]*)>")
                         .Select(m => m.Groups[1].Value)
                         .Where(n => n != "root")
                         .ToList();
        return index < names.Count ? names[index] : "argument";
    }

    // ── typed tree mutations (set-status / set-concern / add-snaplink / set-snaplink / set-node / doctor) ──
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

    // ── Snaplinks a branch has changed but not merged ───────────────────────

    /// <summary>The branch whose snaplink changes this invocation belongs to, or null in the main checkout.
    /// Snaplinks only go to a pending set when there is a branch for them to belong to.</summary>
    private static string? PendingBranch(string root)
    {
        var here = WorkingTreeRootOf(CallerDirectory);
        return here is { Length: > 0 } && !PathsEqual(here, root) ? ProductGit.CurrentBranch(here) : null;
    }

    /// <summary>
    /// The working tree a pending set belongs in — the caller's own, not the product root.
    /// <para>
    /// This is the whole mechanism: the file has to be committable alongside the code it describes, so it
    /// has to be written into the tree the branch is checked out in. Writing it beside the shared tree would
    /// leave it on the wrong side of the merge and reintroduce the problem it exists to solve.
    /// </para>
    /// </summary>
    private static string PendingRoot(string productRoot) =>
        WorkingTreeRootOf(CallerDirectory) is { Length: > 0 } here ? here : productRoot;

    private static PendingStore PendingStoreFor(string root) =>
        new(PendingRoot(root), ExportDirFor(root));

    /// <summary>
    /// Records the link sets <paramref name="touched"/> names into this branch's pending set, so they travel
    /// with the pull request instead of being written into the shared tree before the code they describe
    /// exists anywhere else.
    /// </summary>
    private static PendingSnaplinks CapturePending(ProductState state, string root, string branch,
                                                   IReadOnlyList<(string NodeId, string? Concern)> touched)
    {
        var store   = PendingStoreFor(root);
        var pending = store.Load(branch);

        foreach (var (nodeId, concern) in touched)
        {
            if (!state.Nodes.TryGetValue(nodeId, out var node)) continue;

            if (concern is { Length: > 0 })
            {
                var link = node.Concerns?.FirstOrDefault(c => string.Equals(c.Tag, concern, StringComparison.Ordinal));
                pending.Capture(nodeId, concern, link?.Snaplinks ?? []);
            }
            else pending.Capture(nodeId, null, node.Snaplinks ?? []);
        }

        store.Save(pending);
        Console.Error.WriteLine(
            $"snaplinks: recorded for branch '{branch}' in {ExportDirFor(root)}/{PendingStore.FolderName}/ — "
          + "commit it with your change and it merges into the shared tree with the PR. `nfi pending` to review.");
        return pending;
    }

    /// <summary>
    /// Puts the named link sets back to what the shared tree holds, so a state carrying a branch's deferred
    /// links can still be written without carrying them along. A node that does not exist in the shared tree
    /// yet is being created by this same run, so it lands with no links — its links are in the pending set,
    /// waiting for the branch that creates the files they name.
    /// <para>
    /// The caller passes the whole of the branch's <see cref="PendingSnaplinks.Targets"/>, never just the sets
    /// the command in hand named: the overlay went on whole, so it has to come off whole.
    /// </para>
    /// </summary>
    private static void RestoreSharedLinks(ProductState state, ProductState shared,
                                           IReadOnlyList<(string NodeId, string? Concern)> touched)
    {
        foreach (var (nodeId, concern) in touched)
        {
            if (!state.Nodes.TryGetValue(nodeId, out var node)) continue;
            shared.Nodes.TryGetValue(nodeId, out var before);

            if (concern is { Length: > 0 })
            {
                if (node.Concerns?.FirstOrDefault(c => string.Equals(c.Tag, concern, StringComparison.Ordinal))
                    is not { } target) continue;
                target.Snaplinks =
                    [.. before?.Concerns?.FirstOrDefault(c => string.Equals(c.Tag, concern, StringComparison.Ordinal))
                               ?.Snaplinks ?? []];
            }
            else node.Snaplinks = [.. before?.Snaplinks ?? []];
        }
    }

    /// <summary>
    /// The pending link sets that apply where the caller stands: from a worktree this branch's own, and in the
    /// main checkout every set that has arrived — which is what a merge delivers, the file's presence there
    /// being the merged signal.
    /// <para>
    /// Reading them is all this does. <c>validate</c> overlays them to answer for the tree as it will stand,
    /// and <see cref="Promote"/> is the one verb that folds them in and commits — deliberately, because a
    /// command that writes to git as a side effect of being asked a question moves whatever branch its caller
    /// happened to be standing on.
    /// </para>
    /// </summary>
    private static IReadOnlyList<PendingSnaplinks> PendingHere(string root) =>
        PendingBranch(root) is { } branch
            ? [PendingStoreFor(root).Load(branch)]
            : PendingStoreFor(root).All();

    /// <summary>
    /// What this branch has changed and not yet merged — the review step before committing a pending set.
    /// </summary>
    private static int Pending(string[] args)
    {
        if (!TryRead(Specs.Pending, args, out var a, out var root, out var parseCode)) return parseCode;
        if (!TryLoad(root, out var state, out var code, applyPending: false)) return code;

        var store = PendingStoreFor(root);
        var all   = a.Has("--all") ? store.All()
                  : PendingBranch(root) is { } branch ? [store.Load(branch)] : store.All();

        var sets = all.Where(p => !p.IsEmpty).ToList();
        if (sets.Count == 0)
        {
            Console.WriteLine(PendingBranch(root) is { } b
                ? $"Nothing pending on '{b}' — no snaplink changes waiting to merge."
                : "Nothing pending — no branch has snaplink changes waiting to merge.");
            return Clean;
        }

        foreach (var pending in sets)
        {
            Console.WriteLine($"{pending.Branch}  ({pending.ChangedSets} link set(s) across "
                            + $"{pending.Nodes.Count} node(s))   {store.PathFor(pending.Branch)}");

            foreach (var nodeId in pending.TouchedNodes)
            {
                var entry   = pending.Nodes[nodeId];
                var present = state.Nodes.ContainsKey(nodeId);
                Console.WriteLine($"  {nodeId}{(present ? "" : "   [not in the shared tree — promote will skip it]")}");

                if (entry.Links is { } links) PrintPendingLinks("node", links, state, nodeId, null);
                foreach (var (tag, concernLinks) in entry.Concerns ?? [])
                    PrintPendingLinks(tag, concernLinks, state, nodeId, tag);
            }
        }

        Console.WriteLine();
        Console.WriteLine("Commit these files with your change; they merge into the shared tree with the PR.");
        return Clean;
    }

    /// <summary>One link set, marked against what the shared tree currently holds for it.</summary>
    private static void PrintPendingLinks(string label, IReadOnlyList<Snaplink> links, ProductState state,
                                          string nodeId, string? concern)
    {
        var current = state.Nodes.TryGetValue(nodeId, out var node)
            ? concern is { Length: > 0 }
                ? node.Concerns?.FirstOrDefault(c => c.Tag == concern)?.Snaplinks
                : node.Snaplinks
            : null;

        Console.WriteLine($"    [{label}] {links.Count} link(s), replacing {current?.Count ?? 0} in the shared tree");
        foreach (var link in links) Console.WriteLine($"      + {link.Display}");
    }

    /// <summary>
    /// Folds pending sets into the shared tree and removes them. In the main checkout a pending file can
    /// only be there because the branch that wrote it merged, so its presence is the signal — no need to
    /// know which worktree or machine produced it.
    /// </summary>
    private static int Promote(string[] args)
    {
        if (!TryRead(Specs.Promote, args, out var a, out var root, out var parseCode)) return parseCode;

        // A set in a worktree is that branch's own work, not something that has merged — folding it into the
        // shared tree from here is the mistake this whole split exists to prevent, and it would commit the
        // deletion onto the branch as well. The signal that a set has merged is its arrival in the main checkout.
        if (PendingBranch(root) is { } onBranch)
        {
            Console.Error.WriteLine(
                $"error: promote folds MERGED link sets into the shared tree, and this is branch '{onBranch}' in a "
              + $"worktree — its set has not merged. Run it from the main checkout ({root}) once the PR has landed.");
            return Error;
        }

        if (!TryLoad(root, out var state, out var code)) return code;

        var store = PendingStoreFor(root);
        var sets  = (a.Value("--branch") is { Length: > 0 } only
                        ? store.All().Where(p => p.Branch == only)
                        : store.All()).ToList();

        if (sets.Count == 0)
        {
            Console.WriteLine("Nothing to promote — no pending snaplink sets are present here.");
            return Clean;
        }

        var applied = sets.Sum(p => p.ApplyTo(state));
        if (a.Has("--dry-run"))
        {
            Console.WriteLine($"dry run — would apply {applied} link set(s) from {sets.Count} branch(es) "
                            + "and delete their pending files. Nothing written.");
            return Clean;
        }

        SaveTree(root, new ProductStore(root), state);

        var paths = store.RelativePaths(PendingRoot(root), sets);
        foreach (var pending in sets) store.Delete(pending.Branch);

        Console.WriteLine($"Promoted {applied} link set(s) from {sets.Count} branch(es) into the shared tree.");

        if (!a.Has("--no-commit"))
        {
            var (ok, error) = new ProductGit(PendingRoot(root)).CommitPaths(
                paths, $"Product: consolidate snaplinks from {string.Join(", ", sets.Select(s => s.Branch))}");
            Console.WriteLine(ok
                ? "Committed the consolidated pending file(s) as merged."
                : $"note: the pending file(s) were removed but not committed ({error}).");
        }

        var report = SnaplinkValidator.Validate(state, root, FileRootsFor(root));
        new ProductStore(root).SaveIntegrity(report);
        Console.WriteLine(report.IsClean
            ? $"Snaplinks OK — scanned {report.ScannedSnaplinks}."
            : $"{report.IssueCount} broken snaplink(s) — run: validate .");
        return Clean;
    }

    /// <summary>The committed export directory this product uses.</summary>
    private static string ExportDirFor(string root)
    {
        try { return new ProductStore(root).Load().Product.ExportDir is { Length: > 0 } d ? d : "docs/product"; }
        catch { return "docs/product"; }
    }

    /// <summary>
    /// The write-time gate: every link an operation produced, checked against the caller's own working tree
    /// before anything is saved. Returns the refusal text, or null when the write may proceed.
    /// <para>
    /// Verbs used to write first and validate afterwards, so a snaplink naming a file, class, method, heading
    /// or node id that does not exist was accepted, saved, and only reported on — <c>remap</c> would rewrite
    /// twenty links onto a path that was never there and report success. The check is the operation's own dry
    /// run: it is applied in memory, what it actually produced is checked, and a broken result is refused
    /// rather than persisted for a later scan to find.
    /// </para>
    /// <para>
    /// Only links the operation touched are checked, so the tree's existing breakage neither blocks a write
    /// nor is silently adopted by it. <see cref="LinkVerdict.Unverifiable"/> passes: a .txt with no grammar or
    /// an unreadable file was never proof of anything, and the bar here is proving a link broken.
    /// </para></summary>
    private static string? RefusalFor(ProductState state, string root, IEnumerable<Snaplink> written)
    {
        var roots  = CallerFileRoots(root);
        var nodeIds = state.Nodes.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var link in written)
        {
            var check = SnaplinkValidator.CheckLink(link, root, roots, nodeIds);
            if (check.Verdict != LinkVerdict.Broken) continue;
            return $"{check.Detail} — nothing was written. The snaplink must resolve in this working tree "
                 + "(create the target first, or point the link at what is actually there).";
        }
        return null;
    }

    /// <summary>
    /// Every link in the tree now pointing at one of <paramref name="docs"/> — what a remap produced, gathered
    /// by its destination because the rewrite is precisely what made them all name it. A link that already
    /// pointed there is swept up too, which costs one sound check and keeps the gather honest about what the
    /// tree now claims.
    /// </summary>
    private static IEnumerable<Snaplink> LinksAt(ProductState state, IEnumerable<string> docs)
    {
        var wanted = docs.Select(d => d.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return state.Nodes.Values
            .SelectMany(n => (n.Snaplinks ?? []).Concat((n.Concerns ?? []).SelectMany(c => c.Snaplinks ?? [])))
            .Where(l => l.Doc is { } d && wanted.Contains(d.Replace('\\', '/')));
    }

    /// <summary>
    /// Where a write-time check looks for a file: the caller's working tree ALONE when it is a worktree of this
    /// product root. <see cref="FileRootsFor"/> falls back to the product root as well, which is right for
    /// reporting on a branch whose work has not merged — and wrong here, because a file this branch has moved
    /// away still resolves through the main checkout, which is the one case the gate exists to catch.
    /// </summary>
    private static string[] CallerFileRoots(string productRoot)
    {
        var caller = WorkingTreeRootOf(CallerDirectory);
        if (caller is not { Length: > 0 } || PathsEqual(caller, productRoot)) return [productRoot];
        return TryFindMainCheckout(caller, out var callerMain) && PathsEqual(callerMain, productRoot)
            ? [caller]
            : [productRoot];
    }

    /// <summary>Persist the mutated tree via the canonical serializer, re-validate, and print the outcome —
    /// the same "edit then show the effect" contract as remap/add-node.</summary>
    private static int SaveAndValidate(ProductState state, string root, string message,
                                       IReadOnlyList<(string NodeId, string? Concern)>? touchedLinks = null,
                                       IEnumerable<Snaplink>? written = null)
    {
        // remap rewrites en masse — twenty links over as many nodes — so no argument names one link and only
        // what it produced is worth checking. Checked here, before the write, so a rewrite onto a path that is
        // not there is refused rather than saved and reported on afterwards.
        if (written is not null && RefusalFor(state, root, written) is { } refusal)
        {
            Console.Error.WriteLine($"error: {refusal}");
            return Error;
        }

        var store = new ProductStore(root);

        // A node is a plan, and the shared tree is deliberately forward-looking about those — so it is
        // written at once. A snaplink is a claim that a file exists and contains something, and from an
        // unmerged branch that claim is not true anywhere but here, so it is recorded against the branch
        // instead. Recording it is all that happens here: SaveTree keeps the shared tree free of every set
        // this branch has deferred, this run's included, so one batch can add a node and change a snaplink
        // and have the node land while the link stays with the branch.
        if (touchedLinks is { Count: > 0 } && PendingBranch(root) is { } branch)
            CapturePending(state, root, branch, touchedLinks);

        SaveTree(root, store, state);

        // Validated against the in-memory state either way, so a branch sees its own links resolved against
        // its own tree rather than the shared tree's older idea of them.
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
    /// <param name="touchesSnaplinks">True for the verbs that change a node's links rather than the node
    /// itself — they all name the node positionally and the concern with <c>--concern</c>, so that is enough
    /// to record what a branch changed.</param>
    private static int RunOne(VerbSpec spec, string[] args,
                              Func<ProductState, VerbArgs, (bool Ok, string Message)> apply,
                              bool touchesSnaplinks = false)
    {
        if (!VerbArgs.TryParse(spec, args, out var parsed, out var parseError)) return VerbUsage(parseError);
        var root = ResolveProductRoot(parsed.Root ?? ".");
        if (!TryLoad(root, out var state, out var code)) return code;
        var (ok, msg) = apply(state, parsed);
        if (!ok) { Console.Error.WriteLine($"error: {msg}"); return Error; }

        // A snaplink verb answers for the target its arguments named, before any of it is written.
        if (WrittenBy(state, spec.Verb, parsed) is { } link && RefusalFor(state, root, [link]) is { } refusal)
        {
            Console.Error.WriteLine($"error: {refusal}");
            return Error;
        }

        var touched = touchesSnaplinks && parsed.Positionals.Count > 0
            ? new[] { (parsed[0], parsed.Value("--concern")) }
            : null;
        return SaveAndValidate(state, root, msg, touched);
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

    private static int SetSnaplink(string[] args) => RunOne(Specs.SetSnaplink, args, ApplySetSnaplink, touchesSnaplinks: true);

    private static (bool Ok, string Message) ApplySetSnaplink(ProductState s, VerbArgs a)
    {
        var id = a.Positionals.Count > 0 ? a[0] : string.Empty;
        if (!s.Nodes.ContainsKey(id)) return (false, $"no node '{id}' (try: find)");
        if (a.Value("--index") is not { } raw) return (false, "set-snaplink needs --index <n> (see: describe " + id + ")");
        if (!int.TryParse(raw, out var index)) return (false, $"--index must be a number (got '{raw}')");

        Status? status = null;
        if (a.Value("--status") is { } st)
        {
            if (!TryParseStatus(st, out var sv)) return (false, $"unknown --status '{st}'");
            status = sv;
        }

        var clear = a.Value("--clear") is { Length: > 0 } c
            ? c.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : [];

        var fields = new (string Opt, Action<Snaplink, string> Assign)[]
        {
            ("--doc",        (l, v) => l.Doc = v),
            ("--class",      (l, v) => l.Class = v),
            ("--method",     (l, v) => l.Method = v),
            ("--ast",        (l, v) => l.Ast = v),
            ("--target",     (l, v) => l.Target = v),
            // The one field add-snaplink could write and this could not, which is why repairing a markdown
            // heading path meant removing the link and adding it back — losing its status and its position to
            // fix the one thing about it that had moved.
            ("--title-path", (l, v) => l.TitlePath =
                 [.. v.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)]),
        };

        // What this call changes, read off the arguments rather than discovered while assigning them. Deciding
        // it inside the setter meant a setter that never ran (no such index) reported "nothing to change" —
        // the caller's real mistake hidden behind a complaint about the one thing they had got right.
        var given = fields.Where(f => a.Value(f.Opt) is not null).ToList();
        if (given.Count == 0 && clear.Length == 0 && status is null)
            return (false, "nothing to change - pass a field ("
                         + string.Join('/', fields.Select(f => f.Opt)) + "/--status) or --clear <f,f>");

        var concern = a.Value("--concern");
        var where = concern is null ? $"'{id}'" : $"'{id}' concern '{concern}'";

        // --index is a position, not an identity: anything that adds or removes a link renumbers the rest, and
        // the listing an index was read from is always older than the edit. --expect pins the edit to what was
        // read — the same guard `graph edit --expect` gives a declaration — so a moved link is refused rather
        // than silently rewritten. Required inside a batch, where a whole listing's worth of indices is in play.
        if (a.Value("--expect") is { Length: > 0 } expect)
        {
            if (!ProductTreeOps.TryGetSnaplink(s, id, index, concern, out var current) || current is null)
                return (false, $"no snaplink #{index} on {where}");
            if (!SnaplinkHolds(current, expect))
                return (false, $"snaplink #{index} on {where} no longer contains '{expect}' "
                             + $"(it is now {current.Display}) — re-read it with: describe {id}");
        }

        return ProductTreeOps.SetSnaplink(s, id, index, concern, link =>
        {
            foreach (var (opt, assign) in given) assign(link, a.Value(opt)!);
            if (status is not null) link.Status = status;
        }, clear)
            ? (true, $"Updated snaplink #{index} on {where}.")
            : (false, $"no snaplink #{index} on {where}, or an unknown --clear field "
                    + "(class|method|ast|doc|target|title-path|status)");
    }

    /// <summary>Whether <paramref name="expect"/> still appears in any field a caller could have read the link
    /// by — so the text quoted back from a listing (a doc path, a class, the ast that no longer resolves) is
    /// enough to pin an index-addressed edit.</summary>
    private static bool SnaplinkHolds(Snaplink link, string expect) =>
        new[] { link.Doc, link.Class, link.Method, link.Ast, link.Target, link.Type }
            .Any(f => f is not null && f.Contains(expect, StringComparison.Ordinal));

    private static int RemoveSnaplink(string[] args) => RunOne(Specs.RemoveSnaplink, args, ApplyRemoveSnaplink, touchesSnaplinks: true);

    private static (bool Ok, string Message) ApplyRemoveSnaplink(ProductState s, VerbArgs a)
    {
        var id = a.Positionals.Count > 0 ? a[0] : string.Empty;
        if (!s.Nodes.ContainsKey(id)) return (false, $"no node '{id}' (try: find)");

        int? index = null;
        if (a.Value("--index") is { } raw)
        {
            if (!int.TryParse(raw, out var i)) return (false, $"--index must be a number (got '{raw}')");
            index = i;
        }

        var match = new SnaplinkFilter(a.Value("--type"), a.Value("--doc"), a.Value("--class"),
                                       a.Value("--method"), a.Value("--target"));
        var all = a.Has("--all");
        var concern = a.Value("--concern");
        var where = concern is null ? $"'{id}'" : $"'{id}' concern '{concern}'";

        // Two ways to name the same link, and they disagree the moment anything reorders the list. Refuse
        // rather than pick one, so a script cannot quietly delete the entry next to the one it meant.
        if (index is not null && !match.IsEmpty)
            return (false, "--index and the --type/--doc/--class/--method/--target matchers are alternatives - use one");
        if (all && (index is not null || !match.IsEmpty))
            return (false, "--all removes every link on the node - it takes no --index and no matcher");

        // Naming nothing used to mean "all of them". It reads as a sensible default and it cost somebody four
        // links: the call that wipes the set looks exactly like the call that would have removed one, so a
        // mis-named matcher is indistinguishable from meaning all. Say --all, or name what you meant.
        if (!all && index is null && match.IsEmpty)
            return (false, $"remove-snaplink needs to know which link on {where}: --index <n> (see: describe {id}), "
                         + "a matcher (--type/--doc/--class/--method/--target), or --all to remove every one");

        var removed = all
            ? ProductTreeOps.ClearSnaplinks(s, id, concern)
            : ProductTreeOps.RemoveSnaplink(s, id, concern, index, match);
        var how = all ? " (all of them)"
                : match.IsEmpty ? $" at index {index}"
                : $" matching {Describe(match)}";
        return removed > 0
            ? (true, $"Removed {removed} snaplink(s) from {where}{how}.")
            : (false, $"no matching snaplink to remove on {where}{how}");
    }

    private static string Describe(SnaplinkFilter f) => string.Join(" ", new[]
    {
        f.Type   is null ? null : $"type={f.Type}",
        f.Doc    is null ? null : $"doc={f.Doc}",
        f.Class  is null ? null : $"class={f.Class}",
        f.Method is null ? null : $"method={f.Method}",
        f.Target is null ? null : $"target={f.Target}",
    }.Where(x => x is not null));

    private static int AddSnaplink(string[] args) => RunOne(Specs.AddSnaplink, args, ApplyAddSnaplink, touchesSnaplinks: true);

    private static (bool Ok, string Message) ApplyAddSnaplink(ProductState s, VerbArgs a)
    {
        var id = a.Positionals.Count > 0 ? a[0] : string.Empty;
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
        var id = a.Positionals.Count > 0 ? a[0] : string.Empty;
        if (!s.Nodes.ContainsKey(id)) return (false, $"no node '{id}' (try: find)");
        if (a.Value("--title") is null && a.Value("--desc") is null && a.Value("--note") is null)
            return (false, "set-node needs at least one of --title / --desc / --note");
        ProductTreeOps.EditNode(s, id, a.Value("--title"), a.Value("--desc"), a.Value("--note"));
        return (true, $"Edited '{id}'.");
    }

    // ── batch: apply a whole script of instructions in one load/save/validate (transactional) ──

    /// <summary>
    /// Dispatches one batch instruction. <paramref name="root"/> is the product root the run resolves targets
    /// against; given one, an instruction that writes a snaplink must also produce a link that resolves, so a
    /// bad target is refused as that line rather than saved for a later scan to find. Null skips the check —
    /// for a caller with no tree on disk to check against.
    /// </summary>
    internal static (bool Ok, string Message) ApplyOne(ProductState state, string[] args, string? root = null)
    {
        // Parse against the BATCH form of the spec (no trailing <root> — the run has one), then run the
        // same core the standalone verb uses.
        (bool Ok, string Message) Parsed(
            VerbSpec spec, string[] rest, Func<ProductState, VerbArgs, (bool, string)> apply) =>
            VerbArgs.TryParse(spec.InBatch, rest, out var parsed, out var error)
                ? apply(state, parsed)
                : (false, error);

        // set-snaplink addresses by --index, and a batch is where an index is least trustworthy: an
        // add-snaplink or remove-snaplink earlier in the same script renumbers every later link on that list,
        // so a line written against the pre-batch listing would edit a different link and still report success.
        // Requiring --expect makes each line self-verifying instead of leaving the ordering to the author.
        (bool Ok, string Message) SetSnaplinkLine(string[] rest) =>
            rest.Contains("--expect")
                ? Parsed(Specs.SetSnaplink, rest, ApplySetSnaplink)
                : (false, "set-snaplink needs --expect <text> inside a batch — --index is a position, and an "
                        + "add/remove-snaplink on the same list renumbers it. Pin the line to what you read "
                        + "(the ast, doc or class the listing showed).");

        // Applied, then checked, then rolled back if the check refuses it — the instruction's own dry run, so a
        // refused line leaves the tree exactly as it found it instead of relying on the caller to discard it.
        var list   = root is null ? null : LinkTargetOf(args) is { } t ? ProductTreeOps.SnaplinksOf(state, t.NodeId, t.Concern) : null;
        var before = list?.Select(l => l.Copy()).ToList();

        (bool Ok, string Message) result = args switch
        {
            [] => (false, "empty instruction"),
            ["set-status",      .. var r] => Parsed(Specs.SetStatus,      r, ApplySetStatus),
            ["set-concern",     .. var r] => Parsed(Specs.SetConcern,     r, ApplySetConcern),
            ["remove-concern",  .. var r] => Parsed(Specs.RemoveConcern,  r, ApplyRemoveConcern),
            ["add-snaplink",    .. var r] => Parsed(Specs.AddSnaplink,    r, ApplyAddSnaplink),
            ["set-snaplink",    .. var r] => SetSnaplinkLine(r),
            ["remove-snaplink", .. var r] => Parsed(Specs.RemoveSnaplink, r, ApplyRemoveSnaplink),
            ["remap",           .. var r] => Parsed(Specs.Remap,           r, ApplyRemap),
            ["set-node",        .. var r] => Parsed(Specs.SetNode,        r, ApplySetNode),
            ["add-node",        .. var r] => Parsed(Specs.AddNode,        r, ApplyAddNode),
            ["move",            .. var r] => Parsed(Specs.Move,           r, ApplyMove),
            ["rename",          .. var r] => Parsed(Specs.Rename,         r, ApplyRename),
            ["remove",          .. var r] => Parsed(Specs.Remove,         r, ApplyRemove),
            [var verb, ..] => (false, $"unknown instruction '{verb}' (batch supports: set-status, set-concern, remove-concern, add-snaplink, set-snaplink, remove-snaplink, remap, set-node, add-node, move, rename, remove)"),
        };

        // The line named a target, so the line answers for it — reported as this instruction, with the text
        // that named it, rather than as a broken link somebody finds in the tree a week later. remap is not
        // checked here: it rewrites en masse, so only what it produced as a whole means anything, and that is
        // checked at the write.
        if (result.Ok && root is not null && WrittenBy(state, args) is { } link
            && RefusalFor(state, root, [link]) is { } refusal)
        {
            if (list is not null && before is not null) { list.Clear(); list.AddRange(before); }
            return (false, refusal);
        }

        return result;
    }

    private static int Batch(string[] args)
    {
        if (!TryRead(Specs.Batch, args, out var a, out var root, out var parseCode)) return parseCode;
        var dryRun = a.Has("--dry-run");
        // Measured from where the caller stands, not from the resident process — see CallerPath. A relative
        // script path is the ordinary way to write this ("nfi batch tree.batch"), and it resolved against the
        // daemon's directory, so the file the caller was looking straight at did not exist.
        var file = CallerPath.Of(a[0]);
        if (!File.Exists(file))
            return VerbUsage($"batch: no such script file: {a[0]}"
                           + (PathsEqual(file, a[0]) ? "" : $" (looked in {CallerPath.Directory})"));
        if (!TryLoad(root, out var state, out var code)) return code;

        var lines = File.ReadAllLines(file);
        var applied = new List<string>();

        // Snaplink work is mostly done in batches — written, checked with --dry-run, then applied — so a
        // batch has to make the same split a single verb does: nodes to the shared tree, links to the
        // branch. Collected as we go, because a batch can legitimately do both in one run.
        var touchedLinks = new List<(string NodeId, string? Concern)>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;   // blank lines + # comments
            var tokens = Tokenize(line).ToArray();
            if (LinkTargetOf(tokens) is { } target) touchedLinks.Add(target);
            var (ok, msg) = ApplyOne(state, tokens, root);

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
        return SaveAndValidate(state, root, $"Applied {applied.Count} instruction(s) from {Path.GetFileName(file)}.",
                               touchedLinks);
    }

    /// <summary>
    /// The node (and concern) whose links a batch instruction changes, or null when it changes none.
    /// <para>
    /// <c>remap</c> is deliberately absent: it rewrites links across many nodes to follow a rename, which is
    /// a repair of the shared tree rather than a claim this branch is making, and it is run on main.
    /// </para>
    /// </summary>
    private static (string NodeId, string? Concern)? LinkTargetOf(string[] tokens)
    {
        var spec = tokens switch
        {
            ["add-snaplink",    ..] => Specs.AddSnaplink,
            ["set-snaplink",    ..] => Specs.SetSnaplink,
            ["remove-snaplink", ..] => Specs.RemoveSnaplink,
            _                       => null,
        };
        if (spec is null) return null;

        return VerbArgs.TryParse(spec.InBatch, tokens[1..], out var parsed, out _) && parsed.Positionals.Count > 0
            ? (parsed[0], parsed.Value("--concern"))
            : null;
    }

    /// <summary>
    /// The link a snaplink instruction just wrote, so that ONE line can be answered for by the arguments it was
    /// given: a <c>--doc</c> naming a file that is not there, a <c>--class</c>/<c>--method</c> that is not
    /// declared, a <c>--target</c> that is not a node or not a URL. Null for an instruction that wrote no link.
    /// <para>
    /// <c>remap</c> is deliberately not here. It rewrites every link matching a path — twenty of them, spread
    /// over as many nodes — so no single argument names a link, and only what it produced as a whole is worth
    /// checking. That happens at the write instead, via <see cref="RefusalFor"/>.
    /// </para></summary>
    private static Snaplink? WrittenBy(ProductState state, string[] tokens)
    {
        var spec = tokens switch
        {
            ["set-snaplink", ..] => Specs.SetSnaplink,
            ["add-snaplink", ..] => Specs.AddSnaplink,
            _                    => null,
        };
        return spec is not null && VerbArgs.TryParse(spec.InBatch, tokens[1..], out var a, out _)
            ? WrittenBy(state, tokens[0], a)
            : null;
    }

    /// <summary>As above, for a verb whose arguments are already parsed (the standalone call).</summary>
    private static Snaplink? WrittenBy(ProductState state, string verb, VerbArgs a)
    {
        if (a.Positionals.Count == 0) return null;
        var concern = a.Value("--concern");

        if (verb == "add-snaplink")
            return ProductTreeOps.SnaplinksOf(state, a[0], concern) is { Count: > 0 } list ? list[^1] : null;

        return verb == "set-snaplink"
            && int.TryParse(a.Value("--index"), out var index)
            && ProductTreeOps.TryGetSnaplink(state, a[0], index, concern, out var link)
            ? link
            : null;
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

        // The coverage manifest is derived and gitignored, so it is often absent; LoadTestCoverage then
        // returns null and the leaf-granularity rule quietly sits out rather than the verb failing.
        var findings = StructureLinter.Lint(state, under, new ProductStore(root).LoadTestCoverage());
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
        SaveTree(root, store, state);
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

    /// <summary>
    /// Loads the tree, or emits the right message + exit code when there is nothing to load.
    /// <para>
    /// On a branch the tree is read with that branch's pending snaplinks overlaid, so every verb —
    /// describe, validate, tree, and the edits themselves — sees the links as this branch has them rather
    /// than the shared tree's older idea. That is what makes deferring a link change invisible in normal
    /// use: it is only the <i>write</i> that goes somewhere else.
    /// </para>
    /// </summary>
    /// <param name="applyPending">False for the one caller that must see the shared tree as it stands —
    /// <c>pending</c>, which reports the difference between the two.</param>
    private static bool TryLoad(string root, out ProductState state, out int code, bool applyPending = true)
    {
        state = new ProductState();
        if (!Directory.Exists(root)) { Console.Error.WriteLine($"error: no such directory: {root}"); code = Error; return false; }
        if (!ProductStore.Exists(root)) { Console.Error.WriteLine($"error: no .product/ under {root}."); code = Error; return false; }
        try
        {
            state = LoadTree(root, new ProductStore(root));
            if (applyPending && PendingBranch(root) is { } branch)
                PendingStoreFor(root).Load(branch).ApplyTo(state);
            code = Clean;
            return true;
        }
        catch (Exception ex) { Console.Error.WriteLine($"error: {ex.Message}"); code = Error; return false; }
    }
}
