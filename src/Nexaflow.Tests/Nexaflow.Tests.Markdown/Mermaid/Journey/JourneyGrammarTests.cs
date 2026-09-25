using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Journey;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Journey;

/// <summary>
/// What a <c>journey</c> block is read into: its sections, and its tasks with how each scored and who took part — and what
/// is written instead of any of that, held with the reason.
/// </summary>
[TestClass]
[CoversNode("journey-ast")]
public class JourneyGrammarTests : MermaidGrammarContract
{
    /// <summary>The journey the Mermaid documentation opens with.</summary>
    public const string Working =
        """
        journey
            title My working day
            section Go to work
              Make tea: 5: Me
              Go upstairs: 3: Me
              Do work: 1: Me, Cat
            section Go home
              Go downstairs: 5: Me
              Sit down: 5: Me
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.Journey;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Working,
        "journey\n    title Onboarding\n    section Sign up\n      Visit the site: 4: Visitor\n      Create an account: 2: Visitor, Support\n    section First run\n      Import data: 3: Visitor",
        "---\nconfig:\n  journey:\n    width: 200\n    actorColours:\n      - \"#ff0000\"\n      - \"#00ff00\"\n---\njourney\n    Make tea: 5: Me",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("a task with no actors", "journey\n    Sit down: 5"),
        ("a task with several actors", "journey\n    Do work: 1: Me, Cat, The dog"),
        ("no space after the commas", "journey\n    Do work: 1: Me,Cat"),
        ("no space anywhere", "journey\n    Do work:1:Me"),
        ("tasks before any section", "journey\n    Wake up: 3: Me\n    section Go to work\n      Make tea: 5: Me"),
        ("a comment and a blank line", "journey\n\n    %% the morning\n    Make tea: 5: Me %% first"),
        ("accessibility lines", "journey\n    accTitle: My day\n    accDescr: By task\n    Make tea: 5: Me"),
        ("written on Windows", "journey\r\n    section Go to work  \r\n      Make tea: 5: Me\r\n"),
        // Half written.
        ("a task still to score", "journey\n    Make tea: "),
        ("a task still to say who took part", "journey\n    Make tea: 5: "),
        ("another actor still to write", "journey\n    Make tea: 5: Me, "),
        ("a section still to name", "journey\n    section "),
        ("nothing but the keyword", "journey"),
        // What nobody means to write.
        ("a score nobody can reach", "journey\n    Make tea: 9: Me"),
        ("a score that is no number", "journey\n    Make tea: soon: Me"),
        ("a line that is no task", "journey\n    Make tea"),
    ];

    [TestMethod]
    public void TheDocumentedJourneysLinesAreEachRead()
    {
        var tree = MermaidStaged.Read(Working);

        Assert.AreEqual(2, Nodes(tree, JourneyKinds.Section).Count);
        Assert.AreEqual(5, Nodes(tree, JourneyKinds.Task).Count);
        Assert.AreEqual(6, Nodes(tree, MermaidKinds.Words).Count(words => words.Role == JourneyRoles.Actor), "Me five times and Cat once");
    }

    [TestMethod]
    public void WhatIsStillBeingWrittenIsNoComplaint()
    {
        foreach (var source in new[] { "journey\n    Make tea: ", "journey\n    Make tea: 5: ", "journey\n    Make tea: 5: Me, ", "journey\n    section ", "journey" })
            Assert.AreEqual(0, Trouble(source).Count, $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void WhatIsWrongIsSaid()
    {
        foreach (var (source, reason) in new[]
                 {
                     ("journey\n    Make tea: 9: Me", "scores from 1"),
                     ("journey\n    Make tea: soon: Me", "not a number"),
                     ("journey\n    Make tea", "A task is what is done"),
                 })
            Assert.IsTrue(Trouble(source).Any(said => said.Contains(reason, StringComparison.Ordinal)), $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void ANewLineUnderATaskIsAnotherTask_ScoredInTheMiddle()
    {
        var grammar = new JourneyGrammar();
        var task = Nodes(MermaidParser.Parse("journey\n    Make tea: 5: Me"), JourneyKinds.Task).Single();

        Assert.AreEqual((": 3", 0), grammar.Blank(task));
        Assert.IsNull(grammar.Blank(null));
    }

    [TestMethod]
    public void AnActorRenamedWhereTheyFirstTakePartIsRenamedInEveryTaskTheyAreIn()
    {
        var actors = new JourneyGrammar().Names(ContentReading.Of(MermaidStaged.Read(Working)).Root);

        Assert.AreEqual(2, actors.Count);
        Assert.AreEqual("Me", actors[0].Name);
        Assert.AreEqual(4, actors[0].Uses.Count, "the other four tasks Me takes part in");
        Assert.AreEqual(0, actors[1].Uses.Count, "Cat takes part in one");
    }

    [TestMethod]
    public void AColonTypedIntoWhatATaskSaysIsWrittenAsTheEntityCodeForIt()
    {
        const string source = "journey\n    Make tea: 5: Me";
        var says = ContentReading.Of(MermaidStaged.Read(source)).Root.SelfAndDescendants()
            .First(part => part.Kind == MermaidKinds.Words && part.Text == "Make tea");
        var writing = new JourneyGrammar().Escaping(says, says.End, ": and toast")!.Value;

        Assert.AreEqual("journey\n    Make tea#colon; and toast: 5: Me", source[..writing.Start] + writing.Text + source[writing.End..]);
    }

    [TestMethod]
    public void EachTaskSaysWhichSectionItIsIn()
    {
        var read = ContentReading.Of(MermaidStaged.Read(Working)).Root;

        CollectionAssert.AreEqual(
            new[] { "0", "0", "0", "1", "1" },
            read.SelfAndDescendants().Where(part => part.Kind == JourneyKinds.Task).Select(task => task.Fact(JourneyRoles.In)).ToArray());
    }

    private static List<ContentNode> Nodes(ContentNode tree, string kind) => [.. tree.SelfAndDescendants().Where(node => node.Kind == kind)];

    private static List<string> Trouble(string source) =>
        [.. MermaidStaged.Read(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>()];
}
