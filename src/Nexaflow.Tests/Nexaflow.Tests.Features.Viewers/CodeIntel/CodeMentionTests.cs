using System.Linq;
using Nexaflow.Syntax;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.CodeIntel;

/// <summary>
/// Covers type mentions — every way a member names a type without calling or constructing it. The graph
/// recorded calls and object creations only, so a type reached by a member access, an enum value or a generic
/// argument left no edge at all: it read as dead, and "what references this type" quietly under-reported.
///
/// <para>The tests worth having here are the ones that pin down where the line is drawn, because a mention is
/// the loosest thing the extractor emits: what counts as a type position, what a qualified name contributes,
/// and which receivers are deliberately not taken.</para>
/// </summary>
[TestClass]
[CoversNode("graph-type-mentions")]
public class CodeMentionTests
{
    private static string[] Mentions(string body, string members = "")
    {
        var src = $$"""
                    namespace Demo;

                    public class Subject
                    {
                        {{members}}
                        public void Go()
                        {
                            {{body}}
                        }
                    }
                    """;
        return [.. new CodeRelationshipExtractor().Extract("c-sharp", src)
                     .Refs.Where(r => r.Kind == RawRefKind.Mention)
                     .Select(r => r.Name)];
    }

    [TestMethod]
    public void AMemberAccessReceiverIsAMention()
    {
        // ElevatedOps.DiskMount is neither a call nor a construction. This one site is why a whole family of
        // static classes and enums read as unreached.
        CollectionAssert.Contains(Mentions("var op = ElevatedOps.DiskMount;"), "ElevatedOps");
    }

    [TestMethod]
    public void AnEnumValueReachesItsEnum()
    {
        CollectionAssert.Contains(Mentions("Log(Severity.Warning);"), "Severity");
    }

    [TestMethod]
    public void ALowercaseReceiverIsNotATypeGuess()
    {
        // `config.Value` is a field or local, not a type. Taking it would invent references to any type that
        // happened to share the name.
        var mentions = Mentions("var v = config.Value;");
        CollectionAssert.DoesNotContain(mentions, "config");
        CollectionAssert.DoesNotContain(mentions, "Value");
    }

    [TestMethod]
    public void GenericArgumentsAreMentioned_NotJustTheContainer()
    {
        var mentions = Mentions("Dictionary<string, List<RowVm>> rows = null;");
        CollectionAssert.Contains(mentions, "RowVm", "the interesting type is the argument, not the collection");
        CollectionAssert.Contains(mentions, "Dictionary");
    }

    [TestMethod]
    public void APredefinedTypeIsNotAMention()
    {
        CollectionAssert.DoesNotContain(Mentions("int count = 0;"), "int");
    }

    [TestMethod]
    public void AQualifiedNameContributesOnlyItsLastSegment()
    {
        // System.Text.StringBuilder names one type. Harvesting every segment would put `System` and `Text` into
        // the resolver, where they'd collide with real repo types of those names.
        var mentions = Mentions("System.Text.StringBuilder sb = null;");
        CollectionAssert.Contains(mentions, "StringBuilder");
        CollectionAssert.DoesNotContain(mentions, "System");
        CollectionAssert.DoesNotContain(mentions, "Text");
    }

    [TestMethod]
    public void CastsPatternsAndTypeofAreTypePositions()
    {
        var mentions = Mentions("""
                                var a = (Widget)thing;
                                if (thing is Gadget g) { }
                                var t = typeof(Sprocket);
                                """);
        CollectionAssert.Contains(mentions, "Widget");
        CollectionAssert.Contains(mentions, "Gadget");
        CollectionAssert.Contains(mentions, "Sprocket");
    }

    [TestMethod]
    public void AFieldsDeclaredTypeIsMentioned()
    {
        // A field's member node is its declarator, and the type sits on the enclosing declaration — neither on
        // it nor below it. Missing this would lose every injected dependency held in a readonly field.
        var refs = new CodeRelationshipExtractor()
            .Extract("c-sharp", "public class Subject { private readonly ElevatedOps _ops; }")
            .Refs.Where(r => r.Kind == RawRefKind.Mention).ToList();

        Assert.AreEqual("ElevatedOps", refs.Single().Name);
        Assert.AreEqual("T:Subject/F:_ops", refs.Single().FromAst);
    }

    [TestMethod]
    public void OneMentionPerTypePerMember()
    {
        // A loop naming the same type forty times is one relationship, not forty edges.
        var mentions = Mentions("""
                                Widget a = null;
                                Widget b = null;
                                var c = (Widget)a;
                                """);
        Assert.AreEqual(1, mentions.Count(m => m == "Widget"));
    }

    [TestMethod]
    public void ConstructingATypeStillReportsTheStrongerKind()
    {
        // `new Foo()` is both a construction and a mention of Foo. The extractor reports both and the graph
        // builder keeps only `instantiates` — the extractor's job is to see, not to choose.
        var refs = new CodeRelationshipExtractor()
            .Extract("c-sharp", "public class Subject { void Go() { var x = new Foo(); } }").Refs;

        Assert.IsTrue(refs.Any(r => r.Kind == RawRefKind.New && r.Name == "Foo"));
        Assert.IsTrue(refs.Any(r => r.Kind == RawRefKind.Mention && r.Name == "Foo"));
    }

    [TestMethod]
    public void OnlyCSharpIsRead()
    {
        Assert.AreEqual(0, new CodeRelationshipExtractor().Extract("xaml", "<Grid x:Name=\"A\"/>").Refs.Count);
    }
}
