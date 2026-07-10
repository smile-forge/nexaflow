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
            "validate" => Validate(args[1..]),
            "find"     => Find(args[1..]),
            "describe" => Describe(args[1..]),
            "remap"    => Remap(args[1..]),
            _ => Usage($"unknown command '{args[0]}'")
        };
    }

    private static int Usage(string? error = null)
    {
        if (error is not null) Console.Error.WriteLine($"error: {error}");
        Console.WriteLine("""
            nexaflow-initiatives — product tracker tooling

            usage:
              nexaflow-initiatives validate [<root>] [--json] [--save]
              nexaflow-initiatives find     <term> [<root>] [--json]
              nexaflow-initiatives describe <node-id> [<root>] [--json]
              nexaflow-initiatives remap    <old-path> <new-path> [<root>] [--class <name>] [--method <name>]

            validate   Checks every snaplink still points at a real target (file exists, md heading resolves,
                       class/method declared, URL well formed). --save writes .product/integrity.json.
            find       Lists nodes whose id/title/description contains <term> — "where is feature X".
            describe   Prints one node: path, status, concerns, and its code/test/doc snaplinks.
            remap      Rewrites snaplink doc paths from <old-path> to <new-path> (an exact file, or a
                       directory prefix) — the safe way to follow a rename/move — then re-validates.
                       --class/--method also set those on every affected link (single-file remaps).

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
