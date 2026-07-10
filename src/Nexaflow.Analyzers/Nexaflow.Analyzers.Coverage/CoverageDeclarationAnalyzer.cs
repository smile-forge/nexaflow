using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Nexaflow.Analyzers.Coverage;

/// <summary>
/// Author-time backstop for the test↔product-node coverage rule. Two diagnostics:
/// <list type="bullet">
///   <item><b>NXCOV001</b> — a concrete <c>[TestClass]</c> with no <c>[CoversNode]</c> and no
///   <c>[NoCoverage]</c>: declare the node it backs, or opt out.</item>
///   <item><b>NXCOV002</b> — a <c>[CoversNode("id")]</c> whose id is not in <c>.product/tree.json</c> (only
///   checked when that AdditionalFile is present).</item>
/// </list>
/// The CI guard test enforces the same invariants; this surfaces them in the editor as you type. Attribute
/// matching is by simple type name so the analyzer needs no reference to MSTest or the fixtures library.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CoverageDeclarationAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "Coverage";

    private static readonly DiagnosticDescriptor MissingDeclaration = new(
        "NXCOV001",
        "Test class must declare the product node it covers",
        "Test class '{0}' has no [CoversNode(\"…\")] or [NoCoverage(\"…\")] — declare the product node it backs, or opt out",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "Every concrete [TestClass] must declare the product-tree node it backs (or opt out) so the tree can represent test coverage.");

    private static readonly DiagnosticDescriptor UnknownNode = new(
        "NXCOV002",
        "Unknown product node id",
        "[CoversNode(\"{0}\")] does not match any node in .product/tree.json",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "A [CoversNode] id must be a real node in the current product tree; a stale id no longer points at anything.");

    private static readonly DiagnosticDescriptor ClassLevelLeaf = new(
        "NXCOV003",
        "Class-level [CoversNode] over-claims a leaf node",
        "[CoversNode(\"{0}\")] is at class level, '{0}' is a leaf node (no children), and this class covers other nodes too — so claiming a specific behaviour for the whole class over-claims. Move it to the [TestMethod](s) that exercise it, or add sub-nodes under '{0}' (nexaflow-initiatives add-node) and map methods to them.",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "When a test class covers several nodes, a class-level [CoversNode] should name a container (a node with children); a leaf node names a specific behaviour that belongs on the method(s) covering it. A single-purpose class that covers exactly one leaf is fine and is not flagged.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(MissingDeclaration, UnknownNode, ClassLevelLeaf);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var tree = NodeIdTreeReader.TryLoad(start.Options.AdditionalFiles, start.CancellationToken);
            start.RegisterSymbolAction(ctx => Analyze(ctx, tree), SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext ctx, TreeInfo? tree)
    {
        var type = (INamedTypeSymbol)ctx.Symbol;
        // Skip abstract bases (never run directly) and static classes (assembly-fixture holders, not test
        // cases). Note: Roslyn reports IsAbstract == false for a static class — IsStatic is the real check.
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic) return;
        if (!type.GetAttributes().Any(a => IsNamed(a, "TestClassAttribute"))) return;

        var covers = CoversNode(type).ToList();
        var hasOptOut = type.GetAttributes().Any(a => IsNamed(a, "NoCoverageAttribute"));

        if (covers.Count == 0 && !hasOptOut)
            ctx.ReportDiagnostic(Diagnostic.Create(MissingDeclaration, type.Locations.FirstOrDefault(), type.Name));

        if (tree is null) return;
        // A class-level leaf only over-claims when the class covers *other* nodes too; a single-purpose class
        // that maps to exactly one leaf is correct, so it isn't flagged.
        var distinctIds = covers.Select(c => c.Id).Where(x => x.Length > 0).Distinct().Count();
        foreach (var (id, location, classLevel) in covers)
        {
            if (id.Length == 0) continue;
            if (!tree.Contains(id))
                ctx.ReportDiagnostic(Diagnostic.Create(UnknownNode, location, id));
            else if (classLevel && distinctIds >= 2 && tree.IsLeaf(id))
                ctx.ReportDiagnostic(Diagnostic.Create(ClassLevelLeaf, location, id));
        }
    }

    /// <summary>Every <c>[CoversNode]</c> on the type or a method — id, where to report, and whether it sits on the class.</summary>
    private static IEnumerable<(string Id, Location Location, bool ClassLevel)> CoversNode(INamedTypeSymbol type)
    {
        foreach (var a in type.GetAttributes())
            if (IsNamed(a, "CoversNodeAttribute") && FirstString(a) is { } id)
                yield return (id, AttrLocation(a, type), true);

        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            foreach (var a in method.GetAttributes())
                if (IsNamed(a, "CoversNodeAttribute") && FirstString(a) is { } id)
                    yield return (id, AttrLocation(a, method), false);
    }

    private static bool IsNamed(AttributeData a, string simpleName) => a.AttributeClass?.Name == simpleName;

    private static string? FirstString(AttributeData a) =>
        a.ConstructorArguments.Length > 0 && a.ConstructorArguments[0].Value is string s ? s : null;

    private static Location AttrLocation(AttributeData a, ISymbol fallback) =>
        a.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? fallback.Locations.FirstOrDefault() ?? Location.None;
}
