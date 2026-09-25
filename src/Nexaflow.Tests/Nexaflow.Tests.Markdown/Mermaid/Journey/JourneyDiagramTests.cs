using Nexaflow.Markdown.Mermaid.Journey;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Journey;

/// <summary>
/// A <c>journey</c> block read into what it describes: the sections grouping its tasks, each task's score and actors, the
/// actors in the order they first take part, and what the front matter asks for.
/// </summary>
[TestClass]
[CoversNode("journey-ast")]
public class JourneyDiagramTests
{
    [TestMethod]
    public void EachTaskReadsWhatIsDoneHowItScoredAndWhoTookPart()
    {
        var tasks = JourneyDiagram.Of(MermaidStaged.Read(JourneyGrammarTests.Working)).Tasks.ToList();

        Assert.AreEqual(5, tasks.Count);
        Assert.AreEqual("Make tea", tasks[0].Says.Says.Text);
        Assert.AreEqual(5, tasks[0].Score);
        CollectionAssert.AreEqual(new[] { "Me", "Cat" }, tasks[2].Actors.Select(actor => actor.Name).ToArray());
    }

    [TestMethod]
    public void TheActorsAreEveryoneWhoTakesPart_InTheOrderTheyFirstDo()
    {
        var diagram = JourneyDiagram.Of(MermaidStaged.Read("journey\n  A: 3: Cat\n  B: 3: Me, Cat\n  C: 3: Me"));

        CollectionAssert.AreEqual(new[] { "Cat", "Me" }, diagram.Actors.ToArray());
        CollectionAssert.AreEqual(new[] { 1, 0 }, diagram.Tasks.ElementAt(1).Actors.Select(actor => actor.Order).ToArray());
    }

    [TestMethod]
    public void AnActorIsWhoeverIsNamed_WhateverSpaceIsRoundTheName()
    {
        var diagram = JourneyDiagram.Of(MermaidStaged.Read("journey\n  A: 3: Me,Cat\n  B: 3:  Me ,  Cat "));

        CollectionAssert.AreEqual(new[] { "Me", "Cat" }, diagram.Actors.ToArray());
    }

    [TestMethod]
    public void SectionsGroupTheTasksWrittenUnderThem_AndThoseBeforeAnySectionAreAGroupWithNoName()
    {
        var diagram = JourneyDiagram.Of(MermaidStaged.Read("journey\n  Wake up: 3: Me\n  section Go to work\n    Make tea: 5: Me"));

        Assert.AreEqual(2, diagram.Sections.Count);
        Assert.IsNull(diagram.Sections[0].Name);
        Assert.AreEqual("Wake up", diagram.Sections[0].Tasks.Single().Says.Says.Text);
        Assert.AreEqual("Go to work", diagram.Sections[1].Name!.Says.Text);
        Assert.IsTrue(diagram.Sectioned);
    }

    [TestMethod]
    public void AFaceFollowsTheScore_AndATaskWithNoneSitsInTheMiddle()
    {
        var tasks = JourneyDiagram.Of(MermaidStaged.Read("journey\n  A: 5: Me\n  B: 3: Me\n  C: 1: Me\n  D: \n  E: 9: Me")).Tasks.ToList();

        Assert.AreEqual(JourneyMood.Happy, tasks[0].Mood);
        Assert.AreEqual(JourneyMood.Neutral, tasks[1].Mood);
        Assert.AreEqual(JourneyMood.Sad, tasks[2].Mood);

        Assert.IsNull(tasks[3].Score, "nothing is written to score it");
        Assert.AreEqual(JourneyMood.Neutral, tasks[3].Mood);
        Assert.IsNull(tasks[4].Score, "a score nobody can reach is no score at all");
        Assert.AreEqual(3, tasks[4].Height, "so it sits in the middle, and its line says what is wrong");
    }

    [TestMethod]
    public void TheFrontMattersSizesAndColourListsAreRead()
    {
        var config = JourneyDiagram.Of(MermaidStaged.Read("---\nconfig:\n  journey:\n    width: 200\n    height: 60\n    boxMargin: 4\n    taskFontSize: 14\n"
            + "    actorColours:\n      - \"#ff0000\"\n      - \"#00ff00\"\n    sectionFills: [\"#101010\"]\n"
            + "  themeVariables:\n    fillType1: \"#202020\"\n---\njourney\n  A: 3: Me")).Config;

        Assert.AreEqual(200, config.Width);
        Assert.AreEqual(60, config.Height);
        Assert.AreEqual(4, config.BoxMargin);
        Assert.AreEqual(14, config.TaskFontSize);
        Assert.AreEqual("#ff0000", config.ActorColour(0));
        Assert.IsNull(config.ActorColour(2), "no colour is written for a third actor");
        Assert.AreEqual("#101010", config.SectionFill(0), "the list is what a section is filled with");
        Assert.AreEqual("#202020", config.SectionFill(1), "and the theme's slot behind it");
    }

    [TestMethod]
    public void ABlockWithNoTasksHasNothingToDraw()
    {
        Assert.IsTrue(JourneyDiagram.Of(MermaidStaged.Read("journey")).Empty);
        Assert.IsTrue(JourneyDiagram.Of(MermaidStaged.Read("journey\n  section Go to work")).Empty);
        Assert.IsFalse(JourneyDiagram.Of(MermaidStaged.Read("journey\n  Make tea: 5: Me")).Empty);
    }
}
