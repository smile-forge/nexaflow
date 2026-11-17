using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Timeline;
using Nexaflow.Markdown.Mermaid.Timeline.Stages;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Mermaid.Timeline;

/// <summary>
/// What <see cref="TimelineParser"/> reads a timeline into: which way it runs, its sections, and its periods with the events
/// written after each colon — and what is written instead of any of that, held with the reason. The header is optional, since a
/// <c>timeline</c> fence already says what the block is.
/// </summary>
[TestClass]
[CoversNode("timeline-ast")]
public class TimelineParserTests : ContentParserContract
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

    /// <inheritdoc/>
    protected override ContentNode Parse(string? source) => TimelineParser.Parse(source);

    /// <inheritdoc/>
    protected override IEnumerable<IAstStage> Stages(ContentNode tree, bool holes) => Offered(tree, holes);

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        "timeline\n    2024 : Standup at 10:30 : Retro\n    2025 : See https://example.com",
        "timeline\n    # launch history\n    2004 : Facebook",
        "timeline\n    Direction change : Board approves pivot",
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
        ("words after the keyword that are no way it runs", "timeline please\n    2002 : LinkedIn"),
        ("no header, as a timeline fence holds it", "2002 : LinkedIn\n2004 : Facebook"),
        ("a binding supplying periods", "timeline\n    2002 : LinkedIn\n    {{Periods}}\n"),
        // Half written.
        ("an event still to write", "timeline\n    2004 : "),
        ("a section still to name", "timeline\n    section "),
        ("the way it runs still to say", "timeline\n    direction "),
        ("a title with nothing after it yet", "timeline\n    title"),
        ("nothing but the keyword", "timeline"),
        // What nobody means to write.
        ("a way nobody knows", "timeline\n    direction sideways"),
        ("events with no period above them", "timeline\n    : Google"),
        ("an event that says nothing", "timeline\n    2004 : : Google"),
    ];

    /// <summary>
    /// The stages a timeline's language offers the engine (<c>Shipped.Timeline</c>), named again here — what the engine adds for
    /// every language, what a host binds and what runs of words are made of, is held where the engine reads a block.
    /// </summary>
    internal static IReadOnlyList<IAstStage> Offered(ContentNode tree, bool holes) =>
    [
        new ResolveEvents(),
        new WithConfig<TimelineConfig>(TimelineConfig.Read(MermaidBlock.Of(tree).Config)),
        .. holes ? [new WithHoles(TimelineParser.Holds)] : Array.Empty<IAstStage>(),
    ];

    /// <summary>A block parsed and worked over by its stages, holes and all where somebody is writing in it.</summary>
    internal static ContentNode Staged(string source, bool holes = false)
    {
        var tree = TimelineParser.Parse(source);
        return new AstPipeline(Offered(tree, holes)).Run(tree);
    }

    [TestMethod]
    public void TheDocumentedTimelinesLinesAreEachRead()
    {
        var tree = Staged(Social);

        Assert.AreEqual(4, Nodes(tree, TimelineKinds.Period).Count);
        Assert.AreEqual(5, Events(tree).Count);
        Assert.AreEqual(2, Nodes(Staged(Pizzas), TimelineKinds.Section).Count);
        Assert.AreEqual(1, Nodes(Staged(Pizzas), TimelineKinds.More).Count);
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
        Assert.AreEqual(1, Nodes(Staged("timeline TD\n    2002 : LinkedIn"), TimelineKinds.Direction).Count);
        Assert.AreEqual(0, Nodes(Staged("timeline\n    2002 : LinkedIn"), TimelineKinds.Direction).Count);
        Assert.AreEqual(0, Trouble("timeline TD\n    2002 : LinkedIn").Count);
    }

    [TestMethod]
    public void TheHeaderIsOptional_SinceATimelineFenceSaysWhatTheBlockIs()
    {
        var tree = Staged("2002 : LinkedIn\n2004 : Facebook");

        Assert.AreEqual(TimelineParser.Language, ((BlockNode)tree).Language);
        Assert.AreEqual(2, Nodes(tree, TimelineKinds.Period).Count, "the first line is a period, not a header gone wrong");
        Assert.AreEqual(0, tree.SelfAndDescendants().Count(node => node.Trouble is not null));
    }

    [TestMethod]
    public void EveryColonSplitsWhatFollowsIntoEvents()
    {
        var events = Events(Staged("timeline\n    2004 : Facebook : Google\n         : Orkut"));

        Assert.AreEqual(3, events.Count);
        CollectionAssert.AreEqual(new[] { "Facebook", "Google", "Orkut" }, events.Select(text => text.Print().Trim()).ToArray());
    }

    private static List<ContentNode> Nodes(ContentNode tree, string kind) => [.. tree.SelfAndDescendants().Where(node => node.Kind == kind)];

    private static List<ContentNode> Events(ContentNode tree) =>
        [.. tree.SelfAndDescendants().Where(node => node.Kind == TimelineKinds.Text && node.Role == TimelineRoles.Event)];

    private static List<string> Trouble(string source) =>
        [.. Staged(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>()];

    [TestMethod]
    public void AnEventRunsToTheNextColonWithSpaceAfterIt_SoATimeOrALinkStaysInIt()
    {
        var events = Staged("timeline\n    2024 : Standup at 10:30 : Retro\n    2025 : See https://example.com")
            .SelfAndDescendants().Where(node => node.Kind == TimelineKinds.Text && node.Role == TimelineRoles.Event).Select(node => node.Print()).ToList();

        CollectionAssert.AreEqual(new[] { "Standup at 10:30", "Retro", "See https://example.com" }, events);
    }
}
