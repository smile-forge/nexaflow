using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Graphs;
using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;
using System.Windows.Controls;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The <c>C4Sequence</c> extension: a C4 diagram projected onto the sequence model so it is drawn by
/// the same renderer as a native <c>sequenceDiagram</c>. The interesting cases are the ones where
/// the two languages meet — a native <c>alt</c> around C4 relationships, and numbering that has to
/// agree with the native autonumber it shares a field with.
/// </summary>
[TestClass]
public class C4SequenceProjectionTests
{
    private static SequenceDiagram Project(string src) =>
        C4SequenceProjector.ToSequence(new MermaidC4Parser().Parse(src));

    private const string Src =
        """
        C4Sequence
        title Sign-in
        SHOW_INDEX()
        Person(customer, "Banking Customer")
        Container(spa, "Single-Page App", "Angular")
        Boundary(b, "API Application", "Container")
          Component(signin, "Sign In Controller", "Spring MVC")
          ComponentDb(users, "User Store", "Spring Bean")
        Boundary_End()
        Rel(customer, spa, "Submits credentials", "HTTPS")
        Rel(spa, signin, "POST /signin", "JSON/HTTPS")
        """;

    // ── Participants ──────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void Elements_BecomeParticipantCardsInDeclarationOrder()
    {
        var d = Project(Src);
        CollectionAssert.AreEqual(
            new[] { "customer", "spa", "signin", "users" },
            d.Participants.Select(p => p.Id).ToArray());

        var spa = d.Find("spa")!;
        Assert.AreEqual("Single-Page App", spa.Label);
        Assert.IsNotNull(spa.Card);
        Assert.AreEqual(C4ElementKind.Container, spa.Card!.Kind);
        Assert.AreEqual("Angular", spa.Card.Technology);
        Assert.AreEqual(C4ElementShape.Person, d.Find("customer")!.Card!.Shape);
        Assert.AreEqual(C4ElementShape.Database, d.Find("users")!.Card!.Shape);
    }

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void Elements_DescriptionsAreHiddenUnlessAskedFor()
    {
        // A lifeline head is a column header; a paragraph in every column just pushes them apart.
        const string body = """
            C4Sequence
            Container(api, "API", "Java", "A long description of the API application.")
            """;
        Assert.IsNull(Project(body).Find("api")!.Card!.Description);
        Assert.AreEqual(
            "A long description of the API application.",
            Project(body + "\nSHOW_ELEMENT_DESCRIPTIONS()\n").Find("api")!.Card!.Description);
    }

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void Elements_TitleAndTagStylesCarryOver()
    {
        var d = Project("""
            C4Sequence
            title My title
            AddElementTag("hot", $bgColor="#f00")
            Container(api, "API", $tags="hot")
            """);
        Assert.AreEqual("My title", d.Title);
        Assert.AreEqual("#f00", d.Find("api")!.Card!.FillColor);
    }

    // ── Boundaries ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void Boundary_BecomesTheBoxGroupingWithItsMembers()
    {
        var d = Project(Src);
        var box = d.Boxes.Single();
        Assert.AreEqual("API Application [Container]", box.Label);
        CollectionAssert.AreEqual(new[] { "signin", "users" }, box.ParticipantIds);
    }

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void Boundary_EndStopsTheGrouping()
    {
        var d = Project("""
            C4Sequence
            Boundary(b, "Group")
            Container(inside, "Inside")
            Boundary_End()
            Container(outside, "Outside")
            """);
        CollectionAssert.AreEqual(new[] { "inside" }, d.Boxes.Single().ParticipantIds);
    }

    // ── Messages ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void Rel_BecomesAMessageWithItsTechnology()
    {
        var d = Project(Src);
        var first = d.Messages[0];
        Assert.AreEqual("customer", first.FromId);
        Assert.AreEqual("spa", first.ToId);
        Assert.AreEqual("Submits credentials", first.Text);
        Assert.AreEqual("HTTPS", first.Technology);
        Assert.AreEqual(SequenceArrowHead.Filled, first.Head);
    }

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void RelBack_ReversesTheMessage()
    {
        var d = Project("C4Sequence\nRel_Back(api, db, \"Returns the record\")\n");
        var m = d.Messages.Single();
        Assert.AreEqual("db", m.FromId);
        Assert.AreEqual("api", m.ToId);
    }

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void BiRel_IsBidirectional()
    {
        Assert.IsTrue(Project("C4Sequence\nBiRel(a, b, \"Both\")\n").Messages.Single().Bidirectional);
    }

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void ShowIndex_NumbersEveryMessage_AndAnExplicitIndexWins()
    {
        var numbered = Project("""
            C4Sequence
            SHOW_INDEX()
            Rel(a, b, "one")
            Rel(b, c, "two")
            RelIndex(9, c, d, "nine")
            Rel(d, e, "ten")
            """);
        CollectionAssert.AreEqual(
            new int?[] { 1, 2, 9, 10 },
            numbered.Messages.Select(m => m.Number).ToArray());
    }

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void ShowIndexOff_LeavesMessagesUnnumbered()
    {
        var d = Project("C4Sequence\nRel(a, b, \"one\")\nRel(b, c, \"two\")\n");
        Assert.IsTrue(d.Messages.All(m => m.Number is null), "C4 sequence numbers only when asked");
    }

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void ShowFootBoxes_Propagates()
    {
        Assert.IsTrue(Project("C4Sequence\nRel(a, b, \"x\")\n").ShowFootBoxes, "on unless turned off");
        Assert.IsFalse(Project("C4Sequence\nSHOW_FOOT_BOXES(false)\nRel(a, b, \"x\")\n").ShowFootBoxes);
    }

    // ── The two languages meeting ─────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void NativeControlLines_WrapC4Relationships()
    {
        // The whole point of keeping unclaimed lines in order: alt/else/end come from the native
        // grammar and have to nest correctly around messages the C4 reader produced.
        var d = Project("""
            C4Sequence
            Person(user, "User")
            Container(api, "API")
            alt credentials valid
              Rel(user, api, "Signs in")
            else rejected
              Rel_Back(user, api, "401")
            end
            """);

        var kinds = d.Items.Select(i => i.GetType().Name).ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                nameof(SequenceFragment), nameof(SequenceMessage),
                nameof(SequenceFragment), nameof(SequenceMessage),
                nameof(SequenceFragment),
            },
            kinds);

        var begin = (SequenceFragment)d.Items[0];
        Assert.AreEqual(FragmentBoundary.Begin, begin.Boundary);
        Assert.AreEqual(FragmentKind.Alt, begin.Kind);
        Assert.AreEqual("credentials valid", begin.Label);
        Assert.AreEqual(FragmentBoundary.Section, ((SequenceFragment)d.Items[2]).Boundary);
        Assert.AreEqual(FragmentBoundary.End, ((SequenceFragment)d.Items[4]).Boundary);
    }

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void NativeNotesAndActivationsAlsoWork()
    {
        var d = Project("""
            C4Sequence
            Person(user, "User")
            Container(api, "API")
            activate api
            Note over user,api: A shared note
            deactivate api
            """);
        Assert.AreEqual(2, d.Items.OfType<SequenceActivation>().Count());
        var note = d.Items.OfType<SequenceNote>().Single();
        Assert.AreEqual("A shared note", note.Text);
        CollectionAssert.AreEqual(new[] { "user", "api" }, note.ParticipantIds);
    }

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void NativeParticipantsCoexistWithCards()
    {
        var d = Project("""
            C4Sequence
            Person(user, "User")
            participant Legacy as Old System
            Rel(user, Legacy, "Calls")
            """);
        Assert.IsNotNull(d.Find("user")!.Card);
        Assert.IsNull(d.Find("Legacy")!.Card, "a native participant stays a plain box");
        Assert.AreEqual("Old System", d.Find("Legacy")!.Label);
    }

    // ── Rendering ─────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-sequence")]
    public void Render_RoutesToTheSequenceRendererNotRawText() => UiThread.Run(() =>
    {
        var fe = DiagramRenderer.Render("mermaid", Src, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
        // The raw-source fallback wraps a TextBlock; the sequence renderer wraps a scrolling canvas.
        Assert.IsInstanceOfType(((Border)fe).Child, typeof(ScrollViewer));
    });

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-sequence")]
    public void Render_BothPalettesAndTheFullSampleDraw() => UiThread.Run(() =>
    {
        const string full = """
            C4Sequence
            title Everything
            SHOW_INDEX()
            SHOW_FOOT_BOXES(false)
            SHOW_ELEMENT_DESCRIPTIONS()
            Person(user, "User", "Someone with an account.")
            Boundary(b, "API", "Container")
              Component(api, "Controller", "Spring MVC", "Handles the request.")
              ComponentQueue(q, "Queue", "Kafka")
            Boundary_End()
            ContainerDb(db, "Database", "SQL")
            Rel(user, api, "Signs in", "HTTPS")
            alt valid
              Rel(api, db, "Reads", "JDBC")
            else invalid
              Rel_Back(user, api, "401")
            end
            BiRel(api, q, "Publishes and consumes", "Kafka")
            """;
        foreach (var palette in new[] { MarkdownPalette.Dark, MarkdownPalette.Light })
            Assert.IsNotNull(DiagramRenderer.Render("mermaid", full, palette));
    });

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-sequence")]
    public void Render_EmptyDiagramDoesNotThrow() => UiThread.Run(() =>
    {
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", "C4Sequence\n", MarkdownPalette.Dark));
    });
}
