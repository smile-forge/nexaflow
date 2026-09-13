using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Model;

namespace Nexaflow.Services.Initiatives.Graph;

/// <summary>
/// The tests worth running for a node: those that use it, those that use the code that uses it, and those that declare
/// they cover the feature it belongs to. The point is the loop after a change — prove it with the tests that exercise
/// it, without first working out which suite they live in, how that suite is built, or where its runner is.
/// <para>
/// A use is found by the caller's <see cref="Sources.UsesOf"/> — the compiler's binding, in the CLI — and by spelling
/// only when that cannot answer. Spelling alone chose every suite in the repository for a helper called <c>Named</c>.
/// </para>
/// </summary>
public static class TestSelection
{
    /// <summary>One test project to build and run, the filters that pick its tests, and why they were picked.</summary>
    public sealed record Suite(string Project, IReadOnlyList<string> Filters, IReadOnlyList<string> Reasons);

    /// <param name="Journeys">Suites that use it but take over the mouse and keyboard, which are never run on the
    /// caller's behalf — named, with their filters, for a person to run.</param>
    public sealed record Selection(IReadOnlyList<Suite> Suites, IReadOnlyList<Suite> Journeys, IReadOnlyList<string> Notes);

    /// <summary>A declaration whose uses are wanted.</summary>
    public sealed record Target(string RelativePath, string AstPath, string Name);

    /// <summary>One place a declaration is used.</summary>
    public sealed record Use(string RelativePath, int Line);

    /// <summary>What the selection reads, supplied by the caller so the rules here stay free of files and compilers.</summary>
    /// <param name="Files">The repository's source files, repo-relative.</param>
    /// <param name="ProjectOf">A source file's project, repo-relative — or null for one no project compiles.</param>
    /// <param name="IsTestProject">Whether a project is a test project.</param>
    /// <param name="TakesOverTheMachine">Whether a test project drives the real desktop, and so must not be run for anyone.</param>
    /// <param name="UsesOf">The uses of a target among the candidate files that spell its name, or null when they
    /// cannot be resolved — in which case the spelling is taken as the use.</param>
    /// <param name="Coverage">What the test assemblies declare they cover, when a scan has recorded it.</param>
    public sealed record Sources(Func<string, string?> Read, IReadOnlyList<string> Files, Func<string, string?> ProjectOf,
                                 Func<string, bool> IsTestProject, Func<string, bool> TakesOverTheMachine,
                                 Func<Target, IReadOnlyList<string>, IReadOnlyList<Use>?> UsesOf,
                                 TestCoverageManifest? Coverage);

    /// <summary>How far from the node its uses are followed: the tests that use it, that use what uses it, and one step
    /// beyond — an internal helper's tests are usually two calls up, through the public surface that calls it.</summary>
    private const int Steps = 3;

    private const int TargetsPerStep = 30;

    private static readonly Regex TestAttribute =
        new(@"\[\s*(TestMethod|DataTestMethod|Fact|Theory|Test|TestCase)\b", RegexOptions.CultureInvariant);

    public static Selection For(KnowledgeGraph graph, string nodeId, Sources sources)
    {
        var index = GraphQuery.Index(graph);
        var notes = new List<string>();
        var picks = new Dictionary<string, (HashSet<string> Filters, HashSet<string> Reasons)>(StringComparer.OrdinalIgnoreCase);

        void Pick(string project, string filter, string reason)
        {
            if (!picks.TryGetValue(project, out var pick)) picks[project] = pick = ([], []);
            pick.Filters.Add(filter);
            pick.Reasons.Add(reason);
        }

        // ── Declared coverage: the tests that say they cover the feature this belongs to ──
        var products = ProductsOf(graph, index, nodeId);
        if (sources.Coverage is { } coverage)
        {
            foreach (var product in products)
                foreach (var test in coverage.Coverage.GetValueOrDefault(product) ?? [])
                    if (test.File is { Length: > 0 } file && sources.ProjectOf(file) is { } project)
                        Pick(project, test.Method is { Length: > 0 } method ? $"{test.Class}.{method}" : $"{test.Class}.",
                             $"declares it covers {product}");
        }
        else if (products.Count > 0)
        {
            notes.Add("no coverage manifest yet, so the tests declaring coverage of its feature were not looked up — "
                    + "nfi scan-tests records them");
        }

        // ── By use: the tests that use it, then the tests that use what uses it ──
        var frontier = TargetsOf(graph, index, nodeId);
        var seen     = new HashSet<string>(frontier.Select(t => $"{t.RelativePath}#{t.AstPath}"), StringComparer.Ordinal);
        for (var step = 1; step <= Steps && frontier.Count > 0; step++)
        {
            var spelled = GraphMentions.Of(graph, frontier.Select(t => t.Name).ToList(), sources.Files, sources.Read);
            var next    = new List<Target>();

            foreach (var target in frontier)
            {
                var candidates = spelled.Where(m => m.Text.Contains(target.Name, StringComparison.Ordinal))
                                        .Select(m => m.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var uses = sources.UsesOf(target, candidates)
                        ?? [.. spelled.Where(m => m.Text.Contains(target.Name, StringComparison.Ordinal)).Select(m => new Use(m.RelativePath, m.Line))];

                foreach (var use in uses)
                {
                    if (sources.ProjectOf(use.RelativePath) is not { } project) continue;
                    var owner = GraphMentions.OwnerOf(graph, use.RelativePath, use.Line);

                    if (sources.IsTestProject(project))
                    {
                        if (owner is not null && TestFilter(owner, sources) is { } filter)
                            Pick(project, filter, step == 1 ? $"uses {target.Name}" : $"uses {target.Name}, which uses it");
                    }
                    else if (step < Steps && owner?.FilePath is { } file && owner.Metadata?.GetValueOrDefault("ast") is { Length: > 0 } ast
                             && owner.Label is { Length: > 0 } label && seen.Add($"{file}#{ast}"))
                        next.Add(new Target(file, ast, label));
                }
            }

            if (next.Count > TargetsPerStep)
            {
                notes.Add($"{next.Count} declarations use it; the tests of the first {TargetsPerStep} are included");
                next = [.. next.Take(TargetsPerStep)];
            }
            frontier = next;
        }

        var all = picks.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                       .Select(p => new Suite(p.Key, [.. p.Value.Filters.Order(StringComparer.Ordinal)],
                                              [.. p.Value.Reasons.Order(StringComparer.Ordinal).Take(6)]))
                       .ToList();
        return new Selection([.. all.Where(s => !sources.TakesOverTheMachine(s.Project))],
                             [.. all.Where(s => sources.TakesOverTheMachine(s.Project))], notes);
    }

    /// <summary>The declarations a node stands for: itself, for a code node; the types it declares, for a file.</summary>
    private static List<Target> TargetsOf(KnowledgeGraph graph, Dictionary<string, GraphNode> index, string nodeId)
    {
        if (nodeId.StartsWith("file:", StringComparison.Ordinal))
        {
            var rel = nodeId["file:".Length..];
            return [.. graph.Nodes.Where(n => n.Type == NodeType.Type && string.Equals(n.FilePath, rel, StringComparison.OrdinalIgnoreCase)
                                           && n.Metadata?.GetValueOrDefault("ast") is { Length: > 0 } ast && !ast.Contains('/')
                                           && n.Label is { Length: > 0 })
                                  .Select(n => new Target(rel, n.Metadata!["ast"], n.Label!))];
        }

        if (!nodeId.StartsWith("code:", StringComparison.Ordinal) || nodeId.IndexOf('#') is not (> 0 and var hash)) return [];

        var (file, path) = (nodeId["code:".Length..hash], nodeId[(hash + 1)..]);
        var name = index.GetValueOrDefault(nodeId)?.Label ?? Syntax.StructuralEdit.NameInPath(path);
        return name is { Length: > 0 } ? [new Target(file, path, name)] : [];
    }

    /// <summary>
    /// The product nodes a node belongs to: itself, when it is one, with the features under it; otherwise the features
    /// whose snaplinks name it, the type it is declared in, or its file.
    /// </summary>
    private static List<string> ProductsOf(KnowledgeGraph graph, Dictionary<string, GraphNode> index, string nodeId)
    {
        bool IsProduct(string id) => index.GetValueOrDefault(id)?.Type == NodeType.Product;

        if (IsProduct(nodeId))
        {
            var found = new List<string> { nodeId };
            var queue = new Queue<string>(found);
            while (queue.Count > 0)
            {
                var parent = queue.Dequeue();
                foreach (var e in graph.Edges.Where(e => e.Relationship == EdgeRelationship.Contains && e.Source == parent && IsProduct(e.Target)))
                    if (!found.Contains(e.Target)) { found.Add(e.Target); queue.Enqueue(e.Target); }
            }
            return found;
        }

        var holders = new HashSet<string>(StringComparer.Ordinal) { nodeId };
        var hash    = nodeId.IndexOf('#');
        for (var cut = nodeId.LastIndexOf('/'); hash > 0 && cut > hash; cut = nodeId.LastIndexOf('/', cut - 1))
            holders.Add(nodeId[..cut]);
        if (index.GetValueOrDefault(nodeId)?.FilePath is { Length: > 0 } file) holders.Add("file:" + file);

        return [.. graph.Edges.Where(e => holders.Contains(e.Target) && IsProduct(e.Source)).Select(e => e.Source).Distinct()];
    }

    /// <summary>The filter that runs the test a use sits in: the method when it is one, otherwise its whole class —
    /// a helper in a test class is exercised by that class's tests.</summary>
    private static string? TestFilter(GraphNode owner, Sources sources)
    {
        if (owner.Id.IndexOf('#') is not (> 0 and var hash)) return null;

        var segments = owner.Id[(hash + 1)..].Split('/');
        var type     = segments.LastOrDefault(s => s.StartsWith("T:", StringComparison.Ordinal))?[2..];
        if (type is null) return null;

        var member = segments[^1].StartsWith("M:", StringComparison.Ordinal) ? segments[^1][2..].Split('#')[0] : null;
        return member is not null && IsTest(owner, sources) ? $".{type}.{member}" : $".{type}.";
    }

    private static bool IsTest(GraphNode member, Sources sources)
    {
        if (member.FilePath is not { } rel || sources.Read(rel) is not { } text
            || !int.TryParse(member.Metadata?.GetValueOrDefault("line"), out var line)) return false;

        var lines = text.Split('\n');
        for (var i = Math.Max(0, line - 8); i < Math.Min(lines.Length, line); i++)
            if (TestAttribute.IsMatch(lines[i])) return true;
        return false;
    }
}