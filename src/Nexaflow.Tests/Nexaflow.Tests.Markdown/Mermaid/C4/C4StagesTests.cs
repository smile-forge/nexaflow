using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.C4;
using Nexaflow.Markdown.Mermaid.Sequence;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.C4;

/// <summary>
/// What a C4 block's stages say its macros mean, the same for a graph and a timeline: what each element's card says about
/// itself and is painted with, what each relationship joins and is numbered, what each boundary says, what each argument is to
/// what is drawn, the key, and what the front matter asks for — and which boundary each line is written in.
/// </summary>
[TestClass]
public class C4StagesTests
{
    private static ContentNode Read(string source) => MermaidStaged.Read(source);

    private static IReadOnlyList<C4ElementNode> Elements(ContentNode tree) => [.. tree.SelfAndDescendants().OfType<C4ElementNode>()];

    private static C4ElementNode Element(ContentNode tree, string id) => Elements(tree).Single(element => element.Id == id);

    private static IReadOnlyList<C4RelationNode> Relations(ContentNode tree) => [.. tree.SelfAndDescendants().OfType<C4RelationNode>()];

    private static IReadOnlyList<C4BoundaryNode> Boundaries(ContentNode tree) => [.. tree.SelfAndDescendants().OfType<C4BoundaryNode>()];

    private static IReadOnlyList<C4Key> Legend(ContentNode tree) => tree.SelfAndDescendants().OfType<C4LegendNode>().FirstOrDefault()?.Keys ?? [];

    /// <summary>What the argument of a line meaning something says, as written — or null where none means it.</summary>
    private static string? Argument(ContentNode line, C4Means means) =>
        line.SelfAndDescendants().OfType<C4ArgumentNode>().FirstOrDefault(argument => argument.Means == means)?
            .SelfAndDescendants().FirstOrDefault(node => node.Kind == MermaidKinds.Words && node.Role is C4Roles.Value or SequenceRoles.Id)?.Text;

    /// <summary>The alias of the boundary a line calling an element is written directly inside, or null for none.</summary>
    private static string? HeldBy(ContentNode tree, string id)
    {
        string? held = null;
        Walk(tree, null);
        return held;

        void Walk(ContentNode node, string? inside)
        {
            foreach (var child in node.Children)
            {
                if (child.Kind == MermaidKinds.Group) Walk(child, (child.Children[0].Stated() as C4BoundaryNode)?.Alias ?? inside);
                else if (child.Stated() is C4ElementNode element && element.Id == id) held = inside;
                else Walk(child, inside);
            }
        }
    }

    // ── The macro language ──────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void AnArgumentInQuotesKeepsItsCommasAndBrackets()
    {
        var line = Elements(Read("C4Container\nContainer(c, \"Web (the app)\", \"C#, ASP.NET Core\", \"Does things, well\")")).Single();

        Assert.AreEqual("Web (the app)", Argument(line, C4Means.Label));
        Assert.AreEqual("C#, ASP.NET Core", Argument(line, C4Means.Technology));
        Assert.AreEqual("Does things, well", Argument(line, C4Means.Description));
        Assert.AreEqual("[Container: C#, ASP.NET Core]", line.Stereotype);
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void AnArgumentGivenByNameTakesNoPositionalSlot_AndWinsOverThePositionalOne()
    {
        // $tags is named, so "Somebody" is still the third positional — the description.
        Assert.AreEqual("Somebody", Argument(Elements(Read("C4Context\nPerson(a, \"Alice\", $tags=\"v1\", \"Somebody\")")).Single(), C4Means.Description));
        Assert.AreEqual("By name", Argument(Elements(Read("C4Context\nPerson(a, \"Positional\", $label=\"By name\")")).Single(), C4Means.Label),
                        "a name wins, which is how a long signature is written short");
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void ACallInsideAnArgumentIsOneArgument()
    {
        var link = Relations(Read("C4Dynamic\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"Asks\", $index=SetIndex(9))")).Single();

        Assert.AreEqual("Asks", Argument(link, C4Means.Label));
        Assert.AreEqual("9", link.Number, "SetIndex(9) is one argument, and what it comes to is nine");
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void BothCommentDialectsAreReadAsNothing_AndAnApostropheInTextIsKept()
    {
        var elements = Elements(Read("C4Context\n' a PlantUML comment\n%% a Mermaid one\nPerson(a, \"Alice's account\")"));

        Assert.AreEqual(1, elements.Count);
        Assert.AreEqual("Alice's account", Argument(elements[0], C4Means.Label));
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void TheWrappersAPastedDiagramBringsAreReadAsNothing()
    {
        var tree = Read("C4Context\n@startuml\n!include C4_Context.puml\nPerson(a, \"A\")\n@enduml");

        Assert.AreEqual(1, Elements(tree).Count);
        Assert.AreEqual(0, Boundaries(tree).Count);
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void NothingAnybodyMeansToWriteIsReadWithoutThrowing()
    {
        foreach (var source in new[] { "C4Context\n??? !!!", "C4Context\nPerson(", "C4Context\n)", "C4Context\n}", "C4Context" })
            Assert.AreEqual(source, Read(source).Print(), source);

        Assert.AreEqual(0, Elements(Read("C4Context")).Count, "a diagram with nothing in it says nothing");
    }

    // ── The header ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-diagram")]
    public void EveryC4KeywordNamesTheOneDiagram()
    {
        foreach (var keyword in new[] { "C4Context", "C4Container", "C4Component", "C4Dynamic", "C4Deployment" })
            Assert.AreEqual(MermaidDiagram.C4, MermaidBlock.Read(keyword + "\nPerson(a, \"A\")").Diagram, keyword);
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void ADynamicDiagramNumbersItsRelationshipsAndTheOthersDoNot()
    {
        const string body = "\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"One\")\nRel(a, b, \"Two\")";

        CollectionAssert.AreEqual(new[] { "1", "2" }, Relations(Read("C4Dynamic" + body)).Select(link => link.Number).ToArray());
        Assert.IsTrue(Relations(Read("C4Context" + body)).All(link => link.Number is null), "the other four count nothing");
        Assert.IsTrue(Relations(Read("C4Context\nSHOW_INDEX()" + body)).All(link => link.Number is not null), "unless they are asked to");
    }

    [TestMethod]
    [CoversNode("c4-diagram")]
    public void TheTitleIsTheBlocksOwn()
    {
        Assert.AreEqual("System Context", MermaidBlock.Of(Read("C4Context\ntitle System Context\nPerson(a, \"A\")")).TitleText);
        Assert.AreEqual("From the front", MermaidBlock.Of(Read("---\ntitle: From the front\n---\nC4Context\nPerson(a, \"A\")")).TitleText);
        Assert.AreEqual("Sign-in sequence", MermaidBlock.Of(Read("C4Sequence\ntitle Sign-in sequence\nPerson(a, \"A\")\nRel(a, a, \"x\")")).TitleText);
    }

    // ── Elements ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-elements")]
    public void EveryElementMacroSaysWhatItIsAndHowItIsDrawn()
    {
        var tree = Read(
            "C4Container\nPerson(p, \"p\")\nPerson_Ext(pe, \"pe\")\nSystem(s, \"s\")\nSystemDb(sd, \"sd\")\n"
            + "SystemQueue(sq, \"sq\")\nSystem_Ext(se, \"se\")\nSystemDb_Ext(sde, \"sde\")\nContainer(c, \"c\")\n"
            + "ContainerDb(cd, \"cd\")\nContainerQueue(cq, \"cq\")\nContainer_Ext(ce, \"ce\")\nComponent(m, \"m\")\n"
            + "ComponentDb(md, \"md\")\nComponentQueue(mq, \"mq\")\nComponent_Ext(me, \"me\")");

        (C4Level Level, C4Shape Shape, bool External) Sorted(string id)
        {
            var element = Element(tree, id);
            return (element.Level, element.Shape, element.External);
        }

        Assert.AreEqual((C4Level.Person, C4Shape.Person, false), Sorted("p"));
        Assert.AreEqual((C4Level.Person, C4Shape.Person, true), Sorted("pe"));
        Assert.AreEqual((C4Level.System, C4Shape.Box, false), Sorted("s"));
        Assert.AreEqual((C4Level.System, C4Shape.Database, false), Sorted("sd"));
        Assert.AreEqual((C4Level.System, C4Shape.Queue, false), Sorted("sq"));
        Assert.AreEqual((C4Level.System, C4Shape.Box, true), Sorted("se"));
        Assert.AreEqual((C4Level.System, C4Shape.Database, true), Sorted("sde"));
        Assert.AreEqual((C4Level.Container, C4Shape.Box, false), Sorted("c"));
        Assert.AreEqual((C4Level.Container, C4Shape.Database, false), Sorted("cd"));
        Assert.AreEqual((C4Level.Container, C4Shape.Queue, false), Sorted("cq"));
        Assert.AreEqual((C4Level.Container, C4Shape.Box, true), Sorted("ce"));
        Assert.AreEqual((C4Level.Component, C4Shape.Box, false), Sorted("m"));
        Assert.AreEqual((C4Level.Component, C4Shape.Database, false), Sorted("md"));
        Assert.AreEqual((C4Level.Component, C4Shape.Queue, false), Sorted("mq"));
        Assert.AreEqual((C4Level.Component, C4Shape.Box, true), Sorted("me"));
        Assert.AreEqual(C4Elements.External, Element(tree, "pe").Tone, "somebody else's takes the one band, whatever its level");
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void TheStereotypeSaysWhatItIsWhatItIsBuiltWithAndWhoseItIs()
    {
        string Says(string macro) => Elements(Read("C4Container\n" + macro)).Single().Stereotype;

        Assert.AreEqual("[Person]", Says("Person(a, \"A\")"));
        Assert.AreEqual("[Software System]", Says("System(a, \"A\")"));
        Assert.AreEqual("[Container: Spring MVC]", Says("Container(a, \"A\", \"Spring MVC\")"));
        Assert.AreEqual("[Component]", Says("Component(a, \"A\")"));
        Assert.AreEqual("[Person (external)]", Says("Person_Ext(a, \"A\")"));
        Assert.AreEqual("[Container (external): C#, Xamarin]", Says("Container_Ext(a, \"A\", \" C#, Xamarin \")"));
        Assert.AreEqual("[Legacy]", Says("System(a, \"A\", $type=\"Legacy\")"), "a $type replaces what it would have said");
        Assert.AreEqual(string.Empty, Elements(Read("C4Container\nHIDE_STEREOTYPE()\nContainer(a, \"A\", \"Java\")")).Single().Stereotype,
                        "and HIDE_STEREOTYPE leaves a card its name and its description");
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void WhatAContainerIsBuiltWithSitsWhereASystemsDescriptionDoes()
    {
        // Person and System take (alias, label, descr); Container and Component put the technology at 2. C4-PlantUML's own
        // asymmetry, and the commonest thing to get wrong.
        var system = Elements(Read("C4Context\nSystem(a, \"A\", \"What it does\")")).Single();
        Assert.IsNull(Argument(system, C4Means.Technology));
        Assert.AreEqual("What it does", Argument(system, C4Means.Description));

        var container = Elements(Read("C4Container\nContainer(a, \"A\", \"Java\", \"What it does\")")).Single();
        Assert.AreEqual("Java", Argument(container, C4Means.Technology));
        Assert.AreEqual("What it does", Argument(container, C4Means.Description));
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void AnElementWithNoLabelIsCalledItsOwnName()
    {
        var element = Elements(Read("C4Context\nPerson(customer)")).Single();

        Assert.AreEqual("customer", element.Id);
        Assert.AreEqual("customer", Argument(element, C4Means.Alias));
        Assert.IsNull(Argument(element, C4Means.Label));
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void ALinkOnAnElementOrABoundaryIsRead()
    {
        Assert.AreEqual("https://example.com", Elements(Read("C4Context\nPerson(a, \"A\", $link=\"https://example.com\")")).Single().Href);
        Assert.AreEqual("https://example.com", Boundaries(Read("C4Context\nBoundary(b, \"B\", $link=\"https://example.com\") {\n}")).Single().Href);
    }

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void ADescriptionIsForATimelinesCardOnlyWhereTheBlockAsksForOne()
    {
        Assert.IsFalse(Element(Read("C4Sequence\nPerson(p, \"P\", \"Somebody\")\nRel(p, p, \"x\")"), "p").Described);
        Assert.IsTrue(Element(Read("C4Sequence\nSHOW_ELEMENT_DESCRIPTIONS()\nPerson(p, \"P\", \"Somebody\")\nRel(p, p, \"x\")"), "p").Described);
    }

    // ── Boundaries ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void BoundariesNestAndHoldWhatIsWrittenInsideThem()
    {
        var tree = Read("C4Container\nSystem_Boundary(outer, \"Outer\") {\n  Container_Boundary(inner, \"Inner\") {\n"
                        + "    Container(a, \"A\")\n  }\n  Container(b, \"B\")\n}\nContainer(c, \"C\")");

        Assert.AreEqual("inner", HeldBy(tree, "a"));
        Assert.AreEqual("outer", HeldBy(tree, "b"));
        Assert.IsNull(HeldBy(tree, "c"));

        var groups = tree.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Group).ToList();
        Assert.IsTrue(groups[0].SelfAndDescendants().Contains(groups[1]), "the inner boundary sits inside the outer one");
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void ABoundaryEndClosesOneJustAsABraceDoes()
    {
        var tree = Read("C4Context\nBoundary(b, \"The bank\")\nSystem(s, \"Core\")\nBoundary_End()\nSystem(t, \"Outside\")");

        Assert.AreEqual(1, Boundaries(tree).Count);
        Assert.AreEqual("b", HeldBy(tree, "s"));
        Assert.IsNull(HeldBy(tree, "t"));
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void ABoundaryIsNeverAnElementOfItsOwn()
    {
        var tree = Read("C4Container\nContainer_Boundary(api, \"API\") {\n  Component(c, \"C\")\n}");

        Assert.AreEqual(1, Boundaries(tree).Count);
        CollectionAssert.AreEqual(new[] { "c" }, Elements(tree).Select(element => element.Id).ToArray(), "a Container_Boundary is a box, not a Container");
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void WhatABoundarySaysUnderItsNameIsItsTypeOrWhatANodeRunsOn()
    {
        Assert.AreEqual("[Enterprise]", Boundaries(Read("C4Context\nEnterprise_Boundary(b, \"Big Bank\") {\n}")).Single().Says);
        Assert.AreEqual("[System]", Boundaries(Read("C4Context\nSystem_Boundary(b, \"Bank\") {\n}")).Single().Says);
        Assert.AreEqual("[Container]", Boundaries(Read("C4Container\nContainer_Boundary(b, \"API\") {\n}")).Single().Says);
        Assert.AreEqual("[Grouping]", Boundaries(Read("C4Context\nBoundary(b, \"Some\", \"Grouping\") {\n}")).Single().Says);
        Assert.IsNull(Boundaries(Read("C4Context\nBoundary(b, \"Some\") {\n}")).Single().Says, "one given no type says nothing");
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void ADeploymentNodeIsABoundaryDrawnSolid_AndKeepsWhatItRunsOn()
    {
        var tree = Read("C4Deployment\nDeployment_Node(plc, \"Big Bank plc\", \"Data centre\") {\n"
                        + "  Node(dn, \"bigbank-api\", \"Ubuntu 16.04 LTS\", \"A web server rack\") {\n"
                        + "    Container(api, \"API\", \"Java\")\n  }\n}");

        var outer = Boundaries(tree).Single(box => box.Alias == "plc");
        var inner = Boundaries(tree).Single(box => box.Alias == "dn");

        Assert.IsTrue(outer.Physical);
        Assert.AreEqual("[Deployment Node: Data centre]", outer.Says);
        Assert.AreEqual("[Deployment Node: Ubuntu 16.04 LTS]", inner.Says);
        Assert.AreEqual("dn", HeldBy(tree, "api"));
        Assert.IsFalse(Boundaries(Read("C4Context\nBoundary(b, \"Logical\") {\n}")).Single().Physical);
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void ABoundaryNothingClosesStillHoldsWhatFollowsIt_AndSaysSo()
    {
        var tree = Read("C4Context\nSystem_Boundary(b, \"Bank\") {\n  System(s, \"Core\")");

        Assert.AreEqual("b", HeldBy(tree, "s"));
        Assert.IsTrue(tree.SelfAndDescendants().Any(node => node.Kind == C4Kinds.Boundary && node.Trouble is not null));
    }

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void ABoundaryOnATimelineIsOneNestingWithItsFrames()
    {
        var tree = Read("C4Sequence\nBoundary(b, \"The bank\") {\n  System(s, \"Core\")\n  loop every day\n    Rel(s, s, \"x\")\n  }\n}\nPerson(a, \"A\")");

        Assert.AreEqual("b", HeldBy(tree, "s"));
        Assert.IsNull(HeldBy(tree, "a"), "a } closes the frame opened last, and the next the boundary");
    }

    // ── Relationships ───────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void EveryWayARelationshipMayBeAskedToRunIsOneRelationship()
    {
        var links = Relations(Read("C4Context\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"1\")\nRel_U(a, b, \"2\")\nRel_Up(a, b, \"3\")\n"
                                   + "Rel_D(a, b, \"4\")\nRel_Down(a, b, \"5\")\nRel_L(a, b, \"6\")\nRel_Left(a, b, \"7\")\nRel_R(a, b, \"8\")\n"
                                   + "Rel_Right(a, b, \"9\")"));

        Assert.AreEqual(9, links.Count);
        Assert.IsTrue(links.All(link => link.From == "a" && link.To == "b"),
                      "the direction a hint asks for is a placement, and this places by what is joined to what");
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void RelBackPointsTheOtherWayAndBiRelPointsBothWays()
    {
        var back = Relations(Read("C4Context\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel_Back(a, b, \"Answers\")")).Single();

        Assert.AreEqual(("b", "a"), (back.From, back.To));
        Assert.AreEqual(("b", "a"), (Argument(back, C4Means.From), Argument(back, C4Means.To)), "the argument it leaves is the second written");
        Assert.IsFalse(back.Both);
        Assert.IsTrue(Relations(Read("C4Context\nPerson(a, \"A\")\nSystem(b, \"B\")\nBiRel(a, b, \"Talks to\")")).Single().Both);
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void ARelationshipCarriesWhatItIsDoneWithAndWhatItIsFor()
    {
        var link = Relations(Read("C4Context\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"Uses\", \"HTTPS\", \"To see the balance\")")).Single();

        Assert.AreEqual("Uses", Argument(link, C4Means.Label));
        Assert.AreEqual("HTTPS", Argument(link, C4Means.Technology));
        Assert.AreEqual("To see the balance", Argument(link, C4Means.Description));
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void RelIndexShiftsEveryArgumentAlongByOne()
    {
        var link = Relations(Read("C4Dynamic\nPerson(a, \"A\")\nSystem(b, \"B\")\nRelIndex(7, a, b, \"Then this\", \"HTTPS\")")).Single();

        Assert.AreEqual(("a", "b"), (link.From, link.To));
        Assert.AreEqual("Then this", Argument(link, C4Means.Label));
        Assert.AreEqual("HTTPS", Argument(link, C4Means.Technology));
        Assert.AreEqual("7", link.Number);
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void TheNumberingAdvancesRepeatsAndResets()
    {
        var links = Relations(Read("C4Dynamic\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"one\", $index=Index())\n"
                                   + "Rel(a, b, \"again\", $index=LastIndex())\nRel(a, b, \"on\", $index=Index())\n"
                                   + "Rel(a, b, \"set\", $index=SetIndex(9))\nRel(a, b, \"after\", $index=Index())"));

        CollectionAssert.AreEqual(new[] { "1", "1", "2", "9", "10" }, links.Select(link => link.Number).ToArray());
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void TheNumberMovesWithoutDrawingAnything()
    {
        var links = Relations(Read("C4Dynamic\nincrement(2)\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"x\")\nSetIndex(8)\nRel(a, b, \"y\")"));

        Assert.AreEqual(2, links.Count, "increment and SetIndex are no relationships of their own");
        CollectionAssert.AreEqual(new[] { "3", "8" }, links.Select(link => link.Number).ToArray());
    }

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void ATimelineIsNumberedOnlyWhereShowIndexAsksForIt()
    {
        Assert.IsNull(Relations(Read("C4Sequence\nPerson(a, \"A\")\nRel(a, a, \"x\")")).Single().Number);
        CollectionAssert.AreEqual(new[] { "1", "2" },
                                  Relations(Read("C4Sequence\nSHOW_INDEX()\nPerson(a, \"A\")\nRel(a, a, \"x\")\nRel(a, a, \"y\")")).Select(link => link.Number).ToArray());
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void TheLayoutHintsAreNoRelationships()
    {
        var tree = Read("C4Context\nPerson(a, \"A\")\nSystem(b, \"B\")\nLay_D(a, b)\nLay_R(b, a)\nUpdateLayoutConfig($c4ShapeInRow=\"3\")");

        Assert.AreEqual(0, Relations(tree).Count);
        Assert.AreEqual(2, Elements(tree).Count);
    }

    // ── Styling, switches and the key ───────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-styling")]
    public void AStyleNamesItsTargetByC4TypeOrByTheElementsOwnName_WrittenAboveItOrBelow()
    {
        Assert.AreEqual("#08427b", Elements(Read("C4Context\nPerson(a, \"A\")\nUpdateElementStyle(\"person\", $bgColor=\"#08427b\")")).Single().Paint.Fill);
        Assert.AreEqual("#ff0000", Elements(Read("C4Context\nUpdateElementStyle(\"a\", $bgColor=\"#ff0000\")\nPerson(a, \"A\")")).Single().Paint.Fill,
                        "Mermaid lets a style name the element itself");
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void AnExternalElementIsStyledApartFromThePlainOne()
    {
        var tree = Read("C4Context\nSystem(ours, \"Ours\")\nSystem_Ext(theirs, \"Theirs\")\n"
                        + "UpdateElementStyle(\"system\", $bgColor=\"#111111\")\nUpdateElementStyle(\"external_system\", $bgColor=\"#999999\")");

        Assert.AreEqual("#111111", Element(tree, "ours").Paint.Fill);
        Assert.AreEqual("#999999", Element(tree, "theirs").Paint.Fill);
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void RepeatedStylesLayOverOneAnotherRatherThanReplacing()
    {
        var paint = Elements(Read("C4Context\nPerson(a, \"A\")\nUpdateElementStyle(\"person\", $bgColor=\"#111111\")\n"
                                  + "UpdateElementStyle(\"person\", $fontColor=\"#ffffff\")")).Single().Paint;

        Assert.AreEqual("#111111", paint.Fill, "what the first one set is still set");
        Assert.AreEqual("#ffffff", paint.Ink);
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void AStyleNamingTheElementItselfWinsOverOneNamingItsTypeOrItsTag()
    {
        var paint = Elements(Read("C4Context\nAddElementTag(\"v1\", $bgColor=\"#222222\")\nPerson(a, \"A\", $tags=\"v1\")\n"
                                  + "UpdateElementStyle(\"person\", $bgColor=\"#333333\")\nUpdateElementStyle(\"a\", $bgColor=\"#444444\")")).Single().Paint;

        Assert.AreEqual("#444444", paint.Fill, "the most particular of them wins");
        Assert.AreEqual("#1168bd", Elements(Read("C4Sequence\nAddElementTag(\"v1\", $bgColor=\"#1168bd\")\nPerson(a, \"A\", $tags=\"v1\")\nRel(a, a, \"x\")")).Single().Paint.Fill,
                        "a tag colours what names it, on a timeline too");
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void ATagCarriesColoursAnOutlineAndItsOwnRowOfTheKey()
    {
        var tree = Read("C4Container\nSHOW_LEGEND()\nAddElementTag(\"critical\", $bgColor=\"#c0392b\", $fontColor=\"#ffffff\", "
                        + "$borderColor=\"#000000\", $shape=\"queue\", $legendText=\"Business critical\")\n"
                        + "Container(api, \"API\", \"Java\", $tags=\"critical\")");
        var element = Elements(tree).Single();

        Assert.AreEqual(("#c0392b", "#ffffff", "#000000"), (element.Paint.Fill, element.Paint.Ink, element.Paint.Border));
        Assert.AreEqual(C4Shape.Queue, element.Shape, "a tag may say what outline to draw it with");
        CollectionAssert.Contains(Legend(tree).Select(row => row.Says).ToArray(), "Business critical");
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void ARelationshipTakesItsColoursFromItsTagsAndFromAStyleNamingItsEnds()
    {
        var tree = Read("C4Context\nAddRelTag(\"async\", $lineStyle=DashedLine(), $lineColor=\"#00ff00\", $legendText=\"Asynchronous\")\n"
                        + "SHOW_LEGEND()\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"Sends\", $tags=\"async\")\n"
                        + "UpdateRelStyle(a, b, $textColor=\"#ff0000\")");
        var stroke = Relations(tree).Single().Stroke;

        Assert.IsTrue(stroke.Dotted, "a $lineStyle written as DashedLine() makes the line dashed");
        Assert.AreEqual("#00ff00", stroke.Ink);
        Assert.AreEqual("#ff0000", stroke.Said);
        CollectionAssert.Contains(Legend(tree).Select(row => row.Says).ToArray(), "Asynchronous");
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void TheKeyHasARowForEachKindWrittenAndNoneWithoutTheDirective()
    {
        CollectionAssert.AreEqual(new[] { "Person", "Software System (external)", "Container (database)" },
                                  Legend(Read("C4Container\nSHOW_LEGEND()\nPerson(a, \"A\")\nPerson(b, \"B\")\nSystem_Ext(c, \"C\")\nContainerDb(d, \"D\")\nRel(a, c, \"x\")"))
                                      .Select(row => row.Says).ToArray(),
                                  "a row for each kind written, not a row for each of C4's kinds");
        CollectionAssert.AreEqual(new[] { "Person", "Software System", "Software System (database)", "Version one" },
                                  Legend(Read("C4Sequence\nSHOW_LEGEND()\nAddElementTag(\"v1\", $bgColor=\"#1168bd\", $legendText=\"Version one\")\n"
                                              + "Person(a, \"A\")\nSystem(b, \"B\")\nSystemDb(c, \"C\")\nRel(a, b, \"x\")")).Select(row => row.Says).ToArray(),
                                  "a store is its own row: the key describes what was written, and a cylinder is not a box");
        Assert.AreEqual(0, Legend(Read("C4Container\nPerson(a, \"A\")")).Count);
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void AskingForAKeyHidesTheStereotypesItNowCarries()
    {
        Assert.AreEqual(string.Empty, Elements(Read("C4Context\nSHOW_LEGEND()\nPerson(a, \"A\")")).Single().Stereotype);
        Assert.AreEqual("[Person]", Elements(Read("C4Context\nSHOW_LEGEND($hideStereotype=\"false\")\nPerson(a, \"A\")")).Single().Stereotype);
    }

    [TestMethod]
    [CoversNode("c4-diagram")]
    public void TheFrontMattersOwnSizesAreHungOnTheBlock()
    {
        var config = (Read("---\nconfig:\n  c4:\n    width: 300\n    c4ShapeMargin: 20\n    boxMargin: 4\n    wrap: false\n---\nC4Context\nPerson(a, \"A\")")
                      as ConfiguredNode<C4Metrics>)!.Config;

        Assert.AreEqual(300, config.Widest, 1e-9);
        Assert.AreEqual(20, config.Apart, 1e-9);
        Assert.AreEqual(4, config.Framed, 1e-9);
        Assert.IsFalse(config.Wraps);
        Assert.IsTrue(config.Wrapping > config.Widest, "nothing wraps when nothing is asked to");
    }

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void ShowFootBoxesTurnsATimelinesSecondRowOfParticipantsOff()
    {
        static SequenceConfig Config(string source) => (MermaidStaged.Read(source) as ConfiguredNode<SequenceConfig>)!.Config;

        Assert.IsTrue(Config("C4Sequence\nPerson(a, \"A\")\nRel(a, a, \"x\")").Mirrored);
        Assert.IsFalse(Config("C4Sequence\nPerson(a, \"A\")\nRel(a, a, \"x\")\nSHOW_FOOT_BOXES(false)").Mirrored, "written anywhere in the block");
    }

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void TheFrontMatterSaysWhatAC4SequenceIsDrawnAt()
    {
        var config = C4Config.Read("config:\n  c4:\n    wrap: true\n    width: 220\n  sequence:\n    actorMargin: 70");

        Assert.IsTrue(config.Wraps);
        Assert.AreEqual(220, config.Widest);
        Assert.AreEqual(70, config.Between);
    }
}
