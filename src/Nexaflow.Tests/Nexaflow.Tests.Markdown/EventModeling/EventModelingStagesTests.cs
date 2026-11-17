using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.EventModeling;
using Nexaflow.Markdown.Settings;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.EventModeling;

/// <summary>
/// What an event model's stage says its frames come to together: the lane each is in and what feeds it, by Mermaid's rules — and what
/// is wrong said where it is written, the rest still drawn.
/// </summary>
[TestClass]
[CoversNode("eventmodeling-ast")]
public class EventModelingStagesTests
{
    [TestMethod]
    public void EachTypeHasItsGroupsLane_AndEachFrameWithANamespaceALaneOfItsOwn()
    {
        var block = Block("eventmodeling\ntf 01 ui A\ntf 02 cmd B\ntf 03 evt C\ntf 04 cmd Inventory.D\ntf 05 cmd Inventory.E\ntf 06 pcr F");

        CollectionAssert.AreEqual(
            new[] { "Automation:", "Commands:", "Commands:Inventory", "Commands:Inventory", "Events:" },
            block.Lanes.Select(lane => $"{lane.Group}:{lane.Namespace}").ToArray(),
            "every frame with a namespace a lane of its own, as Mermaid's drawing makes one");

        CollectionAssert.AreEqual(new[] { 0, 1, 4, 2, 3, 0 }, Frames(block).Select(frame => frame.Lane).ToArray());
        Assert.IsTrue(Frames(block)[3].Namespaced);
        Assert.IsFalse(Frames(block)[0].Namespaced);
    }

    [TestMethod]
    public void AFrameIsFedByTheNearestFrameAboveInAnotherLane_UnlessItIsAResetFrameOrTheFirst()
    {
        var frames = Frames(Block("eventmodeling\ntf 01 ui A\ntf 02 cmd B\ntf 03 evt C\nrf 04 evt D\ntf 05 pcr E\ntf 06 pcr F"));

        Assert.AreEqual(0, frames[0].Feeds.Count, "the first frame");
        Assert.AreEqual("0:", Feed(frames[1]));
        Assert.AreEqual("1:", Feed(frames[2]));
        Assert.AreEqual(0, frames[3].Feeds.Count, "a reset frame");
        Assert.AreEqual("3:", Feed(frames[4]));
        Assert.AreEqual("3:", Feed(frames[5]), "the nearest frame above in another lane, past one in its own");
    }

    [TestMethod]
    public void AFeedWrittenNamesAFrameAbove_AndTakesThePlaceOfTheOneInferred()
    {
        var frames = Frames(Block("eventmodeling\nrf 02 evt A\nrf 03 evt B\ntf 01 rmo C ->> 02 ->> 03"));

        CollectionAssert.AreEqual(new[] { "0:0", "1:1" }, frames[2].Feeds.Select(feed => $"{feed.From}:{feed.Written}").ToArray());
    }

    [TestMethod]
    public void WhatIsWrongIsSaidWhereItIsWritten()
    {
        foreach (var (source, reason) in new[]
                 {
                     ("eventmodeling\ntf 01 ui A\ntf 01 cmd B", "Frame 01 is numbered above already"),
                     ("eventmodeling\ntf 01 rmo A ->> 09", "No frame numbered 09"),
                     ("eventmodeling\ntf 01 ui A\ntf 02 rmo B ->> 01", "A read model is fed by an event, not by a ui"),
                     ("eventmodeling\ntf 01 evt A\ntf 02 cmd B ->> 01", "A command is fed by a ui or a processor, not by an event"),
                     ("eventmodeling\ntf 01 cmd A [[Missing]]", "No data block named Missing"),
                     ("eventmodeling\nnote 07 {\n  x\n}", "No frame numbered 07"),
                     ("eventmodeling\ntf 01 cmd A\ngwt 01 given evt Nothing then evt Nothing", "No entity named Nothing"),
                     ("---\nconfig:\n  eventmodeling:\n    padding: lots\n---\neventmodeling", "padding is a number"),
                     ("---\nconfig:\n  themeVariables:\n    emEventFill: orange\n---\neventmodeling", "emEventFill is a colour"),
                 })
            Assert.IsTrue(Trouble(source).Any(said => said.Contains(reason, StringComparison.Ordinal)), $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void AFrameShowsTheDataBlockItNames_AndANoteOrASpecificationIsAboutTheFrameItNames()
    {
        var tree = EventModelingParserTests.Staged(
            "eventmodeling\ntf 01 ui A\ntf 02 cmd B [[Two]]\ndata One {\n  a\n}\ndata Two {\n  b\n}\nnote 02 {\n  why\n}\nentity Added\ngwt 01 given evt Added then evt Added");

        Assert.AreEqual(1, Frames((EventModelingBlockNode)tree)[1].Shows, "the second data block written");
        CollectionAssert.AreEqual(new[] { 1, 0 }, tree.Children.OfType<EventModelingAboutNode>().Select(about => about.Frame).ToArray());
        Assert.AreEqual(0, tree.SelfAndDescendants().Count(node => node.Trouble is not null));
    }

    [TestMethod]
    public void TheFrontMatterAndADirectiveSetThePaddingAndTheThemesColours()
    {
        var block = Block("---\nconfig:\n  eventmodeling:\n    padding: 12\n  themeVariables:\n    emEventFill: '#ff0000'\n---\n%%{init: {'themeVariables': {'emCommandFill': '#00ff00'}}}%%\neventmodeling\ntf 01 evt A");

        Assert.AreEqual(12, block.Padding);
        Assert.AreEqual(new HexColor(0xFF, 0xFF, 0, 0), block.Theme["emEventFill"]);
        Assert.AreEqual(new HexColor(0xFF, 0, 0xFF, 0), block.Theme["emCommandFill"]);
    }

    private static EventModelingBlockNode Block(string source) => (EventModelingBlockNode)EventModelingParserTests.Staged(source);

    private static List<EventModelingFrameNode> Frames(EventModelingBlockNode block) => [.. block.Children.OfType<EventModelingFrameNode>()];

    private static string Feed(EventModelingFrameNode frame) => frame.Feeds.Single() is var feed ? $"{feed.From}:{feed.Written}" : "";

    private static List<string> Trouble(string source) =>
        [.. EventModelingParserTests.Staged(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>()];
}
