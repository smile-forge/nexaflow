using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Class;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A <c>classDiagram</c> block drawn on the shared layout tree: each class as its three compartments, the classes in the ranks
/// the relations put them in, the namespaces holding what is written inside them, and the relations over all of it.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("class-diagram")]
public class ClassBuilderTests : MermaidBuilderContract
{
    private const string Intro =
        "classDiagram\n  note \"From Duck till Zebra\"\n  Animal <|-- Duck\n  Animal <|-- Fish\n  Animal : +int age\n"
        + "  Animal: +isMammal()\n  class Duck{\n    +String beakColor\n    +swim()\n  }\n  class Fish{\n    -int sizeInFeet\n  }";

    private const string Spaces =
        "classDiagram\nnamespace BaseShapes {\n  class Triangle\n  class Rectangle {\n    double width\n    double height\n  }\n}";

            /// <summary>One relation of each kind Mermaid documents.</summary>
            private const string Related =
                "classDiagram\nclassA <|-- classB : Inheritance\nclassC *-- classD : Composition\nclassE o-- classF : Aggregation\n"
                + "classG --> classH : Association\nclassI -- classJ : Link(Solid)\nclassK ..> classL : Dependency\n"
                + "classM ..|> classN : Realization\nclassO .. classP : Link(Dashed)";

    public override MermaidDiagram Diagram => MermaidDiagram.Class;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("the documentation's own", Intro),
        ("namespaces", Spaces),
        ("one relation of each kind", Related),
        ("laid out left to right", "classDiagram\n  direction LR\n  A --> B --> C"),
        ("laid out bottom up", "classDiagram\n  direction BT\n  A --> B"),
        ("laid out right to left", "classDiagram\n  direction RL\n  A --> B"),
        ("a class on its own", "classDiagram\n  class Alone"),
        ("a class with nothing in it", "classDiagram\n  class Alone{}"),
        ("a class with no members box", "---\nconfig:\n  class:\n    hideEmptyMembersBox: true\n---\nclassDiagram\n  class Alone"),
        ("a class with a label", "classDiagram\n  class Animal[\"Animal with a label\"]"),
        ("a class with type parameters", "classDiagram\n  class Square~Shape~ {\n    List~int~ position\n    getPoints() List~int~\n  }"),
        ("an annotation over the name", "classDiagram\n  class Shape {\n    <<interface>>\n    draw()\n  }"),
        ("members either band", "classDiagram\n  class A {\n    +int age\n    -String name\n    +grow()$\n    +draw()*\n  }"),
        ("counts at either end", "classDiagram\n  Customer \"1\" --> \"*\" Ticket : raises"),
        ("a lollipop either end", "classDiagram\n  bar ()-- foo\n  foo --() baz"),
        ("a relation both ways", "classDiagram\n  Animal <|--|> Zebra"),
        ("namespaces nested", "classDiagram\n  namespace Outer {\n    namespace Inner {\n      class One\n    }\n    class Two\n  }"),
        ("a namespace written with dots", "classDiagram\n  namespace A.B.C {\n    class One\n  }"),
        ("a note either way", "classDiagram\n  class A\n  note for A \"About A\"\n  note \"About nothing\""),
        ("classes and styles",
         "classDiagram\n  class A:::blue\n  class B\n  A --> B\n  classDef blue fill:#6e6ce6,color:white\n"
         + "  style B stroke:#f66,stroke-width:3px"),
        ("a ring of classes", "classDiagram\n  A --> B\n  B --> C\n  C --> A"),
        ("a class joined to itself", "classDiagram\n  A --> A\n  A --> B"),
        ("a spacing of its own",
         "---\nconfig:\n  class:\n    nodeSpacing: 80\n    rankSpacing: 80\n---\nclassDiagram\n  A --> B\n  A --> C"),
        ("where pressing a class leads", "classDiagram\n  A --> B\n  click A href \"https://example.com\" \"Go there\""),
        ("a member pointing at its own line", "classDiagram\n  class A {\n    +draw() @@nx:line#42\n  }"),
        ("still being written", "classDiagram\n  A : \n  B --> "),
        ("what nobody means to write", "classDiagram\n  class A\n  }\n  style nowhere fill:#969"),
        ("nothing to draw", "classDiagram"),
    ];

    [TestMethod]
    public void ARelationPutsWhatItReachesInTheRankBeyondWhatItLeaves() => UiThread.Run(() =>
    {
        var down = Boxes("classDiagram\n  A --> B");
        Assert.IsTrue(down["B"].Top > down["A"].Bottom, $"B is below A: {down["A"]} then {down["B"]}");

        var right = Boxes("classDiagram\n  direction LR\n  A --> B");
        Assert.IsTrue(right["B"].Left > right["A"].Right, $"B is right of A: {right["A"]} then {right["B"]}");
    });

    [TestMethod]
    public void AClassDrawsItsFieldsAboveItsMethodsAndBothBelowItsName() => UiThread.Run(() =>
    {
        var laid = Build("classDiagram\n  class A {\n    +int age\n    +grow()\n  }");
        var box = Pieces(laid, ClassPiece.Class).Single();

        var name = Words(box, "A");
        var field = Words(box, "+int age");
        var method = Words(box, "+grow()");

        Assert.IsTrue(field.Top >= name.Bottom, $"the field is under the name: {name} then {field}");
        Assert.IsTrue(method.Top >= field.Bottom, $"the method is under the field: {field} then {method}");
    });

    [TestMethod]
    public void AMemberTheClassItselfHoldsIsUnderlinedAndOneNothingImplementsIsSlanted() => UiThread.Run(() =>
    {
        var plain = Rules(Build("classDiagram\n  class A {\n    +grow()\n  }"));
        var fixture = Rules(Build("classDiagram\n  class A {\n    +grow()$\n  }"));

        Assert.AreEqual(plain + 1, fixture, "a static member is drawn with a rule under it");
    });

    /// <summary>How many lines are drawn inside the class — the rules dividing it, and any member underlined.</summary>
    private static int Rules(Laid laid) =>
        Pieces(laid, ClassPiece.Class).Single().SelfAndDescendants().Sum(piece => piece.Marks.Length is var _ ? Lines(piece) : 0);

    private static int Lines(Piece piece)
    {
        var lines = 0;
        foreach (var mark in piece.Marks) if (mark is LineMark) lines++;

        return lines;
    }

    [TestMethod]
    public void AClassWithNoMembersKeepsABandForThemUnlessTheFrontMatterSaysNot() => UiThread.Run(() =>
    {
        var kept = Pieces(Build("classDiagram\n  class A"), ClassPiece.Class).Single().Bounds;
        var hidden = Pieces(Build("---\nconfig:\n  class:\n    hideEmptyMembersBox: true\n---\nclassDiagram\n  class A"),
                            ClassPiece.Class).Single().Bounds;

        Assert.IsTrue(hidden.Height < kept.Height, $"the box with no bands is shallower: {hidden} against {kept}");
    });

    [TestMethod]
    public void ANamespacesBoxHoldsTheClassesWrittenInsideIt() => UiThread.Run(() =>
    {
        var laid = Build(Spaces);
        var box = Pieces(laid, ClassPiece.Space).Single().Bounds;

        foreach (var (name, bounds) in Boxes(laid))
            Assert.IsTrue(Holds(box, bounds), $"{name} is inside the namespace: {bounds} in {box}");
    });

    [TestMethod]
    public void ANoteIsHeldToTheClassItIsAboutByALineOfItsOwn() => UiThread.Run(() =>
    {
        const string source = "classDiagram\n  class Duck\n  class Zoo\n  Zoo o-- Duck\n  note for Duck \"can fly\"\n  note \"about nothing\"";
        var laid = Build(source);

        var tether = Pieces(laid, ClassPiece.Tether).Single();
        var note = Pieces(laid, ClassPiece.Note).Single(piece => Written(source, piece.Part).StartsWith("note for", System.StringComparison.Ordinal));
        var reach = tether.Bounds;
        reach.Inflate(2, 2);

        Assert.AreEqual(note.Part!.Start, tether.Part!.Start, "the line stands for the note, and a note about no class has none");
        Assert.IsTrue(reach.IntersectsWith(note.Bounds), $"it runs from the note: {tether.Bounds} to {note.Bounds}");
        Assert.IsTrue(reach.IntersectsWith(Boxes(laid)["Duck"]), $"to the class it is about: {tether.Bounds} to {Boxes(laid)["Duck"]}");
    });

    [TestMethod]
    public void EveryOneOfAKindIsDrawnAlike_AndANoteApartFromThem() => UiThread.Run(() =>
    {
        var laid = Build("classDiagram\n  class Duck\n  class Zoo\n  Zoo o-- Duck\n  note for Duck \"can fly\"");
        var classes = Pieces(laid, ClassPiece.Class).Select(Fill).ToList();
        var note = Fill(Pieces(laid, ClassPiece.Note).Single());

        Assert.IsNotNull(classes[0], "a class nothing styles is filled");
        Assert.AreEqual(1, classes.Distinct().Count(), "and every one of them alike");
        Assert.AreNotEqual(classes[0], note, "a note in a colour apart from the classes");

        var spaces = Pieces(Build("classDiagram\nnamespace A {\n  class X\n}\nnamespace B {\n  class Y\n}"), ClassPiece.Space).Select(Fill).ToList();
        Assert.AreEqual(2, spaces.Count);
        Assert.AreEqual(1, spaces.Distinct().Count(), "every namespace alike, whatever order it is written in");
    });

    [TestMethod]
    public void AMemberThatPointsSomewhereIsAPieceOfItsOwnCarryingTheLink() => UiThread.Run(() =>
    {
        var laid = Build("classDiagram\n  class A {\n    +draw() @@nx:line#42\n    +plain()\n  }");
        var linked = Pieces(laid, ClassPiece.Member).Single();

        Assert.AreEqual("nx:line#42", linked.Acts?.Click?.Target);
        Assert.IsTrue(Saying(linked).Contains("+draw()"), "the link is on the member it was written on");
    });

    [TestMethod]
    public void WhereAClickLineSaysAClassLeadsThePressOnItMeansTheLink() => UiThread.Run(() =>
    {
        var laid = Build("classDiagram\n  class A\n  click A href \"https://example.com\" \"Go there\"");
        var box = Pieces(laid, ClassPiece.Class).Single();

        Assert.AreEqual(LayoutVerbs.Navigate, box.Acts?.Click?.Verb);
        Assert.AreEqual("https://example.com", box.Acts?.Click?.Target);
        Assert.AreEqual("Go there", box.Acts?.Click?.Tip);
    });

    [TestMethod]
    public void NothingDrawnLeadsAnywhereUnlessSomethingSaidItDoes() => UiThread.Run(() =>
    {
        var laid = Build("classDiagram\n  class A {\n    +draw()\n  }\n  A --> B");

        Assert.IsFalse(laid.Tree.Root.SelfAndDescendants().Any(piece => piece.Acts is not null));
    });

    // ── What it works with ──────────────────────────────────────────────────

    private static Laid Build(string source, double room = 900) =>
        new ClassBuilder(MermaidBuilders.Read(source), EditState.For(source), StyleFormat.Dark, isReadOnly: true).Lay(room);

    /// <summary>Every class drawn, by what its name says.</summary>
    private static Dictionary<string, Rect> Boxes(string source) => Boxes(Build(source));

    private static Dictionary<string, Rect> Boxes(Laid laid)
    {
        var boxes = new Dictionary<string, Rect>();

        foreach (var piece in Pieces(laid, ClassPiece.Class))
            if (Saying(piece).FirstOrDefault() is { Length: > 0 } said)
                boxes[said] = piece.Bounds;

        return boxes;
    }

    /// <summary>Where the words saying this were drawn inside a piece.</summary>
    private static Rect Words(Piece piece, string said) =>
        Said(piece).First(words => words.Words!.Glyphs.Text == said).Bounds;

    /// <summary>The words drawn under a piece, in the order they were drawn.</summary>
    private static IEnumerable<Piece> Said(Piece piece) =>
        piece.SelfAndDescendants().Where(part => part.Kind == MermaidPiece.Words && part.Words is not null);

    /// <summary>What each run of words under a piece says, in the order they were drawn.</summary>
    private static IReadOnlyList<string> Saying(Piece piece) => [.. Said(piece).Select(words => words.Words!.Glyphs.Text)];

    private static bool Holds(Rect over, Rect inner) =>
        inner.Left >= over.Left - 1 && inner.Right <= over.Right + 1 && inner.Top >= over.Top - 1 && inner.Bottom <= over.Bottom + 1;
}
