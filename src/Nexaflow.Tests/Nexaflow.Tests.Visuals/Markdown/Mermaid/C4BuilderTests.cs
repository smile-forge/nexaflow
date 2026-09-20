using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.C4;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;

using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.C4;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// What a structural C4 diagram draws: the element cards, the boundaries holding them, the relationships between them, and
/// the key.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("c4-diagram")]
public class C4BuilderTests : MermaidBuilderContract
{
    /// <summary>The container diagram our own documentation shows.</summary>
    private const string Intro =
        "C4Container\ntitle Container diagram for the Internet Banking System\n\n"
        + "Person(customer, \"Personal Banking Customer\", \"A customer of the bank.\")\n\n"
        + "System_Boundary(c1, \"Internet Banking\", \"System\") {\n"
        + "  Container(spa, \"Single-Page App\", \"JavaScript, Angular\", \"Provides banking functionality in the browser.\")\n"
        + "  Container(api, \"API Application\", \"Java, Docker\", \"Provides banking functionality via a JSON/HTTPS API.\")\n"
        + "  ContainerDb(db, \"Database\", \"SQL Database\", \"Stores user registration information and access logs.\")\n}\n\n"
        + "Rel(customer, spa, \"Visits bigbank.com/ib using\", \"HTTPS\")\nRel(spa, api, \"Makes API calls to\", \"JSON/HTTPS\")\n"
        + "Rel(api, db, \"Reads from and writes to\", \"JDBC\")";

    public override MermaidDiagram Diagram => MermaidDiagram.C4;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("the documentation's own", Intro),
        ("every kind of element",
         "C4Container\nPerson(p, \"A person\")\nPerson_Ext(q, \"Somebody else's\")\nSystem(s, \"A system\")\n"
         + "SystemDb(d, \"A store\")\nSystemQueue(u, \"A queue\")\nContainer(c, \"A container\", \"Java\", \"What it does\")\n"
         + "Component(m, \"A component\", \"Spring\")\nRel(p, s, \"Uses\", \"HTTPS\")"),
        ("deployment nodes nested three deep",
         "C4Deployment\nDeployment_Node(plc, \"Big Bank plc\", \"Data centre\") {\n"
         + "  Deployment_Node(dn, \"bigbank-api\", \"Ubuntu 16.04 LTS\") {\n"
         + "    Container(api, \"API\", \"Java, Docker\")\n  }\n  ContainerDb(db, \"Database\", \"Oracle\")\n}\n"
         + "Rel(api, db, \"Reads from\", \"JDBC\")"),
        ("a key under it",
         "C4Container\nSHOW_LEGEND()\nAddElementTag(\"v1\", $bgColor=\"#1168bd\", $legendText=\"Version one\")\n"
         + "Person(a, \"A\")\nSystem(b, \"B\")\nSystemDb(c, \"C\")\nRel(a, b, \"Uses\")"),
        ("colours of its own",
         "C4Context\nPerson(a, \"A\")\nSystem(b, \"B\")\nUpdateElementStyle(\"person\", $bgColor=\"#08427b\", $fontColor=\"#ffffff\")\n"
         + "Rel(a, b, \"Uses\")\nUpdateRelStyle(a, b, $textColor=\"#ff6b6b\", $lineColor=\"#ff6b6b\", $lineStyle=DashedLine())"),
        ("laid out across rather than down",
         "C4Component\nLAYOUT_LEFT_RIGHT()\nPerson(a, \"A\")\nContainer_Boundary(api, \"API\") {\n"
         + "  Component(c, \"Sign In\", \"Spring MVC\")\n}\nRel(a, c, \"Submits to\", \"HTTPS\")"),
        ("numbered interactions",
         "C4Dynamic\nPerson(a, \"A\")\nContainer(b, \"B\", \"Angular\")\nRel(a, b, \"One\", $index=Index())\n"
         + "Rel_Back(a, b, \"Two\", $index=Index())"),
        ("a relationship naming something nobody declared", "C4Context\nRel(ghost, other, \"Uses\")"),
        ("the wrappers a pasted diagram brings",
         "C4Context\n@startuml\n!include C4_Context.puml\n' a note\nPerson(a, \"A\")\n@enduml"),
        ("still being written", "C4Context\nRel(, , \"\")"),
        ("what nobody means to write", "C4Context\n??? !!!\n}"),
        ("nothing to draw", "C4Context"),
    ];

    [TestMethod]
    public void AnElementIsACardSayingWhatItIsAndWhatItDoes() => UiThread.Run(() =>
    {
        var laid = Lay("C4Container\nContainer(spa, \"Single-Page App\", \"Angular\", \"Runs in the browser.\")");
        var card = Pieces(laid, C4Piece.Element).Single();

        var name = Words(card).First(words => words.Words!.Glyphs.Text == "Single-Page App").Bounds;
        var says = Words(card).First(words => words.Words!.Glyphs.Text == "[Container: Angular]").Bounds;
        var does = Words(card).First(words => words.Words!.Glyphs.Text.StartsWith("Runs", StringComparison.Ordinal)).Bounds;

        Assert.IsTrue(says.Top >= name.Bottom - 1, $"what it is goes under its name: {name} then {says}");
        Assert.IsTrue(does.Top >= says.Bottom - 1, $"and what it does under that: {says} then {does}");
    });

    [TestMethod]
    public void ABoundaryIsTheBoxRoundWhatIsWrittenInsideIt() => UiThread.Run(() =>
    {
        var laid = Lay("C4Container\nPerson(a, \"A\")\nSystem_Boundary(b, \"The bank\", \"System\") {\n"
                       + "  Container(s, \"Core\", \"Java\")\n}\nRel(a, s, \"Uses\")");

        var box = Pieces(laid, C4Piece.Boundary).Single();
        var cards = Pieces(laid, C4Piece.Element).ToList();

        var inside = cards.Single(card => card.SelfAndDescendants().Any(part => part.Words?.Glyphs.Text == "Core"));
        var outside = cards.Single(card => card.SelfAndDescendants().Any(part => part.Words?.Glyphs.Text == "A"));

        Assert.IsTrue(box.Bounds.Contains(inside.Bounds), $"what is written in it is drawn in it: {box.Bounds} holds {inside.Bounds}");
        Assert.IsFalse(box.Bounds.IntersectsWith(outside.Bounds), "and what is written outside it is not");

        Assert.IsTrue(box.SelfAndDescendants().Any(part => part.Words?.Glyphs.Text == "The bank"), "its name is written on it");
        Assert.IsTrue(box.SelfAndDescendants().Any(part => part.Kind == C4Piece.Stereotype), "and what it is under that");
    });

    [TestMethod]
    public void ABoundaryHoldsTheRelationshipsThatOnlyExistInsideIt() => UiThread.Run(() =>
    {
        var laid = Lay("C4Container\nPerson(a, \"A\")\nSystem_Boundary(b, \"Bank\") {\n  Container(s, \"Core\", \"Java\")\n"
                       + "  ContainerDb(d, \"Store\", \"SQL\")\n}\nRel(a, s, \"Uses\")\nRel(s, d, \"Reads\")");

        var box = Pieces(laid, C4Piece.Boundary).Single();
        var held = box.SelfAndDescendants().Count(part => part.Kind == C4Piece.Relation);

        Assert.AreEqual(1, held, "the line drawn only inside the boundary is held by it, and the one crossing into it is not");
        Assert.AreEqual(2, Pieces(laid, C4Piece.Relation).Count, "both are drawn");
    });

    [TestMethod]
    public void WhatARelationshipIsDoneWithIsWrittenUnderWhatItSays() => UiThread.Run(() =>
    {
        var laid = Lay("C4Context\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"Visits\", \"HTTPS\")");
        var said = Pieces(laid, C4Piece.Label).Single();

        var says = Words(said).First(words => words.Words!.Glyphs.Text == "Visits").Bounds;
        var with = Words(said).First(words => words.Words!.Glyphs.Text == "[HTTPS]").Bounds;

        Assert.IsTrue(with.Top >= says.Bottom - 1, $"what it is done with goes under what it says: {says} then {with}");
    });

    [TestMethod]
    public void WhatIsWrittenOnALineCrossingIntoABoundaryClearsItsName() => UiThread.Run(() =>
    {
        var laid = Lay("C4Container\nPerson(customer, \"Personal Banking Customer\")\n"
                       + "System_Boundary(c1, \"Internet Banking\", \"System\") {\n  Container(spa, \"Single-Page App\", \"Angular\")\n}\n"
                       + "Rel(customer, spa, \"Visits bigbank.com/ib using\", \"HTTPS\")");

        var box = Pieces(laid, C4Piece.Boundary).Single();
        var name = box.SelfAndDescendants().First(part => part.Words?.Glyphs.Text == "Internet Banking").Bounds;
        var said = Pieces(laid, C4Piece.Label).Single().Bounds;

        Assert.IsFalse(said.IntersectsWith(name),
                       $"a boundary keeps the top of itself for its own name: {name} against {said}");
    });

    [TestMethod]
    public void AKeyIsDrawnUnderTheDiagramWhereItAsksForOne() => UiThread.Run(() =>
    {
        var laid = Lay("C4Container\nSHOW_LEGEND()\nPerson(a, \"A\")\nContainerDb(b, \"B\", \"SQL\")\nRel(a, b, \"Uses\")");
        var key = Pieces(laid, MermaidPiece.Legend).Single();

        var rows = key.SelfAndDescendants()
                      .Where(part => part.Kind == MermaidPiece.Words && part.Words is not null)
                      .Select(part => part.Words!.Glyphs.Text)
                      .ToList();

        CollectionAssert.AreEqual(new[] { "Person", "Container (database)" }, rows);
        Assert.IsTrue(key.Bounds.Top >= Pieces(laid, C4Piece.Element).Max(card => card.Bounds.Bottom) - 1,
                      "and it is under the diagram");
    });

    [TestMethod]
    public void LayingItLeftToRightPutsTheEndsOfARelationshipBesideOneAnother() => UiThread.Run(() =>
    {
        const string body = "\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"Uses\")";

        Rect Card(string source, string name) =>
            Pieces(Lay(source), C4Piece.Element).Single(card => card.SelfAndDescendants().Any(part => part.Words?.Glyphs.Text == name)).Bounds;

        Assert.IsTrue(Card("C4Context" + body, "B").Top > Card("C4Context" + body, "A").Bottom - 1, "down the page by default");

        const string across = "C4Context\nLAYOUT_LEFT_RIGHT()" + body;
        Assert.IsTrue(Card(across, "B").Left > Card(across, "A").Right - 1, "and across it where it says so");
    });

    // ── The grading ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-elements")]
    public void TheAbstractionLevelsAreGradedFromTheThemesOwnAccent()
    {
        var ink = C4Grading.Of(MarkdownPalette.Dark);

        double Lit(int tone) => DiagramColour.Luminance(DiagramColour.ColorOf(ink.Band(tone), Colors.Black));

        // C4's information is the grading: deeper for the outer abstraction, lighter as you go in.
        Assert.IsTrue(Lit((int)C4Level.Person) < Lit((int)C4Level.System), "a person is deeper than a system");
        Assert.IsTrue(Lit((int)C4Level.System) < Lit((int)C4Level.Container), "a system than a container");
        Assert.IsTrue(Lit((int)C4Level.Container) < Lit((int)C4Level.Component), "and a container than a component");
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void TheGradingIsMadeFromTheThemeRatherThanFixedHex()
    {
        var dark = C4Grading.Of(MarkdownPalette.Dark).Band((int)C4Level.Container);
        var light = C4Grading.Of(MarkdownPalette.Light).Band((int)C4Level.Container);

        Assert.AreNotEqual(DiagramColour.ColorOf(dark, Colors.Black), DiagramColour.ColorOf(light, Colors.Black),
                           "a retheme retunes a C4 diagram rather than leaving one region stubbornly cornflower blue");
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void WhatIsWrittenWinsOverTheGrading_AndTheInkIsChosenToReadOnIt()
    {
        var ink = C4Grading.Of(MarkdownPalette.Dark);

        var (fill, stroke, text) = ink.Card((int)C4Level.Container, "#969", "#333", "#fff");
        Assert.AreEqual(Color.FromRgb(0x99, 0x66, 0x99), DiagramColour.ColorOf(fill, Colors.Black));
        Assert.AreEqual(Color.FromRgb(0x33, 0x33, 0x33), DiagramColour.ColorOf(stroke, Colors.Black));
        Assert.AreEqual(Colors.White, DiagramColour.ColorOf(text, Colors.Black));

        // On both palettes, because the trap is a theme whose text brush matches the fill's own darkness.
        foreach (var palette in new[] { MarkdownPalette.Dark, MarkdownPalette.Light })
        {
            var on = C4Grading.Of(palette);

            var onWhite = DiagramColour.Luminance(DiagramColour.ColorOf(on.Card(0, "#ffffff", null, null).Ink, Colors.Red));
            Assert.IsTrue(onWhite < 100, $"a white card takes dark ink (luminance {onWhite})");

            var onBlack = DiagramColour.Luminance(DiagramColour.ColorOf(on.Card(0, "#000000", null, null).Ink, Colors.Red));
            Assert.IsTrue(onBlack > 180, $"a black card takes light ink (luminance {onBlack})");

            var (person, _, reading) = on.Card((int)C4Level.Person, null, null, null);
            var gap = Math.Abs(DiagramColour.Luminance(DiagramColour.ColorOf(reading, Colors.Red))
                               - DiagramColour.Luminance(DiagramColour.ColorOf(person, Colors.Red)));
            Assert.IsTrue(gap > 90, $"and the deepest of the grading is legible too (gap {gap})");
        }
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void ThemeTokensWinOverTheDerivedGrading()
    {
        var pinned = Color.FromRgb(0x43, 0x8D, 0xD5);
        var dark = MarkdownPalette.Dark;
        var palette = new MarkdownPalette
        {
            Text = dark.Text, TextMuted = dark.TextMuted, Accent = dark.Accent, Heading = dark.Heading,
            DefTerm = dark.DefTerm, Citation = dark.Citation, Marked = dark.Marked, Success = dark.Success,
            Warning = dark.Warning, Danger = dark.Danger, Important = dark.Important, CodeBg = dark.CodeBg,
            CodeBorder = dark.CodeBorder, QuoteBg = dark.QuoteBg, Hr = dark.Hr, TableBorder = dark.TableBorder,
            TableHeaderBg = dark.TableHeaderBg, TableAltRowBg = dark.TableAltRowBg, FigureBorder = dark.FigureBorder,
            FigureBg = dark.FigureBg, FooterBg = dark.FooterBg,
            C4Container = DiagramColour.Frozen(pinned),
        };

        var ink = C4Grading.Of(palette);

        Assert.AreEqual(pinned, DiagramColour.ColorOf(ink.Band((int)C4Level.Container), Colors.Black));
        Assert.AreNotEqual(pinned, DiagramColour.ColorOf(ink.Band((int)C4Level.Component), Colors.Black),
                           "and the levels it did not pin still derive");
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void SomebodyElsesIsTheOneMutedColourWhateverLevelItSitsAt() => UiThread.Run(() =>
    {
        var ink = C4Grading.Of(MarkdownPalette.Dark);
        var muted = DiagramColour.ColorOf(ink.Band(C4Elements.External), Colors.Black);

        foreach (var level in new[] { C4Level.Person, C4Level.System, C4Level.Container, C4Level.Component })
            Assert.AreEqual(muted, DiagramColour.ColorOf(ink.Band(C4Elements.Banded(level, external: true)), Colors.Black),
                            $"{level}");
    });

    private static IEnumerable<Piece> Words(Piece piece) =>
        piece.SelfAndDescendants().Where(part => part.Words is not null);
}
