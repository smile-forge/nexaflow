using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Timeline;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A <c>timeline</c> block drawn on the shared layout tree: periods on a spine standing for their lines, the events
/// written for each stacked away from it, a band over the periods each section groups, and every word typed into where
/// it is drawn.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("timeline")]
public class TimelineBuilderTests : MermaidBuilderContract
{
    private const string Social =
        "timeline\n  title History of Social Media\n  2002 : LinkedIn\n  2004 : Facebook : Google\n  2005 : YouTube";

    private const string Pizzas =
        "timeline\n  section 2021-2022\n    Bought pizzas : Lorem Ipsum\n                  : Dolor Sit\n    Ate pizzas : Amet\n  section 2022-2023\n    Bought pizzas : Consectetur";

    public override MermaidDiagram Diagram => MermaidDiagram.Timeline;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("the documented timeline", Social),
        ("sections and a line going on from a period", Pizzas),
        ("down the page", "timeline TD\n  section Early\n    2002 : LinkedIn : Friendster\n  section Later\n    2006 : Twitter"),
        ("a line break and a colon in what an event says", "timeline\n  2005 : YouTube<br>launched : 9#colon;00"),
        ("colours and padding of its own",
            "---\nconfig:\n  timeline:\n    padding: 4\n  themeVariables:\n    cScale0: \"#4e79a7\"\n    cScaleLabel0: \"#ffffff\"\n---\ntimeline\n  2002 : LinkedIn\n  2004 : Facebook"),
        ("everything on one colour", "---\nconfig:\n  timeline:\n    disableMulticolor: true\n---\ntimeline\n  2002 : LinkedIn\n  2004 : Facebook"),
        ("still being written", "timeline\n  2004 : \n  section \n  direction "),
        ("what nobody means to write", "timeline\n  : Google\n  direction sideways\n  2004 : : Facebook"),
        ("nothing to draw", "timeline"),
    ];

    private static Laid Build(string source, double room = 900) =>
        TimelineBuilder.Build(MermaidBuilders.Read(source), new DiagramLaying(MarkdownPalette.Dark, room));

    [TestMethod]
    public void ThePeriodsSitOnASpineAcrossThePage_EachWithItsEventsBelowIt() => UiThread.Run(() =>
    {
        var laid = Build(Social);
        var periods = Pieces(laid, TimelinePiece.Period);
        var events = Pieces(laid, TimelinePiece.Event);

        Assert.AreEqual(3, periods.Count);
        Assert.AreEqual(4, events.Count);
        Assert.IsTrue(periods[0].Bounds.Right <= periods[1].Bounds.Left, "one period after another");
        Assert.AreEqual(periods[0].Bounds.Top, periods[1].Bounds.Top, 0.5, "all on the one spine");
        Assert.IsTrue(events.All(said => said.Bounds.Top >= periods[0].Bounds.Bottom), "the events below the spine");
    });

    [TestMethod]
    public void APeriodStandsForItsLine_AndAnEventForWhatItSaysWhereItSaysIt() => UiThread.Run(() =>
    {
        var laid = Build(Social);

        Assert.AreEqual("2004 : Facebook : Google", Written(Social, Pieces(laid, TimelinePiece.Period)[1].Part));
        Assert.AreEqual("Google", Written(Social, Pieces(laid, TimelinePiece.Event)[2].Part));
        Assert.IsTrue(Pieces(laid, TimelinePiece.Says).All(said => said.Words is { Maps: true }), "typed into where it is drawn");
    });

    [TestMethod]
    public void AnEventGoingOnFromALineAboveIsDrawnWithItsOwnPeriod() => UiThread.Run(() =>
    {
        var laid = Build(Pizzas);
        var periods = Pieces(laid, TimelinePiece.Period);
        var events = Pieces(laid, TimelinePiece.Event);

        Assert.AreEqual("Dolor Sit", Written(Pizzas, events[1].Part));
        Assert.AreEqual(Middle(periods[0].Bounds).X, Middle(events[1].Bounds).X, 0.5, "under the period it goes on from");
    });

    [TestMethod]
    public void DownThePageThePeriodsRunInAColumn_WithTheirEventsBeside() => UiThread.Run(() =>
    {
        var laid = Build("timeline TD\n  2002 : LinkedIn\n  2006 : Twitter");
        var periods = Pieces(laid, TimelinePiece.Period);
        var events = Pieces(laid, TimelinePiece.Event);

        Assert.AreEqual(periods[0].Bounds.Left, periods[1].Bounds.Left, 0.5, "one column");
        Assert.IsTrue(periods[0].Bounds.Bottom <= periods[1].Bounds.Top, "one period under another");
        Assert.IsTrue(events.All(said => said.Bounds.Left >= periods[0].Bounds.Right), "the events beside the spine");
    });

    [TestMethod]
    public void ASectionBandsThePeriodsItGroups_AndStandsForItsLine() => UiThread.Run(() =>
    {
        var laid = Build(Pizzas);
        var bands = Pieces(laid, TimelinePiece.Section);
        var periods = Pieces(laid, TimelinePiece.Period);

        Assert.AreEqual(2, bands.Count);
        Assert.AreEqual("section 2021-2022", Written(Pizzas, bands[0].Part));
        Assert.IsTrue(bands[0].Bounds.Bottom <= periods[0].Bounds.Top, "over the periods it groups");
        Assert.IsTrue(bands[0].Bounds.Right >= periods[1].Bounds.Right - 0.5, "as wide as both of them");
        Assert.IsTrue(bands[1].Bounds.Left >= periods[1].Bounds.Right, "the next section starts past them");
    });

    [TestMethod]
    public void WithSectionsAPeriodTakesItsSectionsColour_AndWithoutThemOneOfItsOwn() => UiThread.Run(() =>
    {
        var grouped = Pieces(Build(Pizzas), TimelinePiece.Period).Select(Fill).ToList();
        var alone = Pieces(Build(Social), TimelinePiece.Period).Select(Fill).ToList();

        Assert.AreEqual(grouped[0], grouped[1], "both in the first section");
        Assert.AreNotEqual(grouped[1], grouped[2], "and the next section is its own colour");
        Assert.AreNotEqual(alone[0], alone[1], "each period its own");
    });

    [TestMethod]
    public void DisableMulticolorPutsEveryPeriodOnTheFirstSlot() => UiThread.Run(() =>
    {
        var fills = Pieces(Build("---\nconfig:\n  timeline:\n    disableMulticolor: true\n---\ntimeline\n  2002 : A\n  2004 : B"), TimelinePiece.Period)
            .Select(Fill).ToList();

        Assert.AreEqual(fills[0], fills[1]);
    });

    [TestMethod]
    public void AColourWrittenForASlotIsWhatThatSlotIsDrawnIn() => UiThread.Run(() =>
    {
        var laid = Build("---\nconfig:\n  themeVariables:\n    cScale0: \"#4e79a7\"\n---\ntimeline\n  2002 : LinkedIn");

        Assert.AreEqual(Color.FromRgb(0x4E, 0x79, 0xA7), Fill(Pieces(laid, TimelinePiece.Period).Single()));
    });

    [TestMethod]
    public void WhatHoldsAnEntityCodeSaysWhatTheCodeStandsFor_AndIsWorkedOutRatherThanTypedInto() => UiThread.Run(() =>
    {
        var said = Pieces(Build("timeline\n  2005 : 9#colon;00"), TimelinePiece.Says).Select(piece => piece.Words!).ToList();

        Assert.IsTrue(said.Any(words => words.Glyphs.Text == "9:00" && !words.Maps), "worked out, so only pressed");
    });
}
