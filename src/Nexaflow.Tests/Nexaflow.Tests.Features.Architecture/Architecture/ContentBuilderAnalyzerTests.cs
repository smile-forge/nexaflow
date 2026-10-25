using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

using Nexaflow.Analyzers.Content;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// What <see cref="ContentBuilderAnalyzer"/> holds a builder to: made from the six things every builder is made from, told
/// nothing after, offering nothing the base does not, and made by nothing but the engine — each put to it in a compilation of
/// its own, against a base of the same shape as the real one.
/// </summary>
[TestClass]
[NoCoverage("whole-repo architecture guard; maps to no single product node")]
public class ContentBuilderAnalyzerTests
{
    /// <summary>The base every builder derives from, as the real one is shaped.</summary>
    private const string Base = """
        namespace Nexaflow.Markdown.Layout
        {
            public sealed class ContentReading { }
            public sealed class EditState { }
            public sealed class PortableStyle { }
            public interface ILayoutMedium { }
            public sealed class Medium : ILayoutMedium { }
            public sealed class PortableHost { }
            public sealed class PortableLaid { }

            public abstract class PortableContentBuilder
            {
                protected PortableContentBuilder(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly, ILayoutMedium medium, PortableHost host) { }
                public string Source => "";
                public PortableLaid Lay(double room = double.PositiveInfinity) => new PortableLaid();
                protected abstract PortableLaid Build();
            }
        }
        """;

    private const string Usings = "using Nexaflow.Markdown.Layout;\n";

    /// <summary>A builder in the one shape.</summary>
    private const string Shaped = """
        sealed class Pie(ContentReading r, EditState s, PortableStyle f, bool o, ILayoutMedium m, PortableHost h) : PortableContentBuilder(r, s, f, o, m, h)
        {
            protected override PortableLaid Build() => new PortableLaid();
        }
        """;

    private static IReadOnlyList<string> Found(params string[] sources)
    {
        var trees = new[] { Base }.Concat(sources.Select(source => Usings + source)).Select(source => CSharpSyntaxTree.ParseText(source));
        // The runtime's own assemblies only: this process has MSTest loaded, and a compilation that can see it is one of tests.
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => string.Equals(Path.GetDirectoryName(path), runtime, StringComparison.OrdinalIgnoreCase))
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create("Builders", trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.AreEqual(0, compilation.GetDiagnostics().Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                        string.Join("\n", compilation.GetDiagnostics()));

        return [.. compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ContentBuilderAnalyzer()))
                              .GetAnalyzerDiagnosticsAsync().Result
                              .Select(diagnostic => diagnostic.Id)
                              .Order()];
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ABuilderInTheOneShapeIsNothingToSay() =>
        CollectionAssert.AreEqual(Array.Empty<string>(), Found(Shaped).ToList());

    [TestMethod]
    [TestCategory("Unit")]
    public void ABuilderToldASeventhThingIsNotTheShape() =>
        CollectionAssert.AreEqual(new[] { "NXCB001" }, Found("""
            sealed class Pie(ContentReading r, EditState s, PortableStyle f, bool o, ILayoutMedium m, PortableHost h, int slices) : PortableContentBuilder(r, s, f, o, m, h)
            {
                protected override PortableLaid Build() => new PortableLaid();
            }
            """).ToList());

    [TestMethod]
    [TestCategory("Unit")]
    public void ABuilderMadeTwoWaysIsNotTheShape() =>
        CollectionAssert.AreEqual(new[] { "NXCB001" }, Found("""
            sealed class Pie : PortableContentBuilder
            {
                public Pie(ContentReading r, EditState s, PortableStyle f, bool o, ILayoutMedium m, PortableHost h) : base(r, s, f, o, m, h) { }
                public Pie(ContentReading r, EditState s, PortableStyle f, ILayoutMedium m, PortableHost h) : base(r, s, f, true, m, h) { }
                protected override PortableLaid Build() => new PortableLaid();
            }
            """).ToList());

    [TestMethod]
    [TestCategory("Unit")]
    public void ABuilderToldSomethingAfterItIsMadeIsSaidToBe() =>
        CollectionAssert.AreEqual(new[] { "NXCB002" }, Found("""
            sealed class Pie(ContentReading r, EditState s, PortableStyle f, bool o, ILayoutMedium m, PortableHost h) : PortableContentBuilder(r, s, f, o, m, h)
            {
                internal int Slices { get; set; }
                protected override PortableLaid Build() => new PortableLaid();
            }
            """).ToList());

    [TestMethod]
    [TestCategory("Unit")]
    public void ABuilderOfferingANameOfItsOwnIsSaidToBe_WhileWhatTheBaseNamesIsItsToOffer() =>
        CollectionAssert.AreEqual(new[] { "NXCB003" }, Found("""
            sealed class Pie(ContentReading r, EditState s, PortableStyle f, bool o, ILayoutMedium m, PortableHost h) : PortableContentBuilder(r, s, f, o, m, h)
            {
                public static PortableLaid Draws(string source) => new PortableLaid();
                public new string Source => "pie";
                protected override PortableLaid Build() => new PortableLaid();
            }
            """).ToList());

    [TestMethod]
    [TestCategory("Unit")]
    public void ABuilderMadeAnywhereIsSaidToBe() =>
        CollectionAssert.AreEqual(new[] { "NXCB004" }, Found(Shaped, """
            static class Somewhere
            {
                public static PortableLaid Draw(ContentReading r) => new Pie(r, new EditState(), new PortableStyle(), true, new Medium(), new PortableHost()).Lay();
            }
            """).ToList());

    [TestMethod]
    [TestCategory("Unit")]
    public void TestsMayMakeWhatBuildersTheyLike() =>
        CollectionAssert.AreEqual(Array.Empty<string>(), Found("""
            namespace Microsoft.VisualStudio.TestTools.UnitTesting { public sealed class TestClassAttribute : System.Attribute { } }

            sealed class Box(ContentReading r, bool fail) : PortableContentBuilder(r, new EditState(), new PortableStyle(), true, new Medium(), new PortableHost())
            {
                public static PortableLaid Built(ContentReading r) => new Box(r, fail: true).Lay();
                protected override PortableLaid Build() => new PortableLaid();
            }
            """).ToList());

    [TestMethod]
    [TestCategory("Unit")]
    public void WhereThereIsNoBuilderThereIsNothingToHold() =>
        CollectionAssert.AreEqual(Array.Empty<string>(), CSharpCompilation
            .Create("Nothing", [CSharpSyntaxTree.ParseText("class Pie { public int Slices { get; set; } }")],
                    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)])
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ContentBuilderAnalyzer()))
            .GetAnalyzerDiagnosticsAsync().Result
            .Select(diagnostic => diagnostic.Id)
            .ToList());
}
