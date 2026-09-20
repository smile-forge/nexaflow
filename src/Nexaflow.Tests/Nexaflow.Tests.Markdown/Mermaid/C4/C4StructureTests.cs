using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.C4;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.C4;

/// <summary>
/// What a structural C4 block comes to: the elements and what each card says about itself, the boundaries holding them, the
/// relationships between them, and what the whole diagram is switched to show.
/// </summary>
[TestClass]
public class C4StructureTests
{
    private static C4Structure Read(string source) => C4Structure.Read(source);

    // ── The macro language ──────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void AnArgumentInQuotesKeepsItsCommasAndBrackets()
    {
        var node = Read("C4Container\nContainer(c, \"Web (the app)\", \"C#, ASP.NET Core\", \"Does things, well\")").Nodes.Single();

        Assert.AreEqual("Web (the app)", node.Said?.Text);
        Assert.AreEqual("C#, ASP.NET Core", node.Technology?.Text);
        Assert.AreEqual("Does things, well", node.Describes?.Text);
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void AnArgumentGivenByNameTakesNoPositionalSlot_AndWinsOverThePositionalOne()
    {
        // $tags is named, so "Somebody" is still the third positional — the description.
        var named = Read("C4Context\nPerson(a, \"Alice\", $tags=\"v1\", \"Somebody\")").Nodes.Single();
        Assert.AreEqual("Somebody", named.Describes?.Text);

        var both = Read("C4Context\nPerson(a, \"Positional\", $label=\"By name\")").Nodes.Single();
        Assert.AreEqual("By name", both.Said?.Text, "a name wins, which is how a long signature is written short");
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void ACallInsideAnArgumentIsOneArgument()
    {
        var link = Read("C4Dynamic\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"Asks\", $index=SetIndex(9))").Links.Single();

        Assert.AreEqual("Asks", link.Said?.Text);
        Assert.AreEqual("9", link.Number, "SetIndex(9) is one argument, and what it comes to is nine");
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void BothCommentDialectsAreReadAsNothing_AndAnApostropheInTextIsKept()
    {
        var diagram = Read("C4Context\n' a PlantUML comment\n%% a Mermaid one\nPerson(a, \"Alice's account\")");

        Assert.AreEqual(1, diagram.Nodes.Count);
        Assert.AreEqual("Alice's account", diagram.Nodes[0].Said?.Text);
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void TheWrappersAPastedDiagramBringsAreReadAsNothing()
    {
        var diagram = Read("C4Context\n@startuml\n!include C4_Context.puml\nPerson(a, \"A\")\n@enduml");

        Assert.AreEqual(1, diagram.Nodes.Count);
        Assert.AreEqual(0, diagram.Boxes.Count);
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void NothingAnybodyMeansToWriteIsReadWithoutThrowing()
    {
        foreach (var source in new[] { "C4Context\n??? !!!", "C4Context\nPerson(", "C4Context\n)", "C4Context\n}", "C4Context" })
            Assert.IsNotNull(Read(source), source);

        Assert.IsTrue(Read("C4Context").Empty, "a diagram with nothing in it says so");
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

        var dynamic = Read("C4Dynamic" + body);
        CollectionAssert.AreEqual(new[] { "1", "2" }, dynamic.Links.Select(link => link.Number).ToArray());

        Assert.IsTrue(Read("C4Context" + body).Links.All(link => link.Number is null), "the other four count nothing");
        Assert.IsTrue(Read("C4Context\nSHOW_INDEX()" + body).Links.All(link => link.Number is not null),
                      "unless they are asked to");
    }

    [TestMethod]
    [CoversNode("c4-diagram")]
    public void TheTitleIsTheBlocksOwn()
    {
        Assert.AreEqual("System Context", Read("C4Context\ntitle System Context\nPerson(a, \"A\")").Block.TitleText);
        Assert.AreEqual("From the front", Read("---\ntitle: From the front\n---\nC4Context\nPerson(a, \"A\")").Block.TitleText);
    }

    // ── Elements ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-elements")]
    public void EveryElementMacroSaysWhatItIsAndHowItIsDrawn()
    {
        var diagram = Read(
            "C4Container\nPerson(p, \"p\")\nPerson_Ext(pe, \"pe\")\nSystem(s, \"s\")\nSystemDb(sd, \"sd\")\n"
            + "SystemQueue(sq, \"sq\")\nSystem_Ext(se, \"se\")\nSystemDb_Ext(sde, \"sde\")\nContainer(c, \"c\")\n"
            + "ContainerDb(cd, \"cd\")\nContainerQueue(cq, \"cq\")\nContainer_Ext(ce, \"ce\")\nComponent(m, \"m\")\n"
            + "ComponentDb(md, \"md\")\nComponentQueue(mq, \"mq\")\nComponent_Ext(me, \"me\")");

        (C4Level Level, C4Shape Shape, bool External) Sorted(string id)
        {
            var node = diagram.Nodes.Single(one => one.Id == id);
            return (node.Level, node.Shape, node.External);
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
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void TheStereotypeSaysWhatItIsWhatItIsBuiltWithAndWhoseItIs()
    {
        string Says(string macro) => Read("C4Container\n" + macro).Nodes.Single().Stereotype;

        Assert.AreEqual("[Person]", Says("Person(a, \"A\")"));
        Assert.AreEqual("[Software System]", Says("System(a, \"A\")"));
        Assert.AreEqual("[Container: Spring MVC]", Says("Container(a, \"A\", \"Spring MVC\")"));
        Assert.AreEqual("[Component]", Says("Component(a, \"A\")"));
        Assert.AreEqual("[Person (external)]", Says("Person_Ext(a, \"A\")"));
        Assert.AreEqual("[Container (external): C#, Xamarin]", Says("Container_Ext(a, \"A\", \" C#, Xamarin \")"));
        Assert.AreEqual("[Legacy]", Says("System(a, \"A\", $type=\"Legacy\")"), "a $type replaces what it would have said");
        Assert.AreEqual(string.Empty, Read("C4Container\nHIDE_STEREOTYPE()\nContainer(a, \"A\", \"Java\")").Nodes.Single().Stereotype,
                        "and HIDE_STEREOTYPE leaves a card its name and its description");
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void WhatAContainerIsBuiltWithSitsWhereASystemsDescriptionDoes()
    {
        // Person and System take (alias, label, descr); Container and Component put the technology at 2. C4-PlantUML's own
        // asymmetry, and the commonest thing to get wrong.
        var system = Read("C4Context\nSystem(a, \"A\", \"What it does\")").Nodes.Single();
        Assert.IsNull(system.Technology);
        Assert.AreEqual("What it does", system.Describes?.Text);

        var container = Read("C4Container\nContainer(a, \"A\", \"Java\", \"What it does\")").Nodes.Single();
        Assert.AreEqual("Java", container.Technology?.Text);
        Assert.AreEqual("What it does", container.Describes?.Text);
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void AnElementWithNoLabelIsDrawnAsItsOwnName()
    {
        var node = Read("C4Context\nPerson(customer)").Nodes.Single();

        Assert.AreEqual("customer", node.Id);
        Assert.AreEqual("customer", node.Said?.Text);
    }

    // ── Boundaries ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void BoundariesNestAndHoldWhatIsWrittenInsideThem()
    {
        var diagram = Read(
            "C4Container\nSystem_Boundary(outer, \"Outer\") {\n  Container_Boundary(inner, \"Inner\") {\n"
            + "    Container(a, \"A\")\n  }\n  Container(b, \"B\")\n}\nContainer(c, \"C\")");

        var outer = diagram.Boxes.Single(box => box.Alias == "outer");
        var inner = diagram.Boxes.Single(box => box.Alias == "inner");

        Assert.IsNull(outer.Parent);
        Assert.AreEqual(outer.Key, inner.Parent);
        CollectionAssert.AreEqual(new[] { "a" }, diagram.Inside(inner.Key).Select(node => node.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "b" }, diagram.Inside(outer.Key).Select(node => node.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "c" }, diagram.Inside(null).Select(node => node.Id).ToArray());
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void ABoundaryEndClosesOneJustAsABraceDoes()
    {
        var diagram = Read("C4Context\nBoundary(b, \"The bank\")\nSystem(s, \"Core\")\nBoundary_End()\nSystem(t, \"Outside\")");

        Assert.AreEqual(1, diagram.Boxes.Count);
        CollectionAssert.AreEqual(new[] { "s" }, diagram.Inside(diagram.Boxes[0].Key).Select(node => node.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "t" }, diagram.Inside(null).Select(node => node.Id).ToArray());
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void ABoundaryIsNeverAnElementOfItsOwn()
    {
        var diagram = Read("C4Container\nContainer_Boundary(api, \"API\") {\n  Component(c, \"C\")\n}");

        Assert.AreEqual(1, diagram.Boxes.Count);
        CollectionAssert.AreEqual(new[] { "c" }, diagram.Nodes.Select(node => node.Id).ToArray(),
                                  "a Container_Boundary is a box, not a Container");
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void WhatABoundarySaysUnderItsNameIsItsTypeOrWhatANodeRunsOn()
    {
        Assert.AreEqual("[Enterprise]", Read("C4Context\nEnterprise_Boundary(b, \"Big Bank\") {\n}").Boxes.Single().Says);
        Assert.AreEqual("[System]", Read("C4Context\nSystem_Boundary(b, \"Bank\") {\n}").Boxes.Single().Says);
        Assert.AreEqual("[Container]", Read("C4Container\nContainer_Boundary(b, \"API\") {\n}").Boxes.Single().Says);
        Assert.AreEqual("[Grouping]", Read("C4Context\nBoundary(b, \"Some\", \"Grouping\") {\n}").Boxes.Single().Says);
        Assert.IsNull(Read("C4Context\nBoundary(b, \"Some\") {\n}").Boxes.Single().Says, "one given no type says nothing");
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void ADeploymentNodeIsABoundaryDrawnSolid_AndKeepsWhatItRunsOn()
    {
        var diagram = Read(
            "C4Deployment\nDeployment_Node(plc, \"Big Bank plc\", \"Data centre\") {\n"
            + "  Node(dn, \"bigbank-api\", \"Ubuntu 16.04 LTS\", \"A web server rack\") {\n"
            + "    Container(api, \"API\", \"Java\")\n  }\n}");

        var outer = diagram.Boxes.Single(box => box.Alias == "plc");
        var inner = diagram.Boxes.Single(box => box.Alias == "dn");

        Assert.IsTrue(outer.Physical);
        Assert.AreEqual("[Deployment Node: Data centre]", outer.Says);
        Assert.AreEqual("[Deployment Node: Ubuntu 16.04 LTS]", inner.Says);
        Assert.AreEqual(outer.Key, inner.Parent);
        Assert.IsFalse(Read("C4Context\nBoundary(b, \"Logical\") {\n}").Boxes.Single().Physical);
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void ABoundaryNothingClosesStillHoldsWhatFollowsIt()
    {
        var diagram = Read("C4Context\nSystem_Boundary(b, \"Bank\") {\n  System(s, \"Core\")");

        Assert.AreEqual(1, diagram.Boxes.Count);
        CollectionAssert.AreEqual(new[] { "s" }, diagram.Inside(diagram.Boxes[0].Key).Select(node => node.Id).ToArray());
    }

    // ── Relationships ───────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void EveryWayARelationshipMayBeAskedToRunIsOneRelationship()
    {
        var diagram = Read(
            "C4Context\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"1\")\nRel_U(a, b, \"2\")\nRel_Up(a, b, \"3\")\n"
            + "Rel_D(a, b, \"4\")\nRel_Down(a, b, \"5\")\nRel_L(a, b, \"6\")\nRel_Left(a, b, \"7\")\nRel_R(a, b, \"8\")\n"
            + "Rel_Right(a, b, \"9\")");

        Assert.AreEqual(9, diagram.Links.Count);
        Assert.IsTrue(diagram.Links.All(link => link.From == "a" && link.To == "b"),
                      "the direction a hint asks for is a placement, and this places by what is joined to what");
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void RelBackPointsTheOtherWayAndBiRelPointsBothWays()
    {
        var back = Read("C4Context\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel_Back(a, b, \"Answers\")").Links.Single();
        Assert.AreEqual("b", back.From);
        Assert.AreEqual("a", back.To);
        Assert.IsFalse(back.Both);

        var both = Read("C4Context\nPerson(a, \"A\")\nSystem(b, \"B\")\nBiRel(a, b, \"Talks to\")").Links.Single();
        Assert.IsTrue(both.Both);
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void ARelationshipCarriesWhatItIsDoneWithAndWhatItIsFor()
    {
        var link = Read("C4Context\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"Uses\", \"HTTPS\", \"To see the balance\")")
                   .Links.Single();

        Assert.AreEqual("Uses", link.Said?.Text);
        CollectionAssert.AreEqual(new[] { "HTTPS", "To see the balance" }, link.Under.Select(part => part.Text).ToArray());
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void RelIndexShiftsEveryArgumentAlongByOne()
    {
        var link = Read("C4Dynamic\nPerson(a, \"A\")\nSystem(b, \"B\")\nRelIndex(7, a, b, \"Then this\")").Links.Single();

        Assert.AreEqual("a", link.From);
        Assert.AreEqual("b", link.To);
        Assert.AreEqual("Then this", link.Said?.Text);
        Assert.AreEqual("7", link.Number);
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void TheNumberingAdvancesRepeatsAndResets()
    {
        var diagram = Read(
            "C4Dynamic\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"one\", $index=Index())\n"
            + "Rel(a, b, \"again\", $index=LastIndex())\nRel(a, b, \"on\", $index=Index())\n"
            + "Rel(a, b, \"set\", $index=SetIndex(9))\nRel(a, b, \"after\", $index=Index())");

        CollectionAssert.AreEqual(new[] { "1", "1", "2", "9", "10" }, diagram.Links.Select(link => link.Number).ToArray());
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void TheNumberMovesWithoutDrawingAnything()
    {
        var diagram = Read("C4Dynamic\nincrement(2)\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"x\")\nSetIndex(8)\nRel(a, b, \"y\")");

        Assert.AreEqual(2, diagram.Links.Count, "increment and SetIndex draw nothing of their own");
        CollectionAssert.AreEqual(new[] { "3", "8" }, diagram.Links.Select(link => link.Number).ToArray());
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void AnEndNobodyDeclaredStillStandsSomewhere()
    {
        var diagram = Read("C4Context\nRel(ghost, other, \"Uses\")");

        CollectionAssert.AreEqual(new[] { "ghost", "other" }, diagram.Nodes.Select(node => node.Id).ToArray());
        Assert.AreEqual(1, diagram.Links.Count);
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void TheLayoutHintsAreReadAndDrawNothing()
    {
        var diagram = Read("C4Context\nPerson(a, \"A\")\nSystem(b, \"B\")\nLay_D(a, b)\nLay_R(b, a)\nUpdateLayoutConfig($c4ShapeInRow=\"3\")");

        Assert.AreEqual(0, diagram.Links.Count);
        Assert.AreEqual(2, diagram.Nodes.Count);
    }

    // ── Styling, switches and the key ───────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-styling")]
    public void AStyleNamesItsTargetByC4TypeOrByTheElementsOwnName()
    {
        var byType = Read("C4Context\nPerson(a, \"A\")\nUpdateElementStyle(\"person\", $bgColor=\"#08427b\")").Nodes.Single();
        Assert.AreEqual("#08427b", byType.Fill);

        var byAlias = Read("C4Context\nPerson(a, \"A\")\nUpdateElementStyle(\"a\", $bgColor=\"#ff0000\")").Nodes.Single();
        Assert.AreEqual("#ff0000", byAlias.Fill, "Mermaid lets a style name the element itself");
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void AnExternalElementIsStyledApartFromThePlainOne()
    {
        var diagram = Read(
            "C4Context\nSystem(ours, \"Ours\")\nSystem_Ext(theirs, \"Theirs\")\n"
            + "UpdateElementStyle(\"system\", $bgColor=\"#111111\")\nUpdateElementStyle(\"external_system\", $bgColor=\"#999999\")");

        Assert.AreEqual("#111111", diagram.Nodes.Single(node => node.Id == "ours").Fill);
        Assert.AreEqual("#999999", diagram.Nodes.Single(node => node.Id == "theirs").Fill);
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void RepeatedStylesLayOverOneAnotherRatherThanReplacing()
    {
        var node = Read("C4Context\nPerson(a, \"A\")\nUpdateElementStyle(\"person\", $bgColor=\"#111111\")\n"
                        + "UpdateElementStyle(\"person\", $fontColor=\"#ffffff\")").Nodes.Single();

        Assert.AreEqual("#111111", node.Fill, "what the first one set is still set");
        Assert.AreEqual("#ffffff", node.Ink);
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void AStyleNamingTheElementItselfWinsOverOneNamingItsTypeOrItsTag()
    {
        var node = Read(
            "C4Context\nAddElementTag(\"v1\", $bgColor=\"#222222\")\nPerson(a, \"A\", $tags=\"v1\")\n"
            + "UpdateElementStyle(\"person\", $bgColor=\"#333333\")\nUpdateElementStyle(\"a\", $bgColor=\"#444444\")").Nodes.Single();

        Assert.AreEqual("#444444", node.Fill, "the most particular of them wins");
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void ATagCarriesColoursAnOutlineAndItsOwnRowOfTheKey()
    {
        var diagram = Read(
            "C4Container\nSHOW_LEGEND()\nAddElementTag(\"critical\", $bgColor=\"#c0392b\", $fontColor=\"#ffffff\", "
            + "$borderColor=\"#000000\", $shape=\"queue\", $legendText=\"Business critical\")\n"
            + "Container(api, \"API\", \"Java\", $tags=\"critical\")");

        var node = diagram.Nodes.Single();
        Assert.AreEqual("#c0392b", node.Fill);
        Assert.AreEqual("#ffffff", node.Ink);
        Assert.AreEqual("#000000", node.Border);
        Assert.AreEqual(C4Shape.Queue, node.Shape, "a tag may say what outline to draw it with");
        CollectionAssert.Contains(diagram.Legend.Select(row => row.Says).ToArray(), "Business critical");
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void ARelationshipTakesItsColoursFromItsTagsAndFromAStyleNamingItsEnds()
    {
        var diagram = Read(
            "C4Context\nAddRelTag(\"async\", $lineStyle=DashedLine(), $lineColor=\"#00ff00\", $legendText=\"Asynchronous\")\n"
            + "SHOW_LEGEND()\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"Sends\", $tags=\"async\")\n"
            + "UpdateRelStyle(a, b, $textColor=\"#ff0000\")");

        var link = diagram.Links.Single();
        Assert.IsTrue(link.Dotted, "a $lineStyle written as DashedLine() makes the line dashed");
        Assert.AreEqual("#00ff00", link.Ink);
        Assert.AreEqual("#ff0000", link.SaidInk);
        CollectionAssert.Contains(diagram.Legend.Select(row => row.Says).ToArray(), "Asynchronous");
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void TheKeyHasARowForEachKindWrittenAndNoneWithoutTheDirective()
    {
        var diagram = Read("C4Container\nSHOW_LEGEND()\nPerson(a, \"A\")\nPerson(b, \"B\")\nSystem_Ext(c, \"C\")\n"
                           + "ContainerDb(d, \"D\")\nRel(a, c, \"x\")");

        CollectionAssert.AreEqual(new[] { "Person", "Software System (external)", "Container (database)" },
                                  diagram.Legend.Select(row => row.Says).ToArray(),
                                  "a row for each kind written, not a row for each of C4's kinds");

        Assert.AreEqual(0, Read("C4Container\nPerson(a, \"A\")").Legend.Count);
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void AskingForAKeyHidesTheStereotypesItNowCarries()
    {
        Assert.AreEqual(string.Empty, Read("C4Context\nSHOW_LEGEND()\nPerson(a, \"A\")").Nodes.Single().Stereotype);
        Assert.AreEqual("[Person]", Read("C4Context\nSHOW_LEGEND($hideStereotype=\"false\")\nPerson(a, \"A\")")
                                    .Nodes.Single().Stereotype);
    }

    [TestMethod]
    [CoversNode("c4-diagram")]
    public void TheLayoutDirectivesTurnTheDiagram()
    {
        Assert.AreEqual(C4Way.Down, Read("C4Context\nPerson(a, \"A\")").Way, "down the page unless it says otherwise");
        Assert.AreEqual(C4Way.Right, Read("C4Context\nLAYOUT_LEFT_RIGHT()\nPerson(a, \"A\")").Way);
        Assert.AreEqual(C4Way.Down, Read("C4Context\nLAYOUT_LEFT_RIGHT()\nLAYOUT_TOP_DOWN()\nPerson(a, \"A\")").Way);
    }

    [TestMethod]
    [CoversNode("c4-diagram")]
    public void TheFrontMattersOwnSizesAreRead()
    {
        var config = Read("---\nconfig:\n  c4:\n    width: 300\n    c4ShapeMargin: 20\n    boxMargin: 4\n    wrap: false\n---\n"
                          + "C4Context\nPerson(a, \"A\")").Config;

        Assert.AreEqual(300, config.Widest, 1e-9);
        Assert.AreEqual(20, config.Apart, 1e-9);
        Assert.AreEqual(4, config.Framed, 1e-9);
        Assert.IsFalse(config.Wraps);
        Assert.IsTrue(config.Wrapping > config.Widest, "nothing wraps when nothing is asked to");
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void ALinkOnAnElementOrABoundaryIsRead()
    {
        Assert.AreEqual("https://example.com",
                        Read("C4Context\nPerson(a, \"A\", $link=\"https://example.com\")").Nodes.Single().Href);
        Assert.AreEqual("https://example.com",
                        Read("C4Context\nBoundary(b, \"B\", $link=\"https://example.com\") {\n}").Boxes.Single().Href);
    }
}
