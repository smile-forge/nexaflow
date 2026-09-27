using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Nexaflow.Analyzers.Content;

/// <summary>
/// What a content builder is. A builder is one step of the chain the engine runs — parse, nest, stages, build — handed content
/// already read and turning it into a layout, and owning nothing. A language only names its builder; the engine makes it, from
/// the same five things every time.
///
/// <para>
/// <b>NXCB001</b> — one constructor, taking exactly what <c>ContentBuilder</c>'s own takes. Anything else a builder was going to be
/// told is a fact about the content or the showing of it, and belongs in what every builder is given the same way.
/// <b>NXCB002</b> — nothing told to it after it is made: a property with a setter is a second constructor written the long way
/// round. <b>NXCB003</b> — nothing offered under a name <c>ContentBuilder</c> does not declare, so there is one place that says
/// what a builder is. <b>NXCB004</b> — nothing makes a builder: the engine does, from the type a language names, and a builder
/// made anywhere else is a builder somebody is using as a library.
/// </para>
/// <para>
/// Errors, because there is nothing to shrink: every builder is the shape, and only the engine makes one. Test assemblies are
/// exempt — a test may make a builder of its own to hold the base to what it promises — and are not handed the analyzer.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContentBuilderAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "Content";

    /// <summary>What every builder derives from.</summary>
    private const string Builder = "Nexaflow.Visuals.Text.Editing.ContentBuilder";

    /// <summary>What marks an assembly as tests, which may make builders of their own.</summary>
    private const string TestClass = "Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute";

    private static readonly DiagnosticDescriptor OneShape = new(
        "NXCB001",
        "A builder is made from the five things every builder is made from",
        "{0} {1}; a builder has one constructor, taking ({2})",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "The engine makes every builder the same way, from what was read, what is being written, what it is drawn in, whether it is only looked at, and what lays out what it holds in another language. Anything else is a fact about the content or its showing, and belongs in one of those.");

    private static readonly DiagnosticDescriptor ToldAfter = new(
        "NXCB002",
        "A builder is told nothing after it is made",
        "{0} is told {1} after it is made; there is one way to tell a builder anything",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "A property somebody sets on a builder is a second constructor written the long way round.");

    private static readonly DiagnosticDescriptor OffersMore = new(
        "NXCB003",
        "A builder offers nothing ContentBuilder does not declare",
        "{0} offers {1}, which ContentBuilder does not",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "What a builder is, is decided in one place. A name of its own on a builder is a second door into the same room, which something then starts using.");

    private static readonly DiagnosticDescriptor MadeElsewhere = new(
        "NXCB004",
        "Only the engine makes a builder",
        "{0} is made here; a language names its builder and the engine makes it",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "A builder made anywhere but the engine is a builder used as a library: laid out without being parsed, nested and worked over the way everything else is.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(OneShape, ToldAfter, OffersMore, MadeElsewhere);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetTypeByMetadataName(Builder) is not { } builder) return;
            if (start.Compilation.GetTypeByMetadataName(TestClass) is not null) return;

            var shape = builder.InstanceConstructors.Length == 1 ? builder.InstanceConstructors[0].Parameters : default;
            var named = new HashSet<string>(
                builder.GetMembers().Select(member => member.Name)
                       .Concat(start.Compilation.GetSpecialType(SpecialType.System_Object).GetMembers().Select(member => member.Name)));

            start.RegisterSymbolAction(symbol => Shaped(symbol, builder, shape, named), SymbolKind.NamedType);
            start.RegisterOperationAction(operation => Made(operation, builder), OperationKind.ObjectCreation);
        });
    }

    private static void Shaped(SymbolAnalysisContext context, INamedTypeSymbol builder, ImmutableArray<IParameterSymbol> shape, HashSet<string> named)
    {
        if (context.Symbol is not INamedTypeSymbol { TypeKind: TypeKind.Class } type || !Derives(type, builder)) return;

        var place = type.Locations.FirstOrDefault();
        var made = type.InstanceConstructors.Where(constructor => !constructor.IsImplicitlyDeclared).ToList();

        if (made.Count != 1)
            context.ReportDiagnostic(Diagnostic.Create(OneShape, place, type.Name, $"has {made.Count} constructors", Readable(shape)));
        else if (!shape.IsDefault && !made[0].Parameters.Select(parameter => parameter.Type)
                                           .SequenceEqual(shape.Select(parameter => parameter.Type), SymbolEqualityComparer.Default))
            context.ReportDiagnostic(Diagnostic.Create(OneShape, made[0].Locations.FirstOrDefault() ?? place,
                                                       type.Name, $"is made with ({Readable(made[0].Parameters)})", Readable(shape)));

        foreach (var member in type.GetMembers().Where(member => !member.IsImplicitlyDeclared))
        {
            if (member is IPropertySymbol { SetMethod: not null } told)
                context.ReportDiagnostic(Diagnostic.Create(ToldAfter, told.Locations.FirstOrDefault() ?? place, type.Name, told.Name));

            if (member.DeclaredAccessibility == Accessibility.Public && Offered(member) && !named.Contains(member.Name))
                context.ReportDiagnostic(Diagnostic.Create(OffersMore, member.Locations.FirstOrDefault() ?? place, type.Name, member.Name));
        }
    }

    private static void Made(OperationAnalysisContext context, INamedTypeSymbol builder)
    {
        if (context.Operation is IObjectCreationOperation { Type: INamedTypeSymbol made } && Derives(made, builder))
            context.ReportDiagnostic(Diagnostic.Create(MadeElsewhere, context.Operation.Syntax.GetLocation(), made.Name));
    }

    /// <summary>Whether <paramref name="type"/> is a builder — <paramref name="builder"/> itself apart.</summary>
    private static bool Derives(INamedTypeSymbol type, INamedTypeSymbol builder)
    {
        for (var at = type.BaseType; at is not null; at = at.BaseType)
            if (SymbolEqualityComparer.Default.Equals(at, builder)) return true;

        return false;
    }

    /// <summary>What a type offers the world by name: a method, a property, a field or an event — not an accessor, an operator or a nested type.</summary>
    private static bool Offered(ISymbol member) => member switch
    {
        IMethodSymbol method => method.MethodKind == MethodKind.Ordinary,
        IPropertySymbol or IFieldSymbol or IEventSymbol => true,
        _ => false,
    };

    private static string Readable(ImmutableArray<IParameterSymbol> parameters) =>
        parameters.IsDefault ? "what ContentBuilder's constructor takes" : string.Join(", ", parameters.Select(parameter => parameter.Type.Name));
}
