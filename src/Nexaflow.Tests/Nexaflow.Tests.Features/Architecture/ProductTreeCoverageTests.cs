using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// Keeps the product tree honest about what actually ships, in both directions:
///  • forward — a feature/provider node the tree calls <c>done</c> must point at a real <c>.csproj</c>;
///  • reverse — every feature/provider assembly on disk must have a node under its family, so a shipped
///    assembly can't quietly go untracked (exactly what happened when GraphViewer had no node).
/// The live tree is the gitignored <c>.product/tree.json</c>, so in a clean CI checkout it's absent — these
/// then no-op (they're a dev-time guard, run with the suite before committing where the tree exists).
/// </summary>
[TestClass]
[NoCoverage("architecture guard")]
public class ProductTreeCoverageTests
{
    private static readonly string Root = RepoRoot.Locate();
    private static readonly string TreePath = Path.Combine(Root, ".product", "tree.json");

    // Family node id -> the src subdir whose Nexaflow.<Family>.* assemblies it should track.
    private static readonly (string Node, string SubDir)[] Families =
        [("features", "Nexaflow.Features"), ("providers", "Nexaflow.Providers")];

    private static Dictionary<string, JsonElement> NodeMap(JsonDocument tree)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in tree.RootElement.GetProperty("nodes").EnumerateObject()) map[p.Name] = p.Value;
        return map;
    }

    private static IEnumerable<string> ChildIds(JsonElement node)
        => node.TryGetProperty("children", out var ch) && ch.ValueKind == JsonValueKind.Array
            ? ch.EnumerateArray().Select(e => e.GetString()).Where(s => s is not null).Cast<string>()
            : [];

    private static string Status(JsonElement node)
        => node.TryGetProperty("status", out var s) ? s.GetString() ?? "should" : "should";

    private static IEnumerable<string> CsprojSnaplinkDocs(JsonElement node)
    {
        if (!node.TryGetProperty("snaplinks", out var sl) || sl.ValueKind != JsonValueKind.Array) yield break;
        foreach (var link in sl.EnumerateArray())
            if (link.TryGetProperty("doc", out var d) && d.GetString() is { } doc
                && doc.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                yield return doc.Replace('\\', '/');
    }

    [TestMethod]
    public void Done_feature_and_provider_nodes_link_a_valid_csproj()
    {
        if (!File.Exists(TreePath)) return;   // tree is gitignored → absent in CI; dev-time guard only
        using var tree = JsonDocument.Parse(File.ReadAllText(TreePath));
        var map = NodeMap(tree);

        var offenders = new List<string>();
        foreach (var (familyNode, _) in Families)
        {
            if (!map.TryGetValue(familyNode, out var fam)) continue;
            foreach (var childId in ChildIds(fam))
            {
                if (!map.TryGetValue(childId, out var child) || Status(child) != "done") continue;   // roll-up: only 'done' must be backed
                var hasValid = CsprojSnaplinkDocs(child)
                    .Any(doc => File.Exists(Path.Combine(Root, doc.Replace('/', Path.DirectorySeparatorChar))));
                if (!hasValid) offenders.Add($"{familyNode}/{childId}");
            }
        }

        Assert.AreEqual(0, offenders.Count,
            "A 'done' node under features/providers must carry a code snaplink to a real .csproj (its assembly). " +
            $"Missing or broken: {string.Join(", ", offenders)}");
    }

    [TestMethod]
    public void Every_feature_and_provider_assembly_has_a_tree_node()
    {
        if (!File.Exists(TreePath)) return;   // dev-time guard only (see above)
        using var tree = JsonDocument.Parse(File.ReadAllText(TreePath));
        var map = NodeMap(tree);

        var missing = new List<string>();
        foreach (var (familyNode, subDir) in Families)
        {
            // Every .csproj a direct child of this family node points at.
            var linked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (map.TryGetValue(familyNode, out var fam))
                foreach (var cid in ChildIds(fam))
                    if (map.TryGetValue(cid, out var c))
                        foreach (var doc in CsprojSnaplinkDocs(c)) linked.Add(doc);

            foreach (var dir in Directory.GetDirectories(Path.Combine(Root, "src", subDir), subDir + ".*"))
            {
                var asm = Path.GetFileName(dir);
                var shortName = asm[(subDir.Length + 1)..];
                if (shortName is "Common" || shortName.Contains('.')) continue;   // shared contracts / codec backend → a node is optional
                if (!linked.Contains($"src/{subDir}/{asm}/{asm}.csproj")) missing.Add(asm);
            }
        }

        Assert.AreEqual(0, missing.Count,
            "Every feature/provider assembly must have a node under features/providers linking its .csproj, or a " +
            $"shipped assembly is untracked in the product tree. Missing: {string.Join(", ", missing)}");
    }
}
