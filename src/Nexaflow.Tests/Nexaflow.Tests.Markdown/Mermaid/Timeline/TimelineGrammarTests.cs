using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Timeline;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Timeline;

/// <summary>
/// What a <c>timeline</c> block is read into: which way it runs, its sections, and its periods with the events written
/// after each colon — and what is written instead of any of that, held with the reason.
/// </summary>
[TestClass]
[CoversNode("timeline-ast")]
public class TimelineGrammarTests : MermaidGrammarContract
{
    /// <summary>The timeline the Mermaid documentation opens with.</summary>
    public const string Social =
        """
        timeline
            title History of Social Media Platform
            2002 : LinkedIn
            2004 : Facebook : Google
            2005 : YouTube
            2006 : Twitter
        """;

    /// <summary>The documentation's sections, with events going on over a continuation line.</summary>
    public const string Pizzas =
        """
        timeline
            title MermaidChart timeline
            section 2021-2022
                Bought pizzas : Lorem Ipsum
                              : Lorem Ipsum
                Ate pizzas : Lorem Ipsum
            section 2022-2023
                Bought pizzas : Lorem Ipsum
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.Timeline;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Social,
        Pizzas,
        "timeline\n    title England's History Timeline\n    section Stone Age\n        7600 BC : Britain's oldest known house was built in Orkney, Scotland\n        6000 BC : Sea levels rise and Britain becomes an island.<br>The people who live here are hunter-gatherers.",
        "---\nconfig:\n  timeline:\n    disableMulticolor: true\n---\ntimeline\n    direction TD\n    2002 : LinkedIn\n    2004 : Facebook",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("a period with no events", "timeline\n    2002"),
        ("events on a continuation line", "timeline\n    2004 : Facebook\n         : Google"),
        ("a line break and a colon in what an event says", "timeline\n    2005 : YouTube<br>launched : 9#colon;00"),
        ("the way it runs on the header line", "timeline TD\n    2002 : LinkedIn"),
        ("the way it runs on a line of its own", "timeline\n    direction TD\n    2002 : LinkedIn"),
        ("sections", "timeline\n    section Early\n        2002 : LinkedIn\n    section Later\n        2006 : Twitter"),
        ("no space round the colons", "timeline\n    2004:Facebook:Google"),
        ("a comment and a blank line", "timeline\n\n    %% the early days\n    2002 : LinkedIn %% first"),
        ("accessibility lines", "timeline\n    accTitle: Social media\n    accDescr: By year\n    2002 : LinkedIn"),
        ("written on Windows", "timeline\r\n    section Early  \r\n        2002 : LinkedIn\r\n"),
        // Half written.
        ("an event still to write", "timeline\n    2004 : "),
        ("a section still to name", "timeline\n    section "),
        ("the way it runs still to say", "timeline\n    direction "),
        ("nothing but the keyword", "timeline"),
        // What nobody means to write.
        ("a way nobody knows", "timeline\n    direction sideways"),
        ("events with no period above them", "timeline\n    : Google"),
        ("an event that says nothing", "timeline\n    2004 : : Google"),
    ];

    [TestMethod]
    public void TheDocumentedTimelinesLinesAreEachRead()
    {
        var tree = MermaidParser.Read(Social);

        Assert.AreEqual(4, Nodes(tree, TimelineKinds.Period).Count);
        Assert.AreEqual(5, Events(tree).Count);
        Assert.AreEqual(2, Nodes(MermaidParser.Read(Pizzas), TimelineKinds.Section).Count);
        Assert.AreEqual(1, Nodes(MermaidParser.Read(Pizzas), TimelineKinds.More).Count);
    }

    [TestMethod]
    public void WhatIsStillBeingWrittenIsNoComplaint()
    {
        foreach (var source in new[] { "timeline\n    2004 : ", "timeline\n    section ", "timeline\n    direction ", "timeline\n    2002", "timeline" })
            Assert.AreEqual(0, Trouble(source).Count, $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void WhatIsWrongIsSaid()
    {
        foreach (var (source, reason) in new[]
                 {
                     ("timeline\n    direction sideways", "runs LR"),
                     ("timeline\n    : Google", "the period above it"),
                 })
            Assert.IsTrue(Trouble(source).Any(said => said.Contains(reason, StringComparison.Ordinal)), $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void TheWayItRunsMayFollowTheKeyword()
    {
        Assert.AreEqual(1, Nodes(MermaidParser.Read("timeline TD\n    2002 : LinkedIn"), TimelineKinds.Direction).Count);
        Assert.AreEqual(0, Nodes(MermaidParser.Read("timeline\n    2002 : LinkedIn"), TimelineKinds.Direction).Count);
        Assert.AreEqual(0, Trouble("timeline TD\n    2002 : LinkedIn").Count);
    }

    [TestMethod]
    public void EveryColonSplitsWhatFollowsIntoEvents()
    {
        var events = Events(MermaidParser.Read("timeline\n    2004 : Facebook : Google\n         : Orkut"));

        Assert.AreEqual(3, events.Count);
        CollectionAssert.AreEqual(new[] { "Facebook", "Google", "Orkut" }, events.Select(text => text.Print().Trim()).ToArray());
    }

    [TestMethod]
    public void ANewLineUnderAPeriodIsAnotherEvent_AndElsewhereNothing()
    {
        var grammar = new TimelineGrammar();
        var period = Nodes(MermaidParser.Parse("timeline\n    2002 : LinkedIn"), TimelineKinds.Period).Single();
        var section = Nodes(MermaidParser.Parse("timeline\n    section Early"), TimelineKinds.Section).Single();

        Assert.AreEqual((": ", 2), grammar.Blank(period));
        Assert.IsNull(grammar.Blank(section));
    }

    [TestMethod]
    public void AColonTypedIntoWhatSomethingSaysIsWrittenAsTheEntityCodeForIt()
    {
        const string source = "timeline\n    2004 : Facebook";
        var says = ContentReading.Of(MermaidParser.Read(source)).Root.SelfAndDescendants()
            .First(part => part.Kind == MermaidKinds.Words && part.Text == "Facebook");
        var writing = new TimelineGrammar().Escaping(says, says.End, ": the wall")!.Value;

        Assert.AreEqual("timeline\n    2004 : Facebook#colon; the wall", source[..writing.Start] + writing.Text + source[writing.End..]);
    }

    [TestMethod]
    public void EachPeriodSaysWhichSectionItIsIn_AndEachContinuationWhichPeriod()
    {
        var read = ContentReading.Of(MermaidParser.Read(Pizzas)).Root;

        CollectionAssert.AreEqual(
            new[] { "0", "0", "1" },
            read.SelfAndDescendants().Where(part => part.Kind == TimelineKinds.Period).Select(period => period.Fact(TimelineRoles.In)).ToArray());
        Assert.AreEqual("0", read.SelfAndDescendants().Single(part => part.Kind == TimelineKinds.More).Fact(TimelineRoles.Of));
    }

    private static List<ContentNode> Nodes(ContentNode tree, string kind) => [.. tree.SelfAndDescendants().Where(node => node.Kind == kind)];

    private static List<ContentNode> Events(ContentNode tree) =>
        [.. tree.SelfAndDescendants().Where(node => node.Kind == TimelineKinds.Text && node.Role == TimelineRoles.Event)];

    private static List<string> Trouble(string source) =>
        [.. MermaidParser.Read(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>()];
}
