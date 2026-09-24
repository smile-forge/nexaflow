using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Sequence;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// What a <c>sequenceDiagram</c> draws: the participants in a row along the top, their lifelines running down the page, and
/// every message, note and frame written under them as a row on that timeline.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("sequence-diagram")]
public class SequenceBuilderTests : MermaidBuilderContract
{
    /// <summary>The diagram the documentation opens with.</summary>
    private const string Intro =
        "sequenceDiagram\n    Alice->>John: Hello John, how are you?\n    John-->>Alice: Great!";

    /// <summary>The documentation's frames, nested and divided.</summary>
    private const string Framed =
        "sequenceDiagram\n    Alice->>Bob: Hello Bob, how are you?\n    alt is sick\n        Bob->>Alice: Not so good :(\n"
        + "    else is well\n        Bob->>Alice: Feeling fresh like a daisy\n    end\n    opt Extra response\n"
        + "        Bob->>Alice: Thanks for asking\n    end";

    public override MermaidDiagram Diagram => MermaidDiagram.Sequence;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("the documentation's own", Intro),
        ("frames, nested and divided", Framed),
        ("participants declared in order", "sequenceDiagram\n  participant Alice\n  participant Bob\n  Bob->>Alice: Hi Alice"),
        ("an actor", "sequenceDiagram\n  actor Alice\n  participant Bob\n  Alice->>Bob: Hi Bob"),
        ("every kind of participant there is",
         "sequenceDiagram\n  participant A@{ \"type\": \"boundary\" }\n  participant B@{ \"type\": \"control\" }\n"
         + "  participant C@{ \"type\": \"entity\" }\n  participant D@{ \"type\": \"database\" }\n"
         + "  participant E@{ \"type\": \"collections\" }\n  participant F@{ \"type\": \"queue\" }\n  A->>F: x"),
        ("aliases", "sequenceDiagram\n  participant A as Alice Liddell\n  participant J as John Doe\n  A->>J: Hello"),
        ("every arrow there is",
         "sequenceDiagram\n  A->B: x\n  A-->B: x\n  A->>B: x\n  A-->>B: x\n  A-xB: x\n  A--xB: x\n  A-)B: x\n  A--)B: x"),
        ("both ways at once", "sequenceDiagram\n  A<<->>B: x\n  A<<-->>B: x"),
        ("half an arrow", "sequenceDiagram\n  A-|\\B: x\n  A-|/B: x\n  A/|-B: x\n  A\\|-B: x"),
        ("a stick for a head", "sequenceDiagram\n  A-\\\\B: x\n  A-//B: x\n  A//-B: x\n  A\\\\-B: x"),
        ("a message to a participant itself", "sequenceDiagram\n  A->>A: thinking it over\n  A->>B: done"),
        ("bars started and ended", "sequenceDiagram\n  A->>+B: x\n  B->>+C: y\n  C-->>-B: z\n  B-->>-A: done"),
        ("bars written on their own", "sequenceDiagram\n  activate A\n  A->>B: x\n  deactivate A"),
        ("notes everywhere one goes",
         "sequenceDiagram\n  participant A\n  participant B\n  Note left of A: to the left\n  Note right of B: to the right\n"
         + "  Note over A,B: over them both\n  Note over A: over one"),
        ("a participant made and ended partway down",
         "sequenceDiagram\n  A->>B: x\n  create participant C\n  A->>C: hi\n  destroy C\n  A-xC: bye"),
        ("boxes round the participants",
         "sequenceDiagram\n  box Purple Alice & John\n  participant A\n  participant J\n  end\n  box Another Group\n"
         + "  participant B\n  end\n  A->>J: x\n  J->>B: y"),
        ("a wash of colour", "sequenceDiagram\n  rect rgb(191, 223, 255)\n  A->>B: x\n  A->>B: y\n  end"),
        ("frames of every kind",
         "sequenceDiagram\n  loop a\n    A->>B: x\n  end\n  par b\n    A->>B: y\n  and c\n    A->>B: z\n  end\n"
         + "  critical d\n    A->>B: p\n  option e\n    A->>B: q\n  end\n  break f\n    A->>B: r\n  end"),
        ("numbering", "sequenceDiagram\n  autonumber\n  A->>B: x\n  A->>B: y\n  autonumber off\n  A->>B: z"),
        ("links under a participant",
         "sequenceDiagram\n  participant A\n  link A: Dashboard @ https://example.com/a\n  A->>B: x"),
        ("a title over it", "sequenceDiagram\n  title How it goes\n  A->>B: x"),
        ("the front matter's own sizes",
         "---\nconfig:\n  sequence:\n    actorMargin: 90\n    mirrorActors: false\n    messageFontSize: 12\n---\n"
         + "sequenceDiagram\n  A->>B: x"),
        ("still being written", "sequenceDiagram\n  participant \n  ->>: "),
        ("what nobody means to write", "sequenceDiagram\n  ??? !!!\n  end\n  else nothing"),
        ("nothing to draw", "sequenceDiagram"),
    ];

    [TestMethod]
    public void TheParticipantsStandInARowAlongTheTop() => UiThread.Run(() =>
    {
        var heads = Standing(Lay("sequenceDiagram\n  participant A\n  participant B\n  participant C\n  A->>C: x"));

        Assert.AreEqual(3, heads.Count);
        Assert.AreEqual(heads[0].Top, heads[1].Top, 0.5, "they all start at the same height");
        Assert.IsTrue(heads[0].Right < heads[1].Left, "and stand in the order they were declared");
        Assert.IsTrue(heads[1].Right < heads[2].Left);
    });

    [TestMethod]
    public void AMessageRunsBetweenTheTwoLifelinesItNames() => UiThread.Run(() =>
    {
        var laid = Lay(Intro);
        var heads = Standing(laid);
        var lines = Pieces(laid, SequencePiece.Line).Select(piece => piece.Bounds).ToList();

        Assert.AreEqual(2, lines.Count);

        foreach (var line in lines)
        {
            Assert.IsTrue(line.Top > heads[0].Bottom, $"a message is drawn under the participants: {line} under {heads[0]}");
            Assert.IsTrue(line.Left >= heads[0].Left && line.Right <= heads[1].Right, $"and between their lifelines: {line}");
        }

        Assert.IsTrue(lines[1].Top > lines[0].Top, "and each under the one before it");
    });

    [TestMethod]
    public void WhatAMessageSaysIsWrittenOverItsLine() => UiThread.Run(() =>
    {
        var laid = Lay("sequenceDiagram\n  Alice->>John: Hello John");
        var line = Pieces(laid, SequencePiece.Line).Single().Bounds;
        var said = Words(Pieces(laid, SequencePiece.Message).Single(), "Hello John");

        Assert.IsTrue(said.Bottom <= Middle(line).Y, $"what it says is over the line: {said} over {line}");
        Assert.IsTrue(said.Left >= line.Left - 1 && said.Right <= line.Right + 1, "and between its ends");
    });

    [TestMethod]
    public void ABreakInALabelStartsAnotherLine() => UiThread.Run(() =>
    {
        var laid = Lay("sequenceDiagram\n  participant A as Alice<br/>Johnson\n  A->>B: Hello,<br/>how are you?");

        var name = Said(Pieces(laid, SequencePiece.Head)[0]).Select(words => words.Words!.Glyphs.Text).ToList();
        var said = Said(Pieces(laid, SequencePiece.Message).Single()).Select(words => words.Words!.Glyphs.Text).ToList();

        CollectionAssert.AreEqual(new[] { "Alice", "Johnson" }, name);
        CollectionAssert.AreEqual(new[] { "Hello,", "how are you?" }, said);
    });

    [TestMethod]
    public void AMessageToAParticipantItselfLoopsOffItsOwnLifeline() => UiThread.Run(() =>
    {
        var laid = Lay("sequenceDiagram\n  participant A\n  participant B\n  A->>A: thinking\n  A->>B: done");
        var heads = Standing(laid);
        var loop = Pieces(laid, SequencePiece.Line)[0].Bounds;

        Assert.IsTrue(loop.Height > 4, $"the loop runs down as well as across: {loop}");
        Assert.IsTrue(loop.Right > heads[0].Left + (heads[0].Width / 2), "and out to the right of its own lifeline");
    });

    [TestMethod]
    public void AMessageToAParticipantItselfTurnsSquare_UnlessTheFrontMatterAsksForABow() => UiThread.Run(() =>
    {
        const string source = "sequenceDiagram\n  participant A\n  A->>A: thinking";

        Assert.IsFalse(Bowed(Lay(source)), "the loop turns square corners");
        Assert.IsTrue(Bowed(Lay("---\nconfig:\n  sequence:\n    rightAngles: false\n---\n" + source)), "and bows where rightAngles says not to");

        static bool Bowed(Laid laid) =>
            Pieces(laid, SequencePiece.Line).Single().SelfAndDescendants().SelectMany(piece => piece.Marks.ToArray()).OfType<GeometryMark>()
                .SelectMany(mark => PathGeometry.CreateFromGeometry(mark.Shape).Figures)
                .SelectMany(figure => figure.Segments)
                .Any(segment => segment is BezierSegment or PolyBezierSegment);
    });

    [TestMethod]
    public void AFrameHoldsTheMessagesWrittenInsideIt() => UiThread.Run(() =>
    {
        var laid = Lay(Framed);
        var frames = Pieces(laid, SequencePiece.Frame);

        Assert.AreEqual(2, frames.Count, "an alt and an opt");

        var inside = frames[0].SelfAndDescendants().Where(piece => piece.Kind == SequencePiece.Message).ToList();

        Assert.AreEqual(2, inside.Count, "the two messages written between alt and end are drawn inside the frame");
        foreach (var message in inside)
            Assert.IsTrue(Holds(frames[0].Bounds, message.Bounds), $"{message.Bounds} is inside {frames[0].Bounds}");
    });

    [TestMethod]
    public void AFrameStandsForEverythingWrittenInIt() => UiThread.Run(() =>
    {
        const string source = "sequenceDiagram\n  loop every day\n    A->>B: x\n  end";
        var frame = Pieces(Lay(source), SequencePiece.Frame).Single();

        Assert.AreEqual("loop every day\n    A->>B: x\n  end", Written(source, frame.Part));
    });

    [TestMethod]
    public void AFrameInsideOneIsDrawnInsideIt() => UiThread.Run(() =>
    {
        var laid = Lay("sequenceDiagram\n  loop a\n    alt b\n      A->>B: x\n    end\n  end");
        var frames = Pieces(laid, SequencePiece.Frame);

        Assert.AreEqual(2, frames.Count);
        Assert.IsTrue(Holds(frames[0].Bounds, frames[1].Bounds), $"{frames[1].Bounds} is inside {frames[0].Bounds}");
    });

    [TestMethod]
    public void ADividedFrameDrawsALineWhereItIsDivided() => UiThread.Run(() =>
    {
        var laid = Lay(Framed);
        var frame = Pieces(laid, SequencePiece.Frame)[0].Bounds;
        var divider = Pieces(laid, SequencePiece.Divider).Single().Bounds;

        Assert.IsTrue(divider.Top > frame.Top && divider.Bottom < frame.Bottom + 1,
                      $"the line is drawn across the frame: {divider} in {frame}");
    });

    [TestMethod]
    public void ANoteOverTwoParticipantsSpansThem() => UiThread.Run(() =>
    {
        var laid = Lay("sequenceDiagram\n  participant A\n  participant B\n  Note over A,B: over them both");
        var heads = Standing(laid);
        var note = Pieces(laid, SequencePiece.Note).Single().Bounds;

        Assert.IsTrue(note.Left <= heads[0].Left + 1 && note.Right >= heads[1].Right - 1, $"the note spans both: {note}");
    });

    [TestMethod]
    public void ANoteSitsToTheSideItSays() => UiThread.Run(() =>
    {
        var left = Lay("sequenceDiagram\n  participant A\n  participant B\n  Note left of B: beside it\n  A->>B: x");
        var right = Lay("sequenceDiagram\n  participant A\n  participant B\n  Note right of A: beside it\n  A->>B: x");

        var lanes = Standing(left);
        Assert.IsTrue(Pieces(left, SequencePiece.Note).Single().Bounds.Right <= lanes[1].Left + lanes[1].Width,
                      "a note to the left of one sits to the left of its lifeline");

        var others = Standing(right);
        Assert.IsTrue(Pieces(right, SequencePiece.Note).Single().Bounds.Left >= others[0].Left,
                      "and one to the right sits to the right of it");
    });

    [TestMethod]
    public void ABarSaysAParticipantIsWorkingForAsLongAsItIsOpen() => UiThread.Run(() =>
    {
        var laid = Lay("sequenceDiagram\n  A->>+B: x\n  B->>C: y\n  B-->>-A: done");
        var bar = Pieces(laid, SequencePiece.Bar).Single().Bounds;
        var lines = Pieces(laid, SequencePiece.Line).Select(piece => piece.Bounds).ToList();

        Assert.IsTrue(bar.Top <= Middle(lines[0]).Y + 1, "the bar starts at the message that opened it");
        Assert.IsTrue(bar.Bottom >= Middle(lines[2]).Y - 1, "and runs to the one that ended it");
    });

    [TestMethod]
    public void AnActivateWrittenAfterAMessageStartsTheBarWhereThatMessageRuns() => UiThread.Run(() =>
    {
        var laid = Lay("sequenceDiagram\n  Alice->>John: Hello John, how are you?\n  activate John\n  John-->>Alice: Great!\n  deactivate John");
        var bar = Pieces(laid, SequencePiece.Bar).Single().Bounds;
        var lines = Pieces(laid, SequencePiece.Line).Select(piece => piece.Bounds).ToList();

        Assert.AreEqual(Middle(lines[0]).Y, bar.Top, 1, "the bar starts where the message activating it runs, as a + on it would");
        Assert.AreEqual(Middle(lines[1]).Y, bar.Bottom, 1, "and ends where the message before the deactivate runs");
        Assert.IsTrue(lines[0].Right <= bar.Left + 1, $"and the message meets the bar rather than the lifeline under it: {lines[0]} to {bar}");
    });

    [TestMethod]
    public void TheParticipantsAreDrawnAgainAtTheBottomUnlessTheFrontMatterSaysNot() => UiThread.Run(() =>
    {
        Assert.AreEqual(4, Pieces(Lay("sequenceDiagram\n  A->>B: x"), SequencePiece.Head).Count,
                        "two participants, each drawn at the top and again at the foot");

        var once = "---\nconfig:\n  sequence:\n    mirrorActors: false\n---\nsequenceDiagram\n  A->>B: x";
        Assert.AreEqual(2, Pieces(Lay(once), SequencePiece.Head).Count, "and once each where the front matter says not to");
    });

    [TestMethod]
    public void AParticipantMadePartwayDownStandsWhereTheMessageMakesIt() => UiThread.Run(() =>
    {
        var laid = Lay("sequenceDiagram\n  A->>B: x\n  create participant C\n  A->>C: hi");
        var heads = Pieces(laid, SequencePiece.Head).Select(piece => piece.Bounds).ToList();
        var lines = Pieces(laid, SequencePiece.Line).Select(piece => piece.Bounds).ToList();

        var made = heads.Where(head => head.Top > lines[0].Top).OrderBy(head => head.Top).First();

        Assert.IsTrue(made.Top > lines[0].Bottom, $"the one made partway down stands below the first message: {made}");
    });

    [TestMethod]
    public void ABoxHoldsTheParticipantsWrittenInIt() => UiThread.Run(() =>
    {
        var laid = Lay("sequenceDiagram\n  box Purple The shop\n  participant A\n  participant B\n  end\n"
                       + "  participant C\n  A->>C: x");

        var box = Pieces(laid, SequencePiece.Box).Single().Bounds;
        var heads = Standing(laid);

        Assert.IsTrue(Holds(box, heads[0]) && Holds(box, heads[1]), "the two inside it are inside the box");
        Assert.IsFalse(Holds(box, heads[2]), "and the one outside it is not");
    });

    [TestMethod]
    public void ABoxWithNothingWrittenOnItKeepsNoBandAboveTheLifelines() => UiThread.Run(() =>
    {
        var named = Standing(Lay("sequenceDiagram\n  box The shop\n  participant A\n  end\n  A->>A: x"));
        var bare = Standing(Lay("sequenceDiagram\n  box\n  participant A\n  end\n  A->>A: x"));

        Assert.IsTrue(named[0].Top > bare[0].Top, $"a box that says something reserves room to say it: {named[0]} under {bare[0]}");
    });

    [TestMethod]
    public void AutonumberDrawsTheNumberAgainstEachMessageUnderIt() => UiThread.Run(() =>
    {
        var laid = Lay("sequenceDiagram\n  A->>B: x\n  autonumber\n  A->>B: y\n  A->>B: z");
        var numbers = Pieces(laid, SequencePiece.Number);

        CollectionAssert.AreEqual(new[] { "1", "2" }, numbers.Select(piece => Said(piece).Single().Words!.Glyphs.Text).ToArray());
    });

    [TestMethod]
    public void AMessagesNumberIsDrawnOnTheLifelineItLeaves() => UiThread.Run(() =>
    {
        var laid = Lay("sequenceDiagram\n  autonumber\n  A->>B: x\n  B->>A: y");
        var heads = Standing(laid);
        var numbers = Pieces(laid, SequencePiece.Number).Select(piece => Middle(piece.Bounds)).ToList();

        Assert.AreEqual(Middle(heads[0]).X, numbers[0].X, 1, "the first leaves A, and is numbered on A's lifeline");
        Assert.AreEqual(Middle(heads[1]).X, numbers[1].X, 1, "the second leaves B, and is numbered on B's");
    });

    [TestMethod]
    public void AFramesTitleIsSetAcrossTheMiddleOfIt() => UiThread.Run(() =>
    {
        var laid = Lay("sequenceDiagram\n  A->>B: a message long enough to make the frame wide\n  alt is sick\n    B->>A: no\n  else is well\n    B->>A: yes\n  end");
        var frame = Pieces(laid, SequencePiece.Frame).Single();

        Assert.AreEqual(Middle(frame.Bounds).X, Middle(Words(frame, "is sick")).X, 1, "what the frame is about is its title");
        Assert.AreEqual(Middle(frame.Bounds).X, Middle(Words(frame, "is well")).X, 1, "and so is what each part of it is about");
    });

    [TestMethod]
    public void ALifelineRunsDownToTheTopOfItsOwnFoot() => UiThread.Run(() =>
    {
        // An actor's figure is deeper than a box, so the feet stand in a band deeper than the box's.
        var laid = Lay("sequenceDiagram\n  actor A\n  participant B\n  A->>B: x");
        var lifeline = Pieces(laid, SequencePiece.Lifeline)[1];
        var line = lifeline.Children.First(piece => piece.Kind == MermaidPiece.Shape).Bounds;
        var foot = lifeline.SelfAndDescendants().Where(piece => piece.Kind == SequencePiece.Head).MaxBy(head => head.Bounds.Top)!.Bounds;

        Assert.AreEqual(foot.Top, line.Bottom, 1, "the line meets the box it runs down to");
    });

    [TestMethod]
    public void AParticipantOfAKindIsABoxWithAMarkOfItBeforeItsName_AndAnActorAFigure() => UiThread.Run(() =>
    {
        var laid = Lay("sequenceDiagram\n  participant D@{ \"type\" : \"database\" }\n  actor U\n  D->>U: x");
        var heads = Pieces(laid, SequencePiece.Lifeline).Select(lifeline => lifeline.Children.First(piece => piece.Kind == SequencePiece.Head)).ToList();
        var (database, actor) = (Drawing(heads[0]), Drawing(heads[1]));

        Assert.AreEqual(2, database.Count, "a box, and the mark of a database");
        Assert.IsTrue(database[1].Shape.Bounds.Right < Middle(database[0].Shape.Bounds).X, "the mark set before the name, at the left of the box");
        Assert.AreEqual(1, actor.Count, "an actor is the figure alone, with its name under it");

        static List<GeometryMark> Drawing(Piece head) =>
            [.. head.Children.First(piece => piece.Kind == MermaidPiece.Shape).Marks.ToArray().OfType<GeometryMark>()];
    });

    [TestMethod]
    public void ALinkUnderAParticipantLeadsWhereItSays() => UiThread.Run(() =>
    {
        var laid = Lay("sequenceDiagram\n  participant A\n  link A: Dashboard @ https://example.com/a\n  A->>B: x");
        var menu = Pieces(laid, SequencePiece.Menu).Single();

        Assert.AreEqual("https://example.com/a", menu.Acts?.Click?.Target);
        Assert.AreEqual("Dashboard", Said(menu).Single().Words!.Glyphs.Text);
    });

    [TestMethod]
    public void AParticipantNothingReachesIsLeftOutWhereTheFrontMatterAsks() => UiThread.Run(() =>
    {
        const string source = "sequenceDiagram\n  participant A\n  participant B\n  participant C\n  A->>B: x";

        Assert.AreEqual(3, Standing(Lay(source)).Count);

        var hidden = "---\nconfig:\n  sequence:\n    hideUnusedParticipants: true\n---\n" + source;
        Assert.AreEqual(2, Standing(Lay(hidden)).Count);
    });

    [TestMethod]
    public void HowFarApartTheLifelinesStandIsWhatTheFrontMatterAsks() => UiThread.Run(() =>
    {
        var near = Standing(Lay("sequenceDiagram\n  participant A\n  participant B\n  A->>B: x"));
        var far = Standing(Lay("---\nconfig:\n  sequence:\n    actorMargin: 200\n---\n"
                               + "sequenceDiagram\n  participant A\n  participant B\n  A->>B: x"));

        Assert.IsTrue(far[1].Left - far[0].Right > near[1].Left - near[0].Right + 50,
                      "a wider actorMargin stands them further apart");
    });

    // ── Reading what was drawn ──────────────────────────────────────────────

    /// <summary>Where each participant's own box stands at the top of its lifeline, in the order they stand.</summary>
    private static List<Rect> Standing(Laid laid) =>
        [.. Pieces(laid, SequencePiece.Lifeline)
              .Select(piece => piece.SelfAndDescendants().First(part => part.Kind == SequencePiece.Head).Bounds)
              .OrderBy(bounds => bounds.Left)];

    private static Rect Words(Piece piece, string said) => Said(piece).First(words => words.Words!.Glyphs.Text == said).Bounds;

    private static IEnumerable<Piece> Said(Piece piece) =>
        piece.SelfAndDescendants().Where(part => part.Kind == MermaidPiece.Words && part.Words is not null);

    private static bool Holds(Rect over, Rect inner) =>
        inner.Left >= over.Left - 1 && inner.Right <= over.Right + 1 && inner.Top >= over.Top - 1
        && inner.Bottom <= over.Bottom + 1;
}
