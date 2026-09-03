using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Graphs;
using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The C4 macro reader and parser. WPF-free — this is all source text in, model out.
/// </summary>
[TestClass]
public class C4ParserTests
{
    private static C4Diagram Parse(string src) => new MermaidC4Parser().Parse(src);

    // ── The reader: argument splitting ────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void Reader_KeepsCommasAndParensInsideQuotedArguments()
    {
        var d = Parse("""
            C4Container
            Container(web, "Web App", "C#, ASP.NET Core 2.1 MVC", "Handles requests (and responses)")
            """);
        var web = d.FindElement("web")!;
        Assert.AreEqual("Web App", web.Label);
        Assert.AreEqual("C#, ASP.NET Core 2.1 MVC", web.Technology);
        Assert.AreEqual("Handles requests (and responses)", web.Description);
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void Reader_NamedArgumentsDoNotConsumePositionalSlots()
    {
        var d = Parse("""
            C4Context
            Person(a, "Alice", $tags="v1+v2", $link="https://example.com")
            Rel(a, b, "Uses", $techn="HTTPS")
            """);
        var a = d.FindElement("a")!;
        Assert.AreEqual("Alice", a.Label);
        CollectionAssert.AreEqual(new[] { "v1", "v2" }, a.Tags);
        Assert.AreEqual("https://example.com", a.Link);
        Assert.AreEqual("Uses", d.Relationships[0].Label);
        Assert.AreEqual("HTTPS", d.Relationships[0].Technology);
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void Reader_NamedArgumentWinsOverThePositionalOne()
    {
        var d = Parse("""
            C4Context
            Person(a, "Positional", $label="Named")
            """);
        Assert.AreEqual("Named", d.FindElement("a")!.Label);
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void Reader_HandlesACallInsideAnArgument()
    {
        // $index=Index() puts parens inside the argument list — the reason the reader tracks depth.
        var d = Parse("""
            C4Dynamic
            Rel(a, b, "First", "HTTPS", $index=Index())
            Rel(b, c, "Second", $index=Index())
            """);
        Assert.AreEqual(2, d.Relationships.Count);
        Assert.AreEqual(1, d.Relationships[0].Index);
        Assert.AreEqual("HTTPS", d.Relationships[0].Technology);
        Assert.AreEqual(2, d.Relationships[1].Index);
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void Reader_StripsBothCommentDialects_ButKeepsAnApostropheInText()
    {
        var d = Parse("""
            C4Context
            %% a mermaid comment
            ' a plantuml comment
            Person(a, "Bob's laptop")   %% trailing
            """);
        Assert.AreEqual(1, d.Elements.Count);
        Assert.AreEqual("Bob's laptop", d.FindElement("a")!.Label);
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void Reader_DecodesLineBreaksAndEntities()
    {
        var d = Parse("""
            C4Context
            System(s, "Two<br/>lines", "&amp; an entity")
            """);
        Assert.AreEqual("Two\nlines", d.FindElement("s")!.Label);
        Assert.AreEqual("& an entity", d.FindElement("s")!.Description);
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void Reader_SkipsPlantUmlWrapperLines()
    {
        var d = Parse("""
            C4Context
            @startuml
            !include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Context.puml
            Person(a, "Alice")
            @enduml
            """);
        Assert.AreEqual(1, d.Elements.Count);
        Assert.AreEqual(0, d.Statements.OfType<C4RawLine>().Count());
    }

    // ── Headers ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void Header_SelectsTheKindForAllSixKeywords()
    {
        Assert.AreEqual(C4DiagramKind.Context,    Parse("C4Context\n").Kind);
        Assert.AreEqual(C4DiagramKind.Container,  Parse("C4Container\n").Kind);
        Assert.AreEqual(C4DiagramKind.Component,  Parse("C4Component\n").Kind);
        Assert.AreEqual(C4DiagramKind.Dynamic,    Parse("C4Dynamic\n").Kind);
        Assert.AreEqual(C4DiagramKind.Deployment, Parse("C4Deployment\n").Kind);
        Assert.AreEqual(C4DiagramKind.Sequence,   Parse("C4Sequence\n").Kind);
        Assert.IsTrue(new MermaidC4Parser().CanParse("C4Context"));
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void Header_DynamicNumbersByDefault_OthersDoNot()
    {
        Assert.IsTrue(Parse("C4Dynamic\n").ShowIndex, "a dynamic diagram is about the order");
        Assert.IsFalse(Parse("C4Container\n").ShowIndex);
        Assert.IsFalse(Parse("C4Sequence\n").ShowIndex);
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void Title_IsCaptured()
    {
        Assert.AreEqual("Containers of the system", Parse("C4Container\ntitle Containers of the system\n").Title);
        Assert.AreEqual("Quoted", Parse("C4Context\ntitle \"Quoted\"\n").Title);
    }

    // ── Elements ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Elements_MapEveryKindShapeAndExternalVariant()
    {
        var d = Parse("""
            C4Component
            Person(p, "P")
            Person_Ext(pe, "PE")
            System(s, "S")
            System_Ext(se, "SE")
            SystemDb(sd, "SD")
            SystemQueue(sq, "SQ")
            SystemDb_Ext(sde, "SDE")
            SystemQueue_Ext(sqe, "SQE")
            Container(c, "C")
            ContainerDb(cd, "CD")
            ContainerQueue(cq, "CQ")
            Container_Ext(ce, "CE")
            ContainerDb_Ext(cde, "CDE")
            Component(m, "M")
            ComponentDb(md, "MD")
            ComponentQueue(mq, "MQ")
            Component_Ext(me, "ME")
            """);
        Assert.AreEqual(17, d.Elements.Count);

        void Check(string alias, C4ElementKind kind, C4ElementShape shape, bool ext)
        {
            var e = d.FindElement(alias)!;
            Assert.AreEqual(kind, e.Kind, alias);
            Assert.AreEqual(shape, e.Shape, alias);
            Assert.AreEqual(ext, e.External, alias);
        }

        Check("p",   C4ElementKind.Person,    C4ElementShape.Box,      false);
        Check("pe",  C4ElementKind.Person,    C4ElementShape.Box,      true);
        Check("s",   C4ElementKind.System,    C4ElementShape.Box,      false);
        Check("se",  C4ElementKind.System,    C4ElementShape.Box,      true);
        Check("sd",  C4ElementKind.System,    C4ElementShape.Database, false);
        Check("sq",  C4ElementKind.System,    C4ElementShape.Queue,    false);
        Check("sde", C4ElementKind.System,    C4ElementShape.Database, true);
        Check("sqe", C4ElementKind.System,    C4ElementShape.Queue,    true);
        Check("c",   C4ElementKind.Container, C4ElementShape.Box,      false);
        Check("cd",  C4ElementKind.Container, C4ElementShape.Database, false);
        Check("cq",  C4ElementKind.Container, C4ElementShape.Queue,    false);
        Check("ce",  C4ElementKind.Container, C4ElementShape.Box,      true);
        Check("cde", C4ElementKind.Container, C4ElementShape.Database, true);
        Check("m",   C4ElementKind.Component, C4ElementShape.Box,      false);
        Check("md",  C4ElementKind.Component, C4ElementShape.Database, false);
        Check("mq",  C4ElementKind.Component, C4ElementShape.Queue,    false);
        Check("me",  C4ElementKind.Component, C4ElementShape.Box,      true);
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Elements_TechnologySlotDiffersBetweenSystemAndContainer()
    {
        // C4-PlantUML's own asymmetry: Person/System have descr third, Container/Component have
        // techn third and descr fourth. Getting this wrong silently shows a technology as prose.
        var d = Parse("""
            C4Container
            System(s, "S", "A description")
            Container(c, "C", "Java", "A description")
            """);
        var s = d.FindElement("s")!;
        Assert.IsNull(s.Technology);
        Assert.AreEqual("A description", s.Description);

        var c = d.FindElement("c")!;
        Assert.AreEqual("Java", c.Technology);
        Assert.AreEqual("A description", c.Description);
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Elements_MissingLabelFallsBackToTheAlias()
    {
        Assert.AreEqual("lonely", Parse("C4Context\nSystem(lonely)\n").FindElement("lonely")!.Label);
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Elements_TypeOverridesTheStereotype()
    {
        var d = Parse("C4Context\nSystem(s, \"S\", $type=\"Legacy\")\n");
        Assert.AreEqual("Legacy", d.FindElement("s")!.Type);
    }

    // ── Boundaries ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void Boundaries_NestViaBracesAndRecordMembership()
    {
        var d = Parse("""
            C4Container
            Person(admin, "Administrator")
            Enterprise_Boundary(e, "Big Bank") {
              System_Boundary(c1, "Internet Banking") {
                Container(web, "Web Application")
                Container(api, "API")
              }
              System(mail, "E-mail")
            }
            """);
        Assert.AreEqual(2, d.Boundaries.Count);

        var e = d.FindBoundary("e")!;
        Assert.AreEqual("Enterprise", e.Type);
        Assert.IsNull(e.ParentId);
        CollectionAssert.AreEqual(new[] { "c1", "mail" }, e.MemberIds);

        var c1 = d.FindBoundary("c1")!;
        Assert.AreEqual("System", c1.Type);
        Assert.AreEqual("e", c1.ParentId);
        CollectionAssert.AreEqual(new[] { "web", "api" }, c1.MemberIds);

        Assert.AreEqual("c1", d.FindElement("web")!.OwnerId);
        Assert.IsNull(d.FindElement("admin")!.OwnerId);
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void Boundaries_BoundaryEndClosesInsteadOfABrace()
    {
        // C4_Sequence declares boundaries without braces and closes them with Boundary_End().
        var d = Parse("""
            C4Sequence
            Boundary(b, "Group", "system")
            Container(one, "One")
            Boundary_End()
            Container(two, "Two")
            """);
        var b = d.FindBoundary("b")!;
        Assert.AreEqual("system", b.Type);
        CollectionAssert.AreEqual(new[] { "one" }, b.MemberIds);
        Assert.IsNull(d.FindElement("two")!.OwnerId);
        Assert.AreEqual(1, d.Statements.OfType<C4BoundaryEnd>().Count());
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void Boundaries_ContainerBoundaryIsNotAContainerElement()
    {
        // "Container_Boundary" starts with "container" — the element decomposition must not claim it.
        var d = Parse("C4Component\nContainer_Boundary(cb, \"API\") {\n  Component(x, \"X\")\n}\n");
        Assert.AreEqual(0, d.Elements.Count(e => e.Alias == "cb"));
        Assert.AreEqual("Container", d.FindBoundary("cb")!.Type);
        Assert.AreEqual("cb", d.FindElement("x")!.OwnerId);
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void Boundaries_DeploymentNodesNestAndKeepTheirTypeAndDescription()
    {
        var d = Parse("""
            C4Deployment
            Deployment_Node(prod, "Production", "AWS", "The live estate") {
              Node(web, "Web Server", "Nginx") {
                Container(app, "Web Application", "Spring Boot")
              }
              Node_L(db, "DB Server", "MySQL")
            }
            """);
        var prod = d.FindBoundary("prod")!;
        Assert.IsTrue(prod.IsDeploymentNode);
        Assert.AreEqual("AWS", prod.Type);
        Assert.AreEqual("The live estate", prod.Description);
        CollectionAssert.AreEqual(new[] { "web", "db" }, prod.MemberIds);

        Assert.AreEqual("prod", d.FindBoundary("web")!.ParentId);
        Assert.AreEqual("Nginx", d.FindBoundary("web")!.Type);
        Assert.IsTrue(d.FindBoundary("db")!.IsDeploymentNode);
        Assert.AreEqual("web", d.FindElement("app")!.OwnerId);
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void Boundaries_UnclosedBraceStillYieldsTheBoundary()
    {
        var d = Parse("C4Container\nSystem_Boundary(b, \"B\") {\n  Container(c, \"C\")\n");
        Assert.AreEqual(1, d.Boundaries.Count);
        CollectionAssert.AreEqual(new[] { "c" }, d.FindBoundary("b")!.MemberIds);
    }

    // ── Relationships ─────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void Relationships_EveryDirectionVariantIsOneRelationship()
    {
        var d = Parse("""
            C4Context
            Rel(a, b, "1")
            Rel_U(a, b, "2")
            Rel_Up(a, b, "3")
            Rel_D(a, b, "4")
            Rel_Down(a, b, "5")
            Rel_L(a, b, "6")
            Rel_Left(a, b, "7")
            Rel_R(a, b, "8")
            Rel_Right(a, b, "9")
            Rel_Neighbor(a, b, "10")
            """);
        Assert.AreEqual(10, d.Relationships.Count);
        Assert.IsTrue(d.Relationships.All(r => r.From == "a" && r.To == "b"));
        Assert.IsTrue(d.Relationships.All(r => !r.Back && !r.Bidirectional));
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void Relationships_BackAndBidirectionalAreFlagged()
    {
        var d = Parse("""
            C4Context
            Rel_Back(a, b, "back")
            Rel_Back_Neighbor(a, b, "back too")
            BiRel(a, b, "both")
            BiRel_D(a, b, "both down")
            """);
        Assert.IsTrue(d.Relationships[0].Back);
        Assert.IsTrue(d.Relationships[1].Back);
        Assert.IsTrue(d.Relationships[2].Bidirectional);
        Assert.IsTrue(d.Relationships[3].Bidirectional);
        Assert.IsFalse(d.Relationships[2].Back);
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void Relationships_CarryTechnologyDescriptionAndTags()
    {
        var d = Parse("""
            C4Container
            Rel(a, b, "Makes calls to", "JSON/HTTPS", "A longer note", $sprite="none", $tags="sync+critical")
            """);
        var r = d.Relationships[0];
        Assert.AreEqual("Makes calls to", r.Label);
        Assert.AreEqual("JSON/HTTPS", r.Technology);
        Assert.AreEqual("A longer note", r.Description);
        CollectionAssert.AreEqual(new[] { "sync", "critical" }, r.Tags);
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void Relationships_RelIndexShiftsEveryArgumentAlong()
    {
        var d = Parse("""
            C4Dynamic
            RelIndex(4, spa, api, "Calls", "HTTPS")
            """);
        var r = d.Relationships[0];
        Assert.AreEqual(4, r.Index);
        Assert.AreEqual("spa", r.From);
        Assert.AreEqual("api", r.To);
        Assert.AreEqual("Calls", r.Label);
        Assert.AreEqual("HTTPS", r.Technology);
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void Relationships_IndexFunctionsAdvanceRepeatAndReset()
    {
        var d = Parse("""
            C4Dynamic
            Rel(a, b, "one", $index=Index())
            Rel(b, c, "two", $index=Index())
            Rel(c, d, "again", $index=LastIndex())
            increment(3)
            Rel(d, e, "jumped", $index=Index())
            SetIndex(100)
            Rel(e, f, "reset", $index=Index())
            """);
        var idx = d.Relationships.Select(r => r.Index).ToArray();
        CollectionAssert.AreEqual(new int?[] { 1, 2, 2, 6, 100 }, idx);
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void Relationships_LayoutHintsProduceNothing()
    {
        var d = Parse("""
            C4Context
            Lay_U(a, b)
            Lay_Down(a, b)
            Lay_Distance(a, b, 2)
            UpdateLayoutConfig($c4ShapeInRow="3")
            """);
        Assert.AreEqual(0, d.Relationships.Count);
        Assert.AreEqual(0, d.Statements.OfType<C4RawLine>().Count(), "they are recognised, just ignored");
    }

    // ── Styling ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-styling")]
    public void Styling_UpdateElementStyleIsKeyedByTypeName()
    {
        var d = Parse("""
            C4Context
            UpdateElementStyle("person", $bgColor="#08427b", $fontColor="#ffffff", $borderColor="#3c7fc0")
            UpdateElementStyle("external_system", $bgColor="#999999")
            """);
        var person = d.ElementStyles["person"];
        Assert.AreEqual("#08427b", person.BgColor);
        Assert.AreEqual("#ffffff", person.FontColor);
        Assert.AreEqual("#3c7fc0", person.BorderColor);
        Assert.AreEqual("#999999", d.ElementStyles["external_system"].BgColor);
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void Styling_RepeatedUpdatesMergeRatherThanReplace()
    {
        var d = Parse("""
            C4Context
            UpdateElementStyle("person", $bgColor="#111")
            UpdateElementStyle("person", $fontColor="#fff")
            """);
        Assert.AreEqual("#111", d.ElementStyles["person"].BgColor);
        Assert.AreEqual("#fff", d.ElementStyles["person"].FontColor);
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void Styling_TagsCarryColoursShapeAndLegendText()
    {
        var d = Parse("""
            C4Container
            AddElementTag("v1.0", $bgColor="#4CAF50", $fontColor="#ffffff", $legendText="Released", $borderStyle="DashedLine", $borderThickness="3")
            AddBoundaryTag("team", $bgColor="#eee")
            AddRelTag("async", $textColor="#f00", $lineColor="#f00", $lineStyle="DottedLine", $legendText="Async call")
            """);
        var v1 = d.Tags["v1.0"];
        Assert.AreEqual("#4CAF50", v1.BgColor);
        Assert.AreEqual("Released", v1.LegendText);
        Assert.AreEqual(EdgeStyle.Dashed, v1.BorderStyle);
        Assert.AreEqual(3, v1.BorderThickness);
        Assert.AreEqual("#eee", d.Tags["team"].BgColor);

        var async = d.RelTags["async"];
        Assert.AreEqual("#f00", async.LineColor);
        Assert.AreEqual(EdgeStyle.Dotted, async.LineStyle);
        Assert.AreEqual("Async call", async.LegendText);
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void Styling_UpdateRelStyleIsKeyedByEndpoints()
    {
        var d = Parse("""
            C4Context
            UpdateRelStyle(customer, banking, $textColor="#0000ff", $lineColor="#0000ff", $offsetX="10", $offsetY="-20")
            """);
        var style = d.RelStyles[("customer", "banking")];
        Assert.AreEqual("#0000ff", style.TextColor);
        Assert.AreEqual("#0000ff", style.LineColor);
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void Styling_WholeDiagramSwitchesAreRead()
    {
        var d = Parse("""
            C4Container
            SHOW_LEGEND()
            HIDE_STEREOTYPE()
            LAYOUT_LEFT_RIGHT()
            SHOW_PERSON_PORTRAIT()
            SHOW_ELEMENT_DESCRIPTIONS()
            SHOW_INDEX()
            SHOW_FOOT_BOXES(false)
            """);
        Assert.IsTrue(d.ShowLegend);
        Assert.IsTrue(d.HideStereotype);
        Assert.AreEqual(GraphDirection.LeftRight, d.Direction);
        Assert.AreEqual(C4PersonStyle.Portrait, d.PersonStyle);
        Assert.IsTrue(d.ShowElementDescriptions);
        Assert.IsTrue(d.ShowIndex);
        Assert.IsFalse(d.ShowFootBoxes);
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void Styling_LayoutDirectionAndLegendAliases()
    {
        Assert.AreEqual(GraphDirection.TopDown, Parse("C4Context\nLAYOUT_TOP_DOWN()\n").Direction);
        Assert.AreEqual(GraphDirection.LeftRight, Parse("C4Context\nLAYOUT_LANDSCAPE()\n").Direction);
        Assert.IsTrue(Parse("C4Context\nLAYOUT_WITH_LEGEND()\n").ShowLegend);
        Assert.IsNull(Parse("C4Context\n").Direction, "no directive leaves the layout's own default");
        Assert.AreEqual(C4PersonStyle.Outline, Parse("C4Context\nSHOW_PERSON_OUTLINE()\n").PersonStyle);
    }

    // ── Statement order and unclaimed lines ───────────────────────────────

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void Statements_KeepSourceOrderIncludingBoundaryBoundaries()
    {
        var d = Parse("""
            C4Sequence
            Person(user, "User")
            Boundary(b, "Group")
            Container(api, "API")
            Boundary_End()
            Rel(user, api, "Calls")
            """);
        var kinds = d.Statements.Select(s => s.GetType().Name).ToArray();
        CollectionAssert.AreEqual(
            new[] { nameof(C4ElementStatement), nameof(C4BoundaryBegin), nameof(C4ElementStatement), nameof(C4BoundaryEnd), nameof(C4RelStatement) },
            kinds);
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void Statements_UnclaimedLinesAreKeptInOrderForTheSequenceParser()
    {
        var d = Parse("""
            C4Sequence
            Person(user, "User")
            alt is valid
            Rel(user, api, "Calls")
            else rejected
            end
            """);
        var raw = d.Statements.OfType<C4RawLine>().Select(r => r.Line).ToArray();
        CollectionAssert.AreEqual(new[] { "alt is valid", "else rejected", "end" }, raw);
    }

    // ── Robustness ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void Parser_NeverThrowsOnGarbage()
    {
        foreach (var src in new[]
        {
            "", "C4Context", "C4Context\nPerson(", "C4Context\nPerson()", "C4Context\n)(",
            "C4Context\nRel(a)", "C4Context\nUpdateElementStyle()", "C4Context\n}\n}\n}",
            "C4Context\nBoundary_End()", "C4Context\nSystem(a, \"unterminated",
            "not a c4 diagram at all", "C4Context\n$$$$",
        })
        {
            var d = Parse(src);
            Assert.IsNotNull(d, src);
        }
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void Parser_EmptyDiagramReportsItself()
    {
        Assert.IsTrue(Parse("C4Context\n").IsEmpty);
        Assert.IsFalse(Parse("C4Context\nPerson(a, \"A\")\n").IsEmpty);
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void Styling_TargetMayBeAnAliasAsMermaidWritesIt()
    {
        // The one place the two dialects disagree: C4-PlantUML's UpdateElementStyle takes an element
        // *type* ("person"), Mermaid's takes an element *alias* ("customerA"). Both are recorded as
        // given, so the projector can look up either.
        var d = Parse("""
            C4Context
            Person(customerA, "Banking Customer A")
            UpdateElementStyle(customerA, $fontColor="red", $bgColor="grey", $borderColor="red")
            """);
        var style = d.ElementStyles["customerA"];
        Assert.AreEqual("grey", style.BgColor);
        Assert.AreEqual("red", style.FontColor);
        Assert.AreEqual("red", style.BorderColor);
    }

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void RealWorldDiagram_ParsesEndToEnd()
    {
        // The Mermaid documentation's own context diagram, verbatim in shape: deep boundary nesting,
        // an external db, BiRel, alias-targeted styling and a layout config all in one.
        var d = Parse("""
            C4Context
            title System Context diagram for Internet Banking System
            Enterprise_Boundary(b0, "BankBoundary0") {
              Person(customerA, "Banking Customer A", "A customer of the bank, with personal accounts.")
              Person_Ext(customerC, "Banking Customer C", "desc")
              System(SystemAA, "Internet Banking System", "Allows customers to view information.")
              Enterprise_Boundary(b1, "BankBoundary") {
                SystemDb_Ext(SystemE, "Mainframe Banking System", "Stores core banking information.")
                System_Boundary(b2, "BankBoundary2") {
                  System(SystemA, "Banking System A")
                  System(SystemB, "Banking System B", "A system of the bank.")
                }
              }
            }
            BiRel(customerA, SystemAA, "Uses")
            Rel(SystemAA, SystemE, "Uses", "HTTPS")
            UpdateElementStyle(customerA, $fontColor="red", $bgColor="grey")
            UpdateRelStyle(customerA, SystemAA, $textColor="blue", $lineColor="blue", $offsetX="5")
            UpdateLayoutConfig($c4ShapeInRow="3", $c4BoundaryInRow="1")
            """);

        Assert.AreEqual(C4DiagramKind.Context, d.Kind);
        Assert.AreEqual("System Context diagram for Internet Banking System", d.Title);
        CollectionAssert.AreEqual(
            new[] { "customerA", "customerC", "SystemAA", "SystemE", "SystemA", "SystemB" },
            d.Elements.Select(e => e.Alias).ToArray());
        CollectionAssert.AreEqual(new[] { "b0", "b1", "b2" }, d.Boundaries.Select(b => b.Alias).ToArray());
        Assert.AreEqual(2, d.Relationships.Count);

        // Three levels of nesting, each knowing its parent.
        Assert.IsNull(d.FindBoundary("b0")!.ParentId);
        Assert.AreEqual("b0", d.FindBoundary("b1")!.ParentId);
        Assert.AreEqual("b1", d.FindBoundary("b2")!.ParentId);
        Assert.AreEqual("b2", d.FindElement("SystemA")!.OwnerId);
        Assert.AreEqual("b0", d.FindElement("customerA")!.OwnerId);

        // The external database keeps both its shape and its externality.
        var e = d.FindElement("SystemE")!;
        Assert.AreEqual(C4ElementShape.Database, e.Shape);
        Assert.IsTrue(e.External);

        Assert.IsTrue(d.Relationships[0].Bidirectional);
        Assert.AreEqual("HTTPS", d.Relationships[1].Technology);
        Assert.AreEqual("grey", d.ElementStyles["customerA"].BgColor);
        Assert.AreEqual("blue", d.RelStyles[("customerA", "SystemAA")].LineColor);
        Assert.AreEqual(0, d.Statements.OfType<C4RawLine>().Count(), "nothing in this diagram is unclaimed");
    }

    // ── Config ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-macro-syntax")]
    public void Config_ParsesTheC4Keys()
    {
        var cfg = C4ConfigParser.Parse("""
            config:
              c4:
                wrap: true
                c4ShapeInRow: 3
                c4BoundaryInRow: 2
                width: 800
            """);
        Assert.IsTrue(cfg.Wrap);
        Assert.AreEqual(3, cfg.C4ShapeInRow);
        Assert.AreEqual(2, cfg.C4BoundaryInRow);
        Assert.AreEqual(800, cfg.Width);
        Assert.IsNull(cfg.Height);
        Assert.IsNull(C4ConfigParser.Parse(null).Wrap);
    }
}
