using System.Reflection;
using System.Text.Json;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// The shared reflection logic behind the coverage guard tests (one per test assembly): every concrete
/// <c>[TestClass]</c> must declare the product node(s) it backs with <c>[CoversNode]</c> (on the class or a
/// method) or opt out with <c>[NoCoverage]</c>, and no declared id may be stale. Lives in the
/// dependency-free fixtures lib so both the Core and Features guards call one implementation.
/// </summary>
public static class CoverageDeclarationCheck
{
    private const string TestClassAttr = "Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute";

    private const BindingFlags DeclaredMethods =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>A test class that breaks the declaration rule, with a message suitable for an assert.</summary>
    public sealed record Violation(string TypeName, string Message);

    /// <summary>
    /// Finds every declaration violation in <paramref name="assembly"/>. When <paramref name="validNodeIds"/>
    /// is non-null, also flags any <c>[CoversNode]</c> id absent from it (a stale/typo'd id). Pass null to
    /// enforce presence only — the tree is unavailable (e.g. a clean CI checkout without <c>.product/</c>).
    /// </summary>
    public static IReadOnlyList<Violation> Verify(Assembly assembly, IReadOnlySet<string>? validNodeIds)
    {
        var violations = new List<Violation>();
        foreach (var type in SafeGetTypes(assembly))
        {
            if (type is null || type.IsAbstract || !IsTestClass(type)) continue;   // abstract bases never run directly

            var covers = CoversNodeIds(type).ToList();
            var hasOptOut = type.GetCustomAttributes<NoCoverageAttribute>(false).Any();

            if (covers.Count == 0 && !hasOptOut)
                violations.Add(new Violation(type.FullName ?? type.Name,
                    "is a [TestClass] with no [CoversNode(\"…\")] and no [NoCoverage(\"…\")] — declare the product "
                    + "node it backs, or opt out with a reason."));

            if (validNodeIds is not null)
                foreach (var id in covers.Where(id => !validNodeIds.Contains(id)).Distinct())
                    violations.Add(new Violation(type.FullName ?? type.Name,
                        $"declares [CoversNode(\"{id}\")] but no such node exists in .product/tree.json."));
        }
        return violations;
    }

    /// <summary>
    /// The valid node-id set from the live <c>.product/tree.json</c> (the current intended state), or null
    /// when the tree is absent — <c>.product/</c> is gitignored, so a clean checkout has no tree and id
    /// validity simply isn't checked there. The historical <c>docs/product/*.json</c> snapshots are
    /// deliberately not consulted.
    /// </summary>
    public static IReadOnlySet<string>? LoadValidNodeIds()
    {
        var tree = TryLocateTree();
        if (tree is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(tree));
            if (!doc.RootElement.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Object)
                return null;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in nodes.EnumerateObject()) ids.Add(p.Name);
            return ids;
        }
        catch { return null; }   // an unreadable/partial tree ⇒ skip id validity, never a false failure
    }

    private static IEnumerable<string> CoversNodeIds(Type type)
    {
        foreach (var a in type.GetCustomAttributes<CoversNodeAttribute>(false)) yield return a.NodeId;
        foreach (var m in type.GetMethods(DeclaredMethods))
            foreach (var a in m.GetCustomAttributes<CoversNodeAttribute>(false)) yield return a.NodeId;
    }

    private static bool IsTestClass(Type type) =>
        type.GetCustomAttributesData().Any(a => a.AttributeType.FullName == TestClassAttr);

    private static IEnumerable<Type?> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types; }
    }

    private static string? TryLocateTree()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Nexaflow.slnx")))
            {
                var tree = Path.Combine(dir.FullName, ".product", "tree.json");
                return File.Exists(tree) ? tree : null;
            }
        return null;
    }
}
