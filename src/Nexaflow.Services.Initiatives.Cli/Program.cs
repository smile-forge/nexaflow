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
            _ => Usage($"unknown command '{args[0]}'")
        };
    }

    private static int Usage(string? error = null)
    {
        if (error is not null) Console.Error.WriteLine($"error: {error}");
        Console.WriteLine("""
            nexaflow-initiatives — product tracker tooling

            usage:
              nexaflow-initiatives validate [<product-root>] [--json] [--save]

            validate   Checks every snaplink in <product-root>/.product/tree.json still points at a real
                       target: the file exists, the markdown heading path resolves, the class/method is
                       still declared, the URL is well formed. Defaults to the current directory.

              --json   Emit the full report as JSON on stdout instead of a summary.
              --save   Also write the report to <product-root>/.product/integrity.json.

            exit codes: 0 = clean (or no .product/), 1 = broken snaplinks, 2 = usage/IO error
            """);
        return error is null ? Clean : Error;
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
}
